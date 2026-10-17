# Phát hành trực tiếp từ VPS — 2026-10-17

Source được rà soát trên nhánh `main`, HEAD nền `f0651c918f28ca477f28875818148e9e86240b7b`, cùng working tree chưa commit có thay đổi từ các lượt trước. Source/kiểm thử trên máy phát triển và SQL hosting là các phạm vi riêng: [4.1.20 Simple đã áp và xác minh hosting](TRIEN_KHAI_SQL_4_1_20_DON_GIAN.md), không tự suy ra server mới đã deploy, Setup đã Active hoặc model đã nghiệm thu trên Windows sạch.

## Kết quả nghiệp vụ

1. Publish `TOOL-LOCAL` bằng `FolderProfile` như bình thường. Hook tự tạo **một ZIP ứng dụng** với tên `VideoMaker-<version>-<build>-win-x64-<SHA256>.zip`; không yêu cầu nhập checksum hay nạp JSON trong Admin.
2. Upload nguyên ZIP lên VPS, giữ nguyên tên. Admin nhập **phiên bản + URL HTTPS ZIP**, tùy chọn ghi chú/bắt buộc cập nhật, rồi lưu nháp hoặc phát hành.
3. Hosting xác thực quyền, URL theo allowlist, tên/version/build/hash khai báo và Setup tương thích, rồi lưu database. Không HEAD/GET/DNS tới ZIP VPS, không tạo preparation, không chờ kiểm ZIP. Hosting không báo đã xác minh bytes hay dung lượng ứng dụng.
4. Setup dùng chung **1.2.0**, capability `3,4`, lấy catalog từ hosting và tải ứng dụng/model trực tiếp từ VPS. Setup kiểm SHA toàn ZIP trước khi đọc binary; kiểm layout, native version/build, API/profile, media/runtime, tạo metadata tại staging và dùng transaction/probe/recovery hiện có. Chỉ commit sau probe và kiểm lại exact catalog.
5. Desktop mới nhận cập nhật theo version và chuyển giao cho Setup bằng IPC có owner/PID/SID/nonce/ACK. Repair lấy đúng version/build/hash; UI không hiển thị dung lượng ứng dụng giả khi server chưa biết size.

SHA trong tên là **kỳ vọng do công cụ build công bố**, không phải kết quả hosting tải lại. Admin tin nguồn build mình upload; Setup so bytes tải về với kỳ vọng lấy qua API HTTPS. Nếu đổi bytes, phải Publish/tạo release mới. Link ZIP cũ không có SHA trong tên không đáp ứng catalog mới; dùng ZIP canonical mới được tạo tự động. Không được tự sửa chuỗi hash trong tên để bỏ qua kiểm tra.

## Ranh giới và tương thích

- Catalog mới có DTO riêng, protocol **4**, bảng `auth.DesktopDirectReleases`; không tạo size/build/proof giả trong bảng/artifact cũ.
- Distribution binary ứng dụng, native model policy và Setup vẫn là **3**. Chỉ capability catalog thêm `3,4`, lưu dạng chuỗi để snapshot/record equality ổn định.
- Setup 1.0/1.1 thiếu capability mặc định `3`. Endpoint legacy và bản đã phát hành protocol 2/3 còn được đọc; DirectCatalog chặn tạo/retry/publish inspection mới. Mode `ServerInspection` vẫn giữ source để tương thích.
- Một Setup đã Active có thể phục vụ nhiều ZIP tương thích; không build Setup lại cho mỗi version ứng dụng. Setup cũ còn hiệu lực không tự biến thành binary mới khi Setup khác Active.
- ZIP flat hoặc một wrapper được hỗ trợ; đường direct không nhận bộ ba metadata phát hành cũ lẫn trong ZIP. Thư mục staging có metadata được Setup sinh sau khi hash/layout/native identity hợp lệ.
- File ứng dụng có giới hạn 2 GiB; ZIP model pinned hiện tại khoảng 2,65 GiB dùng chính sách tải model riêng, không áp nhầm giới hạn ứng dụng.
- Giữ TLS xác thực bình thường, allowlist nguồn đã biên dịch, `.part`, ETag/Range, timeout/retry/cancel, kiểm giới hạn streaming, hash và promote atomic. Không bỏ qua chứng chỉ HTTPS.
- Readiness vẫn dựa probe máy đích; không phân phối cache Python, môi trường `.venv`, marker READY hoặc model trong ZIP ứng dụng.

## Source chính

