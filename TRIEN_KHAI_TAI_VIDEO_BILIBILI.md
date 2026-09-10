# Tải video Bilibili — 2026-09-11

## Sử dụng

1. Mở desktop, chọn **Tải video Bilibili** trong menu trái, ngay dưới **Đăng TikTok**.
2. Nếu máy chưa có công cụ, bấm **Chuẩn bị công cụ tải**. App tải bản yt-dlp 2026.08.19 khoảng 18 MB từ release upstream, kiểm đúng kích thước/SHA-256 rồi mới cài. Máy Development đã cài và chạy kiểm phiên bản thành công trong lượt này.
3. Dán link HTTPS video `www.bilibili.com/video/BV…`, `av…`, link kênh `space.bilibili.com/<id>` (kể cả `/video`, `/upload/video`) hoặc `b23.tv`, rồi bấm **Quét video**.
4. Xem thumbnail, tiêu đề, tên kênh, thời lượng; tìm kiếm và tick video. **Chọn tất cả** chọn toàn bộ kết quả tìm kiếm trên các trang, không chỉ 24 mục đang hiển thị.
5. Chọn chất lượng **Tốt nhất hiện có**, **Tối đa 1080p/720p/480p** và thư mục lưu. Bấm **Tải các video đã chọn**.
6. Hàng đợi tải tuần tự, hiển thị tiến độ, cho phép hủy từng mục/hủy tất cả và thử lại mục lỗi/đã hủy. Thử lại giữ ID, thư mục tạm và chất lượng của lượt đó.
7. Khi hoàn tất, bấm **Mở thư mục**. Video MP4 có thể được chọn bằng luồng nhập file hiện có của Vietsub. Chưa có nút tự tạo dự án Vietsub từ trang Bilibili.

Mặc định lưu trong thư mục Videos/Bilibili của tài khoản Windows. Đường dẫn thật được chọn và giữ ở native; React chỉ nhận tên thư mục. Hàng đợi/kết quả quét thuộc phiên đăng nhập hiện tại, không gắn organization/project. Chuyển trang trong app vẫn giữ tác vụ; đóng app hủy process. Chưa phục hồi hàng đợi sau khi khởi động lại.

## Phạm vi và giới hạn

- Video công khai không cần đăng nhập; không nhập cookie, mật khẩu, token Bilibili hay đọc phiên trình duyệt.
- Chỉ các link đã chuẩn hóa/allowlist được đưa vào extractor Bilibili. Không tải từ URL tùy ý, livestream hoặc thư viện yêu thích.
- Quét kênh đi qua các trang bằng extractor upstream; mở rộng bộ sưu tập ẩn được nhận ra và loại video trùng theo URL/part. Mỗi lượt tối đa 20.000 video/1.000 bộ sưu tập; gặp giới hạn hoặc lỗi phải hiển thị **chưa đầy đủ**. Không tự coi trang đầu là toàn bộ kênh.
- Bilibili có thể yêu cầu đăng nhập hoặc giới hạn mạng/IP. Khi gặp chặn/gián đoạn, giữ các video đã quét, báo chưa đầy đủ và cho người dùng thử lại sau. Giãn mỗi request 1,5 giây, giới hạn retry và tải tuần tự; không cố vượt bước xác minh của nền tảng.
- Chất lượng phụ thuộc định dạng công khai có hình và tiếng; lựa chọn độ phân giải là mức tối đa. Giới hạn 4 GiB/video.
- File đang tải nằm trong `.videomaker-bilibili/<job-id>/video.*` dưới thư mục đích; yt-dlp dùng `.part`. Sau tải/ghép kiểm signature MP4, size, SHA-256 và FFprobe (video/audio/duration), rồi promote cùng ổ đĩa bằng rename. Không ghi đè file người dùng; tên có hậu tố ID.
- Thành công dọn file tạm của chính job. Trong phiên, file tạm của job lỗi/hủy được giữ để thử lại. Khi kết thúc phiên, service dọn file tạm sau khi công việc thoát; đóng ứng dụng trong lúc tải, kill process hoặc mất điện vẫn có thể để lại thư mục tạm, không tự phục hồi công việc từ đó.

## Thành phần kỹ thuật

