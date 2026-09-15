# Kiểm thử và nghiệm thu VideoMaker

> Ma trận kiểm thử và Definition of Done. Rà soát ngày 2026-09-07.

Kết quả phải ghi rõ thời điểm, commit/worktree, môi trường và số Passed/Failed/Skipped. Không dùng mốc lịch sử như kết quả của lần thay đổi mới.

## 1. Bộ lệnh chuẩn

Sau thay đổi source, chạy từ root repository:

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Build `TOOL-LOCAL` chạy production build React; nếu thiếu `node_modules` sẽ cài dependency theo lockfile.

Khi chỉ sửa web, có thể chạy vòng nhanh trước full suite:

```powershell
Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
npm test -- --run
```

Không dùng `npm install` để âm thầm đổi lockfile trong một thay đổi không liên quan dependency.

## 2. Chọn phạm vi kiểm thử

| Loại thay đổi | Kiểm tra tối thiểu bổ sung |
|---|---|
| DTO/contract public | Build toàn solution; test serialization/validation; server và desktop consumer |
| Auth/license/device/role | Negative matrix JWT/session/device/license/membership/role/ownership; revoke/expiry |
| AI generation | Idempotency, pricing missing, budget reservation/settlement/release, provider errors, worker retry và output proxy |
| Credential | Permission matrix, encrypt/decrypt, rotate version, redaction và task dùng version cũ |
| Migration | Static migration tests, apply trên clone, chạy lại idempotency, query verify và restore rehearsal |
| WebView bridge | TypeScript/C# contract, invalid message, busy/cancel/reconnect, organization/project switching |
| Media/download/render | Path traversal, `.part`, MIME/signature/size/hash, FFprobe, ApprovedGenerationId và FFmpeg integration |
| Canonical Voice/speech | Feature flag, voice catalog/alias/preview context, pacing, TTS/ASR cost gate, WAV validation, approval/lineage, mix/render và audited review |
| Vietsub/OCR | Manifest/SQLite/revision/lock, path safety, cue/source/language, cancel/retry và atomic SRT |
| Translation runtime | Worker safety/protocol/readiness tests; model integration và benchmark opt-in; desktop smoke |
| Local voice | Phrase/cache/revision, worker protocol, WAV/hash/path, fit 1.20x, FFmpeg timeline, playback authorization; runtime/model thật và nghe smoke là opt-in |
| SePay/seat | Options validation, duplicate/concurrent webhook, matching, expiry, capacity, idempotency và relational tests |
| Updater/setup | Manifest/hash, managed files, install/update/rollback trên VM hoặc máy sạch |

## 3. Kiểm thử bảo mật bắt buộc

### Access control

- token thiếu/sai/hết hạn;
- session/device/license bị revoke hoặc mismatch;
- user không thuộc organization hoặc membership inactive;
- `Viewer` gọi endpoint phát sinh phí;
- role không đủ quyền quản trị member/budget/credential/Owner;
- project khác organization hoặc khác owner;
- Owner Active cuối cùng bị vô hiệu hóa.

### Chi phí và cạnh tranh

- budget bằng `0`, không đủ budget và member limit;
- thiếu/ngoài thời gian rate;
- cùng idempotency key/cùng payload và khác payload;
- submit song song, worker claim hết hạn và retry sau crash;
- provider success/failure/timeout/cancel;
- settlement không vượt/không lặp và release đúng một lần.

### Secret và outbound

- API không trả plaintext/encrypted credential;
- log không chứa Authorization, secret, signed URL, Base64 hay prompt nhạy cảm đầy đủ;
- chặn HTTP, host/port ngoài allowlist, private/reserved IP, DNS rebinding và redirect sai;
- output sai MIME, quá size, hash/signature/media invalid;
- desktop/translation worker không có provider client hoặc đường gọi Cloud ngoài thiết kế.

### Local filesystem

- absolute path, `..`, alternate separator, symlink/reparse escape;
- file `.part` bị gián đoạn và restart;
- stale revision, locked cue và atomic replace;
- updater archive traversal, unexpected managed file và checksum mismatch.

## 4. Database và migration

Test đọc nội dung migration không thay thế apply thật. Với mỗi migration mới:

1. Tạo clone từ schema/version thấp nhất còn hỗ trợ.
2. Apply tuần tự với `sqlcmd -b -f 65001`.
3. Chạy lại migration để kiểm tra idempotency nếu thiết kế cho phép.
4. Xác minh constraint/index/FK/default/backfill và dữ liệu legacy.
5. Chạy server/test relational trên clone.
6. Test `VideoFactory.DesktopLeastPrivilege.sql` bằng account desktop thật: đường cần thiết được phép, `auth`/`ai`/`vs` và secret/ledger bị chặn.
7. Thử restore backup và ghi thời gian/điểm mất dữ liệu chấp nhận được.

Với migration 4.1.2–4.1.5, kiểm tra thêm failure details không lộ nội dung nhạy cảm, backfill speech policy, voice profile approval proof, index/FK mới và constraint `NeedsReview` chỉ cho phép accept khi có lý do/reviewer/timestamp.

Không apply migration vào database thật chỉ để hoàn thành checklist test.

## 5. Provider smoke test

Unit/integration mặc định không gửi request có phí. Smoke thật chỉ chạy khi có organization/project test, rate/credential được duyệt, budget nhỏ hữu hạn và người dùng cho phép chi phí.

### Chung

- Request snapshot đúng organization/user/project/model/rate/credential version.
- Đóng desktop sau submit; worker vẫn poll và reconnect thấy cùng request.
- Output chỉ qua relative proxy, tải/cache/verify thành công.
- Usage/reservation/settlement khớp và không trùng khi retry.
- Log/telemetry đã redaction.

### Kling

- LongForm Native Audio với clip ngắn nhất được phép.
- DirectShortVideo không gọi OpenAI rewrite.
- Approve/reject và một case audio invalid bằng fixture hoặc smoke được duyệt.

### BytePlus

- Chỉ chạy sau khi model được bật riêng cho organization test.
- Xác minh mapping duration/resolution/status/output host; không áp quy tắc Fal first frame.

### Fal/Veo

- LongForm có first frame Approved/current đúng tỷ lệ.
- Chặn missing/stale/wrong-ratio/identity-square input.
- Xác minh không fallback Text-to-Video.

### Canonical Voice và speech verification

- Mở/chọn voice modal không tạo provider request hoặc reservation; catalog 13 giọng và hai alias legacy đúng server contract.
- Preview dùng đúng context project, gồm project kỹ thuật ẩn khi chưa có project nội dung; replay WAV đã tải không tạo phí lần hai.
- Content pacing mục tiêu 85–95%, validator 80–105% và repair luôn qua quote/xác nhận/idempotency riêng.
- TTS fail closed khi thiếu model/credential/rate/budget; WAV sai MIME/hash/sample/duration/audibility bị chặn.
- Canonical Voice không gọi transcription; video dài Provider Native cũng không quote/gọi ASR và không cần `SpeechVerificationReport`.
- `NativeVoiceOver` dùng đúng WAV/speech/voice snapshot và asset sync v3; compatibility v2 chỉ cho exact approved lineage.
- `OnCameraDialogue` dừng ở `SpeechReadyForLipSync`; render không giả định đã lip-sync.
- Nếu bật speech verification ngoài video dài: `Passed`, audited `NeedsReview`, stale version và `Failed` phải đúng policy trước outbound/render.

Kết quả một provider không đại diện cho provider khác.

## 6. SePay và organization provisioning

Test service/relational trước, sau đó simulator trên local/staging:

```powershell
.\scripts\Test-SepayOrganizationProvisioning.ps1 `
  -BaseUrl "https://localhost:7202/" `
  -LicensePlanId "<test-plan-guid>" `
  -Confirmation SEPAY_TEST_ONLY
