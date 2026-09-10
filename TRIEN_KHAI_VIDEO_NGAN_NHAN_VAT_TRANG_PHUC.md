# Video ngắn từ nhân vật và trang phục có sẵn

Ngày cập nhật: 2026-09-10. Nhánh: `vid-short`.

Đã triển khai source MVP và kiểm thử tự động. **Ngày 2026-09-10 đã áp migration và bật tính năng trên máy Development theo yêu cầu người dùng; chưa gọi provider có phí hoặc nghiệm thu ảnh/video thật.** Không coi kết quả kiểm thử bằng HTTP giả lập là bằng chứng endpoint, credential hoặc chất lượng model trên môi trường đích đã đạt.

## Bật trên máy Development — 2026-09-10

- Đích được người dùng xác nhận: `DUNGDEV / VideoFactory`, Account Server HTTPS `localhost:7242`. Trước thay đổi đã tạo backup `COPY_ONLY/CHECKSUM`, chạy `RESTORE VERIFYONLY`, khôi phục thật sang `VideoFactory_OutfitRehearsal_20260910_224343_72f781a3` và kiểm `DBCC CHECKDB` thành công.
- Migration `4.1.9-short-video-outfit` chạy hai lần trên clone, áp least-privilege trên clone, sau đó áp migration đã đối chiếu SHA-256 lên database đích. Xác minh 2 bảng, 5 FK và 8 CHECK được trust, 1 index nghiệp vụ, 8 quyền DENY cho desktop và đúng 1 dòng version. Số lượng project/cảnh cũ và số cảnh `CharacterOutfit` không đổi. Migration đã áp DENY trực tiếp cho hai bảng mới; không cần cấp lại quyền rộng trên database đích.
- Cờ server bật trong Development user-secrets của `TOOL-SERVER`. Cờ desktop bật trong `TOOL-LOCAL/appsettings.user.json` và cấu hình cạnh binary Debug/Release. Cấu hình mặc định trong source vẫn tắt; không đưa cấu hình máy hay secret vào bản phát hành.
- Đã khởi động lại đúng server Release sau khi kiểm tra không có job provider/Cloud/TikTok đang chạy, rồi mở desktop Release. Smoke HTTPS: login trả 400 với JSON kiểm tra sai định dạng, endpoint outfit state trả 401 khi không có phiên, log startup Development không có lỗi schema. Chưa xác minh toàn bộ thao tác nhập ảnh qua WebView2 hoặc gọi AI thật.
- Bằng chứng cục bộ: `artifacts/outfit-enablement/outfit-enablement-preflight.json` và `outfit-enablement-applied.json`. Backup nằm trong thư mục backup của SQL Server; giữ backup và database rehearsal để đối chiếu. Không coi lần bật Development này là rollout production hoặc bằng chứng API contract Kling đã được xác minh.
- Tắt lại trên máy này bằng cách đặt hai override về `false`, đồng bộ override desktop cạnh binary và khởi động lại server/desktop. Giữ schema, lịch sử và snapshot.

## Cách sử dụng sau khi môi trường được bật

Bổ sung giao diện 2026-09-10: trang Video ngắn có nút vào chế độ phối trang phục ngay phía trên form mô tả. Nút mở popup với `CharacterOutfit` đã chọn, không phát sinh AI. Ba test tích hợp kiểm từ nút này đến hai ô ảnh sau khi tạo dự án, đóng/mở lại popup thường và chặn khi feature tắt hoặc generation đang chạy. Release solution và desktop Debug build đạt; .NET **1221 Passed / 0 Failed / 5 Skipped**, frontend **183 Passed / 0 Failed / 0 Skipped**. Đã kiểm hash bundle được đưa vào cả hai bản desktop.

