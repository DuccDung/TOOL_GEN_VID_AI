# Triển khai Piper offline trong ZIP

Ngày 2026-09-26. Rà soát nhánh `main`, commit nền `bc1c6aec3c731bdb0edcebbde17ae13640245e52` cùng working tree đang có thay đổi Setup ZIP và ổn định kiểm thử. Đây là thay đổi chưa commit; bằng chứng gắn với source/binary manifest của từng lượt, không gán kết quả mới cho commit nền.

**Đã triển khai source/payload và hoàn tất các chuỗi nghiệm thu tự động trên máy hiện tại.** Chưa nghiệm thu Windows sạch/máy thứ hai, tài khoản thử thực tế hoặc phát hành production. Cấu hình `TOOL-LOCAL/appsettings.json` của người dùng được giữ nguyên và kiểm lại hash trước bàn giao.

## Hành vi đã triển khai

- ZIP có gói Piper riêng, gồm Python 3.11.15 x64, uv 0.12.3, bảy wheel đã khóa hash, model/config VAIS-1000 và bốn DLL Visual C++ 14.50.35719.0. Máy khách chuẩn bị Piper từ file local; không phải tải Python/model qua GitHub, PyPI hoặc Hugging Face.
- Builder xác minh nguồn, cài thử bằng wheel local và tạo WAV trước khi xuất archive/manifest. Metadata được duyệt được nhúng trong ứng dụng; manifest đặt cạnh ZIP không tự làm nguồn tin cậy.
- Giải nén vào staging có kiểm từng file rồi chuyển payload tới thư mục runtime cuối. Sau đó mới tạo venv. Không copy venv, cache hoặc marker READY từ máy build sang khách.
- Startup gửi một operation Piper sau check trong context được phép, khi host đã xác minh gói đi kèm. Hủy/lỗi/remount không tự lặp cài đặt. Cài/sửa thủ công dùng cùng payload; thiếu/hỏng gói hiển thị yêu cầu bản ZIP đầy đủ.
- Getter không cài đặt. Gate React và C# vẫn yêu cầu mọi thành phần bắt buộc READY. Piper không làm OCR hoặc Qwen tự trở thành sẵn sàng.
- Runtime/marker riêng theo phiên bản, khóa cài giữa tiến trình, giữ runtime cũ và project. Worker/uv thuộc Windows Job Object để được dừng khi tiến trình sở hữu kết thúc. Gói được cài lại khi thư viện/model bị sửa; không có fallback tải mạng ngầm.
- `RequireUvInstaller()` cho Kokoro vẫn dùng uv đã xác minh. Không tự tải thêm Kokoro/Qwen hoặc thay giọng đã chọn của project.
- Worker xử lý đường dẫn có dấu bằng cách nạp dữ liệu eSpeak qua đường dẫn tương đối trong tiến trình riêng. Worker kiểm các DLL Visual C++ thực sự nạp từ Python đi kèm, tránh dùng thành công giả nhờ runtime hệ thống đã cài trên máy dev.

Tính offline áp dụng cho Piper. Login, license, organization, SQL workflow và Cloud vẫn theo kiến trúc hiện hành.

## Artifact được duyệt cho lần triển khai này

Định nghĩa: [PIPER_OFFLINE_APPROVED.json](third_party/voice/PIPER_OFFLINE_APPROVED.json).

| Thuộc tính | Giá trị |
|---|---|
| Bundle version | `piper-1.6.0-python-3.11.15-offline-v3` |
| Archive | 159.970.075 byte, khoảng 152,56 MiB |
| SHA-256 archive | `e2d1b5f212d2cc6a3b547f9280e380416c57c5c151d639595009624caf4f6e1d` |
| SHA-256 manifest | `69bfe70e9ef038f2c157db035cf6f59ae8310028359d93b1dc16de73c691f1b5` |
| Payload giải nén | 231.194.288 byte, chưa tính venv/cache tạo trên máy đích |
| Dung lượng trống tối thiểu | 727.344.098 byte |
| Nguồn payload đang dùng | `artifacts/piper-offline/piper-1.6.0-python-3.11.15-offline-v3/` |
| Input/proof và nhật ký cục bộ | `D:\vmtest\piper-offline-20260926` |

