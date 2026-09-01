# Nghiệp vụ hệ thống VideoMaker 4.0

> Trạng thái hiện tại: AI Gateway tập trung theo tổ chức. Tài liệu BYOK 3.0 đã hết hiệu lực. Source hỗ trợ policy video bất biến theo project, Kling và BytePlus Seedance dưới cùng gateway; BytePlus/Seedance được seed disabled và chỉ được bật theo tổ chức sau migration, credential, rate và smoke test có phí. Luồng mặc định hiện vẫn là **Kling Native Audio 720p** cho tới khi quản trị viên chủ động chọn policy khác. Desktop bắt buộc preview/nghe/duyệt từng clip, chỉ render `SceneVideo` của đúng `ApprovedGenerationId`; TTS/WAV được giữ tương thích nhưng không fallback ngầm.

> Cập nhật ngữ cảnh: 2026-09-01. Đây là nguồn sự thật nghiệp vụ toàn hệ thống. Source đã có thư viện tính nhất quán **text-only** cho bối cảnh/đạo cụ/item theo project, materialize đề xuất AI và xác nhận trực tiếp theo từng cảnh; ảnh tham chiếu của các loại tài sản này chưa thuộc phạm vi hiện tại. `NGHIEP_VU_SINH_VIDEO_VA_DONG_BO_NHAN_VAT.md` và `NGHIEP_VU_TAO_VIDEO_NGAN_KLING.md` chỉ bổ sung chi tiết cho từng luồng; khi có khác biệt, tài liệu này được ưu tiên. Source code và migration vẫn là nguồn sự thật kỹ thuật.

## 1. Mục tiêu

VideoMaker hỗ trợ người dùng nhập chủ đề, tự sinh content plan/kịch bản/prompt bằng OpenAI, sinh clip theo từng cảnh bằng provider video trong snapshot của project và tải clip qua server về workspace để tiếp tục quy trình dựng video.

Ngoài dự án nhiều cảnh, desktop có luồng **Tạo video ngắn**: người dùng nhập direct prompt, hệ thống tạo project một scene và không gọi OpenAI để viết lại nội dung. Màn hình này chỉ cho chạy khi video policy hiện hành của tổ chức là Kling; chi tiết nằm trong `NGHIEP_VU_TAO_VIDEO_NGAN_KLING.md`.

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
9. Desktop hiển thị hồ sơ nhân vật, tài sản text do AI đề xuất và storyboard để người dùng kiểm tra, chọn ảnh tham chiếu, khóa nhân vật, xác nhận tài sản theo cảnh và sửa các cảnh chưa gửi provider trước khi phát sinh chi phí Kling.
10. Với workflow video dài `OpenAiStructuredPlan` đã snapshot Kling, server thay ngôn ngữ nội dung hiệu lực thành `vi-VN`, yêu cầu toàn bộ trường văn bản dành cho người đọc bằng tiếng Việt và chặn output sai ngôn ngữ trước khi desktop lưu scene plan. Tên riêng cùng khóa/enum máy đọc được giữ nguyên. BytePlus/Seedance vẫn dùng ngôn ngữ project; `DirectShortVideo` không thuộc quy tắc này.
11. Cùng phạm vi video dài Kling, OpenAI phải trả `OnCameraDialogue` khi scene có một presenter và có lời; `NativeVoiceOver` chỉ được dùng khi `character_keys=[]`. On-camera visual prompt phải mô tả hành động nói nhìn thấy được, mặt/miệng rõ và cử chỉ khi nói; kết quả sai quan hệ hoặc chỉ đứng/cười không được lưu thành plan hợp lệ.

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

### 7.2. Thư viện text giữ bối cảnh, đạo cụ và item

Giai đoạn hiện tại chỉ quản lý text, không tải lên hoặc sinh ảnh tham chiếu cho bối cảnh/đạo cụ/item:

1. Mỗi project có thư viện riêng với ba loại `Background`, `Prop`, `Item`; mỗi hồ sơ có tên, mô tả chuẩn và trạng thái `Draft`/`Locked`.
2. Người dùng có quyền generation được tạo/sửa tài sản nháp, khóa, mở khóa và gắn tài sản vào các scene của scene-plan hiện hành. `Viewer` chỉ được xem.
3. Khóa tài sản tạo một `ProjectAssetVersion` bất biến. Mở khóa không sửa version cũ; lần khóa tiếp theo tạo version tăng dần.
4. Một tài sản chỉ áp dụng cho những scene được gắn với nó. Không tự chèn toàn bộ thư viện vào mọi scene.
5. Scene được phép không gắn tài sản. Nếu đã gắn thì phải có đúng một `Background`; `Prop` và `Item` là tùy chọn. Server kiểm tra quy tắc này và độ dài prompt Kling trước khi lưu assignment, không tạo provider request và không phát sinh chi phí ở bước kiểm tra.
6. Nếu đã gắn thì toàn bộ tài sản của scene phải ở trạng thái `Locked`; tài sản nháp làm request video dừng với `scene_asset_not_locked` trước resolver, budget và outbound provider.
7. Server ghép text của đúng version đang khóa vào phần continuity bắt buộc của prompt Kling/Seedance. Desktop không được gửi mô tả thay thế làm nguồn sự thật.
8. `ProviderRequestAssetVersions` lưu liên kết từ provider request tới đúng version đã dùng. `ProviderRequests.RequestJson` chỉ lưu ID/version/hash, không lưu lại toàn bộ mô tả hoặc prompt hiệu lực.
9. Mở khóa hay sửa tài sản không làm thay đổi clip đã tạo. Lần sinh clip mới chỉ dùng version mới sau khi người dùng khóa lại.
10. Content plan AI đồng thời trả thư viện bối cảnh/đạo cụ/item và `asset_key` theo từng scene. Server materialize thành tài sản `Draft`, không tự khóa và không cho desktop ghi trực tiếp các bảng sự thật của thư viện.
11. Người dùng có thể duyệt và khóa theo lô toàn bộ tài sản AI nháp đang được scene-plan hiện hành sử dụng. Server xác minh đủ tập tài sản, concurrency token và toàn bộ preflight trước khi ghi các version trong một lần lưu; tài sản `Manual` không bị khóa ngầm.
12. Tại Storyboard, người dùng xác nhận tài sản ngay trên từng cảnh bằng nút **Xác nhận tài sản cảnh**. Hành động này gửi đúng tập ID/concurrency token đang hiển thị, kiểm tra lại assignment và prompt rồi khóa nguyên tử mọi tài sản nháp đang gắn với cảnh, kể cả nguồn `Manual`, vì đây là xác nhận tường minh của người dùng; không gọi provider, không tạo reservation và không phát sinh usage. Tài sản không gắn với cảnh không bị khóa.
13. UI chính chỉ dùng ba trạng thái `Chờ xác nhận`, `Cần chỉnh sửa`, `Đã sẵn sàng` và không yêu cầu người dùng hiểu `Draft`, version hay giới hạn prompt kỹ thuật. Bộ đếm trong **Chi tiết nâng cao** chỉ hiển thị độ dài phần bắt buộc; prompt hoàn chỉnh được server tự co phần tùy chọn trong giới hạn Kling. Chỉ phần bắt buộc thực sự vượt giới hạn mới chặn thao tác.
14. Đồng bộ lại asset plan dùng response của provider request đã hoàn tất, không gọi OpenAI lần nữa. Tài sản `Locked` hoặc nguồn `Manual` không bị ghi đè; tài sản AI nháp được phép cập nhật theo scene-plan mới. UI đặt thao tác này trong tùy chọn nâng cao với tên “Khôi phục đề xuất AI”.
15. Dữ liệu assignment cũ sai quy tắc được trả về kèm blocker để UI yêu cầu sửa, không tự động xóa hoặc đổi bối cảnh.
16. Hệ thống hiện chưa có endpoint sinh ảnh storyboard theo scene, vì vậy text continuity đang được áp dụng trên đường sinh clip video. Khi bổ sung ảnh scene sau này, đường đó phải đọc cùng assignment/version server-side; không tạo một thư viện ảnh hoặc prompt song song ở desktop.

## 8. Luồng sinh video đa provider

