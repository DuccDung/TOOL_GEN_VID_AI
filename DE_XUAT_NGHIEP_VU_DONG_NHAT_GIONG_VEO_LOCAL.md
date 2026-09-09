# Đề xuất nghiệp vụ đồng nhất giọng Veo bằng xử lý local

> Trạng thái: **đề xuất, chưa triển khai**. Tài liệu này mô tả hướng nghiệp vụ dự kiến và không phải bằng chứng tính năng đã có trong source, đã kiểm thử hoặc đã rollout.

## 1. Bài toán

Video dài được dựng từ nhiều clip Veo ngắn. Mỗi clip có thể được Veo tạo đầy đủ hình ảnh, khẩu hình, lời thoại, nhạc nền và hiệu ứng âm thanh. Khẩu hình trong từng clip thường bám theo chính lời thoại native của clip đó, nhưng chất giọng giữa các clip có thể thay đổi về âm sắc, cao độ, tốc độ, độ vang và âm lượng.

Mục tiêu của nghiệp vụ mới là giữ nguyên hình ảnh và timing lời thoại mà Veo đã tạo, sau đó xử lý local để các cảnh của cùng một nhân vật có chất giọng nhất quán. Hướng này không yêu cầu người dùng phải chọn trước một giọng TTS và không gọi thêm dịch vụ lip-sync trả phí.

## 2. Nguyên tắc giải pháp

- Veo vẫn tạo video với Native Audio và khẩu hình đầy đủ.
- Không tạo lại câu nói bằng TTS vì nhịp phát âm mới có thể làm lệch khẩu hình.
- Dùng **voice conversion** để chỉ thay đổi chất giọng, đồng thời giữ nguyên câu chữ, nhịp nói và thời điểm phát âm của audio gốc.
- `FFmpeg` chỉ đảm nhiệm tách, chuẩn hóa, trộn và remux media. Việc phát hiện lời nói, tách giọng và chuyển đổi chất giọng cần runtime/model local chuyên biệt.
- Video provider gốc và media gốc phải được giữ bất biến để có thể đối chiếu, retry local hoặc quay lại bản native sau một xác nhận rõ ràng của người dùng.
- Không fallback ngầm từ bản đổi giọng sang Native Audio hoặc Fal Lip Sync.

## 3. Thuật ngữ

- **Native clip**: clip Veo có video và audio gốc do provider tạo.
- **Speech window**: khoảng thời gian được VAD xác định có tiếng người nói.
- **Speech stem**: track giọng nói được tách khỏi nhạc, ambience và hiệu ứng.
- **Residual stem**: phần audio còn lại sau khi tách lời nói, gồm ambience, nhạc và hiệu ứng.
- **Voice Anchor**: mẫu giọng chuẩn dùng để đồng nhất các clip của một nhân vật hoặc narrator.
- **Converted clip**: clip giữ nguyên video, nhưng speech stem đã được chuyển về Voice Anchor rồi trộn lại với residual stem.

## 4. Phạm vi MVP

MVP chỉ áp dụng cho:

- project `OpenAiStructuredPlan`;
- video provider Fal/Veo;
- scene `OnCameraDialogue`;
- đúng một nhân vật nói trong mỗi scene;
- clip Veo dài 4, 6 hoặc 8 giây có Native Audio nghe được;
- xử lý voice conversion hoàn toàn trên desktop.

MVP chưa bao gồm:

- nhiều người nói chồng nhau trong một scene;
- tự sửa câu Veo nói sai bằng TTS;
- đổi nội dung lời thoại sau khi Veo đã tạo clip;
- huấn luyện model giọng trực tiếp trong ứng dụng;
- tự động upload voice reference, transcript hoặc media local lên server/provider;
- tuyên bố production-ready khi model thật chưa qua kiểm tra license, checksum, benchmark và nghe nghiệm thu.

## 5. Voice Anchor

### 5.1 Voice Anchor theo nhân vật

