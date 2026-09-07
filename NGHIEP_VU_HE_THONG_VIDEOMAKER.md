# Nghiệp vụ và kiến trúc VideoMaker

Tài liệu này là nguồn sự thật nghiệp vụ. Trạng thái triển khai cụ thể nằm trong `TRANG_THAI_DU_AN.md`; hướng dẫn vận hành nằm trong `TRIEN_KHAI_AI_GATEWAY_TO_CHUC.md`.

## 1. Mục tiêu hệ thống

VideoMaker hỗ trợ ba nhóm công việc:

1. Tạo video dài nhiều cảnh từ chủ đề và content plan có cấu trúc.
2. Tạo video ngắn một cảnh trực tiếp từ nội dung người dùng.
3. Tạo/chỉnh phụ đề Vietsub bằng media và AI local.

AI cloud được quản trị theo organization, không theo máy. Một user có thể thuộc nhiều organization nhưng phải chọn organization hiện hành trước khi tạo project hoặc phát sinh chi phí.

## 2. Ranh giới thành phần

### Server

`TOOL-SERVER` chịu trách nhiệm:

- tài khoản, JWT, refresh rotation, session và device;
- license, lease, heartbeat và giới hạn thiết bị/phiên;
- organization, membership, role và ownership;
- provider catalog, model, credential version và pricing;
- budget reservation, settlement, usage ledger và audit;
- gọi OpenAI/Kling/BytePlus/Fal;
- polling task video, cache output và proxy download;
- SePay, organization pool/seat provisioning;
- metadata registry Vietsub;
- phát hành và cung cấp package desktop.

Server là nơi duy nhất được giải mã provider credential và gọi provider cloud.

### Desktop

`TOOL-LOCAL` chịu trách nhiệm:

- đăng nhập, duy trì phiên/license và chọn organization;
- tạo/chọn project, chỉnh nội dung và duyệt asset;
- gọi server gateway bằng JWT;
- tải output qua endpoint tương đối của server;
- lưu workspace/media, kiểm tra hash và probe;
- trim, audio mix, subtitle và render FFmpeg;
- project/editor/job/media/OCR local của Vietsub.

Desktop không được có màn nhập key, provider secret store, direct provider HTTP client hoặc fallback BYOK.

### Contracts và dữ liệu

- `TOOL-SHARED.Contracts` là hợp đồng công khai giữa server/desktop.
- SQL Server chứa auth, governance, workflow và Vietsub registry.
- Workspace cục bộ chứa media và artifact do desktop tạo.
- Trong giai đoạn chuyển tiếp desktop còn đọc/ghi schema workflow `vf`; server vẫn là nguồn sự thật cho credential, request cloud và usage.

## 3. Người dùng và quyền

| Role | Thành viên | Budget/usage | Credential | Phát sinh AI |
|---|---:|---:|---:|---:|
| `Owner` | Có | Có | Có | Có |
| `OrganizationAdmin` | Có | Có | Có | Có |
| `BillingManager` | Không | Có | Không | Có |
| `Member` | Không | Không | Không | Có |
| `Viewer` | Không | Không | Không | Không |

Quy tắc:

- Global Admin tạo organization, quản lý provider/model/rate, license plan, pool và release.
- Owner quản lý Owner; không được xóa/hạ cấp/suspend Owner Active cuối cùng.
- OrganizationAdmin không được cấp hoặc thu hồi Owner.
- BillingManager xem budget/usage nhưng không quản lý member/credential.
- Viewer chỉ đọc metadata được cấp phép; mọi thao tác có khả năng phát sinh chi phí phải bị chặn trước outbound call.

## 4. Xác thực và license

- Access token phải mang user, session và device claim.
- Mỗi API bảo vệ phải kiểm tra session/device ở server, không chỉ tin thời hạn JWT.
- Refresh token lưu dạng hash, rotate khi dùng và thu hồi cả token family/session khi phát hiện reuse.
- Login mới trên cùng device thay thế session cũ theo policy.
- License gắn user, có thời hạn, số device tối đa và lease cho desktop.
- Desktop heartbeat để duy trì lease; lease hết hạn hoặc device bị revoke phải khóa luồng được bảo vệ.
- Logout có thể thu hồi session hiện hành hoặc toàn bộ session.

## 5. Organization AI gateway

Mỗi request cloud phải truy vết được:

- organization;
- user và device/session;
- project;
- provider/model;
- provider request;
- credential version;
- rate snapshot;
- reservation, usage và actual cost;
- idempotency key/request hash.

