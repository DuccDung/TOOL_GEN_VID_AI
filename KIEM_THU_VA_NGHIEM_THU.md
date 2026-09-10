# Kiểm thử và nghiệm thu VideoMaker

### Veo local voice — kiểm thử bổ sung 2026-09-09

TOOL-TESTS/LocalVoice, các test local policy trong ProjectRenderServiceTests, và Web/src/features/localVoice/*.test.ts bao phủ policy, access, stale/hash, checkpoint, cancel/retry/idempotency, duyệt riêng, native exception, cleanup và FFmpeg remux. Fake model/media chỉ chứng minh điều phối, không chứng minh chất lượng tiếng Việt.

LocalVoiceModelTests mặc định **Skipped**, chỉ chạy khi có VM_LOCAL_VOICE_COMPONENT_ROOT, VM_LOCAL_VOICE_SMOKE_SOURCE và VM_LOCAL_VOICE_SMOKE_ANCHOR. Hai sample phải có quyền sử dụng. Technical smoke kiểm nạp model, VAD/separation/conversion, audible/duration, video packet hash và cache retry; không chấm khẩu hình hoặc nhận diện giọng bằng tai.

Bổ sung triển khai 2026-09-10: `LocalVoiceDeploymentTests` kiểm cấu hình component/temp độc lập media workspace, không tự cài khi đọc trạng thái, từ chối đường dẫn không hợp lệ và lọc môi trường tiến trình con. Test model ghi thời gian và peak working set từng worker; số đo là tiến trình Python, không phải tổng RAM cả ứng dụng. Chạy test .NET với TEMP/TMP ngắn (ví dụ `D:\vmt-voice-0910`) để tránh SQLite lỗi đường dẫn dài trên Windows. Có thể dùng Python của component để chạy `TOOL-TESTS/LocalVoice/test_voice_consistency_worker.py`; không cần Python hệ thống. Helper có regression dùng tensor nhỏ, chặn `_native_multi_head_attention` và xác minh CPU profile vẫn xử lý attention được; tái hiện nhánh gây access violation khi tách âm trước bản sửa. Case này cần PyTorch, không tải model khi test. Bằng chứng sau thay đổi: [task triển khai trên máy đích](TASK_TRIEN_KHAI_VEO_LOCAL_SU_DUNG_THUC_TE.md).

Trước production cần rehearsal migration trên clone, UI WebView2 thật, 2–3 clip Veo tiếng Việt cùng nhân vật, giọng nam/nữ, âm nền, lỗi/no-speech/multiple-speaker, restart/retry, nghe A/B và đo CPU/RAM/thời gian. Thiếu clip thật hoặc model test bị skip phải ghi chưa nghiệm thu. Test cloud LipSync bị loại khỏi build thử nghiệm không được tính Passed hoặc Skipped của runner.

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
| TikTok | OAuth state/PKCE/user-device binding, app credential Pending/Active và mã hóa theo ID, Global Admin authorization/audit, token encryption, creator policy, idempotency, chunk/Range, exact host, path/URL redaction, recovery và polling |
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
- TikTok token/app secret chỉ ở server; signed upload URL không vào React/log/database plaintext và upload request không kèm JWT/Bearer của app.

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

Với migration 4.1.6, kiểm tra unique connection/idempotency theo user, FK user/device, ciphertext token/upload URL, row version và desktop principal không có quyền trực tiếp schema `social`.

Với migration 4.1.7, kiểm tra duy nhất một credential `Pending`/`Active`, payload mã hóa, FK OAuth session tới credential, singleton integration settings, audit evidence constraint và không cấp thêm quyền SQL cho desktop.

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

Test không cần model thật phải phủ phrase boundary/fingerprint, migration SQLite, cache theo revision, RIFF/PCM và silence trim, ba nhánh fit tự nhiên/mượn khoảng trống/tăng tốc, publish timeline kèm diagnostic không chặn khi vượt `1.20x`, FFmpeg failure/cancel, cùng playback sai project hoặc sai hash. Không được coi fake synthesizer là bằng chứng chất lượng giọng.

Sau khi đã cài component qua UI vào workspace được duyệt, chạy verify model/worker thật:

```powershell
.\scripts\Verify-VietsubVoiceModel.ps1 `
  -WorkspaceRoot "<approved-workspace-root>" `
  -Configuration Release
```

Script kiểm tra size/SHA-256 model và config trước khi bật riêng category `LocalVoiceIntegration`. Category này phải tạo được nhiều WAV tiếng Việt bằng các index không liên tục, qua đúng worker/runtime và RIFF/PCM validator; khi không bật opt-in, kết quả `Skipped` không phải là đạt.

Trên đúng bundle x64 định phát hành, xác nhận `VietsubLocalVoiceEnabled` đang bật, cài runtime qua UI rồi xác minh model/config đúng checksum. Tạo giọng từ track mà toàn bộ cue đã có nội dung dịch tiếng Việt, gồm cả fixture có trạng thái cảnh báo/chất lượng chưa duyệt để xác minh các trạng thái này không chặn nghiệp vụ; xác minh output xuất hiện ở track **Giọng Việt** dưới phụ đề và đồng bộ play/pause/seek/tốc độ với video mà không còn audio player độc lập trong panel thiết lập. Nghe câu ngắn/dài/dấu câu/tên riêng, thử cancel/retry và khởi động lại để kiểm tra cache/checkpoint. Sửa một cue để xác minh timeline cũ mất hiệu lực, sau đó kiểm tra CPU/RAM/disk/thời gian và worker không có provider/database/network ngoài giai đoạn component store tải các URL đã pin.

`VietsubLocalVoiceEnabled` được bật mặc định để người dùng luôn thấy trạng thái giọng local. Việc bật flag không đồng nghĩa runtime/model đã sẵn sàng: máy thiếu component phải trả `NOT_INSTALLED`, yêu cầu người dùng chủ động xác nhận cài và chỉ được trả `READY` sau khi model/config/worker đúng checksum và probe đạt. Trước khi tuyên bố production-ready vẫn phải kiểm kê đầy đủ license và dependency Python, verify model thật, benchmark, nghe nghiệm thu và smoke desktop; test unit dùng fake worker hoặc model test bị `Skipped` không thay thế các cổng này.

## 7A. TikTok Direct Post

Nhiều tài khoản: kiểm tra ownership mọi route, OAuth thêm/reconnect/sai danh tính, giữ token/job A khi thêm hoặc ngắt B, ClientRequestId độc lập media và conflict khi đổi payload/account, attempt được commit trước outbound và không replay khi timeout/restart. Native phải chặn sai ConnectionId từ server, MediaId cũ và file bị thay trong lúc init trước khi gửi byte. UI kiểm tra A → B → A với creator/error đến muộn, reset privacy/consent, nhiều job và dialog đúng tài khoản. Khi integration tắt, vẫn phải quản lý ngắt tài khoản và xem lịch sử.

SQL integration dùng opt-in `VIDEOMAKER_RUN_TIKTOK_SQL_TESTS=1`: tạo instance LocalDB 2019 riêng với dữ liệu giả, backup CHECKSUM/VERIFYONLY, chạy migration 4.1.8 hai lần, giữ ID/token/job cũ, kiểm unique identity, application lock và worker claim trên SQL Server thật. Không nhận connection string hoặc dùng database của người vận hành; hướng dẫn và kết quả tại [TRIEN_KHAI_TIKTOK_NHIEU_TAI_KHOAN.md](TRIEN_KHAI_TIKTOK_NHIEU_TAI_KHOAN.md). Rehearsal này không thay thế bản sao database đầy đủ của môi trường đích.

Form đăng TikTok dùng thanh tài khoản gọn và hai cột preview/thiết lập trên desktop; preview dọc co theo chiều cao cửa sổ. Browser fixture dùng khung sidebar/topbar hiện hành để kiểm tra nút đăng không cần cuộn ở 1280×720, 1366×768 và 1440×900; nội dung thương mại mở rộng được kiểm tra ở 1366×768. Màn hình nhỏ vẫn cuộn tới được nút đăng, không tràn ngang. Tiến trình dùng dialog giữa viewport với backdrop nhẹ, giữ focus bàn phím, thu nhỏ/mở lại mà không gửi bài mới. Test đi qua bridge event giả lập cho chuẩn bị, upload 100% chưa phải thành công, TikTok xử lý, thành công, lỗi khởi tạo/polling/provider, hủy, thay đổi creator policy trong lúc init và khôi phục job đang chạy. Không gọi TikTok thật.

Server và Desktop phải chấp nhận đúng các host upload `open-upload.tiktokapis.com`, `open-upload-sg.tiktokapis.com`, `upload.us.tiktokapis.com` qua HTTPS/443 và giữ nguyên query của URL trả về. Test dùng URL giả lập cho cả ba host; URL sai giao thức/cổng, host giả mạo hoặc subdomain ngoài danh sách, userinfo, fragment và URL quá dài phải bị chặn. Server không lưu job với URL bị từ chối; Desktop từ chối trước khi mở file hoặc gửi dữ liệu. Không dùng URL/token thật trong fixture.

Preview TikTok phải được cho phép bởi `media-src` cho đúng host nội bộ `https://tiktok-media.app.local`. Browser test dùng CSP từ `Web/index.html`, video H.264 tổng hợp bằng FFmpeg và HTTP range giả lập để kiểm tra tải metadata/hình, phát, tua, đổi file, báo lỗi và tải lại. Kiểm tra tỷ lệ khung hình trên desktop/mobile và xác nhận CSP vẫn chặn media từ host ngoài allowlist. Test này không thay thế smoke WebView2 trên file thực tế của người dùng.

Làm mới creator info trả thông tin trực tiếp từ TikTok, không ghi lại tên hoặc `UpdatedAtUtc` của connection sau mỗi lần đọc. Test dùng hai DbContext thay đổi `RowVersion` trong lúc chờ provider, kiểm tra không xung đột hoặc ghi đè token mới; nhánh access token hết hạn vẫn phải lưu token refresh. Desktop chỉ gửi một yêu cầu creator đang chờ tại một thời điểm; browser test kiểm tra mở trang, gọi refresh liên tiếp và thử lại sau success/error đúng request ID.

Ứng dụng chưa audit yêu cầu cả tài khoản TikTok riêng tư và quyền xem bài đăng `SELF_ONLY`. Tài khoản công khai nhận creator info kèm `PublishingIssue`, không ném exception cho điều kiện này. UI hiển thị hướng dẫn và nút kiểm tra lại, khóa đăng khi còn issue. Server kiểm tra lại trước init; nếu bị chặn thì trả `BlockedCreator`, không tạo job hoặc gọi API đăng. Desktop nhận kết quả này phải cập nhật creator và kết thúc thao tác trước upload. Test phủ trường hợp bị chặn và kiểm tra lại sau khi tài khoản chuyển riêng tư.

Kiểm tra luồng xác minh khi Desktop mở trước Admin: mở lại mục TikTok phải nạp trạng thái mới; lỗi lấy trạng thái phải hiển thị cả khi integration chưa sẵn sàng. Server trả lý do an toàn để phân biệt phiên thuộc tài khoản khác, phiên hết hạn, chưa thiết lập hoặc bị tắt. OAuth trên Desktop chuyển quyết định license cho server để Admin đang xác minh không bị chặn bởi license local; user thường vẫn phải qua kiểm tra license tại server và thao tác đăng video vẫn giữ kiểm tra license. Các trường hợp này được kiểm tra bằng fake HTTP/runtime và test render React, không gọi TikTok thật.

Giao diện Admin TikTok có bộ kiểm tra trạng thái thuần JavaScript và bộ kiểm tra trình duyệt dùng chính Razor markup, CSS, script hiện hành với API giả lập. Chạy từ thư mục gốc repository:

```powershell
node --test TOOL-TESTS/TikTok/admin-tiktok-state.test.cjs
# Cần Playwright và Chromium tương ứng đã cài trong môi trường kiểm thử.
# Nếu package ở ngoài repository, đặt VIDEOMAKER_PLAYWRIGHT_MODULE trỏ tới thư mục package playwright.
node --test TOOL-TESTS/TikTok/admin-tiktok.browser.test.cjs
# Cần npm ci tại TOOL-LOCAL/Web; render React hiện hành bằng Rolldown, dữ liệu giả lập.
node --test TOOL-TESTS/TikTok/desktop-tiktok.browser.test.cjs
```

Bộ trình duyệt chặn network, không khởi động server và không chạm database/TikTok. Phạm vi gồm xác nhận trước khi mở phiên xác minh, xóa secret khỏi form, chặn gửi lặp, giữ bản nháp khi làm mới, phát hiện cài đặt server thay đổi, xử lý lỗi, polling/hết hạn/rời trang, đăng xuất, xác nhận công khai và bố cục 375–1440 px. Có thể đặt `VIDEOMAKER_SCREENSHOT_DIR` để lưu ảnh kiểm tra desktop/mobile. Đây là kiểm tra UI với API giả lập; không thay thế nghiệm thu OAuth thật.

Test tự động không gọi TikTok thật phải phủ chunk boundary, `Content-Range`, 206/201, retry/416 reconciliation, exact HTTPS host/443, file đổi sau init, OAuth state/PKCE/user-device binding, app credential mã hóa/ràng buộc ID, chỉ Admin được quản lý, Pending không dùng chung, activation/audit gate, token encryption theo user, ownership/idempotency và không lộ secret/token/path/signed URL sang React.

Smoke thật chỉ chạy khi được phép trên app/tài khoản test đã review: connect/reconnect/disconnect, query creator info, privacy không có mặc định, interaction bị TikTok disable, disclosure/consent, video nhỏ và nhiều chunk, đóng/mở desktop giữa job, terminal success/failure và refresh token. Không coi build/unit test là bằng chứng TikTok app đã được audit hoặc public posting đã sẵn sàng.

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
- Organization/project switch không giữ state/busy của context cũ.
- Lỗi auth/budget/pricing/provider/media hiển thị có hành động khắc phục, không lộ chi tiết nhạy cảm.
- Approve/reject/retry có xác nhận phù hợp với chi phí.
- Admin desktop/mobile widths: navigation, table/form/modal, Owner protection, credential hint và loading/error state.
- WebView refresh/reconnect không gửi lặp operation.

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
