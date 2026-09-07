# Hướng dẫn AI agent — TOOL-TESTS

> Ngữ cảnh hệ thống và trạng thái test hiện hành: [../BOI_CANH_HE_THONG_HIEN_HANH.md](../BOI_CANH_HE_THONG_HIEN_HANH.md). Cập nhật rà soát: 2026-09-06.

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
- Vietsub: workspace/SQLite migration, source không đổi, OCR, cue manual/locked, job checkpoint/recovery, worker protocol/crash/timeout/cancel/process tree và privacy.

## Quy tắc test

- Bug fix phải có regression test tái hiện nhánh lỗi.
- Không gọi OpenAI/Kling thật trong unit test; dùng fake/mocked HTTP và deterministic time/ID khi có thể.
- Không phụ thuộc database/máy người phát triển cho unit test. Integration test SQL phải được đánh dấu và cấu hình tách biệt nếu bổ sung sau.
- Test Qwen thật và benchmark phải dùng category opt-in, kiểm model size/SHA-256 và resource floor trước khi spawn worker. Mặc định `Skipped` phải được báo riêng, không tính là model đã đạt.
- Khi chạy test media/model trên máy có ổ C gần đầy, dùng một thư mục TEMP/TMP đã xác minh nằm trong workspace/ổ D; không làm yếu free-space guard.
- Không chạy model thật song song với build, OCR/FFmpeg nặng hoặc worker khác; không dừng IDE/app của người dùng để lấy tài nguyên.
- Không làm test yếu đi chỉ để pass; nếu behavior nghiệp vụ đổi, cập nhật tài liệu và giải thích rõ.
- Test security phải kiểm tra kết quả bị từ chối xảy ra trước outbound provider call hoặc trước ghi ledger ngoài ý muốn.

Lệnh chuẩn sau khi đã build Release:

```powershell
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

