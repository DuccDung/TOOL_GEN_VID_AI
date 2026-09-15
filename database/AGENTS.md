# Hướng dẫn AI agent — database

> Danh sách migration hiện hành: [../BOI_CANH_HE_THONG_HIEN_HANH.md](../BOI_CANH_HE_THONG_HIEN_HANH.md). Rà soát source: 2026-09-15.

Áp dụng thêm `../AGENTS.md` và đọc `../VAN_HANH_VA_PHAT_HANH.md` trước mọi thay đổi hoặc thực thi SQL.

## Schema

- `auth`: Identity, session, device, license, payment và Data Protection keys.
- `ai`: organization, membership, credential, budget, reservation, ledger, audit và seat provisioning.
- `vf`: project/workflow, asset, provider catalog/model/rate/request, speech và render.
- `vs`: Vietsub project registry và job/batch/attempt Dịch Cloud; snapshot text/result tạm mã hóa có retention, media và dữ liệu biên tập vẫn ở workspace local.
- `social`: TikTok Developer App credential, OAuth connection/token và publish attempts/jobs theo user; video/path local không nằm trong SQL server.

## Migration

- `VideoFactory.Initial.sql` khởi tạo database.
- Migration versioned từ `4.0.0` đến `4.1.9` theo dependency trong runbook; những file trùng tiền tố số thuộc module khác nhau có mã `ai.SchemaVersions` riêng. Không suy từ version số lớn nhất rằng mọi module đã áp.
- `4.1.6` thêm job Cloud và FK `VietsubProjectId` cho budget/ledger; giữ FK video và CHECK đúng một loại project. Không rollback binary server cũ khi đã có reservation Vietsub.
- `4.1.0` tạo Vietsub registry; `4.1.1` tạo Scene First Frame; `4.1.2` lưu failure details; `4.1.3` thêm speech synchronization; `4.1.4` thêm bằng chứng duyệt voice profile; `4.1.5` thêm audited speech verification review.
- `4.1.6.TikTokPublishing` và `4.1.7.TikTokAdminCredentials` thêm publishing, credential và rollout TikTok. `4.1.8.LocalVoiceConsistency` thêm policy và trạng thái đồng nhất giọng Veo local. Các migration cùng tiền tố số thuộc module khác nhau có mã SchemaVersions riêng; không đổi tên hay ghi đè migration lịch sử.
- `Verify.VideoFactory.4.0.11.OrganizationSeatProvisioning.sql` là script kiểm tra, không phải migration version mới.
- `4.1.8` thêm nhiều tài khoản TikTok, account-bound OAuth, durable publish attempts và history snapshot; không đổi ConnectionId/token/job cũ. Không hạ về server một tài khoản sau khi phát sinh nhiều connection.
- `4.1.9.ShortVideoCharacterOutfit` tạo `vf.ShortVideoOutfits` và `vf.ShortVideoOperations` với mã `4.1.9-short-video-outfit`. Bảng operations còn giữ quote `TextOnly`; cờ phối trang phục tắt không cho phép binary hiện hành chạy trên schema thiếu 4.1.9. Desktop role bị DENY CRUD hai bảng, các thay đổi/duyệt đi qua server API.
- File 4.1.6/4.1.7 lip-sync Cloud được giữ để bảo toàn dữ liệu lịch sử; runtime đã loại bỏ. Không tự chạy, đổi tên, sửa hoặc xóa chúng khi cập nhật chuỗi migration hiện hành.
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
