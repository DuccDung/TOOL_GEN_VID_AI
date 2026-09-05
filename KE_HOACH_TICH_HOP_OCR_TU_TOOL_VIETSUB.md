# Kế hoạch tích hợp OCR từ TOOL_VIETSUB vào VideoMaker

## 1. Thông tin tài liệu

- Ngày khảo sát: `2026-09-04`.
- Trạng thái: đang triển khai; luồng OCR local đã hoạt động ở mức source và automated test, gate đóng gói/phát hành vẫn còn mở.
- Dự án nguồn OCR:
  `D:\laptrinhweb\code_outsrc\TOOL_VIETSUB\TOOL_VIETSUB`.
- Project nguồn đang được solution sử dụng: `TOOL_VIETSUB_APP`.
- Dự án đích:
  `D:\laptrinhweb\code_outsrc\TOOL_AUTO_GEN_POST_VIDEO\Branch-Tool-Sub\TOOL_GEN_VID_AI`.
- Phạm vi: OCR phụ đề cứng chạy local, tạo track phụ đề nguồn để người dùng tiếp tục biên tập và dịch trong workspace Vietsub.

Tài liệu này dùng để lưu context khảo sát, quyết định và checklist triển khai. Source code/migration/package hiện tại là nguồn sự thật kỹ thuật; chỉ tích các mục đã có code và kiểm chứng tương ứng.

### Mốc triển khai 2026-09-04

- Đã có local job engine schema 3, PaddleOCR English/Chinese, FFmpeg raw BGR24, OCR executor/checkpoint, bridge và UI chọn vùng/quét thử/điều khiển job.
- Đã sửa resume để phục hồi cả pending cue accumulator; overlap khi chạy lại không còn cắt cue dài tại checkpoint.
- Khi mở lại project `PROCESSING`, bridge đối soát OCR job đã kết thúc: tự kích hoạt output an toàn hoặc giữ track có bản dịch và yêu cầu xác nhận.
- Runtime probe khởi tạo thật cả model English V5 và Chinese V5 trước khi báo sẵn sàng.
- Gate mới nhất: restore đạt; Release build `0 warning / 0 error`; toàn bộ test .NET `636/636`; OCR integration `4/4`; web test `7/7`.
- Đã đo payload OCR trong output Release khoảng `496.58 MiB`; toàn output Release khoảng `986.74 MiB`. Chưa chốt bundle/optional component, chưa benchmark CPU/RAM và chưa smoke test máy sạch, nên chưa đủ điều kiện phát hành.

## 2. Nguyên tắc tích hợp

“Copy OCR” trong kế hoạch này có nghĩa là tái sử dụng thuật toán, quy tắc nghiệp vụ và các test hữu ích từ `TOOL_VIETSUB_APP`; không copy nguyên ứng dụng hoặc kiến trúc cũ.

Các phần bắt buộc phải viết/adapt lại theo VideoMaker:

- Job phải lưu trong SQLite workspace của Vietsub, không lưu trong project manifest như dự án nguồn.
- FFmpeg phải dùng bundle đã được VideoMaker kiểm tra và phê duyệt.
- WebView message phải dùng namespace `vietsub.*`.
- Track/cue phải dùng `VietsubSubtitleStore` và quy tắc revision hiện tại.
- Phải kiểm tra session, license, tổ chức, membership, role và project context trước khi chạy.
- OCR local không được giả lập thành request OpenAI/Kling và không dùng ngân sách provider.
- UI phải được dựng theo theme VideoMaker; không copy nguyên `MainForm`, layout hoặc CSS của TOOL_VIETSUB.
- Không copy kho credential, HTTP provider client, quota client, FFmpeg downloader hoặc runtime downloader cũ.

## 3. Nguồn sự thật đã khảo sát

### 3.1. Tài liệu VideoMaker

- `README.md`.
- `NGHIEP_VU_HE_THONG_VIDEOMAKER.md`.
- `KE_HOACH_SERVER_AI_GATEWAY.md`.
- `KE_HOACH_TICH_HOP_MODULE_VIETSUB_DOC_LAP.md`.
- `TASK_TRIEN_KHAI_VIETSUB_EDITOR_WORKSPACE.md`.

Theo kế hoạch Vietsub hiện tại:

- E1-E3 đã có nền tảng project, media, subtitle và timeline.
- E4 Local Job Engine đã có trong source và automated test.
- E5 OCR Local đã có luồng chức năng trong source và automated test; legal/package release gate, benchmark và clean-machine smoke test còn mở.
- OCR phải đi sau job engine vì cần progress, cancel, pause, resume, retry và phục hồi khi ứng dụng đóng.

### 3.2. Source OCR của TOOL_VIETSUB_APP

Các file chính đã được đọc và đối chiếu:

- `Core/OcrRegionResolver.cs`.
- `Core/OcrProcessingProfiles.cs`.
- `LocalAi/PaddleOcrRecognizer.cs`.
- `LocalAi/OcrSubtitleLineSegmenter.cs`.
- `LocalAi/OcrCueAccumulator.cs`.
- `Jobs/OcrFrameChangeTracker.cs`.
- `Jobs/OcrJobExecutor.cs`.
- `Jobs/PersistentJobManager.cs`.
- `Jobs/JobStateMachine.cs`.
- `Media/FfmpegOcrFrameReader.cs`.
- `DesktopWorkspaceCoordinator.cs`.
- `ClientApp/src/components/OcrRegionSelector.tsx`.
- Các test OCR trong `TOOL_VIETSUB_APP.Tests`.

Tại thời điểm khảo sát, các file core OCR nói trên không có thay đổi Git chưa commit. Một số file UI khác của dự án nguồn đang có thay đổi cục bộ, vì vậy khi triển khai phải tiếp tục lấy source code và test thực tế làm căn cứ, không dựa vào ảnh giao diện hoặc tài liệu cũ.

## 4. Nghiệp vụ OCR hiện có ở dự án nguồn

### 4.1. Điều kiện bắt đầu

- Project đã có video nguồn hợp lệ.
- File nguồn vẫn tồn tại và không bị thay đổi ngoài dự kiến.
- Ngôn ngữ OCR khi thực thi được resolve thành `en` hoặc `zh`.
- Vùng OCR hợp lệ theo tọa độ chuẩn hóa từ `0` đến `1`.
- PaddleOCR runtime/model và FFmpeg sẵn sàng.

### 4.2. Chọn và xem thử vùng OCR

- Hiển thị frame thật của video tại timestamp do người dùng chọn.
- Cho kéo, di chuyển và resize vùng OCR bằng tám handle.
- Có thể di chuyển vùng bằng bàn phím.
- Lưu vùng bằng `x`, `y`, `width`, `height` chuẩn hóa.
- Hỗ trợ hiệu chỉnh hiển thị khi video có flip hoặc rotation metadata.
- Có thao tác quét thử một frame và trả về text cùng confidence trung bình.
- Vùng mặc định tương thích legacy là 40% phía dưới video.
- Chiều rộng tối thiểu là `0.05`, chiều cao tối thiểu là `0.04`.

### 4.3. Profile xử lý

| Profile | Sample interval | Safety refresh | Max width | Change threshold |
|---|---:|---:|---:|---:|
| `FAST` | 500 ms | 10 giây | 960 px | 0.025 |
| `BALANCED` | 250 ms | 8 giây | 1080 px | 0.015 |
| `ACCURATE` | 200 ms | 4 giây | 1280 px | 0.006 |

Profile và các tham số chiến lược được snapshot vào job. Thay đổi setting sau khi job bắt đầu không được làm thay đổi hành vi của job đó.

### 4.4. Đọc frame bằng FFmpeg

- Kiểm tra FFmpeg trước khi chạy.
- Chỉ map video stream; bỏ audio, subtitle và data stream.
- Crop vùng phụ đề trước, sau đó scale theo profile.
- Đường ưu tiên xuất raw `BGR24` qua stdout, không ghi từng frame ra file tạm.
- Dùng bounded channel để tạo backpressure và giới hạn bộ nhớ.
- Kiểm tra kích thước raw frame chính xác.
- Đọc stderr riêng để không làm nghẽn process.
- Khi cancel phải kill toàn bộ process tree.
- Có xử lý kích thước hiển thị cho video xoay 90 độ.

### 4.5. Nhận dạng PaddleOCR

