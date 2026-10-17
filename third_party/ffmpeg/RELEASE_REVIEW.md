# Rà soát kỹ thuật FFmpeg cho gói Publish

Ngày rà soát: 2026-10-17 (Asia/Bangkok). Source VideoMaker: nhánh `main`, HEAD `f0651c918f28ca477f28875818148e9e86240b7b`, checkout có thay đổi chưa commit.

## Phạm vi và trạng thái

Người dùng yêu cầu chuẩn hóa bộ FFmpeg để Publish và kiểm tra cài đặt cục bộ. Hồ sơ `FFMPEG_RELEASE_APPROVED.json` cho phép **đóng gói kỹ thuật** đúng bộ đã kiểm, với `approvalKind=TechnicalPackaging`. Trường `Approval scope: Release` phục vụ gate đóng gói hiện có; không phải chứng nhận đã hoàn tất mọi nghĩa vụ phân phối GPL hoặc nghiệm thu sản phẩm trên máy khách.

Các trạng thái được ghi riêng:

| Hạng mục | Kết quả |
| --- | --- |
| Nguồn archive và checksum công bố | Đã đối chiếu release chính thức của Gyan |
| Hai EXE và giấy phép trong repository | Khớp byte với archive đã tải |
| FFmpeg/FFprobe cùng phiên bản | Đạt trên máy Windows hiện tại |
| Mã hóa H.264, audio AAC và bộ lọc phụ đề | Đạt với mẫu tổng hợp một giây trên máy hiện tại |
| Complete corresponding source cho binary static cùng các dependency | Chưa đủ bằng chứng |
| Hoàn tất điều kiện phân phối lại cho khách | Chưa xác minh; `redistributionQualified=false` |
| Windows sạch và luồng Setup đầy đủ | Chưa xác minh bởi lần rà soát này; `productionQualified=false` |

Giữ nguyên năm file Development dưới `win-x64/`. Helper Publish sao chép các file có hash được ghim và hồ sơ kỹ thuật mới sang thư mục artifact riêng. Không thay binary hoặc giấy phép để làm gate đạt.

## Nguồn và nhận dạng

