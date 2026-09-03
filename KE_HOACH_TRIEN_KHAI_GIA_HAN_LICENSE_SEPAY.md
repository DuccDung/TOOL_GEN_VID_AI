# Kế hoạch triển khai gia hạn license bằng SePay

## 1. Trạng thái tài liệu

- Ngày lập kế hoạch: 2026-09-03.
- Trạng thái source: các task contract, migration, server, desktop và React/WebView của mốc A/B đã được triển khai; SePay vẫn mặc định tắt.
- Trạng thái nghiệm thu: chưa chạy migration SQL Server staging, chưa cấu hình tài khoản nhận, chưa gọi SePay thật và chưa hoàn tất kiểm thử UI thủ công hoặc vận hành.
- Mục tiêu phiên bản đầu: người dùng hết hạn vẫn đăng nhập và mở được VideoMaker, nhưng toàn bộ thao tác nghiệp vụ bị khóa bởi overlay; người dùng có thể chọn gói, chuyển khoản bằng QR SePay và được tự động mở khóa sau khi server xác nhận tiền vào.
- Dự án tham chiếu nghiệp vụ: `D:\laptrinhweb\code_outsrc\hotel-booking\limit_key_laucher\launcher`.

## 2. Mục tiêu và tiêu chí thành công

Phiên bản đầu được xem là hoàn thành khi đáp ứng đủ các điều kiện sau:

1. Tài khoản chưa có hoặc đã hết hạn license vẫn duy trì được phiên đăng nhập.
2. Desktop mở cửa sổ chính thay vì hiện MessageBox rồi thoát.
3. UI hiển thị overlay không thể đóng, che toàn bộ nội dung và chặn thao tác phía sau.
4. Người dùng xem được danh sách gói và giá do server cung cấp.
5. Người dùng chọn gói và nhận được QR chứa đúng tài khoản, số tiền và nội dung chuyển khoản.
6. SePay webhook xác nhận đúng giao dịch và server cấp license đúng một lần.
7. Desktop polling trạng thái, kích hoạt thiết bị hiện tại rồi tự động gỡ overlay.
8. Người dùng chưa có license không thể vượt qua overlay bằng WebView message hoặc gọi API nghiệp vụ trực tiếp.
9. Người dùng đang có license vẫn sử dụng ứng dụng như hiện tại.
10. Tài khoản nhận tiền và cấu hình SePay chỉ tồn tại phía server.

## 3. Phạm vi phiên bản đầu

### 3.1. Trong phạm vi

- Trạng thái license `Active`, `Locked`, `PaymentPending`, `Activating`.
- Các trường hợp khóa: chưa có license và license hết hạn.
- Danh sách gói công khai lấy từ server.
- Thanh toán chuyển khoản ngân hàng bằng QR động.
- Một giao dịch tương ứng với một gói license.
- Tạo lại hoặc tái sử dụng giao dịch pending chưa hết hạn.
- Polling trạng thái mỗi 5 giây và nút kiểm tra thủ công.
- Webhook SePay loại giao dịch có tiền vào.
- Chống webhook trùng và cấp license lặp.
- Cấp hoặc gia hạn license mà không thu hồi phiên đăng nhập hiện tại.
- Migration SQL idempotent mới.
- Unit test, integration test và kiểm tra thủ công end-to-end.

### 3.2. Ngoài phạm vi

- Giỏ hàng, nhiều sản phẩm trong một đơn, voucher và ví nội bộ.
- Thanh toán thẻ hoặc cổng thanh toán hosted checkout.
- SignalR/realtime trong phiên bản đầu.
- Hoàn tiền tự động.
- Đổi gói giữa kỳ, tính chênh lệch hoặc prorate.
- Hóa đơn điện tử.
- Tự động tạo/cập nhật webhook bằng SePay API.
- Cho desktop truy cập trực tiếp bảng thanh toán.

## 4. Quyết định kiến trúc

### 4.1. Tách xác thực khỏi quyền sử dụng app

