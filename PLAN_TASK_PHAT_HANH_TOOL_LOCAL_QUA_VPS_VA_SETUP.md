# Kế hoạch phát hành TOOL-LOCAL qua VPS và cài bằng Setup

**Cập nhật triển khai 2026-10-17:** người dùng đã giao triển khai sau khi lập kế hoạch. Source, kiểm thử và trạng thái từng phần được theo dõi riêng tại [biên bản triển khai VP](TRIEN_KHAI_PHAT_HANH_TOOL_LOCAL_QUA_VPS_VA_SETUP.md). Mô tả “chỉ đọc/lập kế hoạch” và hiện trạng bên dưới ghi thời điểm lập kế hoạch, không thay cho trạng thái triển khai mới; chưa coi toàn bộ VP01–VP32 hoàn thành.

Ngày lập: **2026-10-17**. Source rà soát: nhánh `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, cùng working tree chưa commit. Giữ nguyên thay đổi có sẵn. HEAD chưa đại diện đầy đủ source hiện hành; khi triển khai phải chụp manifest riêng.

**Lượt này chỉ đọc source và lập kế hoạch. Chưa sửa code, schema, cấu hình runtime; chưa build, chạy test/model, upload, publish release, áp SQL hoặc thay VPS.** Các task VP01–VP32 dưới đây chưa được thực hiện trong kế hoạch này. Các khả năng đã có được ghi làm nền để tái sử dụng, không gọi là kết quả triển khai mới.

## 1. Kết quả cần đạt

Người vận hành thực hiện:

1. Đóng gói `TOOL-LOCAL` thành ZIP ứng dụng đúng chuẩn phát hành.
2. Upload ZIP lên VPS đang phục vụ model, tại URL riêng có phiên bản.
3. Trong Admin VideoMaker, tạo release và đăng ký **URL ZIP ứng dụng trên VPS**, kèm metadata do pipeline tạo.
4. Phân phối Setup đúng release. Setup gọi API VideoMaker lấy metadata, tải ứng dụng trực tiếp từ VPS, tải model từ VPS, chuẩn bị runtime và chạy kiểm tra.
5. Setup chỉ báo hoàn tất sau khi ứng dụng cùng năm thành phần bắt buộc đạt và giao dịch cài đặt đã commit.

| Thành phần | Trách nhiệm sau triển khai |
|---|---|
| `TOOL-SERVER` | Quản lý release, quyền Admin, metadata và trạng thái phát hành; trả URL file đúng phiên bản |
| VPS file | Phục vụ ZIP ứng dụng và ZIP model qua HTTPS, có hỗ trợ Range; không xử lý auth/license/nghiệp vụ VideoMaker |
| Setup | Lấy metadata từ API; tải/xác minh/cài ứng dụng và model; phục hồi khi lỗi |
| Desktop | Đăng nhập/license/nghiệp vụ như hiện hành; update/repair chuyển đúng sang bộ cài tương ứng khi là StaticArchive |

**API VideoMaker và URL tải file là hai cấu hình khác nhau.** `ServerBaseUrl` phải là API ứng dụng, không thay bằng URL ZIP hoặc gốc VPS chỉ phục vụ file.

## 2. Hiện trạng đã đối chiếu source

| Phần | Hiện trạng | Khoảng trống cần xử lý |
|---|---|---|
| Tạo release | [Admin controller](TOOL-SERVER/Controllers/AdminDesktopReleasesController.cs), [request/service](TOOL-SERVER/Updates/DesktopReleaseContracts.cs) dùng metadata release và upload multipart | Chưa đăng ký artifact bằng URL VPS |
| Lưu artifact | [Entity](TOOL-SERVER/Domain/Updates/AppRelease.cs), [storage](TOOL-SERVER/Updates/DesktopReleaseStorage.cs) dùng `RelativePath`; schema hiện yêu cầu path | Chưa biểu diễn hai nguồn internal/external một cách tường minh |
| Link cho Setup | [Launcher controller](TOOL-SERVER/Controllers/LauncherDistributionController.cs) luôn trả route download API rồi đọc file storage | Chưa trả URL VPS cho package external |
| Trường URL cũ | `AppRelease.DownloadUrl` được giữ cho schema legacy | Luồng release hiện hành không sử dụng trường này; không dùng nó làm đường tắt |
| Setup StaticArchive | [Installer](TOOL-SETUP/LauncherInstallerService.Static.cs) có resume/hash/transaction/bootstrap; URL package buộc cùng HTTPS host **và port** với API | Link VPS khác host hoặc chỉ khác port đều bị chặn |
| Cấu hình Setup | [Project](TOOL-SETUP/TOOL-SETUP.csproj), [options](TOOL-SETUP/InstallerOptions.cs) nhúng URL và metadata lúc build; mặc định localhost/Bundled | Cần build đúng API và StaticArchive; sửa JSON cạnh EXE không thay cấu hình nhúng |
| Binding Setup | Setup lọc đúng version/build/SHA của package đã ghép | Giữ một Setup cho một release trong đợt này |
| Model | [Descriptor](TOOL-DISTRIBUTION/StaticModels/ApprovedStaticModelBundle.cs) đã pin ZIP VPS; bootstrap kiểm đủ năm thành phần | Tái sử dụng; chưa thay model/catalog/source pin trong dự án này |
| Update/repair | [Controller](TOOL-SERVER/Controllers/DesktopUpdatesController.cs) dùng chung mapping release; [desktop updater](TOOL-LOCAL/Updates/DesktopPackageUpdateService.cs) tải/copy/restart | Chưa có policy tải external thống nhất; copy binary không tạo lại static receipt/journal đúng bản mới |
| Phát hành | [Pipeline](scripts/Publish-DesktopRelease.ps1) có StaticArchive, binding và gate FFmpeg/profile/WebView2 | Chưa có candidate khách phù hợp được nghiệm thu; `DeliveryMode Direct` không tự tạo external release |

Nguồn kiểm tra cùng: [kiến trúc](KIEN_TRUC_KY_THUAT.md), [nghiệp vụ](NGHIEP_VU_HE_THONG_VIDEOMAKER.md), [vận hành](VAN_HANH_VA_PHAT_HANH.md), [kiểm thử](KIEM_THU_VA_NGHIEM_THU.md), [triển khai StaticArchive](TRIEN_KHAI_SETUP_CAI_MODEL_TU_VPS.md) và [hồ sơ FX](TRIEN_KHAI_KHAC_PHUC_VA_NGHIEM_THU_SETUP_MODEL.md). Kết quả test trước đây là lịch sử, không chứng minh luồng external application ZIP này đã đạt.

## 3. Phạm vi và phương án đề xuất

### 3.1. Phạm vi đợt đầu

- Bổ sung nguồn `ExternalVps` cho **ZIP ứng dụng `DesktopPackage`**; giữ nguồn upload/storage hiện có. Hai nguồn phải rõ ràng và loại trừ nhau trên cùng artifact.
- ZIP model giữ nguyên URL/pin hiện hành. Không đưa lại GGUF/Piper offline payload lớn vào ZIP ứng dụng StaticArchive.
- Setup EXE vẫn có thể phân phối bằng cơ chế hiện có của server hoặc bàn giao file. Đợt đầu chưa yêu cầu hosting Setup EXE ngoài VPS; API phải lấy được **đúng Setup của từng release** để update/repair.
- Mỗi Setup StaticArchive gắn đúng release/version/build/package SHA. Không đổi sang một Setup chung tự cài mọi phiên bản.
- Update/repair StaticArchive dùng **Setup đã ghép đúng release** để thực hiện transaction/bootstrap/receipt. Không để updater chỉ thay binary rồi bỏ qua xác minh model/app commit.
- Nghiệm thu upgrade A → B trong đợt đầu dùng cùng model bundle đã pin. Đổi bundle cần task compatibility/migration riêng vì Setup hiện kiểm bundle của receipt cũ.
- Không mở rộng Cloud, provider key, SQL desktop, catalog model động hoặc dịch vụ ký mới.

### 3.2. Phân công kiểm tra file

Server quản lý metadata; **không tự tải URL do Admin nhập**, không proxy ZIP external cho từng khách. Tránh thêm một hệ thống kiểm file từ xa vào đợt này.

| Nơi kiểm | Kiểm gì | Được kết luận |
|---|---|---|
| Pipeline trên máy build | ZIP thực, manifest, version/build/platform, entry/path/size, thành phần bắt buộc, profile, hash; kiểm Setup và binding | Package local đã qua các kiểm tra thực sự chạy |
| API đăng ký external | Quyền Admin, metadata đầy đủ, URL nằm trong policy, binding release và dữ liệu đăng ký không xung đột | Metadata hợp lệ để đăng ký; **chưa tự chứng minh byte trên VPS** |
| QA sau upload | Tải đúng URL trên VPS, kiểm toàn byte/hash và package thực tải | File tại URL đó đã được kiểm trên môi trường/thời điểm ghi rõ |
| Setup/consumer | Size/hash/MIME/magic bytes, cấu trúc ZIP và manifest thực tải trước stage/apply; policy Authenticode theo mức đã chốt; sau đó bootstrap/probe | Được cài và ghi hoàn tất khi đủ gate trên máy đích |

Pipeline xuất JSON đăng ký nhỏ để tránh gõ nhầm. JSON này **không mặc nhiên có chữ ký hoặc là attestation tin cậy độc lập**. Không nhận trường `Verified=true` tự khai để bỏ kiểm file. Với external, việc kiểm ZIP trước phát hành chuyển sang pipeline/QA và vẫn bắt buộc ở consumer; validator upload internal được giữ. Nếu sau này yêu cầu server tự kiểm byte VPS trước publish thì lập nhánh công việc riêng, không âm thầm fetch URL trong API lưu release.

### 3.3. Chính sách dữ liệu và URL

- Đề xuất mở rộng bảng `auth.AppReleaseArtifacts`: loại nguồn, URL external, install mode/protocol và metadata binding; `RelativePath` chỉ có cho internal. Migration mới phải có ràng buộc đúng một nguồn, giữ unique `(release, kind)`.
- Không ghi URL vào `RelativePath`, không phục hồi logic trường `DownloadUrl` legacy, không xóa entity/migration cũ.
- Release external và Setup đi kèm đóng băng identity/file sau lần phát hành đầu tiên. Tắt Active không tự cho phép sửa lại bytes/URL/hash. Bản mới dùng build/release mới; rollback chọn bản đã kiểm còn giữ file.
- URL ứng dụng chỉ HTTPS tại origin/path prefix do người vận hành cấu hình và được đóng gói/kiểm thống nhất cho các consumer. Origin gồm cả port; cho phép VPS `:8443` có chủ đích, không mở mọi host/port.
- Metadata API không được tự mở rộng danh sách nguồn tin cậy. Không nhận URL từ DOM/project/config người dùng để thay nguồn tải executable.
- Đợt đầu dùng URL file public ổn định, không userinfo/token/query tạm thời/fragment; không tự follow redirect. Cần redirect/CDN mới phải rà policy riêng.
- HTTP tải file tách khỏi HTTP metadata; không mang JWT/cookie/header xác thực API sang VPS.
- Đường endpoint download API cũ giữ cho internal. Với external, consumer mới dùng URL trong metadata; gọi nhầm route cũ trả lỗi có mã rõ, không biến thành proxy/redirect ngoài dự kiến.
- Rollback binary server cũ cần ẩn external release trước, vì binary cũ không hiểu nguồn external/path nullable. Feature flag không thay được migration khi EF đã đọc cột mới.

### 3.4. Kích thước và vòng đời bản cài

| Phần | Nền hiện tại / quyết định cần áp dụng |
|---|---|
| Upload internal | `DesktopReleaseOptions.MaximumArtifactBytes` mặc định 2 GiB, dùng cả cho ZIP nén và tổng giải nén trong storage; giữ hành vi hiện có |
| Static Setup | Gate hiện có 4 GiB cho package/tổng giải nén, tối đa 200.000 entry |
| Updater hiện có | Tổng giải nén 4 GiB/200.000 entry; cần bổ sung kiểm download sớm/policy, không chỉ kiểm sau tải |
| External app MVP | Đề xuất ZIP nén tối đa 2 GiB, tổng giải nén tối đa 4 GiB/200.000 entry; kiểm với mọi consumer và thực tế package trước chốt. Không nới chỉ để build qua |
| ZIP model | File riêng **2.847.980.994 byte**, không áp nhầm cap 2 GiB của ZIP ứng dụng |
| Disk | Tính theo từng volume: package/stage/backup/model archive/payload/runtime/TEMP/evidence; dung lượng download không phải tổng free disk cần |

Không tự nâng cap upload server bằng việc chọn external. Dùng byte kiểu 64 bit xuyên contract/service/downloader; không ép sang Int32 ở mốc 2 GiB.

## 4. Đầu vào cần có

| Đầu vào | Cần chốt | Có thể làm độc lập khi chưa có |
|---|---|---|
| API VideoMaker | URL HTTPS thật, staging trước; phiên bản server và quyền deploy | Source/contract/fixture; chưa build Setup khách trỏ địa chỉ giả |
| VPS ứng dụng | Origin, port, URL prefix, nơi upload và chủ thể cấu hình | Validator/transport fixture; chưa suy ra Nginx đã có route ứng dụng |
| Release | Version/build/channel/platform; gói A và B cùng bundle để thử upgrade | Cơ chế đăng ký và test tổng hợp |
| Profile khách | Server/Update/channel, workflow SQL chuyển tiếp tối thiểu theo validator, flags | Không lấy nguyên appsettings dev; chưa đóng gói credential không được phép |
| FFmpeg/prerequisite | Hồ sơ FFmpeg Release; OCR/native/WebView2 approved | Giữ gate; không đổi chữ Development để vượt kiểm |
| Ký EXE | Chính sách Authenticode đang áp dụng; certificate reference nếu bắt buộc | Không tự tạo certificate hoặc bắt mua chứng thư khi chưa yêu cầu |
| Database | Instance/database đích, schema thực, backup và khả năng restore | Viết/rehearsal migration trên DB QA; không tự áp DB thật |
| Máy QA | Windows x64 sạch, user thường, tài nguyên/volume, quyền snapshot/reset | Test fixture; không xóa model của tài khoản đang làm việc |
| Nghiệm thu | Tài khoản/license staging, fixture Anh/Trung, người nghe/đọc | Kiểm định dạng trước; chưa chứng nhận output nghiệp vụ/chất lượng |

Gợi ý đường dẫn file, **chưa phải cấu hình VPS đã áp**: `https://103.179.190.182:8443/apps/videomaker/<version>-<build>/VideoMaker-<version>-<build>-win-x64.zip`. Thư mục `models` đang có giữ nguyên. Đặt package mới dưới version mới, không ghi đè URL có người đang tải/cài.

