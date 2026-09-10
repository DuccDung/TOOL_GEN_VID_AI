# Vận hành và phát hành VideoMaker

### Veo local voice — bổ sung 2026-09-09

Rehearsal trên database clone được phê duyệt trước: áp dụng migration VideoFactory.4.1.8.LocalVoiceConsistency.sql idempotently sau baseline hiện hành, rồi triển khai server/desktop cùng contract. Mapping mới cần cột LocalVoicePolicyVersion kể cả khi feature tắt; không chạy binary mới lên schema cũ. Migration không phụ thuộc cloud LipSync thử nghiệm 4.1.6/4.1.7. Không xóa cột/policy/lineage khi rollback; desktop cũ không có render guard không được dùng để render project đã bật local policy.

Chỉ bật Features.VeoLocalVoiceConsistencyEnabled ở desktop nghiệm thu, khởi động lại, bật rõ trong project rồi cài runtime từ UI. Installer tải dependency có lock/hash và model ghim; cần mạng/dung lượng trống khi cài, không upload media. Inference chạy CPU và vẫn cần server authorization cho thao tác. Không phát hành profile GPU hoặc chứng nhận model chỉ dựa vào probe. Lưu license/provenance component, nghe thử tiếng Việt/âm nền/khẩu hình và rà soát quyền phân phối trước rollout; xem nhật ký triển khai.

Bổ sung 2026-09-10: cấu hình desktop trong repository đã bật flag theo yêu cầu triển khai. Máy này đặt `LocalVoice:ComponentRoot=D:\VideoMakerLocalVoice\v1`, `LocalVoice:TemporaryRoot=D:\VideoMakerLocalVoice\tmp` trong `appsettings.user.json` được Git bỏ qua; không đưa đường dẫn máy vào cấu hình phát hành. Có thể chuẩn bị component trước đăng nhập bằng `powershell -NoProfile -File scripts/Prepare-VeoLocalVoice.ps1 -Mode Prepare`; `-Mode Verify` kiểm byte và nạp lại model, `-Mode Status` chỉ đọc trạng thái. Ba lệnh đều chạy binary đã build và đọc cấu hình bên cạnh binary. Không ghi tay READY. Hướng dẫn máy đích và phần nghiệm thu còn lại: [HUONG_DAN_CHAY_DONG_NHAT_GIONG_VEO_LOCAL.md](HUONG_DAN_CHAY_DONG_NHAT_GIONG_VEO_LOCAL.md).

> Runbook chuẩn cho database, secret, provider, speech, SePay, desktop bundle và rollback. Rà soát ngày 2026-09-07.

Không chạy nội dung tài liệu này trên production nếu chưa xác định rõ instance/database, người phê duyệt, backup đã kiểm tra và phương án restore. Các giá trị trong dấu `<...>` là placeholder, không được commit secret thật.

## 1. Điều kiện trước mọi thay đổi môi trường

1. Xác nhận tên môi trường, SQL instance, database và application version đích.
2. Xác nhận người thực hiện có đúng quyền tối thiểu.
3. Tạo backup và thử khả năng đọc/restore trên môi trường phù hợp.
4. Chụp lại schema version, feature flag, provider policy và trạng thái worker.
5. Xác định cửa sổ triển khai, tiêu chí dừng và người quyết định rollback.
6. Không phát request provider có phí hoặc webhook thật trong bước rehearsal mặc định.

Server startup có bootstrap catalog và có thể ghi catalog vào database. Không khởi động binary mới trước khi chuỗi migration tương ứng đã hoàn tất.

## 2. Database

### 2.1 Thứ tự migration

Áp dụng theo đúng thứ tự:

```text
database/VideoFactory.Initial.sql
database/VideoFactory.4.0.0.OrganizationAiGateway.sql
database/VideoFactory.4.0.1.VietnameseSeedTextRepair.sql
database/VideoFactory.4.0.2.GptImageCharacterReference.sql
database/VideoFactory.4.0.3.SceneVoiceTts.sql
database/VideoFactory.4.0.4.BytePlusSeedance.sql
database/VideoFactory.4.0.5.SceneNativeAudioStatuses.sql
database/VideoFactory.4.0.6.NativeAudioWorkflowStatuses.sql
database/VideoFactory.4.0.7.ProjectAssetTextLibrary.sql
database/VideoFactory.4.0.8.AiGeneratedProjectAssets.sql
database/VideoFactory.4.0.9.FalVeoLongForm.sql
database/VideoFactory.4.0.10.LicenseSepayPayments.sql
database/VideoFactory.4.0.11.OrganizationSeatProvisioning.sql
database/VideoFactory.4.1.0.VietsubProjectRegistry.sql
database/VideoFactory.4.1.1.SceneFirstFrames.sql
database/VideoFactory.4.1.2.ProviderRequestFailureDetails.sql
database/VideoFactory.4.1.3.SpeechSynchronization.sql
database/VideoFactory.4.1.4.VoiceProfileApproval.sql
database/VideoFactory.4.1.5.SpeechVerificationReview.sql
database/VideoFactory.4.1.6.TikTokPublishing.sql
database/VideoFactory.4.1.7.TikTokAdminCredentials.sql
database/VideoFactory.4.1.8.TikTokMultiAccount.sql
```

Mỗi migration phải giữ tính idempotent theo thiết kế source. Không sửa lịch sử đã có khả năng được triển khai; tạo migration mới nếu cần đổi schema/data.

### 2.2 Chạy rehearsal

Ưu tiên bản sao database đã khử dữ liệu nhạy cảm. Ví dụ cho Windows authentication:

```powershell
sqlcmd -S "<sql-instance>" -d "<database-clone>" -E -b -f 65001 -i "database\VideoFactory.Initial.sql"
```

Sau đó chạy từng file migration còn lại theo thứ tự mục 2.1, vẫn dùng `-b -f 65001`. Với SQL authentication, đưa credential qua secret mechanism của CI/runner; không ghi password vào script, lịch sử shell hoặc tài liệu.

Sau migration:

- chạy lại migration để xác minh idempotency nếu runbook môi trường cho phép;
- chạy query/verify tương ứng, gồm `Verify.VideoFactory.4.0.11.OrganizationSeatProvisioning.sql` khi kiểm tra seat provisioning;
- build/test against clone;
- xác minh rollback từ backup trên rehearsal trước production.

### 2.3 Quyền desktop

Chạy `VideoFactory.DesktopLeastPrivilege.sql` bằng tài khoản quản trị phù hợp và map principal desktop theo hướng dẫn trong script. Desktop chỉ cần quyền workflow chuyển tiếp ở `vf`; không cấp trực tiếp quyền credential, usage ledger, `auth`, `ai`, `dbo` hay `vs`.

Khi workflow đã chuyển hết qua API, thu hồi role SQL desktop là một hạng mục migration/rollout riêng, không xóa connection tùy tiện trước khi đo đường gọi thực tế.

## 3. Secret và cấu hình server

### Development

```powershell
dotnet user-secrets set --project TOOL-SERVER "ConnectionStrings:VideoFactory" "<connection-string>"
dotnet user-secrets set --project TOOL-SERVER "Jwt:SigningKey" "<random-secret-at-least-32-bytes>"
```

SMTP password/App Password, admin bootstrap identity, SePay secret, signing key, production connection string và provider credential không được commit. Production dùng secret manager của môi trường và HTTPS certificate hợp lệ.

TikTok Client Key/Client Secret mới được nhập qua mục **Tích hợp TikTok** bởi Global Admin sau migration 4.1.7. Cấu hình `TikTok:ClientKey`/`TikTok:ClientSecret` chỉ còn là fallback legacy và không dùng cho rollout mới.

Data Protection key ring nằm trong database. Backup/restore phải giữ được key ring cùng encrypted credential; thử giải mã credential bằng health/admin flow sau restore mà không in secret.

## 4. Thiết lập AI Gateway theo tổ chức

Thực hiện theo thứ tự để không tạo outbound thiếu kiểm soát:

1. Global Admin xác minh provider/model catalog; giữ model mới `Disabled`.
2. Tạo rate có đơn vị, currency, hiệu lực và model mapping chính xác.
3. Owner/OrganizationAdmin nhập credential qua HTTPS Admin/API được phép; server test rồi mã hóa.
4. Cấu hình organization policy/provider/model.
5. Cấu hình organization budget và member limit; nhớ rằng `0` là khóa AI.
6. Tạo project thử nghiệm với user/role riêng.
7. Kiểm tra dry path/auth/pricing trước, sau đó mới cho phép một smoke có phí đã phê duyệt.

