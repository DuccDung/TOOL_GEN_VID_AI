# Triển khai form release tối giản và Setup dùng chung

**Bổ sung thay thế yêu cầu metadata trong ZIP:** [luồng ZIP Publish thông thường](TRIEN_KHAI_ZIP_PUBLISH_DON_GIAN.md) cho phép nén output Publish ở gốc hoặc một wrapper mà không kèm ba JSON phát hành. Server kiểm ZIP/native identity và sinh metadata riêng; Setup dùng chung **1.1.0 trở lên** lấy metadata và kiểm hash trước cài. Đường ZIP đã có đầy đủ metadata vẫn hỗ trợ; `DesktopCreateVerifiedReleaseZip=true` giữ packager tùy chọn. Phần mô tả bên dưới về metadata bắt buộc nằm trong ZIP là lịch sử của định dạng cũ. Thay đổi này không thêm migration, vẫn cần schema 4.1.18 nếu môi trường chưa áp. Setup 1.0 cũ cần thay một lần; desktop cũ chưa nhận `/models/` có thể phải chạy Setup mới độc lập. Bằng chứng tự động/lượt SQL dưới đây không đồng nghĩa source mới đã rollout hoặc model đã được nghiệm thu.

Ngày rà soát: **2026-10-17**. Nhánh `main`, HEAD nền `f0651c918f28ca477f28875818148e9e86240b7b`, cùng working tree chưa commit. Giữ nguyên các thay đổi có sẵn. Tài liệu này ghi source của [kế hoạch FR01–FR32](PLAN_TASK_FORM_RELEASE_TOI_GIAN_VA_SETUP_DUNG_CHUNG.md); không dùng các kết quả VP/protocol 2 trước đây làm bằng chứng cho luồng mới.

**Đã triển khai và kiểm thử source cho form, kiểm ZIP chạy nền, Setup dùng chung protocol 3, update/repair và pipeline tách riêng. Full .NET cuối 2.483 Passed/0 Failed/18 Skipped; frontend 341/0/0; Admin browser 121/0/0. Đã áp migration 4.1.18 trên hosting ngày 2026-10-17. Chưa deploy ứng dụng lên hosting/VPS, đăng ký Setup production, phát hành release, cài A/B trên Windows sạch hoặc nghiệm thu model từ bản cài mới.**

Lượt SQL tiếp theo đã commit lúc **2026-10-16 21:33:47 UTC** trên `112.78.2.70 / MB2D70\MSSQLSERVER2019 / tap51004_tool`, kiểm schema và bảo toàn dữ liệu/quyền nhìn thấy bằng cả kết nối mới. Người dùng yêu cầu tiếp tục sau thông báo rằng backup/restore và rehearsal clone hosting **chưa xác minh**; không coi quyền tiếp tục là xác nhận đã có backup. [Biên bản SQL hosting riêng](TRIEN_KHAI_SQL_4_1_18_HOSTING.md) ghi source hash, thời điểm và giới hạn của bằng chứng này.

## 1. Hành vi sau thay đổi

Form **Tạo release** chỉ có hai trường bắt buộc: **Phiên bản** và **Link ZIP ứng dụng trên VPS**. **Ghi chú phát hành** và **Bắt buộc cập nhật** là tùy chọn. Stable/win-x64 là scope hiện hành; người quản trị không nhập tên file, build, checksum, manifest, JSON hoặc upload Setup theo từng release.

Lưu nháp ghi preparation bền rồi xếp hàng kiểm file. Server tải toàn bộ ZIP theo luồng, tự tính SHA-256/dung lượng, đọc manifest và metadata version/build của binary mà không chạy ứng dụng trên server. Chỉ khi kiểm đạt, snapshot còn khớp và có Setup hệ thống tương thích mới được phát hành. Đổi Version/URL vô hiệu bằng chứng cũ; sửa ghi chú/mandatory không buộc tải lại ZIP. Request fingerprint bất biến phân biệt retry tạo nháp với lần sửa nội dung sau đó; revision/lease/generation chặn ghi đè và worker cũ hoàn tất muộn.

