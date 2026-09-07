# Hướng dẫn AI agent — TOOL-SHARED.Contracts

Áp dụng thêm `../AGENTS.md`.

## Phạm vi

Project này chỉ chứa DTO, enum/string constant và helper thuần dùng chung giữa server/desktop. Không thêm EF entity, HTTP client, secret handling, file I/O hoặc nghiệp vụ phụ thuộc môi trường.

## Tương thích

- Thêm field tùy chọn ở cuối positional record khi có thể.
- Không đổi tên/xóa field, đổi nullability hoặc đổi nghĩa field đã công khai nếu chưa cập nhật đồng thời server, desktop, frontend và test.
- Organization-scoped request phải có `OrganizationId` hoặc được route/context xác định rõ.
- Response không chứa plaintext/encrypted credential, Authorization, provider URL gốc, local absolute path hoặc raw provider payload.
- Mã trạng thái/error dùng constant ổn định; tránh magic string khác nhau giữa các project.
- Helper normalization phải deterministic, culture-safe và có test Unicode/whitespace.

## Quy trình

1. Sửa contract trước.
2. Cập nhật server producer/consumer.
3. Cập nhật desktop C# và TypeScript shape/message.
4. Thêm test serialization/compatibility và chạy toàn solution.
