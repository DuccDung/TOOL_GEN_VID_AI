# Kế hoạch tích hợp Fal/Veo cho video dài

> Trạng thái: **Đang triển khai**  
> Ngày lập kế hoạch: 2026-09-01  
> Phạm vi: chỉ workflow video dài `OpenAiStructuredPlan`  
> Mục đích: lưu đầy đủ context để một phiên AI khác có thể tiếp tục triển khai mà không phải nghiên cứu lại từ đầu.

## 1. Mục tiêu

Tích hợp Fal làm provider video mới trên `TOOL-SERVER`, dùng Veo 3.1 để tạo clip cho từng cảnh của video dài, có Native Audio và ưu tiên nhân vật nói tiếng Việt trực tiếp.

Kết quả cần đạt:

- Veo 3.1 Standard Image-to-Video là lựa chọn chất lượng chính.
- Veo 3.1 Fast Image-to-Video là lựa chọn tiết kiệm.
- Server giữ `FAL_KEY`, submit Queue API, polling khi desktop đóng và cache output.
- Desktop tiếp tục dùng API video trung lập, không biết key, endpoint hoặc URL output gốc.
- Không fallback ngầm giữa Standard/Fast, Veo/Kling, Image-to-Video/Text-to-Video hoặc Native Audio/TTS.
- Clip im lặng hoặc nhân vật chỉ đứng cười không được tự động duyệt.
- Kling và project cũ không bị thay đổi provider.

## 2. Tài liệu phải đọc trước khi triển khai

AI tiếp quản task phải đọc các file sau trước khi sửa source:

1. `AGENTS.md` và `AGENTS.md` gần nhất trong thư mục sẽ sửa.
2. `README.md`.
3. `NGHIEP_VU_HE_THONG_VIDEOMAKER.md`.
4. `KE_HOACH_SERVER_AI_GATEWAY.md`.
5. `TRIEN_KHAI_AI_GATEWAY_TO_CHUC.md`.
6. `KE_HOACH_KLING_NHAN_VAT_NOI_TRUC_TIEP_VIDEO_DAI.md`.
7. `KE_HOACH_KLING_NOI_DUNG_TIENG_VIET.md`.

Tài liệu Fal chính thức cần kiểm tra lại tại thời điểm bắt đầu code vì endpoint, schema và giá có thể thay đổi:

- Standard I2V: <https://fal.ai/models/fal-ai/veo3.1/image-to-video/api>
- Fast I2V: <https://fal.ai/docs/model-api-reference/video-generation-api/veo3.1-fast>
- Queue API: <https://fal.ai/docs/documentation/model-apis/inference/queue>
- File input/CDN: <https://fal.ai/docs/documentation/model-apis/fal-cdn>
- Data retention: <https://fal.ai/docs/documentation/model-apis/media-expiration>
- Authentication: <https://fal.ai/docs/documentation/setting-up/authentication>

Không sao chép giá công khai trong tài liệu này vào production. Rate phải lấy lại từ Fal dashboard, Platform Pricing API hoặc hợp đồng của đúng tài khoản tại thời điểm rollout.

## 3. Kiến trúc hiện tại có thể tái sử dụng

Source hiện tại đã có nền tảng video đa provider:

- `IVideoProviderClient` và `VideoProviderRouter` route Kling/BytePlus theo provider snapshot.
- `ProjectVideoPolicyResolver` snapshot provider, model, policy version, resolution và Native Audio vào project.
- `GenerationService` xác minh quyền, scene/prompt/reference version, idempotency, rate và budget trước outbound.
- `VideoPollingWorker` tiếp tục polling task khi desktop đóng.
- `IVideoOutputStore` tải output về cache server, kiểm tra URL/MIME/kích thước và chỉ trả URL proxy tương đối.
- Usage ledger truy được theo tổ chức, user, project, request, model, credential version và rate snapshot.
- Desktop chỉ gọi `ServerGenerationClient`; không còn provider key/client trực tiếp.

Các file chính cần đối chiếu:

