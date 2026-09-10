# Kế hoạch giao diện và thư viện nhân vật, trang phục cho video ngắn

Ngày lập: 2026-09-10. Nhánh khảo sát: `vid-short`.

Trạng thái cập nhật 2026-09-11: **đã triển khai source UI01–UI13, build/kiểm thử và tài liệu UI14–UI15**. Xem [biên bản kết quả và các giới hạn nghiệm thu](TRIEN_KHAI_UI_THU_VIEN_VIDEO_NGAN.md). Nghiệm thu bằng ảnh người dùng/provider thật và ý kiến người dùng về giao diện là bước tiếp theo. Yêu cầu: bố cục theo ảnh mẫu #2; hàng nhân vật theo ảnh #3; hàng trang phục theo ảnh #4.

**1. Kết quả cần đạt**

Người dùng mở trang Video ngắn, nhập nội dung, chọn một nhân vật và một bộ trang phục từ hai hàng ảnh thu nhỏ, tạo/duyệt ảnh mặc thử rồi tạo video. Ảnh đã thêm được lưu vào thư viện để dùng cho các dự án sau, còn lựa chọn của từng dự án được giữ riêng.

Các quyết định dùng để lập kế hoạch:

- Một màn hình làm việc hai cột: thiết lập bên trái, ảnh/video và duyệt kết quả bên phải.
- Nhân vật và trang phục là hai loại tài sản riêng; mỗi loại chọn một ảnh cho một video.
- Có thể xem thư viện, nhập ảnh và chuẩn bị bản nháp ngay khi vào trang Video ngắn, trước khi lưu dự án. Không phải đi qua trang ba khối lớn để tìm chỗ tải ảnh.
- Bản đầu lưu thư viện trên máy, phân tách theo tài khoản và tổ chức đang chọn. Dùng lại giữa các dự án trong cùng phạm vi đó. Đồng bộ nhiều máy hoặc chia sẻ cho cả nhóm là phần mở rộng riêng.
- Giữ các bước báo giá, xác nhận chi phí, duyệt ảnh và duyệt video của nghiệp vụ hiện tại. Chọn/lưu ảnh, đặt tên và dùng lại tài sản không gọi AI.
- Ảnh người dùng tải lên tạo thành thư viện thực tế. Hình minh họa trong ảnh mẫu chỉ là tham chiếu bố cục; không tạo các nhân vật/trang phục giả rồi trình bày như dữ liệu người dùng đã lưu.

**2. Hiện trạng đã đối chiếu source**

| Khu vực | Hiện tại | Phần cần bổ sung |
|---|---|---|
| Trang Video ngắn | `App.tsx` tách form mô tả và trang `OutfitShortVideo`; có nút mở popup tạo dự án phối đồ | Một bố cục chung, hai hàng ảnh xuất hiện ngay trong chế độ phối đồ |
| Ảnh đầu vào | `ShortVideoWorkflowService.ImportAsync` yêu cầu project, chuẩn hóa PNG, lưu trong `projects/{projectId}/short-outfit` theo hash | Thư viện độc lập project, tên, thumbnail, tìm kiếm, chọn lại và phiên bản |
| Hiển thị | Hai ô ảnh cao 260 px, ba khối công việc xếp dọc | Hàng thumbnail nhỏ và cột kết quả cố định trong tầm nhìn |
| Bridge | `outfit.*` kiểm project/organization hiện hành | Message thư viện và draft có scope tài khoản/tổ chức; thao tác gắn ảnh vẫn kiểm project |
| Dữ liệu server | `vf.ShortVideoOutfits`, `vf.ShortVideoOperations` giữ thiết lập, revision, quote và approval | Tiếp tục dùng cho generation; thư viện cục bộ không đòi thêm bảng SQL Server trong phạm vi đề xuất |
| Thay đầu vào | Đổi ảnh, bối cảnh hoặc chuyển động làm tăng revision và mất approval hiện hành | UI giải thích rõ, không tự gửi AI lại; thay tên thư viện không làm mất approval |

