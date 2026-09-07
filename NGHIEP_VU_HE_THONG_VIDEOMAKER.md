# Nghiệp vụ hệ thống VideoMaker

> Nguồn sự thật nghiệp vụ hiện hành. Rà soát ngày 2026-09-06.

## 1. Mục tiêu và phạm vi

VideoMaker hỗ trợ một người dùng có license hợp lệ tạo dự án, sinh nội dung/video bằng AI theo chính sách của tổ chức, duyệt clip và dựng media cục bộ. Server sở hữu auth, tổ chức, chi phí, credential, provider request và registry dùng chung; desktop sở hữu trải nghiệm biên tập, workspace và xử lý media local.

Không có mô hình BYOK. Provider key thuộc tổ chức và chỉ được quản trị trên server.

## 2. Vai trò và quyền

| Role | Quản lý thành viên | Budget/usage | Credential | Phát sinh AI |
|---|---:|---:|---:|---:|
| `Owner` | Có | Có | Có | Có |
| `OrganizationAdmin` | Có | Có | Có | Có |
| `BillingManager` | Không | Có | Không | Có |
| `Member` | Không | Không | Không | Có |
| `Viewer` | Không | Không | Không | Không |

Quy tắc bổ sung:

- Chỉ Global Admin tạo tổ chức và quản lý bảng giá toàn cục.
- Chỉ Owner quản lý Owner; không được vô hiệu hóa/xóa Owner Active cuối cùng.
- Người dùng phải là thành viên Active của tổ chức hiện hành.
- Một người dùng có thể thuộc nhiều tổ chức, nhưng mỗi request chỉ thuộc đúng một tổ chức.
- Project phải thuộc organization và người gọi phải có quyền trên project đó.

## 3. Tài khoản, license, thiết bị và tổ chức

Một phiên dùng AI hợp lệ cần đồng thời:

1. JWT còn hạn và đúng issuer/audience.
2. Session chưa bị thu hồi.
3. Device claim khớp thiết bị đã đăng ký.
4. License lease còn hiệu lực.
5. Organization membership và role hợp lệ.
6. Project thuộc đúng organization/user theo nghiệp vụ.

License, session hoặc membership bị thu hồi phải chặn request mới. Task đã gửi provider được worker theo dõi và quyết toán theo snapshot ban đầu, không chuyển sang tổ chức hay credential khác.

## 4. Thanh toán SePay và phân bổ seat

- SePay là integration tùy chọn và mặc định tắt.
- Payment order phải có mã chuyển khoản duy nhất, số tiền kỳ vọng, thời hạn và trạng thái rõ ràng.
- Webhook được xử lý idempotent; giao dịch trùng không được cấp license/seat hai lần.
- Chỉ đối sánh khi nội dung, số tiền, trạng thái và điều kiện nghiệp vụ hợp lệ; dữ liệu mơ hồ phải chuyển xử lý thủ công.
- Thanh toán thành công có thể tạo tổ chức và Owner hoặc bổ sung quyền/seat theo package đã snapshot.
- Phân bổ user vào tổ chức phải kiểm tra số seat và khóa cạnh tranh; không vượt quá capacity.
- Giao dịch đến muộn, hoàn tiền, chargeback hoặc lỗi provisioning cần trạng thái có thể đối soát; không sửa ledger bằng thao tác ad-hoc.

## 5. Credential provider

- Mỗi tổ chức có tối đa một credential `Active` cho mỗi provider.
- Credential mới phải được test qua server trước khi ghi.
- Server mã hóa secret bằng ASP.NET Core Data Protection và chỉ hiển thị hint.
- Rotation theo vòng đời `Active -> Retiring -> Revoked`.
- Task đang chạy giữ `CredentialVersionId` đã snapshot; rotation không đổi credential giữa chừng.
- Không trả plaintext/encrypted payload cho desktop, không ghi secret vào log hoặc response lỗi.

## 6. Pricing, budget và usage

- Global Admin cấu hình rate theo provider/model/đơn vị/thời gian hiệu lực.
- Không tự suy đoán hoặc hard-code giá provider. Không có rate phù hợp phải trả `pricing_not_configured` trước outbound.
- Mỗi project/request snapshot provider, model, policy và rate dùng để ước tính/settle.
- Budget tháng của tổ chức và hạn mức thành viên được kiểm tra trước outbound. Giá trị `0` nghĩa là khóa AI.
- Reservation, settlement và release chạy trong transaction cô lập `Serializable`.
- Thành công quyết toán theo usage thực tế trong giới hạn hợp đồng; thất bại cuối giải phóng reservation theo quy tắc.
- Mỗi ledger entry phải truy được organization, user, project, request, model, provider request, credential version và rate snapshot.

