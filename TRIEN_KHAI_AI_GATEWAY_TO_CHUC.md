# Triển khai AI Gateway theo tổ chức

Tài liệu này là runbook triển khai VideoMaker 4.0. Các lệnh thay đổi database phải được chạy trong cửa sổ bảo trì và sau khi đã có backup kiểm tra phục hồi được.

## 1. Chuẩn bị

- SQL Server đang chứa database `VideoFactory`.
- Tài khoản chạy migration có quyền DDL và tạo role.
- Server có HTTPS hợp lệ và outbound HTTPS đến `api.openai.com:443`, `api-singapore.klingai.com:443`; nếu rollout Seedance, mở thêm `ark.ap-southeast.bytepluses.com:443` và các host output đã duyệt trong `Generation:VideoOutputs:AllowedHostSuffixes`.
- Server và desktop dùng hai tài khoản database khác nhau.
- Có một tài khoản VideoMaker mang global role `Admin`.
- Có API key OpenAI/Kling và, nếu rollout, BytePlus do doanh nghiệp sở hữu; không gửi key qua chat, email hoặc file cấu hình desktop.

## 2. Nâng cấp database

Kiểm tra đúng instance/database, sao lưu, sau đó chạy:

```powershell
sqlcmd -S <sql-server> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.Initial.sql
sqlcmd -S <sql-server> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.0.OrganizationAiGateway.sql
sqlcmd -S <sql-server> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.1.VietnameseSeedTextRepair.sql
sqlcmd -S <sql-server> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.2.GptImageCharacterReference.sql
sqlcmd -S <sql-server> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.3.SceneVoiceTts.sql
sqlcmd -S <sql-server> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.4.BytePlusSeedance.sql
sqlcmd -S <sql-server> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.5.SceneNativeAudioStatuses.sql
sqlcmd -S <sql-server> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.6.NativeAudioWorkflowStatuses.sql
sqlcmd -S <sql-server> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.DesktopLeastPrivilege.sql
```

`-b` làm `sqlcmd` trả exit code lỗi khi migration thất bại; `-f 65001` buộc công cụ đọc file theo UTF-8 để giữ đúng tiếng Việt. Các script 4.0.x là idempotent và không tự bật Seedance hay tự nhập giá. Script 4.0.4 backfill project cũ về Kling, thêm policy/snapshot video và cache output; 4.0.5 mở rộng trạng thái của `vf.Scenes`; 4.0.6 hoàn thiện cả `vf.Scenes` và `vf.VideoGenerations` cho `PromptInvalid`, `AudioReviewRequired`, `NativeAudioInvalid` mà workflow desktop đang ghi. Phải chạy các migration trước script least privilege.

Gán đúng database user của desktop:

```sql
USE [VideoFactory];
ALTER ROLE [VideoMakerDesktopRole] ADD MEMBER [VideoMakerDesktopUser];
```

Không thêm server user vào role này. Server cần quyền đọc/ghi `auth`, `ai`, `vf`, bảng Data Protection keys và Identity.

Kiểm tra migration:

```sql
SELECT TOP (10) [Version], [AppliedAtUtc]
FROM [ai].[SchemaVersions]
ORDER BY [SchemaVersionId] DESC;

SELECT [Code], [Name], [MonthlyBudgetLimit], [CurrencyCode]
FROM [ai].[Organizations];
```

Phải thấy đủ version từ `4.0.0-organization-ai-gateway` đến `4.0.6-native-audio-workflow-statuses`. Chỉ tiếp tục rollout sau khi chạy lại migration trên database clone và xác minh lần chạy thứ hai không thay đổi dữ liệu ngoài ý muốn.

## 3. Cấu hình server

Dùng secret store của môi trường; ví dụ development:

```powershell
dotnet user-secrets set --project TOOL-SERVER "ConnectionStrings:VideoFactory" "<server-database-connection-string>"
dotnet user-secrets set --project TOOL-SERVER "Jwt:SigningKey" "<random-secret-at-least-32-bytes>"
```

Để gửi OTP quên mật khẩu qua Gmail, bật xác minh hai bước, tạo App Password mới rồi cấu hình bằng secret store:

```powershell
dotnet user-secrets set --project TOOL-SERVER "Smtp:Host" "smtp.gmail.com"
dotnet user-secrets set --project TOOL-SERVER "Smtp:Port" "587"
dotnet user-secrets set --project TOOL-SERVER "Smtp:UseStartTls" "true"
dotnet user-secrets set --project TOOL-SERVER "Smtp:User" "<gmail-address>"
dotnet user-secrets set --project TOOL-SERVER "Smtp:Pass" "<new-gmail-app-password>"
dotnet user-secrets set --project TOOL-SERVER "Smtp:TimeoutSeconds" "30"
```

Không lưu App Password trong source. Có thể chỉnh `PasswordReset:OtpLifetimeMinutes` trong khoảng 5–30 phút và `PasswordReset:MaxFailedAttempts` trong khoảng 3–10; mặc định lần lượt là 10 phút và 5 lần.

Không thêm OpenAI/Kling/BytePlus key vào `appsettings.json`. ASP.NET Core Data Protection dùng application name `VideoMaker.Server` và lưu key ring trong database. Khi scale nhiều server, tất cả instance phải dùng cùng database key ring và cùng application name.

Cache video và polling dùng cấu hình không chứa secret. Giá trị mặc định trong source là retention 48 giờ, tối đa 1 GiB/file, 20 GiB tổng; polling có lease 35 phút và dừng ở 3.000 lần hoặc 72 giờ. Chỉ đổi sau khi đã kiểm tra dung lượng đĩa, timeout mạng và chính sách lưu trữ của môi trường:

```json
{
  "Generation": {
    "VideoOutputs": {
      "StorageRoot": "data/video-outputs",
      "RetentionHours": 48,
      "MaximumFileBytes": 1073741824,
      "MaximumStorageBytes": 21474836480,
      "AllowedHostSuffixes": {
        "kling": [ "klingai.com", "kwaicdn.com", "kwimgs.com" ],
        "byteplus": [ "bytepluses.com", "volces.com" ]
      }
    },
    "VideoPolling": {
      "MaximumAttempts": 3000,
      "MaximumAgeHours": 72,
      "ClaimLeaseMinutes": 35
    }
  }
}
```

Khởi động server sau khi migration hoàn tất:

```powershell
dotnet run --project TOOL-SERVER --configuration Release
```

Server bootstrap catalog OpenAI/Kling và hai model Seedance đã duyệt trong source. Provider/model BytePlus vẫn disabled mặc định; server không tự gán đơn giá, credential hoặc policy tổ chức.

## 4. Chuẩn bị bearer token quản trị

Đăng nhập qua luồng auth sẵn có của hệ thống và lấy access token của global Admin. Các ví dụ sau giả định:

```powershell
$server = "https://server.example.com/"
$adminToken = "<admin-access-token>"
$adminHeaders = @{ Authorization = "Bearer $adminToken" }
```

Không ghi token thật vào source control hoặc lịch sử shell dùng chung.

## 5. Tạo tổ chức

```powershell
$organization = Invoke-RestMethod `
  -Method Post `
  -Uri "${server}api/organizations" `
  -Headers $adminHeaders `
  -ContentType "application/json" `
  -Body (@{
    name = "Nhóm Sản xuất Nội dung"
    code = "content-team"
    monthlyBudgetLimit = 500
    currencyCode = "USD"
  } | ConvertTo-Json)

$organizationId = $organization.organizationId
```

Người tạo trở thành Owner. `monthlyBudgetLimit = 0` sẽ khóa AI.

## 6. Thêm thành viên và hạn mức

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri "${server}api/organizations/$organizationId/members" `
  -Headers $adminHeaders `
  -ContentType "application/json" `
  -Body (@{
    email = "member@example.com"
    role = "Member"
    monthlyBudgetLimit = 50
  } | ConvertTo-Json)
```

Role hợp lệ: `Owner`, `OrganizationAdmin`, `BillingManager`, `Member`, `Viewer`.

Cập nhật thành viên:

```powershell
Invoke-RestMethod `
  -Method Put `
  -Uri "${server}api/organizations/$organizationId/members/<user-id>" `
  -Headers $adminHeaders `
  -ContentType "application/json" `
  -Body (@{
    role = "BillingManager"
    status = "Active"
    monthlyBudgetLimit = 100
  } | ConvertTo-Json)
```

