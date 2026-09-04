# Hướng dẫn cấu hình SePay cho gia hạn license

Tài liệu này dùng cho môi trường development/staging trước khi đưa thanh toán thật vào production. Source code không chứa tài khoản ngân hàng thật.

## 1. Thành phần đã triển khai

- Desktop vẫn mở khi license `Missing`, `Expired`, `Suspended`, `Revoked` hoặc vướng giới hạn thiết bị.
- Overlay khóa toàn bộ nghiệp vụ app, nhưng vẫn cho phép đăng xuất, tải danh sách gói, tạo thanh toán và kiểm tra trạng thái.
- Server tự lấy giá/thời hạn từ `auth.LicensePlans`, tạo mã chuyển khoản và QR, nhận webhook SePay rồi cấp/gia hạn license.
- Desktop không gọi SePay trực tiếp.

## 2. Migration cần áp dụng có kiểm soát

File migration: `database/VideoFactory.4.0.10.LicenseSepayPayments.sql`.

Trước khi chạy trên một database thật phải xác minh đủ instance, database, bản backup và khả năng restore. Nên chạy hai lần trên database staging có bản sao dữ liệu gần production để kiểm tra tính idempotent. Không chạy migration production chỉ từ hướng dẫn này.

## 3. Cấu hình server

Section cấu hình là `Payments:Sepay`. Với environment variables của ASP.NET Core, dùng tên sau:

```text
Payments__Sepay__Enabled=true
Payments__Sepay__QrBaseUrl=https://qr.sepay.vn/img
Payments__Sepay__ReceiverBankCode=<MA_NGAN_HANG>
Payments__Sepay__ReceiverAccountNumber=<SO_TAI_KHOAN>
Payments__Sepay__ReceiverAccountName=<TEN_CHU_TAI_KHOAN>
Payments__Sepay__TransferCodePrefix=VM
Payments__Sepay__PaymentExpireMinutes=15
```

Yêu cầu an toàn:

- Chỉ lưu tài khoản thật trong environment variable, user-secrets hoặc secret store của hạ tầng.
- `QrBaseUrl` phải là HTTPS và thuộc host `qr.sepay.vn` hoặc `vietqr.app`; nên dùng endpoint SePay ở trên.
- Desktop CSP chỉ cho ảnh QR từ đúng hai host này, đồng thời không gửi referrer khi tải ảnh; endpoint khác sẽ bị chặn cho tới khi được rà soát và cập nhật source.
- Khi `Enabled=true` nhưng thiếu cấu hình bắt buộc, server fail khi khởi động. Khi `Enabled=false`, API tạo thanh toán trả trạng thái tạm ngừng và app vẫn bị khóa nghiệp vụ.
- Không đưa các giá trị thật vào `appsettings.json`, log, ticket hoặc tài liệu bàn giao công khai.

## 4. Khai báo gói bán

Trong trang quản trị `Gói sử dụng`, mỗi gói muốn hiển thị ở desktop phải có:

- `Cho phép cấp gói này` được bật.
- `Hiển thị để người dùng mua` được bật.
- Giá bán VND là số nguyên dương.
- Thời hạn mặc định từ 1 đến 3.650 ngày.
- Thứ tự hiển thị và danh sách quyền lợi dạng text, mỗi dòng một mục.

Server là nguồn sự thật của giá và thời hạn. Client chỉ gửi `LicensePlanId` cùng `IdempotencyKey` và không thể ghi đè giá.

