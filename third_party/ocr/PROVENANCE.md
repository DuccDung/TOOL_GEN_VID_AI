# OCR local provenance

VideoMaker uses the following pinned NuGet packages for local OCR on Windows x64:

| Package | Version | Declared license | Upstream |
|---|---:|---|---|
| `Sdcb.PaddleOCR` | `3.3.1` | Apache-2.0 | https://github.com/sdcb/PaddleSharp |
| `Sdcb.PaddleOCR.Models.Local` | `3.3.1` | Apache-2.0 | https://github.com/sdcb/PaddleSharp |
| `Sdcb.PaddleOCR.Models.LocalV5` | `3.3.1` | Apache-2.0 | https://github.com/sdcb/PaddleSharp |
| `Sdcb.PaddleInference.runtime.win64.mkl` | `3.3.1.70` | Apache-2.0 | https://github.com/sdcb/PaddleSharp |
| `OpenCvSharp4.runtime.win` | `4.11.0.20250507` | Apache-2.0 | https://github.com/shimat/opencvsharp |

The package license expressions and repository metadata were checked from the restored `.nuspec` files on 2026-09-04. NuGet's package hashes are recorded in `NUGET_HASHES.sha512`; NuGet restore remains responsible for validating downloaded packages.

The runtime and models are consumed through pinned `PackageReference` entries. VideoMaker does not download OCR binaries or models from an arbitrary URL at runtime. The application processes video frames locally and does not send OCR frames or recognized text to OpenAI, Kling, or another OCR provider.

The Apache-2.0 license text is distributed beside this document. Before a production release, the generated publish directory and installer must still be reviewed for any additional transitive notices introduced by the final resolved dependency graph.
