# Kiểm thử SePay và tự động phân bổ tổ chức không dùng giao dịch thật

**Cập nhật:** 2026-09-04  
**Phạm vi:** payment license, webhook SePay mô phỏng, seat, membership, desktop và đối soát quản trị

## 1. Mục tiêu và giới hạn

Bộ kiểm thử này xác minh luồng nội bộ:

```text
Tạo payment
    -> giữ seat
    -> nhận webhook mô phỏng
    -> cấp/gia hạn license
    -> kích hoạt assignment
    -> tạo hoặc kích hoạt membership
    -> desktop đọc license và tổ chức vừa được cấp
```

Không chuyển tiền, không gọi API thanh toán SePay và không thêm endpoint giả lập vào server. Webhook mô phỏng được gửi vào đúng endpoint đang dùng cho SePay là `POST /api/payments/sepay/webhook`.

Bộ này không thể chứng minh các phần sau:

- ngân hàng giữ nguyên nội dung chuyển khoản;
- QR thực tế mở đúng thông tin trong ứng dụng ngân hàng;
- SePay gửi đúng payload và gọi được URL public;
- reverse proxy/firewall production cho phép đúng nguồn SePay;
- số tiền trong sao kê ngân hàng khớp dữ liệu đối soát.

Các mục trên phải tiếp tục để trạng thái chưa nghiệm thu cho tới khi có smoke test SePay thật được phê duyệt.

## 2. Thành phần đã bổ sung

- `scripts/Test-SepayOrganizationProvisioning.ps1`: runner black-box tạo payment, gửi webhook sai, gửi webhook hợp lệ đồng thời, replay và đối soát API Admin.
- `database/Verify.VideoFactory.4.0.11.OrganizationSeatProvisioning.sql`: kiểm tra chỉ đọc cho migration, orphan, counter, unique index và quyền deny của desktop.
- xUnit regression test cho replay bằng provider transaction ID mới, tiếp tục xử lý webhook cũ khi tắt tạo payment, retry sau khi khôi phục capacity/mapping và contract an toàn của runner.

Runner không tự tạo gói, pool, tổ chức, user hoặc credential. Những dữ liệu này phải được chuẩn bị có kiểm soát bằng Admin Console.

## 3. Cảnh báo an toàn

Runner sẽ ghi dữ liệu thật vào database của server đích và sẽ cấp license cho user test. Chỉ chạy trên database local/staging cô lập.

- Không chạy với user production.
- Không chạy vào URL production.
- Snapshot/backup database test trước khi chạy nếu cần giữ dữ liệu.
- Không truyền JWT trực tiếp trên command line; runner chỉ đọc JWT từ biến môi trường của process.
- Endpoint webhook hiện không có chữ ký/API key. Staging remote phải được giới hạn truy cập trước khi bật payment.
- Runner chặn host remote mặc định. Remote chỉ được phép khi có `-AllowRemote`, dùng HTTPS và vẫn phải truyền xác nhận `SEPAY_TEST_ONLY`.

## 4. Dữ liệu bắt buộc

Chuẩn bị tối thiểu:

1. Một user test có session/device hợp lệ, trạng thái license `Missing` hoặc `Expired`, không có payment `Pending`/`Paid`.
2. Một Global Admin test.
3. Một license plan Active, Public, có giá VND nguyên dương và thời hạn hợp lệ.
4. License plan được ánh xạ Active vào một organization pool Active.
5. Pool có ít nhất một tổ chức Active, `IsAutoAssignmentEnabled=true`, `IsReady=true` và còn seat.
6. Tổ chức có budget lớn hơn `0` cùng credential, policy và rate đủ để vượt readiness. Không cần gọi provider AI trong bài test payment.
7. `Payments:Sepay` dùng cấu hình test hợp lệ và `Enabled=true`.

Nên dùng pool riêng gồm hai tổ chức:

| Tổ chức | Capacity | Priority | Mục đích |
|---|---:|---:|---|
| `QA-ORG-A` | 1 | 10 | Kiểm tra ưu tiên và đầy chỗ |
| `QA-ORG-B` | 2 | 20 | Kiểm tra chuyển sang tổ chức tiếp theo |

