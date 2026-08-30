# Kế hoạch triển khai BytePlus Seedance cho VideoMaker

> Trạng thái: **Kế hoạch — chưa triển khai source code, chưa tạo/chạy migration, chưa gọi API tính phí**  
> Ngày lập: 2026-08-30  
> Phạm vi: bổ sung BytePlus Seedance bên cạnh Kling và chuyển việc chọn provider/model về `TOOL-SERVER`.

## 1. Mục tiêu

Bổ sung BytePlus Seedance làm nhà cung cấp tạo video thứ hai, đồng thời giữ nguyên nguyên tắc:

- Người dùng trên desktop **không được chọn provider hoặc model**.
- Desktop chỉ gửi yêu cầu tạo video theo project/cảnh; `TOOL-SERVER` tự xác định model theo policy của tổ chức và snapshot đã gắn với project.
- Kling tiếp tục hoạt động và là phương án tương thích cho dữ liệu/dự án cũ.
- Không tự động fallback giữa Kling và Seedance vì mỗi model có giá, giới hạn thời lượng và chất lượng khác nhau.
- API key chỉ được quản trị, mã hóa và sử dụng ở server; không truyền key hoặc URL output gốc về desktop.
- Mỗi yêu cầu vẫn phải đi qua xác thực quyền, idempotency, kiểm tra giá, giữ ngân sách, quyết toán và request log.
- Video do Kling hoặc Seedance tạo có âm thanh native; người dùng phải xem trước và duyệt từng cảnh trước khi dựng video cuối.

## 2. Kết quả nghiệp vụ sau khi hoàn thành

Luồng dự kiến:

1. Global Admin khai báo provider/model được phép dùng và bảng giá trên `TOOL-SERVER`.
2. Owner hoặc OrganizationAdmin cấu hình credential và policy video cho tổ chức trên trang quản trị server.
3. Desktop tạo project nhưng không gửi provider/model.
4. Khi project bắt đầu luồng AI, server đọc policy của tổ chức và snapshot provider/model/cấu hình video vào project.
5. Content plan và prompt cảnh được sinh theo giới hạn của model đã snapshot.
6. Desktop gửi lệnh tạo video chung cho từng cảnh, không chứa lựa chọn model.
7. Server xác thực request, giữ ngân sách và điều phối đến Kling hoặc BytePlus Seedance.
8. Worker trên server polling task ngay cả khi desktop đã đóng.
9. Khi task hoàn thành, server tiếp nhận/lưu tạm output và chỉ trả URL proxy tương đối cho desktop.
10. Desktop tải clip, kiểm tra media/âm thanh, lưu vào workspace và cho người dùng xem trước, duyệt hoặc tạo lại.
11. Video cuối chỉ dựng từ các `SceneVideo` đã được duyệt và giữ nguyên audio native của clip.

## 3. Quyết định kiến trúc đã chốt

### 3.1. Quyền chọn model

- Global Admin quản lý catalog provider/model và bảng giá.
- Owner/OrganizationAdmin quản lý credential và policy video của tổ chức trong phạm vi model đang được Global Admin cho phép.
- BillingManager quản lý budget/usage theo quyền hiện có, không được quản lý credential.
- Member có thể phát sinh AI nhưng không được chọn provider/model.
- Viewer không được phát sinh chi phí AI.
- Desktop không có combobox, setting, bridge command hoặc request field để chọn provider/model.
- Desktop có thể hiển thị provider/model ở chế độ chỉ đọc để hỗ trợ vận hành.

### 3.2. Thời điểm gắn model cho project

- Project mới được gắn snapshot tại lần đầu bắt đầu luồng AI/content plan, trước khi chốt cấu trúc cảnh.
- Tất cả lần tạo/tạo lại clip trong cùng project phải dùng snapshot đó.
- Thay đổi policy của tổ chức chỉ áp dụng cho project chưa được snapshot hoặc project mới.
- Không âm thầm đổi model của project đang làm dở.
- Dự án hiện hữu được giữ Kling để tránh thay đổi giới hạn thời lượng và kết quả đã có.

### 3.3. Fallback và retry

- Không fallback tự động từ Seedance sang Kling hoặc ngược lại.
- Retry kỹ thuật phải giữ nguyên provider/model, idempotency và attempt history.
- Nếu credential, rate, budget hoặc provider không sẵn sàng, request dừng trước outbound call với mã lỗi ổn định.
- Việc chuyển model cho một project đã snapshot, nếu sau này cần hỗ trợ, phải là thao tác quản trị tường minh kèm đánh giá lại content plan và chi phí; không nằm trong MVP.

