# Triển khai ổn định kiểm thử

Ngày: 2026-09-26. Nhánh `main`, commit nền `bc1c6aec3c731bdb0edcebbde17ae13640245e52` và working tree chưa commit. Giữ nguyên cấu hình `TOOL-LOCAL/appsettings.json` của người dùng.

**Đã triển khai source/runner và hoàn tất các chuỗi tự động trên máy tham chiếu.** Hai regression đạt 100/100 lượt mỗi bài; C# đạt 10/10 lượt, mỗi lượt 1551 Passed / 0 Failed / 13 Skipped; frontend đạt 10/10 lượt, mỗi lượt 277 Passed / 0 Failed / 0 Skipped. Piper thật thất bại ở DNS. Windows thứ hai, ZIP/máy khách và production chưa được xác minh.

## Nguyên nhân và thay đổi

- Tái hiện hai lần lỗi khi chạy cặp test bridge cài bộ dịch và Cloud Vietsub: Cloud đạt, bridge nhận `system_setup_busy`, installer không được gọi. Hai fixture độc lập cùng dùng `RuntimeUseGate.Shared` của tài khoản Windows.
- Cho phép truyền gate vào job manager và bộ xuất video. Các fixture dùng gate nằm trong workspace riêng. Desktop tiếp tục lấy `Shared` tại thời điểm sử dụng, vì `Program` cấu hình gate sau bước tạo các service. Dịch và giọng đọc dùng cùng gate với job manager.
- Thêm regression giữ khóa cùng workspace, không tranh khóa khác workspace, trả khóa sau khi dừng job, và tranh khóa thật giữa hai tiến trình. Có cả tình huống tiến trình giữ khóa kết thúc đột ngột.
- Chờ trạng thái job bằng event, đăng ký trước khi đọc snapshot và kiểm lại trạng thái DB cuối. Giảm việc đọc/kiểm schema SQLite mỗi 20 ms ở các bài Cloud, bridge, translation, OCR và job. Giữ thời hạn chờ và các assertion dữ liệu hiện có.
- Dọn pool theo DB của fixture, thay cho `ClearAllPools()`. Không thay cấu hình pooling, PRAGMA, schema hoặc transaction production. Chưa coi SQLite là nguyên nhân duy nhất của timeout lịch sử.
- xUnit chạy tối đa 4 collection logic cùng lúc. Các lớp dùng OCR/FFmpeg/WebView2 thật chạy trong collection `Native Windows`, không chạy chồng với collection khác. Các test bắt buộc vẫn nằm trong lệnh toàn suite.
- Form đăng nhập nhận đường dẫn profile WebView2 tùy chọn để test dùng profile riêng. Fixture chờ luồng UI và tiến trình browser do mình tạo kết thúc trước khi dọn.
- Frontend giới hạn 4 worker, giữ cô lập từng file. Thêm kiểm tra polling khi bận, dọn timer/listener khi unmount và phản hồi lỗi từ tổ chức cũ; giữ các bài busy/cancel, request cũ và repair thất bại.
- Thêm HTTP giả kiểm DNS, timeout, sai SHA-256 đúng kích thước và hủy trong lúc ghi file tải. Các ca phải không có READY/marker/file được promote, dọn file tạm và cho phép yêu cầu cài tiếp theo đi tới HTTP.

## Cách chạy

Hoàn tất restore/build và cài dependency frontend theo [hướng dẫn kiểm thử](KIEM_THU_VA_NGHIEM_THU.md) trước. Không sửa source hoặc build trong lúc chạy một chuỗi.

