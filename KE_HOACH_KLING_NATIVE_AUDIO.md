# Kế hoạch triển khai Kling Native Audio cho video

> Ngày lập: 2026-08-29  
> Trạng thái: **Hoàn tất phạm vi source code ngày 2026-08-29**; restore đạt, Release build 0 warning/error và 266/266 test đạt. Cấu hình/smoke test Kling thật, migration database thật, pricing production và phát hành production chưa được thực hiện.  
> Phạm vi: `TOOL-SHARED.Contracts`, `TOOL-SERVER`, `TOOL-LOCAL`, `TOOL-TESTS`, database nếu cần lưu chiến lược audio, UI WebView và tài liệu nghiệp vụ.  
> Quyết định sản phẩm: Giai đoạn hiện tại chỉ dùng âm thanh được Kling sinh trực tiếp cùng clip. TTS và ghép voice-over là tính năng tương lai, không nằm trong workflow mặc định.

## 1. Mục tiêu

VideoMaker phải tạo được clip có hình ảnh và âm thanh đồng bộ ngay trong một request Kling:

1. `Kling 3.0` tạo clip theo từng scene với `NativeAudio = true`.
2. Lời mà người dùng nhìn thấy trên storyboard phải được đưa vào prompt Kling dưới dạng lời thoại hoặc native voice-over.
3. Prompt phải xác định rõ người nói, câu nói, ngôn ngữ, sắc thái, hành động, khẩu hình, âm thanh môi trường và các điều cấm.
4. Desktop phải kiểm tra clip thực sự có audio nghe được, không chỉ kiểm tra sự tồn tại của audio stream.
5. Người dùng phải được nghe thử, duyệt hoặc sinh lại; clip có yêu cầu lời nói nhưng không nghe được không được tự động duyệt.
6. Video cuối phải giữ audio native của từng clip trong quá trình chuẩn hóa, nối cảnh và xuất file.
7. Workflow này không gọi OpenAI Speech, không tải WAV, không tạo voice asset và không ghép voice-over bằng FFmpeg.
8. TTS đã có trong source được giữ nguyên để dùng lại trong tương lai, nhưng phải tách khỏi đường gọi mặc định để không phát sinh chi phí hoặc tạo hai giọng cùng lúc.

## 2. Phạm vi MVP

### 2.1. Trong phạm vi

- Model video: `kling-3.0`.
- Độ phân giải: `720p`.
- Thời lượng mỗi clip: từ 3 đến 15 giây.
- Tỷ lệ: `16:9`, `9:16` hoặc `1:1` theo project.
- `NativeAudio = true` là bắt buộc.
- `multi_shot = false`; mỗi scene là một cảnh quay liên tục.
- Tối đa một nhân vật tham chiếu và một người nói trong một scene.
- Hỗ trợ hai kiểu lời native:
  - `OnCameraDialogue`: nhân vật xuất hiện và trực tiếp nói.
  - `NativeVoiceOver`: Kling sinh giọng ngoài hình trong cùng clip.
- Scene không cần lời nói dùng `None`, nhưng vẫn có thể có ambience/SFX native.
- Prompt được server dựng từ dữ liệu scene và character đã lưu.
- Kiểm tra audio tự động bằng FFprobe/FFmpeg và duyệt nội dung bằng người dùng nghe thử.
- Rate/budget sử dụng đúng biến thể `VideoSecond`, `resolution=720p`, `nativeAudio=true`.

### 2.2. Ngoài phạm vi

- Không gọi `gpt-4o-mini-tts` hoặc provider TTS khác.
- Không tạo `VoiceGeneration` mới trong workflow native.
- Không tạo `GeneratedVoiceOutput` hoặc tải WAV về workspace.
- Không tạo `SceneVoice` hoặc `SceneVideoNarrated` mới.
- Không dùng `SceneAudioMixer` trong workflow native.
- Không voice cloning hoặc voice tone control trong MVP.
- Không nhiều người đối thoại trong cùng scene.
- Không tự động kiểm tra đúng từng chữ bằng speech-to-text trong MVP.
- Không tự động sinh lại clip khi audio lỗi vì mỗi lần sinh lại có thể phát sinh chi phí.
- Không chạy migration thật, gọi Kling thật hoặc smoke test có chi phí nếu chưa xác nhận môi trường, backup, credential, rate và quyền tác động.

## 3. Thuật ngữ và nguồn sự thật

### 3.1. Thuật ngữ

- **Kling Native Audio:** toàn bộ hội thoại, native voice-over, ambience và SFX được Kling sinh cùng video trong một provider request.
- **On-camera dialogue:** nhân vật nhìn thấy trong khung hình trực tiếp nói và cần đồng bộ khẩu hình.
- **Native voice-over:** giọng ngoài hình do Kling sinh trong cùng clip; không phải file được ghép sau.
- **Mixed voice-over:** WAV TTS được tạo riêng rồi ghép vào clip bằng FFmpeg. Đây là tính năng tương lai.
- **Expected spoken text:** câu Kling được yêu cầu nói. Trong UI có thể hiển thị là “Lời Kling sẽ nói”.
- **Audio review:** bước người dùng nghe clip để quyết định duyệt hoặc sinh lại.

### 3.2. Nguồn sự thật dữ liệu

- Character profile và character reference đã khóa là nguồn sự thật nhận diện.
- Scene hiện hành và scene prompt version hiện hành là nguồn sự thật nội dung.
- Expected spoken text phải được lưu có version cùng scene; desktop không được gửi một câu tùy ý để thay thế dữ liệu server đã duyệt.
- Server dựng prompt cuối cùng và hash; desktop không quyết định prompt provider hiệu lực.
- `ProviderRequest`, budget reservation, usage ledger và rate snapshot tiếp tục là nguồn sự thật chi phí ở server.

### 3.3. Quan hệ với kế hoạch TTS cũ

- `KE_HOACH_TTS_GIONG_DOC_VIDEO.md` được chuyển trạng thái thành `Deferred/Future` khi bắt đầu triển khai kế hoạch này.
- Không xóa endpoint, entity, migration hoặc test TTS chỉ vì workflow mặc định không gọi chúng.
- Không rollback migration 4.0.3 nếu môi trường đã chạy.
- Dữ liệu `SceneVoice` và `SceneVideoNarrated` lịch sử vẫn phải đọc được.
- Project/scene mới mặc định dùng `KlingNative`; `MixedVoiceOver` chỉ được bật trong một thay đổi sản phẩm riêng sau này.

## 4. Hiện trạng source đã xác minh

### 4.1. Phần đã có thể tái sử dụng

