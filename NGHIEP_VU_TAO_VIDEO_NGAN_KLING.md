# Nghiệp vụ tạo video ngắn bằng Kling

> Phạm vi: màn hình **Tạo video** tạo một clip duy nhất từ nội dung người dùng nhập.
>
> Cập nhật ngữ cảnh: 2026-08-31. Tài liệu này mô tả màn hình direct prompt một scene đã có trong source, bổ sung cho [Nghiệp vụ hệ thống VideoMaker](NGHIEP_VU_HE_THONG_VIDEOMAKER.md) và không thay thế nghiệp vụ dự án nhiều cảnh bằng OpenAI. Khi có khác biệt, tài liệu nghiệp vụ hệ thống và source hiện hành được ưu tiên.

## 1. Mục đích

Tính năng cho phép người dùng tạo nhanh một clip Kling ngắn mà không cần đi qua các bước sinh content plan, kịch bản, storyboard hoặc prompt bằng OpenAI.

Người dùng mô tả trực tiếp cảnh muốn tạo, chọn tỉ lệ khung hình, thời lượng và có hoặc không giữ âm thanh ở file kết quả. Hệ thống tạo một project gồm đúng một scene, gửi request tạo video qua AI Gateway của tổ chức, tải kết quả về workspace cục bộ và hiển thị để xem trước.

Tính năng phù hợp với các video ngắn độc lập như Shorts, Reels, minh họa một cảnh hoặc thử nghiệm ý tưởng hình ảnh. Đây **không phải** luồng tạo nội dung nhiều cảnh của VideoMaker.

## 2. Nguyên tắc và ranh giới

- Provider của luồng này là **Kling**. Desktop đọc video policy của tổ chức và chặn thao tác nếu policy hiện hành không phải Kling; người dùng desktop không tự chọn provider, model hoặc nhập API key.
- Nội dung người dùng nhập đi thẳng vào prompt hình ảnh của scene. Luồng này **không gọi OpenAI** để viết lại, dịch hoặc tự sinh kịch bản.
- Hệ thống vẫn phải đi qua `TOOL-SERVER`: kiểm tra quyền, giá, ngân sách, idempotency, credential và log usage trước khi gọi Kling.
- API key Kling chỉ tồn tại ở server dưới dạng credential mã hóa. Desktop không nhận key, URL output gốc hoặc secret provider.
- Mỗi lần tạo là một project độc lập có một scene. Không có nhân vật cố định, ảnh tham chiếu, thoại hay voice-over trong màn hình này.
- Video tạo ra phải được kiểm tra cục bộ trước khi được coi là đầu ra hợp lệ.

## 3. Đối tượng và quyền

Người dùng phải đăng nhập hợp lệ, có device claim/lease hợp lệ, chọn một tổ chức và là thành viên Active của tổ chức đó.

Các role có thể phát sinh chi phí AI là `Owner`, `OrganizationAdmin`, `BillingManager` và `Member`. `Viewer` không được tạo video.

Ngoài quyền của người dùng, tổ chức phải có:

- Kling provider và model video Active theo policy;
- credential Kling Active, đã được server kiểm tra;
- rate Active cho biến thể video cần dùng;
- budget tháng tổ chức và hạn mức thành viên còn đủ;
- media tools cục bộ (`ffmpeg` và `ffprobe`) sẵn sàng.

Nếu một điều kiện không đạt, hệ thống dừng trước khi tạo outbound request tới Kling và trả thông báo nghiệp vụ có thể hiểu được. Ví dụ: chưa chọn tổ chức, provider inactive, thiếu rate, budget không đủ hoặc media tool chưa sẵn sàng.

## 4. Dữ liệu người dùng nhập

| Trường | Quy tắc hiện tại | Ý nghĩa |
|---|---|---|
| Nội dung dùng để tạo video | Bắt buộc, 1–2.000 ký tự | Mô tả trực tiếp cho cảnh: chủ thể, bối cảnh, hành động, góc máy, ánh sáng, phong cách… |
| Tỉ lệ khung hình | `9:16`, `16:9`, `1:1` | Tỉ lệ video đầu ra và project mới |
| Thời lượng đầu ra | Số nguyên 5–15 giây | Thời lượng file người dùng nhận |
| Âm thanh | Bật hoặc tắt; mặc định bật | Quy định file kết quả có giữ audio stream hay không |

