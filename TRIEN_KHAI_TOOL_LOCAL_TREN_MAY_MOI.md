# Triển khai TOOL-LOCAL trên máy mới — 2026-09-18

## Phạm vi và trạng thái

Nhánh `main`, nền source `0a85b569914a9a230425442e9f0b3e1c16072cf1`, thay đổi chưa commit. Đây là biên bản phần đã triển khai trong [kế hoạch 24 task](PLAN_TASK_TOOL_LOCAL_CHAY_TREN_MAY_MOI.md), không phải tuyên bố toàn bộ kế hoạch hoặc rollout đã hoàn thành.

Đã sửa nhóm cấu hình/Qwen/quyền ghi và kiểm tra package của desktop. **Chưa bỏ SQL trực tiếp, chưa bổ sung/deploy API workflow, chưa chạy trên Windows sạch.** Cấu hình nguồn vẫn cần SQL workflow hiện hành; chỉ server HTTPS hoạt động chưa đủ cho toàn bộ desktop. Không xóa connection string hoặc bỏ kiểm tra database của luồng ứng dụng trước khi chuyển đủ các service.

Phần SQL/API cần chốt theo giới hạn người dùng đã nêu: chỉ TOOL-LOCAL, server đã deploy. Thực hiện phương án API của kế hoạch sẽ cần thêm source server/contracts và sau đó triển khai server tương thích; giữ nguyên server thì còn phương án SQL chuyển tiếp và cần thông tin SQL đích. Nhóm sửa desktop được triển khai và kiểm thử độc lập; các phần còn lại giữ trạng thái chưa hoàn thành, không tự chuyển thành Done từ kết quả build/test.

## Thay đổi đã có source

### Cờ dịch và Setup Qwen

`SystemSetup/DesktopComponentComposition.cs` dùng cùng điều kiện Vietsub + dịch local cho việc tạo provider và bật registry. `Program` dùng trực tiếp composition này. Khi tắt một trong hai cờ, provider null và Qwen Setup DISABLED; không tải/probe Qwen để vượt gate của một tính năng đang bị tắt. Cờ dịch mặc định không đổi, Cloud không phụ thuộc Qwen.

Test ma trận 4 tổ hợp bật/tắt dùng đúng factory composition, kiểm số lần tạo provider, trạng thái check/install, không gọi HTTP và không tạo thư mục model khi không cần.

### Tùy chọn người dùng

Tùy chọn đồng bộ lời nói được ghi atomically vào `%LOCALAPPDATA%\ToolGenPostVideo\settings\preferences.json`, không ghi cạnh EXE. File deployment `appsettings.json` và override riêng `appsettings.user.json` được giữ nguyên.

Khi chưa có preferences, giá trị cũ được đọc từ cấu hình riêng hoặc fallback hiện hành. Khi đã lưu preferences, giá trị mới ưu tiên. Chỉ áp dụng khóa `SpeechSynchronizationEnabled`; trường server/database/cờ Qwen trong preferences không có tác dụng. Không chép toàn bộ cấu hình máy cũ sang file mới.

Test kiểm ghi khi thư mục app không tồn tại, giữ nguyên file legacy, ưu tiên preferences và không cho preferences đổi endpoint/database/feature khác. Nghiệm thu quyền ACL thật trên Windows sạch vẫn là bước riêng.

### Kiểm tra cấu hình và bundle trước phát hành

- Thêm `scripts/Test-DesktopDeploymentSettings.ps1`: đối chiếu HTTPS của desktop/bộ cài, từ chối localhost trong profile khách, workspace/media/giọng trỏ đường riêng của máy build, secret và SQL chưa được chọn rõ là chuyển tiếp.
- `Publish-DesktopRelease.ps1` bắt buộc truyền `-ServerBaseUrl`, kiểm config trước publish và sau khi đưa config vào package; hỗ trợ `-AllowTransitionalSql` một cách tường minh. Tùy chọn này vẫn từ chối host DUNGDEV và password SQL nhúng; nó không tự xác minh/cấp quyền SQL hoặc làm desktop trở thành chỉ dùng API. Script còn bắt buộc cấu hình SQL bằng `RequireTransitionalSql` vì composition hiện hành vẫn cần SQL; xóa connection string chưa thể biến binary thành bản chỉ dùng API.
- Giữ nguyên gate duyệt FFmpeg; không sửa provenance, checksum hoặc approval scope. Với cấu hình nguồn còn SQL, kiểm phát hành hiện sẽ chủ động chặn thay vì tạo package có vẻ dùng được trên máy khách.
- `Test-DesktopSetupPublish.ps1` kiểm thêm worker/lock Piper, Kokoro, voice consistency, updater, .NET của translation worker, native CPU AVX2/AVX/noAVX, license/provenance và file tài khoản/cấu hình riêng ở package root.
- Publish profile bỏ đường output D cá nhân và yêu cầu media bundle. Đường output publish của máy build khác với yêu cầu runtime của máy khách.

