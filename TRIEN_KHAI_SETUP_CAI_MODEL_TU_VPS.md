# Triển khai Setup cài ứng dụng và model từ VPS

Ngày: **2026-10-16**. Checkout `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b` và working tree chưa commit. Đọc cùng [kế hoạch SC01–SC36](PLAN_TASK_SETUP_CAI_UNG_DUNG_VA_MODEL_TU_VPS.md).

**Đã triển khai source và hoàn tất kiểm thử tự động. Chưa chạy Setup cài đầy đủ trên Windows sạch, chưa nghiệm thu model thật hoặc phát hành production trong lượt này.** Các kết quả cuối và phạm vi nghiệm thu được ghi tại mục 5–7, không suy từ việc URL VPS tải được.

## 1. Luồng mới

Chế độ mới `StaticArchive` thực hiện trong cùng một lần chạy Setup:

1. Ghép chính xác version/build/channel/SHA-256 package desktop với bộ Setup được phát hành.
2. Tải tiếp package ứng dụng, kiểm size/hash/ZIP và stage bộ ứng dụng mới.
3. Kiểm prerequisite trước tải model lớn. Nếu thiếu WebView2, kiểm và chạy installer Microsoft đã pin, sau đó kiểm lại.
4. Tính dung lượng còn cần trên từng volume, có tính cache/runtime đã xác minh.
5. Tải ZIP model từ VPS bằng HTTPS, hỗ trợ Range/If-Range, kiểm full SHA-256 và inventory/compatibility.
6. Import Qwen/Piper từ gói bàn giao. OCR/FFmpeg/native/worker vẫn đi cùng desktop; không cài năm nupkg OCR thành runtime rời.
7. Backup và thay file managed ứng dụng có journal; chuẩn bị runtime Qwen/Piper dưới đúng Windows user.
8. Probe Qwen Anh/Trung, Piper WAV, OCR Anh/Trung, media và WebView2.
9. Bootstrap ghi evidence nhưng giữ pending. Setup kiểm đủ năm kết quả, commit giao dịch dưới khóa độc quyền rồi mới bỏ pending, tạo shortcut và cho mở ứng dụng.

Bỏ chọn "mở ứng dụng sau cài" không bỏ qua bước model/probe. Lỗi, hủy hoặc thiếu xác nhận RAM/commit không được báo hoàn tất.

Mode cũ `Bundled` và `Server` được giữ. Mặc định source không tự chuyển mọi bản build sang StaticArchive; bản phát hành phải chọn rõ chế độ và cấu hình phù hợp.

## 2. Các phần đã thêm hoặc cập nhật

| Phần | Source | Hành vi |
|---|---|---|
| Contract | [StaticModelSetupContracts](TOOL-SHARED.Contracts/ModelDistribution/StaticModelSetupContracts.cs) | Progress/result có version, operation/bundle ID, component result, resource profile và dự toán runtime |
| Pin ZIP | [ApprovedStaticModelBundle](TOOL-DISTRIBUTION/StaticModels/ApprovedStaticModelBundle.cs) | Compile pin đúng URL, size/hash/inventory, worker/protocol và component |
| Tải/archive | [StaticModelArchiveDownload](TOOL-DISTRIBUTION/StaticModels/StaticModelArchiveDownload.cs), [StaticModelArchiveStore](TOOL-DISTRIBUTION/StaticModels/StaticModelArchiveStore.cs) | Stream `long`, cache bền, resume/retry/hủy, kiểm archive/inventory/compatibility và mapping |
| Điều phối Setup | [LauncherInstallerService.Static](TOOL-SETUP/LauncherInstallerService.Static.cs) | Release binding, prerequisite/disk, model cache, transaction, probe và complete có điều kiện |
| Giao dịch | [StaticSetupTransaction](TOOL-SETUP/StaticSetupTransaction.cs) | Journal/backup bền, intent trước mutation, kiểm backup trước restore, giữ cấu hình và file ngoài manifest |
| Tiến trình | [StaticBootstrapRunner](TOOL-SETUP/StaticBootstrapRunner.cs) | Job Object, handshake trước chạy native, timeout/hủy, drain có giới hạn và kiểm typed result |
| Bootstrap | [StaticModelBootstrapCommand](TOOL-LOCAL/SystemSetup/StaticModelBootstrapCommand.cs) | Chạy trước login/SQL, dùng engine production và bộ component cố định |
| Policy/runtime | [StaticModelRuntimePolicy](TOOL-LOCAL/ModelDistribution/StaticModelRuntimePolicy.cs) | Gate pending/receipt/fingerprint, lease, cache binding và kiểm trạng thái thu hồi đã biết |
| WebView2 | [StaticModelWebViewProbe](TOOL-LOCAL/SystemSetup/StaticModelWebViewProbe.cs) | Kiểm khởi tạo controller thực trên STA, không chỉ kiểm file/registry |
| Cấu hình | [DesktopOptions](TOOL-LOCAL/Configuration/DesktopOptions.cs) | Policy StaticArchive áp dụng bốn tính năng local trong bộ nhớ sau merge; giữ nguyên file config và phần Server/SQL/workspace/speech |
| Publish | [Publish-DesktopRelease](scripts/Publish-DesktopRelease.ps1), [validator model](scripts/Test-ModelDistributionPublishSettings.ps1), [validator desktop](scripts/Test-DesktopSetupPublish.ps1) | Thêm StaticArchive, ghép metadata Setup/desktop và kiểm pin prerequisite/policy |
| Prerequisite | [WebView2Prerequisite](scripts/WebView2Prerequisite.psm1), [pin](third_party/webview2/WEBVIEW2_INSTALLER_APPROVED.json), [nguồn](third_party/webview2/PROVENANCE.md) | Kiểm size/hash/chữ ký Microsoft trước đóng gói và trước chạy |

