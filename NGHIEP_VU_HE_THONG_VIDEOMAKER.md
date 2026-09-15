# Nghiệp vụ và kiến trúc VideoMaker

> Nguồn sự thật nghiệp vụ hiện hành. Rà soát theo source ngày 2026-09-07.

Trạng thái triển khai nằm trong `BOI_CANH_HE_THONG_HIEN_HANH.md`; kiến trúc kỹ thuật nằm trong `KIEN_TRUC_KY_THUAT.md`; hướng dẫn vận hành nằm trong `VAN_HANH_VA_PHAT_HANH.md`.

## 1. Mục tiêu và ranh giới hệ thống

VideoMaker hỗ trợ ba nhóm công việc:

1. Tạo video dài nhiều cảnh từ chủ đề và content plan có cấu trúc.
2. Tạo video ngắn một cảnh trực tiếp từ nội dung người dùng.
3. Tạo/chỉnh phụ đề, dịch ngữ cảnh và tạo giọng Việt bằng media/AI local.

AI cloud được quản trị theo organization, không theo máy. Server sở hữu auth, license, organization, chi phí, credential, provider request, output proxy và registry dùng chung; desktop sở hữu trải nghiệm biên tập, workspace và xử lý media local. Không có BYOK.

## 2. Vai trò và quyền

| Role | Quản lý thành viên | Budget/usage | Credential | Phát sinh AI |
|---|---:|---:|---:|---:|
| `Owner` | Có | Có | Có | Có |
| `OrganizationAdmin` | Có | Có | Có | Có |
| `BillingManager` | Không | Có | Không | Có |
| `Member` | Không | Không | Không | Có |
| `Viewer` | Không | Không | Không | Không |

- Global Admin tạo organization, quản lý catalog/rate, license plan, pool và release.
- Chỉ Owner quản lý Owner; không được xóa, hạ cấp hoặc suspend Owner Active cuối cùng.
- OrganizationAdmin không cấp/thu hồi Owner; BillingManager không quản lý member/credential.
- Viewer bị chặn trước mọi outbound có khả năng phát sinh chi phí.

## 3. Xác thực, license và organization

Một request bảo vệ cần JWT, session, user/device Active, device claim đúng, license lease còn hiệu lực, membership/role hợp lệ và project ownership đúng organization. Refresh token lưu hash, rotate atomically và revoke family/session khi reuse.

Một user có thể thuộc nhiều organization nhưng mỗi request chỉ thuộc đúng organization đang chọn. Task đã gửi provider tiếp tục được worker theo dõi và quyết toán theo snapshot ban đầu dù desktop đóng hoặc membership/license thay đổi; không chuyển task sang organization hay credential khác.

## 4. Organization AI Gateway

Mỗi request cloud phải truy vết được organization, user/session/device, project, provider/model, provider request, credential version, rate snapshot, reservation/usage/actual cost và idempotency key/request hash.

Thứ tự bắt buộc trước outbound:

1. JWT, session, user và device.
2. License lease.
3. Membership và role.
4. Project ownership.
5. Payload, request hash và idempotency.
6. Provider/model/policy/capability.
7. Credential version.
8. Rate Active.
9. Budget reservation.
10. Outbound provider.

Không release mù hoặc submit lại khi trạng thái upstream còn không chắc chắn; worker phải reconcile.

## 5. Credential, pricing và budget

- Mỗi organization có tối đa một credential `Active` cho mỗi provider. Credential mới phải test trước khi ghi; response chỉ có hint/version/status.
- Rotation theo `Active -> Retiring -> Revoked`; task đang chạy giữ version đã snapshot.
- Global Admin nhập rate từ hợp đồng/dashboard chính thức; bootstrap không seed giá.
- Thiếu rate trả `pricing_not_configured` trước outbound. Budget organization bằng `0` khóa AI; member limit cũng phải được kiểm tra.
- Reservation/settlement/release dùng transaction `Serializable` và operation key idempotent.
- `RateSnapshotJson` quyết toán request cũ; đổi giá mới không sửa lịch sử. Thiếu usage đáng tin cậy dùng estimate đã khóa theo policy, không mặc định ghi actual cost 0.

## 6. Vòng đời project video

Project có hai cấu trúc:

- `OpenAiStructuredPlan`: video dài nhiều cảnh.
- `DirectShortVideo`: video ngắn một cảnh.

`LongForm` và `Default` là scope của video provider policy, không phải tên cấu trúc project. Project gắn `OrganizationId`, `CreatedByUserId` và snapshot provider/model/policy/resolution/speech production policy. Đổi policy organization chỉ ảnh hưởng project mới.

