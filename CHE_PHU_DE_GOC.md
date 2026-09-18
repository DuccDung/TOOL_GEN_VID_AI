# Che phụ đề gốc trong Vietsub

Triển khai ngày 2026-09-18 trên nhánh `main`, nền `9646a7b4eaf271f79bd3278b36a96e0932661c58`. Working tree có sẵn thay đổi tăng tốc dịch local từ công việc trước; phần này giữ nguyên chúng. Source chưa commit hoặc phát hành.

## Cách sử dụng

1. Mở dự án Vietsub có video, vào cửa sổ **Phụ đề, hình ảnh và âm thanh**.
2. Chọn tab **Che sub gốc**, bật **Che phụ đề gốc**.
3. Chọn **Làm mờ nền (giữ hình video)** để làm nhòe chữ và giữ màu nền; đây là mặc định cho vùng che mới. Nếu muốn lớp màu bán trong suốt, chọn **Lớp màu (chỉnh độ trong suốt)** rồi chỉnh thanh **Độ trong suốt**. Mức 0% là màu kín, 100% hiện nguyên hình và chữ gốc.
4. Kéo vùng che lên chữ gốc. Kéo tám điểm ở cạnh/góc để đổi kích thước; có thể dùng các thanh X/Y/rộng/cao. Phím mũi tên tinh chỉnh 0,5%, giữ Shift thành 2%; khi điểm chỉnh cạnh/góc có focus, phím mũi tên thay kích thước.
5. Dùng **Vừa khung** và tua video kiểm tra các đoạn có chữ. Lật video sẽ mang vùng che theo hình; phụ đề Việt vẫn nằm phía trên, giữ đúng chiều.
6. **Lưu thay đổi** lưu thiết lập vào dự án. **Xuất MP4** lưu thiết kế chưa lưu trước khi xuất; nếu lưu lỗi thì giữ bản nháp, báo lỗi và cho phép thử lại.

**Mặc định** tắt che cùng việc đưa chữ/mixer/lật hình về mặc định hiện có. Hủy và bỏ bản nháp không lưu. Preview ngoài cửa sổ thiết kế cũng hiển thị vùng che đã lưu.

Bản này hỗ trợ **một vùng chữ nhật cố định xuyên suốt video**. Chưa có nhiều vùng, vùng chạy theo chuyển động, khoảng thời gian riêng hay xóa chữ bằng AI. Làm mờ có thể còn nét chữ nếu mức mờ thấp hoặc vùng chọn thiếu phần chữ; lớp màu trong suốt vẫn có thể nhìn thấy chữ gốc. Nên đặt vùng che rộng hơn viền/bóng của chữ gốc.

Nếu dự án đã lưu vùng che đen từ bản đầu, mở **Che sub gốc → Kiểu che → Làm mờ nền (giữ hình video)** rồi **Lưu thay đổi**. Thiết lập đã lưu không tự bị đổi. Các vùng `SOLID` cũ thiếu trường alpha được đọc với opacity 1 để preview và export giữ cùng kết quả cho đến khi người dùng chỉnh.

## Lưu trữ và xuất video

