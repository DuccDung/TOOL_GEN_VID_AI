# Bối cảnh hệ thống hiện hành

### Bổ sung 2026-09-09 trên branch fix-voice

Đã bổ sung luồng thử nghiệm đồng nhất giọng Veo local: policy/migration 4.1.8, runtime/worker, anchor/job/retry/duyệt, UI và render guard. Từ 2026-09-10, `Features.VeoLocalVoiceConsistencyEnabled=true` trong cấu hình desktop của repository theo yêu cầu triển khai; flag chỉ mở tính năng, project vẫn phải bật riêng và runtime phải qua checksum/probe. Không tự chạy migration hoặc request provider có phí; chất lượng tiếng Việt/khẩu hình trên clip Veo thật còn phải nghiệm thu. Lịch sử triển khai nằm tại [KE_HOACH_TRIEN_KHAI_VEO_LOCAL.md](KE_HOACH_TRIEN_KHAI_VEO_LOCAL.md); tiến độ và bằng chứng mới nằm tại [TASK_TRIEN_KHAI_VEO_LOCAL_SU_DUNG_THUC_TE.md](TASK_TRIEN_KHAI_VEO_LOCAL_SU_DUNG_THUC_TE.md).

Theo yêu cầu và database người dùng chỉ định, migration 4.1.8 đã áp thành công trên DUNGDEV / VideoFactory ngày 2026-09-09 sau backup mới, checksum verification, restore thật và kiểm tra idempotency/constraint trên clone. Đã xác minh cột/constraint/version, dữ liệu 17 project cũ giữ nguyên và 0 project được tự bật local policy. Backup được giữ, database rehearsal đã dọn; chi tiết trong biên bản cuối nhật ký triển khai. Không bật feature hoặc coi việc áp schema là nghiệm thu ứng dụng/production; WebView2, quyền desktop thật và clip Veo tiếng Việt vẫn cần kiểm tra.

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
| TikTok Direct Post | Đã có OAuth, credential Admin, upload local, server polling và quản lý nhiều tài khoản; người dùng báo luồng đăng hiện tại hoạt động | Nhiều tài khoản cần migration 4.1.8, bật MultiAccountEnabled và smoke riêng trên môi trường được phép |
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
- Item TikTok mặc định hiển thị với `Features:TikTokEnabled=true`; server có `TikTok:AdminManagedCredentialsEnabled=true` nhưng chưa bật runtime khi không có credential database `Active`, giữ `TikTok:Enabled=false`, `TikTok:EmergencyDisabled=false`, `TikTok:AuditedForPublicPosting=false` và source không chứa secret.
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
20. `VideoFactory.4.1.6.TikTokPublishing.sql`
21. `VideoFactory.4.1.7.TikTokAdminCredentials.sql`
22. `VideoFactory.4.1.8.TikTokMultiAccount.sql`

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

Mốc triển khai item **Đăng TikTok** ngày 2026-09-07:

- `dotnet restore` đạt; Release build toàn solution đạt với 0 warning, 0 error.
- .NET: 965 passed, 0 failed, 3 skipped, 968 total; riêng test có tên TikTok: 26 passed, 0 failed, 0 skipped.
- Frontend: production build đạt; 13/13 test file và 66/66 test đạt. Vite còn cảnh báo chunk JavaScript lớn hơn 500 kB, không phải lỗi build.
- Ba test opt-in bị skip vẫn là Qwen model integration, Qwen benchmark và Piper model integration; không được tính là model/runtime đã đạt.
- Migration 4.1.6 đã được áp dụng trên database local có backup được xác minh; không cấu hình TikTok secret thật, không gọi Content Posting API và chưa smoke desktop/TikTok account.

Mốc bổ sung quản lý TikTok Developer App trong Global Admin ngày 2026-09-08:

- `dotnet restore` đạt; Release build toàn solution đạt với 0 warning, 0 error.
- .NET: 977 passed, 0 failed, 3 skipped, 980 total; riêng test có tên TikTok: 38 passed, 0 failed, 0 skipped.
- Frontend: production build và 66/66 test đạt. Vite còn cảnh báo chunk JavaScript lớn hơn 500 kB, không phải lỗi build.
- Ba test opt-in bị skip là Qwen model integration, Qwen benchmark và Piper model integration; không được tính là model/runtime đã đạt.
- Migration 4.1.7 đã được áp dụng idempotent trên database local sau khi tạo full backup `COPY_ONLY/CHECKSUM` và `RESTORE VERIFYONLY` đạt; chưa gọi TikTok thật và chưa smoke desktop/tài khoản TikTok.

## Việc còn mở ưu tiên

