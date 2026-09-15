# Ảnh kiểm tra OCR

Hai PNG 1000×200 dùng riêng để kiểm tra nhận dạng trong Setup; không chứa dữ liệu dự án.

- `en.png`: chữ “HELLO SUBVID”, Segoe UI Bold, 72 px.
- `zh.png`: chữ “中文字幕”, Microsoft YaHei Bold, 72 px.

Tạo bằng System.Drawing trên Windows với nền trắng, chữ đen, AntiAliasGridFit. Chỉ phân phối ảnh raster, không đóng gói hay phân phối font Windows. Kết quả đạt yêu cầu confidence ≥ 0,45 và chứa `SUBVID` / `文字幕`.
