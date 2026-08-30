# Kế hoạch triển khai UI quản trị Tổ chức & AI

> Ngày lập: 2026-08-27  
> Trạng thái: Đã triển khai source và kiểm thử tự động; chưa chạy smoke database thật hoặc staging live có chi phí  
> Phạm vi: `TOOL-SERVER`, `TOOL-SHARED.Contracts`, `TOOL-TESTS` và Admin Console tại `/admin`  
> Mục tiêu của tài liệu: chia công việc thành các task có thể triển khai, kiểm thử và nghiệm thu độc lập.

## 1. Mục tiêu

Thêm một item **Tổ chức & AI** vào thanh điều hướng Admin Console để quản trị tập trung:

- tổ chức và thành viên;
- vai trò và hạn mức thành viên;
- budget tháng, chi phí đã dùng, đang giữ và còn lại;
- credential OpenAI/Kling theo từng tổ chức;
- bảng giá AI toàn hệ thống;
- token/usage và lịch sử thao tác.

Màn hình phải dùng đúng kiến trúc AI Gateway theo tổ chức. Không đưa API key xuống desktop và không khôi phục mô hình BYOK theo máy/người dùng.

## 2. Quyết định sản phẩm và bảo mật

### 2.1. Điều hướng

Thêm một item duy nhất vào sidebar, đặt sau **Người dùng**:

```text
Tổng quan
Người dùng
Tổ chức & AI
Gói sử dụng
Desktop Releases
```

Trong màn hình **Tổ chức & AI** có hai phạm vi:

1. **Tổ chức**: danh sách tổ chức và trang chi tiết từng tổ chức.
2. **Bảng giá AI**: cấu hình rate toàn hệ thống, chỉ Global Admin được thao tác.

Trang chi tiết tổ chức dùng các tab:

```text
[Tổng quan] [Thành viên] [Ngân sách & sử dụng] [API AI] [Nhật ký]
```

### 2.2. Phạm vi người dùng của phiên bản đầu

- Admin Console hiện chỉ cho tài khoản có global role `Admin` đăng nhập; phiên bản đầu giữ nguyên ranh giới này.
- Global Admin chỉ được tạo tổ chức và quản lý bảng giá theo global role.
- Các thao tác trong một tổ chức vẫn phải kiểm tra membership role hiện hành; global role `Admin` không tự động vượt qua RBAC của tổ chức.
- Người tạo tổ chức trở thành `Owner`, phù hợp với luồng hiện tại.
- Portal riêng cho Owner/OrganizationAdmin không có global role `Admin` là hạng mục mở rộng, không nằm trong MVP này.

### 2.3. Thuật ngữ “token”

- **API key/credential**: secret của OpenAI hoặc Kling, quản lý tại tab **API AI**.
- **Input token/output token/video second**: số lượng sử dụng, hiển thị tại **Ngân sách & sử dụng**.
- **JWT access token/refresh token**: không hiển thị, không cho sao chép và không quản lý như provider key. Admin chỉ được thu hồi phiên trong màn hình người dùng hiện có.

### 2.4. Quy tắc credential bắt buộc

- Chỉ gửi key qua HTTPS tới `TOOL-SERVER`.
- Test credential thành công trước khi rotate/lưu.
- Không trả plaintext key hoặc `EncryptedPayload` về trình duyệt.
- Sau khi lưu chỉ hiển thị `SecretHint`, version, status và thời điểm cập nhật.
- Không ghi key vào log, audit, toast, DOM ngoài thời gian modal đang mở, `localStorage` hoặc `sessionStorage`.
- Modal phải xóa giá trị key khi đóng, thành công hoặc thất bại.
- Giữ đúng vòng đời `Active -> Retiring -> Revoked`.

### 2.5. Ma trận action đã chốt

| Action | Global Admin | Owner | OrganizationAdmin | BillingManager | Member | Viewer |
|---|---:|---:|---:|---:|---:|---:|
| Tạo tổ chức | Có | — | — | — | — | — |
| Quản lý bảng giá AI | Có | — | — | — | — | — |
| Xem/cập nhật thành viên | Không tự vượt membership | Có | Có, trừ Owner | Không | Không | Không |
| Xem/cập nhật budget và usage | Không tự vượt membership | Có | Có | Có | Không | Không |
| Xem/rotate credential | Không tự vượt membership | Có | Có | Không | Không | Không |
| Xem audit tổ chức | Không tự vượt membership | Có | Có | Không | Không | Không |

