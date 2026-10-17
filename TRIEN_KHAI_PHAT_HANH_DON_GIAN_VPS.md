# Phát hành đơn giản: hosting lưu thông tin, VPS giữ file, Setup cài trên máy người dùng

Rà soát ngày 2026-10-17 trên nhánh `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b` và thay đổi chưa commit của workspace. Source hiện hành có catalog độc lập giao thức 5, dùng Setup từ 1.3.0. Biên bản kiểm thử và đường dẫn bàn giao được lưu riêng cho lượt triển khai; có source không đồng nghĩa đã deploy hosting hoặc nghiệm thu máy Windows sạch.

## Cách vận hành

1. Upload ZIP ứng dụng do Publish tạo và EXE Setup do công cụ đóng gói tạo vào VPS. Giữ nguyên tên file để Setup đọc được phiên bản, build và SHA-256 tự sinh.
2. Trong Admin, mục **Link tải Setup cho Windows**, dán link HTTPS tới EXE rồi lưu. Nút tải cho Windows sẽ chuyển hướng trình duyệt tới link đó.
3. Tạo phiên bản: nhập phiên bản và link ZIP ứng dụng, thêm ghi chú/chính sách cập nhật nếu cần, lưu nháp rồi phát hành. Không cần upload hoặc kích hoạt Setup trên hosting.
4. Người dùng chạy Setup 1.3.0 trở lên. Setup đọc catalog từ hosting, tải ZIP ứng dụng và bộ model từ VPS, xác minh và cài trên máy đó.
5. Tool-local có thể báo phiên bản mới. Để tự chuyển giao cập nhật/sửa chữa, link EXE cần giữ tên do công cụ đóng gói tạo để kiểm hash trước khi chạy. Một Setup dùng được cho nhiều ZIP tương thích về API, profile, giao thức và bộ model.

Thư mục VPS đang dùng: `/srv/videomaker-models/models-20261016-03/`, được phục vụ qua `https://103.179.190.182:8443/models/`. Bộ model `VideoMaker-Models-20261016-082700.zip` giữ nguyên; không cần đóng gói lại cho thay đổi catalog này.

## Hosting thực hiện những gì

`DesktopReleases:PublicationMode=StandaloneCatalog` chọn luồng mới. `ApiBaseUrl`, `Profile` và `AllowedExternalPrefixes` vẫn xác định đúng hệ thống và nguồn phân phối; không phụ thuộc `SharedBootstrapperEnabled` hoặc bản ghi Setup Active.

Hosting chỉ xác thực quyền Admin, kiểm tra định dạng/nguồn URL/phiên bản, lưu thông tin vào database, cung cấp catalog và chuyển hướng nút tải EXE. Hosting không tải ZIP/EXE, không đo dung lượng từ xa, không chạy kiểm tra package và không khởi động worker kiểm file của luồng cũ.

Khởi tạo dịch vụ lưu artifact cũng không tạo thư mục nữa: các API đọc trạng thái và metadata không cần vùng lưu file có quyền ghi. Nhánh upload cũ vẫn tạo thư mục đúng lúc thực sự lưu file và giữ kiểm tra đường dẫn/dung lượng/hash.

Link ZIP vẫn có dạng `VideoMaker-<version>-<build>-win-x64-<sha256>.zip`; link Setup mới có dạng `Setup-<version>-win-x64-<sha256>.exe`. Công cụ đóng gói tự tính checksum và đặt tên. Admin chỉ sao chép URL, không nhập hash hoặc JSON riêng. Phiên bản nhập phải khớp tên ZIP. Sau phát hành, URL và danh tính gói giữ cố định; thay gói phải tăng phiên bản.

Link EXE thông thường vẫn có thể lưu cho nút tải website. Nếu tên EXE thiếu hash thì catalog không khai báo danh tính bộ cài cho tự cập nhật: Setup đang chạy vẫn cài được ứng dụng, còn tool-local hướng dẫn tải Setup từ trang tải thay vì tự thực thi EXE chưa kiểm chứng.

API mới: `/api/admin/desktop-standalone-releases`, `/api/launcher-distribution/standalone/*`, `/api/desktop-updates/standalone/*`. Lưu mới, sửa bản nháp, phát hành và ngừng phân phối dùng POST; PUT cũ được giữ tương thích ở các endpoint hỗ trợ. Quyền Admin và kiểm revision không thay đổi. API cũ giao thức 3/4 vẫn giữ quy tắc kiểm tra riêng.

Nếu hosting trả HTML HTTP 403, Admin hiện mã lỗi và hướng kiểm tra quyền/cấu hình, không hiển thị nguyên trang HTML. Thêm POST không tự chứng minh nguyên nhân HTTP 403 trên hosting đã được giải quyết; cần kiểm tra request và log của môi trường đó nếu lỗi tiếp diễn.

## Kiểm tra trên máy cài

Setup 1.3.0 dùng API/profile/model được đóng trong binary, không cần `bootstrapperId` từ database. Nó kiểm lại snapshot đang phát hành trước cài và trước commit; thu hồi hoặc thay đổi snapshot làm dừng cài an toàn.

