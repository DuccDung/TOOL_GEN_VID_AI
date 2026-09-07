# Hướng dẫn AI agent — TOOL-SERVER

> Ngữ cảnh liên module: [../BOI_CANH_HE_THONG_HIEN_HANH.md](../BOI_CANH_HE_THONG_HIEN_HANH.md). Cập nhật rà soát: 2026-09-07.

Áp dụng thêm `../AGENTS.md`. Đọc `../VAN_HANH_VA_PHAT_HANH.md` khi chạm database, credential, pricing, provider rollout, payment hoặc release.

## Trách nhiệm

Server là ranh giới tin cậy duy nhất cho auth/session/device/license, organization/RBAC, budget/usage/audit, catalog/rate/credential, OpenAI/Kling/BytePlus/Fal, polling/settlement/output proxy, SePay/seat, Vietsub registry và desktop release.

Controller phải mỏng; validation nghiệp vụ, transaction và idempotency nằm trong service.

## Thứ tự request có chi phí

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
11. Persist response/status; settle, release hoặc reconcile theo trạng thái chắc chắn.

Không gọi provider, ghi submit mới hoặc release reservation khi trạng thái upstream còn không chắc chắn.

## Auth

- Login dùng kết quả nghiệp vụ có cấu trúc; lỗi dự kiến không thành 500.
- Refresh token lưu hash, rotate atomically và revoke family/session khi reuse.
- API authenticated xác minh session/device server-side trên mỗi token.
- Forgot-password giữ response chung, OTP dùng CSPRNG, không lưu/log plaintext và reset thành công revoke session cũ.

## Credential, pricing và provider

- Data Protection purpose hiện hành là `TOOL_SERVER.OrganizationProviderCredentials.v1`; không đổi nếu chưa có migration ciphertext.
- Credential mới phải test trước khi Active; response chỉ trả hint/version/status.
- Bootstrap tạo catalog/capability nhưng không seed giá hoặc tự bật BytePlus/Fal.
- Runtime exact-host HTTPS/443 theo resolver hiện hành; Fal credential test dùng host riêng đã allowlist.
- OpenAI dùng Responses API/structured output/`store=false`; `safety_identifier` là hash ổn định của user ID.
- Kling/BytePlus/Fal do worker server polling; status endpoint desktop chỉ đọc database.
- Fal Standard/Fast không fallback. Output URL upstream không được persist/return ngoài storage path an toàn.
- Thiếu rate trả `pricing_not_configured`; request settle bằng `RateSnapshotJson`, không truy giá mới.
- Khi provider thiếu usage đáng tin cậy, dùng estimate/rate snapshot theo policy thay vì ghi actual cost 0.

## Speech và audio

- Canonical Voice cần feature flag, voice version Approved, TTS credential/model/rate/budget và exact speech/voice snapshot.
- Catalog voice do server sở hữu; alias legacy chỉ dùng cho tương thích, frontend không tự mở rộng allowlist.
- Canonical Voice không phụ thuộc ASR. Speech verification là luồng độc lập và video dài `OpenAiStructuredPlan` Provider Native được nghe/duyệt trực tiếp theo policy hiện hành.
- Không tự gọi repair, TTS, transcription hoặc provider lần hai mà thiếu quote/xác nhận/idempotency mới phù hợp.

## Output proxy

- Xác minh user/device/license/organization/project/request owner trước download.
- Chỉ HTTPS và host output allowlist theo provider; resolve DNS, chặn IP không public và pin địa chỉ đã kiểm tra.
- Kiểm tra lại mỗi redirect; tối đa 3 redirect, giới hạn MIME/size/retention.
- Cache qua file tạm, hash SHA-256 rồi promote; response dùng no-store/nosniff và không lộ URL gốc.

## Quy tắc sửa

- Dùng `IHttpClientFactory`, `CancellationToken`, error code ổn định và log có cấu trúc.
- Không log Authorization, key, prompt/transcript nhạy cảm, Base64 hoặc raw provider response có URL.
- Thay contract public phải sửa `TOOL-SHARED.Contracts` trước.
- Thay EF/schema phải thêm migration idempotent và cập nhật least-privilege.
- Thay worker phải có test retry/restart/idempotency/settlement.
- Thay authorization phải có test dương và cross-user/cross-org/Viewer.
- Không giả định migration đến 4.1.5 đã chạy trên database đích chỉ vì source build thành công.
