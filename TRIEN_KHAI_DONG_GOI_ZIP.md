# Kết quả đóng gói ZIP desktop

**Cập nhật sau kiểm trên máy khách:** ZIP build 1 bên dưới còn phụ thuộc Visual C++ của Windows cho OCR. Đã có [bản sửa, ZIP build 2 và bản vá nhỏ](TRIEN_KHAI_SUA_OCR_MAY_KHACH.md). Kết quả build 1 ở đây là lịch sử kiểm trên máy đóng gói, không chứng minh chạy được trên máy sạch.

Ngày: 2026-09-26. Nhánh `main`, commit nền `bc1c6aec3c731bdb0edcebbde17ae13640245e52`, gồm các thay đổi chưa commit của những task trước.

## 1. Kết quả

**Đã tạo ZIP ứng viên và kiểm thành phần thành công. Chưa xác nhận bản dùng được đầy đủ cho người dùng cuối: cấu hình SQL dành cho máy khách còn thiếu.**

- ZIP: [taphoatool-1.0.0-1-win-x64-candidate.zip](artifacts/portable/candidate-1.0.0-1-20260926-2140/taphoatool-1.0.0-1-win-x64-candidate.zip).
- Dung lượng: **624.596.900 byte**, khoảng **595,66 MiB**.
- Phiên bản `1.0.0`, build `1`, channel `Stable`, platform `win-x64` theo cấu hình hiện tại.
- SHA-256: `7919e8ba4e9156bf05a5136e845c2648c41c123accc6659b44bbd855a4a9fee5`.
- [Checksum](artifacts/portable/candidate-1.0.0-1-20260926-2140/checksums.sha256) và [bằng chứng kiểm gói](artifacts/portable/candidate-1.0.0-1-20260926-2140/evidence/package-check.json).

ZIP có ứng dụng self-contained, frontend, OCR, FFmpeg/FFprobe, worker, Python/uv/wheel/DLL của Piper và model tiếng Việt `vi_VN-vais1000-medium`. Piper lấy dữ liệu từ bundle `piper-1.6.0-python-3.11.15-offline-v3` đi kèm. Không cần tải dependency Piper trên máy khách.

## 2. Cấu hình server và SQL

Đã làm theo chỉ định dùng server/SQL đang deploy, giữ endpoint và các cờ tính năng hiện tại. Bản gốc `TOOL-LOCAL/appsettings.json` không bị thay đổi trong lượt đóng gói. Chỉ bản cấu hình riêng đưa vào ứng viên được bỏ trường mật khẩu SQL.

Kiểm tra chỉ đọc trên cấu hình gốc:

| Phép kiểm | Kết quả |
|---|---|
| HTTPS endpoint desktop update | Có phản hồi HTTP 401 khi không gửi token; chưa chứng minh đăng nhập thành công. |
| Kết nối SQL | Kết nối được. |
| Thành viên `VideoMakerDesktopRole` | Không. |
| Quyền đọc workflow `vf.Projects` | Có. |
| Quyền đọc schema `auth`, `ai`, `vs`, `dbo` | Đều có. |
| `db_owner` / `sysadmin` | Đều không. |
| Ghi dữ liệu hoặc thay quyền | Không thực hiện. |

[Bằng chứng kết nối](artifacts/portable/candidate-1.0.0-1-20260926-2140/evidence/connection-check.json) chỉ chứa kết quả an toàn, không có connection string hay mật khẩu.

Theo [TOOL-LOCAL/AGENTS.md](TOOL-LOCAL/AGENTS.md), desktop phải dùng tài khoản thuộc `VideoMakerDesktopRole` trong phạm vi workflow. [Test-DesktopDeploymentSettings.ps1](scripts/Test-DesktopDeploymentSettings.ps1) còn chặn việc nhúng mật khẩu SQL dùng chung: `A shared SQL password must not be embedded in the desktop package.` Vì vậy tài khoản hiện tại chưa phù hợp để đi cùng bản phân phối.

**Bỏ mật khẩu chỉ giúp tạo ứng viên an toàn để kiểm component; không làm SQL tự hoạt động.** Cần cấu hình SQL dành cho desktop với quyền phù hợp và cách cấp xác thực cho máy khách. Nếu dùng SQL login, mật khẩu phải được cấp riêng theo cơ chế được duyệt; đổi sang login ít quyền hơn không tự làm việc nhúng mật khẩu dùng chung vào ZIP trở nên hợp lệ.