Mỗi nhân vật có Voice Anchor riêng. Cảnh thoại native đầu tiên đạt chất lượng có thể được người dùng chọn làm Voice Anchor cho nhân vật đó. Các cảnh sau của cùng nhân vật được chuyển đổi về anchor này.

Nếu video có nhiều nhân vật, mỗi nhân vật phải có anchor độc lập. Không dùng một anchor chung cho tất cả nhân vật.

### 5.2 Trình tự chọn anchor

1. Veo tạo clip có lời thoại và Native Audio.
2. Desktop kiểm tra video/audio về stream, thời lượng, độ nghe được và hash.
3. Local pipeline tách speech stem để người dùng nghe riêng.
4. Người dùng phát và xác nhận chất giọng đủ tốt để làm anchor.
5. Hệ thống snapshot nhân vật, source asset, SHA-256, speech window, engine/model fingerprint và thời điểm duyệt.
6. Anchor chỉ trở thành `Approved` sau xác nhận; mở modal hoặc nghe lại không tự tạo job mới.

Nếu thay Voice Anchor, mọi converted clip phụ thuộc anchor cũ trở thành stale và phải được tạo lại local. Native clip gốc vẫn được giữ nguyên.

### 5.3 Hướng mở rộng

Sau MVP có thể cho phép người dùng nhập hoặc thu một mẫu giọng mà họ có quyền sử dụng. Mẫu này chỉ được lưu local, phải có xác nhận quyền sử dụng và không được tự động gửi lên server.

## 6. Luồng xử lý một scene

```text
Veo tạo native clip có hình + lời + ambience/SFX
                         ↓
Desktop tải qua server proxy, kiểm tra và lưu native asset
                         ↓
FFmpeg tạo working audio an toàn từ native clip
                         ↓
VAD phát hiện speech window
                         ↓
Source separation tạo speech stem + residual stem
                         ↓
Voice conversion đổi speech stem về Voice Anchor
                         ↓
Kiểm tra câu chữ/timing/thời lượng và chất lượng audio
                         ↓
Trộn converted speech với residual stem, crossfade biên lời
                         ↓
FFmpeg remux với video gốc bằng video stream copy
                         ↓
Người dùng phát, so sánh và duyệt converted clip
```

### 6.1 Scene có lời thoại

- Native clip phải có video stream và audio nghe được.
- Scene phải có đúng một speaker/character đã khóa.
- Voice Anchor của character phải `Approved`. Nếu chưa có, scene chuyển sang chờ chọn anchor.
- VAD phải tìm thấy speech window hợp lệ trong biên thời lượng clip.
- Voice conversion không được thay đổi nội dung hoặc làm timing lệch quá ngưỡng nghiệm thu.
- Residual stem được giữ để bảo toàn ambience và hiệu ứng của Veo.
- Converted output phải qua kiểm tra video/audio stream, duration, sample rate, audibility, SHA-256 và lineage trước khi mở duyệt.

### 6.2 Scene không có lời

- Không chạy VAD, source separation hoặc voice conversion.
- Native clip được giữ nguyên và đi theo quy trình duyệt Native Audio hiện hành.

### 6.3 Scene voice-over

`NativeVoiceOver` chưa nằm trong MVP. Khi mở rộng, narrator dùng Voice Anchor cấp project thay vì Voice Anchor của character.

## 7. Phát hiện và kiểm tra lời nói

- VAD local là bước bắt buộc để xác định clip có speech và biên lời nói.
- Local ASR/Whisper là bước kiểm tra tùy chọn để so sánh lời Veo nói với nội dung scene; ASR không được dùng để tự sửa hoặc tự gọi provider lần hai.
- Nếu Veo nói sai, thiếu hoặc thêm nội dung vượt ngưỡng, scene chuyển `NeedsReview` hoặc `Failed` theo policy. Người dùng có thể chấp nhận ngoại lệ có lý do hoặc chủ động tạo lại Veo với một request có phí mới.
- Không log transcript đầy đủ hoặc đường dẫn local nhạy cảm.

## 8. Trạng thái nghiệp vụ dự kiến

Một local voice-conversion job có vòng đời tối thiểu:

```text
NotRequired
AnchorRequired
Ready
Preparing
DetectingSpeech
SeparatingAudio
ConvertingVoice
Mixing
Validating
ReviewRequired
Approved
Failed
Cancelled
Stale
```

Các nguyên tắc chuyển trạng thái:

- Chỉ scene hiện hành, đúng project/organization/owner và đúng generation mới được tạo job.
- Retry local dùng lại native clip đã tải; không submit lại Veo.
- Sửa scene, đổi nhân vật, đổi native generation, đổi anchor hoặc đổi engine/model fingerprint làm output cũ `Stale`.
- `Failed` và `Cancelled` không được coi là output có thể render.
- Chỉ converted clip `Approved/current` mới được dùng cho render cuối của policy này.

## 9. Lineage và dữ liệu cần snapshot

Mỗi converted output phải truy vết tối thiểu:

- organization, owner, project, scene và character;
- scene plan/prompt version;
- provider request và `VideoGenerationId` của native clip;
- native video asset ID, SHA-256 và duration;
- Voice Anchor ID, source asset hash và approval version;
- VAD/source-separation/voice-conversion engine, model, config và fingerprint;
- speech windows và giới hạn padding/crossfade;
- speech/residual/converted hash;
- loudness, duration drift và kết quả validation;
- output media asset ID, SHA-256, reviewer và thời điểm duyệt.

Media và voice reference nằm local. Server không nhận absolute local path, voice sample, stem hoặc converted audio.

## 10. Trộn audio và remux

- Working audio phải dùng định dạng PCM/sample rate thống nhất trong pipeline.
- Speech stem sau conversion phải giữ silence và vị trí tương ứng với native timeline.
- Chỉ cho phép time correction nhỏ trong ngưỡng cấu hình; không nén/kéo dài mạnh để che lỗi model.
- Dùng fade/crossfade ngắn ở biên speech window để tránh tiếng bật hoặc cắt cụt hơi thở.
- Residual stem có thể được duck nhẹ trong lúc nói nhưng không làm mất hiệu ứng quan trọng.
- Chuẩn hóa loudness giữa các scene trước khi ghép video dài.
- Khi tạo converted clip, ưu tiên `-c:v copy`; chỉ encode audio mới rồi ghi qua `.part`, kiểm tra xong mới promote atomically.

## 11. Duyệt và render

UI scene cần cho phép:

- nghe/xem Native clip;
- nghe speech stem hoặc Voice Anchor;
- xem converted clip;
- chuyển nhanh giữa bản native và bản converted để so sánh;
- duyệt, từ chối hoặc retry local;
- chọn một scene đủ chất lượng làm Voice Anchor;
- hiển thị lý do rõ ràng khi thiếu runtime/model/anchor hoặc conversion thất bại.

Render cuối phải dùng:

- converted clip `Approved/current` đối với scene thuộc policy đồng nhất giọng;
- native clip đã duyệt đối với scene không có lời;
- đúng thứ tự scene plan và lineage/hash đã snapshot.

Không được tự dùng native clip thay converted clip chỉ vì local worker lỗi. Việc dùng bản native phải là lựa chọn có xác nhận của người dùng và được ghi nhận trong metadata/audit phù hợp.

## 12. Chi phí và retry

- Veo Native Audio vẫn là request cloud có phí và phải đi qua auth, role, pricing, budget, reservation, idempotency và settlement hiện hành.
- VAD, source separation, voice conversion, mix và remux chạy local nên không tạo request Fal Lip Sync hoặc reservation cloud mới.
- Retry các bước local không được submit Veo lần hai.
- Chỉ hành động **Tạo lại bằng Veo** mới phát sinh provider request và chi phí mới; UI phải hiển thị quote/xác nhận theo policy hiện hành.
- Hướng này tiết kiệm phí lip-sync cloud nhưng sử dụng CPU/GPU, RAM, disk và thời gian xử lý trên desktop.

## 13. Bảo mật, quyền riêng tư và pháp lý