Payload trong `artifacts/` bị Git ignore. Checkout mới phải chuẩn bị payload trước publish; dev build không tự tải model. `.gitattributes` cố định LF cho worker/lockfile/notices có hash để tránh thay đổi byte do checkout Windows.

## Đường triển khai chính

| Phạm vi | Source/script |
|---|---|
| Builder | `scripts/Prepare-PiperOfflineBundle.ps1`, `scripts/prepare_piper_bundle.py` |
| Xác minh/giải nén | `TOOL-LOCAL/Vietsub/Voice/PiperOfflineBundle.cs` |
| Cài/kiểm/phục hồi | `VietsubVoiceComponentStore.cs`, `VietsubVoiceComponentStore.Offline.cs`, `PiperProcessTree.cs` |
| Worker | `TOOL-LOCAL/Vietsub/Voice/Workers/piper_worker.py`, `VietsubPiperVoiceSynthesizer.cs` |
| Startup | `SystemSetupAdapters.cs`, `SystemSetupContracts.cs`, `SetupErrors.cs`, `Web/src/features/systemSetup/*` |
| Đóng gói | `TOOL-LOCAL.csproj`, `Publish-DesktopRelease.ps1`, `Test-DesktopSetupPublish.ps1` |
| Chẩn đoán | `PiperOfflineCommand.cs`, `Test-PiperOfflineBundle.ps1` |

`Publish-DesktopRelease.ps1` yêu cầu đúng payload trước publish, đưa hai file Piper vào managedFiles và kiểm chính ZIP sau giải nén. Bước kiểm chỉ thành công khi Piper tạo WAV thật trong workspace mới. Các lệnh `--check-desktop`, `--check-webview2`, `--check-bundled-components` giữ nghĩa cũ và không tự cài Piper.

## Kiểm thử

### Chuỗi nghiệm thu trên source cố định

Môi trường: Windows build 26200 x64, .NET SDK 10.0.301, Node 24.16.0, khoảng 16 GiB RAM. Các chuỗi model dùng workspace/cache mới có dấu, kết thúc tiến trình trước khi chạy Full. Lượt Full đầu chạy đồng thời với frontend và gặp timeout được ghi bên dưới; chuỗi Full tiếp theo chạy riêng sau khi frontend hoàn tất. Không chạy Piper đồng thời với build hoặc kiểm thử native nặng.

| Kiểm tra | Passed / Failed / Skipped | Bằng chứng |
|---|---|---|
| Restore và build Release cuối | Thành công; MSBuild 0 warning, 0 error | `restore.log`, `build-acceptance.log`; Vite còn cảnh báo chunk >500 kB |
| Piper thật, ba lượt liên tiếp | **1 / 0 / 0 mỗi lượt**, chuỗi hoàn tất | `D:\vmpip\20260926-201421-Piper-fcbc8f` |
| C# đầy đủ, ba lượt chạy riêng | **1.570 / 0 / 13 mỗi lượt**, chuỗi hoàn tất | `D:\vmpip\20260926-202444-Full-0e131a` |
| Frontend đầy đủ, ba lượt đổi seed | **280 / 0 / 0 mỗi lượt**, 44 file | `D:\vmpip\20260926-201907-Frontend-370653` |
| Startup/Settings frontend, 20 lượt đổi seed | **23 / 0 / 0 mỗi lượt**, ba file; chuỗi hoàn tất | `D:\vmpip\20260926-201955-Frontend-05621a` |
| Bundle/Startup/gate phía C#, 20 lượt | **58 / 0 / 0 mỗi lượt**, chuỗi hoàn tất | `D:\vmpip\20260926-203647-SetupOffline-15dc5c` |

Ba lượt Piper mất 93,42 / 89,72 / 86,88 giây; mức commit bộ nhớ cao nhất của cả cây tiến trình thử khoảng 839–842 MiB, không phải RAM riêng của ứng dụng. Cả ba đều không còn tiến trình con. Source manifest SHA-256 `7bfdb6e8963536505010bd40bb489946b47c3def5b0e2de8dd4339f4cc51a96e`; binary manifest `2f30ed7981572786c4bf9cd475da25be91ed0206ba8355e33c68130d4a22a8f0`. Các file `summary.json`, `source-manifest.json`, `binary-manifest.json`, TRX/JSON và log lưu tại thư mục từng chuỗi.

