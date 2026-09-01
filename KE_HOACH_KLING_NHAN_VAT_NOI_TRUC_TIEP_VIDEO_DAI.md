# Kế hoạch triển khai Kling để nhân vật nói trực tiếp trong video dài

> Trạng thái: đã triển khai source và kiểm thử tự động; chưa chạy smoke test Kling thật có phí  
> Ngày lập: 2026-09-01  
> Phạm vi: chỉ workflow video dài/nhiều cảnh có `Script.StructureType = OpenAiStructuredPlan`, project đã snapshot provider `Kling` và dùng Kling Native Audio  
> Không áp dụng: video ngắn `DirectShortVideo`, BytePlus/Seedance, TTS ghép ngoài hoặc dữ liệu lịch sử đã hoàn tất

## 1. Mục tiêu

- Khi một cảnh video dài có đúng một nhân vật và có lời, nhân vật đó phải là người nói trực tiếp trên màn hình.
- Kling phải nhận prompt ưu tiên lời nói, gắn rõ câu thoại với người trong ảnh first-frame và yêu cầu chuyển động miệng/biểu cảm theo từng từ.
- Cảnh B-roll không có nhân vật vẫn có thể dùng `NativeVoiceOver`.
- Output thiếu tiếng, gần như im lặng hoặc không đạt kiểm tra nghe duyệt không được trở thành clip `Approved`.
- Retry sau `NativeAudioInvalid` phải dùng biến thể prompt phục hồi lời nói; không chỉ gửi lại nguyên prompt.
- Giữ nguyên các bất biến gateway-only, organization budget, idempotency, credential, output proxy và quy trình người dùng nghe duyệt.

## 2. Bằng chứng chẩn đoán hiện tại

Kết quả đọc source và thống kê ẩn danh từ database ngày 2026-09-01 cho thấy hai nhánh khác nhau:

1. Một project trước đó có 5/5 cảnh gắn nhân vật nhưng đều là `NativeVoiceOver`. Prompt hiện hành của nhánh này ghi rõ không nhân vật nào trên màn hình được nói, vì vậy hành vi nhân vật chỉ đứng/diễn trong khi lời phát ngoài khung hình là đúng theo lệnh hệ thống.
2. Hai content plan gần nhất đã lưu đúng 5/5 cảnh là `OnCameraDialogue`; việc ánh xạ `spoken_text` sang `Scene.Dialogue` không bị mất.
3. Lần render trực tiếp gần nhất gửi `NativeAudio=true`, model `kling-3.0`, `SpeechMode=OnCameraDialogue` và provider báo `Completed`.
4. Media tải về có audio stream nhưng gần như im lặng: mean volume `-57.1 dB`, max volume `-38.8 dB`; hệ thống đã chuyển generation sang `NativeAudioInvalid` với lỗi `audio_effectively_silent`.
5. Trong 5 visual prompt thuộc content plan gần nhất:
   - 0/5 mô tả nhân vật đang nói, đang phát biểu hoặc chuyển động miệng;
   - 0/5 gắn hành động nói với camera/người nghe;
   - 2/5 lại mô tả hành động cười.
6. Kiểm tra audio hiện chỉ xác nhận stream và mức âm lượng. Nó không chứng minh có giọng người, đúng nguyên văn, đúng người nói hoặc khớp khẩu hình.

Kết luận thiết kế: không có lỗi bỏ quên cờ Native Audio và không có lỗi mất `Dialogue` ở đường lưu hiện hành. Khoảng trống chính nằm ở chính sách chọn `SpeechMode`, hợp đồng hành động nhìn thấy được và cách ưu tiên phần thoại trong prompt Kling.

## 3. Quyết định nghiệp vụ mục tiêu

Áp dụng ma trận sau cho content plan video dài Kling:

| Nhân vật trong cảnh | `spoken_text` | Chế độ hợp lệ | Hành vi |
|---:|---:|---|---|
| 1 | Có | `OnCameraDialogue` | Nhân vật duy nhất nói trực tiếp và lip-sync |
| 0 | Có | `NativeVoiceOver` | Một narrator ngoài khung hình; cảnh là B-roll |
| 0 | Không | `None` | Chỉ ambience/SFX |
| 1 | Không | `None` | Chỉ hợp lệ khi người dùng chủ động tạo một nhịp không lời |
| 1 | Có | `NativeVoiceOver` | Không hợp lệ trong policy video dài Kling này |
| Khác 0 hoặc 1 | Bất kỳ | Không hợp lệ | Kling MVP chỉ hỗ trợ tối đa một nhân vật tham chiếu/cảnh |

Các quy tắc bổ sung:

- OpenAI không được tự chọn voice-over cho một cảnh vẫn gắn nhân vật trình bày.
- `NativeVoiceOver` chỉ được dùng khi `character_keys=[]`.
- `OnCameraDialogue` phải có đúng một `speaker_character_key`, trùng với nhân vật duy nhất của cảnh.
- `None` không được chứa `spoken_text` hoặc speaker.
- Người dùng vẫn được tạo cảnh không lời có nhân vật, nhưng phải chọn rõ `None`; hệ thống không suy diễn lời nói từ visual prompt.
- Không tự động sửa/dịch content plan cũ. Project cũ muốn dùng policy mới phải sinh lại plan hoặc sửa từng cảnh rồi tạo generation mới.

## 4. Luồng mục tiêu

```text
OpenAI structured plan
    → kiểm tra quan hệ character/speech mode/spoken text
    → desktop lưu Dialogue hoặc Narration đúng mode
    → server đọc scene hiện hành và dựng speech-first prompt
    → preflight prompt trước quote/reserve
    → gửi Kling với 720p + Native Audio
    → worker hoàn tất và desktop tải media
    → kiểm tra stream/độ nghe được
    → người dùng nghe, đối chiếu lời và khẩu hình
    → Approved hoặc NativeAudioInvalid/tạo attempt mới
```

## 5. Trạng thái task triển khai

### Task 1 — Khóa policy speech intent cho video dài Kling — hoàn tất

Mục tiêu:

- Có một policy dùng chung để xác định mode hợp lệ từ workflow, provider, số nhân vật, lời nói và speaker.
- Không để OpenAI, desktop và server áp dụng ba cách suy diễn khác nhau.

Hạng mục:

- Đặt policy ở lớp generation dùng chung phía server; desktop có validator tương ứng để phản hồi sớm nhưng server vẫn là ranh giới tin cậy.
- Chỉ bật policy khi đồng thời:
  - `StructureType = OpenAiStructuredPlan`;
  - project snapshot provider là `Kling`.
- Giữ nguyên hành vi `DirectShortVideo` và provider khác.
- Chuẩn hóa mã lỗi dự kiến:
  - `kling_on_camera_speaker_required`;
  - `kling_voice_over_character_not_allowed`;
  - `kling_speech_intent_invalid`.
- Chặn trước resolver/rate/budget/outbound nếu scene vi phạm.

File/khu vực dự kiến tác động:

- `TOOL-SERVER/Generation/OpenAiContentClient.cs`
- `TOOL-SERVER/Generation/GenerationService.cs`
- policy/validator generation mới nếu cần
- validator tương ứng trong `TOOL-LOCAL/Generation` hoặc `TOOL-LOCAL/Projects`
- test generation và desktop

Tiêu chí hoàn tất:

- Cảnh một nhân vật có lời không thể đi vào Kling dưới mode voice-over.
- Cảnh B-roll không nhân vật có lời vẫn dùng được voice-over.
- Request bị chặn không tạo provider request, reservation hoặc usage Kling.

### Task 2 — Siết hợp đồng OpenAI content plan — hoàn tất

Mục tiêu:

- OpenAI tạo đúng speech intent ngay từ đầu, thay vì dựa vào bước submit để sửa sai.

Hạng mục:

- Cập nhật instructions và per-scene speech contracts:
  - một nhân vật + có lời → `OnCameraDialogue`;
  - không nhân vật + có lời → `NativeVoiceOver`;
  - voice-over không được giữ presenter trong `character_keys`;
  - on-camera phải gắn speaker chính xác.
- Với `OnCameraDialogue`, `visual_prompt` phải mô tả hành động nhìn thấy được mà không lặp nguyên văn lời:
  - nhân vật đang phát biểu;
  - khuôn mặt và miệng nhìn thấy rõ;
  - bắt đầu nói sớm;
  - cử chỉ/biểu cảm diễn ra trong khi nói.
- Không cho hành động chính chỉ là đứng, tạo dáng hoặc mỉm cười.
- Giữ `spoken_text` riêng và nguyên văn; không nhét câu thoại vào `visual_prompt`.
- Nâng validator output OpenAI để kiểm tra quan hệ chéo giữa `speech_mode`, `spoken_text`, `speaker_character_key` và `character_keys` theo policy mới.
- Không thêm một cuộc gọi OpenAI thứ hai chỉ để sửa speech mode.

Quyết định contract/schema:

- Giai đoạn này ưu tiên dùng các trường hiện có; chưa thêm DTO hoặc cột database mới.
- Hành động nói bắt buộc sẽ được bảo đảm lần hai tại prompt composer, nên không phụ thuộc hoàn toàn vào khả năng OpenAI viết đúng từ khóa trong `visual_prompt`.

Tiêu chí hoàn tất:

- Content plan tự động không còn sinh cảnh có presenter nhưng mode voice-over.
- Visual prompt on-camera luôn có hành động nói nhìn thấy được nhưng không sao chép câu thoại.
- Kết quả sai bị từ chối tại biên OpenAI, không được lưu thành plan hợp lệ.

### Task 3 — Dựng Kling prompt theo chiến lược speech-first — hoàn tất

Mục tiêu:

- Đặt người nói, câu thoại và hành động miệng ở vị trí ưu tiên cao nhất trong prompt.

Thiết kế prompt `OnCameraDialogue`:

1. Mở đầu bằng shot/duration/aspect ratio và Native Audio.
2. Gắn rõ người nói:
   - đây là người duy nhất trên màn hình;
   - đây là nhân vật trong ảnh first-frame đã cung cấp;
   - tên nhân vật chỉ là nhãn liên kết, không phải một narrator khác.
3. Dùng cú pháp tự nhiên trực tiếp: nhân vật nói câu được đặt trong dấu nháy.
4. Yêu cầu bắt đầu nói trong khoảng 0–0,5 giây đầu và hoàn tất một lần trong thời lượng cảnh.
5. Yêu cầu môi, hàm, biểu cảm và cử chỉ nhìn thấy rõ, đồng bộ với từng từ.
6. Cấm narrator, giọng phụ, lặp, dịch, diễn giải, đứng im hoặc chỉ mỉm cười không nói.
7. Sau phần performance mới ghép identity lock, wardrobe, immutable traits và project assets.
8. Cuối cùng ghép scene/action/camera và negative constraints trong phần dung lượng còn lại.

Thiết kế prompt `NativeVoiceOver`:

- Giữ một narrator ngoài khung hình.
- Policy bảo đảm cảnh không gắn nhân vật presenter.
- Không chèn yêu cầu lip-sync hoặc người trên màn hình nói.

Thiết kế audio:

- Speech luôn foreground.
- Không background music trong MVP speech-first.
- Ambience/SFX phải ở mức nhẹ, không cạnh tranh với lời.
- Khi on-camera, không dùng mô tả âm thanh có thể bị hiểu thành chỉ cần ambience là đủ.

Version/idempotency:

- Dùng template speech-first có version; bản tiếng Việt hiện hành là `kling-native-audio-v4-vietnamese-speech-first`.
- Template version mới phải đi vào safe request snapshot và request hash như hiện tại.
- Không log full prompt hoặc nguyên văn câu thoại.

Tiêu chí hoàn tất:

- Phần speech/performance xuất hiện trước các khối identity/assets dài.
- Prompt chỉ có một speaker rõ ràng và gắn được với người trong first-frame.
- Câu thoại được giữ nguyên, không dịch, cắt hoặc diễn giải.
- Prompt vẫn không vượt giới hạn hiện hành; phần bắt buộc vượt giới hạn phải bị chặn trước outbound.

### Task 4 — Thêm preflight mâu thuẫn hành động trước outbound — hoàn tất

Mục tiêu:

- Không chi tiền Kling cho một scene có speech intent và visual action đối nghịch nhau.

Hạng mục:

- Với `OnCameraDialogue`, kiểm tra bắt buộc:
  - đúng một character snapshot đã khóa;
  - có primary reference hợp lệ;
  - `Dialogue` không rỗng, `Narration` rỗng;
  - speaker name có thể dựng được;
  - Native Audio và model capability đúng snapshot;
  - prompt bắt buộc còn đủ chỗ cho toàn bộ câu thoại.
- Phát hiện các mâu thuẫn rõ ràng trong nguồn scene/negative prompt như:
  - silent/does not speak;
  - closed mouth/mouth remains closed;
  - listening only;
  - off-screen narrator/voice-over khi mode là on-camera;
  - chỉ yêu cầu silent smile.
- Không dựa vào keyword validator như bảo đảm duy nhất. Prompt composer vẫn phải tự chèn performance contract chuẩn.
- Với nguồn do người dùng chỉnh tay, trả lỗi có danh sách field an toàn; không âm thầm sửa nội dung đã duyệt.

Tiêu chí hoàn tất:

- Mâu thuẫn được chặn trước quote/reserve/outbound.
- Lỗi hiển thị bằng tiếng Việt và chỉ ra cảnh/trường cần sửa, không lộ full prompt trong log.

### Task 5 — Thiết kế retry phục hồi lời nói — hoàn tất

Mục tiêu:

- Attempt sau `NativeAudioInvalid` có khả năng sửa lỗi thật sự và vẫn kiểm soát chi phí.

Hạng mục:

- Không tự động gọi lại Kling. Người dùng phải xác nhận một attempt có phí mới.
- Server xác định đây là retry từ generation terminal `NativeAudioInvalid`; không tin attempt profile do desktop tự khai báo.
- Dùng profile dự kiến `speech-recovery-v1`:
  - đưa speaker/câu thoại lên ngay câu đầu;
  - medium close-up hoặc medium shot, mặt và miệng luôn thấy rõ;
  - bắt đầu nói ngay, không có đoạn intro im lặng;
  - bỏ nhạc nền;
  - giảm ambience/SFX về room tone tối thiểu;
  - cấm silent smile, pose hoặc hành động phức tạp cạnh tranh với lời.
- Ghi tên profile/version vào safe `RequestJson`, request hash và audit metadata; không ghi full speech.
- Idempotency key mới cho attempt mới; cùng key/profile/hash vẫn replay an toàn.
- UI phải hiển thị đây là request mới và có thể phát sinh chi phí mới.

Tiêu chí hoàn tất:

- Retry không dùng lại nguyên effective prompt của attempt im lặng.
- Không double reserve hoặc double submit khi replay.
- Không có retry provider tự động ngoài sự đồng ý của người dùng.

### Task 6 — Giữ và nâng quy trình kiểm tra audio — hoàn tất MVP

Mục tiêu MVP:

- Không để output im lặng hoặc gần im lặng được duyệt.
- Không tuyên bố tự động rằng câu thoại đúng nếu hệ thống chỉ đo âm lượng.

Hạng mục MVP:

- Giữ kiểm tra stream, duration, mean/max volume và silent ratio hiện có.
- Giữ `NativeAudioInvalid` khi audio thiếu hoặc gần như im lặng.
- Giữ `AudioReviewRequired` cho output có tín hiệu nghe được.
- Metadata chỉ lưu speech hash, mode và chỉ số audio; không lưu transcript/raw prompt trong quality log.
- UI yêu cầu người dùng xác nhận ba mục trước khi duyệt:
  - nghe rõ đủ câu;
  - đúng nhân vật trên màn hình nói;
  - khẩu hình/biểu cảm chấp nhận được.

