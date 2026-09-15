# Bối cảnh hệ thống hiện hành

### Hợp nhất chọn lọc `main` vào `local-2` — 2026-09-15

Nhận thay đổi video ngắn Veo/nhân vật-trang phục, thư viện video ngắn, phục hồi tải output và Bilibili từ `origin/main` tại `9b8052c`. Giữ nguyên Vietsub, gate SystemSetup, chức năng Đăng TikTok hiện hành và không nhận tính năng Publishing mới có luồng đăng TikTok; giữ nguyên các file `.tmp` của `local-2`. Xung đột trong composition desktop/React được ghép theo từng phần. Restore/build Release đạt; .NET 1.352 Passed / 0 Failed / 6 Skipped, frontend 224 Passed / 0 Failed / 0 Skipped. Chưa chạy migration, provider có phí, đăng TikTok, model opt-in hoặc phát hành.

Cập nhật ngày 2026-09-15 trên nhánh `local-2`: ứng dụng đầy đủ đã có màn hình Setup OCR/FFmpeg/Qwen/Piper bằng modal React/WebView. Sau đăng nhập, `Form1` và nền màn hình dự án được hiển thị trước; modal mới phủ và khóa nền nếu còn thành phần bắt buộc chưa `READY`. Host C# đồng thời chặn command nghiệp vụ, nên gate không phụ thuộc riêng vào lớp phủ trình duyệt. Phạm vi tích hợp giữ nguyên video AI và Dịch Cloud; không mang chế độ `VietsubLocalOnly` hoặc điều hướng rút gọn từ `sub-local`. Restore/build Release đạt 0 warning, 0 error; .NET chạy tuần tự collection **1.081 Passed / 0 Failed / 4 Skipped**, frontend **152 Passed / 0 Failed / 0 Skipped**. Chưa chạy model opt-in, package repair thật, desktop/Windows sạch hoặc phát hành. Xem [báo cáo tích hợp](BAO_CAO_SETUP_HE_THONG.md).

Hợp nhất nhánh `local-2` ngày 2026-09-15: giữ commit Setup/Vietsub/lật hình `1333ab2` và các commit GitHub đến `dc6cdc3`, xử lý 5 file conflict về tài liệu, composition desktop và React. Checkout đã restore/build Release (0 warning, 0 error), frontend **187 Passed / 0 Failed / 0 Skipped**, .NET tuần tự **1.242 Passed / 0 Failed / 6 Skipped**. Các migration TikTok/LocalVoice có trong source nhưng không được chạy trong lần hợp nhất; model thật, SQL opt-in, WebView2 trực tiếp và môi trường production chưa được nghiệm thu. Xem [mục kiểm thử hợp nhất](KIEM_THU_VA_NGHIEM_THU.md).

### Hợp nhất local-2 và main — 2026-09-10

Đã xử lý 21 khối xung đột trong 10 file, giữ Vietsub/Cloud/xuất MP4/phục hồi phiên cùng popup tạo dự án ngắn-dài, giọng Veo local và TikTok. Sửa thêm tương tác giữa khóa license và xác minh credential TikTok cho Admin. Xác minh source tại checkout riêng: Release build đạt; .NET **1185 Passed / 0 Failed / 5 Skipped**, frontend **174 Passed / 0 Failed / 0 Skipped**; TikTok state/browser **25 Passed / 0 Failed / 0 Skipped** và smoke popup đạt. Không có tác động database/provider hoặc thay binary đang chạy. Chi tiết và giới hạn: [biên bản hợp nhất](HOP_NHAT_LOCAL_2_MAIN_20260910.md).

### Loại bỏ lip-sync Cloud — 2026-09-10

Source giữ hai luồng: Canonical WAV riêng ghép vào video cảnh và Veo Native Audio đồng nhất giọng local. Thoại nhân vật Canonical đã duyệt WAV tiếp tục tạo video nền/ghép/duyệt, không chờ engine Cloud. Module Cloud và cờ build thử nghiệm đã xóa; trạng thái cũ chỉ đọc tương thích, migration/dữ liệu lịch sử được giữ. Kiểm thử trong checkout riêng: Release build đạt; .NET **1091 Passed / 0 Failed / 5 Skipped**, frontend **89 Passed / 0 Failed / 0 Skipped**. Server/desktop đang chạy chưa được thay bản; chưa nghiệm thu video provider thật. Xem [biên bản và phạm vi triển khai](LOAI_BO_LIP_SYNC_CLOUD.md).