## 7. Video dài và content plan

- OpenAI dùng Responses API, JSON Schema, `store=false` và safety identifier là hash user ID.
- Nội dung `OpenAiStructuredPlan` dùng `vi-VN`; output gồm script, character, scene, speech intent và project asset có key ổn định.
- Scene có content duration và generation duration; provider có thể tạo clip dài hơn rồi desktop trim tail.
- Output sai schema/ngôn ngữ/nhịp lời vẫn phải ghi request, usage và chi phí đã phát sinh; trả lỗi có field/reason/request ID an toàn.
- Replay cùng idempotency trả lỗi cũ. Chỉ cho tối đa một lượt repair sau quote/xác nhận; repair không đổi key, scene count hoặc cấu trúc đã khóa.

## 8. Character, asset và first frame

- Character có version, visual identity, wardrobe, immutable traits và forbidden changes. Chỉ reference primary Approved/current đúng project được dùng.
- Project asset `Background`, `Prop`, `Item` hiện là text-only, có version và trạng thái Draft/Locked. Scene có assignment phải có đúng một Background và mọi asset phải hợp lệ/được khóa.
- Cảnh một nhân vật nói trực diện dùng `OnCameraDialogue`; `NativeVoiceOver` dành cho B-roll không gắn nhân vật.
- `SceneFirstFrame` là entity riêng, đúng project/scene/aspect/source snapshot và có lifecycle generate/materialize/review.
- Fal/Veo Image-to-Video bắt buộc first frame Approved/current. Không gửi identity image vuông trực tiếp và không fallback Text-to-Video.
- Sửa scene/character/asset làm invalid các output phụ thuộc theo version/hash, không sửa lịch sử.

## 9. Video generation và provider

- Server xác minh duration, ratio, resolution, audio và reference capability; submit idempotent và không tự failover model/provider.
- Worker server là nơi polling duy nhất, cache output trước khi hoàn tất và settle terminal.
- Desktop tải qua relative proxy, dùng `.part`, kiểm tra MIME/size/hash/FFprobe rồi tạo local asset.
- Kling là mặc định. BytePlus Seedance và Fal/Veo có adapter nhưng mặc định Disabled và phải rollout riêng.
- Fal Standard/Fast là endpoint riêng, không fallback. Output URL gốc không rời server.

## 10. Video ngắn

- `DirectShortVideo` có một scene, thời lượng 5–15 giây và Kling.
- Không gọi OpenAI để viết lại content và không áp policy tiếng Việt dành riêng cho video dài.
- Khi tắt audio, desktop strip toàn bộ audio khỏi output local; provider policy không làm thay đổi cấu trúc workflow.

## 11. Speech, Native Audio và Canonical Voice

Scene speech mode là `None`, `OnCameraDialogue` hoặc `NativeVoiceOver`. Project speech production policy là:

- `ProviderNativeVerified`: provider tạo Native Audio. Với video dài, desktop kiểm tra audio kỹ thuật rồi người dùng phát video, xác nhận checklist và duyệt trực tiếp; không quote/gọi ASR và không tạo `SpeechVerificationReport`.
- `CanonicalVoice`: dùng voice profile/version Approved để tạo WAV chuẩn và ghép vào video.

Quy tắc Canonical Voice:

- Voice profile version bất biến; preview phải được nghe/xác nhận trước khi approve.
- Catalog do server trả về gồm `alloy`, `ash`, `ballad`, `coral`, `echo`, `fable`, `onyx`, `nova`, `sage`, `shimmer`, `verse`, `marin`, `cedar`. Alias `female-sweet`/`male-warm` ánh xạ `shimmer`/`onyx` cho dữ liệu cũ.
- Mở/chọn modal không gọi provider. Preview TTS là request có phí, dùng project hiện hành hoặc project kỹ thuật ẩn theo user+organization làm context ownership/audit/budget. Project ẩn không xuất hiện trong list/dashboard và không sửa cấu hình project nội dung.
- Phải quote/xác nhận trước outbound; phát lại WAV đã tải trong phiên không tạo request mới.
- Content plan hướng lời tới 85–95% content duration; validator dùng biên 80–105% trước TTS. Lỗi nhịp chỉ mở repair có quote, không tự sinh lại.
- TTS qua đầy đủ credential/model/rate/budget/idempotency/output proxy. WAV được kiểm tra MIME, hash, sample rate, duration, audibility và ratio.
- Canonical Voice không gửi WAV qua ASR và không phụ thuộc WER/CER. Speech verification chỉ áp dụng cho workflow không phải `OpenAiStructuredPlan` khi feature flag độc lập được bật.
- `NativeVoiceOver` có WAV hiện hành đúng lineage đi thẳng sang tạo video nền; lệnh tạo video kiểm tra file rồi chấp nhận đúng VoiceGeneration trước outbound, không có bước duyệt WAV riêng.
- `OnCameraDialogue` dừng ở `SpeechReadyForLipSync`; chưa có engine thì không được render như đã lip-sync.
- Narrated asset mới dùng `scene-audio-sync-v3`; `v2` chỉ tương thích khi là exact approved pointer và còn khớp generation, VoiceGeneration, speech/voice snapshot và hash. Phiên bản cũ hơn bị chặn.
- Retry `NativeAudioInvalid`, repair, TTS hoặc provider lần hai là request có phí mới và cần xác nhận/idempotency phù hợp.

