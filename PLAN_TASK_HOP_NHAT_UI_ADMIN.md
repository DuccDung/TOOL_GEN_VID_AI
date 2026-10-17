# Hợp nhất UI Admin và gỡ giao diện cũ

## Thu gọn header chung — 2026-10-13

Người dùng đã duyệt plan và yêu cầu triển khai. Trên `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit, giữ các thay đổi có sẵn. Marker mới **`admin-only-new-20261013.2`**, composer **`20261013.4`**. Đây là thay đổi trình bày header dùng chung, không sửa API/schema/nghiệp vụ.

| Task | Nội dung | Trạng thái |
|---|---|---|
| H01 | Bỏ nhãn in hoa lặp trên tiêu đề và chữ Admin Console bên phải | Hoàn tất markup và gỡ tham chiếu JS/CSS không còn dùng |
| H02 | Giảm tiêu đề desktop xuống 28–30 px, mô tả 15 px | Hoàn tất; mobile 26/14 px, giữ heading h1 và nội dung mô tả |
| H03 | Thu gọn padding và căn thao tác cạnh tiêu đề | Hoàn tất; desktop header 142–145 px, nút cao tối thiểu 44 px |
| H04 | Áp dụng header chung, xử lý màn hẹp/tên dài | Hoàn tất; wrap nội dung/thao tác khi cần, composer vẫn dùng header riêng |
| H05 | Kiểm hình thức, chức năng và build | Đạt tự động/build/manifest; đã xem ảnh; chưa nạp process local |

Đo bằng Razor/CSS/font thực ở zoom CSS 100%, cùng nội dung Bảng giá AI như ảnh người dùng: màn 1440 px giảm **237,5 → 144,5 px** (khoảng 39%); màn 390 px giảm **272,1 → 160,2 px**. Ở 320/390/768/1024/1280/1440/1920 px không tràn ngang; nút thao tác cao 44,5 px. Trên 320 px, nút tự xuống hàng để giữ khả năng thao tác; mô tả được xuống dòng thay vì cắt mất nội dung. Đã kiểm tiêu đề dài và nhiều nút trên mobile. Đây là kiểm hình học bằng viewport CSS, không suy ra zoom browser thủ công đã đạt.

[Header desktop sau sửa](artifacts/admin-compact-header-20261013/screenshots/pricing-header-after-1440.png), [trước sửa](artifacts/admin-compact-header-20261013/screenshots/pricing-header-before-1440.png), [mobile sau sửa](artifacts/admin-compact-header-20261013/screenshots/pricing-header-after-390.png). Ảnh crop dùng Razor/CSS với văn bản mô phỏng trang, không gọi API/database; ảnh đầy đủ trong thư mục screenshots của browser dùng API fixture. Các kết quả N/V/U dưới đây là lịch sử trước lượt thu gọn header.

| Phạm vi thực chạy sau sửa header | Passed | Failed | Skipped | Bằng chứng |
|---|---:|---:|---:|---|
| Full .NET | 1682 | 0 | 13 | `artifacts/admin-compact-header-20261013/full-tests/header-full.trx`; 1695 Total, 4 phút 56 giây |
| Browser Admin/TikTok | 71 | 0 | 0 | `artifacts/admin-compact-header-20261013/browser.log` |
| Frontend React | 296 | 0 | 0 | `artifacts/admin-compact-header-20261013/frontend-test.log`; 45 file |

Restore, Release solution build, Debug server ở thư mục riêng, npm ci/build đạt; MSBuild **0 Warning / 0 Error**, Vite giữ cảnh báo chunk >500 KB có sẵn. Đã chạy bộ hiện có, không thêm test chỉ để kiểm từng declaration CSS; fixture TikTok được cập nhật để không truy cập nhãn đã xóa. 15 asset × Debug/Release có integrity khớp source, hai assembly chứa marker mới. Kết quả/hình học/hash/runtime ở `artifacts/admin-compact-header-20261013/verification.json`. Model/SQL opt-in bị Skipped không tính là runtime đã đạt.

GET `https://localhost:7202/admin` cuối lượt này trả **`admin-only-new-20261013.1`**, còn `pageEyebrow`: process đã nạp UI mới của lượt trước nhưng chưa nạp header thu gọn. Trong Visual Studio, **Stop Debugging → Build `TOOL-SERVER` → F5** profile `https`, rồi mở `/admin`; marker cần là **`admin-only-new-20261013.2`**. Chưa tự restart process, smoke phiên Admin/database thật hoặc rollout production; không thay database/provider thật.