- Model V5 tiếng Anh cho `en`.
- Model V5 tiếng Trung cho `zh`.
- Chạy CPU bằng OneDNN/MKL.
- Giới hạn số thread dựa trên CPU, trong khoảng 2-12.
- Giữ model nóng bằng recognizer pool.
- Mỗi model được serialize quyền sử dụng để tránh inference đồng thời không an toàn.
- Tối ưu ảnh phụ đề thành một hoặc hai dải chữ trước khi recognize.
- Nếu line segmentation không đủ tin cậy thì fallback về full detection.
- Full detection bỏ kết quả có confidence thấp hơn khoảng `0.45`.
- Fast recognition chỉ chấp nhận trực tiếp khi các dòng đạt khoảng `0.72`; nếu không sẽ fallback.

### 4.6. Tránh OCR frame trùng

- Tạo signature nhị phân tập trung vào vùng chữ phụ đề.
- Yêu cầu thay đổi ổn định qua các frame liên tiếp để tránh nhận nhầm frame chuyển cảnh.
- Nếu frame gần giống frame trước thì tái sử dụng kết quả OCR.
- Vẫn bắt buộc OCR lại sau `safety refresh` để tránh giữ kết quả cũ quá lâu.

### 4.7. Gom kết quả thành cue

- Bỏ text rỗng và kết quả confidence thấp.
- Chuẩn hóa khoảng trắng.
- So sánh nội dung gần giống bằng Levenshtein đã chuẩn hóa.
- Ghép chuỗi kết quả ổn định thành một cue có start/end.
- Một cue được chấp nhận khi có ít nhất hai mẫu hoặc confidence trung bình đủ cao.
- Loại bỏ các flash text đơn lẻ có confidence thấp.

### 4.8. Job, checkpoint và đầu ra

- Job chạy nền và báo progress/ETA.
- Hỗ trợ pause, resume, retry và cancel.
- Job đang `RUNNING` khi ứng dụng đóng sẽ được đánh dấu `INTERRUPTED` khi mở lại.
- Cứ khoảng 15 giây lưu cue hoàn chỉnh và workspace.
- File SRT được ghi vào `.partial`, sau đó atomic move thành file chính thức.
- Lưu metric thời gian xử lý, số frame nhận dạng, số frame reuse và thời gian inference.
- Có bảo vệ cue được khóa khi merge lại dữ liệu của cùng job/track.

### 4.9. Giới hạn đã xác định

- V1 chỉ có model thực thi tiếng Anh và tiếng Trung.
- Tối ưu chủ yếu cho phụ đề cứng nằm ngang, sáng hoặc tương phản rõ.
- OCR chỉ tạo transcript nguồn, không tự động dịch.
- Chế độ Accurate có thể dùng nhiều CPU/RAM.
- Một số integration/benchmark test của dự án nguồn thoát sớm khi thiếu biến môi trường. Test xanh mặc định không phải bằng chứng đầy đủ cho hiệu năng trên video thực.

## 5. Hiện trạng và khoảng cách của VideoMaker

### 5.1. Thành phần đã có

- Workspace Vietsub độc lập.
- Project manifest và thư mục chuẩn cho source, subtitle, cache, temp, logs và output.
- SQLite subtitle store schema hiện tại.
- Import/link video và FFprobe metadata.
- FFmpeg bundle có license, provenance và checksum.
- Track/cue/revision/artifact cho phụ đề.
- Video preview và timeline thumbnail.
- WebView bridge có kiểm tra prefix, request ID và feature flag Vietsub.
- Context user/tổ chức và registry project server.

### 5.2. Thành phần chưa có

- Không có package PaddleOCR/OpenCV trong `TOOL-LOCAL.csproj`.
- Không có OCR runtime hoặc model trong bộ cài.
- Không có recognizer pool.
- Không có frame reader streaming dành cho OCR.
- Không có Vietsub local job engine.
- Không có bảng job/checkpoint/event trong SQLite workspace.
- Không có bridge contract `vietsub.ocr.*` hoặc `vietsub.job.*`.
- Không có dialog chọn vùng/quét thử.
- Card “Nhận dạng & OCR” hiện là placeholder `is-upcoming`.

## 6. Kiến trúc đích đề xuất

### 6.1. Cấu trúc module

```text
TOOL-LOCAL/Vietsub/
├── Jobs/
│   ├── VietsubJobManager.cs
│   ├── VietsubJobStore.cs
│   ├── VietsubJobExecutorRegistry.cs
│   └── VietsubJobRecoveryService.cs
├── Ocr/
│   ├── VietsubOcrJobExecutor.cs
│   ├── PaddleOcrRecognizer.cs
│   ├── PaddleOcrRecognizerPool.cs
│   ├── OcrProcessingProfiles.cs
│   ├── OcrRegionResolver.cs
│   ├── OcrFrameChangeTracker.cs
│   ├── OcrSubtitleLineSegmenter.cs
│   └── OcrCueAccumulator.cs
└── Media/
    └── VietsubOcrFrameReader.cs
```

