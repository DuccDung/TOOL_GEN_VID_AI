# Kế hoạch sửa Setup cho bản ZIP trên máy người dùng

Ngày lập: 2026-09-26. Nhánh `main`, commit `bc1c6aec3c731bdb0edcebbde17ae13640245e52`.

**Trạng thái: đã triển khai phần source và kiểm thử trên working tree của commit trên; đã kiểm gói chẩn đoán sau giải nén. Các bước trên máy khách/server đích chưa hoàn tất, chưa phát hành.** Working tree đã có thay đổi trong `TOOL-LOCAL/appsettings.json` trước lượt này; giữ nguyên. Kết quả và giới hạn nghiệm thu được ghi tại [báo cáo triển khai](TRIEN_KHAI_SUA_LOI_SETUP_BAN_ZIP.md).

## 1. Mục tiêu và phương án

Người dùng nhận ZIP, giải nén toàn bộ vào thư mục có quyền ghi, mở ứng dụng, đăng nhập và hoàn thành bước chuẩn bị thành phần trong giao diện. Sau lần chuẩn bị thành công, ứng dụng dùng lại runtime/model hợp lệ trên máy đó.

Phương án cho đợt này là **ZIP có đủ thành phần đi kèm, có kết nối mạng để chuẩn bị Piper ở lần đầu**. Người dùng bấm xác nhận cài trong Setup; ứng dụng xử lý tải, kiểm tra và cài đặt. Bản không cần tải model/runtime lần đầu là một hạng mục phân phối riêng, chưa nằm trong kế hoạch này.

Kết quả cần đạt:

- OCR và FFmpeg chạy từ bộ publish đã giải nén, không cần thư viện từ môi trường phát triển.
- Piper thiếu trên máy mới có luồng cài rõ ràng, kiểm tra WAV trước khi báo sẵn sàng.
- OCR lỗi không ngăn người dùng cài Piper riêng; nghiệp vụ vẫn bị khóa đến khi toàn bộ component bắt buộc sẵn sàng.
- Repair lấy đúng package của bản đang chạy; không tìm được package thì thông báo có hướng xử lý, không lặp một nút sửa thất bại.
- Gói ZIP cuối được thử trên Windows sạch và máy đã gặp lỗi, có bằng chứng theo version/build/hash.

## 2. Ghi nhận trước sửa và đầu vào còn thiếu

| Nội dung | Bằng chứng và giới hạn |
|---|---|
| Cách phân phối | Người dùng xác nhận nén ZIP rồi đưa khách giải nén, mở ứng dụng. Chưa nhận đúng ZIP hoặc thư mục đã nén. |
| Trạng thái trong ảnh | FFmpeg sẵn sàng; OCR cần sửa; Piper chưa cài. Đây là trạng thái trên máy trong ảnh, không phải máy phát triển. |
| OCR | `PaddleVietsubOcrRecognizer.ProbeRuntime` thử nạp OpenCV, model Anh/Trung và engine. Adapter thay thông tin lỗi bằng câu chung. Chưa có bằng chứng xác định DLL, model hoặc dependency nào lỗi. |
| Piper | Runtime/model nằm trong thư mục component theo người dùng, có hỗ trợ đường dẫn legacy. Marker gắn fingerprint máy/runtime. Nén thư mục publish không tự mang theo môi trường đã cài trên máy phát triển. |
| Repair | Desktop đổi HTTP 404 từ `/api/desktop-updates/repair` thành thông báo thiếu package. Server source tìm release khớp version/build/channel/platform, Active, đã tới thời điểm phát hành và có `DesktopPackage`. Chưa kiểm tra server đích; 404 cũng có thể do route không tồn tại. |
| Điểm kẹt Setup | Modal ưu tiên sửa bộ ứng dụng khi OCR/FFmpeg cần sửa, nên nút chính chưa chuyển sang cài Piper. |
| Kiểm gói hiện hành | Script kiểm file, web asset, WebView2 loader và tùy chọn probe WebView2. Chưa có bước bắt buộc probe OCR Anh/Trung từ chính ZIP cuối trên Windows sạch. |

