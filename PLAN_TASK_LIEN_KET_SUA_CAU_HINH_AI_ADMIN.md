# Plan task liên kết xử lý cấu hình AI trong Admin

Admin bấm vào thao tác cạnh trạng thái thiếu cấu hình để mở đúng form cần sửa, đúng tổ chức, provider và model. Sau khi lưu, giao diện đọc lại kết quả từ server và cho phép quay về vị trí ban đầu để xử lý điều kiện còn thiếu.

**Trạng thái:** T01–T12 đã triển khai trong source và đạt kiểm thử tự động ngày 2026-10-15. Chưa smoke phiên Admin thật hoặc publish hosting. Xem [biên bản triển khai](TRIEN_KHAI_LIEN_KET_SUA_CAU_HINH_AI_ADMIN.md).

**Ngày rà soát:** 2026-10-15. Nhánh `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, cùng working tree chưa commit. Các thay đổi có sẵn trong repository phải được giữ nguyên.

## 1 Mục tiêu và phạm vi

Ưu tiên màn **Tổ chức → AI**, đúng các card OpenAI và Fal trong ảnh người dùng. Cùng cơ chế xử lý được áp dụng cho cảnh báo ở tổng quan tổ chức, wizard Thiết lập mở bán và kiểm tra phân bổ khách hàng.

Kết quả cần đạt:

- Thiếu key: mở popup nhập key đúng nhà cung cấp của tổ chức đang xem.
- Chưa chọn provider video: chuyển đến Policy Video dài và chỉ rõ lựa chọn cần hoàn tất.
- Thiếu đơn giá: mở đúng provider, model và loại chi phí cần cấu hình.
- Ngân sách bị khóa hoặc đã dùng hết: chuyển đến ngân sách của đúng tổ chức.
- Provider/model đang tắt: chuyển đến đúng mục trong catalog để người có quyền xử lý.
- Khi bắt đầu từ nhóm phân bổ, giữ nhóm, tổ chức và bước đang xử lý để quay lại.
- SePay tiếp tục ở trang riêng; liên kết thanh toán mở trang SePay.

Phạm vi dự kiến là Razor, JavaScript, CSS và kiểm thử Admin. Sử dụng các API, form lưu và evaluator hiện có; chưa có nhu cầu thay database hoặc DTO public cho các trường hợp trên.

## 2 Phần đã có trong source và phần cần bổ sung

| Vị trí hiện hành | Đã có | Cần bổ sung |
|---|---|---|
| `renderReadinessAction` trong [admin-organizations.js](TOOL-SERVER/wwwroot/admin/admin-organizations.js) | Nút xử lý budget, provider tắt, policy, giá và credential | Ngữ cảnh tổ chức/model/loại chi phí; xử lý model tắt; tránh lặp thao tác credential |
| `navigateToReadinessSetup` trong cùng file | Mở tab ngân sách, provider, policy hoặc bảng giá | Đi đến đúng model thay vì nút thêm rate đầu tiên của provider; focus và highlight; xử lý đích không còn tồn tại |
| Card provider ở `renderProviders` | Trạng thái, cảnh báo và nút credential | Thao tác chọn policy cho provider chưa được chọn; liên kết xử lý từng điều kiện còn thiếu |
| [admin-allocation-setup.js](TOOL-SERVER/wwwroot/admin/admin-allocation-setup.js) | Các nút Thiết lập và nút quay lại phân bổ | Giữ đúng tổ chức đang kiểm tra, nhóm và bước; trở lại đọc trạng thái mới |
| [admin-setup-wizard.js](TOOL-SERVER/wwwroot/admin/admin-setup-wizard.js) | Checks có mã, bước; điều hướng nâng cao và SePay | Thao tác cụ thể cho check chưa đạt; bảo toàn form khi đi sửa; kiểm soát thao tác đang chờ kết quả |
| `OrganizationAiReadinessResponse` trong [OrganizationContracts.cs](TOOL-SHARED.Contracts/Organizations/OrganizationContracts.cs) | ProviderCode, ModelCode, MissingUsageTypes, BlockingReasons | Dùng dữ liệu này để xác định đích; đối chiếu catalog lấy model ID, không đoán model mặc định khác |
| [OrganizationAdminUiTests.cs](TOOL-TESTS/Organizations/OrganizationAdminUiTests.cs) | Một số kiểm tra markup/handler hiện hành | Bổ sung kiểm thử hành vi trên trình duyệt, đặc biệt sai tổ chức và sai model |

Các liên kết có sẵn là nền để mở rộng. Trạng thái và quyền cuối cùng vẫn do server xác minh.

## 3 Quy tắc UI

### 3.1 Bố cục theo card hiện tại

Giữ card trắng, viền mảnh, badge trạng thái và các dòng thông tin như ảnh. Thao tác dùng chữ xanh có mũi tên `→`, đặt ngay dưới thông báo liên quan. Giữ nút chính để nhập/lưu credential.

- Nút nội bộ dùng `button type="button"`, có nhãn chứa tên provider/model khi cần phân biệt.
- Với thiếu credential, dùng lại nút cấu hình ở chân card; bỏ thao tác trùng chức năng trong cùng card.
- Provider có nhiều điều kiện thiếu được hiển thị các hành động tương ứng. Cùng một điều kiện dùng chung một đích thì chỉ có một hành động.
- Highlight đích đến bằng viền/nền theo màu của form hiện tại, tự bỏ sau khoảng 2–3 giây. Không dùng chuyển động khi hệ thống yêu cầu giảm chuyển động.
- Focus đến input/select/nút cần thao tác sau khi nội dung đã tải. Không chỉ cuộn trang.
- Nút có vùng bấm đủ lớn, focus rõ; trên màn hình hẹp có thể xuống dòng và không che nội dung.
- Loading, không có quyền, dữ liệu hết hiệu lực và lỗi mạng có thông báo rõ tại thao tác đang xử lý.

### 3.2 Phân biệt lỗi chặn và provider chưa được chọn

**Không được chọn** là trạng thái lựa chọn, không tự chuyển thành lỗi. Ví dụ tổ chức đang dùng provider video khác thì card Fal có thể đưa ra hành động **Chọn Fal/Veo cho Video dài →** để admin chủ động đổi.

Bấm hành động này mở phần Policy Video dài, có thể chọn sẵn model Fal hợp lệ nếu xác định được duy nhất. Admin vẫn phải bấm **Áp dụng**. Chưa có model hợp lệ thì chuyển đến cấu hình model và giải thích điều kiện còn thiếu.

### 3.3 Mẫu hiển thị cần nghiệm thu

| Tình huống | Thông báo/thao tác |
|---|---|
| OpenAI chưa có key Active | Chưa kết nối OpenAI; **Cấu hình key OpenAI** |
| Fal chưa có key Active | Chưa kết nối Fal; **Cấu hình key Fal** |
| Fal chưa được chọn cho Video dài | Giữ nhãn Không được chọn; **Chọn Fal/Veo cho Video dài →** |
| Thiếu OutputToken của model nội dung | Nêu tên model và loại chi phí thiếu; **Cấu hình đơn giá →** |
| Ngân sách bằng 0 | Ngân sách đang khóa AI; **Thiết lập ngân sách →** |
| Tổ chức đã dùng hết ngân sách kỳ hiện tại | Ngân sách khả dụng không đủ; **Xem ngân sách và sử dụng →** |

Thông báo ngân sách bằng 0 và hết ngân sách được phân biệt. Không tự tăng ngân sách từ liên kết điều hướng.

## 4 Bảng điều hướng

| Điều kiện | Hành động | Đích và dữ liệu cần mang theo |
|---|---|---|
| `credential_missing` hoặc credential không Active | Cấu hình key theo tên provider | Popup credential; tổ chức và provider cụ thể; focus ô key |
| `video_policy_missing` / `video_policy_invalid` | Chọn provider cho Video dài | Form policy scope LongForm của đúng tổ chức; model hiện tại hoặc lựa chọn hợp lệ được chỉ rõ |
| Provider chưa được chọn | Chọn provider này cho Video dài | Cùng form policy; đây là hành động tùy chọn, không phải lỗi chặn |
| `pricing_not_configured` | Cấu hình đơn giá | Bảng giá AI → mở provider → đúng model → chỉ rõ MissingUsageTypes; cho phép mở popup rate với loại chi phí đã chọn |
| `provider_disabled` | Mở cấu hình provider | Catalog đúng provider; admin chủ động bật bằng thao tác hiện có |
| `model_disabled` hoặc model mặc định thiếu | Mở cấu hình model | Catalog đúng model nếu còn tồn tại; nếu thiếu thì hiển thị danh sách lựa chọn hợp lệ |
| `budget_disabled` | Thiết lập ngân sách | Tab usage/ngân sách của đúng tổ chức; focus hạn mức |
| Ngân sách khả dụng thiếu | Xem ngân sách và sử dụng | Cùng tab, làm rõ hạn mức, đã dùng và đang giữ; không tự điều chỉnh |
| Nhóm có tổ chức chưa qua kiểm tra | Kiểm tra sẵn sàng | Đúng nhóm, tổ chức và bước kiểm tra; dùng thao tác server hiện hành khi admin bấm kiểm tra |
| Tổ chức hết chỗ | Điều chỉnh sức chứa | Form cấu hình tổ chức trong đúng nhóm; focus sức chứa; giữ kiểm tra số chỗ đang dùng/giữ |
| Gói chưa gắn nhóm | Gắn gói vào nhóm | Form mapping đúng gói; admin chọn nhóm hợp lệ nếu chưa có nhóm |
| Thanh toán chưa sẵn sàng | Mở cấu hình SePay | Trang SePay riêng; giữ điểm quay lại; không bật nhận tiền khi chỉ mở trang |
| Mã trạng thái chưa hỗ trợ / đích đã mất | Tải lại trạng thái | Đọc lại dữ liệu và giải thích; không chuyển sang tổ chức/model khác |

Đơn giá và trạng thái catalog có phạm vi toàn hệ thống. Tại đích cần thể hiện phạm vi này và giữ validation/xác nhận hiện có khi admin lưu.

## 5 Logic điều hướng và bảo toàn ngữ cảnh

### 5.1 Một đường xử lý dùng chung

Mở rộng điều hướng trong `admin-organizations.js` và cung cấp một hàm công khai qua `videoMakerOrganizationAdmin` để card provider, tổng quan, wizard và phân bổ gọi cùng cách. `renderReadinessAction` tạo hành động từ mã điều kiện và dữ liệu readiness; không phân tích câu cảnh báo tiếng Việt để tìm đích.

Ngữ cảnh nội bộ gồm:

- Đích: credential, policy, pricing, provider, model, budget, capacity, mapping hoặc payments.
- `organizationId` bắt buộc cho thao tác cấu hình tổ chức; provider/model/usage type khi đích cần.
- `poolId`, `planId`, `draftId` khi nguồn có các đối tượng này.
- Điểm trở về: trang, tab, bước và phần tử đã bấm.
- Actor và phiên điều hướng để bỏ phản hồi cũ khi đổi tài khoản, tổ chức hoặc đích.

Chỉ truyền ID và thông tin cấu hình an toàn. Context không chứa API key, token, payload mã hóa hoặc giá trị ô password.

### 5.2 Thứ tự khi bấm liên kết

1. Xác định tổ chức và provider/model từ hành động vừa bấm; kiểm tra quyền và trạng thái busy/pending.
2. Giữ điểm quay lại và các trường cấu hình không nhạy cảm cần bảo toàn.
3. Mở đúng trang/tab, tải dữ liệu đích bằng API hiện có.
4. Kiểm tra lại tổ chức, actor và phiên điều hướng sau mỗi lần chờ dữ liệu.
5. Xác nhận model thuộc provider và còn trong catalog; mở phần thu gọn nếu cần.
6. Focus và highlight đúng trường. Chỉ điền sẵn lựa chọn không nhạy cảm đã xác định được; không tự lưu.

Bấm liên kết xử lý chỉ mở giao diện và thực hiện các lượt đọc cần thiết. Không tự gửi key test, thay policy, ghi giá/budget, giữ seat, tạo đơn thanh toán hoặc gọi AI.

### 5.3 Form và điểm quay lại

- Ở chi tiết tổ chức, có nút **Quay lại phần cần hoàn tất** khi đã đi sang bảng giá hoặc trang khác.
- Ở phân bổ, nút quay lại đưa về đúng nhóm, tổ chức và bước kiểm tra; giữ lựa chọn gói và hạn mức chưa lưu.
- Ở wizard, các trường không nhạy cảm chưa lưu được giữ trong RAM theo actor/draft. Không dùng đường xử lý `data-advanced` hiện tự lưu nháp cho liên kết chỉ điều hướng mới.
- Thao tác đang chờ kết quả hoặc có receipt chưa đối soát phải được xử lý trước khi rời nguồn bằng liên kết mới; không bỏ pending operation hoặc tạo operation thay thế.
- Key chỉ tồn tại theo vòng đời form credential hiện hành. Không đưa key vào state dùng để quay lại; đóng popup, đổi tổ chức hoặc logout phải xóa key.
- Khi trở lại wizard sau sửa cấu hình bên ngoài, đọc lại draft/readiness và kiểm tra revision/fingerprint. Nếu có xung đột thì hiển thị cách tải lại, không ghi đè cấu hình mới bằng form cũ.
- Nút quay lại không trở thành cơ chế sửa mới browser history của toàn bộ Admin. Back/forward hiện có và link SePay phải tiếp tục hoạt động.

### 5.4 Sau khi admin lưu

Giữ các submit handler hiện có cho credential, policy, rate, budget và cấu hình phân bổ. Chỉ báo đã lưu khi API xác nhận.

Sau thành công, vô hiệu hóa cache bị ảnh hưởng và đọc lại các phần liên quan: provider, policy, readiness tổ chức, bảng giá hoặc chi tiết nhóm. Giao diện chỉ hiển thị sẵn sàng khi dữ liệu server mới xác nhận đủ điều kiện.

Việc tổ chức đủ AI không tự chứng minh nhóm nhận khách đã sẵn sàng. Nếu nhóm còn cần bước kiểm tra hoặc bật phân bổ, hiển thị **Kiểm tra sẵn sàng →** / **Thiết lập phân bổ →** theo dữ liệu mới. Chỉ gửi yêu cầu kiểm tra qua thao tác hiện hành khi admin chủ động bấm; server phải xác minh trước khi ghi trạng thái sẵn sàng.

Lưu thất bại giữ tổ chức/đích và các trường không nhạy cảm; không hiển thị hoàn tất. Credential vẫn tuân thủ quy tắc nhập một lần và xóa key hiện hành.

### 5.5 Quyền và lỗi

- Catalog/rate chỉ cho Global Admin; credential/policy theo quyền tổ chức; budget theo quyền billing hiện hành.
- Với người thiếu quyền, hiển thị lý do và hướng dẫn người có quyền xử lý, không đưa vào form ghi hoặc tự mở rộng quyền.
- Server tiếp tục kiểm quyền ở mỗi API. 401 xóa context thuộc phiên và quay về login; 403 có thông báo đúng hành động.
- Double click chỉ tạo một lượt điều hướng/popup. A → B trong lúc A đang tải thì phản hồi A không được mở form của A trên B.
- Provider/model/organization không còn tồn tại thì dừng ở trạng thái có thể tải lại; không thay bằng phần tử đầu tiên của danh sách.

## 6 Danh sách task triển khai

Tất cả task dưới đây đã triển khai theo yêu cầu tiếp theo của người dùng. Các task nghiệm thu đã đạt ở mức source và fixture tự động; trạng thái runtime/hosting vẫn tách riêng trong biên bản triển khai.

| Task | Mức ưu tiên | Công việc và file dự kiến | Phụ thuộc | Điều kiện hoàn tất |
|---|---|---|---|---|
| T01 | P0 | Chốt danh sách điều kiện, nhãn nút và đích từ readiness/catalog; `admin-organizations.js` | Không | Mỗi điều kiện ở bảng mục 4 có đích rõ; phân biệt chưa chọn và lỗi chặn |
| T02 | P0 | Bổ sung bộ điều hướng và context dùng chung; export qua `videoMakerOrganizationAdmin` | T01 | Đúng tổ chức/provider/model; có guard async, quyền và đích không tồn tại |
| T03 | P0 | Cập nhật card OpenAI/Fal và cảnh báo tổng quan tổ chức; `admin-organizations.js`, `admin.css` hoặc `admin-modules.css` | T02 | Mọi điều kiện thiếu có thao tác phù hợp; không lặp nút credential |
| T04 | P0 | Nối credential/policy/budget; ràng buộc tổ chức khi mở và khi submit | T02 | Popup đúng provider, policy đúng LongForm, budget đúng tổ chức; chỉ ghi khi admin lưu |
| T05 | P0 | Nối bảng giá tới đúng model và MissingUsageTypes; bổ sung marker model ổn định trong renderer | T02 | Không focus nhầm model đầu tiên; không tự điền giá; giữ validation đơn vị/biến thể |
| T06 | P0 | Focus, highlight, thông báo, keyboard và responsive; sửa Razor nếu cần vùng thông báo dùng chung | T03–T05 | Dùng được bằng Tab/Enter/Escape; không mất focus hoặc tràn ngang |
| T07 | P1 | Dùng bộ điều hướng từ kiểm tra phân bổ; `admin-allocation-setup.js`, `admin-organizations.js` | T02, T04–T05 | Sửa đúng tổ chức được chọn trong nhóm; quay lại đúng nhóm và bước |
| T08 | P1 | Dùng bộ điều hướng từ check chưa đạt trong wizard; `admin-setup-wizard.js`, CSS wizard khi cần | T02, T04–T05 | Giữ trường form không nhạy cảm; không tự lưu khi chỉ mở đích; pending/revision được bảo vệ |
| T09 | P1 | Liên kết sức chứa, mapping gói và SePay; dùng form và callbacks hiện có | T07–T08 | Đúng nhóm/gói; SePay ở trang riêng; không tự cấp chỗ hoặc tạo thanh toán |
| T10 | P0 | Đồng bộ sau lưu và điểm quay lại giữa các module | T04–T09 | Trạng thái dựa trên server mới; lỗi mạng không báo sẵn sàng; không tự ghi cờ nhóm |
| T11 | P0 | Bổ sung kiểm thử hành vi trong các browser suite Admin; cập nhật static test nếu thay markup làm mất assertion có ý nghĩa | T03–T10 | Các ca trong mục 7 đạt trên fixture cô lập, không có outbound provider |
| T12 | P0 | Build/test theo AGENTS, chụp UI từ source mới và lập biên bản nghiệm thu | T11 | Có bằng chứng source/build/test/ảnh; phân biệt rõ với smoke/rollout môi trường thật |

P0 xử lý card và logic cốt lõi; P1 hoàn thiện các điểm gọi liên quan. Đợt triển khai hoàn tất khi cả P0 và P1 đạt, không coi riêng card provider là toàn bộ yêu cầu đã xong.

## 7 Ma trận kiểm thử hành vi

| Ca | Kết quả cần kiểm chứng |
|---|---|
| OpenAI/Fal chưa có key | Bấm mở đúng popup, orgId và provider; chỉ focus input, không gọi credential PUT |
| Có hai tổ chức A/B với key khác nhau | Không dùng trạng thái/key của A cho B; save đúng ID đã mở form |
| Đổi tổ chức trong lúc đang tải | Phản hồi cũ không mở popup hoặc focus vào tổ chức mới; không ghi nhầm tổ chức |
| Fal chưa được chọn | Có thao tác policy; badge vẫn là Không được chọn; policy chưa đổi trước khi bấm Áp dụng |
| Đang dùng provider khác | Không tự chuyển policy sang Fal, không tạo thêm lỗi chặn cho provider chưa chọn |
| Thiếu rate ở model thứ hai/thứ ba | Mở đúng model và usage type; model khác cùng provider không được nhận focus/prefill |
| Provider/model tắt hoặc bị xóa | Mở đúng mục còn tồn tại hoặc thông báo tải lại; không tự bật hoặc chọn model khác |
| Budget 0 và budget đã hết | Hai thông báo khác nhau; đều dẫn đúng tổ chức; không tự tăng ngân sách |
| Không có quyền / 401 / 403 | Đúng thông báo, context được dọn khi mất phiên; không có request ghi trái quyền |
| Double click và thao tác đang chạy | Một popup/lượt điều hướng; không mất pending operation hoặc gửi lặp mutation |
| GET lỗi mạng / timeout | Có retry; không báo ready hoặc mở form từ dữ liệu không xác định |
| Key test/lưu thất bại | Form không báo thành công; key không vào URL/context/storage/log; key Active cũ không bị thay |
| Lưu key/policy/rate/budget thành công | Đọc lại đúng trạng thái liên quan và bỏ cảnh báo chỉ khi server xác nhận |
| Wizard có form chưa lưu | Quay lại giữ trường không nhạy cảm; không có POST/PUT do chỉ điều hướng; xử lý revision conflict |
| Nhóm có nhiều tổ chức | Đi sửa tổ chức đang kiểm tra, giữ đúng poolId và bước khi quay lại |
| Gói chưa gắn nhóm / tổ chức hết chỗ | Mở đúng form; chỉ thay đổi sau lưu; kiểm tra seat đang dùng/giữ vẫn có hiệu lực |
| Quay lại từ SePay | Trang SePay riêng; giữ nhóm/draft; không tự bật thanh toán hay tạo đơn |
| Secret và XSS | Escape tên org/provider/model; context chỉ có dữ liệu an toàn; secret không xuất hiện trong ảnh/test output |
| Keyboard và responsive | Kiểm ở 390, 768, 1440, 1920 px và zoom 125%/150%; focus đúng, không tràn, reduced motion hợp lệ |
| Click nút xử lý bất kỳ | Mutation chỉ xuất hiện sau hành động lưu/áp dụng/kiểm tra rõ ràng; không phát sinh request AI có phí |

Ưu tiên mở rộng [admin-shell.browser.test.cjs](TOOL-TESTS/Admin/admin-shell.browser.test.cjs), [admin-allocation-setup.browser.test.cjs](TOOL-TESTS/Admin/admin-allocation-setup.browser.test.cjs), [admin-setup-wizard.browser.test.cjs](TOOL-TESTS/Admin/admin-setup-wizard.browser.test.cjs). Test sử dụng Razor/CSS/JS thật với API giả lập; kiểm đích, focus, payload, số lần ghi và ngữ cảnh, không chỉ kiểm có chuỗi nhãn nút.

## 8 Nghiệm thu và bàn giao

Sau triển khai source, chạy restore/build/test .NET và npm ci/build/test theo [AGENTS.md](AGENTS.md), cùng các browser suite có thay đổi. Báo riêng Passed, Failed và Skipped; bài model hoặc SQL opt-in bị skip không tính là đã đạt.

Ảnh nghiệm thu cần có: card OpenAI thiếu key, Fal chưa được chọn, đích policy được focus, bảng giá đúng model, budget được focus, popup credential trên mobile và quay lại kiểm tra phân bổ. Ảnh fixture chỉ chứng minh hình thức/hành vi từ source.

Biên bản bàn giao ghi commit nền, hash source của working tree, lệnh và kết quả thực chạy. Chỉ kết luận smoke môi trường thật sau khi kiểm chính runtime đích; bản source mới không tự cập nhật hosting đang chạy.

Khi được yêu cầu triển khai môi trường đích, xác minh Razor/JS/CSS cùng bản, cache busting và các đường điều hướng bằng thao tác không phát sinh chi phí. Chưa có yêu cầu chạy SQL, sửa key thật, tạo thanh toán hoặc publish trong task lập plan này.

**Tiêu chí hoàn thành:** Từ mỗi điều kiện thiếu đã hỗ trợ, admin đi đến đúng chỗ cần sửa, lưu bằng thao tác hiện hành, thấy trạng thái mới từ server và quay lại tiếp tục setup mà không nhầm tổ chức/model hoặc mất form không nhạy cảm.

**Bằng chứng triển khai:** Restore/build Release đạt. Browser Admin/TikTok trên source cuối đạt 92 Passed / 0 Failed / 0 Skipped, gồm 13 ca mới. Frontend đạt 296 / 0 / 0. Full .NET cuối đạt 1697 Passed / 0 Failed / 18 Skipped. Chưa smoke hoặc rollout hosting cho tính năng này. Chi tiết và phạm vi mỗi lượt ở biên bản triển khai.
