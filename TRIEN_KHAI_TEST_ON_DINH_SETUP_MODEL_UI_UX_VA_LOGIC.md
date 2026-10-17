# Triển khai kiểm thử ổn định Setup/model — UI, UX và logic

Ngày: **2026-10-17**. Nhánh `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b` và working tree chưa commit. Giữ nguyên các thay đổi có sẵn ngoài phạm vi.

Đã triển khai regression, sửa lỗi phát hiện trong source và kiểm thử trên máy phát triển Windows 11. **Chưa nghiệm thu toàn bộ 32 task/56 ca trên Windows sạch, chưa phát hành Setup khách và chưa chạy soak model thật.** Đọc cùng [kế hoạch](PLAN_TASK_TEST_ON_DINH_SETUP_MODEL_UI_UX_VA_LOGIC.md) và [biên bản luồng cài VPS](TRIEN_KHAI_SETUP_CAI_MODEL_TU_VPS.md).

## 1. Lỗi đã phát hiện và thay đổi

| Nhóm | Lỗi/rủi ro đã xử lý | Source và bằng chứng |
|---|---|---|
| WinForms | Cửa sổ 500×440 che nút Cài/Hủy và cắt nhãn/lỗi dài; nút cuối nay được giữ riêng, các vùng nội dung/trạng thái cuộn được | [InstallerForm](TOOL-SETUP/InstallerForm.cs); ảnh native trước/sau và regression layout |
| Vòng đời UI | Chặn nhấp lặp; hủy request lấy phiên bản khi đóng; bỏ progress của lượt cũ; chạy install ngoài UI thread và marshal xác nhận về UI | [StaticSetupFormTests](TOOL-TESTS/Updates/StaticSetupFormTests.cs) dùng session cô lập, không gọi server |
| Tiến độ | Prerequisite không đẩy lên 85% trước tải model; reporter giữ phần trăm không lùi và tên bước rõ hơn | [LauncherInstallerService.Static](TOOL-SETUP/LauncherInstallerService.Static.cs), [StaticBootstrapRunner](TOOL-SETUP/StaticBootstrapRunner.cs) |
| React readiness | Snapshot rỗng/thiếu/trùng/sai không mở READY; phản hồi hỏng sau READY bị cách ly; khóa nền ngay khi chờ GET đầu | [types](TOOL-LOCAL/Web/src/features/systemSetup/types.ts), [App](TOOL-LOCAL/Web/src/App.tsx), các regression snapshot/startup |
| Bridge/context | Đổi user bỏ state cũ; ACK mất có thời hạn/thông báo và đối soát lại cùng operation; lỗi/event cũ không làm hỏng lượt mới | [useSystemSetup](TOOL-LOCAL/Web/src/features/systemSetup/useSystemSetup.ts) |
| Repair | Progress đến muộn không mở lại modal sửa ứng dụng giả; phân biệt request do App và Setup sở hữu; giữ luồng repair thường | Regression `StartupSystemSetupApp.test.tsx`; giữ cả log fail-before |
| UX/accessibility | Reset consent RAM khi đổi profile/context; trả focus; ưu tiên overlay; reduced motion; nhãn trạng thái dài xuống dòng trong cột | Modal/panel/CSS và fixture WebView2 thật |
| Result/probe | Đòi đúng ba/năm component, fingerprint SHA-256 và trạng thái lỗi nhất quán; từ chối JSON trùng/unknown field, null/sai/oversize | [StaticBootstrapRunner](TOOL-SETUP/StaticBootstrapRunner.cs), [StaticModelComponentEvidence](TOOL-LOCAL/ModelDistribution/StaticModelComponentEvidence.cs) |
| Receipt/commit | Receipt model schema 2 gắn máy/app/root và evidence. Bản compile bắt buộc Setup còn yêu cầu app receipt và journal Committed khớp operation/target/version/build/package | [StaticModelRuntimePolicy](TOOL-LOCAL/ModelDistribution/StaticModelRuntimePolicy.cs), [StaticModelStartupReceiptTests](TOOL-TESTS/SystemSetup/StaticModelStartupReceiptTests.cs) |
| Đường dẫn metadata | Lượt lặp dùng TEMP có dấu phát hiện journal dẫn xuất dài 240 ký tự bị áp nhầm giới hạn CLI 220; đã tách reader metadata có containment/root/reparse khỏi giới hạn CLI/native/cache vẫn giữ 220 | Regression metadata Unicode dài; không chuyển fixture về đường ASCII để làm test đạt |
| Trust | Kiểm thu hồi đã biết/trust hỏng tại policy/prerequisite trước tải model lớn; dữ liệu lỗi không thoát thành exception ngoài chẩn đoán | [StaticModelBootstrapCommand](TOOL-LOCAL/SystemSetup/StaticModelBootstrapCommand.cs) |
| Crash/recovery | Temp copy xác định theo journal/operation, recovery dọn đúng temp sở hữu; không xóa file GUID khác. Marker Job được publish atomically | [StaticSetupTransaction](TOOL-SETUP/StaticSetupTransaction.cs), regression installer |
| Process | Kiểm trực tiếp Job Object production dừng cây cmd→ping thuộc fixture, giữ cây fixture độc lập | [StaticSetupProcessTests](TOOL-TESTS/Updates/StaticSetupProcessTests.cs), loopback duy nhất |

