# VideoMaker

VideoMaker là ứng dụng desktop tự tạo nội dung bằng OpenAI và sinh clip bằng provider video do server chọn theo policy tổ chức. Phiên bản 4.0 sử dụng AI Gateway tập trung: API key chỉ nằm trên server, người dùng desktop không nhận, không tự nhập key và không tự chọn provider/model. Source hiện hỗ trợ Kling và BytePlus Seedance; catalog Seedance mặc định bị tắt cho tới khi quản trị viên hoàn tất migration, credential, rate và rollout theo tổ chức.

Âm thanh mặc định dùng **Provider Native Audio** ở biến thể 720p: OpenAI lập speech intent có cấu trúc, server đưa nguyên văn lời cần nói/voice style/ambience/SFX vào prompt theo provider và desktop bắt buộc nghe duyệt từng scene. TTS ghép WAV được giữ cho tính năng tương lai, không được gọi hoặc fallback trong workflow hiện tại.

Khi toàn bộ scene đã được nghe và duyệt, desktop cho phép dựng video cuối bằng FFmpeg. Luồng này chỉ nối các `SceneVideo` thuộc đúng `ApprovedGenerationId`, giữ nguyên Native Audio và kiểm tra lại hình, audio stream, mức âm lượng cùng thời lượng trước khi ghi nhận `FinalVideo`; dựng lại video không gọi provider AI.

Tài liệu chính:

- [Nghiệp vụ hệ thống](NGHIEP_VU_HE_THONG_VIDEOMAKER.md)
- [Nghiệp vụ sinh video và đồng bộ nhân vật](NGHIEP_VU_SINH_VIDEO_VA_DONG_BO_NHAN_VAT.md)
- [Hướng dẫn triển khai AI Gateway](TRIEN_KHAI_AI_GATEWAY_TO_CHUC.md)
- [Sơ đồ hoạt động API AI](SO_DO_HOAT_DONG_API_AI.docx)
- [Ngữ cảnh và quy tắc dành cho AI agent](AGENTS.md)

## Kiến trúc hiện tại

- `TOOL-SERVER`: tài khoản, JWT, license/lease thiết bị, tổ chức và thành viên, credential OpenAI/Kling/BytePlus, ngân sách, usage ledger, AI Gateway, tạo ảnh nhân vật GPT-Image-2, polling video đa provider, cache output có hạn dùng và API tải output có xác thực.
- `TOOL-LOCAL`: giao diện WinForms/WebView2, dữ liệu dự án và workspace. Người dùng có thể tạo/sinh lại ảnh chuẩn nhân vật, xem trước rồi khóa nhân vật; storyboard hiển thị lời đọc, mô tả, prompt và preview từng cảnh. Mọi request AI đều có JWT, device claim và `organizationId`, sau đó đi qua `TOOL-SERVER`.
- `TOOL-SHARED.Contracts`: hợp đồng request/response dùng chung.
- `TOOL-DISTRIBUTION`: quy tắc dùng chung để xác minh hồ sơ và SHA-256 của bundle FFmpeg trong desktop/setup/updater.
- `TOOL-TESTS`: kiểm thử quyền, gateway, định giá, SSRF, cập nhật desktop và các nghiệp vụ nền.

Admin Console tại `/admin` có mục **Tổ chức & AI** để Global Admin tạo tổ chức, quản lý bảng giá và đọc trang **Cách tính chi phí** bằng rate Active. Các tab thành viên, budget/usage, credential và audit vẫn áp dụng organization membership RBAC; global role `Admin` không tự động vượt quyền của tổ chức.

Một credential được cấp cho một tổ chức, không cấp trực tiếp cho từng người dùng. Mỗi request vẫn được ghi nhận theo cả tổ chức, thành viên, dự án, model, phiên bản credential và chi phí.

## Khởi tạo hoặc nâng cấp database

Sao lưu database trước, rồi chạy theo đúng thứ tự:

```powershell
sqlcmd -S <server> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.Initial.sql
sqlcmd -S <server> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.0.OrganizationAiGateway.sql
sqlcmd -S <server> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.1.VietnameseSeedTextRepair.sql
sqlcmd -S <server> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.2.GptImageCharacterReference.sql
sqlcmd -S <server> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.3.SceneVoiceTts.sql
sqlcmd -S <server> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.4.BytePlusSeedance.sql
sqlcmd -S <server> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.5.SceneNativeAudioStatuses.sql
```