## 12. Render và xuất video

- Bản dựng cần tối thiểu một scene đã duyệt; lấy các scene Approved/current theo đúng thứ tự scene plan và bỏ qua scene chưa duyệt.
- Mỗi scene phải có approved render asset đúng generation/voice/speech snapshot và file khớp SHA-256.
- FFmpeg normalize, concat, mix voice/music/ambience, chèn silent track khi cần và ghi qua file tạm; FFprobe xác minh output.
- Retry render chỉ làm local, không gọi provider.
- FinalVideo được xuất MP4 nhiều lần qua file tạm/atomic replace sau khi kiểm hash; thao tác export không render hoặc gọi AI lại và không làm mất bản workspace dùng để preview.

## 13. Vietsub local-first

- `VietsubProjectId` độc lập project video. `vs.Projects` giữ registry metadata/ownership/audit; dữ liệu biên tập, media và path nằm local. Riêng thao tác **Dịch Cloud** chủ động gửi snapshot text có giới hạn vào job server, mã hóa và dọn theo retention; không upload video/audio/path.
- Workspace thuộc exact organization + owner; COPY sao chép/hash atomically, LINK phát hiện source mất/đổi.
- Playback dùng virtual HTTPS URL và HTTP Range, không lộ absolute path; mọi mutation dùng track revision.
- Cue manual/locked không bị job ghi đè. Local job có state/checkpoint/pause/resume/retry/cancel và recovery.
- OCR local chỉ chạy khi session/license/membership/role/owner hợp lệ; Viewer bị chặn.
- Panel **Thiết lập dự án** chỉ hiển thị ba tác vụ **Quét OCR**, **Dịch tiếng Việt** và **Tạo giọng Việt**; không lặp lại card video nguồn, track phụ đề, revision hay trạng thái runtime đã sẵn sàng. **Quét OCR** mở popup có video hiện tại, điều khiển phát/tua và khung chỉnh vùng subtitle cứng; quét thử phải dùng đúng timestamp đang chọn. **Dịch tiếng Việt** mở popup chọn Local hoặc Cloud. Cloud dùng readiness server độc lập Local, bấm một lần để lưu draft và tạo job; không có model picker/API key. Thiếu flag/config/quyền/ngân sách thì khóa Cloud với lý do an toàn.
- **Thiết kế phụ đề** chỉ mở khi dự án có video phát được. Người dùng có thể tua video, ẩn/hiện phụ đề, chọn chế độ vừa khung/lấp đầy, thu phóng, lật độc lập hình video trái–phải/trên–dưới (hoặc cả hai), chọn preset rồi chỉnh font trong allowlist, màu/độ trong suốt, viền, bóng, nền, căn chữ, chiều rộng và số dòng mục tiêu. Lật hình không lật chữ phụ đề hoặc đổi âm thanh/tỷ lệ video. Vị trí phụ đề được kéo trực tiếp trên video hoặc tinh chỉnh bằng phím mũi tên và lưu theo phần trăm của content box. Cùng modal có bộ trộn hai kênh để chỉnh riêng âm thanh gốc `0–100%`, giọng dịch `0–150%`, tắt/bật từng kênh và bật tự giảm âm gốc khi có tín hiệu giọng Việt. Bản nháp phụ đề/âm thanh/lật hình chỉ tác động preview; phải bấm **Lưu thay đổi** mới ghi theo project. Khi đóng lúc còn thay đổi, UI phải xác nhận bỏ bản nháp. Preview thành phẩm và adapter render phải dùng cùng cấu hình đã lưu.
- Phần đầu card **Biên tập phụ đề** đặt tiêu đề và nhóm nút trên cùng một hàng; số câu, trạng thái dịch và cảnh báo nằm ở hàng nhỏ bên dưới. Không lặp thêm nhãn PHỤ ĐỀ phía trên tiêu đề.
- Nút **Xuất video** nằm ngay cạnh **Thiết kế phụ đề** và menu **Tệp phụ đề** trên thanh công cụ biên tập, đồng thời có thêm nút ở cuối thanh công cụ Timeline. Hai nút dùng chung luồng lưu các chỉnh sửa phụ đề đang chờ trước khi mở hộp thoại chọn nơi lưu MP4; lưu lỗi thì dừng để người dùng sửa. Cả hai cùng có trạng thái **Đang xuất…**, khóa bấm lặp kể cả khi bấm xen kẽ và dùng cùng chức năng xuất với **Xuất MP4** trong thiết kế thành phẩm. Thiếu video/track hoặc đang xử lý thao tác khác thì khóa nút; nhãn xuất luôn hiển thị ở panel hẹp.
- Xuất MP4 phải lưu bản nháp hợp lệ trước, snapshot media/track/revision/style/cấu hình trộn/lật hình và timeline giọng Việt hiện hành, tạo ASS từ bản dịch rồi render local bằng FFmpeg. Khi có lật hình, áp bộ lọc lật trước bộ lọc chèn phụ đề để chữ giữ nguyên chiều. Khi timeline giọng Việt hợp lệ và kênh này không bị tắt, FFmpeg trộn nó với âm thanh gốc; tùy chọn tự giảm nền dùng voice làm sidechain và output luôn qua limiter chống vỡ tiếng. Nếu chưa tạo timeline giọng Việt thì vẫn được xuất với âm gốc theo cấu hình. Output được ghi vào tệp `.partial`, kiểm tra lại video/audio stream, kích thước và thời lượng bằng FFprobe và chỉ publish khi toàn bộ snapshot vẫn còn hiện hành. Không được ghi đè video nguồn hoặc gửi phụ đề/âm thanh ra Cloud.

