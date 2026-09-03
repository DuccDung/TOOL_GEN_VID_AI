# Hướng dẫn kiểm thử tạo video bằng Fal/Veo

Tài liệu này hướng dẫn smoke test Fal/Veo qua đúng luồng `TOOL-LOCAL -> TOOL-SERVER -> Fal Queue`. Không gọi Fal trực tiếp từ desktop, không ghi `FAL_KEY` vào source hoặc `appsettings.json`.

## 1. Phạm vi kiểm thử

- Fal/Veo chỉ dùng cho workflow `LongForm` (Video dài).
- Hệ thống hiện dùng Image-to-Video, độ phân giải `720p`, Native Audio.
- Hai endpoint được phê duyệt:
  - Standard: `fal-ai/veo3.1/image-to-video`
  - Fast: `fal-ai/veo3.1/fast/image-to-video`
- Duration hợp lệ: `4`, `6` hoặc `8` giây.
- Tỷ lệ hợp lệ: `16:9` hoặc `9:16`.
- Lần đầu chỉ test một cảnh Fast 4 giây, không tạo toàn bộ video dài.

## 2. Điều kiện trước khi test

### Database và server

- Database đã chạy migration đến `4.0.9-fal-veo-long-form`.
- `TOOL-SERVER` đã bootstrap provider `fal` và hai model Veo ở trạng thái Disabled.
- Server chạy bằng HTTPS và worker nền đang hoạt động.
- Desktop và trang Admin phải trỏ tới cùng một instance `TOOL-SERVER`.
- Server được phép kết nối outbound tới:
  - `api.fal.ai:443` để kiểm tra credential.
  - `queue.fal.run:443` để submit và polling.
  - `fal.media` và chính xác `storage.googleapis.com` để tải output.
- FFmpeg và FFprobe trên desktop đã qua preflight.

### Tài khoản Fal

- Tài khoản Fal đã bật billing hoặc có đủ credit.
- Có `FAL_KEY` truy cập được cả endpoint Standard và Fast.
- Đơn giá phải lấy từ Fal Dashboard/hợp đồng hiện hành. Không dùng giá cũ trong tài liệu làm nguồn sự thật.
- Không gửi `FAL_KEY` qua chat, email hoặc log.

### Người dùng và tổ chức

- Global Admin dùng để tạo tổ chức, bật provider/model và nhập bảng giá.
- Owner hoặc OrganizationAdmin dùng để cấu hình credential và policy.
- Owner, OrganizationAdmin, BillingManager hoặc Member có thể phát sinh AI.
- Viewer không được phép tạo request AI.
- Người dùng desktop có license, device lease và membership Active.
- Budget tổ chức và hạn mức thành viên lớn hơn `0`.

## 3. Tạo tổ chức test

1. Đăng nhập trang Admin HTTPS.
2. Mở **Tổ chức & AI**.
3. Tạo tổ chức riêng, ví dụ `fal-staging`.
4. Chọn currency phù hợp với bảng giá, thông thường là `USD`.
5. Đặt budget nhỏ đủ cho các request đã dự kiến.
6. Thêm tài khoản sẽ dùng trên desktop và gán role phù hợp.
7. Nếu dùng member limit, đặt giới hạn nhỏ tương ứng.

Budget bằng `0` nghĩa là khóa AI, không phải không giới hạn.

## 4. Cấu hình rate Fal Fast

Trong **Bảng giá AI**:

1. Tìm provider Fal và model `fal-ai/veo3.1/fast/image-to-video`.
2. Tạo rate với:
   - Usage type: `VideoSecond`
   - Unit: `Second`
   - Unit price: đúng giá hiện hành trên Fal Dashboard
   - Currency: cùng currency với budget tổ chức
   - Resolution: `720p`
   - Native Audio: `true`
   - Endpoint ID: `fal-ai/veo3.1/fast/image-to-video`
   - Effective from: thời điểm hiện tại
   - Active: bật
3. Lưu rate.
4. Bật model Fast.
5. Bật provider Fal.

Không tạo request nếu Admin còn báo `pricing_not_configured`.