```powershell
# 100 lần mỗi regression: 90 lần theo cặp + 10 lần riêng từng ca.
.\scripts\Test-Stability.ps1 -Profile RegressionPair -Iterations 90 -OutputRoot D:\vms
.\scripts\Test-Stability.ps1 -Profile RegressionBridge -Iterations 10 -OutputRoot D:\vms
.\scripts\Test-Stability.ps1 -Profile RegressionCloud -Iterations 10 -OutputRoot D:\vms

.\scripts\Test-Stability.ps1 -Profile Full -Iterations 10 -OutputRoot D:\vms
.\scripts\Test-Stability.ps1 -Profile Frontend -Iterations 10 -OutputRoot D:\vms

# Full đã chứa các bài native; dùng lệnh này khi cần chạy nhóm native riêng.
.\scripts\Test-Stability.ps1 -Profile Native -Iterations 5 -OutputRoot D:\vms

# Khi cần kiểm riêng hồi quy layout WebView2:
.\scripts\Test-Stability.ps1 -Profile Timeline -Iterations 5 -OutputRoot D:\vms
.\scripts\Test-StabilityReport.ps1

# Chạy riêng sau khi các bước build/native kết thúc; cần mạng và tải runtime/model thật.
.\scripts\Test-Stability.ps1 -Profile Piper -Iterations 3 -InstallPiper -OutputRoot D:\vms
```

[Runner](scripts/Test-Stability.ps1) tương thích PowerShell 5.1. Mỗi lần gọi tạo một thư mục mới, không ghi đè báo cáo cũ. Có các chế độ `-KeepTemp` và `-TimeoutSeconds`; không dùng tăng timeout để che lỗi test.

- Mỗi lượt khởi động tiến trình mới, dùng TEMP/TMP riêng có dấu và khoảng trắng. Biến opt-in chỉ được đặt trong tiến trình con; các bài model/SQL khác giữ opt-in tắt.
- Lưu source/binary manifest SHA-256, commit, cấu hình worker, SDK/Node/npm, Windows/WebView2, RAM, dung lượng và filter. Frontend đổi seed thứ tự mỗi lượt và ghi seed.
- Lưu TRX hoặc JSON frontend, stdout/stderr và `result.json` từng lượt. Skip có tên/lý do trong `skips.json`; không suy tổng skip từ bộ đếm TRX không phản ánh `NotExecuted`.
- Đối chiếu danh sách phát hiện với bài thực thi, so tên/kết quả giữa các lượt và kiểm binary không đổi. Thiếu kết quả, test thất bại, skip ngoài dự kiến, timeout hoặc tiến trình con còn sót đều làm bước thất bại.
- Dùng Windows Job Object chỉ quản lý cây tiến trình do runner tạo. Dọn cây đó khi lỗi; không tìm/dừng ứng dụng hoặc IDE của người dùng. Giữ artifact lượt lỗi và dừng ngay, không tự thử lại để thay kết quả.
- Chỉ xóa TEMP thành công sau khi xác minh nằm trong thư mục chuỗi và không có reparse point. Báo cáo/log được giữ. Có số tiến trình cao nhất được lấy mẫu, peak working set của tiến trình cha và peak committed memory của cây tiến trình; các số này không thay cho đo cấu hình máy khách.

## Bằng chứng điều tra và nghiệm thu

Máy tham chiếu: Windows 11 x64 `26200`, i7-11800H, 16 luồng, RAM vật lý 16,93 GB, SDK `10.0.301`, Node `24.16.0`, npm `11.13.0`, WebView2 `153.0.4234.48`.

Điều tra ban đầu lưu ở `D:\vmtest\stability-20260926`; các chuỗi chạy lặp lưu dưới `D:\vms`.