Tên file cuối cùng có thể điều chỉnh theo convention hiện tại, nhưng trách nhiệm của từng lớp phải được giữ tách biệt.

### 6.2. Luồng dữ liệu đích

```text
Video nguồn chỉ đọc
    -> xác minh path/kích thước/SHA-256
    -> resolve snapshot ngôn ngữ/profile/vùng
    -> FFmpeg crop + scale + raw frame stream
    -> change detection
    -> PaddleOCR local
    -> lọc confidence + gom cue
    -> transaction lưu cue/checkpoint vào project.db
    -> hoàn thành track PADDLE_OCR_LOCAL
    -> ghi SRT bằng .partial + atomic move
    -> activate track sau khi hoàn thành
```

### 6.3. Quy tắc track

- Một lần chạy OCR mới tạo một track mới có source `PADDLE_OCR_LOCAL`.
- Không ghi đè hoặc xóa track OCR của lần chạy trước.
- Retry/resume của cùng job tiếp tục sử dụng track đã gắn với job đó.
- Track mới chỉ được tự động activate khi job hoàn thành thành công.
- Job thất bại/cancelled giữ metadata cần thiết để chẩn đoán hoặc retry, nhưng không thay active track.
- Cue locked không bị thay thế khi retry cùng track.
- Video nguồn không bị sửa, rename hoặc overwrite.

Quy tắc này chủ động khác với việc tái sử dụng một OCR track chung ở source cũ, nhằm bảo toàn lịch sử và tránh mất nội dung người dùng đã chỉnh sửa.

## 7. Dữ liệu và migration dự kiến

### 7.1. SQLite workspace

Tạo migration idempotent tiếp theo, dự kiến schema version 3:

#### `local_jobs`

- `id`.
- `project_id`.
- `type`.
- `status`.
- `created_at`, `started_at`, `updated_at`, `completed_at`.
- `progress_percent`.
- `status_message`.
- `input_track_id`, `output_track_id`.
- `input_revision`.
- `parameters_json`.
- `checkpoint_json`.
- `metrics_json`.
- `attempt_count`, `max_attempts`.
- `error_code`, `error_message`.

#### `local_job_steps`

- Theo dõi các bước `OCR_EXTRACT_FRAMES`, `OCR_RECOGNIZE`, `OCR_BUILD_CUES`, `OCR_WRITE_ARTIFACT`.
- Có trạng thái, progress, timestamp và lỗi riêng.

#### `local_job_events`

- Lưu lịch sử chuyển trạng thái và sự kiện chẩn đoán không nhạy cảm.
- Không lưu toàn bộ OCR text, frame image hoặc đường dẫn tuyệt đối không cần thiết.

### 7.2. Trạng thái job

- `PENDING`.
- `RUNNING`.
- `PAUSING`.
- `PAUSED`.
- `INTERRUPTED`.
- `COMPLETED`.
- `FAILED`.
- `CANCELLED`.

State transition phải được kiểm tra tập trung; UI không được tự suy diễn hoặc tự ghi trạng thái.

### 7.3. Cấu hình project OCR

- `ocrLanguageCode`.
- `ocrProfile`.
- `ocrRegion.x`.
- `ocrRegion.y`.
- `ocrRegion.width`.
- `ocrRegion.height`.
- Có thể giữ `ocrStrategyVersion` trong job snapshot thay vì setting người dùng.

Project cũ chưa có cấu hình OCR phải đọc được với mặc định vùng 40% phía dưới và profile Balanced.

### 7.4. Checkpoint

Checkpoint nên lưu rõ:

- Timestamp/frame index cuối đã commit.
- Cue batch/revision đã commit.
- Trạng thái accumulator cần thiết.
- Strategy version.

Khi resume phải đọc chồng một khoảng nhỏ rồi deduplicate theo timestamp/text để tránh mất cue hoặc tạo cue trùng. Không chỉ suy ra checkpoint từ cue cuối cùng như source cũ.

## 8. Bridge contract và UI dự kiến

### 8.1. Message namespace