Chuỗi frontend 20 lượt dùng bản sao runner trong thư mục nhật ký, chỉ giới hạn ba file `StartupSystemSetupModal.test.tsx`, `SystemSetupPanel.test.tsx` và `StartupSystemSetupApp.test.tsx`. Runner này vẫn kiểm source/binary manifest, identity test, skip và tiến trình con; không thay runner trong repository để chạy lượt kiểm này.

Sau lỗi Full đầu, kiểm riêng `Cpu_backend_dry_run_reports_selected_avx_and_native_hash` đạt **1 Passed / 0 Failed / 0 Skipped trong cả năm lượt**, thời gian riêng bài khoảng 1,37–2,08 giây. Chuỗi `D:\vmpip\20260926-202418-RegressionBridge-ca660b` dùng bản sao runner có filter đúng tên bài này, không phải bài bridge mặc định. Chưa có bằng chứng đủ để xác định nguyên nhân timeout; tải đồng thời là yếu tố môi trường được loại khỏi chuỗi Full tiếp theo.

Ba lượt Full mất 241,01 / 237,21 / 235,96 giây, giữ nguyên cấu hình xUnit bốn thread và cách nhóm native test hiện hành. 13 bài Skipped trong mỗi lượt là các bài opt-in cho SQL, model Qwen/GPU/benchmark và giọng thật. Piper offline đã có chuỗi model riêng ở trên; các bài còn skip không được tính là model hoặc database đã đạt. Lượt lỗi khi chạy đồng thời vẫn được giữ trong hồ sơ, chưa xác định chắc chắn nguyên nhân timeout đó.

Năm chuỗi nghiệm thu chính đều hoàn tất, giữ nguyên source/binary manifest, identity/kết quả test giữa các lượt và không còn tiến trình con. Các lượt điều tra dưới đây được giữ để đối chiếu, không thay cho chuỗi cuối.

Kiểm tra bàn giao trong `acceptance-final.json`: hash source/binary vẫn khớp cả năm chuỗi, payload/worker/lockfile khớp định nghĩa đã duyệt, `git diff --check` đạt và hash `TOOL-LOCAL/appsettings.json` bằng baseline đầu phiên. Cú pháp năm script PowerShell liên quan được kiểm, không có lỗi parse.

### Các lượt điều tra trước chuỗi cuối

| Lượt điều tra | Kết quả và xử lý |
|---|---|
| Proof Python/wheel offline đầu tiên | Cài thành công; eSpeak lỗi với đường dẫn có dấu. Sửa worker, WAV cùng đường dẫn có dấu đạt. |
| C# install đầu tiên | Phát hiện `activate_this.py` do uv sinh chưa nằm trong manifest. Builder bổ sung hash file này; không bỏ kiểm tra file lạ. |
| Rà soát native dependency | ONNX Runtime import `MSVCP140.dll` và `MSVCP140_1.dll`. Bổ sung DLL từ installer Microsoft đã xác minh signature/hash; proof kiểm đường nạp DLL local và tạo WAV đạt. |
| Full suite đầu | 1.566 Passed / 0 Failed / 13 Skipped trước các bổ sung test cuối. `tests/initial.trx`. |
| Test reparse đầu | 18 Passed / 1 Failed: máy không có quyền tạo symbolic link. Fixture chuyển sang junction NTFS, vẫn kiểm cùng ranh giới reparse; không bỏ assertion. |
| Unit sau bổ sung process ownership | 20 Passed / 0 Failed / 0 Skipped. `tests/offline-unit-jobs.trx`. |
| Model series `20260926-195807-Piper-9cc12b` | Bài model 1 Passed / 0 Failed / 0 Skipped, nhưng runner lỗi cleanup MAX_PATH ở PowerShell 5.1. Đã dùng đường dẫn extended-length sau kiểm root/reparse; không chấp nhận chuỗi này. |
| Model series `20260926-200430-Piper-b0ecd4` | Ba bài model đều Passed, không còn tiến trình con; 92,83 / 91,80 / 89,63 giây mỗi bài. Runner từ chối chốt vì source test/runner được bổ sung giữa chuỗi; phải chạy lại trên source cố định. |
| Full series `20260926-201905-Full-47c8cd` | 1.569 Passed / 1 Failed / 13 Skipped. `Cpu_backend_dry_run_reports_selected_avx_and_native_hash` vượt load timeout 5 giây trong lúc frontend cũng chạy; không còn tiến trình con. Giữ TRX/log, kiểm riêng ca lỗi rồi chạy Full khi frontend đã kết thúc. Không tăng timeout, bỏ assertion hoặc sửa runtime Qwen để lấy kết quả đạt. |