## 5. Task triển khai

P0 chặn tính đúng/cài đặt/tương thích; P1 là hoàn thiện vận hành/trải nghiệm nhưng vẫn cần trước bàn giao. Owner là vai trò dự kiến. Mỗi task phải có source/hash, log và kết quả riêng khi thực hiện; không hard-code số test lịch sử làm mục tiêu.

### A. Baseline và thiết kế

**VP01 — Chốt baseline và sổ khoảng trống — P0; owner: tích hợp; phụ thuộc: không.**

- Chụp branch/HEAD, dirty source manifest, toolchain, cấu hình mặc định an toàn; giữ thay đổi không liên quan. Kiểm lại các đường Create/Upload/Map/Setup/update/repair thay vì dựa output build cũ.
- Phân loại: source đã có, lỗi tái hiện, thay đổi tính năng, test chưa có môi trường. Liên kết các việc còn thiếu của kế hoạch FX; không coi toàn bộ FX đã xong.
- Đạt khi từng artifact/test sau này nối được về đúng source và các blocker bên ngoài có owner.

**VP02 — Chốt đầu vào và phạm vi môi trường — P0; owner: phát hành/vận hành; phụ thuộc: VP01.**

- Điền bảng mục 4; xác định API khác VPS file, exact origin/path prefix, profile khách, release A/B, DB QA và máy sạch.
- Ghi rõ quyền tác động staging/production, không để secret trong Markdown/Git/log. Xác định root QA và ngân sách RAM/commit/disk trước test nặng.
- Đạt khi phần source độc lập có thể làm tiếp; task cài/deploy thiếu đầu vào ghi Blocked đúng chỗ.

