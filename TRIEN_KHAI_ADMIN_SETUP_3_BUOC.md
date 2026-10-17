# Triển khai Admin thiết lập mở bán 3 bước

Rà soát và triển khai ngày 2026-10-14 trên nhánh `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b` cùng working tree chưa commit. Giữ các thay đổi có sẵn. Biên bản ghi source, kiểm thử cô lập và thao tác SQL theo từng môi trường; trạng thái vận hành mới nhất xem mục cuối. Chưa xác minh smoke server, thanh toán thực hoặc rollout production.

## Giao diện và cách dùng

Trong **Tổ chức & AI**, chọn **Thiết lập mở bán**. Menu **Quản lý AI & tổ chức** giữ danh sách tổ chức, phân bổ khách hàng, catalog/rate, cách tính chi phí, thành viên, usage và nhật ký.

1. **Kết nối AI:** chọn tổ chức có membership Owner/OrganizationAdmin hoặc nhập tên tổ chức mới. Kiểm tra và lưu OpenAI, Fal, chọn Veo Standard/Fast. OpenAI Text/Image hiển thị model mặc định thực tế toàn hệ thống; wizard không tạo lựa chọn OpenAI riêng cho tổ chức. Provider/model đang tắt cần được bật tại quản lý nâng cao. Tổ chức mới có ngân sách 0, creator là Owner.
2. **Chi phí & khách hàng:** năm giá cho Text input/output, Image input/output và Veo theo giây. Prefill giá hiện có; chuyển đơn vị token chỉ để hiển thị, không tạo rate mới nếu giá tương đương. Thay giá cần xác nhận phạm vi toàn hệ thống và giá cũ/mới. Đặt ngân sách USD, hạn mức mỗi khách hoặc chọn dùng chung, sức chứa. Hệ thống chuẩn bị policy và nhóm, nhóm mới giữ Inactive/auto-off.
3. **Tạo gói & mở bán:** nhập tên, VND, số ngày, số thiết bị, mô tả/quyền lợi. Kiểm tra AI, budget còn lại, quota, policy, chỗ trống, phiên bản cấu hình và SePay. Có thể lưu bản nháp thiết lập hoặc tạo gói nháp inactive/private. Mở bán kích hoạt nhóm và tạo gói/mapping/receipt trong cùng transaction. **Tạo gói khác** dùng lại tổ chức và vào bước 3.

**Thanh toán SePay** là trang riêng. Wizard chỉ đọc readiness và dẫn sang trang này, lưu nháp trước khi đi; quay lại bằng menu hoặc Browser Back tải lại readiness. Không có form ngân hàng/webhook trong ba bước. Không tự bật SePay hoặc tạo giao dịch kiểm tra.

Ảnh render source thật, font Roboto hiện có, API fixture giả:

- [Bước 1](output/admin-setup-verification/setup-step1-1536.png)
- [Bước 2](output/admin-setup-verification/setup-step2-1536.png)
- [Bước 3](output/admin-setup-verification/setup-step3-1536.png)
- [Mockup tham chiếu](output/imagegen/mockup-admin-setup-3-buoc-sepay-rieng.png)

Đã xem và đối chiếu ảnh bước 2: sidebar 244 px, padding 30 px, ba bước, form/summary 2:1, gap 16 px, card trắng bo góc, footer với Quay lại/Lưu nháp/Lưu và tiếp tục. Giữ logo hiện hành; không đưa nhãn Bản thiết kế vào source. Kiểm tra các width 320/390/768/1024/1366/1440/1536/1920 và zoom 125/150/200%. File JSON cạnh ảnh ghi viewport/bounds. Ảnh thể hiện layout/fixture, không xác nhận key, giá hoặc ngân sách trên runtime.

## Source và transaction

