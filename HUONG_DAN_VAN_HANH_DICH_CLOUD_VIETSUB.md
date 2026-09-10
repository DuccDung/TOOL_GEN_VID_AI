# Vận hành Dịch Cloud Vietsub

> Source triển khai ngày 2026-09-09, trên worktree HEAD `2b4171c`. Ngày 2026-09-10 đã backup, restore thử, chạy migration hai lần trên clone và áp 4.1.6 vào local `DUNGDEV / VideoFactory`. Cloud vẫn tắt; chưa gọi OpenAI có phí hoặc rollout môi trường khác. Bằng chứng: [.tmp/cloud-database-20260910_003325/REPORT.md](.tmp/cloud-database-20260910_003325/REPORT.md).

## Trải nghiệm và đường gọi

Người dùng mở **Dịch tiếng Việt → Dịch Cloud**. Desktop lưu bản nháp trước, đọc toàn active OCR track `en`/`zh`, chọn câu thiếu/lỗi/stale và giữ cue manual/locked hoặc bản dịch hợp lệ. Không có bộ chọn provider/model, ô API key hoặc xác nhận phí bổ sung. Cloud không yêu cầu cài Qwen hay đáp ứng cảnh báo RAM Local.

`useVietsubModule` → `VietsubWebBridge` → `VietsubCloudTranslationService/JobExecutor` trên desktop → `VietsubCloudTranslationsController` → service/worker server → `OpenAiSubtitleTranslationClient`. Qwen worker không thay đổi. Desktop chỉ dùng AccountSessionManager/LicenseSessionManager gọi API server.

## Chuẩn bị môi trường

Thực hiện quy trình xác minh instance/database, backup và restore trong [runbook chính](VAN_HANH_VA_PHAT_HANH.md). Áp chuỗi migration đến [4.1.6](database/VideoFactory.4.1.6.VietsubCloudTranslation.sql) trên clone trước, chạy lại để kiểm idempotency, kiểm FK/CHECK/index và so sánh ledger cũ. Không dùng kết quả SQLite thay cho rehearsal SQL Server.

4.1.6 thêm `vs.CloudTranslationJobs`, `CloudTranslationBatches`, `CloudTranslationAttempts`. `ai.BudgetReservations` và `ai.UsageLedger` giữ `ProjectId`/FK video cũ nhưng cho nullable, thêm `VietsubProjectId`/FK registry và CHECK đúng một loại project. Không backfill dự án giả, không đổi lịch sử phí. Desktop vẫn chỉ có SQL workflow `vf`.

**Áp migration trước khi chạy server binary mới, kể cả Cloud đang tắt**, vì model budget/ledger mới đọc cột `VietsubProjectId`. Chỉ mục version là `ai.SchemaVersions`, giá trị `4.1.6`; không nhầm với schema version `vf`.

Cấu hình server, không đặt trong desktop:

```json
{
  "VietsubCloudTranslation": {
    "Enabled": false,
    "ModelCode": "",
    "MaximumOutputTokens": 6000,
    "RequestTimeoutSeconds": 120,
    "MaximumConcurrentJobs": 2,
    "MaximumConcurrentJobsPerOrganization": 1
  }
}
```

- Global Admin chọn `ModelCode` từ catalog OpenAI Text đang enabled, có capability `structuredOutput=true`. Đây là policy riêng của subtitle, không thay default model của tạo nội dung video. Không mặc định model mới nhất hoặc giá từ tài liệu này.
- Quản trị tổ chức dùng luồng credential hiện hành: test rồi Active, mã hóa bằng Data Protection. Job pin credential version; rotation không đổi version của job đã nhận.
- Cấu hình rate InputToken/OutputToken đang hiệu lực, đơn giá dương, đơn vị `Token`/`1KTokens`/`MillionTokens`, currency khớp tổ chức. Rate thiếu trả `pricing_not_configured`. Không có giá seed cho tính năng này.
- Tổ chức/member cần ngân sách; mức 0 khóa AI. JWT/session/device/license lease, active membership, quyền phát sinh phí và exact owner của project được kiểm trước outbound. Viewer không được dịch.
- Bảo đảm Data Protection key ring dùng chung giữa các server instance và tồn tại qua restart. Không log plaintext payload/key/header.
- Chỉ đặt `Enabled=true` và khởi động phiên bản server tương ứng trên môi trường đã được cho phép sau rehearsal/cấu hình/smoke. Readiness chưa đạt thì nút Cloud vẫn khóa; mở popup không gọi OpenAI.

## Job, dữ liệu và chi phí

Một job giữ snapshot bất biến của operation ID, track, input hash, model, prompt/schema, credential và rate. Mỗi batch tối đa 12 target, ngữ cảnh tối đa 3 cue mỗi phía. Payload lớn được tách tiếp theo kích thước JSON; một target/ngữ cảnh vẫn vượt giới hạn thì từ chối trước provider, không cắt mất text. Snapshot tối đa 5 MiB/20.000 cue. Output cap và timeout được chặn trong source, không nhận từ DOM.

HTTP chỉ đến `https://api.openai.com:443/v1/responses`, không redirect/proxy tùy ý. Responses dùng strict JSON Schema, `store=false`, không tools, `safety_identifier` hash user. Parser kiểm đầy đủ alias/thứ tự/số lượng, text trống, JSON/refusal/incomplete, markup và dung lượng response tối đa 1 MiB. Độ dài đọc/ngôn ngữ nghi vấn tạo cảnh báo để biên tập kiểm lại; đây không phải chứng nhận chất lượng dịch. `store=false` không đồng nghĩa zero retention phía OpenAI.

