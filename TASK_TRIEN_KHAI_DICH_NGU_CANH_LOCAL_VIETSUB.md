# Task triển khai dịch phụ đề local theo ngữ cảnh cho Vietsub

> Ngày lập kế hoạch: `2026-09-05`.
>
> Trạng thái: **chưa triển khai**. File này là kế hoạch bàn giao cho AI khác thực hiện; việc tạo file không đồng nghĩa đã có code, package, model hoặc UI dịch local.
>
> Phạm vi: nhận track phụ đề nguồn đã có từ OCR/SRT, dịch `English -> Vietnamese` và `Chinese -> Vietnamese` hoàn toàn local, có ngữ cảnh, checkpoint, bảo vệ cue thủ công/đã khóa, kiểm tra chất lượng và xuất SRT tiếng Việt.
>
> Không thuộc phạm vi: Cloud translation, API/provider key trên desktop, thay đổi budget/usage Server, giọng đọc, audio mix hoặc export MP4.

## 0. Chỉ dẫn bắt đầu cho AI triển khai

Trước khi sửa source:

1. Đọc `AGENTS.md`, `README.md`, `NGHIEP_VU_HE_THONG_VIDEOMAKER.md` và `KE_HOACH_SERVER_AI_GATEWAY.md`.
2. Đọc thêm `TOOL-LOCAL/AGENTS.md`, `TOOL-TESTS/AGENTS.md`, file này, `KE_HOACH_TICH_HOP_MODULE_VIETSUB_DOC_LAP.md`, `KE_HOACH_TICH_HOP_OCR_TU_TOOL_VIETSUB.md` và `TASK_TRIEN_KHAI_VIETSUB_EDITOR_WORKSPACE.md`.
3. Chạy `git status --short` và giữ nguyên toàn bộ thay đổi không liên quan. Tại thời điểm lập kế hoạch, repository đang có nhiều thay đổi OCR/timeline chưa commit; không được reset, checkout hoặc ghi đè chúng.
4. Kiểm tra lại source hiện hành trước khi dựa vào số dòng hoặc trạng thái trong file này. Source và migration là nguồn sự thật kỹ thuật.
5. Chạy baseline restore/build/test trước lát cắt source đầu tiên nếu chưa có mốc mới đáng tin cậy.
6. Không chạy migration SQL Server thật, không gọi provider Cloud, không publish release và không tải model ngoài allowlist khi chưa có phê duyệt rõ ràng.
7. Thực hiện theo từng phase nhỏ. Chỉ tích `[x]` khi code và test tương ứng đã thực sự đạt.

Nguồn tham chiếu logic, chỉ đọc và thích nghi:

```text
D:\laptrinhweb\code_outsrc\TOOL_VIETSUB\TOOL_VIETSUB
```

Các file tham chiếu hữu ích:

- `TOOL_VIETSUB_APP/Translation/TranslationContracts.cs`
- `TOOL_VIETSUB_APP/Translation/TranslationScenePlanner.cs`
- `TOOL_VIETSUB_APP/Translation/TranslationResultCache.cs`
- `TOOL_VIETSUB_APP/Translation/LocalTranslationContextResolver.cs`
- `TOOL_VIETSUB_APP/Translation/LocalTranslationProviderAdapter.cs`
- `TOOL_VIETSUB_APP/LocalAi/TranslationQualityValidator.cs`
- `TOOL_VIETSUB_APP/Jobs/TranslationJobExecutor.cs`
- `TOOL_VIETSUB_APP/Core/ProjectModels.cs`
- `TOOL_VIETSUB_APP/Subtitles/SrtService.cs`
- `TOOL_VIETSUB_APP/LocalAi/LocalLanguageServices.cs`

Không copy các phần sau từ repository tham chiếu:

- `ProtectedTranslationCredentialStore` hoặc bất kỳ kho API key/DPAPI provider nào.
- `CloudTranslationProviders.cs`, direct OpenAI/Gemini/DeepSeek/Groq client hoặc fallback Cloud.
- `ServerManagedTranslationProvider.cs` trong phase local này.
- UI chọn API key, base URL, provider/model Cloud tùy ý.
- URL, version, checksum, binary, Python runtime hoặc model cũ nếu chưa review lại license/provenance/package.
- Project manifest, database, job manager hoặc app shell nguyên khối của TOOL_VIETSUB.

## 1. Hiện trạng source đã xác minh

### 1.1. Phần đã có và phải tái sử dụng

- Vietsub có project/workspace local độc lập theo organization và owner.
- `project.db` đang ở schema 3, dùng SQLite WAL và đã có:
  - `subtitle_tracks`;
  - `subtitle_cues`;
  - `subtitle_artifacts`;
  - `local_jobs`;
  - `local_job_steps`;
  - `local_job_events`.
- `VietsubSubtitleCue` đã có `OriginalText`, `TranslatedText`, `OriginalLocked`, `TranslationLocked`, `QualityStatus` và `Warnings`.
- Editor đã cho phép nhập/sửa bản dịch thủ công, lọc cue chưa dịch/đã dịch/cảnh báo và xuất SRT `ORIGINAL` hoặc `TRANSLATED`.
- Khi người dùng sửa bản dịch thủ công, `TranslationLocked` được bật; đây là dữ liệu phải được bảo vệ.
- Local job engine đã có state machine, persistence, global concurrency, pause/resume/retry/cancel, checkpoint, progress event và crash recovery.
- `VietsubJobTypes` đã dành sẵn `TRANSLATE_LOCAL`, nhưng chưa có executor.
- OCR local đã tạo track nguồn `PADDLE_OCR_LOCAL`; cue OCR chỉ có `OriginalText`, bản dịch ban đầu rỗng.
- OCR đã có mẫu lưu track + checkpoint nguyên tử qua `SaveTrackAndJobCheckpointAsync`.
- Native local-job authorization đã kiểm tra user, organization hiện hành, project owner, session, device/license và membership role.
- React/WebView bridge Vietsub đã có request ID, payload limit, busy riêng và generic job control.

### 1.2. Phần chưa có

