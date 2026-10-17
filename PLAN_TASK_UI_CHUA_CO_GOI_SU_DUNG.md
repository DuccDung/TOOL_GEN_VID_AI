# Triển khai giao diện khi hệ thống chưa cung cấp gói sử dụng

Màn hình khách hàng hiển thị trạng thái thông tin “Hiện chưa có gói sử dụng” khi danh sách gói rỗng hoặc server trả `payments_unavailable`. Giao diện theo mockup đã được người dùng chọn: biểu tượng thẻ và đồng hồ màu xanh, thông báo ở giữa, nút **Kiểm tra lại**, nút **Đăng xuất** trên header và nền dashboard bị khóa.

Ngày triển khai: 2026-10-13, Asia/Bangkok. Nhánh `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, cùng các thay đổi chưa commit có sẵn. Kết quả kiểm thử phải gắn với working tree này.

## Phạm vi

- Desktop React: màn hình khóa license và popup **Nâng cấp gói**.
- Server: `GET /api/license/offers` trả `200` với danh sách rỗng khi thanh toán chưa sẵn sàng; không ném exception cho trạng thái ngừng mở bán dự kiến.
- Dùng API/bridge hiện có, không đổi DTO public hoặc schema. Tạo thanh toán vẫn trả `503 payments_unavailable` nếu chưa sẵn sàng; lỗi đọc cấu hình/database vẫn là lỗi thật.
- Giữ quyền truy cập bị khóa đến khi có license/device hợp lệ.
- Không bật thanh toán, thêm tài khoản ngân hàng, sửa database, gọi provider hoặc triển khai production trong thay đổi UI này.

## Task triển khai

| Task | Công việc | Nghiệm thu | Trạng thái |
|---|---|---|---|
| 1 | Tách trạng thái tải danh sách gói khỏi trạng thái thanh toán | Phân biệt loading, available, unavailable và error; request riêng theo tài khoản | Hoàn thành |
| 2 | Tạo UI theo mockup và áp dụng tại hai màn hình | Thông báo trung tính, nút kiểm tra lại, typography và bố cục responsive | Hoàn thành |
| 3 | Bảo vệ retry và phản hồi cũ | Không gửi lặp khi đang tải; timeout 30 giây; bỏ response cũ sau retry/đổi tài khoản | Hoàn thành |
| 4 | Giữ checkout và license gate | Đơn cũ vẫn phục hồi được khi ngừng bán gói mới; lỗi mạng vẫn là lỗi; empty không cấp quyền | Hoàn thành |
| 5 | Kiểm thử và kiểm tra giao diện | Frontend, browser fixture, restore/build/test .NET; báo riêng Passed/Failed/Skipped | Hoàn thành |
| 6 | Sửa API đọc sau phản hồi vẫn gặp exception cũ | Không ném exception khi chưa mở bán; theo công tắc/tài khoản SQL; không tạo thanh toán hoặc license; lỗi hạ tầng không bị biến thành empty | Hoàn thành |

## Ma trận hành vi

| Tình huống | UI |
|---|---|
| Chưa có phản hồi | Đang tải; khóa bấm kiểm tra lặp; vẫn cho đăng xuất |
| API trả danh sách rỗng | Hiện chưa có gói sử dụng; không quay vô hạn ở trạng thái loading |
| Thanh toán tắt, thiếu tài khoản nhận hoặc cấu hình không hợp lệ | API đọc gói trả `200 []`; UI hiện trạng thái thông tin |
| API cũ trả `payments_unavailable`, hoặc tạo thanh toán gặp trạng thái này | Cùng trạng thái thông tin; không đưa thông báo kỹ thuật vào banner đỏ |
| Lỗi mạng, timeout, lỗi server khác hoặc dữ liệu sai | Chưa thể tải danh sách gói; giữ lỗi riêng và cho kiểm tra lại |
| Kiểm tra lại có gói | Hiển thị gói thực; chưa mở dashboard khi thiếu license |
| Đổi tài khoản hoặc phản hồi đến muộn | Bỏ kết quả cũ và giữ context hiện hành |
| Đơn thanh toán đang chờ đã có | Phục hồi checkout hiện hành; không tạo đơn mới khi chỉ kiểm tra gói |
| License hết hạn | Giữ gate; áp dụng trạng thái gói tương tự tài khoản chưa có license |
| Device/session limit, suspended hoặc revoked | Giữ luồng phục hồi quyền truy cập hiện hành |

## Kết quả kiểm chứng

Kết quả thực chạy trên working tree nêu trên, trước khi bổ sung nhánh đọc ở server:

| Kiểm tra | Passed | Failed | Skipped | Phạm vi |
|---|---:|---:|---:|---|
| Frontend `npm test` | 296 | 0 | 0 | 45 file; có regression cho empty, lỗi thật, retry/timeout, đổi tài khoản, checkout và khóa license |
| Browser `license-empty-state.browser.test.cjs` | 3 | 0 | 0 | Bundle production hiện hành, bridge giả, Edge headless; 320/768/1440 px, bàn phím, đổi trạng thái và chặn mọi outbound |
| Full .NET Release | 1645 | 0 | 13 | 1658 tổng, collection tuần tự, 5 phút 27 giây; các bài model/SQL opt-in bị skip không được tính là nghiệm thu |

`npm ci`, frontend production build, `dotnet restore` và Release build toàn solution đạt. MSBuild 0 Warning / 0 Error; Vite còn cảnh báo chunk >500 kB của dashboard. Target `CopyDashboardWeb` đã đồng bộ bundle Debug; 24 file trong mỗi bundle Debug/Release khớp SHA-256 với `Web/dist`.

Bằng chứng local: [TRX .NET](artifacts/license-empty-state-ui-20261013/license-ui-final.trx), [ảnh desktop 1440 px](artifacts/license-empty-state-ui-20261013/screenshots/license-unavailable-1440.png), [ảnh hẹp 320 px](artifacts/license-empty-state-ui-20261013/screenshots/license-unavailable-320.png). Đã xem ảnh; artifact không phải nguồn sự thật thay source.

## Bổ sung server sau phản hồi vẫn gặp lỗi cũ

UI đã đổi nhưng `GetOffersAsync` vẫn gọi `EnsurePaymentsReady`, khiến Visual Studio có thể dừng ở exception trước khi desktop nhận response. Sáu regression mới chạy trên source trước sửa: **1 Passed / 5 Failed / 0 Skipped**, năm ca tái hiện đúng `AccountApiException` khi chưa sẵn sàng. Source mới trả danh sách rỗng ở nhánh đọc, giữ guard tạo thanh toán và không bắt rồi bỏ qua lỗi hạ tầng.

Nhóm thanh toán trên binary mới: **88 Passed / 0 Failed / 0 Skipped**, gồm sáu ca mới cho tắt thanh toán, thiếu receiver, cấu hình QR không hợp lệ, lỗi đọc cấu hình và chuyển trạng thái SQL qua Admin. Fixture dùng database giả; không ghi dữ liệu production. Restore, Release build solution và Debug build server đạt **0 Warning / 0 Error** MSBuild; cảnh báo chunk Vite vẫn còn. Cấu hình nguồn và output desktop Debug/Release cùng trỏ `https://localhost:7202/`; 24 file UI của mỗi output vẫn khớp SHA-256 với `Web/dist`.

