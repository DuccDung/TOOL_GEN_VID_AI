# Kiến trúc kỹ thuật VideoMaker

## Chuẩn hóa desktop cho máy mới — 2026-09-18

`DesktopComponentComposition` thống nhất điều kiện Vietsub + dịch local giữa provider và registry; provider null khiến Qwen Setup DISABLED. `DesktopUserSettingsStore` ghi lựa chọn đồng bộ lời nói atomically vào `%LOCALAPPDATA%/ToolGenPostVideo/settings/preferences.json`, đọc fallback legacy nhưng chỉ áp dụng khóa được phép, không chép endpoint/database/đường dẫn riêng. Cấu hình phát hành và override máy phát triển vẫn tách riêng.

`DesktopReadinessCommand` xử lý `--check-desktop` trước WinForms/login, không tạo factory SQL hoặc gọi API. Nó kiểm nền tảng/WebView2, probe fixture media/OCR và đọc bằng chứng runtime Qwen/Piper hiện có; không tải model hay suy ra toàn bộ app đã sẵn sàng từ component readiness. Luồng desktop thông thường vẫn cần SQL; nhóm chuyển workflow qua API chưa triển khai. Script publish kiểm endpoint/config/bundle, yêu cầu lựa chọn SQL chuyển tiếp rõ ràng và không cho xóa connection setting để giả lập bản chỉ dùng API. [Trạng thái và kiểm chứng](TRIEN_KHAI_TOOL_LOCAL_TREN_MAY_MOI.md).

## Thông tin gói trong desktop — 2026-09-18

Hai nút ở Sidebar/Header mở `LicenseInformationDialog`: thông tin hiện tại lấy từ `dashboard.license`, danh sách gói qua bridge `license.offers.get` → `DashboardBridge.GetLicenseOffersAsync` → API thanh toán hiện hành. `useLicenseInformation` giữ request ID riêng; App định tuyến phản hồi của popup trước handler chung để lỗi đọc dữ liệu không thay busy/payment state. Đóng/đổi tài khoản/timeout bỏ qua phản hồi cũ; license gate, Setup và thông báo hệ thống có ưu tiên. Native dialog khóa nền, bổ sung vòng Tab/Shift+Tab, Esc đóng và trả focus về nút mở. Danh sách rỗng/lỗi có thử lại, timeout đọc 30 giây. Chỉ xem giá/quyền lợi server trả về, không POST tạo thanh toán; không đổi DTO, endpoint, schema hoặc cấu hình SePay.

## Bổ sung 2026-09-11: toàn bộ video ngắn dùng Veo

`ShortVideoVeoPolicy` yêu cầu Fal/Veo 3.1 Standard/Fast, 720p, Native Audio, 4/6/8 giây và 9:16/16:9. `DirectShortVideo` dùng policy `LongForm` hiện có nhưng vẫn giữ workflow một cảnh, không áp content/speech rewrite của video dài. `GenerationService` luôn kiểm `SceneFirstFrame` Approved/current; ảnh mặc thử được ánh xạ thành `MediaAsset`/`SceneFirstFrame` qua `ShortVideoVeoFirstFrame`, giữ provider request/hash/revision nguồn. TextOnly đi qua `SceneFirstFrameService`, rồi quote video bền vững trong `vf.ShortVideoOperations`; desktop chỉ gửi quote đã xác nhận với idempotency key tương ứng. Bridge cũ `short-video.generate` không còn tạo clip trực tiếp.

API `migrate-veo` chuyển snapshot dự án sau xác nhận; transaction kiểm pending requests, runtime/rate, thời lượng/tỷ lệ, vô hiệu quote/kết quả duyệt cũ, giữ ảnh mặc thử khi phù hợp. Không cập nhật hàng loạt database hoặc đổi provider ngầm. Native kiểm lineage ảnh đầu cảnh cho cả hai mode trước duyệt/render/export. Xem [biên bản triển khai](TRIEN_KHAI_VIDEO_NGAN_VEO.md).

## Bổ sung 2026-09-11: thư viện và composer video ngắn

`ShortVideoAssetLibraryService` quản lý SQLite/ảnh/thumbnails riêng theo user + organization trong AppData. `short-library.*` qua `DashboardBridge.ShortVideoLibrary`; `short-library.app.local` qua resolver native kiểm scope/hash, không map cả thư mục. Project nhận bản sao ảnh đầy đủ đã kiểm, giữ ref/version trong draft local. Draft dùng revision và ID tạo project bền vững; `CreateShortVideoAsync` kiểm payload/scope khi retry cùng ID. Composer React dùng cùng bố cục cho draft và project, vẫn giữ quote/approval server. Không thêm SQL Server migration hoặc đường gọi provider từ desktop. [Chi tiết](TRIEN_KHAI_UI_THU_VIEN_VIDEO_NGAN.md).

## Bổ sung 2026-09-10: DirectShortVideo/CharacterOutfit

`ShortVideoOutfitContracts` định nghĩa metadata nguồn, settings/revision, quote ảnh/video và approved composition input. `ShortVideoOutfitService` cùng controller tại `api/generation/short-video` giữ access, quote/claim/idempotency/rate/budget/credential và approval ở server. Migration 4.1.9 thêm `vf.ShortVideoOutfits` và `vf.ShortVideoOperations`, không cấp SQL desktop cho hai bảng này. Request snapshot giữ hash/role nguồn, model/rate/credential version; binary ảnh kết quả dùng `GeneratedImageOutputs`/retention và authenticated content endpoint hiện hành.

