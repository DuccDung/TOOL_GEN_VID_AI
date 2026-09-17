# Provenance — Vietsub local translation

> Hồ sơ nguồn/pháp lý; trạng thái runtime và nghiệm thu nằm tại [Bối cảnh hiện hành](../../BOI_CANH_HE_THONG_HIEN_HANH.md). Model đúng hash không đồng nghĩa feature đã `READY`.

## Runtime

- Package: `LLamaSharp` `0.27.0`
- NuGet source: `https://api.nuget.org/v3/index.json`
- Repository: `https://github.com/SciSharp/LLamaSharp`
- Repository commit recorded by NuGet: `7cbbc45e421d55794d5050d126e0b96511007007`
- License: MIT, included in `LICENSE-LLAMASHARP-AND-LLAMA.CPP-MIT.txt`

- Package: `LLamaSharp.Backend.Cpu` `0.27.0`
- NuGet source: `https://api.nuget.org/v3/index.json`
- Native engine: llama.cpp/ggml CPU backend packaged by LLamaSharp
- llama.cpp repository: `https://github.com/ggml-org/llama.cpp`
- License: MIT, included in `LICENSE-LLAMASHARP-AND-LLAMA.CPP-MIT.txt`

## Optional NVIDIA acceleration — 2026-09-17

The CPU baseline remains bundled. The optional CUDA pack is downloaded directly from pinned upstream archives by `VietsubTranslationCudaInstaller`; it is not resolved through a floating NuGet dependency. The pack is Windows x64 only and uses the same LLamaSharp 0.27.0 API and Qwen GGUF. Complete archive and individual native SHA-256 allowlists are in `VietsubTranslationCudaInstaller.cs` and the shared `VietsubTranslationAcceleration.cs`. The worker verifies these before loading native code.

| Archive | Version | License | Source |
|---|---|---|---|
| LLamaSharp.Backend.Cuda12.Windows | 0.27.0 | MIT | https://api.nuget.org/v3-flatcontainer/llamasharp.backend.cuda12.windows/0.27.0/llamasharp.backend.cuda12.windows.0.27.0.nupkg |
| CUDA cuBLAS | 12.4.5.8 | NVIDIA CUDA Toolkit EULA | https://developer.download.nvidia.com/compute/cuda/redist/libcublas/windows-x86_64/libcublas-windows-x86_64-12.4.5.8-archive.zip |
| CUDA Runtime | 12.4.127 | NVIDIA CUDA Toolkit EULA | https://developer.download.nvidia.com/compute/cuda/redist/cuda_cudart/windows-x86_64/cuda_cudart-windows-x86_64-12.4.127-archive.zip |

NVIDIA archive hashes were verified against `https://developer.download.nvidia.com/compute/cuda/redist/redistrib_12.4.1.json`. The unmodified upstream license is preserved in `LICENSE-NVIDIA-CUDA-12.4.txt` (SHA-256 `e2c71babfd18a8e69542dd7e9ca018f9caa438094001a58e6bc4d8c999bf0d07`) and installed as `LICENSE-NVIDIA.txt`. The selected cuBLAS/cuBLASLt/cudart DLL families appear in Attachment A of that license. The complete agreement, NVIDIA-only scope and distribution conditions continue to apply; GPU functionality or a model probe is not a release approval.

CPU and CUDA readiness evidence are separate. Driver/device UUID, model, worker/protocol, CPU kernels, CUDA dependencies, inference config and planner version bind the CUDA probe; free VRAM is checked on each new load and is not a permanent readiness claim. Backend changes restart the isolated worker. Old job strategy versions retain CPU execution and existing semantic fingerprints.

## Model (unchanged artifact)

- Component ID: `qwen3-4b-q4-k-m-cpu`
- Engine version: `bc64014-llamasharp-0.27.0-adapter-1`
- Model: `Qwen3-4B-Q4_K_M.gguf`
- Repository: `https://huggingface.co/Qwen/Qwen3-4B-GGUF`
- Pinned revision: `bc640142c66e1fdd12af0bd68f40445458f3869b`
- Upstream base model: `https://huggingface.co/Qwen/Qwen3-4B`
- Size: `2497280256` bytes
- SHA-256: `7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5`
- License: Apache-2.0. The complete license text is stored at `../ocr/LICENSE-APACHE-2.0.txt` and is copied into release output as `third_party/translation/LICENSE-QWEN3-APACHE-2.0.txt`.

The desktop installer first checks native-defined local development cache locations for the exact approved component/version. A matching artifact is size- and SHA-256-verified, then published into the deterministic application-owned component cache by hard link when possible or by verified `.partial` copy otherwise. Only when no valid local artifact exists does it use the pinned HTTPS URL and restricted Hugging Face CDN redirect hosts. Network downloads also use `.partial`, enforce the declared size, verify SHA-256 and publish atomically. Every path then runs semantic/context probes for both `en -> vi` and `zh -> vi` before marking the component ready.

LLamaSharp/llama.cpp inference runs only in the packaged x64 translation worker, not inside the WinForms process. The desktop and worker both enforce bounded IPC/resource checks; runtime readiness is recorded only after the worker, backend, native library, configuration and English/Chinese probes match the approved fingerprint.

The native runtime policy defines two allowlisted resource profiles without changing the model artifact: Standard recommends 8 GiB total / 4 GiB available physical / 6 GiB available commit; Low-memory recommends 6 GiB total / 3 GiB available physical / 4 GiB available commit with a smaller batch and scene size. Falling below these recommendations requires explicit resource-warning confirmation; integrity/platform checks and actual load failures remain blocking. The profiles remain subject to the documented real-model benchmark and desktop smoke gate before production rollout.

The GGUF model is an optional component and is not stored in source control. No subtitle, prompt, credential or provider request is sent to a Cloud inference service. Production enablement remains a separate acceptance decision; provenance alone is not evidence that model integration, benchmark or desktop smoke test passed.
