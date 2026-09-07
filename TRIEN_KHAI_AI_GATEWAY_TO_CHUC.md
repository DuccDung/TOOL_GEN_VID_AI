# Runbook triển khai VideoMaker

Tài liệu này dành cho database, credential, pricing, provider, thanh toán và release. Không thực hiện trên production nếu chưa có người chịu trách nhiệm phê duyệt môi trường, backup, chi phí và cửa sổ thay đổi.

## 1. Nguyên tắc vận hành

- Dùng staging tách biệt trước production.
- Không đưa secret vào source, command history, ticket, ảnh chụp hoặc log.
- Không gọi provider có phí khi chưa có budget cap và phê duyệt.
- Không tự suy đoán đơn giá. Lấy từ hợp đồng/dashboard chính tài khoản tại thời điểm cấu hình.
- Migration có trong source không đồng nghĩa đã chạy.
- Trước SQL thay đổi dữ liệu phải xác nhận đúng instance/database và backup đã restore thử.
- Dùng `sqlcmd -b -f 65001` để lỗi trả exit code và file tiếng Việt được đọc UTF-8.

## 2. Chuẩn bị môi trường

Yêu cầu tối thiểu:

- Windows x64 cho desktop/setup/updater.
- .NET SDK/runtime 10 phù hợp project.
- SQL Server và login triển khai có quyền tạo/đổi schema cần thiết.
- HTTPS certificate hợp lệ cho server ngoài localhost.
- Secret store hoặc environment variables cho JWT, SMTP, webhook và bootstrap admin.
- Storage có quyền ghi cho Data Protection keys, provider outputs và release artifacts.
- Egress firewall chỉ cho host provider được duyệt.

Trước triển khai:

```powershell
git status --short
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build

Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
npm test
```

Ghi lại commit, timestamp, SDK, số test và kết quả thực tế. Không dùng mốc từ tài liệu cũ thay cho lần chạy này.

## 3. Database

### 3.1 Thứ tự migration chuẩn

Chạy trên database mới hoặc clone theo đúng thứ tự:

```powershell
sqlcmd -S <instance> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.Initial.sql
sqlcmd -S <instance> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.0.OrganizationAiGateway.sql
sqlcmd -S <instance> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.1.VietnameseSeedTextRepair.sql
sqlcmd -S <instance> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.2.GptImageCharacterReference.sql
sqlcmd -S <instance> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.3.SceneVoiceTts.sql
sqlcmd -S <instance> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.4.BytePlusSeedance.sql
sqlcmd -S <instance> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.5.SceneNativeAudioStatuses.sql
sqlcmd -S <instance> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.6.NativeAudioWorkflowStatuses.sql
sqlcmd -S <instance> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.7.ProjectAssetTextLibrary.sql
sqlcmd -S <instance> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.8.AiGeneratedProjectAssets.sql
sqlcmd -S <instance> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.9.FalVeoLongForm.sql
sqlcmd -S <instance> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.10.LicenseSepayPayments.sql
sqlcmd -S <instance> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.0.11.OrganizationSeatProvisioning.sql
sqlcmd -S <instance> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.1.0.VietsubProjectRegistry.sql
sqlcmd -S <instance> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.1.1.SceneFirstFrames.sql
sqlcmd -S <instance> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.1.2.ProviderRequestFailureDetails.sql
sqlcmd -S <instance> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.1.3.SpeechSynchronization.sql
sqlcmd -S <instance> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.1.4.VoiceProfileApproval.sql
sqlcmd -S <instance> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.4.1.5.SpeechVerificationReview.sql
sqlcmd -S <instance> -d VideoFactory -E -b -f 65001 -i database\VideoFactory.DesktopLeastPrivilege.sql
```

Không bỏ qua `4.1.0`; đây là registry `vs.Projects`. Script least-privilege luôn chạy sau migration cuối để áp deny cho bảng mới.

### 3.2 Kiểm tra clone

Trên database clone:

1. Chạy toàn bộ chuỗi hai lần để chứng minh idempotency.
2. Kiểm tra `ai.SchemaVersions` có đủ version.
3. Kiểm tra schema `auth`, `ai`, `vf`, `vs` và các FK/index/unique constraint.
4. Kiểm tra project cũ được backfill an toàn, đặc biệt organization, video policy và speech policy.
5. Kiểm tra budget legacy bằng `0`, không vô tình mở AI.
6. Kiểm tra `VideoMakerDesktopRole` không đọc credential, provider request, usage ledger hoặc auth secret.
7. So sánh row count trước/sau cho bảng quan trọng.
8. Thực hiện rollback bằng restore backup, không dựa vào script down tự chế.
9. Kiểm tra `CK_SpeechVerificationReports_Review`: chỉ `NeedsReview` có đủ lý do, reviewer và thời điểm mới được đánh dấu chấp nhận.

Có thể dùng `database/Verify.VideoFactory.4.0.11.OrganizationSeatProvisioning.sql` cho phần seat provisioning; vẫn phải kiểm tra toàn schema mới bằng truy vấn read-only phù hợp môi trường.

## 4. Cấu hình server

Không đặt production secret trực tiếp trong `appsettings.json`. Dùng environment variables, secret manager hoặc deployment secret store.

Các nhóm cấu hình cần có:

- `ConnectionStrings` cho account/workflow/governance/Data Protection theo source hiện hành.
- `Jwt` signing secret/issuer/audience và thời hạn phù hợp.
- `Admin` bootstrap chỉ dùng lần đầu; xoá hoặc rotate bootstrap secret sau khi hoàn tất.
- `PasswordReset` và `Smtp`; password SMTP phải nằm trong secret store.
- `DesktopReleases` storage và base URL.
- `Generation:OpenAiImage`, `OpenAiSpeech`, `OpenAiTranscription`.
- `Generation:SpeechSynchronization` feature flags.
- `Generation:VideoOutputs` storage/retention/size/allowlist.
- `Generation:VideoPolling` attempts/age/claim lease.
- `Payments:Sepay` khi rollout thanh toán.

Data Protection phải có key ring bền vững và dùng cùng application name của source. Mất key ring có thể làm credential cũ không giải mã được.

## 5. Network allowlist

Runtime provider hiện hành chỉ chấp nhận HTTPS/443:

| Mục đích | Host |
|---|---|
| OpenAI API | `api.openai.com` |
| Kling API | `api-singapore.klingai.com` |
| BytePlus ModelArk | `ark.ap-southeast.bytepluses.com` |
| Fal Queue runtime | `queue.fal.run` |
| Fal credential test | `api.fal.ai` |

Provider output dùng suffix/exact-host allowlist riêng trong `Generation:VideoOutputs`. Không mở rộng wildcard chung như toàn bộ `googleapis.com`. Mọi thay đổi phải có test chặn private/loopback/link-local/reserved/multicast, DNS rebinding và redirect sang host khác.

## 6. Thiết lập organization

Thứ tự trong Admin Setup Center:

1. Tạo organization bằng Global Admin.
2. Thêm Owner Active; xác nhận không thể làm mất Owner cuối cùng.
3. Cấu hình budget tháng. Giá trị `0` giữ organization ở trạng thái khóa AI.
4. Bật provider/model cần dùng trong catalog.
5. Nhập credential qua HTTPS. Server phải test thành công rồi mới rotate.
6. Nhập rate Active đúng model/usage type/metadata.
7. Chọn video policy `Default` và `LongForm` rõ ràng.
8. Gọi readiness/status không tạo chi phí.
9. Chỉ sau đó mới dùng staging project để smoke test.

Không cấp Global Admin chỉ để user dùng AI. Quyền dùng AI đến từ membership organization và license.

## 7. Pricing

Không ghi giá mẫu trong tài liệu. Mỗi lần rollout:

1. Lấy giá từ hợp đồng/dashboard chính thức của credential đang dùng.
2. Xác định đúng currency, unit và effective time.
3. Tạo rate mới; không sửa rate snapshot lịch sử.
4. Đảm bảo mọi usage type bắt buộc của model đều có Active rate.
5. Kiểm tra quote bằng request read-only trước outbound.

Usage type chính:

- OpenAI text/image: `InputToken`, `OutputToken` theo unit cấu hình.
- OpenAI TTS: rate theo output/ước lượng được source yêu cầu.
- OpenAI transcription: `AudioSecond`.
- Kling/Fal: `VideoSecond` với metadata variant/resolution/audio/model chính xác.
- BytePlus: `OutputToken` và unit/model chính xác.

Thiếu rate phải fail closed bằng `pricing_not_configured`.

## 8. Credential rotation

1. Xác nhận organization/provider chính xác.
2. Nhập key mới qua HTTPS Admin/API; không gửi qua chat hoặc lưu file tạm trong repository.
3. Server gọi credential test được duyệt, không tạo render có phí.
4. Chỉ khi test đạt mới ghi version mới Active và chuyển key cũ Retiring.
5. Theo dõi task đang chạy tham chiếu key cũ.
6. Worker retirement chỉ revoke khi không còn task cần version đó hoặc đã qua policy an toàn.
7. Kiểm tra response chỉ có hint, không có plaintext/encrypted payload.

Rollback credential là rotate sang một key đã được test, không sửa ciphertext bằng SQL.

## 9. Smoke test AI/video

Chỉ chạy khi người dùng chỉ rõ staging và chấp thuận chi phí tối đa.

### Không tạo chi phí

- Login/refresh/logout và device/license.
- Organization list, membership role, budget snapshot.
- Provider readiness, model/policy và rate visibility.
- Credential test theo endpoint không tạo generation.
- Cross-user/cross-organization/Viewer bị chặn.
- Thiếu rate/budget/credential bị chặn trước outbound.

### Có thể tạo chi phí

Đặt budget thấp và chạy lần lượt:

1. Một content plan tiếng Việt nhỏ.
2. Replay cùng idempotency để chứng minh không gọi/tính phí lần hai.
3. Một character image hoặc first-frame khi provider cần.
4. Một clip ngắn theo đúng provider/policy.
5. Đóng desktop sau submit, xác nhận worker hoàn tất và settlement đúng.
6. Tải output qua proxy; xác nhận không có provider URL trong response/log.
7. Đối chiếu reservation, actual, release và dashboard provider.

Không bật fallback provider/model để “cứ chạy được”.

## 10. Rollout Fal/Veo hoặc BytePlus

- Giữ provider/model Disabled cho tới khi migration, credential, rate, policy và staging smoke test đều đạt.
- Fal Standard/Fast là endpoint độc lập; không fallback qua lại.
- Fal yêu cầu approved current first-frame đúng ratio và tối đa dung lượng capability.
- Kiểm tra Queue submit/status/result route, output cache và privacy header.
- BytePlus phải đối chiếu output token billing với provider dashboard.
- Chạy test credential retirement trong lúc task đang polling.
- Sau canary, tăng budget/traffic từng bước và theo dõi 401/403/422/429/5xx.

## 11. Rollout Canonical Voice và speech verification

Điều kiện Canonical Voice:

- Migration 4.1.3–4.1.4 đã kiểm tra trên clone và áp đúng môi trường.
- OpenAI TTS credential và rate Active.
- Storage/retention cho voice output đủ dung lượng.
- Voice preview, approval, scene WAV, kiểm tra kỹ thuật, duration guard và render đều smoke test.
- Provider status trả đúng catalog giọng được server cho phép; modal hiển thị đủ 13 giọng dựng sẵn hiện hành, hai alias cũ vẫn mở được project lịch sử và việc mở/chọn modal không tạo `ProviderRequest` hoặc budget reservation. Smoke test nút nghe thử ngay trong form tạo project cả khi có và chưa có project nội dung. Khi đã chọn project, request dùng project đó làm ngữ cảnh hạch toán, kể cả project chưa bật Canonical Voice. Khi chưa chọn project, quote phải tạo/tái sử dụng đúng một project kỹ thuật ẩn theo user+organization; context này không xuất hiện trong danh sách/dashboard. Xác nhận quote xuất hiện trước outbound, request dùng `RequestKind=VoicePreview` với metadata `previewKind=Catalog` và gắn đúng context project/user/organization, không sửa chính sách/giọng của project nội dung, WAV chỉ tải qua server và phát lại mẫu đã tải không reserve budget lần nữa.
- Rollback đã thử bằng cách tắt flags.

