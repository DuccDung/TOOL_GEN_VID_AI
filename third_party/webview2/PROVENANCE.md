# Microsoft WebView2 Evergreen Standalone Installer x64

Nguồn: [Microsoft WebView2](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) và [hướng dẫn phân phối của Microsoft](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution).

Ngày 2026-10-16 đã tải installer qua HTTPS từ `https://go.microsoft.com/fwlink/?linkid=2124701`, kiểm SHA-256 và chữ ký Authenticode `Valid`, publisher Microsoft Corporation. Định danh byte và chứng thư công khai nằm trong `WEBVIEW2_INSTALLER_APPROVED.json`. Không chạy installer khi chuẩn bị pin; không đưa executable vào Git.

`installerProductVersion` là phiên bản của installer, không được dùng thay kết quả kiểm WebView2 Runtime sau cài. URL Evergreen có thể thay đổi nội dung; bản phát hành chỉ dùng file đúng size/hash đã duyệt, không tự tải bản mới rồi cập nhật pin.

Installer được giữ nguyên chữ ký, không repack hoặc sửa byte. Khi đóng gói StaticArchive, người phát hành cung cấp file bên ngoài bằng `-WebView2InstallerPath`; pipeline kiểm hash và Authenticode trước copy vào `prerequisites/webview2-runtime-x64.exe`. Setup kiểm lại trước chạy và chỉ chuẩn bị nếu runtime còn thiếu. Kiểm byte/chữ ký không thay nghiệm thu trên Windows sạch, quyền phân phối hoặc các gate phát hành khác của dự án.