**Setup dùng chung** được quản lý ở nút riêng trên trang Phát hành: chọn file EXE, server đọc metadata trong native version resource, lưu hash/size và cho kích hoạt. Không cần nhập JSON ở giao diện này. Một EXE có thể cài nhiều ZIP ứng dụng trong phạm vi API/profile/platform/protocol/model policy nó hỗ trợ. File ZIP mới không làm thay đổi checksum hoặc bắt build lại Setup. Cần Setup mới khi thay năng lực/giao thức/model policy vượt khả năng đã đóng trong EXE.

TOOL-LOCAL protocol 3 so **Version trước, BuildNumber sau**: `1.10.0` mới hơn `1.9.0` dù build nhỏ hơn; cùng version nhưng build lớn hơn là hotfix mới. Bản nháp/không tương thích không dùng để báo update. Repair lấy đúng version/build/hash đang cài, không tự chuyển latest. Các endpoint và bộ cài protocol 1/2 tiếp tục có đường tương thích riêng.

Client/Setup protocol 1/2 cũ không tự nhận release protocol 3. Nếu đã có khách dùng bản cũ, cần cho chạy Shared Setup một lần để chuyển sang binary hỗ trợ protocol 3, hoặc xây bridge release sau khi xác định đúng runtime đã cài. Source và test fixture hiện tại không chứng minh một binary cũ ngoài thực tế tự nâng được sang luồng mới.

## 2. Source và ranh giới

| Phần | Source chính | Trách nhiệm |
|---|---|---|
| DTO/version | `TOOL-SHARED.Contracts/Updates/DesktopSharedBootstrapperContracts.cs`, `DesktopUpdateContracts.cs`, `DesktopReleaseVersionPolicy.cs` | Preparation, bootstrapper identity, profile/capability; thêm protocol `Shared=3`, giữ `Current=2` |
| Schema | `database/VideoFactory.4.1.18.SharedDesktopBootstrapper.sql` | Preparations, catalog Setup, liên kết verified preparation; mở CHECK cho protocol 3 mà giữ dữ liệu 1/2 |
| Nháp/worker | `TOOL-SERVER/Updates/DesktopReleasePreparationService.cs`, worker của module Updates | Hàng đợi bền, lease/fencing, retry/cancel, CAS, materialize inactive, publish |
| Kiểm ZIP | `TOOL-SERVER/Updates/DesktopReleaseArchiveInspector.cs` | HTTP riêng, scratch `.part`, hash toàn stream, ZIP/inventory/media/profile và native binary metadata |
| Catalog Setup | `TOOL-SERVER/Updates/DesktopBootstrapperService.cs`, `DesktopSharedReleaseValidation.cs` | Đọc binding nhúng trong EXE, quản lý file bất biến, active/revoke, lịch sử và compatibility |
| API/Admin | Các controller `AdminDesktopReleasePreparations`, `AdminDesktopBootstrappers`, `LauncherDistribution`, `DesktopUpdates` | API Admin có quyền; distribution công khai có capability; check/repair desktop có auth |
| Giao diện | `Pages/Admin/Index.cshtml`, `wwwroot/admin/admin-release-preparations.js`, các file Admin liên quan | Form bốn trường, progress/retry/cancel, xung đột revision, quản lý Setup riêng |
| Setup | `TOOL-SETUP/InstallerOptions.cs`, `LauncherInstallerService*`, `SetupMaintenanceHandoff.cs`, `StaticBootstrapRunner.cs` | Shared mode không pin app version/build/hash khi build; bind API snapshot lúc chạy, giữ transaction/probe/recovery |
| Desktop/IPC | `TOOL-LOCAL/Updates/*`, `TOOL-DISTRIBUTION/Updates/DesktopMaintenanceProtocol.cs`, `DesktopSharedBootstrapperPolicy.cs` | Check/update/repair, kiểm profile/API trước UI, tải Setup được duyệt, nonce/PID/SID/ACK và ownership |
| Pipeline | `scripts/Publish-SharedDesktopSetup.ps1`, `scripts/Publish-DesktopRelease.ps1` | Build Setup riêng; `SharedVps` chỉ tạo ZIP ứng dụng và bằng chứng nội bộ |

Không thay gateway AI, credential, budget, license hoặc quyền workflow SQL desktop. Bộ model vẫn là `models-20261016-03`; pin, checksum, worker protocol và probe năm component được giữ. SHA-256 chứng minh đúng bytes đã kiểm, không chứng nhận model inference hoặc tính xác thực của source phần mềm.

### Metadata và snapshot