Kiểm model gồm: workspace/cache mới có dấu, hủy giải nén và thử lại, dọn staging gián đoạn, giữ runtime v2, tạo WAV có tín hiệu âm thanh, năm lần tạo store mới/mở lại, phát hiện module bị sửa và cài sửa offline. Kiểm uv cho Kokoro vẫn trả đúng executable đã pin. Mỗi bài có hai lượt cài hoàn chỉnh; thời gian bài không phải thời gian một lần mở ứng dụng.

Phương pháp kiểm offline: cache riêng rỗng, uv `--offline --no-python-downloads --no-index --find-links`, Python từ payload, HTTP handler từ chối mọi request, proxy không kết nối được trong tiến trình thử. Không thay DNS/proxy/firewall của máy. Chưa có packet capture cho toàn bộ cây tiến trình, nên không mô tả kết quả này như một phép đo lưu lượng mạng độc lập.

## Kiểm bản ZIP đã giải nén

Đã tạo bản chẩn đoán self-contained `win-x64`, bật `RequirePiperOfflineBundle`, kiểm inventory theo bộ lọc publish và tạo ZIP thật. Bản này dùng cấu hình triển khai rỗng `{}`; không lấy cấu hình của người dùng và không dùng để gửi khách hàng.

| Phép kiểm | Kết quả |
|---|---|
| ZIP chẩn đoán | `Piper-offline-DIAGNOSTIC-NO-DEPLOYMENT-SETTINGS.zip`, 624.595.757 byte |
| SHA-256 ZIP | `196c1a67430c2b1b3fa78b486cf47a3ca0514e63d1739db29bccb79e9c8f0728` |
| Inventory sau giải nén | 50 thành phần bắt buộc, 24 web asset, 0 file thiếu hoặc sai hash |
| WebView2, OCR và FFmpeg thật | `WebView2State=READY`, `BundledComponentsState=READY` |
| Thư mục ứng dụng | Có dấu tiếng Việt, nằm trên ổ D; ACL tạm thời từ chối ghi và đã kiểm ghi thất bại |
| Runtime Piper | Workspace mới trên ổ C, cài từ ZIP thành công; ACL của thư mục ứng dụng được khôi phục sau kiểm |
| Kiểm lại qua tiến trình mới | Năm lượt liên tiếp `READY`, không báo lỗi |

Cả lần cài và năm lần kiểm lại báo `NetworkDownloadsAllowed=false`, `ServerAccessChecked=false`. Bằng chứng: `zip-components.json`, `zip-piper.json`, `zip-diagnostic.log` và `publish-diagnostic.log` trong thư mục nhật ký nêu trên. Đây là probe component của artifact; chưa thay cho đăng nhập và thao tác Vietsub trong UI bằng tài khoản thử.

WAV từ proof Piper đã được ghép vào MP4 bằng FFmpeg của bản publish. FFprobe xác nhận video H.264 và audio AAC mono 22.050 Hz, audio dài 2,310385 giây. Bằng chứng `export-smoke.json` và `piper-offline-export-smoke.mp4`. Phép kiểm này xác minh đường codec của WAV thật; chưa nghiệm thu nghe hoặc quy trình xuất bằng UI trên video người dùng.

### Số đo trên máy hiện tại

