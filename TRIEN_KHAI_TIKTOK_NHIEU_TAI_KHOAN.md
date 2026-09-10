# Quản lý nhiều tài khoản TikTok

Cập nhật 2026-09-09. Phạm vi: nhiều tài khoản TikTok cho một người dùng VideoMaker. Không chia sẻ tài khoản qua organization, không đăng hàng loạt, không thêm scheduler.

## Hành vi đã triển khai

- Thêm tài khoản bằng OAuth chính thức; chọn tài khoản nhận bài; tìm kiếm, kết nối lại và ngắt riêng từng tài khoản.
- Kết nối lại giữ nguyên ConnectionId và lịch sử. Đăng nhập bằng tài khoản khác trong luồng kết nối lại trả `tiktok_oauth_account_mismatch`, không thay danh tính bản ghi cũ.
- Khi có nhiều tài khoản, mở lại desktop yêu cầu chọn rõ tài khoản. Chuyển A sang B lấy lại creator info và bỏ privacy, tương tác, disclosure, consent cũ. Caption và file còn trên máy để người dùng có thể đăng lại có chủ đích.
- Một upload local tại một thời điểm; sau upload có thể đăng bằng tài khoản khác trong khi server tiếp tục theo dõi các bài trước. Hiển thị các job đang chạy và lịch sử phân trang theo tài khoản hoặc toàn bộ.
- Ngắt A không đổi token/job của B. Lịch sử A vẫn giữ; job chưa terminal của A dừng theo dõi với lý do `connection_revoked`. Video đã gửi có thể tiếp tục được TikTok xử lý.
- Avatar được tải qua server và virtual host native, có fallback chữ cái khi ảnh không có, hết hạn, quá lớn hoặc host không thuộc allowlist. Không gửi URL CDN có chữ ký sang React.
- Admin hiển thị riêng số người dùng kết nối và số tài khoản TikTok; khóa thay credential dựa trên kết nối/job đang sử dụng.

## Dữ liệu và chống gửi trùng

Migration mới: [VideoFactory.4.1.8.TikTokMultiAccount.sql](database/VideoFactory.4.1.8.TikTokMultiAccount.sql).

Unique key kết nối là `(UserId, AppKeyHash, OpenId)`. OpenId được ràng buộc trong phạm vi ứng dụng TikTok, không dùng username làm danh tính. Migration giữ nguyên ConnectionId, ciphertext, token expiry, job và lịch sử. AppKeyHash của dữ liệu cũ để NULL, chỉ gắn sau OAuth/refresh đã xác minh. Lịch sử ưu tiên username/nickname đã snapshot lúc đăng. Job cũ thiếu snapshot hiển thị tên hiện tại của kết nối có đúng ConnectionId kèm ghi chú; không lấy tên từ tài khoản đang chọn và không ghi ngược tên hiện tại vào lịch sử database. Nếu không tìm được kết nối hoặc tên, hiển thị chưa xác định tài khoản.

`ClientRequestId` đại diện một lần đăng, độc lập `MediaId` của file. Server lưu `TikTokPublishAttempts` và hash payload trước khi gọi Direct Post init. Retry cùng ID và payload trả lại job đã tạo nếu còn phiên upload hợp lệ; đổi account/payload với cùng ID bị từ chối. Timeout hoặc lỗi lưu kết quả sau outbound giữ attempt chưa xác định, chặn tự submit lại kể cả server restart. State có tối đa 20 attempt chưa có job gần nhất để người dùng biết cần kiểm tra trên TikTok. Nút **Chuẩn bị lần đăng mới** yêu cầu xác nhận đã kiểm tra; thao tác này chưa tự gửi video.

SQL session application locks phối hợp OAuth theo user, token/disconnect/reconnect theo connection, init theo user/request. Job snapshot connection và tên creator. Worker claim hạn 5 phút trước polling, không phụ thuộc tài khoản đang chọn ở desktop. State/history/status API đọc SQL; worker thực hiện provider polling.

## API và bridge

