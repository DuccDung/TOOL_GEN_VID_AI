# Task triển khai Vietsub Editor Workspace đầy đủ

> **Task ID:** `VSE-EDITOR-001`  
> **Trạng thái:** `IN_PROGRESS` — đã hoàn tất E1–E3; E2.4 mở rộng thumbnail và E4 trở đi còn mở  
> **Ngày lập:** 2026-09-02  
> **Repository đích:** `TOOL_GEN_VID_AI`  
> **Nguồn tham chiếu chỉ đọc:** `D:\laptrinhweb\code_outsrc\TOOL_VIETSUB\TOOL_VIETSUB`  
> **Kế hoạch cha:** `KE_HOACH_TICH_HOP_MODULE_VIETSUB_DOC_LAP.md`  
> **Điểm bắt đầu kỹ thuật:** Gate 1–5 của kế hoạch cha và editor E1–E3 đã có trong source; Gate 6/E4 trở đi còn mở.

Tài liệu này là task bàn giao để một agent hoặc developer khác có thể tiếp tục triển khai khi không còn ngữ cảnh hội thoại. Tài liệu tập trung vào trải nghiệm sau khi tạo/mở dự án: người dùng phải vào một editor chuyên dụng có preview, cue inspector, timeline, OCR, nhận dạng, dịch, voice và export; không tiếp tục làm việc trong trang danh sách dự án dạng cuộn dọc hiện tại.

Task này **không cho phép copy nguyên solution TOOL_VIETSUB**, không cho phép đưa API key xuống desktop và không thay thế kiến trúc tổ chức/AI Gateway hiện hành của VideoMaker.

---

## 1. Kết quả người dùng cần nhận được

Luồng cuối phải là:

```text
Sidebar “Dịch phụ đề”
  -> Thư viện dự án Vietsub
       -> Tạo dự án hoặc mở dự án
            -> Vietsub Editor Workspace chuyên dụng
                 ├─ Header dự án và trạng thái job
                 ├─ Thiết lập/nhận dạng/dịch bên trái
                 ├─ Preview video ở giữa
                 ├─ Cue inspector/phụ đề bên phải
                 └─ Timeline tương tác ở dưới
```

### 1.1. Hành vi bắt buộc khi tạo hoặc mở dự án

- Sau khi `vietsub.project.create` thành công và host trả `selectedProject`, UI chuyển ngay sang editor.
- Sau khi `vietsub.project.open` thành công, UI chuyển ngay sang editor và phục hồi video, active subtitle track, cue, artifact và job gần nhất.
- Khi editor đang mở, không hiển thị đồng thời form tạo dự án và danh sách dự án ở phía trên.
- Nút **Quay lại dự án** hoặc **Đóng dự án** phải flush thay đổi đang debounce, đóng session sạch rồi trở về thư viện.
- Nếu dự án chưa có video, editor vẫn mở nhưng hiển thị empty state nhập video ngay trong vùng preview.
- Nếu project bị recovery, cảnh báo nằm trong editor header và không đẩy toàn bộ editor xuống dưới một trang dài.
- Đổi organization phải đóng session Vietsub đang mở và trở về thư viện của organization mới.

### 1.2. Bố cục editor mục tiêu

