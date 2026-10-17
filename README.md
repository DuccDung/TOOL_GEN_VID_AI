# VideoMaker

## Phát hành hiện hành: hosting lưu link, Setup cài từ VPS

Source đã chuyển sang `StandaloneCatalog` (catalog 5), dùng Setup **1.3.0 trở lên**. Admin lưu một link EXE cho nút tải website; tạo release bằng phiên bản và link ZIP rồi phát hành, không cần upload hoặc kích hoạt Setup trên hosting. Setup tải/kiểm ZIP ứng dụng và model trên máy cài; hash, probe, transaction và gate READY được giữ. Một Setup dùng cho nhiều ZIP tương thích. SQL **4.1.21 đã áp và hậu kiểm trên hosting** theo [biên bản SQL](TRIEN_KHAI_SQL_4_1_21_HOSTING.md). Người dùng đã xác nhận luồng tải Setup hoạt động; xác nhận này không thay nghiệm thu toàn bộ model trên Windows sạch. [Nghiệp vụ và thao tác Admin/Setup](NGHIEP_VU_SETUP_VA_PHAT_HANH_DESKTOP.md), [đóng gói và bằng chứng kỹ thuật](TRIEN_KHAI_PHAT_HANH_DON_GIAN_VPS.md).

## Lịch sử triển khai

Các mô tả DirectCatalog/Setup Active bên dưới là phạm vi các lượt trước, không phải điều kiện của StandaloneCatalog hiện hành.

Nút **Tải cho Windows** có cấu hình link EXE riêng tại **Admin → Phát hành desktop → Link tải Setup cho Windows**. Hosting lưu URL trong `auth.ServerSettings` và chuyển hướng `302` để trình duyệt tải trực tiếp từ VPS; không nhận/lưu EXE cho luồng này. Cấu hình link không thay điều kiện Setup Active khi phát hành release. [Source, kiểm chứng và hướng dẫn](TRIEN_KHAI_LINK_TAI_SETUP_TREN_VPS.md).

Luồng hiện hành là **DirectCatalog**: Admin nhập **phiên bản + link ZIP trên VPS**, lưu nháp hoặc phát hành; hosting lưu catalog, không tải ZIP hoặc tạo job kiểm ZIP. `TOOL-LOCAL → Publish → FolderProfile` tự tạo ZIP có checksum trong tên `VideoMaker-<version>-<build>-win-x64-<SHA256>.zip`. Giữ nguyên tên khi upload. Setup dùng chung **1.2.0 trở lên**, capability `3,4`, lấy catalog từ server rồi tải và kiểm ZIP trực tiếp trên máy cài, chuẩn bị/probe model và commit transaction. Không cần nhập checksum hoặc tạo ba JSON phát hành thủ công. [Bằng chứng và runbook hiện hành](TRIEN_KHAI_PHAT_HANH_TRUC_TIEP_TU_VPS.md), [hướng dẫn Publish](scripts/VisualStudioPublish.README.vi.md).

FFmpeg 9.0.1 vẫn tự chuẩn bị offline theo pin; bản Publish phải đầy đủ runtime/worker/license và cấu hình phát hành hợp lệ. Setup vẫn cài, kiểm model và chỉ mở khóa sau transaction commit; chạy EXE trực tiếp không thay bước Setup. Scope FFmpeg `Release/TechnicalPackaging` chưa xác nhận corresponding source/GPL hoặc production; [hồ sơ FFmpeg](third_party/ffmpeg/RELEASE_REVIEW.md) giữ các qualification chưa đạt. Source hiện tại chưa đồng nghĩa server/Setup mới đã rollout hoặc đã cài đạt trên Windows sạch.