### Bổ sung 2026-09-09 trên branch fix-voice

Đã bổ sung luồng thử nghiệm đồng nhất giọng Veo local: policy/migration 4.1.8, runtime/worker, anchor/job/retry/duyệt, UI và render guard. Từ 2026-09-10, `Features.VeoLocalVoiceConsistencyEnabled=true` trong cấu hình desktop của repository theo yêu cầu triển khai; flag chỉ mở tính năng, project vẫn phải bật riêng và runtime phải qua checksum/probe. Không tự chạy migration hoặc request provider có phí; chất lượng tiếng Việt/khẩu hình trên clip Veo thật còn phải nghiệm thu. Lịch sử triển khai nằm tại [KE_HOACH_TRIEN_KHAI_VEO_LOCAL.md](KE_HOACH_TRIEN_KHAI_VEO_LOCAL.md); tiến độ và bằng chứng mới nằm tại [TASK_TRIEN_KHAI_VEO_LOCAL_SU_DUNG_THUC_TE.md](TASK_TRIEN_KHAI_VEO_LOCAL_SU_DUNG_THUC_TE.md).

Theo yêu cầu và database người dùng chỉ định, migration 4.1.8 đã áp thành công trên DUNGDEV / VideoFactory ngày 2026-09-09 sau backup mới, checksum verification, restore thật và kiểm tra idempotency/constraint trên clone. Đã xác minh cột/constraint/version, dữ liệu 17 project cũ giữ nguyên và 0 project được tự bật local policy. Backup được giữ, database rehearsal đã dọn; chi tiết trong biên bản cuối nhật ký triển khai. Không bật feature hoặc coi việc áp schema là nghiệm thu ứng dụng/production; WebView2, quyền desktop thật và clip Veo tiếng Việt vẫn cần kiểm tra.

