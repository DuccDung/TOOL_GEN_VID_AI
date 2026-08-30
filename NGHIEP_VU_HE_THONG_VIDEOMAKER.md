# Nghiệp vụ hệ thống VideoMaker 4.0

> Trạng thái hiện tại: AI Gateway tập trung theo tổ chức. Tài liệu BYOK 3.0 đã hết hiệu lực. Source hỗ trợ policy video bất biến theo project, Kling và BytePlus Seedance dưới cùng gateway; BytePlus/Seedance được seed disabled và chỉ được bật theo tổ chức sau migration, credential, rate và smoke test có phí. Luồng mặc định hiện vẫn là **Kling Native Audio 720p** cho tới khi quản trị viên chủ động chọn policy khác. Desktop bắt buộc preview/nghe/duyệt từng clip, chỉ render `SceneVideo` của đúng `ApprovedGenerationId`; TTS/WAV được giữ tương thích nhưng không fallback ngầm.

## 1. Mục tiêu

VideoMaker hỗ trợ người dùng nhập chủ đề, tự sinh content plan/kịch bản/prompt bằng OpenAI, sinh clip theo từng cảnh bằng provider video trong snapshot của project và tải clip qua server về workspace để tiếp tục quy trình dựng video.

Doanh nghiệp quản lý AI theo mô hình:

- Một tổ chức dùng một bộ credential đang hoạt động cho mỗi provider.
- Nhiều người dùng có thể thuộc cùng tổ chức; một người dùng có thể thuộc nhiều tổ chức.
- Không phát API key cho desktop hoặc từng thành viên.
- Mọi request AI đi qua server, được kiểm tra quyền và ngân sách trước khi gửi đến provider.
- Chi phí được quy về tổ chức, thành viên, dự án, request, model và phiên bản credential.

## 2. Thành phần và trách nhiệm

### VideoMaker Server

- Xác thực JWT, session và device claim.
- Kiểm tra license và lease thiết bị.
- Quản lý tổ chức, thành viên và vai trò.
- Mã hóa, rotate và thu hồi credential OpenAI/Kling/BytePlus.
- Lưu bảng giá nội bộ, giữ ngân sách trước request và quyết toán sau request.
- Gọi OpenAI Responses/Image API và provider video Kling hoặc BytePlus theo policy.
- Lưu ảnh sinh tạm thời tối đa khoảng 24 giờ, cung cấp API tải binary có xác thực và dọn payload hết hạn.
- Polling video đa provider nền, kể cả khi desktop đã đóng; worker là polling owner duy nhất.
- Tải ngay output có URL ngắn hạn vào cache server, ghi hash/MIME/size rồi proxy về desktop; không trả URL provider trực tiếp.
- Ghi audit log và usage ledger.
- Cung cấp Admin Console **Tổ chức & AI** để quản trị organization, member, budget/usage, credential, pricing và audit mà không trả secret về trình duyệt; trang **Cách tính chi phí** giải thích reservation/settlement và minh họa bằng rate Active hiện hành.

### VideoMaker Desktop

- Đăng nhập, heartbeat license và chọn tổ chức.
- Tạo/quản lý dự án và workspace.
- Hiển thị storyboard theo kế hoạch cảnh hiện hành: lời đọc, mô tả hình ảnh, prompt, thời lượng, trạng thái và preview clip cục bộ.
- Hiển thị hồ sơ nhân vật do OpenAI tách từ content plan; cho phép chỉnh hồ sơ nháp, chọn ảnh tham chiếu JPEG/PNG hoặc tạo/sinh lại ảnh chuẩn bằng GPT-Image-2, xem trước và chỉ khóa nhân vật khi người dùng xác nhận.
- Cho phép sửa cảnh chưa gửi provider và chọn một hoặc nhiều cảnh để tạo video; cảnh có request đang hoạt động hoặc đã hoàn tất không bị âm thầm đổi prompt.
- Kiểm tra tính toàn vẹn và phiên bản FFmpeg/FFprobe trước outbound Kling. Khi bundle thiếu/hỏng, cho phép sửa chữa bằng đúng package VideoMaker cùng version/build qua Updater có backup/rollback; desktop không tải FFmpeg trực tiếp từ nguồn công cộng.
- Gửi request nghiệp vụ đến AI Gateway bằng JWT.
- Không có màn hình nhập API key, không có HTTP client gọi trực tiếp OpenAI/Kling và không lưu provider key.
- Khi nâng cấp từ BYOK, xóa đúng hai file credential cũ `provider-secrets.bin` và `provider-secrets.bin.tmp`.