1. Khi project tạo video lần đầu, server snapshot provider/model, policy version, resolution và Native Audio từ policy tổ chức. Snapshot không tự đổi khi quản trị viên đổi policy sau đó.
2. Desktop chỉ gửi project, scene, version, idempotency key và ảnh reference đã duyệt; không gửi provider/model/prompt/thời lượng làm nguồn sự thật.
3. Server kiểm tra capability, credential, rate và budget trước outbound, sau đó route qua Kling adapter hoặc BytePlus Seedance adapter. Không fallback giữa provider.
4. Worker đa provider polling task. Khi hoàn tất, server kiểm tra output host theo provider, chống SSRF/DNS rebinding/redirect sai host, giới hạn MIME/dung lượng, tải vào cache và chỉ công bố URL proxy tương đối.
5. Task `Expired` hoặc vượt giới hạn polling là terminal; desktop cho phép attempt mới thay vì chờ vô hạn.
6. Với BytePlus, chỉ ảnh nhân vật do OpenAI trong hệ thống tạo, đã thành primary và được duyệt mới được dùng; ảnh tải lên/người thật bị chặn trước outbound.

### 8.1. Compatibility Kling và Native Audio

1. Người dùng chọn một hoặc nhiều cảnh trên storyboard; với cảnh có nhân vật, desktop chỉ cho tạo clip sau khi hồ sơ đã khóa và có ảnh tham chiếu chính được duyệt.
2. Desktop đọc ảnh từ workspace, kiểm tra dung lượng và SHA-256 rồi gửi request trung lập provider gồm project, scene, scene-plan version, prompt version, `organizationId`, idempotency key và ảnh tham chiếu nếu có. Provider, model, prompt hiệu lực, thời lượng, tỷ lệ, độ phân giải và Native Audio được server đọc từ dữ liệu đã lưu/snapshot; dữ liệu Base64 không được ghi vào request log.
3. Server xác minh lại quyền sở hữu project/scene, prompt version, character mapping, trạng thái nhân vật, ID ảnh, MIME, dung lượng và SHA-256 theo `MediaAsset`; dữ liệu client không được dùng để thay thế hồ sơ đã duyệt trên server.
4. Server ghép prompt hiệu lực bằng template có version. Với on-camera của video dài Kling, khối speech/performance đứng trước identity/tài sản, gắn người nói với người duy nhất trong ảnh first-frame, yêu cầu bắt đầu nói trong 0,5 giây đầu, thấy rõ môi/hàm/biểu cảm và cấm narrator hoặc chỉ mỉm cười im lặng. Với model Kling 3.0 thường, ảnh chuẩn được gửi theo luồng image-to-video làm first frame; model Omni dùng element reference khi model đó được quản trị viên bật và cấu hình rate.
5. Server kiểm tra rate, ngân sách, giữ chi phí ước tính, gửi task Kling và ghi external task ID cùng credential version, character version, reference ID và hash.
6. Worker server polling các task đến hạn. Desktop cũng có thể hỏi trạng thái nhưng không chịu trách nhiệm duy nhất cho polling.
7. Khi Kling hoàn tất, server quyết toán theo reported cost nếu có; nếu không dùng estimated cost đã khóa.
8. Desktop nhận URL tương đối trung lập provider của server: `/api/generation/videos/{providerRequestId}/content`. Endpoint Kling cũ chỉ được giữ cho tương thích, không phải đường gọi mặc định của desktop.
9. Proxy xác thực lại user/license/tổ chức, chỉ cho HTTPS và host thuộc allowlist của provider, kiểm tra DNS chống SSRF, giới hạn redirect, MIME, dung lượng file và tổng dung lượng cache theo cấu hình.
10. Clip tải xong được hiển thị ngay tại card của cảnh qua virtual media host cục bộ; cảnh chưa có clip dùng placeholder theo theme và không tự gọi thêm provider ảnh.
11. Trước resolver/rate/budget/outbound, video dài Kling chặn voice-over còn gắn nhân vật, on-camera thiếu speaker/reference, speech mode không khớp `Dialogue`/`Narration`, Native Audio không phù hợp snapshot và prompt hình ảnh yêu cầu im lặng/khép miệng/lời dẫn ngoài khung hình.

### 8.2. Nghiệp vụ âm thanh của clip và video cuối

