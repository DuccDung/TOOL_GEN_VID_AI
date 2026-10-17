# Publish TOOL-LOCAL bằng Visual Studio

Luồng mới là **Publish → lấy ZIP được tạo tự động → upload VPS → nhập phiên bản và link ZIP → Lưu nháp → Phát hành**. Chọn `TOOL-LOCAL → Publish → FolderProfile → Publish` như bình thường. Profile xuất thư mục ứng dụng và một ZIP có checksum trong tên file; không cần nén bằng tay, tạo JSON, nhập checksum hoặc ký gói riêng.

Trong chế độ đang chọn **StandaloneCatalog**, server chỉ lưu thông tin release trong database, lấy version/build/checksum khai báo từ tên ZIP, không tải hoặc kiểm ZIP trên VPS và không yêu cầu Setup Active. **Setup từ 1.3.0**, capability `3,4,5`, tải trực tiếp VPS, đối chiếu checksum rồi kiểm file và cài ứng dụng/model. Upload EXE do `Publish-SharedDesktopSetup.ps1` tạo, giữ tên có hash và lưu link tại mục tải Setup của Admin một lần. [Runbook hiện hành](../TRIEN_KHAI_PHAT_HANH_DON_GIAN_VPS.md) ghi thứ tự SQL 4.1.21/deploy. DirectCatalog 4/Setup 1.2.0, `ServerInspection` và gói có ba JSON vẫn giữ tương thích release cũ.

Tài liệu mô tả source trên nhánh `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, có thay đổi chưa commit. Bản thân tài liệu không xác nhận đã bấm Publish trong Visual Studio, cài trên Windows sạch hoặc nghiệm thu model thật. Kết quả kiểm thử được ghi riêng trong biên bản của lượt triển khai.

**1. Đầu vào mặc định của máy build**

Trên máy hiện tại đã chuẩn bị `TOOL-LOCAL/appsettings.release.json` riêng, không có SQL password dùng chung, cùng OCR native và WebView2 đúng pin. `FolderProfile` tự chuẩn bị FFmpeg 9.0.1 từ bộ binary đã chốt và hồ sơ kỹ thuật trong repository; không cần nhập đường dẫn hoặc duyệt lại FFmpeg mỗi lần Publish. Version/build phải đặt đúng trước mỗi lượt phát hành, không lấy tên một ZIP cũ làm giá trị hiện hành.

`FolderProfile.local.props` là tùy chọn. Khi cần override version/build hoặc đường dẫn trên máy build, có thể dùng [FolderProfile.local.props.example](../TOOL-LOCAL/Properties/PublishProfiles/FolderProfile.local.props.example) làm mẫu cho file đó. Ví dụ dưới đây không phải cấu hình đã tạo trong lượt hiện tại:

```xml
<Project>
  <PropertyGroup>
    <DesktopPublishVersion>1.2.2</DesktopPublishVersion>
    <DesktopPublishBuildNumber>37</DesktopPublishBuildNumber>
    <!-- appsettings.release.json bên cạnh TOOL-LOCAL.csproj là mặc định. -->
    <DesktopReleaseProfile>Production</DesktopReleaseProfile>
    <DesktopAllowTransitionalSql>true</DesktopAllowTransitionalSql>
  </PropertyGroup>
