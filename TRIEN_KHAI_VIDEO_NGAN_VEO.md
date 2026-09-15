# Triển khai Veo cho toàn bộ video ngắn

Ngày kiểm chứng: 2026-09-11. Nhánh: `vid-short`.

## Hành vi hiện hành

- Cả `DirectShortVideo/TextOnly` và `DirectShortVideo/CharacterOutfit` tạo clip bằng Veo 3.1 qua Fal. Dùng model Standard hoặc Fast đã cấu hình trong policy `LongForm` của tổ chức; không fallback sang Kling hoặc Text-to-Video.
- Thời lượng chọn 4, 6 hoặc 8 giây, mặc định 8; tỷ lệ 9:16 hoặc 16:9; clip provider 720p với Native Audio. Tắt âm thanh vẫn tạo theo policy Native Audio, sau đó desktop loại bỏ audio cục bộ.
- Thư viện nhân vật/trang phục, bản nháp và bố cục composer hai cột được giữ. Tạo ảnh mặc thử bằng OpenAI, duyệt ảnh, báo giá và tạo clip Veo, xem/duyệt video rồi dựng/xuất MP4.
- Video từ nội dung cũng cần tạo và duyệt ảnh đầu cảnh bằng OpenAI trước khi báo giá clip Veo. Không gọi OpenAI viết lại content, không áp content/speech policy dành riêng cho video dài.
- Đóng hộp báo giá ngay khi xác nhận tạo; trạng thái xử lý/lỗi hiển thị tại khung kết quả. Mỗi lần tạo ảnh/video mới phải xác nhận chi phí; phát lại request đã gửi giữ cùng idempotency key.