Các file cần đọc khi triển khai: `App.tsx`, `NewProjectDialog.tsx`, `OutfitShortVideo.tsx`, `outfitShortVideo.css`, `ShortVideoWorkflowService.cs`, `DashboardBridge.ShortVideo.cs`, `ProjectWorkspaceService.cs`, `ShortVideoOutfitContracts.cs` và các test tương ứng. Không sử dụng tài liệu kế hoạch cũ để suy ra API đã được nghiệm thu.

**3. Đặc tả bố cục tổng thể**

Phác bố cục ở kích thước desktop; số đo dưới đây là CSS pixel và cần kiểm lại trong WebView2 với DPI Windows.

```text
VIDEO NGẮN   [Tên dự án / Bản nháp]     Đã lưu · [Thư viện]

┌─────────────────────────────────────┐  ┌────────────────────────┐
│ 1  Nội dung và hình ảnh              │  │ 2  Kết quả              │
│                           Gợi ý     │  │ [Ảnh mặc thử] [Video]   │
│ [Nội dung cảnh....................]  │  │                        │
│ [9:16]       [16:9]       [1:1]      │  │   Preview đúng tỷ lệ   │
│                                     │  │                        │
│ Nhân vật          [+ Thêm nhân vật] │  │                        │
│ [Tải ảnh] [Ảnh ✓] [Ảnh] [Ảnh] [>]   │  │                        │
│                                     │  │                        │
│ Trang phục        [+ Thêm trang phục]│ │                        │
│ [Tải ảnh] [Đồ ✓]  [Đồ]  [Đồ]  [>]   │  │                        │
│                                     │  │ Trạng thái / tiến trình│
│ Thời lượng         ───────●  15 giây │  │ Duyệt / Tạo lại        │
│ [Âm thanh] [Chi phí và điều kiện]    │  │                        │
│ Bối cảnh/chuyển động riêng ▾         │  │ [Duyệt video]          │
│                                     │  │ [Xuất MP4]             │
│ Ước tính        [Hành động tiếp theo]│  │                        │
└─────────────────────────────────────┘  └────────────────────────┘
```

| Thành phần | Đặc tả đề xuất |
|---|---|
| Khung nội dung | Rộng tối đa 1440 px, căn giữa vùng nội dung sau sidebar; padding 16–24 px |
| Hai cột | Khoảng 62% / 38%, khoảng cách 16–20 px; cột phải tối thiểu khoảng 320 px khi đủ chỗ |
| Card | Nền trắng, viền xanh xám nhạt 1 px, bo góc 12 px, padding 16–20 px; bóng rất nhẹ |
| Tiêu đề trang | 20–24 px; tên dự án phụ 13–14 px; giảm phần đầu trang đang chiếm nhiều chiều cao |
| Tiêu đề nhóm | 14–16 px, semibold; nội dung/nhãn chính 13–14 px; trợ giúp 12 px |
| Nút | Cao 36–40 px; hành động chính cao 42–44 px; nút phụ viền mảnh, icon 16–18 px |
| Màu | Tái sử dụng nền, xanh chính và màu chữ hiện có; nhấn mạnh bằng trạng thái chọn và nút chính |
| Preview | Giữ tỷ lệ 9:16, 16:9 hoặc 1:1 bằng khung tỷ lệ; `contain` cho ảnh/video kết quả; không cắt mất đầu/chân để lấp khung |
| Cuộn | Cột phải sticky khi chiều cao cho phép; không tạo nhiều vùng cuộn dọc lồng nhau |
| Cửa sổ hẹp | Khi vùng nội dung không đủ khoảng 960 px, xếp cột kết quả xuống dưới; card và nút co giãn, không tràn ngang trang |
| DPI | Kiểm tại 100%, 125%, 150%; không thu nhỏ font chỉ để ép tất cả vào một màn hình |

Nghiệm thu bố cục tại 1920×1080, 1440×900, 1366×768 và cửa sổ thu nhỏ. Mốc kích thước là mục tiêu thiết kế, không phải bằng chứng đã kiểm.

**4. Nội dung cảnh và thiết lập**