### SQL Server

- Schema `auth`: tài khoản, session, license và Data Protection keys.
- Schema `ai`: tổ chức, thành viên, credential tổ chức, kỳ ngân sách, reservation, usage ledger và audit.
- Schema `vf`: dự án, cảnh, provider catalog, request log và dữ liệu sản xuất video.

## 3. Vai trò và quyền

| Vai trò | Thành viên | Ngân sách/usage | Credential | Dùng AI |
|---|---:|---:|---:|---:|
| `Owner` | Có | Có | Có | Có |
| `OrganizationAdmin` | Có | Có | Có | Có |
| `BillingManager` | Không | Có | Không | Có |
| `Member` | Không | Không | Không | Có |
| `Viewer` | Không | Không | Không | Không |

Quy tắc bổ sung:

- Chỉ Global Admin của hệ thống được tạo tổ chức mới.
- Chỉ Owner được gán vai trò Owner hoặc thay đổi một Owner hiện hữu.
- Không được hạ vai trò/khóa/xóa Owner cuối cùng đang hoạt động.
- Member bị `Suspended` hoặc `Removed` mất quyền ngay ở request tiếp theo.
- Viewer chỉ xem trạng thái/cấu hình không bí mật; không được phát sinh chi phí AI.

## 4. Tổ chức và dự án

- Người dùng desktop phải chọn tổ chức hiện hành.
- Dự án mới được gắn `OrganizationId` và `CreatedByUserId` ngay khi tạo.
- Danh sách dự án trên desktop được lọc theo tổ chức đang chọn.
- Server luôn xác minh dự án thuộc đúng user và đúng tổ chức; không tin hoàn toàn dữ liệu từ desktop.
- Dự án cũ được migration gắn vào tổ chức `legacy-default` khi có thể xác định chủ sở hữu.

## 5. Credential OpenAI/Kling/BytePlus

### Tạo hoặc rotate

1. Owner/OrganizationAdmin gửi credential qua HTTPS đến server.
2. Server chỉ chấp nhận provider và host trong allowlist cố định.
3. Server thử credential với provider trước khi thay đổi dữ liệu.
4. Nếu thử thất bại, credential cũ vẫn nguyên vẹn.
5. Nếu thành công, credential hiện tại chuyển sang `Retiring`; credential mới được mã hóa và trở thành `Active` với version tăng dần.
6. Request mới dùng version `Active`; request Kling đang chạy tiếp tục dùng đúng version `Retiring` đã ghi trên request.
7. Worker chỉ chuyển credential `Retiring` sang `Revoked` và xóa payload mã hóa sau thời gian an toàn và khi không còn request đang chạy.

API không bao giờ trả lại key. Response chỉ có trạng thái, version và bốn ký tự cuối đã che.

## 6. Đơn giá và ngân sách

### Đơn giá

- Global Admin cấu hình đơn giá theo model và `UsageType`.
- Mỗi model OpenAI, gồm model Text và `gpt-image-2`, cần hai rate riêng: `InputToken` và `OutputToken`; hỗ trợ đơn vị `Token`, `1KTokens`, `MillionTokens`. Không dùng rate model Text để tính ảnh.
- Kling cần `VideoSecond` với đơn vị `Second`; luồng hiện tại chỉ nhận rate có metadata `resolution=720p` và `nativeAudio=true`.
- BytePlus Seedance cần `OutputToken`; rate thuộc đúng model và dùng đơn vị token được Global Admin nhập từ hợp đồng/dashboard hiện hành. Estimate dùng capability 720p/24fps, settlement dùng actual `usage.completion_tokens` và rate snapshot của request.
- Hệ thống không tự giả định giá provider. Thiếu rate làm request dừng với `pricing_not_configured` trước khi gọi provider.
- Mỗi request lưu `RateSnapshotJson`; thay đổi bảng giá sau đó không làm đổi chi phí của request đang chạy.

