# Triển khai khắc phục và nghiệm thu Setup/model

Ngày: **2026-10-17**. Nhánh `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b` cùng working tree chưa commit. Giữ các thay đổi có sẵn ngoài phạm vi. Thực hiện theo [kế hoạch FX01–FX36](PLAN_TASK_KHAC_PHUC_VA_NGHIEM_THU_SETUP_MODEL.md).

**Đã triển khai các phần source/công cụ và kiểm thử làm được trên máy phát triển. Chưa nghiệm thu Setup khách trên Windows sạch, chưa chạy Qwen/Piper thật hoặc soak trong lượt này, chưa phát hành.** Các mục chưa có môi trường được ghi riêng; không coi toàn bộ 36 task đã hoàn tất.

## 1. Thay đổi đã triển khai

| Phần | Thay đổi | Phạm vi |
|---|---|---|
| Dependency | Server tham chiếu Pkcs 10.0.13; giữ EF SQL Server 10.0.11/SqlClient 6.1.6, không suppress audit | Hai advisory được đối chiếu nguồn chính thức; graph và runtime cần cùng đạt |
| Installed qualification | CLI/script mới dùng runtime Qwen/Piper của StaticArchive đã cài; kiểm compiled policy, trust, app/model receipt, commit, fingerprint và usage/runtime lease | Không install/repair/download/refresh READY; output chỉ ở thư mục QA mới, tách root production |
| Qwen | Dùng worker CPU thật, prompt/grammar/parser/probe validators hiện có; báo metrics và warmed private-memory growth | Giữ harness bundled; không copy GGUF dev vào publish để giả bản cài |
| Piper | Dùng Python/worker/model/config của runtime đã cài; 30 câu tổng hợp khác nhau, kiểm protocol, WAV và hash | Giữ audio fixture để nghe riêng; định dạng đạt không tự chứng nhận chất lượng nghe |
| Kiểm kê trước cài | Nhận đúng mode StaticArchive, nguồn pin HTTPS:8443, cache Setup và fallback, global Piper và legacy revocation store | Source inventory chỉ đọc; không attest compile policy/runtime hoặc credit file chưa hash |
| Tài nguyên QA | Full/Timeline cần đủ gate timeline hai giờ cộng dự phòng trước chạy; StaticSetup giữ floor 3 GiB | Báo Blocked trước test khi thiếu, không hạ gate renderer hoặc đổi thành Skipped |
| Browser tích hợp | Thêm full App/useSystemSetup/transport trong WebView2; bridge/coordinator và quyết định gate C# thực | Session/component/business handler là fixture, không phải auth/Form1 composition hoàn chỉnh hoặc Setup khách |
| Parent chết | Test helper gọi Job Object production rồi driver kill đúng parent; giữ cây process fixture độc lập | Kiểm Windows job khi parent chết thật; chưa kill Setup sản phẩm hoặc power-off VM |
| Wrapper QA | Native sở hữu tạo output sau guards; PowerShell chỉ ghi series sau khi report file native khớp stdout/binding/pin; helper Job khai báo trong bộ nhớ | Không tạo thư mục/series khi guard từ chối; không biên dịch vào TEMP; dừng đúng process vừa tạo nếu attach Job lỗi |

Source chính: [installed command](TOOL-LOCAL/SystemSetup/InstalledStaticModelCommand.cs), [qualification](TOOL-LOCAL/ModelDistribution/InstalledStaticModelQualification.cs), [session](TOOL-LOCAL/ModelDistribution/InstalledStaticModelSession.cs), [script](scripts/Test-InstalledStaticModel.ps1), [preflight](scripts/ModelSetupPreflight.psm1), [disk assessment](scripts/StabilityResourcePreflight.psm1).

