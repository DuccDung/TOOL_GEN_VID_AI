# Kế hoạch đóng gói ZIP desktop có Piper offline

Ngày lập: 2026-09-26. Rà soát nhánh `main`, commit nền `bc1c6aec3c731bdb0edcebbde17ae13640245e52` cùng working tree đang có các thay đổi Setup ZIP, ổn định kiểm thử và Piper offline.

**Đã triển khai đóng gói ứng viên ngày 2026-09-26 theo yêu cầu tiếp theo của người dùng.** ZIP mới đã đạt kiểm tra OCR, FFmpeg, WebView2 trên máy hiện tại và Piper offline thật. Còn thiếu phương án cấp cấu hình SQL cho máy khách; chưa xác nhận bản dùng được đầy đủ. Kết quả cụ thể nằm trong [báo cáo đóng gói](TRIEN_KHAI_DONG_GOI_ZIP.md). Các bộ kiểm thử source trước đó nằm trong [báo cáo Piper offline](TRIEN_KHAI_PIPER_OFFLINE_TRONG_ZIP.md); không ghi chúng thành lần chạy mới.

## 1. Kết quả cần bàn giao

Một file `VideoMaker-<version>-<build>-win-x64.zip` có cấu hình đúng môi trường khách, đủ ứng dụng, frontend, OCR, FFmpeg/FFprobe, worker và gói Piper offline. Người dùng giải nén toàn bộ, mở `TOOL-LOCAL.exe`, đăng nhập/chọn tổ chức; Piper tự chuẩn bị từ file local và probe trước khi READY.

- Piper có Python, uv, wheel, DLL cần thiết và model/config VAIS-1000 đi kèm. Máy khách không phải cài Python hoặc tải thêm dependency Piper.
- Login, license, tổ chức và SQL workflow vẫn cần môi trường/kết nối hiện hành. Đóng gói Piper không làm toàn bộ ứng dụng thành offline.
- Chốt điều kiện WebView2 trên máy khách trước khi cam kết “giải nén là dùng”. ZIP hiện có loader; runtime trình duyệt được kiểm riêng.
- Không bổ sung model Qwen/Kokoro hoặc bộ chuyển giọng Veo vào scope này. Các file/preview của chúng đã có trong gói vẫn phải được kiểm kê và rà soát phân phối.
- Giao kèm SHA-256, version/build/channel, danh sách thành phần, hướng dẫn mở ứng dụng và biên bản nghiệm thu đúng ZIP.
- Upload/Active release trên server, gửi file cho khách, migration và request AI có phí là các bước riêng khi có môi trường và chỉ định thực hiện.

## 2. Căn cứ từ source