- JWT, refresh token, session và device claim xác định người dùng đã đăng nhập hợp lệ.
- License xác định người dùng có được sử dụng nghiệp vụ VideoMaker hay không.
- Hết license không được tự động thu hồi session.
- Các API AI và API phát sinh chi phí vẫn bắt buộc kiểm tra license lease, organization membership, role và quyền project như hiện tại.
- Các API license offer/payment/status được phép gọi khi session hợp lệ nhưng license không còn hiệu lực.

### 4.2. Server là nguồn sự thật

- Desktop chỉ gửi `planId`, không gửi giá, thời hạn hoặc quyền lợi để server tin cậy.
- Server lấy dữ liệu gói trong database rồi snapshot vào giao dịch.
- Desktop không gọi SePay trực tiếp.
- Chỉ webhook khớp giao dịch tiền vào, tài khoản nhận, transfer code và số tiền mới được thay đổi trạng thái thanh toán và cấp license.

### 4.3. Polling trước, realtime sau

- Desktop gọi status API mỗi 5 giây khi popup QR đang mở.
- Dừng polling khi đã fulfilled, khi form đóng hoặc khi ứng dụng dừng.
- Mất mạng tạm thời không đóng popup và không tạo giao dịch mới tự động.
- SignalR chỉ được xem xét ở giai đoạn sau nếu polling tạo tải đáng kể.

### 4.4. Overlay không phải lớp bảo mật duy nhất

Việc khóa thao tác phải có ba lớp:

1. React overlay chặn chuột, bàn phím và focus.
2. C# WebView bridge chỉ cho phép một allowlist message khi license đang bị khóa.
3. Server tiếp tục từ chối API nghiệp vụ nếu không có license hợp lệ.

## 5. Luồng nghiệp vụ đích

```text
Đăng nhập thành công
        |
        v
Lấy trạng thái license
        |
        +-- Active --> Kích hoạt thiết bị/heartbeat --> Tải dashboard đầy đủ
        |
        +-- Missing/Expired --> Mở app ở trạng thái Locked
                                  |
                                  v
                           Tải danh sách gói
                                  |
                                  v
                           Người dùng chọn gói
                                  |
                                  v
                    Server tạo/tái sử dụng payment
                                  |
                                  v
                   Hiển thị QR + polling mỗi 5 giây
                                  |
                    SePay gửi webhook có tiền vào
                                  |
                                  v
                 Server đối soát và cấp license một lần
                                  |
                                  v
                   Desktop thấy trạng thái Fulfilled
                                  |
                                  v
                     Kích hoạt thiết bị hiện tại
                                  |
                                  v
                      Gỡ overlay, tải dashboard
```

## 6. Mô hình trạng thái

### 6.1. Trạng thái truy cập desktop

| Trạng thái | Ý nghĩa | Hành vi UI |
|---|---|---|
| `Active` | License và lease hợp lệ | Dùng app bình thường |
| `Locked` | Chưa có hoặc đã hết hạn | Hiện danh sách gói, chặn app |
| `PaymentPending` | Đã tạo giao dịch | Hiện QR và polling |
| `Activating` | Server đã cấp license | Khóa nút, làm mới license và kích hoạt device |

Các trạng thái `Suspended`, `Revoked`, `DeviceLimit` không tự động bán gói trong phiên bản đầu. UI hiển thị lý do và hướng dẫn liên hệ quản trị viên, vì thanh toán thêm không chắc giải quyết được các trạng thái này.

### 6.2. Trạng thái thanh toán

| Trạng thái | Ý nghĩa |
|---|---|
| `Pending` | Chờ chuyển khoản |
| `Paid` | Đã xác nhận tiền vào, đang chờ hoàn tất cấp quyền |
| `Fulfilled` | Đã cấp/gia hạn license thành công |
| `Expired` | Hết thời gian hiển thị QR nhưng chưa nhận tiền |
| `Failed` | Có lỗi cần xử lý hoặc đối soát |

Không mở khóa desktop ở trạng thái `Paid`; chỉ mở khóa khi `Fulfilled` và desktop kích hoạt thiết bị thành công.

## 7. Thiết kế dữ liệu dự kiến

### TASK DB-01 — Tạo migration mới

