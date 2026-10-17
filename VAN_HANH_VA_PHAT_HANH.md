# Vận hành và phát hành VideoMaker

## Luồng đang chọn: StandaloneCatalog

Migration additive **4.1.21 đã áp và xác minh** trên `MB2D70\MSSQLSERVER2019 / tap51004_tool` ngày 2026-10-17 lúc 09:49:14 UTC theo yêu cầu người dùng; không cần chạy lại trên database này. [Biên bản đích, bảo toàn dữ liệu và giới hạn backup/restore](TRIEN_KHAI_SQL_4_1_21_HOSTING.md). Tiếp theo deploy server mới với `PublicationMode=StandaloneCatalog`, upload Setup 1.3.0 và ZIP tool-local mới lên VPS, lưu link EXE rồi tạo release bằng phiên bản/link ZIP. Giữ nguyên tên có hash tự sinh; không cần đăng ký hoặc Active Setup trên hosting. Model ZIP giữ nguyên. Setup 1.2.0 không hỗ trợ catalog 5. [Runbook chi tiết và trạng thái kiểm chứng](TRIEN_KHAI_PHAT_HANH_DON_GIAN_VPS.md). Chưa deploy hosting trong lượt SQL này; các yêu cầu Setup Active bên dưới thuộc lịch sử.

## Link tải Setup trên website

Deploy server mới rồi mở **Admin → Phát hành desktop → Link tải Setup cho Windows**, lưu URL EXE HTTPS trên VPS được cấu hình. Server lưu key `Desktop.SetupDownload` trong `auth.ServerSettings` có sẵn và chuyển hướng nút tải trang chủ; không tải/lưu file EXE. Hosting đã được kiểm tra bảng/quyền chỉ đọc, không cần migration mới cho phần này. Link này không thay việc đăng ký Setup Active để phát hành release. [Hướng dẫn và bằng chứng](TRIEN_KHAI_LINK_TAI_SETUP_TREN_VPS.md).

## Phát hành DirectCatalog

Đường khởi tạo hiện được người dùng chọn là [**4.1.20 Simple**](database/VideoFactory.4.1.20.DirectDesktopCatalogSimple.sql), sau 4.1.18: tạo cùng schema catalog, không thao tác quyền, không yêu cầu role/CONTROL. Chỉ dùng file này cho nhánh Simple; không cần chạy tiếp 4.1.19 hoặc wrapper DBA để khởi tạo. Marker nền 4.1.19 ở nhánh này chỉ xác nhận schema; không chứng minh phân quyền đã được nghiệm thu. Theo yêu cầu người dùng, exact Simple đã commit trên `MB2D70\MSSQLSERVER2019 / tap51004_tool` lúc **2026-10-17 07:47:34 UTC** và được kết nối mới xác minh schema/marker, dữ liệu cũ và quyền nhìn thấy giữ nguyên. Backup/restore và clone hosting vẫn chưa xác minh; chưa có xác nhận deploy runtime hoặc Setup Active từ lượt SQL này. [Bằng chứng SQL và cách dùng](TRIEN_KHAI_SQL_4_1_20_DON_GIAN.md).

Lịch sử: lần áp **4.1.19** đã rollback vì SQL 15151 tại quyền DENY trên bảng mới; [biên bản/gói DBA cũ](TRIEN_KHAI_SQL_4_1_19_HOSTING.md) được giữ nguyên, không dùng kết quả rehearsal thay trạng thái hosting.

`FolderProfile` hiện tự tạo ZIP canonical trong `artifacts/releases/direct/<version>-<build>/`. Upload nguyên ZIP, giữ tên chứa SHA-256. Admin chỉ cần phiên bản và URL; ghi chú/bắt buộc cập nhật là tùy chọn. Hosting kiểm cấu trúc URL và identity khai báo, không xác minh bytes VPS. Setup 1.2.0 capability `3,4` mới tải/kiểm bytes, native identity, runtime/model và transaction trên máy đích. Sai hash không được chạy payload. Schema catalog đã có qua **4.1.20 Simple**; vẫn cần server mới và Setup hệ thống Active trước phát hành. [Runbook và bằng chứng](TRIEN_KHAI_PHAT_HANH_TRUC_TIEP_TU_VPS.md), [Publish](scripts/VisualStudioPublish.README.vi.md).

## Lịch sử: Publish bằng Visual Studio qua ServerInspection

`FolderProfile` mặc định xuất thư mục Publish, tự chuẩn bị FFmpeg 9.0.1 offline theo pin và giữ worker/runtime/license cần thiết. Sau Publish thành công, nén **toàn bộ** nội dung hoặc kèm một thư mục ngoài `publish/`, upload ZIP lên VPS rồi nhập **phiên bản + link tải** trong Admin. Không cần tự viết ba JSON phát hành; server kiểm và sinh metadata/checksum. ZIP có một phần metadata cũ nhưng thiếu các file còn lại bị từ chối để tránh trộn hai định dạng. Không ghi đè bytes của release đã phát hành; tăng version/build cho bản khác. [Runbook mới](TRIEN_KHAI_ZIP_PUBLISH_DON_GIAN.md), [cấu hình Publish](scripts/VisualStudioPublish.README.vi.md).

Cần deploy server có luồng kiểm ZIP mới, build/đăng ký **Setup dùng chung 1.1.0 trở lên** một lần bằng API/profile thật và installer WebView2 approved. Script build Setup kiểm pin/chữ ký và nhúng installer; Setup chỉ dùng nó khi máy đích thiếu runtime và ZIP không kèm installer. Sau đó tạo nháp release, chờ kiểm ZIP đạt, phát hành rồi chạy Setup để cài/probe model và commit. Setup 1.0 cũ không cài được định dạng này. Desktop cũ chưa nhận route ứng dụng `/models/` cần chạy Setup mới độc lập hoặc dùng `/apps/videomaker/`; không suy ra cập nhật tự động của binary cũ đã được nghiệm thu.

Nếu muốn tạo ZIP đã kiểm kèm metadata ngay trên máy build, đặt `DesktopCreateVerifiedReleaseZip=true` và lấy output tại `artifacts/releases/<version>-<build>`. Cấu hình release riêng, OCR/native runtime, pin FFmpeg và quyền workflow SQL vẫn phải hợp lệ. Scope FFmpeg `Release/TechnicalPackaging` không xác nhận hồ sơ corresponding source/GPL hoặc production đã đủ; các qualification `false` được giữ. [Biên bản FFmpeg lịch sử](TRIEN_KHAI_CHUAN_HOA_FFMPEG_VA_PUBLISH.md) ghi riêng lượt ZIP từng bị chặn dung lượng; không dùng trạng thái đó thay kết quả ZIP thật mới được inspector kiểm chỉ đọc.

## Form release tối giản và Setup cấp hệ thống

Theo yêu cầu triển khai ngày 2026-10-17, `TOOL-SERVER/appsettings.json` trên checkout `main`, HEAD nền `f0651c918f28ca477f28875818148e9e86240b7b` cùng working tree đã cấu hình `SharedBootstrapperEnabled=true`, `ApiBaseUrl=https://taphoatool.com/`, `Profile=Production` và `AllowedExternalPrefixes=[https://103.179.190.182:8443/models/]`. Bổ sung này không bật luồng protocol 2 và không tạo/Active Setup hoặc phát hành release. Các profile Publish và script IIS giữ mục `DesktopReleases`; khi deploy cần bảo đảm cấu hình hiệu lực trên hosting nhận các giá trị này rồi khởi động lại ứng dụng. File `appsettings.Production.json` cũ hoặc biến môi trường `DesktopReleases__*` có thể ghi đè; cấu hình runtime đích chưa được kiểm trực tiếp trong lượt này. Mặc định dự phòng của lớp options vẫn tắt khi thiếu cấu hình.