| Thành phần | Trạng thái đọc được và ảnh hưởng tới đóng gói |
|---|---|
| [Publish-DesktopRelease.ps1](scripts/Publish-DesktopRelease.ps1) | Đã có đường publish self-contained `win-x64`, kiểm cấu hình, FFmpeg và Piper, tạo manifest/ZIP rồi kiểm chính ZIP sau giải nén. Script còn tạo `VideoMaker Setup.exe` riêng. |
| [TOOL-LOCAL.csproj](TOOL-LOCAL/TOOL-LOCAL.csproj) | Copy payload Piper khi đủ hai file và bật kiểm hash khi `RequirePiperOfflineBundle=true`; script release đã truyền cờ này. |
| [FolderProfile.pubxml](TOOL-LOCAL/Properties/PublishProfiles/FolderProfile.pubxml) | Profile Visual Studio hiện chưa bật `RequirePiperOfflineBundle`; dùng script release làm đường đóng gói chính cho task này. |
| [Định nghĩa Piper](third_party/voice/PIPER_OFFLINE_APPROVED.json) | Bundle `piper-1.6.0-python-3.11.15-offline-v3`, archive đã ghim 159.970.075 byte. Payload nằm trong `artifacts/piper-offline/`, bị Git ignore; checkout ở máy khác không tự có payload. Phải kiểm hash lại trước publish. |
| [Test-DesktopDeploymentSettings.ps1](scripts/Test-DesktopDeploymentSettings.ps1) | Yêu cầu HTTPS đúng server, workspace theo user, media path tương đối; chặn secret và mật khẩu SQL dùng chung. Publish hiện yêu cầu SQL workflow và lựa chọn rõ profile SQL chuyển tiếp. |
| [Hồ sơ FFmpeg](third_party/ffmpeg/win-x64/PROVENANCE.md) | Đang ghi `Approval scope: Development`. Script release yêu cầu `Release`; cần bằng chứng rà soát phân phối trước khi cập nhật hồ sơ. |
| [Hồ sơ voice](third_party/voice/LICENSES.md), [MSVC](third_party/voice/MSVC-NOTICE.md) | Còn yêu cầu hoàn tất tài liệu/phê duyệt phân phối, gồm license gốc Microsoft và các thành phần/preview thực sự đi cùng ZIP. |
| [DesktopWebViewRuntime](TOOL-LOCAL/SystemSetup/DesktopWebViewRuntime.cs) | Kiểm loader x64 và WebView2 runtime đã có trên máy. Khi thiếu runtime, trả `webview2_runtime_missing` và hướng dẫn cài Evergreen; không tự chứng minh ZIP đã có trình duyệt. |
| ZIP chẩn đoán trước đó | Dùng cấu hình rỗng để kiểm component. Bản này không thay cho ZIP có cấu hình khách hàng và nghiệm thu đăng nhập/workflow. |

## 3. Đầu vào cần chốt ở task đầu tiên

| Đầu vào | Yêu cầu |
|---|---|
| Phiên bản và số build | Giữ giá trị hiện tại `1.0.0`, build `1` cho ZIP ứng viên trong thư mục mới; không ghi đè release cũ. |
| Channel | Giữ `Stable`, platform `win-x64` theo cấu hình hiện tại. |
| Môi trường và HTTPS server | Người dùng đã chỉ định dùng server/SQL đang deploy theo cấu hình hiện tại. HTTPS có phản hồi 401 khi chưa đăng nhập; SQL kết nối được bằng cấu hình gốc. Chưa kiểm phiên đăng nhập/license/organization. |
| File cấu hình phát hành | File riêng được rà soát, truyền bằng `-AppSettingsPath`; giữ nguyên `TOOL-LOCAL/appsettings.json` của người dùng. Không đưa secret/connection string production vào tài liệu hoặc câu trả lời. |
| SQL workflow | Chốt cơ chế xác thực/kết nối mà máy khách sử dụng được, quyền chỉ trong phạm vi workflow được phép; không đóng gói mật khẩu SQL chung. |
| Máy kiểm thử | Windows x64 trong phạm vi hỗ trợ, user thường, profile/cache mới; biết trạng thái WebView2. |
| Hồ sơ phân phối | Có người phụ trách và bằng chứng rà soát FFmpeg, Piper/model và dependency thực tế. Không đổi chữ `Development` thành `Release` chỉ để qua bước kiểm. |

Nếu môi trường khách không đáp ứng SQL workflow hiện tại, việc đóng gói chưa đủ để ứng dụng dùng được. Chuyển workflow sang API là công việc kiến trúc riêng; không bỏ connection setting hoặc tắt kiểm tra để giả lập bản chỉ dùng API.

## 4. Danh sách task

