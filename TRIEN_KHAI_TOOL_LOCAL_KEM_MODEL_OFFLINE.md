# Triển khai đóng gói TOOL-LOCAL kèm model offline

Ngày 2026-10-08. Nhánh `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, cùng working tree chưa commit. Kết quả gắn với source/binary đã chạy ở lượt này; không gán cho commit nền nguyên trạng hoặc môi trường khách.

**Đã triển khai pipeline, build/test và tạo ZIP chẩn đoán chứa OCR + Qwen3 4B CPU + Piper. Chưa đủ điều kiện gửi bản phát hành cho máy khách.** Qwen đã qua kiểm file nhưng chưa nạp/probe vì cảnh báo tài nguyên; cấu hình khách và điều kiện phân phối cũng chưa hoàn tất. Danh mục task: [kế hoạch đóng gói](PLAN_TASK_DONG_GOI_TOOL_LOCAL_KEM_MODEL_OFFLINE.md).

## Thay đổi đã triển khai

- [Publish-DesktopRelease.ps1](scripts/Publish-DesktopRelease.ps1) thêm `-IncludeQwenModel`, `-QwenModelPath` và `-DeliveryMode Updater|Direct`. Bản bật dịch local phải kèm GGUF đã pin. Script thêm model trước manifest/inventory, xác minh lại bản giải nén rồi mới nhận gói. Kiểm cấu hình khách và FFmpeg Release được giữ nguyên.
- [DesktopOfflinePackage.psm1](scripts/DesktopOfflinePackage.psm1) nén/giải nén theo stream, không giữ cả GGUF trong RAM. Kiểm traversal, reparse, tên Windows không an toàn, entry trùng, ghi đè file, size khai báo và giới hạn 4 GiB/200.000 entry. Inventory có size/SHA-256 mọi file, ngoại trừ chính inventory để tránh tự tham chiếu.
- [QwenOfflineCommand.cs](TOOL-LOCAL/SystemSetup/QwenOfflineCommand.cs) và [Test-QwenOfflineBundle.ps1](scripts/Test-QwenOfflineBundle.ps1) kiểm riêng model/worker của ứng dụng trong workspace mới, chặn HTTP và bỏ worker test mode. Không tìm model/cache theo CWD máy dev; không đăng nhập, gọi SQL hoặc đánh dấu READY cho component user đang dùng. Lỗi chỉ báo code/stage/error type an toàn.
- [Test-DesktopSetupPublish.ps1](scripts/Test-DesktopSetupPublish.ps1) nhận `-RequireQwenModel` và `-ProbeQwenOffline` để tách kiểm file khỏi chạy model thật. Probe model vẫn cần đủ tài nguyên hoặc xác nhận cảnh báo theo quy định repository.
- [New-DesktopOfflineCandidate.ps1](scripts/New-DesktopOfflineCandidate.ps1) tạo ứng viên có cấu hình rỗng và README Development. Không tạo bộ cài khách, nâng quyền phân phối FFmpeg hoặc upload release.
- [DesktopOfflinePackageTests.cs](TOOL-TESTS/Updates/DesktopOfflinePackageTests.cs) kiểm ZIP Unicode/file hidden, inventory bị sửa/thiếu/thừa, entry traversal/collision, ZIP khai sai size, bảo toàn file có sẵn và từ chối model giả.

Không đổi model pin, feature flag mặc định, credential, workflow SQL, gate Startup, giới hạn server/updater hoặc provenance/checksum bên thứ ba. Không chạy migration, provider có phí, upload/Active release hay gửi file cho người khác.

## Artifact đã tạo

Thư mục: `artifacts/offline-candidates/1.0.0-20261008/`.

| File hoặc số đo | Giá trị |
|---|---|
| ZIP | `VideoMaker-1.0.0-20261008-Qwen-Piper-DIAGNOSTIC.zip` |
| ZIP size | 3.052.786.145 byte, khoảng 3,05 GB / 2,84 GiB |
| Tổng giải nén | 3.707.168.567 byte, gồm inventory |
| Entry ZIP | 326 |
| Inventory | 325 file được hash; không tự hash `package-inventory.json` |
| Checksum | `checksums.sha256` cạnh ZIP; đã đọc lại hash ZIP cuối và đối chiếu đạt |
| Kết quả component | `components-qualification.json` |
| Trạng thái tổng | `qualification.json`: `DiagnosticCandidateNeedsQualification` |
| Snapshot source | `source-inventory.json` tại thời điểm trước publish; gồm commit nền và working tree |
| Publish log | `publish.log` |

```text
ZIP SHA-256:
e984b959134f68389d2799a6c9e0e5e7aee232112a89e6dd49539910b05c7b79