> Chỉ mục trạng thái liên module. Đối chiếu theo source sau hợp nhất ngày 2026-09-15.

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
| Canonical Voice và speech verification | Voice profile/version, catalog/preview, TTS WAV, technical validation, audio mix/render và audited review đã có. Canonical Voice hỗ trợ ghép WAV cho cả lời dẫn và thoại nhân vật; module lip-sync Cloud đã loại bỏ. Video dài Provider Native nghe/duyệt trực tiếp và không gọi ASR | Migration 4.1.3–4.1.5, TTS/transcription rate theo scope, staging smoke và rollout flag; cần nghe nghiệm thu video thực tế |
| Kling video | Luồng video dài/ngắn, Native Audio, polling, recovery và output proxy đã có | Smoke trả phí theo model/policy được duyệt |
| BytePlus Seedance | Adapter, polling và catalog đã có; seed mặc định `Disabled` | Rate, credential, allowlist output thực tế và rollout riêng |
| Fal/Veo | Adapter, polling và luồng `SceneFirstFrame` cho `LongForm` đã có; seed mặc định `Disabled` | Migration 4.1.1 trên môi trường đích, rate/credential và smoke trả phí |
| SePay/license/seat | Payment order, webhook matching, organization provisioning và seat allocation đã có trong source; mặc định `Enabled=false` | Staging rehearsal, secret/webhook validation, QR/bank config, idempotency và đối soát |
| TikTok Direct Post | Đã có OAuth, credential Admin, upload local, server polling và quản lý nhiều tài khoản; người dùng báo luồng đăng hiện tại hoạt động | Nhiều tài khoản cần migration 4.1.8, bật MultiAccountEnabled và smoke riêng trên môi trường được phép |
| Vietsub editor | Workspace local, manifest JSON schema 7 (SQLite schema 6), timeline/editor, thiết kế phụ đề và lật riêng hình video hai chiều theo project, mixer âm gốc/giọng Việt có mute/gain/auto-duck, preview hai kênh cùng playhead và xuất MP4 burn-in + audio mix qua ASS/FFmpeg với publish `.partial` đã kiểm tra | Smoke desktop trên bundle phát hành, nghe/đo audio output, nghiệm thu UX và đối chiếu preview/ASS/libass trên bộ video dọc-ngang, gồm cả hình lật và phụ đề không lật |
| Paddle OCR local | Luồng OCR local và test liên quan đã có; feature mặc định bật | Runtime/model bundle thật, smoke Anh/Trung và đo tài nguyên |
| Dịch local Qwen | Worker x64 cô lập, IPC, readiness fingerprint, apply/retry/cancel và hai resource profile Standard/Low-memory đã có; RAM/commit thấp là cảnh báo có xác nhận được snapshot vào job, còn hard blocker thực tế vẫn chặn; feature mặc định tắt | Verify model thật, benchmark mức khuyến nghị Low-memory 6 GB, probe Anh/Trung và smoke desktop cả nhánh cảnh báo; test opt-in đang có thể `Skipped` |
| Giọng local Piper | Pipeline phrase/cache/checkpoint, worker Python cô lập, kiểm tra WAV/SHA-256, fit tối đa 1.20x với diagnostic không chặn, FFmpeg timeline, playback nội bộ và UI đã có; một giọng Việt, feature mặc định bật nhưng runtime/model chỉ tải sau xác nhận | Verify runtime/model thật, kiểm kê license/dependency Python, benchmark CPU, nghe nghiệm thu và smoke desktop trên bundle phát hành |
| Dịch Cloud OpenAI | Contract/API/worker, budget Vietsub, apply CAS/SRT và CTA Cloud đã nối; 4.1.6 đã rehearsal và áp local `DUNGDEV / VideoFactory` ngày 2026-09-10 theo biên bản trước merge; cấu hình workspace kế thừa `local-2` bật `Enabled=true`, model `gpt-5.6-luna` | Xác minh schema/quyền/rate/credential/budget trên môi trường đích, nhiều instance SQL và smoke OpenAI có phí; merge không xác minh lại database hoặc gọi provider |
| Updater/setup/distribution | Source kiểm tra manifest, checksum, backup/rollback và package đã có | Bundle release thật, ký/phê duyệt, smoke install/update/rollback |
| Workflow media mở rộng | Một số lớp và UI nền đã có; local voice, mixer theo project và xuất MP4 burn-in có trộn timeline giọng Việt đã có trong source nhưng chưa rollout | Whisper, cloud translation và smoke nghe/đo bản xuất Vietsub trên FFmpeg bundle phát hành chưa hoàn tất end-to-end |

## Mặc định quan trọng

- Kling 3.0 Native Audio 720p là lựa chọn video mặc định trong catalog source.
- OpenAI Text/Image/Voice và Kling có catalog hoạt động theo seed hiện hành; khả dụng thực tế còn phụ thuộc policy, rate và credential.
- BytePlus và Fal được seed `Disabled`, không tự bật khi deploy.
- Fal/Veo chỉ áp dụng `LongForm`, cần `SceneFirstFrame` Approved/current đúng tỷ lệ.
- Source server có `CanonicalVoiceEnabled=true` và `SpeechVerificationEnabled=true`; desktop có `SpeechSynchronizationEnabled=false`. Cấu hình runtime có thể ghi đè; flag không thay thế credential, rate, budget hoặc readiness.
- SePay mặc định `Payments:Sepay:Enabled=false`.
- Item TikTok mặc định hiển thị với `Features:TikTokEnabled=true`; server có `TikTok:AdminManagedCredentialsEnabled=true` nhưng chưa bật runtime khi không có credential database `Active`, giữ `TikTok:Enabled=false`, `TikTok:EmergencyDisabled=false`, `TikTok:AuditedForPublicPosting=false` và source không chứa secret.
- Desktop có `VietsubEnabled=true`, `VietsubOcrEnabled=true`, `VietsubLocalTranslationEnabled=false`, `VietsubLocalVoiceEnabled=true`; máy chưa có Piper/model sẽ ở trạng thái `NOT_INSTALLED` và yêu cầu người dùng chủ động xác nhận cài.
- Desktop mặc định còn có connection string SQL workflow; đây là trạng thái chuyển tiếp, không phải kiến trúc đích.

