# Kế hoạch task: TOOL-LOCAL chạy ổn định trên máy mới

Ngày lập: 2026-09-18. Trạng thái: **kế hoạch, chưa triển khai code**.

**Cập nhật sau yêu cầu triển khai:** phần bên dưới giữ nguyên bản kế hoạch ban đầu. Nhóm sửa desktop đã có source và được ghi riêng trong [biên bản triển khai](TRIEN_KHAI_TOOL_LOCAL_TREN_MAY_MOI.md); chưa hoàn tất toàn bộ 24 task, đặc biệt T09–T18 chuyển SQL sang API và nghiệm thu Windows sạch.

Source được đối chiếu trên nhánh `main`. Khi bắt đầu đọc, HEAD là `9646a7b4eaf271f79bd3278b36a96e0932661c58`, cùng working tree có các thay đổi đã stage của những công việc trước. Trong lúc lập tài liệu, checkout chuyển sang `0a85b569914a9a230425442e9f0b3e1c16072cf1` (`commit before setup release`); đã kiểm lại HEAD và danh sách file thay đổi. Các file composition/configuration/SQL service làm cơ sở chính của kế hoạch không nằm trong phần thay đổi giữa hai commit. Agent không thực hiện commit hoặc thao tác stage; khi chốt tài liệu chỉ file kế hoạch này là thay đổi mới chưa được theo dõi. Không dùng các kết quả kiểm thử lịch sử làm kết quả trên commit mới.

## 1. Mục tiêu và phạm vi

Người dùng đã deploy server. Mục tiêu là đưa bản publish TOOL-LOCAL sang máy Windows x64 khác, đăng nhập server hiện có, chuẩn bị runtime cần thiết rồi sử dụng đầy đủ những tính năng được bật mà không phụ thuộc máy phát triển.

Độc lập ở đây là độc lập với SQL `DUNGDEV`, ổ đĩa/thư mục source, cấu hình riêng, môi trường phát triển và model đã cài trên máy cũ. Không thay đổi yêu cầu kết nối server, đăng nhập, license, thiết bị và quyền tổ chức; không chuyển sản phẩm thành ứng dụng offline hoàn toàn.

Giữ phạm vi đầy đủ của ứng dụng: video dài/ngắn, Vietsub, giọng, render và các module hiện hành. Không ẩn module, bỏ kiểm tra quyền hoặc trả dashboard rỗng để làm ứng dụng có vẻ chạy được.

**Phương án chính được lập kế hoạch:** dữ liệu workflow dùng chung đi qua API; desktop giữ media, workspace, editor và xử lý local. Những API thiếu là phụ thuộc được ghi riêng, không phải giả định rằng server đã deploy thì đã có đủ API.

Nếu yêu cầu giữ nguyên tuyệt đối binary server đang deploy, chỉ có thể hoàn thành nhóm sửa desktop và phương án SQL chuyển tiếp ở mục 10. Phương án đó vẫn cần máy khách truy cập SQL triển khai, chưa đạt tiêu chí desktop chỉ cần API HTTPS.

Lượt này chỉ tạo tài liệu. Không build/test mới, sửa source/config, cài model, chạy database, gọi AI, publish hay deploy. Việc hiện thực các task bên dưới thuộc lượt triển khai sau.

## 2. Bằng chứng source làm cơ sở

| Phần | Hiện trạng đã đọc | Ý nghĩa đối với kế hoạch |
|---|---|---|
| Cấu hình database | `TOOL-LOCAL/Configuration/DesktopOptions.cs` bắt buộc connection string; cấu hình gốc dùng SQL host `DUNGDEV`, Windows Authentication | Chỉ đổi `Server.BaseUrl` chưa đủ; chưa được bỏ validation database trước khi chuyển hết đường gọi thực |
| Dashboard | `WebView/DashboardBridge.cs` gọi `ProjectService.ListAsync` rồi `ListAvailableModelsAsync` qua SQL | Đây là phụ thuộc đang hoạt động, có thể làm lỗi dashboard ngay sau đăng nhập |
| Composition | `Program.cs` truyền factory SQL vào `ProjectService`, `ProjectGenerationService`, `ProjectRenderService`, `ShortVideoWorkflowService`, `LocalVoiceService` | Phải chuyển đủ các nhóm này để bỏ SQL; không chỉ sửa danh sách dự án |
| Job SQL | Có `Jobs/SqlJobStore.cs` và `PersistentJobRunner`, nhưng chưa thấy khởi tạo chúng trong composition `Program` hiện hành | Là mục cần xác minh đường gọi; không mặc định toàn bộ job desktop đang chạy qua chúng, không tự xóa code legacy |
| API hiện có | Có auth/license/org, generation, assets/first frames, Vietsub registry, Cloud và local-voice access | Tái sử dụng; source controller hiện chưa cho thấy API thay đầy đủ việc đọc/ghi của `ProjectService` và các service SQL còn lại |
| Qwen | Cấu hình gốc tắt dịch; `appsettings.user.json` trên máy này bật; file riêng bị loại khỏi publish | Cần release config rõ ràng, không chép cấu hình máy phát triển sang khách |
| Gate Qwen | `Program` vẫn tạo provider khi Vietsub bật; `QwenSetupAdapter` chỉ DISABLED khi provider null | Cần sửa composition và test kết hợp các cờ; test adapter riêng chưa đủ |
| Cài đặt người dùng | `DesktopUserSettingsStore` ghi `appsettings.user.json` cạnh app; bridge có báo lỗi thiếu quyền | Rủi ro có điều kiện với thư mục cài không cho user ghi; cần lưu tùy chọn người dùng vào AppData và kiểm thử quyền thật |
| Publish | `TOOL-LOCAL.csproj` dùng win-x64/self-contained, copy web/worker/media; `FolderProfile.pubxml` có đường output trên ổ D của máy build | Đường D trong publish profile là đường build, không phải bằng chứng runtime bắt buộc ổ D; cần chuẩn hóa cách tạo bundle tái lập |
| Lưu dữ liệu | Workspace/WebView2 dùng LocalAppData; media Vietsub có COPY/LINK; identity/token dùng DPAPI CurrentUser | Giữ cơ chế local; chuyển dữ liệu và đăng nhập máy mới là hai việc riêng |