## Thay toàn bộ giao diện Admin — đã triển khai source, chưa nạp process local

Yêu cầu mới ngày 2026-10-13: xóa toàn bộ phần trình bày Admin cũ, chỉ dùng UI mới, bao gồm màn mặc định ngay sau đăng nhập và các mục/popup quản trị. Sau lượt lập plan, người dùng đã yêu cầu triển khai. Thay đổi và kiểm thử trên `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`; working tree chưa commit, giữ nguyên thay đổi không liên quan có sẵn. Marker source/build của lượt N01–N16: **`admin-only-new-20261013.1`**, composer **`20261013.4`**.

### Vấn đề và đích đến

Trước lượt này, login mở Tổng quan theo bố cục cũ; màn composer mới chỉ tự mở theo query create-plan/edit-plan hoặc nút tương ứng. Đây là nguyên nhân vẫn thấy màn cũ sau đăng nhập dù server đã phục vụ font/sidebar mới. Lượt này thay markup và renderer Tổng quan, Người dùng, danh sách gói và Phát hành; login tải API xác minh Admin trước khi hiện shell, rồi mở Tổng quan mới tại `/admin` thông thường.

Đích đến: `/admin` là một entry duy nhất. Đăng nhập thành công mở **Tổng quan mới**, mọi mục dùng cùng layout và component mới. Tổng quan có trạng thái vận hành, những việc cần hoàn tất và thao tác nhanh; thông tin license chi tiết nằm trong màn Người dùng. Link tạo/sửa gói hiện có tiếp tục hoạt động và luôn dùng UI mới. Không tạo trang Admin v2 song song, query bật theme mới, route quay về UI cũ hoặc markup cũ chỉ được ẩn bằng CSS.

Ảnh AI gốc mô tả màn Tạo gói & mở bán, được dùng làm chuẩn phong cách: sidebar sáng, nền xanh rất nhạt, card trắng, font Roboto, icon nhất quán, form dễ đọc, CTA xanh. Những màn chưa có ảnh mẫu phải được thiết kế đầy đủ theo cùng chuẩn này và xem ảnh render, không suy ra đã có thiết kế chỉ vì đổi màu CSS.

Phạm vi xóa là layout/template, renderer trình bày, selector/theme, handler giao diện trùng và asset không còn dùng. Chuyển đầy đủ chức năng quản trị sang component mới. Giữ dữ liệu, DTO/API, quyền, audit/idempotency, license đã cấp và các luồng desktop; không xóa entity/migration hoặc chức năng nghiệp vụ do không xuất hiện trong ảnh mẫu.

### Task triển khai

