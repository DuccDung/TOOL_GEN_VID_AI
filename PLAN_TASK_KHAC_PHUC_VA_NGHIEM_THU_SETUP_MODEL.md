# Plan task khắc phục và nghiệm thu Setup/model

Ngày lập: **2026-10-17**. Source rà soát: nhánh `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b` và working tree chưa commit. HEAD không đại diện đầy đủ source đang thay đổi; lượt triển khai tiếp theo phải chụp manifest/hash riêng.

Tiến độ triển khai sau lượt lập kế hoạch được ghi tại [biên bản FX](TRIEN_KHAI_KHAC_PHUC_VA_NGHIEM_THU_SETUP_MODEL.md). Những câu “chưa chạy/chưa sửa” dưới đây mô tả lượt lập kế hoạch; không thay bằng chứng của lượt triển khai.

**Lượt này chỉ đọc source và lập kế hoạch. Chưa sửa code, build, chạy test/model, xóa model, tạo candidate hoặc triển khai server.**

Mục tiêu: xử lý điểm còn chặn phát hành, bổ sung công cụ kiểm đúng runtime mà StaticArchive đã cài, rồi nghiệm thu hành trình Setup → tải model VPS → chuẩn bị runtime → probe → commit → sử dụng thực tế. Các lỗi đã sửa ở lượt trước được giữ làm baseline hồi quy.

Đọc cùng [biên bản kiểm thử gần nhất](TRIEN_KHAI_TEST_ON_DINH_SETUP_MODEL_UI_UX_VA_LOGIC.md), [kế hoạch ST01–ST32](PLAN_TASK_TEST_ON_DINH_SETUP_MODEL_UI_UX_VA_LOGIC.md), [triển khai StaticArchive](TRIEN_KHAI_SETUP_CAI_MODEL_TU_VPS.md), [kiểm thử/nghiệm thu](KIEM_THU_VA_NGHIEM_THU.md) và [vận hành/phát hành](VAN_HANH_VA_PHAT_HANH.md).

## 1. Phân loại đúng việc cần xử lý

| Mã | Hiện trạng có bằng chứng | Hướng xử lý |
|---|---|---|
| BASE01 | Clipping WinForms, progress lùi/cũ, snapshot READY sai, ACK/context/repair, receipt/metadata Unicode và temp recovery đã sửa, có regression đạt | Giữ regression, kiểm lại tích hợp; không viết lại các phần đã đạt nếu chưa có lỗi mới |
| REL01 | FFmpeg hiện `Approval scope: Development`; validator Release thực sự từ chối | Hoàn tất hồ sơ và bộ binary được phép phân phối; không sửa chữ scope để vượt gate |
| REL02 | Chưa có profile khách, version/build/channel, API release staging và candidate StaticArchive đã chốt | Chuẩn bị đầu vào rồi build/kiểm candidate đúng pipeline |
| DEP01 | Log build trước có NU1902/NU1903 cho `System.Security.Cryptography.Pkcs` 9.0.11 | Rà dependency và advisory hiện hành; chọn bản sửa tương thích, kiểm lại đường auth/SQL/chữ ký liên quan |
| TOOL01 | Harness Qwen/Piper hiện kiểm layout payload bundled/diagnostic; chưa chứng minh trực tiếp runtime production do StaticArchive vừa cài | Bổ sung chế độ nghiệm thu installed StaticArchive, giữ đường bundled hiện có |
| NET01 | HEAD/Range VPS đạt; full GET chẩn đoán chưa hoàn tất trong ngân sách 300 giây | Kiểm full byte/hash với ngân sách phù hợp và bằng chính Setup; chưa có căn cứ kết luận downloader production lỗi timeout tổng 5 phút |
| E2E01 | Chưa cài trọn bộ bằng Setup khách trên Windows sạch | Chuẩn bị VM/profile mới, cài thật và đối chiếu đủ năm component cùng giao dịch commit |
| RC01 | Đã kiểm Job Dispose/Terminate và transaction fixture; chưa kill Setup parent thật, power-off VM hoặc đủ race hai Setup/app | Kiểm fault có kiểm soát rồi sửa nhánh tái hiện lỗi |
| UX01 | Browser thật hiện render component với controller giả; DPI thực mới đo 120 | Kiểm full App + host bridge, DPI Windows thực, Narrator/High Contrast và các overlay |
| MODEL01 | Chưa inference/soak Qwen/Piper thật trong lượt ổn định vừa rồi; OCR native fixture có chạy | Chạy trên bộ đã cài, không thay bằng hash/probe giả hoặc Skipped |
| ENV01 | Full suite từng thiếu đĩa; lượt cuối đã đạt sau khi đủ dung lượng, không hạ gate | Bổ sung preflight môi trường thử; không coi đây là lỗi production đang tái hiện |

Biên bản trước ghi full .NET **2.202 Passed/0 Failed/18 Skipped**, frontend **341/0/0**, nhóm `StaticSetup` **20/20 vòng × 159 bài = 3.180 lượt đạt**. Đây là lịch sử có log/hash, **không phải kết quả chạy mới của kế hoạch này**. Vòng lặp không bao gồm browser và không chứng minh cài model trên máy khách.

18 Skipped phải có danh mục: sáu ca SQL và 12 ca model/GPU/voice. Không ép mọi ca thành Passed: GPU, Kokoro, voice conversion và SQL ngoài phạm vi cần ghi lý do; Qwen CPU/Piper thuộc bộ chính phải có bằng chứng runtime phù hợp trước nghiệm thu.

## 2. Quyết định kỹ thuật giữ nguyên

