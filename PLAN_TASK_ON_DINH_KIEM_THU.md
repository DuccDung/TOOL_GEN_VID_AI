# Kế hoạch ổn định kiểm thử và nghiệm thu bản ZIP

Ngày lập: 2026-09-26. Nhánh `main`, commit nền `bc1c6aec3c731bdb0edcebbde17ae13640245e52`, cùng working tree đã sửa Setup ZIP.

**Trạng thái: đã triển khai source/test và runner; hoàn tất nghiệm thu tự động trên máy tham chiếu.** Hai regression đạt 100/100 lượt mỗi bài; C# và frontend đều đạt 10/10 lượt; đủ lượt native thật. Piper thất bại ở DNS; Windows thứ hai và ZIP/máy khách còn chưa xác minh. Kết quả, lỗi điều tra và phần cần môi trường đích nằm trong [báo cáo triển khai](TRIEN_KHAI_ON_DINH_KIEM_THU.md). Giữ nguyên cấu hình người dùng trong `TOOL-LOCAL/appsettings.json`.

## 1. Mục tiêu

1. Lệnh kiểm thử chuẩn chạy ổn định trên cấu hình máy đã xác định, có bằng chứng khi lỗi.
2. Các test không tranh workspace, khóa runtime, connection pool hoặc tiến trình của nhau ngoài các ca cố ý kiểm tranh chấp.
3. Giữ đầy đủ kiểm tra nghiệp vụ, bảo mật, cancel/retry và dữ liệu; sửa nguyên nhân gây lỗi thất thường.
4. Nghiệm thu riêng OCR/FFmpeg/WebView2/Piper và trải nghiệm giải nén ZIP trên máy sạch/máy khách.

Phạm vi gồm .NET, frontend, test native Windows, cài Piper và kiểm ZIP. Qwen/model khác giữ điều kiện opt-in và phạm vi tính năng hiện hành. Kiểm server/SQL hoặc request có phí cần môi trường thử được xác định; kế hoạch này không cho phép tác động production.

## 2. Bằng chứng đã có và điểm cần điều tra

Các số dưới đây là **kết quả lượt triển khai trước**, không phải kết quả chạy mới của lượt lập kế hoạch. Chi tiết: [báo cáo sửa Setup ZIP](TRIEN_KHAI_SUA_LOI_SETUP_BAN_ZIP.md).

| Nội dung | Đã biết | Cần xác minh |
|---|---|---|
| .NET chạy mặc định | Lượt cuối 1.541 Passed / 1 Failed / 13 Skipped. Test Cloud Vietsub hết thời gian chờ ở storage/job. | Vì sao chậm hoặc bị chặn khi chạy cùng suite; chưa kết luận là lỗi SQLite, khóa hay tài nguyên. |
| .NET chạy tuần tự | Lượt cuối 1.542 Passed / 0 Failed / 13 Skipped, 5 phút 12 giây. | Một lượt đạt chưa chứng minh ổn định qua nhiều lượt hoặc nhiều máy. |
| Test bridge cài bộ dịch | Lượt trước có `InstallCallCount` bằng 0 thay vì 1. | Ghi nhận phản hồi bridge và chủ thể đang giữ khóa tại thời điểm lỗi. |
| Frontend | 272 Passed / 0 Failed / 0 Skipped. | Chưa có chuỗi chạy lặp để đánh giá timer, event listener và thứ tự test. |
| ZIP có dấu | OCR Anh/Trung, FFmpeg và WebView2 đạt sau sửa lỗi marshal đường dẫn. | Chưa thử trên Windows sạch hoặc máy trong ảnh. |
| Cài mới Piper | Bài opt-in thất bại khi tiến trình không phân giải được `github.com:443`. | Nghiệm thu tải/cài/probe trên mạng hoạt động; tách lỗi môi trường khỏi lỗi installer. |

Những điểm đã đối chiếu source khi lập kế hoạch, trước thay đổi ổn định kiểm thử:

- [RuntimeUseGate](TOOL-LOCAL/SystemSetup/RuntimeUseGate.cs) có instance `Shared` trỏ vào thư mục theo tài khoản Windows. [Job manager](TOOL-LOCAL/Vietsub/Jobs/VietsubJobManager.cs) dùng khóa đọc; [dịch local](TOOL-LOCAL/Vietsub/Translation/VietsubTranslationService.cs) dùng khóa độc quyền khi cài. Đây là cơ chế cần giữ trong ứng dụng, đồng thời là điểm cần cô lập giữa fixture.
- [Test Cloud desktop](TOOL-TESTS/Vietsub/VietsubCloudDesktopTests.cs) có ca 105 cue, chờ tối đa 20 giây và đọc trạng thái mỗi 20 ms. `GetAsync` trong [job store](TOOL-LOCAL/Vietsub/Jobs/VietsubJobStore.cs) đi qua `InitializeAsync` của [subtitle store](TOOL-LOCAL/Vietsub/Storage/VietsubSubtitleStore.cs), nơi có PRAGMA và kiểm schema.
- Subtitle store mở kết nối `Cache=Private, Pooling=false`; job store dùng `Cache=Shared`, timeout 5 giây và không đặt `Pooling=false`. Nhiều fixture gọi `SqliteConnection.ClearAllPools()` khi dọn. Cần đo ảnh hưởng trước khi đổi cấu hình hoặc đường gọi production.
- [Test bridge](TOOL-TESTS/Vietsub/VietsubTranslationBridgeTests.cs) có helper chờ bằng polling 20 ms, giới hạn 8 giây; cần kiểm cả vòng đời bridge/session/job khi kết thúc fixture.
- [Project test](TOOL-TESTS/TOOL-TESTS.csproj) dùng xUnit. Chưa thấy cấu hình `xunit.runner.json` hoặc runsettings riêng trong source đã tìm. Frontend dùng `vitest run` trong [package.json](TOOL-LOCAL/Web/package.json).

## 3. Cách tổ chức kiểm thử dự kiến

| Nhóm | Cách chạy và phạm vi |
|---|---|
| Logic/API với dịch vụ giả | Cho phép song song khi mỗi fixture có dữ liệu và tài nguyên riêng; giữ assertion quyền, idempotency, revision và outbound. |
| SQLite/job/bridge local | Dùng project, DB và khóa riêng cho từng fixture. Ca kiểm tranh chấp chủ động dùng cùng tài nguyên trong phạm vi test đó. |
| OCR/FFmpeg/WebView2 thật | Có hạn mức chạy đồng thời theo RAM/CPU và yêu cầu luồng STA/UI. Nhóm nặng trên cùng máy được xếp lịch riêng. |
| Frontend | Chạy trên dependency khóa bởi lockfile, giới hạn worker theo máy thử; dọn timer, listener, DOM và mock sau từng test. |
| Model/cài runtime thật | Opt-in rõ ràng; kiểm checksum, dung lượng và tài nguyên trước chạy. Không chạy cùng build hoặc OCR/FFmpeg nặng trên cùng máy. |
| ZIP/máy khách | Dùng đúng artifact và hash đã nghiệm thu, ghi riêng môi trường và kết quả từng component. |

Lệnh chuẩn phải có cấu hình chạy rõ ràng, thực thi đủ phạm vi test bắt buộc. Chạy toàn suite tuần tự được giữ làm phép đối chiếu khi điều tra. Test đã được yêu cầu chạy thật mà lỗi mạng hoặc thiếu điều kiện phải báo bước kiểm môi trường thất bại/chưa thực hiện, không đổi thành một lượt nghiệm thu đạt.

## 4. Danh sách task

Bảng dưới giữ phạm vi và tiêu chí đã thống nhất. Trạng thái thực hiện được ghi riêng trong [báo cáo triển khai](TRIEN_KHAI_ON_DINH_KIEM_THU.md); chưa coi bước phụ thuộc mạng/máy khách là hoàn thành.