Một lượt đo riêng sau khi toàn bộ suite kết thúc, dùng executable đã publish, workspace/cache mới trên ổ C, PATH tối thiểu và proxy không kết nối được trong tiến trình thử:

| Phép đo | Kết quả |
|---|---|
| Chuẩn bị Piper lần đầu bằng CLI | **42,38 giây**, READY |
| Khởi động tiến trình mới để kiểm lại | **4,12 giây**, READY |
| File trong workspace sau cài | **493.310.074 byte**, khoảng 470,46 MiB, 7.198 file; gồm payload/venv/cache/marker |
| Mức commit bộ nhớ cao nhất của cây tiến trình | Khoảng **693 MiB** khi chuẩn bị, **703 MiB** khi kiểm lại |
| Tiến trình còn lại sau mỗi lệnh | **0** |

Bằng chứng `runtime-measurements.json`. Đây là một mẫu đo trên máy dev, không phải cam kết tốc độ trên máy khách. Thời gian tính toàn lệnh CLI, không gồm đăng nhập/license/startup UI; byte file không phải số block thực cấp phát hoặc đỉnh dung lượng trong lúc cài. Mức commit bộ nhớ không đồng nghĩa với RAM thường trú. Runtime đo này không được copy vào ZIP.

## Cách dùng khi build lại

Theo [runbook](VAN_HANH_VA_PHAT_HANH.md), chuẩn bị bằng Python trên **máy build**:

```powershell
.\scripts\Prepare-PiperOfflineBundle.ps1 -BuilderPython 'C:\BuildTools\Python311\python.exe' `
  -InputDirectory 'D:\PiperBuildInputs' -OutputDirectory 'D:\PiperBuildCandidate' -Download
.\scripts\Test-PiperOfflineBundle.ps1 -BundleDirectory 'D:\PiperBuildCandidate' -ArtifactOnly
```

Chỉ dùng candidate có hash khớp định nghĩa đã duyệt. Khi thay payload đã phát hành, tăng version và duyệt lại định nghĩa. Copy `manifest.json` và `piper-offline.zip` vào thư mục artifacts nêu trên hoặc truyền `-PiperBundlePath` khi publish; không copy thư mục proof/cache/venv.

Kiểm bản đã giải nén:

```powershell
.\scripts\Test-PiperOfflineBundle.ps1 -PublishRoot 'D:\BanDaGiaiNen' -Workspace 'D:\PiperProbeMoi'
.\scripts\Test-PiperOfflineBundle.ps1 -PublishRoot 'D:\BanDaGiaiNen' -Workspace 'D:\PiperProbeMoi' -VerifyOnly
```

Workspace lần đầu phải rỗng. Các lệnh này không đọc cấu hình triển khai, không đăng nhập/SQL/provider và không sửa bộ Piper đang dùng trong profile khác.

## Phạm vi chưa xác minh trước phát hành

- Windows sạch/máy hoặc tài khoản thứ hai; không dùng bằng chứng trên máy dev để thay thế.
- Startup sau đăng nhập/license/organization với môi trường thử thật, nghe nghiệm thu câu tiếng Việt và playback/export trên video thực tế. Test mô phỏng hoặc đo PCM không thay bước nghe.
- Phê duyệt phân phối các dependency. Giữ các điều kiện hiện có của FFmpeg/Piper/model; xem [MSVC-NOTICE.md](third_party/voice/MSVC-NOTICE.md) về DLL mới. Tải tài liệu license gốc Microsoft trên máy này chưa thành công do DNS của host tài liệu; cần bổ sung vào hồ sơ rà soát trước public release. Không thay bằng một tuyên bố cấp phép tự soạn.
- Chưa publish/upload release, thay server/database/credential, gọi provider có phí hoặc thay dữ liệu project người dùng.

PO01–PO10 có source và kiểm thử cục bộ như bảng kết quả. PO11 mới đạt phần kiểm trên máy hiện tại; các môi trường và phép đo chưa truy cập giữ trạng thái chưa xác minh. PO12 đã bàn giao source/runbook/payload/bằng chứng cục bộ; mốc nghiệm thu phát hành chưa hoàn tất.