- Không có thư mục implementation `TOOL-LOCAL/Vietsub/Translation`.
- Không có translation scene/chapter planner trong repository đích.
- Không có local translation provider/engine adapter.
- Không có translation executor được đăng ký trong `VietsubJobExecutorRegistry`.
- Không có translation context, glossary hoặc Translation Memory trong manifest/SQLite đích.
- Không có cue/configuration fingerprint hoặc translation result cache.
- Không có `translation_job_items` để resume theo cue/batch.
- Không có runtime/model provisioning cho dịch local.
- Không có bridge message bắt đầu/configure/status dịch local.
- UI **Dịch tự động** hiện chỉ là placeholder; chỉ sửa tay theo cue.
- `TOOL-SERVER` hiện chỉ có registry metadata Vietsub; không cần mở rộng Server cho task local này.
- `TOOL-SHARED.Contracts/Vietsub` hiện chỉ có DTO project metadata; không cần thêm public DTO cho task local này.

### 1.3. Lưu ý về trạng thái OCR

OCR đã chạy ở mức source và automated test nhưng các gate package/benchmark/clean-machine smoke test vẫn còn mở. Task dịch local không được làm lại OCR hoặc đánh dấu OCR đã phát hành. Nếu cần fixture đầu vào, ưu tiên dùng track/SRT fixture độc lập để test translation không bị phụ thuộc vào runtime OCR nặng.

## 2. Mục tiêu nghiệp vụ và Definition of Done

Người dùng phải có thể:

1. Mở project Vietsub có active track nguồn từ OCR hoặc SRT.
2. Chọn nguồn `English` hoặc `Chinese`; đích luôn là `Vietnamese` trong V1.
3. Nhập ngữ cảnh video, nhân vật/cách xưng hô, phong cách dịch và glossary.
4. Bấm **Dịch tiếp**, **Dịch lại lỗi** hoặc **Dịch lại cue chưa khóa**.
5. Theo dõi progress/ETA, pause, resume, cancel và retry.
6. Đóng/mở app rồi tiếp tục từ checkpoint mà không mất hoặc dịch trùng cue.
7. Xem trạng thái `PENDING`, `VALID`, `REVIEW`, `INVALID` hoặc `MANUAL_REVIEWED` theo cue.
8. Sửa tay/khóa bản dịch và đảm bảo mọi lần chạy sau không ghi đè.
9. Lọc cue cần kiểm tra và xuất SRT tiếng Việt UTF-8 nguyên tử.

Điều kiện hoàn thành kỹ thuật:

- [ ] English -> Vietnamese chạy hoàn toàn offline trên fixture thật đã duyệt.
- [ ] Chinese -> Vietnamese chạy hoàn toàn offline trên fixture thật đã duyệt.
- [ ] Bộ fixture mơ hồ chứng minh context thực sự làm thay đổi/ổn định kết quả đúng yêu cầu; không chỉ chứng minh batch sentence translation.
- [ ] Không có request OpenAI/Kling/Cloud hoặc provider credential trong luồng local.
- [ ] Cue manual/locked và cue đã đổi sau lúc job bắt đầu không bị ghi đè.
- [ ] Resume/retry không gọi lại hoặc commit trùng item đã hoàn tất với đúng fingerprint.
- [ ] Thay source/context/glossary/engine version làm cache/fingerprint invalidation đúng phạm vi.
- [ ] Output lỗi bệnh lý không thay thế bản dịch tốt cũ.
- [ ] Result SRT dịch đúng track/revision, UTF-8 và được publish nguyên tử.
- [ ] Viewer, sai user/organization/project hoặc license/session không hợp lệ bị chặn trước khi chạy model.
- [ ] Model/runtime sai checksum hoặc thiếu file bị từ chối rõ ràng, không crash app.
- [ ] Web build, Release build và toàn bộ test solution đạt.
- [ ] Có benchmark CPU/RAM/thời gian/dung lượng package và smoke test máy sạch trước khi bật mặc định/phát hành.

## 3. Định nghĩa “dịch local theo ngữ cảnh”

Context tối thiểu của một scene request gồm:

- cue mục tiêu;
- cue trước/sau trong cùng chapter;
- timestamp/duration;
- speaker nếu người dùng đã phân loại;
- bản dịch tiếng Việt đã duyệt ở cue lân cận;
- tóm tắt/chủ đề video;
- nhân vật, quan hệ và quy tắc xưng hô;
- phong cách dịch;
- glossary bắt buộc;
- Translation Memory phù hợp;
- giới hạn ký tự gợi ý theo thời lượng cue.

Không coi các hành vi sau là “ngữ cảnh hoàn chỉnh”:

- chỉ gửi một danh sách câu độc lập vào Argos/OPUS-MT;
- chỉ reuse exact Translation Memory;
- chỉ replace glossary sau dịch;
- chỉ batch nhiều câu nhưng model không nhìn được quan hệ giữa các câu;
- dùng cùng một bản dịch cho câu giống nhau dù speaker/context khác nhau.

### Gate chất lượng bắt buộc đối với engine

Argos `en -> vi` và OPUS-MT `zh -> vi` ở repository tham chiếu là baseline sentence-level. Chúng có thể dùng để tạo draft hoặc local-basic, nhưng không tự động được coi là đã đáp ứng context thật.

Trước khi khóa engine phải benchmark ít nhất hai kiến trúc:

1. Engine/model local nhận được cả scene context và trả mapping cue có cấu trúc.
2. Pipeline hai lượt: NMT local tạo draft, một local contextual reviewer sửa xưng hô/ý nghĩa/thuật ngữ theo scene.

Nếu cuối cùng chỉ triển khai Argos/OPUS-MT + memory/glossary, UI và tài liệu phải gọi đúng là **dịch local cơ bản/nhất quán thuật ngữ**. Task này chỉ được đóng với nhãn **dịch theo ngữ cảnh** khi fixture context đạt acceptance đã chốt.

## 4. Các quyết định kiến trúc đã khóa cho task

### ADR-LT-01 — Local-only, không chạm AI Gateway

- Không thêm API/DTO/migration SQL Server cho phase này.
- Không gửi subtitle, context, video, audio hoặc frame lên Server để dịch.
- Không reserve provider budget và không ghi provider usage.
- Vẫn tái sử dụng session/license/organization/membership để phân quyền local job theo bất biến sản phẩm.
- `Owner`, `OrganizationAdmin`, `BillingManager`, `Member` được chạy; `Viewer` bị chặn.

### ADR-LT-02 — Dịch trên cùng track nguồn

