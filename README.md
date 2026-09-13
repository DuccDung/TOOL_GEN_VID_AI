# VideoMaker

**Chế độ mặc định hiện hành: Vietsub local.** Tạo video AI và Dịch Cloud bị khóa; OCR, Qwen, Piper và xuất MP4 chạy trên máy, auth/license/registry vẫn dùng server. Xem [hướng dẫn cấu hình và kiểm chứng](HUONG_DAN_VIETSUB_LOCAL_ONLY.md). Các workflow video bên dưới mô tả khả năng được giữ trong source khi mở lại chế độ đầy đủ.

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

- Registry metadata nằm trên server; workspace, SQLite, media, cue và artifact nằm local. Khi chọn **Dịch Cloud**, server nhận snapshot text có giới hạn và gọi OpenAI; người dùng không chọn model hay nhập key.
- Editor hỗ trợ COPY/LINK, playback Range, SRT, timeline, thumbnail, waveform và PaddleOCR English/Chinese.
- Dịch Qwen chạy trong worker x64 riêng; chế độ Vietsub local mở workflow cài/chạy qua readiness. Trong chế độ đầy đủ, flag legacy vẫn mặc định tắt tới khi model/benchmark/smoke đạt.
- Tạo giọng local Piper hiển thị qua feature flag mặc định bật nhưng runtime/model phải được cài, kiểm checksum và probe; `NOT_INSTALLED` không phải `READY`.

## Nguyên tắc hệ thống

- Provider key chỉ tồn tại trên server, được test rồi mã hóa; desktop không có BYOK.
- Mọi request AI đi qua JWT/session/device/license, membership/role, ownership, pricing, budget và idempotency.
- Budget `0` khóa AI; thiếu rate dừng trước outbound.
- Project snapshot provider/model/policy; không tự failover hoặc đổi provider khi admin đổi policy.
- Output provider không lộ trực tiếp. Server cache/proxy có authorization và SSRF guard; desktop xác minh file local.
- TTS, ASR, Native Audio, Canonical Voice và local model không fallback ngầm cho nhau.
- Vietsub giữ dữ liệu biên tập và media/path local; snapshot text/kết quả Cloud tạm được mã hóa, có thời hạn lưu riêng trên server.

## Mặc định quan trọng

- OpenAI Text/Image/Voice và Kling có catalog trong source; Kling 3.0 Native Audio 720p là video mặc định.
- BytePlus Seedance và Fal/Veo được seed `Disabled`.
- Canonical Voice và speech verification mặc định tắt ở server/desktop.
- SePay mặc định `Enabled=false`.
- `Application:VietsubLocalOnly=true` trên desktop/server. Vietsub, OCR, workflow cài/chạy Qwen và Piper khả dụng; runtime/model phải qua gate. Flag Qwen legacy vẫn tắt khi chuyển lại chế độ đầy đủ.
- Dịch Cloud `VietsubCloudTranslation:Enabled=false` và bị khóa bởi chế độ local; model cấu hình được giữ lại để tương thích. Xem [hướng dẫn vận hành Cloud](HUONG_DAN_VAN_HANH_DICH_CLOUD_VIETSUB.md) trước khi mở lại.
- Migration đến 4.1.6 có trong source nhưng không được mặc định xem là đã chạy trên database thật.

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

Không publish chỉ từ một build xanh. Phải hoàn tất migration rehearsal đến 4.1.5, cấu hình rate/credential/budget, smoke môi trường, kiểm tra FFmpeg `Approval scope: Release`, package integrity và rollback theo [VAN_HANH_VA_PHAT_HANH.md](VAN_HANH_VA_PHAT_HANH.md).