- `TOOL-SERVER/Generation/VideoProviderClient.cs`
- `TOOL-SERVER/Generation/GenerationService.cs`
- `TOOL-SERVER/Generation/VideoModelPolicy.cs`
- `TOOL-SERVER/Generation/AiCostEstimator.cs`
- `TOOL-SERVER/Generation/KlingPollingWorker.cs`
- `TOOL-SERVER/Generation/KlingOutputProxyService.cs`
- `TOOL-SERVER/Generation/ProviderCatalogBootstrapper.cs`
- `TOOL-SERVER/Generation/ProviderRuntimeResolver.cs`
- `TOOL-SERVER/Organizations/OrganizationService.cs`
- `TOOL-SERVER/Organizations/OrganizationProviderCredentialTester.cs`
- `TOOL-SERVER/wwwroot/admin/admin-organizations.js`
- `TOOL-LOCAL/Generation/ProjectGenerationService.cs`
- `TOOL-LOCAL/WebView/DashboardBridge.cs`

## 4. Phạm vi và bất biến

### 4.1. Trong phạm vi

- Chỉ video dài có workflow `OpenAiStructuredPlan`.
- Fal provider dùng credential theo tổ chức.
- Veo 3.1 Standard/Fast, Native Audio, 720p.
- Cảnh provider dài đúng 4, 6 hoặc 8 giây.
- Tỷ lệ chỉ 16:9 hoặc 9:16.
- Prompt composer Veo riêng, speech-first và tiếng Việt.
- Queue submit/status/result, worker polling, budget, cache và output proxy.
- Admin catalog, rate, credential, readiness và policy video dài.
- Audio review/retry có xác nhận chi phí.

### 4.2. Ngoài phạm vi bản đầu

- Không thay đổi nghiệp vụ `DirectShortVideo`.
- Không thêm `FAL_KEY` vào desktop hoặc `appsettings.json`.
- Không thêm lựa chọn model tùy ý cho Member ở desktop.
- Không biến Veo thành một “Gói sử dụng” license.
- Không tự lấy và cập nhật giá Fal vào database.
- Không dùng webhook trong bản đầu; tiếp tục polling bằng worker hiện có.
- Không dùng Veo Extend Video, First/Last Frame hoặc Reference-to-Video nhiều ảnh.
- Không tự fallback sang Kling, Fast, Text-to-Video hay TTS khi request I2V thất bại.

### 4.3. Cấu hình request bị khóa

- Standard endpoint: `fal-ai/veo3.1/image-to-video`.
- Fast endpoint: `fal-ai/veo3.1/fast/image-to-video`.
- `resolution = "720p"`.
- `generate_audio = true`.
- `auto_fix = false`.
- `duration` chỉ là `4s`, `6s`, `8s`.
- `aspect_ratio` chỉ là `16:9`, `9:16`.
- `safety_tolerance` là policy server, không nhận từ desktop.

## 5. Các vấn đề kiến trúc phải xử lý trước

### 5.1. Policy hiện tại chưa tách video dài/video ngắn

`OrganizationVideoPolicies` hiện có khóa chính theo `OrganizationId`, tức mỗi tổ chức chỉ có một policy video. Nếu chỉ thay policy sang Fal/Veo thì video ngắn cũng có thể snapshot Veo.

Thiết kế đề xuất:

- Thêm `PolicyScope` với ít nhất `Default` và `LongForm`.
- Migration idempotent backfill policy cũ thành `Default`.
- Chuyển khóa/unique constraint để một tổ chức có một policy Active trên mỗi scope.
- Resolver video dài đọc `LongForm`, có thể fallback cấu hình sang `Default` chỉ để giữ tương thích trước khi tổ chức cấu hình LongForm; tuyệt đối không fallback provider khi generation lỗi.
- Resolver video ngắn chỉ đọc `Default`.
- Fal bị chặn nếu được gán vào scope không phải `LongForm` trong bản đầu.
- Endpoint/API Admin cũ tiếp tục đại diện cho `Default`; thêm API rõ ràng cho `LongForm` hoặc nâng DTO có `scope` mà vẫn giữ compatibility.

Project đã có video snapshot không được tự đổi khi policy thay đổi.

### 5.2. Capability hiện chỉ giữ min/max duration

`VideoModelCapabilities` hiện đọc `durations` nhưng chỉ lấy min/max. Với Veo, kiểm tra `4..8` là chưa đủ vì 5 và 7 giây không hợp lệ.

Cần bổ sung tập `AllowedDurationsSeconds` và dùng tập này ở:

- content-plan allocator;
- server preflight;
- submit validation;
- desktop display;
- unit test;
- rate reservation.

### 5.3. Video dài hiện có target 75 giây

`DashboardBridge` hiện tạo project dài với `TargetDurationSeconds = 75`. Tổng 75 không thể tạo hoàn toàn bằng clip provider 4/6/8 giây.

Không được âm thầm đổi project thành 76 giây. Phải dùng hai thời lượng đã có trong schema scene:

- `ContentDurationMs`: thời lượng nội dung/timeline.
- `GenerationDurationMs`: thời lượng gửi provider, luôn 4/6/8 giây.
- `TailTrimMs = GenerationDurationMs - ContentDurationMs`.

Allocator phải:

- giữ tổng content đúng target, ví dụ 75 giây;
- chọn tổng generation nhỏ nhất nhưng mọi scene provider đều thuộc 4/6/8;
- tránh content scene quá ngắn nếu có cách phân bổ khác;
- yêu cầu lời thoại kết thúc trước content boundary;
- reserve và quyết toán theo generation duration thực sự mua từ Fal;
- trim clip cục bộ trước audio review/approval và final render.

DTO content plan cần mang riêng content duration và generation duration, sau đó desktop lưu đúng hai cột hiện có. Nếu phải thay DTO công khai, sửa `TOOL-SHARED.Contracts` trước rồi cập nhật server, desktop và test cùng lượt.

### 5.4. Image-to-Video cần ảnh cho từng cảnh

On-camera scene có thể dùng ảnh nhân vật primary đã duyệt/khóa. B-roll hiện có thể không có ảnh reference.

Quyết định khuyến nghị cho bản đầu:

- On-camera dialogue: bắt buộc I2V với ảnh nhân vật đã duyệt.
- B-roll không có ảnh: route Text-to-Video cùng tier phải được quyết định trước submit theo speech mode/scene type; đây là deterministic routing, không phải fallback sau lỗi.
- Nếu sản phẩm yêu cầu tuyệt đối mọi cảnh đều I2V, phải bổ sung nghiệp vụ tạo, duyệt và khóa `SceneFirstFrame` cho B-roll trước khi triển khai Fal production.
- I2V thất bại không được tự đổi sang Text-to-Video.

Quyết định bản đầu ngày 2026-09-01: chỉ nhận cảnh có first-frame đã duyệt. On-camera dùng ảnh nhân vật primary; B-roll thiếu first-frame bị chặn trước budget/outbound. Không route hoặc fallback sang Text-to-Video. Nghiệp vụ tạo/duyệt `SceneFirstFrame` cho B-roll là hạng mục mở riêng sau bản đầu.

### 5.5. Ảnh nhân vật hiện có thể là ảnh vuông

Fal khuyến nghị ảnh input 720p trở lên và đúng 16:9/9:16; ảnh sai tỷ lệ có thể bị crop. Cần:

- kiểm tra MIME, số byte, hash, dimensions và orientation;
- không để crop mất mặt/miệng người nói;
- chuẩn bị derived first-frame đúng tỷ lệ hoặc chặn preflight với thông báo rõ;
- không sửa/ghi đè asset gốc đã khóa;
- snapshot hash/version của derived input được dùng cho request.

## 6. Thiết kế triển khai

### Task 1 — Contract và migration policy scope

- [x] Thiết kế `PolicyScope` tương thích dữ liệu cũ.
- [x] Cập nhật DTO organization video policy trong `TOOL-SHARED.Contracts`.
- [x] Tạo migration SQL idempotent mới, không sửa migration lịch sử.
- [x] Backfill policy hiện tại thành `Default`.
- [x] Cập nhật EF mapping/index/relationship.
- [x] Cập nhật resolver nhận workflow/scope rõ ràng.
- [x] Thêm audit log chứa scope và policy version, không chứa secret.
- [x] Test video ngắn chỉ dùng `Default`; video dài dùng `LongForm`.
- [x] Test snapshot cũ không đổi provider/model.

### Task 2 — Catalog Fal/Veo

