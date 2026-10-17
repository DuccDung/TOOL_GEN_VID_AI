# Plan task kiểm thử ổn định Setup/model — UI, UX và logic

Ngày lập: **2026-10-16**. Rà soát nhánh `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b` cùng working tree chưa commit.

**Đây là kế hoạch được lập ngày 2026-10-16; lượt lập tài liệu chưa thay source hoặc chạy test.** Tiến độ thực hiện từ 2026-10-17, bằng chứng và giới hạn của từng nhóm task nằm trong [biên bản kiểm thử ổn định](TRIEN_KHAI_TEST_ON_DINH_SETUP_MODEL_UI_UX_VA_LOGIC.md). Không thay số liệu nghiệm thu lịch sử bằng kết quả mới khi chưa chạy thực tế.

## 1. Mục tiêu và phạm vi

Kiểm tra hành trình: mở Setup → lấy đúng bản ứng dụng → kiểm prerequisite/dung lượng → tải ZIP model từ VPS → xác minh/import → chuẩn bị runtime → probe → commit → mở app → đăng nhập/license → OCR Anh/Trung → Qwen → Piper → xuất SRT/MP4.

Phạm vi chính là chế độ `StaticArchive`, Qwen 4B CPU, Piper offline và OCR/media đi cùng desktop. Kiểm thêm hồi quy `Bundled`/`Server`; Kokoro, Qwen 1.7B, CUDA/GPU nằm ngoài nghiệm thu bộ chính. Không mở rộng thành kiểm toàn bộ Admin, thanh toán hoặc các provider AI có phí.

Kết quả cần trả lời được: người dùng có cài thành công, hiểu đúng trạng thái, tự thử lại khi lỗi, không mất cấu hình/dữ liệu, không nhận READY giả, không để tiến trình con chạy sót và không phải tải lại model hợp lệ hay không.

Đọc cùng [biên bản triển khai](TRIEN_KHAI_SETUP_CAI_MODEL_TU_VPS.md), [kế hoạch triển khai SC01–SC36](PLAN_TASK_SETUP_CAI_UNG_DUNG_VA_MODEL_TU_VPS.md), [quy tắc kiểm thử](KIEM_THU_VA_NGHIEM_THU.md) và [quy trình phát hành](VAN_HANH_VA_PHAT_HANH.md).

## 2. Baseline và những điều chưa được chứng minh

Biên bản triển khai trước ghi Release build 0 lỗi/2 cảnh báo Pkcs; full .NET 2.150 Passed/0 Failed/18 Skipped; frontend 322 Passed/0 Failed/0 Skipped. Đây là bằng chứng của source/môi trường/lượt chạy trước, **không phải kết quả chạy lại của kế hoạch này**. Fixture HTTP/ZIP nhỏ và jsdom chưa chứng minh tải/cài ZIP thật hoặc giao diện Windows/WebView2 thật.

Đầu vào model đã pin trong [ApprovedStaticModelBundle](TOOL-DISTRIBUTION/StaticModels/ApprovedStaticModelBundle.cs):

| Thuộc tính | Giá trị dùng để đối chiếu |
|---|---|
| Bundle | `models-20261016-03` |
| URL | `https://103.179.190.182:8443/models/VideoMaker-Models-20261016-082700.zip` |
| ZIP | 2.847.980.994 byte, khoảng 2,85 GB theo hệ thập phân |
| SHA-256 ZIP | `e9939863910af178ec6be6e99f7fcc16948b6a8d916bd5b32bc0a50d92d3f471` |
| Worker/protocol | 1.2.1 / IPC 3; phải đọc lại pin khi chốt candidate |
| Bộ kết quả bootstrap đầy đủ | Chính xác `media`, `ocr`, `qwen`, `piper`, `webview2`; tất cả READY. Fingerprint/runtime evidence cần kiểm riêng theo tiêu chí nghiệm thu |

Chưa có bằng chứng cho bộ Setup StaticArchive phát hành mới, cài đầy đủ trên Windows sạch, tải model bằng chính Setup, native probe trên ma trận phần cứng hoặc workflow sau cài. Những ô này cần nghiệm thu riêng.

## 3. Các vùng rủi ro phải ưu tiên

Các nhận xét dưới đây xuất phát từ đọc source, chưa phải lỗi đã tái hiện bằng runtime. Nếu kiểm thử xác nhận sai hành vi thì lập bug và task sửa riêng; không sửa trong lượt lên kế hoạch này.

| Mức | Vùng cần kiểm | Căn cứ và câu hỏi cần trả lời |
|---|---|---|
| P0 | READY/complete giả | Result đủ năm component chưa đủ để suy ra giao dịch app đã commit. Phải đối chiếu pending, receipt component, receipt app, journal và startup gate sau crash |
| P0 | Snapshot UI thiếu/rỗng | Helper React dùng `every()` trên component bắt buộc; kiểm snapshot rỗng, thiếu component và khoảng chờ GET ban đầu. Host phải luôn chặn nghiệp vụ khi chưa đủ điều kiện |
| P0 | Crash ở cửa sổ commit | Xác định trạng thái trước/sau ghi receipt, journal `Committed`, xóa pending; khởi động lại phải phục hồi hoặc yêu cầu kiểm tra lại đúng cách |
| P0 | Process/lease | Job Object, parent chết, hai Setup, app mở cùng lúc, uv/Python/worker bị treo; không phá file đang được sử dụng hoặc còn orphan |
| P0 | File lớn, đĩa và cache | ZIP lớn hơn 2 GiB; app ở D nhưng runtime/cache vẫn ở LocalAppData trên C; dữ liệu tải dở khác với dữ liệu đã kiểm hash |
| P0 | Thu hồi đã biết | Đo thứ tự kiểm trust với cache rỗng. Tối thiểu không được dùng/probe/commit model bị thu hồi; ghi riêng có phát sinh tải lớn trước khi báo lỗi hay không |
| P1 | Bố cục Setup | WinForms có FixedDialog 500×440, status cao cố định; phải đo chữ lỗi dài, nút cuối và DPI cao |
| P1 | Tiến độ gây hiểu nhầm | Bootstrap đang chuyển progress lên khoảng 85%, tải model nằm khoảng 30–65%; kiểm lùi phần trăm và thông điệp qua giai đoạn, đặc biệt trước khi tải model lớn |
| P1 | Mất phản hồi bridge | Polling, event đến trễ, mất ACK và đổi context có thể khác jsdom; kiểm UI không chờ vô hạn hoặc nhận kết quả của lượt cũ |

