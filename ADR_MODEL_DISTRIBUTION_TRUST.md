# ADR: xác thực kho model phiên bản đầu

Ngày: 2026-10-16. Trạng thái: chọn thiết kế và triển khai source; chưa có khóa/metadata production, nghiệm thu runtime hoặc rollout. Source nền `main`, `f0651c918f28ca477f28875818148e9e86240b7b`, cùng working tree chưa commit. ADR thực hiện phần thiết kế MD15 của [kế hoạch](PLAN_TASK_KHO_MODEL_SERVER_VA_CAI_DAT_AN_TOAN.md), không xác nhận các task vận hành đã hoàn tất.

## Quyết định

Phiên bản đầu dùng giao thức riêng: ECDSA P-256/SHA-256, chữ ký IEEE P1363 dài đúng 64 byte, trên **đúng byte UTF-8 của catalog**. Envelope JSON chỉ mang schema, key ID, algorithm, Base64 payload và Base64 signature. Không tự xây JSON canonicalization, không gọi giao thức này TUF compliant. Metadata nhiều GB không đi vào giao thức: chỉ catalog nhỏ, file model tải bằng stream riêng.

Verifier giới hạn envelope 128 KiB, payload 64 KiB, độ sâu JSON; từ chối thuộc tính trùng hoặc không biết. Chỉ parse payload sau khi chữ ký đạt trên cùng byte được dùng. Catalog ràng buộc repository/channel/platform, sequence dương, ngày UTC, hạn không quá 30 ngày và publication không vượt đồng hồ quá 5 phút. Không có root/public key được cấu hình thì fail closed.

Chữ ký hợp lệ vẫn không được thêm model hoặc executable. Ứng dụng pin đúng ba artifact: Qwen3 4B GGUF, Piper offline ZIP và manifest; gồm size/hash/component/version/filename/path canonical cùng worker/protocol khi áp dụng. Đổi pin cần bản ứng dụng đã rà soát mới. OCR tiếp tục được phân phối cùng app build tương thích; Kokoro không thuộc giao thức V1.

Client lưu high-water state theo repository/channel/platform, gồm sequence và SHA-256 của catalog payload. Sequence thấp hơn bị từ chối; sequence bằng nhưng khác hash bị từ chối. Verifier thuần trả `NextState`; caller phải khóa repository và lưu atomically trước dùng metadata hoặc tạo lượt tải. Mất hoặc xóa state làm mất bảo vệ so với lịch sử đã thấy; bộ cài mới chỉ có giới hạn expiry và các pin đi cùng app. Không tuyên bố chống được chủ máy đã kiểm soát binary, root hoặc đồng hồ hệ thống.

Server readonly không lưu high-water vào vùng download. Mỗi channel bắt buộc có pin vận hành `TrustedCatalogSha256ByChannel` của **toàn bộ envelope**, cùng public key và floor `MinimumSequence`. Chữ ký đúng nhưng pin khác vẫn bị từ chối. Promote phải đổi pin có chủ đích; service singleton đọc cấu hình khi tạo nên thay config cần recycle theo quy trình. High-water desktop hash payload và pin envelope server là hai giá trị khác nhau, không dùng thay nhau. Không format lại file envelope sau khi chốt pin.

Kho phục vụ chỉ nhận đường canonical `artifacts/<component>/<version>/<sha256>/<file>` được catalog đã xác thực tham chiếu; không upload/fetch upstream hoặc phát staging qua static-files. Server giữ file handle đã kiểm và quét SHA-256 toàn file cho từng GET/HEAD/Range, có một gate quét và rate limit. Đây là giới hạn hiệu năng V1 cần đo trên hosting với GGUF >2 GiB, chưa được thay bằng cache timestamp. Phân quyền artifact bất biến vẫn là điều kiện vận hành.

## Vì sao chưa tích hợp TUF đầy đủ

