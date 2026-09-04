# Ghi nhận vấn đề

## [UI-ROUTING-001] Mở lại dự án Video ngắn bị chuyển sang màn Video dài

- Ngày ghi nhận: 2026-09-03
- Trạng thái: Chưa triển khai
- Mức độ: Cao — mở sai luồng thao tác và có thể khiến người dùng thực hiện nhầm hành động Video dài trên dự án Video ngắn.

### Hiện trạng

Khi người dùng vào danh sách dự án và chọn một dự án đã được tạo từ luồng **Video ngắn**, ứng dụng lại mở giao diện **Tạo Video dài** (Long-form Studio). Lỗi xuất hiện rõ khi mở lại dự án sau đó, không chỉ ngay sau khi tạo.

### Nguyên nhân kỹ thuật đã xác nhận

1. Hàm chọn dự án trong `TOOL-LOCAL/Web/src/App.tsx` luôn gọi `setPage('longVideo')` khi trang hiện tại không phải `create`.
2. Dấu hiệu loại workflow đã được lưu bền vững trong dữ liệu: dự án Video ngắn tạo `Script.StructureType = "DirectShortVideo"`.
3. `ProjectDashboard` đã trả trường `workflowStructureType` về frontend, nhưng logic điều hướng chưa sử dụng trường này.
4. `shortVideoProjectId` chỉ là React state trong phiên hiện tại và chỉ được gán khi nhận sự kiện `short-video.started`; state này không đủ để nhận diện dự án sau khi đóng/mở ứng dụng.

### Kết quả mong muốn

- `workflowStructureType = "DirectShortVideo"` → mở màn **Video ngắn**.
- `workflowStructureType = "OpenAiStructuredPlan"` → mở màn **Video dài**.
- Dự án đã chọn phải được bind vào đúng màn hình kể cả sau khi khởi động lại desktop.
- Không dùng state tạm `shortVideoProjectId` làm nguồn sự thật duy nhất để phân loại dự án.
- Bổ sung regression test cho cả hai nhánh điều hướng.

### Phạm vi dự kiến khi xử lý

- Điều chỉnh logic frontend sau khi nhận `dashboard.state` của dự án được chọn.
- Có thể bổ sung loại workflow vào dữ liệu danh sách dự án nếu cần hiển thị/điều hướng sớm; không mặc định cần migration database vì dữ liệu `DirectShortVideo` đã tồn tại trong Script.
- Không thay đổi dữ liệu hoặc chạy SQL để xử lý lỗi này.