### 3.4. Model Seedance trong MVP

- Provider code dự kiến: `byteplus`.
- Model cân bằng/ưu tiên ban đầu: `dreamina-seedance-2-0-260128`.
- Model premium/dài hơn: `dreamina-seedance-2-5-260628`.
- Độ phân giải mục tiêu: 720p để khớp workflow hiện tại.
- Bật native audio.
- Không watermark nếu điều khoản tài khoản/provider cho phép.
- MVP chỉ hỗ trợ text-to-video và image-to-video từ ảnh nhân vật đã được duyệt.
- Chưa hỗ trợ reference-video trong MVP để tránh biến thể giá, mức tối thiểu và quy tắc input phức tạp.
- Không dùng người thật/reference face chưa được BytePlus cho phép; lỗi kiểm duyệt phải được ánh xạ rõ ràng cho người dùng.

Các giới hạn thời lượng cần được xác minh lại từ catalog chính thức tại thời điểm triển khai, dự kiến:

| Model | Thời lượng dự kiến | Vai trò |
|---|---:|---|
| Kling 3.0 | 3–15 giây | Tương thích hiện tại |
| Seedance 2.0 | 4–15 giây | Mặc định/cân bằng |
| Seedance 2.5 | 4–30 giây | Premium/cảnh dài |

## 4. Phạm vi triển khai

### Trong phạm vi

- Catalog BytePlus/Seedance ở server.
- Credential BytePlus theo tổ chức.
- Bảng giá theo model và rate snapshot.
- Policy video theo tổ chức và snapshot theo project.
- Contract tạo video trung lập provider.
- Adapter Kling và BytePlus dưới một abstraction chung.
- Worker polling đa provider.
- Lưu tạm output và proxy download trung lập provider.
- Desktop không chọn model và dùng API video chung.
- Native audio, preview/approve và render cuối.
- Migration database-first, test tự động, test migration và quy trình rollout.

### Ngoài phạm vi MVP

- Cho desktop/người dùng cuối chọn model.
- Fallback tự động giữa các provider.
- Trộn nhiều model trong cùng project.
- Chuyển project đang làm dở sang model khác.
- Reference-video, lip-sync riêng hoặc TTS chồng lên native audio.
- Provider SDK/key trong `TOOL-LOCAL`.
- Seed giá production hoặc tự suy đoán giá hợp đồng.
- Tự chạy migration database thật, tạo request trả phí hoặc phát hành production.

## 5. Hiện trạng cần bảo toàn

- Luồng video hiện tại đang gọi các contract/controller/service có tên Kling.
- Worker hiện chỉ polling Kling.
- Output proxy hiện áp dụng riêng cho output Kling.
- Desktop đang gửi cấu hình 720p/native audio và lưu metadata theo chiến lược `KlingNative`.
- Catalog/provider readiness hiện chủ yếu biết OpenAI và Kling.
- Provider/model/request entity đã có tính tổng quát nhất định, nhưng đường gọi video chưa tổng quát.
- Desktop vẫn đọc/ghi trực tiếp SQL cho một phần workflow trong giai đoạn chuyển tiếp; không được mở rộng quyền SQL sang bảng credential, provider request hoặc usage.
- Các dự án/clip Kling hiện có phải tiếp tục mở, polling, tải và render được sau nâng cấp.

## 6. Thiết kế dữ liệu và migration

Tạo migration idempotent phiên bản kế tiếp; tên dự kiến:

`database/VideoFactory.4.0.4.BytePlusSeedance.sql`

Tên/version cuối cùng phải đối chiếu danh sách migration thực tế trước khi tạo file. Không sửa migration đã triển khai.

### 6.1. Policy video của tổ chức

Tạo bảng policy thuộc miền quản trị tổ chức, dự kiến chứa:

- `OrganizationId`.
- Provider/model mặc định đang được tổ chức sử dụng.
- Resolution, native-audio và các capability đã cho phép.
- `PolicyVersion` tăng khi thay đổi cấu hình có ảnh hưởng tới project mới.
- Người cập nhật và thời gian cập nhật.
- Row version/concurrency token.
- Trạng thái active/disabled nếu cần phục vụ rollback vận hành.

