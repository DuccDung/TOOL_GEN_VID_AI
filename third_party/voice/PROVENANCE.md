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

## Kokoro Vietnamese optional synthesis

- Repository: `https://huggingface.co/contextboxai/Kokoro-Vietnamese`
- Pinned revision: `9f210d622209fcc216fe2ac6159fed2ff381cb8a`
- Shared ONNX model: `kokoro_vi.onnx`, 325,731,953 bytes, SHA-256 `da191277f58633649a9c0d2ae8012e80ef57ea8e2a56e30323c0f7df1ca29087`.
- Shared config: `config.json`, 2,351 bytes, SHA-256 `5abb01e2403b072bf03d04fde160443e209d7a0dad49a423be15196b9b43c17f`.
- Voicepacks: the 14 pinned `voicepacks/*.pt` files and their individual exact sizes/SHA-256 are listed in `VietsubVoiceModelCatalog.cs`.
- Upstream model card declares Apache-2.0. Its card says the voicepacks derive from a LarVoice multi-speaker training set; the underlying data and voice rights have not been independently verified for public redistribution.

The ONNX model and config are shared across voicepacks. The modal distinguishes verified model files from `SynthesisReady`, which requires a per-voice WAV probe on the current machine. A selected Kokoro voice is stored in the local project manifest and the voice job snapshots that exact engine/model/voice. The worker loads only its pinned voicepack with `torch.load(weights_only=True)` in an isolated Python process; there is no fallback to Piper. A timeline from another voice is excluded from playback and export.

- Inference reference: `https://github.com/iamdinhthuan/Kokoro-Vietnamese`, Git commit `a249afe5555aec6c435165c2f61ec0f71284812f`, Apache-2.0. `TOOL-LOCAL/Vietsub/Voice/Workers/kokoro_worker.py` adapts the upstream phoneme/style/ONNX CPU inference and crossfade logic.
- Runtime: Python 3.11.15 x64 in a dedicated `uv` environment. `TOOL-LOCAL/SystemSetup/kokoro-requirements.in` pins ONNX Runtime 1.30.0, torch 2.14.0, vig2p 0.1.2 and numpy 2.4.6; `kokoro-requirements.lock` resolves transitive wheels with SHA-256 hashes. Installation happens only when the user requests a Kokoro voice and uses the already verified `uv` executable from the Piper component.
- Worker I/O: bounded JSONL; requests and WAVs stay local. Worker stderr is drained but never exposed to the UI. A probe checks 24 kHz mono PCM WAV before the selected voice can start a job.

This repository now contains the Kokoro synthesis source. Model/runtime verification, CPU benchmark, listening review, clean-machine desktop smoke and public redistribution remain **unverified** on this checkout. The upstream Apache-2.0 model card does not settle LarVoice-derived voice/data rights; release review is still required.

## Bundled voice preview samples

- Location: `TOOL-LOCAL/Web/public/voice-previews/`; `manifest.json` records the voice ID, pinned model revision, duration, sample rate and SHA-256 of each WAV.
- Sentence used for all 15 clips: “Xin chào, đây là giọng đọc tiếng Việt để bạn nghe thử.”
- Format: mono PCM 16-bit WAV. The 14 Kokoro clips use 24 kHz; the Piper clip uses 22.05 kHz. All clips were peak-scaled to 0.8 after synthesis and checked for valid RIFF/PCM, duration, audibility and unique SHA-256.
- Kokoro clips were synthesized offline with the pinned ONNX model/config and each pinned voicepack above, using the upstream `Kokoro-Vietnamese` inference code at Git commit `a249afe5555aec6c435165c2f61ec0f71284812f`, ONNX Runtime 1.30.0, torch 2.14.0 and vig2p 0.1.2. Voicepacks were loaded with `torch.load(weights_only=True)` only in this isolated sample-preparation environment.
- The Piper clip was synthesized offline with pinned VAIS-1000 ONNX/config above and `piper-tts` 1.6.0. The preparation environment and model copies were kept outside application source/runtime; the modal only plays the resulting bundled WAV files.

These clips are source assets for preview before installation, not evidence that Kokoro inference has been probed on a user's machine. The upstream Kokoro model card declares Apache-2.0 but identifies LarVoice-derived voicepacks; voice/data rights for public redistribution of the generated clips remain to be independently reviewed before publishing a release containing them. Piper attribution remains VAIS-1000 CC BY 4.0 as recorded above.

## Resource and rollout policy

Piper has no fixed high-RAM admission gate. VideoMaker serializes local AI jobs, unloads the translation worker after its request, and starts Piper only in its own process. The Vietsub local voice UI/install flag defaults on, while a machine without verified Piper components reports `NOT_INSTALLED`; a source flag alone does not establish runtime or rollout readiness. VieNeu and Cloud TTS are not part of this component.

Before public redistribution, release engineering must include the complete applicable upstream license texts and review the resolved Python dependency inventory produced for the release candidate.