- Bộ chính: Qwen 4B CPU, Piper offline và OCR/media đồng bộ. Chưa thêm Qwen 1.7B, Kokoro hoặc GPU vào bộ chính.
- Dùng `StaticArchive` hiện có: model được pin trong source. Không mở dự án chuyển sang catalog động/chữ ký catalog chỉ để xử lý lượt này. Mode `Server` tiếp tục theo thiết kế riêng.
- Pin hiện hành tại [ApprovedStaticModelBundle](TOOL-DISTRIBUTION/StaticModels/ApprovedStaticModelBundle.cs): `models-20261016-03`, ZIP **2.847.980.994 byte**, SHA-256 `e9939863910af178ec6be6e99f7fcc16948b6a8d916bd5b32bc0a50d92d3f471`, worker **1.2.1**, IPC **3**. Đọc lại khi chốt candidate; đổi pin phải tạo candidate/version tương ứng.
- URL model: `https://103.179.190.182:8443/models/VideoMaker-Models-20261016-082700.zip`. Kho file này khác API phát hành ứng dụng/auth/license của VideoMaker; có URL ZIP chưa đủ để Setup lấy đúng desktop package.
- Setup chỉ hoàn tất khi `media`, `ocr`, `qwen`, `piper`, `webview2` đều READY với fingerprint hợp lệ; model receipt schema 2, app receipt, journal `Committed`, pending và operation phải nhất quán.
- Giữ TLS/host/port/pin, `.part`, kiểm size/hash, promote atomically và gate hai lớp React/C#. Không lấy READY cũ, cache developer hoặc venv máy khác làm bằng chứng.
- App cài ở D không có nghĩa cache/runtime/TEMP đều ở D. Tính ngân sách từng volume theo đường đã resolve và byte cần tại từng giai đoạn.
- “Offline” của Qwen/Piper là inference/runtime không tải model/package bổ sung sau chuẩn bị. Đăng nhập, license và quyền sử dụng vẫn theo chính sách server hiện hành.

## 3. Đầu vào cần chốt trước các bước phụ thuộc

| Đầu vào | Cần xác định | Nếu chưa có |
|---|---|---|
| Phát hành | Version/build/channel, API staging, desktop package/release, người phụ trách | Làm được rà source/công cụ; chưa nghiệm thu Setup tải app thật |
| Profile khách | Cấu hình tối thiểu hợp lệ, feature flag đúng bộ chính, workflow SQL chuyển tiếp được validator hiện hành yêu cầu, không credential phát triển | Profile API-only/SQL rỗng chưa thay được profile hợp lệ của composition hiện tại |
| FFmpeg | Binary, provenance, checksum, license/notice và phê duyệt phân phối phù hợp | Gate Release tiếp tục chặn |
| Chính sách ký | Có bắt buộc Authenticode hay không; certificate reference nếu có | Ghi đúng trạng thái; `None` hiện được script cho phép, không tự gọi đây là lỗi validator |
| Windows thử | VM/máy QA được phép reset, snapshot, user thường; OS/build/CPU/RAM trong phạm vi hỗ trợ | Không xóa model trên tài khoản đang làm việc để giả máy sạch |
| Staging nghiệp vụ | Tài khoản/license/context có sẵn và quyền smoke phù hợp | Chưa kiểm workflow có authorization; không thanh toán hay gọi provider thật |
| Nghiệm thu output | Fixture Anh/Trung, giọng Piper chính, người đọc/nghe xác nhận | Có thể kiểm định dạng trước; chưa kết luận chất lượng nghe/đọc |

Ma trận đầu tiên: Windows 11 x64, user thường, Intel có tài nguyên đủ gate. Thêm AMD và cấu hình RAM thấp cho phạm vi cam kết tương ứng. Windows 10/ARM64/GPU chỉ được đưa vào mức hỗ trợ khi đã chốt và thử riêng; không tự suy từ target framework.

## 4. Task triển khai theo thứ tự

Mọi task dưới đây bắt đầu ở trạng thái **Chưa thực hiện trong kế hoạch này**. P0 chặn tính toàn vẹn/cài đặt/nghiệm thu; P1 chặn chất lượng hành trình hoặc xử lý dependency. Một khoảng trống kiểm thử chưa tự được coi là bug. Nếu có lỗi mới: tái hiện → ghi expected/actual → regression phù hợp → sửa → kiểm lại.

### A. Chốt baseline, phạm vi và tài nguyên

**FX01 — Chốt source/candidate identity — P0; phụ thuộc: không.**

- Chụp branch/HEAD, `git status`, hash source/script/cấu hình an toàn, toolchain và binary; giữ nguyên thay đổi không liên quan.
- Đầu ra: manifest có thời điểm, đường dẫn evidence mới và các pin dự kiến. Đạt khi xác định được chính xác source dirty nào sinh ra từng candidate/test run.

**FX02 — Lập sổ lỗi và phân loại 18 skip — P0; phụ thuộc: FX01.**

- Tách `Fixed`, `OpenConfirmed`, `Unverified`, `Blocked`, `OutOfScope`; liên kết BASE01/REL01… và ST/UI/LG/RC/RT tương ứng.
- Đạt khi mỗi mục có căn cứ và owner, không đưa lỗi đã đạt lại thành Open hoặc dùng số Skipped làm số model đã nghiệm thu.

**FX03 — Chốt môi trường và quyền tác động — P0; phụ thuộc: FX01.**

- Ghi các đầu vào mục 3, root QA, máy/user/volume được phép sử dụng, staging URL và giới hạn mạng. Thông tin đăng nhập cấu hình ngoài source/log.
- Đạt khi phần làm độc lập và phần đang chờ đầu vào được đánh dấu riêng; chưa deploy VPS/production từ task này.

**FX04 — Preflight tài nguyên trước test nặng — P0; phụ thuộc: FX03.**

