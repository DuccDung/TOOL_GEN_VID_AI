# Hướng dẫn AI agent — TOOL-TESTS

Áp dụng thêm `../AGENTS.md`.

Test dùng xUnit trên `net10.0-windows` và có quyền truy cập internal của server/desktop qua `InternalsVisibleTo`.

## Ưu tiên kiểm thử

- RBAC: role dương và role bị chặn; bảo vệ Owner cuối cùng.
- Cross-organization/cross-user/project ownership.
- Budget: reserve đồng thời, member limit, settle/release/reconciliation và rate snapshot.
- Idempotency: replay cùng payload và conflict khác payload.
- Credential: protect/unprotect, test-before-rotate, version retirement và không lộ secret.
- Provider outbound: HTTPS/host/port allowlist.
- Kling proxy: loopback/private/reserved IPv4/IPv6, redirect, DNS pinning, MIME và size.
- Desktop: không có đường lưu/gọi provider trực tiếp; legacy cleaner chỉ xóa đúng hai file.
- Update: checksum, size, backup và rollback.

## Quy tắc test

- Bug fix phải có regression test tái hiện nhánh lỗi.
- Không gọi OpenAI/Kling thật trong unit test; dùng fake/mocked HTTP và deterministic time/ID khi có thể.
- Không phụ thuộc database/máy người phát triển cho unit test. Integration test SQL phải được đánh dấu và cấu hình tách biệt nếu bổ sung sau.
- Không làm test yếu đi chỉ để pass; nếu behavior nghiệp vụ đổi, cập nhật tài liệu và giải thích rõ.
- Test security phải kiểm tra kết quả bị từ chối xảy ra trước outbound provider call hoặc trước ghi ledger ngoài ý muốn.

Lệnh chuẩn sau khi đã build Release:

```powershell
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

