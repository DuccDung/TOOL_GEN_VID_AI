# Hướng dẫn AI agent — database

Áp dụng thêm `../AGENTS.md` và đọc `../TRIEN_KHAI_AI_GATEWAY_TO_CHUC.md` trước mọi thay đổi hoặc thực thi SQL.

## Schema

- `auth`: Identity, session, device, license, payment và Data Protection keys.
- `ai`: organization, membership, credential, budget, reservation, ledger, audit và seat provisioning.
- `vf`: project/workflow, asset, provider catalog/model/rate/request và render.
- `vs`: Vietsub project registry; media/subtitle content vẫn ở workspace local.

## Migration

- `VideoFactory.Initial.sql` khởi tạo database.
- Migration versioned từ `4.0.0` đến `4.1.5` chạy theo thứ tự số trong runbook.
- `VideoFactory.DesktopLeastPrivilege.sql` chạy sau cùng để áp quyền desktop.
- Không sửa migration đã có khả năng được triển khai. Tạo file version mới và ghi version idempotent.

## Bất biến

- Organization ID và ownership phải theo mọi request/usage/resource tenant-scoped.
- Idempotency cloud nằm trong organization.
- Ledger, rate snapshot, provider request và credential version là dữ liệu đối soát; không cascade/xóa/sửa lịch sử tùy tiện.
- Budget `0` là khóa AI.
- Credential chỉ tồn tại dạng mã hóa và desktop role không được đọc.
- Retiring credential còn được giữ khi task đang chạy tham chiếu.
- Backfill phải hoàn tất trước `NOT NULL`/unique/FK mới.

## Quy tắc viết SQL

- Idempotent: kiểm tra schema/table/column/index/constraint/version trước tạo/đổi.
- Dùng `SET XACT_ABORT ON` và transaction cho thay đổi cần nguyên tử.
- Lỗi phải rollback và làm `sqlcmd -b` trả exit code khác 0.
- Không seed giá provider hoặc secret production.
- Khi thêm bảng mới, cập nhật EF server/local phù hợp, least-privilege và migration test.
- Không cấp quyền rộng schema `ai`, `auth` hoặc `dbo` cho desktop.

## An toàn thực thi

AI không tự chạy SQL thay đổi dữ liệu trên database thật. Cần người dùng xác nhận instance, database, backup đã restore thử và quyền tác động. Luôn chạy lặp trên clone, kiểm tra version/FK/index/row count rồi mới lập kế hoạch production.