Giai đoạn mở rộng, không chặn MVP:

- Nghiên cứu Voice Activity Detection để phân biệt giọng người với ambience.
- Chỉ bổ sung ASR nếu có quyết định riêng về model, credential, rate, budget, quyền riêng tư và nơi xử lý audio.
- Không gọi OpenAI Speech/Transcription hoặc provider mới ngầm từ desktop.

Tiêu chí hoàn tất MVP:

- Clip gần im lặng tiếp tục bị chặn như dữ liệu chẩn đoán hiện tại.
- Clip chỉ có ambience nhưng thiếu lời không được người dùng duyệt nhầm do UI mô tả mơ hồ.
- Hệ thống không quảng bá loudness check như kiểm tra đúng nguyên văn hoặc lip-sync.

### Task 7 — Cập nhật desktop và WebView UI cho video dài — hoàn tất

Mục tiêu:

- Người dùng nhìn thấy rõ ai sẽ nói trước khi phát sinh chi phí và có hành động retry đúng sau lỗi.

Hạng mục:

- Trên card scene và hộp xác nhận generation, hiển thị rõ:
  - `Nhân vật nói trực tiếp`;
  - `Lời dẫn ngoài khung hình — cảnh không có nhân vật`;
  - `Không có lời nói`.
- Chặn lưu `NativeVoiceOver` khi scene video dài Kling còn gắn character.
- Khi chuyển từ voice-over sang on-camera, tự ánh xạ nội dung đang sửa sang `Dialogue` theo đường lưu hiện hành nhưng vẫn yêu cầu đúng một nhân vật.
- Với `NativeAudioInvalid`, hiển thị nguyên nhân audio và nút/hành động rõ ràng: **Tạo lại với prompt ưu tiên lời thoại**.
- Hộp xác nhận retry phải hiển thị số giây và chi phí ước tính của request mới.
- Không thay đổi màn hình `DirectShortVideo`; màn hình này tiếp tục ghi rõ Native Audio không tự tạo lời thoại.
- Cập nhật đồng thời TypeScript payload, C# bridge contract/handler và busy state nếu có thay đổi message.

Tiêu chí hoàn tất:

- Người dùng không thể nhầm voice-over là nhân vật sẽ nói.
- UI không cho gửi một scene vi phạm policy và server vẫn kiểm tra lại.
- Retry audio lỗi là thao tác tách biệt, có xác nhận chi phí.

### Task 8 — Tương thích dữ liệu cũ và migration — hoàn tất, không cần migration mới

Quyết định mặc định:

- Không migration hoặc sửa hàng loạt `Scene.Dialogue`/`Scene.Narration` cũ.
- Không đổi mode của clip/generation đã hoàn tất.
- Project cũ chỉ áp dụng policy mới khi người dùng sửa/sinh lại scene và tạo request mới.
- Template version/profile retry có thể lưu trong `ProviderRequests.RequestJson` và metadata hiện có; dự kiến không cần cột mới.

Chỉ tạo migration idempotent mới nếu trong lúc triển khai xác định bắt buộc phải lưu một snapshot mới không thể biểu diễn an toàn bằng các trường hiện có. Không sửa migration 4.0.x đã phát hành.

Tiêu chí hoàn tất:

- Dữ liệu lịch sử vẫn đọc/preview được.
- Request mới không replay nhầm request cũ do template version/hash thay đổi.
- Không có script SQL sửa lời thoại hàng loạt.

### Task 9 — Kiểm thử tự động — hoàn tất

#### OpenAI content planner

- Một character + spoken text chỉ chấp nhận `OnCameraDialogue`.
- Không character + spoken text chỉ chấp nhận `NativeVoiceOver`.
- Voice-over có `character_keys` bị từ chối.
- On-camera thiếu/mismatch speaker bị từ chối.
- Visual prompt on-camera được yêu cầu có visible speaking performance nhưng không lặp `spoken_text`.
- `None` giữ đúng hợp đồng rỗng.