Các kết quả 120 Passed / 0 Failed / 3 Skipped trong báo cáo rà soát trước là bằng chứng lịch sử trên máy phát triển, **không phải kết quả của kế hoạch này**. Trạng thái server deploy do người dùng cung cấp; không suy ra phiên bản API đang chạy từ source trong repository.

## 3. Mốc hoàn thành và thứ tự

| Mốc | Task | Kết quả được phép kết luận |
|---|---|---|
| M0 — Chốt thiết kế | T00, T09 | Có bản đồ đường SQL thực, API thiếu, cấu hình đích và ma trận kiểm thử |
| M1 — Desktop được chuẩn hóa | T01–T08 | Cấu hình, quyền ghi, Qwen, runtime, bundle đã được sửa/kiểm tra; chưa đồng nghĩa đã bỏ SQL |
| M2 — Desktop dùng API cho workflow | T10–T18 | Toàn bộ luồng đang dùng SQL đã chuyển, có bằng chứng không mở kết nối SQL từ desktop |
| M3 — Nghiệm thu trên máy mới | T19–T23 | Đúng artifact publish đạt trên cấu hình Windows/phần cứng cụ thể, có biên bản và giới hạn |

Chuỗi phụ thuộc chính: **T00 → T09 → T10/T11/T12 → T13/T14/T15/T16 → T17/T18 → T19 → T20/T21/T22 → T23**.

T01–T08 có thể thực hiện độc lập trong khi API được chuẩn bị. T07 kiểm bundle sơ bộ; T19 tạo lại artifact cuối sau tất cả thay đổi. T10–T12 là nhóm phụ thuộc server/contracts, không âm thầm triển khai lên server người dùng đang chạy.

Mức ưu tiên: **P0** là điều kiện bắt buộc để đạt mục tiêu tương ứng; **P1** là bảo đảm vận hành/phục hồi cần xong trước bàn giao rộng. Tất cả task hiện chưa triển khai; phần đã đọc source chỉ là đầu vào.

## 4. Nhóm chuẩn hóa TOOL-LOCAL

### T00 — Lập bản đồ phụ thuộc và khóa phạm vi — P0

**Phụ thuộc:** không. **Phạm vi:** đọc source/composition, không sửa môi trường.

- Ghi commit, trạng thái worktree, cấu hình public đã làm sạch và phiên bản binary được chọn để kiểm thử về sau.
- Liệt kê từng thao tác UI/bridge → service → SQL hoặc API; ghi rõ đọc/ghi, dữ liệu local/remote, quyền, retry, tác vụ có phí và xử lý media.
- Với SQL: rà EF factory, raw `SqlConnection`, stored procedure, background callback, handler và đường reconnect/export, không chỉ constructor.
- Phân loại `SqlJobStore`/runner thành đang gọi, chỉ test hoặc legacy chưa xác minh; giữ những thành phần chưa đủ bằng chứng để bỏ.
- Lập danh sách chức năng bắt buộc giữ, project cũ cần tương thích, máy/OS/CPU/GPU/RAM/disk và tài khoản test sẽ dùng.
- Đối chiếu API/version của bản server đích bằng phương thức đọc trên môi trường được xác định khi triển khai; hiện chưa xác minh runtime đó.

**Đầu ra:** bảng ánh xạ đường gọi + API thiếu + ma trận tương thích. **Nghiệm thu:** mỗi thao tác đang chạy có chủ sở hữu dữ liệu và đích xử lý rõ ràng; không còn đánh đồng file có source với service đang dùng.

### T01 — Chuẩn hóa cấu hình bản phát hành — P0

**Phụ thuộc:** T00. **File dự kiến:** `Configuration/DesktopOptions.cs`, `appsettings.json`, project/publish config và test cấu hình.

- Tách cấu hình bản phát hành khỏi override của máy phát triển; giữ việc không publish `appsettings.user.json` riêng tư.
- Xác định rõ URL HTTPS, workspace, media path, channel/platform và các feature flag của artifact.
- Dịch local mặc định an toàn vẫn tắt; chỉ tạo profile có bật dịch sau khi model và bundle đạt tiêu chí T22. Không đổi mặc định chỉ để vượt Setup.
- Quy định thứ tự đọc cấu hình; không để tùy chọn UI tự thay endpoint tin cậy hoặc ghi đè toàn bộ cấu hình triển khai.
- Bản còn SQL giữ validation phù hợp; bản API hoàn chỉnh chỉ bỏ yêu cầu connection string tại T18.
- Kiểm cấu hình lỗi/thiếu bằng thông báo cụ thể, không trả connection string hay dữ liệu bí mật ra UI/log.

**Test:** không có file override, cấu hình hợp lệ/thiếu/sai HTTPS, cờ bật/tắt, đường dẫn có dấu/cách, cấu hình release bị lẫn dữ liệu máy phát triển. **Nghiệm thu:** biết chính xác máy mới sẽ bật tính năng nào, không cần file cá nhân của máy build.

### T02 — Lưu tùy chọn vào thư mục user có quyền ghi — P1

**Phụ thuộc:** T01. **File dự kiến:** `DesktopUserSettingsStore.cs`, `DesktopOptions.cs`, `DashboardBridge.cs`, `Configuration/DesktopUserSettingsStoreTests.cs`.

- Chuyển các tùy chọn do UI cho phép sửa sang thư mục cấu hình người dùng trong LocalAppData; giữ release config cạnh app ở chế độ đọc.
- Dùng danh sách khóa được phép, không import nguyên file cấu hình cũ chứa endpoint/database/đường dẫn riêng.
- Nếu cần tiếp nhận tùy chọn cũ: đọc có chọn lọc, giữ bản gốc, có version/mốc chuyển đổi, không ghi đè giá trị mới của user; xác định hành vi downgrade trước khi phát hành.
- Ghi atomically qua file tạm; báo lỗi disk/quyền/JSON hỏng mà không làm mất bản cấu hình đang dùng.
- Không yêu cầu chạy desktop bằng Administrator để lưu tùy chọn thông thường.

