# Hướng dẫn cho AI agent — VideoMaker

File này áp dụng cho toàn bộ repository. Khi sửa một thư mục có `AGENTS.md` riêng, phải tuân thủ cả file gốc này và file gần nhất trong cây thư mục.

## Bắt đầu một phiên làm việc

1. Trả lời người dùng bằng tiếng Việt, trừ khi họ yêu cầu ngôn ngữ khác.
2. Đọc `README.md`, `NGHIEP_VU_HE_THONG_VIDEOMAKER.md` và `KE_HOACH_SERVER_AI_GATEWAY.md` trước thay đổi lớn.
3. Đọc `TRIEN_KHAI_AI_GATEWAY_TO_CHUC.md` nếu công việc liên quan database, credential, giá AI hoặc phát hành.
4. Kiểm tra file đang có và đường gọi thực tế; không khôi phục thiết kế BYOK từ lịch sử hoặc tài liệu cũ.
5. Giữ nguyên thay đổi không liên quan của người dùng. Repository có thể không dùng Git, vì vậy không được dựa vào `git reset` để hoàn tác.

## Nghiệp vụ cốt lõi

VideoMaker là ứng dụng desktop hỗ trợ một quy trình sản xuất video:

1. Người dùng chọn tổ chức và tạo dự án.
2. OpenAI sinh content plan, kịch bản và prompt có cấu trúc.
3. Kling sinh clip theo từng cảnh.
4. Server polling các task Kling, kể cả khi desktop đã đóng.
5. Desktop tải clip qua proxy của server và tiếp tục dựng video trong workspace bằng FFmpeg.

AI được quản trị theo tổ chức, không theo máy và không phát key cho từng người dùng:

- Một tổ chức có nhiều thành viên và có một credential Active cho mỗi provider.
- Một người dùng có thể thuộc nhiều tổ chức nhưng phải chọn tổ chức hiện hành khi làm việc.
- Mỗi request được quy về tổ chức, user, project, model, provider request, credential version và rate snapshot.
- Ngân sách tháng của tổ chức và hạn mức thành viên được kiểm tra trước khi gọi provider.
- Budget bằng `0` nghĩa là khóa AI, không phải không giới hạn.

## Các bất biến không được phá vỡ

- API key OpenAI/Kling chỉ đi từ màn hình/API quản trị qua HTTPS vào `TOOL-SERVER`, được test rồi mã hóa bằng ASP.NET Core Data Protection.
- Không thêm màn hình nhập key, kho key, provider HTTP client hoặc fallback gọi OpenAI/Kling trực tiếp trong `TOOL-LOCAL`.
- Không trả plaintext key hoặc URL output gốc của Kling về desktop; chỉ trả secret hint và URL proxy tương đối.
- Mọi request AI phải xác minh JWT, session, device claim, license lease, organization membership, role và quyền sở hữu project.
- Chỉ `Owner`, `OrganizationAdmin`, `BillingManager`, `Member` được phát sinh AI; `Viewer` không được phát sinh chi phí.
- Chỉ Global Admin tạo tổ chức và quản lý bảng giá. Chỉ Owner được quản lý Owner; không được làm mất Owner Active cuối cùng.
- Credential rotation phải test trước khi ghi. Key cũ vẫn phục vụ task đang chạy theo vòng đời `Active -> Retiring -> Revoked`.
- Không tự đoán giá provider. Thiếu rate phải dừng với `pricing_not_configured` trước outbound call.
- Giữ ngân sách và quyết toán phải dùng rate snapshot cùng transaction cô lập `Serializable`; idempotency nằm trong phạm vi tổ chức.
- Outbound provider chỉ dùng HTTPS và allowlist hiện tại: `api.openai.com:443`, `api-singapore.klingai.com:443`.
- Proxy Kling phải giữ authorization, chống SSRF/DNS rebinding, giới hạn redirect, MIME và kích thước.
- Desktop không được ghi bảng sự thật về credential, provider request hoặc usage. Kết nối SQL desktop hiện chỉ là giải pháp chuyển tiếp cho dữ liệu workflow.

## Vai trò và phạm vi quyền