- ZIP ứng dụng có `update-manifest.json`, `package-inventory.json` và `distribution-profile.json`. File profile do pipeline tạo, không do quản trị nhập trên form. Apphost có native ProductVersion dạng `1.2.0+desktopbuild.37`; desktop vẫn hiển thị version trước dấu `+`.
- Profile ứng dụng là khai báo từ pipeline được quản trị tin cậy; server kiểm native version/build thực và các file gói. Không coi file profile là chữ ký source hoặc bằng chứng app/model đã chạy.
- Trên máy cài, Shared Setup chạy policy CLI của executable đã kiểm hash và bắt report **distributionProtocol=3/profile khớp giá trị biên dịch** trước khi chuẩn bị model. Gói khai báo protocol 3 nhưng binary thực vẫn protocol 2 sẽ bị chặn. Legacy exact mode giữ report tương thích cũ.
- Setup shared có `AssemblyDescription`/native VERSIONINFO Comments prefix `VideoMaker.SharedBootstrapper.v1:` với Base64 JSON public binding. Server đọc có giới hạn, không thực thi EXE upload. Khi chạy, Setup đối chiếu binding với API/profile/capability đã biên dịch.
- Operation giữ ReleaseId/package hash cùng BootstrapperId/hash. Refresh theo release ID và bootstrapper ID lịch sử; đổi Setup Active không âm thầm đổi executable hoặc package đang cài. Release bị thu hồi, bootstrapper revoked hoặc mất compatibility thì dừng trước mutation.
- IPC giữ nonce, SID, PID/starttime, expiry, ownership và ACK trước khi desktop đóng; Setup chờ desktop thoát. Repair vẫn exact; không bỏ qua receipt/journal hoặc readiness để dùng chung EXE.
- IPC protocol 2 không gửi các trường shared nullable mới khi chúng null, nhằm giữ khả năng đọc của binary cũ có JSON strict mode.

## 3. Cấu hình và thứ tự triển khai

Default trong `DesktopReleaseOptions` là **`SharedBootstrapperEnabled=false`**, `ApiBaseUrl` rỗng, `Profile=Production`, `AllowedExternalPrefixes=[]`. `ExternalVpsEnabled` là cờ luồng protocol 2 cũ, không tự thay bằng cờ shared. Chưa bật cờ hoặc viết URL API production trong lượt triển khai source này.

Cấu hình dưới đây minh họa trạng thái mặc định, không phải cấu hình production đã áp:

```json
{
  "DesktopReleases": {
    "SharedBootstrapperEnabled": false,
    "ExternalVpsEnabled": false,
    "ApiBaseUrl": "",
    "Profile": "Production",
    "AllowedExternalPrefixes": [],
    "ScratchRoot": "App_Releases_Inspection",
    "InspectionTimeoutSeconds": 600,
    "MaximumInspectionConcurrency": 1,
    "PreparationLeaseSeconds": 90,
    "MaximumPreparationAttempts": 3,
    "InspectionRetryDelaySeconds": 10
  }
}
```

Khi triển khai được chỉ định: đặt `ApiBaseUrl` đúng API VideoMaker thật và prefix ứng dụng phù hợp, hiện source pin hỗ trợ `https://103.179.190.182:8443/apps/videomaker/`. ZIP model có route/policy riêng; không dán URL ZIP model vào form release. Server kiểm TLS bình thường, outbound :8443 đúng nguồn, không redirect/auth/cookie sang VPS. Scratch cần quyền ghi, quota/free space và worker có vòng đời phù hợp IIS; mất mạng/recycle phải kiểm bằng pilot môi trường thật.

Thứ tự vận hành:

1. Xác minh DB/backup/restore và quyền trên môi trường được chỉ rõ; rehearsal clone phù hợp. **Áp migration 4.1.18 trước binary EF/server mới. Cờ tắt không tránh lỗi thiếu bảng/cột khi binary mới đọc schema.** Không chạy lại 4.1.17 làm verifier sau 4.1.18 vì CHECK definition cũ sẽ từ chối schema đã nâng đúng.
2. Deploy server hiểu schema/giao thức mới với shared flag tắt, cấu hình API/profile/prefix/scratch và vòng đời worker; kiểm startup và route.
3. Bật pilot theo môi trường đã cho phép; đăng ký một Setup đúng profile, kiểm metadata rồi kích hoạt. Hai cờ protocol 2/shared được vận hành theo đúng luồng tương ứng.
4. Build/upload ZIP ứng dụng; nhập Version + URL, lưu nháp, chờ kiểm đạt và phát hành.
5. Nghiệm thu một Setup cài A, cập nhật B, repair exact, lỗi mạng/restart và giữ model cache trên Windows QA. Chỉ sau bằng chứng máy đích mới mở rộng phát hành.