| Task | Công việc | Tiêu chí nghiệm thu | Trạng thái |
|---|---|---|---|
| N01 | Lập mapping toàn bộ màn, popup và đường render cũ | Mỗi chức năng có vị trí mới; xác định rõ markup/CSS/JS được thay hoặc xóa; kiểm caller trước gỡ | Hoàn tất rà soát; lưu baseline trước sửa |
| N02 | Thiết kế các màn và component còn thiếu | Layout, bảng/danh sách, form, dialog, trạng thái loading/empty/error/blocked và mobile có chuẩn chung theo ảnh mẫu | Hoàn tất source; đã xem ảnh render |
| N03 | Thay màn login, shell và navigation | Một entry `/admin`; khôi phục phiên, hết hạn/logout/deep link giữ đúng; chỉ render shell sau xác minh Admin; không fallback UI cũ | Hoàn tất source; regression login/session/thiếu asset đạt |
| N04 | Xây Tổng quan mới và nối luồng sau login | Login ở `/admin` mở Tổng quan mới; trạng thái/việc cần làm/thao tác nhanh lấy dữ liệu thật; bỏ bố cục Tổng quan cũ | Hoàn tất source; browser login mặc định đạt |
| N05 | Thay Người dùng và quản lý license | Tìm kiếm/phân trang, xem chi tiết, cấp gói, thiết bị/phiên, thu hồi giữ hoạt động; bảng và popup dùng UI mới | Hoàn tất source; tìm kiếm/phân trang/dialog đạt |
| N06 | Thay danh sách gói và nối composer mới | Danh sách mới hiển thị trạng thái bán; tạo/sửa/clone dùng một form; giữ trạng thái nháp/riêng tư/mở bán/tạm ngừng, revision và retry | Hoàn tất source; composer/browser/nghiệp vụ đạt |
| N07 | Thay Trung tâm thiết lập, danh sách/chi tiết tổ chức | Thành viên/role, provider/credential, policy, budget, usage và audit có UI mới; blocker rõ; giữ phân quyền và Owner cuối cùng | Hoàn tất source; API/RBAC hiện hành được giữ |
| N08 | Thay Phân bổ khách hàng | Nhóm/tổ chức/mapping gói, sức chứa, readiness và danh sách phân bổ có UI mới; giữ bảo vệ seat Active/Reserved/Scheduled | Hoàn tất source; wizard + quản lý chi tiết mới đạt |
| N09 | Thay Bảng giá AI và hướng dẫn chi phí | Chỉ Admin thao tác giá; form/table/helper mới; currency/rate/status/budget chính xác; không biến thiếu rate thành đã sẵn sàng | Hoàn tất component chung; browser/static UI đạt |
| N10 | Thay Thanh toán SePay | Tài khoản nhận tiền, chọn tài khoản, bật/tắt và trạng thái cấu hình dùng UI mới; giữ validation/revision/retry, không tạo thanh toán thật khi test | Hoàn tất danh sách tài khoản mới; browser đạt |
| N11 | Thay Tích hợp TikTok | Cấu hình, credential, kiểm tra kết nối, polling và xác nhận dùng component mới; giữ deadline 15 phút, khóa môi trường và hạn chế publish | Hoàn tất component chung; browser TikTok đạt |
| N12 | Thay Phát hành desktop | Danh sách/chi tiết/form release có UI mới; giữ metadata, checksum, policy cập nhật, quyền và trạng thái; không publish release khi test UI | Hoàn tất source; danh sách/form responsive đạt |
| N13 | Hợp nhất popup và thông báo | Chi tiết người dùng/cấp gói/tổ chức/thành viên/pool/mapping/credential/rate/TikTok/release dùng component chung; focus/ESC/validation/loading đúng | Hoàn tất source; confirm/prompt chuyển sang dialog mới |
| N14 | Gỡ phần trình bày cũ sau chuyển chức năng | Xóa markup, CSS trùng/override theme, renderer/handler/import/asset hết caller; không giữ màn cũ trong DOM hoặc đường truy cập cũ; một bộ UI | Hoàn tất source; gỡ fallback nạp UI cho Razor cũ |
| N15 | Kiểm chức năng và đối chiếu hình thức | Kiểm `/admin` → login → Tổng quan mới → menu/popup; refresh/deep link/logout/session hết hạn; ảnh desktop/mobile và trạng thái lỗi | Đạt tự động; đã xem ảnh; chưa smoke phiên thật/zoom browser thủ công |
| N16 | Build, xác minh bản local và ghi bằng chứng | Restore/build/test thực chạy; JS/CSS/font/assembly cùng bản; local đúng build mới; tách source/fixture/live smoke | Build + manifest đạt; process local vẫn bản trước, chưa nạp UI mới |

Thứ tự: N01–N02 xác định thiết kế/mapping; N03–N04 xử lý ngay login và trang mặc định; N05–N12 chuyển các module; N13 đồng bộ popup; N14 gỡ phần cũ; N15–N16 nghiệm thu toàn bộ. Trong quá trình triển khai, từng module thay đường render đang dùng; kết quả bàn giao chỉ còn UI mới.

### Nghiệm thu bắt buộc

- Đăng nhập từ `/admin` không cần query đặc biệt vẫn nhìn thấy Tổng quan mới, dùng cùng phong cách với composer.
- Bảy mục chính và năm scope Tổ chức & AI đều có UI mới; popup liên quan cũng được chuyển. Không chấp nhận chỉ đổi màu, sidebar hoặc màn tạo gói.
- Xóa các phần trình bày cũ khỏi source và đường gọi sau khi mapping chức năng hoàn tất; không coi ẩn DOM là đã xóa. Không xóa renderer hoặc handler còn phục vụ chức năng chưa được chuyển.
- Một font/theme/layout/component system; đổi menu, mở popup, reload hoặc khôi phục phiên không đổi sang giao diện khác.
- Dữ liệu/permission/readiness/capacity vẫn từ server; trạng thái thiếu cấu hình/lỗi/đang tải không bị làm giả để giống ảnh.
- So form tham chiếu ở 1672 × 941; kiểm các màn ở 1920/1366/1024/768/390/320 px và zoom 125%; không tràn ngang hoặc che nút chính, keyboard/focus/forced colors/reduced motion giữ dùng được.
- Chạy bộ lệnh trong AGENTS.md cùng browser cho các module bị thay; ghi Passed/Failed/Skipped thực chạy. Kiểm UI behavior, không giữ test đòi markup cũ hoặc làm yếu test để vượt qua.
- Chụp ảnh tất cả màn chính và popup đã chuyển. Ảnh fixture ghi rõ dữ liệu giả. Local smoke phải vào đúng luồng login mặc định, không chỉ mở trực tiếp composer.
- Xác minh marker và hash asset của process local. Không tự dừng IDE/server chỉ để giải phóng lock; nếu chưa nạp lại process hoặc chưa có phiên Admin thật thì ghi chưa xác minh tương ứng. Không chạy migration, tạo dữ liệu/chi phí provider hoặc publish thật để test UI.

