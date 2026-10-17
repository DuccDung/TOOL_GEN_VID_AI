# Kế hoạch Setup cài ứng dụng và model từ VPS trong cùng một lần chạy

Theo dõi lượt triển khai được người dùng yêu cầu sau kế hoạch tại [biên bản Setup StaticArchive](TRIEN_KHAI_SETUP_CAI_MODEL_TU_VPS.md). Trạng thái "chỉ lập kế hoạch" và task chưa thực hiện bên dưới là baseline của lượt lập plan; không thay kết quả source/test và không có nghĩa toàn bộ SC01–SC36 đã được nghiệm thu. Các bước Windows sạch/pilot/production vẫn cần bằng chứng riêng.

Ngày lập: **2026-10-16**. Nhánh rà soát: `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, cùng working tree chưa commit. Commit nền không đại diện đầy đủ cho các thay đổi hiện có.

**Trạng thái: CHỈ LẬP KẾ HOẠCH.** Các task SC01–SC36 bên dưới chưa được triển khai trong lượt này. Chỉ thêm tài liệu này; không sửa code/cấu hình, không build/test, không chạy Setup/inference, không xóa model trên máy và không thay đổi VPS.

Mục tiêu: người dùng chạy Setup, cài ứng dụng và bộ Qwen 4B/Piper từ VPS, kiểm OCR/FFmpeg cùng prerequisite trên máy đích. Setup chỉ báo hoàn tất và cho mở ứng dụng sau khi bộ bắt buộc đã đạt. Khi đăng nhập, desktop nhận đúng runtime đó và không yêu cầu tải/cài model lại.

## 1. Quyết định triển khai

1. Giữ máy chủ phân phối ứng dụng hiện hành. Kho model dùng URL VPS đã được kiểm tra ở mục 2, có cấu hình riêng với API đăng nhập/license/update.
2. Thêm chế độ phân phối model **StaticArchive** cho ZIP hiện có; giữ các chế độ Bundled và Server/catalog ký đang có. Không biến ZIP tĩnh thành API catalog giả.
3. URL, số byte, SHA-256 và tương thích của gói model được gắn vào metadata bản phát hành đã kiểm, dùng chung giữa Setup và desktop. Không lấy checksum tải cạnh ZIP làm gốc tin cậy duy nhất.
4. Setup tải model bằng HTTPS xác thực bình thường, hỗ trợ `.part`/Range, kiểm hash toàn file rồi mới dùng. Cổng `8443` chỉ được cho phép cho nguồn model đã chốt; không mở rộng quy tắc outbound của provider AI.
5. Dùng chung bộ chuẩn bị/probe component của desktop. Bổ sung command bootstrap production trước login để Setup gọi và theo dõi; không sao chép toàn bộ logic Qwen/Piper vào WinForms Setup.
6. Bộ chính bắt buộc là **Qwen3 4B CPU, Piper offline, OCR Anh/Trung và FFmpeg/FFprobe**. WebView2 là prerequisite bắt buộc. Kokoro, Qwen 1.7B, CUDA và lựa chọn model khác nằm ngoài bản đầu.
7. Model chỉ tải từ VPS trong chế độ này. Có thể dùng lại component/cache đã được kiểm đúng pin; không tự chuyển sang Hugging Face hoặc nguồn phát triển khi VPS lỗi.
8. Bản đầu cài theo Windows user. Chuẩn bị model trước đăng nhập không cấp quyền nghiệp vụ: auth/license/project/organization gate của ứng dụng vẫn áp dụng khi sử dụng.
9. Cài lại hoặc chạy tiếp phải dùng lại file hợp lệ, tiếp tục phần tải dở phù hợp và khôi phục được nếu dừng giữa chừng. Không bắt người dùng tải lại 2,85 GB chỉ vì mở Setup lần nữa.

Đây là hướng triển khai mới theo ZIP công khai trên VPS. Những bước catalog ký/HTTPS 443 trong [kế hoạch test server trước đây](PLAN_TASK_TEST_SETUP_MODEL_TU_SERVER.md) vẫn thuộc chế độ Server; chúng không tự áp dụng thành điều kiện bắt buộc của StaticArchive. Các kết quả lịch sử trong tài liệu cũ giữ nguyên phạm vi và thời điểm của chúng.

## 2. Đầu vào đã xác minh và giới hạn bằng chứng

### 2.1. Gói ZIP trên VPS

| Thuộc tính | Giá trị |
|---|---|
| URL | `https://103.179.190.182:8443/models/VideoMaker-Models-20261016-082700.zip` |
| Kích thước | **2.847.980.994 byte**, khoảng 2,85 GB / 2,65 GiB |
| SHA-256 toàn ZIP | `e9939863910af178ec6be6e99f7fcc16948b6a8d916bd5b32bc0a50d92d3f471` |
| Candidate nguồn | `models-20261016-03` |
| SHA-256 inventory nguồn | `2f387304020b06c9c59747eb3cbf3dda08e4b029eafd49d4c77581c4154d4843` |
| HTTP | Full GET **200**, `application/zip`, đủ số byte |
| Tải tiếp | Ba phép thử Range/If-Range **206**, mẫu đầu/cuối/giữa khớp ZIP gốc |
| TLS | Xác thực bình thường; không bỏ qua kiểm tra chứng thư |
| Thời điểm kiểm full ZIP | `2026-10-16T15:36:00Z` |

Đã đọc toàn bộ luồng tải và đối chiếu hash, không lưu thêm bản ZIP trên máy kiểm tra. [Báo cáo full SHA-256](D:/vm-tests/vps-model-20261016-222717-21483a/https-stream-verification.json) và [báo cáo Range](D:/vm-tests/model-url-range-20261016-152646-e33d0f59/range-verification.json) là bằng chứng tải file tại thời điểm đó. Chúng **không chứng minh Setup đã cài hoặc runtime đã chạy trên máy khách**. Trước phát hành kiểm lại endpoint và đúng artifact.

### 2.2. Các thành phần trong gói

| Thành phần | Số byte đã pin | SHA-256 |
|---|---:|---|
| `Qwen3-4B-Q4_K_M.gguf` | 2.497.280.256 | `7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5` |
| `piper-offline.zip` | 159.970.075 | `e2d1b5f212d2cc6a3b547f9280e380416c57c5c151d639595009624caf4f6e1d` |
| Piper `manifest.json` | 977.507 | `69bfe70e9ef038f2c157db035cf6f59ae8310028359d93b1dc16de73c691f1b5` |

Gói bàn giao có 33 file, tổng giải nén đã ghi nhận **2.918.879.650 byte**; gồm artifact Qwen/Piper, năm `.nupkg` OCR, inventory, compatibility, license và provenance. Đây là định danh của gói, không phải bằng chứng runtime hiện hành. Khi triển khai phải đối chiếu lại với định nghĩa trong source; phát hiện khác pin thì dừng, không sửa checksum để chấp nhận.

Năm `.nupkg` OCR là **đầu vào build**. Bản cài máy khách lấy OCR/model/native DLL từ desktop đã publish; không giải nén các NuGet package thành một runtime OCR tùy ý.

Candidate cũ có metadata compatibility riêng. SC02 phải xác nhận tương thích với desktop/worker định phát hành; không sửa candidate hoặc ZIP đã bàn giao tại chỗ để hợp thức hóa version mới. Nếu không tương thích, tạo gói version mới, URL mới và hash mới.

## 3. Khoảng trống của source hiện tại