Không dùng đường dẫn artifact hoặc kết quả trong báo cáo tháng 9 trước đây làm bằng chứng cho ZIP hiện tại. Các báo cáo đó chỉ giúp tìm đường gọi và tránh sửa lại phần đã có source.

Đầu vào cần có trước khi chốt nguyên nhân:

1. ZIP thực tế đã gửi, hoặc thư mục nguồn được nén; cách tạo gói và SHA-256.
2. Version/build của EXE, channel/platform và server mà bản đó sử dụng; chỉ lấy metadata cần thiết, không sao chép secret vào báo cáo.
3. Windows/CPU/RAM của máy lỗi, thư mục giải nén, quyền ghi, trạng thái WebView2 và kết quả chẩn đoán an toàn.
4. Môi trường thử riêng để kiểm tra release/repair, đăng nhập và SQL workflow khi nghiệm thu đầy đủ.

## 3. Phạm vi triển khai

### Bao gồm

- Chẩn đoán lỗi nạp OCR, sửa đóng gói hoặc cách nạp theo nguyên nhân đã tái hiện.
- Chuẩn bị Piper lần đầu bằng installer/checksum/probe hiện có.
- Điều chỉnh hành động Setup theo từng component, busy/cancel/retry và thông báo repair.
- Kiểm gói sau nén/giải nén, nhận diện phiên bản và hướng dẫn bàn giao ZIP.
- Cấu hình phân phối package trên server để repair hoạt động; sửa source server chỉ khi kiểm tra cho thấy cần thay đổi API.

### Ranh giới cần giữ

- Giữ hai lớp gate React và C#; chỉ component đã được kiểm tra trên máy đích mới có trạng thái `READY`.
- Giữ auth/license/organization/role/context và khóa thao tác trong lúc cài hoặc repair. Qwen vẫn tắt theo cấu hình hiện hành; không bật tính năng để xử lý lỗi đóng gói.
- Giữ model/runtime đã pin, checksum, provenance, license và cơ chế ghi file an toàn. Không chép marker READY hoặc môi trường Python đã cài của máy phát triển sang máy khách.
- Chưa chuyển workflow SQL sang API trong đợt này. Việc dùng được server và SQL `vf` vẫn là điều kiện riêng để toàn bộ desktop hoạt động; không kết luận ứng dụng sẵn sàng chỉ từ kiểm component.
- Chưa có nhu cầu migration database từ lỗi trong ảnh. Không tự thêm migration hoặc thay nghiệp vụ video, AI pricing, credential, thanh toán.
- Không đóng gói dữ liệu tài khoản, token hoặc secret. Cấu hình triển khai và quyền SQL chuyển tiếp tuân theo runbook hiện hành.

## 4. Danh sách task triển khai

Bảng dưới đây giữ tiêu chí của kế hoạch ban đầu. Trạng thái thực hiện từng nhóm nằm trong báo cáo triển khai. Task có điều kiện chỉ được thực hiện khi có bằng chứng cần thiết; nếu không cần, ghi rõ lý do thay vì sửa thêm source.

