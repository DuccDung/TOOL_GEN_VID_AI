# Kế hoạch đóng gói FFmpeg/FFprobe cùng VideoMaker Desktop

> Ngày lập: 2026-08-28  
> Phạm vi: `TOOL-LOCAL`, `TOOL-SETUP`, `TOOL-UPDATER`, script phát hành và tài liệu vận hành.  
> Mục tiêu: người dùng cài VideoMaker là có sẵn FFmpeg/FFprobe, không phải cài riêng hoặc cấu hình `PATH`.

## Trạng thái triển khai source — 2026-08-28

Đã triển khai:

- Script `Prepare-FfmpegBundle.ps1` nhận bộ ba file đã duyệt, tạo `PROVENANCE.md`, `checksums.sha256` và từ chối FFmpeg/FFprobe khác phiên bản.
- Script publish xác minh SHA-256, chạy `-version`, đóng đủ năm file vào `tools/ffmpeg` và quản lý chúng trong `update-manifest.json`.
- Server nhận release kiểm tra manifest/package có đủ hồ sơ FFmpeg.
- Luồng desktop update, installer và updater dùng chung validator SHA-256 trước khi thay file.
- Updater giữ rollback hiện có; `appsettings.json` và `appsettings.user.json` tiếp tục được bảo toàn.
- Desktop preflight từ chối bộ FFmpeg/FFprobe khác phiên bản với mã `media_tool_version_mismatch`.
- Bundle có phạm vi `Development` được dùng cho máy dev nhưng bị chặn ở publish/package; release bắt buộc `Approval scope: Release`.
- UI có luồng **Cài bộ xử lý video** dùng package VideoMaker đúng version/build và Updater hiện có để sửa chữa có rollback.

Đã chuẩn bị cho môi trường phát triển:

- Gyan FFmpeg `9.0.1-essentials_build-www.gyan.dev`, static `win-x64`, GPLv3, có `libx264`, AAC và filter `subtitles`.
- Archive nguồn khớp SHA-256 do nhà phân phối công bố.
- Binary thật đã được tạo profile `Development` tại `third_party/ffmpeg/win-x64` bằng script chuẩn.

Chưa thể hoàn tất chỉ bằng source code:

- Owner sản phẩm chưa phê duyệt nghĩa vụ phân phối GPL cho release.
- Bundle chưa được nâng phạm vi từ `Development` lên `Release`.
- Chạy publish release thật, ký artifact và QA cài mới/cập nhật/rollback trên máy Windows sạch.

## 1. Kết quả mong muốn

Bản phát hành Windows `win-x64` phải chứa đầy đủ:

```text
tools/ffmpeg/ffmpeg.exe
tools/ffmpeg/ffprobe.exe
tools/ffmpeg/LICENSE.txt
tools/ffmpeg/PROVENANCE.md
tools/ffmpeg/checksums.sha256
```

Desktop ưu tiên sử dụng bộ công cụ này. Nếu package thiếu hoặc sai, lỗi phải được phát hiện trong quá trình publish/cài đặt/cập nhật, không chờ đến lúc người dùng đã phát sinh request Kling.

Hệ thống không tự tải FFmpeg từ Internet trong lúc desktop đang chạy. Việc chọn nguồn, kiểm tra license, phiên bản và checksum là trách nhiệm của quy trình phát hành.

## 2. Trạng thái source hiện tại

Các phần sau đã có trong source và phải được giữ nguyên khi triển khai:

- Desktop resolve FFmpeg/FFprobe từ `tools/ffmpeg`, hỗ trợ cấu hình cục bộ và fallback `PATH` cho môi trường phát triển.
- Desktop chạy preflight trước outbound Kling và trả mã lỗi ổn định khi thiếu hoặc không chạy được media tool.
- UI hiển thị trạng thái Media, cảnh báo và nút **Kiểm tra lại**.
- Request Kling đã `Completed` được dùng lại để tiếp tục tải/probe clip; không submit và tính phí lần hai.
- Project `TOOL-LOCAL` có hook đưa bundle vào output/publish khi đủ năm file và publish bắt buộc xác minh hồ sơ bundle.
- Script publish, installer và updater có kiểm tra bundle bắt buộc.
- Repository có binary FFmpeg với hồ sơ `Development`; mọi đường phát hành phải từ chối hồ sơ này cho đến khi có phê duyệt Release.

Phần còn lại chủ yếu là chọn bộ binary hợp lệ, đưa vào quy trình phát hành, kiểm thử package thật và ban hành runbook.

## 3. Nguyên tắc bắt buộc