1. Trên trang **Tạo Video Ngắn**, bấm **Nhân vật + trang phục** ở đầu trang để mở sẵn chế độ phối đồ. Nhập tên/mô tả, chọn tỷ lệ, thời lượng 5–15 giây và giữ/tắt âm thanh, rồi tạo dự án. Vẫn có thể đi qua **Tạo video mới → Video ngắn → Nhân vật mặc trang phục có sẵn**.
2. Chọn riêng ảnh nhân vật và ảnh trang phục từ máy, nhập bối cảnh và chuyển động, bấm **Lưu ảnh và thiết lập**.
3. Xem báo giá ảnh và xác nhận chi phí. Kiểm tra khuôn mặt, dáng người, màu sắc/chi tiết quần áo rồi duyệt ảnh mặc thử.
4. Xem báo giá video và xác nhận chi phí. Video được tạo từ chính ảnh đã duyệt; không chuyển ngầm sang tạo từ chữ.
5. Xem hết video, xác nhận và duyệt. Video tắt tiếng cũng cần duyệt hình. Sau đó **Dựng MP4 từ video đã duyệt → Xuất MP4**. Xuất lại không gọi AI.

Tỷ lệ, thời lượng và lựa chọn audio cố định theo dự án đã tạo. Đổi ảnh nguồn, bối cảnh **hoặc chuyển động** trong bản đầu đều tăng revision và yêu cầu tạo/duyệt lại ảnh, video. Bản đầu dùng hai ảnh riêng; chưa có đường bỏ bước phối đồ cho một ảnh nhân vật đã mặc sẵn trang phục. Các lựa chọn chưa bấm Lưu không được coi là thiết lập hiện hành.

## Phần đã triển khai

| Task | Source và kiểm thử | Phần cần môi trường thật |
|---|---|---|
| T01 | Chốt `CharacterOutfit`, hai ảnh edit, first-frame; chỉ mở `kling-3.0`/720p/Native Audio theo catalog và adapter hiện hành; chặn Omni/BytePlus/Fal/model khác | Xác nhận API contract Kling trên endpoint thực tế; smoke model/capability |
| T02 | DTO chia sẻ, mode mặc định `TextOnly`, message C#/TypeScript và state theo project/organization/revision | WebView2 trên bản cài đích |
| T03 | Migration idempotent 4.1.9, mapping server, token concurrency, FK/check/index, DENY desktop và migration tests; đã backup/restore, chạy lặp trên clone và áp Development | Kiểm SQL concurrency và quyền bằng principal desktop thực tế |
| T04–T06 | Native import/EXIF, hash/MIME/size, ảnh edit hai nguồn, quote/claim/idempotency, approve/reject/invalidation | Chấm chất lượng phối đồ bằng ảnh mẫu |
| T07–T08 | Preflight policy/credential/rate video trước báo giá ảnh; giá từ server; clip từ ảnh Approved/current, snapshot nguồn và polling/output proxy hiện hành | Submit/poll/restart/settlement qua provider thật |
| T09–T10 | Component riêng, xác nhận giá, chống bấm lặp/response cũ, duyệt clip im lặng, render/export kiểm lineage | Smoke desktop thật và xuất MP4 với clip thật |
| T11 | Build toàn solution và các test tự động đạt; chi tiết bên dưới | Các bài opt-in/model thật chưa chạy |
| T12 | Có hướng dẫn cấu hình, rehearsal, nghiệm thu và rollback; đã áp database và bật override Development theo yêu cầu | Nghiệm thu AI thật và rollout production |

Các file chính:

- `TOOL-SHARED.Contracts/Generation/ShortVideoOutfitContracts.cs`: metadata ảnh, settings, quote, composition, approval và input video.
- `TOOL-SERVER/Generation/ShortVideoOutfitService.cs`: access, revision, báo giá, snapshot nguồn/rate/model/credential, claim trước outbound, tạo/duyệt ảnh và kiểm input video.
- `TOOL-SERVER/Controllers/ShortVideoOutfitController.cs`: API authenticated tại `api/generation/short-video` và binary image download cùng origin.
- `TOOL-LOCAL/Generation/ShortVideoWorkflowService.cs`: ảnh cục bộ, chuẩn hóa orientation/bỏ EXIF, workspace/hash, quote video đã xác nhận và kiểm lineage.
- `TOOL-LOCAL/WebView/DashboardBridge.ShortVideo.cs`, `TOOL-LOCAL/Web/src/features/shortVideo/OutfitShortVideo.tsx`: toàn bộ luồng trên trang Video ngắn.
- `ProjectGenerationService`, `ProjectService`, `ProjectRenderService`: tải/kiểm clip, duyệt hình, kiểm composition/revision/generation và manifest trước/sau render, trước promote file xuất.