Native `ShortVideoWorkflowService` quản lý hai ảnh trong workspace, kiểm orientation/MIME/size/hash, lưu quote video trước submit và kiểm lineage. React component riêng nối qua `DashboardBridge.ShortVideo`; không nhận absolute path/provider key/Base64. Từ 2026-09-11, `GenerationService` gửi ảnh phối đồ Approved/current qua `SceneFirstFrame` thật sang Fal/Veo, giữ worker/output proxy và toàn bộ gate first-frame. Render/export kiểm lại composition/revision, approved generation và manifest. Chất lượng ảnh/video thật còn cần nghiệm thu; [biên bản cơ chế phối đồ ban đầu](TRIEN_KHAI_VIDEO_NGAN_NHAN_VAT_TRANG_PHUC.md), [triển khai Veo hiện hành](TRIEN_KHAI_VIDEO_NGAN_VEO.md).

## Bổ sung 2026-09-09: local Veo voice consistency

React LocalVoicePanel → bridge local-voice.* → LocalVoiceService. Server POST /api/generation/local-voice/access chỉ kiểm session/device/license/membership/role/project qua access service hiện hành; không reserve budget, gọi provider hoặc nhận media.

Policy snapshot là vf.Projects.LocalVoicePolicyVersion; JSON checkpoint, voice samples và model nằm trong workspace local. Desktop chỉ ghi workflow vf, không thêm quyền ai/auth/vs. LocalVoiceRuntime chạy Python riêng với môi trường lọc, không provider key/Cloud client; installer và dependency/model lock là đường tải component duy nhất. Worker dùng profile CPU OpenVoice V2 converter + Silero + Demucs, không gọi module TTS.

Từ 2026-09-10, cấu hình native `LocalVoice:ComponentRoot` và `LocalVoice:TemporaryRoot` cho phép đặt model/cache/temp trên ổ riêng, không chuyển media workspace. Đường dẫn phải là thư mục local đầy đủ, không qua reparse point; temp nằm ngoài component manifest. Installer và worker nhận TEMP/TMP trong môi trường tiến trình đã lọc. Desktop có các lệnh bảo trì `--prepare-local-voice`, `--verify-local-voice`, `--check-local-voice` dùng cùng runtime/checksum/probe, không đăng nhập, truy cập database, bật project hay chuyển media; thao tác project vẫn qua server access như cũ.

Profile CPU Windows chạy một luồng, tắt oneDNN/MHA fastpath và dùng math SDPA: model thật trên máy đích đã tái hiện access violation tại `torch._native_multi_head_attention` trong Demucs; chỉ tắt MHA fastpath vẫn chưa ổn định ở bước tách âm. Worker dùng triển khai attention chuẩn với cùng trọng số đã ghim; fingerprint worker thay đổi sẽ yêu cầu probe lại và làm checkpoint cũ hết hiệu lực. Chỉ số RAM/CPU do chính tiến trình model báo, tránh đo nhầm launcher của Python venv.

Giới hạn đoạn Demucs 4 giây được đặt cả trên `model.segment` và lời gọi `apply_model`: HTDemucs tự đệm theo `model.segment` trong forward, nên chỉ chia input thành đoạn 4 giây ở bên ngoài vẫn có thể cấp phát attention theo đoạn training dài hơn. Đây là giới hạn tài nguyên của profile CPU; chất lượng âm nền và giọng vẫn cần nghe nghiệm thu.

LocalVoiceStore kiểm path/reparse/hash, atomic checkpoint, khóa project liên tiến trình. Runtime có khóa component, xác minh byte trước khi chạy Python; worker nhận request riêng của host, kiểm lại model ghim và chặn socket API trong inference (không phải sandbox cấp hệ điều hành). LocalVoiceMedia kiểm duration/audio/stream và promote MP4 từ .part. Asset được duyệt là SceneVideoVoiceConverted, lưu lineage metadata; ProjectRenderService kiểm lại clip, mẫu và source trước và sau render.

Module Cloud Fal LipSync đã bị loại bỏ, gồm provider client, worker, input store, API/cấu hình và cờ build thử nghiệm. Migration cũ được giữ làm lịch sử; trạng thái `SpeechReadyForLipSync` chỉ được đọc tương thích, không phát sinh mới và không thay thế bằng chứng duyệt WAV.

> Mô tả ranh giới module và đường gọi; rà soát source tại commit `8f10cc9` ngày 2026-09-15. Các báo cáo runtime/môi trường nằm trong `BOI_CANH_HE_THONG_HIEN_HANH.md`.

## 1. Tổng thể

```text
React/WebView2 -> WinForms desktop -> TOOL-SERVER API -> AI providers
       |                |                  |               |
       |                |                  +-> SQL Server  +-> OpenAI
       |                +-> workspace                     +-> Kling
       |                +-> FFmpeg/FFprobe                 +-> BytePlus
       |                +-> OCR                            +-> Fal/Veo
       |                +-> translation worker x64
       +-> UI state/commands
```

Desktop không gọi AI provider trực tiếp. Server là trust boundary cho auth, license, tổ chức, pricing, budget, credential, request log, worker và output proxy. Xử lý media, subtitle và translation model diễn ra local.

