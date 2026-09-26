# Kiểm thử và nghiệm thu VideoMaker

## Sửa runtime OCR của ZIP trên máy khách — 2026-09-26

Nhánh `main`, commit nền `bc1c6aec3c731bdb0edcebbde17ae13640245e52` cùng working tree. Bộ Visual C++ x64 cho OCR được ghim hash và đi cạnh EXE; kiểm startup từ chối file thiếu/hỏng, CLI kiểm vị trí module thực nạp. Chi tiết artifact và giới hạn: [báo cáo sửa OCR](TRIEN_KHAI_SUA_OCR_MAY_KHACH.md).

- Restore/build Release đạt, 0 warning/error. Frontend ci/build đạt; **280 Passed / 0 Failed / 0 Skipped**.
- Regression OCR **14 Passed / 0 Failed / 0 Skipped**; Full C# **1.576 Passed / 0 Failed / 13 Skipped**, source/binary manifest ổn định, 0 tiến trình con còn lại.
- ZIP build 2: 56 thành phần, 24 web asset, OCR Anh/Trung/FFmpeg/WebView2 READY; cả bốn DLL Visual C++ nạp từ thư mục ứng dụng. Bỏ hoặc sửa một byte `vcomp140.dll` trong bản thử trả đúng lỗi, kể cả máy đã có runtime hệ thống.
- Piper từ ZIP cài mới offline và kiểm lại ba lần READY. ZIP vá nhỏ áp lên EXE build 1 cũ đạt OCR và xác minh cả bốn DLL nạp tại chỗ, cấu hình kết nối giữ nguyên.
- Chưa kiểm trực tiếp trên máy khách hoặc Windows sạch riêng, chưa đăng nhập/workflow SQL hoặc đổi package repair trên server. Những bài Skipped không được coi là đạt; hồ sơ phê duyệt phân phối giữ trạng thái trước đó.

> Ma trận áp dụng cho source hiện hành; rà soát dependency tại commit `8f10cc9` ngày 2026-09-15. Những mục có ngày/commit và số test bên dưới là **biên bản lịch sử**, không phải kết quả chạy mới trên checkout hoặc môi trường đang dùng.

Để kết luận một tính năng sẵn sàng, báo riêng: source/contract/migration có mặt; build/test tự động trên commit đích với Passed/Failed/Skipped; clone/schema/quyền trên database đích; smoke runtime/model/provider/UI trên máy/bundle đích; quyết định rollout production. Không cộng test model/SQL opt-in bị `Skipped` vào Passed và không thay smoke thật bằng fake HTTP/model.

### Ổn định kiểm thử — triển khai 2026-09-26

Nhánh `main`, commit nền `bc1c6aec3c731bdb0edcebbde17ae13640245e52` và working tree đã sửa. Có fixture cô lập runtime gate/pool SQLite, chờ job bằng event, collection native riêng, profile WebView2 riêng và runner lặp lưu đầy đủ lỗi. Hai regression bridge/Cloud đạt **100/100 lượt mỗi bài**. C# toàn bộ đạt **10/10 lượt**, mỗi lượt **1551 Passed / 0 Failed / 13 Skipped**; frontend đạt **10/10 lượt**, mỗi lượt **277 Passed / 0 Failed / 0 Skipped**. Trong mỗi lượt C# có 14 ca native trực tiếp đạt trên tiến trình mới với TEMP Unicode; không còn tiến trình con sau cleanup, source/binary manifest ổn định. Các bài model/SQL bị skip chưa được nghiệm thu. Piper thật đã thử và thất bại khi .NET HTTP không phân giải được `github.com:443`, chưa đạt cài mới. Windows thứ hai, ZIP/máy khách và production chưa xác minh. [Lệnh, artifact, lỗi trước/sau và giới hạn](TRIEN_KHAI_ON_DINH_KIEM_THU.md).

### Setup cho bản ZIP — biên bản trước đợt ổn định, 2026-09-26