Source `DesktopReleases` đã chọn `PublicationMode=DirectCatalog`, `SharedBootstrapperEnabled=true`, API `https://taphoatool.com/`, profile `Production`, nguồn `https://103.179.190.182:8443/models/`. Sau lỗi quyền của bản 4.1.19, theo yêu cầu người dùng đã áp exact [SQL **4.1.20 Simple**](database/VideoFactory.4.1.20.DirectDesktopCatalogSimple.sql) lên `MB2D70\MSSQLSERVER2019 / tap51004_tool` ngày 2026-10-17; kết nối mới xác minh đủ schema/marker và giữ dữ liệu, quyền nhìn thấy. Script không thao tác phân quyền; giữ nguyên migration cũ. Cần deploy server mới và đăng ký Setup Active trước khi phát hành; lượt SQL này không xác nhận hai bước đó hoặc nghiệm thu model. Catalog dùng protocol 4; binary ứng dụng/model và Setup vẫn dùng distribution protocol 3. Protocol 2/3 cũ được giữ tương thích. [Kết quả SQL Simple và giới hạn backup/restore](TRIEN_KHAI_SQL_4_1_20_DON_GIAN.md); các biên bản [kiểm ZIP trên hosting](TRIEN_KHAI_ZIP_PUBLISH_DON_GIAN.md) và [form cũ](TRIEN_KHAI_FORM_RELEASE_TOI_GIAN_VA_SETUP_DUNG_CHUNG.md) là bằng chứng lịch sử.

Phát hành ứng dụng qua VPS đã có source cho ZIP external, JSON đăng ký Admin, Setup protocol 2 và update/repair bằng đúng Setup từng release. Cờ external mặc định tắt; chưa có candidate khách hoặc rollout. Xem [biên bản, kiểm thử và runbook](TRIEN_KHAI_PHAT_HANH_TOOL_LOCAL_QUA_VPS_VA_SETUP.md).

Kế hoạch nền cho release bằng URL ZIP ứng dụng trên VPS và Setup tải trực tiếp: [32 task, ma trận nghiệm thu, thứ tự triển khai và điều kiện bàn giao](PLAN_TASK_PHAT_HANH_TOOL_LOCAL_QUA_VPS_VA_SETUP.md). Trạng thái source triển khai và giới hạn nghiệm thu được cập nhật ở các biên bản phía trên.

Đã bổ sung công cụ nghiệm thu runtime StaticArchive đã cài, sửa kiểm kê nguồn/cache, preflight dung lượng QA và dependency Pkcs; thêm full App/native bridge cùng bài parent chết. [Biên bản khắc phục và giới hạn nghiệm thu](TRIEN_KHAI_KHAC_PHUC_VA_NGHIEM_THU_SETUP_MODEL.md) ghi riêng bằng chứng source/tự động/HTTP và phần Setup khách, Windows sạch, model thật còn thiếu. Chưa phát hành hoặc đổi VPS.

Đã bổ sung regression và sửa lỗi ổn định Setup/model: bố cục WinForms, snapshot/ACK/repair React, receipt gắn giao dịch ứng dụng, metadata đường dài và phục hồi temp sau crash. [Kết quả kiểm thử, ảnh native/WebView2 và các bài còn thiếu](TRIEN_KHAI_TEST_ON_DINH_SETUP_MODEL_UI_UX_VA_LOGIC.md) ghi riêng phạm vi tự động và nghiệm thu Windows sạch/model thật; chưa thay trạng thái phát hành production.

Setup có chế độ **StaticArchive** để tải ZIP model từ VPS HTTPS:8443 đã pin, cài/probe Qwen/Piper cùng OCR/media/WebView2 trước khi báo hoàn tất. Có resume, journal/backup và gate cài dở; giữ các mode Bundled/Server. [Biên bản triển khai, kiểm thử, cách publish và giới hạn](TRIEN_KHAI_SETUP_CAI_MODEL_TU_VPS.md). Chưa nghiệm thu Setup trọn bộ trên Windows sạch hoặc phát hành production trong lượt này.

Đã đóng gói server IIS x64 kèm Qwen 4B, Piper offline và bộ đầu vào OCR thành ZIP khoảng 2,91 GB, giải nén kiểm checksum đạt. [Biên bản đóng gói và đường dẫn bàn giao](TRIEN_KHAI_DONG_GOI_SERVER_KEM_MODEL.md) ghi hash, cấu trúc và cách triển khai; chưa upload hoặc chạy server, kho model vẫn tắt và chưa có catalog ký.

Kho model Qwen 4B/Piper và bộ đầu vào OCR đã có công cụ gom bàn giao, catalog ký, API chỉ đọc, tải tiếp/kiểm hash và mode đóng gói Server/Bundled. Mặc định chưa bật tải server và chưa có khóa production. Xem [triển khai, bằng chứng và các gate còn lại](TRIEN_KHAI_KHO_MODEL_SERVER_VA_CAI_DAT_AN_TOAN.md) cùng [thiết kế tin cậy](ADR_MODEL_DISTRIBUTION_TRUST.md); chưa upload hoặc rollout môi trường thật.