## 2. Module

| Project/thư mục | Trách nhiệm chính |
|---|---|
| `TOOL-SERVER` | ASP.NET Core API, Razor Admin, auth/license, tổ chức, AI governance, provider adapters, hosted workers, output cache/proxy, release API |
| `TOOL-LOCAL` | WinForms host, React/WebView2 UI, gateway client, workflow project, System Setup, TikTok native upload, Bilibili, workspace, download/verify/render và Vietsub |
| `TOOL-SHARED.Contracts` | Request/response DTO và enum public giữa server/desktop |
| `TOOL-VIETSUB-TRANSLATION-WORKER` | Tiến trình `net10.0-windows` x64 chạy LLamaSharp/Qwen qua IPC local |
| `TOOL-DISTRIBUTION` | Manifest, provenance và kiểm tra SHA-256 bundle |
| `TOOL-TESTS` | xUnit xuyên module |
| `TOOL-UPDATER` | Download/update có backup và rollback |
| `TOOL-SETUP` | Launcher/bộ cài desktop |
| `database` | Initial schema, migration tuần tự đến 4.1.9, verify và desktop least-privilege role |
| `scripts` | Kiểm tra runtime assets, model, bundle, publish và test opt-in |

`TOOL-SERVER`, `TOOL-LOCAL`, `TOOL-SHARED.Contracts`, `TOOL-TESTS` và `database` có `AGENTS.md` cục bộ; quy tắc gần nhất áp dụng cùng file root.

### Bản đồ đường gọi theo nghiệp vụ

| Tác vụ | UI/native | API/server | Dữ liệu và worker |
|---|---|---|---|
| Tạo video dài | `Web/src/App.tsx` → `Form1`/`DashboardBridge` → `ProjectGenerationService`/`ServerGenerationClient` | `GenerationController` → `GenerationService` → `GenerationAccessService`, pricing/budget, adapter | Project/scene/request trong `vf` và ledger `ai`; `VideoPollingWorker` poll/cache/settle; desktop tải proxy rồi duyệt/render FFmpeg |
| Tạo video ngắn | `TextShortVideo`/`OutfitShortVideo` → bridge video ngắn → `ShortVideoWorkflowService` | API `short-video`/`SceneFirstFrames` và `GenerationController`; `ShortVideoVeoPolicy` buộc Fal/Veo | `vf.ShortVideoOperations` giữ quote cả `TextOnly`; `vf.ShortVideoOutfits` giữ mode phối đồ; first frame và clip có lineage/approval riêng |
| Vietsub local | `VietsubPage` → `VietsubWebBridge` | Registry metadata `VietsubProjectsController`; không gửi media/cue local lên server | Workspace/manifest/SQLite/SRT, PaddleOCR, Qwen worker x64, Piper Python và FFmpeg ở desktop |
| Dịch Vietsub Cloud | `VietsubPage` → native snapshot/`VietsubCloudTranslationClient` | `VietsubCloudTranslationsController` → service/worker → OpenAI | Chỉ snapshot text giới hạn vào `vs` được mã hóa/retention; budget/ledger trong `ai`; desktop apply CAS và SRT atomic |
| Đăng TikTok | `TikTokPage` → `TikTokWebBridge`/`TikTokUploadService` | `TikTokController`/service/OAuth, `TikTokPublishingWorker` | OAuth/token/job trong `social`; video/path và signed upload URL chỉ dùng ở desktop native |
| Tải Bilibili | `BilibiliPage` → `BilibiliWebBridge`/`BilibiliService` | Không có provider AI/budget request | Runtime downloader local, hàng đợi theo phiên, `.part`/checksum/probe rồi promote MP4 |

Các bảng trên là bản đồ source; chúng không khẳng định runtime, migration hay credential đã sẵn sàng ở môi trường đích.

## 3. Trust boundary và quyền sở hữu dữ liệu

### Server sở hữu

- user, session, device, license và refresh token metadata;
- organization, membership, role, budget và member limit;
- provider catalog, model, pricing/rate snapshot và policy;
- encrypted credential cùng version/lifecycle;
- AI request, provider request, idempotency, reservation và usage ledger;
- project registry/server workflow trong SQL;
- Vietsub project registry metadata;
- TikTok Developer App credential dùng chung được Data Protection bảo vệ, kết nối OAuth/token theo user, integration policy và metadata publish job trong schema `social`;
- output cache và release metadata.

### Desktop sở hữu

- UI state và selection hiện hành;
- workspace media, file `.part`, thumbnail/waveform và render output;
- manifest/SQLite/SRT/OCR artifact của Vietsub;
- lựa chọn file, absolute path, preview và byte video dùng để upload trực tiếp TikTok;
- cấu hình máy phát triển không chứa provider secret.

Desktop còn kết nối SQL trực tiếp cho workflow schema `vf` trong giai đoạn chuyển tiếp. Nó không được có quyền đọc/ghi bảng sự thật `auth`, `ai`, credential/usage hoặc schema `vs`.

## 4. Server

### 4.1 Khởi động và dependency injection

`Program.cs` đăng ký authentication/authorization, rate limiting, Data Protection, các DbContext, service nghiệp vụ, provider HTTP client, output proxy/cache và hosted worker. Startup cũng bootstrap catalog cần thiết, nên không chạy server trước khi database đúng phiên bản và cấu hình môi trường đã sẵn sàng.