| ID | Ưu tiên | Công việc | Phụ thuộc | Tiêu chí hoàn tất |
|---|---|---|---|---|
| ST01 | P0 | Chốt baseline và kiểm kê test | — | Ghi commit + thay đổi chưa commit, hash binary, SDK/.NET/Node, lockfile, Windows/WebView2, RAM/CPU, lệnh/filter/worker. Liệt kê test dùng native, DB, khóa chung, mạng và opt-in; không dùng số test cố định trong runner. |
| ST02 | P0 | Chuẩn hóa môi trường chạy | ST01 | Mỗi lượt có thư mục ngắn riêng cho TEMP/TMP, log và artifact; mỗi fixture có workspace/DB/khóa riêng. Biến môi trường chỉ áp dụng cho tiến trình test. Kiểm quyền ghi, dung lượng và dependency; build hoàn tất trước khi chạy test trên binary cố định. |
| ST03 | P0 | Tái hiện hai lỗi thất thường | ST01–ST02 | Chạy riêng, theo cặp nghi tranh chấp và cùng nhóm; so sánh mức song song 1/2/4 phù hợp tài nguyên. Ghi mã lỗi bridge, chủ thể giữ lease, thời gian chờ SQLite và trạng thái job cuối. Có ca tái hiện có kiểm soát hoặc bằng chứng thu hẹp nguyên nhân, không quy lỗi chỉ từ stack trace. |
| ST04 | P0 | Cô lập khóa runtime giữa fixture | ST03 | Đánh giá truyền instance gate/đường dẫn vào đối tượng test; production vẫn dùng khóa chung đúng thiết kế. Hai fixture độc lập không khóa nhau; reader/writer của cùng tài nguyên vẫn bị chặn đúng, kể cả khác tiến trình. Không đổi static `Shared` qua lại để tạo “cô lập”. |
| ST05 | P0 | Ổn định SQLite và dọn fixture | ST03 | Đo tần suất khởi tạo schema, chờ khóa và vòng đời kết nối; kiểm ảnh hưởng của `ClearAllPools`. Dọn đúng DB/pool của fixture sau khi job/session đã dừng. Chỉ sửa đường gọi production nếu chứng minh là nguyên nhân; giữ kiểm schema/idempotency và dữ liệu. Ca 105 cue vẫn giữ đủ manual/locked/revision/ACK/SRT. |
| ST06 | P0 | Làm rõ việc chờ job và hủy tác vụ | ST03–ST05 | Test ưu tiên tín hiệu hoàn tất có giới hạn thời gian; đăng ký trước khi start và kiểm trạng thái DB cuối. Timeout ghi trạng thái/mã lỗi an toàn. Fixture chờ các task/tiến trình do mình tạo dừng trước cleanup. Có ca cancel, retry và callback đến muộn; không dùng sleep dài hoặc tăng timeout chung để lấy kết quả đạt. |
| ST07 | P1 | Ổn định test native và WebView2 | ST02, ST04–ST06 | Phân bổ OCR/FFmpeg theo tài nguyên, WebView2 đúng STA/message loop, profile riêng và cleanup rõ ràng. Giữ kiểm nội dung/geometry có ý nghĩa, xác định sai số hợp lệ theo DPI nếu cần. Kiểm đường dẫn có dấu, khoảng trắng, thiếu fixture/native DLL; không skip rộng test bắt buộc. |
| ST08 | P1 | Rà độ ổn định frontend | ST01–ST02 | Kiểm timer polling, mock/global, DOM, listener và callback sau unmount. Có regression cho busy/cancel, đổi organization, request cũ, repair lỗi rồi cài Piper. Chỉ dùng fake timer ở test thời gian phù hợp; thực thi đủ test qua cấu hình worker đã ghi. |
| ST09 | P1 | Tách kiểm mạng và nghiệm thu Piper | ST02, ST07 | Test lỗi DNS/timeout/hash/hủy bằng HTTP giả; kiểm installer thật ở môi trường mạng hoạt động. Kiểm DNS và HTTPS bằng runtime mà installer dùng, cùng các host redirect đã được cho phép. Cài vào thư mục trống, probe WAV, mở lại và dùng lại runtime hợp lệ; không dùng cache để gọi đó là cài mới. |
| ST10 | P1 | Tạo lệnh chạy và báo cáo nhất quán | ST04–ST08 | Dự kiến thêm script `scripts/Test-Stability.ps1` và cấu hình runner phù hợp. Chạy đủ tập test, lưu TRX/frontend report, seed/thứ tự nếu hỗ trợ, filter, thời gian và metadata; có chế độ opt-in cho ST09. Giữ mọi failure đầu tiên; tổng hợp Passed/Failed/Skipped khớp kết quả thực. Thiếu artifact, test bị bỏ sót hoặc test thất bại làm bước kiểm tra thất bại. |
| ST11 | P1 | Chạy lặp để nghiệm thu ổn định | ST10 | Đạt bốn dòng đầu của ma trận mục 5 trên cùng source/binary đã chốt; hai dòng Piper/ZIP thuộc ST09/ST12. Bất kỳ lỗi bất thường nào đều được ghi và điều tra; sau sửa chạy lại phạm vi chịu ảnh hưởng và chuỗi nghiệm thu liên quan. Không tự rerun tới khi xanh rồi chỉ lưu lượt cuối. |
| ST12 | P1 | Nghiệm thu ZIP trên máy sạch/máy lỗi | ST07, ST09, ST11 | Đúng ZIP/hash; user thường, đường dẫn có dấu/khoảng trắng, PATH không dựa công cụ dev. OCR/media → cài Piper → probe → mở lại. Kiểm repair thiếu/đúng package và lỗi mạng. Ghi riêng local component, đăng nhập/license/SQL workflow và xuất video; chỉ thử môi trường được xác định. |
| ST13 | P1 | Bàn giao và chốt phạm vi sẵn sàng | ST11–ST12 | Có lệnh chạy chuẩn, bảng kết quả từng môi trường, danh sách skip có lý do, lỗi còn mở và hướng dẫn thu chẩn đoán. Cập nhật tài liệu bằng kết quả thực trên source/artifact xác định; chỉ chốt phát hành khi các bước bắt buộc đã đạt. |

