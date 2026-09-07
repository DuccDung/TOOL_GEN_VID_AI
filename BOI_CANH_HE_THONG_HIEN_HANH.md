# Bối cảnh hệ thống hiện hành

> Chỉ mục trạng thái liên module. Rà soát theo source ngày 2026-09-07.

Tài liệu này phân biệt rõ bốn mức: **đã có trong source**, **đã có kiểm thử tự động**, **đã xác minh thủ công trên môi trường**, và **đã rollout production**. Không được suy từ mức trước sang mức sau nếu thiếu bằng chứng.

## Nguồn sự thật

Khi có mâu thuẫn, dùng thứ tự sau:

1. Source code, project file, cấu hình mặc định và migration hiện hành.
2. [NGHIEP_VU_HE_THONG_VIDEOMAKER.md](NGHIEP_VU_HE_THONG_VIDEOMAKER.md) cho quy tắc nghiệp vụ.
3. [KIEN_TRUC_KY_THUAT.md](KIEN_TRUC_KY_THUAT.md) cho ranh giới module và đường gọi.
4. Tài liệu này cho trạng thái tích hợp, kiểm thử và rollout.
5. [VAN_HANH_VA_PHAT_HANH.md](VAN_HANH_VA_PHAT_HANH.md) và [KIEM_THU_VA_NGHIEM_THU.md](KIEM_THU_VA_NGHIEM_THU.md) cho quy trình thao tác.

Migration có trong repository không chứng minh migration đã chạy trên database thật. Test tự động đạt không thay thế smoke test có credential, model và môi trường thật.

## Trạng thái theo phạm vi

| Phạm vi | Trạng thái source | Bằng chứng còn thiếu trước rollout |
|---|---|---|
| Gateway AI theo tổ chức | Có auth, membership/role, ownership, pricing, budget, idempotency, credential version, reservation/settlement và request log | Rehearsal database, cấu hình từng môi trường, smoke và quan sát vận hành |
| OpenAI content/image/speech | Adapter, catalog, policy, content pacing, TTS/transcription và luồng quyết toán đã có | Credential/rate thật và smoke có kiểm soát; speech không phải fallback mặc định |
| Canonical Voice và speech verification | Voice profile/version, catalog/preview, TTS WAV, technical validation, audio mix/render và audited review đã có; mặc định tắt. Video dài Provider Native nghe/duyệt trực tiếp và không gọi ASR | Migration 4.1.3–4.1.5, TTS/transcription rate theo scope, staging smoke và rollout flag; `OnCameraDialogue` chưa có lip-sync engine |
| Kling video | Luồng video dài/ngắn, Native Audio, polling, recovery và output proxy đã có | Smoke trả phí theo model/policy được duyệt |
| BytePlus Seedance | Adapter, polling và catalog đã có; seed mặc định `Disabled` | Rate, credential, allowlist output thực tế và rollout riêng |
| Fal/Veo | Adapter, polling và luồng `SceneFirstFrame` cho `LongForm` đã có; seed mặc định `Disabled` | Migration 4.1.1 trên môi trường đích, rate/credential và smoke trả phí |
| SePay/license/seat | Payment order, webhook matching, organization provisioning và seat allocation đã có trong source; mặc định `Enabled=false` | Staging rehearsal, secret/webhook validation, QR/bank config, idempotency và đối soát |
| Vietsub editor | Workspace local, manifest, SQLite, timeline/editor và API registry metadata đã có | Smoke desktop trên bundle phát hành và nghiệm thu UX |
| Paddle OCR local | Luồng OCR local và test liên quan đã có; feature mặc định bật | Runtime/model bundle thật, smoke Anh/Trung và đo tài nguyên |
| Dịch local Qwen | Worker x64 cô lập, IPC, readiness fingerprint, apply/retry/cancel và hai resource profile Standard/Low-memory đã có; RAM/commit thấp là cảnh báo có xác nhận được snapshot vào job, còn hard blocker thực tế vẫn chặn; feature mặc định tắt | Verify model thật, benchmark mức khuyến nghị Low-memory 6 GB, probe Anh/Trung và smoke desktop cả nhánh cảnh báo; test opt-in đang có thể `Skipped` |
| Giọng local Piper | Pipeline phrase/cache/checkpoint, worker Python cô lập, kiểm tra WAV/SHA-256, fit tối đa 1.20x với diagnostic không chặn, FFmpeg timeline, playback nội bộ và UI đã có; một giọng Việt, feature mặc định bật nhưng runtime/model chỉ tải sau xác nhận | Verify runtime/model thật, kiểm kê license/dependency Python, benchmark CPU, nghe nghiệm thu và smoke desktop trên bundle phát hành |
| Updater/setup/distribution | Source kiểm tra manifest, checksum, backup/rollback và package đã có | Bundle release thật, ký/phê duyệt, smoke install/update/rollback |
| Workflow media mở rộng | Một số lớp và UI nền đã có; local voice/mix đã có MVP nhưng chưa rollout | Whisper, cloud translation và full export Vietsub chưa được coi là hoàn tất end-to-end |

