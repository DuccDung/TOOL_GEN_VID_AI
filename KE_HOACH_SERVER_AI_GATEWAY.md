# Kế hoạch và trạng thái triển khai Server AI Gateway

> Cập nhật ngữ cảnh: 2026-08-31. Kiến trúc AI Gateway theo tổ chức, GPT-Image-2, Kling Native Audio 720p và gateway video đa provider Kling/BytePlus đã được triển khai trong source. BytePlus/Seedance được seed disabled và chưa được coi là đã rollout nếu thiếu migration, credential, rate, policy và smoke test có phí. Gateway kiểm tra scene-plan/prompt version, dùng snapshot idempotency theo tổ chức và không lưu raw provider payload hoặc signed output URL. Migration chưa được tự động chạy trên database đang sử dụng.

Tài liệu này chỉ theo dõi trạng thái source/vận hành còn mở. Nghiệp vụ nằm tại `NGHIEP_VU_HE_THONG_VIDEOMAKER.md`; lệnh triển khai nằm tại `TRIEN_KHAI_AI_GATEWAY_TO_CHUC.md`.

## 1. Hạng mục đã triển khai

- [x] Hợp đồng Organization, member role, budget, provider và usage.
- [x] Schema/migration 4.0 cho Organization, membership, credential version, budget period, reservation, usage ledger và audit.
- [x] Bootstrap dữ liệu cũ vào `legacy-default` với budget `0`.
- [x] Role matrix Owner/OrganizationAdmin/BillingManager/Member/Viewer.
- [x] Bảo vệ Owner cuối cùng; OrganizationAdmin không thể sửa Owner.
- [x] Credential OpenAI/Kling mã hóa trên server, test trước rotate và Active → Retiring → Revoked.
- [x] Allowlist outbound provider cố định và HTTPS-only.
- [x] Bảng giá InputToken/OutputToken/VideoSecond, rate snapshot và API quản trị giá.
- [x] Reservation/settlement/release bằng transaction `Serializable`.
- [x] Reconciliation worker cho reservation dở dang.
- [x] OpenAI Responses API với structured output, `store=false`, hashed `safety_identifier` và usage token.
- [x] GPT-Image-2 text-to-image cố định một ảnh PNG 1024×1024 medium; server tự dựng prompt và kiểm tra Base64/MIME/kích thước/SHA-256.
- [x] Migration idempotent 4.0.2 cho output ảnh tạm, liên kết request–character–MediaAsset, retention worker và deny payload đối với desktop SQL role.
- [x] API tạo/tải ảnh có authorization, budget, rate snapshot, idempotency và URL server tương đối; không trả URL provider hoặc Base64.
- [x] Desktop lưu `.part` nguyên tử, tạo primary `CharacterReference`, hỗ trợ tạo lại/preview/khóa thủ công và không gọi OpenAI trực tiếp.
- [x] Kling submit/status, worker polling nền và credential version snapshot.
- [x] Kling Native Audio dùng prompt server-side, scene/prompt version hiện hành, snapshot hash không chứa full prompt/spoken text/Base64 và lỗi provider đã chuẩn hóa.
- [x] Migration 4.0.4–4.0.6 cho policy video, project snapshot, cache output và trạng thái `PromptInvalid`/`AudioReviewRequired`/`NativeAudioInvalid`.
- [x] Contract/API video trung lập provider; desktop không gửi provider/model/prompt/thời lượng/độ phân giải làm nguồn sự thật.
- [x] BytePlus Seedance client, prompt composer, policy resolver, worker đa provider, pricing theo `completion_tokens` và output cache/proxy dùng chung.
- [x] Catalog Seedance 2.0/2.5 được seed disabled; không tự bật provider, model, rate, credential hoặc policy tổ chức.
- [x] Output proxy có authorization, DNS/IP SSRF checks, redirect/size limit.
- [x] Generation API xác thực JWT/session/device/license/organization/project và rate limit.
- [x] Idempotency theo tổ chức cùng request hash.
- [x] Desktop chỉ dùng `ServerGenerationClient`; đã xóa client gọi trực tiếp provider và mã lưu DPAPI.
- [x] Desktop cho chọn tổ chức, lọc dự án theo tổ chức và gắn OrganizationId khi tạo dự án.
- [x] Desktop không còn UI nhập key; bundle production không chứa UI BYOK cũ.
- [x] Bộ dọn credential BYOK cũ chỉ xóa đúng `provider-secrets.bin` và `.tmp`.
- [x] SQL role ít quyền cho database user desktop.
- [x] Cập nhật README, tài liệu nghiệp vụ và runbook triển khai.
- [x] Build toàn solution Release không có warning/error.
- [x] Unit/security test cho role, cost snapshot, allowlist, SSRF và legacy credential cleanup.