Ví dụ nội dung phù hợp:

> Một cô gái mặc áo dài xanh bước chậm giữa phố cổ Hội An lúc bình minh, máy quay dolly lùi mượt, đèn lồng lay nhẹ trong gió, phong cách điện ảnh chân thực.

Nội dung nên tập trung vào **hình ảnh, hành động và máy quay**. Không dùng ô này như lời thoại cần đọc, vì luồng video ngắn không có cấu hình nhân vật nói hoặc lời dẫn.

## 5. Vòng đời thao tác của người dùng

```text
Nhập nội dung
  → chọn tỉ lệ, thời lượng, âm thanh
  → bấm “Tạo video”
  → xem và xác nhận thông tin/chi phí ước tính
  → hệ thống kiểm tra điều kiện tạo AI
  → tạo project một scene
  → server gửi task cho Kling
  → Kling/worker xử lý bất đồng bộ
  → desktop tải video qua proxy server
  → FFmpeg xử lý và kiểm tra output
  → hiển thị preview clip
```

### 5.1. Xác nhận trước khi tạo

Trước khi gửi request, giao diện hiển thị:

- Kling/model/resolution theo policy hiện hành;
- tỉ lệ và thời lượng người dùng đã chọn;
- trạng thái âm thanh đầu ra;
- phần xem trước nội dung đã nhập;
- chi phí ước tính nếu rate hiện hành cho phép tính;
- lưu ý về thời lượng tối thiểu của provider và chi phí.

Người dùng chỉ phát sinh request sau khi xác nhận. Mục tiêu là tránh tạo video nhầm và làm rõ rằng chi phí thuộc budget của tổ chức.

### 5.2. Kiểm tra ở desktop

Desktop kiểm tra dữ liệu cơ bản trước khi gửi:

- Nội dung sau khi trim không được rỗng và không vượt 2.000 ký tự.
- Tỉ lệ phải thuộc ba giá trị được hỗ trợ.
- Thời lượng phải là số nguyên từ 5 đến 15.
- Không tạo song song khi đang có tác vụ generation chạy.
- Người dùng phải chọn tổ chức, Kling phải sẵn sàng và FFmpeg/FFprobe phải dùng được.

Các kiểm tra này là để trải nghiệm tốt hơn. Chúng không thay thế kiểm tra bảo mật/ngân sách ở server.

## 6. Prompt và cách Kling nhận nội dung

### 6.1. Nội dung không qua OpenAI

Nội dung người dùng nhập trở thành `FinalPrompt` của scene. Hệ thống không gọi OpenAI để tóm tắt, mở rộng, dịch hoặc thay đổi ý định hình ảnh của người dùng.

Khi tạo project, hệ thống lưu nội dung này đồng thời ở topic, script một cảnh, mô tả hình ảnh và prompt của scene. Prompt được đánh dấu đã duyệt để có thể gửi sang provider ngay.

### 6.2. Server bọc prompt kỹ thuật

Server không tin prompt do desktop tự khai báo là đủ cho provider. Server nạp prompt đã lưu theo project/scene/version, sau đó bọc thêm ràng buộc kỹ thuật dành cho Kling, bao gồm:

- một shot điện ảnh liên tục, đúng thời lượng và đúng tỉ lệ đã chọn;
- quy tắc Native Audio không có người nói/lời dẫn;
- âm thanh môi trường và hiệu ứng hành động phù hợp khi giữ âm thanh;
- negative constraints: không text overlay, subtitle, watermark, logo, lỗi giải phẫu, nhân vật trùng, flicker hoặc visual artifact;
- phần nội dung của người dùng chỉ được coi là dữ liệu về scene, action và camera; chỉ dẫn thoại/âm thanh bên trong phần này không được dùng để cấu hình speech.