StaticArchive không nhận nguồn model tùy ý từ DOM/config. Bản phát hành dùng compile pin; không tự fallback Hugging Face hoặc dò ancestor của checkout để lấy model. Qwen được copy/kiểm hash từ payload, không hardlink runtime vào cache. Piper dùng Python/uv/wheel offline của bundle, tạo venv ở đường runtime cuối trên máy khách.

Các trạng thái thu hồi đã biết thuộc repo/channel/platform hiện hành được kiểm từ trust cache cũ; state hỏng không được coi là không có thu hồi. Cổng model 8443 không thay validation của provider AI hoặc chế độ signed catalog.

## 3. Đầu vào model và WebView2

| Artifact | Số byte | SHA-256 |
|---|---:|---|
| ZIP VPS | 2.847.980.994 | `e9939863910af178ec6be6e99f7fcc16948b6a8d916bd5b32bc0a50d92d3f471` |
| Inventory | Theo file đã pin | `2f387304020b06c9c59747eb3cbf3dda08e4b029eafd49d4c77581c4154d4843` |
| Microsoft WebView2 Standalone x64 installer | 214.131.448 | `3505a37b54a294c2bc423b30d6c02cd33a2e58dbcdc7e03b50d7a4112746c986` |

URL model: `https://103.179.190.182:8443/models/VideoMaker-Models-20261016-082700.zip`.

Đã kiểm full GET/Range/hash ZIP ở lượt trước; trong lượt triển khai này đã đọc central directory, inventory và compatibility của ZIP gốc: 33 entry, 31 file inventory, expanded 2.918.879.650 byte, metadata và ba artifact pin khớp. Không tải lại 2,85 GB hoặc chạy inference để tạo kết quả này. [Bằng chứng metadata](D:/vm-tests/static-model-metadata-20261016-232504/metadata-verification.json).

Đã tải WebView2 từ nguồn Microsoft qua HTTPS, kiểm Authenticode `Valid`, publisher Microsoft Corporation, thumbprint `21CA9DD6DA7C57C3A599880D17317D7BDA31899C`. File được giữ ngoài Git tại [installer đã kiểm](C:/vm-setup-verification/20261016-231825-26ae68/MicrosoftEdgeWebView2RuntimeInstallerX64.exe). [Báo cáo kiểm](C:/vm-setup-verification/20261016-231825-26ae68/webview-installer-verification.json). **Chưa chạy installer này trên máy.** Phiên bản installer không thay bằng chứng version/khởi tạo runtime sau cài.

Không ghi đè ZIP trên cùng URL bằng nội dung khác. Đổi model hoặc WebView2 cần pin/release mới được kiểm; không sửa hash để chấp nhận file lỗi.

## 4. Cách chuẩn bị bản phát hành

### Profile desktop

Tạo profile phân phối riêng đã được rà soát theo quy trình hiện hành. Các field liên quan cần có:

```json
{
  "Features": {
    "VietsubEnabled": true,
    "VietsubOcrEnabled": true,
    "VietsubLocalTranslationEnabled": true,
    "VietsubLocalVoiceEnabled": true
  },
  "ModelDistribution": {
    "Mode": "StaticArchive",
    "Enabled": false
  }
}
```