- Tính disk cho package, stage, backup, ZIP, payload, runtime, TEMP và evidence trên từng volume; thêm nhu cầu fixture timeline/soak và khoảng dự phòng. Không dùng 2,85 GB làm tổng dung lượng cần.
- Rà [Test-Stability](scripts/Test-Stability.ps1) và [ModelSetupPreflight](scripts/ModelSetupPreflight.psm1): guard 3 GiB của harness không thay yêu cầu thật của full/timeline. Nếu bổ sung preflight, báo Blocked trước chạy khi không đủ, không đổi gate/assertion hoặc chuyển lỗi đã chạy thành Skipped.
- Đạt khi ghi RAM/free commit/pagefile/disk ngay trước lượt; không chạy native model cùng build/OCR/FFmpeg nặng, không đóng IDE/app người dùng để lấy tài nguyên.

### B. Gỡ chặn phát hành và dependency

**FX05 — Hoàn tất bộ FFmpeg được phép phát hành — P0; phụ thuộc: FX01, FX03.**

- Rà [PROVENANCE](third_party/ffmpeg/win-x64/PROVENANCE.md), [validator](scripts/Test-FfmpegBundle.ps1), [chuẩn bị bundle](scripts/Prepare-FfmpegBundle.ps1) và license binary thực. Chốt bộ được phép phân phối cùng hồ sơ tương ứng.
- Đạt khi `Test-FfmpegBundle -RequireReleaseApproval` qua bằng hồ sơ thật, đủ file/hash/version và smoke media; publish giữ gate. Không chỉ thay `Development` thành `Release`, đổi Channel hoặc dùng gói diagnostic để vượt kiểm tra.

**FX06 — Xử lý cảnh báo Pkcs theo dependency graph — P1; phụ thuộc: FX01.**

- Kiểm graph restore hiện hành của [server project](TOOL-SERVER/TOOL-SERVER.csproj); lượt trước thấy EF SQL → Microsoft.Data.SqlClient → Pkcs 9.0.11. Xác minh advisory/phiên bản vá từ nguồn chính thức ở lúc thực hiện, không đoán version trong plan.
- Chọn thay đổi nhỏ tương thích, ưu tiên package sở hữu dependency; không thêm override Pkcs mù hoặc nâng toàn bộ stack. Kiểm restore/audit, auth/credential protection/chữ ký có liên quan và SQL staging nếu graph thay đổi cần integration.
- Đạt khi graph mới chứng minh đã xử lý hai advisory, build không còn cảnh báo tương ứng và regression liên quan đạt. Không `NoWarn`/tắt audit; chưa phát hành server hoặc chạy migration chỉ vì đổi package.

**FX07 — Chuẩn bị profile khách và chính sách ký — P0; phụ thuộc: FX03.**

- Dùng cấu hình khách tối thiểu qua validator: đúng bốn component local, URL API/channel/platform, flags và worker. Pipeline hiện luôn dùng `-RequireTransitionalSql`: cần workflow SQL staging có quyền tối thiểu schema `vf`, không dùng SQL rỗng/chuỗi giả/API-only để vượt validator. Không mở quyền sang `ai`/`auth`/`dbo`/`vs`.
- Profile/credential QA cấu hình ngoài Git/log; không copy credential phát triển/production, SQL admin, provider key, workspace/path hoặc cấu hình máy dev. Việc bỏ SQL chuyển tiếp là thay đổi kiến trúc riêng, ngoài kế hoạch này.
- Chốt Authenticode theo yêu cầu phát hành; nếu bắt buộc thì kiểm chuỗi chứng chỉ/timestamp và từ chối unsigned/tamper. Nếu chưa bắt buộc, báo rõ candidate chưa ký; không lẫn với chữ ký catalog model.
- Đạt khi profile đã làm sạch qua validation và policy ký được ghi rõ, không có cấu hình nhạy cảm trong artifact/log.

**FX08 — Build candidate StaticArchive và kiểm artifact — P0; phụ thuộc: FX05, FX07 và mốc A của FX10 nếu thay binary ứng dụng.**

- Dùng [Publish-DesktopRelease](scripts/Publish-DesktopRelease.ps1) và [Test-DesktopSetupPublish](scripts/Test-DesktopSetupPublish.ps1), output mới. `StaticModelSetupRequired=true`; metadata Setup bind đúng `SetupDesktopVersion`, `SetupDesktopBuildNumber`, `SetupDesktopSha256`, channel và model pin của desktop package. Không dùng FileVersion của Setup EXE thay bằng chứng binding này.
- Kiểm package app trong giới hạn updater hiện hành, OCR/MSVC app-local, worker/native, WebView2 installer approved size/hash/chữ ký và managed manifest. Giữ nguồn/license/notice và đối chiếu phạm vi phân phối model/runtime; hash đúng không tự chứng nhận quyền phân phối. ZIP model phục vụ riêng; không nhét lại vào desktop package.
- Đạt khi giải nén kiểm hash/binding/policy/notice, không cache/venv/READY, không phụ thuộc máy dev. `New-DesktopOfflineCandidate` dạng diagnostic bundled không thay candidate này.

FX06 là nhánh dependency server có thể làm song song; không mặc định bắt desktop candidate chờ thay đổi server không liên quan. Chốt graph và hồi quy toàn solution tại FX34; thay đổi artifact nào phải kiểm lại artifact đó.

**FX09 — Chuẩn bị release staging mà Setup thực sự tải — P0; phụ thuộc: FX08 và môi trường staging đã chốt.**

- Đưa đúng package/version/build/channel/platform vào staging theo cơ chế phát hành hiện có; kiểm API phiên bản/package và hash khớp candidate. Nếu cần ghi cấu hình/Active release, làm đúng môi trường đã được chỉ định.
- Đạt khi Setup lấy đúng app package từ staging; URL ZIP model vẫn là nguồn model đã pin. Chưa đánh dấu production/khách hàng phát hành.

### C. Công cụ nghiệm thu và hành trình cài thực

**FX10 — Bổ sung harness kiểm runtime installed StaticArchive — P0; phụ thuộc: FX01; nghiệm thu trên bản cài sau FX15.**

