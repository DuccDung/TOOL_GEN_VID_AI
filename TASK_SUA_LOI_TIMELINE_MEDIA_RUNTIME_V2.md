# Task V2 — Sửa dứt điểm thumbnail và waveform không hiển thị trên timeline Vietsub

## 1. Mục đích tài liệu

Tài liệu này là đặc tả bàn giao cho AI triển khai tiếp theo. Đây là nguồn task hiện hành cho lỗi timeline media; tài liệu `TASK_SUA_LOI_TIMELINE_THUMBNAIL_WAVEFORM.md` chỉ còn giá trị lịch sử.

Mục tiêu cuối cùng:

- Track `Video` hiển thị được frame JPEG thật trong WebView2.
- Track `Voice gốc` hiển thị được waveform PNG thật khi video có audio.
- Lỗi tạm thời có thể tự phục hồi có giới hạn, không biến thành placeholder vĩnh viễn.
- Có bằng chứng end-to-end từ project/session thật trong test, không chỉ test từng lớp độc lập.
- Giữ nguyên kiểm tra project, organization, user, media ID và source SHA-256.

Không được đánh dấu task hoàn thành chỉ vì unit test, frontend test hoặc WebView2 test giả lập riêng lẻ đều xanh. Phải có smoke test toàn ứng dụng theo mục 12.

## 2. Trạng thái hiện hành và bằng chứng đã xác nhận

Ngày tái hiện gần nhất: 2026-09-04.

Hiện tượng:

- Timeline tạo đủ các ô frame nhưng mọi ô hiển thị `Frame chưa tải được`.
- Track âm thanh hiển thị `Chưa tải được waveform`.
- Ruler, playhead và geometry timeline vẫn render bình thường.

Đã xác nhận:

1. Project runtime ở trạng thái `READY`.
2. Video nguồn `COPY` tồn tại, dung lượng và SHA-256 phù hợp manifest.
3. Có đủ 12 JPEG trong `thumbnails/v1/{sha256}`.
4. Có PNG trong `waveforms/v1/{sha256}/source.png`.
5. JPEG và PNG có magic bytes hợp lệ, kích thước hợp lệ và mở xem trực tiếp được.
6. Các artifact được tạo xong trước thời điểm chụp màn hình lỗi.
7. WebView2 đã nạp bundle web mới nhất tại thời điểm tái hiện; không được quy lỗi lần này cho JavaScript cũ.
8. `GetSourceStatus` xử lý riêng chế độ `COPY`, nên khác biệt `LastWriteTime` giữa file gốc và bản copy không phải nguyên nhân trong mẫu đã kiểm tra.
9. Overload ba tham số của `AddWebResourceRequestedFilter` với `SourceKinds.All` là hợp lệ và test WebView2 tối giản bắt được request.

Kết luận chắc chắn:

- Lỗi không nằm ở bước FFmpeg tạo thumbnail/waveform.
- Lỗi xảy ra sau khi frontend đã nhận URL artifact: trong chuỗi request qua bridge/playback service, lúc tạo response WebView2, hoặc lúc trình duyệt chấp nhận response.
- Chưa biết HTTP status và `X-Vietsub-Error-Code` của lần lỗi thật vì code hiện chỉ ghi `System.Diagnostics.Trace`, không có log bền vững sau khi đóng app.

Không được tự khẳng định một nguyên nhân cụ thể như CORS, cache, session mismatch hoặc stream lifetime trước khi T1 thu được bằng chứng runtime.

## 3. Khoảng trống của bộ test hiện tại

Hai nhóm test hiện hành đang kiểm tra hai nửa tách rời:

### 3.1. Test playback service

`TOOL-TESTS/Vietsub/VietsubMediaTests.cs` kiểm tra route, permission, status, header và magic bytes. Artifact từ test runner chủ yếu là dữ liệu giả có magic bytes, không nhất thiết là ảnh WebView2 giải mã được.

### 3.2. Test WebView2

