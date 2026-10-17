# Nghiệp vụ tải Setup và phát hành ứng dụng desktop

Cập nhật ngày **2026-10-17**. Đối chiếu source trên nhánh `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b` cùng các thay đổi chưa commit của workspace. Luồng hiện hành là **StandaloneCatalog**, dùng Setup **1.3.0 trở lên**.

Người dùng đã xác nhận trong phiên làm việc: **“nghiệp vụ setup tải về đã oke”**. Tài liệu ghi nhận xác nhận đó cho luồng tải Setup; không tự suy ra đã nghiệm thu toàn bộ model trên Windows sạch hoặc mọi trường hợp cập nhật/sửa chữa.

## 1. Mục tiêu và vai trò

Admin quản lý link tải và phiên bản ứng dụng. Người dùng tải một Setup dùng chung; Setup lấy thông tin phát hành từ server rồi tải, kiểm tra và cài ứng dụng/model từ VPS.

| Thành phần | Trách nhiệm |
| --- | --- |
| Admin | Lưu link EXE Setup toàn hệ thống; tạo release bằng phiên bản và link ZIP ứng dụng; phát hành hoặc ngừng phân phối. |
| Website / TOOL-SERVER trên hosting | Lưu thông tin vào database, kiểm quyền/định dạng, cung cấp catalog và chuyển hướng nút tải Windows tới VPS. |
| VPS | Giữ và phục vụ trực tiếp EXE Setup, ZIP ứng dụng và ZIP model qua HTTPS. |
| Setup trên máy người dùng | Đọc bản đã phát hành, tải file, kiểm tính toàn vẹn/tương thích, cài ứng dụng và chuẩn bị/probe model. |
| Tool-local | Sử dụng bản đã cài, giữ gate thành phần bắt buộc, kiểm tra phiên bản mới và chuyển giao cập nhật/sửa chữa cho Setup khi đủ điều kiện. |

Hosting không nhận EXE, không tải ZIP/EXE để kiểm file và không yêu cầu đăng ký hoặc Active Setup cho luồng này. Lưu/phát hành thành công xác nhận metadata hợp lệ; việc kiểm bytes thực được thực hiện trên máy cài.

## 2. Ba loại file cần phân biệt

| Loại | Ví dụ hiện tại | Cách sử dụng |
| --- | --- | --- |
| Setup `.exe` | Setup 1.3.0 | Lưu URL tại **Link tải Setup cho Windows**. Một Setup dùng cho nhiều ZIP tương thích. |
| ZIP tool-local | Ứng dụng 1.0.3, build 4 | Lưu URL trong **Tạo phiên bản** cùng phiên bản `1.0.3`. Mỗi release trỏ đúng một ZIP bất biến. |
| ZIP model | `VideoMaker-Models-20261016-082700.zip` | Setup/bản ứng dụng dùng bundle đã pin; không nhập link model vào form release. |

Phiên bản Setup và phiên bản tool-local độc lập: **Setup 1.3.0 cài tool-local 1.0.3** là hợp lệ. Ô **Phiên bản** trong form release là phiên bản ứng dụng, không phải phiên bản Setup.

Thư mục VPS đang dùng:

```text
/srv/videomaker-models/models-20261016-03/
```

URL gốc phục vụ các file:

```text
https://103.179.190.182:8443/models/
```

Upload nguyên EXE và ZIP ứng dụng, giữ nguyên tên. Không cần giải nén ZIP ứng dụng trên VPS. Nếu phát hành ứng dụng mới vẫn dùng cùng bundle model thì giữ nguyên bộ model hiện có.

## 3. Cấu hình nút “Tải cho Windows”

Admin mở **Phát hành desktop → Link tải Setup cho Windows**, nhập link HTTPS trực tiếp tới EXE rồi bấm **Lưu**. Ví dụ:

```text
https://103.179.190.182:8443/models/Setup-1.3.0-win-x64-415bf5a3d3e6fa3de927b532c9cf8825edd874966279c727c6e2c34d1409167b.exe
```

Server lưu URL trong `auth.ServerSettings`, key `Desktop.SetupDownload`. Khi người dùng bấm **Tải cho Windows**, website chuyển hướng trình duyệt tới URL đã lưu; trình duyệt tải EXE trực tiếp từ VPS. Thay link này không tạo release ứng dụng và không tự sửa các ZIP đã phát hành.

Ô cấu hình chỉ cần link, không yêu cầu nhập thêm tên file/checksum. Tuy vậy, nên giữ tên EXE do công cụ đóng gói tạo để tool-local nhận được danh tính Setup và kiểm hash trước khi tự chạy nó cho cập nhật/sửa chữa. Link EXE thông thường không có hash vẫn dùng được cho nút tải website, nhưng không đủ cho cơ chế tự tải rồi thực thi Setup đã kiểm chứng.

