# SQL 4.1.21 trên hosting — đã áp và xác minh

Ngày 2026-10-17, theo yêu cầu **“bạn hãy chạy file sql đó cho tôi”**. Nhánh `main`, HEAD nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit có 435 đường dẫn thay đổi trước thao tác. Không sửa source ứng dụng, cấu hình hoặc migration.

**Đã chạy đúng một lần toàn bộ `database/VideoFactory.4.1.21.StandaloneDesktopCatalogSimple.sql` và xác minh bằng kết nối mới.**

| Thông tin | Kết quả |
| --- | --- |
| SQL endpoint | `112.78.2.70` |
| Instance / database | `MB2D70\MSSQLSERVER2019 / tap51004_tool` |
| Engine / compatibility | `15.0.2000.5 / 150` |
| SHA-256 migration | `0b63286b5e394fe7f95770ea0ce360730af936a05eafa105d66a5994ad118ef2` |
| Runner xác nhận commit, UTC | `2026-10-17T09:49:14.4783223Z` |
| Hậu kiểm độc lập hoàn tất, UTC | `2026-10-17T09:49:33.2543042Z` |
| Trạng thái | `AppliedAndVerified`, sau đó `IndependentlyVerified` |

## Kiểm tra thực tế

- Trước chạy: đúng đích, ONLINE/ghi được, không có transaction ngoài; marker 4.1.18/4.1.19/4.1.20 mỗi mã một dòng, marker 4.1.21 chưa có. Catalog đủ 24 cột, 5 CHECK trusted/enabled, PK cùng 3 index; có 1 dòng catalog. Quyền ALTER catalog và INSERT SchemaVersions đầy đủ.
- Chạy nguyên SQL đã kiểm hash trong connection riêng, lock timeout 15 giây và deadlock priority LOW. SQL tự quản lý transaction; không bọc transaction ngoài, không chạy migration cũ hoặc thay quyền.
- Sau chạy và bằng kết nối mới: marker `4.1.21-standalone-desktop-catalog-simple` đúng một dòng; CHECK `CK_DesktopDirectReleases_Scope` đúng định nghĩa protocol 4/5, trusted/enabled. Các marker cũ còn nguyên; không còn transaction mở.
- Fingerprint mọi dòng của catalog và bốn bảng phát hành cũ giữ nguyên. Metadata cột, index, FK, CHECK khác, các dòng SchemaVersions cũ, quyền và membership **quan sát được bằng tài khoản chạy** giữ nguyên. Không khẳng định đã kiểm chứng toàn bộ quyền bị ẩn.
- Một tiến trình hậu kiểm riêng chỉ đọc tiếp tục xác minh đạt; không INSERT thử release hoặc protocol 5 trên hosting.

## Backup và phạm vi ủy quyền

Đọc lại được full backup set `495443`, hoàn tất `2026-10-17T07:06:50` theo đồng hồ SQL Server, không có checksum, `is_damaged=false`. Restore history nhìn thấy gần nhất thuộc backup cũ `489418` ngày 2026-09-15. Principal không có quyền BACKUP DATABASE. **Chưa độc lập xác minh restore bản backup hiện tại hoặc rehearsal trên clone hosting.**

Tiếp tục theo yêu cầu thực thi hiện tại và ủy quyền trên cùng hosting đã ghi ở các lượt 4.1.18–4.1.20. Không coi ủy quyền tiếp tục là xác nhận backup đã restore. Mười ca rehearsal SQL2019/backup/restore synthetic của lượt triển khai source là bằng chứng riêng, không thay backup hosting. Không backup/restore thật, nâng quyền, deploy server, upload file, ghi link Setup hoặc phát hành release trong lượt SQL này.

## Bằng chứng

Thư mục `D:/vm-sql-verification/20261017-164728-hosting-421-07e98a/`:

- `preflight.json`, `preflight-state.json`: đích, schema, quyền và backup/restore metadata.
- `Hosting421.ps1`, `Verify-Hosting420.sql`, `migration-exact.sql`: runner không chứa credential, bộ kiểm schema nền và exact SQL đã thực thi.
- `before.json`, `after.json`, `fresh-connection.json`, `independent-verification-state.json`: snapshot cùng fingerprint trước/sau, không xuất nội dung dữ liệu phát hành.
- `operation.json`, `verification-only.json`, `completion.json`: trạng thái thực thi và hậu kiểm độc lập.
- `preflight-initial-helper-failure.json`: lỗi adapter PowerShell 5.1 ở helper trước thay đổi SQL; đã đổi sang getter/setter ADO.NET tường minh và chạy lại preflight đạt. Không sửa migration hoặc chạy lại DDL vì lỗi helper.

Không chạy lại build/test .NET/frontend vì không đổi code hoặc SQL source. Tiếp theo deploy server đã bàn giao với cấu hình hiệu lực `StandaloneCatalog`, upload Setup/ZIP mới và tạo release. Không cần chạy lại SQL 4.1.21 cho database hosting này.
