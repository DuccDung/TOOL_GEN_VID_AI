# VideoMaker

> Điểm vào repository. Rà soát context theo source ngày 2026-09-15.

VideoMaker là hệ thống desktop/server hỗ trợ tạo video bằng AI và xử lý media cục bộ. OpenAI tạo nội dung có cấu trúc và giọng Canonical; Kling, BytePlus hoặc Fal/Veo tạo clip theo policy của tổ chức; desktop tải output qua server, yêu cầu người dùng duyệt và dựng video bằng FFmpeg. Module Vietsub cung cấp editor/OCR, dịch ngữ cảnh Qwen và tạo giọng Việt local theo giọng đã chọn (Piper hoặc Kokoro) khi runtime tương ứng đã được kiểm tra.

## Đọc tài liệu theo thứ tự

1. [BOI_CANH_HE_THONG_HIEN_HANH.md](BOI_CANH_HE_THONG_HIEN_HANH.md) — trạng thái source, kiểm thử, rollout và công việc còn mở.
2. [NGHIEP_VU_HE_THONG_VIDEOMAKER.md](NGHIEP_VU_HE_THONG_VIDEOMAKER.md) — nguồn sự thật nghiệp vụ.
3. [KIEN_TRUC_KY_THUAT.md](KIEN_TRUC_KY_THUAT.md) — module, dữ liệu, API, worker và đường gọi thực tế.
4. [VAN_HANH_VA_PHAT_HANH.md](VAN_HANH_VA_PHAT_HANH.md) — database, secret, pricing, credential, rollout, rollback và publish.
5. [KIEM_THU_VA_NGHIEM_THU.md](KIEM_THU_VA_NGHIEM_THU.md) — test matrix và điều kiện nghiệm thu.
6. [AGENTS.md](AGENTS.md) — quy tắc dành cho AI agent.
7. [DE_XUAT_NGHIEP_VU_DONG_NHAT_GIONG_VEO_LOCAL.md](DE_XUAT_NGHIEP_VU_DONG_NHAT_GIONG_VEO_LOCAL.md) — nghiệp vụ gốc; xem [trạng thái triển khai thử nghiệm](KE_HOACH_TRIEN_KHAI_VEO_LOCAL.md).

Khi tài liệu khác source hoặc migration, source/migration là sự thật kỹ thuật. Khi mô tả nghiệp vụ khác nhau, ưu tiên `NGHIEP_VU_HE_THONG_VIDEOMAKER.md`.

Các báo cáo build/test và triển khai ở đây gắn với checkout, thời điểm và môi trường đã ghi trong tài liệu; chúng không xác nhận trạng thái của binary/database đang chạy. Muốn đánh giá một môi trường, kiểm tra schema version, cờ, credential/rate/budget, runtime model và bundle trên chính môi trường đó. [Bối cảnh hiện hành](BOI_CANH_HE_THONG_HIEN_HANH.md) phân biệt source, test tự động, smoke thủ công và rollout production.

## Cấu trúc solution

Admin Web tại `/admin` đã có giao diện vuông, không viền trang trí: sidebar, tổng quan, tìm người dùng, tổ chức/AI, TikTok, gói sử dụng và phát hành desktop. Phạm vi source, kế hoạch, ảnh kiểm tra và kết quả theo checkout: [triển khai Admin Web](PLAN_TASK_ADMIN_WEB_VUONG_KHONG_VIEN.md). Bản giao diện trong source không tự cập nhật server đang chạy.

| Thành phần | Trách nhiệm |
|---|---|
| `TOOL-SERVER` | ASP.NET Core API/Razor Admin; auth, license, SePay, tổ chức, budget, pricing, credential, AI Gateway, polling và proxy/cache output. |
| `TOOL-LOCAL` | WinForms + WebView2/React; đăng nhập, chọn tổ chức, workflow video, workspace, FFmpeg và Vietsub. |
| `TOOL-VIETSUB-TRANSLATION-WORKER` | Worker x64 cô lập LLamaSharp/Qwen, CPU và gói NVIDIA CUDA tùy chọn; không có Cloud client hoặc database workflow. |
| `TOOL-SHARED.Contracts` | DTO public giữa server và desktop. |
| `TOOL-DISTRIBUTION` | Kiểm tra manifest, provenance và SHA-256 của bundle phân phối. |
| `TOOL-TESTS` | xUnit cho nghiệp vụ, bảo mật, migration, worker, updater và media. |
| `TOOL-UPDATER` | Cập nhật desktop có backup/rollback. |
| `TOOL-SETUP` | Bộ cài launcher/desktop. |
| `database` | Bootstrap, migration idempotent và role SQL ít quyền. |

Solution: `TOOL_GEN_POST_VIDEO.slnx`.

## Luồng sản phẩm