**Test:** cài vào thư mục không cho user ghi, user mới, JSON hỏng, ghi gián đoạn, giữ khóa hợp lệ và cập nhật/rollback app. **Nghiệm thu:** user thường lưu được tùy chọn và giá trị còn sau restart/update.

### T03 — Đồng bộ cờ dịch và Qwen Setup — P0

**Phụ thuộc:** T01. **File dự kiến:** `Program.cs`, `SystemSetupAdapters.cs`, registry/service dịch và test SystemSetup.

- Dùng cùng điều kiện `VietsubEnabled && VietsubLocalTranslationEnabled` cho khả dụng dịch, thành phần Qwen của Setup và đường gọi install/verify.
- Khi không khả dụng: component DISABLED, không tạo yêu cầu cài/probe/download và không spawn worker; vẫn giữ Cloud độc lập với readiness Qwen.
- Khi khả dụng: phân biệt thiếu model, cần kiểm tra lại, thiếu tài nguyên, lỗi worker và READY.
- Nếu cần tách helper/factory composition để test, tách tối thiểu và dùng đúng factory đó trong `Program`.
- Giữ gate React và C# đồng bộ; không sửa bằng cách chỉ ẩn Qwen trên giao diện hoặc luôn trả READY.

**Test:** ma trận Vietsub bật/tắt × dịch bật/tắt; model thiếu/có/hỏng; kiểm từ composition thực đến registry và gate, không chỉ adapter giả. **Nghiệm thu:** tắt dịch không bị Qwen chặn khởi động; bật dịch chỉ chạy sau probe hợp lệ.

### T04 — Kiểm tra lifecycle Setup và phục hồi — P0

**Phụ thuộc:** T03. **File dự kiến:** `StartupSystemSetupWorkflow`, `StartupSystemSetupGate`, `SystemSetupCoordinator`, bridge và React Setup.

- Giữ các role bắt buộc và ngoại lệ Viewer theo nghiệp vụ hiện hành; không mở quyền nghiệp vụ để vượt lỗi runtime.
- Kiểm install/check/repair/retry/cancel; không tạo hai lượt cài trùng hoặc mở gate bằng response cũ.
- Khi logout, hết lease, đổi organization hoặc đóng app: hủy đúng công việc, bỏ progress/state cũ, giải phóng khóa và không để worker mồ côi.
- Marker từ máy khác, user khác hoặc bundle/worker/model hash khác phải được kiểm lại.
- Đánh giá lại component bắt buộc từ cờ hiện hành; không yêu cầu cài tất cả giọng/model tùy chọn để vào app.

**Test:** gate host từ chối command trước READY, modal khóa nền/focus, retry sau hủy, response muộn, cài gián đoạn và restart. **Nghiệm thu:** trạng thái UI khớp host và lỗi có cách phục hồi rõ ràng.

### T05 — Kiểm tra điều kiện Windows/WebView2/native — P0

**Phụ thuộc:** T00, T01. **File dự kiến:** `DesktopPrerequisites.cs`, media preflight, OCR adapter và package checks.

- Chốt dải Windows x64 hỗ trợ theo .NET/WebView2/native runtime thực tế; không ghi hỗ trợ mọi Windows trước nghiệm thu.
- Giữ kiểm WebView2 trước login; thiếu runtime thì hướng dẫn cài nguồn đã duyệt và kiểm tra lại. Chưa đưa tự tải/chạy bootstrapper vào phạm vi mặc định.
- Probe FFmpeg/FFprobe và OCR Anh/Trung thực; kiểm DLL native/phụ thuộc hệ thống trên máy sạch để phát hiện trường hợp máy phát triển đang vô tình cung cấp thư viện còn thiếu.
- Nếu phát hiện dependency native thiếu, bổ sung đúng cơ chế phân phối đã được rà soát; chưa coi thiếu Visual C++ runtime là lỗi đã được tái hiện.
- Phân biệt lỗi nền tảng không hỗ trợ, file thiếu/hỏng và cài đặt có thể sửa; không đưa user vào vòng lặp retry vô hạn.

**Test:** Windows/user sạch, WebView2 có/thiếu, bundle thiếu một DLL/tool, OCR probe lỗi. **Nghiệm thu:** mở được UI trên nền tảng đã chọn hoặc báo đúng điều kiện cần bổ sung trước khi chạy nghiệp vụ.

### T06 — Bảo đảm model/runtime cài được trên máy mới — P0

**Phụ thuộc:** T03–T05. **File dự kiến:** runtime/resource probe Qwen, voice component store, SystemSetup paths/locks.

- Kiểm cài Qwen từ trạng thái trống: disk/RAM/commit, tải có giới hạn, checksum/size, probe và marker theo máy hiện hành.
- Giữ worker CPU AVX2/AVX/noAVX phù hợp, lựa chọn GPU và fallback có kiểm chứng. Không hứa mọi GPU chạy được hoặc giữ tốc độ của máy phát triển.
- Giữ chính sách an toàn/chất lượng của tối ưu layer hiện hành; không mở rộng layer cho batch dài trong task portability.
- Piper dùng Python riêng; Kokoro/giọng bổ sung và Veo local voice consistency kiểm theo tính năng được bật/chọn, không dùng Piper READY để đại diện mọi engine.
- Kiểm mạng đứt, hash sai, tải dở, thiếu disk và antivirus/file lock; chỉ resume nếu cơ chế hiện hành bảo đảm an toàn, nếu không thì tải lại có thông báo.
- Bộ cài runtime không cần Python hệ thống; không tìm rồi dùng nhầm Python/FFmpeg/worker từ PATH máy phát triển.