## 4. Chuẩn bị môi trường và điều kiện đầu vào

### 4.1. Các điều kiện cần chốt trước E2E

- Chốt version/build/channel, profile khách hợp lệ, URL API phát hành, bộ FFmpeg đã duyệt Release và chính sách ký hiện hành. Dùng tài khoản/license staging có sẵn; không cần thanh toán thật.
- Tạo candidate bằng pipeline StaticArchive. Kiểm compile policy `StaticModelSetupRequired=true`, model pin, binding version/build/hash, OCR/native/worker và WebView2 installer đã duyệt. Bản build mặc định có policy false không thay candidate này.
- Có release staging khớp để Setup tải package desktop. URL ZIP model hoạt động không thay API phát hành ứng dụng.
- Có VM snapshot/profile Windows thử nghiệm mới; không Python/uv/model/Hugging Face cache developer, không READY cũ. Không xóa model ở tài khoản đang dùng để giả lập máy sạch.
- Có ổ thử nghiệm đủ chỗ cho app, ZIP, giải nén, runtime, backup và evidence. Ghi free disk của C/D trước mỗi lượt; không dùng 2,85 GB làm tổng yêu cầu dung lượng.
- Có fixture media ngắn Anh/Trung được phép dùng, WAV/SRT chuẩn và project có cue manual/locked để đối chiếu bảo toàn dữ liệu.
- Chốt danh sách Windows build/hardware sẽ cam kết hỗ trợ. Source kiểm Windows/x64 không phải bằng chứng mọi bản Windows 10/11 đều được hỗ trợ.

Nếu thiếu candidate hoặc môi trường thì vẫn làm rà coverage/logic/UI bằng fixture; ghi **Blocked** ở E2E, không chuyển thành Passed. Không dùng cấu hình phát triển có credential để lách gate publish.

### 4.2. Ma trận máy thử

| Mã | Cấu hình/mục đích | Kết quả phải phân biệt |
|---|---|---|
| ENV01 | Windows 11 x64 build được chọn, user thường, Intel, RAM khoảng 16 GB, SSD | Máy sạch chuẩn; đủ tài nguyên thực tế trước native probe |
| ENV02 | Windows 11 x64 build được chọn, AMD, RAM khoảng 16–32 GB | Khác nhà sản xuất CPU, cùng candidate/model pin |
| ENV03 | Windows 10 x64 build còn thuộc phạm vi hỗ trợ đã chốt | Nghiệm thu riêng nếu dự án cam kết hỗ trợ; không tự suy từ target framework |
| ENV04 | RAM/available memory/commit thấp, pagefile hạn chế, telemetry tài nguyên không đọc được | Cảnh báo rõ và cần xác nhận; không tự xác nhận hoặc báo model đạt |
| ENV05 | Thiếu WebView2; Windows N thiếu media prerequisite; native thiếu/hỏng | Cài đúng prerequisite được duyệt hoặc dừng với hướng dẫn đúng nguyên nhân |
| ENV06 | User/path có tiếng Việt, dấu cách; app ở D; đường dài sát giới hạn hỗ trợ đã chốt | Tạo Piper venv ở đường cuối, không hỏng quoting, đúng volume tính dung lượng |
| ENV07 | Hai Windows user/profile riêng; bản cũ và StaticArchive; app/Setup chạy đồng thời | Cách ly receipt/cache/lease, không lấy READY của user/máy khác |
| ENV08 | x86 hoặc platform không thuộc bộ win-x64; ổ mạng/removable/reparse | Từ chối đúng phạm vi trước mutation/native; không tuyên bố ARM64/GPU được hỗ trợ từ các ca này |

Các cấu hình RAM trên là phân lớp máy thử, không đặt ra mức RAM cứng mới. Source có ngưỡng cảnh báo Standard 8/4/6 GiB và LowMemory 6/3/4 GiB cho total RAM/free RAM/free commit; Static bootstrap đánh giá LowMemory. Dưới ngưỡng hoặc không đọc được tài nguyên phải đi đúng nhánh xác nhận theo profile. Xác nhận RAM không bỏ qua disk/platform/hash/probe.

CPU có nhánh `avx2`/`avx`/`noavx` trong source; không mặc định bộ chính chỉ hỗ trợ AVX2 hoặc suy ra mọi CPU đều chạy được. Ghi backend/instruction thực và kết quả native của từng máy. Thử thêm nâng quyền cùng user và nâng quyền bằng user khác để xác minh đúng LocalAppData/identity; không chấp nhận cài runtime cho tài khoản khác rồi báo thành công cho người dùng ban đầu.

### 4.3. Ma trận mạng

