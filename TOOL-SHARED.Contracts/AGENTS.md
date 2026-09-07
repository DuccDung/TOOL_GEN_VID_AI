# Hướng dẫn AI agent — TOOL-SHARED.Contracts

> Ngữ cảnh hệ thống liên module: [../BOI_CANH_HE_THONG_HIEN_HANH.md](../BOI_CANH_HE_THONG_HIEN_HANH.md). Cập nhật rà soát: 2026-09-06.

Áp dụng thêm `../AGENTS.md`.

Đây là hợp đồng public dùng chung giữa server và desktop. Giữ DTO đơn giản, không chứa EF entity, service logic, secret đã mã hóa hoặc kiểu chỉ tồn tại trong một UI.

## Quy tắc contract

- Thêm field mới theo hướng tương thích ngược khi có client cũ: ưu tiên nullable/default ở cuối record constructor.
- Không đổi tên/xóa field hoặc đổi ý nghĩa enum/string âm thầm; nếu bắt buộc, cập nhật server, desktop, tài liệu và test trong cùng thay đổi.
- Request generation phải mang `OrganizationId` khi ngữ cảnh không thể suy ra duy nhất.
- Response credential chỉ có trạng thái, version và secret hint; tuyệt đối không có API key/encrypted payload.
- Response output video/ảnh chỉ có URL proxy tương đối của server, không có provider output URL.
- Contract registry Vietsub chỉ mang metadata tối thiểu; không thêm subtitle text, media, model path hoặc local workspace path.
- Error response dùng code ổn định, message có thể thân thiện với người dùng.
- Không đặt giá mặc định hoặc provider credential trong contracts.

Sau thay đổi, tìm mọi nơi dùng tên DTO bằng `rg`, rồi build toàn solution; chỉ build project contracts không đủ chứng minh tương thích.

