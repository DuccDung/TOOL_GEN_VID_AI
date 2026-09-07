# Hướng dẫn AI agent — TOOL-LOCAL

Áp dụng thêm `../AGENTS.md`. File này bao phủ WinForms, WebView bridge, React, media và Vietsub local.

## Trách nhiệm

- `Program.cs`/`Form1.cs`: composition, login/dashboard lifecycle và WebView2.
- `Authentication`: token cục bộ, refresh và license heartbeat.
- `Generation/ServerGenerationClient`: client duy nhất cho AI gateway.
- `Projects`/`Data`: workflow SQL chuyển tiếp và dashboard projection.
- `Media`: FFmpeg/FFprobe, validation, trim, audio mix và render.
- `WebView` + `Web/src`: message contract, UI/state/busy/error.
- `Vietsub`: workspace, SQLite, playback, timeline, subtitle, local job và OCR.

## Gateway-only

- Không thêm provider SDK/client, key field, secret store, base URL tùy ý hoặc BYOK fallback.
- UI provider chỉ hiển thị readiness/model/policy/budget an toàn.
- Token đăng nhập có thể được bảo vệ cục bộ; provider key thì không bao giờ thuộc desktop.
- `401` từ API authenticated phải invalidate session và quay về login. `403` license/role không mặc định là token hỏng.
- Mọi request generation phải dùng organization đang chọn và khóa organization selector trong thao tác.

## SQL chuyển tiếp

- Desktop chỉ dùng database user thuộc `VideoMakerDesktopRole`.
- Không ghi credential, provider request, reservation, usage ledger hoặc audit truth từ desktop.
- Khi chuyển nghiệp vụ sang server API, bỏ đường SQL tương ứng; không giữ hai nguồn sự thật.
- Không xóa entity legacy nếu còn EF navigation/migration/data compatibility.

## WebView và React

- Message mới phải cập nhật `Web/src/types.ts`, nơi phát message, `WebMessageContracts.cs`, `DashboardBridge.cs` và test.
- Validate payload ở C#; giới hạn size, giữ request ID và error code ổn định.
- Getter state không được khởi động job hoặc gọi provider.
- Busy state phải được giải phóng ở success/error/cancel; đổi organization/project phải hủy state cũ.
- Không đưa token, secret, connection string, user/org ID không cần thiết hoặc absolute local path vào DOM/console.
- Chỉ sửa source TypeScript; không sửa `dist`, generated Vite files hoặc `node_modules`.

## Media

- Chạy media preflight trước provider generation khi output cần FFmpeg.
- Download qua `.part`, giới hạn size/MIME, xác minh SHA-256 và probe trước promote.
- Retry lỗi local phải dùng lại provider request/output đã có, không submit cloud lần hai.
- Native Audio cần playback/manual approval theo policy.
- Render chỉ dùng approved asset đúng scene/generation/voice/speech snapshot và hash trên disk.
- FFmpeg process phải dùng argument list, timeout/cancellation và giới hạn stderr; không ghép command string từ input người dùng.

## Vietsub

- Project/workspace phải khớp exact organization + owner + selected context.
- Virtual media URL không lộ path; handler luôn kiểm tra project/media/source hash và HTTP Range.
- `LINK` phải phát hiện source mất/đổi; `COPY` không được sửa source gốc.
- Subtitle mutation dùng track revision; không ghi đè cue manual/locked.
- Job local dùng state machine/checkpoint/pause/resume/retry/cancel và global heavy-job limit.
- OCR phải xác minh session/license/membership/role trước khi chạy; Viewer bị chặn.
- Không coi job type placeholder là tính năng. Không thêm direct cloud translation hoặc key vào Vietsub.

## Kiểm tra

Frontend:

```powershell
Set-Location Web
npm ci --no-audit --no-fund
npm run build
npm test
```

Sau thay đổi C#/bridge/media chạy toàn bộ lệnh trong `../AGENTS.md`.