- [x] Thêm `ProviderCodes.Fal`.
- [x] Seed Fal ở trạng thái Disabled.
- [x] Seed Standard và Fast ở trạng thái Disabled.
- [x] Standard là model mặc định bên trong catalog Fal nhưng provider vẫn Disabled.
- [x] Capability chứa exact durations, ratios, resolution, Native Audio, reference support, billing usage type và endpoint ID.
- [x] Runtime chỉ cho phép endpoint ID exact-match trong allowlist nội bộ.
- [x] Cập nhật provider readiness và resolver để nhận Fal.
- [x] Không tự bật provider/model/rate/policy khi bootstrap.

### Task 3 — Credential Fal

- [x] Admin nhập/rotate `FAL_KEY` qua HTTPS như OpenAI/Kling.
- [x] Mã hóa bằng ASP.NET Core Data Protection.
- [x] Dùng `Authorization: Key <FAL_KEY>`, không dùng Bearer.
- [x] Test key bằng Fal Platform API không tạo render có phí, ví dụ model lookup cho hai endpoint đã duyệt.
- [x] Credential mới phải test thành công trước khi Active.
- [x] Credential cũ đi qua `Active -> Retiring -> Revoked` và tiếp tục phục vụ request đang chạy theo version snapshot.
- [x] Response chỉ trả secret hint/version/status.

### Task 4 — Exact duration allocator

- [x] Mở rộng `VideoModelCapabilities` với exact durations.
- [x] Viết allocator cho content/generation duration.
- [x] Giữ tổng content đúng target project.
- [x] Chỉ sinh generation duration 4/6/8.
- [x] Cập nhật OpenAI schema/prompt content-plan để phân biệt hai thời lượng.
- [x] Cập nhật DTO và persistence desktop.
- [x] Cập nhật server validation, request hash và safe snapshot.
- [x] Trim phần dư bằng FFmpeg trước khi approve clip.
- [x] Test đặc biệt target 75 giây.

### Task 5 — Reference/first-frame transport

- [x] Chỉ dùng ảnh primary đã duyệt, đúng project/organization và version hiện hành.
- [x] Kiểm tra MIME hỗ trợ, tối đa 8 MB và dimensions trước budget/outbound.
- [x] Chặn ảnh sai aspect ratio mà không crop hoặc ghi đè asset gốc.
- [x] Bản đầu gửi Data URI server-side vì Fal hỗ trợ và không cần URL public.
- [x] Không ghi Data URI/Base64 vào `ProviderRequests`, logs hoặc exception.
- [ ] Benchmark ảnh thực tế trước production vì Base64 làm payload lớn.
- [ ] Nếu Data URI không ổn định, thiết kế URL input HTTPS ký ngắn hạn, token entropy cao, hết hạn và không cần auth header; không tự chuyển sang Fal CDN mà chưa đánh giá retention/privacy.

### Task 6 — Prompt composer Veo

- [x] Tạo composer riêng, không tái sử dụng trực tiếp prompt Kling.
- [x] Version template `veo-native-audio-v1-vietnamese-speech-first`.
- [x] Giữ nguyên `spoken_text` đã duyệt.
- [x] Đặt dialogue/performance trước visual/identity/asset context.
- [x] On-camera yêu cầu đúng một speaker, bắt đầu nói sớm, thấy rõ môi/hàm, lip-sync và không narrator.
- [x] Cấm nhân vật chỉ đứng yên/mỉm cười im lặng khi có dialogue.
- [x] B-roll voice-over không được gắn nhân vật nói trực tiếp.
- [x] Negative prompt chặn closed mouth, silent smiling, subtitles, text overlay, sai speaker và nhiều người cùng nói.
- [x] Không áp dụng giới hạn từ cố định và không tự cắt lời.
- [x] `auto_fix=false` luôn được gửi rõ trong request.
- [x] Mở rộng language/speech policy video dài sang Fal mà vẫn giữ version Kling cũ để audit dữ liệu lịch sử.
- [x] Thêm manual recovery profile riêng cho Veo; retry cần xác nhận chi phí mới.

### Task 7 — Fal Queue client

