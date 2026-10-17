# Bản ZIP offline support 1.0.1 / 20261008

Ngày 2026-10-08; nhánh `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b` và working tree có thay đổi chưa commit. Giữ nguyên thay đổi có sẵn của người dùng. `source-inventory.json` trong output ghi hash source lúc publish; tài liệu kết quả cập nhật sau snapshot đó.

## Artifact đã tạo

Output: `D:\laptrinhweb\DesktopPackages\1.0.1-20261008`.

| Thuộc tính | Kết quả |
|---|---|
| ZIP | `VideoMaker-1.0.1-20261008-Qwen-Piper-DIAGNOSTIC.zip` |
| Dung lượng ZIP | 3.053.177.788 byte |
| SHA-256 ZIP | `5a99b57643f88a4e4d3f238fbef827686817a4bcb5febfb1ba639977479ae3f0` |
| Sau giải nén | 3.708.142.289 byte, 336 file |
| Inventory | 335 file được kiểm hash; không tự hash file inventory |
| Cấu hình chung | HTTPS `https://taphoatool.com/`, SQL để trống; không có password SQL chung |
| Trạng thái | Candidate đủ payload; `CustomerReleaseQualified=false` |

ZIP chứa desktop Windows x64 self-contained, frontend, updater, FFmpeg/FFprobe, OCR en/zh, GGUF Qwen3 4B CPU cùng worker/native dependency, payload Python/uv/Piper và voice Việt, notice/license/provenance đi kèm. Không đóng cache, token, cấu hình SQL riêng hay marker READY từ máy build. Qwen GGUF đúng 2.497.280.256 byte và SHA-256 `7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5`.

Bản hỗ trợ tắt kiểm tra cập nhật tự động. ZIP vượt limit upload mặc định 2 GiB; không upload/Active release, không tăng limit server/updater. Thư mục OneDrive được yêu cầu ban đầu có reparse attribute nên script staging từ chối; dùng output thường ở trên và giữ nguyên gói `1.0.0-20261008` cũ.

## Thay đổi và nguyên nhân đã xác minh

Gói cũ thiếu bốn DLL MSVC trong thư mục worker riêng. Preflight của đúng worker cũ nạp chúng từ System32 máy build. Đây là sự phụ thuộc môi trường đã xác minh; chưa đủ kết luận nguyên nhân duy nhất của `TRANSLATION_PROCESS_FAILED` trên máy khách.

Worker mới kèm runtime Microsoft từ input đã pin, kiểm size/hash trước native load, preload bằng đường tuyệt đối và từ chối nếu module thực không nằm trong thư mục worker. Lượt regression đầu chứng minh chỉ copy DLL vẫn nạp System32; sau sửa preload, regression và probe chính ZIP đều đạt. Fingerprint READY bao gồm bốn DLL, nên marker cũ không che thay đổi dependency.

Offline harness dùng root TEMP mới có GUID/marker sở hữu và grant chính xác cho child diagnostic. Root component thường không được nới; model/profile/protocol/hash vẫn được kiểm. Worker bình thường xóa grant diagnostic kế thừa. Regression có kiểm root hợp lệ rồi bytes GGUF giả vẫn bị từ chối, DLL thiếu/hỏng bị chặn dù máy có VC++ trong System32.

`Configure-CustomerSql.ps1` tạo `appsettings.user.json` riêng trên máy nhận, hạn chế ACL trước khi ghi secret và không ghi đè file cũ. Không mở SQL, chạy migration hoặc kiểm quyền từ script. `Check-Qwen-Customer.ps1` kiểm hash model, tài nguyên và native dependency; không load weights/dịch thật, không đọc/in secret SQL.

Với lỗi Win32 53, source có đường truy cập SQL cần kiểm cùng cấu hình và khả năng kết nối từ máy nhận. Chi tiết người dùng gửi chưa có stack đầy đủ để xác định chính xác điểm phát sinh. Worker Qwen không dùng SQL. Không nhúng cấu hình SQL host phát triển hoặc mật khẩu chung vào ZIP để giải quyết lỗi này.

## Kiểm chứng thực chạy

