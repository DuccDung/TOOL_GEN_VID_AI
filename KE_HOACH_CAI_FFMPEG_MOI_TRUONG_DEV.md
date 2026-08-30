# Kế hoạch cài FFmpeg cho môi trường phát triển và tái sử dụng khi đóng gói

## 1. Mục tiêu

Chuẩn bị một bộ FFmpeg Windows x64 duy nhất, có nguồn gốc và license rõ ràng, để:

1. VideoMaker chạy được luồng tải, kiểm tra và xử lý clip Kling ngay trên máy phát triển.
2. Build desktop tự chép đúng bộ FFmpeg đó vào `tools\ffmpeg`.
3. Bộ cài và bản cập nhật sau này tái sử dụng đúng bundle đã kiểm tra, không phụ thuộc `PATH` của Windows.
4. Cảnh báo thiếu FFmpeg có thể dẫn người dùng vào luồng cài đặt/sửa chữa an toàn.

Kế hoạch này không tự động chấp thuận license, không gọi OpenAI/Kling, không chạy migration và không publish release thật.

## Trạng thái triển khai — 2026-08-28

- Đã tải Gyan FFmpeg `9.0.1-essentials_build-www.gyan.dev` từ URL phiên bản cố định và xác minh SHA-256 do nhà phân phối công bố.
- Đã xác minh Windows x64, GPLv3, `ffmpeg`/`ffprobe` cùng phiên bản và có `libx264`, AAC, filter `subtitles`.
- Đã tạo bundle thật tại `third_party\ffmpeg\win-x64` với `Approval scope: Development`; không sửa `PATH`.
- Đã bổ sung nút **Cài bộ xử lý video**, popup xác nhận và tiến trình sửa chữa bằng package VideoMaker đúng version/build.
- Đã bổ sung cổng chặn: publish, Setup và Updater chỉ nhận bundle có `Approval scope: Release`.
- Smoke test đã tạo/ghép hai cảnh và probe đầu ra H.264/AAC dài 2 giây thành công.
- Release build đạt `0 warning`, `0 error`; toàn bộ `212/212` test đạt.
- Build output đã có đủ năm file và `Test-FfmpegBundle.ps1` xác nhận đúng phiên bản/phạm vi Development.
- Chưa publish release thật và chưa coi GPL redistribution là đã được Owner sản phẩm phê duyệt.
- Còn kiểm tra thủ công UI sau khi người dùng khởi động lại ứng dụng và QA repair/cài mới trên package Release thật.

## 2. Quyết định kỹ thuật

- Không cài FFmpeg global và không sửa biến môi trường `PATH`.
- Bundle chuẩn của dự án đặt tại `third_party\ffmpeg\win-x64`.
- Chỉ dùng một bản phân phối `win-x64` đã được người có thẩm quyền duyệt nguồn và license.
- Năm file bắt buộc phải đi cùng nhau:
  - `ffmpeg.exe`
  - `ffprobe.exe`
  - `LICENSE.txt`
  - `PROVENANCE.md`
  - `checksums.sha256`
- Desktop không tự tải FFmpeg từ một URL công cộng. Bản phát hành chỉ cài/sửa chữa từ package VideoMaker đã được kiểm tra.
- Thay bundle phải thay đồng bộ cả năm file; không thay riêng `ffmpeg.exe` hoặc `ffprobe.exe`.

## 3. Hiện trạng đã có

- [x] `scripts\Prepare-FfmpegBundle.ps1` tạo hồ sơ nguồn và checksum rồi cài bundle vào thư mục đích.
- [x] `scripts\Test-FfmpegBundle.ps1` kiểm tra file, SHA-256, kiến trúc và phiên bản của hai executable.
- [x] `TOOL-LOCAL.csproj` tự chép bundle từ `third_party\ffmpeg\win-x64` vào `tools\ffmpeg` khi build/publish.
- [x] Publish, Setup, Desktop Update và Updater đã có bước từ chối bundle thiếu hoặc sai checksum.
- [x] Desktop đã có media preflight trước khi tạo request Kling.
- [x] Repository đã có bộ binary FFmpeg thật được duyệt cho phạm vi Development.
- [x] Đã thực hiện smoke test bằng clip fixture thật trên máy phát triển.
- [x] Cảnh báo thiếu FFmpeg đã có thao tác cài đặt/sửa chữa bằng package VideoMaker hợp lệ.

## 4. Thông tin cần chốt trước khi tải

Owner sản phẩm hoặc người phụ trách release phải cung cấp/phê duyệt:

| Thông tin | Yêu cầu |
|---|---|
| Nguồn tải | URL chính xác hoặc mã artifact nội bộ |
| Phiên bản | Token chính xác trả về sau `ffmpeg version` |
| Kiến trúc | `win-x64` |
| Kiểu build | Phải phù hợp với codec VideoMaker thực sự sử dụng |
| License | File license đi cùng đúng bản binary |
| Người duyệt | Tên hoặc định danh người phê duyệt |
| Hồ sơ rà soát | Mã ticket/biên bản rà soát license |
| Chính sách lưu binary | Xác nhận được lưu trong source/artifact nội bộ hay chỉ trên máy release |

Không tiếp tục sang bước cài chính thức nếu các thông tin trên còn là giá trị tạm như `TODO` hoặc `TBD`.

## 5. Danh sách task triển khai

### Giai đoạn A — Thu nhận và kiểm tra nguồn

- [x] `FFDEV-01` Chọn đúng bản phân phối FFmpeg Windows x64 và ghi nhận URL/artifact nguồn.
- [x] `FFDEV-02` Tải file vào một thư mục staging ngoài repository; không chạy trực tiếp từ thư mục tải xuống.
- [x] `FFDEV-03` Kiểm tra checksum/chữ ký do nguồn phân phối công bố, nếu nguồn có cung cấp.
- [x] `FFDEV-04` Kiểm tra package nguồn có `ffmpeg.exe`, `ffprobe.exe` và license tương ứng.
- [x] `FFDEV-05` Chạy `ffmpeg -version` và `ffprobe -version`; xác nhận hai file cùng phiên bản và chạy được trên Windows x64.
- [x] `FFDEV-06` Xác nhận build có các codec/demuxer/muxer cần cho clip Kling và quy trình render hiện tại.

**Điểm dừng phê duyệt:** chỉ tiếp tục khi nguồn, phiên bản và license đã được xác nhận.

### Giai đoạn B — Tạo bundle chuẩn của dự án

- [x] `FFDEV-07` Chạy script chuẩn bị bundle với dữ liệu đã duyệt:

```powershell
.\scripts\Prepare-FfmpegBundle.ps1 `
  -SourceDirectory <thu-muc-staging-da-duyet> `
  -ExpectedVersion <phien-ban-chinh-xac> `
  -Source <URL-hoac-ma-artifact> `
  -ApprovedBy <nguoi-phe-duyet> `
  -LicenseReview <ma-ho-so-license>
```

- [x] `FFDEV-08` Xác nhận `third_party\ffmpeg\win-x64` có đủ năm file bắt buộc.
- [x] `FFDEV-09` Chạy kiểm tra độc lập:

```powershell
.\scripts\Test-FfmpegBundle.ps1 `
  -BundlePath .\third_party\ffmpeg\win-x64 `
  -ExpectedVersion <phien-ban-chinh-xac>