Nút **Thông tin gói** mở chi tiết gói của tài khoản (trạng thái, ngày bắt đầu/hết hạn và thiết bị). **Nâng cấp gói** mở bảng so sánh giá, thời hạn và quyền lợi lấy từ server; hỗ trợ tải lại khi lỗi hoặc chưa có gói mở bán. Hai popup hiện dùng để xem thông tin; nâng cấp/gia hạn được hướng dẫn liên hệ quản trị viên.

Nút **Tạo video mới** mở popup chọn **Video ngắn** hoặc **Video dài**, nhập nội dung rồi lưu và mở project mới trong tổ chức đang chọn. Tạo project chưa gọi AI; video ngắn lưu sẵn một cảnh và chỉ tạo clip khi người dùng bấm tạo video, xác nhận chi phí.

### Video dài

1. Người dùng đăng nhập, có license/device lease và chọn organization.
2. Tạo project có cấu trúc `OpenAiStructuredPlan`; project snapshot policy provider `LongForm` và speech policy.
3. OpenAI sinh content plan tiếng Việt có schema; lỗi ngôn ngữ/nhịp lời chỉ được repair sau quote và xác nhận.
4. Người dùng duyệt nhân vật, tài sản text và gán tài sản cho cảnh.
5. Fal/Veo yêu cầu `SceneFirstFrame` Approved/current đúng tỷ lệ.
6. Server reserve budget, submit clip và worker polling độc lập với desktop.
7. Desktop tải clip qua proxy, kiểm tra hash/media/audio, duyệt và render bằng FFmpeg.

`ProviderNativeVerified` dùng Native Audio, kiểm tra kỹ thuật rồi nghe/checklist/duyệt trực tiếp trong video dài, không gọi ASR. `CanonicalVoice` dùng voice profile/version đã duyệt, TTS WAV và ghép lời có lineage; `NativeVoiceOver` đi sang tạo video nền khi WAV hợp lệ, còn `OnCameraDialogue` cần duyệt WAV nhân vật trước khi tạo video nền và ghép. Cả hai đều nghe/duyệt clip đã ghép trước render; hình ảnh và chuyển động miệng giữ theo clip provider. Module lip-sync Cloud đã được loại bỏ.

Render cuối cần tối thiểu một cảnh đã duyệt, giữ đúng thứ tự scene plan và bỏ qua cảnh chưa duyệt. FinalVideo có thể xuất MP4 nhiều lần sau khi kiểm lại SHA-256 mà không render hoặc gọi provider lại.

Đồng nhất giọng Veo local (thử nghiệm, đã bật flag desktop): chọn mẫu giọng từ clip native đã duyệt cho mỗi nhân vật, chạy VAD/tách giọng/chuyển màu giọng local, nghe duyệt rồi mới render. Cần bật riêng cho project, migration 4.1.8 và runtime được cài/probe; xem [hướng dẫn bật và nghiệm thu](KE_HOACH_TRIEN_KHAI_VEO_LOCAL.md). Có source không đồng nghĩa model tiếng Việt hoặc production đã được nghiệm thu.

### Video ngắn

- Composer hai cột, hàng thumbnail Nhân vật/Trang phục, thư viện SQLite trên máy theo tài khoản/tổ chức và bản nháp phục hồi trước khi tạo project. [Hướng dẫn giao diện, lưu ảnh và kiểm chứng](TRIEN_KHAI_UI_THU_VIEN_VIDEO_NGAN.md).
- Project dùng `DirectShortVideo`, một cảnh **4/6/8 giây**, tỷ lệ **9:16 hoặc 16:9**, tạo clip bằng **Veo 3.1 qua Fal** theo policy `LongForm` của tổ chức. Không tự chuyển sang Kling khi Veo chưa sẵn sàng.
- Không gọi OpenAI để viết lại content.
- Chế độ nhập nội dung: lưu dự án → báo giá/tạo/duyệt ảnh đầu cảnh bằng OpenAI → báo giá/tạo/duyệt clip Veo → dựng/xuất MP4. Mỗi lần tạo AI cần xác nhận chi phí riêng.
- Dự án Kling cũ hiển thị nút **Chuyển dự án sang Veo** để xác nhận thời lượng/tỷ lệ mới; giữ lịch sử và ảnh mặc thử đã duyệt nếu tỷ lệ không đổi. [Triển khai và kiểm chứng Veo video ngắn](TRIEN_KHAI_VIDEO_NGAN_VEO.md).
- Desktop có thể giữ Native Audio hoặc loại bỏ audio cục bộ.
- Chế độ **Nhân vật mặc trang phục có sẵn** đã có source: nhập hai ảnh → báo giá/tạo/duyệt ảnh mặc thử → báo giá/tạo/duyệt clip → dựng/xuất MP4. Cần migration 4.1.9 và bật cờ server/desktop; mặc định tắt, chưa smoke provider thật. Xem [triển khai và nghiệm thu](TRIEN_KHAI_VIDEO_NGAN_NHAN_VAT_TRANG_PHUC.md).

