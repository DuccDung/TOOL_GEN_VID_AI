# Kế hoạch triển khai Admin setup 3 bước theo mockup

Admin hoàn tất kết nối AI, chi phí và sức chứa, sau đó tạo gói trong một luồng liên tục. Form phải bám ảnh đã duyệt; cấu hình SePay giữ tại mục **Thanh toán SePay** riêng. Tạo gói tiếp theo dùng lại tổ chức, nhóm và cấu hình AI đã chuẩn bị.

- Ngày lập: 2026-10-14.
- Nhánh rà soát: `main`.
- Commit nền: `f0651c918f28ca477f28875818148e9e86240b7b`, cùng working tree có thay đổi chưa commit.
- Trạng thái hiện hành: **đã triển khai source và hoàn tất kiểm thử trên working tree**. Runtime/database đích chưa xác minh; xem phần 14 và biên bản triển khai.
- Lượt lập kế hoạch chỉ tạo tài liệu. Lượt theo lệnh triển khai sau đó đã sửa source, thêm migration và chạy kiểm thử cô lập; không ghi database đích, gọi provider có phí hoặc thay cấu hình đang chạy.

## 1 Phạm vi đã chốt

1. Luồng có đúng ba bước: **Kết nối AI → Chi phí & khách hàng → Tạo gói & mở bán**.
2. SePay không thành bước thứ tư, không có form ngân hàng/key/webhook trong wizard. Bước 3 chỉ đọc điều kiện thanh toán và dẫn sang mục SePay khi cần.
3. UI là Razor và JavaScript của `TOOL-SERVER`, không chuyển sang React hoặc sửa dashboard desktop để triển khai wizard.
4. Phạm vi mặc định theo mockup: OpenAI Text, OpenAI Image, Fal/Veo 3.1 Standard hoặc Fast, 720p, Native Audio. TTS và các provider khác ở quản lý nâng cao, không tự bật bằng wizard này.
5. Không tự điền giá AI, đổi VND sang budget USD, mua thử, tạo content/ảnh/video hoặc giữ chỗ khách khi setup.
6. Không xóa tổ chức, nhóm, gói, credential version, policy hoặc lịch sử thanh toán đang tồn tại để thay bằng cấu trúc mới.

## 2 Ảnh làm chuẩn và quy tắc đối chiếu

Ảnh chuẩn cho **bước 2**: [Mockup Admin setup 3 bước](output/imagegen/mockup-admin-setup-3-buoc-sepay-rieng.png).

![Form chuẩn bước 2 với SePay ở menu riêng](output/imagegen/mockup-admin-setup-3-buoc-sepay-rieng.png)

- Kích thước ảnh: 1536 × 1024 pixel.
- SHA-256: `E3D84979288351742B78B44B11C8F98FDCA93B62C4F8F8D106209CFEAA7CD34B`.
- Ảnh là chuẩn hình thức, không phải bằng chứng cấu hình, credential hay readiness thật.
- Bước 1 và 3 dùng cùng shell, stepper, hệ form, cột tóm tắt và footer của ảnh; nội dung chi tiết được quy định bên dưới.
- Nhãn **Bản thiết kế** chỉ thuộc mockup, không đưa vào giao diện vận hành. Logo dùng asset thương hiệu hiện hành; không thay logo chỉ vì ảnh AI có biểu tượng khác.
- Không coi việc có đủ input và nút là đạt UI. Phải đối chiếu ảnh render thực của source với ảnh chuẩn ở cùng viewport.

### 2.1 Bố cục và kích thước

| Vùng | Yêu cầu ở 1536 × 1024 và zoom 100% |
|---|---|
| Sidebar | Khoảng 244 px, nền trắng, đường phân cách nhẹ; submenu gọn như ảnh |
| Topbar | Breadcrumb cao khoảng 54 px; không thêm banner hoặc tiêu đề lặp |
| Main | Padding ngang 28–30 px; header gồm tiêu đề, mô tả và selector tổ chức |
| Stepper | Ba bước trên một hàng; bước hoàn tất có dấu kiểm, bước hiện tại màu xanh |
| Nội dung | Hai cột khoảng 2:1, gap 16 px; cột trái form, cột phải tóm tắt |
| Card | Nền trắng, border nhẹ, radius 10–12 px, padding 20–24 px |
| Input | Cao 40–44 px theo ảnh, label luôn hiện, suffix đơn vị tách rõ |
| Footer | Quay lại bên trái; Lưu nháp và CTA chính bên phải; không che nội dung |
| Typography | Roboto tiếng Việt self-host hiện có; heading 28–30 px, body 15–16 px, help 13–14 px |
| Màu | Dùng token của theme sáng, chỉnh token cần thiết để khớp xanh/trắng trong ảnh; không tạo thêm theme riêng |

Ảnh dùng để xác định hình học tương đối. Khi kiểm browser, vị trí biên của sidebar/card/footer sai lệch mục tiêu không quá 8 CSS px ở viewport chuẩn; chiều cao control sai lệch không quá 4 px. Các sai lệch về thứ tự, số cột, nhóm field, menu hoặc vị trí SePay đều phải sửa, không tính trong dung sai.

Ở 1536 × 1024, bước 2 với fixture như ảnh phải nhìn thấy các card và footer. Ở 1366 × 768 được cuộn nội dung; footer không che field cuối. Dưới khoảng 1100 px chuyển cột tóm tắt xuống dưới; tablet/mobile dùng menu thu gọn, field một cột và stepper vẫn có nhãn. Kiểm 320, 390, 768, 1024, 1366, 1440, 1536 và 1920 px; thêm zoom 125%, 150%, 200%.

### 2.2 Navigation

Trong nhóm **Tổ chức & AI** chỉ còn hai entry chính:

- **Thiết lập mở bán**: wizard và trạng thái tiến độ.
- **Quản lý AI & tổ chức**: directory và các chức năng nâng cao đang có.

Các mục **Gói sử dụng**, **Thanh toán SePay**, **Tích hợp TikTok**, **Phát hành desktop**, Người dùng và Tổng quan giữ đường truy cập riêng. Bảng giá, phân bổ chi tiết, usage, audit và cách tính chi phí vẫn truy cập được bên trong quản lý nâng cao hoặc trợ giúp tại field, không bị xóa chức năng.

Deep link cũ phải có alias tới đúng view/tab. Header chung, tìm kiếm, login và trạng thái active của sidebar không được render trùng. Mọi chỉnh hình học shell phải kiểm hồi quy các trang Admin còn lại.

## 3 Source hiện có và khoảng trống cần xử lý

