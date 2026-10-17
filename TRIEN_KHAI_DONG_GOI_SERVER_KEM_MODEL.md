# Đóng gói server kèm bộ model — 2026-10-16

Đã tạo ZIP bàn giao thực tế trên máy đóng gói, chưa upload hoặc khởi động server. Binary được publish từ nhánh `main`, nền `f0651c918f28ca477f28875818148e9e86240b7b`, có thay đổi chưa commit; không coi đây là bản build nguyên commit. Snapshot 1.082 file source/script trong phạm vi cho phép được so sánh trước và sau publish. Các cập nhật tài liệu liên kết biên bản này được thực hiện sau khi hoàn tất ZIP.

## Gói bàn giao

Thư mục: `D:\vm-delivery\20261016-145626-e51402`.

| Nội dung | Giá trị |
|---|---|
| ZIP | `VideoMaker-Server-Models-20261016-075850.zip` |
| Dung lượng ZIP | 2.907.053.183 byte, khoảng 2,91 GB / 2,71 GiB |
| SHA-256 ZIP | `58ace746ac99e596aa476b5662fa97901b7813373fa2a6b693c7c57583c8e6be` |
| Dung lượng giải nén | 3.057.160.295 byte |
| Kiểm file sau giải nén | 533 file khớp inventory; thêm chính file inventory, tổng 534 file |
| Runtime server đi kèm | .NET / ASP.NET Core 10.0.9, Windows x64 |
| SHA-256 `TOOL-SERVER.dll` | `4944e3e4c63bfc67df5788196253068af8f9a842ab6b54c7d04af038009b3d83` |
| Candidate model | `models-20261016-03`, giữ nguyên 33 file, 2.918.879.650 byte |
| SHA-256 inventory model gốc | `2f387304020b06c9c59747eb3cbf3dda08e4b029eafd49d4c77581c4154d4843` |

Các file bên ngoài ZIP gồm `checksums.sha256`, `verification-result.json` và kết quả kiểm thử trong `test-results/`. `package/` là thư mục đã gom; `verified-extraction/` được giải nén từ chính ZIP và đối chiếu lại. `.incomplete` đã được xóa sau khi mọi bước kiểm gói đạt.

Trong ZIP:

- `server/`: DLL, Web Admin, cấu hình đã làm sạch, `web.config` và runtime riêng; tổng 488 file.
- `models/models-20261016-03/`: Qwen 4B GGUF, Piper offline ZIP + manifest, năm package đầu vào OCR và hồ sơ inventory/checksum/license/provenance/qualification. Giữ nguyên byte của bộ bàn giao.
- `tools/model-publisher/`: công cụ offline chuẩn bị và ký catalog bằng khóa do người vận hành cấp riêng.
- `deployment/`: hướng dẫn `README-DEPLOY.vi.md`, cấu hình mẫu tắt kho, trạng thái bàn giao và snapshot hash source.
- `package-inventory.json`: danh mục size/SHA-256 toàn gói; checksum không thay chữ ký nhà phát hành.

Không đưa cache, venv hoặc marker READY từ máy build vào bộ bàn giao. Các nupkg OCR là đầu vào build desktop, chưa phải artifact tải riêng của catalog V1. Kokoro và Qwen 1.7B không thuộc bộ chính này.

## Thay đổi phục vụ đóng gói

`scripts/New-ServerModelDelivery.ps1` nhận inventory pin độc lập, kiểm bộ handover, publish server, copy và kiểm hash hai đầu, gom nguyên bộ model, publish công cụ offline, tạo inventory, nén bằng stream rồi giải nén kiểm lại. Output phải là thư mục mới ngoài checkout và handover; không ghi đè bộ cũ. ZIP chỉ được đổi từ `.zip.part` sang tên cuối sau khi kiểm đạt.

`TOOL-SERVER.csproj` loại output/debug/runtime data khỏi Content/Compile/None/EmbeddedResource. Rà soát trước publish phát hiện Web SDK có thể lấy cả cấu hình debug trong `TOOL-SERVER/artifacts/`; đường này đã bị loại khỏi gói. `Publish-ToolServerIis.ps1` hỗ trợ output riêng và `-SkipArchive`, để wrapper dùng nén stream cho GGUF vượt 2 GiB.

`ServerPackageSafety.psm1` làm sạch các trường cấu hình nhạy cảm theo quy tắc, xóa cấu hình môi trường phụ, ép kho model tắt và bỏ trust/pin từ máy build. Audit chặn cấu hình lồng, thư mục dữ liệu/debug, key file và reparse trước đóng gói và sau giải nén. Đây là kiểm tra theo quy tắc, không phải chứng nhận loại trừ mọi dạng secret trong mọi binary. Source/cấu hình đang dùng không bị ghi đè.

Engine ZIP dùng chung hiện giới hạn tổng giải nén 4 GiB và 200.000 entry. Gói này nằm trong giới hạn; không suy ra mọi gói model lớn hơn đều dùng được. Không dùng `Compress-Archive` cho ZIP server kèm GGUF.

## Lặp lại đóng gói

