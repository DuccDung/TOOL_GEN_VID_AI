# Kế hoạch form release tối giản và Setup dùng chung

Ngày lập: **2026-10-17**. Rà soát nhánh `main`, HEAD nền `f0651c918f28ca477f28875818148e9e86240b7b`, cùng working tree chưa commit. HEAD không đại diện đầy đủ source đang có; giữ nguyên thay đổi của người dùng.

**Kế hoạch ban đầu được lập khi chưa sửa code. Theo yêu cầu triển khai tiếp theo, source đã được bổ sung; trạng thái từng nhóm task, kiểm thử và giới hạn hiện hành nằm trong [biên bản triển khai](TRIEN_KHAI_FORM_RELEASE_TOI_GIAN_VA_SETUP_DUNG_CHUNG.md). Nội dung task/hiện trạng bên dưới giữ mốc lập kế hoạch, không tự chứng nhận mọi tiêu chí nghiệm thu đã hoàn thành. Đã áp migration 4.1.18 lên hosting ngày 2026-10-17 theo [biên bản SQL riêng](TRIEN_KHAI_SQL_4_1_18_HOSTING.md); chưa deploy ứng dụng hoặc rollout. Backup/restore và clone hosting vẫn chưa xác minh.**

Kế hoạch này thay phần thiết kế vận hành release mới trong [kế hoạch VP](PLAN_TASK_PHAT_HANH_TOOL_LOCAL_QUA_VPS_VA_SETUP.md): chuyển từ nạp JSON + Setup riêng từng release sang nhập URL + server kiểm file + Setup dùng chung. Khả năng tương thích protocol cũ vẫn phải được giữ. Không coi source VP hoặc kết quả kiểm thử trước đây là bằng chứng luồng mới đã đạt.

## 1. Yêu cầu đã chốt

### Form tạo release

| Trường | Người quản trị thao tác | Quy tắc |
|---|---|---|
| Phiên bản | Bắt buộc nhập, ví dụ `1.2.0` | Phải khớp phiên bản trong ZIP và binary, không chỉ là nhãn hiển thị |
| Link ZIP ứng dụng trên VPS | Bắt buộc nhập | Link trực tiếp, HTTPS, thuộc nguồn ứng dụng được hệ thống cho phép |
| Ghi chú phát hành | Tùy chọn | Giữ nội dung khi lưu/kiểm tra lỗi |
| Bắt buộc cập nhật | Tùy chọn, mặc định tắt | Chỉ có hiệu lực sau phát hành; giữ quy tắc cập nhật bắt buộc hiện hành |

Không yêu cầu nhập tên file, dung lượng, checksum, build number, manifest hoặc JSON đăng ký. Không chọn nguồn upload/VPS và không upload Setup trong form release mới. Kênh mặc định `Stable`, nền tảng `win-x64`; bộ model dùng policy đã được duyệt ở cấp hệ thống.

Hệ thống tự lấy tên file an toàn từ phản hồi HTTP/URL, tính dung lượng thực và SHA-256 từ **toàn bộ nội dung ZIP**, đọc build/manifest/platform/capability từ gói, đối chiếu phiên bản đã nhập. Build number lấy từ gói đã build, không tự đoán hoặc cấp một số không khớp binary.

### Setup dùng chung

- Build và đăng ký **một Setup cấp hệ thống**, trỏ API VideoMaker thật. Không build lại Setup chỉ vì ZIP có phiên bản/hash mới.
- Khi chạy, Setup hỏi API lấy release đã phát hành và tương thích, cố định snapshot cho lượt cài rồi tải/kiểm/cài ứng dụng và chuẩn bị model.
- Setup dùng chung cho nhiều ZIP TOOL-LOCAL đúng định dạng, cùng giao thức/model policy mà nó hỗ trợ. Không hứa một EXE cài mọi định dạng/model policy tương lai.
- Khi cần thay giao thức hoặc năng lực bộ cài, có thể phát hành Setup hệ thống mới; vẫn giữ file cũ cho lượt cài đang chạy và hỗ trợ vận hành.
- TOOL-LOCAL thấp phiên bản hơn bản đang phát hành thì báo cập nhật; người dùng chọn cập nhật sẽ chuyển sang Setup dùng chung. Setup/desktop chỉ truy cập thông tin phát hành qua API, không đọc trực tiếp các bảng `auth`.

## 2. Hiện trạng source và khoảng trống