- Rà [Qwen harness](scripts/Test-QwenStability.ps1), [QwenOfflineCommand](TOOL-LOCAL/SystemSetup/QwenOfflineCommand.cs), [Piper harness](scripts/Test-PiperOfflineBundle.ps1), [PiperOfflineCommand](TOOL-LOCAL/SystemSetup/PiperOfflineCommand.cs). Các đường hiện tại giả định payload bundled/diagnostic.
- Thiết kế chế độ chọn runtime installed qua pin/receipt/root production đã kiểm, không nhận arbitrary model/executable từ UI và không copy model dev vào publish để làm đạt. Giữ chế độ bundled và workspace diagnostic cũ.
- Kiểm trust/binding, giữ usage lease khi dùng runtime production; chế độ nghiệm thu không âm thầm repair/copy/promote hoặc thay receipt của bản cài. Output fixture nằm tại root QA riêng.
- Đạt khi chạy được Qwen/Piper của chính bản Setup vừa cài, phát hiện sai/mất receipt/root/model/worker; report phân biệt kiểm installed, diagnostic và quyền nghiệp vụ. Không tự ghi/refresh READY production từ một workspace diagnostic.
- Tách hai mốc: **A** thiết kế/implementation và regression fixture đóng băng trước FX08 nếu sửa desktop CLI; **B** nghiệm thu installed runtime sau FX15. Không tạo vòng phụ thuộc yêu cầu runtime đã cài trước khi build được harness. Nếu chỉ thêm công cụ bên ngoài thì vẫn ghim hash công cụ với candidate.

**FX11 — Chuẩn hóa evidence và fault injection — P0; phụ thuộc: FX01–FX04.**

- Lưu phase/timestamp, request/operation ID, byte/ETag, PID/start time, exit code, file-state/hash và chuỗi tài nguyên; không log token, transcript/prompt thật hoặc path nhạy cảm vào UI.
- Fault chỉ tác động process/root/VM của ca QA. Nếu cần build có hook fault, tách khỏi artifact phát hành và ghi hash khác; không coi kết quả hook build là kiểm toàn bộ release candidate. Ưu tiên fault ngoài tiến trình trên cùng candidate khi làm được.
- Đạt khi tái hiện được đúng checkpoint, thu log cả failure và biết chính xác đối tượng được cleanup; cây process độc lập được giữ nguyên.

**FX12 — Tạo Windows/profile sạch và phương án reset — P0; phụ thuộc: FX03–FX04.**

- Snapshot Windows/user QA mới, không Python/uv/model/Hugging Face cache hoặc marker developer; ghi OS/build/CPU instruction, RAM/commit/disk, user/elevation và DPI.
- Ưu tiên revert snapshot để thử lại. Nếu cần quarantine model trên máy QA: inventory/hash trước, xác minh absolute path/user/containment/reparse và khả năng khôi phục; không đụng config, workspace, license, trust hoặc provenance.
- Đạt khi chứng minh trạng thái trước cài thực sự thiếu runtime. Không xóa pending/receipt/journal thủ công để mở gate hoặc xóa model của tài khoản đang dùng.

**FX13 — Chuẩn bị fixtures và dữ liệu bảo toàn — P0; phụ thuộc: FX03, FX11.**

- Media Anh/Trung ngắn, ảnh OCR chuẩn, câu Piper có dấu/số/câu dài; expected nghiệp vụ và mẫu nghe/đọc. Tạo project có cue manual/locked/revision và file unmanaged.
- Đạt khi có quyền dùng fixtures, hash đầu vào và danh mục file/cue phải giữ nguyên qua cancel/crash/repair; evidence không chứa dữ liệu khách thật.

**FX14 — Hoàn tất kiểm full HTTP/ZIP trên VPS — P0; phụ thuộc: FX04, FX11.**

- Full streaming GET phải nhận đúng **2.847.980.994 byte** và SHA-256 pin. Giữ kiểm TLS, MIME, ETag và không redirect/fallback ngoài policy. Ngân sách chẩn đoán tính theo bandwidth + disk/hash; không giữ 300 giây cho mọi đường truyền hoặc đổi timeout production khi chưa tái hiện lỗi.
- Có thể lấy bằng chứng full GET từ lượt cold Setup FX15 để tránh tải trùng. Nếu kiểm riêng trước, dùng profile/root khác để không làm ấm cache của bài cài sạch.
- Đạt khi có hash toàn bộ file từ đúng URL, timestamp/byte/timing; HEAD/206/hash local không thay phép kiểm này. Lượt kế hoạch hiện tại chưa tải lại VPS.

**FX15 — Cài cold bằng Setup UI thật — P0; phụ thuộc: FX08–FX09, FX11–FX13; thu FX14 cùng lượt nếu phù hợp.**

- Từ snapshot sạch: lấy app → preflight → tải model → verify/import → runtime → probe → commit. Thử riêng bật/tắt “mở app sau cài”; bỏ chọn launch vẫn phải cài/probe đầy đủ.
- Đạt khi đúng năm component READY có fingerprint, model receipt/app receipt/journal cùng operation và đúng machine/user/app root/version/build/package; pending đã được xóa sau commit thành công, trace ghi operation của marker trước khi xóa. App chỉ mở sau commit; không Python/package/model fallback từ nguồn ngoài bộ đã duyệt.
- Bằng chứng: video UI, network byte/hash, kết quả component và state sau restart. Chưa pass nếu chỉ chạy bootstrap CLI hoặc component fixture.

**FX16 — Root, volume, Unicode, quyền và dung lượng — P0; phụ thuộc: FX12, FX15.**

