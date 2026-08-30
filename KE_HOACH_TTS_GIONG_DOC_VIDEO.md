# Kế hoạch triển khai TTS để video có giọng đọc

> Ngày lập: 2026-08-28  
> Phạm vi: `TOOL-SHARED.Contracts`, `TOOL-SERVER`, `TOOL-LOCAL`, `TOOL-TESTS`, `database` và tài liệu vận hành.  
> Trạng thái: **Deferred/Future từ 2026-08-29**. Nền móng contract/schema/client đã có trong source và được giữ để tương thích dữ liệu, nhưng TTS đã bị tách khỏi workflow tạo Kling mặc định. Không chạy migration/rate/smoke test TTS cho luồng hiện tại. Khi muốn mở lại chức năng ghép giọng, phải có quyết định sản phẩm, feature gate/audio strategy riêng và QA lại toàn bộ kế hoạch này.

> Luồng đang Active được mô tả tại `KE_HOACH_KLING_NATIVE_AUDIO.md`: Kling tự sinh lời nói, ambience và SFX cùng clip; hệ thống không fallback ngầm sang TTS.

## 1. Mục tiêu

Triển khai đầy đủ luồng biến `Scene.Narration` thành giọng đọc AI, tải audio về workspace và ghép vào clip Kling để:

1. Video xem trước ngay tại từng card cảnh có thể nghe được lời đọc.
2. Video cuối giữ đúng lời đọc đã duyệt của từng cảnh.
3. Âm thanh native của Kling vẫn được giữ làm âm nền và tự giảm khi giọng đọc phát.
4. Clip Kling đã hoàn tất không bị xóa hoặc tạo lại khi TTS/FFmpeg lỗi.
5. Retry TTS hoặc retry ghép audio không tạo lại request Kling và không tính phí trùng.
6. Credential, rate, budget, usage và idempotency tiếp tục được quản lý tập trung theo tổ chức tại `TOOL-SERVER`.

## 2. Hiện trạng đã xác minh

- Kling đang được gửi với `NativeAudio = true`, nhưng native audio không đảm bảo có lời đọc narration.
- Clip mới nhất đã kiểm tra có audio stream AAC nhưng toàn bộ khoảng 15 giây gần như im lặng.
- UI đã truyền và lưu `female-sweet`/`male-warm` cùng tốc độ đọc vào project mới; project legacy chưa có lựa chọn vẫn giữ giá trị null và hiển thị “Chưa chọn giọng đọc”.
- `Scene.Narration` đã được lưu theo từng cảnh và gắn với script/scene plan version.
- Migration 4.0.3 đã được thêm trong source để mở rộng `vf.VoiceGenerations` theo scene và lưu output WAV tạm; migration chưa được chạy trên database thật.
- `FfmpegRenderService` đã có một `VoicePath` tùy chọn nhưng chưa có dịch vụ tạo voice asset, chưa được nối vào workflow và `NormalizeSceneAsync` hiện dùng `-an`, làm mất native audio khi dựng.
- `FfprobeService` mới kiểm tra có audio stream; chưa phát hiện stream im lặng hoặc mức âm lượng không đạt.
- Source chưa có API TTS, client TTS, output voice có xác thực, bước tải voice về workspace hoặc bước mix/ducking theo scene.

## 3. Quyết định kỹ thuật cho MVP

### 3.1. Provider và model

- Dùng credential OpenAI Active hiện có của tổ chức; không tạo BYOK hoặc key riêng trên desktop.
- Bootstrap model Voice `openai/gpt-4o-mini-tts` và gọi `POST /v1/audio/speech` qua `TOOL-SERVER`.
- Output mặc định là WAV để dễ kiểm tra, ghép và hạn chế suy hao qua nhiều lần encode.
- Tiếng Việt được hỗ trợ bằng cách gửi nguyên văn narration tiếng Việt; giọng có thể vẫn cần QA thủ công vì built-in voice được tối ưu chủ yếu cho tiếng Anh.
- Tên giọng trong sản phẩm (`female-sweet`, `male-warm`) là alias nghiệp vụ, không phải provider voice ID. Server resolve alias qua allowlist/configuration và lưu provider voice thật vào snapshot của `VoiceGeneration`.
- Chưa triển khai custom voice/voice cloning trong MVP.

Tài liệu OpenAI tham chiếu khi lập kế hoạch:

- [Text to speech](https://developers.openai.com/api/docs/guides/text-to-speech)
- [Create speech API](https://developers.openai.com/api/reference/resources/audio/subresources/speech/methods/create)
- [GPT-4o Mini TTS](https://developers.openai.com/api/docs/models/gpt-4o-mini-tts)

### 3.2. Phạm vi một voice generation

- Mỗi scene có narration không rỗng tạo một `VoiceGeneration` độc lập.
- Nội dung TTS luôn được server đọc từ `Scene.Narration`; desktop không gửi một đoạn narration tùy ý để thay thế dữ liệu đã duyệt.
- Snapshot phải chứa tối thiểu: organization, user, project, script/version, scene/scene-plan version, narration hash, language, voice alias, provider voice, speaking rate, model, provider request, credential version và rate snapshot.
- Scene không có narration không gọi TTS và giữ native audio Kling.

### 3.3. Thời điểm tạo giọng

Khi người dùng bấm tạo clip cho một hoặc nhiều cảnh:

1. Desktop chạy media preflight và kiểm tra cả Kling lẫn TTS readiness.
2. Với scene có narration, server tạo hoặc trả lại voice generation theo idempotency key.
3. Kling và TTS là hai nhánh độc lập; có thể chạy song song sau khi cả hai preflight đã đạt.
4. TTS lỗi không hủy task Kling đang chạy. Kling lỗi không xóa voice đã tạo.
5. Khi raw clip và voice đều sẵn sàng, desktop tự ghép thành clip có lời đọc.
6. Scene chỉ được coi là sẵn sàng để render cuối khi raw video hợp lệ và, nếu có narration, clip đã ghép voice hợp lệ.

### 3.4. Các loại asset

- `SceneVideo`: raw clip Kling hiện hữu, luôn được giữ để retry cục bộ.
- `SceneVoice`: WAV TTS của một scene.
- `SceneVideoNarrated`: clip dẫn xuất đã mix native audio và voice-over; UI ưu tiên asset này để preview.
- `FinalVideo`: video cuối ghép từ `SceneVideoNarrated`; scene không có narration dùng `SceneVideo`.

Không ghi đè raw clip Kling bằng clip đã mix.

## 4. Luồng mục tiêu

```text
Chọn giọng khi tạo project
  -> lưu voice setting của project
  -> người dùng duyệt/sửa Scene.Narration
  -> chọn scene và bấm tạo clip
       -> Kling tạo raw video
       -> OpenAI Speech tạo WAV theo scene
  -> desktop tải hai asset qua server
  -> FFmpeg kiểm tra, cân thời lượng, loudness và ducking
  -> tạo SceneVideoNarrated
  -> card scene phát clip có lời đọc
  -> final render nối các scene đã có audio
```

## 5. Danh sách task triển khai

### Giai đoạn A — Hợp đồng và migration 4.0.3

- [x] `TTS-01` Sửa DTO công khai trong `TOOL-SHARED.Contracts` trước khi sửa server/desktop:
  - `GenerateSceneVoiceRequest`;
  - `SceneVoiceGenerationResponse`;
  - metadata tải voice gồm relative content URL, MIME, size, SHA-256, duration và sample rate;
  - trạng thái voice trong provider readiness và storyboard.
- [x] `TTS-02` Request TTS chỉ nhận `projectId`, `sceneId`, version/snapshot mong đợi, idempotency key và `organizationId`; không nhận API key, provider URL hoặc narration tùy ý.
- [x] `TTS-03` Tạo migration idempotent mới `database/VideoFactory.4.0.3.SceneVoiceTts.sql`; không sửa âm thầm migration lịch sử đã triển khai.
- [x] `TTS-04` Thêm voice setting vào project:
  - `VoiceCode` là alias nghiệp vụ;
  - `VoiceSpeakingRate` có giới hạn hợp lệ;
  - tùy chọn style/instruction chỉ lưu dữ liệu không nhạy cảm;
  - project cũ chưa có voice phải được yêu cầu chọn trước khi tạo TTS, không tự đoán giọng.
- [x] `TTS-05` Mở rộng `vf.VoiceGenerations` theo scene:
  - thêm `SceneId` và foreign key;
  - thêm `ScenePlanVersion`/narration version;
  - thêm `NarrationHash`;
  - lưu voice alias và provider voice snapshot;
  - thay unique constraint project/version bằng filtered/scene-aware index, đồng thời giữ khả năng đọc dữ liệu legacy.
- [x] `TTS-06` Tạo vùng output tạm do server quản lý, ví dụ `vf.GeneratedVoiceOutputs`, gồm provider request ID, binary, MIME, size, SHA-256, duration, sample rate, created/expiry time và row version.
- [ ] `TTS-07` Thêm retention khoảng 24 giờ cho binary TTS; metadata audit/usage không bị xóa cùng payload tạm.
- [x] `TTS-08` Cập nhật model/EF mapping ở cả server và desktop; giữ navigation legacy cần thiết.
- [x] `TTS-09` Cập nhật `VideoFactory.DesktopLeastPrivilege.sql` để desktop không được đọc trực tiếp binary TTS, credential, provider request hoặc usage truth.
- [x] `TTS-10` Thêm migration tests: chạy lặp không lỗi, index đúng, FK đúng, không làm mất dữ liệu `VoiceGenerations` cũ và desktop role bị deny payload.

**Điểm dừng:** chưa chạy migration trên database thật nếu chưa xác minh instance, database, backup và khả năng restore.

### Giai đoạn B — Catalog, voice mapping và pricing

- [ ] `TTS-11` Thêm `gpt-4o-mini-tts` vào `ProviderCatalogBootstrapper` với modality `Voice`, endpoint `audio/speech`, output WAV, giới hạn input và allowlist built-in voice.
- [ ] `TTS-12` Thêm `OpenAiSpeechOptions` để cấu hình model, response format, max input/response bytes, timeout, speaking-rate range, voice alias mapping và style instruction theo ngôn ngữ.
- [ ] `TTS-13` Không gắn cố định nhận định giới tính cho provider voice nếu chưa được QA. Alias UI được map qua cấu hình server và có sample/QA tiếng Việt trước khi phát hành.
- [ ] `TTS-14` Bổ sung readiness theo từng modality/model; OpenAI Text hoặc Image sẵn sàng không đồng nghĩa TTS đã sẵn sàng.
- [ ] `TTS-15` Yêu cầu rate Active riêng cho model Voice. Với `gpt-4o-mini-tts`, dùng `InputToken` cho text input và `OutputToken` cho audio output; metadata phải ghi rõ modality, nguồn giá/hợp đồng, response format và estimation policy.
- [ ] `TTS-16` Không hard-code giá hoặc tự suy ra tỷ lệ audio token. Nếu Speech API không trả usage theo request, reservation/settlement dùng chính sách estimate đã được phê duyệt và version hóa trong model/rate metadata; thiếu metadata phải dừng bằng `pricing_not_configured` trước outbound call.
- [ ] `TTS-17` Mở rộng Admin Console để model Voice hiển thị đúng rate bắt buộc, hướng dẫn cấu hình và link đi thẳng đến model/rate còn thiếu.
- [ ] `TTS-18` Mở rộng trang “Cách tính chi phí” để phân biệt text input token, audio output token, estimated usage và actual/provider-reported usage.
- [ ] `TTS-19` Mở rộng usage projection để admin phân biệt chi phí Text, Image, Voice và Kling theo model; không gộp audio TTS với native audio Kling.

**Điểm dừng:** chỉ Global Admin nhập rate từ giá/hợp đồng hiện hành. Không sao chép giá cố định từ kế hoạch này.

### Giai đoạn C — OpenAI Speech client trên server

- [ ] `TTS-20` Tạo `IOpenAiSpeechClient`/`OpenAiSpeechClient` trong `TOOL-SERVER`; không thêm client OpenAI vào `TOOL-LOCAL`.
- [ ] `TTS-21` Gọi HTTPS tới allowlist `api.openai.com:443`, dùng credential Active đã giải mã trong server và không log Authorization, request body đầy đủ hoặc audio binary.
- [ ] `TTS-22` Request gửi model, nguyên văn narration, provider voice đã resolve, style instructions, speed hợp lệ và `response_format = wav`.
- [ ] `TTS-23` Giới hạn narration theo API/model; cảnh vượt giới hạn phải trả validation error rõ ràng trước outbound. Không tự cắt mất nội dung.
- [ ] `TTS-24` Đọc response dạng stream với size limit; kiểm tra Content-Type, chữ ký RIFF/WAVE, sample rate, channel, duration và SHA-256 trước khi ghi output tạm.
- [ ] `TTS-25` Chuẩn hóa lỗi 400/401/403/429/5xx, timeout và moderation/provider rejection; response/log không chứa secret hoặc narration đầy đủ.
- [ ] `TTS-26` Thêm retry có giới hạn chỉ cho lỗi tạm thời trước khi nhận response; không retry mù khi chưa biết provider đã tính phí hay chưa.

### Giai đoạn D — Nghiệp vụ TTS trong AI Gateway

- [ ] `TTS-27` Thêm API `POST /api/generation/scenes/{sceneId}/voice`.
- [ ] `TTS-28` Xác minh JWT, session, device claim, license lease, organization membership, role dùng AI, project ownership, scene ownership và current script/scene-plan version.
- [ ] `TTS-29` Server đọc `Scene.Narration`, language và voice setting đã lưu; từ chối stale version/hash để không đọc nhầm nội dung vừa bị chỉnh sửa.
- [ ] `TTS-30` Idempotency key tối thiểu khóa theo organization, project, script/version, scene, narration hash, language, voice alias/provider voice, speaking rate và model.
- [ ] `TTS-31` Cùng key/cùng hash trả lại voice generation/output hiện hữu; cùng key/khác payload trả `idempotency_key_conflict`.
- [ ] `TTS-32` Resolve đúng model Voice, OpenAI credential Active, rate Voice và quote; reserve budget bằng transaction `Serializable` trước outbound.
- [ ] `TTS-33` Tạo `ProviderRequest` loại `Voice` và `VoiceGeneration` trước outbound; request log chỉ giữ hash/snapshot/options đã lọc, không giữ toàn bộ narration nếu không cần cho audit.
- [ ] `TTS-34` Khi thành công, lưu output tạm, metadata/hash, cập nhật `VoiceGeneration`, settlement và usage ledger theo cùng rate snapshot.
- [ ] `TTS-35` Nếu provider không trả usage, ghi rõ `usageSource = estimated`, estimator version và audio duration; không ghi actual cost bằng `0` tùy tiện.
- [ ] `TTS-36` Khi thất bại, cập nhật trạng thái/error đã lọc và release reservation; không thay đổi hoặc xóa Kling request/clip đã có.
- [ ] `TTS-37` Thêm API lấy trạng thái voice và `GET /api/generation/scene-voices/{providerRequestId}/content` để tải binary qua server.
- [ ] `TTS-38` API tải voice xác minh lại user, license, organization, project, scene, provider request, expiry, MIME, size và hash; chỉ trả binary, không trả URL provider.
- [ ] `TTS-39` Thêm retention/reconciliation worker cho voice output và reservation dở dang.
- [ ] `TTS-40` Bổ sung mã lỗi ổn định: `openai_voice_not_configured`, `voice_pricing_not_configured`, `voice_generation_failed`, `voice_output_expired`, `voice_version_conflict`, `voice_duration_mismatch` và `voice_audio_invalid`.

### Giai đoạn E — Lưu lựa chọn giọng trên desktop

- [x] `TTS-41` Thêm `VoiceCode` và `VoiceSpeakingRate` vào `CreateProjectCommand`, entity/model, validate và mapping tạo project.
- [x] `TTS-42` Sửa `DashboardBridge` để giá trị `voiceCode` từ React thực sự được lưu, không chỉ validate rồi bỏ qua.
- [ ] `TTS-43` Khi mở project cũ chưa có voice, hiển thị trạng thái “Chưa chọn giọng đọc” và yêu cầu người dùng chọn trước khi tạo TTS. *(Đã có trạng thái hiển thị; phần chọn/bắt buộc trước TTS sẽ hoàn tất cùng API/workflow TTS.)*
- [ ] `TTS-44` Cho phép đổi voice khi scene chưa có voice generation hoạt động. Nếu đổi sau khi đã có voice, yêu cầu xác nhận và tạo version mới; không ghi đè asset/usage cũ.
- [x] `TTS-45` Cập nhật WebView message/TypeScript/C# đồng thời để không lệch contract.

### Giai đoạn F — Tải và quản lý voice asset trong workspace

- [ ] `TTS-46` Thêm phương thức generate/status/download voice vào `ServerGenerationClient`; mọi request mang JWT, device/license và organization hiện hành.
- [ ] `TTS-47` Tải vào file `.part`, giới hạn dung lượng, đối chiếu Content-Type/Content-Length, SHA-256 và metadata server trước khi đổi tên nguyên tử.
- [ ] `TTS-48` Chạy FFprobe sau khi tải để xác nhận audio stream, duration, codec/sample rate; file lỗi bị xóa `.part` và không được đánh dấu Ready.
- [ ] `TTS-49` Lưu WAV ở đường dẫn xác định theo project/scene/provider request, ví dụ `audio/voice/scene-001-{providerRequestId}.wav`.
- [ ] `TTS-50` Tạo/cập nhật `MediaAsset` loại `SceneVoice`, liên kết scene, source provider request, size, SHA-256, duration, sample rate và trạng thái xác minh.
- [ ] `TTS-51` Retry tải/mix dùng voice output/asset hiện hữu; không gọi lại OpenAI nếu idempotent output vẫn còn hợp lệ.

### Giai đoạn G — Orchestration Kling + TTS

- [ ] `TTS-52` Mở rộng `ProjectGenerationService` để mỗi scene có hai nhánh trạng thái độc lập: raw video Kling và voice TTS.
- [ ] `TTS-53` Chạy preflight model/rate/budget cho TTS trước khi bắt đầu batch có narration; link cảnh báo tới đúng trang cấu hình Voice.
- [ ] `TTS-54` Sau khi submit Kling, tạo/reuse TTS cho scene có narration trong cùng workflow nhưng không ràng buộc vòng đời hai provider request.
- [ ] `TTS-55` Kling hoàn tất/TTS lỗi: vẫn tải và giữ raw clip, hiển thị nút **Thử lại giọng đọc**.
- [ ] `TTS-56` TTS hoàn tất/Kling lỗi: giữ voice asset để lần tạo lại Kling có thể dùng lại nếu narration/voice/model snapshot không đổi.
- [ ] `TTS-57` Scene narration rỗng: bỏ qua TTS, dùng raw native audio và không tạo request/ledger rỗng.
- [ ] `TTS-58` Không dùng `Scene.Status = Approved` một mình để kết luận scene đã có lời. Storyboard phải tính riêng `videoReady`, `voiceRequired`, `voiceReady`, `mixReady` và lỗi tương ứng.
- [ ] `TTS-59` Project chỉ chuyển `ReadyToRender` khi mọi scene có video, và mọi scene có narration đã có narrated clip hợp lệ.

### Giai đoạn H — FFmpeg mix, ducking và kiểm tra im lặng

- [ ] `TTS-60` Không dùng trực tiếp `FinalRenderManifest.VoicePath` đơn lẻ cho toàn project. Thêm manifest theo scene gồm raw video path, voice path, native-audio state, expected duration và output path.
- [ ] `TTS-61` Tạo bước `MixSceneAudioAsync` sinh `SceneVideoNarrated` mà không ghi đè raw clip.
- [ ] `TTS-62` Khi raw clip có native audio hợp lệ:
  - chuẩn hóa loudness của voice;
  - dùng voice làm sidechain để giảm native audio trong thời gian lời đọc phát;
  - mix voice + native audio;
  - ngăn clipping ở output.
- [ ] `TTS-63` Khi raw clip không có audio hoặc audio gần như im lặng, ghép voice với nền im lặng; không coi đây là lý do phải gọi lại Kling.
- [ ] `TTS-64` Khi voice ngắn hơn scene, pad silence sau voice và giữ native audio đến hết scene.
- [ ] `TTS-65` Khi voice dài hơn scene, không cắt narration. Cho phép điều chỉnh bằng `atempo` trong ngưỡng chất lượng đã cấu hình; vượt ngưỡng trả `voice_duration_mismatch` để người dùng chỉnh narration/speed hoặc tạo lại.
- [ ] `TTS-66` Sửa đường render để không còn vô điều kiện loại native audio bằng `-an`; final render phải dùng narrated scene hoặc giữ native audio cho scene không narration.
- [ ] `TTS-67` Tạo `AudioQualityValidator` dùng FFprobe và FFmpeg `volumedetect`/`silencedetect` hoặc `astats` để kiểm tra:
  - có audio stream;
  - duration hợp lệ;
  - không im lặng toàn bộ;
  - peak/loudness trong ngưỡng cấu hình;
  - không lệch thời lượng quá mức.
- [ ] `TTS-68` Sau khi mix thành công, tính SHA-256, probe lại output, tạo `MediaAsset` loại `SceneVideoNarrated` và chỉ sau đó đổi preview sang file mới.
- [ ] `TTS-69` Nếu mix lỗi, giữ nguyên raw video và voice; retry cục bộ không gọi OpenAI/Kling và không tạo usage mới.
- [ ] `TTS-70` Final render ưu tiên `SceneVideoNarrated`; với scene không narration dùng raw `SceneVideo`. Không concat một scene narration có voice chưa mix thành công.

### Giai đoạn I — Storyboard và trải nghiệm người dùng

- [ ] `TTS-71` Card scene ưu tiên phát `SceneVideoNarrated`; chỉ fallback raw clip khi chưa có voice/mix và phải hiển thị cảnh báo rõ ràng.
- [ ] `TTS-72` Thêm trạng thái: **Chưa tạo giọng**, **Đang tạo giọng**, **Đã có lời đọc**, **Đang ghép âm thanh**, **Giọng đọc lỗi**, **Audio không đạt**.
- [ ] `TTS-73` Thêm thao tác **Nghe thử giọng**, **Tạo lại giọng đọc** và **Ghép lại âm thanh**; các thao tác có busy state và chống bấm lặp.
- [ ] `TTS-74` Khi narration thay đổi, đánh dấu voice/mix cũ là stale; không âm thầm phát lời cũ cho nội dung mới.
- [ ] `TTS-75` Hiển thị voice alias, model, duration và thông báo “Giọng đọc được tạo bởi AI” theo yêu cầu công bố của OpenAI.
- [ ] `TTS-76` Badge **Hoàn thành** của scene chỉ hiện khi video và lời đọc bắt buộc đều đạt; raw clip hoàn tất nhưng voice lỗi phải hiển thị trạng thái một phần.
- [ ] `TTS-77` Hiển thị link trực tiếp tới cấu hình budget/rate/model Voice khi bị `pricing_not_configured`, budget bằng 0 hoặc Voice model chưa sẵn sàng.

### Giai đoạn J — Kiểm thử tự động

- [ ] `TTS-78` Unit test voice alias mapping, speaking rate, narration normalization/hash và stale version.
- [ ] `TTS-79` Test `OpenAiSpeechClient` bằng fake HTTP response: request đúng, WAV hợp lệ, MIME sai, oversized, malformed, timeout, 401/403/429/5xx và không log secret/body.
- [ ] `TTS-80` Test authorization: user ngoài tổ chức, Viewer, license/device/session hết hạn và cross-project/cross-scene đều bị chặn trước outbound.
- [ ] `TTS-81` Test pricing/budget: thiếu rate Voice, budget 0, member limit, reservation/settlement/release, rate snapshot và estimate usage source.
- [ ] `TTS-82` Test idempotency: cùng key/cùng hash chỉ một provider request/voice/ledger; cùng key/khác narration hoặc voice trả conflict.
- [ ] `TTS-83` Test download voice: URL tương đối, ownership, expiry, MIME, size, SHA-256, `.part`, atomic move và desktop không đọc binary từ SQL.
- [ ] `TTS-84` Test FFmpeg bằng fixture cục bộ, không gọi provider:
  - raw clip có native audio + voice;
  - raw clip im lặng + voice;
  - raw clip không có audio + voice;
  - voice ngắn/dài;
  - scene không narration;
  - retry mix không phát sinh provider call.
- [ ] `TTS-85` Test output narrated clip có video H.264, audio AAC, không im lặng và duration trong tolerance.
- [ ] `TTS-86` Test workflow lỗi cô lập: TTS lỗi không mất Kling; Kling lỗi không mất voice; FFmpeg lỗi không gọi lại provider.
- [ ] `TTS-87` UI/contract test xác nhận `voiceCode` được lưu, scene preview ưu tiên narrated asset, stale narration bị cảnh báo và AI disclosure xuất hiện.
- [ ] `TTS-88` Regression test Kling native audio, character reference, content generation, budget, updater và FFmpeg bundle hiện có.

### Giai đoạn K — Tài liệu và vận hành

- [ ] `TTS-89` Cập nhật `README.md`, `NGHIEP_VU_HE_THONG_VIDEOMAKER.md`, `NGHIEP_VU_SINH_VIDEO_VA_DONG_BO_NHAN_VAT.md`, `KE_HOACH_SERVER_AI_GATEWAY.md` và `TRIEN_KHAI_AI_GATEWAY_TO_CHUC.md` sau khi source thực sự hoàn tất.
- [ ] `TTS-90` Bổ sung runbook migration 4.0.3, cấu hình model/rate Voice, voice alias, retention, lỗi thường gặp và rollback.
- [ ] `TTS-91` Bổ sung health/metrics cho TTS latency, success/failure, output expiry, estimated settlement, audio validation và local mix failure.
- [ ] `TTS-92` Chuẩn bị staging smoke test có phê duyệt chi phí: một scene tiếng Việt, một request TTS, tải qua server, mix cục bộ và nghe đối chiếu narration.
- [ ] `TTS-93` Đối chiếu usage ledger với OpenAI provider dashboard; nếu API không trả usage theo request, xác minh estimation policy và sai số trước production.

## 6. Thứ tự triển khai khuyến nghị

1. `TTS-01` đến `TTS-10`: contract và migration.
2. `TTS-11` đến `TTS-19`: catalog, mapping, pricing và admin readiness.
3. `TTS-20` đến `TTS-40`: Speech client và server gateway.
4. `TTS-41` đến `TTS-51`: lưu voice setting, desktop client và workspace asset.
5. `TTS-52` đến `TTS-70`: orchestration và FFmpeg mix/validation.
6. `TTS-71` đến `TTS-77`: UI storyboard.
7. `TTS-78` đến `TTS-93`: test, tài liệu, staging và vận hành.

Không triển khai UI “Hoàn thành” trước khi trạng thái server/desktop và audio validator đã có nguồn dữ liệu thật.

## 7. Kiểm tra bắt buộc sau khi thay đổi source

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Khi chỉ sửa web:

```powershell
Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
```

Smoke test provider thật chỉ thực hiện trên staging sau khi người dùng xác nhận môi trường và cho phép chi phí.

## 8. Tiêu chí nghiệm thu

1. Voice alias được lưu theo project và snapshot đúng vào từng voice generation.
2. Scene có narration tạo đúng một TTS output cho cùng idempotency key/snapshot.
3. Desktop không nhận OpenAI key, provider URL hoặc binary qua SQL; audio tải qua API có xác thực và kiểm tra hash.
4. Card scene phát `SceneVideoNarrated` và người dùng nghe được đúng nội dung `Scene.Narration` đã duyệt.
5. Clip narrated có audio stream hợp lệ và không bị validator đánh dấu im lặng toàn bộ.
6. Native audio Kling giảm khi lời đọc phát; scene không narration vẫn giữ native audio.
7. Voice dài không bị cắt mất lời; mismatch vượt tolerance được báo rõ.
8. TTS lỗi không xóa/tạo lại Kling; mix lỗi không gọi lại OpenAI hoặc Kling.
9. Final video dùng narrated asset cho mọi scene có narration và có audio nghe được sau concat/render.
10. Viewer, cross-organization, license/session/device hết hạn, thiếu rate hoặc budget không đủ đều bị chặn trước outbound.
11. Rate snapshot, reservation, settlement/release và usage ledger truy được theo organization, user, project, scene, model, credential version và provider request.
12. UI công bố rõ giọng đọc do AI tạo.
13. Release build không warning/error và toàn bộ test đạt.

## 9. Rollback

- Dùng feature flag server để ngừng tạo TTS mới nhưng vẫn cho tải output đã hoàn tất trong thời gian retention.
- Desktop fallback về raw `SceneVideo` và hiển thị cảnh báo “Chưa có giọng đọc”; không đánh dấu scene narration là hoàn thành.
- Không xóa migration/schema, `ProviderRequest`, `VoiceGeneration`, usage ledger, raw Kling clip hoặc voice asset khi rollback binary.
- Retry render/mix sau rollback chỉ dùng asset đã có; không gọi provider mới.
- Nếu model Voice bị vô hiệu hóa, giữ rate/usage lịch sử để audit và reconciliation.

## 10. Ngoài phạm vi MVP

- Voice cloning/custom voice và quản lý consent recording.
- Đồng bộ khẩu hình/lip-sync nhân vật với narration.
- Realtime speech/WebSocket hoặc phát audio trước khi output hoàn tất.
- Tự động dịch narration sang ngôn ngữ khác.
- Dùng speech-to-text để chấm điểm từng từ của output; MVP bảo đảm input là narration đã duyệt và QA nghe đối chiếu.
- Gọi TTS trực tiếp từ desktop hoặc phát credential theo máy/người dùng.
- Tự chạy migration production, tự nhập giá, tự rotate credential hoặc tự chạy smoke test có chi phí.
