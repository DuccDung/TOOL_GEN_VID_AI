# Kế hoạch tích hợp module Vietsub độc lập vào VideoMaker

> Trạng thái: **đang triển khai** — Gate 1 đến Gate 5 đã hoàn tất ở mức source và automated test; Gate 6 là điểm tiếp tục hiện tại. Smoke test media/WebView2 thật vẫn phải thực hiện trước release.
>
> Mục tiêu: chuyển đầy đủ logic nghiệp vụ của `TOOL_VIETSUB` vào repository VideoMaker, nhưng giữ Vietsub thành một module độc lập. Hai hệ thống chỉ dùng chung app shell WinForms/WebView2, phiên đăng nhập, tổ chức hiện hành, theme, AI Gateway, updater và những runtime dùng chung đã được phê duyệt.
>
> Nguồn tham chiếu nghiệp vụ và source:
> `D:\laptrinhweb\code_outsrc\TOOL_VIETSUB\TOOL_VIETSUB`.

## 0. Nhật ký triển khai và điểm chuyển giao

### Mốc 2026-09-01 — Baseline và Gate 1

Trạng thái repository trước triển khai:

- VideoMaker không có thay đổi source; chỉ có file kế hoạch này là file mới chưa được Git theo dõi.
- Repository nguồn TOOL_VIETSUB có thay đổi chưa commit của người dùng trong UI và ToolDownloader. Không file nào ở repository nguồn bị sửa bởi đợt tích hợp này.
- Solution nguồn hợp lệ là `TOOL_VIETSUB.slnx`, gồm Server, App, App.Tests và Setup. `SubVid.*`, `ToolDowloader` và code ngoài solution không nằm trong phạm vi copy.

Baseline đã chạy thực tế:

- VideoMaker: restore thành công, Release build `0 warning / 0 error`, test `406/406` đạt.
- TOOL_VIETSUB ở chế độ chỉ đọc source: restore thành công, Release build `0 warning / 0 error`, test `369 đạt / 19 skip / 388 tổng`.
- Inventory nguồn theo khu vực chính: `Core` 16 file, `Jobs` 22, `LocalAi` 19, `Media` 9, `Translation` 8, `Api` 7, `ClientApp` 69. Mapping chi tiết nằm tại mục 8.

Gate 1 đã triển khai:

- Thêm feature flag desktop `Features:VietsubEnabled`; cấu hình mặc định hiện bật để có thể truy cập module.
- Dashboard state chỉ nhận cờ hiển thị; state nghiệp vụ Vietsub nằm trong hook/store riêng tại `Web/src/features/vietsub`.
- Thêm page và sidebar **Dịch phụ đề** theo theme VideoMaker; khi flag tắt, menu và route Vietsub bị ẩn/chuyển về Dashboard.
- Thêm `VietsubWebBridge` riêng. Form1 route toàn bộ prefix `vietsub.*` sang bridge này trước `DashboardBridge`.
- Mọi message Vietsub bắt buộc có request ID; lỗi dùng prefix/code riêng; busy/cancellation token không dùng lock của generation VideoMaker.
- Test mới kiểm tra flag override, bridge boundary, correlation ID, disabled feature và cancellation riêng.

Xác minh sau Gate 1:

- `npm run build`: đạt.
- Restore + Release build toàn solution: `0 warning / 0 error`.
- Test: `412/412` đạt, tăng 6 test so với baseline.

Điểm tiếp tục cho agent kế tiếp:

1. Bắt đầu Gate 2 tại G2-01 với `VietsubAppPaths`; tuyệt đối không dùng `ProjectWorkspaceService` hoặc bảng `vf.Projects` làm nguồn sự thật Vietsub.
2. Nối create/list/open/rename qua `VietsubWebBridge`, không thêm case Vietsub vào `DashboardBridge`.
3. Dùng manifest versioned + SQLite riêng trong workspace; hoàn thành test path/recovery/concurrent lock trước Gate 3.
4. Sau mỗi lát cắt source phải chạy lại ba lệnh restore/build/test ở mục 13 và cập nhật nhật ký này.

### Mốc 2026-09-01 — Gate 2

Đã triển khai workspace và persistence local độc lập:

- Root Vietsub là `<Storage:WorkspaceRoot>/vietsub/projects/{VietsubProjectId}`; không dùng workspace hoặc ID của `vf.Projects`.
- Mỗi project có manifest `project.json` schema version 1 và các thư mục `source`, `audio`, `subtitles`, `voice`, `music`, `cache`, `thumbnails`, `output`, `temp`, `logs`.
- Manifest publish nguyên tử qua `.tmp`, giữ `.bak`, retry khi file tạm bị khóa và tự phục hồi candidate hợp lệ gần nhất.
- `workspace.lock` dùng file lock độc quyền; session khác không thể mở cùng project để ghi.
- Session đánh dấu `LastCleanShutdown=false` khi mở, debounce autosave không chặn UI và trả về `true` khi đóng sạch.
- Subtitle track/cue lưu trong `project.db` bằng SQLite WAL, foreign key, unique order index và timeline index; save dùng transaction, upsert tăng dần và xóa cue stale.
- Bridge hỗ trợ `vietsub.project.create/open/rename/close`; response chỉ có metadata, không trả local path hoặc tên file database.
- UI theo theme VideoMaker đã có form tạo project, danh sách, mở, đổi tên, đóng project và recovery banner.
- Project được lọc đồng thời theo `OrganizationId` và `OwnerUserId`; đổi organization tự đóng session Vietsub đang mở.

Sửa lỗi trong lúc kiểm thử:

- Nhánh recovery ban đầu giữ stream `.bak` mở trong lúc publish manifest chuẩn, làm phục hồi thất bại. Stream hiện được đóng trước atomic publish và có regression test.

Xác minh sau Gate 2:

- Test riêng Vietsub: `13/13` đạt.
- Restore + Release build toàn solution: `0 warning / 0 error`.
- Toàn bộ test: `419/419` đạt.

Điểm tiếp tục hiện tại:

1. Gate 3 bắt đầu từ shared DTO, sau đó migration `vs.Projects`, Server service/controller và cuối cùng desktop API client.
2. Không chạy migration trên database thật. Chỉ thêm migration idempotent và test cấu trúc/source.
3. Local manifest/project.db vẫn là nguồn sự thật media/cue; Server chỉ registry metadata và access/audit.
4. Khi Gate 3 chưa hoàn tất, `ServerSynchronized` phải tiếp tục là `false`; không giả lập đồng bộ thành công.

### Mốc 2026-09-01 — Gate 3

Đã triển khai Server registry và đồng bộ metadata:

- Shared contract mới chỉ chứa project ID, organization, owner, tên, trạng thái, ngôn ngữ và timestamp; không có subtitle text, media hoặc local path.
- Server có domain/service riêng trong `TOOL-SERVER/Vietsub`, DbContext riêng và API `api/vietsub/projects` cho create/list/get/rename/archive.
- Mọi API có `[Authorize]`; device claim được yêu cầu tại controller. Service tái sử dụng `IGenerationAccessService.RequireProjectAccessAsync` để kiểm tra session/device/license lease/organization membership.
- List/get lọc đồng thời organization + owner; cross-user, cross-organization và archived project đều trả not found.
- `Viewer` chỉ được đọc metadata; create/rename/archive bị chặn bằng `vietsub_write_denied`. Không có AI provider hoặc budget call trong registry.
- Create idempotent khi cùng project ID + metadata; payload khác trả `vietsub_project_id_conflict`.
- Audit create/rename/archive được ghi cùng transaction context, chỉ chứa metadata registry.
- Migration idempotent mới là `database/VideoFactory.4.1.0.VietsubProjectRegistry.sql`, tạo schema `vs` và bảng `vs.Projects`. Migration **chưa được chạy trên database thật**.
- Desktop có API client riêng dùng bearer token + license lease, không có API key. Local create/rename giữ thành công khi Server offline; manifest ghi trạng thái chưa đồng bộ và retry khi mở lại.
- Phản hồi bridge chỉ hiển thị trạng thái metadata đã/chờ đồng bộ, không lộ đường dẫn local.