| Hạng mục | Hiện trạng đã đọc | Phần cần làm |
|---|---|---|
| Setup | [LauncherInstallerService](TOOL-SETUP/LauncherInstallerService.cs) tải/cài desktop, tạo shortcut, báo hoàn tất và tùy chọn launch | Đặt bước model/prerequisite/probe trước hoàn tất; có phục hồi toàn quy trình |
| Cấu hình Setup | [InstallerOptions](TOOL-SETUP/InstallerOptions.cs) chỉ có server/channel/platform; giá trị nhúng build được ưu tiên | Thêm metadata bộ cài/model có version; không dùng URL ZIP làm `ServerBaseUrl` ứng dụng |
| Model online | [DesktopModelDistributionClient](TOOL-LOCAL/ModelDistribution/DesktopModelDistributionClient.cs) chỉ HTTPS/443, đọc signed catalog; root hiện chưa có public key phát hành | Nguồn StaticArchive riêng, không chỉ đổi `Enabled`/`BaseUrl` |
| Resume | [ResumableModelDownloader](TOOL-LOCAL/ModelDistribution/ResumableModelDownloader.cs) đã có resume/hash nhưng ghép đường API catalog | Tách transport dùng chung với nguồn tĩnh, giữ regression của mode cũ |
| Qwen | [VietsubTranslationRuntime](TOOL-LOCAL/Vietsub/Translation/VietsubTranslationRuntime.cs) ưu tiên reuse, có đường tải nguồn gốc khi distribution tắt | Chế độ VPS phải giới hạn nguồn; giải quyết mapping và dùng đúng production root |
| Piper | [PiperOfflineBundle](TOOL-LOCAL/Vietsub/Voice/PiperOfflineBundle.cs) và [offline installer](TOOL-LOCAL/Vietsub/Voice/VietsubVoiceComponentStore.Offline.cs) có pin, Python/wheel offline và probe | Đưa cặp bundle đúng nơi, tạo runtime mới ở đường cuối, chạy dưới user đích |
| Bootstrap CLI | [QwenOfflineCommand](TOOL-LOCAL/SystemSetup/QwenOfflineCommand.cs), [PiperOfflineCommand](TOOL-LOCAL/SystemSetup/PiperOfflineCommand.cs) dành cho diagnostic workspace | Thêm command production, không coi diagnostic READY là cài thực tế đã xong |
| Readiness | Component `DISABLED` có thể không thuộc gate hiện tại; dịch local mặc định tắt | Bộ cài dùng tập bắt buộc cố định, không dùng cờ tắt Qwen để báo đủ bộ |
| WebView2 | [DesktopPrerequisites](TOOL-LOCAL/SystemSetup/DesktopPrerequisites.cs) chỉ hướng người dùng đến trang Microsoft | Tự chuẩn bị runtime đã duyệt khi thiếu, rồi kiểm lại |
| OCR native | [DEPENDENCIES](TOOL-LOCAL/SystemSetup/DEPENDENCIES.md) yêu cầu native DLL pin và Media Foundation | Giữ DLL cùng desktop; phát hiện prerequisite Windows thiếu |
| Temp/rollback | Setup dùng `%TEMP%` GUID và dọn trong `finally`; rollback chỉ trong bước copy app | Journal/backup bền, không mất khi model lỗi hoặc Setup crash |
| UI | [InstallerForm](TOOL-SETUP/InstallerForm.cs) truyền `CancellationToken.None`, chưa có nút hủy | Hủy có kiểm soát, tiếp tục sau lỗi, tiến độ theo giai đoạn |
| Publish | [Publish-DesktopRelease](scripts/Publish-DesktopRelease.ps1) chỉ `Bundled|Server`, chưa gắn ZIP model vào Setup | Thêm StaticArchive và kiểm bộ Setup/desktop/descriptor nhất quán |

## 4. Trải nghiệm cài đặt cần đạt

Trình tự dự kiến:

1. Mở Setup, chọn bản ứng dụng và thư mục cài; hiển thị bộ model bắt buộc và lượng dữ liệu còn phải tải.
2. Kiểm Windows/x64, đúng user, quyền ghi, ứng dụng/worker đang dùng, prerequisite, RAM/commit và dung lượng từng ổ.
3. Chốt snapshot release và khóa lượt cài; tải/xác minh/stage package ứng dụng cùng prerequisite payload để có bootstrap đúng phiên bản. Chưa thay bản đang hoạt động.
4. Nếu thiếu WebView2, chuẩn bị runtime chính thức đã duyệt rồi dùng bootstrap đã stage kiểm lại dưới user đích. Thiếu prerequisite Windows không thể tự xử lý thì hướng dẫn rõ trước tải ZIP model lớn. Khi prerequisite đạt, tải/xác minh ZIP model và dùng lại file hợp lệ.
5. Kiểm inventory và giải nén vào staging an toàn; chưa thay thế bản đang hoạt động khi tải/kiểm file chưa xong.
6. Cài ứng dụng, import đúng artifact Qwen/Piper, tạo runtime Piper offline ở thư mục đích của user.
7. Chạy probe Qwen Anh/Trung, Piper WAV, OCR Anh/Trung, FFmpeg/FFprobe và WebView2 trên bản cài đó.
8. Ghi evidence tại máy đích, commit lượt cài, tạo shortcut theo lựa chọn và báo hoàn tất. Chỉ lúc này mới launch khi người dùng chọn.
9. Sau đăng nhập/license, desktop dùng cùng resolver và runtime; giữ nguyên gate nghiệp vụ, không yêu cầu tải model lại.

Thiếu RAM/commit, prerequisite hoặc probe lỗi thì giữ trạng thái chưa hoàn tất. Người dùng có thể xem hướng xử lý, thử lại hoặc hủy; không tự đánh dấu READY, không tự chấp nhận cảnh báo tài nguyên và không đổi model sang bản nhỏ hơn.

## 5. Metadata và ranh giới module

### 5.1. Descriptor cho bản phát hành

Descriptor là schema mới cần thiết kế ở SC02–SC03, **chưa có trong code**. Trường tối thiểu:

| Nhóm | Dữ liệu cần có |
|---|---|
| Định danh | Schema version, bundle ID, source mode, channel, platform |
| Desktop | Version/build được ghép, hash/định danh package desktop được snapshot từ release đã kiểm |
| ZIP model | URL chính xác, số byte kiểu `long`, SHA-256 toàn ZIP, hash inventory |
| Component | ID/engine/bundle version, size/hash Qwen, Piper archive/manifest, phiên bản worker/protocol tương thích |
| Giới hạn | Danh sách entry được dùng, giới hạn giải nén/file count/file size, kích thước Piper lồng và phần cài venv |
| Chính sách | Bộ bắt buộc, nguồn được phép, cấm fallback mạng trong StaticArchive, quy tắc reuse |
| Prerequisite | Installer WebView2 đã duyệt: định danh/version/size/hash/publisher, điều kiện nền tảng |

Pin ngoài ZIP phải nằm trong payload/metadata phát hành được kiểm và bảo vệ cùng Setup/desktop. Bản v1 không cần dựng thêm dịch vụ catalog ký cho VPS. Nếu descriptor đặt rời, nó phải được neo hash vào executable/metadata tin cậy của release, không nhận bản tùy ý từ URL hoặc UI.

Setup có thể cho chọn phiên bản desktop từ API hiện hành; mỗi phiên bản phải có binding tới đúng descriptor. Không dùng một descriptor "mới nhất" chung cho mọi bản. Khi resume, giữ release snapshot từ lúc bắt đầu, không tự đổi sang release mới hơn vừa xuất hiện.

Đổi ZIP hoặc endpoint cần bản metadata phát hành mới đã kiểm. Giữ URL cũ phục vụ bộ cài cũ; không ghi đè cùng tên bằng nội dung khác. Cơ chế Authenticode của executable vẫn theo chính sách phát hành hiện hành; không tạo key hoặc chứng thư trong lượt lập kế hoạch.

### 5.2. Phân chia trách nhiệm