Tham chiếu thông số adapter đã kiểm: [Fal Veo 3.1 Image-to-Video API](https://fal.ai/models/fal-ai/veo3.1/image-to-video/api). Khả năng thực tế còn phụ thuộc catalog, policy, credential, rate và budget trên server.

## Dùng với dự án Kling đang lỗi

1. Mở dự án trong **Tạo video ngắn**, bấm **Chuyển dự án sang Veo**.
2. Chọn 4/6/8 giây và tỷ lệ; dự án 15 giây đề xuất chuyển thành 8 giây. Xác nhận chuyển thiết lập chưa phát sinh phí AI.
3. Ảnh mặc thử đã duyệt được giữ nếu tỷ lệ không đổi, đúng kích thước và giới hạn ảnh của Veo. Nếu đổi tỷ lệ, tạo và duyệt ảnh mới. Nhân vật, trang phục và lịch sử request cũ vẫn được giữ.
4. Bấm tạo video, kiểm tra báo giá Veo và xác nhận. Xem hết clip, duyệt hình/âm thanh, chuẩn bị và xuất MP4.

Không đổi snapshot dự án tự động khi mở trang hoặc khi admin đổi policy. Thao tác chuyển chạy transaction, kiểm quyền, runtime/rate và yêu cầu không còn request/operation pending hoặc Unknown; báo giá chưa dùng hết hiệu lực và các con trỏ video đã duyệt được xóa để tạo thế hệ mới. Không xóa request, ledger hoặc media lịch sử. Chuyển lại cùng thiết lập là idempotent và không gọi provider.

## Đường gọi và kiểm soát

- DTO công khai: `ShortVideoVeoContracts`, `SubmitVideoRequest.ShortVideoQuoteId`; server và desktop dùng cùng contract. Bridge mới: `outfit.migrate`, `outfit.text-quote`, `outfit.text-video`, `outfit.text-resume`.
- Server `ShortVideoVeoPolicy` chặn provider/model/thời lượng/tỷ lệ không hỗ trợ trước outbound. Policy `LongForm` được dùng lại mà không thay policy Default hoặc snapshot video dài hiện có.
- `ShortVideoVeoFirstFrame` ánh xạ composition đã duyệt sang `MediaAsset` và `SceneFirstFrame` thật, giữ approval, provider request, hash và revision nguồn. `GenerationService` vẫn đi qua toàn bộ kiểm tra Approved/current của `SceneFirstFrameService`, đồng thời đối chiếu composition.
- TextOnly dùng `SceneFirstFrameService` hiện hành. Quote video bền vững trong `vf.ShortVideoOperations`, gắn frame/hash, prompt/version/hash, plan, model, credential, thiết lập và rate snapshot. Idempotency key là `short-video:{quoteId:N}`; claim/consume/reservation giữ cơ chế transaction và chống giữ ngân sách lần hai.
- Báo giá frontend/native gắn tổ chức, tài khoản và dự án; thay context hoặc ảnh/prompt phải lấy báo giá mới. Resume chỉ dùng request thực sự đã gửi, không tự tạo task mới để kiểm tra lỗi.
- Desktop lưu quote và tải output qua proxy, kiểm bytes/MIME/hash/stream và lineage ảnh đầu vào cho cả hai mode trước duyệt/render/export. Không gửi provider key hoặc URL output gốc xuống frontend; không có provider client trực tiếp trên desktop.
- Server tiếp tục polling task, settlement/release và output lifecycle hiện hành. Lỗi Kling cũ không được tự submit lại bằng Veo.

## Môi trường và database

- Thay đổi này không thêm migration. Cần schema `4.1.9-short-video-outfit` hiện có cho quote video ngắn, kể cả TextOnly; feature flag chỉ kiểm soát mode CharacterOutfit.
- Đã kiểm SQL chỉ đọc tại `DUNGDEV / VideoFactory`: có migration 4.1.9; policy Active `LongForm` là `fal-ai/veo3.1/fast/image-to-video`, 720p, Native Audio; không có provider request chưa terminal hoặc operation Submitting/Unknown trước cập nhật binary.
- Không chạy SQL cập nhật dữ liệu, không đổi rate/credential hoặc tạo request AI có phí trong lần triển khai này. Flag Development có sẵn được giữ; mặc định phát hành vẫn không tự bật Fal hoặc phối đồ.
- Cập nhật server và desktop cùng bản; không chỉ chép JavaScript vào desktop cũ. Backup binary nằm trong `artifacts/short-video-veo/binary-backup-*`, đường dẫn chính xác trong `binary-backup-path.txt`; không sao chép user-secrets hoặc file cấu hình bí mật vào báo cáo.
- Đã build Release toàn solution tại đường dẫn chuẩn và Debug desktop: 0 warning/error. Server Release của checkout này đã khởi động lại tại `https://localhost:7242/`; `/api/auth/me`, `/api/generation/short-video/migrate-veo` và `/api/generation/short-video/quote-text-video` trả 401 khi không có đăng nhập, đúng yêu cầu bảo vệ API. Không dừng server của checkout video dài.
- Đã so SHA-256 cả 3 file frontend production với `wwwroot` ở Debug và Release: khớp. Bộ kiểm duyệt tự động chặn lệnh kết hợp đọc override cục bộ và mở desktop, phản hồi chỉ ghi `blocked by policy`; desktop chưa được mở lại. Có thể mở thủ công `TOOL-LOCAL/bin/Release/net10.0-windows/win-x64/TOOL-LOCAL.exe` để đăng nhập và chuyển dự án cũ qua UI.

## Kiểm chứng

| Phạm vi | Passed | Failed | Skipped |
|---|---:|---:|---:|
| .NET ngoài Vietsub | 950 | 0 | 2 |
| .NET Vietsub | 293 | 0 | 3 |
| Tổng .NET, hai nhóm không giao nhau | 1243 | 0 | 5 |
| Frontend, 34 file | 193 | 0 | 0 |

Restore solution, `npm ci`, production frontend build và Release solution build ở thư mục riêng đạt, không warning/error ở lượt build cuối. Test chạy với TEMP/TMP/cache trên D và giới hạn song song phù hợp tài nguyên máy. Năm test opt-in SQL/model bị skip không được coi là đã nghiệm thu SQL race hoặc model thật.

Bổ sung kiểm thời lượng/tỷ lệ Veo, duyệt/stale ảnh mặc thử, chuyển snapshot giữ ảnh/đổi tỷ lệ/idempotency/pending, quote TextOnly gắn frame/prompt và một lần claim, tạo/duyệt ảnh đầu cảnh TextOnly. Test gateway tích hợp với provider giả kiểm gửi đúng frame đã duyệt sang Fal, chặn thiếu frame trước ngân sách/outbound và replay không reserve lần hai. Các bài Kling Native Audio tiếp tục kiểm workflow legacy; DirectShortVideo gọi Kling bị chặn.

Frontend kiểm lưu dự án không gọi AI, điều kiện xem/duyệt ảnh, báo giá/xác nhận tạo video và phục hồi lỗi, chuyển dự án cũ có xác nhận. WebView2 thật kiểm composer nhân vật/trang phục với fixture tự tạo ở 1920/1440/1366/760 và zoom 100/125/150%, kiểm tràn khung và thumbnail. Screenshot trong `artifacts/short-video-veo/screenshots`; hình fixture không phải kết quả AI thật.

Log cuối trong `artifacts/short-video-veo`: `restore.log`, `build-verified.log`, `dotnet-main-verified.log`, `dotnet-vietsub.log`, `frontend-verified.log`, `release-deployment-build.log`, `debug-desktop-build.log`, `https-smoke.json`. Lượt main trước đó có một test source tìm component cũ bị lỗi; đã cập nhật để kiểm component mới và chạy lại toàn bộ nhóm main đạt. Không cộng lượt lỗi hoặc test lặp vào kết quả cuối.

Chưa gọi provider trả phí để nghiệm thu chất lượng nhận diện nhân vật, độ đúng trang phục, chuyển động hoặc âm thanh thực tế. Không khẳng định chuyển provider sẽ loại bỏ mọi lỗi hạn mức; lỗi Fal được xử lý qua cơ chế server hiện hành.
