import contextlib
import json
import pathlib
import sys
import wave

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
        from piper import PiperVoice, SynthesisConfig

        model_path = pathlib.Path(request["modelPath"]).resolve(strict=True)
        config_path = pathlib.Path(request["configPath"]).resolve(strict=True)
        voice = PiperVoice.load(str(model_path), config_path=str(config_path), use_cuda=False)
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