Data Protection key được lưu trong database để credential đã mã hóa tiếp tục giải mã được giữa restart/instance. Việc mất key ring làm mất khả năng dùng credential; key ring phải nằm trong backup/restore plan.

### 4.2 DbContext và schema

Server tách phạm vi dữ liệu bằng các context chính:

- `AccountDbContext`: tài khoản, session, device, license và payment-related account state.
- `DataProtectionKeyDbContext`: key ring ASP.NET Core Data Protection.
- `AiGovernanceDbContext`: organization, membership, provider catalog, pricing, budget, credential và usage.
- `ProviderAdminDbContext`: thao tác quản trị provider/catalog.
- `VideoFactoryDbContext`: project, scene, asset và generation workflow.
- `VietsubDbContext`: registry `vs.Projects`, job/batch/attempt Cloud và payload/result tạm được mã hóa; không thay database biên tập local.
- `TikTokDbContext`: app credential/version, integration settings, nhiều connection theo user/app/OpenId, OAuth session có target connection, publish attempt và publish job trong schema `social`; audit quản trị ghi vào `auth.AccountAuditLogs`.

Database dùng các schema nghiệp vụ `auth`, `ai`, `vf`, `vs`, `social` cùng các bảng cần thiết trong `dbo`. Ranh giới DbContext là ranh giới ownership trong code, không thay thế quyền SQL và transaction thích hợp. Migration 4.1.9 thêm `vf.ShortVideoOperations` (quote ảnh/video, kể cả `TextOnly`) và `vf.ShortVideoOutfits`; script DENY trực tiếp quyền CRUD của desktop role lên cả hai bảng. Không chạy binary server mới trước khi xác minh migration, schema version và quyền trên database đích, vì startup bootstrap catalog có thể ghi dữ liệu.

### 4.3 Nhóm API

Các nhóm endpoint chính gồm:

- auth, session, device, password reset và license lease;
- organization selection, member/role, budget và usage;
- Global Admin/Admin cho provider model, rate, credential, pool và release;
- project/content/character/asset/scene generation;
- video submit/status/retry/approve và output proxy;
- SePay payment order/webhook/status;
- Vietsub registry metadata.
- TikTok state/OAuth/creator info/direct-post init/status theo user và device hiện hành; API Global Admin quản lý credential dạng write-only, mở cửa sổ xác minh và policy public posting.

Contract public nằm ở `TOOL-SHARED.Contracts`; thay contract phải cập nhật server, desktop và test cùng lúc.

### 4.4 Hosted worker

Server có các background worker cho request/provider polling, settlement/release, output caching/cleanup và các quy trình nền liên quan. Worker dùng claim lease để nhiều instance không xử lý cùng bản ghi, có giới hạn attempt/age và chỉ chuyển trạng thái tiến tới terminal.

Task provider tiếp tục chạy sau khi desktop đóng. Desktop reconnect bằng status API/idempotency thay vì gửi lại request mới tùy tiện.

`TikTokPublishingWorker` polling các publish job chưa terminal theo batch, luôn dùng `job.TikTokConnectionId`. Worker claim bằng `NextPollAtUtc` trước outbound để phối hợp nhiều instance; claim hết hạn sau 5 phút nếu tiến trình chết, lượt tiếp theo sau 30 giây. API trạng thái chỉ đọc SQL, không gọi provider theo tần suất refresh của desktop. Signed upload URL chỉ dùng trong desktop native để chuyển byte file local; worker/server không đọc file người dùng và không trả URL này cho React.

`TikTokOperationLock` dùng SQL Server session application lock trên connection riêng cho OAuth theo user, refresh/reconnect/disconnect theo connection và publish theo user/request ID. Không giữ transaction SQL qua HTTP. `TikTokPublishAttempts` được commit trước Direct Post init; payload hash gắn account và metadata. Attempt chưa xác định kết quả không được submit lại sau restart.

Bridge TikTok gắn request ID, connection ID, media ID và ID lần đăng; snapshot file trước init, kiểm tra lại trước upload. React bỏ phản hồi creator/history đến muộn, giữ job theo ID và tài khoản. Avatar đi qua proxy có ownership, exact HTTPS host/443, DNS public pinning, không redirect, giới hạn MIME/signature/1 MiB; native đưa ảnh qua virtual host nội bộ. URL CDN có chữ ký được mã hóa, không sang React và HTTP client tải avatar tắt log URL.

`TikTokCredentialRuntime` ưu tiên credential database `Active`; cấu hình Client Key/Secret tĩnh chỉ là đường tương thích legacy. OAuth session snapshot `TikTokAppCredentialId`. Credential `Pending` chỉ khả dụng cho đúng Global Admin trong cửa sổ xác minh và chỉ được kích hoạt sau code exchange có scope `video.publish`.

## 5. AI Gateway

### 5.1 Chuỗi kiểm tra chung

```text
HTTP request
  -> JWT/session/device/license
  -> organization membership + role
  -> project ownership
  -> idempotency
  -> model/policy + Active credential
  -> rate snapshot + budget reservation (Serializable)
  -> provider outbound HTTPS
  -> request snapshot/provider id
  -> poll/cache
  -> settlement hoặc release
```