- `OriginalText` tiếp tục là nguồn.
- Local translation ghi vào `TranslatedText` của đúng cue trong input track.
- Không tạo một subtitle track mới cho mỗi lần dịch.
- `OutputTrackId` của translation job có thể trỏ lại input track để UI/recovery đối chiếu, nhưng không tự activate track khác.
- Artifact `SRT_TRANSLATED` gắn đúng track ID + revision.

### ADR-LT-03 — Manual edit là dữ liệu ưu tiên cao nhất

- Bản dịch do người dùng sửa và lưu phải có source `MANUAL`, status `MANUAL_REVIEWED` và `TranslationLocked=true`.
- Executor không được ghi đè cue locked trong mọi run mode.
- Manual edit có thể cập nhật Translation Memory theo policy rõ ràng.
- Auto result không tự khóa cue; người dùng vẫn có thể duyệt/sửa/dịch lại.

### ADR-LT-04 — Job dài chạy qua job engine hiện có

- Bridge chỉ validate, enqueue/start và trả summary.
- Executor chạy nền qua `VietsubJobManager`.
- Getter state/status không được tự start/resume.
- Progress phải throttle/coalesce theo cơ chế hiện có.
- Pause/cancel chỉ tại checkpoint an toàn.
- Không tạo một job manager hoặc global semaphore thứ hai.

### ADR-LT-05 — Context/fingerprint là ranh giới chống stale result

- Mỗi target cue có input fingerprint riêng.
- Fingerprint phải bao gồm source cue, context liên quan, config và engine version.
- Result chỉ được apply khi fingerprint hiện tại còn khớp và cue chưa locked/đổi thủ công.
- Không chỉ dựa vào track revision toàn cục vì executor tự tăng revision khi commit từng batch; cần phân biệt revision do chính job tạo với concurrent user edit.

### ADR-LT-06 — Dữ liệu lớn ở SQLite, manifest chỉ giữ settings nhỏ

- Không nhét Translation Memory, result cache, item progress hoặc toàn bộ cue vào `project.json`.
- Manifest chỉ giữ settings/context/glossary trong giới hạn nhỏ và versioned.
- Translation Memory, cache và job item nằm trong `project.db` schema 4.
- Không tạo migration SQL Server.

### ADR-LT-07 — Provider-neutral core, local adapter thật thà về capability

- Core scene/planner/result/quality không phụ thuộc Argos, OPUS hay một runtime cụ thể.
- Adapter phải khai báo capability như `SupportsSceneContext`, `SupportsReviewPass`, batch limit và language pair.
- UI không được quảng bá context/review nếu adapter thực tế không hỗ trợ.
- Không cho người dùng nhập executable, model path hoặc URL tùy ý.

### ADR-LT-08 — Runtime/model là optional component có kiểm chứng

- Mỗi component có ID, version, architecture, size, license, provenance và SHA-256.
- Tải qua `.partial`, verify rồi publish nguyên tử.
- Không tải ngầm model lớn.
- Runtime probe phải load/infer tối thiểu thực tế, không chỉ kiểm tra file tồn tại.
- Rollback component không được xóa project, cue hoặc manual translation.

### ADR-LT-09 — Không fallback âm thầm

- Không fallback sang Cloud trong task này.
- Nếu engine contextual lỗi và có local-basic fallback, UI/job metadata phải ghi rõ engine thực tế đã dùng và hạ mức chất lượng/cảnh báo.
- Không đổi model/version giữa một job đang chạy nếu không tạo attempt/fingerprint mới.

## 5. Kiến trúc đích

```text
React Vietsub settings/editor
  -> vietsub.translation.* / vietsub.job.translate
  -> VietsubWebBridge
  -> VietsubTranslationService
       -> IVietsubLocalJobAuthorizer
       -> ILocalTranslationRuntime/Registry
       -> VietsubJobManager.EnqueueAsync
  -> VietsubTranslationJobExecutor
       -> TranslationScenePlanner
       -> TranslationMemoryStore / TranslationResultCache
       -> ILocalTranslationProvider
       -> TranslationQualityValidator
       -> atomic cue + job-item + checkpoint commit
  -> VietsubSubtitleStore / project.db schema 4
  -> subtitle.changed / job.changed
  -> review/edit/export SRT translated
```

Cấu trúc file dự kiến:

```text
TOOL-LOCAL/Vietsub/Translation/
  VietsubTranslationContracts.cs
  VietsubTranslationSettings.cs
  VietsubTranslationScenePlanner.cs
  VietsubTranslationFingerprintBuilder.cs
  VietsubTranslationContextResolver.cs
  VietsubTranslationQualityValidator.cs
  VietsubTranslationStore.cs
  VietsubTranslationResultCache.cs
  IVietsubLocalTranslationProvider.cs
  VietsubLocalTranslationProviderAdapter.cs
  VietsubTranslationRuntimeRegistry.cs
  VietsubTranslationService.cs
  VietsubTranslationJobExecutor.cs

TOOL-LOCAL/Web/src/features/vietsub/
  VietsubTranslationPanel.tsx
```

Tên cuối có thể thay đổi để khớp convention source hiện hành; không tạo hai abstraction trùng vai trò.

## 6. Mô hình dữ liệu đề xuất

### 6.1. Manifest version mới

Thêm `TranslationSettings` theo hướng tương thích ngược:

```text
TranslationSettings
  SourceLanguageCode       en | zh
  TargetLanguageCode       vi
  EnginePolicy             giá trị allowlist nội bộ, không phải path/model tùy ý
  ContextCueCount
  SceneMaximumTargetCues
  SceneGapMilliseconds
  MaximumCharactersPerSecond
  ContextSummary
  CharacterInstructions
  StyleInstructions
  Glossary[]
```

Yêu cầu:

- [ ] Quyết định rõ tăng `VietsubProjectManifest.CurrentSchemaVersion` và viết migration in-memory từ manifest schema 1.
- [ ] Fixture manifest cũ mở được với default an toàn.
- [ ] Source/target language không mâu thuẫn với field hiện có của project.
- [ ] Context/glossary có giới hạn độ dài, số mục và ký tự control.
- [ ] Không chứa cue, result cache, secret, executable path hoặc URL model tùy ý.

Giới hạn khởi điểm để review khi code:

- summary: tối đa 4.000 ký tự;
- character/addressing: tối đa 4.000 ký tự;
- style: tối đa 2.000 ký tự;
- glossary: tối đa 200 mục hoặc 20.000 ký tự tổng;
- source/target của mỗi glossary item: tối đa 200 ký tự;
- note: tối đa 300 ký tự.

### 6.2. Mở rộng `subtitle_cues`

Giữ các cột hiện có và thêm nullable/default để migration an toàn:

```text
translation_source
translation_engine_id
translation_engine_version
translation_source_fingerprint
translation_confidence
translation_reviewed_at_utc
```

Tái sử dụng:

```text
translated_text
translation_locked
quality_status
warning_json
updated_at_utc
```

Quy tắc:

- `MANUAL` luôn thắng `LOCAL_AUTO`.
- Auto translation hợp lệ không tự đặt `translation_locked=1`.
- Manual edit phải xóa model/fingerprint auto cũ hoặc ghi metadata source rõ ràng.
- Khi original text thay đổi, bản dịch auto cũ trở thành stale/được làm rỗng theo policy; bản manual locked không bị xóa âm thầm nhưng phải có cảnh báo source changed nếu cần.

### 6.3. Bảng `translation_memory`

Trường tối thiểu:

```text
entry_id
project_id
source_language_code
target_language_code
normalized_source_hash
source_text
translated_text
context_fingerprint nullable
source_kind              MANUAL_APPROVED | IMPORTED_APPROVED
use_count
created_at_utc
updated_at_utc
```

Quy tắc:

- [ ] Exact match phải kiểm tra language pair.
- [ ] Câu giống nhau nhưng speaker/context làm đổi nghĩa không được reuse mù.
- [ ] Ưu tiên entry manual mới nhất.
- [ ] Giới hạn mặc định 500 mục gần nhất/dùng nhiều nhất.
- [ ] Không đưa auto result chưa duyệt, `REVIEW` hoặc `INVALID` vào memory mặc định.

### 6.4. Bảng `translation_cache`

Trường tối thiểu:

```text
cache_key
project_id
engine_id
engine_version
configuration_fingerprint
input_fingerprint
result_json
created_at_utc
last_used_at_utc
```

Quy tắc:

- Cache key là SHA-256 của canonical payload/version, không phải chỉ source text.
- Cache hit vẫn phải chạy structural/quality validation trước apply.
- File/model/context/glossary/version thay đổi phải miss cache.
- Cache hỏng JSON được bỏ qua an toàn, không làm hỏng project.
- Có retention/size cap và cleanup project-scoped.

### 6.5. Bảng `translation_job_items`

Trường tối thiểu:

```text
job_id
cue_id
scene_number
chapter_number
input_fingerprint
status                  PENDING | RUNNING | COMPLETED | REVIEW | INVALID | STALE | FAILED | SKIPPED_LOCKED
attempt_count
translated_text nullable
confidence nullable
warning_json
error_code nullable
created_at_utc
updated_at_utc
completed_at_utc nullable
```

Khóa đề xuất: `(job_id, cue_id)`.

Mục tiêu:

- resume không xử lý lại item completed cùng fingerprint;
- retry failed không đụng item valid;
- lưu lý do skip/stale mà không log toàn bộ subtitle ra event log;
- đối chiếu được progress sau app restart.

### 6.6. Migration SQLite schema 3 -> 4

- [ ] Migration idempotent, transaction và rollback khi lỗi.
- [ ] Không sửa ngược migration schema 1/2/3 đã tồn tại ngoài phần dispatcher cần thiết.
- [ ] Mở project schema 3 tạo đủ bảng/cột/index rồi cập nhật `schema_info=4` sau cùng.
- [ ] Mở lại schema 4 không tạo duplicate/index conflict.
- [ ] Schema lớn hơn version hỗ trợ phải fail closed với lỗi rõ.
- [ ] Có fixture schema 1/2/3 và test dữ liệu cue/artifact/job cũ giữ nguyên.
- [ ] Có test migration bị gián đoạn không để database ở trạng thái khai đã là schema 4 nhưng thiếu object.

## 7. Translation core và context planner

### Phase LT-0 — Baseline, engine gate và acceptance dataset

- [ ] LT0-01 Ghi baseline Git/status mà không thay đổi dữ liệu người dùng.
- [ ] LT0-02 Chạy restore/build/test baseline và ghi số test thực tế.
- [ ] LT0-03 Tạo bộ fixture Anh/Trung chứa đại từ, xưng hô, tỉnh lược, tên riêng, số liệu, thuật ngữ và câu trùng khác context.
- [ ] LT0-04 Chốt thước đo: giữ nghĩa, nhất quán tên/xưng hô, glossary, số liệu, độ dài và lỗi lặp.
- [ ] LT0-05 Review license/provenance/redistribution của mọi runtime/model ứng viên.
- [ ] LT0-06 Benchmark Argos/OPUS baseline và ít nhất một phương án contextual offline hoặc two-pass offline.
- [ ] LT0-07 Chốt engine ID/version, CPU/GPU/RAM/disk minimum và package strategy.
- [ ] LT0-08 Ghi rõ capability matrix, không suy diễn từ tên model.

Điều kiện qua LT-0:

- Có engine/pipeline offline được phê duyệt cho cả hai language pair hoặc có quyết định tách milestone rõ ràng.
- Có bằng chứng engine/pipeline đạt context fixture; nếu chưa đạt, không chuyển UI placeholder thành “dịch ngữ cảnh sẵn sàng”.

### Phase LT-1 — Contract/provider-neutral core

- [ ] LT1-01 Tạo input/result/pass/run-mode contract nội bộ.
- [ ] LT1-02 Chuẩn hóa source/target language `en|zh -> vi`; unsupported pair fail rõ.
- [ ] LT1-03 Thêm capability contract cho engine.
- [ ] LT1-04 Thêm giới hạn target cue, context cue, source characters và output characters.
- [ ] LT1-05 Dùng cue alias cục bộ nếu engine trả structured result; không phụ thuộc raw GUID do model lặp lại.
- [ ] LT1-06 Validate output đúng một item cho mỗi target cue, không thiếu/thừa/trùng và đúng mapping.
- [ ] LT1-07 Unit test serialization, normalization, limits và invalid result.

### Phase LT-2 — Scene/chapter planner