- `vietsub.ocr.runtime.status`.
- `vietsub.ocr.region.update`.
- `vietsub.ocr.preview`.
- `vietsub.job.ocr`.
- `vietsub.job.status`.
- `vietsub.job.pause`.
- `vietsub.job.resume`.
- `vietsub.job.retry`.
- `vietsub.job.cancel`.

Mọi request phải có request ID, validate dữ liệu ở native side và trả error code ổn định. Không copy các message cũ như `ocr:preview` hoặc `job:ocr`.

### 8.2. UI

- Card OCR hiển thị trạng thái runtime: chưa cài, sẵn sàng hoặc lỗi.
- Chỉ bật cấu hình OCR khi project có video hợp lệ.
- Dialog chọn vùng dùng frame video thật và tọa độ chuẩn hóa.
- Có chọn timestamp, ngôn ngữ và profile.
- Nút “Quét thử” có busy state độc lập.
- Nút “Bắt đầu OCR” tạo job rồi đóng/thu gọn phần cấu hình phù hợp.
- Hiển thị progress, ETA, pause/resume/cancel/retry.
- Không gửi toàn bộ danh sách cue qua bridge trong mỗi lần progress.
- Khi hoàn thành, refresh danh sách track và activate track đầu ra.
- Nếu người dùng đang có track dịch/voice gắn với active track khác, hiển thị cảnh báo trước khi chuyển nguồn.

## 9. Phân quyền, license và quota

Trước khi preview hoặc bắt đầu OCR phải kiểm tra:

- WebView message đến từ origin tin cậy.
- Session hợp lệ.
- Device/license lease hợp lệ.
- Đã chọn tổ chức.
- User còn là thành viên Active của tổ chức.
- Role được phép phát sinh tác vụ: `Owner`, `OrganizationAdmin`, `BillingManager`, `Member`.
- `Viewer` không được chạy OCR.
- Project thuộc đúng user/tổ chức context.

OCR chạy local và không phát sinh request provider, do đó:

- Không gọi OpenAI/Kling gateway.
- Không copy `QuotaProtectedJobService` hoặc feature quota `ocr.detect` từ TOOL_VIETSUB.
- Không reserve/settle ngân sách provider.
- Nếu sản phẩm cần giới hạn OCR theo gói, phải thiết kế entitlement/local-usage contract riêng ở server.

## 10. Runtime và đóng gói

### 10.1. Package tham khảo từ source

- `Sdcb.PaddleOCR` phiên bản `3.3.1`.
- `Sdcb.PaddleOCR.Models.Local` phiên bản `3.3.1`.
- `Sdcb.PaddleOCR.Models.LocalV5` phiên bản `3.3.1`.
- `Sdcb.PaddleInference.runtime.win64.mkl` phiên bản `3.3.1.70`.
- `OpenCvSharp4.runtime.win` phiên bản `4.11.0.20250507`.

Đây chỉ là baseline để đánh giá, chưa phải quyết định pin version cho VideoMaker.

### 10.2. Gate bắt buộc trước khi thêm package

- Kiểm tra license trực tiếp và transitive dependency.
- Cập nhật third-party notices.
- Kiểm tra checksum/provenance của native runtime/model.
- Đo kích thước publish và bộ cài.
- Xác minh chỉ hỗ trợ kiến trúc đã công bố, trước mắt là `win-x64`.
- Kiểm thử máy sạch không có Visual Studio hoặc runtime phát triển.
- Quyết định bundle sẵn hay component tùy chọn.
- Nếu là component tùy chọn, manifest tải xuống phải được ký, kiểm tra hash và có rollback; không tải tùy ý từ URL ngoài.

Không được đưa package/model vào release trước khi qua gate này.

## 11. Kế hoạch triển khai chi tiết

### Phase OCR-0 — Chốt package và kiến trúc

- [x] Xác nhận phạm vi V1: OCR tiếng Anh và tiếng Trung.
- [x] Chốt profile mặc định.
- [ ] Chốt bundle sẵn hay cài component OCR riêng.
- [ ] Kiểm tra license, notice, native runtime và dung lượng.
- [x] Chốt capability/feature flag riêng cho OCR.
- [x] Chốt giới hạn số job đồng thời.
- [x] Chốt nguyên tắc không tính provider budget cho OCR local.

Điều kiện hoàn thành: có quyết định kỹ thuật/phát hành được ghi nhận trước khi sửa package hoặc setup.