### Giữ và quyết toán ngân sách

1. Server ước tính chi phí theo rate snapshot.
2. Transaction `Serializable` khóa kỳ ngân sách tháng và kiểm tra:
   - `ActualCost + ReservedCost + EstimatedCost <= HardLimit` của tổ chức;
   - hạn mức tháng của thành viên, nếu có.
3. Nếu đủ, tạo reservation và ledger `Reservation` trước khi gọi provider.
4. Request thành công: chuyển reservation sang `Settled`, giảm reserved, tăng actual và ghi ledger `Actual`/`Release`.
5. Request thất bại trước khi provider hoàn thành: chuyển sang `Released` và trả lại ngân sách giữ.
6. Worker reconciliation xử lý reservation quá hạn hoặc trường hợp provider đã thành công nhưng bước quyết toán tạm thời lỗi.

Ngân sách `0` có nghĩa là khóa phát sinh AI, không phải không giới hạn.

## 7. Luồng tạo nội dung OpenAI

1. Desktop gửi `projectId`, `idempotencyKey`, `organizationId` đến `POST /api/generation/content`.
2. Server kiểm tra session, device, license lease, membership, vai trò và quyền sở hữu dự án.
3. Server kiểm tra idempotency theo tổ chức và hash nội dung request.
4. Server chọn model Text đang bật, credential Active và hai rate token.
5. Server giữ ngân sách.
6. Server gọi `POST /v1/responses` với JSON Schema, `store=false`, giới hạn output và `safety_identifier` là hash ổn định của user ID.
7. Server đọc structured output cùng `usage.input_tokens`/`usage.output_tokens`, tính actual cost theo rate snapshot và quyết toán.
8. Kết quả content plan gồm hồ sơ nhân vật, `character_key` của từng cảnh và prompt được trả về desktop, version hóa rồi ghi vào dữ liệu dự án.
9. Desktop hiển thị hồ sơ nhân vật và storyboard để người dùng kiểm tra, chọn ảnh tham chiếu, khóa nhân vật và sửa các cảnh chưa gửi provider trước khi phát sinh chi phí Kling.

### 7.1. Luồng tạo ảnh chuẩn nhân vật GPT-Image-2

1. Người dùng bấm **Tạo ảnh bằng AI** hoặc **Sinh lại ảnh** khi nhân vật còn `Draft`; desktop chỉ gửi `projectId`, `characterId`, `organizationId` và idempotency key, không gửi prompt tùy ý.
2. Server xác minh JWT, session, device, license, membership, vai trò, quyền sở hữu project và quan hệ character–project. `Viewer`, truy cập chéo tổ chức và nhân vật đã khóa bị chặn trước outbound call.
3. Server tự dựng prompt từ hồ sơ, trang phục, đặc điểm bất biến và điều cấm thay đổi đã lưu; request log chỉ giữ hash/template/options, không giữ prompt đầy đủ hoặc Base64.
4. Server resolve đúng model Image `openai/gpt-image-2`, credential Active, hai rate riêng của model ảnh và budget; sau đó tạo reservation cùng `ProviderRequest` trong luồng idempotent.
5. Server gọi `POST /v1/images/generations` với một ảnh, `1024x1024`, quality `medium`, output PNG; ảnh Base64 được giải mã, kiểm tra chữ ký, kích thước tối đa 10 MB, chiều rộng/cao và SHA-256.
6. Binary được lưu ở `vf.GeneratedImageOutputs` với hạn dùng khoảng 24 giờ, không nằm trong `ProviderRequests.ResponseJson`. Retry cùng key trả lại cùng ảnh còn hạn và không gọi/tính phí lần hai.
7. Desktop tải qua `/api/generation/character-images/{providerRequestId}/content`; server xác thực lại user/tổ chức/project/character và không trả URL provider.
8. Desktop kiểm tra metadata, SHA-256 và PNG, ghi file `.part` rồi đổi tên nguyên tử; tạo `MediaAsset` loại `CharacterReference`, tạo reference primary mới và hạ primary cũ.
9. Tạo ảnh không tự khóa nhân vật. Nhân vật tiếp tục ở `Draft` để người dùng xem, sinh lại, thay ảnh hoặc bấm **Khóa nhân vật cho các cảnh**; sau khi khóa, Kling tiếp tục dùng reference primary theo luồng hiện tại.