Mọi nhánh lỗi trước outbound phải không tạo chi phí. Lỗi sau reservation phải đi qua trạng thái/recovery có thể kiểm toán.

### 5.2 Provider adapter

- OpenAI dùng cho content có schema, image, Canonical Voice TTS và transcription ở workflow được phép.
- Kling là provider/model video mặc định của catalog và luồng video dài khi policy tổ chức chọn Kling; `DirectShortVideo` mới dùng Fal/Veo theo snapshot `LongForm`, không nhận default Kling làm fallback.
- BytePlus Seedance có adapter riêng, catalog mặc định tắt.
- Fal/Veo có adapter riêng, catalog mặc định tắt và nhận first frame hợp lệ cho `OpenAiStructuredPlan` lẫn `DirectShortVideo`; cả hai lấy policy scope `LongForm` nhưng giữ workflow riêng.

Provider/model được chọn bởi project snapshot và organization policy trên server, không bởi desktop. Không có failover provider ngầm.

Outbound provider chỉ cho phép HTTPS đến:

- `api.openai.com:443`
- `api-singapore.klingai.com:443`
- `ark.ap-southeast.bytepluses.com:443`
- `queue.fal.run:443`
- `api.fal.ai:443` chỉ cho credential test theo resolver hiện hành

Output URL dùng allowlist riêng theo provider và vẫn qua DNS/redirect/MIME/size validation.

### 5.3 Output cache/proxy

Server nhận URL output gốc, xác minh provider/host/scheme/DNS, giới hạn redirect và tải về cache. Desktop chỉ nhận đường proxy tương đối có authorization. Cache có retention, giới hạn file và tổng dung lượng; cleanup không thay thế ledger hoặc provider request history.

## 6. Desktop

### 6.1 Composition

WinForms là process host. WebView2 tải React production bundle và trao đổi message với C# bridge. C# giữ gateway client, workflow service, workspace/media service, download/verification và command điều phối.

Trước login và trước API WebView2 đầu tiên, `DesktopWebViewRuntime` kiểm PE/x64 của `runtimes/win-x64/native/WebView2Loader.dll`, rồi chỉ định thư mục tuyệt đối bằng `SetLoaderDllFolderPath`. Gate khởi động và hai lệnh `--check-desktop`/`--check-webview2` dùng chung bước này; không tìm loader qua PATH hoặc thư mục làm việc. Thiếu/hỏng loader được phân biệt với thiếu Evergreen Runtime. MSBuild giữ loader ngoài EXE single-file, còn script kiểm publish có probe với PATH chỉ gồm Windows. Chi tiết và giới hạn nghiệm thu: [bản sửa WebView2](PLAN_TASK_SUA_LOI_WEBVIEW2_PUBLISH.md).

Sau đăng nhập/license/chọn organization, `Program.cs` tạo `SystemSetupCoordinator` cho FFmpeg/OCR/Qwen/Piper; `SystemSetupAuthorizer.CanManage` yêu cầu gate cho Owner/OrganizationAdmin/BillingManager/Member, không yêu cầu Viewer. `Form1` tải dashboard trước, sau đó `StartupSystemSetupModal` phủ React; nền dùng `inert`/`aria-hidden`. `StartupSystemSetupGate` ở host từ chối command nghiệp vụ đến khi mọi component không `DISABLED` đều `READY`. Modal và host phải cùng context/operation ID; không dùng trạng thái UI làm quyền bypass runtime.

Bridge là contract hai phía: TypeScript message, C# DTO/handler, validation và busy/error state phải thay đổi đồng bộ. Không tin path, organization id, project id hoặc trạng thái gửi từ JavaScript nếu chưa xác minh ở C#/server.

### 6.2 Workspace và media

Workspace mặc định dưới `%LOCALAPPDATA%\ToolGenPostVideo\workspace`. Mọi path lưu trong database/manifest nên là relative path đã chuẩn hóa và phải resolve bên trong root.

Download đi qua `.part`, sau đó kiểm tra HTTP metadata, file signature, size/hash và FFprobe trước rename atomically. Render dùng FFmpeg/FFprobe từ bundle đã được phân phối và chỉ nhận generation đã Approved/current.

### 6.3 Workflow generation

- **Content:** desktop gửi request contract đến server, theo dõi trạng thái và đồng bộ kết quả có cấu trúc vào project.
- **Character/image:** server kiểm tra ownership/policy/budget, gọi OpenAI và trả asset qua kênh kiểm soát.
- **Video:** server submit/poll/cache; desktop download, verify, preview và approve/reject.
- **Fal first frame:** scene cần first frame Approved/current trước submit video.
- **Canonical Voice:** server sở hữu catalog/profile/version, quote và TTS; desktop tải/kiểm WAV, giữ speech/voice snapshot và mix vào video nền.
- **Provider Native:** video dài kiểm tra audio kỹ thuật rồi nghe/duyệt trực tiếp, không gọi ASR; speech verification là workflow độc lập ngoài video dài khi được bật.
- **Render:** desktop tái xác minh approved generation và media trước ghép.

Asset đã ghép audio mới dùng policy `scene-audio-sync-v3`, áp dụng cho cả lời dẫn và thoại nhân vật Canonical Voice. Thoại nhân vật dùng WAV đã duyệt để tạo video nền rồi ghép bằng FFmpeg; hình và chuyển động miệng giữ theo clip provider. Tương thích `v2` chỉ áp dụng cho exact approved pointer còn khớp generation, VoiceGeneration, speech/voice snapshot và hash. Project cũ có trạng thái chờ được đối chiếu WAV/voice version hiện hành; dashboard chỉ chiếu trạng thái tiếp tục, không tự ghi approval vào database.