Bài thử cài mới từ server đã bổ sung retry theo `Retry-After`, pipeline Authenticode và công cụ kiểm kê chỉ đọc. [Biên bản triển khai bài thử](TRIEN_KHAI_TEST_SETUP_MODEL_TU_SERVER.md) ghi bằng chứng mới, giới hạn dung lượng và các đầu vào còn thiếu; chưa dọn model hoặc phát hành Setup lên server đích.

Gói **Chỉ tool dịch / Vietsub**, tắt Cloud: admin nhập thông tin gói và mở bán khi SePay sẵn sàng. Không chọn nhóm nhận khách, không giữ chỗ hoặc thêm khách vào tổ chức. Thanh toán cấp license theo thời hạn/thiết bị; khách dùng không gian Local cá nhân và vẫn phải cài, probe runtime/model. Gói có Cloud và toàn bộ công cụ tiếp tục dùng tổ chức cùng kiểm tra credential/rate/budget hiện hành. SePay giữ trang riêng.

Desktop gói Local mở thẳng **Dịch phụ đề**. Các menu khác vẫn hiển thị với biểu tượng khóa, gồm Cài đặt và Tạo video mới; bấm chuột hoặc bàn phím không chuyển trang hay gửi lệnh công cụ. Thông tin gói, đăng xuất vẫn khả dụng; Setup nằm trong mục thu gọn **Thiết lập công cụ dịch** ngay tại trang Dịch. Tính năng Dịch đang tắt/chưa sẵn sàng hiển thị thông báo tại phần Dịch, không chuyển sang menu khác.

Source trên `main`, nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit. Registry cá nhân cần [migration 4.1.16](database/VideoFactory.4.1.16.VietsubPersonalProjects.sql), triển khai đồng bộ server/Admin/desktop. Chưa áp SQL hoặc rollout hosting trong lượt sửa này. Xem [nghiệp vụ](NGHIEP_VU_HE_THONG_VIDEOMAKER.md), [vận hành](VAN_HANH_VA_PHAT_HANH.md) và [bối cảnh kiểm thử](BOI_CANH_HE_THONG_HIEN_HANH.md).

Admin thiết lập mở bán có luồng 3 bước, SePay ở trang riêng. Xem [source, kiểm thử và hướng dẫn triển khai](TRIEN_KHAI_ADMIN_SETUP_3_BUOC.md); chọn [migration tạo bảng đơn giản 4.1.15](database/VideoFactory.4.1.15.AdminSetupWizardSimple.sql) trước khi dùng wizard trên môi trường đích. File ghi version nền `4.1.14-admin-setup-wizard` cùng `4.1.15-admin-setup-wizard-simple`, không thay đổi quyền database.

> Điểm vào repository. Rà soát context theo source ngày 2026-09-15.

VideoMaker là hệ thống desktop/server hỗ trợ tạo video bằng AI và xử lý media cục bộ. OpenAI tạo nội dung có cấu trúc và giọng Canonical; Kling, BytePlus hoặc Fal/Veo tạo clip theo policy của tổ chức; desktop tải output qua server, yêu cầu người dùng duyệt và dựng video bằng FFmpeg. Module Vietsub cung cấp editor/OCR, dịch ngữ cảnh Qwen và tạo giọng Việt local theo giọng đã chọn (Piper hoặc Kokoro) khi runtime tương ứng đã được kiểm tra.

## Đọc tài liệu theo thứ tự

1. [BOI_CANH_HE_THONG_HIEN_HANH.md](BOI_CANH_HE_THONG_HIEN_HANH.md) — trạng thái source, kiểm thử, rollout và công việc còn mở.
2. [NGHIEP_VU_HE_THONG_VIDEOMAKER.md](NGHIEP_VU_HE_THONG_VIDEOMAKER.md) — nguồn sự thật nghiệp vụ.
3. [KIEN_TRUC_KY_THUAT.md](KIEN_TRUC_KY_THUAT.md) — module, dữ liệu, API, worker và đường gọi thực tế.
4. [VAN_HANH_VA_PHAT_HANH.md](VAN_HANH_VA_PHAT_HANH.md) — database, secret, pricing, credential, rollout, rollback và publish.
5. [KIEM_THU_VA_NGHIEM_THU.md](KIEM_THU_VA_NGHIEM_THU.md) — test matrix và điều kiện nghiệm thu.
6. [AGENTS.md](AGENTS.md) — quy tắc dành cho AI agent.
7. [DE_XUAT_NGHIEP_VU_DONG_NHAT_GIONG_VEO_LOCAL.md](DE_XUAT_NGHIEP_VU_DONG_NHAT_GIONG_VEO_LOCAL.md) — nghiệp vụ gốc; xem [trạng thái triển khai thử nghiệm](KE_HOACH_TRIEN_KHAI_VEO_LOCAL.md).

