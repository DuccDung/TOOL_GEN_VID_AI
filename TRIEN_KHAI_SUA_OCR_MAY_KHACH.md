# Sửa dependency OCR cho bản ZIP trên máy khách

Ngày 2026-09-26. Nhánh `main`, commit nền `bc1c6aec3c731bdb0edcebbde17ae13640245e52`, cùng working tree chưa commit. Không thay cấu hình server/SQL gốc, dữ liệu dự án hoặc server đang deploy.

## 1. File bàn giao

Thư mục: `artifacts/portable/ocr-fix-1.0.0-2-20260926/`.

### Bản vá cho ứng dụng đã giải nén trên máy khách

[taphoatool-ocr-runtime-win-x64-patch.zip](artifacts/portable/ocr-fix-1.0.0-2-20260926/taphoatool-ocr-runtime-win-x64-patch.zip), **379.350 byte**, khoảng **370 KiB**.

1. Thoát hẳn taphoatool trên máy khách.
2. Giải nén toàn bộ bản vá vào thư mục đang chứa `TOOL-LOCAL.exe`. Bốn DLL phải nằm cùng cấp EXE, giữ thư mục notice đi kèm.
3. Mở lại ứng dụng, bấm **Kiểm tra lại** nếu modal còn hiện.

Bản vá bổ sung Visual C++ cho OCR; giữ nguyên `appsettings.json`, workspace và model đang dùng. Đã áp dụng chính ZIP vá lên bản publish **build 1** cũ tại máy kiểm, chạy OCR fixture thật và quan sát cả bốn DLL được nạp từ thư mục ứng dụng. Không sửa DLL của Windows. Việc thoát/mở lại là cần thiết vì lỗi khởi tạo thư viện có thể được giữ trong tiến trình cũ.

SHA-256: `cc4656d54101139189aa9643a5853299fb2f94e7827c46ce8fd8b80047daf905`.

### ZIP đầy đủ mới

[taphoatool-1.0.0-2-win-x64-ocr-fix.zip](artifacts/portable/ocr-fix-1.0.0-2-20260926/taphoatool-1.0.0-2-win-x64-ocr-fix.zip), **624.978.847 byte**, khoảng **596 MiB**. Version `1.0.0`, build **2**, `Stable`, `win-x64`.

Có ứng dụng, OCR/model, Visual C++ x64 cho OCR, FFmpeg, WebView2 loader, Piper offline/model Việt và các notice. Giải nén toàn bộ vào thư mục mới. **ZIP đầy đủ vẫn là ứng viên không kèm mật khẩu SQL dùng chung**, cần cấu hình xác thực SQL riêng để dùng workflow. Bản vá nhỏ ở trên phù hợp khi chỉ cần bổ sung runtime cho bản đã cấu hình trên máy khách.

SHA-256: `8ecc5d36efc83d29214fb00cc53f027c7e9f554081ca09aaea55e3c724f0d814`.

[Checksum hai ZIP](artifacts/portable/ocr-fix-1.0.0-2-20260926/checksums.sha256).

## 2. Nguyên nhân và thay đổi

Thông báo trong ảnh ánh xạ tới `OCR_NATIVE_LOAD_FAILED`. Kiểm import của đúng DLL trong ZIP cũ cho thấy `onnxruntime.dll` cần `MSVCP140`, `VCRUNTIME140`, `VCRUNTIME140_1`; `mkldnn.dll` cần `VCOMP140`. Gói cũ chưa mang các DLL này cạnh desktop, trong khi máy build đã có Visual C++. Runtime trong payload Python/Piper chạy ở tiến trình/thư mục riêng nên không thay thế dependency OCR.

Đây là thiếu sót đã xác minh của ZIP. Chưa truy cập máy khách để chứng minh nó là nguyên nhân duy nhất của thông báo; thiếu/hỏng Media Foundation hoặc file khác trên máy đích vẫn cần được phân biệt.

- Thêm [định nghĩa runtime](third_party/ocr/MSVC_RUNTIME.json) và [notice Microsoft](third_party/ocr/MSVC-NOTICE.md): phiên bản **14.50.35719.0**, nguồn installer Microsoft có SHA-256 cố định, bốn DLL có chữ ký hợp lệ khi kiểm trên máy chuẩn bị.
- [Prepare-OcrNativeRuntime.ps1](scripts/Prepare-OcrNativeRuntime.ps1) chỉ trích CAB của installer đã pin; không chạy installer/MSI, không lấy DLL từ System32, không đổi máy khách hoặc Windows của máy build.
- [TOOL-LOCAL.csproj](TOOL-LOCAL/TOOL-LOCAL.csproj) đưa bốn DLL cạnh EXE ở cả build và publish, giữ chúng ngoài single-file. Build/publish kiểm hash bắt buộc. Nguồn mặc định là `artifacts/ocr-native-runtime/msvc-14.50.35719.0-win-x64`, bị Git ignore; checkout mới phải chuẩn bị bundle trước build.
- [OcrNativeDependencies.cs](TOOL-LOCAL/Vietsub/Ocr/OcrNativeDependencies.cs) dùng định nghĩa nhúng trong assembly để kiểm size/hash; không tin sidecar có thể sửa. OCR thiếu/hỏng runtime trả `OCR_NATIVE_DEPENDENCY_MISSING` hoặc `OCR_NATIVE_DEPENDENCY_INVALID` trước inference. Thiếu Media Foundation trả `OCR_WINDOWS_MEDIA_MISSING`.
- [Kiểm ZIP](scripts/Test-DesktopSetupPublish.ps1), [kiểm runtime](scripts/Test-DesktopBundleRuntime.ps1) và script release yêu cầu đủ runtime. `--check-bundled-components` còn xác minh bốn module đã nạp từ thư mục ứng dụng; PATH chỉ có Windows không được dùng làm bằng chứng thay thế.