Quote dùng kích thước request đã serialize và output cap với biên bảo thủ; trước start kiểm tổng estimate, nhưng reservation chỉ giữ từng batch. Chi phí thực tính bằng usage và rate snapshot. Usage thiếu thì dùng estimate đã giữ, kể cả output không hợp lệ, theo policy server hiện hành; không tự ghi actual 0. Reservation/settlement/release dùng transaction Serializable và idempotency chung cho Video + Vietsub. Admin ledger hiển thị loại project tương ứng.

Worker dùng SQL application lock `VietsubCloudDispatch`, unique active-project và lease. Commit reservation trước `DISPATCHING`, commit result/usage trước settlement. Một vòng xử lý một batch; các quota là giới hạn tối đa toàn cụm/tổ chức. Job cũ giữ snapshot khi cấu hình/rate/key thay đổi. Mất session/license chặn batch mới dù desktop đã đóng.

Trạng thái server: `QUEUED`, `RUNNING`, `PAUSED`, `BLOCKED`, `UNKNOWN`, `COMPLETED`, `FAILED`, `CANCELLED`. Pause/cancel chặn batch tiếp theo; request đang ở upstream vẫn phải ghi nhận kết quả/chi phí. Poll/reconnect không tạo attempt. Thử lại do người dùng yêu cầu chỉ áp dụng batch thất bại đã quyết toán, tối đa 3 attempt/batch; không có vòng tự repair hoặc retry provider tự động.

Desktop giữ snapshot và operation ID trong thư mục job của workspace trước POST. Sau mất response hoặc mở lại, tìm cùng operation ID trước khi gửi. Retry download/apply/ACK dùng result cũ. Cue + receipt + checkpoint commit cùng transaction SQLite; CAS kiểm text/timing/speaker/updatedAt/lock/track. Kết quả stale bị bỏ qua, job báo phần chưa áp dụng; không báo hoàn tất tuyệt đối. SRT dùng file tạm và promote atomic, đăng ký artifact đúng revision trước ACK. Voice/export cũ bị invalid theo revision; không tự tạo giọng.

## Retention và đối soát

Payload/result được mã hóa bằng purpose `VideoMaker.VietsubCloudPayload.v1`, tách khỏi audit/ledger. Dọn input trong vòng khoảng 24 giờ sau terminal, cộng chu kỳ cleanup khoảng một phút; job kéo dài hơn 7 ngày cũng phải dọn payload. Result tối đa 7 ngày sau terminal, ACK có thể dọn sớm. Job treo/Unknown quá 7 ngày mất payload nhưng vẫn giữ metadata đối soát và reservation. Cleanup không xóa job/attempt/ledger. Snapshot local thuộc lịch sử workspace, không thuộc retention server.

`UNKNOWN` nghĩa là chưa biết upstream đã tính phí hay chưa. Không suy luận timeout = chưa gửi. Không tự resend, release reservation, đổi credential hoặc hủy lịch sử; credential retirement vẫn giữ version đang bị job tham chiếu. Lỗi bất ngờ sau `DISPATCHING` cũng được đưa về Unknown.

Global Admin đối chiếu batch RequestId, thời gian attempt, credential version và response ID nếu có với chứng từ provider. Việc xác minh nội dung/chứng từ diễn ra qua kênh quản trị phù hợp; không đưa key hoặc transcript vào log.

Khi đã có chứng từ xác định, API quản trị:

```text
POST /api/admin/vietsub/cloud-translations/{jobId}/reconcile
{
  "requestId": "<request-guid>",
  "decision": "CONFIRMED_USAGE",
  "evidenceReference": "INC-12345",
  "inputTokens": 123,
  "outputTokens": 45,
  "responseId": "resp_example"
}
```

Các số trên chỉ minh họa contract, không phải usage để áp dụng. `CONFIRMED_NO_CHARGE` chỉ dùng khi có bằng chứng không phát sinh phí, không gửi token fields. Endpoint kiểm live session/device và role Admin trong database, yêu cầu tham chiếu chứng từ an toàn, lưu evidence hash/audit trước hoặc cùng các bước bền vững. Gửi lại cùng nội dung idempotent; nội dung khác bị conflict. Đối soát đưa job về Failed để người dùng quyết định thử lại; endpoint không gọi provider.

Quản trị đọc metadata Unknown bằng kênh SQL chỉ đọc được phân quyền, ví dụ chọn ID/status/time/cost từ jobs/batches/attempts. Không xuất `ProtectedInput`/`ProtectedResult` hoặc credential payload. Nếu chứng từ không đủ, tiếp tục giữ Unknown; không tự chọn quyết định.

## Dừng rollout và phần còn phải nghiệm thu

Tắt `Enabled` chặn job/outbound mới. Worker vẫn giữ settlement, cleanup và đối soát; job chưa gửi bị Blocked, có thể resume sau khi điều kiện trở lại. Không drop bảng hoặc quay về binary reconciliation cũ khi đã có reservation Vietsub: bản cũ không hiểu request kind này và có thể release sai.

Trước rollout cần SQL Server clone/rehearsal concurrency thực, cấu hình một môi trường/tổ chức thử nghiệm được phép, OpenAI smoke có hạn mức, và người rà chất lượng Anh/Trung, tên riêng/xưng hô/độ dài. Test fake HTTP, SQLite và WebView2 chỉ chứng minh cơ chế phần mềm. Kết quả kiểm thử/build cụ thể được ghi trong [kế hoạch triển khai](KE_HOACH_TRIEN_KHAI_DICH_CLOUD_OPENAI.md) và [ma trận kiểm thử](KIEM_THU_VA_NGHIEM_THU.md).