| Role | Thành viên | Budget/usage | Credential | Dùng AI |
|---|---:|---:|---:|---:|
| `Owner` | Có | Có | Có | Có |
| `OrganizationAdmin` | Có | Có | Có | Có |
| `BillingManager` | Không | Có | Không | Có |
| `Member` | Không | Không | Không | Có |
| `Viewer` | Không | Không | Không | Không |

## Cấu trúc solution

- `TOOL-SERVER`: ASP.NET Core API/Razor admin; auth, license, tổ chức, budget, pricing, credential, OpenAI/Kling gateway, worker và output proxy.
- `TOOL-LOCAL`: WinForms + WebView2/React; đăng nhập, chọn tổ chức, quản lý project/workspace, gọi gateway và xử lý media cục bộ.
- `TOOL-SHARED.Contracts`: DTO request/response dùng chung giữa server và desktop.
- `TOOL-TESTS`: xUnit cho nghiệp vụ, bảo mật, updater và media.
- `TOOL-UPDATER`: tiến trình cập nhật desktop có backup/rollback.
- `TOOL-SETUP`: bộ cài launcher/desktop.
- `database`: schema khởi tạo, migration 4.0 và role SQL ít quyền cho desktop.
- `scripts`: publish package desktop vào `artifacts`; `artifacts` là đầu ra có thể tái tạo, không phải source.

## Nguồn sự thật và trạng thái hiện tại

- Source code và migration đang là nguồn sự thật kỹ thuật.
- `NGHIEP_VU_HE_THONG_VIDEOMAKER.md` là nguồn sự thật nghiệp vụ.
- `TRIEN_KHAI_AI_GATEWAY_TO_CHUC.md` là runbook vận hành.
- `SO_DO_HOAT_DONG_API_AI.docx` là sơ đồ bàn giao, không phải cấu hình runtime.
- Migration 4.0 đã có trong source nhưng chưa được mặc định xem là đã chạy trên database thật.
- Backend API tổ chức đã có; UI quản trị đầy đủ cho member/budget/credential/pricing vẫn là hạng mục mở.
- Hệ thống vẫn dùng SQL trực tiếp từ desktop cho workflow trong giai đoạn chuyển tiếp; mục tiêu dài hạn là đưa toàn bộ workflow qua server.

## Quy tắc thay đổi

- Thay DTO công khai: sửa `TOOL-SHARED.Contracts` trước, rồi cập nhật đồng thời server, desktop và test.
- Thay schema: tạo migration SQL idempotent mới; không âm thầm sửa lịch sử đã triển khai nếu có thể làm sai checksum/quy trình vận hành.
- Thay AI generation: kiểm tra cả access control, idempotency, budget reservation, settlement/release, request log và worker retry.
- Thay credential: không log request body/header/secret; response không được chứa encrypted payload.
- Thay WebView bridge: cập nhật đồng thời TypeScript message, C# contract/handler và trạng thái busy/organization selection.
- Không xóa các entity/model legacy chỉ vì không thấy đường gọi trực tiếp; EF navigation, migration hoặc khả năng đọc dữ liệu cũ có thể vẫn cần chúng.
- Có thể xóa `bin`, `obj`, `.vs`, `node_modules`, `dist`, `artifacts` và `*.tsbuildinfo`; tất cả đều được sinh lại.

## Kiểm tra bắt buộc

Sau thay đổi source, chạy từ root:

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Build `TOOL-LOCAL` sẽ chạy `npm ci` nếu chưa có `node_modules` và luôn chạy `npm run build`. Khi chỉ sửa web, có thể kiểm tra nhanh:

```powershell
Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
```

Mốc xác minh gần nhất ngày 2026-08-31: restore thành công, Release build không warning/error và 347/347 test đạt. Không ghi nhận mốc mới nếu chưa thực sự chạy lại.

## An toàn vận hành

- Không tự chạy migration trên database thật, rotate credential production, tạo request OpenAI/Kling thật hoặc publish release nếu người dùng chưa chỉ rõ môi trường và cho phép tác động/chi phí.
- Trước lệnh SQL thay đổi dữ liệu phải xác minh instance, database, backup và khả năng restore.
- Không đưa signing key, refresh token, API key, connection string production hoặc prompt nhạy cảm vào source, log hay câu trả lời.
- Không dừng server/IDE đang chạy của người dùng chỉ để dọn cache hoặc giải phóng file lock.
