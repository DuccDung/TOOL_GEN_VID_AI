# Triển khai phát hành TOOL-LOCAL qua VPS và Setup

Ngày triển khai: **2026-10-17**. Rà soát trên nhánh `main`, HEAD nền `f0651c918f28ca477f28875818148e9e86240b7b` và working tree chưa commit. Giữ nguyên thay đổi có sẵn; HEAD không đại diện đầy đủ source đã kiểm. Thực hiện theo [kế hoạch VP01–VP32](PLAN_TASK_PHAT_HANH_TOOL_LOCAL_QUA_VPS_VA_SETUP.md).

## Áp migration 4.1.17 trên hosting — 2026-10-17

Theo yêu cầu tiếp tục của người dùng sau khi đã nêu đúng đích và chẩn đoán thiếu cột, đã áp duy nhất `database/VideoFactory.4.1.17.DesktopExternalArtifacts.sql`, SHA-256 `33954864675573d04ca988ae61d6545f669d5bda0369373cc590d7ee4f7c72c5`, vào **112.78.2.70 / MB2D70\MSSQLSERVER2019 / tap51004_tool**, SQL Server `15.0.2000.5`. Commit hoàn tất `2026-10-16T20:16:28Z` (03:16 ngày 17/10, UTC+7). Nhánh/HEAD nền như trên, giữ working tree có sẵn; không đổi source ứng dụng.

Phiên riêng kiểm target và quyền trước chạy, implicit transaction tắt, thời gian chờ khóa tối đa 15 giây; chạy nguyên file trong transaction của migration, không chạy migration khác. Kết nối yêu cầu mã hóa, dùng `TrustServerCertificate=true` tạm thời vì chứng chỉ hosting chưa được Windows tin cậy; không lưu credential vào bằng chứng hoặc thay cấu hình kết nối ứng dụng.

Kết quả kiểm sau commit và bằng kết nối mới: đủ **9 cột mới**, `RelativePath` nullable, **6 CHECK enabled/trusted**, unique index đúng `(AppReleaseId, Kind)`, marker `4.1.17-desktop-external-artifacts` đúng **1 dòng**. Hai bảng release/artifact trước và sau đều **0 dòng**; fingerprint field cũ, FK và metadata quyền database không đổi. Truy vấn trực tiếp đọc bộ cột release/artifact mới biên dịch thành công. Chưa smoke trang Admin hoặc deploy/restart server trong lượt này.

**Backup/restore hosting và rehearsal trên clone hosting vẫn chưa xác minh**; tài khoản không có quyền `BACKUP DATABASE`. Đã ghi rõ giới hạn này khi người dùng tiếp tục yêu cầu thực thi. Rehearsal trước chỉ đạt trên dữ liệu giả LocalDB `17.0.4025.3`, không phải clone/backup SQL Server 2019 hosting; không dùng kết quả đó để chứng nhận backup hosting.

Bằng chứng không chứa credential: [operation.json](C:/vm-sql-verification/20261017-031455-hosting-apply-417-534bc4/operation.json), [before.json](C:/vm-sql-verification/20261017-031455-hosting-apply-417-534bc4/before.json), [after.json](C:/vm-sql-verification/20261017-031455-hosting-apply-417-534bc4/after.json), [xác minh kết nối mới](C:/vm-sql-verification/20261017-031455-hosting-apply-417-534bc4/fresh-connection-verification.json). Không chạy lại build/test ứng dụng; bằng chứng mới của lượt này chỉ là thực thi và kiểm SQL đích.

## Phạm vi đã triển khai

Source đã nối luồng: build ZIP ứng dụng + exact Setup + JSON → Admin đăng ký URL VPS → server phân phối metadata protocol 2 → Setup tải/xác minh ứng dụng và model → transaction/probe/commit. Desktop StaticArchive chuyển update/repair sang đúng Setup qua IPC có xác minh trước khi đóng. Nguồn internal hiện hành vẫn có đường xử lý riêng.

- Contract dùng chung thể hiện nguồn artifact, install mode/protocol, model bundle và binding Setup theo release; JSON legacy còn mặc định Internal/Legacy/protocol 1.
- Server nhận metadata external bằng quyền Admin, không HTTP/DNS fetch URL. Draft/publish dùng Revision CAS; external đóng băng identity/file sau lần phát hành đầu, kể cả khi tắt Active. Delete không xóa file VPS.
- Admin nạp JSON hoặc nhập metadata, lưu nháp → đăng ký app → upload Setup đúng hash → phát hành; giữ dữ liệu khi lỗi, khóa submit trùng, hiển thị rõ mới kiểm metadata.
- Pipeline ExternalVps kiểm file ZIP thực, profile/build policy, version/build/channel/hash và Setup thực; xuất JSON cuối cùng ngoài ZIP. Giữ các gate FFmpeg/profile/OCR/WebView2/ký đang có.
- Policy tải executable dùng chung, HTTPS đúng origin/port/prefix, chặn redirect/credential/query/fragment/traversal; HTTP file không mang token/cookie API. App ZIP dùng `.part`, resume/ETag, kiểm size/hash/MIME/ZIP/manifest trước apply.
- Maintenance IPC có frame giới hạn, nonce/operation, Windows user, peer PID/start-time, target ownership, exact Setup và metadata mới từ API. Chỉ ACK hợp lệ mới cho desktop đóng; Setup chờ đúng desktop thoát rồi gọi engine. Repair độc lập giữ đường dùng khi app không mở được, không yêu cầu model READY trước repair.
- Transaction giữ commit bản đang dùng trong giai đoạn tải; lỗi trước mutation không tự làm mất commit cũ. Sau mutation vẫn recovery/pending/readiness theo engine hiện hành.
- Repair lấy danh mục file sở hữu từ journal Committed đã khớp receipt, không cấp quyền sở hữu từ manifest sửa tay. Thiếu/hỏng manifest vẫn sửa được; sidecar lưu commit/receipt cũ để phục hồi ownership sau rollback và thử lại. Thiếu/sai bằng chứng dừng trước restore; pending gate vẫn giữ cho đến lần probe/commit đạt.
- Migration mới `VideoFactory.4.1.17.DesktopExternalArtifacts.sql` có backfill Internal/Legacy, mapping cả hai DbContext, nullable local path cho external, CHECK đúng nguồn/protocol/evidence và guard unique/trusted schema. **Đã áp trên hosting `tap51004_tool` ngày 2026-10-17**, xem biên bản SQL ở trên; chưa chứng nhận rollout ứng dụng.