**VP03 — Chốt thiết kế external và maintenance — P0; owner: tích hợp; phụ thuộc: VP01.**

- Ghi quyết định mục 3: external app ZIP, per-release Setup, trusted Admin registration, kiểm ZIP tại build/QA/client, không server fetch; internal tiếp tục hoạt động.
- Chốt state publish/immutability, protocol tương thích và handoff StaticArchive sang exact Setup. Ghi bảng giới hạn thực của từng consumer, không hiểu `Direct` hiện có là external delivery.
- Đạt khi không còn vòng phụ thuộc build/hash hoặc đường update bypass receipt; thay đổi phương án phải cập nhật task/test trước code.

### B. Contract, dữ liệu và release API

**VP04 — Contract dùng chung và tương thích JSON — P0; owner: contracts; phụ thuộc: VP03.**

- Sửa `TOOL-SHARED.Contracts/Updates` trước: metadata nguồn, install mode, distribution protocol và artifact Setup đúng release. Tên field/type chốt khi triển khai, thêm optional ở cuối khi tương thích được.
- Request đăng ký có URL, filename, size/SHA và identity release/package; response Admin phân biệt metadata đăng ký với file đã QA. Không trả absolute storage path/credential.
- Đạt khi server/desktop/Setup/frontend/test cùng đọc đúng; JSON cũ còn xử lý được theo policy legacy, giá trị mode/protocol lạ bị từ chối an toàn.

**VP05 — Migration additive và mapping EF — P0; owner: database/server; phụ thuộc: VP03–VP04.**

- Tạo migration idempotent version mới chưa trùng trong repository; không sửa `Initial.sql` hoặc migration lịch sử để thay cho nâng cấp DB đang dùng. Rà cả `AccountDbContext` và mapping legacy `VideoFactoryDbContext` có liên quan.
- Bổ sung nguồn/path/URL/identity/protocol theo thiết kế; CHECK đúng một nguồn, kích thước/hash hợp lệ, unique release/kind; backfill internal giữ nguyên bytes/hash/visibility. Không đoán install mode của artifact cũ chưa kiểm.
- Đạt khi DB QA chạy hai lần an toàn, dữ liệu cũ nguyên vẹn, schema sai dở được phát hiện; có runbook rollback binary với external ẩn trước. Không mở quyền schema `auth` cho desktop.

**VP06 — Policy nguồn tải dùng thống nhất — P0; owner: distribution; phụ thuộc: VP03–VP04.**

