# Task sửa lỗi thumbnail và waveform không hiển thị trên timeline Vietsub

> **ĐÃ BỊ THAY THẾ:** Sau lần triển khai và kiểm thử ghi trong tài liệu này, lỗi vẫn tái hiện trên ứng dụng thật ngày 2026-09-04. Không dùng các checkbox hoàn thành bên dưới làm trạng thái hiện hành. AI tiếp theo phải thực hiện theo [TASK_SUA_LOI_TIMELINE_MEDIA_RUNTIME_V2.md](TASK_SUA_LOI_TIMELINE_MEDIA_RUNTIME_V2.md).

## 1. Trạng thái tài liệu

- Ngày ghi nhận: 2026-09-04.
- Trạng thái: **đã triển khai source, đạt toàn bộ kiểm tra tự động và integration test WebView2 thật; còn smoke test tương tác toàn ứng dụng của T7**.
- Phạm vi: `TOOL-LOCAL`, WebView2 bridge và giao diện React của editor Vietsub.
- Không thuộc phạm vi: thay đổi nghiệp vụ OCR, dịch, voice AI, export, database server hoặc AI Gateway.

### 1.1. Tiến độ triển khai ngày 2026-09-04

- [x] T1 — Response typed, mã lỗi ổn định, ảnh dùng buffer riêng và `HEAD` không có body.
- [x] T2 — Filter được đăng ký trước navigation, handler chuyển nguyên response và log chẩn đoán an toàn.
- [x] T3 — State chỉ phát artifact hợp lệ, project cũ phát `PENDING` rồi tự tái tạo, có khóa chống job trùng.
- [x] T4 — React có trạng thái load/error, placeholder giữ geometry và retry tối đa một lần.
- [x] T5 — Có regression test backend/media cho route, quyền, hash, magic bytes, cache và stream.
- [x] T6 — Có test frontend cho thumbnail, waveform, fallback, retry và đổi media.
- [x] T7a — Integration test dùng runtime WebView2 thật đã tải thành công JPEG và PNG qua custom host/response của ứng dụng.
- [ ] T7b — Chưa chạy smoke test tương tác toàn ứng dụng với đăng nhập, project thật và các thao tác zoom/seek/reopen trong phiên terminal này.

Mốc xác minh đã chạy thực tế:

- `npm ci --no-audit --no-fund`: đạt.
- `npm test`: 19/19 test đạt.
- `npm run build`: đạt.
- `dotnet restore TOOL_GEN_POST_VIDEO.slnx`: đạt.
- `dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore`: đạt, 0 warning/0 error.
- `dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build`: 647/647 test đạt, 0 skipped.

## 2. Hiện tượng người dùng

Sau khi mở project đã có video:

- Track `Video` trên timeline có đủ các ô theo thời gian nhưng hiển thị biểu tượng ảnh lỗi và alt text dạng `Frame video tại 0:01`.
- Track `Voice gốc` hiển thị biểu tượng ảnh lỗi và alt text `Dạng sóng âm thanh gốc`.
- Ruler, playhead, track phụ đề và bố cục timeline vẫn render.
- Video nguồn trong khu vực preview vẫn có thể tồn tại độc lập với lỗi này.

Kết luận ở tầng giao diện: React đã nhận URL thumbnail/waveform và đã tạo thẻ `<img>`, nhưng WebView2 không tải hoặc không giải mã được response của các URL nội bộ.

## 3. Những gì đã kiểm tra

### 3.1. Artifact được tạo thành công

- Project kiểm tra có video nguồn khoảng 9,2 giây và có audio.
- Có đủ 12 file thumbnail trong cache project theo source SHA-256.
- Các thumbnail có chữ ký JPEG hợp lệ và mở xem trực tiếp được.
- File waveform có chữ ký PNG hợp lệ và mở xem trực tiếp được.

Do đó chưa có bằng chứng cho thấy FFmpeg, bước trích frame hoặc bước sinh waveform là nguyên nhân trực tiếp.

### 3.2. Metadata đã đến React

`VietsubTimeline.tsx` đang dùng:

- `thumbnail.url` làm `src` cho ảnh ở track Video.
- `media.waveformUrl` làm `src` cho ảnh ở track Voice gốc khi trạng thái là `READY`.

Việc trình duyệt hiện đúng alt text chứng minh dữ liệu timeline đã đến frontend; lỗi xảy ra ở bước request/response ảnh nội bộ hoặc lúc WebView2 nhận response.

### 3.3. URL và CSP đã có cấu hình

Các artifact dùng host nội bộ:

```text
https://vietsub-media.app.local/projects/{projectId}/media/{mediaId}/thumbnails/v{profileVersion}/{sourceSha256}/{index}.jpg
https://vietsub-media.app.local/projects/{projectId}/media/{mediaId}/waveform/v{profileVersion}/{sourceSha256}/source.png
```

