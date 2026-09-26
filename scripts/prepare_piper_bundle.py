"""Build the reviewed Windows Piper payload. Requires Python on the BUILD machine only."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import tarfile
import urllib.request
import zipfile

REPO = Path(__file__).resolve().parent.parent
VERSION = "piper-1.6.0-python-3.11.15-offline-v3"
LOCK = REPO / "TOOL-LOCAL/SystemSetup/piper-requirements.lock"
WORKER = REPO / "TOOL-LOCAL/Vietsub/Voice/Workers/piper_worker.py"
SOURCES = {
    "vc_redist.x64.exe": (
        "https://download.visualstudio.microsoft.com/download/pr/6f02464a-5e9b-486d-a506-c99a17db9a83/8995548DFFFCDE7C49987029C764355612BA6850EE09A7B6F0FDDC85BDC5C280/VC_redist.x64.exe",
        "8995548dfffcde7c49987029c764355612ba6850ee09a7b6f0fddc85bdc5c280"),
    "uv.zip": (
        "https://releases.astral.sh/github/uv/releases/download/0.12.3/uv-x86_64-pc-windows-msvc.zip",
        "b23350c79e8ad0192b8124af13a0f17e8d4e4549524785e1aef389ae5a06990e"),
    "python.tar.gz": (
        "https://releases.astral.sh/github/python-build-standalone/releases/download/20260807/cpython-3.11.15%2B20260807-x86_64-pc-windows-msvc-install_only_stripped.tar.gz",
        "ebfd13f290b79fc0cd874304e80944d81a368edc2c7cd94994a7d5104cc0c6b3"),
    "vi_VN-vais1000-medium.onnx": (
        "https://huggingface.co/rhasspy/piper-voices/resolve/ea046e8458f6acd997706d6e6066a022b42f6fb1/vi/vi_VN/vais1000/medium/vi_VN-vais1000-medium.onnx?download=true",
        "ec7c89e2c85f4d1edc24b6120c18aaf1bda614f06b511567eb9c7c0de15e2dab"),
    "vi_VN-vais1000-medium.onnx.json": (
        "https://huggingface.co/rhasspy/piper-voices/resolve/ea046e8458f6acd997706d6e6066a022b42f6fb1/vi/vi_VN/vais1000/medium/vi_VN-vais1000-medium.onnx.json?download=true",
        "fafb9da1354ed4b77c31af228ed41fb41cd825c14cffa105454b25e6ae751ee0"),
}
MSVC_FILES = {
    "msvcp140.dll": "def46aa6a8f72f27bafac0c43334419486a4d1dcdb6c479a8ef7034b3e1fa4cb",
    "msvcp140_1.dll": "2dd670f874562fbdca5b022df1943d70a57ba91fde559280e3a1daebe4db2380",
    "vcruntime140.dll": "184146852727a9db4eea06178716bec3cdbb1015c911f6b0f915b184ad7775b2",
    "vcruntime140_1.dll": "e6bfb3662ab4b1969a73441dbe35c96d51441b6bff8cf1fe7430bd5b246ca605",
}


def extract_msvc(installer, output, python_root):
    # Offsets refer ONLY to the SHA-256-pinned, Microsoft-signed 14.50.35719.0
    # installer above. Extract CAB payloads; never run the installer or MSI actions.
    cab = output / "msvc-attached.cab"
    cab.write_bytes(installer.read_bytes()[630176:630176 + 17918388])
    attached, minimum = output / "msvc-attached", output / "msvc-minimum"
    attached.mkdir()
    minimum.mkdir()
    expand = str(Path(os.environ["SystemRoot"]) / "System32/expand.exe")
    for source, target in ((cab, attached), (attached / "a4", minimum)):
        result = subprocess.run([expand, "-F:*", str(source), str(target)], capture_output=True, timeout=60)
        if result.returncode:
            raise ValueError("Could not extract the pinned Microsoft runtime CAB.")
    for name, expected in MSVC_FILES.items():
        source = minimum / (name + "_amd64")
        if digest(source) != expected:
            raise ValueError("Microsoft runtime file checksum mismatch: " + name)
        shutil.copyfile(source, python_root / name)


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def json_write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def safe_name(name):
    if (not name or "\\" in name or ":" in name or name.startswith("/")
            or any(p in ("", ".", "..") or p.endswith((".", " ")) for p in name.split("/"))):
        raise ValueError(f"Unsafe archive entry: {name}")
    return name


def obtain(path, url, expected, download):
    if not path.exists():
        if not download:
            raise FileNotFoundError(f"Missing approved input: {path.name}; use --download or provide verified inputs.")
        path.parent.mkdir(parents=True, exist_ok=True)
        partial = path.with_name(path.name + ".part")
        try:
            with urllib.request.urlopen(url, timeout=90) as response, partial.open("wb") as output:
                if not response.geturl().startswith("https://"):
                    raise ValueError("A source redirected outside HTTPS.")
                shutil.copyfileobj(response, output)
            if digest(partial) != expected:
                raise ValueError(f"Checksum mismatch: {path.name}")
            partial.replace(path)
        finally:
            partial.unlink(missing_ok=True)
    if path.is_symlink() or digest(path) != expected:
        raise ValueError(f"Invalid approved input: {path.name}")
    return path


def wheel_inputs(cache, download):
    selected = []
    blocks = re.split(r"(?m)^(?=[A-Za-z0-9_-]+==)", LOCK.read_text(encoding="utf-8"))
    for block in blocks:
        match = re.match(r"([A-Za-z0-9_-]+)==([^\s]+)", block)
        if not match:
            continue
        name, version = match.groups()
        allowed = set(re.findall(r"--hash=sha256:([a-f0-9]{64})", block))
        cached = [p for p in (cache / "wheels").glob("*.whl")
                  if p.name.lower().startswith(name.lower().replace("-", "_") + "-" + version + "-")
                  and digest(p) in allowed]
        if not cached and download:
            with urllib.request.urlopen(f"https://pypi.org/pypi/{name}/{version}/json", timeout=30) as response:
                records = json.load(response)["urls"]
            for record in records:
                filename = record["filename"]
                if not filename.endswith(".whl") or record["digests"]["sha256"] not in allowed:
                    continue
                tags = filename.removesuffix(".whl").rsplit("-", 3)[-3:]
                py, abi, platform = tags
                compatible = (platform == "any" and "py3" in py) or (platform == "win_amd64" and
                    (py == "cp311" or (abi == "abi3" and py.startswith("cp") and int(py[2:]) <= 311)))
                if compatible:
                    cached.append(obtain(cache / "wheels" / safe_name(filename), record["url"],
                                         record["digests"]["sha256"], True))
        if len(cached) != 1:
            raise ValueError(f"Expected one locked Windows/CPython 3.11 wheel for {name}=={version}.")
        selected.append(cached[0])
    if not selected:
        raise ValueError("No locked dependencies found.")
    return selected


def run(args, cwd, env, log):
    result = subprocess.run([str(x) for x in args], cwd=cwd, env=env, capture_output=True, timeout=600)
    log.write_bytes(result.stdout + result.stderr)
    if result.returncode:
        raise RuntimeError(f"Preparation process failed; inspect {log.name}.")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input-dir", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--download", action="store_true")
    args = parser.parse_args()
    cache, output = args.input_dir.resolve(), args.output_dir.resolve()
    if output.exists():
        raise ValueError("Output must be a new directory; previous evidence is preserved.")
    cache.mkdir(parents=True, exist_ok=True)
    inputs = {name: obtain(cache / name, *source, args.download) for name, source in SOURCES.items()}
    wheels = wheel_inputs(cache, args.download)
    output.mkdir(parents=True)
    payload, proof = output / "payload", output / "proof"
    payload.mkdir()
    proof.mkdir()
    seen = set()
    with tarfile.open(inputs["python.tar.gz"], "r:gz") as archive:
        for member in archive:
            if member.isdir():
                continue
            name = safe_name(member.name)
            if not member.isfile() or not name.startswith("python/") or name.casefold() in seen:
                raise ValueError("Unsupported Python archive entry.")
            seen.add(name.casefold())
            target = payload / name
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.extractfile(member) as source, target.open("wb") as destination:
                shutil.copyfileobj(source, destination)
    with zipfile.ZipFile(inputs["uv.zip"]) as archive:
        uv = [x for x in archive.infolist() if PurePosixPath(x.filename).name == "uv.exe"]
        if len(uv) != 1:
            raise ValueError("uv executable missing or duplicated.")
        (payload / "uv.exe").write_bytes(archive.read(uv[0]))
    if digest(payload / "uv.exe") != "68a22cbab1674647bcda32120b214e6480f875414e3333f49f87ae99b4b0e0fa":
        raise ValueError("uv executable checksum mismatch.")
    extract_msvc(inputs["vc_redist.x64.exe"], output, payload / "python")
    (payload / "wheels").mkdir()
    for wheel in wheels:
        shutil.copyfile(wheel, payload / "wheels" / wheel.name)
    (payload / "model").mkdir()
    for name in ("vi_VN-vais1000-medium.onnx", "vi_VN-vais1000-medium.onnx.json"):
        shutil.copyfile(inputs[name], payload / "model" / name)
    # Full wheel license texts and CPython's LICENSE remain in the payload.
    (payload / "notices").mkdir()
    for name in ("LICENSES.md", "PROVENANCE.md", "MSVC-NOTICE.md"):
        shutil.copyfile(REPO / "third_party/voice" / name, payload / "notices" / name)
    for name, expected in {
        "LICENSE-APACHE": "c71d239df91726fc519c6eb72d318ec65820627232b2f796219e87dcf35d0ab4",
        "LICENSE-MIT": "860e3d7a86b84e6a7012c7a635fc64df475cebc6cce34dfeb73a5982ec58176c",
    }.items():
        source = obtain(cache / name, f"https://raw.githubusercontent.com/astral-sh/uv/0.12.3/{name}", expected, args.download)
        shutil.copyfile(source, payload / "notices" / ("uv-" + name + ".txt"))
    env = {k: v for k, v in os.environ.items() if not k.upper().startswith(("UV_", "PIP_", "PYTHON"))}
    env.update(UV_OFFLINE="1", UV_PYTHON_DOWNLOADS="never", UV_CACHE_DIR=str(proof / "cache"),
               UV_PYTHON_NO_REGISTRY="1", UV_NO_CONFIG="1")
    python = proof / ".venv/Scripts/python.exe"
    uv = payload / "uv.exe"
    run([uv, "venv", "--python", payload / "python/python.exe", "--no-python-downloads", "--offline",
         "--no-config", proof / ".venv"], proof, env, proof / "venv.log")
    run([uv, "pip", "install", "--python", python, "--no-index", "--find-links", payload / "wheels",
         "--only-binary", ":all:", "--require-hashes", "--offline", "--no-python-downloads", "--no-config",
         "--link-mode", "copy", "--requirements", LOCK], proof, env, proof / "install.log")
    request = {"protocolVersion": 1, "requestId": "bundle-proof", "modelPath": str(payload / "model/vi_VN-vais1000-medium.onnx"),
               "configPath": str(payload / "model/vi_VN-vais1000-medium.onnx.json"), "items": [
                   {"index": 0, "text": "Xin chào, đây là kiểm tra giọng Việt ngoại tuyến.", "outputPath": str(proof / "probe.wav")}]}
    json_write(proof / "request.json", request)
    # Worker reads its lock adjacent to itself; use a copy of SOURCE, never a prior build.
    shutil.copyfile(WORKER, proof / "piper_worker.py")
    shutil.copyfile(LOCK, proof / "piper-requirements.lock")
    run([python, "-I", "-B", "-X", "utf8", proof / "piper_worker.py", proof / "request.json"],
        proof, env, proof / "probe.log")
    import wave
    with wave.open(str(proof / "probe.wav")) as wav:
        if wav.getnframes() == 0 or wav.getnchannels() != 1 or wav.getsampwidth() != 2:
            raise ValueError("Probe WAV failed.")

    def entry(path, root):
        return {"path": path.relative_to(root).as_posix(), "size": path.stat().st_size, "sha256": digest(path)}

    files = [entry(p, payload) for p in sorted(payload.rglob("*")) if p.is_file()
             and "__pycache__" not in p.parts and p.suffix not in (".pyc", ".pyo")]
    # Immutable installed modules, data and native DLLs. RECORD and entry-point launchers
    # contain target-specific paths; they are not loaded by the application's worker.
    site = proof / ".venv/Lib/site-packages"
    installed = [entry(p, proof) for p in sorted(site.rglob("*")) if p.is_file()
                 and p.name not in ("RECORD", "direct_url.json", "uv_cache.json") and "__pycache__" not in p.parts]
    installed.append(entry(python, proof))
    installed.append(entry(proof / ".venv/Scripts/activate_this.py", proof))
    manifest = {"schemaVersion": 1, "bundleVersion": VERSION, "files": files, "installedFiles": installed}
    json_write(output / "manifest.json", manifest)
    archive_path = output / "piper-offline.zip"
    with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for item in files:
            info = zipfile.ZipInfo(item["path"], date_time=(2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            with (payload / item["path"]).open("rb") as source, archive.open(info, "w", force_zip64=True) as target:
                shutil.copyfileobj(source, target)
    expanded = sum(x["size"] for x in files)
    installed_bytes = sum(x["size"] for x in installed)
    definition = {"schemaVersion": 1, "bundleVersion": VERSION, "platform": "win-x64", "protocolVersion": 1,
                  "archiveSize": archive_path.stat().st_size, "archiveSha256": digest(archive_path),
                  "manifestSize": (output / "manifest.json").stat().st_size, "manifestSha256": digest(output / "manifest.json"),
                  "expandedBytes": expanded, "minimumFreeDiskBytes": expanded * 2 + installed_bytes + 128 * 1024 * 1024,
                  "workerSha256": digest(WORKER), "requirementsSha256": digest(LOCK)}
    json_write(output / "approved-definition.json", definition)
    json_write(output / "sources.json", {"sources": [{"file": n, "url": u, "sha256": h} for n, (u, h) in SOURCES.items()],
                                        "wheels": [entry(p, cache) for p in wheels]})
    print(json.dumps(definition, ensure_ascii=False))


if __name__ == "__main__":
    main()
