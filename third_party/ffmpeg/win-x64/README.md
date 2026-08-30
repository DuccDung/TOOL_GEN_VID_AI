# FFmpeg bundle for VideoMaker

Release packaging expects an approved Windows x64 FFmpeg distribution in this directory:

- `ffmpeg.exe`
- `ffprobe.exe`
- `LICENSE.txt`
- `PROVENANCE.md`
- `checksums.sha256`

The binaries are intentionally not committed without an approved source, build configuration and license review. Do not manually create the two profile files. Prepare the directory from an approved source bundle:

```powershell
.\scripts\Prepare-FfmpegBundle.ps1 `
  -SourceDirectory C:\deploy\ffmpeg-source\win-x64 `
  -ExpectedVersion <exact-version-token> `
  -Source <source-URL-or-internal-artifact-id> `
  -ApprovedBy <approver> `
  -LicenseReview <review-record-id> `
  -ApprovalScope Development
```

`ExpectedVersion` is the version token printed after `ffmpeg version` and must match the token returned by `ffprobe -version`. `scripts/Publish-DesktopRelease.ps1` fails before publish when a file is missing, a checksum differs, an executable cannot run, or the versions differ. The resulting desktop package places all five files under `tools/ffmpeg/`; installer and updater validate the same checksum profile before replacing application files.

Current local development profile:

- Version: `9.0.1-essentials_build-www.gyan.dev`
- Distribution: Gyan release essentials, Windows x64 static
- License declared by the distribution: GPLv3
- Approval scope: `Development`

This profile is suitable for local development because VideoMaker currently requires `libx264`. It is intentionally blocked from release packaging until the product owner records a separate redistribution/license approval and recreates the profile with `-ApprovalScope Release`.
