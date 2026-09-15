# Triển khai lên lịch tạo video và xuất bản

Ngày cập nhật: 2026-09-11. Đã triển khai source; chưa chạy migration trên database đích, chưa bật worker hoặc nghiệm thu đăng thật.

## Nghiệp vụ đã triển khai

Menu trái **Lên lịch xuất bản** mở trang quản lý lịch, tài khoản, lượt chạy và lịch tháng. Người dùng nhập tiêu đề, mô tả hành động, chọn ảnh nhân vật và ảnh sản phẩm, chọn tài khoản đăng, ngày/giờ, múi giờ và các thứ trong tuần. Một lịch hỗ trợ một giờ đăng mỗi ngày được chọn, tối đa 90 ngày; có thể tạo nhiều lịch cho nhiều giờ.

Mỗi lượt tạo một video ngắn Veo 3.1 từ hai ảnh; chọn 4/6/8 giây, mặc định 8 giây. YouTube/TikTok hỗ trợ 9:16 hoặc 16:9; Facebook Reels dùng 9:16. Nội dung dùng lại mô tả của lịch, không sinh kế hoạch chủ đề mới mỗi ngày; chưa có video nhiều cảnh 30–60 giây, TTS hay lời thoại được kiểm chứng. Âm thanh theo chế độ video ngắn hiện hành.

Lưu lịch tạo **bản nháp**, chưa phát sinh chi phí. **Kích hoạt** yêu cầu xác nhận riêng cho phép tự tạo/duyệt ảnh đầu cảnh và tạo video theo giới hạn tổng báo giá USD/lượt. Đây là quyền tự động hóa được người dùng giao cho lịch, có thời điểm đồng ý trong snapshot từng lượt; không được ghi nhận như việc người dùng đã xem ảnh/video cụ thể. Budget tổ chức, role, credential, rate và policy vẫn được kiểm tra tại bước tạo có phí. Giới hạn báo giá không phải cam kết actual usage không bao giờ vượt estimate.

Server bắt đầu chuẩn bị trước giờ đăng 15–1.440 phút, mặc định 120 phút. Đến giờ đăng:

- Facebook: đăng Reels lên Page đã cấp quyền tạo nội dung; không hỗ trợ trang cá nhân/group.
- YouTube: đăng lên kênh được kết nối, theo quyền riêng tư và khai báo dành cho trẻ em; khai báo nội dung tổng hợp bằng AI.
- TikTok: video chuyển sang chờ xem trước. Người dùng xem đúng file/hash, chọn privacy từ Creator Info hiện hành, sửa caption/khai báo thương mại, đồng ý điều khoản và xác nhận đăng. Các đích Facebook/YouTube cùng lượt vẫn có thể tiếp tục khi TikTok chờ duyệt. Quy trình dựa trên [hướng dẫn chia sẻ nội dung của TikTok](https://developers.tiktok.com/docs/en/content-sharing-guidelines).

Chính sách trễ **Trong ngày** cho phép gửi trước nửa đêm theo múi giờ lịch; **Bỏ qua** dừng thao tác đăng mới sau giờ hẹn 5 phút. Không tạo lại các lượt đã lỡ hạn, không bù chi phí quá khứ khi kích hoạt lại. Nền tảng xử lý bất đồng bộ nên không cam kết bài xuất hiện công khai đúng phút đã chọn.

Worker thuộc server nhưng vẫn phải kiểm session/device/license lease hiện hành. Cần giữ app đăng nhập để duy trì lease; hết quyền thì lượt chuyển **Cần xử lý**, không bỏ qua kiểm license để chạy nền. Khi tiếp tục, snapshot lịch cũ và checkpoint đã lưu được giữ nguyên.

## Task triển khai

| Task | Kết quả source | Nơi triển khai |
|---|---|---|
| P01 — DTO và mô hình dữ liệu | Hoàn thành | `TOOL-SHARED.Contracts/Publishing`, `TOOL-SERVER/Publishing/PublishingModels.cs` |
| P02 — Migration, quyền SQL | Có migration mới; chưa áp database thật | `database/VideoFactory.4.1.10.PublishingSchedules.sql` |
| P03 — Tạo/sửa/bật/tạm dừng lịch | Hoàn thành; revision, snapshot, user/org scope | `PublishingService`, `PublishingCalendar`, `PublishingController` |
| P04 — Worker và khôi phục | Hoàn thành; unique lượt, lease, checkpoint, giờ/zone/DST | `PublishingWorker` |
| P05 — Tạo video từ nhân vật/sản phẩm | Hoàn thành đường gọi gateway hiện hành | `PublishingProduction`, `ShortVideoOutfitService` |
| P06 — Kết nối và xuất bản | Có OAuth Google/Meta, resumable YouTube, Reels Page, duyệt TikTok | `PublishingSocialService`, `PublishingPublisher`, `PublishingReviewService` |
| P07 — Media và retention | Kiểm signature/hash/codec/audio/ratio; dọn cache hết hạn | `PublishingMedia`, `PublishingRetention` |
| P08 — Native bridge và giao diện | Hoàn thành; menu, form, lịch tháng, lịch sử, preview/duyệt | `TOOL-LOCAL/Publishing`, `Web/src/features/publishing` |
| P09 — Kiểm thử tự động | Xem kết quả xác minh bên dưới | `TOOL-TESTS/Publishing`, frontend test |
| P10 — Rollout và nghiệm thu thật | Chờ môi trường đích và quyền tác động | Database clone, OAuth apps, quota/chi phí, tài khoản thử |

## Đường gọi và dữ liệu

`PublishingPage → publishing.* → PublishingWebBridge → ServerGenerationClient → /api/publishing → PublishingService → PublishingDispatcher → PublishingProduction/PublishingPublisher`.

Schema `social` gồm schedules, runs, deliveries, images, connections và OAuth sessions. Desktop role bị DENY schema này; thao tác chỉ qua API. Lịch và lượt theo owner/org; tài khoản mạng xã hội thuộc user. Ảnh mã hóa với purpose gắn user/org/image, token mã hóa gắn user/connection/platform. OAuth state lưu hash, hết hạn 10 phút, dùng một lần và kiểm lại session/quyền trước đổi code.

Project kỹ thuật dùng `WorkspaceRelativePath=publishing/<run-id>`, không xuất hiện trong danh sách project workspace desktop. Dùng project/quote/request ID bền vững, quote ảnh + quote video, reservation/settlement và worker provider hiện hành. Tách prompt sản phẩm khỏi nghiệp vụ mặc trang phục, giữ ảnh Approved/current cùng lineage cho Veo.

MP4 được sao chép vào `.part`, kiểm SHA-256, signature, H.264/AAC, thời lượng/kích thước và FFprobe đã pin hash trước promote. Preview và upload kiểm lại đúng bytes; DOM không nhận path, token hoặc URL output gốc. Outbound mạng xã hội chỉ HTTPS/443 exact-host, tắt redirect/proxy/cookie/log HTTP và pin DNS qua resolver hiện hành.

Mỗi đích đăng có checkpoint riêng. YouTube đối soát offset/ID cũ để khôi phục upload; không gửi lại init sau kết quả mơ hồ. Facebook/TikTok có ID/phiên upload đã lưu. **Unknown** cần người vận hành kiểm kết quả trên nền tảng; không có nút đăng lại mù. Hủy không thu hồi bài đã được nền tảng nhận hoặc hoàn tiền request AI đã gửi.

Cache MP4 riêng của lịch lưu đến 7 ngày sau deadline; ảnh đầu vào hết hạn sau 460 ngày. Cleanup chạy mỗi giờ khi `Publishing:Enabled=true`, kể cả khi WorkerEnabled=false hoặc EmergencyDisabled=true; giữ lịch sử, ID bài, ledger. Cache preview desktop được dọn file thuộc user/org hiện hành quá 7 ngày khi mở preview tiếp theo. Tắt Enabled hoàn toàn cũng dừng cleanup vì database có thể chưa có schema. Kho output provider vẫn theo retention hiện hành của module generation.

## Cấu hình server và rollout

1. Xác minh instance/database, backup và restore thử theo `database/AGENTS.md`; chạy chuỗi migration trên clone hai lần, kiểm FK/index/version và desktop DENY. Sau khi có phê duyệt mới áp `4.1.10-publishing-schedules` lên database đích. Migration yêu cầu schema video ngắn 4.1.9; không seed giá, secret hoặc tự bật tính năng.
2. Triển khai đồng bộ contracts/server/desktop/frontend. Giữ `Publishing:Enabled=false`, `WorkerEnabled=false` trong lần cài đầu. Server đang chạy của người dùng chưa được dừng hay thay thế trong phiên này.
3. Cấu hình `Publishing:MediaRoot` là đường dẫn tuyệt đối local, tài khoản chạy worker có quyền đọc/ghi và đủ dung lượng. Cấu hình `FfprobePath`, `FfprobeSha256` từ executable đã có provenance/license/hash được rà soát; không lấy executable từ DOM. Các replica xử lý cùng database phải truy cập cùng kho media tại cùng đường dẫn local/mount; không trỏ UNC hoặc symlink. Dùng một worker khi chưa có topology đáp ứng điều kiện này.
4. Xác minh AI image edit và Fal/Veo 3.1 theo policy `LongForm`, 720p/Native Audio, cờ `Generation:ShortVideoCharacterOutfit:Enabled`, credential, rate Active, budget > 0, output worker và output cache hiện hành. Lịch không tự sửa policy hoặc đoán rate còn thiếu.
5. Google: cấu hình server `Publishing:YouTube:{Enabled,ClientId,ClientSecret,RedirectUri}`; bật YouTube Data API, consent screen và grant `youtube.upload` + `youtube.readonly`, offline refresh token. Redirect URI là HTTPS/443 có route `/api/publishing/oauth/callback`, đăng ký exact URI trên ứng dụng Google. Secret chỉ cấu hình qua secret store server. Chỉ bật `PublicPostingApproved` khi ứng dụng đủ điều kiện; thử private trước. [OAuth web server](https://developers.google.com/identity/protocols/oauth2/web-server), [resumable upload](https://developers.google.com/youtube/v3/guides/using_resumable_upload_protocol).
6. Meta: cấu hình server `Publishing:Facebook:{Enabled,ClientId,ClientSecret,RedirectUri,ApiVersion}`. Chọn phiên bản Graph API được app hỗ trợ; source không đoán phiên bản triển khai. Cấp `pages_show_list`, `pages_read_engagement`, `pages_manage_posts`; Page cần CREATE_CONTENT/MANAGE. Xác minh App Review/access level trước `PublicPostingApproved=true`. Hiện truy vấn tối đa 100 Page được cấp quyền. Bộ lọc media áp thông số trong [mẫu Reels chính thức của Meta](https://github.com/fbsamples/reels_publishing_apis/blob/main/fb_reels_publishing_api_sample/README.md); vẫn cần nghiệm thu API version/account đích.
7. TikTok: giữ cấu hình/rollout/credential và menu kết nối hiện hành, scope `video.publish`. Duyệt video cụ thể trong lịch; không tự chọn privacy thay người dùng. App audit/quyền công khai vẫn do TikTok quyết định.
8. Bật Enabled để kiểm GET state, lưu bản nháp, kết nối trên tài khoản thử. Sau khi có ngân sách thử và quyền tác động mới bật WorkerEnabled, tạo một lịch tương lai, kiểm một reservation/settlement mỗi bước, media/hash, đúng đích/giờ, status cuối và bài thực tế. Thử restart, mất mạng, hết lease, bỏ lỡ giờ, ngắt kết nối, pause/cancel; kiểm không tạo hay đăng trùng.

Khi cần dừng khẩn cấp, đặt `EmergencyDisabled=true`; cleanup vẫn chạy khi Enabled=true. Worker generation/TikTok hiện hành tiếp tục đối soát tác vụ đã nhận theo cấu hình riêng. Không xóa run/delivery/quote/request/ledger hoặc hạ binary về phiên bản không hiểu checkpoint. Đối soát các upload Unknown trước khi cho phép tạo lượt mới tương tự.

## Xác minh và giới hạn

Restore solution, npm ci, build Release và frontend production build đã đạt. Server build được chuyển riêng vào `.tmp/publishing-validation/server` bằng MSBuild targets vì tiến trình server đang chạy khóa DLL ở output mặc định; không dừng tiến trình của người dùng.

Kết quả cuối ghi ở `KIEM_THU_VA_NGHIEM_THU.md`; TRX trong `.tmp/publishing-validation`. Kiểm thử dùng fake HTTP/time, database InMemory/SQLite tạm và media fixture. Migration mới chỉ có kiểm model/schema/index tĩnh và unique index trên SQLite, chưa có SQL Server rehearsal cho migration 4.1.10. Chưa thử OAuth thật, chi phí AI thật hoặc bài đăng thật; không coi các test model/SQL bị Skipped là đạt.

Frontend: 212 Passed / 0 Failed / 0 Skipped. WebView2 với App production và bridge giả kiểm menu, thẻ thống kê, không tràn ngang ở 1440/1024 px và zoom 125%; đã xem ảnh trang lịch/form tạo lịch trong `.tmp/publishing-validation/ui`. Harness chặn toàn bộ network ngoài virtual host, không kết nối app/server hoặc tài khoản thật.

Full .NET cuối: 1.348 Passed / 1 Failed / 5 Skipped. Lỗi duy nhất là đo căn chữ trong test UI Vietsub; chạy lại bài đó cùng nhóm lịch trên cùng binary đạt 59 Passed / 0 Failed / 0 Skipped. Không sửa/giảm assertion hoặc bỏ test để đạt kết quả. Hai lượt toàn bộ trước đã qua; cần theo dõi riêng độ ổn định của phép đo UI đó. Các test nghiệp vụ lịch mới đã qua, gồm khôi phục project ID đã giữ trước khi tạo project thật.

Máy có giới hạn RAM/commit nên chạy frontend một worker và .NET theo collection tuần tự, không chạy hai bộ đồng thời. TEMP/TMP .NET dùng thư mục thử riêng có đường dẫn ngắn trên D. Lượt mặc định song song bị hết bộ nhớ và build output server mặc định bị khóa không được tính Passed; giữ nguyên assertion để chạy lại trong môi trường phù hợp.