| Phần | Source đã kiểm | Khoảng trống của yêu cầu mới |
|---|---|---|
| Form | [Index.cshtml](TOOL-SERVER/Pages/Admin/Index.cshtml), [admin.js](TOOL-SERVER/wwwroot/admin/admin.js), [danh sách release](TOOL-SERVER/wwwroot/admin/admin-pages.js) | Đang yêu cầu build/source, metadata thủ công/JSON và Setup từng release |
| Đăng ký external | [DesktopReleaseService.External.cs](TOOL-SERVER/Updates/DesktopReleaseService.External.cs) | Chủ ý không fetch URL; nhận metadata tự khai và bắt binding Setup riêng |
| Publish và distribution | [service](TOOL-SERVER/Updates/DesktopReleaseService.cs), [Admin controller](TOOL-SERVER/Controllers/AdminDesktopReleasesController.cs), [launcher controller](TOOL-SERVER/Controllers/LauncherDistributionController.cs) | Chưa có kiểm ZIP chạy nền và catalog Setup dùng chung |
| Schema | [4.1.17](database/VideoFactory.4.1.17.DesktopExternalArtifacts.sql), [entities](TOOL-SERVER/Domain/Updates/AppRelease.cs), hai DbContext | CHECK StaticArchive chỉ chấp nhận protocol 2; release có BuildNumber bắt buộc trước khi đọc ZIP |
| Setup | [InstallerOptions](TOOL-SETUP/InstallerOptions.cs), [LauncherInstallerService](TOOL-SETUP/LauncherInstallerService.cs), [Static](TOOL-SETUP/LauncherInstallerService.Static.cs) | Version/build/hash của ZIP đang được nhúng cứng lúc build và so exact khi cài |
| Pipeline | [Publish-DesktopRelease.ps1](scripts/Publish-DesktopRelease.ps1), [DesktopExternalRelease.psm1](scripts/DesktopExternalRelease.psm1) | Build Setup theo ZIP và xuất JSON có binding riêng |
| Update và IPC | [DesktopInstalledMaintenanceService](TOOL-LOCAL/Updates/DesktopInstalledMaintenanceService.cs), [maintenance protocol](TOOL-DISTRIBUTION/Updates/DesktopMaintenanceProtocol.cs), [handoff](TOOL-SETUP/SetupMaintenanceHandoff.cs) | Đang tải/xác minh đúng Setup của release; cần capability Setup chung và snapshot runtime |
| So sánh phiên bản | [DesktopReleaseVersionPolicy](TOOL-SHARED.Contracts/Updates/DesktopReleaseVersionPolicy.cs), [DesktopVersionComparer](TOOL-SERVER/Updates/DesktopVersionComparer.cs) | Hiện ưu tiên build trước version; parser lấy phần số bằng regex, chưa phải validation version nghiêm ngặt |
| Thông báo cập nhật | [Form1](TOOL-LOCAL/Form1.cs), [DesktopUpdateApiClient](TOOL-LOCAL/Updates/DesktopUpdateApiClient.cs) | Đã có check/thông báo/apply; cần nối đúng giao thức và thứ tự phiên bản mới |

Theo [biên bản SQL](TRIEN_KHAI_PHAT_HANH_TOOL_LOCAL_QUA_VPS_VA_SETUP.md), 4.1.17 đã áp vào `112.78.2.70 / MB2D70\MSSQLSERVER2019 / tap51004_tool`, đủ 9 cột, CHECK/index/marker và kết nối mới xác minh. Đây là bằng chứng lượt vận hành trước; không tái truy cập hosting trong lượt lập kế hoạch. Backup/restore hosting và rehearsal clone hosting vẫn chưa được biên bản đó chứng nhận. Rehearsal cũ dùng LocalDB major 17, không chứng minh SQL Server 2019 hosting.

## 3. Các quyết định triển khai

### 3.1. Phạm vi

Làm đồng bộ form, API/worker kiểm ZIP, schema, catalog Setup hệ thống, Setup runtime, pipeline và update/repair. Chỉ bỏ trường HTML sẽ không đáp ứng yêu cầu: publish hiện vẫn cần metadata/Setup riêng và binary Setup hiện vẫn khóa theo từng ZIP.

Giữ nguyên gateway/provider/credential/license/SQL workflow và bộ model `models-20261016-03`. Không đưa ZIP model vào ZIP ứng dụng, không đổi engine Qwen/Piper/OCR hoặc bỏ readiness/probe. Không phát request AI có phí trong kiểm thử mặc định.

### 3.2. Phiên bản và cập nhật

- Đề xuất cho form Stable mới: phiên bản số `major.minor.patch`, ví dụ `1.2.0`. Không cho tùy ý nhập nhãn khác phiên bản của gói. Các version legacy khác định dạng giữ nguyên dữ liệu và đường tương thích.
- Quy tắc cho luồng mới: so **Version trước**, BuildNumber là thứ tự phụ khi version bằng nhau. `1.10.0` mới hơn `1.9.0`; build lớn không được biến version thấp thành mới hơn.
- BuildNumber và version vẫn được đóng vào binary khi build. Inspector phải đối chiếu manifest và metadata version/build của binary bằng cách đọc file, không chạy EXE trên server.
- Phát hành cùng version/build/channel/platform khác bytes bị chặn. Hotfix có thể dùng cùng version nhưng build lớn hơn theo quy tắc chung; người quản trị vẫn chỉ nhập version và URL.
- Latest chọn trong tập release Active, đã đến lịch, kiểm file đạt và tương thích. Không lấy nháp, bản bị tắt hoặc bản chưa có Setup hỗ trợ.
- Đổi comparator phải đồng bộ lựa chọn latest, API check, Setup và maintenance; không thay một hàm rồi để phần còn lại vẫn sort build-first. Protocol cũ giữ hành vi đã công bố hoặc đi qua đường chuyển tiếp được kiểm riêng.

### 3.3. Giao thức và metadata

Đề xuất capability/giao thức **SharedBootstrapper, protocol 3**, tách khỏi exact Setup protocol 2. Đây là tên/số dự kiến, phải kiểm lại khi FR03 triển khai; không tăng hằng `Current` rồi ngầm đổi nghĩa mọi endpoint cũ.

Tách hai identity:

| Identity | Nội dung | Vòng đời |
|---|---|---|
| Gói ứng dụng | ReleaseId, version/build, URL, SHA-256/size, manifest, model policy/capability | Bất biến sau lần phát hành đầu |
| Setup hệ thống | BootstrapperId/version, URL nội bộ, SHA-256/size, API profile, protocol/platform/channel/model policy hỗ trợ | Upload một lần mỗi bản Setup; một bản Active phù hợp trong scope hiện tại |

Mỗi lượt cài/update/repair ghép hai identity thành snapshot đã được API xác nhận. Setup chung không phải bỏ hash, ownership hay binding; binding chuyển từ lúc build EXE sang lúc bắt đầu operation.

Một bản Setup cùng bytes/SHA phải cài được ít nhất hai ZIP khác version. Repair tiếp tục lấy **đúng release/version/build/hash đang cài**, không tự chuyển sang latest. Update lấy release mới hơn rồi giữ nguyên snapshot; không đổi bản giữa lượt tải.

### 3.4. Nháp khi chưa biết BuildNumber

Đề xuất bảng mới `auth.DesktopReleasePreparations`, gộp dữ liệu form nháp và operation kiểm file bền. Lưu Version/URL/notes/mandatory, revision, fingerprint đầu vào, trạng thái/tiến độ, lease/generation/heartbeat, số lần thử, safe error và `ReleaseId` nullable.

Không tạo AppRelease tạm với build giả `1`, không nới cột BuildNumber thành nullable làm client/EF cũ đọc lỗi. Sau khi kiểm thành công mới transaction tạo/cập nhật `AppReleases` + artifact **inactive** bằng build thật; gắn preparation với release qua khóa duy nhất khi ReleaseId khác NULL. Đây là mô hình dự kiến, chưa phải DDL đã tạo.

Notes/mandatory đổi riêng không buộc tải lại ZIP. Đổi Version/URL của nháp phải đồng thời vô hiệu bằng chứng cũ, tăng thế hệ đầu vào và giữ release inactive; worker cũ không được ghi kết quả đè lại. `FirstPublishedAtUtc` có giá trị thì identity luôn khóa, kể cả đã tắt Active.

### 3.5. Kiểm file từ URL

- Server dùng HTTP client riêng cho file ứng dụng; chỉ HTTPS đúng host/port/prefix ứng dụng được duyệt, có chủ đích hỗ trợ VPS `:8443`. Không nới policy outbound AI provider.
- Kiểm IP public và pin địa chỉ kết nối nếu dùng hostname, chặn DNS rebinding/private address; kiểm TLS bình thường. Không redirect, query token, userinfo, cookie, JWT hoặc provider credential sang VPS; không dùng proxy mặc định có credential.
- Tải stream xuống `.part`, tính SHA-256 và đếm byte thực. HEAD/Content-Length/tên đuôi `.zip` không thay việc đọc toàn file. MIME, magic bytes và ZIP thực đều phải hợp lệ.
- Giữ cap hiện hành: **2 GiB ZIP ứng dụng, 4 GiB giải nén, 200.000 entry, tên file 180 ký tự**. ZIP model riêng không chịu cap 2 GiB này. Thời gian/mức concurrency/disk budget phải cấu hình và đo trên hosting.
- Kiểm manifest duy nhất, product/version/build/platform, inventory, file bắt buộc, ZIP traversal/absolute path/duplicate/case collision/symlink và lượng giải nén thực. Không chạy file trong ZIP để “kiểm”.
- SHA-256 xác nhận client nhận đúng bytes server đã kiểm; không tự chứng nhận chất lượng phần mềm hoặc model inference.
- VPS vẫn là nơi khách tải ứng dụng. File cache kiểm tra của server có TTL và cleanup theo lease; không proxy ZIP cho mỗi máy khách.

## 4. Luồng người quản trị và trạng thái

1. Mở **Tạo release**: Phiên bản, Link ZIP, Ghi chú, Bắt buộc cập nhật.
2. Nhấn **Lưu nháp**: lưu dữ liệu và xếp hàng kiểm file, trả operation ID nhanh. Helper text nói rõ lưu nháp sẽ bắt đầu kiểm tra ZIP.
3. Hiển thị tiến độ đọc file/kiểm gói. Đóng form không hủy operation; danh sách release vẫn hiện trạng thái, mở lại tiếp tục xem.
4. Kiểm đạt: hiển thị phiên bản/build lấy từ gói, tên/dung lượng và trạng thái đã kiểm. Checksum/chi tiết kỹ thuật nằm trong phần xem thêm chỉ đọc.
5. **Phát hành** khả dụng khi dữ liệu form đã lưu khớp lần kiểm thành công và có Setup hệ thống tương thích. Bấm phát hành dùng revision hiện hành để tránh ghi đè.
6. Khách mở Setup dùng chung để cài; TOOL-LOCAL đang chạy bản thấp hơn được báo cập nhật qua API.

| Trạng thái nhìn thấy | Ý nghĩa | Hành động |
|---|---|---|
| Chưa kiểm tra / đã sửa thông tin | Chưa có bằng chứng cho Version/URL hiện tại | Lưu nháp; Phát hành khóa |
| Đang chờ | Operation đã lưu và chờ worker | Xem tiến độ, có thể yêu cầu hủy |
| Đang tải ZIP | Đọc file và tính hash; hiện byte đã nhận, phần trăm chỉ khi biết tổng | Không submit trùng; Phát hành khóa |
| Đang kiểm tra gói | Kiểm manifest/inventory/version/compatibility | Giữ dữ liệu; Phát hành khóa |
| Đã kiểm tra | Bằng chứng server sinh khớp đầu vào hiện tại | Phát hành nếu Setup hệ thống phù hợp |
| Kiểm tra lỗi | Có lỗi cụ thể: không tải được, sai version, gói không hợp lệ… | Sửa thông tin hoặc Thử lại; không tạo trùng nháp |
| Đã hủy | Worker đã dừng hoặc đã bị chặn promote | Lưu nháp/Thử lại; không giả thành Ready |
| Đã phát hành / đã tắt | AppRelease là nguồn sự thật cho trạng thái phát hành | Identity khóa; notes/mandatory/Active theo quyền hiện hành |