Pkcs dựa trên [advisory Microsoft thứ nhất](https://github.com/dotnet/runtime/security/advisories/GHSA-2fmv-rv2c-59x7) và [thứ hai](https://github.com/dotnet/runtime/security/advisories/GHSA-846f-f764-76mx). Báo cáo research lưu cả lựa chọn không dùng bản vá nhánh 9, dependency parent chưa đủ floor, assembly resolution và tác động package pruning .NET 10. Không cập nhật shared framework/hosting toàn máy hoặc deploy server trong lượt này.

## 2. Kiểm file VPS thực tế

Đã GET toàn bộ `https://103.179.190.182:8443/models/VideoMaker-Models-20261016-082700.zip` bằng streaming, không lưu thêm ZIP xuống ổ đĩa:

- HTTP 200, `application/zip`, nhận đúng **2.847.980.994 byte**.
- SHA-256 toàn stream: `e9939863910af178ec6be6e99f7fcc16948b6a8d916bd5b32bc0a50d92d3f471`, khớp source pin.
- **343,55 giây**, TLS giữ kiểm chứng, không redirect/bypass. Script chẩn đoán có ngân sách 1.200 giây và inactivity body 60 giây; không đổi timeout downloader production.
- Đây là bằng chứng HTTP/file, chưa thay tải bằng Setup, import ZIP, cài runtime hoặc native inference. Không đổi Nginx/file VPS.

## 3. Kiểm thử và các lỗi bắt được trong triển khai

Giữ toàn bộ log/TRX của các lượt fail và retest. Kết quả trên working tree của lượt này:

| Kiểm tra | Passed | Failed | Skipped | Ghi chú |
|---|---:|---:|---:|---|
| Restore/Release solution build cuối | Đạt | 0 | — | 0 warning .NET; helper đúng Release |
| Full .NET cuối | 2.230 | 0 | 18 | 2.248 bài, 4 phút 41 giây; `full-suite-unicode-final.trx` |
| Installed qualification regression cuối | 14 | 0 | 0 | TEMP có dấu, từ chối trước output; đã nằm trong full suite |
| StaticSetup lặp 20/20 vòng | 3.480 lượt | 0 | 0 | 174 bài/vòng; identity không đổi, không timeout/process thuộc lượt còn lại |
| Frontend ci/build/test | 341 | 0 | 0 | 48 file test; Vite còn cảnh báo chunk hiện hữu |
| NuGet audit server | Đạt | 0 | — | Không package vulnerable trong graph server được audit; không chứng nhận mọi runtime/phụ thuộc trên máy |

Full suite dùng TEMP ngắn trên C sau preflight **7.288.426.496 byte khả dụng / 5.290.061.824 byte yêu cầu**. Riêng regression wrapper luôn chạy PowerShell 5 với TEMP/TMP có dấu. Trong lúc full suite chạy, hai dòng comment helper được sửa để mô tả đúng vòng đời worker, không đổi lệnh thực thi; `full-comment-only-change.json` tái dựng hash cũ khớp hoàn toàn. Source mới sau comment được chụp lại trước vòng lặp. Các nhóm test không cộng trùng.

Chuỗi lặp cuối: `C:\vmr-fix\20261017-013353-StaticSetup-c5afb7`. Harness kiểm danh sách test với discovery, hash source/binary và process ownership; TEMP từng vòng có dấu. Browser bị loại khỏi profile lặp, đã chạy riêng trong full suite. Giữ nguyên chuỗi đầu thất bại ở `C:\vmr-fix\20261017-012316-StaticSetup-888970`; không ghi đè lịch sử lỗi.

18 Skipped được lưu riêng theo từng outcome `NotExecuted`: sáu SQL và 12 model/GPU/voice. Không lấy counter skipped bằng 0 của adapter, không tính các bài này là model đạt. OCR native Anh/Trung và nhận chữ từ khung hình video đã chạy đạt trong full suite; chưa chạy Qwen/Piper inference trong lượt này.

- Restore và build đầu đạt 0 error/0 warning .NET; frontend vẫn có cảnh báo chunk Vite hiện hữu.
- Build solution đầu đưa helper ngoài solution vào Debug nên target copy Release không có file. Đã thêm project test helper vào solution; build sau dùng đúng helper Release. Đây là công cụ kiểm thử, không thay Setup production.
- Focused đầu: **196 Passed/2 Failed/0 Skipped/198 Total**, 50 giây. Một ca corpus Piper chưa đủ câu dài >150 ký tự; đã kéo dài câu, giữ assertion. Một ca chứng minh testhost vẫn nạp Pkcs 10.0.9 dù graph server đã chọn 10.0.13.
- Điều tra Pkcs phân biệt server và testhost: server output đã có DLL/deps mới; dependency gián tiếp bị .NET 10 pruning khỏi test graph WindowsDesktop. Đã giữ riêng Pkcs khỏi prune trong test project, không tắt pruning toàn cục; DLL server/test có cùng hash và file version 10.0.1326.50504. Không bỏ assertion runtime hoặc cập nhật shared framework để làm đạt.
- Focused sau sửa đạt **198/0/0**. Full suite trước sửa wrapper đạt **2.229/0/18**, tổng 2.247 bài, 5 phút 9 giây; đây chưa phải lượt cuối sau bổ sung regression wrapper.
- Review phát hiện wrapper tạo thư mục/series trước native guard. Đã bỏ tạo sớm, chuyển ownership về native và thêm test bằng desktop build hoàn chỉnh: phải trả đúng `installed_model_release_required`, không output. Nhóm qualification sau sửa **14/0/0**. Probe dùng apphost/runtimeconfig cũ trong test output chỉ giữ làm bằng chứng phụ về wrapper ghi sớm; không dùng làm bằng chứng hành vi desktop hiện hành. Regression chính dùng bộ build đầy đủ, không dùng apphost cũ.
- Vòng lặp đầu sau đó dừng đúng tại vòng 1/20: **173 Passed/1 Failed/0 Skipped/174 Total**, không process thuộc lượt thử còn lại. TEMP có dấu làm CodeDom của PowerShell 5 không ghi được DLL helper. Giữ lỗi và assertion; sửa bằng Reflection.Emit trong bộ nhớ, không đổi TEMP sang ASCII hoặc cài thêm runtime. Regression mới luôn ép TEMP/TMP có dấu cả khi full suite dùng root ngắn. Nhóm qualification sau sửa này đạt **14/0/0**.
- Review bắt thêm nhánh attach Job thất bại có thể để child tiếp tục chạy. Helper hiện dừng đúng `Process` vừa tạo và chờ thoát; fixture lỗi attach thật trên PowerShell 5/TEMP có dấu xác nhận child đã thoát, process độc lập vẫn sống. Các phép thử Job Assign/Dispose và hai Job độc lập cũng đạt; đây là process fixture, không inference model.
- Browser full App, parent-death và các regression preflight/receipt đã đạt; chưa suy thành nghiệm thu khách. CLI thực của build thường cũng từ chối đúng release policy trước output/inference.

Ảnh full App chụp ngay khi DOM hiện có thể nằm trong hiệu ứng mở modal 180/220 ms; chưa dùng làm bằng chứng hình thức ở trạng thái ổn định. Ảnh viewport 390 px sau `scrollIntoView` nằm trong bảng rộng tối thiểu 650 px có cuộn ngang; chưa có bằng chứng nội dung không truy cập được. Các ảnh stress/fixture không thay DPI vật lý, Narrator, High Contrast hoặc nghiệm thu UI trên bản Setup khách.

## 4. Trạng thái kế hoạch

| Task | Trạng thái/phạm vi | Còn thiếu |
|---|---|---|
| FX01–FX02 | Có baseline source, delta, log, sổ lỗi và phân loại skip | Identity source hiện hành không thay identity candidate khách chưa có |
| FX03 | Một phần | Chưa có profile/version/build/channel/API staging và Windows QA được chỉ định |
| FX04 | Source/công cụ và regression đã bổ sung | Chưa xác minh ngân sách đầy đủ trên candidate/máy khách; không coi floor harness là tổng dung lượng cài |
| FX05 | Blocked | Validator thực trả lỗi vì FFmpeg còn scope Development; chưa có hồ sơ Release |
| FX06 | Source/graph/audit và guard runtime đạt trong full suite | Không nâng trạng thái môi trường hosting/production |
| FX07–FX09 | Blocked | Profile hợp lệ gồm workflow SQL chuyển tiếp, policy ký, FFmpeg Release, candidate và release staging |
| FX10 A | Source/harness đã triển khai | Mốc B installed runtime phải chạy sau Setup thật; chưa có bản cài đạt commit để nghiệm thu |
| FX11 | Một phần | Có capture trace/hash/ảnh/PID và fault fixture; chưa đủ fault trên Setup/VM thật |
| FX12 | Blocked | Chưa Windows/user sạch, snapshot và phương án reset; root TEMP QA không thay điều kiện này |
| FX13 | Một phần | Có corpus tổng hợp; chưa đủ workflow bảo toàn dữ liệu và nghe/đọc nghiệm thu |
| FX14 | Full GET/hash đạt | Kiểm file không thay cài Setup |
| FX15–FX18 | Fixture/source một phần, Setup thật Blocked | Chưa full tải bằng Setup, volume/quota/ACL/AV/prerequisite/network matrix thật |
| FX19 | Chưa nghiệm thu native | Chưa tám checkpoint × ba lượt/power-off VM bằng candidate |
| FX20 | Parent-death helper đạt trong full suite | Chưa kill Setup sản phẩm trong các phase native |
| FX21–FX23 | Fixture hiện có; nghiệm thu tích hợp còn thiếu | Hai Setup/app/legacy/multiuser, repair/reboot/warm cache thật |
| FX24–FX25 | Full App/native controlled bridge regression đã bổ sung | Không thay login/license staging/Form1 composition thật |
| FX26–FX28 | Một phần | Chưa DPI Windows khác 120, Narrator/High Contrast hoặc đo phản hồi trên Setup khách |
| FX29–FX32 | Blocked ở runtime/candidate | Qwen 3×30, Piper 30 lượt và nghe, OCR/workflow sau cài, soak 4 giờ, mode matrix |
| FX33 | Sáu lỗi source/harness tái hiện đã sửa, có proof và hồi quy cuối | Không suy các ca chưa chạy thành không có lỗi |
| FX34–FX35 | Hoàn tất kiểm tra/hồ sơ source; nghiệm thu kế hoạch còn một phần | Chưa candidate đạt và AC01–AC16 trên bản cài khách; không quyết định phát hành từ fixture |
| FX36 | Chưa triển khai | Cần candidate đạt, môi trường/người nhận pilot được chỉ định |

## 5. Bằng chứng và thao tác tiếp theo

[Hồ sơ lượt triển khai](C:/vm-setup-verification/20261017-005137-remediation) chứa baseline/hash source, environment, dependency research/graph/audit, restore/build, TRX, browser evidence và `vps-full-get.json`. Chưa dùng kết quả lịch sử ở biên bản trước làm Passed mới.

Sổ trạng thái từng FX và phân biệt lỗi tái hiện/khoảng trống kiểm thử/đầu vào bên ngoài nằm trong `task-status.json` và `issue-ledger.json` của hồ sơ. AC01–AC16 là ca nghiệm thu tích hợp, chưa đạt tổng thể từ các fixture của lượt này.

`build-test-summary.json` chốt số đếm theo TRX/JSON, hash report và phạm vi. Baseline gồm 1.236 file; manifest bàn giao source gồm 1.250 file, loại output/cache khỏi nguồn sự thật. Ba lockfile có sẵn nằm ngoài nhóm extension của baseline được ghi riêng, không gọi là file mới do lượt này. Manifest của chuỗi ổn định vẫn bao gồm lockfile. Kiểm bổ sung tám file EXE/DLL/runtimeconfig/deps của desktop build đầy đủ và helper trước/sau vòng lặp, ngoài manifest binary tiêu chuẩn của harness. Sau snapshot QA cuối chỉ cập nhật tài liệu; không chạy lại build/test cho thay đổi tài liệu.

Kiểm kê chỉ đọc trên bộ build hiện hành báo mode `Server`, bốn reuse candidate và các blocker về disk/catalog tắt. Báo cáo `InventoryOnly` không xác nhận đây là candidate StaticArchive hay máy sạch; không đổi settings hoặc xóa các candidate để làm bài cài mới.

Máy thử là Windows 11, RAM tổng khoảng 16 GiB. Snapshot đầu có 6,25 GiB free RAM/6,57 GiB free virtual memory, C khoảng 5,63 GB/D khoảng 2,12 GB trống; các giá trị là thời điểm đo, không phải cam kết tài nguyên thường trực. Không xóa/di chuyển model hoặc dữ liệu người dùng, không truy cập DB thật, gọi provider có phí, publish release hoặc đổi VPS. SQLite/process/HTTP fixture chỉ hoạt động trong môi trường thử riêng.

Sau khi có profile khách/API staging, FFmpeg Release và Windows QA phù hợp: build candidate theo pipeline, cài qua Setup, rồi dùng `Test-InstalledStaticModel.ps1` với `-Engine qwen -ColdStarts 3 -Iterations 30` và Piper 30 lượt tại output QA mới. Chỉ truyền cặp consent/profile nếu người vận hành đã chấp nhận đúng cảnh báo tài nguyên. Giữ riêng bằng chứng nghe/đọc, recovery, DPI và soak trước quyết định phát hành.