| ID | Ưu tiên | Công việc | Đầu ra / điều kiện hoàn tất |
|---|---|---|---|
| DG01 | P0 | Chốt thông tin release và snapshot source | Ghi các đầu vào mục 3, branch/commit nền và hash source đang có thay đổi. Giữ cả code mới chưa commit; không lấy riêng HEAD cũ làm source phát hành. Ghi baseline cấu hình người dùng để đối chiếu sau công việc. |
| DG02 | P0 | Chuẩn bị cấu hình khách hàng | File cấu hình riêng đúng HTTPS, channel, `win-x64`, workspace theo user và media path tương đối. Chọn rõ SQL chuyển tiếp sau khi xác minh cơ chế triển khai; validator đạt mà không nhúng secret. Xác nhận feature flag theo phạm vi đã chốt, không tắt tính năng chỉ để che component thiếu. |
| DG03 | P0 | Hoàn tất hồ sơ phân phối | Rà soát inventory thực tế, nguồn/checksum/license/attribution của FFmpeg, Piper/model, Python/uv/wheel, MSVC và các asset đi kèm. FFmpeg chỉ có scope Release khi đã đủ bằng chứng; lưu hồ sơ bên ngoài gói nếu không cần cho người dùng, giữ license bắt buộc trong gói. |
| DG04 | P0 | Kiểm bộ đầu vào đóng gói | Xác minh Piper archive/manifest/worker/lock với resource đã ghim, OCR/native, FFmpeg và WebView2 loader. Kiểm lại các artifact trên máy build; nếu thiếu thì chuẩn bị từ input đã pin. Không copy venv, cache hay marker READY từ máy dev. |
| DG05 | P1 | Build và kiểm thử source được chốt | Restore/build Release và frontend theo lệnh chuẩn; chạy C# và frontend tuần tự, lưu Passed/Failed/Skipped. Dùng cấu hình native test hiện hành; model thật chạy riêng. Nếu có lỗi, giữ bằng chứng và xử lý trước bước đóng gói; không đổi timeout/assertion chỉ để đạt. |
| DG06 | P1 | Tạo ZIP ứng viên bằng script hiện có | DG01–DG05 đạt rồi chạy `Publish-DesktopRelease.ps1` với tham số đã chốt. Output mới trong `artifacts/releases/<version>-<build>/`; self-contained x64, app/settings/frontend/native/worker/Piper/notices đầy đủ, manifest có payload Piper. Không sửa code nếu quy trình hiện tại đáp ứng. |
| DG07 | P1 | Kiểm chính ZIP ứng viên | Giải nén ZIP vừa tạo vào thư mục mới. Kiểm inventory, version/build, frontend, OCR Anh/Trung, FFmpeg, WebView2 và Piper offline thật. So SHA-256/manifest, rà soát file không được phát hành; thiếu/hỏng thành phần thì ứng viên chưa đạt. |
| DG08 | P1 | Nghiệm thu trên Windows sạch | User thường không có Python/uv trong PATH, root/cache mới và đúng điều kiện WebView2. Kiểm startup với tài khoản/organization thử được chỉ định, Piper tự chuẩn bị, mở lại, nghe WAV và dùng media fixture để playback/xuất MP4. Không gọi provider có phí. |
| DG09 | P1 | Chốt artifact và khả năng quay lại | Đối chiếu toàn bộ ma trận mục 5; ghi SHA-256 ZIP cuối, dung lượng, môi trường, thời gian, log an toàn và lỗi đã gặp. Giữ gói trước cùng dữ liệu user; nếu chưa kiểm updater/server thì ghi chưa xác minh. Mọi sửa đổi nội dung ZIP sau đó phải tạo lại hash và kiểm lại phần bị ảnh hưởng. |
| DG10 | P1 | Bàn giao | Giao đường dẫn ZIP đã đạt, checksum, hướng dẫn ngắn và báo cáo nghiệm thu. Ghi rõ Windows/WebView2/kết nối cần có, model nào được kèm, cách báo lỗi và quay lại gói trước. Chỉ upload/Active hoặc gửi cho khách khi được chỉ định riêng. |

Thứ tự: **DG01 → DG02/DG03/DG04 → DG05 → DG06 → DG07 → DG08 → DG09 → DG10**. Có thể kiểm input và hồ sơ độc lập trong lúc chờ cấu hình/máy thử; không cần sửa lại bộ cài Piper đã có chỉ để bắt đầu đóng gói.

Nếu phải thay byte trong payload Piper, worker hoặc lockfile khi hoàn thiện hồ sơ, thực hiện quy trình cập nhật bundle/định nghĩa và kiểm chứng lại theo [plan Piper](PLAN_TASK_PIPER_OFFLINE_TRONG_ZIP.md). Hash của gói cũ không xác nhận gói đã thay đổi.