| Kiểm tra | Passed | Failed | Skipped | Ghi chú |
|---|---:|---:|---:|---|
| Tái hiện cặp lỗi trước cô lập, mỗi lượt | 1 | 1 | 0 | `baseline-pair.trx`, `baseline-pair-diagnostic.trx`; mã bridge `system_setup_busy`. |
| Bridge/Cloud/job sau cô lập | 33 | 0 | 0 | `pair-after-isolation.trx`. |
| Gate giữa tiến trình, job, translation | 31 | 0 | 0 | `gate-and-jobs.trx`. |
| Login lifecycle, HTTP giả và OCR, lượt điều tra | 6 | 1 | 0 | `lifecycle-and-download.trx`; chờ event browser sau khi vòng lặp STA đã đóng bị timeout. |
| Login và HTTP giả sau sửa cách chờ browser | 5 | 0 | 0 | `lifecycle-and-download-fixed.trx`; chờ tiến trình OS thay cho event STA. |
| Frontend sau bổ sung kiểm lifecycle | 274 | 0 | 0 | `frontend-first.json`; chưa phải chuỗi 10 lượt. |
| C# toàn bộ với TEMP Unicode, lượt điều tra | 1550 | 1 | 13 | `D:\vms\20260926-160835-Full-e88708`; fixture pipeline OCR lỗi marshal tại `Cv2.ImWrite`. Hai regression bridge/Cloud đạt; không còn tiến trình con sau lượt. |
| Pipeline OCR sau sửa cách ghi ảnh Unicode | 1 | 0 | 0 | `ocr-write-unicode-fixed.trx`; FFmpeg/Paddle thật. |
| Cài mới Piper thật, bắt đầu chuỗi 3 lượt | 0 | 1 | 0 | `D:\vms\20260926-161440-Piper-93085b`; dừng ở lượt 1/3, không thực hiện lượt 2/3. .NET HTTP không phân giải được `github.com:443`; chưa tải/cài/probe thành công. |
| C# sau sửa fixture, runner trước sửa cách so sánh | 1551 | 0 | 13 | Hai lượt tại `D:\vms\20260926-162550-Full-25158e` cùng đạt ở TRX; runner dừng tại lượt 2 vì dấu vân tay thay đổi. Không đánh dấu chuỗi này hoàn tất. |
| Regression cho cách tổng hợp báo cáo | 5 | 0 | 0 | `report-helper-tests.json`; đổi thứ tự/hoa-thường, thiếu hoặc thừa dòng, tên bị rút gọn và trạng thái skip. |
| C# với runner ordinal, trước sửa chờ layout | 1550 | 1 | 13 | `D:\vms\20260926-163714-Full-7e36b4` dừng ở lượt 4/10; ba lượt đầu mỗi lượt 1551/0/13. Bài timeline WebView2 báo nhãn chưa căn giữa ở lần đo đầu. |
| Timeline chạy riêng để điều tra | 1 | 0 | 0 | 30/30 lượt tại `D:\vms\20260926-165609-Timeline-603125`; không tái hiện lỗi ở phạm vi chạy riêng. Không thay kết quả lỗi toàn suite. |
| Điều kiện chờ layout/zoom | 3 | 0 | 0 | `timeline-readiness-tests.log`; chờ cập nhật chậm hơn hai frame, chờ đúng zoom và timeout có số đo. |

Lỗi ghi ảnh fixture được sửa bằng `Cv2.ImEncode` rồi ghi bytes bằng .NET. Bài pipeline tiếp tục tạo video, trích frame và nhận dạng bằng Paddle thật. Đường dẫn có dấu được giữ ngay trong fixture để lệnh test thường cũng kiểm nhánh này.

Restore/build Release và `npm ci`/build frontend đã chạy. MSBuild không có warning/error; Vite vẫn cảnh báo bundle lớn hơn 500 kB.

### Chuỗi trước sửa điều kiện chờ layout

| Phạm vi | Số lượt hoàn tất | Kết quả mỗi lượt | Artifact |
|---|---:|---|---|
| Frontend, seed 260927–260936 | 10/10 | 274 Passed / 0 Failed / 0 Skipped | `D:\vms\20260926-161501-Frontend-482ba3` |
| Bridge + Cloud chạy cùng nhau | 90/90 | 2 Passed / 0 Failed / 0 Skipped | `D:\vms\20260926-161741-RegressionPair-9e08c0` |
| Bridge chạy riêng | 10/10 | 1 Passed / 0 Failed / 0 Skipped | `D:\vms\20260926-162429-RegressionBridge-62abb3` |
| Cloud 105 cue chạy riêng | 10/10 | 1 Passed / 0 Failed / 0 Skipped | `D:\vms\20260926-162500-RegressionCloud-e035bb` |