Ràng buộc:

- Mỗi tổ chức chỉ có một policy video active tại một thời điểm trong MVP.
- Policy chỉ được trỏ tới provider/model đang enabled trong catalog.
- Không lưu plaintext API key trong bảng policy.
- Tổ chức chưa có policy không được tự suy đoán model; server trả lỗi cấu hình rõ ràng.

### 6.2. Snapshot video trên project

Bổ sung snapshot bất biến vào project hoặc bảng snapshot riêng, tối thiểu gồm:

- Provider ID/code.
- Provider model ID/code.
- Policy version.
- Resolution.
- Native-audio flag.
- Thời điểm snapshot.

Yêu cầu:

- Server là bên duy nhất được gắn snapshot.
- Khi đã có snapshot, request từ desktop không thể ghi đè.
- Backfill project cũ về Kling 3.0/720p/native audio theo dữ liệu thực tế hiện có.
- Migration phải xử lý dữ liệu thiếu theo cách an toàn và ghi rõ điều kiện không thể backfill.
- Không làm project cũ vi phạm constraint chỉ vì catalog/rate mới chưa được cấu hình.

### 6.3. Metadata output do server lưu tạm

BytePlus trả URL output có thời hạn, vì vậy bổ sung metadata để server tiếp nhận output sau khi task hoàn thành:

- Provider request ID/task ID.
- Storage kind và relative storage key; không lưu đường dẫn tùy ý do provider gửi.
- MIME type, byte size và SHA-256.
- Thời gian tạo, thời gian hết hạn và trạng thái cleanup.
- Thời điểm desktop tải thành công, nếu cần phục vụ retention.

Không khuyến nghị lưu video blob lớn trực tiếp trong SQL. File/object storage phải có root cố định, path chuẩn hóa và lifecycle cleanup.

### 6.4. Catalog và giá

- Thêm provider `byteplus` và các model Seedance được duyệt.
- Provider/model mới mặc định ở trạng thái disabled để tránh outbound ngoài ý muốn.
- Không seed rate giả định.
- Global Admin phải nhập rate hiện hành sau khi đối chiếu hợp đồng/dashboard BytePlus.
- Migration/catalog bootstrap phải chạy lặp lại an toàn và không ghi đè cấu hình quản trị đã có.

## 7. Contract dùng chung

Thực hiện trong `TOOL-SHARED.Contracts` trước, sau đó cập nhật server, desktop và test trong cùng thay đổi.

### 7.1. Request tạo video chung

Tạo contract có ý nghĩa `SubmitVideoRequest`, chỉ chứa dữ liệu nghiệp vụ cần thiết, ví dụ:

- Project/scene/version.
- Prompt hoặc tham chiếu prompt đã được server quản lý theo thiết kế hiện hành.
- Approved character image/reference hợp lệ nếu có.
- Idempotency key/attempt theo quy tắc hiện tại.

Không được có:

- Provider code.
- Model code.
- API key/base URL.
- Giá hoặc quyền ghi đè capability.

### 7.2. Response/status chung

Tạo response trung lập provider với:

- Request/task ID nội bộ.
- Trạng thái chuẩn hóa: queued, running, succeeded, failed, cancelled, expired.
- Progress khi provider hỗ trợ.
- Provider/model chỉ đọc.
- Error code/message đã được làm sạch.
- URL proxy tương đối khi output sẵn sàng.
- Audio metadata trung lập provider.

### 7.3. Tương thích ngược

- Giữ contract/endpoint Kling cũ trong một giai đoạn deprecation nếu desktop cũ vẫn còn được hỗ trợ.
- Endpoint cũ phải đi vào cùng service/router chung, không duy trì hai logic budget/polling riêng.
- Không thay đổi JSON field cũ theo cách khiến bản desktop đã phát hành deserialize lỗi.
- Có test serialization/backward compatibility cho response cũ.

## 8. Công việc tại `TOOL-SERVER`

### 8.1. Catalog, allowlist và credential

