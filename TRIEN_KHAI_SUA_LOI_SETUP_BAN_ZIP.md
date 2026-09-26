# Triển khai sửa Setup cho bản ZIP

Ngày 2026-09-26. Source: nhánh `main`, commit nền `bc1c6aec3c731bdb0edcebbde17ae13640245e52`, cùng thay đổi chưa commit của đợt này. Giữ nguyên thay đổi có sẵn trong `TOOL-LOCAL/appsettings.json`.

## Hành vi đã sửa

- Đã tái hiện lỗi `Cannot marshal: Encountered unmappable character` khi OpenCV đọc ảnh kiểm tra OCR trong đường dẫn có dấu. Cùng gói ở đường dẫn không dấu đạt. Adapter nay đọc bytes bằng .NET rồi giải mã ảnh bằng OpenCV; có regression chạy thật OCR Anh/Trung từ thư mục tiếng Việt. Đây là nguyên nhân đã chứng minh trên gói thử; chưa xác nhận là nguyên nhân của máy trong ảnh.
- Khi OCR cần sửa và Piper chưa cài, nút **Cài giọng Việt** cài riêng Piper. **Kiểm tra lại** chạy kiểm tra component mà không cần package repair trên server. Trong lúc chạy có thao tác hủy; nút xung đột bị khóa.
- Cài xong Piper vẫn giữ modal và host gate nếu OCR hoặc component bắt buộc khác chưa sẵn sàng.
- Repair progress/failure gắn request ID. Phản hồi đến muộn hoặc của yêu cầu khác không làm UI quay lại trạng thái bận sau khi đã báo lỗi.
- OCR có mã lỗi an toàn cho lỗi nạp native/dependency, định dạng/kiến trúc, thiếu file và lỗi nạp assembly. UI có mục **Thông tin hỗ trợ**. Không suy đoán dependency cụ thể từ ảnh; không hiển thị nội dung exception hoặc đường dẫn riêng.
- API repair trả mã `desktop_repair_package_not_found` khi xác định không có release phù hợp. Desktop phân biệt mã đó với 404 rỗng/HTML/server cũ. Lỗi có hướng lấy lại ZIP đầy đủ, cùng version/build khi cần đối chiếu; giữ nguyên quy tắc repair đúng phiên bản.
- Script phát hành kiểm channel/platform, kiểm các native DLL của Paddle đã pin, rồi giải nén ZIP vừa tạo và chạy thật fixture OCR Anh/Trung, FFmpeg và kiểm WebView2. Lỗi probe làm script dừng.

Piper tiếp tục dùng installer, runtime/model đã pin và phép kiểm WAV hiện có. ZIP cần mạng để chuẩn bị Piper ở lần đầu. Không chép môi trường Python hoặc marker READY từ máy phát triển sang máy khách.

## Source chính

- UI: [StartupSystemSetupModal.tsx](TOOL-LOCAL/Web/src/features/systemSetup/StartupSystemSetupModal.tsx), [useSystemSetup.ts](TOOL-LOCAL/Web/src/features/systemSetup/useSystemSetup.ts).
- OCR: [VietsubOcrRuntimeDiagnostics.cs](TOOL-LOCAL/Vietsub/Ocr/VietsubOcrRuntimeDiagnostics.cs), [SystemSetupAdapters.cs](TOOL-LOCAL/SystemSetup/SystemSetupAdapters.cs).
- Repair: [contract](TOOL-SHARED.Contracts/Updates/DesktopUpdateContracts.cs), [controller](TOOL-SERVER/Controllers/DesktopUpdatesController.cs), [DesktopRepairErrors.cs](TOOL-LOCAL/Updates/DesktopRepairErrors.cs), [Form1.cs](TOOL-LOCAL/Form1.cs).
- Đóng gói: [Publish-DesktopRelease.ps1](scripts/Publish-DesktopRelease.ps1), [Test-DesktopSetupPublish.ps1](scripts/Test-DesktopSetupPublish.ps1), [Test-DesktopBundleRuntime.ps1](scripts/Test-DesktopBundleRuntime.ps1).
- Chẩn đoán: [DesktopReadinessCommand.cs](TOOL-LOCAL/SystemSetup/DesktopReadinessCommand.cs) thêm `--check-bundled-components`, hoạt động trước khi đọc cấu hình triển khai; không đăng nhập, gọi server hoặc kiểm SQL, không cài/probe model Piper/Qwen đã cài.

## Kiểm chứng

File log/TRX và artifact chẩn đoán của lượt này nằm tại `D:\VideoMakerDiagnostics\zip-setup-20260926` trên máy phát triển. Không phải kết quả trên Windows sạch hoặc máy khách.