Không tự đoán giá từ website hoặc model name. Thiếu rate phải để hệ thống trả `pricing_not_configured`.

## 5. Rotation credential

1. Kiểm tra task đang `Queued/Submitted/Running` và credential version chúng đang dùng.
2. Nhập key mới qua Admin/API; server phải test thành công trước ghi.
3. Chuyển version mới thành `Active`, version cũ sang `Retiring`.
4. Theo dõi task cũ đến terminal; chúng tiếp tục dùng version đã snapshot.
5. Chỉ chuyển version cũ sang `Revoked` khi không còn task cần dùng và rollback window đã hết.
6. Xác minh log/response chỉ có hint, không có plaintext/encrypted payload.

Không rotate production chỉ để test giao diện.

## 6. Rollout provider

### Kling

- Xác minh catalog/model/resolution/Native Audio và rate.
- Smoke một project riêng; kiểm tra submit, polling sau khi đóng desktop, cache/proxy, audio review, approve và settlement.
- Test `NativeAudioInvalid`/retry bằng fixture hoặc môi trường được phép; retry thật là chi phí mới.

### BytePlus

- Giữ seed `Disabled` đến khi rate, credential, request/response mapping và output host thực tế đã được duyệt.
- Rollout theo tổ chức thử nghiệm, không bật toàn cục và không failover từ Kling.

### Fal/Veo

- Cần migration 4.1.1, first-frame workflow và policy `LongForm`.
- Xác minh first frame Approved/current, tỷ lệ đúng và không có T2V fallback.
- Bật catalog theo môi trường/tổ chức sau smoke image-to-video có phí được phê duyệt.

Mỗi provider có rollback flag/policy riêng. Tắt provider chặn request mới nhưng worker vẫn cần xử lý an toàn task đã gửi.

## 7. Rollout Canonical Voice và speech verification

Từ thay đổi loại bỏ lip-sync Cloud ngày 2026-09-10, triển khai server và desktop cùng phiên bản: server cũ vẫn chặn Canonical thoại nhân vật. Không cần migration dữ liệu để tiếp tục project chờ cũ; dashboard đối chiếu WAV/voice version hiện hành, sau đó thao tác tạo/duyệt ghi trạng thái theo workflow thông thường. Giữ nguyên migration 4.1.6/4.1.7 lip-sync và dữ liệu lịch sử; chúng không còn là bước cài bắt buộc của hai luồng hiện hành. Không DROP bảng hoặc xóa ledger/request cũ. Bản server/desktop đang chạy không tự được thay bằng kết quả build kiểm thử.

Điều kiện Canonical Voice:

- Migration 4.1.3–4.1.4 đã chạy lặp trên clone và áp đúng môi trường.
- OpenAI TTS credential/model/rate Active, budget giới hạn và storage/retention đủ dung lượng.
- Catalog server trả đúng 13 giọng hiện hành; alias legacy chỉ dùng để đọc project cũ.
- Voice preview/approval, content pacing, scene WAV, technical validation, audio mix/render và export đã smoke test.
- Server `CanonicalVoiceEnabled` và desktop `SpeechSynchronizationEnabled` được bật theo thứ tự canary; rollback bằng cách tắt flag đã được thử.

Smoke bắt buộc:

1. Mở/chọn modal không tạo request hoặc reservation.
2. Preview khi có project dùng đúng project; khi chưa có project dùng đúng một project kỹ thuật ẩn theo user+organization và không xuất hiện trong list/dashboard.
3. Quote/xác nhận xảy ra trước TTS outbound; phát lại WAV đã tải không tạo request mới.
4. Content plan đạt mục tiêu nhịp 85–95%; output ngoài biên 80–105% chỉ mở repair có quote, không tự gọi lần hai.
5. WAV qua MIME/SHA-256/sample rate/duration/audibility và đúng speech/voice snapshot.
6. `NativeVoiceOver` và `OnCameraDialogue` ghép toàn bộ WAV vào video nền; thoại nhân vật cần duyệt WAV trước. Nghe/duyệt clip đã ghép trước render. Không có module lip-sync Cloud.
7. Video dài `ProviderNativeVerified` không hiển thị/quote/gọi ASR và duyệt bằng audio hợp lệ cùng checklist nghe.