- [x] Tạo `FalVeoVideoClient` triển khai `IVideoProviderClient`.
- [x] Đăng ký client vào `VideoProviderRouter` và DI.
- [x] Submit qua `https://queue.fal.run/{approved-endpoint}`.
- [x] Lưu `request_id` làm external request ID.
- [x] Map `IN_QUEUE`, `IN_PROGRESS`, `COMPLETED` về trạng thái nội bộ.
- [x] Khi `COMPLETED`, gọi result endpoint và lấy `video.url` trong bộ nhớ.
- [x] Chuẩn hóa lỗi 401/403/422/429/5xx và lỗi terminal queue.
- [x] Không lưu `status_url`, `response_url`, signed output URL hoặc raw response có URL.
- [x] Không tự retry submit khi timeout xảy ra sau khi Fal có thể đã nhận request.
- [x] Worker chỉ polling request đã có external request ID.
- [x] Không thêm webhook trong bản đầu.

Payload outbound do server dựng phải khóa:

```json
{
  "prompt": "<effective Veo prompt>",
  "image_url": "<validated data URI or approved transport URL>",
  "aspect_ratio": "16:9",
  "duration": "8s",
  "resolution": "720p",
  "generate_audio": true,
  "auto_fix": false,
  "safety_tolerance": "<server policy>"
}
```

Không nhận các trường provider/model/prompt/duration/resolution/native audio từ desktop làm nguồn sự thật.

### Task 8 — Privacy, outbound và output cache

- [x] Allow outbound runtime chính xác `queue.fal.run:443`.
- [x] Allow credential test chính xác `api.fal.ai:443`.
- [x] Thêm output suffix tối thiểu `fal.media` và exact `storage.googleapis.com`.
- [x] Không allow toàn bộ `googleapis.com`.
- [x] Giữ HTTPS-only, DNS/IP validation, redirect limit, MIME và size limit.
- [x] Gửi `X-Fal-Store-IO: 0` để giảm lưu JSON input/output ở Fal.
- [x] Cấu hình `X-Fal-Object-Lifecycle-Preference` cho polling/cache.
- [x] Cache output ngay khi hoàn tất rồi chỉ trả proxy URL tương đối.
- [x] Không đưa output URL gốc vào request log, usage, audit hoặc desktop response.
- [x] Cleanup output cache theo retention hiện có.

### Task 9 — Pricing, budget và settlement

- [x] Fal Standard và Fast có rate `VideoSecond` riêng.
- [x] Rate metadata khóa đúng `resolution=720p`, `nativeAudio=true` và endpoint/model.
- [x] Không seed giá.
- [ ] Admin nhập rate Active từ Fal dashboard/Platform Pricing API/hợp đồng tại thời điểm rollout.
- [x] Reservation dùng generation duration thực sự gửi Fal.
- [x] Rate snapshot và reservation dùng transaction `Serializable` như provider hiện tại.
- [x] Thiếu rate trả `pricing_not_configured` trước outbound.
- [x] Nếu Fal không trả actual cost đủ tin cậy, settlement dùng estimate theo generation duration và rate snapshot đã khóa.
- [ ] Đối chiếu usage ledger với Fal dashboard sau smoke test.

### Task 10 — Admin Console

- [x] Thêm Fal vào catalog/readiness/provider cards.
- [x] Thêm credential dialog cho `FAL_KEY` nhưng không hiển thị lại plaintext.
- [x] Required rate của Fal là `VideoSecond` đúng variant.
- [x] Tách UI policy `Default` và `Video dài`.
- [x] Cho Owner/OrganizationAdmin chọn Standard hoặc Fast cho scope `LongForm`.
- [x] Hiển thị read-only capability: `720p · Native Audio · 4/6/8s · 16:9/9:16`.
- [x] Generalize trang cách tính chi phí video, không hard-code chỉ Kling.
- [x] Sửa hướng dẫn Native Audio/rate theo provider.
- [x] Không sửa menu/nghiệp vụ `Gói sử dụng` để chứa quota Veo.

### Task 11 — Desktop video dài

- [x] Không thêm provider HTTP client hoặc key storage.
- [x] Tiếp tục submit/status/download qua generic server API.
- [x] Hiển thị provider/model snapshot ở chế độ chỉ đọc.
- [x] Chặn 1:1 trước outbound khi long-form policy là Veo.
- [x] Hiển thị content duration, provider duration và tail trim dễ hiểu.
- [x] Giữ bước preview và nghe duyệt Native Audio.
- [x] Clip thiếu/inaudible audio chuyển `NativeAudioInvalid`.
- [x] Retry Veo speech recovery dùng xác nhận thời lượng và chi phí request mới hiện có.
- [x] Không thay đổi workflow/màn hình video ngắn ngoài test chống regression.