| Mã | Mô phỏng | Mục đích |
|---|---|---|
| NET01 | Kết nối ổn định không giới hạn nhân tạo | Baseline tốc độ, thời gian và hash ZIP thật |
| NET02 | Giới hạn 5–10 Mbps, latency 150–300 ms | Tải lâu vẫn phản hồi; không áp timeout header cho toàn bộ download |
| NET03 | Mất mạng tại khoảng 10%, 50%, 95%, nối lại | Tải tiếp đúng offset/ETag, không nối dữ liệu khác phiên bản |
| NET04 | Không có byte mới quá timeout; DNS/TLS/8443 bị chặn | Retry có giới hạn, hủy được, thông báo khắc phục rõ |
| NET05 | Server test trả 200/206/416, ETag đổi/yếu/mất, 429/503/404 | Kiểm transport bằng fixture/local server có kiểm soát |

Lỗi HTTP, ZIP độc hại và TLS giả được tạo trên test handler hoặc server cô lập; không thay ZIP, chứng chỉ hay Nginx của VPS đang phục vụ. Không tắt kiểm chứng TLS để làm test tải thành công. Chưa làm load test nhiều khách trên VPS dùng chung; chốt hạn mức và môi trường riêng trước nếu cần kiểm năng lực phân phối.

## 5. Danh sách task triển khai kiểm thử

P0: có thể mất dữ liệu, thực thi sai, READY giả, vượt gate hoặc làm bộ cài không thể phục hồi. P1: chặn hành trình chính, UI không thao tác được, retry/cancel sai hoặc hiệu năng gây treo. P2: vấn đề trình bày phụ không chặn thao tác và có cách xử lý rõ.

| Task | Ưu tiên | Công việc | Phụ thuộc | Điều kiện hoàn thành |
|---|---|---|---|---|
| ST01 | P0 | Chốt phạm vi/candidate và snapshot source dirty | — | Có commit nền, hash file đang đổi và giới hạn hỗ trợ; không chỉ ghi HEAD |
| ST02 | P0 | Lập bảng coverage từ test hiện có và ca còn thiếu | ST01 | Mỗi UI/LG/RC/RT phía dưới gắn automated/manual/missing, không viết lại test chỉ để tăng số lượng |
| ST03 | P0 | Chuẩn bị profile phát hành, FFmpeg, signing policy và API release staging | ST01 | Candidate StaticArchive qua validator, binding và hash; không publish production |
| ST04 | P0 | Chuẩn bị VM sạch, snapshot, disk và tài khoản staging | ST01 | Có biên bản môi trường, tài nguyên, quyền và baseline dữ liệu |
| ST05 | P0 | Chuẩn bị fixtures/fault injection/evidence | ST02, ST04 | Lỗi chỉ tác động root/tiến trình thử nghiệm; ghi được phase, timestamp, byte, exit code |
| ST06 | P1 | Kiểm WinForms layout và tiếng Việt/DPI | ST03, ST04 | UI01–UI03 có ảnh và đo thao tác được |
| ST07 | P1 | Kiểm tiến độ, loading, phản hồi khi thao tác | ST05 | UI04–UI05 đối chiếu được progress thật với stage |
| ST08 | P1 | Kiểm retry/cancel/đóng cửa sổ/chọn phiên bản | ST03, ST05 | UI06–UI08 không tạo nhiều lượt chạy và không báo hoàn tất sai |
| ST09 | P1 | Kiểm keyboard/focus/screen reader/modal WebView2 thật | ST04 | UI09–UI10 có bằng chứng native và WebView2, không chỉ jsdom |
| ST10 | P0 | Kiểm snapshot/event lỗi, initial GET, license overlay, partial repair và host gate | ST02, ST05 | UI11–UI12, UI15, LG14–LG15 chặn đúng cả UI lẫn host |
| ST11 | P1 | Kiểm thông báo dung lượng/tài nguyên/lỗi và khả năng tự khắc phục | ST05 | UI13–UI14; có mã hỗ trợ, không lộ dữ liệu nhạy cảm |
| ST12 | P0 | Kiểm tải/resume/retry/hash với fixture | ST02, ST05 | LG01–LG05 đạt các nhánh transport, không nới pin/timeout |
| ST13 | P0 | Kiểm file lớn thật và cache được tái dùng | ST03–ST05, ST12 | LG06–LG07 có byte/hash/request log của đúng candidate |
| ST14 | P0 | Kiểm archive/inventory/compatibility/path safety | ST02, ST05 | LG08–LG09 không promote payload sai hoặc ghi ra ngoài root |
| ST15 | P0 | Kiểm ước tính đĩa và đầy ổ giữa ghi | ST04, ST05 | LG10–LG11 kiểm riêng C/D/cache/runtime/backup |
| ST16 | P0 | Kiểm quyền ghi/file lock/trust/pin/prerequisite | ST05 | LG12–LG13, LG16–LG17 không vượt validation |
| ST17 | P0 | Kiểm result CLI/policy/config/feature flag | ST02, ST05 | LG18–LG20 giữ compatibility và đủ năm component |
| ST18 | P0 | Kiểm transaction/journal và dữ liệu người dùng | ST05, ST14 | RC01–RC03 chứng minh file managed/không managed và backup đúng |
| ST19 | P0 | Kiểm crash ở từng mốc commit | ST05, ST18 | RC04–RC07 có snapshot trạng thái trước/sau và kết quả khởi động lại |
| ST20 | P0 | Kiểm cây tiến trình, timeout và parent chết | ST04, ST05 | RC08–RC09 không còn worker/uv/Python do lượt thử sở hữu |
| ST21 | P0 | Kiểm hai Setup/app/multiuser và file đang sử dụng | ST04, ST05 | RC10–RC11 không đồng thời mutation, không tắt app người dùng |
| ST22 | P0 | Cài end-to-end từ candidate trên máy sạch | ST03–ST21 | RT01–RT02: app + năm component, không phụ thuộc môi trường developer |
| ST23 | P0 | Kiểm Qwen/Piper/OCR/media thật sau cài | ST22 | RT03–RT04 có output, fingerprint, stream/hash và nghiệm thu nghe/đọc |
| ST24 | P1 | Kiểm login/license/context và workflow hồi quy | ST22, ST23 | RT05–RT06 đúng quyền/revision, không gọi Cloud ngoài ý muốn |
| ST25 | P1 | Chạy stability Qwen qua harness hiện có | ST23 | 3 cold start × 30 iteration, đủ tài nguyên; ghi riêng máy/profile đã chạy |
| ST26 | P1 | Soak ứng dụng/workflow và vòng đời worker | ST23–ST25 | RT07–RT08 có chuỗi số đo trước/sau, không dùng RAM peak làm bằng chứng leak |
| ST27 | P1 | Chạy repair/reinstall/reboot và mạng sau cài | ST22 | RT09–RT10 không tải lại model hợp lệ, pending/receipt đúng user/build |
| ST28 | P1 | Chạy hồi quy Bundled/Server và bản legacy | ST17, ST22 | Mode cũ không bị ép StaticArchive; legacy không tương thích được chặn có hướng dẫn |
| ST29 | P0 | Tổng hợp bug, tái hiện và regression cần bổ sung | ST06–ST28 | Mỗi lỗi có ID, bước, expected/actual, evidence, severity và nguồn build |
| ST30 | P0 | Sửa và kiểm lại trong lượt triển khai được giao riêng | ST29 | Không sửa trong lượt lập kế hoạch; sau sửa chạy đúng checks và retest liên quan |
| ST31 | P0 | Chạy bộ kiểm tra cuối trên cùng candidate/source | ST30 hoặc ST29 nếu không có lỗi | Kết quả Passed/Failed/Skipped/Blocked riêng, hash binary/source khớp |
| ST32 | P0 | Lập biên bản quyết định mức sẵn sàng | ST31 | Kết luận riêng source, tự động, runtime, Setup thật, pilot; chưa tự nâng thành production |