### Build Setup một lần

Ví dụ lệnh, **chưa thực thi để tạo candidate khách**. Thay URL/đường dẫn bằng profile được duyệt; `api.example.invalid` chỉ là placeholder. Script từ chối output đã tồn tại, xác minh WebView2 installer đã pin và đối chiếu native Comments/CLI sau publish.

```powershell
.\scripts\Publish-SharedDesktopSetup.ps1 `
  -BootstrapperVersion '1.0.0' `
  -ServerBaseUrl 'https://api.example.invalid/' `
  -Profile 'Production' -Channel 'Stable' `
  -WebView2InstallerPath 'D:\release-inputs\webview2-runtime-x64.exe' `
  -OutputDirectory 'D:\release-output\shared-setup-1.0.0'
```

Kết quả gồm `VideoMaker Setup.exe`, `shared-bootstrapper.json`, `bootstrapper-report.json` và checksum. JSON là bằng chứng build nội bộ; **Admin chỉ chọn EXE**, server tự đọc binding. Nếu môi trường phát hành yêu cầu Authenticode, dùng các tham số WindowsStore/certificate/timestamp và `RequireAuthenticode` hiện có; không ký giả hoặc hạ gate.

### Build ZIP mỗi release

```powershell
.\scripts\Publish-DesktopRelease.ps1 `
  -Version '1.2.2' -BuildNumber 37 -Channel 'Stable' `
  -ServerBaseUrl 'https://api.example.invalid/' `
  -AppSettingsPath 'D:\release-inputs\desktop.production.json' `
  -ModelDeliveryMode 'StaticArchive' -DeliveryMode 'SharedVps' `
  -ReleaseProfile 'Production' -MinimumBootstrapperVersion '1.0.0' `
  -WebView2InstallerPath 'D:\release-inputs\webview2-runtime-x64.exe' `
  -FfmpegBundlePath 'D:\release-inputs\ffmpeg-release-win-x64' `
  -OcrNativeRuntimePath 'D:\release-inputs\ocr-runtime-win-x64' `
  -AllowTransitionalSql
```

`AllowTransitionalSql` chỉ chọn profile workflow `vf` được duyệt khi composition vẫn cần SQL; validator vẫn cấm mật khẩu SQL dùng chung trong package. API thật, profile khách, FFmpeg có Release approval, OCR/native/WebView cùng các gate package hiện hành phải đạt. Không dùng cấu hình dev hoặc bỏ probe để build qua.

Trong `ApprovedModelArtifactPins.Create`, minimum desktop hiện là **1.0.0 build 1**; **1.2.1/protocol 3 là yêu cầu translation worker**, không phải minimum desktop. Ví dụ release 1.2.2/build 37 chỉ minh họa một phiên bản mới. Version trong form vẫn phải khớp binary/ZIP; kiểm import không thay runtime compatibility gate hoặc probe.

`SharedVps` đóng protocol 3/profile vào desktop, thêm `distribution-profile.json`, kiểm inventory/ZIP sau giải nén và **không build hoặc ghi lại Setup**. Chỉ upload ZIP ứng dụng lên route immutable trên VPS; model dùng bộ đã có. Không overwrite bytes của một URL/hash đã phát hành. JSON/checksum báo cáo không phải thao tác bắt buộc trong form.

## 4. API và giới hạn file