- Chỉ hỗ trợ `win-x64` trong phạm vi kế hoạch này.
- `ffmpeg.exe` và `ffprobe.exe` phải đến từ cùng một bản phân phối và cùng phiên bản.
- Không đổi tên một file không phải FFmpeg thành `ffmpeg.exe`/`ffprobe.exe` để vượt qua bước kiểm tra.
- Không commit binary trước khi chủ sở hữu sản phẩm xác nhận nguồn và nghĩa vụ license.
- Không tải executable trực tiếp khi desktop đang chạy.
- Không yêu cầu người dùng cuối sửa `PATH`.
- Không gọi Kling nếu preflight media tool chưa đạt.
- Không xóa request/usage Kling đã hoàn tất chỉ vì bước tải hoặc FFprobe cục bộ lỗi.
- Mỗi release phải lưu được nguồn, phiên bản, cấu hình build, license và SHA-256 của bundle.

## 4. Cấu trúc thư mục chuẩn

Thư mục nguồn dùng khi build/publish:

```text
third_party/
  ffmpeg/
    win-x64/
      ffmpeg.exe
      ffprobe.exe
      LICENSE.txt
      PROVENANCE.md
      checksums.sha256
```

Trong package cài đặt:

```text
VideoMaker/
  TOOL-LOCAL.exe
  appsettings.json
  tools/
    ffmpeg/
      ffmpeg.exe
      ffprobe.exe
      LICENSE.txt
      PROVENANCE.md
      checksums.sha256
```

`PROVENANCE.md` và `checksums.sha256` phục vụ audit và là thành phần bắt buộc của bản phát hành. Installer/updater dùng hồ sơ này để từ chối binary bị thay đổi sau bước chuẩn bị bundle.

## 5. Danh sách task triển khai

### Task 1 — Chọn bản phân phối FFmpeg

Chủ trì: Release Admin/Owner sản phẩm.

- Chọn nguồn phân phối đáng tin cậy hoặc quy trình tự build nội bộ.
- Chốt phiên bản FFmpeg cụ thể, không sử dụng nhãn thay đổi như `latest` trong pipeline production.
- Xác nhận binary chạy trên Windows `win-x64` mà không cần DLL ngoài package, hoặc liệt kê đầy đủ runtime dependency nếu có.
- Ghi lại URL/nguồn nhận file, ngày nhận, phiên bản và thông tin người phê duyệt.
- Ghi lại kết quả `ffmpeg -version` và `ffprobe -version`.

Đầu ra:

- Một bộ `ffmpeg.exe` và `ffprobe.exe` đã được chọn.
- Thông tin nguồn được ghi trong `PROVENANCE.md`.

### Task 2 — Rà soát license và quyền phân phối

Chủ trì: Owner sản phẩm/người phụ trách pháp lý.

- Xác định bản build đang áp dụng LGPL, GPL hoặc điều khoản bổ sung nào dựa trên cấu hình build thực tế.
- Xác định các codec/thư viện được bật và nghĩa vụ đi kèm.
- Phê duyệt việc phân phối binary cùng VideoMaker.
- Đặt nội dung license/notice cần thiết vào `LICENSE.txt` và bổ sung các notice khác nếu được yêu cầu.
- Không coi file license chung chung là đủ nếu bản build thực tế có thành phần với nghĩa vụ khác.

Điều kiện hoàn thành:

- Có xác nhận nội bộ rằng bundle được phép phân phối.
- License/notice khớp bản build đã chọn.

### Task 3 — Tạo hồ sơ checksum và nguồn gốc

Chủ trì: Release Admin.

- Tính SHA-256 riêng cho `ffmpeg.exe`, `ffprobe.exe` và `LICENSE.txt`.
- Lưu checksum trong `checksums.sha256`.
- Ghi phiên bản, nguồn, ngày kiểm tra, kiến trúc và người phê duyệt vào `PROVENANCE.md`.
- Lưu bản chuẩn trong kho artifact nội bộ có quyền truy cập giới hạn.
- Không dựa vào một đường dẫn tải công khai không cố định trong mỗi lần build.

Điều kiện hoàn thành:

- Có thể đối chiếu lại binary dùng cho một release cụ thể.
- Pipeline nhận đúng file đã phê duyệt, không nhận file trùng tên nhưng khác hash.

### Task 4 — Chuẩn hóa cách cấp bundle cho máy build

Chủ trì: DevOps/Release Admin.

- Chọn một trong hai cách:
  - Cấp bundle từ kho artifact nội bộ vào `third_party/ffmpeg/win-x64` trước khi publish; hoặc
  - Truyền thư mục bundle đã giải nén qua tham số `-FfmpegBundlePath`.