- Chuẩn hóa/kiểm HTTPS, exact host+port+path prefix, URI tuyệt đối/tương đối đúng ngữ cảnh; tách policy file artifact khỏi policy provider AI. Server options mặc định chưa cho external khi chưa cấu hình.
- Chốt cách neo policy vào desktop/Setup qua pipeline; user config/response API không tự thêm nguồn. Không URL userinfo, query bí mật, fragment, path traversal hoặc redirect không duyệt.
- Đạt khi API origin khác VPS origin được chấp nhận đúng whitelist, cùng host khác port được kiểm chính xác, nguồn ngoài danh sách bị chặn trước network.

**VP07 — Lifecycle release và chống thay identity — P0; owner: server; phụ thuộc: VP04–VP06.**

- Tách lưu draft khỏi publish; chỉ visible khi đủ metadata, artifact phù hợp, lịch phát hành và mode/protocol tương thích. Mất package/Setup bắt buộc không được Active giả.
- External package/Setup sau lần publish đầu đóng băng version/build/channel/platform/URL/size/hash/binding; tắt Active không mở khóa sửa. CAS/transaction bảo vệ hai Admin publish/replace đồng thời.
- Đạt khi create/update/upload/register đều chịu rule server, không chỉ UI; sửa nội dung cần release mới. Không vô tình thay policy toàn bộ release legacy ngoài phạm vi đã chốt.

**VP08 — Đăng ký external artifact — P0; owner: server; phụ thuộc: VP05–VP07.**

- Thêm operation/API Admin để đăng ký ZIP ngoài bằng metadata hoặc file JSON pipeline; giữ endpoint upload internal và giới hạn hiện tại. Kiểm Admin hiện hành, dữ liệu đầy đủ và package/release binding.
- Retry cùng identity trả kết quả hiện có; dữ liệu khác xung đột không ghi đè bản đang phục vụ. Không HTTP/DNS outbound trong lưu metadata; không nhận cờ tự khai để đánh dấu kiểm ZIP.
- Đạt khi server phân biệt được nguồn, CRUD/delete không gọi storage với URL/null và không xóa bất kỳ file VPS nào.

**VP09 — Mapping và truy vấn distribution — P0; owner: server; phụ thuộc: VP07–VP08.**

- Sửa latest/versions/check/repair trả external URL cho external, route tương đối cho internal; luôn cùng filename/size/hash/mode/protocol đã snapshot.
- Thêm metadata/endpoint lấy **Setup đúng releaseId/version/build** với size/hash/binding; `setup/latest` không thay được repair exact. Route download package cũ không proxy external.
- Đạt khi filter Active/lịch/channel/platform/capability diễn ra trước chọn latest; release không đủ dữ liệu không che mất bản đủ điều kiện cũ. Repair không fallback khác version.

### C. Admin, đóng gói và file VPS

**VP10 — Admin UI đăng ký link VPS — P1; owner: Admin UI; phụ thuộc: VP04, VP08.**

- Form nguồn gói ứng dụng gồm upload nội bộ hoặc link VPS; cho nạp JSON do build xuất và xem version/file/size/hash/mode. Backend từ chối payload khai hai nguồn dù UI đã khóa lựa chọn.
- Phân biệt Lưu nháp/Phát hành, Metadata hợp lệ/Đã kiểm file trên môi trường QA. Field lỗi đặt đúng vị trí, giữ nội dung khi lỗi/retry, chống submit trùng và render URL/filename như text.
- Đạt khi keyboard/focus/busy/error/cancel/edit hoạt động; trạng thái không giả Verified/Active; bảng release hiển thị nguồn và sự thiếu artifact rõ ràng.

**VP11 — Pipeline tạo bộ đăng ký external — P0; owner: build/release; phụ thuộc: VP03, VP04, VP06.**

- Dùng publish chuẩn: app ZIP có manifest → tính size/SHA → build Setup ghép đúng package → xuất JSON đăng ký và checksum bàn giao. Kiểm cả metadata thực của Setup trên máy build, không chạy EXE upload trên server để lấy metadata.
- Giữ gate profile khách, FFmpeg Release, OCR/MSVC/worker/WebView2 và policy Authenticode đã chốt. Profile public thiếu API thật hoặc còn loopback bị báo rõ; không âm thầm dùng mặc định localhost.
- JSON đăng ký nằm ngoài app ZIP; không đưa hash Setup sinh sau vào ZIP để tạo vòng băm. Không secrets/venv/cache/READY; không gọi script publish là đã upload/phát hành.
- Đạt khi đổi một byte package hoặc version/build làm binding sai bị bắt, output mới không ghi đè release cũ và bản build lỗi không sinh bộ bàn giao được coi là đạt.

**VP12 — Bố trí và upload file VPS — P0; owner: vận hành; phụ thuộc: VP02, VP27.**

- Tạo prefix ứng dụng riêng bên cạnh prefix model, URL bất biến theo release. Upload bằng kênh đã được chỉ định; hash tại VPS sau upload rồi mới dùng link đăng ký.
- Kiểm HTTPS trực tiếp, MIME ZIP hoặc kiểu nhị phân được policy cho phép, Content-Length, strong ETag/Range/If-Range, quyền đọc và dung lượng. Không thay container/site Ghepgo ngoài cấu hình file đã được giao.
- Đạt khi file ZIP ứng dụng thực tải toàn bộ khớp hash, mẫu Range khớp, không redirect ra origin khác; giữ model endpoint và file cũ. Metadata đúng chưa tự làm task này Passed.

**VP13 — Kiểm package và bằng chứng trước publish — P0; owner: QA/build; phụ thuộc: VP11–VP12.**

- Kiểm ZIP local và ZIP tải từ URL có cùng hash; manifest product/version/build/platform/filelist khớp release, có EXE/worker/OCR/media/prerequisite/notice được yêu cầu.
- Thử ZIP traversal, duplicate, symlink/reparse, stream lỗi, entry/expanded-size quá giới hạn; các consumer thực tải đều phải kiểm trước apply. File app đúng không tự chứng nhận model đã chạy.
- Đạt khi có hồ sơ file trên VPS và binding package/Setup/model; server chỉ ghi đúng mức metadata, không nâng JSON tự khai thành kiểm file thực.

### D. Setup, transport và maintenance

**VP14 — Setup nhận nguồn VPS đã duyệt — P0; owner: Setup; phụ thuộc: VP04, VP06, VP09.**

- Thay rule buộc URL package cùng API origin bằng policy có hai nguồn hợp lệ; giữ exact version/build/channel/platform/hash và model pin ghép trong Setup.
- Dùng metadata client riêng; trình tải file không mang JWT/cookie, không follow redirect. Kiểm policy trước tạo network request; URL model không bị trộn với URL ứng dụng.
- Đạt khi API ở host/cổng A và app ZIP ở VPS B được tải đúng; nguồn ngoài policy bị từ chối, không fallback localhost/bundled/upstream model.

