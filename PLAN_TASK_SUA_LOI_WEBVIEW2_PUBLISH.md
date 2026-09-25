# Kế hoạch và triển khai: sửa lỗi DLL khi mở desktop đã publish

Ngày: 2026-09-23. Nhánh `main`, HEAD `0a85b569914a9a230425442e9f0b3e1c16072cf1`, working tree có thay đổi trước phiên. Người dùng đã yêu cầu triển khai sau yêu cầu lập kế hoạch. Giữ nguyên các thay đổi về đăng xuất và chuẩn hóa desktop có sẵn.

## Bằng chứng chẩn đoán

Gói người dùng: `D:\laptrinhweb\code_outsrc\TOOL_AUTO_GEN_POST_VIDEO\Branch-Tool-Sub\desktop-publish\desktop-publish`.

- EXE single-file có WebView2 Core/WinForms 1.0.4129.50; runtime .NET 10.0.9 được đóng kèm.
- `runtimes/win-x64/native/WebView2Loader.dll` tồn tại, chữ ký Microsoft hợp lệ, SHA-256 khớp DLL của package NuGet đã pin: `A9A09232C25805323D4CFB3FC8F545A190A9C8A99C93262EA99D0B88DF99EC90`.
- Dependency metadata trong EXE không đăng ký native loader; code chưa chỉ định đường nạp trước kiểm WebView2. Máy phát triển có DLL cùng tên trong các thư mục phần mềm khác thuộc PATH.
- Cùng gói cũ, `--check-desktop` với PATH chỉ có thư mục Windows trả exit 1; thêm thư mục loader của chính gói vào PATH tiến trình thử trả exit 0. Không sửa PATH hệ thống hoặc gói gốc. Đây là tái hiện trên máy phát triển; chưa phải kiểm chứng trực tiếp máy người dùng.

## Phương án

Giữ thư mục loader hiện hành trong gói, chỉ định đường tuyệt đối từ `AppContext.BaseDirectory` bằng `CoreWebView2Environment.SetLoaderDllFolderPath` trước API WebView2 đầu tiên. Dùng chung cho kiểm tra trước login và chẩn đoán. Không phụ thuộc DLL từ PATH hoặc thư mục làm việc; không yêu cầu người dùng chép DLL thủ công mỗi lần publish.

Phân biệt thiếu/hỏng/sai kiến trúc loader với chưa cài Evergreen Runtime; thông báo có hướng xử lý và không trả raw exception/path. Runtime Evergreen vẫn được cài từ Microsoft theo luồng hiện có.

## Task và tiêu chí nghiệm thu

| Task | Phạm vi | Tiêu chí |
|---|---|---|
| W01 | Tái hiện và đối chiếu gói | Ghi rõ commit, gói cũ, thử PATH tối giản, không nhầm lỗi loader với thiếu .NET/VC++ |
| W02 | `DesktopWebViewRuntime`, `DesktopPrerequisites` | Nạp đúng loader x64 của ứng dụng trước login; không fallback DLL khác; thiếu/hỏng báo đúng, retry sau phục hồi |
| W03 | `DesktopReadinessCommand`, `Program` | `--check-desktop` trả tình trạng WebView2 cụ thể; thêm `--check-webview2` độc lập config/SQL/login/model để chẩn đoán nhanh |
| W04 | `TOOL-LOCAL.csproj`, script kiểm publish | Loader luôn nằm ngoài EXE tại đường chuẩn; publish thiếu file bị chặn; kiểm PE/DLL/x64 trước bàn giao |
| W05 | Regression C# và gói thử | Thiếu/hỏng/x86, runtime thiếu, lỗi nạp, không lộ exception; subprocess với PATH Windows và working directory khác; gói single-file cũng được thử |
| W06 | Kiểm thử toàn solution/frontend | Restore/build/test theo AGENTS; báo Passed/Failed/Skipped thực tế; không tính model/SQL opt-in bị skip là đạt |
| W07 | Gói thử và báo cáo | Tạo thư mục riêng, giữ gói cũ; kiểm checksum/bundle; ghi version/hash và giới hạn môi trường |
| W08 | Nghiệm thu máy đích | Mở login trên Windows x64 của người dùng, có/thiếu WebView2, sau đó kiểm server/SQL theo cấu hình triển khai riêng |