| ID | Công việc | Phụ thuộc | Đầu ra và tiêu chí hoàn tất |
|---|---|---|---|
| Z01 | Kiểm kê đúng ZIP đã gửi khách | Có ZIP/thư mục nguồn | Lưu hash, version/build, cấu trúc gói và cách publish. Đối chiếu model/native OCR, fixture, worker/lock Piper, FFmpeg, WebView2, updater, manifest và web asset; chỉ rõ file thiếu/hỏng nếu có. Giữ nguyên gói gốc. |
| Z02 | Tái hiện OCR trên môi trường sạch | Z01, máy thử | Chạy bản giải nén với PATH tiến trình chỉ có Windows, working directory khác và tài khoản chưa có component. Ghi giai đoạn lỗi, loại lỗi nạp, kiến trúc và dependency liên quan bằng thông tin đã lọc. Đối chiếu trên máy đã gặp lỗi. Không kết luận thiếu VC++/DLL chỉ từ ảnh. |
| Z03 | Kiểm tra nguồn package repair | Z01, server thử được xác định | Đối chiếu request version/build/channel/platform và phản hồi. Phân biệt route không có với không có release phù hợp, release chưa Active/chưa phát hành/thiếu artifact. Xác minh file package thực sự tải được và đúng hash; không chỉ kiểm metadata. |
| Z04 | Bổ sung chẩn đoán OCR có thể hành động | Z02 | Mã lỗi ổn định cho nhánh thiếu file, sai kiến trúc, lỗi nạp dependency, model hoặc probe; chỉ phân loại khi có bằng chứng. UI/CLI có hướng xử lý và metadata gói. Giữ lỗi chưa xác định khi không đủ dữ liệu; không đưa raw exception, absolute path hay cấu hình nhạy cảm ra giao diện/log. |
| Z05 | Sửa OCR theo nguyên nhân đã tái hiện | Z02, Z04 | Sửa publish/native loading hoặc prerequisite được chứng minh cần thiết. OCR nạp và nhận dạng fixture Anh/Trung từ bản giải nén; không phụ thuộc PATH máy build. Nếu chỉ cần đóng gói lại thì không sửa engine. Dependency mới phải được kiểm nguồn, phiên bản và quyền phân phối. |
| Z06 | Hoàn thiện cài Piper trên máy mới | Z01 | Dùng installer hiện có: xác nhận tải, kiểm dung lượng, tiến độ, hủy/thử lại và dùng lại file đã xác minh. Cài runtime tại vị trí cuối, pin dependency và probe WAV trên máy đích. Thử lại sau lỗi mạng/hủy/khởi động lại không để marker READY giả. Mở lần sau không tải lại component hợp lệ. |
| Z07 | Điều chỉnh Setup theo từng thành phần | Z04, Z06 | Có thao tác kiểm tra lại, cài Piper và sửa ứng dụng theo khả năng từng component. Ca OCR lỗi + Piper thiếu vẫn cài được Piper riêng. Không chạy cài component đồng thời với thay file ứng dụng; gate chỉ mở khi mọi thành phần bắt buộc READY. Đồng bộ C#/TypeScript, context và operation ID. |
| Z08 | Xử lý repair không có package | Z03, Z07 | Lỗi repair kết thúc busy state, giữ nguyên ứng dụng và cho hướng xử lý lấy lại/giải nén gói đầy đủ. Không tự đổi sang phiên bản khác. Phân biệt lỗi mạng, phiên đăng nhập và lỗi package khi có dữ liệu đáng tin; 404 không có nội dung xác định phải giữ nguyên mức chưa rõ nguyên nhân. Nếu cần mã lỗi API mới, sửa shared contract và server/client/test cùng thay đổi, giữ tương thích server cũ. |
| Z09 | Chuẩn hóa tạo và kiểm ZIP cuối | Z05, Z06, Z08 | Tạo từ publish sạch bằng cấu hình triển khai đã kiểm, version/build rõ ràng. Kiểm channel desktop khớp channel phát hành. Giải nén ZIP sang thư mục riêng, kiểm lại inventory/hash/manifest và probe OCR/FFmpeg/WebView2 trên nội dung vừa giải nén. Không cần Piper READY sẵn trên máy build để tạo ZIP; Piper được nghiệm thu cài mới ở Z11. |
| Z10 | Kiểm thử hồi quy và toàn solution | Z04–Z09 | Có regression tái hiện lỗi trước sửa; test backend/frontend/bridge phù hợp, rồi chạy restore/build/test chuẩn. Báo riêng Passed/Failed/Skipped và cảnh báo. Test model/SQL bị skip chưa được coi là đạt. |
| Z11 | Nghiệm thu ZIP trên máy sạch và máy lỗi | Z09, Z10 | Windows x64 trong phạm vi hỗ trợ, user thường, chưa có Python/Piper hoặc công cụ dev. Giải nén → mở → kiểm OCR/media → cài Piper → READY → mở lại. Kiểm đường dẫn có dấu/khoảng trắng, tải gián đoạn, thiếu quyền/dung lượng, không mạng và lỗi repair. Ghi hash ZIP cùng môi trường; không dùng cache user cũ làm bằng chứng cài mới. |
| Z12 | Kiểm tra sử dụng sau Setup | Z11, môi trường nghiệp vụ thử | Đăng nhập/license/organization và SQL workflow hợp lệ. Dùng dự án/video fixture có quyền sử dụng: nhập video, OCR, cue tiếng Việt thử, tạo giọng và xuất MP4. Kiểm nghe/stream/hash/thời lượng. Không cần request AI Cloud có phí; kiểm local component độc lập với server/SQL và báo riêng kết quả. |
| Z13 | Chuẩn bị và phát hành bản ZIP cùng package repair | Z10–Z12, điều kiện phát hành | Trước phát hành: kiểm FFmpeg scope Release, license/provenance và config; lưu gói/hash/version. Đăng đúng package qua luồng quản trị release, giữ các bản còn cần repair; kiểm endpoint và tải package trên môi trường đích. Chỉ tác động môi trường thật sau khi người dùng xác định môi trường và cho phép phát hành. |
| Z14 | Bàn giao và cập nhật tài liệu | Z11–Z13 | Hướng dẫn giải nén toàn bộ, chuẩn bị lần đầu, dung lượng/mạng, lấy báo cáo an toàn và phục hồi. Ghi commit/build/hash, kết quả từng môi trường và việc chưa làm. Kịch bản rollback giữ workspace/preferences/component; nghiệm thu lại trước khi phát tiếp. |

