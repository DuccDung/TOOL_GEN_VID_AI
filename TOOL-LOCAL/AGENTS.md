# Hướng dẫn AI agent — TOOL-LOCAL

> Ngữ cảnh liên module: [../BOI_CANH_HE_THONG_HIEN_HANH.md](../BOI_CANH_HE_THONG_HIEN_HANH.md). Cập nhật rà soát: 2026-09-07.

Áp dụng thêm `../AGENTS.md`. File này bao phủ WinForms, WebView bridge, React, media và Vietsub local.

## Trách nhiệm

- `Program.cs`/`Form1.cs`: composition, login/dashboard lifecycle và WebView2.
- `Authentication`: token cục bộ, refresh và license heartbeat.
- `Generation/ServerGenerationClient`: client duy nhất cho AI gateway.
- `Projects`/`Data`: workflow SQL chuyển tiếp và dashboard projection.
- `Media`: FFmpeg/FFprobe, validation, trim, speech/audio mix và render.
- `WebView` + `Web/src`: message contract, UI/state/busy/error.
- `Vietsub`: workspace, SQLite, playback, timeline, subtitle, OCR, dịch local và tạo giọng local.

## Gateway-only và phiên đăng nhập

- Không thêm provider SDK/client, key field, secret store, base URL tùy ý hoặc BYOK fallback.
- UI provider chỉ hiển thị readiness/model/policy/budget an toàn; token đăng nhập có thể được bảo vệ local nhưng provider key không bao giờ thuộc desktop.
- `401` từ API authenticated phải invalidate session và quay về login. `403` license/role không mặc định là token hỏng; `401 invalid_credentials` khi login chỉ hiển thị lỗi nhập lại.
- Mọi request generation dùng organization đang chọn và khóa organization selector khi thao tác đang chạy.
- `LegacyProviderCredentialCleaner` chỉ xóa `provider-secrets.bin` và file `.tmp` tương ứng, không mở rộng phạm vi.

## SQL chuyển tiếp

- Desktop chỉ dùng database user thuộc `VideoMakerDesktopRole` và chỉ cho workflow schema `vf`.
- Không ghi credential, provider request, reservation, usage ledger hoặc audit truth từ desktop; không cấp quyền `ai`, `auth`, `dbo` hoặc `vs`.
- Khi chuyển nghiệp vụ sang server API, bỏ đường SQL tương ứng; không giữ hai nguồn sự thật.
- Không xóa entity legacy nếu còn EF navigation, migration hoặc dữ liệu tương thích.

## WebView và React

- Message mới phải cập nhật `Web/src/types.ts`, nơi phát message, `WebMessageContracts.cs`, `DashboardBridge.cs` và test.
- Validate payload ở C#; giới hạn size, giữ request ID và error code ổn định.
- Getter state không khởi động job hoặc gọi provider. Busy state phải được giải phóng ở success/error/cancel.
- Đổi organization/project phải hủy state cũ. Không đưa token, secret, connection string hoặc absolute path vào DOM/console.
- Chỉ sửa source TypeScript; không sửa `dist`, generated Vite files hoặc `node_modules`.

## Media, speech và render

- Chạy media preflight trước provider generation khi output cần FFmpeg.
- Download qua `.part`, giới hạn size/MIME, xác minh SHA-256 và probe trước promote.
- Retry lỗi local dùng lại provider request/output đã có, không submit cloud lần hai.
- Native Audio cần playback/manual approval theo policy. Canonical Voice phải giữ đúng voice version, speech hash và lineage.
- Render chỉ dùng approved asset đúng scene/generation/voice/speech snapshot và hash trên disk.
- FFmpeg dùng argument list, timeout/cancellation và giới hạn stderr; không ghép command string từ input người dùng.

## Vietsub local

- Project/workspace phải khớp exact organization + owner + selected context; virtual media URL không lộ path.
- `LINK` phát hiện source mất/đổi; `COPY` không sửa source gốc. Subtitle mutation dùng track revision và không ghi đè cue manual/locked.
- Dịch chỉ tạo job cho active track `PADDLE_OCR_LOCAL`, language `en`/`zh`, có cue và revision khớp.
- Qwen/LLamaSharp chỉ chạy trong worker x64 riêng. Worker crash/timeout/cancel không được làm desktop chết hoặc để orphan process.
- Model/path/URL/executable không nhận từ DOM/project manifest. READY phải khớp model/worker/protocol/config/backend/native fingerprint và probe.
- Cảnh báo thiếu RAM/commit cần xác nhận rõ; xác nhận không bỏ qua platform, disk, checksum/probe hoặc lỗi worker thật.
- Piper local voice dùng component đã pin, kiểm tra RIFF/PCM/SHA-256 và playback registry theo đúng project/track/revision.
- `VietsubLocalTranslationEnabled=false` mặc định. `VietsubLocalVoiceEnabled=true` chỉ hiển thị/cài đặt workflow; thiếu runtime/model trả `NOT_INSTALLED`.
- Test model/benchmark `Skipped` không chứng minh production-ready.

## Kiểm tra

```powershell
Set-Location Web
npm ci --no-audit --no-fund
npm run build
npm test
```

Sau thay đổi C#/bridge/media chạy toàn bộ lệnh trong `../AGENTS.md`. Không chạy UI automation hoặc generation thật khi chưa có môi trường và phê duyệt chi phí.