| Module | Trách nhiệm dự kiến |
|---|---|
| `TOOL-SHARED.Contracts` | DTO dùng chung mới nếu cần: descriptor, bootstrap request/progress/result có version; cập nhật trước các bên sử dụng |
| `TOOL-DISTRIBUTION` | Transport stream/resume, kiểm size/hash, giới hạn ZIP và dữ liệu metadata dùng chung; không tham chiếu WinForms/auth/SQL |
| `TOOL-SETUP` | UI, điều phối các giai đoạn, lựa chọn thư mục, journal/backup, gọi bootstrap, quyết định hoàn tất/launch |
| `TOOL-LOCAL` | Production bootstrap trước login; resolver runtime; cài/probe Qwen/Piper/OCR/media bằng các engine hiện hành |
| Translation worker | Giữ x64 cô lập và kiểm root/native/protocol; cập nhật đồng bộ nếu thêm binding root hợp lệ |
| Scripts phát hành | Đóng descriptor/prerequisite, chế độ StaticArchive, kiểm package và tạo bằng chứng |
| VPS | Phục vụ nguyên ZIP HTTPS/Range đang có; chưa cần thay Ghepgo hoặc backend chỉ để thực hiện chế độ này |

Chế độ SignedCatalog giữ nguyên kiểm chữ ký, trust state, sequence và thu hồi. Không đổi validation của mode cũ thành chấp nhận mọi host/port. Nếu đã biết artifact bị thu hồi theo chính sách/trust state áp dụng, không dùng StaticArchive để chấp nhận lại; không xóa state cũ để vượt chặn.

### 5.3. Bootstrap production

- Tên command và schema cụ thể chốt ở SC03/SC13; chạy trước `DesktopOptions.Load()` thông thường, login, SQL và tạo API client nghiệp vụ.
- Chỉ nhận descriptor/binding đã xác minh và đường do native Setup lập; không nhận URL, EXE hoặc root tùy ý từ WebView/DOM.
- Chạy đúng user cài. Payload local không mang JWT, connection string hay provider key sang tiến trình này.
- Phân biệt `prepare`, `verify`, `repair`, `cancel`; mọi lượt có operation ID, target identity và lock thống nhất với desktop System Setup.
- Gửi progress/result có schema qua IPC hoặc output riêng có giới hạn; tách JSON khỏi native stdout/stderr, không dùng một dòng "READY" tự do làm kết quả.
- Exit code và result phải khớp operation ID, descriptor, component và fingerprint. Không chỉ nhìn exit code 0.
- Ghi evidence bằng inspector/installer hiện hành tại đúng production root. Không copy marker từ workspace chẩn đoán hoặc máy build.

## 6. Thư mục, identity và dung lượng

### 6.1. Root và quyền

| Dữ liệu | Chính sách bản đầu |
|---|---|
| Ứng dụng | Mặc định `%LOCALAPPDATA%\Programs\VideoMaker`; giữ lựa chọn thư mục hiện hành nếu hợp lệ |
| Component | Mặc định `%LOCALAPPDATA%\ToolGenPostVideo\components` |
| Cache tải | Root bền theo user, khóa theo SHA-256; nằm ngoài `%TEMP%` ngẫu nhiên bị xóa mỗi lượt |
| Journal | Root native theo user dưới `system-setup`, chia operation ID; không chứa secret hoặc plaintext cấu hình |
| Stage/backup | Cạnh destination hoặc root kiểm soát trên cùng volume; chỉ lượt cài sở hữu được ghi/dọn |
| Model trên ổ khác | Chỉ root fixed drive thuộc dạng đã được host/worker cho phép, được chọn rõ và lưu binding native; chưa mở arbitrary path/UNC/removable/all-users |

SC05 phải chốt một resolver dùng chung giữa Setup, desktop và worker. Hiện worker có kiểm root; không thể chỉ thêm ô chọn bất kỳ thư mục nào ở Setup rồi cho worker bỏ qua kiểm tra. App root và component root có thể khác ổ; desktop phải đọc đúng binding đã xác minh, không tự dò sang model khác.

Không chạy toàn bộ Setup dưới tài khoản Administrator khác rồi cài model vào profile đó. Kiểm identity trước tải; nếu identity không đúng thì hướng dẫn chạy lại đúng user. Chỉ helper prerequisite riêng được elevate khi thực sự cần; quay về user gốc để chuẩn bị/probe model. Không tắt UAC/Defender hoặc yêu cầu quyền toàn máy cho thao tác model theo user.

Kiểm đường dẫn tiếng Việt, khoảng trắng, user profile dài và giới hạn path của Python/uv/native. Giới hạn tính trên đường cuối dài nhất trong manifest payload và installed files. Không hứa hỗ trợ mọi đường dài chỉ vì .NET hỗ trợ; nếu không đạt phải lỗi sớm hoặc dùng root ngắn đã được resolver/worker hỗ trợ.

### 6.2. Preflight theo từng volume

SC06 phải tính **đỉnh dung lượng còn cần thêm cho từng giai đoạn trên từng ổ**, dựa trên artifact đã có và bản copy dự kiến:

- Byte ZIP ứng dụng/model còn thiếu để tải; phần `.part` đã có đã chiếm đĩa phải được hạch toán đúng.
- Stage ứng dụng, file mới, backup của file managed cũ và file managed bị bỏ trong phiên bản mới.
- Gói model còn giữ trong cache, file giải nén được chọn và bản copy Qwen sang runtime đích nếu cần.
- Piper payload giải nén, `.venv` mới, wheel/cache tạm riêng, runtime cũ đang giữ để phục hồi.
- Prerequisite installer, journal và phần dự phòng có cấu hình đã kiểm.

Dùng số `long` và phép tính checked; xét ZIP lồng Piper và `InstalledFiles`, không chỉ kích thước ZIP ngoài. Khi dùng space đã được chiếm trong cache thì không cộng hai lần; reuse chỉ được giảm ngân sách sau khi xác minh file thực. Không cam kết chung "6 GB là đủ" cho mọi cấu hình.

Kiểm lại ngay trước tải, extract, copy lớn và tạo venv. Model có thể ở D nhưng `%TEMP%`/profile vẫn ở C; không chỉ kiểm một ổ. Nếu thiếu, hiển thị ổ thiếu và số byte cần thêm, cho chọn root hợp lệ hoặc quay lại; không tự xóa dữ liệu người dùng.

Tải và hash theo stream/buffer nhỏ; ZIP trên 2 GiB không đưa toàn bộ vào `byte[]`/`MemoryStream`. Chưa có task nào được coi đạt chỉ vì trên máy phát triển còn đủ chỗ.

## 7. Tải tiếp và xác minh archive

### 7.1. Quy tắc tải

| Tình huống | Hành vi bắt buộc |
|---|---|
| Chưa có file | Full GET, ghi `.part`, giới hạn byte theo descriptor |
| Có cache hoàn chỉnh | Hash lại theo policy trước dùng; đúng pin thì không tải lại |
| Có `.part` phù hợp | Receipt gắn URL/source scope, size/hash và strong ETag; gửi Range/If-Range đúng offset |
| Trả `206` | Kiểm Content-Range/total/offset/length và ETag; chỉ append khi khớp |
| Trả `200` khi xin Range | Reset/truncate phần cũ rồi ghi từ đầu; không append full file vào phần dở |
| Trả `416` | Nếu phần dở đã đủ thì kiểm full size/hash; không đủ/không khớp thì restart có giới hạn |
| ETag đổi/weak/thiếu | Không nối mù; full hash vẫn quyết định cuối, restart nếu không thể resume an toàn |
| Mất mạng/timeout/429/5xx | Retry có giới hạn, backoff và Retry-After có trần; thời gian chờ hủy được |
| 404/403/TLS lỗi/redirect lạ | Báo lỗi đúng nhóm; không hạ HTTPS, bỏ kiểm chứng thư hoặc đổi nguồn |
| Hash sai/HTML thay ZIP/encoding sai | Không promote/extract; cách ly hoặc loại đúng scratch sở hữu, giữ runtime tốt |
| Hủy/đóng Setup | Giữ `.part`/receipt đã ghi bền và journal để lần sau tải tiếp |

