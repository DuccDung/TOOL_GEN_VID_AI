# Trạng thái dự án VideoMaker

> Cập nhật theo source/worktree ngày 2026-09-07. Đây là bản đồ trạng thái, không phải bằng chứng rằng migration hoặc cấu hình đã được áp dụng lên môi trường thật.

## Cách dùng file này

- `Đã có trong source`: code và test liên quan tồn tại trong repository.
- `Mặc định tắt`: code tồn tại nhưng feature flag/provider chưa được bật.
- `Chưa rollout`: chưa xác minh trên database/staging/production thật.
- `Hạng mục mở`: chưa có luồng hoàn chỉnh hoặc chưa đạt gate phát hành.

Khi source mâu thuẫn với file này, source và migration là nguồn sự thật kỹ thuật. Cập nhật file này trong cùng thay đổi làm trạng thái bị thay đổi.

## Trạng thái tổng quan

| Khu vực | Trạng thái |
|---|---|
| Auth, device, session, refresh rotation | Đã có trong source |
| License lease, heartbeat, giới hạn thiết bị | Đã có trong source |
| Organization, membership, RBAC, budget/usage/audit | Đã có trong source |
| Credential theo organization và rotation | Đã có trong source |
| Admin organization/member/budget/provider/pricing | Đã có trong source |
| OpenAI content, image, TTS, transcription | Đã có trong source; TTS/ASR workflow mặc định tắt |
| Kling video | Đã có trong source; provider mặc định khả dụng khi được cấu hình |
| BytePlus/Seedance | Đã có catalog/client/policy; mặc định Disabled, chưa rollout |
| Fal/Veo | Đã có catalog/client/policy/first-frame; mặc định Disabled, chưa paid smoke test |
| Server polling và video output cache/proxy | Đã có trong source |
| Video ngắn | Đã có trong source |
| Video dài nhiều cảnh | Đã có trong source |
| Character reference và project asset library | Đã có trong source |
| Scene first-frame | Đã có trong source; rollout DB/provider còn phải xác minh |
| Canonical Voice và speech verification | Canonical Voice có TTS, kiểm tra kỹ thuật và timeline mix/render; `NativeVoiceOver` có WAV hiện hành đi thẳng sang tạo video, còn `OnCameraDialogue` giữ bước duyệt/chờ lip-sync. Không phụ thuộc ASR. Speech verification vẫn là luồng độc lập cho Provider Native khi được bật |
| SePay license payment | Đã có trong source; mặc định Disabled |
| Organization pool và seat allocation | Đã có trong source; rollout phụ thuộc SePay/cấu hình pool |
| Vietsub project/editor/SRT/timeline/OCR | Đã có trong source |
| Vietsub local contextual translation | Chưa triển khai; mới có job type placeholder |
| Vietsub local STT/voice/export MP4 | Chưa thành luồng hoàn chỉnh |
| Updater/setup/distribution integrity | Đã có trong source |
| FFmpeg release redistribution | Chưa được phê duyệt; development-only |

## Provider catalog hiện hành

| Provider | Model/endpoint chính | Mặc định |
|---|---|---|
| OpenAI | `gpt-5.6-luna`, `gpt-image-2`, `gpt-4o-mini-tts`, `whisper-1` | Provider/model Enabled; vẫn cần credential và rate Active |
| Kling | `kling-3.0` | Enabled; vẫn cần credential/rate/policy |
| BytePlus | Seedance 2.0 và 2.5 | Disabled |
| Fal | Veo 3.1 Standard/Fast Image-to-Video | Disabled |

Bootstrap chỉ tạo catalog/capability. Không seed giá và không tự bật provider/model đang Disabled.

## Feature flag mặc định

Server:

- `Generation:SpeechSynchronization:CanonicalVoiceEnabled = false`.
- `Generation:SpeechSynchronization:SpeechVerificationEnabled = false`.
- `Payments:Sepay:Enabled = false`.

Desktop:

- `Features:VietsubEnabled = true`.
- `Features:VietsubOcrEnabled = true`.
- `Features:SpeechSynchronizationEnabled = false`.

Feature flag desktop không thể vượt qua flag hoặc readiness của server.

## Những gì đã hoàn thiện trong source

### Gateway và quản trị AI

