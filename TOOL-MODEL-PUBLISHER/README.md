# Chuẩn bị kho model bằng công cụ offline

`TOOL-MODEL-PUBLISHER` chuẩn bị một **thư mục kho mới trên máy vận hành**. Công cụ không upload, không sửa server đang chạy, không sinh khóa, không gọi model/database/provider và không chứng nhận nghiệm thu thay người vận hành. Catalog được ký ở cuối; `stage-result.json` luôn giữ `published=false`.

## Đầu vào

- Handover mới tạo bằng `scripts/New-ModelDistributionHandover.ps1`, có `inventory.json` ở trạng thái `ArtifactVerified`. SHA-256 inventory phải được lấy từ kết quả kiểm độc lập của bước gom bộ bàn giao.
- Ba file Qwen 4B/Piper ZIP/Piper manifest phải đúng các pin trong `ApprovedModelArtifactPins`. OCR là đầu vào build/repair đi cùng handover, chưa trở thành gói runtime tải riêng trong catalog v1.
- Khóa ECDSA P-256 PEM do người có quyền ký cung cấp ở đường dẫn tuyệt đối **ngoài repository, handover và output**. Khóa không nằm trên web server. Công cụ không xuất khóa bí mật hoặc ghi nội dung khóa vào log.
- Output là thư mục tuyệt đối chưa tồn tại, không lồng trong handover và không chứa handover; các đường dẫn không được đi qua junction/symlink/reparse point.

Tên script gom handover và schema inventory được mô tả thêm trong runbook kho model tại thư mục gốc. Chạy `--help` để đối chiếu tham số theo source hiện hành.

## Chuẩn bị candidate Staging

Ví dụ dưới đây chỉ dùng đường dẫn minh họa; không trỏ vào thư mục production:

```powershell
dotnet run --project TOOL-MODEL-PUBLISHER -c Release -- stage `
  --handover 'D:\ModelHandover\candidate-001' `
  --output 'D:\ModelRepositoryStaging\candidate-001' `
  --repository 'videomaker-models' `
  --channel 'Staging' `
  --sequence 1 `
  --desktop-version '1.0.0' `
  --desktop-build 1 `
  --key-id 'model-release-01' `
  --private-key 'C:\VideoMakerSigning\model-release-01.pem' `
  --inventory-sha256 '<64-hex-chars-from-verified-handover>' `
  --valid-days 7
```

Công cụ xác minh inventory và `compatibility.json`, yêu cầu desktop version/build đúng bộ bàn giao, kiểm từng pin/hash file nguồn dưới handle chỉ đọc, copy theo luồng và hash lại bản đích. Giữ nguyên các hồ sơ `licenses/` và `provenance/` trong `operation-records` sau kiểm hash. Không có giới hạn 2 GiB của kênh desktop release. Bản copy vẫn cần đủ đĩa và thời gian; Qwen khoảng 2,5 GB được đọc để kiểm nguồn, copy rồi kiểm đích. Mất điện/hủy/lỗi giữ `.incomplete`, không có metadata phát hành mới; dùng output mới khi thử lại, không ghi đè candidate cũ.

Đầu ra:

```text
artifacts/<component>/<version>/<sha256>/<file>
metadata/<channel>/catalog.json
public-trust-root.json
server-options.example.json
operation-records/<candidate-id>/inventory.json
operation-records/<candidate-id>/stage-result.json
```

Catalog ký exact byte payload theo giao thức riêng `ECDSA-P256-SHA256-P1363`; đây **không phải TUF**. Catalog mặc định hết hạn sau 7 ngày, tối đa 30 ngày. Tool xuất public root dạng `{schemaVersion,repositoryId,publicKeys}`. Khách phải nhận gốc tin cậy qua ứng dụng/kênh vận hành đã xác thực; không tự tin một khóa lấy từ chính máy chủ tải model.

Xuất riêng public PEM từ khóa đã được cấp:

```powershell
dotnet run --project TOOL-MODEL-PUBLISHER -c Release -- public-key `
  --private-key 'C:\VideoMakerSigning\model-release-01.pem' `
  --output 'D:\ModelHandover\model-release-01-public.pem'