- Bổ sung provider/model Seedance vào bootstrap/catalog với trạng thái disabled mặc định.
- Bổ sung host BytePlus chính thức vào outbound allowlist theo đúng region sử dụng.
- Base URL dự kiến cho khu vực Singapore/APAC: `https://ark.ap-southeast.bytepluses.com/api/v3/`.
- Credential dùng header Bearer và được quản lý theo vòng đời Active → Retiring → Revoked như provider hiện tại.
- Bổ sung credential tester BytePlus bằng endpoint an toàn, không tạo video tính phí.
- Không log header/body chứa key và không trả encrypted payload/plaintext key ra response.
- Đảm bảo task đang chạy vẫn dùng đúng credential version đã snapshot khi rotate key.

### 8.2. Trang/API policy video của tổ chức

- Global Admin bật/tắt catalog model và quản lý giá.
- Owner/OrganizationAdmin chọn policy video của tổ chức trên server admin, không trên desktop.
- Chỉ hiển thị model đang enabled, có capability phù hợp và provider được tổ chức cấu hình credential.
- Validate policy trước khi lưu nhưng không tạo task trả phí.
- Ghi audit người thay đổi, policy version, giá trị trước/sau.
- Không cho xóa/tắt cấu hình theo cách làm mất khả năng theo dõi request đang chạy.

### 8.3. Resolver và snapshot project

- Tách logic chọn model khỏi UI desktop.
- Khi project chưa có snapshot, resolver đọc policy active của tổ chức, kiểm tra catalog/capability rồi ghi snapshot trong transaction phù hợp.
- Các lần sau chỉ đọc snapshot của project.
- Content plan phải lấy min/max duration từ capability model đã snapshot.
- Nếu project chưa có snapshot và policy không hợp lệ, trả mã lỗi trước khi gọi OpenAI hoặc video provider nếu request phụ thuộc model.
- Chống race khi hai request đầu tiên cùng cố gắn snapshot.

### 8.4. Abstraction provider video

Tạo interface/router chung cho các thao tác:

- Submit task.
- Get task/status.
- Cancel task nếu provider hỗ trợ.
- Chuẩn hóa output metadata.
- Chuẩn hóa usage và error.

Adapter riêng:

- Kling adapter giữ hành vi hiện tại.
- BytePlus Seedance adapter gọi ModelArk async video API.

Router phải chọn adapter từ snapshot server-side, không từ field do desktop gửi.

### 8.5. BytePlus Seedance client

Endpoint dự kiến:

- `POST /api/v3/contents/generations/tasks` để tạo task.
- `GET /api/v3/contents/generations/tasks/{id}` để polling.

Yêu cầu triển khai:

- Chỉ HTTPS, host allowlist chính xác, timeout hợp lý và cancellation token.
- Bearer key lấy từ credential vault theo organization/provider/version.
- Model lấy từ project snapshot.
- Resolution/native-audio lấy từ snapshot/capability, không nhận override tùy ý từ desktop.
- Parse trạng thái queued/running/succeeded/failed/cancelled/expired.
- Lưu provider request ID, sanitized response metadata và usage thực tế.
- Không lưu/log signed output URL ngoài nơi cần thiết để worker tải ngay.
- Ánh xạ lỗi auth, quota, moderation, invalid input, timeout và provider unavailable sang mã lỗi nội bộ ổn định.

### 8.6. Prompt composer theo provider

- Giữ ngữ nghĩa chung của cảnh ở tầng domain.
- Kling và Seedance có composer riêng để tối ưu cú pháp/capability.
- Prompt không được rò key, dữ liệu quản trị hoặc metadata nội bộ.
- Với native audio, prompt phải mô tả ambience/dialogue/SFX theo cấu trúc được provider hỗ trợ.
- Nếu reference image không hợp lệ hoặc bị policy người thật chặn, dừng trước outbound call.
- Lưu prompt/version cần thiết để retry cùng model có thể truy vết được.

### 8.7. Pricing, giữ ngân sách và quyết toán

MVP BytePlus direct ModelArk dự kiến dùng usage `completion_tokens`:

- Rate type: `OutputToken`.
- Rate unit: `MillionTokens`.
- Giá do Global Admin nhập theo hợp đồng thực tế.
- Reservation estimate dựa trên duration, resolution và fps/công thức provider đã xác minh.
- Settlement dùng `completion_tokens` thực tế trả về.
- Rate snapshot phải được giữ nhất quán từ reservation tới settlement.
- Transaction giữ/quyết toán/release tiếp tục dùng isolation `Serializable`.
- Thiếu rate phải trả `pricing_not_configured` trước outbound call.
- Không coi budget bằng 0 là không giới hạn; budget 0 phải khóa AI.
- Retry tạo provider task mới phải được tính là attempt mới và không che giấu chi phí.