### Phase OCR-1 — Local job engine

- [x] Thiết kế model job, step, event và checkpoint.
- [x] Tạo migration SQLite idempotent.
- [x] Viết job store transaction-safe.
- [x] Viết state machine và kiểm tra transition.
- [x] Viết executor registry theo job type.
- [x] Thêm global semaphore mặc định một heavy job.
- [x] Thêm giới hạn một active heavy job/project.
- [x] Thêm pause, resume, cancel, retry.
- [x] Mark `RUNNING` thành `INTERRUPTED` khi phục hồi workspace.
- [x] Throttle ghi progress để tránh ghi SQLite quá thường xuyên.
- [x] Dừng và dispose job an toàn khi đóng ứng dụng.

Điều kiện hoàn thành: có executor giả lập chạy nền, phục hồi được, không cần PaddleOCR.

### Phase OCR-2 — Setting và domain

- [x] Thêm model normalized OCR region.
- [x] Thêm language/profile vào project setting.
- [x] Thêm migration/default cho project cũ.
- [x] Thêm validation native-side.
- [x] Thêm job parameter snapshot và strategy version.
- [x] Thêm track source `PADDLE_OCR_LOCAL` nếu chưa có enum/value tương ứng.

Điều kiện hoàn thành: project mở cũ/mới đều hợp lệ và snapshot không thay đổi khi setting bị sửa.

### Phase OCR-3 — FFmpeg frame pipeline

- [x] Dùng `IMediaToolPreflightService` và `MediaToolPaths` hiện tại.
- [x] Thiết kế streaming process abstraction phù hợp cho stdout raw frame.
- [x] Port/adapt crop/scale filter và rotation handling.
- [x] Dùng raw `BGR24` là đường ưu tiên.
- [x] Thêm bounded channel/backpressure.
- [x] Giới hạn và làm sạch stderr.
- [x] Validate exact frame size.
- [ ] Kill process tree khi cancel/timeout. *(Đã có kill-tree khi cancel/dispose; còn thiếu timeout riêng và integration test xác nhận process tree.)*
- [x] Không ghi frame tạm ở đường xử lý chuẩn.

Điều kiện hoàn thành: đọc ổn định frame crop từ video ngang, dọc và xoay 90 độ mà chưa cần OCR.

### Phase OCR-4 — PaddleOCR adapter

- [ ] Thêm package sau khi OCR-0 được duyệt. *(Package đã pin trong source, nhưng OCR-0 release gate chưa được duyệt đầy đủ.)*
- [x] Tạo recognizer cho `en` và `zh`.
- [x] Tạo recognizer pool và serialize access theo model.
- [x] Cấu hình OneDNN/MKL và thread limit.
- [x] Port/adapt subtitle line segmenter.
- [x] Thêm full-detection fallback.
- [x] Chuẩn hóa text và filter confidence.
- [x] Dispose native resource sạch khi đóng ứng dụng.
- [x] Trả runtime diagnostics không chứa dữ liệu nhạy cảm.

Điều kiện hoàn thành: quét được ảnh/frame tiếng Anh và tiếng Trung trên máy sạch hỗ trợ.

### Phase OCR-5 — OCR executor và track output

- [x] Xác minh file nguồn và SHA-256 trước khi chạy.
- [x] Tạo/bind output track mới cho job mới.
- [x] Port/adapt frame change tracker.
- [x] Port/adapt cue accumulator.
- [x] Ghi cue theo batch transaction.
- [x] Ghi checkpoint cùng transaction phù hợp.
- [x] Resume có overlap và deduplication.
- [x] Bảo vệ cue locked khi retry cùng track.
- [x] Ghi SRT UTF-8 bằng `.partial` rồi atomic move.
- [x] Thu thập metric và ETA.
- [x] Chỉ activate output track sau khi hoàn thành.
- [x] Không làm thay đổi video nguồn hoặc track cũ.

Điều kiện hoàn thành: job OCR end-to-end tạo track/SRT và phục hồi đúng sau khi bị gián đoạn.

### Phase OCR-6 — Bridge và phân quyền