- Textarea chính cao khoảng 88–104 px lúc đầu, được kéo giãn; nhãn “Nội dung cảnh”, placeholder ngắn, bộ đếm đúng 0–2.000.
- Dòng trợ giúp hướng người dùng mô tả bối cảnh và chuyển động. Không đặt thông tin API, đường dẫn workspace hoặc cấu trúc database trong form.
- Khi dùng nội dung chung, native ánh xạ nội dung đó vào hai trường Background và Motion hiện có. Phần “Bối cảnh/chuyển động riêng” cho phép ghi đè từng phần; phải có preview nội dung sẽ được lưu và test để không ghi đè lựa chọn riêng.
- Project cũ có Background/Motion khác nhau được nạp nguyên trạng và mở phần thiết lập riêng để người dùng nhìn thấy. Không tự gộp rồi mất dữ liệu.
- Ba nút tỷ lệ đồng kích thước, icon khung hình, nhãn 9:16 / 16:9 / 1:1; trạng thái chọn có viền và nền xanh nhạt.
- Thanh thời lượng 5–15 giây, nhãn số hiện tại, nút cộng/trừ và điều khiển bằng bàn phím. Tôn trọng giới hạn của project/model.
- Bản nháp được đổi tỷ lệ/thời lượng/audio. Project đã lưu giữ snapshot hiện hành; muốn đổi thiết lập cố định thì tạo bản sao dự án với lựa chọn mới, không âm thầm sửa project cũ.
- Switch âm thanh điều khiển giữ âm thanh hoặc xuất im lặng theo nghiệp vụ hiện có. Không thêm thoại/TTS vào phạm vi này.
- “Gợi ý nội dung” dùng mẫu soạn sẵn trên máy: studio, phố cổ, đi chậm, xoay nhẹ. Xem trước trước khi chèn/thay nội dung; không gọi AI chỉ vì bấm nút gợi ý.
- Không sao chép các dòng trong ảnh mẫu nếu gây hiểu sai, như bảo đảm giữ nguyên khuôn mặt tuyệt đối hoặc switch kiểm tra nội dung chưa có chức năng thật. Các kiểm tra quyền/chi phí của hệ thống vẫn bắt buộc.

**5. Hàng nhân vật — theo ảnh #3**

- Header: icon người bên trái, tiêu đề “Nhân vật”, mô tả “Chọn một nhân vật từ thư viện của bạn”; bên phải nút “+ Thêm nhân vật”.
- Ô đầu tiên luôn là “Tải ảnh lên”, viền nét đứt, icon tải ảnh, có trạng thái hover/focus.
- Tiếp theo là các ảnh đã lưu, ưu tiên mục đang chọn rồi danh sách theo lần dùng gần nhất. Giữ thứ tự ổn định trong lúc đang tương tác để thẻ không nhảy dưới con trỏ.
- Thẻ rộng khoảng 96–108 px, ảnh cao 76–88 px, bo góc 8 px; tên nằm dưới, tối đa hai dòng. Tên dài có tooltip, không làm thay đổi chiều rộng hàng.
- Thumbnail nhân vật dùng crop hiển thị để dễ nhận diện; ảnh gốc đã chuẩn hóa được giữ đầy đủ và có thể mở xem lớn. Điều chỉnh crop thumbnail không làm thay đổi ảnh gửi AI.
- Chọn thẻ: viền xanh 2 px, dấu tích ở góc phải trên, tên đậm; chỉ một thẻ được chọn. Phải nhận biết được lựa chọn cả khi không phân biệt màu.
- Có mũi tên sang trái/phải khi tràn; có thể cuộn ngang, dùng bàn phím. Ô tải ảnh giữ ở đầu; không để cả trang bị tràn ngang.
- “Xem tất cả (N)” mở cửa sổ thư viện khi nhiều ảnh. Tìm kiếm/quản lý đầy đủ nằm trong cửa sổ này để hàng ảnh vẫn gọn.
- Nút ba chấm trên thẻ: Xem ảnh, Đổi tên, Thay ảnh, Xóa khỏi thư viện. Có cách mở bằng bàn phím và trên màn hình cảm ứng, không chỉ hiện khi hover.

**6. Hàng trang phục — theo ảnh #4**

