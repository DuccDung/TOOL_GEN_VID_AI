# Kế hoạch tự động phân bổ người mua gói vào tổ chức

**Trạng thái:** Đã triển khai source; chưa chạy migration/rehearsal trên database clone, staging hoặc production  
**Ngày lập:** 2026-09-04  
**Ngày cập nhật source:** 2026-09-04  
**Phạm vi:** License, thanh toán SePay, tổ chức, thành viên, quản trị và desktop

Các task source từ `TASK-01` đến `TASK-10` đã hoàn tất và được kiểm tra bằng build/test trong repository. `TASK-11` là công việc vận hành riêng, chỉ thực hiện sau khi xác minh đúng instance/database, backup và khả năng restore. Migration 4.0.11 có trong source không đồng nghĩa đã được áp dụng trên database thật.

## 1. Mục tiêu

Cho phép Global Admin chuẩn bị trước nhiều tổ chức, cấu hình sức chứa và gắn credential/model policy/ngân sách cho từng tổ chức. Khi người dùng mua gói thành công, hệ thống tự động cấp license và thêm người dùng vào một tổ chức còn chỗ với vai trò `Member`.

Luồng mục tiêu:

```text
Gói license -> Pool tổ chức -> Các tổ chức đã chuẩn bị sẵn
                                      |
                           Giữ chỗ khi tạo payment
                                      |
                    Cấp license + membership khi nhận tiền
```

## 2. Hiện trạng

Source hiện đã nối hai lớp license và tổ chức:

- Gói `LicensePlan` được ánh xạ tới pool tổ chức và vẫn cấp quyền sử dụng ứng dụng qua `UserLicense`.
- Pool quản lý sức chứa, reservation/assignment và chỉ chọn tổ chức đủ readiness.
- Khi payment SePay được fulfillment, server cấp hoặc gia hạn license, kích hoạt seat và tạo/kích hoạt `OrganizationMember` trong cùng transaction.
- Membership có dấu vết `IsProvisioningManaged`; thao tác quản trị thủ công chuyển quyền kiểm soát khỏi worker.
- Worker giải phóng reservation, retry payment `Paid`, kích hoạt assignment lên lịch và tạm ngưng membership tự động khi license không còn hiệu lực.
- Desktop hiển thị trạng thái sức chứa/provisioning, làm mới license và ưu tiên chọn tổ chức vừa được cấp.

Phần chưa thực hiện là chạy migration trên database clone/staging, smoke test SePay trong môi trường được phép và rollout production theo `TASK-11`.

## 3. Nguyên tắc nghiệp vụ đề xuất

- Một gói được ánh xạ vào một pool tổ chức đang hoạt động.
- Một pool có nhiều tổ chức đã chuẩn bị sẵn.
- Một tổ chức chỉ thuộc một pool tự động đang hoạt động tại một thời điểm.
- Mỗi tổ chức có sức chứa khách hàng, số chỗ đã dùng, đang giữ và còn trống.
- Owner và tài khoản vận hành không chiếm chỗ khách hàng.
- Người mua được thêm với vai trò `Member`, không tự động nhận quyền quản trị.
- Chỉ phân bổ vào tổ chức `Active`, bật nhận người tự động và đã sẵn sàng về credential, model policy cùng ngân sách.
- Membership do hệ thống cấp phải được phân biệt với membership do admin tạo thủ công.
- Hệ thống không tự thay đổi Owner, OrganizationAdmin hoặc membership thủ công.
- Mua lại cùng gói ưu tiên giữ nguyên tổ chức.
- Nếu đổi gói/pool, việc chuyển tổ chức phải bảo toàn dữ liệu và quyền truy cập dự án cũ theo chính sách được chốt trước khi triển khai.
- Nếu gói bao gồm hạn mức AI cho từng người, hạn mức phải được cấu hình rõ ràng; không suy đoán từ giá bán.
- API key tiếp tục chỉ tồn tại trên server và không được trả về desktop.

## 4. Các quyết định cần chốt trước khi triển khai