#### Persistence desktop

- On-camera lưu lời vào `Scene.Dialogue`, không lưu `Scene.Narration`.
- Voice-over lưu lời vào `Scene.Narration`, không lưu `Scene.Dialogue`.
- Mode và `RequiredCapabilitiesJson` nhất quán.
- Sửa scene không tạo đồng thời Dialogue và Narration.

#### GenerationService và prompt composer

- Speech-first prompt gắn speaker với first-frame character.
- Câu thoại đứng trước identity/assets và được giữ nguyên.
- Có yêu cầu bắt đầu nói sớm, visible mouth/lip-sync và no narrator.
- On-camera prompt không cho silent smile/closed mouth/voice-over conflict.
- Voice-over không chèn on-camera lip-sync.
- Prompt bắt buộc quá dài bị chặn trước resolver/budget/outbound.
- Safe request log chỉ chứa hash/mode/template/retry profile, không chứa full speech.

#### Retry/idempotency/budget

- `NativeAudioInvalid` tạo profile `speech-recovery-v1` khi người dùng xác nhận.
- Retry có request hash/idempotency riêng và reserve riêng đúng một lần.
- Replay cùng key/hash không outbound lần hai.
- Request lỗi preflight không tạo reservation hoặc provider request.

#### Kling client và media

- Payload Kling vẫn gửi `settings.audio = native`, `multi_shot = false`, đúng model/path và first-frame.
- Audio stream thiếu/gần im lặng vẫn là `NativeAudioInvalid`.
- Audio nghe được chỉ đến `AudioReviewRequired`, không tự `Approved`.
- Final render không nhận generation chưa được người dùng duyệt.

#### Regression ngoài phạm vi

- `DirectShortVideo` không bị ép thêm thoại hoặc policy presenter.
- BytePlus/Seedance không bị thay đổi prompt/mode ngoài quyết định hiện hành.
- Gateway authorization, organization ownership, Viewer, rate, budget và credential lifecycle vẫn đạt.
- Không có OpenAI/Kling thật trong unit/integration test tự động.

### Task 10 — Cập nhật tài liệu và runbook — hoàn tất source

Sau khi source được triển khai, cập nhật đồng thời:

- `README.md`;
- `NGHIEP_VU_HE_THONG_VIDEOMAKER.md`;
- `NGHIEP_VU_SINH_VIDEO_VA_DONG_BO_NHAN_VAT.md`;
- `KE_HOACH_SERVER_AI_GATEWAY.md`;
- `TRIEN_KHAI_AI_GATEWAY_TO_CHUC.md`;
- tài liệu kế hoạch này: đổi trạng thái từng task và ghi mốc xác minh thực tế.

Runbook smoke test phải bổ sung kiểm tra on-camera dialogue, retry speech recovery và đối chiếu usage có phí.

## 5.1. Kết quả triển khai thực tế ngày 2026-09-01

- Thêm policy server `KlingLongFormSpeechPolicy` và validator desktop `KlingLongFormSpeechIntentValidator`; cả hai chỉ bật cho `OpenAiStructuredPlan + Kling`.
- OpenAI contract/output validator khóa quan hệ presenter/on-camera và B-roll/voice-over; on-camera visual prompt phải có hành động nói cùng mặt/miệng nhìn thấy được.
- Video dài Kling dùng `kling-native-audio-v4-vietnamese-speech-first`; speech/performance đứng trước identity và continuity asset, giữ nguyên câu thoại tiếng Việt trong JSON string và không ghi full speech vào request log. Template v3 được giữ cho các đường gọi không thuộc policy tiếng Việt.
- Generic video endpoint và endpoint Kling tương thích đều preflight speech intent/action trước provider resolver, quote, reservation và outbound.
- Server tự suy ra retry từ `VideoGenerations.Status = NativeAudioInvalid`; replay request cũ dùng profile đã snapshot, attempt mới dùng `speech-recovery-v1` và idempotency key/reservation mới.
- Storyboard chặn voice-over có nhân vật, hiển thị hành động **Tạo lại với prompt ưu tiên lời thoại**, nêu chi phí attempt mới và thêm checklist duyệt đủ câu/đúng người nói/khẩu hình.
- Không thêm DTO công khai, cột hoặc migration SQL; dữ liệu cũ không bị chuyển đổi hàng loạt.
- Release build đạt 0 warning/0 error và 388/388 test đạt. Không có request OpenAI/Kling thật, không chạy migration và không publish release trong đợt triển khai này.
- Smoke test staging có phí tại mục 8 vẫn là điều kiện vận hành mở, chỉ chạy khi có staging organization và phê duyệt chi phí cụ thể.

