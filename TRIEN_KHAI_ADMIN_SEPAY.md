# Admin quản lý tài khoản nhận thanh toán SePay

Ngày triển khai source: 2026-10-11. Checkout `main`, nền commit `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit; giữ nguyên các thay đổi đăng ký và đóng gói desktop có sẵn.

## Phạm vi đã triển khai

- Mục **Thanh toán SePay** trong Admin: danh sách tài khoản, thêm/sửa, chọn nơi nhận tiền cho đơn mới, bật/tắt nhận thanh toán và sao chép URL webhook của server đang mở.
- Form có nhãn, giới hạn dữ liệu, ngân hàng khác bằng mã/tên tùy chỉnh, trạng thái tải/lưu, lỗi/thử lại, chống bấm lưu trùng và giữ bản nháp khi làm mới. Hủy bỏ bản nháp và lấy lại phiên bản mới nhất; phản hồi đến muộn sau đổi màn hình/đăng xuất không ghi đè UI.
- API `GET api/admin/payments/sepay`; `POST accounts`; `PUT accounts/{accountId}`, `selected-account`, `enabled` dưới cùng route. Tất cả yêu cầu JWT/session hợp lệ và role hệ thống `Admin` (Global Admin); role trong tổ chức không có quyền quản lý cấu hình này.
- SQL chứa `auth.SepayReceivingAccounts` và singleton `auth.SepayPaymentSettings`. Version GUID chống ghi đè, transaction Serializable, unique bank/account, FK/CHECK và audit thay đổi. Audit chỉ chứa ID/thao tác/trạng thái, không chứa tên chủ hoặc số tài khoản.
- Thêm/chọn tài khoản không tự bật thanh toán. Bật chỉ được khi đã chọn tài khoản hợp lệ. Cấu hình receiver và Enabled trong appsettings/user-secrets cũ không điều khiển runtime được đăng ký DI; QR endpoint, prefix và expiry vẫn lấy cấu hình server.
- Đơn mới snapshot tài khoản đang được chọn trong transaction. Đơn cũ giữ mã, số tiền, QR và tài khoản đã lưu; webhook xác minh dữ liệu theo snapshot ngay cả khi tài khoản nhận đơn mới đã đổi/sửa hoặc nhận đơn mới đã tắt.
- Theo yêu cầu sản phẩm, webhook không dùng key/chữ ký; không xác minh nguồn gửi. Giữ giới hạn 64 KiB/rate limit, kiểm transaction ID, tiền vào, account/code/exact amount và idempotency, cấp license/seat theo luồng hiện hành.
- Desktop không thêm UI cấu hình ngân hàng, SQL access hoặc bridge mới; các DTO checkout/status dùng chung hiện hành giữ nguyên.

## Task và bằng chứng

| Task | Trạng thái |
| --- | --- |
| Contracts, entity và EF mapping | Có source |
| Admin API, validation, revision, audit và transaction | Có source |
| UI/UX theo ảnh đã duyệt, responsive và xử lý bất đồng bộ | Có source; kiểm trình duyệt với API giả |
| Runtime payment dùng SQL và webhook dùng snapshot đơn | Có source; kiểm service và transaction SQLite |
| Migration SePay và cấu hình mặc định | Bản đơn giản 4.1.13 đã áp thành công trên `tap51004_tool`, xác minh bằng kết nối mới; regression SQL local 18 Passed |
| Restore/Release build, kiểm .NET/frontend/browser | Đạt kiểm tự động trên working tree này; kết quả bên dưới |
| Migration, smoke SePay và rollout server thật | SQL 4.1.13 đã áp; backup/restore chưa xác minh; chưa smoke/rollout server hoặc bật thanh toán |

Đã chạy trên working tree nêu trên:

- `dotnet restore TOOL_GEN_POST_VIDEO.slnx`: đạt.
- `dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore`: đạt, MSBuild 0 Warning / 0 Error; frontend build chạy qua MSBuild, còn cảnh báo Vite chunk >500 kB hiện có.
- `npm ci --no-audit --no-fund` và `npm test` trong `TOOL-LOCAL/Web`: 280 Passed / 0 Failed / 0 Skipped, 44 file.
- Nhóm `Payments`, `SepayReceivingAccountsMigrationTests`, `OrganizationProvisioningUiTests`: 93 Passed / 0 Failed / 0 Skipped; gồm 20 ca SePay mới. TRX `artifacts/sepay-admin-20261011/sepay-focused-final.trx`.
- Browser `admin-sepay.browser.test.cjs` và `admin-shell.browser.test.cjs`: 18 Passed / 0 Failed / 0 Skipped. Dùng Playwright có sẵn tại `C:/Users/Admin/AppData/Local/npm-cache/_npx/e41f203b7505f1fb/node_modules/playwright-core` qua `VIDEOMAKER_PLAYWRIGHT_MODULE`; kiểm các độ rộng 1920, 1440, 1024, 768 và 390 px, lỗi/thử lại, conflict, duplicate submit, draft, late response/reset, ngân hàng khác, clipboard và escape dữ liệu. Đã xem ảnh desktop/mobile; không tràn ngang trang.
- `dotnet test TOOL-TESTS/TOOL-TESTS.csproj -c Release --no-build --logger 'trx;LogFileName=sepay-admin-final.trx' --results-directory artifacts/sepay-admin-20261011`: **1.644 Passed / 0 Failed / 13 Skipped / 1.657 Total**, 4 phút 55 giây. Model/SQL opt-in bị skip không tính là đã nghiệm thu. Không coi các lượt kiểm tra trước khi sửa xong là nghiệm thu cuối; TRX final giữ riêng các lượt phát hiện lỗi đã sửa.

## Rollout trên môi trường được xác nhận

1. Xác định server/IIS folder, instance/database, quyền ứng dụng, backup và restore trước thao tác SQL. Không khởi động binary mới chỉ để kiểm schema vì startup có bootstrap ghi database.
2. Rehearsal `database/VideoFactory.4.1.13.SepayReceivingAccountsSimple.sql` hai lần trên clone được duyệt sau baseline hiện hành. Theo yêu cầu mới, file chỉ tạo bảng/cấu hình, thay bước khởi tạo 4.1.10/4.1.12 và giữ các file cũ bất biến. Xác minh bảng, singleton SettingsId=1, CHECK, FK, unique bank/account, version `4.1.10-sepay-receiving-accounts`/`4.1.13-sepay-receivers-simple` và quyền đọc/ghi của server. Không có thao tác cấp/thu hồi quyền hoặc kiểm tra role; các quyền database đã có giữ nguyên.
3. Sau phê duyệt môi trường, áp migration và triển khai server. Migration không seed ngân hàng, không bật thanh toán, không chép user-secrets và không ghi đè cấu hình đã có khi chạy lại.
4. Global Admin mở **Thanh toán SePay**, thêm tài khoản đã liên kết SePay, đối chiếu tên/số tài khoản, chọn tài khoản cho đơn mới. Ngân hàng khác phải dùng mã QR đúng của ngân hàng, không suy ra từ tên hiển thị.
5. Dashboard SePay: bật webhook tiền vào, JSON, URL HTTPS `/api/payments/sepay/webhook` của môi trường đích và cấu hình mã thanh toán tương ứng prefix server. Luồng này không dùng key/chữ ký theo yêu cầu đã chốt.
6. Kiểm trên local/staging được xác nhận: bật/tắt nhận đơn mới, QR, sai account/code/amount, webhook lặp, đổi/sửa tài khoản với đơn cũ đang chờ, tổ chức hết chỗ và retry provisioning. Script simulator hiện hành có thể dùng theo runbook nhưng không gửi vào server dùng chung/production khi chưa được phép.
7. Xác minh gói Active/Public, giá VND, thời hạn, mapping pool, seat capacity và readiness tổ chức. Chỉ bật **Nhận thanh toán** sau nghiệm thu môi trường.

## Giới hạn xác minh

Kiểm thử browser dùng Razor/CSS/JavaScript thực cùng API giả, không chạy server bootstrap hay gửi webhook vào server thật. SQLite kiểm transaction/rollback, không thay rehearsal SQL Server. Các bài SQL/model opt-in bị skip không phải bằng chứng đã nghiệm thu. Đã kiểm metadata database production như mục bên dưới; chưa đọc cấu hình runtime server, chưa liên kết tài khoản ngân hàng, chưa bật SePay hoặc publish/deploy production.

Artifacts kiểm thử và ảnh UI nằm trong `artifacts/sepay-admin-20261011`; ảnh chụp dùng dữ liệu giả và origin `admin.example.test`.

## Preflight SQL production — 2026-10-11

Người dùng yêu cầu áp migration lên production. Đã kết nối bằng SQL authentication qua kết nối mã hóa, nhập mật khẩu vào bộ nhớ qua prompt ẩn; không lưu connection string/credential vào repository. Rà soát trên cùng nhánh/commit nền và working tree nêu ở đầu tài liệu.

- Target đã xác minh: database `tap51004_tool`, instance thực `MB2D70\MSSQLSERVER2019`, SQL Server 2019 Express `15.0.2000.5`; database `ONLINE`, `READ_WRITE`, recovery `FULL`. Mốc SQL kiểm quyền: `2026-10-10T17:42:03.1857021Z` (ngày 11/10 theo múi giờ làm việc).
- Có `auth.LicensePayments`, `ai.SchemaVersions`, version nền thanh toán `4.0.10-license-sepay-payments` và provisioning `4.0.11-organization-seat-provisioning`. Hai bảng SePay mới chưa tồn tại; danh sách version chưa có `4.1.10-sepay-receiving-accounts`. Lịch sử production có thêm migration của các module khác; không sửa/xóa hoặc chạy lại chúng.
- Tài khoản có `CREATE TABLE`, `ALTER`/`REFERENCES` schema `auth` và `INSERT` bảng version. Không có `BACKUP DATABASE`, không thuộc `sysadmin`/`db_owner`/`dbcreator`, không có `CONTROL DATABASE`. Quyền thực thi toàn bộ migration (bao gồm DENY) chưa kết luận.
- `msdb` có lịch sử full backup ngày 10/10/2026: ID `494127` hoàn tất lúc `05:50:32`, ID `493981` lúc `02:13:56` theo giờ ghi trên server; đều không ghi checksum. Lịch sử này không chứng minh file còn đọc được hoặc đã restore thử. Query restore history cho backup của database đích không có bản ghi.
- Thử `RESTORE VERIFYONLY` với tệp backup được ghi trong metadata thất bại: SQL error `262`, thiếu quyền `CREATE DATABASE` trong `master`. Không thực hiện restore dữ liệu.
- Một kết nối riêng để kiểm cú pháp `PARSEONLY` bị lỗi mạng `Win32Exception: The specified network name is no longer available` trước khi có kết quả; không tính kiểm cú pháp trên đích là Passed.
- SHA-256 migration được rà soát: `CE7B5664DAAFB5E174E66C6AF8C210450404E1CEEA438ABAB9C9571A8491107B`.

**Chưa thực thi migration, chưa thay đổi schema/dữ liệu production.** Đã yêu cầu xác nhận bản backup đã restore thử theo `database/AGENTS.md`; cần quản trị hosting hỗ trợ backup/restore rehearsal hoặc cung cấp bằng chứng tương ứng trước bước ghi SQL. Không chạy lại build/test source trong lượt kiểm môi trường này.

## Thực thi production theo yêu cầu tiếp tục — 2026-10-11

Sau thông báo về quyền backup/restore chưa xác minh, người dùng tiếp tục yêu cầu chạy SQL. Đã thực thi nguyên file migration với SHA-256 nêu trên; không sửa hoặc bỏ bước bảo vệ quyền desktop. Kết nối kiểm lại đúng `MB2D70\MSSQLSERVER2019 / tap51004_tool`, schema nền có đủ, principal thực thi không thuộc `VideoMakerDesktopRole`. Mốc bắt đầu theo SQL UTC: `2026-10-10T17:48:43.7579250Z`.

**Kết quả: Failed, migration chưa áp thành công.** SQL error `15151` tại dòng 59, bước `DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[auth].[SepayReceivingAccounts] TO [VideoMakerDesktopRole]`: không có quyền trên đối tượng để đặt DENY. Migration đã rollback transaction; kiểm cùng kết nối ghi nhận `OpenTransactions=0`, cả hai bảng mới không tồn tại và version migration có 0 dòng.

Kết nối mới kiểm lại lúc `2026-10-10T17:49:30.2377508Z`: vẫn không có hai bảng/version mới; `auth.LicensePayments` vẫn có 3 dòng. Quyền thực thi: `db_ddladmin=1`, `db_securityadmin=0`, `db_owner=0`, `CONTROL` và `TAKE OWNERSHIP` schema `auth` đều bằng 0. Principal có quyền DDL và CRUD nhưng thiếu quyền đặt DENY; không tự nâng quyền hoặc bỏ DENY để chạy tiếp.

Cần quản trị SQL/hosting thực thi cùng file bằng principal có quyền bảo mật phù hợp trên schema `auth` và quyền ghi `ai.SchemaVersions`. Backup/restore rehearsal vẫn chưa xác minh. Chưa deploy server, chưa bật thanh toán, chưa gửi webhook hoặc chạy lại build/test source trong lượt thao tác SQL này.

## Bản sửa SQL cho tài khoản migration hạn chế — 2026-10-11

- Thêm `database/VideoFactory.4.1.12.SepayReceivingAccountsPermissions.sql`, giữ nguyên SHA-256 file 4.1.10. DDL/seed tương thích với 4.1.10; không ghi đè receiver, Enabled, selection hoặc revision. Ghi cả version nền 4.1.10 và version sửa 4.1.12 khi thành công.
- Chỉ tái sử dụng bảo vệ khi xác minh được đủ DENY SELECT/INSERT/UPDATE/DELETE trên schema `auth` hoặc cả hai object. Nếu cần đặt DENY mới, principal phải đủ quyền. Kiểm tra visibility/protection trước CREATE TABLE; kiểm lại bảo vệ trong transaction trước ghi version/commit. Không bắt rồi bỏ qua lỗi DENY, không impersonate hoặc thêm db_owner.
- `Prepare.SepayReceivingAccountsPermissions.sql` là template dành cho DBA: xác minh đúng database/user, đặt bảo vệ schema auth và cấp VIEW DEFINITION trên role/schema cho migration user. Template không chứa secret. SQL 2019 có thể ẩn role/perms khỏi db_ddladmin; không suy từ 0 dòng metadata rằng DENY chưa tồn tại. Theo [Microsoft về quyền SQL](https://learn.microsoft.com/en-us/sql/relational-databases/security/permissions-database-engine?view=sql-server-ver17), quyền bị từ chối tại securable chứa đối tượng tham gia kiểm quyền; [metadata permission](https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-database-permissions-transact-sql?view=sql-server-ver17) còn phụ thuộc visibility của principal.
- Gói DBA đã điền target/user được xác minh, không có mật khẩu: `artifacts/sepay-admin-20261011/production/Apply-SePay-Permissions-Fix.sql`. Guard yêu cầu đúng database và instance production nêu trên; preparation và migration dùng chung outer transaction, lỗi rollback cả metadata grants/bảo vệ/schema. Dành cho quản trị hosting chạy một lần sau backup/restore verification; chưa thực thi gói DBA trên production bằng principal có quyền quản trị.

Đã chạy `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-SepayReceiverMigration.ps1`: **13 Passed / 0 Failed / 0 Skipped**, private LocalDB SQL Server 2019, dữ liệu giả. Kiểm backup CHECKSUM/VERIFYONLY và restore/CHECKDB thật trên baseline giả; tái hiện lỗi 15151; migration với principal chỉ DDL/CRUD; rerun giữ cấu hình; desktop bị từ chối 8 thao tác CRUD; hidden/missing/partial protection dừng trước DDL; fallback DBA/object DENY; rollback version thất bại; gói DBA commit/rollback nguyên tử; production target guard từ chối instance/database khác. Instance/tệp test riêng được dọn sau kiểm; không dừng SQL/app của người dùng. Bằng chứng local này không thay backup/restore production.

Production: thử migration 4.1.12 lúc `2026-10-10T18:06:07.4750961Z`, bị chặn **SQL 51124** ở preflight do role metadata không đọc được. Kiểm sau lỗi: hai bảng mới đều không tồn tại, hai version có 0 dòng, `OpenTransactions=0`. Query riêng xác nhận migration user không thuộc desktop role và `VIEW DEFINITION` role bằng 0. Chưa xác minh schema DENY đang có hay chưa vì metadata bị ẩn; cần DBA chạy gói chuẩn bị/triển khai nêu trên.

Lượt source mới trên cùng nhánh/commit nền và working tree sau bản sửa SQL: `dotnet restore` đạt; Release solution build đạt **0 Warning / 0 Error** MSBuild (Vite còn cảnh báo chunk hiện có); `npm ci` và frontend **280 Passed / 0 Failed / 0 Skipped**. `dotnet test TOOL-TESTS/TOOL-TESTS.csproj -c Release --no-build --logger 'trx;LogFileName=sepay-permissions-fix-final.trx' --results-directory artifacts/sepay-admin-20261011`: **1.645 Passed / 0 Failed / 13 Skipped / 1.658 Total**, 4 phút 22 giây; gồm regression tĩnh mới cho preflight/bảo vệ/transaction của 4.1.12. Không tính các SQL/model opt-in bị skip là nghiệm thu. Browser không chạy lại trong lượt sửa SQL này; số browser trước đó chỉ thuộc lượt UI đã ghi riêng phía trên. Không coi regression LocalDB hoặc test tự động là rollout production.

Lượt chạy lại theo yêu cầu người dùng lúc `2026-10-10T18:13:53.2348212Z`: xác minh đúng instance/database, SHA-256 migration/gói DBA không đổi. Quyền vẫn `sysadmin=0`, `db_owner=0`, `db_securityadmin=0`, `CONTROL auth=0`, `VIEW DEFINITION` desktop role bằng 0. Chạy migration 4.1.12 lại vẫn **Failed: SQL 51124** trước DDL. Kiểm sau lỗi: `ReceiverTableExists=0`, `SettingsTableExists=0`, `SepayVersionRows=0`, `OpenTransactions=0`; không có thay đổi schema/dữ liệu. Không tự nâng quyền hoặc bỏ bảo vệ; cần principal quản trị thực thi gói DBA, hoặc hosting thiết lập protection/metadata visibility trước khi principal hạn chế chạy lại. Không chạy lại build/test source trong lượt vận hành này.

## File SQL đơn giản theo yêu cầu mới — 2026-10-11

Người dùng xác nhận không cần CONTROL schema/role hoặc thao tác phân quyền trong file SQL và yêu cầu tạo lại file. Trên checkout `main`, nền commit `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit, đã thêm [VideoFactory.4.1.13.SepayReceivingAccountsSimple.sql](database/VideoFactory.4.1.13.SepayReceivingAccountsSimple.sql). Đây là file hiện được chọn để khởi tạo SePay; dùng thay bước 4.1.10/4.1.12 và gói DBA cũ.

