# Hướng dẫn cho AI agent — VideoMaker

> Áp dụng cho toàn bộ repository. Cập nhật ngữ cảnh: 2026-09-06.

Khi làm việc trong thư mục có `AGENTS.md` riêng, phải tuân thủ đồng thời file này và file gần nhất trong cây thư mục.

## Bắt đầu phiên làm việc

1. Trả lời bằng tiếng Việt, trừ khi người dùng yêu cầu ngôn ngữ khác.
2. Đọc [README.md](README.md), [BOI_CANH_HE_THONG_HIEN_HANH.md](BOI_CANH_HE_THONG_HIEN_HANH.md), [NGHIEP_VU_HE_THONG_VIDEOMAKER.md](NGHIEP_VU_HE_THONG_VIDEOMAKER.md) và [KIEN_TRUC_KY_THUAT.md](KIEN_TRUC_KY_THUAT.md) trước thay đổi lớn.
3. Đọc [VAN_HANH_VA_PHAT_HANH.md](VAN_HANH_VA_PHAT_HANH.md) nếu công việc liên quan database, secret, credential, giá AI, thanh toán, package hoặc phát hành.
4. Đọc [KIEM_THU_VA_NGHIEM_THU.md](KIEM_THU_VA_NGHIEM_THU.md) trước khi chọn phạm vi kiểm thử hoặc kết luận tính năng đã sẵn sàng.
5. Kiểm tra source, migration, cấu hình mặc định và đường gọi thực tế. Không phục hồi thiết kế BYOK hoặc hành vi cũ từ lịch sử Git.
6. Giữ nguyên thay đổi không liên quan của người dùng. Worktree có thể đang bẩn và một số file hiện hành có thể chưa được theo dõi.

## Nguồn sự thật

Thứ tự ưu tiên khi có khác biệt:

1. Source code, project file, cấu hình mặc định và migration hiện hành.
2. `NGHIEP_VU_HE_THONG_VIDEOMAKER.md` cho quy tắc nghiệp vụ.
3. `KIEN_TRUC_KY_THUAT.md` cho ranh giới module và đường gọi.
4. `BOI_CANH_HE_THONG_HIEN_HANH.md` cho trạng thái source, kiểm thử và rollout.
5. `VAN_HANH_VA_PHAT_HANH.md` và `KIEM_THU_VA_NGHIEM_THU.md` cho thao tác có kiểm soát.

Không dùng `bin`, `obj`, `.vs`, `dist`, `artifacts`, ảnh giao diện, file bàn giao hoặc output build cũ làm nguồn sự thật thay source.

## Nghiệp vụ cốt lõi

VideoMaker là ứng dụng desktop hỗ trợ quy trình sản xuất video:

1. Người dùng đăng nhập, có license/device lease hợp lệ và chọn tổ chức.
2. Người dùng tạo dự án video dài hoặc video ngắn.
3. OpenAI tạo content plan, kịch bản, nhân vật, tài sản text và prompt có cấu trúc cho video dài.
4. Provider video được policy tổ chức chọn tạo clip; Kling là mặc định, BytePlus/Fal chỉ chạy sau rollout có kiểm soát.
5. Server polling task, cache output và quyết toán ngay cả khi desktop đã đóng.
6. Desktop tải clip qua proxy, kiểm tra media, yêu cầu người dùng nghe/duyệt và dựng video bằng FFmpeg.

AI được quản trị theo tổ chức, không theo máy:

- Mỗi tổ chức có tối đa một credential `Active` cho mỗi provider.
- Một user có thể thuộc nhiều tổ chức nhưng phải chọn tổ chức hiện hành.
- Mỗi request phải truy được organization, user, project, model, provider request, credential version và rate snapshot.
- Budget bằng `0` nghĩa là khóa AI.

## Bất biến bảo mật và chi phí

- API key OpenAI/Kling/BytePlus/Fal chỉ đi qua HTTPS vào `TOOL-SERVER`, được test rồi mã hóa bằng ASP.NET Core Data Protection.
- Không thêm UI nhập key, secret store provider, provider SDK/client hoặc fallback gọi Cloud trực tiếp trong `TOOL-LOCAL` hay translation worker.
- Không trả plaintext key, encrypted payload, Base64 provider output hoặc URL output gốc về desktop. Chỉ trả secret hint và URL proxy tương đối.
- Mọi request AI phải kiểm tra JWT, session, device claim, license lease, organization membership, role và project ownership.
- `Viewer` không được phát sinh chi phí. Các role được dùng AI là `Owner`, `OrganizationAdmin`, `BillingManager`, `Member`.
- Chỉ Global Admin tạo tổ chức và quản lý bảng giá. Chỉ Owner quản lý Owner; không được làm mất Owner `Active` cuối cùng.
- Credential rotation phải test trước khi ghi. Task đang chạy giữ đúng credential version theo vòng đời `Active -> Retiring -> Revoked`.
- Không tự đoán giá. Thiếu rate phải trả `pricing_not_configured` trước outbound.
- Reservation, settlement và release phải dùng rate snapshot, transaction `Serializable` và idempotency trong phạm vi tổ chức.
- Outbound provider chỉ dùng HTTPS và allowlist source hiện hành.
- Output proxy phải giữ authorization, chống SSRF/DNS rebinding, giới hạn redirect, MIME và kích thước.
- Không log Authorization, secret, prompt nhạy cảm, toàn văn lời nói/phụ đề, Base64 hoặc signed output URL.

## Bất biến desktop và dữ liệu

