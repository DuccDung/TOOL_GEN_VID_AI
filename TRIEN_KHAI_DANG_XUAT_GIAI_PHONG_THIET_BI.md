# Đăng xuất giải phóng suất thiết bị

Ngày thay đổi: 2026-09-18. Checkout: nhánh `main`, HEAD
`0a85b569914a9a230425442e9f0b3e1c16072cf1` cùng thay đổi chưa commit trong workspace.

## Vấn đề và hành vi mới

Trước đây, `/api/auth/logout` thu hồi phiên và refresh token nhưng giữ
`auth.LicenseActivations.Status = Active`. Gói một thiết bị vẫn bị chiếm suất
sau khi người dùng đăng xuất, khiến máy khác nhận `device_limit_reached`.

Server mới thực hiện trong cùng transaction:

- Đăng xuất thông thường thu hồi phiên/token và giải phóng kích hoạt license
  của thiết bị tương ứng khi thiết bị không còn phiên Active chưa hết hạn.
- Đăng xuất tất cả thu hồi toàn bộ phiên/token của tài khoản và giải phóng
  toàn bộ kích hoạt còn Active của tài khoản, kể cả suất bị giữ từ lần đăng
  xuất trên bản server cũ.
- Yêu cầu đăng xuất trễ từ phiên cũ không giải phóng suất mà phiên mới trên
  cùng thiết bị đang sử dụng.
- Không tác động phiên hoặc kích hoạt thuộc tài khoản khác. Nếu ghi audit
  thất bại, toàn bộ thao tác rollback.
- Khi máy mới xin kích hoạt, server thu hồi các suất Active của license
  hiện tại mà thiết bị không còn phiên Active chưa hết hạn. Thao tác chạy
  sau kiểm tra user/device/session/license, trong transaction Serializable,
  trước khi đếm giới hạn thiết bị; ghi audit `DeviceActivationsReleased`.
  Phiên vẫn hợp lệ giữ suất dù đã lâu không gửi heartbeat. Không dọn hàng
  loạt database hoặc mở quyền SQL cho desktop.

Desktop làm mới access token sắp hết hạn trước khi gửi logout và gửi đúng
refresh token mới sau rotation. Lỗi mạng/server không làm mất token cần
cho lần thử lại. `401` vẫn làm xóa phiên cục bộ; vì vậy riêng ảnh màn hình
đăng nhập không phải bằng chứng server đã nhận logout hay đã trả suất.

Giải phóng nghĩa là chuyển kích hoạt sang `Revoked`, ghi thời điểm và lý do.
Giữ bản ghi thiết bị, lịch sử phiên và kích hoạt để bảo toàn tham chiếu/audit;
không đặt `RegisteredDevices.IsRevoked` khi đăng xuất. Do đó máy cũ có thể
đăng nhập và kích hoạt lại khi còn suất. Thiết bị đã bị quản trị viên thu hồi
vẫn bị chặn như trước.

## Triển khai

1. Build/publish và deploy lại **TOOL-SERVER** bằng quy trình hiện có của môi
   trường đích, giữ cấu hình và secret của môi trường đó.
2. Khởi động lại ứng dụng server theo quy trình vận hành. Publish và cập nhật
   **TOOL-LOCAL** để có sửa lỗi token hết hạn khi đăng xuất. Gói đã publish
   trước lần bổ sung này không chứa hai thay đổi mới.
3. Với suất bị giữ từ trước khi deploy: trên máy mới bấm **Kiểm tra lại**
   hoặc đăng nhập lại. Server sẽ giải phóng suất của máy cũ nếu máy đó
   không còn phiên hợp lệ, rồi kích hoạt máy mới trong cùng transaction.
   Không cần đăng nhập lại máy cũ chỉ để dọn dữ liệu từ bản trước.
4. Smoke: kích hoạt máy A với gói một thiết bị → đăng xuất A → kích hoạt B
   thành công → đăng xuất B → đăng nhập/kích hoạt lại A thành công.
5. Xác minh máy thứ hai vẫn bị giới hạn nếu máy đang giữ suất chưa đăng xuất.

Không đổi API/DTO hoặc migration SQL. Server vẫn tương thích desktop cũ,
nhưng cần cập nhật desktop để có bước refresh trước logout. Deploy binary
không tự sửa dữ liệu: suất cũ được xử lý khi nhận logout hoặc yêu cầu kích hoạt.
Nút Admin **Thu hồi thiết bị** vẫn là thao tác chặn thiết bị, không dùng thay
cho chuyển máy bằng đăng xuất.

Gói server đã tạo cục bộ cho lần bổ sung:
`artifacts/TOOL-SERVER-IIS-dll-runtime-win-x64-20260918-150632-49d9ba57.zip`.
SHA-256: `4ba7fe1ab41a368ce82d1a54159542ea1232c5af977425d224cc58faa3b29308`.
Đã kiểm DLL chứa cả release-on-logout và audit phục hồi suất cũ.
Gói kèm runtime .NET/ASP.NET Core 10.0.9; cấu hình nhạy cảm đã được script
publish bỏ trống. **Giữ nguyên appsettings/secret trên hosting khi cập nhật**.
Chưa upload, restart website hoặc sửa SQL production. Desktop cần publish
lại từ source mới để có bước refresh trước logout; chưa tạo ZIP desktop mới.