**Test:** fixture tải/probe/hủy không phụ thuộc mạng; test model thật tách ở T22. **Nghiệm thu:** thiếu component có thể cài/kiểm lại, lỗi không làm desktop chết hoặc báo READY sai.

### T07 — Chuẩn hóa và kiểm nội dung folder publish — P0

**Phụ thuộc:** T01, T05; bổ sung T02–T06 khi có thay đổi. **File dự kiến:** `TOOL-LOCAL.csproj`, publish profile, script kiểm bundle và test distribution hiện có.

- Chốt một đường folder publish win-x64/self-contained tái lập được, output nhận tham số/thư mục build riêng. Không lấy profile có đường D cá nhân làm hướng dẫn máy khách.
- Kiểm đủ executable/.NET, `wwwroot`, loader WebView2, OCR model/native, `tools/ffmpeg`, `_translation_worker` cùng native CPU/GPU được chọn, `_updater`, worker Python, requirements lock, fixtures và hồ sơ bên thứ ba.
- Ghi inventory và SHA-256 cho artifact cuối; kiểm frontend/host/worker protocol cùng phiên bản, không trộn build cũ.
- Bắt lỗi thiếu thành phần bắt buộc của release profile trước bàn giao; giữ `appsettings.user.json`, token, identity, workspace và model marker cá nhân ngoài package.
- Nếu dùng single-file, vẫn kiểm đủ asset ngoài EXE; hướng dẫn người dùng chuyển cả thư mục artifact.
- Không bỏ qua quy trình duyệt FFmpeg khi tạo artifact phát hành chính thức. Trạng thái duyệt là điều kiện phát hành, không phải lỗi runtime hoặc lỗi server.

**Test:** thiếu/corrupt từng thành phần đại diện, publish lặp vào output riêng không mất web, chạy từ thư mục khác thư mục build. **Nghiệm thu:** bundle tự mô tả đủ thành phần, không cần Visual Studio/Node/SDK trên máy khách.

### T08 — Chẩn đoán lỗi và trạng thái có thể phục hồi — P1

**Phụ thuộc:** T01, T04–T06; nối lỗi API sau T13.

- Dùng mã lỗi hiện có khi phù hợp; tách mất mạng, API không tương thích, thiếu license, sai quyền, runtime thiếu, disk/quyền ghi và component hỏng.
- UI báo hành động tiếp theo, có retry/hủy phù hợp; không kẹt busy hoặc báo dữ liệu rỗng như thao tác thành công.
- Gói chẩn đoán tối thiểu nếu cần: app/worker/protocol version, trạng thái component, hardware đã làm sạch, thời điểm/mã lỗi và hash bundle.
- Không thu token, connection string, prompt/transcript, local path người dùng hoặc signed URL trong log/UI/report mặc định.

**Test:** response muộn, timeout, đổi context, log redaction và retry không phát sinh job/phí trùng. **Nghiệm thu:** có thể phân biệt và hướng dẫn xử lý các lỗi trên máy mới bằng dữ liệu an toàn.

## 5. Nhóm phụ thuộc API và chuyển hết SQL đang dùng

Nhóm này cần thay đổi source contracts/server nếu API thiếu. Không thực hiện deploy server trong kế hoạch. Các tên DTO/service/endpoint mới sẽ chốt tại T09; không coi tên mô tả dưới đây là API đã tồn tại.

### T09 — Chốt contract và bảng API thay thế SQL — P0

**Phụ thuộc:** T00. **Phạm vi:** `TOOL-SHARED.Contracts`, mapping source server/desktop.

- Với mỗi đọc/ghi SQL thực: xác định API hiện có có đúng ngữ nghĩa hay phải bổ sung, request/response, quyền và phiên bản tối thiểu.
- Bao phủ danh sách/dashboard dự án, catalog model khả dụng, tạo dự án, sửa/duyệt scene/character, generation projection, metadata asset local và snapshot render.
- DTO chỉ chứa dữ liệu cần dùng; không trả EF entity/navigation, provider secret/raw URL hoặc absolute path máy người dùng. Workspace dùng ID/key tương đối do host kiểm soát.
- Ownership lấy từ phiên server; không tin `remoteUserId` do client tự khai. Scope organization và project phải rõ.
- Mutation có operation ID/payload hash hoặc idempotency phù hợp, version/revision cho optimistic concurrency; xung đột trả mã rõ ràng.
- Chốt bootstrap/capability/version contract để client mới biết server đủ khả năng; server cũ thiếu API phải báo không tương thích, không âm thầm quay về SQL.
- Chốt phân biệt trạng thái nguồn server với tình trạng file chỉ có trên từng máy; không coi metadata tồn tại là file đã có ở máy mới.

**Test thiết kế/contract:** serialization, nullable/default compatibility, pagination, conflict và response không lộ trường cấm. **Nghiệm thu:** không còn mutation/read đang dùng nào thiếu phương án, và biết chính xác thay đổi server nào bắt buộc.

### T10 — Bổ sung API đọc workflow còn thiếu — P0, phụ thuộc server

**Phụ thuộc:** T09.

- Thêm service/controller mỏng cho đọc dự án/dashboard/catalog còn thiếu; tái sử dụng quyền, policy và source dữ liệu hiện hành.
- Danh sách phải lọc đúng user/org, phân trang/giới hạn hợp lý; project ID thuộc tổ chức khác không được truy cập qua ID trực tiếp.
- Trả snapshot dự án đã lưu, không thay policy/model của project cũ theo cấu hình tổ chức hiện tại.
- Ghi rõ trạng thái output remote và yêu cầu materialize local; không trả đường dẫn file của máy khác như file sử dụng được trên máy mới.

**Test:** user/org/role/license, project cũ, danh sách rỗng/lớn, missing entity và snapshot policy. **Nghiệm thu:** desktop đủ dữ liệu dashboard/catalog qua API mà không gọi SQL.

### T11 — Bổ sung API mutation còn thiếu — P0, phụ thuộc server