```

## Gate Stable/Beta

`Stable` và `Beta` bắt buộc thêm `--qualification-file <absolute-external-json>`. Không có công tắc bỏ qua. Báo cáo do người chịu trách nhiệm nghiệm thu ký duyệt về mặt vận hành, nằm ngoài repository/handover/output, gắn đúng candidate, inventory và desktop build. Công cụ đối chiếu hash bằng chứng, không chạy lại hoặc tự diễn giải kết quả inference/nghe thử/legal review.

Schema v1 của báo cáo tổng hợp:

```json
{
  "schemaVersion": 1,
  "candidateId": "candidate-001",
  "inventorySha256": "<same-inventory-sha256>",
  "desktopVersion": "1.0.0",
  "desktopBuildNumber": 1,
  "runtimeQualified": true,
  "distributionApproved": true,
  "evidence": [
    { "kind": "QwenRuntime", "status": "Passed", "path": "runtime/qwen.json", "sha256": "<sha256>" },
    { "kind": "PiperRuntime", "status": "Passed", "path": "runtime/piper.json", "sha256": "<sha256>" },
    { "kind": "OcrRuntime", "status": "Passed", "path": "runtime/ocr.json", "sha256": "<sha256>" },
    { "kind": "LicenseReview", "status": "Passed", "path": "review/distribution.json", "sha256": "<sha256>" }
  ]
}
```

Các đường dẫn bằng chứng tương đối với thư mục báo cáo, không traversal hoặc symlink; mỗi loại có đúng một bản, dung lượng 1 byte–64 MiB. Bản gốc và hash được kiểm rồi giữ trong `operation-records`. Không sửa các trường `CustomerReleaseQualified=false`, `OtherMachinesVerified=false` của harness cũ để làm bằng chứng đạt; báo cáo tổng hợp phải viện dẫn đúng kết quả và phạm vi máy/build đã nghiệm thu. Nếu runtime hoặc quyền phân phối chưa đạt thì dừng ở Staging/Development.

## Phục vụ kho trên server

Sau khi môi trường đích và tác động được cho phép, người vận hành chuyển candidate vào staging riêng, kiểm lại toàn bộ hash tại server rồi tải thử qua đúng HTTPS/proxy. Gói kho này chưa tự chứng minh `ServerVerified`, `RuntimeQualified` hoặc đã rollout.

`server-options.example.json` mặc định `Enabled=false`. Cấu hình thật cần `StorageRoot` ngoài `wwwroot`, quyền web process chỉ đọc, public key và `TrustedCatalogSha256ByChannel` khớp SHA-256 **toàn bộ envelope catalog**. Promoting catalog mới yêu cầu đổi pin có chủ đích, kiểm sequence và restart/recycle ứng dụng theo quy trình môi trường; service singleton đọc `IOptions` khi được tạo, không tự reload pin trên mỗi request. Đây là gốc chống rollback của server readonly. Không tăng giới hạn release desktop để phục vụ GGUF.

### Chốt trust root và pin trước bật

Giữ nguyên byte file do tool xuất, kể cả JSON envelope; không format lại, sửa newline hoặc sửa Base64 sau ký. Hai hash có ý nghĩa khác nhau:

| Vị trí | Nội dung được hash |
|---|---|
| `TrustedCatalogSha256ByChannel` và ETag catalog của server | Toàn bộ file `metadata/<channel>/catalog.json`, gồm envelope và chữ ký. |
| High-water state của desktop | Payload catalog UTF-8 đã được xác minh chữ ký; không phải file envelope. |

Đọc pin từ **đúng bản ở server sau chuyển**, đối chiếu pin đã bàn giao qua kênh vận hành độc lập:

```powershell
(Get-FileHash -LiteralPath 'D:\ModelRepositoryStaging\candidate-001\metadata\Staging\catalog.json' -Algorithm SHA256).Hash.ToLowerInvariant()
```

Ví dụ cấu hình server, chỉ điền giá trị thật sau khi kiểm môi trường; không bật ví dụ còn placeholder:

```json
{
  "ModelDistribution": {
    "Enabled": false,
    "StorageRoot": "D:\\ModelRepository\\verified-root",
    "RepositoryId": "videomaker-models",
    "Platform": "win-x64",
    "AllowedChannels": ["Staging"],
    "PublicKeysPem": { "model-release-01": "<public SPKI PEM from approved public-trust-root.json>" },
    "TrustedCatalogSha256ByChannel": { "Staging": "<exact envelope SHA-256 verified on the server>" },
    "MinimumSequence": 1
  }
}
```

`MinimumSequence` là floor chung của cấu hình server, không phải bộ nhớ high-water riêng từng channel. Mỗi channel phải có pin envelope riêng; chỉ đưa channel đã được phép vào `AllowedChannels`. Server không tự chọn catalog mới nhất, không ghi state vào kho và không tải URL upstream.

Desktop nhúng public root đã rà soát khi build bằng `ModelDistributionTrustRootPath`; pipeline `Publish-DesktopRelease.ps1` nhận `-ModelDeliveryMode Server -ModelDistributionTrustRootPath <approved-public-root>`. Cấu hình desktop cần `ModelDistribution.Enabled=true`, `BaseUrl` HTTPS/443 công khai, cache mặc định theo user và channel `Update.Channel` khớp catalog. Không đặt đường dẫn cache riêng của máy build vào package. Script phát hành desktop hiện nhận Stable/Beta/Development; catalog Staging dùng cho kiểm kho/harness, hoặc dùng Development khi thử toàn luồng bằng pipeline desktop hiện có. `TOOL-LOCAL.exe --check-model-distribution-config` chỉ kiểm cấu hình/root nhúng, báo số request bằng 0; không chứng minh kết nối hoặc runtime READY.

### Promote và xác minh qua hosting

Trên môi trường đã được cho phép: lưu backup catalog, pin/config hiện hành và operation records; chuyển file vào vùng staging ngoài public; kiểm inventory/hash hai đầu; chuẩn bị quyền web process chỉ đọc vùng artifact đã niêm. Giữ đường dẫn canonical `artifacts/<component>/<version>/<sha256>/<file>` bất biến, không ghi đè file dưới một hash cũ. Staging đang upload, `.incomplete`, operation records và signing key không được phục vụ bằng static-files.

Chỉ đưa catalog đã kiểm vào `metadata/<channel>/catalog.json` cùng cấu hình đúng root/key/pin, rồi recycle theo cửa sổ được phép. Có thể đổi nguyên root đã kiểm và pin trong một đợt cấu hình. Khi catalog và pin chưa đồng bộ, API phải từ chối; không tắt kiểm chữ ký/pin để tránh khoảng từ chối này. Với nhiều node phải giữ binary, trust config và kho file đồng bộ trên tất cả node trước canary.

Sau bật staging, kiểm qua đúng domain/proxy: GET catalog khớp envelope pin; HEAD artifact đúng `Content-Length`/strong ETag; Range đầu/cuối trả 206, `If-Range` khác ETag trả toàn file; tải đầy đủ GGUF >2 GiB rồi tính lại SHA-256. Thử mất mạng/tải tiếp và file sửa một byte trên kho thử riêng. Metadata và payload phải giữ nguyên byte, không nén/chuyển đổi body làm lệch Range/hash; chỉ tin forwarding headers từ proxy đã cấu hình. Gate 30 request/phút theo địa chỉ IP và gate kiểm hash 503 cần được đo dưới NAT/proxy thật.

API đọc:

```text
GET/HEAD https://<host>/api/model-distribution/<channel>/catalog
GET/HEAD https://<host>/api/model-distribution/<channel>/artifacts/<sha256>/<fileName>
```

Artifact có strong ETag và hỗ trợ Range/If-Range. V1 server quét SHA-256 toàn bộ file trước mỗi GET, HEAD hoặc Range, chỉ một lượt quét cùng lúc; bận trả 503 cùng `Retry-After: 5`. Chưa có tối ưu cache kiểm hash: phải đo cold latency và timeout của HTTPS/proxy với Qwen >2 GiB trước khi bật cho khách. Metadata/payload không được công khai qua static-files hoặc một route khác bỏ qua kiểm chữ ký/pin. Revocation cần catalog mới và ứng dụng phải đọc metadata hợp lệ trước khi dùng file; giữ hồ sơ bản cũ, không tự xóa dữ liệu máy khách.

## Thu hồi model và phục hồi catalog

Lệnh `revoke` chỉ tạo **metadata ở output mới**, không copy model, không sửa kho đang phục vụ hoặc cấu hình server. Đầu vào là catalog hiện hành còn hiệu lực, pin envelope đã xác minh và public root được cấp qua kênh tin cậy; khóa ký phải thuộc đúng key ID đã được root đó tin. Công cụ không học khóa mới từ catalog, không cho dùng khóa khác dưới cùng key ID.

```powershell
dotnet run --project TOOL-MODEL-PUBLISHER -c Release -- revoke `
  --catalog 'D:\ModelRepository\verified-root\metadata\Staging\catalog.json' `
  --catalog-sha256 '<exact-current-envelope-pin>' `
  --trust-root 'D:\ModelRepository\verified-root\public-trust-root.json' `
  --output 'D:\ModelRevocations\revocation-002' `
  --sequence 2 `
  --revoke-sha256 '<approved-artifact-sha256>' `
  --key-id 'model-release-01' `
  --private-key 'C:\VideoMakerSigning\model-release-01.pem' `
  --valid-days 7
```