Job phải sống qua recycle/restart, có claim lease và fencing; không dùng Task.Run gắn vào HTTP request làm hàng đợi. Lần đầu có thể retry tải toàn file trong ngân sách giới hạn; resume server chỉ thêm nếu có nhu cầu đo được và kiểm được ETag/Range, không ghép byte từ hai bản ZIP.

## 5. Task triển khai FR01–FR32

Mọi task dưới đây **chưa thực hiện**. P0 chặn tính đúng/cài được; P1 hoàn thiện vận hành/UX nhưng vẫn cần trước bàn giao. Owner là vai trò triển khai dự kiến, không phải người đã nhận tác động production.

### A. Baseline và thiết kế

**FR01 — Chụp baseline và phạm vi thay đổi — P0; owner tích hợp; phụ thuộc: không.**

- Chụp branch/HEAD, manifest dirty source, cấu hình mặc định và đường gọi Create/Publish/Check/Setup/Repair. Phân biệt SQL 4.1.17 đã áp với feature mới chưa có.
- Đạt khi có danh mục file cần sửa, sổ yêu cầu và bằng chứng gắn đúng checkout; không mất thay đổi có sẵn.

**FR02 — Chốt form và quy tắc version — P0; owner nghiệp vụ/tích hợp; phụ thuộc FR01.**

- Áp bốn trường ở mục 1; defaults Stable/win-x64 và version-first cho luồng mới. Xác định normalization/version binary và xử lý hotfix/duplicate.
- Đạt khi có ví dụ cập nhật thấp/cao/bằng/version `1.9.0`–`1.10.0`, build đảo thứ tự và version UI không khớp ZIP; không cần người quản trị nhập build.

**FR03 — Chốt capability/giao thức và chuyển tiếp — P0; owner kiến trúc; phụ thuộc FR01–FR02.**

- Mô tả protocol 3, Setup identity tách package, supported manifest/model policy và DTO runtime snapshot. Lập ma trận protocol 1/2/3 và client/Setup cũ–mới.
- Đạt khi client cũ không nhận payload quá khả năng; quyết định có khách cũ cần bridge release hay chỉ nhận Setup mới trực tiếp. Không ép tạo cầu chuyển tiếp nếu chưa có bản cũ ngoài thực tế.

**FR04 — Kiểm đầu vào môi trường — P0; owner vận hành; phụ thuộc FR01.**

- Xác định API thật, route VPS ứng dụng, TLS, outbound :8443, scratch path/quota, RAM/disk, quyền ghi, IIS recycle/AlwaysOn hoặc worker chạy liên tục. Chốt FFmpeg Release/profile khách và WebView/OCR/native prerequisite hiện hành.
- Đạt khi source/fixture làm độc lập được, còn candidate/deploy thiếu môi trường được ghi rõ. Không giả URL API, lấy cấu hình dev hoặc đổi gate Release để build qua.

### B. Contracts và dữ liệu

**FR05 — Shared contracts trước — P0; owner Contracts; phụ thuộc FR02–FR03.**

- Thêm request preparation tối giản, response operation/progress/error, capability/bootstrapper metadata, shared release snapshot và schema/giao thức mới. Manifest ZIP mới phải mô tả model policy và capability/minimum Setup cần thiết; đồng bộ producer/consumer thay vì giả định manifest năm trường cũ đã có các thông tin đó. Không nhận SHA/Ready từ form như sự thật.
- Đạt khi serialization/legacy default tương thích; DTO chung không chứa HTTP/EF/file I/O, secret hoặc local path.

**FR06 — Migration mới và mapping — P0; owner database/server; phụ thuộc FR05.**

- Kiểm lại slot, dự kiến migration **4.1.18** cho preparations, catalog Setup chung, liên kết bằng chứng artifact và CHECK hỗ trợ protocol mới. Tại lúc lập kế hoạch chưa có file 4.1.18 trong source.
- Giữ nguyên 4.1.17 đã áp. Rà cả AccountDbContext, VideoFactoryDbContext và legacy models; giữ cột/dữ liệu/role/FK cũ. Catalog có tối đa một Setup Active trong profile/platform/channel; file và hash từng bản bất biến.
- Đạt khi schema kiểm được preparation lease/CAS, unique liên kết release và protocol 1/2/3; không cấp quyền desktop vào auth/ai. Sau đổi CHECK, dùng verifier mới, không rerun 4.1.17 làm verifier vì guard definition cũ sẽ từ chối hợp lệ.

**FR07 — Rehearsal SQL đúng engine — P0; owner database/QA; phụ thuộc FR06.**

- Trên SQL Server 2019/clone phù hợp hosting, chạy migration hai lần, fault/rollback và low privilege phù hợp; kiểm rows, version, FK, index, CHECK, quyền và backup/restore của môi trường QA.
- Đạt khi có engine/commit/hash/log rõ; không thay bằng SQLite hoặc rehearsal LocalDB major 17. Không tự chạy migration mới lên hosting trong task này.