## 8. Luồng sinh video đa provider

1. Khi project tạo video lần đầu, server snapshot provider/model, policy version, resolution và Native Audio từ policy tổ chức. Snapshot không tự đổi khi quản trị viên đổi policy sau đó.
2. Desktop chỉ gửi project, scene, version, idempotency key và ảnh reference đã duyệt; không gửi provider/model/prompt/thời lượng làm nguồn sự thật.
3. Server kiểm tra capability, credential, rate và budget trước outbound, sau đó route qua Kling adapter hoặc BytePlus Seedance adapter. Không fallback giữa provider.
4. Worker đa provider polling task. Khi hoàn tất, server kiểm tra output host theo provider, chống SSRF/DNS rebinding/redirect sai host, giới hạn MIME/dung lượng, tải vào cache và chỉ công bố URL proxy tương đối.
5. Task `Expired` hoặc vượt giới hạn polling là terminal; desktop cho phép attempt mới thay vì chờ vô hạn.
6. Với BytePlus, chỉ ảnh nhân vật do OpenAI trong hệ thống tạo, đã thành primary và được duyệt mới được dùng; ảnh tải lên/người thật bị chặn trước outbound.

### 8.1. Compatibility Kling và Native Audio

1. Người dùng chọn một hoặc nhiều cảnh trên storyboard; với cảnh có nhân vật, desktop chỉ cho tạo clip sau khi hồ sơ đã khóa và có ảnh tham chiếu chính được duyệt.
2. Desktop đọc ảnh từ workspace, kiểm tra dung lượng và SHA-256 rồi gửi ảnh cùng ID tham chiếu, scene, thời lượng, tỷ lệ, độ phân giải, `organizationId` và idempotency key. Dữ liệu Base64 không được ghi vào request log.
3. Server xác minh lại quyền sở hữu project/scene, prompt version, character mapping, trạng thái nhân vật, ID ảnh, MIME, dung lượng và SHA-256 theo `MediaAsset`; dữ liệu client không được dùng để thay thế hồ sơ đã duyệt trên server.
4. Server ghép prompt hiệu lực từ identity, trang phục, đặc điểm bất biến, điều cấm thay đổi và prompt của cảnh. Với model Kling 3.0 thường, ảnh chuẩn được gửi theo luồng image-to-video làm first frame; model Omni dùng element reference khi model đó được quản trị viên bật và cấu hình rate.
5. Server kiểm tra rate, ngân sách, giữ chi phí ước tính, gửi task Kling và ghi external task ID cùng credential version, character version, reference ID và hash.
6. Worker server polling các task đến hạn. Desktop cũng có thể hỏi trạng thái nhưng không chịu trách nhiệm duy nhất cho polling.
7. Khi Kling hoàn tất, server quyết toán theo reported cost nếu có; nếu không dùng estimated cost đã khóa.
8. Desktop nhận URL tương đối của server: `/api/generation/kling/videos/{providerRequestId}/content`.
9. Proxy xác thực lại user/license/tổ chức, chỉ cho HTTPS và host thuộc allowlist của provider, kiểm tra DNS chống SSRF, giới hạn redirect, MIME, dung lượng file và tổng dung lượng cache theo cấu hình.
10. Clip tải xong được hiển thị ngay tại card của cảnh qua virtual media host cục bộ; cảnh chưa có clip dùng placeholder theo theme và không tự gọi thêm provider ảnh.

### 8.2. Nghiệp vụ âm thanh của clip và video cuối