Nhiều hash dùng danh sách ngăn cách bằng dấu phẩy, không khoảng trắng. Sequence phải cao hơn catalog đầu vào; chỉ nhận hash của ba artifact đã pin. Hash đã thu hồi được hợp nhất với danh sách cũ; bỏ artifact đó khỏi active catalog. Thu hồi tất cả tạo catalog active rỗng hợp lệ. Hash không biết, danh sách lặp hoặc yêu cầu không tạo thay đổi đều bị từ chối. `Published=false` giữ nguyên.

Output giữ catalog trước đó trong operation records, public root, sample config pin mới và catalog mới ở `metadata/<channel>/catalog.json`. Vì **không có payload trong output revoke**, không chuyển `StorageRoot` server sang thư mục này khi vẫn còn artifact active. Sau backup và xác minh độc lập, promote đúng metadata vào kho artifact hiện hữu, cập nhật pin envelope và restart theo quy trình trên. Giữ payload cũ trong kho lịch sử; client đã nhận lệnh thu hồi phải từ chối sử dụng hash đó kể cả catalog sau bỏ tên khỏi danh sách.

Sequence/high-water tách theo repository/channel/platform. Luôn dùng catalog và pin hiện hành đã ghi trong hồ sơ vận hành, không chọn một backup cũ làm nguồn để ký sequence thấp hơn những máy đã thấy. Rollback một lỗi metadata phải là catalog **sequence mới cao hơn**, artifact vẫn thuộc bộ pin và chưa bị thu hồi; không phục hồi pin/catalog cũ, không xóa state desktop hoặc chỉnh đồng hồ để vượt gate. `stage` không phải lệnh undo revoke: nó đề xuất cả ba artifact chính và sẽ bị client đã ghi thu hồi từ chối nếu chứa hash đó. V1 không hỗ trợ un-revoke một hash; thay bytes đã thu hồi cần pin mới và bản ứng dụng/root được phát hành qua kênh có xác thực.