- Release chính thức: [GyanD/codexffmpeg 9.0.1](https://github.com/GyanD/codexffmpeg/releases/tag/9.0.1), công bố `2026-08-12T11:06:14Z`.
- Asset: `ffmpeg-9.0.1-essentials_build.7z`, 34.372.199 byte, SHA-256 `49a73bdf0850092a252ac4641d922f3048d63ed113e196cc65ce1e4f7fb33e85`. Digest asset từ [GitHub API của chính release](https://api.github.com/repos/GyanD/codexffmpeg/releases/tags/9.0.1) khớp file đã tải.
- Version thực tế: `9.0.1-essentials_build-www.gyan.dev`; Windows x64; FFmpeg và FFprobe cùng version.
- Source FFmpeg mà nhà cung cấp khai báo: [commit bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa](https://github.com/FFmpeg/FFmpeg/commit/bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa).
- README nguyên bản của asset được giữ tại `review/gyan-9.0.1-essentials-win-x64/README.gyan.txt`, SHA-256 `1c8c50e4df1623673ec236bafe774f3c4ee41a1e6b0cde05e31b7d89b31efa06`.

| File gốc | Số byte | SHA-256 |
| --- | ---: | --- |
| `ffmpeg.exe` | 102856192 | `72a489eccd008c2ec2c0a5856c5c75bc3d8bbfa90166c4566865c246445e6aa3` |
| `ffprobe.exe` | 102652416 | `19202b23c0043f15ad1b7bce2344f406fd52bd6efd8f995ce02e7392a1cec52f` |
| `LICENSE.txt` | 35147 | `8ceb4b9ee5adedde47b31e975c1d90c73ad27b6b165a1dcd80c7c545eb65b903` |

Các file này không được chỉnh sửa. `LICENSE.txt` trong repository khớp file `LICENSE` của archive.

## Tích hợp và kiểm tra kỹ thuật

Desktop gọi hai EXE qua tiến trình riêng với `ProcessStartInfo.ArgumentList`; xem `TOOL-LOCAL/Media/ExternalProcessRunner.cs`. Project không liên kết thư viện FFmpeg bằng DLL trong đường gọi đã kiểm. Điều này mô tả kiến trúc thực tế, không tự quyết định giấy phép của toàn bộ sản phẩm.

Render và xuất Vietsub cần `libx264`, AAC và bộ lọc `subtitles`/libass. `ffmpeg -version` của hai EXE được đối chiếu cho thấy cấu hình static, `--enable-gpl`, `--enable-version3`, có x264 và libass, không có `--enable-nonfree`. Không thay bằng một build thiếu những tính năng này.

Mẫu kiểm tra cục bộ: nền màu 320x180/25 fps, âm thanh sine 48 kHz, ASS tổng hợp, encode `libx264` + AAC trong một giây. Lệnh kết thúc thành công; FFprobe xác nhận stream H.264, AAC và thời lượng 1 giây. Bằng chứng local: `D:\vm-ffmpeg-review\20261017-9.0.1\native-probe\qualification.json`. Đây không phải kiểm tra video thực của khách hoặc Windows sạch.

`Test-FfmpegBundle.ps1 -RequireReleaseApproval` vẫn kiểm đủ năm file, hash, version và metadata. Kết quả script không thay thế phần trạng thái phân phối còn lại dưới đây.

## Nguồn tương ứng còn thiếu

Gyan khai báo build static GPLv3; [trang build chính thức](https://www.gyan.dev/ffmpeg/builds/) cũng mô tả phạm vi này. README của đúng asset liệt kê cấu hình và nhiều version dependency, nhưng chưa tạo thành complete corresponding source:

- Có source commit của FFmpeg core, nhưng chưa thu thập source/patch/build script đầy đủ cho binary static cùng dependency.
- Một số thư viện chỉ có version chưa đủ định danh revision, ví dụ cairo `1.18.5`, libgme `0.6.6`.
- Version/patch của các phần nén và dependency bắc cầu như zlib, bzip2, lzma cùng runtime MSYS2 chưa đầy đủ trong README.
- Repository `GyanD/codexffmpeg` tại tag `9.0.1` là kho hỗ trợ/mirror, không có recipe build hoàn chỉnh trong cây đã xem.

[GPLv3, mục 1 và 6](https://www.gnu.org/licenses/gpl-3.0.html) là nguồn cần đối chiếu khi chuẩn bị corresponding source và phương thức cung cấp. [Tài liệu FFmpeg](https://ffmpeg.org/legal.html) yêu cầu chú ý cấu hình và thành phần GPL; checklist LGPL ở đó không được coi là chứng nhận cho build GPL đang dùng.

Để xác minh việc phân phối cho khách, cần lưu source/patch/build instructions phù hợp hoặc hồ sơ cung cấp nguồn tương ứng có thể kiểm chứng, cùng attribution và điều khoản phân phối. Chưa gửi thư cho nhà cung cấp, chưa đăng artifact lên VPS và chưa khẳng định phần này đã hoàn tất. Nếu chọn tự build để quản lý source chặt hơn, đó sẽ là binary mới với checksum mới và cần rà soát/nghiệm thu lại.

## Bằng chứng của phiên rà soát

Thư mục local riêng: `D:\vm-ffmpeg-review\20261017-9.0.1`. Gồm archive đã kiểm hash, README/license trích từ archive, biên nhận đối chiếu hash hai EXE (`upstream-binary-comparison.json`), metadata release/commit từ GitHub API, cùng fixture và kết quả kiểm cục bộ. Hai bản EXE trích tạm đã được dọn sau khi đối chiếu để tiết kiệm dung lượng; archive gốc vẫn được giữ. Bằng chứng này gắn với máy hiện tại; không đại diện rollout production.