Luồng sản phẩm hiện tại chỉ dùng Native Audio do provider video đã snapshot cho project sinh trực tiếp cùng clip. Kling 3.0 là policy mặc định; BytePlus chỉ đi vào luồng này sau rollout có kiểm soát:

1. Mỗi cảnh có một `SpeechMode`: `None`, `OnCameraDialogue` hoặc `NativeVoiceOver`.
2. OpenAI content planner trả riêng `spoken_text`, speaker, voice style, ambience và sound effects; `visual_prompt` không được lặp lại lời nói.
3. Server đọc dữ liệu scene đã lưu, không tin prompt lời nói do desktop tự gửi, rồi dựng prompt theo adapter/template có version của provider snapshot. Prompt phải giữ nguyên lời đã duyệt, speaker, ngôn ngữ, voice style, ambience và SFX; on-camera dialogue còn yêu cầu đúng một người nói và lip-sync.
4. Hệ thống không áp dụng giới hạn số từ cố định theo thời lượng. `spoken_text` đã lưu được giữ nguyên khi dựng prompt provider; desktop và server không tự cắt lời hoặc chặn request chỉ vì số từ. Người dùng phải nghe duyệt kết quả vì lời quá dài vẫn có thể khiến provider nói nhanh, thiếu lời hoặc lệch khẩu hình.
5. Kling hiện dùng `720p`, `NativeAudio = true` và `multi_shot = false`; BytePlus hiện dùng biến thể 720p/24fps/Native Audio theo catalog. Rate Active phải khớp đúng model, usage type và capability; thiếu rate/budget dừng trước outbound.
6. Desktop tải raw `SceneVideo` qua proxy có xác thực, kiểm tra video bằng FFprobe và đo audio bằng `AudioQualityValidator`. Metadata chỉ lưu speech hash, mode và thống kê audio; không log nguyên văn lời hoặc provider URL.
7. Clip thiếu audio stream hoặc gần như im lặng chuyển `NativeAudioInvalid`, không được duyệt và có thể sửa prompt/tạo lại bằng provider request mới.
8. Attempt mới sau `NativeAudioInvalid` không tự chạy. Khi người dùng xác nhận request có phí mới, server đọc generation terminal và tự áp `speech-recovery-v1` cho on-camera: medium close-up/medium shot, nói ngay không có intro im lặng, room tone tối thiểu, không nhạc và không hành động cạnh tranh với lời. Profile/version được lưu trong request snapshot an toàn; full speech vẫn chỉ lưu hash.
9. Clip có audio nghe được chuyển `AudioReviewRequired`. Người dùng phải phát preview và xác nhận đủ câu, đúng người nói/kiểu voice-over và khẩu hình/đồng bộ chấp nhận được trước khi bấm **Duyệt hình và âm thanh**. Loudness check không được mô tả như ASR hoặc kiểm tra lip-sync tự động.
10. Chỉ khi duyệt, scene mới có `ApprovedGenerationId` và trạng thái `Approved`; project chỉ `ReadyToRender` khi toàn bộ scene đã duyệt.
11. Workflow mặc định không gọi OpenAI Speech, không tải WAV, không tạo `SceneVoice`, không chạy `SceneAudioMixer` và không tạo `SceneVideoNarrated`. Không fallback ngầm sang TTS khi provider trả âm thanh sai hoặc im lặng.

### 8.3. Trạng thái triển khai và điều kiện vận hành hiện tại

Source hiện hành đã có contract speech intent, OpenAI structured output, policy tiếng Việt cho video dài Kling, template `kling-native-audio-v4-vietnamese-speech-first`, retry `speech-recovery-v1`, rate policy cho `720p + nativeAudio`, kiểm tra audio sau tải, trạng thái `NativeAudioInvalid`/`AudioReviewRequired`, checklist duyệt scene và UI phân biệt lời nhân vật với native voice-over.

Source cũng đã có migration 4.0.4–4.0.8, admin video policy, generic contract/service/bridge, Seedance client/prompt composer, worker đa provider, cache/proxy/cleanup, pricing bằng `completion_tokens`, thư viện tài sản text và xác nhận tài sản theo scene. Trạng thái source không đồng nghĩa migration thật, credential/rate thật hoặc smoke test BytePlus đã hoàn tất trên một môi trường triển khai. Riêng cải tiến xác nhận tài sản một chạm và prompt analyzer dùng schema 4.0.8 hiện có, không cần migration SQL bổ sung.