## Kiểm chứng

### Bổ sung xử lý suất cũ và refresh trước logout

- Nhóm auth/session/license liên quan: **38 Passed / 0 Failed / 0 Skipped**,
  gồm 17 bài `AuthLogoutDeviceTests` và 7 bài `AccountLogoutRefreshTests`.
- Bổ sung regression cho suất còn Active khi phiên đã Revoked/Expired hoặc
  quá hạn; giữ suất của phiên vẫn hợp lệ, kể cả heartbeat cũ; cô lập tài khoản;
  rollback khi audit lỗi; từ chối phiên gọi đã hết hạn trước khi thu hồi suất.
- Desktop kiểm refresh trước logout, refresh token mới sau rotation, logout
  tất cả, giữ token khi lỗi mạng/server để thử lại và invalidation khi `401`.
- Restore/build Release: Passed, MSBuild 0 warning / 0 error; Vite vẫn cảnh
  báo chunk trên 500 kB.
- Toàn suite .NET: **1.517 Passed / 0 Failed / 13 Skipped / 1.530 Total**,
  collection tuần tự, TEMP/TMP và `WEBVIEW2_USER_DATA_FOLDER` riêng trên D.
  TRX: `.tmp/auth-logout-validation/logout-recovery-full.trx`.
- `npm ci --no-audit --no-fund`, `npm run build`: Passed. Frontend
  `npm test -- --maxWorkers=1 --no-file-parallelism`:
  **268 Passed / 0 Failed / 0 Skipped**, 43 file.
- `scripts/Publish-ToolServerIis.ps1`: Passed; đã đối chiếu checksum gói,
  marker code trong DLL và xác nhận gói không chứa SQL connection/signing key.
- Chưa xác minh runtime hosting hoặc hành vi khóa SQL Server đích; các bài
  model/SQL opt-in bị Skipped không được coi là nghiệm thu.

### Lượt kiểm chứng bản sửa logout ban đầu

- Đã thêm 11 test quan hệ SQLite trong
  `TOOL-TESTS/Authentication/AuthLogoutDeviceTests.cs`, dùng Identity và các
  service đăng nhập/kích hoạt/đăng xuất thật với tài khoản giả, thời gian giả.
- Test bao phủ đổi máy và quay lại, giới hạn thiết bị, thu hồi token, phạm vi
  tài khoản/thiết bị, đăng xuất tất cả, phiên cũ/phiên hết hạn, rollback audit
  và giữ nguyên lệnh cấm thiết bị của Admin.
- `dotnet restore TOOL_GEN_POST_VIDEO.slnx`: Passed.
- `dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore`: Passed,
  MSBuild 0 warning / 0 error. Vite có cảnh báo chunk trên 500 kB.
- Nhóm mới `AuthLogoutDeviceTests`: **11 Passed / 0 Failed / 0 Skipped**.
- Toàn bộ .NET, collection tuần tự, TEMP/TMP riêng trên D:
  **1.503 Passed / 1 Failed / 13 Skipped / 1.517 Total**. Bài lỗi duy nhất
  là `LoginWebViewIntegrationTests` do WebView2 không khởi tạo với thư mục
  dữ liệu đăng nhập dùng chung; desktop đang mở trên máy kiểm thử.
- Chạy lại đúng bài lỗi với `WEBVIEW2_USER_DATA_FOLDER` trỏ tới thư mục
  test riêng: **1 Passed / 0 Failed / 0 Skipped**. Không sửa bài test hoặc
  dừng ứng dụng của người dùng. Tổng hợp kết quả cuối theo từng bài:
  **1.504 Passed / 0 Failed / 13 Skipped**; đây là tổng hợp lượt toàn suite
  và lượt chạy lại có thay đổi môi trường, không phải một lượt full-suite
  duy nhất toàn bộ xanh.
- Frontend: `npm ci --no-audit --no-fund`, `npm run build` Passed;
  `npm test -- --maxWorkers=1 --no-file-parallelism`:
  **268 Passed / 0 Failed / 0 Skipped**, trên 43 file.
- TRX cục bộ: `.tmp/auth-logout-validation/logout-device-release.trx` và
  `.tmp/auth-logout-validation/logout-webview-isolated.trx`. Những bài
  model/runtime/SQL opt-in bị Skipped không được tính là đã nghiệm thu.
- Chưa deploy hoặc smoke server/SQL Server đích. SQLite không chứng minh
  hành vi khóa cạnh tranh của SQL Server trên môi trường production.

Rollback bằng bản server trước sẽ khôi phục hành vi logout cũ. Những suất
đã giải phóng vẫn có thể kích hoạt lại qua luồng có sẵn; không cần rollback
schema hoặc xóa lịch sử.