**Phụ thuộc:** T09; dùng mô hình T10.

- Tạo dự án dài/ngắn, cập nhật scene/character, import metadata và duyệt/hủy duyệt theo đúng nghiệp vụ hiện hành.
- Giữ project ID và dữ liệu cũ; tạo mới có ID/idempotency ổn định để retry sau mất response không nhân đôi dự án.
- Tạo project không tự gọi AI; generation/quote có phí tiếp tục dùng gateway hiện có và xác nhận từng request.
- Chốt thứ tự tạo workspace local với tạo project server; lỗi một phía có cơ chế reconcile, không xóa dự án hợp lệ chỉ vì máy hiện tại thiếu file.
- Những trường client gửi như hash/metadata local chỉ là khai báo của client; server kiểm quyền và lineage từ dữ liệu tin cậy, không gán nhầm thành bằng chứng media đã được server xác minh.
- Chỉ thêm migration khi thiếu cấu trúc cần thiết; migration mới phải idempotent, rehearsal trên clone và giữ tương thích binary cũ theo ma trận T09.

**Test:** lặp request cùng ID, payload khác, concurrent edit, sai scope, Viewer, approval stale và mất response sau commit. **Nghiệm thu:** mỗi mutation có một đường ghi có thẩm quyền; không ghi đồng thời qua SQL desktop và API.

### T12 — API snapshot/metadata cho media, giọng và render — P0, phụ thuộc server

**Phụ thuộc:** T09, T11.

- Cung cấp snapshot scene/generation/voice/speech/approved asset và revision cần để render/check local voice.
- Metadata materialize, duyệt hoặc kết quả render phải gắn operation/project/asset/source version; không đưa media/path Vietsub lên server ngoài nghiệp vụ Cloud text đã có.
- Giữ metadata dùng chung trên server, media và checkpoint local theo máy/user/project; media thiếu trên máy mới phải báo thiếu/cần tải lại hoặc chuyển dữ liệu.
- Thiết kế cơ chế finalize có kiểm tra version và replay idempotent; source đổi trong lúc xử lý thì không công nhận output cũ là current.
- Chốt các điểm crash trước/sau ghi file tạm, trước/sau commit metadata, trước/sau atomic promote; có reconcile, không chạy render/provider lại chỉ vì ACK bị mất.
- Server kiểm trạng thái/phê duyệt/lineage của workflow; host kiểm byte/hash/stream/duration. Không chuyển phép kiểm file local thành phép tin payload client ở server.

**Test:** thay nguồn giữa render, lease/role thay đổi, mất ACK, replay, file thiếu/hỏng, metadata của thiết bị khác. **Nghiệm thu:** không làm yếu bất biến Approved/current khi tách SQL khỏi desktop.

### T13 — Chuyển ProjectService/dashboard sang API — P0

**Phụ thuộc:** T10, T11.

- Thêm client/repository qua session/license HTTP infrastructure hiện có; không thêm provider client hoặc auth riêng.
- Giữ hoặc điều chỉnh interface `IProjectService` có kiểm soát để bridge/UI giữ đủ chức năng; domain desktop không phụ thuộc EF entity cho response API mới.
- Chuyển list/dashboard/catalog/create/update/approval đã map ở T09; cache local chỉ là dữ liệu có thể làm mới, không tạo một database workflow thứ hai.
- Đổi tổ chức/logout phải hủy và bỏ response cũ; 401 quay login, 403 xử lý đúng license/role.
- Kiểm UI/bridge busy/retry/error với API thật trong môi trường test; không bắt lỗi SQL rồi trả list rỗng.

**Test:** API fake qua host/bridge, auth/context đổi, tạo project mất response và UI vẫn mở đúng project. **Nghiệm thu:** toàn bộ phương thức đang dùng của `IProjectService` không mở SQL.

### T14 — Chuyển orchestration generation còn dùng SQL — P0

**Phụ thuộc:** T11–T13.

- Rà từng phương thức trong `ProjectGenerationService`; giữ request provider trên API hiện có, thay riêng việc đọc/ghi workflow trực tiếp.
- Giữ download qua proxy, `.part`, signature/MIME/size/hash, snapshot và materialize; không tải lại bằng URL provider gốc.
- Retry tải/ghép local không submit AI mới; trường hợp Unknown hoặc mất response dùng operation/status để reconcile.
- Đóng desktop không ảnh hưởng polling/provider task do server sở hữu; reconnect lấy đúng trạng thái và không đếm phí hai lần.
- Khi thay config tổ chức, project hiện hữu vẫn dùng snapshot đã lưu.

**Test:** provider fake, reconnect/retry, hết quyền giữa job, output hết retention, rate/budget/idempotency hồi quy. **Nghiệm thu:** không còn SQL trong đường generation đang hoạt động, không thay đổi behavior chi phí.

### T15 — Chuyển workflow video ngắn và giọng local — P0

**Phụ thuộc:** T11–T14.

- `ShortVideoWorkflowService`: metadata/policy/approval qua API; ảnh gốc, library, draft và cache vẫn local.
- Giữ cả TextOnly và CharacterOutfit; first frame Approved/current, quote/idempotency riêng và không fallback provider.
- `LocalVoiceService`: project/policy/scene/source/approval qua API; model, voice sample, checkpoint và render local giữ đúng chỗ.
- Phân biệt Veo local voice consistency với Piper/Kokoro Vietsub; không gộp readiness hoặc thay engine ngầm.
- Dự án cũ và dữ liệu đã duyệt không mất ID, lineage hoặc quyền truy cập khi đổi nguồn đọc.

**Test:** clip TextOnly/CharacterOutfit bằng fixture, input/revision thay đổi, voice anchor stale, chuyển native/converted và preview local. **Nghiệm thu:** các đường đang dùng SQL của hai service được thay hết; media vẫn ở desktop.

### T16 — Chuyển render/export sang snapshot API và trạng thái local — P0

**Phụ thuộc:** T12–T15.