Các con số giá tham khảo trong tài liệu provider chỉ phục vụ nghiên cứu, không được hardcode hoặc seed làm giá production.

### 8.8. Worker polling đa provider

- Chuyển worker Kling hiện tại thành worker/router video chung hoặc thêm worker theo provider nhưng vẫn đảm bảo một owner polling cho mỗi request.
- Query request theo provider/model snapshot, không filter cứng chỉ Kling.
- Duy trì polling khi desktop đóng.
- Retry polling phải có backoff, giới hạn thời gian và không tạo trùng provider task.
- Trạng thái terminal phải idempotent.
- Khi succeeded, worker tải output về vùng lưu tạm ngay, vì URL BytePlus có thể hết hạn.
- Nếu tải output thất bại tạm thời, giữ trạng thái trung gian và retry có giới hạn; không đánh dấu hoàn tất khi file chưa an toàn.

### 8.9. Lưu tạm output và proxy chung

- Tải output bằng client có SSRF/DNS rebinding protection.
- Chỉ cho phép host/provider URL đã xác minh; giới hạn redirect, MIME, dung lượng và thời gian tải.
- Ghi file atomically rồi mới đánh dấu output sẵn sàng.
- Tính hash và lưu metadata.
- Chỉ trả URL tương đối của server cho desktop.
- Proxy phải kiểm tra JWT/session/device/license/org membership/project ownership như hiện tại.
- Không cho người dùng truyền arbitrary URL/path vào proxy.
- Cleanup theo TTL sau khi desktop tải hoặc sau retention cấu hình; không xóa file còn request active.
- Có cơ chế quan sát dung lượng lưu tạm và lỗi cleanup.

### 8.10. Mã lỗi và readiness

Chuẩn hóa tối thiểu các lỗi:

- `video_policy_not_configured`.
- `video_model_not_enabled`.
- `provider_credential_not_configured`.
- `provider_credential_invalid`.
- `pricing_not_configured`.
- `budget_exceeded`.
- `video_duration_not_supported`.
- `reference_not_allowed`.
- `provider_moderation_rejected`.
- `provider_unavailable`.
- `provider_output_expired`.
- `provider_output_download_failed`.

Readiness/status phải dùng catalog chung, không còn giả định hệ thống chỉ có OpenAI/Kling.

## 9. Công việc tại `TOOL-LOCAL`

### 9.1. Không cho desktop chọn model

- Không thêm dropdown/provider picker/model picker.
- Loại bỏ vai trò nguồn sự thật của catalog hardcode trong React hiện tại.
- Nếu hiển thị model, lấy từ project snapshot/server status và chỉ đọc.
- Không lưu lựa chọn provider/model trong setting máy, local database hoặc project form.
- Không cho WebView message tùy ý truyền provider/model xuống C#.

### 9.2. Bridge và service tạo video

- Đổi message/DTO/handler từ Kling-specific sang video-generic.
- Request chỉ gửi project, scene, version và idempotency data cần thiết.
- Gọi endpoint submit/status/output chung.
- Đồng bộ TypeScript type, C# bridge contract, handler và busy state trong cùng thay đổi.
- Nếu duy trì hỗ trợ server cũ, capability/version negotiation phải rõ ràng; không fallback gọi provider trực tiếp.

### 9.3. UI/UX

- Đổi text “Kling” mang tính hành động thành “Tạo video”, “Đang tạo video”, “Tạo lại video”.
- Có thể hiển thị “Được xử lý bởi Kling/Seedance” ở vùng thông tin chỉ đọc.
- Hiển thị lỗi cấu hình kèm hướng dẫn liên hệ quản trị, không yêu cầu người dùng nhập API key.
- Người dùng vẫn được chọn cảnh cần tạo, xem trước, duyệt, từ chối hoặc tạo lại.
- Trạng thái vẫn khôi phục được sau khi đóng/mở desktop.

### 9.4. Media và native audio

- Sau khi tải, kiểm tra container, video stream, audio stream, duration và giới hạn dung lượng bằng ffprobe/logic hiện có.
- Chuẩn hóa metadata mới thành `ProviderNative` hoặc tên trung lập tương đương.
- Vẫn đọc được dữ liệu legacy `KlingNative`.
- Không chồng TTS mặc định lên clip đã có native audio.
- Clip không có audio khi policy yêu cầu native audio phải được cảnh báo/fail theo rule nghiệp vụ đã chốt.
- Chỉ clip đã được người dùng duyệt mới trở thành nguồn cho render cuối.