## Vị trí source chính

| Phần | File cần rà soát |
|---|---|
| Contract | [External artifact/maintenance DTO](TOOL-SHARED.Contracts/Updates/DesktopExternalArtifactContracts.cs), [release response](TOOL-SHARED.Contracts/Updates/DesktopUpdateContracts.cs), [thứ tự phiên bản](TOOL-SHARED.Contracts/Updates/DesktopReleaseVersionPolicy.cs) |
| Policy/IPC chung | [Download policy](TOOL-DISTRIBUTION/Updates/DesktopArtifactDownloadPolicy.cs), [maintenance protocol](TOOL-DISTRIBUTION/Updates/DesktopMaintenanceProtocol.cs) |
| Server lifecycle | [Service hiện hành](TOOL-SERVER/Updates/DesktopReleaseService.cs), [external registration/publish/filter](TOOL-SERVER/Updates/DesktopReleaseService.External.cs), [options](TOOL-SERVER/Configuration/DesktopReleaseOptions.cs) |
| API | [Admin](TOOL-SERVER/Controllers/AdminDesktopReleasesController.cs), [Setup distribution](TOOL-SERVER/Controllers/LauncherDistributionController.cs), [desktop update/repair](TOOL-SERVER/Controllers/DesktopUpdatesController.cs) |
| Schema | [Migration 4.1.17](database/VideoFactory.4.1.17.DesktopExternalArtifacts.sql), [Account EF](TOOL-SERVER/Data/AccountDbContext.cs), [legacy EF](TOOL-SERVER/Data/VideoFactoryDbContext.cs), [legacy model bổ sung](TOOL-SERVER/Models/AppRelease.External.cs) |
| Admin UI | [Form Razor](TOOL-SERVER/Pages/Admin/Index.cshtml), [logic form](TOOL-SERVER/wwwroot/admin/admin.js), [bảng release](TOOL-SERVER/wwwroot/admin/admin-pages.js) |
| Setup | [Versions/legacy flow](TOOL-SETUP/LauncherInstallerService.cs), [static flow/transport/ownership](TOOL-SETUP/LauncherInstallerService.Static.cs), [transaction/recovery](TOOL-SETUP/StaticSetupTransaction.cs), [handoff](TOOL-SETUP/SetupMaintenanceHandoff.cs), [UI](TOOL-SETUP/InstallerForm.cs), [CLI](TOOL-SETUP/Program.cs) |
| Desktop | [Maintenance owner](TOOL-LOCAL/Updates/DesktopInstalledMaintenanceService.cs), [route cập nhật](TOOL-LOCAL/Updates/DesktopPackageUpdateService.cs), [API capability](TOOL-LOCAL/Updates/DesktopUpdateApiClient.cs); wiring trong `Program.cs`/`Form1.cs`, policy report trong `StaticModelBootstrapCommand.cs` |
| Pipeline | [Publish](scripts/Publish-DesktopRelease.ps1), [external helper](scripts/DesktopExternalRelease.psm1), [kiểm output](scripts/Test-DesktopSetupPublish.ps1), [profile stability](scripts/Test-Stability.ps1) |
| Regression | [Server](TOOL-TESTS/Updates/DesktopExternalReleaseTests.cs), [pipeline](TOOL-TESTS/Updates/ExternalDesktopReleasePipelineTests.cs), [policy](TOOL-TESTS/Updates/DesktopArtifactPolicyTests.cs), [IPC](TOOL-TESTS/Updates/DesktopMaintenanceProtocolTests.cs), [ownership](TOOL-TESTS/Updates/DesktopMaintenanceOwnershipTests.cs), [Setup external](TOOL-TESTS/Updates/StaticSetupExternalReleaseTests.cs), [maintenance](TOOL-TESTS/Updates/StaticSetupMaintenanceTests.cs), [repair](TOOL-TESTS/Updates/StaticSetupRepairTests.cs), [Admin browser](TOOL-TESTS/Admin/admin-external-release.browser.test.cjs) |

Các test WinForms/process/installer hiện hành cũng được cập nhật cùng thay đổi. Git đang có nhiều chỉnh sửa của những lượt trước; bảng này không quy toàn bộ `git status` thành thay đổi mới của đợt VP.

## Mức bàn giao

| Mức | Trạng thái |
|---|---|
| Source | Đã triển khai trên working tree; có manifest riêng để gắn kết quả kiểm tra |
| Tự động | Release build đạt; full .NET 2.356/0/18; frontend 341/0/0; browser Admin 22/0/0. Profile Setup lặp được ghi riêng bên dưới |
| Candidate và QA máy sạch | Chưa có bộ khách A/B đúng API/profile và đủ gate Release; chưa nghiệm thu cài/upgrade/repair từ VPS thực |
| Staging/production | Chưa chạy migration, upload app mới, đăng ký/publish release hoặc deploy môi trường thật |

