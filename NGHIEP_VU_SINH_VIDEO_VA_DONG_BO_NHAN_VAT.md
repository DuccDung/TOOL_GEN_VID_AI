# Nghiệp vụ sinh video và đồng bộ nhân vật

> Cập nhật theo source ngày 2026-08-28. Phần Kling Native Audio 720p đã triển khai; TTS voice-over và FFmpeg mix/ducking vẫn được đánh dấu rõ là mục tiêu tiếp theo.

## 1. Mục tiêu và phạm vi

VideoMaker biến một chủ đề thành video theo chuỗi nghiệp vụ:

1. Người dùng chọn tổ chức và dự án.
2. OpenAI tạo content plan có cấu trúc, gồm kịch bản, hồ sơ nhân vật và danh sách cảnh.
3. Người dùng kiểm tra, chỉnh và khóa nhân vật bằng một ảnh tham chiếu chính.
4. Người dùng kiểm tra storyboard, chỉnh các cảnh chưa gửi provider và chọn cảnh cần tạo.
5. Kling tạo một clip cho từng cảnh.
6. Server tiếp tục polling Kling khi desktop đóng.
7. Desktop tải clip qua proxy của server, lưu vào workspace và đánh dấu cảnh hoàn tất.
8. Khi mọi cảnh trong kế hoạch hiện hành có clip được duyệt, dự án chuyển sang `ReadyToRender` để bước dựng video cục bộ tiếp tục.

Tài liệu này tập trung vào content, nhân vật, storyboard và clip theo cảnh. Ghép clip, giọng đọc, phụ đề và xuất video cuối bằng FFmpeg là bước sau, không được hiểu là tự động hoàn tất ngay khi Kling trả video.

## 2. Các bất biến không được phá vỡ

- OpenAI và Kling chỉ được gọi từ `TOOL-SERVER`; `TOOL-LOCAL` không giữ key và không gọi provider trực tiếp.
- Mọi request AI phải gắn với tổ chức, user, project, model, credential version và rate snapshot.
- Budget tổ chức bằng `0` là khóa AI. Thiếu rate hoặc credential Active phải dừng trước outbound call.
- `Viewer` không được phát sinh chi phí AI.
- Cảnh có nhân vật chỉ được gửi Kling khi nhân vật ở trạng thái `Approved` và có ảnh tham chiếu chính hợp lệ.
- Cảnh không gắn nhân vật không được gửi kèm ảnh tham chiếu nhân vật.
- Model Kling hiện hành chỉ hỗ trợ tối đa một nhân vật tham chiếu trong mỗi cảnh.
- Không được âm thầm sửa prompt của cảnh đã có provider request đang hoạt động hoặc đã có clip được duyệt.
- Không trả URL output gốc của Kling về desktop; desktop chỉ nhận URL proxy tương đối của server.
- Không log Base64 ảnh tham chiếu, API key, Authorization header hoặc secret provider.

## 3. Ranh giới trách nhiệm hiện tại

### `TOOL-SERVER`

- Xác minh JWT, session, device claim, license lease, organization membership, role và quyền sở hữu project.
- Chọn model, credential Active và rate hiện hành.
- Giữ ngân sách, gọi OpenAI/Kling, ghi provider request, usage và quyết toán chi phí.
- Xác minh lại scene, prompt, character mapping và ảnh tham chiếu trước khi gọi Kling.
- Polling task Kling ở background và proxy nội dung video về desktop.

### `TOOL-LOCAL`

- Chọn tổ chức, tạo và hiển thị dự án/storyboard.
- Sau khi nhận content plan từ server, version hóa và ghi concept, script, style, character, scene và scene prompt vào dữ liệu workflow.
- Cho phép chỉnh hồ sơ nhân vật nháp, nhập ảnh tham chiếu và khóa nhân vật.
- Cho phép chỉnh cảnh chưa gửi Kling, chọn cảnh cần tạo, gửi request gateway và tải clip hoàn tất về workspace.
- Lưu `MediaAsset` và `VideoGeneration` cục bộ sau khi tải clip thành công.

### Lưu ý kiến trúc chuyển tiếp

Hiện dữ liệu workflow vẫn được desktop ghi trực tiếp vào SQL bằng database role ít quyền. Endpoint content của server lưu provider request/usage và trả structured plan; chính desktop mới persist plan vào các bảng workflow. Không được giả định rằng `POST /api/generation/content` đã tự tạo đầy đủ storyboard trong database nếu desktop bị đóng trước bước persist.

Nếu OpenAI đã hoàn tất nhưng desktop lỗi trước khi lưu plan, lần gọi lại với cùng idempotency key và cùng payload có thể lấy kết quả đã lưu tại server rồi tiếp tục persist mà không gọi OpenAI lần hai.