Phiên bản hiện tại dùng chính `TransferCode` do server sinh làm nội dung chuyển khoản. Trước rollout phải quét QR bằng ứng dụng ngân hàng đích và xác minh nội dung webhook còn chứa nguyên mã này. Một số ngân hàng/tài khoản định danh có quy tắc thêm tiền tố hoặc biến đổi nội dung; nếu tài khoản nhận thực tế yêu cầu định dạng như vậy thì chưa được bật production cho tới khi cấu hình/mapping tương ứng được triển khai và kiểm thử. Tham chiếu định dạng QR và lưu ý theo ngân hàng tại [tài liệu QR chính thức của SePay](https://developer.sepay.vn/vi/tien-ich-khac/tao-qr-code).

## 5. Cấu hình webhook trên SePay

- Sự kiện: giao dịch tiền vào.
- Method: `POST`.
- URL: `https://<server-public>/api/payments/sepay/webhook`.
- Authentication: không xác thực theo phạm vi MVP; không cần cấu hình header `Authorization`.
- Nên lọc theo đúng tài khoản nhận và prefix `VM` để giảm webhook không liên quan.
- Endpoint công khai không có bằng chứng mật mã rằng request thật sự đến từ SePay. Chỉ bật sau khi đã chấp nhận rủi ro giả mạo; nên giới hạn IP tại reverse proxy nếu có dải IP chính thức được vận hành xác minh.

Server chỉ fulfillment khi đồng thời đúng:

- giao dịch tiền vào;
- tài khoản nhận;
- mã chuyển khoản duy nhất;
- số tiền chính xác;
- provider transaction ID chưa từng được xử lý.

Webhook trùng được trả thành công nhưng không cấp license lần hai. Giao dịch đến sau khi QR hết hạn vẫn được xử lý nếu tiền thực nhận khớp hoàn toàn.

## 6. Kịch bản kiểm tra staging

1. Apply migration trên database staging và chạy lại lần hai.
2. Khai báo một gói public có giá thử nghiệm phù hợp với môi trường staging.
3. Bật cấu hình SePay staging bằng tài khoản nhận lưu ngoài source.
4. Đăng nhập bằng user không có license hoặc đã hết hạn; xác nhận app mở và overlay không thể đóng.
5. Chọn gói, xác nhận ảnh QR tải được trong WebView, quét bằng ứng dụng ngân hàng đích và kiểm tra đúng tài khoản/số tiền/nội dung chuyển khoản.
6. Gửi webhook mô phỏng với sai tài khoản, sai mã và sai số tiền; xác nhận không cấp license.
7. Gửi một webhook hợp lệ; xác nhận desktop tự polling, activate thiết bị và mở khóa không cần đăng nhập lại.
8. Gửi lại cùng provider transaction ID; xác nhận không gia hạn lần hai.
9. Kiểm tra log/audit không chứa toàn bộ payload.

Không dùng giao dịch ngân hàng thật hoặc webhook production trong kiểm thử tự động.

## 7. Theo dõi và đối soát

- Structured log của payment đã match có `LicensePaymentId`, `OrderCode`, `PaymentStatus`; webhook chưa match chỉ ghi provider transaction ID và lý do tổng quát.
- Meter `VideoMaker.Payments` phát counter `videomaker.license_payment.events`. Tag `event` chỉ có `created`, `fulfilled`, `expired`, `duplicate`, `unmatched`; sự kiện unmatched có thêm `reason` thuộc tập cố định.
- Cấu hình metrics exporter của môi trường để thu counter này; source không tự chọn hoặc tự gửi dữ liệu tới một hệ thống quan sát bên ngoài.
- Global Admin có thể gọi `GET /api/admin/licenses/payments?search=<ORDER_OR_PROVIDER_ID>&status=<STATUS>&take=100`. Tra cứu là exact match, `take` từ 1 đến 200; response không có tài khoản nhận, idempotency key, entitlement, provider reference hoặc raw webhook.
- Trạng thái `Paid` và `Fulfilled` được ghi trong cùng transaction ở luồng chuẩn. Vẫn phải tạo cảnh báo hạ tầng nếu database có bản ghi `Paid` tồn tại quá ngưỡng vận hành và dùng API Admin với `status=Paid` để đối soát.

## 8. Tạm dừng và rollback

- Đặt `Payments__Sepay__Enabled=false` để ngừng tạo payment mới. Nếu các thông số nhận tiền hợp lệ vẫn được giữ trong secret store, server tiếp tục xử lý webhook của payment đã tạo trước đó.
- Không xóa payment hoặc license đã fulfillment vì đây là dữ liệu cần đối soát.
- Chỉ gỡ cấu hình tài khoản nhận sau khi đã đối soát hết giao dịch đang chờ.
- Khi payment bị tắt, overlay tiếp tục chặn nghiệp vụ và hiển thị thông báo tạm ngừng từ server.