| Thao tác | API mới hoặc thay đổi |
|---|---|
| State nhiều tài khoản | `GET /api/tiktok/connections` |
| Creator | `POST /api/tiktok/connections/{connectionId}/creator-info` |
| Ngắt | `DELETE /api/tiktok/connections/{connectionId}` |
| Avatar | `GET /api/tiktok/connections/{connectionId}/avatar` |
| Lịch sử | `GET /api/tiktok/publish-history?connectionId=...&page=1&pageSize=20` |
| OAuth | Start thêm `multiAccount=true`, `targetConnectionId` khi kết nối lại |
| Init | Request thêm `connectionId`, `clientRequestId` riêng theo lần đăng |
| Status | `GET /api/tiktok/publish/{jobId}` chỉ đọc trạng thái đã lưu |

Bridge gửi `connectionId`, `mediaId`, `clientRequestId`; mỗi phản hồi có request ID. Native kiểm tra account ID từ server, snapshot file trước init và kiểm lại file trước upload. Một thao tác không thể hủy thao tác khác bằng request ID cũ. React bỏ creator/history trả về sai account hoặc request cũ, không đưa tiến độ/tên file của B vào dialog job A.

API legacy giữ tương thích cho user có tối đa một bản ghi kết nối. Khi đã có nhiều bản ghi, endpoint không chỉ rõ connection trả `tiktok_client_update_required`; không tự chọn tài khoản đầu tiên, kể cả khi một số kết nối đã ngắt.

## Thứ tự bật trên môi trường đích

1. Xác định instance/database đích và cửa sổ bảo trì. Backup database và Data Protection key ring, xác minh backup/restore theo runbook. Không chạy server cũ và mới cùng lúc trong giai đoạn nâng cấp này.
2. Áp dụng migration 4.1.8 sau 4.1.7; kiểm tra bản ghi schema `4.1.8-tiktok-multi-account`, ConnectionId/token/job cũ giữ nguyên, bỏ unique UserId và có unique user/app/OpenId.
3. Triển khai server mới với `TikTok:MultiAccountEnabled=false`; worker và API mới đều yêu cầu schema 4.1.8, kể cả khi cờ tắt.
4. Triển khai desktop mới. Kiểm tra tài khoản cũ, preview, creator info, lịch sử và job recovery bằng môi trường test đã được phép.
5. Bật `TikTok:MultiAccountEnabled=true` trên server (biến môi trường tương ứng `TikTok__MultiAccountEnabled=true`, áp dụng khi server khởi động lại). Tài khoản hiện có không cần ngắt hoặc đổi credential để thêm tài khoản thứ hai.
6. Smoke OAuth thêm B, reconnect đúng/sai tài khoản, đăng riêng A/B, ngắt A, mở lại desktop/server và lịch sử. Chỉ thử đăng thật trên tài khoản/môi trường đã cho phép.

Tắt `MultiAccountEnabled` chỉ chặn thêm danh tính mới khi user đã có kết nối; các tài khoản hiện có vẫn có thể chọn, kết nối lại và sử dụng. Không rollback binary server một tài khoản sau khi đã phát sinh nhiều kết nối. Giữ server tương thích và tắt cờ để dừng mở rộng. Muốn quay lại toàn bộ schema/dữ liệu cũ cần kế hoạch restore và xử lý các bài đăng đã gửi sau thời điểm backup.

## Kiểm chứng trong workspace

- Release solution build: thành công, 0 warning C#, 0 error. Vite có cảnh báo kích thước bundle lớn hơn 500 kB.
- .NET: **1029 passed, 0 failed, 3 skipped** (1032 total), gồm kiểm thử SQL có opt-in. Ba ca Qwen/Piper chưa chạy model thật.
- Frontend: **77 passed, 0 failed** (15 file).
- Trình duyệt desktop: **7 passed**; Admin state/trình duyệt: **18 passed**. Dùng React/CSS/script hiện hành với dữ liệu giả và chặn network ngoài fixture.
- SQL Server LocalDB riêng: chạy migration 4.1.8 hai lần; backup CHECKSUM và RESTORE VERIFYONLY; giữ token/ID/job legacy, không đoán snapshot; unique identity, attempt commit trước outbound, khóa init giữa hai DbContext và claim worker không trùng. Instance và file thử nghiệm được tạo riêng rồi dọn sạch.