- [ ] LT2-01 Sort cue theo timeline và kiểm tra timestamp hợp lệ.
- [ ] LT2-02 Tách chapter theo gap; default tham khảo `8.000 ms` nhưng phải benchmark với OCR thực tế.
- [ ] LT2-03 Giới hạn chapter, tham khảo tối đa `10 phút`.
- [ ] LT2-04 Pack target theo số cue và tổng source character, không chỉ count.
- [ ] LT2-05 Thêm cue trước/sau nhưng không vượt ranh giới chapter.
- [ ] LT2-06 Dùng bản dịch locked/manual lân cận làm Vietnamese continuity.
- [ ] LT2-07 Build chapter summary từ representative source lines trong giới hạn.
- [ ] LT2-08 Tính suggested max characters theo cue duration.
- [ ] LT2-09 Mỗi target cue phải xuất hiện đúng một lần.
- [ ] LT2-10 Cue locked không là target nhưng được phép làm context.

Test bắt buộc:

- target đầu/cuối track;
- gap lớn giữa hai scene;
- chapter dài;
- target pack vượt character cap;
- cue overlap;
- context count 0 và maximum;
- locked Vietnamese continuity;
- cùng source text ở hai speaker/context khác nhau.

### Phase LT-3 — Fingerprint, memory, glossary và cache

- [ ] LT3-01 Canonicalize payload ổn định, không phụ thuộc dictionary order hoặc culture máy.
- [ ] LT3-02 Configuration fingerprint gồm language pair, engine/version, settings, project context, character rules, style, glossary và planner version.
- [ ] LT3-03 Cue fingerprint gồm source/timestamp/speaker và cửa sổ context liên quan.
- [ ] LT3-04 Chỉ đưa translated context vào fingerprint khi đó là manual/locked/approved.
- [ ] LT3-05 Translation Memory exact match trước model nhưng phải xét context compatibility.
- [ ] LT3-06 Glossary match longest-first, có word/token boundary phù hợp; không replace substring mù.
- [ ] LT3-07 Cache đọc/ghi nguyên tử trong transaction SQLite.
- [ ] LT3-08 Context/glossary/model đổi làm đúng cue stale; không invalidate manual locked cue.
- [ ] LT3-09 Test cache corrupt, stale cache, repeated source và concurrent read/write.

## 8. Runtime và local provider

### Phase LT-4 — Runtime/model registry

- [ ] LT4-01 Tạo manifest allowlist cho runtime/model đã được duyệt.
- [ ] LT4-02 Mỗi artifact có version, size, SHA-256, source/provenance và license.
- [ ] LT4-03 Download/copy qua `.partial`, kiểm hash rồi publish atomic nếu sản phẩm cho phép cài component.
- [ ] LT4-04 Không cho tùy ý URL/path/executable từ WebView hoặc project manifest.
- [ ] LT4-05 Runtime status phân biệt `READY`, `NOT_INSTALLED`, `INVALID`, `UNSUPPORTED_HARDWARE`, `BUSY`.
- [ ] LT4-06 Probe load/infer model thật cho `en -> vi` và `zh -> vi`.
- [ ] LT4-07 Kiểm tra disk/RAM/CPU trước start.
- [ ] LT4-08 Nếu dùng worker process: UTF-8, timeout, stdin/stdout protocol có giới hạn, stderr cap và kill process tree khi cancel.
- [ ] LT4-09 Không ghi source/translation đầy đủ vào runtime log mặc định.
- [ ] LT4-10 Test component thiếu/sai hash/sai version/process treo/process crash/output quá lớn.

### Phase LT-5 — Local translation adapter

- [ ] LT5-01 Implement `ILocalTranslationProvider` cho engine/pipeline đã chốt.
- [ ] LT5-02 Route đúng language pair; không âm thầm dùng model sai ngôn ngữ.
- [ ] LT5-03 Tôn trọng scene/batch/context capability thực tế.
- [ ] LT5-04 Exact memory hit không gọi model.
- [ ] LT5-05 Repeated source chỉ reuse khi context compatible.
- [ ] LT5-06 Apply glossary theo policy và sinh warning khi không thể bảo toàn.
- [ ] LT5-07 Không trả item rỗng hoặc sai mapping.
- [ ] LT5-08 Snapshot engine ID/version vào result/job item.
- [ ] LT5-09 Nếu two-pass: lưu rõ draft/reviewer attempt và không ghi đè draft tốt bằng reviewer invalid.
- [ ] LT5-10 Không fallback Cloud.

## 9. Quality validator và trạng thái cue

### Phase LT-6 — Validation/review policy

Validator tối thiểu:

- [ ] LT6-01 Rỗng/whitespace-only.
- [ ] LT6-02 Output quá dài bất thường so với source.
- [ ] LT6-03 Repeated-token run và repeated-phrase loop.
- [ ] LT6-04 Không phạt nhầm lặp có chủ đích đã tồn tại trong source.
- [ ] LT6-05 Number mismatch.
- [ ] LT6-06 Glossary missing.
- [ ] LT6-07 Reading speed cao theo duration cue.
- [ ] LT6-08 Confidence thấp nếu engine có confidence.
- [ ] LT6-09 Source-language leakage vượt ngưỡng đã chốt.
- [ ] LT6-10 Suggested maximum characters vượt nhiều.
- [ ] LT6-11 Invalid structured/mapping result.

Semantics:

- `VALID`: output qua validator, không warning.
- `REVIEW`: output dùng được nhưng có warning; được lưu để người dùng xem.
- `INVALID`: output không an toàn; không ghi đè translated text tốt trước đó.
- `MANUAL_REVIEWED`: người dùng sửa/lưu; locked và ưu tiên cao nhất.
- `PENDING`: chưa có output có thể dùng.

Không đổi ý nghĩa field hiện có âm thầm. Phải cập nhật đồng thời C# domain/store/service, bridge DTO nội bộ, TypeScript type/UI và test.

## 10. Job service, executor và recovery

### Phase LT-7 — `VietsubTranslationService`