Thứ tự thực hiện:

1. Z01 → Z02/Z03: xác định đúng gói, nguyên nhân OCR và tình trạng repair.
2. Z04/Z05/Z06 → Z07/Z08: hoàn thiện runtime và hành vi Setup.
3. Z09 → Z10 → Z11 → Z12: kiểm source, ZIP và trải nghiệm thực tế.
4. Z13 → Z14: phát hành theo môi trường đã được phép và bàn giao.

## 5. Bản đồ source dự kiến

| Nhóm | File chính |
|---|---|
| OCR và thông báo lỗi | [PaddleVietsubOcrRecognizer.cs](TOOL-LOCAL/Vietsub/Ocr/PaddleVietsubOcrRecognizer.cs), [SystemSetupAdapters.cs](TOOL-LOCAL/SystemSetup/SystemSetupAdapters.cs), [DesktopReadinessCommand.cs](TOOL-LOCAL/SystemSetup/DesktopReadinessCommand.cs) |
| Setup host | [SystemSetupCoordinator.cs](TOOL-LOCAL/SystemSetup/SystemSetupCoordinator.cs), [SystemSetupContracts.cs](TOOL-LOCAL/SystemSetup/SystemSetupContracts.cs), [SystemSetupBridge.cs](TOOL-LOCAL/SystemSetup/SystemSetupBridge.cs), [StartupSystemSetupWorkflow.cs](TOOL-LOCAL/SystemSetup/StartupSystemSetupWorkflow.cs), [StartupSystemSetupGate.cs](TOOL-LOCAL/SystemSetup/StartupSystemSetupGate.cs), [Form1.cs](TOOL-LOCAL/Form1.cs) |
| Setup React | [StartupSystemSetupModal.tsx](TOOL-LOCAL/Web/src/features/systemSetup/StartupSystemSetupModal.tsx), [useSystemSetup.ts](TOOL-LOCAL/Web/src/features/systemSetup/useSystemSetup.ts), [types.ts](TOOL-LOCAL/Web/src/features/systemSetup/types.ts) |
| Piper | [DesktopComponentComposition.cs](TOOL-LOCAL/SystemSetup/DesktopComponentComposition.cs), [VietsubVoiceComponentStore.cs](TOOL-LOCAL/Vietsub/Voice/VietsubVoiceComponentStore.cs), [DEPENDENCIES.md](TOOL-LOCAL/SystemSetup/DEPENDENCIES.md) |
| Repair | [DesktopUpdateApiClient.cs](TOOL-LOCAL/Updates/DesktopUpdateApiClient.cs), [DesktopPackageUpdateService.cs](TOOL-LOCAL/Updates/DesktopPackageUpdateService.cs), [DesktopUpdatesController.cs](TOOL-SERVER/Controllers/DesktopUpdatesController.cs), [DesktopReleaseService.cs](TOOL-SERVER/Updates/DesktopReleaseService.cs), [DesktopUpdateContracts.cs](TOOL-SHARED.Contracts/Updates/DesktopUpdateContracts.cs) nếu đổi DTO public |
| Publish và kiểm gói | [TOOL-LOCAL.csproj](TOOL-LOCAL/TOOL-LOCAL.csproj), [FolderProfile.pubxml](TOOL-LOCAL/Properties/PublishProfiles/FolderProfile.pubxml), [Publish-DesktopRelease.ps1](scripts/Publish-DesktopRelease.ps1), [Test-DesktopSetupPublish.ps1](scripts/Test-DesktopSetupPublish.ps1), [DesktopMediaBundleIntegrity.cs](TOOL-DISTRIBUTION/DesktopMediaBundleIntegrity.cs) khi cần mở rộng kiểm package |