- [x] Tạo migration idempotent mới, dự kiến `database/VideoFactory.4.0.10.LicenseSepayPayments.sql`.
- [x] Không sửa âm thầm migration lịch sử đã có.
- [x] Dùng `XACT_ABORT ON`, transaction và kiểm tra tồn tại trước khi tạo column/table/index/constraint.
- [x] Không tự chạy migration trên database thật.

### TASK DB-02 — Bổ sung thông tin bán cho `auth.LicensePlans`

Các cột dự kiến:

| Cột | Kiểu dự kiến | Ghi chú |
|---|---|---|
| `SalePriceVnd` | `decimal(19,0) NULL` | `NULL` nếu gói không bán trực tiếp |
| `IsPublic` | `bit NOT NULL` | Mặc định `0` để fail closed |
| `DisplayOrder` | `int NOT NULL` | Thứ tự card |
| `MarketingFeaturesJson` | `nvarchar(max) NULL` | Danh sách quyền lợi hiển thị, tách khỏi entitlement nội bộ |

- [x] Thêm check constraint giá không âm.
- [x] Kiểm tra JSON hợp lệ khi `MarketingFeaturesJson` có dữ liệu.
- [x] Chỉ trả gói `IsActive = 1`, `IsPublic = 1`, có giá dương và có thời hạn hợp lệ.
- [x] Không hard-code giá trong desktop.

### TASK DB-03 — Tạo `auth.LicensePayments`

Các trường tối thiểu dự kiến:

- `LicensePaymentId`
- `UserId`
- `LicensePlanId`
- `OrderCode`
- `TransferCode`
- `IdempotencyKey`
- `PriceSnapshotVnd`
- `DurationSnapshotDays`
- `PlanCodeSnapshot`
- `PlanNameSnapshot`
- `EntitlementSnapshotJson`
- `Status`
- `ReceiverBankCodeSnapshot`
- `ReceiverAccountNumberSnapshot`
- `ProviderTransactionId`
- `ProviderReferenceCode`
- `FulfilledUserLicenseId`
- `CreatedAtUtc`
- `ExpiresAtUtc`
- `PaidAtUtc`
- `FulfilledAtUtc`
- `FailureCode`
- `RowVersion`

Ràng buộc/index bắt buộc:

- [x] `OrderCode` unique.
- [x] `TransferCode` unique.
- [x] `ProviderTransactionId` unique khi khác `NULL`.
- [x] `(UserId, IdempotencyKey)` unique.
- [x] Check constraint số tiền dương.
- [x] Check constraint trạng thái hợp lệ.
- [x] FK tới user, license plan và user license đã cấp.
- [x] Index phục vụ tìm payment pending theo user/plan/thời hạn.
- [x] Không cascade delete dữ liệu thanh toán.
- [x] Không cấp quyền đọc bảng này cho SQL role của desktop.

### TASK DB-04 — Mapping server

- [x] Thêm domain entity và EF mapping ở `TOOL-SERVER`.
- [x] Cấu hình precision, max length, unique filtered index và row version thống nhất với migration.
- [x] Không thêm payment entity vào `TOOL-LOCAL` database context.

## 8. Shared contracts

### TASK CONTRACT-01 — Mở rộng trạng thái license

- [x] Giữ tương thích với các trường hiện có của `CurrentLicenseResponse`.
- [x] Bổ sung trạng thái truy cập rõ ràng, dự kiến: `Active`, `Missing`, `Expired`, `Suspended`, `Revoked`, `DeviceLimit`.
- [x] Bổ sung mã lý do và thông báo an toàn để desktop quyết định UI.
- [x] Khi hết hạn, server có thể trả tên gói và ngày hết hạn gần nhất thay vì xóa toàn bộ thông tin về gói.
- [x] Cập nhật đồng thời server, desktop và test theo quy tắc thay DTO công khai.

### TASK CONTRACT-02 — Hợp đồng gói và thanh toán

Tạo các contract dự kiến:

- [x] `LicenseOfferResponse`
- [x] `CreateLicensePaymentRequest`
- [x] `LicensePaymentCheckoutResponse`
- [x] `LicensePaymentStatusResponse`

Checkout response chỉ trả dữ liệu cần hiển thị:

- Order code và transfer code.
- Tên gói, thời hạn snapshot và số tiền snapshot.
- Ngân hàng, số tài khoản, tên chủ tài khoản.
- Nội dung chuyển khoản.
- URL QR.
- Thời điểm tạo/hết hạn.
- Trạng thái thanh toán.
- Cờ `ReusedExistingPayment`.

Webhook payload là contract nội bộ của server, không cần đưa vào shared contracts nếu desktop không sử dụng.

## 9. Server: xác thực và license

### TASK AUTH-01 — Cho phép session tồn tại khi license hết hạn

- [x] Sửa refresh token để chỉ kiểm tra user, session, device và token hợp lệ.
- [x] Không thu hồi session chỉ vì license hết hạn hoặc chưa được cấp.
- [x] Giữ nguyên việc thu hồi session khi user/device/session thực sự bị revoke.
- [x] Viết test chứng minh expired user vẫn refresh token được.
- [x] Viết test chứng minh device revoked vẫn không refresh được.

### TASK LICENSE-01 — Trả trạng thái inactive thay vì lỗi khởi động

- [x] API lấy license hiện tại trả `200` với trạng thái `Missing` hoặc `Expired`.
- [x] Chỉ endpoint activate/heartbeat yêu cầu license active.
- [x] Giữ server time trong response để desktop không phụ thuộc đồng hồ máy người dùng.
- [x] Không tự kích hoạt thiết bị khi chưa có license.

### TASK LICENSE-02 — Tạo nghiệp vụ cấp license từ thanh toán

- [x] Tạo service riêng cho payment fulfillment; không gọi nguyên trạng luồng admin grant đang thu hồi session.
- [x] Snapshot entitlement từ payment sang `UserLicense`.
- [x] Với người chưa có hoặc đã hết hạn: bắt đầu license từ thời điểm fulfillment.
- [x] Nếu người dùng được admin cấp license trong lúc đang chuyển khoản: tính mốc bắt đầu từ `max(now, expiresAt hiện tại)` để không làm mất ngày sử dụng.
- [x] Không cấp hai lần khi cùng webhook hoặc cùng payment được xử lý đồng thời.
- [x] Không thu hồi session sau khi cấp/gia hạn.
- [x] Ghi audit không chứa secret hoặc raw webhook.

## 10. Server: tích hợp SePay

### TASK SEPAY-01 — Cấu hình phía server

Tạo options dự kiến dưới namespace cấu hình `Payments:Sepay`:

- `Enabled`
- `QrBaseUrl`
- `ReceiverBankCode`
- `ReceiverAccountNumber`
- `ReceiverAccountName`
- `TransferCodePrefix`
- `PaymentExpireMinutes`, mặc định 15 phút

Yêu cầu:

- [x] Không commit giá trị production vào source/appsettings.
- [x] Tài khoản nhận lấy từ environment variable, user-secrets hoặc secret store của môi trường triển khai.
- [x] Khi `Enabled = true`, thiếu tài khoản nhận phải làm payment fail closed.
- [x] Desktop không nhận cấu hình nội bộ của webhook.
- [x] Phiên bản đầu không cần SePay API token vì không chủ động truy vấn giao dịch hoặc tài khoản ngân hàng.

### TASK SEPAY-02 — API danh sách gói

- [x] `GET /api/license/offers` yêu cầu JWT/session/device hợp lệ nhưng không yêu cầu active license.
- [x] Chỉ trả gói công khai và đang hoạt động.
- [x] Sắp xếp theo `DisplayOrder`.
- [x] Không trả entitlement nội bộ không dùng cho UI.
- [x] Trả trạng thái thanh toán tạm ngừng nếu cấu hình SePay chưa sẵn sàng.

### TASK SEPAY-03 — API tạo/tái sử dụng payment

- [x] `POST /api/license/payments` nhận `planId` và `idempotencyKey`.
- [x] Không nhận giá từ client.
- [x] Từ chối plan không public, không active, không có giá hoặc thời hạn không hợp lệ.
- [x] Tái sử dụng payment pending cùng user/plan còn hạn.
- [x] Sinh `OrderCode` và `TransferCode` ngẫu nhiên, không đoán được và unique.
- [x] Nội dung chuyển khoản chỉ dùng chữ/số không dấu và có prefix riêng của VideoMaker.
- [x] Snapshot gói, số tiền và tài khoản nhận vào payment.
- [x] Tạo URL QR ở server với số tiền/nội dung đã URL encode.
- [x] Không log toàn bộ QR URL nếu nó chứa thông tin tài khoản nhận.