- [ ] LT7-01 Reuse/generalize `IVietsubLocalJobAuthorizer` để lỗi không còn mang tên OCR sai ngữ cảnh; giữ tương thích OCR.
- [ ] LT7-02 Validate project/session/organization/owner/license/membership trước runtime/model.
- [ ] LT7-03 Validate active track, track revision, cue count, source language và target `vi`.
- [ ] LT7-04 Chặn Viewer và unsupported role.
- [ ] LT7-05 Chặn start khi có active heavy local job theo unique index/job manager hiện có.
- [ ] LT7-06 Snapshot settings/context/glossary/planner/engine version trong job parameters bằng metadata có giới hạn; không nhét toàn bộ track.
- [ ] LT7-07 Enqueue `TRANSLATE_LOCAL` với input track ID/revision và step code ổn định.
- [ ] LT7-08 Trả error code ổn định cho UI.

Error code dự kiến, tên cuối phải thống nhất:

```text
TRANSLATION_ACCESS_DENIED
TRANSLATION_LICENSE_REQUIRED
TRANSLATION_SOURCE_TRACK_REQUIRED
TRANSLATION_SOURCE_LANGUAGE_REQUIRED
TRANSLATION_LANGUAGE_UNSUPPORTED
TRANSLATION_RUNTIME_NOT_INSTALLED
TRANSLATION_RUNTIME_INVALID
TRANSLATION_MODEL_NOT_READY
TRANSLATION_CONTEXT_INVALID
TRANSLATION_GLOSSARY_INVALID
TRANSLATION_JOB_CONFLICT
TRANSLATION_TRACK_CHANGED
TRANSLATION_RESULT_INVALID
TRANSLATION_PROCESS_FAILED
TRANSLATION_PROCESS_TIMEOUT
TRANSLATION_JOB_NOT_RESUMABLE
```

### Phase LT-8 — `VietsubTranslationJobExecutor`

- [ ] LT8-01 Resolve đúng input track đã snapshot; không tự lấy active track mới khi resume.
- [ ] LT8-02 Tạo target set theo run mode.
- [ ] LT8-03 Bỏ cue locked trước planner và ghi item `SKIPPED_LOCKED` nếu cần đối chiếu.
- [ ] LT8-04 Lập scene/chapter và tạo item/checkpoint ban đầu.
- [ ] LT8-05 Xử lý tuần tự mặc định; chỉ tăng concurrency sau benchmark chứng minh không phá continuity/RAM.
- [ ] LT8-06 Ưu tiên memory/cache trước model.
- [ ] LT8-07 Lưu result/item/checkpoint theo từng scene/batch.
- [ ] LT8-08 Trước apply phải reload cue hiện tại và kiểm lock/source/fingerprint.
- [ ] LT8-09 Concurrent manual edit hoặc source edit -> `STALE`, không overwrite.
- [ ] LT8-10 Executor tự tăng track revision theo commit nhưng không tự coi commit của chính mình là external conflict.
- [ ] LT8-11 Invalidate artifact `SRT_TRANSLATED`, voice/export artifact tương lai đúng track/revision.
- [ ] LT8-12 Complete tạo/cập nhật `SRT_TRANSLATED` qua `.partial` + atomic move.
- [ ] LT8-13 Pause/cancel tại checkpoint; không để worker process sống sót.
- [ ] LT8-14 Resume không dịch lại completed item cùng fingerprint.
- [ ] LT8-15 Retry failed chỉ retry `FAILED/INVALID` theo policy.
- [ ] LT8-16 Restart unlocked bỏ cache/fingerprint của cue auto chưa khóa nhưng giữ manual/locked.
- [ ] LT8-17 Metrics chỉ chứa count/time/cache hit/model version, không log toàn văn subtitle.

Run mode chi tiết:

```text
CONTINUE
  - target: PENDING, INVALID, stale fingerprint, hoặc auto result cần xử lý lại theo policy
  - skip: manual/locked và auto VALID còn đúng fingerprint

RETRY_FAILED
  - target: item FAILED/INVALID của job/track hiện hành
  - skip: VALID/REVIEW còn đúng fingerprint, manual/locked

RESTART_UNLOCKED
  - target: toàn bộ cue chưa khóa
  - bypass result cache cũ nếu mục đích là tạo lại thật
  - giữ manual/locked tuyệt đối
```

## 11. Bridge và UI

### Phase LT-9 — WebView bridge

Message dự kiến:

```text
vietsub.translation.runtime.status
vietsub.translation.settings.update
vietsub.translation.memory.summary
vietsub.job.translate
```

Payload `vietsub.job.translate` tối thiểu:

```text
runMode: CONTINUE | RETRY_FAILED | RESTART_UNLOCKED
expectedTrackId
expectedTrackRevision
```

Quy tắc:

- [ ] Không gửi toàn bộ track qua bridge khi start; native side đọc SQLite.
- [ ] Mọi message có request ID và validation native.
- [ ] Giữ `MaxMessageLength`; context/glossary phải bị giới hạn cả React và C#.
- [ ] Không gửi local absolute path/model executable vào DOM.
- [ ] Không thêm API key/provider URL/model Cloud.
- [ ] Generic `vietsub.job.pause/resume/retry/cancel/status` tiếp tục dùng chung.
- [ ] `vietsub.state.get` không tự chạy/reconcile nặng ngoài logic recovery được thiết kế rõ.
- [ ] `subtitle.changed` chỉ gửi delta/reset cần thiết, không đẩy 20.000 cue.

### Phase LT-10 — React UI

Tạo panel **Dịch local** trong editor:

- [ ] LT10-01 Runtime status và nút cài/sửa component nếu policy cho phép.
- [ ] LT10-02 Source `English/Chinese`, target cố định `Vietnamese`.
- [ ] LT10-03 Tóm tắt/chủ đề video.
- [ ] LT10-04 Nhân vật và cách xưng hô.
- [ ] LT10-05 Phong cách dịch.
- [ ] LT10-06 Glossary editor với format/validation rõ.
- [ ] LT10-07 Translation Memory count và mô tả nguồn manual-approved.
- [ ] LT10-08 Capability label trung thực: local-basic hay contextual.
- [ ] LT10-09 **Dịch tiếp**.
- [ ] LT10-10 **Dịch lại lỗi**.
- [ ] LT10-11 **Dịch lại cue chưa khóa**, có confirmation về thời gian/tài nguyên.
- [ ] LT10-12 Progress/ETA/count `VALID/REVIEW/INVALID/STALE`.
- [ ] LT10-13 Pause/resume/cancel/retry dùng job control hiện có.
- [ ] LT10-14 Cue inspector filter đúng quality status/warning.
- [ ] LT10-15 Manual edit chuyển cue sang `MANUAL_REVIEWED`, cập nhật memory và không bị auto job ghi đè.
- [ ] LT10-16 Empty/error/recovery states và accessible label/keyboard focus.