- Thử app ở D nhưng cache/runtime/TEMP ở C; thiếu đĩa từng volume trước cài và giữa ghi; ACL read-only, file lock có kiểm soát, username/path có dấu cách/tiếng Việt, đường sát giới hạn hỗ trợ.
- Giữ bound CLI/native/cache hiện hành; metadata dẫn xuất dài phải qua reader riêng/containment. Thử user thường, nâng quyền cùng user và khác user để tránh cài sai LocalAppData.
- Đạt khi dự toán theo volume đúng, dừng trước mutation không an toàn, không READY giả, retry phục hồi được và file unmanaged/hash dữ liệu bảo toàn. Full disk bằng volume/quota QA, không lấp ổ hệ thống đang dùng.

**FX17 — Prerequisite và platform thật — P0; phụ thuộc: FX08, FX12.**

- Windows sạch thiếu WebView2; installer prerequisite đúng/sai hash/chữ ký; OCR/MSVC/media mất/hỏng; CPU/backend hoặc platform không nằm trong phạm vi hỗ trợ. Giả lỗi chỉ trên root/snapshot QA.
- Đạt khi cài nguồn đã duyệt hoặc từ chối có mã lỗi/hướng dẫn đúng trước native không an toàn; DLL bắt buộc app-local không được mượn từ System32/máy dev, không mượn venv dev và không báo READY khi probe thiếu. Windows Media DLL mà source chủ động nạp từ SystemDirectory là nhánh hợp lệ, cần kiểm riêng.

**FX18 — Resume, retry, cancel với file lớn — P0; phụ thuộc: FX11, FX14–FX15.**

- Mạng chậm/mất tại khoảng 10/50/95%, sát trước/sau 2 GiB; đóng/mở lại Setup; treo không byte mới, hủy trong backoff/hash. Kiểm Range/If-Range, 200 thay 206, 416, ETag đổi/yếu/mất, 429/503 và Retry-After bằng server QA/handler khi cần.
- Đạt khi part hợp lệ resume đúng offset/ETag; dữ liệu đổi bị reset/từ chối đúng policy; full hash cuối đúng; retry hữu hạn, cancel phản hồi và không commit sớm. Ở 5–10 Mbps, riêng ZIP này mất khoảng 38–76 phút lý thuyết; đo cả ngân sách tổng bootstrap/runtime, không suy stall chỉ từ thời gian dài.
- Không sửa ZIP/Nginx/chứng chỉ VPS đang phục vụ để tạo lỗi; chưa load-test nhiều khách trên VPS dùng chung.

### D. Crash, cạnh tranh và cài lại

**FX19 — Crash/recovery tại các mốc commit — P0; phụ thuộc: FX11–FX13, FX15.**

- Tám checkpoint, tối thiểu ba lượt/checkpoint: pending; đang copy app; copy/promote Qwen; tạo venv Piper; model receipt; app receipt; journal `Committed`; sau commit trước UI complete/launch. Ghi chính xác thứ tự thực trong source khi đặt checkpoint.
- Thử kill tiến trình và power-off trên VM ở các cửa sổ chọn trước. Đạt khi khởi động lại phục hồi/tiếp tục/từ chối đúng journal, không READY giả, không mất dữ liệu ngoài manifest; các thời điểm trước/sau pending finalization được phân biệt.

**FX20 — Setup parent chết và process treo thật — P0; phụ thuộc: FX11, FX15.**

- Từ process nhỏ trước, rồi candidate: kill parent ở handshake, probe Qwen/Piper, đọc stdout hoặc chờ con treo; giữ một cây fixture độc lập làm đối chứng.
- Đạt khi toàn bộ descendants thuộc operation thoát trong timeout đã chốt, lease nhả, pending giữ gate khi chưa commit và cache đúng còn; không tắt process người dùng. PID phải ghép start time/ownership, không kill theo tên chung.
- Hai test Dispose/Terminate hiện có vẫn là baseline, không thay bằng chứng parent chết thật.

**FX21 — Hai Setup, app đang dùng và nhiều user — P0; phụ thuộc: FX11–FX12, FX15.**

- Hai Setup cùng/khác target trên một user; app mở trước và giữa install; hai Windows user; bản legacy ở root khác. Kiểm lease suốt mọi bước mutation và startup gate khi state đổi.
- Đạt khi chỉ owner hợp lệ mutate tài nguyên chung, user khác không mượn receipt/READY, app đang dùng không bị thay file hoặc bị tự kill. Race legacy chỉ được sửa sau tái hiện và chốt phạm vi tương thích.

**FX22 — Repair, reinstall, receipt cũ và rollback — P0; phụ thuộc: FX19–FX21.**

- Thử payload đúng/hỏng, receipt schema cũ, thiếu journal/pending/app receipt, thay worker/native; bản app cũ tương thích và không tương thích; cancel sau backup.
- Đạt khi receipt cũ không tự thành READY, sửa đúng component cần thiết, giữ managed/unmanaged/config/media, rollback nhất quán hoặc chặn có hướng dẫn. Piper venv chỉ phục hồi ở đúng đường gốc; không di chuyển venv thành bản cài mới.
- Giữ journal `Committed` của bản hiện hành vì reader startup còn đối chiếu; không cleanup metadata để lấy dung lượng QA.

**FX23 — Warm cache, reboot và ngắt mạng sau cài — P1; phụ thuộc: FX15, FX22.**

- Mở lại app/Setup, reboot Windows, repair cùng pin, ngắt mạng sau chuẩn bị; sửa một byte model/runtime rồi verify lại.
- Đạt khi artifact/payload đúng không bị tải lại, file đổi/mất mất readiness và có đường repair. Chặn HTTP model/package trong bài offline runtime; vẫn ghi riêng giới hạn login/license server, không tuyên bố toàn bộ sản phẩm offline.

### E. UI/UX qua toàn App và host thật

**FX24 — Kiểm full App trong WebView2 và gate C# — P0; phụ thuộc: FX11, FX13; candidate thật sau FX15.**