## 5. Ma trận nghiệm thu của ZIP cuối

| Phép kiểm | Điều kiện đạt |
|---|---|
| Cấu hình và thành phần | Đúng server/version/build/channel/platform, không có secret, đường dẫn máy dev, profile WebView2, cache/venv/marker của máy build hoặc dữ liệu project người dùng. Có đủ license/attribution bắt buộc. |
| Cài Piper mới | Chỉ lấy Python/wheel/model từ payload local, cache mới, tạo WAV Việt thật và READY. Lưu phương pháp chặn/kiểm mạng riêng cho tiến trình Piper; không chặn kết nối login/SQL hoặc đổi DNS/proxy/firewall toàn máy. Không gọi phép thử proxy là packet capture nếu chưa đo lưu lượng. |
| Mở lại và đường dẫn | Mở lại ít nhất ba lần; kiểm đường dẫn có dấu/khoảng trắng, thư mục ứng dụng chỉ đọc và workspace trên ổ khác. Runtime lưu ở vùng ghi được của user, không yêu cầu quyền admin để cài Piper. |
| WebView2 | Kiểm loader lẫn runtime trên máy sạch. Nếu máy thiếu runtime, ghi đúng prerequisite và cách chuẩn bị. Yêu cầu dùng ngay trên cả máy chưa có runtime cần một phương án bổ sung được chốt và kiểm riêng trước khi tuyên bố đạt. |
| Startup có auth | Đăng nhập/license/organization hoạt động trên môi trường đã chỉ định; các thành phần bắt buộc được probe READY trước khi mở gate. Quyền và context vẫn được kiểm ở host. CLI component đạt không thay cho ca này. |
| Media/Vietsub | OCR fixture Anh/Trung đạt, nghe WAV Việt, playback và xuất MP4 từ project/fixture thử; kiểm stream, audio và thời lượng. Không dùng project thật của người dùng làm fixture. |
| Hỏng gói, hủy và thử lại | Dùng bản sao riêng để kiểm thiếu/hỏng payload, hủy/thử lại và cài sửa local. Không READY sai, không tải dependency Piper ngầm, không còn tiến trình con. Không sửa ZIP cuối cho các phép thử phá hỏng này. |
| Đối chiếu cuối | SHA-256 và metadata khớp file bàn giao; source/binary không đổi trong quá trình kiểm. Bài bị skip/môi trường chưa truy cập được báo riêng. Chưa đủ máy sạch/auth/hồ sơ phân phối thì giữ trạng thái ứng viên. |

Tham khảo cách kiểm và bằng chứng cũ trong [báo cáo Piper](TRIEN_KHAI_PIPER_OFFLINE_TRONG_ZIP.md); không lấy thời gian, kích thước ZIP hoặc số test của bản chẩn đoán làm kết quả cho ZIP khách hàng mới.

## 6. Lệnh dự kiến khi triển khai

Các lệnh dưới đây **chưa chạy trong lượt lập plan**. Điền đầu vào sau DG01; lệnh publish chỉ dùng khi cấu hình SQL chuyển tiếp và hồ sơ phát hành đã được chốt.

```powershell
.\scripts\Test-PiperOfflineBundle.ps1 -ArtifactOnly
.\scripts\Test-FfmpegBundle.ps1 -BundlePath '.\third_party\ffmpeg\win-x64' -RequireReleaseApproval

.\scripts\Publish-DesktopRelease.ps1 `
  -Version '<version>' -BuildNumber <build-number> -Channel <channel> `
  -ServerBaseUrl 'https://<server>/' `
  -AppSettingsPath '<absolute-path-to-reviewed-settings.json>' `
  -AllowTransitionalSql

.\scripts\Test-DesktopSetupPublish.ps1 -PublishRoot '<extracted-zip-directory>' `
  -ProbeWebView2 -ProbeBundledComponents -ProbePiperOffline
