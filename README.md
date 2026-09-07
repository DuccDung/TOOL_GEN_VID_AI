# VideoMaker

VideoMaker là ứng dụng desktop Windows hỗ trợ tạo video ngắn, video nhiều cảnh và chỉnh phụ đề. AI được quản trị tập trung theo tổ chức: desktop không nhận API key và không gọi trực tiếp OpenAI/Kling/BytePlus/Fal.

## Đọc gì trước

- `NGHIEP_VU_HE_THONG_VIDEOMAKER.md`: kiến trúc và bất biến nghiệp vụ.
- `TRANG_THAI_DU_AN.md`: tính năng đã có, feature flag và backlog hiện hành.
- `TRIEN_KHAI_AI_GATEWAY_TO_CHUC.md`: migration, cấu hình, staging và release.
- `AGENTS.md`: quy tắc bắt buộc cho AI/cộng tác viên sửa repository.

Các file task/kế hoạch lịch sử đã được loại bỏ. Không tạo lại tài liệu theo từng lỗi nếu thông tin có thể cập nhật trực tiếp vào một trong bốn nguồn trên.

## Cấu trúc solution

| Project | Trách nhiệm |
|---|---|
| `TOOL-SERVER` | ASP.NET Core API/Razor Admin; auth, license, organization, pricing, credential, AI gateway, worker, payments, output proxy và release storage |
| `TOOL-LOCAL` | WinForms + WebView2/React; đăng nhập, chọn organization, project/workspace, gọi gateway và xử lý media cục bộ |
| `TOOL-SHARED.Contracts` | DTO request/response dùng chung giữa server và desktop |
| `TOOL-TESTS` | xUnit cho nghiệp vụ, bảo mật, migration, media, updater và Vietsub |
| `TOOL-DISTRIBUTION` | Kiểm tra tính toàn vẹn bundle FFmpeg trong package |
| `TOOL-UPDATER` | Cập nhật desktop có backup/rollback |
| `TOOL-SETUP` | Launcher/installer tải release từ server |
| `database` | Schema ban đầu, migration idempotent và quyền SQL tối thiểu |
| `scripts` | Chuẩn bị FFmpeg, publish và smoke-test có kiểm soát |

Solution: `TOOL_GEN_POST_VIDEO.slnx`.

## Luồng sản phẩm

### Video dài

1. Người dùng đăng nhập, có license và chọn organization.
2. Tạo project `OpenAiStructuredPlan` bằng tiếng Việt.
3. OpenAI sinh content plan có schema; server kiểm tra ngôn ngữ và cho phép một lượt repair có quote nếu cần.
4. Người dùng duyệt nhân vật, tài sản text và gán tài sản cho cảnh.
5. Với Fal/Veo, mỗi cảnh phải có first-frame đúng tỷ lệ và được duyệt.
6. Server reserve budget, submit clip tới provider và worker tiếp tục polling độc lập với desktop.
7. Desktop tải clip qua server, kiểm tra hash/media/audio, duyệt và render cuối bằng FFmpeg.

Người dùng có thể dựng video từ bất kỳ số lượng cảnh đã duyệt nào, tối thiểu một cảnh. Bản dựng lấy các cảnh đã duyệt của scene plan hiện hành theo đúng thứ tự và bỏ qua những cảnh chưa duyệt; không bắt buộc hoàn tất toàn bộ scene plan.

Khi FinalVideo đã dựng xong, nút `Xuất video MP4` mở hộp thoại lưu file của Windows. Desktop kiểm tra lại SHA-256 của bản dựng, sao chép nguyên tử ra vị trí người dùng chọn rồi ghi nhận trạng thái `Exported`; thao tác này không render lại và không gọi AI/provider.

Với `CanonicalVoice`, readiness dựa trên TTS có model/credential/rate/budget hợp lệ. Voice profile preview phải được nghe trước khi khóa phiên bản giọng. WAV `NativeVoiceOver` của cảnh được kiểm tra kỹ thuật; khi WAV hiện hành đã có, nút chuyển thẳng sang tạo video nền, lệnh tạo video dùng lại và tự chấp nhận đúng VoiceGeneration trước server cost gate, không có bước duyệt WAV riêng. `OnCameraDialogue` vẫn dừng ở bước duyệt/chờ lip-sync. Canonical Voice không chạy ASR/transcript verification.

Narrated asset mới tiếp tục dùng `scene-audio-sync-v3`. Khi dựng cuối, desktop tương thích có giới hạn với asset `v2` đã được người dùng duyệt chính xác và còn khớp scene/generation/voice/speech snapshot/hash; phiên bản cũ hơn hoặc asset không còn đúng lineage vẫn bị từ chối. Cơ chế này không tạo lại video hoặc TTS.

### Video ngắn

- Project dùng `DirectShortVideo`.
- Một cảnh, thời lượng 5–15 giây, provider hiện là Kling.
- Không gọi OpenAI để sinh content.
- Có thể giữ Native Audio hoặc loại bỏ hoàn toàn audio ở output cục bộ.

### Vietsub

- Project Vietsub tách khỏi `vf.Project` và có registry server trong schema `vs`.
- Workspace, media, subtitle cue, local job và artifact nằm cục bộ.
- Hiện có import COPY/LINK, playback Range, SRT, timeline, thumbnail, waveform và PaddleOCR local.
- Dịch ngữ cảnh local, STT local, voice và export MP4 Vietsub chưa phải luồng hoàn chỉnh.

## Yêu cầu phát triển

- Windows x64.
- .NET SDK 10.
- SQL Server cho server và workflow chuyển tiếp của desktop.
- Node.js phù hợp với Vite hiện hành; dùng version đáp ứng `package-lock.json`.
- FFmpeg/FFprobe đã được duyệt và đặt trong `third_party/ffmpeg/win-x64` khi build/publish yêu cầu bundle.

Không commit secret. Cấu hình máy cá nhân của desktop nằm trong `appsettings.user.json`, file này đã được ignore.

## Build và test

Từ root:

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

Build `TOOL-LOCAL` sẽ tự chạy npm build và dùng `npm ci` khi chưa có `node_modules`.

## Chạy phát triển

Server:

```powershell
dotnet run --project TOOL-SERVER\TOOL-SERVER.csproj --launch-profile https
```

Mặc định development dùng `https://localhost:7202/`. Sau khi server sẵn sàng, chạy desktop từ IDE hoặc:

```powershell
dotnet run --project TOOL-LOCAL\TOOL-LOCAL.csproj
```

Không coi cấu hình localhost là cấu hình triển khai. Xem `TRIEN_KHAI_AI_GATEWAY_TO_CHUC.md` trước khi dùng database, credential hoặc provider thật.

## Thư mục không phải source

Không sửa trực tiếp:

- `.vs`, `bin`, `obj`;
- `node_modules`, `dist`, `*.tsbuildinfo`;
- `artifacts` và `TOOL-SERVER/App_Releases`;
- cache/output runtime trong `data/video-outputs`.

## Trạng thái kiểm thử

Baseline và mức độ rollout chỉ được ghi tại `TRANG_THAI_DU_AN.md`. Hãy chạy lại các lệnh ở trên trước khi công bố mốc mới.