### Task 12 — Kiểm thử tự động

Tối thiểu phải có test cho:

- [ ] Policy LongForm không ảnh hưởng `DirectShortVideo`.
- [ ] Project Kling cũ không đổi snapshot.
- [ ] Fal không được chọn cho scope video ngắn.
- [ ] Capability chỉ chấp nhận 4/6/8 và 16:9/9:16.
- [ ] Allocator 75 giây giữ content 75 và chỉ dùng generation 4/6/8.
- [ ] Reservation tính theo generation duration.
- [ ] Payload luôn có `generate_audio=true`, `auto_fix=false`.
- [ ] Standard không fallback Fast và ngược lại.
- [ ] I2V lỗi không tự chuyển T2V.
- [ ] Auth header dùng `Key`, không dùng `Bearer`.
- [ ] Queue submit/status/result mapping.
- [ ] Idempotency không tạo hai Fal request cho cùng operation.
- [ ] Timeout submit không tự phát sinh request thứ hai.
- [ ] Worker hoàn tất khi desktop đóng.
- [ ] Credential version snapshot phục vụ task đang chạy sau rotation.
- [ ] Budget/rate/Viewer/cross-organization bị chặn trước outbound.
- [ ] Log và safe request snapshot không chứa key, Base64, full speech/prompt hoặc output URL.
- [ ] Output host không allow bị chặn trước download.
- [ ] Redirect/DNS rebinding/private IP/MIME/size tiếp tục bị chặn.
- [ ] Audio im lặng không được approve.
- [ ] Recovery retry tạo reservation/idempotency riêng và cần xác nhận.
- [ ] Desktop short-video regression giữ nguyên Kling/default policy.

Không đưa test Fal thật có phí vào test suite tự động.

## 7. Rollout staging có kiểm soát

Fal và hai model phải giữ Disabled cho đến khi hoàn thành các bước sau:

1. Chạy migration trên staging sau khi xác minh instance, database, backup và restore.
2. Tạo tổ chức staging riêng, budget nhỏ và member limit nhỏ.
3. Nhập rate hiện hành từ đúng Fal account.
4. Nhập `FAL_KEY` API scope qua Admin HTTPS và test credential.
5. Bật Fal Fast trước để kiểm tra hạ tầng với chi phí thấp hơn.
6. Chọn Fast cho policy `LongForm` của tổ chức staging.
7. Chạy smoke test có phê duyệt chi phí.
8. Đối chiếu ledger với Fal dashboard.
9. Sau khi hạ tầng đạt mới thử Standard và đánh giá chất lượng.
10. Chỉ bật model production sau khi có biên bản kết quả và ngưỡng nghiệm thu được chấp nhận.

Ma trận thử nghiệm nhỏ nên phủ:

- Standard và Fast.
- 4, 6 và 8 giây.
- 16:9 và 9:16.
- Câu tiếng Việt ngắn, vừa và gần giới hạn thực tế.
- Nhân vật/giọng nam và nữ.
- On-camera trực diện, chuyển động nhẹ và camera tĩnh.
- Ít nhất một cảnh dùng tail trim.

Mỗi clip ghi nhận:

- request/model/duration/aspect/tier;
- queue latency và tổng latency;
- audio stream có tồn tại và nghe được hay không;
- tỷ lệ đọc đủ câu;
- đúng người nói;
- độ khớp môi;
- có lỗi chỉ đứng cười/im lặng hay không;
- crop ảnh/mất mặt/mất miệng;
- chi phí ledger và chi phí Fal dashboard;
- kết luận Pass/Fail và lý do.

## 8. Tiêu chí hoàn thành

Task chỉ được coi là hoàn thành khi:

1. Fal/Veo chỉ chạy cho video dài; video ngắn không thay đổi provider.
2. Standard/Fast được chọn từ Admin policy LongForm và project snapshot chính xác.
3. Chỉ request 4/6/8 giây, 720p, 16:9/9:16, Native Audio và `auto_fix=false` được gửi.
4. Mọi request đi qua quyền truy cập, idempotency, rate, budget reservation và credential version snapshot.
5. Desktop đóng nhưng server vẫn polling, cache và quyết toán được.
6. Desktop không nhận `FAL_KEY` hoặc URL output gốc.
7. Log/audit/request snapshot không chứa secret, Base64, full prompt/full speech hoặc signed URL.
8. Clip không có audio hoặc gần như im lặng không được duyệt.
9. Retry speech không tự chạy và cần xác nhận chi phí mới.
10. Project Kling cũ tiếp tục hoạt động bằng snapshot cũ.
11. Toàn bộ build/test bắt buộc đạt.
12. Smoke test Fal có phí được thực hiện riêng sau phê duyệt và có kết quả đối chiếu chi phí/chất lượng.

