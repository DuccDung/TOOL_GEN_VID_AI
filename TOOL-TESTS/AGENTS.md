# Hướng dẫn AI agent — TOOL-TESTS

Áp dụng thêm `../AGENTS.md`.

## Mục tiêu

Test phải bảo vệ hành vi nghiệp vụ và ranh giới bảo mật, không chỉ tăng số lượng case.

Ưu tiên:

- auth refresh rotation/reuse, device/session/license;
- cross-user, cross-organization, role và project ownership;
- idempotency cùng key/cùng payload và conflict khi payload khác;
- rate/budget fail-closed trước outbound;
- reservation settlement/release/reconciliation;
- credential rotation và không rò secret;
- provider allowlist, SSRF/DNS/redirect/MIME/size;
- polling restart/retry không submit/settle trùng;
- media hash/probe/audio/render lineage;
- migration idempotency/least-privilege contract;
- Vietsub path/context/revision/job recovery/OCR;
- updater/setup traversal, hash và rollback.

## Quy tắc

- Test không gọi provider, webhook hoặc database production thật.
- Dùng fake HTTP handler/time provider/temp workspace và cleanup có giới hạn.
- Integration test FFmpeg/WebView2 chỉ skip khi điều kiện môi trường được mô tả rõ; không che regression logic bằng skip rộng.
- Không chứa API key, token hoặc connection string thật trong fixture/snapshot.
- Khi sửa lỗi phải có test tái hiện lỗi trước và khóa đường đúng sau sửa.
- Không hard-code tổng số test trong source. Chỉ cập nhật baseline tài liệu sau khi chạy thực tế.

Chạy theo `../AGENTS.md`; với frontend chạy thêm `npm test` trong `TOOL-LOCAL/Web`.