| Nội dung | Source cần tái sử dụng | Khoảng trống của wizard mới |
|---|---|---|
| Shell và navigation | [Index.cshtml](TOOL-SERVER/Pages/Admin/Index.cshtml), [admin.js](TOOL-SERVER/wwwroot/admin/admin.js), [admin.css](TOOL-SERVER/wwwroot/admin/admin.css) | Chưa có form ba bước làm ngay tại chỗ |
| Setup và quản lý AI | [admin-organizations.js](TOOL-SERVER/wwwroot/admin/admin-organizations.js) | Setup hiện dẫn sang trang khác, chưa kiểm SePay, gợi ý tạo gói trước nhóm |
| Tổ chức và credential | [OrganizationService.cs](TOOL-SERVER/Organizations/OrganizationService.cs) | Create/rotate chưa có receipt phục hồi dành cho toàn wizard |
| Model runtime | [ProviderRuntimeResolver.cs](TOOL-SERVER/Generation/ProviderRuntimeResolver.cs) | OpenAI Text/Image chọn model mặc định chung, chưa có preference riêng theo tổ chức |
| Pricing | [AiPricingAdminService.cs](TOOL-SERVER/Providers/AiPricingAdminService.cs) | Ghi từng rate riêng; chưa có batch năm rate và version guard cho form mới |
| Budget | [AiBudgetService.cs](TOOL-SERVER/Organizations/AiBudgetService.cs) | Cần kiểm ngân sách còn lại; `GetSnapshotAsync` hiện có thể tạo kỳ budget, chưa phù hợp GET chỉ đọc |
| Nhóm và sức chứa | [OrganizationProvisioningAdminService.cs](TOOL-SERVER/Organizations/OrganizationProvisioningAdminService.cs) | Các thao tác nhóm/link tách riêng; phải bảo vệ tài nguyên có khách |
| Readiness | [OrganizationProvisioningReadinessEvaluator.cs](TOOL-SERVER/Organizations/OrganizationProvisioningReadinessEvaluator.cs) | Hiện chấp nhận bất kỳ Text đủ rate và bất kỳ video policy; thiếu Image và đúng model thực sự được gọi |
| Tạo gói | [AdminLicensePlanComposerService.cs](TOOL-SERVER/Accounts/AdminLicensePlanComposerService.cs) | Composer đã có transaction/replay cho gói, chưa bao trùm kích hoạt nhóm mới |
| SePay | [SepayPaymentAdminService.cs](TOOL-SERVER/Payments/SepayPaymentAdminService.cs) | Wizard cần đọc status và cơ chế quay lại, không thêm form SePay |
| Mua và phân bổ | [LicensePaymentService.cs](TOOL-SERVER/Payments/LicensePaymentService.cs), [OrganizationSeatProvisioningService.cs](TOOL-SERVER/Organizations/OrganizationSeatProvisioningService.cs) | Phải hồi quy từ gói mới đến reserve, webhook, membership và renewal |
| Trạng thái license | [AccountManagementService.cs](TOOL-SERVER/Accounts/AccountManagementService.cs) | License yêu cầu plan Active; thao tác dừng bán phải giữ quyền khách đã mua |

Các API cũ chưa tạo một transaction chung cho toàn luồng. Không bọc các service đang tự `BeginTransaction` bằng một transaction bên ngoài rồi mặc định coi là atomic. Đây là task backend có điều phối và kiểm thử riêng.

## 4 Các quyết định logic cho phiên bản đầu

### 4.1 Model và phạm vi ảnh hưởng

- Text/Image lấy **đúng model mà runtime sẽ dùng**, ưu tiên mặc định hiện hành của từng modality. Bước 1 hiển thị tên để Admin biết cấu hình đang áp dụng.
- Không dựng selector OpenAI riêng theo tổ chức nếu backend chưa có dữ liệu/routing tương ứng. Thay model mặc định OpenAI qua quản lý nâng cao phải ghi rõ **áp dụng toàn hệ thống** và xác nhận tác động.
- Fal Standard/Fast chọn theo policy `LongForm` của tổ chức. Không dùng default Kling để phục hồi khi Fal thiếu điều kiện.
- Draft ghi model ID/code và version đã đọc. Model mặc định đổi trước lưu/mở bán phải trả conflict và yêu cầu tải lại; không đổi nhãn âm thầm nhưng vẫn gọi model khác.
- Project cũ giữ provider/model/policy snapshot. Wizard không chuyển hàng loạt project cũ sang Veo.

### 4.2 Phạm vi tiền và quota

- Đơn giá AI: chung theo model, USD. Budget tổ chức: USD theo tháng. Hạn mức khách: USD theo tháng. Giá bán gói: VND nguyên dương.
- Không tự quy đổi giữa giá VND và quota USD. Không mặc định cấp AI không giới hạn vì Admin chưa nhập quota.
- Hạn mức khách `null` chỉ được lưu khi Admin chọn rõ **Dùng chung ngân sách tổ chức**. UI giải thích ý nghĩa và giữ lựa chọn này khi mở lại.
- Hạn mức khách `0` được lưu nháp để cấu hình sau, nhưng chặn mở bán **gói AI qua wizard này** và giải thích khách sẽ bị khóa AI. Không tự sửa dữ liệu gói legacy có quota 0.
- Budget không được thấp hơn `ActualCost + ReservedCost`. Không reset kỳ budget hoặc ledger khi sửa form.
- Quota mặc định trên nhóm/gói không tự cập nhật membership đã Active. Với mapping đã có, chặn thay quota trong wizard khi còn đơn Pending chưa hết hạn, Paid chưa Fulfilled hoặc assignment Scheduled, vì fulfillment hiện đọc quota từ mapping. Hiện lý do và cho tạo gói mới với quota khác. Guard phải nằm trong core ghi mapping để quản lý nâng cao cũng không vượt được; khôi phục mapping bị mất cho đơn đã trả tiền là đường sửa chữa riêng, không tự đoán quota lịch sử.

### 4.3 Tài nguyên mới và tài nguyên đang sử dụng

- Luồng đơn giản tạo hoặc dùng lại **một tổ chức và một nhóm**. Mã tài nguyên gắn draft, server sinh; retry không tạo tổ chức/nhóm trùng.
- Tổ chức mới có Owner là Admin tạo. Budget ban đầu 0; chưa auto nhận khách. Group mới giữ `Inactive`, link giữ `IsAutoAssignmentEnabled=false` trong giai đoạn chuẩn bị.
- Nhóm/link mới chỉ bật trong thao tác mở bán được xác nhận ở bước 3. Lưu nháp không mở bán hoặc chiếm seat.
- Với tổ chức/nhóm đã Active, không tắt auto allocation để dựng trạng thái nháp. UI báo cấu hình nào đang phục vụ khách và yêu cầu xác nhận trước khi sửa budget/rate/policy/sức chứa.
- Không lấy số thành viên làm số seat thương mại. Seat tính theo counter và assignment hiện hành; capacity không được thấp hơn Active + Reserved.
- Một tổ chức đang bật tự phân bổ trong nhóm khác không được tự chuyển. Nhóm nhiều tổ chức hoặc policy hỗn hợp đưa sang nâng cao; wizard không hứa khách dùng cùng model nếu các tổ chức nhận khách có model khác.