```

- [x] `FFDEV-10` Thử sửa một bản sao của binary để xác nhận checksum phát hiện được bundle bị can thiệp; không sửa bundle chuẩn.

### Giai đoạn C — Kích hoạt trên máy phát triển

- [x] `FFDEV-11` Build `TOOL-LOCAL` và xác nhận năm file được chép vào `bin\Release\net10.0-windows\win-x64\tools\ffmpeg`.
- [ ] `FFDEV-12` Mở VideoMaker, chạy media preflight và xác nhận không còn cảnh báo thiếu FFmpeg.
- [x] `FFDEV-13` Probe một clip fixture cục bộ hợp lệ.
- [x] `FFDEV-14` Chạy một ca ghép/render fixture không dùng provider thật và kiểm tra video đầu ra.
- [x] `FFDEV-15` Xác nhận build và smoke test dùng đường dẫn bundle cục bộ, không phụ thuộc `PATH` hiện tại. Việc mở lại UI do người dùng xác nhận sau khi restart.

Không cần tạo `appsettings.user.json` nếu bundle chuẩn đã nằm trong repository. File cấu hình user chỉ dùng khi một lập trình viên cần trỏ tạm tới bundle khác đã được duyệt.

### Giai đoạn D — Nút cài đặt/sửa chữa tại cảnh báo thiếu FFmpeg

- [x] `FFDEV-16` Đổi cảnh báo thành hai thao tác rõ ràng: **Cài bộ xử lý video** và **Kiểm tra lại**.
- [x] `FFDEV-17` Thêm popup overlay mô tả mục đích, dung lượng, phiên bản và nguồn package VideoMaker sẽ dùng.
- [x] `FFDEV-18` Xây dựng lệnh cài/sửa chữa qua Setup/Updater; desktop không tự ghi trực tiếp vào thư mục cài đặt đang chạy.
- [x] `FFDEV-19` Chỉ lấy bundle từ package VideoMaker đúng version/build đã có manifest/checksum hợp lệ; không tải FFmpeg trực tiếp từ website bên thứ ba ở runtime.
- [x] `FFDEV-20` Dùng cơ chế staging, kiểm tra đủ năm file, backup và rollback hiện có của Updater.
- [x] `FFDEV-21` Sau khi cài thành công, Updater khởi động lại VideoMaker để media preflight chạy với bundle mới.
- [x] `FFDEV-22` Nếu server không có package cùng version/build, hiển thị hướng dẫn cài lại bản VideoMaker đầy đủ hoặc liên hệ quản trị viên.

Trạng thái giao diện cần hỗ trợ:

- Chưa cài: `Cài bộ xử lý video`.
- Đang xử lý: `Đang cài đặt...`, khóa thao tác lặp.
- Thành công: `Đã cài đặt`, sau đó tự kiểm tra lại.
- Bundle hỏng: `Sửa chữa bộ xử lý video`.
- Không có nguồn sửa chữa hợp lệ: không cố tải tùy ý; hiển thị hướng dẫn rõ ràng.

### Giai đoạn E — Tự động hóa kiểm thử

- [ ] `FFDEV-23` Test media preflight khi đủ bundle.
- [ ] `FFDEV-24` Test các trường hợp thiếu từng file, checksum sai, sai kiến trúc và lệch phiên bản.
- [ ] `FFDEV-25` Test cài/sửa chữa thành công và tự chạy lại preflight.
- [ ] `FFDEV-26` Test rollback khi quá trình thay thế bị lỗi giữa chừng.
- [ ] `FFDEV-27` Test không cho phép đường dẫn thoát khỏi thư mục staging/đích dự kiến.
- [ ] `FFDEV-28` Test thao tác lặp/idempotent: bấm cài lại với cùng bundle không làm hỏng bản đang chạy.
- [ ] `FFDEV-29` Xác nhận không phát sinh outbound OpenAI/Kling trong toàn bộ test media.

### Giai đoạn F — Sẵn sàng đóng gói

- [ ] `FFDEV-30` Publish thử vào thư mục artifacts phát triển bằng `RequireMediaToolBundle=true`.
- [ ] `FFDEV-31` Mở package và xác nhận đủ năm file dưới `tools/ffmpeg` và trong `update-manifest.json`.
- [ ] `FFDEV-32` Kiểm tra Setup cài mới trên máy Windows sạch không có FFmpeg trong `PATH`.
- [ ] `FFDEV-33` Kiểm tra update và rollback giữ nguyên một bundle đồng nhất.
- [ ] `FFDEV-34` Chỉ publish release thật sau khi người dùng chỉ rõ môi trường, version/build/channel và cho phép tác động phát hành.

## 6. Kiểm tra bắt buộc sau khi thay đổi source

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Yêu cầu kết quả:

- Restore thành công.
- Release build `0 warning`, `0 error`.
- Toàn bộ test đạt.
- `Test-FfmpegBundle.ps1` đạt với bundle thật.
- Media preflight, probe và render fixture đạt mà không cần sửa `PATH`.

## 7. Tiêu chí nghiệm thu

1. Máy phát triển chạy được VideoMaker với FFmpeg từ `tools\ffmpeg`, kể cả khi Windows không có FFmpeg global.
2. Bundle trong `third_party\ffmpeg\win-x64` truy vết được nguồn, phiên bản, người duyệt, license và SHA-256.
3. Build và publish dùng cùng một bundle; không có thao tác copy tay sau build.
4. Bundle thiếu, sai hash hoặc lệch phiên bản bị chặn trước khi xử lý clip hoặc gửi request Kling mới.
5. Nút cài/sửa chữa chỉ sử dụng package VideoMaker hợp lệ, có rollback và tự kiểm tra lại sau khi hoàn tất.
6. Người dùng cài bản phát hành sau này không phải cài FFmpeg riêng hoặc cấu hình `PATH`.

## 8. Rollback

- Nếu bundle mới không đạt, khôi phục đồng bộ năm file của bundle đã duyệt trước đó.
- Không xóa workspace, project, clip đã tải hoặc provider request.
- Nếu lỗi chỉ xảy ra trong bản dev, xóa output build và build lại từ bundle chuẩn; không sửa binary trong `bin` bằng tay.
- Nếu lỗi đã vào package phát hành, dừng phát hành package đó và tạo build number mới sau khi sửa; không ghi đè artifact đã công bố.

## 9. Ngoài phạm vi

- Cài FFmpeg global qua Chocolatey, Winget hoặc sửa `PATH` hệ thống.
- Tự chọn hoặc tự chấp thuận license thay cho Owner sản phẩm.
- Desktop tải trực tiếp FFmpeg từ nguồn công cộng.
- Chạy migration database, rotate credential hoặc gọi provider có chi phí.
- Publish release Production khi chưa có phê duyệt riêng.