1. Sức chứa chỉ tính khách hàng tự động hay toàn bộ thành viên.
2. Một gói được phép ánh xạ một hay nhiều pool.
3. Một tổ chức có được tham gia nhiều pool hay không.
4. Thuật toán phân bổ: theo priority, lấp đầy lần lượt hay cân bằng tải.
5. Thời gian giữ chỗ của payment pending.
6. Thời gian ân hạn membership sau khi license hết hạn.
7. Cách truy cập dự án cũ nếu người dùng được chuyển sang tổ chức khác.
8. Cách xử lý khi tiền đến muộn nhưng tất cả tổ chức đã hết chỗ.
9. Gói quyết định hạn mức AI thành viên hay dùng hạn mức mặc định của pool/tổ chức.
10. Cách xử lý người dùng đã có membership thủ công trong một tổ chức thuộc pool.

Khuyến nghị ban đầu:

- một gói ánh xạ một pool;
- một tổ chức thuộc một pool tự động;
- chỉ assignment khách hàng `Reserved`/`Active` chiếm chỗ;
- phân bổ theo priority, sau đó theo tỷ lệ sử dụng thấp nhất;
- giữ chỗ ngay khi tạo payment;
- mua lại cùng gói giữ nguyên tổ chức;
- membership tự động hết hạn được tạm ngưng trước khi giải phóng chỗ;
- membership thủ công không bị quy trình license tự động thay đổi.

## 5. Mô hình dữ liệu dự kiến

Tạo migration idempotent mới, dự kiến:

`database/VideoFactory.4.0.11.OrganizationSeatProvisioning.sql`

Không sửa trực tiếp migration 4.0.9 hoặc 4.0.10 đã có thể được triển khai.

### 5.1. Pool tổ chức

Đề xuất bảng `ai.OrganizationPools`:

- mã và tên pool;
- trạng thái hoạt động;
- chiến lược phân bổ;
- thông tin audit và row version.

### 5.2. Tổ chức trong pool

Đề xuất bảng `ai.OrganizationPoolOrganizations`:

- `OrganizationPoolId`;
- `OrganizationId`;
- sức chứa khách hàng;
- độ ưu tiên;
- trạng thái bật/tắt nhận người;
- trạng thái sẵn sàng;
- thông tin audit và row version.

### 5.3. Ánh xạ gói với pool

Đề xuất bảng `ai.LicensePlanOrganizationPools`:

- `LicensePlanId`;
- `OrganizationPoolId`;
- trạng thái ánh xạ;
- hạn mức thành viên mặc định nếu nghiệp vụ yêu cầu;
- thông tin audit.

### 5.4. Giữ chỗ và assignment

Đề xuất bảng `ai.OrganizationSeatAssignments` để lưu toàn bộ vòng đời:

- người dùng, gói, payment, license và tổ chức;
- trạng thái `Reserved`, `Scheduled`, `Active`, `Released` hoặc `Failed`;
- thời điểm giữ chỗ, hết hạn, kích hoạt và giải phóng;
- lý do giải phóng/lỗi;
- idempotency và row version.

Cần có unique index và kiểm tra transaction để:

- một payment không chiếm nhiều chỗ;
- webhook lặp không tạo assignment mới;
- một người không có nhiều assignment tự động trùng nhau trong cùng pool;
- tổng `Reserved + Active` không vượt sức chứa.

### 5.5. Quyền SQL

- Desktop không được đọc hoặc sửa trực tiếp các bảng phân bổ.
- Server là bên duy nhất ghi reservation và assignment.
- Cập nhật `VideoFactory.DesktopLeastPrivilege.sql` để deny các bảng mới.
- Migration phải ghi version mới vào `ai.SchemaVersions`.

## 6. Kế hoạch task triển khai

### [x] TASK-01 - Hoàn thiện đặc tả nghiệp vụ

- Chốt các quyết định tại mục 4.
- Bổ sung nghiệp vụ vào `NGHIEP_VU_HE_THONG_VIDEOMAKER.md`.
- Xác định rõ membership tự động và membership thủ công.
- Xác định chính sách bảo toàn dự án khi chuyển tổ chức.
- Xác định cách tính hạn mức AI theo gói.

### [x] TASK-02 - Tạo migration 4.0.11

- Tạo các bảng pool, mapping và assignment.
- Thêm foreign key, check constraint, unique index và row version.
- Backfill an toàn nếu cần.
- Ghi `ai.SchemaVersions`.
- Cập nhật script least privilege.
- Viết kiểm tra migration idempotent và schema verification.

### [x] TASK-03 - Cập nhật shared contracts