Thư mục bằng chứng: `C:\vm-setup-verification\20261017-020828-external-vps`. `source-before.json` được chụp sau khi bắt đầu phân công, nên không gọi là baseline sạch trước mọi edit; `git-status-before.txt` giữ trạng thái dirty có sẵn. Kết quả các lượt cũ trong tài liệu khác là lịch sử, không cộng vào lượt này.

## Bằng chứng kiểm tra

| Kiểm tra | Passed | Failed | Skipped | Bằng chứng |
|---|---:|---:|---:|---|
| Restore + Release build cuối | Đạt | 0 lỗi | — | `restore.log`, `build-repair-final.log`: 0 Warning / 0 Error |
| Full .NET, source cuối | 2.356 | 0 | 18 | `full-final.trx`, `full-final.stdout.log`, `full-final.stderr.log` |
| Frontend | 341 | 0 | 0 | npm ci/build đạt; `frontend-tests.json` |
| Admin shell browser | 14 | 0 | 0 | `admin-browser/admin-shell-frozen.tap` |
| Admin external browser | 8 | 0 | 0 | `admin-browser/admin-external-release-frozen.tap` |
| Stability StaticSetup, 20 vòng | 300 mỗi vòng | 0 | 0 | `static-stability-final.log`, `static-stability-summary.json` |

Full .NET có **2.374 Total**, thời gian test khoảng 4 phút 51 giây; process wrapper 294,02 giây, không timeout, **0 process còn lại** trước cleanup, peak 21 process và 1.865.338.880 byte committed trong cây QA. TEMP ngắn riêng `C:\vp17\42175e`. Resource preflight trước full: C trống 6.206.386.176 byte, yêu cầu harness 5.290.061.824 byte; không tính ngân sách cài model/máy sạch.

18 Skipped được xuất riêng tại `full-final-skips.json`: các bài model/SQL/native opt-in không được tính là đã đạt. Native/HTTP/SQLite/file fixture có chạy không chứng minh Windows sạch, SQL Server, model từ bản cài khách hoặc staging đã được nghiệm thu. Frontend còn cảnh báo chunk Vite lớn hơn 500 kB; compiler .NET cuối không có cảnh báo.

Các lớp mới trong full gồm server external **32**, URL policy **15**, maintenance ownership **6**, maintenance protocol **18**, pipeline **26**, Setup external **9**, Setup maintenance **6**, repair **10**, đều Passed và đã nằm trong tổng full. Chi tiết `full-final-new-groups.json`; không cộng các lớp này thêm lần nữa. Những ca parent wait/native UI thêm vào lớp hiện hành cũng nằm trong full.

Giữ nguyên lịch sử lỗi và retest: `server-build-first.log` có 9 lỗi mapping scaffold legacy, đã bổ sung model/mapping; `focused-first.log`/`focused.trx` có **275 Passed / 1 Failed / 0 Skipped** vì fixture native pipe chờ ghi trước khi đọc. Sửa fixture để hai peer đọc/ghi đồng thời, siết loại exception; `focused-second.trx` đạt **276/0/0** trước các sửa repair cuối. Full cuối ở trên đã bao gồm toàn bộ thay đổi sau đó. Các lần browser thiếu module cache, chưa mở menu mobile hoặc đọc bảng trước khi render được giữ riêng; lượt frozen cuối 22/0/0.

Snapshot trước kiểm thử: `source-verified-before.json` có **1.157 file source/script/schema**, không gồm output/build/docs; `binary-verified-before.json` có **1.973 file executable/assembly/deps/runtimeconfig** từ test/desktop/Setup/server Release, bao gồm helper và native binary. Snapshot toàn bộ docs được ghi riêng sau bàn giao. HEAD nền không thay thế các manifest này.

Profile ổn định sau source cuối đã hoàn tất **20/20 vòng × 300 ca**, tương đương 6.000 lượt thực thi của cùng 300 ca, không phải 6.000 ca duy nhất. **0 Failed / 0 Skipped / 0 timeout / 0 process còn lại**; một fingerprint testcase/outcome xuyên suốt. Mỗi lượt test 18,54–21,99 giây; peak cây QA 591.216.640 byte committed, 17 process. TEMP có dấu nằm trong từng run; harness dọn đúng workspace của run.

Series: `C:\vp17-repeat\20261017-024136-StaticSetup-238c3b`. `summary.json` có `Complete=true`, đủ 20 result/TRX và discovery. Harness kiểm binary trước từng vòng và source cuối series; kiểm bổ sung sau toàn bộ full + repeat cho thấy **1.157 source/script/schema và 1.973 binary giữ nguyên hash**, không file thêm/mất trong tập snapshot. Razor/resx/config được đối chiếu riêng trong 11 file bổ sung; năm file Admin khớp hash của đợt browser trước full. Xem `verification-summary.json` và các manifest trước/sau; không lấy lịch sử 174 ca/vòng của đợt trước làm kết quả mới.

Hash manifest source: `f62c057cd8659f33abd5cbe5cc0dd3f924578f8233353b56c9ea017d2e3e69f9`; manifest binary: `6079dfe8b59c86a7478dba5163cdf1ca5ac2cd8fd82b0e1374ab202d2a3e6bd9`. Đây là hash danh mục, không phải hash app ZIP/Setup khách chưa được tạo. `source-delivery-all.json` ghi riêng source/docs sau bàn giao; `git-status-after.txt` giữ trạng thái working tree.

CLI metadata trên chính Release dev cuối đạt: `--inspect-setup-binding` và `--check-static-model-policy`, log `development-cli-verification.json`. Kết quả vẫn là Setup `Legacy`, API `https://localhost:7202/`, desktop `required=false`; **không phải candidate khách hoặc bằng chứng model READY**. Bộ khách phải được build bằng pipeline StaticArchive/ExternalVps và đủ các gate ở phần đầu vào.