- Contract: `TOOL-SHARED.Contracts/Accounts/AdminSetupContracts.cs`. Các form bền vững chỉ nhận field an toàn, từ chối field không khai báo. Credential dùng request riêng.
- API Admin-only: `TOOL-SERVER/Controllers/AdminSetupController.cs`, prefix `/api/admin/setup`; bootstrap, drafts GET/POST/PUT, organization, connections, pricing, configuration, readiness, publish và operations.
- Core: `AdminSetupService`, `AdminSetupDraftEntity`, `AdminSetupOperationEntity`; EF trong `AccountDbContext`. Tái sử dụng cấu hình model governance/provider để mọi ghi phối hợp dùng cùng connection/context.
- Các command database dùng execution strategy + Serializable. Batch pricing nguyên tử; organization/budget/policy/pool/link nguyên tử; publish gồm activation + plan/mapping/audit/receipt nguyên tử. `AdminLicensePlanComposerService` có core dùng transaction do coordinator sở hữu.
- Credential test GET không phát sinh generation nằm ngoài SQL transaction, timeout 45 giây, operation lease 2 phút. Postflight kiểm lại membership, provider, credential version và draft revision. `OrganizationCredentialWriter` được dùng chung với trang quản lý, giữ Active → Retiring và version snapshot.
- Request hash của connection chỉ chứa metadata an toàn. Replay thành công so sánh key gửi lại với credential version đã lưu bên trong server, không lưu plaintext key, ciphertext hay hash key vào draft/receipt/audit. Browser chỉ lưu draft ID và command an toàn; retry mất phản hồi của connection cần nhập lại key, dùng cùng operation ID.
- Revision chặn hai tab sửa cùng draft; fingerprint cấu hình chặn ghi đè thay đổi budget/policy/pool ngoài draft. GET bootstrap/readiness chỉ đọc, không tạo kỳ ngân sách khi rollover. Ước tính readiness dùng giá đã cấu hình cho 1.000 token mỗi loại và 8 giây video; không giữ ngân sách hoặc gọi provider có phí.
- Gói wizard có flag `requiresOpenAiImageFalSetup` trong FeatureFlagsJson. Offers và reserve kiểm lại profile đúng model mặc định Text/Image, policy LongForm Fal, rate/metadata, credential và budget/quota cho từng tổ chức được chọn. Nhóm nhiều tổ chức chỉ phân bổ gói này vào các tổ chức đạt profile. Clone giữ flag; edit/publish lại kiểm profile. Gói legacy giữ quy tắc hiện có.
- Ngừng bán dùng active/private (`IsActive=true`, `IsPublic=false`); không dùng inactive để biểu diễn pause. Heartbeat của license đã mua giữ hiệu lực. Guard quota dùng chung cho composer và mapping nâng cao, từ chối đổi mặc định khi còn Pending chưa hết hạn/Paid chưa fulfilled/Scheduled; không ghi đè quota membership cũ.

Các bước đã lưu không được rollback ngầm khi bước tiếp theo bị lỗi: giá toàn hệ thống và kết nối đã lưu là thay đổi độc lập. UI giữ lỗi và cho kiểm tra/lưu lại đúng command, không tự tạo operation mới sau timeout.

## Migration và rollout

Theo yêu cầu tạo bảng đơn giản ngày 2026-10-14, chọn migration mới [VideoFactory.4.1.15.AdminSetupWizardSimple.sql](database/VideoFactory.4.1.15.AdminSetupWizardSimple.sql) thay bước khởi tạo 4.1.14. File tạo `auth.AdminSetupDrafts`, `auth.AdminSetupOperations`, PK/FK/index/CHECK và ghi version nền `4.1.14-admin-setup-wizard` cùng `4.1.15-admin-setup-wizard-simple`; có thể chạy lại, giữ nháp/receipt và quyền đã có. Không GRANT/DENY/REVOKE, không kiểm tra role/CONTROL, không seed giá/key hoặc bật thanh toán. File 4.1.14 lịch sử được giữ nguyên; không chạy tiếp file đó hoặc wrapper DBA cũ khi chọn nhánh đơn giản.

Chỉ áp vào database đích sau khi xác định instance/database, backup và restore theo runbook. Binary/UI mới cần migration này trước khi dùng wizard. Bảng mới lưu nháp/receipt, không thay dữ liệu tổ chức/gói/credential/thanh toán cũ.

Sau khi triển khai được phép, chạy `scripts/Test-AdminRuntimeAssets.ps1` trên URL loopback của đúng server. Marker HTML hiện hành: `admin-only-new-20261014.1`; wizard: `admin-setup-20261014.1`. Script chỉ GET HTML/CSS/JS/fonts, kiểm cache busting và hai asset wizard. Chưa chạy kiểm tra này trên server đích; chưa restart server/IDE của người dùng.