Source có [script quyền desktop](database/VideoFactory.DesktopLeastPrivilege.sql) để tham khảo. Chưa chạy script này, tạo login hoặc thay quyền trên database đang deploy; trước thay đổi thật phải xác minh đúng đích và phương án backup/restore.

## 3. Kiểm tra trên chính ZIP mới

ZIP được giải nén vào thư mục mới có dấu/khoảng trắng trên ổ D:. Piper cài vào workspace mới trên ổ C:. Các lệnh probe dùng PATH chỉ có thư mục Windows; Piper được chạy với proxy riêng cho tiến trình trỏ tới cổng local không hoạt động và chế độ không tải mạng. Đây là phép thử offline bằng cấu hình tiến trình, không phải packet capture.

| Phép kiểm | Kết quả mới trong lượt này |
|---|---|
| Publish Release self-contained x64 | Thành công; bật `RequireMediaToolBundle` và `RequirePiperOfflineBundle`. |
| Hash Piper archive/manifest/worker/lock | Đạt. |
| Cấu hình ứng viên | Validator đạt sau khi bỏ mật khẩu; không đồng nghĩa SQL workflow đã chạy. |
| Thành phần bắt buộc / web asset | 50 / 24; 0 thiếu hoặc sai hash. |
| OCR tiếng Anh và Trung | READY qua runtime probe thực tế. |
| FFmpeg/FFprobe | READY qua runtime probe thực tế. |
| WebView2 | Loader/runtime READY trên máy kiểm hiện tại. |
| Piper cài mới, tổng hợp WAV Việt | READY. |
| Piper kiểm lại sau cài | 3/3 READY. |
| ZIP và manifest | SHA-256 giữ nguyên sau probe; inventory khớp nội dung giải nén. |
| Tiến trình chẩn đoán còn sót | 0. |
| Cấu hình gốc | Hash giữ nguyên. |

Bằng chứng: [component](artifacts/portable/candidate-1.0.0-1-20260926-2140/evidence/zip-components.json), [Piper](artifacts/portable/candidate-1.0.0-1-20260926-2140/evidence/zip-piper.json), [kiểm cuối](artifacts/portable/candidate-1.0.0-1-20260926-2140/evidence/final-verification.json), [inventory](artifacts/portable/candidate-1.0.0-1-20260926-2140/evidence/package-inventory.json).

Các probe mới đều đạt; không có bài probe bị bỏ qua. Đăng nhập và Windows sạch là **chưa kiểm**, không được tính là đạt.

## 4. Source và kiểm thử trước đó

Lượt này không sửa source sản phẩm. Snapshot 988 file source trùng đợt nghiệm thu trước, hash manifest `7BFDB6E8963536505010BD40BB489946B47C3DEF5B0E2DE8DD4339F4CC51A96E`. Bản publish vừa được tạo và kiểm component riêng; không gán kết quả test binary cũ thành lần chạy test của binary mới.

Các chuỗi test C# và frontend trước đó được ghi riêng trong [báo cáo Piper](TRIEN_KHAI_PIPER_OFFLINE_TRONG_ZIP.md). Không chạy lại chúng trong lượt này. Bằng chứng mới ở mục 3 là kết quả đóng gói và kiểm thực tế ZIP.

Log/script làm việc nằm tại `D:\vmpackage\taphoatool-20260926-2140`. Thư mục này có cấu hình phục vụ đóng gói; không dùng cả thư mục làm file bàn giao.

## 5. Phạm vi còn lại

1. Hoàn tất cấu hình/xác thực SQL dành cho máy khách, không dùng tài khoản hiện tại có quyền đọc ngoài workflow.
2. Sau khi có cấu hình hợp lệ, tạo lại ZIP/checksum và kiểm phần cấu hình thay đổi; đăng nhập, license, tổ chức và workflow cần nghiệm thu bằng tài khoản ứng dụng được chỉ định.
3. Kiểm trên Windows sạch riêng. ZIP có WebView2 loader; máy khách vẫn cần WebView2 Runtime. Chưa cam kết dùng ngay trên máy thiếu runtime.
4. Hồ sơ phân phối vẫn giữ nguyên: FFmpeg còn scope `Development`, các mục rà soát MSVC/voice trong hồ sơ trước chưa được tự nâng trạng thái. Ứng viên được tạo trực tiếp để kiểm component; chưa chạy trọn đường `Publish-DesktopRelease.ps1` cho release đã duyệt.

Chưa nghe WAV thủ công hoặc chạy workflow media qua UI trên môi trường khách. Không chạy migration, sửa tài khoản SQL, tạo request AI có phí, upload/Active package trên server hoặc gửi ZIP ra ngoài.