- Cấu trúc đồng nhất hàng nhân vật, icon áo, tiêu đề “Trang phục”, trợ giúp “Chọn một bộ trang phục cho nhân vật”.
- Nút “+ Thêm trang phục”, ô “Tải ảnh lên” và lựa chọn duy nhất.
- Thumbnail dùng nền trung tính và `contain` để thấy toàn bộ bộ đồ; không cắt chân váy, tay áo hoặc phụ kiện vì ép ảnh phủ kín.
- Tên do người dùng nhập: “Áo dài xanh”, “Vest đen”…; không tự dùng AI phân loại/đặt tên.
- Có thể lưu nhiều loại ảnh trang phục nhưng không tự nhận diện hoặc bóc tách quần áo khỏi người trong ảnh khi nhập thư viện. Chất lượng ảnh mặc thử vẫn cần người dùng duyệt.
- Chọn một nhân vật không làm mất trang phục đang chọn và ngược lại. Thay cả hai là một bản nháp trước khi người dùng lưu/xác nhận thay đổi.

**7. Popup thêm, sửa và quản lý thư viện**

Popup “Thêm nhân vật” / “Thêm trang phục”, rộng khoảng 560–640 px:

1. Tiêu đề và nút đóng; giải thích ảnh sẽ được lưu để dùng lại.
2. Vùng chọn ảnh rõ ràng, mở hộp chọn file native. Bản đầu ưu tiên nút chọn file; kéo thả chỉ bổ sung khi bridge có đường nhận file được kiểm tra, không nhận path tùy ý từ DOM.
3. Preview lớn và thông tin định dạng/kích thước an toàn; có “Chọn ảnh khác”.
4. Ô tên bắt buộc, 1–80 ký tự; gợi ý từ tên file nhưng người dùng có thể sửa. Không dùng tên làm tên thư mục hoặc khóa tài sản.
5. Nút “Hủy”, “Lưu vào thư viện”, “Lưu và chọn”. “Lưu và chọn” là hành động chính.

Trạng thái cần có: chưa chọn ảnh; đang đọc/chuẩn hóa; preview sẵn sàng; tên không hợp lệ; file không hợp lệ; đang lưu; lưu thành công; lỗi ghi file/thiếu dung lượng. Đóng hộp chọn file không phải lỗi và không làm mất lựa chọn trước đó. Trong lúc commit không cho bấm lưu hai lần; không hiển thị “Đã lưu” trước khi file và metadata hoàn tất.

Popup “Thư viện”, rộng tối đa khoảng 960 px, cao tối đa 80–85% cửa sổ:

- Hai tab Nhân vật / Trang phục, tổng số mục, ô tìm theo tên, sắp xếp Mới thêm / Dùng gần đây / Tên A–Z.
- Lưới ảnh có tên và trạng thái đang chọn; tải thumbnail theo nhu cầu, phân trang hoặc phân đoạn khi nhiều mục.
- Nút thêm mới và nút “Sử dụng ảnh này”; Escape đóng, Tab giữ trong popup, đóng xong trả focus về nút đã mở.
- Empty state riêng cho thư viện trống và tìm kiếm không có kết quả.
- Xóa phải chỉ rõ “Xóa khỏi thư viện”; ảnh đã dùng trong dự án vẫn được giữ trong bản lưu của dự án. Thao tác xóa là ẩn mục khỏi thư viện, không xóa vật lý ngay dữ liệu còn được dùng.
- Thay ảnh tạo phiên bản mới. Dự án cũ tiếp tục giữ ảnh cũ; muốn dùng phiên bản mới phải chọn và xác nhận lại.
- Đổi tên cập nhật nhãn thư viện, không tự tạo lại ảnh/video và không thay đổi hash ảnh.

**8. Cột kết quả và hành động chính**

Cột phải có hai tab “Ảnh mặc thử” và “Video”. Ảnh mặc thử hiển thị lớn vừa khung, video có controls chuẩn; bên dưới là trạng thái, thao tác duyệt và xuất. Các hành động liên quan trực tiếp đến kết quả nằm cạnh preview, không đẩy người dùng xuống khối thứ ba ở cuối trang.