## 4. Các khái niệm dữ liệu chính

| Khái niệm | Ý nghĩa nghiệp vụ |
|---|---|
| `Project` | Dự án thuộc một tổ chức và một user; giữ các con trỏ version hiện hành và trạng thái pipeline. |
| `Concept` | Ý tưởng, hook, góc triển khai, đối tượng khán giả và CTA của một phiên bản content. |
| `Script` | Kịch bản đầy đủ và lời đọc theo cảnh của một phiên bản content. |
| `StyleProfile` | Phong cách hình ảnh và negative prompt của phiên bản content. |
| `Character` | Hồ sơ nhận diện nhân vật có `CharacterKey`, `Version`, đặc điểm cố định, trang phục và điều cấm thay đổi. |
| `CharacterReference` | Liên kết nhân vật với một ảnh `MediaAsset`; một ảnh được chọn làm `IsPrimary`. |
| `Scene` | Một cảnh thuộc `ScenePlanVersion`, có lời đọc, mô tả hình ảnh, thời lượng, timeline và danh sách Character ID. |
| `ScenePrompt` | Prompt Kling có version; chỉnh cảnh tạo prompt version mới và đánh dấu prompt trước là `Superseded`. |
| `ProviderRequest` | Sự thật về request OpenAI/Kling trên server: tổ chức, user, project, scene, model, credential, idempotency, rate và usage. |
| `VideoGeneration` | Một lần thử tạo clip cho một scene/prompt; liên kết với `ProviderRequest` và output asset. |
| `MediaAsset` | File ảnh tham chiếu hoặc clip đã tải về workspace, kèm MIME, size và SHA-256. |

`CharacterKey` là khóa logic ổn định trong một content plan, ví dụ `main_presenter`. `CharacterId` là khóa database của đúng phiên bản nhân vật. Scene do OpenAI trả về dùng `CharacterKey`; khi persist, desktop ánh xạ key đó thành `CharacterId` và lưu trong `CharacterIdsJson`.

## 5. Điều kiện trước khi dùng AI

Một request content hoặc Kling chỉ được đi tiếp khi thỏa toàn bộ điều kiện:

1. User đã đăng nhập và access token còn hiệu lực.
2. Session, device claim, license và device lease hợp lệ.
3. User là thành viên Active của tổ chức đang chọn.
4. Role là `Owner`, `OrganizationAdmin`, `BillingManager` hoặc `Member`.
5. Project thuộc đúng tổ chức và đúng user.
6. Provider/model tương ứng đang bật.
7. Tổ chức có credential Active cho provider.
8. OpenAI có đủ rate `InputToken` và `OutputToken`; Kling có rate `VideoSecond` đúng metadata `resolution=720p`, `nativeAudio=true`.
9. Budget tháng của tổ chức và hạn mức thành viên còn đủ cho reservation.
10. Idempotency key hợp lệ và chưa được dùng cho payload khác.

Desktop hiển thị provider readiness để hướng dẫn người dùng, nhưng server vẫn phải kiểm tra lại toàn bộ điều kiện; không tin trạng thái UI.

## 6. Luồng sinh content bằng OpenAI

### 6.1 Đầu vào

Desktop gửi:

- `ProjectId`;
- `OrganizationId` đang được chọn;
- `IdempotencyKey` theo mẫu `content:{projectId}:v{version}`.

Server lấy topic, language, platform, aspect ratio và target duration từ project trong database, không nhận các trường này làm nguồn sự thật từ UI ở request generation.

### 6.2 Giới hạn hiện hành

- Project có thể lưu target duration đến 3.600 giây, nhưng luồng tạo content tự động hiện chỉ hỗ trợ tối đa 360 giây.
- Số cảnh được yêu cầu bằng `ceil(targetDurationSeconds / 15)`.
- Tổng thời lượng các cảnh phải bằng đúng target duration.
- Mỗi cảnh hợp lệ từ 3 đến 15 giây.
- Content plan có tối đa một nhân vật dẫn chuyện xuất hiện lặp lại.
- Mỗi cảnh có từ 0 đến 1 `character_key`.
- Cảnh B-roll hoặc cảnh không có người dẫn phải trả `character_keys: []`.

### 6.3 Structured output bắt buộc

Content plan gồm:

- `title`, `hook`, `angle`, `audience`, `call_to_action`;
- `script_full_text`;
- `visual_style`, `negative_prompt`;
- danh sách `characters`;
- danh sách `scenes` theo đúng thứ tự thời gian.

Mỗi scene gồm:

- `sequence_number`;
- `story_purpose`;
- `narration`;
- `visual_prompt` mô tả chủ thể, môi trường, ánh sáng, hành động, bố cục và chuyển động camera;
- `character_keys`.

