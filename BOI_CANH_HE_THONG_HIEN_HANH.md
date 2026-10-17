# Bối cảnh hệ thống hiện hành

## StandaloneCatalog — source 2026-10-17

Trên `main`, HEAD nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit: đã thêm catalog 5/Setup 1.3.0 không phụ thuộc bản ghi Setup Active. Hosting lưu phiên bản/link ZIP và link EXE toàn hệ thống, không tải/kiểm file. Setup/tool-local kiểm hash/native binding/model và giữ snapshot/IPC/transaction/rollback. Source chọn `StandaloneCatalog`; migration additive 4.1.21 mở CHECK 4/5 sau 4.1.20, không sửa quyền hay dữ liệu cũ. Theo yêu cầu tiếp theo, **đã áp exact 4.1.21 trên `MB2D70\MSSQLSERVER2019 / tap51004_tool` lúc 2026-10-17 09:49:14 UTC**, kết nối mới xác minh marker/CHECK và bảo toàn dữ liệu/schema/quyền nhìn thấy. Backup restore hosting vẫn chưa xác minh; chưa deploy hosting hoặc nghiệm thu toàn bộ model trên Windows sạch. [Biên bản SQL đích](TRIEN_KHAI_SQL_4_1_21_HOSTING.md), [runbook và bằng chứng ứng dụng](TRIEN_KHAI_PHAT_HANH_DON_GIAN_VPS.md). Các mục DirectCatalog/Setup Active tiếp theo ghi lại phạm vi lịch sử.

## Link tải Setup trên website — source 2026-10-17

Theo phạm vi tối giản đã duyệt, thêm cấu hình một link EXE tại Admin, lưu trong `auth.ServerSettings` và dùng endpoint tải trang chủ để chuyển hướng `302/no-store` sang VPS. Đã kiểm chỉ đọc bảng/quyền trên hosting, không cần SQL mới trên database đã kiểm tra; chưa ghi link hoặc deploy runtime. Không đăng ký Setup Active, không thay gate phát hành hay luồng cài/update/model. [Biên bản và cách dùng](TRIEN_KHAI_LINK_TAI_SETUP_TREN_VPS.md).

## DirectCatalog — source 2026-10-17

Theo yêu cầu tiếp theo **“tạo sql đơn giản thôi”**, đã chọn đường khởi tạo mới [4.1.20 DirectDesktopCatalogSimple](database/VideoFactory.4.1.20.DirectDesktopCatalogSimple.sql): cùng 24 cột/PK/5 CHECK/3 index, không thao tác quyền hoặc yêu cầu role. Sau đó theo yêu cầu chạy SQL, exact 4.1.20 đã commit trên `MB2D70\MSSQLSERVER2019 / tap51004_tool` lúc **2026-10-17 07:47:34 UTC**; kết nối mới xác minh đạt lúc **07:48:52 UTC**: 24 cột, 5 CHECK đúng/trusted, PK cùng 3 index, marker 4.1.18/4.1.19/4.1.20 mỗi mã một dòng, catalog mới 0 dòng. Dữ liệu/schema bốn bảng phát hành cũ và quyền nhìn thấy giữ nguyên; file 4.1.19 không đổi. Backup/restore và clone hosting chưa xác minh; không suy ra đã deploy server, đăng ký Setup Active hoặc nghiệm thu model. [Bằng chứng SQL Simple](TRIEN_KHAI_SQL_4_1_20_DON_GIAN.md). Lượt lỗi 4.1.19 dưới đây là lịch sử, không còn là đường khởi tạo được chọn.

Lịch sử trước nhánh Simple: đã thử áp exact **4.1.19** lên hosting `MB2D70\MSSQLSERVER2019 / tap51004_tool` ngày 2026-10-17. SQL **15151** tại lệnh DENY cho role desktop làm toàn transaction rollback; kết nối mới khi đó xác nhận bảng/marker 4.1.19 chưa có và dữ liệu/schema/quyền cũ quan sát được giữ nguyên. Gói DBA đã được chuẩn bị cho nhánh cũ; không cần chạy tiếp sau khi chọn và áp 4.1.20 Simple. [Biên bản SQL 4.1.19 và file DBA](TRIEN_KHAI_SQL_4_1_19_HOSTING.md).

Trên `main`, HEAD nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit: hosting chỉ lưu/phát hành version và URL ZIP có SHA-256 trong tên; không tải ZIP, không enqueue preparation khi `PublicationMode=DirectCatalog`. Publish mặc định tự tạo ZIP canonical; Setup 1.2.0 capability `3,4` tải trực tiếp, kiểm toàn archive/native identity, tạo metadata cục bộ rồi dùng transaction/probe hiện có. Catalog 4 độc lập với application distribution 3. Schema catalog đã áp hosting qua **4.1.20 Simple** như biên bản trên; source, artifact và kiểm thử ứng dụng được ghi riêng tại [biên bản DirectCatalog](TRIEN_KHAI_PHAT_HANH_TRUC_TIEP_TU_VPS.md), không đồng nghĩa runtime đã rollout. Các mục tiếp theo là lịch sử.

Lượt cuối sau Publish/build lại riêng: full .NET **2.809 Passed / 0 Failed / 18 Skipped**, React **345/0/0**, browser Admin **34/0/0**; migration 4.1.19 rehearsal synthetic SQL2019 đạt. Đã tạo desktop **1.0.2/build 3**, Setup **1.2.0** và server IIS trong `D:\vm-direct-catalog\20261017-140233\delivery`. ZIP thật qua resolver/extraction, native media/OCR Anh/Trung/WebView2 đạt; chưa cài model trên Windows sạch hoặc rollout. Các lượt crash/resource, test bị Publish đổi binary và bằng chứng cuối được giữ riêng trong biên bản, không dùng các lượt đầu làm kết quả đạt.

## Lịch sử: ZIP Publish thông thường — source 2026-10-17

Trên `main`, HEAD nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit: `FolderProfile` mặc định chỉ xuất thư mục Publish. Người vận hành nén toàn bộ nội dung hoặc kèm một thư mục ngoài, upload VPS và nhập phiên bản/link trong Admin. Server nhận ZIP không có ba JSON phát hành, kiểm layout/native version/profile/FFmpeg, tính hash toàn ZIP và từng file rồi lưu metadata riêng. Setup dùng chung tối thiểu **1.1.0** lấy metadata từ API, xác minh ZIP và tạo metadata trong staging trước luồng cài/model/receipt/rollback hiện có. `DesktopCreateVerifiedReleaseZip=true` giữ đường tạo ZIP có metadata trên máy build. Thay đổi này dùng schema 4.1.18 hiện có, không thêm migration; môi trường chưa áp 4.1.18 vẫn phải áp trước binary server tương ứng.

