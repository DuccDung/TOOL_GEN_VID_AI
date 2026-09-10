# Kế hoạch cải thiện biên tập phụ đề Vietsub

Ngày lập: 2026-09-09.

Trạng thái: Đã hoàn thành Task 1–5 ngày 2026-09-09. Kết quả, phạm vi kiểm chứng và ngoại lệ kiểm thử được ghi tại mục 5.

## 1. Kết quả cần đạt

1. Danh sách phụ đề theo câu đang phát mượt hơn, không giật hoặc tranh cuộn với người dùng.
2. Các thanh thông báo trong tool dịch có nút **×** để đóng, bắt đầu từ thanh “Dự án được phục hồi sau lần đóng trước”.
3. Bỏ thanh “Chọn trang này / số câu được chọn / Bỏ qua tạo giọng / Bật tạo giọng” trong danh sách. Giữ thao tác bật/bỏ qua giọng riêng từng câu.

Phạm vi là giao diện Vietsub local. Giữ nguyên dữ liệu phụ đề, lựa chọn tạo giọng đã lưu, cơ chế đồng bộ âm thanh và các kiểm tra nghiệp vụ. Không cần migration, thay đổi provider hoặc chạy sinh giọng thật.

## 2. Hiện trạng source trước khi triển khai

| Vị trí | Hiện trạng | Việc cần xử lý |
|---|---|---|
| `VietsubSubtitleEditor.tsx` — `VietsubCueRow` | Mỗi row gọi `scrollIntoView({ block: 'nearest', behavior: 'smooth' })` khi `active`, `expanded`, `selected` đổi | Tập trung điều khiển cuộn tại danh sách, giới hạn đúng vùng cuộn và tránh lệnh cuộn cạnh tranh |
| `VietsubSubtitleEditor.tsx` — danh sách row | Câu đang chọn được ưu tiên mở; khi không có câu chọn trên trang thì câu đang phát được mở | Kiểm tra tác động của đóng/mở card và thay đổi chiều cao lên vị trí cuộn |
| `VietsubSubtitleEditor.tsx` — key và callback row | Key gồm `cueId` và `trackRevision`; row chưa được memo hóa; một số callback tạo mới khi render | Đo render/remount thực tế trước khi tối ưu; bảo vệ bản nháp và cơ chế lưu |
| `VietsubEditorWorkspace.tsx` | `activePageCue` được tính từ playhead và trang đang nạp; editor nhận ID câu đang phát | Giảm cập nhật danh sách không cần thiết mà vẫn giữ clock video/giọng hiện tại |
| `VietsubEditorWorkspace.tsx` — recovery banner | Thanh phục hồi hiện theo `project.needsRecovery`, chưa có nút đóng | Thêm trạng thái ẩn thông báo theo lần mở project |
| `VietsubPage.tsx`, `VietsubSettingsPanel.tsx`, `VietsubSubtitleEditor.tsx`, `VietsubPreviewPanel.tsx` | Có các thanh lỗi/thông báo khác; một số có nút thử lại | Áp dụng quy tắc đóng thống nhất, giữ hành động khắc phục và trạng thái thực |
| `VietsubSubtitleEditor.tsx` — `vietsub-voice-selection-bar` | Thanh batch và checkbox từng câu dùng chung `voiceSelection`; thông báo lưu giọng nằm trong thanh batch | Gỡ giao diện batch, chuyển thông báo lưu sang vị trí còn hiển thị |

Các điểm trên là quan sát trước triển khai. Kết quả đo render, layout và cuộn trước/sau được ghi tại mục 5.

Các đường dẫn TypeScript trong tài liệu nằm dưới [TOOL-LOCAL/Web/src/features/vietsub](TOOL-LOCAL/Web/src/features/vietsub). CSS nằm tại [styles.css](TOOL-LOCAL/Web/src/styles.css).

## 3. Task triển khai

### Task 1 — Tái hiện và đo trước khi sửa