Receipt schema cũ không tự được nâng thành READY. Model/component có thể cần verify lại bằng đúng Setup. Không xóa marker/journal/trust thủ công để vượt gate. Cài đè legacy chưa tương thích vẫn bị chặn; rollback file ứng dụng không có nghĩa mọi runtime cũ lập tức dùng được.

## 2. Bộ kiểm thử và công cụ đã thêm

- [StaticModelTransportStabilityTests](TOOL-TESTS/Updates/StaticModelTransportStabilityTests.cs): hết bốn lượt retry, hủy trong backoff, headers/body treo, tải có tiến triển lâu hơn timeout headers, 416 lặp, Retry-After hết hạn, thiếu đĩa trước retry và hủy trước full hash rồi dùng lại part hợp lệ.
- [StaticModelStartupReceiptTests](TOOL-TESTS/SystemSetup/StaticModelStartupReceiptTests.cs): mất pending/journal/receipt ở các trạng thái, JSON hỏng, binding, fingerprint, lease, trust và digest file đổi/mất; tất cả dưới TEMP riêng.
- [StaticSetupInstallerTests](TOOL-TESTS/Updates/StaticSetupInstallerTests.cs): result strict, recovery/temp/marker/backup và progress.
- [StaticSetupFormTests](TOOL-TESTS/Updates/StaticSetupFormTests.cs), [StaticSetupProcessTests](TOOL-TESTS/Updates/StaticSetupProcessTests.cs): UI STA và process nhỏ do fixture sở hữu.
- Frontend thêm 19 regression; 13 ca có log fail-before ở ba đợt. Không thay assertion quyền/menu để bỏ qua initial gate; fixture Local trả READY đủ contract trước khi kiểm menu.
- [StaticSetupWebViewBrowserTests](TOOL-TESTS/SystemSetup/StaticSetupWebViewBrowserTests.cs) và [fixture](TOOL-LOCAL/Web/tests/static-setup-browser/probe.tsx): render component production bằng controller giả trong WebView2 thật, sáu tổ hợp viewport/zoom × ba trạng thái, keyboard Tab/Shift+Tab/Escape, focus, consent, layout và unmount READY. Đây không phải full App hoặc backend thật.
- [Test-Stability](scripts/Test-Stability.ps1) thêm profile `StaticSetup` để lặp nhóm logic/Form/process; tách browser khỏi vòng lặp này. Manifest kiểm thêm CSS/HTML/MJS/CJS/PSM1 và hash assembly Setup. Không sửa mức free disk tối thiểu hay tự bật model/SQL opt-in.

## 3. Kết quả kiểm tra trên source lần này

Kết quả cuối gắn với snapshot **1.151 file** tại `source-final-tests.json`, test assembly và log có SHA-256 trong [bảng kết quả](C:/vm-setup-verification/20261017-000053-stability/build-test-summary.json). Các nhóm focused/lặp đã nằm trong phạm vi full suite, không cộng thành số test duy nhất.

| Phép kiểm cuối | Passed | Failed | Skipped | Bằng chứng/phạm vi |
|---|---:|---:|---:|---|
| Restore và solution Release build | Đạt | 0 error | — | Hai warning Pkcs NU1902/NU1903 hiện hữu; `dotnet-build-unicode-verified.log` |
| Full .NET | **2.202** | **0** | **18** | `full-suite-final.trx`, tổng 2.220 bài, 4 phút 22 giây |
| Focused Setup/model | **160** | **0** | **0** | `static-stability-unicode-verified.trx` |
| Lặp `StaticSetup` 20/20 vòng | **3.180 lượt** | **0** | **0** | 159 bài/vòng; tập bài không đổi; không còn tiến trình fixture thuộc lượt kiểm; tổng 223,15 giây |
| Full frontend | **341** | **0** | **0** | 48 file; `npm ci`, production build và test đạt |