`-f 65001` buộc `sqlcmd` đọc các file nguồn bằng UTF-8. Migration 4.0.1 sửa seed text bị sai mã hóa; 4.0.2 thêm output ảnh nhân vật có hạn dùng; 4.0.3 bổ sung nền tảng TTS tương thích; 4.0.4 thêm policy video theo tổ chức, snapshot provider/model bất biến trên project, catalog Seedance bị tắt mặc định và metadata cache video an toàn; 4.0.5 đồng bộ constraint trạng thái scene với luồng chỉnh lời và duyệt Native Audio. Chạy `VideoFactory.DesktopLeastPrivilege.sql` sau cùng để áp lại quyền deny cho các bảng mới.

Nếu desktop vẫn cần truy cập trực tiếp dữ liệu workflow trong giai đoạn chuyển tiếp, tạo user SQL riêng và chạy:

```powershell
sqlcmd -S <server> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.DesktopLeastPrivilege.sql
```

Sau đó chỉ thêm user database dành riêng cho desktop vào role `VideoMakerDesktopRole`. Không dùng chung tài khoản SQL của server với desktop.

Migration 4.0 tạo tổ chức `legacy-default` cho dữ liệu cũ với ngân sách bằng `0`. Quản trị viên phải cấu hình ngân sách, đơn giá và credential trước khi AI Gateway chấp nhận request.

## Chạy server

Cấu hình connection string và JWT signing key bằng secret của môi trường triển khai:

```powershell
dotnet user-secrets set --project TOOL-SERVER "ConnectionStrings:VideoFactory" "<server-connection-string>"
dotnet user-secrets set --project TOOL-SERVER "Jwt:SigningKey" "<random-secret-at-least-32-bytes>"
dotnet run --project TOOL-SERVER --launch-profile https
```

Không lưu API key OpenAI/Kling/BytePlus trong `appsettings.json`. Credential được gửi một lần qua API quản trị tổ chức trên HTTPS, được kiểm tra với provider, mã hóa bởi ASP.NET Core Data Protection và chỉ giải mã trong server khi gọi provider.

### Cấu hình quên mật khẩu

Luồng quên mật khẩu gửi OTP 6 số qua SMTP. OTP mặc định có hiệu lực 10 phút, mã cũ bị thay thế khi gửi lại và bị vô hiệu sau 5 lần nhập sai. Cấu hình Gmail SMTP bằng secret store:

```powershell
dotnet user-secrets set --project TOOL-SERVER "Smtp:Host" "smtp.gmail.com"
dotnet user-secrets set --project TOOL-SERVER "Smtp:Port" "587"
dotnet user-secrets set --project TOOL-SERVER "Smtp:UseStartTls" "true"
dotnet user-secrets set --project TOOL-SERVER "Smtp:User" "<gmail-address>"
dotnet user-secrets set --project TOOL-SERVER "Smtp:Pass" "<new-gmail-app-password>"
dotnet user-secrets set --project TOOL-SERVER "Smtp:TimeoutSeconds" "30"
```

Tài khoản Gmail phải bật xác minh hai bước và dùng App Password, không dùng mật khẩu đăng nhập Gmail. Không ghi `Smtp:Pass` vào `appsettings.json`, source hoặc log. OTP chỉ được lưu dưới dạng payload mã hóa Data Protection trong `AspNetUserTokens`; sau khi đổi mật khẩu, OTP cùng mọi session/refresh token hiện hữu đều bị thu hồi.

## Luồng sử dụng

1. Global Admin tạo tổ chức.
2. Owner hoặc OrganizationAdmin thêm thành viên.
3. Owner, OrganizationAdmin hoặc BillingManager đặt ngân sách tháng và hạn mức thành viên.
4. Global Admin cấu hình đơn giá model.
5. Owner hoặc OrganizationAdmin lưu/rotate credential OpenAI và provider video được tổ chức sử dụng.
6. Người dùng đăng nhập desktop, chọn tổ chức và tạo dự án.
7. Desktop gọi AI Gateway; server kiểm tra session, license, membership, vai trò, budget và idempotency trước khi gọi provider. Ảnh GPT-Image-2 được tải qua URL tương đối có xác thực, kiểm tra SHA-256 rồi lưu vào workspace; desktop không nhận URL OpenAI.
8. Chi phí được quyết toán vào usage ledger theo từng thành viên. Worker đa provider tiếp tục polling và cache video trên server ngay cả khi desktop đã đóng.

