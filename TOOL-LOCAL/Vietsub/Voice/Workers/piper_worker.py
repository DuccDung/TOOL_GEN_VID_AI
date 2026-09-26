import contextlib
import json
import pathlib
import sys
import wave
import importlib.metadata
import re
import os

PROTOCOL_VERSION = 1
sys.stdout.reconfigure(encoding="utf-8")
sys.stderr.reconfigure(encoding="utf-8")
PROTOCOL_STDOUT = sys.stdout


def emit(request_id: str, event_type: str, **values) -> None:
    event = {
        "protocolVersion": PROTOCOL_VERSION,
        "requestId": request_id,
        "type": event_type,
        **values,
    }
    PROTOCOL_STDOUT.write(json.dumps(event, ensure_ascii=False) + "\n")
    PROTOCOL_STDOUT.flush()


def main() -> None:
    if sys.version_info[:3] != (3, 11, 15) or sys.maxsize <= 2**32:
        raise ValueError("Pinned CPython 3.11.15 x64 is required.")
    lock_path = pathlib.Path(__file__).with_name("piper-requirements.lock")
    requirements = re.findall(r"^([A-Za-z0-9_-]+)==([^\s]+)", lock_path.read_text(encoding="utf-8"), re.MULTILINE)
    if not requirements:
        raise ValueError("Runtime dependency lock is missing.")
    for name, version in requirements:
        if importlib.metadata.version(name) != version:
            raise ValueError("Runtime dependency does not match the bundled lock.")
    if len(sys.argv) != 2:
        raise ValueError("A single request file is required.")
    with open(sys.argv[1], "r", encoding="utf-8") as request_file:
        request = json.load(request_file)
    request_id = str(request["requestId"])
    if int(request.get("protocolVersion", -1)) != PROTOCOL_VERSION:
        raise ValueError("Unsupported protocol version.")
    items = request.get("items", [])
    emit(request_id, "started", total=len(items))

    with contextlib.redirect_stdout(sys.stderr):
        # The offline bundle supplies the C++ runtime needed by ONNX Runtime.
        # Load absolute local paths and verify Windows did not reuse a system DLL.
        msvc_libraries = []
        if os.name == "nt" and (pathlib.Path(sys.base_prefix) / "msvcp140.dll").is_file():
            import ctypes
            get_module_path = ctypes.windll.kernel32.GetModuleFileNameW
            get_module_path.argtypes = [ctypes.c_void_p, ctypes.c_wchar_p, ctypes.c_uint]
            get_module_path.restype = ctypes.c_uint
            for name in ("vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "msvcp140_1.dll"):
                expected_path = (pathlib.Path(sys.base_prefix) / name).resolve(strict=True)
                library = ctypes.WinDLL(str(expected_path))
                loaded_path = ctypes.create_unicode_buffer(32768)
                if not get_module_path(library._handle, loaded_path, len(loaded_path)):
                    raise ValueError("Cannot verify the local C++ runtime.")
                if pathlib.Path(loaded_path.value).resolve() != expected_path:
                    raise ValueError("C++ runtime did not load from the approved local bundle.")
                msvc_libraries.append(library)
        from piper import PiperVoice, SynthesisConfig

        model_path = pathlib.Path(request["modelPath"]).resolve(strict=True)
        config_path = pathlib.Path(request["configPath"]).resolve(strict=True)
        # eSpeak's Windows C file API cannot open a UTF-8 absolute data path.
        # Windows resolves an ASCII relative path against its Unicode working directory.
        # Resolve request paths first; this worker owns its process and working directory.
        for item in items:
            item["outputPath"] = str(pathlib.Path(item["outputPath"]).resolve())
        from piper.phonemize_espeak import ESPEAK_DATA_DIR
        espeak_data_dir = ESPEAK_DATA_DIR
        if os.name == "nt":
            os.chdir(ESPEAK_DATA_DIR.parent)
            espeak_data_dir = pathlib.Path("espeak-ng-data")
        voice = PiperVoice.load(str(model_path), config_path=str(config_path),
                                use_cuda=False, espeak_data_dir=espeak_data_dir)
        synthesis_config = SynthesisConfig(
            volume=float(request.get("volume", 1.0)),
            length_scale=float(request.get("lengthScale", 1.0)),
            normalize_audio=True,
        )
        emit(request_id, "model_loaded", total=len(items))
        for fallback_index, item in enumerate(items):
            index = int(item.get("index", fallback_index))
            output_path = pathlib.Path(item["outputPath"]).resolve()
            output_path.parent.mkdir(parents=True, exist_ok=True)
            emit(request_id, "item_started", index=index, total=len(items))
            with wave.open(str(output_path), "wb") as wave_file:
                voice.synthesize_wav(str(item["text"]), wave_file, syn_config=synthesis_config)
            emit(request_id, "item_completed", index=index, written=index + 1, total=len(items))

    emit(request_id, "completed", written=len(items), total=len(items))


if __name__ == "__main__":
    main()