Thứ tự: W01 → W02/W03/W04 → W05 → W06 → W07 → W08. Lượt triển khai chỉ tạo gói thử local; phát hành production và nghiệm thu máy đích là các trạng thái riêng. Lỗi SQL workflow/endpoint sau login thuộc [kế hoạch máy mới](PLAN_TASK_TOOL_LOCAL_CHAY_TREN_MAY_MOI.md), không được che bằng việc bỏ gate.

## Kiểm chứng dự kiến

- `dotnet restore TOOL_GEN_POST_VIDEO.slnx`.
- `dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore`.
- `dotnet test TOOL-TESTS/TOOL-TESTS.csproj -c Release --no-build -- xUnit.ParallelizeTestCollections=false`.
- Trong `TOOL-LOCAL/Web`: `npm ci --no-audit --no-fund`, `npm run build`, `npm test`.
- Publish Development riêng, single-file/win-x64/self-contained; kiểm gói và chạy EXE `--check-webview2`/`--check-desktop` với PATH tiến trình tối giản. Kiểm mẫu gói thiếu/hỏng/x86 trong bản sao fixture.
- Không khởi động server, migration, provider có phí hoặc cài model để xác minh lỗi loader. Không thay license/provenance hay bỏ gate Release của FFmpeg.

## Kết quả triển khai ngày 2026-09-23

W01–W07 đã hoàn tất trên working tree nêu trên; W08 **chưa xác minh** vì chưa truy cập máy đích. Không commit, không rollout production. Gói publish người dùng cung cấp được giữ nguyên.

Source đã đổi:

- `DesktopWebViewRuntime.cs`: kiểm loader PE/x64, chỉ định đường nạp trước API WebView2, trả trạng thái và thông báo an toàn.
- `DesktopPrerequisites.cs`: dùng chung probe trước login, phân biệt sửa gói với cài Evergreen Runtime; cho kiểm tra lại.
- `DesktopReadinessCommand.cs` và `Program.cs`: thêm `--check-webview2`, giữ thông tin WebView2 chi tiết trong `--check-desktop`.
- `TOOL-LOCAL.csproj`: pin đường copy loader từ package NuGet, giữ file ngoài EXE, chặn publish thiếu file. Tham số `DesktopDeploymentSettingsPath` chọn cấu hình triển khai ngay trong quá trình publish, không lấy cấu hình nguồn khi đã chọn file thay thế.
- `Test-DesktopSetupPublish.ps1`: bắt buộc loader PE/DLL/x64; tùy chọn `-ProbeWebView2` chạy EXE với PATH chỉ gồm Windows. Thiếu Evergreen được phân biệt và báo `NOT_INSTALLED`; không coi đó là desktop READY. `Publish-DesktopRelease.ps1` gọi probe này và chuyển cấu hình đã kiểm vào MSBuild.
- `DesktopWebViewRuntimeTests.cs` và `DesktopReadinessCommandTests.cs`: regression nhánh lỗi, thứ tự khởi tạo, thử lại, nhận dạng lệnh và subprocess dùng PATH tối giản.

| Kiểm tra thực chạy | Passed | Failed | Skipped | Bằng chứng/giới hạn |
|---|---:|---:|---:|---|
| Restore + Release solution build | Đạt | 0 | 0 | Build cuối 0 Warning / 0 Error MSBuild; frontend build được chạy bởi target |
| Test .NET đầy đủ, collection tuần tự | 1.528 | 0 | 13 | 1.541 tổng, 5 phút 11 giây; TEMP/TMP riêng trên D; TRX lưu bên dưới |
| Nhóm regression WebView2 chạy riêng | 11 | 0 | 0 | Cũng nằm trong lượt full, không cộng trùng |
| Frontend `npm ci`, `npm run build`, `npm test` | 268 test | 0 | 0 | 43 file; Vite còn cảnh báo chunk >500 kB |
| Publish single-file, self-contained, win-x64, ReadyToRun | Đạt | 0 | 0 | Hai cảnh báo NETSDK1198 vì project updater/worker không có FolderProfile; publish hai project con vẫn thành công |
| Kiểm gói và config thay thế | 42 component / 24 web asset | 0 | 0 | Hash web khớp build; config publish khớp chính xác file được chọn; không có mật khẩu SQL nhúng |
| Gói thật với PATH chỉ gồm Windows, working directory khác | Đạt | 0 | 0 | `--check-webview2`: exit 0 / READY |
| Fixture thiếu DLL / DLL hỏng / PE x86 trên gói mới | 3 | 0 | 0 | EXE exit 2 với đúng error code; script kiểm gói từ chối cả ba; loader được phục hồi và probe lại thành công |
| `--check-desktop` trên gói mới, PATH tối giản | Đạt | 0 | 0 | exit 0; WebView2/media/OCR/Piper READY, Qwen DISABLED đúng cờ; SQL/server chưa kiểm |