Mỗi regression đã hoàn tất **100/100 lượt trên tiến trình mới**: 90 theo cặp và 10 riêng. Không còn tiến trình con sau cleanup; tất cả lượt kiểm source/binary không đổi đều đạt.

Runner từng dùng `Sort-Object Name`, không phân biệt hoa/thường. Hai lượt C# chứa các ca dữ liệu khác nhau như `DurationSeconds`/`durationSeconds` nên thứ tự giữa các dòng có thể đổi, dù mọi test ID và kết quả đều khớp. Đã sửa thành so sánh ordinal, đưa test ID ổn định vào dấu vân tay và giữ cả dòng trùng tên hiển thị. Cách so mới đã đối chiếu lại **120 báo cáo** của chuỗi regression/frontend, đều khớp (`report-identity-audit.json`). Các báo cáo gốc và lỗi runner được giữ nguyên.

Thay đổi cách so sánh nằm ở script báo cáo; tại bước đó source ứng dụng, test C#/frontend và binary không thay đổi. Chuỗi C# bắt đầu lại tại `D:\vms\20260926-163714-Full-7e36b4` đã dừng vì lỗi timeline ở lượt 4, được ghi trong bảng điều tra. Runner cũng mở rộng source manifest để gồm Python, SQL và các cấu hình build; vì vậy source manifest khác bản runner trước.

### Chờ layout timeline trước phép đo native

Probe cũ chỉ chờ hai `requestAnimationFrame`. Component khởi tạo thang thời gian 40 px/giây trước khi effect/ResizeObserver đưa chiều rộng viewport thực vào state. Số đo thêm ở `useLayoutEffect` của fixture ghi **clip rộng 60 px, chữ 74,40625 px, lệch tâm 12,203125 px** ở trạng thái ban đầu. Sau khi kích thước viewport được áp dụng, clip rộng **166 px** và lệch tâm **0 px** trên cùng lần khởi động. Đây là trạng thái có thể làm phép đo trước khi hoàn tất layout thất bại; lỗi ở lượt 4 ban đầu chưa có các số đo ngang chi tiết.

Probe mới đợi chiều rộng content khớp viewport/thang thời gian và `devicePixelRatio` khớp zoom host qua ba mẫu liên tiếp, tối đa 3 giây. Nó không dùng kết quả căn giữa/clipping để quyết định đã sẵn sàng. Các ngưỡng căn giữa dưới 1 px, căn dọc dưới 2 px, glyph nằm trong nhãn, waveform và toolbar vẫn được kiểm sau đó. Các phép đo chuẩn bị và hình học được lưu trong stdout của TRX; CSS ứng dụng không thay đổi.

Định danh artifact trước sửa điều kiện chờ layout:

- Binary manifest SHA-256 chung cho regression, frontend và C#: `8954766D556EF08FC363719994F0E6581F6C3DFEA84D4C57010F27AEC46EB196`.
- Source manifest của runner mới: `D159B8C44F6EC77F944B47672C8F917A7BE24162476FD3B7786025E5C192C24C`.
- Source manifest trước sửa bộ tổng hợp/mở rộng danh sách file: `7957158CFEB6B5FEC0A568962898B1920EE7C6E8FCE1CE9FD74DD96B8D47CE0A`.

Trong 100 lượt regression, thời gian P95 của bridge là **0,55 giây**, Cloud giả lập 105 cue là **2,60 giây**; chậm nhất lần lượt **0,61 / 2,87 giây** (`regression-final-metrics.json`). Đây là thời gian bài kiểm trên máy tham chiếu, không phải độ trễ provider thật.

13 skip mặc định gồm: 7 bài Qwen/CUDA/benchmark, 4 bài Piper/Kokoro, 1 bài model chuyển giọng và 1 bài SQL LocalDB TikTok. Danh sách tên đầy đủ và điều kiện từng bài nằm trong `run-*/skips.json`. Cài Piper thật đã được chạy riêng và ghi Failed ở bảng điều tra; không dùng skip mặc định để thay kết quả đó.

