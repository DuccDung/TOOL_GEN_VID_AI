# Triển khai UI tạo gói và mở bán

**Đã thay thế phần giữ theme/dialog cũ bằng [hợp nhất UI Admin](PLAN_TASK_HOP_NHAT_UI_ADMIN.md).** Các bước và kết quả bên dưới ghi lịch sử trước hợp nhất; source hiện hành dùng một theme sáng và một form cho tạo/sửa/nhân bản.

Mục tiêu: Admin tạo gói và liên kết nhóm nhận khách trong một form theo mockup đã gửi, có xem trước gói, lưu nháp và mở bán. Cấu hình AI, ngân sách, sức chứa và SePay vẫn do luồng quản trị hiện hành kiểm tra; tạo gói không tạo request provider hoặc thanh toán.

Phạm vi được người dùng yêu cầu triển khai ngày 2026-10-13 trên `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree có các thay đổi chưa commit từ trước.

## Điều chỉnh theo mockup và cách mở local — 2026-10-13

Sau phản hồi chạy local không thấy giống ảnh, đã đối chiếu lại mockup: bản đầu giữ sidebar tối và form vuông của shell cũ, chưa đáp ứng phần nhìn của ảnh. Đã sửa màn hình tạo gói sang sidebar sáng, mục đang chọn xanh nhạt, form/ô nhập bo góc và có viền, preview trắng cùng nút xanh rõ. Theme áp dụng khi mở form, được gỡ khi chuyển mục/đóng/logout; dữ liệu form và guard nghiệp vụ giữ nguyên. Các ảnh ở thư mục `reference-fix/screenshots` là bản mới, ảnh ở thư mục `screenshots` ban đầu chỉ là lịch sử của bản trước.

Nút vào form đổi thành **Tạo gói & mở bán**. Có đường dẫn mở trực tiếp `https://localhost:7202/admin?view=create-plan` với profile `https` hiện hành của `TOOL-SERVER`; vẫn yêu cầu phiên Global Admin, không tự gửi POST. Sau login/refresh, form mở đúng vị trí và khôi phục request chưa xác nhận; đóng form trả focus về nút tạo gói. Meta `videomaker-plan-composer-build=20261013.2` hỗ trợ kiểm bản Razor được nạp.

Lúc kiểm tra 18:33 ngày 13/10/2026, ba cổng localhost 7202/5021/7242 không lắng nghe; chưa có URL/màn hình runtime người dùng đang mở để xác định nguyên nhân không thấy form. Binary Debug có timestamp 16:03 đã chứa API/Razor/asset mới, nên không kết luận người dùng chạy binary cũ từ lời phản hồi. Ảnh này thuộc Web Admin của `TOOL-SERVER`, không phải màn hình mua gói trong desktop `TOOL-LOCAL`. Đã build lại cả Debug và Release trong lượt sửa giao diện; các lệnh build không khởi động server hoặc ghi database.

[Ảnh desktop mới](artifacts/admin-plan-composer-20261013/reference-fix/screenshots/plan-composer-1920.png), [ảnh mobile mới](artifacts/admin-plan-composer-20261013/reference-fix/screenshots/plan-composer-390.png). Browser cuối **49 Passed / 0 Failed / 0 Skipped** (19 ca form mới + 30 ca shell/phân bổ/SePay), có kiểm màu sidebar trắng, border/radius theo mockup, mở URL trực tiếp, phiên hết hạn rồi đăng nhập lại, thoát/quay về form và focus. Đã xem ảnh mới; mọi HTTP trong browser vẫn là fixture, không phải smoke đăng nhập/server/database thật. Frontend chạy lại **296 Passed / 0 Failed / 0 Skipped**; restore/npm ci/build và Debug server/Release solution build đạt **0 Warning / 0 Error** MSBuild, Vite giữ cảnh báo chunk hiện có. Full .NET lượt mới **1670 Passed / 0 Failed / 13 Skipped / 1683 Total**, 5 phút 3 giây, TRX `reference-fix/full-tests/Admin_DUNGDEV_2026-10-13_18_45_31_net10.0.trx`. Sau chỉnh JS cho link khi session hết hạn, chạy lại nhóm static UI .NET **9 Passed / 0 Failed / 0 Skipped** và browser toàn Admin; assembly server/test giữ nguyên. Không coi model/SQL bị skip là đã nghiệm thu runtime.