### Kết quả triển khai N01–N16

`admin-pages.js` là renderer duy nhất cho Tổng quan, Người dùng, danh sách gói, Phát hành, chi tiết người dùng và quản lý nhóm nhận khách. Đã xóa markup Tổng quan và các builder tương ứng cũ, bảng tổ chức/tài khoản nhận tiền cũ, CSS theme cũ và loader bổ sung asset cho Razor cũ. Danh sách gói thành card giá/trạng thái; tổ chức và tài khoản SePay thành card; nhóm nhận khách có wizard cùng quản lý chi tiết mới. Chưa có nhóm thì hướng dẫn hiển thị trực tiếp. Chi tiết người dùng giữ trạng thái tài khoản, lần đăng nhập gần nhất, số thiết bị/phiên, lịch sử gói và thao tác thu hồi trong component mới.

`admin.css` định nghĩa một hệ font/token/component cho login, shell, table, form, card, dialog và trạng thái. `admin-modules.css` chỉ chứa hình học bố cục các module đang dùng, không chứa theme dự phòng. CSS composer và allocation chỉ mở rộng form/wizard theo theme này. Các dialog nghiệp vụ còn dùng giữ chức năng và chuyển sang component chung; `admin-dialogs.js` thay confirm/prompt native bằng dialog bất đồng bộ, giữ cancel/ESC/focus và chặn xác nhận trùng. Handler submit/API/RBAC/budget/readiness hiện hành được giữ.

Shell có thuộc tính `hidden` từ HTML và chỉ mở sau phản hồi API Admin hợp lệ. Không mở từ role lưu trong browser nếu toàn bộ API không phản hồi; tài khoản bị từ chối quay về login. Thiếu module mới thì dừng khởi tạo, không gọi API hoặc hiển thị giao diện cũ. Logout đóng dialog và reset module; link create/edit vẫn dùng composer sau xác minh phiên. Không thêm route hoặc query bật UI mới.

| Phạm vi thực chạy trong lượt này | Passed | Failed | Skipped | Bằng chứng |
|---|---:|---:|---:|---|
| Full .NET | 1682 | 0 | 13 | `artifacts/admin-only-new-20261013/full-tests/admin-only-new-full.trx`; 1695 Total, 3 phút 55 giây |
| .NET UI sau build cuối | 37 | 0 | 0 | `artifacts/admin-only-new-20261013/ui-tests/ui-source-final.trx`; subset, không cộng vào total |
| Browser Admin/TikTok cuối | 71 | 0 | 0 | `artifacts/admin-only-new-20261013/browser-final.log` |
| Frontend React | 296 | 0 | 0 | `artifacts/admin-only-new-20261013/frontend-test.log`; 45 file |

Restore, Release solution build, Debug server vào thư mục riêng, npm ci/build đạt. MSBuild **0 Warning / 0 Error**; Vite còn cảnh báo chunk >500 KB có sẵn. Full .NET/frontend chạy trước chỉnh JS/CSS cuối về guard, empty state và thông tin chi tiết tài khoản; các chỉnh này đã kiểm bằng browser cuối và đối chiếu build/asset. Model/SQL opt-in bị skip không tính là runtime/model đã đạt. Các assertion đòi DOM/CSS hoặc fallback cũ được chuyển sang hành vi mới, giữ validation/quyền; không phục hồi UI cũ để đạt test. Regression thiếu CSS phát hiện shell chỉ ẩn bằng class: đã sửa bằng `hidden` HTML và kiểm lại fixture TikTok mô phỏng đăng nhập.

