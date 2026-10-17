# Triển khai kho model và cài đặt an toàn

Ngày thực hiện: 2026-10-16. Source nền: nhánh `main`, commit `f0651c918f28ca477f28875818148e9e86240b7b`, cùng working tree chưa commit. Các thay đổi đã có của người dùng được giữ nguyên. Không coi commit nền là toàn bộ source được build.

Tài liệu này ghi kết quả triển khai theo [kế hoạch MD01–MD36](PLAN_TASK_KHO_MODEL_SERVER_VA_CAI_DAT_AN_TOAN.md). Có source và kiểm thử tại máy phát triển chưa đồng nghĩa đã nghiệm thu trên Windows sạch hoặc đã đưa lên server thật.

## 1 Phạm vi đã triển khai

| Thành phần | Hành vi trong source |
|---|---|
| Bộ chính | Qwen3 4B GGUF, Piper offline v3 và năm package đầu vào OCR đồng bộ. Qwen 1.7B và Kokoro không đưa vào catalog chính. |
| Gom bàn giao | Script tạo candidate mới ngoài checkout; kiểm pin trước/sau copy; kiểm mọi entry Piper; ghi inventory, checksum, source snapshot, compatibility, nguồn và license. Không mang cache, venv đã cài hay marker READY. |
| Danh mục tin cậy | Catalog nhỏ ký ECDSA P-256/SHA-256 trên exact bytes; root public nhúng trong desktop; giới hạn schema/size/expiry; chống lùi sequence; thu hồi hash không tự được bỏ qua. Chi tiết tại [ADR](ADR_MODEL_DISTRIBUTION_TRUST.md). |
| Server | Module chỉ đọc, cấu hình riêng, kho ngoài `wwwroot`; catalog phải đúng chữ ký và pin envelope theo channel; GET/HEAD/Range; kiểm hash, đường dẫn và reparse; không gọi database/provider. |
| Công cụ phát hành | `TOOL-MODEL-PUBLISHER` chuẩn bị kho staging có chữ ký từ handover; giữ bằng chứng; Stable/Beta yêu cầu hồ sơ nghiệm thu. Không tự upload hoặc publish. |
| Tải desktop | `.part` và receipt theo identity; Range/If-Range, ETag, giới hạn byte/disk, timeout, retry, cancellation; SHA-256 toàn file trước đổi tên atomically. Không dùng JWT/provider credential để tải model. |
| Qwen/Piper | Dùng nguồn kho đã xác thực khi bật Server mode; vẫn giữ pin nhúng và probe thật trước READY. Piper tạo venv tại máy đích từ ZIP; online thiếu payload cho phép tải. Danh mục sai không tự chuyển sang nguồn khác. |
| OCR/media | Đi cùng app build tương thích; giữ gate bốn DLL MSVC app-local và FFmpeg. Năm package OCR trong handover là build/repair inputs; không cài DLL rời vào app đang chạy. |
| Đóng gói | `-ModelDeliveryMode Bundled|Server`, mặc định Bundled. Server mode cần HTTPS/443, bật cấu hình rõ, public root hợp lệ và không chứa GGUF/Piper payload; vẫn phải có worker/OCR/media. Không nâng cap release 2 GiB. |

Mặc định `ModelDistribution.Enabled=false`; root trong source không có khóa phát hành. Bản build mặc định chưa được cấu hình để tải từ một server thật. `VietsubLocalTranslationEnabled` vẫn giữ mặc định an toàn. Không sinh khóa production, sửa secret, chạy migration hoặc thay đổi server đang vận hành.

Giao thức V1 **không phải TUF**. Rotation root/key qua bản desktop được ký; chưa có chuỗi root rotation online hay threshold signing. Không nhận public root mới từ chính endpoint tải model để tự mở rộng lòng tin.

## 2 Tạo bộ bàn giao

Đóng băng source liên quan trước khi chạy; script so source snapshot đầu/cuối. Candidate phải có ID mới, output ngoài repository và không qua junction/symlink.

