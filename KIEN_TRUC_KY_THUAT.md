# Kiến trúc kỹ thuật VideoMaker

> Mô tả ranh giới module và đường gọi theo source ngày 2026-09-07.

## 1. Tổng thể

```text
React/WebView2 -> WinForms desktop -> TOOL-SERVER API -> AI providers
       |                |                  |               |
       |                |                  +-> SQL Server  +-> OpenAI
       |                +-> workspace                     +-> Kling
       |                +-> FFmpeg/FFprobe                 +-> BytePlus
       |                +-> OCR                            +-> Fal/Veo
       |                +-> translation worker x64
       +-> UI state/commands
```

Desktop không gọi AI provider trực tiếp. Server là trust boundary cho auth, license, tổ chức, pricing, budget, credential, request log, worker và output proxy. Xử lý media, subtitle và translation model diễn ra local.

## 2. Module

| Project/thư mục | Trách nhiệm chính |
|---|---|
| `TOOL-SERVER` | ASP.NET Core API, Razor Admin, auth/license, tổ chức, AI governance, provider adapters, hosted workers, output cache/proxy, release API |
| `TOOL-LOCAL` | WinForms host, React/WebView2 UI, gateway client, workflow project, workspace, download/verify/render và Vietsub |
| `TOOL-SHARED.Contracts` | Request/response DTO và enum public giữa server/desktop |
| `TOOL-VIETSUB-TRANSLATION-WORKER` | Tiến trình `net10.0-windows` x64 chạy LLamaSharp/Qwen qua IPC local |
| `TOOL-DISTRIBUTION` | Manifest, provenance và kiểm tra SHA-256 bundle |
| `TOOL-TESTS` | xUnit xuyên module |
| `TOOL-UPDATER` | Download/update có backup và rollback |
| `TOOL-SETUP` | Launcher/bộ cài desktop |
| `database` | Initial schema, migration tuần tự, verify và desktop least-privilege role |
| `scripts` | Kiểm tra runtime assets, model, bundle, publish và test opt-in |

`TOOL-SERVER`, `TOOL-LOCAL`, `TOOL-SHARED.Contracts`, `TOOL-TESTS` và `database` có `AGENTS.md` cục bộ; quy tắc gần nhất áp dụng cùng file root.

## 3. Trust boundary và quyền sở hữu dữ liệu

### Server sở hữu

- user, session, device, license và refresh token metadata;
- organization, membership, role, budget và member limit;
- provider catalog, model, pricing/rate snapshot và policy;
- encrypted credential cùng version/lifecycle;
- AI request, provider request, idempotency, reservation và usage ledger;
- project registry/server workflow trong SQL;
- Vietsub project registry metadata;
- output cache và release metadata.

### Desktop sở hữu

- UI state và selection hiện hành;
- workspace media, file `.part`, thumbnail/waveform và render output;
- manifest/SQLite/SRT/OCR artifact của Vietsub;
- cấu hình máy phát triển không chứa provider secret.

Desktop còn kết nối SQL trực tiếp cho workflow schema `vf` trong giai đoạn chuyển tiếp. Nó không được có quyền đọc/ghi bảng sự thật `auth`, `ai`, credential/usage hoặc schema `vs`.

## 4. Server

### 4.1 Khởi động và dependency injection

`Program.cs` đăng ký authentication/authorization, rate limiting, Data Protection, các DbContext, service nghiệp vụ, provider HTTP client, output proxy/cache và hosted worker. Startup cũng bootstrap catalog cần thiết, nên không chạy server trước khi database đúng phiên bản và cấu hình môi trường đã sẵn sàng.

Data Protection key được lưu trong database để credential đã mã hóa tiếp tục giải mã được giữa restart/instance. Việc mất key ring làm mất khả năng dùng credential; key ring phải nằm trong backup/restore plan.

### 4.2 DbContext và schema

Server tách phạm vi dữ liệu bằng các context chính:

- `AccountDbContext`: tài khoản, session, device, license và payment-related account state.
- `DataProtectionKeyDbContext`: key ring ASP.NET Core Data Protection.
- `AiGovernanceDbContext`: organization, membership, provider catalog, pricing, budget, credential và usage.
- `ProviderAdminDbContext`: thao tác quản trị provider/catalog.
- `VideoFactoryDbContext`: project, scene, asset và generation workflow.
- `VietsubDbContext`: registry `vs.Projects`, job/batch/attempt Cloud và payload/result tạm được mã hóa; không thay database biên tập local.

Database dùng các schema nghiệp vụ `auth`, `ai`, `vf`, `vs` cùng các bảng cần thiết trong `dbo`. Ranh giới DbContext là ranh giới ownership trong code, không thay thế quyền SQL và transaction thích hợp.

### 4.3 Nhóm API

Các nhóm endpoint chính gồm:

- auth, session, device, password reset và license lease;
- organization selection, member/role, budget và usage;
- Global Admin/Admin cho provider model, rate, credential, pool và release;
- project/content/character/asset/scene generation;
- video submit/status/retry/approve và output proxy;
- SePay payment order/webhook/status;
- Vietsub registry metadata.

Contract public nằm ở `TOOL-SHARED.Contracts`; thay contract phải cập nhật server, desktop và test cùng lúc.

### 4.4 Hosted worker

Server có các background worker cho request/provider polling, settlement/release, output caching/cleanup và các quy trình nền liên quan. Worker dùng claim lease để nhiều instance không xử lý cùng bản ghi, có giới hạn attempt/age và chỉ chuyển trạng thái tiến tới terminal.

Task provider tiếp tục chạy sau khi desktop đóng. Desktop reconnect bằng status API/idempotency thay vì gửi lại request mới tùy tiện.

## 5. AI Gateway

### 5.1 Chuỗi kiểm tra chung

```text
HTTP request
  -> JWT/session/device/license
  -> organization membership + role
  -> project ownership
  -> idempotency
  -> model/policy + Active credential
  -> rate snapshot + budget reservation (Serializable)
  -> provider outbound HTTPS
  -> request snapshot/provider id
  -> poll/cache
  -> settlement hoặc release
```

Mọi nhánh lỗi trước outbound phải không tạo chi phí. Lỗi sau reservation phải đi qua trạng thái/recovery có thể kiểm toán.

### 5.2 Provider adapter

- OpenAI dùng cho content có schema, image, Canonical Voice TTS và transcription ở workflow được phép.
- Kling là video provider mặc định.
- BytePlus Seedance có adapter riêng, catalog mặc định tắt.
- Fal/Veo có adapter riêng, catalog mặc định tắt và chỉ nhận first frame hợp lệ cho `LongForm`.

Provider/model được chọn bởi project snapshot và organization policy trên server, không bởi desktop. Không có failover provider ngầm.

Outbound provider chỉ cho phép HTTPS đến:

- `api.openai.com:443`
- `api-singapore.klingai.com:443`
- `ark.ap-southeast.bytepluses.com:443`
- `queue.fal.run:443`
- `api.fal.ai:443` chỉ cho credential test theo resolver hiện hành

Output URL dùng allowlist riêng theo provider và vẫn qua DNS/redirect/MIME/size validation.

### 5.3 Output cache/proxy

Server nhận URL output gốc, xác minh provider/host/scheme/DNS, giới hạn redirect và tải về cache. Desktop chỉ nhận đường proxy tương đối có authorization. Cache có retention, giới hạn file và tổng dung lượng; cleanup không thay thế ledger hoặc provider request history.

## 6. Desktop

### 6.1 Composition

WinForms là process host. WebView2 tải React production bundle và trao đổi message với C# bridge. C# giữ gateway client, workflow service, workspace/media service, download/verification và command điều phối.

Bridge là contract hai phía: TypeScript message, C# DTO/handler, validation và busy/error state phải thay đổi đồng bộ. Không tin path, organization id, project id hoặc trạng thái gửi từ JavaScript nếu chưa xác minh ở C#/server.

### 6.2 Workspace và media