- Access check gắn user, device, license, organization, role và project.
- Credential test trước rotation; payload mã hóa bằng Data Protection.
- Rate theo model/usage type, quote trước thao tác và snapshot vào request.
- Budget organization/member dùng reserve, settle, release và reconciliation.
- Idempotency chống submit hoặc tính phí trùng trong phạm vi organization.
- Admin có setup center cho organization, member, budget, provider, policy, pricing, pool, license và release.

### Content và tài sản

- OpenAI Responses API với structured output.
- Content plan video dài bắt buộc tiếng Việt; plan sai được lưu chi tiết field/reason an toàn.
- Cho phép quote và xác nhận tối đa một lượt content repair, có request/budget riêng.
- Character reference GPT-Image-2, lifecycle duyệt và primary reference.
- Project asset text library có version, lock/unlock, gán và xác nhận theo scene.
- Scene first-frame tách khỏi character reference, có quote/generate/materialize/approve/reject/invalidation.

### Video và audio

- Video provider router dùng Kling, BytePlus hoặc Fal theo policy snapshot của project.
- Policy có scope `Default` và `LongForm`; video ngắn chỉ dùng Default/Kling.
- Worker polling server có claim lease, backoff, giới hạn tuổi/lần thử và settlement terminal.
- Output được server cache rồi trả qua endpoint tương đối có authorization; desktop không thấy URL gốc.
- Desktop tải bằng `.part`, kiểm tra MIME/size/hash, probe, trim và lưu asset.
- Native Audio cần người dùng nghe trước khi duyệt.
- Canonical Voice có voice profile version, preview/playback gate cho phiên bản giọng, TTS WAV, kiểm tra MIME/hash/sample/duration/audibility, duration/timeline guard và render pointer bất biến; endpoint ASR từ chối project Canonical Voice trước pricing/outbound.
- Dashboard tương thích dữ liệu cũ: `NativeVoiceOver` có đúng WAV hiện hành được đưa thẳng về bước tạo video nền, không yêu cầu tạo/duyệt lại TTS và không hiện cảnh báo Native Audio cũ. Lệnh tạo video kiểm tra lại file cục bộ rồi tự chấp nhận VoiceGeneration trước outbound.
- `NativeVoiceOver` dùng toàn bộ WAV đã kiểm tra kỹ thuật, điều chỉnh tempo trong giới hạn rồi pad theo thời lượng cảnh; render hỗ trợ timeline có lẫn scene audio và scene im lặng mà không làm lệch stream concat.
- Render cuối chấp nhận bất kỳ số lượng cảnh đã duyệt nào, tối thiểu một cảnh; manifest giữ đúng thứ tự các cảnh đã duyệt và bỏ qua cảnh chưa duyệt thay vì bắt buộc hoàn tất toàn bộ scene plan.
- Bước xuất video có nút lưu MP4 bằng hộp thoại Windows sau khi FinalVideo hoàn tất. Desktop xác minh SHA-256, sao chép nguyên tử, cập nhật `FinalVideo=Exported` và `Project=Completed`; bản gốc workspace vẫn được dùng cho preview và có thể xuất lại mà không render/gọi AI.
- Render Canonical Voice chấp nhận có giới hạn narrated asset `scene-audio-sync-v2` đã duyệt và còn khớp toàn bộ lineage/hash; asset mới vẫn dùng `v3`, còn phiên bản cũ hơn tiếp tục bị chặn. Compatibility này không gọi lại provider.
- `OnCameraDialogue` với Canonical Voice dừng ở `SpeechReadyForLipSync`; chưa có engine lip-sync.
- `NativeVoiceOver` có thể thay toàn bộ native audio hoặc trộn ambience đã xác minh theo policy.

### Vietsub

- Registry metadata server trong `vs.Projects`, ownership theo organization + user.
- Workspace cục bộ độc lập, manifest versioned, lock, autosave, backup/recovery.
- SQLite WAL cho subtitle track/cue/artifact và local job/step/event.
- Import media COPY/LINK, hash, source mutation detection và FFprobe metadata.
- Virtual media URL, HTTP Range, project-scoped authorization và log runtime đã redaction.
- Timeline windowing, thumbnail theo viewport, waveform, cache version và retry giới hạn.
- Import/edit/filter/split/align/duplicate/delete/export SRT.
- PaddleOCR English/Chinese chạy local, region/profile, frame streaming, dedup, cue accumulation, checkpoint và resume.

