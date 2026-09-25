# Triển khai Admin Web vuông, không viền

Ngày 2026-09-23. Nhánh `main`, nền `0a85b569914a9a230425442e9f0b3e1c16072cf1`; working tree có thay đổi từ trước. Người dùng đã yêu cầu triển khai sau yêu cầu lập kế hoạch. Giữ nguyên các thay đổi desktop, đăng xuất và publish có sẵn.

## Thiết kế và phạm vi

Áp dụng ảnh concept VideoMaker Admin đã tạo trong hội thoại: sidebar xanh đậm `#172137`, nền sáng `#f4f7fb`, panel trắng, nhấn xanh `#315bea`, góc vuông và không đường viền trang trí. Phân vùng bằng màu nền, khoảng cách, cỡ/chất chữ. Áp dụng đồng bộ Tổng quan, Người dùng, Tổ chức & AI (các tab và màn hình thiết lập), TikTok, Gói sử dụng, Desktop Releases, đăng nhập và dialog.

Giữ Razor + JavaScript hiện tại, không đổi framework hoặc API/database. Số liệu trong ảnh chỉ minh họa: source đếm phiên online, không phải thiết bị online; chưa có API tạo người dùng từ admin hoặc tìm kiếm toàn hệ thống. Nút chính dùng Quản lý người dùng; tìm kiếm gọi endpoint người dùng hiện có. Số liệu tổng lấy từ API tổng quan; bảng xem nhanh ghi rõ phạm vi trang đầu, không suy ra số license từng gói từ dữ liệu phân trang. Không thêm nút chuông thông báo không có chức năng.

Ảnh tham chiếu: `C:\Codex\20x\generated_images\01a0cd58-95e4-74f0-808f-4832fe5d0f99\exec-1e03ae68-051c-498b-91f7-35068d849597.png`.

## Task

| ID | Công việc | Điều kiện hoàn thành |
|---|---|---|
| A01 | Rà source/DOM/API/test, ghi kế hoạch | Xác định selector đang dùng và khác biệt giữa concept với dữ liệu thật |
| A02 | Chuẩn hóa CSS | Radius 0, bỏ border/shadow/gradient trang trí, tăng cỡ chữ, giữ trạng thái và chỉ báo focus bàn phím |
| A03 | Sidebar/header/đăng nhập | Bố cục theo mẫu, tìm người dùng hoạt động, menu mobile và đăng xuất dùng được |
| A04 | Trang Tổng quan | Bốn chỉ số thật, bảng license xem nhanh, các gói hiện có, nhắc hết hạn từ API |
| A05 | Các màn hình và dialog còn lại | Đồng bộ kiểu hiển thị; giữ ID/data attributes, validation, loading, quyền và các thao tác hiện hành |
| A06 | Responsive/accessibility | Kiểm 390/768/1024/1440/1920 px; không tràn trang, bảng rộng cuộn riêng; Tab/Escape/focus và reduced motion |
| A07 | Regression UI | Dữ liệu giả trong browser, search/pagination/detail/dialog, trạng thái rỗng/lỗi, TikTok và tổ chức; không gọi server/database/provider thật |
| A08 | Build/test | Lệnh theo AGENTS, ghi Passed/Failed/Skipped thực tế; không dùng số test cũ |
| A09 | Bàn giao | Ảnh giao diện thật, tài liệu kết quả và giới hạn; chưa deploy môi trường dùng chung |

Thứ tự: A01 → A02/A03 → A04/A05 → A06/A07 → A08 → A09. Kết quả thực hiện và phạm vi kiểm chứng ghi ở cuối tài liệu.

## Quy tắc giữ hành vi

- Không đưa dữ liệu demo, số đếm hay giá minh họa vào runtime.
- Không đổi luồng đăng nhập/refresh, quyền admin, credential lifecycle, xác nhận cấp/thu hồi, TikTok OAuth, upload release, pricing hoặc ngân sách.
- Giữ việc xóa secret khỏi form và lọc nội dung trước render; không đưa secret vào ảnh hoặc fixture.
- Viền trang trí bị bỏ. Chỉ báo `focus-visible` và chế độ forced-colors vẫn phải rõ để dùng bàn phím/high contrast.
- Không khởi động server thật để chụp ảnh; dùng Razor/CSS/JS hiện hành với API giả trong trình duyệt. Production cần triển khai và smoke riêng.

## Xác minh dự kiến

Build/test .NET toàn solution và frontend theo AGENTS; JavaScript syntax; các bài admin TikTok hiện có; bổ sung browser regression cho shell/tổng quan và kiểm hình học thực tế của các màn hình. Chụp ảnh desktop/mobile/dialog trong `artifacts/admin-square/`, ảnh tham chiếu cũng lưu tại đây. Không tính test opt-in bị skip là đã nghiệm thu môi trường hoặc model.

## Kết quả