Các bài model/runtime/SQL opt-in bị skip không chứng minh model hoặc môi trường đích đã đạt. Nhánh thiếu Evergreen được kiểm bằng exception giả trong unit test; không gỡ Evergreen khỏi máy hiện tại. Không đăng nhập hoặc smoke UI trên máy đích, không tải/chạy inference model để thu readiness, không gọi provider hoặc database thật.

## Gói kiểm chứng và cách dùng

Thư mục: `D:\VideoMakerDiagnostics\webview2-loader-20260923-153932\desktop-publish`.

- Môi trường probe: Windows x64 `10.0.26200.0`, có Evergreen Runtime; cấu hình build Release nhưng **mức phê duyệt bundle là Development**. Không dùng kết quả này để nâng FFmpeg từ Development lên Release.
- 321 file, 1.104.015.243 byte. EXE SHA-256: `3A12BB078899982EE31EDD48476BBB20931BB17E9176B0FFDEA8D1AB0D126D04`.
- Loader SHA-256 giữ đúng package đã pin: `A9A09232C25805323D4CFB3FC8F545A190A9C8A99C93262EA99D0B88DF99EC90`; chữ ký Microsoft `Valid` sau khi phục hồi fixture.
- Cấu hình thử giữ endpoint/đường dẫn triển khai và đã loại mật khẩu SQL. Cấu hình nguồn cùng gói cũ không bị sửa. Khi kiểm workflow thật phải dùng cấu hình truy cập SQL riêng phù hợp; gói này không chứng minh database/server đã sẵn sàng.
- Bằng chứng trong thư mục cha: `full-suite.trx`, `publish-check.json`, `negative-publish-checks.json`, `webview-probe.json`, `desktop-probe.json`, `publish-sha256.json`, `artifact-summary.json`.

Sao chép **toàn bộ** thư mục `desktop-publish` sang máy thử Windows x64. Mở PowerShell tại đó và chạy:

```powershell
.\TOOL-LOCAL.exe --check-webview2 | Out-String
.\TOOL-LOCAL.exe --check-desktop | Out-String
```

Kết quả WebView2 mong đợi là `READY`. Nếu `webview2_runtime_missing`, cài Microsoft Edge WebView2 Evergreen Runtime x64 rồi kiểm tra lại. Nếu `webview2_loader_missing` hoặc `webview2_loader_invalid`, giải nén/sao chép lại đầy đủ gói. Sau đó mở EXE để nghiệm thu login trên máy đích; khả năng truy cập workflow SQL là bước kiểm độc lập.

Có thể tạo lại gói thử bằng cấu hình triển khai đã kiểm, không chứa secret:

```powershell
dotnet publish TOOL-LOCAL\TOOL-LOCAL.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishProfile=FolderProfile `
  '-p:PublishDir=D:\VideoMakerDiagnostics\webview2-loader-20260923-153932\desktop-publish\' `
  '-p:DesktopDeploymentSettingsPath=D:\VideoMakerDiagnostics\webview2-loader-20260923-153932\appsettings.diagnostic.json'
.\scripts\Test-DesktopSetupPublish.ps1 `
  -PublishRoot 'D:\VideoMakerDiagnostics\webview2-loader-20260923-153932\desktop-publish' -ProbeWebView2
```

Đây là lệnh tạo artifact Development local. Phát hành chính thức vẫn dùng quy trình kiểm cấu hình, provenance, manifest và rollback của `Publish-DesktopRelease.ps1`.