- [x] Bổ sung request/response contract `vietsub.ocr.*` và `vietsub.job.*`.
- [x] Validate request ID, timestamp, region, language và profile.
- [x] Bổ sung execution authorizer cho local AI job.
- [x] Kiểm tra session, license, org, membership, role và project ownership.
- [x] Chặn Viewer ở native side.
- [x] Chuẩn hóa error code.
- [x] Không trả đường dẫn tuyệt đối hoặc dữ liệu native không cần thiết về WebView.
- [x] Đẩy progress gọn, không đẩy toàn bộ cue list.

Điều kiện hoàn thành: gọi được toàn bộ vòng đời OCR qua bridge, kể cả khi UI gửi dữ liệu không hợp lệ.

### Phase OCR-7 — Giao diện

- [x] Chuyển card OCR khỏi trạng thái placeholder khi runtime/capability sẵn sàng.
- [ ] Thêm dialog chọn vùng trên frame video.
- [x] Thêm drag/resize/keyboard và crop preview.
- [x] Thêm chọn timestamp/ngôn ngữ/profile.
- [x] Thêm quét thử và hiển thị confidence.
- [x] Thêm start/progress/ETA/pause/resume/cancel/retry.
- [x] Thêm trạng thái runtime/model chưa cài hoặc lỗi.
- [x] Cảnh báo ảnh hưởng khi đổi active source track.
- [x] Giữ bố cục responsive và module resize hiện tại.
- [x] Hỗ trợ focus, keyboard và accessibility cơ bản.

Ghi chú: selector vùng hiện nằm trực tiếp trong settings panel thay vì dialog riêng, nên mục dialog vẫn để mở.

Điều kiện hoàn thành: người dùng thực hiện được toàn bộ nghiệp vụ mà không mở màn hình/cửa sổ legacy.

### Phase OCR-8 — Test, benchmark và release

- [x] Unit test region resolver và migration legacy.
- [x] Unit test profiles và parameter snapshot.
- [x] Unit test frame change tracker.
- [x] Unit test line segmenter và cue accumulator.
- [x] Unit test job transitions/recovery/checkpoint.
- [x] Integration test FFmpeg raw frame pipeline.
- [x] Integration test PaddleOCR tiếng Anh và tiếng Trung thật.
- [ ] Test video 16:9, 9:16, xoay 90 độ và video dài.
- [x] Test pause/resume không mất hoặc trùng cue.
- [ ] Test cancel kill process tree.
- [x] Test source hash/file không đổi.
- [x] Test quyền Viewer và sai tổ chức/project.
- [ ] Benchmark CPU, RAM, tốc độ và kích thước publish.
- [x] Không cho integration test âm thầm pass khi thiếu runtime/fixture; phải report skip rõ ràng hoặc chạy trong CI job bắt buộc.
- [ ] Cập nhật installer/updater, notices, checksum và rollback.
- [x] Chạy toàn bộ restore/build/test bắt buộc của solution.

Điều kiện hoàn thành: vượt qua test chức năng, bảo mật, hiệu năng và đóng gói trên máy sạch.

## 12. Error code dự kiến

- `OCR_RUNTIME_NOT_INSTALLED`.
- `OCR_RUNTIME_INVALID`.
- `OCR_LANGUAGE_NOT_SUPPORTED`.
- `OCR_REGION_INVALID`.
- `OCR_VIDEO_NOT_READY`.
- `OCR_SOURCE_CHANGED`.
- `OCR_FRAME_EXTRACTION_FAILED`.
- `OCR_MODEL_LOAD_FAILED`.
- `OCR_INFERENCE_FAILED`.
- `OCR_TEXT_NOT_DETECTED`.
- `OCR_JOB_ALREADY_ACTIVE`.
- `OCR_JOB_NOT_RESUMABLE`.
- `OCR_JOB_CANCELLED`.
- `OCR_ACCESS_DENIED`.
- `OCR_LICENSE_REQUIRED`.

Thông báo UI có thể dịch thân thiện, nhưng logic không được phụ thuộc vào chuỗi thông báo.

## 13. Test matrix tối thiểu

| Nhóm | Trường hợp |
|---|---|
| Ngôn ngữ | English V5, Chinese V5, language không hỗ trợ |
| Video | 16:9, 9:16, rotation 90/270, video ngắn/dài |
| Vùng | mặc định, vùng nhỏ nhất, sát biên, dữ liệu NaN/out-of-range |
| Profile | Fast, Balanced, Accurate |
| Text | một dòng, hai dòng, text giữ nguyên, text đổi nhanh, confidence thấp |
| Lifecycle | start, pause, resume, cancel, retry, app restart |
| Dữ liệu | track cũ, cue locked, source bị thay đổi, SRT partial tồn tại |
| Quyền | Member hợp lệ, Viewer, sai org, project không thuộc context, license hết hạn |
| Runtime | thiếu model, thiếu native DLL, FFmpeg sai checksum, process lỗi |