## Trạng thái VP01–VP32

“Có source/fixture” chỉ kết luận trong phạm vi kiểm tự động đã ghi; không thay acceptance từ môi trường đích. Task có bước môi trường chưa thực hiện vẫn là **Partial**. Chỉ phần thiếu đầu vào cụ thể ghi **Blocked**.

| Task | Phần đã có | Phần chưa nghiệm thu |
|---|---|---|
| VP01 | Git/HEAD, dirty baseline và manifest riêng từng lượt | Baseline đầu được chụp khi một phần edit đã bắt đầu; không coi là checkout sạch |
| VP02 | Chốt phạm vi external/maintenance và các gate | API/profile/A-B/FFmpeg Release/DB/Windows QA chưa đủ: Blocked cho candidate và môi trường |
| VP03 | External app ZIP, metadata-only, exact Setup, IPC và immutable release | Hành trình staging/candidate thật |
| VP04 | DTO additive, protocol/source/mode/binding và JSON legacy | Ma trận các binary cũ/mới thực tế |
| VP05 | Migration 4.1.17, hai EF mapping, guard shape/CHECK/index | SQL Server clone, chạy hai lần, backfill, schema fault và rollback: chưa xác minh |
| VP06 | Shared HTTPS origin/port/prefix policy; mặc định server tắt | Kiểm nguồn app/API thật trên candidate |
| VP07 | Draft/publish, Revision CAS, freeze sau lần publish đầu | Concurrency SQL Server và nhiều API instances |
| VP08 | Admin register metadata, idempotency, internal upload giữ nguyên | Middleware/token và API staging thật |
| VP09 | Filter protocol trước latest, URL theo nguồn, exact Setup | Các HTTP endpoint trên staging và storage nhiều instances |
| VP10 | Hai nguồn, nạp JSON, giữ form/retry/focus/busy và khóa identity | Admin thao tác file/DB thật; browser hiện dùng fake API |
| VP11 | Pipeline ZIP → hash → exact Setup → JSON ngoài ZIP, giữ gate | Chạy pipeline tạo candidate khách: Blocked bởi đầu vào |
| VP12 | Policy và runbook vị trí ZIP immutable trên VPS | Chưa upload app mới, hash tại VPS, full GET/Range/MIME/TLS app |
| VP13 | Validator ZIP/manifest/path/cap/binding tại pipeline và consumer | Byte app local/VPS và inventory/license/prerequisite của candidate thật |
| VP14 | Setup protocol 2, API khác file origin, compiled package/model pin | HTTPS API/VPS và request thực của bộ cài phát hành |
| VP15 | `.part`, receipt/ETag/resume, full hash, MIME/ZIP, timeout/cancel | Ma trận HTTP/TLS/disk đầy đủ trên final candidate; fixture app không được suy từ mọi test model |
| VP16 | Phase/progress/retry/cancel, exact release, nhãn cài/sửa/cập nhật | Trọn hành trình native UI, DPI vật lý và launch bật/tắt trên máy sạch |
| VP17 | IPC cùng user, peer PID/start, nonce/ACK, exact binding, chờ parent | Handoff thật giữa app và Setup EXE phát hành; fault/lifetime toàn cây |
| VP18 | StaticArchive dùng exact Setup, repair không đòi READY, standalone | Update A→B, repair B và đường chuyển khách legacy thực tế |
| VP19 | Transaction/ownership/pending/recovery; giữ file ngoài bộ cài | App/model crash theo phase trên candidate đã cài |
| VP20 | Protocol 1 mặc định, consumer mới quảng bá 2, reject unknown | Old/new server × old/new Setup/desktop và chuyển khách cũ |
| VP21 | Regression server/contract/CAS/policy qua fixture | Rehearsal T-SQL và quyền/middleware thực; SQLite không thay SQL Server |
| VP22 | Browser Admin shell và external, 390/1440, focus/overflow | Admin staging và thao tác với artifact thật |
| VP23 | Pure policy, ZIP/HTTP/resume/no-credential fixtures | Đầy đủ fault matrix app trên candidate và file host thật |
| VP24 | WinForms, ownership/ACK, transaction, native pipe/process fixtures | Desktop + Setup + bootstrap + model tích hợp từ candidate khách |
| VP25 | Resource preflight, Unicode TEMP, profile lặp có hash/case/process identity | Quota/ACL/low RAM/multivolume trên máy QA; xem kết quả vòng lặp thực ở trên |
| VP26 | Installed qualification CLI/harness và pin model có sẵn | Qwen/Piper/OCR/output/nghe/đọc và soak từ chính bản cài mới: chưa chạy |
| VP27 | Công cụ build/kiểm candidate và binding | Candidate A/B: Blocked bởi API/profile/FFmpeg Release và đầu vào phát hành |
| VP28 | Migration/API/Admin/options/runbook chuẩn bị xong | Chưa migration/deploy/register release trên staging |
| VP29 | Engine/UI/tooling cho cài sạch/update/repair | Windows x64 sạch/user thường, cold install, A→B, repair app hỏng: chưa thực hiện |
| VP30 | Log fail/retest, manifest và ma trận bằng chứng | Chưa nghiệm thu candidate/runtime/staging nên không chốt toàn plan |
| VP31 | Runbook rollout/rollback và đường nâng client cũ | Chưa pilot/Stable/production hoặc diễn tập rollback nhiều instances |
| VP32 | Source/migration, báo cáo và runbook bàn giao | Chưa có artifact khách cuối cùng, biên bản runtime/pilot hoặc diễn tập vận hành |