Thứ tự chính: **ST01 → ST02 → ST03 → ST04/ST05/ST06 → ST07/ST08 → ST10 → ST11 → ST12 → ST13**. ST09 có thể chuẩn bị sau ST07 và phải hoàn tất trước ST12; thiếu mạng thử Piper không ngăn hoàn thiện bộ test tự động. Các nhánh độc lập có thể chuẩn bị riêng; bài model thật vẫn tuân thủ giới hạn tài nguyên cùng máy.

## 5. Ma trận nghiệm thu đề xuất

Các số lượt dưới đây là tiêu chí đặt ra trước triển khai. Kết quả thực tế được ghi trong báo cáo triển khai; không phải cam kết ứng dụng sẽ không bao giờ phát sinh lỗi.

| Phạm vi | Số lượt và điều kiện | Điều kiện đạt |
|---|---|---|
| Hai regression lỗi thất thường | Mỗi ca 100 lượt trên tiến trình mới, kết hợp chạy riêng và với ca tranh chấp đã xác định; dùng fixture mới | 0 failure bất thường; vẫn chứng minh khóa/quyền/revision/idempotency đúng. |
| C# toàn bộ theo lệnh chuẩn | 10 lượt liên tiếp trên máy tham chiếu; 3 lượt bổ sung trên máy Windows kiểm thử thứ hai | Tất cả lượt đạt; cùng tập test bắt buộc; các skip opt-in được liệt kê. Không có tiến trình test còn chạy hoặc workspace còn bị khóa sau cleanup. |
| Frontend toàn bộ | 10 lượt, gồm tiến trình mới và kiểm thứ tự/seed khi runner hỗ trợ | 0 failure, không còn timer/listener/DOM làm ảnh hưởng test sau. |
| Native OCR/FFmpeg/WebView2 | 5 lượt mở tiến trình mới, có đường dẫn tiếng Việt và khoảng trắng | Nhận dạng đúng fixture, probe media đạt, WebView2 đúng điều kiện UI/DPI đã định. |
| Piper thật | 3 lần cài mới ở môi trường cô lập có mạng; mỗi lần kiểm mở lại | Checksum hợp lệ, probe WAV đạt, marker đúng máy/runtime, lần mở lại dùng component đã xác minh. Bổ sung hủy/lỗi mạng/thử lại theo ca kiểm riêng. |
| ZIP trước bàn giao | 3 lượt mở lại trên máy sạch và thử lại máy đã báo lỗi | Thành phần bắt buộc READY qua probe; không mở gate khi lỗi; xác nhận workflow và output riêng nếu thuộc phạm vi phát hành. |

Theo dõi thời gian từng test, thời gian chờ DB/lease, RAM đỉnh và số tiến trình. Ngưỡng thời gian/worker được chốt từ số đo trên từng cấu hình máy; không dùng kết quả máy mạnh để suy ra máy cấu hình thấp đã đạt.

Các ca âm bắt buộc: thiếu DLL, ảnh fixture hỏng, hash sai, server repair trả 404 rỗng/mã thiếu package, mất mạng, cancel/retry, workspace thiếu quyền/dung lượng, request cũ và runtime đang được dùng. Test đạt khi chứng minh ứng dụng từ chối hoặc phục hồi đúng kỳ vọng.

## 6. Phạm vi source dự kiến kiểm/sửa khi triển khai