`TOOL-TESTS/Vietsub/VietsubWebView2MediaIntegrationTests.cs` dùng WebView2 thật và ảnh hợp lệ, nhưng handler tự tạo response HTTP 200. Test này không gọi:

- `VietsubWebBridge.TryOpenPlaybackRequest`;
- `VietsubMediaPlaybackService.Open`;
- project store/session thật;
- context organization/user thật;
- resolver artifact thật.

Do đó chưa có test đi qua toàn bộ chuỗi:

```text
React/HTML <img>
  -> WebView2 WebResourceRequested
  -> Form1 response adapter
  -> VietsubWebBridge.TryOpenPlaybackRequest
  -> VietsubMediaPlaybackService.Open
  -> source/context/artifact validation
  -> JPEG/PNG thật
  -> WebView2 decode
  -> img load + naturalWidth/naturalHeight > 0
```

AI triển khai phải đóng khoảng trống này trước hoặc đồng thời với bản sửa.

## 4. Logic tham chiếu phải đọc

Repository tham chiếu chỉ đọc, không sửa:

```text
D:\laptrinhweb\code_outsrc\TOOL_VIETSUB\TOOL_VIETSUB
```

Các file chính:

- `SubVid.App/MainForm.cs`
- `SubVid.App/Media/TimelineThumbnailService.cs`
- `SubVid.App/ClientApp/src/lib/timelineThumbnails.ts`
- `SubVid.App/ClientApp/src/components/Timeline.tsx`

Các đặc điểm cần học từ dự án tham chiếu:

1. Frontend chỉ yêu cầu các index đang nằm trong viewport cộng overscan.
2. Native ưu tiên index gần tâm viewport.
3. Mỗi frame chỉ được gửi URL sau khi cache đã có file dùng được.
4. Native phát `timeline:thumbnail:ready` riêng cho từng frame.
5. Khi source thay đổi, queue cũ bị hủy và cache frontend bị reset.
6. Endpoint thumbnail có route content-addressed theo profile/SHA/index.
7. Handler trả `FileStream` trực tiếp với `Content-Type`, `Content-Length`, CORS origin chính xác và cache immutable.
8. Trang web được điều hướng với query version lấy từ mốc sửa `index.html` để tránh dùng index cũ.

Lưu ý quan trọng: waveform trong dự án tham chiếu chỉ là 52 thanh CSS mô phỏng, không phải waveform thật. Dự án hiện tại phải tiếp tục dùng PNG thật; không được thay bằng CSS giả chỉ để làm UI trông như đã sửa.

## 5. Ràng buộc bắt buộc

- Đọc `AGENTS.md`, `TOOL-LOCAL/AGENTS.md`, `README.md`, `NGHIEP_VU_HE_THONG_VIDEOMAKER.md` và `KE_HOACH_SERVER_AI_GATEWAY.md` trước khi sửa.
- Kiểm tra source và đường gọi hiện tại; không phục hồi thiết kế cũ từ Git/history.
- Repository đang có nhiều thay đổi chưa commit. Không ghi đè hoặc hoàn tác thay đổi không liên quan.
- Không dùng `git reset --hard`, `git checkout --` hoặc xóa worktree.
- Không sửa trực tiếp `Web/dist`, `bin`, `obj` hoặc artifact build làm source.
- Không bỏ kiểm tra session, organization, user, project, media ID hoặc source SHA để làm ảnh hiển thị.
- Không đưa absolute path, token, user ID, organization ID hoặc exception thô vào DOM, response hay log.
- Không gọi OpenAI/Kling thật, không chạy migration database, không publish release.
- Không thay đổi nghiệp vụ OCR, subtitle, dịch, voice AI hoặc export ngoài phần cần thiết để tránh regression.

## 6. Kế hoạch triển khai

### T0 — Chụp baseline và bảo vệ thay đổi hiện có

Trạng thái: `[x]`

Công việc:

1. Chạy `git status --short` nếu repository có Git và ghi nhận các file đã thay đổi.
2. Đọc diff của các file sẽ sửa; không giả định toàn bộ thay đổi hiện tại do task này tạo ra.
3. Chạy riêng các test timeline/media hiện có để tạo baseline.
4. Không cập nhật số lượng test trong tài liệu nếu chưa chạy thực tế.

