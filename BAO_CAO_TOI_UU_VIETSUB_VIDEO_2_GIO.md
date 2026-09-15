# Triển khai tối ưu Vietsub cho video 2 tiếng

Ngày: 2026-09-13. Thay đổi trên workspace từ HEAD `a91b7e9`, chưa phát hành/cài đặt vào môi trường đang sử dụng.

Đã triển khai các đường xử lý gây nghẽn ở audio dài, truy vấn/lưu phụ đề, playback, waveform và tiến độ xuất MP4. Đã kiểm tra timeline và MP4 tổng hợp đủ 7.200 giây. **Chưa nghiệm thu độ mượt của toàn bộ OCR → Qwen → Piper → preview → export trên phim 1080p 120 phút thực tế.** Các ngưỡng UI/dropped frame/tổng RAM trong kế hoạch vẫn cần đo trên workload đó.

## Thay đổi trong source

| Phần | Hành vi sau thay đổi |
|---|---|
| WAV | Phrase vẫn bị giới hạn 32 MiB. Timeline dùng parser stream, định dạng PCM16 stereo 48 kHz và số mẫu khớp thời lượng dự kiến; giới hạn 4 giờ trong RIFF. Không cấp phát theo kích thước chunk. |
| Ghép giọng | Video dài dùng cửa sổ 120 giây, thêm 1 giây ngữ cảnh filter mỗi phía, ghép PCM theo số mẫu. Mỗi lượt FFmpeg nhận tối đa 80 input; filter dùng 2 thread. Khoảng không có giọng được ghi bằng buffer hữu hạn. |
| Chất lượng/khôi phục giọng | Tính fit trên toàn timeline, giữ tempo tối đa 1.20x, giữ tiếng tràn và WAV phrase gốc. Cache segment v2 dựa trên timing, tempo, trim/fade và hash âm thanh thật, có receipt/hash kiểm lại khi tái sử dụng. Hủy không publish timeline dở; chạy lại dùng segment hợp lệ. |
| Disk/cache | Preflight timeline và file trung gian theo số input trong cửa sổ; cache segment tối đa mục tiêu 4 GiB mỗi project, dọn phần không dùng. Cache phrase và timeline lịch sử vẫn giữ cơ chế hiện hành, không phải tổng workspace được giới hạn 4 GiB. |
| Phụ đề | Trang, bộ lọc, tổng số kết quả và summary được truy vấn ở SQLite. Giữ tìm kiếm Unicode và thứ tự cue. Timeline vẫn tối đa 500 cue/cửa sổ; editor vẫn mặc định 50 cue/trang. |
| Ghi dữ liệu | Sửa text/timing chỉ ghi cue đó và tăng revision bằng CAS. Checkpoint OCR ghi cue mới/chỉ số cần đổi cùng checkpoint trong một transaction; không ghi đè nội dung cue đã lưu. Apply dịch chỉ lấy cue hiện tại và tối đa 10 cue ngữ cảnh mỗi phía. |
| Giao diện | Đưa các truy vấn chính và tải/lưu track lớn ra khỏi UI thread. WebView dùng deferral khi mở media; hash playback có hủy và gộp request xác minh. Kiểm lại project/organization/user sau khi chạy nền. |
| Waveform/thumbnail | Waveform v2 tạo PCM tổng quan mono 2 kHz trên disk, tổng hợp theo block 32 KiB thành ảnh 8.192 × 64; không chuyển PCM vào JavaScript. Thumbnail giữ hàng đợi theo viewport. Hai dịch vụ chia sẻ một slot decode nền. Ghi PNG qua stream hỗ trợ workspace có đường dẫn dài. |
| Tiến độ | Job giọng báo số đoạn đã ghép và giai đoạn xác minh, tương thích step code của job cũ. MP4 báo phần trăm từ `out_time_us` của FFmpeg và bước verify; frontend bỏ progress của request cũ. |
| Xuất MP4 | Bổ sung dự toán dung lượng đích; giữ kiểm tra source/voice hash, snapshot/revision/mixer, FFprobe, cancellation và promote file `.partial`. Render cuối bị hủy sẽ chạy lại từ đầu, không resume encoder giữa file. |

Không thay migration/schema SQLite 6, cấu hình model, credential, quyền Cloud hoặc chính sách bật/tắt các tính năng của ứng dụng. Query summary vẫn tính từ database; chưa thêm cache summary vì tránh thêm invalidation khi số đo truy vấn đã nằm trong ngân sách. Split/duplicate/delete vẫn dùng đường snapshot track, nay tải/lưu ở background; chưa chuyển toàn bộ thao tác cấu trúc sang delta.

## Kiểm chứng

Máy thử: i7-11800H, 8 nhân/16 luồng, RAM 15,77 GiB, SSD NVMe, Windows 11; .NET 10.0.301, Node 24.16.0. Fixture và TEMP/TMP nằm riêng dưới `D:/tmp/vm-long-video`. Không dùng project/video của người dùng hoặc gọi provider có phí.