Xác minh sau Gate 3:

- Test riêng Vietsub: `19/19` đạt.
- Restore + Release build toàn solution: `0 warning / 0 error`.
- Toàn bộ test: `425/425` đạt.

Điểm tiếp tục hiện tại:

1. Gate 4 bắt đầu bằng media preflight/import vào workspace local; không gửi video lên Server registry.
2. Reuse `MediaToolPathResolver`, `IMediaToolPreflightService`, FFprobe và process runner hiện tại; không sao chép runtime/download logic FFmpeg từ TOOL_VIETSUB.
3. Playback URL phải nằm dưới virtual host project-scoped, chống traversal và không trả absolute path vào DOM.
4. Chỉ đánh dấu G4 hoàn tất sau test COPY/LINK, source mutation, HTTP Range/seek và thumbnail cache.

### Mốc 2026-09-02 — Gate 4 và Gate 5

Đã hoàn tất lát cắt import/playback media và MVP subtitle/SRT độc lập:

- Gate 4 dùng lại `IMediaToolPreflightService`, `FfprobeService`, FFmpeg path và process runner hiện có; không sao chép downloader/runtime từ TOOL_VIETSUB.
- Import video hỗ trợ COPY/LINK, kiểm tra extension/size/duration/disk, hash SHA-256, file `.partial`, cleanup khi hủy và phát hiện source LINK bị thay đổi.
- Playback đi qua `https://vietsub-media.app.local/projects/{projectId}/media/{mediaId}`, chỉ cho project/session đang mở, hỗ trợ GET/HEAD, HTTP Range, MIME và thumbnail cache project-scoped.
- Gate 5 thêm `Vietsub/Subtitles`, import SRT UTF-8 tối đa 10 MB/20.000 cue, active track, revision và artifact trạng thái `READY`/`STALE`.
- SQLite local được nâng idempotent từ schema 1 lên schema 2, bổ sung quality/warning và subtitle artifact; không có migration SQL Server mới cho nội dung phụ đề.
- Cue editor tải theo trang tối đa 200 cue và thêm giới hạn text 120.000 ký tự mỗi payload để không đẩy toàn bộ track dài qua WebView bridge.
- UI hỗ trợ search/filter status/speaker/text, sửa original/translated debounce, khóa chỉnh tay, split/align/duplicate/delete và seek video theo cue.
- Export SRT original/translated dùng file tạm rồi move nguyên tử; bridge chỉ trả tên file hoàn tất, không trả absolute path vào DOM.

Sửa lỗi trong lúc kiểm thử:

- Upsert cue cũ có thể va unique index `(track_id, cue_index)` khi chèn/xóa làm đổi thứ tự. Transaction hiện tạm giải phóng order index cũ trước khi ghi batch mới và có regression test.
- Message cue trước đây chỉ giới hạn số phần tử; cue có text dài vẫn có thể tạo payload nhiều MB. Paging hiện đồng thời giới hạn số cue và tổng ký tự text.

Xác minh sau Gate 5:

- `npm run build`: đạt.
- Restore + Release build toàn solution: `0 warning / 0 error`.
- Test riêng Vietsub: `42/42` đạt.
- Toàn bộ test: `448/448` đạt.
- Không chạy migration trên database thật, không gọi provider và không phát sinh chi phí.
- Automated media test dùng fake FFmpeg/FFprobe; smoke test video thật, browser seek và visual UI vẫn là hạng mục nghiệm thu môi trường trước release.

Điểm tiếp tục hiện tại:

1. Gate 6 chuyển local job state machine, persistence, checkpoint, pause/cancel/retry và crash recovery.
2. Getter state không được tự khởi động job; progress phải throttle và mọi executor phải nằm trong registry Vietsub riêng.
3. Chưa tích hợp Whisper/OCR/model runtime ở Gate 6; các package đó chỉ bắt đầu sau legal/package review tương ứng.
4. Giữ payload subtitle/media local; không đưa nội dung cue vào `vs.Projects`, audit hoặc Server registry.

Quyết định kiến trúc đã khóa ở Gate 0:

- `VietsubProjectId` là ID độc lập; Server sau này dùng schema `vs`, không tạo project giả trong `vf.Projects`.
- Budget AI dùng chung hard limit tổ chức nhưng được generalize bằng workload kind/id; không tạo một budget Vietsub song song có thể vượt trần.
- V1 hỗ trợ nguồn English/Chinese và đích Vietnamese.
- `vs.Projects` không lưu subtitle text. Payload/kết quả Cloud sau này phải mã hóa, retention cấu hình mặc định 7 ngày; audit/usage chỉ giữ metadata không chứa toàn văn.
- Model/runtime local là optional component có manifest, version, size và SHA-256; không nhét vào base installer trước legal/package review.
- Rollback Gate 1 là tắt `Features:VietsubEnabled`; bridge vẫn từ chối request trực tiếp bằng `vietsub_feature_disabled`.

## 1. Kết quả cuối cùng cần đạt

Trong cùng ứng dụng desktop VideoMaker, sidebar có thêm khu vực **Dịch phụ đề**. Khi người dùng vào khu vực này, họ làm việc trong một hệ thống Vietsub riêng với:

1. Danh sách dự án Vietsub riêng.
2. Workspace và dữ liệu local riêng.
3. Job, checkpoint, cache và lịch sử lỗi riêng.
4. Pipeline nhập video/SRT, nhận dạng, OCR, dịch, kiểm duyệt, tạo giọng, đồng bộ và xuất video riêng.
5. API và dữ liệu Server riêng cho Vietsub.
6. Trạng thái busy, recovery và lỗi không khóa hoặc làm thay đổi workflow tạo video hiện tại.
7. UI được dựng lại hoàn toàn theo theme và component của VideoMaker; không sao chép giao diện, CSS hay bố cục thương hiệu của TOOL_VIETSUB.

Mục tiêu là đạt **feature parity về logic nghiệp vụ**, không phải ghép hai solution hoặc chạy hai ứng dụng trong cùng một process theo kiểu phụ thuộc chéo.

## 2. Nguyên tắc bắt buộc

### 2.1. Tách biệt domain

- Vietsub không sử dụng `vf.Projects`, `vf.Scenes`, scene plan, storyboard, render job hoặc pipeline generation hiện tại làm nguồn sự thật nghiệp vụ.
- Dự án Vietsub không xuất hiện trong danh sách **Dự án của tôi** của VideoMaker hiện tại.
- Dự án VideoMaker không xuất hiện trong danh sách dự án Vietsub, trừ thao tác nhập video có chủ đích như **Lấy video đã xuất từ VideoMaker**.
- Vietsub có namespace, service, bridge contract, API route, database schema và test suite riêng.
- Không tham chiếu project trực tiếp tới solution `TOOL_VIETSUB`; logic được chuyển và thích nghi vào repository hiện tại.
- Không dùng static state hoặc singleton mutable state chung giữa Vietsub và workflow tạo video.

### 2.2. Chỉ dùng chung app shell

Các phần được phép dùng chung:

- Cửa sổ WinForms và WebView2 hiện tại.
- Đăng nhập, refresh token, device identity và license lease.
- Organization selector và thông tin role hiện hành.
- Sidebar, top bar, notification, modal, icon và design tokens của VideoMaker.
- Cơ chế update ứng dụng và ký gói phát hành.
- Bundle FFmpeg/FFprobe đã được VideoMaker kiểm tra và phê duyệt.
- AI Gateway, credential tổ chức, budget period, rate snapshot và audit dùng chung ở cấp hạ tầng.