Ảnh dùng Razor/CSS/JS hiện hành với API giả, không dùng server/database/provider thật. Đã xem [màn ngay sau login mặc định](artifacts/admin-only-new-20261013/screenshots/after-login-default-1440.png), [form cùng kích thước ảnh mẫu](artifacts/admin-only-new-20261013/screenshots/plan-composer-reference-1672.png), [mobile](artifacts/admin-only-new-20261013/screenshots/overview-390.png), [gói](artifacts/admin-only-new-20261013/screenshots/plans-1440.png), [tổ chức/API AI](artifacts/admin-only-new-20261013/screenshots/organization-detail-providers-1440.png), [SePay](artifacts/admin-only-new-20261013/screenshots/sepay-admin-1440.png) và [TikTok](artifacts/admin-only-new-20261013/screenshots/tiktok-admin-desktop.png). Thư mục screenshots có các scope tổ chức, phân bổ, giá, chi phí, người dùng, release và các bước thiết lập. Browser kiểm form 320/390/768/1366/1672/1920 px, shell và SePay ở nhiều độ rộng, keyboard/ESC/forced colors/reduced motion. Chưa kiểm zoom browser 125% thủ công hoặc chụp từng popup với dữ liệu runtime thật.

15 asset × Debug/Release có integrity khớp source, hai assembly chứa marker mới cùng composer `20261013.4`. Hash và kết quả ở `artifacts/admin-only-new-20261013/verification.json`. Script `scripts/Test-AdminRuntimeAssets.ps1` nhận marker/layout/module mới, hỗ trợ asset fingerprint hoặc query `v`; HTTP fixture GET-only đạt 4/4 nhánh hợp lệ/marker cũ/thiếu font.

### Nạp bản mới trên local

GET `https://localhost:7202/admin` ở lượt kiểm cuối trả **`admin-mockup-20261013.2`**, không có `homeLaunch`: process đang chạy chưa nạp assembly Razor mới. Đây là bằng chứng runtime còn bản trước; source/build mới đã có Tổng quan mới. Không tự dừng IDE/server hay chạy startup bootstrap trên database thật để thay bản trong phiên của người dùng.

Trong Visual Studio: **Stop Debugging → Build `TOOL-SERVER` → F5** profile `https`, rồi mở **`https://localhost:7202/admin`** và đăng nhập Admin. Trang mặc định phải là Tổng quan mới; không cần query composer. Marker mong đợi **`admin-only-new-20261013.1`**. Có thể kiểm chỉ đọc bằng `scripts/Test-AdminRuntimeAssets.ps1`; Ctrl+F5 browser riêng lẻ không thay assembly trong process server.

**Chưa nạp lại process local, smoke phiên Admin/database thật hoặc rollout production.** Không sửa database, migration, secret, DTO, giá AI hoặc thực hiện request provider có phí trong lượt này. Các phần V/U dưới đây là lịch sử trước thay toàn bộ trình bày N01–N16.

---

## Đợt sửa độ khớp ảnh mẫu — 2026-10-13

