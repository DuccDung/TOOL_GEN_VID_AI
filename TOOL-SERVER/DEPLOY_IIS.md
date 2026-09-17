# Đóng gói TOOL-SERVER cho IIS

Source kiểm tra trên nhánh `main`, commit `3ec0f1d` cùng các thay đổi chưa commit trong working tree, ngày 2026-09-16. Tài liệu này mô tả cách tạo gói tại máy phát triển; gói thành công chưa xác nhận hosting hoặc database đích đã sẵn sàng.

Chạy từ thư mục gốc repository:

```powershell
.\scripts\Publish-ToolServerIis.ps1
```

Script tạo thư mục publish, file ZIP và SHA-256 tương ứng trong `artifacts/`. Ứng dụng được publish thành `TOOL-SERVER.dll` **không có `TOOL-SERVER.exe`**. Gói kèm runtime .NET 10 và ASP.NET Core 10 x64 từ máy build trong thư mục `runtime/`, gồm `runtime/dotnet.exe` và các file license/attribution gốc. Máy build phải có hai runtime cùng phiên bản và `hostfxr` tương ứng. Giải nén **nội dung** ZIP vào physical path của IIS application. IIS vẫn cần ASP.NET Core Hosting Bundle/`AspNetCoreModuleV2` ở cấp server để nhận request, dù không cần dùng .NET 10 runtime cài toàn máy để chạy DLL này. Chọn application pool **No Managed Code**, chạy x64 (`Enable 32-Bit Applications = False`), dùng HTTPS và cấp quyền **Read & Execute** cho pool; chỉ cấp quyền ghi ở các thư mục dữ liệu/log thực sự cần.

`web.config` tại root của gói gọi `.\runtime\dotnet.exe .\TOOL-SERVER.dll` theo chế độ IIS out-of-process, map mọi request vào ASP.NET Core và cho phép GET, HEAD, OPTIONS, POST, PUT, PATCH, DELETE qua IIS Request Filtering. Nó cũng bỏ WebDAV handler/module ở phạm vi application để tránh xung đột PUT/DELETE. `TRACE` và các method khác không được bật. Quyền method ở đây chỉ quyết định request có tới ứng dụng hay không; controller vẫn giữ auth và kiểm tra quyền hiện hành.

Script bỏ `appsettings.Development.json` và để trống các giá trị connection string/secret trong `appsettings.json` **của gói**. File source không bị sửa. Nếu vận hành bằng `appsettings.json` trên Plesk, sau khi giải nén gói vào physical path của IIS application, quản trị viên điền `Jwt:SigningKey` và các giá trị cần thiết vào **file trên máy host** trước khi khởi động ứng dụng. Giữ file này ngoài Git/ZIP, giới hạn quyền đọc và kiểm tra lại sau mỗi lần triển khai vì gói mới có thể ghi đè nó. Hosting cũng có thể cấp `ConnectionStrings__VideoFactory`, `Jwt__SigningKey` và các secret khác qua secret manager hoặc biến môi trường của application pool; không ghi chúng vào `web.config`.

**Trước request đầu tiên**, xác minh tiến trình ứng dụng IIS đọc được `ConnectionStrings:VideoFactory` và `Jwt:SigningKey` (ít nhất 32 byte UTF-8) từ `appsettings.json` trên host hoặc từ cấu hình hosting. Hai giá trị này trong ZIP đều trống; biến môi trường cùng tên sẽ ghi đè giá trị trong file. `Program.cs` chạy bootstrap role/license/catalog trên database trước `app.Run()`, nên thiếu connection string hoặc database/schema/quyền chưa sẵn sàng sẽ khiến ANCM trả HTTP 502.5. Thiếu JWT signing key cũng khiến validation khi startup thất bại. Không dùng kết quả build hoặc runtime `--list-runtimes` làm bằng chứng startup đã đạt.

### Khi IIS trả 502.5

1. Xem Windows Event Viewer → **Windows Logs → Application**, lọc nguồn **IIS AspNetCore Module V2** hoặc tên ứng dụng. Lấy thông báo exception đầu tiên, sau khi xóa secret/connection string nếu có.
2. Nếu không truy cập được Event Viewer, tạm đổi `stdoutLogEnabled="false"` thành `"true"` trong `web.config` của site và cho application pool quyền ghi thư mục `logs/`; tải lại trang một lần rồi đọc file `logs/stdout_*.log`. Trả `stdoutLogEnabled` về `false` sau khi tìm nguyên nhân và xử lý log theo quy định lưu giữ của hosting. Không gửi log nguyên bản nếu nó chứa secret hoặc dữ liệu nhạy cảm.
3. `The ConnectionString property has not been initialized` nghĩa là tiến trình IIS chưa nhận `ConnectionStrings__VideoFactory`. Nếu nhận `SqlException`, kiểm tra SQL instance/database, quyền, mạng và schema ở **môi trường đích** trước khi khởi động lại. Lỗi JWT key hoặc `OptionsValidationException` cần kiểm `Jwt__SigningKey`.
4. Nếu log nói không tìm thấy hoặc không chạy được tiến trình, xác minh tại physical path có `web.config`, `TOOL-SERVER.dll`, `runtime/dotnet.exe`; application pool x64 có quyền Read & Execute. Chạy `runtime\dotnet.exe --list-runtimes` trên máy host nếu có terminal. IIS vẫn cần `AspNetCoreModuleV2` ở cấp server.

Nếu hosting trả 405/404.6, kiểm tra IIS substatus, Request Filtering và WebDAV ở cấp server/site. Nếu trả 500.19, có thể hosting đã khóa `<modules>`, `<handlers>` hoặc `<requestFiltering>`; người quản trị hosting phải mở quyền cấu hình tương ứng hoặc áp chính sách ở cấp server. `web.config` của ứng dụng không thể vượt qua policy đã khóa, proxy/WAF ở phía trước, hay hosting không hỗ trợ ASP.NET Core .NET 10.

Không khởi động server trên database thật chỉ từ gói này: cần xác minh schema/migration, quyền, backup/restore và cấu hình runtime theo [VAN_HANH_VA_PHAT_HANH.md](../VAN_HANH_VA_PHAT_HANH.md). Server có startup bootstrap catalog và có thể ghi database.