Lượt browser đầu khi chỉnh hình thức có **41 Passed / 7 Failed / 0 Skipped**: CSS opacity cũ thắng selector preview mới và fixture chờ trang Tổng quan hiện dù deep link đã mở form. Đã sửa specificity CSS và chờ DOM render thay vì bắt view khác phải hiện. Lượt tiếp theo **47 Passed / 1 Failed / 0 Skipped**: bài replay vẫn bấm nút danh sách đã ẩn sau reload tự mở form; cập nhật bài kiểm để xác minh form khôi phục trực tiếp, giữ nguyên assertion payload/ID và không POST trùng. Lượt 48 ca đạt trước khi thêm regression phiên hết hạn; lượt cuối 49 ca đạt như trên, không skip/giảm assertion hình thức để đạt. Log, hash và tổng hợp cuối nằm trong `artifacts/admin-plan-composer-20261013/reference-fix`.

## Task và tiêu chí nghiệm thu

| Task | Nội dung | Tiêu chí | Trạng thái |
|---|---|---|---|
| P01 | Contract cho form và trạng thái mở bán | DTO mới, không đổi shape desktop, không chứa secret | Hoàn tất trong source |
| P02 | Server tạo gói cùng liên kết phân bổ | Transaction, kiểm nhóm/tổ chức/readiness/sức chứa/SePay; replay cùng yêu cầu, conflict khác payload | Hoàn tất trong source |
| P03 | Form tạo gói theo mockup | Năm trường chính, thiết lập nâng cao, preview, lưu nháp và tạo/mở bán | Hoàn tất trong source |
| P04 | Nhân bản và phục hồi thao tác | Giữ quyền của gói nguồn, bảo vệ request khi timeout/logout/đổi tài khoản; giữ chỉnh sửa gói hiện có | Hoàn tất trong source |
| P05 | Lỗi và điều hướng setup | Phân biệt chưa có tổ chức, chưa kiểm tra, thiếu AI, hết chỗ và SePay; đến đúng màn hình sửa | Hoàn tất trong source |
| P06 | Kiểm thử và kiểm tra ảnh | Test server trên dữ liệu giả/relational, browser ở desktop/mobile, lệnh chuẩn solution/frontend | Hoàn tất kiểm thử tự động và xem ảnh |
| P07 | Tài liệu và giới hạn triển khai | Báo Passed/Failed/Skipped thật; phân biệt source/fixture với runtime/production | Hoàn tất; runtime/production chưa xác minh |

## Luồng và quy tắc

Màn hình mới mở từ Tạo gói hoặc Nhân bản gói. Nhóm được lấy từ server; nếu chỉ có một nhóm sẵn sàng thì chọn trước. Số chỗ là sức chứa dùng chung của nhóm, không nhân lên theo số gói. Nhóm có tổ chức đã kiểm tra nhưng chưa gắn gói nào vẫn được chọn để tạo gói đầu tiên.

Lưu nháp tạo gói riêng tư, chưa cho cấp hoặc bán; được phép chưa chọn nhóm. Tạo và mở bán cần giá VND nguyên dương, thời hạn hợp lệ, nhóm Active có tổ chức Active/bật phân bổ/đã kiểm tra và còn chỗ, readiness AI hiện hành cùng SePay sẵn sàng. Server kiểm lại khi ghi. Không tự bật nhóm, thay credential/rate/budget hoặc giữ seat khi tạo gói.

Gói và mapping mới cùng thành công hoặc rollback. ID thao tác ổn định khi gửi lại; request khác payload hoặc khác Admin không được replay. Không sửa database schema. Luồng chỉnh sửa gói hiện có và các đơn/license đã cấp được giữ.

## Kiểm thử và bằng chứng

Kiểm thử ngày 2026-10-13 trên checkout `main`, nền `f0651c918f28ca477f28875818148e9e86240b7b` cùng working tree chưa commit; không dùng bằng chứng của checkout khác. Restore solution, `npm ci --no-audit --no-fund`, Release build solution và `npm run build` đã chạy đạt. MSBuild **0 Warning / 0 Error**; Vite còn cảnh báo bundle dashboard >500 kB hiện có.