Speech verification là rollout độc lập cho workflow không phải `OpenAiStructuredPlan`: cần migration 4.1.5, transcription credential/model/rate và `SpeechVerificationEnabled`. `NeedsReview` phải có lý do/reviewer/timestamp; stale row version và `Failed` bị chặn.

## 7A. Rollout TikTok Direct Post

Với nhiều tài khoản, thực hiện [quy trình migration, bật cờ và rollback](TRIEN_KHAI_TIKTOK_NHIEU_TAI_KHOAN.md). Cấu hình workspace đã bật `TikTok:MultiAccountEnabled=true` theo yêu cầu người dùng sau khi áp migration vào `DUNGDEV / VideoFactory`. Khi triển khai sang môi trường khác, giữ cờ tắt cho đến khi migration, server và desktop cùng tương thích. Migration 4.1.8 vẫn bắt buộc cho server mới khi cờ tắt. Không hạ về server một tài khoản sau khi có nhiều connection. Tắt cờ chỉ ngăn mở rộng tài khoản, giữ vận hành các kết nối hiện có.

Item TikTok mặc định hiển thị trên desktop nhưng thao tác phía server vẫn tắt. Trước khi bật integration:

- apply/rehearsal tuần tự migration TikTok 4.1.6, 4.1.7 rồi 4.1.8, kiểm tra Data Protection key ring nằm trong backup/restore plan;
- đăng ký đúng loopback redirect URI cho Login Kit Desktop, có scope `video.publish`, hoàn tất app review/audit theo yêu cầu TikTok;
- Global Admin mở **Tích hợp TikTok**, nhập credential đã regenerate, bấm yêu cầu xác minh, rồi trong 15 phút đăng nhập đúng tài khoản Admin đó trên Desktop và hoàn tất **Kết nối TikTok**;
- dùng tài khoản test để smoke connect/reconnect/disconnect, creator-info, privacy/interaction/disclosure, chunk upload, retry, restart desktop và terminal status;
- xác nhận log/API/React không lộ token, app secret, signed upload URL hoặc absolute local path;
- sau OAuth thành công, credential chuyển `Pending -> Active` và integration được bật nhưng public posting vẫn ở `SELF_ONLY`;
- chỉ bật xác nhận public posting trong Admin sau khi có bằng chứng TikTok audit còn hiệu lực; dùng `TikTok:EmergencyDisabled=true` nếu cần dừng khẩn cấp.

Không chạy smoke đăng bài vào tài khoản thật hoặc bật public posting nếu chưa có phê duyệt môi trường và TikTok audit tương ứng.

## 8. Rollout SePay

SePay mặc định tắt. Trước khi bật:

- cấu hình bank code, account number/name, transfer prefix, QR endpoint và expiry theo môi trường;
- cấu hình và xác minh cơ chế xác thực webhook mà môi trường yêu cầu;
- chạy simulator local/staging tại `scripts/Test-SepayOrganizationProvisioning.ps1` với token test qua environment variable;
- test duplicate/concurrent webhook, sai amount/content, expired/late order, seat shortage và retry provisioning;
- xác minh ledger/payment/order có thể đối soát mà không log dữ liệu nhạy cảm;
- chỉ bật `Payments:Sepay:Enabled=true` sau sign-off.

Ví dụ test chỉ dành cho endpoint test đã xác nhận:

```powershell
.\scripts\Test-SepayOrganizationProvisioning.ps1 `
  -BaseUrl "https://localhost:7202/" `
  -LicensePlanId "<test-plan-guid>" `
  -Confirmation SEPAY_TEST_ONLY