Thứ tự bật:

1. Bật `CanonicalVoiceEnabled` trên staging. `SpeechVerificationEnabled` là cờ độc lập cho Provider Native của workflow không phải video dài và không phải điều kiện của Canonical Voice.
2. Bật desktop feature flag trên nhóm canary.
3. Tạo voice draft -> preview -> nghe -> approve.
4. Sinh content plan có lời chiếm mục tiêu 85–95% thời lượng cảnh; xác nhận output ngoài biên an toàn 80–105% chỉ mở lựa chọn repair sau báo giá, không tự gọi OpenAI lần hai.
5. Tạo scene WAV, tải qua proxy và kiểm tra MIME, SHA-256, sample rate, duration, audibility cùng tỷ lệ thời lượng cảnh.
6. Xác nhận readiness `CanonicalVoiceReady`; kiểm tra TTS model/credential, rate và budget trước outbound. Transcription không được làm Canonical Voice mất readiness.
7. Phát nghe WAV và kiểm tra đúng voice generation/speech hash/voice snapshot; WAV ngắn hơn mục tiêu biên tập chỉ cảnh báo nếu vẫn hợp lệ kỹ thuật, không được tự sinh lại TTS. Xác nhận không có request Transcription hoặc `SpeechVerificationReport` mới.
8. Với `NativeVoiceOver`, tạo video nền không lời rồi ghép toàn bộ WAV, chỉ điều chỉnh tempo trong giới hạn và pad theo thời lượng cảnh; kiểm tra timeline gồm lẫn scene có/không audio.
9. Xác nhận `OnCameraDialogue` dừng ở `SpeechReadyForLipSync` và không bị render giả.

Nếu rollout speech verification cho `ProviderNativeVerified` của workflow không phải `OpenAiStructuredPlan`, cấu hình riêng `SpeechVerificationEnabled`, transcription model/credential/rate và migration 4.1.5. Kiểm tra `Passed` đi tiếp; `NeedsReview` bắt nhập lý do và lưu audit; stale row version và `Failed` đều bị chặn. Với video dài, phải xác nhận UI không hiện nút ASR, server từ chối quote/verify trước pricing/outbound và duyệt/render chỉ dựa trên audio hợp lệ cùng xác nhận nghe thủ công.

Tắt flag phải giữ project cũ đọc được theo `ProviderNativeVerified`.

## 12. Rollout SePay và organization seat

Điều kiện:

- Migration 4.0.10–4.0.11 và verify script đã đạt trên clone.
- Receiver bank/account/name, webhook secret và QR URL được cấu hình qua secret/config deployment.
- License plan map vào pool Active có organization readiness/capacity hợp lệ.
- User thử nghiệm không có payment/license xung đột.

Trước giao dịch thật, dùng harness chỉ trên localhost hoặc staging cô lập:

```powershell
.\scripts\Test-SepayOrganizationProvisioning.ps1 `
  -BaseUrl https://localhost:7202/ `
  -LicensePlanId <guid> `
  -Confirmation SEPAY_TEST_ONLY
```

Token test lấy từ environment variables theo script. Không dùng `-AllowRemote` nếu chưa xác nhận đây là staging cô lập.

Acceptance:

- webhook sai chiều/tài khoản/code/amount không fulfill;
- webhook hợp lệ đồng thời/replay chỉ fulfill một lần;
- seat reserved chuyển Active và membership managed đúng;
- expiry/reconciliation giải phóng reservation an toàn;
- admin response không lộ snapshot/idempotency/reference không cần thiết.

## 13. Release desktop

### FFmpeg

Bundle bắt buộc có:

- `ffmpeg.exe`;
- `ffprobe.exe`;
- `LICENSE.txt`;
- `PROVENANCE.md`;
- `checksums.sha256`.

Kiểm tra:

```powershell
.\scripts\Test-FfmpegBundle.ps1 `
  -BundlePath .\third_party\ffmpeg\win-x64 `
  -RequireReleaseApproval
```

Profile hiện hành là Development-only; lệnh trên phải fail cho tới khi product owner hoàn tất redistribution/license review và tạo provenance `Approval scope: Release`.

### Publish

Sau khi FFmpeg được duyệt:

```powershell
.\scripts\Publish-DesktopRelease.ps1 `
  -Version <version> `
  -BuildNumber <number> `
  -Channel Stable `
  -ServerBaseUrl https://<server>/
```

Publish tạo package/setup trong `artifacts`; không sửa artifact bằng tay. Trước khi upload:

- xác minh SHA-256/size/version/build/channel;
- clean-machine install;
- update từ version trước;
- rollback khi package hỏng;
- giữ user settings/workspace;
- kiểm tra FFmpeg/OCR runtime và WebView2;
- kiểm tra launcher/download Range và mandatory update policy.

## 14. Monitoring và reconciliation

Theo dõi tối thiểu:

- auth/session/device/license failure rate;
- provider 401/403/422/429/5xx và timeout;
- polling age/count/claim lease;
- reservation quá hạn, settlement/release/reconciliation;
- video/image/voice output storage và cleanup;
- SePay unmatched/duplicate/fulfillment failure;
- organization seat reserved/active/expired;
- update/download/install failure.

Log chỉ dùng ID kỹ thuật và mã lỗi cần thiết; không log prompt, transcript, Base64, key, token, local path hoặc provider output URL.

## 15. Rollback

- App: triển khai lại artifact đã ký/kiểm tra trước đó.
- Feature: tắt provider/model/policy hoặc feature flag, không xóa dữ liệu lịch sử.
- Credential: rotate sang key đã test.
- Database: restore backup đã chứng minh; không chạy script down chưa được kiểm thử.
- Worker: dừng nhận request mới nếu cần nhưng phải bảo toàn request/ledger để reconcile.
- Payment: tắt `Payments:Sepay:Enabled`; không sửa trạng thái payment/license trực tiếp nếu chưa có quy trình đối soát.

Sau rollback, xác minh request không bị submit lại, reservation không bị release/settle trùng và desktop cũ vẫn đọc được dữ liệu tương thích.

## 16. Checklist trước production

- [ ] Commit/tag cần phát hành đã được chốt; worktree sạch hoặc mọi thay đổi đều được giải thích.
- [ ] Restore, Release build, xUnit và web test đạt trên commit đó.
- [ ] Backup database đã restore thử.
- [ ] Migration chạy lặp trên clone và schema/version/row count đúng.
- [ ] Secret nằm ngoài source; HTTPS/certificate/Data Protection key ring đúng.
- [ ] Organization/Owner/budget/provider/model/credential/rate/policy đã sẵn sàng.
- [ ] Viewer/cross-tenant/thiếu pricing/budget bị chặn trước outbound.
- [ ] Worker polling, idempotency, settlement và output proxy đã smoke test.
- [ ] Paid smoke test nằm trong budget được duyệt.
- [ ] SePay/speech/provider mới chỉ bật nếu checklist riêng đạt.
- [ ] FFmpeg có Release approval; OCR/transitive notices đã review.
- [ ] Clean-machine install/update/rollback đạt.
- [ ] Monitoring, alert và người chịu trách nhiệm rollback đã sẵn sàng.