- `TOOL-LOCAL/Bilibili`: chuẩn hóa link, installer checksum, process streaming/cancellation, scanner, downloader, kiểm media, hàng đợi và bridge.
- `BilibiliWebBridge` được Form1 dispatch cùng các bridge Vietsub/TikTok; message `bilibili.*` có request ID, strict payload và snapshot revision. `WebMessageContracts.cs` và TypeScript types đi cùng nhau. Không cần public DTO server, migration hoặc package NuGet/npm mới.
- `Web/src/features/bilibili`: trang React, bộ điều khiển state và CSS dùng token của app. Menu/header được nối trong `App.tsx`.
- Bilibili là thao tác local cá nhân cần license/session hiện hành. Không gọi AI Gateway, không phát sinh reservation/chi phí AI và không gửi media/path lên server. License bị vô hiệu hóa hủy tác vụ local.
- Process chạy ẩn bằng argument list, timeout và kill process tree; loại config, plugin, proxy và biến môi trường nhạy cảm. Stderr chỉ được phân loại thành mã lỗi/thông báo cố định, không đưa signed URL hoặc path vào log/React.
- Thumbnail chỉ nhận `i0.hdslb.com`, `i1.hdslb.com`, `i2.hdslb.com` qua HTTPS; CSP mở đúng ba host ảnh đó, giữ `connect-src 'none'`.
- Runtime cố định tại `%LOCALAPPDATA%\VideoMaker\Bilibili\runtime\2026.08.19`. Hash được kiểm lại trước khi chạy; không dựa vào marker READY, không tự cập nhật upstream.
- Nguồn gốc/license/checksum: [third_party/bilibili/PROVENANCE.md](third_party/bilibili/PROVENANCE.md). Binary upstream không được commit; license/provenance được nhúng để installer chép cùng runtime.

## Kiểm chứng

- `dotnet restore TOOL_GEN_POST_VIDEO.slnx`: đạt.
- `dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore`: đạt, 0 warning/error MSBuild. Vite còn cảnh báo kích thước bundle trên 500 kB.
- `npm ci --no-audit --no-fund`, `npm run build`: đạt.
- Frontend: 204 Passed / 0 Failed / 0 Skipped, gồm 8 test Bilibili về menu, chọn nhiều/trang, dữ liệu partial, cancel, stale reply và giải phóng busy.
- .NET lượt cuối: **1.294 Passed / 0 Failed / 5 Skipped**, gồm 40 test Bilibili. Kết quả tại `.tmp/bilibili-validation/bilibili-final.trx`. Các bài model/SQL opt-in bị skip không chứng minh model hoặc database đã nghiệm thu.
- Lượt all-in-one đầu tiên dùng TEMP mặc định bị 21 Failed vì dung lượng ổ C thấp. Đã nén output build cũ `.tmp/outfit-validation/bin` bằng NTFS (giữ nguyên nội dung), chuyển TEMP/TMP kiểm thử sang `D:\vm-bilibili-testtemp` và chạy collection tuần tự bằng `.tmp/bilibili-validation/serial.runsettings`. Không dừng server/IDE, không xóa source/media/model/backup.
- WebView2 thật với dữ liệu giả, chặn mạng ngoài: 1440×900, 1024×900 và 1440×900 zoom 125%; trang/menu/danh sách hiển thị, không tràn ngang. Ảnh tại `.tmp/bilibili-validation/ui`.
- Smoke công cụ thật: installer đúng SHA-256/size và `--version` trả 2026.08.19. Scan video mẫu public trong bộ fixture upstream trả một video; tải 480p, ghép và kiểm MP4 thành công, kích thước 68.265.077 byte; SHA-256 `80D84B75E79D6A1982427F878AEA425BFE6A927AC8E0F463BDC1A9077083E18C`.
- Smoke danh sách kênh upstream, giới hạn riêng ở hai mục trong harness: trả hai mục và exit 0. Các lượt tiếp theo quét toàn kênh bị nền tảng hạn chế truy cập, kể cả sau giãn nhịp; **chưa nghiệm thu quét hoàn tất toàn kênh trên mạng hiện tại**. Unit test kiểm danh sách nhiều trang/collection, giữ partial và không báo complete khi lỗi. Harness smoke không thay đổi giới hạn quét của sản phẩm.

Không thay đổi database, credential, pricing hoặc gọi AI có phí. Binary Release và frontend cùng bản đã được build tại đường dẫn chuẩn; mở lại desktop để dùng menu mới. Không publish bộ cài/release.