## 10. Kế hoạch kiểm thử

### 10.1. Unit test server

- Resolver chọn đúng policy và chỉ snapshot một lần.
- Desktop/request không thể override provider/model.
- Project cũ luôn giữ Kling sau khi org chuyển policy sang Seedance.
- Project mới dùng Seedance khi policy hợp lệ.
- Thiếu policy/credential/rate/budget dừng trước outbound call.
- Budget 0 bị khóa.
- Reservation và settlement dùng cùng rate snapshot.
- Settlement BytePlus dùng actual `completion_tokens`.
- Retry không tạo trùng task với cùng idempotency scope.
- Không fallback provider khi submit/poll thất bại.
- Error mapping và sanitization không rò secret/signed URL.

### 10.2. Adapter/HTTP test

- BytePlus request đúng base URL, auth scheme, model và native-audio config.
- Parse đủ các trạng thái task.
- Xử lý timeout, 401/403, 429, 5xx, malformed response và moderation.
- Không gửi request tới host ngoài allowlist.
- Kling regression giữ nguyên submit/poll/usage/output.

### 10.3. Worker/output test

- Worker tiếp tục polling khi không có desktop kết nối.
- Một request chỉ có một polling owner.
- Succeeded chỉ được công bố khi output đã lưu tạm an toàn.
- URL hết hạn/tải lỗi được retry và kết thúc bằng lỗi ổn định.
- Proxy chặn arbitrary URL, traversal, redirect lạ, MIME sai và file quá lớn.
- User khác organization/project không tải được output.
- Cleanup không xóa output đang hoạt động và có thể chạy lặp lại.

### 10.4. Contract/desktop test

- Request chung không có provider/model field.
- WebView không có command chọn model.
- UI không có control chọn model.
- Provider/model chỉ đọc khớp snapshot server.
- Desktop cũ vẫn đọc được response Kling trong thời gian tương thích.
- Desktop mới mở project Kling cũ, tải clip, preview và render được.
- Metadata `KlingNative` cũ và `ProviderNative` mới đều được xử lý.

### 10.5. Migration test trên database clone

- Chạy migration trên clone có dữ liệu project/provider request Kling thực tế.
- Chạy lại lần hai không lỗi và không tạo trùng catalog/policy/snapshot.
- Xác minh backfill project cũ đúng Kling.
- Xác minh constraint/index/FK và rollback bằng backup/restore procedure.
- Xác minh role SQL desktop không có quyền đọc credential, provider request truth, usage hoặc sửa policy.
- Không chạy database thật cho tới khi xác minh instance, database, backup và khả năng restore.

### 10.6. Smoke test có kiểm soát

Chỉ thực hiện khi người dùng phê duyệt rõ môi trường và chi phí:

- Dùng organization test, credential test và budget thấp.
- Global Admin nhập rate đã xác minh.
- Bật đúng một model Seedance cho tổ chức test.
- Tạo clip ngắn nhất được provider hỗ trợ ở 720p/native audio.
- Đóng desktop trong lúc task chạy, chờ worker hoàn tất, mở lại và tải qua proxy.
- Xác minh video stream, audio stream, duration, preview/approve và render cuối.
- Đối chiếu provider usage, request log, reservation, settlement và số dư budget.
- Tắt model/credential test sau smoke test nếu chưa rollout.

## 11. Trình tự task triển khai

> Cập nhật kiểm chứng ngày 2026-08-30: source đã hoàn thiện các hạng mục được tích bên dưới; `dotnet restore`, Release build (bao gồm Web build) và 305/305 test offline đều đạt. Chưa chạy migration trên database clone/thật, chưa nhập credential/rate thật và chưa gọi provider có phí.

### Giai đoạn 0 — Chốt spec và guardrail

- [x] Xác minh model ID, duration, resolution, fps, native-audio và hạn URL từ tài liệu BytePlus hiện hành.
- [ ] Xác minh region và host ModelArk dùng cho tài khoản production.
- [ ] Chốt rate unit/usage field theo hợp đồng thực tế; không dùng giá tham khảo làm giá production.
- [x] Chốt retention và dung lượng tối đa của server output cache.
- [ ] Chốt thời gian hỗ trợ desktop/server version cũ.

