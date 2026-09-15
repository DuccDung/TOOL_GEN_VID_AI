# Công cụ tải Bilibili

- Upstream: https://github.com/yt-dlp/yt-dlp
- Phiên bản ghim: **2026.08.19**, phát hành 2026-08-19; đối chiếu 2026-09-11.
- Binary Windows x64: https://github.com/yt-dlp/yt-dlp/releases/download/2026.08.19/yt-dlp.exe
- Kích thước: **17840399 bytes**.
- SHA-256: **66674953fe251b89f4d08c5f0e35e0728679bd67ab3d7d05c0562af101dd3e7a**.
- Nguồn checksum: https://github.com/yt-dlp/yt-dlp/releases/download/2026.08.19/SHA2-256SUMS và digest asset từ GitHub Release API.
- Source tương ứng: https://github.com/yt-dlp/yt-dlp/tree/2026.08.19
- `LICENSE` và `THIRD_PARTY_LICENSES.txt` được lưu nguyên văn từ tag trên.

Theo README upstream, source yt-dlp dùng Unlicense, nhưng executable PyInstaller tổng hợp chứa thành phần GPLv3+ và toàn bộ executable được cấp phép GPLv3+. Không được mô tả binary chỉ là Unlicense. Xem https://github.com/yt-dlp/yt-dlp/tree/2026.08.19#license.

VideoMaker gọi công cụ độc lập bằng process/argument list; không nhúng binary vào source hoặc bộ cài. Người dùng tải bản upstream không sửa đổi bằng nút Chuẩn bị công cụ tải. Installer kiểm đúng size/hash trước promote và chép kèm ba tài liệu này. Không có tự cập nhật, plugin ngoài, đọc cookie/trình duyệt hay nạp config yt-dlp của máy. Thay phiên bản phải rà lại source, checksum, license và kiểm thử Bilibili. FFmpeg dùng cơ chế kiểm tra/phân phối riêng của dự án.

Đây là ghi nhận nguồn gốc và cơ chế tích hợp, không thay thế rà soát pháp lý cho một bản phân phối mới chứa executable bên thứ ba.