- Nếu dùng CI/CD, secret/token tải artifact chỉ tồn tại trong môi trường build, không ghi vào source hoặc log.
- Xác minh checksum trước khi gọi script publish.
- Xóa workspace tạm theo chính sách CI sau khi kết thúc job.

Điều kiện hoàn thành:

- Máy build luôn nhận cùng bundle khi build lại cùng một release.
- Build thất bại sớm nếu thiếu file hoặc sai checksum.

### Task 5 — Hoàn thiện kiểm tra tại publish

Chủ trì: Developer/DevOps.

- Giữ kiểm tra bắt buộc đối với `ffmpeg.exe`, `ffprobe.exe`, `LICENSE.txt`, `PROVENANCE.md` và `checksums.sha256`.
- Bổ sung kiểm tra checksum dựa trên hồ sơ bundle đã được phê duyệt.
- Chạy `ffmpeg -version` và `ffprobe -version` trong môi trường build để loại file hỏng hoặc sai kiến trúc.
- Kiểm tra hai executable trả về cùng dòng phiên bản mong đợi.
- Đảm bảo năm file xuất hiện dưới `tools/ffmpeg` trong output publish và `update-manifest.json`.
- Không publish release nếu một bước kiểm tra thất bại.

Điều kiện hoàn thành:

- Package không thể được tạo khi bundle thiếu, sai hash hoặc không thực thi được.

### Task 6 — Kiểm tra installer và updater

Chủ trì: Developer/QA.

- Installer từ chối package thiếu media tool hoặc manifest không quản lý các file này.
- Updater từ chối package thiếu media tool và rollback về phiên bản trước nếu sao chép/khởi động thất bại.
- `appsettings.user.json` của máy người dùng được bảo toàn khi cập nhật.
- Bản cập nhật có thể thay FFmpeg bằng phiên bản mới khi manifest và checksum hợp lệ.
- Không để lại trạng thái nửa cũ/nửa mới giữa `ffmpeg.exe` và `ffprobe.exe`.

Điều kiện hoàn thành:

- Cài mới và nâng cấp đều tạo ra một bộ media tool đồng nhất, chạy được.
- Rollback phục hồi ứng dụng trước đó khi update thất bại.

### Task 7 — Kiểm thử desktop trên máy sạch

Chủ trì: QA.

Chuẩn bị một Windows `win-x64` sạch, không có FFmpeg trong `PATH`, sau đó kiểm tra:

1. Cài VideoMaker từ installer chính thức.
2. Xác nhận `tools/ffmpeg/ffmpeg.exe`, `ffprobe.exe`, `LICENSE.txt`, `PROVENANCE.md`, `checksums.sha256` đã có.
3. Mở desktop và xác nhận trạng thái **Media · FFmpeg sẵn sàng**.
4. Bấm **Kiểm tra lại** và xác nhận cả hai version được nhận diện.
5. Tải một clip fixture nội bộ qua luồng test không phát sinh chi phí provider và xác nhận FFprobe đọc được video stream.
6. Render một video fixture ngắn bằng FFmpeg.
7. Đổi tên tạm `ffprobe.exe`, mở lại ứng dụng và xác nhận Kling bị khóa trước outbound.
8. Khôi phục file, bấm **Kiểm tra lại** và xác nhận thao tác được mở lại.
9. Với một provider request fixture ở trạng thái `Completed`, xác nhận **Tiếp tục tải clip** không tạo provider request mới.

Không chạy request Kling/OpenAI thật nếu chưa có môi trường test và phê duyệt chi phí.

### Task 8 — Kiểm thử antivirus, quyền và đường dẫn

Chủ trì: QA/IT.

- Kiểm thử ở thư mục cài đặt mặc định và đường dẫn có dấu cách/ký tự tiếng Việt.
- Kiểm thử với user Windows không có quyền admin sau khi cài đặt.
- Kiểm tra Windows Defender/antivirus không cách ly binary hợp lệ.
- Kiểm tra lỗi `media_tool_not_executable` có hướng dẫn phù hợp nếu file bị chặn.
- Không yêu cầu desktop tự nâng quyền chỉ để chạy FFmpeg.

### Task 9 — Cập nhật tài liệu vận hành và release checklist

Chủ trì: Release Admin/Developer.

