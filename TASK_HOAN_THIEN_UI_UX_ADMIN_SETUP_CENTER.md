# Task hoàn thiện UI/UX Admin Setup Center

Mục tiêu: khôi phục giao diện quản trị chuyên nghiệp tại `/admin`, bảo đảm phần Trung tâm thiết lập và Phân bổ khách hàng có đầy đủ CSS, đồng thời giúp người vận hành nhận biết ngay khi đang mở nhầm build hoặc nhầm repository.

## TASK-01 — Xác minh đúng ứng dụng đang chạy

- [x] Kiểm tra đường dẫn CSS/JavaScript mà Razor Admin đang khai báo.
- [x] Đối chiếu tiến trình đang chiếm `https://localhost:7202` với repository hiện tại.
- [x] Xác định nguyên nhân: cổng `7202` đang chạy executable từ checkout `Branch-Tool-Sub`, trong khi source hiện tại còn thiếu CSS cho các component mới.
- [x] Bổ sung dấu nhận diện build trong HTML và công cụ kiểm tra runtime chỉ đọc.

## TASK-02 — Hoàn thiện CSS cho Trung tâm thiết lập

- [x] Thiết kế hero tiến độ, CTA và thanh tiến độ rõ ràng.
- [x] Thiết kế danh sách bước với trạng thái sẵn sàng/cần xử lý.
- [x] Thiết kế ghi chú luồng vận hành và trạng thái loading/error.
- [x] Giữ giao diện thống nhất với design system Admin hiện tại.

## TASK-03 — Hoàn thiện UI nhóm phân bổ khách hàng

- [x] Bổ sung style cho phần tóm tắt danh sách nhóm.
- [x] Bổ sung style cho trang chi tiết độc lập, nút quay lại và bảng dữ liệu.
- [x] Làm rõ checklist thiết lập, sức chứa và trạng thái sẵn sàng.
- [x] Hoàn thiện khối giải thích trong dialog cấu hình tổ chức.

## TASK-04 — Responsive và accessibility

- [x] Tối ưu Setup Center, tab phạm vi và bảng phân bổ trên tablet/mobile.
- [x] Bổ sung focus-visible cho thành phần tương tác mới.
- [x] Tôn trọng `prefers-reduced-motion` và hỗ trợ forced colors cơ bản.
- [x] Khai báo progressbar có nhãn và giá trị cho trình đọc màn hình.

## TASK-05 — Chống nhầm build và cache cũ

- [x] Gắn marker phiên bản UI không chứa thông tin nhạy cảm vào trang Admin.
- [x] Hiển thị marker nhỏ trong sidebar để Admin xác minh bằng mắt.
- [x] Tạo script chỉ đọc kiểm tra HTML, CSS và JavaScript thực tế từ URL localhost.
- [x] Script phải báo rõ khi URL đang phục vụ nhầm checkout/build cũ.

## TASK-06 — Regression test

- [x] Kiểm tra các class do JavaScript sinh ra đều có selector CSS quan trọng.
- [x] Kiểm tra build marker, progressbar và quy tắc accessibility tồn tại.
- [x] Kiểm tra script chẩn đoán runtime có cú pháp PowerShell hợp lệ.

## TASK-07 — Xác minh kỹ thuật

- [x] Kiểm tra cú pháp JavaScript và PowerShell.
- [x] Chạy `dotnet restore`.
- [x] Chạy Release build.
- [x] Chạy toàn bộ test.

Kết quả xác minh ngày 2026-09-04: restore thành công, Release build đạt 0 warning/0 error và 609/609 test đạt.

## TASK-08 — Nghiệm thu tại trình duyệt

- [ ] Mở đúng build của repository hiện tại và kiểm tra `/admin` ở desktop.
- [ ] Kiểm tra responsive ở tablet/mobile.
- [ ] Xác nhận không còn component chữ thô, tràn khung hoặc thiếu style.

> Lưu ý vận hành: không dừng Visual Studio hoặc server hiện tại chỉ để chiếm lại cổng `7202`. Khi nghiệm thu, người vận hành cần chạy đúng project trong repository này hoặc chọn một cổng localhost khác.

Kiểm tra build đang được localhost phục vụ bằng lệnh chỉ đọc:

```powershell
.\scripts\Test-AdminRuntimeAssets.ps1 -AdminUrl https://localhost:7202/admin
```

Nếu certificate development chưa được máy tin cậy, chạy bằng PowerShell 7 (`pwsh`) và thêm `-SkipCertificateCheck`. Marker mong đợi của task này là `admin-setup-center-20260904.1`. Các mục TASK-08 chỉ được đánh dấu hoàn tất sau khi mở đúng server của repository và nghiệm thu thủ công bằng trình duyệt.