Các lần native trong suite C# toàn bộ dùng tiến trình mới và TEMP Unicode mỗi lượt. Có thể dùng TRX của các lượt này để nghiệm thu lặp native; chế độ `Native` trong runner dành cho kiểm nhóm riêng khi cần điều tra.

### Chuỗi sau sửa điều kiện chờ layout

| Phạm vi | Lượt hoàn tất | Kết quả mỗi lượt | Artifact |
|---|---:|---|---|
| Timeline WebView2, bốn mức zoom | 5/5 | 1 Passed / 0 Failed / 0 Skipped | `D:\vms\20260926-170214-Timeline-7ce17b` |
| Frontend, gồm ba regression chờ layout | 10/10 | 277 Passed / 0 Failed / 0 Skipped | `D:\vms\20260926-170351-Frontend-4cb17e` |
| Bridge + Cloud theo cặp | 90/90 | 2 Passed / 0 Failed / 0 Skipped | `D:\vms\20260926-170610-RegressionPair-f4ae29` |
| Bridge riêng | 10/10 | 1 Passed / 0 Failed / 0 Skipped | `D:\vms\20260926-171308-RegressionBridge-a0222a` |
| Cloud riêng | 10/10 | 1 Passed / 0 Failed / 0 Skipped | `D:\vms\20260926-171340-RegressionCloud-266a0e` |
| C# toàn bộ | 10/10 | 1551 Passed / 0 Failed / 13 Skipped | `D:\vms\20260926-171430-Full-4b6170` |

Đã chạy lại đủ 100 lượt mỗi regression và 10 lượt C# trên binary test sau sửa timeline. Cả năm chuỗi regression/frontend/C# có `Complete=true`, cùng source/binary manifest, danh sách và kết quả test ổn định. Không còn tiến trình con hoặc thư mục TEMP của các lượt đạt. Đối chiếu cuối lưu tại `D:\vmtest\stability-20260926\latest-acceptance-audit.json`.

Mỗi lượt C# mất **228,48–238,79 giây**; tổng thời gian tiến trình của 10 lượt là **2332,26 giây**. Peak committed memory lớn nhất của cây tiến trình C# khoảng **1,51 GB**. Đây là số đo trên máy tham chiếu, chưa xác định cấu hình tối thiểu của máy khách.

Trên chuỗi hiện hành, thời gian P95 của bridge là **0,61 giây**, Cloud giả lập 105 cue là **2,65 giây**; chậm nhất lần lượt **0,68 / 2,87 giây**. Số đo từng bài nằm trong `D:\vmtest\stability-20260926\regression-after-timeline-metrics.json`.

Đã đối chiếu TRX của cả **10 lượt Full**: mỗi lượt có **14 ca trực tiếp dùng OCR/FFmpeg/WebView2 thật Passed / 0 Failed / 0 Skipped**, trên tiến trình mới với TEMP Unicode. Các ca gồm pipeline Paddle, adapter OCR/media, đăng nhập, composer, subtitle editor, timeline, playback và media WebView2. Cả 10 lượt không còn tiến trình con sau cleanup. Bằng chứng này đáp ứng tiêu chí năm lượt native trên máy tham chiếu; không gộp các test logic khác trong collection thành số test native thật. Bảng đối chiếu và định danh test nằm trong `D:\vmtest\stability-20260926\native-after-timeline-repeats.json`.

Source manifest: `7463BCB06998368A12180831568EA5636C7381DEB10D3C976BC8D79CC7AC0C91`.
Binary manifest: `FD2DE256B99EBACBCEC2C2617FBDF488FE02D1C2C8D3389C12D38865B285C8D2`.

## Phần cần môi trường đích