Thực hiện trong `TOOL-SHARED.Contracts` trước khi cập nhật server và desktop:

- DTO quản lý pool;
- DTO cấu hình sức chứa tổ chức;
- DTO ánh xạ gói với pool;
- thống kê `Capacity`, `Used`, `Reserved`, `Available`;
- trạng thái reservation/assignment của payment;
- thông tin tổ chức được cấp sau fulfillment;
- mã lỗi hết chỗ, chờ phân bổ và cấu hình pool chưa sẵn sàng.

Không đưa credential hoặc dữ liệu nhạy cảm vào DTO.

### [x] TASK-04 - API quản trị pool và sức chứa

Global Admin có thể:

- tạo, sửa và ngừng pool;
- ánh xạ gói với pool;
- thêm hoặc gỡ tổ chức khỏi pool;
- đặt sức chứa và priority;
- bật/tắt nhận người tự động;
- xem số chỗ đã dùng, đang giữ và còn trống;
- xem assignment đang hoạt động hoặc lỗi;
- retry hoặc chuyển assignment có audit.

Global Admin vẫn tạo tổ chức theo quy tắc hiện tại. Credential phải được quản lý qua quyền Owner/OrganizationAdmin của tổ chức và luôn đi qua luồng test, mã hóa trên server.

### [x] TASK-05 - Dịch vụ chọn tổ chức và giữ chỗ

Khi tạo payment:

1. Xác minh gói còn bán và có pool hợp lệ.
2. Kiểm tra pool còn tổ chức đủ điều kiện.
3. Chọn tổ chức theo thứ tự ổn định.
4. Khóa bản ghi sức chứa trong transaction `Serializable`.
5. Kiểm tra lại `Reserved + Active < Capacity`.
6. Tạo seat reservation gắn với user, plan và payment.
7. Chỉ trả QR sau khi giữ chỗ thành công.

Thứ tự chọn đề xuất:

1. priority cao hơn;
2. tỷ lệ sử dụng thấp hơn;
3. `OrganizationId` để kết quả ổn định.

Phải có retry giới hạn khi deadlock/concurrency và không được vượt sức chứa trong mọi trường hợp.

### [x] TASK-06 - Tích hợp payment fulfillment

Khi webhook hợp lệ:

- chuyển payment sang `Paid`;
- cấp hoặc gia hạn `UserLicense`;
- chuyển reservation thành assignment `Active`;
- tạo hoặc kích hoạt `OrganizationMember` với vai trò `Member`;
- thiết lập hạn mức thành viên nếu gói/pool có cấu hình;
- ghi liên kết payment, license, assignment và organization;
- chỉ chuyển payment sang `Fulfilled` khi toàn bộ quy trình hoàn tất.

Webhook hoặc request lặp không được:

- gia hạn license lần hai;
- tạo membership trùng;
- chiếm nhiều seat;
- ghi usage hoặc audit trùng không cần thiết.

Nếu tiền đến sau khi reservation hết hạn:

- thử phân bổ lại trong transaction;
- nếu hết chỗ, giữ trạng thái đã nhận tiền nhưng chờ provisioning;
- ghi cảnh báo vận hành và cho phép admin retry;
- không âm thầm đánh dấu `Fulfilled` khi người dùng chưa nhận được tổ chức.

### [x] TASK-07 - Worker quản lý vòng đời seat

Worker định kỳ:

- giải phóng reservation của payment hết hạn;
- retry payment đã nhận tiền nhưng chưa phân bổ;
- kích hoạt assignment được lên lịch;
- tạm ngưng membership tự động khi license hết hạn, suspended hoặc revoked;
- giải phóng seat sau thời gian ân hạn;
- ưu tiên khôi phục tổ chức cũ khi gia hạn;
- không thay đổi membership thủ công, Owner hoặc OrganizationAdmin;
- không xóa dự án và dữ liệu lịch sử.

### [x] TASK-08 - Giao diện quản trị

Bổ sung màn hình quản trị:

- danh sách pool;
- gói thuộc pool nào;
- tổ chức thuộc pool nào;
- sức chứa, đã dùng, đang giữ và còn trống;
- trạng thái credential, policy và ngân sách;
- trạng thái sẵn sàng nhận người;
- danh sách assignment;
- lỗi provisioning và thao tác retry/chuyển tổ chức;
- audit của thay đổi capacity và phân bổ.