Phân biệt timeout kết nối/header, thời gian không nhận byte và tổng thời gian thao tác. Không dùng timeout tổng quá ngắn khiến mạng chậm hợp lệ luôn thất bại; không để read treo vô hạn. Tham số retry/timeout cần được chốt và kiểm bằng test, không thêm retry vô hạn.

Client model không chuyển Authorization/cookie từ API ứng dụng sang VPS. HTTPS dùng đúng IP/port/path được gắn trong release; redirect mặc định tắt, không đổi chính sách proxy/certificate của Windows. Sau tải vẫn kiểm full size/SHA-256 dù từng đoạn Range đã đúng.

### 7.2. Kiểm ZIP và inventory

1. Hoàn tất hash ZIP ngoài trước khi parse/import dữ liệu bên trong.
2. Kiểm inventory pin độc lập, schema/version và tương thích với worker/app đã snapshot.
3. Kiểm danh sách entry, size tổng, size từng file, duplicate không phân biệt hoa/thường, entry không hợp lệ và ZIP hỏng.
4. Chặn path traversal, absolute/UNC/drive path, ADS, tên thiết bị Windows, symlink/reparse/junction và đích thoát staging. Không tin root thư mục chỉ từ tên ZIP.
5. Đọc theo stream với giới hạn byte thực được giải nén; kiểm cả ZIP lồng Piper bằng định nghĩa đã duyệt để tránh vượt giới hạn/zip bomb.
6. Map từ đường `artifacts/<id>/<version>/<hash>/...` sang component bằng allowlist/descriptor, không lấy path đích từ inventory làm quyền ghi tùy ý.
7. Kiểm size/hash từng artifact Qwen/Piper; giữ license/provenance. Nupkg OCR có thể không extract vào máy khách, nhưng không xóa/sửa chúng trong ZIP gốc.
8. File chưa xác minh ở staging không được dùng làm runtime. Không sửa hash/compatibility của gói khi validation thất bại.

### 7.3. Bảng mapping phải bàn giao ở SC02/SC12

Các placeholder được lấy từ định nghĩa đã pin: `<C>` hiện là `qwen3-4b-q4-k-m-cpu`, `<E>` là `bc64014-llamasharp-0.27.0-adapter-1`, `<B>` là `piper-1.6.0-python-3.11.15-offline-v3`. Đây là mapping dự kiến của StaticArchive; resolver cho mode mới phải được bổ sung, không khẳng định desktop hiện đã nhận các đường này tự động.

| Nguồn trong ZIP | Payload sau import | Production runtime |
|---|---|---|
| `artifacts/<C>/<E>/<hash>/Qwen3-4B-Q4_K_M.gguf` | File staging đã verify, truyền tường minh cho component installer | `<ComponentsRoot>/vietsub-translation/<C>/<E>/Qwen3-4B-Q4_K_M.gguf` |
| Hai entry Piper manifest/archive thuộc hai thư mục hash riêng | `<CacheRoot>/bundles/piper/<B>/{manifest.json,piper-offline.zip}` được kiểm theo approved definition; source resolver dùng chung trả bundle này | `<ComponentsRoot>/voice/piper/runtime/<B>/`, gồm payload và `.venv` tạo trên máy đích |
| `licenses/`, `provenance/` và metadata bàn giao cần giữ | Vùng hồ sơ theo bundle ID trong root component kiểm soát | Không dùng làm executable hoặc evidence READY |
| Năm OCR `.nupkg` | Giữ trong ZIP/cache bàn giao; không import thành runtime | OCR/native/model do desktop publish cung cấp |

SC02 còn phải xuất inventory translation worker/native CPU, Piper worker/requirements và OCR/media/WebView2 loader đi với desktop. SC12 đối chiếu inventory đó trước bootstrap. Mode Bundled vẫn có thể dùng đường bundle trong app hiện hành; StaticArchive không dựa vào việc nhét GGUF hoặc venv ngoài manifest vào thư mục ứng dụng.

## 8. Chuẩn bị và probe component

| Component | Chuẩn bị trên máy khách | Điều kiện đạt |
|---|---|---|
| Qwen 4B | Import GGUF đúng engine vào production root, dùng translation worker/native của desktop vừa cài; không cần download nguồn gốc | Size/hash đúng, platform/backend/profile phù hợp, probe runtime và dịch Anh/Trung sang Việt đạt; evidence khớp worker/protocol/native/config/máy/user |
| Piper | Map ZIP/manifest đúng bundle; giải nén payload đã kiểm; dùng Python/uv/wheel đi kèm | Tạo `.venv` mới tại đường cuối, installed files đúng manifest, tạo WAV thật và kiểm RIFF/PCM/dung lượng; evidence khớp máy/user/worker/lockfile |
| OCR | Dùng model Anh/Trung, native Paddle và Visual C++ DLL đã publish cùng desktop | Native DLL đúng pin và được nạp đúng nơi; OCR fixture Anh/Trung đạt |
| FFmpeg/FFprobe | Dùng executable/license/provenance/checksums của desktop | Hash/version phù hợp, chạy và tạo/probe mẫu media đạt |
| WebView2 | Kiểm loader của desktop và Evergreen Runtime | Sau chuẩn bị, kiểm version/runtime và khởi tạo được dưới đúng user |

### 8.1. Qwen

- Source hiện tại worker version `1.2.1`, IPC `3`; SC02 pin lại theo bản thực sự phát hành, không chép con số này vô điều kiện sang release sau.
- Dùng approved resource profile hiện hành; không tự hạ ngưỡng RAM, context hoặc backend để qua probe.
- Nếu cảnh báo cần xác nhận, UI yêu cầu xác nhận rõ gắn đúng profile/lượt kiểm. Từ chối hoặc cảnh báo thay đổi thì giữ payload đã kiểm, trạng thái vẫn chưa hoàn tất.
- Không dùng `DISABLED`, `ModelVerified=true` hoặc file GGUF tồn tại thay bằng chứng inference.
- Chế độ repair dùng lại GGUF đúng hash; đổi worker/native/config phải đánh giá lại fingerprint và probe cần thiết, không tải lại GGUF vô ích.

### 8.2. Piper

- Giữ cờ offline/no-index/require-hashes/no-python-downloads, Python trong payload và môi trường UV/PIP/PYTHON đã lọc như engine hiện hành.
- Chuẩn bị payload ở staging; **tạo venv sau khi payload vào đúng đường runtime cuối**. Không tạo venv ở staging rồi rename sang đường khác; Python/venv có thể chứa đường tuyệt đối.
- Giữ bản runtime tốt trước đó trong quá trình repair; bản backup chỉ dùng khôi phục về đúng đường gốc, không chạy venv trực tiếp từ đường backup mới.
- Tái sử dụng timeout, drain output, Windows Job Object và cơ chế dừng cây tiến trình do lượt cài tạo. Không dùng Python/uv trong PATH của máy khách.
- Không mang venv, cache, READY hoặc machine fingerprint từ máy đóng gói.

### 8.3. Prerequisite máy sạch

Setup và desktop đang self-contained .NET; vẫn kiểm desktop, worker và native payload thật, không suy mọi executable con đều đầy đủ chỉ từ Setup chạy được.

Để đạt cài trong một lần Setup trên máy thiếu WebView2, P0 là chuẩn bị **Microsoft Evergreen Standalone Installer x64 chính thức**, xác nhận quyền phân phối/provenance, version/size/hash/chữ ký Microsoft, đóng trong prerequisite payload phát hành. Chỉ chạy khi thiếu và kiểm lại sau cài. Microsoft mô tả standalone offline và cài theo user/per-machine tùy elevation tại [tài liệu phân phối WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution). Không thêm phụ thuộc vào một URL bootstrapper thay đổi chưa được pin.