- Bổ sung harness render `App` + `useSystemSetup` với bridge có kiểm soát thay vì chỉ modal/controller no-op. Kiểm GET chậm/rỗng/hỏng/thiếu/trùng và đủ READY.
- Gửi thẳng command nghiệp vụ từ WebView khi chưa ready, không chỉ bấm UI. Rà [App](TOOL-LOCAL/Web/src/App.tsx), [StartupSystemSetupGate](TOOL-LOCAL/SystemSetup/StartupSystemSetupGate.cs), [Form1](TOOL-LOCAL/Form1.cs), [bridge](TOOL-LOCAL/SystemSetup/SystemSetupBridge.cs).
- Đạt khi nền bị khóa từ đầu và host từ chối trước job/outbound; bỏ DOM/modal không vượt gate. GET/Setup/thoát hợp lệ vẫn dùng được. Fixture bridge và bài candidate thật báo riêng.

**FX25 — Context, mất ACK, overlay và repair — P0; phụ thuộc: FX24.**

- Mất ACK START, GET/event đến trễ/trùng/sai thứ tự, reload WebView, đổi user/org; Setup cạnh license/update/payment/service error; repair progress đến muộn.
- Đạt khi reconcile cùng operation/payload, không chạy đôi hoặc nhận READY/error của context cũ; một overlay điều khiển tương tác/focus, nền giữ gate; không tự mở lại repair giả.
- Trace chỉ lưu request/operation/context identifier an toàn; sửa hook/App/native contract cùng nhau nếu lỗi nằm ở bridge.

**FX26 — DPI Windows thực và work area — P1; phụ thuộc: FX12, FX24.**

- Scaling Windows thực 100/125/150/200%, work area 1366×768 và 1920×1080 khi thiết bị hỗ trợ; chuyển màn hình khác DPI lúc idle/busy/error. Đo DeviceDpi, bounds, working area và dialog owner.
- Rà `InstallerForm`/`Program` nếu tái hiện; đặc biệt vòng đời fit work area khi đổi màn hình. Đạt khi Cài/Hủy và consent luôn tới được, lỗi dài cuộn/đọc được, focus không bị che.
- Font scale/geometry/WebView zoom chỉ là bằng chứng bổ sung; ảnh lượt trước DeviceDpi 120 không chứng nhận bốn mức DPI vật lý.

**FX27 — Keyboard, Narrator và High Contrast — P1; phụ thuộc: FX24.**

- Tab/Shift+Tab/Enter/Space/Escape, focus trước/sau modal, status/error cập nhật, read-only status cuộn, consent RAM và reduced motion; kiểm WinForms UIA và React/WebView2.
- Đạt khi có accessible name/role/state, đọc được lỗi/hành động tiếp theo, focus thấy được, không lọt nền, không chỉ dùng màu; xác nhận RAM mặc định chưa đồng ý và reset đúng context/profile.
- Giữ semantics hiện có; chỉ sửa notification/contrast/focus khi có căn cứ. Không suy role/aria có trong source là đã nghiệm thu Narrator.

**FX28 — Phản hồi, tiến độ và thông báo tự khắc phục — P1; phụ thuộc: FX15, FX18, FX24–FX27.**

- Đo UI trong lúc tải/hash/native và hủy/đóng/nhấp lặp. Phần trăm không lùi, tên bước đúng, không complete trước commit, trạng thái reconcile/timeout có lối xử lý.
- Mục tiêu nghiệm thu đề xuất: phản hồi thao tác trong 500 ms ở máy baseline; ACK theo timeout 15 giây hiện có, có sai số đo đã chốt; thời hạn dừng native đo riêng, không đòi model thoát tức thì.
- Đạt khi người mới biết thiếu dung lượng ở ổ nào, lỗi mạng/hash/runtime hay cần RAM consent; mã hỗ trợ an toàn, không lộ token/path nhạy cảm. Không thêm ETA/tốc độ giả hoặc đổi timeout chỉ để đạt mục tiêu UI.

### F. Runtime và workflow bằng bộ đã cài

**FX29 — Qwen CPU thật: 3 cold start × 30 lượt — P0; phụ thuộc: FX04, FX10, FX15, FX23.**

- Dùng đúng installed model/worker/profile/native đã pin, Anh/Trung xen kẽ; 90 iteration hợp lệ, log fingerprints và metric, không prompt thật. Không tự bật `AcceptResourceWarning`; chỉ dùng khi người vận hành chấp nhận cảnh báo cụ thể.
- Đạt khi assertion Anh/Trung hiện hành qua, không crash/hang/orphan và private-memory growth sau warm-up **<256 MiB** theo phép đo cửa sổ hiện có. Không thay bằng peak RAM, giảm iteration hoặc tải model khác.
- Báo theo từng máy/profile; kiểm thêm đọc chất lượng mẫu. Kết quả CPU không chứng nhận CUDA/GPU hoặc mọi CPU khác.

**FX30 — Piper offline thật: 30 lượt — P0; phụ thuộc: FX04, FX10, FX13, FX15, FX23.**

- Chạy 30 câu fixture ngắn/dài, dấu tiếng Việt/số/dấu câu bằng runtime đã cài ở đường cuối; không cài Python/package online hoặc mượn venv máy dev.
- Đạt khi 30 output qua inspector RIFF/PCM/stream/duration/hash, protocol đúng, không crash/orphan; nghe các mẫu đại diện có tiêu chí phát âm/rè/cắt chữ. Lưu report/metadata và audio fixture được phép dùng.
- Thiếu người nghe: ghi phần định dạng đã đạt, chất lượng nghe chưa nghiệm thu; không nâng thành Passed toàn bộ.

**FX31 — OCR/media và workflow sau login — P0; phụ thuộc: FX13, FX15, FX29–FX30 và staging đã chốt.**