Rollback: ngừng thao tác wizard, trả binary/assets tương thích trước đó và giữ hai bảng mới, credential version, rate, policy, nhóm, plan/mapping, ledger/payment. Không xóa dữ liệu để rollback. Các nhóm đã mở bán cần được dừng nhận khách hoặc gói chuyển private bằng thao tác vận hành rõ ràng; không tắt license đã cấp. Giữ profile flag khi quay về binary mới để kiểm lại khả năng nhận khách.

## Kiểm thử

Đã chạy trên working tree này:

- `dotnet restore TOOL_GEN_POST_VIDEO.slnx`: đạt.
- `dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore`: đạt, 0 warning C#, 0 error.
- Test liên quan sau cập nhật profile: **82 Passed, 0 Failed, 0 Skipped**, có ba bài SQL Server thực trên LocalDB instance riêng, database/dữ liệu giả, tự dọn scope được tạo. Chạy migration hai lần, DENY desktop, mở bán đồng thời/replay, stale revision. Không dùng connection string ứng dụng.
- Browser Admin + TikTok state/browser: **86 Passed, 0 Failed, 0 Skipped**; toàn bộ dùng Razor/CSS/JS thật và HTTP fixture chặn outbound.
- Frontend `npm ci --no-audit --no-fund`, `npm run build`: đạt; `npm test`: **296 Passed, 0 Failed, 0 Skipped**, 45 file. Vite vẫn báo bundle trên 500 kB.

Lượt full .NET cuối và kiểm Browser Back SePay bổ sung được ghi khi hoàn tất bên dưới. Bài model/SQL opt-in bị skip không được tính là model/SQL đã đạt. Không chạy paid provider request, webhook tiền thật, migration database đích hoặc publish release trong lượt triển khai source.

### Kết quả cuối trên working tree đã triển khai

- Full .NET Release với collection tuần tự, TEMP/TMP tại đường D: ngắn và SQL wizard opt-in bật: **1700 Passed, 0 Failed, 13 Skipped**, tổng 1713. Bằng chứng: `output/admin-setup-verification/dotnet/admin-setup-final.trx`. Ba bài SQL wizard trong full là Passed; SQL TikTok/model/GPU/voice/benchmark opt-in khác còn Skipped.
- Browser Back SePay và toàn bộ wizard sau thay đổi cuối: **8 Passed, 0 Failed, 0 Skipped**. Bộ rộng Admin/TikTok trước thay đổi chỉ ở history SePay: 86/86; phần thay đổi history đã được kiểm lại trong tám bài wizard.
- Có `output/admin-setup-verification/source-manifest.json` ghi hash source/assets/test/migration được bàn giao. `git diff --check` đạt. Private LocalDB instance do test tạo đã dọn; không dừng instance/server của người dùng.

Bốn mức trạng thái: **có source: đạt**; **kiểm thử tự động trên working tree/commit nền nêu trên: đạt theo biên bản**; **smoke thủ công trên runtime đích: chưa xác minh**; **rollout production: chưa xác minh**. Migration mới có trong repository và đã rehearsal trên private SQL, chưa có bằng chứng đã áp vào database ứng dụng.

## Kiểm tra SQL đích theo yêu cầu chạy migration — 2026-10-14