Không hiển thị:

- API key;
- provider Cloud;
- base URL;
- model ID tự do;
- token/cost provider;
- local absolute path.

Lưu ý UX: OCR hiện mặc định speaker là `speaker_1`. Context xưng hô theo nhiều nhân vật chỉ đáng tin khi người dùng đã sửa speaker hoặc nhập quy tắc đủ rõ; UI cần nói đúng giới hạn này.

## 12. Test matrix bắt buộc

### 12.1. Unit

- [ ] Contract normalization và limit.
- [ ] Scene/chapter planner.
- [ ] Mỗi target cue đúng một lần.
- [ ] Context không vượt chapter.
- [ ] Character/source character caps.
- [ ] Fingerprint ổn định giữa culture/process.
- [ ] Fingerprint đổi khi source/context/glossary/engine đổi.
- [ ] Fingerprint không đổi bởi dữ liệu ngoài cửa sổ không liên quan nếu policy thiết kế như vậy.
- [ ] Memory exact/context-compatible match.
- [ ] Repeated source khác speaker/context không reuse sai.
- [ ] Glossary longest-first/boundary/duplicate/invalid line.
- [ ] Quality: empty, repetition, number, glossary, speed, confidence, leakage.
- [ ] Cache hit/miss/corrupt/invalidation.
- [ ] Run mode target selection.
- [ ] Manual/locked/stale protection.
- [ ] Schema 3 -> 4 migration.

### 12.2. Executor/recovery

- [ ] Complete một scene commit cue + item + checkpoint nhất quán.
- [ ] Crash trước commit không tạo half-applied item.
- [ ] Crash sau commit và trước progress event vẫn resume không trùng.
- [ ] Pause/resume ở ranh giới batch.
- [ ] Cancel kill process tree và giữ completed items.
- [ ] Retry process crash/timeout theo retryability rõ.
- [ ] Track bị xóa/đổi khi resume trả lỗi an toàn.
- [ ] Active track đổi không khiến job dịch nhầm track mới.
- [ ] User sửa/lock cue giữa provider call local và apply giữ bản user.
- [ ] Restart unlocked không xóa manual translations.
- [ ] Cache result invalid không được apply.

### 12.3. Local model integration

- [ ] English fixture thật, không skip âm thầm.
- [ ] Chinese fixture thật, không skip âm thầm.
- [ ] Context ambiguity fixture.
- [ ] Name/number/glossary fixture.
- [ ] Worker UTF-8 cho tiếng Trung/Việt.
- [ ] Runtime/model thiếu hoặc sai hash.
- [ ] Worker stderr lớn, output lớn, timeout, cancel và process tree.
- [ ] Máy không có GPU nếu CPU là cấu hình được hỗ trợ.

### 12.4. Authorization/security

- [ ] Owner/Admin/BillingManager/Member hợp lệ được chạy.
- [ ] Viewer bị chặn trước runtime/model execution.
- [ ] Sai user/organization/project owner bị chặn.
- [ ] Session/device/license invalid bị chặn.
- [ ] Không có outbound OpenAI/Kling/Cloud trong test local.
- [ ] Không có provider key/URL trong source mới, manifest, SQLite event log, bridge payload hoặc bundle.
- [ ] WebView không thể chỉ định executable/model path/URL.
- [ ] Project path/cache/artifact không traversal.

### 12.5. UI/regression

- [ ] Job dịch không khóa sidebar/title bar/logout ngoài phần cần thiết.
- [ ] Progress dày không làm WebView lag.
- [ ] Cue list/page/timeline không tải toàn bộ 20.000 cue.
- [ ] Manual edit debounce không va commit job.
- [ ] OCR, import SRT, timeline, playback, export original/translated hiện có không regression.
- [ ] Đổi organization đóng/reject job/session đúng policy, không cross-tenant.
- [ ] Feature flag tắt giữ dữ liệu và manual editor an toàn.

## 13. Benchmark và package gate

Phải ghi số thực tế cho ít nhất:

- thời gian cold start model;
- thời gian cho 100/1.000 cue;
- peak RAM;
- CPU utilization;
- disk size runtime + model;
- cache growth;
- thời gian cancel;
- chất lượng context fixture Anh/Trung;
- máy cấu hình thấp và máy mục tiêu chuẩn;
- project/video dài sau app restart.

Quyết định phát hành:

- [ ] Bundle hay optional component.
- [ ] Có thông báo dung lượng trước tải.
- [ ] Có provenance/license/third-party notice.
- [ ] Có checksum trong package/updater.
- [ ] Có clean-machine install/repair/uninstall/rollback smoke test.
- [ ] Không bật `VietsubLocalTranslationEnabled` mặc định trước khi gate hoàn tất.

## 14. Feature flag, rollback và tương thích

Đề xuất feature flag riêng:

```text
Features:VietsubLocalTranslationEnabled
```

Yêu cầu:

- Flag tắt ẩn/khóa action auto translation nhưng vẫn cho xem/sửa/export bản dịch đã có.
- Flag tắt không xóa model, cache, job, cue hoặc Translation Memory.
- Job đang chạy khi flag bị đổi cần policy rõ: hoàn tất an toàn hoặc pause/cancel tại checkpoint; không terminate mù.
- Manifest/schema mới vẫn đọc được khi tính năng tắt.
- Rollback app không được mở database schema mới bằng binary cũ nếu binary cũ fail không an toàn; phải chốt compatibility/updater policy trước release.

## 15. File dự kiến tác động

File hiện có có thể cần sửa:

```text
TOOL-LOCAL/Configuration/DesktopOptions.cs
TOOL-LOCAL/appsettings.json
TOOL-LOCAL/Program.cs
TOOL-LOCAL/Form1.cs
TOOL-LOCAL/Vietsub/Domain/VietsubProjectModels.cs
TOOL-LOCAL/Vietsub/Storage/VietsubProjectStore.cs
TOOL-LOCAL/Vietsub/Storage/VietsubSubtitleStore.cs
TOOL-LOCAL/Vietsub/Subtitles/VietsubSubtitleService.cs
TOOL-LOCAL/Vietsub/Jobs/VietsubJobModels.cs
TOOL-LOCAL/Vietsub/Jobs/VietsubJobExecutorRegistry.cs
TOOL-LOCAL/Vietsub/VietsubWebBridge.cs
TOOL-LOCAL/Web/src/features/vietsub/types.ts
TOOL-LOCAL/Web/src/features/vietsub/useVietsubModule.ts
TOOL-LOCAL/Web/src/features/vietsub/VietsubEditorWorkspace.tsx
TOOL-LOCAL/Web/src/features/vietsub/VietsubSettingsPanel.tsx
TOOL-LOCAL/Web/src/features/vietsub/VietsubSubtitleEditor.tsx
TOOL-LOCAL/Web/src/styles.css
TOOL-LOCAL/TOOL-LOCAL.csproj
TOOL-TESTS/TOOL-TESTS.csproj
```

File/thư mục mới dự kiến:

```text
TOOL-LOCAL/Vietsub/Translation/**
TOOL-LOCAL/Web/src/features/vietsub/VietsubTranslationPanel.tsx
TOOL-TESTS/Vietsub/VietsubTranslationDomainTests.cs
TOOL-TESTS/Vietsub/VietsubTranslationPersistenceTests.cs
TOOL-TESTS/Vietsub/VietsubTranslationPlannerTests.cs
TOOL-TESTS/Vietsub/VietsubTranslationExecutorTests.cs
TOOL-TESTS/Vietsub/VietsubLocalTranslationIntegrationTests.cs
third_party/translation/**
```

Không dự kiến sửa trong phase local:

```text
TOOL-SERVER/**
TOOL-SHARED.Contracts/**
database/*.sql
```

Nếu implementation phát hiện thực sự cần sửa ba khu vực trên, phải dừng, giải thích lý do và tách thành scope mới; không âm thầm mở Cloud/gateway trong task local.

## 16. Thứ tự triển khai khuyến nghị

```text
LT-0 Baseline + engine/package/context acceptance
  -> LT-1 Provider-neutral contracts
  -> LT-2 SQLite/manifest migration
  -> LT-3 Scene planner + fingerprint + memory/glossary/cache
  -> LT-4 Runtime/model registry
  -> LT-5 Local provider adapter
  -> LT-6 Quality policy
  -> LT-7 Translation service/authorization
  -> LT-8 Executor/checkpoint/recovery
  -> LT-9 WebView bridge
  -> LT-10 React UI
  -> Integration/security/performance/regression
  -> Clean-machine/package/release gate
```

Lát cắt commit/PR khuyến nghị:

1. Core contracts + planner + validator + unit tests, chưa nối UI/runtime.
2. Schema 4 + manifest migration + memory/cache/job-item store + persistence tests.
3. Runtime registry + selected engine adapter + real fixture tests.
4. Service + executor + checkpoint/recovery + concurrency tests.
5. Bridge + UI + manual memory + translated artifact.
6. Hardening, benchmark, package, docs và release flag.

Không gộp tất cả vào một thay đổi lớn khó review.

## 17. Lệnh xác minh bắt buộc

Khi chỉ sửa Web:

```powershell
Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
```

Sau mỗi lát cắt source hoàn chỉnh, chạy từ repository root:

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Test model/runtime thật phải dùng fixture local và không gọi provider Cloud. Không ghi nhận mốc pass mới nếu chưa thực sự chạy đủ lệnh tương ứng.

## 18. Những lỗi thiết kế phải tránh

- Dịch từng cue độc lập rồi gọi là dịch ngữ cảnh.
- Reuse cùng source text bất kể speaker/context.
- Gửi toàn bộ 20.000 cue qua WebView một lần.
- Lưu Translation Memory 500 mục trong manifest và rewrite theo từng cue.
- Dựa duy nhất vào track revision toàn cục rồi tự xung đột sau commit batch đầu tiên.
- Apply result mà không reload lock/source/fingerprint hiện tại.
- Clear manual translation trong `RESTART_UNLOCKED`.
- Đánh dấu `INVALID` nhưng vẫn ghi đè bản dịch tốt cũ.
- Replace glossary theo substring mù.
- Tự tải Python/model hàng GB không thông báo/checksum.
- Chỉ `File.Exists` rồi báo runtime ready.
- Copy direct Cloud client hoặc credential store từ TOOL_VIETSUB.
- Tạo server/provider usage giả cho local translation.
- Dùng log/event để lưu toàn văn subtitle hoặc context nhạy cảm không cần thiết.
- Làm lại OCR/job manager/subtitle store bằng một nguồn sự thật song song.
- Chạy migration database thật hoặc publish release khi chưa được người dùng cho phép.

## 19. Điểm dừng của task local

Task này kết thúc sau khi local translation đạt Definition of Done và package gate. Không tiếp tục tự động sang Cloud translation.

Cloud translation sau này là task riêng và phải có:

- shared DTO;
- Server access/project ownership;
- organization policy/model/credential;
- pricing bắt buộc;
- budget reservation `Serializable`;
- idempotency;
- settlement/reconciliation;
- payload encryption/retention;
- audit/usage an toàn.

Không chuẩn bị trước API key, provider client hoặc bypass trong desktop để “dùng tạm”.

## 20. Nhật ký triển khai để AI kế tiếp cập nhật

### Mẫu cập nhật sau mỗi phase

```text
Ngày:
Phase/task đã hoàn tất:
Quyết định engine/model/runtime:
File source đã thay đổi:
Migration manifest/SQLite đã thêm:
Kết quả npm build/test:
Kết quả dotnet restore/build/test:
Fixture local đã chạy:
Benchmark CPU/RAM/time/size:
Smoke test máy sạch:
Hạng mục còn mở:
Rủi ro hoặc quyết định cần người dùng chốt:
```

### Trạng thái ban đầu 2026-09-05

- Chưa có code dịch local trong repository đích.
- OCR local đã có source/test nhưng còn package/release hardening.
- `TRANSLATE_LOCAL` mới chỉ là job type placeholder.
- Manual `TranslatedText`/lock/filter/export SRT đã có và phải giữ tương thích.
- Chưa chọn/chốt engine contextual local, package strategy hoặc hardware floor.
- Chưa có schema 4, translation memory/cache/job items, executor, bridge hoặc UI dịch tự động.
- File này chỉ là kế hoạch; không có build/test mới được chạy riêng cho việc lập kế hoạch.