**VP15 — Transport và snapshot tải ứng dụng — P0; owner: distribution/Setup; phụ thuộc: VP06, VP14.**

- Giữ/reuse downloader hiện có khi phù hợp; thống nhất cho những consumer cần tải artifact. `.part`, receipt URL+hash+size+ETag, full hash trước promote; bound byte sớm, signature/MIME phù hợp và timeout header/body/cancel.
- Test 200/206/416, offset/Content-Range sai, ETag yếu/đổi, server không Range, chunked/no length, 404/429/5xx, mất mạng/disk. Không append dữ liệu khi không chứng minh cùng artifact.
- Đạt khi retry/resume không đổi release giữa chừng, hash sai không apply, byte vượt cap dừng ngay; giữ cache hợp lệ và cleanup đúng owner.

**VP16 — Setup UX kết nối và cài đặt — P1; owner: Setup UI; phụ thuộc: VP14–VP15.**

- Tách lỗi không kết nối API, không có bản phù hợp, sai binding, VPS trả lỗi, checksum sai, thiếu disk/RAM/prerequisite và model probe lỗi. Có hành động Thử lại tương ứng.
- Giữ tiến độ theo phase không lùi, cancel/busy, nội dung model và cảnh báo tài nguyên. Static Setup chỉ thấy release đã ghép; không giả dropdown chọn được mọi phiên bản.
- Có đường mở Setup exact độc lập để sửa bản cài không khởi động được; nhận diện bản cài từ ownership/commit hợp lệ, không phụ thuộc token/PID của desktop đang chạy. Hiển thị rõ chế độ sửa và thư mục đích.
- Đạt khi không báo hoàn tất/launch ở lỗi; bật/tắt launch đều phải cài và kiểm model đầy đủ; ảnh native/keyboard/DPI có bằng chứng đúng môi trường.

**VP17 — Maintenance handoff tới đúng Setup — P0; owner: desktop/Setup; phụ thuộc: VP04, VP09, VP11, VP14.**

- Thêm handoff/CLI bounded cho update/repair: release/package/Setup binding, target ownership, operation, app PID+start time và lựa chọn mở lại. Không mở đường nhận model/executable/target tùy ý từ DOM.
- Desktop xác minh Setup artifact, khởi động và nhận ACK hợp lệ rồi mới đóng. Setup chờ đúng app thoát/lease được nhả trước gọi engine; launch process thành công chưa phải ACK. Không force-kill app/người dùng.
- ACK phải khớp operation/nonce, exact release/package SHA, target, Setup PID/start-time, hạn dùng và cùng Windows user; ACK cũ/giả/khác target không làm app đóng. Chọn IPC/ACL phù hợp tại VP03, không dùng file xác nhận chung không có ownership.
- App đóng sau ACK là chuyển giao thành công: lifetime/job của desktop không được giết Setup. Bootstrap con vẫn thuộc Setup; timeout/hủy/lỗi phải có đường khôi phục và không orphan.
- Giữ user/AppData của bản cài; nếu prerequisite helper cần elevation thì tách helper, không đổi user của toàn bộ engine/runtime. Chế độ Setup chạy độc lập không yêu cầu handshake/PID desktop không tồn tại.
- Đạt khi handoff thành công/fail-before-ACK/app chưa thoát đều cho trạng thái đúng và không mất bản đang dùng.

**VP18 — Route update/repair theo install mode — P0; owner: desktop; phụ thuộc: VP15, VP17.**

- StaticArchive dùng exact Setup đã ghép; internal/Bundled/Server theo phạm vi tương thích đã kiểm. Không chọn `setup/latest`, updater copy thuần hoặc URL khác khi thiếu Setup exact.
- Update chọn bản mới đủ điều kiện; repair chọn đúng version/build đang cài. Repair đọc ownership/commit receipt bounded nhưng không yêu cầu toàn runtime READY trước khi được sửa; receipt hỏng không được tự giả lập.
- Khi app không mở được, hướng dẫn tải/chạy Setup exact độc lập để sửa tại root có ownership hợp lệ. Receipt ownership hỏng thì dừng và hướng dẫn hỗ trợ/phục hồi hoặc cài root mới; không xóa/giả receipt để ép repair.
- Đạt khi thiếu/inactive/hash sai Setup trả lỗi rõ và giữ app; update/repair không mutate credential/profile/workspace để vượt gate.

**VP19 — Receipt/transaction sau update và repair — P0; owner: Setup/runtime; phụ thuộc: VP17–VP18.**

- Dùng transaction app/model hiện có; kiểm application fingerprint/MVID/version/build, model receipt schema, journal Committed và pending thuộc đúng operation sau cài lại/upgrade.
- Với cùng bundle và cache hợp lệ: chỉ reuse sau xác minh; re-probe theo policy, không chép READY cũ sang binary mới. Lỗi/cancel/crash rollback file được quản lý và giữ gate đúng.
- Đạt khi A → B mở app bình thường sau commit, repair cùng version xử lý file hỏng, unmanaged data/config/manual cue còn nguyên; bundle khác bị báo không tương thích, không xóa metadata để qua gate.

**VP20 — Protocol và tương thích bản cũ — P0; owner: server/desktop/Setup; phụ thuộc: VP04, VP09, VP18–VP19.**

- Consumer mới quảng bá distribution protocol/capability; server chỉ cấp external/Static maintenance cho consumer hiểu nó. Không dùng `MinimumSupportedVersion` như bằng chứng capability.
- Old client không gửi capability vẫn thấy legacy phù hợp; latest phải chọn trong các bản client hỗ trợ. Client mới gặp mode/protocol/binding lạ fail rõ, không fallback updater cũ.
- Có đường chuyển người dùng cũ: tải/chạy Setup protocol mới đúng release bằng kênh phân phối đã kiểm. Không đưa external release vào bare updater cũ để bootstrap. Bản legacy thiếu receipt tương thích dùng root mới hoặc migration riêng bảo toàn dữ liệu.
- Đạt khi có ma trận old/new server × old/new Setup/desktop, repair exact và inactive/future release; chưa bật external Stable trước khi đường update/repair và nâng cấp client an toàn.

### E. Kiểm thử theo ranh giới

**VP21 — Regression contract/server/schema — P0; owner: QA server; phụ thuộc: VP04–VP09, VP20.**