| Kiểm chứng | Passed | Failed | Skipped | Phạm vi |
|---|---:|---:|---:|---|
| .NET full suite | 1.600 | 0 | 13 | Release; các bài model/GPU/SQL opt-in bị skip ghi riêng |
| Regression offline/native | 36 | 0 | 0 | Harness, dependency, fingerprint, config và nhánh lỗi |
| Frontend | 280 | 0 | 0 | 44 file Vitest |

Restore và build Release đạt; build C# 0 warnings/0 errors. `npm ci`, `npm run build` và `npm test` đã chạy; frontend build có cảnh báo chunk lớn của Vite. Parser của năm script PowerShell thay đổi trả 0 lỗi. `git diff --check` đạt, có thông báo chuẩn hóa LF/CRLF.

Publish từ source mới thành công. Chính ZIP đã được giải nén vào `extracted có dấu`; inventory đạt. SHA-256 ZIP được đọc lại độc lập và khớp qualification. Kiểm publish xác nhận 62 component yêu cầu, 24 web asset, 0 thiếu/sai hash.

Probe tuần tự từ bản giải nén: WebView2 READY trên máy build; FFmpeg/FFprobe xử lý fixture; OCR nhận fixture en/zh; Piper cài private runtime từ offline payload và tạo WAV. Không dùng bài model opt-in bị skip để thay các probe này.

Native preflight của worker Qwen trong ZIP đạt CPU AVX2, native hash `1aa8f6f65386b7b977f9a0bb631d6d7e2a30d3beb3f5747e80e1b114f4885fb8`; `vcruntime140.dll`, `vcruntime140_1.dll`, `msvcp140.dll`, `vcomp140.dll` đều nạp từ `_translation_worker` của bản giải nén. Worker exit 0. Đây là kiểm native, **không phải load weights hoặc inference Qwen**.

Lượt harness Qwen thật không truyền cờ chấp thuận và dừng ở `TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED`, stage `VERIFYING`. Snapshot Windows 11 Home, i7-11800H: RAM tổng 15,77 GiB, RAM trống 3,65 GiB, commit trống 2,73 GiB. Dưới ngưỡng low-memory commit 4 GiB; chưa chạy dịch Anh/Trung. Workspace diagnostic được dọn bởi script; không tạo READY trên kho người dùng.

## Điều kiện còn lại

1. Chạy load/probe dịch Qwen thật trên máy đủ tài nguyên, hoặc sau xác nhận rõ cho phép chạy dù thiếu RAM/commit. `TOOL-LOCAL/AGENTS.md` yêu cầu: “Cảnh báo thiếu RAM/commit cần xác nhận rõ”. Chưa nhận xác nhận nên không vượt gate.
2. Provision SQL và quyền workflow `vf` trên máy nhận; kiểm kết nối và startup/UI với tài khoản/organization phù hợp. Database đích chưa được truy cập; chưa chạy migration hoặc smoke nghiệp vụ.
3. Nghiệm thu máy khách/Windows sạch: native và private Piper probe trên máy build không thay kiểm môi trường nhận, WebView2 runtime, UI gate, retry/cancel và dịch/tạo giọng fixture.
4. Hoàn tất hồ sơ/phê duyệt third-party Release. FFmpeg provenance hiện `Development`; giữ nguyên và không bypass gate Release. Chưa xác nhận phê duyệt phân phối Release.

Không kết luận bản khách production-ready và chưa rollout production.

## File bàn giao

Trong output có `checksums.sha256`, `qualification.json`, `qwen-native-preflight.json`, `qwen-offline-probe.json`, `HUONG_DAN_MAY_KHACH.txt`, `BAO_CAO_BAN_GIAO.md` và `verification` chứa hai TRX đạt cuối. Hướng dẫn mô tả giải nén, SQL riêng, setup gate và thu diagnostics an toàn. Chỉ gửi bộ file đã chọn; không gửi thư mục staging hoặc cấu hình riêng.

Kế hoạch/tiêu chí: [PLAN_TASK_ZIP_MAY_KHACH_DAY_DU.md](PLAN_TASK_ZIP_MAY_KHACH_DAY_DU.md). Báo cáo offline trước giữ nguyên trạng thái lịch sử.