Nhánh `main`, commit nền `bc1c6aec3c731bdb0edcebbde17ae13640245e52` cùng working tree của bản sửa. Restore/build Release và frontend build đạt; frontend **272 Passed / 0 Failed / 0 Skipped**. C# lượt cuối chạy collection tuần tự **1542 Passed / 0 Failed / 13 Skipped**; lượt mặc định **1541 Passed / 1 Failed / 13 Skipped** do một test Cloud Vietsub hết thời gian chờ. Không coi lượt mặc định là đạt. ZIP chẩn đoán sau giải nén trong đường dẫn có dấu đã qua OCR Anh/Trung, FFmpeg và WebView2; regression chứng minh lỗi marshal đường dẫn trước sửa. Bài cài mới Piper opt-in **Failed** do tiến trình không phân giải được `github.com`, chưa nghiệm thu cài mới. Chưa kiểm máy khách/server/SQL đích hoặc rollout. [Artifact, hash, lệnh và giới hạn](TRIEN_KHAI_SUA_LOI_SETUP_BAN_ZIP.md).

### Hợp nhất chọn lọc `main` vào `local-2` — xác minh 2026-09-15

Checkout tích hợp từ `local-2` `959aeb9` và `origin/main` `9b8052c`: `dotnet restore`, `dotnet build -c Release --no-restore`, `npm ci` và `npm run build` đều Passed. `dotnet test -c Release --no-build -- xUnit.ParallelizeTestCollections=false`: **1.352 Passed / 0 Failed / 6 Skipped / 1.358 Total**. `npm test -- --maxWorkers=1 --no-file-parallelism`: **224 Passed / 0 Failed / 0 Skipped** trên 39 file. Các bài model/runtime và SQL opt-in bị Skipped không được coi là nghiệm thu. Không chạy SQL thay đổi dữ liệu, provider có phí, đăng TikTok hoặc phát hành.

### Tải video Bilibili — 2026-09-11

Restore/build Release đạt; MSBuild 0 warning/error, Vite còn cảnh báo bundle trên 500 kB. Frontend **204 Passed / 0 Failed / 0 Skipped**; .NET **1.294 Passed / 0 Failed / 5 Skipped** trên toàn suite chạy collection tuần tự và TEMP/TMP riêng ở D. Lượt đầu 21 failure do ổ C thiếu dung lượng không được tính Passed. Thêm 40 native và 8 frontend test cho input/allowlist, partial scan, collection/dedup, access/stale selection, busy/cancel/retry, checksum/signature, atomic promote/không ghi đè, cleanup và menu/UI. WebView2 với fixture giả không tràn ngang ở 1440/1024 px và zoom 125%; smoke public tải MP4 thật qua checksum/probe đạt. Full-channel bị nền tảng hạn chế nên chưa nghiệm thu quét hoàn tất toàn kênh. [Lệnh, artifact và giới hạn](TRIEN_KHAI_TAI_VIDEO_BILIBILI.md).

### Phục hồi kết quả video ngắn — 2026-09-11

Restore/build Release và Debug đạt, 0 warning/error MSBuild; frontend 196 Passed / 0 Failed / 0 Skipped; .NET 1.254 Passed / 0 Failed / 5 Skipped. Regression kiểm GET 429 và không replay POST, khôi phục cache trên cùng request, hash không khớp, lỗi tải hiển thị trên composer và resume không tạo AI mới. Bốn bài model và một SQL rehearsal opt-in bị skip không được coi là đạt. Xem [bằng chứng và phần còn cần người dùng xem/duyệt](SUA_LOI_TAI_VIDEO_NGAN.md).

### Veo cho toàn bộ video ngắn — 2026-09-11

Lượt cuối: .NET **1243 Passed / 0 Failed / 5 Skipped** qua hai nhóm không giao nhau (ngoài Vietsub 950/0/2, Vietsub 293/0/3); frontend **193 Passed / 0 Failed / 0 Skipped**. Restore, npm ci, production frontend build và Release solution build riêng đạt. Kiểm gateway với provider giả xác minh SceneFirstFrame thực đã duyệt, chặn thiếu frame trước chi phí, Fal submit và idempotent replay; thêm chuyển snapshot, quote TextOnly, input lineage và UI xác nhận. Browser WebView2 kiểm composer/thư viện ở 4 kích thước × 3 mức zoom. Không nghiệm thu paid provider hoặc tính test model/SQL opt-in bị skip là đạt. [Log, phạm vi và giới hạn](TRIEN_KHAI_VIDEO_NGAN_VEO.md).