Luồng sản phẩm hiện tại chỉ dùng âm thanh được Kling sinh trực tiếp cùng clip:

1. Mỗi cảnh có một `SpeechMode`: `None`, `OnCameraDialogue` hoặc `NativeVoiceOver`.
2. OpenAI content planner trả riêng `spoken_text`, speaker, voice style, ambience và sound effects; `visual_prompt` không được lặp lại lời nói.
3. Server đọc dữ liệu scene đã lưu, không tin prompt lời nói do desktop tự gửi, rồi dựng prompt Kling theo template có version. Prompt bắt buộc nêu một người nói, nguyên văn câu cần nói, ngôn ngữ, phong cách giọng, lip-sync, ambience và SFX.
4. Lời nói được giới hạn theo thời lượng: tối đa 8 từ cho clip 3–5 giây, 18 từ cho 6–10 giây và 28 từ cho 11–15 giây. Vượt giới hạn phải chặn trước outbound, không tự cắt lời.
5. Request Kling luôn dùng `720p`, `NativeAudio = true` và `multi_shot = false`. Rate Active phải có metadata đúng biến thể `720p + nativeAudio`; thiếu rate/budget dừng trước outbound.
6. Desktop tải raw `SceneVideo` qua proxy có xác thực, kiểm tra video bằng FFprobe và đo audio bằng `AudioQualityValidator`. Metadata chỉ lưu speech hash, mode và thống kê audio; không log nguyên văn lời hoặc provider URL.
7. Clip thiếu audio stream hoặc gần như im lặng chuyển `NativeAudioInvalid`, không được duyệt và có thể sửa prompt/tạo lại bằng provider request mới.
8. Clip có audio nghe được chuyển `AudioReviewRequired`. Người dùng phải phát preview, đối chiếu lời, người nói, phát âm, khẩu hình, ambience và SFX rồi bấm **Duyệt hình và âm thanh**.
9. Chỉ khi duyệt, scene mới có `ApprovedGenerationId` và trạng thái `Approved`; project chỉ `ReadyToRender` khi toàn bộ scene đã duyệt.
10. Workflow mặc định không gọi OpenAI Speech, không tải WAV, không tạo `SceneVoice`, không chạy `SceneAudioMixer` và không tạo `SceneVideoNarrated`. Không fallback ngầm sang TTS khi Kling sai hoặc im lặng.

### 8.3. Trạng thái triển khai và điều kiện vận hành hiện tại

Source hiện hành đã có contract speech intent, OpenAI structured output, `KlingNativeAudioPromptComposer`, rate policy cho `720p + nativeAudio`, kiểm tra audio sau tải, trạng thái `NativeAudioInvalid`/`AudioReviewRequired`, API bridge duyệt scene và UI phân biệt lời nhân vật với native voice-over.

Source cũng đã có migration 4.0.4, admin video policy, generic contract/service/bridge, Seedance client/prompt composer, worker đa provider, cache/proxy/cleanup và pricing bằng `completion_tokens`. Release build và test offline đã đạt ngày 2026-08-30; trạng thái này không đồng nghĩa migration thật, credential/rate thật hoặc smoke test BytePlus đã hoàn tất.

Để chạy được trong một môi trường, vẫn phải thỏa đồng thời:

- tổ chức có Kling credential Active và model Kling Video Active;
- model Kling có rate Active đúng metadata `{"resolution":"720p","nativeAudio":true}`;
- budget tổ chức và hạn mức thành viên còn đủ;
- nhân vật của cảnh đã khóa và có ảnh reference primary nếu cảnh dùng nhân vật;
- desktop có FFmpeg/FFprobe hợp lệ để kiểm tra clip và audio;
- người dùng nghe/duyệt từng output; kiểm tra tự động chỉ phát hiện track im lặng, không chứng minh lời nói đúng ngữ nghĩa.

Tiếng Việt được gắn nhãn **experimental/best-effort**. Tài liệu Kling 3.0 hiện công bố Native Audio đa ngôn ngữ cho Chinese, English, Japanese, Korean và Spanish, đồng thời cảnh báo ngôn ngữ ngoài danh sách có thể bị dịch sang English. Prompt vẫn yêu cầu giữ nguyên tiếng Việt, nhưng manual review là bắt buộc và chưa được xem là hỗ trợ production cho đến khi smoke test thực tế đạt.