ZIP tải qua file tạm, giới hạn dung lượng, kiểm SHA-256 rồi kiểm cấu trúc và phiên bản/build native. Metadata cài đặt được tạo tại máy người dùng; không cần ba file metadata trong ZIP. ZIP có một thư mục bọc `publish/` vẫn được hỗ trợ theo bộ kiểm tra layout hiện hành.

Bộ model vẫn theo bundle đã pin: tải/khôi phục khi gián đoạn theo cơ chế hiện hành, kiểm hash từng thành phần và chạy probe Qwen/OCR/Piper/FFmpeg trước READY. Giao dịch, rollback, receipt và gate React/C# được giữ. Kiểm thử giả lập hoặc model bị skip không chứng minh runtime trên máy người dùng đã sẵn sàng.

Tự cập nhật tải EXE qua HTTPS, kiểm hash và metadata native API/profile/model/giao thức trước khi chạy. Chuyển giao giữa ứng dụng và Setup vẫn dùng SID, PID/start time, nonce và ACK; không đưa thông tin ownership này qua WebView.

## Database và thứ tự rollout

1. Kiểm tra đúng database ứng dụng, backup và khả năng restore. Nhánh hiện tại dùng bảng catalog do `VideoFactory.4.1.20.DirectDesktopCatalogSimple.sql` tạo.
2. Áp **`database/VideoFactory.4.1.21.StandaloneDesktopCatalogSimple.sql`** theo quy trình vận hành. File mới chỉ mở CHECK cho catalog 4/5 và ghi marker; không đổi quyền và không sửa migration 419/420. Nếu schema khác dự kiến, migration dừng và rollback.
3. Deploy server mới, giữ cấu hình hosting/secret đúng môi trường. Xác nhận `PublicationMode=StandaloneCatalog`; biến môi trường hoặc cấu hình hosting cũ có thể ghi đè cấu hình đóng gói.
4. Upload Setup 1.3.0 và ZIP tool-local mới, giữ nguyên tên; lưu link EXE, tạo và phát hành phiên bản từ link ZIP.
5. Thử tải website, cài mới, mở ứng dụng, kiểm READY và báo cập nhật trên môi trường kiểm tra. Setup 1.2.0 cũ không hỗ trợ catalog độc lập; cần dùng EXE mới.

Tool-local cũ chưa biết API catalog 5 nên không tự nhận thông báo phiên bản chỉ phát hành ở luồng mới. Lượt chuyển đổi đầu cần chạy Setup 1.3.0 và cài ZIP tool-local mới; từ bản đó việc kiểm tra cập nhật/sửa chữa mới dùng catalog độc lập. Giữ API 3/4 không tự nâng khả năng của binary đã phát hành.

Không cần đổi nginx hoặc đưa model lên hosting cho thay đổi này. Chứng chỉ HTTPS, quyền đọc file và URL tải trực tiếp trên VPS cần hoạt động ở máy người dùng.

## Lệnh đóng gói

Publish tool-local bằng profile `FolderProfile` như hiện hành, khai báo phiên bản/build phát hành. Profile tự tạo ZIP có checksum trong tên. Tạo Setup dùng chung bằng `scripts/Publish-SharedDesktopSetup.ps1` với `BootstrapperVersion=1.3.0`, API Production, profile Production và WebView2 offline đã duyệt. Không đổi tên EXE đầu ra.

Đóng gói server bằng `scripts/Publish-ToolServerIis.ps1` vào thư mục mới. Script loại secret từ cấu hình phát hành; giữ secret tại cấu hình hosting khi deploy.

## Trạng thái nghiệm thu

Trên working tree nêu đầu tài liệu, restore/build Release đạt **0 Warning / 0 Error**; lượt full cuối `full-delivery.trx` đạt **2.933 Passed / 0 Failed / 18 Skipped / 2.951 Total**. Frontend sau `npm ci`/build đạt **347/0/0**; bốn bộ browser Admin đạt **65/0/0**, có ảnh 390/1440 px. Nhóm kiểm tra tập trung Standalone/lưu file/WebView đạt **98/0/0**; nhóm lưu file và phát hành cũ sau sửa assertion đạt **42/0/0**, đều đã nằm trong full cuối. Không cộng các lượt này thành tổng test độc lập.

SQL Server 2019 QA riêng đạt **10 ca** cho migration 4.1.21, gồm chạy lặp, rollback các schema lạ, backup CHECKSUM/VERIFYONLY/restore và bảo toàn dữ liệu/quyền. Đây là baseline catalog đúng 4.1.20 cùng sentinel schema, không phải nghiệm thu toàn bộ chuỗi migration. HTTP thực với SQL2019 và đúng DLL server cuối đạt **24/24 ca**, bao gồm không có Setup registry/link, quyền Admin/Viewer, POST/PUT/CAS, chuyển hướng website và đọc metadata khi vùng lưu file bị chặn bởi sentinel file. Các host và database GUID thử nghiệm đã dọn, instance QA cũ giữ nguyên.

