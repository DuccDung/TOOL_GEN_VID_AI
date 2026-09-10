# Hướng dẫn AI agent — VideoMaker

> Áp dụng cho toàn bộ repository. Cập nhật ngữ cảnh: 2026-09-07.

Khi làm việc trong thư mục có `AGENTS.md` riêng, phải tuân thủ đồng thời file này và file gần nhất trong cây thư mục.

## Bắt đầu phiên làm việc

1. Trả lời bằng tiếng Việt, trừ khi người dùng yêu cầu ngôn ngữ khác.
2. Đọc [README.md](README.md), [BOI_CANH_HE_THONG_HIEN_HANH.md](BOI_CANH_HE_THONG_HIEN_HANH.md), [NGHIEP_VU_HE_THONG_VIDEOMAKER.md](NGHIEP_VU_HE_THONG_VIDEOMAKER.md) và [KIEN_TRUC_KY_THUAT.md](KIEN_TRUC_KY_THUAT.md) trước thay đổi lớn.
3. Đọc [VAN_HANH_VA_PHAT_HANH.md](VAN_HANH_VA_PHAT_HANH.md) nếu công việc liên quan database, secret, credential, giá AI, thanh toán, package hoặc phát hành.
4. Đọc [KIEM_THU_VA_NGHIEM_THU.md](KIEM_THU_VA_NGHIEM_THU.md) trước khi chọn phạm vi kiểm thử hoặc kết luận tính năng đã sẵn sàng.
5. Chạy `git status --short`; giữ nguyên thay đổi không liên quan của người dùng.
6. Kiểm tra source, migration, cấu hình mặc định và đường gọi thực tế. Không phục hồi thiết kế BYOK hoặc hành vi cũ chỉ từ lịch sử Git.

## Nguồn sự thật

Thứ tự ưu tiên khi có khác biệt:

1. Source code, project file, cấu hình mặc định và migration hiện hành.
2. `NGHIEP_VU_HE_THONG_VIDEOMAKER.md` cho quy tắc nghiệp vụ.
3. `KIEN_TRUC_KY_THUAT.md` cho ranh giới module và đường gọi.
4. `BOI_CANH_HE_THONG_HIEN_HANH.md` cho trạng thái source, kiểm thử và rollout.
5. `VAN_HANH_VA_PHAT_HANH.md` và `KIEM_THU_VA_NGHIEM_THU.md` cho thao tác có kiểm soát.

Không dùng `bin`, `obj`, `.vs`, `.tmp`, `node_modules`, `dist`, `artifacts`, ảnh giao diện, file bàn giao hoặc output build cũ làm nguồn sự thật thay source. Không dùng số test, trạng thái rollout hoặc cấu hình môi trường trong commit cũ làm bằng chứng hiện hành.

## Kiến trúc không được phá vỡ

- `TOOL-SERVER` là ranh giới tin cậy cho auth, license, tổ chức, credential, pricing, budget, provider request, usage, worker và output proxy.
- `TOOL-LOCAL` là WinForms + WebView2/React, quản lý trải nghiệm desktop, workflow SQL chuyển tiếp, workspace và media cục bộ.
- `TOOL-SHARED.Contracts` chứa DTO công khai dùng chung.
- `TOOL-VIETSUB-TRANSLATION-WORKER` là tiến trình x64 cô lập LLamaSharp/Qwen; không có Cloud client, credential hoặc database workflow.
- Desktop không giữ hoặc gọi trực tiếp bằng provider API key. Task video bất đồng bộ do server polling kể cả khi desktop đóng.
- Registry `vs.Projects` do server sở hữu; subtitle, media và workspace Vietsub nằm local.
- Khi người dùng chọn Dịch Cloud, server được nhận snapshot text giới hạn cho job OpenAI, mã hóa và có retention. Không gửi media/path; credential vẫn chỉ ở server, worker Qwen vẫn cô lập.

## Bất biến bảo mật và chi phí

