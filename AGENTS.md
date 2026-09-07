# Hướng dẫn AI agent — VideoMaker

File này áp dụng cho toàn repository. Nếu thư mục đang sửa có `AGENTS.md` riêng, phải tuân thủ thêm file gần nhất trong cây thư mục.

## Bắt đầu phiên làm việc

1. Trả lời bằng tiếng Việt, trừ khi người dùng yêu cầu ngôn ngữ khác.
2. Đọc `README.md`, `NGHIEP_VU_HE_THONG_VIDEOMAKER.md` và `TRANG_THAI_DU_AN.md`.
3. Đọc `TRIEN_KHAI_AI_GATEWAY_TO_CHUC.md` khi công việc liên quan database, credential, giá AI, thanh toán, môi trường hoặc phát hành.
4. Chạy `git status --short`; repository có thể đang có thay đổi chưa commit. Không hoàn tác hoặc ghi đè thay đổi không liên quan.
5. Kiểm tra source, migration và đường gọi thực tế trước khi sửa. Không khôi phục thiết kế BYOK hoặc hành vi cũ chỉ từ lịch sử Git.

## Nguồn sự thật

Theo thứ tự ưu tiên kỹ thuật:

1. Source code, project configuration và migration hiện hành.
2. `NGHIEP_VU_HE_THONG_VIDEOMAKER.md` cho bất biến nghiệp vụ và bảo mật.
3. `TRANG_THAI_DU_AN.md` cho trạng thái triển khai và backlog.
4. `TRIEN_KHAI_AI_GATEWAY_TO_CHUC.md` cho vận hành.
5. `SO_DO_HOAT_DONG_API_AI.docx` chỉ là sơ đồ bàn giao, không phải cấu hình runtime.

Không dùng số test, trạng thái rollout hoặc cấu hình môi trường trong commit cũ làm bằng chứng hiện hành. Muốn công bố mốc mới phải thực sự chạy lại kiểm tra.

## Kiến trúc không được phá vỡ

- `TOOL-SERVER` là ranh giới tin cậy cho auth, license, tổ chức, credential, pricing, budget, provider request, usage, worker và output proxy.
- `TOOL-LOCAL` là WinForms + WebView2/React, quản lý trải nghiệm desktop, workspace và media cục bộ. Desktop không được giữ hoặc gọi trực tiếp bằng provider API key.
- `TOOL-SHARED.Contracts` chứa DTO công khai dùng chung.
- Desktop còn truy cập SQL workflow trong giai đoạn chuyển tiếp; không mở rộng quyền này sang credential, provider request hoặc usage truth.
- Task video bất đồng bộ được server polling kể cả khi desktop đóng. Desktop chỉ đọc trạng thái, tải output qua URL tương đối của server rồi dựng bằng FFmpeg.

## Bất biến bảo mật và chi phí

- API key chỉ đi từ API/UI quản trị qua HTTPS vào server, được test rồi mã hóa bằng ASP.NET Core Data Protection.
- Không trả plaintext key, encrypted payload, Authorization header hoặc URL output gốc của provider về desktop.
- Mọi request có chi phí phải xác minh JWT, session, device, license lease, membership, role, organization và project ownership trước outbound call.
- `Viewer` chỉ đọc và không được phát sinh chi phí. `Owner`, `OrganizationAdmin`, `BillingManager`, `Member` được dùng AI theo quyền hiện hành.
- Budget bằng `0` nghĩa là khóa AI. Thiếu rate phải trả `pricing_not_configured`; không tự đoán giá.
- Reserve/settle/release dùng rate snapshot và transaction cô lập `Serializable`; idempotency nằm trong phạm vi organization.
- Credential rotation phải test trước khi Active. Task đang chạy tiếp tục dùng credential version đã snapshot qua vòng đời `Active -> Retiring -> Revoked`.
- Provider runtime chỉ dùng HTTPS/443 và exact host allowlist trong source: OpenAI, Kling, BytePlus và Fal. Khi thay allowlist phải cập nhật resolver, credential tester, output proxy, tài liệu và security test cùng lượt.
- Output proxy phải chống SSRF/DNS rebinding, pin địa chỉ đã kiểm tra, giới hạn redirect, MIME, dung lượng và retention.
- Không log secret, token, connection string, request body nhạy cảm, Base64 ảnh hoặc provider output URL.

## Vai trò

| Role | Quản lý thành viên | Budget/usage | Credential | Dùng AI |
|---|---:|---:|---:|---:|
| `Owner` | Có | Có | Có | Có |
| `OrganizationAdmin` | Có | Có | Có | Có |
| `BillingManager` | Không | Có | Không | Có |
| `Member` | Không | Không | Không | Có |
| `Viewer` | Không | Không | Không | Không |

Chỉ Global Admin tạo organization, quản lý catalog/rate và pool phân bổ. Chỉ Owner quản lý Owner; không được làm mất Owner Active cuối cùng.

## Quy tắc thay đổi

- DTO công khai: sửa Contracts trước, sau đó cập nhật server, desktop và test trong cùng thay đổi.
- Schema: tạo migration SQL idempotent mới; không sửa migration đã có khả năng được triển khai.
- AI generation: kiểm tra access, idempotency, pricing, budget reservation, settlement/release, request log, retry và output lifecycle.
- Credential: không log body/header/secret; response chỉ có hint và metadata an toàn.
- WebView bridge: cập nhật đồng thời TypeScript type/message, C# contract/handler, busy state và organization selection.
- Không xóa model/entity legacy chỉ vì không thấy gọi trực tiếp; EF navigation, migration và dữ liệu lịch sử có thể vẫn cần.
- Chỉ sửa source. `bin`, `obj`, `.vs`, `node_modules`, `dist`, `artifacts` và `*.tsbuildinfo` là output có thể tái tạo.

## Kiểm tra sau khi sửa source

Từ root:

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Khi sửa frontend, chạy thêm hoặc kiểm tra nhanh bằng:

```powershell
Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
npm test
```

Không cập nhật số lượng test trong tài liệu nếu chưa chạy thực tế.

## An toàn vận hành

- Không tự chạy migration trên database thật, rotate credential, tạo request provider có phí, mô phỏng webhook vào môi trường dùng chung hoặc publish release.
- Trước SQL thay đổi dữ liệu phải xác minh instance, database, backup và khả năng restore.
- Không dừng server/IDE của người dùng chỉ để dọn cache hoặc giải phóng file lock.
- Không đưa signing key, refresh token, API key, OTP, connection string production hoặc prompt nhạy cảm vào source, log hay câu trả lời.