### C. Nhập ZIP và phát hành trên server

**FR08 — API lưu nháp tối giản — P0; owner server; phụ thuộc FR05–FR06.**

- Lưu preparation trước khi biết build thật; request idempotent trả ID/revision và `202` khi xếp hàng. Không tạo release BuildNumber giả. Có GET trạng thái, retry/hủy có quyền Admin.
- Đạt khi submit lại, mất response hoặc hai tab không tạo release trùng; dữ liệu notes/mandatory được giữ. Danh sách mới gộp preparation/release không đếm hai lần.

**FR09 — Worker và hàng đợi bền — P0; owner server; phụ thuộc FR08.**

- Claim lease/fencing, heartbeat, retry hữu hạn, cancellation, recovery sau recycle; worker dùng DI scope/DbContext riêng. Khởi đầu concurrency 1, có quota toàn hệ thống.
- Đạt khi worker hết lease hoặc bị hủy không promote kết quả; server restart không mất nháp/job. Môi trường hosting phải đáp ứng vòng đời worker đã chốt ở FR04.

**FR10 — HTTP transport cho artifact — P0; owner server/security; phụ thuộc FR03–FR04.**

- Tách client và policy app URL; allowlist exact host/port/prefix, public IP/pinned connect, HTTPS/TLS, không redirect/auth/cookie/proxy credential. Không dùng provider output client hoặc nới host AI.
- Đạt khi URL nội bộ, nguồn sai, TLS sai, path mơ hồ và DNS đổi đều bị chặn trước đọc file.

**FR11 — Tải stream và tự tính checksum — P0; owner server; phụ thuộc FR09–FR10.**

- Lưu `.part` dưới root riêng, tính SHA-256 và size thực trong lúc tải; timeout header/body/stall/overall, cap byte và free-disk. Progress không bịa phần trăm khi thiếu tổng.
- Đạt khi không buffer cả ZIP vào RAM; mất mạng/file cụt/quá cap/chunked/Content-Length sai/hết disk/cancel trả safe error và không Ready. Hash chỉ chốt sau EOF hợp lệ.

**FR12 — Inspector ZIP và metadata tự động — P0; owner distribution/server; phụ thuộc FR11.**

- Tái sử dụng validator package có thể dùng chung; đọc manifest/inventory và metadata binary an toàn. Đối chiếu Version UI, build/platform/product/model policy/minimum bootstrap capability; giữ gate package/profile hiện hành.
- Đạt khi ZIP giả/HTML/encrypted hoặc sai manifest/entry/path/inventory/version bị chặn. Tên archive được làm sạch từ HTTP/URL; không cần trường tên file của người dùng.

**FR13 — Cache, lease và cleanup — P1; owner server/vận hành; phụ thuộc FR11–FR12.**

- Xác định thời hạn giữ ZIP tạm, budget, đường dẫn an toàn và cleanup job lỗi/orphan. Không xóa cache của worker đang có lease; không dùng archive name ngoài làm đường ghi tùy ý.
- Đạt khi restart/cleanup không xóa file đang kiểm, không giữ ZIP vô hạn và không tác động model storage/App_Releases cũ.

**FR14 — Commit bằng chứng và materialize release — P0; owner server; phụ thuộc FR08–FR12.**

- Transaction kiểm fingerprint Version/URL/profile, preparation generation/lease, revision và trạng thái chưa từng publish; ghi bằng chứng server sinh và tạo/cập nhật release + artifact inactive bằng build thật.
- Đạt khi sửa input trong lúc tải hoặc worker cũ xong sau worker mới không ghi đè; natural-key conflict thành lỗi nghiệp vụ, chỉ reuse khi toàn snapshot giống. Không dùng `MetadataRegisteredOnly` để gọi một file đã thực sự kiểm bytes.

**FR15 — Publish, chỉnh nháp và khóa identity — P0; owner server; phụ thuộc FR14, FR17.**

- Publish CAS chỉ nhận bằng chứng hiện hành và Setup hệ thống tương thích. Đổi Version/URL của nháp đã materialize phải invalidate bằng chứng và giữ inactive atomically; notes/mandatory đổi riêng không tải lại ZIP.
- Đạt khi hai Admin publish/sửa đồng thời không vượt gate; release đã từng phát hành khóa URL/version/build/hash dù tắt Active. Không overwrite bytes tại URL cũ; bản thay đổi dùng release/build mới.

**FR16 — Distribution và lựa chọn latest — P0; owner server; phụ thuộc FR03, FR14–FR15.**

- API protocol mới trả package và bootstrapper capability riêng; chọn latest version-first trong tập hợp hợp lệ. GET theo ID phục vụ snapshot và repair exact; nháp/unsupported/inactive chưa đến lịch không lộ như bản cài được.
- Đạt khi metadata/version selection thống nhất với Setup/desktop; protocol 1/2 giữ filtering và đường download cũ. Không biến API thành proxy ZIP VPS.

### D. Setup dùng chung, desktop và pipeline

**FR17 — Catalog Setup cấp hệ thống — P0; owner server/phát hành; phụ thuộc FR03, FR05–FR06.**