## Công cụ media cục bộ

Desktop cần cả `ffmpeg.exe` và `ffprobe.exe` để kiểm tra clip provider, ghép và render video. Bản phát hành chuẩn phải đóng gói năm file tại `tools\ffmpeg`: hai executable, `LICENSE.txt`, `PROVENANCE.md` và `checksums.sha256`. Script publish, server nhận release, bộ cài, desktop update và updater đều từ chối package thiếu hồ sơ hoặc có SHA-256 không khớp.

Release Admin chuẩn bị bundle từ một thư mục nguồn đã được duyệt bằng lệnh:

```powershell
.\scripts\Prepare-FfmpegBundle.ps1 `
  -SourceDirectory C:\deploy\ffmpeg-source\win-x64 `
  -ExpectedVersion <version-trả-về-bởi-ffmpeg-version> `
  -Source <URL-hoặc-mã-artifact-nội-bộ> `
  -ApprovedBy <người-phê-duyệt> `
  -LicenseReview <mã-biên-bản-rà-soát-license> `
  -ApprovalScope Development
```

Script chỉ sao chép đúng `ffmpeg.exe`, `ffprobe.exe`, `LICENSE.txt`, tự tạo hồ sơ nguồn và SHA-256, rồi chạy lại cả hai executable để xác nhận cùng phiên bản `win-x64`. Có thể kiểm tra lại độc lập bằng `scripts\Test-FfmpegBundle.ps1 -BundlePath <thư-mục-bundle>`.

Bundle cục bộ hiện tại là Gyan FFmpeg `9.0.1-essentials_build-www.gyan.dev`, static `win-x64`, GPLv3 và có `libx264` theo yêu cầu render hiện tại. Hồ sơ được đánh dấu `Approval scope: Development`; vì vậy dùng được khi build/chạy dev nhưng `Publish-DesktopRelease.ps1`, Setup và Updater sẽ từ chối đưa bundle này vào release. Trước khi phát hành, Owner sản phẩm phải hoàn tất rà soát nghĩa vụ phân phối GPL và tạo lại hồ sơ với `-ApprovalScope Release`.

Trong môi trường phát triển, có thể tạo `TOOL-LOCAL\appsettings.user.json` từ `appsettings.user.example.json` để trỏ tới bộ FFmpeg đã được phê duyệt trên máy. File user chỉ ghi đè cấu hình cục bộ và không nên commit. Desktop kiểm tra `-version` trước khi submit video; nếu media tool chưa sẵn sàng thì không tạo outbound request hoặc chi phí mới. Khi request cũ đã `Completed`, sau khi sửa media tool người dùng bấm **Tiếp tục tải clip** để dùng lại request đó.

## Kiểm tra

```powershell
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

## Cập nhật desktop

Desktop gọi `/api/desktop-updates/check` sau khi đăng nhập. Package được kiểm tra kích thước và SHA-256; updater có backup và rollback khi lỗi.

```powershell
.\scripts\Publish-DesktopRelease.ps1 `
  -Version 1.0.3 `
  -BuildNumber 4 `
  -Channel Stable `
  -ServerBaseUrl https://server.example.com/ `
  -AppSettingsPath C:\deploy\videomaker.appsettings.json `
  -FfmpegBundlePath C:\deploy\ffmpeg\win-x64
```

Thư mục `-FfmpegBundlePath` bắt buộc chứa đủ `ffmpeg.exe`, `ffprobe.exe`, `LICENSE.txt`, `PROVENANCE.md` và `checksums.sha256` của cùng một bản phân phối đã được duyệt cho `Approval scope: Release`. Publish chạy lại checksum và `-version`, sau đó kiểm tra năm file đều có trong `tools/ffmpeg` và `update-manifest.json`. Repository không tự tải binary từ Internet.

Ở lần chạy đầu sau nâng cấp, desktop xóa chính xác file credential BYOK cũ `%LOCALAPPDATA%\ToolGenPostVideo\provider-secrets.bin` và file `.tmp` tương ứng. Các file cấu hình và workspace khác được giữ nguyên.