```

Script không được hướng đến remote nếu chưa chủ động cho phép và xác minh đó là staging an toàn.

## 9. Desktop và Vietsub runtime

- Desktop trỏ tới HTTPS server URL đúng môi trường.
- SQL workflow dùng account/role ít quyền; không đóng gói production credential trong `appsettings.json`.
- FFmpeg/FFprobe phải nằm ở `tools/ffmpeg`, đúng win-x64, provenance và checksum.
- OCR/model/native runtime phải đúng bundle/fingerprint.
- Profile Low-memory 6 GB chỉ được bật trong bundle đã benchmark; không hạ thêm ngưỡng qua WebView, manifest project hoặc cấu hình người dùng.
- Giữ `VietsubLocalTranslationEnabled=false` đến khi verify model, benchmark và desktop smoke đạt trên chính bundle định phát hành.
- `VietsubLocalVoiceEnabled=true` chỉ mở UI/cài đặt. Máy thiếu runtime/model phải trả `NOT_INSTALLED`; chỉ rollout sau verify checksum/probe, kiểm kê license/dependency Python, benchmark CPU, nghe nghiệm thu và smoke timeline/playback.
- Không dùng marker `READY` từ máy/build khác.

## 10. Bundle FFmpeg và phát hành desktop

Bundle bắt buộc có `ffmpeg.exe`, `ffprobe.exe`, `LICENSE.txt`, `PROVENANCE.md` và `checksums.sha256`. Trước release:

```powershell
.\scripts\Test-FfmpegBundle.ps1 `
  -BundlePath ".\third_party\ffmpeg\win-x64" `
  -RequireReleaseApproval
```

Publish tạo artifact mới và từ chối release directory đã có file:

```powershell
.\scripts\Publish-DesktopRelease.ps1 `
  -Version "<semver>" `
  -BuildNumber <integer> `
  -Channel Stable `
  -ServerBaseUrl "https://<server>/"
```

Nếu dùng appsettings đóng gói riêng, truyền `-AppSettingsPath` đến file đã rà soát không chứa secret. `artifacts` là đầu ra tái tạo, không phải source of truth.

## 11. Smoke sau triển khai

- Login, refresh/revoke session, device/license lease.
- Chọn organization và xác minh matrix role, đặc biệt `Viewer`.
- Pricing missing, budget `0`, insufficient budget và idempotency conflict.
- Một request content; provider video chỉ khi được phép phát sinh phí.
- Canonical Voice/speech verification chỉ khi nằm trong scope rollout và đã phê duyệt chi phí.
- Poll khi desktop reconnect, proxy download, MIME/size/hash và approve/render.
- Admin credential hint/rotation không lộ secret.
- SePay chỉ nếu nằm trong scope rollout.
- TikTok chỉ nếu nằm trong scope rollout: OAuth, local direct upload, reconnect/poll và không lộ token/path/signed URL.
- Vietsub create/open/sync registry, OCR và translation chỉ nếu runtime đã được phê duyệt.
- Updater install/update/rollback trên máy sạch hoặc VM.
- Health/log/metrics không chứa token, prompt nhạy cảm hoặc signed URL.

## 12. Rollback

- Dừng request mới bằng feature flag/model/policy thay vì xóa dữ liệu đang chạy.
- Để worker settle/release task đã outbound; không xóa provider request hoặc usage ledger.
- Roll back application chỉ khi binary cũ tương thích schema mới; migration schema/data rollback cần kịch bản riêng đã rehearsal.
- Restore database chỉ theo quyết định sự cố, với đánh giá task/ledger phát sinh sau backup.
- Desktop updater dùng backup/rollback tích hợp và phải xác minh manifest/checksum trước phục hồi.
- Ghi lại timeline, version, migration, flag, task bị ảnh hưởng và quyết định tài chính.

## 13. Checklist production

- [ ] Backup và restore rehearsal đạt.
- [ ] Migration clone, idempotency và verify đạt.
- [ ] Secret/certificate/Data Protection được kiểm tra.
- [ ] Rate, credential, policy, budget và role đúng.
- [ ] Canonical Voice/speech/local model chỉ bật khi checklist riêng đạt; video dài Provider Native không gọi ASR.
- [ ] Build/test/smoke theo [KIEM_THU_VA_NGHIEM_THU.md](KIEM_THU_VA_NGHIEM_THU.md) đạt.
- [ ] FFmpeg provenance/checksum có `Approval scope: Release`.
- [ ] Monitoring/alert và rollback owner sẵn sàng.
- [ ] Chỉ các provider/payment/translation feature đã được sign-off mới bật.
- [ ] Kết quả rollout được cập nhật vào [BOI_CANH_HE_THONG_HIEN_HANH.md](BOI_CANH_HE_THONG_HIEN_HANH.md) bằng bằng chứng mới.