- Tạo đường quản trị riêng để đăng ký Setup một lần mỗi phiên bản, lưu file/hash/size/version/API profile/capability do build hợp lệ cung cấp; đối chiếu metadata nhúng bằng cách đọc EXE/resource an toàn, không chỉ tin metadata rời. Không nhét các thao tác này vào form release.
- Đạt khi activate dùng CAS, chỉ một Active tương thích, không chạy EXE upload trên server để lấy metadata. Setup sai API/profile/capability bị chặn dù hash file đúng; profile A không được dùng cho release profile B. Operation đang chạy vẫn dùng bootstrapper ID/hash đã snapshot; giữ file/history khi đổi Active.

**FR18 — Build Setup shared mode — P0; owner Setup; phụ thuộc FR03–FR05.**

- Thêm mode rõ ràng thay pin version/build/hash ứng dụng bằng API/profile/capability của Setup. Giữ pin nguồn file, model đã duyệt, WebView/prerequisite và chính sách chữ ký đang áp dụng.
- Đạt khi không có app version/hash baked vào shared mode; cấu hình thiếu API/capability hợp lệ bị từ chối, còn exact mode cũ vẫn chạy đúng.

**FR19 — Setup chọn và khóa snapshot runtime — P0; owner Setup; phụ thuộc FR16–FR18.**

- Cài mới lấy release hợp lệ từ API; update/repair nhận target cụ thể và xác minh lại API. Lưu snapshot release/hash/revision + bootstrapper identity cho operation; revalidate trạng thái theo policy trước mutation.
- Đạt khi publish bản mới trong lúc tải không làm đổi gói đang cài; source/capability/model không hỗ trợ thì dừng có lý do. Không nhận URL/hash tự khai từ command line làm nguồn tin cậy.

**FR20 — Tái sử dụng transaction, bootstrap và recovery — P0; owner Setup; phụ thuộc FR19.**

- Nối snapshot mới vào download/stage/backup/apply/probe/receipt/commit đang có. Giữ ownership, pending/recovery, model usage lease và gate năm thành phần bắt buộc.
- Đạt khi lỗi download/hash/probe/cancel không giả READY; rollback giữ bản cũ và file người dùng. Setup chung vẫn kiểm hash gói thực, không bỏ pin model/probe.

**FR21 — TOOL-LOCAL update/repair và IPC — P0; owner desktop/Setup; phụ thuộc FR05, FR16, FR19–FR20.**

- Dùng lại check/thông báo hiện có, gửi capability mới; lấy Setup hệ thống qua API và kiểm hash/size/version. Đồng bộ comparator server/desktop/IPC; repair đúng release đang cài.
- Giữ nonce/SID/PID/start time/expiry/ownership, ACK hợp lệ rồi desktop mới đóng; Setup chờ desktop thoát, không force-kill.
- Đạt khi version thấp báo update, version cao không báo hạ cấp; cùng version chỉ báo khi build phát hành cao hơn build đang cài, build bằng/thấp hơn không báo. Mất mạng không làm ứng dụng đóng. Cập nhật bắt buộc tiếp tục theo policy hiện hành, không tạo vòng lặp tới Setup không tương thích.

**FR22 — Tách pipeline ZIP và Setup — P0; owner phát hành; phụ thuộc FR12, FR18–FR20.**

- Một pipeline build/register Setup hệ thống; pipeline mỗi release chỉ build ZIP ứng dụng có manifest/version/build/capability đúng. Có thể giữ checksum/JSON làm evidence nội bộ nhưng form không yêu cầu nạp.
- Đạt khi build ZIP A rồi B không rebuild/rewrite Setup; profile/API/FFmpeg/OCR/WebView và secret/package gates vẫn giữ. Version trên form không thay thế việc build đúng version binary.

### E. Form và trải nghiệm vận hành

**FR23 — Form bốn trường và thông tin chỉ đọc — P0; owner Admin UI; phụ thuộc FR02, FR05, FR08, FR15.**

- Hiển thị Phiên bản, Link ZIP, Ghi chú, Bắt buộc cập nhật. Gỡ nhập JSON/source/build/platform/name/size/hash/manifest/Setup khỏi form mới; dữ liệu kỹ thuật tự sinh được xem thêm khi cần.
- Đạt khi tạo release mới chỉ cần hai trường bắt buộc; legacy release vẫn có đường xem/quản trị phù hợp, không tự chuyển source/dữ liệu cũ.

**FR24 — Lưu nháp, polling và khôi phục màn hình — P0; owner Admin UI; phụ thuộc FR08–FR09, FR14, FR23.**

- Lưu nháp enqueue, theo dõi progress theo operation ID; bấm trùng bị khóa. Đóng modal chỉ dừng polling UI, job vẫn chạy; mở lại/reload lấy trạng thái từ server.
- Đạt khi retry/mất response không tạo nháp trùng, không có progress stale từ URL cũ; Hủy kiểm tra là thao tác riêng, server xác nhận mới hiện đã hủy.

**FR25 — Lỗi, CAS và trạng thái nút — P0; owner Admin UI/server; phụ thuộc FR14–FR15, FR24.**

- Error summary đầu form liên kết field và lỗi inline; thông báo version sai nêu bản nhập/bản gói. URL hoặc Version đổi là khóa Phát hành ngay, kể cả form còn hiển thị kết quả cũ.
- Đạt khi lỗi giữ input, server conflict có hướng tải lại, thiếu Setup hệ thống có link cấu hình riêng thay vì bắt upload per-release. Publish gửi revision mới nhất sau save/inspect.

**FR26 — Keyboard, responsive và browser regression — P1; owner UI/QA; phụ thuộc FR23–FR25.**