Khi tài liệu khác source hoặc migration, source/migration là sự thật kỹ thuật. Khi mô tả nghiệp vụ khác nhau, ưu tiên `NGHIEP_VU_HE_THONG_VIDEOMAKER.md`.

Các báo cáo build/test và triển khai ở đây gắn với checkout, thời điểm và môi trường đã ghi trong tài liệu; chúng không xác nhận trạng thái của binary/database đang chạy. Muốn đánh giá một môi trường, kiểm tra schema version, cờ, credential/rate/budget, runtime model và bundle trên chính môi trường đó. [Bối cảnh hiện hành](BOI_CANH_HE_THONG_HIEN_HANH.md) phân biệt source, test tự động, smoke thủ công và rollout production.

## Cấu trúc solution

Admin Web tại `/admin` chỉ dùng UI mới: đăng nhập thông thường mở Tổng quan mới với nút Tạo gói & mở bán, thống kê và các bước chuẩn bị. Người dùng, gói, tổ chức/AI, phân bổ, SePay, TikTok, phát hành và popup dùng cùng component sáng. Markup/renderer/theme cũ và cơ chế nạp UI cho Razor cũ đã gỡ; tạo, sửa và nhân bản dùng cùng form. [Plan triển khai và bằng chứng](PLAN_TASK_HOP_NHAT_UI_ADMIN.md). Bản giao diện trong source không tự cập nhật server đang chạy.

Form **Tạo gói & mở bán** dùng cùng layout Admin theo mockup. Khi chạy `TOOL-SERVER` profile `https`, mở `https://localhost:7202/admin?view=create-plan` rồi đăng nhập Global Admin; hoặc chọn **Gói sử dụng → Tạo gói & mở bán**. Nút Chỉnh sửa và Nhân bản dùng cùng form này. Đây là Web Admin, không phải màn hình mua gói trong desktop. [Hướng dẫn local, ảnh thực tế và phạm vi kiểm thử](PLAN_TASK_HOP_NHAT_UI_ADMIN.md).

Đợt thay toàn bộ trình bày ngày 2026-10-13 dùng font Roboto tiếng Việt đi kèm server, sidebar và component mới, form/preview theo ảnh mẫu và giá hiển thị `70.000`. Header chung đã thu gọn: bỏ nhãn lặp, tiêu đề desktop 28–30 px, nội dung trang lên cao hơn. Marker mới **`admin-only-new-20261013.2`**. Để nạp source mới trong Visual Studio, Stop Debugging rồi Build `TOOL-SERVER`/F5; tải lại browser riêng lẻ không cập nhật process server. Mở `/admin` thông thường để kiểm Tổng quan sau login. [Header sau thu gọn](artifacts/admin-compact-header-20261013/screenshots/pricing-header-after-1440.png) được render từ source với văn bản mô phỏng; [ảnh login của lượt trước](artifacts/admin-only-new-20261013/screenshots/after-login-default-1440.png) và [form cùng kích thước mẫu](artifacts/admin-only-new-20261013/screenshots/plan-composer-reference-1672.png) dùng dữ liệu giả để kiểm hình thức.