`vf.ShortVideoOutfits` giữ thiết lập hiện hành; `vf.ShortVideoOperations` giữ quote và lịch sử composition. Bảng này do server sở hữu, desktop không truy cập SQL trực tiếp. Quote giữ metadata/hash nguồn theo vai trò, hash bối cảnh/chuyển động, tỷ lệ/thời lượng, model/credential/rate snapshot. Ảnh nguồn không nằm trong request JSON/ledger; byte chỉ được gửi từ native vào gateway khi xác nhận tạo ảnh/video. Base64 không trả lại frontend.

Ảnh nguồn PNG/JPEG tối đa 10 MiB, 16 megapixel; bản chuẩn hóa cũng phải đạt giới hạn này. Ảnh kết quả hiện chấp nhận 720×1280, 1280×720 hoặc 1024×1024, tối đa 8 MiB. Payload kết quả trong `GeneratedImageOutputs` dùng retention `Generation:OpenAiImage:RetentionHours` và cleanup hiện hành; bản native đã kiểm tra được lưu tại workspace dự án qua `.part` rồi promote. Mở lại trên cùng workspace dùng file đã lưu; không yêu cầu gọi AI lại chỉ vì payload server đã hết hạn.

Video request mới có composition ID, revision, quote ID và SHA-256 ảnh trong request snapshot. Snapshot JSON của request video cũ không bị thêm field null làm đổi hash idempotency. First-frame Fal/Veo dài vẫn dùng service/gate riêng.

## Điều kiện bật và rehearsal

Cờ mặc định đều tắt:

```text
Server:  Generation:ShortVideoCharacterOutfit:Enabled = false
Desktop: Features:ShortVideoCharacterOutfitEnabled = false
```

Chỉ đổi sang `true` trong môi trường đã được chỉ định, sau khi:

1. Xác minh instance/database, quyền tác động và backup đã restore thử. Trên clone đã có baseline, chạy `database/VideoFactory.4.1.9.ShortVideoCharacterOutfit.sql` hai lần, sau đó `VideoFactory.DesktopLeastPrivilege.sql`. Kiểm version `4.1.9-short-video-outfit`, hai bảng, FK/check/index, quyền desktop bị từ chối và project cũ không đổi mode.
2. Kiểm race trên SQL Server bằng hai session: claim cùng quote, hai quote cùng scene, đổi revision trong lúc ảnh đang chạy, approve ảnh đồng thời đổi selection. Kiểm chỉ một outbound cho cùng operation và không release reservation khi upstream chưa chắc chắn.
3. Deploy server/desktop cùng contract. Global Admin cấu hình rate Active, Owner cấu hình credential đã test và ngân sách dương. Policy `Default` cần `kling-3.0`, 720p, Native Audio và đúng tỷ lệ/thời lượng. OpenAI image cần `gpt-image-2`. Không seed giá từ tài liệu này.
4. Xác nhận API contract ở môi trường đích. Adapter đang dùng `image-to-video/kling-3.0` với `contents[].type=first_frame` và polling `/tasks`, giữ cơ chế hiện hành của repository. Fake HTTP chỉ kiểm body/đường gọi này. Tài liệu Kling công khai bị robots chặn khi đọc lại trong phiên này; **chưa xác minh được mapping wire protocol thực tế trên host được allowlist**. Phải đối chiếu với tài liệu API của tài khoản/provider đang dùng trước khi bật. Nếu khác, sửa adapter/catalog và test tương ứng; không mở host tùy ý hoặc fallback từ desktop.
5. Chỉ chạy smoke AI khi đã có môi trường, ảnh mẫu và trần chi phí được phê duyệt. Chạy ít nhất một mẫu cho từng tỷ lệ; cả giữ audio và tắt audio; kiểm crop/mặt/họa tiết/tay/chuyển động, đóng mở desktop khi video đang chạy, tải lại cùng request và xuất MP4.

