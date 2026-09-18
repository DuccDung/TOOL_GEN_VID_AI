# Rà soát TOOL-LOCAL trên máy mới — 2026-09-18

## Phạm vi đã làm rõ: chỉ TOOL-LOCAL

Theo yêu cầu tiếp theo của người dùng, coi server đã được deploy; chỉ đánh giá ứng dụng desktop khi đưa sang máy Windows khác. Không dùng số release trên server, cấu hình TOOL-SETUP hoặc trạng thái duyệt phát hành FFmpeg làm bằng chứng rằng runtime TOOL-LOCAL không chạy. Các phát hiện đó trong phần rà soát ban đầu bên dưới thuộc phạm vi phân phối/phát hành, không phải kết luận của lượt kiểm tra riêng desktop này.

Đã đọc lại source trên `main`, HEAD `9646a7b4eaf271f79bd3278b36a96e0932661c58` cùng working tree chưa commit. Không sửa source/cấu hình, không gọi lại server và không chạy thêm build/test trong lượt làm rõ này.

**Kết luận riêng TOOL-LOCAL: có nền tảng đóng gói để chạy trên máy khác, nhưng cấu hình/source hiện tại còn hai vấn đề trực tiếp; chưa đủ bằng chứng xác nhận máy mới chạy ổn định.**

1. **Client còn gọi trực tiếp SQL của máy phát triển.** `DesktopOptions.Load` bắt buộc cấu hình database; `Program.cs:118` tạo factory SQL; `DashboardBridge.cs:1803` gọi `ProjectService.ListAsync` để lấy dự án qua EF/SQL. Cấu hình gốc trỏ `DUNGDEV`, dùng Windows Authentication; cấu hình riêng hiện tại không ghi đè database. Việc server API đã deploy không tự thay đổi đường gọi này trong desktop. Đây là phụ thuộc của TOOL-LOCAL cần giải quyết trước khi máy khách chỉ có kết nối HTTPS có thể sử dụng đầy đủ dashboard/workflow.
2. **Dịch local và gate Qwen chưa đồng nhất trong bản publish mặc định.** Cờ dịch trong `appsettings.json` tắt; máy phát triển bật qua `appsettings.user.json`, file được chủ động loại khỏi publish. `Program` vẫn tạo Qwen provider và đưa vào Setup khi Vietsub bật, nên gate vẫn yêu cầu component này sẵn sàng dù registry dịch bị tắt. Không sửa bằng cách chép nguyên cấu hình riêng sang máy khách; cần cấu hình phát hành rõ ràng và điều kiện gate thống nhất với feature flag.

Các phần desktop đã hỗ trợ theo source:

- `win-x64`, `SelfContained=true`; có target đóng gói frontend, worker dịch, updater, FFmpeg và tài nguyên OCR. Cần chuyển **đầy đủ thư mục publish**, không chỉ tệp EXE.
- Workspace và WebView2 user data dùng `%LOCALAPPDATA%`; workspace mở rộng biến môi trường, không buộc có ổ D hoặc thư mục source của máy phát triển.
- Kiểm tra Windows x64 và WebView2 trước đăng nhập. Máy thiếu WebView2 phải cài Evergreen x64 qua hướng dẫn; app chưa tự cài runtime này.
- Model Qwen và runtime/giọng cần cài hoặc kiểm tra lại trên máy mới; không dùng trạng thái READY của máy cũ làm chứng nhận cho máy mới. Dữ liệu/media dự án local cần chuyển riêng nếu tiếp tục dự án cũ.

Kết quả **120 Passed / 0 Failed / 3 Skipped** ở phần kiểm thử bên dưới là lượt audit trước trên máy phát triển, không phải một lượt kiểm thử mới hoặc chứng nhận Windows sạch. Chưa nghiệm thu bản publish trên máy đích với tài khoản Windows thường, OCR, dịch CPU/GPU, giọng và xuất MP4.

## Phụ lục: lượt rà soát ban đầu có cả phân phối/phát hành

## Kết luận