| Thành phần | Trách nhiệm |
|---|---|
| `TOOL-SERVER` | ASP.NET Core API/Razor Admin; auth, license, SePay, tổ chức, budget, pricing, credential, AI Gateway, polling và proxy/cache output. |
| `TOOL-LOCAL` | WinForms + WebView2/React; đăng nhập, chọn tổ chức, workflow video, workspace, FFmpeg và Vietsub. |
| `TOOL-VIETSUB-TRANSLATION-WORKER` | Worker x64 cô lập LLamaSharp/Qwen, CPU và gói NVIDIA CUDA tùy chọn; không có Cloud client hoặc database workflow. |
| `TOOL-SHARED.Contracts` | DTO public giữa server và desktop. |
| `TOOL-DISTRIBUTION` | Kiểm tra manifest, provenance và SHA-256 của bundle phân phối. |
| `TOOL-MODEL-PUBLISHER` | Công cụ offline chuẩn bị/ký kho model staging và metadata thu hồi; không tự upload hoặc publish. |
| `TOOL-TESTS` | xUnit cho nghiệp vụ, bảo mật, migration, worker, updater và media. |
| `TOOL-UPDATER` | Cập nhật desktop có backup/rollback. |
| `TOOL-SETUP` | Bộ cài launcher/desktop. |
| `database` | Bootstrap, migration idempotent và role SQL ít quyền. |

Solution: `TOOL_GEN_POST_VIDEO.slnx`.

## Luồng sản phẩm

Nút **Thông tin gói** mở chi tiết gói của tài khoản (trạng thái, ngày bắt đầu/hết hạn và thiết bị). **Nâng cấp gói** mở bảng so sánh giá, thời hạn và quyền lợi lấy từ server; hỗ trợ tải lại khi lỗi hoặc chưa có gói mở bán. Hai popup hiện dùng để xem thông tin; nâng cấp/gia hạn được hướng dẫn liên hệ quản trị viên.

Nút **Tạo video mới** mở popup chọn **Video ngắn** hoặc **Video dài**, nhập nội dung rồi lưu và mở project mới trong tổ chức đang chọn. Tạo project chưa gọi AI; video ngắn lưu sẵn một cảnh và chỉ tạo clip khi người dùng bấm tạo video, xác nhận chi phí.

### Video dài

1. Người dùng đăng nhập, có license/device lease và chọn organization.
2. Tạo project có cấu trúc `OpenAiStructuredPlan`; project snapshot policy provider `LongForm` và speech policy.
3. OpenAI sinh content plan tiếng Việt có schema; lỗi ngôn ngữ/nhịp lời chỉ được repair sau quote và xác nhận.
4. Người dùng duyệt nhân vật, tài sản text và gán tài sản cho cảnh.
5. Fal/Veo yêu cầu `SceneFirstFrame` Approved/current đúng tỷ lệ.
6. Server reserve budget, submit clip và worker polling độc lập với desktop.
7. Desktop tải clip qua proxy, kiểm tra hash/media/audio, duyệt và render bằng FFmpeg.

`ProviderNativeVerified` dùng Native Audio, kiểm tra kỹ thuật rồi nghe/checklist/duyệt trực tiếp trong video dài, không gọi ASR. `CanonicalVoice` dùng voice profile/version đã duyệt, TTS WAV và ghép lời có lineage; `NativeVoiceOver` đi sang tạo video nền khi WAV hợp lệ, còn `OnCameraDialogue` cần duyệt WAV nhân vật trước khi tạo video nền và ghép. Cả hai đều nghe/duyệt clip đã ghép trước render; hình ảnh và chuyển động miệng giữ theo clip provider. Module lip-sync Cloud đã được loại bỏ.

Render cuối cần tối thiểu một cảnh đã duyệt, giữ đúng thứ tự scene plan và bỏ qua cảnh chưa duyệt. FinalVideo có thể xuất MP4 nhiều lần sau khi kiểm lại SHA-256 mà không render hoặc gọi provider lại.

Đồng nhất giọng Veo local (thử nghiệm, đã bật flag desktop): chọn mẫu giọng từ clip native đã duyệt cho mỗi nhân vật, chạy VAD/tách giọng/chuyển màu giọng local, nghe duyệt rồi mới render. Cần bật riêng cho project, migration 4.1.8 và runtime được cài/probe; xem [hướng dẫn bật và nghiệm thu](KE_HOACH_TRIEN_KHAI_VEO_LOCAL.md). Có source không đồng nghĩa model tiếng Việt hoặc production đã được nghiệm thu.

### Video ngắn