### 4.4 SePay riêng và quyền sử dụng đã cấp

- Bước 1, bước 2 và lưu nháp bước 3 không đòi SePay bật.
- Bước 3 đọc trạng thái receiver/Enabled/QR-prefix-expiry hiện hành từ server. Chưa sẵn sàng thì khóa **Tạo và mở bán**, giữ **Lưu nháp** và hiện **Mở Thanh toán SePay**.
- Trước khi chuyển sang SePay, lưu draft an toàn và context quay lại. Khi quay lại phải đọc status mới, không dùng cache cũ hoặc tự bật Enabled.
- Trạng thái cấu hình SePay đạt không phải xác nhận đã thu tiền thật. Việc webhook hiện chưa xác thực nguồn gửi vẫn thuộc runbook SePay riêng, không âm thầm thay trong task wizard.
- Nút **Dừng mở bán** giữ `IsActive=true`, đặt `IsPublic=false` và xác nhận “Đã dừng mở bán”. Danh sách dùng nhãn **Riêng tư / không mở bán** cho tổ hợp này; không suy ra gói đã từng bán chỉ từ hai boolean. Không dùng `IsActive=false` cho nút dừng bán vì sẽ làm heartbeat từ chối license cũ.
- Trạng thái legacy Inactive/public vẫn đọc được, nhưng phải phân biệt với dừng bán an toàn. Không tự bật lại dữ liệu legacy chưa rõ lý do bị vô hiệu hóa.

## 5 Form chi tiết của từng bước

### 5.1 Bước 1 Kết nối AI

**Cột trái:** selector tổ chức đã có hoặc form tên tổ chức mới; card OpenAI; card Fal/Veo. **Cột phải:** AI đang chọn và giải thích thao tác tiếp theo. Footer cùng vị trí như ảnh.

| Field hoặc thao tác | Hành vi |
|---|---|
| Tổ chức | Chỉ liệt kê scope Admin được quản lý; thay context có kiểm tra thay đổi chưa lưu |
| Tên tổ chức mới | Bắt buộc, theo giới hạn service hiện hành; tạo sau thao tác lưu rõ ràng |
| Model viết nội dung | Hiển thị đúng default runtime; thay mặc định chung nằm trong nâng cao |
| Model tạo ảnh | Hiển thị đúng default runtime; thiếu/bị tắt thì có hành động xử lý rõ |
| Model video | Chọn Standard/Fast từ catalog được server cho phép; không tự chọn provider khác |
| Credential OpenAI/Fal | Input password; hiển thị/ẩn; không điền key cũ; đã cấu hình chỉ hiện hint/version |
| Kiểm tra và lưu kết nối | Chỉ test theo allowlist bằng cơ chế hiện có, test đạt rồi mã hóa và Active |
| Provider/model đang tắt | Hiện điều kiện thiếu; Global Admin bật có xác nhận phạm vi chung; không tự bật khi GET |

Không lưu key trong localStorage/sessionStorage, draft JSON, query string, log, telemetry, HTML attribute hay ảnh kiểm thử. Xóa field sau lưu, đổi tổ chức và logout. Key chỉ trong bộ nhớ của form đang nhập và request HTTPS tới server.

“Kết nối đã kiểm tra” chỉ nói credential test đạt. Bước 1 có thể hoàn tất trước pricing/budget; không gắn nhãn “Sẵn sàng bán” ở đây. Kiểm tra kết nối không gọi tạo content/ảnh/video có phí.

### 5.2 Bước 2 Chi phí và khách hàng

**Bố cục phải khớp ảnh chuẩn:** card Đơn giá AI, card Ngân sách & sức chứa, disclosure Thiết lập nâng cao ở cột trái; AI đã chọn, Hệ thống tự liên kết và callout tiền tệ ở cột phải.

| Nhóm field | UI chuẩn | Mapping server |
|---|---|---|
| Text đầu vào | USD / triệu token | `InputToken`, unit `MillionTokens`, đúng Text model |
| Text đầu ra | USD / triệu token | `OutputToken`, unit `MillionTokens`, đúng Text model |
| Image đầu vào | USD / triệu token | `InputToken`, unit `MillionTokens`, đúng Image model |
| Image đầu ra | USD / triệu token | `OutputToken`, unit `MillionTokens`, đúng Image model |
| Video | USD / giây; chip 720p · Native Audio | `VideoSecond`, unit `Second`, metadata đúng endpoint Standard/Fast |
| Budget tổ chức | Suffix USD | MonthlyBudgetLimit và kỳ budget hiện hành |
| Hạn mức khách | Suffix USD, lựa chọn dùng chung | DefaultMemberMonthlyBudgetLimit của gói sẽ tạo |
| Số khách tối đa | Suffix khách | SeatCapacity của tổ chức trong nhóm |
| Nhóm nhận khách | Read-only “Tự tạo theo tổ chức” hoặc tên nhóm đã có | Mapping do server xác minh |

Rate hiện có phải điền từ server. Nếu rate gốc là Token/1KTokens, chuyển giá hiển thị sang triệu token bằng decimal chính xác; không đổi rate trong SQL khi chỉ mở form. Giữ RateId/unit/version ban đầu để nhận biết field không đổi và không tạo rate mới vì khác đơn vị hiển thị.

- Tự tạo metadata variant ở server; UI không nhập JSON, endpoint ID, UsageType hoặc currency tùy ý.
- Lưu đủ bộ rate cần thay trong một transaction. Giữ rate snapshot của request cũ; replay không tạo thêm rate hoặc kết thúc rate hai lần.
- Nếu sửa giá chung đã có, dialog xác nhận nêu model, giá trước/sau, đơn vị và phạm vi toàn hệ thống. Nếu không sửa, tái sử dụng rate; không tạo version mới cho giá không đổi.
- USD không nhận NaN, Infinity, exponent, âm hoặc 0 cho rate; rate tuân giới hạn và precision của SQL hiện hành. Hiển thị parse error ngay field, không làm tròn giá nhỏ thành 0.
- Budget/quota theo range `0..100.000.000`, decimal đúng precision; capacity là số nguyên `1..100.000`. Giá trị blank và 0 phải phân biệt.
- Nội bộ giữ số tiền bằng decimal/text chuẩn hóa, không dùng phép tính floating-point JavaScript làm nguồn sự thật. Định dạng VND/USD khi blur, giá trị gửi được server parse và validate lại.
- Lưu budget/policy/link mới phải atomic trong phần thay đổi thuộc tổ chức. Giá global và cấu hình tổ chức là hai phạm vi ghi khác nhau, có receipt riêng và báo kết quả từng phần nếu phạm vi thứ hai lỗi.
- Nút **Lưu và tiếp tục** đưa sang bước 3 sau khi server xác nhận bước 2 hoàn tất. **Lưu nháp** chấp nhận dữ liệu chưa đủ, không apply rate/budget dở dang.