- `TOOL-SHARED.Contracts/Updates/DesktopDirectCatalogContracts.cs`: DTO riêng, trạng thái Draft/Published, capability/minimum Setup.
- `TOOL-SERVER/Updates/DesktopDirectCatalogService.cs` và ba controller direct: Admin CRUD/publish/deactivate, catalog công khai, update/repair.
- `database/VideoFactory.4.1.20.DirectDesktopCatalogSimple.sql`: đường khởi tạo schema hiện được chọn và đã áp hosting; không thao tác quyền. `4.1.19.DirectDesktopCatalog.sql` giữ nguyên cho lịch sử nhánh có kiểm phân quyền.
- `TOOL-DISTRIBUTION/Updates/DesktopDirectCatalogPolicy.cs`, `DesktopDirectPackageResolver.cs`: danh tính canonical, archive/native validation, metadata local.
- `TOOL-SETUP/LauncherInstallerService.Direct*.cs`, `SetupMaintenanceHandoff.Direct.cs`: tải trực tiếp và bàn giao/cài đặt.
- `TOOL-LOCAL/Updates/DesktopInstalledMaintenanceService.Direct.cs`, `Form1.cs`, React `App.tsx`/types: cập nhật/sửa và trạng thái giao diện.
- `scripts/DesktopCanonicalPackage.psm1`, `New-DesktopCanonicalPackage.ps1`, `TOOL-LOCAL/Build/VisualStudioRelease.*`: Publish tự đóng ZIP, kiểm từng entry và toàn ZIP, không sửa artifact cũ.
- Form Admin được rà soát theo [ui-ux-pro-max](C:/Codex/20x/skills/ui-ux-pro-max/SKILL.md): liên kết lỗi với trường, giữ dữ liệu khi lỗi, khóa thao tác trùng, bàn phím và màn hình hẹp.

## Đưa lên hosting

Đường SQL hiện được chọn theo yêu cầu đơn giản hóa: dùng [4.1.20 Simple](database/VideoFactory.4.1.20.DirectDesktopCatalogSimple.sql) sau 4.1.18 để tạo cùng bảng/khóa/index, không thao tác quyền. Exact script đã commit trên `MB2D70\MSSQLSERVER2019 / tap51004_tool` lúc **2026-10-17 07:47:34 UTC**, kết nối mới xác minh đạt lúc **07:48:52 UTC**: 24 cột, 5 CHECK đúng/trusted, PK cùng 3 index, marker 418/419/420 mỗi mã một dòng, catalog 0 dòng. Dữ liệu/schema bốn bảng phát hành cũ và quyền nhìn thấy giữ nguyên. Không cần build lại ứng dụng chỉ vì dùng script này. [Phạm vi, bằng chứng và giới hạn backup/restore hosting](TRIEN_KHAI_SQL_4_1_20_DON_GIAN.md).

Lịch sử nhánh ban đầu: đã thử exact 4.1.19 theo yêu cầu người dùng; hosting từ chối lệnh DENY (15151), transaction rollback và kết nối mới khi đó xác minh bảng/marker chưa có. [Biên bản SQL 4.1.19 và file DBA](TRIEN_KHAI_SQL_4_1_19_HOSTING.md) giữ nguyên lịch sử; không chạy tiếp file này để khởi tạo sau khi đã chọn và áp Simple.

1. Đối chiếu SQL đích và [biên bản 4.1.20 Simple](TRIEN_KHAI_SQL_4_1_20_DON_GIAN.md) đã áp/xác minh. Backup/restore và clone hosting vẫn chưa xác minh; kết quả synthetic QA không thay bằng chứng này. Không cần chạy lại nhánh 4.1.19/DBA.
2. Build/deploy `TOOL-SERVER`, bảo toàn secret, Data Protection keys và storage Setup đang có trên hosting. Không chép connection string từ máy build.
3. Cấu hình nguồn đã thêm `DesktopReleases.PublicationMode=DirectCatalog`, shared enabled, API `https://taphoatool.com/`, profile Production, prefix `https://103.179.190.182:8443/models/`. Kiểm override môi trường hosting vì environment/appsettings.Production có thể ghi đè source.
4. Đăng ký file Setup mới 1.2.0 qua mục Setup hệ thống và Active. Server kiểm file Setup **nội bộ hosting**, không tải ZIP ứng dụng VPS.
5. Upload ZIP canonical mới, nhập đúng version và URL, lưu/phát hành. Không cần chờ kiểm ZIP.
6. Tải file Setup hệ thống, cài trên Windows kiểm thử phù hợp. Kiểm mất mạng/resume, sai hash, thiếu dung lượng, model/probe, retry/repair, ứng dụng cũ và rollback trước khi phân phối rộng.

Không cần upload lại bộ model nếu bytes/URL/hash pinned không đổi. VPS hiện phục vụ thư mục `/srv/videomaker-models/models-20261016-03` ở `/models/`. Source/binary mới chỉ được xác nhận rollout sau khi truy cập môi trường đích và kiểm thực tế.