User chạy runner không được có membership thủ công trong hai tổ chức này; nếu có, runner sẽ dừng ở bước xác minh `membershipManaged`.

## 5. Xác minh database sau migration

Lệnh dưới đây chỉ đọc dữ liệu nhưng phải thay đúng server/database test:

```powershell
sqlcmd -S <SQL_TEST_INSTANCE> -d <SQL_TEST_DATABASE> -E -b -f 65001 -i database\Verify.VideoFactory.4.0.11.OrganizationSeatProvisioning.sql
```

Kết quả đạt khi:

- lệnh trả exit code `0`;
- có schema version `4.0.10-license-sepay-payments` và `4.0.11-organization-seat-provisioning`;
- counter Active/Reserved khớp assignment đang chiếm seat;
- không có orphan;
- unique index và quyền deny cho `VideoMakerDesktopRole` tồn tại;
- result set payment `Paid` không có bản ghi tồn đọng ngoài dự kiến.

Nếu script phát hiện counter lệch, không sửa trực tiếp counter. Khởi động server test, chờ worker reconciliation chạy rồi kiểm tra lại; nếu vẫn lệch thì dừng nghiệm thu và điều tra.

## 6. Chạy mô phỏng end-to-end

Đặt token trong biến môi trường của đúng PowerShell process:

```powershell
$env:VIDEOMAKER_TEST_USER_TOKEN = '<JWT_CUA_USER_TEST>'
$env:VIDEOMAKER_TEST_ADMIN_TOKEN = '<JWT_CUA_GLOBAL_ADMIN_TEST>'
```

Chạy local:

```powershell
.\scripts\Test-SepayOrganizationProvisioning.ps1 `
    -BaseUrl 'https://localhost:7202/' `
    -LicensePlanId '<LICENSE_PLAN_ID>' `
    -Confirmation SEPAY_TEST_ONLY
```

Chạy staging remote chỉ sau khi đã xác minh đúng môi trường:

```powershell
.\scripts\Test-SepayOrganizationProvisioning.ps1 `
    -BaseUrl 'https://<STAGING_HOST>/' `
    -LicensePlanId '<LICENSE_PLAN_ID>' `
    -Confirmation SEPAY_TEST_ONLY `
    -AllowRemote