```

Token test được truyền qua environment variable mà script quy định, không ghi vào command/document. Các case bắt buộc:

- payment order/QR/expiry hợp lệ;
- wrong amount, content, account hoặc transaction status;
- duplicate và concurrent webhook;
- webhook sau expiry;
- tổ chức còn/hết seat, nhiều tổ chức theo priority;
- provisioning bị lỗi rồi retry;
- cùng transaction không cấp license/seat hai lần;
- payment/organization/license có thể đối soát.

Không dùng `-AllowRemote` nếu endpoint chưa được xác minh là staging an toàn.

## 7. Vietsub, OCR và dịch local

### Test không cần model thật

- Worker project không tham chiếu HTTP/Cloud/database workflow.
- IPC chỉ chấp nhận protocol/message/path hợp lệ.
- Readiness fingerprint thay đổi khi model/worker/config/backend/native runtime thay đổi.
- CTA chỉ tạo job cho active `PADDLE_OCR_LOCAL` track `en`/`zh` có cue.
- Manual/locked cue được giữ; stale/invalid output không apply.
- Cancel/retry không tạo hai apply; SRT ghi atomically.
- Resource warning/timeout/crash không làm desktop treo hoặc báo Ready sai. RAM/commit thấp hay snapshot không đọc được phải yêu cầu xác nhận trước khi tạo job/spawn worker; sau xác nhận worker được phép thử chạy.
- Boundary test phải phủ Standard, Low-memory, cảnh báo chưa xác nhận, cảnh báo đã xác nhận và hard blocker. READY/job/cache phải giữ đúng resource profile; worker phải từ chối profile/ngưỡng ngoài allowlist, còn platform/disk/model/probe/OOM thực tế không được bỏ qua bằng cờ xác nhận.

### Xác minh model thật

Model được pin tại component root và script tự kiểm tra size/SHA-256. Chạy:

```powershell
.\scripts\Verify-VietsubTranslationModel.ps1 `
  -ComponentRoot "<approved-component-root>" `
  -Configuration Release
```

Script bật riêng category `LocalModelIntegration`. Nếu model thiếu/sai hash hoặc test fail thì không tạo/nhận marker Ready.

### Benchmark

```powershell
.\scripts\Benchmark-VietsubTranslationWorker.ps1 `
  -ComponentRoot "<approved-component-root>" `
  -Configuration Release
```

Script chạy các profile context/token/batch/thread được định nghĩa trong source với category `LocalModelBenchmark`. Báo tối thiểu:

- CPU/RAM/máy và thời gian warm-up;
- latency, throughput và peak working set theo profile;
- chất lượng đầu ra cho bộ cue Anh/Trung đại diện;
- timeout/cancel/restart và hành vi khi thiếu RAM;
- profile được chọn cùng lý do.
- kết quả riêng cho Standard và Low-memory, gồm safety margin so với peak working set/commit thực đo.

`Skipped` của hai category opt-in là bình thường trong full suite nhưng không phải kết quả đạt.

### Smoke desktop

Trên đúng bundle x64 định phát hành:

1. Tạo/mở project Vietsub và đồng bộ registry.
2. Import media, quét OCR English/Chinese.
3. Bấm **Dịch tiếng Việt**, quan sát prepare/ready/progress/cancel/retry.
4. Sửa/khóa một cue rồi dịch lại để xác minh không ghi đè.
5. Đóng/mở desktop và kiểm tra resume/state/revision.
6. Xuất SRT và kiểm tra timestamp/encoding/nội dung.
7. Xác minh worker không mở network/provider/database workflow.

Chỉ sau các bước này mới xem xét bật `VietsubLocalTranslationEnabled` cho môi trường cụ thể.

### Tạo giọng local Piper

`VietsubVoiceBoundaryTests` tái hiện WAV 1834 ms, sau trim đầu còn 1832 ms nhưng chỉ có cửa sổ 1500 ms: ở 1.20x sẽ tràn 27 ms; thêm trường hợp tràn 135 ms như lỗi người dùng báo. Kiểm cả đuôi dưới ngưỡng tín hiệu, tiếng nói còn ở cuối, tắt phân tích khoảng lặng, cue tiếp giáp/chồng nhau/nằm trong vùng bỏ qua và nhiều câu bỏ qua liên tiếp. FFmpeg thật phải giữ đúng thời lượng, khoảng im lặng đầu và tín hiệu cuối, kể cả phần tiếng nói tràn sang câu bỏ qua. Kiểm tạo mới, cache, bật lại, hash phrase không đổi và job hoàn thành 100% với diagnostic không chặn. Test xuất MP4 dùng bộ dựng tham số production, giải mã lại audio để kiểm phần giọng tràn còn nghe được và âm gốc khi bật/tắt nền. WAV tone và MP4 fixture không thay thế nghe nghiệm thu Piper.

Hồi quy playback phải kiểm tra mở lại video khi URL giọng không đổi; giọng nạp muộn, phát hết rồi tua lại, video buffering, mute/unmute và tải lại WAV lỗi. Preview và modal dùng chung bộ đồng bộ theo clock video; lệnh chờ `AudioContext.resume()` không được chặn video hoặc phát giọng trở lại sau pause. React replay effect không được đóng context vẫn còn gắn với audio element. Registry phải thay entry nguyên tử khi refresh, đồng thời từ chối artifact/revision/hash cũ. `VietsubVoicePlaybackIntegrationTests` có fixture Vite trong `TOOL-LOCAL/Web/tests/voice-playback-browser`, build vào thư mục tạm và chạy hook production trong WebView2 với WAV tone; kiểm tín hiệu thật qua analyser sau mở, mute/unmute, seek, replay và remount. Cần chạy `npm ci` trước .NET test theo quy trình frontend; fixture không gọi model/provider.

Hồi quy hiển thị track Giọng Việt: `VietsubTimelineLayoutIntegrationTests` dựng component timeline cùng CSS production trong WebView2 từ fixture `TOOL-LOCAL/Web/tests/timeline-layout-browser`. Ở zoom 100%, 125%, 150% và 200%, nhãn `Không tạo giọng` phải nằm giữa card, đủ khoảng trống trên/dưới để không cắt chân chữ; waveform phải có chiều cao và các cột sóng nằm ngang, không bị CSS của nhãn ghi đè. Kiểm thêm card bỏ qua ngắn. Có thể đặt `VIDEOMAKER_TIMELINE_LAYOUT_ARTIFACTS` để lưu ảnh từng mức zoom; fixture dùng dữ liệu mẫu, không mở project thật hoặc gọi model/provider.

Kiểm tra lựa chọn giọng theo câu: nâng SQLite 5 → 6 giữ dữ liệu và mặc định bật; mở lại giữ lựa chọn; batch cập nhật nguyên tử, chống revision cũ/cue khác track/job đang chạy; split/duplicate kế thừa trạng thái. Câu bỏ qua vẫn còn phụ đề nhưng không đi vào Piper, không ghép phrase qua khoảng bỏ qua; phần giọng dài của câu trước được phép tràn vào khoảng này. Cache chứa cả câu bật lẫn câu bỏ qua phải bị từ chối; bật lại có thể phục hồi cache tương thích. Bỏ qua hết cho phép xuất âm gốc, còn bật câu mà timeline đã cũ phải chặn xuất giọng cũ. Kiểm tra batch ở tầng API/storage; UI chỉ giữ thao tác từng câu trong menu Thao tác và menu chuột phải, không còn thanh chọn trang hoặc checkbox. Tín hiệu im lặng không được làm hạ âm gốc trong preview. Test fake audio không thay thế nghe thử Piper và MP4 thật.

Test không cần model thật phải phủ phrase boundary/fingerprint, migration SQLite, cache theo revision, RIFF/PCM và silence trim, ba nhánh fit tự nhiên/mượn khoảng trống/tăng tốc, publish timeline kèm diagnostic không chặn khi vượt `1.20x`, FFmpeg failure/cancel, cùng playback sai project hoặc sai hash. Không được coi fake synthesizer là bằng chứng chất lượng giọng.

Sau khi đã cài component qua UI vào workspace được duyệt, chạy verify model/worker thật:

```powershell
.\scripts\Verify-VietsubVoiceModel.ps1 `
  -WorkspaceRoot "<approved-workspace-root>" `
  -Configuration Release