Đã kiểm chỉ đọc ZIP thật [publish-01.zip trên VPS](https://103.179.190.182:8443/models/publish-01.zip): **485.992.498 byte**, SHA-256 `20b59b48f774c2ea89c5a5e000306dd445e90a17c3f2b284679739b6f993ab73`, **338 file**, layout `publish/`, native version **1.0.0/build 1**; inspector đạt. Bằng chứng ở `C:\vm-simple-zip\20261017-125003`. Kết quả này chứng minh kiểm file, chưa chạy EXE, cài Setup, probe model hoặc deploy production. Kết quả build/test cuối và phạm vi đầy đủ được ghi tại [biên bản ZIP Publish đơn giản](TRIEN_KHAI_ZIP_PUBLISH_DON_GIAN.md); các lượt dưới đây là lịch sử riêng.

Setup 1.0 cũ chưa hiểu ZIP thông thường này; cần build/đăng ký Setup mới một lần và tiếp tục dùng chung cho các phiên bản tương thích. Desktop protocol 3 cũ có thể chưa chấp nhận URL ứng dụng trong `/models/`; trường hợp đó chạy Setup mới độc lập hoặc dùng route `/apps/videomaker/` đã được binary cũ hỗ trợ. Bộ model và gate READY giữ nguyên. WebView2 đã duyệt có thể nhúng trong Setup mới để cài khi ZIP không kèm installer; hash/chữ ký vẫn bắt buộc. Hồ sơ corresponding source/GPL và qualification production của FFmpeg vẫn chưa hoàn tất.

## Lịch sử: Publish Visual Studio tạo ZIP SharedVps — 2026-10-17

Profile `FolderProfile` áp protocol 3, StaticArchive bắt buộc và version/build trước khi đánh giá metadata biên dịch; nhận cả `PublishProfile` và `WebPublishProfileFile` của Visual Studio. Sau Publish, packager lấy danh sách file SDK, giữ main EXE và worker độc lập, tạo metadata/inventory/checksum, kiểm ZIP rồi promote nguyên thư mục. ZIP cũ cùng identity chỉ được dùng lại khi payload trùng; payload khác yêu cầu tăng build. FFmpeg 9.0.1 tự chuẩn bị offline theo pin vào bundle riêng, giữ bộ Development; đủ tám file binary/license/hồ sơ được thêm ngay lần đầu. Hook trước `GetCopyToPublishDirectoryItems` giữ runtime Content thành file rời, managed reference vẫn bundle. Cấu hình `appsettings.release.json` riêng đã tạo, không có SQL password dùng chung và validator đạt; chưa tạo `.local.props`, vẫn version `1.0.0`/build `1`. Scope FFmpeg `Release/TechnicalPackaging` không xác nhận corresponding source/phân phối GPL/production đã đủ; ba qualification vẫn `false`. [Hướng dẫn](scripts/VisualStudioPublish.README.vi.md) và [biên bản cuối](TRIEN_KHAI_CHUAN_HOA_FFMPEG_VA_PUBLISH.md) tách bằng chứng tự động/local khỏi nghiệm thu khách.

Lượt cuối tại `C:\vm-ffmpeg-release\20261017-053859`: Release build **0 error/0 warning**, focused **96 Passed / 0 Failed / 0 Skipped** (38 giây), frontend **341/0/0** (48 file). Frontend build qua SDK đạt, còn Vite chunk warning; chưa chạy lại `npm ci` vì thiếu chỗ. SDK đã tạo output TOOL-LOCAL single-file thật tại `publish`; worker/fixture/license rời, probe WebView2/Media/OCR Anh/Trung **READY**, MSVC app-local, chưa kiểm server. **Tạo ZIP chưa đạt**, dừng guard dung lượng; chưa ZIP mới, Setup, Qwen/Piper installed, Windows sạch hoặc rollout. Full chưa chạy lại: cần 5.290.061.824 byte, khả dụng khoảng 2,39 GiB tại thời điểm preflight trước; đo cuối còn C **0,771 GiB**, D **0,034 GiB**. Manifest cuối **1.400 file**, SHA-256 `d97754e5d288aa2c32606fc7b746a70820e96c0b1af53b640a61da7d2a97a742`; `verification-final.json` xác nhận source và hash native EXE không đổi sau test, ZIP mới **0**.

Full gần nhất là lịch sử lượt tích hợp trước: **2.535 Passed / 1 Failed / 18 Skipped / 2.554 Total**, focused **53/0/0**; bài hai giờ thiếu dung lượng. Manifest 1.117 file và ba assembly bất biến thuộc lượt cũ, không thay identity hoặc full test source bổ sung.

## Form release tối giản và Setup dùng chung — source 2026-10-17

Đã thêm form bốn trường, nháp/job kiểm ZIP lưu database, tự tính SHA-256 toàn ZIP và từng file, đối chiếu version/build native, catalog Setup hệ thống và snapshot cài đặt protocol 3. Pipeline tách build ZIP khỏi build Setup; cập nhật mới so Version trước BuildNumber. Protocol 1/2 và bộ model hiện hành giữ nguyên. Cờ shared mặc định tắt; 4.1.18 đã rehearsal trên SQL Server 2019 LocalDB riêng và **đã áp hosting ngày 2026-10-17**. [Biên bản hiện hành](TRIEN_KHAI_FORM_RELEASE_TOI_GIAN_VA_SETUP_DUNG_CHUNG.md) ghi kiểm thử trên source đang sửa, giới hạn bằng chứng và các bước nghiệm thu/rollout còn lại; các mục bên dưới là lịch sử.

Theo yêu cầu tiếp tục của người dùng, 4.1.18 đã commit lúc **2026-10-16 21:33:47 UTC** trên `112.78.2.70 / MB2D70\MSSQLSERVER2019 / tap51004_tool`; marker, cột `VerifiedPreparationId`, hai bảng mới và CHECK/FK/index đạt, kết nối mới xác nhận. Số dòng release/artifact vẫn 0; FK/index cũ và fingerprint quyền nhìn thấy giữ nguyên. Backup/restore và rehearsal clone hosting **chưa xác minh**; người dùng yêu cầu tiếp tục sau khi được thông báo giới hạn này. [Biên bản SQL hosting](TRIEN_KHAI_SQL_4_1_18_HOSTING.md) tách bằng chứng SQL khỏi deploy ứng dụng, phát hành và nghiệm thu model còn chưa thực hiện.

Kết quả lượt form trước tích hợp Publish: restore/Release build đạt **0 error/0 warning .NET**; full .NET **2.483 Passed/0 Failed/18 Skipped/2.501 Total**, nhóm shared focused **127/0/0** nằm trong tổng full. Frontend ci/build/test **341/0/0** trên 48 file; Admin browser **121/0/0**, năm asset UI khớp hash trước/sau. Manifest 1.143 file code/script và sáu assembly được giữ nguyên qua lượt kiểm thử đó. Frontend vẫn có Vite chunk warning; 18 bài opt-in bị skip không chứng minh model/SQL runtime đích đã đạt.

## Phát hành TOOL-LOCAL qua VPS — source 2026-10-17

Nhánh `main`, HEAD nền `f0651c918f28ca477f28875818148e9e86240b7b` và working tree chưa commit: đã thêm ExternalVps cho ZIP ứng dụng, Admin draft/register/upload exact Setup/publish bằng Revision, distribution protocol 2 và maintenance IPC sang đúng Setup. Model bundle/pin giữ nguyên. Migration mới `4.1.17-desktop-external-artifacts` phải đi trước binary EF mới; external mặc định tắt. Ngày 2026-10-17 đã áp duy nhất migration này trên `112.78.2.70 / MB2D70\MSSQLSERVER2019 / tap51004_tool`: đủ 9 cột, RelativePath nullable, 6 CHECK trusted, unique index đúng, marker duy nhất và quyền không đổi; kết nối mới xác nhận commit. Backup/restore và rehearsal clone hosting vẫn chưa xác minh. [Biên bản triển khai](TRIEN_KHAI_PHAT_HANH_TOOL_LOCAL_QUA_VPS_VA_SETUP.md) ghi bằng chứng SQL và ma trận còn thiếu; lượt này chưa deploy/API Admin/VPS app mới, chưa tạo candidate khách, cài Windows sạch hoặc chạy model từ bản cài mới.

## Khắc phục và công cụ nghiệm thu StaticArchive — 2026-10-17

Thực hiện [FX01–FX36](PLAN_TASK_KHAC_PHUC_VA_NGHIEM_THU_SETUP_MODEL.md): thêm CLI/script nghiệm thu Qwen/Piper tại runtime đã cài, giữ mode bundled; kiểm trust/receipt/commit/lease và không install/refresh READY. Preflight đọc đúng pin/cache/revocation StaticArchive và phân biệt ngân sách QA với dung lượng cài. Đã thêm full App/WebView2 với native bridge/coordinator fixture và test Job Object khi parent chết. Dependency Pkcs được pin nhánh vá .NET 10 và giữ khỏi pruning gián tiếp ở testhost WindowsDesktop. Kết quả từng lượt và giới hạn nằm tại [biên bản khắc phục](TRIEN_KHAI_KHAC_PHUC_VA_NGHIEM_THU_SETUP_MODEL.md); chưa cài Setup khách, chạy model thật/soak hoặc rollout trong lượt này.

## Kiểm thử ổn định Setup/model — 2026-10-17

Source trên `main`, nền `f0651c918f28ca477f28875818148e9e86240b7b` cùng working tree: đã sửa các lỗi UI/bridge/receipt/metadata/transaction phát hiện khi triển khai kế hoạch 32 task. [Biên bản kiểm thử ổn định](TRIEN_KHAI_TEST_ON_DINH_SETUP_MODEL_UI_UX_VA_LOGIC.md) chứa kết quả hiện hành, cả lượt fail và retest; không suy từ đó rằng Setup đã cài trên Windows sạch, model thật đã chạy soak hoặc đã rollout.

## Setup cài model từ VPS — source 2026-10-16

Đã thêm StaticArchive cho ZIP VPS compile pin, transport resume/hash, import, production bootstrap trước login, journal/backup, WebView2 prerequisite và commit sau năm component probe. Build Release đã đạt; bằng chứng test hiện hành và các giới hạn nằm tại [biên bản Setup](TRIEN_KHAI_SETUP_CAI_MODEL_TU_VPS.md). Đây là source trên `main` nền `f0651c9` cùng working tree, chưa phải binary đã rollout; Windows sạch/model thật/pilot còn chưa xác minh.

## Đóng gói server kèm model — 2026-10-16

Đã tạo ZIP IIS Windows x64 khoảng 2,91 GB từ `main`, nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit; snapshot 1.082 file source/script khớp trước/sau publish. Gói có runtime 10.0.9 và nguyên bộ model `models-20261016-03`; kiểm 533 file sau giải nén đạt. Đã loại output/debug khỏi Content và bổ sung sanitizer/audit cấu hình trước ZIP. [Biên bản, hash và hướng dẫn](TRIEN_KHAI_DONG_GOI_SERVER_KEM_MODEL.md) tách kiểm toàn vẹn khỏi nghiệm thu IIS/DB/Setup. Chưa upload, khởi động server, ký catalog hoặc bật kho model.

## Chuẩn bị test Setup tải model từ server — 2026-10-16

Trên `main`, nền `f0651c918f28ca477f28875818148e9e86240b7b` và working tree chưa commit, downloader đã hỗ trợ Retry-After có giới hạn/hủy; pipeline có Authenticode opt-in trước inventory/hash; Piper có phase/exit code an toàn và công cụ preflight chỉ đọc. Kiểm lại 31 file bàn giao đạt. Piper build mới cài venv/probe và verify bằng tiến trình mới đạt trong workspace D cô lập có độ dài prefix tương đương global, không tải dependency qua mạng; chưa phải root thực hoặc Setup tải từ server. Chưa xác minh Qwen/OCR runtime mới trong lượt này.

Cache mặc định C thiếu dung lượng cho Qwen. Chưa chỉ định server đích, khóa/profile và bộ Setup cuối; chưa upload, publish, cách ly hoặc xóa model đang dùng. [Biên bản task, kiểm thử và giới hạn](TRIEN_KHAI_TEST_SETUP_MODEL_TU_SERVER.md) tách source, test giả lập, smoke component và bài nghiệm thu server còn thiếu. Không thay các trạng thái này bằng lịch sử ở dưới.

## Kho model và cài đặt an toàn — 2026-10-16

Source mới có handover Qwen 4B/Piper/OCR, catalog ký riêng V1, kho server chỉ đọc và downloader streaming/resume gắn Setup. Server mode của pipeline vẫn yêu cầu root public nhúng, profile HTTPS và worker/OCR/media đầy đủ; mặc định giữ Bundled, `ModelDistribution.Enabled=false` và pin hiện hành. Xem [biên bản triển khai hiện tại](TRIEN_KHAI_KHO_MODEL_SERVER_VA_CAI_DAT_AN_TOAN.md) cho kết quả build/test, file thật và phần chưa nghiệm thu. Các số kiểm thử trong các mục lịch sử dưới đây không thay bằng chứng của lượt này; chưa có server đích, khóa production hoặc rollout.

## Gia cố worker Qwen và chẩn đoán lỗi — 2026-10-16

Source trên `main`, nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit và giữ thay đổi có sẵn. Worker 1.2.1 giữ IPC 3/model/profile; callback log native cô lập lỗi I/O/OOM của phần chẩn đoán, bỏ Regex trong callback và phân loại allocation CPU đúng lỗi bộ nhớ. Client lưu tối đa 20 báo cáo metadata an toàn (16 KiB/báo cáo), giữ exit code hex/phase và mã hỗ trợ qua Setup; không lưu prompt/stderr/path hoặc biến thao tác hủy thành crash. Fingerprint thêm runtime .NET/config/deps; publish giữ version worker độc lập desktop. Có harness CPU cold start/dịch fixture lặp với đo tăng private memory; không nới gate hoặc tự chấp thuận cảnh báo tài nguyên.

Restore và build Release cuối đạt **2 cảnh báo Pkcs có sẵn / 0 Error**; frontend còn cảnh báo chunk Vite. npm ci/build đạt, frontend **322 Passed / 0 Failed / 0 Skipped**. Full .NET cuối **1.813 Passed / 0 Failed / 18 Skipped / 1.831 Total**; hai regression native callback I/O/OOM đã chạy riêng đạt và nằm trong full suite, không cộng trùng. Các lượt build lỗi do hết bộ nhớ và thiếu import đã được giữ log; số trên là lượt đầy đủ sau sửa cuối.

Publish diagnostic riêng thành công, không cấu hình khách/credential và không kích hoạt release. Worker publish **1.2.1.0** có SHA-256 trùng worker đã test; kiểm file **64 component / 24 web asset / 0 thiếu-sai hash**. Native preflight của chính publish đạt CPU AVX2, đúng bốn MSVC app-local, model đúng hash và worker exit 0; không nạp weights/inference. Probe offline giữ gate, trả `TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED`, stage `VERIFYING`, 0 lượt HTTP. Snapshot ngay trước đó: RAM trống 2,98 GiB, commit trống 2,90 GiB; chưa nhận chấp thuận chạy dưới ngưỡng.

**Chưa kết luận đã giải quyết nguyên nhân crash native cũ hoặc đủ điều kiện gửi khách.** Event Windows cũ ghi worker Debug/coreclr/`0xC0000005`; symbol Microsoft khớp địa chỉ tại `ProcessCLRException+0x13f`, chưa có crash dump để xác định stack/nguyên nhân gốc. Chưa chạy model thật, soak trên bản mới hoặc ma trận Windows sạch/Intel/AMD/GPU; các bài opt-in bị skip không thay các bước này. Không sửa cờ mặc định, migration, provider hoặc server; không dừng app/IDE người dùng. Bằng chứng: `D:\VideoMakerDiagnostics\qwen-fix-20261016-002903`; hướng chạy harness ở [vận hành](VAN_HANH_VA_PHAT_HANH.md).

## Modal xác nhận thanh toán trước khi vào ứng dụng — 2026-10-15

Source trên `main`, nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit. Desktop thêm `PaymentSuccessDialog`: sau khi đơn được server xác nhận `Fulfilled` và không chờ ngày hiệu lực, hiển thị thông báo thành công, tên gói, số tiền, ngày hiệu lực/hết hạn và mã đơn từ snapshot. Chỉ bật **Vào ứng dụng** khi license và kích hoạt thiết bị hợp lệ; dashboard/heartbeat không tự bỏ qua bước xác nhận. Nền dùng `inert`, dialog giữ focus và không đóng bằng Escape hoặc bấm ngoài. Nếu kích hoạt thiết bị lỗi, giữ thông tin đã thanh toán và cho kiểm tra lại bằng status API, không tạo đơn mới. Sau xác nhận, Setup runtime vẫn chặn ứng dụng nếu component chưa READY.

`Paid` còn chờ cấp gói và gói `Scheduled` giữ luồng hiện hành, không hứa vào ứng dụng ngay. Đổi tài khoản/thu hồi quyền xóa trạng thái xác nhận, phản hồi cũ không được khôi phục quyền. Không thay public DTO, API, webhook, SQL hoặc cấu hình thanh toán.

`npm ci`, frontend production build và Release build solution đạt; frontend **322 Passed / 0 Failed / 0 Skipped** trên 47 file. Browser production bundle + native bridge giả lập **11 Passed / 0 Failed / 0 Skipped**, gồm 3 ca modal mới và 8 ca hồi quy; đã xem ảnh desktop 1440 px và màn hẹp 390/320 px. Log từng lượt .NET, hash source/bundle và kết quả cuối được ghi riêng tại `output/payment-success-ui-20261015-231603/report.json`. Cảnh báo Pkcs NU1902/NU1903 và Vite chunk có sẵn còn trong build. Chưa gửi webhook/thanh toán thật, chạy SQL hosting, publish hoặc nghiệm thu desktop với giao dịch thật.

Full .NET cuối **1785 Passed / 0 Failed / 18 Skipped / 1803 Total**, collection tuần tự với TEMP/TMP ngắn `D:\vm-tests\payment-20261015-231603`. Lượt đầu dùng đường dẫn tạm sâu trong repository có 66 failure liên quan mở SQLite; sau đổi thư mục tạm, nhóm storage đạt 16/0/0 và toàn bộ suite đạt như trên, không sửa hoặc bỏ assertion. Giữ cả hai TRX; không cộng trùng các lần chạy lại. Model/SQL opt-in bị Skipped vẫn chưa được nghiệm thu. Bundle frontend trong desktop Release khớp SHA-256 với Web/dist.

## Menu Local chỉ vào Dịch — 2026-10-15

Rà soát trên `main`, nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit. Gói Local mở thẳng Dịch phụ đề; giữ toàn bộ menu và nút Tạo video mới nhưng khóa các mục ngoài Dịch, gồm Cài đặt. Icon khóa, lý do, `aria-disabled` và focus bàn phím vẫn có; chuột/Enter/Space không điều hướng hoặc gửi command công cụ. Trang hiển thị được ràng buộc trực tiếp theo license, tránh state điều hướng cũ mở trang ngoài gói. Nhận `license.activated` hoặc dashboard mới cập nhật quyền ngay; gói Cloud/toàn bộ công cụ giữ navigation cũ.

Thông tin gói/đăng xuất còn dùng được. Setup được mở từ mục thu gọn Thiết lập công cụ dịch ngay trong trang Dịch, dùng context cá nhân; không yêu cầu tổ chức và không mở menu Cài đặt. Module Dịch chưa được bật/kiểm tra thì báo chưa sẵn sàng, không dựng thư viện. Hook TikTok không tải nền cho gói Local. Không đổi public DTO, schema, quyền SQL hoặc thanh toán trong lượt này; không ghi SQL hosting hay publish.

Restore và build Release đạt, còn cảnh báo Pkcs NU1902/NU1903 và Vite chunk có sẵn. Full .NET **1779 Passed / 0 Failed / 18 Skipped / 1797 Total**; frontend cuối **312 Passed / 0 Failed / 0 Skipped** trên 46 file; browser bản bundle thật + bridge giả lập **8 Passed / 0 Failed / 0 Skipped**. Sau bổ sung lối Setup thu gọn, đã build lại và kiểm tra native policy/Setup **76 Passed / 0 Failed / 0 Skipped**, không cộng 76 vào tổng full. Lượt frontend đầu có hai lỗi mock Vietsub thiếu danh sách project, đã hoàn thiện fixture và guard module chưa bật; log được giữ. Đã xem ảnh menu Local 390/1440 px sau khi chờ sidebar mở xong. [Report và hash source](output/local-menu-lock-20261015/report.json) ghi rõ từng lượt; snapshot READY là fixture, không chứng minh model/runtime máy khách hoặc hosting sẵn sàng.

## Local cá nhân bỏ phân bổ — source 2026-10-15

Trên `main`, nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit: form chỉ Local ẩn phân bổ, mở bán không cần pool nhưng cần SePay. Checkout cấp license theo snapshot/lịch mua, không reserve/activate membership; reservation Local cũ được release khi fulfillment. Desktop startup, dashboard, project/job và Setup dùng context cá nhân; giữ license/thiết bị/owner và gate probe runtime. Project tổ chức/dữ liệu lịch sử giữ nguyên; Cloud vẫn cần tổ chức.

Migration mới `VideoFactory.4.1.16.VietsubPersonalProjects.sql` cho phép NULL ở `vs.Projects.OrganizationId`; audit cá nhân dùng auth audit cùng transaction. Chưa chạy migration trên SQL Server/hosting, chưa rollout hoặc smoke thanh toán/provider/runtime thật. Bằng chứng kiểm thử của lượt này nằm trong `output/vietsub-personal-local-20261015`; số liệu các đợt trước bên dưới là lịch sử, không thay thế kết quả hiện hành.

Restore/build Release, npm ci/build đạt. Frontend **310 Passed / 0 Failed / 0 Skipped** trên 46 file. Full .NET có **1768 Passed / 1 Failed / 18 Skipped / 1787 Total**; ca fail dùng entity payment cũ sau khi webhook clear tracking, đã đổi sang đọc lại DB. Sau bổ sung release Scheduled Local lịch sử, nhóm hồi quy cuối có **308 Passed / 0 Failed / 1 Skipped / 309 Total**; lượt trước đó có hai lỗi fixture gắn trùng plan, đã sửa dùng plan ID. Kết quả mới nhất theo `testId` duy nhất: **1771 Passed / 0 Failed / 18 Skipped / 1789 Total**; không cộng trùng test chạy lại.

Browser Admin/TikTok lượt đầy đủ có **111 Passed / 1 Failed / 0 Skipped / 112 Total**. Nút SePay bị di chuyển DOM khi blur ô giá khiến mất click; đã thêm guard và chạy lại ca SePay cùng toàn bộ composer **36 Passed / 0 Failed / 0 Skipped**. Kết quả cuối theo từng ca duy nhất **112 Passed / 0 Failed / 0 Skipped**. Các log fail được giữ nguyên. Đã xem ảnh Local 390/1440 px: hai phần thông tin/quyền, không còn bước phân bổ hoặc ghi chú nhóm dùng chung. [Report và hash source](output/vietsub-personal-local-20261015/report.json) phân biệt từng lượt chạy, kết quả cuối và giới hạn fixture. SQL Server/model opt-in vẫn chưa nghiệm thu; cảnh báo Pkcs NU1902/NU1903 và Vite chunk có sẵn còn trong log.

## Hoàn thiện logic gói tool dịch — 2026-10-15

Trên `main`, nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit: đã thống nhất license hiệu lực và lease của chính license đó, tách quyền Vietsub khỏi project video, giữ quyền đã mua khi dừng bán. Wizard/phản hồi pool có profile Local/Cloud, capacity và liên kết sửa đúng tổ chức/model; Local không ghi cờ video-ready và lưu quota AI bằng 0. Cloud publisher/runtime dùng cùng quy tắc model/host/capability/rate; member budget được kiểm trước khi tạo job, settlement/recovery tiếp tục cho phần đã gửi.

Thanh toán giữ snapshot, kiểm lại tổ chức đã giữ chỗ khi nhận tiền, lưu Paid/chờ kích hoạt nếu chưa cấp được và retry idempotently. Gói mới nối sau cả lịch license tương lai; chỉ gia hạn gộp khi cùng gói/quyền. Scheduled bị revoke/hết hạn không tự kích hoạt membership. Desktop nhận ngày dự kiến/ngày hiệu lực và không chờ kích hoạt ngay gói tương lai. Không thêm schema/migration, đổi cấu hình/giá/key trên hosting, restart server, phát hành hoặc gọi provider có phí.

npm ci/build và Release build đạt; frontend **309 Passed / 0 Failed / 0 Skipped**, browser Admin/TikTok **110 Passed / 0 Failed / 0 Skipped**. Lượt browser đầu có một ca SePay đọc trạng thái trước khi navigation bất đồng bộ hoàn tất; đã thêm chờ panel hiển thị, giữ nguyên assertion và chạy lại cả bộ đạt. Full .NET tuần tự có **1755 Passed / 2 Failed / 18 Skipped / 1775 Total**: một Vite memory allocation failure và một timeout media WebView2. Chạy riêng toàn bộ nhóm desktop browser/WebView2 đạt **24 Passed / 0 Failed / 0 Skipped**, bao gồm hai ca lỗi đó. Kết quả mới nhất theo từng test duy nhất là **1757 Passed / 0 Failed / 18 Skipped**, không cộng trùng 24 ca chạy lại vào tổng. Lượt full song song trước đó có 4 lỗi cấp phát bộ nhớ; log được giữ nguyên. Bằng chứng: [report.json](output/vietsub-plan-logic-20261015/report.json), `full-serial.trx`, `desktop-browser-isolated.trx`, log và ảnh trong cùng thư mục; ảnh Local 1440/390 px đã xem. Browser/API dùng fixture, không xác nhận hosting/thanh toán/provider thật hoặc model opt-in đã nghiệm thu. Build giữ cảnh báo Pkcs NU1902/NU1903 và Vite chunk có sẵn.

## Gói chỉ tool dịch Vietsub trong Admin — 2026-10-15

Trên `main`, nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit: form tạo/sửa/nhân bản có loại Chỉ tool dịch / Vietsub, ba phần theo mockup, quyền Local/Cloud, preview và kiểm tra mở bán theo loại gói. Local không cần provider/rate/budget AI; Cloud kiểm OpenAI/model dịch/rate/ngân sách và không yêu cầu Fal/Veo. Offers/reserve và checkout dùng cùng scope; quyền được snapshot và kiểm tra ở server, host C# cùng React. Gói hiện có giữ quyền khi sửa; thay quyền qua tạo mới/nhân bản. SePay riêng, không thêm migration hoặc thay giá/key/gói thật trên hosting.

Restore và build Release đạt; build còn hai cảnh báo Pkcs NU1902/NU1903 và frontend còn cảnh báo Vite chunk có sẵn. npm ci và frontend build đạt; frontend **307 Passed / 0 Failed / 0 Skipped** trên 46 file. Browser Admin/TikTok **106 Passed / 0 Failed / 0 Skipped**, gồm form Vietsub ở 390/1440/1792 px và toàn bộ hồi quy form cũ. Full .NET cuối **1733 Passed / 0 Failed / 18 Skipped / 1751 Total**, chạy collection tuần tự với TEMP/TMP trên D. Lượt đầu trên C có 8 failure do dung lượng trống không đủ; không tính lượt đó là Passed. Lượt đầy đủ trên D trước hai case cấp quyền cài FFmpeg cuối đạt 1731/0/18; con số nghiệm thu cuối là 1733/0/18.

Browser dùng source thật với HTTP fixture, không chứng minh đã rollout. Model/SQL opt-in bị Skipped không được tính đã nghiệm thu. Chưa khởi động lại server/IDE, publish hosting, ghi SQL, gửi webhook thật hoặc gọi provider có phí trong đợt này. Cần deploy đồng bộ server/desktop; runtime Local và cấu hình Cloud vẫn được kiểm tra riêng trên máy/môi trường đích. Hash source, log, TRX và ảnh thực tế: `output/vietsub-plan-ui-verification/report.json`, `screenshots-final/vietsub-plan-1440.png` trong cùng thư mục.

## Thu gọn header Admin — 2026-10-13

Trên `main`, nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit: bỏ nhãn in hoa lặp trên tiêu đề và chữ Admin Console bên phải; thu gọn khoảng cách, tiêu đề desktop 28–30 px, mô tả 15 px. Header Bảng giá AI ở viewport 1440 px/zoom CSS 100% giảm **237,5 → 144,5 px**. Tiêu đề dài và thao tác được xuống dòng khi cần; kiểm 320–1920 px không tràn ngang, nút vẫn cao tối thiểu 44 px. Gỡ các tham chiếu JS/CSS đến phần đã bỏ, giữ giao diện gọi metadata của các module. Không thay API/schema/nghiệp vụ.

Restore, Release solution và Debug server riêng đạt **0 Warning / 0 Error**; npm ci/build đạt với cảnh báo Vite chunk có sẵn. Sau sửa header, Full .NET **1682 Passed / 0 Failed / 13 Skipped / 1695 Total**, 4 phút 56 giây; browser **71/0/0**; frontend **296/0/0**. 15 asset × Debug/Release khớp source, hai assembly chứa marker **`admin-only-new-20261013.2`**. Đã xem ảnh crop trước/sau và trang Bảng giá AI đầy đủ; ảnh/luồng browser dùng fixture, model/SQL bị skip không tính là runtime đã đạt. [Task và bằng chứng](PLAN_TASK_HOP_NHAT_UI_ADMIN.md).

GET local cuối lượt này vẫn trả **`admin-only-new-20261013.1`**, còn nhãn header cũ: UI mới của lượt trước đã được nạp, bản thu gọn header chưa được process nạp. Cần Stop Debugging → Build `TOOL-SERVER` → F5 rồi kiểm `/admin`. **Chưa tự restart process, smoke phiên Admin/database thật hoặc rollout production.** Các mục UI bên dưới là lịch sử trước lượt thu gọn header.

## Chỉ dùng UI Admin mới sau đăng nhập — 2026-10-13

Trên `main`, nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit: đã thay markup/renderer Tổng quan mặc định, Người dùng/chi tiết, danh sách gói và Phát hành; danh sách tổ chức/tài khoản SePay/nhóm nhận khách dùng card mới. Gỡ theme cũ và loader bổ sung asset cho Razor cũ; một theme/component dùng chung cho các module và popup. `admin-pages.js` render các màn mới, `admin-dialogs.js` thay confirm/prompt native. Login thường tại `/admin` xác minh API Admin trước khi hiện Tổng quan mới; thiếu module hoặc chỉ có role lưu trong browser mà API không phản hồi thì shell vẫn ẩn. Nghiệp vụ/API/schema/RBAC/desktop được giữ.

Restore, Release solution và Debug server riêng đạt **0 Warning / 0 Error**; npm ci/build đạt với cảnh báo Vite chunk hiện có. Full .NET **1682 Passed / 0 Failed / 13 Skipped / 1695 Total**, 3 phút 55 giây; UI subset **37/0/0** không cộng total; browser Admin/TikTok cuối **71/0/0**; frontend **296/0/0**. Full/frontend chạy trước chỉnh JS/CSS cuối về guard, empty state và thông tin chi tiết tài khoản; browser cuối đã kiểm những chỉnh này. 15 asset × Debug/Release khớp source, assembly chứa marker **`admin-only-new-20261013.1`**, composer **`20261013.4`**. Đã xem ảnh login mặc định/form/mobile/module; API trong ảnh là fixture, model/SQL bị skip không tính là runtime đã đạt. [Task và bằng chứng](PLAN_TASK_HOP_NHAT_UI_ADMIN.md).

GET local `https://localhost:7202/admin` cuối lượt này vẫn trả **`admin-mockup-20261013.2`**, chưa có markup Tổng quan mới. Cần Stop Debugging → Build `TOOL-SERVER` → F5 để process nạp Razor mới, rồi kiểm `/admin` thông thường. **Chưa restart process, smoke phiên Admin/database thật, kiểm zoom browser thủ công hoặc rollout production.** Không thay database/provider thật. Các mục UI bên dưới là lịch sử trước thay toàn bộ trình bày này.

## Sửa UI Admin theo ảnh mẫu — 2026-10-13

Trên `main`, nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit: font Roboto Latin/tiếng Việt self-host, sidebar 290 px và icon mới, tên Phát hành desktop, khoảng cách form/preview và alignment trên màn rộng được sửa. Giá ô nhập format khi blur/focus để chỉnh thuận tiện; API vẫn nhận số nguyên, giữ validation và các luồng tạo/sửa/clone/retry. Readiness/sức chứa lấy từ API, không giả trạng thái theo ảnh.

Release solution và Debug server ở thư mục riêng build đạt 0 warning/error MSBuild; npm ci/build đạt, Vite còn cảnh báo chunk >500 KB. Full .NET **1682 Passed / 0 Failed / 13 Skipped / 1695 Total**, 3 phút 54 giây; source UI subset sau điều chỉnh high contrast **21/0/0** (không cộng vào total); browser Admin/TikTok **66/0/0**, frontend **296/0/0**. Đã xem ảnh tham chiếu 1672 × 941 và mobile; fixture API không outbound, model/SQL bị skip không tính là nghiệm thu runtime. [Plan và bằng chứng mới](PLAN_TASK_HOP_NHAT_UI_ADMIN.md).

Marker source/build mới `admin-mockup-20261013.2`, composer `20261013.4`; manifest CSS/JS/font khớp source. GET local vẫn trả `admin-unified-20261013.1`; Debug mặc định bị DLL lock bởi IDE/server, đã build thành công ở thư mục artifact riêng. Cần Stop Debugging rồi Build/F5 để nạp source mới. **Chưa restart process, smoke phiên Admin/database thật hoặc rollout production.** Các mục bên dưới là lịch sử trước lượt sửa hình thức này.

## Hợp nhất UI Admin, gỡ theme/dialog gói cũ — 2026-10-13

Trên `main`, nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit: Admin dùng một layout sáng cho mọi mục; đã gỡ theme có điều kiện, dialog/form gói cũ và handler trùng. Tạo/sửa/nhân bản dùng composer; sửa giữ code/quyền và trạng thái nháp/riêng tư/mở bán/tạm ngừng, ghi gói cùng mapping trong transaction, kiểm phiên bản cả hai và chặn đổi/gỡ nhóm còn seat Active/Reserved/Scheduled. DTO tùy chọn giữ hash tạo cũ; replay dùng plan ID trong audit. Dữ liệu/desktop/API tương thích và các chức năng quản trị khác được giữ.

Restore, Release solution build, Debug server build, npm ci/build đạt **0 Warning / 0 Error** MSBuild; Vite giữ cảnh báo chunk hiện có. Full .NET cuối **1682 Passed / 0 Failed / 13 Skipped / 1695 Total**, 5 phút 4 giây; browser Admin/TikTok **63 Passed / 0 Failed / 0 Skipped**; frontend **296 Passed / 0 Failed / 0 Skipped**. Đã xem ảnh; tám asset Debug/Release khớp source, cả hai assembly có marker `admin-unified-20261013.1`, không còn dialog cũ. Test dùng fixture, model/SQL bị skip không tính là nghiệm thu runtime. **Chưa restart/smoke server với phiên Admin/database thật hoặc rollout production.** [Task và bằng chứng](PLAN_TASK_HOP_NHAT_UI_ADMIN.md). Các mục UI bên dưới ghi lịch sử trước hợp nhất.

## UI Admin tạo gói và mở bán — 2026-10-13

Điều chỉnh sau phản hồi local: form tạo gói dùng sidebar sáng, input/form bo góc có viền, preview trắng theo mockup; mở trực tiếp bằng `/admin?view=create-plan` sau login, kể cả phiên cũ hết hạn. Đã build cả Debug và Release, kiểm bản Razor Debug chứa marker `20261013.2`. Browser cuối **49 Passed / 0 Failed / 0 Skipped**, frontend chạy lại **296 Passed / 0 Failed / 0 Skipped**, full .NET **1670 Passed / 0 Failed / 13 Skipped / 1683 Total** trong 5 phút 3 giây; static UI .NET sau chỉnh JS cuối **9 Passed / 0 Failed / 0 Skipped**. Kiểm asset manifest và ảnh thực tế tại năm độ rộng, chưa smoke UI với server/phiên Admin/database thật. Khi đọc máy local lúc 18:33, các cổng 7202/5021/7242 không lắng nghe; chưa có URL/view người dùng mở để kết luận nguyên nhân runtime không thấy form. Xem ảnh và bằng chứng mới trong mục `reference-fix` của [plan task](PLAN_TASK_UI_TAO_GOI_VA_MO_BAN.md); các số/ảnh bên dưới là lượt triển khai trước điều chỉnh hình thức.

Trên `main`, nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit và giữ thay đổi có sẵn: **Gói sử dụng → Tạo gói** mở form năm trường chính, thiết lập nâng cao, preview, lưu nháp/mở bán và nhân bản gói. Form dẫn tới setup nhóm/SePay khi thiếu điều kiện, giữ dữ liệu và đọc lại trạng thái khi quay về. API Global Admin mới tạo gói và mapping trong transaction `Serializable`, kiểm lại AI readiness/sức chứa/SePay; nháp không Active/Public, clone giữ quyền gói nguồn, retry cùng actor/payload không tạo gói trùng. Không đổi schema hoặc DTO desktop, không tự bật nhóm/AI/thanh toán và không giữ seat khi tạo gói.

Restore, npm ci/build và Release solution build đã đạt **0 Warning / 0 Error** MSBuild; Vite còn cảnh báo chunk >500 kB hiện có. Frontend **296 Passed / 0 Failed / 0 Skipped**; browser Admin **46 Passed / 0 Failed / 0 Skipped**, gồm 16 ca form mới với API giả và chặn outbound. Server riêng tính năng **19 Passed / 0 Failed / 0 Skipped** với SQLite private relational; full .NET collection tuần tự **1670 Passed / 0 Failed / 13 Skipped / 1683 Total**, 5 phút 17 giây, với TEMP/TMP riêng process đặt trên đường dẫn ngắn ổ D. Model/SQL bị skip không tính là nghiệm thu. Kiểm năm độ rộng và đã xem ảnh desktop/mobile. **Chưa smoke thao tác tạo gói với phiên Admin/database đích hoặc rollout production; cần nạp server build mới cho API/Razor.** Lịch sử sửa bài kiểm CSS và lỗi môi trường được ghi trong [task và bằng chứng](PLAN_TASK_UI_TAO_GOI_VA_MO_BAN.md).

## UI Admin hướng dẫn phân bổ khách hàng — 2026-10-13

Trên `main`, nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit và giữ thay đổi có sẵn: **Phân bổ khách hàng** có bốn bước tổng quan, tổ chức/sức chứa, kiểm tra sẵn sàng và gắn gói/mở bán theo mockup đã duyệt. Có đường tới budget/OpenAI/policy/pricing/SePay và quay lại nhóm; quản lý chi tiết/audit/retry vẫn được giữ. Dùng API hiện có, không đổi DTO/schema hoặc guard server. Mapping từng gói được lưu tuần tự, lỗi lưu một phần được báo rõ; nhóm nháp chỉ bật sau hoàn tất, nhóm đã Active giữ trạng thái và gói mới có thể bán ngay khi liên kết được lưu. UI không tự chuyển gói khỏi nhóm khác, bỏ phản hồi cũ và dừng ghi tiếp theo khi logout.

Restore, npm ci/build và Release build solution đạt **0 Warning / 0 Error** MSBuild, Vite còn cảnh báo chunk >500 kB. Frontend **296 Passed / 0 Failed / 0 Skipped**; browser Admin **30 Passed / 0 Failed / 0 Skipped** (12 ca mới), kiểm năm độ rộng, dùng source thực/API giả và chặn outbound; đã xem ảnh. Full .NET collection tuần tự **1651 Passed / 0 Failed / 13 Skipped / 1664 Total**, 6 phút 55 giây. Build static assets cuối không đổi hash assembly server/test. Model/SQL bị skip không tính là nghiệm thu. HTTP read-only trên local 7202 xác nhận loader và JS/CSS hướng dẫn đang phục vụ khớp source; **chưa smoke thao tác lưu bằng phiên Admin/database thật hoặc rollout production**. [Task, ảnh và bằng chứng](PLAN_TASK_UI_ADMIN_PHAN_BO_KHACH_HANG.md).

## UI khi hệ thống chưa cung cấp gói sử dụng — 2026-10-13

Trên `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit: màn hình khóa license và popup **Nâng cấp gói** đã dùng trạng thái thông tin **Hiện chưa có gói sử dụng** khi catalog rỗng hoặc API trả `payments_unavailable`, theo mockup đã chọn. Lỗi mạng/server khác vẫn hiển thị lỗi riêng; retry giới hạn request đang chờ, timeout 30 giây và bỏ phản hồi cũ khi đổi tài khoản. Bổ sung sau phản hồi vẫn gặp exception: `GET /api/license/offers` trả `200 []` khi thanh toán chưa sẵn sàng; tạo thanh toán vẫn giữ `503 payments_unavailable`, lỗi đọc cấu hình vẫn là lỗi thật. Giữ checkout của đơn đang chờ và gate đến khi license/device hợp lệ; không đổi DTO, SQL, credential hoặc công tắc thanh toán.

`npm ci`, frontend production build, restore và Release build solution đạt; MSBuild 0 Warning / 0 Error, Vite còn cảnh báo chunk >500 kB. Frontend **296 Passed / 0 Failed / 0 Skipped**, browser fixture **3 Passed / 0 Failed / 0 Skipped** ở 320/768/1440 px, full .NET collection tuần tự **1645 Passed / 0 Failed / 13 Skipped / 1658 Total**. Các bài model/SQL opt-in bị skip không tính là nghiệm thu. Đã đồng bộ UI Debug bằng target MSBuild hiện có; 24 file của mỗi bundle Debug/Release khớp SHA-256 `Web/dist`. Browser chạy bridge giả và chặn outbound; đã xem ảnh. **Chưa smoke với tài khoản/database thật hoặc rollout production.** [Plan task, bằng chứng và cách nạp UI](PLAN_TASK_UI_CHUA_CO_GOI_SU_DUNG.md).

Bổ sung server: sáu regression mới trước sửa **1 Passed / 5 Failed / 0 Skipped**, tái hiện exception ở API đọc. Sau sửa nhóm thanh toán **88 Passed / 0 Failed / 0 Skipped**, full .NET collection tuần tự **1651 Passed / 0 Failed / 13 Skipped / 1664 Total**, 5 phút 6 giây; restore/Release build solution và Debug build server đạt **0 Warning / 0 Error** MSBuild. Output desktop Debug/Release vẫn có UI mới và trỏ local `https://localhost:7202/`; frontend/browser không chạy lại trong lượt C# này, hash bundle vẫn khớp lượt UI phía trên. Bằng chứng và giới hạn trong plan đã liên kết; model/SQL bị skip không tính là nghiệm thu, chưa smoke runtime đăng nhập thật hoặc rollout.

## Admin quản lý tài khoản nhận thanh toán SePay — 2026-10-11

Trên `main`, nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit: có mục **Thanh toán SePay** trong Admin để thêm/sửa/chọn ngân hàng nhận tiền và bật/tắt nhận đơn mới. Cấu hình lưu trong `auth.SepayReceivingAccounts` và `auth.SepayPaymentSettings`, chỉ Global Admin được quản lý, có version/audit và transaction. Binary cần migration `4.1.10-sepay-receiving-accounts`; migration không seed ngân hàng và mặc định tắt. Đơn mới dùng tài khoản được chọn trong SQL; đơn cũ giữ QR và receiver snapshot, webhook vẫn xử lý khi đổi/sửa tài khoản hoặc nhận đơn mới đã tắt. Theo yêu cầu sản phẩm, webhook không dùng key/chữ ký và không xác minh nguồn gửi.

Restore/Release build đạt, MSBuild 0 Warning / 0 Error; Vite còn cảnh báo chunk >500 kB. Nhóm thanh toán/phân quyền/admin CSS **93 Passed / 0 Failed / 0 Skipped**, gồm 20 ca SePay mới; full .NET **1.644 Passed / 0 Failed / 13 Skipped**, 4 phút 55 giây; frontend **280 Passed / 0 Failed / 0 Skipped**; browser Admin/SePay **18 Passed / 0 Failed / 0 Skipped**, dùng source thực/API giả tại 5 độ rộng. Model/SQL opt-in bị skip không tính là đã nghiệm thu. Tại thời điểm lượt kiểm source ban đầu, chưa áp SQL Server đích/smoke/rollout; các lượt thao tác database tiếp theo được ghi bên dưới. [Phạm vi, task, bằng chứng và các bước rollout](TRIEN_KHAI_ADMIN_SEPAY.md).

Bổ sung production ngày 11/10/2026 theo yêu cầu người dùng: đã đọc metadata `MB2D70\MSSQLSERVER2019 / tap51004_tool`, xác nhận schema nền thanh toán/provisioning. Tài khoản không có quyền backup; `RESTORE VERIFYONLY` bị từ chối (SQL 262), khả năng restore chưa được xác minh. Sau yêu cầu tiếp tục, đã chạy nguyên migration nhưng **Failed/rollback hoàn toàn** tại DENY quyền desktop (SQL 15151); kết nối mới xác minh hai bảng/version mới vẫn chưa tồn tại. Principal có `db_ddladmin`, thiếu quyền bảo mật/CONTROL schema `auth`; cần quản trị SQL thực thi migration với quyền phù hợp. Chưa deploy/bật SePay hoặc nâng kiểm giả lập thành nghiệm thu production; chi tiết trong biên bản SePay đã liên kết ở trên.

Bản sửa SQL: thêm migration khởi tạo thay thế `4.1.12.SepayReceivingAccountsPermissions`, giữ 4.1.10 bất biến, chỉ tái sử dụng DENY schema/object khi xác minh đủ CRUD. Regression private LocalDB/dữ liệu giả **13 Passed / 0 Failed / 0 Skipped**, gồm tái hiện lỗi, restore/checksum, quyền desktop, idempotency và rollback. Restore/Release build mới đạt **0 Warning / 0 Error** MSBuild; full .NET **1.645 Passed / 0 Failed / 13 Skipped**, 4 phút 22 giây; frontend **280 Passed / 0 Failed / 0 Skipped**, browser không chạy lại trong lượt SQL. Trên production, bản mới dừng trước DDL với SQL 51124 vì thiếu metadata visibility; hai bảng/version vẫn chưa có. Đã chuẩn bị gói DBA có guard target và outer transaction tại `artifacts/sepay-admin-20261011/production/Apply-SePay-Permissions-Fix.sql`; chưa áp gói bằng principal quản trị. Chi tiết và kết quả source mới trong biên bản SePay.

Theo yêu cầu tiếp theo ngày 11/10/2026, đường khởi tạo SePay hiện chọn `database/VideoFactory.4.1.13.SepayReceivingAccountsSimple.sql`: chỉ tạo bảng/ràng buộc, seed cấu hình mặc định và ghi version, không GRANT/DENY/REVOKE hoặc kiểm tra role/CONTROL; giữ nguyên quyền và cấu hình đã có. Private LocalDB SQL Server 2019/dữ liệu giả **18 Passed / 0 Failed / 0 Skipped**, gồm 5 ca mới cho nhánh đơn giản; các migration cũ giữ nguyên. Sau yêu cầu chạy production, **4.1.13 đã áp thành công** trên `MB2D70\MSSQLSERVER2019 / tap51004_tool`, `AppliedAtUtc=2026-10-10T18:42:59.425Z`; kết nối mới xác minh 2 bảng, PK/unique/CHECK/FK, 2 version SePay, singleton tắt/chưa chọn tài khoản và `OpenTransactions=0`. Payment cũ vẫn 3, version khác vẫn 29. Backup/restore tiếp tục bị giới hạn quyền; runtime/UI production, triển khai server và thanh toán thật chưa xác minh. Không chạy lại build/test trong lượt vận hành SQL. Chi tiết trong biên bản SePay.

Bổ sung theo môi trường người dùng chỉ định ngày 2026-10-14: **đã áp 4.1.13 vào DUNGDEV / VideoFactory lúc 12:08:45 UTC**. Backup COPY_ONLY/CHECKSUM, VERIFYONLY, restore thật/CHECKDB, chạy hai lần trên clone và kiểm schema sau áp đều đạt; dữ liệu cũ và quyền database giữ nguyên. Hai bảng SePay có đủ ràng buộc, singleton tắt/chưa chọn tài khoản, receiver count=0; clone đã dọn, backup được giữ. Đối chiếu source server: **87/87 bảng mapped có mặt**, không còn bảng thiếu trong phạm vi mapping, tổng 105 bảng. Không chạy lại build/test, đổi kết nối hoặc restart server; runtime/UI và thanh toán thật vẫn chưa xác minh. [Bằng chứng SQL DUNGDEV](TRIEN_KHAI_ADMIN_SEPAY.md).

## Admin Web vuông, không viền — 2026-09-23

Trên `main`, nền `0a85b569914a9a230425442e9f0b3e1c16072cf1`, working tree chưa commit: đã đồng bộ shell, Tổng quan, người dùng, tổ chức/AI, TikTok, gói, release, đăng nhập và dialog theo thiết kế vuông, không viền trang trí. Header tìm người dùng bằng API hiện có; số liệu Tổng quan lấy từ server, không suy ra thống kê toàn hệ thống từ một trang dữ liệu. Giữ Razor/JavaScript, contract, quyền và luồng nghiệp vụ; có bố cục mobile và chỉ báo focus bàn phím.

Restore/Release build đạt 0 Warning / 0 Error MSBuild; Vite còn cảnh báo chunk >500 kB. Full .NET **1.528 Passed / 0 Failed / 13 Skipped**, frontend **268 Passed / 0 Failed / 0 Skipped**, Admin shell/TikTok state/browser **27 Passed / 0 Failed / 0 Skipped**. Browser dùng source hiện hành với API giả, kiểm năm độ rộng và các tab tổ chức, đã xem ảnh. **Chưa smoke server/database thật hoặc rollout production**; các bài opt-in bị skip không tính là đạt. [Task, ảnh, lệnh và giới hạn](PLAN_TASK_ADMIN_WEB_VUONG_KHONG_VIEN.md).

## Sửa loader WebView2 trong desktop publish — 2026-09-23

Trên `main`, nền `0a85b569914a9a230425442e9f0b3e1c16072cf1`, working tree chưa commit và giữ các thay đổi có sẵn: desktop chỉ định đường loader x64 trong chính gói trước API WebView2 đầu tiên; phân biệt thiếu/hỏng DLL với thiếu Evergreen. Thêm `--check-webview2`, kiểm loader trong MSBuild/script publish và probe với PATH chỉ gồm Windows. Gói cũ tái hiện lỗi với PATH tối giản; gói single-file mới trả READY và ba fixture thiếu/hỏng/x86 bị từ chối đúng.

Release solution build đạt 0 Warning / 0 Error MSBuild; full .NET **1.528 Passed / 0 Failed / 13 Skipped**, frontend **268 Passed / 0 Failed / 0 Skipped**. Gói Development riêng kiểm 42 component/24 web asset đạt; probe local media/OCR/Piper READY, Qwen DISABLED. Config thử đã bỏ mật khẩu SQL; không thay cấu hình gốc. Publish còn hai cảnh báo profile ở project con và Vite chunk >500 kB. **Chưa nghiệm thu login/SQL trên máy đích hoặc rollout production**; giữ nguyên phê duyệt FFmpeg Development. [Task, hash, lệnh và bằng chứng](PLAN_TASK_SUA_LOI_WEBVIEW2_PUBLISH.md).

## Chuẩn hóa TOOL-LOCAL cho máy mới — 2026-09-18, triển khai một phần

Trên nhánh `main`, nền `0a85b569914a9a230425442e9f0b3e1c16072cf1`, working tree chưa commit: đã chuyển tùy chọn UI sang LocalAppData, sửa composition để tắt dịch thì không yêu cầu Qwen, bổ sung `--check-desktop` và kiểm cấu hình/bundle trước phát hành. Lệnh chẩn đoán và app dùng chung factory Qwen/Piper; không cài/chạy inference model để thu báo cáo. **Chưa chuyển workflow SQL sang API, chưa sửa/deploy server, chưa nghiệm thu Windows sạch.** Bản hiện hành vẫn cần SQL; script phát hành từ chối host phát triển/mật khẩu nhúng và không chấp nhận xóa cấu hình SQL để giả định đã hỗ trợ API.

Restore và Release solution build đạt, MSBuild 0 Warning / 0 Error; Vite còn cảnh báo chunk >500 kB. Full .NET sau sửa cuối chạy collection tuần tự **1.493 Passed / 0 Failed / 13 Skipped / 1.506 Total**, 4 phút 04 giây. Frontend **268 Passed / 0 Failed / 0 Skipped**, 43 file. Model/runtime/SQL opt-in bị skip không được tính là đạt. Artifact self-contained Development có 319 file; kiểm 41 mục component/24 web asset đạt, ba fixture package lỗi bị chặn đúng. Chạy EXE publish trên máy hiện tại: media/OCR/Piper READY, Qwen DISABLED đúng cờ, WebView2 có; không suy ra server/workflow hoặc cài model trên máy sạch đã sẵn sàng. Gate release vẫn chặn FFmpeg vì approval scope Development. [Phạm vi, bản đồ SQL, hash và bằng chứng](TRIEN_KHAI_TOOL_LOCAL_TREN_MAY_MOI.md); artifact tại `D:\VideoMakerDiagnostics\desktop-portability-implementation-20260918`. Chưa rollout production.

## Popup Thông tin gói và Nâng cấp gói — 2026-09-18

Hai nút Sidebar/Header đã mở popup thật. **Thông tin gói** hiển thị gói, trạng thái, ngày bắt đầu/hết hạn, số thiết bị và tổ chức được cấp nếu có. **Nâng cấp gói** tải giá, thời hạn, số thiết bị, quyền lợi và tình trạng chỗ từ server; nhận diện gói hiện tại và hỗ trợ lỗi/rỗng/thử lại. Phạm vi hiện tại là xem thông tin, hướng dẫn liên hệ quản trị viên để nâng cấp/gia hạn; không tạo thanh toán QR hoặc thay đổi license. Giữ nguyên bridge/API/SePay và luồng thanh toán của license gate. Popup đóng bằng nút/Esc, giữ Tab/Shift+Tab bên trong, trả focus; bỏ qua phản hồi cũ sau đóng, timeout hoặc đổi tài khoản.

Xác minh trên `main`, nền `9646a7b4eaf271f79bd3278b36a96e0932661c58`, working tree chưa commit và giữ các thay đổi Vietsub/dịch có sẵn. Restore, npm ci, frontend build và Release solution build đạt; **6 Warning NU1900 / 0 Error** do audit NuGet không truy cập được, Vite còn cảnh báo chunk >500 kB. Frontend **268 Passed / 0 Failed / 0 Skipped**, 43 file, gồm 9 ca App/bridge mới. Chrome headless dùng bundle production với bridge/dữ liệu giả đạt **6/6** trường hợp hai popup tại 1366×900, 768×650 và 390×720; kiểm tràn ngang, cuộn, bàn phím và focus, đã xem ảnh. Lỗi Tab phát hiện ở vòng đầu đã sửa và kiểm lại.

Full .NET collection tuần tự **1466 Passed / 1 Failed / 13 Skipped / 1480 Total**, 4 phút 10 giây. Bài lỗi `VietsubTimelineLayoutIntegrationTests.WebView2_keeps_label_descenders_and_waveforms_visible_at_multiple_zoom_levels` báo căn giữa chữ tại zoom đầu, cùng lỗi đã ghi ở lượt vùng che trước. Kiểm tra riêng nhóm contract giao diện/license bridge và bài Timeline sau build cuối: **21 Passed / 0 Failed / 0 Skipped**; không coi lượt full là đạt, không sửa source/assertion Timeline và chưa kết luận nguyên nhân. Các bài model/SQL opt-in bị bỏ qua không được tính là nghiệm thu.

Artifact: `D:\VideoMakerDiagnostics\license-information-20260918` (log, TRX, ảnh và `verification.json`). Asset web khớp SHA-256 với wwwroot Release; diff check đạt. Chưa smoke desktop đăng nhập với catalog thật, chưa thay đổi database/cấu hình thanh toán hoặc phát hành production.

## Vùng che giữ nền video và lớp màu trong suốt — 2026-09-18

Sau phản hồi vùng che đen, đổi mặc định mask mới sang **Làm mờ nền (giữ hình video)**. Lớp màu có thanh **Độ trong suốt** 0–100%; preview và FFmpeg áp alpha riêng cho vùng che, giữ chữ Việt rõ. Alpha màu dự phòng cho mask mới là 0,35 (65% trong suốt). Cấu hình SOLID đã lưu thiếu alpha vẫn đọc là 1; người dùng đổi kiểu che hoặc thanh trong suốt rồi lưu, không tự ghi lại dự án. Trường `opacity` tùy chọn được validate hữu hạn/0–1, giữ JSON schema 8 và SQLite schema 6.

Trên `main`, nền `9646a7b`, working tree chưa commit: restore/build Release và npm ci/build thành công. Build có **6 cảnh báo NU1900 / 0 Error** do NuGet không truy cập được dịch vụ audit; Vite còn cảnh báo chunk >500 kB. Frontend **259 Passed / 0 Failed / 0 Skipped**; nhóm mask/style/export C# **46 Passed / 0 Failed / 0 Skipped**, gồm pixel FFmpeg tại alpha 0/0,35/1. Hai fixture browser cho lớp màu trong suốt và BLUR đạt, đã xem ảnh. Full .NET collection tuần tự **1466 Passed / 1 Failed / 13 Skipped / 1480 Total**; lỗi căn giữa Timeline tại zoom đầu. Chạy riêng bài Timeline trên cùng binary đạt **1 Passed / 0 Failed / 0 Skipped**; chưa kết luận nguyên nhân, không coi lượt full là đạt. Không đổi assertion hoặc source Timeline.

Artifact: `D:\VideoMakerDiagnostics\subtitle-mask-transparency-20260918`. Asset web và wwwroot Release khớp SHA-256; diff check đạt. Chưa smoke với video/dự án người dùng hoặc phát hành. [Cách chuyển vùng đen cũ sang giữ nền và chi tiết kiểm chứng](CHE_PHU_DE_GOC.md).

## Che phụ đề gốc trong Thiết kế thành phẩm — 2026-09-18

Thêm tab **Che sub gốc** trong cửa sổ **Phụ đề, hình ảnh và âm thanh**: bật/tắt, phủ màu kín/làm mờ, kéo vùng chữ nhật và đổi kích thước ở tám cạnh/góc, điều chỉnh bằng thanh trượt hoặc bàn phím. Một vùng cố định áp dụng xuyên suốt video, nằm dưới chữ Việt và đi theo hình khi lật; hỗ trợ tính tọa độ ở FIT/FILL và zoom. Lưu và lưu trước xuất dùng contract thiết kế hiện hành; preview ngoài modal hiển thị vùng che đã lưu. Manifest JSON nâng 7 → 8 với che mặc định tắt, giữ lật hình; SQLite vẫn schema 6. FFmpeg áp dụng che → lật → phụ đề; snapshot vùng che đổi giữa render thì không publish file tạm.

Kiểm chứng trên `main`, nền `9646a7b`, working tree chưa commit và có sẵn nâng cấp dịch local từ phiên trước. Restore, Release build, npm ci/build đạt; MSBuild 0 Warning / 0 Error, Vite còn cảnh báo chunk trên 500 kB. Frontend **257 Passed / 0 Failed / 0 Skipped**. Nhóm mask/style/export C# **40 Passed / 0 Failed / 0 Skipped**, gồm tám trường hợp FFmpeg thật. Full .NET chạy collection tuần tự **1461 Passed / 0 Failed / 13 Skipped / 1474 Total**, 4 phút 25 giây. Chromium headless với component/CSS hiện hành và video tổng hợp đạt sáu tình huống viewport/zoom/FIT/FILL; đã xem ảnh, kiểm vị trí và thứ tự lớp. Asset web và wwwroot Release khớp SHA-256; diff check đạt.

Các bài model/runtime và SQL opt-in bị Skipped không được tính là đạt. Chưa smoke desktop đăng nhập/video người dùng hoặc phát hành production. Blur của trình duyệt và FFmpeg có thể khác nhẹ ở kernel/biên; không khẳng định giống từng pixel. [Cách dùng, source và bằng chứng](CHE_PHU_DE_GOC.md); artifact ở `D:\VideoMakerDiagnostics\subtitle-mask-20260918`.

## Nâng cấp tốc độ dịch local — 2026-09-17–18

Triển khai trên `main`, nền `9646a7b`, working tree sạch trước thay đổi; chưa commit hoặc phát hành. Worker 1.2.0 / IPC 3 tái sử dụng `StatelessExecutor` trong phiên nạp model, vẫn tạo context inference/sampling riêng cho mỗi request. Planner `qwen4b-cuda12-v3` thêm 28/30/32 layer, giữ dự phòng VRAM và giới hạn tối đa ba lần GPU thất bại; lưu admission/retry trước load, resume không tăng lại layer. Cache prefix có source thử nghiệm và benchmark riêng, chưa bật cho job bình thường.

Kiểm tra nội dung model thật phát hiện các mức mới làm thay đổi nghĩa trong fixture 12 câu. Vì vậy chỉ chọn 28/30/32 khi mọi nhóm đã lập kế hoạch trong job có tối đa 6 target cue; nhóm lớn dùng các mức cũ 12/24/36. Nhóm 2/6 câu của corpus khảo sát cho output trùng baseline; không suy ra chất lượng trên mọi phụ đề. Cache bản dịch, prompt/sampling, cue manual/locked, CAS và SRT atomic giữ nguyên. Bản cũ cũng có lỗi chất lượng trong fixture lớn; nâng cấp tốc độ này không được coi là sửa toàn bộ chất lượng model.

Restore/Release build đạt 0 Warning / 0 Error ở MSBuild; Vite còn cảnh báo chunk trên 500 kB. Frontend **252 Passed / 0 Failed / 0 Skipped**. Full .NET trên binary cuối: lượt song song mặc định **1.442 Passed / 1 Failed / 13 Skipped**, lỗi khởi tạo PaddleOCR; lượt đầy đủ chạy tuần tự trên cùng binary **1.443 Passed / 0 Failed / 13 Skipped**. Không cộng lượt chạy rời để coi lượt mặc định đã đạt, không tính model/SQL opt-in Skipped là đã nghiệm thu. Hash binary, benchmark RTX 3050 và kiểm thử chạy dài nằm trong [biên bản nâng cấp tốc độ](NANG_CAP_TOC_DO_DICH_LOCAL.md). Chưa smoke job qua desktop đăng nhập thật, Windows sạch, bundle hoặc rollout production.

Benchmark model thật trên binary cuối, RTX 3050 Laptop 4 GiB / driver 610.62: **1 Passed / 0 Failed / 0 Skipped**, bên trong **15/15 lượt cấu hình Passed**. Với corpus 16 cue chia nhóm 2/6, ba vòng đo cho thời gian inference trung bình từ **28,492 giây** (cách cũ 24 layer) xuống **18,203 giây** (reuse 32 layer), giảm **36,1%**; output trùng baseline. Không gồm load model và không áp dụng kết luận này cho job có nhóm lớn. Kiểm thử native chạy liên tục 100 nhóm cho reuse/prefix đạt trên binary khảo sát trước giới hạn chất lượng; biên bản phân biệt rõ hai bộ binary.

## Sửa lỗi GPU Vietsub khi đổi worker — 2026-09-17

Triển khai trên `main`, nền `e7e8e83`, giữ các thay đổi CPU/GPU chưa commit đã có. Đã tái hiện EOF của worker dò GPU bị chủ động dừng tác động sang worker mới và gây `TRANSLATION_PROCESS_CRASHED`, khiến job Auto chuyển CPU. `VietsubTranslationWorkerClient` hiện cô lập trạng thái theo phiên, chờ stdout/stderr kết thúc trước thay process, hủy request đúng phiên và xử lý dispose trong lúc startup/request mà không giải phóng semaphore quá sớm. Không sửa model, protocol, GPU planner, checkpoint hoặc dữ liệu project.

Restore/Release build solution đạt **0 Warning / 0 Error**; frontend **252 Passed / 0 Failed / 0 Skipped**. Full .NET mặc định trên binary cuối: **1.405 Passed / 1 Failed / 11 Skipped**; lỗi `LoginForm_LoadsWebViewAndAuthenticatesThroughTheHost` ở cleanup `BeginInvoke`, chạy riêng đạt **1 Passed / 0 Failed / 0 Skipped**. Full suite chạy lại trên cùng binary với `-- xUnit.ParallelizeTestCollections=false` đạt **1.406 Passed / 0 Failed / 11 Skipped** trong một lượt đầy đủ. Sáu case lifecycle mới đều Passed; trước sửa hai regression EOF đến muộn đều Failed đúng nguyên nhân. Giữ báo cáo lượt mặc định để theo dõi độ ổn định WebView2; không cộng lượt riêng thành full suite xanh hoặc tính model/SQL bị Skipped là đạt.

GPU model chạy riêng **1 Passed / 0 Failed / 0 Skipped**: ba chu kỳ dò GPU → reset → nạp mới với profile Standard, RTX 3050 Laptop / driver 610.62 dùng **24 layer CUDA**, cả fixture Anh và Trung đều dịch thành công ở mỗi chu kỳ. Đây là kiểm chứng trực tiếp worker trên máy hiện hành; chưa smoke job qua desktop đăng nhập thật, Windows sạch, bundle hoặc rollout production. Job cũ có `CpuFallback=true` tiếp tục CPU khi resume; job Local mới chọn `AUTO` được thử GPU. Chi tiết và đường dẫn bằng chứng ở [nhật ký tăng tốc CPU/GPU](TRIEN_KHAI_TANG_TOC_DICH_CPU_GPU.md).

## Lựa chọn giọng và tạo audio Vietsub — source checkout 2026-09-15

Rà soát và triển khai trên nhánh `main`, HEAD `52f0931`, working tree sạch trước thay đổi; phần triển khai này **chưa commit**. Source đã nối modal 15 giọng với `VoiceSettings` của project local, job snapshot đúng voice và executor chọn Piper hoặc Kokoro CPU theo snapshot. Kokoro dùng runtime Python x64 riêng với dependency hash-locked và probe WAV theo voice; `READY` của file model không tự thành `SynthesisReady`. Timeline khác engine/model/version/voice được loại khỏi preview/export; không có fallback engine khi runtime lỗi.

`dotnet restore`, Release build toàn solution và `npm ci`/frontend build đã đạt trên checkout này; build .NET 0 warning, 0 error. Trên binary cuối: .NET chạy regression trừ hai fixture timing **1.357 Passed / 0 Failed / 8 Skipped / 1.365 Total**; hai fixture translation runtime và nhãn WebView2 chạy riêng đều **Passed**, phủ đủ **1.359 Passed / 0 Failed / 8 Skipped / 1.367 Total** theo các lượt không giao nhau. Lệnh full suite mặc định có một lượt **1 Failed** ở translation runtime; full suite tuần tự có một lượt **1 Failed** ở phép đo căn nhãn WebView2 dưới 1 px. Cả hai đạt khi chạy riêng trên cùng binary cuối, nên chưa có bằng chứng một lượt full suite không lỗi; các lượt chia nhóm không chứng minh đã chạy model thật. Frontend **229 Passed / 0 Failed / 0 Skipped**. Chưa cài/probe Kokoro hoặc Piper trên máy đích, chưa nghe nghiệm thu, benchmark CPU, smoke desktop/bundle hay rollout production. Quyền LarVoice-derived voicepack cho phát hành công khai vẫn chưa được xác minh; xem `third_party/voice/PROVENANCE.md`.

## Snapshot source được rà soát — 2026-09-15

Checkout được rà soát để cập nhật context là nhánh `main`, commit `8f10cc9`; `git status --short` sạch trước khi sửa tài liệu. Đây là snapshot **source/project file/cấu hình mặc định/migration**, không phải ảnh chụp server, desktop hoặc SQL đang chạy. Không chạy build/test, migration, provider/model thật hoặc smoke UI trong lượt rà soát tài liệu này. Các số Passed/Failed/Skipped bên dưới thuộc những lần kiểm thử có commit/môi trường riêng; không dùng chúng làm kết quả hiện hành của checkout này.

Đường gọi chính đã đối chiếu: `Web/src/App.tsx` → `bridge.ts` → `Form1`/`DashboardBridge`/bridge module → `ServerGenerationClient` → `TOOL-SERVER/Controllers/GenerationController.cs` → `GenerationService`/`GenerationAccessService`/`AiBudgetService` → adapter provider. `VideoPollingWorker` trên server tiếp tục status/cache/settlement khi desktop đóng; status API chỉ đọc task đã lưu. Vietsub media/cue/SQLite/render và Bilibili download ở desktop; riêng job Dịch Cloud gửi snapshot **text** giới hạn đến server. TikTok dùng OAuth/token/job ở server, còn byte video và upload URL tạm chỉ ở desktop native.

Binary hiện hành phụ thuộc schema `4.1.9-short-video-outfit` cho `vf.ShortVideoOperations`, kể cả mode video ngắn `TextOnly`; cờ phối trang phục tắt không bỏ phụ thuộc này. Migration trong repository hoặc báo cáo đã áp trên `DUNGDEV / VideoFactory` không chứng minh schema của môi trường khác. Cần kiểm `ai.SchemaVersions`, bảng/constraint/quyền desktop và dữ liệu liên quan trên database đích trước khi khởi động server mới, vì startup bootstrap catalog có thể ghi database.

### Cập nhật danh mục giọng Vietsub local — 2026-09-15

Rà soát source trên nhánh `main`, commit `8f10cc9`, với working tree đã có thay đổi chưa commit trước lượt cập nhật tài liệu này. Nút **Tạo giọng Việt** hiện mở modal chọn **15 giọng local**: Piper VAIS-1000 và 14 voicepack Kokoro Vietnamese đã pin. Modal hiển thị card vuông; **Nghe thử** và **Cài giọng** dùng cùng kiểu nút. `vietsub.voice.models.status` lấy trạng thái file model trên máy qua host C#: chỉ file đủ dung lượng và khớp SHA-256 mới hiện **Sẵn sàng**; file thiếu hiện **Cài giọng**, file sai hiện **Cần cài lại/Cài lại**. Cài đặt chỉ tải các file model đã pin của giọng được bấm, dùng `.part`, xác minh rồi publish nguyên tử; Kokoro dùng chung ONNX model/config giữa các voicepack. **Sẵn sàng trong modal là trạng thái tài nguyên model**, tách biệt với Piper runtime `READY`; chưa có Kokoro runtime hoặc job tạo giọng Kokoro, và job tạo âm thanh phụ đề vẫn dùng Piper. Giọng AI chưa thuộc modal này.

Mỗi giọng có một WAV nghe thử đóng gói trong `TOOL-LOCAL/Web/public/voice-previews/`, phát được trước khi cài model; `manifest.json` ghi voice ID, revision model và SHA-256. Modal chỉ dùng URL local cố định theo voice ID và một trình phát tại một thời điểm, có tạm dừng/tiếp tục và dừng khi đóng. Source `Web/index.html` đã thêm `'self'` vào `media-src` để WebView2 cho phép tải WAV từ `https://app.local/voice-previews/`; trước sửa, chính CSP này chặn phát mẫu dù WAV có trong bundle. Lượt sửa source ngay trước cập nhật tài liệu đã build frontend và desktop Release/Debug đạt; frontend **228 Passed / 0 Failed / 0 Skipped**, .NET **1.355 Passed / 0 Failed / 7 Skipped**. Lượt tài liệu này **không chạy lại build/test**. Chưa có smoke nghe thử trực tiếp trên WebView2 sau sửa CSP, chưa nghiệm thu chất lượng giọng trên máy đích, và các bài model opt-in bị Skipped không chứng minh runtime/model đã đạt.

Kokoro model card ghi Apache-2.0 nhưng quyền dữ liệu/voicepack LarVoice và quyền phân phối các WAV tạo từ chúng chưa được xác minh độc lập. Cần rà soát [provenance](third_party/voice/PROVENANCE.md) và [license notice](third_party/voice/LICENSES.md), nghe kiểm tra và smoke trên bundle đích trước phát hành công khai; source, build và trạng thái model trong modal không chứng minh đã rollout production.

### Hợp nhất chọn lọc `main` vào `local-2` — 2026-09-15

Nhận thay đổi video ngắn Veo/nhân vật-trang phục, thư viện video ngắn, phục hồi tải output và Bilibili từ `origin/main` tại `9b8052c`. Giữ nguyên Vietsub, gate SystemSetup, chức năng Đăng TikTok hiện hành và không nhận tính năng Publishing mới có luồng đăng TikTok; giữ nguyên các file `.tmp` của `local-2`. Xung đột trong composition desktop/React được ghép theo từng phần. Restore/build Release đạt; .NET 1.352 Passed / 0 Failed / 6 Skipped, frontend 224 Passed / 0 Failed / 0 Skipped. Chưa chạy migration, provider có phí, đăng TikTok, model opt-in hoặc phát hành.

Cập nhật ngày 2026-09-15 trên nhánh `local-2`: ứng dụng đầy đủ đã có màn hình Setup OCR/FFmpeg/Qwen/Piper bằng modal React/WebView. Sau đăng nhập, `Form1` và nền màn hình dự án được hiển thị trước; modal mới phủ và khóa nền nếu còn thành phần bắt buộc chưa `READY`. Host C# đồng thời chặn command nghiệp vụ, nên gate không phụ thuộc riêng vào lớp phủ trình duyệt. Phạm vi tích hợp giữ nguyên video AI và Dịch Cloud; không mang chế độ `VietsubLocalOnly` hoặc điều hướng rút gọn từ `sub-local`. Restore/build Release đạt 0 warning, 0 error; .NET chạy tuần tự collection **1.081 Passed / 0 Failed / 4 Skipped**, frontend **152 Passed / 0 Failed / 0 Skipped**. Chưa chạy model opt-in, package repair thật, desktop/Windows sạch hoặc phát hành. Xem [báo cáo tích hợp](BAO_CAO_SETUP_HE_THONG.md).

Hợp nhất nhánh `local-2` ngày 2026-09-15: giữ commit Setup/Vietsub/lật hình `1333ab2` và các commit GitHub đến `dc6cdc3`, xử lý 5 file conflict về tài liệu, composition desktop và React. Checkout đã restore/build Release (0 warning, 0 error), frontend **187 Passed / 0 Failed / 0 Skipped**, .NET tuần tự **1.242 Passed / 0 Failed / 6 Skipped**. Các migration TikTok/LocalVoice có trong source nhưng không được chạy trong lần hợp nhất; model thật, SQL opt-in, WebView2 trực tiếp và môi trường production chưa được nghiệm thu. Xem [mục kiểm thử hợp nhất](KIEM_THU_VA_NGHIEM_THU.md).

### Hợp nhất local-2 và main — 2026-09-10

Đã xử lý 21 khối xung đột trong 10 file, giữ Vietsub/Cloud/xuất MP4/phục hồi phiên cùng popup tạo dự án ngắn-dài, giọng Veo local và TikTok. Sửa thêm tương tác giữa khóa license và xác minh credential TikTok cho Admin. Xác minh source tại checkout riêng: Release build đạt; .NET **1185 Passed / 0 Failed / 5 Skipped**, frontend **174 Passed / 0 Failed / 0 Skipped**; TikTok state/browser **25 Passed / 0 Failed / 0 Skipped** và smoke popup đạt. Không có tác động database/provider hoặc thay binary đang chạy. Chi tiết và giới hạn: [biên bản hợp nhất](HOP_NHAT_LOCAL_2_MAIN_20260910.md).

### Loại bỏ lip-sync Cloud — 2026-09-10

Source giữ hai luồng: Canonical WAV riêng ghép vào video cảnh và Veo Native Audio đồng nhất giọng local. Thoại nhân vật Canonical đã duyệt WAV tiếp tục tạo video nền/ghép/duyệt, không chờ engine Cloud. Module Cloud và cờ build thử nghiệm đã xóa; trạng thái cũ chỉ đọc tương thích, migration/dữ liệu lịch sử được giữ. Kiểm thử trong checkout riêng: Release build đạt; .NET **1091 Passed / 0 Failed / 5 Skipped**, frontend **89 Passed / 0 Failed / 0 Skipped**. Server/desktop đang chạy chưa được thay bản; chưa nghiệm thu video provider thật. Xem [biên bản và phạm vi triển khai](LOAI_BO_LIP_SYNC_CLOUD.md).

### Bổ sung 2026-09-09 trên branch fix-voice

Đã bổ sung luồng thử nghiệm đồng nhất giọng Veo local: policy/migration 4.1.8, runtime/worker, anchor/job/retry/duyệt, UI và render guard. Từ 2026-09-10, `Features.VeoLocalVoiceConsistencyEnabled=true` trong cấu hình desktop của repository theo yêu cầu triển khai; flag chỉ mở tính năng, project vẫn phải bật riêng và runtime phải qua checksum/probe. Không tự chạy migration hoặc request provider có phí; chất lượng tiếng Việt/khẩu hình trên clip Veo thật còn phải nghiệm thu. Lịch sử triển khai nằm tại [KE_HOACH_TRIEN_KHAI_VEO_LOCAL.md](KE_HOACH_TRIEN_KHAI_VEO_LOCAL.md); tiến độ và bằng chứng mới nằm tại [TASK_TRIEN_KHAI_VEO_LOCAL_SU_DUNG_THUC_TE.md](TASK_TRIEN_KHAI_VEO_LOCAL_SU_DUNG_THUC_TE.md).

Theo yêu cầu và database người dùng chỉ định, migration 4.1.8 đã áp thành công trên DUNGDEV / VideoFactory ngày 2026-09-09 sau backup mới, checksum verification, restore thật và kiểm tra idempotency/constraint trên clone. Đã xác minh cột/constraint/version, dữ liệu 17 project cũ giữ nguyên và 0 project được tự bật local policy. Backup được giữ, database rehearsal đã dọn; chi tiết trong biên bản cuối nhật ký triển khai. Không bật feature hoặc coi việc áp schema là nghiệm thu ứng dụng/production; WebView2, quyền desktop thật và clip Veo tiếng Việt vẫn cần kiểm tra.

> Chỉ mục trạng thái liên module. Đối chiếu theo source sau hợp nhất ngày 2026-09-15.

Tài liệu này phân biệt rõ bốn mức: **đã có trong source**, **đã có kiểm thử tự động**, **đã xác minh thủ công trên môi trường**, và **đã rollout production**. Không được suy từ mức trước sang mức sau nếu thiếu bằng chứng.

## Nguồn sự thật

Khi có mâu thuẫn, dùng thứ tự sau:

1. Source code, project file, cấu hình mặc định và migration hiện hành.
2. [NGHIEP_VU_HE_THONG_VIDEOMAKER.md](NGHIEP_VU_HE_THONG_VIDEOMAKER.md) cho quy tắc nghiệp vụ.
3. [KIEN_TRUC_KY_THUAT.md](KIEN_TRUC_KY_THUAT.md) cho ranh giới module và đường gọi.
4. Tài liệu này cho trạng thái tích hợp, kiểm thử và rollout.
5. [VAN_HANH_VA_PHAT_HANH.md](VAN_HANH_VA_PHAT_HANH.md) và [KIEM_THU_VA_NGHIEM_THU.md](KIEM_THU_VA_NGHIEM_THU.md) cho quy trình thao tác.

Migration có trong repository không chứng minh migration đã chạy trên database thật. Test tự động đạt không thay thế smoke test có credential, model và môi trường thật.

## Trạng thái theo phạm vi

| Phạm vi | Trạng thái source | Bằng chứng còn thiếu trước rollout |
|---|---|---|
| Gateway AI theo tổ chức | Có auth, membership/role, ownership, pricing, budget, idempotency, credential version, reservation/settlement và request log | Rehearsal database, cấu hình từng môi trường, smoke và quan sát vận hành |
| OpenAI content/image/speech | Adapter, catalog, policy, content pacing, TTS/transcription và luồng quyết toán đã có | Credential/rate thật và smoke có kiểm soát; speech không phải fallback mặc định |
| Canonical Voice và speech verification | Voice profile/version, catalog/preview, TTS WAV, technical validation, audio mix/render và audited review đã có. Canonical Voice hỗ trợ ghép WAV cho cả lời dẫn và thoại nhân vật; module lip-sync Cloud đã loại bỏ. Video dài Provider Native nghe/duyệt trực tiếp và không gọi ASR | Migration 4.1.3–4.1.5, TTS/transcription rate theo scope, staging smoke và rollout flag; cần nghe nghiệm thu video thực tế |
| Kling video | Luồng video dài và dữ liệu project video ngắn Kling cũ còn để đọc/chuyển có xác nhận; Native Audio, polling, recovery và output proxy đã có. Video ngắn mới dùng Veo, không tự fallback Kling | Smoke trả phí theo model/policy được duyệt; xác minh chuyển project cũ và lineage |
| BytePlus Seedance | Adapter, polling và catalog đã có; seed mặc định `Disabled` | Rate, credential, allowlist output thực tế và rollout riêng |
| Fal/Veo | Adapter, polling và `SceneFirstFrame` Approved/current cho video dài `OpenAiStructuredPlan` và video ngắn `DirectShortVideo`; cả hai dùng policy scope `LongForm`. Seed mặc định `Disabled` | Migration 4.1.1 và 4.1.9 trên môi trường đích, policy/model/rate/credential/budget và smoke trả phí riêng từng mode |
| Video ngắn Veo | `TextOnly` và `CharacterOutfit` có quote/confirm/approval riêng cho ảnh đầu cảnh và clip; một scene 4/6/8 giây, 9:16 hoặc 16:9. `CharacterOutfit` có hai cờ server/desktop mặc định tắt; `TextOnly` vẫn dùng `vf.ShortVideoOperations` | Schema 4.1.9, first-frame lineage, review ảnh/video, provider paid smoke và render/export trên media thật |
| SePay/license/seat | Payment order, webhook matching, organization provisioning và seat allocation đã có trong source; mặc định `Enabled=false` | Staging rehearsal, secret/webhook validation, QR/bank config, idempotency và đối soát |
| TikTok Direct Post | Đã có OAuth, credential Admin, upload local, server polling và quản lý nhiều tài khoản. Báo cáo người dùng về luồng đăng cũ thuộc môi trường riêng, không phải smoke checkout này | Nhiều tài khoản cần migration 4.1.8, cấu hình/runtime tương ứng và smoke riêng trên môi trường được phép |
| Tải Bilibili | Menu, quét link video/kênh public, hàng đợi tải local và kiểm MP4/checksum/probe đã có; không phát sinh AI budget | Smoke mạng/kênh public trên môi trường đích, ghi rõ kết quả quét chưa đầy đủ khi nguồn giới hạn |
| Vietsub editor | Workspace local, manifest JSON schema 7 (SQLite schema 6), timeline/editor, thiết kế phụ đề và lật riêng hình video hai chiều theo project, mixer âm gốc/giọng Việt có mute/gain/auto-duck, preview hai kênh cùng playhead và xuất MP4 burn-in + audio mix qua ASS/FFmpeg với publish `.partial` đã kiểm tra | Smoke desktop trên bundle phát hành, nghe/đo audio output, nghiệm thu UX và đối chiếu preview/ASS/libass trên bộ video dọc-ngang, gồm cả hình lật và phụ đề không lật |
| System Setup khởi động | React modal hiển thị sau dashboard; nền bị khóa và host C# chặn command nghiệp vụ cho Owner/OrganizationAdmin/BillingManager/Member đến khi component không `DISABLED` đều `READY`. Viewer không thuộc gate | Smoke WebView2/Windows sạch, cài/sửa package thật, probe FFmpeg/OCR/Qwen/Piper và kiểm phục hồi/hủy |
| Paddle OCR local | Luồng OCR local và test liên quan đã có; feature mặc định bật | Runtime/model bundle thật, smoke Anh/Trung và đo tài nguyên |
| Dịch local Qwen | Worker x64 cô lập, IPC, readiness fingerprint, apply/retry/cancel và hai resource profile Standard/Low-memory đã có; RAM/commit thấp là cảnh báo có xác nhận được snapshot vào job, còn hard blocker thực tế vẫn chặn; feature mặc định tắt | Verify model thật, benchmark mức khuyến nghị Low-memory 6 GB, probe Anh/Trung và smoke desktop cả nhánh cảnh báo; test opt-in đang có thể `Skipped` |
| Giọng local Piper | Pipeline phrase/cache/checkpoint, worker Python cô lập, kiểm tra WAV/SHA-256, fit tối đa 1.20x với diagnostic không chặn, FFmpeg timeline, playback nội bộ và UI đã có; Piper VAIS-1000 vẫn là giọng dùng cho job tạo âm thanh phụ đề, feature mặc định bật nhưng runtime/model chỉ tải sau xác nhận | Verify runtime/model thật, kiểm kê license/dependency Python, benchmark CPU, nghe nghiệm thu và smoke desktop trên bundle phát hành |
| Danh mục giọng Vietsub local | Modal có 15 giọng Piper/Kokoro để kiểm tra/cài model theo voice ID và nghe WAV mẫu đóng gói trước khi cài. `READY` ở đây chỉ là size/SHA-256 của model trên máy; source CSP đã cho phép media `'self'`. Kokoro chưa có runtime/tạo giọng trong ứng dụng | Smoke nghe thử WebView2 sau sửa CSP, thử tải/probe model trên máy đích, rà soát quyền Kokoro/LarVoice và WAV mẫu trước phân phối |
| Đồng nhất giọng Veo local | Policy theo project, anchor/job/worker Python CPU, review và render guard đã có; desktop flag mặc định bật nhưng project/runtime vẫn phải qua gate riêng | Model/runtime thật, 2–3 clip tiếng Việt có quyền sử dụng, nghe A/B, khẩu hình/âm nền và hiệu năng trên máy đích |
| Dịch Cloud OpenAI | Contract/API/worker, budget Vietsub, apply CAS/SRT và CTA Cloud đã nối; 4.1.6 đã rehearsal và áp local `DUNGDEV / VideoFactory` ngày 2026-09-10 theo biên bản trước merge; cấu hình workspace kế thừa `local-2` bật `Enabled=true`, model `gpt-5.6-luna` | Xác minh schema/quyền/rate/credential/budget trên môi trường đích, nhiều instance SQL và smoke OpenAI có phí; merge không xác minh lại database hoặc gọi provider |
| Updater/setup/distribution | Source kiểm tra manifest, checksum, backup/rollback và package đã có | Bundle release thật, ký/phê duyệt, smoke install/update/rollback |
| Workflow media mở rộng | Một số lớp và UI nền đã có; local voice, mixer theo project và xuất MP4 burn-in có trộn timeline giọng Việt đã có trong source nhưng chưa rollout | Whisper, cloud translation và smoke nghe/đo bản xuất Vietsub trên FFmpeg bundle phát hành chưa hoàn tất end-to-end |

## Mặc định quan trọng

- Kling 3.0 Native Audio 720p là model mặc định **trong catalog source**; project video ngắn mới yêu cầu Fal/Veo theo policy `LongForm`, không dùng default Kling để fallback.
- OpenAI Text/Image/Voice và Kling có catalog hoạt động theo seed hiện hành; khả dụng thực tế còn phụ thuộc policy, rate và credential.
- BytePlus và Fal được seed `Disabled`, không tự bật khi deploy.
- Fal/Veo chỉ áp dụng `LongForm`, cần `SceneFirstFrame` Approved/current đúng tỷ lệ.
- `Generation:ShortVideoCharacterOutfit:Enabled=false` trên server và `Features:ShortVideoCharacterOutfitEnabled=false` trên desktop. Tắt hai cờ không miễn trừ schema 4.1.9 cho quote video ngắn `TextOnly`.
- Source server có `CanonicalVoiceEnabled=true` và `SpeechVerificationEnabled=true`; desktop có `SpeechSynchronizationEnabled=false`. Cấu hình runtime có thể ghi đè; flag không thay thế credential, rate, budget hoặc readiness.
- SePay mặc định `Payments:Sepay:Enabled=false`.
- Item TikTok mặc định hiển thị với `Features:TikTokEnabled=true`; server có `TikTok:AdminManagedCredentialsEnabled=true` nhưng chưa bật runtime khi không có credential database `Active`, giữ `TikTok:Enabled=false`, `TikTok:EmergencyDisabled=false`, `TikTok:AuditedForPublicPosting=false` và source không chứa secret.
- `TikTok:MultiAccountEnabled=true` là mặc định của workspace này; môi trường khác cần schema 4.1.8 và kiểm runtime/cấu hình riêng.
- Desktop có `VietsubEnabled=true`, `VietsubOcrEnabled=true`, `VietsubLocalTranslationEnabled=false`, `VietsubLocalVoiceEnabled=true`; máy chưa có Piper/model sẽ ở trạng thái `NOT_INSTALLED` và yêu cầu người dùng chủ động xác nhận cài.
- Desktop mặc định còn có connection string SQL workflow; đây là trạng thái chuyển tiếp, không phải kiến trúc đích.

## Thông báo lỗi license và đăng xuất — 2026-09-10

- API license bắt lỗi nghiệp vụ dự kiến tại controller, giữ HTTP status và `ApiErrorResponse`; lỗi bất ngờ vẫn qua global handler.
- Desktop phân biệt `SessionLimit`, `DeviceLimit` và `Unavailable`, báo ngay khi heartbeat từ chối quyền và xóa lease local. Giao diện có **Kiểm tra lại** / **Đăng xuất**, chống bấm đăng xuất lặp và giữ ngữ cảnh dự án khi bị khóa. Đăng xuất chỉ tác động phiên hiện tại, quay về màn hình đăng nhập.
- Thay đổi này xử lý thông báo/phục hồi; quy tắc đếm phiên trên server vẫn giữ nguyên. Việc tách phiên Admin Web khỏi suất desktop là hạng mục riêng.
- Restore, build Release/Debug toàn solution đạt, MSBuild 0 warning/error. Frontend 131 Passed / 0 Failed / 0 Skipped; .NET Release 1.029 Passed / 0 Failed / 3 Skipped. Ba bài model Qwen/Piper opt-in vẫn chưa chạy. Chi tiết và giới hạn bằng chứng nằm trong mục 15 của `KIEM_THU_VA_NGHIEM_THU.md`.

## Tạo giọng vượt thời gian câu bỏ qua — 2026-09-10

Theo yêu cầu tiếp tục tạo giọng bình thường, phần âm thanh tràn sang câu bỏ qua không còn làm job thất bại ở 72%. Renderer vẫn thử fit tối đa 1.20x và rút đuôi im lặng đã xác minh; nếu không đủ, giữ phần tiếng nói tràn và publish timeline với diagnostic không chặn. Câu bỏ qua vẫn không được gửi tới Piper. Tạo mới, thử lại job lỗi cũ và dựng lại từ cache dùng chung quy tắc; không thay fingerprint hoặc sửa WAV phrase đã lưu.

Regression dùng WAV/MP4 fixture và FFmpeg thật kiểm tràn 27/135 ms, tắt phân tích khoảng lặng, câu bắt đầu trong vùng bỏ qua, cache/đổi lựa chọn, giữ hash và phần giọng cuối trong file xuất. Build solution Release và desktop Debug đạt; .NET **1.030 Passed / 0 Failed / 3 Skipped**, frontend **131 Passed / 0 Failed / 0 Skipped**. Ca thử lại job lỗi cũ hoàn thành 100% bằng cache, không gọi synthesizer thêm. Chi tiết ở mục 16 của `KIEM_THU_VA_NGHIEM_THU.md`; ba bài model opt-in vẫn chưa chạy, chưa thay thế nghe nghiệm thu Piper trên video người dùng.

## Nút xuất video trong editor Vietsub — 2026-09-10

Đã thêm nút **Xuất video** ngay bên phải nút thiết kế/menu tệp trên toolbar phụ đề và một nút ở cuối thanh công cụ Timeline, giữ nhãn ở panel hẹp. Phần đầu card được thu gọn: tiêu đề cùng hàng với các nút, số câu/trạng thái/cảnh báo nằm ở hàng nhỏ bên dưới. Hai nút dùng chung luồng lưu bản nháp trước khi xuất MP4, trạng thái đang xuất và khóa bấm lặp; phục hồi sau kết thúc/hủy/lỗi. Kết quả hiển thị trong thông báo của editor; không đổi renderer hoặc contract C#.

Đã sửa lỗi xuất xong vẫn loading: route C# bật `notifyCompletion` để gửi `vietsub.operation.completed` sau kết quả và state hết busy, giải phóng Promise mà hai nút đang chờ. Thông báo thành công có tên file, hủy/lỗi vẫn cho phép thử lại. Regression bridge và frontend kiểm chuỗi message thực tế; kiểm thử nút với Promise giả trước đó chưa bắt được thiếu sót này.

Cụm Âm gốc/Giọng Việt/Tự hạ nền trên Timeline đã được căn sang phải, sát nhóm Theo playhead với khoảng cách 12 CSS px khi đủ rộng. Màn hình hẹp tiếp tục co hoặc chuyển hàng; xem xác minh bố cục ở mục 22 của `KIEM_THU_VA_NGHIEM_THU.md`.

Xác minh cuối: frontend **138 Passed / 0 Failed / 0 Skipped**; .NET **1.033 Passed / 0 Failed / 3 Skipped** với TEMP/TMP riêng có đường dẫn ngắn trên D. Build Release/Debug đạt, bundle desktop khớp Web/dist. WebView2 đã xác minh card gọn ở panel 320/420 px, nút Timeline ở các chiều rộng 420–1160 px và zoom 100/125/150/200%; xem mục 17–19 của `KIEM_THU_VA_NGHIEM_THU.md`. Chưa xuất file từ project người dùng; chạy lại desktop để nạp DLL mới.

## Quay về tạo dự án Vietsub — 2026-09-10

Nút **Tạo dự án mới** có mũi tên quay lại, nằm trong phần đầu card **Thiết lập dự án**, cạnh tiêu đề khi đủ rộng và tự xuống hàng ở panel hẹp. Đã bỏ hàng điều hướng riêng cùng nhãn/mô tả lặp trong card để thu gọn; trên cửa sổ hẹp mở tab **Thiết lập** để truy cập nút. Nút lưu bản nháp rồi gọi `vietsub.project.close` hiện có; khi đóng xong, trang chuyển về thư viện có form nhập tên dự án. Không tự tạo project khi bấm quay về. Chặn bấm lặp/khi đang xử lý hoặc xuất video; lưu lỗi giữ bản nháp, đóng lỗi cho thử lại.

Frontend **141 Passed / 0 Failed / 0 Skipped**; .NET **1.033 Passed / 0 Failed / 3 Skipped**. Restore, build Release solution và Debug desktop đạt; bundle hai cấu hình khớp Web/dist. Kiểm giao diện bằng bundle production với bridge giả: đầu card cao 49 CSS px ở panel mặc định, 76 px khi thu panel xuống 220 px; nút hiện trong tab Thiết lập ở 720×900/zoom 125% và thao tác quay về form ở 1200×900 đạt. Xem mục 20–21 của `KIEM_THU_VA_NGHIEM_THU.md`. Cần chạy lại desktop để nạp bundle mới.

## Database hiện hành

Chuỗi migration đang có trong source:

1. `VideoFactory.Initial.sql`
2. `VideoFactory.4.0.0.OrganizationAiGateway.sql`
3. `VideoFactory.4.0.1.VietnameseSeedTextRepair.sql`
4. `VideoFactory.4.0.2.GptImageCharacterReference.sql`
5. `VideoFactory.4.0.3.SceneVoiceTts.sql`
6. `VideoFactory.4.0.4.BytePlusSeedance.sql`
7. `VideoFactory.4.0.5.SceneNativeAudioStatuses.sql`
8. `VideoFactory.4.0.6.NativeAudioWorkflowStatuses.sql`
9. `VideoFactory.4.0.7.ProjectAssetTextLibrary.sql`
10. `VideoFactory.4.0.8.AiGeneratedProjectAssets.sql`
11. `VideoFactory.4.0.9.FalVeoLongForm.sql`
12. `VideoFactory.4.0.10.LicenseSepayPayments.sql`
13. `VideoFactory.4.0.11.OrganizationSeatProvisioning.sql`
14. `VideoFactory.4.1.0.VietsubProjectRegistry.sql`
15. `VideoFactory.4.1.1.SceneFirstFrames.sql`
16. `VideoFactory.4.1.2.ProviderRequestFailureDetails.sql`
17. `VideoFactory.4.1.3.SpeechSynchronization.sql`
18. `VideoFactory.4.1.4.VoiceProfileApproval.sql`
19. `VideoFactory.4.1.5.SpeechVerificationReview.sql`
20. `VideoFactory.4.1.6.VietsubCloudTranslation.sql`
21. `VideoFactory.4.1.6.TikTokPublishing.sql`
22. `VideoFactory.4.1.7.TikTokAdminCredentials.sql`
23. `VideoFactory.4.1.8.TikTokMultiAccount.sql`
24. `VideoFactory.4.1.8.LocalVoiceConsistency.sql`
25. `VideoFactory.4.1.9.ShortVideoCharacterOutfit.sql`

Các migration lip-sync Cloud 4.1.6/4.1.7 được giữ để tương thích dữ liệu lịch sử; module thực thi đã loại bỏ. Migration cùng tiền tố số của các module dùng mã SchemaVersions riêng.

`VideoFactory.DesktopLeastPrivilege.sql` cấp quyền chuyển tiếp cho desktop; `Verify.VideoFactory.4.0.11.OrganizationSeatProvisioning.sql` là script xác minh chuyên biệt. Không có bằng chứng trong repository rằng toàn bộ chuỗi trên đã được áp dụng vào production.

## Mốc kiểm thử lịch sử

Mốc được tài liệu hóa ngày 2026-09-06, chỉ dùng làm tham chiếu lịch sử:

- Web: 29/29 test đạt và production build đạt.
- Restore đạt; Release build không warning/error.
- .NET: 802 passed, 0 failed, 2 skipped, 804 total khi TEMP/TMP đặt trên ổ D.

Các lệnh này chưa được chạy lại chỉ vì chuẩn hóa Markdown. Hai test model/benchmark bị skip không chứng minh dịch local đã sẵn sàng.

Mốc triển khai local voice ngày 2026-09-07:

- Web: 31/31 test đạt và production build đạt.
- Restore đạt; Release build toàn solution đạt với 0 warning, 0 error.
- .NET: 814 passed, 0 failed, 3 skipped, 817 total.
- Ba test opt-in bị skip gồm Qwen model integration, Qwen benchmark và Piper model integration; không test nào trong ba test này được coi là đã đạt.

Mốc chuyển RAM dịch local thành cảnh báo có xác nhận ngày 2026-09-07:

- Web production build đạt; 32/32 test đạt.
- Restore đạt; Release build toàn solution đạt với 0 warning, 0 error.
- .NET: 820 passed, 0 failed, 3 skipped, 823 total.
- Ba test opt-in vẫn bị skip gồm Qwen model integration, Qwen benchmark và Piper model integration. Nhánh xác nhận RAM đã có fake-worker/bridge test nhưng chưa thay thế verify model thật hoặc smoke desktop trên máy ít RAM.

Mốc hợp nhất `main` vào `local-2` ngày 2026-09-07:

- `dotnet restore` đạt; Release build toàn solution đạt với 0 warning, 0 error.
- .NET: 939 passed, 0 failed, 3 skipped, 942 total.
- Frontend: production build đạt; 12/12 test file và 64/64 test đạt.
- Ba test opt-in bị skip gồm một Piper model integration, một Qwen model integration và một Qwen benchmark; không test nào trong ba test này được coi là model/runtime đã nghiệm thu.
- Các lệnh trên xác minh source sau hợp nhất; chưa chạy migration database, provider có phí, model thật hoặc smoke desktop.

Rà soát lỗi bỏ qua câu tạo giọng ngày 2026-09-09 trên dirty worktree hiện hành:

- `dotnet restore` đạt; Release build toàn solution đạt với 0 warning và 0 error của MSBuild. Vite vẫn cảnh báo chunk lớn hơn 500 kB.
- .NET: 970 passed, 0 failed, 3 skipped, 973 total. Ba test bỏ qua là Qwen integration, Qwen benchmark và Piper integration; không tính là model đã đạt.
- Frontend: `npm ci`, production build và 99/99 test đạt, 0 failed, 0 skipped.
- Chín ca hồi quy mới chạy FFmpeg thật, gồm rút đuôi dưới ngưỡng tín hiệu, chặn cắt tiếng, ranh giới chồng/tiếp giáp, tạo mới, cache/bật lại, tiến độ lỗi và giải mã MP4 có/không trộn nền. Sửa timestamp và giới hạn padding theo số mẫu để giữ khoảng im lặng và thời lượng timeline.
- Kiểm tra riêng WAV cache gây lỗi bằng renderer mới: 1832 ms sau trim đầu được rút còn 1800 ms, fit cửa sổ 1500 ms ở 1.20x; output đủ 9200 ms, mẫu âm thanh trong đoạn bỏ qua bằng 0 và SHA-256 của WAV đầu vào giữ nguyên. Đầu ra kiểm tra được ghi riêng; không sửa database/project đang sử dụng.
- Bằng chứng .NET: `artifacts/voice-boundary-verification-20260909/voice-boundary-final.trx` (artifact local, không commit). Chưa chạy sinh giọng bằng model thật hoặc nghe nghiệm thu thủ công trong desktop.

Rà soát hiển thị timeline Giọng Việt ngày 2026-09-09 trên dirty worktree hiện hành:

- Tách class nhãn khỏi waveform, tăng chiều cao dòng và khoảng đệm để chữ `Không tạo giọng` không bị cắt chân. Test WebView2 dùng component và CSS production tái hiện cả lỗi cắt chữ lẫn mất sóng trước sửa, đạt sau sửa tại zoom 100%, 125%, 150% và 200%.
- `npm ci`, frontend production build và 99 test đạt, 0 failed, 0 skipped. Restore, Release build toàn solution và Debug build desktop đạt; hai bundle desktop khớp Web/dist. Vite vẫn cảnh báo chunk lớn hơn 500 kB.
- .NET: 971 passed, 0 failed, 3 skipped, 974 total. Ba test model Qwen/Piper opt-in vẫn bị bỏ qua; không tính là model đã đạt. Bằng chứng local: `artifacts/timeline-label-layout/timeline-layout-final.trx` và ảnh WebView2 trong `artifacts/timeline-label-layout/after/`.

Triển khai Dịch Cloud OpenAI, xác minh cuối ngày 2026-09-10 (Asia/Bangkok):

- Đã nối CTA Cloud một lần bấm qua gateway/server OpenAI, job/batch/attempt, readiness và ngân sách riêng cho project Vietsub. Desktop áp kết quả bằng fingerprint/receipt, giữ cue manual/locked và ghi SRT atomically; Local vẫn độc lập.
- Frontend: 23 file, 126 Passed / 0 Failed / 0 Skipped; `npm ci` và production build đạt. Restore, Release build solution và Debug build desktop đạt, MSBuild 0 warning/error. Bundle Debug/Release khớp SHA-256 với Web/dist; Vite vẫn cảnh báo chunk >500 kB.
- Full .NET suite cuối chạy tuần tự collection: 1.014 Passed / 0 Failed / 3 Skipped / 1.017 Total. Ba test Qwen/Piper opt-in không được tính là model đã đạt. Fixture bổ sung kiểm mất POST/ACK, Unknown/đối soát, chống quyết toán lặp từ context cũ và bảo vệ sửa tay.
- WebView2 Cloud đạt tại zoom 100/125/150/200%, kể cả khi Local chưa sẵn sàng; ảnh/JSON local ở `.tmp/cloud-verification-20260909`. Đây là fixture UI, chưa có OpenAI thật.
- Cloud giữ `Enabled=false`, `ModelCode` trống. Chưa apply migration 4.1.6, thử nhiều instance SQL Server hoặc smoke OpenAI có phí. Xem [báo cáo kiểm thử](KIEM_THU_VA_NGHIEM_THU.md#13-xác-minh-triển-khai-dịch-cloud-openai--2026-09-10) và [runbook Cloud](HUONG_DAN_VAN_HANH_DICH_CLOUD_VIETSUB.md).

Database Dịch Cloud cập nhật ngày 2026-09-10 sau vòng kiểm thử source:

- Theo yêu cầu chạy database, đã xác minh đích cấu hình `DUNGDEV / VideoFactory`, tạo backup COPY_ONLY/CHECKSUM mới, VERIFYONLY và restore sang clone riêng. `DBCC CHECKDB` clone đạt; clone đã dọn sau xác minh, backup giữ trong thư mục backup SQL Server.
- Rehearsal phát hiện thiếu SET options cho filtered index khi chạy sqlcmd; đã bổ sung vào migration 4.1.6 chưa áp đích. Script sau sửa chạy hai lần trên clone và một lần trên đích đều đạt.
- Đối chiếu 81 bảng cũ: chỉ thêm version `4.1.6`; 62 reservation, 178 dòng ledger và toàn bộ quyền/role membership giữ nguyên. Ba bảng Cloud mới, FK/CHECK trusted và unique filtered index đã xác minh. Các migration TikTok/lip-sync của nhánh khác được giữ nguyên.
- Test migration 1 Passed / 0 Failed / 0 Skipped. Chưa chạy OpenAI, đổi cấu hình hoặc restart ứng dụng; Cloud vẫn tắt. Xem [báo cáo database](.tmp/cloud-database-20260910_003325/REPORT.md). Kiểm nhiều instance và nghiệm thu model/ứng dụng thật vẫn còn mở.

Cải thiện biên tập phụ đề ngày 2026-09-09: hoàn thành Task 1–5 trong [KE_HOACH_CAI_THIEN_BIEN_TAP_PHU_DE.md](KE_HOACH_CAI_THIEN_BIEN_TAP_PHU_DE.md). Cuộn được tập trung tại danh sách, tạm ngưng theo thao tác người dùng, giữ draft/focus qua revision và từ chối response trang cũ. Banner có nút × theo vòng đời sự kiện/project; danh sách đã bỏ chọn giọng hàng loạt và checkbox, giữ menu từng câu/timeline và API batch bên dưới.

- Frontend: 119 Passed / 0 Failed / 0 Skipped; production build đạt. Restore và Release build toàn solution đạt, MSBuild 0 warning/error; Debug desktop đạt và bundle hai cấu hình khớp Web/dist. Vite vẫn cảnh báo chunk >500 kB.
- .NET cuối chạy tuần tự collection: 972 Passed / 0 Failed / 3 Skipped / 975 Total. Ba test model opt-in Piper/Qwen không được tính là đã đạt. Một lượt song song trước đó có timeout preflight worker; kiểm tra riêng lại và full suite tuần tự đều đạt, không sửa worker/timeout.
- WebView2 editor/Enter, playback và timeline layout đạt. Fixture trang 50 câu/panel 420 px/zoom 100% giảm render row từ 1200 xuống 33, remount từ 50 xuống 0 và độ lệch cuộn tay từ 3032 px xuống 0. Số đo/ảnh ở zoom 100/125/150/200% và bằng chứng `artifacts/subtitle-editor-ui/final-suite-serial.trx` là artifact local. Chưa chạy model thật hoặc nghe video người dùng; không có tác động database/provider.

Rà soát playback Vietsub ngày 2026-09-09: editor và modal dùng chung đồng bộ video/giọng, phục hồi khi giọng tải muộn hoặc tua lại, không chặn video khi AudioContext chờ khởi động; mở lại nguồn giữ đúng lựa chọn giọng/mixer. Registry phát giọng thay entry nguyên tử; lỗi tải/phát hiển thị nút thử lại. Có regression DOM và WebView2 chạy hook production với WAV mẫu; việc nghe Piper trên video người dùng vẫn là bước nghiệm thu riêng.

Cập nhật tool dịch ngày 2026-09-09: đã bổ sung lựa chọn bỏ qua/bật lại giọng theo câu và batch, giữ phụ đề/timing, SQLite schema 6 với migration mặc định bật cho cue cũ. Timeline thể hiện câu bỏ qua; Piper chỉ nhận câu đã chọn; cache được kiểm tra lại khi đổi lựa chọn và xuất MP4 chặn dùng giọng cũ. Preview hạ âm gốc theo tín hiệu giọng thay vì sự hiện diện của phụ đề. Các thay đổi này vẫn cần nghe nghiệm thu bằng Piper và video thực tế trên desktop; không thay thế cổng model/runtime opt-in.

Mốc triển khai item **Đăng TikTok** ngày 2026-09-07:

- `dotnet restore` đạt; Release build toàn solution đạt với 0 warning, 0 error.
- .NET: 965 passed, 0 failed, 3 skipped, 968 total; riêng test có tên TikTok: 26 passed, 0 failed, 0 skipped.
- Frontend: production build đạt; 13/13 test file và 66/66 test đạt. Vite còn cảnh báo chunk JavaScript lớn hơn 500 kB, không phải lỗi build.
- Ba test opt-in bị skip vẫn là Qwen model integration, Qwen benchmark và Piper model integration; không được tính là model/runtime đã đạt.
- Migration 4.1.6 đã được áp dụng trên database local có backup được xác minh; không cấu hình TikTok secret thật, không gọi Content Posting API và chưa smoke desktop/TikTok account.

Mốc bổ sung quản lý TikTok Developer App trong Global Admin ngày 2026-09-08:

- `dotnet restore` đạt; Release build toàn solution đạt với 0 warning, 0 error.
- .NET: 977 passed, 0 failed, 3 skipped, 980 total; riêng test có tên TikTok: 38 passed, 0 failed, 0 skipped.
- Frontend: production build và 66/66 test đạt. Vite còn cảnh báo chunk JavaScript lớn hơn 500 kB, không phải lỗi build.
- Ba test opt-in bị skip là Qwen model integration, Qwen benchmark và Piper model integration; không được tính là model/runtime đã đạt.
- Migration 4.1.7 đã được áp dụng idempotent trên database local sau khi tạo full backup `COPY_ONLY/CHECKSUM` và `RESTORE VERIFYONLY` đạt; chưa gọi TikTok thật và chưa smoke desktop/tài khoản TikTok.

## Việc còn mở ưu tiên

1. Rehearsal chuỗi migration cần cho binary hiện hành đến 4.1.9 trên bản sao database, gồm project kind của budget/ledger, concurrency Cloud và quote video ngắn `TextOnly`/`CharacterOutfit`; xác minh least-privilege và rollout từng môi trường có backup/restore đã thử.
2. Đăng ký/review TikTok app, xác minh credential bằng OAuth Admin trên Desktop và smoke nhiều tài khoản/upload/status trên môi trường được phép.
3. Cấu hình rate/credential/policy/budget và smoke riêng cho từng provider; không gộp Kling, BytePlus và Fal thành một cờ hoàn tất.
4. Rehearsal SePay ở staging, gồm duplicate webhook, late payment, seat shortage và rollback vận hành.
5. Rehearsal Canonical Voice trên staging: migration, catalog preview, pacing, TTS WAV, mix/render, export và xác nhận video dài Provider Native không gọi ASR.
6. Verify/benchmark model Qwen thật, probe runtime/Anh/Trung và smoke CTA dịch trên desktop x64.
7. Verify model/runtime Piper thật, kiểm kê dependency/license, benchmark CPU, nghe nghiệm thu và smoke tạo giọng trên desktop x64.
8. Đưa phần workflow desktop còn dùng SQL trực tiếp qua server API và thu hẹp/bỏ database role desktop.
9. Hoàn thiện health/metrics/alert cho worker, budget, polling, cache và webhook.
10. Phê duyệt bundle FFmpeg ở scope Release, rồi smoke install/update/rollback.
11. Nghiệm thu thủ công Admin responsive và các workflow UI chính.

## Bộ tài liệu chuẩn

- [README.md](README.md): điểm vào repository.
- [NGHIEP_VU_HE_THONG_VIDEOMAKER.md](NGHIEP_VU_HE_THONG_VIDEOMAKER.md): nghiệp vụ và bất biến.
- [KIEN_TRUC_KY_THUAT.md](KIEN_TRUC_KY_THUAT.md): kiến trúc và đường gọi.
- [VAN_HANH_VA_PHAT_HANH.md](VAN_HANH_VA_PHAT_HANH.md): triển khai, vận hành, rollback, release.
- [KIEM_THU_VA_NGHIEM_THU.md](KIEM_THU_VA_NGHIEM_THU.md): test matrix và Definition of Done.

Các `AGENTS.md` theo thư mục vẫn có hiệu lực. Các file provenance, license và nguồn ảnh bên thứ ba là hồ sơ bắt buộc, không phải tài liệu lịch sử để gom/xóa.

## Cập nhật nhiều tài khoản TikTok — 2026-09-09

Source đã có quản lý nhiều tài khoản, OAuth reconnect đúng danh tính, ClientRequestId riêng mỗi lần đăng, durable publish attempts, lịch sử/account snapshot, worker claim và bridge chống phản hồi đến muộn. Theo yêu cầu trực tiếp của người dùng, migration `4.1.8-tiktok-multi-account` đã áp vào `DUNGDEV / VideoFactory` và xác minh lúc 15:35 UTC ngày 2026-09-09. Sau đó đã bật `TikTok:MultiAccountEnabled=true` trong `TOOL-SERVER/appsettings.json` của workspace theo yêu cầu người dùng.

Kiểm tra tại workspace: Release build thành công (0 warning C#, 0 error); .NET 1029 passed / 0 failed / 3 skipped với SQL test opt-in; frontend 77 passed / 0 failed; browser desktop 7 passed, Admin state/browser 18 passed. Model Qwen/Piper bị skip không được tính là đạt. Vite vẫn cảnh báo bundle lớn hơn 500 kB.

Đã rehearsal migration 4.1.8 hai lần và khóa đồng thời trên instance SQL Server LocalDB riêng với dữ liệu giả. Khi áp vào database đang dùng, đã backup COPY_ONLY/CHECKSUM, VERIFYONLY, restore bản sao đầy đủ, chạy migration hai lần trên bản sao và DBCC CHECKDB đạt. So sánh trước/sau trên database đích xác nhận giữ nguyên 1 kết nối, 2 job, OAuth/app credential và 1 Data Protection key. Bản sao thử đã dọn, backup được giữ lại. Khi bật cờ, server đang chạy từ checkout `Branch-Tool-Sub`, khác workspace hiện tại; chưa restart server đó hoặc xác minh cờ trên runtime. Chưa OAuth/đăng thật nhiều tài khoản và chưa phát hành. Người dùng đã báo đăng được bằng luồng cũ; đó không phải kết quả smoke nhiều tài khoản của agent. Chi tiết và đường dẫn backup: [TRIEN_KHAI_TIKTOK_NHIEU_TAI_KHOAN.md](TRIEN_KHAI_TIKTOK_NHIEU_TAI_KHOAN.md).

## Admin setup 3 bước — cập nhật 2026-10-14

Đã triển khai source trên `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b` và working tree chưa commit: form ba bước theo mockup, SePay riêng, nháp/receipt, kiểm model/rate/credential/budget/capacity, publication nguyên tử, profile gói ở offers/reserve, ngừng bán giữ license cũ. Theo yêu cầu tạo bảng đơn giản, chọn migration mới 4.1.15 thay bước khởi tạo 4.1.14, giữ nguyên quyền database; kiểm thử SQL cô lập **5 Passed / 0 Failed / 0 Skipped**. Theo đích người dùng chỉ định sau đó, **đã áp 4.1.15 vào DUNGDEV / VideoFactory lúc 2026-10-14 11:50:04 UTC**: backup CHECKSUM, VERIFYONLY, restore thật/CHECKDB, chạy hai lần trên clone, giữ số dòng cũ/quyền và kiểm schema sau áp đều đạt; clone đã dọn, backup giữ lại. Database hosting `WIN-FQJ43SS9NHL\MSSQLSERVER2019 / tap51004_tool` chỉ được preflight đọc, chưa áp migration. Smoke wizard/runtime và rollout production: **chưa xác minh**. Bằng chứng SQL theo từng môi trường, kiểm thử và ảnh render: [TRIEN_KHAI_ADMIN_SETUP_3_BUOC.md](TRIEN_KHAI_ADMIN_SETUP_3_BUOC.md); không dùng các baseline lịch sử phía trên thay kết quả lượt này.

Cập nhật theo yêu cầu dùng hosting và chỉ tạo hai bảng: **đã áp `Create.AdminSetupTables.sql` vào 112.78.2.70 / tap51004_tool lúc 2026-10-14 13:52:55 UTC**. Hai bảng wizard đủ 22 cột, 2 PK/5 FK trusted/4 CHECK trusted/index; kết nối độc lập đọc đủ cột đạt, không còn transaction mở. Đối chiếu source: 87/87 bảng mapped có mặt, tổng 105 bảng. File không phân quyền, không ghi dữ liệu nghiệp vụ hoặc `SchemaVersions`; version 4.1.14/4.1.15 vẫn chưa ghi trên hosting. Backup/restore hosting chưa xác minh; không dùng kết quả DUNGDEV thay thế. Không sửa code/kết nối hoặc restart runtime; smoke wizard và thanh toán thật chưa xác minh. Chi tiết và bằng chứng mới trong biên bản Admin setup.