Luồng mới là opt-in protocol 3. Phải áp `VideoFactory.4.1.18.SharedDesktopBootstrapper.sql` **trước khi chạy binary server mới**, kể cả khi cờ shared còn tắt vì EF đã ánh xạ cột mới. Rehearsal SQL Server 2019 riêng không chứng minh backup/restore hoặc schema hosting. Runbook build Setup một lần, build ZIP `SharedVps`, cấu hình API/profile/nguồn VPS, đăng ký Setup rồi nhập version/link nằm trong [biên bản mới](TRIEN_KHAI_FORM_RELEASE_TOI_GIAN_VA_SETUP_DUNG_CHUNG.md). Không dùng Setup fixture trỏ `api.invalid`/`metadata.invalid` cho khách; FFmpeg Release, Windows A/B và model probe thực vẫn là gate nghiệm thu.

Ngày **2026-10-17**, đã áp đúng migration 4.1.18 trên `MB2D70\MSSQLSERVER2019 / tap51004_tool` theo yêu cầu tiếp tục của người dùng; schema/marker, dữ liệu cũ, FK/index và quyền nhìn thấy đã được kiểm, gồm kết nối mới sau commit. Backup/restore và clone hosting vẫn **chưa xác minh**; không ghi quyền tiếp tục thành xác nhận đã có backup. Xem [biên bản SQL hosting](TRIEN_KHAI_SQL_4_1_18_HOSTING.md). Lượt này không deploy server, đăng ký Setup hoặc phát hành release.

## Phát hành ZIP ứng dụng trên VPS

Luồng mới có nguồn `ExternalVps`, pipeline `-DeliveryMode ExternalVps -ModelDeliveryMode StaticArchive -PackageDownloadUrl ...`, JSON đăng ký và exact Setup từng release. Đọc [runbook và bằng chứng](TRIEN_KHAI_PHAT_HANH_TOOL_LOCAL_QUA_VPS_VA_SETUP.md) trước thao tác: migration `4.1.17-desktop-external-artifacts` phải được rehearsal/áp trước binary mới, kể cả khi external tắt; giữ file/release cũ cho repair. API VideoMaker khác kho file VPS. Ngày 2026-10-17, theo yêu cầu tiếp tục của người dùng, đã áp duy nhất 4.1.17 trên hosting `tap51004_tool`, kiểm schema/marker/quyền bằng kết nối mới đạt; backup/restore và rehearsal clone hosting vẫn chưa xác minh. Lượt SQL này không đổi cấu hình ứng dụng, deploy, upload hoặc publish release.

## Nghiệm thu runtime đã cài bằng StaticArchive — 2026-10-17

Sau khi chính Setup đã tải/cài/probe/commit trên máy QA, dùng `scripts/Test-InstalledStaticModel.ps1 -InstallRoot <root đã cài> -Engine qwen -OutputDirectory <QA mới> -ColdStarts 3 -Iterations 30`; Piper dùng `-Engine piper -ColdStarts 1 -Iterations 30`. Output dùng đường local ngắn, mới và tách khỏi app/runtime/cache/metadata. Harness lấy root/pin từ bản cài, không nhận model/executable override và không cài/sửa/refresh receipt. Chờ tác vụ đang dùng runtime kết thúc nếu gặp lease busy; công cụ không tự đóng app/IDE.

Chỉ thêm cả `-AcceptResourceWarning` và `-ConfirmedResourceProfile <id chính xác>` khi đã chấp nhận cảnh báo tài nguyên tương ứng. Chạy model riêng, không cạnh build/OCR/FFmpeg nặng. Giữ report thất bại và audio fixture; kết quả định dạng WAV không thay nghe thử. Harness bundled `Test-QwenOfflineBundle`/`Test-PiperOfflineBundle` vẫn dành cho layout bundled/diagnostic, không copy model dev vào publish để thay bài installed.

Bản build thông thường chưa compile policy bắt buộc sẽ trả `installed_model_release_required` trước native hoặc tạo output; đây là guard đúng, không phải model thất bại. Candidate khách vẫn cần FFmpeg Release, profile hợp lệ và API release staging. [Biên bản khắc phục](TRIEN_KHAI_KHAC_PHUC_VA_NGHIEM_THU_SETUP_MODEL.md) ghi kết quả kiểm thử/HTTP, source/binary identity và phần Windows sạch/model thật chưa nghiệm thu; chưa deploy server hoặc cập nhật hosting runtime.

## Nghiệm thu ổn định Setup/model — 2026-10-17

Profile `StaticSetup` của `scripts/Test-Stability.ps1` lặp fixture tải/cài/receipt/Form/Job, không tự bật model/SQL opt-in hoặc thay bài Windows sạch. Giữ journal `Committed` và receipt của bản cài đang dùng vì startup còn đối chiếu chúng; không dọn metadata production để lấy dung lượng test. Dùng root QA riêng và giữ logs của lượt fail. Lượt ổn định trước bổ sung Publish dùng FFmpeg scope Development; bundle kỹ thuật mặc định mới được mô tả ở đầu tài liệu và không tự chứng nhận phát hành khách. [Kết quả lịch sử và đầu vào khi đó](TRIEN_KHAI_TEST_ON_DINH_SETUP_MODEL_UI_UX_VA_LOGIC.md).

## Publish Setup StaticArchive — 2026-10-16

`Publish-DesktopRelease.ps1 -ModelDeliveryMode StaticArchive` yêu cầu profile đủ bốn component local, nguồn model compile pin và `-WebView2InstallerPath` đúng approved size/hash/chữ ký Microsoft. Script ghép Setup với package desktop chính xác; giữ gate FFmpeg Release, profile khách, Authenticode và cap updater. ZIP model phục vụ riêng trên VPS. Không đóng nguyên cấu hình phát triển, cache, venv hoặc READY. [Lệnh chuẩn bị, bằng chứng và xử lý lỗi](TRIEN_KHAI_SETUP_CAI_MODEL_TU_VPS.md); chưa publish release hoặc thay môi trường thật trong lượt này.

### Gói server kèm model — 2026-10-16

`scripts/New-ServerModelDelivery.ps1` tạo ZIP local gồm server, private runtime, handover model nguyên bản và publisher offline; kiểm inventory/hash trước và sau giải nén. Dùng output mới ngoài checkout, inventory pin độc lập và đường dẫn ngắn. [Gói thật, checksum và hướng dẫn IIS](TRIEN_KHAI_DONG_GOI_SERVER_KEM_MODEL.md) đã có; không đặt toàn bộ thư mục bàn giao thành website, chỉ dùng `server/`. Gói làm sạch cấu hình nhạy cảm theo quy tắc và để kho model tắt/chưa ký; cần cấu hình, schema và nghiệm thu đích trước startup. Không tự upload hoặc chạy migration từ bước đóng gói.

### Kho model Qwen/Piper và handover OCR — 2026-10-16

Đã thêm công cụ gom candidate và publisher offline, module server chỉ đọc cùng mode đóng gói `Bundled|Server`; mặc định chưa bật kho online. Quy trình, artifact thật, kiểm thử và các gate còn thiếu ở [biên bản triển khai kho model](TRIEN_KHAI_KHO_MODEL_SERVER_VA_CAI_DAT_AN_TOAN.md); tham số ký/revoke/promote ở [hướng dẫn Publisher](TOOL-MODEL-PUBLISHER/README.md). Chưa có server/domain/thư mục đích hoặc khóa production được chỉ định; không suy từ appsettings hiện có và không tự upload/reload môi trường thật. Chỉ cập nhật pin envelope sau khi đã kiểm kho tại server; chữ ký model riêng với Authenticode của Setup/EXE. Không coi file đúng hash hoặc smoke localhost là RuntimeQualified/Published.

### Chẩn đoán và kiểm tra ổn định Qwen — 2026-10-16