### 6.4 Tải Bilibili trên desktop

`App.tsx` → `useBilibiliModule` → `BilibiliWebBridge` → `BilibiliService` → `BilibiliDownloader`/process yt-dlp ghim checksum → kiểm MP4/FFprobe → thư mục local. Form1 dispatch bridge riêng, license invalidation hủy process; không có API server, SQL hoặc chi phí AI mới. State dùng request ID/revision, ID video đã quét và ID job; path/command/URL CDN không nhận từ DOM. Quét streaming có phân trang/collection và giữ partial khi lỗi; hàng đợi là state của phiên theo user. [Chi tiết và bằng chứng](TRIEN_KHAI_TAI_VIDEO_BILIBILI.md).

## 7. Vietsub

Cloud translation có đường riêng: `vietsub.job.translate.cloud` → desktop snapshot/`TRANSLATE_CLOUD` → API `cloud-translation` → worker server → OpenAI Responses. Server giữ model/credential/rate/prompt snapshot; SQL application lock và lease bảo vệ dispatch. Mỗi batch reserve vào `VietsubProjectId`, giữ FK project video cũ bằng CHECK đúng một loại project. Result + trạng thái được lưu trước settlement; Unknown không tự retry/release. Desktop lấy lại cùng operation ID, CAS từng cue với receipt/checkpoint trong SQLite rồi ghi SRT atomically; provenance `CLOUD_AUTO` được xử lý như bản dịch tự động khi nguồn bị sửa. Qwen worker không tham chiếu Cloud. Xem [runbook Cloud](HUONG_DAN_VAN_HANH_DICH_CLOUD_VIETSUB.md).

### 7.1 Lưu trữ

Mỗi project Vietsub có manifest JSON schema 8; nâng từ 7 bổ sung `videoTransformSettings.subtitleMask` mặc định tắt và giữ lựa chọn lật hình. SQLite `project.db` vẫn schema 6 độc lập, media và artifact local. `subtitleStyle`, `audioMixSettings` và `videoTransformSettings` nằm trong manifest; style được validate theo font/color/range allowlist, mixer giới hạn âm gốc `0–1` và giọng dịch `0–1.5`, với migration mặc định `0.25/1.0` và auto-duck bật. React ghi đồng thời style/mixer/lật hình/vùng che qua message `vietsub.subtitle.style.update`; vùng che có mode `SOLID`/`BLUR`, màu `#RRGGBB`, tọa độ chuẩn hóa theo hình nguồn sau autorotation và trước lật. Host kiểm tra số hữu hạn, giới hạn kích thước/vị trí/mức mờ trước lưu. Preview lật `<video>` và ánh xạ vùng che theo cùng phép lật, giữ lớp chữ Việt phía trên đúng chiều; content box tính letterbox/cover và zoom khi kéo vùng che. Preview phát audio engine giọng ẩn cùng playhead và áp gain/mute/duck theo bản nháp. Bộ dựng ASS chỉ nhận model style đã normalize, ánh xạ cùng anchor/tọa độ, cân dòng theo mục tiêu và trung hòa override tag trong cue; không nhận font path hoặc tham số FFmpeg từ DOM. `vietsub.video.export` chỉ nhận đích từ `SaveFileDialog` native; service xác thực user/license/organization/project, hash video, revision track, style, mixer, lật hình/vùng che và SHA-256 timeline giọng trước khi render. FFmpeg che trước lật (`drawbox` hoặc `split/crop/gblur/overlay`), tiếp đến `hflip`/`vflip`, cuối cùng `subtitles`; phần audio trộn stem gốc/giọng, dùng sidechain khi auto-duck bật và limiter trước AAC. MP4 ghi `.partial`, probe đúng trạng thái có/không audio rồi mới publish; thay thiết kế hoặc vùng che trong lúc render khiến snapshot fail closed. Registry server dùng metadata để tìm/chọn project; riêng job Cloud giữ snapshot text tạm mã hóa. Không được cấp SQL trực tiếp từ desktop vào schema `vs`. Xem [phạm vi và kiểm chứng che phụ đề gốc](CHE_PHU_DE_GOC.md).

Vùng che mới mặc định `BLUR`; chế độ `SOLID` hỗ trợ alpha `opacity` 0–1, UI hiển thị độ trong suốt đảo chiều 0–100%. Preview chỉ áp opacity cho lớp màu, FFmpeg dùng `drawbox` với alpha tương ứng, không áp alpha lên chữ Việt. Dữ liệu SOLID cũ thiếu alpha mặc định 1; dữ liệu BLUR thiếu alpha nhận 0,35 làm giá trị dự phòng khi chuyển sang lớp màu. Schema manifest vẫn 8 vì trường mới là tùy chọn; không tự đổi chế độ đã lưu của dự án.