Trên `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit, đã đọc cấu hình hiện hành và kết nối SQL chỉ đọc, yêu cầu Encrypt=true. Không in/lưu connection string hoặc mật khẩu. Đích: `112.78.2.70`, instance trả về `WIN-FQJ43SS9NHL\MSSQLSERVER2019`, database `tap51004_tool`, SQL 2019 Express `15.0.2000.5`, ONLINE/READ_WRITE. Tên instance hiện hành khác tên MB2D70 trong biên bản SePay cũ; gói mới khóa theo giá trị đọc thực tế ngày này.

Preflight:

- Có các bảng nền Identity, organization/pool, license/payment và version; kiểu FK hiện hành khớp (User Id nvarchar(450), các Id còn lại uniqueidentifier). SePay 4.1.10/4.1.13 đã có.
- Chưa có `auth.AdminSetupDrafts`, `auth.AdminSetupOperations`, version `4.1.14-admin-setup-wizard`.
- Principal `tap51004_sa` không thuộc desktop role; có CREATE TABLE, ALTER/REFERENCES auth, REFERENCES các bảng cha, SELECT/INSERT version. Không có CONTROL auth/database, không thuộc db_securityadmin/sysadmin, số CRUD auth WITH GRANT OPTION bằng 0. Không đủ quyền cho phần DENY của migration hiện hành; không bỏ bước bảo vệ hoặc tự nâng quyền.
- BACKUP DATABASE bằng 0. Latest full backup trong msdb: ID 494861, kết thúc `2026-10-14T06:14:46` theo giờ metadata server, không ghi checksum, recorded restore count bằng 0. Các số này không chứng minh file còn đọc được hoặc đã restore thử ở nơi khác; backup/restore hiện vẫn **chưa xác minh**.
- Query DMV encryption bị từ chối SQL 300/297, nên bỏ query cần VIEW SERVER STATE và dùng preflight chỉ đọc trong phạm vi principal; không cấp thêm quyền để đọc DMV.

**Kết quả: chỉ preflight, chưa thực thi migration hoặc thay đổi database đích.** Đã hỏi người dùng về tài khoản đủ quyền/DBA và xác nhận backup restore, theo `database/AGENTS.md`: “Cần người dùng xác nhận instance, database, backup đã restore thử và quyền tác động.”

Gói DBA: [Apply-AdminSetup-4.1.14-DBA.sql](output/admin-setup-verification/sql-target-20261014/Apply-AdminSetup-4.1.14-DBA.sql). Gói nhúng nguyên migration SHA-256 `D0ABCC271DFCE0E3FBDD2B079D68D6E3472DD58B763F4F0901F6C205808E3DDD`; thêm target/backup/authority gates và outer transaction kiểm 22 cột, 2 PK, 5 FK trusted, 4 CHECK trusted, index, 8 DENY và version trước commit. `BackupRestoreVerified=0` là mặc định; DBA chỉ đổi thành 1 sau xác minh backup. Không chứa credential, không chạy GRANT và không sửa quyền server đang dùng. Evidence metadata: [preflight.json](output/admin-setup-verification/sql-target-20261014/preflight.json).

Gói chưa được chạy trên SQL đích. Lệnh rehearsal bổ sung trên LocalDB bị bộ duyệt tự động từ chối (`blocked by policy`) trước khi thực thi; không có kết quả rehearsal mới để báo đạt. Ba bài SQL core đã đạt trong lượt implementation là bằng chứng lịch sử nêu trên, không xác minh quyền/backup đích hoặc wrapper DBA mới. Không chạy lại build/test source, deploy/restart server, cấu hình key/rate/SePay hoặc gọi provider trong lượt vận hành này.

## SQL tạo bảng đơn giản theo yêu cầu mới — 2026-10-14

Trên `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit, đã tạo migration 4.1.15; giữ nguyên SHA-256 của file 4.1.14 là `D0ABCC271DFCE0E3FBDD2B079D68D6E3472DD58B763F4F0901F6C205808E3DDD`. Nhánh mới thay thế phần thao tác quyền của lựa chọn trước; gói DBA 4.1.14 ở mục trên là lịch sử, không dùng cho yêu cầu mới.

Preflight chỉ đọc đã chạy lại theo cấu hình server hiện hành (không in/lưu credential): instance `WIN-FQJ43SS9NHL\MSSQLSERVER2019`, database `tap51004_tool`, SQL `15.0.2000.5`, ONLINE/READ_WRITE; đủ năm bảng cha/nền. Hai bảng wizard và cả hai version 4.1.14/4.1.15 đều chưa có. Latest full backup vẫn ID 494861, `2026-10-14T06:14:46` theo giờ metadata server, không có checksum hoặc recorded restore; query lịch sử restore từ backup của database này không trả dòng. Backup restore vẫn **chưa xác minh**. Đã hỏi xác nhận backup theo `database/AGENTS.md`; chưa chạy DDL trên database ứng dụng trong lúc chờ.

Kiểm chứng mới trong lượt tạo SQL đơn giản:

- SHA-256 file 4.1.15: `F1AD417930537BD3A269548FB4E94C168837515CF66C18DBD1759567FD16EE20`. Kiểm tra source không có statement GRANT/DENY/REVOKE hoặc gate role/CONTROL.
- `dotnet build TOOL-TESTS/TOOL-TESTS.csproj -c Release --no-restore`: **Passed**, 0 warning C#/0 error; build frontend đi theo dependency vẫn báo chunk trên 500 kB.
- `VIDEOMAKER_ADMIN_SETUP_LOCALDB=1`, chạy filter `TOOL_TESTS.Database.AdminSetupSqlTests` với collection tuần tự: **5 Passed / 0 Failed / 0 Skipped**. Hai regression mới xác minh nhánh đơn giản tạo/rerun rồi giữ draft/receipt, không thêm quyền desktop; chạy trên bảng đã được 4.1.14 tạo thì giữ nguyên quyền đã có. Ba bài cũ kiểm publish đồng thời/replay, stale draft và nhánh 4.1.14 vẫn đạt.
- Evidence: [admin-setup-simple-sql.trx](output/admin-setup-verification/sql-simple-20261014/admin-setup-simple-sql.trx), [preflight.json](output/admin-setup-verification/sql-simple-20261014/preflight.json). Test chỉ dùng LocalDB instance/database riêng với dữ liệu giả; đã tự dọn scope do fixture tạo. `git diff --check` đạt. Không chạy lại full .NET, npm test hoặc browser vì không thay logic/UI ứng dụng.

**Trạng thái cuối lượt tạo SQL, trước khi người dùng chuyển đích sang DUNGDEV: SQL đơn giản đã tạo và kiểm thử; chưa thực thi trên database hosting vì chờ xác nhận backup restore.** Không deploy/restart server hoặc gọi provider/webhook trong lượt đó.

## Đã áp SQL vào DUNGDEV / VideoFactory theo yêu cầu — 2026-10-14

Người dùng chỉ định trực tiếp `DUNGDEV / VideoFactory`, Windows Authentication, command timeout 180 giây. Đã kết nối đúng instance/database, ONLINE/READ_WRITE, đủ bảng nền, hai bảng wizard chưa có; migration 4.1.15 giữ nguyên SHA-256 nêu trên. Thao tác trên `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit.

- Tạo backup mới `COPY_ONLY, CHECKSUM, COMPRESSION`, 6.655.488 byte, backup ID **2036**. [Backup được giữ lại](<D:/SQL2019/Microsoft SQL Server/MSSQL15.MSSQLSERVER/MSSQL/Backup/VideoFactory_pre_admin_setup_simple_20261014_114819_b10bc5c3.bak>).
- `RESTORE VERIFYONLY WITH CHECKSUM` đạt; restore thật sang `VideoFactory_AdminSetup_Rehearsal_20261014_114819_b10bc5c3`; `DBCC CHECKDB` đạt trước/sau migration.
- Chạy nguyên file 4.1.15 **hai lần trên bản restore**: đạt. So sánh số dòng ở mọi bảng có sẵn (ngoài bảng version) và fingerprint quyền database trước/sau: giữ nguyên.
- Áp nguyên file 4.1.15 trên **VideoFactory**, outer transaction Serializable; kiểm schema, số dòng cũ và fingerprint quyền trước commit: đạt. Hoàn tất lúc **2026-10-14 11:50:04 UTC / 18:50:04 UTC+7**.
- Kiểm lại bằng kết nối độc lập: có `auth.AdminSetupDrafts` (12 cột) và `auth.AdminSetupOperations` (10 cột), **2 PK, 5 FK trusted, 4 CHECK trusted, 1 lookup index**; version nền 4.1.14 cùng nhánh 4.1.15 mỗi mã một dòng. Hai bảng mới đều 0 dòng; không còn transaction mở. Quyền trước/sau có cùng SHA-256 `AD683D5986D2618EC6701BECE961D8F9D1434F9996EC7863C9B2044C9B3BCB90`.
- Database rehearsal đã dọn sau khi kiểm đúng tên và đường dẫn hai file riêng; backup được giữ trong thư mục backup SQL Server. Không tác động các database rehearsal khác.

Lượt đầu đã backup/restore/CHECKDB thành công nhưng truy vấn kiểm đếm trong script vận hành dùng alias `RowCount` chưa có dấu ngoặc nên lỗi trước khi chạy migration hoặc thay đổi database chính. Đã sửa thành `[RowCount]`, tiếp tục trên chính bản backup/restore đó và hoàn tất. File migration không thay đổi. Evidence ghi cả lỗi đã sửa và kết quả cuối:

- [applied-20261014_114819_b10bc5c3.json](output/admin-setup-verification/sql-dungdev-20261014/applied-20261014_114819_b10bc5c3.json)
- [postflight.json](output/admin-setup-verification/sql-dungdev-20261014/postflight.json)
- [script vận hành giới hạn DUNGDEV](output/admin-setup-verification/sql-dungdev-20261014/Apply-AdminSetupSimple-Dungdev.ps1)

**SQL trên DUNGDEV / VideoFactory đã áp và kiểm chứng.** Database hosting `tap51004_tool` không thay đổi trong lượt này. Không sửa cấu hình kết nối ứng dụng, deploy/restart server, nhập key/rate, bật SePay, gọi provider có phí hoặc webhook. Không chạy lại build/test source; kết quả 5 Passed phía trên thuộc lượt tạo SQL trước đó. Smoke wizard/runtime và rollout production vẫn chưa xác minh.

## Đã tạo hai bảng trên hosting theo yêu cầu — 2026-10-14

Người dùng xác nhận muốn dùng SQL hosting, yêu cầu viết file chỉ tạo hai bảng bình thường và tiếp tục yêu cầu “bạn chỉ cần tạo cho tôi” sau khi đã giải thích điều kiện backup/restore. Đã áp đúng file [Create.AdminSetupTables.sql](database/Create.AdminSetupTables.sql) vào **112.78.2.70 / WIN-FQJ43SS9NHL\MSSQLSERVER2019 / tap51004_tool**, không chuyển kết nối ứng dụng sang DUNGDEV. Nhánh `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit; giữ các thay đổi có sẵn.