Worker 1.2.1 / IPC 3 lưu báo cáo lỗi an toàn tại `%LOCALAPPDATA%\ToolGenPostVideo\system-setup\qwen-diagnostics`. Mỗi báo cáo có mã chẩn đoán, phase, exit code decimal/hex, fingerprint, phiên bản runtime và snapshot RAM/commit; không lưu prompt, stderr, đường dẫn hay dữ liệu tài khoản. Giữ tối đa 20 báo cáo, mỗi báo cáo tối đa 16 KiB. Ghi báo cáo thất bại không thay lỗi gốc. Setup hiển thị mã chẩn đoán khi báo cáo đã được ghi; hủy do người dùng không bị ghi thành crash.

Fingerprint mới gồm cả runtime .NET self-contained, runtimeconfig và deps. Bản mới phải probe lại; không mang marker READY cũ qua. Giữ cờ dịch local và gate hiện hành. Không thay DLL runtime hoặc nâng dependency riêng lẻ để thử trên máy khách; phải dùng bộ publish đầy đủ và kiểm lại.

Kiểm đúng model/worker của bản giải nén và giữ JSON kể cả khi probe thất bại:

```powershell
.\scripts\Test-QwenOfflineBundle.ps1 -PublishRoot 'D:\BanKiemTra' -ResultPath 'D:\ChanDoan\qwen-probe.json'
.\scripts\Test-QwenStability.ps1 -PublishRoot 'D:\BanKiemTra' -OutputDirectory 'D:\ChanDoan\qwen-stability-moi' -ColdStarts 3 -Iterations 30
```

Thư mục kết quả stability phải mới. Mỗi cold start dùng workspace riêng, kiểm runtime/Anh/Trung rồi dịch fixture luân phiên; từ 30 lượt mới đánh giá tăng bộ nhớ private qua cửa sổ sau warm-up, giới hạn 256 MiB như bài soak native hiện có. Report không chứa nội dung dịch và luôn để `CustomerReleaseQualified=false`, `OtherMachinesVerified=false`. Đây là bằng chứng cho máy/bundle đã chạy, chưa thay nghiệm thu ma trận Intel/AMD, RAM, Windows sạch và GPU.

Chỉ thêm `-AcceptResourceWarning` sau khi người vận hành chấp nhận rõ cảnh báo RAM/commit của máy thử. Không chạy model song song build/OCR/FFmpeg nặng; không dừng ứng dụng/IDE người dùng để lấy tài nguyên. Thiếu tài nguyên, hết đĩa hoặc chưa chạy inference phải ghi chưa nghiệm thu. `New-DesktopOfflineCandidate.ps1` mặc định giới hạn build một worker MSBuild; không tự nâng trạng thái candidate thành Release.

### Gói Local cá nhân / Cloud — source 2026-10-15

Triển khai đồng bộ server, Web Admin và desktop/contracts. Trước deploy server mới, xác minh instance/database, backup và khả năng restore; áp `database/VideoFactory.4.1.16.VietsubPersonalProjects.sql` trên database đích được phê duyệt. Migration idempotent chỉ cho phép NULL ở `vs.Projects.OrganizationId`, ghi schema version; giữ FK, dữ liệu cũ và quyền SQL. Không cấp desktop quyền SQL vào `vs`/`auth`/`ai`. Source này chưa được áp migration hoặc rollout lên hosting.

Mở bán Local: nhập gói, tắt Cloud, kiểm SePay riêng. Không cần nhóm, tổ chức, key/rate/budget AI. Máy khách vẫn phải cài và probe runtime/model; cờ bật hoặc license hợp lệ không tự chứng minh runtime đã sẵn sàng. Cloud vẫn cần tổ chức nhận khách, sức chứa, schema job, cờ/model dịch, OpenAI Active, rate InputToken/OutputToken và ngân sách.

Smoke trên staging được cho phép: khách không thuộc tổ chức mua Local → nhận license/lease → Setup → tạo project cá nhân → OCR/dịch/giọng/xuất Local; kiểm tài khoản khác không mở project, hết hạn/thu hồi license chặn job, Cloud không chạy trên project cá nhân. Kiểm webhook lặp, lịch nối gói và đơn Local cũ đang giữ chỗ được release khi cấp license. Giữ dữ liệu lịch sử, không tự chuyển project tổ chức sang cá nhân. Nếu quay về binary cũ, dừng phát hành gói Local mới và dùng backup/forward fix được duyệt; không đổi cột về NOT NULL khi đã có project cá nhân.

Không tự gửi webhook thật, gọi provider có phí hoặc thay key trên môi trường dùng chung. Thực thi migration SQL Server và smoke hosting còn **chưa xác minh**.