Việc dùng chung hạ tầng không biến Vietsub thành một phần của workflow tạo video. Mọi request phải mang `workloadKind = Vietsub` hoặc định danh tương đương để log, budget, idempotency và quan sát vận hành không bị trộn.

### 2.3. Gateway-only

- Desktop không nhận, lưu hoặc log API key của OpenAI, Kling hay provider khác.
- Không chuyển `ProtectedTranslationCredentialStore`, kho BYOK hoặc màn hình nhập provider key từ TOOL_VIETSUB sang VideoMaker.
- Dịch Cloud và giọng Cloud phải gọi `TOOL-SERVER` bằng JWT, device claim và organization ID.
- Server chọn credential và model theo cấu hình tổ chức; UI khách hàng không được nhập model ID hoặc base URL tùy ý.
- Không thêm provider/host mới nếu chưa có quyết định nghiệp vụ, rate, allowlist, credential và phê duyệt vận hành tương ứng.

### 2.4. Media local-first

- Video, audio, frame OCR, voice cue và video xuất nằm trên máy người dùng.
- Không tải toàn bộ video/audio lên TOOL-SERVER cho pipeline Vietsub.
- Chỉ nội dung văn bản cần dịch và ngữ cảnh được phép mới gửi tới Server.
- Source video không bao giờ bị ghi đè.
- File output quan trọng phải ghi qua `.partial` rồi move nguyên tử.
- Đường dẫn gửi vào React phải là virtual URL, không phải absolute local path.

### 2.5. Không ảnh hưởng hệ thống hiện tại

- Vietsub không thay đổi hành vi của các page `create`, `longVideo`, `shortVideo`, `projects` và `apiKeys` hiện có.
- Busy state Vietsub chỉ khóa thao tác Vietsub cần thiết; không vô hiệu hóa toàn bộ sidebar hoặc các page VideoMaker khác nếu không có lý do an toàn rõ ràng.
- Job Vietsub lỗi không được cập nhật trạng thái của project VideoMaker.
- Server worker Vietsub có queue, lease và kill switch riêng.
- Có feature flag để tắt Vietsub nhưng các chức năng VideoMaker vẫn chạy bình thường.

## 3. Phạm vi logic phải chuyển đầy đủ

### 3.1. Quản lý dự án Vietsub

- Tạo, mở, đổi tên và liệt kê dự án Vietsub.
- Lưu organization, user tạo, tên, trạng thái và thời điểm cập nhật.
- Workspace lock ngăn hai process chỉnh cùng dự án.
- `LastCleanShutdown` và recovery sau khi app tắt đột ngột.
- Autosave có debounce; save manifest nguyên tử và có backup.
- Không thay video nguồn sau khi đã nhập; muốn thay phải tạo dự án mới hoặc thực hiện workflow thay nguồn được thiết kế riêng.

### 3.2. Nhập và kiểm tra media

- Nhập MP4, MKV, MOV, WEBM.
- Hai chế độ `COPY` và `LINK`.
- FFprobe đọc duration, kích thước, FPS, codec, audio track, bitrate, rotation và VFR.
- SHA-256, kích thước file, dung lượng đĩa và dấu vết thay đổi source.
- Copy qua file `.partial`; cancel phải dọn file tạm an toàn.
- Source đã copy đặt read-only nếu phù hợp.
- Kiểm tra source hash trước OCR, STT và export.

### 3.3. Playback và timeline

- Virtual HTTPS playback có HTTP Range.
- Play, pause, seek, tốc độ phát, volume và current time.
- Timeline cue, playhead, zoom, chọn cue và đồng bộ preview.
- Tách cue, căn cue, nhân bản, xóa và cập nhật timeline.
- Thumbnail timeline theo checkpoint/cache.
- Không lộ đường dẫn local hoặc cho phép path traversal.

### 3.4. Track và cue phụ đề

- Nhiều subtitle track trong cùng dự án.
- Track nguồn từ Whisper, OCR hoặc SRT import tồn tại độc lập.
- Chọn active track và giữ nguyên dữ liệu track cũ.
- Cue có start/end, speaker, original, translated, voice, lock, quality và warning.
- Revision theo track; artifact phụ thuộc track phải stale khi cue thay đổi.
- Cue người dùng đã khóa không bị ghi đè khi chạy lại pipeline.
- SQLite local cho track/cue; không serialize hàng nghìn cue vào WebView state hoặc manifest JSON.

### 3.5. SRT

- Import SRT UTF-8.
- Validate timestamp, cue rỗng, số lượng cue và overlap.
- Sửa câu gốc và câu dịch.
- Export câu gốc hoặc bản dịch.
- Không ghi đè file người dùng nếu chưa xác nhận.
- Chuẩn hóa CRLF/LF và format timestamp ổn định.

### 3.6. Speech-to-Text local

- Tách WAV mono 16 kHz bằng FFmpeg.
- Whisper multilingual và language hint.
- Auto-detect hoặc chọn tiếng Anh/Trung.
- Timestamp theo cue.
- Video dài được chia audio chunk có overlap và checkpoint.
- Resume không tạo cue trùng ở ranh giới chunk.
- Cứ một số cue nhất định phải lưu checkpoint và tiến độ.
- Cue khóa không bị thay đổi khi chạy lại.

### 3.7. OCR local

- Chọn vùng OCR trực tiếp trên video.
- Preview frame và kết quả nhận dạng trước khi chạy toàn bộ.
- PaddleOCR English/Chinese.
- Profile nhanh/cân bằng/chính xác.
- Sampling interval thích nghi theo thời lượng video.
- Frame change detection và reuse kết quả khi hình không đổi.
- Gộp text qua nhiều frame thành cue timeline.
- Checkpoint, resume và metrics OCR.
- Không chạy OCR cả frame nếu vùng phụ đề đã được người dùng xác định.

### 3.8. Dịch local

- English → Vietnamese bằng Argos hoặc engine tương đương đã được phê duyệt.
- Chinese → Vietnamese bằng OPUS-MT hoặc engine tương đương đã được phê duyệt.
- Cài runtime/model theo package có version, size và SHA-256.
- Chỉ cho phép nguồn tải đã ký/allowlist; không tải binary hoặc model tùy ý.
- Translation memory exact match trước khi gọi model.
- Áp dụng glossary sau khi dịch local.
- Cache kết quả theo source fingerprint, model version và configuration fingerprint.
- Không hỗ trợ ngôn ngữ thì báo lỗi rõ ràng, không dịch sai âm thầm.

### 3.9. Dịch Cloud

- Chia cue theo chapter/scene thay vì dịch từng câu rời rạc.
- Có context trước/sau, project summary, character/addressing, style, glossary và memory.
- Server sở hữu system prompt và output schema.
- Alias cue trong prompt để giảm rủi ro model làm sai ID.
- Structured JSON output, đủ cue, không trùng cue, đúng thứ tự mục tiêu.
- Review pass có chọn lọc cho cue confidence thấp, quá dài hoặc có warning.
- Safety repair cho câu lặp, loop từ, rỗng hoặc bất thường.
- Quality validator kiểm tra glossary, tốc độ đọc và pathology.
- Dịch tiếp, dịch lại lỗi và dịch lại toàn bộ.
- Cue đã chỉnh thủ công/khóa vẫn được bảo toàn.
- Job Cloud bền vững: queue, lease, poll, resume, cancel khi chưa outbound, trạng thái `UNKNOWN` khi không chắc usage.
- Idempotency cùng payload trả cùng job; cùng key khác payload phải conflict.
- Mã hóa request/result được lưu tạm trên Server và có retention.

### 3.10. Giọng Việt

