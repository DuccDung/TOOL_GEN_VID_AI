# VideoMaker

> Điểm vào repository. Cập nhật ngữ cảnh: 2026-09-07.

VideoMaker là hệ thống desktop/server hỗ trợ tạo video bằng AI và xử lý media cục bộ. OpenAI tạo nội dung có cấu trúc và giọng Canonical; Kling, BytePlus hoặc Fal/Veo tạo clip theo policy của tổ chức; desktop tải output qua server, yêu cầu người dùng duyệt và dựng video bằng FFmpeg. Module Vietsub cung cấp editor/OCR, dịch ngữ cảnh Qwen và tạo giọng Việt Piper theo mô hình local-first.

## Đọc tài liệu theo thứ tự

1. [BOI_CANH_HE_THONG_HIEN_HANH.md](BOI_CANH_HE_THONG_HIEN_HANH.md) — trạng thái source, kiểm thử, rollout và công việc còn mở.
2. [NGHIEP_VU_HE_THONG_VIDEOMAKER.md](NGHIEP_VU_HE_THONG_VIDEOMAKER.md) — nguồn sự thật nghiệp vụ.
3. [KIEN_TRUC_KY_THUAT.md](KIEN_TRUC_KY_THUAT.md) — module, dữ liệu, API, worker và đường gọi thực tế.
4. [VAN_HANH_VA_PHAT_HANH.md](VAN_HANH_VA_PHAT_HANH.md) — database, secret, pricing, credential, rollout, rollback và publish.
5. [KIEM_THU_VA_NGHIEM_THU.md](KIEM_THU_VA_NGHIEM_THU.md) — test matrix và điều kiện nghiệm thu.
6. [AGENTS.md](AGENTS.md) — quy tắc dành cho AI agent.

Khi tài liệu khác source hoặc migration, source/migration là sự thật kỹ thuật. Khi mô tả nghiệp vụ khác nhau, ưu tiên `NGHIEP_VU_HE_THONG_VIDEOMAKER.md`.

## Cấu trúc solution

| Thành phần | Trách nhiệm |
|---|---|
| `TOOL-SERVER` | ASP.NET Core API/Razor Admin; auth, license, SePay, tổ chức, budget, pricing, credential, AI Gateway, polling và proxy/cache output. |
| `TOOL-LOCAL` | WinForms + WebView2/React; đăng nhập, chọn tổ chức, workflow video, workspace, FFmpeg và Vietsub. |
| `TOOL-VIETSUB-TRANSLATION-WORKER` | Worker x64 CPU cô lập LLamaSharp/Qwen; không có Cloud client hoặc database workflow. |
| `TOOL-SHARED.Contracts` | DTO public giữa server và desktop. |
| `TOOL-DISTRIBUTION` | Kiểm tra manifest, provenance và SHA-256 của bundle phân phối. |
| `TOOL-TESTS` | xUnit cho nghiệp vụ, bảo mật, migration, worker, updater và media. |
| `TOOL-UPDATER` | Cập nhật desktop có backup/rollback. |
| `TOOL-SETUP` | Bộ cài launcher/desktop. |
| `database` | Bootstrap, migration idempotent và role SQL ít quyền. |

Solution: `TOOL_GEN_POST_VIDEO.slnx`.

## Luồng sản phẩm

### Video dài

1. Người dùng đăng nhập, có license/device lease và chọn organization.
2. Tạo project có cấu trúc `OpenAiStructuredPlan`; project snapshot policy provider `LongForm` và speech policy.
3. OpenAI sinh content plan tiếng Việt có schema; lỗi ngôn ngữ/nhịp lời chỉ được repair sau quote và xác nhận.
4. Người dùng duyệt nhân vật, tài sản text và gán tài sản cho cảnh.
5. Fal/Veo yêu cầu `SceneFirstFrame` Approved/current đúng tỷ lệ.
6. Server reserve budget, submit clip và worker polling độc lập với desktop.
7. Desktop tải clip qua proxy, kiểm tra hash/media/audio, duyệt và render bằng FFmpeg.

`ProviderNativeVerified` dùng Native Audio, kiểm tra kỹ thuật rồi nghe/checklist/duyệt trực tiếp trong video dài, không gọi ASR. `CanonicalVoice` dùng voice profile/version đã duyệt, TTS WAV và ghép lời có lineage; `NativeVoiceOver` đi thẳng sang tạo video nền khi WAV hợp lệ, còn `OnCameraDialogue` dừng ở trạng thái chờ lip-sync.

Render cuối cần tối thiểu một cảnh đã duyệt, giữ đúng thứ tự scene plan và bỏ qua cảnh chưa duyệt. FinalVideo có thể xuất MP4 nhiều lần sau khi kiểm lại SHA-256 mà không render hoặc gọi provider lại.

### Video ngắn

- Project dùng `DirectShortVideo`, một cảnh 5–15 giây và Kling.
- Không gọi OpenAI để viết lại content.
- Desktop có thể giữ Native Audio hoặc loại bỏ audio cục bộ.

### Vietsub