Admin Console chỉ mở cho global role `Admin`, nhưng mọi action ở cột tổ chức vẫn được server kiểm tra bằng membership role hiện hành.

## 3. Trạng thái source hiện tại

### 3.1. Backend đã có thể tái sử dụng

- `GET|POST /api/organizations`.
- `GET|POST /api/organizations/{id}/members`.
- `PUT /api/organizations/{id}/members/{userId}`.
- `PUT /api/organizations/{id}/budget`.
- `GET /api/organizations/{id}/providers`.
- `PUT /api/organizations/{id}/providers/{providerCode}/credential`.
- `GET /api/organizations/{id}/usage`.
- `GET /api/admin/ai-pricing`.
- `POST /api/admin/ai-pricing/models/{modelId}/rates`.
- `DELETE /api/admin/ai-pricing/rates/{rateId}`.
- RBAC, bảo vệ Owner cuối cùng, credential test/rotate, budget ledger và audit write đã có trong service.

### 3.2. Khoảng trống cần triển khai

- Admin Console chưa có item/panel **Tổ chức & AI**.
- Chưa có UI tạo tổ chức, quản lý thành viên, budget, credential và pricing.
- Usage response hiện thiên về ledger chi phí; UI chưa có tổng hợp input/output token và video second dạng typed contract.
- Chưa có API đọc `OrganizationAuditLogs` cho tab **Nhật ký**.
- Chưa có empty/loading/error state riêng cho quản trị tổ chức.
- Chưa có kiểm thử UI/contract cho luồng quản trị mới.

### 3.3. Database

- MVP dự kiến dùng các bảng 4.0 hiện có, không cần sửa schema.
- Nếu sau khi đo truy vấn mới cần index hoặc cột mới, phải tạo migration idempotent phiên bản mới; không sửa ngược migration 4.0 đã triển khai.
- Không chạy migration trên database thật trong quá trình phát triển nếu chưa xác nhận instance, database, backup và khả năng restore.

### 3.4. Cấu trúc JavaScript đã triển khai

- `admin.js`: shell đăng nhập, refresh token, API helper và các màn hình license/release hiện hữu.
- `admin-organizations.js`: state, lazy loader, render và event của Organization/AI; không lưu credential vào state/storage.
- `admin.css`: giữ hệ thiết kế hiện hữu và bổ sung responsive styles cho organization tabs, provider cards, usage và pricing.
- Request chi tiết dùng `AbortController` và version guard để không render response cũ sau khi đổi tổ chức.

## 4. Thiết kế màn hình đề xuất

### 4.1. Danh sách tổ chức

Các cột:

| Cột | Nội dung |
|---|---|
| Tổ chức | Tên, code và status |
| Vai trò của tôi | Owner/OrganizationAdmin/... |
| Thành viên | Tổng số và số Active nếu contract hỗ trợ |
| Budget | Hard limit của kỳ hiện tại |
| Sử dụng | Actual, Reserved, Remaining |
| OpenAI | Sẵn sàng/thiếu credential/thiếu rate |
| Kling | Sẵn sàng/thiếu credential/thiếu rate |
| Thao tác | Xem chi tiết |

Hành động chính: **Tạo tổ chức**.

Form tạo tổ chức:

- Tên tổ chức.
- Mã tổ chức; cho phép để trống để server sinh.
- Budget tháng ban đầu.
- Currency; MVP chỉ hiển thị/chấp nhận `USD` theo cấu hình hiện tại.
- Cảnh báo rõ `0 = khóa AI`.

### 4.2. Tổng quan tổ chức

Hiển thị:

- status, code và membership role của người đang thao tác;
- số thành viên;
- budget/actual/reserved/remaining;
- trạng thái OpenAI/Kling;
- danh sách cảnh báo cấu hình có thứ tự ưu tiên:
  1. budget bằng 0;
  2. thiếu credential Active;
  3. thiếu rate bắt buộc;
  4. provider/model bị tắt.