```

Không ghi token vào script, `appsettings.json`, ticket hoặc ảnh chụp kết quả. Xóa hai biến môi trường sau phiên test:

```powershell
Remove-Item Env:VIDEOMAKER_TEST_USER_TOKEN -ErrorAction SilentlyContinue
Remove-Item Env:VIDEOMAKER_TEST_ADMIN_TOKEN -ErrorAction SilentlyContinue
```

Runner thực hiện theo thứ tự:

1. Kiểm tra user chưa có active license và chưa có open payment.
2. Kiểm tra gói public còn organization seat.
3. Tạo payment mới và yêu cầu assignment `Reserved`.
4. Gửi webhook sai chiều, sai tài khoản, sai mã và sai số tiền; payment phải tiếp tục `Pending`.
5. Gửi nhiều webhook hợp lệ đồng thời với cùng provider transaction ID.
6. Yêu cầu payment `Fulfilled`, assignment `Active` và current license có đúng organization.
7. Replay cùng transaction ID và replay transfer code với transaction ID mới; ngày hết hạn license không được thay đổi.
8. Dùng API Global Admin xác nhận chỉ có một payment, một assignment và response không lộ snapshot nhạy cảm.

Kết quả thành công trả một object chỉ chứa mã đối soát và ID kỹ thuật, không chứa JWT hoặc tài khoản nhận.

## 7. Ma trận trạng thái cần quan sát

| Thời điểm | Payment | Assignment | Membership | Counter |
|---|---|---|---|---|
| Sau tạo checkout | `Pending` | `Reserved` | Chưa tạo mới | Reserved tăng 1 |
| Webhook sai | `Pending` | `Reserved` | Không đổi | Không đổi |
| Webhook đúng, license hiệu lực ngay | `Fulfilled` | `Active` | `Member/Active`, tự động quản lý | Reserved giảm 1, Active tăng 1 |
| Payment hết hạn | `Expired` | `Released` | Không tạo mới | Reserved giảm 1 |
| Tiền đến muộn nhưng hết chỗ | `Paid` | `Released` hoặc chưa giữ lại được | Không cấp mới | Không vượt capacity |
| Retry sau khi có chỗ | `Fulfilled` | `Active` hoặc `Scheduled` | Cấp đúng một lần | Khớp assignment |
| License kết thúc/revoke | Payment không đổi | `Released` | Chỉ membership tự động bị `Suspended` | Active giảm 1 |

## 8. Các case staging thủ công còn lại

Runner chính chỉ dùng một user sạch. Các case sau phải chạy riêng để tránh làm kết quả concurrent khó đối soát.

### `STAGE-01` — Hết chỗ và retry

1. Tạo payment rồi để hết hạn và giải phóng reservation.
2. Lấp đầy pool bằng user test khác.
3. Gửi webhook hợp lệ của payment đã hết hạn.
4. Xác nhận payment là `Paid`, `FailureCode=organization_capacity_unavailable`, chưa có license mới.
5. Giải phóng một seat và dùng nút Retry hoặc chờ worker tối đa một chu kỳ.
6. Xác nhận chỉ một license, assignment và membership được cấp.

### `STAGE-02` — Mất mapping và retry

1. Chỉ thực hiện sau khi payment đã hết hạn và assignment đã `Released`.
2. Gỡ mapping plan/pool bằng Admin Console.
3. Gửi webhook hợp lệ và xác nhận `Paid`, `FailureCode=license_plan_pool_not_configured`.
4. Khôi phục mapping, Retry và xác nhận `Fulfilled`.

### `STAGE-03` — Gia hạn cùng gói

1. Dùng user đang có license/assignment Active.
2. Tạo và fulfillment payment cùng gói.
3. Xác nhận expiry được cộng đúng một lần, assignment mới không chiếm seat thứ hai và ActiveSeatCount không tăng.

### `STAGE-04` — Đổi gói/pool

1. Dùng user đang có license Active ở pool A.
2. Mua gói ánh xạ pool B.
3. Xác nhận payment `Fulfilled`, assignment pool B `Scheduled` tới khi license hiện tại kết thúc.
4. Sau reconciliation, membership tự động ở A bị suspend và membership B được Active; project cũ không bị xóa.

### `STAGE-05` — Membership thủ công

- Manual Member/Admin phải được giữ nguyên role, status và budget.
- Viewer không được tự nâng lên Member; hệ thống phải chọn tổ chức khác còn chỗ.
- Sau khi Admin takeover membership tự động, `IsProvisioningManaged=false` và worker không được sửa membership đó khi license kết thúc.

### `STAGE-06` — Desktop

- Overlay không thể đóng, tab xuyên hoặc gọi message nghiệp vụ khi locked.
- Checkout hiển thị đúng số tiền, transfer code và organization đã giữ.
- Restart/offline-online khôi phục đúng payment pending.
- Trong lúc runner gửi webhook, desktop polling chuyển sang activating rồi mở khóa mà không đăng nhập lại.
- Nếu chạm device limit, license vẫn đã fulfillment nhưng overlay phải giữ và hướng dẫn xử lý thiết bị; không tạo payment mới.

## 9. Tiêu chí hoàn tất

Chỉ đánh dấu kiểm thử mô phỏng đạt khi:

- toàn bộ xUnit đạt;
- verification SQL trả exit code `0` trên database test đã migrate;
- runner black-box đạt trên SQL Server test với ít nhất 8 webhook đồng thời;
- các case `STAGE-01` đến `STAGE-06` có evidence đã che dữ liệu nhạy cảm;
- không có payment `Paid` tồn đọng ngoài case đang chủ động kiểm tra;
- không có capacity âm/vượt giới hạn hoặc counter lệch;
- không có license, assignment hay membership bị cấp trùng;
- chưa ghi nhận “SePay production đạt” nếu chưa có giao dịch thật.