| Phạm vi | Passed | Failed | Skipped | Bằng chứng |
|---|---:|---:|---:|---|
| Server tạo gói mới | 19 | 0 | 0 | `artifacts/admin-plan-composer-20261013/server-tests/*.trx` |
| Frontend React hiện hành | 296 | 0 | 0 | `npm test`, 45 file |
| Browser Admin, phân bổ, SePay và tạo gói | 46 | 0 | 0 | Bốn script `TOOL-TESTS/Admin/*.browser.test.cjs`, gồm 16 ca tạo gói |
| Full .NET, collection tuần tự, TEMP/TMP ngắn trên D | 1670 | 0 | 13 | `artifacts/admin-plan-composer-20261013/full-tests-verified/Admin_DUNGDEV_2026-10-13_15_54_05_net10.0.trx`; 1683 Total, 5 phút 17 giây |

Server test dùng SQLite private relational, readiness/thanh toán giả: gói đầu tiên, từng điều kiện chặn, dữ liệu đổi sau GET, replay/conflict, nháp, giữ quyền gói nguồn và rollback khi mapping lỗi. Browser chạy Razor/CSS/JS hiện hành với mọi HTTP được intercept, kiểm validation, lỗi đọc/ghi, chống gửi trùng, giữ form khi đi setup, phục hồi request sau mất phản hồi/reload, timeout và logout. Không tạo gói, migration, thanh toán hoặc request provider trên database/môi trường thật.

Đã kiểm layout ở 320/390/768/1366/1920 px và xem ảnh desktop/mobile. [Ảnh desktop](artifacts/admin-plan-composer-20261013/screenshots/plan-composer-1920.png), [ảnh mobile](artifacts/admin-plan-composer-20261013/screenshots/plan-composer-390.png). Tên nhóm, giá và 10 chỗ trong ảnh là dữ liệu fixture, không chứng minh nhóm production đã sẵn sàng.

Lượt full .NET đầu có **1669 Passed / 1 Failed / 13 Skipped / 1683 Total**: bài kiểm selector chỉ đọc `admin.css`, chưa đọc CSS module mới. Đã sửa bài kiểm để đọc toàn bộ stylesheet thực sự được Razor liên kết, giữ nguyên yêu cầu mọi selector phải tồn tại. Lượt tiếp theo cũng **1669 Passed / 1 Failed / 13 Skipped / 1683 Total**, duy nhất bài Vietsub dài 2 giờ thiếu dung lượng ổ C. Khi chuyển TEMP/TMP vào thư mục artifact sâu trong repository, SQLite báo không mở được file vì đường dẫn dài; đã dừng lượt này, không coi là một full suite hoàn tất. Thư mục tạm ngắn trên D được xác minh bằng **42 Passed / 0 Failed / 0 Skipped** cho các bài tạo gói, translation executor và short-video library. Full cuối đạt **1670 Passed / 0 Failed / 13 Skipped / 1683 Total**, gồm cả bài selector và Vietsub dài 2 giờ. Không sửa hoặc bỏ qua kiểm tra dung lượng/SQLite. Các bài model/SQL opt-in bị skip không tính là đã nghiệm thu model/runtime.

Hash source/assembly của lượt cuối ghi trong `artifacts/admin-plan-composer-20261013/source-hashes.json`; tổng hợp kết quả ghi trong `verification.json` cùng thư mục. `git diff --check` đạt; chỉ còn cảnh báo chuyển LF/CRLF của working tree hiện có.