| API | Mục đích |
|---|---|
| `POST /api/admin/desktop-release-preparations` | Lưu nháp, idempotency request ID và trả `202`/operation ID |
| `GET .../desktop-release-preparations`, `GET .../{id}`, `GET .../by-release/{releaseId}` | Danh sách/trạng thái nháp bền |
| `PUT .../desktop-release-preparations/{id}` | Sửa input với revision hiện hành |
| `POST .../{id}/retry`, `/cancel`, `/publish`, `/deactivate` | Thao tác có quyền và revision; cancel phải được server xác nhận |
| `GET /api/admin/desktop-bootstrappers/status` và `GET /api/admin/desktop-bootstrappers` | Cấu hình/trạng thái/catalog Setup hệ thống |
| `POST /api/admin/desktop-bootstrappers` | Multipart **chỉ field `file` EXE**; không yêu cầu metadata JSON từ người dùng |
| `POST .../desktop-bootstrappers/{id}/activate`, `/revoke` | Kích hoạt/thu hồi bằng revision |
| `GET /api/launcher-distribution/bootstrapper?profile=Production&distributionProtocol=3` | Metadata Setup Active phù hợp |
| `GET /api/launcher-distribution/bootstrappers/{id}/download` | File EXE bất biến theo ID; không lấy EXE tùy ý từ URL form |
| `GET /api/launcher-distribution/versions?...&distributionProtocol=3` | Release cài mới phù hợp |
| `GET /api/launcher-distribution/versions/{releaseId}?distributionProtocol=3&profile=Production&bootstrapperId={id}` | Xác minh snapshot bằng đúng release và Setup lịch sử |
| `GET /api/desktop-updates/check` và `/repair` | Check/update hoặc repair exact qua API có auth; truyền version/build/platform/channel/protocol/profile |

Giữ giới hạn: ZIP ứng dụng **2 GiB**, giải nén **4 GiB**, **200.000 entry**, tên archive tối đa **180 ký tự**; Setup tối đa **512 MiB**. Bộ ZIP model riêng không dùng cap 2 GiB của ZIP ứng dụng. SHA-256 được tính sau khi đọc đủ bytes; HEAD/header/tên `.zip` không thay việc kiểm file thực. ZIP path traversal, absolute path, duplicate/case collision/link, file lỗi hoặc version không khớp không được phát hành.

## 5. Bằng chứng và việc chưa nghiệm thu

Evidence root lượt này: `C:\vm-release-form\20261017-035544-shared-setup`. Báo riêng source, kiểm thử tự động, smoke native và rollout.