- Giữ abstraction một hoặc nhiều voice engine.
- Chọn giọng mặc định và ánh xạ theo speaker.
- Tạo WAV theo cue hoặc phrase.
- Cache theo text, voice, speed và model version.
- Checkpoint provider request nếu engine Cloud bất đồng bộ.
- Local Piper/VieNeu chỉ được đưa vào release sau khi hoàn tất license và package review.
- Logic FPT API key trên desktop không được chuyển. Nếu cần FPT trong tương lai, phải đi qua Server và cần bổ sung provider allowlist/rate/credential riêng.
- Bản đầu có thể dùng local voice đã duyệt hoặc OpenAI Voice thông qua gateway hiện tại nhưng vẫn phải giữ đầy đủ abstraction để không khóa provider.

### 3.11. Đồng bộ voice timeline

- Trim silence đầu/cuối.
- Fit cue bằng tempo, pad, trim và khoảng nghỉ.
- Giới hạn tốc độ đọc trong khoảng cho phép.
- Mượn khoảng trống giữa hai cue trong giới hạn.
- Phát hiện overlap và cue cần người dùng rút gọn.
- Phrase planner cho phép join/break thủ công.
- Artifact timeline mang track ID và revision để chống dùng nhầm dữ liệu cũ.

### 3.12. Âm thanh và xuất video

- Bật/tắt âm gốc, giọng Việt và nhạc nền.
- Điều chỉnh volume từng track.
- Background music loop, trim, fade in/out và ducking.
- Fit voice timeline trước export nếu artifact chưa sẵn sàng.
- Xuất H.264/AAC MP4.
- Soft subtitle hoặc burn-in theo lựa chọn sản phẩm.
- Flip video, style phụ đề và vùng xóa/che phụ đề cứng.
- Output validation: có video stream, audio theo cấu hình, duration hợp lệ và source không đổi.
- Xuất MP4, SRT và transcript.

### 3.13. Job và recovery

- Job state: pending, running, paused, interrupted, completed, failed, cancelled.
- Step state và progress message riêng.
- Pause/cancel chỉ xảy ra tại checkpoint an toàn.
- App khởi động lại chuyển job running thành interrupted, không tự chạy job nặng trên UI startup.
- Resume/retry dùng đúng executor và đúng subtitle track ban đầu.
- Progress, error code và metrics được lưu local.
- Không dùng getter có side effect khởi động worker.
- Không gọi `SemaphoreSlim.Wait()` đồng bộ trên WinForms UI thread.

## 4. Những phần tuyệt đối không sao chép từ TOOL_VIETSUB

- Server đăng nhập và JWT riêng của TOOL_VIETSUB.
- User, plan, subscription, purchase, SePay và trang admin tài chính của TOOL_VIETSUB.
- Database `TOOL_VIETSUB` và các migration lịch sử của dự án mẫu.
- Cơ chế cấp API key cho desktop hoặc BYOK.
- `ProtectedTranslationCredentialStore` và màn hình nhập key Cloud.
- Credential pool theo user của TOOL_VIETSUB.
- Updater, setup và release server riêng của TOOL_VIETSUB.
- CSS, Tailwind config, theme, logo và app shell của TOOL_VIETSUB.
- Batch Mode đã bị gỡ hoặc code legacy không nằm trong solution `TOOL_VIETSUB.slnx` hiện hành.
- Hai project legacy `SubVid.App` và `SubVid.Server` không thuộc solution hiện hành.

## 5. Kiến trúc đích

```text
VideoMaker.exe
├── Shared App Shell
│   ├── Login / session / device / license
│   ├── Organization selector
│   ├── Sidebar / header / theme / notification
│   └── Updater / release runtime
│
├── Existing VideoMaker Module
│   ├── vf projects / scenes / generation
│   ├── current WebView contracts
│   └── current render pipeline
│
└── Vietsub Module
    ├── Vietsub React feature
    ├── Vietsub bridge router
    ├── Local workspace + SQLite
    ├── Local media/STT/OCR/voice jobs
    ├── Subtitle editor + timeline
    └── Vietsub API client
             │
             └── TOOL-SERVER
                 ├── /api/vietsub/*
                 ├── vs schema + durable workers
                 ├── shared organization access
                 ├── shared credential resolver
                 └── shared organization budget/usage
```

## 6. Cấu trúc thư mục dự kiến

Tên cụ thể có thể điều chỉnh khi triển khai, nhưng ranh giới module phải được giữ:

```text
TOOL-LOCAL/
  Vietsub/
    Core/
    Storage/
    Media/
    Playback/
    Jobs/
    Subtitles/
    Recognition/
    Ocr/
    Translation/
    Voice/
    Export/
    Api/
    Bridge/

TOOL-LOCAL/Web/src/
  features/vietsub/
    pages/
    components/
    hooks/
    state/
    bridge/
    types/
    styles/

TOOL-SHARED.Contracts/
  Vietsub/
    VietsubProjectContracts.cs
    VietsubTranslationContracts.cs
    VietsubJobContracts.cs

TOOL-SERVER/
  Vietsub/
    Access/
    Projects/
    Translation/
    Workers/
    Persistence/
    Observability/
  Controllers/
    VietsubProjectsController.cs
    VietsubTranslationsController.cs

TOOL-TESTS/
  Vietsub/
    Unit/
    Integration/
    Security/
    Media/
    UiContracts/

database/
  VideoFactory.<version>.VietsubModule.sql
```

Không được tạo project reference từ VideoMaker sang solution TOOL_VIETSUB bên ngoài repository.

## 7. Mô hình dữ liệu tách biệt

### 7.1. Local workspace

```text
{WorkspaceRoot}/vietsub/projects/{vietsub-project-id}/
  project.json
  project.json.bak
  project.db
  workspace.lock
  source/
  audio/
  subtitles/
  voice/
  music/
  cache/
  thumbnails/
  output/
  temp/
  logs/
```

Yêu cầu:

- Root `vietsub/projects` tách khỏi `projects` của VideoMaker hiện tại.
- `project.json` chỉ lưu metadata nhỏ, settings, job summary và tham chiếu file.
- `project.db` SQLite lưu subtitle tracks/cues và index phục vụ tìm kiếm.
- Mọi relative path phải được resolve dưới đúng Vietsub project root.
- Không cho WebView đọc tùy ý toàn bộ workspace root.

### 7.2. Server schema

Tạo schema riêng, ví dụ `vs`:

- `vs.Projects`: Vietsub project metadata, organization, owner, status.
- `vs.TranslationJobs`: job dịch bền vững.
- `vs.TranslationAttempts`: từng outbound attempt và provider request ID.
- `vs.TranslationPayloads`: payload/result mã hóa hoặc tích hợp trực tiếp vào job nếu giới hạn phù hợp.
- `vs.ProjectAudit`: audit nghiệp vụ Vietsub cần thiết.

Không dùng `vf.Projects`, `vf.ProviderRequests` hoặc `vf.Jobs` làm bảng nghiệp vụ Vietsub.

### 7.3. Dùng chung AI governance mà không trộn workflow

AI governance hiện tại gắn budget reservation và usage ledger với `vf.Projects`. Để Vietsub thực sự độc lập, cần generalize theo workload thay vì tạo project giả trong `vf.Projects`:

- Bổ sung định danh `WorkloadKind` và `WorkloadId` cho budget reservation/usage.
- Giữ `ProjectId` legacy cho VideoMaker trong giai đoạn tương thích ngược.
- Vietsub dùng `WorkloadKind = Vietsub`, `WorkloadId = vs.ProjectId`.
- Operation key phải nằm trong scope organization và prefix Vietsub.
- Budget tổng tổ chức phải tính đồng thời usage VideoMaker và Vietsub.
- Reconciliation worker phải biết đọc request truth từ cả VideoMaker và Vietsub.
- Không duplicate một ngân sách tổ chức thành hai quỹ độc lập nếu nghiệp vụ không yêu cầu.

Migration phải idempotent, tương thích ngược và chỉ được chạy khi người dùng xác nhận môi trường/database/backup.