```

`-AllowTransitionalSql` là lựa chọn triển khai rõ ràng mà source hiện yêu cầu, không cho phép nhúng mật khẩu SQL hoặc bỏ kiểm quyền. Có thể truyền `-PiperBundlePath`/`-FfmpegBundlePath` để dùng bộ đã duyệt nằm ngoài vị trí mặc định.

Restore/build/test thực hiện theo [AGENTS.md](AGENTS.md) và [hướng dẫn kiểm thử](KIEM_THU_VA_NGHIEM_THU.md). Không chạy full frontend đồng thời với C#; không chạy model thật đồng thời với build/OCR/FFmpeg nặng. Lặp thêm khi có lỗi hoặc thay đổi cần xác minh, không tự chạy lại toàn bộ các chuỗi dài của đợt trước.

## 7. Điều kiện chốt bàn giao

- **Có ZIP ứng viên:** đã chốt cấu hình, đủ hồ sơ/input, build và kiểm ZIP đạt.
- **Có ZIP dùng được trên môi trường khách đã thử:** hoàn tất Windows sạch, auth/SQL/WebView2 và các ca media/voice, có checksum cùng báo cáo.
- **Đã phát hành:** chỉ ghi sau thao tác phân phối/Active trên môi trường được chỉ định và có bằng chứng riêng.

Script hiện còn tạo `VideoMaker Setup.exe`; file này tải package qua server theo luồng hiện hành. Việc có file Setup hoặc ZIP local không tự tạo package Active cho cập nhật/sửa chữa trên server. Bàn giao ZIP giải nén trực tiếp không đòi thay server release trong task này; khả năng repair qua server phải được kiểm riêng nếu muốn cam kết hỗ trợ.

## 8. Tiến độ triển khai ngày 2026-09-26

| Task | Kết quả thực tế |
|---|---|
| DG01 | Giữ nhánh/commit nền và working tree; đối chiếu 988 file source với snapshot đã kiểm trước đó. Cấu hình gốc giữ nguyên. |
| DG02 | Đã tạo cấu hình riêng giữ endpoint/channel/workspace/feature flag, bỏ mật khẩu SQL dùng chung. Cấu hình SQL để khách dùng còn chờ; tài khoản gốc có quyền đọc ngoài `vf`. |
| DG03 | Giữ nguyên hồ sơ/license/provenance. FFmpeg vẫn có scope `Development`; chưa hoàn tất phê duyệt phân phối. |
| DG04 | Hash payload Piper, worker/lock và bộ FFmpeg đạt; publish có đủ các thành phần bắt buộc. |
| DG05 | `dotnet publish` Release self-contained x64 thành công, yêu cầu đủ FFmpeg/Piper. Không sửa source sản phẩm trong lượt đóng gói; không chạy lại các chuỗi test source đã đạt trên snapshot không đổi. |
| DG06 | Đã tạo ZIP ứng viên local mới bằng `dotnet publish` và kiểm tra artifact. Chưa chạy đường release hoàn chỉnh có phê duyệt phân phối hoặc upload server. |
| DG07 | Chính ZIP sau giải nén: 50 thành phần, 24 web asset, 0 thiếu/sai; OCR/FFmpeg/WebView2 READY; Piper cài mới và kiểm lại 3 lần READY. |
| DG08 | Chưa có Windows sạch riêng; chưa kiểm đăng nhập/workflow bằng tài khoản ứng dụng. |
| DG09 | Đã lưu SHA-256, inventory và bằng chứng kiểm ZIP. Chưa chốt ứng viên thành bản dùng được đầy đủ. |
| DG10 | Có ZIP ứng viên và báo cáo để rà soát; chưa bàn giao như bản cuối cho người dùng. |

Không chạy migration hoặc sửa quyền/tài khoản SQL thật trong lượt đóng gói. Chi tiết đường dẫn, checksum và phần cần xử lý tiếp được ghi trong [báo cáo triển khai](TRIEN_KHAI_DONG_GOI_ZIP.md).