- Kiểm serialize legacy/new, URL/size/hash/source ambiguity, RBAC Admin, duplicate/retry/concurrency, freeze sau publish và atomic visibility. Metadata-save không được gây HTTP outbound.
- Rehearsal migration hai lần trên clone QA có internal data; CHECK/unique/EF mapping và delete không đụng file VPS. Kiểm lọc latest/versions/check/repair/exact Setup theo protocol.
- Đạt khi test thực chạy trên snapshot xác định; SQL opt-in bị skip không được ghi migration đã nghiệm thu.

**VP22 — Regression Admin UI — P1; owner: QA UI; phụ thuộc: VP10, VP21.**

- Browser thật kiểm hai nguồn, nạp metadata JSON, validate field, form giữ dữ liệu khi lỗi, submit kép, publish khi thiếu artifact, readonly sau publish và phản hồi API conflict.
- Kiểm keyboard/focus, nguồn tải dài, URL được encode/render an toàn, kích thước màn hình và thông báo trạng thái. Không gọi provider hoặc POST vào production khi automation.
- Đạt khi có test/assertion/bằng chứng UI; test giả lập không thay thao tác Admin trên staging ở VP28.

**VP23 — Regression transport/policy — P0; owner: QA distribution; phụ thuộc: VP06, VP15.**

- Dùng HTTP/TLS fixture cô lập: allowed origin khác port, redirect, source ngoài policy, sai MIME/ZIP/hash/length, stall/reset/ETag/Range, overflow byte/cap và resume snapshot.
- Chứng minh Authorization/cookie không xuất hiện ở file host; invalid URL bị chặn trước request. Tách app ZIP dưới cap và model ZIP vượt 2 GiB để không dùng nhầm chính sách.
- Đạt khi tests meaningful và không cần phá mạng/VPS đang phục vụ thật.

**VP24 — Regression Setup và maintenance — P0; owner: QA desktop; phụ thuộc: VP16–VP20.**

- WinForms/bridge/host fixture kiểm lỗi API/VPS, retry/cancel, ACK/handoff, PID reuse/start time, app chưa thoát, Setup exact thiếu/sai, parent chết, job lifetime và child treo.
- Thử ACK cũ/giả/khác nonce/target/user/Setup PID, và repair bằng Setup độc lập khi desktop không thể khởi động; không áp requirement PID/handoff vào đường độc lập.
- Kiểm transaction crash trước/sau apply/probe/commit/pending, gate React+C#, dữ liệu bảo toàn, nguồn internal/external cùng hoạt động. Mỗi fault chỉ tác động root/process của ca QA.
- Đạt khi không READY giả/orphan, cây process độc lập còn sống; không gộp helper fixture thành cài Setup khách.

**VP25 — Kiểm môi trường và độ ổn định — P0; owner: QA tích hợp; phụ thuộc: VP21–VP24.**

- Preflight từng volume, RAM/commit/pagefile; chạy riêng build, native, vòng lặp. Unicode TEMP/path, disk đầy/quota/ACL, hai Setup/app đang dùng runtime và cache hợp lệ/hỏng.
- Cập nhật profile ổn định để bao gồm các test mới phù hợp; 20 vòng sau source cuối, hash source/binary/case identity và process ownership không đổi. Browser/native/SQL/model opt-in báo riêng.
- Đạt khi không Failed bất thường/timeout/orphan, không hạ assertion/gate hoặc xóa model thật để làm đạt; không chốt số lượng bài trước discovery.

**VP26 — Nghiệm thu runtime thật và output — P0; owner: QA model; phụ thuộc: VP19, VP29 và tài nguyên phù hợp.**

- Trên chính bản cài: Qwen CPU 3 cold start × 30 lượt Anh/Trung, memory gate hiện hành <256 MiB theo cửa sổ warm-up; Piper 30 WAV và nghe mẫu; OCR/video Anh/Trung, SRT/MP4 cùng cue manual/locked.
- Dùng [harness installed](scripts/Test-InstalledStaticModel.ps1), không copy model dev/venv/READY. Chỉ truyền consent/profile sau khi người vận hành đã chấp nhận đúng cảnh báo.
- Đạt khi đủ bằng chứng file/runtime/nghe/đọc; soak 4 giờ của kế hoạch FX32 và các fault/ma trận FX chưa đạt vẫn phải hoàn thành nếu muốn tuyên bố nghiệm thu toàn bộ bộ chính. Không lấy bài Skipped làm Passed.

### F. Candidate, staging và bàn giao

**VP27 — Build candidate đóng băng — P0; owner: phát hành; phụ thuộc: VP11, VP20–VP24, gate/profile ở VP02.**

- Restore/build/frontend theo AGENTS, publish StaticArchive đúng API/origin/model/version/build; app ZIP và Setup có hash/binding, nguồn/license/notice, policy ký đúng yêu cầu.
- Giải nén từ chính ZIP cuối để kiểm và ghi inventory. Nếu source đổi sau candidate: build/hash lại, kiểm lại phạm vi ảnh hưởng; không trộn A/B hoặc diagnostic với candidate khách.
- Đạt khi có bộ A/B cùng bundle và file đăng ký tương ứng; FFmpeg còn Development hoặc profile không hợp lệ thì Blocked, không tự bỏ gate.

**VP28 — Rehearsal staging và đăng ký release — P0; owner: vận hành/QA; phụ thuộc: VP02, VP05, VP09–VP13, VP27.**

- Xác minh DB QA/backup/restore; áp migration lên đúng môi trường được giao trước binary mới. Deploy API/Admin mới với external chưa public; kiểm health/schema/internal trước.
- Upload ZIP immutable lên VPS, kiểm hash tại VPS và full GET; tạo draft bằng Admin, đăng ký link+JSON, upload đúng Setup artifact và kiểm metadata API. Không tự publish production.
- Đạt khi API trả đúng external URL và binding, internal còn hoạt động, old consumer không nhận artifact không hiểu; lỗi ở bước nào giữ draft không public.

**VP29 — Cài sạch, upgrade và repair qua UI thật — P0; owner: QA Windows; phụ thuộc: VP25, VP27–VP28.**