## 6. Ma trận ca UI/UX

Các tiêu chí về thời gian/phản hồi dưới đây là mục tiêu nghiệm thu đề xuất, chưa phải số đo hiện hành. Với ca lỗi dữ liệu/gate, mức ưu tiên thực tế tăng lên P0.

Kiểm riêng WinForms Setup trước login và React Setup trong ứng dụng. Native bootstrap cần năm component; modal React hiện hiển thị media/OCR/Qwen/Piper theo contract, không bắt buộc thêm hàng WebView2 chỉ để bảng UI có năm hàng. Mục tiêu là hai lớp nhất quán về gate và trạng thái, không phải giống hệt cách trình bày.

| ID | Thao tác/điều kiện | Kết quả mong đợi | Bằng chứng |
|---|---|---|---|
| UI01 | Setup ở 1366×768, 1920×1080; DPI 100/125/150/200%; đổi màn hình nếu có | Nút chính/Hủy và thư mục luôn thao tác được; chữ không bị cắt làm mất nghĩa; không yêu cầu resize FixedDialog | Ảnh toàn cửa sổ + kích thước client/work area/DPI |
| UI02 | Thông điệp lỗi nhiều dòng, URL/path dài, thông báo thiếu đĩa và xác nhận RAM | Đọc được nguyên nhân, hành động và trạng thái; không bị label 42 px che phần cần thiết; dialog xuất hiện trên cửa sổ cha | Ảnh từng lỗi + click/keyboard |
| UI03 | Tiếng Việt/username có dấu, font lớn, screen reader/contrast hệ thống nếu hỗ trợ | Nhãn rõ, không lỗi dấu; trạng thái có chữ/icon chứ không chỉ màu; lỗi phân biệt được với cảnh báo | Ảnh + ghi nhận đọc nhãn |
| UI04 | Theo dõi từ tải app đến prerequisite, tải model, hash, import, probe, commit | Biết đang làm gì; không hiểu 85% ở preflight là gần cài xong; phần trăm giảm phải được giải thích theo giai đoạn; 100% hoàn tất chỉ sau commit | Video + trace stage/percent theo thời gian |
| UI05 | Hash/giải nén/probe lâu và download không có byte mới | UI vẫn repaint; có thông điệp xử lý; không bịa tốc độ/ETA; không dùng im lặng để biểu thị thành công; thao tác Hủy phản hồi được | Video + timestamp event/progress |
| UI06 | Nhấp Cài/Thử lại nhanh 10 lần; thay version/folder/checkbox khi busy | Tối đa một operation; input phù hợp bị khóa; không đổi target giữa lượt; lỗi/hủy trả về trạng thái có thể thử lại | Operation ID + số process/request |
| UI07 | Hủy bằng nút và đóng X ở từng phase; thử lại sau đó | Có phản hồi “đang hủy”; chờ cleanup/restore đúng; không báo complete, không tạo shortcut/chạy app khi probe chưa đạt | Video + cache/journal/process snapshot |
| UI08 | Bỏ chọn “Chạy VideoMaker sau khi cài”; kiểm phiên bản rỗng/API lỗi/sai binding | Vẫn cài/probe đủ model; không chạy app ngoài ý muốn; nút thử lại rõ; không chọn package khác binding để cài tạm | Screenshot + result/launch trace |
| UI09 | Chỉ dùng Tab/Shift+Tab/Enter/Space, focus đang ở control bị disable/ẩn | Thứ tự hợp lý, focus nhìn thấy; mọi hành động cần thiết thao tác được; không bị kẹt trong WinForms | Video bàn phím ở DPI chuẩn/cao |
| UI10 | Startup modal: Tab vòng, Escape, click nền, programmatic focus, cuộn bảng/footer | Focus ở modal, nền không tương tác; Escape không vượt gate; Hủy/Thoát hoạt động đúng nhãn; footer tới được ở viewport hẹp và zoom lớn | WebView2 thật + host reject log |
| UI11 | GET đầu chậm/mất, snapshot null/rỗng/thiếu/lỗi, bridge không ACK, snapshot cũ đến trễ | Không hiện READY/đóng modal từ dữ liệu không đủ; có cách đối soát/thử lại; không spinner vô hạn; host tiếp tục chặn nghiệp vụ | Event timeline + command bị từ chối |
| UI12 | License/payment overlay cùng lúc Setup; đổi user/org/context; sign-out lúc busy | Overlay đúng thứ tự, không mắc kẹt focus; state cũ không áp sang user/org mới; command không vượt license hoặc startup gate | Video + context/operation timeline |
| UI13 | C thấp đĩa, D còn nhiều; RAM/commit thấp; xác nhận mặc định No rồi Yes | Nêu đúng ổ/thiếu bao nhiêu; chỉ tiếp tục RAM sau xác nhận đúng profile; không hứa model sẽ chạy chắc chắn | Ảnh cảnh báo + decision/profile metadata |
| UI14 | Người thử gặp lỗi mạng, file sai hash, runtime crash; đọc lỗi và thử lại không có hướng dẫn miệng | Nhận biết bước lỗi và bước cần làm; có mã hỗ trợ; không lộ token/connection string/prompt. Native path cần thiết chỉ ở UI native, không đưa vào DOM/console | Quan sát thao tác + log đã làm sạch |
| UI15 | OCR cần sửa ứng dụng đồng thời Piper chưa cài; cài Piper riêng; repair lỗi rồi kiểm lại; rời/quay lại trang | Piper không làm gate mở khi OCR chưa đạt; trạng thái lỗi/busy được giải phóng đúng; không tự tạo operation/download/listener/poller lặp sau remount | Video + component/operation timeline + request count |