- Ghi nguồn bundle, phiên bản và checksum vào hồ sơ của từng release.
- Bổ sung bước xác nhận `tools/ffmpeg` vào checklist publish.
- Ghi cách nâng phiên bản FFmpeg và cách rollback.
- Ghi rõ `appsettings.user.json` chỉ dành cho phát triển/chẩn đoán, không phải yêu cầu với người dùng cuối.
- Ghi rõ hệ thống không tự tải binary khi runtime.

### Task 10 — Phát hành theo từng giai đoạn

Chủ trì: Owner sản phẩm/Release Admin.

- Phát hành trước vào channel `Development`.
- Theo dõi lỗi preflight, lỗi antivirus, lỗi probe và lỗi render.
- Sau khi QA đạt, phát hành vào `Beta` cho nhóm nhỏ.
- Chỉ chuyển `Stable` khi cài mới, update và rollback đều đạt.
- Nếu có lỗi bundle, dừng release và phát hành package sửa lỗi; không hướng dẫn người dùng tải executable từ nguồn ngẫu nhiên.

## 6. Ma trận trách nhiệm

| Hạng mục | Owner | Developer | DevOps/Release | QA |
|---|---:|---:|---:|---:|
| Chọn nguồn và phiên bản | Phê duyệt | Tư vấn kỹ thuật | Thực hiện | Kiểm tra |
| License/quyền phân phối | Phê duyệt | Cung cấp cấu hình build | Lưu hồ sơ | Đối chiếu package |
| Resolver/preflight desktop | Theo dõi | Chịu trách nhiệm | — | Kiểm thử |
| Artifact/checksum | Phê duyệt | Hỗ trợ | Chịu trách nhiệm | Đối chiếu |
| Publish/installer/updater | Theo dõi | Chịu trách nhiệm source | Chịu trách nhiệm pipeline | Kiểm thử |
| Rollout/rollback | Quyết định | Hỗ trợ | Thực hiện | Xác nhận |

## 7. Checklist cho mỗi lần nâng FFmpeg

- [ ] Phiên bản mới đã được chốt và không dùng `latest` động.
- [ ] Nguồn tải hoặc quy trình build nội bộ đã được ghi lại.
- [ ] License và configure flags đã được rà soát lại.
- [ ] SHA-256 đã được cập nhật và phê duyệt.
- [ ] `ffmpeg -version` và `ffprobe -version` chạy thành công trên máy build.
- [ ] Hai executable thuộc cùng phiên bản/bản phân phối.
- [ ] Release package chứa đúng `tools/ffmpeg` và manifest quản lý đủ file.
- [ ] Cài mới trên Windows sạch đạt.
- [ ] Update từ bản Stable gần nhất đạt.
- [ ] Rollback khi giả lập lỗi đạt.
- [ ] Probe clip fixture và render video fixture đạt.
- [ ] Không phát hiện outbound provider ngoài dự kiến.

## 8. Tiêu chí nghiệm thu cuối cùng

Kế hoạch được coi là hoàn thành khi:

1. Người dùng cài VideoMaker trên máy Windows sạch và không phải cài FFmpeg hoặc sửa `PATH`.
2. Desktop báo Media sẵn sàng ngay lần mở đầu tiên.
3. Publish, installer và updater đều chặn bundle thiếu/sai.
4. Bundle có nguồn gốc, license, phiên bản và SHA-256 truy vết được.
5. Thiếu/hỏng media tool chặn Kling trước outbound call.
6. Sau khi công cụ được khôi phục, request Kling `Completed` tiếp tục tải được mà không tạo request/usage mới.
7. Release build đạt `0 warning`, `0 error` và toàn bộ test tự động đạt.
8. Kiểm thử cài mới, cập nhật và rollback trên máy sạch đều đạt.

## 9. Phương án rollback

- Dừng phát hành channel đang lỗi.
- Gỡ package lỗi khỏi danh sách release khả dụng trên server theo quy trình quản trị release.
- Giữ package Stable trước đó để updater/installer có thể quay lại.
- Phục hồi đồng thời cả `ffmpeg.exe`, `ffprobe.exe` và license của cùng bundle; không rollback riêng một executable.
- Giữ nguyên workspace, project, provider request và usage của người dùng.
- Sau khi sửa bundle, tăng build number và phát hành package mới; không ghi đè âm thầm artifact đã công bố.

## 10. Ngoài phạm vi

- Tự tải FFmpeg trực tiếp khi desktop đang chạy.
- Hỗ trợ macOS/Linux hoặc Windows ARM64.
- Thay đổi kiến trúc AI Gateway, credential, budget hoặc pricing.
- Chạy migration database.
- Gửi request Kling/OpenAI thật trong quá trình kiểm thử mặc định.
- Tự động chấp thuận license thay cho Owner sản phẩm.