- Trên chính app vừa cài: login/license/context → import video Anh và Trung → OCR → Qwen → Piper → SRT/MP4. Kiểm native app-local, probe media và output thực; OCR fixture đã đạt trên dev không thay bài này.
- Đạt khi chỉ dịch active `PADDLE_OCR_LOCAL` track en/zh đúng revision; cue manual/locked bảo toàn; SRT atomic, WAV/MP4 đúng stream/audio/thời lượng; quyền/license từ chối đúng trước nghiệp vụ.
- Không gửi media/prompt sang Cloud hoặc dùng provider có phí; ghi riêng lỗi auth/staging và lỗi model. Project/workspace/cấu hình không mất qua restart/repair.

**FX32 — Soak ít nhất 4 giờ và hồi quy mode khác — P1; phụ thuộc: FX19–FX31.**

- Xen kẽ thao tác workflow fixture, idle, cancel/retry, đóng/mở project và lifecycle worker; lấy memory/handle/process/disk theo chuỗi thời gian và phase. Không chạy cạnh full build/native suite.
- Đạt khi không hang/crash/orphan, job/state không kẹt, không tự tải lại model đúng pin; tài nguyên sau warm-up không tăng liên tục không kiểm soát. Ngưỡng phụ ngoài Qwen phải chốt bằng baseline trước, không đặt sau khi xem kết quả.
- Hồi quy `Bundled`/`Server` và legacy đúng phạm vi: không ép StaticArchive sang mode khác hoặc silently fallback; nếu mode chưa có môi trường hợp lệ thì báo Blocked, không ghi Passed từ unit fixture.

### G. Sửa lỗi mới, kiểm cuối và bàn giao

**FX33 — Sửa các bug tái hiện, theo module sở hữu — P0; phụ thuộc: có bug từ FX04–FX32.**

- Mỗi bug có build/hash, bước tái hiện, expected/actual, mức độ, state trước/sau và evidence. Chỉ sửa module gây lỗi; regression bảo vệ hành vi thay vì sao chép implementation.
- Đạt khi có fail-before/pass-after hoặc bằng chứng runtime tương đương, retest cùng ca/môi trường và kiểm hồi quy liên quan. Không đổi assertion, pin, timeout, retry count, disk/resource gate để che lỗi.
- Sửa receipt/contract/public bridge phải kiểm cả các bên đọc/ghi và tương thích; không xóa license/provenance/checksum. Bug P0/P1 chưa xử lý chặn nghiệm thu phần tương ứng.

**FX34 — Kiểm tra cuối trên source/candidate đã chốt — P0; phụ thuộc: FX06, các ca bắt buộc và FX33 nếu có bug.**

- Restore/Release build/full .NET và frontend ci/build/test theo AGENTS; lặp `StaticSetup` 20 vòng sau thay đổi liên quan, browser chạy riêng; native model chạy riêng theo FX29–FX32.
- Nếu đổi source sau build candidate thì build lại, hash lại và chạy lại phạm vi bị ảnh hưởng; không trộn các candidate thành một biên bản. Không hard-code mục tiêu số lượng test cũ.
- Đạt khi không fail các ca bắt buộc, skip/blocked có lý do và phạm vi rõ, không orphan thuộc run, source/binary/test identity ổn định. Giữ log các lượt fail; không cộng trùng test lặp thành test duy nhất.

**FX35 — Chốt hồ sơ và runbook hỗ trợ — P0; phụ thuộc: FX34.**

- Bàn giao candidate/checksum/config mẫu an toàn, ma trận test, bug register, coverage ST/UI/LG/RC/RT, report model/UI, nguồn/license và chỉ dẫn tải/repair/recovery.
- Đạt khi phân biệt rõ: có source; tự động đạt; smoke runtime trên máy xác định; Setup sạch đạt; pilot; production. Chỉ nâng những mức có bằng chứng.
- Runbook nêu đúng cache/runtime/transaction, giữ journal Committed, diagnostic an toàn và thời điểm cần hỗ trợ. Không hướng dẫn xóa READY/pending/trust để vượt gate.

**FX36 — Pilot trước phát hành rộng — P1; phụ thuộc: FX35 và môi trường/người nhận pilot được chỉ định.**

- Chốt nhóm máy nhỏ trong phạm vi hỗ trợ, candidate bất biến và kênh rollback; thu kết quả cài, resume, startup và workflow. Chỉ đo tải đồng thời VPS khi có hạn mức/môi trường được cho phép.
- Đạt khi không P0/P1 còn mở, từng máy có kết quả và phương án khôi phục. Publish/Active production là bước riêng sau nghiệm thu và chỉ định môi trường; kế hoạch này chưa triển khai bước đó.

## 5. Ma trận tối thiểu bắt buộc trước nghiệm thu khách