| Trạng thái | Cột trái / nút chính | Cột phải |
|---|---|---|
| Chưa chọn đủ ảnh/nội dung | Hướng dẫn đúng mục còn thiếu; “Xem trước trang phục” chưa khả dụng | Placeholder trung tính, chưa có kết quả |
| Đủ đầu vào nhưng chưa lưu | “Xem trước trang phục”: lưu dự án/ảnh/thiết lập trước khi lấy báo giá | Tóm tắt hai ảnh đã chọn |
| Đang lưu | Khóa gửi lặp; ghi rõ đang lưu | Giữ preview đang có |
| Có báo giá ảnh | Hiện giá ảnh, thời hạn, nút xác nhận riêng | Giải thích sẽ tạo ảnh mặc thử trước |
| Đang tạo ảnh | “Đang tạo ảnh mặc thử…” | Tiến trình đúng dữ liệu có; nếu không có phần trăm thì chỉ trạng thái đang xử lý |
| Ảnh chờ duyệt | Chưa mở submit video | “Duyệt ảnh này”, “Tạo lại ảnh”, xem ảnh nguồn để so sánh |
| Ảnh đã duyệt | “Tạo video N giây” | Badge ảnh đã duyệt |
| Có báo giá video | Giá video và nút xác nhận tạo; không dùng giá ảnh làm tổng giá video | Giữ ảnh mặc thử trong khi chờ xác nhận |
| Đang tạo video | Khóa thay đầu vào đang được xử lý | Trạng thái/progress từ server; đóng mở desktop vẫn tiếp tục theo request cũ |
| Video chờ duyệt | Không tự render/export bản chưa duyệt | Phát video, xác nhận đã xem, “Duyệt video” |
| Video đã duyệt | Có thể tạo phiên bản mới bằng thao tác riêng | “Xuất MP4”; nếu cần dựng local thì báo “Đang chuẩn bị MP4” |
| Đổi ảnh/nội dung sau khi duyệt | Xác nhận việc kết quả cũ không còn hiện hành khi lưu | Giữ lịch sử nhưng đánh dấu cần duyệt lại; khóa xuất bản cũ như kết quả mới |
| Mất mạng/lỗi tải | “Thử tải lại” hoặc “Tiếp tục” dùng request cũ | Không tạo thêm request provider chỉ để lấy lại file |
| Kết quả upstream chưa rõ | Thông báo cần kiểm tra/đối soát; không mở tự gửi lại | Giữ thông tin operation và hướng dẫn phục hồi phù hợp |

Khung giá đặt gần nút chính, hiển thị riêng ảnh/video khi đã có quote. Thiếu rate hoặc chưa quote thì ghi rõ chưa có giá; không đặt cố định 1,89 US$ từ ảnh minh họa. Nút “Tạo lại” cũng phải qua xác nhận chi phí mới. Không gộp tự động duyệt ảnh vào thao tác tạo video để giống hình mẫu.

**9. Cơ chế lưu bền vững**

Thiết kế đề xuất: `ShortVideoAssetLibraryService` ở native desktop, kho metadata SQLite riêng và file ảnh trong vùng dữ liệu ứng dụng. Không dùng localStorage của WebView làm nguồn lưu thư viện. Có thể dùng package SQLite đã hiện diện trong desktop; không trộn bảng vào database subtitle của Vietsub.

| Dữ liệu | Nội dung |
|---|---|
| Library item | ID, loại Character/Outfit, tên, user, organization, phiên bản hiện tại, ngày tạo/cập nhật/dùng gần nhất, trạng thái ẩn |
| Asset version | ID/version, SHA-256, MIME, width/height/size, đường dẫn nội bộ tương đối, thumbnail, thời điểm chuẩn hóa |
| Draft | Draft ID, user/org, nội dung chung và ghi đè, tỷ lệ/thời lượng/audio, hai ID/version được chọn, project đã gắn nếu có |
| Project selection | Project ID, vai trò ảnh, library ID/version nếu có, hash và metadata, bản ảnh riêng trong workspace, revision của thiết lập server |

Quy tắc lưu và phục hồi:

- Kho thư viện tách theo user + organization; ID do native tạo, thư mục được resolve trong root đã kiểm tra. Khi logout/đổi tổ chức hủy request đang đọc và dọn state/preview của scope cũ.
- Import dùng lại kiểm PNG/JPEG, giới hạn 10 MiB/16 MP, kiểm signature/decode, xử lý EXIF và loại metadata không cần. Tạo thumbnail riêng; luôn giữ bản ảnh chuẩn hóa đầy đủ cho generation.
- File ghi `.part`, kiểm lại hash rồi promote atomically; metadata commit sau khi file hợp lệ. Recovery nhận biết file dở/mục thiếu file; không báo thành công bằng thumbnail còn sót.
- SQLite có version schema, transaction và kiểm concurrency; khóa import/write theo scope và xử lý cả trường hợp hai cửa sổ cùng mở thư viện.
- Cùng ảnh đã có trong cùng loại/scope: báo tìm thấy ảnh trùng, cho dùng mục đã lưu; không âm thầm tạo nhiều bản giống nhau. Không deduplicate xuyên tài khoản/tổ chức.
- Khi dùng cho project, sao chép đúng phiên bản vào workspace `short-outfit` rồi kiểm hash trước khi gửi settings lên server. Project giữ bản riêng nên xóa file nguồn ngoài ứng dụng, đổi tên hoặc thay ảnh trong thư viện không phá project cũ.
- Không tải full-resolution của mọi mục khi mở hàng thumbnail. Đọc metadata theo trang, lazy-load thumbnail; kiểm với thư viện 0/1/6/50/200 mục.
- Đổi tài khoản/tổ chức không tự mang lựa chọn từ scope trước sang. React chỉ nhận ID, metadata an toàn và URL preview được native cấp theo scope; không nhận absolute path hay Base64.
- Với thư viện nằm ngoài workspace, bổ sung media resolver kiểm scope thay vì map toàn bộ thư mục thư viện thành một URL công khai có thể đoán. Không mở rộng quyền media mapping hiện có một cách chung chung.
- Backup thư viện gồm metadata và file ảnh nhất quán; hướng dẫn vị trí lưu và dung lượng trong phần quản lý. Export/import thư viện sang máy khác được để ở giai đoạn mở rộng.

**10. Bản nháp, tạo dự án và tương thích dữ liệu cũ**

- Sidebar Video ngắn mở cùng composer. Chế độ “Nhân vật + trang phục” hiện hai hàng ảnh ngay trên form. “Từ mô tả” tiếp tục có luồng tạo bằng text.
- Tạo dự án mới từ popup chung đưa đến cùng composer, không có một trang phối đồ với bố cục khác.
- Bản nháp có thể chọn tài sản trước khi có project. Có trạng thái Chưa lưu / Đang lưu / Đã lưu / Lỗi lưu; native giữ nội dung nháp theo scope để phục hồi khi mở lại.
- Lần bấm Lưu hoặc Xem trước đầu tiên tạo project, ghi lại liên kết draft→project trước trả kết quả, gắn ảnh rồi lưu settings. Chỉ lấy quote khi các bước này thành công.
- Chặn bấm lặp, dùng request/draft ID cho phục hồi. Nếu không xác định lần tạo project trước đã hoàn tất hay chưa, dừng và phục hồi project trước thay vì tự tạo project hoặc gửi AI lần nữa.
- Dự án TextOnly đã lưu không tự đổi mode khi click một thẻ thư viện. Muốn phối đồ từ nội dung đó thì tạo bản sao mới và hiển thị rõ thao tác này.
- Project CharacterOutfit cũ vẫn hiển thị hai ảnh workspace như mục “Ảnh đang dùng”, kể cả chưa có library ID. Cho “Lưu vào thư viện” theo từng ảnh; không quét và nhập toàn bộ project cũ tự động.
- Không ghi lại settings chỉ vì mở trang hoặc refresh; không làm tăng revision, mất approval hay quote hiện hành nếu đầu vào không thay đổi.

**11. Danh sách task triển khai**

Danh sách dưới đây giữ phạm vi công việc đã dùng khi triển khai. Source, test tự động, screenshot WebView2 và bản Debug/Release đã có; số kiểm thử và phần chưa nghiệm thu thực tế nằm trong biên bản kết quả. Các checkbox mục 13 là checklist để người dùng nghiệm thu, không phải xác nhận đã chạy provider thật.