### 5.3 Bước 3 Tạo gói và mở bán

**Cột trái:** form gói ngắn, thiết lập nâng cao đóng mặc định, checklist điều kiện. **Cột phải:** preview gói và tóm tắt AI/tổ chức/sức chứa. Footer: Quay lại, Lưu nháp, Tạo và mở bán.

| Field | Validation |
|---|---|
| Tên gói | Theo service hiện hành, 2–200 ký tự |
| Giá bán | VND nguyên dương, không vượt `1.000.000.000.000`; hiển thị dạng `70.000`, không seed số ví dụ vào dữ liệu thật |
| Thời hạn | Số ngày nguyên `1..3650` khi mở bán |
| Số thiết bị | Số nguyên `1..1000` |
| Tổ chức/nhóm | Kế thừa context wizard, hiện tên và sức chứa; đổi qua thao tác rõ ràng |
| Quota khách | Kế thừa bước 2; tùy chọn chỉnh cho gói này trong nâng cao, không sửa quota gói khác |
| Mô tả/quyền lợi | Tái sử dụng validation của composer; không tự quảng cáo số video vô hạn |
| Phiên đồng thời/offline/thứ tự | Nâng cao, giữ giới hạn service và quyền gói nguồn khi clone |

Checklist hiển thị độc lập: AI đúng model và rate; ngân sách còn đủ; nhóm/tổ chức có thể nhận khách; SePay sẵn sàng; thông tin gói hợp lệ. Mỗi lỗi có nhãn rõ, target step/field hoặc nút sang SePay. HTTP lỗi đọc status không được hiển thị như “chưa cấu hình”.

SePay chỉ có status và đường dẫn ở bước này. Không hiện form thêm ngân hàng, đổi receiver hoặc bật thanh toán. Quay về wizard giữ chính xác draft/step/context, reload status, đặt focus tới checklist liên quan.

Mở bán thành công chỉ sau response server có plan ID, mapping và operation receipt. Mất phản hồi phải hiển thị “Chưa xác nhận kết quả” và đối soát cùng operation ID; không báo thất bại chắc chắn hoặc tạo gói khác.

### 5.4 Tạo thêm gói và quản lý nâng cao

- Tổ chức/nhóm đủ cấu hình: vào thẳng bước 3 với bước 1–2 đã được server kiểm lại; không nhập key/rate lần nữa.
- Clone giữ entitlements nguồn; chỉ đổi các field Admin nhập. Tạo mới và clone cùng form; không dựng thêm dialog gói cạnh tranh với composer.
- Edit giữ PlanId, code, version plan/mapping; không chuyển/gỡ nhóm còn assignment Active/Reserved/Scheduled.
- Đổi tổ chức/model trong draft làm đánh giá bước 2–3 cũ hết hiệu lực, nhưng không sửa project hoặc gói đã lưu.
- Dừng bán an toàn và vô hiệu hóa gói là hai thao tác có ý nghĩa khác nhau. Vô hiệu hóa gói có khách phải nêu tác động license và cần xác nhận rõ ràng.

## 6 Trạng thái và khôi phục

### 6.1 Trạng thái draft

| State | Ý nghĩa | Thao tác tiếp theo |
|---|---|---|
| Draft | Có bản nháp an toàn, field có thể chưa đủ | Sửa hoặc lưu nháp |
| ConnectionsSaved | Tổ chức xác định, credential/model đã xác nhận | Hoàn thành chi phí và sức chứa |
| ConfigurationSaved | Các thao tác cấu hình đã có receipt, chưa mở bán | Tạo gói hoặc sang SePay riêng |
| PublicationBlocked | Cấu hình đã lưu nhưng điều kiện hiện hành chưa đủ | Sửa đúng blocker, kiểm lại |
| Published | Gói và mapping đã commit | Xem gói hoặc tạo thêm gói |
| Cancelled | Dừng draft, không phát thao tác mới | Đọc lịch sử hoặc tạo draft mới |

State là tiến độ lưu, không thay thế readiness hiện hành. Loading/saving/unknown/error là trạng thái thao tác riêng, không được dùng như bằng chứng config đã lưu.

### 6.2 Quy tắc frontend

- Quản lý theo DraftId, OrganizationId, actor, revision, step và request ID; bỏ response cũ sau đổi context/logout.
- Draft an toàn lưu server; browser chỉ giữ ID/step và dữ liệu không nhạy cảm cần phục hồi request. Không đưa credential vào bản nháp tự động.
- Lưu nháp không yêu cầu đủ field bắt buộc của submit. Có thông báo thành công/lỗi rõ; không dùng thông báo “đã lưu” ngay khi bấm nút.
- Quay lại giữ field. Đi bước chưa đủ prerequisite thì giải thích blocker; cho xem/sửa nháp, chặn apply/publish không hợp lệ.
- Disable thao tác ghi lặp khi đang chờ. Timeout không cấp operation ID mới. Hai tab sửa cùng draft phải conflict theo version.
- Đổi model/tổ chức khi còn thao tác ghi chưa xác định kết quả phải đối soát trước. Không bỏ context để gửi tiếp nơi khác.
- Khi đi SePay, lưu nháp trước; lưu lỗi thì ở lại form. Khi quay lại, chỉ phục hồi nếu actor/session/context khớp.
- Logout xóa key trong bộ nhớ, pending UI và ID gắn phiên; thao tác server đã commit vẫn được đối soát ở phiên được phép tiếp theo.
- Tự động restore không tạo rate, rotate key, mở bán hoặc gọi provider. Reload/GET chỉ đọc.

## 7 Thiết kế backend và contract

### 7.1 Thành phần dự kiến

Thêm module điều phối setup trên server, tái sử dụng validation và core service hiện hành. Tên dưới đây là **dự kiến**, chưa có source:

- `AdminSetupContracts.cs` trong `TOOL-SHARED.Contracts/Accounts`.
- `AdminSetupController`, `AdminSetupCoordinator`, `AdminSetupReadinessService`.
- `AdminSetupDraft` và `AdminSetupOperation`, cấu hình EF trong context phù hợp.
- `admin-setup.js`, `admin-setup.css` và view/template trong Razor Admin.

Không copy nghiệp vụ pricing, budget, credential, composer sang controller hoặc JavaScript. API setup chỉ nhận cấu hình cần thiết, không nhận SQL, output URL, host provider hay metadata JSON tùy ý.

### 7.2 API dự kiến

