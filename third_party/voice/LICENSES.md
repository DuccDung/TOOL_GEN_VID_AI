# License notice — Piper Vietnamese voice component

- Piper runtime: GPL-3.0; see the upstream project identified in `PROVENANCE.md`.
- VAIS-1000 Vietnamese voice data: CC BY 4.0; attribution and pinned source are recorded in `PROVENANCE.md`.
- uv installer: Apache-2.0 OR MIT; pinned archive checksum is recorded in `checksums.sha256`.
- CPython and bundled dependencies: retain the complete upstream license/notice files included in the approved Python distribution and Windows wheels. Offline payload `notices/` also includes the complete uv Apache-2.0 and MIT texts.
- Microsoft Visual C++ runtime: Microsoft license, with source identity and redistribution review requirements in `MSVC-NOTICE.md`; not covered by Piper's GPL or Python's license.
- Optional Kokoro Vietnamese ONNX/voicepack resources and adapted CPU inference code: the upstream model card and inference repository declare Apache-2.0; see the pinned sources and unresolved voicepack training-data rights in `PROVENANCE.md` before any public redistribution.
- Bundled preview WAVs: generated from the pinned Piper/Kokoro models, with per-file checksums and source details in `PROVENANCE.md` and `TOOL-LOCAL/Web/public/voice-previews/manifest.json`. The Piper sample carries VAIS-1000 attribution; the Kokoro voice/data rights review also applies to the generated samples before public redistribution.

This repository records the component identity and attribution here. The release checklist must attach the complete license texts from the reviewed upstream revisions before distribution.