URL phải thuộc nguồn VPS cho phép và truy cập được từ máy người dùng. Lưu link không chứng minh file đã tồn tại hoặc chứng chỉ HTTPS trên VPS đã hợp lệ.

## 4. Tạo release ứng dụng

Form **Tạo phiên bản** có các trường:

| Trường | Bắt buộc | Quy tắc |
| --- | --- | --- |
| Phiên bản | Có | Dạng `major.minor.patch`, ví dụ `1.0.3`; phải khớp phiên bản trong tên ZIP và binary ứng dụng. |
| Link ZIP ứng dụng trên VPS | Có | Link HTTPS trực tiếp tới ZIP do Publish/đóng gói tạo, trong nguồn cho phép; tên chứa phiên bản, build và SHA-256. |
| Ghi chú phát hành | Không | Mô tả thay đổi; có thể để trống. |
| Bắt buộc cập nhật | Không | Cờ chính sách cập nhật của release; không tự cài ứng dụng chỉ vì được chọn. |

Ví dụ cho gói đã bàn giao:

```text
Phiên bản: 1.0.3

Link ZIP:
https://103.179.190.182:8443/models/VideoMaker-1.0.3-4-win-x64-47d93433e839db6dedbb349643aea62aeb237a61ce40f7dc7521e1201e2324b8.zip
```

Upload file trước, sau đó nhập thông tin → **Lưu nháp → Phát hành**. Khi đang thử nghiệm có thể để trống ghi chú và bỏ chọn bắt buộc cập nhật.

Admin không phải nhập riêng build/checksum hoặc nạp JSON. Công cụ đóng gói tính checksum trên file thực và đặt tên:

```text
ZIP:   VideoMaker-<version>-<build>-win-x64-<sha256>.zip
Setup: Setup-<version>-win-x64-<sha256>.exe
```

Server đọc build/hash khai báo từ tên ZIP, kiểm tra phiên bản khớp ô nhập và lưu metadata. Server không tính lại checksum bằng cách tải file từ VPS. Setup tải bytes thực rồi đối chiếu hash; đổi tên tùy ý hoặc gắn hash không đúng sẽ làm cài đặt thất bại.

## 5. Trạng thái và quy tắc release

| Trạng thái | Hành vi |
| --- | --- |
| Bản nháp | Đã lưu trên server; có thể sửa phiên bản/link; chưa xuất hiện trong danh sách để Setup cài. |
| Đã phát hành, đang phân phối | Có thể được Setup tìm thấy khi đúng API/profile/kênh/nền tảng. |
| Đã ngừng phân phối | Không còn được trả trong catalog công khai; không tự gỡ ứng dụng đã cài trên máy người dùng. |

Sau lần phát hành đầu, phiên bản và link ZIP bị khóa. Có thể sửa ghi chú/chính sách bắt buộc cập nhật theo quyền hiện hành; muốn thay nội dung ứng dụng phải tạo bản mới với phiên bản cao hơn. Không ghi đè ZIP tại URL của release đã phát hành.

Hệ thống kiểm revision để tránh ghi đè thay đổi đồng thời. Khi báo thông tin vừa thay đổi, tải lại bản ghi rồi thao tác tiếp.

## 6. Người dùng tải và chạy Setup

```text
Website → tải EXE từ VPS → chạy Setup
Setup → đọc danh sách release đã phát hành từ server
Setup → tải ZIP ứng dụng và bộ model từ VPS
Setup → kiểm tra, cài, probe thành phần → hoàn tất
```

1. Người dùng bấm **Tải cho Windows**, tải và mở Setup mới.
2. Setup kết nối API `https://taphoatool.com/`, lấy các release đang phân phối và tương thích; hiển thị phiên bản để cài.
3. Người dùng chọn phiên bản, thư mục cài và các tùy chọn shortcut/chạy sau cài.
4. Setup tải ZIP ứng dụng qua file tạm; hỗ trợ tải tiếp khi trạng thái file/máy chủ cho phép. Không dùng file tải dở làm ứng dụng đã cài.
5. Kiểm hash toàn ZIP, cấu trúc, phiên bản/build native và cấu hình API/profile/chế độ model. ZIP được nhận ở gốc hoặc có một thư mục bọc; dùng ZIP Publish thông thường, không tự thêm ba JSON phát hành. Setup tạo metadata cài đặt tại máy cài.
6. Kiểm các thành phần đi kèm: FFmpeg/FFprobe, OCR Anh/Trung và WebView2. Setup có WebView2 offline được duyệt để chuẩn bị khi máy thiếu.
7. Tải/kiểm bundle model đã pin, chuẩn bị Qwen 4B và Piper offline; probe thành phần bắt buộc trên chính máy đang cài. Cache hợp lệ có thể được tái sử dụng nhưng không thay cho probe READY.
8. Kiểm lại snapshot release trước khi hoàn tất. Nếu release bị thu hồi hoặc danh tính thay đổi, dừng an toàn thay vì commit bản không còn được duyệt.
9. Chỉ báo hoàn tất sau khi giao dịch cài đặt và các kiểm tra bắt buộc đạt. Nếu lỗi, giữ cơ chế journal/cache/rollback để thử lại hoặc sửa chữa; không báo READY chỉ vì tải xong.