Thứ tự bắt buộc trước outbound call:

1. JWT hợp lệ.
2. Session, user và device Active.
3. License lease hợp lệ.
4. Membership Active và role được phép.
5. Project thuộc đúng organization và owner/user theo policy.
6. Payload và idempotency không xung đột.
7. Provider/model/policy/capability hợp lệ.
8. Credential đúng version và còn dùng được.
9. Rate Active đầy đủ.
10. Reserve budget organization/member thành công.

Không được đổi thứ tự theo cách khiến provider được gọi trước khi quyền, rate hoặc budget được xác nhận.

## 6. Credential

- Mỗi organization có tối đa một credential `Active` cho mỗi provider.
- Credential mới được test bằng request không tạo chi phí trước khi ghi Active.
- Payload được mã hóa bằng ASP.NET Core Data Protection; response chỉ trả hint, version, status và timestamp an toàn.
- Rotation dùng `Active -> Retiring -> Revoked`.
- Request mới chỉ dùng Active; task đã submit tiếp tục resolve credential version đã snapshot, kể cả khi Retiring.
- Không log key, Authorization header, encrypted payload hoặc request body chứa secret.

## 7. Pricing và budget

- Global Admin nhập rate từ hợp đồng/dashboard chính thức; bootstrap không seed giá.
- Thiếu rate bắt buộc trả `pricing_not_configured` trước outbound.
- Budget tháng organization bằng `0` nghĩa là khóa AI.
- Member có thể có limit riêng; cả organization và member limit đều được kiểm tra.
- Reservation/settlement/release dùng transaction `Serializable` và operation key idempotent.
- `RateSnapshotJson` là nguồn quyết toán của request; thay giá mới không sửa chi phí lịch sử.
- Nếu provider không trả usage đáng tin cậy, dùng công thức estimate/rate snapshot đã khóa theo policy của model; không ghi actual bằng 0 chỉ vì thiếu usage.
- Lỗi trước khi provider có thể nhận request thì release. Trạng thái submit không chắc chắn phải reconcile, không release mù và submit lại tự động.

## 8. Vòng đời project video

Project video có hai cấu trúc:

- `OpenAiStructuredPlan`: video dài nhiều cảnh.
- `DirectShortVideo`: video ngắn một cảnh.

Project gắn `OrganizationId` và `CreatedByUserId`. Danh sách desktop phải lọc theo organization đang chọn; đổi organization đóng/chuyển context an toàn và không để request đang chạy đổi tenant.

## 9. Video dài

### 9.1 Content plan

- Người dùng nhập topic, tỷ lệ, thời lượng, policy speech và voice tùy chọn.
- Luồng hiện hành dùng `vi-VN`; các trường con người đọc phải đạt policy tiếng Việt.
- OpenAI dùng Responses API, JSON Schema, `store=false` và safety identifier dạng hash.
- Output gồm script, character, scene, speech intent và project asset có key ổn định.
- Scene có content duration và generation duration; provider duration có thể dài hơn phần nội dung rồi desktop trim tail.

Nếu plan sai ngôn ngữ:

- vẫn lưu provider request, usage và chi phí đã phát sinh;
- trả lỗi `422` có field path/reason an toàn và request ID;
- replay cùng idempotency trả lại lỗi cũ, không gọi provider lần hai;
- cho tối đa một lượt repair sau quote/xác nhận;
- repair không được đổi character key, asset key, scene count hoặc cấu trúc đã khóa.

### 9.2 Character và project asset

- Character có version, visual identity, wardrobe, immutable traits và forbidden changes.
- Character reference AI được tạo server-side, tải qua proxy, materialize vào workspace rồi duyệt.
- Chỉ primary reference đã duyệt và còn current được dùng làm đầu vào provider.
- Project asset gồm `Background`, `Prop`, `Item`, có canonical description, version và trạng thái Draft/Locked.
- Scene assignment phải hợp lệ và được xác nhận trước first-frame/video.
- Sửa scene/character/asset làm invalid các snapshot/output phụ thuộc theo version/hash, không sửa ngầm output lịch sử.

### 9.3 Scene first-frame

- First-frame là entity riêng, không dùng character reference vuông làm ảnh video.
- Ảnh phải đúng project/scene, aspect ratio, kích thước, MIME, dung lượng và source snapshot.
- Vòng đời: generate -> materialize -> pending review -> approve/reject; version cũ có thể superseded/invalidated.
- Fal/Veo Image-to-Video bắt buộc approved current first-frame trước submit.
- First-frame stale vì scene prompt, plan, character reference, asset hoặc aspect ratio thay đổi phải bị chặn.

