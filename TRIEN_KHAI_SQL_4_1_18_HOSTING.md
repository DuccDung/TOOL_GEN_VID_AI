# Áp migration 4.1.18 trên SQL hosting

Ngày thực hiện: **2026-10-17**. Nhánh `main`, HEAD nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit, 356 đường dẫn thay đổi đầu lượt. Giữ nguyên source/migration và các thay đổi có sẵn; lượt này chỉ áp SQL đã rà soát và ghi biên bản vận hành.

**Đã áp duy nhất migration 4.1.18 và xác minh bằng kết nối mới. Lỗi thiếu cột `VerifiedPreparationId` không còn xuất hiện trong truy vấn release đã kiểm. Chưa smoke HTTP/UI hoặc deploy/bật luồng Setup dùng chung.**

## Đích và phạm vi

| Thông tin | Kết quả |
|---|---|
| SQL endpoint | `112.78.2.70` |
| Instance đã xác minh | `MB2D70\MSSQLSERVER2019` |
| Database | `tap51004_tool` |
| SQL version | `15.0.2000.5`, compatibility 150 |
| Migration | `database/VideoFactory.4.1.18.SharedDesktopBootstrapper.sql` |
| SHA-256 exact source | `61d38d14cf768802a2a4c5aabc665f09bd0d16de111aa75d00ffe844dd385689` |
| Commit migration, UTC của runner | `2026-10-16T21:33:47.0469882Z` |
| Trạng thái | `AppliedAndVerified` |

Preflight xác minh database ONLINE, không read-only, session không có transaction ngoài; 4.1.17 có một marker, 4.1.18 chưa có, chưa có hai bảng mới/cột mới. Release và artifact đều 0 dòng. Principal có quyền tạo bảng, ALTER schema/bảng và REFERENCES cần thiết. Dùng connection riêng, lock timeout 15 giây, deadlock priority LOW; SQL gốc tự quản lý transaction với `XACT_ABORT ON` và rollback khi lỗi. Không chạy lại 4.1.17 hoặc chuỗi migration khác.

User đã yêu cầu chạy SQL, rồi tiếp tục chỉ thị **“bạn hãy chạy sql cho tôi”** sau khi được thông báo backup/restore chưa xác minh. Thao tác được thực hiện theo chỉ thị tiếp tục đó. Bằng chứng phân biệt rõ **`UnverifiedBackupProceedAuthorized=true`**, **`BackupRestoreUserConfirmed=false`**, **`BackupRestoreIndependentlyVerified=false`**, **`HostingCloneRehearsalVerified=false`**. Không suy diễn chỉ thị thực thi thành xác nhận backup đã được restore thử.

Hosting có lịch sử full backup, các bản được đọc không có checksum và không có restore history quan sát được; principal có `BACKUP DATABASE=0`. Không tạo backup, restore hoặc nâng quyền trên hosting. Rehearsal SQL Server 2019 synthetic và restore QA ở lượt triển khai source trước là bằng chứng riêng, không thay backup/clone hosting.

## Kiểm tra sau commit

- `auth.AppReleaseArtifacts.VerifiedPreparationId`: `uniqueidentifier NULL`, 16 byte.
- `auth.DesktopReleasePreparations`: 31 cột; `auth.DesktopBootstrappers`: 19 cột. Verifier kiểm toàn bộ 51 cột mới/liên quan theo type/length/nullability.
- Marker `4.1.18-shared-desktop-bootstrapper`: đúng một dòng; marker 4.1.17 còn nguyên.
- 13 CHECK liên quan đều enabled/trusted; hai FK mới đúng cột/đích, enabled/trusted và NO ACTION; năm index cùng hai PK đúng cấu trúc.
- Trước/sau vẫn 0 release và 0 artifact. Fingerprint các trường dữ liệu cũ, FK/index cũ và quyền database **quan sát được** không đổi; migration không có GRANT/DENY/REVOKE.
- Verifier chạy lại bằng kết nối mới đạt. Một kết nối độc lập tiếp theo chạy phân trang `AppReleases` JOIN `AppReleaseArtifacts`, đọc `VerifiedPreparationId` cùng các trường protocol/source mới, đã compile/thực thi thành công và trả 0 dòng; không còn SQL error 207 trong truy vấn này.

Không khởi động hoặc dừng server/IDE người dùng, không sửa cấu hình/connection string, không bật feature flag, upload Setup/ZIP/model hoặc publish release. Không phát request AI có phí. Không chạy lại build/full test vì không sửa code hoặc SQL source; các kết quả kiểm thử source trước đó là lịch sử, ghi riêng trong [biên bản triển khai form/Setup](TRIEN_KHAI_FORM_RELEASE_TOI_GIAN_VA_SETUP_DUNG_CHUNG.md).

## Bằng chứng

Thư mục: `C:\vm-sql-verification\20261017-042709-hosting-418-342c18`.

- `preflight.json`, `backup-restore-readonly.json`: kiểm đích, schema, quyền và backup metadata trước thay đổi.
- `operation-before-user-proceed.json`: trạng thái chờ trước chỉ thị tiếp tục; `operation.json`: trạng thái áp và xác minh thực tế.
- `migration-exact.sql`: bản SQL khớp SHA nguồn; `Apply-HostingMigration418.ps1`: runner riêng, không chứa credential.
- `before.json`, `after.json`, `fresh-connection.json`: snapshot và đối chiếu dữ liệu/FK/index/quyền quan sát được.
- `Verify-Hosting418.sql`, `verification-summary.json`, `fresh-verification-summary.json`: xác minh schema sau commit và trên kết nối mới. Verifier SHA-256 `dbc76e6abb905c5f10761e43b80cc5e767c4fcff9077720a6e8f62547415f77f`.
- `verifier-private-qa.json`: thử cú pháp/assertion verifier trên SQL Server 2019 synthetic, chỉ thay guard target trong bản copy riêng; không coi là chạy verifier trên hosting.
- `release-query-verification.json`: truy vấn phân trang/JOIN release thực trên hosting sau áp, không phải smoke HTTP/UI.

Migration không còn là bước thiếu trên database này. Tiếp theo có thể tiếp tục chạy server và mở lại trang Phát hành; các bước đăng ký Setup, cấu hình API/profile/VPS và nghiệm thu Windows A/B vẫn theo runbook riêng. Không suy từ schema đã cập nhật sang Setup/model hoặc production rollout đã hoàn tất.