- Windows x64 sạch/user thường: Setup cold từ API staging → app ZIP VPS → model ZIP VPS → runtime/probe → commit → mở app; kiểm cả không launch sau cài. Theo dõi request thực để phân biệt metadata API và byte tải VPS.
- Thử path/volume/nguồn mạng/cache, restart/hủy/retry và gate; cài A → update B bằng exact Setup, repair B hỏng app/component, restart lại. Không chỉ chạy CLI hoặc fixture.
- Làm hỏng bản QA để desktop không khởi động, rồi repair bằng Setup exact độc lập; kiểm thêm đường chuyển từ client protocol cũ bằng Setup mới, không qua updater không tương thích.
- Đạt khi receipt/journal/fingerprint/runtime và dữ liệu khớp trên cùng candidate; có lỗi là tái hiện/sửa/rebuild/retest, không đánh dấu nghiệm thu từ các bài tự động đã đạt.

**VP30 — Chốt hồi quy cuối và ma trận bằng chứng — P0; owner: tích hợp/QA; phụ thuộc: VP21–VP29, VP26 theo phạm vi nghiệm thu.**

- Chạy các kiểm tra bắt buộc sau source cuối; giữ mọi log fail/retest, manifest/hash, môi trường và test identity. Không cộng vòng lặp thành số test duy nhất.
- Đối chiếu AC ở mục 6 và FX còn mở; báo Passed/Failed/Skipped/Blocked/Unverified riêng. Test source, smoke staging, installed model và rollout là bốn mức khác nhau.
- Đạt khi không lỗi P0/P1 ở phạm vi bàn giao, source/candidate thống nhất; nếu chưa môi trường thì chỉ bàn giao source/công cụ và nêu rõ chưa là bộ cài production đã nghiệm thu.

**VP31 — Rollout có kiểm soát và phương án quay lại — P0; owner: vận hành; phụ thuộc: VP28–VP30 và môi trường được chỉ định.**

- Thứ tự: schema → toàn bộ API/Admin instances mới, flag tắt → consumer/Setup mới được kiểm → release external pilot → Stable. Không bật trên mixed old/new API instances.
- Giữ release/file A cho Setup cũ và repair exact; latest chọn B bằng thứ tự version/build hiện hành, không deactivate A chỉ để đẩy B lên đầu.
- Cung cấp đường tải/chạy Setup mới thủ công cho client cũ chưa có capability; kiểm đúng root/receipt. Không coi việc ẩn release với client cũ là đã hoàn tất chuyển khách sang protocol mới.
- Khi lỗi: ngừng phân phối B, giữ byte/cache/URL bất biến, chuyển về bản phù hợp đã kiểm; ẩn external trước downgrade server cũ, không xóa cột/dữ liệu để rollback nhanh. Đã cài B quay về A phải có thao tác/candidate rollback được kiểm riêng.
- Đạt khi pilot thực có biên bản; chưa có đối tượng/môi trường cho phép thì task này chưa thực hiện.

**VP32 — Hồ sơ và runbook bàn giao — P1; owner: tích hợp/hỗ trợ; phụ thuộc: VP30; ghi rollout thực sau VP31.**

- Bàn giao source/change list, migration, gói app/Setup/JSON/checksum, policy origin/API, ma trận AC, bug register, file/runtime report và trạng thái release.
- Runbook: build → upload → kiểm hash → tạo draft/register → publish → tải Setup → cài/update/repair; xử lý localhost/không có phiên bản/404/hash/Range/disk/RAM/receipt và cách giữ evidence.
- Đạt khi người vận hành thực hiện được đúng trình tự mà không cần sửa DB tay, chép URL vào legacy field, xóa receipt/READY hoặc copy appsettings dev; mô tả rõ việc đã rollout và chưa rollout.

## 6. Ma trận nghiệm thu

Các hàng là tình huống/tổ hợp, không phải số test unit cố định. Fixture không thay bằng chứng từ môi trường đích.

| Mã | Ca bắt buộc | Điều kiện đạt | Task |
|---|---|---|---|
| AC01 | Tạo external release từ JSON build | Metadata đúng, không outbound fetch, chưa đủ artifact thì không public | VP08–VP11 |
| AC02 | Internal cũ + external mới | Cả hai source phân biệt, đúng URL, không gọi local storage với external | VP05, VP08–VP09 |
| AC03 | URL sai origin/port/path, HTTP, userinfo/query/fragment | Từ chối trước network; không lộ credential | VP06, VP14–VP15 |
| AC04 | API HTTPS khác host/port với VPS :8443 | Metadata từ API, file byte từ VPS; URL model giữ đúng pin | VP14, VP28–VP29 |
| AC05 | ZIP sai version/build/hash/size/manifest | Không apply/launch/ghi READY hoặc Active giả | VP07, VP13–VP15 |
| AC06 | File giả ZIP, traversal/duplicate/symlink/oversize | Dừng trước ghi ngoài root hoặc thay bản đang dùng | VP13, VP23–VP24 |
| AC07 | Mất mạng, ETag đổi/yếu, Range sai/416/server không Range | Resume đúng hoặc restart an toàn; final hash đúng | VP15, VP23 |
| AC08 | Metadata thay/release mới xuất hiện khi đang tải | Operation giữ snapshot đã chọn; không trộn file hoặc version | VP07, VP15 |
| AC09 | 404/429/5xx/stall/TLS/redirect | Retry có giới hạn, thông báo đúng, không bypass TLS hoặc đổi nguồn | VP15–VP16 |
| AC10 | Thiếu từng volume, quota/ACL, RAM thấp | Preflight theo root thực, không hạ gate; consent đúng profile | VP25, VP29 |
| AC11 | Lưu draft/publish kép/sửa release đã phát hành | Không visible nửa chừng, idempotent/CAS, identity không đổi | VP07–VP08, VP21–VP22 |
| AC12 | Publish trong tương lai/inactive/channel/platform khác | Latest/versions/check/repair lọc đúng; còn tìm được bản hợp lệ cũ | VP09, VP20–VP21 |
| AC13 | Cài sạch, launch bật/tắt | Đủ năm thành phần và app/model commit; launch chỉ sau hoàn tất | VP24, VP29 |
| AC14 | Cancel/crash trước/sau apply/probe/commit | Phục hồi đúng, pending/receipt/journal nhất quán, unmanaged data còn nguyên | VP19, VP24–VP29 |
| AC15 | Handoff ACK lỗi/cũ/giả/khác nonce-target-user, app không thoát/PID thay | Desktop không đóng sớm; Setup chờ đúng owner, không force-kill | VP17, VP24 |
| AC16 | Desktop thoát sau ACK, bootstrap/worker đang chạy | Setup sống qua handoff; worker đúng lifetime, không orphan | VP17, VP24 |
| AC17 | Update StaticArchive A → B cùng bundle | Đúng Setup B, binary mới và receipt/journal mới, startup gate mở hợp lệ | VP18–VP20, VP29 |
| AC18 | Repair B khi app/component hỏng, không READY hoặc desktop không mở được | Đúng version/build/Setup, có đường chạy Setup độc lập, không bỏ ownership/gate hoặc đòi READY trước repair | VP18–VP19, VP29 |
| AC19 | Thiếu Setup exact/binding sai/inactive | Báo lỗi và giữ bản cũ; không fallback latest/bare updater | VP09, VP17–VP20 |
| AC20 | Bundle khác hoặc cài legacy không receipt | Từ chối/hướng dẫn phạm vi hỗ trợ, không xóa metadata/dữ liệu để ép cài | VP19, VP29 |
| AC21 | Old/new client × old/new server và chuyển client cũ | Chỉ nhận delivery hiểu được; tải Setup mới qua kênh đã kiểm, legacy thiếu receipt không bị ép update tại chỗ | VP04, VP20–VP21, VP29 |
| AC22 | Header/cookie API khi tải file host | Không chuyển token/cookie sang VPS; policy vẫn kiểm internal URL | VP14–VP15, VP23 |
| AC23 | Hai Setup/app đang dùng model, Unicode TEMP/path | Lease/process ownership đúng; không READY chéo hoặc kill cây độc lập | VP24–VP25 |
| AC24 | Native UI, full App/bridge/gate, DPI/keyboard | Trạng thái/tiến độ/focus đúng; báo riêng DPI vật lý chưa thử | VP16, VP22, VP24, VP29 |
| AC25 | Model cache đúng/hỏng, offline runtime | Chỉ reuse khi xác minh, file hỏng mất readiness; không tải upstream ngầm | VP19, VP26, VP29 |
| AC26 | Qwen/Piper/OCR/workflow sau cài | Đủ metric/assertion/WAV/nghe/đọc/cue/revision theo phạm vi FX liên quan | VP26 |
| AC27 | Soak ≥4 giờ và fault matrix FX còn thiếu | Có chuỗi tài nguyên/phase, không hang/orphan/data loss trong phạm vi đã chốt | VP25–VP26, FX19–FX32 |
| AC28 | Deploy/rollback nhiều API instances | External chưa visible khi có binary cũ; rollback không xóa file/data hoặc làm hỏng repair A | VP28, VP31 |

