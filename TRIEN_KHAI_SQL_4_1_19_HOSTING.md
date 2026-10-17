# Áp SQL 4.1.19 trên hosting — bị chặn quyền, đã rollback

Cập nhật theo yêu cầu tiếp theo của người dùng: đã tạo nhánh [SQL 4.1.20 Simple](database/VideoFactory.4.1.20.DirectDesktopCatalogSimple.sql), không phân quyền và không phụ thuộc role, để khởi tạo cùng schema. [Biên bản Simple](TRIEN_KHAI_SQL_4_1_20_DON_GIAN.md). Nội dung lỗi và wrapper DBA bên dưới là lịch sử của nhánh 4.1.19; không cần chạy wrapper đó để khởi tạo bằng Simple.

Ngày thực hiện: **2026-10-17**. Nhánh `main`, HEAD nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit, 406 đường dẫn thay đổi đầu lượt. Lượt này không sửa migration, code, connection string hoặc cấu hình phát hành.

**Đã thực thi đúng SQL 4.1.19 trên hosting nhưng SQL Server trả lỗi 15151 tại lệnh DENY. Transaction đã rollback và được xác minh bằng kết nối mới. Bảng `auth.DesktopDirectReleases` cùng marker 4.1.19 vẫn chưa có. Chưa áp migration thành công.**

## Đích và đầu vào

| Thông tin | Đã xác minh |
|---|---|
| SQL endpoint | `112.78.2.70` |
| Instance | `MB2D70\MSSQLSERVER2019` |
| Database | `tap51004_tool` |
| Engine / compatibility | `15.0.2000.5` / `150` |
| Trạng thái database | ONLINE, không read-only |
| Prerequisite | Một marker `4.1.18-shared-desktop-bootstrapper`, bảng bootstrapper hiện hữu |
| Exact migration | `database/VideoFactory.4.1.19.DirectDesktopCatalog.sql` |
| SHA-256 | `ccda46d8a4863663d59656bff62e42767875a45731ac249d97f70560217acfe5` |
| Thời gian bắt đầu / kết thúc UTC | `2026-10-17T07:34:32.6802894Z` / `2026-10-17T07:34:35.1518140Z` |
| Kết quả | `FailedRolledBackVerified` |

User đã yêu cầu **“bạn hãy chạy sql cho tôi”** sau khi được chỉ rõ migration 4.1.19 trên database hosting. Việc tiếp tục khi backup chưa được xác minh độc lập trên cùng hosting đã được cho phép ở lượt SQL trước; lượt này thông báo lại giới hạn trước thực thi. Không coi yêu cầu thực thi là xác nhận backup hoặc restore đã đạt.

Đọc được lịch sử full backup mới nhất hoàn tất **2026-10-17T07:06:50 theo thời gian SQL Server**, không có backup checksum, `is_damaged=false`; có restore history ngày 2026-09-15 nhưng không chứng minh bản backup mới nhất đã restore thử. Tài khoản có `BACKUP DATABASE=0`; không tạo/restore backup hoặc nâng quyền trên hosting. Rehearsal SQL2019 synthetic ở lượt source trước không phải clone hosting.

## Lỗi quyền và bảo toàn dữ liệu

SQL Server trả **15151**, severity 16, state 1, dòng **106** của exact migration:

```sql
DENY SELECT,INSERT,UPDATE,DELETE
ON OBJECT::auth.DesktopDirectReleases TO VideoMakerDesktopRole;
```

Role có ID 5, tài khoản server không là thành viên role desktop. Tài khoản có CREATE TABLE, ALTER schema auth và INSERT SchemaVersions; không có CONTROL schema auth, CONTROL bảng đã tạo ở lượt trước, VIEW DEFINITION/ALTER role, ALTER ANY ROLE, db_owner hoặc db_securityadmin. Không thấy DENY schema trong metadata hiện được phép đọc; điều đó không chứng minh không có DENY đang bị ẩn. Không bỏ dòng bảo vệ hoặc sửa migration để vượt giới hạn quyền.

SQL gốc tự quản lý transaction với `XACT_ABORT ON`, TRY/CATCH và ROLLBACK. Runner dùng connection riêng, lock timeout 15 giây, deadlock priority LOW. Sau lỗi đã mở kết nối mới và đối chiếu:

- Marker 4.1.18 vẫn đúng một dòng; marker 4.1.19 vẫn 0; bảng mới không tồn tại.
- 0 release, 0 artifact, 1 preparation, 0 bootstrapper; hash tất cả dòng ở bốn bảng giữ nguyên, không xuất nội dung payload.
- Index, CHECK, FK cũ và permissions **quan sát được** ngoài bảng mới không đổi.
- Kết nối read-only tiếp theo tiếp tục xác nhận rollback. Không deploy, bật flag, upload/Active Setup hoặc publish release.

Chỉ có connection hiện hành cho đích này; không có SQL override quản trị bổ sung trong cấu hình liên quan. Không thử tự cấp quyền rộng, đổi ownership hoặc dùng identity khác ngoài phạm vi.

## Bước cần DBA/hosting thực hiện

[File DBA áp đúng 4.1.19](D:/vm-sql-verification/20261017-143113-hosting-419-ef14b7/DBA-Apply-Hosting-4.1.19.sql) đã được tạo cùng [hướng dẫn](D:/vm-sql-verification/20261017-143113-hosting-419-ef14b7/HUONG_DAN_DBA.md). File chứa nguyên migration đã chốt, guard đúng instance/database và verifier trong scope riêng; không cần chạy các migration cũ. DBA dùng tài khoản quản trị hiện có sau khi kiểm backup/restore; **không cấp db_owner/sysadmin cho tài khoản ứng dụng**.

Kết quả mong đợi sau khi DBA chạy: `Status=Verified`, 24 cột, 5 CHECK trusted, 4 index tính cả PK, một marker 4.1.19 và DENY CRUD cho desktop. Sau đó cần xác minh lại bằng tài khoản ứng dụng trước deploy/Active Setup/phát hành. Lượt này không thực hiện bước DBA trên hosting và không coi script đã chuẩn bị là migration đã áp.

Wrapper DBA có SHA-256 `398553ebf28bdc1019400259e8527e0ed384cf7a2c51cbfd41e46ce7a0521382`. Đã rehearsal wrapper trên database GUID mới của SQL2019 LocalDB, chỉ đổi guard đích trong bản QA: restore backup synthetic 4.1.17, áp exact 4.1.18 rồi chạy wrapper hai lần đều `Verified`; guard sai instance trả 51210 trước mutation. Fixture chủ động không có DENY schema để nhánh DENY object được thực thi: đủ bốn DENY, desktop CRUD=0, db_owner=0; 3 release/6 artifact, CHECK/quyền/membership cũ giữ nguyên. Database QA mới đã được xóa; instance và database cũ giữ nguyên. [Report QA wrapper](D:/vm-direct-catalog-sql/20261017-143820-dba-wrapper-d09084c2/report.json), SHA-256 `42b34e2bb3e6f8c157293b55d2cc1d19f655eb306e3c6fcfe0fbf29a0adf9fce`. Đây là kiểm chứng file bàn giao, không phải áp hosting.

## Bằng chứng

Gốc: `D:\vm-sql-verification\20261017-143113-hosting-419-ef14b7`.

- `preflight.json`: đích, prerequisite, quyền và lịch sử backup/restore chỉ đọc.
- `migration-exact.sql`, `Apply-Hosting419.ps1`: exact SQL và runner không chứa credential.
- `before.json`, `failure-fresh-state.json`: row hash, schema và quyền quan sát được trước/sau rollback.
- `operation.json`: lỗi SQL được làm sạch và trạng thái rollback thực tế.
- `Verify-Hosting419.sql`, `expected-checks-sql2019.json`: verifier và định nghĩa CHECK đã rehearsal; chưa được dùng làm bằng chứng migration hosting đã đạt.
- `DBA-Apply-Hosting-4.1.19.sql`, `HUONG_DAN_DBA.md`: gói hành động tiếp theo cho DBA.

Không chạy build/test ứng dụng mới vì không sửa source hoặc SQL migration. Kết quả full suite và rehearsal trước đó là lịch sử trong [biên bản triển khai DirectCatalog](TRIEN_KHAI_PHAT_HANH_TRUC_TIEP_TU_VPS.md), không phải nghiệm thu hosting trong lượt SQL này.