## 8. Mapping logic từ dự án mẫu sang module mới

| Logic nguồn TOOL_VIETSUB | Đích VideoMaker | Hướng xử lý |
|---|---|---|
| `Core/ProjectModels.cs` | `TOOL-LOCAL/Vietsub/Core` | Chuyển model, đổi namespace và loại bỏ account/plan-specific fields |
| `Core/ProjectWorkspaceService.cs` | `Vietsub/Storage` | Giữ atomic save, backup, lock, recovery; đổi root path |
| `Core/ProjectSubtitleStore.cs` | `Vietsub/Storage` | Chuyển SQLite track/cue store và incremental upsert |
| `Core/SubtitleTrackResolver.cs` | `Vietsub/Subtitles` | Giữ track/revision/artifact rules |
| `Subtitles/SrtService.cs` | `Vietsub/Subtitles` | Chuyển parser, editor operations và exporter |
| `Media/MediaImportService.cs` | `Vietsub/Media` | Dùng media preflight/bundle hiện tại, giữ COPY/LINK/hash |
| `Playback/LocalMediaRange.cs` | `Vietsub/Playback` | Chuyển Range logic vào virtual URL scope dự án |
| `Jobs/PersistentJobManager.cs` | `Vietsub/Jobs` | Chuyển local state machine; loại bỏ mọi startup side effect |
| `Jobs/TranscriptionJobExecutor.cs` | `Vietsub/Recognition` | Chuyển Whisper/chunk/checkpoint |
| `Jobs/OcrJobExecutor.cs` | `Vietsub/Ocr` | Chuyển OCR profile/frame dedup/checkpoint |
| `LocalAi/SpeechRecognition.cs` | `Vietsub/Recognition` | Chuyển adapter Whisper, dùng model package policy mới |
| `LocalAi/PaddleOcrRecognizer.cs` | `Vietsub/Ocr` | Chuyển recognizer English/Chinese |
| `Translation/TranslationContracts.cs` | local + shared contracts | Tách local provider contract và server DTO |
| `Translation/TranslationScenePlanner.cs` | `Vietsub/Translation` | Chuyển đầy đủ chapter/scene/context planning |
| `Translation/TranslationPromptBuilder.cs` | `TOOL-SERVER/Vietsub/Translation` | Server sở hữu prompt; desktop không build Cloud prompt |
| `Translation/TranslationResultCache.cs` | `Vietsub/Translation` | Chuyển cache fingerprint |
| `Translation/LocalTranslationProviderAdapter.cs` | `Vietsub/Translation` | Chuyển local provider + translation memory/glossary |
| `Translation/ServerManagedTranslationProvider.cs` | `Vietsub/Api` | Viết lại để dùng organization JWT/device/gateway hiện tại |
| `Jobs/TranslationJobExecutor.cs` | `Vietsub/Translation` | Chuyển orchestration/apply/quality/restart modes |
| `LocalAi/TranslationQualityValidator.cs` | `Vietsub/Translation` | Chuyển pathology/glossary/length checks |
| `Jobs/Voice*` | `Vietsub/Voice` | Chuyển phrase/cache/timing; thay credential path |
| `Jobs/VideoExportJobExecutor.cs` | `Vietsub/Export` | Dùng FFmpeg bundle hiện tại và workspace riêng |
| `Media/BackgroundMusicService.cs` | `Vietsub/Media` | Chuyển music settings/copy/hash |
| `ClientApp/src/components/*` | `features/vietsub/components` | Chỉ lấy behavior/UX flow; dựng JSX/CSS mới theo VideoMaker |
| `TOOL_VIETSUB/Cloud/Translation/*` | `TOOL-SERVER/Vietsub/Translation` | Chuyển durable job pattern nhưng thay access/quota/credential |
| Tests liên quan pipeline | `TOOL-TESTS/Vietsub` | Chuyển test theo behavior, không copy test database/account cũ |

## 9. Kế hoạch triển khai theo gate

Mỗi gate chỉ hoàn thành khi code, test và tiêu chí nghiệm thu tương ứng đều đạt. Không đánh dấu hoàn thành chỉ vì đã tạo file/class.

### Gate 0 — Baseline và khóa quyết định kiến trúc

- [x] G0-01 Chụp baseline Git status của repository VideoMaker và ghi nhận thay đổi đang có của người dùng.
- [x] G0-02 Chạy baseline restore/build/test bắt buộc của VideoMaker.
- [x] G0-03 Chạy baseline build/test solution TOOL_VIETSUB ở trạng thái read-only nếu môi trường cho phép.
- [x] G0-04 Lập inventory class/file nghiệp vụ trong solution hiện hành `TOOL_VIETSUB.slnx`.
- [x] G0-05 Loại project legacy, code batch đã gỡ và source ngoài solution khỏi phạm vi copy.
- [x] G0-06 Chốt `VietsubProjectId` độc lập với `vf.ProjectId`.
- [x] G0-07 Chốt schema Server `vs` và cách generalize AI budget theo workload.
- [x] G0-08 Chốt retention văn bản dịch trên Server.
- [x] G0-09 Chốt ngôn ngữ V1: English/Chinese → Vietnamese như source hiện tại.
- [x] G0-10 Chốt chính sách package local model và dung lượng installer.
- [ ] G0-11 Hoàn tất legal review cho Whisper, PaddleOCR, OpenCV, Argos/OPUS-MT, Piper/VieNeu và FFmpeg.
- [x] G0-12 Chốt feature flag và rollback boundary.

Điều kiện qua gate: có ADR/thiết kế được duyệt, chưa thay đổi production behavior.

### Gate 1 — Module shell độc lập trong app

- [x] G1-01 Thêm page key Vietsub vào React navigation mà không đổi behavior các page cũ.
- [x] G1-02 Thêm sidebar item **Dịch phụ đề** theo theme VideoMaker.
- [x] G1-03 Tạo feature folder `features/vietsub` và route/render boundary riêng.
- [x] G1-04 Tạo Vietsub bridge router/service riêng thay vì nhét toàn bộ handler vào `DashboardBridge`.
- [x] G1-05 Prefix message `vietsub.*`.
- [x] G1-06 Thêm request ID/correlation ID và error code ổn định.
- [x] G1-07 Tạo state store Vietsub riêng; không dùng `DashboardState` hiện tại làm manifest Vietsub.
- [x] G1-08 Busy state và cancellation token Vietsub riêng.
- [x] G1-09 Feature flag có thể ẩn toàn bộ module.
- [x] G1-10 Test đảm bảo tắt flag không làm thay đổi bundle/page cũ ngoài phần điều hướng dự kiến.

Điều kiện qua gate: mở được trang shell Vietsub trong cùng form; chưa có pipeline; các page cũ hoạt động như baseline.

### Gate 2 — Workspace, project và persistence local

- [x] G2-01 Tạo `VietsubAppPaths` và root riêng.
- [x] G2-02 Tạo manifest versioned.
- [x] G2-03 Tạo project create/list/open/rename.
- [x] G2-04 Tạo workspace directories riêng.
- [x] G2-05 Tạo workspace lock.
- [x] G2-06 Tạo atomic save, `.bak` và recovery candidate.
- [x] G2-07 Tạo SQLite track/cue schema, WAL, foreign key và index.
- [x] G2-08 Tạo incremental upsert, delete stale cue và transaction.
- [x] G2-09 Tạo session autosave không chặn UI thread.
- [x] G2-10 Tạo `LastCleanShutdown` và recovery banner.
- [x] G2-11 Chặn relative path thoát root.
- [x] G2-12 Test Unicode path, concurrent open, corrupted manifest và backup recovery.

Điều kiện qua gate: dự án Vietsub tồn tại độc lập, mở lại giữ nguyên dữ liệu và không tạo bản ghi `vf.Projects`.