- DDL hai bảng giữ nguyên so với 4.1.10, khớp entity/EF hiện hành: PK, unique bank/account, CHECK, FK, revision và timestamp. Không thay code ứng dụng hoặc DTO.
- File không có GRANT/DENY/REVOKE, CONTROL, truy vấn role hoặc metadata permission. Không cấp, thu hồi hay thay đổi các quyền database hiện có.
- Chỉ seed singleton SettingsId=1 khi chưa có, mặc định Enabled=0 và chưa chọn tài khoản; không seed ngân hàng. Chạy lại giữ cấu hình/tài khoản/revision/timestamp đã lưu.
- Ghi version nền `4.1.10-sepay-receiving-accounts` và `4.1.13-sepay-receivers-simple` idempotent; không ghi `4.1.12-sepay-receiver-permissions`. Kiểm ràng buộc trong transaction trước version/commit; lỗi rollback nguyên tử.
- File 4.1.10 và 4.1.12 không đổi SHA-256. File mới có SHA-256 `0741F792BE5236F6B7260D79E176859E90FC7E511AF7E9B6D996A79A019520C3`.

Đã chạy `scripts/Test-SepayReceiverMigration.ps1`: **18 Passed / 0 Failed / 0 Skipped**, private SQL Server 2019 LocalDB/dữ liệu giả. Gồm 13 ca nhánh cũ và 5 ca mới: principal DDL/CRUD không có CONTROL, role metadata bị ẩn vẫn tạo được bảng và không sửa quyền; rerun giữ tài khoản/Enabled/revision/timestamp; các ràng buộc từ chối dữ liệu sai; rollback toàn bộ khi version cuối bị lỗi; chạy trên schema đã có không sửa DENY object hoặc version cũ. Backup CHECKSUM/VERIFYONLY, restore và CHECKDB chỉ trên fixture giả. Đã kiểm DDL khớp bản gốc, PowerShell parse và `git diff --check` đạt.

