# Hướng dẫn AI agent — database

> Danh sách migration hiện hành: [../BOI_CANH_HE_THONG_HIEN_HANH.md](../BOI_CANH_HE_THONG_HIEN_HANH.md). Cập nhật rà soát: 2026-09-07.

Áp dụng thêm `../AGENTS.md` và đọc `../VAN_HANH_VA_PHAT_HANH.md` trước mọi thay đổi hoặc thực thi SQL.

## Schema

- `auth`: Identity, session, device, license, payment và Data Protection keys.
- `ai`: organization, membership, credential, budget, reservation, ledger, audit và seat provisioning.
- `vf`: project/workflow, asset, provider catalog/model/rate/request, speech và render.
- `vs`: Vietsub project registry; media/subtitle content vẫn ở workspace local.

## Migration

- `VideoFactory.Initial.sql` khởi tạo database.
- Migration versioned từ `4.0.0` đến `4.1.5` chạy theo thứ tự số trong runbook.
- `4.1.0` tạo Vietsub registry; `4.1.1` tạo Scene First Frame; `4.1.2` lưu failure details; `4.1.3` thêm speech synchronization; `4.1.4` thêm bằng chứng duyệt voice profile; `4.1.5` thêm audited speech verification review.
- `Verify.VideoFactory.4.0.11.OrganizationSeatProvisioning.sql` là script kiểm tra, không phải migration version mới.
- `VideoFactory.DesktopLeastPrivilege.sql` chạy sau cùng để áp quyền desktop.
- Không sửa migration đã có khả năng được triển khai; tạo file version mới và ghi version idempotent.

## Bất biến

- Organization ID và ownership đi theo mọi request/usage/resource tenant-scoped.
- Idempotency cloud nằm trong organization.
- Ledger, rate snapshot, provider request và credential version là dữ liệu đối soát; không cascade/xóa/sửa lịch sử tùy tiện.
- Budget `0` là khóa AI. Credential chỉ tồn tại dạng mã hóa và desktop role không được đọc.
- Retiring credential được giữ khi task đang chạy tham chiếu.
- Backfill hoàn tất trước `NOT NULL`, unique hoặc FK mới.

## Quy tắc SQL

- Idempotent: kiểm tra schema/table/column/index/constraint/version trước tạo hoặc đổi.
- Dùng `SET XACT_ABORT ON` và transaction cho thay đổi cần nguyên tử; lỗi phải rollback và làm `sqlcmd -b` trả exit code khác 0.
- Không seed giá provider hoặc secret production.
- Khi thêm bảng, cập nhật EF server/local phù hợp, least-privilege và migration test.
- Không cấp quyền rộng schema `ai`, `auth`, `dbo` hoặc `vs` cho desktop.

## An toàn thực thi

AI không tự chạy SQL thay đổi dữ liệu trên database thật. Cần người dùng xác nhận instance, database, backup đã restore thử và quyền tác động. Luôn chạy toàn bộ chuỗi lặp trên clone, kiểm tra version/FK/index/row count rồi mới lập kế hoạch production.