Không dùng từ “Sẵn sàng” nếu còn bất kỳ điều kiện bắt buộc nào chưa đạt.

### 4.3. Thành viên

Các thao tác:

- tải danh sách thành viên;
- thêm bằng email;
- thay đổi role/status/member budget;
- hiển thị quyền thao tác theo role của actor;
- xác nhận trước khi suspend/remove/hạ quyền;
- giải thích lỗi khi thao tác làm mất Owner Active cuối cùng.

Không hiển thị nút mà actor chắc chắn không có quyền; server vẫn là nơi quyết định cuối cùng.

### 4.4. Ngân sách & sử dụng

Cards bắt buộc:

- Budget tháng.
- Actual cost.
- Reserved cost.
- Remaining budget.

Thành phần bổ sung:

- progress bar với cảnh báo 70%/90%/100%;
- form cập nhật budget, ghi rõ `0 = khóa AI`;
- bảng usage theo thời gian, provider, model, user và project;
- tổng input token, output token và video second;
- trạng thái reservation/actual/release để hỗ trợ đối soát.

### 4.5. API AI

Mỗi provider hiển thị một card:

- provider/model;
- configured/not configured;
- credential version;
- `SecretHint`;
- `Active/Retiring/Revoked`;
- thời điểm cập nhật;
- trạng thái các rate bắt buộc;
- nút **Cấu hình credential** hoặc **Thay credential**.

Modal credential:

- tên gợi nhớ, không chứa key;
- ô password để dán API key;
- cảnh báo key chỉ gửi một lần và không xem lại được;
- nút **Kiểm tra và lưu**;
- trạng thái busy chặn gửi lặp;
- xóa giá trị khỏi input ngay khi request kết thúc.

### 4.6. Bảng giá AI

- Nhóm theo provider/model.
- OpenAI yêu cầu `InputToken` và `OutputToken`.
- Kling yêu cầu `VideoSecond`.
- Hiển thị unit, unit price, currency và khoảng hiệu lực.
- Chỉ tạo rate mới và vô hiệu hóa rate cũ; không sửa lịch sử rate đã dùng.
- Cảnh báo khi model thiếu rate bắt buộc.
- Không seed hoặc tự đoán đơn giá provider.

### 4.7. Nhật ký

Hiển thị các sự kiện tổ chức:

- tạo tổ chức;
- thêm/cập nhật thành viên;
- đổi budget;
- rotate credential;
- các sự kiện vận hành được phép hiển thị sau này.

Chỉ trả dữ liệu audit đã lọc; không trả encrypted payload, key, authorization header hoặc request body chứa secret.

## 5. Danh sách task triển khai

Quy ước effort: `S` nhỏ, `M` vừa, `L` lớn. Tất cả task ban đầu ở trạng thái `[ ]`.

### Phase A — Chốt contract và kiến trúc UI

- [x] **ORG-A01 — Chốt sitemap và quyền hiển thị** (`S`)
  - Xác nhận item **Tổ chức & AI**, hai phạm vi Tổ chức/Bảng giá AI và năm tab chi tiết.
  - Lập ma trận action theo global role và organization role.
  - Ghi rõ Global Admin không tự động có quyền credential nếu không có membership phù hợp.
  - Đầu ra: sitemap, role/action matrix và danh sách trạng thái UI.

- [x] **ORG-A02 — Chốt cách tổ chức JavaScript Admin Console** (`M`)
  - Không đưa thêm toàn bộ feature vào một file khó bảo trì nếu `admin.js` tiếp tục tăng lớn.
  - Tách phần shell/auth/API dùng chung và module organization/pricing, hoặc tạo namespace rõ ràng nếu chưa chuyển sang ES module.
  - Không thay framework trong phạm vi feature này.
  - Đầu ra: cấu trúc file và ranh giới state/render/event.

### Phase B — Contracts và API đọc dữ liệu

- [x] **ORG-B01 — Bổ sung typed usage metrics** (`M`, phụ thuộc ORG-A01)
  - Sửa `TOOL-SHARED.Contracts/Organizations/OrganizationContracts.cs` trước.
  - Bổ sung tổng hợp nullable cho `InputTokens`, `OutputTokens`, `VideoSeconds` và nhóm theo provider/model.
  - Server parse `UsageJson` thành field typed; không trả raw JSON không kiểm soát cho UI.
  - Giữ backward compatibility cho các client hiện hành.