Kết quả cần lưu trong phần báo cáo cuối:

- Test nào đã xanh trước khi sửa.
- Test nào tái hiện được lỗi trước khi sửa.
- File nào có thay đổi người dùng cần giữ nguyên.

### T1 — Thêm chẩn đoán runtime bền vững, an toàn

Trạng thái: `[x]`

Ghi chú 2026-09-05: log ứng dụng desktop thật ghi nhận 46 request thumbnail và 6 request waveform đều dừng tại `filter`, không có HTTP response; 5 request video đi đủ đến `response_creation` và trả 206. Nguyên nhân được khoanh chính xác tại thao tác đọc header `Range` không tồn tại trước bridge. Handler đã bỏ đọc `Range` cho JPEG/PNG, dùng kiểm tra header an toàn cho video và integration test T2 đã được cập nhật để chạy cùng logic production.

File dự kiến:

- `TOOL-LOCAL/Form1.cs`
- Có thể thêm helper log dưới `TOOL-LOCAL/Vietsub/` nếu đã có pattern logging phù hợp.
- `TOOL-TESTS/Vietsub/` cho test log/redaction.

Công việc:

1. Với mọi request tới `vietsub-media.app.local`, ghi tối thiểu:
   - loại resource: `video`, `thumbnail`, `waveform`, `unknown`;
   - method;
   - HTTP status;
   - mã lỗi ổn định hoặc `none`;
   - giai đoạn: filter, bridge, playback, response creation, response received;
   - correlation ID ngẫu nhiên không mang dữ liệu người dùng.
2. Log phải được ghi vào cơ chế log desktop bền vững có rotation/giới hạn kích thước. Nếu chưa có abstraction phù hợp, tạo file log riêng dưới thư mục `Logs` của ứng dụng.
3. Không log URL đầy đủ, absolute path, SHA đầy đủ, project/media/user/org ID hoặc body/header nhạy cảm.
4. Bắt và phân loại riêng exception khi tạo WebView response; chỉ log tên loại exception an toàn.
5. Bổ sung một kênh báo lỗi an toàn cho frontend, ví dụ `vietsub.media.load.failed`, chứa resource type, correlation ID và error code; không chứa local path.
6. Viết test xác nhận log/message không rò rỉ path và identifier.

Điểm dừng điều tra:

- Tái hiện ứng dụng thật một lần và xác định request ảnh nhận `2xx`, `4xx`, `5xx` hay không đi vào handler.
- Không bắt đầu thay đổi kiến trúc dựa trên phỏng đoán trước khi có kết quả này, trừ khi test end-to-end ở T2 đã tái hiện chính xác lỗi.

### T2 — Viết integration test end-to-end thực sự

Trạng thái: `[x]`

File chính:

- `TOOL-TESTS/Vietsub/VietsubWebView2MediaIntegrationTests.cs`
- Có thể thêm fixture nhỏ dưới cấu trúc test phù hợp.

Công việc:

1. Tạo temporary workspace và project manifest thật.
2. Tạo/import video test có cả video và audio mà không gọi dịch vụ ngoài.
3. Tạo JPEG và PNG thật, có thể giải mã; không dùng file chỉ có magic bytes.
4. Khởi tạo project store, media import service, thumbnail/waveform service, playback service và `VietsubWebBridge` thật.
5. Mở project qua message bridge giống ứng dụng.
6. Dùng context user/organization khớp manifest.
7. Cấu hình WebView2 giống `Form1`, bao gồm:
   - virtual host `app.local`;
   - virtual host legacy `media.app.local` nếu production đang cấu hình;
   - filter chính xác cho `vietsub-media.app.local`;
   - cùng response adapter/header code production.