WebView2 thật đã chạy sáu tổ hợp viewport/zoom × ba trạng thái, kèm keyboard/focus/consent/unmount; thuộc focused/full suite và tách khỏi 20 vòng lặp. Hai ca Dispose/Terminate kiểm Job Object production với cây process do fixture sở hữu đều đạt; chưa thử kill Setup parent thật hoặc crash OS. Ảnh WinForms cuối kiểm bốn hệ số mô phỏng, DeviceDpi thực 120. Không đổi code sau các phép kiểm cuối; những chỉnh sửa biên bản sau đó được ghi riêng trong `source-after-final-tests-differences.json`.

Giữ nguyên bằng chứng các lượt trước để giải thích lỗi và cách sửa:

- Transport mini-project độc lập đạt 62/0/0; frontend targeted đạt 65/0/0. Đây là kết quả trung gian, không cộng vào kết quả cuối.
- Focused .NET đầu 157/2/0: thiếu catch `InvalidDataException` ở policy và fixture kiểm request trước khi WinForms Shown được pump. Đã sửa catch và chờ request thực sự bắt đầu, giữ assertion/timeout; lượt sau 159/0/0, cuối 160/0/0.
- WebView2 native đầu đạt nhưng ảnh còn cho thấy nhãn RAM tràn cột. Đã sửa wrap, thêm assertion hình học và chạy lại trên source cuối; giữ ảnh trước/sau riêng.
- Full .NET đầu 2.200/1/18: `VoiceRenderer_TwoHours_KeepsLastPhraseAndUsesBoundedIntermediateFiles` bị gate free disk của TEMP chặn. Trước lượt cuối, C còn 5.392.211.968 byte, đủ ngưỡng 4.753.190.912 byte; bài đạt trong full suite cuối. Không xóa/di chuyển dữ liệu, hạ gate hoặc sửa assertion/timeout để làm đạt.
- Lượt lặp đầu dừng đúng ở vòng 1/20 với 152/6/0: bốn ca receipt phát hiện giới hạn journal nội bộ, hai ca process fixture phát hiện quote `cmd /s /c` và batch UTF-8/OEM. Đã sửa reader metadata và fixture quote/batch, giữ TEMP có dấu và mọi assertion. Lượt 20 vòng cuối dùng source/binary mới, thư mục evidence riêng.

Chưa chạy inference/soak Qwen hoặc Piper thật và không tạo request provider có phí, truy cập database hay thay VPS. Full suite có các fixture OCR native Anh/Trung và video-frame đã chạy; đó là bằng chứng component trên máy phát triển, không phải OCR do Setup cài trên Windows sạch. Không dùng Skipped để xác nhận model đạt.

## 4. Kiểm file và HTTP VPS

- ZIP gốc local 2.847.980.994 byte được hash lại toàn bộ, khớp `e9939863910af178ec6be6e99f7fcc16948b6a8d916bd5b32bc0a50d92d3f471`.
- HTTPS HEAD trả 200, đúng size/MIME và strong ETag. Hai Range/If-Range request trả 206: đoạn qua mốc 2 GiB và 4 KiB cuối; byte khớp file gốc local. Giữ kiểm chứng TLS, không redirect/fallback.
- Full streaming GET của công cụ chẩn đoán hết ngân sách 300 giây trước khi có kết quả hash cuối. **Chưa đạt phép kiểm full GET trong lượt này.** Giới hạn này thuộc script chẩn đoán ngoài repository, không phải bằng chứng downloader production có timeout tổng 5 phút; không sửa timeout production để lách kết quả.
- Không tải ZIP vào cache Setup hoặc import runtime qua phép kiểm HTTP này. Bằng chứng full GET/hash ở lượt trước chỉ là lịch sử, không thay kết quả mới.

## 5. Trạng thái kế hoạch và phạm vi còn thiếu