### TASK SEPAY-04 — API lấy trạng thái payment

- [x] `GET /api/license/payments/{orderCode}/status` yêu cầu JWT/session/device nhưng không yêu cầu active license.
- [x] Chỉ chủ sở hữu payment mới xem được.
- [x] Không trả dữ liệu ngân hàng không cần thiết trong status response.
- [x] Tự biểu diễn payment pending quá hạn thành `Expired`.
- [x] Nếu có tiền đến sau thời gian QR nhưng đúng mã và số tiền, webhook vẫn được phép fulfillment vì tiền đã thực nhận.

### TASK SEPAY-05 — Webhook SePay

- [x] `POST /api/payments/sepay/webhook` là endpoint public HTTPS và không yêu cầu API key theo phạm vi MVP.
- [x] Không đọc hoặc yêu cầu header `Authorization`.
- [x] Giới hạn request body/rate; không ghi raw payload vào log.
- [x] Giới hạn kích thước request body và kiểm tra JSON hợp lệ.
- [x] Chỉ nhận giao dịch `transferType = in`.
- [x] Kiểm tra đúng tài khoản nhận đã snapshot/cấu hình.
- [x] Ưu tiên match chính xác trường `code`; fallback chỉ được phép tìm unique `TransferCode` trong nội dung đã normalize.
- [x] Bắt buộc số tiền bằng chính xác `PriceSnapshotVnd`.
- [x] Không match chỉ dựa trên số tiền.
- [x] Xử lý trong transaction isolation `Serializable`.
- [x] Chống trùng bằng unique `ProviderTransactionId`.
- [x] Webhook trùng trả thành công nhưng không cấp lại license.
- [x] Giao dịch không liên quan được bỏ qua an toàn; cấu hình SePay nên lọc theo prefix để giảm webhook thừa.
- [x] Không lưu raw Authorization header hoặc toàn bộ payload thô.
- [x] Trả HTTP/body đúng định dạng SePay yêu cầu trong thời gian dưới 30 giây.

## 11. Desktop C#

### TASK DESKTOP-01 — State machine cho `LicenseSessionManager`

- [x] `InitializeAsync` không throw khi trạng thái là `Missing` hoặc `Expired`.
- [x] Lưu `Current` cho cả trạng thái active và inactive.
- [x] Chỉ activate device và bắt đầu heartbeat khi license active.
- [x] Bổ sung phương thức refresh license sau thanh toán.
- [x] Hỗ trợ chuyển `Locked -> Activating -> Active` trong cùng phiên chạy.
- [x] Khi heartbeat phát hiện hết hạn, phát sự kiện chuyển sang `Locked` thay vì đóng app.
- [x] Các lỗi session thực sự hết hạn vẫn quay về màn hình đăng nhập.

### TASK DESKTOP-02 — Sửa luồng khởi động

- [x] `Program.cs` không hiện MessageBox rồi `return` khi chỉ thiếu/hết license.
- [x] Vẫn khởi tạo form/WebView chính ở trạng thái locked.
- [x] Không tải tổ chức, project, provider hoặc asset server trước khi active.
- [x] Phân biệt lỗi license với lỗi mạng/server để không hiển thị nhầm màn hình mua gói.

### TASK DESKTOP-03 — Payment API client

- [x] Tạo client riêng cho offer/payment/status thay vì đưa vào `ServerGenerationClient`.
- [x] Client luôn dùng access token/session hiện tại nhưng không gọi `EnsureAccessAsync` trước payment API.
- [x] Chuẩn hóa lỗi `401`, `403`, `404`, `409`, `422`, `503` thành mã lỗi UI hiểu được.
- [x] Không ghi account number hoặc response nhạy cảm vào log.
- [x] Có cancellation token cho polling.

### TASK DESKTOP-04 — WebView bridge allowlist khi locked

Các message được phép dự kiến:

- `app.ready`
- `license.refresh`
- `license.offers.get`
- `license.payment.create`
- `license.payment.status`
- `account.logout`
- Các message kiểm tra/cài đặt update cần thiết

Yêu cầu:

- [x] Mọi message project/generation/render/media/provider/organization bị từ chối khi locked.
- [x] Trả lỗi thống nhất `license_required` cho React.
- [x] Không chỉ dựa vào việc nút bị overlay che.
- [x] Khi active trở lại, thực hiện dashboard refresh đầy đủ đúng một lần.

## 12. React/WebView UI

### TASK UI-01 — Overlay khóa app

- [x] Overlay `position: fixed`, phủ toàn bộ viewport và có z-index cao nhất trong vùng app.
- [x] Không có nút đóng và không đóng bằng `Escape` hoặc click ra ngoài.
- [x] Dùng `role="dialog"`, `aria-modal="true"` và giữ focus bên trong overlay.
- [x] Chặn cuộn và pointer event của nội dung phía sau.
- [x] Cho phép đăng xuất và xem trạng thái dịch vụ thanh toán.
- [x] Hiển thị đúng lý do: chưa có gói hoặc gói đã hết hạn.

### TASK UI-02 — Danh sách gói

- [x] Hiển thị card từ dữ liệu server: tên, mô tả, giá/tháng hoặc thời hạn, quyền lợi và badge nếu có.
- [x] Không render HTML tùy ý từ server; marketing features chỉ là text.
- [x] Format giá VND ở client nhưng giữ giá trị số từ server.
- [x] Nút chọn gói có trạng thái loading và chống click lặp.
- [x] Không cho mua plan không còn public nếu response cũ đang cache.

### TASK UI-03 — Màn hình QR

- [x] Hiển thị QR, ngân hàng, tài khoản, chủ tài khoản, số tiền và nội dung chuyển khoản.
- [x] Có nút sao chép số tài khoản và nội dung.
- [x] Nhấn mạnh phải chuyển đúng số tiền và nội dung.
- [x] Đếm ngược thời hạn QR bằng `ServerTimeUtc`/`ExpiresAtUtc`, không tin hoàn toàn đồng hồ local.
- [x] Polling mỗi 5 giây và nút “Tôi đã thanh toán/Kiểm tra lại”.
- [x] Polling lỗi mạng chỉ hiện trạng thái thử lại, không tự hủy payment.
- [x] Khi hết hạn, dừng polling và cho tạo mã mới.
- [x] Khi app khởi động lại, nếu còn payment pending thì mở lại QR đó.

### TASK UI-04 — Mở khóa sau thanh toán

- [x] Khi status là `Fulfilled`, chuyển UI sang `Activating`.
- [x] Gọi refresh license, sau đó activate current device nếu cần.
- [x] Chỉ gỡ overlay sau khi có license lease hợp lệ.
- [x] Nếu cấp license thành công nhưng activate device lỗi do giới hạn thiết bị, giữ overlay và hiển thị hướng dẫn rõ ràng; không tạo payment mới.
- [x] Sau khi mở khóa, tải organization/project/provider một lần và đưa người dùng vào dashboard.

## 13. Kiểm thử

### TASK TEST-01 — Test auth/license server

- [x] User hết license vẫn refresh token thành công.
- [x] User/device/session bị revoke vẫn bị từ chối.
- [x] Missing/expired license trả state đúng.
- [x] Activate/heartbeat vẫn từ chối license inactive.
- [x] API AI vẫn từ chối user hết license.

### TASK TEST-02 — Test payment server

- [x] Chỉ trả plan public/active/có giá hợp lệ.
- [x] Client sửa giá không ảnh hưởng giá snapshot.
- [x] Idempotency key lặp không tạo giao dịch mới.
- [x] Pending payment cùng plan được tái sử dụng.
- [x] User khác không đọc được status.
- [x] Webhook không cần API key; chỉ payload khớp đầy đủ mới được fulfillment.
- [x] Sai transfer type, tài khoản, mã hoặc số tiền không cấp license.
- [x] Webhook hợp lệ cấp đúng một license.
- [x] Webhook lặp không cấp lần hai.
- [ ] Hai webhook concurrent trên SQL Server không cấp lần hai.
- [x] Payment đến muộn nhưng hợp lệ vẫn được fulfillment.
- [x] Fulfillment không revoke session hiện tại.
- [x] Lỗi giữa payment update và license grant rollback toàn bộ transaction.