8. Handler trong test bắt buộc gọi `bridge.TryOpenPlaybackRequest`; cấm tự tạo response 200.
9. Trang test lấy URL từ state bridge hoặc từ summary thật, không tự viết URL giả.
10. Xác nhận cho ít nhất một JPEG và waveform PNG:
    - request đã đi qua handler;
    - status là 200;
    - không có `X-Vietsub-Error-Code`;
    - sự kiện `load` chạy;
    - `naturalWidth > 0` và `naturalHeight > 0`.
11. Thêm case sai user/org và xác nhận bị từ chối, không trả nội dung file.
12. Thêm case artifact bị xóa/stale và xác nhận code recovery đúng.

Test này phải thất bại với đường chạy đang gây lỗi hoặc ít nhất phải chứng minh đường production hoàn chỉnh. Nếu test vẫn xanh nhưng app thật lỗi, dùng log T1 so sánh khác biệt runtime/profile/cache/threading.

### T3 — Sửa lifecycle thumbnail theo mô hình request/ready của dự án tham chiếu

Trạng thái: `[x]`

Ghi chú bổ sung 2026-09-05: smoke thực tế đã hiển thị waveform và 4/12 frame, đồng thời log xác nhận mọi request ảnh đã đi đủ tới `response_received` với HTTP 200. Project chưa có active subtitle track nên `requestVisibleWindow` thoát sớm trước `setVisibleRange`, làm phạm vi media mắc ở `0–1 ms` và chỉ request các index `0..3`. Frontend đã được sửa để luôn cập nhật viewport media trước, sau đó mới bỏ qua riêng request cửa sổ cue khi thiếu `trackId`. Regression test khóa thứ tự này đã được thêm; web test đạt 21/21, Release build đạt 0 warning/error và toàn bộ test đạt 655/655. Việc xác nhận 12/12 frame trên ứng dụng sau bản sửa vẫn thuộc smoke test còn mở.

File dự kiến:

- `TOOL-LOCAL/Vietsub/Media/VietsubTimelineThumbnailService.cs`
- `TOOL-LOCAL/Vietsub/VietsubWebBridge.cs`
- `TOOL-LOCAL/Web/src/features/vietsub/useVietsubModule.ts`
- `TOOL-LOCAL/Web/src/features/vietsub/VietsubTimeline.tsx`
- `TOOL-LOCAL/Web/src/features/vietsub/types.ts`
- Test C# và frontend liên quan.

Công việc:

1. Thêm message request thumbnail theo viewport, ví dụ:

```json
{
  "type": "vietsub.timeline.thumbnails.request",
  "payload": {
    "sourceSha256": "...",
    "indices": [0, 1, 2]
  }
}
```

2. Bridge phải kiểm tra:
   - có project session hiện hành;
   - source SHA khớp manifest;
   - media nguồn vẫn dùng được;
   - index nằm trong profile hiện hành;
   - giới hạn số index/request, đề xuất tối đa 64;
   - loại bỏ index trùng.
3. Thumbnail service phải:
   - trả ready ngay cho artifact đã có;
   - ưu tiên các index frontend vừa yêu cầu;
   - không chạy hai FFmpeg job cho cùng source/index;
   - hủy hoặc bỏ kết quả queue cũ khi project/source đổi;
   - phát ready riêng sau khi file đã được move atomically vào cache;
   - phát failed riêng theo resource/index nhưng không lộ path.
4. Frontend phải tính các index đang nhìn thấy cộng overscan và chỉ request phần còn thiếu.
5. Cache frontend phải được khóa theo media ID, profile version, source SHA và index.
6. Khi nhận ready đúng source/index, frontend mới gắn URL hoặc tăng revision để thẻ ảnh tải lại.
7. Có thể giữ số lượng 12 frame ở bản sửa tối thiểu. Không tự tăng lên 160 nếu chưa đánh giá hiệu năng và UX; số lượng frame là profile cần test riêng.

Không sao chép nguyên xi cache toàn cục của dự án tham chiếu nếu điều đó làm mất ranh giới project. Route có thể content-addressed, nhưng handler vẫn phải xác minh active manifest và quyền context.