## 6. Thứ tự triển khai và phụ thuộc

| Thứ tự | Hạng mục | Phụ thuộc | Có thể triển khai song song |
|---:|---|---|---|
| 1 | Task 1 — policy speech intent | Không | Khung test Task 9 |
| 2 | Task 2 — OpenAI contract | Task 1 | Test OpenAI |
| 3 | Task 3 — speech-first composer | Task 1 | Test composer |
| 4 | Task 4 — preflight outbound | Task 1, 3 | Test service |
| 5 | Task 5 — retry recovery | Task 3, 4 | Test idempotency/budget |
| 6 | Task 6 — audio review MVP | Task 5 | Test media |
| 7 | Task 7 — desktop/UI | Task 1, 5, 6 | Test frontend/bridge |
| 8 | Task 8 — compatibility/migration decision | Task 1–7 | Không |
| 9 | Hoàn tất Task 9 — toàn bộ regression | Task 1–8 | Không |
| 10 | Task 10 — tài liệu/build/smoke test | Task 1–9 | Không |

## 7. Kiểm tra kỹ thuật bắt buộc

Sau thay đổi source, chạy từ root:

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Nếu thay đổi frontend:

```powershell
Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
```

Không ghi mốc test mới vào tài liệu nếu chưa thực sự chạy đủ lệnh tương ứng.

## 8. Smoke test có phí trên staging

Chỉ thực hiện khi người dùng chỉ rõ staging organization và phê duyệt chi phí.

Kịch bản tối thiểu:

1. Tạo một project video dài Kling; content/prompt/lời hiệu lực phải là tiếng Việt theo policy hiện hành.
2. Sinh plan 5 cảnh có một presenter xuyên suốt.
3. Xác nhận cả 5 cảnh có lời được hiển thị `Nhân vật nói trực tiếp`, đúng speaker và không có voice-over.
4. Kiểm tra safe request snapshot của từng cảnh có `OnCameraDialogue`, `NativeAudio=true`, template speech-first và không có full speech.
5. Tạo ít nhất một cảnh không yêu cầu cười và một cảnh mỉm cười trong khi nói để xác nhận hành động cười không làm mất lời.
6. Với từng output:
   - có audio stream nghe được;
   - đúng nhân vật mở miệng nói;
   - câu tiếng Việt đủ và hiểu được;
   - không phát narrator ngoài khung hình;
   - khẩu hình chấp nhận được.
7. Nếu có `NativeAudioInvalid`, xác nhận clip bị chặn và retry dùng `speech-recovery-v1` với một request/chi phí mới đã được đồng ý.
8. Đóng desktop trong lúc task chạy để xác nhận worker/polling không đổi hành vi.
9. Chỉ duyệt các clip đã nghe; final render phải giữ Native Audio đã duyệt.
10. Đối chiếu provider request, reservation, usage ledger và dashboard Kling để bảo đảm không double charge.

Chỉ số cần ghi nhận, không ghi full prompt/lời:

- số clip on-camera đã thử;
- số clip có lời đạt ngay attempt đầu;
- số clip `NativeAudioInvalid`;
- số retry và kết quả retry;
- số output có ambience nhưng thiếu giọng;
- provider/model/template/retry profile;
- estimated/actual cost theo request.

## 9. Điều kiện nghiệm thu