**Chưa đủ điều kiện bàn giao với cam kết cài lên máy Windows khác là hoạt động ổn định.** Source có hỗ trợ đóng gói Windows x64 kèm .NET, bộ cài/update, workspace theo tài khoản Windows và cài runtime riêng. Tuy nhiên cấu hình SQL còn phụ thuộc máy phát triển; luồng phát hành Stable chưa có package; cờ dịch local và điều kiện Setup chưa đồng bộ.

TOOL-LOCAL hiện là client của TOOL-SERVER, không phải ứng dụng offline hoàn toàn. Việc cài được trên máy khác không loại bỏ yêu cầu đăng nhập, license/device, organization, server API và SQL workflow đang tồn tại trong kiến trúc.

Rà soát trên nhánh `main`, HEAD `9646a7b4eaf271f79bd3278b36a96e0932661c58`, working tree chưa commit, có các thay đổi dịch local/vùng che/popup gói từ những lượt trước. Lượt này không sửa source hoặc cấu hình; chỉ đọc, chạy kiểm thử có sẵn, kiểm bundle FFmpeg và GET endpoint danh sách phiên bản công khai. Không chạy migration, đăng nhập tài khoản thật, phát sinh phí, cài model hoặc publish/upload release.

## Các vấn đề đã xác nhận

### 1. Desktop vẫn truy cập SQL trên máy DUNGDEV bằng Windows Authentication

- `TOOL-LOCAL/appsettings.json:6` có data source `DUNGDEV`, database `VideoFactory`, Integrated Security bật. Báo cáo không chứa connection string đầy đủ.
- `DesktopOptions.Load` yêu cầu cấu hình database. `Program.cs:121` tạo `VideoFactoryDbContextFactory`; factory dùng `UseSqlServer`.
- `DashboardBridge.RefreshAsync` gọi `_projectService.ListAsync` trước khi trả dashboard; `ProjectService.ListAsync` trực tiếp query EF/SQL. Đây là đường khởi động thực, không chỉ là model/đường gọi legacy còn lưu trong repository.

**Tác động:** Máy khác phải tìm thấy và kết nối được SQL này, đồng thời danh tính Windows phải được cấp quyền tương ứng. Chỉ kết nối được API `https://taphoatool.com/` chưa đủ để dashboard/workflow hoạt động. Chưa xác minh kết nối SQL hoặc quyền trên máy đích.

**Hướng xử lý:** Chốt cấu hình triển khai SQL chuyển tiếp có quyền giới hạn cho môi trường đích; hướng loại bỏ phụ thuộc máy phát triển là chuyển các thao tác workflow còn truy cập SQL trực tiếp sang server API.

### 2. Kênh Stable/win-x64 trên server chưa trả về phiên bản để cài

GET không xác thực lúc `2026-09-18T04:26:44Z` đến endpoint `https://taphoatool.com/api/launcher-distribution/versions?channel=Stable&platform=win-x64` trả HTTP 200, JSON hợp lệ, **0 release**.

`LauncherInstallerService.GetVersionsAsync` dùng endpoint này. `InstallerForm.cs:60` hiển thị “Server chưa có release có package.” và tắt nút cài nếu danh sách rỗng.

**Tác động:** Ngay cả khi bộ cài đã trỏ đúng server, tại thời điểm kiểm tra vẫn chưa có gói Stable/win-x64 để lựa chọn. Không suy ra các kênh khác cũng rỗng. Không tải package hoặc kiểm tra Admin/database để đoán trạng thái release bị ẩn.

### 3. Bản publish sẽ mất lựa chọn bật dịch local của máy phát triển; Setup vẫn yêu cầu Qwen

- `TOOL-LOCAL/appsettings.json:26`: `VietsubLocalTranslationEnabled=false`.
- `TOOL-LOCAL/appsettings.user.json`: máy hiện tại ghi đè cờ này thành `true`.
- `TOOL-LOCAL.csproj:64–66`: file cấu hình riêng được copy khi build nhưng `CopyToPublishDirectory=Never`. Việc không phát hành cấu hình riêng là đúng; cần cấu hình release riêng được nghiệm thu.
- `Program.cs:249` vẫn tạo `QwenGgufVietsubTranslationProvider` khi Vietsub bật, độc lập với cờ dịch local. Cờ chỉ được chuyển vào registry tại dòng 254.
- `Program.cs:348` truyền provider này vào `QwenSetupAdapter`. Adapter chỉ trả `DISABLED` khi provider là null. `StartupSystemSetupWorkflow.RequiredComponents` yêu cầu mọi component không `DISABLED` đạt `READY`.