Danh sách là phạm vi dự kiến, không phải yêu cầu sửa mọi file. Tái sử dụng các luồng đang có và giữ bản sửa nhỏ nhất giải quyết nguyên nhân đã chứng minh.

## 6. Ma trận kiểm thử bắt buộc khi triển khai

| Tình huống | Kết quả mong đợi |
|---|---|
| ZIP đầy đủ, user/máy mới | OCR và FFmpeg probe thành công; Piper báo cần cài với hành động rõ ràng. |
| Thiếu/hỏng native OCR hoặc model | Gói kiểm chứng bị từ chối hoặc Setup báo đúng nhóm lỗi; không ghi READY. |
| Sai kiến trúc/khởi tạo native thất bại | Có lỗi an toàn, không gán nhầm sang thiếu package trên server. Nếu cần kiểm native riêng, dùng tiến trình probe có timeout để thu exit code mà không làm chết giao diện. |
| OCR hỏng + Piper thiếu | Có thể cài Piper riêng; desktop vẫn khóa nghiệp vụ vì OCR chưa READY. |
| Cài Piper bị ngắt mạng/hủy/đóng ứng dụng | Trạng thái có thể phục hồi; chỉ dùng lại file đúng checksum; xác minh lại trước READY. |
| Marker Piper từ máy khác hoặc runtime thay đổi | Yêu cầu kiểm/cài theo máy hiện hành; không dùng bằng chứng cũ để mở gate. |
| Mở lại sau cài thành công | Dùng lại component hợp lệ, không tải lại không cần thiết. |
| Repair khớp version/build/channel/platform | Tải package đúng hash, kiểm manifest, thay file bằng updater và khởi động lại an toàn. |
| Repair 404, server cũ, offline, 401/403 hoặc timeout | Thông báo đúng mức biết được, giải phóng busy state; không submit lặp và không tự nâng/hạ phiên bản. Giữ xử lý session hiện hành. |
| Repair đang chạy và người dùng bấm cài Piper | Khóa thao tác xung đột; không thay runtime/file trong lúc đang được dùng. |
| Đổi organization, logout hoặc hủy Setup | Phản hồi cũ không cập nhật context mới; không còn job/tiến trình do lượt đã hủy chạy ngoài kiểm soát. |
| UI gửi command nghiệp vụ khi Setup chưa READY | Host từ chối, kể cả khi DOM/modal bị can thiệp. Kiểm role Viewer và role được quản lý Setup theo policy hiện hành. |
| Thư mục có dấu/khoảng trắng; quyền ghi hoặc dung lượng thiếu | Chạy được trên cấu hình hỗ trợ hoặc báo nguyên nhân rõ, không ghi file dở thành thành công. |
| Nguồn package/hash sai, ZIP traversal, rollback | Bị từ chối trước thay ứng dụng; workspace, preferences và dữ liệu người dùng được bảo toàn. |