## Thông báo lỗi license và đăng xuất — 2026-09-10

- API license bắt lỗi nghiệp vụ dự kiến tại controller, giữ HTTP status và `ApiErrorResponse`; lỗi bất ngờ vẫn qua global handler.
- Desktop phân biệt `SessionLimit`, `DeviceLimit` và `Unavailable`, báo ngay khi heartbeat từ chối quyền và xóa lease local. Giao diện có **Kiểm tra lại** / **Đăng xuất**, chống bấm đăng xuất lặp và giữ ngữ cảnh dự án khi bị khóa. Đăng xuất chỉ tác động phiên hiện tại, quay về màn hình đăng nhập.
- Thay đổi này xử lý thông báo/phục hồi; quy tắc đếm phiên trên server vẫn giữ nguyên. Việc tách phiên Admin Web khỏi suất desktop là hạng mục riêng.
- Restore, build Release/Debug toàn solution đạt, MSBuild 0 warning/error. Frontend 131 Passed / 0 Failed / 0 Skipped; .NET Release 1.029 Passed / 0 Failed / 3 Skipped. Ba bài model Qwen/Piper opt-in vẫn chưa chạy. Chi tiết và giới hạn bằng chứng nằm trong mục 15 của `KIEM_THU_VA_NGHIEM_THU.md`.

## Tạo giọng vượt thời gian câu bỏ qua — 2026-09-10

Theo yêu cầu tiếp tục tạo giọng bình thường, phần âm thanh tràn sang câu bỏ qua không còn làm job thất bại ở 72%. Renderer vẫn thử fit tối đa 1.20x và rút đuôi im lặng đã xác minh; nếu không đủ, giữ phần tiếng nói tràn và publish timeline với diagnostic không chặn. Câu bỏ qua vẫn không được gửi tới Piper. Tạo mới, thử lại job lỗi cũ và dựng lại từ cache dùng chung quy tắc; không thay fingerprint hoặc sửa WAV phrase đã lưu.

Regression dùng WAV/MP4 fixture và FFmpeg thật kiểm tràn 27/135 ms, tắt phân tích khoảng lặng, câu bắt đầu trong vùng bỏ qua, cache/đổi lựa chọn, giữ hash và phần giọng cuối trong file xuất. Build solution Release và desktop Debug đạt; .NET **1.030 Passed / 0 Failed / 3 Skipped**, frontend **131 Passed / 0 Failed / 0 Skipped**. Ca thử lại job lỗi cũ hoàn thành 100% bằng cache, không gọi synthesizer thêm. Chi tiết ở mục 16 của `KIEM_THU_VA_NGHIEM_THU.md`; ba bài model opt-in vẫn chưa chạy, chưa thay thế nghe nghiệm thu Piper trên video người dùng.

## Nút xuất video trong editor Vietsub — 2026-09-10

Đã thêm nút **Xuất video** ngay bên phải nút thiết kế/menu tệp trên toolbar phụ đề và một nút ở cuối thanh công cụ Timeline, giữ nhãn ở panel hẹp. Phần đầu card được thu gọn: tiêu đề cùng hàng với các nút, số câu/trạng thái/cảnh báo nằm ở hàng nhỏ bên dưới. Hai nút dùng chung luồng lưu bản nháp trước khi xuất MP4, trạng thái đang xuất và khóa bấm lặp; phục hồi sau kết thúc/hủy/lỗi. Kết quả hiển thị trong thông báo của editor; không đổi renderer hoặc contract C#.

Đã sửa lỗi xuất xong vẫn loading: route C# bật `notifyCompletion` để gửi `vietsub.operation.completed` sau kết quả và state hết busy, giải phóng Promise mà hai nút đang chờ. Thông báo thành công có tên file, hủy/lỗi vẫn cho phép thử lại. Regression bridge và frontend kiểm chuỗi message thực tế; kiểm thử nút với Promise giả trước đó chưa bắt được thiếu sót này.

