# SQL DirectCatalog đơn giản — 2026-10-17

Theo yêu cầu **“tạo sql đơn giản thôi”**, tạo file [VideoFactory.4.1.20.DirectDesktopCatalogSimple.sql](database/VideoFactory.4.1.20.DirectDesktopCatalogSimple.sql). Nhánh `main`, HEAD nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit có 407 đường dẫn thay đổi đầu lượt. Source migration 4.1.19 được giữ nguyên.

## Nội dung file

- Tạo `auth.DesktopDirectReleases` đúng 24 cột mà server hiện hành sử dụng.
- Giữ PK, 5 CHECK kiểm dữ liệu và 3 index, trong đó 2 index unique.
- Kiểm schema đã có trước khi tái sử dụng; chạy lại không xóa hoặc ghi đè dữ liệu.
- Ghi marker schema nền `4.1.19-direct-desktop-catalog` nếu thiếu và marker riêng `4.1.20-direct-desktop-catalog-simple`. Marker nền chỉ xác nhận schema tương thích, không xác nhận phân quyền.
- Không thao tác GRANT/DENY/REVOKE, không đọc/đòi role hoặc CONTROL; giữ quyền hiện có. Không có cursor hoặc bảng tạm để dựng định nghĩa CHECK.
- Transaction với rollback nếu lỗi. Yêu cầu schema 4.1.18 đã có; không sửa các bảng release/artifact/preparation/bootstrapper cũ, không tạo dữ liệu phát hành hoặc Active Setup.

## Cách dùng

Chọn đúng database TOOL-SERVER cần triển khai, kiểm backup/khả năng restore và chạy **toàn bộ file Simple**. Không cần chạy 4.1.19/DBA wrapper sau đó để khởi tạo. Nếu đã có schema 4.1.19 đúng, file tái sử dụng và chỉ bổ sung marker Simple; không sửa quyền trước đó.

Server/Setup/ZIP hiện có không cần build lại vì script mới tạo cùng schema. Vẫn cần deploy server, đăng ký/Active Setup 1.2.0 và tạo release theo runbook DirectCatalog. Kết quả tạo file hoặc QA không chứng minh các bước hosting/ứng dụng/model đã hoàn tất.

## Kiểm chứng QA khi tạo file

SQL Server 2019 LocalDB, database GUID mới từ backup synthetic, đã kiểm đạt:

- Chạy 4.1.20 hai lần sau 4.1.18 bằng tài khoản có CREATE TABLE/ALTER schema auth và SELECT/INSERT SchemaVersions, không có CONTROL/db_owner, thử DENY bị 15151, không có role desktop: script vẫn thành công. Đủ 24 cột, 5 CHECK trusted, 4 index tính cả PK, hai marker 4.1.19/4.1.20 mỗi loại đúng một dòng.
- Cả 5 CHECK chặn đúng dữ liệu sai: publication, hash, protocol, giới hạn ghi chú và build dương.
- Chạy hai lần trên schema 4.1.19 đã có: giữ nguyên dòng catalog, mô tả marker nền, 3 release/6 artifact, CHECK và toàn bộ quyền/membership. DENY desktop sẵn có vẫn hiệu lực.
- Lỗi được chèn giữa migration rollback bảng/marker; partial schema sai bị từ chối với 51222, CHECK sai 51223 và index sai 51224, không tự sửa dữ liệu/schema sai.
- Cả ba database thử mới được xóa và xác minh; giữ nguyên instance/database QA cũ. Không truy cập hosting.

[Report QA](D:/vm-direct-catalog-sql/20261017-144208-simple420-d6f7241f/report.json), SHA-256 `f05e195881dfcfb9716885e1fba45ed52397dd22440fbef5fd3722a707ca82ca`. Script SQL có SHA-256 `a6dad6fd84dd15f74cc59234bc00817ae3af99f8947c7d8457fd6632d7607ea7`. Lượt helper đầu vướng ghép mảng tên database trong PowerShell trước restore; đã giữ log, sửa helper và chạy lại toàn bộ đạt, không sửa SQL để né bài kiểm.

Lượt tạo file chỉ tạo/kiểm thử SQL, không kết nối hay thay đổi hosting. Không chạy lại build/test .NET hoặc frontend vì không sửa code ứng dụng. `git diff --check` đạt ở lượt đó. Bản sao file SQL được đưa vào `D:\vm-direct-catalog\20261017-140233\delivery\database`; hướng dẫn bàn giao chọn file Simple mới.