- Giữ phong cách Admin hiện có; label thật, focus modal/error, aria-busy/live có tiết chế, nút disabled có lý do. Progress có text, không chỉ màu hoặc spinner.
- Đạt khi bàn phím, Escape/close/reopen, màn hẹp, long URL/version/notes và refresh trong mọi trạng thái dùng được. Browser fixture kiểm actual Razor/CSS/JS với API giả, không gọi VPS/DB thật.

### F. Kiểm chứng và rollout

**FR27 — Kiểm thử backend, transport và dữ liệu — P0; owner QA/server; phụ thuộc FR07–FR17.**

- Tự động hóa ma trận ở mục 6 cho URL, full stream/hash/ZIP, quyền Admin, job lease/restart/CAS, materialize/publish và SQL constraints. SQL Server kiểm riêng khác SQLite.
- Đạt khi lỗi không tạo Ready/Active giả, dữ liệu/permission cũ được giữ; ghi riêng Passed/Failed/Skipped và engine/commit. Không đếm lại cùng case nhiều vòng như test duy nhất.

**FR28 — Kiểm thử Setup, desktop, IPC và pipeline — P0; owner QA/desktop; phụ thuộc FR18–FR22.**

- Regression nhóm Updates/StaticSetup/Maintenance/Ownership/Repair/Pipeline hiện có, bổ sung protocol cũ–mới, version-first và hai ZIP cùng Setup. Cố định source/binary hash cho từng lượt.
- Đạt khi file Setup không đổi hash giữa A/B, handshake lỗi không đóng desktop và rollback không mất commit/file người dùng. Native/controlled fixture không được gọi là cài khách thật.

**FR29 — Chạy kiểm tra tích hợp đầy đủ — P0; owner tích hợp; phụ thuộc FR26–FR28.**

- Chạy lệnh chuẩn mục 7 khi triển khai source; tách kết quả frontend, browser và .NET. Chỉ chạy rộng/lặp thêm khi lỗi hoặc thay đổi mới cần kiểm, không đặt số test lịch sử thành chỉ tiêu.
- Đạt khi required checks thực sự pass trên source cuối, báo riêng skipped model/SQL và giới hạn môi trường; không hạ assertion hoặc đổi gate để xanh.

**FR30 — Nghiệm thu A/B bằng một Setup trên máy QA — P0; owner QA/vận hành; phụ thuộc FR22, FR29.**

- Dùng cùng EXE đã hash, ZIP A/B đúng gói, API/VPS staging được chỉ định. Cài A trên Windows sạch, publish B, kiểm báo update và dùng cùng Setup cài B; thử repair exact, mất mạng/restart và giữ cache model hợp lệ.
- Đạt khi app/model/probe/receipt đúng, có Qwen/Piper/OCR/media/WebView2 thực trên máy đã cài. Nếu chưa có môi trường thì ghi chưa nghiệm thu; không dùng model test skipped hoặc DB fixture làm bằng chứng thay.

**FR31 — Triển khai có kiểm soát — P0; owner vận hành; phụ thuộc FR04, FR07, FR29–FR30.**

- Rehearsal migration mới trên clone và xác minh backup/restore, rồi mới áp vào đúng DB được người dùng chỉ rõ và cho phép. Deploy server hiểu schema/giao thức mới với flag tắt, đăng ký Setup hệ thống, bật pilot, kiểm nhập URL/publish/Setup/update/repair thật.
- Đạt khi đủ bằng chứng cho từng môi trường và có owner chấp nhận. Chưa upload/publish hoặc tác động production chỉ từ việc người dùng duyệt tài liệu kế hoạch.

**FR32 — Bàn giao và rollback — P1; owner tích hợp/vận hành; phụ thuộc FR30–FR31.**

- Bàn giao đường build ZIP, upload VPS, dùng form, đăng ký Setup một lần, đọc lỗi/progress và cleanup/retention; ghi rõ nguồn API khác nguồn file VPS. Giữ EXE/ZIP/manifest/hash và metadata các bản đã phát hành.
- Khi dừng luồng mới, tắt phân phối mới/worker theo lease và giữ bản cài cũ; không xóa receipt, drop bảng, sửa CHECK ngược hoặc đổi path/hash giả. Chỉ downgrade binary đã kiểm tương thích dữ liệu mới trên clone, nếu không thì dùng forward fix.
- Đạt khi sổ task và giới hạn chứng cứ cập nhật; chưa gọi “production-ready” nếu chỉ có source/unit test hoặc thiếu smoke máy đích.

## 6. Ma trận kiểm thử bắt buộc

Các ca dưới đây là tiêu chí dự kiến, chưa chạy trong lượt lập kế hoạch.