## Đối chiếu AC01–AC28

| AC | Bằng chứng trong phạm vi source/fixture | Phần còn mở |
|---|---|---|
| AC01 | Đăng ký metadata, thiếu Setup không publish, idempotency và Admin import | Pipeline/candidate thật → API staging → publish QA |
| AC02 | Tách internal/external, URL/storage dispatch và legacy upload | Backfill và tải cả hai nguồn trên DB/API thật |
| AC03 | Reject URL sai origin/port/path/HTTP/credential/query/fragment trước tải | TLS và consumer thực trên candidate |
| AC04 | Fixture API khác external origin, pin model riêng | Traffic API/VPS thật của Setup phát hành |
| AC05 | Identity/hash/size/manifest/binding và file Setup sai bị từ chối | Tamper candidate/app ZIP VPS, không chỉ declaration hoặc ZIP nhỏ |
| AC06 | ZIP traversal/duplicate/link/cap có validator và regression | Đầy đủ containment/fault theo từng consumer trên bản cài QA |
| AC07 | App strong ETag/206/no-credential và các suite model resume | Mọi nhánh app Range/416/weak-change ETag/chunked trên candidate |
| AC08 | Release immutable/CAS, refresh snapshot exact trước ACK | Metadata thay khi consumer tải candidate thật |
| AC09 | Redirect/MIME và retry/timeouts trong source/fixture | Đủ 404/429/5xx/stall/reset/TLS fault của app/VPS thực |
| AC10 | Disk/resource/consent guards | Quota/ACL/từng volume/low RAM/commit/pagefile trên Windows QA |
| AC11 | SQLite CAS, idempotency/freeze/visibility; browser chống submit kép | SQL Server/API concurrency thật |
| AC12 | Future/inactive/channel/platform và filter trước latest | HTTP staging và nhiều API instances |
| AC13 | Engine/bootstrap/launch/gate cùng fixture | Cài sạch đủ năm component, launch bật/tắt trên candidate |
| AC14 | Recovery/receipt/pending/journal và file do test sở hữu | Crash/cancel theo phase app/model thật và khởi động lại |
| AC15 | ACK tamper/expiry/owner/binding, native peer PID và parent wait | Hai binary phát hành handshake thật và các fault toàn tuyến |
| AC16 | Setup tách lifetime desktop; native bootstrap Job Object | Setup/worker sống đúng sau desktop thoát trong handoff candidate |
| AC17 | Exact Setup/newer release và transaction source | A→B cùng bundle, receipt/fingerprint/gate thực tế |
| AC18 | Không đòi READY, thiếu EXE, repair manifest và giữ unmanaged data | App B không mở → exact Setup độc lập → model/runtime/gate đạt |
| AC19 | Exact route/binding, thiếu artifact không fallback latest | Mất/sai Setup trên host thật và UX giữ bản cũ |
| AC20 | Bundle pin và legacy/ownership reject | Chuyển root/legacy và bundle khác trên máy QA, giữ dữ liệu |
| AC21 | Default protocol 1, capability 2 và fail unknown | Old/new binary matrix và chuyển khách cũ qua Setup mới |
| AC22 | HTTP file riêng; fixture assert không Authorization/cookie | Traffic trên VPS thật từ candidate |
| AC23 | Usage lease, process ownership và Unicode TEMP fixtures/lặp | Hai Setup/app/model thật cùng lúc và nhiều volume |
| AC24 | Admin browser, WinForms và host/bridge regression | Full app/Setup candidate, DPI vật lý, keyboard/focus thủ công |
| AC25 | Hash/cache/receipt/startup/runtime guards và fixture | Cache đúng/hỏng/offline trên runtime vừa cài từ VPS |
| AC26 | Harness installed có source | Qwen cold start/memory, Piper WAV/nghe, OCR/cue/SRT/MP4 chưa chạy lượt này |
| AC27 | Harness giới hạn tài nguyên/timeout và 20 vòng fixture | Soak ≥4 giờ và fault matrix FX còn thiếu; vòng lặp không thay soak |
| AC28 | Runbook rollout/rollback giữ schema và file cũ | Deploy/rollback nhiều instances, pilot, repair A sau rollback |

## Đầu vào còn thiếu để tạo bộ cài khách và triển khai thật

1. URL HTTPS API VideoMaker thật; khác URL VPS file. Bản build dev kiểm CLI hiện còn `https://localhost:7202/`, không phải bộ Setup gửi khách.
2. Version/build/channel A/B và profile khách đã được duyệt, chỉ cung cấp đường dẫn profile, không gửi credential trong chat/log. Workflow SQL chuyển tiếp còn theo validator hiện hành.
3. FFmpeg được duyệt phạm vi Release; hồ sơ hiện có vẫn ghi `Approval scope: Development`. Không sửa attribution/approval để vượt gate.
4. Database QA/đích, backup/restore, môi trường được phép deploy; Windows x64 sạch/user thường và tài nguyên cho model/runtime/soak.
5. Chính sách Authenticode nếu bản phát hành yêu cầu ký. Runtime app/Setup hiện kiểm hash và binding; kiểm chữ ký EXE của dự án nằm ở pipeline theo policy tùy chọn, chưa có consumer signer/revocation policy. WebView2 vẫn kiểm Microsoft signer/WinVerifyTrust bắt buộc.

Cờ external mặc định tắt; việc sửa source không bật kho phát hành hoặc nâng bản Setup localhost thành bộ cài khách. Model ZIP giữ nguyên pin, không copy model/cache/venv/READY từ tài khoản đang làm việc vào candidate.

## Runbook vận hành

### 1. Phân biệt nơi lưu file và API