Qwen3-4B-Q4_K_M.gguf:
Size: 2497280256 bytes
SHA-256: 7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5
```

ZIP có EXE, frontend, toàn bộ worker CPU/native, payload Piper, OCR/native MSVC, FFmpeg, WebView2 loader và updater. GGUF nằm ở đường `third_party/translation/runtime-test/qwen3-4b-q4-k-m-cpu/bc64014-llamasharp-0.27.0-adapter-1/Qwen3-4B-Q4_K_M.gguf`, đã nằm trong managed files và inventory. Không kèm runtime/model Kokoro hoặc CUDA; preview/frontend hiện hành vẫn có trong publish và còn cần rà soát quyền phân phối.

`appsettings.json` trong ZIP là `{}`; không lấy connection string hoặc cấu hình của người dùng. Nhấp đúp ứng viên này chưa dùng được quy trình đăng nhập/workflow khách. README trong ZIP ghi rõ Development diagnostic candidate.

ZIP vượt giới hạn artifact server mặc định 2 GiB; không dùng như package updater tại giới hạn đó. Tổng giải nén còn dưới cap 4 GiB. Gửi file trực tiếp chỉ thực hiện sau khi chốt cấu hình, model probe, quyền phân phối và nghiệm thu khách.

## Kiểm tra đã chạy

Môi trường: Windows 11 Home Single Language x64, RAM vật lý khoảng 16 GiB, trên máy phát triển hiện tại. Thư mục ứng dụng giải nén có dấu/khoảng trắng. Đây chưa phải Windows sạch hoặc máy khách.

| Phép kiểm | Kết quả |
|---|---|
| `dotnet restore TOOL_GEN_POST_VIDEO.slnx` | Passed |
| Build solution Release `--no-restore` | Passed, 0 warning / 0 error MSBuild |
| Nhóm đóng gói cuối | 18 Passed / 0 Failed / 0 Skipped |
| Full C# cuối, `--no-build -- xUnit.ParallelizeTestCollections=false` | 1.594 Passed / 0 Failed / 13 Skipped; 6 phút 4 giây |
| Frontend `npm ci`, `npm run build` | Passed; còn cảnh báo chunk Vite >500 kB |
| Frontend `npm test` | 280 Passed / 0 Failed / 0 Skipped, 44 file |
| Publish ứng viên self-contained x64 | Passed |
| Nén/giải nén GGUF >2 GiB, inventory/hash | Passed; 325 file đúng size/hash, ZIP checksum cuối đúng |
| ZIP sau giải nén: thành phần và frontend | 56 thành phần bắt buộc, 24 web asset, 0 thiếu/sai |
| WebView2 từ ZIP | `READY` trên máy thử |
| OCR Anh/Trung và FFmpeg từ ZIP | `BundledComponentsState=READY` |
| Piper offline từ ZIP | Cài/probe workspace mới đạt; Python/model/worker từ payload local; không dùng READY/venv cũ |
| File Qwen từ ZIP | `ARTIFACT_VERIFIED`, size/SHA-256 đạt |
| Runtime/probe Qwen từ ZIP | Bị chặn trước nạp model: `TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED`, stage `VERIFYING`; chưa READY |
| Cấu hình khách với cấu hình source hiện có | Failed: SQL host phát triển |
| FFmpeg `-RequireReleaseApproval` | Failed: được duyệt Development, chưa Release |
| Cấu hình source và provenance FFmpeg | Hash giữ nguyên so với baseline trước đóng gói |

13 Skipped thuộc các bài opt-in SQL/model/GPU/benchmark/voice. Không tính chúng là model hoặc database đã đạt. Piper và component từ ZIP có chuỗi runtime riêng nêu trên; Qwen chỉ đạt file integrity, không có bằng chứng inference/benchmark trong lượt này.

Lượt Full đầu trước khi bổ sung regression ZIP khai sai size đạt 1.593 Passed / 0 Failed / 13 Skipped; kết quả cuối là dòng 1.594 ở trên. Không có test failure trong các lượt đã chạy; lỗi probe/preflight được ghi riêng, không gộp thành Passed.

Bằng chứng test tại `.tmp/offline-package-tests/`: `offline-package-unit-final.trx`, `offline-package-full-final.trx`, `build-final.log`, `full-final.log`, `candidate.log`, `release-preflight.json` và `protected-inputs.json`. Frontend tại `.tmp-offline-web-tests.log`. Tài liệu được cập nhật sau snapshot source của artifact; binary/payload của ZIP được kiểm bằng inventory/checksum riêng.

## Điều kiện còn thiếu

1. **Qwen thật:** máy đóng gói báo thiếu RAM/commit khả dụng. Đã hỏi xác nhận chạy trong điều kiện cảnh báo; chưa nhận câu trả lời và chưa truyền cờ chấp nhận để tiếp tục. Cần chọn profile đúng, probe runtime/Anh/Trung, test cancel/retry và benchmark theo phạm vi trước khi bật dịch local trong bản khách. Lỗi này khác `TRANSLATION_PROCESS_FAILED` trong ảnh của người dùng; chưa có binary/log stage máy khách để tái hiện lỗi đó.
2. **Cấu hình khách:** còn cần endpoint HTTPS, file cấu hình deployment và cơ chế SQL hợp lệ cho binary hiện hành; không dùng mật khẩu SQL chung, host dev hoặc tự chuyển kiến trúc sang API-only. Chưa truy cập server/database khách.
3. **Phân phối:** FFmpeg hiện bị gate Release từ chối. Cần hồ sơ/bundle đã được rà soát Release và review dependency/preview thực nằm trong gói; không đổi nhãn provenance để qua script.
4. **Nghiệm thu đích:** chưa có Windows/profile sạch, tài khoản test, Startup có auth/license/organization, nghe/xuất MP4 qua UI và repair/rollback khách. WebView2 loader đi kèm chưa thay cho kiểm Evergreen Runtime trên máy đó.

Quy định [AGENTS.md](AGENTS.md): “Cảnh báo thiếu RAM/commit cần xác nhận rõ”. Xác nhận tài nguyên chỉ cho phép thử worker; không bỏ checksum, platform, disk, probe hoặc lỗi thiếu bộ nhớ thực tế. Khi chưa có xác nhận, giữ task model pending và tiếp tục công việc độc lập.

## Cách tiếp tục

Giữ nguyên ZIP chẩn đoán để đối chiếu. Sau khi có điều kiện tài nguyên phù hợp hoặc xác nhận tương ứng, chạy probe Qwen bằng worker/model của bộ giải nén và lưu kết quả; không copy marker READY từ máy khác. Nếu có sửa binary/script liên quan, tạo ứng viên mới, chốt inventory/checksum và kiểm lại bản giải nén.

Khi có cấu hình khách và bundle FFmpeg Release, dùng pipeline phát hành với `-AppSettingsPath`, `-AllowTransitionalSql`, `-IncludeQwenModel` và `-DeliveryMode Direct`, cùng version/build/channel/HTTPS của môi trường đã chốt. Lệnh vẫn phải qua preflight/probe; không dùng ZIP chẩn đoán để thay bản phát hành hoặc upload Active. Chi tiết vận hành: [runbook](VAN_HANH_VA_PHAT_HANH.md).
