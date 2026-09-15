# Hợp nhất main vào local-2 — 2026-09-10

## Phạm vi

Hoàn tất merge đang dở từ `main` (`52ee253b4dfd4616deaa12c209489fee22e55516`) vào `local-2` (`f602016588cba8c8bd172dd222eed32821633183`). Xử lý 21 khối xung đột trong 10 file; giữ các thay đổi tự hợp nhất của Git.

- Giữ Vietsub editor, thiết kế phụ đề, mixer, xuất MP4, dịch Cloud và phục hồi phiên đăng nhập từ `local-2`.
- Giữ popup tạo dự án ngắn/dài, hai luồng âm thanh video, đồng nhất giọng Veo local và TikTok nhiều tài khoản từ `main`. Module thực thi lip-sync Cloud tiếp tục được loại bỏ; migration và trạng thái lịch sử vẫn được giữ.
- Ghép đầy đủ constructor, composition và WebView bridge của desktop; đăng ký cả worker Vietsub Cloud và các dịch vụ TikTok trên server.
- Khi bị khóa license, giữ ngữ cảnh dự án và xử lý `SessionLimit`/đăng xuất/kiểm tra lại. Vẫn cập nhật cờ TikTok để Admin có thể xác minh credential trong phạm vi cho phép. Member không được mở ngoại lệ này.
- Cập nhật hai fixture license theo contract mới; bổ sung hai ca hồi quy Admin/Member. Fixture browser chờ React xử lý phản hồi và reset consent trước thao tác tiếp theo, không bỏ assertion hoặc tăng timeout.
- Ghép tài liệu của cả hai nhánh, giữ bằng chứng lịch sử tách biệt với kết quả lần merge này. Các migration cùng tiền tố số có mã `SchemaVersions` riêng; không sửa hay đổi tên SQL lịch sử.

Cấu hình được kế thừa: desktop bật `VeoLocalVoiceConsistencyEnabled`/`TikTokEnabled`; server bật `TikTok:MultiAccountEnabled`; cấu hình Vietsub Cloud của `local-2` đã có `Enabled=true` và model `gpt-5.6-luna`. Không suy từ các cờ này rằng runtime, database hoặc provider thật đã được nghiệm thu.

## Xác minh source sau hợp nhất

Build và kiểm thử tại checkout riêng `D:\VideoMakerValidation\merge-local-2-main-20260910\checkout`, TEMP/TMP trên D để tránh ổ C gần đầy và đường dẫn SQLite dài. Source/test trong checkout đã được đối chiếu với workspace, bỏ qua khác biệt CRLF/LF.

| Phạm vi | Passed | Failed | Skipped |
|---|---:|---:|---:|
| .NET Release, toàn bộ suite | 1185 | 0 | 5 |
| Frontend Vitest, 31 file | 174 | 0 | 0 |
| TikTok Admin state | 7 | 0 | 0 |
| TikTok Admin/Desktop browser | 18 | 0 | 0 |

- `dotnet restore` và Release build toàn solution đạt, MSBuild 0 warning/0 error. Production build frontend qua `npm run build` đạt; Vite vẫn cảnh báo chunk JavaScript lớn hơn 500 kB.
- `npm ci --no-audit --no-fund` đạt. Frontend chạy `npm test -- --maxWorkers=1 --no-file-parallelism`; .NET chạy `dotnet test ... -c Release --no-build -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`.
- Hai regression license/TikTok thất bại trước sửa và đạt sau sửa. Các ca license hiện có tiếp tục đạt.
- Browser popup dùng App production và bridge giả: chọn loại, Escape/focus, lỗi giữ nội dung, tạo/mở dự án ngắn và dài, chống bấm lặp, không gọi AI khi tạo dự án, tái sử dụng dự án ngắn và bố cục mobile đều đạt.
- Ca browser đổi nhanh A/B/A đã chạy lại ba lần sau khi sửa đồng bộ fixture. Các lượt lặp không cộng vào tổng Passed trong bảng.
- Lượt đầu phát hiện fixture TypeScript thiếu `tikTokEnabled`, fixture C# thiếu tham số constructor mới; đã cập nhật. Lượt frontend song song gặp thiếu bộ nhớ nên chạy lại tuần tự. Playwright mặc định trỏ tới Chromium không còn trên máy; lượt cuối dùng executable Chromium 1234 có sẵn qua adapter bên ngoài repository, không đổi dependency hay tải browser.

Năm bài .NET Skipped: Qwen integration, Qwen benchmark, Piper integration, Veo local voice model và TikTok SQL LocalDB opt-in. Không tính các bài này là model/database đã đạt.

## Bằng chứng và giới hạn

Log, TRX, fixture browser và ảnh tại `D:\VideoMakerValidation\merge-local-2-main-20260910`; TRX cuối là `results\merge-final.trx`. Bản sao index, trạng thái Git, patch và 10 file trước sửa nằm ở thư mục `before` cùng vị trí. Artifact không được đưa vào commit.

Lần merge này không chạy migration trên database thật, không gọi provider/OAuth/đăng TikTok thật, không thay binary của server/desktop đang chạy và không push. Nghiệm thu model, clip Veo tiếng Việt, Cloud có phí và môi trường triển khai vẫn là các bước riêng theo runbook.
