# Giao diện và thư viện video ngắn

Triển khai trên nhánh `vid-short`, ngày 2026-09-11, theo `KE_HOACH_UI_THU_VIEN_NHAN_VAT_TRANG_PHUC.md`.

## Giao diện đã triển khai

- Trang Video ngắn mở composer hai cột khi chế độ nhân vật/trang phục được bật. Dự án TextOnly vẫn mở luồng mô tả hiện có; người dùng có thể chuyển sang bản nháp phối đồ mới.
- Cột trái có nội dung, ba tỷ lệ, hàng Nhân vật, hàng Trang phục, thời lượng 5–15 giây, âm thanh, nội dung riêng và hành động tiếp theo. Không còn hai ô ảnh nguồn lớn chiếm cả trang.
- Mỗi hàng có ô tải ảnh, nút thêm, thumbnail, tên, dấu tích lựa chọn, nút quản lý và mở toàn bộ thư viện. Hàng chỉ cuộn ngang khi thiếu chỗ. Ảnh trang phục dùng `contain`; ảnh nhân vật crop hiển thị, không crop ảnh gửi tạo video.
- Cửa sổ thêm ảnh có chọn file native, preview, tên, Lưu vào thư viện và Lưu và chọn. Hủy hộp chọn file giữ nguyên preview cũ. Quản lý hỗ trợ tìm tên, sắp xếp, lưới 24 mục mỗi lần, đổi tên, thay ảnh và xác nhận xóa.
- Cột phải có tab Ảnh mặc thử / Video, trạng thái, duyệt ảnh, duyệt video sau khi xem hết và chuẩn bị/xuất MP4. Đầu vào thay đổi khóa kết quả cũ cho đến khi lưu/tạo/duyệt lại.
- Gợi ý nội dung là mẫu trên máy, có xem trước và thao tác chèn rõ ràng.

## Lưu thư viện và bản nháp

`ShortVideoAssetLibraryService` dùng SQLite riêng và ảnh trong `%LOCALAPPDATA%\VideoMaker\ShortVideoLibrary`. Mỗi tài khoản + tổ chức có một thư mục scope theo hash; không đưa tên file nguồn hoặc đường dẫn tuyệt đối vào WebView.

Ảnh PNG/JPEG tối đa 10 MiB, 16 megapixel được kiểm signature, decode, orientation và chuẩn hóa PNG. Thumbnail tối đa 256 px theo cạnh dài nhất. Ảnh đầy đủ và thumbnail lưu theo SHA-256; kiểm lại file `.part` trước promote. Metadata commit sau khi ảnh hợp lệ; một import trùng loại/hash dùng lại mục hiện có.

Thay ảnh tạo version mới. Xóa là ẩn khỏi thư viện; phiên bản đã dùng vẫn được giữ. Đổi tên không đổi version/hash và không ghi lại approval server. Khi áp dụng cho project, native kiểm scope/loại/version/hash và sao chép ảnh đầy đủ vào workspace của project. Ảnh cũ trong workspace có thể thêm vào thư viện bằng thẻ “Ảnh đang dùng”.

Bản nháp được lưu sau khi ngừng nhập khoảng 650 ms và flush trước hành động tiếp theo. Mỗi lần lưu kiểm revision. Khi tạo dự án, native ghi ID dự án dự kiến vào draft trước, sau đó tạo/gắn ảnh/lưu settings. Retry dùng cùng ID và chỉ chấp nhận cùng chủ sở hữu/tổ chức/nội dung/thiết lập. Lưu dự án mới thành công mới lấy báo giá ảnh; không tự submit provider. Việc mở trang hay tải lại không ghi settings lên server.

Nội dung chung tối đa 2.000 ký tự. Do API hiện hành giới hạn bối cảnh 1.500 ký tự, nội dung chung dài hơn phải có bối cảnh riêng; không cắt ngầm nội dung. Bối cảnh/chuyển động riêng của project cũ được giữ nguyên và hiển thị khi mở.

Tỷ lệ, thời lượng, âm thanh của project đã lưu được khóa theo snapshot. Có thể tạo bản sao từ hai ảnh đã lưu thư viện để chọn thiết lập mới. Sao chép có xác nhận thay thế bản nháp mới đang có.

## Ranh giới và vận hành

`ShortVideoLibraryContracts` chứa metadata, ref/version, draft và thông báo dự án vừa tạo. `short-library.*` đi qua bridge kiểm session/license/user/org/project. URL ảnh `https://short-library.app.local/...` được xử lý bằng resolver native kiểm scope và hash; không map toàn bộ thư mục. Preview trả `no-store`, không trả Base64 hoặc đường dẫn nguồn. Đổi scope dọn ảnh pick tạm; response muộn không được gắn sang màn hình khác.