- [x] Dựng fixture bằng component và CSS production trong WebView2, dùng dữ liệu mẫu, không mở hoặc sửa project đang sử dụng.
- [x] Tái hiện với 7 câu như ảnh, một trang 50 câu và dữ liệu nhiều trang; có câu ngắn nối tiếp, câu dài và khoảng trống giữa câu.
- [x] Kiểm tra phát 1×/2×, pause/replay, tua nhanh, chọn câu từ timeline, kéo thanh cuộn và nhập bản dịch khi video chạy.
- [x] Ghi nhận số lệnh cuộn, đích cuộn, số lần render/remount row và thời gian layout tại ranh giới câu. Kiểm tra cả scroll anchoring của trình duyệt khi card đổi chiều cao.
- [x] Ghi baseline theo cùng kích thước panel, mức zoom và fixture để so sánh sau sửa; không ghi transcript/video riêng của người dùng vào log.

**Đầu ra:** tình huống tái hiện ổn định và nguyên nhân có số đo. Không kết luận tăng hiệu năng chỉ từ việc thêm `memo` hoặc từ test DOM.

### Task 2 — Làm mượt danh sách theo video

- [x] Tập trung quyết định cuộn tại `VietsubSubtitleEditor` hoặc một hook riêng nếu cần; row cung cấp vị trí/ref, không tự phát các lệnh cuộn độc lập.
- [x] Theo dõi thay đổi ID câu cần hiển thị. Playhead thay đổi trong cùng câu không được khởi động lại cuộn.
- [x] Đo vị trí sau khi card đóng/mở đã cập nhật layout. Chỉ cuộn `.vietsub-cue-list` khi phần cần đọc ra ngoài vùng nhìn thấy, có khoảng đệm trên/dưới.
- [x] Với card cao hơn vùng nhìn thấy, ưu tiên đầu card và vùng đang thao tác; không cuộn qua lại để cố chứa toàn bộ card.
- [x] Chỉ giữ một yêu cầu cuộn tới đích mới nhất. Tua nhanh/đổi câu liên tiếp phải hủy đích cũ, không xếp hàng animation và không cuộn cả trang ngoài.
- [x] Khi người dùng wheel, kéo thanh cuộn, touch hoặc dùng phím cuộn trong danh sách, tạm ngưng cuộn tự động. Dự kiến tiếp tục sau 2 giây không tương tác nếu video đang phát và không có trường nhập/menu đang thao tác; kiểm chứng khoảng chờ bằng fixture.
- [x] Khi đang nhập bản dịch hoặc mở menu, giữ câu đang sửa và focus. Bấm chọn một câu từ timeline/danh sách là yêu cầu điều hướng rõ ràng, cần đưa đúng câu vào vùng nhìn thấy sau khi bảo đảm quy trình lưu hiện tại.
- [x] Giữ quy tắc chỉ mở câu đang chọn/đang phát. Kiểm soát thay đổi chiều cao và scroll anchoring theo kết quả đo, không thêm animation chiều cao kéo dài khiến danh sách liên tục dồn lại.
- [x] Tôn trọng `prefers-reduced-motion`: đưa câu vào vùng nhìn thấy bằng cuộn tức thời khi người dùng yêu cầu giảm chuyển động.
- [x] Nếu baseline xác nhận render dư: ổn định callback và memo hóa đúng chỗ. Nếu thay key để tránh remount theo revision, phải phân biệt track/project, đồng bộ dữ liệu mới và giữ đúng dirty/save/error; không đổi key một cách độc lập với cơ chế lưu.
- [x] Giữ phân trang/tìm kiếm/lọc hiện hành. Không tự xóa bộ lọc hoặc tải trang mỗi nhịp phát để đuổi theo playhead; chọn câu từ timeline vẫn dùng đường điều hướng tới đúng trang đang có.

**File dự kiến:** `VietsubSubtitleEditor.tsx`, `VietsubEditorWorkspace.tsx`, `styles.css`; hook riêng nếu phần điều khiển cuộn đủ phức tạp.

**Điều kiện đạt:** cue còn trong vùng đọc không phát sinh cuộn dư; cuộn tay không bị kéo ngược; tua tới đích mới không quay về câu cũ; không mất bản nháp/focus và không thay đổi đồng bộ video/giọng.