### Vietsub

- Registry metadata nằm trên server; workspace, SQLite, media, cue và artifact nằm local. Khi chọn **Dịch Cloud**, server nhận snapshot text có giới hạn và gọi OpenAI; người dùng không chọn model hay nhập key.
- Editor hỗ trợ COPY/LINK, playback Range, SRT, timeline, thumbnail, waveform và PaddleOCR English/Chinese.
- Trong Thiết kế thành phẩm, tab **Che sub gốc** cho phép kéo/đổi kích thước vùng phủ màu hoặc làm mờ, lưu theo dự án và áp dụng khi xuất MP4. [Cách dùng và kiểm chứng](CHE_PHU_DE_GOC.md).
- Dịch local Qwen chạy trong worker x64 riêng và mặc định tắt tới khi model/benchmark/smoke đạt.
- Tăng tốc dịch local: tái sử dụng executor, chọn layer CUDA theo VRAM và fallback có giới hạn; xem [cấu hình, benchmark và phạm vi nghiệm thu](NANG_CAP_TOC_DO_DICH_LOCAL.md).
- Chọn giọng Vietsub lưu vào project local; job tạo audio dùng đúng engine/model/voice đã snapshot. Piper hoặc Kokoro chỉ sẵn sàng khi model và runtime của giọng đó được cài, kiểm checksum và probe WAV; `NOT_INSTALLED` không phải `READY`. Kokoro vẫn cần benchmark, nghe nghiệm thu, smoke desktop và rà soát quyền voicepack trước phát hành.

### Setup hệ thống khi mở desktop

Sau đăng nhập, license và organization hợp lệ, desktop tạo dashboard rồi mở modal Setup cho role `Owner`, `OrganizationAdmin`, `BillingManager` hoặc `Member` nếu FFmpeg hoặc các thành phần OCR/Qwen/Piper được bật đang thiếu hoặc hỏng; `Viewer` không thuộc gate này. Qwen chỉ được khởi tạo và yêu cầu trong Setup khi cả Vietsub và dịch local đều bật. Modal khóa nền; host C# đồng thời từ chối command nghiệp vụ cho tới khi mọi thành phần không `DISABLED` đều `READY`. Cờ hiển thị hoặc marker cũ không thay thế kiểm tra checksum/probe trên máy hiện hành. [Báo cáo tích hợp](BAO_CAO_SETUP_HE_THONG.md) ghi kiểm thử source; smoke trên Windows sạch và bundle phát hành là bước riêng.

Lệnh `TOOL-LOCAL.exe --check-desktop` kiểm tra các thành phần local trước đăng nhập, không kết nối SQL hoặc tải model. Tùy chọn người dùng được lưu trong LocalAppData, tách khỏi cấu hình triển khai cạnh EXE. Desktop hiện vẫn còn SQL workflow; xem [biên bản triển khai cho máy mới](TRIEN_KHAI_TOOL_LOCAL_TREN_MAY_MOI.md) để phân biệt phần đã sửa, kết quả kiểm thử và các task chưa hoàn tất.

Lệnh `TOOL-LOCAL.exe --check-webview2` kiểm riêng DLL và Evergreen Runtime, không cần cấu hình triển khai. Desktop nạp loader x64 từ `runtimes/win-x64/native` trong gói, kể cả khi publish single-file; phải sao chép đầy đủ thư mục publish. [Kế hoạch, bản sửa và kiểm chứng lỗi DLL](PLAN_TASK_SUA_LOI_WEBVIEW2_PUBLISH.md).

### Tải video Bilibili

- Mục **Tải video Bilibili** trong menu trái nhận link video/kênh/b23.tv, quét danh sách, chọn nhiều video và tải MP4 về máy với tiến độ, hủy/thử lại và chọn thư mục/chất lượng.
- Công cụ tải có phiên bản/checksum cố định, chạy local và không phát sinh chi phí AI. Hỗ trợ video công khai; lỗi hoặc giới hạn từ Bilibili phải báo danh sách chưa đầy đủ. [Sử dụng, kiến trúc và giới hạn kiểm chứng](TRIEN_KHAI_TAI_VIDEO_BILIBILI.md).

### Đăng TikTok