Desktop rộng:

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ ← Dự án | Tên dự án | trạng thái lưu | job/progress | Import | Export       │
├─────────────────┬──────────────────────────────┬─────────────────────────────┤
│ Thiết lập       │ Preview video                │ Phụ đề / cue inspector      │
│ - Nguồn         │ - video thật                 │ - track selector            │
│ - OCR/STT       │ - overlay phụ đề             │ - search/filter             │
│ - Dịch          │ - play/pause/seek/rate       │ - original/translated       │
│ - Voice         │ - chọn vùng OCR              │ - speaker/quality/warning   │
│ - Output        │                              │ - lock/save                 │
├─────────────────┴──────────────────────────────┴─────────────────────────────┤
│ Timeline: ruler + playhead + thumbnail + subtitle + audio/voice/music tracks│
└──────────────────────────────────────────────────────────────────────────────┘
```

Quy tắc responsive:

- Từ 1180 px trở lên: ba cột và timeline dưới.
- Từ 900–1179 px: settings thu gọn thành tab/drawer, preview và cue inspector còn hai cột.
- Dưới 900 px: một vùng chính theo tab `Preview | Phụ đề | Thiết lập`, timeline vẫn neo dưới và có thể thu gọn.
- Không để editor trở lại dạng nhiều card nối tiếp theo chiều dọc.
- Panel size có thể lưu ở frontend theo project/version, nhưng không lưu local path, token hoặc secret vào `localStorage`.
- Detach panel như TOOL_VIETSUB là hạng mục nâng cao, không phải điều kiện của lát cắt editor đầu tiên.

---

## 2. Hiện trạng đã xác minh trong repository đích

### 2.1. Phần đã có và phải tái sử dụng

| Năng lực | File chính | Trạng thái |
|---|---|---|
| Feature flag và sidebar Vietsub | `TOOL-LOCAL/Configuration/DesktopOptions.cs`, `TOOL-LOCAL/Web/src/App.tsx` | Đã có |
| Bridge riêng, request ID, busy và cancel | `TOOL-LOCAL/Vietsub/VietsubWebBridge.cs` | Đã có |
| Project local độc lập theo organization/user | `TOOL-LOCAL/Vietsub/Storage/VietsubProjectStore.cs` | Đã có |
| Session lock, autosave, recovery | `TOOL-LOCAL/Vietsub/Storage/VietsubProjectSession.cs` | Đã có |
| Server registry metadata `vs.Projects` | `TOOL-SERVER/Vietsub`, `TOOL-SHARED.Contracts/Vietsub` | Đã có trong source; migration thật không mặc định đã chạy |
| Import video COPY/LINK, hash, FFprobe | `TOOL-LOCAL/Vietsub/Media/VietsubMediaImportService.cs` | Đã có |
| Virtual HTTPS playback và HTTP Range | `TOOL-LOCAL/Vietsub/Playback/VietsubMediaPlaybackService.cs` | Đã có |
| 12 thumbnail timeline cơ bản | `TOOL-LOCAL/Vietsub/Media/VietsubTimelineThumbnailService.cs` | Đã có |
| SQLite WAL cho subtitle track/cue/artifact | `TOOL-LOCAL/Vietsub/Storage/VietsubSubtitleStore.cs` | Đã có, schema 2 |
| Import/export SRT, paging, search/filter | `TOOL-LOCAL/Vietsub/Subtitles/VietsubSubtitleService.cs` | Đã có |
| Sửa cue, split, align, duplicate, delete | `VietsubSubtitleService.cs`, `VietsubSubtitleEditor.tsx` | Đã có |
| Đồng bộ playhead giữa video và cue editor | `VietsubPage.tsx` | Có ở mức cơ bản |

Không được làm lại các năng lực trên bằng một store/project model khác.

### 2.2. Vấn đề bố cục hiện tại

`TOOL-LOCAL/Web/src/features/vietsub/VietsubPage.tsx` hiện render cùng lúc:

1. hero;
2. form tạo dự án;
3. danh sách dự án;
4. selected project;
5. import video;
6. preview/video metadata;
7. subtitle editor;
8. các card mô tả workflow.

Khi project đã mở, người dùng vẫn thấy toàn bộ trang quản lý phía trên và phải cuộn xuống để làm việc. Đây là nguyên nhân UX chính. Việc sửa không chỉ là đổi CSS; phải tách state/view **library** và **editor**.

### 2.3. Năng lực còn thiếu

- Editor shell riêng và điều hướng library/editor.
- Playback controls đồng bộ tập trung, phím tắt và overlay subtitle.
- Timeline thật có ruler, zoom, playhead, cue block và thao tác trực quan.
- Timeline windowing cho video/cue dài; paging hiện tại chỉ phục vụ cue inspector.
- Job engine local bền vững, checkpoint, pause/resume/retry/crash recovery.
- OCR region selector, OCR preview, PaddleOCR và cue accumulator.
- Whisper STT local.
- Pipeline dịch local hoặc dịch AI qua organization gateway.
- Translation context, glossary, memory, fingerprint và quality validation.
- Voice generation, voice timeline và audio mix.
- Subtitle style, che phụ đề cứng và export MP4 hoàn chỉnh.
- Optional model/runtime provisioning, checksum, license và updater policy.

---

## 3. Đối chiếu TOOL_VIETSUB và phạm vi được phép chuyển

### 3.1. UI/logic tham chiếu nên thích nghi

| Nguồn tham chiếu | Dùng để học/chuyển logic | Đích dự kiến |
|---|---|---|
| `TOOL_VIETSUB_APP/ClientApp/src/App.tsx` | Grid editor, resize, state orchestration | `VietsubEditorWorkspace.tsx`, hook Vietsub hiện tại |
| `components/PreviewPanel.tsx` | Playback, overlay, audio controls, transform | `VietsubPreviewPanel.tsx` |
| `components/Timeline.tsx` | Geometry, playhead, zoom, cue operation | `VietsubTimeline.tsx` và helper thuần |
| `components/SubtitlePanel.tsx` | Track/cue inspector | Tách/refactor `VietsubSubtitleEditor.tsx` |
| `components/SettingsPanel.tsx` | Nhóm OCR/STT/dịch/output/job controls | `VietsubSettingsPanel.tsx` |
| `components/OcrRegionSelector.tsx` | Chọn vùng chuẩn hóa và preview | `VietsubOcrRegionDialog.tsx` |
| `components/TranslationSettingsEditor.tsx` | Context/glossary/quality UX | `VietsubTranslationPanel.tsx` |
| `components/SubtitleStyleEditor.tsx` | Style subtitle | `VietsubSubtitleStylePanel.tsx` |
| `components/VoiceWorkspace.tsx` | Voice mapping và job UX | `VietsubVoicePanel.tsx` |
| `lib/timelineGeometry.ts` | Tính px/time, clamp, viewport | helper Vietsub mới, có unit test |
| `lib/workspaceLayout.ts` | Grid responsive và panel sizing | helper layout Vietsub mới |
| `Core/ProjectModels.cs` | Danh sách setting/artifact/job cần có | Mở rộng model Vietsub hiện tại, không copy nguyên manifest |
| `Core/ProjectSubtitleStore.cs` | Migration/persistence pattern | Mở rộng `VietsubSubtitleStore` hiện tại |
| `Jobs/PersistentJobManager.cs` | State machine/job lifecycle | `TOOL-LOCAL/Vietsub/Jobs` |
| `Jobs/OcrJobExecutor.cs` | OCR checkpoint/metrics | `TOOL-LOCAL/Vietsub/Recognition/Ocr` |
| `Jobs/TranscriptionJobExecutor.cs` | Whisper chunk/checkpoint | `TOOL-LOCAL/Vietsub/Recognition/Speech` |
| `Jobs/TranslationJobExecutor.cs` | Translation scene/checkpoint | `TOOL-LOCAL/Vietsub/Translation` |
| `Media/FfmpegOcrFrameReader.cs` | Frame extraction stream | Reuse process/media abstractions VideoMaker |
| `LocalAi/OcrCueAccumulator.cs` | Gộp frame thành cue | Chuyển có test |
| `LocalAi/OcrSubtitleLineSegmenter.cs` | Segment dòng subtitle | Chuyển có test |
| `Translation/*` | Planner/cache/memory/validator | Chuyển phần provider-neutral |
| `Jobs/Voice*`, `Jobs/VideoExportJobExecutor.cs` | Voice fit/mix/export | Chuyển ở phase sau |

### 3.2. Phần tuyệt đối không copy vào desktop VideoMaker

- `ProtectedTranslationCredentialStore`.
- `ProtectedVoiceCredentialStore`.
- Bất kỳ UI/form/bridge field `apiKey`, provider base URL hoặc model tùy ý.
- Các cloud provider client gọi OpenAI/Gemini/DeepSeek/Groq trực tiếp từ desktop.
- `FptVoiceSynthesizer` nếu nó yêu cầu key cục bộ và gọi provider trực tiếp.
- Server auth, finance, purchase, quota hoặc database của TOOL_VIETSUB.
- `MainForm.cs` nguyên khối và message names không có prefix `vietsub.*`.
- Tailwind/theme/branding của TOOL_VIETSUB.
- FFmpeg downloader/runtime riêng của TOOL_VIETSUB; phải dùng bundle/preflight VideoMaker.
- Model binary, Python runtime hoặc package lớn trước khi hoàn tất license/checksum/package review.

### 3.3. Quy tắc đối với Cloud translation

- Desktop chỉ gửi JWT/device/license/organization/project/idempotency và text cần dịch trong giới hạn.
- Server tự chọn provider/model/credential theo policy tổ chức.
- `Viewer` không được phát sinh chi phí.
- Request phải đi qua access check, rate snapshot, reservation `Serializable`, settlement/release và audit an toàn.
- Không tự đoán giá. Thiếu rate dừng với `pricing_not_configured` trước outbound.
- Không ghi full subtitle/prompt vào audit hoặc request log thông thường.
- API trả kết quả/metadata, không trả provider key hoặc provider URL.

---

## 4. Quyết định kiến trúc đã khóa cho task này

### ADR-VSE-01 — Library và editor là hai view của cùng module

- Không thêm React Router chỉ cho Vietsub vì `App.tsx` hiện dùng union `Page` và state nội bộ.
- `state.selectedProject == null` hiển thị `VietsubProjectLibrary`.
- `state.selectedProject != null` hiển thị `VietsubEditorWorkspace`.
- Create/open chỉ chuyển view sau response `vietsub.state` xác nhận session đã mở.
- Close project trả về library sau khi host đóng session.

### ADR-VSE-02 — Giữ local-first và project độc lập

- Manifest/SQLite trong `<WorkspaceRoot>/vietsub/projects/{projectId}` là nguồn sự thật cho media, cue, local job và artifact.
- `vs.Projects` chỉ là registry metadata/ownership.
- Không ghi cue/media/job Vietsub vào `vf.Projects` hoặc các bảng workflow VideoMaker.
- Video/audio/frame/model local không upload lên server chỉ để OCR/STT/export.

### ADR-VSE-03 — Tái sử dụng media infrastructure VideoMaker

- Dùng `IMediaToolPreflightService`, `FfprobeService`, `IExternalProcessRunner` và bundle FFmpeg hiện hành.
- Không tạo đường dò/tải FFmpeg thứ hai.
- Source video luôn read-only về mặt nghiệp vụ; output qua `.partial` rồi move.

### ADR-VSE-04 — Editor shell được triển khai trước pipeline AI

- Phase UI đầu tiên dùng ngay import/playback/SRT/cue operation đã có.
- Nút OCR/STT/dịch/voice/export chỉ bật khi capability thật đã sẵn sàng.
- Không dùng button giả báo thành công hoặc progress giả.
- Capability response phải phân biệt `available`, `notInstalled`, `notConfigured`, `permissionDenied` và `busy`.

### ADR-VSE-05 — Job dài không chạy trực tiếp trong bridge handler

- Bridge chỉ validate, enqueue/start và trả state.
- Executor chạy nền, có cancellation/checkpoint.
- Getter `vietsub.state.get` tuyệt đối không tự start/resume job.
- Progress được throttle/coalesce, mục tiêu tối đa khoảng 4–5 event/giây/job.

### ADR-VSE-06 — Dữ liệu lớn dùng window/page/delta

- Cue inspector tiếp tục dùng paging.
- Timeline dùng truy vấn theo khoảng thời gian visible window.
- Không gửi toàn bộ 20.000 cue trong `vietsub.state`.
- Update cue/timeline gửi delta và expected revision, không gửi lại toàn track.

---

## 5. Thiết kế state và contract frontend

### 5.1. State đề xuất

Mở rộng `VietsubModuleState` theo hướng:

```ts
type VietsubViewMode = 'library' | 'editor'

type VietsubEditorState = {
  project: VietsubProjectSummary
  capabilities: VietsubEditorCapabilities
  media: VietsubMediaSummary | null
  subtitleWorkspace: VietsubSubtitleWorkspace | null
  subtitlePage: VietsubSubtitlePage | null
  timelineWindow: VietsubTimelineWindow | null
  settings: VietsubEditorSettings
  latestJob: VietsubJobSummary | null
  playheadMilliseconds: number
  selectedCueId: string | null
}
```

Không nhất thiết tạo object đúng y nguyên trên trong một commit; mục tiêu là tách state editor khỏi state library và tránh `VietsubPage` nhận hàng chục callback không phân nhóm.

### 5.2. Capability bắt buộc

```text
canImportMedia
canImportSrt
canEditSubtitle
canRunTranscription
canRunOcr
canTranslateLocal
canTranslateOrganizationAi
canGenerateVoiceLocal
canGenerateVoiceOrganizationAi
canExportSrt
canExportVideo
mediaToolsReady
speechModelState
ocrModelState
translationModelState
voiceModelState
```

Mỗi capability cần `enabled`, `reasonCode`, `message`; không chỉ dùng boolean nếu UI cần hướng xử lý.

### 5.3. Message bridge hiện có phải giữ tương thích

```text
vietsub.state.get
vietsub.project.create
vietsub.project.open
vietsub.project.rename
vietsub.project.close
vietsub.media.import
vietsub.subtitle.import
vietsub.subtitle.track.activate
vietsub.subtitle.page.get
vietsub.subtitle.cue.update
vietsub.subtitle.cue.split
vietsub.subtitle.cue.align-start
vietsub.subtitle.cue.duplicate
vietsub.subtitle.cue.delete
vietsub.subtitle.export
vietsub.operation.cancel
```

Không đổi tên/xóa các message trên trong lát cắt editor đầu tiên.

### 5.4. Message mới dự kiến

| Request | Response/event | Mục đích |
|---|---|---|
| `vietsub.editor.state.get` | `vietsub.editor.state` | Lấy capability/settings/job/editor metadata |
| `vietsub.timeline.window.get` | `vietsub.timeline.window` | Lấy cue trong khoảng visible time |
| `vietsub.timeline.cue.update` | `vietsub.subtitle.changed` | Kéo/resize start/end với revision |
| `vietsub.editor.settings.update` | `vietsub.editor.state` | Lưu source/OCR/translation/output setting |
| `vietsub.ocr.preview` | `vietsub.ocr.preview.completed` | Preview frame crop + text/confidence |
| `vietsub.job.transcribe.start` | `vietsub.job.changed` | Enqueue STT |
| `vietsub.job.ocr.start` | `vietsub.job.changed` | Enqueue OCR |
| `vietsub.job.translate.start` | `vietsub.job.changed` | Enqueue local/cloud translate |
| `vietsub.job.voice.start` | `vietsub.job.changed` | Enqueue voice |
| `vietsub.job.export.start` | `vietsub.job.changed` | Enqueue export |
| `vietsub.job.pause` | `vietsub.job.changed` | Pause ở checkpoint an toàn |
| `vietsub.job.resume` | `vietsub.job.changed` | Resume job interrupted/paused |
| `vietsub.job.retry` | `vietsub.job.changed` | Tạo attempt hợp lệ |
| `vietsub.job.cancel` | `vietsub.job.changed` | Cancel + cleanup partial |
| `vietsub.job.list` | `vietsub.job.list` | Xem job gần đây, phân trang nếu cần |
| `vietsub.model.status.get` | `vietsub.model.status` | Trạng thái component/model local |
| `vietsub.model.install` | `vietsub.model.progress` | Cài optional component đã duyệt |
| `vietsub.export.destination.select` | event riêng | Chọn output mà không đưa path vào DOM |

### 5.5. Quy tắc contract

- Mọi request từ React có `requestId` do bridge helper hiện tại tạo.
- C# validate GUID, enum/string allowlist, độ dài text, range thời gian và revision.
- Error code ổn định, prefix `vietsub_`.
- Response không chứa absolute local path.
- Timeline update cần `trackId`, `cueId`, `expectedTrackRevision`, `startMilliseconds`, `endMilliseconds`.
- OCR region dùng tọa độ chuẩn hóa `[0,1]`: `x`, `y`, `width`, `height`.
- Job start snapshot input track/revision/settings; thay setting sau đó không đổi job đang chạy.
- Cloud translation request không nhận `providerCode`, `modelId`, `apiKey` từ UI làm nguồn sự thật.

---

## 6. Thiết kế persistence và migration local

### 6.1. Manifest

Mở rộng manifest theo hướng tương thích ngược:

```text
EditorSettings
  SourceLanguageCode
  TargetLanguageCode
  OcrLanguageCode
  OcrRegion
  OcrProcessingProfile
  OcrSampleIntervalMilliseconds
  TranslationMode
  TranslationContext
  VoiceSettings
  AudioMixSettings
  SubtitleStyle
  SubtitleRemovalSettings
  ExportSettings
```

Quy tắc:

- Default an toàn khi đọc manifest schema cũ.
- Nếu tăng `CurrentSchemaVersion`, phải có migration in-memory rõ ràng và test fixture schema cũ.
- Không nhét cue, frame OCR, WAV hoặc toàn bộ job log vào manifest.
- Không lưu secret/provider key.
- Không cập nhật manifest theo từng frame/playhead để tránh write amplification.

### 6.2. SQLite `project.db`

Schema 2 hiện có subtitle track/cue/artifact. Các migration dự kiến:

#### Schema 3 — Job engine

```text
local_jobs
local_job_steps
local_job_events
```

Trường tối thiểu của `local_jobs`:

```text
job_id, project_id, job_type, status, attempt
input_track_id, input_track_revision
parameters_json, checkpoint_json
progress_percent, progress_message
error_code, error_message
created_at_utc, started_at_utc, updated_at_utc, completed_at_utc
```

#### Schema 4 — Translation context/memory/cache

```text
translation_memory
translation_cache
translation_job_items
```

#### Schema 5 — Voice/artifact metadata

```text
voice_cue_artifacts
voice_timeline_artifacts
export_artifacts
```

Không cần tạo đủ schema 3–5 trong một migration. Mỗi phase chỉ thêm bảng cần dùng và migration idempotent từ version trước.

### 6.3. Checkpoint/file layout

```text
cache/jobs/{jobId}/
  checkpoint.json
  chunks/
  frames/
  partial/

audio/
subtitles/
voice/
music/
output/
temp/
logs/
```

- File quan trọng publish nguyên tử.
- Cancel/fail dọn `partial`, không xóa artifact completed của job/track khác.
- Tất cả path đi qua `VietsubAppPaths.GetProjectPath`.
- Job log kỹ thuật phải lọc subtitle text nếu không cần và không có secret.

---

## 7. Kế hoạch triển khai theo phase

## Phase E1 — Tách thư viện dự án và editor shell

**Mục tiêu:** sửa đúng vấn đề người dùng đang gặp bằng các năng lực đã có, chưa thêm OCR/AI giả.

### Task E1.1 — Tách component

- [x] Tạo `TOOL-LOCAL/Web/src/features/vietsub/VietsubProjectLibrary.tsx`.
- [x] Chuyển hero, create form, project list, rename và refresh từ `VietsubPage.tsx` sang library.
- [x] Tạo `VietsubEditorWorkspace.tsx`.
- [x] `VietsubPage.tsx` chỉ quyết định library/editor và render error boundary cấp module.
- [x] Khi có `selectedProject`, không render create/list/workflow cards.
- [x] Không thay bridge contract ở lát cắt này nếu chưa cần.

### Task E1.2 — Editor header

- [x] Hiển thị back/close, tên project, sync state và save state.
- [x] Hiển thị recovery warning gọn trong header.
- [x] Có action nhập video/nhập SRT phù hợp capability.
- [x] Không hiển thị badge “sẵn sàng” nếu media/model/job chưa sẵn sàng.
- [x] Busy của một thao tác chỉ disable action xung đột, không khóa toàn sidebar app.

### Task E1.3 — Grid ba panel + timeline placeholder thật

- [x] Tạo `VietsubSettingsPanel.tsx` với các section hiện có và capability disabled state.
- [x] Tạo `VietsubPreviewPanel.tsx` dùng playback URL hiện có.
- [x] Refactor `VietsubSubtitleEditor.tsx` thành cue inspector; giữ paging/search/filter/import/export.
- [x] Tạo vùng timeline dưới, ban đầu dùng playhead + 12 thumbnail + cue active hiện có.
- [x] Di chuyển CSS Vietsub ra file riêng nếu `styles.css` tiếp tục khó quản lý; nếu chưa tách, giữ prefix `.vietsub-` tuyệt đối.

### Task E1.4 — Điều hướng và state

- [x] Create/open chỉ chuyển editor khi `vietsub.state` xác nhận selected project.
- [x] Close xóa editor-only state: selected cue, timeline window, local draft và notice cũ.
- [x] Chuyển organization đóng editor an toàn.
- [x] Refresh trong editor không làm nhảy về library khi session còn hợp lệ.
- [x] Tránh request page subtitle lặp khi active track không đổi.

### Task E1.5 — Empty/error states

- [x] Chưa có video: CTA import ở preview.
- [x] Video LINK bị đổi/mất: chặn playback/OCR/STT/export, vẫn cho xem/sửa/export SRT.
- [x] Chưa có track: CTA import SRT/nhận dạng.
- [x] Bridge lỗi: giữ project/editor nếu lỗi không làm session invalid.
- [x] Session/project not found: trở về library và thông báo rõ.

### File tác động E1

```text
Sửa:
  TOOL-LOCAL/Web/src/features/vietsub/VietsubPage.tsx
  TOOL-LOCAL/Web/src/features/vietsub/VietsubSubtitleEditor.tsx
  TOOL-LOCAL/Web/src/features/vietsub/useVietsubModule.ts
  TOOL-LOCAL/Web/src/features/vietsub/types.ts
  TOOL-LOCAL/Web/src/App.tsx
  TOOL-LOCAL/Web/src/styles.css

Tạo dự kiến:
  TOOL-LOCAL/Web/src/features/vietsub/VietsubProjectLibrary.tsx
  TOOL-LOCAL/Web/src/features/vietsub/VietsubEditorWorkspace.tsx
  TOOL-LOCAL/Web/src/features/vietsub/VietsubEditorHeader.tsx
  TOOL-LOCAL/Web/src/features/vietsub/VietsubSettingsPanel.tsx
  TOOL-LOCAL/Web/src/features/vietsub/VietsubPreviewPanel.tsx
  TOOL-LOCAL/Web/src/features/vietsub/VietsubTimeline.tsx
  TOOL-LOCAL/Web/src/features/vietsub/vietsub-editor.css (nếu tách CSS)
```

### Điều kiện qua E1

- Tạo/mở project đưa thẳng vào editor.
- Trang danh sách không còn nằm phía trên editor.
- Import/playback/SRT/edit/export SRT cũ vẫn chạy.
- Resize cửa sổ ở ba breakpoint không làm panel chồng nhau hoặc mất action chính.

---

## Phase E2 — Preview và playback controller hoàn chỉnh

### Task E2.1 — Một nguồn playhead duy nhất

- [x] `VietsubEditorWorkspace` sở hữu playhead, playing, playbackRate và selected cue.
- [x] `video.currentTime`, timeline và cue inspector đồng bộ hai chiều.
- [x] Seek từ cue/timeline không tạo vòng lặp update vô hạn.
- [x] Khi đổi source/project, reset state đúng.

### Task E2.2 — Playback controls

- [x] Play/pause, seek, current/duration, rate `0.5–2.0`, volume và mute.
- [x] Keyboard shortcut khi focus không ở input: Space, J/K/L hoặc mũi tên theo quyết định UX.
- [x] Không chặn phím nhập nội dung trong textarea.
- [x] Hiển thị cue translated hiện hành dưới dạng overlay tùy bật/tắt.

### Task E2.3 — Media safety

- [x] Tiếp tục dùng virtual HTTPS URL và Range handler hiện có.
- [x] Không đưa original path vào React/error/log.
- [x] Mọi job media kiểm tra lại source availability/hash trước chạy.
- [x] Playback source changed phải trả error code có hướng xử lý.

### Task E2.4 — Thumbnail profile

- [x] Lát cắt đầu tái sử dụng 12 thumbnail canonical.
- [ ] Sau khi timeline zoom hoạt động, mở rộng service thành cache theo timestamp/profile thay vì sinh lại vô hạn.
- [ ] Thumbnail request theo visible viewport, debounce và cancel request cũ.
- [x] Cache key gồm source hash + profile version + timestamp bucket.

### Điều kiện qua E2

- Playhead/cue/timeline không lệch đáng kể khi play/seek/rate.
- HTTP Range vẫn hoạt động.
- Không lộ path và source không bị ghi.

---

## Phase E3 — Timeline tương tác thật

### Task E3.1 — Geometry thuần và test được

- [x] Tạo helper `timeToPixel`, `pixelToTime`, zoom clamp, viewport range, snap.
- [x] Đơn vị nguồn sự thật là millisecond; chỉ format sang giây ở UI.
- [x] Test video ngắn, video nhiều giờ, zoom min/max, DPI/width bất thường.

### Task E3.2 — Timeline window API

- [x] Thêm query SQLite theo `track_id`, `start_ms < windowEnd`, `end_ms > windowStart`.
- [x] Có `maximumCues` và báo `truncated` nếu vượt.
- [x] Response chỉ chứa field cần render timeline, không gửi text dài nếu không cần.
- [x] Cache/deduplicate request theo track revision + window.

### Task E3.3 — Track và ruler

- [x] Ruler thích ứng zoom.
- [x] Playhead draggable và auto-follow tùy chọn.
- [x] Thumbnail/video track.
- [x] Subtitle cue blocks có selected/active/warning/locked state.
- [x] Sau phase voice thêm source audio/voice/music track; không dựng giả ở E3.

### Task E3.4 — Cue operations

- [x] Click cue select + seek.
- [x] Split ở playhead.
- [x] Align start ở playhead.
- [x] Duplicate/delete.
- [x] Drag/resize start/end qua `vietsub.timeline.cue.update`.
- [x] Validate `end > start`, minimum duration, project media duration và overlap policy.
- [x] Dùng expected track revision để chặn stale edit.
- [x] Sau edit, invalidate artifact/voice phụ thuộc revision theo policy hiện có/mở rộng.

### Task E3.5 — Hiệu năng/accessibility

- [x] Chỉ render cue trong viewport + overscan.
- [x] Pointer capture và cleanup listener khi unmount.
- [x] Keyboard nudge có bước rõ ràng.
- [x] ARIA label cho cue/time/action.
- [x] Không render lại toàn cue inspector khi playhead tick.

### File tác động E3

```text
TOOL-LOCAL/Vietsub/Storage/VietsubSubtitleStore.cs
TOOL-LOCAL/Vietsub/Subtitles/VietsubSubtitleService.cs
TOOL-LOCAL/Vietsub/VietsubWebBridge.cs
TOOL-LOCAL/Web/src/features/vietsub/types.ts
TOOL-LOCAL/Web/src/features/vietsub/useVietsubModule.ts
TOOL-LOCAL/Web/src/features/vietsub/VietsubTimeline.tsx
TOOL-TESTS/Vietsub/VietsubSubtitleTests.cs
TOOL-TESTS/Vietsub/VietsubEditorContractTests.cs (mới dự kiến)
```

### Điều kiện qua E3

- Timeline dùng được với track lớn mà không gửi toàn track qua bridge.
- Edit bằng timeline và cue inspector nhất quán.
- Stale revision không ghi đè dữ liệu mới.

---

## Phase E4 — Local job engine và recovery

Phase này tương ứng Gate 6 của kế hoạch cha và là prerequisite của OCR/STT/dịch/voice/export.

### Task E4.1 — Domain/state machine

- [ ] Job type allowlist: `EXTRACT_AUDIO`, `TRANSCRIBE_LOCAL`, `OCR_LOCAL`, `TRANSLATE_LOCAL`, `TRANSLATE_CLOUD`, `SYNTHESIZE_VOICE_LOCAL`, `EXPORT_VIDEO_LOCAL`.
- [ ] Status: `PENDING`, `RUNNING`, `PAUSING`, `PAUSED`, `INTERRUPTED`, `COMPLETED`, `FAILED`, `CANCELLED`.
- [ ] Chỉ cho transition hợp lệ; test mọi transition sai.
- [ ] Một project chỉ có một heavy job active, trừ tác vụ read-only được quyết định rõ.
- [ ] Global semaphore giới hạn OCR/STT/model nặng giữa các project.

### Task E4.2 — Persistence

- [ ] Migration SQLite schema 3.
- [ ] Job/step/checkpoint lưu trước khi executor chạy.
- [ ] `RUNNING -> INTERRUPTED` khi app/project mở lại sau crash.
- [ ] Lưu input track/revision và settings snapshot.
- [ ] Event/log có retention/cap, không tăng vô hạn.

### Task E4.3 — Executor registry

- [ ] Interface executor riêng Vietsub.
- [ ] Không resolve executor bằng string reflection.
- [ ] Pause/cancel thông qua token + checkpoint boundary.
- [ ] Retry tạo attempt đúng, không chạy lại job đang active.
- [ ] Resume kiểm tra dependency/model/source hash trước chạy.

### Task E4.4 — Bridge/UI

- [ ] `vietsub.job.*` validate job thuộc selected project/session.
- [ ] Progress throttle/coalesce.
- [ ] Editor header/status bar hiển thị phase, %, ETA nếu có, cancel/pause/resume/retry.
- [ ] `vietsub.state.get` chỉ đọc state.
- [ ] Đóng app không sync-wait trên UI thread.

### File tạo dự kiến E4

```text
TOOL-LOCAL/Vietsub/Jobs/VietsubJobModels.cs
TOOL-LOCAL/Vietsub/Jobs/IVietsubJobExecutor.cs
TOOL-LOCAL/Vietsub/Jobs/VietsubJobStore.cs
TOOL-LOCAL/Vietsub/Jobs/VietsubJobManager.cs
TOOL-LOCAL/Vietsub/Jobs/VietsubJobExecutorRegistry.cs
TOOL-LOCAL/Web/src/features/vietsub/VietsubJobStatusBar.tsx
TOOL-TESTS/Vietsub/VietsubJobTests.cs
```

### Điều kiện qua E4

- Job dài không block UI.
- Cancel/retry/reopen app không tạo executor trùng.
- Job interrupted được hiển thị và resume có chủ đích.

---

## Phase E5 — OCR local đầy đủ

### Task E5.0 — Legal/package gate

- [ ] Xác minh license NuGet/runtime/model cho PaddleOCR, PaddleInference và OpenCV.
- [ ] Chốt package version; không copy `bin`, model cache hoặc runtime từ repository nguồn.
- [ ] Ghi third-party notice, nguồn tải, size, SHA-256 và redistribution scope.
- [ ] Quyết định optional component hay bundle; mặc định ưu tiên optional component.

### Task E5.1 — OCR settings và region selector

- [ ] Thêm region normalized `x/y/width/height` với default vùng dưới video.
- [ ] Profile `FAST`, `BALANCED`, `ACCURATE`.
- [ ] Source language routing `en`, `zh`; `auto` phải có behavior rõ.
- [ ] Dialog hiển thị frame thật, drag/resize region, flip/rotation đúng display coordinates.
- [ ] Preview chỉ xử lý một frame và trả text/confidence, có cancel/timeout.

### Task E5.2 — Frame pipeline

- [ ] FFmpeg đọc/crop frame theo region, không xuất hàng nghìn JPEG nếu stream/raw frame phù hợp.
- [ ] Source hash/preflight trước job.
- [ ] Sampling interval/profile snapshot trên job.
- [ ] Frame dedup/change tracker và periodic safety recognition.
- [ ] Dọn frame/temp khi complete/fail/cancel.

### Task E5.3 — OCR inference và cue accumulation

- [ ] PaddleOCR chạy ngoài UI thread.
- [ ] English/Chinese model routing.
- [ ] Confidence threshold và subtitle line segmentation.
- [ ] Gộp frame gần giống thành cue; bỏ flash confidence thấp.
- [ ] Checkpoint theo thời gian/frame; resume không lặp cue.
- [ ] Không ghi đè cue locked; output là track mới nguồn `PADDLE_OCR_LOCAL`.

### Task E5.4 — UI và metrics

- [ ] Action **Quét OCR** mở region dialog trước khi chạy.
- [ ] Hiển thị frame processed, elapsed, ETA, reused/recognized frames.
- [ ] Hoàn tất tự activate track mới sau xác nhận hợp lý; track cũ vẫn giữ.
- [ ] Lỗi model/runtime/disk/source changed có hướng sửa cụ thể.

### File tham chiếu chính E5

```text
Nguồn:
  Core/OcrProcessingProfiles.cs
  Core/OcrRegionResolver.cs
  Jobs/OcrJobExecutor.cs
  Jobs/OcrFrameChangeTracker.cs
  LocalAi/PaddleOcrRecognizer.cs
  LocalAi/OcrCueAccumulator.cs
  LocalAi/OcrSubtitleLineSegmenter.cs
  Media/FfmpegOcrFrameReader.cs
  ClientApp/src/components/OcrRegionSelector.tsx

Đích dự kiến:
  TOOL-LOCAL/Vietsub/Recognition/Ocr/*
  TOOL-LOCAL/Vietsub/Media/VietsubOcrFrameReader.cs
  TOOL-LOCAL/Web/src/features/vietsub/VietsubOcrRegionDialog.tsx
  TOOL-TESTS/Vietsub/VietsubOcrTests.cs
```

### Điều kiện qua E5

- Preview OCR hoạt động trên vùng người dùng chọn.
- OCR video tạo track cue có timeline và checkpoint.
- Cancel/retry không rò file/process và source giữ nguyên hash.

---

## Phase E6 — Whisper STT local

### Task E6.0 — Package/model gate

- [ ] Duyệt `Whisper.net` và runtime version.
- [ ] Registry model có URL/nguồn nội bộ, version, size, SHA-256 và license.
- [ ] Download `.partial`, verify hash rồi publish; không tải ngầm khi chưa thông báo dung lượng.
- [ ] Kiểm tra disk/RAM/CPU trước job.

### Task E6.1 — Audio extraction

- [ ] Reuse FFmpeg để tạo WAV mono 16 kHz.
- [ ] Artifact gắn source hash và settings version.
- [ ] Video không audio trả lỗi trước khi tạo transcription job.
- [ ] Long video dùng chunk có coverage đầy đủ, overlap có giới hạn.

### Task E6.2 — Transcription

- [ ] Auto detect hoặc hint `en`/`zh`.
- [ ] Timestamp toàn cục đúng khi merge chunk.
- [ ] Checkpoint theo chunk/batch.
- [ ] Dedup ranh giới chunk.
- [ ] Không ghi đè cue locked; output track `WHISPER_LOCAL`.
- [ ] Metrics duration/audio/model/elapsed không chứa transcript trong log thông thường.

### Task E6.3 — Editor UX

- [ ] **Nhận dạng lời nói** chỉ bật khi video có audio và model sẵn sàng.
- [ ] Nếu active track khác đã có translation/voice, cảnh báo chuyển track nhưng không xóa dữ liệu cũ.
- [ ] Resume/retry từ checkpoint.

### Điều kiện qua E6

- Video có lời tạo được track timestamp đúng.
- Video dài resume không mất/lặp cue.
- Source video không đổi.

---

## Phase E7 — Translation core và local translation

### Task E7.1 — Provider-neutral core

- [ ] Chuyển/adapt translation contracts, scene/chapter planner.
- [ ] Context gồm cue trước/sau trong cùng scene/chapter hợp lý.
- [ ] Translation fingerprint gồm source, context, glossary, settings và engine version.
- [ ] Translation memory theo project, giới hạn retention/số lượng.
- [ ] Glossary parse/validate, không cho dòng quá lớn hoặc key rỗng.
- [ ] Quality validator: empty, repetition, number, glossary, reading speed, confidence.

### Task E7.2 — Local provider

- [ ] Chốt engine cho `en -> vi` và `zh -> vi` sau license/model review.
- [ ] Worker process UTF-8, timeout, stderr cap và kill tree khi cancel.
- [ ] Batch/checkpoint.
- [ ] Không ghi đè `translation_locked`.
- [ ] `Continue`, `Retry failed`, `Restart unlocked` có semantics rõ.

### Task E7.3 — Translation editor

- [ ] Panel context, cách xưng hô/style và glossary.
- [ ] Không hiện provider/model cloud tùy ý ở UI.
- [ ] Cue hiển thị `PENDING`, `VALID`, `REVIEW`, `INVALID`, warning.
- [ ] Manual edit cập nhật translation memory theo policy.
- [ ] Filter/review warning trong cue inspector.

### File tham chiếu E7

```text
Translation/TranslationContracts.cs
Translation/TranslationScenePlanner.cs
Translation/TranslationResultCache.cs
Translation/LocalTranslationContextResolver.cs
Translation/LocalTranslationProviderAdapter.cs
LocalAi/TranslationQualityValidator.cs
Jobs/TranslationJobExecutor.cs
```

Không chuyển các direct cloud provider client hoặc credential store.

### Điều kiện qua E7

- English/Chinese -> Vietnamese local hoạt động có checkpoint.
- Locked/manual translations được bảo vệ.
- Quality warning có thể lọc và xử lý.

---

## Phase E8 — Cloud translation qua AI Gateway tổ chức

Phase này có thay đổi public DTO/server/database và phải tuân thủ đầy đủ AGENTS của Contracts/Server/database.

### Task E8.1 — Contract và access

- [ ] Thêm DTO trong `TOOL-SHARED.Contracts/Vietsub`, tương thích ngược.
- [ ] Request có organization ID, Vietsub project ID, local job/request ID, source/target, target/context cues, glossary/context trong giới hạn.
- [ ] Không nhận API key, base URL hoặc provider/model tùy ý.
- [ ] Server xác minh JWT, session, device, license, membership, role và `vs.Projects` ownership.
- [ ] Viewer bị chặn trước reservation/outbound.

### Task E8.2 — Server job/idempotency/budget

- [ ] Quyết định tái sử dụng `vf.ProviderRequests` hay tạo bảng `vs.TranslationJobs` sau khi kiểm tra constraint/ownership thực tế; không giả định.
- [ ] Nếu cần schema mới, tạo migration SQL idempotent mới, không sửa migration cũ.
- [ ] Idempotency scope organization + request hash.
- [ ] Resolve model Text/credential Active/rate InputToken+OutputToken từ policy server.
- [ ] Reserve bằng transaction `Serializable` trước outbound.
- [ ] Settle actual usage bằng rate snapshot; release khi chắc chắn chưa phát sinh.
- [ ] Trạng thái submission/result không chắc chắn phải reconcile, không release mù.

### Task E8.3 — Prompt/output/data minimization

- [ ] Server sở hữu prompt template và JSON Schema.
- [ ] Validate đúng cue ID, không thiếu/thừa/trùng.
- [ ] Limit cue count, tổng ký tự, glossary, context và output.
- [ ] Audit/RequestJson chỉ giữ metadata/hash cần thiết.
- [ ] Nếu lưu payload/result để worker resume, phải mã hóa và có retention/cleanup rõ.
- [ ] Không gửi video/audio/frame.

### Task E8.4 — Desktop poll/recovery

- [ ] Desktop submit một lần rồi poll bằng backoff.
- [ ] App đóng không làm server bỏ settlement.
- [ ] Mở lại project reconcile bằng translation job ID hoặc request ID.
- [ ] Chỉ áp result nếu local input track/revision/fingerprint còn khớp.
- [ ] Nếu cue đã sửa/khóa sau submit, giữ bản người dùng và đánh dấu result stale.

### Test bắt buộc E8

- [ ] Cross-org/cross-user/project not found.
- [ ] Viewer/license/session/device invalid trước outbound.
- [ ] Budget thiếu/rate thiếu trước outbound.
- [ ] Hai submit đồng thời cùng idempotency chỉ một provider call.
- [ ] Same key khác payload conflict.
- [ ] Provider 401/403/429/5xx/timeout và settlement.
- [ ] Server restart/desktop close vẫn hoàn tất/reconcile.
- [ ] Response/log không chứa key hoặc provider URL.

### Điều kiện qua E8

- Cloud translation dùng credential/budget tổ chức mà desktop không nhận key.
- Usage truy được đến organization/user/Vietsub project/request/rate snapshot.

---

## Phase E9 — Voice, audio và preview timeline

### Task E9.1 — Quyết định engine

- [ ] Local voice chỉ được đóng gói sau model/license review.
- [ ] Cloud voice nếu có phải đi qua organization gateway; không port FPT key input từ TOOL_VIETSUB.
- [ ] UI không có trường API key.

### Task E9.2 — Voice cue/phrase

- [ ] Speaker -> voice mapping.
- [ ] Cache key gồm translated text, voice, speed, engine/model version.
- [ ] Cue/phrase planner và boundary `AUTO/JOIN/BREAK`.
- [ ] WAV validate sample rate/channel/duration/hash.
- [ ] Text/voice change làm artifact stale.

### Task E9.3 — Voice timeline/audio tracks

- [ ] Fit duration bằng policy tempo/pad/borrow gap có giới hạn.
- [ ] Cảnh báo cue không thể fit an toàn.
- [ ] Tạo immutable voice timeline qua `.partial`.
- [ ] Preview có source audio, Vietnamese voice và sau này background music.
- [ ] Timeline hiển thị audio/voice state thật, không giả waveform nếu chưa phân tích.

### Task E9.4 — Background music và mix

- [ ] Copy/link/hash theo policy rõ.
- [ ] Volume, loop, trim, fade, ducking.
- [ ] Không ghi đè source/music input.

### Điều kiện qua E9

- Voice artifact đúng track revision và cue fingerprint.
- Preview/mix phản ánh setting sẽ dùng khi export.

---

## Phase E10 — Subtitle style, che phụ đề cứng và export

### Task E10.1 — Style và removal region

- [ ] Font, size, màu, outline, shadow, margin/alignment có validation.
- [ ] Overlay preview tương ứng gần đúng output.
- [ ] Một hoặc nhiều vùng blur/cover phụ đề cứng, normalized coordinates.
- [ ] Video rotation/flip được áp cùng hệ tọa độ region.

### Task E10.2 — Export orchestrator

- [ ] Chọn destination native; React chỉ nhận file name/status.
- [ ] Chống ghi đè source và path ngoài ý muốn.
- [ ] Snapshot input track/revision, voice timeline, music, settings và source hash.
- [ ] Tạo MP4 qua `.partial`.
- [ ] Mix original/voice/music theo setting.
- [ ] Burn-in hoặc soft subtitle theo quyết định sản phẩm đã chốt.
- [ ] Export SRT/transcript riêng tiếp tục hoạt động.

### Task E10.3 — Output validation

- [ ] FFprobe video/audio stream.
- [ ] Duration/resolution/tolerance.
- [ ] Audio track phải tồn tại nếu setting yêu cầu.
- [ ] Output hash/size/artifact metadata.
- [ ] Job fail không công bố file partial.

### Điều kiện qua E10

- Full local pipeline tạo MP4/SRT hợp lệ.
- Source video byte-for-byte không đổi.
- Export dùng đúng active track/revision và không dùng voice stale.

---

## Phase E11 — Hardening, UX và release gate

### Task E11.1 — Performance

- [ ] Soak test 5.000/20.000 cue.
- [ ] Video dài nhiều giờ: timeline window, thumbnail cache, STT chunk, OCR memory.
- [ ] Không spam bridge/progress.
- [ ] Không giữ bitmap/frame/WAV stream sau cancel/unmount.

### Task E11.2 — Accessibility và thao tác

- [ ] Keyboard navigation cho panel/cue/timeline/dialog.
- [ ] Focus restore sau modal.
- [ ] Reduced motion và contrast.
- [ ] Confirm destructive delete/restart translation rõ ràng.
- [ ] Error message có hành động sửa, không chỉ hiện mã kỹ thuật.

### Task E11.3 — Security/regression

- [ ] Không token/key/path tuyệt đối trong DOM/console/log.
- [ ] Virtual URL traversal/range security.
- [ ] Organization switch giữa job/request.
- [ ] Feature flag off không ảnh hưởng VideoMaker pages.
- [ ] Regression create/long/short/projects/API organization/update.

### Task E11.4 — Package/release

- [ ] Optional model/runtime có signed manifest/checksum/source/license.
- [ ] Updater backup/rollback không xóa workspace Vietsub.
- [ ] Không publish hoặc smoke test provider thật nếu chưa được người dùng chỉ định môi trường và phê duyệt chi phí.
- [ ] Canary nội bộ trước rollout rộng.

---

## 8. Ma trận file đích dự kiến

### 8.1. Frontend

| File | Hành động | Trách nhiệm |
|---|---|---|
| `Web/src/features/vietsub/VietsubPage.tsx` | Refactor | Chọn library/editor |
| `VietsubProjectLibrary.tsx` | Mới | Create/list/open/rename |
| `VietsubEditorWorkspace.tsx` | Mới | Orchestrate editor state/layout |
| `VietsubEditorHeader.tsx` | Mới | Project/save/recovery/job actions |
| `VietsubSettingsPanel.tsx` | Mới | Source/OCR/STT/dịch/voice/output settings |
| `VietsubPreviewPanel.tsx` | Mới | Video/overlay/playback controls |
| `VietsubSubtitleEditor.tsx` | Refactor | Cue inspector/paging/filter/edit |
| `VietsubTimeline.tsx` | Mới | Timeline/ruler/playhead/cue/audio tracks |
| `VietsubOcrRegionDialog.tsx` | Mới | Region/preview OCR |
| `VietsubTranslationPanel.tsx` | Mới | Translation context/glossary/mode |
| `VietsubVoicePanel.tsx` | Mới | Voice mapping/generation |
| `VietsubExportDialog.tsx` | Mới | Export settings/confirmation |
| `VietsubJobStatusBar.tsx` | Mới | Progress/control/recovery |
| `useVietsubModule.ts` | Mở rộng | Host events/actions/editor state |
| `types.ts` | Mở rộng | Contract frontend |
| `styles.css` hoặc `vietsub-editor.css` | Refactor | Grid/responsive/theme |

Không bắt buộc tạo đúng số component trên trong một commit; nếu gộp/tách khác phải giữ ranh giới trách nhiệm và tránh một file editor hàng nghìn dòng.

### 8.2. Desktop C#

| Khu vực | Hành động |
|---|---|
| `VietsubWebBridge.cs` | Thêm handler nhỏ, chuyển business logic xuống service |
| `Domain/VietsubProjectModels.cs` | Settings/capability/artifact models tương thích |
| `Storage/VietsubSubtitleStore.cs` | Migration job/translation/voice/artifact |
| `Subtitles/VietsubSubtitleService.cs` | Timeline window/revision/delta |
| `Media/*` | Audio extraction, OCR frame, export helpers; reuse process runner |
| `Jobs/*` | Job store/manager/registry/executors |
| `Recognition/Ocr/*` | OCR inference/dedup/accumulator |
| `Recognition/Speech/*` | Whisper/chunk/merge |
| `Translation/*` | Planner/provider-neutral/local/server adapter |
| `Voice/*` | Voice engine/cache/timeline/fit |
| `Export/*` | Subtitle/audio/video composition và validation |
| `Runtime/*` | Optional model/runtime registry/provisioning |
| `Program.cs`, `Form1.cs` | DI, dialog selector, media handler; không đưa business logic vào Form |
| `TOOL-LOCAL.csproj` | Package/content chỉ sau review |
| `DesktopOptions.cs` | Chỉ setting không chứa secret |

### 8.3. Server/Contracts/database khi làm Cloud

```text
TOOL-SHARED.Contracts/Vietsub/*TranslationContracts.cs
TOOL-SERVER/Vietsub/Translation/*
TOOL-SERVER/Controllers/VietsubTranslationsController.cs
TOOL-SERVER/Program.cs
TOOL-SERVER/Data hoặc VietsubDbContext tùy quyết định schema
database/VideoFactory.<version>.VietsubTranslation.sql (nếu thực sự cần)
database/VideoFactory.DesktopLeastPrivilege.sql (deny bảng mới nếu có)
```

Không tạo migration cho editor local/timeline/OCR local nếu dữ liệu chỉ nằm trong `project.db`.

---

## 9. Error code tối thiểu cần ổn định

```text
vietsub_editor_not_ready
vietsub_media_source_required
vietsub_media_source_unavailable
vietsub_media_source_changed
vietsub_subtitle_track_required
vietsub_subtitle_track_changed
vietsub_timeline_window_invalid
vietsub_timeline_edit_conflict
vietsub_job_already_active
vietsub_job_not_found
vietsub_job_transition_invalid
vietsub_job_interrupted
vietsub_dependency_not_installed
vietsub_dependency_hash_invalid
vietsub_insufficient_disk_space
vietsub_ocr_region_invalid
vietsub_ocr_preview_failed
vietsub_ocr_failed
vietsub_transcription_audio_required
vietsub_transcription_failed
vietsub_translation_input_stale
vietsub_translation_failed
vietsub_voice_artifact_stale
vietsub_export_input_stale
vietsub_export_failed
```

Server Cloud tiếp tục dùng các mã chung như `pricing_not_configured`, `organization_budget_exceeded`, `member_budget_exceeded`, `organization_generation_denied`, `idempotency_key_conflict` khi phù hợp; không tạo mã khác nghĩa cho cùng nghiệp vụ.

---

## 10. Kiểm thử bắt buộc

### 10.1. Frontend logic

Repository đích đã có Vitest cho logic frontend thuần. Tiếp tục bổ sung test theo phase:

- [x] Cân nhắc thêm `vitest` và script `npm test` trong cùng phase có test.
- [ ] Test view selection library/editor.
- [ ] Test reducer/event không giữ state project cũ.
- [x] Test timeline geometry/window/zoom/snap.
- [ ] Test keyboard shortcut không chạy trong input.
- [ ] Test capability disable reason.
- [ ] Test progress event coalescing.

Không bắt buộc đưa Playwright vào phase đầu. Visual smoke có checklist thủ công; automation UI chỉ thêm khi hạ tầng ổn định.

### 10.2. Desktop unit/integration

- [x] Project/session/recovery cũ tiếp tục đạt.
- [ ] SQLite migration từ schema 1/2 đến version mới.
- [x] Timeline window không vượt payload cap.
- [x] Timeline stale revision bị từ chối.
- [ ] Job state/race/cancel/retry/recovery.
- [ ] OCR region/dedup/accumulator/model routing.
- [ ] STT chunk/merge/checkpoint.
- [ ] Translation planner/cache/memory/lock/quality.
- [ ] Voice fingerprint/timing/stale artifact.
- [ ] Export arguments/output/source hash.
- [x] Virtual media path/range/traversal.

### 10.3. Server/security nếu làm Cloud

- [ ] JWT/session/device/license/organization/project/role.
- [ ] Viewer/cross-org/cross-user bị chặn trước outbound.
- [ ] Pricing/budget/idempotency/concurrency.
- [ ] Credential không lộ.
- [ ] Prompt/subtitle không xuất hiện trong log/audit ngoài retention mã hóa đã phê duyệt.
- [ ] Worker restart và reconciliation.

### 10.4. Manual UX smoke

1. Tạo project -> vào editor ngay.
2. Back -> library; mở lại -> editor.
3. Import COPY và LINK.
4. Play/seek/rate; click cue và timeline.
5. Import SRT, sửa, split, drag time, export.
6. OCR preview vùng trên/dưới và chạy OCR.
7. STT audio Anh/Trung.
8. Dịch local; khóa một cue và chạy lại.
9. Dịch organization AI trên staging đã phê duyệt chi phí.
10. Tạo voice, preview mix và export.
11. Kill app giữa job, mở lại và resume.
12. Đổi organization khi editor đang mở.
13. Mất/mutate file LINK.
14. Resize cửa sổ ở các breakpoint.

---

## 11. Lệnh xác minh sau mỗi thay đổi source

Từ root:

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Khi sửa frontend:

```powershell
Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
```

Nếu bổ sung `vitest`:

```powershell
npm test
```

Integration model/media thật phải opt-in và dùng fixture được phê duyệt. Không gọi provider thật trong unit test.

---

## 12. Definition of Done tổng thể

- [x] Tạo/mở dự án chuyển thẳng vào editor chuyên dụng.
- [x] Library không nằm cùng trang với editor.
- [x] Editor có settings, preview, cue inspector và timeline dưới.
- [x] Playback/timeline/cue đồng bộ.
- [x] Timeline xử lý track lớn bằng windowing.
- [x] Import SRT/edit/export SRT hiện có không regression.
- [ ] Job dài có persistence/checkpoint/pause/resume/retry/recovery.
- [ ] OCR có region preview và tạo track.
- [ ] STT tạo track từ audio.
- [ ] Dịch local/cloud bảo vệ cue khóa và có quality state.
- [ ] Cloud dùng organization gateway; desktop không có key/provider URL.
- [ ] Voice/artifact gắn đúng track revision.
- [ ] Export output được validate và source không đổi.
- [x] Không lộ local path/token/credential.
- [ ] Cross-org/Viewer/license/budget/idempotency đúng.
- [ ] UI không lag/deadlock trong giới hạn cue/video đã chốt.
- [x] Build Release không warning/error và toàn bộ test đạt.
- [ ] Package/model/license/checksum/updater/rollback được nghiệm thu trước release.

---

## 13. Thứ tự commit/lát cắt khuyến nghị

Không làm một commit “copy full TOOL_VIETSUB”. Chia lát cắt có thể review/rollback:

```text
1. Library/editor view split + editor shell
2. Preview controller + responsive layout
3. Timeline geometry + timeline window + cue timing update
4. Local job store/state machine + UI status
5. OCR region preview
6. OCR executor/model integration
7. STT audio/chunk/executor
8. Translation core/local
9. Cloud translation contracts/server/desktop
10. Voice/cache/timeline
11. Style/removal/audio mix/export
12. Performance/security/release hardening
```

Mỗi lát cắt phải:

- có test của behavior mới;
- giữ build/test toàn solution xanh;
- không sửa source TOOL_VIETSUB;
- cập nhật checkbox và nhật ký bên dưới;
- không trộn cleanup/refactor không liên quan.

---

## 14. Nhật ký và điểm tiếp tục cho agent kế tiếp

### Mốc 2026-09-02 — Tạo task

Đã khảo sát:

- Tài liệu bắt buộc của VideoMaker.
- `KE_HOACH_TICH_HOP_MODULE_VIETSUB_DOC_LAP.md`.
- UI/hook/bridge/store/subtitle/media hiện tại của module Vietsub.
- Editor, timeline, OCR, STT, translation, voice và export trong TOOL_VIETSUB tham chiếu.
- Dependency/package khác biệt giữa hai repository.

Kết luận:

- Gate nền tảng/project/media/SRT đã có; không cần copy lại.
- Lỗi UX hiện tại do chưa tách library/editor.
- Bước source tiếp theo nên là **Phase E1**, sau đó E2/E3, rồi quay về Gate job engine E4 trước khi làm OCR/STT.
- Cloud translation phải viết theo organization gateway của VideoMaker; không chuyển BYOK/direct provider client.
- Lượt tạo task này chỉ thêm tài liệu Markdown, không triển khai tính năng.

### Mốc 2026-09-02 — Hoàn tất Phase E1

Phase/task đã hoàn tất:

- E1.1 tách thư viện dự án khỏi editor.
- E1.2 thêm editor header và trạng thái thật.
- E1.3 dựng grid settings/preview/cue inspector cùng timeline nền dùng playhead, 12 thumbnail và cue hiện có.
- E1.4 giữ/giải phóng editor state đúng theo project, organization và loại lỗi.
- E1.5 bổ sung empty/error/source-changed state; không dựng OCR/AI giả.

File source đã thay đổi trong lượt E1:

- `TOOL-LOCAL/Web/src/App.tsx`.
- `TOOL-LOCAL/Web/src/features/vietsub/VietsubPage.tsx`.
- `TOOL-LOCAL/Web/src/features/vietsub/useVietsubModule.ts`.
- `TOOL-LOCAL/Web/src/features/vietsub/VietsubProjectLibrary.tsx`.
- `TOOL-LOCAL/Web/src/features/vietsub/VietsubEditorWorkspace.tsx`.
- `TOOL-LOCAL/Web/src/features/vietsub/VietsubEditorHeader.tsx`.
- `TOOL-LOCAL/Web/src/features/vietsub/VietsubSettingsPanel.tsx`.
- `TOOL-LOCAL/Web/src/features/vietsub/VietsubPreviewPanel.tsx`.
- `TOOL-LOCAL/Web/src/features/vietsub/VietsubTimeline.tsx`.
- `TOOL-LOCAL/Web/src/styles.css`.
- `TOOL-TESTS/Vietsub/VietsubModuleShellTests.cs`.

Thay đổi liên quan có sẵn từ checkpoint sửa recovery trước E1 và đã được giữ nguyên:

- `TOOL-LOCAL/Vietsub/Storage/VietsubProjectStore.cs`.
- `TOOL-TESTS/Vietsub/VietsubWorkspaceTests.cs`.

Migration local/server đã thêm: không có. Bridge contract không đổi.

Kết quả kiểm tra:

- `npm run build`: đạt, 1818 module transformed.
- `dotnet restore TOOL_GEN_POST_VIDEO.slnx`: đạt.
- `dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore`: đạt, 0 warning/0 error.
- `dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build`: đạt 449/449 test.
- Smoke test WebView2 trên ứng dụng desktop và resize trực quan: chưa chạy; cần chạy trước khi kết luận nghiệm thu giao diện cuối.

Rủi ro/việc còn mở:

- Timeline E1 mới là nền hiển thị; chưa có zoom, viewport API, drag/resize hoặc virtualization.
- Playback vẫn dùng control native của video; controller/shortcut/overlay sẽ làm ở E2.
- Cần smoke test bằng video COPY, video LINK bị đổi/mất và SRT thật trong WebView2.

Task chính xác tiếp theo: **E2.1 — đưa `playing`, `playbackRate`, `selectedCueId` về một controller trong `VietsubEditorWorkspace`, rồi đồng bộ video/timeline/cue inspector hai chiều.**

### Mốc 2026-09-02 — Hoàn tất E2.1–E2.3, nền E2.4

Đã triển khai:

- Một playback controller trong `VietsubEditorWorkspace`: playhead, duration, playing, rate, volume, mute, overlay và selected cue.
- Đồng bộ hai chiều giữa video, seek bar, timeline và cue inspector; seek có ngưỡng chống ghi `currentTime` lặp.
- Control play/pause, seek, tốc độ 0.5–2×, volume/mute và bật/tắt phụ đề dịch overlay.
- Shortcut Space/K, J/L, mũi tên và M; bỏ qua input/textarea/select/button/contenteditable.
- Reset controller khi đổi project/media.
- Playback source LINK bị đổi/mất trả HTTP 409 với `X-Vietsub-Error-Code` và `X-Vietsub-Recovery-Action`, không chứa đường dẫn gốc.
- Giữ Range handler/virtual HTTPS URL và cache 12 thumbnail theo profile version + source hash + canonical index/timestamp bucket.

File thay đổi thêm ở checkpoint E2:

- `TOOL-LOCAL/Web/src/features/vietsub/VietsubEditorWorkspace.tsx`.
- `TOOL-LOCAL/Web/src/features/vietsub/VietsubPreviewPanel.tsx`.
- `TOOL-LOCAL/Web/src/features/vietsub/VietsubSubtitleEditor.tsx`.
- `TOOL-LOCAL/Web/src/features/vietsub/VietsubTimeline.tsx`.
- `TOOL-LOCAL/Web/src/styles.css`.
- `TOOL-LOCAL/Vietsub/Playback/VietsubMediaPlaybackService.cs`.
- `TOOL-TESTS/Vietsub/VietsubMediaTests.cs`.
- `TOOL-TESTS/Vietsub/VietsubModuleShellTests.cs`.
- `TOOL-TESTS/Vietsub/VietsubWorkspaceTests.cs`: thay fixed delay bằng polling có timeout để test autosave không flaky; test mục tiêu chạy lặp 5/5 đạt.

Kết quả kiểm tra mới nhất:

- `npm run build`: đạt, 1818 module transformed.
- `dotnet restore`: đạt.
- Release build: đạt, 0 warning/0 error.
- Toàn bộ test: đạt 450/450.
- Autosave test chạy lặp độc lập: 5/5 đạt.
- Smoke test phát video thật trong WebView2: chưa chạy.

E2.4 còn mở có chủ đích:

- Cache thumbnail theo timestamp/profile mở rộng và request theo visible viewport chỉ làm sau khi E3 có zoom + viewport; không sinh API giả trước geometry.

Task chính xác tiếp theo: **E3.1 — tạo helper geometry thuần (`timeToPixel`, `pixelToTime`, clamp zoom, viewport range, snap) cùng unit test biên; sau đó E3.2 mới thêm timeline window API.**

### Mốc 2026-09-02 — Hoàn tất E3 và khép acceptance E1/E2

Đã triển khai:

- Geometry timeline thuần theo millisecond: chuyển đổi pixel/time, zoom clamp, viewport + overscan, snap và ruler thích ứng; có test video ngắn, nhiều giờ, zoom min/max và fractional CSS pixel.
- Timeline window đọc trực tiếp SQLite bằng điều kiện overlap trên index hiện có; giới hạn tối đa 500 cue, cờ `truncated`, preview text tối đa 200 ký tự và không gửi toàn bộ cue text.
- Correlate response theo request ID để response window cũ không ghi đè window mới; deduplicate query cùng track/window và tải lại sau thay đổi revision.
- Drag/move/resize cue, keyboard nudge, draggable playhead, auto-follow, selected/active/warning/locked state và ARIA; snap luôn được clamp lại theo media/minimum duration.
- `vietsub.timeline.cue.update` dùng expected track revision; stale edit bị từ chối, revision tăng và artifact phụ thuộc chuyển stale theo policy đang có. Policy overlap SRT hiện hành được ghi rõ là cho phép cue chồng thời gian.
- Inspector không nhận playhead tick trực tiếp; chỉ cue active đổi mới làm render lại danh sách. Editor responsive dùng tab/drawer ở breakpoint hẹp.
- Autosave có trạng thái `dirty/saving/saved/error`; blur, Back và đổi organization dùng chung promise flush tuần tự, chỉ rời editor khi save/close thật sự hoàn tất. Lỗi của request timeline song song không còn tự ý xóa trạng thái busy của operation khác.
- Không thêm migration/schema mới, ML package, provider client hay credential ở desktop.

File chính thay đổi ở checkpoint E3:

- `TOOL-LOCAL/Vietsub/Storage/VietsubSubtitleStore.cs`.
- `TOOL-LOCAL/Vietsub/Subtitles/VietsubSubtitleService.cs`.
- `TOOL-LOCAL/Vietsub/VietsubWebBridge.cs`.
- `TOOL-LOCAL/Web/src/features/vietsub/useVietsubModule.ts` và `types.ts`.
- `TOOL-LOCAL/Web/src/features/vietsub/VietsubEditorWorkspace.tsx`, `VietsubEditorHeader.tsx`, `VietsubSubtitleEditor.tsx`, `VietsubTimeline.tsx` và `timelineGeometry.ts`.
- `TOOL-LOCAL/Web/src/features/vietsub/timelineGeometry.test.ts`, `package.json`, `package-lock.json` và `src/styles.css`.
- `TOOL-TESTS/Vietsub/VietsubSubtitleTests.cs` và `VietsubModuleShellTests.cs`.

Kết quả kiểm tra mới nhất:

- `npm ci --no-audit --no-fund`: đạt.
- `npm test`: đạt 7/7 test.
- `npm run build`: đạt, 1819 module transformed.
- `dotnet restore`: đạt.
- Release build: đạt, 0 warning/0 error.
- Toàn bộ test .NET: đạt 452/452.
- `git diff --check`: đạt; chỉ có cảnh báo quy đổi LF/CRLF của Git.
- Smoke test video/SRT thật trong WebView2 và resize cửa sổ: chưa chạy.

Việc còn mở gần nhất:

- E2.4 mở rộng thumbnail theo timestamp/profile và request/cancel theo visible viewport.
- E4/Gate 6 xây local job engine có persistence/checkpoint/cancel/retry/recovery trước khi thêm OCR/STT/ML runtime.

Task chính xác tiếp theo: **E4.1 — chốt state machine và schema local job idempotent trong `project.db`, thêm test transition/race/recovery; chưa cài model hay gọi provider thật.**

### Checklist bắt đầu phiên triển khai tiếp theo

1. Đọc `AGENTS.md`, `README.md`, `NGHIEP_VU_HE_THONG_VIDEOMAKER.md`, `KE_HOACH_SERVER_AI_GATEWAY.md`.
2. Đọc file task này và mục 0 của `KE_HOACH_TICH_HOP_MODULE_VIETSUB_DOC_LAP.md`.
3. Chạy `git status --short`; giữ nguyên thay đổi không liên quan của người dùng.
4. Chạy baseline restore/build/test trước phase lớn nếu chưa có mốc mới đáng tin cậy.
5. Bắt đầu E4/Gate 6 bằng local job engine, chưa thêm ML package.
6. Chỉ đánh dấu checkbox khi code + test tương ứng đã thực sự đạt.

### Mẫu cập nhật sau mỗi phase

```text
Ngày:
Phase/task đã hoàn tất:
File source đã thay đổi:
Migration local/server đã thêm:
Kết quả npm build/test:
Kết quả dotnet restore/build/test:
Smoke test đã chạy/chưa chạy:
Rủi ro hoặc việc còn mở:
Task chính xác tiếp theo:
```