1. Policy chỉ tác động workflow video dài Kling; video ngắn và provider khác không đổi hành vi.
2. Content plan mới không còn cảnh một nhân vật có lời nhưng dùng `NativeVoiceOver`.
3. Mọi on-camera prompt gắn rõ câu thoại với người duy nhất trong ảnh first-frame.
4. Speech/performance đứng trước identity/assets và có yêu cầu bắt đầu nói sớm, visible lip-sync, không silent smile/narrator.
5. `settings.audio = native` và `NativeAudio=true` vẫn được bảo đảm bởi snapshot server.
6. Mâu thuẫn speech/visual bị chặn trước quote/reserve/outbound.
7. `NativeAudioInvalid` không thể duyệt và retry dùng profile phục hồi khác prompt ban đầu.
8. Audio nghe được vẫn phải qua người dùng nghe duyệt; không tự động coi ambience là lời đúng.
9. Không log plaintext credential, full prompt, spoken text, Base64 hoặc provider output URL.
10. Request retry/idempotency/budget không tạo submit hoặc ledger trùng.
11. Dữ liệu cũ không bị dịch/sửa hàng loạt và vẫn xem được.
12. Release build không warning/error và toàn bộ test đạt.
13. Live smoke test có phí chỉ được đánh dấu đạt sau khi có phê duyệt môi trường/chi phí và ghi nhận kết quả thật.

## 10. Rủi ro và biện pháp kiểm soát

| Rủi ro | Tác động | Kiểm soát |
|---|---|---|
| Kling vẫn bỏ qua lời dù prompt đúng | Tốn chi phí, clip không dùng được | Speech-first, recovery profile, chặn duyệt và đo tỷ lệ smoke test |
| Prompt identity/assets quá dài làm giảm ưu tiên speech | Nhân vật đúng hình nhưng không nói | Đưa speech lên trước, phân biệt phần bắt buộc/tùy chọn, preflight length |
| Visual prompt mâu thuẫn với speech | Nhân vật đứng/cười/nghe | Output validator OpenAI, preflight conflict và performance contract tự chèn |
| Retry tạo chi phí lặp | Vượt budget | Không auto retry, xác nhận chi phí, idempotency và reserve theo request |
| Loudness pass nhưng chỉ có ambience | Người dùng duyệt nhầm | UI checklist rõ, manual review bắt buộc; VAD/ASR là giai đoạn mở rộng |
| Policy làm hỏng video ngắn/provider khác | Regression ngoài yêu cầu | Gate bằng provider + structure type và test âm tính |
| Sửa dữ liệu cũ ngoài ý muốn | Mất tính truy vết | Không migration hàng loạt; áp dụng cho generation mới |

## 11. Ngoài phạm vi

- Thay Kling bằng Veo hoặc provider khác.
- Bật Kling Omni/Advanced Element hoặc voice binding trong cùng đợt triển khai này.
- Tự động gọi TTS, tạo WAV, chạy lip-sync API hoặc ghép giọng ngoài khi Native Audio lỗi.
- Tích hợp ASR/Whisper/provider transcription có phí nếu chưa có quyết định credential/rate/budget riêng.
- Ép video ngắn `DirectShortVideo` phải có lời thoại.
- Chuyển toàn bộ workflow desktop SQL sang server.
- Tự động sửa, dịch hoặc regenerate toàn bộ project lịch sử.
- Chạy migration production, rotate credential hoặc gọi Kling thật khi chưa được phép.

## 12. Kết quả bàn giao dự kiến

- Policy speech intent video dài Kling có test.
- OpenAI content contract và output validator mới.
- Kling prompt template speech-first có version mới.
- Preflight mâu thuẫn trước outbound.
- Retry profile `speech-recovery-v1` có idempotency/budget đúng.
- UI video dài hiển thị mode và retry rõ ràng.
- Regression test server/desktop/web/media.
- Tài liệu nghiệp vụ/runbook được cập nhật.
- Báo cáo build/test tự động và, nếu được phê duyệt, báo cáo staging smoke test có phí.
