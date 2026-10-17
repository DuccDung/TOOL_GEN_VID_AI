# Bộ bàn giao server và dữ liệu model VideoMaker

Bộ này chứa server IIS Windows x64 cùng Qwen 4B, Piper offline và đầu vào build OCR. Đây là gói để người vận hành triển khai; chưa kích hoạt website, truy cập database hoặc chứng nhận production. Xem `package-status.json` và kết quả kiểm tra bên ngoài ZIP.

## Cấu trúc

- `server/`: DLL server, Web Admin, `web.config`, cấu hình đã làm sạch và runtime .NET/ASP.NET Core riêng.
- `models/{{HANDOVER_CANDIDATE}}/`: nguyên bộ bàn giao đã kiểm file, gồm inventory/checksums/license/provenance và ba artifact Qwen/Piper cùng năm package OCR. Không giải nén Piper ZIP để copy môi trường Python/READY từ máy đóng gói.
- `tools/model-publisher/`: công cụ offline chuẩn bị và ký catalog trên máy vận hành.
- `deployment/`: hướng dẫn, trạng thái, cấu hình mẫu tắt kho và hash source hiện hành.
- `package-inventory.json`: checksum từng file của toàn bộ bộ bàn giao; không phải chữ ký nhà phát hành.

Inventory gốc của model có SHA-256 `{{INVENTORY_SHA256}}`. Giữ nguyên byte của các file checksum/license và hồ sơ Staging. Không đổi nhãn RuntimeQualified/Published trong candidate để thay nghiệm thu thật.

## Triển khai phần server

Giải nén vào thư mục staging mới có đường dẫn ngắn, xác minh SHA-256 ZIP và inventory trước khi thay file. Đặt IIS application tại **thư mục `server`**, giữ `models`, `tools` và `deployment` ngoài website. Bộ này có private .NET/ASP.NET runtime; IIS vẫn cần ASP.NET Core Module V2/Hosting Bundle phù hợp.

Cấu hình gói không có credential thật: connection string, JWT signing secret và cấu hình tài khoản gửi mail đã được làm sạch. Trước chạy, người vận hành giữ/cấp cấu hình đích bằng cơ chế secret của môi trường; không đưa secret vào ZIP hoặc chat. Giữ nguyên Data Protection key ring, dữ liệu ứng dụng và cấu hình đang vận hành, không ghi đè bằng file mẫu rỗng.

Binary là source checkout hiện hành kèm thay đổi chưa commit. Trước startup phải kiểm schema/migration hiện hành của đúng database, backup và khả năng restore. Source bao gồm thay đổi Local cá nhân 4.1.16 cùng các module hiện hành; chỉ build thành công không xác nhận database đích đã tương thích. Startup bootstrap có thể ghi dữ liệu: không chạy server chỉ để thử kết nối trên database chưa được cho phép. Gói không tự chạy migration hoặc seed.

## Chuẩn bị kho model phục vụ Setup

**ModelDistribution.Enabled=false. Thư mục `models` là bộ nguyên liệu đã kiểm, chưa phải kho tải có catalog ký.** Gói không có khóa ký riêng, catalog đã ký hoặc server pin phát hành. Chỉ đưa GGUF/ZIP lên website chưa làm Setup tải được.

Trên máy vận hành, dùng publisher với khóa ECDSA P-256 được cấp riêng ngoài repository, bộ bàn giao và output. Có thể dùng runtime đi kèm chạy `tools/model-publisher/TOOL-MODEL-PUBLISHER.dll` qua `server/runtime/dotnet.exe`; chạy `--help` để xem tham số. Không tạo, chuyển hoặc giữ private key trên web server.

Publisher `stage` nhận handover nói trên và inventory pin, tạo **thư mục kho mới**, có `artifacts/`, `metadata/<channel>/catalog.json`, public root, sample config và operation records. Candidate hiện có compatibility Staging/desktop 1.0.0 build 1; cần chốt channel/version/build cho bài Setup cuối. Pipeline desktop nhận Development/Stable/Beta. Nếu cần compatibility mới, tạo candidate mới từ source đã chốt, không sửa bộ cũ tại chỗ. Stable/Beta yêu cầu qualification ngoài gói, không có công tắc bỏ qua.

Chuyển kho đã ký vào vùng dữ liệu ngoài `server/wwwroot`; web process chỉ có quyền đọc. Kiểm hash lại ở chính server rồi cấu hình StorageRoot, repository/channel/platform, public key, pin **toàn bộ envelope catalog** và MinimumSequence đồng bộ. Chỉ sau đó mới bật Enabled và recycle trong phạm vi môi trường được chỉ định. Không tự bật từ file mẫu, không format lại catalog đã ký.

Khóa công khai tương ứng phải được nhúng vào bản desktop/Setup có mode tải Server, với URL HTTPS và channel tương thích. Bản desktop cũ có trust root rỗng không tự tin một khóa lấy từ máy chủ tải model. OCR vẫn đi cùng package desktop tương thích; các nupkg OCR trong handover là đầu vào build, không phải gói runtime tải riêng của catalog V1.

## Nghiệm thu sau chuyển

Kiểm HTTPS qua domain thật, catalog/signature/pin/expiry, HEAD/Range/ETag, tải đầy đủ Qwen trên 2 GiB, hủy/tải tiếp và SHA-256 file cuối. Sau đó mới nghiệm thu Setup trên máy khách, runtime Qwen/Piper/OCR và workflow. Không xóa model máy khách trước khi kho và Setup đã sẵn sàng, có kiểm kê và phương án khôi phục.

Không thay quyền truy cập dữ liệu, catalog/rate/provider, thanh toán hoặc feature production chỉ từ việc nhận được bộ ZIP. Các file đúng checksum chứng minh toàn vẹn; không thay chữ ký, quyền phân phối, readiness runtime, schema đích hoặc nghiệm thu production.