`dotnet restore` và Release solution build đạt, MSBuild **0 Warning / 0 Error**; Vite còn cảnh báo chunk hiện có. Full .NET: **1.645 Passed / 0 Failed / 13 Skipped / 1.658 Total**, 4 phút 55 giây, TRX `artifacts/sepay-admin-20261011/sepay-simple-sql-final.trx`; SQL/model opt-in bị skip không tính là đã nghiệm thu. Không chạy lại test frontend/browser trong lượt chỉ sửa SQL/script/tài liệu này. Tại thời điểm tạo file, chưa chạy 4.1.13 trên production; lần áp tiếp theo được ghi riêng bên dưới. Kết quả LocalDB không xác minh backup/restore, runtime hay quyền trên production.

## Áp file SQL đơn giản lên production — xác minh 2026-10-11

Người dùng yêu cầu chạy file mới lên database production. Trên cùng `main`, nền commit `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit, đã kiểm lại đúng instance `MB2D70\MSSQLSERVER2019`, database `tap51004_tool`, SQL Server 2019 Express `15.0.2000.5`, trạng thái ONLINE/READ_WRITE và principal không thuộc desktop role. Kết nối mã hóa; mật khẩu nhập qua prompt ẩn vào bộ nhớ, không ghi connection string/credential vào source, log hoặc tài liệu.

Preflight xác nhận principal có CREATE TABLE, ALTER/REFERENCES/SELECT/INSERT schema `auth`, SELECT/INSERT `ai.SchemaVersions`; các quyền này đủ cho file đơn giản. Hai bảng mới chưa có, hai version SePay cần ghi đều chưa có. `auth.LicensePayments` có 3 dòng và có 29 version của các migration khác. Backup metadata vẫn có full backup ID 494127/493981; không có checksum hoặc recorded restore. `BACKUP DATABASE=0`, `RESTORE VERIFYONLY` bị từ chối SQL 262/3013. Giới hạn backup/restore đã được nêu trong các lượt trước và nhắc lại trước khi áp; không coi lịch sử backup là bằng chứng restore thành công.

Đã thực thi nguyên `VideoFactory.4.1.13.SepayReceivingAccountsSimple.sql`, SHA-256 `0741F792BE5236F6B7260D79E176859E90FC7E511AF7E9B6D996A79A019520C3`. **Kết quả: Applied, đã commit và xác minh bằng kết nối mới.** Mốc version `AppliedAtUtc` đọc trực tiếp dạng ISO từ SQL: `2026-10-10T18:42:59.425Z` (01:42:59 ngày 11/10, UTC+7). Lượt đọc riêng tiếp theo lúc `2026-10-10T18:44:38.8849393Z` xác nhận:

- Có `auth.SepayReceivingAccounts` và `auth.SepayPaymentSettings`, tổng 14 column khớp schema hiện hành.
- Có 2 primary key, 1 unique bank/account index hoạt động, 5 CHECK và 1 FK đều Enabled/Trusted.
- Singleton có đúng 1 dòng SettingsId=1, Enabled=0, SelectedAccountId=NULL; receiving account count=0.
- Version `4.1.10-sepay-receiving-accounts` và `4.1.13-sepay-receivers-simple` mỗi mã có đúng 1 dòng. Không ghi version 4.1.12 quản lý quyền.
- `OpenTransactions=0`; số payment cũ vẫn 3 và số version khác vẫn 29. Không thực hiện GRANT/DENY/REVOKE hoặc sửa credential.

Lượt này chỉ áp SQL và cập nhật biên bản; không chạy lại build/test source, không deploy/khởi động server, gửi webhook hoặc bật SePay. SQL production đã áp; backup/restore, runtime/UI production và thanh toán thật vẫn chưa xác minh. Cấu hình ngân hàng do Global Admin thêm/chọn và bật sau khi bản server tương ứng đã được triển khai và kiểm môi trường.

## Áp SQL đơn giản vào DUNGDEV / VideoFactory — 2026-10-14

Theo yêu cầu trực tiếp của người dùng sau kiểm tra bảng thiếu, đã áp nguyên `database/VideoFactory.4.1.13.SepayReceivingAccountsSimple.sql` vào **DUNGDEV / VideoFactory**, Windows Authentication, command timeout 180 giây. Nhánh `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit; giữ thay đổi có sẵn. SHA-256 migration vẫn `0741F792BE5236F6B7260D79E176859E90FC7E511AF7E9B6D996A79A019520C3`; không sửa SQL hoặc logic ứng dụng.