Đăng nhập, license và quyền sử dụng công cụ vẫn theo nghiệp vụ hiện hành sau cài; tải được Setup không thay việc cấp quyền sử dụng.

## 7. Bộ model và điều kiện sẵn sàng

Bundle hiện tại là `models-20261016-03`. Địa chỉ archive, dung lượng và hash được pin trong source/binary tương thích, không lấy tùy ý từ trường URL ZIP ứng dụng trong Admin.

```text
https://103.179.190.182:8443/models/VideoMaker-Models-20261016-082700.zip
```

FFmpeg và OCR/runtime đồng bộ đi cùng gói ứng dụng; bundle model cung cấp đầu vào Qwen/Piper tương ứng. Không chép môi trường Python đã cài hoặc marker READY từ máy phát triển sang máy khách để coi là đã cài đạt.

`READY` nghĩa là thành phần thực trên máy đích đã được kiểm tra thành công. Thiếu file, sai hash, thiếu runtime hoặc probe thất bại phải hiển thị lỗi/thử lại; modal React và host C# tiếp tục chặn nghiệp vụ cần thành phần đó. Tải xong ZIP model không tự mở khóa công cụ.

Phát hành bản ứng dụng mới dùng cùng bundle không cần upload model lại. Thay bộ model/URL/hash pin là một thay đổi phát hành riêng, cần binary Setup/ứng dụng tương thích và nghiệm thu tương ứng.

## 8. Cập nhật, sửa chữa và tái sử dụng Setup

- Tool-local hỗ trợ catalog mới so phiên bản đang chạy với bản mới nhất đang phân phối; ví dụ `1.0.3 → 1.0.4` thì có thể báo cập nhật. Chỉ tăng build trong cùng phiên bản không thay quy tắc so phiên bản hiện hành.
- Khi đủ danh tính Setup, tool-local tải EXE từ link toàn hệ thống, kiểm hash và cấu hình native trước khi chuyển giao cài đặt. Không tự chạy EXE chưa kiểm chứng. Link website và catalog release là hai thông tin riêng, đều cần đúng cho trải nghiệm đầy đủ.
- Sửa chữa tìm đúng release theo phiên bản/build và checksum của package đang cài, không tự thay bằng bản mới nhất. Giữ file/release còn cần cho sửa chữa; bản bị ngừng phân phối không còn được trả qua catalog để sửa chữa theo đường đó.
- Setup dùng chung không gắn cứng vào một ZIP cụ thể. Có thể tiếp tục dùng cùng EXE khi API, profile, giao thức, bundle model và khả năng hỗ trợ vẫn tương thích.
- Setup 1.2.0 không hỗ trợ catalog 5. Đổi tên EXE không bổ sung khả năng này; cần dùng Setup 1.3.0 trở lên.
- ZIP ứng dụng cũ chỉ dùng lại được khi qua đủ kiểm tra cấu trúc, cấu hình, version/build và hash. Không cấm tuyệt đối ứng dụng 1.0.1, nhưng binary cũ không tự có cơ chế cập nhật catalog 5. Lượt chuyển đổi để có đầy đủ cơ chế mới dùng Setup mới và bản tool-local tương thích.

## 9. Các tình huống thường gặp

| Tình huống | Cách xử lý nghiệp vụ |
| --- | --- |
| Website không tải được Setup | Kiểm link EXE toàn hệ thống, file trên VPS, quyền đọc và HTTPS. Không nhập link ZIP/model vào ô link Setup. |
| Form từ chối link ZIP | Kiểm tên có version/build/SHA-256, phiên bản khớp ô nhập và nguồn URL được phép. Không dùng đường dẫn placeholder trong ô nhập. |
| Lưu/phát hành được nhưng Setup tải thất bại | Kiểm file thực và kết nối từ máy cài. Server chỉ lưu metadata nên lưu thành công không chứng nhận VPS/file đã đạt. |
| Setup không thấy phiên bản | Kiểm đã phát hành/đang phân phối, server đã deploy đúng luồng và Setup đúng API/profile/phiên bản hỗ trợ. |
| Mất mạng khi tải | Thử lại bằng Setup; dùng cache/tải tiếp nếu điều kiện còn hợp lệ. Không đổi tên hoặc thay nội dung file đã phát hành để né lỗi. |
| Sai hash hoặc sai version trong ZIP | Dừng cài; đối chiếu file đã upload với gói đóng gói. Không bỏ kiểm tra hoặc đổi nhãn phiên bản để chạy binary khác. |
| Model chưa sẵn sàng | Chạy tiếp/sửa chữa bằng Setup và xem kết quả từng thành phần. Không sửa marker để ép READY. |
| HTTP 403 từ hosting | Kiểm quyền/request/log môi trường đích. Kết quả local hoặc bản ghi đã lưu không tự chứng minh nguyên nhân 403 đã được khắc phục. |