### 9.4 Video generation

- Policy project snapshot provider/model tại thời điểm tạo để thay policy organization không đổi request cũ.
- `Default` dùng cho video ngắn; `LongForm` dùng cho video dài.
- Server xác minh exact duration, ratio, resolution, Native Audio và reference capability.
- Submit idempotent; không fallback model/provider hoặc Text-to-Video/Image-to-Video âm thầm.
- Worker server là nơi polling duy nhất, cache output trước khi đánh dấu hoàn tất và settle terminal.
- Desktop tải file bằng proxy, kiểm tra MIME/size/hash, probe, trim/strip audio và tạo MediaAsset/VideoGeneration.
- Clip Native Audio cần nghe/duyệt; clip không speech vẫn phải qua kiểm tra media phù hợp.

## 10. Video ngắn

- Người dùng nhập nội dung trực tiếp, tỷ lệ, thời lượng 5–15 giây và lựa chọn audio.
- Desktop tạo `DirectShortVideo` với một scene và submit Kling; không gọi OpenAI content.
- Màn hình mở lại project phải dựa vào `workflowStructureType`, không dựa vào React state tạm.
- Nếu tắt audio, Kling vẫn dùng variant/rate theo policy hiện hành nhưng desktop strip toàn bộ audio khỏi output.
- Fal/BytePlus/LongForm policy không được làm thay đổi luồng video ngắn.

## 11. Provider video

### Kling

- Hỗ trợ Text-to-Video và first-frame/reference theo capability model.
- Worker polling task ID; desktop không gọi status provider trực tiếp.
- Output URL gốc không rời server.

### BytePlus/Seedance

- Client/policy tồn tại nhưng provider/model mặc định Disabled.
- Billing dựa trên output token/rate metadata đúng model.
- Không bật trước credential, rate, budget và smoke test.

### Fal/Veo

- Dùng Queue API và `Authorization: Key`.
- Standard/Fast là hai endpoint riêng, không fallback.
- Image-to-Video, 720p, 16:9/9:16, exact duration 4/6/8 giây và Native Audio.
- Không lưu status/result/signed output URL trong log công khai.
- Output chỉ được tải từ allowlist tối thiểu, qua SSRF/DNS/redirect/MIME/size guard rồi cache server.

## 12. Speech và audio

Scene speech mode:

- `None`.
- `OnCameraDialogue`.
- `NativeVoiceOver`.

Project speech production policy:

- `ProviderNativeVerified`: tương thích mặc định và dùng Native Audio provider. Trong video dài, desktop kiểm tra kỹ thuật rồi người dùng nghe/checklist/duyệt trực tiếp, không chạy ASR. Hạ tầng speech verification chỉ còn áp dụng cho workflow không phải `OpenAiStructuredPlan` khi feature flag được bật.
- `CanonicalVoice`: dùng voice profile/version đã duyệt để tạo WAV chuẩn.

Quy tắc:

- Voice profile version bất biến; phải tạo preview và người dùng nghe trước khi approve.
- Catalog giọng Canonical Voice do server công bố cho desktop và hiện gồm 13 giọng dựng sẵn OpenAI: `alloy`, `ash`, `ballad`, `coral`, `echo`, `fable`, `onyx`, `nova`, `sage`, `shimmer`, `verse`, `marin`, `cedar`. Frontend không tự quyết định allowlist; mã cũ `female-sweet`/`male-warm` tiếp tục ánh xạ sang `shimmer`/`onyx` để đọc project lịch sử.
- Modal chỉ thay đổi lựa chọn cục bộ; thao tác mở/chọn không được gọi provider hoặc giữ budget. Nút nghe thử khả dụng cả trong form tạo project. Catalog preview là request có phí nên luôn phải có project làm ngữ cảnh ownership, organization, quyền, budget và hạch toán: dùng project đang chọn nếu có; nếu chưa có project nội dung, server tạo hoặc tái sử dụng một project kỹ thuật ẩn xác định theo user và organization sau khi đã kiểm tra JWT, session/device, license, membership và role. Project kỹ thuật dùng `ProviderNativeVerified`, không mang cấu hình giọng nội dung và bị loại khỏi danh sách/dashboard của desktop. Sau báo giá và xác nhận mới được reserve/outbound; server kiểm tra lại ownership của context project trước outbound, WAV tải qua endpoint server, được desktop kiểm tra rồi mới phát. Phát lại mẫu đã tải không tạo `ProviderRequest` mới. Voice profile draft vẫn có preview riêng bắt buộc trước khi approve.
- Scene voice request khóa exact speech text/hash, voice snapshot và plan version.
- Với content plan `CanonicalVoice`, lời có speech hướng tới 85–95% thời lượng nội dung cảnh. Bộ ước lượng tiếng Việt tính cụm đọc và khoảng nghỉ dấu câu; biên 80–105% dùng để phát hiện output OpenAI quá ngắn/dài trước TTS, không thay thế thời lượng WAV thực tế.
- Content plan sai nhịp có thể dùng chung lượt repair có báo giá/xác nhận với lỗi ngôn ngữ. Không tự gọi repair, TTS hoặc provider lần hai; request đã tiêu thụ vẫn được quyết toán theo rate snapshot và idempotency hiện hành.
- TTS phải có rate, reserve, usage và proxy như request cloud khác. ASR chỉ áp dụng cho `ProviderNativeVerified` của workflow không phải video dài khi được bật và cũng phải đi qua đầy đủ các chốt chi phí này.
- WAV Canonical được kiểm tra MIME, hash, sample rate, duration, mức nghe được và tỷ lệ thời lượng so với cảnh trước khi được dùng cho video.
- Canonical Voice không gửi WAV qua transcription, không tạo WER/CER và không phụ thuộc word timing ASR. Với `NativeVoiceOver`, WAV hiện hành đúng scene plan/speech hash/voice profile snapshot mở thẳng bước tạo video; lệnh tạo video kiểm tra kỹ thuật file cục bộ rồi tự chấp nhận đúng VoiceGeneration trước outbound, không có bước duyệt WAV riêng. `OnCameraDialogue` vẫn cần duyệt trước khi chuyển sang trạng thái chờ lip-sync.
- Với video dài `ProviderNativeVerified`, không tạo `SpeechVerificationReport`: clip phải có audio nghe được, người dùng phải phát video và xác nhận checklist trước khi duyệt. Với workflow khác có bật ASR, report vẫn gắn đúng source asset/hash và so expected transcript bằng WER/CER/required-term recall; `Passed` được đi tiếp, `NeedsReview` cần lý do audit và `Failed` không được override.
- Audio quá dài/ngắn không được sửa âm thầm ngoài giới hạn tempo đã cấu hình.
- Dashboard hiển thị nhịp ước tính trước TTS và tỷ lệ WAV thực tế sau TTS. WAV ngắn hơn mục tiêu biên tập nhưng vẫn qua kiểm tra kỹ thuật chỉ hiện cảnh báo; không chặn tạo video và không yêu cầu tạo lại giọng.
- `NativeVoiceOver` Canonical dùng toàn bộ WAV đã kiểm tra kỹ thuật, chỉ điều chỉnh tempo trong giới hạn và pad đến thời lượng cảnh. Narrated asset mới phải ghi policy đồng bộ hiện hành để output cũ tạo theo cơ chế ASR timing không bị tái sử dụng nhầm. Render cuối chỉ được tương thích với `scene-audio-sync-v2` khi đó là exact approved render pointer và vẫn khớp generation, VoiceGeneration, speech hash, voice snapshot, trạng thái, audibility và hash file; không chấp nhận phiên bản cũ hơn.
- `NativeVoiceOver` Canonical Voice thay native speech hoặc mix ambience đã xác minh, có ducking/limiter/loudness normalization.
- `OnCameraDialogue` Canonical Voice chỉ đạt `SpeechReadyForLipSync`; không render như đã lip-sync khi chưa có engine.
- Sửa speech hoặc voice version phải hủy approved pointer/output phụ thuộc.

## 13. Render cục bộ

- Một bản dựng cần tối thiểu một scene đã duyệt. Người dùng có thể dựng từ bất kỳ số lượng scene đã duyệt nào của scene plan hiện hành; scene chưa duyệt bị bỏ qua và không chặn bản dựng.
- Mỗi scene được đưa vào bản dựng phải có approved render asset đúng generation/voice/speech snapshot.
- File trên disk phải khớp metadata/hash trong database.
- Render dùng FFmpeg để normalize scene, concat, mix voice/music/scene audio, burn subtitle khi có và ghi output qua file tạm.
- Timeline có thể gồm cả scene có audio và scene im lặng; renderer phải chèn silent track chuẩn cho scene im lặng khi output chung có audio để concat không lệch stream/thời lượng.
- FFprobe kiểm tra stream, duration, resolution và audio yêu cầu trước khi công bố FinalVideo.
- Retry render chỉ làm lại local, không tạo provider request mới.
- FinalVideo hợp lệ được phép xuất nhiều lần bằng hộp thoại lưu file cục bộ. Trước khi sao chép phải kiểm tra file workspace còn khớp SHA-256 đã lưu, ghi qua file tạm rồi mới thay thế đích; xuất file không render lại và không phát sinh request provider.
- Sau khi xuất, giữ bản dựng gốc trong workspace làm nguồn preview tin cậy; không đưa đường dẫn tuyệt đối bên ngoài workspace vào URL WebView.