Mỗi character gồm:

- khóa logic, tên và vai trò;
- giới tính, tuổi, khuôn mặt, tóc, da và vóc dáng;
- quần áo và phụ kiện;
- `visual_identity`;
- `immutable_traits`;
- `forbidden_changes`.

OpenAI được gọi bằng Responses API, JSON Schema strict, `store=false`, giới hạn output token và `safety_identifier` là hash ổn định của user ID.

### 6.4 Chi phí và lưu kết quả

1. Server kiểm tra idempotency theo tổ chức và request hash.
2. Server chọn model Text, credential Active và hai rate token.
3. Server tạo rate snapshot và giữ estimated cost trong transaction nghiệp vụ ngân sách.
4. OpenAI trả structured plan cùng `input_tokens` và `output_tokens`.
5. Server tính actual cost bằng rate snapshot, ghi usage và quyết toán reservation.
6. Desktop kiểm tra plan không rỗng, duration hợp lệ, CharacterKey không trùng và mọi key của scene đều tồn tại.
7. Desktop persist một version content mới trong transaction rồi ghi thêm bản JSON vào `workspace/script/content-plan-v{version}.json`.

## 7. Version hóa và sinh lại content

Nút **Sinh lại content có nhân vật** tạo một phiên bản hoàn toàn mới, không chỉnh đè phiên bản hiện hành.

Khi version mới được persist:

- Concept, Script và StyleProfile `Approved` cũ chuyển thành `Superseded`.
- Character `Draft` hoặc `Approved` cũ chuyển thành `Superseded`.
- Character mới được tạo ở trạng thái `Draft` với ID và version mới.
- Scene và ScenePrompt mới được tạo theo version mới.
- Các con trỏ `CurrentConceptVersion`, `CurrentScriptVersion`, `CurrentCharacterVersion`, `CurrentStyleVersion`, `CurrentScenePlanVersion` chuyển sang version mới.
- UI storyboard chỉ đọc kế hoạch scene và character version hiện hành.

Dữ liệu version cũ, provider request, usage, ảnh và clip cũ không bị xóa để còn khả năng audit/đối soát. Các scene cũ có thể vẫn tồn tại trong database nhưng không còn là kế hoạch hiện hành.

Hệ quả nghiệp vụ:

- Sinh lại content phát sinh một request OpenAI và chi phí mới.
- Nhân vật phiên bản mới phải chọn lại ảnh và khóa lại; hệ thống hiện không tự kế thừa ảnh của nhân vật cũ.
- Clip đã tạo cho scene version cũ không tự động trở thành clip của scene version mới.
- Không được sửa trực tiếp character `Approved`; muốn thay nhận diện đã khóa phải sinh content version mới.

## 8. Nghiệp vụ đồng bộ nhân vật

### 8.1 Mục đích

Đồng bộ nhân vật không chỉ là lặp lại một câu mô tả. Hệ thống dùng đồng thời hai lớp khóa:

1. **Khóa bằng dữ liệu mô tả:** visual identity, trang phục, đặc điểm bất biến và điều cấm thay đổi.
2. **Khóa bằng ảnh:** một ảnh tham chiếu chính đã duyệt, xác minh bằng ID, MIME, dung lượng và SHA-256.

Hai lớp này được ghép vào request Kling cho mọi scene có cùng nhân vật.

### 8.2 Vòng đời nhân vật

| Trạng thái | Ý nghĩa | Được phép |
|---|---|---|
| `Draft` | Hồ sơ mới do OpenAI sinh, chưa khóa. | Sửa hồ sơ, thay ảnh tham chiếu, khóa nhân vật. |
| `Approved` | Hồ sơ và ảnh chính đã được người dùng chấp nhận. | Dùng để tạo Kling; không sửa hồ sơ hoặc thay ảnh. |
| `Superseded` | Thuộc content version cũ. | Chỉ giữ để đọc/audit; không dùng cho kế hoạch hiện hành. |

### 8.3 Chỉnh hồ sơ nháp

Khi character còn `Draft`, người dùng có thể sửa:

- tên và vai trò;
- mô tả nhận diện hình ảnh;
- trang phục;
- từ 1 đến 12 đặc điểm cố định;
- từ 1 đến 12 điều cấm thay đổi.

Tên tối đa 200 ký tự; vai trò tối đa 200; visual identity và wardrobe tối đa 4.000 ký tự; mỗi rule tối đa 500 ký tự. Character không còn `Draft` phải bị từ chối cập nhật.

### 8.4 Ảnh tham chiếu

Hiện hệ thống không tự sinh ảnh nhân vật. Người dùng chọn một file có sẵn từ máy:

- chỉ chấp nhận JPEG hoặc PNG thật, không chỉ dựa vào phần mở rộng;
- dung lượng từ 1 byte đến 10 MB;
- kiểm tra chữ ký file và đọc kích thước ảnh;
- tính SHA-256;
- sao chép file vào thư mục `characters` trong workspace của project;
- tạo `MediaAsset` loại `CharacterReference`, trạng thái `Ready`;
- tạo `CharacterReference` loại `Front`, `IsPrimary = true`, `ApprovalStatus = Approved`;
- ảnh primary cũ được bỏ cờ primary nhưng không bị xóa.

Chỉ character `Draft` được thay ảnh. Việc import ảnh thành công mới thay đổi primary reference trong database; nếu persist lỗi, file vừa copy được dọn lại.

### 8.5 Khóa nhân vật

Người dùng chỉ khóa character khi:

- character còn `Draft`;
- có một primary reference `Approved`;
- MediaAsset của ảnh ở trạng thái `Ready` và chưa bị xóa.

Khóa thành công chuyển character sang `Approved` và ghi thời điểm duyệt. Đây là thao tác một chiều trong version hiện tại; không có thao tác mở khóa để sửa tiếp.

### 8.6 Ánh xạ nhân vật vào scene

- OpenAI gắn `character_key` giống nhau vào mọi scene có cùng người dẫn.
- Desktop ánh xạ key thành Character ID đúng version và lưu vào scene.
- Scene có danh sách rỗng là scene không có nhân vật, được phép tạo text-to-video.
- Scene có một Character ID chỉ được tạo khi character `Approved` và có primary reference hợp lệ.
- Scene có trên một Character ID bị chặn vì model hiện hành chỉ hỗ trợ một nhân vật tham chiếu.

### 8.7 Prompt khóa nhân vật gửi Kling

Server không tin prompt character do desktop tự ghép. Server đọc snapshot character đã duyệt rồi tạo effective prompt theo thứ tự:

1. identity lock với tên/vai trò;
2. visual identity;
3. trang phục và phụ kiện đã khóa;
4. immutable traits;
5. forbidden changes;
6. yêu cầu khớp ảnh tham chiếu trong toàn clip;
7. prompt của scene;
8. negative prompt chung.

Effective prompt tối đa 3.072 ký tự. Khi vượt giới hạn, phần cuối bị cắt; vì vậy thay đổi cách ghép prompt phải có test đảm bảo thông tin nhận diện quan trọng không bị mất.

### 8.8 Cách Kling dùng ảnh

- Không có character reference: gọi luồng text-to-video.
- Kling thường có reference: gọi image-to-video và gửi ảnh dưới dạng `first_frame`.
- Model có mã chứa `omni`: gọi omni-video và gửi ảnh dưới dạng `refer_image`.

Provider/model và rate phải do quản trị viên cấu hình. Không tự chuyển sang model Omni hoặc tự đoán giá chỉ vì scene có nhân vật.

## 9. Nghiệp vụ storyboard và chỉnh scene

Storyboard hiện hành hiển thị cho mỗi cảnh:

- số thứ tự và đoạn thời gian;
- mục đích cảnh;
- lời đọc;
- mô tả hình ảnh;
- prompt Kling;
- nhân vật gắn với cảnh và trạng thái sẵn sàng;
- trạng thái tạo clip, lỗi gần nhất và preview cục bộ.

Người dùng chỉ được chỉnh lời đọc, mô tả hình ảnh và prompt khi:

- scene thuộc `CurrentScenePlanVersion`;
- scene chưa có `ApprovedGenerationId`;
- scene chưa có provider request nào ngoài trạng thái `Failed` hoặc `Cancelled`.

Khi lưu chỉnh sửa:

1. ScenePrompt hiện hành chuyển `Superseded`.
2. Tạo ScenePrompt version mới ở trạng thái `Approved`.
3. Scene trở lại `PromptReady` và xóa lỗi gần nhất.
4. Project trở lại `ScenePlanning`.

Việc sửa text của scene không làm thay đổi Character ID đã gắn với scene. Muốn thay cấu trúc nhân vật/cảnh phải sinh content version mới.

## 10. Luồng sinh clip Kling

### 10.0 Preflight media tool trước outbound

Trước khi desktop submit hoặc tiếp tục bất kỳ scene Kling nào, nó phải chạy `ffmpeg -version` và `ffprobe -version`. Hai công cụ ưu tiên được lấy từ bundle `tools/ffmpeg`; môi trường phát triển có thể ghi đè bằng `appsettings.user.json`, sau đó mới fallback sang `PATH`.