Máy thử: Windows 11 Home Single Language x64, build `26200`, Intel Core i7-11800H, RAM khả dụng cho hệ điều hành khoảng 15,8 GB. Metadata không chứa tên tài khoản/máy được lưu trong `machine-metadata.json`.

| Kiểm tra | Passed | Failed | Skipped | Bằng chứng/giới hạn |
|---|---:|---:|---:|---|
| Restore solution, `npm ci`, build Release, build frontend | Đạt | 0 | 0 | MSBuild 0 warning/error; Vite còn cảnh báo chunk lớn hơn 500 kB. |
| Frontend toàn bộ | 272 | 0 | 0 | 43 file; `web-tests.log`. |
| C# Setup/repair, sau sửa đường dẫn OCR | 77 | 0 | 1 | `zip-setup-targeted-final.trx`; bài cài Piper thật là opt-in. |
| C# toàn bộ, chạy mặc định sau sửa OCR | 1541 | 1 | 13 | `zip-setup-full-final.trx`; `VietsubCloudDesktopTests.Cloud_TranslatesEntireTrackAndPreservesManualLockedAndValidLocalCues` hết hạn chờ ở storage/job. Không tính lượt này là đạt. |
| C# toàn bộ, collection tuần tự sau sửa OCR | 1542 | 0 | 13 | `zip-setup-sequential-final.trx`; 1555 bài, 5 phút 12 giây. Test opt-in bị bỏ qua chưa được coi là nghiệm thu. |
| Piper cài mới, opt-in, thư mục cô lập | 0 | 1 | 0 | `piper-clean.trx`; lỗi phân giải tên `github.com:443`, chưa tải/cài/probe thành công. |
| Gói ZIP mới trong đường dẫn có dấu | Đạt | 0 | 0 | `bundle-fixed.json`; 50 thành phần bắt buộc, 24 web asset khớp build; OCR Anh/Trung, FFmpeg và WebView2 READY. PATH chỉ có Windows, working directory nằm ngoài bundle. |
| Gói thử bị thiếu native OCR | Đạt | 0 | 0 | `bundle-negative.json`; thiếu `paddle_inference_c.dll` bị chặn bởi inventory và probe trả `OCR_NATIVE_LOAD_FAILED`. Đã trả lại file và kiểm hash khớp. |
| Cú pháp PowerShell / diff whitespace | Đạt | 0 | 0 | Ba script được parse; `git diff --check` đạt. |

Regression OCR có bằng chứng trước/sau: `ocr-unicode-before.trx` thất bại tại `Cv2.ImRead` với lỗi marshal ký tự, gói trước sửa ở đường dẫn không dấu đạt (`bundle-ascii-control.json`) nhưng ở đường dẫn có dấu lỗi (`bundle-failed.json`). Sau đổi sang decode bytes, test và ZIP thật trong đường dẫn có dấu đạt.

Lượt C# trước khi bổ sung regression Unicode: chạy mặc định **1539 Passed / 1 Failed / 13 Skipped** (test bridge cài bộ dịch), chạy tuần tự **1540 Passed / 0 Failed / 13 Skipped**. Đây là kết quả của bước trước, không thay lượt kiểm cuối. Source các test bridge/cloud bị lỗi và cơ chế khóa runtime dùng chung không bị thay đổi trong đợt này; nguyên nhân không ổn định của toàn suite chạy song song chưa được kết luận đầy đủ.

### Artifact chẩn đoán

- ZIP: `D:\VideoMakerDiagnostics\zip-setup-20260926\diagnostic-desktop-fixed.zip`.
- Version `1.0.0`, build `1`; kích thước `465267383` byte.
- SHA-256: `49364132267a69578592cdca7e01db93ce493163ff7f6d06a47f7082f0b072af`.
- Thư mục đã giải nén: `D:\VideoMakerDiagnostics\zip-setup-20260926\Bản sửa giải nén có dấu`.
- Gói được publish self-contained Windows x64/single-file bằng cấu hình chẩn đoán riêng, không chứa cấu hình server/database của người dùng. Đây là artifact kiểm chứng đóng gói; chưa phải bản phát hành để đưa khách sử dụng hoặc đăng repair.

Không chạy script phát hành production, không thay scope FFmpeg hoặc công bố release. Không truy cập SQL/provider có phí trong các probe này. Test Piper dùng thư mục mới riêng và runtime có version, không ghi vào component của tài khoản đang dùng.

## Trạng thái task trong kế hoạch

