# Chạy đồng nhất giọng Veo local

Cập nhật 2026-09-10 cho repository `TOOL_GEN_POST_VIDEO`, nhánh `local-3`.

Chức năng lấy màu giọng từ một cảnh Veo đã duyệt để chuyển giọng các cảnh khác của cùng nhân vật. Giữ lời nói sẵn có và hình ảnh; mỗi kết quả cần nghe, duyệt trước khi dựng video.

## Bản đã chuẩn bị trên máy này

- `Features:VeoLocalVoiceConsistencyEnabled=true` trong cấu hình desktop.
- Python 3.11.11, PyTorch CPU, OpenVoice V2, Silero và Demucs đã cài bằng installer có lock/hash; runtime đã probe và trả `READY`.
- Component: `D:\VideoMakerLocalVoice\v1`; temp/cache: `D:\VideoMakerLocalVoice\tmp`. Media workspace hiện có giữ nguyên.
- Lần kiểm tra ngày 2026-09-10, ổ C còn khoảng 0,23 GB trống; cần dành thêm dung lượng cho media workspace hiện có trước khi tạo nhiều clip hoặc render project lớn. Component và temp của đồng nhất giọng đã được đặt trên ổ D còn hơn 23 GB.
- `TOOL-LOCAL/appsettings.user.json` chứa cấu hình máy, được Git bỏ qua và không publish. Binary Release đọc bản sao cùng thư mục binary; build lại sau khi sửa cấu hình source.
- Database `DUNGDEV / VideoFactory` đã được kiểm tra chỉ đọc: có migration `4.1.8-local-voice-consistency`, cột và constraint đang hoạt động. Phiên triển khai này không chạy migration hoặc bật project bằng SQL.
- Desktop máy này được cấu hình cho `https://localhost:7242/`. Launcher kiểm tra cổng thuộc server Release của đúng repository; không dùng bản desktop cũ để render project đã bật local voice.

Model thật đã qua hai lượt liên tiếp trên cấu hình CPU cuối, kiểm lấy mẫu, chuyển giọng, remux và retry cache: khoảng 3 phút 48 giây và 4 phút 14 giây; peak worker tối đa khoảng 1,88 GiB. Trạng thái test và phần chưa nghiệm thu được ghi tại [task triển khai](TASK_TRIEN_KHAI_VEO_LOCAL_SU_DUNG_THUC_TE.md). READY là trạng thái component, chưa thay thế kiểm tra WebView2 và nghe thử clip Veo tiếng Việt thật.

## Mở đúng bản

Nhấp đúp **`Mo-VideoMaker.cmd`** tại thư mục repository. Launcher khởi động Account Server ở chế độ nền nếu chưa chạy, đợi HTTPS sẵn sàng, kiểm runtime READY rồi mở desktop Release. Chạy lại launcher sẽ dùng server và desktop đang mở, không tạo thêm bản trùng.

Máy này đã xác minh database Development `DUNGDEV / VideoFactory` và khởi động server tại `https://localhost:7242/`. Launcher kiểm tra schema chỉ đọc trước khi khởi động; không tự chạy migration. Server có thể bootstrap catalog và chạy worker trên database. Nếu đổi sang môi trường khác, cần xác minh cấu hình và quyền tác động trước khi mở.

Tương đương trong PowerShell tại thư mục repository:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Open-VeoLocalVoice.ps1
```

Nếu chỉ cần mở hoặc kiểm server:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Open-VeoLocalVoice.ps1 -ServerOnly
```

Launcher kiểm tra chứng chỉ HTTPS bằng cơ chế mặc định của Windows và cổng thuộc server Release của đúng repository. Phép kiểm endpoint đăng nhập gửi JSON không hợp lệ, đợi HTTP 400 từ bước kiểm dữ liệu; không gửi tài khoản hoặc mật khẩu. Nhật ký server nằm tại `artifacts/local-voice-server`. Launcher không tự dừng hay thay server khác. Đăng nhập bằng tài khoản hiện có, chọn đúng tổ chức và project.

## Chọn mẫu và chuyển giọng

1. Mở project video dài Fal/Veo dùng **Provider Native Audio** (`ProviderNativeVerified`). Panel local không áp dụng cho project Canonical Voice.
2. Ở **Storyboard & clip** hoặc **Duyệt & xuất**, tìm **Đồng nhất giọng Veo · local**. Xác nhận và chọn **Bật cho project**. Các cảnh thoại cần được duyệt kết quả local hoặc xác nhận ngoại lệ native trước khi dựng.
3. Chọn cảnh native đã nghe duyệt, chỉ một nhân vật đang nói, generation Veo 4/6/8 giây. Xác nhận quyền dùng giọng và clip chỉ có một người nói, rồi bấm **Chuẩn bị mẫu**. Mẫu cần ít nhất 1,5 giây lời nói sạch.
4. Nghe mẫu và duyệt. Chọn các cảnh của cùng nhân vật để chuyển giọng. Máy xử lý local bằng CPU; mỗi bước có thể mất vài phút.
5. Nghe A/B nguồn và kết quả: kiểm lời tiếng Việt, dấu, màu giọng, âm nền và khẩu hình. Duyệt từng kết quả đạt; kết quả chưa đạt có thể từ chối/chạy lại hoặc chọn mẫu khác.
6. Dựng và xuất MP4 theo luồng hiện có. Đổi mẫu hoặc nguồn làm kết quả cũ hết hiệu lực và cần xử lý/duyệt lại.

Bật cho project không tự chọn mẫu hoặc duyệt thay người dùng. Không cần tạo clip Veo mới nếu đã có đủ clip native hợp lệ; xử lý giọng local không submit lại provider.

## Kiểm tra hoặc cài lại runtime

```powershell
# Đọc trạng thái; không tải model, không ghi project.
powershell -NoProfile -File scripts/Prepare-VeoLocalVoice.ps1 -Mode Status

# Kiểm toàn bộ checksum và nạp model, không tải lại dependency.
powershell -NoProfile -File scripts/Prepare-VeoLocalVoice.ps1 -Mode Verify

# Cài/khôi phục component từ nguồn ghim, rồi verify/probe.
powershell -NoProfile -File scripts/Prepare-VeoLocalVoice.ps1 -Mode Prepare
```

Mã thoát `0` là READY, `2` là chưa sẵn sàng, `1` là lỗi bảo trì. Nếu đang có tác vụ dùng runtime, đợi tác vụ xong hoặc hủy bằng UI trước khi bảo trì. Không chỉnh manifest hay ghi READY thủ công.

Để dùng ổ khác, sửa `LocalVoice.ComponentRoot` và `LocalVoice.TemporaryRoot` trong cấu hình người dùng rồi build lại. Chọn đường dẫn local đầy đủ, không chọn thư mục gốc ổ đĩa hoặc junction; temp phải nằm ngoài component. Khi đổi component root sang thư mục mới, chạy Prepare ở cấu hình mới; không di chuyển venv Python đã cài vì có đường dẫn interpreter cố định.