Chạy SQL test bằng PowerShell ở root (cần SQL Server 2019 LocalDB):

```powershell
$env:VIDEOMAKER_RUN_TIKTOK_SQL_TESTS = '1'
dotnet test TOOL-TESTS/TOOL-TESTS.csproj -c Release --no-build --filter FullyQualifiedName~MultiAccount_SqlServer
Remove-Item Env:VIDEOMAKER_RUN_TIKTOK_SQL_TESTS
```

Test không nhận connection string môi trường: nó luôn tạo instance `VideoMakerTikTokTest_<GUID>` riêng. Mặc định ca này skipped khi chưa opt-in. Đây là rehearsal schema TikTok với các bảng auth phụ trợ tối thiểu và dữ liệu giả; chưa thay thế rehearsal bản sao database đầy đủ của môi trường đích.

## Áp dụng vào database đang dùng — 2026-09-09

Theo yêu cầu trực tiếp của người dùng, đã áp migration `4.1.8-tiktok-multi-account` trên instance **DUNGDEV**, database **VideoFactory**. Kiểm tra sau áp dụng lúc **15:35 UTC / 22:35 giờ Việt Nam** ngày 2026-09-09.

- Trước thay đổi: database ONLINE/READ_WRITE, TikTok schema 4.1.7 đã có, 1 kết nối, 2 job đều terminal, 1 Data Protection key; không có session người dùng khác trên database lúc preflight.
- Backup đầy đủ `COPY_ONLY, CHECKSUM, COMPRESSION`; `RESTORE VERIFYONLY` đạt và đã restore thật sang database rehearsal riêng. Backup được giữ tại [VideoFactory_pre_4.1.8_tiktok_multi_20260909_223109_9e57abe5.bak](<D:/SQL2019/Microsoft SQL Server/MSSQL15.MSSQLSERVER/MSSQL/Backup/VideoFactory_pre_4.1.8_tiktok_multi_20260909_223109_9e57abe5.bak>) (4.646.400 byte).
- Trên bản sao đầy đủ: migration chạy hai lần, kiểm tra dữ liệu cũ bằng so sánh hai chiều các cột trước migration (ngoại trừ rowversion), `DBCC CHECKDB` đạt. Database rehearsal đã được dọn sau khi áp dụng và kiểm tra thành công.
- Trên database đích: migration nằm trong transaction ngoài; chỉ commit sau khi kiểm tra dữ liệu connections/jobs/OAuth/app credentials/Data Protection không thay đổi. Sau áp dụng: 1 kết nối, 2 job, 1 Data Protection key, 0 publish attempts; giữ các schema version không liên quan.
- Index unique UserId cũ đã bỏ; index identity user/app/OpenId, lịch sử, worker due và request idempotency đều hoạt động. Năm FK mới và hai check constraint đều được bật, trusted.
- SHA-256 migration đã đối chiếu trước rehearsal và trước áp dụng: `F2E1E6B48CECC8094F2E28A1253DF3634A6F3EA87783770A870284DE2680F220`.

Lần vận hành SQL giữ cờ `TikTok:MultiAccountEnabled=false`. Theo yêu cầu tiếp theo của người dùng, đã bật cờ thành `true` trong `TOOL-SERVER/appsettings.json` của workspace. Không có ghi đè cờ trong `appsettings.Development.json`, User Secrets hoặc biến môi trường Process/User/Machine được kiểm tra. Server đang chạy từ checkout `Branch-Tool-Sub`, khác workspace đang sửa; cần chạy server từ workspace này để áp dụng cấu hình và mã nhiều tài khoản. Chưa restart server đang chạy, chưa xác minh cờ trên runtime, chưa đăng video thật hoặc phát hành. Người dùng đã báo luồng đăng hiện tại hoạt động; kết quả đó không được tính là smoke nhiều tài khoản của agent.