Để chạy được trong một môi trường, vẫn phải thỏa đồng thời:

- tổ chức có credential Active, model video Active và policy Active cho provider được chọn; mặc định là Kling;
- model có đủ rate Active đúng usage type/capability; Kling cần metadata `{"resolution":"720p","nativeAudio":true}`;
- budget tổ chức và hạn mức thành viên còn đủ;
- nhân vật của cảnh đã khóa và có ảnh reference primary nếu cảnh dùng nhân vật;
- nếu cảnh có gắn tài sản text thì assignment hợp lệ, có đúng một `Background` và mọi tài sản đang gắn đã được xác nhận/khóa;
- desktop có FFmpeg/FFprobe hợp lệ để kiểm tra clip và audio;
- người dùng nghe/duyệt từng output; kiểm tra tự động chỉ phát hiện track im lặng, không chứng minh lời nói đúng ngữ nghĩa.

Trong workflow video dài `OpenAiStructuredPlan`, project snapshot Kling bắt buộc dùng lời tiếng Việt và metadata `vi-VN`; prompt hiệu lực cũng dùng wrapper tiếng Việt. Prompt tiếng Anh cũ, hồ sơ nhân vật/tài sản tiếng Anh hoặc chỉnh sửa scene bằng tiếng Anh bị chặn trước chi phí Kling và UI hướng dẫn sinh lại nội dung tiếng Việt. Luồng video ngắn `DirectShortVideo` vẫn giữ direct visual prompt theo nội dung người dùng nhập, không có speech intent chính thức và không bị policy video dài áp dụng.

Policy nhân vật nói trực tiếp cũng chỉ bật khi đồng thời là `OpenAiStructuredPlan` và provider snapshot `Kling`. BytePlus/Seedance cùng `DirectShortVideo` không dùng template/recovery profile này. Dữ liệu lịch sử không bị migration hoặc tự đổi `Narration` thành `Dialogue`; request mới từ scene cũ phải sửa/sinh lại để thỏa policy.

Kling Native Audio không phụ thuộc rate OpenAI Voice hoặc việc bật TTS. Runbook vẫn chạy migration 4.0.3 theo đúng chuỗi 4.0.0–4.0.8 để giữ schema tương thích; schema/entity/API TTS hiện hữu được giữ để đọc dữ liệu cũ và phát triển tính năng ghép giọng sau này, nhưng không nằm trên đường gọi mặc định.

`ProjectRenderService` là đường gọi thực tế từ UI vào `FfmpegRenderService`. Service chỉ chọn `SceneVideo` thuộc đúng `ApprovedGenerationId` của từng scene trong scene-plan hiện hành, xác minh SHA-256 và cờ `nativeAudioAudible`, sau đó chuẩn hóa/concat theo thứ tự. Final MP4 chỉ được lưu thành `FinalVideo` khi có video stream, audio stream nghe được, đúng kích thước và thời lượng nằm trong tolerance; lỗi render chỉ retry cục bộ, không tạo request provider/TTS mới.

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

### Thư viện text của project

