# Hướng dẫn AI agent — database

Áp dụng thêm `../AGENTS.md`.

## Vai trò các script

- `VideoFactory.Initial.sql`: bootstrap đầy đủ cho database mới và chứa các bước nâng cấp lịch sử cần thiết.
- `VideoFactory.4.0.0.OrganizationAiGateway.sql`: migration idempotent sang AI Gateway theo tổ chức.
- `VideoFactory.DesktopLeastPrivilege.sql`: tạo role quyền tối thiểu cho desktop trong giai đoạn còn truy cập workflow trực tiếp.

Schema chính:

- `auth`: Identity, session, device, license và Data Protection keys.
- `ai`: organization, membership, credential version, budget period, reservation, usage ledger và audit.
- `vf`: project/workflow, provider catalog/model/rate và provider request log.

## Bất biến dữ liệu

- `OrganizationId` phải đi cùng project/provider request/budget/usage để truy vết tenant.
- Idempotency generation là duy nhất trong phạm vi organization, không phải toàn hệ thống.
- Ledger và rate snapshot là dữ liệu đối soát; không cascade delete hoặc cập nhật lại chi phí lịch sử.
- Credential payload chỉ tồn tại dạng mã hóa trên server; desktop role không được đọc schema/bảng chứa secret.
- Migration legacy tạo `legacy-default` với budget `0` để không phát sinh chi phí ngoài ý muốn.
- Không xóa version credential `Retiring` khi còn task Kling đang chạy tham chiếu.

## Quy tắc viết migration

- Script phải idempotent: kiểm tra schema/table/column/index/constraint/version trước khi tạo hoặc đổi.
- Dùng transaction và `XACT_ABORT ON` cho nhóm thay đổi cần nguyên tử; lỗi phải rollback và trả exit code cho `sqlcmd -b`.
- Backfill trước khi đặt `NOT NULL` hoặc unique constraint.
- Kiểm tra/tránh tên index/constraint cũ trước khi tạo uniqueness mới.
- Không tự điền đơn giá provider; rate production do Global Admin nhập từ hợp đồng/dashboard hiện hành.
- Không thay trực tiếp migration đã có thể được chạy ở production. Tạo migration phiên bản mới và cập nhật `ai.SchemaVersions`.
- Giữ script least-privilege đồng bộ khi thêm bảng/view/procedure desktop thực sự cần.

## An toàn chạy SQL

AI không được tự chạy các script này trên database thật. Trước khi chạy cần người dùng chỉ rõ instance/database, xác nhận backup đã restore thử và cho phép cửa sổ bảo trì. Thứ tự chuẩn:

```powershell
sqlcmd -S <server> -d VideoFactory -E -b -i database\VideoFactory.Initial.sql
sqlcmd -S <server> -d VideoFactory -E -b -i database\VideoFactory.4.0.0.OrganizationAiGateway.sql
sqlcmd -S <server> -d VideoFactory -E -b -i database\VideoFactory.DesktopLeastPrivilege.sql
```

Ưu tiên test migration trên bản sao có dữ liệu gần production, chạy lặp lại để chứng minh idempotency, rồi đối chiếu row count, FK/index và `ai.SchemaVersions`.