## 7. Cấu hình đơn giá

Lấy catalog và model ID:

```powershell
$catalog = Invoke-RestMethod `
  -Method Get `
  -Uri "${server}api/admin/ai-pricing" `
  -Headers $adminHeaders

$models = @($catalog | ForEach-Object { $_.models })
$openAiModel = $models | Where-Object { $_.modelCode -eq "gpt-5.6-luna" }
$openAiImageModel = $models | Where-Object { $_.modelCode -eq "gpt-image-2" }
$klingModel = $models | Where-Object { $_.modelCode -eq "kling-3.0" }
$bytePlusModel = $models | Where-Object { $_.modelCode -eq "dreamina-seedance-2-5-260628" }
```

Đọc đơn giá hiện hành trực tiếp từ tài khoản/hợp đồng provider tại thời điểm triển khai. Không sao chép một giá cũ từ tài liệu dự án. Nhập giá OpenAI theo USD/1 triệu token:

```powershell
$inputPrice = [decimal](Read-Host "OpenAI input USD per 1M tokens")
$outputPrice = [decimal](Read-Host "OpenAI output USD per 1M tokens")

foreach ($rate in @(
  @{ usageType = "InputToken"; unit = "MillionTokens"; unitPrice = $inputPrice },
  @{ usageType = "OutputToken"; unit = "MillionTokens"; unitPrice = $outputPrice }
)) {
  Invoke-RestMethod `
    -Method Post `
    -Uri "${server}api/admin/ai-pricing/models/$($openAiModel.providerModelId)/rates" `
    -Headers $adminHeaders `
    -ContentType "application/json" `
    -Body ((@{
      currencyCode = "USD"
      metadataJson = '{"source":"provider-contract"}'
    } + $rate) | ConvertTo-Json)
}
```

Nhập riêng rate GPT-Image-2 theo bảng giá/hợp đồng hiện hành. Không sao chép rate của model Text và không hard-code giá trong source:

```powershell
$imageInputPrice = [decimal](Read-Host "GPT-Image-2 input USD per 1M tokens")
$imageOutputPrice = [decimal](Read-Host "GPT-Image-2 image output USD per 1M tokens")

foreach ($rate in @(
  @{ usageType = "InputToken"; unit = "MillionTokens"; unitPrice = $imageInputPrice },
  @{ usageType = "OutputToken"; unit = "MillionTokens"; unitPrice = $imageOutputPrice }
)) {
  Invoke-RestMethod `
    -Method Post `
    -Uri "${server}api/admin/ai-pricing/models/$($openAiImageModel.providerModelId)/rates" `
    -Headers $adminHeaders `
    -ContentType "application/json" `
    -Body ((@{
      currencyCode = "USD"
      metadataJson = '{"source":"provider-contract","size":"1024x1024","quality":"medium","outputFormat":"png"}'
    } + $rate) | ConvertTo-Json)
}
```

Trước smoke test ảnh, xác nhận organization OpenAI đã được phép dùng `gpt-image-2`; một số tài khoản có thể bị provider yêu cầu organization verification.

Nhập Kling theo USD/giây:

```powershell
$klingPricePerSecond = [decimal](Read-Host "Kling USD per video second")
Invoke-RestMethod `
  -Method Post `
  -Uri "${server}api/admin/ai-pricing/models/$($klingModel.providerModelId)/rates" `
  -Headers $adminHeaders `
  -ContentType "application/json" `
  -Body (@{
    usageType = "VideoSecond"
    unit = "Second"
    unitPrice = $klingPricePerSecond
    currencyCode = "USD"
    metadataJson = '{"source":"provider-contract","resolution":"720p","nativeAudio":true}'
  } | ConvertTo-Json)
```

Nếu rollout Seedance, nhập đúng rate `OutputToken` lấy từ hợp đồng/dashboard BytePlus của tài khoản và region đang dùng. Estimator dự kiến token theo công thức video 720p/24fps; settlement dùng `usage.completion_tokens` do provider trả về. Không sao chép giá tham khảo vào production:

```powershell
$bytePlusOutputPrice = [decimal](Read-Host "BytePlus Seedance output USD per 1M completion tokens")
Invoke-RestMethod `
  -Method Post `
  -Uri "${server}api/admin/ai-pricing/models/$($bytePlusModel.providerModelId)/rates" `
  -Headers $adminHeaders `
  -ContentType "application/json" `
  -Body (@{
    usageType = "OutputToken"
    unit = "MillionTokens"
    unitPrice = $bytePlusOutputPrice
    currencyCode = "USD"
    metadataJson = '{"source":"provider-contract","resolution":"720p","fps":24,"nativeAudio":true}'
  } | ConvertTo-Json)
```

Rate mới phải có ngày hiệu lực sau rate active cùng usage type. Phiên bản hiện tại không nhận ngày hiệu lực tương lai; bỏ trống trường này để dùng giờ UTC của server. Request đang chạy vẫn dùng snapshot cũ.

## 8. Lưu hoặc rotate credential

Dùng token của Owner hoặc OrganizationAdmin. Đọc key vào bộ nhớ tiến trình thay vì ghi vào file:

```powershell
$openAiKey = Read-Host "OpenAI API key"
Invoke-RestMethod `
  -Method Put `
  -Uri "${server}api/organizations/$organizationId/providers/openai/credential" `
  -Headers $adminHeaders `
  -ContentType "application/json" `
  -Body (@{ apiKey = $openAiKey; name = "OpenAI production" } | ConvertTo-Json)
$openAiKey = $null

$klingKey = Read-Host "Kling API key"
Invoke-RestMethod `
  -Method Put `
  -Uri "${server}api/organizations/$organizationId/providers/kling/credential" `
  -Headers $adminHeaders `
  -ContentType "application/json" `
  -Body (@{ apiKey = $klingKey; name = "Kling production" } | ConvertTo-Json)
$klingKey = $null

# Chỉ thực hiện khi rollout BytePlus cho tổ chức thử nghiệm.
$bytePlusKey = Read-Host "BytePlus ModelArk API key"
Invoke-RestMethod `
  -Method Put `
  -Uri "${server}api/organizations/$organizationId/providers/byteplus/credential" `
  -Headers $adminHeaders `
  -ContentType "application/json" `
  -Body (@{ apiKey = $bytePlusKey; name = "BytePlus production" } | ConvertTo-Json)
$bytePlusKey = $null
```

Server kiểm tra key trước khi rotate. Nếu provider từ chối, API trả lỗi và credential cũ vẫn Active.

Xác nhận trạng thái không lộ secret:

```powershell
Invoke-RestMethod `
  -Method Get `
  -Uri "${server}api/organizations/$organizationId/providers" `
  -Headers $adminHeaders