Bổ sung đánh giá UX với 2–3 người chưa biết luồng: mỗi người làm một lượt cài chuẩn và một lượt lỗi mạng. Ghi nơi dừng, nhầm nút hoặc tưởng đã cài xong; không quy đổi mẫu nhỏ này thành tỷ lệ ổn định thống kê cho mọi người dùng.

## 7. Ma trận logic tải/cài/validation

| ID | Tình huống | Expected chính |
|---|---|---|
| LG01 | Download bị ngắt, có strong ETag, mở Setup mới | Gửi Range/If-Range đúng prefix, kiểm lại full SHA-256 trước promote |
| LG02 | Server bỏ Range trả 200; trả 416; part bằng/khác kích thước thật | Reset/recheck có giới hạn đúng nhánh; không nối full body vào prefix, không retry vô hạn |
| LG03 | ETag yếu/mất/đổi; Content-Range sai offset/total; receipt khác URL/hash/bundle | Không tái dùng prefix không đáng tin; reset hoặc từ chối đúng lỗi; không báo đủ |
| LG04 | 429/5xx với Retry-After giây/ngày hợp lệ hoặc cực lớn; 404; mạng treo | Retry/backoff có giới hạn và hủy được; lỗi vĩnh viễn không lặp tải không cần thiết; timeout inactivity không thành giới hạn tổng download |
| LG05 | TLS không hợp lệ, redirect, origin khác, MIME HTML, encoding bất ngờ, body dài/ngắn/sai hash | Không thực thi/import nội dung lỗi; không đổi nguồn/upstream hoặc bỏ TLS/pin |
| LG06 | ZIP thật 2.847.980.994 byte, ngắt trước/sau mốc 2 GiB | Counter/offset/progress không overflow; file đích đúng size/full hash; không giữ toàn ZIP trong RAM |
| LG07 | Cache archive đúng; payload đúng không còn archive; cache/part/receipt sửa 1 byte | Chỉ dùng lại cache đã xác minh; payload đúng không tải lại; phần hỏng được báo hoặc khôi phục từ nguồn đã kiểm; không bỏ qua kiểm hash |
| LG08 | ZIP traversal/absolute path/ADS/Windows alias/reparse/symlink/case duplicate/zip bomb | Không ghi ngoài root, không materialize payload bán phần thành hợp lệ; kiểm giới hạn tài nguyên |
| LG09 | Inventory/checksums thiếu/sai; artifact thừa/thiếu; worker/IPC/desktop không hợp compatibility | Từ chối trước runtime; không tự đổi pin/version; license/provenance/attribution còn nguyên |
| LG10 | App/cache/runtime cùng ổ hoặc khác C/D; archive/payload/part đã kiểm và chưa kiểm | Tính đúng theo từng volume; chỉ trừ phần được xác minh; ước tính read-only; chỉ đổi app sang D không đổi model root |
| LG11 | Đầy ổ tại download, giải nén, copy Qwen, tạo venv, ghi receipt hoặc backup | Không promote/trả READY; giữ phần hợp lệ có thể tái dùng; nêu đúng lỗi; recovery không đè dữ liệu ngoài phạm vi |
| LG12 | Không có quyền ghi, file đang lock, antivirus giữ file, target ổ mạng/mapped/removable/reparse | Từ chối hoặc báo retry đúng lúc, không đi vòng root/ACL, không tắt antivirus hoặc dừng ứng dụng người dùng |
| LG13 | Known revoked hợp repo/channel/platform; trust cache hỏng; revoked khi payload thiếu | Không reuse/probe/commit model đã biết thu hồi; trust hỏng không coi như rỗng; mã lỗi Piper không bị đổi thành lỗi runtime chung. Đo riêng việc chặn trước large GET |
| LG14 | Gửi lệnh nghiệp vụ trực tiếp qua bridge khi UI chưa nhận trạng thái hoặc đang Setup | Host từ chối, không chỉ dựa vào modal; không tạo job/ghi dữ liệu nghiệp vụ ngoài ý muốn |
| LG15 | Event sai operation/context/revision/sequence; GET cũ; terminal event đến trước Running cũ | Bỏ dữ liệu cũ/sai; không đổi terminal về Running, không hoàn tất lượt hiện tại bằng event lượt trước |
| LG16 | Thiếu WebView2; installer sai hash/size/publisher/thumbprint; installer lỗi/UAC hủy/reboot required | Chỉ chạy file đã duyệt; recheck controller thực sau cài; không READY từ registry/file đơn lẻ; không tự reboot |
| LG17 | Thiếu/hỏng FFmpeg/OCR/native/worker; chỉ có năm OCR nupkg | Dừng prerequisite trước download lớn nếu phát hiện được; không coi nupkg là runtime; OCR Anh/Trung và media phải probe thực |
| LG18 | Bootstrap exit 0 nhưng result thiếu/sai schema/GUID/bundle, component duplicate/thừa/thiếu/false/fingerprint rỗng; exit khác 0 nhưng result success | Từ chối complete; đủ đúng ba component ở prerequisite hoặc năm ở full bootstrap; result cũ không được tái dùng |
| LG19 | User config cũ tắt feature/Mode sai hoặc dùng key khác casing; sửa DOM/model URL/path | Compile policy thắng đúng phạm vi trong bộ nhớ; không sửa config người dùng; không nhận source tùy ý; không fallback HF/cache developer |
| LG20 | StaticArchive so với Bundled/Server; gói desktop lớn hoặc binding version/build/channel/hash lệch | Validator chặn publish/cài sai mode/binding; cap 2 GiB desktop không áp nhầm vào ZIP model riêng; mode cũ giữ policy tương ứng |