Không cần migration TTS 4.0.3 hoặc rate OpenAI Voice để chạy Kling Native Audio. Schema/entity/API TTS hiện hữu vẫn được giữ để đọc dữ liệu cũ và phát triển tính năng ghép giọng sau này, nhưng không nằm trên đường gọi mặc định.

`ProjectRenderService` là đường gọi thực tế từ UI vào `FfmpegRenderService`. Service chỉ chọn `SceneVideo` thuộc đúng `ApprovedGenerationId` của từng scene trong scene-plan hiện hành, xác minh SHA-256 và cờ `nativeAudioAudible`, sau đó chuẩn hóa/concat theo thứ tự. Final MP4 chỉ được lưu thành `FinalVideo` khi có video stream, audio stream nghe được, đúng kích thước và thời lượng nằm trong tolerance; lỗi render chỉ retry cục bộ, không tạo request Kling/TTS mới.

## 9. Idempotency

- Idempotency key là duy nhất trong phạm vi tổ chức.
- Cùng key và cùng request trả về kết quả cũ hoặc trạng thái request hiện hữu.
- Cùng key nhưng khác dự án/payload trả `idempotency_key_conflict`.
- Budget reservation dùng cùng operation key nên hai request đồng thời không thể giữ ngân sách hai lần cho cùng thao tác.

## 10. API nghiệp vụ

### Tổ chức

- `GET /api/organizations`: các tổ chức user đang tham gia.
- `POST /api/organizations`: Global Admin tạo tổ chức.
- `GET|POST /api/organizations/{id}/members`: xem/thêm thành viên.
- `PUT /api/organizations/{id}/members/{userId}`: đổi role, status, hạn mức.
- `PUT /api/organizations/{id}/budget`: cập nhật ngân sách tháng.
- `GET /api/organizations/{id}/providers`: trạng thái provider, không có key.
- `PUT /api/organizations/{id}/providers/{providerCode}/credential`: lưu/rotate credential.
- `GET /api/organizations/{id}/video-policy`: đọc policy video và model hiện hành.
- `PUT /api/organizations/{id}/video-policy`: Owner/OrganizationAdmin đổi policy cho các project chưa snapshot.
- `GET /api/organizations/{id}/usage`: tổng hợp và ledger kỳ hiện tại.
- `GET /api/organizations/{id}/audit`: nhật ký tổ chức đã lọc dữ liệu nhạy cảm; chỉ Owner/OrganizationAdmin.

Usage response có tổng input token, output token, video second và nhóm theo provider/model/member. UI chỉ format dữ liệu quyết toán do server trả về, không tự tính lại chi phí.

### Giá AI

- `GET /api/admin/ai-pricing`: catalog/model/rate, chỉ Global Admin.
- `POST /api/admin/ai-pricing/models/{modelId}/rates`: tạo rate mới và kết thúc rate cũ cùng usage type.
- `DELETE /api/admin/ai-pricing/rates/{rateId}`: ngừng rate.

### Generation

- `GET /api/generation/providers/status?organizationId=...`
- `POST /api/generation/content`
- `POST /api/generation/characters/{characterId}/reference-images`
- `GET /api/generation/character-images/{providerRequestId}/content`
- `POST /api/generation/scenes/{sceneId}/voice`
- `GET /api/generation/scene-voices/{providerRequestId}/content`
- `POST /api/generation/videos`
- `GET /api/generation/videos/{providerRequestId}`
- `GET /api/generation/videos/{providerRequestId}/content`
- Các endpoint `/api/generation/kling/videos...` được giữ cho desktop cũ trong thời gian compatibility.
- `POST /api/generation/kling/videos`
- `GET /api/generation/kling/videos/{providerRequestId}`
- `GET /api/generation/kling/videos/{providerRequestId}/content`

### Quên mật khẩu