Cụm Âm gốc/Giọng Việt/Tự hạ nền trên Timeline đã được căn sang phải, sát nhóm Theo playhead với khoảng cách 12 CSS px khi đủ rộng. Màn hình hẹp tiếp tục co hoặc chuyển hàng; xem xác minh bố cục ở mục 22 của `KIEM_THU_VA_NGHIEM_THU.md`.

Xác minh cuối: frontend **138 Passed / 0 Failed / 0 Skipped**; .NET **1.033 Passed / 0 Failed / 3 Skipped** với TEMP/TMP riêng có đường dẫn ngắn trên D. Build Release/Debug đạt, bundle desktop khớp Web/dist. WebView2 đã xác minh card gọn ở panel 320/420 px, nút Timeline ở các chiều rộng 420–1160 px và zoom 100/125/150/200%; xem mục 17–19 của `KIEM_THU_VA_NGHIEM_THU.md`. Chưa xuất file từ project người dùng; chạy lại desktop để nạp DLL mới.

## Quay về tạo dự án Vietsub — 2026-09-10

Nút **Tạo dự án mới** có mũi tên quay lại, nằm trong phần đầu card **Thiết lập dự án**, cạnh tiêu đề khi đủ rộng và tự xuống hàng ở panel hẹp. Đã bỏ hàng điều hướng riêng cùng nhãn/mô tả lặp trong card để thu gọn; trên cửa sổ hẹp mở tab **Thiết lập** để truy cập nút. Nút lưu bản nháp rồi gọi `vietsub.project.close` hiện có; khi đóng xong, trang chuyển về thư viện có form nhập tên dự án. Không tự tạo project khi bấm quay về. Chặn bấm lặp/khi đang xử lý hoặc xuất video; lưu lỗi giữ bản nháp, đóng lỗi cho thử lại.

Frontend **141 Passed / 0 Failed / 0 Skipped**; .NET **1.033 Passed / 0 Failed / 3 Skipped**. Restore, build Release solution và Debug desktop đạt; bundle hai cấu hình khớp Web/dist. Kiểm giao diện bằng bundle production với bridge giả: đầu card cao 49 CSS px ở panel mặc định, 76 px khi thu panel xuống 220 px; nút hiện trong tab Thiết lập ở 720×900/zoom 125% và thao tác quay về form ở 1200×900 đạt. Xem mục 20–21 của `KIEM_THU_VA_NGHIEM_THU.md`. Cần chạy lại desktop để nạp bundle mới.

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
20. `VideoFactory.4.1.6.VietsubCloudTranslation.sql`
21. `VideoFactory.4.1.6.TikTokPublishing.sql`
22. `VideoFactory.4.1.7.TikTokAdminCredentials.sql`
23. `VideoFactory.4.1.8.TikTokMultiAccount.sql`
24. `VideoFactory.4.1.8.LocalVoiceConsistency.sql`

Các migration lip-sync Cloud 4.1.6/4.1.7 được giữ để tương thích dữ liệu lịch sử; module thực thi đã loại bỏ. Migration cùng tiền tố số của các module dùng mã SchemaVersions riêng.

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

Rà soát lỗi bỏ qua câu tạo giọng ngày 2026-09-09 trên dirty worktree hiện hành:

- `dotnet restore` đạt; Release build toàn solution đạt với 0 warning và 0 error của MSBuild. Vite vẫn cảnh báo chunk lớn hơn 500 kB.
- .NET: 970 passed, 0 failed, 3 skipped, 973 total. Ba test bỏ qua là Qwen integration, Qwen benchmark và Piper integration; không tính là model đã đạt.
- Frontend: `npm ci`, production build và 99/99 test đạt, 0 failed, 0 skipped.
- Chín ca hồi quy mới chạy FFmpeg thật, gồm rút đuôi dưới ngưỡng tín hiệu, chặn cắt tiếng, ranh giới chồng/tiếp giáp, tạo mới, cache/bật lại, tiến độ lỗi và giải mã MP4 có/không trộn nền. Sửa timestamp và giới hạn padding theo số mẫu để giữ khoảng im lặng và thời lượng timeline.
- Kiểm tra riêng WAV cache gây lỗi bằng renderer mới: 1832 ms sau trim đầu được rút còn 1800 ms, fit cửa sổ 1500 ms ở 1.20x; output đủ 9200 ms, mẫu âm thanh trong đoạn bỏ qua bằng 0 và SHA-256 của WAV đầu vào giữ nguyên. Đầu ra kiểm tra được ghi riêng; không sửa database/project đang sử dụng.
- Bằng chứng .NET: `artifacts/voice-boundary-verification-20260909/voice-boundary-final.trx` (artifact local, không commit). Chưa chạy sinh giọng bằng model thật hoặc nghe nghiệm thu thủ công trong desktop.