| Ca | Môi trường/thao tác | Bằng chứng đạt | Task |
|---|---|---|---|
| AC01 | Windows sạch, cache rỗng, Setup cold, bật/tắt launch | Full ZIP/hash + năm component + commit, app mở đúng lúc | FX14–FX15 |
| AC02 | App D, runtime/cache C; thiếu từng volume | Tính đúng ổ, dừng/rollback/retry, không mất dữ liệu | FX04, FX16 |
| AC03 | User/path Unicode, elevation cùng/khác user, bound path | Runtime đúng user/root; metadata dài hợp lệ; từ chối root không hỗ trợ | FX16 |
| AC04 | Thiếu/hỏng prerequisite và platform không hỗ trợ | Tải nguồn duyệt hoặc báo lỗi đúng; không READY giả | FX17 |
| AC05 | Mất mạng/restart quanh mốc 2 GiB và gần cuối | Offset/ETag/full hash đúng, reuse part và retry/cancel đúng | FX18 |
| AC06 | Cache đúng/hỏng, reboot, repair, offline runtime | Không tải lại file đúng, file hỏng mất readiness | FX22–FX23 |
| AC07 | Tám checkpoint crash × ba lượt, power-off chọn trước | State/receipt/journal nhất quán, không xóa unmanaged | FX19 |
| AC08 | Parent chết/child treo và cây độc lập đối chứng | Không orphan thuộc operation, cây độc lập còn sống | FX20 |
| AC09 | Hai Setup/app/legacy/multiuser | Lease đúng, không mutate trùng hoặc lấy READY chéo | FX21 |
| AC10 | Full App + WebView + host, direct command khi chưa READY | React và C# cùng chặn trước nghiệp vụ/outbound | FX24 |
| AC11 | ACK mất/context đổi/overlay/repair progress cũ | Cùng operation, focus đúng, không READY/repair giả | FX25 |
| AC12 | DPI vật lý, keyboard, Narrator, High Contrast | Ảnh/bounds/UIA/biên bản, thao tác và đọc được | FX26–FX28 |
| AC13 | Qwen CPU installed | 3×30 lượt, đúng output assertion và ngưỡng memory hiện hành | FX29 |
| AC14 | Piper installed | 30 WAV hợp lệ và mẫu nghe đã nghiệm thu | FX30 |
| AC15 | Video Anh và Trung sau login/license staging | OCR→dịch→giọng→SRT/MP4, bảo toàn cue/revision/quyền | FX31 |
| AC16 | Soak 4 giờ + reinstall/reboot + mode regression | Không crash/hang/orphan/data loss; tài nguyên có chuỗi đo | FX22–FX23, FX32 |

Mỗi hàng là ca tích hợp có nhiều tổ hợp, không phải “16 test tự động”. Các nhánh TLS/HTTP lỗi và fault nguy hiểm dùng môi trường cô lập; không tạo tải phá hoại ở VPS đang phục vụ.

## 6. Thứ tự thực hiện và điểm dừng

1. **Đợt 1:** FX01–FX04; song song rà FFmpeg/dep/profile FX05–FX07 và chuẩn bị FX10–FX13. Mốc A của FX10 phải đóng băng trước candidate nếu thay binary desktop. Có thể tiến hành phần độc lập khi chưa có profile/VM, không dừng toàn bộ công việc.
2. **Đợt 2:** qua gate phát hành → FX08–FX09 → full tải/cài FX14–FX18. Chưa có candidate/profile hợp lệ thì chưa chạy cài khách để lấy kết quả nghiệm thu.
3. **Đợt 3:** FX19–FX28; full App fixture có thể chuẩn bị trước candidate. Ưu tiên mọi lỗi READY sai/mất dữ liệu/process trước phần trình bày.
4. **Đợt 4:** FX29–FX32 trên runtime đã cài. Tài nguyên không đủ hoặc chưa có chấp nhận cảnh báo thì ghi Blocked, giữ report và tiếp tục các phần độc lập.
5. **Đợt 5:** FX33 thực hiện khi phát hiện bug, lặp đến khi retest đạt; FX34–FX35 chốt hồ sơ. FX36 chỉ bắt đầu với đối tượng pilot được chỉ định.

Ước lượng phục vụ sắp lịch, không phải cam kết: rà/chốt đầu vào 0,5–1 ngày; dependency/công cụ/fixture 1–3 ngày tùy bug; baseline tải/cài và fault/UI 2–4 ngày; native/soak tối thiểu 4 giờ cộng 90 lượt Qwen và 30 lượt Piper; retest/bàn giao 1–2 ngày. Thời gian chờ hồ sơ FFmpeg, signing, staging, VM và các vòng sửa lỗi tách riêng. Không giảm ma trận chỉ để khớp ước lượng.

## 7. Bộ kiểm tra khi bắt đầu sửa source

Các lệnh sau là checklist cho lượt triển khai sau, **chưa chạy ở lượt lập kế hoạch**:

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build

Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
npm test
```

Sau khi build/frontend đạt, chạy `scripts/Test-Stability.ps1 -Profile StaticSetup -Iterations 20` từ root với output QA mới đủ dung lượng. Harness này không tự bật model/SQL opt-in và loại browser, nên phải chạy native/browser theo task riêng. Chỉ dùng lệnh Qwen/Piper installed mới sau FX10; không lấy lệnh bundled hiện có chạy vào StaticArchive rồi kết luận lỗi model hoặc copy payload dev để làm đạt.

Không chạy rộng/lặp lại test khi chưa có thay đổi, failure hoặc nghi ngại cụ thể cần xác minh. Khi chỉ sửa tài liệu, kiểm source/link/diff và ghi không chạy build/test mới.

## 8. Điều kiện kết thúc kế hoạch

- REL01/REL02 đã gỡ bằng candidate/phê duyệt thực; DEP01 có remediation được kiểm, không tắt cảnh báo.
- Các ca bắt buộc AC01–AC16 trong phạm vi hỗ trợ đã chốt có bằng chứng trên cùng candidate; không P0/P1 còn mở ở hành trình bộ chính.
- Qwen/Piper đã chạy thật bằng runtime Setup cài; đủ kiểm định output, nghe/đọc và soak. Không dùng unit fake hoặc Skipped để thay.
- Dữ liệu người dùng được bảo toàn qua cancel/crash/repair/reinstall; gate không mở khi receipt/runtime/transaction thiếu hoặc sai; không orphan thuộc lượt thử.
- Báo cáo nêu rõ máy/OS/profile đã đạt, các mode/ma trận chưa thử và trạng thái phát hành. Chưa có đầu vào môi trường thì trạng thái là Blocked ở đúng task, không tự đánh dấu toàn bộ kế hoạch hoàn tất.

Việc làm đầu tiên khi được giao triển khai: **FX01–FX04, rà graph Pkcs và chuẩn bị thiết kế harness installed; đồng thời hoàn tất đầu vào FFmpeg/profile/staging/VM trước build Setup khách.** Không bắt đầu bằng xóa toàn bộ model trên máy đang dùng.