- Cấu hình `videoTransformSettings.subtitleMask` đi trong contract thiết kế hiện hành của desktop; không có endpoint server hoặc provider mới.
- Manifest JSON nâng 7 → 8 khi mở bằng bản mới: vùng che mặc định tắt, giữ trạng thái lật hiện có. SQLite vẫn schema 6. Các dự án đã được ghi schema 8 cần binary có hỗ trợ schema 8; không sửa tay số schema để chạy bằng bản cũ.
- Tọa độ X/Y/rộng/cao chuẩn hóa theo hình nguồn sau autorotation, trước lật. Preview quy đổi sang khung hiển thị và chia đúng tỷ lệ zoom khi kéo; vùng che không được kéo ra ngoài hình nguồn.
- C# kiểm mode, màu hex, số hữu hạn, kích thước tối thiểu 2%, giới hạn khung và mức mờ 0,2–4% chiều cao. Không nhận filter, đường dẫn hoặc lệnh FFmpeg từ React.
- Thuộc tính `opacity` bổ sung trong schema 8 là alpha 0–1 của lớp màu; UI hiển thị độ trong suốt bằng `(1 - opacity) × 100%`. Mask mới mặc định BLUR và có alpha màu dự phòng 0,35 (65% trong suốt). Mask SOLID cũ thiếu alpha giữ mức 1. Không thay SQLite hoặc cần SQL migration.
- Thứ tự render: hình nguồn → vùng che → lật hình → chữ Việt. Vùng phủ màu dùng `drawbox`; làm mờ dùng một nhánh crop/Gaussian blur/overlay trong cùng lượt encode. Nhánh âm thanh/mix/duck giữ luồng hiện hành.
- Vùng che nằm trong snapshot thiết kế. Nếu cấu hình đổi giữa lúc render, file tạm không được publish. Hash nguồn, revision cue, timeline giọng, xác minh MP4 và không ghi đè nguồn giữ cơ chế hiện hành.
- Preview mờ dùng Gaussian blur của trình duyệt, export dùng `gblur` của FFmpeg. Cùng vùng chọn và mức sigma theo chiều cao; thuật toán biên/kernel có thể khác nhẹ. Export mở rộng biên ra lưới pixel chẵn tối đa dưới 2 pixel để không bỏ sót chữ. Không khẳng định hai ảnh giống tuyệt đối từng pixel.

## Bổ sung độ trong suốt — 2026-09-18, sau phản hồi giao diện

Tiếp tục trên `main`, nền `9646a7b`, cùng working tree chưa commit. Đổi mặc định vùng che mới từ SOLID sang BLUR; bổ sung thanh độ trong suốt cho lớp màu, alpha preview chỉ áp dụng cho lớp che, không làm mờ chữ Việt. FFmpeg truyền alpha màu bằng số invariant; validation từ chối NaN/ngoài 0–1. Đã kiểm tra dữ liệu SOLID cũ thiếu alpha và đổi từ lớp màu sang BLUR bỏ màu nền/alpha trong preview.

Bằng chứng mới ở `D:\VideoMakerDiagnostics\subtitle-mask-transparency-20260918`:

- Restore và Release build thành công; build **6 Warning / 0 Error**. Sáu cảnh báo `NU1900` do không truy cập được dịch vụ lấy thông tin lỗ hổng NuGet; không tắt audit hoặc đổi dependency để bỏ cảnh báo. Vite vẫn báo chunk >500 kB.
- npm ci/build đạt; frontend **259 Passed / 0 Failed / 0 Skipped**, 42 file.
- Nhóm mask/style/export C#: **46 Passed / 0 Failed / 0 Skipped**. Bổ sung kiểm alpha 0/0,35/1 bằng pixel FFmpeg thật, số thập phân với culture vi-VN, default BLUR và dữ liệu SOLID cũ. Các bài xuất MP4/mix/lật/chữ Việt tiếp tục đạt.
- Browser headless với component/CSS hiện hành: hai trường hợp lớp màu 65% trong suốt và BLUR đạt, đã xem ảnh. Bản xem trước cho thấy nền qua lớp màu, còn BLUR làm nhòe chữ và giữ màu cảnh.
- Full .NET Release, collection tuần tự: **1466 Passed / 1 Failed / 13 Skipped / 1480 Total**, 4 phút 15 giây (`full-final.trx`). Lỗi ở `VietsubTimelineLayoutIntegrationTests.WebView2_keeps_label_descenders_and_waveforms_visible_at_multiple_zoom_levels`: nhãn không đạt ngưỡng căn giữa ngang ở trường hợp zoom đầu. Bài này chạy lại riêng trên cùng binary: **1 Passed / 0 Failed / 0 Skipped** (`timeline-recheck.trx`). Không sửa Timeline, assertion hoặc bỏ bài kiểm tra; chưa kết luận nguyên nhân lỗi không ổn định này. Không gọi lượt full đầu là Passed hoặc cộng lượt kiểm tra lại thành một lượt full xanh.
- Các asset web khớp SHA-256 với wwwroot Release; `git diff --check` đạt. Binary mới: `TOOL-LOCAL.dll` SHA-256 `2E0F6D868FB3B12DDB8F4262BDB96F49A13CE14DB737DBC00C2186372BDEA3ED`, `TOOL-TESTS.dll` `8393566C52F7A2BD34012B0765B9844B05E533F420163C4B9A96D29C9D40F4C8`.