Vì vậy, dòng UI “gửi thẳng vào prompt Kling, không qua OpenAI” có nghĩa là nội dung **không bị OpenAI biên tập**, chứ không có nghĩa desktop gửi raw prompt trực tiếp tới API Kling. Server vẫn có trách nhiệm bọc prompt, bảo vệ credential và áp dụng policy.

### 6.3. Ngôn ngữ tiếng Việt

Người dùng có thể viết mô tả hình ảnh bằng tiếng Việt. Tuy nhiên, độ bám prompt đối với hướng dẫn hình ảnh/camera phức tạp thường ổn định hơn khi dùng tiếng Anh hoặc Việt–Anh song song. VideoMaker hiện không tự dịch prompt tiếng Việt sang tiếng Anh.

Luồng này không hỗ trợ lời thoại. Nếu người dùng yêu cầu nhân vật đọc một câu tiếng Việt trong ô nội dung, yêu cầu đó không trở thành thoại chính thức của Kling và không có bảo đảm về phát âm/lip-sync.

Theo tài liệu Kling VIDEO 3.0 tại thời điểm viết, các ngôn ngữ công bố cho **dialogue output** là Trung, Anh, Nhật, Hàn và Tây Ban Nha; ngôn ngữ khác có thể bị dịch sang tiếng Anh. Đây là giới hạn của provider, không phải cơ chế dịch của VideoMaker. Xem [Kling VIDEO 3.0 Model User Guide](https://app.klingai.com/cn/quickstart/klingai-video-3-model-user-guide).

## 7. Thời lượng

Người dùng chọn thời lượng đầu ra từ 5 đến 15 giây. Khoảng này khớp trực tiếp với ràng buộc `TargetDurationSeconds` của database workflow.

| Thời lượng người dùng chọn | Thời lượng gửi Kling | Xử lý đầu ra |
|---:|---:|---|
| 5–15 giây | Bằng thời lượng đã chọn | Giữ nguyên thời lượng provider trả về, sau khi kiểm tra |

Chi phí được quote theo đúng thời lượng provider thực tế đã chọn trong khoảng 5–15 giây.

File tải về phải có video stream và thời lượng nằm trong sai số kỹ thuật cho phép quanh thời lượng yêu cầu. Nếu không đạt, video bị coi là không hợp lệ và không được duyệt đầu ra.

## 8. Âm thanh

### 8.1. Khi bật âm thanh

- Server gọi Kling với biến thể Native Audio của policy hiện tại.
- Prompt yêu cầu âm thanh môi trường và hiệu ứng tự nhiên đồng bộ hành động, không có thoại/lời dẫn.
- Desktop kiểm tra audio stream và phân tích mức âm thanh trước khi đưa clip vào trạng thái cần duyệt/hoàn tất theo workflow.

### 8.2. Khi tắt âm thanh

- Server vẫn dùng biến thể Native Audio trong policy hiện tại để tạo clip. Việc tắt âm thanh **không** làm giảm chi phí Kling của luồng hiện tại.
- Khi desktop nhận clip, FFmpeg loại bỏ hoàn toàn audio stream khỏi file MP4.
- Desktop kiểm tra lại để chắc chắn output chỉ có video stream. Clip silent được tự động duyệt vì không còn bước nghe audio.

Không được chỉ tắt âm lượng ở giao diện hoặc gắn cờ metadata mà giữ audio stream trong file. “Tắt âm thanh” có nghĩa output không chứa audio stream.

## 9. Tạo project và scene một cảnh

Sau khi tất cả điều kiện đã đạt, desktop tạo dữ liệu workflow cục bộ:

- một project mới, gắn `OrganizationId` đang chọn;
- một concept/script/style profile ở trạng thái đã duyệt;
- đúng một scene với sequence number là 1;
- một scene prompt manual, version 1, đã duyệt;
- project không yêu cầu duyệt content/storyboard trước khi tạo video;
- scene không có character reference, dialogue hoặc narration.

Tên project được tạo từ nội dung đã nhập, có chuẩn hóa khoảng trắng và giới hạn độ dài. Project mang tỉ lệ, kích thước output và thời lượng tương ứng với lựa chọn của người dùng.

Project và scene lưu đồng thời hai khái niệm thời lượng:

- `ContentDuration`: thời lượng file đầu ra người dùng yêu cầu;
- `GenerationDuration`: thời lượng thực gửi provider, bằng thời lượng người dùng chọn trong luồng video ngắn 5–15 giây.

Hai giá trị bằng nhau trong luồng video ngắn hiện tại.

## 10. AI Gateway, chi phí và an toàn

Khi desktop yêu cầu tạo video, request chỉ mang danh tính project/scene, version prompt và idempotency key. Server tự nạp snapshot project để biết provider, model, độ phân giải và biến thể audio. Desktop không được phép ghi đè các giá trị đó.

Trước outbound call, server bắt buộc:

1. Xác thực JWT, session, device claim, license lease, organization membership, role và quyền sở hữu project.
2. Xác minh scene và prompt version hiện hành để chặn tạo video từ dữ liệu cũ.
3. Kiểm tra model/provider Active, credential Active và rate Active.
4. Quote chi phí bằng rate snapshot.
5. Giữ ngân sách tổ chức/hạn mức thành viên bằng transaction cô lập `Serializable`.
6. Ghi provider request/audit theo organization, user, project, scene, model, credential version và idempotency key.
7. Chỉ sau đó mới gọi Kling qua HTTPS và allowlist của server.

Budget bằng `0` nghĩa là khóa AI. Thiếu rate trả lỗi `pricing_not_configured`; không được tự đoán giá hoặc gọi Kling trước rồi mới tính tiền.

Khi provider hoàn tất, server quyết toán usage bằng rate snapshot đã giữ cho request đó. Nếu request thất bại/cancel/expired, reservation được release theo nghiệp vụ gateway.

## 11. Xử lý bất đồng bộ và đầu ra

1. Kling nhận request và có thể trả task đang xử lý.
2. Server worker là chủ sở hữu polling; worker tiếp tục theo dõi task ngay cả khi desktop đã đóng.
3. Khi Kling hoàn tất, server cache output, kiểm tra giới hạn an toàn và chỉ cung cấp URL proxy có xác thực cho desktop. URL gốc của provider không được trả về desktop.
4. Desktop tải file vào workspace bằng cơ chế `.part` để tránh ghi file dở dang.
5. Desktop dùng `ffprobe` kiểm tra video stream; dùng `ffmpeg` bỏ audio nếu người dùng tắt âm thanh.
6. Desktop kiểm tra cuối cùng về video stream, thời lượng, audio stream và chất lượng audio phù hợp với lựa chọn của người dùng.
7. File hợp lệ được ghi là `SceneVideo` trong workspace và hiển thị ở khung preview.

Nếu desktop đóng sau khi task đã được gửi, việc tạo/polling ở server vẫn diễn ra. Khi mở lại desktop, người dùng có thể tiếp tục tải clip đã hoàn tất qua server.

## 12. Trạng thái và kết quả người dùng nhìn thấy

| Mốc | Ý nghĩa |
|---|---|
| Đang tạo workflow | Project và scene một cảnh vừa được tạo |
| Kling đang xử lý clip | Provider request đã được gửi, đang polling |
| Đã tải xong clip | Server có output và desktop đã tải file về để xử lý |
| Sẵn sàng / cần kiểm tra audio | File hợp lệ, tùy chiến lược audio mà có bước nghe duyệt hoặc tự duyệt |
| Sẵn sàng render | Project đủ điều kiện dựng video cuối nếu người dùng cần |
| Lỗi | Có lỗi nghiệp vụ/provider/media; phải hiển thị thông báo dễ hiểu, không để exception thô làm crash UI |

Các lỗi phải được chuyển thành thông báo UI, ví dụ: provider chưa active, thiếu rate, budget không đủ, request hết hạn, thiếu media tool, clip không có video stream, sai thời lượng hoặc audio output không đúng lựa chọn. Không được để `throw` chưa xử lý làm dừng ứng dụng desktop.

## 13. Tiêu chí hoàn tất

Một yêu cầu tạo video ngắn được coi là hoàn tất khi:

- Clip thuộc đúng project/scene/organization của người dùng;
- Video có đúng tỉ lệ và thời lượng đầu ra đã yêu cầu;
- Khung preview giữ đúng tỉ lệ project `9:16`, `16:9` hoặc `1:1`, không kéo méo hay cắt nội dung video;
- Video chứa stream hình hợp lệ;
- Khi bật âm thanh: file còn audio stream hợp lệ để phát/kiểm tra;
- Khi tắt âm thanh: file không còn audio stream;
- Hash, MIME, kích thước và metadata output đã được ghi nhận;
- Chi phí/usage của provider request đã được quyết toán hoặc được xử lý theo trạng thái terminal;
- Preview có thể phát trong desktop qua file workspace cục bộ.

## 14. Ngoài phạm vi hiện tại

Các hạng mục sau không thuộc màn hình tạo video ngắn trực tiếp và không được tự suy diễn khi bảo trì tính năng:

- OpenAI tự phân tích chủ đề hoặc viết lại prompt;
- tạo nhiều cảnh, kịch bản, storyboard hay continuity giữa cảnh;
- tạo/chọn/khóa nhân vật và ảnh tham chiếu;
- lời thoại trực tiếp, voice-over, TTS/WAV fallback;
- cho desktop nhập key, chọn provider/model hoặc gọi API Kling trực tiếp;
- dùng URL output gốc của Kling tại desktop;
- tự cấu hình giá provider hoặc bỏ qua budget/rate để tạo video.

## 15. Hướng dẫn cho AI/agent bảo trì

Khi thay đổi luồng này, AI/agent phải bảo toàn các bất biến sau:

1. `TOOL-SHARED.Contracts` thay đổi trước nếu thay hợp đồng public; sau đó cập nhật server, desktop và test đồng thời.
2. Không chuyển prompt qua OpenAI nếu nghiệp vụ vẫn là “direct prompt”; nếu muốn thêm bước tối ưu/biên dịch prompt, đó là một nghiệp vụ mới cần người dùng phê duyệt rõ ràng.
3. Không cho desktop tự chọn provider/model/credential và không thêm HTTP client gọi provider tại `TOOL-LOCAL`.
4. Không đưa API key, URL provider gốc, raw provider payload hoặc prompt nhạy cảm vào UI/log không được phép.
5. Duy trì kiểm tra server-side cho quyền, ownership, snapshot version, rate, budget reservation, idempotency và settlement.
6. Nếu đổi biến thể provider thành “no native audio” thực sự, phải cập nhật đồng thời policy snapshot, rate, quote, settlement, prompt, UI và test; không được giả định việc tắt audio hậu xử lý tự làm giảm giá.
7. Nếu mở hỗ trợ lời thoại tiếng Việt, phải đánh giá capability/rate thực tế của model Kling và thêm luồng speech có cấu trúc; không diễn giải prompt hình ảnh là lời thoại.
8. Mọi lỗi phải được đưa về thông báo nghiệp vụ ở UI; exception chỉ là cơ chế nội bộ, không phải trải nghiệm người dùng cuối.

## 16. Kịch bản kiểm thử nghiệp vụ tối thiểu

- Tạo clip `9:16`, 15 giây, giữ âm thanh: có một project/scene, video phát được và có audio stream.
- Tạo clip 5 giây: project/scene được lưu thành công và provider nhận đúng thời lượng 5 giây.
- Nhập hoặc gửi thời lượng dưới 5 giây hay trên 15 giây: bị chặn trước khi tạo workflow và trước outbound call.
- Tạo clip 15 giây, tắt âm thanh: file thành phẩm có video stream nhưng không có audio stream.
- Chọn Viewer, budget bằng 0, vượt member limit, thiếu rate hoặc provider inactive: bị chặn trước outbound call và nhận thông báo rõ ràng.
- Đóng desktop sau khi gửi task: worker server vẫn hoàn tất polling; mở lại có thể tải output qua proxy.
- Provider trả file lỗi/không có video/audio sai kỳ vọng: desktop không duyệt clip và hiển thị lỗi có thể hành động.
- Gửi lại cùng idempotency key/payload: không phát sinh request Kling hoặc chi phí trùng.