### Giai đoạn 1 — Database và catalog

- [x] Viết migration idempotent phiên bản kế tiếp.
- [x] Tạo organization video policy.
- [x] Tạo project provider/model snapshot.
- [x] Tạo metadata output cache.
- [x] Backfill project cũ về Kling.
- [x] Thêm catalog BytePlus/Seedance disabled mặc định, không seed giá.
- [x] Cập nhật EF/data-first model theo schema đã duyệt.
- [ ] Kiểm thử migration trên database clone.

### Giai đoạn 2 — Contracts và policy server

- [x] Tạo contract submit/status video chung.
- [x] Giữ compatibility contract Kling cũ.
- [x] Xây API/UI quản trị policy video trên server.
- [x] Mở rộng readiness/catalog/credential tester cho BytePlus.
- [x] Xây resolver snapshot provider/model bất biến trên project.
- [ ] Kiểm chứng transaction/concurrency của lần snapshot đầu trên SQL Server clone.
- [x] Áp capability model vào content plan/duration validation.

### Giai đoạn 3 — Gateway provider và chi phí

- [x] Tạo abstraction/router video provider.
- [x] Chuyển Kling hiện tại vào Kling adapter mà không đổi hành vi.
- [x] Tạo BytePlus Seedance adapter/client.
- [x] Tạo prompt composer theo provider.
- [x] Bổ sung allowlist, secret handling và credential lifecycle.
- [x] Bổ sung estimate/reservation/settlement bằng rate snapshot và actual usage.
- [x] Chuẩn hóa error code và audit/request log.

### Giai đoạn 4 — Worker, cache và proxy

- [x] Tổng quát hóa worker polling cho nhiều provider.
- [x] Bảo đảm single polling owner và idempotent terminal state.
- [x] Tải ngay output có URL ngắn hạn về cache server.
- [x] Ghi hash/MIME/size và trạng thái output.
- [x] Tổng quát hóa secure output proxy.
- [x] Thêm retention/cleanup/observability.

### Giai đoạn 5 — Desktop

- [x] Chuyển service và WebView bridge sang video-generic.
- [x] Loại bỏ catalog hardcode khỏi vai trò nguồn sự thật.
- [x] Xác minh không có UI/request/setting cho phép chọn model.
- [x] Đổi nhãn/trạng thái/lỗi Kling-specific sang trung lập provider.
- [x] Hiển thị provider/model chỉ đọc nếu cần.
- [x] Chuẩn hóa native-audio metadata và đọc tương thích dữ liệu cũ.
- [ ] Kiểm tra preview/approve/regenerate/final render cho cả Kling và Seedance.

### Giai đoạn 6 — Xác minh và rollout

- [x] Chạy restore/build/test Release theo `AGENTS.md`.
- [x] Chạy web build (được kích hoạt trong Release build của `TOOL-LOCAL`).
- [x] Đạt toàn bộ test offline access control, pricing, worker, proxy, contract và migration tĩnh.
- [ ] Deploy server/schema khi đã có backup và runbook rollback.
- [ ] Nhập credential/rate bằng admin HTTPS; không chỉnh trực tiếp source/config.
- [ ] Smoke test có chi phí sau khi được phê duyệt.
- [ ] Bật Seedance theo từng tổ chức test trước, không bật toàn hệ thống ngay.
- [ ] Theo dõi failure rate, latency, settlement, storage và cleanup.
- [ ] Cập nhật tài liệu nghiệp vụ/runbook/sơ đồ bàn giao sau khi rollout ổn định.

## 12. Tiêu chí nghiệm thu

Hạng mục chỉ được coi là hoàn tất khi:

- Desktop không có cách chọn hoặc gửi provider/model.
- Server gắn provider/model snapshot đúng policy và không đổi ngầm giữa vòng đời project.
- Dự án Kling cũ tiếp tục submit/poll/download/preview/render được.
- Project Seedance có thể submit, tiếp tục chạy khi desktop đóng, tải qua proxy khi mở lại.
- Không trả plaintext API key, encrypted credential payload hoặc output URL gốc cho desktop.
- Thiếu credential, rate hoặc budget đều dừng trước outbound call.
- Chi phí Seedance được giữ và quyết toán bằng rate snapshot cùng actual usage.
- Không fallback tự động giữa provider.
- Output Seedance được lưu an toàn trước khi URL provider hết hạn.
- Clip được xác minh có audio native và vẫn bắt buộc người dùng duyệt trước render cuối.
- Access control, SSRF/DNS rebinding protection, MIME/size/redirect limit và project ownership đều có test.
- Migration chạy thành công và idempotent trên database clone.
- Release build và toàn bộ test đạt; số test chỉ được ghi vào mốc tài liệu khi đã thực sự chạy.
- Runbook có hướng dẫn cấu hình provider/model/rate/credential, rollout và rollback.