- Chỉ dùng giọng mà người dùng có quyền sử dụng hoặc đã được chủ thể đồng ý.
- Voice sample, speech stem, transcript và converted audio phải nằm trong workspace local được bảo vệ bằng path normalization và hash.
- Worker không nhận URL/model/path tùy ý từ WebView hoặc project manifest.
- Component phải được tải từ HTTPS host allowlist, pin version/size/SHA-256 và chỉ báo `READY` sau checksum/probe.
- Không log voice sample, transcript nhạy cảm, Base64, absolute path hoặc raw worker payload chứa dữ liệu audio.
- Runtime/model phải có provenance, license và attribution đầy đủ trước khi đóng gói phát hành.

## 14. Xử lý lỗi

- Không có audio hoặc audio không nghe được: chặn trước VAD và yêu cầu tạo lại/duyệt hướng xử lý.
- Không tìm thấy speech: trả `speech_not_detected`, không tạo converted output giả.
- Nhiều speaker hoặc lời chồng nhau: trả `multiple_speakers_not_supported` trong MVP.
- Source separation chất lượng thấp: chuyển `NeedsReview`, không tự trộn output kém.
- Voice Anchor quá ngắn/nhiễu: yêu cầu chọn scene anchor khác.
- Timing drift vượt ngưỡng: fail conversion; không tự time-stretch mạnh.
- Worker crash/timeout/OOM: giữ native asset, checkpoint an toàn và cho retry local sau khi người dùng xử lý tài nguyên.
- File/hash/fingerprint stale: hủy apply và yêu cầu chạy lại từ source hiện hành.

## 15. Tiêu chí nghiệm thu MVP

- Một nhân vật xuất hiện trong nhiều clip có chất giọng nhất quán hơn rõ rệt so với Native Audio độc lập.
- Nội dung lời nói và timing được giữ; khẩu hình không lệch nhận thấy do bước conversion.
- Ambience/SFX không bị mất và không còn speech native nghe rõ chồng lên speech converted.
- Không có click/pop tại biên lời, không cắt đầu/cuối câu và loudness giữa scene không nhảy bất thường.
- Cảnh không lời không chạy model không cần thiết.
- Retry local không tạo provider request hoặc chi phí cloud mới.
- Đổi anchor/generation/model làm đúng output phụ thuộc trở thành stale.
- Render chỉ nhận asset Approved/current đúng hash và lineage.
- Fake-worker/unit/integration FFmpeg đạt; model thật, benchmark tài nguyên và nghe nghiệm thu được báo riêng, không tính `Skipped` là đạt.

## 16. Trình tự triển khai dự kiến

1. Ổn định baseline hiện tại và xử lý phần Fal Lip Sync dang dở mà không làm mất thay đổi đã có.
2. Chốt engine/model VAD, source separation và voice conversion sau khi rà soát license/provenance.
3. Bổ sung contract/policy/state/lineage và migration idempotent mới sau chuỗi migration hiện hành.
4. Tạo local component store, readiness probe và worker cô lập.
5. Xây pipeline FFmpeg → VAD → separation → conversion → mix → validate → atomic promote.
6. Nối project/scene orchestration, invalidation và render selection.
7. Cập nhật TypeScript/C# WebView bridge, UI progress/review/anchor/settings.
8. Thêm regression, security, path, worker, FFmpeg và migration tests.
9. Chạy full build/test, verify model, benchmark, nghe nghiệm thu và desktop smoke trên đúng bundle phát hành.

## 17. Quyết định còn mở trước khi code

- Engine/model local nào đáp ứng tiếng Việt, timing, CPU/GPU và giấy phép phân phối.
- Ngưỡng tối thiểu về độ dài/chất lượng Voice Anchor.
- Ngưỡng timing drift, source-separation quality và điều kiện `NeedsReview`.
- MVP chỉ dùng anchor lấy từ Veo hay cho phép voice reference do người dùng cung cấp ngay từ đầu.
- Chính sách lưu/cleanup speech stem, residual stem và converted intermediate.
- Fal Lip Sync được giữ làm fallback có xác nhận hay chỉ giữ như workflow độc lập.