Workspace mặc định dưới `%LOCALAPPDATA%\ToolGenPostVideo\workspace`. Mọi path lưu trong database/manifest nên là relative path đã chuẩn hóa và phải resolve bên trong root.

Download đi qua `.part`, sau đó kiểm tra HTTP metadata, file signature, size/hash và FFprobe trước rename atomically. Render dùng FFmpeg/FFprobe từ bundle đã được phân phối và chỉ nhận generation đã Approved/current.

### 6.3 Workflow generation

- **Content:** desktop gửi request contract đến server, theo dõi trạng thái và đồng bộ kết quả có cấu trúc vào project.
- **Character/image:** server kiểm tra ownership/policy/budget, gọi OpenAI và trả asset qua kênh kiểm soát.
- **Video:** server submit/poll/cache; desktop download, verify, preview và approve/reject.
- **Fal first frame:** scene cần first frame Approved/current trước submit video.
- **Canonical Voice:** server sở hữu catalog/profile/version, quote và TTS; desktop tải/kiểm WAV, giữ speech/voice snapshot và mix vào video nền.
- **Provider Native:** video dài kiểm tra audio kỹ thuật rồi nghe/duyệt trực tiếp, không gọi ASR; speech verification là workflow độc lập ngoài video dài khi được bật.
- **Render:** desktop tái xác minh approved generation và media trước ghép.

Narrated asset mới dùng policy `scene-audio-sync-v3`. Tương thích `v2` chỉ áp dụng cho exact approved pointer còn khớp generation, VoiceGeneration, speech/voice snapshot và hash; `OnCameraDialogue` Canonical Voice dừng ở `SpeechReadyForLipSync` khi chưa có engine lip-sync.

## 7. Vietsub

Cloud translation có đường riêng: `vietsub.job.translate.cloud` → desktop snapshot/`TRANSLATE_CLOUD` → API `cloud-translation` → worker server → OpenAI Responses. Server giữ model/credential/rate/prompt snapshot; SQL application lock và lease bảo vệ dispatch. Mỗi batch reserve vào `VietsubProjectId`, giữ FK project video cũ bằng CHECK đúng một loại project. Result + trạng thái được lưu trước settlement; Unknown không tự retry/release. Desktop lấy lại cùng operation ID, CAS từng cue với receipt/checkpoint trong SQLite rồi ghi SRT atomically; provenance `CLOUD_AUTO` được xử lý như bản dịch tự động khi nguồn bị sửa. Qwen worker không tham chiếu Cloud. Xem [runbook Cloud](HUONG_DAN_VAN_HANH_DICH_CLOUD_VIETSUB.md).

### 7.1 Lưu trữ

Mỗi project Vietsub có manifest JSON schema 7 (nâng từ 6 với lật hình mặc định tắt), SQLite `project.db` schema 6 độc lập, media và artifact local. `subtitleStyle`, `audioMixSettings` và `videoTransformSettings` nằm trong manifest; style được validate theo font/color/range allowlist, mixer giới hạn âm gốc `0–1` và giọng dịch `0–1.5`, với migration mặc định `0.25/1.0` và auto-duck bật. React ghi đồng thời style/mixer/lật hình qua message `vietsub.subtitle.style.update`; preview chỉ đặt `scale(-1,1)`/`scale(1,-1)` lên `<video>`, không đặt lên lớp phụ đề hoặc âm thanh; content box vẫn tính đúng khi có letterbox. Preview phát audio engine giọng ẩn cùng playhead và áp gain/mute/duck theo bản nháp. Bộ dựng ASS chỉ nhận model style đã normalize, ánh xạ cùng anchor/tọa độ, cân dòng theo mục tiêu và trung hòa override tag trong cue; không nhận font path hoặc tham số FFmpeg từ DOM. `vietsub.video.export` chỉ nhận đích từ `SaveFileDialog` native; service xác thực user/license/organization/project, hash video, revision track, style, mixer, lật hình và SHA-256 timeline giọng trước khi render. FFmpeg dùng `hflip`/`vflip` trước `subtitles`, trộn stem gốc/giọng, dùng sidechain khi auto-duck bật, limiter trước AAC, ghi MP4 `.partial`, probe đúng trạng thái có/không audio rồi mới publish; thay thiết lập lật trong lúc render khiến snapshot fail closed. Registry server dùng metadata để tìm/chọn project; riêng job Cloud giữ snapshot text tạm mã hóa. Không được cấp SQL trực tiếp từ desktop vào schema `vs`.