### T4 — Làm chắc đường phục vụ JPEG/PNG qua WebView2

Trạng thái: `[x]`

File chính:

- `TOOL-LOCAL/Form1.cs`
- `TOOL-LOCAL/Vietsub/Playback/VietsubMediaPlaybackService.cs`

Công việc phụ thuộc kết quả T1/T2:

1. Nếu handler trả lỗi:
   - sửa đúng nhánh route/context/source/artifact tạo ra error code;
   - không đổi mọi lỗi thành 200 hoặc bỏ authorization.
2. Nếu handler trả 200 nhưng `<img>` vẫn lỗi:
   - so sánh byte length, stream position và stream lifetime;
   - thử đường trả `FileStream` trực tiếp giống dự án tham chiếu với `FileShare.Read | FileShare.Delete`;
   - bảo đảm stream còn sống đến khi WebView2 đọc xong;
   - kiểm tra header được gắn đúng trên response thực tế;
   - kiểm tra MIME bằng nội dung thật, không chỉ extension.
3. Response thành công tối thiểu phải có:
   - `Content-Type` đúng;
   - `Content-Length` đúng;
   - cache policy tương thích URL có version/SHA;
   - CORS origin giới hạn `https://app.local` khi frontend dùng CORS;
   - resource policy phù hợp;
   - body rỗng cho `HEAD`.
4. Không phụ thuộc vào suffix matching giữa `media.app.local` và `vietsub-media.app.local`; test phải chứng minh đúng host được handler bắt.
5. Không đổi filter ba tham số sang overload cũ chỉ vì dự án tham chiếu dùng overload cũ. Chỉ đổi khi test production-equivalent chứng minh overload hiện tại là nguyên nhân.
6. Nếu frontend không đọc pixel/canvas từ thumbnail hoặc waveform, đánh giá bỏ `crossOrigin="anonymous"` để dùng cơ chế `<img>` đơn giản như dự án tham chiếu. Chỉ thực hiện khi test CSP và security vẫn đạt; không nới lỏng `img-src`.

### T5 — Sửa frontend để lỗi tạm thời không thành lỗi vĩnh viễn

Trạng thái: `[x]`

File chính:

- `TOOL-LOCAL/Web/src/features/vietsub/VietsubTimeline.tsx`
- `TOOL-LOCAL/Web/src/features/vietsub/timelineMediaState.ts`
- `TOOL-LOCAL/Web/src/features/vietsub/useVietsubModule.ts`
- `TOOL-LOCAL/Web/src/styles.css`
- Test component liên quan.

Công việc:

1. Không tháo vĩnh viễn `<img>` chỉ sau một `onError`.
2. Mỗi artifact có state rõ ràng: `missing`, `requested`, `ready`, `loading`, `loaded`, `retry_wait`, `failed_terminal`.
3. Retry chỉ được kích hoạt bởi một trong các sự kiện:
   - native phát ready/revision mới;
   - error code chỉ ra lỗi tạm thời;
   - project được mở lại và bridge xác nhận artifact tồn tại.
4. Retry có giới hạn và backoff; không tạo vòng lặp request vô hạn.
5. Lỗi 403/context mismatch không tự retry; yêu cầu refresh state/session.
6. Lỗi missing/stale có thể yêu cầu native regenerate đúng một job cho source/index.
7. Đổi media/project phải hủy timer, request và state lỗi của media cũ.
8. Placeholder phải giữ nguyên geometry timeline và không hiển thị broken-image icon/alt text tràn ô.
9. Có thể hiển thị mã lỗi an toàn trong tooltip/dev diagnostics, không hiển thị identifier/path.

### T6 — Giữ waveform thật và áp dụng lifecycle tương đương

Trạng thái: `[x]`

File dự kiến:

- `TOOL-LOCAL/Vietsub/Media/VietsubTimelineWaveformService.cs`
- `TOOL-LOCAL/Vietsub/VietsubWebBridge.cs`
- `TOOL-LOCAL/Web/src/features/vietsub/VietsubTimeline.tsx`

Công việc:

1. Chỉ phát `READY` sau khi PNG tồn tại, magic bytes đúng và endpoint có thể mở file.
2. Phân biệt `PENDING`, `READY`, `FAILED`, `NO_AUDIO`.
3. Khi project cũ thiếu PNG, tạo lại một job duy nhất và phát event ready sau khi hoàn thành.
4. Khi request tải PNG lỗi tạm thời, dùng cùng cơ chế revision/retry có giới hạn như thumbnail.
5. Không thay waveform thật bằng các thanh CSS của dự án tham chiếu.

### T7 — Chống cache bundle cũ và asset build tồn dư

Trạng thái: `[x]`

File dự kiến:

- `TOOL-LOCAL/Form1.cs`
- `TOOL-LOCAL/TOOL-LOCAL.csproj`

Công việc:

1. Điều hướng dashboard bằng URL có version, ví dụ `index.html?v={lastWriteTicks}` như dự án tham chiếu.
2. Đảm bảo bước build/copy dọn đúng thư mục asset sinh ra trong target `wwwroot/assets` trước khi copy bundle mới.
3. Chỉ dọn output tái tạo được; không xóa source hoặc toàn bộ workspace.
4. Test hoặc xác minh rằng `index.html` trong output trỏ đến asset hiện hành và không phụ thuộc asset hash cũ còn sót lại.

Đây là hardening bắt buộc để tránh lỗi tái diễn sau cập nhật, nhưng không được báo cáo là nguyên nhân trực tiếp của mẫu ngày 2026-09-04 vì bundle mới đã được nạp trong lần đó.

### T8 — Regression test và kiểm tra bảo mật

Trạng thái: `[x]`

Các trường hợp bắt buộc:

- Thumbnail JPEG thật tải và decode được qua full bridge/playback path.
- Waveform PNG thật tải và decode được qua full bridge/playback path.
- `naturalWidth`/`naturalHeight` lớn hơn 0.
- Request đúng project/user/org trả 200.
- Sai project, media, user hoặc org bị từ chối.
- Sai profile hoặc source SHA không dùng cache cũ.
- Artifact missing/stale kích hoạt recovery có kiểm soát.
- Source đổi trong chế độ `LINK` làm artifact cũ bị từ chối.
- Source `COPY` hợp lệ không bị đánh dấu đổi chỉ do timestamp workspace.
- Đổi project trong lúc queue đang chạy không phát ready của project cũ vào UI mới.
- Nhiều request trùng không tạo nhiều FFmpeg process.
- Retry không vô hạn.
- Log, message và response không lộ local path/identifier nhạy cảm.
- Preview video, OCR region preview, cue subtitle, seek, zoom và playhead không regression.

## 7. Thứ tự thực hiện bắt buộc

```text
T0 baseline
  -> T1 chẩn đoán runtime
  -> T2 integration test full path
  -> phân nhánh theo status/error thực tế
  -> T3 lifecycle thumbnail
  -> T4 media response
  -> T5 frontend recovery
  -> T6 waveform
  -> T7 cache hardening
  -> T8 regression
  -> smoke test toàn ứng dụng
```

T3–T6 có thể điều chỉnh phạm vi dựa trên bằng chứng T1/T2, nhưng mọi điều chỉnh phải ghi rõ lý do trong tài liệu này hoặc báo cáo bàn giao.

## 8. Danh sách file dự kiến bị tác động

```text
TOOL-LOCAL/Form1.cs
TOOL-LOCAL/TOOL-LOCAL.csproj
TOOL-LOCAL/Vietsub/Media/VietsubTimelineThumbnailService.cs
TOOL-LOCAL/Vietsub/Media/VietsubTimelineWaveformService.cs
TOOL-LOCAL/Vietsub/Playback/VietsubMediaPlaybackService.cs
TOOL-LOCAL/Vietsub/VietsubWebBridge.cs
TOOL-LOCAL/Web/src/features/vietsub/VietsubTimeline.tsx
TOOL-LOCAL/Web/src/features/vietsub/timelineMediaState.ts
TOOL-LOCAL/Web/src/features/vietsub/useVietsubModule.ts
TOOL-LOCAL/Web/src/features/vietsub/types.ts
TOOL-LOCAL/Web/src/styles.css
TOOL-LOCAL/Web/src/features/vietsub/VietsubTimeline.test.tsx
TOOL-TESTS/Vietsub/VietsubMediaTests.cs
TOOL-TESTS/Vietsub/VietsubWebView2MediaIntegrationTests.cs
```