- [x] **ORG-B02 — Bổ sung API đọc audit tổ chức** (`M`)
  - Thêm contract audit không chứa secret.
  - Thêm `GET /api/organizations/{organizationId}/audit` với `take` giới hạn và thứ tự mới nhất trước.
  - Chỉ Owner/OrganizationAdmin được xem ở MVP; nếu cho BillingManager xem phải chốt lại nghiệp vụ trước khi code.
  - Kiểm tra membership/status tại server.

- [x] **ORG-B03 — Đánh giá dữ liệu tổng hợp cho danh sách tổ chức** (`S`)
  - Ưu tiên dùng `GET /api/organizations` hiện có.
  - Chỉ mở rộng summary với member/provider readiness khi có thể làm bằng truy vấn gộp, tránh N+1.
  - Nếu cần danh sách mọi tổ chức cho nhiều Global Admin, tạo endpoint admin riêng; không đổi `GetMine` thành truy vấn vượt membership.

- [x] **ORG-B04 — Chuẩn hóa mã lỗi UI cần xử lý** (`S`)
  - Lập danh sách code cho access denied, last owner, budget validation, credential test failure, pricing validation.
  - UI ánh xạ thành thông báo rõ hành động khắc phục; không suy luận bằng nội dung message tự do.

### Phase C — Khung màn hình Tổ chức & AI

- [x] **ORG-C01 — Thêm icon, nav item và panel** (`S`, phụ thuộc ORG-A02)
  - Sửa `TOOL-SERVER/Pages/Admin/Index.cshtml`.
  - Thêm SVG icon phù hợp, `data-view="organizations"`, page metadata và top action.
  - Giữ đúng style sidebar hiện tại và hỗ trợ màn hình hẹp.

- [x] **ORG-C02 — Mở rộng state và loader theo từng view** (`M`)
  - Không bắt `loadAll()` tải toàn bộ usage/audit/credential ở lần mở trang.
  - Lazy-load danh sách tổ chức khi vào view.
  - Lazy-load tab chi tiết khi chọn tổ chức.
  - Có abort/ignore response cũ khi người dùng đổi tổ chức nhanh.

- [x] **ORG-C03 — Danh sách và empty/error/loading state** (`M`)
  - Render bảng/cards tổ chức.
  - Có empty state “Chưa có tổ chức”.
  - Phân biệt 401, 403, validation và lỗi server.
  - Có nút retry mà không đăng xuất người dùng khi lỗi không liên quan phiên.

- [x] **ORG-C04 — Form tạo tổ chức** (`M`)
  - Dùng `POST /api/organizations` hiện có.
  - Validate name/code/budget/currency ở client để hỗ trợ UX, nhưng vẫn tin kết quả server.
  - Sau thành công: refresh list, chọn tổ chức mới và mở Tổng quan.

### Phase D — Thành viên và budget

- [x] **ORG-D01 — Tab Thành viên** (`M`)
  - Dùng các API members hiện có.
  - Hiển thị email, display name, role, status, member limit và ngày tham gia.
  - Có search/filter client-side cho danh sách hiện tại.

- [x] **ORG-D02 — Modal thêm/cập nhật thành viên** (`M`)
  - Thêm thành viên theo email.
  - Cập nhật role/status/monthly limit.
  - Confirmation cho thao tác nhạy cảm.
  - Hiển thị rõ lỗi Owner cuối cùng và quyền actor không đủ.

- [x] **ORG-D03 — Tab Ngân sách** (`M`)
  - Hiển thị hard limit/actual/reserved/remaining và kỳ UTC.
  - Form cập nhật budget dùng endpoint hiện có.
  - Cảnh báo nổi bật khi budget bằng 0.
  - Không mô tả budget như số dư đã nạp; đây là hạn mức nội bộ.

### Phase E — Credential và pricing