### Task 3 — Thêm nút × cho thanh thông báo

- [x] Thêm nút đóng bên phải thanh phục hồi trong `VietsubEditorWorkspace.tsx`, có `type="button"`, tên truy cập “Đóng thông báo”, tooltip và trạng thái focus rõ ràng.
- [x] Đóng thanh phục hồi chỉ ẩn trong lần mở project hiện tại. Đổi project hoặc đóng/mở lại thì đánh giá lại `needsRecovery`; không ghi lại manifest hoặc xóa cờ phục hồi để ẩn UI.
- [x] Rà soát và dùng cùng cách hiển thị cho các banner cùng loại: lỗi chung ở `VietsubPage`, lỗi phát giọng/media, thông báo dịch/giọng ở `VietsubSettingsPanel`, thông báo phụ đề/lưu lựa chọn giọng.
- [x] Phân biệt banner thông báo với badge trạng thái, tiến độ tác vụ, lỗi xác thực ngay tại trường nhập và hộp thoại yêu cầu quyết định. Nút đóng banner không thay thế thao tác hủy job hoặc xác nhận trong hộp thoại.
- [x] Giữ các nút “Thử lại”/“Thử lại giọng” khi banner hiện. Đóng banner không biến lỗi thành thành công, không gỡ khóa tính năng và không dừng/phát lại video.
- [x] Theo dõi lần phát sinh thông báo và context project/job/media. Poll hoặc render lại cùng sự kiện không làm banner vừa đóng bật lại; lỗi/thông báo mới, kể cả nội dung trùng lần trước, vẫn phải hiện. Nếu cần bổ sung định danh thì thực hiện trong state frontend tại nơi nhận sự kiện, không suy đoán chỉ từ chuỗi nội dung.
- [x] Kiểm tra đóng riêng một banner không ẩn banner khác; không lưu tùy chọn ẩn vĩnh viễn bằng localStorage.
- [x] Kiểm tra nút × trên panel hẹp, thông báo nhiều dòng và zoom cao; không đè lên nội dung hoặc nút thử lại.

**File dự kiến:** các component banner nêu trên, `styles.css`; `useVietsubModule.ts`/props liên quan chỉ khi cần định danh sự kiện. Có thể tách component thông báo nhỏ nếu giúp tránh lặp.

**Điều kiện đạt:** thanh ở ảnh 2 đóng được bằng chuột/bàn phím, ẩn đúng vòng đời và thông báo mới vẫn xuất hiện.

### Task 4 — Gỡ thanh chọn hàng loạt trong danh sách

- [x] Xóa toàn bộ `vietsub-voice-selection-bar`: “Chọn trang này”, bộ đếm, hai nút bật/bỏ qua batch.
- [x] Gỡ các checkbox “Chọn câu” chỉ phục vụ thanh batch, cùng state, props và CSS không còn dùng.
- [x] Giữ thông tin “Tạo giọng / Không tạo giọng” của từng câu ở vị trí gọn, độc lập với checkbox đã bỏ.
- [x] Giữ “Bật tạo giọng / Bỏ qua tạo giọng” trong menu **Thao tác** từng câu và menu chuột phải trên timeline.
- [x] Giữ đường gọi lưu một câu với đúng `cueId`, `trackId`, revision, kiểm tra busy và flush bản nháp trước thao tác. API hỗ trợ danh sách ID ở các lớp dưới không cần xóa.
- [x] Chuyển thông báo lưu thành công/thất bại đang nằm trong thanh batch sang vùng thông báo của editor, áp dụng nút × theo Task 3.
- [x] Cập nhật test batch UI cũ thành test giao diện mới: thanh/checkbox không còn, thao tác từng câu vẫn hoạt động và bị khóa đúng lúc.

**File dự kiến:** `VietsubSubtitleEditor.tsx`, `VietsubSubtitleEditor.test.tsx`, `styles.css`.

**Điều kiện đạt:** khối trong ảnh 3 biến mất, không còn checkbox vô tác dụng, vẫn bật/bỏ qua được giọng từng câu mà không xóa phụ đề.

