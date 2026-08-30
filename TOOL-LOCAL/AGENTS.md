# Hướng dẫn AI agent — TOOL-LOCAL

Áp dụng thêm các quy tắc trong `../AGENTS.md`. File này cũng áp dụng cho `Web` và WebView bridge.

## Vai trò của desktop

`TOOL-LOCAL` là WinForms host có giao diện React chạy bằng WebView2. Nó quản lý phiên đăng nhập, organization đang chọn, dữ liệu project/workspace và media cục bộ. Nó không phải AI gateway và không được giữ provider credential.

Luồng chính:

1. `LoginForm` đăng nhập và `LicenseSessionManager` duy trì heartbeat/lease.
2. `Form1` tạo WebView2, khởi tạo service và chuyển message giữa React với C#.
3. `ServerGenerationClient` gọi duy nhất `TOOL-SERVER` bằng access token và organization ID.
4. `ProjectGenerationService` lưu content/scene/output vào dữ liệu workflow và workspace sau khi nhận kết quả server.
5. Các service `Media` dùng FFmpeg/FFprobe để kiểm tra, ghép, render và tạo subtitle cục bộ.

FFmpeg/FFprobe của bản phát hành phải nằm trong `tools/ffmpeg` và đi cùng `LICENSE.txt`, `PROVENANCE.md`, `checksums.sha256`. Cấu hình máy phát triển chỉ được ghi đè qua `appsettings.user.json`; preflight phải hoàn tất trước outbound Kling và retry sau lỗi media phải dùng lại provider request đã có.

## Bất biến gateway-only

- Không thêm OpenAI/Kling SDK hoặc `HttpClient` gọi host provider trong desktop.
- Không thêm trường `openAiApiKey`, `klingApiKey`, form nhập key, bridge event lưu key, DPAPI provider store hoặc fallback BYOK.
- Trang “API AI tổ chức” chỉ hiển thị organization, role, budget và trạng thái provider không bí mật.
- `ProviderSettingsResponse` là view trạng thái read-only; không tạo lại `SaveProviderSettingsPayload` hay `providers.settings.save`.
- `LegacyProviderCredentialCleaner` phải chỉ xóa `%LOCALAPPDATA%\ToolGenPostVideo\provider-secrets.bin` và `.tmp` tương ứng. Không mở rộng phạm vi xóa.
- Token đăng nhập có thể dùng DPAPI; lệnh cấm chỉ áp dụng cho provider API key.
- Khi refresh token bị từ chối bằng `401`/`403`, `AccountSessionManager` phải xóa token cục bộ, phát `SessionInvalidated`, đóng dashboard và đưa người dùng về `LoginForm`. Không chỉ hiển thị lỗi rồi giữ phiên cũ.
- `401` từ API đã xác thực cũng làm mất phiên; lỗi license/quyền nghiệp vụ `403` không mặc định ép đăng nhập lại nếu session vẫn hợp lệ.
- `401 invalid_credentials` trong thao tác login không phải session expiration: giữ nguyên `LoginForm`, hiển thị lỗi tại ô mật khẩu và cho phép nhập lại.

## Organization và project

- User phải chọn organization trước khi tạo project hoặc generation.
- Project mới phải có cả `OrganizationId` và `CreatedByUserId`.
- Danh sách project được lọc theo organization hiện hành.
- Khóa organization selector khi đang chạy thao tác để request không đổi tenant giữa chừng.
- Không coi kiểm tra desktop là ranh giới bảo mật; server vẫn phải xác minh lại mọi quyền và ownership.

## SQL chuyển tiếp

Desktop hiện còn đọc/ghi trực tiếp schema workflow `vf` qua `VideoFactoryDbContext`. Đây là trạng thái chuyển tiếp:

- Chỉ dùng database user thuộc `VideoMakerDesktopRole`.
- Không thêm thao tác ghi credential, provider request hoặc usage truth từ desktop.
- Không cấp quyền schema `ai`, `auth`, `dbo` cho desktop.
- Khi chuyển một nghiệp vụ SQL sang API server, bỏ đường SQL tương ứng và thêm contract/test cùng lượt; không duy trì hai nguồn sự thật.

## WebView2 và React

- `Web/src/App.tsx`: UI/state và phát message.
- `Web/src/types.ts`: shape dữ liệu frontend.
- `Web/src/bridge.ts`: lớp giao tiếp host.
- `WebView/WebMessageContracts.cs` và `DashboardBridge.cs`: contract/handler C#.
- Mọi message mới phải có validation ở C#, error code ổn định và trạng thái busy hợp lý ở React.
- Không chèn secret, access token hoặc connection string vào DOM, console hay bundle.

Frontend dùng TypeScript strict, React và Vite. `dist`, `node_modules`, `*.tsbuildinfo`, `vite.config.js` và `vite.config.d.ts` là file sinh ra, không sửa trực tiếp.

## Kiểm tra desktop

Sau thay đổi web:

```powershell
Set-Location Web
npm ci --no-audit --no-fund
npm run build
```

Sau thay đổi C#/bridge/generation, chạy build/test toàn solution theo `../AGENTS.md`. Khi kiểm tra bundle, tìm các dấu vết bị cấm:

```powershell
rg -n -i "openAiApiKey|klingApiKey|providers\.settings\.save|DirectGenerationClient|LocalProviderRuntimeResolver|ProviderSecretStore" Web dist .
```

Không chạy UI automation hoặc generation thật nếu chưa có môi trường test và phê duyệt chi phí.