- Bundle phát hành phải có `LICENSE.txt`, `PROVENANCE.md`, `checksums.sha256` và `Approval scope: Release`; desktop update, installer và updater xác minh SHA-256 trước khi thay file. FFmpeg và FFprobe khác phiên bản hoặc bundle đi kèm thiếu/sai hồ sơ bị coi là chưa sẵn sàng.
- Thiếu media tool phải chặn trước outbound call để không phát sinh task/chi phí Kling không thể hoàn tất cục bộ.
- Project trở về `ScenePlanning`, giữ mã lỗi ổn định và UI phải hiển thị hướng xử lý cùng nút **Cài bộ xử lý video** và **Kiểm tra lại**.
- Nút cài đặt không tải FFmpeg trực tiếp từ website bên thứ ba. Desktop yêu cầu đúng package VideoMaker cùng version/build từ server, kiểm tra package và bundle, rồi dùng Updater hiện có để backup, thay file, rollback khi lỗi và khởi động lại ứng dụng.
- Nếu provider request đã `Completed` nhưng bước tải/probe từng lỗi, generation trở về `Generated`; retry đọc lại request hiện hữu và tải clip qua proxy, không submit Kling lần hai.
- File `.part` chỉ được đổi thành `.mp4` sau khi FFprobe xác nhận có video stream. Lỗi vẫn phải xóa file tạm, không xóa provider request hay usage đã quyết toán.

### 10.1 Chọn cảnh

- Người dùng có thể chọn một hoặc nhiều scene, tối đa 100 scene trong một lần thao tác.
- Scene đã có clip `Approved` được bỏ qua, không tạo lại và không tính là một outbound call mới.
- Nếu scene đã có provider request không phải `Failed`/`Cancelled`, desktop đọc trạng thái request hiện hữu thay vì submit task khác.
- Scene có character chưa sẵn sàng phải bị khóa nút tạo và vẫn bị server kiểm tra lại.

### 10.2 Dữ liệu desktop gửi

Với implementation hiện tại, desktop gửi:

- Project ID, Scene ID;
- prompt của scene;
- duration đúng theo scene;
- aspect ratio đúng theo project;
- resolution `720p`;
- native audio `true`;
- idempotency key chứa ScenePrompt ID, CharacterReference ID và attempt;
- ảnh reference Base64 nếu scene có character.

Trước khi gửi ảnh, desktop đọc đúng file trong workspace, kiểm tra size, MIME và tính lại SHA-256. File đã bị thay đổi sau lúc duyệt phải bị từ chối.

### 10.3 Server xác minh lại

Server kiểm tra:

- quyền truy cập và project ownership;
- scene thuộc project;
- duration và aspect ratio khớp dữ liệu hiện hành;
- ScenePrompt mới nhất ở trạng thái `Approved` hoặc `Ready`;
- scene có tối đa một character;
- character đúng project, đúng version đã gắn, đang `Approved`;
- CharacterReference ID khớp primary reference;
- MIME là JPEG/PNG, Base64 hợp lệ, size khớp và không quá 10 MB;
- chữ ký file và SHA-256 khớp MediaAsset đã duyệt.

Request log chỉ chứa Character ID/version, reference ID/hash và scene prompt version; không chứa Base64 ảnh.

### 10.4 Giữ ngân sách và submit

1. Server tạo request hash và xử lý idempotency trong phạm vi tổ chức.
2. Server chọn credential/rate Kling và quote theo `VideoSecond` của đúng biến thể `720p + Native Audio`.
3. Server reserve estimated cost trước outbound call.
4. Server ghi ProviderRequest và credential version.
5. Server gửi task Kling, lưu external task ID và trạng thái.
6. Nếu Kling thất bại/hủy, reservation được release.
7. Nếu hoàn tất, actual cost dùng reported cost khi có, nếu không dùng estimated cost đã khóa.

### 10.5 Polling và desktop đóng

`KlingPollingWorker` là chủ sở hữu polling trên server:

- chạy chu kỳ khoảng 10 giây;
- claim task đến hạn để tránh hai worker cùng xử lý;
- polling theo credential version đã gắn với request;
- cập nhật trạng thái, backoff khi lỗi tạm thời;
- settle hoặc release reservation ở trạng thái cuối.

API đọc status không tự polling provider để tránh hai đường cùng quyết toán chi phí.

Desktop có thể đóng sau khi submit; server vẫn hoàn tất polling và budget settlement. Tuy nhiên server không tự tải clip vào workspace desktop. Khi desktop mở lại và chạy tiếp luồng scene, nó đọc request hiện hữu, nhận trạng thái `Completed` rồi tải clip qua proxy.

### 10.6 Tải và duyệt clip

Khi task hoàn tất:

1. Server trả URL proxy tương đối `/api/generation/kling/videos/{providerRequestId}/content`.
2. Proxy xác thực lại user, license, tổ chức và project; chặn SSRF/DNS rebinding, redirect nguy hiểm, MIME sai và file quá 1 GB.
3. Desktop tải vào file `.part`, hoàn tất mới đổi sang `.mp4` trong thư mục `scenes`.
4. Desktop tính SHA-256 và tạo/cập nhật `MediaAsset` loại `SceneVideo`.
5. `VideoGeneration` chuyển `Approved`, gắn `OutputMediaAssetId`.
6. Scene gắn `ApprovedGenerationId` và chuyển `Approved`.
7. Nếu mọi scene của `CurrentScenePlanVersion` đã có approved generation, project chuyển `ReadyToRender`; nếu chưa đủ, project trở lại `ScenePlanning`.

### 10.7 Nghiệp vụ âm thanh đã chốt — chưa triển khai

Âm thanh đầu ra phải được tạo từ hai luồng chạy cùng nhau:

```text
Scene visual prompt
  -> Kling với NativeAudio = true
  -> clip có ambience/SFX/chuyển động/hội thoại của cảnh

Scene.Narration đã duyệt
  -> TOOL-SERVER kiểm tra quyền/rate/budget/idempotency
  -> dịch vụ TTS
  -> VoiceGeneration + MediaAsset audio
  -> desktop tải qua API xác thực vào workspace

clip Kling + native audio + TTS voice-over
  -> FFmpeg mix/ducking/căn timeline
  -> clip preview đã mix hoặc video cuối
```

Các quy tắc bắt buộc:

- Desktop phải gửi `NativeAudio = true` cho luồng Kling mới; server vẫn xác minh và ghi giá trị này vào request hash, request log, usage và rate snapshot.
- Prompt Kling mô tả ambience, sound effect và hội thoại nhân vật khi cần. `Scene.Narration` không được chèn để Kling đọc lại nếu narration đã đi qua TTS.
- TTS phải đọc đúng narration của scene/version đã duyệt, không tự viết lại nội dung.
- Mỗi kết quả TTS phải truy được về organization, user, project, script/version, scene/cue, model, voice setting, provider request, credential version và rate snapshot.
- Audio TTS được lưu thành `MediaAsset` trong workspace và liên kết bằng `VoiceGeneration`; file phải có MIME, size, duration, sample rate và SHA-256 đã xác minh.
- Voice-over TTS là lớp lời dẫn chính. Khi voice-over hoạt động, audio native Kling phải giảm âm lượng có kiểm soát; không để narration và hội thoại cùng nội dung chồng lên nhau.
- Nếu clip Kling không có audio stream, FFmpeg dùng nền im lặng và vẫn ghép voice-over. Nếu scene không có narration, giữ audio native Kling và không tạo TTS rỗng.
- Nếu TTS thất bại, không xóa hoặc submit lại clip Kling đã hoàn tất. Retry chỉ tác động TTS bằng cùng idempotency key hoặc attempt hợp lệ theo trạng thái cuối.
- Nếu mix/render cục bộ thất bại, retry từ asset đã có; không gọi lại Kling/TTS và không phát sinh usage mới.
- Preview phải nói rõ đang phát clip Kling gốc hay bản đã mix voice-over; không hiển thị text “Lời đọc” như thể audio đã được tạo thành công.

### 10.8 Pricing, budget và idempotency cho audio

- Kling native audio có thể có đơn giá khác audio-off. Quote phải dùng rate/capability đúng với `NativeAudio = true`; thiếu rate phải trả `pricing_not_configured` trước outbound call.
- TTS phải có provider/model/credential/rate riêng được quản trị tại server. Không hard-code giá và không gọi TTS trực tiếp từ desktop.
- Provider/model/voice TTS cụ thể là quyết định cấu hình còn phải chốt trước khi code; không tự phục hồi BYOK hoặc dùng credential máy người dùng.
- Idempotency TTS tối thiểu phải khóa theo organization, project, script/version, scene/cue, narration hash, language, voice và model. Cùng key/cùng hash trả kết quả cũ; cùng key/khác nội dung trả `idempotency_key_conflict`.
- Settlement TTS dùng actual usage khi provider trả về; nếu không có usage, áp dụng chính sách estimate đã được chốt cho model, không ghi chi phí bằng `0` tùy tiện.
- Native audio Kling và TTS phải có reservation/usage ledger riêng để audit và không che lẫn chi phí.

## 11. Trạng thái nghiệp vụ chính

### Project

```text
Draft
  -> ContentPlanning
  -> ScenePlanning
  -> GeneratingScenes
  -> ScenePlanning     (mới hoàn tất một phần cảnh)
  -> ReadyToRender     (mọi cảnh hiện hành đã có clip)

Bất kỳ bước generation nào lỗi có thể đưa project -> Failed.
```