- `ProjectRenderService` lấy danh sách Approved/current và version từ API; resolve file bằng local store an toàn.
- Giữ FFprobe/hash/audio/duration checks trước và sau render; không chỉ kiểm metadata server rồi bỏ kiểm file local.
- Thực hiện finalize/reconcile đã chốt tại T12; thiếu mạng khi hoàn tất không báo kết quả sai hoặc tự render/gọi AI lại.
- Export FinalVideo đã có giữ khả năng xuất nhiều lần sau kiểm hash; xử lý rõ output không tồn tại trên máy mới.
- Giữ mọi thay đổi phụ đề/mask/lật hình/âm thanh đã có; không lùi về pipeline cũ khi refactor dữ liệu.

**Test:** file/source/approval đổi giữa render, hủy/crash/mất ACK, disk full, export lặp và preview/MP4 tương ứng. **Nghiệm thu:** render/export đúng lineage và không cần SQL client.

### T17 — Khép lại kiểm kê job và các đường SQL gián tiếp — P0

**Phụ thuộc:** T00, T13–T16.

- Rà lại constructor/DI/event/background task, `SqlJobStore`, runner, helper và stored procedure được gọi từ desktop.
- Nếu job local có đường chạy thực: chốt store local hoặc API điều phối theo ownership; không chuyển provider polling từ server về desktop.
- Job local có file trên máy này không được bị máy khác claim nhầm; giữ scope user/org/project/device khi cần và idempotency/recovery.
- Nếu class chỉ legacy/test: ghi bằng chứng không reachable trong runtime; không xóa entity/migration/procedure lịch sử chỉ để grep hết từ SQL.

**Test:** restart/claim/lease/retry cho đường job thật nếu có; runtime composition/architecture test ngăn đăng ký lại factory SQL. **Nghiệm thu:** mọi SQL còn trong source đều có phân loại, bản app mới không có đường gọi SQL hoạt động.

### T18 — Bỏ yêu cầu database khỏi desktop và chuyển composition — P0

**Phụ thuộc:** T13–T17; T09 có compatibility contract.

- Bỏ khởi tạo factory SQL khỏi composition đang chạy và bỏ yêu cầu `Database.ConnectionString` trong profile API.
- Loại cấu hình `DUNGDEV` khỏi artifact API; chỉ gỡ package SQL/EF nếu dependency thật cho phép, không gỡ SQLite của Vietsub/library.
- Không fallback SQL khi API lỗi hoặc server thiếu version; hiển thị lỗi tương thích/hướng nâng cấp cụ thể.
- Không tự tạo SQL database trên máy khách, đổi toàn bộ workflow sang SQLite hoặc mở quyền server database từ desktop.
- Tạo bằng chứng test network/composition: chạy full chức năng trên máy test không cài SQL client tooling, không có connection string và bị chặn truy cập SQL/DUNGDEV trong môi trường test cô lập.

**Nghiệm thu:** ứng dụng có thể login/dashboard và dùng các workflow đã chuyển chỉ với HTTPS cùng runtime local; không phát sinh kết nối SQL từ tiến trình desktop.

## 6. Nhóm kiểm thử và bàn giao

### T19 — Regression và tạo artifact ứng viên cuối — P0

**Phụ thuộc:** T01–T18 cho phương án API; nhóm server tương ứng phải có trong môi trường test.

- Chạy test đích theo từng task trước; sau khi tích hợp chạy restore/build/test toàn solution và frontend theo AGENTS.
- Regression gồm auth/license/org, setup composition, contracts/API, gateway cost behavior, media lineage, Vietsub OCR/dịch/giọng/mask, library, TikTok/Bilibili và update theo phần có tác động.
- Giữ test model thật opt-in; ghi Passed/Failed/Skipped riêng. Failure phải điều tra, không sửa assertion/skip để báo đạt.
- Tạo artifact từ đúng source đã kiểm, inventory/hash/build number/protocol/config snapshot, không lấy thư mục build cũ làm sản phẩm nghiệm thu.

**Nghiệm thu:** không có failure chưa giải thích/chưa xử lý thuộc phạm vi bàn giao; mọi giới hạn suite và test skip được ghi rõ trước bước máy sạch.

### T20 — Nghiệm thu cài và thao tác trên Windows sạch — P0

**Phụ thuộc:** T07, T18, T19.

- VM/máy vật lý riêng, tài khoản Windows thường; không có Visual Studio/.NET SDK/Node/Python hoặc source repository. Thử hệ thống không có ổ D.
- Hai tình huống WebView2 có sẵn và chưa cài; thư mục app chỉ đọc đối với user, đường dẫn/user name có dấu/cách.
- Login, device/lease, chọn org, dashboard, project dài/ngắn và Vietsub; máy mới không có dữ liệu phải hiển thị đúng, không giả có media từ registry.
- Cài/probe component cần thiết, OCR en/zh, dịch, giọng đã chọn, preview/timeline/mask, lưu/mở lại và xuất MP4.
- Thử license thiếu chỗ thiết bị, mạng lỗi, disk thiếu, DLL/model hỏng; không tự vô hiệu license hoặc tăng seat.
- Generation/Cloud/TikTok thật chỉ trong môi trường và tài khoản test phù hợp; provider có phí hoặc public posting chưa được phép thì dùng fixture và ghi phần thật chưa nghiệm thu.

**Nghiệm thu:** ghi màn hình/log đã làm sạch, artifact hash, OS/hardware/config, kết quả từng luồng; không lấy build pass thay cho smoke desktop.

### T21 — Chuyển dữ liệu, update và rollback — P1

**Phụ thuộc:** T02, T07, T19; phối hợp T20.