## 3. Kiểm thử mới trong lượt triển khai

| Phép kiểm | Kết quả |
|---|---|
| Restore / build solution Release | Đạt; build 0 warning / 0 error. |
| Frontend `npm ci`, build, test | Đạt; **280 Passed / 0 Failed / 0 Skipped**, 44 file. Vite còn cảnh báo chunk lớn. |
| Regression OCR có runtime thật | **14 Passed / 0 Failed / 0 Skipped**; gồm sáu ca mới về DLL thiếu/hỏng/sidecar và các ca OCR hiện có. |
| Full C# | **1.576 Passed / 0 Failed / 13 Skipped**, 271,39 giây; 0 tiến trình con còn lại. |
| ZIP cũ qua guard mới | Bị từ chối đúng vì thiếu runtime. |
| Publish build 2 | Thành công, self-contained x64, yêu cầu đủ FFmpeg/Piper. |
| ZIP build 2 sau giải nén có dấu/khoảng trắng | 56 thành phần, 24 web asset; 0 thiếu/sai. OCR Anh/Trung, FFmpeg, WebView2 READY. |
| Nguồn DLL đã nạp | Cả bốn module `AppLocal=true` trong tiến trình chẩn đoán mới. |
| Bỏ `vcomp140.dll` ở bản giải nén thử | Exit 2, `OCR_NATIVE_DEPENDENCY_MISSING`; runtime trong Windows không che được file thiếu. |
| Sửa một byte, giữ nguyên dung lượng DLL | Exit 2, `OCR_NATIVE_DEPENDENCY_INVALID`; khôi phục file và đối chiếu inventory đạt. |
| Piper từ ZIP build 2 | Cài mới offline, tạo WAV và kiểm lại ba lần đều READY. |
| ZIP vá trên EXE build 1 cũ | OCR/FFmpeg READY; quan sát bốn DLL nạp từ thư mục ứng dụng, cấu hình kết nối giữ nguyên. |
| Cấu hình gốc / source | Hash cấu hình gốc giữ nguyên; 993 file source vẫn khớp snapshot Full. |

13 bài C# bị Skipped vẫn là các bài opt-in; không tính là model/SQL/GPU đã đạt. Piper thật được kiểm riêng từ ZIP như trên. Windows kiểm hiện tại là **10.0.26200.0 x64**, đã có Visual C++; phép kiểm module và thiếu/hỏng DLL bổ sung bằng chứng chạy với runtime của gói, **không thay cho máy Windows sạch hoặc máy khách thực tế**.

Source manifest Full: `8FC18491B0ECD7C8EF8E7C4E33E2CC4DDE37054A2C0B0DCAD9B3870D1FF465A2`. Binary manifest suite: `D43CB3D012B2BE1745ACA1AF2740E0193D3F1497A4159509367AD8E03E13EE7E`. Publish build 2 có kiểm artifact riêng sau suite; không gán manifest test build 1 cho EXE publish build 2.

## 4. Bằng chứng

- [Kết quả package](artifacts/portable/ocr-fix-1.0.0-2-20260926/evidence/package-check.json), [module đã nạp](artifacts/portable/ocr-fix-1.0.0-2-20260926/evidence/zip-native-runtime.json).
- [Thiếu/hỏng DLL](artifacts/portable/ocr-fix-1.0.0-2-20260926/evidence/zip-negative-native-runtime.json), [Piper](artifacts/portable/ocr-fix-1.0.0-2-20260926/evidence/zip-piper.json).
- [Bản vá trên build 1](artifacts/portable/ocr-fix-1.0.0-2-20260926/evidence/patch-old-build.json), [Full C#](artifacts/portable/ocr-fix-1.0.0-2-20260926/evidence/full-summary.json), [regression OCR](artifacts/portable/ocr-fix-1.0.0-2-20260926/evidence/ocr-regression.trx).
- Log chuẩn bị/build: `D:\vmocr\20260926-ocr-runtime`. Publish, workspace Piper và script kiểm: `C:\vmocr\20260926-ocr-runtime`.

Lần khởi động Full trên D: bị guard dung lượng chặn trước khi chạy test; lần nghiệm thu trên C: đạt như bảng. Thao tác dọn các bản giải nén tạm cũ bị công cụ chặn, các thư mục đó được giữ nguyên. Không giảm ngưỡng dung lượng hoặc sửa timeout/test để đạt.

## 5. Phần cần xác minh trên máy khách

- Áp bản vá và mở lại để xác nhận OCR READY trên chính máy đã báo lỗi. Chưa biết phiên bản Windows hoặc trạng thái Visual C++/Media Foundation của máy đó.
- Windows N/Server thiếu Media Foundation cần bật thành phần Windows phù hợp. ZIP không mang DLL hệ thống thay thế; WebView2 Runtime vẫn là prerequisite.
- Chưa đăng nhập/nghiệm thu workflow SQL trên máy khách. Bản vá giữ cấu hình tại chỗ; ZIP đầy đủ không chứa mật khẩu SQL dùng chung.
- Chưa upload/Active release hoặc thay dịch vụ sửa chữa trên server; thông báo không lấy được package repair là phần riêng.
- Giữ nguyên trạng thái hồ sơ phân phối trước đó: không tự nâng FFmpeg Development thành Release hay thay license/provenance cũ. Notice Microsoft mới ghi nguồn kỹ thuật và dẫn điều khoản gốc; phê duyệt phân phối công khai vẫn theo quy trình hiện hành.