### Gate 3 — Server project registry và access riêng

- [x] G3-01 Thêm shared DTO cho Vietsub project trước.
- [x] G3-02 Tạo migration idempotent cho `vs.Projects`.
- [x] G3-03 Tạo API create/list/get/rename/archive Vietsub project.
- [x] G3-04 Xác minh JWT, session, device, license, organization membership và role.
- [x] G3-05 Viewer chỉ đọc metadata được phép, không tạo AI cost.
- [x] G3-06 Ownership theo organization + user/resource policy.
- [x] G3-07 Desktop đồng bộ metadata nhưng local workspace vẫn là nguồn sự thật media/cue.
- [x] G3-08 Không trả path local hoặc content phụ đề trong project list API.
- [x] G3-09 Audit create/rename/archive.
- [x] G3-10 Security test cross-user/cross-organization.

Điều kiện qua gate: Vietsub project được quản lý riêng trên Server và không giả lập bằng project VideoMaker.

### Gate 4 — Import media, playback và thumbnail

- [x] G4-01 Tích hợp media preflight hiện tại.
- [x] G4-02 Import dialog COPY/LINK theo theme VideoMaker.
- [x] G4-03 Validate extension, size, duration và disk space.
- [x] G4-04 FFprobe metadata đầy đủ.
- [x] G4-05 Copy `.partial`, hash và cancel cleanup.
- [x] G4-06 Source mutation detection.
- [x] G4-07 Virtual project-scoped playback URL.
- [x] G4-08 HTTP Range, seek và MIME hợp lệ.
- [x] G4-09 Timeline thumbnails có cache.
- [x] G4-10 Security test traversal, absolute path leak và unauthorized project URL.
- [x] G4-11 Test source byte-for-byte không đổi.

Điều kiện qua gate: nhập và phát được video thật, không lộ path và không tác động workspace VideoMaker.

### Gate 5 — Subtitle track, editor và SRT

- [x] G5-01 Chuyển model track/cue/revision/artifact.
- [x] G5-02 Import SRT UTF-8.
- [x] G5-03 Active track selector.
- [x] G5-04 Danh sách cue virtualized/paginated.
- [x] G5-05 Sửa original/translated với debounce.
- [x] G5-06 Lock original/translation sau chỉnh thủ công.
- [x] G5-07 Search/filter theo status, speaker và text.
- [x] G5-08 Split/align/duplicate/delete cue.
- [x] G5-09 Timeline đồng bộ preview.
- [x] G5-10 Export SRT original/translated.
- [x] G5-11 Test cue count lớn không vượt bridge message limit và không làm lag UI.

Điều kiện qua gate: MVP nhập/sửa/xuất SRT hoạt động độc lập, kể cả chưa có STT/OCR/Cloud.

### Gate 6 — Job engine local và recovery

- [ ] G6-01 Chuyển local job state machine.
- [ ] G6-02 Job/step/event persistence trong workspace Vietsub.
- [ ] G6-03 Progress throttling để không spam bridge.
- [ ] G6-04 Pause/cancel tại checkpoint an toàn.
- [ ] G6-05 Resume/retry theo executor registry Vietsub.
- [ ] G6-06 Running → interrupted sau crash.
- [ ] G6-07 Getter state không khởi động job.
- [ ] G6-08 Không chờ semaphore đồng bộ trên UI thread.
- [ ] G6-09 Global concurrency limit cho local AI nặng.
- [ ] G6-10 Test race, startup recovery, cancellation và app close.

Điều kiện qua gate: job dài không treo app; mở lại dự án có thể tiếp tục từ checkpoint.

### Gate 7 — Whisper STT

- [ ] G7-01 Tích hợp Whisper.net/runtime theo package đã duyệt.
- [ ] G7-02 Model registry, version, size và SHA-256.
- [ ] G7-03 Tách audio 16 kHz mono.
- [ ] G7-04 Auto-detect/hint language.
- [ ] G7-05 Single-file transcription.
- [ ] G7-06 Long-form chunk + overlap.
- [ ] G7-07 Checkpoint cue batch.
- [ ] G7-08 Merge ranh giới không trùng/mất cue.
- [ ] G7-09 Không ghi đè cue khóa.
- [ ] G7-10 SRT artifact và track revision.
- [ ] G7-11 Unit/integration test bằng audio test được phê duyệt.

Điều kiện qua gate: video có audio tạo được track Whisper, resume an toàn và source không đổi.

### Gate 8 — PaddleOCR

- [ ] G8-01 Tích hợp PaddleOCR/OpenCV runtime theo legal/package review.
- [ ] G8-02 UI chọn vùng OCR theo theme VideoMaker.
- [ ] G8-03 Preview frame và text/confidence.
- [ ] G8-04 English/Chinese routing.
- [ ] G8-05 Processing profiles.
- [ ] G8-06 Frame extraction stream.
- [ ] G8-07 Change detection/reuse.
- [ ] G8-08 Cue accumulator/segmenter.
- [ ] G8-09 Checkpoint/resume/metrics.
- [ ] G8-10 Không ghi đè cue khóa.
- [ ] G8-11 Benchmark video ngắn/dài và giới hạn RAM.

Điều kiện qua gate: phụ đề cứng tạo được track OCR chính xác trong vùng chọn và không block UI.

### Gate 9 — Translation core và local translation

- [ ] G9-01 Chuyển translation contracts nội bộ.
- [ ] G9-02 Chuyển scene/chapter planner.
- [ ] G9-03 Chuyển configuration/cue fingerprint.
- [ ] G9-04 Chuyển result cache.
- [ ] G9-05 Chuyển translation memory exact/context matching.
- [ ] G9-06 Chuyển glossary application.
- [ ] G9-07 Chuyển local provider adapter.
- [ ] G9-08 Package English/Chinese local engines theo chính sách đã duyệt.
- [ ] G9-09 Chuyển quality validator và pathology detection.
- [ ] G9-10 Dịch tiếp/dịch lại lỗi/dịch lại toàn bộ.
- [ ] G9-11 Không ghi đè cue khóa hoặc cue đã thay đổi sau khi job bắt đầu.
- [ ] G9-12 Cache invalidation theo model/config/source.

Điều kiện qua gate: dịch local đạt behavior parity cho English/Chinese → Vietnamese.

### Gate 10 — Cloud translation qua AI Gateway tổ chức

- [ ] G10-01 Tạo shared Cloud translation DTO không có API key/provider URL.
- [ ] G10-02 Tạo migration `vs.TranslationJobs` và attempt/result retention.
- [ ] G10-03 Generalize AI budget/usage theo workload, tương thích ngược.
- [ ] G10-04 Tạo Vietsub access service kiểm tra project riêng.
- [ ] G10-05 Server chọn OpenAI text credential/model của tổ chức.
- [ ] G10-06 Server sở hữu prompt builder/output schema.
- [ ] G10-07 Request normalization và giới hạn cue/character/token.
- [ ] G10-08 Request hash + organization-scoped idempotency.
- [ ] G10-09 Tạo job và budget reservation trong transaction Serializable.
- [ ] G10-10 Worker claim bằng lease và recovery sau restart.
- [ ] G10-11 Provider call, structured output validation và usage normalization.
- [ ] G10-12 Review pass/safety repair có giới hạn.
- [ ] G10-13 Settle actual cost bằng rate snapshot.
- [ ] G10-14 Release khi chắc chắn chưa outbound; `UNKNOWN` khi kết quả/usage không chắc chắn.
- [ ] G10-15 Poll bằng backoff; resume polling khi mở lại app.
- [ ] G10-16 Cancel chỉ khi Server xác nhận chưa outbound.
- [ ] G10-17 Mã hóa payload/result và cleanup retention.
- [ ] G10-18 Kill switch, rate limit và maximum concurrent jobs riêng.
- [ ] G10-19 Audit/metric không chứa subtitle text hoặc secret nếu không cần thiết.
- [ ] G10-20 Security/integration test concurrency, idempotency, budget và restart.

