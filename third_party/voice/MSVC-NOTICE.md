# Microsoft Visual C++ runtime in the Piper offline payload

Copyright Microsoft Corporation. All rights reserved.

- Version: **14.50.35719.0**, Windows x64.
- Four application-local DLLs: `msvcp140.dll`, `msvcp140_1.dll`, `vcruntime140.dll`, `vcruntime140_1.dll`.
- Source: Microsoft's signed `VC_redist.x64.exe`, SHA-256 `8995548dfffcde7c49987029c764355612ba6850ee09a7b6f0fddc85bdc5c280`. The exact Microsoft download URL and individual DLL hashes are pinned in `scripts/prepare_piper_bundle.py`.
- Installer identity was cross-checked with [Microsoft's WinGet manifest](https://github.com/microsoft/winget-pkgs/blob/master/manifests/m/Microsoft/VCRedist/2015%2B/x64/14.50.35719.0/Microsoft.VCRedist.2015%2B.x64.installer.yaml). Extraction reads its CAB payloads; no installer/MSI action is run.
- [Microsoft runtime license terms](https://visualstudio.microsoft.com/license-terms/vs2026-ga-visualcpp-v14-redist-runtime/), [original license document](https://visualstudio.microsoft.com/wp-content/uploads/2025/10/Visual-C-V14-License-Redistributable_and_Runtime_ENU.docx).
- [Redistribution rules](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files) and [application-local deployment](https://learn.microsoft.com/en-us/cpp/windows/choosing-a-deployment-method).

These files retain Microsoft's license; they are not relicensed under the application or Piper license. Redistribution requires the appropriate Visual Studio license and compliance with its distributable-code terms. The original license document must accompany the public release review. This notice identifies the inputs for local testing and does not itself approve public distribution.

The payload replaces the two C runtime DLLs from the pinned Python archive with this same Microsoft runtime version. The worker also loads and checks the exact local paths of all four DLLs before importing Piper, so the probe cannot pass merely because the build machine has a system C++ runtime installed.