## Mặc định quan trọng

- Kling 3.0 Native Audio 720p là lựa chọn video mặc định trong catalog source.
- OpenAI Text/Image/Voice và Kling có catalog hoạt động theo seed hiện hành; khả dụng thực tế còn phụ thuộc policy, rate và credential.
- BytePlus và Fal được seed `Disabled`, không tự bật khi deploy.
- Fal/Veo chỉ áp dụng `LongForm`, cần `SceneFirstFrame` Approved/current đúng tỷ lệ.
- Server có `CanonicalVoiceEnabled=false` và `SpeechVerificationEnabled=false`; desktop có `SpeechSynchronizationEnabled=false`.
- SePay mặc định `Payments:Sepay:Enabled=false`.
- Desktop có `VietsubEnabled=true`, `VietsubOcrEnabled=true`, `VietsubLocalTranslationEnabled=false`, `VietsubLocalVoiceEnabled=true`; máy chưa có Piper/model sẽ ở trạng thái `NOT_INSTALLED` và yêu cầu người dùng chủ động xác nhận cài.
- Desktop mặc định còn có connection string SQL workflow; đây là trạng thái chuyển tiếp, không phải kiến trúc đích.

## Database hiện hành

Chuỗi migration đang có trong source:

1. `VideoFactory.Initial.sql`
2. `VideoFactory.4.0.0.OrganizationAiGateway.sql`
3. `VideoFactory.4.0.1.VietnameseSeedTextRepair.sql`
4. `VideoFactory.4.0.2.GptImageCharacterReference.sql`
5. `VideoFactory.4.0.3.SceneVoiceTts.sql`
6. `VideoFactory.4.0.4.BytePlusSeedance.sql`
7. `VideoFactory.4.0.5.SceneNativeAudioStatuses.sql`
8. `VideoFactory.4.0.6.NativeAudioWorkflowStatuses.sql`
9. `VideoFactory.4.0.7.ProjectAssetTextLibrary.sql`
10. `VideoFactory.4.0.8.AiGeneratedProjectAssets.sql`
11. `VideoFactory.4.0.9.FalVeoLongForm.sql`
12. `VideoFactory.4.0.10.LicenseSepayPayments.sql`
13. `VideoFactory.4.0.11.OrganizationSeatProvisioning.sql`
14. `VideoFactory.4.1.0.VietsubProjectRegistry.sql`
15. `VideoFactory.4.1.1.SceneFirstFrames.sql`
16. `VideoFactory.4.1.2.ProviderRequestFailureDetails.sql`
17. `VideoFactory.4.1.3.SpeechSynchronization.sql`
18. `VideoFactory.4.1.4.VoiceProfileApproval.sql`
19. `VideoFactory.4.1.5.SpeechVerificationReview.sql`

`VideoFactory.DesktopLeastPrivilege.sql` cấp quyền chuyển tiếp cho desktop; `Verify.VideoFactory.4.0.11.OrganizationSeatProvisioning.sql` là script xác minh chuyên biệt. Không có bằng chứng trong repository rằng toàn bộ chuỗi trên đã được áp dụng vào production.

## Mốc kiểm thử lịch sử

Mốc được tài liệu hóa ngày 2026-09-06, chỉ dùng làm tham chiếu lịch sử:

- Web: 29/29 test đạt và production build đạt.
- Restore đạt; Release build không warning/error.
- .NET: 802 passed, 0 failed, 2 skipped, 804 total khi TEMP/TMP đặt trên ổ D.

Các lệnh này chưa được chạy lại chỉ vì chuẩn hóa Markdown. Hai test model/benchmark bị skip không chứng minh dịch local đã sẵn sàng.

Mốc triển khai local voice ngày 2026-09-07:

- Web: 31/31 test đạt và production build đạt.
- Restore đạt; Release build toàn solution đạt với 0 warning, 0 error.
- .NET: 814 passed, 0 failed, 3 skipped, 817 total.
- Ba test opt-in bị skip gồm Qwen model integration, Qwen benchmark và Piper model integration; không test nào trong ba test này được coi là đã đạt.

Mốc chuyển RAM dịch local thành cảnh báo có xác nhận ngày 2026-09-07:

- Web production build đạt; 32/32 test đạt.
- Restore đạt; Release build toàn solution đạt với 0 warning, 0 error.
- .NET: 820 passed, 0 failed, 3 skipped, 823 total.
- Ba test opt-in vẫn bị skip gồm Qwen model integration, Qwen benchmark và Piper model integration. Nhánh xác nhận RAM đã có fake-worker/bridge test nhưng chưa thay thế verify model thật hoặc smoke desktop trên máy ít RAM.

Mốc hợp nhất `main` vào `local-2` ngày 2026-09-07:

- `dotnet restore` đạt; Release build toàn solution đạt với 0 warning, 0 error.
- .NET: 939 passed, 0 failed, 3 skipped, 942 total.
- Frontend: production build đạt; 12/12 test file và 64/64 test đạt.
- Ba test opt-in bị skip gồm một Piper model integration, một Qwen model integration và một Qwen benchmark; không test nào trong ba test này được coi là model/runtime đã nghiệm thu.
- Các lệnh trên xác minh source sau hợp nhất; chưa chạy migration database, provider có phí, model thật hoặc smoke desktop.

## Việc còn mở ưu tiên

1. Chạy migration rehearsal đến 4.1.5 trên bản sao database, sau đó rollout từng môi trường có backup/restore đã thử.
2. Cấu hình rate/credential/policy/budget và smoke riêng cho từng provider; không gộp Kling, BytePlus và Fal thành một cờ hoàn tất.
3. Rehearsal SePay ở staging, gồm duplicate webhook, late payment, seat shortage và rollback vận hành.
4. Rehearsal Canonical Voice trên staging: migration, catalog preview, pacing, TTS WAV, mix/render, export và xác nhận video dài Provider Native không gọi ASR.
5. Verify/benchmark model Qwen thật, probe runtime/Anh/Trung và smoke CTA dịch trên desktop x64.
6. Verify model/runtime Piper thật, kiểm kê dependency/license, benchmark CPU, nghe nghiệm thu và smoke tạo giọng trên desktop x64.
7. Đưa phần workflow desktop còn dùng SQL trực tiếp qua server API và thu hẹp/bỏ database role desktop.
8. Hoàn thiện health/metrics/alert cho worker, budget, polling, cache và webhook.
9. Phê duyệt bundle FFmpeg ở scope Release, rồi smoke install/update/rollback.
10. Nghiệm thu thủ công Admin responsive và các workflow UI chính.

## Bộ tài liệu chuẩn

- [README.md](README.md): điểm vào repository.
- [NGHIEP_VU_HE_THONG_VIDEOMAKER.md](NGHIEP_VU_HE_THONG_VIDEOMAKER.md): nghiệp vụ và bất biến.
- [KIEN_TRUC_KY_THUAT.md](KIEN_TRUC_KY_THUAT.md): kiến trúc và đường gọi.
- [VAN_HANH_VA_PHAT_HANH.md](VAN_HANH_VA_PHAT_HANH.md): triển khai, vận hành, rollback, release.
- [KIEM_THU_VA_NGHIEM_THU.md](KIEM_THU_VA_NGHIEM_THU.md): test matrix và Definition of Done.

Các `AGENTS.md` theo thư mục vẫn có hiệu lực. Các file provenance, license và nguồn ảnh bên thứ ba là hồ sơ bắt buộc, không phải tài liệu lịch sử để gom/xóa.