- Hướng dẫn backup/chuyển workspace khi app đóng, giữ manifest/SQLite/media/hash; không mặc định copy EXE hay đăng nhập là đồng bộ dữ liệu local.
- Test COPY giữ file tương đối; LINK mất nguồn phải báo và có cách liên kết lại được kiểm chứng. Nếu UI chưa có thao tác cần thiết, ghi task bổ sung tối thiểu, không hứa đã có.
- Máy mới đăng nhập và tạo identity riêng; không chuyển token/DPAPI/device-id để né kích hoạt. Runtime chuyển máy phải probe lại.
- Update khi user có preferences/workspace/model: không mất dữ liệu, marker hết hiệu lực đúng lúc, có rollback app theo manifest.
- Chỉ rollback về binary tương thích API/schema/config; bản cũ còn cần DUNGDEV không được coi là fallback hoạt động cho máy khách mới. Chuẩn bị một bản API đã nghiệm thu để rollback về sau.

**Test:** update thành công/thất bại giữa chừng, thiếu disk, file lock, preserve dữ liệu, rollback đọc đúng settings. **Nghiệm thu:** có runbook phục hồi đã diễn tập, không dựa vào thao tác SQL ad hoc.

### T22 — Kiểm model thật và độ ổn định theo phần cứng — P0 cho tính năng được bật

**Phụ thuộc:** T06, T19; dùng artifact T20.

- Ít nhất một máy CPU-only và máy NVIDIA mục tiêu nếu công bố GPU; thêm nhóm CPU khác theo dải hỗ trợ chốt ở T05. Máy GPU không có trong nghiệm thu thì không công bố đã xác minh GPU đó.
- Đo cold start/cài lần đầu, warm start, RAM/commit/VRAM peak, disk dùng, thời gian OCR/dịch/giọng/render và tỉ lệ lỗi trên fixture cố định.
- Qwen: en/zh, batch ngắn và dài, mapping cue, không ghi đè manual/locked; giữ quality cap layer hiện hành, không chấp nhận tăng tốc nếu chất lượng/mapping lùi.
- Giọng: verify WAV, nghe mẫu tiếng Việt, đúng engine/voice, preview và MP4 khớp. Model bị skip không được bật profile chỉ dựa test fake.
- Đề xuất đợt đầu: mỗi cấu hình 10 chu kỳ mở/xử lý/hủy/mở lại và một phiên 2 giờ với video test; xác định tiêu chí thời gian/RAM theo baseline cùng máy trước chạy, ghi rõ đây là phạm vi đo hữu hạn.
- Không chấp nhận crash desktop, worker mồ côi, mất dữ liệu, READY sai hoặc tài nguyên tăng liên tục chưa giải thích. Worker lỗi phải hồi phục theo contract; CPU fallback phải được báo đúng backend.

**Nghiệm thu:** có bảng cấu hình nào đạt tính năng nào; không cam kết mọi máy hoặc tốc độ benchmark cũ. Chốt release flag dựa trên kết quả của chính artifact.

### T23 — Chốt hồ sơ bàn giao và điều kiện triển khai — P0

**Phụ thuộc:** T19–T22.

- Ghi source/worktree đã kiểm, artifact version/hash, API tối thiểu, OS/hardware hỗ trợ, component/model/network/disk cần có và tính năng chưa nghiệm thu.
- Hướng dẫn cài từ folder publish, WebView2, login/device, cài model, backup/chuyển workspace, lỗi thường gặp và phục hồi.
- Tách kết quả: có source, test tự động, smoke máy đích, rollout. Kế hoạch hoặc test fixture không tự đánh dấu production-ready.
- Nếu nhóm API cần deploy bổ sung: triển khai tương thích server trước, sau đó desktop; xác định môi trường, migration nếu có, backup/restore và rollback theo runbook hiện hành.
- Upload release, deploy/migration production, sửa quyền DB production hoặc smoke provider có phí là bước vận hành riêng với đích/tác động đã xác định; không tự thực hiện từ yêu cầu lập kế hoạch này.

**Nghiệm thu:** người vận hành có thể cài và kiểm app mà không cần máy phát triển; chỉ công bố ổn định trong phạm vi đã đạt M3.

## 7. Ma trận nghiệm thu tối thiểu

| Nhóm | Trường hợp bắt buộc | Kết quả mong đợi |
|---|---|---|
| SQL/API | Không có cấu hình SQL, không truy cập DUNGDEV/SQL, API đạt version | Các tính năng đã chuyển vẫn hoạt động; không có kết nối SQL từ desktop |
| Server cũ | API contract/capability thiếu | Báo không tương thích; không fallback SQL hay bỏ kiểm tra quyền |
| Qwen flag | Vietsub/dịch lần lượt bật và tắt | RequiredComponents/registry/UI khớp; cờ tắt không tải/probe Qwen |
| Auth/context | 401, 403, hết lease, đổi org/logout khi có request | Đúng trạng thái, không trả dữ liệu của context cũ, không bỏ qua gate |
| Quyền thư mục | App directory không writable, LocalAppData writable | Preferences/workspace/cache hoạt động bằng user thường |
| Cấu hình/đường dẫn | Không ổ D; đường có dấu/cách; CWD khác app | Resolve đúng; không tìm vào source, PATH hay profile máy phát triển |
| Prerequisite | WebView2 thiếu, DLL native/FFmpeg thiếu hoặc hỏng | Dừng/hướng dẫn đúng component, có retry/repair phù hợp |
| Runtime/model | Trống, tải dở, hash sai, marker máy cũ, hết disk/RAM | Không READY sai; cài/kiểm lại có kiểm soát và hủy được |
| Phần cứng | CPU-only; GPU đích; driver/backend không dùng được | Backend/fallback đúng, không làm desktop chết, ghi phạm vi đã đo |
| Nghiệp vụ | Video dài/ngắn, Vietsub, voice/render, module hiện có | Giữ chức năng/quyền/snapshot, không bỏ module để app khởi động |
| Retry | Mất response sau tạo project/submit/finalize | Reconcile bằng ID; không tạo dự án/phí/output trùng |
| Media | COPY/LINK, source đổi, approval cũ, file local thiếu | Không render nhầm; báo cần file hoặc stale, bảo toàn dữ liệu |
| Update | Cập nhật/rollback, restart giữa thao tác | Giữ dữ liệu, kiểm lại runtime và tương thích config/API |
| Chẩn đoán | Lỗi auth/provider/config/path | Log/UI không chứa secret, token, connection string hoặc transcript |