- `GET /api/projects/{projectId}/assets`
- `POST /api/projects/{projectId}/assets`
- `POST /api/projects/{projectId}/assets/materialize`
- `PUT /api/projects/{projectId}/assets/{projectAssetId}`
- `POST /api/projects/{projectId}/assets/{projectAssetId}/lock`
- `POST /api/projects/{projectId}/assets/{projectAssetId}/unlock`
- `POST /api/projects/{projectId}/assets/approve-ai`
- `POST /api/projects/{projectId}/assets/scenes/{sceneId}/confirm`
- `DELETE /api/projects/{projectId}/assets/{projectAssetId}`
- `PUT /api/projects/{projectId}/assets/scenes/{sceneId}`

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
| `project_asset_changed` / `project_asset_name_conflict` | Tài sản bị cập nhật đồng thời hoặc trùng tên trong cùng loại/project |
| `project_asset_locked` / `project_asset_edit_denied` | Tài sản phải mở khóa trước khi sửa hoặc vai trò chỉ được xem |
| `scene_asset_not_locked` / `scene_asset_version_missing` | Scene dùng tài sản chưa khóa hoặc thiếu version bất biến; chặn trước outbound |
| `scene_asset_background_invalid` / `kling_prompt_too_long` | Assignment thiếu/thừa bối cảnh hoặc phần prompt bắt buộc vượt giới hạn Kling; chặn ngay khi lưu tài sản cảnh, trước outbound |
| `project_asset_approval_stale` | Danh sách tài sản AI cần duyệt đã thay đổi; desktop phải tải lại trước khi khóa theo lô |
| `scene_asset_confirmation_stale` | Assignment hoặc tài sản của cảnh đã thay đổi; desktop tải lại và yêu cầu người dùng xác nhận lại |
| `kling_on_camera_speaker_required` / `kling_speech_intent_invalid` | Scene video dài Kling thiếu đúng một speaker/reference hoặc mode đã lưu không khớp `Dialogue`/`Narration`; sửa hoặc sinh lại cảnh trước khi tạo clip |
| `kling_voice_over_character_not_allowed` | Voice-over của video dài Kling còn gắn nhân vật; chuyển sang on-camera hoặc bỏ nhân vật để thành B-roll |
| `kling_on_camera_action_invalid` | Prompt hình ảnh yêu cầu im lặng/khép miệng/lời dẫn ngoài khung hình; sửa prompt trước khi tạo request có phí |

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
5. Credential rotate không làm hỏng task provider video đang chạy theo credential version đã snapshot.
6. Thay đổi rate không đổi giá request đã có snapshot.
7. Mỗi actual cost truy được về user, project, provider request, model và credential version.
8. Khi desktop đóng, worker video server vẫn polling và reconciliation vẫn hoạt động.
9. URL video provider không lộ cho desktop và proxy chặn SSRF.
10. Toàn bộ solution Release biên dịch không cảnh báo và bộ test tự động đạt.
11. Video request của luồng mới bật Native Audio và được quote theo đúng provider/model/rate/capability đã snapshot.
12. Prompt provider chứa speech mode, nguyên văn lời cần nói, language, voice style, ambience và SFX; on-camera dialogue có đúng một speaker/lip-sync, không áp dụng ngưỡng số từ cố định theo thời lượng.
13. Clip thiếu/không nghe được audio không thể duyệt; clip hợp lệ phải qua bước nghe và duyệt thủ công trước khi scene `Approved`.
14. Workflow mặc định không gọi TTS, không tạo WAV/`SceneVideoNarrated` và retry Native Audio không phát sinh request trùng theo cùng idempotency key.
15. Thiếu rate/budget, Viewer hoặc truy cập chéo project/character không tạo outbound request GPT-Image-2.
16. Retry ảnh cùng idempotency key không tạo ảnh hoặc chi phí trùng; payload Base64 và prompt đầy đủ không xuất hiện trong request log.
17. Ảnh được tải qua API có xác thực, kiểm tra hash trước khi lưu; sinh lại đổi primary nhưng không tự khóa nhân vật.
18. Tài sản text chỉ ảnh hưởng scene được gắn; tài sản chưa khóa chặn video trước resolver/budget/outbound và không làm thay đổi clip cũ.
19. Mỗi provider request video truy được đúng `ProjectAssetVersion` đã đưa vào prompt, còn request log không chứa toàn bộ mô tả chuẩn.
20. Xác nhận tài sản trên card cảnh khóa nguyên tử đúng tập tài sản đang gắn, không khóa tài sản ngoài cảnh, không gọi provider và không ghi usage.
21. Storyboard hiển thị đúng ba trạng thái tài sản có thể hành động; assignment sai yêu cầu sửa lựa chọn, assignment hợp lệ nhưng còn nháp yêu cầu xác nhận, assignment hợp lệ đã khóa báo sẵn sàng.
22. Preflight Kling chỉ chặn khi phần prompt bắt buộc vượt giới hạn; phần scene/negative tùy chọn được composer tự co và không bị UI trình bày như lỗi giả.