**Tác động theo đường gọi source:** Với cấu hình publish mặc định, người dùng thuộc role phải qua Setup có thể bị yêu cầu cài/probe Qwen khoảng 2,5 GB dù dịch local vẫn bị feature flag tắt. Đây là bất nhất cần sửa và có test composition cho cả flag bật/tắt; test adapter độc lập chưa bảo vệ trường hợp này. Chưa tái hiện toàn luồng trên một Windows sạch.

Không khắc phục bằng cách tự bật flag an toàn hoặc chép toàn bộ cấu hình riêng của máy phát triển sang release.

### 4. Địa chỉ mặc định của Setup khác địa chỉ desktop

- Desktop hiện trỏ `https://taphoatool.com/`; không còn mặc định localhost trong `TOOL-LOCAL/appsettings.json` đang rà soát.
- `TOOL-SETUP.csproj:17`, `setupsettings.json` và `Publish-DesktopRelease.ps1:13` vẫn mặc định `https://localhost:7202/`.
- Tham số `-ServerBaseUrl` của script được gắn vào metadata của Setup; nó không tự sửa `Server.BaseUrl` trong cấu hình desktop. `-AppSettingsPath` là cơ chế ghi đè cấu hình package riêng.

**Tác động:** Build/publish Setup bằng mặc định sẽ tìm server ngay trên máy khách. Khi phát hành cho một server khác, cần đồng bộ rõ endpoint của Setup và desktop; script hiện chưa kiểm chúng có trùng môi trường.

### 5. Quy trình publish đang bị chặn bởi trạng thái duyệt bundle FFmpeg

Đã chạy thực tế `Test-FfmpegBundle.ps1` trên bundle source:

- Chế độ Development: **Passed**, version/hash/provenance hợp lệ.
- Với `-RequireReleaseApproval`: **bị chặn**, thông báo `FFmpeg bundle is approved for Development only and cannot be published.`

`third_party/ffmpeg/win-x64/PROVENANCE.md` ghi `Approval scope: Development`. `Publish-DesktopRelease.ps1` gọi kiểm tra Release ngay đầu luồng. Đây là điều kiện phát hành chưa hoàn tất; không phải bằng chứng FFmpeg không chạy. Không sửa hồ sơ duyệt hoặc bỏ qua cổng kiểm tra trong lượt rà soát.

## Những phần đã hỗ trợ chuyển máy

| Phần | Bằng chứng source và giới hạn |
|---|---|
| .NET và frontend | Desktop/Setup/Updater cấu hình `SelfContained=true`, `win-x64`; web được build vào `wwwroot`. Máy người dùng không cần môi trường Node/Visual Studio để chạy package đã publish đúng. |
| Workspace | `%LOCALAPPDATA%\ToolGenPostVideo\workspace`; source mở rộng biến môi trường, không cố định vào ổ D của máy phát triển. |
| WebView2 | Kiểm trước màn hình đăng nhập. Nếu thiếu, có hộp thoại hướng dẫn cài Evergreen x64 từ Microsoft và kiểm tra lại; runtime này chưa được đóng gói/cài tự động cùng app. |
| OCR và FFmpeg | Có source copy native/model OCR, ảnh probe và bộ FFmpeg vào package; Setup chạy kiểm tra thực trước `READY`. Chưa chứng minh dependency native đầy đủ trên mọi Windows sạch. |
| Python/giọng | Piper có cơ chế cài Python riêng và dependency khóa phiên bản/hash; không yêu cầu Python hệ thống. Lần cài đầu cần truy cập nguồn tải. Kokoro và các giọng bổ sung có runtime/model riêng, không suy ra đã sẵn sàng từ Piper. |
| Qwen/GPU | Model tải riêng, kiểm size/hash; worker tách tiến trình, chọn CPU AVX2/AVX/noAVX và có luồng GPU/fallback. Hiệu năng và dung lượng phải được probe trên máy đích; kết quả RTX 3050 của máy hiện tại không chứng nhận mọi GPU. |
| Update/cài đặt | Có SHA-256, kiểm manifest, bảo vệ đường dẫn và backup/rollback. Test tự động không thay thử cài/update/rollback bản phát hành thật trên Windows sạch. |