| Task | Trạng thái thực hiện | Phần chưa nghiệm thu |
|---|---|---|
| ST01–ST02 | Đã chốt baseline source và mapping 56 ca/32 task | Kết quả cuối có snapshot và hash source/binary; mapping không thay nghiệm thu tích hợp |
| ST03 | Blocked | FFmpeg hiện `Approval scope: Development`; validator Release từ chối. Chưa được cung cấp profile khách/candidate/API release staging đã chốt |
| ST04–ST05 | Một phần | Có máy phát triển, TEMP riêng, fixtures/evidence; chưa có VM/profile Windows sạch hay ma trận Intel/AMD/OS/ACL/quota |
| ST06–ST11 | Một phần | Có sửa/regression UI và native fixture; chưa Narrator/high contrast, DPI vật lý khác 120, đầy đủ overlay/license/workflow thật hoặc người dùng mới nghiệm thu |
| ST12 | Đã bổ sung và chạy transport fixture | TLS/DNS/proxy/NGINX thật và mọi tổ hợp retry mạng vẫn cần ma trận môi trường |
| ST13–ST17 | Một phần | Có file/hash/Range và regression validation/policy; chưa full ZIP qua Setup, disk đầy thật, AV lock, mọi root/platform hoặc candidate compile policy true |
| ST18–ST21 | Một phần | Có transaction/receipt/marker/lease tests và Job thật; chưa power-off VM, parent Setup chết thật, hai Setup sản phẩm và legacy race đầy đủ |
| ST22–ST28 | Blocked/một phần ở component | OCR native fixture đã chạy trong suite; chưa bộ cài khách, Windows sạch, login/license staging/workflow sau cài, Qwen/Piper thật, soak 4 giờ, Qwen 3×30, Piper 30 lượt hoặc ma trận reinstall/reboot |
| ST29–ST30 | Đã xử lý các lỗi tái hiện trong phạm vi source | Không coi mọi rủi ro môi trường còn thiếu là đã được sửa hoặc nghiệm thu |
| ST31–ST32 | Đã chốt hồi quy tự động và biên bản trong phạm vi máy phát triển | Chưa đủ hồ sơ nghiệm thu khách/rollout production; các phần thiếu ở trên vẫn giữ nguyên |

Không phát hành khi thiếu gate Release hoặc các bài bắt buộc của máy khách. Không tự đổi scope FFmpeg, mang cấu hình phát triển/credential dùng chung vào candidate, tắt pin/TLS, hạ ngưỡng disk hoặc tự chấp thuận cảnh báo RAM.

## 6. Hồ sơ bằng chứng

- [Thư mục lượt kiểm](C:/vm-setup-verification/20261017-000053-stability): snapshot source trước/sau, environment, restore/build log, TRX, frontend JSON, ảnh WebView2, report HTTP/hash và coverage.
- [Ảnh WinForms baseline và stress layout](C:/vm-setup-verification/stability-setup-20261017-000113): JSON đo DeviceDpi thực **120 (125%)**; tỷ lệ 1/1,25/1,5/2 là mô phỏng thêm bằng font+geometry, không phải chuyển DPI vật lý. [Ảnh cuối](C:/vm-setup-verification/stability-setup-20261017-000113/final-20261017-002537) có hash ba assembly nguồn/bản copy khớp và bốn PNG/layout JSON; các ảnh `after` đầu giữ riêng làm lịch sử. Ghi chú 96 DPI ban đầu đã được sửa theo JSON đo thực tế.
- [Frontend fail-before/pass-after](D:/vm-tests/setup-stability-react-20261017): các lỗi snapshot/ACK/context/consent/repair và hash source.
- [Bảng kết quả và hash](C:/vm-setup-verification/20261017-000053-stability/build-test-summary.json), [mapping kết quả 56 ca/32 task](C:/vm-setup-verification/20261017-000053-stability/coverage-results.json) và [20 vòng lặp](C:/vm-setup-verification/20261017-000053-stability/repeat-series/20261017-002632-StaticSetup-e94988/summary.json). `coverage-draft.json` giữ bản mapping trước khi root chạy; `coverage-results.json` chỉ gắn bằng chứng tự động vào các phần đã kiểm, không đánh dấu toàn bộ ca tích hợp là Passed.

Bước còn lại để nghiệm thu người dùng cuối: cung cấp profile phát hành/candidate đúng policy và FFmpeg Release đã duyệt, môi trường Windows sạch có tài nguyên phù hợp; rồi chạy Setup tải/cài, crash/recovery, native model và workflow trên chính môi trường đó.
