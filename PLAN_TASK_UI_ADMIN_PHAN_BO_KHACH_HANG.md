# Triển khai UI Admin phân bổ khách hàng

Admin thiết lập nhóm phân bổ theo bốn màn đã duyệt: tổng quan, tổ chức và sức chứa, kiểm tra sẵn sàng, gắn gói và mở bán. UI chỉ rõ điều kiện thiếu và dẫn tới nơi cấu hình. Trạng thái sẵn sàng và số chỗ cấp cho khách tiếp tục được server xác minh.

Ngày 2026-10-13, nhánh `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree có thay đổi chưa commit từ các công việc trước. Mockup nằm tại `artifacts/admin-provisioning-ui-mockups-20261013`.

## Task và tiêu chí nghiệm thu

| Task | Nội dung | Tiêu chí | Trạng thái |
|---|---|---|---|
| 1 | Đối chiếu API, quyền và luồng hiện hành | Dùng API Admin hiện có; giữ xác minh server; không đổi DTO/schema | Hoàn thành |
| 2 | Tổng quan và tiến trình 4 bước | Số liệu thật; phân biệt chưa cấu hình, chưa sẵn sàng, hết chỗ và SePay chưa bật | Hoàn thành |
| 3 | Form tổ chức và sức chứa | Sửa/thêm tổ chức, giới hạn theo số đang dùng/giữ; sau lưu chuyển bước kiểm tra | Hoàn thành |
| 4 | Kiểm tra sẵn sàng và đường quay lại | Hiển thị kết quả server, hướng dẫn ngân sách/OpenAI/policy/pricing; quay lại đúng nhóm | Hoàn thành |
| 5 | Chọn nhiều gói và bật nhóm | Không chuyển gói khác âm thầm; đọc lại điều kiện trước lưu; giữ hạn mức gói đã có; báo lỗi lưu một phần | Hoàn thành |
| 6 | Giữ quản lý chi tiết và accessibility | Có cấu hình legacy/audit/retry; bàn phím, responsive, loading/error/retry, bỏ phản hồi cũ | Hoàn thành |
| 7 | Kiểm thử, build và kiểm tra ảnh | Browser API giả, hồi quy Admin, frontend và .NET; ghi riêng Passed/Failed/Skipped | Hoàn thành |

## Phạm vi và điều kiện

Razor/JavaScript hiện hành được giữ. Module mới `admin-allocation-setup.js` và CSS riêng tạo luồng hướng dẫn; khu vực quản lý chi tiết vẫn có chỉnh sửa/gỡ mapping, sức chứa và nhật ký phân bổ. Không dùng ảnh mockup như dữ liệu runtime.

Thêm tổ chức gửi `isReady=false`; nút kiểm tra gửi yêu cầu xác minh qua API hiện có. UI đọc trạng thái sẵn sàng trong pool và dữ liệu hướng dẫn AI; không tự cấp trạng thái READY. Mở bán đọc lại các nhóm/gói, cấu hình SePay và pool; lưu mapping từng gói rồi mới bật nhóm nháp. Nếu một bước lỗi, liên kết đã lưu được ghi rõ và UI tải lại trạng thái. Với nhóm đã Active, liên kết mới có thể mở bán ngay khi lưu thành công; UI giải thích rõ vì API hiện có lưu từng gói, không có transaction cho cả thao tác. Gói thuộc nhóm khác phải xử lý liên kết qua quản lý chi tiết; không tự chuyển. Khi logout hoặc đổi context, UI bỏ phản hồi cũ và dừng các bước ghi tiếp theo.

Không chạy migration, viết SQL, thay credential/rate/budget thật, bật thanh toán thật, gọi provider có phí hoặc rollout trong lượt triển khai source. Kiểm chứng browser sử dụng dữ liệu giả và chặn outbound. Smoke phiên Admin/database thật và rollout cần được ghi riêng.

## Kiểm chứng trên working tree

| Kiểm tra | Passed | Failed | Skipped | Phạm vi |
|---|---:|---:|---:|---|
| Frontend | 296 | 0 | 0 | 45 file Vitest; npm ci và npm run build đã chạy |
| Browser Admin | 30 | 0 | 0 | 12 ca luồng mới, 9 shell và 9 SePay; Edge headless, Razor/JS/CSS thực, API giả |
| Full .NET Release | 1651 | 0 | 13 | 1664 tổng, collection tuần tự; 6 phút 55 giây; model/SQL opt-in bị skip không tính là nghiệm thu |

Restore và Release build toàn solution đã đạt, MSBuild **0 Warning / 0 Error**; Vite vẫn có cảnh báo chunk dashboard >500 kB. Browser kiểm 4 bước ở 320/390/768/1024/1440 px, focus bàn phím, XSS, loading/error/retry, thiếu sức chứa/SePay, liên kết đổi trước lưu, ghi lặp, lỗi lưu một phần và logout trước bật nhóm. Đã xem ảnh desktop/mobile.

Build Release cuối đã đồng bộ thay đổi static assets sau kiểm tra UI; SHA-256 assembly server và assembly test giữ nguyên so với lượt full .NET. [TRX full .NET](artifacts/admin-allocation-ui-20261013/admin-allocation-final.trx).

Ảnh: [tổng quan](artifacts/admin-allocation-ui-20261013/screenshots/01-overview-1440.png), [tổ chức](artifacts/admin-allocation-ui-20261013/screenshots/02-organization-1440.png), [kiểm tra](artifacts/admin-allocation-ui-20261013/screenshots/03-readiness-1440.png), [gắn gói](artifacts/admin-allocation-ui-20261013/screenshots/04-plans-1440.png).

Đọc HTTP không xác thực tại `https://localhost:7202` xác nhận JS tổ chức có loader mới và nội dung module/CSS hướng dẫn khớp source. Trang Razor đang chạy dùng tên tài nguyên fingerprint cũ; loader hỗ trợ nạp hướng dẫn ngay cả khi Razor cũ chưa có script/link mới. Dùng **Ctrl+F5** để bỏ cache trình duyệt, vào **Tổ chức & AI → Phân bổ khách hàng**, chọn **Tiếp tục thiết lập**. Kiểm tra tài nguyên HTTP này không thay thế smoke đăng nhập/thao tác lưu vào database thật.