### Task 5 — Kiểm thử, build và nghiệm thu

- [x] Test React cho quyết định cuộn: cùng cue không lặp cuộn, cue ngoài vùng mới cuộn, manual scroll/focus tạm ngưng, đổi đích hủy yêu cầu cũ, reduced motion.
- [x] Test draft/save khi đổi câu, revision, trang, track và project; thêm ca cập nhật lỗi để bảo đảm tối ưu render không làm mất nội dung chưa lưu.
- [x] Test banner: đóng riêng từng thanh, render/poll lại, mở project khác, phát sinh thông báo mới có cùng nội dung; trạng thái job và lỗi thực vẫn giữ nguyên.
- [x] Test menu giọng từng câu: đúng ID/track/revision, busy, lưu lỗi và bật lại; không còn thanh batch hoặc checkbox đi kèm.
- [x] Chạy WebView2 với CSS production để kiểm cuộn và bố cục thực, cả panel hẹp/rộng, zoom 100%/125%/150%/200%. Lưu ảnh và số đo trước/sau; DOM test đơn thuần không chứng minh độ mượt.
- [x] So sánh số lệnh cuộn dư, render/remount và thời gian layout với Task 1 trên cùng fixture. Xác nhận hết hiện tượng giật tái hiện được; không tuyên bố đạt một mức FPS cố định nếu chưa đo.
- [x] Chạy lại regression playback và timeline layout hiện có, bảo đảm không tái phát mất waveform/cắt chân chữ hoặc mất giọng.
- [x] Chạy các lệnh chuẩn dưới đây; ghi riêng Passed/Failed/Skipped của lần chạy mới, không dùng số test của lần sửa trước.
- [x] Build thêm Debug desktop nếu người dùng chạy từ IDE; xác minh bundle frontend được copy vào output thực tế. Không chỉnh tay `dist`/`wwwroot` hoặc dừng ứng dụng người dùng.
- [x] Cập nhật tài liệu kiểm thử/ngữ cảnh theo kết quả thật và đánh dấu các task đã hoàn thành trong file này.

Thứ tự lệnh từ root repo, dùng `npm ci` trước các test WebView2 cần Vite:

```powershell
Push-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
npm test
Pop-Location

dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build

# Khi sử dụng bản Debug trong IDE:
dotnet build TOOL-LOCAL\TOOL-LOCAL.csproj -c Debug --no-restore
```

Quy tắc nghiệm thu theo [KIEM_THU_VA_NGHIEM_THU.md](KIEM_THU_VA_NGHIEM_THU.md). Test model bị bỏ qua không được tính là model đã đạt. Công việc này dùng fixture local, không yêu cầu chạy Piper/Qwen thật hoặc provider có phí.

## 4. Thứ tự và tiêu chí bàn giao

Thực hiện Task 1 → Task 2 → Task 3 → Task 4 → Task 5. Task 3 xác định chỗ hiển thị thông báo trước khi Task 4 gỡ thanh đang chứa thông báo lưu giọng.

- [x] Danh sách theo video mượt hơn với bằng chứng so sánh trước/sau, đồng thời dùng chuột/bàn phím và sửa nội dung ổn định.
- [x] Thanh phục hồi và các banner trong phạm vi có nút ×, đóng đúng vòng đời và không làm sai trạng thái nghiệp vụ.
- [x] Thanh trong ảnh 3 cùng checkbox batch đã bỏ; menu giọng từng câu vẫn dùng được.
- [x] Build, regression và kiểm tra WebView2 đạt; ghi rõ phần chưa kiểm chứng nếu có.

**Ghi chú phạm vi:** thay đổi UI trong kế hoạch được triển khai trên dirty worktree hiện hành. Những thay đổi C#/media/voice và các file khác đã có trước phiên này được giữ nguyên; không quy chúng thành kết quả của task UI.

## 5. Kết quả triển khai và kiểm chứng — 2026-09-09

### Thay đổi đã thực hiện