Migration cũ 4.1.19 vẫn có SHA-256 `ccda46d8a4863663d59656bff62e42767875a45731ac249d97f70560217acfe5`. Lỗi 15151/rollback trên hosting của nhánh cũ được giữ ở [biên bản 4.1.19](TRIEN_KHAI_SQL_4_1_19_HOSTING.md), không sửa lịch sử thành đã áp thành công.

## Đã áp SQL trên hosting — 2026-10-17

Theo yêu cầu tiếp theo **“chạy lên sql trên hostting cho tôi”**, đã chạy nguyên file 4.1.20 có checksum trên lên instance `MB2D70\MSSQLSERVER2019`, địa chỉ `112.78.2.70`, database `tap51004_tool`. Checkout vẫn là nhánh `main`, HEAD nền nêu trên; working tree có 409 đường dẫn thay đổi đầu lượt. Không chạy lại migration 4.1.19 hoặc wrapper DBA.

- Preflight bằng kết nối thực xác nhận SQL Server `15.0.2000.5`, compatibility 150, database ONLINE/ghi được, không có transaction treo; marker 4.1.18 có một dòng, bảng catalog và marker 4.1.19/4.1.20 chưa có.
- File Simple được thực thi một lần, commit lúc **2026-10-17T07:47:34.3864067Z** (14:47 giờ Việt Nam).
- Kiểm chứng bằng kết nối mới đạt lúc **2026-10-17T07:48:52.9452325Z**: đủ 24 cột đúng type/length/nullability/scale, không computed/identity; 5 CHECK có định nghĩa khớp QA SQL2019 và đều trusted/enabled; PK cùng 3 index đúng key/unique/trạng thái. Truy vấn đọc đủ các cột server sử dụng thành công. Marker 4.1.18/4.1.19/4.1.20 mỗi mã đúng một dòng; bảng catalog mới có 0 dòng.
- Fingerprint từng dòng của 4 bảng release/artifact/preparation/bootstrapper cũ không đổi; index/CHECK/FK và metadata quyền **nhìn thấy bằng tài khoản chạy** trước/sau không đổi. Đây không phải kiểm chứng toàn bộ quyền hoặc DENY desktop.
- Kiểm tra sau commit đầu tiên vướng cách PowerShell 5.1 serialize mảng `ConvertFrom-Json` thành đối tượng `value/Count`; các định nghĩa CHECK thực tế vẫn khớp từng trường. Đã sửa riêng helper **chỉ đọc**, giữ log ban đầu và kiểm lại đạt. Không sửa migration hoặc chạy lại DDL để xử lý lỗi helper.

[Báo cáo hoàn tất](D:/vm-sql-verification/20261017-144722-hosting-420-89f5d6/completion.json), [báo cáo kiểm chứng bằng kết nối mới](D:/vm-sql-verification/20261017-144722-hosting-420-89f5d6/reconciliation.json) và [tóm tắt schema](D:/vm-sql-verification/20261017-144722-hosting-420-89f5d6/reconciliation-summary.json). Thư mục bằng chứng giữ preflight, fingerprint trước/sau, SQL nguyên bản, helper và trạng thái lỗi kiểm chứng ban đầu; không chứa credential/connection string.

Metadata hosting cho thấy full backup gần nhất hoàn tất `2026-10-17T07:06:50` theo đồng hồ SQL Server (chưa xác định timezone), backup set `495443`, không có backup checksum; lịch sử restore nhìn thấy gần nhất thuộc backup cũ. **Chưa độc lập xác minh restore được bản backup hiện tại**, chưa rehearsal trên clone hosting; tiếp tục theo ủy quyền trước đó của người dùng cho cùng hosting và yêu cầu chạy Simple hiện tại. Không coi QA synthetic hoặc lịch sử backup là bằng chứng đã restore thử hosting.

Lượt này chỉ thực thi SQL và cập nhật biên bản; không sửa code ứng dụng, không chạy lại build/test .NET/frontend. Chưa xác minh deploy server mới, Active Setup, phát hành release hoặc cài model trên máy Windows sạch trong lượt này.