```powershell
.\scripts\New-ModelDistributionHandover.ps1 `
  -CandidateId 'models-20261016-01' `
  -OutputRoot 'D:\VideoMakerModelDistribution\handover' `
  -DesktopVersion '1.0.0' `
  -DesktopBuildNumber 1 `
  -Channel Staging
```

Thư mục có `inventory.json`, `checksums.sha256`, `source-snapshot.json`, `compatibility.json`, `artifacts/`, `licenses/`, `provenance/` và `qualification/artifact-verification.json`. Artifact dùng đường dẫn `artifacts/<component>/<version>/<sha256>/<filename>`. Ghi SHA-256 inventory vào biên bản ngoài candidate để đối chiếu khi ký/chuyển server.

`ArtifactVerified` chỉ xác nhận file. Các trường `runtimeQualified`, `serverVerified`, `published` vẫn false. Không sửa các trường đó để vượt gate.

## 3 Ký và chuẩn bị staging

Sau khi người phụ trách cung cấp khóa riêng ở máy ký và đích staging cụ thể, làm theo [hướng dẫn công cụ](TOOL-MODEL-PUBLISHER/README.md). Công cụ chỉ nhận khóa P-256 ở đường dẫn ngoài repository, handover và output, không sinh hoặc đưa khóa vào kho tải.

`stage` cần đúng SHA-256 inventory, candidate/build/channel/sequence, output mới và khóa được cấp. Catalog được ghi cuối cùng. `stage-result.json` luôn `published=false`; `.incomplete` còn lại khi thao tác chưa hoàn tất. Không copy output dở vào vùng phục vụ.

Stable/Beta cần báo cáo ngoài candidate, gắn đúng inventory và desktop build; đủ bằng chứng có hash cho Qwen, Piper, OCR và LicenseReview. Staging/Development không thay thế nghiệm thu production.

## 4 Chuyển lên server đích

Chỉ thực hiện sau khi xác định server, thư mục staging/live, domain HTTPS và phạm vi tác động. Chưa suy server đích từ cấu hình hiện có của checkout.

1. Chuyển kho đã chuẩn bị vào staging ngoài vùng public, dùng file/thư mục tạm trong khi truyền.
2. Tính lại hash từng artifact và catalog ngay tại server; đối chiếu biên bản độc lập từ máy ký. Giữ license và operation records ở vùng không public.
3. Cấp web process quyền đọc; tài khoản import riêng có quyền ghi staging. Object có hash trong URL không được thay byte.
4. Cấu hình `ModelDistribution` dựa trên `server-options.example.json`: bật rõ `Enabled`, đặt `StorageRoot`, repository/platform/channel, public PEM và `TrustedCatalogSha256ByChannel` đúng SHA-256 **toàn envelope**. Không bỏ pin chỉ vì chữ ký đã hợp lệ.
5. Promote artifact trước metadata. Không cung cấp trực tiếp qua `wwwroot` hoặc static route vượt qua kiểm tra của controller. Thay catalog cần pin mới và sequence tăng; reload theo quy trình của đúng môi trường.
6. Kiểm GET/HEAD/Range qua đúng HTTPS/proxy, rồi tải trọn GGUF >2 GiB về workspace mới và hash lại. Chỉ sau đó ghi ServerVerified.

API:

```text
GET/HEAD /api/model-distribution/{channel}/catalog
GET/HEAD /api/model-distribution/{channel}/artifacts/{sha256}/{fileName}
```

V1 quét hash toàn bộ artifact dưới file handle trước mỗi GET/HEAD/Range; một lượt quét cùng lúc, bận trả 503/Retry-After. Rate limiter theo IP là 30 request/phút. Cần đo cold latency, dung lượng và timeout qua proxy thật trước mở tải khách hàng; chưa có benchmark production hoặc cơ chế dọn cache/retention tự động.

## 5 Bản desktop dùng kho server

Deployment profile cần chứa phần sau với domain thật đã xác định; không đặt đường cache máy phát triển vào package:

```json
{
  "ModelDistribution": {
    "Enabled": true,
    "BaseUrl": "https://models.example.invalid/"
  }
}
```

Domain trên chỉ minh họa, không phải endpoint đã triển khai. Channel của model đi theo `Update.Channel`; desktop publish hiện hỗ trợ Stable/Beta/Development. Model catalog Staging phục vụ thử công cụ; bản desktop thử cần channel nhất quán với profile và pipeline được hỗ trợ.

Truyền `-ModelDeliveryMode Server -ModelDistributionTrustRootPath <public-trust-root.json đã xác thực>` cho `scripts/Publish-DesktopRelease.ps1`, cùng các tham số version/build/server/profile/media của pipeline hiện hành. Không truyền `IncludeQwenModel`, `QwenModelPath` hoặc `PiperBundlePath` trong Server mode. Script kiểm public root và CLI `--check-model-distribution-config` xác nhận hash resource thực nhúng sau publish, không tạo request mạng.

Bundled mode giữ hành vi package hiện hành. Bản đủ Qwen cần kênh Direct phù hợp; vẫn kiểm ZIP và tổng giải nén. Server mode không tự bật Qwen, không bỏ startup modal/host gate hoặc kiểm session/license.

## 6 Nghiệm thu và các gate còn lại

Hồ sơ kiểm tra thực tế của lượt triển khai nằm ngoài repository tại `D:\vm-tests\model-distribution-20261016`. Kết quả build/test cuối và bộ bàn giao thật được ghi ở phần biên bản dưới đây sau khi chạy. Harness giả HTTP, test ký và test filesystem không chứng minh inference, nghe tiếng Việt hoặc máy Windows sạch.

Các bước cần môi trường/đầu vào riêng:

- Server/domain/thư mục đích, dung lượng và quyền vận hành: chưa được cung cấp; chưa upload hoặc thay cấu hình server thật.
- Khóa ký model và chứng thư ký EXE/Setup: chưa được cung cấp; không có production catalog hoặc package đã ký.
- Rà quyền phân phối, source provision của Piper GPL/dependency và phê duyệt FFmpeg Release: còn gate vận hành; hồ sơ FFmpeg hiện là Development, không đổi nhãn để phát hành.
- Windows sạch, user thường, Intel/AMD/RAM mục tiêu, đường dẫn có dấu và nghe WAV: cần nghiệm thu riêng. Qwen warning tài nguyên không được tự xác nhận.
- Luồng từ Setup cuối tới login/license, tải qua server staging thật, Vietsub và xuất MP4: chưa nghiệm thu.
- Rotation/recovery khóa qua bản desktop được ký, rollback/revoke trên server thật, retention/metrics và rollout theo nhóm khách: chưa hoàn tất chỉ từ các test tự động.

MD07–MD09, MD13–MD14, MD31, MD33–MD36 chưa được coi là hoàn tất production. Các task adapter/verifier/pipeline đã có source nhưng chỉ được mở Server mode trong profile thử được kiểm; qualification chưa đạt không bật phát hành Stable/Beta.

## 7 Biên bản lượt chạy

Phần này chỉ cập nhật bằng kết quả thực chạy, kèm phạm vi và đường dẫn bằng chứng. Không chuyển Passed của test logic thành RuntimeQualified hoặc Published.

### 7.1 Source và kiểm thử tự động

- Restore solution đạt. Release build cuối `--no-restore -m:1` đạt **0 Error / 2 Warning**; hai cảnh báo có sẵn là NU1902/NU1903 của `System.Security.Cryptography.Pkcs 9.0.11`. Frontend còn cảnh báo Vite chunk >500 kB. Không nâng dependency ngoài phạm vi kho model.
- Nhóm regression cơ chế phân phối: **161 Passed / 0 Failed / 0 Skipped**, 36 giây. Bao gồm trust, downloader, client offline/revocation, server, publisher/revoke, handover và đóng gói. `regressions/model-distribution-regressions.trx`.
- Full .NET cuối: **1.974 Passed / 0 Failed / 18 Skipped / 1.992 Total**, 6 phút 46 giây; `tests-verified-final/model-distribution-verified-final.trx`. TEMP/TMP ngắn `D:\vm-tests\md16`, collections tuần tự. Những bài opt-in SQL/model bị skip không được tính là runtime đạt. Nhóm 161 ở trên đã nằm trong full suite, không cộng trùng.
- `npm ci --no-audit --no-fund`, `npm run build`, `npm test -- --run` đạt; frontend **322 Passed / 0 Failed / 0 Skipped**, 47 file. Source frontend không đổi trong lượt này; build Release cuối đã cập nhật bundle tương ứng.
- Giữ log các lượt trước: build đầu có lỗi biến trong fixture handover; lượt full trước sửa cuối có **1.973 Passed / 1 Failed / 18 Skipped** do xử lý `InvalidDataException` của trust state. Đã sửa để Qwen/Piper trả trạng thái lỗi an toàn, không bỏ assertion hoặc nới gate. Full cuối nêu trên đã chạy lại sau sửa.

### 7.2 Bộ bàn giao thật

Candidate cuối: `D:\VideoMakerModelDistribution\handover\models-20261016-03`.

| Bằng chứng | Kết quả |
|---|---|
| Inventory | **31 file**, tổng **2.918.846.873 byte** được khai báo và kiểm lại sau copy; ngoài ra có inventory và checksum file. |
| SHA-256 `inventory.json` | `2f387304020b06c9c59747eb3cbf3dda08e4b029eafd49d4c77581c4154d4843` |
| Piper ZIP | **3.254/3.254 entry** đúng size/hash; tổng giải nén 231.194.288 byte. |
| OCR | Năm raw `.nupkg` đúng SHA-512 đã pin; có SHA-256 trong inventory. Đây là build inputs, không phải runtime cài rời. |
| Source snapshot | 1.068 file identity; kiểm lại **926 file source/cấu hình/script chức năng, 0 thay đổi** sau build cuối. SHA-256 snapshot `06813bef0181664f3ef818c8962984e6c814bb8e58726f954c26dfb3882afc82`. |
| Trạng thái | `ArtifactVerified=true`; `RuntimeQualified=false`, `ServerVerified=false`, `Published=false`. |

Lượt `models-20261016-01` bị từ chối khi ADR đổi giữa snapshot đầu/cuối; thư mục staging được giữ để tra cứu. Candidate `02` đã kiểm file nhưng snapshot có trước bản sửa lỗi trust-state; candidate `03` là bộ bàn giao cuối được dùng cho smoke. Không ghi đè/xóa candidate cũ. Phần tài liệu biên bản này được cập nhật sau khi niêm candidate để ghi kết quả; không thay source chức năng, binary hoặc bytes trong candidate.

Runtime chẩn đoán đã được copy sang `D:\vm-tests\model-distribution-20261016\runtime-smoke03\runtime`, giữ hash từng file trong `runtime-bundle-manifest.json`. Ba payload lấy đúng candidate `03`; settings tạo mới `{}`, không mang cấu hình người dùng, cache, venv hoặc marker READY. Đây là output kiểm tra, không dùng làm pin thay source.

### 7.3 HTTPS và tải tiếp với file thật lớn hơn 2 GiB

Harness ngoài repository tham chiếu binary cuối, dùng publisher, store/controller và MVC file executor thực của source. Kho thử dùng đúng handover `03`; khóa model chỉ dành cho phép thử, đã xóa sau chạy. Kestrel riêng chỉ nghe loopback, đã dừng; không host `Program` production, kết nối database hoặc gọi provider.

Lượt cuối **đạt**, 11 request trong 104,669 giây; private memory tối đa 85.684.224 byte (khoảng 81,7 MiB):

- HEAD Qwen trả đúng 2.497.280.256 byte và strong ETag; Range 32 byte đầu/cuối trả 206, đúng nội dung.
- Client desktop hủy khi đã ghi 3.145.728 byte; `.part`/receipt được giữ, chưa promote file cuối.
- Tạo client mới, tải tiếp bằng 206/If-Range; toàn bộ GGUF khớp SHA-256 đã pin sau khi hoàn tất.
- Client tải đúng manifest/ZIP Piper qua catalog đã ký, chuẩn bị cặp payload cache và `VerifyArchive`/`Available` đạt; không cài runtime trong harness mạng.
- TLS dùng pin exact certificate thử trong handler riêng; `ConnectCallback` chỉ tới listener loopback, không đổi DNS hoặc global trust. Certificate dùng transient UserKeySet do Schannel trên Windows, không PersistKeySet/MachineKeySet; PFX chỉ nằm trong bộ nhớ và certificate được dispose. Không dùng khóa/chứng thư production.

Report: `large-http-proof-05/local-large-http-report.json`; hash binary: `large-http-harness/binary-fingerprints.json`. Giữ các lượt lỗi TLS/test-harness trước đó riêng; không phải lỗi kiểm hash model. Lượt cuối dùng stream cho HEAD, không đọc Content-Length >2 GiB vào byte array. Attribute rate limiter không được thực thi qua adapter của harness; policy có source/test riêng, cần kiểm lại qua host/proxy thật.

**LocalOnly=true; ServerVerified=false; RuntimeQualified=false; Published=false.** Phép thử không xác nhận DNS/proxy/CDN, timeout, quyền filesystem hoặc server production của người dùng.

### 7.4 Smoke runtime trên máy phát triển

Máy thử: Windows 11 Home Single Language build 26200, Intel i7-11800H, 8 core/16 logical processor, RAM khoảng 16 GiB; không phải Windows/profile sạch. Các phép thử model thực chạy sau full build/test, dùng workspace riêng, giữ gate tài nguyên và không bật feature flag.

| Thành phần | Kết quả thực tế | Giới hạn |
|---|---|---|
| OCR/media | **Đạt** `--check-bundled-components`: fixture Anh/Trung, FFmpeg và bốn DLL MSVC được nạp app-local; không dùng model Qwen/Piper đã cài. | Chưa thay kiểm Windows thiếu prerequisite hoặc bộ cài cuối trên máy sạch; FFmpeg provenance vẫn Development. |
| Piper | **Đạt** cài từ đúng ZIP candidate vào workspace ngắn `D:\vm-tests\piper-md16-01`, tạo venv mới, kiểm installed files và tạo/kiểm WAV; `PiperOfflineReady=true`, `NetworkDownloadsAllowed=false`. | Lượt workspace dài của runner thất bại; chưa dùng kết quả workspace ngắn để kết luận hỗ trợ đường dẫn dài. Chưa nghe WAV thủ công. |
| Qwen | **Đạt** offline probe runtime/English/Chinese và lượt stability riêng **1 cold start × 30 fixture**, worker **1.2.1 / IPC 3**, profile `qwen3-cpu-safe-v2`, không tải mạng hoặc chấp nhận cảnh báo tài nguyên. | Chưa có ba cold start liên tiếp hoặc phần cứng/Windows sạch khác. |

Bằng chứng: `runtime-smoke03/reports/probe-20261016-022902-102c27386f8b47fc841d5c17d791413c/bundled-components.json`, `piper-runtime-short-path.json`, `qwen-runtime-short-path.json`, `runtime-environment.json`, các snapshot `resources-before-*.json`. Qwen worker fingerprint `dfe27bf8b11ee46166c90f010cc41898c6dfda1701819ddbdd8a57de3957797a`; RAM trống trước probe khoảng 5,25 GiB, commit trống khoảng 8,02 GiB. Không suy kết quả này là đã giải quyết mọi crash cũ hoặc đạt ma trận khách hàng.

Stability riêng kết thúc lúc `2026-10-16T02:44:38Z`, **Passed=true**, đủ 30 fixture Anh/Trung luân phiên, tăng private memory theo cửa sổ warm-up **5.378.048 byte (khoảng 5,13 MiB)**, dưới ngưỡng 256 MiB của harness. `ResourceWarningAccepted=false`, `CustomerReleaseQualified=false`, `OtherMachinesVerified=false`. Report ở `qwen-stability-01/qualification.json` và `cold-start-01.json`; không cộng lượt này vào số xUnit. Chỉ một cold start được chạy, không ghi thành ba cold start.

Rà soát filesystem của lượt Piper dài: 3.254 payload đủ size, `python.exe` đúng hash; chưa tạo `.venv` hoặc READY. Máy có `LongPathsEnabled=0`; 401 đường payload và 809 đường installed dự kiến vượt 259 ký tự, trong khi workspace ngắn không vượt. File dài có thể thấy bằng đường dẫn mở rộng nhưng API thường báo thiếu. Đây là bằng chứng lỗi trước bước tạo venv, có tương quan mạnh với độ dài prefix; thiếu stderr/mã gốc nên chưa khẳng định API cụ thể gây lỗi. Không đổi registry hoặc hạ xác minh để làm phép thử đạt. Ma trận đường dẫn dài vẫn **chưa đạt**.

## 8 Đối chiếu trạng thái task

| Task | Trạng thái sau lượt triển khai |
|---|---|
| MD01–MD02, MD04–MD06 | Đã chốt bộ chính, compatibility, snapshot và tạo/kiểm handover thật. |
| MD03 | Đã giữ nguồn/license/notice; quyền phân phối public, source provision GPL và FFmpeg Release còn cần duyệt. |
| MD07–MD09 | Chưa làm tại server đích vì thiếu đích được chỉ định. HTTPS loopback ở mục 7.3 là bằng chứng phát triển riêng. |
| MD10–MD12 | Runtime local đạt ở phạm vi đã ghi: Qwen 1 cold start/30 fixture, Piper workspace ngắn/WAV tự động, OCR/media app-local. Chưa nghe WAV hoặc thay ma trận máy sạch. |
| MD13–MD14 | Chưa hoàn tất ma trận/qualification; không nâng candidate lên RuntimeQualified. |
| MD15–MD16 | Có ADR giao thức V1, DTO và verifier; không nhận là TUF compliant. |
| MD17 | Có signer/publisher/revoke, root nhúng và test; khóa/chứng thư production, rotation/recovery qua desktop được ký chưa thực hiện. |
| MD18 | Có API chỉ đọc và phép thử HTTPS thật >2 GiB trên local; host đầy đủ/proxy/CDN/rate limiter môi trường đích chưa nghiệm thu. |
| MD19 | Có staging bất biến, kiểm file, hồ sơ và gate ký; chưa import/promote live hoặc publish production. |
| MD20–MD23 | Có verifier, downloader/resume, cache và xác minh/extract; regression và luồng file lớn/Piper thật đạt local. |
| MD24 | Có khóa tải/cài/chuẩn bị, reuse và readiness theo runtime; chưa có cơ chế retention/cleanup tự động cho toàn kho/version. |
| MD25–MD28 | Đã nối Qwen/Piper với Setup hiện hành, giữ OCR/media theo app và progress/quyền/gate; vẫn cần luồng người dùng end-to-end trên môi trường đích. |
| MD29–MD30 | Có Server/Bundled mode, validator, test và giữ tương thích mặc định; chưa phát hành/chạy Setup khách cuối hoặc repair end-to-end. |
| MD31 | Chưa ký Authenticode/chốt public Release; thiếu chứng thư, license review và FFmpeg Release hợp lệ. |
| MD32 | Kiểm thử tự động cuối đạt; các bài opt-in vẫn ghi riêng Skipped. |
| MD33 | Có các bằng chứng local riêng; chưa nghiệm thu Setup cuối → login/license → staging server → Vietsub/xuất MP4 trên Windows sạch. |
| MD34 | Có progress/mã lỗi/hạn mức/rate limiter cơ bản; chưa triển khai metrics, retention và cảnh báo production. |
| MD35 | Chưa rollout. |
| MD36 | Có thu hồi và chống rollback trong source/tools/tests; chưa diễn tập live rollback/revoke/key recovery trên môi trường khách. |

Báo cáo tổng hợp máy đọc được: `D:\vm-tests\model-distribution-20261016\implementation-result.json`. Các tiến trình runtime/HTTPS chẩn đoán của lượt này đã kết thúc; không dừng server/IDE của người dùng. Chưa tạo khóa production, đổi database, gọi provider có phí hoặc publish release. Bộ bàn giao, runtime và kết quả thử được giữ nguyên để người dùng rà soát.