- Desktop đang submit Kling ở `720p` với `NativeAudio = true`.
- Server chỉ chấp nhận biến thể `720p + Native Audio` qua `KlingNativeAudioPolicy`.
- Kling client đang gửi `audio = "native"` và `multi_shot = false`.
- Server đã có kiểm tra quyền, project, scene, character/reference, credential, rate, budget, reservation và idempotency.
- Server đã có worker polling Kling khi desktop đóng.
- Video được tải qua proxy server có xác thực và chống SSRF.
- Desktop đã có `FfprobeService` và `AudioQualityValidator` để phát hiện audio stream im lặng/gần im lặng.
- Raw clip Kling được lưu dưới dạng `SceneVideo`.
- `FfmpegRenderService` đã có nền tảng xử lý audio khi dựng.

### 4.2. Khoảng trống cần sửa

1. OpenAI content plan đang trả `Narration` và `VisualPrompt` riêng nhưng `ScenePrompt.FinalPrompt` chỉ lấy `VisualPrompt`.
2. Prompt Kling hiệu lực hiện chỉ gồm identity lock, visual scene prompt và negative prompt; không ghép nguyên văn lời scene cần nói.
3. `Scene.Narration` hiện kích hoạt `EnsureSceneNarrationAsync`, tạo TTS và ghép `SceneVideoNarrated`.
4. Workflow đang kiểm tra Voice readiness/VoiceCode cho scene có narration, trái với phạm vi native-only.
5. Audio quality được ghi vào metadata nhưng clip có audio không đạt vẫn có thể được đánh dấu `Approved`.
6. UI dùng nhãn “Lời đọc”, chưa phân biệt lời Kling nói trực tiếp với voice-over ghép.
7. Prompt không có cấu trúc bắt buộc cho speaker, exact line, language, voice style, lip-sync, ambience và SFX.
8. Giới hạn 3.072 ký tự có thể cắt phần lời thoại nếu phần audio nằm cuối prompt.
9. Final render chưa có workflow UI/job hoàn chỉnh; cần xác minh đường chọn asset không ưu tiên `SceneVideoNarrated` trong chế độ native.
10. Nghiệp vụ hệ thống hiện mô tả Native Audio và TTS chạy đồng thời; tài liệu này không còn khớp quyết định sản phẩm mới.

## 5. Giới hạn provider và chính sách tiếng Việt

Theo hướng dẫn chính thức Kling VIDEO 3.0 hiện có, Native Audio công bố hỗ trợ thoại tiếng Trung, Anh, Nhật, Hàn và Tây Ban Nha; câu thoại ngoài danh sách có thể bị dịch sang tiếng Anh. Tài liệu cũng khuyến nghị ghép rõ từng character với câu thoại và giới hạn clip từ 3 đến 15 giây:

- [Kling VIDEO 3.0 Model User Guide](https://app.klingai.com/cn/quickstart/klingai-video-3-model-user-guide)

Chính sách MVP đề xuất cho project `vi-VN`:

1. Cho phép tạo theo chế độ `best-effort`, không quảng cáo là bảo đảm đúng tiếng Việt.
2. Hiển thị cảnh báo trước khi phát sinh chi phí: Kling có thể nói sai, bỏ lời hoặc chuyển sang tiếng Anh.
3. Bắt buộc nghe thử trước khi duyệt scene có expected spoken text.
4. Không tự động đánh dấu đạt chỉ vì có audio stream.
5. Không tự động sinh lại khi sai lời; người dùng xác nhận chi phí trước mỗi lần thử mới.
6. Trước phát hành rộng phải chạy smoke test thật theo ma trận tiếng Việt ở mục 17.
7. Nếu smoke test tiếng Việt không đạt ngưỡng nghiệm thu, tính năng phải được gắn nhãn thử nghiệm hoặc bị chặn cho `vi-VN`; không giải quyết bằng cách kéo dài prompt vô hạn.

## 6. Luồng nghiệp vụ mục tiêu

```text
Người dùng tạo/chọn project
  -> OpenAI tạo content plan có visual + native speech intent
  -> desktop lưu/version hóa scene
  -> người dùng xem/sửa “Lời Kling sẽ nói”
  -> nếu có character: chọn/tạo ảnh và khóa character
  -> người dùng chọn scene và bấm tạo Kling
  -> preflight media tools + Kling readiness
  -> server xác thực toàn bộ quyền và ownership
  -> server đọc scene/character/prompt version đã lưu
  -> server dựng Kling Native Audio prompt có cấu trúc
  -> server kiểm tra rate 720p-native-audio và budget
  -> server reserve budget trong transaction Serializable
  -> server submit đúng một Kling request idempotent
  -> worker server polling đến khi hoàn tất
  -> desktop tải clip qua proxy có xác thực
  -> FFprobe xác nhận video/audio stream
  -> AudioQualityValidator xác nhận audio nghe được
  -> desktop lưu raw SceneVideo và metadata kiểm tra
  -> người dùng nghe thử
       -> đạt: duyệt scene
       -> sai lời/không tiếng: sinh lại sau xác nhận chi phí
  -> final render nối SceneVideo đã duyệt và giữ native audio
```

## 7. Mô hình dữ liệu và hợp đồng đề xuất

### 7.1. Audio strategy của project

Nên có một giá trị nghiệp vụ rõ ràng:

- `KlingNative`: giá trị mặc định và duy nhất được bật trong MVP.
- `MixedVoiceOver`: giá trị dự phòng cho tính năng TTS tương lai, chưa cho chọn.

Trước khi thêm migration phải kiểm tra có thể lưu ổn định trong cấu trúc hiện hữu hay không:

- Phương án ưu tiên: thêm `AudioStrategy` vào project bằng migration idempotent mới nếu cần phân biệt dữ liệu lâu dài.
- Phương án không migration: feature/configuration cố định toàn hệ thống là `KlingNative`, đồng thời giữ các cột voice legacy. Chỉ dùng nếu chưa cần project-level strategy.

Không được sửa âm thầm migration 4.0.3 đã có.

### 7.2. Native speech intent của scene

Hợp đồng content plan nên có tối thiểu:

- `SpeechMode`: `None`, `OnCameraDialogue`, `NativeVoiceOver`.
- `SpokenText`: nguyên văn câu cần nói.
- `SpeakerCharacterKey`: bắt buộc với `OnCameraDialogue`, null với `None`.
- `VoiceStyle`: tuổi cảm nhận, sắc thái, năng lượng, tốc độ và cách phát âm.
- `AmbientAudio`: room tone hoặc âm thanh nền tự nhiên.
- `SoundEffects`: hiệu ứng gắn với hành động trong scene.
- `MusicIntent`: mặc định `None` trong MVP để không lấn lời.

Quy tắc:

- `None` bắt buộc `SpokenText` rỗng.
- `OnCameraDialogue` bắt buộc có đúng một character key thuộc scene.
- `NativeVoiceOver` không được đồng thời yêu cầu character trên màn hình nói.
- Không cho phép hai speaker trong MVP.
- `SpokenText` không được tự thay thế bằng `VisualPrompt`.

### 7.3. Tận dụng field hiện có

Để hạn chế migration:

- Có thể lưu on-camera text vào `Scene.Dialogue`.
- Có thể lưu native voice-over text vào `Scene.Narration`.
- `ScenePrompt.CanonicalInputJson` lưu speech mode, voice style, ambience, SFX và template version.
- Khi đọc dữ liệu legacy chỉ có `Narration`:
  - scene có một character: đề xuất map thành `OnCameraDialogue` nhưng bắt buộc người dùng kiểm tra trước khi sinh;
  - scene không có character: map thành `NativeVoiceOver`;
  - scene đã có provider request không được âm thầm đổi prompt hoặc resubmit.

Nếu cách tận dụng field hiện hữu không bảo đảm query/version/UI rõ ràng thì tạo migration mới thay vì nhồi dữ liệu tùy tiện vào JSON.

### 7.4. Shared contracts

Nếu public DTO thay đổi, phải sửa `TOOL-SHARED.Contracts` trước rồi cập nhật đồng thời server, desktop và test:

- Mở rộng `GeneratedContentScene` với native speech intent.
- Mở rộng storyboard/scene summary để UI đọc trạng thái audio và review.
- Mở rộng update-scene contract để lưu speech mode, spoken text và audio intent.
- Không thêm API key, provider URL hoặc prompt provider đầy đủ vào DTO.
- `SubmitKlingVideoRequest` tiếp tục gửi ID/version và options cần thiết; server vẫn đọc dữ liệu scene đã lưu để dựng prompt hiệu lực.
- Không bắt desktop gửi một đoạn narration tùy ý làm nguồn sự thật.

## 8. Chuẩn prompt Kling Native Audio

### 8.1. Thành phần bắt buộc và thứ tự ưu tiên

Prompt composer trên server phải dựng theo thứ tự:

1. `IDENTITY LOCK` và reference consistency.
2. `NATIVE SPEECH` gồm speaker, language và exact spoken text.
3. `VOICE AND PERFORMANCE`.
4. `LIP SYNC AND GESTURE` nếu on-camera.
5. `SCENE` gồm bối cảnh, ánh sáng và style.
6. `ACTION AND TIMING`.
7. `CAMERA`.
8. `ENVIRONMENT AUDIO` và SFX.
9. `NEGATIVE CONSTRAINTS`.

Lý do đặt speech sớm: server giới hạn prompt 3.072 ký tự; lời thoại và identity không được mất khi rút gọn.

### 8.2. Template on-camera

```text
Create a {duration}-second single continuous cinematic shot in {aspectRatio}
with synchronized native audio.

IDENTITY LOCK:
Use the approved reference image for {characterName}. Preserve the exact face,
age, hairstyle, body proportions, clothing and accessories throughout the clip.
{characterName} is the only speaking character.

NATIVE SPEECH:
{characterName} says exactly once, without translating, paraphrasing or repeating:
"{spokenText}"

VOICE AND PERFORMANCE:
Language: {language}. Voice: {voiceStyle}. Natural conversational pace,
clear pronunciation, natural breathing and pauses.

LIP SYNC AND GESTURE:
Synchronize lip movements, facial expressions and body gestures with every word.

SCENE AND ACTION:
{visualScene}. {timedAction}.

CAMERA:
{cameraInstruction}. One continuous shot, no cuts.

ENVIRONMENT AUDIO:
{ambientAudio}. {soundEffects}. Keep the spoken voice clear and foregrounded.

Do not generate additional speakers, off-screen narration, repeated dialogue,
gibberish speech, subtitles, captions, logos, watermarks, loud music,
camera cuts, identity changes or clothing changes.
```

### 8.3. Template native voice-over

```text
Create a {duration}-second single continuous cinematic shot in {aspectRatio}
with synchronized native audio.

NATIVE VOICE-OVER:
One off-screen narrator says exactly once, without translating,
paraphrasing or repeating:
"{spokenText}"

VOICE AND PERFORMANCE:
Language: {language}. Voice: {voiceStyle}. Natural pace and clear pronunciation.

SCENE, ACTION AND CAMERA:
{visualScene}. {timedAction}. {cameraInstruction}. No on-screen character speaks.

ENVIRONMENT AUDIO:
{ambientAudio}. {soundEffects}. Keep narration clear above ambience.

Do not generate on-screen dialogue, additional voices, repeated words,
gibberish, subtitles, captions, logos, watermarks or loud music.
```

### 8.4. Giới hạn lời theo thời lượng

Ngưỡng nghiệp vụ ban đầu để validation/cảnh báo:

| Thời lượng | Số từ đề xuất | Chính sách |
|---:|---:|---|
| 3–5 giây | 5–8 | Cảnh báo nếu vượt 8 |
| 6–10 giây | 10–18 | Cảnh báo nếu vượt 18 |
| 11–15 giây | 18–28 | Cảnh báo nếu vượt 28 |

- Đây là ngưỡng sản phẩm, không phải cam kết provider.
- Nếu vượt ngưỡng, OpenAI planner nên chia thành scene khác; không tự cắt mất nội dung.
- UI phải cho người dùng sửa trước khi submit.
- Server vẫn có validation cuối để tránh client cũ bỏ qua UI.

### 8.5. Quy tắc rút gọn prompt

Khi prompt vượt giới hạn:

1. Không cắt giữa chuỗi Unicode hoặc giữa exact spoken text.
2. Không cắt identity lock thiết yếu.
3. Rút gọn mô tả style lặp lại.
4. Rút gọn immutable traits ít quan trọng đã được ảnh reference thể hiện.
5. Rút gọn negative prompt trùng lặp.
6. Nếu vẫn vượt giới hạn, trả validation error trước outbound; không dùng substring mù.

Prompt composer cần có `PromptTemplateVersion` để idempotency và audit nhận biết thay đổi cấu trúc.

## 9. Kế hoạch triển khai theo task

### 9.1. Trạng thái thực hiện ngày 2026-08-29

| Task | Trạng thái | Xác minh/đầu ra chính |
|---|---|---|
| KNA-01 | [x] Hoàn tất | Workflow mặc định là `KlingNative`; TTS được giữ ở trạng thái Future. |
| KNA-02 | [x] Hoàn tất | Đã chạy lại restore/build/test; mốc mới là 266/266 test. |
| KNA-03 | [x] Hoàn tất | Dùng strategy cố định `KlingNative`, không thêm migration. |
| KNA-04 | [x] Hoàn tất | Contract content có speech mode/text/speaker/style/ambience/SFX. |
| KNA-05 | [x] Hoàn tất | TypeScript, C# bridge và service cập nhật scene đồng bộ. |
| KNA-06 | [x] Không cần migration | Field hiện hữu đủ cho MVP; không tác động database thật. |
| KNA-07 | [x] Hoàn tất | Structured output OpenAI có native speech intent. |
| KNA-08 | [x] Hoàn tất | Word budget theo duration và giới hạn scene 3–15 giây. |
| KNA-09 | [x] Hoàn tất | Validation output chặn speech/speaker/prompt mâu thuẫn. |
| KNA-10 | [x] Hoàn tất | Có `KlingNativeAudioPromptComposer` server-side. |
| KNA-11 | [x] Hoàn tất | Prompt được rút gọn theo section, không cắt spoken text. |
| KNA-12 | [x] Hoàn tất | Log chỉ lưu hash/snapshot; response Kling được sanitize, không lưu raw payload. |
| KNA-13 | [x] Hoàn tất | Server bắt buộc current scene-plan và exact prompt version trước outbound. |
| KNA-14 | [x] Hoàn tất | Snapshot có organization/user/model/scene/prompt/speech/character/options; replay/conflict có test trực tiếp. |
| KNA-15 | [x] Hoàn tất | Rate snapshot và reserve/settle/release giữ nguyên cơ chế governance hiện hành. |
| KNA-16 | [x] Hoàn tất | Request Kling dùng native audio, 720p, single-shot và đúng endpoint/reference mode. |
| KNA-17 | [x] Hoàn tất | Lỗi rate-limit/moderation/request/native-audio/output-missing được chuẩn hóa, không lộ provider payload. |
| KNA-18 | [x] Hoàn tất | Đường tạo Kling mặc định không gọi TTS/Voice readiness. |
| KNA-19 | [x] Hoàn tất | Tải `.part`, probe/hash/audio quality và chỉ lưu `SceneVideo`. |
| KNA-20 | [x] Hoàn tất | Có `AudioReviewRequired`, `NativeAudioInvalid`, `Approved`. |
| KNA-21 | [x] Hoàn tất | Audio không nghe được bị chặn; retry cần attempt/chi phí mới, không fallback TTS. |
| KNA-22 | [x] Hoàn tất | Storyboard hiển thị speech intent, word count và `Kling 3.0 · Native Audio · 720p`. |
| KNA-23 | [x] Hoàn tất | UI validation và server validation cùng được giữ. |
| KNA-24 | [x] Hoàn tất | Modal hiển thị toàn bộ scene/duration/exact text, model, variant, chi phí và cảnh báo retry/ngôn ngữ. |
| KNA-25 | [x] Hoàn tất | Người dùng phải phát preview; UI và C# service cùng kiểm tra `PlaybackConfirmed`. |
| KNA-26 | [x] Hoàn tất | Workflow Kling không phụ thuộc cấu hình/rate OpenAI Voice. |
| KNA-27 | [x] Hoàn tất | Render chỉ chọn `SceneVideo` của đúng `ApprovedGenerationId` trong current plan. |
| KNA-28 | [x] Hoàn tất | UI đã nối vào `ProjectRenderService`/`FfmpegRenderService`; không truyền voice/music mặc định. |
| KNA-29 | [x] Hoàn tất | Final output được kiểm tra video/audio/audibility/kích thước/duration; lỗi chỉ retry local. |
| KNA-30 | [x] Hoàn tất | README, nghiệp vụ, gateway plan, TTS plan và runbook đã đồng bộ. |
| KNA-31 | [ ] Chờ môi trường | Source/runbook đã sẵn sàng; chưa xác minh credential/rate/budget/bundle release và dashboard trên staging/production. |

Các mục `[x]` ở trên đã qua cổng test tự động. KNA-31 và smoke test tiếng Việt/Kling thật chỉ được đánh dấu hoàn tất khi có môi trường được chỉ định và phê duyệt chi phí/tác động.

### Giai đoạn A — Chốt nghiệp vụ và baseline

#### KNA-01 — Ghi nhận quyết định native-only

- Đánh dấu `KlingNative` là workflow mặc định.
- Đánh dấu `MixedVoiceOver/TTS` là `Deferred`.
- Không xóa source/schema TTS hiện hữu.
- Ghi rõ tiếng Việt là best-effort.

**Đầu ra:** tài liệu nghiệp vụ không còn mô tả TTS là bước bắt buộc của mọi scene.

#### KNA-02 — Chụp baseline kỹ thuật

- Kiểm tra thay đổi người dùng hiện hữu; không ghi đè phần không liên quan.
- Ghi nhận test count thực tế sau khi chạy, không sao chép mốc cũ.
- Chạy restore/build/test Release trước khi sửa nếu môi trường cho phép.
- Chạy web build để phát hiện baseline TypeScript.
- Không gọi provider thật trong baseline.

**Điểm dừng:** nếu baseline đang fail vì thay đổi không liên quan, ghi nhận rõ và không đổ lỗi cho task native audio.

### Giai đoạn B — Contracts và dữ liệu

#### KNA-03 — Thiết kế audio strategy

- Chọn cách lưu `AudioStrategy` cố định hoặc theo project.
- Giá trị MVP duy nhất được bật là `KlingNative`.
- `MixedVoiceOver` chỉ là reserved value, không hiển thị để chọn.
- Xác định mapping project legacy có VoiceCode/TTS asset.

#### KNA-04 — Mở rộng generated content contract

- Thêm speech mode, spoken text, speaker character key, voice style, ambient audio và SFX.
- Giữ tương thích đọc dữ liệu content plan cũ nếu có thể.
- Validation không cho speaker key ngoài danh sách character.
- Không cho nhiều speaker trong MVP.

#### KNA-05 — Mở rộng update scene/WebView contract

- TypeScript payload, C# WebView contract và handler phải thay đổi đồng thời.
- Không cho sửa scene có request Kling đang hoạt động hoặc đã hoàn thành nếu không tạo version/attempt mới rõ ràng.
- Khi sửa spoken text, cập nhật prompt version/hash và làm mất hiệu lực kết quả chưa submit cũ theo đúng nghiệp vụ.

#### KNA-06 — Migration nếu cần

- Chỉ tạo migration idempotent mới nếu field hiện hữu không đủ.
- Không sửa migration 4.0.3.
- Migration phải bảo toàn VoiceGeneration/SceneVideoNarrated lịch sử.
- Desktop least-privilege role chỉ được quyền cần thiết cho workflow data, không được đọc provider/usage truth.
- Chưa chạy migration trên database thật khi chưa xác minh instance, database, backup và restore.

**Điểm dừng giai đoạn B:** contracts compile ở cả server/desktop; migration test đạt nếu có schema mới.

### Giai đoạn C — OpenAI content planning

#### KNA-07 — Sửa JSON Schema content plan

- Yêu cầu structured output cho speech intent.
- `visual_prompt` chỉ mô tả hình ảnh/hành động/camera.
- `spoken_text` giữ nguyên nội dung theo ngôn ngữ project.
- Với scene có presenter, ưu tiên on-camera dialogue.
- Với B-roll, dùng native voice-over hoặc `None` theo mục đích.
- Không tạo hai nguồn lời cùng lúc.

#### KNA-08 — Ràng buộc độ dài và scene allocation

- Planner tính số từ dựa trên thời lượng scene.
- Nếu nội dung dài, chia narration theo scene; không tăng tốc bất thường.
- Tổng thời lượng vẫn đúng target duration.
- Mỗi scene vẫn nằm trong 3–15 giây.

#### KNA-09 — Validation output OpenAI

- Required fields đúng schema.
- Speaker thuộc character keys của scene.
- `None` không có spoken text.
- Không có prompt trống.
- Không chấp nhận output mâu thuẫn giữa on-camera và voice-over.
- Lỗi structured output không được tạo Kling request.

### Giai đoạn D — Server prompt composer

#### KNA-10 — Tạo KlingNativeAudioPromptComposer

- Tách việc dựng prompt ra khỏi chuỗi nối đơn giản hiện tại.
- Input chỉ gồm snapshot đã xác minh: scene, prompt, character, speech intent và options.
- Dựng section theo thứ tự tại mục 8.
- Chuẩn hóa whitespace và dấu ngoặc kép.
- Bảo vệ tiếng Việt/Unicode.
- Trả template version, effective prompt hash và speech hash.

#### KNA-11 — Ưu tiên và giới hạn prompt

- Thay logic cắt `[..3072]` bằng rút gọn có thứ tự.
- Không được cắt spoken text.
- Không được cắt nửa câu identity lock.
- Trả `kling_prompt_too_long` trước outbound nếu không thể rút gọn an toàn.

#### KNA-12 — Bảo mật prompt và log

- Provider vẫn nhận effective prompt đầy đủ qua HTTPS.
- Request/audit log chỉ lưu hash, template version, mode và metadata cần thiết; không log full prompt/spoken text nếu không cần.
- Không log Base64 reference hoặc Authorization.
- Error trả về desktop không chứa provider payload nhạy cảm.

### Giai đoạn E — Gateway Kling và chi phí

#### KNA-13 — Xác minh request theo dữ liệu server

Trước outbound:

1. JWT/session/device/license.
2. Organization membership và role.
3. Project thuộc organization/user.
4. Scene thuộc project và đúng current version.
5. Character/reference đã khóa nếu scene dùng character.
6. Speech intent hợp lệ.
7. Kling model modality Video.
8. Kling credential Active.
9. Rate `VideoSecond` có metadata `720p + nativeAudio=true`.
10. Budget tổ chức và member limit.

Viewer, cross-organization, stale scene, character chưa khóa, thiếu rate hoặc thiếu budget đều phải dừng trước outbound.

#### KNA-14 — Idempotency và snapshot

Request hash phải bao gồm tối thiểu:

- organization, user, project, scene;
- scene prompt ID/version;
- speech mode và speech hash;
- character/version/reference ID/hash;
- duration, aspect ratio, resolution;
- native audio flag;
- prompt template version;
- model code.

Quy tắc:

- Cùng key/cùng snapshot trả request cũ.
- Cùng key/khác spoken text hoặc prompt version trả `idempotency_key_conflict`.
- Sinh lại do audio sai phải tạo attempt/key mới và hiển thị xác nhận chi phí.
- Không gọi TTS ở bất kỳ nhánh retry nào.

#### KNA-15 — Reservation và settlement

- Reservation trong transaction `Serializable` trước submit.
- Dùng rate snapshot của Kling native audio, không dùng audio-off.
- Provider submit fail: release reservation theo chính sách hiện hành.
- Task hoàn tất: settlement theo reported cost hoặc estimated policy đã chốt.
- Retry download/review không tạo reservation/provider request mới.

### Giai đoạn F — Kling client

#### KNA-16 — Xác nhận provider request body

- `settings.audio = "native"`.
- `settings.resolution = "720p"`.
- Duration 3–15.
- `multi_shot = false`.
- Không gửi voice file/TTS URL.
- Text-to-video và image-to-video chọn đúng endpoint.
- Reference image không làm mất prompt native speech.

#### KNA-17 — Chuẩn hóa provider errors

Phân biệt tối thiểu:

- credential/permission;
- rate limit;
- moderation;
- invalid prompt;
- provider unavailable;
- native audio không được model/tài khoản hỗ trợ;
- task completed nhưng output thiếu/không hợp lệ.

Không biến mọi lỗi thành thông báo “server bảo trì”. UI phải nhận mã ổn định và lời giải thích phù hợp.

### Giai đoạn G — Desktop generation workflow

#### KNA-18 — Ngắt TTS khỏi đường mặc định

- `GenerateKlingVideosAsync` không gọi `EnsureSceneNarrationAsync` trong `KlingNative`.
- Scene đã approved không gọi TTS khi người dùng bấm tiếp tục.
- Không yêu cầu VoiceCode/VoiceSpeakingRate.
- Không kiểm tra OpenAI Voice readiness/rate/budget.
- Không xóa phương thức/entity TTS; giữ cho future mode.

#### KNA-19 — Tải và kiểm tra raw clip

- Tải qua URL tương đối có xác thực.
- Ghi `.part`, kiểm tra giới hạn dung lượng rồi đổi tên nguyên tử.
- FFprobe xác nhận video stream, audio stream, codec, duration, dimensions và sample rate.
- AudioQualityValidator xác nhận `IsAudible`.
- Lưu `SceneVideo`, không tạo `SceneVideoNarrated`.
- Metadata ghi native audio expected/present/audible và quality metrics.

#### KNA-20 — Trạng thái review

Đề xuất trạng thái:

- `Generated`: đã tải clip nhưng chưa review.
- `AudioReviewRequired`: có audio nghe được, cần người dùng kiểm tra lời.
- `NativeAudioInvalid`: thiếu audio stream hoặc gần như im lặng; áp dụng cho cả cảnh chỉ có ambience/SFX để bảo đảm video không bị mất tiếng.
- `Approved`: người dùng đã nghe và duyệt.
- `Failed`: lỗi provider/download/media không tạo được clip hợp lệ.

Không dùng `Approved` ngay sau download đối với scene có expected spoken text.

#### KNA-21 — Chính sách audio không đạt

- Audio không nghe được: không cho duyệt, bất kể speech mode.
- Hiển thị lý do và nút sinh lại có cảnh báo chi phí.
- `SpeechMode = None` vẫn phải có ambience/SFX nghe được trong MVP; chưa hỗ trợ scene chủ ý im lặng.
- Không tự gọi lại Kling.
- Không fallback ngầm sang TTS.

### Giai đoạn H — UI/UX

#### KNA-22 — Storyboard editor

Mỗi scene hiển thị:

- Speech mode.
- “Lời Kling sẽ nói”.
- Người nói.
- Sắc thái giọng.
- Âm thanh môi trường/SFX.
- Số từ so với thời lượng.
- Model `Kling 3.0 · Native Audio · 720p`.

#### KNA-23 — Validation phía UI

- Spoken text bắt buộc theo speech mode.
- Cảnh báo vượt word budget.
- On-camera bắt buộc một character đã gắn.
- Không cho hai speaker.
- Không cho lưu scene mâu thuẫn.
- Server vẫn validation lại; UI không phải trust boundary.

#### KNA-24 — Modal xác nhận chi phí

Trước mỗi attempt mới hiển thị:

- scene và duration;
- model/resolution/native audio;
- exact spoken text;
- chi phí ước tính;
- cảnh báo retry phát sinh chi phí mới;
- cảnh báo tiếng Việt best-effort nếu `vi-VN`.

#### KNA-25 — Preview và review

- Card video có điều khiển âm lượng rõ ràng.
- Hiển thị badge “Có Native Audio”, “Cần nghe duyệt” hoặc “Audio không đạt”.
- Nút: nghe lại, duyệt, sửa prompt trước attempt mới, sinh lại.
- Không gọi provider chỉ vì người dùng mở preview.
- Không tự khóa/duyệt scene sau khi tải.

#### KNA-26 — Dọn UI TTS khỏi workflow chính

- Không bắt chọn giọng TTS khi tạo project.
- Không hiển thị lỗi thiếu Voice pricing/Voice credential trong luồng Kling.
- Voice settings lịch sử có thể ẩn dưới nhãn tính năng tương lai; không xóa dữ liệu.

### Giai đoạn I — Final render

#### KNA-27 — Chọn asset nguồn

- Project `KlingNative` dùng `SceneVideo` đã approved.
- Không ưu tiên `SceneVideoNarrated` lịch sử cho render mới trừ khi project rõ ràng ở future mode.
- Không dùng asset chưa review.
- Thứ tự scene theo current scene plan.

#### KNA-28 — Giữ native audio qua FFmpeg

- Normalize không dùng `-an`.
- Mọi scene đầu vào được chuẩn hóa video/audio tương thích để concat.
- Scene chủ ý im lặng cần audio track im lặng có format tương thích nếu concat yêu cầu, nhưng không thay clip có native audio.
- Final concat giữ lời nói, ambience và SFX.
- Nếu có music feature, mặc định tắt; nếu bật thì mix nhỏ hơn native speech, không replace audio.

#### KNA-29 — Kiểm tra final output

- FFprobe xác nhận final video có video/audio stream.
- AudioQualityValidator xác nhận output nghe được khi ít nhất một scene có speech/audio.
- Duration gần bằng tổng scene theo tolerance đã chốt.
- Lỗi render retry cục bộ, không submit Kling mới.

### Giai đoạn J — Tài liệu và vận hành

#### KNA-30 — Cập nhật nguồn sự thật

- `NGHIEP_VU_HE_THONG_VIDEOMAKER.md`: thay nghiệp vụ TTS mặc định bằng Kling native-only.
- `KE_HOACH_TTS_GIONG_DOC_VIDEO.md`: đánh dấu Deferred, không xóa lịch sử.
- `README.md`: mô tả storyboard/native audio/review chính xác.
- `KE_HOACH_SERVER_AI_GATEWAY.md`: cập nhật trạng thái nếu contracts/workflow thay đổi.
- Runbook: không yêu cầu Voice model/rate cho Kling native-only.

#### KNA-31 — Cấu hình vận hành

- Kling credential Active.
- Kling model/rate native audio Active.
- Budget và member limit đủ.
- FFmpeg/FFprobe bundle hợp lệ.
- Không yêu cầu `gpt-4o-mini-tts` model/rate để tạo Kling.
- Dashboard theo dõi task failure, audio invalid, retry rate và chi phí mỗi accepted clip.

## 10. Mã lỗi/trạng thái đề xuất

| Mã | Ý nghĩa | Outbound đã xảy ra? |
|---|---|---:|
| `kling_native_audio_required` | Request không dùng đúng 720p native audio | Không |
| `kling_speech_mode_invalid` | Speech mode và spoken text mâu thuẫn | Không |
| `kling_speaker_invalid` | Speaker không thuộc scene/character | Không |
| `kling_spoken_text_too_long` | Lời vượt giới hạn an toàn của scene | Không |
| `kling_prompt_too_long` | Không thể rút prompt an toàn | Không |
| `kling_scene_version_conflict` | Scene/prompt đã đổi so với snapshot | Không |
| `kling_native_audio_missing` | Output không có audio stream | Có |
| `kling_native_audio_inaudible` | Audio stream gần như im lặng | Có |
| `kling_audio_review_required` | Clip có tiếng nhưng cần người dùng nghe duyệt | Có |
| `kling_language_best_effort` | Ngôn ngữ chưa được provider hỗ trợ chính thức | Chỉ cảnh báo |

Các lỗi quyền, organization, budget, pricing, credential và idempotency tiếp tục dùng mã ổn định hiện có.

## 11. Kế hoạch test tự động

### 11.1. Contract tests

- Deserialize content response mới có speech intent.
- Tương thích payload cũ nếu đã quyết định hỗ trợ.
- `None` + spoken text bị từ chối.
- On-camera thiếu speaker bị từ chối.
- Speaker ngoài character keys bị từ chối.
- WebView TypeScript/C# payload đồng nhất.

### 11.2. OpenAI content client tests

- JSON Schema có đủ speech mode/text/speaker/style/ambient/SFX.
- Instructions yêu cầu visual và speech tách biệt.
- Scene 3–15 giây có word budget phù hợp.
- Planner chia narration dài, không cắt mất nội dung.
- Structured output sai không tạo dữ liệu scene.
- Nội dung tiếng Việt giữ nguyên Unicode.

### 11.3. Prompt composer unit tests

- On-camera prompt chứa đúng speaker và exact line đúng một lần.
- Native voice-over prompt ghi rõ không có on-screen dialogue.
- `None` không thêm lời nói giả.
- Identity lock đứng trước visual decoration.
- Speech đứng trước phần có thể bị rút gọn.
- Prompt chứa language, voice style, lip-sync, ambience và SFX.
- Negative constraints không mâu thuẫn positive prompt.
- Không thêm TTS/voice file instruction.
- Prompt không vượt 3.072 ký tự.
- Prompt dài rút đúng section ưu tiên.
- Không cắt spoken text, Unicode hoặc surrogate pair.
- Không thể rút an toàn thì fail trước outbound.
- Template version/hash ổn định với cùng input.
- Sửa một từ spoken text làm speech hash/request hash thay đổi.

### 11.4. Gateway authorization/security tests

- Viewer không submit Kling.
- User ngoài organization bị chặn.
- Project/scene/character cross-organization bị chặn.
- Character chưa khóa hoặc reference sai hash bị chặn.
- Stale scene/prompt version bị chặn.
- Thiếu Kling credential không outbound.
- Thiếu rate native audio không outbound.
- Budget bằng 0 không outbound.
- Member limit không đủ không outbound.
- Prompt/spoken text/Base64/Authorization không xuất hiện trong log được phép kiểm tra.

### 11.5. Idempotency và budget tests

- Cùng key/cùng snapshot chỉ một outbound Kling call.
- Cùng key/khác spoken text trả conflict.
- Hai request đồng thời không reserve hai lần.
- Retry status/download/review không tính phí mới.
- Attempt sinh lại dùng key mới và có reservation mới rõ ràng.
- Provider submit fail giải phóng reservation.
- Worker settlement dùng đúng rate snapshot.
- Thay rate sau submit không đổi chi phí request cũ.

### 11.6. Kling client tests

- Request body có `audio = native`.
- Request body có `720p`, đúng duration/aspect ratio và `multi_shot=false`.
- Text-to-video dùng đúng endpoint.
- Image-to-video gửi reference đúng nhưng không làm mất prompt.
- Không gửi TTS URL/audio file.
- Provider 400/401/403/429/5xx được chuẩn hóa.
- Response không có task ID hoặc output bị từ chối an toàn.

### 11.7. Desktop workflow tests

- Scene có spoken text không gọi `GenerateSceneVoiceAsync`.
- Scene approved không gọi TTS khi resume.
- Không cần VoiceCode/Voice readiness để submit Kling.
- Tải clip dùng `.part` và đổi tên nguyên tử.
- Hash/MIME/size/video stream sai bị từ chối.
- Clip hợp lệ tạo đúng `SceneVideo`.
- Không tạo `SceneVoice` hoặc `SceneVideoNarrated`.
- Audio audible đưa scene vào review, không auto approve.
- Audio missing/inaudible đưa scene vào trạng thái lỗi audio.
- Retry review/download không submit Kling.
- Retry generation chỉ chạy sau attempt mới.

### 11.8. Audio quality tests

- MP4 có AAC audible được nhận đúng.
- MP4 có audio stream gần im lặng bị đánh dấu inaudible.
- MP4 không audio stream bị đánh dấu missing.
- Noise ngắn không bị nhầm thành lời đạt nếu threshold chưa đủ.
- Tolerance áp dụng nhất quán cho clip 3, 5, 10 và 15 giây.
- File test nhỏ, có nguồn/provenance phù hợp và không chứa dữ liệu nhạy cảm.

### 11.9. Render integration tests

- Normalize giữ audio native.
- Concat nhiều scene giữ audio và đúng thứ tự.
- Một scene im lặng chủ ý không làm mất audio scene khác.
- Không ưu tiên narrated asset trong `KlingNative`.
- Final MP4 có video/audio stream, duration hợp lệ và audio nghe được.
- Render retry không gọi provider.
- Đường dẫn có khoảng trắng/ký tự Unicode được xử lý an toàn.

### 11.10. UI/Web tests

- Speech mode hiển thị đúng.
- Word count/cảnh báo theo duration đúng.
- Không cho on-camera thiếu character.
- Modal chi phí hiển thị exact spoken text và cảnh báo tiếng Việt.
- Badge audio/review đúng trạng thái.
- Nút preview không phát sinh outbound.
- Nút sinh lại yêu cầu xác nhận chi phí.
- Web production build không lỗi TypeScript.

### 11.11. Regression TTS tests

- Entity/migration/API TTS hiện hữu vẫn compile và đọc dữ liệu cũ.
- Workflow KlingNative không gọi TTS.
- Không xóa voice asset lịch sử.
- Future `MixedVoiceOver` vẫn bị disabled rõ ràng, không vô tình chạy nửa workflow.

### 11.12. Migration tests nếu có schema mới

- Script idempotent, chạy lặp không lỗi.
- Default audio strategy đúng cho project mới và legacy.
- Không mất `VoiceGeneration`, `SceneVideoNarrated` hoặc project voice settings cũ.
- Index/FK/check constraint đúng.
- Desktop role không được mở thêm quyền provider/usage/payload.
- Script được đọc UTF-8 với `-f 65001`.

## 12. Thứ tự chạy test trong quá trình triển khai

### Sau contracts/data

```powershell
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --filter "Contracts|Migration"
```

Tên filter thực tế phải đối chiếu test class sau khi tạo; không ghi nhận pass nếu filter không chọn test nào.

### Sau server prompt/gateway

```powershell
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --filter "Kling|Generation|NativeAudio|AiCostEstimator"
```

### Sau desktop/media

```powershell
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --filter "NativeAudio|AudioQuality|FfmpegRender"
```

### Sau UI

```powershell
Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
```

### Xác minh cuối từ repository root

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Yêu cầu:

- Build Release không warning/error.
- Toàn bộ test đạt, không chỉ test mới.
- Ghi test count thực tế tại thời điểm chạy.
- Không tuyên bố smoke test provider đạt nếu chưa thực sự gọi provider trong môi trường được phê duyệt.

### Kết quả xác minh cuối ngày 2026-08-29

- [x] `dotnet restore TOOL_GEN_POST_VIDEO.slnx -m:1`: đạt, toàn bộ project đã up-to-date.
- [x] `dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore -m:1`: đạt, 0 warning, 0 error; web production build được chạy trong build `TOOL-LOCAL`.
- [x] `dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build -m:1 --logger "trx;LogFileName=KlingNativeAudio-final.trx"`: 266/266 test đạt, 0 fail, 0 skip.
- [x] Test tập trung gateway mới: 4/4 đạt (`GenerationServiceKlingNativeAudioTests`).
- [x] Test tập trung review/render/client/UI: 27/27 đạt.
- [x] File kết quả: `TOOL-TESTS\TestResults\KlingNativeAudio-final.trx`.
- [ ] Chưa chạy migration/database thật, request Kling thật, đối chiếu provider billing hoặc publish release.

## 13. Test thủ công không phát sinh chi phí

1. Tạo project local/stub với một scene có character.
2. Sửa spoken text và xác nhận word count thay đổi.
3. Kiểm tra prompt preview đã lọc không lộ secret.
4. Kiểm tra UI chặn on-camera khi chưa có character/reference khóa.
5. Dùng fixture MP4 audible để kiểm tra trạng thái review.
6. Dùng fixture MP4 silent để kiểm tra trạng thái native audio invalid.
7. Kiểm tra preview không gọi server generation.
8. Kiểm tra retry download dùng request cũ.
9. Render fixture nhiều scene và nghe/đo audio final.
10. Mở project legacy có voice asset để xác nhận không mất dữ liệu và workflow mới không tự gọi TTS.

## 14. Smoke test Kling thật có chi phí

Chỉ thực hiện khi người dùng xác nhận rõ môi trường staging, credential, rate, budget và chấp nhận chi phí.

### 14.1. Điều kiện trước test

- Xác minh đúng server instance và database staging.
- Có backup và thử được restore nếu có migration.
- Kling credential Active và đã test.
- Rate Active khớp `720p + nativeAudio=true`.
- Budget staging giới hạn nhỏ, đủ cho ma trận test.
- Không sử dụng project/customer data nhạy cảm.
- Có người nghe tiếng Việt để chấm kết quả.

### 14.2. Ma trận tối thiểu

| Mẫu | Duration | Mode | Ngôn ngữ | Character | Mục tiêu |
|---|---:|---|---|---:|---|
| A1 | 5s | OnCameraDialogue | English | Có | Baseline provider hỗ trợ |
| A2 | 10s | OnCameraDialogue | English | Có | Lip-sync và câu dài hơn |
| A3 | 15s | NativeVoiceOver | English | Không | Native voice-over baseline |
| V1 | 5s | OnCameraDialogue | Vietnamese | Có | Câu rất ngắn |
| V2 | 10s | OnCameraDialogue | Vietnamese | Có | Câu trung bình |
| V3 | 15s | OnCameraDialogue | Vietnamese | Có | Câu tối đa khuyến nghị |
| V4 | 10s | NativeVoiceOver | Vietnamese | Không | Kiểm tra voice-over tiếng Việt |
| S1 | 5s | None | Không lời | Tùy chọn | Ambience/SFX |

Mỗi mẫu chỉ retry sau khi ghi nhận kết quả attempt cũ và xác nhận chi phí mới.

### 14.3. Tiêu chí chấm thủ công

Chấm từng clip theo thang đạt/không đạt:

- Có audio nghe được.
- Đúng ngôn ngữ.
- Không tự dịch sang tiếng Anh.
- Gần đúng/đúng nguyên văn.
- Không lặp từ hoặc nói vô nghĩa.
- Người nói đúng character.
- Khẩu hình hợp lý.
- Giọng rõ hơn ambience.
- Không có speaker thừa.
- Hình ảnh/identity không bị suy giảm do prompt audio.

### 14.4. Đối chiếu chi phí

- ProviderRequest chỉ có một record cho mỗi idempotent attempt.
- Reservation/settlement/release đúng.
- Usage ledger truy được về organization/user/project/scene/model.
- Số giây và actual/estimated cost khớp policy/rate snapshot.
- Provider dashboard được đối chiếu với ledger.

### 14.5. Cổng quyết định tiếng Việt

Sau smoke test, Product Owner chọn một trong hai:

1. **Experimental allow:** cho phép `vi-VN`, luôn cảnh báo và bắt buộc review.
2. **Block until supported:** chặn native speech tiếng Việt, chỉ cho ambience/SFX hoặc ngôn ngữ được hỗ trợ.

Không được bỏ cảnh báo và quảng cáo “đọc đúng tiếng Việt” nếu dữ liệu test không chứng minh được.

## 15. Điều kiện nghiệm thu

### 15.1. Nghiệp vụ

- Giai đoạn mặc định chỉ dùng Kling Native Audio.
- Scene có lời nói đưa đúng expected spoken text vào prompt Kling.
- Không gọi TTS hoặc FFmpeg voice mixing.
- Người dùng nghe thử trước khi duyệt scene có lời.
- Retry có xác nhận chi phí và không tạo trùng do idempotency.

### 15.2. Hình ảnh và âm thanh

- Character/reference consistency tiếp tục được giữ.
- Clip có speech phải có audio nghe được mới vào bước review.
- Clip im lặng không tự Approved.
- Final video giữ native speech/ambience/SFX của các scene.
- Không có hai giọng cùng đọc một nội dung.

### 15.3. Bảo mật và chi phí

- Desktop không có provider key và không gọi Kling trực tiếp.
- Viewer/cross-organization/license/budget/rate lỗi đều bị chặn trước outbound.
- Prompt đầy đủ, spoken text nhạy cảm, Base64 và Authorization không bị log ngoài policy.
- Rate snapshot đúng biến thể native audio.
- Worker tiếp tục polling khi desktop đóng.

### 15.4. Chất lượng kỹ thuật

- Restore/build/test Release đạt toàn bộ.
- Web production build đạt.
- Không warning/error mới.
- Regression TTS/data legacy đạt dù TTS không được gọi.
- Smoke test chỉ được đánh dấu đạt khi đã chạy thật và ghi nhận chi phí/kết quả.

## 16. Rollback và khả năng phục hồi

- Ưu tiên bọc workflow mới bằng audio strategy/feature gate rõ ràng, không xóa TTS source.
- Nếu prompt composer mới gây lỗi trước outbound, có thể tắt generation native speech nhưng không fallback ngầm sang TTS.
- ProviderRequest Kling đã submit phải tiếp tục được worker polling dù desktop/version mới rollback.
- Raw `SceneVideo` đã tải không bị xóa khi review/render lỗi.
- Migration mới nếu có phải có backup/restore plan; không rollback bằng cách xóa cột/bảng chứa dữ liệu production mà chưa đánh giá.
- Render lỗi chỉ retry cục bộ.
- Không dùng `git reset --hard` hoặc thao tác phá hủy thay đổi người dùng.

## 17. Thứ tự triển khai khuyến nghị

1. KNA-01 → KNA-02: chốt tài liệu và baseline.
2. KNA-03 → KNA-06: contracts, strategy và migration nếu cần.
3. KNA-07 → KNA-09: content planning có native speech intent.
4. KNA-10 → KNA-12: prompt composer và bảo mật log.
5. KNA-13 → KNA-17: gateway, idempotency, pricing và Kling client.
6. KNA-18 → KNA-21: desktop workflow và audio review.
7. KNA-22 → KNA-26: UI/UX.
8. KNA-27 → KNA-29: final render giữ native audio.
9. KNA-30 → KNA-31: tài liệu/runbook.
10. Chạy toàn bộ test tự động.
11. Test thủ công bằng fixture không chi phí.
12. Dừng và xin xác nhận trước migration staging hoặc Kling smoke test có chi phí.
13. Chạy smoke test, đối chiếu ledger/provider và quyết định chính sách tiếng Việt.

## 18. Checklist trước khi bắt đầu code

- [x] Product Owner xác nhận `KlingNative` là workflow duy nhất của MVP.
- [x] Xác nhận `OnCameraDialogue`, `NativeVoiceOver`, `None` là ba mode cần dùng.
- [x] Giữ entity/API/asset TTS legacy nhưng tách khỏi đường gọi mặc định.
- [x] Bắt buộc manual review cho mọi scene trước khi `Approved`.
- [x] Tiếng Việt chạy best-effort và có cảnh báo trong UI.
- [x] Dùng AudioStrategy cố định `KlingNative`, không cần migration mới.
- [x] Nhạc nền không nằm trong generation MVP; renderer hiện hữu không thay thế native speech.
- [x] Không chạy migration/provider thật trong unit/integration test.
- [x] Baseline trước sửa: Release build 0 warning/error và 245/245 test đạt.

## 19. Định nghĩa hoàn tất của kế hoạch triển khai

Task chỉ được coi là hoàn tất khi đồng thời thỏa:

1. Source, contracts, UI và tài liệu cùng mô tả một workflow native-only.
2. Prompt Kling thực tế chứa exact spoken text có cấu trúc, không chỉ có visual prompt.
3. Không có đường gọi mặc định nào tự sinh TTS hoặc `SceneVideoNarrated`.
4. Clip audio không đạt không bị tự động duyệt.
5. Final render giữ audio native.
6. Toàn bộ test tự động đạt và số lượng test được ghi nhận thực tế.
7. Smoke test tiếng Việt có kết luận rõ ràng hoặc tính năng vẫn mang nhãn experimental/block.
8. Không có migration, provider cost hoặc release production nào được thực hiện ngoài phạm vi đã phê duyệt.