Điều kiện qua gate: Cloud translation hoạt động mà desktop không nhận key, budget tổ chức chính xác và job sống qua app/server restart.

### Gate 11 — Voice generation

- [ ] G11-01 Chuyển voice catalog/engine abstraction.
- [ ] G11-02 Chuyển cue/phrase planner.
- [ ] G11-03 Chuyển voice cache/fingerprint/checkpoint.
- [ ] G11-04 Tích hợp local voice engine được phê duyệt.
- [ ] G11-05 Tích hợp OpenAI Voice Server nếu dùng Cloud.
- [ ] G11-06 Speaker → voice mapping.
- [ ] G11-07 Voice preview không lưu secret.
- [ ] G11-08 Validate WAV và artifact track revision.
- [ ] G11-09 Pause/resume/retry và partial cleanup.
- [ ] G11-10 Test cache, changed text, changed voice và stale artifact.

Điều kiện qua gate: tạo giọng Việt cho cue/phrase, không dùng key desktop và không dùng artifact sai revision.

### Gate 12 — Voice timeline, audio mix và subtitle styling

- [ ] G12-01 Chuyển trim silence và voice activity analysis.
- [ ] G12-02 Chuyển tempo/pad/borrow-gap fit policy.
- [ ] G12-03 Chuyển phrase boundary auto/join/break.
- [ ] G12-04 Cảnh báo cue quá dài, không ép tốc độ vượt ngưỡng.
- [ ] G12-05 Tạo voice timeline WAV nguyên tử.
- [ ] G12-06 Original audio volume/toggle.
- [ ] G12-07 Vietnamese voice volume/toggle.
- [ ] G12-08 Background music copy/hash/loop/trim/fade/duck.
- [ ] G12-09 Subtitle style presets theo theme UI VideoMaker nhưng output behavior tương đương.
- [ ] G12-10 Vùng blur/cover phụ đề cứng và video flip.
- [ ] G12-11 Test overlap, duration, stale timeline và audio quality.

Điều kiện qua gate: preview và timeline phản ánh đúng output dự kiến, cue cần sửa được cảnh báo rõ.

### Gate 13 — Export MP4/SRT/transcript

- [ ] G13-01 Chuyển export orchestrator.
- [ ] G13-02 Reuse FFmpeg/FFprobe bundle và preflight VideoMaker.
- [ ] G13-03 Build/reuse voice timeline đúng revision.
- [ ] G13-04 Mix original/voice/music theo settings.
- [ ] G13-05 Soft subtitle hoặc burn-in theo quyết định sản phẩm.
- [ ] G13-06 Export `.partial` và move nguyên tử.
- [ ] G13-07 Destination picker và chống ghi đè ngoài ý muốn.
- [ ] G13-08 Validate output stream, duration, resolution, audio và subtitle.
- [ ] G13-09 Ghi artifact metadata/hash local.
- [ ] G13-10 Full pipeline integration test với media fixture.

Điều kiện qua gate: tạo được MP4/SRT/transcript hợp lệ, source byte-for-byte không đổi.

### Gate 14 — UI/UX hoàn thiện theo theme VideoMaker

- [ ] G14-01 Trang danh sách dự án Vietsub riêng.
- [ ] G14-02 Empty/loading/error/recovery states.
- [ ] G14-03 Editor ba khu vực: settings, preview, cue inspector.
- [ ] G14-04 Timeline phía dưới.
- [ ] G14-05 Stepper: Nguồn → Nhận dạng → Dịch → Kiểm tra → Giọng → Xuất.
- [ ] G14-06 Responsive layout theo kích thước cửa sổ app hiện tại.
- [ ] G14-07 Keyboard navigation và shortcut.
- [ ] G14-08 Accessible labels/focus/reduced motion.
- [ ] G14-09 Virtualized cue list cho video dài.
- [ ] G14-10 Progress/ETA/metrics dễ hiểu cho khách hàng, không lộ provider internals.
- [ ] G14-11 Error mapping thành hướng xử lý cụ thể.
- [ ] G14-12 Visual regression bằng screenshot các viewport chính.
- [ ] G14-13 Không import Tailwind/theme/CSS từ app mẫu nếu VideoMaker không dùng chúng.

Điều kiện qua gate: UI nhìn và hoạt động như một phần tự nhiên của VideoMaker nhưng dữ liệu/module vẫn độc lập.

### Gate 15 — Hardening, quan sát và release

- [ ] G15-01 Feature flag server và desktop.
- [ ] G15-02 Kill switch Cloud translation riêng.
- [ ] G15-03 Log correlation: UI request, local job, server job, provider request, budget reservation.
- [ ] G15-04 Không log token, key, authorization header hoặc toàn bộ subtitle payload.
- [ ] G15-05 Soak test UI/job với video dài.
- [ ] G15-06 Memory/CPU/disk benchmark Whisper/OCR/local translation/voice.
- [ ] G15-07 Test app close/reopen, server offline/restart và mất mạng.
- [ ] G15-08 Test song song Vietsub với workflow VideoMaker.
- [ ] G15-09 Xác nhận organization selector không đổi tenant giữa một request đang chạy.
- [ ] G15-10 Package optional local AI components, license và checksum.
- [ ] G15-11 Canary nội bộ, đo lỗi/chi phí/queue latency.
- [ ] G15-12 Rollback bằng feature flag không làm mất job đang xử lý.
- [ ] G15-13 Chỉ publish khi được người dùng cho phép rõ môi trường và tác động.

Điều kiện qua gate: module đạt tiêu chí bảo mật, ổn định, không regression và có rollback vận hành.

## 10. Thiết kế API dự kiến

Tên route cuối cùng có thể đổi sau khi chốt contract.

### 10.1. Vietsub project

```text
POST   /api/vietsub/projects
GET    /api/vietsub/projects
GET    /api/vietsub/projects/{vietsubProjectId}
PATCH  /api/vietsub/projects/{vietsubProjectId}/name
POST   /api/vietsub/projects/{vietsubProjectId}/archive
```

Project API chỉ quản lý metadata/ownership, không upload video hay đồng bộ toàn bộ cue.

### 10.2. Cloud translation

```text
POST   /api/vietsub/projects/{vietsubProjectId}/translations
GET    /api/vietsub/translations/{translationJobId}
GET    /api/vietsub/translations/by-request/{requestId}
POST   /api/vietsub/translations/{translationJobId}/cancel
```

Request tạo job gồm:

- `requestId` và idempotency metadata.
- `organizationId`.
- Vietsub project ID và local job ID.
- Source/target language.
- Project summary, nhân vật/xưng hô, style và chapter context trong giới hạn.
- Glossary và translation memory đã lọc.
- Cue target + cue context.
- Translation pass và run mode trong allowlist.

Request không gồm:

- API key.
- Provider base URL.
- Plaintext credential.
- Absolute local path.
- Video/audio/frame.
- Model tùy ý ngoài policy Server.

## 11. Thiết kế WebView bridge

Message prefix đề xuất:

```text
vietsub.state.get
vietsub.project.list
vietsub.project.create
vietsub.project.open
vietsub.project.rename
vietsub.media.import
vietsub.media.import.cancel
vietsub.subtitle.track.activate
vietsub.subtitle.srt.import
vietsub.subtitle.srt.export
vietsub.subtitle.update
vietsub.timeline.split
vietsub.timeline.align
vietsub.timeline.duplicate
vietsub.timeline.delete
vietsub.ocr.preview
vietsub.job.transcribe
vietsub.job.ocr
vietsub.job.translate
vietsub.job.voice
vietsub.job.export
vietsub.job.pause
vietsub.job.resume
vietsub.job.retry
vietsub.job.cancel
```