### Composer và thư viện video ngắn — 2026-09-11

Restore, `npm ci`, frontend build, Release solution build ở thư mục riêng và Debug/Release desktop build đều đạt. Frontend **189 Passed / 0 Failed / 0 Skipped**. .NET được chạy hết qua hai filter bổ sung nhau: nhóm ngoài Vietsub **935 Passed / 0 Failed / 2 Skipped**; Vietsub **293 Passed / 0 Failed / 3 Skipped**; tổng **1228 Passed / 0 Failed / 5 Skipped**. Không tính lượt all-in-one bị abort là Passed: lượt đầu ổ C chỉ còn ~90 MB làm 22 test fail, lượt tiếp theo test host hết tài nguyên/crash; dùng TEMP/TMP riêng trên D và tắt song song collection cho hai lượt nhóm cuối. Một lượt browser trùng lúc `npm ci` đã được chạy lại sau khi cài dependency xong và đạt. Frontend giới hạn 2 worker vì lượt mặc định thiếu bộ nhớ.

Thêm kiểm persistence/scope/role/hash/version/draft/idempotent project; React entry/upload/cancel/quote/approval/stale/access; WebView2 thật với fixture tự tạo, không mạng/provider, ở 4 kích thước cửa sổ × 3 mức zoom. Bundle Debug/Release đã so hash với production frontend. Log, screenshot và hướng dẫn: [biên bản UI/thư viện](TRIEN_KHAI_UI_THU_VIEN_VIDEO_NGAN.md). Chưa nghiệm thu generation provider thật, model opt-in hoặc ảnh người dùng sau đăng nhập.

### Video ngắn phối trang phục — 2026-09-10

Bổ sung sau khi bật Development: sửa lối vào phối trang phục trên trang Video ngắn, thêm 3 test tích hợp App/popup/điều hướng tới hai ô chọn ảnh. Lượt cuối: frontend **183 Passed / 0 Failed / 0 Skipped**, .NET **1221 Passed / 0 Failed / 5 Skipped**; build Release toàn solution và Debug desktop đạt, bundle mới đã có ở cả hai bản. Không phát sinh yêu cầu provider thật trong kiểm tra này.

Release restore/build đạt; .NET **1221 Passed / 0 Failed / 5 Skipped**, frontend **180 Passed / 0 Failed / 0 Skipped**. Thêm kiểm hai ảnh/edit HTTP, first-frame, quote/Unknown/idempotency, quyền/rate/budget, thay revision/hash, duyệt im lặng và render/export stale. Một timeout dry-run worker dịch ở lượt trước đã qua khi chạy riêng và toàn bộ lượt cuối; không sửa test timeout. Bổ sung triển khai Development 2026-09-10: backup/restore thật, CHECKDB, migration hai lần trên clone, schema/quyền sau áp trên `DUNGDEV / VideoFactory` và smoke HTTPS đều đạt. Lượt triển khai chỉ đổi cấu hình máy và áp SQL đã kiểm, không chạy lại bộ test source; các số test trên thuộc lần kiểm source trước. WebView2 nhập ảnh, SQL race, ảnh/video thật và các model opt-in chưa được nghiệm thu. [Lệnh, giới hạn và checklist chạy thật](TRIEN_KHAI_VIDEO_NGAN_NHAN_VAT_TRANG_PHUC.md).

### Hợp nhất local-2 và main — xác minh 2026-09-10

Release build/restore toàn solution, `npm ci` và production build đạt. Source hợp nhất được kiểm tra ở checkout riêng: .NET **1185 Passed / 0 Failed / 5 Skipped**, frontend **174 Passed / 0 Failed / 0 Skipped**, TikTok Admin state/browser **25 Passed / 0 Failed / 0 Skipped**, smoke popup ngắn/dài đạt. Các bài model thật và SQL opt-in bị skip không được tính là đã nghiệm thu. Phạm vi sửa, lệnh chạy, lỗi phát hiện trong quá trình hợp nhất và artifact: [biên bản hợp nhất 2026-09-10](HOP_NHAT_LOCAL_2_MAIN_20260910.md).