- [x] **ORG-E01 — Tab API AI và readiness rules** (`M`)
  - Kết hợp organization provider response với pricing catalog.
  - OpenAI ready khi model enabled, credential Active, đủ InputToken/OutputToken và budget > 0.
  - Kling ready khi model enabled, credential Active, đủ VideoSecond và budget > 0.
  - Hiển thị nguyên nhân thiếu cụ thể, không chỉ “Chưa cấu hình”.

- [x] **ORG-E02 — Modal test và rotate credential** (`L`)
  - Dùng endpoint credential hiện có; server tiếp tục test trước khi lưu.
  - `type="password"`, không prefill, không giữ secret trong state lâu dài.
  - Chặn double submit.
  - Sau thành công chỉ render `SecretHint` và version trả về.
  - Sau thất bại key cũ vẫn Active và UI phải nói rõ điều này.

- [x] **ORG-E03 — Màn hình Bảng giá AI** (`L`)
  - Dùng API pricing hiện có.
  - Form tạo rate theo usage type/unit/currency/effective time hợp lệ.
  - Cho deactivate rate với xác nhận.
  - Không cho chỉnh sửa in-place rate lịch sử.
  - Hiển thị cảnh báo thiếu rate bắt buộc theo model.

### Phase F — Usage và audit

- [x] **ORG-F01 — Dashboard usage** (`M`, phụ thuộc ORG-B01)
  - Cards chi phí và thanh tiến độ budget.
  - Tổng input/output token và video second.
  - Nhóm theo provider/model/member.
  - Không dùng floating-point JavaScript để quyết toán; UI chỉ format số server trả về.

- [x] **ORG-F02 — Bảng ledger** (`M`)
  - Hiển thị reservation/actual/release/refund/adjustment.
  - Có filter provider/model/entry kind.
  - Có pagination hoặc `take` hợp lý; không tải không giới hạn.

- [x] **ORG-F03 — Tab Nhật ký** (`M`, phụ thuộc ORG-B02)
  - Hiển thị actor, event type, thời gian, correlation ID và dữ liệu đã lọc.
  - Mapping event type sang tiếng Việt tại UI.
  - Không render JSON bằng `innerHTML` nếu chưa escape.

### Phase G — Bảo mật, accessibility và kiểm thử

- [x] **ORG-G01 — Test RBAC dương và âm** (`L`)
  - Owner/OrganizationAdmin quản lý credential.
  - BillingManager chỉ quản lý/xem billing đúng phạm vi.
  - Member/Viewer bị chặn thao tác quản trị.
  - Cross-organization bị chặn.
  - Global Admin không có membership không được đọc/rotate credential tổ chức ngoài phạm vi.
  - Không thể làm mất Owner Active cuối cùng.

- [x] **ORG-G02 — Test credential không rò rỉ** (`M`)
  - Response không chứa `ApiKey` hoặc `EncryptedPayload`.
  - Credential test thất bại không thay đổi bản Active.
  - Audit/log chỉ có hint, provider, version và trạng thái an toàn.
  - Không có key trong exception/message fixture.

- [x] **ORG-G03 — Test budget/usage/pricing** (`L`)
  - Budget 0 hiển thị khóa AI.
  - Thiếu từng rate bắt buộc cho kết quả readiness đúng.
  - Token/video-second parsing đúng từ usage snapshot.
  - Rate mới không sửa snapshot request cũ.

- [x] **ORG-G04 — Test Admin UI** (`M`)
  - Navigation và lazy-load.
  - Escape dữ liệu server trước khi chèn DOM.
  - Modal reset secret sau close/success/failure.
  - Keyboard focus, label, dialog close và trạng thái disabled/busy.
  - Responsive ở chiều rộng tương tự ảnh Admin Console hiện tại.

- [x] **ORG-G05 — Chạy bộ kiểm tra bắt buộc** (`S`)
  - `dotnet restore TOOL_GEN_POST_VIDEO.slnx`.
  - `dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore`.
  - `dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build`.
  - Ghi nhận đúng số test thực tế, không sao chép mốc cũ.
  - Kết quả ngày 2026-08-27: Release build `0` warning/error; `122/122` test đạt; hai file JavaScript Admin vượt qua `node --check`.

### Phase H — Smoke test và triển khai