## 5. Cấu hình credential

1. Chọn tổ chức `fal-staging`.
2. Mở provider Fal.
3. Nhấn **Cấu hình credential**.
4. Dán `FAL_KEY` qua form HTTPS.
5. Đặt tên dễ truy vết, ví dụ `Fal staging`.
6. Kiểm tra và lưu.
7. Chỉ tiếp tục khi credential có trạng thái `Active`.

Credential test của hệ thống yêu cầu key nhìn thấy cả hai endpoint Veo được phê duyệt. Nếu test thất bại, credential Active cũ phải được giữ nguyên.

## 6. Chọn policy LongForm

1. Trong tổ chức test, tìm **Policy Video dài - LongForm**.
2. Chọn Fal/Veo Fast.
3. Xác nhận `720p` và Native Audio.
4. Lưu policy.
5. Giữ policy `Default`/Video ngắn ở provider hiện tại, không chọn Fal.
6. Kiểm tra readiness của Fal hiển thị **Sẵn sàng**.

Dự án đã snapshot provider/model sẽ không tự đổi policy. Luôn tạo dự án Video dài mới sau khi lưu policy Fal.

## 7. Chuẩn bị OpenAI và nội dung dự án

Workflow Video dài cần OpenAI để sinh content plan và storyboard. Trong cùng tổ chức test phải có:

- Credential OpenAI Active.
- Model/rate OpenAI cần thiết đã cấu hình.
- Budget còn đủ cho bước sinh nội dung.

Nếu OpenAI chưa sẵn sàng, quy trình sẽ dừng trước bước gọi Fal.

## 8. Tạo dự án và chọn cảnh test

1. Mở desktop và đăng nhập bằng thành viên của tổ chức test.
2. Chọn đúng tổ chức hiện hành.
3. Tạo một dự án **Video dài** mới.
4. Chọn tỷ lệ `16:9` hoặc `9:16`.
5. Sinh content plan và storyboard.
6. Chọn một cảnh đơn giản:
   - Đúng một nhân vật.
   - Nhân vật nói trực diện.
   - Ít chuyển động.
   - Câu thoại tiếng Việt ngắn.
   - Ưu tiên duration provider 4 giây.
7. Không dùng B-roll cho smoke test đầu tiên.
8. Xác nhận/lock các asset được gán cho cảnh.

## 9. Chuẩn bị first-frame

Ảnh đầu vào phải:

- Là PNG hoặc JPEG.
- Không quá 8 MB.
- Với `16:9`: tối thiểu 1280x720.
- Với `9:16`: tối thiểu 720x1280.
- Có tỷ lệ gần như chính xác với tỷ lệ dự án.
- Được duyệt làm reference chính của nhân vật.
- Chỉ có một nhân vật rõ ràng cho cảnh đối thoại.

Không dùng trực tiếp ảnh vuông 1024x1024 do luồng GPT-Image hiện tại tạo. Hệ thống không tự crop ảnh sang `16:9` hoặc `9:16`; lần đầu nên import thủ công một ảnh đúng tỷ lệ.

## 10. Chạy smoke test Fast

1. Trong Storyboard, chỉ chọn một cảnh.
2. Nhấn tạo video cho cảnh đã chọn.
3. Trước khi xác nhận, kiểm tra hộp chi phí:
   - Provider: `fal`
   - Model: `fal-ai/veo3.1/fast/image-to-video`
   - Resolution: `720p`
   - Native Audio: bật
   - Duration: ưu tiên `4s`
   - Chi phí dự kiến khớp rate đã nhập
4. Nếu hộp thoại hiển thị Kling, Standard hoặc rate không đúng, hủy request và kiểm tra lại policy/snapshot.
5. Xác nhận gửi request.
6. Lần đầu giữ desktop mở để quan sát.

Luồng trạng thái dự kiến:

```text
Submitting
-> WaitingProvider
-> Downloading
-> AudioReviewRequired
```

## 11. Tiêu chí đạt

Smoke test đạt khi:

- Chỉ có một provider request cho cùng idempotency key.
- Request dùng provider `fal` và đúng endpoint Fast.
- Request ghi nhận credential version và rate snapshot.
- Budget được reserve rồi settle/release đúng.
- Worker polling task đến trạng thái hoàn tất.
- Server tải và cache output thành công.
- Desktop chỉ nhận URL proxy tương đối, không nhận URL Fal gốc.
- FFprobe nhận được cả video và audio.
- Video đúng tỷ lệ, không crop sai chủ thể.
- Câu tiếng Việt được đọc đủ và đúng người nói.
- Lip-sync ở mức chấp nhận được.
- Cảnh chuyển sang trạng thái cần review, không tự Approved.
- Chi phí trong Usage Ledger khớp với Fal Dashboard theo rate snapshot.

Sau khi nghe/xem đạt, người dùng duyệt cảnh thủ công.

## 12. Kiểm tra polling khi desktop đóng

Chỉ thực hiện sau khi smoke test đầu tiên thành công:

1. Gửi thêm một request Fast ngắn.
2. Chờ request vào `WaitingProvider`.
3. Đóng desktop nhưng giữ `TOOL-SERVER` chạy.
4. Chờ Fal hoàn thành.
5. Mở lại desktop.
6. Xác nhận request cũ được tiếp tục tải và không tạo request mới.

## 13. Kiểm thử Standard

Sau khi Fast đạt:

1. Tạo rate cho `fal-ai/veo3.1/image-to-video` với giá hiện hành.
2. Bật model Standard.
3. Đổi policy LongForm sang Standard.
4. Tạo dự án Video dài mới vì dự án cũ đã snapshot Fast.
5. Test một cảnh 4 giây.
6. Đối chiếu chất lượng, queue latency, tổng thời gian và chi phí với Fast.

## 14. Lỗi thường gặp

| Mã/lỗi | Cách kiểm tra |
|---|---|
| `provider_disabled` | Bật provider Fal trong bảng giá AI. |
| `model_disabled` | Bật đúng model Fast hoặc Standard. |
| `pricing_not_configured` | Kiểm tra rate Active, effective time, `VideoSecond`, `Second`, `720p`, Native Audio và exact endpoint. |
| `credential_missing` | Kiểm tra credential của đúng tổ chức đã Active. |
| Credential không thấy đủ model | Kiểm tra quyền của key đối với cả Standard và Fast trên Fal. |
| `budget_exceeded` | Kiểm tra budget tổ chức, budget period và member limit. |
| Hộp chi phí vẫn hiện Kling | Dự án đã snapshot policy cũ; tạo dự án mới sau khi lưu policy Fal. |
| Ảnh bị từ chối | Kiểm tra MIME, kích thước, độ phân giải và tỷ lệ ảnh. |
| Không tải được output | Kiểm tra allowlist, DNS, HTTPS, MIME, giới hạn file và dung lượng cache server. |
| Video tải được nhưng không duyệt | Nghe audio, kiểm tra FFprobe và trạng thái `AudioReviewRequired`/`NativeAudioInvalid`. |

## 15. Nguyên tắc an toàn

- Không thử toàn bộ video 75 giây ngay từ đầu.
- Không bật Standard trước khi Fast đã đạt smoke test.
- Không thay rate đang phục vụ request chạy dở; tạo rate mới theo thời gian hiệu lực.
- Không revoke credential khi task còn đang chạy.
- Không disable provider/model cho tới khi task hiện tại đã terminal và output đã cache.
- Không chạy paid request nếu chưa chốt mức trần chi phí.
- Không dùng giá công khai làm cấu hình nếu Fal Dashboard/hợp đồng hiển thị giá khác.
- Không log prompt đầy đủ, ảnh Base64, authorization header, encrypted payload hoặc URL output gốc.

Tài liệu API chính thức:

- [Fal Veo 3.1 Image-to-Video](https://fal.ai/models/fal-ai/veo3.1/image-to-video/api)
- [Fal Veo 3.1 Fast Image-to-Video](https://fal.ai/models/fal-ai/veo3.1/fast/image-to-video)