OpenAI Image Edits hỗ trợ nhiều ảnh đầu vào theo [API reference đã đối chiếu](https://developers.openai.com/api/reference/resources/images/methods/edit). Giới hạn chất lượng và quyền model vẫn cần nghiệm thu với ảnh thực tế. Danh sách mẫu chi tiết nằm trong [kế hoạch ban đầu](KE_HOACH_VIDEO_NGAN_NHAN_VAT_TRANG_PHUC.md).

## Lỗi, phục hồi và rollback

- Quote hết hạn sau 10 phút. Tạo lại ảnh/video cần báo giá và xác nhận mới. Chọn **Tiếp tục / tải lại video đã gửi** để dùng quote/request đã lưu trong workspace; không tự tạo quote trả phí mới khi retry local.
- HTTP từ chối ảnh chắc chắn như 400/401/403/429 được ghi Failed và release reservation. Timeout/mất kết nối sau dispatch giữ Unknown cùng reservation. Các quote đã cấp trước đó cũng không vượt qua được khóa operation chưa xác định. Getter hiển thị cảnh báo trạng thái này.
- Request ảnh Completed nhưng settlement lỗi giữ output và kết quả đã lưu. `BudgetReconciliationWorker` đối soát request Completed bằng usage/rate snapshot. Unknown/Submitting cần chứng cứ từ provider để đối soát; không sửa thành Failed hoặc tạo lại chỉ để hết thông báo. Không có chức năng tự gửi lại ảnh sau khi desktop đóng giữa request chưa rõ kết quả.
- Chỉ thay đổi motion cũng vô hiệu hóa ảnh trong MVP; đây là quyết định đơn giản hóa revision, chưa tối ưu tái sử dụng composition khi chỉ đổi chuyển động.
- Khi thiếu file nguồn hoặc ảnh tạm đã hết hạn trên máy khác, nhập lại nguồn hoặc phục hồi workspace. Không coi preview/file tồn tại là bằng chứng duyệt; render/export còn kiểm state trên server và generation trong manifest.
- Tắt cả hai cờ để khóa luồng mới. Worker polling video, output cleanup và budget reconciliation hiện hành tiếp tục xử lý task đã gửi. Giữ migration/lịch sử/snapshot; không dùng binary cũ không biết mode/lineage để dựng project `CharacterOutfit`.

## Kết quả kiểm thử source

| Kiểm tra cuối | Passed | Failed | Skipped |
|---|---:|---:|---:|
| .NET Release (`TOOL-TESTS`) | 1221 | 0 | 5 |
| Frontend Vitest | 180 | 0 | 0 |

`dotnet restore TOOL_GEN_POST_VIDEO.slnx`, Release build toàn solution và frontend production build đạt. Release build cuối có 0 warning, 0 error. Frontend có cảnh báo kích thước bundle trên 500 kB, không làm build thất bại.

Đã thêm kiểm thử server cho hai nguồn, idempotency, Unknown, hai quote cũ, rate/budget/access, hết hạn/đổi byte, approval/revision, policy/model không hỗ trợ; kiểm multipart OpenAI và first-frame Kling bằng HTTP giả. Native có kiểm ảnh/hash và render/export cũ, kể cả scene im lặng. Sáu test React kiểm context/request ID, busy, xác nhận giá, duyệt ảnh, thay nguồn và feature tắt. Migration test là kiểm cấu trúc script, **không phải** kết quả thực thi SQL.

Lượt .NET trước có một test `Cpu_backend_dry_run_reports_selected_avx_and_native_hash` timeout; chạy riêng đạt và lượt toàn bộ cuối đạt các số trên. Không sửa worker dịch hoặc nới timeout để che lỗi. Năm bài Skipped là Qwen/model/benchmark, Piper, Veo local voice và SQL opt-in TikTok; không tính chúng là đã nghiệm thu model hay database. Build thử ban đầu gặp DLL server bị khóa; đã dùng thư mục build riêng để tiếp tục, sau đó build chuẩn toàn solution đạt. Không dừng server/IDE của người dùng.

Logs kiểm tra nằm dưới `.tmp/outfit-tests-final.log` và các log validation cục bộ; không đưa chúng thành nguồn cấu hình hoặc bằng chứng rollout.