## 7. Thứ tự triển khai và điểm dừng

1. **Đợt 1:** VP01–VP03; chốt phương án và đầu vào, giữ task môi trường thiếu ở trạng thái Blocked. Chưa cần đụng VPS/DB thật.
2. **Đợt 2:** VP04 → VP05/VP06 → VP07–VP09. Sau contract, Admin VP10 và pipeline VP11 có thể làm song song với consumer VP14–VP20 trên fixture.
3. **Đợt 3:** VP21–VP25 kiểm từng ranh giới bằng fixture, sửa lỗi có regression. Không mở external Stable khi update/repair mới chỉ qua download.
4. **Đợt 4:** VP27 → VP12 → VP13 → VP28 → VP29 → VP26. Pipeline/fixture được kiểm trước; full GET và package QA dùng chính candidate đã đóng băng sau build để tránh vòng phụ thuộc. Cần đủ profile/FFmpeg/môi trường trước cài khách/model thật; không dùng diagnostic ZIP thay candidate.
5. **Đợt 5:** VP30–VP32, pilot rồi Stable sau khi có kết quả thực và môi trường/người nhận đã chỉ định. VP32 có thể chuẩn bị runbook trước pilot, nhưng trạng thái rollout phải ghi sau khi thực hiện.

Hai nhánh môi trường và source có thể tiến hành độc lập đến điểm phụ thuộc. Ước lượng để sắp lịch, không phải cam kết: thiết kế/contract/schema 1–2 ngày; server/Admin/build 2–3 ngày; Setup/maintenance/compatibility 2–4 ngày; fixture và hồi quy 1–2 ngày; staging/cài sạch/native/soak tối thiểu 1–2 ngày tùy lỗi và máy thử. Thời gian chờ profile/FFmpeg/API/DB/VPS/Windows QA tách riêng, không giảm ma trận để khớp lịch.

## 8. Kiểm tra khi triển khai source

Đây là checklist tương lai, **chưa chạy ở lượt lập kế hoạch**:

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build

Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
npm test
```

Chạy thêm các bài server/Admin asset/browser bị ảnh hưởng theo harness hiện hành; SQL migration trên DB QA đã chốt; transport/Setup/handoff trên fixture. Sau source cuối chạy ổn định `StaticSetup` 20 vòng trên output mới đủ tài nguyên, browser/native/model riêng theo task. Không chạy test rộng lặp lại khi không có thay đổi, failure hoặc nghi ngại cụ thể cần kiểm.

Không ghi số test cũ thành kết quả mới. Báo Passed/Failed/Skipped/Blocked/Unverified và source/candidate/environment identity; không tự áp migration, publish Active production, gọi provider có phí hoặc xóa model máy người dùng từ checklist này.

## 9. Điều kiện hoàn thành và bàn giao

- Có luồng Admin đăng ký URL ZIP ứng dụng VPS; public metadata/client dùng URL đúng source và protocol, giữ internal hoạt động.
- Có bộ app ZIP/Setup/metadata nhất quán; Setup trỏ API thật, app ZIP tải từ VPS khác origin, model đúng pin; đã cài trọn bộ và chạy model trên máy QA theo phạm vi công bố.
- Update/repair StaticArchive qua đúng Setup, A/B và receipt/transaction đã nghiệm thu; không còn đường mở nghiệp vụ bằng READY hoặc commit cũ.
- Không lỗi P0/P1 chưa xử lý trong phạm vi bàn giao; các AC/FX chưa thử vẫn ghi riêng và không gọi toàn bộ hệ thống production-ready.
- Candidate/checksum/runbook/migration/bug register/bằng chứng được bàn giao; ghi rõ source đã có, tự động đã chạy, smoke đã thực hiện và rollout đã tới đâu.

**Việc bắt đầu khi được giao code:** VP01–VP04, song song chuẩn bị đầu vào VP02; không bắt đầu bằng đổi riêng `localhost` hoặc nới host check của Setup. Cần hoàn thiện cả release external, policy tải và maintenance để luồng sử dụng được ổn định.