Catalog hết hạn chặn tải/cài mới. Lên lịch vận hành ký catalog sequence mới trước hết hạn; không sửa ngày trực tiếp trên file và không kéo dài quá 30 ngày. `revoke` cũng từ chối nguồn hết hạn hoặc sai chữ ký. Model đã cài không tự bị xóa vì lỗi metadata, nhưng known revocation và các gate license/setup/runtime vẫn áp dụng. Máy offline chưa từng nhận catalog thu hồi không thể biết thu hồi tức thì.

## Phạm vi kiểm tra hiện có

Ngày 2026-10-16, trên working tree nền `main`/`f0651c918f28ca477f28875818148e9e86240b7b`: publisher source gồm revoke được build trong harness cô lập, 0 warning/0 error. Ba lớp test server/publisher/revocation liên kết source đạt **48 Passed / 0 Failed / 0 Skipped**, TRX `D:\vm-tests\model-server-b2238f1d\results\server-publisher-revocation-final.trx`. Các test HTTP thực thi MVC file executor trong memory; đây chưa phải kiểm hosting HTTPS/proxy hoặc artifact thật >2 GiB. Test ký dùng khóa tạm, không dùng khóa production. Bản publisher đầy đủ với ba model thật chưa được chạy để ký candidate phát hành trong lượt kiểm này.

Kết quả này không thay full solution build/test trên snapshot cuối, smoke Qwen/Piper/OCR, Windows sạch, server thật hoặc rollout. Hash đúng chỉ là `ArtifactVerified`; muốn ghi `READY` phải probe đúng runtime/bundle trên từng máy khách. Không coi marker hoặc model từ máy build là bằng chứng máy khách sẵn sàng.

Mã lỗi CLI chỉ gồm mã an toàn, không in exception/path/nội dung khóa. Không copy signing key, cache, venv đã cài hoặc marker READY vào kho. Từng máy khách vẫn phải probe runtime trước `READY`.