Thư viện chỉ ở máy hiện tại; không đồng bộ hoặc chia sẻ nhiều tài khoản/máy. Không thêm SQL Server migration, provider credential, giá mặc định hoặc request AI có phí trong đợt giao diện này. Hai feature flag của MVP trước vẫn được tôn trọng.

Để backup thư viện: đóng các cửa sổ VideoMaker đang dùng kho, sao chép **toàn bộ** thư mục `ShortVideoLibrary` gồm SQLite và `images`, giữ cấu trúc các scope. Restore khi ứng dụng đóng. Muốn khôi phục project/output phải backup cả workspace riêng; thư viện không thay thế workspace. Phiên bản đã xóa vẫn chiếm dung lượng để phục vụ dự án cũ; chưa có chức năng dọn vật lý/export/import thư viện tự động.

## Kiểm chứng

- `ShortVideoLibraryTests`: lưu bền vững, tách scope/role, hash, thumbnail khác full-resolution, chống trùng, đổi tên/phiên bản, soft delete, draft revision và phục hồi cùng project ID.
- `ShortVideoWorkflowTests`: tạo lại cùng ID không thêm project/cảnh/prompt; từ chối ID cũ khi khác tài khoản, tổ chức, nội dung hoặc âm thanh.
- `OutfitShortVideo.test.tsx` và `ShortVideoEntry.test.tsx`: entry trước project, phục hồi draft, chọn ảnh độc lập, native upload/cancel, khóa busy, báo giá/xác nhận, duyệt ảnh, stale input và response sai project.
- `ShortVideoComposerBrowserTests`: WebView2 thực, profile tạm, fixture SVG tự tạo chỉ dùng trong test, chặn mạng ngoài. Kiểm cửa sổ 1920×1080, 1440×900, 1366×768, 760×900 ở zoom 100/125/150%, hai cột/stack, thumbnail và overflow; có screenshot popup.
- Ảnh chụp và log ở `artifacts/short-video-ui`. Đây là giao diện thật render với fixture; không phải ảnh/video đã tạo qua AI.

Kết quả cuối:

| Kiểm tra | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Frontend, 33 file test, giới hạn 2 worker | 189 | 0 | 0 |
| .NET ngoài namespace Vietsub | 935 | 0 | 2 |
| .NET namespace Vietsub | 293 | 0 | 3 |
| Tổng .NET, hai nhóm bổ sung nhau | 1228 | 0 | 5 |

Restore chuẩn và restore/build Release solution vào `artifacts/short-video-ui/build` đạt. Desktop Debug và Release trong `TOOL-LOCAL/bin` đều build **0 warning / 0 error**. `npm ci --no-audit --no-fund` và production build đạt; Vite còn cảnh báo kích thước chunk trên 500 kB của ứng dụng. `binary-verification.json` xác nhận mọi file frontend dist khớp `wwwroot` của cả hai bản và feature flag bật. Cửa sổ Release cũ được đóng nhẹ khi đang ở màn hình Đăng nhập để cập nhật binary.

Các lượt chưa đạt được giữ trong log: ổ C còn ~90 MB khiến kiểm media fail; test host chạy gộp bị abort (có lượt stack overflow); frontend mặc định quá nhiều worker bị thiếu bộ nhớ; một lượt browser chạm thời điểm `npm ci` đã được chạy lại. Không sửa yếu các test để bỏ lỗi. Hai lượt .NET nhóm cuối dùng TEMP/TMP tại `artifacts/short-video-ui/temp` trên D và `serial.runsettings`; frontend dùng `--maxWorkers=2`. Không cộng test bị skip hoặc lượt abort vào số Passed.

Log cuối: `dotnet-tests-main-final.log`, `dotnet-tests-vietsub.log`, `frontend-tests.log`, `build-release.log`, `desktop-debug.log`, `desktop-release.log`. Screenshot: `screenshots/composer-1440-100.png`, `screenshots/composer-add-outfit.png` và các mức DPI khác.

Đã mở lại desktop Release mới; `launch-verification.json` lưu xác minh tiến trình. HTTPS `https://localhost:7242/api/auth/me` trả 401 khi không gửi token, đúng với endpoint yêu cầu đăng nhập; không gửi thông tin đăng nhập, token hoặc gọi provider trong bước kiểm này. Ổ C gần đầy ở thời điểm bàn giao; kho thư viện nằm trong AppData trên C nên cần dung lượng trống để thêm nhiều ảnh.

Nghiệm thu chất lượng nhân vật mặc trang phục và video từ provider thật vẫn cần ảnh được phép dùng và trần phí do người dùng chỉ định. Chưa chạy generation có phí hoặc smoke nhập ảnh người dùng trong phiên đăng nhập thật. Thư viện 200 ảnh, backup/restore thao tác tay và độ bền trong tình huống máy mất điện chưa có nghiệm thu thực tế; cơ chế lưu/version/commit được kiểm bằng test tự động.