1. Rehearsal chuỗi migration đến 4.1.7 trên bản sao của từng môi trường đích, sau đó rollout có backup/restore đã thử; kết quả local không thay thế staging/production rehearsal.
2. Đăng ký/review TikTok app, nhập credential đã regenerate trong Global Admin, yêu cầu xác minh rồi hoàn tất OAuth bằng đúng tài khoản Admin trên Desktop; smoke upload/status bằng tài khoản test trước khi xác nhận public posting.
3. Cấu hình rate/credential/policy/budget và smoke riêng cho từng provider; không gộp Kling, BytePlus và Fal thành một cờ hoàn tất.
4. Rehearsal SePay ở staging, gồm duplicate webhook, late payment, seat shortage và rollback vận hành.
5. Rehearsal Canonical Voice trên staging: migration, catalog preview, pacing, TTS WAV, mix/render, export và xác nhận video dài Provider Native không gọi ASR.
6. Verify/benchmark model Qwen thật, probe runtime/Anh/Trung và smoke CTA dịch trên desktop x64.
7. Verify model/runtime Piper thật, kiểm kê dependency/license, benchmark CPU, nghe nghiệm thu và smoke tạo giọng trên desktop x64.
8. Đưa phần workflow desktop còn dùng SQL trực tiếp qua server API và thu hẹp/bỏ database role desktop.
9. Hoàn thiện health/metrics/alert cho worker, budget, polling, cache và webhook.
10. Phê duyệt bundle FFmpeg ở scope Release, rồi smoke install/update/rollback.
11. Nghiệm thu thủ công Admin responsive và các workflow UI chính.

## Bộ tài liệu chuẩn

- [README.md](README.md): điểm vào repository.
- [NGHIEP_VU_HE_THONG_VIDEOMAKER.md](NGHIEP_VU_HE_THONG_VIDEOMAKER.md): nghiệp vụ và bất biến.
- [KIEN_TRUC_KY_THUAT.md](KIEN_TRUC_KY_THUAT.md): kiến trúc và đường gọi.
- [VAN_HANH_VA_PHAT_HANH.md](VAN_HANH_VA_PHAT_HANH.md): triển khai, vận hành, rollback, release.
- [KIEM_THU_VA_NGHIEM_THU.md](KIEM_THU_VA_NGHIEM_THU.md): test matrix và Definition of Done.

Các `AGENTS.md` theo thư mục vẫn có hiệu lực. Các file provenance, license và nguồn ảnh bên thứ ba là hồ sơ bắt buộc, không phải tài liệu lịch sử để gom/xóa.

## Cập nhật nhiều tài khoản TikTok — 2026-09-09

Source đã có quản lý nhiều tài khoản, OAuth reconnect đúng danh tính, ClientRequestId riêng mỗi lần đăng, durable publish attempts, lịch sử/account snapshot, worker claim và bridge chống phản hồi đến muộn. Theo yêu cầu trực tiếp của người dùng, migration `4.1.8-tiktok-multi-account` đã áp vào `DUNGDEV / VideoFactory` và xác minh lúc 15:35 UTC ngày 2026-09-09. Sau đó đã bật `TikTok:MultiAccountEnabled=true` trong `TOOL-SERVER/appsettings.json` của workspace theo yêu cầu người dùng.

Kiểm tra tại workspace: Release build thành công (0 warning C#, 0 error); .NET 1029 passed / 0 failed / 3 skipped với SQL test opt-in; frontend 77 passed / 0 failed; browser desktop 7 passed, Admin state/browser 18 passed. Model Qwen/Piper bị skip không được tính là đạt. Vite vẫn cảnh báo bundle lớn hơn 500 kB.

Đã rehearsal migration 4.1.8 hai lần và khóa đồng thời trên instance SQL Server LocalDB riêng với dữ liệu giả. Khi áp vào database đang dùng, đã backup COPY_ONLY/CHECKSUM, VERIFYONLY, restore bản sao đầy đủ, chạy migration hai lần trên bản sao và DBCC CHECKDB đạt. So sánh trước/sau trên database đích xác nhận giữ nguyên 1 kết nối, 2 job, OAuth/app credential và 1 Data Protection key. Bản sao thử đã dọn, backup được giữ lại. Khi bật cờ, server đang chạy từ checkout `Branch-Tool-Sub`, khác workspace hiện tại; chưa restart server đó hoặc xác minh cờ trên runtime. Chưa OAuth/đăng thật nhiều tài khoản và chưa phát hành. Người dùng đã báo đăng được bằng luồng cũ; đó không phải kết quả smoke nhiều tài khoản của agent. Chi tiết và đường dẫn backup: [TRIEN_KHAI_TIKTOK_NHIEU_TAI_KHOAN.md](TRIEN_KHAI_TIKTOK_NHIEU_TAI_KHOAN.md).