Tổ chức chưa đủ cấu hình phải hiển thị `Chưa sẵn sàng` và không được allocator chọn.

### [x] TASK-09 - Trải nghiệm desktop

Sau khi payment được fulfillment:

1. Desktop refresh license.
2. Refresh danh sách tổ chức.
3. Tự chọn tổ chức vừa được phân bổ.
4. Kích hoạt thiết bị nếu cần.
5. Gỡ overlay khóa.
6. Thông báo gói và tổ chức đã sẵn sàng.

Các trạng thái cần hiển thị riêng:

- đang giữ chỗ;
- đã nhận tiền và đang cấp tổ chức;
- đã cấp tổ chức;
- pool hết sức chứa;
- provisioning lỗi và cần quản trị viên hỗ trợ.

### [x] TASK-10 - Kiểm thử

Unit/regression test và runner mô phỏng không giao dịch thật đã có trong source. Runner SQL Server/staging là opt-in, không tự chạy và không thay thế smoke test SePay thật; xem `KIEM_THU_SEPAY_PHAN_BO_TO_CHUC_KHONG_GIAO_DICH_THAT.md`.

Phải có test cho:

- một user mua một lần;
- nhiều user mua đồng thời;
- tổ chức đầy thì chuyển sang tổ chức tiếp theo;
- toàn bộ pool đầy thì không tạo payment mới;
- request tạo payment lặp;
- webhook trùng;
- payment hết hạn giải phóng chỗ;
- webhook đến sau khi QR hết hạn;
- gia hạn cùng gói;
- đổi sang gói/pool khác;
- user đã có membership thủ công;
- license hết hạn, suspended hoặc revoked;
- không thay đổi Owner cuối cùng;
- Viewer không được phát sinh AI;
- hạn mức tổ chức và thành viên vẫn được kiểm tra;
- migration chạy lần hai không thay đổi sai dữ liệu;
- desktop không được sửa bảng phân bổ;
- API và log không lộ credential.

### [ ] TASK-11 - Rehearsal và rollout

- Chạy migration trên database clone có dữ liệu gần production.
- Chạy migration lần hai để xác minh idempotency.
- Đối chiếu row count, foreign key, index, constraint và quyền SQL.
- Tạo pool staging có ít nhất hai tổ chức.
- Cấu hình credential test, model policy và ngân sách nhỏ.
- Kiểm tra mua gói, hết chỗ, webhook lặp, payment muộn và hết hạn license.
- Thiết lập cảnh báo payment `Paid` nhưng chưa `Fulfilled`.
- Chỉ rollout production sau khi xác minh backup và khả năng restore.

## 7. Tiêu chí nghiệm thu

- Global Admin có thể chuẩn bị trước nhiều tổ chức và đặt sức chứa.
- Gói được ánh xạ rõ ràng với pool tổ chức.
- Chỉ tổ chức sẵn sàng mới nhận người tự động.
- Người mua thành công tự nhận license và membership `Member`.
- Desktop tự làm mới và chọn tổ chức vừa được cấp.
- Không vượt sức chứa khi có nhiều thanh toán đồng thời.
- Hết chỗ được phát hiện trước khi nhận tiền trong luồng thông thường.
- Payment/webhook lặp không cấp trùng license, membership hoặc seat.
- Payment đã nhận tiền nhưng provisioning lỗi có trạng thái và quy trình retry rõ ràng.
- API key chỉ tồn tại trên server.
- Membership thủ công và vai trò quản trị không bị worker tự thay đổi.
- Dự án cũ không bị xóa khi license hết hạn hoặc người dùng chuyển tổ chức.
- Build và toàn bộ test bắt buộc đạt sau khi triển khai source.

## 8. Thứ tự triển khai

```text
Đặc tả nghiệp vụ
    -> Migration và quyền SQL
    -> Shared Contracts
    -> API quản trị
    -> Seat allocator/reservation
    -> Payment fulfillment
    -> Worker vòng đời
    -> Admin UI và Desktop UI
    -> Test đầy đủ
    -> Database clone
    -> Staging
    -> Production
```

Không chạy migration trên database thật, không tạo giao dịch SePay thật và không rollout production nếu chưa xác minh đúng instance/database, backup, khả năng restore và phạm vi tác động.