| API dưới `/api/admin/setup` | Nội dung |
|---|---|
| GET bootstrap | Catalog/default thật, tổ chức có quyền, draft đang có, giới hạn field, status SePay an toàn |
| POST drafts | Tạo draft với operation ID; replay trả cùng draft |
| GET drafts/{id} | Đọc draft, tiến độ và receipt; không trả key/ciphertext |
| PUT drafts/{id} | Lưu dữ liệu nháp không nhạy cảm, có expected version |
| POST drafts/{id}/connections | Tạo/dùng lại tổ chức, áp kết nối; key transient, test ngoài transaction |
| POST drafts/{id}/pricing | Batch giá global sau xác nhận phạm vi; receipt và version guard |
| POST drafts/{id}/configuration | Budget, policy, capacity và nhóm/link chuẩn bị |
| GET drafts/{id}/readiness | Đánh giá lại đúng tài nguyên; không tạo request AI hoặc mutation |
| POST drafts/{id}/publish | Kiểm lại và commit mở bán; cùng operation ID để replay |
| GET drafts/{id}/operations/{operationId} | Đối soát khi mất phản hồi |

Endpoint mutate yêu cầu Global Admin, session/device hợp lệ, ownership draft và quyền tổ chức theo service hiện hành. Admin không thuộc tổ chức vẫn không được bypass Owner/OrganizationAdmin khi rotate key/chỉnh policy. Tổ chức mới tạo Owner theo cơ chế hiện có.

DTO response có trạng thái từng capability, model/rate/credential version an toàn, RemainingBudget, capacity, blocker code/field/target và operation receipt. DTO không có plaintext key, encrypted payload, header auth hoặc raw provider response. Public contract phải được làm trước server/frontend/test và giữ tương thích API desktop.

### 7.3 Idempotency và concurrency

- Unique operation theo actor + draft + operation ID + command; payload thay hoặc actor khác bị conflict.
- Payload hash chỉ chứa dữ liệu an toàn. Command credential cần fingerprint bí mật có khóa server hoặc cơ chế tương đương để phát hiện payload khác; không persist key trong receipt/hash input JSON.
- Receipt gồm tài nguyên đã ghi, version, kết quả và trạng thái, không chứa secret. Receipt phải commit cùng dữ liệu nghiệp vụ tương ứng.
- Create organization/pool dùng ID hoặc code ổn định theo draft; không dựa vào timestamp để chống trùng.
- Giá/model/budget/pool/plan có expected version hoặc fingerprint. Version đổi thì trả conflict trước ghi; không coi query GET trước đó là khóa.
- Reuse rate nếu nội dung không đổi; nhiều field rate ghi atomically. Không replay việc rotate key hoặc add rate nếu đã có receipt commit.
- Không giữ transaction SQL qua credential test HTTP. Test rồi recheck version/quyền trước transaction ghi.
- Không coi một operation đang thực hiện là lỗi chắc chắn. Đối soát receipt; action mới chỉ sau kết quả xác định.
- Operation có `Pending`, `Running`, `Succeeded`, `Failed` và lease hết hạn cho phần HTTP. Restart phải đọc receipt/tài nguyên đã commit trước khi kết luận có thể thử lại; không để draft busy vĩnh viễn. Nếu credential chưa commit và input key không còn, yêu cầu nhập lại qua form, không khôi phục key từ storage.

### 7.4 Transaction theo phạm vi

1. **Connections:** mỗi thao tác provider có receipt riêng; OpenAI thành công/Fal lỗi thì hiện kết quả từng provider. Không rollback bằng cách thu hồi key cũ hoặc xóa version đã commit.
2. **Pricing:** các rate đang sửa trong một SQL transaction cùng audit/receipt, không lưu nửa bộ năm rate.
3. **Configuration:** cập nhật budget/kỳ budget, policy và nhóm/link cần cùng transaction; với nhiều DbContext phải dùng chung connection/transaction hoặc core ghi đã tách phù hợp.
4. **Publish:** nhóm/link mới được bật, plan, mapping, audit và receipt trong cùng transaction `Serializable`. Nhóm đã Active giữ trạng thái; lỗi tạo plan/mapping không làm nhóm mới mở nhận khách.

Tách core composer nhận transaction hiện hữu hoặc đơn vị ghi chung; không gọi wrapper `CreateAsync` đang tự mở transaction như thể nó tự tham gia transaction của coordinator. Mọi context cần đọc/ghi trong unit phải tham gia đúng transaction; không giả định cùng connection string là đủ. Credential HTTP đã hoàn tất trước publish; publish không gọi provider.

Pricing có thể đã commit trước configuration/publish. Nếu bước sau lỗi, UI nói rõ giá nào đã lưu và tổ chức/gói nào chưa lưu. Không hứa rollback toàn bộ setup nhiều bước hoặc tự đảo giá global đã có request sử dụng.

### 7.5 Migration dự kiến

Cần durable draft/operation để khôi phục sau reload/restart và không tạo tài nguyên trùng. Tạo migration SQL idempotent mới, dự kiến hậu tố `AdminSetupWizard`; chọn version mới sau khi kiểm lại nhánh triển khai, không sửa các migration SePay 4.1.10/4.1.12/4.1.13 hoặc lịch sử đã có.

- Draft: actor, org/pool ID, safe payload, model/config snapshot, step/state, result plan ID, created/updated time và rowversion.
- Operation: draft/actor/command/operation ID, hash an toàn, status, lease expiry, result/reference, timestamps và unique key.
- Draft/operation chỉ server quản lý trong schema quản trị phù hợp; không mở quyền desktop. Không chứa provider key hoặc bản sao ciphertext credential.
- Không backfill draft giả cho tổ chức/gói cũ; dữ liệu cũ được chọn và dùng lại qua GET.
- Kiểm CHECK/FK/index/unique/rowversion, apply hai lần, rollback DDL khi lỗi và least privilege trên SQL Server private.
- SQL không seed giá, key, Enabled SePay, model/provider mới Active hoặc pool nhận khách thật.

## 8 Kiểm tra sẵn sàng trước mở bán

Thêm profile kiểm tra phục vụ wizard OpenAI + Fal/Veo. Không thay blanket readiness của tất cả pool legacy thành Fal-only hoặc yêu cầu TTS/ASR ở workflow Native Audio.

