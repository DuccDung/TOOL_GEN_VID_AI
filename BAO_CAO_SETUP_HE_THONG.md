# Tích hợp Setup hệ thống vào local-2 — cập nhật 2026-09-15

## Phạm vi đã tích hợp

- Thêm **Cài đặt → Setup hệ thống** vào ứng dụng đầy đủ của `local-2`. Các trang và đường gọi video AI, Dịch Cloud, quản trị, dự án video dài cùng Vietsub hiện có vẫn được giữ.
- Kiểm tra, cài đặt, sửa và thử lại bốn thành phần OCR, FFmpeg, Qwen và Piper mà không yêu cầu mở project Vietsub.
- Giữ kiểm tra session, license, tổ chức và vai trò trước thao tác cài đặt. Viewer chỉ xem trạng thái.
- Cho phép hủy, tiếp tục sau khi đổi trang, phục hồi trạng thái qua journal và chặn callback cũ sau khi đổi tổ chức.
- Dùng chung coordinator và khóa runtime cho màn hình Setup, nút cài Qwen/Piper cũ, job Vietsub, xuất MP4 và updater.
- Pin Python/Piper dependency, kiểm checksum và marker theo máy/build. Model hợp lệ có thể được dùng lại.
- Kiểm WebView2 prerequisite trước khi mở giao diện và thêm gate kiểm thành phần/asset khi đóng gói desktop.
- Sau đăng nhập, license và tổ chức hợp lệ, tạo `Form1`, tải WebView và hiển thị màn hình dự án trước. Khi React đã nhận dashboard cùng tổ chức hiện hành, Setup mới tự kiểm tra các thành phần bắt buộc.
- Nếu còn thiếu, hiển thị modal React/WebView trên nền màn hình dự án với hai lựa chọn: **OK** để cài/sửa và **Hủy và thoát**. Modal không đóng bằng `Esc` hoặc bấm ra ngoài; sidebar và nội dung chính được đặt `inert` trong lúc gate còn khóa.
- Host C# giữ gate độc lập với overlay: trước khi tất cả thành phần bắt buộc `READY`, command nghiệp vụ bị từ chối với `system_setup_required`; chỉ các lệnh tải dashboard, license, đăng xuất, Setup và package repair được phép đi qua. Cờ `startupRequired` do host xác định theo role và gửi trong snapshot, React không tự đoán quyền.
- Qwen/Piper được cài trực tiếp qua coordinator; OCR/FFmpeg dùng package sửa chữa đã được server phân phối và updater tự khởi động lại ứng dụng. Kiểm tra thiếu WebView2 vẫn là prerequisite native vì WebView chưa thể render React ở nhánh này.

Không đưa cấu hình `Application:VietsubLocalOnly`, bộ lọc local-only, rút gọn điều hướng hoặc cơ chế khóa video AI/Dịch Cloud từ `sub-local` sang `local-2`.

## Tối ưu Vietsub/video dài đi cùng thay đổi

- Tách truy vấn và mutation subtitle store, tránh tải toàn bộ track cho các thao tác chỉ cần một cửa sổ cue.
- Ghép giọng theo segment 120 giây, kiểm WAV theo stream và giới hạn kích thước file trung gian.
- Chạy thumbnail, waveform và playback verification ở nền; chặn dữ liệu cũ quay lại UI sau khi đổi project hoặc tổ chức.
- Báo tiến độ xuất MP4 và giữ khóa runtime trong suốt job/export.
- Sửa vòng đời WebView2 deferral để không hoàn tất hai lần khi phục vụ media.

Chi tiết giới hạn và phép đo gốc nằm trong [báo cáo video dài](BAO_CAO_TOI_UU_VIETSUB_VIDEO_2_GIO.md).

## Kết quả kiểm tra trên local-2

| Phạm vi | Kết quả |
|---|---|
| `dotnet restore TOOL_GEN_POST_VIDEO.slnx` | Passed |
| `dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore` | Passed, 0 warning / 0 error |
| Full .NET Release, tắt song song collection | **1.081 Passed / 0 Failed / 4 Skipped / 1.085 Total** |
| `npm ci --no-audit --no-fund` | Passed |
| Frontend production build | Passed; Vite còn cảnh báo chunk lớn hơn 500 kB |
| Frontend test | **152 Passed / 0 Failed / 0 Skipped**, 29 file |
| Test frontend Setup panel/modal/App gate | **11 Passed / 0 Failed / 0 Skipped**, 3 file |
| Test .NET riêng namespace SystemSetup | **41 Passed / 0 Failed / 1 Skipped / 42 Total** |
| Test workflow/gate Setup lúc khởi động | **6 Passed / 0 Failed / 0 Skipped** |
| `git diff --check` | Passed |

Bốn bài .NET bị skip cần opt-in và thành phần thật: hai bài Qwen, một bài Piper hiện có và một bài cài/probe Piper của Setup. Không tính các bài skip là model đã đạt trên bản tích hợp này.

Lượt full .NET đầu phát hiện đường cài Qwen cũ có thể trả mã lỗi chung khi tranh khóa runtime trước bước xác nhận tài nguyên. Đã chuyển bước xác nhận lên trước khóa, ánh xạ lỗi khóa Setup về mã lỗi Vietsub tương ứng và chạy lại test đích cùng full suite thành công.

Khi chạy full suite theo chế độ song song mặc định trong lần tích hợp ban đầu, hai lượt chẩn đoán lần lượt gặp sai lệch căn giữa WebView2 một pixel và tranh khóa `RuntimeUseGate` giữa các collection. Các test lỗi đều Passed khi chạy cô lập. Lượt nghiệm thu modal WebView ngày 2026-09-15 chạy theo quy ước hiện có của repository với `xUnit.ParallelizeTestCollections=false` đạt toàn bộ 1.085 bài, không sửa assertion hoặc giảm phạm vi kiểm tra.

## Chưa thực hiện

- Chưa chạy model Qwen/Piper thật, pipeline OCR → Qwen → Piper → MP4 trên video người dùng hoặc video 1080p dài hai giờ.
- Chưa chạy package sửa chữa thật hoặc xác minh luồng khởi động trên máy Windows sạch.
- Chưa smoke trực tiếp modal mới bằng tài khoản/organization thật trong desktop hoặc chụp ảnh WebView2 ở các mức DPI/zoom; kiểm tra giao diện hiện tại dùng React/App với bridge giả.
- Chưa nghiệm thu trên Windows sạch, máy RAM thấp hoặc hồ sơ phát hành.
- Chưa publish, chạy migration, gọi provider có phí hay thay dữ liệu project thật.

Hướng dẫn sử dụng: [Cài VideoMaker trên nhiều máy](HUONG_DAN_SETUP_HE_THONG_NHIEU_MAY.md).