Editor phụ đề điều khiển cuộn tại `useCueListFollow`: chỉ cuộn container danh sách, giữ một đích mới nhất, tạm giữ card khi người dùng cuộn/nhập/menu và tiếp tục sau hai giây rảnh khi đang phát. Row được memo hóa, key theo track + cue và giữ draft qua revision; project được remount theo project ID. Phản hồi trang chỉ được áp dụng khi khớp request mới nhất. Banner dùng `VietsubNotice`; `noticeEvents` là metadata riêng của state frontend để phân biệt một lần lỗi mới với poll cùng sự kiện, không đổi DTO native/server hoặc trạng thái nghiệp vụ. Thao tác giọng trong editor chỉ gửi một cue ID, flush draft trước và lấy revision mới nhất từ page/track summary.

### 7.2 OCR

Paddle OCR chạy local và tạo track/cue có source, language và revision. Translation CTA chỉ tạo job khi active track là `PADDLE_OCR_LOCAL`, ngôn ngữ hỗ trợ và có cue hợp lệ.

### 7.3 Translation worker

Worker x64 riêng tải LLamaSharp/native runtime và Qwen đã pin. Giao tiếp là IPC local với protocol giới hạn; worker không tham chiếu Cloud provider client, secret hoặc workflow database.

Cập nhật source 2026-09-17: worker protocol 3 / version 1.2.0 hỗ trợ CPU hoặc CUDA NVIDIA tùy chọn. Job strategy 4 snapshot `AUTO`/`CPU_ONLY`, tách khỏi fingerprint nội dung; chuyển backend không làm mất cue đã commit. CUDA chọn UUID/VRAM, offload 12/24/28/30/32/36 layer và probe Anh/Trung riêng. Các mức mới 28/30/32 chỉ được chọn khi mọi nhóm đã lập kế hoạch trong job có tối đa 6 target cue; nhóm lớn giữ các mức cũ 12/24/36. Chính sách `qwen4b-cuda12-v3` giảm tải khi OOM từ mức trên 24 về 24, rồi 12, rồi CPU; tối đa ba lần GPU thất bại, kiểm lại VRAM và lưu checkpoint trước retry. Resume không nâng mức đã chọn; job đã fallback CPU tiếp tục CPU. Gói CUDA sai checksum bị từ chối sử dụng; sai model/protocol vẫn dừng. CPU READY vẫn đủ cho Setup khi chưa cài GPU. Engine tái sử dụng một `StatelessExecutor` trong phiên model; mỗi request vẫn có context/sampling độc lập. Cache prefix chỉ có trong benchmark và chưa bật cho job bình thường. Xem [nâng cấp, kiểm thử và benchmark hiện hành](NANG_CAP_TOC_DO_DICH_LOCAL.md); [biên bản CPU/GPU ban đầu](TRIEN_KHAI_TANG_TOC_DICH_CPU_GPU.md) là bằng chứng lịch sử.

Runtime có profile Standard (8 GB tổng/4 GB RAM trống/6 GB commit) và Low-memory (6 GB tổng/3 GB RAM trống/4 GB commit). Low-memory giảm batch, thread và kích thước scene nhưng giữ context window 4096; worker xác minh lại profile/ngưỡng theo allowlist. Các ngưỡng này là mức khuyến nghị: native trả resource warning, yêu cầu xác nhận rõ ràng rồi snapshot admission vào job/load request trước khi cả desktop client và worker cho phép nạp model. Platform, disk, checksum/probe và lỗi nạp/OOM thực tế không thể được xác nhận để bỏ qua. Các profile vẫn phải qua benchmark phần cứng đích trước rollout.

Readiness marker gắn fingerprint của model, worker, protocol, resource profile, config, backend và native runtime. Probe cần xác minh runtime cùng input English/Chinese; chỉ có file model không đủ để báo Ready. Thiếu RAM không làm mất READY của model đã probe nhưng runtime status phải trả cảnh báo riêng. Profile và resource admission được snapshot vào job; cache vẫn tách theo profile, còn apply dùng revision/lock check và ghi SRT atomically.

### 7.4 Tạo giọng local

Desktop điều phối Piper CPU qua Python worker JSONL cô lập. Component store chỉ tải từ HTTPS host allowlist, kiểm tra đúng size/SHA-256 của `uv`, model và config trước khi ghi marker `READY`; không dùng ngưỡng RAM cứng để chặn cài hoặc chạy. Kokoro dùng Python 3.11.15 x64 và dependency hash-locked trong runtime riêng, ONNX CPU cùng voicepack được chọn; marker theo từng voice chỉ READY sau probe WAV 24 kHz. `VietsubVoiceService` lưu lựa chọn trong manifest, job snapshot lựa chọn đó, executor chọn đúng synthesizer và `VietsubVoiceStore` chỉ trả timeline cùng engine/model/version/voice để preview và export.

Job snapshot track/revision/cấu hình, gom cue thành phrase ổn định, tái dùng cache theo fingerprint và checkpoint sau từng phrase. Mỗi WAV phải là RIFF/PCM hợp lệ và được ghi atomically. Bộ dựng timeline trim silence, mượn khoảng trống kế tiếp và chỉ dùng `atempo` đến `1.20x`; vượt ngưỡng vẫn publish timeline, giữ timing diagnostic không chặn và nới đuôi timeline để không cắt câu cuối. React hiển thị output này bằng track **Giọng Việt** trên cùng time scale với video/phụ đề và dùng audio engine ẩn đồng bộ với master video, không dùng native audio controls độc lập. Thao tác chỉ nới `end` sẽ tạo bản ghi timeline revision mới trỏ tới đúng WAV/SHA-256 đã xác minh. Với các chỉnh sửa timing khác hoặc project đã lệch nhiều revision, service xác minh lại fingerprint/cue coverage/SHA-256 của toàn bộ phrase cache rồi dùng FFmpeg dựng timeline theo timing hiện hành mà không gọi Piper; cache không tương thích vẫn fail closed.