- Đây là item độc lập, theo tài khoản người dùng và không dùng organization/project video làm ownership.
- Có thanh chọn tài khoản, quản lý thêm/kết nối lại/ngắt từng tài khoản và lịch sử riêng. Nhiều tài khoản cần migration 4.1.8 và `TikTok:MultiAccountEnabled=true` trên server; cấu hình workspace đã bật cờ này theo yêu cầu người dùng. Xem [hướng dẫn triển khai nhiều tài khoản](TRIEN_KHAI_TIKTOK_NHIEU_TAI_KHOAN.md).
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
- Vietsub giữ dữ liệu biên tập và media/path local; snapshot text/kết quả Cloud tạm được mã hóa, có thời hạn lưu riêng trên server.
- TikTok token/session nằm ở server; video/path local nằm ở desktop và signed upload URL không được đưa vào React hoặc log.

## Mặc định quan trọng

- OpenAI Text/Image/Voice và Kling có catalog trong source; Kling 3.0 Native Audio 720p là **model video mặc định của catalog**, còn project video ngắn mới chọn Fal/Veo theo policy `LongForm` khi môi trường đã cấu hình/rollout.
- BytePlus Seedance và Fal/Veo được seed `Disabled`.
- Source server có `CanonicalVoiceEnabled=true` và `SpeechVerificationEnabled=true`; desktop giữ `SpeechSynchronizationEnabled=false`. Khả dụng thực tế còn phụ thuộc cấu hình máy, credential, rate, budget và readiness.
- SePay mặc định `Enabled=false`.
- Vietsub và OCR bật; dịch Qwen tắt; UI/cài đặt giọng Piper bật nhưng runtime/model không được coi là sẵn sàng khi chưa qua gate.
- Cấu hình workspace kế thừa `local-2` đang bật Dịch Cloud (`VietsubCloudTranslation:Enabled=true`, model `gpt-5.6-luna`); khả dụng vẫn phụ thuộc schema, quyền, credential, rate và budget. Xem [hướng dẫn vận hành Cloud](HUONG_DAN_VAN_HANH_DICH_CLOUD_VIETSUB.md) trước khi triển khai môi trường khác.
- Item TikTok hiển thị mặc định ở desktop; quản lý credential trong Admin được hỗ trợ nhưng integration runtime vẫn tắt cho tới khi credential được OAuth xác minh. `TikTok:EmergencyDisabled` là kill switch theo môi trường.
- Migration nghiệp vụ hiện có đến `4.1.9-short-video-outfit`. Bảng `vf.ShortVideoOperations` của 4.1.9 được dùng cả cho báo giá `TextOnly`; migration trong source không chứng minh đã áp trên database đích. Các migration lip-sync Cloud cũ còn trong repository để giữ lịch sử, module runtime đã loại bỏ.

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

Build `TOOL-LOCAL` tự chạy production build của web. Test model Qwen/Piper/Kokoro thật và benchmark là opt-in; xem `KIEM_THU_VA_NGHIEM_THU.md`.

## Chạy development

Không lưu secret trong source. Cấu hình connection string và JWT signing key bằng secret store:

```powershell
dotnet user-secrets set --project TOOL-SERVER "ConnectionStrings:VideoFactory" "<connection-string>"
dotnet user-secrets set --project TOOL-SERVER "Jwt:SigningKey" "<secret-at-least-32-bytes>"
dotnet run --project TOOL-SERVER --launch-profile https
```

Desktop mặc định kết nối `https://localhost:7202/`. Có thể dùng `TOOL-LOCAL\appsettings.user.json` để ghi đè cấu hình máy không chứa secret. Không khởi động server trước khi database đúng version vì startup bootstrap có thể ghi catalog.

Đồng nhất giọng Veo local đã bật trong cấu hình desktop; model vẫn phải cài và probe trước khi dùng. Xem [hướng dẫn chạy trên máy đích](HUONG_DAN_CHAY_DONG_NHAT_GIONG_VEO_LOCAL.md) để kiểm tra READY và chọn mẫu giọng từ cảnh Native Audio đã duyệt. Trên máy đã cấu hình, nhấp đúp [Mo-VideoMaker.cmd](Mo-VideoMaker.cmd) để mở Account Server nền và desktop Release. Launcher kiểm schema trước khi khởi động và dùng URL của desktop; máy triển khai hiện dùng HTTPS 7242 với database Development đã xác minh.

## Phát hành

Không publish chỉ từ một build xanh. Với binary hiện hành, phải rehearsal chuỗi migration đến 4.1.9 trên clone và xác minh schema/quyền ở môi trường đích trước khi khởi động server; cấu hình rate/credential/budget, xác minh TikTok Developer App qua Global Admin nếu bật, smoke môi trường, kiểm tra FFmpeg `Approval scope: Release`, package integrity và rollback theo [VAN_HANH_VA_PHAT_HANH.md](VAN_HANH_VA_PHAT_HANH.md). Tách riêng Passed/Failed/Skipped; các bài model/SQL opt-in bị Skipped không phải bằng chứng nghiệm thu.