Đây là phần cấu hình liên quan, không phải toàn bộ appsettings để chép vào bản cài. Profile vẫn phải có Server, Update.Channel/Platform và phần cấu hình khách hợp lệ theo validator hiện hành. Không lấy nguyên cấu hình phát triển hoặc đóng gói credential SQL dùng chung. StaticArchive không đặt `BaseUrl`/`CacheRoot` riêng trong profile và không cần public key của catalog ký.

Để giữ tương thích khách cũ, policy release StaticArchive có quyền quyết định bốn tính năng local bắt buộc trong **bộ nhớ**. User config cũ không thể tắt Qwen để bộ cài báo đủ giả hoặc tạo vòng lặp Setup. Cấu hình server, SQL, workspace, speech và file cấu hình vẫn được giữ.

### Lệnh publish dự kiến

Sau khi chốt version/build/channel, profile và gate phát hành:

```powershell
.\scripts\Publish-DesktopRelease.ps1 `
  -Version '<version-moi>' `
  -BuildNumber <build-moi> `
  -Channel Development `
  -ServerBaseUrl 'https://<may-chu-ung-dung>/' `
  -AppSettingsPath 'C:\ReleaseProfiles\customer.json' `
  -ModelDeliveryMode StaticArchive `
  -WebView2InstallerPath 'C:\Prerequisites\MicrosoftEdgeWebView2RuntimeInstallerX64.exe'