- Desktop không được ghi bảng sự thật về credential, provider request server hoặc usage ledger.
- Kết nối SQL trực tiếp từ desktop chỉ là giải pháp chuyển tiếp cho workflow schema `vf`; không mở rộng quyền sang `ai`, `auth`, `dbo` hoặc `vs`.
- Registry `vs.Projects` do server sở hữu. Desktop chỉ đồng bộ metadata Vietsub qua API; subtitle, media và workspace nằm local.
- Đường dẫn workspace phải là đường dẫn tương đối đã kiểm tra nằm trong root cho phép.
- File tải về phải dùng `.part`, kiểm tra signature/MIME/size/hash rồi mới rename atomically.
- Render cuối chỉ dùng `SceneVideo` thuộc đúng `ApprovedGenerationId` và phải kiểm lại hash, video stream, audio cùng thời lượng.

## Bất biến video

- Project snapshot provider/model/policy/resolution/Native Audio khi bắt đầu workflow; thay policy tổ chức không đổi project cũ.
- Video dài Kling/Fal dùng content tiếng Việt `vi-VN`; video ngắn `DirectShortVideo` và BytePlus không bị áp quy tắc này.
- Cảnh có một nhân vật và có lời phải dùng `OnCameraDialogue`; `NativeVoiceOver` chỉ hợp lệ cho B-roll không gắn nhân vật.
- Retry sau `NativeAudioInvalid` là request có phí mới, cần xác nhận người dùng và dùng recovery profile do server quyết định.
- Tài sản `Background`/`Prop`/`Item` hiện là text-only. Nếu scene có assignment thì phải có đúng một `Background` và mọi tài sản phải được khóa.
- Fal/Veo chỉ dùng `SceneFirstFrame` Approved/current đúng tỷ lệ; không gửi ảnh identity vuông trực tiếp sang Veo và không fallback Text-to-Video.
- TTS/WAV được giữ để tương thích nhưng không nằm trên workflow mặc định và không được dùng làm fallback ngầm.

## Bất biến Vietsub local

- Nút **Dịch tiếng Việt** luôn hiện; thiếu active OCR track có cue phải báo quét OCR và không tạo job.
- Chỉ track `PADDLE_OCR_LOCAL`, ngôn ngữ `en`/`zh`, cue tồn tại và revision khớp mới được dịch qua CTA hiện hành.
- Cue manual/locked không bị ghi đè; output stale/invalid không được apply; SRT được ghi atomically.
- Qwen/LLamaSharp chỉ chạy trong worker x64 riêng. Worker không có Cloud client, credential hoặc database workflow.
- Marker `READY` chỉ hợp lệ khi khớp đầy đủ model/worker/protocol/config/backend/native fingerprint và đã qua probe runtime/English/Chinese.
- Không bật mặc định `VietsubLocalTranslationEnabled` hoặc tuyên bố production-ready khi model integration/benchmark còn `Skipped` hay chưa có smoke desktop.

## Cấu trúc solution

- `TOOL-SERVER`: ASP.NET Core API/Razor Admin, auth, license/SePay, tổ chức, budget, pricing, credential, AI Gateway, worker và output proxy.
- `TOOL-LOCAL`: WinForms + WebView2/React, project/workspace, gateway client và media local.
- `TOOL-VIETSUB-TRANSLATION-WORKER`: tiến trình x64 cô lập LLamaSharp/Qwen.
- `TOOL-SHARED.Contracts`: DTO public dùng chung.
- `TOOL-DISTRIBUTION`: kiểm tra manifest và tính toàn vẹn bundle.
- `TOOL-TESTS`: xUnit cho server, desktop, worker, migration, updater và media.
- `TOOL-UPDATER`: cập nhật có backup/rollback.
- `TOOL-SETUP`: bộ cài launcher/desktop.
- `database`: bootstrap, migration và role SQL ít quyền.
- `scripts`: chuẩn bị/kiểm tra bundle, publish và test opt-in.

## Quy tắc thay đổi

- Thay DTO public: sửa `TOOL-SHARED.Contracts` trước, rồi cập nhật server, desktop và test trong cùng thay đổi.
- Thay schema: tạo migration SQL idempotent mới; không sửa âm thầm migration có thể đã triển khai.
- Thay generation: kiểm tra access control, idempotency, pricing, reservation, settlement/release, request snapshot, worker retry và output proxy.
- Thay credential: không log request body/header/secret; response không chứa encrypted payload.
- Thay WebView bridge: cập nhật TypeScript message, C# contract/handler, validation và trạng thái busy/organization đồng thời.
- Không xóa entity/model legacy chỉ vì không thấy đường gọi trực tiếp; navigation, migration và dữ liệu cũ có thể còn phụ thuộc.
- Có thể xóa output sinh lại được như `bin`, `obj`, `.vs`, `node_modules`, `dist`, `artifacts`, `*.tsbuildinfo`, nhưng không dừng IDE/app đang chạy chỉ để dọn cache.

## Kiểm tra bắt buộc sau thay đổi source

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Khi chỉ sửa web, có thể chạy nhanh trước full suite:

```powershell
Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
npm test -- --run
```

Không dùng số test lịch sử làm kết quả mới. Báo riêng Passed/Failed/Skipped và không coi test model bị skip là đạt.

## An toàn vận hành

- Không tự chạy migration trên database thật, rotate credential production, tạo request provider có phí, gửi webhook production hoặc publish release nếu người dùng chưa chỉ rõ môi trường và cho phép tác động.
- Trước lệnh SQL thay đổi dữ liệu phải xác minh instance, database, backup và khả năng restore.
- Không đưa signing key, refresh token, API key, App Password, connection string production hoặc prompt nhạy cảm vào source, log hay câu trả lời.
- Không sửa hoặc xóa license, provenance, checksum hay attribution bên thứ ba nếu không có thay đổi package/version/hash/source và bằng chứng rà soát tương ứng.