Lượt full đầu có hai bài không dựng được fixture WebView vì V8/Rolldown thiếu bộ nhớ. Lượt kế tiếp đã qua hai bài đó nhưng phát hiện assertion cũ còn liệt kê một thư mục nay không được tạo; assertion đã đổi sang yêu cầu thư mục không tồn tại. Lượt full cuối phía trên đạt với `NODE_OPTIONS=--max-old-space-size=512 --max-semi-space-size=4`, `RAYON_NUM_THREADS=2`, `DOTNET_GCConserveMemory=9`, TEMP/TMP trên D. Các giới hạn chỉ áp dụng tiến trình QA, không đưa vào ứng dụng phát hành; giữ log các lượt lỗi, không skip bài lỗi để đạt.

Xem biên bản của lượt đóng gói trong `D:/vm-standalone-release/20261017-155515-f30f29/`. SQL thử nghiệm tại đó chạy trên SQL Server 2019 LocalDB riêng, không phải hosting. Tại thời điểm đóng gói chưa áp migration 4.1.21 lên hosting. Theo yêu cầu tiếp theo, **đã áp exact 4.1.21 trên `MB2D70\MSSQLSERVER2019 / tap51004_tool` lúc 2026-10-17 09:49:14 UTC**, hậu kiểm độc lập đạt; không cần chạy lại SQL trên database này. [Biên bản SQL đích và giới hạn backup/restore](TRIEN_KHAI_SQL_4_1_21_HOSTING.md). Chưa deploy server, publish release thật hoặc xác nhận cài mới toàn bộ model trên Windows sạch trong lượt SQL.

## Bộ bàn giao đã đóng gói

Thư mục gốc: `D:/vm-standalone-release/20261017-155515-f30f29/delivery/`. Hướng dẫn thao tác ngắn nằm trong `BAT_DAU_O_DAY.md`.

| Gói | Đường dẫn tương đối trong thư mục bàn giao | Dung lượng byte |
| --- | --- | ---: |
| Setup dùng chung 1.3.0 | `setup/Setup-1.3.0-win-x64-415bf5a3d3e6fa3de927b532c9cf8825edd874966279c727c6e2c34d1409167b.exe` | 267537807 |
| Tool-local 1.0.3, build 4 | `application/1.0.3-4/VideoMaker-1.0.3-4-win-x64-47d93433e839db6dedbb349643aea62aeb237a61ce40f7dc7521e1201e2324b8.zip` | 485896731 |
| Server IIS, kèm runtime riêng 10.0.9 | `server/TOOL-SERVER-IIS-dll-runtime-win-x64-20261017-093110-b0e4802e.zip` | 59075778 |
| Migration mới | `database/VideoFactory.4.1.21.StandaloneDesktopCatalogSimple.sql` | Xem danh mục bàn giao |

`delivery-manifest.json` ghi đường dẫn, dung lượng và SHA-256 của các file bàn giao; đây là bằng chứng kỹ thuật, không phải dữ liệu phải nhập vào Admin. Hai file ứng dụng và Setup đã mang checksum trong tên. Gói server có SHA-256 `8fab9df00754eec07684cc4e08a1b160a53b043d76c4b4c40b68bfea362611fe`; migration 4.1.21 có SHA-256 `0b63286b5e394fe7f95770ea0ce360730af936a05eafa105d66a5994ad118ef2`.

Lượt kiểm tra HTTP bổ sung dùng đúng ba DLL từ thư mục server đã publish đạt **24/24**, có so hash DLL đang chạy và xác nhận dừng host/dọn database QA: `http-published-summary.json`, `http-published-binary-identity.json`, `http-published-shutdown-cleanup.json`. Đây là host QA cô lập, không khởi chạy Program production và không truy cập hosting.

Đã dùng chính ZIP 1.0.3/build 4 để kiểm hash, giải nén, kiểm layout/native version và tạo metadata cài cục bộ; kiểm native binding của chính EXE Setup 1.3.0 đạt. Chạy chẩn đoán trên thư mục vừa giải nén cho kết quả WebView2, FFmpeg/FFprobe và OCR Anh/Trung **READY**. Chính sách model trong binary xác nhận bundle `models-20261016-03`, archive hash đã pin, distribution 3 và profile Production. Bằng chứng: `package-proof-final.json`, `real-stage-bundled-runtime.json`, `real-stage-static-model-policy.json`.

Các kiểm tra trên không thực hiện giao dịch Setup hoàn chỉnh, không chạy Qwen/Piper và không xác nhận toàn bộ model trên máy Windows sạch. Không nâng kết quả kiểm file/chẩn đoán component thành trạng thái cài đặt production. Snapshot 1.136 file source giữ nguyên giữa lượt full test cuối và đóng gói (`source-after-packaging.json`). Các file mới chưa được upload VPS; URL trong hướng dẫn là URL cần tạo sau khi upload.