## Bằng chứng lượt này

Thư mục bằng chứng: `D:\vm-direct-catalog\20261017-140233`. Source snapshot gồm **1.383 file**, manifest `source-final-before.json`, SHA-256 `c32af337488a1c0ef308dc048d7a324ed97e875cf472820fc65d0128fb574049`. Không dùng số của các biên bản cũ làm kết quả hiện hành.

- Restore/Release build cuối: **0 Error / 0 Warning**. Nhóm catalog/Setup/native ZIP/Publish và media: **138 Passed / 0 Failed / 0 Skipped**.
- Browser Admin bằng Microsoft Edge, hai bộ release cũ/mới: **34 Passed / 0 Failed / 0 Skipped**. Có các ca DirectCatalog không enqueue/poll inspection, validation, CAS/retry, deactivation/republish và màn hình hẹp/bàn phím.
- SQL **4.1.19** rehearsal đạt trên SQL Server 2019 LocalDB `15.0.4382.1`, database GUID mới từ backup synthetic: chạy hai lần/idempotency, giữ 3 release/6 artifact/các CHECK cũ, desktop CRUD bị DENY, hash/publication không hợp lệ bị chặn, lỗi giữa chừng rollback và partial schema bị từ chối. [Report SQL](D:/vm-direct-catalog-sql/20261017-140806-3d678fdd/report.json). SHA migration `ccda46d8a4863663d59656bff62e42767875a45731ac249d97f70560217acfe5`; backup synthetic `cd8a51ee99e449cd74fdb8934dccee47743c7b235ee1e3e1560a541700fd5bf6`. Database thử mới được xóa; database cũ không thay đổi, không truy cập hosting.
- Lượt full .NET đầu trước phần bổ sung cuối: **2.797 Passed / 1 Failed / 18 Skipped**, lỗi FFmpeg phân tích âm thanh trả stderr rỗng khi chạy đồng thời nhiều tiến trình; bài này chạy lại trong nhóm 138 đã đạt. Không sửa guard hay bỏ bài. Full sẽ được ghi riêng cho snapshot cuối.
- `npm ci` và frontend build đạt. Lượt Vitest mặc định/2 worker có Node native out-of-memory; một regression mới tìm nút có icon sai đã được sửa bằng selector đúng và kiểm nhãn/trạng thái. Kết quả chạy lại toàn bộ với giới hạn tài nguyên được ghi riêng, không tính tiến trình crash là Passed.

Lượt full tiếp theo bị dừng chủ động: Publish `FolderProfile` chạy đồng thời và đổi `TOOL-LOCAL/bin` sang release gate, trong khi một bài native CLI còn dùng DLL test build thường. Log `test-full-final.log` ghi **2.617 Passed / 1 Failed / 12 Skipped**, run **Aborted**, không tính là full đạt. Đã hoàn tất Publish, build Release thường lại và chạy riêng; không sửa hoặc bỏ assertion của bài này.

**Kết quả cuối trên source đã chốt:**

- `restore-isolated.log` / `build-isolated.log`: restore và Release build toàn solution đạt, **0 Warning / 0 Error**, sau tất cả bước Publish. Chạy với `DOTNET_PROCESSOR_COUNT=2`, MSBuild một node/không reuse để giới hạn tài nguyên máy phát triển; không thay assertion hoặc skip test.
- `test-full-isolated.log` / `full-isolated.trx`: **2.809 Passed / 0 Failed / 18 Skipped / 2.827 Total**, **5 phút 17 giây**. TEMP/TMP đặt trong thư mục D riêng để bài render giọng hai giờ qua kiểm dung lượng thật; không nới guard.
- `frontend-ci.log`, `frontend-build.log`: `npm ci --no-audit --no-fund` và `npm run build` đạt. Vite còn cảnh báo chunk >500 kB.
- `frontend-tests-serial.log`: **345 Passed / 0 Failed / 0 Skipped**, 48 file, 46,74 giây, `npm test -- --maxWorkers=1`, heap Node giới hạn 512 MiB. Đã giải phóng riêng 11 MSBuild worker nhàn rỗi do lượt build này tạo trước khi chạy; không dừng IDE/server người dùng.
- `admin-browser.log`: **34 Passed / 0 Failed / 0 Skipped**. Nhóm focused **138/0/0** là một phần của full, không cộng thành tổng mới.
- `source-verification.json`: cả **1.383 file** trong snapshot giữ nguyên sau build/test/Publish. `git diff --check` đạt. Source chưa commit và không gán kết quả cho riêng HEAD nền.

## Gói bàn giao đã tạo