Các ca đã có test được chạy lại khi chốt baseline; chỉ bổ sung regression còn thiếu. Dùng fixture nhỏ cho biến thể phá hoại, rồi kiểm riêng mốc >2 GiB với file thật. Không sửa URL production để trỏ tới file giả.

Expected trong ma trận là tiêu chí nghiệm thu, không khẳng định source đã bảo vệ mọi nhánh. Riêng LG18, validator result hiện kiểm số lượng/ID/READY nhưng chưa trực tiếp bắt fingerprint rỗng của từng component. Cần kiểm đường evidence thực và negative result này; nếu hoàn tất mà thiếu evidence bắt buộc thì ghi bug/gap, không sửa kỳ vọng để coi là đạt.

## 8. Ma trận phục hồi, crash và đồng thời

Mỗi ca phải lưu trạng thái file/hash trước khi gây lỗi, chụp trạng thái ngay sau lỗi và chạy lại đúng Setup. Crash/power-off chỉ thực hiện trong VM thử; không kill tiến trình chỉ vì trùng tên mà chưa xác minh PID/path/operation thuộc lượt thử.

| ID | Điểm gây lỗi | Expected sau mở lại |
|---|---|---|
| RC01 | Ngắt lúc backup/journal intent/app copy mới copy một phần | Khôi phục file managed từ backup đúng hash; giữ cấu hình/workspace/media và file ngoài manifest |
| RC02 | Backup/journal hỏng hoặc missing; target có file user trùng tên managed mới | Không restore một phần trước khi phát hiện backup hỏng; không đè file user hoặc tự xóa bằng chứng lỗi |
| RC03 | Hủy lúc Qwen copy/Piper tạo venv tại root cuối | Không giữ READY sai; runtime cũ/backup chỉ được coi dùng lại khi đã kiểm; không lấy venv máy khác để chữa tạm |
| RC04 | Crash sau pending nhưng trước app Apply hoàn tất | Có dấu cài dở; app không vượt gate; đối soát lượt cũ, phục hồi rồi tạo lượt mới đúng target/pin; không nhận result của operation cũ |
| RC05 | Crash sau component receipt nhưng trước app receipt | Pending còn giữ; không coi receipt component là giao dịch app đã hoàn tất |
| RC06 | Crash sau app receipt nhưng trước journal Committed; sau Committed nhưng trước xóa pending | Hai cửa sổ kiểm riêng; app tiếp tục bị khóa khi pending còn; retry không double-apply hoặc rollback sai bản |
| RC07 | Crash ngay sau xóa pending/trước shortcut; mất/hỏng pending hoặc journal ở các mốc trước đó | Nhận diện đúng committed hay chưa; thiếu state không tự tạo READY giả. Thử độc lập từng file thiếu và tổ hợp pending+journal mất; không xóa marker thủ công để vượt gate |
| RC08 | Kill Setup/parent khi worker/Python/uv hoạt động; child thoát nhưng grandchild giữ pipe | Job của lượt thử được dọn; stdout/stderr drain có giới hạn; không còn tiến trình ngầm làm model mutation |
| RC09 | Worker/native crash/OOM/timeout; mất handshake Job; result write lỗi | App/Setup sống hoặc dừng có chẩn đoán rõ, không READY; cache/journal giữ; hủy không bị báo nhầm thành crash |
| RC10 | Hai Setup đồng thời cùng target/cache; mở app trong lúc chuẩn bị và lúc final commit | Khóa chặn mutation đồng thời trước outbound cần tránh; app không lấy shared lease để vượt pending; không deadlock vô hạn |
| RC11 | App legacy đang chạy; mở legacy ở thư mục khác giữa phiên; hai Windows user | Không dừng app cũ tự động; báo giới hạn legacy đúng. User khác không dùng receipt/fingerprint sai. Race legacy chưa được chứng minh phải ghi giới hạn, không đánh dấu đã giải quyết |

