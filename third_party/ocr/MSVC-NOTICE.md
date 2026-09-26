# Microsoft Visual C++ runtime for desktop OCR

Copyright Microsoft Corporation. All rights reserved.

Version **14.50.35719.0**, Windows x64. `MSVC_RUNTIME.json` pins the original Microsoft installer and four application-local DLLs: `msvcp140.dll`, `vcruntime140.dll`, `vcruntime140_1.dll`, `vcomp140.dll`.

The installer is the same signed input recorded for Piper in `third_party/voice/MSVC-NOTICE.md`. The DLLs are extracted from its attached CAB and `a4` minimum-runtime CAB, without running the installer or MSI. All four signatures were verified as Microsoft-signed, valid, during preparation on 2026-09-26. Do not copy DLLs from System32, a developer toolchain or a third-party DLL download site.

`onnxruntime.dll` in PaddleOCR imports the C/C++ runtime; `mkldnn.dll` imports `vcomp140.dll`. These files must travel alongside `TOOL-LOCAL.exe`, independently of the isolated Python/Piper runtime. Windows Media Foundation remains an operating-system prerequisite; its system DLLs are not redistributed in this bundle.

- [Microsoft installer identity in WinGet](https://github.com/microsoft/winget-pkgs/blob/master/manifests/m/Microsoft/VCRedist/2015%2B/x64/14.50.35719.0/Microsoft.VCRedist.2015%2B.x64.installer.yaml).
- [Microsoft runtime license terms](https://visualstudio.microsoft.com/license-terms/vs2026-ga-visualcpp-v14-redist-runtime/) and [original license document](https://visualstudio.microsoft.com/wp-content/uploads/2025/10/Visual-C-V14-License-Redistributable_and_Runtime_ENU.docx).
- [Redistribution rules](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files) and [application-local deployment](https://learn.microsoft.com/en-us/cpp/windows/choosing-a-deployment-method).

Microsoft's license applies to these files; the application's and OCR's licenses do not replace it. Redistribution requires the appropriate Visual Studio license and compliance with its distributable-code terms. This notice records technical provenance and does not approve public distribution; the original license document remains part of the public release review described in the existing voice notices.