```

Script kiểm tra size/SHA-256 model và config trước khi bật riêng category `LocalVoiceIntegration`. Category này phải tạo được nhiều WAV tiếng Việt bằng các index không liên tục, qua đúng worker/runtime và RIFF/PCM validator; khi không bật opt-in, kết quả `Skipped` không phải là đạt.

Trên đúng bundle x64 định phát hành, xác nhận `VietsubLocalVoiceEnabled` đang bật, cài runtime qua UI rồi xác minh model/config đúng checksum. Tạo giọng từ track mà toàn bộ cue đã có nội dung dịch tiếng Việt, gồm cả fixture có trạng thái cảnh báo/chất lượng chưa duyệt để xác minh các trạng thái này không chặn nghiệp vụ; xác minh output xuất hiện ở track **Giọng Việt** dưới phụ đề và đồng bộ play/pause/seek/tốc độ với video mà không còn audio player độc lập trong panel thiết lập. Nghe câu ngắn/dài/dấu câu/tên riêng, thử cancel/retry và khởi động lại để kiểm tra cache/checkpoint. Nới mép phải một cue để xác minh voice được chuyển tiếp sang revision mới; đổi `start`/rút ngắn hoặc tạo chênh nhiều revision để xác minh timeline được dựng lại từ phrase cache mà không chạy Piper. Đổi nội dung/speaker/cấu trúc cue phải làm cache không tương thích mất hiệu lực. Cuối cùng kiểm tra CPU/RAM/disk/thời gian và worker không có provider/database/network ngoài giai đoạn component store tải các URL đã pin.

`VietsubLocalVoiceEnabled` được bật mặc định để người dùng luôn thấy trạng thái giọng local. Việc bật flag không đồng nghĩa runtime/model đã sẵn sàng: máy thiếu component phải trả `NOT_INSTALLED`, yêu cầu người dùng chủ động xác nhận cài và chỉ được trả `READY` sau khi model/config/worker đúng checksum và probe đạt. Trước khi tuyên bố production-ready vẫn phải kiểm kê đầy đủ license và dependency Python, verify model thật, benchmark, nghe nghiệm thu và smoke desktop; test unit dùng fake worker hoặc model test bị `Skipped` không thay thế các cổng này.

## 8. FFmpeg, distribution và updater

Kiểm tra bundle development:

```powershell
.\scripts\Test-FfmpegBundle.ps1 -BundlePath ".\third_party\ffmpeg\win-x64"
```

Release bắt buộc thêm `-RequireReleaseApproval`. Kiểm tra version ffmpeg/ffprobe khớp, license/provenance đầy đủ, checksum đúng và binary chạy được trên win-x64.

Sau publish:

- giải nén/cài trên VM sạch;
- kiểm tra manifest và toàn bộ managed files;
- khởi động, login, workspace, download/preview/render;
- update từ version đang hỗ trợ;
- mô phỏng download hỏng/hash sai/process crash;
- rollback và xác minh user workspace không mất.

## 9. Nghiệm thu UI thủ công

- Desktop ở DPI/scale phổ biến, cửa sổ nhỏ/lớn và thao tác bàn phím.
- Panel Vietsub **Thiết lập dự án** chỉ hiển thị ba tác vụ chính, không lặp card video/track hoặc thông tin runtime đã sẵn sàng. Popup OCR phải hiển thị đúng video hiện tại, cho phép phát/tua đến timestamp cần kiểm tra, hỗ trợ kéo/đổi kích thước vùng subtitle cứng và chỉ chạy sau khi lưu cấu hình. Quét thử phải nhận timestamp vừa tua. Popup dịch phải có Local và Cloud; Cloud khả dụng theo readiness server, độc lập với model/RAM Local. Bấm **Dịch Cloud** một lần phải lưu draft rồi bắt đầu, không có bước chọn model/provider/key. CTA **Dịch tiếng Việt** luôn hiện; cả hai phương thức vẫn cần active OCR track hợp lệ.
- Cuối panel **Thiết lập dự án** phải có hướng dẫn tiến độ gọn theo đúng thứ tự **OCR → Dịch → Giọng**. Trạng thái, phần trăm và bước tiếp theo phải lấy từ active track/job/timeline giọng hiện hành; bước đang chạy có chuyển động nhẹ, bước lỗi/tạm dừng phải có màu và nội dung riêng, đồng thời tôn trọng `prefers-reduced-motion`.
- Popup dịch phải phân biệt **chưa cài model** với **model đã có nhưng engine cần probe/kiểm tra lại**. Khi mở cảnh báo tài nguyên, popup chọn phương thức phải đóng trước để không chồng nhiều lớp overlay; cảnh báo RAM không được biến thành nhãn “Cần cài đặt”.
- Card xem trước trong editor Vietsub phải ưu tiên diện tích video và control phát: không lặp tên file/badge kỹ thuật ở đầu card, không để metadata bị cắt ở đáy, và bộ chọn tốc độ phải có trạng thái focus rõ ràng nhưng không chiếm quá nhiều chiều ngang.
- Card **Biên tập phụ đề** phải dùng thuật ngữ dễ hiểu, gom Nhập/Xuất SRT và thao tác thời gian vào menu phụ, chỉ mở rộng câu đang chọn/đang phát, hiển thị rõ `Chưa lưu`/`Đang lưu`/`Đã lưu`/`Lưu thất bại`, không làm mất bản nháp khi đổi lọc/trang/nguồn, tự đưa câu được chọn từ timeline vào vùng nhìn thấy và chỉ hiện phân trang khi thực sự có nhiều hơn một trang. Panel hẹp phải co theo chiều rộng card thay vì chiều rộng toàn cửa sổ.
- Modal **Thiết kế thành phẩm** phải giữ đúng tỷ lệ video dọc/ngang, phát/tua không giật do reload nguồn, cho ẩn/hiện và vừa khung/lấp đầy/thu phóng. Kiểm tra lật hình trái–phải, trên–dưới, cả hai hoặc không lật trong FIT/FILL/zoom; chỉ hình video lật, chữ phụ đề và âm thanh không bị đảo. Kiểm tra kéo phụ đề, phím mũi tên `0,5%`, `Shift + mũi tên` `2%`, preset, font allowlist, cảnh báo tương phản và xác nhận bỏ bản nháp lật hình. Tab **Âm thanh** phải chỉnh riêng âm gốc/giọng Việt, mute từng kênh, nghe thử đồng bộ khi seek/play và chỉ auto-duck âm gốc trong đoạn có giọng Việt; mức giọng `>100%` phải nghe được trong preview. Sau khi lưu, đóng/mở lại phải giữ đúng style/mixer/lật hình; project schema 6 mở lại phải mặc định không lật và lưu thành schema 7. Xuất MP4 vào đường dẫn mới, xác minh không còn `.partial`, FFprobe đọc đúng trạng thái audio, burn-in khớp preview và mức âm lượng/duck/limiter không clipping trên ít nhất một video `16:9` và một video `9:16`; sửa cue/style/mixer/lật hình hoặc timeline giọng trong lúc xuất phải fail closed và không publish output stale.
- Accordion, mục **Chi tiết kỹ thuật**, tùy chọn vùng OCR nâng cao và thanh hành động cố định phải dùng được bằng bàn phím, không tràn hoặc che nút ở panel hẹp.
- Organization/project switch không giữ state/busy của context cũ.
- Lỗi auth/budget/pricing/provider/media hiển thị có hành động khắc phục, không lộ chi tiết nhạy cảm.
- Approve/reject/retry có xác nhận phù hợp với chi phí.
- Admin desktop/mobile widths: navigation, table/form/modal, Owner protection, credential hint và loading/error state.
- WebView refresh/reconnect không gửi lặp operation.

Hồi quy biên tập phụ đề (2026-09-09): `VietsubSubtitleEditorBrowserTests` build fixture `TOOL-LOCAL/Web/tests/subtitle-editor-browser` bằng component/CSS production, đo trang 7/50 câu và tập 120 câu phân trang ở panel 320/420 px, zoom 100/125/150/200%. Fixture ghi số lệnh cuộn, đích cuộn, render/remount, độ lệch cuộn tay và focus; CDP đo LayoutCount/LayoutDuration. Nhãn “Tạo giọng / Không tạo giọng” phải nằm trong phần tiêu đề câu, không lấn nội dung xem trước, không lớn hơn chữ phụ đề và không tạo hàng riêng phía trên tiêu đề. Đối chiếu ảnh để kiểm dải xanh của thẻ được chọn không bị nền tiêu đề che đứt. Nút đóng banner phải không chồng nội dung/nút thử lại, có focus rõ và nhận phím Enter thật qua CDP. Đặt `VIDEOMAKER_SUBTITLE_EDITOR_ARTIFACTS` để lưu JSON và ảnh. Số render được đo bằng Vite transform chỉ áp dụng trong fixture, không thêm telemetry vào sản phẩm.

Test React bổ sung phải giữ draft khi revision/track/trang thay đổi hoặc lưu lỗi, chỉ cuộn khi cue ra khỏi vùng đọc, tạm ngưng khi wheel/phím/kéo thanh cuộn/focus và tiếp tục sau hai giây rảnh khi video đang phát. Kiểm reduced motion, hủy đích cuộn cũ, từ chối response trang cũ, thao tác giọng một câu dùng revision đã cập nhật sau lưu. Banner giữ nguyên lỗi/job thật sau khi đóng; poll cùng job/attempt không làm hiện lại, request/attempt mới với cùng nội dung vẫn hiện. Test audio-duck điều khiển timer và chờ ramp 140 ms hiện hành để không phụ thuộc tốc độ máy; vẫn kiểm riêng mức nền trong im lặng, có giọng và sau khi giọng ngừng.

## 10. Definition of Done

Một thay đổi được coi là hoàn tất khi:

- [ ] Contract/source/migration/tài liệu liên quan đồng bộ.
- [ ] Restore, Release build và test suite đạt; mọi Failed/Skipped được giải thích.
- [ ] Test bảo mật và negative path theo phạm vi đạt.
- [ ] Migration clone/idempotency/least privilege đạt nếu có schema/data change.
- [ ] Smoke môi trường thật đạt nếu thay integration/runtime; chi phí được phê duyệt trước.
- [ ] Không có secret hoặc dữ liệu nhạy cảm trong source/log/artifact.
- [ ] Monitoring, support note và rollback đã sẵn sàng.
- [ ] [BOI_CANH_HE_THONG_HIEN_HANH.md](BOI_CANH_HE_THONG_HIEN_HANH.md) chỉ được cập nhật bằng kết quả vừa thực sự chạy.

## 11. Mẫu báo cáo

```text
Thời điểm/múi giờ:
Commit hoặc mô tả dirty worktree:
Máy/OS/.NET/Node/SQL:
Lệnh đã chạy:
Passed / Failed / Skipped:
Migration/database đích:
Provider/model/chi phí smoke (nếu có):
Manual smoke:
Log/artifact bằng chứng:
Hạng mục chưa chạy và lý do:
Quyết định release/không release:
```

## 12. Mốc lịch sử, không phải kết quả hiện tại

Ngày 2026-09-06 từng ghi nhận Web 29/29, production build đạt, Release build 0 warning/error và .NET 802 passed / 0 failed / 2 skipped / 804 total với TEMP/TMP trên ổ D. Lần chuẩn hóa Markdown này không chạy lại các lệnh đó; không được sao chép con số này vào báo cáo mới như bằng chứng hiện hành.

## 13. Xác minh triển khai Dịch Cloud OpenAI — 2026-09-10

Phiên triển khai bắt đầu ngày 2026-09-09, chốt báo cáo ngày 2026-09-10, múi giờ Asia/Bangkok (UTC+07). HEAD `2b4171c`, dirty worktree có các thay đổi UI/media/voice từ trước; không tạo commit hoặc tác động database thật. Máy Windows `10.0.26200.0`, .NET SDK `10.0.301`, Node `v24.16.0`, npm `11.13.0`.

| Phạm vi | Kết quả thực chạy |
|---|---|
| `npm ci --no-audit --no-fund` | Passed |
| `npm run build` | Passed; Vite còn cảnh báo chunk lớn hơn 500 kB |
| `npm test` | 23 file, 126 Passed / 0 Failed / 0 Skipped |
| `dotnet restore TOOL_GEN_POST_VIDEO.slnx` | Passed |
| `dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore` | Passed, 0 warning / 0 error của MSBuild |
| `dotnet test TOOL-TESTS/TOOL-TESTS.csproj -c Release --no-build -- xUnit.ParallelizeTestCollections=false` | 1.014 Passed / 0 Failed / 3 Skipped / 1.017 Total, 2 phút 45 giây |
| `dotnet build TOOL-LOCAL/TOOL-LOCAL.csproj -c Debug --no-restore` | Passed, 0 warning / 0 error |
| SHA-256 Web/dist so với wwwroot Debug và Release | Mỗi cấu hình 3 file, 0 sai khác |
| WebView2 dùng component/CSS production | Passed ở zoom 100/125/150/200%; Cloud một click khi Local chưa sẵn sàng, không có model picker, không tràn ngang, nút thao tác tiếp cận được khi cuộn modal |

Full suite chạy tuần tự collection để hạn chế tranh tài nguyên giữa các fixture worker/media/WebView2; không tăng timeout hoặc giảm assertion. Ba test Skipped là Piper model integration, Qwen English/Chinese integration và Qwen benchmark 20 cảnh do chưa bật/cấu hình runtime/model thật. Không tính các test này là model đã đạt. Không có test Cloud bị skip.

Fixture Cloud kiểm strict contract/fingerprint, chia batch/cue mapping, endpoint allowlist/giới hạn response, refusal/incomplete/usage, authorization gate trước outbound, idempotent start, settlement/reconciliation, Unknown giữ reservation, retention, mất response POST/ACK, bảo vệ cue sửa tay/khóa, apply receipt và SRT. Budget dùng SQLite kiểm video/Vietsub chia sẻ hạn mức, tách loại project và replay settlement từ một DbContext còn giữ reservation cũ sau khi DbContext khác đã quyết toán. Lỗi bất ngờ sau dispatch phải giữ cả batch và attempt ở Unknown để quản trị có thể đối soát.

Kết quả build/test lấy từ output lệnh trong phiên. Ảnh và JSON WebView2 ở [.tmp/cloud-verification-20260909](.tmp/cloud-verification-20260909), gồm `cloud-100.png`, `cloud-125.png`, `cloud-150.png`, `cloud-200.png`; đã xem ảnh 100% và 200%. Đây là fixture với readiness giả, không phải phiên OpenAI thật. Artifact local không chứa transcript người dùng.

**Chưa chạy:** migration 4.1.6 trên SQL Server clone/đích và chạy lại idempotency; cạnh tranh nhiều server instance với SQL application lock/Serializable; smoke OpenAI có phí/chất lượng bản dịch; nghiệm thu desktop với tài khoản và video thực tế. Test migration hiện kiểm cấu trúc script; SQLite không thay thế các cổng SQL Server. Các bài rehearsal crash/rotation/đổi quyền giữa batch và lỗi disk/SQLite trên môi trường triển khai vẫn phải đối chiếu đầy đủ ma trận task.

**Quyết định:** source và fixture đã xác minh; chưa release/bật Cloud. Cấu hình vẫn `Enabled=false`, `ModelCode` trống; chưa chạy migration hoặc gọi OpenAI có phí. Điều kiện chuẩn bị môi trường, cấu hình, đối soát và rollback nằm trong [hướng dẫn vận hành Cloud](HUONG_DAN_VAN_HANH_DICH_CLOUD_VIETSUB.md).

## 14. Áp dụng migration Cloud trên database local — 2026-09-10

Sau báo cáo source ở mục 13, người dùng yêu cầu chạy database. Đích được đối chiếu từ cấu hình dự án và SQL runtime: `DUNGDEV / VideoFactory`, SQL Server `15.0.2000.5`, Windows authentication. Đã backup COPY_ONLY/CHECKSUM/COMPRESSION mới, VERIFYONLY và restore thực sang clone riêng; `DBCC CHECKDB` clone Passed.

Lần chạy clone đầu fail ở filtered index do sqlcmd chưa bật `QUOTED_IDENTIFIER`; transaction rollback trên clone. Bổ sung SET options bắt buộc vào migration mới, sau đó chạy hai lần trên clone và một lần trên đích đều Passed. Kiểm schema/version, FK/CHECK trusted, filtered index và nullability Passed. So sánh số dòng/checksum trên các cột cũ của 81 bảng chỉ thấy thêm version; 62 reservation, 178 ledger và quyền/role membership giữ nguyên. Các migration của nhánh khác đã có trên đích không bị thay đổi.

Test `VietsubCloudMigrationTests`: **1 Passed / 0 Failed / 0 Skipped**; `git diff --check` Passed. Thay đổi trong bước này chỉ là SQL SET options và tài liệu; không chạy lại build/full suite ứng dụng. Bản sao đã được dọn, backup trước migration còn giữ. Log/script/hash và đường dẫn backup nằm trong [báo cáo database](.tmp/cloud-database-20260910_003325/REPORT.md).

Cloud vẫn `Enabled=false`, model chưa cấu hình; chưa restart ứng dụng hoặc gọi OpenAI. Chưa chạy toàn chuỗi từ baseline thấp nhất, test nhiều server instance hoặc nghiệm thu model thật; không coi migration local thành công là đã rollout Cloud.

## 15. Thông báo lỗi license và đăng xuất — 2026-09-10

Xác minh trên dirty worktree hiện hành, múi giờ Asia/Bangkok. Không thay đổi dữ liệu license/session, không chạy migration hoặc gọi provider. Giữ nguyên quy tắc chọn phiên trên server; phạm vi sửa là trả lỗi nghiệp vụ có cấu trúc và giao diện phục hồi.

- `npm ci --no-audit --no-fund`, `npm run build`: Passed. Vite vẫn cảnh báo chunk lớn hơn 500 kB.
- `npm test`: 24 file, **131 Passed / 0 Failed / 0 Skipped**. Năm ca mới dựng App thật với bridge giả, kiểm thông báo khi khởi tạo/chạy, phân biệt giới hạn phiên/thiết bị, không tự gọi thanh toán hoặc lặp kiểm tra, giữ nút đăng xuất khi đang kiểm tra lại, chống double-click, phục hồi lỗi đăng xuất và mở khóa sau heartbeat thành công.
- `dotnet restore TOOL_GEN_POST_VIDEO.slnx`, build toàn solution Release và Debug `--no-restore`: Passed, **0 warning / 0 error MSBuild**.
- `dotnet test TOOL-TESTS/TOOL-TESTS.csproj -c Release --no-build -- xUnit.ParallelizeTestCollections=false`: **1.029 Passed / 0 Failed / 3 Skipped / 1.032 Total**, 2 phút 39 giây. TEMP/TMP đặt trong thư mục kiểm thử riêng trên ổ D. TRX tại `.tmp/license-access-verification/license-access-suite.trx`.
- Regression server kiểm HTTP 403/409/423, response thành công và lỗi bất ngờ; regression desktop kiểm thông báo ngay khi lease bị từ chối, chặn `EnsureAccessAsync`, không xóa phiên đăng nhập trên 409, vẫn xóa phiên trên 401 và thao tác bridge đăng xuất chỉ thu hồi phiên hiện tại rồi quay về đăng nhập.
- Đã xem ảnh dựng bằng Edge headless từ bundle production và bridge giả ở 1100×800 / 600×720: `.tmp/license-access-verification/desktop.png`, `narrow.png`. Không dùng tài khoản thật hoặc khởi động server; ảnh không phải nghiệm thu desktop đăng nhập thật.

Ba bài Skipped vẫn là Piper integration, Qwen English/Chinese integration và Qwen benchmark; không tính là model đã đạt. Chưa smoke lại thao tác bằng tài khoản thật trong Visual Studio. Sau build cần chạy lại server/desktop để nạp binary mới.

## 16. Tiếp tục tạo giọng khi tràn sang câu bỏ qua — 2026-09-10

Theo yêu cầu người dùng, phần giọng vượt thời gian câu bỏ qua được giữ lại và không làm tác vụ thất bại. Vẫn giới hạn tốc độ 1.20x, chỉ rút đuôi im lặng đã xác minh, giữ diagnostic không chặn, kiểm tra WAV/hash/revision và không gửi nội dung câu bỏ qua tới Piper. Renderer không còn cắt âm tại đầu câu bỏ qua.

- `dotnet restore TOOL_GEN_POST_VIDEO.slnx`, build toàn solution Release và desktop Debug `--no-restore`: **Passed**, MSBuild **0 warning / 0 error**.
- `npm ci --no-audit --no-fund`, `npm run build` (qua target build desktop): **Passed**. Vite vẫn cảnh báo chunk lớn hơn 500 kB.
- `npm test`: **131 Passed / 0 Failed / 0 Skipped** trong 24 file.
- .NET Release toàn bộ, `--no-build -- xUnit.ParallelizeTestCollections=false`: **1.030 Passed / 0 Failed / 3 Skipped / 1.033 Total**, 2 phút 49 giây. TEMP/TMP dùng thư mục riêng trên D. TRX: `.tmp/voice-overflow-verification/voice-overflow-suite.trx`.
- Regression dùng FFmpeg thật và WAV fixture: giữ tiếng nói tràn 27/135 ms, không chặn khi tắt phân tích khoảng lặng hoặc khi câu bắt đầu trong vùng bỏ qua; vẫn giữ khoảng im lặng đầu và hash WAV phrase gốc. Kiểm lại tạo mới, phục hồi cache, bật lại câu và xuất MP4 với âm gốc bật/tắt; phần tiếng nói tràn vẫn nghe được trong audio giải mã.
- Fixture lưu job lỗi cũ `VOICE_SKIPPED_CUE_OVERLAP` tại 72%, gọi `VietsubJobManager.RetryAsync` và xác minh hoàn thành 100%, xóa lỗi, checkpoint `COMPLETED`, dùng lại hai phrase cache mà không gọi synthesizer thêm.
- `git diff --check`: **Passed**. Không chạy migration hoặc sửa dữ liệu project của người dùng, không gọi provider hay model thật. Ba bài Skipped vẫn là Piper integration, Qwen English/Chinese integration và Qwen benchmark; chưa nghe nghiệm thu giọng Piper trên video người dùng. Cần chạy lại desktop để nạp DLL đã build.

## 17. Nút Xuất video trên thanh công cụ phụ đề — 2026-09-10

Nút **Xuất video** nằm bên phải nút thiết kế và menu tệp phụ đề, gọi chức năng xuất MP4 hiện có qua `vietsub.video.export`. Editor lưu bản nháp trước khi xuất, chặn khi lưu thất bại hoặc ngữ cảnh track thay đổi, khóa bấm lặp và chờ operation kết thúc để giải phóng trạng thái **Đang xuất…**. Không thêm renderer hoặc thay đổi contract WebView/C#.

- `npm ci --no-audit --no-fund`, frontend production build: **Passed**. Vite vẫn cảnh báo chunk lớn hơn 500 kB.
- `npm test`: **134 Passed / 0 Failed / 0 Skipped**, 24 file. Ba ca mới kiểm lưu bản nháp trước khi xuất, chặn bấm lặp, điều kiện thiếu video/track/busy và phục hồi sau hủy/lỗi để thử lại.
- `dotnet restore TOOL_GEN_POST_VIDEO.slnx`, build solution Release và desktop Debug `--no-restore`: **Passed**, MSBuild **0 warning / 0 error**. SHA-256 của ba file Web/dist khớp wwwroot ở cả hai cấu hình.
- .NET Release toàn bộ, `--no-build -- xUnit.ParallelizeTestCollections=false`: **1.030 Passed / 0 Failed / 3 Skipped / 1.033 Total**, 2 phút 40 giây. TRX: `.tmp/video-export-verification/video-export-suite.trx`.
- WebView2 dùng component/CSS production ở panel 320/420 px, zoom 100/125/150/200%: **Passed**, mỗi mức zoom 6 kịch bản và 0 lỗi. Nhãn **Xuất video** luôn hiện, nút nằm trong toolbar; các kiểm tra scroll/focus/bản nháp hiện hành vẫn đạt. Đã xem ảnh `editor-100.png` và `editor-200.png` trong `.tmp/video-export-verification/ui`.
- `git diff --check`: **Passed**. Kiểm thử dùng fixture, không xuất video project người dùng, không gọi provider hoặc model thật. Ba bài model Piper/Qwen opt-in vẫn Skipped; không coi là đã nghiệm thu model. Chạy lại desktop để nạp bundle mới.

## 18. Thu gọn card phụ đề và thêm nút xuất ở Timeline — 2026-09-10

Toolbar phụ đề đặt tiêu đề và nhóm nút cùng hàng, giữ số câu/trạng thái/cảnh báo ở hàng nhỏ phía dưới và bỏ nhãn PHỤ ĐỀ lặp lại. Nút xuất ở cuối thanh công cụ Timeline gọi cùng thao tác của editor; hai nút chia sẻ trạng thái đang xuất, lưu bản nháp và chống bấm lặp. Nhãn nút Timeline vẫn hiện ở viewport hẹp/zoom lớn; thanh âm lượng và nhóm công cụ chuyển hàng khi cần.

- `npm ci --no-audit --no-fund`, `npm run build`: **Passed**, còn cảnh báo Vite chunk lớn hơn 500 kB.
- `npm test`: **135 Passed / 0 Failed / 0 Skipped**, 24 file. Regression dựng `VietsubEditorWorkspace` thật, sửa bản dịch rồi bấm xen kẽ hai nút; kiểm chỉ lưu/xuất một lần, chặn khi lưu lỗi, cùng hiện **Đang xuất…**, phục hồi sau hoàn tất và thử lại.
- Restore solution, build solution Release và desktop Debug: **Passed**, MSBuild **0 warning / 0 error**. Ba file Web/dist khớp SHA-256 với wwwroot của cả hai cấu hình.
- .NET Release toàn bộ, `--no-build -- xUnit.ParallelizeTestCollections=false`: **1.030 Passed / 0 Failed / 3 Skipped / 1.033 Total**, 2 phút 41 giây. Kết quả cuối tại `.tmp/compact-timeline-export-verification/compact-timeline-export-final.trx`.
- WebView2 dùng component/CSS production: panel phụ đề 320/420 px với 7/50/120 câu, 3 cảnh báo; ở zoom 100/125/150/200%, tiêu đề cùng hàng với nút, toolbar không cao quá 68 CSS px, nhãn xuất không bị che. Các kiểm tra focus/cuộn/bản nháp cũ vẫn đạt, 6 kịch bản và 0 lỗi tại mỗi mức zoom.
- Timeline kiểm chiều rộng 420/640/820/1160 px theo giới hạn viewport ở cùng bốn mức zoom: nút xuất và nhãn hiện đầy đủ, không tràn toolbar hoặc chồng lên mixer; waveform/nhãn giọng hiện hành vẫn đạt. Đã xem ảnh card `final-editor/editor-100.png` và Timeline `final-timeline/timeline-200.png` trong `.tmp/compact-timeline-export-verification`.
- `git diff --check`: **Passed**. Ba bài model Qwen/Piper opt-in vẫn Skipped. Không gọi model/provider, không sửa dữ liệu project hoặc xuất video người dùng. Chạy lại desktop để dùng bundle mới.

## 19. Sửa trạng thái đang xuất không kết thúc — 2026-09-10

Frontend chờ Promise của `vietsub.video.export`, nhưng route C# chưa bật `notifyCompletion`; sau khi ghi file và gửi kết quả, bridge chỉ cập nhật state nên Promise vẫn chờ. Đã bật thông báo `vietsub.operation.completed` cho route xuất video, gửi sau khi giải phóng busy và cập nhật state. Editor/Timeline thoát trạng thái **Đang xuất…**, giữ thông báo thành công kèm tên file; hủy hộp thoại lưu hoặc lỗi render vẫn cho phép xuất lại.

- Regression C# gọi bridge thật với project/subtitle/MP4 fixture và process runner giả: kiểm thứ tự kết quả → state hết busy → completion có cùng request ID, hủy hộp thoại lưu, lỗi render và lần xuất tiếp theo. Trước sửa, cả ba ca tái hiện thiếu completion; lỗi render được báo đúng nhưng lần thử lại vẫn thiếu completion.
- Regression frontend dựng editor thật cùng `useVietsubModule` với message desktop giả, kiểm tên file/thông báo hiển thị, hết spinner, request ID không khớp không kết thúc nhầm tác vụ, hủy/lỗi và thử lại. Không thay Promise xuất bằng kết quả giả trực tiếp như các test nút trước đó.
- `npm ci --no-audit --no-fund`, production build qua target desktop: **Passed**. `npm test`: **138 Passed / 0 Failed / 0 Skipped**, 25 file. Vite vẫn cảnh báo chunk lớn hơn 500 kB.
- Restore solution, build solution Release và desktop Debug: **Passed**, MSBuild **0 warning / 0 error**. Ba file Web/dist khớp SHA-256 với wwwroot Debug/Release.
- .NET Release toàn bộ, `--no-build -- xUnit.ParallelizeTestCollections=false`: **1.033 Passed / 0 Failed / 3 Skipped / 1.036 Total**, 2 phút 58 giây; kết quả `.tmp/export-completion-verification/export-completion-final.trx`. Lượt đầu dùng TEMP/TMP dưới đường dẫn repo dài có 41 lỗi mở SQLite, 992 Passed và 3 Skipped; cả ba regression bridge mới đã Passed. Chạy lại toàn bộ với cùng binary và TEMP/TMP riêng có đường dẫn ngắn dưới `D:/tmp/vm-export-*` đạt; không sửa source hoặc giảm assertion để xử lý lỗi môi trường này.
- `git diff --check`: **Passed**. Ba bài model Qwen/Piper opt-in vẫn Skipped; chưa xuất lại video project người dùng hoặc gọi model/provider thật. Chạy lại desktop để nạp DLL đã sửa.

## 20. Nút quay về tạo dự án Vietsub — 2026-09-10

Thêm **Tạo dự án mới** phía trên các bảng biên tập, dùng mũi tên quay lại. Gọi luồng đóng project hiện có sau khi lưu bản nháp; giao diện quay về thư viện/form tạo dự án. Không phát sinh project trước khi người dùng nhập tên và bấm tạo. Khóa bấm lặp và khóa khi xử lý/xuất, phục hồi nút khi lưu/đóng thất bại; hàng điều hướng có nhãn đầy đủ ở màn hình hẹp.

- `npm ci --no-audit --no-fund`, production build: **Passed**; Vite còn cảnh báo chunk lớn hơn 500 kB. `npm test`: **141 Passed / 0 Failed / 0 Skipped**, 26 file.
- Ba regression dựng `VietsubPage`, editor và hook thật với message bridge giả: sửa bản nháp → lưu một lần → đóng → hiện form tạo và dự án cũ, chỉ tạo mới khi submit tên; giữ bản nháp khi lưu lỗi và thử lại sau lỗi đóng; chặn điều hướng khi có operation đang xử lý.
- Restore solution, build solution Release và desktop Debug: **Passed**, MSBuild **0 warning / 0 error**. SHA-256 ba file Web/dist khớp wwwroot cả hai cấu hình.
- .NET Release toàn bộ, `--no-build -- xUnit.ParallelizeTestCollections=false`, TEMP/TMP riêng dưới đường dẫn ngắn `D:/tmp/vm-nav-*`: **1.033 Passed / 0 Failed / 3 Skipped / 1.036 Total**, 2 phút 50 giây. TRX tại `.tmp/project-navigation-verification/project-navigation-suite.trx`.
- Edge headless chạy bundle App production cùng bridge giả: nút hiện đầy đủ trong viewport 1600×1000 và 720×900/zoom 125%; tại 1200×900 bấm đúng một request đóng, editor biến mất và form nhập tên dự án hiện ra. Đã xem ảnh `desktop.png`, `narrow.png`, `create.png` cùng JSON trong `.tmp/project-navigation-verification`.
- `git diff --check`: **Passed**. Ba test model opt-in Qwen/Piper vẫn Skipped, không tính là model đã đạt. Không dùng project/tài khoản thật; chạy lại desktop để dùng bundle mới.

## 21. Thu gọn nút tạo dự án trong card Thiết lập — 2026-09-10

Theo yêu cầu bố trí lại, chuyển nút **Tạo dự án mới** vào đầu card **Thiết lập dự án**, dùng cùng handler lưu/đóng đang có. Tiêu đề và nút cùng hàng khi đủ rộng; bỏ nhãn CÔNG CỤ/dòng mô tả và hàng điều hướng riêng. Khôi phục chiều cao editor trước khi thêm hàng điều hướng. Nút ở màn hình hẹp nằm trong tab Thiết lập.

- `npm ci --no-audit --no-fund`, production build, restore solution, build solution Release và desktop Debug: **Passed**. MSBuild **0 warning / 0 error**; Vite vẫn cảnh báo chunk >500 kB. Ba file Web/dist khớp SHA-256 với wwwroot Debug/Release.
- Bộ frontend hiện có: **141 Passed / 0 Failed / 0 Skipped**, 26 file; gồm kiểm luồng lưu trước khi quay về, lỗi/thử lại và chặn lúc bận. Không thêm test mới cho thay đổi vị trí/CSS.
- .NET Release toàn bộ, `--no-build -- xUnit.ParallelizeTestCollections=false`, TEMP/TMP riêng có đường dẫn ngắn trên D: **1.033 Passed / 0 Failed / 3 Skipped / 1.036 Total**, 3 phút 3 giây. TRX: `.tmp/compact-settings-navigation-verification/compact-settings-navigation-suite.trx`.
- Edge headless với bundle App production và bridge giả: đầu card cao **49 CSS px** ở chiều rộng panel mặc định, **76 px** khi thu panel về **220 px**; nút luôn nằm trong đầu card, không bị che chữ. Ở **720×900/zoom 125%**, mở tab Thiết lập thấy nút đầy đủ. Ở **1200×900**, bấm nút gửi một request đóng và mở đúng form tạo dự án. Đã xem ảnh `desktop.png`, `compact.png`, `narrow.png`; ảnh/JSON và `create.png` ở `.tmp/compact-settings-navigation-verification`.
- `git diff --check`: **Passed**. Ba test model opt-in Qwen/Piper vẫn Skipped; không tính là model đã đạt. Dùng fixture, không thay đổi project người dùng. Chạy lại desktop để nạp bundle mới.

## 22. Căn cụm âm thanh sát nhóm điều khiển Timeline — 2026-09-10

Toolbar phân bổ khoảng trống vào cột nhãn Timeline bên trái, đưa cụm Âm gốc/Giọng Việt/Tự hạ nền sát nhóm Theo playhead ở bên phải. Cụm âm thanh giữ chiều rộng tối đa 520 CSS px và khoảng cách 12 px với nhóm nút khi đủ rộng; các breakpoint hẹp tiếp tục co hoặc chuyển hàng theo bố cục hiện có. Chỉ sửa CSS.

- `npm ci --no-audit --no-fund`, frontend production build, restore solution, build solution Release và desktop Debug: **Passed**, MSBuild **0 warning / 0 error**. Vite còn cảnh báo chunk >500 kB. Ba file Web/dist khớp SHA-256 với wwwroot Debug/Release.
- `npm test`: **141 Passed / 0 Failed / 0 Skipped**, 26 file; không thêm test mới cho thay đổi căn lề.
- Edge headless dùng bundle App production và bridge giả: viewport 1600×1000 và 1920×1080 có mixer rộng **520 CSS px**, cách nhóm nút **12 px**. 720×900/zoom 125% chuyển hàng, không chồng nhóm nút hoặc tràn toolbar. Đã xem ảnh `wide.png`, `narrow.png`; ảnh/JSON ở `.tmp/mixer-right-alignment-verification`.
- .NET Release toàn bộ, `--no-build -- xUnit.ParallelizeTestCollections=false`, TEMP/TMP riêng có đường dẫn ngắn trên D: **1.033 Passed / 0 Failed / 3 Skipped / 1.036 Total**, 3 phút 3 giây. TRX tại `.tmp/mixer-right-alignment-verification/mixer-right-alignment-suite.trx`. Fixture WebView2 Timeline hiện có đạt ở zoom 100/125/150/200%, kiểm các chiều rộng 420/640/820/1160 px, nút xuất và mixer không chồng nhau; ảnh trong thư mục `timeline` cùng artifact.
- `git diff --check`: **Passed**. Ba test model opt-in Qwen/Piper vẫn Skipped; không tính là model đã đạt. Không thay dữ liệu project hoặc cấu hình âm lượng người dùng. Chạy lại desktop để nạp bundle mới.

## 23. Tích hợp Setup và tối ưu Vietsub/video dài vào local-2 — 2026-09-14

Phạm vi lấy nghiệp vụ Setup hệ thống và xử lý video dài từ `sub-local`, đồng thời giữ ứng dụng đầy đủ của `local-2`. Source không có `VietsubLocalOnly`, bộ lọc local-only hoặc nhánh giao diện chỉ hiện Vietsub. Video AI, Dịch Cloud và các trang hiện hành vẫn giữ đường gọi cũ. Gate mới tự kiểm tra FFmpeg/OCR/Qwen/Piper sau đăng nhập, license và tổ chức hợp lệ; trạng thái thiếu mở hộp thoại **OK - Cài đặt/Sửa bộ ứng dụng** hoặc **Hủy và thoát** trước khi tạo màn hình chính.

- `dotnet restore TOOL_GEN_POST_VIDEO.slnx`: **Passed**.
- `dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore`: **Passed**, 0 warning / 0 error.
- `dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build -- xUnit.ParallelizeTestCollections=false`: **1.078 Passed / 0 Failed / 4 Skipped / 1.082 Total**.
- `npm ci --no-audit --no-fund` và `npm run build`: **Passed**. Vite còn cảnh báo chunk lớn hơn 500 kB.
- `npm test`: **148 Passed / 0 Failed / 0 Skipped**, 27 file. Test riêng Setup panel có 7 bài và đều Passed.
- Test workflow Setup lúc khởi động: **4 Passed / 0 Failed / 0 Skipped**; bao phủ bỏ qua marker đã sẵn sàng, kiểm rồi cài model thiếu, chuyển OCR/FFmpeg hỏng sang updater và xác nhận tài nguyên Qwen gắn với đúng lượt retry.
- `git diff --cached --check`: **Passed**.

Full suite đầu phát hiện một lỗi ở đường cài Qwen cũ: bước lấy khóa runtime chạy trước kiểm tra xác nhận tài nguyên nên có thể che mã lỗi nghiệp vụ bằng lỗi chung. Đã kiểm tra xác nhận trước khóa, kiểm lại sau khi có khóa và ánh xạ lỗi Setup về lỗi Vietsub tương ứng cho cả Qwen/Piper. Test đích và full suite sau sửa đều Passed.

Hai lượt full suite theo chế độ song song mặc định dùng để chẩn đoán gặp một sai lệch căn giữa WebView2 một pixel và một lần tranh khóa `RuntimeUseGate` giữa collection. Từng test đều Passed khi chạy cô lập. Lượt nghiệm thu cuối tắt song song collection theo quy ước kiểm thử hiện có của repository và Passed toàn bộ; không sửa assertion hoặc bỏ test để đạt kết quả này.

Bốn bài Skipped là các bài opt-in dùng Qwen/Piper thật; không tính là model đã đạt trong lần tích hợp này. Chưa smoke bằng project/video người dùng, package sửa chữa thật, Windows sạch hoặc máy RAM thấp; chưa publish, chạy migration, gọi provider có phí hay sửa dữ liệu thật.

## 24. Chuyển gate Setup lúc khởi động sang modal WebView — 2026-09-15

Luồng khởi động tạo `Form1`, tải dashboard React và hiển thị nền dự án trước khi mở modal Setup. Cờ `startupRequired` do host xác định theo role và đi cùng snapshot Setup. Khi modal hiện, sidebar/nội dung chính dùng `inert` và `aria-hidden`; modal giữ focus, chặn `Esc`/backdrop, tự chạy kiểm tra, hỗ trợ cài/retry/xác nhận tài nguyên Qwen, hiển thị package repair và có **Hủy và thoát**. Host C# chặn command nghiệp vụ với `system_setup_required` cho tới khi toàn bộ thành phần không `DISABLED` đều `READY`.

- `npm ci --no-audit --no-fund`, `npm run build`: **Passed**. Vite còn cảnh báo chunk lớn hơn 500 kB.
- `npm test`: **152 Passed / 0 Failed / 0 Skipped**, 29 file. Ba file Setup có **11 Passed / 0 Failed / 0 Skipped**, gồm panel Settings hiện hành, modal startup và App thật với bridge giả.
- Regression frontend kiểm nền dashboard tồn tại trước modal, khóa/mở `inert`, không đóng bằng `Esc`, tự check đúng context, retry Qwen gắn operation/profile, repair package và lệnh thoát.
- `dotnet restore TOOL_GEN_POST_VIDEO.slnx`: **Passed**.
- `dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore`: **Passed**, MSBuild **0 warning / 0 error**.
- Test riêng namespace SystemSetup: **41 Passed / 0 Failed / 1 Skipped / 42 Total**. Test mới kiểm gate chỉ cho phép command bootstrap/Setup/repair, mở khóa sau `READY`, bỏ gate cho role không được yêu cầu Setup, snapshot có cờ startup và request thoát phải rỗng/hợp lệ.
- Full .NET Release `--no-build -- xUnit.ParallelizeTestCollections=false`: **1.081 Passed / 0 Failed / 4 Skipped / 1.085 Total**, lượt cuối 3 phút 18 giây.
- `git diff --check`: **Passed**.

Bốn bài Skipped vẫn là các bài opt-in dùng model/runtime Qwen/Piper thật; không tính là model đã đạt. Không chạy migration, provider có phí, package repair thật hoặc dữ liệu project người dùng. Chưa smoke modal trực tiếp trong desktop với tài khoản/organization thật, chưa xác minh Windows sạch và chưa chụp giao diện WebView2 ở ma trận DPI/zoom; vì vậy thay đổi source/test đạt nhưng chưa phải bằng chứng phát hành production.

## 25. Lật hình video trong Thiết kế thành phẩm — 2026-09-15

Modal có hai nút bật/tắt độc lập **Lật trái–phải** và **Lật trên–dưới**. Bản nháp chỉ lật phần tử video, giữ phụ đề và âm thanh đúng chiều; nút **Mặc định** tắt cả hai. Lưu và lưu trước khi xuất ghi `videoTransformSettings` theo project cùng style/mixer; manifest JSON schema 6 nâng sang 7 với cả hai chiều mặc định tắt, không thay SQLite schema 6. FFmpeg đặt `hflip`/`vflip` trước `subtitles`, so lại snapshot lật hình trước khi publish `.partial.mp4`; thay đổi giữa lúc render không xuất bản output cũ.

- `dotnet restore TOOL_GEN_POST_VIDEO.slnx`: **Passed**; build solution Release `--no-restore`: **Passed**, 0 warning / 0 error.
- `npm ci --no-audit --no-fund`, `npm run build`: **Passed**; Vite vẫn cảnh báo chunk >500 kB. `npm test`: **154 Passed / 0 Failed / 0 Skipped** trong 29 file; test tương tác kiểm hai nút, preview chỉ lật video, lưu trước xuất và reset mặc định.
- Test C# kiểm migration 6 → 7, bridge lưu/trả event, bốn thứ tự bộ lọc và từ chối output khi lật hình đổi giữa lúc render: **Passed**. Bốn fixture qua FFmpeg/FFprobe bundle thật kiểm vị trí pixel hình ảnh, phụ đề vẫn phía dưới, kích thước và trạng thái audio không đổi: **4 Passed / 0 Failed / 0 Skipped**.
- Full .NET Release ở chế độ song song mặc định: **1.089 Passed / 1 Failed / 4 Skipped**, lỗi tại bài cài runtime translation không thuộc tính năng lật hình (`InstallCallCount` 0 thay vì 1). Bài này chạy riêng: **1 Passed / 0 Failed / 0 Skipped**. Full suite theo quy ước `--no-build -- xUnit.ParallelizeTestCollections=false`: **1.090 Passed / 0 Failed / 4 Skipped / 1.094 Total**, 3 phút 29 giây; không sửa assertion hay tắt test để đạt kết quả.
- `git diff --check`: **Passed**. Không dùng project/video người dùng, không chạy migration SQL, model/provider có phí hay publish. Bốn bài Skipped vẫn là model/runtime opt-in, không tính là đạt. Chưa nghiệm thu thủ công modal WebView2 trên video 16:9 và 9:16 cùng ma trận DPI/zoom hoặc đối chiếu hình/phụ đề/audio trên MP4 người dùng; đây là bước trước khi kết luận sẵn sàng phát hành.