Rollback hiện tại chủ yếu bảo vệ file ứng dụng. Model global/pending có thể vẫn yêu cầu verify lại. **Expected không phải “mọi runtime/bản cũ sử dụng ngay sau rollback”.** Điều cần đạt là dữ liệu an toàn, state trung thực và đường phục hồi có thể thực hiện.

## 9. Model thật, workflow và chạy lâu

| ID | Bài thử | Tiêu chí |
|---|---|---|
| RT01 | Cài chuẩn trên VM/profile sạch từ Setup đã bind | Tải đúng API app và đúng ZIP VPS, không nhận model/runtime developer; năm component probe đạt trước complete |
| RT02 | Cài mạng chậm/ngắt và thử lại bằng tiến trình Setup mới | Dùng lại byte/cache hợp lệ; xác minh hash cuối; người dùng không phải tự sửa marker/config |
| RT03 | Qwen dịch fixture Anh/Trung, unicode, câu ngắn/dài; Piper tổng hợp tiếng Việt | Qwen worker/profile/pin đúng, output hợp cấu trúc; Piper WAV có RIFF/PCM/rate/stream/thời lượng/hash đúng và nghe hiểu được |
| RT04 | OCR fixture Anh/Trung, FFprobe/FFmpeg xử lý media; render mẫu | OCR đọc được fixture; không nhầm probe file tồn tại; video/audio đầu ra mở được và đúng stream/duration theo fixture |
| RT05 | Đăng nhập/license, Local cá nhân và organization staging; hết hạn/thu hồi/license thiếu | Chuẩn bị component trước login không cấp quyền nghiệp vụ; workflow sau login đúng scope; không phát sinh request Cloud có phí |
| RT06 | OCR → sửa/lock cue → Qwen → Piper → nghe → xuất SRT/MP4; đổi revision khi job chạy | Không ghi đè cue manual/locked, không áp kết quả revision/context cũ; SRT atomic; render dùng asset/hash hiện hành |
| RT07 | Qwen CPU stability qua harness: 3 cold start × 30 iteration | Từng lượt chạy thật và hoàn tất; lưu native/profile/exit/memory. Dùng cửa sổ sau warm-up và ngưỡng tăng private memory hiện có 256 MiB; không giảm iteration hoặc nới gate để pass |
| RT08 | Phiên desktop liên tục 4 giờ, tối thiểu 20 vòng fixture OCR/dịch/giọng; xen playback, hủy/retry và đóng/mở project | Không crash/hang/asset sai; số process/handle/private bytes/đĩa theo phase không tăng vô hạn; đo riêng khi worker còn sống và sau đã thoát |
| RT09 | App cold start 10 lần, reboot VM, chạy lại Setup repair/reinstall | Không tải model lớn lại khi payload hợp lệ; trạng thái đúng sau reboot; cache không tích lũy bản sao ngoài thiết kế |
| RT10 | Sau cài, chặn Internet trên môi trường thử có phiên/license phù hợp | Phân biệt dependency/model offline với quyền đăng nhập/license cần mạng; Piper không tải pip/wheel, Qwen không tìm HF; không tuyên bố toàn app dùng offline vô hạn |

Chạy model tuần tự, không cùng build/OCR/FFmpeg nặng. Dùng [Test-QwenStability.ps1](scripts/Test-QwenStability.ps1) và các harness offline sẵn có khi phù hợp; harness trên publish/workspace riêng không thay RT01/RT03 trên runtime do Setup cài. Thư mục output stability phải mới. Chỉ dùng `AcceptResourceWarning` khi người vận hành đã chấp thuận cảnh báo cụ thể; không bật mặc định trong script kiểm thử.

Một fixture dịch thành công không chứng minh chất lượng dịch toàn bộ nội dung. Lưu bộ đối chiếu chất lượng riêng với dữ liệu thử không nhạy cảm; log thường chỉ có metadata/hash, không chứa transcript/prompt.

Với Piper, bổ sung tối thiểu 30 lượt sinh/preview/hủy xen kẽ trong phiên runtime; kiểm WAV đã hoàn tất và WAV của lượt bị hủy riêng. Với repair/reinstall, thực hiện tối thiểu 10 vòng có cache, theo dõi byte tải và dung lượng sau từng vòng; dùng cùng bản trước, rồi mới kiểm nâng cấp candidate tương thích.

## 10. Thứ tự chạy và số vòng

| Vòng | Nội dung | Số lượt tối thiểu đề xuất | Gate chuyển vòng |
|---|---|---:|---|
| V0 | ST01–ST05: baseline, coverage, candidate, VM, instrumentation | 1 bộ chuẩn | Đúng source/candidate, môi trường và phạm vi lỗi cô lập |
| V1 | Logic/transaction/bridge fixture, test hiện có và regression thiếu | 1 lượt đầy đủ; lặp 20 lần nhóm P0 về race/crash bằng fixture | 0 lỗi P0; không skip regression logic vì môi trường |
| V2 | UI WinForms + React/WebView2, keyboard/DPI/error UX | Mỗi tổ hợp DPI/màn hình chính 1 lượt; lặp 10 chuỗi click/cancel/retry | Không có nút chính bị che hoặc thao tác vượt gate |
| V3 | Setup thật từ cache rỗng | 3 lượt độc lập tối thiểu: chuẩn Intel, AMD, mạng ngắt; thêm từng OS/hardware cam kết | Mỗi lượt đúng hash, đủ probe, dữ liệu và recovery đạt |
| V4 | Crash/đầy đĩa/job/đồng thời trên VM | Mỗi checkpoint RC04–RC09 ít nhất 2 lần; các nhánh khác ít nhất 1 | Không READY giả/mất dữ liệu/orphan; retry có thể hoàn tất |
| V5 | Native và workflow sau cài | Qwen 3×30; app 4 giờ/20 vòng; 10 cold start; 1 reboot | Đủ bằng chứng runtime/workflow cho từng máy được nghiệm thu |
| V6 | Retest lỗi và checks cuối | 1 lượt trên candidate cuối; lặp nhánh từng lỗi theo tái hiện | Hash nguồn/binary khớp report, không còn P0/P1 |