Rà soát hiển thị timeline Giọng Việt ngày 2026-09-09 trên dirty worktree hiện hành:

- Tách class nhãn khỏi waveform, tăng chiều cao dòng và khoảng đệm để chữ `Không tạo giọng` không bị cắt chân. Test WebView2 dùng component và CSS production tái hiện cả lỗi cắt chữ lẫn mất sóng trước sửa, đạt sau sửa tại zoom 100%, 125%, 150% và 200%.
- `npm ci`, frontend production build và 99 test đạt, 0 failed, 0 skipped. Restore, Release build toàn solution và Debug build desktop đạt; hai bundle desktop khớp Web/dist. Vite vẫn cảnh báo chunk lớn hơn 500 kB.
- .NET: 971 passed, 0 failed, 3 skipped, 974 total. Ba test model Qwen/Piper opt-in vẫn bị bỏ qua; không tính là model đã đạt. Bằng chứng local: `artifacts/timeline-label-layout/timeline-layout-final.trx` và ảnh WebView2 trong `artifacts/timeline-label-layout/after/`.

Triển khai Dịch Cloud OpenAI, xác minh cuối ngày 2026-09-10 (Asia/Bangkok):

- Đã nối CTA Cloud một lần bấm qua gateway/server OpenAI, job/batch/attempt, readiness và ngân sách riêng cho project Vietsub. Desktop áp kết quả bằng fingerprint/receipt, giữ cue manual/locked và ghi SRT atomically; Local vẫn độc lập.
- Frontend: 23 file, 126 Passed / 0 Failed / 0 Skipped; `npm ci` và production build đạt. Restore, Release build solution và Debug build desktop đạt, MSBuild 0 warning/error. Bundle Debug/Release khớp SHA-256 với Web/dist; Vite vẫn cảnh báo chunk >500 kB.
- Full .NET suite cuối chạy tuần tự collection: 1.014 Passed / 0 Failed / 3 Skipped / 1.017 Total. Ba test Qwen/Piper opt-in không được tính là model đã đạt. Fixture bổ sung kiểm mất POST/ACK, Unknown/đối soát, chống quyết toán lặp từ context cũ và bảo vệ sửa tay.
- WebView2 Cloud đạt tại zoom 100/125/150/200%, kể cả khi Local chưa sẵn sàng; ảnh/JSON local ở `.tmp/cloud-verification-20260909`. Đây là fixture UI, chưa có OpenAI thật.
- Cloud giữ `Enabled=false`, `ModelCode` trống. Chưa apply migration 4.1.6, thử nhiều instance SQL Server hoặc smoke OpenAI có phí. Xem [báo cáo kiểm thử](KIEM_THU_VA_NGHIEM_THU.md#13-xác-minh-triển-khai-dịch-cloud-openai--2026-09-10) và [runbook Cloud](HUONG_DAN_VAN_HANH_DICH_CLOUD_VIETSUB.md).

Database Dịch Cloud cập nhật ngày 2026-09-10 sau vòng kiểm thử source:

- Theo yêu cầu chạy database, đã xác minh đích cấu hình `DUNGDEV / VideoFactory`, tạo backup COPY_ONLY/CHECKSUM mới, VERIFYONLY và restore sang clone riêng. `DBCC CHECKDB` clone đạt; clone đã dọn sau xác minh, backup giữ trong thư mục backup SQL Server.
- Rehearsal phát hiện thiếu SET options cho filtered index khi chạy sqlcmd; đã bổ sung vào migration 4.1.6 chưa áp đích. Script sau sửa chạy hai lần trên clone và một lần trên đích đều đạt.
- Đối chiếu 81 bảng cũ: chỉ thêm version `4.1.6`; 62 reservation, 178 dòng ledger và toàn bộ quyền/role membership giữ nguyên. Ba bảng Cloud mới, FK/CHECK trusted và unique filtered index đã xác minh. Các migration TikTok/lip-sync của nhánh khác được giữ nguyên.
- Test migration 1 Passed / 0 Failed / 0 Skipped. Chưa chạy OpenAI, đổi cấu hình hoặc restart ứng dụng; Cloud vẫn tắt. Xem [báo cáo database](.tmp/cloud-database-20260910_003325/REPORT.md). Kiểm nhiều instance và nghiệm thu model/ứng dụng thật vẫn còn mở.

Cải thiện biên tập phụ đề ngày 2026-09-09: hoàn thành Task 1–5 trong [KE_HOACH_CAI_THIEN_BIEN_TAP_PHU_DE.md](KE_HOACH_CAI_THIEN_BIEN_TAP_PHU_DE.md). Cuộn được tập trung tại danh sách, tạm ngưng theo thao tác người dùng, giữ draft/focus qua revision và từ chối response trang cũ. Banner có nút × theo vòng đời sự kiện/project; danh sách đã bỏ chọn giọng hàng loạt và checkbox, giữ menu từng câu/timeline và API batch bên dưới.

- Frontend: 119 Passed / 0 Failed / 0 Skipped; production build đạt. Restore và Release build toàn solution đạt, MSBuild 0 warning/error; Debug desktop đạt và bundle hai cấu hình khớp Web/dist. Vite vẫn cảnh báo chunk >500 kB.
- .NET cuối chạy tuần tự collection: 972 Passed / 0 Failed / 3 Skipped / 975 Total. Ba test model opt-in Piper/Qwen không được tính là đã đạt. Một lượt song song trước đó có timeout preflight worker; kiểm tra riêng lại và full suite tuần tự đều đạt, không sửa worker/timeout.
- WebView2 editor/Enter, playback và timeline layout đạt. Fixture trang 50 câu/panel 420 px/zoom 100% giảm render row từ 1200 xuống 33, remount từ 50 xuống 0 và độ lệch cuộn tay từ 3032 px xuống 0. Số đo/ảnh ở zoom 100/125/150/200% và bằng chứng `artifacts/subtitle-editor-ui/final-suite-serial.trx` là artifact local. Chưa chạy model thật hoặc nghe video người dùng; không có tác động database/provider.

Rà soát playback Vietsub ngày 2026-09-09: editor và modal dùng chung đồng bộ video/giọng, phục hồi khi giọng tải muộn hoặc tua lại, không chặn video khi AudioContext chờ khởi động; mở lại nguồn giữ đúng lựa chọn giọng/mixer. Registry phát giọng thay entry nguyên tử; lỗi tải/phát hiển thị nút thử lại. Có regression DOM và WebView2 chạy hook production với WAV mẫu; việc nghe Piper trên video người dùng vẫn là bước nghiệm thu riêng.

Cập nhật tool dịch ngày 2026-09-09: đã bổ sung lựa chọn bỏ qua/bật lại giọng theo câu và batch, giữ phụ đề/timing, SQLite schema 6 với migration mặc định bật cho cue cũ. Timeline thể hiện câu bỏ qua; Piper chỉ nhận câu đã chọn; cache được kiểm tra lại khi đổi lựa chọn và xuất MP4 chặn dùng giọng cũ. Preview hạ âm gốc theo tín hiệu giọng thay vì sự hiện diện của phụ đề. Các thay đổi này vẫn cần nghe nghiệm thu bằng Piper và video thực tế trên desktop; không thay thế cổng model/runtime opt-in.

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

1. Chạy migration rehearsal đến 4.1.8 trên bản sao database, gồm project kind của budget/ledger và concurrency Cloud, sau đó rollout từng môi trường có backup/restore đã thử.
2. Đăng ký/review TikTok app, xác minh credential bằng OAuth Admin trên Desktop và smoke nhiều tài khoản/upload/status trên môi trường được phép.
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