Full .NET Release sau sửa server, collection tuần tự: **1651 Passed / 0 Failed / 13 Skipped / 1664 Total**, 5 phút 6 giây. Các bài model/SQL opt-in bị skip không tính là nghiệm thu. Frontend/browser không chạy lại trong lượt bổ sung C# này; source frontend và hash bundle khớp lượt kiểm UI phía trên.

Bằng chứng: [tái hiện trước sửa](artifacts/license-empty-state-ui-20261013/server-fix/offers-before-fix.trx), [regression thanh toán sau sửa](artifacts/license-empty-state-ui-20261013/server-fix/payments-after-fix.trx), [full .NET sau sửa server](artifacts/license-empty-state-ui-20261013/server-fix/license-server-fix-final.trx). Đây là kiểm thử tự động trên working tree, chưa smoke phiên desktop/server đăng nhập thật.

## Cách kiểm tra trên desktop

Khởi động phiên debug server mới để nạp assembly đã build lại; phiên đang dừng ở exception vẫn giữ assembly cũ trong bộ nhớ. Mở lại tool để nạp bundle mới. Khi chưa mở bán, đọc danh sách gói trả `200 []` và hiện “Hiện chưa có gói sử dụng”; không cần tiếp tục qua exception của API đọc. Điều kiện từ chối tạo thanh toán khi chưa sẵn sàng vẫn được giữ.

Browser fixture không thay thế smoke có tài khoản trên desktop/database thật. Chưa rollout production, bật thanh toán, thay ngân hàng hoặc gửi giao dịch/webhook thật trong thay đổi này.