- `useCueListFollow` tập trung cuộn trong danh sách, giữ đích mới nhất, có đệm vùng đọc, hủy animation trước khi đổi đích và tôn trọng reduced motion. Wheel, phím cuộn, kéo thanh cuộn, touch, nhập liệu và menu tạm ngưng follow; chỉ tiếp tục sau hai giây rảnh khi video đang phát. Card đang thao tác được giữ mở.
- Memo hóa row, ổn định callback, dùng key track/cue và remount workspace theo project ID. Revision mới không xóa draft hoặc focus; lỗi lưu chặn chuyển trang/track/project và thao tác giọng. Field dùng read-only khi busy để giữ focus. Điều hướng timeline chờ flush và bỏ yêu cầu đã cũ; phản hồi page phải khớp request mới nhất.
- Banner phục hồi, lỗi chung, media/giọng, dịch, phụ đề và lưu lựa chọn giọng có nút ×. `noticeEvents` chỉ nằm trong state frontend; đóng banner không sửa manifest, lỗi, job hoặc readiness. Request/attempt mới được phân biệt với poll cùng sự kiện. Nút thử lại được giữ; không thêm nút đóng cho badge, tiến độ, lỗi tại field hoặc dialog cần quyết định.
- Gỡ thanh batch, bộ đếm và checkbox khỏi danh sách. Giữ trạng thái giọng, menu từng câu và menu timeline; API/storage nhận danh sách ID vẫn nguyên. Thao tác giọng dùng một cue ID, flush draft trước và lấy revision mới nhất từ page/track summary.

### Baseline và so sánh cùng fixture

Fixture dùng component/CSS production, dữ liệu mẫu 7 câu, trang 50 câu và tập 120 câu phân trang, panel 320/420 px. Có chuỗi chuyển câu chậm/nhanh, tua liên tiếp, cuộn tay và thay revision khi field có focus. Bộ playback/timeline hiện hành được chạy lại riêng trong full suite. Không mở project hoặc ghi transcript của người dùng.

Với panel 420 px, trang 50 câu, zoom 100%:

| Số đo | Trước | Sau |
|---|---:|---:|
| Cuộn khi chọn lại cue đang trong vùng đọc | 1 | 0 |
| Lệnh cuộn tại ranh giới cue sau wheel | 1 | 0 |
| Độ lệch vị trí cuộn tay | 3032 px | 0 px |
| Tổng lượt render row trong chuỗi fixture | 1200 | 33 |
| Row remount khi tăng revision | 50 | 0 |
| Giữ field đang focus qua revision | Không | Có |

Tổng thời gian layout CDP cho sáu trường hợp của mỗi mức zoom:

| Zoom | Trước | Sau |
|---|---:|---:|
| 100% | 276,1 ms | 104,2 ms |
| 125% | 287,4 ms | 112,5 ms |
| 150% | 293,1 ms | 91,8 ms |
| 200% | 301,7 ms | 180,3 ms |

Đây là số đo một lượt trên fixture, không phải cam kết FPS hoặc benchmark video/model thật. Tổng API cuộn ở trường hợp 420 px/50 câu là 18 trước sửa và 26–28 sau sửa tùy zoom, vì bản mới bao gồm lệnh dừng animation cũ khi đổi đích. Đánh giá cuộn dư bằng các trường hợp cùng cue/manual scroll và việc giữ đúng đích, không suy từ tổng API cuộn.

WebView2 đã đạt tại zoom 100/125/150/200%, cả hai chiều rộng panel. Banner nhiều dòng không chồng nút ×/Thử lại, có focus rõ và đóng bằng chuột hoặc Enter qua CDP. Ảnh và JSON local nằm trong `artifacts/subtitle-editor-ui/before/` và `artifacts/subtitle-editor-ui/final/`.

### Build và test

Môi trường: Windows build 26200, .NET SDK 10.0.301, Node v24.16.0; dirty worktree trên HEAD `2b4171c`. Giữ nguyên thay đổi của công việc trước.