URL chứa profile version và source SHA-256 để response `immutable` không dùng nhầm cache của source hoặc cấu hình tạo artifact cũ. `TOOL-LOCAL/Web/index.html` tiếp tục chỉ cho phép đúng host này trong `img-src` và `media-src`.

### 3.4. Điểm mù request/response đã được đóng

Luồng request hiện tại:

```text
React <img>
  -> WebView2 WebResourceRequested
  -> Form1.WebViewOnVietsubMediaRequested
  -> VietsubWebBridge.TryOpenPlaybackRequest
  -> VietsubMediaPlaybackService.Open
  -> đọc artifact từ workspace
```

Luồng đã được làm chắc ở các điểm trực tiếp liên quan WebView2:

- Filter dùng overload có `CoreWebView2WebResourceRequestSourceKinds.All`, đăng ký trước navigation.
- Response header được append từng dòng bằng API typed của WebView2 thay vì phụ thuộc hoàn toàn vào chuỗi raw header.
- CORS chỉ cho origin `https://app.local`; `Cross-Origin-Resource-Policy` cho phép ảnh từ custom host được nhúng cross-origin.
- Mỗi lỗi route, method, context, artifact missing/stale/invalid/unreadable có HTTP status và mã lỗi ổn định riêng.
- JPEG/PNG được kiểm tra magic bytes, giới hạn kích thước rồi phục vụ từ `MemoryStream`; `HEAD` không có body.
- Integration test tạo WebView2 thật và xác nhận cả hai thẻ `<img crossorigin="anonymous">` tải thành công JPEG/PNG qua handler này.

Kiểm tra project, user, organization, media ID, source hash và profile version vẫn được giữ nguyên.

## 4. Mục tiêu sửa lỗi

1. Thumbnail JPEG và waveform PNG được WebView2 tải ổn định từ host nội bộ.
2. Xác định được nguyên nhân bằng mã lỗi an toàn thay vì mọi lỗi đều thành `404` im lặng.
3. Project cũ tự tái tạo artifact khi cache thiếu hoặc stale.
4. Frontend không hiện biểu tượng ảnh vỡ trong thời gian artifact đang tạo hoặc khi tải lỗi.
5. Giữ nguyên giới hạn truy cập theo project, organization, user và source hash.
6. Không lộ đường dẫn file tuyệt đối, token, thông tin phiên hoặc dữ liệu nhạy cảm qua DOM, response hay log.

## 5. Kế hoạch triển khai

### Task T1 — Chuẩn hóa kết quả phục vụ media nội bộ

File chính:

- `TOOL-LOCAL/Vietsub/Playback/VietsubMediaPlaybackService.cs`

Công việc:

- Thay cơ chế dùng `null` cho mọi lỗi bằng kết quả có trạng thái và mã lỗi nội bộ ổn định.
- Phân loại tối thiểu:
  - `400`: URL, index hoặc method không hợp lệ.
  - `403`: request không thuộc context project/session hiện hành.
  - `404`: artifact không tồn tại.
  - `409`: media/source hash đã thay đổi hoặc artifact stale.
  - `500`: không đọc được artifact dù metadata hợp lệ.
- Dùng chung một helper tạo response ảnh cho JPEG và PNG.
- Chỉ hỗ trợ `GET` và `HEAD` theo đúng contract.
- Bảo đảm stream trả về bắt đầu tại position `0` và còn sống đủ lâu để WebView2 đọc.
- Với thumbnail/waveform nhỏ, ưu tiên đọc file thành buffer bất biến rồi tạo `MemoryStream` riêng cho response để loại trừ lỗi vòng đời `FileStream`.
- Trả header đúng:
  - `Content-Type: image/jpeg` hoặc `image/png`.
  - `Content-Length` chính xác.
  - cache header phù hợp với artifact bất biến theo source hash.
  - header cross-origin/resource policy cần thiết cho WebView2 nhưng không mở rộng host tùy ý.
- Có thể kiểm tra nhanh magic bytes trước khi đánh dấu response thành công.
- Không đưa absolute path hoặc exception message thô vào response.

Kết quả mong đợi: service luôn trả lời rõ ràng cho request đã đi vào route, không để tầng WinForms phải suy đoán nguyên nhân từ `null`.

### Task T2 — Làm chắc tầng chặn request của WebView2

File chính:

- `TOOL-LOCAL/Form1.cs`

Công việc:

- Xác nhận filter `https://vietsub-media.app.local/*` được đăng ký trước lần navigation đầu tiên.
- Xác nhận request context `Image` và `Media` đều đi qua handler; có thể tiếp tục dùng `All` nếu test chứng minh đúng.
- Gán `eventArgs.Response` ngay trong callback, không giữ event args để xử lý bất đồng bộ sau callback.
- Chuyển nguyên trạng status, reason phrase, header và stream từ playback service sang `CreateWebResourceResponse`.
- Không tự đổi tất cả lỗi thành `404` chung.
- Thêm log chẩn đoán an toàn gồm loại resource, HTTP status và mã lỗi ổn định; không log URL đầy đủ nếu URL có thể mang định danh, không log path workspace.
- Nếu bridge chưa sẵn sàng, trả mã lỗi rõ thay vì response rỗng.

Kết quả mong đợi: có thể biết request thất bại ở route, authorization, artifact hay stream mà không làm yếu bảo mật.

### Task T3 — Đồng bộ context bảo mật và trạng thái artifact trong bridge

File chính:

- `TOOL-LOCAL/Vietsub/VietsubWebBridge.cs`

Công việc:

- Giữ kiểm tra module enabled, bridge chưa dispose, session project, organization và user hiện hành.
- Trả kết quả lỗi có mã ổn định cho từng điều kiện thay vì trả `null` chung.
- Chỉ phát `waveformStatus = READY` khi artifact tồn tại và endpoint có thể mở nó.
- Chỉ gửi thumbnail metadata của các artifact thực sự tồn tại và hợp lệ.
- Khi mở project cũ mà artifact thiếu/stale:
  1. gửi state `PENDING` hoặc danh sách tạm thời rỗng;
  2. gọi `EnsureAsync` để tái tạo;
  3. gửi lại `vietsub.state` hoặc event ready sau khi hoàn tất.
- Không coi lỗi thumbnail/waveform là lỗi làm hỏng media import hoặc OCR.
- Chống chạy lặp nhiều job FFmpeg cho cùng project/source hash.

Kết quả mong đợi: frontend không nhận trạng thái `READY` sớm hơn khả năng phục vụ file thật.

### Task T4 — Fallback thân thiện ở React

File chính:

- `TOOL-LOCAL/Web/src/features/vietsub/VietsubTimeline.tsx`
- `TOOL-LOCAL/Web/src/styles.css`
- Nếu cần thay message shape: `TOOL-LOCAL/Web/src/features/vietsub/types.ts` và C# bridge phải sửa đồng thời.

Công việc:

- Theo dõi `onLoad`/`onError` cho thumbnail và waveform.
- Khi ảnh lỗi, ẩn biểu tượng ảnh vỡ/alt text mặc định và hiển thị placeholder đúng kích thước track.
- Phân biệt UI:
  - đang tạo artifact;
  - video không có audio;
  - artifact tải thất bại;
  - artifact đã sẵn sàng.
- Cho phép retry đúng một lần sau khi bridge báo artifact ready hoặc URL/revision thay đổi.
- Không tạo vòng retry vô hạn và không tự kích hoạt thêm FFmpeg từ mỗi thẻ `<img>`.
- Giữ chiều cao hàng, lưới thời gian và playhead ổn định dù ảnh đang pending/error.

Lưu ý: fallback chỉ cải thiện trải nghiệm; không được coi là bản sửa hoàn tất nếu request ảnh vẫn lỗi.

### Task T5 — Bổ sung test backend/media

File dự kiến:

- `TOOL-TESTS/Vietsub/VietsubMediaTests.cs`
- Có thể tách file test playback artifact nếu file hiện tại quá lớn.

Các trường hợp bắt buộc:

- 12 URL thumbnail parse đúng và trả `200 image/jpeg`.
- Waveform hợp lệ trả `200 image/png`.
- Đọc được magic bytes JPEG/PNG từ stream response.
- `HEAD` trả header đúng và không gửi body.
- Sai project ID hoặc media ID bị từ chối.
- Context organization/user không khớp không được phục vụ artifact.
- Index thumbnail ngoài khoảng bị từ chối.
- File cache bị xóa trả lỗi artifact-missing rõ ràng.
- Source hash thay đổi không được dùng artifact cũ.
- File hỏng không được trả `200` như ảnh hợp lệ.
- Response và log không chứa absolute path.
- Stream vẫn đọc được sau khi service đã trả kết quả cho caller.

### Task T6 — Bổ sung test frontend

File dự kiến:

- Test component timeline mới hoặc test gần `VietsubTimeline.tsx`.

Các trường hợp:

- URL hợp lệ render đúng số lượng thumbnail.
- `onError` chuyển ảnh sang placeholder, không để browser alt text phá bố cục.
- Waveform `PENDING`, `READY`, `FAILED`, `NO_AUDIO` hiển thị đúng.
- Event ready cho phép ảnh được thử tải lại một lần.
- Thay project/media xóa trạng thái lỗi của project cũ.
- Placeholder không làm thay đổi geometry của ruler/playhead/cue.

### Task T7 — Smoke test WebView2 thật

