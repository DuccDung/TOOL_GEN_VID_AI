# Kế hoạch test Setup tự tải model từ server trên máy hiện tại

Theo dõi thực hiện sau khi người dùng yêu cầu triển khai tại [biên bản triển khai và các điều kiện còn thiếu](TRIEN_KHAI_TEST_SETUP_MODEL_TU_SERVER.md). Các trạng thái “chưa thực hiện” bên dưới là baseline lúc lập kế hoạch, không phải kết luận về source sau đó.

Ngày lập: 2026-10-16. Nhánh rà soát `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, cùng working tree chưa commit. Commit nền không đại diện đầy đủ cho source đang có.

Mục tiêu là dùng chính máy hiện tại làm máy khách chưa có model khả dụng: cài bằng file Setup thật, tải Qwen và Piper từ server được chỉ định, kiểm file, tạo runtime rồi probe trước khi báo **Sẵn sàng**. Phải chứng minh dữ liệu đi qua server đó, giữ dữ liệu người dùng và có thể khôi phục khi thử thất bại.

Trạng thái: **chỉ lập kế hoạch**. Các task ST01–ST32 chưa thực hiện trong lượt này. Không sửa source/script/cấu hình, xóa hoặc cách ly model, tạo khóa, upload, publish, chạy Setup/inference hoặc tác động database. Kết quả local trước đây trong [biên bản kho model](TRIEN_KHAI_KHO_MODEL_SERVER_VA_CAI_DAT_AN_TOAN.md) là đầu vào lịch sử, không thay nghiệm thu lần cài mới qua server đích.

## 1 Phạm vi và kết quả cần đạt

| Thành phần | Phạm vi bài test |
|---|---|
| Qwen3 4B GGUF | Không còn bản đã cài/cache/nguồn reuse khả dụng cho bản thử; tải từ server rồi probe runtime, Anh và Trung. |
| Piper offline v3 | Không còn runtime/venv/marker và cặp ZIP/manifest khả dụng; tải cặp payload từ server, tạo môi trường mới và WAV mẫu. |
| OCR và FFmpeg | Đi cùng package desktop tương thích, được kiểm tại máy cài. Không xóa DLL hoặc tải DLL rời như model Qwen/Piper. |
| Kokoro, Qwen 1.7B, CUDA và các model khác | Ngoài phạm vi dọn của bài test chính; giữ nguyên khi không có chỉ định mở rộng. |
| Dự án và tài khoản | Giữ media, subtitle, output, database dự án, thiết lập, token, device identity và trạng thái tin cậy. |

Đây là phép thử **máy đang dùng nhưng chưa có model khả dụng**, không phải Windows sạch: máy vẫn có WebView2, prerequisite và phần mềm đã cài. Nghiệm thu Windows/profile sạch là bước riêng.

Trong bài thử, “dọn model” ưu tiên cách ly bản cũ vào khu lưu có phục hồi, ngoài mọi đường tìm của ứng dụng. File có thể còn trên đĩa dưới dạng backup; không gọi trạng thái đó là đã xóa vật lý toàn bộ model. Xóa vật lý bản cách ly chỉ nằm ở ST32 sau khi đã có kết quả và thời hạn giữ được chốt.

Tiêu chí thành công chính:

1. Đúng Setup và desktop build đã chốt được tải từ môi trường thử của người dùng.
2. Trước cài component, Qwen/Piper hiện thiếu và không còn nguồn local có thể tự dùng lại.
3. Có bằng chứng ba artifact được tải từ đúng domain/kho model đã ký, đúng size/hash.
4. Piper tạo venv mới; Qwen/Piper probe đạt trên bản cài đó; OCR/media đạt với dependency của package.
5. Chỉ sau đó Setup và host mở khóa nghiệp vụ. Hủy/mất mạng/file sai không tạo READY giả.
6. Dữ liệu người dùng còn nguyên, các thay đổi thử được ghi riêng và khôi phục được.

## 2 Baseline đã xác định

Các pin sau có trong source và bộ bàn giao đã tạo; khi triển khai vẫn phải kiểm lại file thực trước ký/chuyển:

| Artifact | Dung lượng byte | SHA 256 |
|---|---:|---|
| `Qwen3-4B-Q4_K_M.gguf` | 2.497.280.256 | `7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5` |
| `piper-offline.zip` | 159.970.075 | `e2d1b5f212d2cc6a3b547f9280e380416c57c5c151d639595009624caf4f6e1d` |
| Piper `manifest.json` | 977.507 | `69bfe70e9ef038f2c157db035cf6f59ae8310028359d93b1dc16de73c691f1b5` |

Tổng ba artifact tải riêng là **2.658.227.838 byte**, không tính retry, package ứng dụng hoặc bằng chứng. OCR có năm package đầu vào build/repair trong bộ bàn giao; chúng không phải năm file runtime để Setup tải riêng.

Bộ đã tập hợp nằm tại `D:\VideoMakerModelDistribution\handover\models-20261016-03`; SHA-256 inventory đã ghi là `2f387304020b06c9c59747eb3cbf3dda08e4b029eafd49d4c77581c4154d4843`. Bộ này vẫn có phạm vi candidate Staging/build cũ; giữ nguyên nó làm nguồn đối chiếu, không sửa inventory hoặc compatibility tại chỗ để biến thành bản test mới.

Source đã có server kho chỉ đọc, signer/publisher, downloader/resume và adapter Qwen/Piper. Mặc định kênh model server đang tắt, public root nhúng chưa có khóa phát hành; chưa có server đích được xác minh. Local proof trước dùng listener loopback và khóa thử đã xóa, không phải bộ kho đã triển khai trên server của người dùng.

## 3 Điều kiện phải giải quyết trước khi dọn máy

| Điều kiện | Yêu cầu cho lần thử |
|---|---|
| Server đích | Chốt máy/hosting, domain HTTPS/443, thư mục staging/kho, cách chuyển file, quyền và phạm vi thay binary/config/recycle. Không suy đích từ appsettings của checkout. |
| Channel | Dự kiến dùng **Development trên môi trường staging riêng**. Setup, desktop `Update.Channel`, catalog, server allowlist và release phải khớp. Pipeline desktop hiện không nhận channel `Staging`. |
| Version và build | Chọn cặp mới sau khi kiểm release đang thấy trên đích; chốt cùng source snapshot, candidate và runtime/worker. Không mặc định dùng lại build 1 hoặc ghi đè artifact cũ. |
| Ba lớp xác thực | TLS cho domain; khóa P-256 cho catalog; chứng thư Authenticode cho executable do dự án phát hành. Khóa riêng không nằm trong repo, web server, kho tải hoặc câu trả lời. |
| Điều kiện phát hành | Pipeline desktop luôn yêu cầu FFmpeg Release hợp lệ và profile SQL chuyển tiếp, kể cả channel Development. Hồ sơ FFmpeg hiện còn Development; không đổi nhãn hoặc bỏ gate để chạy bài thử. |
| Phiên và license | Dùng tài khoản/device/license đang có quyền thử Local. Local cá nhân không cần tổ chức; context tổ chức vẫn cần role hợp lệ. Không tạo thanh toán/webhook hoặc request provider có phí. |
| Cờ component | Profile thử bật rõ Vietsub, OCR, dịch local Qwen và giọng local Piper. Qwen mặc định tắt có thể làm Setup đạt mà không hề thử Qwen. Không đổi mặc định toàn sản phẩm chỉ để thử. |
| Dung lượng | Lúc lập kế hoạch C còn **1.667.043.328 byte, khoảng 1,55 GiB**; D còn khoảng 19,38 GiB. Đây là snapshot, phải đo lại trước thao tác. Cache mặc định ở C, nên tình trạng hiện tại không đủ cho tải Qwen. |
| Piper và đường dẫn | Thử đúng đường runtime mà desktop sẽ chọn. Lịch sử workspace ngắn đạt, workspace dài lỗi trước tạo venv; không dùng CLI với đường ngắn thay kết quả cài mặc định. |

Tính đĩa cho cả Setup ZIP/giải nén, artifact cache, bản copy vào component, Piper prepared bundle, payload giải nén, venv mới, bản phục hồi và dự phòng trên từng volume. Riêng downloader Qwen hiện đòi phần byte còn tải cộng reserve khoảng 3,3 GB và 64 MiB dự phòng; không lấy con số model 2,5 GB làm yêu cầu free disk tổng.

Rename cùng volume không giải phóng dung lượng. Nếu phải chuyển backup sang volume khác, copy và hash bản backup trước khi loại bản nguồn; không coi rename hoặc hardlink là bản sao độc lập.

Ưu tiên giữ cache mặc định để kiểm luồng khách chuẩn. Nếu C không thể đáp ứng, chốt cách xử lý trước: không tự dọn dữ liệu khác. Override cache cho riêng máy thử, nếu sử dụng, phải được ghi rõ sau merge cấu hình, giữ state tin cậy tương ứng và ghi kết quả là luồng có override. Không đóng đường cache máy phát triển vào release, không đổi cache/repository/channel để né revoke hoặc sequence.

## 4 Danh mục dữ liệu cần giữ và cần cách ly

Các đường sau là pattern source để kiểm kê; chưa phải lệnh xóa. ST03 phải resolve thành danh sách absolute path thực theo tài khoản, cấu hình sau merge và volume hiện có.

Quy ước:

- `<C>` là `qwen3-4b-q4-k-m-cpu`.
- `<E>` là `bc64014-llamasharp-0.27.0-adapter-1`.
- `<B>` là `piper-1.6.0-python-3.11.15-offline-v3`.
- `<CacheRoot>` lấy từ cấu hình hiệu lực; mặc định `%LOCALAPPDATA%\ToolGenPostVideo\components\model-downloads`.
- `<WorkspaceRoot>` là root cấu hình trước khi thêm thư mục `vietsub`.

| Nhóm | Đường hoặc nguồn cần kiểm kê | Xử lý trong bài thử |
|---|---|---|
| Qwen AppData | `%LOCALAPPDATA%\ToolGenPostVideo\components\vietsub-translation\<C>\<E>\` | Cách ly model, `.partial`, `probe.json` và bản ghi tạm của đúng version. |
| Qwen fixed drive | `<ổ Fixed>:\VideoMakerData\components\vietsub-translation\<C>\<E>\` | Kiểm cả nguồn chưa active, chỉ cách ly đúng component/version đã lập danh sách. |
| Qwen nguồn reuse | `third_party\translation\runtime-test\<C>\<E>\Qwen3-4B-Q4_K_M.gguf` dưới tối đa 10 ancestor của thư mục EXE **và CurrentDirectory** | Cài ngoài repo, launch tại thư mục cài; xác nhận các candidate của phiên thử không có file. Giữ nguồn/bộ bàn giao ngoài tập đường tìm. |
| Artifact cache | `<CacheRoot>\artifacts\<sha256>\<filename>` của đúng ba artifact | Cách ly cả file hoàn chỉnh và scratch `.part`, receipt, `.tmp`; chỉ xử lý lock sau khi đã chứng minh không có lease đang giữ. |
| Piper prepared bundle | `<CacheRoot>\bundles\piper\<B>\` | Cách ly cặp ZIP/manifest và scratch riêng của bundle để buộc tải mới. |
| Piper trong app | `<InstallRoot>\components\piper\<B>\` | Package Server mới phải không có payload này; nếu còn thì loại bản package khỏi bài test. |
| Piper global | `%LOCALAPPDATA%\ToolGenPostVideo\components\voice\piper\` | Cách ly runtime/model/venv/cache/READY thuộc Piper; ghi rõ version offline và legacy. |
| Piper workspace legacy | `<WorkspaceRoot>\vietsub\components\voice\piper\` | Cách ly riêng thư mục Piper. Legacy tồn tại có thể làm desktop chọn nó thay global; kiểm cả hai phía sau dọn. |

Source Qwen chọn model đã có trong AppData/fixed drive và ưu tiên reuse local trước download; nguồn reuse có thể được hardlink. Do đó dọn một GGUF hoặc một marker không đủ. Nếu nhiều path cùng file ID, ghi mọi alias có thể được tìm; không sửa byte trên một alias để tạo lỗi vì có thể làm hỏng các hardlink khác.

Những phần được bảo vệ:

| Dữ liệu | Quy tắc |
|---|---|
| `<CacheRoot>\trust\*.json` | Giữ high-water sequence và known revocations, kể cả khi dọn artifact cache. Không xóa toàn bộ `model-downloads`. State có scope `repository/channel/platform`; đối chiếu cả scope cũ liên quan và scope Development thử, không coi scope mới trống là cách cho phép hash từng bị revoke. Nếu di chuyển cache hợp lệ, đóng app/worker liên quan trước khi chuyển state; conflict sequence/hash/revokes thì dừng, không chọn bản ít chặn. |
| Account/device/settings | Giữ `auth-token.bin`, `device-id.bin`, `settings\preferences.json` và cấu hình triển khai riêng. Token có thể được rotate hợp lệ khi đăng nhập; không yêu cầu hash token bất biến hoặc đưa token vào báo cáo. |
| Dự án | Giữ toàn bộ `<WorkspaceRoot>\vietsub\projects`, media, subtitle, SQLite, voice/output và dữ liệu workflow khác. Chỉ project fixture mới được dùng trong bài thử. |
| Journal Setup | Có thể giữ `system-setup` journal; source không coi journal cũ là bằng chứng READY. Không cần xóa cả metadata root. |
| Dependency và trust root | Giữ worker, lockfile, approved definitions, OCR/native, FFmpeg và public root nhúng. Không xóa DLL để giả lập thiếu model. |
| Nguồn bàn giao và bằng chứng | Giữ handover, license, provenance, checksum, snapshot, runtime smoke và source; chỉ bảo đảm chúng không nằm trong đường tìm của bản thử. |
| Model tùy chọn | Giữ Kokoro, Qwen 1.7B, voice consistency và runtime khác ngoài phạm vi. Không xóa thư mục cha `voice` hoặc toàn bộ `components`. |

## 5 Task chốt đầu vào và chuẩn bị

Tất cả task dưới đây đang **chưa thực hiện**. P0 là điều kiện bắt buộc trước bước phụ thuộc; P1 là nghiệm thu mở rộng. Mỗi task khi chạy phải có trạng thái Passed, Failed hoặc Not run cùng bằng chứng.

| Mã | Ưu tiên | Công việc | Điều kiện hoàn tất | Phụ thuộc |
|---|---|---|---|---|
| ST01 | P0 | Chốt server/domain/thư mục, máy thử, tài khoản/license và phạm vi tác động. | Có phiếu môi trường không chứa secret; xác định staging riêng, người chuyển file, cửa sổ recycle và cách phục hồi. | Đầu vào người dùng. |
| ST02 | P0 | Chốt version/build/channel và snapshot source của lần thử. | Development nhất quán; public root/repository/platform được định danh. Không dùng lại package/harness cũ làm Setup cuối. | ST01. |
| ST03 | P0 | Kiểm kê model, alias/hardlink, runtime/cache/marker và dữ liệu bảo vệ trên máy. | Có danh sách absolute path, vai trò, size/version, nguồn lựa chọn trước/sau dọn; resolve cả legacy, fixed drive, cấu hình user và ancestor reuse. Đối chiếu trust scope cũ liên quan với scope thử, ghi hash bị revoke nếu có. | ST02. |
| ST04 | P0 | Thiết kế backup/quarantine và kế hoạch khôi phục theo từng path. | Khu lưu ngoài đường tìm, không overwrite; map nguồn/backup, volume, file identity/hash cần kiểm và điều kiện restore. Không tạo thêm bản hardlink để gọi là backup. | ST03. |
| ST05 | P0 | Kiểm quota/disk/path/RAM/commit và chuẩn bị giải pháp cho máy thử. | Đủ dung lượng từng volume cả khi giữ backup; đường Piper thực được đánh giá, Windows long-path setting chỉ đọc; cảnh báo tài nguyên có xử lý theo gate. | ST03–ST04. |
| ST06 | P0 | Hoàn tất điều kiện ký/phân phối và profile đăng nhập trước đóng gói. | Có cách giữ khóa ngoài kho, public root duyệt, FFmpeg Release thật, license/source provision và profile SQL chuyển tiếp phù hợp. Thiếu thì dừng trước dọn máy. | ST01–ST02. |

## 6 Task đưa model lên server thật

| Mã | Ưu tiên | Công việc | Điều kiện hoàn tất | Phụ thuộc |
|---|---|---|---|---|
| ST07 | P0 | Tạo candidate mới cho version/build/channel đã chốt. | Inventory/checksums/compatibility/source snapshot mới; model giữ pin gốc; candidate 03 không bị sửa. | ST02, ST06. |
| ST08 | P0 | Ký và chuẩn bị kho bằng publisher offline. | `stage --channel Development` dùng exact inventory hash; catalog, public root, pin envelope và operation records được kiểm; không còn `.incomplete`. | ST07. |
| ST09 | P0 | Khảo sát rồi chuyển kho vào staging trên server được chỉ định. | Đủ capacity/quota, chặn junction/reparse ngoài phạm vi, kênh truyền xác định; hash lại ba artifact và metadata ở chính server. GGUF đi kho model riêng, không qua desktop release cap 2 GiB. | ST01, ST08. |
| ST10 | P0 | Cấu hình và kích hoạt kho thử qua module server chỉ đọc. | Backup binary/config/pin cũ; web process chỉ đọc; root/key/pin/sequence đồng bộ; staging/operation records không public. Nếu thay server binary, preflight startup/schema và tác động môi trường trước recycle; module model không tự yêu cầu migration mới. | ST09. |
| ST11 | P0 | Kiểm TLS, host, proxy và API qua domain thật. | Không cần bỏ kiểm chứng TLS hoặc thêm redirect lạ; catalog đúng chữ ký/pin; HEAD, Range đầu/cuối, ETag/If-Range và MIME đúng; HTTPS termination thật được nhận đúng. | ST10. |
| ST12 | P0 | Tải kiểm đầy đủ Qwen và Piper từ đích về workspace xác minh riêng. | GGUF >2 GiB và cặp Piper đúng bytes/hash hai đầu; có log của đích và file kết quả. Workspace xác minh được giữ ngoài đường reuse của bản desktop thử. | ST11. |
| ST13 | P0 | Đo cold scan, timeout, retry và truy cập đồng thời. | Đo một client và hai client; xác định cold headers hoàn tất trong hạn 60 giây có biên phù hợp, 503/rate limit/NAT không khiến luồng cài không phục hồi. Ghi lỗi hoặc tách task sửa trước nghiệm thu tải nếu không đạt. | ST11–ST12. |

Server cần `Enabled`, `StorageRoot`, `RepositoryId`, `Platform=win-x64`, `AllowedChannels`, `PublicKeysPem`, `TrustedCatalogSha256ByChannel.Development` và `MinimumSequence` phù hợp. Pin server là hash **toàn envelope catalog**, khác hash payload được lưu trong high-water desktop. Không format lại file sau chốt pin. `IOptions`/service singleton cần quy trình reload/recycle phù hợp khi thay cấu hình.

Mỗi GET/HEAD/Range hiện quét hash toàn file trước response, một lượt scan cùng lúc; bận trả 503/Retry-After 5 giây. Downloader hiện retry ngắn 300/600/900 ms, chưa theo Retry-After. Đây là hạn chế có trong source cần kiểm ST13, không coi loopback trước là benchmark hosting thật. Không tự tin `X-Forwarded-Proto` từ mọi client để xử lý proxy.

## 7 Task chuẩn bị bộ Setup tải model từ server

| Mã | Ưu tiên | Công việc | Điều kiện hoàn tất | Phụ thuộc |
|---|---|---|---|---|
| ST14 | P0 | Tạo deployment profile riêng cho bài thử. | `ModelDistribution.Enabled=true`, BaseUrl HTTPS/443 của đích, `Update.Channel=Development`, platform win-x64 và các cờ Qwen/Piper/OCR bật rõ. Kiểm config sau merge, không ghi secret vào repo. | ST02, ST06, ST11. |
| ST15 | P0 | Build/publish desktop với mode Server và public root đã duyệt. | Không GGUF/Piper payload/venv/READY trong package; đủ worker 1.2.1/IPC 3, approved definitions, lockfile, OCR/media/MSVC và assets. Bundled mode sản phẩm cũ được giữ. | ST14. |
| ST16 | P0 | Hoàn thiện ký executable, manifest/hash và kiểm package cuối. | Ký executable dự án trước chốt hash/manifest; kiểm root thực nhúng bằng `--check-model-distribution-config`, version/build/profile và ZIP/expanded cap hiện hành. Nếu pipeline thiếu điểm ký đúng thứ tự, tách task hoàn thiện; không ký xong rồi giữ hash cũ. | ST15. |
| ST17 | P0 | Đưa package và Setup đúng build lên kênh thử. | Launcher endpoint trả đúng release và download; Setup nhúng đúng server/channel/platform; cài vào thư mục mới trống ngoài repo. Bỏ chọn “Chạy VideoMaker sau khi cài” để desktop không mở/reuse model trước ST23; tùy chọn hiện mặc định bật. Không thay release Stable khách đang dùng. | ST12–ST13, ST16. |
| ST18 | P0 | Kiểm trước dọn toàn bộ môi trường thử. | ST01–ST17 đạt; merged config sau cài đúng, disk/path đủ, bản Setup nhận diện được, trust state giữ được và không né known revoke qua scope mới, đường backup/restore hợp lệ; Piper không còn blocker trên chính đường runtime thử. | ST03–ST05, ST17. |

Pipeline dùng `-ModelDeliveryMode Server -ModelDistributionTrustRootPath <approved-public-root>`, không kèm `IncludeQwenModel`, `QwenModelPath` hoặc `PiperBundlePath`. Validator yêu cầu cache mặc định trong profile đóng gói. File `appsettings.user.json` còn ở thư mục cài có thể đổi config hiệu lực; vì installer giữ cấu hình cũ khi cài đè, bài test phải dùng thư mục cài mới.

ST18 là điều kiện **trước dọn máy**. Nếu bước này dùng bản copy có model sẵn hoặc đường runtime ngắn thay đường của Setup cuối, nó chưa đủ. Có thể kiểm Piper đường đích bằng trường hợp thử có phục hồi trước đó; sau kiểm phải đưa mọi payload/runtime/marker phát sinh vào danh mục ST19 để lần tải đầu tiên vẫn sạch.

## 8 Task dọn model có phục hồi

| Mã | Ưu tiên | Công việc | Điều kiện hoàn tất | Phụ thuộc |
|---|---|---|---|---|
| ST19 | P0 | Chốt manifest thao tác cuối và giải phóng đúng app/worker đang dùng model. | Bổ sung file phát sinh từ preflight; absolute paths nằm trong allowlist; người dùng đóng tác vụ/desktop liên quan và lease đã nhả. Không dừng server/IDE hoặc xóa lock để ép chạy. | ST18. |
| ST20 | P0 | Cách ly Qwen đã cài và mọi alias/candidate có thể được bản thử tìm thấy. | Không còn GGUF/partial/probe ở các root AppData/fixed drive và candidate hiệu lực. Không sửa byte hardlink, nguồn bàn giao hoặc model tùy chọn. | ST19. |
| ST21 | P0 | Cách ly Piper runtime, payload và cache artifact có thể được dùng lại. | Kiểm cả legacy/global; model/venv/READY/bundle ZIP/manifest/scratch ba artifact không khả dụng; giữ nguyên thư mục trust, dữ liệu dự án và engine khác. | ST19. |
| ST22 | P0 | Kiểm trạng thái máy sau cách ly và khả năng restore. | Có manifest before/after; phiên/account/device được giữ; không mất dữ liệu bảo vệ; ghi fingerprint/sequence/revocation state theo scope. Backup hiện có và bản nguồn không bị ghi đè. | ST20–ST21. |
| ST23 | P0 | Mở đúng desktop bằng Setup/shortcut đã cài và xác minh chưa có model. | EXE/version và WorkingDirectory đúng InstallRoot; Qwen/Piper thiếu, không DISABLED/READY do cờ sai; modal và host gate giữ nền. Kiểm lại các đường chọn sau khi legacy folder không còn. | ST22. |

Quarantine phải ở ngoài mọi đường tìm, kiểm reparse trước cả thư mục nguồn và đích, dùng tên mới không trùng. Với hai thao tác Qwen/Piper, nếu một thao tác lỗi thì ghi trạng thái từng phần và khôi phục theo manifest; không tiếp tục giả định máy đã sạch. Mọi recursive move/delete sau này phải kiểm resolved absolute paths theo quy tắc Windows trong repository.

Việc xóa model cũ không được xóa state để buộc tải lại một hash đã bị revoke. Nếu model chuẩn đang nằm trong known revocations, dừng và xử lý chính sách phát hành; restore model hoặc xóa trust cache không phải cách hợp lệ để vượt chặn.

## 9 Task chạy Setup và nghiệm thu

| Mã | Ưu tiên | Công việc | Điều kiện hoàn tất | Phụ thuộc |
|---|---|---|---|---|
| ST24 | P0 | Cài component bằng UI của bản desktop đã cài. | Có GET catalog và các artifact từ đúng server; không tải Hugging Face/PyPI hoặc dùng nguồn local. Theo dõi tải/verify/install/probe; không chấp nhận resource warning tự động. | ST23. |
| ST25 | P0 | Hoàn tất tải, probe và nghe mẫu trên runtime vừa được tạo. | Hash model/cặp Piper khớp; Qwen runtime/Anh/Trung đạt đúng fingerprint; Piper tạo venv mới, probe kiểm WAV PCM hợp lệ; OCR en/zh và bốn MSVC app-local đạt. Chỉ sau các probe đó READY. Sau READY, tạo một voice job bằng text fixture mới để nghe và lưu WAV bằng chứng. | ST24. |
| ST26 | P0 | Thử ngắt tải và khởi động lại app trong một lượt cài lạnh riêng. | Lập lại trạng thái model thiếu theo allowlist, không xóa trust; hủy/mất kết nối chỉ của app thử. Có part/receipt chưa promote, mở lại gửi Range/If-Range, nhận 206 và hash toàn file đạt. | ST24–ST25, chuẩn bị lượt thử mới. |
| ST27 | P0 | Kiểm mở lại và khả năng dùng model đã cài khi endpoint model không khả dụng. | Không tải lại bytes hợp lệ; vẫn kiểm session/license theo policy, không gọi đây là sản phẩm hoàn toàn offline. READY mất hiệu lực khi fingerprint không khớp. | ST25. |
| ST28 | P1 | Chạy Vietsub fixture riêng trên bản cài. | OCR → dịch local → giọng Piper → preview/xuất SRT/MP4 và mở lại; đúng cue/revision, WAV và media. Không dùng Cloud, không sửa dự án thật. | ST25. |
| ST29 | P0/P1 | Chạy ma trận lỗi, an toàn dữ liệu và đường dẫn. | Các ca P0 ở mục 10 đạt. Ca mở rộng có kết quả riêng; lỗi đúng lớp, không READY giả hoặc phá dữ liệu. Fault injection dùng kho/cache/bản app thử riêng. | ST25–ST27. |
| ST30 | P0 | Diễn tập khôi phục máy và khôi phục cấu hình server thử. | Đóng đúng app/worker thử và nhả lease; cách ly bộ mới đang chiếm path, không ghi đè. Restore từng mục về absolute path cũ theo manifest khi còn hợp lệ; giữ trust/revokes mới nhất, không restore nguyên CacheRoot/trust cũ. Re-probe khi worker/fingerprint thay đổi. Khôi phục server theo backup/scope đã chốt, giữ bảo vệ chống sequence lùi. | ST22, ST29. |
| ST31 | P0 | Lập biên bản nghiệm thu và bàn giao. | Đủ bằng chứng mục 11; báo riêng test file, server đích, runtime, UI và giới hạn máy. Chưa đạt thì không kết luận đủ điều kiện gửi khách. | ST25–ST30. |
| ST32 | P1 | Chốt retention và xử lý backup/quarantine của bài thử. | Sau thời hạn giữ đã chốt, chỉ xóa đúng mục tiêu được định danh; giữ nguồn bàn giao/license/bằng chứng cần thiết. Có thể tiếp tục giữ backup; ghi rõ việc đó thay vì báo đã xóa vật lý. | ST31. |

Piper tự xóa request/WAV của probe sau khi kiểm, nên không lấy file probe đã bị xóa làm bằng chứng nghe. Mẫu cần nghe ở ST25 phải là WAV vừa sinh từ voice job fixture sau READY; clip preview đi kèm package không chứng minh runtime vừa cài đã tổng hợp được giọng. ST28 mở rộng sang toàn bộ workflow, không thay yêu cầu nghe mẫu P0 này.

## 10 Ma trận nghiệm thu và ca lỗi

| Ca | Mức | Kết quả cần chứng minh |
|---|---|---|
| Cài lần đầu qua Setup thật | P0 | Không payload trong package hoặc local lookup; ba artifact tải từ đúng server, cuối cùng probe mới đạt. |
| Ngắt giữa GGUF, đóng/mở app | P0 | Part/receipt được giữ, request mới có offset/If-Range, server trả 206; hash cuối đúng. Không tắt mạng toàn máy để ảnh hưởng việc khác. |
| Server/proxy trả 200 khi resume, ETag thay đổi | P0 | Restart an toàn; không append hai representation, không giữ file cuối sai. |
| Catalog/key/signature sai hoặc hết hạn | P0 | Bị từ chối tại server hoặc client, không tải artifact chưa tin cậy, không fallback upstream. Ghi đúng lớp chặn. |
| File store hoặc file client sai hash/truncated | P0 | Store sai bị server từ chối; client sai không promote/READY. Muốn thử client rejection dùng endpoint/fake transport/cache thử, không phá kho live. |
| Model chưa READY | P0 | UI và C# cùng chặn nghiệp vụ; không chỉ kiểm màu card. |
| Cài lại/mở lại cùng build | P0 | Dùng file hợp lệ và bằng chứng đúng máy; không mất workspace/account/device hoặc tải model lại không cần thiết. |
| Không có quyền/license/context đổi | P0 | Không mở quyền do response cũ; kiểm bằng test account phù hợp, không thu hồi license thật của người đang dùng để tạo ca lỗi. |
| Đĩa/quota thiếu hoặc file lock | P0 | Dừng rõ, không promote dở hoặc xóa dữ liệu ngoài phạm vi; tạo tình huống bằng quota/volume thử, không cố làm đầy ổ C. |
| Piper đúng đường runtime Setup chọn | P0 | Cài/venv/WAV đạt tại đó. Failure đường này giữ Failed; không thay bằng kết quả CLI workspace ngắn. |
| Hai client và rate limit qua proxy | P0 | Retry/503/429 có khả năng phục hồi trong giới hạn; log không chứa secret và không nhận diện sai toàn bộ khách thành proxy chưa được cấu hình. |
| Prefix dài, dấu/khoảng trắng và user thường | P1 | Kiểm cả runtime/cache thực, không chỉ tên thư mục EXE. Chưa đạt không quảng bá hỗ trợ đường dẫn dài. |
| Hai instance cùng cài/kill ở các pha promote | P1 | Lock và retry đúng, không có runtime nửa chừng hoặc orphan process; chỉ kill tiến trình thử do bài test tạo. |
| Sequence replay/revoke/khôi phục key | P1 | Test trong harness/cache/profile hoặc máy ảo cô lập. Không tiêm revoke sticky vào state của máy đang dùng và không restore state cũ để “gỡ” revoke. |
| Ba cold start hoặc soak/hardware mở rộng | P1 | Ghi số lượt thật, profile và resource consent; không cộng bài model bị skip thành Passed. |

## 11 Bằng chứng cần lưu

Lưu hồ sơ trong thư mục nghiệm thu ngoài repository, có quyền phù hợp. Không chứa secret, token, plaintext connection string, prompt/subtitle người dùng hoặc log toàn bộ request đăng nhập. Chỉ log route model cần thiết; không tắt TLS validation ở bản Setup khách.

Hồ sơ tối thiểu:

1. Phiếu môi trường, source snapshot, version/build/channel, hash Setup/package/binary, public-root fingerprint và pin của ba artifact.
2. Candidate inventory/checksum, catalog nguyên byte, hash envelope/payload, key ID, sequence, expiry và compatibility.
3. Kết quả copy/hash tại server, domain/TLS/proxy, HEAD/Range/full-download, latency/timeout và concurrency.
4. Manifest trước/sau dọn, alias/lookup của phiên thử, quarantine map và trạng thái dữ liệu cần bảo vệ; config chỉ ghi metadata an toàn.
5. Bằng chứng UI/host trước cài và từng pha tải/verify/install/probe/READY; đường EXE/WorkingDirectory chỉ giữ trong hồ sơ local được bảo vệ.
6. Access log ở đúng server/proxy, client receipt trong lúc bị ngắt và hash file cuối. Receipt được xóa sau promote là hành vi đúng; cần ghi bằng chứng trước khi nó mất.
7. Báo cáo Qwen/Piper/OCR, version/fingerprint, máy/OS/CPU/RAM, số lượt, WAV mẫu được nghe và kết quả workflow fixture khi chạy.
8. Biên bản restore, phần Failed/Not run, tồn tại backup và quyết định giữ/cập nhật/xóa đúng phạm vi.

Không dùng tổng Content-Length của các request để nói số byte đã nhận: request bị hủy có thể khai toàn bộ độ dài nhưng chỉ truyền một phần. Đối chiếu byte thực ghi trong part, offset/Content-Range, byte gửi thực nếu proxy có số liệu, và hash file hoàn chỉnh. Retry có thể làm tổng lưu lượng lớn hơn kích thước model.

Chỉ hash đúng hoặc GET catalog thành công không chứng minh tải từ server. Cần đồng thời có preflight không nguồn reuse, package Server không payload, request artifact đúng domain và file đích mới đúng hash.

## 12 Điều kiện kết luận

| Mức | Chỉ được ghi đạt khi |
|---|---|
| Bộ file hợp lệ | Đúng pin/size/license records sau copy, có inventory mới. |
| Server thử đã xác minh | Hash trên đích và tải lại qua đúng domain/proxy đạt; không thay bằng listener loopback. |
| Cài model từ server trên máy hiện tại | Setup cuối và lookup sạch được chứng minh, artifact thực đến từ server và runtime probe mới đạt trên máy này. |
| Windows sạch | Có lần thử riêng trên Windows/profile/prerequisite sạch được mô tả rõ. |
| Phát hành production | Đủ ký/license/qualification, đích và nhóm người dùng được chỉ định, rollout/recovery được quyết định riêng. |

Kho/catalog đã kích hoạt ở channel thử phải ghi rõ phạm vi đó; không đồng nhất với Published production. Các trường false trong candidate/biên bản cũ không được sửa để thay bằng chứng lần thử mới.

Kế hoạch thực thi theo thứ tự: **chốt đầu vào → server thật đạt → Setup Server đạt → kiểm disk/path/backup → dọn model có phục hồi → Setup tải và probe → ca lỗi/restore → nghiệm thu**. Không đảo thứ tự để xóa model trước rồi mới tìm cách dựng kho tải.

## 13 Source và tài liệu đối chiếu

- [Kế hoạch kho model tổng thể](PLAN_TASK_KHO_MODEL_SERVER_VA_CAI_DAT_AN_TOAN.md), [biên bản triển khai](TRIEN_KHAI_KHO_MODEL_SERVER_VA_CAI_DAT_AN_TOAN.md), [ADR tin cậy](ADR_MODEL_DISTRIBUTION_TRUST.md).
- [Publisher và vận hành kho](TOOL-MODEL-PUBLISHER/README.md), [options server](TOOL-SERVER/Configuration/ModelDistributionOptions.cs), [controller](TOOL-SERVER/Controllers/ModelDistributionController.cs), [store](TOOL-SERVER/ModelDistribution/ModelDistributionStore.cs).
- [Pipeline publish](scripts/Publish-DesktopRelease.ps1), [validator Server mode](scripts/Test-ModelDistributionPublishSettings.ps1), [kiểm package](scripts/Test-DesktopSetupPublish.ps1), [launcher Setup](TOOL-SETUP/LauncherInstallerService.cs).
- [Qwen lookup và reuse](TOOL-LOCAL/Vietsub/Translation/VietsubTranslationRuntime.cs), [Piper store](TOOL-LOCAL/Vietsub/Voice/VietsubVoiceComponentStore.cs), [Piper offline install](TOOL-LOCAL/Vietsub/Voice/VietsubVoiceComponentStore.Offline.cs), [bundle](TOOL-LOCAL/Vietsub/Voice/PiperOfflineBundle.cs).
- [Client trust/cache](TOOL-LOCAL/ModelDistribution/DesktopModelDistributionClient.cs), [downloader](TOOL-LOCAL/ModelDistribution/ResumableModelDownloader.cs), [verifier](TOOL-DISTRIBUTION/ModelDistribution/ModelCatalogTrust.cs).
- [Paths và journal](TOOL-LOCAL/SystemSetup/SystemSetupPaths.cs), [merge config](TOOL-LOCAL/Configuration/DesktopOptions.cs), [feature composition](TOOL-LOCAL/SystemSetup/DesktopComponentComposition.cs), [authorization](TOOL-LOCAL/SystemSetup/SystemSetupAuthorizer.cs).
- [Vận hành phát hành](VAN_HANH_VA_PHAT_HANH.md), [kiểm thử nghiệm thu](KIEM_THU_VA_NGHIEM_THU.md), [nghiệp vụ](NGHIEP_VU_HE_THONG_VIDEOMAKER.md).