## 9. Giá trị nhận được sau triển khai

- Một pipeline Veo 3.1 hoàn chỉnh cho video dài, được quản trị tập trung theo tổ chức.
- Standard ưu tiên chất lượng và Fast ưu tiên chi phí.
- Khả năng nhân vật nói tiếng Việt trực tiếp, Native Audio và lip-sync tốt hơn nhờ prompt/policy riêng.
- Giảm lỗi nhân vật chỉ đứng cười nhờ speech-first prompt, preflight và audio review.
- Queue/polling/cache tiếp tục chạy khi desktop đóng.
- Budget, rate, usage và audit truy vết được theo từng request.
- Không lộ key hoặc provider URL cho desktop.
- Không làm project Kling cũ tự đổi provider và không làm thay đổi video ngắn.

Không cam kết Veo đọc chính xác 100% lời thoại tiếng Việt. Chất lượng thực tế phải được quyết định bằng bộ smoke test có giới hạn chi phí trước production.

## 10. Thứ tự triển khai đề xuất

1. Policy scope và migration.
2. Exact duration capability/allocator.
3. Catalog Fal, credential test và pricing readiness.
4. Reference/first-frame validation và transport.
5. Veo prompt composer/language/speech policy.
6. Fal Queue client và error normalization.
7. Worker polling, output cache và security allowlist.
8. Admin Console.
9. Desktop long-form và audio recovery UI.
10. Unit/integration/security/regression tests.
11. Cập nhật tài liệu nghiệp vụ/runbook.
12. Staging smoke test có phê duyệt chi phí.

## 11. Lệnh xác minh sau khi thực sự triển khai source

Chạy từ root repository:

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Nếu chỉ sửa phần Web của desktop, có thể kiểm tra nhanh thêm:

```powershell
Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
```

Không ghi mốc build/test mới vào tài liệu nếu chưa thực sự chạy và đạt.

## 12. Trạng thái bàn giao hiện tại

- [x] Đã triển khai policy scope `Default`/`LongForm` và migration idempotent 4.0.9 trong source.
- [x] Đã triển khai catalog/credential/rate/readiness/Admin cho Fal/Veo nhưng giữ provider và model Disabled.
- [x] Đã triển khai exact duration, content/generation duration, tail trim và desktop hiển thị thời lượng.
- [x] Đã triển khai first-frame preflight; ảnh sai tỷ lệ bị chặn, không tự crop asset khóa; B-roll chưa có first-frame bị chặn.
- [x] Đã triển khai prompt Veo tiếng Việt speech-first và recovery profile riêng.
- [x] Đã triển khai Queue submit/status/result, worker polling, privacy headers, output allowlist/cache/proxy.
- [x] Đã thêm unit/security/regression test cho catalog, policy scope, duration, pricing, credential, Queue client, prompt, first-frame và output host.
- [x] Đã chạy restore, Release build 0 warning/error và 442/442 test đạt ngày 2026-09-02.
- [ ] Chưa chạy migration trên database thật.
- [ ] Chưa nhập `FAL_KEY` hoặc rate thật.
- [ ] Chưa bật provider/model/policy Fal trên môi trường triển khai.
- [ ] Chưa gọi Fal thật, chưa smoke test và chưa phát sinh chi phí.
- [ ] Nghiệp vụ tạo/duyệt `SceneFirstFrame` riêng cho B-roll vẫn là hạng mục mở sau bản đầu.

AI tiếp quản không được bỏ qua quyết định “chỉ video dài”. Khi rollout phải chạy migration 4.0.9 trên staging đã backup, nhập rate/credential thật, bật Fal có kiểm soát và smoke test có phê duyệt chi phí; không được coi source đã build/test là bằng chứng Fal production đã sẵn sàng.