### Cài đặt và cập nhật

- Package có manifest managed files.
- Setup kiểm tra size/hash, zip traversal/zip bomb, bundle media và rollback.
- Updater bảo vệ cấu hình/workspace, backup file bị thay, rollback khi lỗi và khởi động lại.
- Distribution kiểm tra đủ năm file FFmpeg và SHA-256/provenance.

## Hạng mục mở ưu tiên

### Trước mọi rollout

1. Chạy toàn bộ migration lặp trên database clone và kiểm tra schema/FK/index/version.
2. Chứng minh backup có thể restore trước khi thay database thật.
3. Chạy Release build, xUnit và web test trên chính commit chuẩn bị phát hành.
4. Dùng staging tách biệt, budget nhỏ và credential/rate test; không dùng production để thử lần đầu.

### AI/video

- Chưa paid smoke test BytePlus/Fal/Veo hiện hành.
- Chưa đối chiếu settlement với dashboard/hợp đồng provider thật.
- Cần benchmark ảnh first-frame Data URI cho Fal trước production.
- Canonical Voice cần migration 4.1.3–4.1.4, TTS rate/model, staging smoke test và rollout flag. Speech verification cho Provider Native cần transcription rate/model và migration 4.1.5 nếu dùng audited `NeedsReview`.
- Chưa có lip-sync cho `OnCameraDialogue` Canonical Voice.
- Dashboard metrics/alert cho worker, budget reconciliation và output cache còn hạn chế.

### Vietsub

- Local contextual translation English/Chinese -> Vietnamese chưa có engine, model provisioning, schema, executor, cache, translation memory hoặc UI hoàn chỉnh.
- Local STT, voice synthesis, music và export MP4 chưa hoàn thiện.
- OCR cần benchmark video dài, memory/CPU và clean-machine packaging smoke test.
- Cần smoke test tương tác đầy đủ timeline/thumbnail/waveform trên bản cài thật.

### Vận hành và phát hành

- SePay và organization seat allocation chưa được bật trên môi trường thật.
- FFmpeg hiện có `Approval scope: Development`; publish script sẽ chặn Release.
- Cần hoàn tất legal/transitive notice review của OCR trong publish output cuối.
- Desktop SQL workflow vẫn là giải pháp chuyển tiếp; mục tiêu dài hạn là đưa workflow mutation qua server API.

## Migration hiện có

- `VideoFactory.Initial.sql`.
- `4.0.0` đến `4.0.11`.
- `4.1.0` Vietsub project registry.
- `4.1.1` Scene first-frame.
- `4.1.2` Provider request failure details/content repair.
- `4.1.3` Speech synchronization.
- `4.1.4` Voice profile approval proof.
- `4.1.5` Audited approval cho speech verification `NeedsReview`.
- `VideoFactory.DesktopLeastPrivilege.sql` áp lại quyền desktop sau cùng.

Migration có trong source không đồng nghĩa đã chạy ở bất kỳ database nào.

## Baseline kiểm thử

- Ngày 2026-09-07, trên worktree chưa commit, đã chạy từ root: `dotnet restore TOOL_GEN_POST_VIDEO.slnx`, Release build 0 warning/error và 803/803 xUnit đạt.
- Frontend cùng ngày: `npm ci`, `npm run build` thành công; 7/7 test file và 40/40 test đạt bằng `npm test`.
- Mốc này chỉ xác minh source/worktree hiện hành; không chứng minh migration, credential, provider trả phí hoặc release production đã được rollout.

Không dùng các mốc nhỏ hơn trong commit/tài liệu lịch sử làm baseline hiện hành. Khi có lần chạy mới, thay đúng mục này bằng ngày, commit, lệnh và kết quả thực tế.

## Quy tắc cập nhật trạng thái

- Chỉ ghi “đã triển khai” khi code và test tương ứng tồn tại.
- Chỉ ghi “đã rollout” khi chỉ rõ môi trường, migration/config đã áp dụng và smoke test đã chạy.
- Chỉ ghi “đã phát hành” khi package, legal/provenance, clean-machine install và rollback đều được nghiệm thu.
- Không tạo thêm file `TASK_*`, `KE_HOACH_*` hoặc ghi nhận lỗi riêng. Cập nhật backlog ở đây; chi tiết kỹ thuật nằm trong issue tracker/commit/PR.