| Điều kiện | Kiểm tra bắt buộc |
|---|---|
| Scope | Draft actor có quyền, organization đúng và Active |
| Text | Chính xác default runtime/model enabled, capability phù hợp, credential Active, InputToken + OutputToken hợp lệ |
| Image | Chính xác Image model runtime, credential Active, hai rate hợp lệ |
| Video | Policy LongForm đúng Fal Standard/Fast, Native Audio, 720p, capability image-to-video và variant đúng |
| Rate | Đúng model, IsActive, thời gian hiệu lực, UnitPrice > 0, đơn vị/currency/metadata hợp lệ |
| Credential | Phiên bản Active hiện hành, metadata test đã có; không dùng Retiring làm credential mới |
| Budget | HardLimit và RemainingBudget hiện hành; đủ preflight estimate theo calculator hiện có cho capability cần dùng |
| Quota khách | Giá trị > 0 hoặc chọn dùng chung rõ ràng; không dùng 0 để mở bán gói AI |
| Nhóm và seat | Nhóm/link đúng, không xung đột nhóm khác, có capacity thương mại còn lại; không đòi đã có gói trước để tạo gói đầu tiên |
| SePay | Chỉ publish kiểm cấu hình SQL receiver/Enabled và runtime QR/prefix/expiry hợp lệ |
| Gói | Giá/thời hạn/device/entitlement và version plan/mapping hợp lệ |

Preflight estimate chỉ tính từ rate thật bằng calculator source, dùng input mẫu đã định nghĩa rõ, không reserve budget hoặc gửi AI. Đánh giá đủ ngân sách mẫu không bảo đảm đủ cho mọi video độ dài tùy ý; generation vẫn quote/reserve độc lập theo snapshot. Nếu không tính được estimate đáng tin cậy thì trả blocker cấu hình, không đoán giá.

Readiness trả ba nhóm riêng: **AI**, **Phân bổ**, **Mở bán**. Bước 1–2 không đỏ chỉ vì SePay tắt. Recheck sau thay model/rate/budget/capacity và trong publish. Guard publish cần đọc dữ liệu ở phạm vi transaction để không dùng readiness đã stale.

GET bootstrap/readiness cần phương thức budget chỉ đọc, không gọi trực tiếp `GetSnapshotAsync` rồi tạo kỳ mới. Nếu kỳ hiện hành chưa tồn tại, tính snapshot đọc theo chính sách kỳ tháng của source và đánh dấu chưa persist; kỳ được tạo trong apply/reservation hoặc job được phép ghi. Không bootstrap catalog, tạo draft, cập nhật `IsReady` hoặc log thao tác nghiệp vụ thành công chỉ vì mở màn hình.

## 9 Danh sách task triển khai

Tất cả task có trạng thái **Chưa bắt đầu**. Chỉ đánh dấu hoàn tất khi có source và bằng chứng nghiệm thu tương ứng, không kế thừa số test trong plan cũ.

| ID | Task | Phụ thuộc | Kết quả phải có |
|---|---|---|---|
| P01 | Chốt baseline source, ảnh, scope model và API | Không | Danh sách đường gọi, snapshot phiên bản và delta chính xác |
| P02 | Đặc tả UI ba bước và fixture visual | P01 | Layout/field/state từng bước bám ảnh, không thêm SePay step |
| P03 | DTO và mã lỗi setup | P01 | Contract an toàn, scope/version/operation, test serialization |
| P04 | Schema draft và operation | P03 | Migration mới idempotent, EF và quyền server |
| P05 | Service draft, quyền và receipt | P04 | Save/read/replay/conflict/context, không chứa secret |
| P06 | Apply kết nối và tổ chức | P05 | Create không trùng, test key trước ghi, replay không rotate lặp |
| P07 | Batch pricing global | P05 | Năm field đúng model/unit, giữ snapshot, transaction và confirm ảnh hưởng |
| P08 | Apply budget, policy và tự liên kết nhóm | P06, P07 | Nhóm mới Inactive, counters đúng, existing resources được bảo vệ |
| P09 | Readiness đúng capability | P03, P08 | Text/Image/Fal/budget/seat/SePay được phân biệt và recheck |
| P10 | Publish trong transaction chung | P08, P09 | Plan/mapping/activation/audit/receipt atomic; replay cùng kết quả |
| P11 | Dừng bán và tiếp tục gói cũ | P10 | Dừng bán không làm mất license; edit/clone bảo toàn lịch sử |
| P12 | Shell, navigation và module wizard | P02, P03 | Hai submenu, SePay riêng, alias và form không nhảy trang |
| P13 | UI bước 1 | P06, P12 | Credential field an toàn, model rõ phạm vi, trạng thái test thật |
| P14 | UI bước 2 theo ảnh | P07, P08, P12 | Hai card + cột summary, năm rate và bốn field capacity/budget |
| P15 | UI bước 3 và quay lại từ SePay | P09, P10, P12 | Preview/checklist, giữ draft, recheck, không có form bank |
| P16 | Resume, timeout, nhiều tab và logout | P05, P13–P15 | Request/context guard và đối soát receipt, không submit lặp |
| P17 | Quản lý nâng cao và tạo gói tiếp theo | P11, P16 | Giữ chức năng cũ; cấu hình đã có vào bước 3 trực tiếp |
| P18 | Test server nghiệp vụ và bảo mật | P06–P11 | Negative/replay/rate/budget/capacity/transaction có regression |
| P19 | Test SQL Server và migration | P04, P08, P10 | Concurrency, rollback, receipt và quyền trên private SQL |
| P20 | Browser test UI, visual và accessibility | P13–P17 | Screenshot/source thật với API giả, đối chiếu ảnh và lỗi thật |
| P21 | Hồi quy mua gói và cấp tổ chức | P10, P11, P18 | Reserve/webhook/member/renewal/expiry không trùng hoặc vượt chỗ |
| P22 | Build, full suite và asset verification | P18–P21 | Báo Passed/Failed/Skipped thực tế, asset nạp đúng |
| P23 | Tài liệu vận hành và smoke được phép | P22 | Runbook, rollback và trạng thái môi trường được ghi đúng |

### 9.1 Chi tiết task UI

**P02 và P12:** dựng frame chuẩn trước; dùng font self-host và component chung. Đi trực tiếp wizard hoặc quay lại từ navigation đều chọn đúng step/context. Không giữ thêm menu cũ bên cạnh menu mới. Không xóa handlers/module còn dùng cho nâng cao hoặc deep link.

**P13:** kiểm các trạng thái chưa có tổ chức, tổ chức đã cấu hình, một provider lỗi, provider/model disabled, credential đang Retiring và mất quyền. Sau test thành công xóa key khỏi DOM/state nhạy cảm. Field credential cũ không hiển thị chuỗi masked như thể đó là key thật để gửi lại.

**P14:** đo bounding box/sidebar/header/card/input/footer ở viewport chuẩn; nhập USD/VND/localized decimal không mất giá trị. Fixture có model như ảnh nhưng không key hoặc giá thật. Thử rate đã có, đổi unit hiển thị, chỉ sửa một field, confirm giá chung, dismiss confirm và conflict trong lúc nhập.