Các lệnh kiểm tra sau khi sửa source; kết quả thực chạy nằm trong báo cáo triển khai:

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Frontend, chạy trong `TOOL-LOCAL\Web`:

```powershell
npm ci --no-audit --no-fund
npm run build
npm test
```

Test runtime/model thật, repair package và máy sạch là các lượt riêng trên artifact cuối. Không cộng các lượt test rời để thay thế một lượt full suite bị lỗi. Nếu chạy lại collection tuần tự để chẩn đoán, ghi cả kết quả lượt mặc định và lượt tuần tự.

## 7. Điều kiện hoàn tất và phát hành

- [ ] Có nguyên nhân OCR được chứng minh bằng ZIP/máy thử, cùng regression tương ứng.
- [ ] ZIP cuối qua kiểm tra sau giải nén và OCR/media probe; cài Piper mới thành công trên máy chưa có runtime.
- [x] Setup không kẹt giữa OCR repair và Piper install; cả hai lớp gate vẫn đúng theo regression.
- [ ] Repair thành công với bản khớp và xử lý được nhánh không có package; rollback bảo toàn dữ liệu.
- [x] Build/test có kết quả thực tế trên commit nền + working tree và artifact xác định, tách Passed/Failed/Skipped trong báo cáo.
- [ ] Đã kiểm máy Windows sạch và máy lỗi; các cấu hình chưa thử được ghi rõ.
- [ ] Server/license/SQL workflow được nghiệm thu riêng; component READY không thay kết quả này.
- [ ] Đủ điều kiện provenance/license/FFmpeg Release và có môi trường phát hành được người dùng cho phép.
- [ ] Tài liệu bàn giao có version/build/hash và hướng dẫn phục hồi.

Nếu chưa có ZIP hoặc máy đích, Z01/Z02/Z11 vẫn chưa hoàn tất. Có thể hoàn thiện phần source/test phù hợp, nhưng chưa chốt nguyên nhân OCR hoặc tuyên bố bản phát hành đã xử lý lỗi cho người dùng.

## 8. Tài liệu liên quan và kiểm chứng lượt này

- [Nghiệp vụ](NGHIEP_VU_HE_THONG_VIDEOMAKER.md), [kiến trúc](KIEN_TRUC_KY_THUAT.md), [bối cảnh hiện hành](BOI_CANH_HE_THONG_HIEN_HANH.md).
- [Vận hành/phát hành](VAN_HANH_VA_PHAT_HANH.md), [kiểm thử/nghiệm thu](KIEM_THU_VA_NGHIEM_THU.md).
- [Kế hoạch desktop trên máy mới](PLAN_TASK_TOOL_LOCAL_CHAY_TREN_MAY_MOI.md), [phần đã triển khai](TRIEN_KHAI_TOOL_LOCAL_TREN_MAY_MOI.md).
- [Setup hệ thống](BAO_CAO_SETUP_HE_THONG.md), [bản sửa WebView2 trước đây](PLAN_TASK_SUA_LOI_WEBVIEW2_PUBLISH.md).

Đã đối chiếu source, cấu hình project, script publish, contract và test hiện có. Chưa nhận xác nhận ZIP thực tế đã gửi; chưa truy cập máy lỗi/server/database đích. Các kết quả kiểm tra mới phải đọc cùng môi trường và giới hạn trong báo cáo triển khai; không nâng thành trạng thái rollout.