| Phạm vi | Trạng thái tại lúc viết biên bản | Bằng chứng/giới hạn |
|---|---|---|
| Source shared flow | **Có source và kiểm thử tích hợp cuối** | 1.143 file code/script không đổi trước/sau test; danh mục ở mục 2, không suy ra server đã deploy |
| SQL Server 2019 rehearsal | **Passed**, synthetic DB riêng, engine **15.0.4382.1** | `sql2019-rehearsal/engine.json`, `migration-418-report.json`, `guard-418-report.json` |
| SQL exact source | SHA-256 **`61d38d14cf768802a2a4c5aabc665f09bd0d16de111aa75d00ffe844dd385689`** | 4.1.18 x2 dưới user QA không db_owner, actual CHECKSUM restore sang DB riêng; 3 release/6 artifact gồm protocol 2, old rows/FK/index/quyền giữ nguyên |
| SQL fault/guard | **Passed** | Late injected THROW rollback DDL/version/data; exact source từ chối CHECK không trusted và index sai. Effective CRUD của desktop fixture vẫn 0 trên auth/ai. Backup/restore là synthetic, không phải backup hosting hoặc clone hosting |
| SQL lỗi đầu và sửa | Giữ lịch sử | `attempt-1-*` ghi lỗi `nvarchar(10000)` trên SQL Server thật; đã đổi thành nvarchar(max) với CHECK 10.000 ký tự. `attempt-2-*` là hash trước bổ sung CreateRequestFingerprint, không thay final proof |
| SQL hosting 4.1.18 | **Đã áp và kiểm bằng kết nối mới**, engine **15.0.2000.5**, commit **2026-10-16 21:33:47 UTC** | [Biên bản SQL riêng](TRIEN_KHAI_SQL_4_1_18_HOSTING.md): đúng hash source, marker duy nhất, hai bảng/cột/CHECK/FK/index/PK đạt; release/artifact 0 dòng trước/sau, FK/index cũ và fingerprint quyền nhìn thấy giữ nguyên. Backup/restore/clone hosting chưa xác minh; người dùng yêu cầu tiếp tục sau thông báo |
| Native Setup proof ban đầu | **Passed trên source tại lượt đó**, fixture API `api.invalid` | `native-setup-proof/native-proof.json`: Comments/CLI protocol 3 đúng, EXE 52.736.754 byte; chưa thay bằng chứng source cuối sau các chỉnh sửa tiếp theo |
| Native Setup proof source cuối | **Passed**: native Comments + CLI binding, 4 DLL của build thường giữ nguyên SHA | `native-final-proof/native-proof.json`; isolated `--artifacts-path`, EXE **52.738.580 byte**, SHA-256 **`1aae14047bb08b58253c1327863e65134e28ee05762304a384a21b4f83661380`**. API `api.invalid` là fixture; chưa phải candidate khách, không tải/chạy model |
| .NET restore/build | **Passed source cuối hiện hành: 0 Warning/0 Error** | `restore-final-summary.json`, `build-final-2.log`; source còn dirty theo baseline đã ghi |
| Focused tests luồng mới | **127 Passed / 0 Failed / 0 Skipped** | `test-results/shared-final-2-focused.trx`; giữ lượt trước 125/2/0 và retest sau sửa xử lý InvalidDataException. Không cộng trùng vào full suite |
| Full .NET cuối | **2.483 Passed / 0 Failed / 18 Skipped / 2.501 Total** | `test-results/shared-final-full.trx`, `full-final.log`, `test-outcomes.json`; console 7 phút 52 giây, TRX 473,92 giây gồm runner. Sáu SQL và 12 model/GPU/voice opt-in bị skip |
| Admin browser | **121 Passed / 0 Failed / 0 Skipped** | `C:\vm-release-ui-verification-20261017\admin-all-footer-frozen.tap`, `footer-result.json`; năm asset Razor/CSS/JS khớp hash qua test và kiểm lại cuối. Đã xem ảnh 390/1440 px, harness kiểm 320–1440 px với API fixture; chưa chứng minh hosting/VPS thật |
| Frontend ci/build/test | **341 Passed / 0 Failed / 0 Skipped**, 48 file | `frontend-summary.json` ghi cả ba exit code 0; `frontend-test.log` ghi số thực. Đây là React frontend, không thay Admin browser |
| Cài khách A/B và model | **Chưa nghiệm thu** | Chưa candidate Windows sạch, Qwen/Piper/OCR/media/WebView2 từ bản cài khách, inference/quality/soak, profile máy Intel/AMD/RAM |
| Hosting/VPS ứng dụng production | **Chưa rollout trong lượt này** | SQL hosting đã cập nhật theo dòng riêng phía trên; chưa deploy/upload app/Setup/model, bật flag hoặc publish release |

Tests shared bootstrapper phân biệt rõ runtime metadata/loaded assembly/transaction fixtures với cài đặt thực tế. Cùng một loaded Setup assembly có thể validate hai package identity khác nhau mà checksum binary giữ nguyên; native proof xác nhận EXE single-file giữ Comments và binding. Hai bằng chứng đó **chưa thay nghiệm thu cùng một Setup cài A rồi update B trên máy QA thực**.

Manifest `source-final-2-before.json` có SHA-256 **`be83effbc45cd589963f5ce4a703a7e0489816823bd09c05d454a1e07dc4a2f8`**; 1.143 file code/script khớp `source-final-2-after-tests.json`, sáu assembly trong `binaries-final-2-before.json` không đổi qua test. `final-consistency-after-tests.json` ghi số thay đổi bằng 0; `final-consistency.json` kiểm lại sau native proof cũng có **0 thay đổi** trên 1.143 file, sáu assembly và năm asset Admin. Manifest này không bao gồm tài liệu, cache, model hoặc output build và không thay snapshot source trước khi bắt đầu sửa vốn chưa được chụp.

Lượt build tích hợp đầu phát hiện hai fixture project chưa nằm trong solution nên output Release không được copy đúng; đã bổ sung project vào solution và giữ `build-initial.log`. Focused đầu trên nhóm đầy đủ đạt **125 Passed/2 Failed/0 Skipped**, phát hiện `InvalidDataException` chưa được map sang lỗi kiểm file Setup. Đã sửa catch rõ ràng tại upload/verify/list compatibility/download, chạy lại focused **127/0/0** rồi full như trên; giữ cả hai TRX. Không giảm assertion hoặc bỏ bài lỗi để đạt. PowerShell parser 0 lỗi, validator/preflight chạy fixture; 14 bài pipeline mới nằm trong full suite, không cộng thêm 40 bài regression đã chạy riêng trước đó.