| Task | Trạng thái |
|---|---|
| Z01–Z03 | Chưa có xác nhận đúng ZIP/máy khách/server đích. Đã đối chiếu luồng source và tạo ca lỗi đường dẫn Unicode trên gói thử. |
| Z04 | Đã bổ sung mã lỗi OCR, mã lỗi fixture/probe và báo cáo CLI an toàn; có test. |
| Z05 | Đã sửa lỗi đọc fixture trong đường dẫn có dấu theo regression thực. Những nguyên nhân khác trên máy khách còn cần chẩn đoán. |
| Z06 | Luồng cài Piper riêng và thông báo đã có; giữ installer/checksum/probe hiện có. Nghiệm thu cài mới bị chặn ở DNS trong lượt thử thật. |
| Z07–Z08 | Đã triển khai hành động Setup, giữ hai lớp gate, xử lý request repair và tương thích 404 server cũ; có regression. Repair thành công trên server đích chưa thử. |
| Z09 | Đã thêm bước kiểm sau giải nén vào script; ZIP chẩn đoán đã chạy probe thật và thử thiếu native DLL. |
| Z10 | Đã chạy build, frontend và C#; kết quả từng lượt ghi riêng ở bảng trên. |
| Z11–Z13 | Chưa nghiệm thu Windows sạch/máy lỗi, workflow đích hoặc rollout. |
| Z14 | Đã có hướng dẫn chẩn đoán/bàn giao và ghi rõ các phần chưa xác minh. |

## Phần cần đối chiếu với môi trường thực tế

- Chưa xác nhận đúng ZIP đã gửi và chưa chạy trên máy lỗi. Đường dẫn ZIP trong tài liệu cũ chỉ là đầu mối, chưa được xem là artifact của sự cố này. Đã sửa lỗi đường dẫn có dấu trên gói thử, nhưng chưa kết luận được nguyên nhân OCR trên máy khách. Engine và dependency được giữ theo phiên bản đã pin.
- Chưa truy cập server đích hoặc đăng package repair. Server cần bản API mới để trả mã thiếu package cụ thể; desktop mới vẫn xử lý an toàn 404 của server cũ.
- Chưa nghiệm thu Windows sạch/tài khoản thường, quyền ghi thấp, mất mạng giữa cài đặt hoặc hết đĩa trên máy đích.
- Chưa nghiệm thu đăng nhập/license/SQL workflow và xuất video cuối trên môi trường đích. Component READY không chứng minh các phần này hoạt động.
- Chưa rollout production. Gate FFmpeg Release, provenance/license và cấu hình triển khai của quy trình phát hành vẫn được giữ nguyên.

## Cách kiểm tra và bàn giao ZIP

Tạo gói qua script phát hành với version/build, channel, cấu hình triển khai và FFmpeg đã đủ điều kiện trong [runbook](VAN_HANH_VA_PHAT_HANH.md). Giải nén toàn bộ, giữ cấu trúc thư mục. Không đóng gói workspace, token hoặc dữ liệu tài khoản của người build.

Kiểm gói tại máy kiểm thử có checkout tương ứng:

```powershell
.\scripts\Test-DesktopSetupPublish.ps1 -PublishRoot 'D:\ThuMucDaGiaiNen' -ProbeWebView2 -ProbeBundledComponents
```

Trên máy gặp lỗi, chạy chẩn đoán từ bản có thay đổi này bằng PowerShell; thay đường dẫn EXE cho đúng:

```powershell
$probeExe = 'D:\ThuMucDaGiaiNen\TOOL-LOCAL.exe'
$probeJson = Join-Path $env:TEMP 'taphoatool-bundled-check.json'
$probeStderr = Join-Path $env:TEMP 'taphoatool-bundled-check.stderr'
$probeProcess = Start-Process -FilePath $probeExe -ArgumentList '--check-bundled-components' -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $probeJson -RedirectStandardError $probeStderr
Get-Content -LiteralPath $probeJson
$probeProcess.ExitCode
```

Báo cáo JSON chứa version/build, trạng thái và mã lỗi component. Chỉ gửi báo cáo JSON đã kiểm nội dung; không gửi cấu hình, token hoặc log stderr thô. CLI không sửa bản đang dùng hoặc tự tải model. Exit code `0` chỉ xác nhận phạm vi component trong báo cáo trên máy vừa chạy.

Nếu server chưa có package repair, lấy lại bộ ZIP đầy đủ, thoát ứng dụng và giải nén vào thư mục mới. Giữ workspace, preferences và component của người dùng. Kiểm bản mới trước khi thay thư mục chạy cũ.
