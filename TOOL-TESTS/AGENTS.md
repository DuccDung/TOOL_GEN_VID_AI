# Hướng dẫn AI agent — TOOL-TESTS

> Ngữ cảnh và trạng thái test: [../BOI_CANH_HE_THONG_HIEN_HANH.md](../BOI_CANH_HE_THONG_HIEN_HANH.md). Rà soát source: 2026-09-15.

Áp dụng thêm `../AGENTS.md`.

## Mục tiêu

Test phải bảo vệ hành vi nghiệp vụ và ranh giới bảo mật, không chỉ tăng số lượng case. Ưu tiên:

- auth refresh rotation/reuse, device/session/license;
- cross-user, cross-organization, role, Owner cuối cùng và project ownership;
- idempotency cùng key/cùng payload và conflict khi payload khác;
- rate/budget fail-closed, reservation/settlement/release/reconciliation;
- credential rotation, retirement và không rò secret;
- provider allowlist, SSRF/DNS/redirect/MIME/size;
- polling restart/retry không submit/settle trùng;
- media hash/probe/audio/render lineage và Canonical Voice/speech snapshot;
- migration idempotency/least-privilege;
- Vietsub path/context/revision/job recovery/OCR/translation/voice;
- updater/setup traversal, hash, backup và rollback.
- video ngắn `TextOnly`/`CharacterOutfit`: migration 4.1.9 kể cả khi cờ phối đồ tắt, quote/first-frame/lineage Veo, chuyển Kling cũ có xác nhận và không submit lại khi `Unknown`;
- System Setup startup: nền dashboard/modal và gate C# cùng chặn command, role/context, checksum/probe/install/repair/retry;
- Bilibili: URL public/partial scan, selection, cancel/retry và `.part`/checksum/signature/MP4 probe.

## Quy tắc

- Bug fix phải có regression test tái hiện nhánh lỗi.
- Không gọi provider, webhook hoặc database production thật; dùng fake HTTP/time/ID/temp workspace.
- Không chứa API key, token hoặc connection string thật trong fixture/snapshot.
- Integration FFmpeg/WebView2 chỉ skip khi điều kiện môi trường được mô tả rõ; không che regression logic bằng skip rộng.
- Test model Qwen/Piper thật và benchmark dùng category opt-in, kiểm size/SHA-256/resource trước khi spawn. `Skipped` không được tính là model đã đạt.
- Không chạy model thật song song với build hoặc OCR/FFmpeg nặng; không dừng IDE/app của người dùng để lấy tài nguyên.
- Không làm test yếu đi chỉ để pass. Test security phải chứng minh bị từ chối trước outbound hoặc ledger ngoài ý muốn.
- Không hard-code tổng số test trong source; chỉ cập nhật baseline tài liệu sau khi chạy thực tế.
- Số Passed/Failed/Skipped trong Markdown là biên bản của commit/môi trường đã ghi; lượt cập nhật tài liệu không được chép chúng thành kết quả kiểm thử mới.

Chạy theo `../AGENTS.md`; frontend chạy thêm `npm test` trong `TOOL-LOCAL/Web`.
