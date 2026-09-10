# Sửa lỗi tải video ngắn — 2026-09-11

Video Veo đã hoàn tất nhưng desktop chưa tải được kết quả. Hai nguyên nhân được xác minh trên Development `DUNGDEV / VideoFactory`: các lần đọc trạng thái cùng tiêu thụ giới hạn 30 yêu cầu/phút của thao tác tạo AI; worker ở checkout `vid-long` lưu tệp vào thư mục khác server phục vụ desktop.

## Thay đổi

- GET trạng thái video, lỗi nội dung, trạng thái phối đồ và tải kết quả sử dụng `ai-status` (120/phút). Các thao tác tạo vẫn dùng `ai-gateway` (30/phút). Server trả `Retry-After` khi từ chối vì tần suất.
- Desktop chỉ thử lại GET không có body khi gặp 429, tối đa hai lần, tuân thủ `Retry-After` trong giới hạn 60 giây mỗi lần. Không tự thử lại POST tạo AI; cancellation vẫn kết thúc việc chờ.
- Dashboard trả trạng thái provider và khả năng tiếp tục lấy kết quả. Hai composer mở tab Video khi đã có kết quả hoặc có tác vụ cần tải tiếp, hiển thị lỗi tải ở project và ưu tiên nút tải lại tác vụ đã gửi.
- Bridge dùng `resumeOnly` cho cả hai chế độ video ngắn. Nếu tác vụ đúng idempotency/ảnh hiện hành không còn hoặc đã Failed/Cancelled/Expired, dừng trước submit. Việc tải lại giữ nguyên các bước xác minh lineage, media, audio và duyệt thủ công.
- Output proxy kiểm SHA-256 trên cùng file handle trước khi gửi header/byte video. Thiếu tệp hoặc nội dung thay đổi không được coi là tải thành công.

## Kho video của môi trường Development

Các instance chia sẻ database phải cùng truy cập một kho video thực tế, cấu hình bằng đường dẫn tuyệt đối `Generation:VideoOutputs:StorageRoot`. Không dùng `data/video-outputs` tương đối khi khởi chạy nhiều checkout hoặc worker. Server cảnh báo nếu cấu hình vẫn là đường dẫn tương đối; không tự chuyển hoặc tìm tệp ngoài kho được cấu hình.

Máy đang chạy có server `vid-long` dùng kho hiện hữu tại `D:\laptrinhweb\code_outsrc\TOOL_AUTO_GEN_POST_VIDEO\vid-long\TOOL_GEN_VID_AI\TOOL-SERVER\data\video-outputs`. Development user-secrets chung của các checkout đã được cấu hình trỏ tuyệt đối tới kho này, giữ tương thích với server dài đang chạy. Hai tệp còn hạn đã được đối chiếu database, dung lượng và SHA-256, rồi sao chép qua tệp tạm trước khi promote; giữ nguyên bản gốc. Bản clip ngắn cũng được đặt lại vào kho tương đối cũ để phục hồi server trước khi khởi động lại.

Kho này là dữ liệu vận hành: không xóa checkout `vid-long` hoặc dọn thư mục data như output build. Khi cần chuyển kho ra ngoài checkout hoặc sang máy khác, dừng tác vụ mới, xác minh các job đang chạy, sao chép và đối chiếu toàn bộ tệp còn hạn, cấu hình tất cả instance cùng đường dẫn/mount rồi restart có kiểm soát. Không sửa ledger hoặc tạo lại provider request để bù lỗi đường dẫn.

Không có migration hoặc SQL ghi dữ liệu; không thay credential, rate hoặc gọi tạo video có phí. Video đã có vẫn cần tải vào workspace và được người dùng xem/duyệt trước khi xuất.

## Kiểm chứng

Regression bao phủ GET 429/backoff/cancel và không replay POST; endpoint đọc dùng policy riêng; tệp thiếu được phục hồi trên cùng request; tệp cùng dung lượng nhưng khác hash bị chặn; dashboard và hai composer cho tải tiếp mà không báo giá mới; resume với request thiếu/Failed bị chặn trước outbound; tải lại media đã có qua FFmpeg.

Xác minh cuối: restore, build Release toàn solution và Debug desktop đạt, 0 warning/error MSBuild. Frontend `npm ci`, build và **196 Passed / 0 Failed / 0 Skipped**. .NET **1.254 Passed / 0 Failed / 5 Skipped** (4 bài model opt-in và 1 bài SQL rehearsal opt-in). Không tính các bài bỏ qua là đã nghiệm thu. Bộ hồi quy tập trung 35 Passed / 0 Failed / 0 Skipped.

Đã cập nhật binary Development, restart đúng server HTTPS 7242 sau khi kiểm không có provider request đang chạy; giữ server `vid-long` hoạt động. Bundle `wwwroot` của Debug/Release khớp SHA-256 với frontend source build. GET không đăng nhập vào auth và video content trả 401; log startup không còn cảnh báo storage tương đối. Không dùng smoke không đăng nhập để khẳng định đã tải thành công qua phiên người dùng.

Mở lại VideoMaker, chọn dự án cũ và bấm **Tiếp tục / tải lại video đã gửi**. Nút này chỉ tiếp tục tác vụ đã có; sau tải thành công cần xem hết, duyệt video rồi chuẩn bị/xuất MP4. Chưa tự chạy thao tác trong phiên desktop của người dùng hoặc duyệt thay người dùng.

Log tại `artifacts/short-video-recovery`; bản sao DLL/EXE cũ ở đường dẫn ghi trong `binary-backup-path.txt` (không phải gói rollback phát hành đầy đủ). Không coi kiểm tra fixture là nghiệm thu video AI mới.