Người dùng đã cho phép triển khai sau lượt rà soát UI. Checkout `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, giữ các thay đổi có sẵn. Ảnh AI gốc là tham chiếu hình thức cho màn Tạo gói & mở bán; trạng thái sẵn sàng, giá và sức chứa trong ảnh là minh họa.

| Task | Nội dung | Nghiệm thu | Trạng thái |
|---|---|---|---|
| V01 | Font cố định, sidebar và icon | Font tiếng Việt self-host, sidebar 290 px ở desktop, tên Phát hành desktop, icon thống nhất | Hoàn tất source; đã xem ảnh |
| V02 | Bố cục form và preview | So ở 1672 × 941 với cùng dữ liệu; nút lưu nằm trong viewport, form và breadcrumb thẳng mép; 1920 px không lệch 50 px | Đạt browser; đã xem ảnh |
| V03 | Giá nhập có dấu phân nhóm | Hiển thị 70.000, preview đúng, server vẫn nhận 70000; chặn số âm/lẻ/quá giới hạn, giữ sửa/clone/retry | Đạt browser |
| V04 | Responsive và chức năng | 320/390/768/1366/1672/1920 px không tràn ngang, keyboard/focus, create/edit/clone, điều kiện mở bán | Đạt kiểm thử tự động |
| V05 | Xác minh build và local | Build/test thực chạy, asset mới cùng font; script nhận fingerprint ASP.NET; phân biệt process đang chạy với build mới | Build đạt; process local chưa nạp bản mới |

Phạm vi sửa lần này là trình bày Admin và định dạng số ở form hiện hành; không thay contract, schema, rate, credential hoặc nghiệp vụ thanh toán. Giữ nút kiểm tra lại nhóm dưới dạng icon cạnh trạng thái. Cần xem ảnh render thực, không dùng việc test theme đạt để kết luận UI giống mockup. Kết quả bên dưới là lịch sử đợt hợp nhất trước; chưa phải bằng chứng của đợt V01–V05.

Font Roboto Latin + tiếng Việt (khoảng 57 KB) đi kèm server và giấy phép/nguồn/checksum tại `TOOL-SERVER/wwwroot/admin/fonts`; không có request Google Fonts khi chạy. Giá hiển thị dấu phân nhóm lúc rời ô nhập, dùng chữ số thuần khi focus để giữ thao tác sửa; paste 70.000 hợp lệ, server luôn nhận số nguyên. Readiness/sức chứa vẫn lấy từ API, không hard-code trạng thái xanh theo mockup.

### Kiểm thử đợt sửa ảnh mẫu

| Phạm vi | Passed | Failed | Skipped | Bằng chứng |
|---|---:|---:|---:|---|
| Full .NET cuối | 1682 | 0 | 13 | `artifacts/admin-mockup-20261013/full-tests-final/admin-mockup-full-final.trx`; 1695 Total, 3 phút 54 giây |
| UI source sau điều chỉnh icon high contrast | 21 | 0 | 0 | `artifacts/admin-mockup-20261013/static-ui-final/static-ui-final.trx`; chạy lại subset, không cộng vào tổng .NET |
| Browser Admin/TikTok cuối | 66 | 0 | 0 | `artifacts/admin-mockup-20261013/browser-final.log` |
| Frontend React | 296 | 0 | 0 | `artifacts/admin-mockup-20261013/frontend-test.log`; 45 file |

Restore, npm ci/build, Release solution build và Debug server ở thư mục riêng đạt. Hai build cuối 0 warning/error MSBuild; Vite vẫn cảnh báo chunk >500 KB hiện có. Full .NET dùng TEMP/TMP riêng `D:\vmt-ui1013`; các test model/SQL opt-in Skipped không tính là nghiệm thu. Browser dùng source cùng font binary thực, API giả và không outbound; đã xem ảnh desktop/mobile. Ca bổ sung kiểm typing/sửa/paste giá, chặn âm/lẻ/scientific/quá giới hạn, nút lưu trong viewport tham chiếu, alignment ở 1920 px và native select bằng keyboard trong forced colors/reduced motion.

[Ảnh desktop cùng kích thước mẫu](artifacts/admin-mockup-20261013/screenshots/plan-composer-reference-1672.png), [mobile](artifacts/admin-mockup-20261013/screenshots/plan-composer-390.png), [Tổng quan](artifacts/admin-mockup-20261013/screenshots/overview-1440.png). Đây là ảnh render source với dữ liệu minh họa, không phải ảnh phiên Admin/database thật. Nút kiểm tra lại vẫn có ở cạnh trạng thái nhóm; tên/email/avatar phản ánh tài khoản thực, không sao chép tài khoản minh họa trong ảnh.

Lượt composer đầu phát hiện formatter chèn dấu nhóm trong khi gõ làm chuỗi 7.0000; đã chuyển format sang blur và dùng chữ số thuần khi focus, có regression typing/sửa/payload. Một assertion bảo toàn giá khi chuyển view đã cập nhật để kiểm cùng giá trị cả lúc focus và blur. Lượt .NET đầu 1679 Passed / 3 Failed / 13 Skipped vì assertion còn đòi input number và marker cũ; đã cập nhật assertion cho validation integer mới, giữ kiểm required/range và chạy lại full đạt như trên. Không phục hồi markup cũ để đạt test.

Script diagnostic đã hỗ trợ cả filename fingerprint của ASP.NET và query `v`; kiểm thêm composer và hai font local. Đã thực chạy trên HTTP fixture cô lập: hai dạng asset hợp lệ đạt, marker cũ/thiếu font bị từ chối; tất cả request là GET. Integrity manifest của Release và Debug riêng khớp source cho CSS/JS/form/font, assembly chứa marker `admin-mockup-20261013.2` và composer `20261013.4`.

### Nạp bản mới trên local

GET `/admin` trong lượt kiểm tra này vẫn trả `admin-unified-20261013.1`. Build Debug vào thư mục mặc định bị lỗi MSB3027/MSB3021 vì `TOOL-SHARED.Contracts.dll` bị Visual Studio Insiders PID 6340 và server PID 16096 giữ; 10 warning, 2 error. Đã build Debug thành công ở `artifacts/admin-mockup-20261013/debug-build` để xác minh source mà không dừng server/IDE của người dùng.

Để dùng source mới từ Visual Studio: Stop Debugging, Build `TOOL-SERVER`, rồi F5 profile `https`; mở `https://localhost:7202/admin?view=create-plan`. Marker mới phải là `admin-mockup-20261013.2`; Ctrl+F5 trên browser riêng lẻ không nạp lại assembly Razor hoặc registry font của process cũ. **Chưa nạp lại process local, smoke phiên Admin/database thật hoặc rollout production.** Không thay dữ liệu, migration hay gọi provider có phí trong lượt sửa này.

