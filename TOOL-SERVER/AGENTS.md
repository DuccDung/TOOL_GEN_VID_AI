# Hướng dẫn AI agent — TOOL-SERVER

Áp dụng thêm `../AGENTS.md`. Chỉ đọc `../TRIEN_KHAI_AI_GATEWAY_TO_CHUC.md` khi công việc chạm database, credential, pricing, provider rollout, payment hoặc release.

## Trách nhiệm

Server là ranh giới tin cậy duy nhất cho:

- auth, session, device, password reset và license;
- organization, membership/RBAC, budget/usage/audit;
- provider catalog/model/rate và credential mã hóa;
- OpenAI/Kling/BytePlus/Fal gateway;
- polling, settlement, output cache/proxy/cleanup;
- SePay, organization seat, Vietsub registry và desktop release.

Controller phải mỏng; validation nghiệp vụ, transaction và idempotency nằm trong service.

## Thứ tự bắt buộc cho request có chi phí

1. JWT, session và device.
2. License lease.
3. Organization membership/role.
4. Project ownership.
5. Payload, request hash và idempotency.
6. Provider/model/policy/capability.
7. Credential version.
8. Rate Active.
9. Budget reservation.
10. Outbound provider call.
11. Persist response/status; settle, release hoặc để worker reconcile theo trạng thái chắc chắn.

Không gọi provider, ghi submit mới hoặc release reservation khi trạng thái upstream còn không chắc chắn.

## Auth

- Login dùng kết quả nghiệp vụ có cấu trúc cho invalid credentials/account state; lỗi dự kiến không thành 500.
- Refresh token lưu hash, rotate atomically và revoke family/session khi reuse.
- API authenticated phải xác minh session/device server-side trên mỗi token.
- Forgot-password giữ response chung, OTP dùng CSPRNG, không lưu/log plaintext và reset thành công revoke session cũ.

## Credential, pricing và provider

- Data Protection purpose hiện hành không được đổi nếu không có kế hoạch migrate ciphertext.
- Credential mới phải test trước khi Active; response chỉ trả hint/version/status.
- Bootstrap tạo catalog/capability nhưng không seed giá hoặc tự bật BytePlus/Fal.
- Runtime exact-host HTTPS/443: OpenAI, Kling, BytePlus, Fal theo resolver hiện hành; Fal credential test dùng host riêng đã allowlist.
- OpenAI dùng Responses API/structured output/`store=false`.
- Kling/BytePlus/Fal task video do worker server polling; status endpoint desktop chỉ đọc database.
- Fal Standard/Fast không fallback; output URL upstream không được persist/return ngoài storage path an toàn.
- Thiếu rate trả `pricing_not_configured`; request luôn settle bằng rate snapshot đã lưu.

## Output proxy

- Xác minh user/device/license/organization/project/request owner trước download.
- Chỉ HTTPS và host output allowlist theo provider.
- Resolve DNS, chặn mọi IP không public, pin địa chỉ đã kiểm tra và kiểm tra lại mỗi redirect.
- Giới hạn tối đa 3 redirect, MIME, file/storage size và retention.
- Cache qua file tạm, hash SHA-256 rồi promote; response dùng no-store/nosniff và không lộ URL gốc.

## Quy tắc sửa

- Dùng `IHttpClientFactory`, `CancellationToken`, error code ổn định và log có cấu trúc.
- Không log Authorization, key, prompt/transcript nhạy cảm, Base64 hoặc raw provider response có URL.
- Thay contract công khai phải sửa `TOOL-SHARED.Contracts` trước.
- Thay EF/schema phải thêm migration idempotent và cập nhật least-privilege.
- Thay worker phải có test retry/restart/idempotency/settlement.
- Thay authorization phải có test dương và cross-user/cross-org/Viewer.