Nếu cần UAC hoặc restart, ghi trạng thái rõ, không tự reboot và không báo complete trước khi probe lại. Không gỡ WebView2 dùng chung khi rollback app/model.

Windows N/KN thiếu Media Foundation cần Feature Pack phù hợp hệ điều hành; phát hiện sớm, hướng dẫn theo [Microsoft Media Feature Pack](https://support.microsoft.com/en-us/windows/experience/platform-variants/media-feature-pack-for-windows-n), rồi kiểm lại sau người dùng xử lý/reboot. Bản đầu không tự chạy DISM hoặc copy DLL hệ thống. Chưa xử lý prerequisite này thì máy đó chưa nghiệm thu cài hoàn tất.

## 9. Journal, commit và phục hồi

### 9.1. Trạng thái dự kiến

| Trạng thái | Ý nghĩa |
|---|---|
| `Preflight` | Chưa mutate; đang kiểm nền tảng/identity/path/resource và chốt snapshot |
| `PrerequisitePending` | Cần WebView2/Feature Pack/reboot; chưa hoàn tất |
| `Downloading` | Tải desktop/model, có receipt/checkpoint bền |
| `FilesVerified` | Byte đúng size/hash, chưa chứng minh runtime chạy |
| `Staged` | App/payload đã được kiểm ở staging; backup/commit plan sẵn sàng |
| `AppInstalledModelsPending` | Desktop đã đặt vào đích nhưng model/prerequisite/probe chưa xong; chưa complete/auto-launch |
| `ModelsPrepared` | Model/runtime đã chuẩn bị; còn probe/evidence |
| `AwaitingResourceConfirmation` | Chờ xác nhận cảnh báo tài nguyên đúng profile |
| `Probing` | Kiểm runtime bằng bộ vừa cài ở đường đích |
| `Committed` | Bộ bắt buộc đạt, evidence và commit record đã ghi bền; cho complete/launch |
| `Interrupted` / `Cancelled` / `Failed` | Chưa thành công; có thể resume hoặc rollback tùy checkpoint và file thực |
| `RollbackPending` / `RolledBack` | Khôi phục bản tốt trước đó; không coi bản cài mới đã đạt |

Đây là schema thiết kế cần bổ sung; không khẳng định các trạng thái này đã có trong source. `FilesVerified`, `ModelsPrepared` và `Committed` phải là các mốc khác nhau.

### 9.2. Ghi bền và khóa

- Lock theo user/target/component để Setup thứ hai, desktop System Setup, updater và worker không thay cùng runtime khi đang dùng. Không tự kill ứng dụng người dùng để lấy lock.
- Journal ghi operation ID, SID/target binding, release/model descriptor hash, phase, file managed trước/sau và vị trí backup. Không lưu token, connection string, prompt hoặc toàn bộ appsettings plaintext.
- Ghi intent **trước** mutation; cập nhật checkpoint bằng file tạm, flush và replace/rename atomic trên cùng volume. Giữ bản journal trước hợp lệ để phục hồi nếu ghi lỗi.
- Resume phải đối chiếu file thực/hash với intent/done, không tin journal đơn độc. Journal hỏng không tạo READY; hướng verify/repair và giữ dữ liệu có thể khôi phục.
- Với bản StaticArchive mới, thiếu/hỏng journal hoặc mất checkpoint sau mutation cũng không được tự suy lượt cài đã hoàn tất. Gate cần đối chiếu commit record, manifest và evidence thực; thiếu bằng chứng thì vào verify/recovery trước nghiệp vụ. Không có file journal không đồng nghĩa "không có lượt cài dở".

### 9.3. App, model và nhiều volume

- Không có một atomic rename chung cho app ở D và model ở C. Mỗi destination stage/backup/promote cùng volume, được điều phối bằng journal có khả năng reconcile.
- Cache khác volume: copy sang staging đích, kiểm hash rồi rename tại đích. Không coi `Move` xuyên ổ hoặc hardlink là bản backup độc lập.
- App chỉ thay file managed trong manifest; backup cả file bị thay và file managed cũ bị loại. Giữ file ngoài manifest và cấu hình người dùng.
- Khi nâng từ desktop cũ, không giả định binary cũ biết đọc lock/journal mới. SC07/SC18 phải chốt và kiểm cơ chế chặn khởi chạy tại entrypoint/Windows trong cửa sổ thay managed files, rồi bàn giao sang gate của binary mới trước khi thay runtime. Có thể cần maintenance launcher đã kiểm/sign theo release; giải pháp cụ thể phải có bằng chứng ngăn mở từ shortcut cũ. Nếu không thiết lập được barrier an toàn thì dừng trước mutation, không tiếp tục copy rồi hy vọng app cũ không được mở.
- Khi update/repair, giữ app cũ cùng model/runtime tốt trước đó đến khi bản mới probe đạt và commit ghi bền. Không để `finally` xóa backup pending như cơ chế temp hiện tại.
- Qwen promote file đúng hash theo atomic file operation. Piper chuẩn bị payload rồi tạo venv ở đường cuối dưới lock; không coi rename một venv đã tạo ở đường khác là tương đương.
- Gate khởi động cần nhận biết lượt cài pending trước khi app tạo service nghiệp vụ, kể cả người dùng mở shortcut cũ trong lúc Setup dừng. Không chỉ chặn nút launch trong Setup.
- Nếu probe model lỗi sau khi app đã thay, mặc định giữ checkpoint để retry; khi người dùng chọn rollback thì khôi phục snapshot đã lưu. Cài mới không có bản cũ: giữ trạng thái chưa hoàn tất, cho tiếp tục hoặc gỡ đúng file mới do lượt cài sở hữu.
- Cleanup chỉ xóa scratch/backup đã xác định thuộc lượt cài và không còn cần phục hồi. Commit thành công nhưng cleanup lỗi là cảnh báo có thể dọn lại, không đổi thành rollback ngầm.
- Prerequisite dùng chung như WebView2 có lifecycle riêng; rollback VideoMaker không gỡ prerequisite của ứng dụng khác.

## 10. Cấu hình, readiness và tương thích cập nhật

1. Release StaticArchive mang descriptor/binding riêng để Setup và desktop nhận cùng nguồn. Setup hiện giữ `appsettings.json` cũ, nên không thể dựa vào việc chép config mới để đổi nguồn model.
2. SC23 phải chốt thứ tự ưu tiên metadata release và native user binding; nguồn URL/hash/version không được override tùy ý qua appsettings cũ/DOM. Lựa chọn root hợp lệ của user được giữ riêng và được kiểm.
3. Nếu cần migration cấu hình, chỉ sửa whitelist property thuộc phần cài model, có version/backup và idempotent. Không ghi đè toàn bộ cấu hình SQL/server/workspace hoặc token/device của khách.
4. Giữ mặc định source an toàn. Profile release dành cho bộ dịch phải bật đúng Qwen/Piper/OCR sau các gate nghiệm thu hiện hành; không tắt Qwen để tạo kết quả "đủ thành phần".
5. Result bootstrap luôn ghi trạng thái riêng cho Qwen4B, Piper, OCR, media và WebView2. Tập bắt buộc lấy từ descriptor đã kiểm; `DISABLED`, `SKIPPED`, chưa chạy hoặc chỉ có file không được tính đạt.
6. Evidence gắn user/máy, đường runtime, model và worker/config/native/backend hiện hành. Desktop System Setup đọc cùng kết quả qua inspector, không dựa vào một marker hoàn tất do Setup tự viết.
7. Khi chạy lại cùng bản hợp lệ, verify evidence/fingerprint rồi reuse; chỉ probe lại theo điều kiện hiện hành hoặc người dùng yêu cầu. Khi worker/config/native đổi hoặc evidence hỏng, probe lại phần cần thiết, không tải lại model đúng hash.
8. Mode Bundled và SignedCatalog/Server vẫn tương thích với package cũ. StaticArchive lỗi không tự rơi sang mode cũ hoặc dùng Hugging Face. Repair trong app cũng phải dùng đúng nguồn đã chọn.
9. Gói mới vẫn giữ giới hạn release/updater desktop hiện hành. ZIP model 2,85 GB ở VPS riêng; không tăng cap server 2 GiB chỉ để nhét ZIP model vào desktop release.

## 11. Danh sách task triển khai

Tất cả task dưới đây là **chưa thực hiện**. `P0` là điều kiện để tuyên bố bộ cài đầy đủ ổn định trong phạm vi hỗ trợ đã chốt. "Đạt" trong bảng là tiêu chí hoàn tất tương lai, không phải kết quả kiểm thử hiện có.

### A. Chốt đầu vào và thiết kế nền

| Task | Mức | Công việc | Điều kiện hoàn tất | Phụ thuộc |
|---|---|---|---|---|
| SC01 | P0 | Chụp baseline source/working tree và phạm vi release | Có branch/commit, hash snapshot source thực, danh sách thay đổi cần giữ; không dùng output build cũ làm source | — |
| SC02 | P0 | Chốt version/build/channel, descriptor và compatibility | Pin ZIP mục 2, component/worker/protocol, bộ bắt buộc, schema và mapping đúng bản desktop; đầu vào release/prerequisite còn thiếu ghi rõ | SC01 |
| SC03 | P0 | Thiết kế contract/bootstrap/source interface dùng chung | DTO/progress/result có version và code lỗi; cập nhật Contracts trước bên dùng; engine dùng được trước login, không kéo auth/SQL vào bootstrap | SC02 |
| SC04 | P0 | Thiết kế và thêm StaticArchive policy | Cho đúng origin model 8443; giữ Bundled/Server; URL/hash neo release; chặn fallback, giữ trust state/thu hồi áp dụng | SC02, SC03 |
| SC05 | P0 | Resolver root và identity dùng chung | App/cache/components/journal chọn đúng user; fixed-drive binding hợp lệ, host/worker đồng bộ; path không hỗ trợ lỗi sớm | SC03 |
| SC06 | P0 | Dự toán/check disk theo volume và phase | Tính đủ ZIP/stage/backup/Qwen/Piper/venv/prerequisite, checked long, reuse hợp lệ; UI báo ổ thiếu và recheck trước ghi lớn | SC02, SC05 |
| SC07 | P0 | Khóa cài và barrier khởi chạy | Chặn Setup song song, app/updater/job đang dùng; có barrier cho bản desktop cũ chưa hiểu journal mới, handoff an toàn sang gate mới; lock giải phóng sau hủy/crash, không kill process người dùng | SC05 |
| SC08 | P0 | Journal bền và schema trạng thái | Intent/checkpoint atomic, snapshot pin cố định, receipt/backup không ở temp bị dọn; corrupt journal không tạo READY | SC02, SC05, SC07 |

### B. Tải và import payload

| Task | Mức | Công việc | Điều kiện hoàn tất | Phụ thuộc |
|---|---|---|---|---|
| SC09 | P0 | Tách transport resume/hash dùng chung | Tái sử dụng thuật toán hiện có, tách URI resolver khỏi API catalog; mode cũ vẫn giữ validation/trust riêng | SC03, SC04 |
| SC10 | P0 | Downloader StaticArchive và cache bền | Đạt quy tắc 200/206/416/ETag, retry có trần, timeout/hủy, stream >2 GiB; hash sai không promote; resume qua lần mở Setup mới | SC06–SC09 |
| SC11 | P0 | Validator ZIP/inventory/compatibility | Kiểm outer hash rồi inventory pin, entry/path/link/size lồng; file thiếu/thừa/sai không lọt vào runtime | SC02, SC09 |
| SC12 | P0 | Import Qwen/Piper và lưu license/provenance | Mapping allowlist tới đúng root; verify từng artifact; không cài nupkg OCR lên runtime, không lấy marker/venv/cache máy build | SC05–SC08, SC10, SC11 |

### C. Bootstrap và runtime thật

| Task | Mức | Công việc | Điều kiện hoàn tất | Phụ thuộc |
|---|---|---|---|---|
| SC13 | P0 | Production bootstrap trước login | Command mới dùng installer/probe chung, lock/journal và result có version; không đọc DB/key, không nhận URL/path tùy ý, không dùng diagnostic workspace để báo production READY | SC03–SC08, SC12 |
| SC14 | P0 | Cài/probe Qwen 4B CPU | Dùng GGUF đã pin, production root và worker đúng release; probe en/zh đạt; resource warning accept/decline đúng profile và không tự fallback | SC13 |
| SC15 | P0 | Cài/probe Piper offline | Tạo venv ở đường cuối, Python/wheel/installed files pin đúng; không mạng phụ; WAV thực đạt, cancel không orphan | SC13 |
| SC16 | P0 | Kiểm OCR/FFmpeg/native trong desktop | Fixture OCR en/zh, DLL đúng nguồn/pin và media probe đạt khi không có dependency developer ngoài gói; prerequisite thiếu có lỗi riêng | SC13 |
| SC17 | P0 | Provision/verify WebView2 và prerequisite | Standalone x64 chính thức được duyệt/pin/đóng gói; thiếu thì cài và probe lại đúng user; UAC cancel/reboot/Media Foundation thiếu chưa được báo complete | SC02, SC05, SC06, SC13 |

### D. Điều phối Setup, phục hồi và phát hành

| Task | Mức | Công việc | Điều kiện hoàn tất | Phụ thuộc |
|---|---|---|---|---|
| SC18 | P0 | Chuyển app install sang transaction có journal | Tải/verify/stage và lập barrier trước mutate, backup bền file replace/create/remove managed, giữ userfiles/config; binary cũ không mở giữa copy, không complete/launch sớm | SC06–SC08, SC10, SC11 |
| SC19 | P0 | Commit model theo component và phối hợp nhiều volume | Copy+hash khi khác ổ, promote cùng ổ, giữ runtime cũ; Piper venv không đổi đường sau tạo; bản mới chỉ active theo phase đã kiểm | SC12–SC18 |
| SC20 | P0 | Recovery/resume/rollback khi dừng giữa chừng | Mở lại reconcile file thực với intent/done; giữ pin snapshot; pending app không bypass gate; rollback đúng managed files và không mất dữ liệu | SC08, SC18, SC19 |
| SC21 | P0 | Hủy thao tác và cleanup đúng quyền sở hữu | Token truyền xuyên UI/download/extract/uv/worker/probe; chỉ dừng tiến trình con sở hữu; giữ phần tải tốt, backup pending; cleanup idempotent | SC07–SC20 |
| SC22 | P0 | UI Setup đầy đủ các phase và lỗi | Hiện bytes/tổng/còn tải, file verified khác runtime ready; retry/resume/rollback/hủy; confirmation resource đúng lượt; complete chỉ sau commit | SC13–SC21 |
| SC23 | P0 | Metadata và cấu hình khi cài mới/update/repair | Descriptor đúng selected release; appsettings cũ được bảo vệ; config mới migrate whitelist nếu cần; source/root nhất quán Setup/desktop | SC02–SC05, SC18–SC20 |
| SC24 | P0 | Pipeline publish StaticArchive | Đóng descriptor/prerequisite và đồng bộ Setup/desktop/channel/build; giữ cap desktop/gate FFmpeg/license/signing/profile khách; không đưa secret/cache/marker vào gói | SC17, SC22, SC23 |
| SC25 | P0 | Nối desktop startup và repair | Gate pending và journal/commit thiếu/hỏng trước nghiệp vụ; sau login nhận evidence thật, không tải lại; hỏng runtime/đổi worker sửa đúng nguồn; giữ auth/license/organization policy | SC13–SC24 |

### E. Kiểm thử và nghiệm thu

| Task | Mức | Công việc | Điều kiện hoàn tất | Phụ thuộc |
|---|---|---|---|---|
| SC26 | P0 | Test logic core, HTTP và transaction bằng fixture | Các ca trọng yếu ở mục 12 có regression thật; pass/failed/skipped riêng; không dùng fake inference chứng minh runtime | SC09–SC25 |
| SC27 | P0 | Build/test solution và frontend đúng phạm vi | Các lệnh mục 13 chạy trên snapshot xác định; lưu log/TRX; mọi failure phân tích, không sửa gate hoặc tính skip thành pass | SC26 |
| SC28 | P0 | Cài Setup thật trên Windows/profile sạch | Đúng Setup hash; không Python/uv/VC++ developer/model/cache; tải đúng VPS, cài và probe cả bộ; WebView2 thiếu vẫn xử lý đúng | SC24–SC27 |
| SC29 | P0 | Thử mạng lỗi, disk, cancel, crash và rollback | Ma trận fault injection đạt; mỗi checkpoint quan trọng có trước/sau crash và phục hồi thực; không mất project/config | SC28 |
| SC30 | P0 | Cài lại/update/repair và ma trận path/hardware | Reuse không tải thừa; version/worker/config đổi xử lý đúng; Unicode/path/root/CPU/RAM theo phạm vi hỗ trợ đạt hoặc chặn đúng | SC28, SC29 |
| SC31 | P0 | Smoke workflow local sau cài và đăng nhập | License thật môi trường thử, tạo project → OCR → Qwen → Piper → nghe mẫu → xuất SRT/MP4; không provider có phí, không download model lại | SC28–SC30 |

### F. Bàn giao và rollout có kiểm soát

| Task | Mức | Công việc | Điều kiện hoàn tất | Phụ thuộc |
|---|---|---|---|---|
| SC32 | P0 | Đóng gói candidate và biên bản nghiệm thu | Có Setup/desktop/descriptor/source hashes, compatibility, report từng máy, artifact verified và runtime probe tách riêng; lỗi còn mở rõ | SC27–SC31 |
| SC33 | P0 | Chuẩn bị release thử trên môi trường được chỉ định | Chốt đích/version/channel và quyền tác động trước publish; app release Active đúng lịch cho Setup tải, URL model còn phục vụ nguyên byte; có phương án quay lại | SC32 |
| SC34 | P0 | Pilot máy khách được chọn | Người dùng thật chạy cùng Setup, đủ bộ sau một lượt trong phạm vi hỗ trợ; ghi thời gian/disk/retry/probe và kết quả startup | SC33 |
| SC35 | P0 | Runbook vận hành và xử lý sự cố | Hướng dẫn tải tiếp/repair/rollback/thu log an toàn, đổi URL/hash bằng release mới, không xóa model/userdata tùy ý; giữ bản cũ khi còn cần | SC32–SC34 |
| SC36 | P0 | Quyết định phát hành và xác nhận phạm vi đã rollout | Chỉ ghi production-ready sau bằng chứng và rollout đích thật; nếu thiếu Windows sạch/runtime/pilot thì ghi chưa đạt, không nâng trạng thái | SC34, SC35 |

Ưu tiên thực hiện: SC01–SC08 trước; sau khi contract/path/journal chốt, có thể làm song song transport/import, bootstrap component và UI/publish. SC18–SC25 phải hợp nhất trước khi kiểm Setup thật. Không chờ tới cuối mới thiết kế crash recovery hoặc prerequisite máy sạch.

## 12. Ma trận kiểm thử bắt buộc

| Mã | Kịch bản | Kết quả bắt buộc |
|---|---|---|
| AT01 | Windows x64 sạch trong phạm vi hỗ trợ, không model/cache/Python/uv/dependency developer | Setup tự chuẩn bị bộ cần thiết và probe thật; không dựa vào phần mềm máy build |
| AT02 | WebView2 thiếu/có/hỏng; installer bị hủy hoặc cần restart | Cài khi cần, probe sau cài; pending không complete; không gỡ runtime dùng chung khi rollback |
| AT03 | Windows N/KN thiếu Media Foundation hoặc nền tảng/CPU không hỗ trợ | Chặn/hướng xử lý đúng trước workload; không báo sai thành model hỏng/READY |
| AT04 | Full GET từ đúng VPS | Đủ byte/hash ZIP và từng artifact, có evidence nguồn tải an toàn |
| AT05 | Mất mạng giữa tải rồi bấm thử lại; đóng/mở Setup; khởi động lại Windows | Tiếp tục đúng file/offset hoặc restart an toàn khi không đủ điều kiện; pin không đổi |
| AT06 | Range đúng/sai, ETag đổi/weak/thiếu, trả 200 cho Range, 416 đủ hoặc thiếu file | Không append sai; full size/hash quyết định; retry có giới hạn |
| AT07 | Mạng chậm nhưng còn byte; kết nối/header/body treo; 429/5xx/404/TLS/redirect/HTML | Timeout/retry đúng nhóm, hủy được, không hạ TLS/đổi nguồn/treo vô hạn |
| AT08 | ZIP thiếu byte, hash sai, inventory/compatibility sai | Từ chối trước import/promote; bản tốt còn nguyên |
| AT09 | Traversal, duplicate case, symlink/reparse, path thiết bị/ADS, ZIP lồng vượt size | Không ghi ngoài staging/root, không vượt quota; lỗi rõ và cleanup đúng scratch |
| AT10 | Cài lại cùng bản/runtime hợp lệ | Không tải lại ZIP/model không cần; evidence reuse theo fingerprint thực |
| AT11 | Chỉ Qwen hỏng, chỉ Piper runtime hỏng, worker/native/config đổi | Repair đúng phần, probe lại cần thiết; không dùng marker cũ cho runtime mới |
| AT12 | App ở D, cache/model/temp ở C hoặc root fixed drive khác được cho phép | Tính per-volume và binding nhất quán; copy khác ổ kiểm hash; không giả atomic xuyên volume |
| AT13 | Thiếu đĩa từ đầu và hết đĩa giữa download/extract/copy/venv/journal | Dừng đúng phase; resume/rollback được; không xóa dữ liệu khác hoặc promote nửa file |
| AT14 | User/tên thư mục tiếng Việt, khoảng trắng, profile dài, path vượt giới hạn, app read-only | Các path hỗ trợ đạt; path/quyền không hỗ trợ lỗi sớm, không kết luận mọi uv error là lỗi path |
| AT15 | User thường, elevated cùng user, elevation bằng user khác | Root/evidence thuộc đúng user; không cài nhầm profile hoặc dùng READY người khác |
| AT16 | Mở hai Setup; app/updater/worker đang giữ runtime; mở shortcut bản cũ giữa upgrade | Khóa/barrier đúng scope; binary cũ không khởi chạy giữa copy, không sửa khi đang dùng, không tự kill process người dùng |
| AT17 | Hủy khi tải/extract/uv/Qwen/Piper probe; đóng/crash parent | Không orphan process do lượt cài tạo; token/lock giải phóng; không ghi READY giả |
| AT18 | Crash trước/sau ghi journal intent, replace file app, promote model, tạo venv, ghi evidence, commit, cleanup; journal hỏng/mất checkpoint sau mutation | Mở lại reconcile được, resume hoặc rollback đúng; không mất backup pending, không suy mất journal là được mở nghiệp vụ |
| AT19 | Qwen profile đủ tài nguyên; cảnh báo RAM/commit accept và decline; probe native crash/OOM | Kết quả theo profile, confirmation tường minh; decline/chưa probe không complete |
| AT20 | Qwen en/zh, Piper WAV, OCR en/zh, media mẫu ở production root | Probe thật đúng worker/native/model, report riêng từng thành phần |
| AT21 | Feature Qwen false, test bị skip, marker copy từ máy khác, evidence hỏng | Bộ bắt buộc không được thành complete do DISABLED/SKIPPED/marker giả |
| AT22 | Update từ bản cũ giữ appsettings/userfiles, resume khi server có release mới | Giữ cấu hình/dữ liệu cần bảo vệ, source đúng descriptor release đã pin; không tự đổi latest |
| AT23 | Mode Bundled/Server cũ và artifact đã biết bị thu hồi | Regression không phá trust/sequence/revoke; không dùng StaticArchive né chính sách |
| AT24 | Sau complete mở app, login/license và thao tác Local; mở lại sau reboot | Nhận runtime đúng root, không tải/cài lại; OCR/dịch/giọng/xuất đạt, gate quyền vẫn còn |

AT01/AT20/AT24 cần runtime thật. Các ca HTTP/archive/transaction có thể dùng fixture nhỏ cho regression; phải có thêm lượt tải ZIP >2 GiB thật để chứng minh đường `long`/stream hoạt động. Không tải nhiều GB cho mọi test unit.

Ma trận máy tối thiểu gồm CPU Intel và AMD phù hợp source, các profile RAM được dự án hỗ trợ, Windows x64 trong phạm vi phát hành, một máy không có WebView2 và một máy sạch dependency developer. Phiên bản Windows/hardware cụ thể chốt ở SC02/SC28; không mặc định mọi Windows x64 đều được hỗ trợ. Windows N thiếu Feature Pack là ca chặn có hướng dẫn, không được ghi thành cài một lượt đã đạt.

Để thử máy chưa có model, ưu tiên VM/profile kiểm tra riêng. Không tự xóa/cách ly model trên máy đang làm việc của người dùng trong kế hoạch này; nếu sau này dùng máy đó cần kiểm kê, phạm vi và phương án phục hồi riêng như kế hoạch test trước.

## 13. Cách chạy kiểm tra và lưu bằng chứng khi triển khai

Sau thay đổi source, thực hiện theo `AGENTS.md`:

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Frontend:

```powershell
Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
npm test
```

Đây là lệnh dự kiến cho giai đoạn triển khai; **chưa chạy trong lượt lập kế hoạch này**. Phạm vi thay đổi phải có test phù hợp; không tạo test hình thức chỉ kiểm lại chính implementation.

Mỗi lượt nghiệm thu lưu tối thiểu:

- Branch/commit và hash snapshot working tree thực, thời điểm build, version/build/channel/platform.
- Hash Setup, desktop package/EXE/worker, descriptor và ZIP model; ghi nguồn tải, byte tải mới/reuse và kết quả hash.
- Môi trường Windows/CPU/RAM/commit, prerequisite, volume/disk peak, root và identity đã giảm dữ liệu nhạy cảm khi chia sẻ.
- Operation/phase/result, checkpoint phục hồi, elapsed time; lỗi/exit/native code an toàn, không prompt/token/connection string/raw stderr.
- Kết quả từng component: `FilesVerified`, `RuntimeProbePassed`, fingerprint và thời điểm probe tại máy đích.
- TRX/log build/test, report smoke và biên bản cài Setup thật; Passed/Failed/Skipped riêng, không cộng trùng.
- Chưa rollout hoặc rollout vào đâu/phạm vi nào, ai nghiệm thu và cách quay lại; không suy production từ test fixture.

Biên bản mới phân biệt bốn mức: **có source → test tự động trên snapshot xác định → cài/probe thực tế trên môi trường xác định → rollout production**. Kết quả download VPS đã có ở mục 2 chỉ thuộc kiểm file/đường truyền.

## 14. Điều kiện trước khi tạo bản Setup để bàn giao

| Đầu vào | Hiện trạng / việc cần chốt |
|---|---|
| Nguồn model | Đã xác định URL, size/hash và kiểm GET/Range; kiểm lại tại thời điểm phát hành |
| Version/build/channel desktop | Chưa chốt cho bộ Setup mới; cần đọc release hiện hữu, không ghi đè bản đang có |
| Nguồn ứng dụng | Dùng API phân phối desktop thực đã được chỉ định; URL model không thay endpoint này |
| Compatibility | Cần xác nhận bộ model cũ với worker/native bản mới; giữ nguyên candidate gốc |
| WebView2 payload | Chưa chuẩn bị installer offline được duyệt/pin cho kế hoạch mới |
| Profile khách | Cấu hình phân phối được rà soát, không dùng config phát triển có credential/SQL phụ thuộc máy build |
| FFmpeg/phân phối thư viện | Giữ gate license/provenance/Release hiện hành; hồ sơ chưa đạt cần giải quyết thật, không đổi nhãn để qua script |
| Chữ ký executable | Theo chính sách Authenticode của kênh phát hành; ghi rõ signed/unsigned và mức nghiệm thu, không tự suy đã production-qualified |
| Máy kiểm | Cần Windows/profile sạch, quyền thử, disk và tài khoản/license môi trường thử; không dùng thanh toán/provider có phí để nghiệm thu installer |
| Publish/rollout | Chỉ thực hiện sau khi đích và phạm vi tác động được chỉ định trong bước triển khai sau; lượt này chưa publish |

Các đầu vào chưa chốt không ngăn viết core/fixture sau khi được yêu cầu code, nhưng phải giải quyết trước các task build candidate/release tương ứng. Không để SC32–SC36 được đánh dấu đạt khi còn prerequisite hoặc gate phát hành bị bỏ qua.

## 15. Phạm vi để sau bản đầu

- Tải artifact riêng từng component để repair một file không phải tải cả ZIP ngoài. Bản đầu vẫn dùng ZIP 2,85 GB đã có và reuse cache hợp lệ.
- Tự chọn Kokoro/Qwen1.7B/CUDA và nhiều bộ giọng/model.
- Di chuyển runtime giữa root tùy ý, network drive, removable drive và cài all-users.
- Dịch vụ catalog động/rotation cho StaticArchive; không cần dựng mới cho ZIP pin của bản đầu.
- Tự cài Media Feature Pack của Windows và điều phối reboot toàn máy.
- Tối ưu tốc độ/benchmark mở rộng ngoài các bài runtime/profile/benchmark bắt buộc của quy trình phát hành hiện hành.

Không đưa các phần này vào đường chính khiến bản đầu phải thay VPS/Ghepgo hoặc gỡ bỏ trust/gate của hệ thống hiện có.

## 16. Tiêu chí kết thúc

Chỉ kết luận **"Setup cài ứng dụng và model ổn định"** khi đáp ứng đầy đủ:

1. Setup thật tải đúng desktop và ZIP VPS đã pin trên máy thuộc phạm vi hỗ trợ.
2. Qwen/Piper nằm đúng production root; Piper được tạo mới offline trên máy đích, OCR/media/native/prerequisite đủ.
3. Toàn bộ thành phần bắt buộc probe đạt; file đúng hash hoặc cờ UI không thay bằng chứng này.
4. Mất mạng, cancel, crash, disk full và cài lại có kết quả phục hồi kiểm chứng được; không mất dữ liệu người dùng.
5. Mở desktop sau đăng nhập nhận đúng runtime, không yêu cầu tải/cài lại; workflow Local thực đạt.
6. Bộ phát hành giữ compatibility, license/provenance/signing/profile hiện hành; không có secret, venv/cache hoặc READY từ máy build.
7. Có biên bản môi trường/snapshot xác định và trạng thái rollout rõ. Chưa có các bằng chứng này thì giữ trạng thái **chưa nghiệm thu**, dù endpoint VPS đang tải tốt.
