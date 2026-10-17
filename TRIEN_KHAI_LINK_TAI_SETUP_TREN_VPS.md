# Link tải Setup trên VPS — 2026-10-17

Theo phạm vi người dùng đã duyệt, Admin chỉ lưu link EXE và nút **Tải cho Windows** dùng link đó để tải trực tiếp từ VPS. Rà soát/triển khai trên nhánh `main`, HEAD nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit có 409 đường dẫn thay đổi đầu lượt; giữ nguyên các thay đổi khác.

## Hành vi

- Trong **Admin → Phát hành desktop**, có ô **Link tải Setup cho Windows**, nút **Lưu** và **Mở link tải**. Không yêu cầu version, checksum hay upload EXE cho ô cấu hình này.
- Chỉ Admin được đọc/sửa cấu hình qua `GET/PUT /api/admin/desktop-download-settings`. Lưu một JSON object tại key `Desktop.SetupDownload` trong `auth.ServerSettings`; dùng rowversion có sẵn để báo xung đột khi hai phiên sửa cùng lúc.
- Server chỉ kiểm cấu trúc URL HTTPS, đuôi `.exe` và nguồn `DesktopReleases.AllowedExternalPrefixes`, không tải/HEAD/chạy file. Hỗ trợ tên file mã hóa khoảng trắng/Unicode; từ chối userinfo, query, fragment và đường dẫn không hợp lệ.
- Các nút tải trên trang chủ vẫn dùng `GET /api/launcher-distribution/setup/latest/download?channel=Stable&platform=win-x64`. Endpoint đọc link đã lưu và trả `302` cùng `Cache-Control: no-store`; trình duyệt tải bytes trực tiếp từ VPS. Không proxy EXE qua hosting.
- Chưa có link hoặc dữ liệu lưu không hợp lệ: trả `503` với thông báo tiếng Việt dạng văn bản. Query ngoài Stable/win-x64 trả `404`.
- Mở lại trang, quay lại mục phát hành hoặc bấm Làm mới sẽ đọc cấu hình mới. Lượt lưu chưa rõ kết quả được đối soát bằng lần đọc kế tiếp; phản hồi cũ sau đăng xuất/đổi view không điền lại dữ liệu. Chỉ mở link đã được server xác nhận lưu.

Đây là cấu hình nút tải trên website. **Nó không đăng ký/kích hoạt Setup và không gỡ điều kiện Setup Active của form phát hành release.** API metadata, endpoint tải bootstrapper theo ID, kiểm hash, cài ứng dụng/model và desktop update/repair giữ nguyên. Không cần build/upload lại ZIP ứng dụng, Setup hoặc ZIP model chỉ để thêm cấu hình nút tải này.

## Source

- [DTO](TOOL-SHARED.Contracts/Updates/DesktopDownloadSettingsContracts.cs).
- [Lưu và kiểm link](TOOL-SERVER/Updates/DesktopDownloadSettingsService.cs), [API Admin](TOOL-SERVER/Controllers/AdminDesktopDownloadSettingsController.cs), DI trong [Program.cs](TOOL-SERVER/Program.cs).
- [Endpoint tải công khai](TOOL-SERVER/Controllers/LauncherDistributionController.cs).
- [Form Admin](TOOL-SERVER/Pages/Admin/Index.cshtml), [module giao diện](TOOL-SERVER/wwwroot/admin/admin-desktop-download-settings.js), vòng đời view/session trong [admin.js](TOOL-SERVER/wwwroot/admin/admin.js).
- [Kiểm thử server](TOOL-TESTS/Updates/DesktopDownloadSettingsTests.cs), [kiểm thử trình duyệt](TOOL-TESTS/Admin/admin-desktop-download-settings.browser.test.cjs).

## Database và hosting

Đã kiểm tra **chỉ đọc** lúc `2026-10-17T08:11:31.7498873Z` trên `MB2D70\MSSQLSERVER2019 / tap51004_tool`: `auth.ServerSettings` có đủ sáu cột, `RowVersion` timestamp 8 byte; tài khoản cấu hình server có SELECT/INSERT/UPDATE. Chưa có key `Desktop.SetupDownload`. Không ghi/sửa dữ liệu hosting trong lượt triển khai này; không cần migration mới trên database đã kiểm tra. [Bằng chứng chỉ đọc](D:/vm-setup-download/20261017-151108-ce53d3/hosting-settings-readonly.json).

## Đưa vào sử dụng