- `npm ci --no-audit --no-fund`: đạt.
- Frontend production build: đạt; 22 file test, **119 Passed / 0 Failed / 0 Skipped**.
- `dotnet restore` và Release build toàn solution: đạt, MSBuild **0 warning / 0 error**.
- Debug build desktop: đạt; cả Debug/Release có ba file bundle khớp SHA-256 với `Web/dist`.
- Full .NET chạy tuần tự các collection: **972 Passed / 0 Failed / 3 Skipped / 975 Total**. Bao gồm WebView2 editor/Enter, playback, timeline layout và các regression FFmpeg hiện hành.
- Ba test bị bỏ qua là Piper model integration, Qwen model integration và Qwen benchmark. Không coi các model/runtime này đã được nghiệm thu.
- Vite vẫn có cảnh báo chunk lớn hơn 500 kB.

Lệnh kiểm tra cuối:

```powershell
$env:VIDEOMAKER_SUBTITLE_EDITOR_ARTIFACTS = Join-Path (Get-Location) 'artifacts/subtitle-editor-ui/final'
dotnet test TOOL-TESTS/TOOL-TESTS.csproj -c Release --no-build --logger 'trx;LogFileName=final-suite-serial.trx' --results-directory artifacts/subtitle-editor-ui -- xUnit.ParallelizeTestCollections=false
```

Ngoại lệ đã ghi nhận: một lượt full suite song song bị timeout ở `Cpu_backend_dry_run_reports_selected_avx_and_native_hash` (971 Passed / 1 Failed / 3 Skipped); chạy riêng lại đạt, rồi toàn suite tuần tự đạt như trên. Không đổi timeout hoặc logic worker để làm test qua. Test audio-duck được sửa cách điều khiển thời gian để chờ ramp 140 ms hiện hành; logic audio giữ nguyên. Fixture bàn phím được bổ sung focus emulation và ký tự Enter để kiểm hành vi native đúng trong WebView2 chạy ẩn.

Bằng chứng local: `artifacts/subtitle-editor-ui/final-suite-serial.trx`, `worker-dry-run-recheck.trx`, JSON số đo và ảnh; artifact không commit. Phạm vi này không chạy migration, provider có phí, Piper/Qwen thật hoặc nghiệm thu nghe video riêng của người dùng.

### Sửa bổ sung phần đầu thẻ theo ảnh phản hồi (2026-09-09)

- Khối trạng thái giọng trước đây tạo một hàng riêng phía trên tiêu đề; nền tiêu đề có góc bo riêng còn che lên dải xanh của thẻ. Chuyển trạng thái giọng thành badge nhỏ cùng cụm trạng thái dịch trong tiêu đề, đồng bộ cỡ chữ và vẽ dải chọn phía trên nền bằng pseudo-element không nhận sự kiện chuột. Menu thao tác vẫn được phép tràn ra ngoài thẻ.
- Bổ sung phép đo trong fixture WebView2 cho cả nhãn bật/tắt giọng: không có hàng thừa phía trên tiêu đề, nhãn nằm trong tiêu đề, không lấn chữ xem trước và không lớn hơn chữ phụ đề. Đạt 24 kịch bản tại panel 320/420 px, zoom 100/125/150/200%; đã đối chiếu ảnh dải xanh sau sửa.
- Chạy lại `npm ci`, build frontend và test: **119 Passed / 0 Failed / 0 Skipped**. Restore, build solution Release và desktop Debug đạt; ba file bundle ở cả hai cấu hình khớp SHA-256 với `Web/dist`. Vite vẫn cảnh báo chunk lớn hơn 500 kB.
- Toàn bộ .NET chạy với `xUnit.ParallelizeTestCollections=false`: **972 Passed / 0 Failed / 3 Skipped / 975 Total**. Ba test model/benchmark opt-in vẫn được báo riêng là chưa chạy.
- Bằng chứng đợt sửa bổ sung: `artifacts/subtitle-editor-ui/header-fix-suite.trx` và ảnh/JSON trong `artifacts/subtitle-editor-ui/header-fix/`. Các số đo trước/sau ở phần trên thuộc đợt triển khai ban đầu, không được thay bằng kết quả đợt sửa bố cục này.