Gốc: [delivery](D:/vm-direct-catalog/20261017-140233/delivery). [Hướng dẫn triển khai từng bước](D:/vm-direct-catalog/20261017-140233/delivery/HUONG_DAN_TRIEN_KHAI.md). `delivery-manifest.json` trong thư mục bằng chứng ghi size/hash của file bàn giao, không phải trường phải nhập trong Admin.

| Gói | File | Byte | SHA-256 |
|---|---|---:|---|
| Ứng dụng 1.0.2/build 3 | `packages/1.0.2-3/VideoMaker-1.0.2-3-win-x64-5a851677f9d8cfbdda1de5e31ba2c711da399ebfb0304ead38cbb00e1d19e713.zip` | 485820823 | `5a851677f9d8cfbdda1de5e31ba2c711da399ebfb0304ead38cbb00e1d19e713` |
| Setup dùng chung 1.2.0 | `shared-setup-1.2.0/VideoMaker Setup.exe` | 267513757 | `15811a261e4420fc8143f3a3cd0902fbccbca173326bf5dd7f6b94e9cd806d27` |
| Server IIS DLL + private .NET 10.0.9 | `TOOL-SERVER-IIS-dll-runtime-win-x64-20261017-071748-77656541.zip` | 59028672 | `9d9a345410a0df6f947344fe3c84684ebdf3276b08875f4d2f1d9ade90797e9b` |
| Migration | `database/VideoFactory.4.1.19.DirectDesktopCatalog.sql` | 10622 | `ccda46d8a4863663d59656bff62e42767875a45731ac249d97f70560217acfe5` |

Publish ứng dụng đã chạy đúng `FolderProfile`, Release/win-x64/self-contained, tự chuẩn bị pin FFmpeg, build frontend/worker và tự tạo ZIP canonical thành công. Setup build script đã xác minh native resource binding/capability `3,4`, nhúng WebView2 đã pin và đối chiếu kết quả `--inspect-shared-bootstrapper`. Server package sanitizer/audit đã chạy; cấu hình không bí mật DirectCatalog/Production/API/prefix được giữ. Các gói chưa upload, đăng ký Active hoặc phát hành hosting.

ZIP thật có **331 file**, **1.106.471.500 byte** sau giải nén, layout gốc và không có ba metadata phát hành. Runner QA gọi trực tiếp `DesktopDirectPackageResolver.ResolveAsync` rồi `ExtractAndMaterializeAsync` trên chính ZIP này, với catalog fixture gắn native binding của Setup thật 1.2.0: SHA/native version/build đạt và tạo stage **334 file** gồm ba JSON local. [Kết quả resolver](D:/vm-direct-catalog/20261017-140233/package-proof.json). Đây là kiểm file/giải nén, không truy cập catalog production, không tạo bản cài hoặc receipt model.

Trên stage vừa giải nén, pin/approval kỹ thuật FFmpeg, cấu hình StaticArchive và OCR MSVC runtime đã kiểm đạt. Native `--check-static-model-policy` đạt. `Test-DesktopBundleRuntime.ps1` chạy binary **1.0.2/build 3** từ chính stage: **WebView2 READY, Media READY, OCR Anh/Trung READY, MSVC app-local true; ServerAccessChecked=false**. Log `real-stage-*` trong thư mục bằng chứng. Chưa chạy installer WebView2, Qwen/Piper inference hoặc tạo trạng thái model READY từ fixture này.

[Đối chiếu cuối](D:/vm-direct-catalog/20261017-140233/verification-final.json) xác nhận cả ba artifact còn nguyên checksum, source snapshot không đổi và cấu hình release ignored trên máy build trùng byte với `appsettings.json` trong ZIP/stage, SHA-256 `2c80e8e3192c8092be1855c975685568908792d99f44b872ce1b7396278c3af7`. Không xuất giá trị cấu hình bí mật vào báo cáo. `git diff --check` cuối đạt.

## Nghiệm thu đích còn lại

Chưa áp SQL hosting, deploy server, đăng ký/Active Setup, upload/phát hành release hoặc chạy cài mới/cập nhật/repair qua API thật trên Windows sạch trong lượt này. Model ZIP vẫn giữ pin hiện hành; không tải lại 2,65 GiB hoặc sửa model/READY của máy người dùng. Các ca fake HTTP/transaction/IPC đạt và native media/OCR đạt không thay bước nghiệm thu Qwen/Piper cùng mạng/đĩa/RAM của máy khách.

FFmpeg giữ scope `Release/TechnicalPackaging`; corresponding source, redistribution/GPL và production qualification chưa được nâng trạng thái. Test model bỏ qua không được tính là Qwen/Piper đã chạy đạt.
