# Triển khai liên kết xử lý cấu hình AI trong Admin

Các trạng thái thiếu key, policy, đơn giá, ngân sách và model có thao tác đưa admin đến đúng vị trí cần sửa. Card Fal chưa được chọn có liên kết **Chọn Fal/Veo cho Video dài**, giữ nguyên ý nghĩa đây là lựa chọn tùy chọn. Nút nhập key ghi rõ provider; bỏ nút credential lặp trong cùng card.

Ngày triển khai 2026-10-15 trên `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, cùng working tree chưa commit. [Plan task](PLAN_TASK_LIEN_KET_SUA_CAU_HINH_AI_ADMIN.md) đã được cập nhật kết quả. Các thay đổi có sẵn của người dùng được giữ nguyên.

## Thay đổi UI và logic

- `admin-organizations.js` cung cấp `openSetupTarget` dùng chung: xác minh tổ chức, quyền, provider/model, chống phản hồi cũ và mở đúng form. Trường hợp tổ chức ngoài trang danh sách hiện tại vẫn xác định đúng qua API tổ chức.
- Key, policy và budget được ràng buộc với tổ chức khi mở form và khi submit. Đổi tổ chức hoặc mất phiên không được dùng phản hồi cũ để mở popup; key bị xóa khi đóng hoặc reset.
- Bảng giá mở đúng model và MissingUsageTypes; focus ô đơn giá trong popup. Provider/model tắt chuyển đến đúng nút cấu hình, không tự bật hoặc chuyển model.
- Wizard có thao tác xử lý từng check chưa đạt. Các liên kết sửa cấu hình chuyển bước trong UI hoặc sang trang riêng, giữ trường không nhạy cảm trong RAM. Quay lại từ SePay giữ nội dung đang nhập; revision mới ở nơi khác được báo để rà soát.
- Phân bổ giữ nhóm, tổ chức đang kiểm tra và bước khi quay lại. Hết chỗ có nút điều chỉnh sức chứa. Mapping dùng form hiện có; SePay ở trang riêng.
- `admin-modules.css` bổ sung focus/highlight và vùng quay lại; sửa giới hạn chiều rộng grid bảng giá để các model và bảng rate không đẩy trang tràn ngang trên mobile.
- `admin.js` trả Promise của module được điều hướng để caller chờ dữ liệu; reset điều hướng tổ chức khi đăng xuất/hết phiên.
- Khi admin chủ động chuyển menu/tab/tổ chức, điểm quay lại cũ được dọn. Thao tác mới của tổ chức B tạo điểm quay lại của B; mở rộng menu không tự hủy điều hướng đang chờ.

Liên kết chỉ điều hướng và đọc dữ liệu. Ghi key/policy/rate/budget, kiểm tra readiness nhóm hoặc bật phân bổ vẫn cần thao tác lưu/kiểm tra hiện hành của admin và validation server. Sau lưu, metadata/readiness được đọc lại; không tự nâng trạng thái nhóm chỉ vì đã nhập key.

Phạm vi source: 5 file JavaScript/CSS hiện có, một browser suite mới và các assertion điều hướng hiện hành trong `OrganizationAdminUiTests.cs`. Không thêm API, DTO, schema, package hoặc SQL cho tính năng này.

## Kiểm chứng tự động

| Kiểm tra thực chạy | Passed | Failed | Skipped | Phạm vi |
|---|---:|---:|---:|---|
| Restore và Release solution build | Đạt | 0 lỗi build | Không áp dụng | Có cảnh báo NU1902/NU1903 của `System.Security.Cryptography.Pkcs` 9.0.11 hiện có |
| Toàn bộ browser Admin cùng TikTok trên source cuối | 92 | 0 | 0 | Asset thật với API fixture; gồm 13 ca mới và ca dọn điểm quay lại A → B |
| npm ci và frontend build | Đạt | 0 | Không áp dụng | Có cảnh báo Vite về kích thước chunk hiện có |
| Frontend Vitest | 296 | 0 | 0 | 45 file test |
| Full .NET Release cuối | 1697 | 0 | 18 | 1715 tổng; 4 phút 50 giây; model/GPU/SQL opt-in bị skip không tính là đạt |

Các ca mới kiểm đúng tổ chức/provider/model, tổ chức ngoài trang phân trang, chỉ ghi sau submit, refresh readiness sau lưu, key không vào storage, quyền Owner/BillingManager/Viewer/Global Admin, phản hồi chậm, GET lỗi có cache, double click, revision conflict, XSS, quay lại wizard/phân bổ, dọn điểm quay lại khi chuyển A → B và bàn phím ở 390/768/1440/1920 px. Các suite wizard hiện hành còn kiểm zoom CSS; chưa có smoke zoom browser thủ công trên phiên thật.

Lệnh chạy browser sử dụng Playwright đã có tại `.tmp/license-ui-browser/node_modules/playwright` và Chromium headless 1243 đã cài trên máy. Preload chỉ chọn executable trình duyệt; không thay source hay assertion test để xử lý khác phiên bản Chromium cache. Không chạy server ứng dụng, kết nối database, gửi webhook hoặc gọi provider thật trong fixture.

Bằng chứng:

- [Full .NET TRX cuối](output/admin-readiness-links-verification/readiness-links-final.trx).
- [Hash source và kết quả](output/admin-readiness-links-verification/report.json).
- [Các ca browser mới](TOOL-TESTS/Admin/admin-readiness-links.browser.test.cjs).
- Bản source trước lượt sửa nằm trong `output/admin-readiness-links-verification/before`, dùng đối chiếu với các thay đổi đã có của người dùng.

## Ảnh giao diện từ source

Các ảnh dùng dữ liệu giả lập, không có key hoặc dữ liệu người dùng thật:

- [Card cảnh báo và liên kết xử lý](output/admin-readiness-links-verification/screenshots/cards-1440.png).
- [Policy Video dài được focus](output/admin-readiness-links-verification/screenshots/policy-focus-1440.png).
- [Ngân sách đúng tổ chức](output/admin-readiness-links-verification/screenshots/budget-focus-1440.png).
- [Popup đơn giá đúng model trên mobile](output/admin-readiness-links-verification/screenshots/rate-focus-390.png).
- [Popup key trên mobile](output/admin-readiness-links-verification/screenshots/credential-390.png).
- [Quay lại nhóm và tổ chức đang kiểm tra](output/admin-readiness-links-verification/screenshots/allocation-return-1440.png).

## Trạng thái môi trường

| Mức kiểm chứng | Trạng thái |
|---|---|
| Source | Đã triển khai |
| Build và kiểm thử tự động | Đã đạt theo bảng trên |
| Smoke phiên Admin/runtime thật | Chưa xác minh |
| Rollout hosting | Chưa thực hiện |

Không sửa dữ liệu hosting, chạy migration, thay credential thật, tạo request AI có phí hoặc publish release. Hosting cần được cập nhật các asset cùng bản trước khi kết luận giao diện đang chạy đã có tính năng này; cache busting Razor hiện hành tiếp tục dùng `asp-append-version`.