```

Trong Admin Console, Global Admin chỉ bật provider/model Seedance sau khi rate và credential đã được kiểm tra. Sau đó Owner/OrganizationAdmin chọn model tại **Tổ chức & AI → Policy tạo video**. Policy chỉ áp dụng khi project được snapshot lần đầu; project đang dùng Kling không tự chuyển sang BytePlus. Workflow BytePlus chỉ nhận ảnh nhân vật `SourceType=Generated`, do OpenAI trong hệ thống tạo và đã được người dùng duyệt; ảnh tải lên/người thật bị chặn trước outbound.

## 9. Cấu hình và phát hành desktop

Desktop `appsettings.json` chỉ cần URL server, connection string workflow với user ít quyền, workspace và media tools. Không thêm provider key.

Ở lần chạy đầu sau nâng cấp:

- `provider-secrets.bin` và `provider-secrets.bin.tmp` bị xóa vĩnh viễn;
- workspace, `appsettings.json` và `appsettings.user.json` được giữ;
- người dùng chọn tổ chức trên thanh đầu trang;
- dự án mới được gắn vào tổ chức đang chọn.

## 10. Smoke test

Thực hiện bằng một tài khoản Member có license và device lease hợp lệ:

1. Đăng nhập desktop và xác nhận thấy đúng tổ chức.
2. Trang “API AI tổ chức” hiển thị OpenAI Text, GPT-Image-2 và Kling sẵn sàng.
3. Tạo dự án và tạo content.
4. Kiểm tra usage tăng theo token và user.
5. Tạo ảnh AI cho một nhân vật Draft, xác nhận preview PNG 1024×1024 được lưu trong workspace nhưng nhân vật chưa tự khóa.
6. Sinh lại ảnh, xác nhận reference mới trở thành primary; sau đó khóa nhân vật và xác nhận Kling dùng primary này.
7. Gửi lại cùng idempotency key tạo ảnh, xác nhận không có outbound call hoặc chi phí thứ hai; kiểm tra API không trả URL OpenAI/Base64.
8. Tạo một clip Kling, đóng desktop, chờ worker server polling hoàn tất.
9. Mở lại desktop và tải clip qua URL server.
10. Giảm hạn mức thành viên đến sát mức đã dùng, xác nhận request tiếp theo bị chặn trước provider.
11. Đổi user sang Viewer, xác nhận request trả `organization_generation_denied`.
12. Gửi lại cùng idempotency key/payload, xác nhận không phát sinh provider request thứ hai.

Với rollout Seedance, tạo một tổ chức thử nghiệm riêng và thực hiện thêm: chọn policy Seedance, tạo clip ngắn nhất được model hỗ trợ ở 720p/Native Audio, đóng desktop khi task chạy, mở lại và tải từ `/api/generation/videos/{providerRequestId}/content`. Xác minh `ProviderRequests.ResponseJson` không chứa signed URL, `GeneratedVideoOutputs` có hash/MIME/size, audio nghe được, actual `completion_tokens` được quyết toán theo rate snapshot và cleanup xóa output sau retention. Đây là smoke test có phí, chỉ chạy khi đã được phê duyệt.

Theo dõi usage:

```powershell
Invoke-RestMethod `
  -Method Get `
  -Uri "${server}api/organizations/$organizationId/usage?take=100" `
  -Headers $adminHeaders
```

## 11. Rollback ứng dụng

Không xóa schema/bảng 4.0 khi rollback binary. Dữ liệu credential, usage và audit phải được giữ để đối soát. Khi rollback Seedance, ngừng tạo project mới, chuyển policy của tổ chức thử nghiệm về Kling cho project mới, chờ task BytePlus đang chạy về terminal rồi mới disable model/credential; project đã snapshot BytePlus không tự đổi provider. Nếu cần quay lại binary trước, chặn quyền AI và đặt budget tổ chức về `0` trước; không tái phát API key xuống desktop.

## 12. Checklist production

- [ ] Backup và thử restore database.
- [ ] Migration 4.0.0 đến 4.0.6 có trong `ai.SchemaVersions`; 4.0.4, 4.0.5 và 4.0.6 đã chạy idempotent trên database clone.
- [ ] Server/desktop dùng database user khác nhau.
- [ ] HTTPS hợp lệ; không cho HTTP public.
- [ ] JWT signing key nằm trong secret manager.
- [ ] Gmail App Password nằm trong secret manager; gửi thử OTP và xác nhận email nhận đúng mã 6 số.
- [ ] Reset mật khẩu thành công làm các phiên cũ bị từ chối ngay.
- [ ] Data Protection key ring dùng chung giữa các server instance.
- [ ] Tổ chức, role, budget và member limit đúng.
- [ ] Có đủ rate bắt buộc cho provider/model thực sự bật; Seedance dùng `OutputToken/MillionTokens` lấy từ hợp đồng hiện hành, không dùng giá tham khảo.
- [ ] Xác nhận workflow Kling Native Audio không phụ thuộc model/rate OpenAI Voice; chỉ cấu hình `gpt-4o-mini-tts` khi chủ động mở lại tính năng TTS tương lai.
- [ ] Organization OpenAI dùng được GPT-Image-2; đã hoàn tất organization verification nếu provider yêu cầu.
- [ ] Credential test thành công; API chỉ trả secret hint.
- [ ] Worker video đa provider, dọn output ảnh/video tạm, credential retirement và budget reconciliation đang chạy.
- [ ] Dung lượng cache, retention, output-host allowlist và quyền ghi thư mục `Generation:VideoOutputs:StorageRoot` đã được kiểm tra.
- [ ] Log/telemetry không chứa Authorization header, prompt nhạy cảm hoặc provider key.
- [ ] Desktop mới không còn UI/mã BYOK và đã dọn credential cũ.
- [ ] Build Release và toàn bộ test đạt trước khi publish.