## 2. Hạng mục vận hành phải làm khi triển khai

- [ ] Backup và thử restore database đích.
- [ ] Chạy `VideoFactory.Initial.sql`, migration 4.0.0 đến 4.0.6 và script least privilege theo runbook.
- [ ] Tạo database user riêng cho server và desktop.
- [ ] Cấu hình JWT signing key/Data Protection cho môi trường production.
- [ ] Tạo tổ chức, gán thành viên, budget và member limit thật.
- [ ] Nhập rate riêng cho model Text, `gpt-image-2`, Kling và provider video thực sự rollout từ hợp đồng/provider dashboard.
- [ ] Xác nhận tổ chức OpenAI đã được phép dùng GPT-Image-2; xử lý bước organization verification nếu provider yêu cầu.
- [ ] Nhập production credential qua HTTPS bằng Owner/OrganizationAdmin.
- [ ] Chạy staging smoke test có phê duyệt chi phí với OpenAI Text, GPT-Image-2 và Kling thật; smoke test BytePlus trên tổ chức thử nghiệm riêng nếu rollout Seedance.
- [ ] Đối chiếu usage ledger với hóa đơn/provider dashboard.
- [ ] Phát hành desktop gateway sau khi server/migration/configuration sẵn sàng.
- [ ] Theo dõi worker, provider 401/403/429/5xx và reservation quá hạn.

## 3. Hạng mục mở rộng

- [x] Xây UI quản trị Organization/member/budget/credential/pricing/usage/audit tại `/admin`; global Admin vẫn phải có membership phù hợp cho thao tác nội bộ tổ chức.
- [ ] Chuyển toàn bộ dữ liệu workflow desktop sang server để bỏ hoàn toàn kết nối SQL trực tiếp; hiện dùng role ít quyền trong giai đoạn chuyển tiếp.
- [ ] Thêm metrics/health dashboard và cảnh báo production.
- [ ] Integration test migration trên bản sao SQL Server có dữ liệu production.
- [ ] Hỗ trợ HTTP Range/resume cho video lớn nếu cần.

## 4. Điều kiện phát hành

1. Migration có đủ version từ `4.0.0-organization-ai-gateway` đến `4.0.6-native-audio-workflow-statuses`.
2. Budget/rate/credential đã cấu hình và credential test thành công.
3. Desktop không có provider key và dùng database role riêng.
4. Cross-organization, Viewer, license hết hạn và budget exceeded đều bị chặn trước provider.
5. Task video của provider được bật vẫn hoàn thành qua worker khi desktop đóng.
6. Usage ledger truy được theo Organization, user, project, request và credential version.
7. Build/test Release đạt và staging live smoke test đã được phê duyệt.
8. GPT-Image-2 có đủ rate riêng, ảnh tải qua server đúng hash/MIME và retry không tạo request hoặc chi phí trùng.
9. Nếu rollout BytePlus, provider/model chỉ được bật sau smoke test; project đã snapshot Kling không tự đổi provider và ảnh upload/người thật bị chặn trước outbound BytePlus.

Chi tiết thao tác: [TRIEN_KHAI_AI_GATEWAY_TO_CHUC.md](TRIEN_KHAI_AI_GATEWAY_TO_CHUC.md).
