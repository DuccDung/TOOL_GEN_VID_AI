# Kế hoạch và task tạo ZIP đầy đủ cho máy khách

Ngày 2026-10-08. Rà soát nhánh `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, cùng working tree chưa commit. Giữ các thay đổi có sẵn. Người dùng đã yêu cầu triển khai sau lượt yêu cầu lập kế hoạch; tài liệu này theo dõi lần đóng gói mới, không thay bằng chứng lịch sử trong kế hoạch offline trước.

## 1. Mục tiêu và phạm vi

Tạo một ZIP mới Windows x64 để gửi trực tiếp cho khách, giữ gói `1.0.0-20261008` cũ. Bộ ZIP chứa desktop self-contained, frontend, updater, FFmpeg/FFprobe, PaddleOCR Anh/Trung, Qwen3 4B CPU cùng worker, payload giọng Piper và đủ dependency của các engine này. Không tự thêm CUDA, Kokoro đầy đủ hoặc model chuyển giọng Veo vào danh mục đã thống nhất. Feature ngoài danh mục phải được ghi rõ là tùy chọn hoặc chưa có payload offline.

Máy khách vẫn cần Windows x64, WebView2 Runtime, đủ RAM/commit/đĩa, truy cập server để đăng nhập/license và SQL workflow chuyển tiếp được cấp quyền. Tài nguyên model có sẵn trong ZIP; mỗi máy phải vượt probe trước khi READY. Không copy marker READY, token, cache người dùng, Python hệ thống hoặc DLL từ System32 vào ZIP.

Đầu ra thực tế: `D:\laptrinhweb\DesktopPackages\1.0.1-20261008\VideoMaker-1.0.1-20261008-Qwen-Piper-DIAGNOSTIC.zip`, checksum SHA-256, inventory, báo cáo qualification, hướng dẫn cấu hình SQL trên máy khách và lệnh chẩn đoán. Version/build là `1.0.1 / 20261008`. Đây là candidate đủ payload, chưa đạt toàn bộ gate để gọi là bản khách đã nghiệm thu. Không upload/Active release hay tăng giới hạn server chỉ để giao ZIP trực tiếp.

## 2. Bằng chứng đầu vào hiện hành

- Gói cũ được kiểm trực tiếp tại `D:\laptrinhweb\OneDrive\Documents\1.0.0-20261008`: ZIP đúng SHA-256, GGUF đúng 2.497.280.256 byte/hash pin. Inventory 325 file khớp ngoại trừ cấu hình cạnh EXE đã thay sau đóng gói.
- Worker thiếu bốn DLL MSVC trong thư mục riêng. Native preflight của đúng worker cũ nạp chúng từ System32 máy đóng gói. Đây là thiếu sót đóng gói đã xác minh; nguyên nhân chính xác trên máy khách vẫn chưa có diagnostics.
- Marker Qwen trên máy đóng gói khớp fingerprint worker cũ, dùng profile CPU ít RAM. Marker trên máy này không chứng nhận máy khách.
- ZIP cũ chứa `appsettings.json={}`; bản giải nén đang dùng SQL host phát triển và Windows Authentication. Không đưa cấu hình này vào ZIP mới.
- Harness `--check-qwen-offline` dùng root tạm khác allowlist worker. Phải sửa harness trước khi dùng nó làm bằng chứng chạy model thật.
- FFmpeg provenance hiện ghi Development; không tự đổi nhãn thành Release. Cấu hình SQL khách và bài probe trong điều kiện thiếu commit cần được xử lý đúng quy tắc repository.

## 3. Task và tiêu chí hoàn tất

| Task | Phạm vi triển khai | Tiêu chí hoàn tất |
|---|---|---|
| C01 | Chốt danh mục, output/version/build và cơ chế SQL khách | Có danh mục cơ bản ở mục 1; output mới; thông tin không chứa secret. SQL desktop vẫn chỉ `vf`/`VideoMakerDesktopRole`, không giả định đã chuyển sang API. |
| C02 | Đóng MSVC cho worker | Worker build/publish có đúng bốn DLL từ input Microsoft đã pin và notice/definition. Custom đường input được truyền đồng nhất. Không dùng System32 làm nguồn. |
| C03 | Kiểm native và readiness | Worker từ chối DLL thiếu/hỏng trước native load, trả mã repair ổn định. Fingerprint readiness bao gồm MSVC để marker cũ không che thay đổi dependency. Xác minh module thực nạp từ worker của ZIP. |
| C04 | Sửa offline harness | Kho tạm mới trong TEMP, tên GUID và marker sở hữu; chỉ cấp cho child diagnostic. Không nới root thường, hash/model/profile/protocol. Có regression chứng minh root hợp lệ qua bước path nhưng bytes giả vẫn bị chặn. |
| C05 | Regression và kiểm pipeline | Thiếu/hỏng DLL bị từ chối cả khi System32 có runtime; workspace ngoài TEMP/sibling/thiếu marker bị chặn; script publish kiểm cả runtime desktop và worker. Các bài model giả không được tính là model thật đạt. |
| C06 | Cấu hình khách | HTTPS đúng môi trường; tránh DUNGDEV, đường dev và secret trong ZIP. SQL lấy từ cơ chế triển khai riêng trên máy khách hoặc Windows Authentication đã cấp quyền; không đóng mật khẩu đã gửi trong hội thoại. Feature Qwen/Piper/OCR phù hợp payload. |
| C07 | Hồ sơ phân phối | Giữ license/provenance/checksum của FFmpeg, Qwen, LLamaSharp, OCR, Python/uv/Piper, MSVC và preview thực có trong gói. Gate Release không bị bypass; mọi phê duyệt mới phải có rà soát tương ứng. |
| C08 | Build frontend/solution | Chạy lệnh chuẩn ở mục 4, ghi source snapshot và Passed/Failed/Skipped thật. Giải quyết lỗi phát sinh trong phạm vi; giữ bằng chứng lượt lỗi. |
| C09 | Publish và staging | Publish từ source đã kiểm, toàn bộ worker/native/OCR/Piper/media, GGUF đúng pin; không lấy EXE cũ thay source mới. Bổ sung hướng dẫn/config provisioning/diagnostics trước khi chốt manifest/inventory. |
| C10 | ZIP lớn và integrity | Nén stream hỗ trợ GGUF >2 GiB, Unicode; giải nén chính ZIP, kiểm size/hash/inventory. Đo dung lượng thực và ghi tương thích server/updater; gửi trực tiếp không chứng minh update server đạt. |
| C11 | Probe runtime từ ZIP | WebView2, FFmpeg, OCR en/zh, Piper WAV đạt; Qwen load + Runtime + en→vi + zh→vi đều đạt, 0 HTTP tải model. Tài nguyên kiểm trước; warning chỉ được vượt khi người dùng chấp thuận rõ. |
| C12 | Windows/profile sạch | User thường, không VC++/.NET/Python dev, cache mới, path có dấu/khoảng trắng và CWD khác repo. Kiểm startup gate, retry/cancel, mở lại, dịch fixture/tạo WAV và đọc log an toàn. Nếu môi trường chưa có, ghi chưa xác minh. |
| C13 | Chốt và bàn giao | ZIP cuối/hash/inventory/qualification/hướng dẫn đặt cùng output mới. Giữ workspace/credential/gói cũ. Chỉ gọi sẵn sàng bàn giao khi các gate thực đạt; thiếu điều kiện thì ghi chính xác và tiếp tục công việc độc lập. |

Thứ tự: C01 → C02/C03/C04/C05/C06/C07 → C08 → C09 → C10 → C11 → C12 → C13. Không chạy model thật cùng build/OCR nặng. Thu diagnostics của lỗi khách nếu có; không kết luận mọi `TRANSLATION_PROCESS_FAILED` đều do MSVC.

## 4. Kiểm thử bắt buộc

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Trong `TOOL-LOCAL\Web`:

```powershell
npm ci --no-audit --no-fund
npm run build
npm test
```

Regression worker/offline chạy riêng để kiểm nhánh mới. Sau build/test mới chạy probe payload thật theo thứ tự, không song song. Báo riêng các bài opt-in bị Skip. Probe Qwen phải dùng chính worker/model trong ZIP đã giải nén, root/cache mới và HTTP bị chặn; native dry-run không thay load/inference thật.

Ma trận lỗi: DLL thiếu/sửa byte; GGUF thiếu/sai size/hash; root tạm không được cấp/thiếu marker/reparse; RAM/commit thiếu; cancel/timeout; retry; mở lại với fingerprint mới; config SQL chưa provisioned. Giữ gate host/React, không ghi tay READY và không bỏ kiểm hash để đạt.

## 5. Cấu hình và nghiệm thu máy khách

SQL là điều kiện riêng của giao diện nghiệp vụ. Cơ chế credential phải được chốt trước khi tuyên bố mở app sử dụng đầy đủ; không gửi mật khẩu trong câu trả lời/log/source/ZIP. Không tự chạy migration, thử provider có phí, đăng TikTok, hoặc sửa quyền trên database thật. API login/license vẫn dùng HTTPS của môi trường người dùng đã chỉ định.

WebView2 loader được đóng trong ZIP; Runtime trình duyệt là prerequisite phải kiểm trên máy đích. Nếu cần bundle offline Runtime, dùng bộ Microsoft có version/chữ ký/nguồn được xác minh, cập nhật inventory và đo lại ZIP. Model/runtime đã có cache trên máy build không được dùng để thay phép cài mới.

## 6. Trạng thái triển khai mới

- C02–C05 đã triển khai và kiểm regression: worker kèm DLL MSVC pin, xác minh trước load, preload bằng đường tuyệt đối và kiểm module thực nạp. Chỉ copy DLL chưa đủ vì lượt regression đầu vẫn phát hiện native nạp System32; đã sửa và kiểm lại đạt. Fingerprint READY bao gồm bốn DLL. Harness dùng workspace TEMP có marker sở hữu và grant riêng cho child.
- C06 có cấu hình HTTPS đúng môi trường, SQL để trống trong ZIP, cùng `Configure-CustomerSql.ps1` tạo override riêng có ACL cho user hiện tại. Chưa provision SQL/quyền trên máy khách; chưa truy cập database đích.
- C07 giữ nguyên provenance/license/checksum; FFmpeg vẫn có approval scope Development, chưa đạt Release approval. Không tự đổi nhãn hoặc bỏ gate.
- C08 đạt restore, build Release (0 lỗi/0 cảnh báo C#), frontend build và kiểm thử. .NET **1.600 Passed / 0 Failed / 13 Skipped**; regression **36 Passed / 0 Failed / 0 Skipped**; frontend **280 Passed / 0 Failed / 0 Skipped**, 44 file. Các bài model opt-in bị skip không được tính là model đã chạy.
- C09–C10 đã publish từ working tree mới và tạo ZIP **3.053.177.788 byte**; giải nén **3.708.142.289 byte**, 336 file. Inventory kiểm 335 file (không tự hash inventory); checksum ZIP đọc lại độc lập khớp `5a99b57643f88a4e4d3f238fbef827686817a4bcb5febfb1ba639977479ae3f0`. Đường giải nén có dấu. OneDrive có reparse attribute nên output chuyển sang thư mục thường; giữ nguyên gói cũ.
- C11 đạt WebView2 trên máy build, FFmpeg/FFprobe xử lý fixture, OCR Anh/Trung, Piper cài private runtime từ payload và tạo WAV, Qwen artifact/hash cùng native CPU AVX2 preflight. Worker của ZIP nạp cả bốn DLL từ chính `_translation_worker`. **Chưa load weights/dịch Qwen thật**: lượt offline harness dừng `TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED`, stage `VERIFYING`; snapshot commit trống 2,73 GiB < ngưỡng low-memory 4 GiB. Không truyền cờ chấp thuận khi người dùng chưa xác nhận.
- C12 chưa xác minh Windows/profile sạch và startup/UI trên máy nhận. Kết quả probe trên máy build không chứng nhận máy khách.
- C13 đã có artifact cụ thể, checksum, qualification, native diagnostics, TRX và hướng dẫn tiếng Việt cùng output. Trạng thái `DevelopmentCandidate`, `CustomerReleaseQualified=false`; còn các điều kiện C06/C07/C11/C12 trước khi xác nhận dùng chính thức.

Chi tiết thay đổi, kết quả và phần chưa xác minh: [TRIEN_KHAI_ZIP_MAY_KHACH_DAY_DU.md](TRIEN_KHAI_ZIP_MAY_KHACH_DAY_DU.md). Bằng chứng được ghi trên commit nền cùng working tree đã inventory lúc publish; đây không phải bằng chứng rollout production.
