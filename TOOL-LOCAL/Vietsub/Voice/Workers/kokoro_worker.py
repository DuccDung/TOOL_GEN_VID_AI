"""Isolated CPU ONNX inference for pinned Kokoro Vietnamese resources.

The phoneme, style and crossfade logic follows Kokoro-Vietnamese at
a249afe5555aec6c435165c2f61ec0f71284812f (Apache-2.0).
"""

import contextlib
import importlib.metadata
import io
import json
import pathlib
import re
import sys
import wave

PROTOCOL_VERSION = 1
SAMPLE_RATE = 24000
sys.stdout.reconfigure(encoding="utf-8")
sys.stderr.reconfigure(encoding="utf-8")
PROTOCOL_STDOUT = sys.stdout


def emit(request_id: str, event_type: str, **values) -> None:
    PROTOCOL_STDOUT.write(json.dumps({
        "protocolVersion": PROTOCOL_VERSION,
        "requestId": request_id,
        "type": event_type,
        **values,
    }, ensure_ascii=False) + "\n")
    PROTOCOL_STDOUT.flush()


def check_runtime() -> None:
    if sys.version_info[:3] != (3, 11, 15) or sys.maxsize <= 2**32:
        raise RuntimeError("unsupported_python")
    lock = pathlib.Path(__file__).with_name("kokoro-requirements.lock")
    requirements = re.findall(
        r"^([A-Za-z0-9_-]+)==([^\s]+)", lock.read_text(encoding="utf-8"), re.MULTILINE)
    if not requirements:
        raise RuntimeError("missing_runtime_lock")
    for name, version in requirements:
        if importlib.metadata.version(name) != version:
            raise RuntimeError("runtime_lock_mismatch")


def chunks_for(text: str):
    normalized = re.sub(r"\s+", " ", text.strip())
    if not normalized:
        return
    words = normalized.split(" ")
    current = []
    length = 0
    for word in words:
        added = len(word) + (1 if current else 0)
        if current and length + added > 180:
            yield " ".join(current)
            current = []
            length = 0
        current.append(word)
        length += added
    if current:
        yield " ".join(current)


def infer_text(text: str, session, voicepack, config):
    import numpy as np
    from vig2p import phonemize_text

    vocab = config["vocab"]
    context_length = int(config["plbert"]["max_position_embeddings"])
    audio = []
    for chunk in chunks_for(text):
        phonemes = phonemize_text(chunk)
        if not phonemes:
            continue
        ids = [vocab[p] for p in phonemes if p in vocab]
        if not ids or len(ids) + 2 > context_length:
            raise RuntimeError("phoneme_limit")
        style = voicepack[min(len(phonemes), voicepack.shape[0]) - 1]
        waveform, _duration = session.run(None, {
            "input_ids": np.asarray([[0, *ids, 0]], dtype=np.int64),
            "ref_s": np.asarray(style, dtype=np.float32),
            "speed": np.asarray(1.0, dtype=np.float32),
        })
        samples = np.asarray(waveform, dtype=np.float32).reshape(-1)
        if samples.size:
            audio.append(samples)
    if not audio:
        raise RuntimeError("no_audio")
    merged = audio[0]
    for part in audio[1:]:
        overlap = min(1200, len(merged), len(part))
        if overlap:
            fade = np.linspace(1.0, 0.0, overlap + 2, dtype=np.float32)[1:-1]
            blend = merged[-overlap:] * fade + part[:overlap] * (1.0 - fade)
            merged = np.concatenate((merged[:-overlap], blend, part[overlap:]))
        else:
            merged = np.concatenate((merged, part))
    if not np.all(np.isfinite(merged)):
        raise RuntimeError("invalid_audio")
    peak = float(np.max(np.abs(merged)))
    if peak > 0.95:
        merged = merged * (0.95 / peak)
    return np.round(np.clip(merged, -1.0, 1.0) * 32767).astype("<i2")


def main() -> None:
    check_runtime()
    if len(sys.argv) != 2:
        raise RuntimeError("invalid_arguments")
    with open(sys.argv[1], encoding="utf-8") as request_file:
        request = json.load(request_file)
    request_id = str(request["requestId"])
    if request.get("protocolVersion") != PROTOCOL_VERSION:
        raise RuntimeError("invalid_protocol")
    items = request.get("items")
    if not isinstance(items, list) or len(items) > 2000:
        raise RuntimeError("invalid_items")
    emit(request_id, "started", total=len(items))

    # Third-party import/inference output must not corrupt JSONL or leak cue text.
    with contextlib.redirect_stdout(io.StringIO()):
        import numpy as np
        import onnxruntime as ort
        import torch

        onnx_path = pathlib.Path(request["modelPath"]).resolve(strict=True)
        config_path = pathlib.Path(request["configPath"]).resolve(strict=True)
        voicepack_path = pathlib.Path(request["voicepackPath"]).resolve(strict=True)
        with config_path.open(encoding="utf-8") as config_file:
            config = json.load(config_file)
        voicepack = torch.load(voicepack_path, map_location="cpu", weights_only=True)
        if hasattr(voicepack, "detach"):
            voicepack = voicepack.detach().cpu().numpy()
        voicepack = np.asarray(voicepack, dtype=np.float32)
        if voicepack.ndim != 3 or voicepack.shape[1:] != (1, 256):
            raise RuntimeError("invalid_voicepack")
        session = ort.InferenceSession(str(onnx_path), providers=["CPUExecutionProvider"])
        emit(request_id, "model_loaded", total=len(items))
        for fallback_index, item in enumerate(items):
            index = int(item.get("index", fallback_index))
            text = str(item["text"])
            if not text or len(text) > 4500:
                raise RuntimeError("invalid_text")
            output_path = pathlib.Path(item["outputPath"]).resolve()
            output_path.parent.mkdir(parents=True, exist_ok=True)
            emit(request_id, "item_started", index=index, total=len(items))
            samples = infer_text(text, session, voicepack, config)
            with wave.open(str(output_path), "wb") as wav:
                wav.setnchannels(1)
                wav.setsampwidth(2)
                wav.setframerate(SAMPLE_RATE)
                wav.writeframes(samples.tobytes())
            emit(request_id, "item_completed", index=index, written=fallback_index + 1, total=len(items))
    emit(request_id, "completed", written=len(items), total=len(items))


if __name__ == "__main__":
    try:
        main()
    except Exception:
        # Sensitive cue text and paths must never reach the host error stream.
        sys.stderr.write("kokoro_worker_failed\n")
        sys.exit(1)