- `POST /api/auth/forgot-password`: nhận email và luôn trả thông báo chung để không tiết lộ tài khoản có tồn tại hay không.
- `POST /api/auth/reset-password`: xác minh email, OTP 6 số và đặt mật khẩu mới.
- OTP mặc định hết hạn sau 10 phút, bị thay thế khi gửi lại và bị xóa sau 5 lần nhập sai.
- OTP không lưu plaintext; server lưu hash bên trong payload Data Protection tại bảng Identity `AspNetUserTokens` có sẵn.
- Yêu cầu gửi email bị giới hạn 5 lần/15 phút/IP; thao tác reset bị giới hạn 10 lần/15 phút/IP.
- Reset thành công cập nhật thời điểm đổi mật khẩu, bỏ lockout Identity và thu hồi toàn bộ session/refresh token cũ.

Gateway giới hạn mặc định 30 request/phút cho mỗi user/IP.

## 11. Mã lỗi nghiệp vụ chính

| Mã | Ý nghĩa |
|---|---|
| `license_unavailable` | License hoặc lease thiết bị không hợp lệ |
| `invalid_credentials` | Email hoặc mật khẩu sai; giữ màn hình đăng nhập để user nhập lại |
| `account_locked` / `account_unavailable` | Tài khoản bị khóa hoặc không được phép đăng nhập |
| `invalid_refresh_token` / `session_expired` | Phiên đã hết hạn hoặc bị thu hồi; desktop xóa token cục bộ và đưa user về đăng nhập |
| `password_reset_unavailable` | Server chưa cấu hình đầy đủ SMTP để gửi OTP |
| `invalid_password_reset_otp` | OTP sai, hết hạn hoặc đã vượt quá số lần thử |
| `password_reset_validation_failed` | Mật khẩu mới chưa đạt chính sách Identity |
| `organization_access_denied` | Không phải thành viên hoạt động |
| `organization_generation_denied` | Vai trò không được dùng AI |
| `organization_required` | Thuộc nhiều tổ chức nhưng request không chỉ rõ tổ chức |
| `organization_budget_exceeded` | Không đủ ngân sách tổ chức |
| `member_budget_exceeded` | Không đủ hạn mức thành viên |
| `pricing_not_configured` | Model thiếu rate bắt buộc |
| `video_policy_not_configured` / `video_model_not_enabled` | Tổ chức chưa chọn model video hoặc model chưa được Global Admin bật |
| `video_snapshot_unavailable` | Provider/model đã snapshot trên project không còn trong catalog; không fallback sang model khác |
| `provider_task_expired` / `provider_polling_exhausted` | Task provider đã hết hạn hoặc vượt giới hạn polling |
| `unsafe_provider_output_url` | URL/redirect output không thuộc allowlist hoặc DNS trỏ vào vùng mạng bị chặn |
| `provider_output_cache_full` | Cache video server đã đạt giới hạn dung lượng cấu hình |
| `byteplus_reference_not_approved` | Ảnh nhân vật không phải output OpenAI đã duyệt của hệ thống |
| `openai_not_configured` / `kling_not_configured` | Thiếu provider/model/credential hoạt động |
| `openai_image_model_not_configured` | Model Image đang hoạt động không phải `gpt-image-2` hoặc chưa được cấu hình |
| `openai_organization_verification_required` | Tổ chức OpenAI chưa hoàn tất xác minh để dùng model ảnh |
| `openai_image_moderation_blocked` | Prompt do server dựng bị chính sách an toàn của provider từ chối |
| `generated_image_expired` | Payload ảnh tạm đã hết hạn, cần sinh lại |
| `openai_voice_model_not_configured` | Model Voice Active không phải `gpt-4o-mini-tts` hoặc chưa được cấu hình đúng |
| `project_voice_not_configured` / `project_voice_rate_invalid` | Project chưa có voice alias hoặc tốc độ đọc hợp lệ |
| `scene_narration_empty` / `scene_narration_changed` | Scene không có narration hoặc narration/hash đã thay đổi so với request |
| `generated_voice_expired` | WAV tạm trên server đã hết hạn; cần tạo lại voice generation hợp lệ |
| `voice_audio_invalid` | Provider trả audio không phải WAV hợp lệ hoặc metadata audio không đạt |
| `idempotency_key_conflict` | Key đã dùng cho payload khác |
| `provider_credential_test_failed` | Credential mới bị provider từ chối; key cũ được giữ |