## 8. Lệnh kiểm thử dự kiến khi triển khai

Các lệnh dưới đây là bước tương lai, **chưa chạy trong lượt lập kế hoạch**:

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Trong `TOOL-LOCAL/Web`:

```powershell
npm ci --no-audit --no-fund
npm run build
npm test
```

Nếu dùng chế độ xUnit tuần tự để tránh tranh tài nguyên theo quy ước repo, ghi chính xác lệnh và lý do trong biên bản. Không chạy model thật đồng thời với build/OCR/FFmpeg nặng. Không dừng ứng dụng/IDE của người dùng để giải phóng tài nguyên.

Mỗi biên bản cần: task ID, commit/worktree, ngày giờ, cấu hình máy, artifact hash, flag, lệnh, Passed/Failed/Skipped, log đã làm sạch và lỗi còn mở. API fake, VM sạch, CPU thật và GPU thật là các loại bằng chứng khác nhau.

## 9. Rủi ro kỹ thuật cần xử lý trong task

| Rủi ro | Task xử lý | Quy tắc |
|---|---|---|
| Chỉ chuyển dashboard nhưng generation/render còn SQL | T00, T09, T17, T18 | Kiểm toàn chuỗi gọi và đo không có SQL ở runtime |
| Server deploy hiện tại chưa có API mới | T09–T12, T23 | Tách rõ dependency, kiểm version; không giả định server lỗi hoặc tự deploy |
| Metadata dùng chung nhưng file chỉ tồn tại ở máy cũ | T12, T16, T21 | Mô hình local availability rõ ràng, không tin đường dẫn từ máy khác |
| Retry HTTP làm submit AI hoặc tạo dự án hai lần | T11, T14 | Operation ID/idempotency bền vững, replay và reconcile |
| Source thay đổi trong render, output cũ được duyệt | T12, T16 | Revision/snapshot/finalize + kiểm hash local, test race/crash |
| Copy settings/token/marker máy cũ làm lộ dữ liệu hoặc READY sai | T01, T02, T06, T21 | Allowlist cấu hình, login mới, probe đúng máy |
| Refactor xóa nhầm legacy/đổi dữ liệu đang dùng | T00, T17, T23 | Không xóa theo kết quả grep; schema thay đổi có migration và rehearsal riêng |
| Rollback desktop cũ lại cần SQL máy dev | T09, T21 | Không hứa rollback tới binary chưa đáp ứng kiến trúc máy khách |
| Chỉ test máy dev nên bỏ sót native dependency | T05, T20, T22 | Bắt buộc chạy artifact trên môi trường sạch và hardware mục tiêu |

## 10. Phương án phụ nếu phải giữ nguyên server hiện tại

Chỉ chọn khi chấp nhận desktop tiếp tục dùng SQL trong môi trường được quản lý. Đây là nhánh thay thế T09–T18, không làm cùng để giữ hai cơ chế ghi hoặc tự fallback.

| Task | Công việc | Điều kiện đạt |
|---|---|---|
| S01 — Chốt kết nối triển khai | Xác định SQL đích, mạng riêng/VPN, TLS, cơ chế danh tính và cách cấp cấu hình trên từng máy; không dùng host DUNGDEV/Windows identity máy phát triển làm mặc định chung | Máy test được phép kết nối SQL đích, không lộ credential trong bundle/log |
| S02 — Xác minh quyền giới hạn | Kiểm trên môi trường test với `VideoMakerDesktopRole` đúng workflow `vf`; không mở quyền `auth`, `ai`, `dbo`, `vs` hay các bảng server-only; kiểm procedure nếu có đường gọi thật | Đủ chức năng hiện hành với quyền tối thiểu; thao tác ngoài phạm vi bị từ chối |
| S03 — Nghiệm thu và ghi giới hạn | Hoàn thành T01–T08 và T19–T23 với biến thể tiêu chí SQL đã ghi rõ; kiểm mất kết nối SQL và thông báo phục hồi | Công bố là bản dùng mạng/API + SQL triển khai, không gọi là desktop chỉ cần HTTPS |

Nhánh này không yêu cầu sửa code ứng dụng server để bổ sung API, nhưng vẫn cần hạ tầng/quyền SQL phù hợp. Không đề xuất đóng gói mật khẩu chung, mở SQL công khai hoặc dùng quyền quản trị để né lỗi.

## 11. Checklist đóng kế hoạch khi triển khai xong

- [ ] T00: inventory bám đường gọi thật, API target/version đã xác minh trên môi trường test.
- [ ] T01–T08: release config, quyền ghi user, Setup, Qwen và bundle đạt.
- [ ] T09–T18: API workflow đủ và desktop không còn mở SQL; hoặc ghi rõ chọn nhánh S01–S03 với giới hạn khác.
- [ ] T19: build/full suite/frontend có biên bản mới; không gộp Skipped vào Passed.
- [ ] T20: artifact cuối chạy trên Windows sạch bằng user thường.
- [ ] T21: chuyển workspace/update/rollback đã diễn tập, không mất dữ liệu.
- [ ] T22: model thật và cấu hình phần cứng được công bố đã đạt, flag khớp nghiệm thu.
- [ ] T23: hồ sơ bàn giao ghi rõ giới hạn, rollback và trạng thái rollout thực.

Tài liệu tham chiếu: [rà soát máy mới](RA_SOAT_TOOL_LOCAL_TREN_MAY_MOI.md), [kiến trúc](KIEN_TRUC_KY_THUAT.md), [nghiệp vụ](NGHIEP_VU_HE_THONG_VIDEOMAKER.md), [vận hành](VAN_HANH_VA_PHAT_HANH.md), [kiểm thử](KIEM_THU_VA_NGHIEM_THU.md).