Rollback đã được kiểm tra tự động bằng SQLite relational transaction. Trường hợp hai webhook đồng thời vẫn cần integration test trên SQL Server/staging để xác minh hành vi khóa `Serializable` và unique index trong provider thực tế.

### TASK TEST-03 — Test desktop/bridge

- [x] Missing/expired license không làm app thoát.
- [x] Trạng thái locked không chạy heartbeat.
- [x] Bridge chặn message nghiệp vụ trong trạng thái locked.
- [x] Bridge vẫn cho phép offer/payment/status/logout.
- [x] Chuyển trạng thái sang active không cần khởi động lại app.
- [x] Polling được hủy đúng khi form đóng hoặc payment hoàn tất.

### TASK TEST-04 — Test UI thủ công

- [ ] Không thể click, tab hoặc dùng phím tắt vào app phía sau overlay.
- [ ] Card gói hiển thị tốt ở kích thước cửa sổ tối thiểu.
- [ ] QR quét được và chứa đúng số tiền/nội dung.
- [ ] Copy account/content hoạt động.
- [ ] Offline/online lại không làm mất payment pending.
- [ ] Payment thành công tự mở khóa.
- [ ] Payment hết hạn cho tạo QR mới.
- [ ] Logout từ overlay hoạt động.

### TASK TEST-05 — Kiểm tra migration và solution

- [ ] Chạy migration trên database test có dữ liệu gần production.
- [ ] Chạy migration lần hai để chứng minh idempotency.
- [ ] Kiểm tra row count, FK, unique index, check constraint và quyền SQL.
- [ ] Không chạy migration production nếu chưa xác minh instance, database, backup và restore.
- [x] Sau khi thay source, chạy đầy đủ:

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Mốc source ngày 2026-09-03: restore thành công, Release build 0 warning/error và 510/510 test đạt. Mốc này không thay thế migration rehearsal, kiểm thử concurrent trên SQL Server hoặc nghiệm thu UI/SePay staging.

- [x] Khi kiểm tra riêng UI web:

```powershell
Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
```

## 14. Cấu hình và triển khai vận hành

### TASK OPS-01 — Cấu hình SePay

- [ ] Kết nối đúng tài khoản ngân hàng trong SePay.
- [ ] Tạo webhook sự kiện “Có tiền vào”.
- [ ] Trỏ tới URL HTTPS production của `TOOL-SERVER`.
- [ ] Chọn chế độ không xác thực theo phạm vi MVP.
- [ ] Lọc theo tài khoản nhận và prefix transfer code của VideoMaker.
- [ ] Bật bỏ qua giao dịch không có mã nếu phù hợp cấu hình thực tế.
- [ ] Cấu hình cảnh báo webhook lỗi/retry.
- [ ] Nếu có dải IP chính thức đã được xác minh, dùng allowlist tại reverse proxy như một lớp phụ.

### TASK OPS-02 — Cấu hình môi trường

- [ ] Lưu tài khoản production ngoài source.
- [ ] Không đưa tài khoản thật hoặc connection string vào log/tài liệu bàn giao công khai.
- [ ] Có `Payments:Sepay:Enabled` để tắt tạo payment khi có sự cố.
- [ ] Khi payment bị tắt, overlay hiện thông báo bảo trì và cách liên hệ; không mở app nghiệp vụ.

### TASK OPS-03 — Theo dõi và đối soát

- [x] Log có cấu trúc theo `LicensePaymentId`, `OrderCode` và trạng thái khi đã match payment; webhook unmatched dùng provider transaction ID, không ghi secret/raw payload.
- [x] Theo dõi số payment created, fulfilled, expired, duplicate và unmatched.
- [ ] Cảnh báo payment ở trạng thái `Paid` quá lâu chưa `Fulfilled`.
- [x] Có truy vấn quản trị để tra payment theo order code/provider transaction ID.
- [ ] Chưa cần UI admin thanh toán trong phiên bản đầu; có thể bổ sung ở giai đoạn sau.