## 14. Rủi ro và biện pháp

| Rủi ro | Biện pháp |
|---|---|
| Bộ cài tăng mạnh do model/native runtime | Đo size từ Phase OCR-0; cân nhắc signed optional component |
| CPU/RAM cao | Một global heavy job, bounded buffer, profile mặc định Balanced |
| Native DLL không load trên máy sạch | Integration test/publish test trên clean VM |
| Phụ đề ít tương phản nhận dạng kém | Full-detection fallback, profile Accurate, cho preview trước |
| Resume tạo cue trùng/mất cue | Checkpoint transaction, overlap và deduplication |
| UI báo sẵn sàng nhưng runtime lỗi | Native runtime probe là nguồn sự thật |
| Copy nhầm quota/credential kiến trúc cũ | Không port quota client, secret store hoặc provider HTTP client |
| Log lộ nội dung/video path | Chỉ log metric/error code; không log OCR text/frame/path tuyệt đối |
| Test xanh giả do thiếu fixture | Skip phải hiển thị rõ; có CI job OCR thật bắt buộc trước release |

## 15. Ngoài phạm vi V1

- Dịch tự động ngay trong OCR job.
- OCR cloud hoặc gửi frame/video lên server.
- OCR mọi ngôn ngữ PaddleOCR hỗ trợ.
- Text detection cho bố cục tài liệu phức tạp.
- OCR chữ dọc hoặc subtitle xoay tự do.
- Xóa watermark/logo.
- Tự động burn phụ đề vào video.
- Chạy nhiều OCR job nặng song song.

Các hạng mục trên chỉ được mở rộng sau khi V1 ổn định và có yêu cầu nghiệp vụ riêng.

## 16. Definition of Done

OCR chỉ được xem là hoàn thành khi đồng thời đáp ứng:

- Người dùng được phép có thể chọn vùng, quét thử và chạy OCR từ VideoMaker.
- English/Chinese hoạt động trên fixture và video thực đã duyệt.
- Job không khóa UI và hỗ trợ pause/resume/cancel/retry/recovery.
- Track cũ, cue locked và video nguồn không bị ghi đè.
- Kết quả lưu trong SQLite và SRT được ghi atomic.
- Thiếu runtime/model được báo rõ, không crash ứng dụng.
- Viewer, sai tổ chức, sai project hoặc license không hợp lệ bị chặn ở native side.
- Không có provider secret, request OpenAI/Kling hoặc provider budget trong luồng OCR local.
- Installer/updater có license, provenance, checksum và rollback phù hợp.
- Restore, Release build và toàn bộ test solution đạt.
- Có kết quả benchmark CPU/RAM/thời gian và kích thước package được chấp nhận.

## 17. Các quyết định cần chốt trước khi code

1. OCR runtime/model được bundle sẵn hay cài như component tùy chọn?
2. V1 có giữ đúng hai ngôn ngữ English/Chinese hay cần thêm tiếng Việt/khác?
3. Profile mặc định là Balanced hay được cấu hình theo phần cứng?
4. Viewer bị chặn hoàn toàn đúng theo bất biến AI hiện tại hay sản phẩm muốn coi OCR local là thao tác không phát sinh AI? Mặc định kế hoạch này là chặn Viewer.
5. OCR local có entitlement riêng theo gói hay chỉ phụ thuộc license chung?
6. Mức CPU/thread tối đa có cần setting người dùng hay chỉ cấu hình nội bộ?
7. Chính sách giữ lại track/job thất bại và file `.partial` trong bao lâu?

Nếu chưa có quyết định khác, phương án mặc định là: English/Chinese, Balanced, một heavy job toàn ứng dụng, Viewer bị chặn, không trừ provider budget và giữ OCR sau feature/capability flag cho tới khi package gate được duyệt.

## 18. Lệnh xác minh bắt buộc khi triển khai source sau này

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Khi chỉ thay đổi Web UI có thể kiểm tra nhanh:

```powershell
Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
```

Không ghi nhận mốc test mới nếu các lệnh tương ứng chưa thực sự được chạy.