Quy tắc:

- C# validate toàn bộ payload.
- Không gửi toàn bộ hàng nghìn cue trong một message.
- Dùng page/cursor hoặc window quanh playhead.
- Update một cue dùng concurrency/revision token.
- Progress update được throttle/coalesce.
- Mỗi modal operation có trạng thái độc lập.
- Bridge handler không giữ UI thread trong suốt job dài; chỉ enqueue/start và trả state.

## 12. Ma trận kiểm thử bắt buộc

### 12.1. Unit

- Project path normalization và traversal.
- Manifest atomic save/backup/recovery.
- SQLite track/cue incremental persistence.
- SRT parse/export/timestamp/UTF-8.
- Subtitle track revision và stale artifact.
- Job state transition.
- Chunk/overlap merge của Whisper.
- OCR frame dedup và cue accumulation.
- Scene planner/context/glossary/memory.
- Translation fingerprint/cache/restart mode.
- Quality/pathology/length validator.
- Voice phrase/timing fit.
- FFmpeg argument composition.

### 12.2. Integration

- Import media thật bằng FFprobe/FFmpeg.
- Playback Range.
- Whisper fixture.
- OCR fixture.
- Local translation fixture.
- Voice fixture.
- Full local pipeline MP4 → MP4/SRT.
- Source hash không đổi.
- Server create/poll/cancel/recover translation job.
- Budget reserve/settle/release/unknown.
- Worker lease và restart.

### 12.3. Security

- Không response/log/bundle nào chứa provider API key.
- Cross-user/cross-organization bị từ chối.
- Viewer không tạo AI cost.
- Device/session/license invalid bị từ chối.
- Project Vietsub không tồn tại không được ánh xạ sang project VideoMaker.
- Payload quá lớn, cue quá nhiều và ký tự control bị chặn.
- Provider/model/base URL ngoài policy bị chặn.
- Request ID conflict bị phát hiện.
- Virtual media URL không traversal/lộ path.
- Encrypted payload không đọc được khi không có Data Protection key đúng.

### 12.4. Regression

- Tạo video dài hiện tại.
- Tạo video ngắn hiện tại.
- Project list VideoMaker hiện tại.
- Organization switching.
- Content/image/voice/video generation hiện tại.
- Render final video hiện tại.
- Login/logout/session invalidation.
- Desktop update.

### 12.5. UI/concurrency

- Cue list lớn vẫn cuộn và edit mượt.
- Job chạy không khóa title bar/sidebar/logout.
- Không deadlock khi app mở với job interrupted.
- Nhiều progress message không làm WebView lag.
- Đóng modal/import/export giữa chừng không rò handle/process.
- Đổi track trong khi job active bị chặn hoặc cảnh báo đúng.

## 13. Lệnh xác minh trong mỗi đợt source change

Từ root VideoMaker:

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Khi sửa React/WebView UI:

```powershell
Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
```

Integration test dùng model/media thật phải opt-in bằng biến môi trường và không được gọi provider có phí nếu chưa có phê duyệt.

## 14. Rủi ro chính và cách kiểm soát

| Rủi ro | Tác động | Kiểm soát |
|---|---|---|
| Copy nguyên server TOOL_VIETSUB | Phá kiến trúc organization gateway | Chỉ chuyển domain logic, viết lại access/quota/credential |
| Dùng chung `vf.Projects` | Trộn danh sách/trạng thái hai hệ thống | `vs.Projects` và Vietsub project ID riêng |
| Local AI quá nặng | Installer lớn, cài chậm, thiếu ổ đĩa | Optional signed component, preflight dung lượng, tải theo nhu cầu |
| License Piper/FFmpeg/model | Rủi ro phát hành | Legal gate và full notices/source obligations |
| WebView nhận toàn bộ cue | Lag/memory/message quá lớn | SQLite, pagination, virtualization, delta updates |
| Long job chạy trong bridge | UI treo/deadlock | Enqueue + background executor, async only, no sync wait |
| App đóng giữa Cloud call | Không rõ cost/result | Durable Server job + request ID + polling resume + UNKNOWN |
| Cue thay đổi giữa job | Ghi đè chỉnh sửa người dùng | Source fingerprint + lock + optimistic revision |
| Track cũ dùng cho voice/export | Output sai | Track ID/revision trên mọi artifact |
| Budget tách thành hai quỹ | Vượt ngân sách tổ chức | Generalize shared budget theo workload, Serializable |
| Provider key ra desktop | Lộ secret | Server-managed only và security tests |
| Regression workflow hiện tại | Ảnh hưởng khách hàng cũ | Module boundary, feature flag, regression suite/canary |

## 15. Rollback

- Desktop feature flag ẩn page và dừng nhận job Vietsub mới.
- Server feature flag từ chối create job mới nhưng worker tiếp tục drain job đã nhận.
- Migration chỉ thêm schema/column/index tương thích ngược trong giai đoạn đầu.
- Không rollback bằng cách chuyển Cloud provider key về desktop.
- Local workspace Vietsub được giữ nguyên khi tắt module để người dùng có thể phục hồi sau.
- Không xóa dữ liệu `vs` hoặc local workspace trong rollback tự động.
- Release cũ vẫn chạy workflow VideoMaker hiện tại vì không phụ thuộc assembly/module Vietsub để khởi động.

## 16. Tiêu chí nghiệm thu cuối cùng

- [ ] Vietsub nằm trong cùng form/app shell và dùng đúng theme VideoMaker.
- [ ] Không sao chép UI/theme của TOOL_VIETSUB.
- [ ] Dự án, dữ liệu, job, API và workspace Vietsub độc lập.
- [ ] Không có project Vietsub trong `vf.Projects` hoặc danh sách project VideoMaker.
- [ ] Import video/SRT, Whisper, OCR, dịch, kiểm duyệt, voice, timeline và export đạt feature parity đã chốt.
- [ ] Cue khóa/chỉnh thủ công không bị ghi đè.
- [ ] Job local và Cloud resume được sau app/server restart.
- [ ] Desktop không nhận/lưu/log provider key.
- [ ] Video/audio không được gửi lên Server trong pipeline local.
- [ ] Organization access, role, project ownership và budget được kiểm tra đầy đủ.
- [ ] Budget VideoMaker + Vietsub không vượt hard limit khi chạy đồng thời.
- [ ] Không lộ local path hoặc cho phép traversal qua WebView.
- [ ] Source video byte-for-byte không đổi.
- [ ] Output MP4/SRT/transcript được validate.
- [ ] UI không deadlock/lag với video dài và cue count lớn trong giới hạn sản phẩm.
- [ ] Tắt module Vietsub không ảnh hưởng hệ thống hiện tại.
- [ ] Toàn bộ build/test/regression/security đạt.
- [ ] License, package, checksum, updater và rollback được nghiệm thu trước release.

## 17. Thứ tự triển khai khuyến nghị

```text
Baseline + ADR
  → Module shell
  → Workspace/SQLite
  → Server Vietsub project registry
  → Import/playback
  → Track/editor/SRT
  → Local job engine
  → Whisper
  → OCR
  → Translation core/local
  → Cloud translation gateway
  → Voice
  → Timeline/audio/style
  → Export
  → UI polish
  → Security/performance/regression
  → Canary/release
```

Không triển khai Cloud provider trước access/budget/idempotency. Không triển khai voice/export trước khi track revision và artifact invalidation ổn định. Không publish model/runtime trước legal/package review.

## 18. Ghi chú phạm vi tài liệu

File này vừa là kế hoạch vừa là nhật ký chuyển giao triển khai. Không tự tải model, chạy provider, áp dụng database thật hoặc publish release; mọi thay đổi tiếp theo phải tuân thủ đầy đủ `AGENTS.md` của repository và cập nhật mục 0 sau mỗi gate/lát cắt hoàn tất.
