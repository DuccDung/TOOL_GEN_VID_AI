# Hướng dẫn AI agent — TOOL-SERVER

Áp dụng thêm các quy tắc trong `../AGENTS.md`.

## Trách nhiệm của server

`TOOL-SERVER` là ranh giới tin cậy duy nhất cho tài khoản, license và AI. Desktop chỉ gửi yêu cầu nghiệp vụ; server chịu trách nhiệm xác thực, phân quyền, chọn cấu hình provider, giữ ngân sách, gọi provider, quyết toán và ghi audit/usage.

Các nhóm mã chính:

- `Authentication`, `Accounts`, `Controllers/AuthController.cs`, `DevicesController.cs`, `LicenseController.cs`: JWT, refresh rotation, session, device và license lease.
- `Organizations`: membership/RBAC, budget, credential rotation và các worker reconciliation/retirement.
- `Generation`: access context, idempotency, OpenAI Responses, Kling submit/polling và proxy output.
- `Providers`: catalog/model/rate và mã hóa credential. `ProviderAdminDbContext` vẫn cần cho catalog, model, rate và request log; không khôi phục API credential toàn cục cũ.
- `Updates`: lưu package, chính sách cập nhật và download launcher/desktop.
- `Data`/`Domain`: mapping EF tới các schema `auth`, `ai`, `vf`.

## API AI hiện hành

- `GET|POST /api/organizations`
- `GET|POST /api/organizations/{id}/members`
- `PUT /api/organizations/{id}/members/{userId}`
- `PUT /api/organizations/{id}/budget`
- `GET /api/organizations/{id}/providers`
- `PUT /api/organizations/{id}/providers/{providerCode}/credential`
- `GET /api/organizations/{id}/usage`
- `GET /api/organizations/{id}/audit`
- `GET /api/admin/ai-pricing`
- `POST /api/admin/ai-pricing/models/{modelId}/rates`
- `DELETE /api/admin/ai-pricing/rates/{rateId}`
- `GET /api/generation/providers/status?organizationId=...`
- `POST /api/generation/content`
- `POST /api/generation/kling/videos`
- `GET /api/generation/kling/videos/{providerRequestId}`
- `GET /api/generation/kling/videos/{providerRequestId}/content`
- `POST /api/auth/forgot-password`
- `POST /api/auth/reset-password`

Generation controller có rate limit mặc định 30 request/phút theo user/IP.

`POST /api/auth/refresh` phải chuyển refresh token không hợp lệ/hết hạn/tái sử dụng thành `401` hoặc `403` kèm `ApiErrorResponse` ngay tại controller. Đây là kết quả nghiệp vụ để desktop xóa phiên và quay lại đăng nhập, không phải lỗi 500.

`POST /api/auth/login` không dùng exception cho kết quả dự kiến. `AuthService.LoginAsync` trả `AuthLoginResult`: `invalid_credentials`, `account_deleted`, `account_unavailable`, `account_locked` và lỗi validation được controller ánh xạ thành HTTP có cấu trúc. Chỉ lỗi hệ thống ngoài dự kiến mới được ném để exception handler ghi log 500.

`POST /api/auth/forgot-password` phải giữ phản hồi chung cho email tồn tại/không tồn tại, dùng rate limit và không log OTP. OTP phải được sinh bằng CSPRNG, không lưu plaintext, có hạn dùng và giới hạn lần nhập sai. `Smtp:Pass` chỉ nằm trong secret store. Reset thành công phải xóa OTP và thu hồi toàn bộ session/refresh token cũ.

## Thứ tự kiểm tra generation

Không gọi provider trước khi hoàn tất toàn bộ bước tiền kiểm:

1. JWT chứa session ID và device ID hợp lệ.
2. Session/user/device còn Active.
3. License và device lease còn hiệu lực.
4. Membership của tổ chức còn Active và role được phép generation.
5. Project thuộc đúng organization và user.
6. Idempotency key không xung đột với request hash.
7. Model, credential Active và rate bắt buộc tồn tại.
8. Budget organization/member được reserve thành công.

Mọi nhánh lỗi sau reserve phải settle hoặc release đúng trạng thái, hoặc để reconciliation worker xử lý theo thiết kế rõ ràng.

## Credential và outbound security

- Chỉ chấp nhận provider `openai` và `kling` với base URL/host/port trong allowlist.
- Credential mới phải test trước khi transaction rotate làm thay đổi key hiện tại.
- Data Protection purpose hiện là `TOOL_SERVER.OrganizationProviderCredentials.v1`; thay purpose sẽ làm key cũ không giải mã được.
- Request mới dùng credential `Active`; task Kling đang chạy dùng version đã snapshot, kể cả khi credential đó đang `Retiring`.
- Không dùng automatic redirect cho credential test hoặc Kling output proxy.
- Proxy phải resolve DNS, loại IP loopback/private/link-local/reserved/multicast, rồi pin kết nối vào đúng IP đã kiểm tra.
- Output chỉ chấp nhận `video/*` hoặc `application/octet-stream`, tối đa 1 GB, tối đa 3 redirect và có `X-Content-Type-Options: nosniff`.

## Pricing và budget

- Catalog bootstrap hiện tạo `openai/gpt-5.6-luna` và `kling/kling-3.0`, không seed giá.
- OpenAI cần rate `InputToken` và `OutputToken`; Kling cần `VideoSecond`.
- Rate có thể dùng token, 1K token hoặc million token; chi phí phải chuẩn hóa đúng đơn vị.
- Không cho tạo rate hiệu lực trong tương lai theo hành vi hiện tại.
- Mỗi request lưu `RateSnapshotJson`; không truy lại giá mới để quyết toán request cũ.
- Budget reservation dùng transaction `Serializable` và operation key idempotent.

## OpenAI và Kling

- OpenAI dùng Responses API, structured output/JSON Schema, `store=false`, giới hạn output và `safety_identifier` là hash ổn định của user ID.
- Khi provider không trả usage token, actual cost OpenAI dùng estimate đã quote thay vì ghi chi phí 0.
- Endpoint status Kling từ desktop chỉ đọc trạng thái nội bộ; chỉ worker server polling provider để tránh double polling/double settlement.
- Không trả URL video gốc của Kling ra response công khai.

## Quy tắc triển khai

- Controller mỏng; nghiệp vụ và transaction nằm trong service.
- Dùng `IHttpClientFactory`, truyền `CancellationToken` và không log Authorization/API key/prompt nhạy cảm.
- Mã lỗi nghiệp vụ phải ổn định vì desktop sử dụng chúng để hiển thị.
- Với thay đổi authorization, thêm test dương và ít nhất một test chặn cross-user/cross-organization/role không hợp lệ.
- Với thay đổi background worker, chứng minh retry không tạo thêm provider request hoặc ledger entry.
- Không giả định migration 4.0 đã chạy trên database đích chỉ vì source build thành công.