Phần integration tự động đã hoàn tất: test khởi tạo runtime WebView2 thật, điều hướng trang ở `https://app.local`, chặn request custom host và xác nhận JPEG/PNG phát sinh sự kiện `load`. Phần còn lại dưới đây là smoke test tương tác toàn ứng dụng:

Thực hiện trên project test cục bộ, không gọi AI thật:

1. Mở project có video ngắn và audio.
2. Xác nhận đủ frame thumbnail hiển thị, không có biểu tượng ảnh lỗi.
3. Xác nhận waveform hiển thị trên track Voice gốc.
4. Đóng/mở lại project và xác nhận dùng cache được.
5. Xóa riêng một artifact trong workspace test, mở lại project và xác nhận hệ thống tái tạo.
6. Kiểm tra zoom timeline, resize module và seek video không làm ảnh vỡ lại.
7. Kiểm tra project khác không thể request artifact của project đang mở.
8. Kiểm tra video preview và OCR không bị ảnh hưởng.

Nếu vẫn lỗi, dùng status/mã lỗi mới để khoanh chính xác tầng thất bại; không suy đoán bằng hình ảnh giao diện.

## 6. Thứ tự thực hiện đề xuất

1. T1 — response typed và stream ổn định.
2. T2 — WebView2 handler và chẩn đoán an toàn.
3. T3 — bridge state/artifact lifecycle.
4. T5 — test C# cho route, bảo mật và stream.
5. T4 — fallback/retry có kiểm soát ở React.
6. T6 — test frontend.
7. T7a — integration test WebView2 thật; T7b — smoke test tương tác toàn ứng dụng.

Không bắt đầu bằng việc che ảnh lỗi ở CSS vì cách đó không giải quyết nguyên nhân request thất bại.

## 7. Kiểm tra bắt buộc sau khi triển khai source

Kiểm tra nhanh frontend:

```powershell
Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm test
npm run build
```

Sau đó chạy từ root repository:

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Không ghi nhận mốc test mới nếu chưa thực sự chạy và đạt toàn bộ lệnh trên.

## 8. Tiêu chí hoàn thành

- [ ] Track Video hiển thị frame thật, không còn broken-image icon hoặc alt text tràn trong ô. *(Cần T7b để xác nhận trực quan; fallback và WebView2 load tự động đã có test.)*
- [ ] Track Voice gốc hiển thị waveform thật khi video có audio. *(Cần T7b để xác nhận trực quan; response PNG, UI `READY` và WebView2 load đã có test.)*
- [x] Runtime WebView2 thật tải được JPEG thumbnail và PNG waveform qua custom host với CSP/CORS hiện hành.
- [x] Video không audio hiển thị đúng trạng thái `NO_AUDIO`.
- [x] Mở lại project cũ vẫn hiển thị hoặc tự tái tạo artifact thiếu.
- [x] Lỗi route/context/artifact/stream có status và mã lỗi phân biệt được.
- [x] Không bỏ qua kiểm tra project, organization, user hoặc source hash.
- [x] Không lộ path local, token hay dữ liệu phiên trong DOM, response và log.
- [x] Không tạo vòng retry hoặc nhiều tiến trình FFmpeg trùng lặp.
- [x] Preview video, OCR, subtitle cue, ruler và playhead không bị regression trong toàn bộ suite tự động.
- [x] Test frontend, Release build và toàn bộ `TOOL-TESTS` đạt.

## 9. Các file dự kiến bị tác động khi triển khai

```text
TOOL-LOCAL/Form1.cs
TOOL-LOCAL/Vietsub/Playback/VietsubMediaPlaybackService.cs
TOOL-LOCAL/Vietsub/VietsubWebBridge.cs
TOOL-LOCAL/Web/src/features/vietsub/VietsubTimeline.tsx
TOOL-LOCAL/Web/src/features/vietsub/types.ts                 (nếu đổi contract)
TOOL-LOCAL/Web/src/styles.css
TOOL-TESTS/Vietsub/VietsubMediaTests.cs
TOOL-TESTS/Vietsub/VietsubWebView2MediaIntegrationTests.cs
TOOL-TESTS/TOOL-TESTS.csproj
TOOL-LOCAL/Web/src/features/vietsub/*timeline*.test.*        (test mới hoặc mở rộng)
```

## 10. Ghi chú bàn giao

- Repository đang có nhiều thay đổi chưa commit liên quan Vietsub/OCR/timeline. Khi triển khai phải giữ nguyên thay đổi không liên quan và kiểm tra diff theo từng file.
- Không dùng `git reset`, `git checkout --` hoặc sửa file sinh ra trong `dist`.
- Chưa cần migration database cho task này.
- Không cần và không được gọi OpenAI/Kling thật.
- Ghi chú “chưa triển khai source code” chỉ đúng tại thời điểm tạo task; source và integration test WebView2 đã được triển khai, xác minh ngày 2026-09-04 như mốc ở mục 1.1.