**P15:** checkout chỉ là read trạng thái SePay, không POST vào API bật bank/Enabled. Đi SePay rồi quay về, browser Back và deep link đều phục hồi draft đúng. Thử SePay tắt, receiver chưa chọn, status timeout và receiver thay đổi trước publish.

**P16:** mỗi thao tác có abort/timeout và request epoch; abort phía browser không có nghĩa server rollback. Chặn bấm lặp, giải phóng busy ở mọi kết quả, giữ operation chưa xác định để đối soát. Không tự retry command có secret hoặc phát operation mới sau 5xx.

**P17:** nâng cao giữ provider/rate/history/pool/member/usage/audit/retry. Tạo gói sau setup không yêu cầu chạy lại step 1–2, nhưng server vẫn recheck hiện trạng. Đổi dữ liệu trên tab quản lý làm wizard biết cần kiểm lại.

### 9.2 Chi tiết task logic

**P05–P08:** tách validation, apply và receipt; authorization thực hiện trước ghi hoặc outbound credential test. Các phần dùng service hiện hành phải giữ error code và rule Owner cuối cùng, rotation, rate snapshot, budget floor và capacity. Test transaction với failure injection sau từng điểm ghi.

**P09:** kiểm default đúng model, cả Image, đúng LongForm Fal và remaining budget; bổ sung đường budget chỉ đọc. Một model Text khác có đủ rate không làm default đang thiếu rate được báo Ready. Rate endpoint Standard không làm Fast Ready. Readiness không được tạo mutation/AI request, kể cả khi sang kỳ tháng mới.

**P10:** không dựa vào pool có gói trước để mở bán gói đầu tiên; activation mới nằm cùng transaction với plan/mapping. Hai Admin cùng publish cùng draft hoặc cập nhật cùng pool phải có kết quả xác định theo version/operation. Paid order đang chờ không bị xóa vì setup lỗi.

**P11:** regression khách có license còn hạn khi dừng bán; kiểm heartbeat và current license, không chỉ kiểm IsPublic ở database. Clone và edit không sửa code/entitlements của gói nguồn hoặc tự chuyển assignment. Chặn đổi quota mapping có đơn chờ/Scheduled và phân biệt lỗi này với sửa chữa mapping bị mất.

## 10 Ma trận kiểm thử cần đạt

### 10.1 UI và hành vi

- Ba bước, đúng thứ tự; sidebar chỉ hai submenu; SePay riêng trên mọi breakpoint.
- Step 2 đúng ảnh ở 1536 × 1024; ảnh các step 1/3 dùng cùng form system.
- Card/ngữ cảnh/quota/đơn vị/model đúng; không lộ JSON, key, URL provider hoặc kỹ thuật không cần cho quyết định.
- Required validation khi submit; nháp chưa đủ vẫn lưu được; error summary và lỗi tại field cùng hoạt động.
- Bàn phím: Tab order, focus sau đổi step, selector, disclosure, dialog confirm, return từ SePay và error focus. Screen reader có label/step/current/status; status không chỉ dựa màu.
- Browser reload/back, hai tab, logout/login actor khác, đổi tổ chức, API chậm và response đến muộn không apply sai context.
- CTA có loading/disabled, không gửi lặp; success chỉ theo receipt.
- Text dài, nhiều blocker, nhóm đã có khách và zoom 200% không che field/nút; tôn trọng reduced motion.

### 10.2 Server và transaction

- Non-Admin, thiếu/revoked session/device, draft actor khác, tổ chức ngoài membership hoặc role không đủ bị chặn trước ghi/test key.
- Replay create organization/pool/rate/key/publish không nhân bản tài nguyên hoặc version; khác payload/actor bị conflict.
- Rate missing/wrong unit/currency/metadata/effective date, model disabled/default changed, credential missing/Retiring đều có blocker đúng.
- Budget 0, remaining 0, dưới committed cost, quota khách 0/null chưa xác nhận đều xử lý đúng; không reserve AI ở setup.
- GET bootstrap/readiness không tạo kỳ budget hoặc mutation khi DB chưa có kỳ tháng hiện hành; test mốc rollover bằng fake time.
- Capacity 0/âm/fractional/vượt range, giảm dưới Active+Reserved, tổ chức đang ở nhóm auto khác bị chặn.
- Rate batch lỗi giữa chừng rollback toàn batch; configuration lỗi rollback unit; publish lỗi rollback plan/mapping/activation/receipt tương ứng.
- Timeout sau commit, process restart và execution-strategy retry trả kết quả cũ; không tự rotate/add-rate lần nữa.
- SQL nhiều context thực sự join transaction; test failure đọc/ghi giữa context. SQLite/in-memory không thay thế test SQL locking/rowversion.
- Không có credential/key trong draft, SQL receipt, audit, exception, HTTP logs hoặc browser storage/screenshot.

### 10.3 Mua gói và compatibility

- Gói nháp không hiện offers, không mua/cấp tay được; gói mở bán có mapping đúng và còn chỗ.
- Tạo đơn giữ seat trước QR, duplicate/concurrent webhook không cấp license/member/seat hai lần.
- Hết capacity không tạo đơn mới; đơn hết hạn trả chỗ; paid nhưng chưa cấp được giữ trạng thái chờ/retry.
- Người mới được cấp đúng org/Member/quota; manual member và Viewer cũ không bị ghi đè.
- Gia hạn cùng gói không dùng thêm seat; mua gói khác giữ hành vi Scheduled hiện hành, không tự đổi thành upgrade tức thì.
- Dừng bán không từ chối heartbeat của khách còn hạn. Chính sách project/model cũ và ledger/rate snapshot vẫn đọc được.
- Quota mapping có đơn Pending/Paid/Scheduled không bị đổi giữa checkout và fulfillment; đường repair mapping được kiểm riêng, không bị nhầm với chỉnh quota của wizard.
- Desktop DTO/bridge, license offers và popup hiện hành không đổi ngoài phạm vi contract thực sự cần. Wizard không biến popup nâng cấp desktop thành checkout mới.

### 10.4 Bộ test và lệnh dự kiến

Thêm test module setup có fixture riêng: server `AdminSetup*Tests`, browser `admin-setup.browser.test.cjs`, SQL `AdminSetupMigration/ConcurrencyTests`. Mở rộng các suite pricing, composer, organization provisioning, authentication/license và payment hiện có bằng regression có ý nghĩa. Không hard-code tổng test và không nới assertion chỉ để pass.

Sau khi source đã triển khai, chạy theo [KIEM_THU_VA_NGHIEM_THU.md](KIEM_THU_VA_NGHIEM_THU.md):

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Frontend hồi quy theo root AGENTS, dù wizard nằm ở Razor/JS:

```powershell
Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
npm test
```