### Dịch ngữ cảnh Qwen

- Nút **Dịch tiếng Việt** luôn hiện; thiếu active OCR track có cue phải yêu cầu quét OCR và không tạo job.
- Chỉ nhận source `PADDLE_OCR_LOCAL`, language `en`/`zh`, cue tồn tại và revision khớp.
- LLamaSharp/Qwen chạy trong worker x64 riêng qua IPC giới hạn; không có Cloud client, credential hoặc workflow database.
- READY phải khớp model/worker/protocol/config/backend/native fingerprint và probe runtime/English/Chinese.
- Resource warning RAM/commit cần người dùng xác nhận và snapshot theo job; không bỏ qua platform, disk, checksum/probe hoặc OOM thật.
- Output stale/invalid không apply; translation memory/cache tách theo fingerprint/profile và SRT ghi atomically.
- `VietsubLocalTranslationEnabled=false` cho tới khi model integration, benchmark và smoke desktop đạt; `Skipped` không phải pass.

### Tạo giọng Việt Piper

- Chỉ track hiện hành có revision khớp và các cue được bật tạo giọng đều có bản dịch tiếng Việt mới được tạo giọng; warning chất lượng dịch không chặn. Mặc định mọi cue được bật; người dùng có thể bỏ qua/bật lại từng câu trong menu Thao tác hoặc menu chuột phải trên timeline mà không xóa phụ đề hay đổi timing. Danh sách không còn thanh chọn hàng loạt hoặc checkbox chọn câu; API/storage vẫn hỗ trợ danh sách ID. Cue bỏ qua không gửi tới Piper và ngắt cụm đọc. Hệ thống cố gắng fit giọng câu trước đến đầu câu bỏ qua; nếu không đủ thời gian, vẫn giữ phần giọng tràn và hoàn thành tác vụ.
- Lựa chọn tạo giọng lưu tại `subtitle_cues.voice_enabled` trong SQLite schema 6; nâng từ schema 5 đặt mặc định bật cho dữ liệu cũ. Thay đổi theo batch phải khớp active track/revision, không có job local đang hoạt động và tăng revision một lần; split/duplicate kế thừa lựa chọn của câu gốc.
- Đổi lựa chọn làm timeline giọng revision cũ mất hiệu lực. Chỉ dựng lại bằng cache khi các phrase phủ đủ những câu đang bật, không chứa câu bị bỏ qua và còn khớp nội dung/cấu hình/hash. Nếu cache không đủ, UI báo **Cập nhật giọng Việt**; không phát hoặc xuất âm giọng cũ. Bỏ qua toàn bộ câu cho phép xuất phụ đề và âm gốc theo mixer. Khi đã từng có giọng nhưng lựa chọn hiện tại chưa có timeline hợp lệ, xuất MP4 phải yêu cầu cập nhật giọng hoặc tắt kênh giọng Việt.
- Piper CPU chạy trong Python worker cô lập với model/config/runtime đã pin; không nhận provider credential, URL tùy ý hoặc output path từ WebView.
- Phrase/WAV/timeline cache theo content/config/revision. File `.partial` phải qua RIFF/PCM, size và SHA-256 trước promote.
- Timeline giọng Việt là track riêng dùng chung playhead/play/pause/seek/rate với video, không dùng audio player độc lập.
- Chỉnh timing nhưng giữ nguyên cue/nội dung không được làm mất voice: trường hợp chỉ nới `end` được chuyển tiếp WAV đã xác minh sang revision mới; các thay đổi timing khác dựng lại timeline bằng FFmpeg từ phrase WAV cache, không gọi lại Piper hoặc tải model. Đổi nội dung/speaker hay cấu trúc cue vẫn làm timeline cũ mất hiệu lực.
- Hệ thống mượn khoảng trống kế tiếp và tăng tốc tối đa `1.20x`; phrase dài hơn vẫn publish ở tốc độ tối đa, giữ diagnostic và không cắt phần tiếng nói. Nếu tràn vào câu bỏ qua, có thể rút tối đa 120 ms đuôi WAV đã được bộ phân tích xác định nằm sau tín hiệu tiếng nói, giữ thêm 5 ms đệm; không sửa WAV phrase gốc. Thiếu khoảng lặng thì giữ phần tràn, lưu diagnostic `REVIEW_REQUIRED` không chặn và hoàn thành tạo giọng bình thường. Quy tắc này dùng chung cho tạo mới, thử lại và dựng lại cache, kể cả câu bắt đầu trong vùng bỏ qua.
- `VietsubLocalVoiceEnabled=true` làm workflow cài đặt/ trạng thái hiển thị. Thiếu component phải trả `NOT_INSTALLED`; chỉ trả READY sau checksum/probe và vẫn cần legal review, benchmark, nghe nghiệm thu, smoke trước production.