| Thành phần | Internal | ExternalVps trong đợt này |
|---|---|---|
| ZIP ứng dụng | Upload vào storage `DesktopReleases:StorageRoot`, mặc định `App_Releases`; tải qua API | Upload thủ công lên VPS; server đăng ký metadata và trả URL trực tiếp |
| Setup EXE | Artifact nội bộ như hiện hành | Vẫn upload lên storage server VideoMaker; mỗi release có đúng Setup đã ghép với ZIP đó |
| ZIP model StaticArchive | Theo source/model mode của bản cài | Giữ URL/hash đã pin trong source; độc lập ZIP ứng dụng |
| API | Auth/license/nghiệp vụ/distribution | Vẫn API VideoMaker thật; không đổi `ServerBaseUrl` thành URL ZIP hoặc gốc file VPS |

Nguồn `Internal` và `ExternalVps` loại trừ nhau trên một artifact. Không chuyển một release đang có ZIP nội bộ thành external; tạo version/build mới. ZIP model hiện tại không phải ZIP ứng dụng và không được đăng ký vào slot `DesktopPackage`.

URL model đang pin: `https://103.179.190.182:8443/models/VideoMaker-Models-20261016-082700.zip`, dung lượng `2847980994` byte, SHA-256 `e9939863910af178ec6be6e99f7fcc16948b6a8d916bd5b32bc0a50d92d3f471`. File model vượt 2 GiB, dùng policy riêng. Việc kiểm ZIP từ lượt trước không thay kiểm ứng dụng hoặc nghiệm thu bản cài mới.

Nguồn ZIP ứng dụng được biên dịch hiện tại:

- `https://103.179.190.182:8443/apps/videomaker/`
- `https://103.179.190.182:8443/models/` — để hỗ trợ upload ứng dụng vào kho file hiện có; nên bố trí ứng dụng trong prefix `/apps/videomaker/` riêng.

Origin gồm cả cổng `8443`; URL dùng HTTPS trực tiếp, không credential/query/fragment/percent encoding/whitespace/đoạn `.` hoặc `..`, không redirect. Server không tự mở rộng nguồn tin cậy cho Setup/desktop: URL phải qua cả allowlist server và policy biên dịch trong consumer. Đổi VPS/origin/prefix ngoài danh sách cần sửa source, build và kiểm lại các consumer.

### 2. Migration trước binary server mới

Migration: `database/VideoFactory.4.1.17.DesktopExternalArtifacts.sql`, mã schema `4.1.17-desktop-external-artifacts`.

1. Chỉ định instance/database QA, người vận hành, backup và cách restore; rà quyền database tương ứng.
2. Rehearsal migration hai lần trên clone có release/artifact nội bộ, kiểm cột/check constraint/index, đọc EF, upload/download nội bộ và rollback dữ liệu.
3. Áp migration đã nghiệm thu lên đúng database được giao **trước** triển khai server binary mới; EF mới đọc các cột mới dù cờ external đang tắt. Feature flag không thay migration.
4. Deploy server mới với external chưa mở; kiểm startup/health/schema, CRUD và download internal.
5. Cấu hình nguồn VPS và mở external ở staging sau khi các bước dưới đủ dữ liệu.

Không tự chạy SQL production từ các ví dụ này. Migration file có source và test giả lập không chứng minh schema đích đã được áp hoặc rehearsal SQL thực đã đạt.

### 3. Cấu hình server — mặc định external tắt

`DesktopReleaseOptions` mặc định `ExternalVpsEnabled=false`, `AllowedExternalPrefixes=[]`. Vì vậy cập nhật binary không tự cho phát hành nguồn VPS.

Ví dụ fragment cấu hình server staging sau khi nguồn file đã được giao:

```json
{
  "DesktopReleases": {
    "StorageRoot": "App_Releases",
    "MaximumArtifactBytes": 2147483648,
    "ExternalVpsEnabled": true,
    "AllowedExternalPrefixes": [
      "https://103.179.190.182:8443/apps/videomaker/"
    ],
    "MaximumExternalArtifactBytes": 2147483648
  }
}
```

Có thể thêm prefix `/models/` đã biên dịch nếu thực sự đặt ZIP ứng dụng tại đó. Chỉ cấu hình các prefix cần dùng. Không đặt URL model thay URL app. Biến môi trường tương đương: `DesktopReleases__ExternalVpsEnabled`, `DesktopReleases__AllowedExternalPrefixes__0`, `DesktopReleases__MaximumExternalArtifactBytes`.

Giới hạn external thống nhất: ZIP ứng dụng tối đa 2 GiB, tổng giải nén 4 GiB, 200.000 entry; Setup tối đa 512 MiB, tên artifact tối đa 180 ký tự. Upload internal giữ giới hạn cũ của server cho nén và giải nén. ZIP model dùng giới hạn riêng.

Tắt `ExternalVpsEnabled` hoặc bỏ prefix đang dùng làm external release không còn được distribution cấp ra. Cần cân nhắc các lượt cài/update đang lấy metadata; cờ không thu hồi file ZIP public trên VPS. Không xóa file release cũ khi còn cần repair/rollback.

### 4. Tạo candidate ứng dụng + exact Setup + JSON

Chốt trước API HTTPS thật, version/build/channel, profile khách, FFmpeg được duyệt Release, OCR/MSVC, WebView2 offline installer đã duyệt và policy ký. Profile không được chứa API loopback, cache dev, marker READY, model payload lớn hoặc credential dùng chung. Workflow SQL chuyển tiếp hiện vẫn bắt buộc: chỉ dùng `-AllowTransitionalSql` với profile khách đã được chấp nhận; không thêm password vào command hoặc tài liệu.