### Lệnh chẩn đoán desktop

Thêm `TOOL-LOCAL.exe --check-desktop`, chạy trước giao diện/login. Lệnh không xác thực, kết nối SQL, gọi provider, cài model hoặc mở dự án.

Lệnh kiểm Windows x64, WebView2, probe FFmpeg/OCR bằng fixture đi kèm, đọc trạng thái đã cài của Qwen/Piper. Không chạy inference Qwen/Piper chỉ để thu báo cáo. Nếu Vietsub/giọng bật, các thư mục dữ liệu/component có thể được chuẩn bị theo cơ chế path hiện có; fixture media tạm được dọn sau probe.

Lệnh chẩn đoán và ứng dụng dùng chung factory Qwen/Piper. Factory Piper chọn runtime có version trong thư mục component người dùng và giữ tương thích thư mục legacy. Trong kiểm thử artifact đã phát hiện lệnh chẩn đoán ban đầu chọn nhầm chế độ thư mục Piper; đã sửa, thêm regression test và chạy lại EXE. Không sửa marker hoặc cài lại runtime để biến kết quả thành READY.

Output JSON chỉ có version/trạng thái/mã lỗi, không có exception text, token, connection string, transcript hoặc đường dẫn local. Kết quả `LocalComponentsReady` chỉ nói về các thành phần đã kiểm; các trường `WorkflowAccess=...NOT_CHECKED`, `ServerAccessChecked=false`, `OptionalVoiceEnginesChecked=false` ghi rõ phạm vi chưa kiểm. Không dùng exit code 0 để suy ra đăng nhập/dashboard, Kokoro hoặc Veo voice consistency đã sẵn sàng.

Ví dụ chạy trong thư mục bản publish bằng PowerShell:

```powershell
$checkOutput = Join-Path $env:TEMP ('videomaker-check-' + [Guid]::NewGuid().ToString('N'))
$checkProcess = Start-Process -FilePath '.\TOOL-LOCAL.exe' `
  -ArgumentList '--check-desktop' -Wait -PassThru -WindowStyle Hidden `
  -RedirectStandardOutput ($checkOutput + '.json') `
  -RedirectStandardError ($checkOutput + '.error.txt')