## Khi chuyển tài khoản và dự án đang có

- Máy mới tạo danh tính thiết bị riêng. License phải còn chỗ kích hoạt; gói một thiết bị có thể cần thao tác quản lý thiết bị cũ. Chưa truy cập license thật trong lượt này.
- Token và device identity dùng DPAPI theo Windows user. Không coi việc chép các file này là đăng nhập/kích hoạt được trên máy mới.
- Workspace, video, cue và audio Vietsub nằm local; cài ứng dụng hoặc đăng nhập cùng tài khoản không tự chuyển những dữ liệu này từ máy cũ.
- Media `COPY` nằm theo đường dẫn tương đối trong workspace; `LINK` giữ đường dẫn tuyệt đối của nguồn. Máy mới phải có dữ liệu tương ứng hoặc người dùng liên kết lại. Nguồn kiểm tra: `VietsubMediaImportService.ResolveEffectivePath`.
- Marker runtime gắn với máy/tài khoản/hash; model/runtime chuyển sang máy mới cần kiểm lại, không chép marker để coi là `READY`.

## Kiểm thử trong lượt rà soát này

Chạy trên Windows máy phát triển, binary Release khớp lần build trước trong cùng working tree:

```powershell
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build `
  --filter 'FullyQualifiedName~TOOL_TESTS.SystemSetup|FullyQualifiedName~TOOL_TESTS.Updates|FullyQualifiedName~LicenseSessionManagerTests|FullyQualifiedName~VietsubTranslationRuntimeTests|FullyQualifiedName~VietsubModuleShellTests|FullyQualifiedName~LocalVoiceDeploymentTests|FullyQualifiedName~MediaToolPreflightServiceTests' `
  -- xUnit.ParallelizeTestCollections=false
```

Kết quả mới: **120 Passed / 0 Failed / 3 Skipped / 123 Total**, 15 giây. Ba bài bị bỏ qua là Qwen inference thật, Qwen benchmark và cài Piper từ runtime sạch. Có kiểm OCR/FFmpeg trên máy hiện tại; không giả định máy đích đã có native dependency giống máy phát triển.

Không build hoặc chạy lại toàn suite trong lượt chỉ rà soát này. Kết quả full suite từ lượt popup gói trước có một failure Timeline; không dùng số test subset ở đây để đổi full suite thành Passed.

Hash DLL được đối chiếu:

- Desktop: `2E0F6D868FB3B12DDB8F4262BDB96F49A13CE14DB737DBC00C2186372BDEA3ED`.
- Tests: `8393566C52F7A2BD34012B0765B9844B05E533F420163C4B9A96D29C9D40F4C8`.

Artifact: `D:\VideoMakerDiagnostics\desktop-portability-audit-20260918`, gồm log/TRX, kiểm FFmpeg và kết quả GET danh sách release. Chưa nghiệm thu máy/VM sạch, không xác minh SQL trên máy đích, không gọi AI có phí hoặc rollout.

## Thứ tự cần hoàn tất trước bàn giao

1. Giải quyết phụ thuộc SQL/DUNGDEV và chuẩn hóa cấu hình đích; thống nhất endpoint Setup/desktop.
2. Sửa điều kiện Setup theo cờ dịch local, chốt những tính năng được bật trong cấu hình release riêng.
3. Hoàn tất điều kiện duyệt bundle, tạo package đúng quy trình và kiểm nội dung trước khi upload/publish theo môi trường được phép.
4. Nghiệm thu bản package trên Windows x64 sạch với user thường: WebView2, đăng nhập/license, dashboard, cài model/Python, OCR/dịch/giọng/xuất MP4, thiếu mạng/thiếu đĩa, restart, update và rollback.
5. Chỉ kết luận ổn định trên các cấu hình máy đã qua nghiệm thu; giữ riêng kết quả CPU/GPU và các tính năng chưa đủ model/provider.