## 13. Rủi ro và phương án kiểm soát

| Rủi ro | Kiểm soát |
|---|---|
| URL output BytePlus hết hạn khi desktop đóng | Worker tải ngay về cache server và chỉ công bố proxy sau khi lưu an toàn |
| Giá provider thay đổi | Không hardcode/seed; Global Admin cấu hình rate có hiệu lực và snapshot theo request |
| Project đổi model giữa chừng | Snapshot bất biến trên project; policy mới chỉ áp dụng project mới |
| Chi phí kép do retry/fallback | Không fallback; idempotency và attempt/cost log tách bạch |
| Desktop cũ bị hỏng | Giữ endpoint/DTO Kling tương thích trong giai đoạn deprecation |
| Prompt/cảnh không hợp giới hạn Seedance | Snapshot trước content plan và validate capability trước outbound |
| Lộ key hoặc signed URL | Key chỉ ở server; log sanitization; output proxy tương đối |
| SSRF qua output URL | Allowlist, DNS/IP validation, redirect/MIME/size limit và server-managed storage key |
| Ảnh người thật bị từ chối | Chặn flow chưa được phê duyệt và ánh xạ lỗi moderation/reference rõ ràng |
| Cache đầy | Quota, retention, cleanup job, metrics và cảnh báo dung lượng |

## 14. Các file/khu vực dự kiến bị ảnh hưởng khi bắt đầu code

Danh sách này để định tuyến công việc, không phải cam kết tên file mới cuối cùng:

- `database/`: migration 4.0.x kế tiếp và least-privilege verification.
- `TOOL-SHARED.Contracts/Generation/`: request/response video chung và compatibility DTO.
- `TOOL-SERVER/Generation/`: resolver, service, router, adapters, polling worker, output cache/proxy.
- `TOOL-SERVER/Controllers/`: endpoint video chung và admin policy.
- `TOOL-SERVER` provider catalog/runtime/credential/pricing services.
- `TOOL-LOCAL`: project generation service, WebView bridge, media validation và metadata.
- `TOOL-LOCAL/Web`: TypeScript messages, trạng thái và text UI trung lập provider.
- `TOOL-TESTS`: unit, integration, security, worker, proxy, pricing và compatibility tests.
- `NGHIEP_VU_HE_THONG_VIDEOMAKER.md`: cập nhật nguồn sự thật nghiệp vụ sau khi implementation được nghiệm thu.
- `TRIEN_KHAI_AI_GATEWAY_TO_CHUC.md`: cập nhật runbook cấu hình/triển khai/rollback.

## 15. Tài liệu tham chiếu

### Nội bộ repository

- `README.md`.
- `NGHIEP_VU_HE_THONG_VIDEOMAKER.md`.
- `KE_HOACH_SERVER_AI_GATEWAY.md`.
- `TRIEN_KHAI_AI_GATEWAY_TO_CHUC.md`.
- `KE_HOACH_KLING_NATIVE_AUDIO.md`.

### BytePlus chính thức

- Create video generation task: <https://docs.byteplus.com/en/docs/ModelArk/1520757>
- Retrieve video generation task: <https://docs.byteplus.com/en/docs/ModelArk/1521309>
- API key: <https://docs.byteplus.com/en/docs/ModelArk/1541594>
- Region/base URL: <https://docs.byteplus.com/en/docs/ModelArk/2191806>
- ModelArk pricing: <https://docs.byteplus.com/en/docs/ModelArk/1544106>
- Video generation enhanced/reference capabilities: <https://docs.byteplus.com/en/docs/byteplus_las/video_gen_enhanced>

> Trước khi triển khai hoặc nhập giá, phải đọc lại tài liệu chính thức và dashboard/hợp đồng của tài khoản đang dùng. Model ID, capability, giới hạn, giá và chính sách provider có thể thay đổi.