## 7. Idempotency và trạng thái request

- Idempotency key có phạm vi tổ chức và operation; cùng key/cùng payload trả lại kết quả hiện hành.
- Cùng key nhưng payload khác phải bị từ chối.
- Retry mạng không được tạo hai provider request hoặc hai khoản giữ ngân sách.
- Worker claim bằng lease, cho phép khôi phục sau crash nhưng không poll/settle song song cùng task.
- Trạng thái terminal không được quay lại trạng thái đang chạy.

## 8. Workflow video dài

### 8.1 Khởi tạo

Người dùng chọn tổ chức và tạo project `LongForm`. Project snapshot video provider/model/policy, resolution, Native Audio và các tùy chọn liên quan. Đổi policy tổ chức sau đó chỉ tác động project mới, không âm thầm đổi project đang làm.

### 8.2 Nội dung có cấu trúc

OpenAI tạo content plan, kịch bản/cảnh, nhân vật và prompt theo contract có schema. Với Kling/Fal `LongForm`, nội dung đầu vào theo chính sách hiện hành là tiếng Việt `vi-VN`; không áp quy tắc này cho BytePlus hoặc `DirectShortVideo`.

Mỗi scene phải giữ liên kết rõ với nội dung, nhân vật, tài sản và generation. Kết quả JSON sai schema hoặc vi phạm policy không được ghi như thành công.

### 8.3 Nhân vật và tài sản

- Character identity và ảnh tham chiếu phải thuộc đúng project.
- Cảnh một nhân vật có lời trực diện dùng `OnCameraDialogue`.
- `NativeVoiceOver` chỉ dành cho B-roll không gắn nhân vật.
- `Background`, `Prop` và `Item` hiện là tài sản text-only trong workflow này.
- Scene có asset assignment phải có đúng một `Background`; mọi asset được dùng phải ở trạng thái khóa/hợp lệ.

### 8.4 First frame cho Fal/Veo

Fal/Veo `LongForm` yêu cầu `SceneFirstFrame`:

- thuộc đúng project/scene/generation;
- là bản current và đã Approved;
- đúng tỷ lệ/resolution policy;
- được lấy qua asset đã kiểm soát của server.

Không gửi ảnh identity vuông trực tiếp làm input Veo và không fallback sang Text-to-Video khi first frame không hợp lệ.

### 8.5 Sinh video, polling và duyệt

Server giữ budget, gửi request provider, lưu snapshot và worker tiếp tục polling kể cả khi desktop đóng. Khi hoàn tất, server tải/cache output theo allowlist; desktop chỉ nhận URL proxy tương đối.

Desktop tải bằng file `.part`, kiểm tra media/hash rồi yêu cầu người dùng nghe/xem và Approve hoặc Reject. Render cuối chỉ lấy clip thuộc đúng `ApprovedGenerationId` và kiểm lại video stream, audio, duration và hash.

### 8.6 Native Audio và retry

Native Audio là workflow mặc định. TTS/WAV chỉ còn vì tương thích và không được dùng làm fallback ngầm.

Nếu output bị `NativeAudioInvalid`, retry là request provider có phí mới. Desktop phải yêu cầu xác nhận người dùng; recovery profile do server/policy quyết định và vẫn qua pricing/budget/idempotency.

## 9. Workflow video ngắn

Project `DirectShortVideo` gửi nội dung trực tiếp theo contract được hỗ trợ. Workflow hiện hành chỉ dùng Kling và không gọi OpenAI để viết lại prompt/nội dung. Không áp quy tắc content tiếng Việt dành riêng cho `LongForm` một cách máy móc lên luồng này.

## 10. Quy tắc theo provider

- **Kling:** provider video mặc định; hỗ trợ luồng Native Audio và video ngắn theo catalog/policy.
- **BytePlus:** adapter có trong source nhưng catalog mặc định tắt; rollout độc lập và không dùng quy tắc first-frame của Fal.
- **Fal/Veo:** catalog mặc định tắt; chỉ `LongForm`, yêu cầu approved/current first frame và không fallback T2V.
- Mỗi provider cần rate, credential Active, model Enabled và organization policy cho phép trước outbound.
- Không tự failover giữa provider vì điều đó thay đổi giá, dữ liệu gửi đi và semantics của project.

## 11. Vietsub local

Vietsub là module local-first:

- Server giữ `vs.Projects` như registry metadata; không nhận subtitle/media/workspace database.
- Desktop giữ manifest JSON, SQLite `project.db`, media, OCR/SRT và artifact trong workspace.
- Nút **Dịch tiếng Việt** luôn hiện. Nếu không có active OCR track có cue, UI yêu cầu quét OCR và không tạo translation job.
- CTA hiện hành chỉ nhận track `PADDLE_OCR_LOCAL`, ngôn ngữ `en` hoặc `zh`, có cue và revision khớp.
- Worker Qwen x64 chạy qua IPC local, không có provider client, credential hay database workflow.
- Cue manual/locked không bị ghi đè. Output stale, sai revision hoặc invalid không được apply; SRT ghi atomically.
- Readiness phải khớp model/worker/protocol/config/backend/native fingerprint và qua probe runtime/Anh/Trung.
- Resource profile được native chọn và snapshot theo job. Job đang chạy/resume không tự đổi giữa Standard và Low-memory; cache/fingerprint phải tách theo profile.
- Ngưỡng RAM tổng, RAM trống và commit của profile là mức khuyến nghị. Nếu dưới ngưỡng hoặc không đọc được snapshot, hệ thống phải cảnh báo và chưa được tạo job/nạp worker cho tới khi người dùng bấm **Vẫn tiếp tục**; xác nhận được snapshot vào job. Xác nhận không được bỏ qua Windows x64, dung lượng đĩa, checksum/probe model hoặc lỗi worker thực tế.
- Feature dịch local giữ mặc định tắt cho đến khi model thật, benchmark và smoke desktop đạt.
- Tạo giọng local là thao tác riêng sau dịch. Chỉ track hiện hành có revision khớp và toàn bộ cue có nội dung dịch tiếng Việt mới được tạo giọng; trạng thái cảnh báo/chất lượng bản dịch không chặn tạo giọng và không có fallback Cloud ngầm.
- MVP dùng một giọng Việt Piper CPU đã pin model/config/SHA-256. Runtime Python và worker chạy cô lập, không nhận provider credential, URL tùy ý hoặc đường dẫn output từ WebView.
- Phrase, WAV và timeline được cache theo nội dung/cấu hình/revision. File tạm dùng hậu tố `.partial`, phải qua kiểm tra RIFF/PCM, kích thước và SHA-256 trước khi ghi artifact hiện hành.
- Timeline giọng Việt đã tạo phải hiển thị thành track riêng dưới timeline phụ đề, dùng chung playhead/play-pause/seek/tốc độ với video; không hiển thị bằng audio player độc lập trong panel thiết lập.
- Hệ thống được mượn khoảng trống kế tiếp và tăng tốc tối đa `1.20x`. Phrase cần nhanh hơn ngưỡng này vẫn được dựng ở tốc độ tối đa, không làm job thất bại và không cắt câu cuối; hệ thống lưu timing diagnostic để cảnh báo khả năng chồng âm hoặc timeline dài hơn video.
- Sửa cue hoặc đổi revision làm timeline cũ không còn được phát như output hiện hành; playback URL nội bộ chỉ mở artifact đúng project/track/revision/hash.
- Feature tạo giọng local giữ mặc định tắt cho đến khi runtime/model thật, kiểm kê license/dependency, smoke và nghe nghiệm thu trên bundle phát hành đạt.

## 12. Bảo mật dữ liệu và output

- Provider outbound chỉ qua HTTPS đến host allowlist source hiện hành.
- Output proxy xác minh authorization, project ownership, scheme/host/DNS, redirect, MIME và kích thước; không lộ signed URL gốc.
- Không log secret, Authorization header, signed URL, Base64 hoặc nội dung nhạy cảm đầy đủ.
- Desktop không được ghi credential, provider request hay usage ledger.
- Path local phải chuẩn hóa, dùng relative path trong workspace root và chống path traversal/symlink escape.

## 13. Điều kiện được coi là hoàn tất

Một tính năng chỉ được tuyên bố sẵn sàng khi đồng thời có:

1. Source và migration/contract đồng bộ.
2. Test tự động phù hợp đạt, `Skipped` được giải thích.
3. Cấu hình/rate/credential của môi trường đã xác minh mà không lộ secret.
4. Smoke thủ công end-to-end đạt trên đúng bundle/môi trường.
5. Quan sát, rollback và runbook khả dụng.

Trạng thái hiện hành và các hạng mục còn mở được ghi tại [BOI_CANH_HE_THONG_HIEN_HANH.md](BOI_CANH_HE_THONG_HIEN_HANH.md).