## Kiểm chứng bản đầu, trước bổ sung độ trong suốt

Bằng chứng lưu ở `D:\VideoMakerDiagnostics\subtitle-mask-20260918`.

- `dotnet restore`, solution Release build, `npm ci --no-audit --no-fund`, `npm run build`: đạt. MSBuild 0 Warning / 0 Error; Vite vẫn có cảnh báo chunk trên 500 kB.
- Frontend toàn bộ: **257 Passed / 0 Failed / 0 Skipped**, 42 file. Bao gồm bật/lưu không sửa object gốc, kéo và đổi kích thước ở zoom 150%, lật hình, làm mờ, bàn phím, reset/bỏ bản nháp, lưu lỗi/thử lại và lưu trước export. Lượt đầu có 2 lỗi do thanh trượt chưa có nhãn truy cập riêng; đã thêm `aria-label`, chạy lại file liên quan và toàn suite đạt.
- Nhóm C# mask/style/export: **40 Passed / 0 Failed / 0 Skipped**. Tám trường hợp dùng FFmpeg/FFprobe thật với video dọc/ngang, hai kiểu che và bốn trạng thái lật, kiểm pixel mẫu, chữ Việt rõ, audio và thời lượng. Các bài còn lại kiểm migration, persistence, input không hợp lệ, deep copy, invariant culture và từ chối xuất snapshot vùng che đã thay đổi.
- Browser Chromium headless dùng component/CSS hiện hành, video tổng hợp và callback lưu giả: **6 tình huống đạt / 0 lỗi**. Kích thước viewport 1440×1000, 1024×768, 720×900; zoom khung 75/100/150%; FIT/FILL; video ngang/dọc; device scale 1/1,25. Kiểm tọa độ/chiều lớp/lật/lưu/tab không tràn và đã xem screenshot. Đây là kiểm tra trình duyệt với fixture, chưa phải smoke trong desktop đăng nhập thật.
- Toàn bộ .NET Release, `--no-build -- xUnit.ParallelizeTestCollections=false`: **1461 Passed / 0 Failed / 13 Skipped / 1474 Total**, 4 phút 25 giây. TRX: `full-final.trx`; các bài model/runtime và SQL opt-in bị Skipped không được tính là đạt. Lượt này chạy các collection tuần tự, không phải bằng chứng cho chế độ song song mặc định.
- `git diff --check`: đạt. Các asset Web/dist và bản sao trong wwwroot Release khớp SHA-256.

Binary Release sau thay đổi source:

- `TOOL-LOCAL.dll`: `9C1CB5023E902B1336F75E51A74454011DF45499ADC194B0C40AFA02F7FEC098`.
- `TOOL-TESTS.dll`: `D2DE5396E2BC29D04FA577369DDD62C8BF1837E95DDE5AA58A335A0EB1661DEC`.

Chưa dùng video/project của người dùng, chưa chạy model dịch/giọng opt-in, chưa chạy migration SQL, chưa gọi provider hay phát hành production. Đóng/mở lại desktop bằng bản Release mới để nạp giao diện; thao tác đó do người dùng thực hiện, phiên làm việc này không dừng ứng dụng/IDE đang mở.