## 10. Phát hành lần tiếp theo

1. Chốt phiên bản ứng dụng cao hơn, Publish/đóng gói ZIP từ source đã duyệt.
2. Upload nguyên ZIP mới lên VPS, giữ tên chứa version/build/hash. Giữ Setup và bundle model nếu vẫn tương thích.
3. Trong Admin nhập phiên bản và URL ZIP, ghi chú/chính sách nếu cần; lưu nháp rồi phát hành.
4. Thử tải/cài bản mới và kiểm tra thông báo cập nhật trên môi trường phù hợp. Không thay bytes của bản đã phát hành.

Không cần chạy SQL mỗi khi thêm release. Database hosting `tap51004_tool` đã áp migration 4.1.21 lúc **2026-10-17 09:49:14 UTC**, có hậu kiểm bằng kết nối mới. Môi trường khác vẫn phải kiểm schema và cấu hình trước triển khai.

## 11. Nguồn đối chiếu và phạm vi xác nhận

| Nội dung | Trạng thái / nguồn |
| --- | --- |
| Luồng tải Setup hiện tại | Người dùng xác nhận đã ổn trong phiên làm việc ngày 2026-10-17; không có biên bản máy/ảnh log cài đặt kèm xác nhận này. |
| Quy tắc luồng cài, update/repair | Đối chiếu source hiện hành; xem các file dưới đây. |
| SQL hosting 4.1.21 | Đã chạy và hậu kiểm độc lập ở lượt trước; xem [biên bản SQL](TRIEN_KHAI_SQL_4_1_21_HOSTING.md). |
| Build Setup 1.3.0 và ZIP ứng dụng 1.0.3 | Đã đóng gói/kiểm file ở các lượt trước; [runbook và bằng chứng](TRIEN_KHAI_PHAT_HANH_DON_GIAN_VPS.md). |
| Full Setup/model trên Windows sạch và ma trận sự cố | Xác nhận tải Setup của người dùng không thay nghiệm thu này. Chưa có kết quả mới trong lượt viết tài liệu. |

Source chính:

- [DesktopDownloadSettingsService.cs](TOOL-SERVER/Updates/DesktopDownloadSettingsService.cs), [LauncherDistributionController.cs](TOOL-SERVER/Controllers/LauncherDistributionController.cs): lưu link và chuyển hướng tải EXE.
- [DesktopStandaloneCatalogService.cs](TOOL-SERVER/Updates/DesktopStandaloneCatalogService.cs): lưu nháp/phát hành/thu hồi, kiểm metadata và tính bất biến.
- [DesktopStandaloneCatalogContracts.cs](TOOL-SHARED.Contracts/Updates/DesktopStandaloneCatalogContracts.cs): catalog 5, Setup tối thiểu 1.3.0.
- [LauncherInstallerService.Standalone.cs](TOOL-SETUP/LauncherInstallerService.Standalone.cs), [LauncherInstallerService.Static.cs](TOOL-SETUP/LauncherInstallerService.Static.cs): đọc catalog, cài, revalidate, transaction/model.
- [DesktopStandalonePackageResolver.cs](TOOL-DISTRIBUTION/Updates/DesktopStandalonePackageResolver.cs): hash/layout/native version và metadata tại máy cài.
- [ApprovedStaticModelBundle.cs](TOOL-DISTRIBUTION/StaticModels/ApprovedStaticModelBundle.cs): bundle model/URL/hash được pin.
- [DesktopStandaloneUpdatesController.cs](TOOL-SERVER/Controllers/DesktopStandaloneUpdatesController.cs), [DesktopUpdateApiClient.Standalone.cs](TOOL-LOCAL/Updates/DesktopUpdateApiClient.Standalone.cs): cập nhật bản mới hơn và sửa chữa đúng phiên bản/build.
- [Publish-SharedDesktopSetup.ps1](scripts/Publish-SharedDesktopSetup.ps1): đóng gói Setup dùng chung và tên file có checksum.

Lượt cập nhật này chỉ viết tài liệu và kiểm tra nội dung/liên kết/whitespace. Không chạy mới build/test ứng dụng, probe model, tải lại file VPS, deploy, ghi database hoặc phát hành release.
