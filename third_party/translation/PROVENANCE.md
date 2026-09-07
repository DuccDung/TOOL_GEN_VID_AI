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

## Model

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

The native runtime policy defines two allowlisted CPU profiles without changing the model artifact: Standard uses the original 8 GiB total / 4 GiB available physical / 6 GiB available commit gate; Low-memory uses a 6 GiB total / 3 GiB available physical / 4 GiB available commit hard floor with a smaller batch and scene size. The Low-memory thresholds remain subject to the documented real-model benchmark and desktop smoke gate before production rollout.

The GGUF model is an optional component and is not stored in source control. No subtitle, prompt, credential or provider request is sent to a Cloud inference service. Production enablement remains a separate acceptance decision; provenance alone is not evidence that model integration, benchmark or desktop smoke test passed.