Ví dụ cấu trúc lệnh, **chưa phải lệnh sẵn sàng chạy** vì đường dẫn/URL/chứng thư phải do vận hành cung cấp:

```powershell
.\scripts\Publish-DesktopRelease.ps1 `
  -Version '1.2.0' -BuildNumber 37 -Channel Beta `
  -ServerBaseUrl 'https://api-videomaker.example.invalid/' `
  -AppSettingsPath 'C:\approved-profile\customer-beta.json' `
  -AllowTransitionalSql `
  -ModelDeliveryMode StaticArchive -DeliveryMode ExternalVps `
  -PackageDownloadUrl 'https://103.179.190.182:8443/apps/videomaker/1.2.0-37/VideoMaker-1.2.0-37-win-x64.zip' `
  -FfmpegBundlePath 'C:\approved-bundles\ffmpeg-release' `
  -OcrNativeRuntimePath 'C:\approved-bundles\ocr-msvc' `
  -WebView2InstallerPath 'C:\approved-bundles\webview2-runtime-x64.exe' `
  -AuthenticodeMode WindowsStore `
  -SigningCertificateThumbprint '<APPROVED_CODE_SIGNING_THUMBPRINT>' `
  -SignToolPath 'C:\approved-tools\signtool.exe' `
  -SigningTimestampUrl 'https://approved-timestamp.example.invalid/' `
  -RequireAuthenticode
```

Lựa chọn Authenticode phải theo policy phát hành đã chốt; không bỏ yêu cầu ký hoặc đổi FFmpeg Development thành Release chỉ để vượt gate. Cấu hình nguồn hiện tại chưa tự chứng minh đủ đầu vào để tạo candidate khách.

Pipeline thực hiện: publish app với compiled StaticArchive policy → kiểm thành phần/bản publish → tạo manifest/inventory và ZIP → hash ZIP → giải nén đúng ZIP kiểm lại → build Setup ghép version/build/SHA/channel/API → ký/kiểm Setup theo policy → chạy `--inspect-setup-binding` trên **Setup vừa build** → đọc/hash lại ZIP và Setup → xuất JSON đăng ký riêng. Không có upload, API registration hay publish release tự động.

Output dưới `artifacts/releases/1.2.0-37/`:

- `VideoMaker-1.2.0-37-win-x64.zip`
- `setup/VideoMaker Setup.exe`
- `external-registration.json`
- `checksums.sha256`
- `authenticode-report.json`

JSON nằm ngoài ZIP để không tạo vòng băm; không đưa Setup hash sinh sau vào ZIP app. Pipeline dùng `CreateNew` cho JSON và từ chối ghi đè bộ output cũ có file. Build/publish lỗi không được lấy ZIP dở dang làm bộ bàn giao. `authenticode-report.json` vẫn ghi `ProductionQualified=false`: ký/hash đạt không tự là nghiệm thu cài thật.

`DeliveryMode Updater` và `Direct` giữ đường cũ; `PackageDownloadUrl` chỉ được nhận với `ExternalVps`, và ExternalVps bắt buộc `ModelDeliveryMode StaticArchive`. Không dùng `Direct` để suy ra đã đăng ký external.

### 5. Upload và kiểm byte VPS

Upload duy nhất ZIP ứng dụng từ bộ candidate vào URL immutable có version/build, ví dụ `/apps/videomaker/1.2.0-37/VideoMaker-1.2.0-37-win-x64.zip`. Giữ kho model và file cũ. Setup/JSON/checksum giữ trong bộ bàn giao; Setup được upload riêng tới server VideoMaker qua Admin.

Trước đăng ký/phát hành, tính lại SHA-256 tại VPS, full GET đúng URL và so toàn byte/hash với candidate. Kiểm TLS bình thường, HTTP 200, MIME ZIP/octet-stream được phép, Content-Length, Range/If-Range/ETag và mẫu byte resume; không dùng tùy chọn bỏ kiểm certificate. API metadata hợp lệ không chứng minh URL đó đang phục vụ đúng byte.

### 6. Thao tác Admin và API

Trong Admin → Phát hành desktop → Thêm phiên bản:

1. Chọn **Link ZIP trên VPS**.
2. Nạp `external-registration.json`; xem version/build/channel/platform/URL/file/size/hash/manifest và binding Setup.
3. Chọn chính `setup/VideoMaker Setup.exe` từ cùng bộ bàn giao.
4. Chọn **Lưu nháp** để chuẩn bị; chọn **Phát hành** chỉ sau QA VPS và candidate đã được chấp nhận.

UI lưu release `IsActive=false` trước → đăng ký external JSON → upload Setup khớp filename/length/SHA → lấy Revision mới → publish. Lỗi ở một bước giữ ID bản nháp và field để retry; không tạo thêm release trùng. Sau khi đã từng phát hành, thay file/URL/hash/binding phải dùng version/build mới.

Browser tính SHA-256 của Setup tới 256 MiB. Với Setup lớn hơn 256 MiB tới 512 MiB, nạp JSON pipeline; UI đối chiếu tên/dung lượng rồi server kiểm SHA thực khi upload. Không coi checksum tự khai trong JSON là byte đã được server/QA kiểm.

Thứ tự API tương đương, tất cả operation Admin dùng quyền Admin hiện hành:

| Operation | Method/route | Ghi chú |
|---|---|---|
| Tạo nháp | `POST /api/admin/desktop-releases` | Metadata release, `isActive:false`; response có `releaseId` và `revision` |
| Đọc nháp chính xác | `GET /api/admin/desktop-releases/{releaseId}` | Dùng để nhận Revision mới sau mutation của chính lượt thao tác |
| Sửa metadata | `PUT /api/admin/desktop-releases/{releaseId}` | Gửi Revision đang giữ để phát hiện conflict |
| Đăng ký app VPS | `POST /api/admin/desktop-releases/{releaseId}/artifacts/DesktopPackage/external` | Body nguyên JSON pipeline; tối đa 4 MiB, **không upload ZIP**, không HTTP/DNS fetch VPS trong operation này |
| Upload exact Setup | `POST /api/admin/desktop-releases/{releaseId}/artifacts/Setup` | Multipart `file`; phải khớp Setup metadata đã đăng ký |
| Phát hành | `POST /api/admin/desktop-releases/{releaseId}/publish` | Body `{ "revision": "<CURRENT_GUID>" }`; server kiểm đầy đủ và CAS |

Không đưa token vào JSON đăng ký, source hoặc log. Với API ngoài UI, lấy Admin session qua đường xác thực hiện hành; ví dụ route trên không cung cấp hoặc tự tạo credential.

Đăng ký external metadata giống hệt là idempotent. Metadata khác trên nháp có thể thay trước lần publish đầu; nếu Setup cũ không khớp thì binding bị gỡ và cần upload đúng Setup mới. Artifact external sau lần phát hành đầu không được đổi dù tắt Active. `FirstPublishedAtUtc` giữ lịch sử freeze; không gửi trường này để mở khóa. Lịch `PublishedAtUtc`, notes, mandatory và Active vẫn có đường quản trị; scheduled release chưa được public trước thời điểm đã đặt.

### 7. Distribution, Setup và maintenance

Consumer mới dùng distribution protocol 2. Kiểm metadata bằng `GET /api/launcher-distribution/versions?channel=Beta&platform=win-x64&distributionProtocol=2`; external chỉ hiện khi Active, đến lịch, đủ registration/Setup/binding và flag/prefix vẫn hợp lệ. Client không quảng bá protocol mới tiếp tục nhận legacy phù hợp; external mới không che bản legacy cũ mà client hiểu.

Với external, `DownloadUrl` trong package response trỏ thẳng VPS. Route package API cũ không proxy hay redirect external; có thể trả mã `desktop_external_direct_download_required`. Đường lấy exact Setup metadata: `GET /api/launcher-distribution/versions/{releaseId}/setup?distributionProtocol=2`; file nằm ở `/api/launcher-distribution/versions/{releaseId}/setup/download`. Không dùng `setup/latest` để repair một release đã biết.

Setup StaticArchive chỉ nhận đúng version/build/hash đã ghép; tải app từ nguồn được duyệt, kiểm byte/hash/ZIP/manifest, bootstrap model, probe Qwen/Piper/OCR/media/WebView2 và commit transaction/receipt rồi mới hoàn tất. File ZIP app đạt chưa chứng minh model runtime trên máy khách đạt.

Desktop update/repair StaticArchive dùng đúng Setup của release. ACK có nonce/operation/target/user/Setup PID+start-time và snapshot; desktop chỉ đóng sau ACK hợp lệ. Setup chờ desktop thoát, không force-kill. Parent không thoát/ACK sai/file sai giữ ứng dụng và trả lỗi. Nếu app không mở được, vận hành cung cấp **Setup exact** độc lập và chọn root có ownership/commit hợp lệ; receipt ownership hỏng cần hỗ trợ/phục hồi hoặc root mới, không xóa/giả receipt để ép repair. Repair cùng build không đổi bundle model trong đợt này.

### 8. Rollback và mức bằng chứng

Giữ release/candidate cũ cùng app ZIP, exact Setup, checksum, JSON và nguồn/license. Rollback phân phối chọn bản cũ đã được kiểm; không ghi đè byte tại URL đã publish. Trước rollback server binary cũ phải ngừng phân phối external; backup/schema rollback và khả năng đọc dữ liệu phải theo kế hoạch database riêng.

**Deactivation chỉ là điều kiện trước rollback, chưa chứng nhận downgrade an toàn.** Rà source binary nền `f065`: EF cũ coi `RelativePath` là required, trong khi danh sách/CRUD Admin vẫn include artifact inactive. Vì vậy dữ liệu external có NULL có thể làm Admin cũ lỗi ngay khi materialize, dù distribution đã ẩn; đây là suy luận từ source, chưa chạy tái hiện binary/SQL cũ. Chỉ downgrade theo phương án đã rehearsal trên clone QA với backup/restore và kiểm Admin/API/internal/repair; nếu chưa có bằng chứng thì giữ hoặc roll forward bản tương thích. Không xóa metadata external, điền path giả hoặc sửa dữ liệu tay để ép binary cũ chạy.

Browser source cuối đã chạy trong evidence này: shell hiện có **14 Passed / 0 Failed / 0 Skipped**, external release **8 Passed / 0 Failed / 0 Skipped**; log `admin-browser/admin-shell-frozen.tap`, `admin-browser/admin-external-release-frozen.tap`, tổng hợp `admin-browser/admin-frozen-summary.json`. Browser dùng actual Razor/CSS/JS với API giả lập, không server/DB/VPS thật. Ảnh responsive và keyboard không thay kiểm DPI/Narrator native hoặc cài Setup thật.

Các mức chưa được tài liệu này chứng nhận: backup/restore và rehearsal clone hosting, deploy staging, upload ZIP app mới, API Admin thật, full GET ứng dụng mới, Windows sạch, update A→B/repair qua candidate, model inference từ bản cài và rollout production. Migration 4.1.17 đã áp và kiểm trực tiếp theo biên bản SQL ở trên. Kết quả build/full/repeat của source cuối được ghi ở phần bằng chứng đầu biên bản; các nháp ngoài repository chỉ là hỗ trợ rà soát, không thay kết quả cuối.