- Composer hai cột, hàng thumbnail Nhân vật/Trang phục, thư viện SQLite trên máy theo tài khoản/tổ chức và bản nháp phục hồi trước khi tạo project. [Hướng dẫn giao diện, lưu ảnh và kiểm chứng](TRIEN_KHAI_UI_THU_VIEN_VIDEO_NGAN.md).
- Project dùng `DirectShortVideo`, một cảnh **4/6/8 giây**, tỷ lệ **9:16 hoặc 16:9**, tạo clip bằng **Veo 3.1 qua Fal** theo policy `LongForm` của tổ chức. Không tự chuyển sang Kling khi Veo chưa sẵn sàng.
- Không gọi OpenAI để viết lại content.
- Chế độ nhập nội dung: lưu dự án → báo giá/tạo/duyệt ảnh đầu cảnh bằng OpenAI → báo giá/tạo/duyệt clip Veo → dựng/xuất MP4. Mỗi lần tạo AI cần xác nhận chi phí riêng.
- Dự án Kling cũ hiển thị nút **Chuyển dự án sang Veo** để xác nhận thời lượng/tỷ lệ mới; giữ lịch sử và ảnh mặc thử đã duyệt nếu tỷ lệ không đổi. [Triển khai và kiểm chứng Veo video ngắn](TRIEN_KHAI_VIDEO_NGAN_VEO.md).
- Desktop có thể giữ Native Audio hoặc loại bỏ audio cục bộ.
- Chế độ **Nhân vật mặc trang phục có sẵn** đã có source: nhập hai ảnh → báo giá/tạo/duyệt ảnh mặc thử → báo giá/tạo/duyệt clip → dựng/xuất MP4. Cần migration 4.1.9 và bật cờ server/desktop; mặc định tắt, chưa smoke provider thật. Xem [triển khai và nghiệm thu](TRIEN_KHAI_VIDEO_NGAN_NHAN_VAT_TRANG_PHUC.md).

### Vietsub

- Registry metadata nằm trên server; workspace, SQLite, media, cue và artifact nằm local. Khi chọn **Dịch Cloud**, server nhận snapshot text có giới hạn và gọi OpenAI; người dùng không chọn model hay nhập key.
- Editor hỗ trợ COPY/LINK, playback Range, SRT, timeline, thumbnail, waveform và PaddleOCR English/Chinese.
- Trong Thiết kế thành phẩm, tab **Che sub gốc** cho phép kéo/đổi kích thước vùng phủ màu hoặc làm mờ, lưu theo dự án và áp dụng khi xuất MP4. [Cách dùng và kiểm chứng](CHE_PHU_DE_GOC.md).
- Dịch local Qwen chạy trong worker x64 riêng và mặc định tắt tới khi model/benchmark/smoke đạt.
- Tăng tốc dịch local: tái sử dụng executor, chọn layer CUDA theo VRAM và fallback có giới hạn; xem [cấu hình, benchmark và phạm vi nghiệm thu](NANG_CAP_TOC_DO_DICH_LOCAL.md).
- Chọn giọng Vietsub lưu vào project local; job tạo audio dùng đúng engine/model/voice đã snapshot. Piper hoặc Kokoro chỉ sẵn sàng khi model và runtime của giọng đó được cài, kiểm checksum và probe WAV; `NOT_INSTALLED` không phải `READY`. Kokoro vẫn cần benchmark, nghe nghiệm thu, smoke desktop và rà soát quyền voicepack trước phát hành.

### Setup hệ thống khi mở desktop

Sau đăng nhập, license và organization hợp lệ, desktop tạo dashboard rồi mở modal Setup cho role `Owner`, `OrganizationAdmin`, `BillingManager` hoặc `Member` nếu FFmpeg hoặc các thành phần OCR/Qwen/Piper được bật đang thiếu hoặc hỏng; `Viewer` không thuộc gate này. Qwen chỉ được khởi tạo và yêu cầu trong Setup khi cả Vietsub và dịch local đều bật. Modal khóa nền; host C# đồng thời từ chối command nghiệp vụ cho tới khi mọi thành phần không `DISABLED` đều `READY`. Cờ hiển thị hoặc marker cũ không thay thế kiểm tra checksum/probe trên máy hiện hành. [Báo cáo tích hợp](BAO_CAO_SETUP_HE_THONG.md) ghi kiểm thử source; smoke trên Windows sạch và bundle phát hành là bước riêng.

Lệnh `TOOL-LOCAL.exe --check-desktop` kiểm tra các thành phần local trước đăng nhập, không kết nối SQL hoặc tải model. Tùy chọn người dùng được lưu trong LocalAppData, tách khỏi cấu hình triển khai cạnh EXE. Desktop hiện vẫn còn SQL workflow; xem [biên bản triển khai cho máy mới](TRIEN_KHAI_TOOL_LOCAL_TREN_MAY_MOI.md) để phân biệt phần đã sửa, kết quả kiểm thử và các task chưa hoàn tất.