Lệnh chính đã chạy:

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS/TOOL-TESTS.csproj -c Release --no-build --logger trx --results-directory artifacts/admin-plan-composer-20261013/full-tests-verified -- xUnit.ParallelizeTestCollections=false
```

Lệnh full cuối đặt TEMP/TMP cho riêng process vào thư mục ngắn trên D ghi trong `artifacts/admin-plan-composer-20261013/temp-root.txt`; không đổi môi trường Windows toàn cục. Trong `TOOL-LOCAL/Web` đã chạy `npm ci --no-audit --no-fund`, `npm run build`, `npm test`. Browser dùng Playwright 1.62.1 phù hợp Chromium đã cài, qua `VIDEOMAKER_PLAYWRIGHT_MODULE`; chạy cả bốn script `admin-shell`, `admin-allocation-setup`, `admin-sepay`, `admin-plan-composer` bằng `node --test`.

## Source triển khai

| File | Vai trò |
|---|---|
| `TOOL-SHARED.Contracts/Accounts/AdminLicensePlanComposerContracts.cs` | DTO trạng thái nhóm và request/response form |
| `TOOL-SERVER/Accounts/AdminLicensePlanComposerService.cs` | Điều kiện mở bán, ghi transaction, clone quyền và replay |
| `TOOL-SERVER/Controllers/AdminLicensePlanComposerController.cs` | API Global Admin, no-store |
| `TOOL-SERVER/wwwroot/admin/admin-plan-composer.js/css` | Form, preview, trạng thái, setup và phục hồi request |
| `TOOL-SERVER/Pages/Admin/Index.cshtml`, `wwwroot/admin/admin.js` | Nạp module, nút tạo/nhân bản và cập nhật danh sách |
| `TOOL-SERVER/wwwroot/admin/admin-organizations.js` | Mở đúng nhóm khi đi setup |
| `TOOL-TESTS/Payments/AdminLicensePlanComposerTests.cs` | Test server với relational fixture |
| `TOOL-TESTS/Admin/admin-plan-composer.browser.test.cjs` | Test UI, lỗi, timeout, replay và responsive |

`AdminLicenseService.ToPlanResponse` đổi phạm vi sang internal để tái dùng response; service mới được đăng ký scoped trong `Program.cs`. Không sửa model/migration hoặc workflow desktop cho tính năng này. Các thay đổi chưa commit có sẵn được giữ.

## Cách sử dụng sau khi nạp server mới

1. Chạy project `TOOL-SERVER` với profile `https`, mở `https://localhost:7202/admin?view=create-plan`, đăng nhập Global Admin; hoặc chọn **Gói sử dụng → Tạo gói & mở bán**. Nếu server đang chạy bản trước, nạp lại bản build mới và tải lại trang bằng Ctrl+F5. Chạy riêng `TOOL-LOCAL` không mở màn hình quản trị này.
2. Nhập tên, thời hạn, giá, số thiết bị và nhóm nhận khách. Preview cập nhật ngay theo nội dung nhập.
3. Nếu nhóm chưa sẵn sàng, bấm **Thiết lập nhóm nhận khách** để cấu hình tổ chức/sức chứa và kiểm tra readiness; nếu thiếu thanh toán, bấm **Thiết lập SePay**. Quay lại Gói sử dụng giữ form và tự tải trạng thái mới; có nút **Kiểm tra lại**.
4. Chọn **Lưu nháp** nếu chưa hoàn tất, hoặc **Tạo và mở bán** khi các điều kiện đạt. Nháp được chỉnh bằng chức năng chỉnh sửa gói hiện hành; gói chưa có mapping cần liên kết nhóm trước khi bán.
5. Muốn tạo gói tương tự, chọn **Nhân bản gói** trên danh sách rồi sửa tên/giá/thời hạn. Mã gói tạo tự động.
6. Nếu chưa xác nhận kết quả gửi, dùng **Thử lại** với request đã giữ trong phiên. Không sửa request đang chưa rõ kết quả; server trả gói đã lưu nếu lần gửi trước đã commit.

## Giới hạn và nghiệm thu runtime

**Có source và kiểm thử tự động trên working tree nêu trên. Chưa smoke thao tác tạo gói với phiên Admin/database đích và chưa rollout production.** Không restart server/IDE đang chạy. Cần nạp bản server build mới vì thêm cả API và Razor, không chỉ thay JS/CSS; luồng hiện có vẫn cần schema provisioning/SePay nền và cấu hình hợp lệ tại môi trường đích. Tính năng này không thêm migration.

Khi đã chọn môi trường kiểm thử được phép ghi, kiểm tra bằng tài khoản Admin: lưu một nháp và xác minh chưa bán/cấp, tạo một gói với nhóm thực sự sẵn sàng, xác minh mapping/audit, đọc danh sách offers bằng tài khoản khách và thử lại cùng thao tác. Nghiệm thu tạo gói không cần gọi AI hoặc gửi webhook/thanh toán có tiền. Test model/SQL opt-in bị skip không tính là runtime/model đã đạt.