```

Các placeholder phải được thay bằng đầu vào đã chốt. Lệnh này **chưa chạy trong lượt triển khai**. Giữ tham số/quy trình Authenticode, FFmpeg Release, OCR native và SQL chuyển tiếp đã yêu cầu của dự án. Không thêm `-IncludeQwenModel` hoặc `-PiperBundlePath`: payload model đi từ VPS; worker/native và WebView2 prerequisite vẫn nằm trong package desktop.

Script truyền compile flag `StaticModelSetupRequired=true`, channel cố định và metadata version/build/hash desktop vào Setup. Package desktop tiếp tục chịu cap 2 GiB của updater; ZIP model nằm riêng trên VPS. Setup chỉ hiển thị package khớp binding, nên server ứng dụng phải có release phù hợp, Active/đến lịch theo luồng phân phối hiện hành. Đổi đường model không thay API server này.

### Đường dữ liệu máy khách

| Dữ liệu | Đường mặc định |
|---|---|
| Ứng dụng | `%LOCALAPPDATA%\Programs\VideoMaker` hoặc thư mục local cố định được chọn |
| Cache model Setup | `%LOCALAPPDATA%\ToolGenPostVideo\system-setup\static-model-cache\<zip-hash>\` |
| Journal/backup app | `%LOCALAPPDATA%\ToolGenPostVideo\system-setup\installer-transactions\<target-key>\` |
| Component Qwen/Piper | Dưới `%LOCALAPPDATA%\ToolGenPostVideo\components\`, theo engine/bundle đã pin |
| Pending và receipt model | `%LOCALAPPDATA%\ToolGenPostVideo\system-setup\static-model-install*.json` |
| Receipt ứng dụng | `<InstallRoot>\static-setup-receipt.json` |

Chọn ứng dụng ở D không tự chuyển component khỏi ổ chứa LocalAppData. Bản đầu chưa có UI di chuyển model hoặc chọn root component tùy ý. Kiểm dung lượng tính riêng từng ổ và có thể bảo thủ với archive/staging; không lấy 2,85 GB làm yêu cầu free disk tổng.

## 5. Kiểm thử trên source lần này

- Restore solution đã đạt.
- Build Release toàn solution đã đạt **0 Error / 2 Warning**. Hai warning NU1902/NU1903 của `System.Security.Cryptography.Pkcs` 9.0.11 là dependency hiện hữu, chưa nâng trong thay đổi này.
- Nhóm test StaticModel/StaticSetup: **100 Passed / 0 Failed / 0 Skipped**.
- Nhóm StaticModel/StaticSetup cùng regression offline sau sửa cuối: **108 Passed / 0 Failed / 0 Skipped**; các bài này đã nằm trong full suite, không cộng trùng.
- Full .NET cuối: **2.150 Passed / 0 Failed / 18 Skipped / 2.168 Total**, thời lượng **4 phút 18 giây**. Những bài SQL/model/native opt-in bị skip không được tính là nghiệm thu môi trường đích hoặc model thật.
- Frontend `npm ci`, production build và test đạt: **322 Passed / 0 Failed / 0 Skipped**, 47 file; build còn cảnh báo chunk lớn hiện hữu.

Bằng chứng nằm tại [thư mục kiểm tra](C:/vm-setup-verification/20261016-231825-26ae68), tổng hợp trong [build-test-summary.json](C:/vm-setup-verification/20261016-231825-26ae68/build-test-summary.json): log restore/build, TRX của từng lượt và hash binary test. `source-final-tests.json` chụp 1.113 file source/config/tài liệu trước full suite cuối; kiểm ngay sau test cho kết quả không có file thay đổi. Cập nhật biên bản sau đó chỉ đổi tài liệu, được ghi riêng trong `source-after-final-tests-differences.json`. Số skipped lấy từ outcome `NotExecuted` trong TRX, không theo counter adapter.

Giữ nguyên log/TRX của các lượt lỗi: full suite đầu **2.148 Passed / 2 Failed / 18 Skipped** phát hiện hai ca Piper mất mã lỗi thu hồi đã biết khi payload bị thiếu. Đã thu hẹp xử lý lỗi quanh bước kiểm trust để giữ `VOICE_OFFLINE_BUNDLE_INVALID`, giữ hành vi lỗi runtime cũ ở các bước khác; không sửa assertion để bỏ qua lỗi. Sau đó build, nhóm regression 108 bài và full suite cuối đều đạt. Các build thất bại ban đầu do xung đột type BrandIdentity và alias bị mất ở resolved reference đã được sửa bằng alias riêng trong project test; không sửa BrandIdentity của người dùng.

Fixture test HTTP/ZIP/transaction sử dụng file nhỏ và handler giả. Chúng kiểm logic từ chối/resume/rollback, không chứng minh model thật đã chạy hoặc Setup đã cài trọn bộ trên máy khách.

## 6. Giới hạn và thao tác khi lỗi

- Chưa chạy full ZIP download → production bootstrap → login/workflow trên Windows sạch. Các task nghiệm thu SC28–SC36 giữ trạng thái chưa đạt khi chưa có bằng chứng tương ứng.
- Chưa build/upload bản phát hành Setup/desktop mới qua pipeline Release; còn cần version/build/channel, profile khách, gate FFmpeg/phân phối và chính sách ký đã chốt. Chưa thay VPS, Ghepgo, database hoặc release Active.
- Cài đè binary legacy chưa có receipt/khóa tương thích bị chặn. Chọn thư mục trống mới; không xóa ứng dụng/dữ liệu cũ. Update/repair trong phạm vi binary StaticArchive tương thích có transaction riêng.
- Quét tiến trình chặn ứng dụng/worker cũ đang chạy trước khi cài. Bản legacy ở thư mục khác không biết khóa mới, nên không mở nó đồng thời trong khi cài/probe. Chưa coi tình huống mở binary legacy bất kỳ giữa phiên cài là đã nghiệm thu.
- Hủy hoặc lỗi sau khi thay app: cache/journal được giữ; file managed có thể được phục hồi nhưng pending vẫn yêu cầu verify lại model. Không tuyên bố mọi app/runtime cũ đã dùng lại được ngay sau rollback. Chạy đúng Setup và bấm thử lại để xác minh/hoàn tất.
- Pending/receipt/journal hỏng không được xóa thủ công để vượt gate. Giữ log, kiểm đúng user/target và thực hiện verify/recovery; dữ liệu không có backup hợp lệ thì dừng sửa tự động.
- WebView2 UAC/reboot hoặc Windows N thiếu Feature Pack vẫn là prerequisite cần xử lý; không tự reboot, chạy DISM hoặc copy DLL hệ thống. Không có READY khi prerequisite chưa đạt.
- Chưa nghiệm thu mọi đường dẫn dài/username Unicode và CPU/RAM được hỗ trợ bằng cài thật. Piper luôn tạo venv ở đường cuối; không lấy venv của máy đóng gói để né lỗi.
- Chưa chạy model thật, nghe giọng hoặc xuất video trong lượt này; không có request provider có phí.

## 7. Bước nghiệm thu tiếp theo

1. Chốt profile, version/build/channel và các gate phát hành còn thiếu; tạo candidate bằng pipeline StaticArchive.
2. Kiểm package/Setup/metadata/hash thật và xác minh package ứng dụng được phục vụ đúng API release.
3. Dùng Windows/profile sạch có đủ tài nguyên, không cache/model/dependency developer. Cài bằng Setup, ghi bytes/hash và probe từng component.
4. Thử mất mạng, đóng Setup, thiếu đĩa, archive sai, hủy khi uv/worker chạy, crash trước/sau commit và chạy lại.
5. Mở app sau đăng nhập/license, kiểm không tải model lại; OCR → Qwen → Piper → nghe mẫu → xuất SRT/MP4.
6. Ghi riêng kết quả kiểm file, test tự động, smoke runtime, Setup thật và rollout. Chỉ sau nghiệm thu mới nâng trạng thái của bộ cài.