Test model/SQL bị Skipped ghi riêng theo outcome `NotExecuted`, không nâng thành Passed hoặc production-ready. Source/binary cuối đã kiểm không đồng nghĩa candidate khách hoặc rollout.

### Sổ FR01–FR32 theo nhóm

| Task | Phần đã có bằng chứng | Giới hạn/công việc còn lại |
|---|---|---|
| FR01 | Baseline nhánh/HEAD và **331 entry dirty** đầu lượt được ghi; manifest cuối root đã chụp **1.143 file** | Không chụp source hash đầy đủ trước khi sửa, vì vậy manifest cuối không được gọi là so sánh source trước/sau lượt. Giữ riêng baseline Git và evidence thực |
| FR02–FR03 | Quy tắc bốn trường, version-first/hotfix, DTO/capability protocol 3 và đường protocol 1/2 đã có source | Chuyển khách cũ cần xác định binary/runtime thật; không tự hứa bridge đã rollout |
| FR04 | Đã xác định các gate API/profile/VPS/TLS/scratch/worker/package trong source/runbook | **Partial**: chưa có candidate/profile khách, môi trường QA Windows sạch và vận hành worker/IIS đích được nghiệm thu |
| FR05–FR06 | Shared contracts, entity/mapping và migration 4.1.18 có source; 4.1.17 được giữ nguyên; 4.1.18 đã áp và kiểm trên hosting ngày 2026-10-17 | Chưa deploy ứng dụng; xem [biên bản SQL](TRIEN_KHAI_SQL_4_1_18_HOSTING.md) cho đúng môi trường và giới hạn |
| FR07 | SQL Server 2019 synthetic baseline/backup/actual restore, exact migration x2, quyền và fault guards đạt; SQL hosting sau đó đã được xác minh schema/bảo toàn bằng kết nối mới | Synthetic không phải clone/backup/restore hosting; backup/restore và rehearsal clone hosting vẫn chưa xác minh dù người dùng đã yêu cầu tiếp tục áp SQL |
| FR08–FR17 | Source API/worker/inspector/CAS/publish/distribution/catalog và coverage tự động luồng mới | Fixture HTTP/SQLite và SQL synthetic không thay network/IIS/DB hosting thật |
| FR18–FR22 | Shared Setup, runtime snapshot/IPC, update/repair/version-first, pipeline tách riêng; native metadata/CLI proof **source cuối đạt** bằng publish cô lập | Chưa cài app/model khách thật bằng một EXE qua A/B |
| FR23–FR26 | Form, progress/retry/cancel, giữ input/CAS/keyboard có source; **Admin browser 121/0/0** | Chưa DPI/Narrator hoặc UX trên hosting thật |
| FR27–FR29 | Focused mới 127/0/0, build cuối 0 warning/0 error; full **2.483/0/18**, frontend **341/0/0** | 18 Skipped là 6 SQL/12 model opt-in; fixture không thay runtime đích |
| FR30 | Có fixture runtime/metadata/ownership và test một loaded Setup binary nhận hai package identity | **Chưa nghiệm thu** Windows sạch A/B, installed-model inference/quality/soak |
| FR31 | Runbook schema trước binary/flag/pilot đã viết | **Chưa rollout** hosting/VPS, chưa bật flag/đăng ký Setup/publish thật |
| FR32 | Đã viết hướng dẫn build/form/catalog, bảo toàn artifact và rollback ở tài liệu này | Rollback production chưa được diễn tập trên môi trường đích; không đánh 32/32 task hoàn tất |

## 6. Rollback vận hành

Nếu pilot lỗi, dừng phân phối shared/worker theo lease và giữ EXE/ZIP/manifest/hash/history của các phiên bản đã phát hành. Không sửa checksum để khớp file sai, xóa receipt/model READY thủ công, hoặc sửa ngược CHECK rồi chạy lại 4.1.17. Giữ bản cài cũ và dữ liệu người dùng qua transaction/recovery hiện có.

Hạ binary server chỉ khi đã kiểm tương thích schema/dữ liệu mới trên clone; nếu chưa thì dùng forward fix. Quyền tác động hosting, migration/publish và môi trường QA khách vẫn phải được xác định theo runbook trước thao tác thật.