## 14. Vietsub

- `VietsubProjectId` độc lập với project video.
- Server chỉ giữ metadata registry/ownership/audit; không nhận local path, media hoặc subtitle content trong API danh sách.
- Workspace local thuộc exact organization + owner, có lock và recovery.
- Media `COPY` được sao chép/hash nguyên tử; `LINK` phải kiểm tra file còn tồn tại và không đổi hash.
- Playback dùng virtual HTTPS URL, hỗ trợ Range, không lộ absolute path và luôn kiểm tra active project/context/source hash.
- Subtitle track có revision; edit dùng expected revision để chặn stale write.
- Manual original/translation lock không được job AI ghi đè.
- Local job có state machine, step, event, checkpoint, pause/resume/retry/cancel và startup interruption recovery.
- OCR local chỉ chạy khi session/license/membership/role/owner còn hợp lệ; Viewer bị chặn.
- Dịch local/cloud, STT, voice và export video chỉ được coi là có khi executor, model/runtime, UI và test hoàn chỉnh tồn tại; job type placeholder không phải tính năng đã triển khai.

## 15. SePay và phân bổ organization

- Người dùng chọn license offer, server tạo/reuse payment bằng idempotency key.
- Payment snapshot số tiền/tài khoản/nội dung chuyển khoản và có hạn dùng.
- Webhook chỉ nhận giao dịch vào, đúng tài khoản, transfer code, exact amount và chưa được xử lý.
- Provider transaction ID và fulfillment phải idempotent dưới transaction `Serializable`.
- Plan có thể map vào organization pool; checkout reserve seat trước, payment thành công activate membership/license.
- Replay webhook không gia hạn hai lần. Fulfillment lỗi được đánh dấu và chỉ retry có kiểm soát.
- Webhook response/admin projection không lộ dữ liệu nhạy cảm không cần thiết.

## 16. Update và release

- Server lưu metadata và artifact release, cung cấp manifest/download có kiểm tra quyền phù hợp.
- Setup và updater kiểm tra size, SHA-256, path traversal, package root và bundle FFmpeg.
- Update bảo vệ `appsettings.json`, `appsettings.user.json`, workspace và WebView2 user data theo policy.
- Thay file phải có backup/rollback; package lỗi không được để cài đặt nửa chừng.
- Publish Release bị chặn nếu FFmpeg provenance chưa có `Approval scope: Release`.

## 17. Mã lỗi và API response

- Lỗi nghiệp vụ dùng HTTP status phù hợp và `ApiErrorResponse` có `code`, `message`, `errors`, `traceId` khi cần.
- Mã lỗi là contract ổn định vì desktop dùng để điều hướng/hiển thị/retry.
- `401` từ API authenticated làm desktop xóa phiên; `403` license/role không mặc định đồng nghĩa token hỏng.
- Không trả raw exception, provider response, prompt nhạy cảm, key hoặc URL gốc.
- Lỗi tạm thời của polling/output cache không được biến thành submit mới hoặc settle trùng.

## 18. Điều kiện nghiệm thu chung

- Không có provider credential trong desktop, DOM, log hoặc response.
- Cross-user/cross-organization/Viewer bị chặn trước outbound và trước đọc artifact riêng tư.
- Cùng idempotency + payload trả kết quả cũ; cùng key + payload khác trả conflict.
- Thiếu pricing/budget/credential/model/policy dừng trước outbound.
- Worker tiếp tục và quyết toán đúng khi desktop đóng/restart.
- Output proxy chặn host/IP/redirect/MIME/size không hợp lệ.
- Migration idempotent và least-privilege được kiểm tra trên database clone.
- Build/test tự động đạt trên commit phát hành; paid smoke test chỉ chạy ở staging được phê duyệt.
- Source cũ và project đã hoàn tất tiếp tục đọc được theo compatibility policy.