Không lặp tải ZIP 2,85 GB 20 lần để thay test race. V1 dùng fixture; V3 mới chứng minh tải/cài thật. Candidate thay đổi thì xác định ma trận cần chạy lại; test binary cũ không xác nhận source mới.

Mục tiêu phản hồi UI đề xuất: nhấp Cài/Hủy thấy thay đổi trạng thái trong 1 giây trên máy chuẩn; không có khoảng UI mất đáp ứng quá 5 giây chưa giải thích. Đây không phải giới hạn hoàn tất rollback hay nạp Qwen. Phải đo thực tế trước khi chốt SLA.

Timeout source cần đối chiếu khi chạy: headers/inactivity download khoảng 60 giây; prerequisite bootstrap 5 phút; full bootstrap 45 phút; handshake khoảng 15 giây; pipe drain khoảng 10 giây. Chúng giới hạn các phase riêng, **không phải toàn bộ hành trình tải model + cài đặt**. Tải chậm có byte đều không được hủy vì chạm 60 giây tổng.

Ghi thời gian tải/verify/import/probe/cold start và p50/p95 workflow theo từng cấu hình/mạng. Chốt ngưỡng hiệu năng sau baseline đầu tiên; không áp một thời gian cài đặt chung cho mọi CPU. Leak phải đánh giá chuỗi số đo sau warm-up, tách cache có chủ đích khỏi tài nguyên không được giải phóng; ngưỡng Qwen không áp thay cho app/Piper.

## 11. Bằng chứng và cách báo cáo

Mỗi đợt dùng một thư mục evidence mới ngoài Git, ví dụ `D:\vm-tests\setup-stability-<timestamp>\`; kiểm dung lượng trước. Bố trí:

| Mục | Nội dung |
|---|---|
| `baseline/` | Branch/commit nền, hash các file source dirty, candidate/Setup/desktop/model hash, policy, tool versions |
| `environment/` | OS build, CPU/instruction/backend, RAM/free/commit, disk trước/sau, DPI, user type, WebView version; làm sạch định danh |
| `automated/` | Restore/build log, TRX/JUnit/Vitest report, Passed/Failed/Skipped; lý do skip từng nhóm |
| `runs/<case>/<attempt>/` | Bước chạy, expected/actual, phase/operation, request byte/offset, result, exit code, file-state snapshot trước/sau |
| `ui/` | Ảnh/video WinForms và WebView2 thật, DPI/viewport, focus và lỗi dài |
| `runtime/` | Probe/fingerprint/hash/metrics, Qwen stability JSON, mẫu WAV/SRT/MP4 thử nghiệm và nghiệm thu nghe/đọc |
| `defects/` | Bug ID/severity/repro/build, log đã làm sạch, task sửa/regression/retest |
| `summary/` | Matrix case × môi trường, Passed/Failed/Skipped/Blocked/Not run, giới hạn và quyết định sẵn sàng |

Đếm skipped bằng outcome thực trong TRX khi adapter counter sai. Không cộng trùng nhóm focused vào full suite. Lưu cả lượt fail và lượt pass sau sửa. Không log token, connection string, key, prompt/transcript thật hoặc signed URL; không chụp credential trong màn hình.

Restore/build/test .NET và frontend cuối chạy đúng lệnh trong `AGENTS.md`. Frontend Vitest/jsdom, test HTTP giả, WebView2 thật, native probe, Setup thật và rollout được báo thành các lớp riêng. Chỉ lập tài liệu ở lượt này nên không có số build/test mới để ghi.

## 12. Điều kiện kết luận ổn định

1. Không còn P0/P1 mở; lỗi P2 còn lại phải có mô tả ảnh hưởng và người phụ trách, không tự coi là đã được chấp nhận.
2. Không có complete/READY khi thiếu component, pin/hash sai, trust hỏng, pending chưa giải quyết hoặc giao dịch chưa commit hợp lệ.
3. Không mất/ghi đè cấu hình, project, media hoặc file người dùng ngoài manifest; phục hồi và thử lại được chứng minh trên các checkpoint bắt buộc.
4. Các ca bắt buộc của từng OS/hardware đã chốt phải Passed. Skipped/Blocked ở Setup sạch, model thật, crash hoặc workflow bắt buộc đồng nghĩa phạm vi đó **chưa nghiệm thu**, dù test unit đạt.
5. Tải ZIP thật, resume, hash cuối, native probe năm component và workflow sau cài đều có evidence gắn cùng candidate.
6. UI/UX ở ma trận DPI/keyboard đã chốt thao tác được, trạng thái trung thực, lỗi có đường xử lý; không còn spinner chờ vô hạn chưa có cách đối soát.
7. Qwen stability và soak đạt trong tài nguyên/môi trường đã ghi; mọi timeout/crash/leak có tái hiện đều được xử lý hoặc giữ trạng thái chưa đạt.
8. Biên bản cuối tách: **có source → kiểm thử tự động → smoke/runtime máy xác định → Setup Windows sạch → pilot → production**. Harness luôn giữ các cờ qualification theo đúng phạm vi; không sửa cờ để biến báo cáo thành nghiệm thu khách.

Bước đầu khi triển khai kế hoạch này là **ST01–ST05**. Cổng ra quan trọng đầu tiên là có candidate StaticArchive đúng cấu hình và môi trường sạch; sau đó mới thực hiện tải/cài và đánh giá ổn định thực tế.