Source phát counter `videomaker.license_payment.events` trong meter `VideoMaker.Payments`, chỉ dùng tag `event` và `reason` có tập giá trị cố định. API `GET /api/admin/licenses/payments` yêu cầu role `Admin`, dùng exact match, giới hạn tối đa 200 dòng và không trả các snapshot nhạy cảm. Việc cấu hình exporter/dashboard/cảnh báo production vẫn thuộc hạ tầng vận hành.

## 15. Thứ tự triển khai đề xuất

| Thứ tự | Nhóm task | Phụ thuộc | Kết quả |
|---:|---|---|---|
| 1 | `CONTRACT-01..02` | Không | Chốt DTO và state |
| 2 | `DB-01..04` | Contract đã chốt | Schema và mapping payment |
| 3 | `AUTH-01`, `LICENSE-01` | Contract | Expired user vẫn đăng nhập được |
| 4 | `SEPAY-01..04` | Database, auth | Tạo QR và polling status được |
| 5 | `SEPAY-05`, `LICENSE-02` | Payment service | Webhook cấp license idempotent |
| 6 | `DESKTOP-01..04` | API ổn định | Desktop chạy được trạng thái locked |
| 7 | `UI-01..04` | Bridge/payment client | Overlay và thanh toán hoàn chỉnh |
| 8 | `TEST-01..05` | Toàn bộ source | Xác minh hồi quy và bảo mật |
| 9 | `OPS-01..03` | Staging đã đạt | Sẵn sàng triển khai có kiểm soát |

## 16. Các mốc bàn giao

### Mốc A — App mở được khi hết hạn

- Auth refresh không phụ thuộc license.
- Desktop không thoát khi license missing/expired.
- Overlay khóa app và bridge guard hoạt động.
- Chưa cần thanh toán thật.

### Mốc B — Thanh toán sandbox/staging

- Có plan offer, payment record, QR và polling.
- Webhook không API key và fulfillment idempotent theo tài khoản/mã/số tiền.
- Dùng tài khoản/cấu hình staging do người vận hành cung cấp.

### Mốc C — Hoàn thiện production

- Đầy đủ test và migration rehearsal.
- Tài khoản production đã cấu hình ngoài source.
- Webhook production được kiểm tra có kiểm soát.
- Có giám sát, đối soát và phương án tắt payment.

## 17. Phương án rollback

- Tắt `Payments:Sepay:Enabled` để ngừng tạo giao dịch mới.
- Giữ nguyên dữ liệu payment đã có để đối soát; không xóa hoặc rollback dữ liệu tiền đã nhận.
- Overlay chuyển sang thông báo bảo trì/thông tin hỗ trợ.
- Không nới lỏng server license guard khi payment bị tắt.
- Nếu UI desktop có lỗi, có thể phát hành bản vá chỉ tắt nút mua; webhook vẫn phải tiếp tục xử lý giao dịch đã tạo trước đó.
- Migration chỉ bổ sung bảng/cột; rollback logic không được xóa dữ liệu thanh toán production.

## 18. Checklist nghiệm thu cuối

- [ ] Expired user mở được app mà không gặp MessageBox “License chưa sẵn sàng”.
- [ ] Session không bị revoke chỉ vì license hết hạn.
- [ ] Overlay không thể đóng hoặc xuyên tương tác.
- [ ] Direct WebView message bị bridge từ chối khi locked.
- [ ] Direct AI API request bị server từ chối khi license inactive.
- [ ] Giá và thời hạn luôn lấy từ server và được snapshot.
- [ ] QR đúng ngân hàng, số tiền và transfer code.
- [ ] Webhook sai tài khoản/sai tiền/sai mã không cấp license.
- [ ] Webhook hợp lệ/lặp/concurrent chỉ cấp một lần.
- [ ] Desktop tự mở khóa mà không cần đăng nhập hoặc khởi động lại.
- [ ] Active user không bị ảnh hưởng luồng hiện tại.
- [ ] Không có secret hoặc raw payload nhạy cảm trong source/log.
- [ ] Release build và toàn bộ test đạt trước khi phát hành.