1. Upload đúng Setup EXE lên VPS. Có thể đặt tên dễ dùng như `VideoMaker-Setup-1.2.0.exe`; tên này là ví dụ, không xác nhận file đó đã tồn tại trên VPS.
2. Deploy server mới, giữ cấu hình database, secret và Data Protection keys của hosting. Nguồn VPS đang dùng phải nằm trong `DesktopReleases.AllowedExternalPrefixes`.
3. Vào **Admin → Phát hành desktop → Link tải Setup cho Windows**, dán link EXE rồi **Lưu**.
4. Bấm **Mở link tải**, sau đó thử nút **Tải cho Windows** trên trang chủ ở trình duyệt chưa đăng nhập.
5. Muốn đổi bộ cài tải từ website, upload file mới và sửa link tại đây. Cấu hình link không chứng nhận nội dung EXE; cần thử tải/chạy đúng bộ cài trước khi phân phối.

## Kiểm chứng

Kết quả thực tế trên checkout nêu trên được giữ tại `D:\vm-setup-download\20261017-151108-ce53d3`:

- `dotnet restore TOOL_GEN_POST_VIDEO.slnx`: đạt. Release build toàn solution: **0 Warning / 0 Error**.
- Full .NET: **2.845 Passed / 0 Failed / 18 Skipped / 2.863 Total**, 5 phút 19 giây. Nhóm riêng tính năng **36 Passed / 0 Failed / 0 Skipped** đã nằm trong full suite, không cộng thêm. Kiểm lưu qua DbContext mới, revision cũ/race ghi, URL không hợp lệ, dữ liệu lưu hỏng, `302/no-store`, `503` văn bản và link không làm thỏa Setup Active.
- Frontend `npm ci`, `npm run build`, `npm test`: **345 Passed / 0 Failed / 0 Skipped**, 48 file; còn cảnh báo Vite chunk size hiện hữu.
- Bốn bộ browser Admin (link tải mới, release preparation, external release, shell): **62 Passed / 0 Failed / 0 Skipped**, gồm **14 bài mới**. Đã xem ảnh card ở viewport 390 và 1440 px. Bộ shell còn kiểm các trang ở năm kích thước, keyboard/focus và đăng xuất.
- Lượt browser đầu **60 Passed / 2 Failed** do fixture shell cũ chưa mô phỏng API trạng thái bootstrapper. Đã bổ sung đúng mock và nạp module mới, giữ assertion chặn request ngoài dự kiến; chạy lại cả bốn bộ đạt. Giữ riêng log ban đầu và log cuối, không thay kết quả đầu thành đạt.
- Manifest 11 file source/test của phần này cùng ba assembly build không thay đổi trong lượt kiểm thử cuối. Kiểm `git diff --check` đạt. Không sửa Setup/tool-local/model hoặc cấu hình secret.

[Tổng hợp kết quả](D:/vm-setup-download/20261017-151108-ce53d3/verification-summary.json), [TRX full](D:/vm-setup-download/20261017-151108-ce53d3/setup-download-full.trx), [log browser cuối](D:/vm-setup-download/20261017-151108-ce53d3/admin-browser-final.log), [ảnh 1440 px](D:/vm-setup-download/20261017-151108-ce53d3/setup-download-1440.png), [ảnh 390 px](D:/vm-setup-download/20261017-151108-ce53d3/setup-download-390.png).

SQLite trong test mô phỏng rowversion SQL Server; kiểm tra schema hosting chỉ đọc không thay smoke lưu cấu hình sau deploy. 18 bài SQL/model/runtime opt-in bị Skipped không chứng minh đã nghiệm thu model. Browser dùng Razor/JS/CSS thật và API fixture, không truy cập VPS/hosting. Chưa ghi link, deploy hosting hoặc xác nhận khách tải/cài đúng EXE trên môi trường thật.

## Gói server đã chuẩn bị

Đã chạy `scripts/Publish-ToolServerIis.ps1` với thư mục output mới sau khi kiểm thử hoàn tất. [ZIP server IIS mới](D:/vm-setup-download/20261017-151108-ce53d3/TOOL-SERVER-IIS-dll-runtime-win-x64-20261017-082038-d6598158.zip), **59.041.264 byte** (khoảng 56,31 MiB), 502 entry; SHA-256 `352782b3a3c3ff9b371bf620aa1d70d04461b20f7d7445f604c7f48059de5c89`. Có runtime .NET/ASP.NET Core 10.0.9 riêng theo script hiện hành.

Đã kiểm lại cấu hình bàn giao không có secret, checksum ZIP/sidecar khớp, DLL/config/JS trong ZIP khớp staging và hai JS Admin khớp source đã kiểm thử. Cả bốn bản nén gzip/Brotli giải nén ra đúng bytes JS hiện tại. [Báo cáo gói](D:/vm-setup-download/20261017-151108-ce53d3/server-package.json). Gói mới được tạo riêng, không ghi đè gói bàn giao cũ. Khi deploy phải giữ cấu hình database/secret và Data Protection keys của hosting; gói này không mang cấu hình bí mật từ máy build.