### Scene và VideoGeneration

```text
Scene: PromptReady -> WaitingProvider -> Generated -> Approved
                         |                 |
                         +----> Failed/Cancelled

VideoGeneration: WaitingProvider -> Generated -> Downloading -> Approved
                                +-> Failed/Cancelled
```

Trạng thái `Completed` là trạng thái provider request trên server. Scene chỉ thành `Approved` sau khi desktop tải file, tạo MediaAsset và liên kết generation thành công.

## 12. Idempotency, retry và tạo lại

### Content

- Key theo project và version.
- Cùng key, cùng request hash: dùng lại kết quả request cũ khi đã completed.
- Cùng key nhưng payload/project khác: trả `idempotency_key_conflict`.

### Kling

- Key chứa ScenePrompt ID, CharacterReference ID và attempt.
- Request đang chạy/hoàn tất được tái sử dụng, không submit trùng.
- Chỉ tạo attempt mới sau request `Failed` hoặc `Cancelled`.
- Scene đã `Approved` bị bỏ qua trong thao tác tạo hàng loạt.

Không được xử lý retry bằng cách sinh idempotency key ngẫu nhiên cho cùng một thao tác; cách đó có thể giữ budget và tạo task provider nhiều lần.

## 13. Lỗi nghiệp vụ cần hiển thị có hướng dẫn

| Mã/nhóm lỗi | Ý nghĩa và hướng xử lý |
|---|---|
| `organization_generation_denied` | Role không được dùng AI; quản trị viên cần đổi role nếu phù hợp. |
| `organization_budget_exceeded` | Budget tổ chức không đủ hoặc bằng 0; mở trang cấu hình budget. |
| `member_budget_exceeded` | Thành viên đã hết hạn mức tháng. |
| `pricing_not_configured` | Model thiếu rate bắt buộc; Global Admin cấu hình rate. |
| `openai_not_configured` | Thiếu model/credential/rate OpenAI Active. |
| `kling_not_configured` | Thiếu model/credential/rate Kling Active. |
| `scene_prompt_not_ready` | Scene chưa có prompt hợp lệ. |
| `character_not_ready` | Character chưa khóa hoặc chưa có primary reference. |
| `character_reference_required` | Request không mang đúng ảnh primary đã duyệt. |
| `idempotency_key_conflict` | Key cũ đang đại diện cho payload khác; không được tự động retry mù. |
| `kling_generation_failed` | Task Kling thất bại; giữ thông tin lỗi an toàn và cho phép attempt mới. |
| `ffmpeg_not_found` | Desktop chưa tìm thấy FFmpeg; cài/bundle đúng bộ media tool rồi bấm kiểm tra lại. |
| `ffprobe_not_found` | Desktop chưa tìm thấy FFprobe; cài/bundle đúng bộ media tool rồi bấm kiểm tra lại. |
| `media_tool_not_executable` | Windows không cho chạy executable; kiểm tra quyền file hoặc cài lại bundle. |
| `media_tool_version_check_failed` | Media tool tồn tại nhưng không trả kết quả kiểm tra phiên bản hợp lệ. |
| `media_tool_version_mismatch` | FFmpeg và FFprobe không cùng phiên bản; cài lại trọn bộ đã được duyệt. |
| `media_tool_bundle_invalid` | Bundle đi kèm thiếu license/provenance/checksum hoặc SHA-256 không khớp; chạy sửa chữa từ package VideoMaker hợp lệ. |
| `media_tool_repair_package_not_found` | Server không còn package đúng version/build đang chạy; cài lại bản đầy đủ hoặc liên hệ quản trị viên. |

UI nên có link trực tiếp đến đúng màn hình setup khi lỗi thuộc budget, rate hoặc credential; không chỉ hiển thị một banner đỏ không có hành động.

## 14. Các giới hạn hiện tại phải nói rõ

- Chỉ tối đa một nhân vật lặp lại trong content plan và một nhân vật tham chiếu trong mỗi scene.
- Ảnh nhân vật có thể được import hoặc sinh bằng GPT-Image-2 qua server; cả hai luồng đều phải preview, duyệt và khóa primary reference trước khi dùng cho Kling.
- Không tự kế thừa ảnh khi sinh content version mới.
- Character đã khóa không thể mở khóa/chỉnh; phải sinh content version mới.
- Luồng content tự động tối đa 360 giây dù project model cho phép duration dài hơn.
- Desktop hiện gửi Kling ở `720p` với native audio; server từ chối biến thể khác và chặn trước outbound nếu thiếu rate đúng metadata.
- Source hiện chưa có TTS gateway thực thi, chưa tạo voice asset và chưa mix voice-over; các entity/job voice hiện có không được coi là bằng chứng tính năng đã hoàn tất.
- Server polling Kling khi desktop đóng, nhưng desktop phải mở lại để tải clip vào workspace và duyệt scene cục bộ.
- Đồng bộ ở đây khóa nhận diện hình ảnh và ảnh tham chiếu; chưa được hiểu thành tính năng voice clone, lip-sync, motion identity hoặc diễn viên nhiều góc nhìn.
- Dữ liệu workflow vẫn đi qua SQL trực tiếp từ desktop trong giai đoạn chuyển tiếp.