---

Người dùng yêu cầu lập plan rồi triển khai ngày 2026-10-13. Checkout `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit với các thay đổi có sẵn được giữ.

Mục tiêu: toàn bộ Web Admin dùng layout sáng thống nhất theo mockup. Không còn theme đổi khi chuyển mục, dialog gói cũ hoặc hai luồng form tạo/sửa. Dữ liệu, phân quyền, API desktop, license đã cấp và các chức năng tổ chức/AI/SePay/TikTok/release được giữ.

| Task | Nội dung | Nghiệm thu | Trạng thái |
|---|---|---|---|
| U01 | Rà soát UI cũ, mapping chức năng | Xác định CSS theme, dialog, handler và các trạng thái gói phải giữ | Đã rà soát |
| U02 | Một theme/layout Admin | Sidebar sáng, component thống nhất, bỏ CSS theme có điều kiện và rule nền trùng | Hoàn tất source |
| U03 | Một form tạo/sửa/nhân bản | Gỡ dialog/handler cũ, sửa gói không tạo bản mới, giữ code/quyền và trạng thái | Hoàn tất source |
| U04 | Lưu sửa gói cùng mapping | Transaction, kiểm mở bán, bảo vệ seat, stale revision, audit và replay | Hoàn tất source |
| U05 | Navigation, lỗi và phục hồi | Link trực tiếp, không mất input, timeout/retry/logout, focus và mobile | Hoàn tất source |
| U06 | Kiểm thử chức năng và hình thức | Tạo/sửa/clone, giữ theme khi chuyển mục, các module Admin và nhiều độ rộng | Hoàn tất tự động, đã xem ảnh |
| U07 | Build đúng bản local | Debug/Release có cùng UI, manifest khớp JS/CSS, không tự khởi động server/database | Hoàn tất build và đối chiếu asset |
| U08 | Cập nhật tài liệu và bằng chứng | Số Passed/Failed/Skipped thực chạy, ảnh mới, tách runtime/production | Hoàn tất; chưa smoke server thật/rollout |

Luồng sửa dùng cùng form composer, đọc plan và mapping hiện hành từ server. Giữ khả năng nháp, gói riêng tư Active để cấp tay, mở bán, và tạm ngừng bán. Giữ mã/quyền của plan nguồn, kiểm timestamp của cả plan và mapping trước ghi; không chuyển/xóa mapping còn seat Active/Reserved/Scheduled. Gói và mapping cùng commit/rollback, gửi lại thao tác đã lưu không tạo lại/sửa lần hai. Hash payload tạo cũ được giữ bằng các field tùy chọn không serialize khi null; replay tra plan ID trong audit kể cả API tương thích đã đổi mã.

Không xóa entity, migration, API tương thích hoặc dữ liệu vì chúng còn phục vụ license đã cấp/desktop/tích hợp. Chỉ gỡ phần trình bày và handler trùng; component còn chức năng được chuyển sang style thống nhất. Không chạy migration, tạo gói trên database thật, gọi provider hoặc deploy production trong lượt kiểm thử source.

## Kiểm thử trên working tree này

| Phạm vi | Passed | Failed | Skipped | Bằng chứng |
|---|---:|---:|---:|---|
| Full .NET cuối | 1682 | 0 | 13 | `artifacts/admin-unified-20261013/full-tests-final/Admin_DUNGDEV_2026-10-13_19_47_30_net10.0.trx`; 1695 Total, 5 phút 4 giây |
| Browser Admin + TikTok | 63 | 0 | 0 | `artifacts/admin-unified-20261013/browser-tests-final.log` |
| Frontend React | 296 | 0 | 0 | `npm test`, 45 file |

Restore, Release build solution, Debug server build, npm ci và npm run build đạt. MSBuild **0 Warning / 0 Error**; Vite giữ cảnh báo dashboard chunk >500 kB hiện có. Full .NET dùng collection tuần tự, TEMP/TMP riêng process ở đường dẫn ngắn trên D; model/SQL opt-in bị skip không tính là runtime/model đã nghiệm thu. Full có 31 ca composer (12 ca mới cho sửa, trạng thái, revision, seat, rollback, hash/replay), static UI và các module hiện hành.

Browser dùng Razor/CSS/JS hiện hành, intercept mọi HTTP bằng dữ liệu giả. Kiểm 320/390/768/1366/1920 px cho form, năm độ rộng cho shell, chuyển giữa các mục giữ sidebar trắng, validation tạo/sửa, quyền lợi clone, timeout/retry/logout, phiên hết hạn, SePay và TikTok. Đã xem ảnh desktop/mobile; dữ liệu giá, sức chứa và thống kê trong ảnh là fixture. [Tổng quan](artifacts/admin-unified-20261013/screenshots/overview-1920.png), [form gói](artifacts/admin-unified-20261013/screenshots/plan-composer-1920.png), [mobile](artifacts/admin-unified-20261013/screenshots/plan-composer-390.png).

Lượt browser đầu sau chuẩn hóa CSS có **57 Passed / 5 Failed / 0 Skipped** vì selector preview mất ưu tiên so với style nút disabled; đã sửa selector và giữ assertion opacity. Lượt full .NET đầu dừng sau phát hiện một test còn kiểm hàm dialog cũ; đã chuyển bài này sang kiểm validation composer và bổ sung browser kiểm thiếu giá/thời hạn không gửi POST, không phục hồi hàm cũ chỉ để đạt test. Một lượt browser khởi chạy khi helper cập nhật fixture chưa hoàn tất đã dừng, không tính là nghiệm thu. Lượt cuối đạt như bảng trên.

`git diff --check` đạt. Hash source/assembly, integrity manifest và tổng hợp kết quả ở `artifacts/admin-unified-20261013`. Tám asset (bốn file CSS/JS × Debug/Release) khớp source. Cả hai assembly chứa marker **admin-unified-20261013.1**, composer **20261013.3**, không chứa DOM dialog gói cũ.

## Phần đã gỡ và phần dùng chung

- Gỡ `planDialog/planForm` trong Razor, hàm `openPlanDialog/syncPlanSalesRequirements` và submit handler cũ trong `admin.js`.
- Gỡ toàn bộ selector và toggle theme `plan-composer-open`; root palette/sidebar dùng mặc định cho mọi mục.
- Loại declaration nền bị lặp/xung đột trong `admin.css`; component còn chức năng giữ selector/structure và dùng màu/radius mới. CSS composer chỉ chứa form/preview, không quản lý theme shell.
- Nút sửa chuyển sang `videoMakerPlanComposer.edit`; GET editor đọc bản server hiện hành, POST composer lưu cùng mapping trong transaction. Không POST qua form gói cũ.
- Giữ UI/API chức năng user/license, credential, policy/pricing/budget, seat/audit/retry, SePay, TikTok và release; các dialog còn chức năng dùng style chung. Không xóa entity, migration hoặc dữ liệu.

## Kiểm tra local

1. Nạp bản Debug mới của `TOOL-SERVER`, profile `https`.
2. Mở `https://localhost:7202/admin`, đăng nhập Global Admin, tải lại bằng Ctrl+F5 nếu browser đang giữ trang cũ.
3. Chuyển Tổng quan/Người dùng/Tổ chức/Gói/SePay/TikTok/Release: sidebar vẫn sáng.
4. Trong Gói sử dụng, Tạo và Chỉnh sửa mở cùng form; trạng thái Nháp/Riêng tư/Mở bán/Tạm ngừng nằm trong Thiết lập nâng cao khi sửa.
5. Có thể mở thẳng `/admin?view=create-plan` hoặc `/admin?view=edit-plan&planId=<guid>`.

Đối chiếu runtime bằng script read-only `scripts/Test-AdminRuntimeAssets.ps1`, marker kỳ vọng mặc định đã cập nhật. **Chưa khởi động/restart server/IDE, smoke thao tác với phiên Admin/database đích hoặc rollout production.** Không có SQL thay đổi dữ liệu, migration hoặc provider có phí trong lượt này. Build mới không tự thay assembly của process đang chạy; cần nạp lại server mới khi kiểm local.