Đã tìm thấy thư viện .NET được duy trì: [Tuf 1.1.0](https://www.nuget.org/packages/Tuf/1.1.0), thuộc [sigstore-dotnet](https://github.com/mitchdenny/sigstore-dotnet/tree/v1.1.0/src/Tuf). Không kết luận .NET không có TUF. Danh sách [implementation chính thức](https://theupdateframework.io/docs/getting-started/) và [bảng conformance](https://theupdateframework.github.io/tuf-conformance/) được đối chiếu ngày 2026-10-16 chưa có kết quả cho client .NET này.

API target của tag 1.1.0 trả byte array và tải target vào byte array. Không dùng trực tiếp API đó với GGUF 2,5 GB. Một phương án TUF phù hợp về sau là dùng thư viện chỉ để xác thực catalog target nhỏ, custom bounded transport/cache, rồi tải model bằng descriptor đã xác thực. Phương án này còn đòi repository bốn role, công cụ ký, trust/cache/rotation adapter và nghiệm thu riêng. Không đưa package chưa có spike/conformance của dự án vào trust path sản phẩm trong lượt V1.

Giao thức V1 đơn giản hơn nhưng không có toàn bộ bảo vệ TUF: không role separation/threshold hay delegated targets, không root rotation tự động qua mạng, và mức chống freeze bị giới hạn bởi expiry/đồng hồ. Hard pin giữ file được phép bất biến; key bị lộ vẫn có thể làm sai metadata như compatibility/availability, dù không thể đưa bytes model mới qua pin. Đây là giới hạn cần ghi trong điều kiện phát hành.

## Khóa, rotation và phục hồi

Khóa riêng chỉ ở máy ký hoặc kho khóa được chỉ định, ngoài repository và server download. Source không chứa khóa production; helper ký chỉ nhận key do caller giữ. Tests sinh key tạm trong bộ nhớ. Server dùng public root từ cấu hình vận hành được bảo vệ; desktop phải nhận public root qua bản desktop được ký, không lấy trust root mới từ catalog hoặc endpoint cùng kho tải.

Rotation V1 qua signed desktop release: giao bản app mới tin key kế tiếp trong giai đoạn chuyển tiếp, sau đó phát hành catalog bằng key mới; loại key cũ qua bản app được ký tiếp theo. Khi mất/lộ key, dừng publish, giữ bằng chứng và phát hành root/key mới qua kênh ứng dụng được xác thực; client cũ không tự học key qua mạng. Ký số EXE/Setup là lớp riêng và phải hoàn tất trước chốt inventory/hash phát hành.

Metadata lỗi/hết hạn chặn cài mới, không tự xóa model hợp lệ đang cài. Rollback vận hành dùng catalog sequence mới trỏ artifact cũ còn được phép, không giảm high-water. Catalog có danh sách hash thu hồi tường minh, chỉ nhận hash thuộc bộ đã pin và không đồng thời active. `NextState` hợp nhất danh sách mới với known revocations trước đó; V1 không tự cho một hash đã thu hồi trở lại dù catalog sau bỏ nó khỏi danh sách. Caller lưu state này atomically và kiểm runtime gate, kể cả offline. Máy hoàn toàn offline chưa từng thấy lệnh thu hồi không thể bị thu hồi tức thì; verifier metadata không thay thế runtime gate.

Tắt `ModelDistribution.Enabled` chỉ tắt tải từ kho server. Desktop vẫn tạo client chỉ đọc trạng thái cục bộ theo cùng repository/channel/platform, không yêu cầu public key cho thao tác chỉ đọc và không phát HTTP. Vì vậy chuyển sang bộ offline không bỏ qua lệnh thu hồi đã lưu. Qwen/Piper kiểm trạng thái này trước cài hoặc reuse; Piper kiểm lại quanh probe và không tạo READY từ kết quả cũ khi trạng thái hiện tại đã bị chặn. Hành vi tải upstream đã pin của chế độ legacy được giữ khi kênh server tắt; bản online bật kênh server không tự fallback upstream khi metadata lỗi.

Tool offline `TOOL-MODEL-PUBLISHER` có lệnh `stage`, `public-key` và `revoke`; không sinh khóa hoặc triển khai server. `stage` giữ gate qualification Stable/Beta; `revoke` yêu cầu catalog hiện hành còn hiệu lực, envelope pin độc lập, public root và khóa ký trùng một key đã tin. Catalog thu hồi mới có sequence cao hơn, hợp nhất các hash thu hồi cũ và loại artifact bị thu hồi khỏi active list; output metadata mới không chứa payload và không sửa kho cũ. Cần backup metadata/config, promote catalog mới rồi cập nhật pin qua quy trình được phép. Không khôi phục pin cũ hoặc xóa state để rollback, và không dùng `stage` như lệnh un-revoke. Hướng dẫn/config/schema đầy đủ nằm trong [runbook công cụ ký](TOOL-MODEL-PUBLISHER/README.md).

## Kiểm chứng

Tests dành cho chữ ký sai/key lạ, sửa byte/whitespace, repository/channel/platform, expiry, sequence replay/equivocation, schema/JSON trùng/size, model/path/worker ngoài pin và đồng bộ pin với approved definitions của runtime. Chỉ ghi kết quả của lệnh thực sự đã chạy. Tài liệu [.NET ECDsa.VerifyData](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.ecdsa.verifydata?view=net-10.0) là nguồn cho API chữ ký nền tảng; không tự viết thuật toán mật mã.

Kiểm tra riêng ngày 2026-10-16 trên working tree: build `TOOL-DISTRIBUTION` Release `--no-restore` đạt, 0 warning/0 error. Harness xUnit tạm liên kết source ba lớp test và hai lớp desktop tải/catalog mới, tham chiếu Distribution/Contracts vừa build và Local binary hiện có để đọc các approved definitions: **55 Passed / 0 Failed / 0 Skipped**, khoảng 7 giây (trust 31, downloader 17, client 7). TRX tại `C:\Users\Admin\AppData\Local\Temp\vm-model-security-isolated-d111a374a5544a73994a6c8b555a96ff\TestResults\model-security-isolated.trx`. Đây là kiểm tra logic cô lập để tránh build chồng cùng working tree; chưa thay full build/test solution, model thật, file GGUF >2 GiB hoặc Windows sạch. Harness không gọi mạng thật, provider hoặc database.