| Kiểm tra cuối | Kết quả |
|---|---|
| Restore và build solution Release | Passed; MSBuild 0 warning / 0 error |
| Full .NET sau source cuối | **1.075 Passed / 0 Failed / 3 Skipped / 1.078 Total**, 3 phút 19 giây |
| Frontend | `npm ci`, build Passed; **148 Passed / 0 Failed / 0 Skipped**, 27 file test |
| Kiểm tra diff | `git diff --check` Passed |

Vite còn cảnh báo chunk >500 kB. Ba bài model bị skip là Qwen integration, Qwen benchmark và Piper integration; lượt này không bật opt-in và không tính chúng là đã kiểm chứng model. Báo cáo model của thay đổi trước vẫn tách riêng. Bằng chứng lần cuối: [full-verified.trx](artifacts/vietsub-long-video/full-verified.trx).

- Baseline `baseline.trx`: regression WAV dài thất bại ở giới hạn 32 MiB trước sửa.
- `VietsubLongVideoTests`: timeline 2 tiếng, tiếng ở cuối, file trung gian hữu hạn, phrase lớn bị từ chối, RIFF truncated/sai thời lượng, giọng tràn qua biên, cancel/retry, cache hỏng và bảo toàn WAV gốc.
- Cùng fixture xuất MP4 7.200 giây qua service thật, FFmpeg/FFprobe thật; giải mã audio tại giây 7.199,1 và kiểm tra còn tín hiệu.
- `VietsubLongSubtitleTests`: 10.000 cue; đối chiếu Unicode, filter/count/thứ tự và fingerprint ngữ cảnh; trigger SQLite chứng minh sửa một cue/checkpoint không ghi lại cả track; kiểm CAS, rollback và giữ nội dung sửa tay/khóa.
- Regression WebView2 dùng media thật kiểm tải waveform/thumbnail; kiểm playback/seek và hook production hiện có vẫn được chạy. Kiểm bridge từ chối context thay đổi cả trước và sau đọc nền.

### Kết quả đo fixture

Số đo dưới đây lấy từ `full-verified.trx`. Truy vấn đo 20 lượt, không xóa OS/file cache. Đối chứng là đường tải toàn bộ track trên cùng dữ liệu/máy, không phải benchmark end-to-end bản ứng dụng cũ.

| Phép đo | Kết quả |
|---|---|
| Trang 50 cue / track 10.000 cue | P50 **11,72 ms**, P95 **46,32 ms** |
| Đối chứng tải toàn track | P50 34,16 ms, P95 89,66 ms |
| WAV timeline 120 phút | **1,34 giây**; 1.382.400.044 byte; file trung gian lớn nhất 23.232.078 byte |
| Waveform 120 phút | Tổng hợp **114,02 ms**, managed allocation **145.056 byte**; PCM tổng quan trên disk 28.800.000 byte |
| MP4 thử | **22,11 giây**, 3.688.715 byte; 256 × 144, 1 fps, nền màu đơn giản, tone ngắn ở cuối; chỉ có âm thanh từ timeline giọng |

Thời gian dựng WAV chủ yếu là ghi khoảng lặng. Waveform đo bước tổng hợp PCM, không bao gồm decode cả phim. Managed allocation không phải peak RAM toàn tiến trình/máy và không bao gồm bộ nhớ native GDI+. MP4 độ phân giải thấp không đại diện hiệu năng encoder 1080p/4K. Chưa đo P95 thao tác desktop, tua trên phim thật, dropped frame, chất lượng ngôn ngữ hoặc thời gian OCR/dịch/tổng hợp giọng toàn phim.

### Lệnh tái lập

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
$env:TEMP='D:/tmp/vm-long-video'
$env:TMP=$env:TEMP
dotnet test TOOL-TESTS/TOOL-TESTS.csproj -c Release --no-build --logger 'trx;LogFileName=full-verified.trx' --results-directory artifacts/vietsub-long-video -- xUnit.ParallelizeTestCollections=false
```

Frontend: `npm ci --no-audit --no-fund`, `npm run build`, `npm test` tại `TOOL-LOCAL/Web`. Các bài model opt-in chạy riêng theo runbook, không song song với build/FFmpeg/model khác.

## Phần nghiệm thu còn mở

1. Chọn video Anh/Trung 1080p/120 phút đại diện và chạy toàn pipeline bằng runtime/model đã pin; đo từng giai đoạn và nghe/soát đầu, giữa, cuối.
2. Đo UI latency, seek, dropped frame, RAM/commit/I/O toàn máy, peak disk và cancellation khi có từng worker. Hiện chưa có bằng chứng đạt các ngưỡng này trên phim thật.
3. Soak/crash ở nhiều thời điểm, kiểm nhiều codec/VFR/rotation và video dọc. Chỉ thêm proxy, nhiều mức waveform hoặc encoder phần cứng khi số đo yêu cầu.
4. Smoke trên bundle dự định sử dụng. Build/test của workspace không phải rollout hoặc cam kết tốc độ xử lý nhanh hơn thời lượng video.

Chi tiết task gốc: [kế hoạch](KE_HOACH_TOI_UU_VIETSUB_VIDEO_2_GIO.md). Không đánh dấu toàn bộ kế hoạch đã nghiệm thu từ các fixture tổng hợp.