Giữ checkout ổn định trong suốt quá trình. Chọn thư mục mới với đường dẫn ngắn và đủ chỗ cho bản gom, ZIP, giải nén kiểm tra và server publish. Script này chỉ tạo gói local, không upload hoặc chạy ứng dụng.

```powershell
& .\scripts\New-ServerModelDelivery.ps1 `
  -ModelHandoverPath 'D:\VideoMakerModelDistribution\handover\models-20261016-03' `
  -HandoverInventorySha256 '2f387304020b06c9c59747eb3cbf3dda08e4b029eafd49d4c77581c4154d4843' `
  -OutputDirectory 'D:\vm-delivery\candidate-moi'
```

Không dùng lại output đã tồn tại. Khi thay bộ model, phải lấy inventory pin đã kiểm độc lập cho candidate mới; không sửa checksum/license/provenance để vượt kiểm tra.

## Kiểm chứng

Restore và Release solution build đã chạy đạt; build có hai cảnh báo NU1902/NU1903 hiện hữu của `System.Security.Cryptography.Pkcs` 9.0.11. Frontend production build trong solution đạt, còn cảnh báo chunk lớn. Chưa nâng dependency trong lượt đóng gói; cần xử lý các advisory hiện hữu trước nghiệm thu production. Không ghi một lượt frontend test mới cho thay đổi backend/script này.

Wrapper hoàn tất exit 0; kiểm lại 533 file toàn gói và 31 file trong inventory model sau giải nén, cùng DLL identity và audit cấu hình. Cả 33 file handover đều được copy với kiểm hash, gồm inventory đã pin và metadata checksum kiểm riêng. Công cụ publisher `--help` chạy exit 0 bằng runtime của bản giải nén; không tạo khóa hoặc ký catalog trong lượt này.

Full .NET chạy riêng cuối đạt **2.050 Passed / 0 Failed / 18 Skipped / 2.068 Total**, 4 phút 27 giây; gồm 21 bài package safety. Lượt đồng thời nén trước đó có 2.049 Passed / 1 Failed / 18 Skipped: worker native Load timeout 5 giây. Giữ cả hai TRX; ca này đạt lại trong full suite khi đóng gói đã kết thúc, không sửa binary, code, timeout hoặc assertion. Chưa chứng minh nguyên nhân timeout là tác vụ nén. `build-test-summary.json` cạnh ZIP ghi hash và số đếm outcome; [kiểm thử và nghiệm thu](KIEM_THU_VA_NGHIEM_THU.md) tách rõ các lượt.

`source-test-identity.json` kiểm 997 file code/script trong scope snapshot, ngoài Markdown, vẫn khớp sau đóng gói; chỉ bổ sung tài liệu. Test model/SQL opt-in bị skip không được tính là model hay database đã đạt. ZIP không bị chỉnh sửa để bổ sung kết quả kiểm thử sau đóng gói.

## Triển khai tiếp

1. Chuyển ZIP vào staging mới trên đúng server, đối chiếu SHA-256 bên ngoài và inventory sau giải nén tại máy đích. IIS physical path chỉ trỏ `server/`; giữ model, publisher và hồ sơ triển khai ngoài website. IIS vẫn cần ASP.NET Core Module V2/Hosting Bundle phù hợp, dù gói có private runtime.
2. Cấp cấu hình bí mật môi trường đích, xác minh schema/migration hiện hành, backup và khả năng restore trước startup. Giữ dữ liệu, cấu hình vận hành và Data Protection key ring hiện hữu. Source hiện có thay đổi 4.1.16 cùng các module khác; build thành công không chứng minh database đã tương thích. Startup bootstrap có thể ghi DB; chưa chạy server để thử trong lượt này.
3. Giữ `ModelDistribution.Enabled=false`. Bộ model hiện là **UnsignedHandover**, chưa có catalog ký hoặc production qualification. Dùng publisher trên máy vận hành với khóa riêng nằm ngoài gói và web server để tạo kho mới, chốt compatibility/channel/sequence, kiểm lại tại server rồi cấu hình public key, pin envelope, MinimumSequence và ACL chỉ đọc. Candidate hiện giữ hồ sơ Staging/desktop 1.0.0 build 1; thay compatibility phải tạo candidate mới, không sửa tại chỗ.
4. Tạo desktop/Setup mode Server với public root nhúng, HTTPS URL và channel tương thích. Kiểm domain thật, catalog/signature/expiry, HEAD/Range/ETag, tải Qwen trên 2 GiB, mất mạng/tải tiếp và hash cuối; tiếp đó mới thử Setup sạch và runtime Qwen/Piper/OCR. Chưa xóa hoặc cách ly model đang dùng trên máy khách.

Trạng thái bàn giao là đã có source và đã kiểm tính toàn vẹn gói trên máy đóng gói. `serverStarted`, `databaseContacted`, `serverVerified`, `deployed`, `runtimeModelProbePerformed`, `modelCatalogSigned` và `productionQualified` đều chưa đạt trong lượt này; không suy từ file ZIP, test fake hoặc các smoke lịch sử sang production.