</Project>
```

File `.local.props` chứa đường dẫn và thông tin phát hành của máy build; không chứa SQL password, API key hoặc token. Không commit file này. DirectCatalog yêu cầu Setup từ 1.2.0 hỗ trợ catalog 4. Đổi nơi lưu ZIP bằng `DesktopCanonicalPackageOutputRoot`; thư mục đó phải tách khỏi thư mục Publish. Mặc định là `artifacts/releases/direct`.

Các bộ đầu vào phải đáp ứng kiểm tra hiện có:

- **FFmpeg:** mặc định tự chuẩn bị offline tại `artifacts/ffmpeg-release/gyan-9.0.1-essentials-win-x64`. Helper đối chiếu [FFMPEG_RELEASE_APPROVED.json](../third_party/ffmpeg/FFMPEG_RELEASE_APPROVED.json), kiểm hash tám file, kiểm phiên bản FFmpeg/FFprobe và dùng lại bundle khi còn nguyên vẹn. Bộ gốc `third_party/ffmpeg/win-x64` giữ scope `Development`; bundle riêng có scope `Release` cho `TechnicalPackaging`, kèm license, provenance, README nguồn, hồ sơ nguồn và probe. Không tải lại hoặc sửa binary gốc. Khi cache bị thay đổi, Publish báo lỗi và giữ nguyên để kiểm tra. Hồ sơ [RELEASE_REVIEW.md](../third_party/ffmpeg/RELEASE_REVIEW.md) ghi rõ corresponding source còn chưa đủ; `correspondingSourceComplete`, `redistributionQualified`, `productionQualified` đều `false`. Phê duyệt kỹ thuật không xác nhận đã hoàn tất điều kiện phân phối GPL.
- **OCR native:** thư mục runtime đúng bộ được `Test-OcrNativeRuntime.ps1` kiểm tra. Bộ mặc định của repository nằm tại `artifacts/ocr-native-runtime/msvc-14.50.35719.0-win-x64`; bộ tại máy hiện tại đã kiểm đủ bốn file/hash. Nếu dùng nơi khác, thêm `OcrNativeRuntimeDirectory` vào `.local.props`.
- **WebView2:** offline installer x64 phải khớp dung lượng, SHA-256 và chữ ký Microsoft trong [WEBVIEW2_INSTALLER_APPROVED.json](../third_party/webview2/WEBVIEW2_INSTALLER_APPROVED.json). Đầu vào hiện tại là `artifacts/prerequisites/webview2-runtime-x64.exe`; có thể override bằng `DesktopWebView2InstallerPath`. Setup dùng chung mới nhúng installer này một lần khi build, nên ZIP ứng dụng thường không phải kèm installer. Nếu WebView2 còn thiếu, Setup lấy bản nhúng qua file `.part`, kiểm lại size/hash/chữ ký rồi mới chạy; kiểm runtime trên máy đích vẫn bắt buộc. Gói ZIP metadata tùy chọn vẫn có thể kèm `prerequisites/webview2-runtime-x64.exe`. Các đầu vào lớn trong `artifacts` không thuộc Git; máy build khác phải chuẩn bị theo pin.
- **Cấu hình phát hành:** mặc định `TOOL-LOCAL/appsettings.release.json`, đã chuẩn bị riêng trên máy hiện tại và được Git bỏ qua. File đúng API/profile local, không chứa SQL password dùng chung. Không chọn nguyên `TOOL-LOCAL/appsettings.json` phát triển nếu còn password hoặc dữ liệu riêng. Đổi nơi lưu bằng `DesktopDeploymentSettingsPath` nếu cần.

Máy build khác vẫn phải có binary FFmpeg nguồn đúng pin, OCR và WebView2. Cache và các đầu vào lớn không được đưa vào Git. Sau khi chuẩn bị, các lần Publish sau kiểm và dùng lại tự động.

**2. Chuẩn bị cấu hình phát hành**

Ví dụ cấu trúc tối thiểu dưới đây dùng toàn bộ địa chỉ và tài khoản giả. Thay bằng API và thông tin workflow SQL do quản trị viên cấp; không dùng nguyên ví dụ cho khách:

```json
{
  "Server": {
    "BaseUrl": "https://api.example.com/"
  },
  "Database": {
    "ConnectionString": "Server=tcp:sql.example.com,1433;Database=VideoMakerWorkflow;User ID=desktop_customer;Encrypt=True;TrustServerCertificate=False;Persist Security Info=False"
  },
  "Storage": {
    "WorkspaceRoot": "%LOCALAPPDATA%/VideoMaker/Workspace"
  },
  "MediaTools": {
    "FfmpegPath": "tools/ffmpeg/ffmpeg.exe",
    "FfprobePath": "tools/ffmpeg/ffprobe.exe"
  },
  "Update": {
    "Enabled": true,
    "Channel": "Stable",
    "Platform": "win-x64",
    "CheckIntervalSeconds": 120
  },
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

`Server.BaseUrl` là API VideoMaker, khác link ZIP trên VPS. API/profile/channel phải khớp Setup dùng chung đã đăng ký. `StaticArchive` dùng bundle model đã pin; không đặt thêm `BaseUrl` hoặc `CacheRoot` riêng trong `ModelDistribution`. Bật bốn feature chỉ cho phép luồng cài và sử dụng tương ứng; trạng thái sẵn sàng vẫn phải qua kiểm tra trên máy đích.

Desktop hiện vẫn dùng workflow SQL. `DesktopAllowTransitionalSql=true` chỉ cho phép profile chuyển tiếp; nó không cấp quyền SQL, tạo user, nhập password hoặc chuyển ứng dụng sang API-only. Chuỗi không password trong ví dụ có thể qua kiểm tra đóng gói nhưng chưa đủ để đăng nhập SQL bằng SQL authentication.

Trước khi sử dụng trên máy khách, quản trị viên cần cấp tài khoản riêng chỉ có quyền workflow `vf`, rồi cấu hình riêng trên máy đó. Công cụ [Configure-CustomerSql.ps1](Configure-CustomerSql.ps1) hỗ trợ ghi `appsettings.user.json` với ACL của người dùng hiện tại và hỏi password bằng ô nhập bảo vệ. Chạy script được cung cấp riêng tại máy khách, ví dụ:

```powershell
.\Configure-CustomerSql.ps1 `
  -PackageRoot "$env:LOCALAPPDATA\Programs\VideoMaker" `
  -SqlServer 'sql.example.com' -Port 1433 `
  -Database 'VideoMakerWorkflow' -UserName 'desktop_customer'
```

Thay thư mục cài và thông tin giả bằng giá trị thật được cấp. Script không kiểm kết nối, quyền hoặc schema và không ghi đè `appsettings.user.json` đã có; cần kiểm riêng các điều kiện đó. Không gửi file cấu hình riêng này vào ZIP hoặc đưa credential vào chat/log. Tích hợp Publish không bổ sung màn hình tự nhập SQL khi khởi động và không tự cung cấp script này cho máy khách.

**3. Chọn version/build rồi bấm Publish**

Mặc định hiện tại là version `1.0.0`, build `1` trong project. Khi phát hành nội dung mới, người phát hành quản lý version/build và có thể override bằng `DesktopPublishVersion`, `DesktopPublishBuildNumber` trong `.local.props` tùy chọn. Version dùng dạng `major.minor.patch`, ví dụ `1.2.2`, không có tiền tố `v`, hậu tố hoặc số có số 0 thừa. Build là số nguyên dương; quy trình không tự tăng hai giá trị này.

Trong Visual Studio, chọn `TOOL-LOCAL`, profile `FolderProfile`, cấu hình `Release`, runtime `win-x64`, self-contained, rồi bấm **Publish** như bình thường. Hook chuẩn bị FFmpeg tự động, thêm đủ tám file vào danh sách Publish ngay cả khi cache chưa có ở đầu lượt và kiểm các đầu vào. Thiếu file nguồn hoặc checksum, OCR, WebView2 hay cấu hình không hợp lệ sẽ làm Publish báo lỗi trong Output.

Sau khi Publish báo thành công, lấy đường dẫn **VideoMaker ZIP** trong cửa sổ Output. Ví dụ với version `1.2.2` và build `37`, file nằm tại:

```text
artifacts/releases/direct/1.2.2-37/
  VideoMaker-1.2.2-37-win-x64-<sha256-64-ký-tự>.zip
```

Đây là một file ZIP duy nhất, không cần upload thêm JSON hoặc file checksum. Giữ nguyên tên và bytes sau upload. Tên bắt buộc có dạng `VideoMaker-<version>-<build>-win-x64-<sha256>.zip`; build nguyên dương, SHA-256 là 64 ký tự hex chữ thường. Server phân tích tên này mà không gọi VPS; Setup kiểm bytes thực có đúng checksum đó hay không.

Packager đọc trực tiếp danh sách file từ lượt Publish hiện tại, tạo entry với dấu `/` chuẩn, kiểm từng file và ZIP trước khi promote từ `.part`. Không tạo bản sao đầy đủ payload hoặc giải nén toàn bộ ZIP chỉ để nén lại. ZIP không chứa ba JSON phát hành; Setup sinh các file nội bộ này sau khi xác minh gói. Bộ model/cache/READY/receipt/credential riêng và thư mục phát triển bị chặn khỏi payload. Việc mở trực tiếp EXE vừa Publish vẫn không thay quá trình cài bằng Setup.

`DesktopCreateCanonicalReleaseZip=true` là mặc định. Nếu tắt để chỉ kiểm thư mục Publish, sẽ không có ZIP dùng cho DirectCatalog; bật lại trước lượt phát hành. Không đổi tên một ZIP nén thủ công thành tên chứa hash tùy ý: checksum phải là SHA-256 của chính ZIP nguyên vẹn. `publish-01.zip` cũ không đáp ứng tên chuẩn của DirectCatalog; Publish lại bằng source mới để có ZIP đúng tên.

Nếu vẫn muốn pipeline cũ tạo ZIP kiểm chứng tại máy build, thêm tùy chọn sau vào `.local.props`:

```xml
<DesktopCreateVerifiedReleaseZip>true</DesktopCreateVerifiedReleaseZip>
```

Khi bật, ZIP cùng báo cáo xuất tại:

```text
TOOL_GEN_VID_AI/
  artifacts/releases/1.2.2-37/
    VideoMaker-1.2.2-37-win-x64.zip
    checksums.sha256
    package-report.json
```

ZIP tùy chọn có `TOOL-LOCAL.exe` cùng ba JSON ở gốc và dùng với luồng ServerInspection cũ. Chế độ này cần thêm dung lượng staging; khi bật `DesktopCreateVerifiedReleaseZip=true`, target canonical không chạy để tránh xuất cả hai gói. Mặc định giữ `false` để tự tạo ZIP canonical cho DirectCatalog.

Nếu ZIP tạo lại có đúng bytes/hash, packager dùng lại file cùng tên. Nếu bytes khác, tạo tên có hash khác và giữ nguyên ZIP cũ; server không cho thay version/URL của release đã từng phát hành. Tăng phiên bản cho nội dung phát hành mới, đồng thời quản lý build; không dùng nhiều bộ bytes cho một version đã công bố. Tên chứa hash là checksum do người phát hành khai báo qua link, không thay chữ ký nhà phát hành hoặc chứng nhận an toàn của ứng dụng.

**4. Upload và tạo release**

Trước khi dùng DirectCatalog, áp [migration 4.1.19](../database/VideoFactory.4.1.19.DirectDesktopCatalog.sql) theo runbook triển khai, deploy server source mới, đặt `DesktopReleases.PublicationMode=DirectCatalog` và bật Setup dùng chung với API/profile thật. Đăng ký/kích hoạt **Setup từ 1.2.0 có capability catalog `3,4`**, được build một lần bằng [Publish-SharedDesktopSetup.ps1](Publish-SharedDesktopSetup.ps1) với installer WebView2 đã pin. Setup 1.1.0 không hỗ trợ catalog 4. Không cần build lại Setup cho mỗi ZIP tương thích; không áp SQL hoặc deploy chỉ bằng việc đọc tài liệu này.

Upload ZIP ứng dụng lên nơi VPS đang phục vụ HTTPS. Source mới hỗ trợ hai prefix đã duyệt, đồng thời server đích phải cấu hình allowlist tương ứng:

```text
https://103.179.190.182:8443/apps/videomaker/1.2.2-37/VideoMaker-1.2.2-37-win-x64-<sha256>.zip
https://103.179.190.182:8443/models/VideoMaker-1.2.2-37-win-x64-<sha256>.zip
```

VPS/Nginx phải phục vụ được URL tải trực tiếp; Publish không cấu hình VPS. Trong Admin, nhập **phiên bản khớp binary + URL đầy đủ của ZIP đúng tên**, tùy chọn ghi chú/bắt buộc cập nhật, rồi **Lưu nháp → Phát hành**. Không chờ server tải hoặc kiểm ZIP; trạng thái “Đang phân phối” chỉ xác nhận thông tin release đang được cung cấp. Server không tự biết dung lượng hoặc xác nhận file trên VPS tồn tại. Model vẫn tải từ bundle riêng đã pin.

Ví dụ build `1.2.2`/`37` thì Admin nhập `1.2.2`; không nhập phiên bản khác để biến ZIP cũ thành bản mới. Khi ứng dụng/phiên bản đã công bố cần đổi bytes, tăng version và Publish lại; giữ nguyên các URL đã phát hành.

Chạy Setup mới trỏ đúng API/profile để cài. Setup lấy bản khai báo qua API, tải ZIP thẳng VPS, kiểm hash, cấu trúc, version/build và chính sách runtime trước transaction. Model, OCR, media, WebView2 và rollback vẫn phải đạt. Link 404, ZIP sai hash hoặc binary sai version làm cài thất bại; server đã lưu/phát hành thông tin không làm các phép kiểm đó tự đạt.

Desktop/Setup cũ chỉ hiểu release protocol 3 không tự hiểu catalog 4. Cần Publish desktop từ source mới và dùng Setup mới cài một lần để chuyển sang luồng cập nhật trực tiếp; cài lại đúng EXE cũ bằng Setup mới không bổ sung khả năng updater vào EXE đó. Các release cũ tiếp tục dùng đường tương thích ServerInspection.

[Biên bản ZIP Publish đơn giản](../TRIEN_KHAI_ZIP_PUBLISH_DON_GIAN.md) ghi lịch sử kiểm ZIP `/models/publish-01.zip` bằng inspector của đợt trước, không phải bằng chứng catalog mới đã deploy hoặc cài thành công. [Biên bản FFmpeg](../TRIEN_KHAI_CHUAN_HOA_FFMPEG_VA_PUBLISH.md) giữ các lượt đóng gói cũ thiếu dung lượng. Tài liệu này không tạo thêm kết quả build/test hoặc nâng trạng thái nghiệm thu model/Windows sạch/production.
