# Cài VideoMaker trên nhiều máy

## Trên máy mới

1. Cài **đầy đủ bộ ứng dụng Windows x64**. Không chép riêng `TOOL-LOCAL.exe` hoặc thư mục source.
2. Nếu máy thiếu WebView2, cửa sổ chuẩn bị sẽ mở trang Microsoft để cài Evergreen Runtime x64; cài xong bấm **Kiểm tra lại**.
3. Đăng nhập và bảo đảm license còn hiệu lực. VideoMaker chọn tổ chức đang hoạt động, mở màn hình dự án rồi tự kiểm tra FFmpeg, OCR, Qwen và Piper.
4. Nếu mọi thành phần đã **Sẵn sàng**, màn hình dự án dùng được ngay. Nếu còn thiếu hoặc hỏng, modal chuẩn bị bằng WebView hiển thị trên nền dự án; nền vẫn nhìn thấy nhưng bị khóa thao tác.
5. Chọn **OK - Cài đặt** để tải/cài Qwen, Piper và kiểm tra lại. Modal không đóng bằng `Esc` hoặc bấm ra ngoài; chọn **Hủy và thoát** hoặc đóng cửa sổ VideoMaker để hủy lượt đang chạy và thoát ứng dụng.
6. Nếu OCR/FFmpeg đi kèm bị thiếu hoặc hỏng, chọn **OK - Sửa bộ ứng dụng**. Updater tải package đã được server phân phối, kiểm tra package, đóng ứng dụng, sửa bộ cài rồi tự khởi động lại.

Luồng bắt buộc này áp dụng cho tài khoản có vai trò được phép chạy nghiệp vụ local. Viewer và màn hình xử lý license vẫn vào được giao diện phù hợp vì các vai trò/trạng thái đó không được phép bắt đầu cài đặt hoặc xử lý video. Sau khi vào ứng dụng, có thể kiểm tra lại tại **Cài đặt → Setup hệ thống**.

Qwen tải khoảng 2,50 GB. Piper tải model khoảng 63 MB cùng Python và các thư viện riêng; 63 MB không phải tổng dung lượng cài Piper. Nút **Kiểm tra hệ thống** chỉ kiểm file/runtime đã có, không tự tải phần thiếu. Mạng chỉ cần cho việc đăng nhập/xác minh quyền và tải dependency; nội dung phụ đề local không gửi đến nguồn tải model.

## Khi gặp lỗi

- **Cần kiểm tra**: model có thể còn đúng nhưng chưa có bằng chứng probe của máy/build hiện tại. Bấm kiểm tra; không cần tự xóa GGUF.
- **Chưa cài / Cần sửa**: bấm cài hoặc **Thử lại phần chưa đạt**. File đã tải hoàn tất và đúng hash được dùng lại; file tải dở có thể tải lại từ đầu.
- **RAM/bộ nhớ thấp**: đóng ứng dụng nặng trước khi thử lại. Nếu chọn tiếp tục, đọc và đánh dấu xác nhận cảnh báo. Xác nhận không vượt kiểm checksum, nền tảng hoặc probe thật.
- **OCR/FFmpeg thiếu hoặc hỏng**: bấm **OK - Sửa bộ ứng dụng**. Tiến độ package hiển thị ngay trong modal; ứng dụng đóng để updater áp dụng package đã xác minh.
- **Thành phần đang được sử dụng**: chờ OCR/dịch/giọng/xuất video hoặc lượt Setup khác kết thúc. Không chạy cài đồng thời với tác vụ đang dùng runtime.

Modal Setup lúc khởi động khóa chuyển trang, đổi dự án và mọi command nghiệp vụ cho tới khi các thành phần bắt buộc sẵn sàng. Sau khi vào ứng dụng, màn hình **Cài đặt → Setup hệ thống** vẫn cho phép chuyển trang trong lúc một lượt Setup tiếp tục chạy. Đổi tổ chức, logout, license mất hiệu lực hoặc đóng ứng dụng sẽ hủy lượt đang chạy. Khi mở lại, modal khởi động kiểm tra và cho phép tiếp tục cài. Thành phần bị tắt bằng feature flag không thuộc danh sách bắt buộc của lần khởi động đó.

## Vị trí và nâng cấp

Thành phần mới mặc định thuộc tài khoản Windows hiện tại trong LocalAppData, dùng chung giữa các dự án. Model Qwen/Piper đã có ở root legacy được nhận diện và dùng lại; không tự di chuyển hàng loạt dữ liệu. Python v2 cài trong thư mục phiên bản cuối, giữ model ONNX riêng để không tải lại khi sửa Python.

Chép marker READY từ máy khác không thay thế kiểm tra trên máy mới. Tài khoản Windows khác có thư mục thành phần riêng; không giả định việc cài cho một tài khoản đã cài cho mọi người dùng máy đó.

## Tình trạng phát hành

Chức năng Setup đã có trong source. Bộ publish local là ứng viên để kiểm tra, chưa phải release đã phê duyệt. Nghiệm thu Windows sạch/nâng cấp, ma trận RAM và hồ sơ phân phối FFmpeg phải hoàn tất trước khi phân phối diện rộng. Xem [báo cáo triển khai](BAO_CAO_SETUP_HE_THONG.md).