Cú pháp đổi nullability được đối chiếu với [ALTER TABLE của Microsoft](https://learn.microsoft.com/en-us/sql/t-sql/statements/alter-table-transact-sql?view=sql-server-ver17#null--not-null); việc đối chiếu tài liệu không thay thế rehearsal trên SQL Server đích.

### Tải Bilibili — 2026-09-11

Không cần migration/server flag. Desktop tải yt-dlp 2026.08.19 bằng thao tác **Chuẩn bị công cụ tải**, kiểm pinned size/SHA-256, giữ license/provenance bên runtime; không tự nâng bản. Chỉ triển khai frontend cùng native bridge mới. Chức năng dùng video public và có thể bị Bilibili hạn chế theo mạng; giữ partial, không báo quét đủ khi chưa hoàn tất. Hàng đợi trong phiên, MP4 lưu ở máy; rollback desktop không xóa video đã tải. [Hướng dẫn, nguồn gốc và giới hạn smoke](TRIEN_KHAI_TAI_VIDEO_BILIBILI.md).

### Phục hồi kết quả video ngắn — 2026-09-11

Mọi server/worker chia sẻ database phải cùng truy cập kho video qua `Generation:VideoOutputs:StorageRoot` tuyệt đối. Máy Development đã thống nhất kho hiện hữu của server `vid-long`, giữ nguyên bản gốc và kiểm hash các tệp còn hạn; không sửa SQL hay tạo lại tác vụ. Khi chuyển kho cần bảo toàn các tệp đang được database tham chiếu. Xem [biên bản phục hồi và cấu hình](SUA_LOI_TAI_VIDEO_NGAN.md).

### Video ngắn dùng Veo — 2026-09-11

Triển khai đồng bộ server/contracts/desktop/frontend. Video ngắn yêu cầu policy `LongForm` Fal/Veo 3.1 Standard hoặc Fast, Native Audio 720p, credential/model/rate Active và budget. Không dùng policy Default Kling để fallback. Schema 4.1.9 đã có là điều kiện bắt buộc cho báo giá video ngắn cả TextOnly; thay đổi này không thêm migration. Máy Development `DUNGDEV / VideoFactory` đã có schema này và policy Veo 3.1 Fast qua Fal; lần triển khai này chỉ kiểm SQL đọc, không sửa SQL, rate hoặc credential.

Dự án cũ được chuyển bằng **Chuyển dự án sang Veo** trong UI, sau xác nhận thời lượng/tỷ lệ và kiểm không có tác vụ pending/Unknown. Không chạy SQL cập nhật hàng loạt snapshot; không tự gửi lại request lỗi để kiểm thử. Khi rollback, giữ schema, lịch sử request/ledger và thư viện; binary cũ không hiểu quote/lineage Veo video ngắn không được dùng để tạo hoặc render các dự án đã chuyển. [Quy trình và bằng chứng kiểm thử](TRIEN_KHAI_VIDEO_NGAN_VEO.md).

### Thư viện nhân vật/trang phục cục bộ — 2026-09-11

Kho mới ở `%LOCALAPPDATA%\VideoMaker\ShortVideoLibrary`, tách scope user/org, không cần SQL Server migration. Backup/restore cả SQLite và thư mục ảnh khi ứng dụng đóng; backup workspace riêng để giữ project/output. Thay/xóa mục thư viện giữ version cũ, nên chưa tự thu hồi dung lượng. Không chép riêng bundle frontend vào binary desktop cũ vì bridge có contract mới. [Bản chạy, kiểm chứng và hướng dẫn](TRIEN_KHAI_UI_THU_VIEN_VIDEO_NGAN.md).

## Video ngắn phối trang phục — source 2026-09-10

Hai cờ `Generation:ShortVideoCharacterOutfit:Enabled` (server) và `Features:ShortVideoCharacterOutfitEnabled` (desktop) mặc định tắt. Trước bật môi trường mới, rehearsal `VideoFactory.4.1.9.ShortVideoCharacterOutfit.sql` hai lần trên clone có backup/restore đã thử, rồi áp least-privilege; deploy server/desktop cùng contract và xác minh rate/credential/budget cùng first-frame, quote, approval và API Fal/Veo thực tế. Riêng máy Development `DUNGDEV / VideoFactory` đã được người dùng yêu cầu áp migration và bật ngày 2026-09-10 sau backup/restore, rehearsal và kiểm schema/quyền. Override server nằm trong Development user-secrets, override desktop nằm trong `appsettings.user.json` của source và cạnh binary Debug/Release; không đổi mặc định phát hành. Đã khởi động lại server/desktop và smoke HTTPS, chưa gọi provider có phí hoặc nghiệm thu ảnh/video thật. Khi tắt cờ, giữ polling video/cleanup/reconciliation và dữ liệu lịch sử; không rollback binary không hiểu lineage để render project mới. [Các bước triển khai, đối soát Unknown và phần cần người vận hành xác nhận](TRIEN_KHAI_VIDEO_NGAN_NHAN_VAT_TRANG_PHUC.md).

### Veo local voice — bổ sung 2026-09-09

Rehearsal trên database clone được phê duyệt trước: áp dụng migration VideoFactory.4.1.8.LocalVoiceConsistency.sql idempotently sau baseline hiện hành, rồi triển khai server/desktop cùng contract. Mapping mới cần cột LocalVoicePolicyVersion kể cả khi feature tắt; không chạy binary mới lên schema cũ. Migration không phụ thuộc cloud LipSync thử nghiệm 4.1.6/4.1.7. Không xóa cột/policy/lineage khi rollback; desktop cũ không có render guard không được dùng để render project đã bật local policy.

Chỉ bật Features.VeoLocalVoiceConsistencyEnabled ở desktop nghiệm thu, khởi động lại, bật rõ trong project rồi cài runtime từ UI. Installer tải dependency có lock/hash và model ghim; cần mạng/dung lượng trống khi cài, không upload media. Inference chạy CPU và vẫn cần server authorization cho thao tác. Không phát hành profile GPU hoặc chứng nhận model chỉ dựa vào probe. Lưu license/provenance component, nghe thử tiếng Việt/âm nền/khẩu hình và rà soát quyền phân phối trước rollout; xem nhật ký triển khai.

Bổ sung 2026-09-10: cấu hình desktop trong repository đã bật flag theo yêu cầu triển khai. Máy này đặt `LocalVoice:ComponentRoot=D:\VideoMakerLocalVoice\v1`, `LocalVoice:TemporaryRoot=D:\VideoMakerLocalVoice\tmp` trong `appsettings.user.json` được Git bỏ qua; không đưa đường dẫn máy vào cấu hình phát hành. Có thể chuẩn bị component trước đăng nhập bằng `powershell -NoProfile -File scripts/Prepare-VeoLocalVoice.ps1 -Mode Prepare`; `-Mode Verify` kiểm byte và nạp lại model, `-Mode Status` chỉ đọc trạng thái. Ba lệnh đều chạy binary đã build và đọc cấu hình bên cạnh binary. Không ghi tay READY. Hướng dẫn máy đích và phần nghiệm thu còn lại: [HUONG_DAN_CHAY_DONG_NHAT_GIONG_VEO_LOCAL.md](HUONG_DAN_CHAY_DONG_NHAT_GIONG_VEO_LOCAL.md).

> Runbook chuẩn cho database, secret, provider, speech, SePay, desktop bundle và rollback. Rà soát chuỗi source tại commit `8f10cc9` ngày 2026-09-15.

Không chạy nội dung tài liệu này trên production nếu chưa xác định rõ instance/database, người phê duyệt, backup đã kiểm tra và phương án restore. Các giá trị trong dấu `<...>` là placeholder, không được commit secret thật.

## 1. Điều kiện trước mọi thay đổi môi trường

1. Xác nhận tên môi trường, SQL instance, database và application version đích.
2. Xác nhận người thực hiện có đúng quyền tối thiểu.
3. Tạo backup và thử khả năng đọc/restore trên môi trường phù hợp.
4. Chụp lại schema version, feature flag, provider policy và trạng thái worker.
5. Xác định cửa sổ triển khai, tiêu chí dừng và người quyết định rollback.
6. Không phát request provider có phí hoặc webhook thật trong bước rehearsal mặc định.

Server startup có bootstrap catalog và có thể ghi catalog vào database. Không khởi động binary mới trước khi chuỗi migration tương ứng đã hoàn tất.

## 2. Database

### 2.1 Thứ tự migration

Áp dụng theo đúng thứ tự:

```text
database/VideoFactory.Initial.sql
database/VideoFactory.4.0.0.OrganizationAiGateway.sql
database/VideoFactory.4.0.1.VietnameseSeedTextRepair.sql
database/VideoFactory.4.0.2.GptImageCharacterReference.sql
database/VideoFactory.4.0.3.SceneVoiceTts.sql
database/VideoFactory.4.0.4.BytePlusSeedance.sql
database/VideoFactory.4.0.5.SceneNativeAudioStatuses.sql
database/VideoFactory.4.0.6.NativeAudioWorkflowStatuses.sql
database/VideoFactory.4.0.7.ProjectAssetTextLibrary.sql
database/VideoFactory.4.0.8.AiGeneratedProjectAssets.sql
database/VideoFactory.4.0.9.FalVeoLongForm.sql
database/VideoFactory.4.0.10.LicenseSepayPayments.sql
database/VideoFactory.4.0.11.OrganizationSeatProvisioning.sql
database/VideoFactory.4.1.0.VietsubProjectRegistry.sql
database/VideoFactory.4.1.1.SceneFirstFrames.sql
database/VideoFactory.4.1.2.ProviderRequestFailureDetails.sql
database/VideoFactory.4.1.3.SpeechSynchronization.sql
database/VideoFactory.4.1.4.VoiceProfileApproval.sql
database/VideoFactory.4.1.5.SpeechVerificationReview.sql
database/VideoFactory.4.1.6.VietsubCloudTranslation.sql
database/VideoFactory.4.1.6.TikTokPublishing.sql
database/VideoFactory.4.1.7.TikTokAdminCredentials.sql
database/VideoFactory.4.1.8.TikTokMultiAccount.sql
database/VideoFactory.4.1.8.LocalVoiceConsistency.sql
database/VideoFactory.4.1.9.ShortVideoCharacterOutfit.sql
database/VideoFactory.4.1.10.SepayReceivingAccounts.sql
database/VideoFactory.4.1.12.SepayReceivingAccountsPermissions.sql
database/VideoFactory.4.1.13.SepayReceivingAccountsSimple.sql
```

Đây là thứ tự **dependency source cho binary hiện hành**, không phải xác nhận đã áp trên database nào. Các file 4.1.6/4.1.7 lip-sync Cloud còn trong repository để bảo toàn lịch sử nhưng module runtime đã loại bỏ; không tự chạy hoặc xóa chúng chỉ vì cùng tiền tố version. Những migration cùng số thuộc module khác nhau có mã `ai.SchemaVersions` riêng; kiểm **mã version cụ thể**, bảng, constraint, FK và quyền, không chỉ so số lớn nhất. Mỗi migration phải giữ tính idempotent theo thiết kế source. Không sửa lịch sử đã có khả năng được triển khai; tạo migration mới nếu cần đổi schema/data.

4.1.6 thay nullability của project budget/ledger và thêm tham chiếu Vietsub. Áp migration trước khi chạy binary server mới, kể cả Cloud còn disabled. Không rollback về worker cũ sau khi có reservation Vietsub. Quy trình cấu hình, giữ payload, đối soát Unknown và dừng rollout nằm trong [runbook Dịch Cloud](HUONG_DAN_VAN_HANH_DICH_CLOUD_VIETSUB.md).

4.1.9 tạo `vf.ShortVideoOutfits` và `vf.ShortVideoOperations`, ghi `4.1.9-short-video-outfit` trong `ai.SchemaVersions` và DENY CRUD trực tiếp cho `VideoMakerDesktopRole`. `TextOnly` lưu quote video trong `ShortVideoOperations`; cờ `CharacterOutfit=false` không cho phép bỏ migration. Khi kiểm môi trường đích, xác minh hai bảng/version/index/CHECK/FK/DENY và quote `TextOnly` hoạt động qua server API trước khi bật tạo video ngắn. Không chạy server mới chỉ để "kiểm thử" schema: startup bootstrap catalog có thể ghi database.

### 2.2 Chạy rehearsal

Riêng SePay, theo yêu cầu người dùng ngày 2026-10-11: chọn `VideoFactory.4.1.13.SepayReceivingAccountsSimple.sql` thay bước khởi tạo 4.1.10/4.1.12. File chỉ tạo hai bảng/ràng buộc, seed singleton tắt thanh toán khi chưa có và ghi version `4.1.10-sepay-receiving-accounts` cùng `4.1.13-sepay-receivers-simple`. Không có GRANT/DENY/REVOKE, không đọc metadata role hoặc kiểm tra CONTROL, không sửa quyền hiện có; rerun giữ nguyên tài khoản, Enabled, selection và revision. Cần schema nền thanh toán, quyền DDL/REFERENCES trong `auth`, đọc/ghi cấu hình và SELECT/INSERT `ai.SchemaVersions`. Không cần chạy file cũ hoặc gói `Apply-SePay-Permissions-Fix.sql` để khởi tạo bảng theo nhánh này.

Nhánh thiết lập bảo vệ 4.1.12 vẫn được giữ nguyên cho môi trường chọn quản lý quyền riêng: `VideoFactory.4.1.12.SepayReceivingAccountsPermissions.sql` thay bước khởi tạo 4.1.10, chỉ tái sử dụng DENY schema `auth` khi đọc được đủ SELECT/INSERT/UPDATE/DELETE cho `VideoMakerDesktopRole`, hoặc dùng DENY object đã có; nếu cần đặt DENY mới, principal phải đủ quyền. Đây không phải bước bắt buộc của nhánh đơn giản 4.1.13.

Khi chọn nhánh 4.1.12, DBA có thể chuẩn bị bằng `Prepare.SepayReceivingAccountsPermissions.sql` sau khi điền và xác minh database/user: DENY CRUD schema `auth` cho desktop role và chỉ GRANT VIEW DEFINITION trên role/schema cho migration user. Không thêm user vào db_owner, không impersonate, không cấp quyền desktop vào auth. SQL 2019 có thể ẩn metadata role khỏi db_ddladmin; 0 dòng trong sys.database_permissions không chứng minh chưa có DENY. Rehearsal cả nhánh đơn giản và nhánh có kiểm tra bảo vệ trên private LocalDB bằng `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-SepayReceiverMigration.ps1`; script không nhận connection string/target ngoài và dùng dữ liệu giả.

Ưu tiên bản sao database đã khử dữ liệu nhạy cảm. Ví dụ cho Windows authentication:

```powershell
sqlcmd -S "<sql-instance>" -d "<database-clone>" -E -b -f 65001 -i "database\VideoFactory.Initial.sql"
```

Sau đó chạy từng file migration còn lại theo thứ tự mục 2.1, vẫn dùng `-b -f 65001`. Với SQL authentication, đưa credential qua secret mechanism của CI/runner; không ghi password vào script, lịch sử shell hoặc tài liệu.

Sau migration:

- chạy lại migration để xác minh idempotency nếu runbook môi trường cho phép;
- chạy query/verify tương ứng, gồm `Verify.VideoFactory.4.0.11.OrganizationSeatProvisioning.sql` khi kiểm tra seat provisioning;
- build/test against clone;
- xác minh rollback từ backup trên rehearsal trước production.

### 2.3 Quyền desktop

Chạy `VideoFactory.DesktopLeastPrivilege.sql` bằng tài khoản quản trị phù hợp và map principal desktop theo hướng dẫn trong script. Desktop chỉ cần quyền workflow chuyển tiếp ở `vf`; không cấp trực tiếp quyền credential, usage ledger, `auth`, `ai`, `dbo` hay `vs`.

Khi workflow đã chuyển hết qua API, thu hồi role SQL desktop là một hạng mục migration/rollout riêng, không xóa connection tùy tiện trước khi đo đường gọi thực tế.

## 3. Secret và cấu hình server

### Development

```powershell
dotnet user-secrets set --project TOOL-SERVER "ConnectionStrings:VideoFactory" "<connection-string>"
dotnet user-secrets set --project TOOL-SERVER "Jwt:SigningKey" "<random-secret-at-least-32-bytes>"
```

SMTP password/App Password, admin bootstrap identity, SePay secret, signing key, production connection string và provider credential không được commit. Production dùng secret manager của môi trường và HTTPS certificate hợp lệ.

TikTok Client Key/Client Secret mới được nhập qua mục **Tích hợp TikTok** bởi Global Admin sau migration 4.1.7. Cấu hình `TikTok:ClientKey`/`TikTok:ClientSecret` chỉ còn là fallback legacy và không dùng cho rollout mới.

Data Protection key ring nằm trong database. Backup/restore phải giữ được key ring cùng encrypted credential; thử giải mã credential bằng health/admin flow sau restore mà không in secret.

## 4. Thiết lập AI Gateway theo tổ chức

Thực hiện theo thứ tự để không tạo outbound thiếu kiểm soát:

1. Global Admin xác minh provider/model catalog; giữ model mới `Disabled`.
2. Tạo rate có đơn vị, currency, hiệu lực và model mapping chính xác.
3. Owner/OrganizationAdmin nhập credential qua HTTPS Admin/API được phép; server test rồi mã hóa.
4. Cấu hình organization policy/provider/model.
5. Cấu hình organization budget và member limit; nhớ rằng `0` là khóa AI.
6. Tạo project thử nghiệm với user/role riêng.
7. Kiểm tra dry path/auth/pricing trước, sau đó mới cho phép một smoke có phí đã phê duyệt.

Không tự đoán giá từ website hoặc model name. Thiếu rate phải để hệ thống trả `pricing_not_configured`.

## 5. Rotation credential

1. Kiểm tra task đang `Queued/Submitted/Running` và credential version chúng đang dùng.
2. Nhập key mới qua Admin/API; server phải test thành công trước ghi.
3. Chuyển version mới thành `Active`, version cũ sang `Retiring`.
4. Theo dõi task cũ đến terminal; chúng tiếp tục dùng version đã snapshot.
5. Chỉ chuyển version cũ sang `Revoked` khi không còn task cần dùng và rollback window đã hết.
6. Xác minh log/response chỉ có hint, không có plaintext/encrypted payload.

Không rotate production chỉ để test giao diện.

## 6. Rollout provider

### Kling

- Xác minh catalog/model/resolution/Native Audio và rate.
- Smoke một project riêng; kiểm tra submit, polling sau khi đóng desktop, cache/proxy, audio review, approve và settlement.
- Test `NativeAudioInvalid`/retry bằng fixture hoặc môi trường được phép; retry thật là chi phí mới.

### BytePlus

- Giữ seed `Disabled` đến khi rate, credential, request/response mapping và output host thực tế đã được duyệt.
- Rollout theo tổ chức thử nghiệm, không bật toàn cục và không failover từ Kling.

### Fal/Veo

- Cần migration 4.1.1 cho first frame và 4.1.9 cho quote video ngắn, policy `LongForm`, model Veo 3.1 Standard/Fast, rate/credential/budget Active theo tổ chức.
- Xác minh first frame Approved/current, tỷ lệ đúng và không có T2V fallback.
- Với `DirectShortVideo`, kiểm riêng `TextOnly` và `CharacterOutfit`: quote ảnh/video, xác nhận chi phí từng bước, lineage composition/revision, chuyển project Kling cũ có xác nhận, status/cache/reconnect và render/export. Không dùng Kling để fallback khi Veo chưa sẵn sàng.
- Bật catalog theo môi trường/tổ chức sau smoke image-to-video có phí được phê duyệt.

Mỗi provider có rollback flag/policy riêng. Tắt provider chặn request mới nhưng worker vẫn cần xử lý an toàn task đã gửi.

## 7. Rollout Canonical Voice và speech verification

Từ thay đổi loại bỏ lip-sync Cloud ngày 2026-09-10, triển khai server và desktop cùng phiên bản: server cũ vẫn chặn Canonical thoại nhân vật. Không cần migration dữ liệu để tiếp tục project chờ cũ; dashboard đối chiếu WAV/voice version hiện hành, sau đó thao tác tạo/duyệt ghi trạng thái theo workflow thông thường. Giữ nguyên migration 4.1.6/4.1.7 lip-sync và dữ liệu lịch sử; chúng không còn là bước cài bắt buộc của hai luồng hiện hành. Không DROP bảng hoặc xóa ledger/request cũ. Bản server/desktop đang chạy không tự được thay bằng kết quả build kiểm thử.

Điều kiện Canonical Voice:

- Migration 4.1.3–4.1.4 đã chạy lặp trên clone và áp đúng môi trường.
- OpenAI TTS credential/model/rate Active, budget giới hạn và storage/retention đủ dung lượng.
- Catalog server trả đúng 13 giọng hiện hành; alias legacy chỉ dùng để đọc project cũ.
- Voice preview/approval, content pacing, scene WAV, technical validation, audio mix/render và export đã smoke test.
- Server `CanonicalVoiceEnabled` và desktop `SpeechSynchronizationEnabled` được bật theo thứ tự canary; rollback bằng cách tắt flag đã được thử.

Smoke bắt buộc:

1. Mở/chọn modal không tạo request hoặc reservation.
2. Preview khi có project dùng đúng project; khi chưa có project dùng đúng một project kỹ thuật ẩn theo user+organization và không xuất hiện trong list/dashboard.
3. Quote/xác nhận xảy ra trước TTS outbound; phát lại WAV đã tải không tạo request mới.
4. Content plan đạt mục tiêu nhịp 85–95%; output ngoài biên 80–105% chỉ mở repair có quote, không tự gọi lần hai.
5. WAV qua MIME/SHA-256/sample rate/duration/audibility và đúng speech/voice snapshot.
6. `NativeVoiceOver` và `OnCameraDialogue` ghép toàn bộ WAV vào video nền; thoại nhân vật cần duyệt WAV trước. Nghe/duyệt clip đã ghép trước render. Không có module lip-sync Cloud.
7. Video dài `ProviderNativeVerified` không hiển thị/quote/gọi ASR và duyệt bằng audio hợp lệ cùng checklist nghe.

Speech verification là rollout độc lập cho workflow không phải `OpenAiStructuredPlan`: cần migration 4.1.5, transcription credential/model/rate và `SpeechVerificationEnabled`. `NeedsReview` phải có lý do/reviewer/timestamp; stale row version và `Failed` bị chặn.

## 7A. Rollout TikTok Direct Post

Với nhiều tài khoản, thực hiện [quy trình migration, bật cờ và rollback](TRIEN_KHAI_TIKTOK_NHIEU_TAI_KHOAN.md). Cấu hình workspace đã bật `TikTok:MultiAccountEnabled=true` theo yêu cầu người dùng sau khi áp migration vào `DUNGDEV / VideoFactory`. Khi triển khai sang môi trường khác, giữ cờ tắt cho đến khi migration, server và desktop cùng tương thích. Migration 4.1.8 vẫn bắt buộc cho server mới khi cờ tắt. Không hạ về server một tài khoản sau khi có nhiều connection. Tắt cờ chỉ ngăn mở rộng tài khoản, giữ vận hành các kết nối hiện có.

Item TikTok mặc định hiển thị trên desktop nhưng thao tác phía server vẫn tắt. Trước khi bật integration:

- apply/rehearsal tuần tự migration TikTok 4.1.6, 4.1.7 rồi 4.1.8, kiểm tra Data Protection key ring nằm trong backup/restore plan;
- đăng ký đúng loopback redirect URI cho Login Kit Desktop, có scope `video.publish`, hoàn tất app review/audit theo yêu cầu TikTok;
- Global Admin mở **Tích hợp TikTok**, nhập credential đã regenerate, bấm yêu cầu xác minh, rồi trong 15 phút đăng nhập đúng tài khoản Admin đó trên Desktop và hoàn tất **Kết nối TikTok**;
- dùng tài khoản test để smoke connect/reconnect/disconnect, creator-info, privacy/interaction/disclosure, chunk upload, retry, restart desktop và terminal status;
- xác nhận log/API/React không lộ token, app secret, signed upload URL hoặc absolute local path;
- sau OAuth thành công, credential chuyển `Pending -> Active` và integration được bật nhưng public posting vẫn ở `SELF_ONLY`;
- chỉ bật xác nhận public posting trong Admin sau khi có bằng chứng TikTok audit còn hiệu lực; dùng `TikTok:EmergencyDisabled=true` nếu cần dừng khẩn cấp.

Không chạy smoke đăng bài vào tài khoản thật hoặc bật public posting nếu chưa có phê duyệt môi trường và TikTok audit tương ứng.

## 8. Rollout SePay

SePay mặc định tắt trong SQL. Binary hiện hành cần migration `4.1.10-sepay-receiving-accounts` trước startup. Migration tạo cấu hình tắt, không chép thông tin ngân hàng từ file hoặc user-secrets. Các giá trị receiver/Enabled legacy trong `Payments:Sepay` không điều khiển runtime sau khi dùng cấu hình SQL; QR URL, transfer prefix và expiry vẫn là cấu hình server. [Phạm vi triển khai, kiểm thử và rollout](TRIEN_KHAI_ADMIN_SEPAY.md). Trước khi bật:

- xác minh migration, singleton, unique bank/account, FK/CHECK và DENY trực tiếp hai bảng SePay cho desktop role;
- Global Admin thêm bank code, account number/name và chọn tài khoản nhận tiền trong **Thanh toán SePay**; xác minh transfer prefix, QR endpoint và expiry trong cấu hình server;
- tài khoản được chọn phải đã liên kết SePay; cấu hình webhook tiền vào, JSON, HTTPS và bộ lọc prefix tương ứng. Luồng sản phẩm hiện hành không dùng key/chữ ký webhook theo yêu cầu, đối chiếu account/code/exact amount của snapshot đơn;
- chạy simulator local/staging tại `scripts/Test-SepayOrganizationProvisioning.ps1` với token test qua environment variable;
- test duplicate/concurrent webhook, sai amount/content, expired/late order, seat shortage và retry provisioning;
- xác minh ledger/payment/order có thể đối soát mà không log dữ liệu nhạy cảm;
- chỉ bật **Nhận thanh toán** trong Admin sau sign-off; tắt chỉ chặn đơn mới, không dừng đối soát webhook của đơn cũ.

Ví dụ test chỉ dành cho endpoint test đã xác nhận:

```powershell
.\scripts\Test-SepayOrganizationProvisioning.ps1 `
  -BaseUrl "https://localhost:7202/" `
  -LicensePlanId "<test-plan-guid>" `
  -Confirmation SEPAY_TEST_ONLY
```

Script không được hướng đến remote nếu chưa chủ động cho phép và xác minh đó là staging an toàn.

## 9. Desktop và Vietsub runtime

- Desktop trỏ tới HTTPS server URL đúng môi trường.
- SQL workflow dùng account/role ít quyền; không đóng gói production credential trong `appsettings.json`.
- FFmpeg/FFprobe phải nằm ở `tools/ffmpeg`, đúng win-x64, provenance và checksum.
- OCR/model/native runtime phải đúng bundle/fingerprint.
- OCR desktop cần bốn DLL Visual C++ x64 cạnh `TOOL-LOCAL.exe`, gồm `VCOMP140.dll` mà MKL dùng. Chuẩn bị từ installer Microsoft được ghim trong `third_party/ocr/MSVC_RUNTIME.json` bằng `scripts/Prepare-OcrNativeRuntime.ps1 -InstallerPath <file local đã kiểm checksum>`. Build và publish kiểm hash bắt buộc; có thể truyền `OcrNativeRuntimeDirectory` cho MSBuild hoặc `-OcrNativeRuntimePath` cho script release. Không lấy DLL từ System32/máy dev hoặc chép runtime Piper sang desktop. Giữ notice Microsoft cùng gói.
- Khi kiểm ZIP OCR, xác minh thêm `OcrNativeRuntimeModules` của lệnh `--check-bundled-components`: bốn DLL đều có `AppLocal=true`. Kiểm file thiếu/hỏng trên bản giải nén thử, kể cả máy kiểm đã cài Visual C++; không thử bằng cách xóa hoặc thay DLL Windows. Windows N/Server thiếu Media Foundation cần chuẩn bị thành phần Windows phù hợp; không phân phối DLL hệ thống thay thế.
- Profile Low-memory 6 GB chỉ được bật trong bundle đã benchmark; không hạ thêm ngưỡng qua WebView, manifest project hoặc cấu hình người dùng.
- Giữ `VietsubLocalTranslationEnabled=false` đến khi verify model, benchmark và desktop smoke đạt trên chính bundle định phát hành.
- `VietsubLocalVoiceEnabled=true` chỉ mở UI/cài đặt. Máy thiếu runtime/model phải trả `NOT_INSTALLED`; chỉ rollout sau verify checksum/probe, kiểm kê license/dependency Python, benchmark CPU, nghe nghiệm thu và smoke timeline/playback.
- Không dùng marker `READY` từ máy/build khác.

## 10. Bundle FFmpeg và phát hành desktop

Bundle bắt buộc có `ffmpeg.exe`, `ffprobe.exe`, `LICENSE.txt`, `PROVENANCE.md` và `checksums.sha256`. Trước release:

```powershell
.\scripts\Test-FfmpegBundle.ps1 `
  -BundlePath ".\third_party\ffmpeg\win-x64" `
  -RequireReleaseApproval
```

Publish tạo artifact mới và từ chối release directory đã có file:

```powershell
.\scripts\Publish-DesktopRelease.ps1 `
  -Version "<semver>" `
  -BuildNumber <integer> `
  -Channel Stable `
  -ServerBaseUrl "https://<server>/"
```

Nếu dùng appsettings đóng gói riêng, truyền `-AppSettingsPath` đến file đã rà soát không chứa secret. `artifacts` là đầu ra tái tạo, không phải source of truth.

`Update.Channel` trong cấu hình desktop phải khớp `-Channel`; `Update.Platform` phải là `win-x64`. Script giải nén ZIP vừa tạo, kiểm đủ thành phần và chạy fixture OCR Anh/Trung, FFmpeg cùng kiểm WebView2. Probe không đạt thì chưa bàn giao ZIP. Có thể chạy lại trên thư mục đã giải nén:

```powershell
.\scripts\Test-DesktopSetupPublish.ps1 -PublishRoot 'D:\ThuMucDaGiaiNen' -ProbeWebView2 -ProbeBundledComponents -ProbePiperOffline
```

Máy mới tự chuẩn bị Piper từ payload offline đã xác minh sau bước kiểm tra startup; vẫn có **Cài giọng Việt** để thử lại. ZIP phải chứa `components/piper/piper-1.6.0-python-3.11.15-offline-v3/{manifest.json,piper-offline.zip}`. Không đưa marker READY hoặc venv của máy build vào ZIP. Kiểm cả đường dẫn có dấu/khoảng trắng. Để repair toàn bộ ứng dụng qua server hoạt động, server phải có package Active, đã tới lịch phát hành, khớp version/build/channel/platform; gửi ZIP riêng cho khách không tự tạo release trên server. [Bản sửa Setup ZIP và cách lấy báo cáo chẩn đoán](TRIEN_KHAI_SUA_LOI_SETUP_BAN_ZIP.md).

### Kèm model Qwen offline

Thêm `-IncludeQwenModel` vào lệnh publish để kèm Qwen3 4B CPU đã pin; `-QwenModelPath` cho phép chọn file nguồn đã kiểm đúng size/SHA-256. Script thêm GGUF trước manifest/inventory, nén và giải nén theo stream, rồi probe runtime/Anh/Trung bằng worker của ZIP. Bản cấu hình bật dịch local phải kèm model. Không thay mặc định tắt trong source hoặc mang marker READY từ máy build.

`-DeliveryMode Updater` là mặc định và từ chối ZIP vượt giới hạn artifact server mặc định 2 GiB. Dùng `-DeliveryMode Direct` cho gói gửi file riêng; script vẫn kiểm tổng giải nén tối đa 4 GiB/200.000 entry và ghi rõ khả năng upload ở giới hạn mặc định. Cả hai cách đều giữ kiểm cấu hình khách và gate FFmpeg Release; không tự sửa giới hạn server/updater hoặc upload release.

Kiểm lại Qwen từ thư mục giải nén:

```powershell
.\scripts\Test-QwenOfflineBundle.ps1 -PublishRoot 'D:\ThuMucDaGiaiNen'
```

Lệnh chỉ dùng model và worker của bộ đó, chặn HTTP, dùng component workspace tạm mới và dọn worker/workspace sau probe. Báo code/stage/error type an toàn khi lỗi; chưa chứng minh Startup có auth/license/organization hoặc chất lượng video trên máy khách. Nếu có cảnh báo tài nguyên, chỉ thêm `-AcceptResourceWarning` sau khi đánh giá máy thử; xác nhận không bỏ checksum, platform, disk, probe hoặc OOM thật.

Khi chưa có cấu hình khách hoặc hồ sơ FFmpeg Release, `New-DesktopOfflineCandidate.ps1 -Version <semver> -BuildNumber <integer>` tạo ZIP chẩn đoán riêng với `appsettings.json` rỗng. ZIP và README ghi Development candidate; dùng để nghiệm thu component, chưa dùng gửi khách hay Active release. Kế hoạch và điều kiện bàn giao: [đóng gói TOOL-LOCAL kèm model](PLAN_TASK_DONG_GOI_TOOL_LOCAL_KEM_MODEL_OFFLINE.md).

### Chuẩn bị payload Piper trước publish

Chỉ máy build cần Python và mạng để lấy input. Script kiểm các hash đã pin, cài thử bằng wheel local và tạo WAV trước khi xuất bundle:

```powershell
.\scripts\Prepare-PiperOfflineBundle.ps1 -BuilderPython 'C:\BuildTools\Python311\python.exe' `
  -InputDirectory 'D:\PiperBuildInputs' -OutputDirectory 'D:\PiperBuildCandidate' -Download
```

Rà soát `sources.json`, license, kết quả proof và `approved-definition.json`; định nghĩa đã duyệt trong source phải khớp candidate. Khi đổi pin/worker/payload đã phát hành, tăng bundle version và cập nhật định nghĩa trong cùng thay đổi. Chỉ copy `manifest.json` và `piper-offline.zip` vào `artifacts/piper-offline/<bundle-version>` hoặc truyền `-PiperBundlePath` cho `Publish-DesktopRelease.ps1`. Không copy thư mục `proof`, cache hoặc venv. Script publish bắt buộc kiểm hash và cài thử từ ZIP vừa giải nén; thiếu/hỏng gói phải dừng.

Kiểm runtime offline độc lập, không đọc cấu hình triển khai hoặc đăng nhập/SQL:

```powershell
.\scripts\Test-PiperOfflineBundle.ps1 -PublishRoot 'D:\ThuMucDaGiaiNen' -Workspace 'D:\PiperProbeMoi'
.\scripts\Test-PiperOfflineBundle.ps1 -PublishRoot 'D:\ThuMucDaGiaiNen' -Workspace 'D:\PiperProbeMoi' -VerifyOnly
```

Workspace đầu tiên phải rỗng. Script dùng PATH tối thiểu và proxy không kết nối được trong tiến trình con; không thay DNS/proxy của máy. Cờ offline của uv chặn tải dependency. Các lệnh `--check-desktop` và `--check-bundled-components` vẫn không cài model. Lưu báo cáo rồi dọn đúng thư mục probe do mình tạo. Cần Windows sạch và nghiệm thu nghe/xuất video trước phát hành cho khách; login, license và Cloud vẫn cần kết nối theo kiến trúc hiện hành.

### Xử lý lỗi Piper trong ZIP

| Mã lỗi | Hướng xử lý |
|---|---|
| `VOICE_OFFLINE_BUNDLE_MISSING` | Cấp lại bản ZIP đầy đủ đúng phiên bản có cả manifest và payload Piper. |
| `VOICE_OFFLINE_BUNDLE_INVALID` | Kiểm hash bằng `-ArtifactOnly`; thay nguyên bộ ZIP đúng phiên bản nếu file thiếu/hỏng hoặc worker không khớp. Không sửa hash để chấp nhận file đang lỗi. |
| `VOICE_RUNTIME_INVALID` | Kiểm tra/cài lại giọng Việt từ payload local trong Setup; không copy marker READY từ máy khác. |
| `VOICE_RUNTIME_BUSY` | Đợi thao tác Piper ở cửa sổ khác kết thúc rồi thử lại. Không xóa khóa hoặc runtime khi còn job sử dụng. |
| `VOICE_RUNTIME_INSTALL_FAILED` | Kiểm dung lượng trên ổ chứa component, quyền ghi và tính đầy đủ của ZIP, rồi thử lại. Bundle hiện yêu cầu tối thiểu 727.344.098 byte trống trước khi cài. |
| `VOICE_RUNTIME_UNSUPPORTED` | Dùng Windows x64 theo nền tảng của gói. |

Sửa riêng runtime Piper không cần package sửa ứng dụng trên server. Nếu payload trong chính ZIP thiếu/hỏng, người vận hành phải cấp lại ZIP hoặc dùng kênh cập nhật/sửa ứng dụng đã cấu hình. Bằng chứng và các môi trường còn cần nghiệm thu được ghi tại [báo cáo Piper offline](TRIEN_KHAI_PIPER_OFFLINE_TRONG_ZIP.md).

## 11. Smoke sau triển khai

- Login, refresh/revoke session, device/license lease.
- Chọn organization và xác minh matrix role, đặc biệt `Viewer`.
- Pricing missing, budget `0`, insufficient budget và idempotency conflict.
- Một request content; provider video chỉ khi được phép phát sinh phí.
- Canonical Voice/speech verification chỉ khi nằm trong scope rollout và đã phê duyệt chi phí.
- Poll khi desktop reconnect, proxy download, MIME/size/hash và approve/render.
- Admin credential hint/rotation không lộ secret.
- SePay chỉ nếu nằm trong scope rollout.
- TikTok chỉ nếu nằm trong scope rollout: OAuth, local direct upload, reconnect/poll và không lộ token/path/signed URL.
- Vietsub create/open/sync registry, OCR và translation chỉ nếu runtime đã được phê duyệt.
- Updater install/update/rollback trên máy sạch hoặc VM.
- Health/log/metrics không chứa token, prompt nhạy cảm hoặc signed URL.

## 12. Rollback

- Dừng request mới bằng feature flag/model/policy thay vì xóa dữ liệu đang chạy.
- Để worker settle/release task đã outbound; không xóa provider request hoặc usage ledger.
- Roll back application chỉ khi binary cũ tương thích schema mới; migration schema/data rollback cần kịch bản riêng đã rehearsal.
- Restore database chỉ theo quyết định sự cố, với đánh giá task/ledger phát sinh sau backup.
- Desktop updater dùng backup/rollback tích hợp và phải xác minh manifest/checksum trước phục hồi.
- Ghi lại timeline, version, migration, flag, task bị ảnh hưởng và quyết định tài chính.

## 13. Checklist production

- [ ] Backup và restore rehearsal đạt.
- [ ] Migration clone, idempotency và verify đạt.
- [ ] Secret/certificate/Data Protection được kiểm tra.
- [ ] Rate, credential, policy, budget và role đúng.
- [ ] Canonical Voice/speech/local model chỉ bật khi checklist riêng đạt; video dài Provider Native không gọi ASR.
- [ ] Build/test/smoke theo [KIEM_THU_VA_NGHIEM_THU.md](KIEM_THU_VA_NGHIEM_THU.md) đạt.
- [ ] FFmpeg provenance/checksum có `Approval scope: Release`.
- [ ] Monitoring/alert và rollback owner sẵn sàng.
- [ ] Chỉ các provider/payment/translation feature đã được sign-off mới bật.
- [ ] Kết quả rollout được cập nhật vào [BOI_CANH_HE_THONG_HIEN_HANH.md](BOI_CANH_HE_THONG_HIEN_HANH.md) bằng bằng chứng mới.

## Triển khai wizard Admin — cập nhật 2026-10-14

Theo yêu cầu ngày 2026-10-14, chọn [migration tạo bảng đơn giản 4.1.15](database/VideoFactory.4.1.15.AdminSetupWizardSimple.sql) thay bước khởi tạo 4.1.14 trên database đã xác định và được cho phép, sau backup/restore rehearsal; sau đó triển khai binary và toàn bộ asset Admin cùng phiên bản. File chỉ tạo hai bảng, khóa/ràng buộc/index và ghi version nền 4.1.14 cùng version nhánh đơn giản; không GRANT/DENY/REVOKE hoặc kiểm tra role/CONTROL. Không chạy tiếp file 4.1.14 hoặc wrapper DBA cũ trong nhánh này. Wizard dùng marker `admin-only-new-20261014.1`; kiểm source đang phục vụ bằng script read-only `scripts/Test-AdminRuntimeAssets.ps1` trên loopback. Trạng thái database/runtime đích cần tra biên bản mới nhất. Rollback giữ draft/receipt, rate/credential version và dữ liệu khách; không xóa bảng hoặc tắt license chỉ để ngừng bán. Xem [biên bản triển khai](TRIEN_KHAI_ADMIN_SETUP_3_BUOC.md).