Editor phụ đề điều khiển cuộn tại `useCueListFollow`: chỉ cuộn container danh sách, giữ một đích mới nhất, tạm giữ card khi người dùng cuộn/nhập/menu và tiếp tục sau hai giây rảnh khi đang phát. Row được memo hóa, key theo track + cue và giữ draft qua revision; project được remount theo project ID. Phản hồi trang chỉ được áp dụng khi khớp request mới nhất. Banner dùng `VietsubNotice`; `noticeEvents` là metadata riêng của state frontend để phân biệt một lần lỗi mới với poll cùng sự kiện, không đổi DTO native/server hoặc trạng thái nghiệp vụ. Thao tác giọng trong editor chỉ gửi một cue ID, flush draft trước và lấy revision mới nhất từ page/track summary.

### 7.2 OCR

Paddle OCR chạy local và tạo track/cue có source, language và revision. Translation CTA chỉ tạo job khi active track là `PADDLE_OCR_LOCAL`, ngôn ngữ hỗ trợ và có cue hợp lệ.

### 7.3 Translation worker

Worker x64 riêng tải LLamaSharp/native runtime và Qwen đã pin. Giao tiếp là IPC local với protocol giới hạn; worker không tham chiếu Cloud provider client, secret hoặc workflow database.

Runtime có profile Standard (8 GB tổng/4 GB RAM trống/6 GB commit) và Low-memory (6 GB tổng/3 GB RAM trống/4 GB commit). Low-memory giảm batch, thread và kích thước scene nhưng giữ context window 4096; worker xác minh lại profile/ngưỡng theo allowlist. Các ngưỡng này là mức khuyến nghị: native trả resource warning, yêu cầu xác nhận rõ ràng rồi snapshot admission vào job/load request trước khi cả desktop client và worker cho phép nạp model. Platform, disk, checksum/probe và lỗi nạp/OOM thực tế không thể được xác nhận để bỏ qua. Các profile vẫn phải qua benchmark phần cứng đích trước rollout.

Readiness marker gắn fingerprint của model, worker, protocol, resource profile, config, backend và native runtime. Probe cần xác minh runtime cùng input English/Chinese; chỉ có file model không đủ để báo Ready. Thiếu RAM không làm mất READY của model đã probe nhưng runtime status phải trả cảnh báo riêng. Profile và resource admission được snapshot vào job; cache vẫn tách theo profile, còn apply dùng revision/lock check và ghi SRT atomically.

### 7.4 Tạo giọng local

Desktop điều phối Piper CPU qua Python worker JSONL cô lập. Component store chỉ tải từ HTTPS host allowlist, kiểm tra đúng size/SHA-256 của `uv`, model và config trước khi ghi marker `READY`; không dùng ngưỡng RAM cứng để chặn cài hoặc chạy.

Job snapshot track/revision/cấu hình, gom cue thành phrase ổn định, tái dùng cache theo fingerprint và checkpoint sau từng phrase. Mỗi WAV phải là RIFF/PCM hợp lệ và được ghi atomically. Bộ dựng timeline trim silence, mượn khoảng trống kế tiếp và chỉ dùng `atempo` đến `1.20x`; vượt ngưỡng vẫn publish timeline, giữ timing diagnostic không chặn và nới đuôi timeline để không cắt câu cuối. React hiển thị output này bằng track **Giọng Việt** trên cùng time scale với video/phụ đề và dùng audio engine ẩn đồng bộ với master video, không dùng native audio controls độc lập. Thao tác chỉ nới `end` sẽ tạo bản ghi timeline revision mới trỏ tới đúng WAV/SHA-256 đã xác minh. Với các chỉnh sửa timing khác hoặc project đã lệch nhiều revision, service xác minh lại fingerprint/cue coverage/SHA-256 của toàn bộ phrase cache rồi dùng FFmpeg dựng timeline theo timing hiện hành mà không gọi Piper; cache không tương thích vẫn fail closed.

