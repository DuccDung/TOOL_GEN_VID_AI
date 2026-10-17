# Triển khai bài thử Setup tải model từ server

Ngày thực hiện: 2026-10-16. Nhánh `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b` và working tree chưa commit. Các sửa đổi có sẵn khác được giữ nguyên. Tham chiếu [kế hoạch ST01–ST32](PLAN_TASK_TEST_SETUP_MODEL_TU_SERVER.md).

**Đã hoàn tất phần source và kiểm thử chuẩn bị được mô tả dưới đây. Chưa đưa kho lên server thật, chưa phát hành Setup mới, chưa cách ly hoặc xóa model đang dùng.** Không dùng kết quả source/test giả lập để kết luận đã cài model qua server.

## Phần đã bổ sung trong source

### Tải lại khi kho model bận

[Downloader](TOOL-LOCAL/ModelDistribution/ResumableModelDownloader.cs) đọc `Retry-After` dạng số giây/ngày HTTP khi gặp 503/429 và các response 5xx. Giữ tối đa bốn request, chờ tối đa 60 giây mỗi lần và tổng backoff tối đa 180 giây. Hủy được trong lúc chờ; `.part`, receipt và Range/If-Range được giữ để tải tiếp. Response lỗi được dispose trước khi đợi. Timeout headers/body, exact origin, size/hash và nguyên tắc không fallback upstream giữ nguyên.

Đây là chuẩn bị source cho ST13. Hosting có cold scan vượt ngân sách retry vẫn có thể trả lỗi an toàn; phải đo trên đích, chưa có bằng chứng concurrency của server thật.

### Ký executable trước chốt hash

[Pipeline publish](scripts/Publish-DesktopRelease.ps1) có chế độ Authenticode Windows Store tường minh. Ký đúng executable dự án trước probe/inventory/ZIP/hash, xác minh lại byte được copy/giải nén và ký Setup trước hash cuối. Certificate phải đúng thumbprint, còn hạn, có private key và EKU code signing; timestamp và SignTool được chỉ định. Không nhận PFX/password hoặc tạo/import/export khóa.

Mặc định `None` được giữ, báo `Signed=false`; `-RequireAuthenticode` chặn thiếu cấu hình ký. Không nâng trạng thái production hoặc bỏ gate FFmpeg Release. [Tham số và thứ tự ký](scripts/DesktopAuthenticode.md). Tests dùng fake certificate/SignTool; chưa ký certificate thật, gọi timestamp endpoint hoặc phát hành artifact.

### Chẩn đoán cài Piper

[Piper diagnostics](TOOL-LOCAL/Vietsub/Voice/PiperInstallDiagnostics.cs) giữ phase, exit code, mã Windows và độ dài đường dẫn. Output offline được drain với buffer giới hạn, không đưa path/stdout/stderr vào diagnostic. Chỉ phân loại giới hạn đường dẫn khi có `PathTooLongException` hoặc mã Windows 206; exit code 1 không đủ chứng minh nguyên nhân đó.

[CLI](TOOL-LOCAL/SystemSetup/PiperOfflineCommand.cs) và [script probe](scripts/Test-PiperOfflineBundle.ps1) trả báo cáo lỗi được whitelist; `-FailureReportPath` chỉ nhận đường đầy đủ không qua reparse và tạo file mới trong thư mục đã tồn tại. Stdout JSON hỏng được đổi sang mã lỗi an toàn, không in nội dung parser. Process không khởi động hoặc timeout trước khi có report vẫn có thể không tạo file này; không diễn giải thiếu report là Passed. Không đổi runtime root hoặc registry, không thay pin/worker/model. Lỗi workspace dài lịch sử chưa được xác định nguyên nhân gốc.

### Kiểm kê chỉ đọc trước thao tác

[Get-ModelSetupPreflight.ps1](scripts/Get-ModelSetupPreflight.ps1) cùng [module](scripts/ModelSetupPreflight.psm1) đọc cấu hình base/user và liệt kê:

- Qwen trong AppData/fixed drive và nguồn reuse dưới tối đa mười ancestor của EXE/CurrentDirectory.
- Piper legacy/global, payload trong app, prepared bundle và ba artifact/cache/scratch tương ứng.
- Trust state các channel hiện có, known revocations, dữ liệu account/device/project cần giữ và disk check của downloader.

Không đọc token, không xuất connection string, không chạy desktop/model hoặc ghi cache/marker. Wrapper chỉ tạo báo cáo mới, không overwrite. Định nghĩa model/repository lấy từ source checkout, **chưa xác minh trust root nhúng của ứng dụng đích**. Chưa kiểm từng file con/hardlink hoặc tạo manifest quarantine; báo cáo luôn là `InventoryOnly`, `cleanupAuthorized=false`.

Ví dụ dùng khi đã có bản desktop cài riêng:

```powershell
.\scripts\Get-ModelSetupPreflight.ps1 `
  -ApplicationRoot '<absolute-installed-desktop-directory>' `
  -CurrentDirectory '<desktop-launch-working-directory>' `
  -ReportPath '<new-absolute-local-evidence-file.json>'
```

Không dùng blocker list rỗng của công cụ này thay gate ST18. Server, package, embedded trust, disk tổng, Piper trên đường thực và backup/restore vẫn cần được xác minh riêng.

## Quan sát trên máy hiện tại

Bằng chứng nằm tại `D:\vm-tests\setup-from-server-20261016-141304`, ngoài repository. Báo cáo đầu tiên dùng thư mục source `TOOL-LOCAL` cùng working directory checkout để kiểm kê; **đây chưa phải lần chạy Setup đã cài**.

- Kiểm lại inventory bộ `D:\VideoMakerModelDistribution\handover\models-20261016-03`: 31 file đạt, chưa probe runtime trong lượt kiểm file này. Giữ nguyên candidate cũ.
- Qwen component tại AppData và GGUF nguồn reuse trong repo còn tồn tại. Piper legacy trong workspace còn tồn tại; các nguồn này có thể khiến ứng dụng bỏ qua lượt tải mới.
- Cache mặc định ở C. Snapshot kiểm kê còn khoảng 1,61 tỷ byte trống. Downloader cần **5.866.975.744 byte** cho Qwen mới, gồm model, reserve đã pin và 64 MiB; chưa bao gồm toàn bộ Setup/Piper/backup. Không tự dọn dữ liệu khác để lấy chỗ.
- Kiểm chỉ đọc route `/api/model-distribution/Development/catalog` trên domain đang có trong cấu hình source `taphoatool.com` trả 404. Domain này **chưa được người dùng xác nhận là đích triển khai**; kết quả đó không xác minh hoặc thay đổi môi trường.
- Piper từ build Release mới đã cài/probe thành công trong workspace mới cô lập trên D, với prefix component **68 ký tự**, bằng độ dài root global mà desktop sẽ chọn sau khi legacy không còn. Có venv mới, không tải dependency qua mạng; hai lượt verify/probe bằng tiến trình mới cũng đạt, lượt cuối dùng script đã hoàn thiện xử lý JSON/path. Không dùng root thực của người dùng, không phải Setup từ server, chưa nghe mẫu hoặc nghiệm thu workflow. Workspace được ghi trong `piper-probe-scope.json`; báo cáo `piper-safe-success.json`, `piper-verify-safe-success.json` và `piper-final-verify-safe-success.json`. Không suy ra mọi đường dẫn dài đều hoạt động.

## Kiểm thử và giới hạn

| Kiểm tra | Passed | Failed | Skipped | Phạm vi |
|---|---:|---:|---:|---|
| Full .NET cuối | 2.029 | 0 | 18 | 2.047 test; TEMP/TMP chỉ của tiến trình test trên D. |
| Frontend | 322 | 0 | 0 | 47 file; `npm ci`, production build và `npm test` đạt. |
| Downloader | 32 | 0 | 0 | Nằm trong full .NET, không cộng trùng. |
| Authenticode | 19 | 0 | 0 | Fake certificate/SignTool; nằm trong full .NET. |
| Preflight | 11 | 0 | 0 | Kiểm reuse, state revoke, config/private data và không overwrite; nằm trong full .NET. |
| Piper diagnostics | 10 | 0 | 0 | Phân loại lỗi/drain/safe metadata; nằm trong full .NET. |

`dotnet restore` và Release build đạt 0 error; giữ hai cảnh báo Pkcs NU1902/NU1903 có sẵn. Frontend còn cảnh báo kích thước chunk Vite. Không sửa dependency hoặc test media để vượt điều kiện môi trường.

Lượt .NET đầu dùng TEMP mặc định C: **2.024 Passed / 1 Failed / 18 Skipped**, lỗi `VoiceRenderer_TwoHours_KeepsLastPhraseAndUsesBoundedIntermediateFiles` do free disk. Giữ `tests/full-regression.trx`; sau chuyển TEMP/TMP của riêng tiến trình test sang D và build bổ sung regression preflight, full cuối đạt như bảng trong `tests/full-regression-temp-d.trx`. Không xóa assertion, nới gate disk hoặc đổi biến môi trường hệ thống.

`piper-script-validation.json` ghi kiểm tra parser, ba ca đường phụ thuộc CWD bị từ chối, năm ca stdout sai được sanitize, cùng report success/managed failure hợp lệ. Những kiểm tra hàm PowerShell này không chạy UV/model. Smoke Piper thật được ghi riêng ở trên; các test fake tải/certificate/process không thay bài cài qua server. Các bài model/SQL opt-in bị Skipped vẫn chưa nghiệm thu.

Báo cáo tổng hợp và hash 14 file source/script/test: **`implementation-result-final.json`** trong thư mục bằng chứng. File này thay bản tổng hợp ban đầu: skipped lấy từ outcome từng `UnitTestResult`, vì counter `notExecuted` của adapter trong TRX bằng 0 dù có 18 outcome `NotExecuted`. Không cộng các lượt kiểm lại vào tổng test mới.

## Trạng thái task và điều kiện tiếp tục

| Task | Trạng thái hiện tại |
|---|---|
| ST01–ST02 | Chờ chỉ định server/domain/storage/cách truy cập và version/build/channel của lần thử. Không suy đích triển khai từ appsettings. |
| ST03–ST05 | Có công cụ/báo cáo kiểm kê và Piper smoke mới đạt ở prefix tương đương global; chưa kiểm đủ hardlink, backup map, disk tổng hoặc cài trên root thực bằng Setup. Chưa thực hiện cách ly. |
| ST06 | Chưa có public root/catalog signing key và certificate ký executable được chỉ định; FFmpeg vẫn cần Release approval thật, profile khách cần được rà soát. |
| ST07–ST12 | Chưa tạo candidate/ký/chuyển hoặc kích hoạt kho trên server đích. |
| ST13 | Source retry đã bổ sung; chưa nghiệm thu timeout/concurrency/rate limit qua hosting thật. |
| ST14–ST18 | Pipeline ký đã bổ sung; chưa có profile/package/Setup cuối được ký và phục vụ từ đích. Gate trước dọn chưa đạt. |
| ST19–ST32 | Chưa chạy bài cách ly → tải qua UI → runtime/workflow → lỗi/restore/retention. Model và dữ liệu người dùng hiện còn nguyên. |

Để thực hiện phần server, cần xác định domain/máy đích, thư mục kho và phương thức truy cập cùng phạm vi thay binary/config/recycle. Không gửi mật khẩu, private key hoặc connection string trong hội thoại. Sau khi đích và bộ ký/profile hợp lệ được xác định, vẫn phải hoàn tất server + Setup trước khi thao tác model đang dùng.