| ID | Công việc cụ thể | Đầu ra và tiêu chí hoàn thành | Phụ thuộc |
|---|---|---|---|
| UI01 | Chốt luồng chung, phạm vi thư viện và toàn bộ trạng thái | Mapping rõ trang mới/project cũ, dữ liệu nháp, chọn ảnh, giá và approval; không còn đường vào riêng gây nhầm | — |
| UI02 | Thiết kế chi tiết hai cột | Bản bố cục desktop/hẹp và toàn bộ trạng thái empty/selected/loading/error; thông số typography, spacing, màu, focus | UI01 |
| UI03 | Định nghĩa contract thư viện, draft và lựa chọn | DTO public ở Shared trước nếu cần; TypeScript/C# đồng bộ; kiểm enum/ID/version/context; không nhận path từ DOM | UI01 |
| UI04 | Kho thư viện native | Metadata SQLite riêng, ảnh/version/thumbnail, chuẩn hóa, chống trùng, atomic write, recovery, scope user/org | UI03 |
| UI05 | Bridge và preview thư viện | List/import/rename/replace/delete/select, hủy và request ID; kiểm scope trước/sau IO, media preview không vượt phạm vi | UI03, UI04 |
| UI06 | Hàng Nhân vật và Trang phục | Component dùng chung với cấu hình crop khác nhau; ô tải ảnh, thêm, tên, dấu tích, cuộn ngang, bàn phím, trạng thái trống/lỗi | UI02, UI05 |
| UI07 | Popup thêm và cửa sổ quản lý | Lưu và chọn, đổi tên, thay phiên bản, xóa khỏi thư viện, tìm/sắp xếp/xem tất cả; đóng mở lại dữ liệu còn đúng | UI06 |
| UI08 | Composer và bản nháp trước project | Hai hàng xuất hiện trực tiếp ở trang Video ngắn; lưu/khôi phục draft, tạo/gắn project một lần, chặn gửi AI khi lưu lỗi | UI03, UI05, UI06 |
| UI09 | Snapshot ảnh cho project, tương thích project cũ | Chọn thư viện tạo bản workspace đã kiểm hash; ảnh cũ có thể lưu vào thư viện; sửa/xóa thư viện không đổi output dự án cũ | UI04, UI08 |
| UI10 | Kết nối cột kết quả và approval | Tab ảnh/video, báo giá riêng, trạng thái hợp lệ, đổi input/quote expired/Unknown/retry; giữ kiểm generation và export | UI08, UI09 |
| UI11 | Nội dung, tỷ lệ, thời lượng, audio và giá | Form gọn theo mẫu, mapping Background/Motion rõ, snapshot project cũ được giữ, giá thật và lý do nút bị khóa | UI08, UI10 |
| UI12 | Gợi ý nội dung trên máy | Chọn mẫu, xem trước, chèn/thay rõ ràng; không tự ghi đè bản nháp và không gọi AI | UI11 |
| UI13 | Responsive, DPI và khả năng truy cập | Kiểm kích thước cửa sổ/DPI, focus/Tab/Enter/Arrow/Escape, tên dài, popup, overflow; không thumbnail mất nội dung quan trọng | UI06–UI12 |
| UI14 | Kiểm thử và xác minh bản chạy | Regression thư viện/scope/revision/chi phí/khôi phục và video cũ; build/test chuẩn; đúng bundle và flag ở Debug/Release; ghi rõ Passed/Failed/Skipped | UI04–UI13 |
| UI15 | Bàn giao và nghiệm thu người dùng | Cập nhật tài liệu, ảnh chụp giao diện bản thật với fixture có quyền dùng, checklist so với ảnh mẫu; nghiệm thu AI thật là bước riêng có dữ liệu và trần phí được chỉ định | UI14 |

Các component dự kiến để phân chia trách nhiệm: `ShortVideoComposer`, `ShortVideoAssetStrip`, `ShortVideoAssetCard`, `ShortVideoAssetDialog`, `ShortVideoLibraryDialog`, `ShortVideoResultPanel`, `ShortVideoActionBar`. Đây là tên đề xuất; chỉ tách khi giúp quản lý state và dùng lại, không viết lại toàn bộ `App.tsx`.

**12. Mốc bàn giao**