| Nhóm | Ca kiểm | Kết quả cần đạt |
|---|---|---|
| Form | Chỉ Version + URL hợp lệ; notes trống, mandatory tắt | Lưu được nháp, không yêu cầu JSON/Setup/name/hash |
| Form | Thiếu trường, URL sai, version sai định dạng | Lỗi đúng field, summary có focus, giữ input |
| Nháp | Lưu lại sau mất response, double click, hai tab | Idempotency/CAS, không tạo trùng release |
| Nháp | Lưu lúc chưa biết build | Không tạo AppRelease build giả |
| HTTP | 404/403, TLS lỗi, HTML thay ZIP, redirect | Safe error; không Ready/Active |
| HTTP | HEAD hợp lệ nhưng body cụt/đổi, chunked không tổng | Kiểm toàn stream, không tin header thay hash |
| Policy | Host/port/prefix sai, localhost/private/DNS rebinding | Chặn trước kết nối ngoài policy |
| Resource | Quá 2 GiB, expanded quá 4 GiB, quá entry, hết disk | Dừng có giới hạn, không cạn RAM/vô hạn cleanup |
| ZIP | Traversal/absolute/duplicate/case collision/symlink | Từ chối trước promote và không ghi ngoài root |
| Manifest | UI `1.2.0`, ZIP/binary `1.1.0`; build lệch; thiếu inventory | Báo cụ thể, không publish |
| Worker | Recycle/restart, lease hết, hai worker cùng claim | Có recovery; fencing chặn stale completion |
| Worker | Hủy hoặc đổi URL/version khi worker sắp commit | Không ghi bằng chứng/Ready cho input mới |
| Mutation | Đổi notes/mandatory, URL giữ nguyên | Không bắt tải lại ZIP; revision được cập nhật |
| Publication | Hai Admin publish/sửa, trùng natural key | Conflict rõ ràng; không overwrite identity |
| Publication | Tắt release từng publish rồi sửa URL/hash | Bị khóa identity, không mở lại như draft |
| Distribution | Nháp/inactive/unsupported; client protocol 1/2/3 | Chỉ trả bản hợp lệ và tương thích |
| Version | `1.10.0`/`1.9.0`, cao/thấp/bằng, build đảo thứ tự | Version-first ở luồng mới, nhất quán mọi peer |
| Hotfix | Cùng version, build mới cao hơn/bằng/thấp hơn | Chỉ build cao hơn báo update, không loop cùng build |
| Setup chung | Cài A và B bằng cùng một EXE/SHA | Không rebuild/upload Setup mỗi release |
| Setup chung | Không có Setup Active tương thích/model policy khác | Chặn publish hoặc cài với lý do đúng, không bỏ probe |
| Setup profile | EXE hash đúng nhưng API/capability sai; Active của profile khác | Chặn đăng ký hoặc lựa chọn sai profile, không dùng lẫn |
| Snapshot | Release mới hoặc Setup Active đổi khi đang tải | Operation giữ snapshot; không tự đổi target |
| Download | ZIP VPS đổi bytes sau kiểm/publish | Client phát hiện hash lệch trước apply, giữ bản đang dùng |
| IPC | Nonce/SID/PID/starttime sai, replay, timeout | Không ACK/không đóng desktop sai |
| Repair | Bản đang cài không phải latest; thiếu proof | Chọn exact hoặc dừng rõ ràng, không âm thầm upgrade |
| Recovery | Crash/cancel/probe lỗi ở stage/apply/commit | Ownership/journal/receipt đúng, giữ file người dùng |
| Model | Windows sạch thiếu model; update giữ cache hợp lệ | Đủ readiness/probe mới hoàn tất; không tải lại chỉ vì version app đổi |
| UX | Đóng/mở modal, refresh, keyboard, màn hẹp | Tiến độ bền, không treo hoặc mất dữ liệu |
| Legacy | API exact/internal cũ và dữ liệu 4.1.17 | Hành vi cũ còn đường tương thích, không sửa lịch sử |

## 7. Lệnh kiểm tra khi có triển khai source

Chỉ liệt kê để thực hiện sau; **chưa chạy trong lượt lập kế hoạch**.

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Trong `TOOL-LOCAL/Web`:

```powershell
npm ci --no-audit --no-fund
npm run build
npm test
```

Browser Admin chạy harness hiện hành tại `TOOL-TESTS/Admin` sau khi cập nhật fixture cho API/flow mới. SQL rehearsal chạy trên engine phù hợp và DB QA riêng. Test installed model chỉ chạy khi có đúng candidate/máy được chỉ định; ghi riêng quality listening/reading so với probe kỹ thuật.

## 8. Thứ tự và điều kiện hoàn thành

Thứ tự triển khai: **FR01–FR04 → FR05–FR07 → server nhập ZIP/catalog + Setup/pipeline làm song song theo dependency → form → integration → A/B cùng Setup → rollout**. Có thể làm contract/fixture khi chưa có hosting QA, nhưng không vượt qua các gate môi trường bằng giá trị giả.

Chỉ coi yêu cầu hoàn thành khi đồng thời:

1. Tạo release mới chỉ cần nhập Version + URL, optional notes/mandatory; không hỏi JSON/Setup/metadata thủ công.
2. Server đọc đủ file, sinh bằng chứng/hash và trạng thái bền; thất bại hoặc input đổi không cho publish.
3. Cùng một file Setup cài được A/B đúng gói; Setup có cấu hình và vòng đời cấp hệ thống.
4. TOOL-LOCAL version thấp báo update đúng; update/repair/rollback giữ ownership và readiness.
5. Legacy/DB được giữ, build/test và smoke môi trường thật có bằng chứng riêng; các việc chưa kiểm được ghi rõ.

Hướng dẫn UI sử dụng skill [ui-ux-pro-max](C:/Codex/20x/skills/ui-ux-pro-max/SKILL.md): hai truy vấn local `form error summary validation` và `background task progress cancellation` trả được hướng dẫn phù hợp về error summary liên kết field, label, submit feedback và progress. Chỉ áp dụng các kết quả phù hợp; giữ design Admin hiện có, không thay toàn bộ phong cách giao diện trong đợt này.