Timeline được trộn bằng FFmpeg theo từng stem giới hạn số phrase. WebView chỉ phát qua virtual host nội bộ với registry của project/track/revision hiện hành và xác minh lại kích thước/SHA-256; UI không nhận đường dẫn filesystem. Job dịch và job giọng dùng cùng local job manager nên không chạy đồng thời trên máy.

Mốc đầu câu bỏ qua được dùng làm mục tiêu fit, kể cả khi cue chồng thời gian; trường nội bộ `HardEndMilliseconds` không còn là giới hạn cắt âm. WAV inspector giữ riêng mốc cuối vượt ngưỡng `max(400, peak * 0.02)` của PCM16 trước khi thêm đệm. Renderer chỉ rút phần đuôi sau mốc này cộng 5 ms, tối đa 120 ms tính trên WAV trước `atempo`; fade tối đa 5 ms cũng chỉ nằm trong phần đuôi đó. Metadata trim chỉ dùng cho lần render, phrase/cache hash giữ nguyên. Nếu vẫn tràn, renderer trả diagnostic `REVIEW_REQUIRED` nhưng tiếp tục dựng timeline, không throw `VOICE_SKIPPED_CUE_OVERLAP` hoặc dùng `atrim` cắt ở đầu câu bỏ qua. Tạo mới và phục hồi cache dùng chung renderer này. Bước `VOICE_TIMELINE` vẫn ghi checkpoint 72% để lỗi render thật giữ đúng tiến độ; thành công publish ở 100%. Sau `adelay`, `asetpts=N/SR/TB` dựng lại timestamp từ mẫu âm thanh để không mất đoạn im lặng đầu do frame thiếu PTS; limiter bù latency để giữ đúng timing.

Sau trộn stem, timestamp cũng được dựng lại; `apad=whole_len` và `atrim=end_sample` giới hạn đầu ra theo số mẫu 48 kHz. Cách này tránh việc padding tiếp tục ghi WAV khi tổ hợp filter truyền timestamp thiếu hoặc không hợp lệ.

## 8. Cấu hình mặc định cần biết

- Server cache output: `data/video-outputs`, retention 48 giờ, tối đa 1 GiB/file và 20 GiB tổng theo config hiện hành.
- Video polling: tối đa 3000 attempt, 72 giờ tuổi và claim lease 35 phút theo config hiện hành.
- SePay: tắt mặc định.
- Desktop server URL: `https://localhost:7202/`.
- Desktop updater: bật, channel `Stable`, platform `win-x64`.
- Canonical Voice/speech verification: tắt ở server; Speech Synchronization: tắt ở desktop.
- Vietsub/OCR: bật; translation local: tắt; local voice UI/cài đặt: bật nhưng runtime thiếu component trả `NOT_INSTALLED`.

Giá, credential, bank account và production connection string không nằm trong tài liệu hoặc source commit; chúng phải được cấu hình theo môi trường.

## 9. Điểm nóng bảo trì

Các file orchestration/UI lớn cần thay đổi có kiểm soát và test hẹp trước full suite, đặc biệt `TOOL-LOCAL/Web/src/App.tsx`, generation service server/desktop và Vietsub WebView bridge. Khi tách nhỏ, giữ nguyên contract, transaction boundary, cancellation, idempotency và state machine; không xóa code legacy chỉ vì không thấy call site trực tiếp.

Quy tắc nghiệp vụ chi tiết nằm tại [NGHIEP_VU_HE_THONG_VIDEOMAKER.md](NGHIEP_VU_HE_THONG_VIDEOMAKER.md); trạng thái rollout nằm tại [BOI_CANH_HE_THONG_HIEN_HANH.md](BOI_CANH_HE_THONG_HIEN_HANH.md).