Timeline được trộn bằng FFmpeg theo từng stem giới hạn số phrase. WebView chỉ phát qua virtual host nội bộ với registry của project/track/revision hiện hành và xác minh lại kích thước/SHA-256; UI không nhận đường dẫn filesystem. Job dịch và job giọng dùng cùng local job manager nên không chạy đồng thời trên máy.

Mốc đầu câu bỏ qua được dùng làm mục tiêu fit, kể cả khi cue chồng thời gian; trường nội bộ `HardEndMilliseconds` không còn là giới hạn cắt âm. WAV inspector giữ riêng mốc cuối vượt ngưỡng `max(400, peak * 0.02)` của PCM16 trước khi thêm đệm. Renderer chỉ rút phần đuôi sau mốc này cộng 5 ms, tối đa 120 ms tính trên WAV trước `atempo`; fade tối đa 5 ms cũng chỉ nằm trong phần đuôi đó. Metadata trim chỉ dùng cho lần render, phrase/cache hash giữ nguyên. Nếu vẫn tràn, renderer trả diagnostic `REVIEW_REQUIRED` nhưng tiếp tục dựng timeline, không throw `VOICE_SKIPPED_CUE_OVERLAP` hoặc dùng `atrim` cắt ở đầu câu bỏ qua. Tạo mới và phục hồi cache dùng chung renderer này. Bước `VOICE_TIMELINE` vẫn ghi checkpoint 72% để lỗi render thật giữ đúng tiến độ; thành công publish ở 100%. Sau `adelay`, `asetpts=N/SR/TB` dựng lại timestamp từ mẫu âm thanh để không mất đoạn im lặng đầu do frame thiếu PTS; limiter bù latency để giữ đúng timing.

Sau trộn stem, timestamp cũng được dựng lại; `apad=whole_len` và `atrim=end_sample` giới hạn đầu ra theo số mẫu 48 kHz. Cách này tránh việc padding tiếp tục ghi WAV khi tổ hợp filter truyền timestamp thiếu hoặc không hợp lệ.

## 8. Cấu hình mặc định cần biết

- Server cache output: `data/video-outputs`, retention 48 giờ, tối đa 1 GiB/file và 20 GiB tổng theo config hiện hành.
- Video polling: tối đa 3000 attempt, 72 giờ tuổi và claim lease 35 phút theo config hiện hành.
- SePay: tắt mặc định.
- Desktop server URL: `https://localhost:7202/`.
- Desktop updater: bật, channel `Stable`, platform `win-x64`.
- Canonical Voice/speech verification: source server bật hai flag; Speech Synchronization: source desktop tắt, có thể được ghi đè theo máy. Readiness vẫn kiểm credential/rate/budget và voice version.
- Vietsub/OCR: bật; translation local: tắt; local voice UI/cài đặt: bật nhưng runtime thiếu component trả `NOT_INSTALLED`.
- TikTok: item desktop hiển thị mặc định với `Features:TikTokEnabled=true`; server bật khả năng quản trị bằng `TikTok:AdminManagedCredentialsEnabled=true`, giữ legacy `TikTok:Enabled=false`, `TikTok:EmergencyDisabled=false`, `TikTok:AuditedForPublicPosting=false` và không chứa Client Key/Secret trong cấu hình mặc định.
- Server có `TikTok:MultiAccountEnabled=true` trong workspace; schema 4.1.8 và trạng thái runtime/môi trường vẫn phải xác minh riêng.
- Video ngắn phối đồ: `Generation:ShortVideoCharacterOutfit:Enabled=false` và desktop `Features:ShortVideoCharacterOutfitEnabled=false`. Quote `TextOnly` vẫn phụ thuộc bảng `vf.ShortVideoOperations` của 4.1.9.
- Dịch Cloud: `VietsubCloudTranslation:Enabled=true`, model `gpt-5.6-luna` theo `TOOL-SERVER/appsettings.json`; API readiness còn kiểm schema, quyền, rate/credential và budget.

Giá, credential, bank account và production connection string không nằm trong tài liệu hoặc source commit; chúng phải được cấu hình theo môi trường.

## 9. Điểm nóng bảo trì

Các file orchestration/UI lớn cần thay đổi có kiểm soát và test hẹp trước full suite, đặc biệt `TOOL-LOCAL/Web/src/App.tsx`, generation service server/desktop và Vietsub WebView bridge. Khi tách nhỏ, giữ nguyên contract, transaction boundary, cancellation, idempotency và state machine; không xóa code legacy chỉ vì không thấy call site trực tiếp.

Quy tắc nghiệp vụ chi tiết nằm tại [NGHIEP_VU_HE_THONG_VIDEOMAKER.md](NGHIEP_VU_HE_THONG_VIDEOMAKER.md); trạng thái rollout nằm tại [BOI_CANH_HE_THONG_HIEN_HANH.md](BOI_CANH_HE_THONG_HIEN_HANH.md).
