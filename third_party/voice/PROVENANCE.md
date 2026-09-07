# Provenance — Vietsub local voice

## Piper runtime

- Package: `piper-tts` `1.6.0`
- Installation: isolated Python 3.11 x64 environment created by `uv`.
- Package cutoff: `--exclude-newer 2026-08-14`.
- Upstream project: `https://github.com/OHF-Voice/piper1-gpl`
- License declared by the approved source integration: GPL-3.0.

`piper-tts` is installed only after the user explicitly requests the optional local voice component. It is not loaded into the WinForms process. The packaged Python worker communicates through bounded JSONL and has no Cloud client, credential or workflow database access.

## Vietnamese voice model

- Repository: `https://huggingface.co/rhasspy/piper-voices`
- Pinned revision: `ea046e8458f6acd997706d6e6066a022b42f6fb1`
- Model: `vi_VN-vais1000-medium.onnx`
- Model SHA-256: `ec7c89e2c85f4d1edc24b6120c18aaf1bda614f06b511567eb9c7c0de15e2dab`
- Config SHA-256: `fafb9da1354ed4b77c31af228ed41fb41cd825c14cffa105454b25e6ae751ee0`
- Voice dataset: VAIS-1000.
- License declared by the upstream voice metadata: CC BY 4.0.

The model and config are downloaded through an HTTPS host allowlist with each redirect checked, written to `.partial`, checked for exact size and SHA-256, and atomically published. A `READY` marker is accepted only when protocol, runtime, worker, model and config fingerprints match and an x64 worker probe has loaded the pinned model and produced a valid Vietnamese PCM WAV.

## Resource and rollout policy

Piper has no fixed high-RAM admission gate. VideoMaker serializes local AI jobs, unloads the translation worker after its request, and starts Piper only in its own process. The feature remains disabled by default until a real-model integration run and desktop smoke test have completed. VieNeu and Cloud TTS are not part of this component.

Before public redistribution, release engineering must include the complete applicable upstream license texts and review the resolved Python dependency inventory produced for the release candidate.