Lệnh `TOOL-LOCAL.exe --check-webview2` kiểm riêng DLL và Evergreen Runtime, không cần cấu hình triển khai. Desktop nạp loader x64 từ `runtimes/win-x64/native` trong gói, kể cả khi publish single-file; phải sao chép đầy đủ thư mục publish. [Kế hoạch, bản sửa và kiểm chứng lỗi DLL](PLAN_TASK_SUA_LOI_WEBVIEW2_PUBLISH.md).

### Tải video Bilibili

- Mục **Tải video Bilibili** trong menu trái nhận link video/kênh/b23.tv, quét danh sách, chọn nhiều video và tải MP4 về máy với tiến độ, hủy/thử lại và chọn thư mục/chất lượng.
- Công cụ tải có phiên bản/checksum cố định, chạy local và không phát sinh chi phí AI. Hỗ trợ video công khai; lỗi hoặc giới hạn từ Bilibili phải báo danh sách chưa đầy đủ. [Sử dụng, kiến trúc và giới hạn kiểm chứng](TRIEN_KHAI_TAI_VIDEO_BILIBILI.md).

### Đăng TikTok

- Đây là item độc lập, theo tài khoản người dùng và không dùng organization/project video làm ownership.
- Có thanh chọn tài khoản, quản lý thêm/kết nối lại/ngắt từng tài khoản và lịch sử riêng. Nhiều tài khoản cần migration 4.1.8 và `TikTok:MultiAccountEnabled=true` trên server; cấu hình workspace đã bật cờ này theo yêu cầu người dùng. Xem [hướng dẫn triển khai nhiều tài khoản](TRIEN_KHAI_TIKTOK_NHIEU_TAI_KHOAN.md).
- Người dùng chọn MP4/MOV/WebM từ máy; đường dẫn và byte video ở desktop, không được gửi qua server.
- Login Kit Desktop dùng OAuth 2.0 + PKCE qua trình duyệt hệ thống và loopback `127.0.0.1`. Server giữ `client_secret`, access/refresh token đã mã hóa và metadata job.
- Global Admin nhập Client Key/Client Secret tại mục **Tích hợp TikTok**. Server mã hóa ngay, chỉ trả hint và giữ credential ở `Pending`; credential chỉ thành `Active` sau khi đúng Admin hoàn tất một lần OAuth thật trên Desktop.
- Server khởi tạo Direct Post; desktop tải tuần tự các chunk trực tiếp lên signed upload URL TikTok rồi chỉ hỏi trạng thái qua server. Server tiếp tục polling job khi desktop đóng.
- Không hỗ trợ cookie/session trình duyệt, Selenium hoặc nhập token thủ công. Public posting chỉ rollout sau khi TikTok duyệt app/scope và audit Content Posting API; `AuditedForPublicPosting=false` giữ an toàn cho tài khoản test riêng tư.

## Nguyên tắc hệ thống

- Provider key chỉ tồn tại trên server, được test rồi mã hóa; desktop không có BYOK.
- Mọi request AI đi qua JWT/session/device/license, membership/role, ownership, pricing, budget và idempotency.
- Budget `0` khóa AI; thiếu rate dừng trước outbound.
- Project snapshot provider/model/policy; không tự failover hoặc đổi provider khi admin đổi policy.
- Output provider không lộ trực tiếp. Server cache/proxy có authorization và SSRF guard; desktop xác minh file local.
- TTS, ASR, Native Audio, Canonical Voice và local model không fallback ngầm cho nhau.
- Vietsub giữ dữ liệu biên tập và media/path local; snapshot text/kết quả Cloud tạm được mã hóa, có thời hạn lưu riêng trên server.
- TikTok token/session nằm ở server; video/path local nằm ở desktop và signed upload URL không được đưa vào React hoặc log.

## Mặc định quan trọng