$checkProcess.ExitCode
Get-Content -LiteralPath ($checkOutput + '.json') -Encoding UTF8
```

Exit 0: nhóm component được kiểm đang READY/DISABLED và WebView2 có sẵn. Exit 2: còn thiếu/không đạt một component hoặc nền tảng. Exit 1: không hoàn tất được lệnh chẩn đoán. Cài/repair/inference thật vẫn thực hiện qua System Setup với gate và context nghiệp vụ như cũ.

## Trạng thái task

| Task | Trạng thái trong lượt này |
|---|---|
| T00 | Đã đối chiếu composition và các nhóm SQL thực; chưa đối chiếu phiên bản API/runtime của server đích |
| T01 | Có validation release config và tách preferences; cấu hình không cần SQL chưa có |
| T02 | Đã sửa lưu/read preferences, có regression test; ACL Windows sạch còn chờ |
| T03 | Đã sửa composition Qwen và có test 4 tổ hợp |
| T04 | Giữ gate/lifecycle hiện có, kiểm thử hồi quy; chưa smoke phiên đăng nhập thật trên máy sạch |
| T05 | Có chẩn đoán Windows/WebView2/media/OCR; nghiệm thu native dependency trên máy sạch chưa có |
| T06 | Giữ installer/model pin/resource/fallback hiện hành; không coi model opt-in bị skip là đạt |
| T07 | Đã tăng kiểm bundle/config; kiểm artifact Development và gate release được ghi riêng bên dưới |
| T08 | Có chẩn đoán riêng không đăng nhập và test không lộ exception; lỗi/API compatibility thuộc nhóm API chưa triển khai |
| T09–T18 | Chưa triển khai chuyển workflow qua API hoặc bỏ SQL |
| T19 | Build/test được ghi đúng kết quả mới bên dưới |
| T20–T22 | Chưa nghiệm thu Windows sạch, chuyển máy/update đầy đủ hoặc model/hardware đích |
| T23 | Có hướng dẫn cho phần đã sửa, chưa phải hồ sơ hoàn tất bàn giao/rollout |

## Bản đồ các đường SQL còn hoạt động

Đối chiếu source ở nền commit nêu trên và working tree hiện hành. Đây là kiểm kê phụ thuộc cho T00/T09, chưa phải contract API đã triển khai. `Program` tạo `VideoFactoryDbContextFactory` và truyền vào năm service bên dưới; không chỉ dashboard dùng SQL.

| Đường UI/bridge và service | SQL hiện hành / phần cần API | Phần phải giữ local |
|---|---|---|
| `DashboardBridge` → `ProjectService.ListAsync/GetDashboardAsync/ListAvailableModelsAsync` | Đọc dự án theo owner/org, dashboard scene/character/asset/voice/render và catalog; cần API projection có quyền đọc, phân trang và snapshot | Kiểm file có trên máy, tạo URL preview bằng registry local |
| `CreateAsync/CreateShortVideoAsync` | Ghi project/script/scene/snapshot; cần tạo idempotent, quyền tổ chức và kiểm policy hiện hành | Tạo workspace và reconcile nếu API thành công nhưng tạo thư mục thất bại |
| `UpdateSceneAsync/UpdateCharacterAsync/ApproveCharacterAsync` | Ghi nội dung/version/approval và vô hiệu hóa kết quả cũ; cần expected revision, ownership và chống replay payload khác | Editor, ảnh nguồn, preview |
| `ImportCharacterReferenceAsync` | Ghi metadata reference/asset; cần operation và metadata đã giới hạn, quyền và liên hệ character/project | Kiểm byte/hash/MIME ảnh, copy atomically; không đưa absolute path vào API |
| `ApproveSceneNativeAudioAsync/UnapproveSceneAudioAsync` | Ghi approval/lineage của scene; cần snapshot/check version và xác nhận playback theo nghiệp vụ | Probe/hash file và playback |
| `ProjectGenerationService` → content/repair, voice profile/catalog, speech verification | Request Cloud đã có API nhưng còn đọc SQL trước request, lưu script/scene và materialize/update sau response | Preview, tải qua output proxy, kiểm media/hash và ghép tiếng; retry phần local không submit AI lại |
| `ProjectGenerationService` → reference/first frame/video/voice | Đọc/ghi generation, asset, speech/voice snapshot, trạng thái và approval; còn nhiều đoạn SQL sau polling/download | `.part`, FFprobe, hash, mix, bộ nhớ trạng thái tải; không lấy URL provider gốc |
| `DashboardBridge.ShortVideo`/library → `ShortVideoWorkflowService` | `ProjectAsync`, prepare/resume và lineage còn SQL; settings/quote/compose/approval có API hiện hành để tái sử dụng | Ảnh nhân vật/trang phục, thư viện SQLite và file composition tải về |
| `DashboardBridge.LocalVoice` → `LocalVoiceService` | Đọc source/current, bật policy, duyệt/chọn native/materialize còn SQL; API local-voice hiện chỉ kiểm access | Engine, anchor/job/checkpoint và file giọng; nguồn đổi thì kết quả stale |
| `ProjectRenderService.RenderFinalVideoAsync/ExportFinalVideoAsync` | Đọc Approved/current, tạo/cập nhật RenderJob/FinalVideo/asset, kiểm lại lineage lúc finalize | FFmpeg, hash/audio/duration, file render/export; mất ACK phải reconcile bằng operation |
| `Jobs.SqlJobStore/PersistentJobRunner` | Có source nhưng không thấy composition hoặc nơi tạo instance trong TOOL-LOCAL khi rà toàn bộ `.cs` hiện hành | Giữ legacy, không xóa entity/procedure lịch sử; phải rà lại khi chuyển composition |

API đã có trong source: auth/license/organizations, generation/provider status, project assets, first frames, short-video state/settings/quote/compose/approval, Vietsub registry/Cloud và local-voice access. Chưa thấy API thay toàn bộ list/dashboard/create/edit/approval, snapshot generation và finalize render nêu trên. Chưa truy cập runtime server đích để khẳng định phiên bản API đang deploy.

Đích tách dữ liệu cho T09: DTO public ở `TOOL-SHARED.Contracts`; server giữ owner/org/policy/snapshot/approval và trạng thái operation; desktop giữ bản ghi file theo máy, media và checkpoint. API phải kiểm session/device/license/member/project; mutation dùng revision và operation ID, từ chối Viewer khi không đủ quyền. Không truyền EF entity graph hoặc lệnh SQL qua HTTP. Thiếu endpoint không fallback về SQL trong bản API.

Trước khi bỏ factory ở T18, cần kiểm đủ các đường trong bảng với test cross-user/org, mất ACK/retry, stale revision, mất file, đổi context và compatibility server. Hiện T09 vẫn chưa hoàn tất thiết kế từng DTO/error/version; T10–T18 chưa có source triển khai.

## Kiểm thử và artifact

Artifact log nằm tại `D:\VideoMakerDiagnostics\desktop-portability-implementation-20260918`.

Kết quả trên Windows x64 máy phát triển, nền `main/0a85b569914a9a230425442e9f0b3e1c16072cf1` và thay đổi chưa commit trong hồ sơ này:

| Kiểm tra | Kết quả thực tế |
|---|---|
| `dotnet restore TOOL_GEN_POST_VIDEO.slnx` | Đạt |
| `dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore` | Đạt, 0 Warning / 0 Error của MSBuild |
| Full .NET sau sửa cuối | **1.493 Passed / 0 Failed / 13 Skipped / 1.506 Total**, 4 phút 04 giây |
| `npm ci --no-audit --no-fund`, `npm run build` | Đạt; Vite có cảnh báo chunk trên 500 kB |
| `npm test -- --run` | **268 Passed / 0 Failed / 0 Skipped**, 43 file |
| Publish win-x64 self-contained single-file | Đạt; artifact Development, chưa phải release phân phối |
| `Test-DesktopSetupPublish.ps1` trên artifact cuối | 41 mục kiểm component, 24 web asset, 0 thiếu/sai hash web |
| Fixture package lỗi | 3/3 đạt: thiếu file bắt buộc, lẫn preferences riêng, web không khớp; artifact gốc không bị sửa |
| EXE publish `--check-desktop` | Exit 0; Windows x64 và WebView2 có; media/OCR/Piper READY; Qwen DISABLED |
| FFmpeg Development gate | Đạt hash/version; approval scope Development |
| FFmpeg release gate | Chặn đúng: bundle chỉ được duyệt Development |
| Deployment config gate | Chặn đúng: cấu hình nguồn vẫn phụ thuộc SQL máy phát triển |
| `git diff --check` | Đạt |

Full .NET chạy collection tuần tự bằng `-- xUnit.ParallelizeTestCollections=false`, giữ riêng điều kiện chạy này trong biên bản. 13 bài Skipped là các bài model/runtime hoặc SQL opt-in; không tính là đã kiểm GPU, cài model sạch, Kokoro/Veo voice hoặc database đích. Frontend không thay đổi trong lượt này; đã chạy bộ kiểm thử hiện hành. Không gọi provider có phí hoặc database thật.

Lượt CLI đầu báo Piper `VOICE_RUNTIME_INVALID` do lệnh mới dùng nhầm chế độ đường dẫn. Sau sửa chung factory và publish lại, `desktop-readiness-final.json` + `desktop-readiness-final-exit.json` ghi kết quả READY/Exit 0. Piper dùng runtime đã cài trên máy hiện tại và được kiểm fingerprint; chưa chạy cài đặt sạch hoặc synthesis model thật trong lượt này. `ServerAccessChecked=false`, `OptionalVoiceEnginesChecked=false` và workflow SQL chưa được kiểm qua CLI vẫn được giữ nguyên.

Artifact cuối: `development-publish`, **319 file / 1.053.635.997 byte**; `development-publish-inventory-final.json` ghi SHA-256 từng file. EXE SHA-256: `117B18B4DB147B45B05756D7B64CD7A5C1AB6D74D99EBA00A7EC72C92CD5264D`. Desktop DLL đã build và DLL được test cùng hash `D81307E4A80175DF6E2CC25630A406ADBF32D11460E07EF5C1A70DE4A3860111`; test assembly hash `CC7852122A57DA620252B602A114C2E2E6393BCDF535727ABE7A6722ECBBC19A`.

Log cuối: `build-final.log`, `full-suite-verified.log/.trx`, `frontend-tests.log`, `publish-final.log`, `publish-components-final.json`, `publish-negative-cases.json`; thông tin tổng hợp ở `verification.json`. Các log `full-suite`, `full-suite-final` và inventory không có hậu tố `final` thuộc những lượt trước sửa factory Piper, không thay thế kết quả cuối. Thư mục `negative-publish-candidate` chỉ là fixture cố ý chứa preferences riêng, không được dùng để cài hoặc phát hành.

## Phần cần hoàn tất để đạt mục tiêu chuyển máy

1. Chốt phương án API workflow đầy đủ hoặc SQL chuyển tiếp; không tự coi việc sửa Qwen/preferences đã giải quyết SQL.
2. Nếu API: thực hiện T09–T18, kiểm compatibility/ownership/revision/idempotency và triển khai server tương thích trước bản desktop không SQL. Nếu SQL chuyển tiếp: cần SQL đích, cơ chế danh tính/mạng và quyền hạn đúng môi trường, không nhúng password dùng chung vào package.
3. Hoàn thành điều kiện phát hành bundle và tạo lại artifact cuối từ source/config đã nghiệm thu; không phát hành bản Development trong hồ sơ này.
4. Nghiệm thu T20–T22 trên máy được xác định, đăng nhập thiết bị mới và chuyển media/workspace khi cần. Test tự động trên máy phát triển không thay thế các bước này.