## 15. Checklist nghiệm thu nghiệp vụ

Một thay đổi liên quan sinh video/nhân vật chỉ được coi là đạt khi kiểm tra tối thiểu:

1. User ngoài tổ chức, Viewer, license hết hạn và project không thuộc user đều bị chặn trước provider.
2. Budget bằng 0, thiếu credential hoặc thiếu rate không tạo outbound request.
3. Thiếu FFmpeg/FFprobe chặn trước outbound Kling; sau khi cài tool, request Kling đã `Completed` được tiếp tục tải mà không tạo request hoặc usage mới.
4. Content plan sai schema, trùng CharacterKey hoặc scene tham chiếu key không tồn tại bị từ chối.
5. Sinh lại content tạo version mới và không xóa usage/artifact version cũ.
6. Character Draft sửa được; Approved và Superseded không sửa/thay ảnh được.
7. Không khóa character nếu thiếu primary reference hợp lệ.
8. Scene có character chưa Approved không tạo được Kling; scene B-roll không bị yêu cầu ảnh.
9. Ảnh bị thay file, sai MIME, sai signature, sai size hoặc sai SHA-256 bị server từ chối.
10. Effective prompt giữ identity, wardrobe, immutable traits và forbidden changes.
11. Cùng idempotency key không tạo hai provider request/reservation.
12. Desktop đóng không làm dừng polling Kling.
13. URL gốc Kling và Base64 reference không xuất hiện trong response/log desktop.
14. Clip chỉ được đánh dấu Approved sau khi tải và tạo MediaAsset thành công.
15. Build/test Release đạt theo hướng dẫn repository sau mọi thay đổi source.
16. Request Kling mới mang `NativeAudio = true` và thiếu rate native audio bị chặn trước outbound call.
17. TTS đọc đúng narration/version/voice đã chọn; cùng idempotency key không tạo hai voice asset hoặc hai ledger entry.
18. Clip/video mix có audio stream hợp lệ; voice-over rõ, audio Kling được ducking và scene không narration không tạo TTS rỗng.
19. TTS lỗi không làm mất clip Kling; mix lỗi không gọi lại bất kỳ provider nào.
20. Desktop, bundle và log không chứa TTS credential hoặc URL output gốc của provider.

## 16. Điểm vào source cần đọc trước khi sửa

- Contract generation: `TOOL-SHARED.Contracts/Generation/GenerationContracts.cs`.
- OpenAI schema và mapping content plan: `TOOL-SERVER/Generation/OpenAiContentClient.cs`.
- Access, budget, character verification và effective prompt: `TOOL-SERVER/Generation/GenerationService.cs`.
- Payload text/image-to-video/Omni: `TOOL-SERVER/Generation/KlingVideoClient.cs`.
- Polling nền: `TOOL-SERVER/Generation/KlingPollingWorker.cs`.
- Persist content, submit/download/approve clip: `TOOL-LOCAL/Generation/ProjectGenerationService.cs`.
- Resolve và preflight FFmpeg/FFprobe: `TOOL-LOCAL/Media/MediaToolPreflightService.cs`.
- Sửa character, import ảnh, khóa character và sửa scene: `TOOL-LOCAL/Projects/ProjectService.cs`.
- DTO dashboard desktop: `TOOL-LOCAL/Projects/ProjectContracts.cs`.
- WebView bridge: `TOOL-LOCAL/WebView/DashboardBridge.cs`.
- Storyboard/character UI: `TOOL-LOCAL/Web/src/App.tsx` và `TOOL-LOCAL/Web/src/types.ts`.
- Nghiệp vụ toàn hệ thống: `NGHIEP_VU_HE_THONG_VIDEOMAKER.md`.
- Runbook budget/rate/credential: `TRIEN_KHAI_AI_GATEWAY_TO_CHUC.md`.

Nếu source và tài liệu này khác nhau, source code/migration là sự thật kỹ thuật; phải cập nhật lại tài liệu trong cùng thay đổi để tránh AI agent sau đó phục hồi hành vi cũ hoặc tự thêm đường gọi provider sai kiến trúc.