Đã hoàn thành A01–A09 trong phạm vi source, kiểm thử tại workspace và bàn giao ảnh/tài liệu. Bốn chỉ số Tổng quan, danh sách license, gói và nhắc hết hạn dùng API hiện có. Tìm kiếm trên header mở danh sách người dùng, giữ phân trang và không làm thay đổi tập dữ liệu xem nhanh của Tổng quan. Điều hướng mobile có mở/đóng và Escape; dialog và các màn hình tổ chức/TikTok dùng cùng kiểu CSS. Chỉ báo focus/high contrast được giữ riêng.

Các file chính:

- `TOOL-SERVER/Pages/Admin/Index.cshtml`: shell, header, bố cục Tổng quan và marker `admin-square-20260923.1`.
- `TOOL-SERVER/wwwroot/admin/admin.css`: giao diện chung, responsive, focus và forced-colors.
- `TOOL-SERVER/wwwroot/admin/admin.js`: render số liệu, tìm người dùng và điều hướng.
- `TOOL-TESTS/Admin/admin-shell.browser.test.cjs`: 9 bài browser mới; chạy cùng 7 bài TikTok state và 11 bài TikTok browser hiện có. Fixture nằm trong test, không đưa dữ liệu demo vào runtime.
- `TOOL-TESTS/Organizations/OrganizationProvisioningUiTests.cs`, `scripts/Test-AdminRuntimeAssets.ps1`: cập nhật marker kiểm tra asset đồng bộ. Không chạy script kiểm runtime vào server thật.

Kiểm tra tại workspace Windows hiện tại, nhánh/commit nền như đầu tài liệu, working tree chưa commit:

| Kiểm tra | Kết quả |
|---|---|
| `dotnet restore TOOL_GEN_POST_VIDEO.slnx` | Đạt |
| Release build toàn solution | Đạt; MSBuild 0 Warning / 0 Error |
| Build riêng TOOL-SERVER sau chỉnh CSS mobile cuối | Đạt; 0 Warning / 0 Error |
| `npm ci --no-audit --no-fund`, `npm run build` tại TOOL-LOCAL/Web | Đạt; production build cũng được gọi bởi MSBuild, Vite còn cảnh báo chunk >500 kB |
| `npm test` tại TOOL-LOCAL/Web | 268 Passed / 0 Failed / 0 Skipped, 43 file |
| Admin shell + TikTok state/browser | 27 Passed / 0 Failed / 0 Skipped |
| `node --check TOOL-SERVER/wwwroot/admin/admin.js` | Đạt |
| Full .NET | 1.528 Passed / 0 Failed / 13 Skipped / 1.541 Total, 5 phút 26 giây |

Full .NET chạy Release `--no-build`, collection tuần tự bằng `xUnit.ParallelizeTestCollections=false`, TEMP/TMP tại `D:\tmp\vm-admin-square-20260923`; TRX: `artifacts/admin-square/full-suite.trx`. Các bài model/runtime/SQL opt-in bị skip không được tính là đã nghiệm thu. Hash source của lượt bàn giao: `artifacts/admin-square/verification.json`.

Lệnh browser từ thư mục gốc (cần Playwright/Chromium có sẵn; có thể dùng `VIDEOMAKER_PLAYWRIGHT_MODULE` trỏ tới package ngoài repository):

```powershell
$env:VIDEOMAKER_SCREENSHOT_DIR = (Resolve-Path 'artifacts/admin-square').Path
node --test TOOL-TESTS/Admin/admin-shell.browser.test.cjs TOOL-TESTS/TikTok/admin-tiktok-state.test.cjs TOOL-TESTS/TikTok/admin-tiktok.browser.test.cjs
```

Browser chặn network và dùng Razor/CSS/JS hiện hành với API giả. Đã kiểm tra bố cục tại 390/768/1024/1440/1920 px; bảng cuộn trong vùng riêng, trang không tràn ngang. Kiểm thêm năm tab chi tiết tổ chức ở 390/1440 px, dialog mobile, Tab/Escape, reduced motion và high contrast. Đã mở xem ảnh thực tế; chỉnh phần tiêu đề provider trên điện thoại sau khi phát hiện biểu tượng bị co nhỏ.

Ảnh tại `artifacts/admin-square/` (thư mục artifact bị Git bỏ qua): [Tổng quan desktop](artifacts/admin-square/overview-1440.png), [Tổng quan mobile](artifacts/admin-square/overview-390.png), [Người dùng](artifacts/admin-square/users-1440.png), [Thiết lập tổ chức](artifacts/admin-square/organization-setup.png), [API AI mobile](artifacts/admin-square/organization-detail-providers-390.png), [TikTok](artifacts/admin-square/tiktok-admin-desktop.png), [Đăng nhập](artifacts/admin-square/login-desktop.png). Đây là ảnh giao diện đã triển khai với dữ liệu kiểm thử.

**Chưa xác minh** đăng nhập/CRUD/OAuth trên server và database thật; **chưa rollout production**. Không chạy migration, gọi provider có phí, đổi credential, cấu hình SQL hoặc thay gói desktop của công việc trước. Khi triển khai server sau này cần smoke `/admin`, kiểm marker/asset và các thao tác với quyền phù hợp trên chính môi trường đích.