- Registry metadata nằm trên server; workspace, SQLite, media, cue và artifact nằm local.
- Editor hỗ trợ COPY/LINK, playback Range, SRT, timeline, thumbnail, waveform và PaddleOCR English/Chinese.
- Dịch local Qwen chạy trong worker x64 riêng và mặc định tắt tới khi model/benchmark/smoke đạt.
- Tạo giọng local Piper hiển thị qua feature flag mặc định bật nhưng runtime/model phải được cài, kiểm checksum và probe; `NOT_INSTALLED` không phải `READY`.

### Đăng TikTok

- Đây là item độc lập, theo tài khoản người dùng và không dùng organization/project video làm ownership.
- Người dùng chọn MP4/MOV/WebM từ máy; đường dẫn và byte video ở desktop, không được gửi qua server.
- Login Kit Desktop dùng OAuth 2.0 + PKCE qua trình duyệt hệ thống và loopback `127.0.0.1`. Server giữ `client_secret`, access/refresh token đã mã hóa và metadata job.
- Global Admin nhập Client Key/Client Secret tại mục **Tích hợp TikTok**. Server mã hóa ngay, chỉ trả hint và giữ credential ở `Pending`; credential chỉ thành `Active` sau khi đúng Admin hoàn tất một lần OAuth thật trên Desktop.
- Server khởi tạo Direct Post; desktop tải tuần tự các chunk trực tiếp lên signed upload URL TikTok rồi chỉ hỏi trạng thái qua server. Server tiếp tục polling job khi desktop đóng.
- Không hỗ trợ cookie/session trình duyệt, Selenium hoặc nhập token thủ công. Public posting chỉ rollout sau khi TikTok duyệt app/scope và audit Content Posting API; `AuditedForPublicPosting=false` giữ an toàn cho tài khoản test riêng tư.

## Nguyên tắc hệ thống

- Provider key chỉ tồn tại trên server, được test rồi mã hóa; desktop không có BYOK.
- Mọi request AI đi qua JWT/session/device/license, membership/role, ownership, pricing, budget và idempotency.
- Budget `0` khóa AI; thiếu rate dừng trước outbound.
- Project snapshot provider/model/policy; không tự failover hoặc đổi provider khi admin đổi policy.
- Output provider không lộ trực tiếp. Server cache/proxy có authorization và SSRF guard; desktop xác minh file local.
- TTS, ASR, Native Audio, Canonical Voice và local model không fallback ngầm cho nhau.
- Vietsub giữ subtitle/media/path local; server chỉ giữ registry metadata.
- TikTok token/session nằm ở server; video/path local nằm ở desktop và signed upload URL không được đưa vào React hoặc log.

## Mặc định quan trọng

- OpenAI Text/Image/Voice và Kling có catalog trong source; Kling 3.0 Native Audio 720p là video mặc định.
- BytePlus Seedance và Fal/Veo được seed `Disabled`.
- Canonical Voice và speech verification mặc định tắt ở server/desktop.
- SePay mặc định `Enabled=false`.
- Vietsub và OCR bật; dịch Qwen tắt; UI/cài đặt giọng Piper bật nhưng runtime/model không được coi là sẵn sàng khi chưa qua gate.
- Item TikTok hiển thị mặc định ở desktop; quản lý credential trong Admin được hỗ trợ nhưng integration runtime vẫn tắt cho tới khi credential được OAuth xác minh. `TikTok:EmergencyDisabled` là kill switch theo môi trường.
- Migration đến 4.1.7 có trong source nhưng không được mặc định xem là đã chạy trên database thật.

## Yêu cầu phát triển

- Windows x64, .NET SDK 10, Node/npm phù hợp lockfile và SQL Server.
- FFmpeg/FFprobe đã kiểm tra provenance/checksum cho xử lý media.
- Không commit secret. Cấu hình máy cá nhân của desktop nằm trong `appsettings.user.json` đã được ignore.

## Build và test

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Frontend:

```powershell
Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
npm test
```

Build `TOOL-LOCAL` tự chạy production build của web. Test model Qwen/Piper thật và benchmark là opt-in; xem `KIEM_THU_VA_NGHIEM_THU.md`.

## Chạy development

Không lưu secret trong source. Cấu hình connection string và JWT signing key bằng secret store:

```powershell
dotnet user-secrets set --project TOOL-SERVER "ConnectionStrings:VideoFactory" "<connection-string>"
dotnet user-secrets set --project TOOL-SERVER "Jwt:SigningKey" "<secret-at-least-32-bytes>"
dotnet run --project TOOL-SERVER --launch-profile https
```

Desktop mặc định kết nối `https://localhost:7202/`. Có thể dùng `TOOL-LOCAL\appsettings.user.json` để ghi đè cấu hình máy không chứa secret. Không khởi động server trước khi database đúng version vì startup bootstrap có thể ghi catalog.

## Phát hành

Không publish chỉ từ một build xanh. Phải hoàn tất migration rehearsal đến 4.1.7, cấu hình rate/credential/budget, xác minh TikTok Developer App qua Global Admin nếu bật, smoke môi trường, kiểm tra FFmpeg `Approval scope: Release`, package integrity và rollback theo [VAN_HANH_VA_PHAT_HANH.md](VAN_HANH_VA_PHAT_HANH.md).