Preflight đọc xác nhận đúng instance/database, ONLINE/READ_WRITE, đủ `auth.LicensePayments`/`ai.SchemaVersions`, hai bảng SePay chưa có và principal đủ quyền backup. Đã thực hiện:

- Backup mới `COPY_ONLY, CHECKSUM, COMPRESSION`, **6.657.536 byte**, backup ID **2037**. [Backup được giữ lại](<D:/SQL2019/Microsoft SQL Server/MSSQL15.MSSQLSERVER/MSSQL/Backup/VideoFactory_pre_sepay_simple_20261014_120841_4af320d3.bak>).
- `RESTORE VERIFYONLY WITH CHECKSUM` đạt; restore thật sang `VideoFactory_Sepay_Rehearsal_20261014_120841_4af320d3`; `DBCC CHECKDB` đạt trước/sau migration.
- Chạy migration **hai lần trên bản restore**: schema/ràng buộc/version đạt, lần hai giữ revision/timestamp singleton; số dòng mọi bảng có sẵn (ngoài version) và fingerprint quyền giữ nguyên.
- Áp file trên database chính trong outer transaction Serializable, kiểm schema/số dòng cũ/quyền trước commit. **Applied** lúc `2026-10-14T12:08:45.6061494Z` (**19:08:45 UTC+7**).
- Kiểm bằng kết nối độc lập: hai bảng đủ **14 cột**, **2 PK, 1 unique index, 5 CHECK trusted, 1 FK trusted**; hai version nền 4.1.10 và nhánh 4.1.13 mỗi mã một dòng. Singleton có 1 dòng, `Enabled=0`, `SelectedAccountId=NULL`, receiver count=0; `OpenTransactions=0`.
- Fingerprint quyền trước/sau cùng SHA-256 `AD683D5986D2618EC6701BECE961D8F9D1434F9996EC7863C9B2044C9B3BCB90`. Không có GRANT/DENY/REVOKE hoặc gate role/CONTROL trong file được chạy.
- Database rehearsal đã dọn sau khi kiểm đúng tên và đường dẫn hai file riêng. Backup giữ trong thư mục backup SQL Server.

Đối chiếu lại source server hiện hành, gồm mapping partial/video ngắn và Vietsub Cloud: **87/87 bảng mapped đều có**, danh sách bảng thiếu rỗng; database có tổng **105 bảng**. Kết quả này xác minh sự tồn tại bảng, không thay smoke runtime hoặc kiểm toàn bộ cột ở các module khác.

Bằng chứng:

- [applied-20261014_120841_4af320d3.json](output/sepay-dungdev-20261014/applied-20261014_120841_4af320d3.json)
- [postflight.json](output/sepay-dungdev-20261014/postflight.json)
- [script vận hành giới hạn DUNGDEV](output/sepay-dungdev-20261014/Apply-SepaySimple-Dungdev.ps1)

Lượt này chỉ thao tác SQL và ghi biên bản; không chạy lại build/test source, đổi cấu hình kết nối, deploy/restart server, thêm tài khoản ngân hàng, bật thanh toán hoặc gửi webhook/provider request. Database hosting không thay đổi. **SQL DUNGDEV đã áp, backup/restore và schema SePay đã kiểm chứng; runtime/UI và thanh toán thật chưa xác minh.**