- [ ] **ORG-H01 — Smoke test không phát sinh chi phí** (`M`)
  - Tạo tổ chức với budget 0.
  - Thêm/cập nhật thành viên và xác minh RBAC.
  - Kiểm tra trạng thái thiếu credential/rate.
  - Test credential bằng fake provider trong test environment.
  - Xác minh API/DOM/log không lộ secret.

- [ ] **ORG-H02 — Staging live test có phê duyệt chi phí** (`M`)
  - Chỉ thực hiện sau khi có budget, rate và credential staging.
  - Gọi một request OpenAI nhỏ và một Kling clip ngắn nếu được duyệt chi phí.
  - Đối chiếu usage ledger, provider dashboard và audit.

- [ ] **ORG-H03 — Checklist phát hành** (`S`)
  - Database có migration 4.0 và backup/restore đã xác minh nếu có thay đổi DB.
  - HTTPS hợp lệ và Data Protection key ring ổn định.
  - Không có secret trong source/appsettings/log.
  - Admin UI hiển thị đúng quyền và trạng thái thiếu cấu hình.
  - Có rollback binary; không xóa dữ liệu credential/usage/audit khi rollback.

## 6. Thứ tự triển khai khuyến nghị

```text
ORG-A01 -> ORG-A02
        -> ORG-B01/B02/B03/B04
        -> ORG-C01/C02/C03/C04
        -> ORG-D01/D02/D03
        -> ORG-E01/E02/E03
        -> ORG-F01/F02/F03
        -> ORG-G01..G05
        -> ORG-H01/H02/H03
```

Mốc bàn giao nên chia thành ba increment:

1. **Increment 1 — Tổ chức cơ bản**: nav, danh sách, tạo tổ chức, thành viên, budget.
2. **Increment 2 — AI configuration**: provider readiness, credential và pricing.
3. **Increment 3 — Vận hành**: usage token, ledger, audit, security regression và staging smoke test.

## 7. Tiêu chí nghiệm thu cuối

- Sidebar có item **Tổ chức & AI** đúng phong cách hiện tại.
- Global Admin tạo được tổ chức và trở thành Owner.
- UI chỉ cho phép action phù hợp organization role; server chặn mọi trường hợp vượt quyền.
- Không thể xóa/hạ quyền Owner Active cuối cùng.
- Budget 0 được giải thích rõ là khóa AI.
- OpenAI/Kling hiển thị chính xác credential, model và rate còn thiếu.
- Credential mới được test trước khi lưu; key cũ không hỏng khi test thất bại.
- Trình duyệt không bao giờ nhận lại plaintext/encrypted credential.
- Usage hiển thị được chi phí, input/output token và video second.
- Pricing không tự đoán giá và không sửa lịch sử rate đã dùng.
- Audit không chứa secret.
- Release build không warning/error và toàn bộ test đạt.

## 8. Ngoài phạm vi MVP

- Portal quản trị riêng cho Owner/OrganizationAdmin không có global role `Admin`.
- Thanh toán/nạp tiền hoặc đồng bộ hóa đơn tự động với provider.
- Hiển thị/copy JWT access token, refresh token hoặc API key đã lưu.
- Cho desktop nhập hoặc quản lý provider key.
- Tự động lấy giá từ website provider.
- Xóa credential/usage/audit lịch sử.
- Đổi framework frontend Admin Console.

## 9. Rủi ro cần theo dõi

| Rủi ro | Cách giảm thiểu |
|---|---|
| Global Admin bị hiểu nhầm là có toàn quyền trong mọi tổ chức | Giữ membership RBAC tại service; test trường hợp Admin không có membership |
| API key bị giữ trong DOM/state/log | Input một lần, reset bắt buộc, response chỉ có hint, test không rò rỉ |
| UI báo “Ready” sai khi thiếu rate/budget | Readiness rule tập trung và test từng điều kiện |
| N+1 khi tải danh sách tổ chức | Dùng summary query gộp hoặc lazy-load chi tiết |
| Usage lớn làm trang chậm | Giới hạn `take`, pagination/filter và chỉ tải khi mở tab |
| Sửa rate làm sai đối soát lịch sử | Chỉ tạo/deactivate; request giữ rate snapshot |
| `admin.js` trở nên khó bảo trì | Chốt ranh giới module trước khi thêm feature lớn |