File SHA-256 `8C2423F34D641A5F9D311AF7A75CAC54DB59EAA214B56388C6FF82B7F3CCF248` chỉ tạo hai bảng, khóa/ràng buộc/index với transaction và hỗ trợ chạy lại. Không GRANT/DENY/REVOKE, không role/CONTROL gates, không ghi `SchemaVersions` hoặc dữ liệu nghiệp vụ. Định nghĩa 12+10 cột khớp file 4.1.15 và EF hiện hành; giữ nguyên các migration lịch sử.

Preflight xác nhận đúng target ONLINE/READ_WRITE, hai bảng chưa có, đủ quyền CREATE/ALTER auth/REFERENCES bốn bảng cha, kiểu FK khớp. Tài khoản không có BACKUP DATABASE; backup/restore hosting vẫn **chưa xác minh**. Latest backup đọc được ID 494861, không checksum hoặc recorded restore; không coi metadata đó hay backup DUNGDEV là bằng chứng restore hosting.

**Applied và đã commit lúc `2026-10-14T13:52:55.8816617Z` (20:52:55 UTC+7).** Chạy nguyên file với outer transaction Serializable, kiểm trước commit:

- `auth.AdminSetupDrafts`: 12 cột; `auth.AdminSetupOperations`: 10 cột.
- Có **2 PK, 5 FK trusted, 4 CHECK trusted, 1 lookup index**.
- Hai bảng mới đều 0 dòng; không sửa quyền hoặc dữ liệu của các bảng cũ.

Kết nối độc lập kiểm lại lúc `2026-10-14T13:54:18.5334662Z`: truy vấn đủ 12 cột draft và 10 cột operation đều đạt, `OpenTransactions=0`; source server có **87/87 bảng mapped hiện diện**, không còn bảng thiếu trong phạm vi mapping; tổng database **105 bảng**. Version 4.1.14/4.1.15 vẫn có 0 dòng do file được chọn không ghi version; đây là trạng thái chủ ý của bản tạo bảng đơn thuần.

Bằng chứng an toàn, không chứa credential:

- [preflight.json](output/admin-setup-verification/hosting-create-admin-20261014/preflight.json)
- [applied.json](output/admin-setup-verification/hosting-create-admin-20261014/applied.json)
- [postflight.json](output/admin-setup-verification/hosting-create-admin-20261014/postflight.json)

Lượt này chỉ tạo bảng hosting và ghi biên bản; không sửa code ứng dụng, đổi connection string, restart server/IDE, bật thanh toán, nhập key/rate hoặc gọi provider/webhook. Không chạy lại build/test source. **Schema hai bảng hosting đã áp và kiểm chứng; backup/restore, smoke wizard/runtime và thanh toán thật vẫn chưa xác minh.**