Nếu thay đổi message contract giữa TypeScript và C#, phải sửa đồng thời hai phía và test contract. Không tự mở rộng sang DTO public/server nếu message chỉ là WebView bridge nội bộ.

## 9. Kiểm tra bắt buộc sau khi sửa

Kiểm tra web:

```powershell
Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm test
npm run build
```

Kiểm tra toàn solution từ root:

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Không sửa test để chỉ né lỗi. Mọi test mới phải thất bại hợp lý khi bỏ bản sửa tương ứng.

## 10. Smoke test toàn ứng dụng bắt buộc

Dùng video test cục bộ ngắn, có audio; không phát sinh request AI:

1. Tạo project mới và import bằng `COPY`.
2. Xác nhận frame xuất hiện dần hoặc xuất hiện sau khi ready, không có `Frame chưa tải được` kéo dài.
3. Xác nhận waveform thật hiển thị.
4. Zoom in/out, cuộn timeline và resize cửa sổ; frame đang nhìn thấy tiếp tục tải đúng.
5. Seek bằng timeline và xác nhận preview video không regression.
6. Đóng/mở lại project; artifact cache tải lại được không cần chạy FFmpeg thừa.
7. Xóa riêng một thumbnail trong project test, mở lại/request lại và xác nhận chỉ frame đó được phục hồi.
8. Xóa waveform trong project test và xác nhận tạo lại đúng một lần.
9. Chuyển nhanh giữa hai project trong lúc artifact đang tạo; UI không nhận nhầm frame.
10. Kiểm tra log của lần chạy: request thành công là 200; không có path hoặc identifier nhạy cảm.

Nếu smoke test còn lỗi, phải ghi lại correlation ID, resource type, status và error code; không chụp UI rồi suy đoán.

## 11. Tiêu chí hoàn thành

- [x] Có test full path đi qua WebView2 -> Form1 adapter -> bridge -> playback service -> artifact thật.
- [x] Test xác nhận JPEG và PNG phát `load` và có kích thước tự nhiên lớn hơn 0.
- [x] Track Video hiển thị frame thật trên ứng dụng.
- [x] Track Voice gốc hiển thị waveform PNG thật khi có audio.
- [x] Không còn trạng thái lỗi vĩnh viễn sau một lỗi tải tạm thời.
- [x] Không có retry vô hạn hoặc FFmpeg job trùng.
- [x] Project/source cũ không phát ready nhầm sang project/source mới.
- [x] Context sai không đọc được artifact.
- [x] Log đủ chẩn đoán nhưng không lộ dữ liệu nhạy cảm.
- [x] Cache web được version hóa.
- [x] Frontend test, Release build và toàn bộ test solution đạt.
- [ ] Smoke test toàn ứng dụng đạt đầy đủ mục 10.

## 12. Nội dung AI phải báo cáo khi bàn giao

AI triển khai phải trả lời rõ:

1. HTTP status/error code thực tế trước khi sửa là gì.
2. Nguyên nhân gốc đã được chứng minh bằng test hoặc log nào.
3. Những phần nào học từ `TOOL_VIETSUB`, những phần nào không sao chép và lý do.
4. Danh sách file đã sửa.
5. Test đã thêm và lý do test cũ không bắt được lỗi.
6. Kết quả chính xác của từng lệnh build/test.
7. Kết quả từng bước smoke test.
8. Rủi ro hoặc công việc còn lại, nếu có.

Không dùng cụm từ “đã sửa xong” nếu chưa hoàn thành test full path và smoke test toàn ứng dụng.
