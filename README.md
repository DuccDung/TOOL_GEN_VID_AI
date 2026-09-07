# VideoMaker

> Điểm vào duy nhất cho repository. Cập nhật ngữ cảnh: 2026-09-06.

VideoMaker là hệ thống desktop/server hỗ trợ tạo video bằng AI và xử lý media cục bộ. OpenAI tạo nội dung có cấu trúc; Kling, BytePlus hoặc Fal/Veo tạo clip theo policy của tổ chức; desktop tải output qua server, yêu cầu người dùng duyệt và dựng video cuối bằng FFmpeg.

## Đọc tài liệu theo thứ tự

1. [BOI_CANH_HE_THONG_HIEN_HANH.md](BOI_CANH_HE_THONG_HIEN_HANH.md) — trạng thái source, kiểm thử, rollout và công việc còn mở.
2. [NGHIEP_VU_HE_THONG_VIDEOMAKER.md](NGHIEP_VU_HE_THONG_VIDEOMAKER.md) — nguồn sự thật nghiệp vụ.
3. [KIEN_TRUC_KY_THUAT.md](KIEN_TRUC_KY_THUAT.md) — module, dữ liệu, API, worker và đường gọi thực tế.
4. [VAN_HANH_VA_PHAT_HANH.md](VAN_HANH_VA_PHAT_HANH.md) — database, secret, pricing, credential, rollout, rollback và publish.
5. [KIEM_THU_VA_NGHIEM_THU.md](KIEM_THU_VA_NGHIEM_THU.md) — test matrix và điều kiện nghiệm thu.
6. [AGENTS.md](AGENTS.md) — quy tắc dành cho AI agent.

Khi tài liệu khác source hoặc migration, source/migration là sự thật kỹ thuật. Khi mô tả nghiệp vụ khác nhau, ưu tiên `NGHIEP_VU_HE_THONG_VIDEOMAKER.md`.

## Kiến trúc ngắn gọn

| Thành phần | Trách nhiệm |
|---|---|
| `TOOL-SERVER` | ASP.NET Core API/Razor Admin; auth, license, SePay, tổ chức, budget, pricing, credential, AI Gateway, polling và proxy/cache output. |
| `TOOL-LOCAL` | WinForms + WebView2/React; đăng nhập, chọn tổ chức, workflow video, workspace, FFmpeg và module Vietsub. |
| `TOOL-VIETSUB-TRANSLATION-WORKER` | Worker x64 CPU cô lập LLamaSharp/Qwen; IPC local giới hạn, không có Cloud client hoặc database workflow. |
| `TOOL-SHARED.Contracts` | DTO public giữa server và desktop. |
| `TOOL-DISTRIBUTION` | Kiểm tra hồ sơ và SHA-256 của bundle phân phối. |
| `TOOL-TESTS` | xUnit cho nghiệp vụ, bảo mật, migration, worker, updater và media. |
| `TOOL-UPDATER` | Cập nhật desktop có backup/rollback. |
| `TOOL-SETUP` | Bộ cài launcher/desktop. |

## Nguyên tắc hệ thống

- Provider key chỉ tồn tại trên server, được test rồi mã hóa. Desktop không có BYOK và không tự chọn provider/model.
- Mọi request AI phải qua JWT/session/device/license, organization membership, role, project ownership, pricing, budget và idempotency.
- Budget `0` khóa AI; thiếu rate dừng trước outbound.
- Project snapshot video policy; không tự đổi provider khi admin đổi policy sau đó.
- Output provider không lộ trực tiếp. Server cache/proxy có authorization và kiểm tra SSRF; desktop xác minh file cục bộ.
- Native Audio là workflow mặc định. TTS không fallback ngầm.
- Vietsub giữ subtitle/media/path ở local; server chỉ có registry metadata.

## Trạng thái mặc định

- OpenAI Text/Image/Voice và Kling có catalog trong source; Kling 3.0 Native Audio 720p là video mặc định.
- BytePlus Seedance và Fal/Veo được seed `Disabled`.
- Fal/Veo chỉ dùng cho policy `LongForm` và cần `SceneFirstFrame` Approved/current.
- SePay mặc định `Enabled=false`.
- Vietsub và OCR mặc định bật; dịch local Qwen và tạo giọng local Piper mặc định tắt. Source có profile dịch Standard/Low-memory; ngưỡng RAM/commit là mức khuyến nghị và người dùng có thể xác nhận để vẫn thử dịch, còn platform/disk/model/probe/lỗi worker thực tế vẫn chặn. Pipeline Piper CPU có một giọng Việt đã pin model; cả hai tính năng chỉ được rollout sau verify model, benchmark phù hợp và smoke desktop trên bundle đích.
- Migration đến 4.1.1 có trong source nhưng không được mặc định xem là đã chạy trên database thật.

## Yêu cầu phát triển

- Windows x64.
- .NET SDK hỗ trợ `net10.0` và `net10.0-windows`.
- Node/npm cho React/Vite của desktop.
- SQL Server cho server và workflow video.
- FFmpeg/FFprobe đã kiểm tra hồ sơ và checksum cho xử lý media.

## Build và test

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Web desktop:

```powershell
Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
npm test -- --run
```

Build `TOOL-LOCAL` tự chạy production build của web. Test model Qwen thật và benchmark là opt-in; xem `KIEM_THU_VA_NGHIEM_THU.md` trước khi chạy.

## Chạy development

Không lưu secret trong source. Cấu hình connection string và JWT signing key bằng secret store:

```powershell
dotnet user-secrets set --project TOOL-SERVER "ConnectionStrings:VideoFactory" "<connection-string>"
dotnet user-secrets set --project TOOL-SERVER "Jwt:SigningKey" "<secret-at-least-32-bytes>"
dotnet run --project TOOL-SERVER --launch-profile https
```

Desktop mặc định kết nối `https://localhost:7202/`. Có thể dùng `TOOL-LOCAL\appsettings.user.json` để ghi đè đường dẫn FFmpeg cho máy phát triển; không commit file chứa cấu hình máy hoặc secret.

Không khởi động server trước khi database đúng phiên bản: quá trình startup bootstrap catalog và có thể ghi dữ liệu catalog vào database.

## Dữ liệu và output

- Server dùng các schema `auth`, `ai`, `vf`, `vs`.
- Desktop còn dùng SQL trực tiếp cho workflow `vf` trong giai đoạn chuyển tiếp và phải dùng database role ít quyền.
- Workspace video mặc định nằm dưới `%LOCALAPPDATA%\ToolGenPostVideo\workspace`.
- Vietsub dùng `project.json`, `project.db`, media và artifact local trong workspace riêng.
- Cache output video server mặc định ở `data/video-outputs` với retention có giới hạn.

## Phát hành

Không publish trực tiếp chỉ từ một build xanh. Phải hoàn tất migration rehearsal, cấu hình rate/credential/budget, smoke test môi trường, kiểm tra bundle FFmpeg `Approval scope: Release`, package integrity, rollback và checklist tại [VAN_HANH_VA_PHAT_HANH.md](VAN_HANH_VA_PHAT_HANH.md).