Chưa có xác nhận đúng ZIP đã gửi, Windows thứ hai hoặc quyền truy cập máy đã báo lỗi. Cài mới Piper đã thử thật và thất bại ở phân giải tên miền trong .NET HTTP. Chưa xác minh các host redirect hoặc tải Python/package/model tiếp theo. Không sửa DNS/hosts/proxy của máy để thay kết quả. Nghiệm thu Piper cần một mạng cho phép installer hoàn tất rồi chạy lại đủ 3 lượt.

ZIP trên Windows sạch, repair qua server đích và đăng nhập/license/SQL workflow phải có bằng chứng riêng. Kết quả logic/native trên máy phát triển không nâng các phần này thành đã nghiệm thu hoặc đã rollout.

Sau khi có ZIP đúng version/build/hash, dùng [Test-DesktopSetupPublish.ps1](scripts/Test-DesktopSetupPublish.ps1) với `-ProbeWebView2 -ProbeBundledComponents`. CLI và phạm vi READY được giải thích trong [báo cáo Setup ZIP](TRIEN_KHAI_SUA_LOI_SETUP_BAN_ZIP.md).

## Trạng thái task

| Task | Trạng thái và giới hạn |
|---|---|
| ST01 | Đã ghi baseline, phiên bản công cụ, cấu hình máy, source/binary manifest và danh sách test phát hiện. Source chưa commit được định danh bằng SHA-256 manifest. |
| ST02 | Runner tạo TEMP/workspace riêng, kiểm dung lượng và phạm vi cleanup; fixture Vietsub dùng DB/gate riêng; profile login riêng. |
| ST03 | Đã tái hiện lỗi bridge theo cặp hai lần, xác định `system_setup_busy`. Timeout Cloud trong toàn suite lịch sử chưa được tái hiện như một lỗi SQLite riêng; đã loại bỏ polling dày và tách tải native để kiểm chứng. |
| ST04 | Đã triển khai gate injection và regression tranh khóa cùng workspace/khác workspace/khác tiến trình/tiến trình chết. Production giữ gate chung. |
| ST05 | Đã thay cleanup toàn bộ pool bằng cleanup đúng DB; giữ schema/transaction/config production. Chưa đo riêng số lần schema validation hoặc quy toàn bộ timeout lịch sử cho pool SQLite. |
| ST06 | Các bài job chính chờ event và kiểm DB cuối; giữ assertion pause/resume/retry/cancel/revision/SRT/ACK. Login chờ browser và UI thread kết thúc. Không tăng timeout test chung. |
| ST07 | Đã có lịch native riêng, sửa fixture Unicode và chờ layout đã đo được. 14 ca native trực tiếp đạt trong cả 10 lượt C#; chưa ngoại suy cho Windows/DPI/máy khác. |
| ST08 | Đã bổ sung regression lifecycle; frontend đạt 10/10 lượt với 10 seed, không skip. |
| ST09 | HTTP giả đạt. Cài Piper thật thất bại ở DNS .NET, dừng lượt 1/3; các bước tiếp theo và đủ 3 lần cài chưa nghiệm thu. |
| ST10 | Đã có runner, cấu hình, manifest, phát hiện bài thiếu/skip bất thường, log từng lỗi và cleanup tiến trình sở hữu. |
| ST11 | Đạt trên máy tham chiếu: 100/100 lượt mỗi regression, 10/10 C#, 10/10 frontend và đủ lượt native. Windows thứ hai chưa có môi trường để chạy 3 lượt bổ sung, nên phần này của task còn mở. |
| ST12 | Chưa có ZIP/máy khách được xác nhận; chưa nghiệm thu Windows sạch, server repair hoặc workflow đích. |
| ST13 | Đã bàn giao lệnh, artifact, manifest và báo cáo phân biệt kết quả thực/skip/lỗi môi trường. Điều kiện phát hành còn phụ thuộc ST09, phần Windows thứ hai của ST11 và ST12. |

Các phần chưa xác minh được giữ mở, kể cả khi toàn bộ chuỗi tự động trên máy tham chiếu đạt.