- Mốc A — UI01–UI02: đặc tả bố cục và trạng thái đủ để rà lại theo ảnh mẫu trước khi làm giao diện.
- Mốc B — UI03–UI09: có thư viện lưu thật, hai hàng chọn ảnh, popup, bản nháp và project snapshot. Mở lại ứng dụng/dự án vẫn đúng ảnh.
- Mốc C — UI10–UI12: thao tác ảnh mặc thử → duyệt → video → duyệt → MP4 trong một màn hình; quote/xác nhận vẫn đúng từng bước.
- Mốc D — UI13–UI15: hoàn thiện DPI/responsive, test và cập nhật đúng bản desktop đang dùng, rồi nghiệm thu giao diện.

Phân biệt hai kết quả: UI và thư viện có thể nghiệm thu bằng ảnh hợp lệ, fixture và kiểm luồng không tốn phí; chất lượng nhân vật mặc đồ và video thật cần nghiệm thu provider riêng. Không đánh dấu cả hai đã đạt từ một build xanh.

**13. Tiêu chí nghiệm thu bắt buộc**

- [ ] Mở trang Video ngắn ở chế độ phối đồ thấy ngay hai hàng Nhân vật/Trang phục và cột kết quả.
- [ ] Mỗi hàng có tải ảnh, thêm tài sản, thumbnail, tên và lựa chọn viền xanh/dấu tích như ảnh tham chiếu.
- [ ] Lưu nhân vật/trang phục, tắt mở ứng dụng rồi tạo dự án khác vẫn chọn lại được trong đúng scope.
- [ ] Đổi tài khoản/tổ chức không thấy thumbnail, kết quả tìm kiếm hoặc response muộn từ scope khác.
- [ ] Xóa file nguồn bên ngoài không làm mất ảnh đã lưu. Thay/xóa mục thư viện không phá ảnh của dự án cũ.
- [ ] Rename không làm mất approval; đổi ảnh dùng cho project làm invalidation đúng revision.
- [ ] File sai định dạng, quá giới hạn, hỏng, bị đổi hash hoặc ghi dang dở đều có xử lý rõ; không báo lưu thành công giả.
- [ ] Thumbnail không được dùng nhầm làm ảnh full-resolution gửi AI; byte gửi đúng ảnh của project đã kiểm hash.
- [ ] Hủy hộp chọn ảnh, đổi project khi đang load và bấm liên tiếp không làm kẹt busy hoặc gắn nhầm tài sản.
- [ ] Chỉ xem/chọn/lưu thư viện không tạo quote có tác động chi phí hoặc request provider; tạo ảnh/video vẫn xác nhận riêng.
- [ ] Duyệt và xuất bị chặn với kết quả stale; retry tải output không tạo thêm job AI.
- [ ] Project cũ TextOnly và CharacterOutfit mở được; không tự đổi mode hoặc mất nội dung/ảnh/approval.
- [ ] Hai cột, popup, hàng thumbnail, nút và tên dài đạt ở các kích thước/DPI mục 3; sử dụng được bằng bàn phím.
- [ ] Bộ lệnh chuẩn đạt: restore/build/test .NET; npm ci/build/test frontend; báo riêng Passed/Failed/Skipped. SQL/model opt-in skip không tính là đã nghiệm thu thực tế.
- [ ] Bản chạy Debug/Release thực sự có bundle mới và feature đúng. Không kết luận đã sửa giao diện chỉ từ source khi người dùng còn mở bundle cũ.

**14. Phần chưa nằm trong đợt này**

Đồng bộ thư viện nhiều máy, chia sẻ tài sản trong tổ chức, nhiều nhân vật/bộ đồ cùng clip, tự tạo nhân vật bằng AI, tự tách đồ khỏi ảnh, huấn luyện nhận diện và bảo đảm giữ mặt/họa tiết tuyệt đối cần kế hoạch riêng. Giai đoạn này không thay provider, tự cấu hình giá hay bỏ điều kiện duyệt hiện có.

Lượt lập kế hoạch ban đầu không chạy code/SQL/AI. Lượt triển khai 2026-09-11 đã thay source, build và kiểm thử UI/thư viện; không thêm SQL Server migration, đổi giá hoặc gọi provider có phí. Nếu đổi phạm vi sang thư viện server dùng chung, phải bổ sung contract, migration và quyền sở hữu trước.