## 12. Bảo mật

- Chỉ HTTPS; JWT ngắn hạn và refresh token rotation.
- OTP reset mật khẩu được sinh bằng bộ sinh số mật mã; hash và thời hạn nằm trong payload Data Protection, không lưu OTP plaintext trong database hoặc log.
- API yêu cầu reset trả cùng một phản hồi cho email tồn tại và không tồn tại; không gửi email cho tài khoản không hoạt động.
- Đổi mật khẩu qua luồng reset thu hồi mọi session hiện hữu để access token cũ mất hiệu lực ở request tiếp theo.
- Refresh token không hợp lệ, hết hạn hoặc bị phát hiện tái sử dụng làm desktop xóa phiên cục bộ và quay về màn hình đăng nhập.
- Mỗi access token phải có session ID và device ID hợp lệ.
- License lease được kiểm tra tại từng request AI và download.
- Credential mã hóa bằng Data Protection keys lưu trong database của server.
- Không log hoặc trả API key; audit chỉ ghi secret hint.
- Base URL provider dùng allowlist host/HTTPS/port cố định.
- Output proxy chỉ nhận host thuộc allowlist của đúng provider, kiểm tra lại mọi redirect và chặn loopback, private, link-local, carrier-grade NAT, IPv6 nội bộ cùng DNS rebinding. Signed output URL chỉ tồn tại trong bộ nhớ khi tải cache, không ghi vào request log mới và không trả cho desktop.
- Desktop SQL user dùng role tối thiểu; bị deny schema `ai`, `auth`, `dbo`, provider credentials, provider requests và usage truth.
- Desktop SQL user bị deny trực tiếp toàn bộ bảng `vf.GeneratedImageOutputs` và `vf.GeneratedVideoOutputs`; output chỉ tải qua API server có xác thực.

## 13. Điều kiện nghiệm thu

1. Desktop không có form nhập key và bundle không chứa UI BYOK.
2. Desktop không có client gọi trực tiếp OpenAI/Kling/BytePlus và không có request/setting chọn provider/model.
3. User ngoài tổ chức, Viewer, license hết hạn hoặc lease quá hạn không phát sinh provider request.
4. Hai request đồng thời không vượt ngân sách đã khóa và không tạo hai reservation cùng operation.
5. Credential rotate không làm hỏng task Kling đang chạy.
6. Thay đổi rate không đổi giá request đã có snapshot.
7. Mỗi actual cost truy được về user, project, provider request, model và credential version.
8. Khi desktop đóng, Kling worker server vẫn polling và reconciliation vẫn hoạt động.
9. URL video provider không lộ cho desktop và proxy chặn SSRF.
10. Toàn bộ solution Release biên dịch không cảnh báo và bộ test tự động đạt.
11. Kling request của luồng mới bật native audio và được quote theo đúng rate/capability có audio.
12. Prompt Kling chứa speech mode, đúng một speaker, nguyên văn lời cần nói, language, voice style, lip-sync, ambience và SFX; lời vượt word budget bị chặn trước outbound.
13. Clip thiếu/không nghe được audio không thể duyệt; clip hợp lệ phải qua bước nghe và duyệt thủ công trước khi scene `Approved`.
14. Workflow mặc định không gọi TTS, không tạo WAV/`SceneVideoNarrated` và retry Native Audio không phát sinh request trùng theo cùng idempotency key.
15. Thiếu rate/budget, Viewer hoặc truy cập chéo project/character không tạo outbound request GPT-Image-2.
16. Retry ảnh cùng idempotency key không tạo ảnh hoặc chi phí trùng; payload Base64 và prompt đầy đủ không xuất hiện trong request log.
17. Ảnh được tải qua API có xác thực, kiểm tra hash trước khi lưu; sinh lại đổi primary nhưng không tự khóa nhân vật.