Browser chạy source Razor/CSS/JS, font thật và API fake chặn network; giữ bộ Admin shell, allocation, composer, SePay và TikTok. Lệnh test browser mới chỉ được thêm khi file được triển khai. Nếu full suite dùng collection tuần tự/TEMP ngắn theo runbook thì ghi đúng cấu hình và lý do. Model/SQL opt-in Skipped không phải model/SQL đã đạt.

## 11 Bằng chứng UI và điều kiện hoàn tất

Mỗi vòng nghiệm thu lưu ảnh step 1/2/3, fixture dữ liệu, viewport/zoom, hash source/assets, manifest selector và kết quả browser. Thư mục dự kiến `artifacts/admin-setup-3steps/<run-id>`; không dùng artifact cũ thay source mới.

Checklist hoàn tất:

- [x] Form bước 2 khớp ảnh chuẩn theo tiêu chí layout; đã xem ảnh thực và sửa sai lệch.
- [x] Step 1/3 dùng cùng frame; responsive, bàn phím và focus đạt.
- [x] SePay riêng; đi/return giữ draft và recheck status; không tự bật nhận tiền.
- [x] Model hiển thị khớp đường chọn runtime trong source; rate/Image/ngân sách có kiểm thử. Provider thực chưa smoke.
- [x] Draft/receipt, retry, nhiều tab, transaction và security matrix đạt.
- [x] Nhóm tự tạo không nhận khách trước publish; gói đầu tiên và gói tiếp theo đều hoạt động.
- [x] Dừng bán giữ license cũ; mua/renewal/seat lifecycle không hồi quy.
- [x] Restore/build/test/browser đạt; báo riêng Passed/Failed/Skipped bằng kết quả mới.
- [ ] Static assets và Razor đang phục vụ được xác minh đúng version sau deploy được phép.
- [x] Tài liệu contract/migration/runbook/current context cập nhật đúng trạng thái thực tế.

UI fixture đạt chỉ xác nhận source/layout/hành vi giả lập. Readiness cấu hình đạt chỉ xác nhận điều kiện được kiểm; không tuyên bố đã nghiệm thu provider trả phí, webhook nhận tiền hoặc rollout production.

## 12 Thứ tự thực hiện và vận hành

1. P01–P04 chốt scope, UI spec, contract và migration.
2. P05–P11 làm core backend, readiness và publish an toàn; kiểm server hẹp trước.
3. P12–P17 hoàn thiện UI theo ảnh và nối API, giữ quản lý nâng cao.
4. P18–P21 xác minh transaction, concurrency, visual và luồng mua bằng môi trường cô lập.
5. P22 chạy bộ kiểm tra chuẩn; P23 cập nhật tài liệu và chuẩn bị smoke/rollback.

Không deploy hoặc chạy migration thật trong task lập kế hoạch. Khi triển khai môi trường sau này, phải xác định instance/database, backup/restore và quyền tác động theo [VAN_HANH_VA_PHAT_HANH.md](VAN_HANH_VA_PHAT_HANH.md). Không restart server/IDE người dùng để giải phóng lock.

Rollout giữ dữ liệu cũ, có đường mở quản lý nâng cao và aliases. Nếu wizard gặp lỗi, chặn thao tác mới, giữ draft/receipt và các gói đang phục vụ; không đảo rate/credential hoặc xóa nhóm/gói để rollback. Binary rollback phải tương thích migration mới, catalog và dữ liệu đã ghi.

Nghiệm thu trên môi trường thật chia riêng: đăng nhập/Admin/readiness; tạo nháp và gói test; offers/assignment bằng fixture được phép; provider paid smoke và tiền thật chỉ sau phê duyệt riêng. Không nâng bằng chứng của checkout, database hoặc binary khác thành kết quả của lượt triển khai này.

## 13 Trạng thái tại thời điểm lập kế hoạch

- UI/mockup đã có để làm chuẩn; wizard ba bước trong source **chưa triển khai**.
- P01–P23: **Chưa bắt đầu triển khai**; việc đọc source trong lượt lập kế hoạch không được tính là hoàn tất task implementation.
- Build/test/browser/migration/runtime smoke: **không chạy trong lượt này**.
- Database, credential, rate, SePay và server đang chạy: **chưa xác minh trong lượt này**.
- Chỉ tài liệu kế hoạch mới được tạo; các thay đổi chưa commit có sẵn được giữ nguyên.

## 14 Tiến độ triển khai source ngày 2026-10-14

Theo yêu cầu triển khai sau khi lập kế hoạch, P01–P22 đã có source/kiểm thử hoặc hồ sơ kiểm tra tương ứng. P23 đã cập nhật tài liệu/migration/runbook; phần áp DB và smoke runtime/production còn **chưa xác minh**, cần môi trường và quyền tác động xác định theo runbook.

| Task | Bằng chứng hiện hành |
|---|---|
| P01–P02 | Giữ working tree có sẵn, rà source; mockup và screenshot render source/JSON bounds ở `output/admin-setup-verification` |
| P03–P05 | Shared DTO, migration 4.1.14, entity/EF, draft revision và operation receipt |
| P06 | Org creator Owner, key probe có lease ngoài transaction, writer dùng chung, Active → Retiring, transient key |
| P07–P09 | Batch rate global, đơn vị/fingerprint, ngân sách/policy/pool, GET readonly và profile đúng model |
| P10–P11 | Serializable activation/plan/mapping/audit/receipt; private stop-sale và guard quota chung |
| P12–P17 | Menu hai mục, wizard ba bước theo ảnh, SePay riêng, resume/unknown/retry/logout, Browser Back và tạo gói tiếp theo |
| P18–P21 | Test server/SQL riêng, rollback/replay/concurrency, source browser/API fake, heartbeat và offers/reserve profile |
| P22 | Restore/Release build, full .NET, frontend install/build/test và Admin/TikTok browser; kết quả cuối ghi trong biên bản |
| P23 | Biên bản, context/nghiệp vụ/kiến trúc/runbook/kiểm thử cập nhật; chưa áp DB đích hoặc rollout |

Các chi tiết triển khai khác mô tả dự kiến: tên draft kết thúc khi chỉ tạo gói nháp là `PlanDraftSaved`; command database thành công có receipt cùng transaction, failed database command rollback cả receipt, connection failure có receipt Failed để giải phóng lease. Key không được hash vào draft/receipt; replay so sánh với credential version đã mã hóa bên trong server. Profile khả năng dùng AI được giữ theo flag của gói và kiểm ở offers/reserve để không áp Fal/Image bắt buộc lên mọi gói legacy.

Xem [TRIEN_KHAI_ADMIN_SETUP_3_BUOC.md](TRIEN_KHAI_ADMIN_SETUP_3_BUOC.md) cho source, ảnh, kiểm thử mới, migration và giới hạn môi trường. Phần 13 phía trên là trạng thái lịch sử tại lượt lập kế hoạch.