- OpenAI Text/Image/Voice và Kling có catalog trong source; Kling 3.0 Native Audio 720p là **model video mặc định của catalog**, còn project video ngắn mới chọn Fal/Veo theo policy `LongForm` khi môi trường đã cấu hình/rollout.
- BytePlus Seedance và Fal/Veo được seed `Disabled`.
- Source server có `CanonicalVoiceEnabled=true` và `SpeechVerificationEnabled=true`; desktop giữ `SpeechSynchronizationEnabled=false`. Khả dụng thực tế còn phụ thuộc cấu hình máy, credential, rate, budget và readiness.
- SePay mặc định `Enabled=false`.
- Vietsub và OCR bật; dịch Qwen tắt; UI/cài đặt giọng Piper bật nhưng runtime/model không được coi là sẵn sàng khi chưa qua gate.
- Cấu hình workspace kế thừa `local-2` đang bật Dịch Cloud (`VietsubCloudTranslation:Enabled=true`, model `gpt-5.6-luna`); khả dụng vẫn phụ thuộc schema, quyền, credential, rate và budget. Xem [hướng dẫn vận hành Cloud](HUONG_DAN_VAN_HANH_DICH_CLOUD_VIETSUB.md) trước khi triển khai môi trường khác.
- Item TikTok hiển thị mặc định ở desktop; quản lý credential trong Admin được hỗ trợ nhưng integration runtime vẫn tắt cho tới khi credential được OAuth xác minh. `TikTok:EmergencyDisabled` là kill switch theo môi trường.
- Migration nghiệp vụ hiện có đến `4.1.9-short-video-outfit`. Bảng `vf.ShortVideoOperations` của 4.1.9 được dùng cả cho báo giá `TextOnly`; migration trong source không chứng minh đã áp trên database đích. Các migration lip-sync Cloud cũ còn trong repository để giữ lịch sử, module runtime đã loại bỏ.

## Yêu cầu phát triển

- Windows x64, .NET SDK 10, Node/npm phù hợp lockfile và SQL Server.
- FFmpeg/FFprobe đã kiểm tra provenance/checksum cho xử lý media.
- Không commit secret. Cấu hình máy cá nhân của desktop nằm trong `appsettings.user.json` đã được ignore.

## Build và test

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Frontend:

```powershell
Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
npm test
```

Build `TOOL-LOCAL` tự chạy production build của web. Test model Qwen/Piper/Kokoro thật và benchmark là opt-in; xem `KIEM_THU_VA_NGHIEM_THU.md`.

## Chạy development

Không lưu secret trong source. Cấu hình connection string và JWT signing key bằng secret store:

```powershell
dotnet user-secrets set --project TOOL-SERVER "ConnectionStrings:VideoFactory" "<connection-string>"
dotnet user-secrets set --project TOOL-SERVER "Jwt:SigningKey" "<secret-at-least-32-bytes>"
dotnet run --project TOOL-SERVER --launch-profile https
```

Desktop mặc định kết nối `https://localhost:7202/`. Có thể dùng `TOOL-LOCAL\appsettings.user.json` để ghi đè cấu hình máy không chứa secret. Không khởi động server trước khi database đúng version vì startup bootstrap có thể ghi catalog.

Đồng nhất giọng Veo local đã bật trong cấu hình desktop; model vẫn phải cài và probe trước khi dùng. Xem [hướng dẫn chạy trên máy đích](HUONG_DAN_CHAY_DONG_NHAT_GIONG_VEO_LOCAL.md) để kiểm tra READY và chọn mẫu giọng từ cảnh Native Audio đã duyệt. Trên máy đã cấu hình, nhấp đúp [Mo-VideoMaker.cmd](Mo-VideoMaker.cmd) để mở Account Server nền và desktop Release. Launcher kiểm schema trước khi khởi động và dùng URL của desktop; máy triển khai hiện dùng HTTPS 7242 với database Development đã xác minh.

## Phát hành

Không publish chỉ từ một build xanh. Với binary hiện hành, phải rehearsal chuỗi migration đến 4.1.9 trên clone và xác minh schema/quyền ở môi trường đích trước khi khởi động server; cấu hình rate/credential/budget, xác minh TikTok Developer App qua Global Admin nếu bật, smoke môi trường, kiểm tra FFmpeg `Approval scope: Release`, package integrity và rollback theo [VAN_HANH_VA_PHAT_HANH.md](VAN_HANH_VA_PHAT_HANH.md). Tách riêng Passed/Failed/Skipped; các bài model/SQL opt-in bị Skipped không phải bằng chứng nghiệm thu.