### Veo local voice — kiểm thử bổ sung 2026-09-09

TOOL-TESTS/LocalVoice, các test local policy trong ProjectRenderServiceTests, và Web/src/features/localVoice/*.test.ts bao phủ policy, access, stale/hash, checkpoint, cancel/retry/idempotency, duyệt riêng, native exception, cleanup và FFmpeg remux. Fake model/media chỉ chứng minh điều phối, không chứng minh chất lượng tiếng Việt.

LocalVoiceModelTests mặc định **Skipped**, chỉ chạy khi có VM_LOCAL_VOICE_COMPONENT_ROOT, VM_LOCAL_VOICE_SMOKE_SOURCE và VM_LOCAL_VOICE_SMOKE_ANCHOR. Hai sample phải có quyền sử dụng. Technical smoke kiểm nạp model, VAD/separation/conversion, audible/duration, video packet hash và cache retry; không chấm khẩu hình hoặc nhận diện giọng bằng tai.

Bổ sung triển khai 2026-09-10: `LocalVoiceDeploymentTests` kiểm cấu hình component/temp độc lập media workspace, không tự cài khi đọc trạng thái, từ chối đường dẫn không hợp lệ và lọc môi trường tiến trình con. Test model ghi thời gian và peak working set từng worker; số đo là tiến trình Python, không phải tổng RAM cả ứng dụng. Chạy test .NET với TEMP/TMP ngắn (ví dụ `D:\vmt-voice-0910`) để tránh SQLite lỗi đường dẫn dài trên Windows. Có thể dùng Python của component để chạy `TOOL-TESTS/LocalVoice/test_voice_consistency_worker.py`; không cần Python hệ thống. Helper có regression dùng tensor nhỏ, chặn `_native_multi_head_attention` và xác minh CPU profile vẫn xử lý attention được; tái hiện nhánh gây access violation khi tách âm trước bản sửa. Case này cần PyTorch, không tải model khi test. Bằng chứng sau thay đổi: [task triển khai trên máy đích](TASK_TRIEN_KHAI_VEO_LOCAL_SU_DUNG_THUC_TE.md).

Trước production cần rehearsal migration trên clone, UI WebView2 thật, 2–3 clip Veo tiếng Việt cùng nhân vật, giọng nam/nữ, âm nền, lỗi/no-speech/multiple-speaker, restart/retry, nghe A/B và đo CPU/RAM/thời gian. Thiếu clip thật hoặc model test bị skip phải ghi chưa nghiệm thu. Module và test chuyên biệt cloud LipSync đã xóa; migration lịch sử được giữ nguyên. Không tính test đã loại bỏ vào Passed hoặc Skipped.

> Ma trận kiểm thử và Definition of Done. Dependency source/migration được rà soát tại commit `8f10cc9` ngày 2026-09-15; các biên bản cũ giữ nguyên thời điểm gốc.

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
| Video ngắn Veo | `TextOnly`/`CharacterOutfit`, quote ảnh và clip tách riêng, first frame Approved/current, revision/input hash, policy `LongForm`, 4/6/8 giây, 9:16/16:9, chuyển Kling cũ có xác nhận, `Unknown` không submit lại, clip/tắt audio/render/export lineage |
| Credential | Permission matrix, encrypt/decrypt, rotate version, redaction và task dùng version cũ |
| Migration | Static migration tests, apply trên clone, chạy lại idempotency, query verify và restore rehearsal |
| WebView bridge | TypeScript/C# contract, invalid message, busy/cancel/reconnect, organization/project switching |
| Media/download/render | Path traversal, `.part`, MIME/signature/size/hash, FFprobe, ApprovedGenerationId và FFmpeg integration |
| Canonical Voice/speech | Feature flag, voice catalog/alias/preview context, pacing, TTS/ASR cost gate, WAV validation, approval/lineage, mix/render và audited review |
| Vietsub/OCR | Manifest/SQLite/revision/lock, path safety, cue/source/language, cancel/retry và atomic SRT |
| System Setup startup | Dashboard xuất hiện trước modal, `inert`/focus/không đóng bằng Esc, role/context, host C# chặn command, checksum/probe từng component, install/repair/retry/cancel và Windows sạch |
| Translation runtime | Worker safety/protocol/readiness tests; model integration và benchmark opt-in; desktop smoke |
| Local voice | Phrase/cache/revision, worker protocol, WAV/hash/path, fit 1.20x, FFmpeg timeline, playback authorization; runtime/model thật và nghe smoke là opt-in |
| TikTok | OAuth state/PKCE/user-device binding, app credential Pending/Active và mã hóa theo ID, Global Admin authorization/audit, token encryption, creator policy, idempotency, chunk/Range, exact host, path/URL redaction, recovery và polling |
| Bilibili | URL/host public, quét video/kênh phân trang và kết quả chưa đầy đủ, chọn đúng job, hủy/thử lại, tool pinned checksum, `.part`/signature/hash/FFprobe/không ghi đè; smoke mạng public có giới hạn nguồn |
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

Với hai migration 4.1.8 của TikTok và LocalVoice, xác minh **hai mã `ai.SchemaVersions` riêng**, account-bound OAuth/attempt/history snapshot và cột/policy/lineage voice local. Không suy từ một mã 4.1.8 rằng module kia đã áp. Không xóa migration 4.1.6/4.1.7 lip-sync Cloud lịch sử chỉ vì runtime hiện hành đã loại bỏ.

Với migration 4.1.9, xác minh mã `4.1.9-short-video-outfit`, `vf.ShortVideoOutfits`, `vf.ShortVideoOperations`, index/CHECK/FK, DENY CRUD cho `VideoMakerDesktopRole` và quote `TextOnly` qua server API khi cờ `CharacterOutfit=false`. Binary hiện hành vẫn cần bảng quote dù mode phối đồ tắt; kết quả test static `ShortVideoOutfitMigrationTests` không thay apply hai lần/verify trên clone và schema môi trường đích.

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
- `OnCameraDialogue` Canonical Voice phải qua duyệt WAV, tạo video nền, ghép và duyệt clip trước render. Kiểm thử project cũ mang trạng thái chờ với WAV đã duyệt, chưa duyệt và lời thoại thay đổi; đọc dashboard không được ghi dữ liệu. Retry dùng lại WAV/provider request còn hợp lệ, không submit mới chỉ vì lỗi ghép local.
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

### Tạo giọng local theo giọng đã chọn

Luồng chọn giọng cần kiểm cả 15 ID đã pin, job snapshot engine/model/version/voice, chặn đổi khi có job active và loại timeline khác voice khỏi preview/export. Modal chỉ cho tạo khi giọng đang chọn có model đã xác minh và runtime đã probe; trạng thái file `READY` riêng không đủ. Bài `KokoroFixture_UsesSelectedVoicepackAndProducesVietnamesePcmWav` là opt-in và cần workspace có model/runtime Kokoro đã cài/probe trước: `VIDEOMAKER_RUN_LOCAL_VOICE_TESTS=1` cùng `VIDEOMAKER_VOICE_WORKSPACE_ROOT`, tùy chọn `VIDEOMAKER_KOKORO_TEST_VOICE_ID` cho một voice ID đã pin, chạy riêng với filter tên bài Kokoro. Script `Verify-VietsubVoiceModel.ps1` giữ phạm vi Piper. Bài bị `Skipped` không xác minh được chất lượng giọng. Trước phát hành cần benchmark CPU, nghe hai giọng khác nhau để xác nhận không dùng nhầm voicepack, smoke WebView2/MP4 trên máy sạch và rà soát quyền voicepack/preview WAV.

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
# Admin shell: tổng quan, tìm kiếm, phân trang, dialog và các màn hình tổ chức.
node --test TOOL-TESTS/Admin/admin-shell.browser.test.cjs
# Cần npm ci tại TOOL-LOCAL/Web; render React hiện hành bằng Rolldown, dữ liệu giả lập.
node --test TOOL-TESTS/TikTok/desktop-tiktok.browser.test.cjs
```

Bộ trình duyệt chặn network, không khởi động server và không chạm database/TikTok. Phạm vi gồm xác nhận trước khi mở phiên xác minh, xóa secret khỏi form, chặn gửi lặp, giữ bản nháp khi làm mới, phát hiện cài đặt server thay đổi, xử lý lỗi, polling/hết hạn/rời trang, đăng xuất, xác nhận công khai và bố cục 375–1440 px. Có thể đặt `VIDEOMAKER_SCREENSHOT_DIR` để lưu ảnh kiểm tra desktop/mobile. Đây là kiểm tra UI với API giả lập; không thay thế nghiệm thu OAuth thật.

Bộ Admin shell kiểm tra thêm Tổng quan dùng số liệu API, hiển thị thiếu dữ liệu khác số 0, escape tên người dùng, tìm kiếm/phân trang/chi tiết, dialog, menu mobile/đăng xuất và focus bàn phím. Bố cục vuông không viền được kiểm tại 390/768/1024/1440/1920 px; các màn hình thiết lập, danh sách, pool, pricing, hướng dẫn chi phí và năm tab chi tiết tổ chức được kiểm ở mobile/desktop. Kết quả theo checkout và ảnh giao diện: [triển khai Admin Web 2026-09-23](PLAN_TASK_ADMIN_WEB_VUONG_KHONG_VIEN.md).

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

Khi thay **chỉ Markdown**, kiểm `git diff --check`, đường link/file được dẫn, phiên bản migration và tên cờ/DTO theo source; báo rõ build/test/migration/smoke **không chạy**. Không chèn số Passed mới hoặc nâng mức readiness/rollout vì một lượt sửa context.

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

## 26. Hợp nhất `local-2` với GitHub — 2026-09-15

Checkout hợp nhất từ commit local `1333ab2` và remote `dc6cdc3` trên nhánh tạm. Hai phía trùng 11 file; Git báo conflict ở 5 file. Giữ đồng thời gate Setup trong `Program`/`Form1`/React và composition TikTok/giọng local từ GitHub, thêm các asset cần thiết vào project file; fixture Setup được bổ sung `tikTokEnabled` theo contract frontend hiện hành. Không chọn toàn bộ một phía cho file conflict, không xóa migration/license/provenance và không chạy SQL.

- `dotnet restore TOOL_GEN_POST_VIDEO.slnx`: **Passed**. Build solution Release `--no-restore`: **Passed**, 0 warning / 0 error.
- `npm ci --no-audit --no-fund`, `npm run build`: **Passed**, còn cảnh báo chunk >500 kB. `npm test`: **187 Passed / 0 Failed / 0 Skipped** trong 34 file.
- .NET Release `--no-build -- xUnit.ParallelizeTestCollections=false`: **1.242 Passed / 0 Failed / 6 Skipped / 1.248 Total**, 3 phút 52 giây. Các bài Skipped gồm model/runtime Qwen/Piper/LocalVoice thật và SQL TikTok opt-in; không coi là nghiệm thu model, database hay TikTok production.
- `git diff origin/local-2 --check`: **Passed** cho phần nội dung được bổ sung/hợp nhất. `git diff --cached --check` trên toàn merge vẫn báo whitespace đã có trong các file `.codex/skills/ui-ux-pro-max` từ remote; không sửa các asset đó chỉ để làm sạch kết quả. Kiểm tra phần ngoài `.codex` không báo lỗi whitespace.

Chưa chạy UI automation/WebView2 trực tiếp, đăng TikTok, provider có phí, migration trên database thật hoặc publish release. Build và test trên checkout hợp nhất không tự chứng minh cấu hình/runtime/schema của máy đích đã sẵn sàng.

## 27. Piper offline trong ZIP — 2026-09-26

Rà soát nhánh `main`, commit nền `bc1c6aec3c731bdb0edcebbde17ae13640245e52` cùng working tree chưa commit. Kết quả dưới đây gắn với source/binary manifest của chuỗi kiểm thử, không gán cho commit nền. Chi tiết, hash artifact và lịch sử điều tra: [báo cáo Piper offline](TRIEN_KHAI_PIPER_OFFLINE_TRONG_ZIP.md).

- Restore và build solution Release: **Passed**, MSBuild 0 warning / 0 error. `npm ci --no-audit --no-fund`, `npm run build`: **Passed**; Vite còn cảnh báo chunk >500 kB.
- Full C# chạy riêng ba lượt liên tiếp: **1.570 Passed / 0 Failed / 13 Skipped mỗi lượt**. Chuỗi `D:\vmpip\20260926-202444-Full-0e131a`, thời gian 241,01 / 237,21 / 235,96 giây, không còn tiến trình con.
- Frontend đầy đủ, đổi seed qua ba lượt: **280 Passed / 0 Failed / 0 Skipped mỗi lượt**, 44 file. Chuỗi `D:\vmpip\20260926-201907-Frontend-370653`.
- Kiểm bundle/Startup/gate C#: **58 Passed / 0 Failed / 0 Skipped mỗi lượt trong 20 lượt**, chuỗi `D:\vmpip\20260926-203647-SetupOffline-15dc5c`. Ba file Startup/Settings frontend: **23 Passed / 0 Failed / 0 Skipped mỗi lượt trong 20 lượt**, chuỗi `D:\vmpip\20260926-201955-Frontend-05621a`.
- Piper offline thật: **1 Passed / 0 Failed / 0 Skipped mỗi lượt trong ba lượt**, chuỗi `D:\vmpip\20260926-201421-Piper-fcbc8f`. Mỗi lượt có root/cache mới, đường dẫn có dấu, hủy/thử lại, cài sửa sau khi module bị sửa, giữ runtime v2, WAV có tín hiệu và năm lần mở lại. Không còn tiến trình con; không dùng cache cũ để chứng minh cài mới.
- ZIP chẩn đoán 624.595.757 byte được publish với cấu hình rỗng, giải nén và kiểm inventory: 50 thành phần bắt buộc, 24 web asset, 0 thiếu/sai hash; OCR, FFmpeg, WebView2 đạt. Piper cài và kiểm lại năm lần khi thư mục ứng dụng chỉ đọc, runtime ở ổ khác. WAV proof ghép vào MP4 H.264/AAC đạt qua FFmpeg/FFprobe đi kèm.
- Đo riêng bằng CLI sau các suite: cài mới **42,38 giây**, kiểm lại **4,12 giây**, runtime/cache/venv sau cài **493.310.074 byte**. Đây là số đo trên máy hiện tại, không gồm startup có đăng nhập; báo cáo có giới hạn phép đo.
- Kiểm tra cuối: source/binary manifest của năm chuỗi giống nhau và vẫn khớp checkout/output; hash payload, worker, lockfile đạt; `git diff --check` đạt. Bằng chứng tổng hợp `D:\vmtest\piper-offline-20260926\acceptance-final.json`.

Lượt Full đầu chạy đồng thời với frontend có **1.569 Passed / 1 Failed / 13 Skipped**: bài `Cpu_backend_dry_run_reports_selected_avx_and_native_hash` vượt load timeout 5 giây. Bài này chạy riêng năm lượt đều đạt; ba lượt Full chạy sau khi frontend kết thúc cũng đạt. Giữ TRX/log của lượt lỗi; không tăng timeout, bỏ assertion hoặc sửa runtime Qwen. Chưa đủ bằng chứng để khẳng định nguyên nhân timeout.

13 bài Skipped trong Full là các bài opt-in SQL/model/GPU/benchmark/voice; không tính là model hoặc database đã nghiệm thu. Piper offline có chuỗi thật riêng như trên. Chưa xác minh Windows sạch/máy hoặc tài khoản thứ hai, startup bằng auth/license thật, nghe và xuất video qua UI, hoặc packet capture cho toàn cây tiến trình. Không publish/upload release, chạy migration, gọi provider có phí hay sửa dữ liệu project người dùng; `TOOL-LOCAL/appsettings.json` được giữ nguyên. Hồ sơ phân phối dependency còn các mục cần rà soát trong báo cáo trước public release.