| Nhóm | File chính |
|---|---|
| Khóa runtime và job | [RuntimeUseGate.cs](TOOL-LOCAL/SystemSetup/RuntimeUseGate.cs), [VietsubJobManager.cs](TOOL-LOCAL/Vietsub/Jobs/VietsubJobManager.cs), [VietsubTranslationService.cs](TOOL-LOCAL/Vietsub/Translation/VietsubTranslationService.cs), [SystemSetupTests.cs](TOOL-TESTS/SystemSetup/SystemSetupTests.cs). |
| SQLite và fixture | [VietsubJobStore.cs](TOOL-LOCAL/Vietsub/Jobs/VietsubJobStore.cs), [VietsubSubtitleStore.cs](TOOL-LOCAL/Vietsub/Storage/VietsubSubtitleStore.cs), [VietsubCloudDesktopTests.cs](TOOL-TESTS/Vietsub/VietsubCloudDesktopTests.cs), [VietsubTranslationBridgeTests.cs](TOOL-TESTS/Vietsub/VietsubTranslationBridgeTests.cs). |
| Native và Piper | [SystemSetupAdapterTests.cs](TOOL-TESTS/SystemSetup/SystemSetupAdapterTests.cs), [LoginWebViewIntegrationTests.cs](TOOL-TESTS/Authentication/LoginWebViewIntegrationTests.cs), [VietsubVoiceComponentStore.cs](TOOL-LOCAL/Vietsub/Voice/VietsubVoiceComponentStore.cs). |
| Frontend | [useSystemSetup.ts](TOOL-LOCAL/Web/src/features/systemSetup/useSystemSetup.ts), [StartupSystemSetupModal.test.tsx](TOOL-LOCAL/Web/src/features/systemSetup/StartupSystemSetupModal.test.tsx), [SystemSetupPanel.test.tsx](TOOL-LOCAL/Web/src/features/systemSetup/SystemSetupPanel.test.tsx), [package.json](TOOL-LOCAL/Web/package.json). |
| Runner và ZIP | [TOOL-TESTS.csproj](TOOL-TESTS/TOOL-TESTS.csproj), [Test-DesktopSetupPublish.ps1](scripts/Test-DesktopSetupPublish.ps1), [Test-DesktopBundleRuntime.ps1](scripts/Test-DesktopBundleRuntime.ps1), [Publish-DesktopRelease.ps1](scripts/Publish-DesktopRelease.ps1). |

Danh sách trên là nơi cần đối chiếu, không phải yêu cầu sửa toàn bộ file. Nếu nguyên nhân chỉ nằm trong fixture/runner thì phạm vi sửa tương ứng. Mọi thay đổi hành vi production phải có bằng chứng và regression riêng.

## 7. Các mốc bàn giao

- **Mốc A — bộ test tự động ổn định:** ST01–ST08, ST10–ST11 đạt; lệnh chuẩn có kết quả lặp và mọi failure được lưu. Chưa nâng trạng thái model thật từ các bài bị skip.
- **Mốc B — runtime và ZIP được nghiệm thu:** ST09 và ST12 đạt trên môi trường đã ghi; có version/build/hash cùng kết quả cài mới Piper.
- **Mốc C — đủ bằng chứng để quyết định phát hành:** ST13, điều kiện server/SQL, license/provenance/FFmpeg Release và các bước phát hành trong runbook đạt. Quyết định rollout là bước riêng.

Đầu vào cần bổ sung khi thực hiện ST12: đúng ZIP đã gửi, thông tin Windows/CPU/RAM máy lỗi, môi trường kiểm thử có mạng để tải Piper và server/SQL thử nếu kiểm workflow. Các task ổn định source/test có thể bắt đầu trước các đầu vào này.

## 8. Quy tắc thực hiện

- Tuân thủ [AGENTS cho test](TOOL-TESTS/AGENTS.md), [kiểm thử/nghiệm thu](KIEM_THU_VA_NGHIEM_THU.md) và [vận hành/phát hành](VAN_HANH_VA_PHAT_HANH.md).
- Dùng fixture tổng hợp, metadata/mã lỗi an toàn; không ghi token, connection string, transcript hoặc stderr nhạy cảm vào báo cáo chung.
- Chỉ dọn thư mục và dừng tiến trình do lượt kiểm thử tạo, sau khi xác minh phạm vi. Giữ nguyên IDE/app và dữ liệu của người dùng.
- Sau khi sửa source, chạy các lệnh restore/build/test chuẩn trong AGENTS cùng frontend. Báo riêng các lượt chạy mặc định, đối chiếu tuần tự, test opt-in và smoke máy đích.
- Lượt lập kế hoạch ban đầu không chạy build/test. Lượt triển khai sau đó có thay đổi source, runner và kết quả mới; xem báo cáo triển khai để phân biệt bằng chứng từng lượt.
