# Chuẩn hóa FFmpeg và Publish mặc định

Rà soát trên nhánh `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, checkout có thay đổi chưa commit, ngày 2026-10-17. Chỉ thao tác build/kiểm tra cục bộ; tài liệu không xác nhận đã deploy server hoặc phát hành cho khách.

## Phạm vi và task

| Task | Kết quả source | Bằng chứng hiện hành |
|---|---|---|
| Chuẩn hóa FFmpeg đã chốt | Pin FFmpeg/FFprobe 9.0.1, license và hồ sơ; giữ nguyên bộ Development | Đã đối chiếu archive chính thức, SHA-256 từng file và chạy media smoke |
| Gắn vào FolderProfile | Tự chuẩn bị bundle offline, kiểm pin trước khi chạy binary, tái sử dụng cache đúng hash; đưa đủ 8 file vào Publish | Helper và target MSBuild đã chạy thật; kiểm thử tự động đạt, xem phần kiểm chứng |
| Publish và kiểm ZIP | Có kiểm inventory/native identity trước và sau nén; có script chạy media từ ZIP | Bản chạy Windows đã tạo và probe đạt; tạo ZIP bị chặn bởi dung lượng, chưa kiểm ZIP thật |
| Cài toàn bộ bằng Setup | Giữ gate tải/kiểm model thật của Setup | Chưa nghiệm thu trên Windows sạch; không bỏ qua gate để tạo kết quả giả |

## Bộ FFmpeg

Định nghĩa đầu vào: [FFMPEG_RELEASE_APPROVED.json](third_party/ffmpeg/FFMPEG_RELEASE_APPROVED.json).

- Bundle: `gyan-9.0.1-essentials-win-x64`.
- Nguồn: [GyanD/codexffmpeg, FFmpeg 9.0.1 essentials](https://github.com/GyanD/codexffmpeg/releases/download/9.0.1/ffmpeg-9.0.1-essentials_build.7z).
- Archive gốc: 34.372.199 byte; SHA-256 `49a73bdf0850092a252ac4641d922f3048d63ed113e196cc65ce1e4f7fb33e85`.
- `ffmpeg.exe`: 102.856.192 byte; SHA-256 `72a489eccd008c2ec2c0a5856c5c75bc3d8bbfa90166c4566865c246445e6aa3`.
- `ffprobe.exe`: 102.652.416 byte; SHA-256 `19202b23c0043f15ad1b7bce2344f406fd52bd6efd8f995ce02e7392a1cec52f`.
- Bộ đầu ra gồm hai EXE và sáu file license/provenance/checksum/README/source availability/probe. Không mang Python, model, cache hoặc marker READY từ máy build vào bundle này.

[Hồ sơ rà soát](third_party/ffmpeg/RELEASE_REVIEW.md) phân biệt `approvalScope=Release`, `approvalKind=TechnicalPackaging` với nghiệm thu phân phối. Các trường `correspondingSourceComplete`, `redistributionQualified`, `productionQualified` vẫn là `false`: nguồn tương ứng đầy đủ của các phụ thuộc, patch và hướng dẫn build chưa được tập hợp. Không diễn giải việc đóng gói kỹ thuật thành hoàn tất nghĩa vụ GPL hoặc đã sẵn sàng production.

## Luồng Publish

1. Chọn `TOOL-LOCAL → Publish → FolderProfile → Publish`.
2. [VisualStudioRelease.props](TOOL-LOCAL/Build/VisualStudioRelease.props) chọn mặc định `artifacts/ffmpeg-release/gyan-9.0.1-essentials-win-x64`.
3. [Prepare-ReleaseFfmpegBundle.ps1](scripts/Prepare-ReleaseFfmpegBundle.ps1) kiểm đầu vào đã pin, chuẩn bị qua `.part`, kiểm lại rồi promote. Không tải binary mới trên mạng trong lúc Publish. Cache sai hash làm dừng; không tự ghi đè.
4. Target thêm đủ tám file ngay lần Publish đầu, kể cả khi thư mục chưa có lúc MSBuild đánh giá project.
5. [New-DesktopPublishedPackage.ps1](scripts/New-DesktopPublishedPackage.ps1) tạo ZIP có version/build/protocol 3, inventory, checksum và metadata SharedVps.
6. Payload tạm, ZIP và bản giải nén được xử lý tuần tự để giảm dung lượng đỉnh. Chỉ giải phóng payload tạm thuộc đúng lượt đóng gói sau khi ZIP và toàn bộ entry đã khớp hash, inventory và native identity. Bản Publish gốc được giữ nguyên. Các giới hạn ZIP/expanded size và probe sau giải nén được giữ nguyên.

Lượt thử thật phát hiện và đã sửa hai lỗi tích hợp:

- Windows PowerShell 5.1 gọi script bằng `-File` có thể thấy `$PSScriptRoot` rỗng lúc tính tham số mặc định. Hai đường dẫn mặc định của packager được tính trong thân script; regression gọi đúng `powershell.exe -File` đã đạt.
- Single-file Publish nhúng script/fixture/notice mà runtime cần đọc từ thư mục cài. Target `KeepVisualStudioRuntimeContentLoose` chạy trước `GetCopyToPublishDirectoryItems`, cập nhật `Content` và bản clone `ContentWithTargetPath`. Worker, lockfile, fixture và license được giữ thành file rời; DLL managed từ reference vẫn ở trong bundle, Content `Never` không được phân phối. Regression dùng SDK Publish thật với đầu vào nhỏ đã đạt; chỉ hai entrypoint kiểm prerequisite/đóng gói của fixture dùng stub.

ZIP mặc định: `artifacts/releases/<version>-<build>/VideoMaker-<version>-<build>-win-x64.zip`. Nên chừa ít nhất 8 GiB trên các ổ chứa build/tạm/đầu ra cho lượt Publish và kiểm ZIP; nhu cầu tăng theo payload. Không tự nén lại thư mục Publish để thay ZIP đã kiểm.

`TOOL-LOCAL/appsettings.release.json` đã được chuẩn bị riêng trên máy này, không commit và không chứa SQL password dùng chung. Kiểm cấu hình phát hành/StaticArchive đã đạt. Cấu hình này không thay thế việc cấp thông tin workflow SQL riêng cho máy đích; chưa xác minh kết nối SQL của một bản cài mới.

## Kiểm chứng

Thư mục bằng chứng hiện hành: `C:\vm-ffmpeg-release\20261017-053859`.

- Restore solution: đạt (`restore.log`).
- Build Release solution cuối: đạt, **0 warning / 0 error** (`build-final.log`).
- Nhóm FFmpeg/Publish cuối: **96 Passed / 0 Failed / 0 Skipped**, 38 giây (`focused-layout-final.log`, `ffmpeg-focused-layout-final.trx`). Các lượt 86 và 95 trước đó được giữ làm lịch sử, không cộng trùng vào 96.
- Frontend: **341 Passed / 0 Failed / 0 Skipped**, 48 file (`frontend-final.log`). Build frontend thực qua SDK Publish đạt; còn cảnh báo Vite chunk lớn. Không chạy lại `npm ci` khi ổ D chỉ còn khoảng 34 MiB.
- Bản single-file TOOL-LOCAL thật **1.0.0 / build 1** đã tạo tại `C:\vm-ffmpeg-release\20261017-053859\publish`. Native EXE SHA-256: `8a8781f25ae658b5cac41055427c21a9a446655959a38023efcc3c6529a59e6c`. Kết quả/status ghi tại `native-publish-status.json`.
- Đã chạy `Test-DesktopBundleRuntime.ps1` với chính bản Publish trên: **WebView2 READY, Media READY, OCR Anh/Trung READY, OCR MSVC app-local đạt** (`native-bundle-final.log`). Không truy cập API, SQL hoặc kiểm model Qwen/Piper đã cài trong phép probe này.
- **Chưa tạo được ZIP ứng dụng mới.** Lượt cuối dừng tại kiểm dung lượng trước hoàn tất đóng gói (`publish-final-loose-content.log`). Payload tạm của lượt thất bại được dọn theo ownership guard; bản Publish và release cũ được giữ nguyên. Không có kết quả kiểm media từ ZIP khách thật.
- Full suite chưa chạy lại: preflight profile `Full` cần **5.290.061.824 byte** tại volume temp, trong khi có khoảng 2,39 GiB (`full-suite-resource-preflight.json`). Đây là **Blocked trước chạy**, không phải một bài Passed hoặc Skipped. Kết quả full suite của lượt trước không đại diện source cuối này.

Source cuối có danh mục **1.400 file code/script/config**, SHA-256 của `source-final-manifest.json`: `d97754e5d288aa2c32606fc7b746a70820e96c0b1af53b640a61da7d2a97a742`. Bằng chứng `original-ffmpeg-preserved.json` và `settings-preserved.json` xác nhận bộ Development gốc và `appsettings.json` phát triển không đổi hash. Không tạo `FolderProfile.local.props` thật trong lượt này.

`verification-final.json` xác nhận 1.400 file source và native EXE giữ nguyên hash sau kiểm thử, 96 kết quả TRX đều Passed, số ZIP mới bằng 0. Dung lượng trống đo cuối: **C khoảng 0,771 GiB, D khoảng 0,034 GiB**; số 2,39 GiB trong preflight Full là tại thời điểm đo trước đó.

Để vượt giới hạn dung lượng, lượt kiểm SDK dùng cache biên dịch riêng và chuyển intermediate của TOOL-LOCAL sang ổ C qua file targets tạm bên ngoài repository; tiếp tục bằng `NoBuild=true`. Các thiết lập Release/win-x64/self-contained/single-file/ReadyToRun của profile được giữ. Đây là bằng chứng CLI SDK cho profile, chưa phải thao tác bấm nút Publish trong giao diện Visual Studio hoặc build từ checkout sạch.

Hai thao tác dọn cache build riêng trên ổ D đã bị cơ chế kiểm duyệt tự động chặn với lý do duy nhất `blocked by policy`; không thực hiện xóa. Sau đó chỉ tiếp tục bằng thư mục tạm mới và luồng staging có ownership guard. Không xóa model, output cũ hoặc dừng IDE/server của người dùng.

[Test-InstalledFfmpegPackage.ps1](scripts/Test-InstalledFfmpegPackage.ps1) yêu cầu ZIP, SHA-256 và thư mục QA mới. Script kiểm inventory, native policy, bundle, encode H.264/AAC, burn phụ đề tiếng Việt, mux/đọc lại phụ đề và so sánh frame. Script ghi riêng `setupCompleted=false`, `installedModelInferenceChecked=false`, `cleanWindowsChecked=false`; giải nén payload và chạy media không phải một lượt cài toàn bộ bằng Setup.

## Các bước nghiệm thu còn lại

Trước hết cần dành thêm ít nhất khoảng **8 GiB trống** trên ổ build/đầu ra phù hợp, rồi chạy lại FolderProfile và kiểm chính ZIP đã tạo bằng script QA. Lượt này chưa upload ZIP hoặc tạo/publish release trên server.

Sau khi có ZIP được kiểm, upload đúng ZIP lên VPS và đăng ký release ở API theo luồng hiện có. Trên máy kiểm tra phù hợp, chạy Setup dùng chung để tải ứng dụng/model, kiểm gate READY rồi thử mở ứng dụng và workflow SQL. Ghi riêng kết quả cài toàn bộ, model inference và Windows sạch. Không công bố production qualification chỉ từ test giả lập hoặc media smoke cục bộ.