## 14. SePay và seat

- SePay mặc định tắt. Payment snapshot amount/account/content/expiry và xử lý webhook idempotent.
- Chỉ match giao dịch vào đúng account/code/exact amount và trạng thái; dữ liệu mơ hồ chuyển thủ công.
- Pool/seat allocation dùng khóa cạnh tranh, không vượt capacity. Replay webhook không cấp license/seat hai lần.
- Late payment, refund/chargeback và provisioning failure phải có trạng thái đối soát; không sửa ledger ad-hoc.

## 15. Dữ liệu, output và bảo mật

- Desktop chỉ đọc/ghi workflow `vf` trong giai đoạn chuyển tiếp; server là nguồn sự thật cho credential, cloud request và usage.
- Provider outbound chỉ qua HTTPS exact allowlist. Output proxy xác minh auth/ownership/scheme/host/DNS/redirect/MIME/size và không lộ signed URL.
- Path local phải chuẩn hóa, tương đối trong workspace root và chống traversal/reparse escape.
- Không log secret, Authorization, signed URL, Base64, prompt/transcript nhạy cảm hoặc absolute local path.

## 16. Update và release

- Server lưu metadata/artifact release. Setup/updater kiểm tra size, SHA-256, traversal, package root và bundle FFmpeg.
- Update bảo vệ appsettings, workspace và WebView2 user data; thay file có backup/rollback.
- Publish Release bị chặn nếu FFmpeg provenance chưa có `Approval scope: Release`.

## 17. API error và tương thích

- Lỗi nghiệp vụ dùng HTTP status phù hợp và `ApiErrorResponse` có code ổn định; không trả raw exception/provider response/secret.
- `401` từ API authenticated làm desktop xóa phiên; `403` license/role không mặc định là token hỏng.
- Lỗi polling/cache không được biến thành submit hoặc settlement mới.
- Project/output lịch sử tiếp tục đọc được theo compatibility policy; không xóa entity legacy chỉ vì không thấy call site trực tiếp.

## 18. Điều kiện nghiệm thu

- Contract/source/migration/tài liệu đồng bộ; migration idempotent và least-privilege đạt trên clone.
- Cross-user/cross-org/Viewer, thiếu rate/budget/credential/policy bị chặn trước outbound.
- Idempotency, polling restart, settlement và output proxy đạt test.
- Build/test đạt trên commit phát hành; Failed/Skipped được giải thích.
- Integration/model/provider thật có smoke trên đúng môi trường/bundle và chi phí được phê duyệt.
- Monitoring, rollback và runbook khả dụng; không có secret/dữ liệu nhạy cảm trong source/log/artifact.