- API key OpenAI/Kling/BytePlus/Fal chỉ đi qua HTTPS vào server, được test rồi mã hóa bằng ASP.NET Core Data Protection.
- Không thêm UI nhập key, secret store provider, provider SDK/client hoặc fallback gọi Cloud trực tiếp trong desktop hay translation worker.
- Không trả plaintext key, encrypted payload, Authorization header, Base64 provider output hoặc URL output gốc về desktop.
- Mọi request có chi phí phải xác minh JWT, session, device, license lease, organization membership, role và project ownership trước outbound.
- `Viewer` không được phát sinh chi phí. Chỉ Global Admin quản lý catalog/rate; chỉ Owner quản lý Owner và không được làm mất Owner Active cuối cùng.
- Budget `0` khóa AI. Thiếu rate phải trả `pricing_not_configured`; không tự đoán giá.
- Reservation, settlement và release dùng rate snapshot, transaction `Serializable` và idempotency trong phạm vi organization.
- Credential rotation phải test trước khi Active. Task đang chạy giữ credential version đã snapshot qua `Active -> Retiring -> Revoked`.
- Provider runtime chỉ dùng HTTPS/443 và exact host allowlist trong source. Output proxy phải chống SSRF/DNS rebinding, pin địa chỉ, giới hạn redirect, MIME, dung lượng và retention.
- Không log secret, token, connection string, prompt/transcript nhạy cảm, Base64 hoặc signed output URL.

## Bất biến desktop, video và Vietsub

- Desktop chỉ truy cập SQL chuyển tiếp cho workflow schema `vf`; không mở rộng quyền sang `ai`, `auth`, `dbo` hoặc `vs`.
- File tải về dùng `.part`, kiểm tra signature/MIME/size/hash rồi mới promote atomically.
- Project snapshot provider/model/policy/resolution/speech policy; thay policy tổ chức không đổi project cũ.
- `OpenAiStructuredPlan` là cấu trúc project video dài; `LongForm` là scope policy provider. `DirectShortVideo` là video ngắn.
- Fal/Veo chỉ dùng `SceneFirstFrame` Approved/current đúng tỷ lệ và không fallback Text-to-Video.
- Canonical Voice, TTS và speech verification chỉ chạy sau đủ feature flag, credential, rate, budget và readiness. Không fallback ngầm từ Provider Native Audio.
- Render cuối chỉ dùng asset đã duyệt đúng generation/voice/speech snapshot và phải kiểm lại hash, stream, audio cùng thời lượng.
- Dịch Vietsub chỉ nhận active `PADDLE_OCR_LOCAL` track `en`/`zh` có cue và revision khớp; cue manual/locked không bị ghi đè và SRT ghi atomically.
- `VietsubLocalTranslationEnabled=false` là mặc định an toàn. `VietsubLocalVoiceEnabled=true` chỉ làm UI/cài đặt khả dụng; runtime thiếu component phải trả `NOT_INSTALLED`, không được coi là production-ready.

## Quy tắc thay đổi

- DTO public: sửa `TOOL-SHARED.Contracts` trước, rồi cập nhật server, desktop, frontend và test trong cùng thay đổi.
- Schema: tạo migration SQL idempotent mới; không sửa âm thầm migration có thể đã triển khai.
- Generation: kiểm tra access, idempotency, pricing, reservation, settlement/release, request snapshot, worker retry và output lifecycle.
- Credential: không log request body/header/secret; response chỉ có hint và metadata an toàn.
- WebView bridge: cập nhật TypeScript message, C# contract/handler, validation, busy state và organization/project context đồng thời.
- Không xóa entity/model legacy chỉ vì không thấy đường gọi trực tiếp; navigation, migration và dữ liệu lịch sử có thể còn phụ thuộc.
- Không dừng server/IDE của người dùng chỉ để dọn cache hoặc giải phóng file lock.

## Kiểm tra sau thay đổi source

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

Báo riêng Passed/Failed/Skipped; test model bị skip không được tính là model đã đạt.

## An toàn vận hành

- Không tự chạy migration trên database thật, rotate credential production, tạo request provider có phí, gửi webhook vào môi trường dùng chung hoặc publish release nếu người dùng chưa chỉ rõ môi trường và cho phép tác động.
- Trước SQL thay đổi dữ liệu phải xác minh instance, database, backup và khả năng restore.
- Không đưa signing key, refresh token, API key, OTP/App Password, connection string production hoặc prompt nhạy cảm vào source, log hay câu trả lời.
- Không sửa hoặc xóa license, provenance, checksum hay attribution bên thứ ba nếu thiếu bằng chứng rà soát tương ứng.
