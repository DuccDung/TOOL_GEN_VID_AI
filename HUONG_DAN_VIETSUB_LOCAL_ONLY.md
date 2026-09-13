# Chế độ Vietsub local

Ứng dụng mặc định chỉ mở nghiệp vụ Vietsub local. OCR Paddle, dịch Qwen, giọng Piper, biên tập phụ đề và xuất SRT/MP4 được xử lý trên máy. Đăng nhập, license, tổ chức và registry dự án tiếp tục dùng server hiện có.

## Cấu hình

Desktop và server cùng dùng cấu hình sau; thiếu mục này cũng mặc định bật:

```json
"Application": {
  "VietsubLocalOnly": true
}
```

- Desktop vẫn bật `Features:VietsubEnabled`, `VietsubOcrEnabled` và `VietsubLocalVoiceEnabled`.
- Chế độ này cho phép khởi tạo giao diện/cài đặt Qwen dù flag legacy `VietsubLocalTranslationEnabled` còn `false`. Khả năng chạy thực tế vẫn phụ thuộc checksum, fingerprint, probe model/worker, tài nguyên và xác nhận cảnh báo hiện hành. Đây là quyền sử dụng workflow local theo cấu hình mới, không phải bằng chứng model đã đạt nghiệm thu.
- Server đặt `VietsubCloudTranslation:Enabled=false`. Khi `VietsubLocalOnly=true`, hậu cấu hình server buộc Cloud tắt kể cả nếu nguồn cấu hình khác bật cờ Cloud.
- `appsettings.user.json` của desktop được merge như hiện hành. Khi kiểm tra cấu hình hiệu lực, cần đối chiếu cả mục `Application` trong file này; giao diện không cung cấp công tắc mở lại AI Cloud.
- Desktop không yêu cầu connection string SQL workflow trong chế độ này. Server vẫn cần database của auth/license/organization/registry.
- Sau thay đổi cấu hình, khởi động lại đúng tiến trình để nạp cấu hình. Không tự dừng server/IDE đang dùng.

## Những luồng được khóa

Giao diện chỉ giữ điều hướng Vietsub và cài đặt local. Lệnh tạo video dài/ngắn, project video, tạo nội dung/ảnh/clip, repair, preview giọng Cloud, TTS/ASR, thiết lập provider và render của workflow video bị chặn tại WebView bridge. Factory SQL workflow từ chối truy cập khi đang ở chế độ local. HTTP client desktop chặn API video và POST tạo/tiếp tục/thử lại Dịch Cloud.

Server dùng resource filter để chặn mutation API generation/project video và submission Cloud trước xử lý nội dung. Provider resolver từ chối lấy runtime cho lượt mới trước truy vấn credential; HTTP handler của các runtime provider cũng chặn submission. Mã lỗi dùng chung là `vietsub_local_only` với HTTP 403.

Chặn Cloud ở cả service desktop và local job manager: không lưu snapshot/job mới từ lệnh dịch bị khóa; job Cloud cũ không thể start/resume/retry thành lượt mới. Thao tác hủy hoặc tạm dừng vẫn được giữ để giải phóng luồng local theo quy tắc hiện hành.

Request đã gửi provider trước khi khóa vẫn cần được theo dõi và quyết toán. GET trạng thái/output, cancellation, settlement, đối soát Unknown và cleanup tiếp tục hoạt động; không tắt toàn bộ hosted worker, xóa ledger hoặc tự release reservation chưa chắc chắn. Cloud worker vẫn thực hiện settlement trước khi kiểm tra khả năng dispatch lượt tiếp theo.

## Luồng sử dụng

1. Đăng nhập, xác minh license và chọn tổ chức.
2. Tạo/mở dự án Vietsub, nhập video COPY hoặc LINK.
3. Quét OCR tiếng Anh/Trung, kiểm tra và chỉnh các cue.
4. Mở Dịch tiếng Việt để cài/kiểm tra Qwen rồi dịch trên máy.
5. Sửa bản dịch, bật hoặc bỏ qua giọng theo câu; cài Piper nếu cần và tạo giọng Việt.
6. Thiết kế phụ đề, chỉnh mixer, nghe preview rồi xuất SRT hoặc MP4 bằng FFmpeg.

SRT nhập ngoài vẫn được biên tập; dịch tự động hiện chỉ nhận active track `PADDLE_OCR_LOCAL` có cue, ngôn ngữ `en`/`zh` và revision khớp. Câu sửa tay/khóa, kiểm tra snapshot, cache và ghi file atomically giữ nguyên. Thiếu component phải hiển thị `NOT_INSTALLED`/lỗi readiness, không tự chuyển Cloud. Các lần cài component có thể tải runtime/model đã pin; không gửi nội dung phụ đề cho provider Cloud.

## Kiểm chứng

Kết quả thực chạy trên worktree local ngày 2026-09-13 nằm ở mục 23 của [KIEM_THU_VA_NGHIEM_THU.md](KIEM_THU_VA_NGHIEM_THU.md), gồm model Qwen/Piper thật và giới hạn của bằng chứng.

- `VietsubLocalOnlyTests`: lệnh desktop, API server, HTTP gateway/provider, phục hồi job Cloud cũ và truy cập SQL workflow bị khóa.
- `LicenseSessionManagerTests.LocalOnlyDashboard_LoadsAndSwitchesOrganizationWithoutWorkflowSqlOrProviderConfiguration`: đăng nhập/lease và chuyển tổ chức vẫn hoạt động với ngân sách AI 0, không có service SQL video hoặc provider.
- `LocalOnlyNavigation.test.tsx`: menu, cài đặt và tạo dự án Vietsub, bảo vệ bản nháp.
- `VietsubCloudTranslation.test.tsx`: không có lựa chọn Cloud/readiness/submission trong chế độ local, vẫn cài Qwen được.
- `VietsubSettingsPanel.test.tsx`: job Cloud cũ đang tạm dừng, gián đoạn hoặc thất bại không hiện nút tiếp tục/thử lại; vẫn cho hủy.
- Chạy restore/build/test solution và `npm ci`, `npm run build`, `npm test` theo `AGENTS.md`.
- Verify model thật, benchmark và nghe smoke là bước riêng theo `KIEM_THU_VA_NGHIEM_THU.md`; test bị Skipped không chứng minh model sẵn sàng.

## Mở lại nghiệp vụ đầy đủ khi có yêu cầu

Đặt `Application:VietsubLocalOnly=false` trên cả desktop và server, rồi khởi động lại tiến trình tương ứng. `Features:VietsubLocalTranslationEnabled` trở lại là flag điều khiển Qwen trong chế độ đầy đủ. Cloud chỉ mở khi được bật riêng và đủ model, rate, credential, ngân sách, quyền và điều kiện vận hành. Đổi mode không xóa project, subtitle, cache, credential hoặc lịch sử chi phí; không cần migration cho thay đổi này.
