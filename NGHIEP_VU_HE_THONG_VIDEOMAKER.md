# Nghiệp vụ và kiến trúc VideoMaker

Luồng phát hành hiện hành là **StandaloneCatalog**: Admin nhập phiên bản/link ZIP rồi lưu, phát hành; link tải EXE được lưu một lần cho toàn hệ thống. Không cần upload hoặc Active Setup trên hosting. Setup 1.3.0 đọc catalog 5, tải và kiểm ứng dụng/model từ VPS tại máy cài; chỉ READY sau probe và commit. Phiên bản/URL đã phát hành bất biến; cập nhật lên phiên bản mới hơn, sửa chữa đúng package đang cài. Tự cập nhật yêu cầu link EXE có hash do công cụ đóng gói tự đặt tên. Người dùng đã xác nhận luồng tải Setup hoạt động ngày 2026-10-17. [Nghiệp vụ chi tiết, trường nhập và cách phát hành tiếp](NGHIEP_VU_SETUP_VA_PHAT_HANH_DESKTOP.md), [triển khai và bằng chứng kỹ thuật](TRIEN_KHAI_PHAT_HANH_DON_GIAN_VPS.md). Những đoạn DirectCatalog/Setup Active phía dưới mô tả luồng cũ được giữ tương thích, không là gate của luồng mới.

Nút tải Setup trên website có cấu hình URL riêng: Admin lưu một link EXE HTTPS trên VPS, server lưu trong `auth.ServerSettings` và chuyển hướng để trình duyệt tải trực tiếp. Ô cấu hình này không yêu cầu upload/version/checksum và không chứng nhận file đã được kiểm tra. Đây là link tải trên website, không thay đăng ký Setup Active hoặc điều kiện phát hành/cài/update ứng dụng. [Phạm vi và kiểm chứng](TRIEN_KHAI_LINK_TAI_SETUP_TREN_VPS.md).

Luồng hiện hành **DirectCatalog**: Admin nhập phiên bản và URL ZIP rồi lưu/phát hành, không chờ hosting tải kiểm ZIP. Công cụ Publish tạo tên ZIP gắn version/build/SHA-256; server lưu danh tính khai báo, không báo đã kiểm file. Setup dùng chung 1.2.0 capability `3,4` lấy catalog 4, tải ZIP từ VPS, kiểm hash và native identity rồi cài/probe ứng dụng/model theo transaction distribution 3. Identity đã phát hành bất biến; update chọn version mới hơn, repair đúng package đang cài; model chỉ READY sau probe trên máy đích. [Chi tiết và bằng chứng](TRIEN_KHAI_PHAT_HANH_TRUC_TIEP_TU_VPS.md). Các đoạn protocol 2/3 tiếp theo mô tả đường cũ được giữ tương thích.

Release mới protocol 3: Admin nhập **Phiên bản, URL ZIP ứng dụng**, tùy chọn ghi chú/bắt buộc cập nhật. Hệ thống tải kiểm ở nền trước khi cho phát hành; đổi version/link của nháp làm mất kết quả kiểm cũ, release đã từng phát hành khóa identity. Setup đăng ký riêng cấp hệ thống và tái dùng cho nhiều ZIP tương thích; mỗi lượt cài giữ snapshot release/hash/Setup, repair lấy đúng bản đang cài. Latest/cập nhật mới so Version trước BuildNumber. Model chỉ READY sau probe trên máy đích. [Biên bản triển khai](TRIEN_KHAI_FORM_RELEASE_TOI_GIAN_VA_SETUP_DUNG_CHUNG.md) tách source/kiểm thử khỏi nghiệm thu khách và rollout; đoạn protocol 2 bên dưới mô tả luồng cũ được giữ tương thích.

Phát hành ứng dụng qua VPS (source 2026-10-17): Admin đăng ký ZIP ứng dụng ngoài bằng metadata và exact Setup của cùng release; server không tự chứng nhận byte VPS từ JSON. Setup tải, xác minh, chuẩn bị/probe đủ thành phần và commit rồi mới hoàn tất. StaticArchive update/repair phải đi qua đúng Setup; repair không đòi model READY trước khi sửa và không được giả/xóa ownership để vượt gate. [Trạng thái triển khai và nghiệm thu](TRIEN_KHAI_PHAT_HANH_TOOL_LOCAL_QUA_VPS_VA_SETUP.md) phân biệt source, fixture, bản cài thật và rollout.

## Trạng thái cài đặt và phục hồi — 2026-10-17

Setup chỉ báo hoàn tất khi probe/evidence đủ và giao dịch ứng dụng đã commit hợp lệ. Snapshot UI rỗng/thiếu/hỏng, receipt cũ hoặc missing marker không thay bằng chứng này. Mất phản hồi phải đối soát operation cũ, không tự tạo lượt cài mới; người dùng chỉ xác nhận RAM cho đúng profile đang được kiểm. Khôi phục file ứng dụng vẫn có thể yêu cầu verify model lại. [Phạm vi kiểm thử ổn định](TRIEN_KHAI_TEST_ON_DINH_SETUP_MODEL_UI_UX_VA_LOGIC.md).

## Setup StaticArchive — bổ sung 2026-10-16

Bản phát hành chọn StaticArchive có thể chuẩn bị bộ local bắt buộc trước đăng nhập: tải đúng ZIP VPS, kiểm byte/inventory, tạo runtime và probe Qwen/Piper/OCR/media/WebView2. Chỉ sau commit đủ bộ mới báo hoàn tất; file đúng hash, component bị tắt hoặc test skip không thay READY. Policy của bản StaticArchive quyết định bốn tính năng local bắt buộc trong bộ nhớ, không ghi đè cấu hình server/SQL/workspace của người dùng. Chuẩn bị component không cấp quyền sử dụng: login/license/project/organization gate vẫn giữ. [Phạm vi source và nghiệm thu](TRIEN_KHAI_SETUP_CAI_MODEL_TU_VPS.md).

## Gói chỉ dùng tool dịch Vietsub — source 2026-10-15

Logic bổ sung ngày 2026-10-15: gói chỉ Local dùng workspace cá nhân, không thiết lập phân bổ. Nhóm nhận khách chỉ chọn Toàn bộ công cụ hoặc Vietsub có Cloud; các kiểm tra dịch không ghi đè cờ sẵn sàng video. Hạn mức AI bằng 0 không khóa tác vụ Local. Cloud dùng cùng quy tắc model/host/structured output và rate duy nhất ở bước mở bán lẫn bước tạo job; thiếu schema Cloud, cấu hình server, credential, rate hoặc ngân sách sẽ chặn mở bán theo đúng nguyên nhân. Liên kết sửa cấu hình mang organization/model cụ thể; cờ Cloud và migration còn thiếu phải xử lý ở cấu hình server/database, không giả lập bằng cách mở credential.

License hiện hành được chọn thống nhất theo trạng thái, khoảng hiệu lực, thời hạn và thứ tự ổn định; API không cộng quyền từ các license đã kích hoạt khác. Ngừng bán hoặc tắt catalog không thu hồi quyền đã cấp: thu hồi phải thực hiện trên license. Đơn đang chờ vẫn giữ giá và quyền đã snapshot. Mua khác gói nối sau toàn bộ lịch đã mua; gia hạn chỉ gộp vào license cuối cùng nếu cùng gói và cùng quyền. Nếu quyền khác, cấp license nối tiếp, không sửa snapshot cũ. Tài khoản có license không giới hạn thời hạn cần quản trị viên xử lý trước khi mua nối tiếp. Ngày dự kiến chỉ là ước tính trước khi nhận tiền; ngày hiệu lực chính thức được cấp trong transaction khi xử lý thanh toán.

Đã nhận tiền nhưng tổ chức chưa sẵn sàng giữ trạng thái Paid/chờ kích hoạt, có thể retry mà không thu tiền hoặc cấp quyền lần hai. License chờ hiệu lực đã hết hạn/bị thu hồi không được tự kích hoạt membership. Trạng thái Scheduled và ngày hiệu lực được trả về desktop, không chờ kích hoạt thiết bị vô hạn. Trạng thái/kết quả/hủy tác vụ Cloud đã tạo được kiểm quyền phiên, thành viên và chủ project; quyền tạo/resume/retry có phí vẫn kiểm lại license Cloud. Worker tiếp tục settlement/reconciliation cho phần đã gửi, kể cả license hết hạn hoặc Cloud bị tắt.

Form tạo/sửa/nhân bản có loại gói Toàn bộ công cụ và Chỉ tool dịch / Vietsub. Gói Vietsub gồm OCR, dịch Local, biên tập phụ đề, giọng Local và xuất SRT/MP4; Cloud là quyền riêng, mặc định tắt khi tạo gói mới. Model/runtime trên máy khách vẫn cần cờ và probe hiện hành, quyền gói không tự chứng minh runtime đã sẵn sàng.

Theo yêu cầu tiếp theo ngày 2026-10-15, gói chỉ Local mở mặc định Dịch phụ đề; các menu nghiệp vụ khác, Cài đặt và nút Tạo video mới vẫn hiện nhưng khóa. Giữ nhãn/icon, bổ sung biểu tượng khóa, lý do và trạng thái trợ năng; click, Enter, Space không điều hướng hoặc gửi command. Quyền được tính từ license đang có hiệu lực: nhận `license.activated` hoặc dashboard mới phải cập nhật ngay; đang ở trang khác phải quay về Dịch. Chưa bật tính năng hoặc chưa hoàn tất Setup thì phần Dịch báo chưa sẵn sàng. Xem/nâng cấp gói, đăng xuất và Setup runtime giữ đường riêng; khóa menu Cài đặt không bỏ gate/probe runtime. Không đổi chính sách menu của gói Cloud/toàn bộ công cụ trong thay đổi này.

Điều kiện nhóm nhận khách được tính theo loại gói tại form, khi ghi, offers và reserve. Với gói chỉ Local không cần tổ chức hoặc nhóm nhận khách, không tạo mapping mới, không kiểm sức chứa/giữ chỗ/kích hoạt membership; chỉ cần thông tin bán hợp lệ và SePay sẵn sàng. Thanh toán cấp license, desktop dùng workspace cá nhân theo tài khoản với kiểm tra license/thiết bị/chủ project và runtime. Không cần ngân sách AI, OpenAI/Fal hoặc cờ sẵn sàng AI đã lưu. Với gói bật Cloud, thêm cờ Cloud/model dịch trên server, OpenAI Active, capability structured output, đúng host, đơn giá InputToken/OutputToken duy nhất còn hiệu lực cùng tiền tệ và ngân sách khả dụng. Cloud không yêu cầu policy/video provider. Reservation và membership chỉ áp dụng gói có Cloud hoặc toàn bộ công cụ. Local giữ snapshot đơn/license, lịch hiệu lực, transaction và idempotency; đơn Local cũ đang Reserved được release khi cấp gói, không tạo membership. Dữ liệu phân bổ/project cũ không tự chuyển chủ hoặc xóa; điều kiện SePay riêng vẫn áp dụng khi mở bán.

Quyền nằm trong `FeatureFlagsJson` với `productScope=all-tools|vietsub` và `vietsubCloudEnabled`, được snapshot vào đơn/license hiện hành. Thiếu scope giữ quy tắc legacy; scope lạ hoặc JSON hỏng không được mở công cụ ngoài gói. Server từ chối generation/TikTok và Cloud không thuộc quyền trước provider/job, desktop chặn command và chỉ hiển thị module được cấp. Chỉnh sửa giữ quyền gói hiện có; muốn đổi quyền thì nhân bản/tạo gói mới. Mặc định nhân bản giữ quyền nguồn, admin có thể chọn loại/quyền Cloud khác cho bản mới; không đổi gói nguồn hoặc license đã cấp.

Đây là trạng thái source trên `main`, nền `f0651c918f28ca477f28875818148e9e86240b7b`, working tree chưa commit. Có migration mới 4.1.16 cho registry cá nhân; chưa chạy SQL hosting hoặc provider có phí. Phải triển khai đồng bộ server/desktop và migration; chưa xác minh rollout hoặc giao dịch thật của loại gói mới. Local Scheduled từ luồng cũ được release khi đối soát, không tự tạo membership, kể cả trước ngày hiệu lực.

## Admin tạo gói và mở bán — 2026-10-13

Global Admin nhập tên, thời hạn, giá VND, số thiết bị và nhóm nhận khách trong cùng một form, xem trước gói rồi chọn lưu nháp hoặc tạo và mở bán. Nhóm chỉ có một lựa chọn sẵn sàng được chọn trước. Nhiều gói trong cùng nhóm dùng chung sức chứa; tạo gói không làm tăng hoặc giữ chỗ nhận khách.

Nháp có `IsActive=false` và `IsPublic=false`, chưa được cấp hoặc bán, có thể chưa chọn nhóm/giá/thời hạn. Với gói toàn bộ công cụ, mở bán cần thông tin bán hợp lệ, nhóm Active có ít nhất một tổ chức Active/bật phân bổ/đã kiểm tra, readiness AI hiện hành đạt, còn chỗ và SePay sẵn sàng. Gói chỉ tool dịch dùng điều kiện riêng ở mục phía trên. Server kiểm lại lúc ghi, không tự bật nhóm hoặc thay credential/rate/budget để đạt điều kiện.

Nhân bản tạo gói mới, giữ quyền của gói nguồn; gói nguồn và license/đơn đã cấp không đổi. Tạo gói cùng mapping phải thành công hoặc rollback toàn bộ. Gửi lại cùng thao tác không tạo thêm gói; nội dung hoặc Admin khác bị conflict. Tạo/sửa/nhân bản dùng cùng form; dialog cũ đã gỡ. Sửa giữ mã/quyền, kiểm phiên bản plan và mapping, không chuyển/gỡ nhóm còn seat Active/Reserved/Scheduled. Có bốn trạng thái: nháp (Inactive/private), riêng tư (Active/private, cấp tay), mở bán (Active/public) và tạm ngừng bán (Inactive/public). Dừng bán/nháp/riêng tư không cần thanh toán sẵn sàng; public vẫn cần giá/thời hạn hợp lệ. [Phạm vi source, kiểm thử và hướng dẫn](PLAN_TASK_HOP_NHAT_UI_ADMIN.md).

## Bổ sung 2026-09-10: video ngắn phối trang phục

`DirectShortVideo` có mode `TextOnly` mặc định và `CharacterOutfit` khi cờ được mở. Mode mới dùng một ảnh nhân vật và một ảnh trang phục; lưu bối cảnh/chuyển động, báo giá riêng từng bước AI, duyệt ảnh trước clip và duyệt hình/âm thanh trước xuất MP4. Từ 2026-09-11, cả hai mode tạo clip bằng Veo 3.1, một cảnh 4/6/8 giây, tỷ lệ 9:16 hoặc 16:9; tỷ lệ/audio chọn lúc tạo project. Đổi ảnh, bối cảnh hoặc chuyển động làm tăng revision và vô hiệu hóa kết quả đã duyệt. Chỉ hỗ trợ âm thanh môi trường hoặc tắt tiếng, không TTS/thoại. Feature mặc định tắt, máy Development đã bật bằng override; chưa nghiệm thu provider thật. Xem [triển khai Veo](TRIEN_KHAI_VIDEO_NGAN_VEO.md).

## Bổ sung 2026-09-09: đồng nhất giọng Veo local (thử nghiệm)

- Chỉ project Fal/Veo OpenAiStructuredPlan + ProviderNativeVerified; native clip đã duyệt, cảnh thoại một nhân vật, generation 4/6/8 giây. Bật rõ cho từng project; không đổi ngầm project cũ.
- Mỗi nhân vật dùng Voice Anchor lấy từ đoạn nói sạch của clip native. Người dùng xác nhận quyền dùng giọng, clip một người nói, nghe mẫu rồi duyệt; không nhận mẫu giọng ngoài trong MVP.
- Chỉ speech-to-speech: VAD, separation, voice conversion, giữ residual và trộn lại, remux hình gốc. Không TTS, ASR Cloud hay fallback lip-sync có phí; không hứa bảo đảm khẩu hình chỉ từ thời lượng.
- Kết quả chạy xong ở ReviewRequired; phải nghe kiểm tra câu chữ, mẫu giọng, khẩu hình và âm nền. Duyệt mẫu khác, sửa lời/prompt/nhân vật hoặc đổi nguồn làm kết quả cũ hết hiệu lực.
- Batch do người dùng chọn; lỗi/hủy có checkpoint và chạy lại local, không tạo request Veo. Ngoại lệ dùng native phải có xác nhận, lý do, người duyệt và thời gian.
- Khi policy local bật, native approval không tự trở thành final approval. Render chỉ dùng converted result hoặc ngoại lệ native được duyệt còn khớp hash/snapshot; bỏ qua cảnh chưa đủ duyệt và báo lỗi nếu không còn cảnh nào.
- Giữ native, anchor, checkpoint và kết quả; dọn intermediate theo thao tác xác nhận có allowlist. Model mặc định tắt cho đến khi có nghiệm thu thật; chi tiết trạng thái ở nhật ký triển khai.

> Nguồn sự thật nghiệp vụ hiện hành. Rà soát theo source tại commit `8f10cc9` ngày 2026-09-15; trạng thái rollout phải tra theo môi trường.

Trạng thái triển khai nằm trong `BOI_CANH_HE_THONG_HIEN_HANH.md`; kiến trúc kỹ thuật nằm trong `KIEN_TRUC_KY_THUAT.md`; hướng dẫn vận hành nằm trong `VAN_HANH_VA_PHAT_HANH.md`.

## 1. Mục tiêu và ranh giới hệ thống

VideoMaker hỗ trợ ba nhóm công việc:

1. Tạo video dài nhiều cảnh từ chủ đề và content plan có cấu trúc.
2. Tạo video ngắn một cảnh trực tiếp từ nội dung người dùng.
3. Tạo/chỉnh phụ đề, dịch ngữ cảnh và tạo giọng Việt bằng media/AI local.

AI cloud được quản trị theo organization, không theo máy. Server sở hữu auth, license, organization, chi phí, credential, provider request, output proxy và registry dùng chung; desktop sở hữu trải nghiệm biên tập, workspace và xử lý media local. Không có BYOK.

## 2. Vai trò và quyền

| Role | Quản lý thành viên | Budget/usage | Credential | Phát sinh AI |
|---|---:|---:|---:|---:|
| `Owner` | Có | Có | Có | Có |
| `OrganizationAdmin` | Có | Có | Có | Có |
| `BillingManager` | Không | Có | Không | Có |
| `Member` | Không | Không | Không | Có |
| `Viewer` | Không | Không | Không | Không |

- Global Admin tạo organization, quản lý catalog/rate, license plan, pool và release.
- Chỉ Owner quản lý Owner; không được xóa, hạ cấp hoặc suspend Owner Active cuối cùng.
- OrganizationAdmin không cấp/thu hồi Owner; BillingManager không quản lý member/credential.
- Viewer bị chặn trước mọi outbound có khả năng phát sinh chi phí.

## 3. Xác thực, license và organization

Một request bảo vệ cần JWT, session, user/device Active, device claim đúng, license lease còn hiệu lực, membership/role hợp lệ và project ownership đúng organization. Refresh token lưu hash, rotate atomically và revoke family/session khi reuse.

Một user có thể thuộc nhiều organization nhưng mỗi request chỉ thuộc đúng organization đang chọn. Task đã gửi provider tiếp tục được worker theo dõi và quyết toán theo snapshot ban đầu dù desktop đóng hoặc membership/license thay đổi; không chuyển task sang organization hay credential khác.

## 4. Organization AI Gateway

Mỗi request cloud phải truy vết được organization, user/session/device, project, provider/model, provider request, credential version, rate snapshot, reservation/usage/actual cost và idempotency key/request hash.

Thứ tự bắt buộc trước outbound:

1. JWT, session, user và device.
2. License lease.
3. Membership và role.
4. Project ownership.
5. Payload, request hash và idempotency.
6. Provider/model/policy/capability.
7. Credential version.
8. Rate Active.
9. Budget reservation.
10. Outbound provider.

Không release mù hoặc submit lại khi trạng thái upstream còn không chắc chắn; worker phải reconcile.

## 5. Credential, pricing và budget

- Mỗi organization có tối đa một credential `Active` cho mỗi provider. Credential mới phải test trước khi ghi; response chỉ có hint/version/status.
- Rotation theo `Active -> Retiring -> Revoked`; task đang chạy giữ version đã snapshot.
- Global Admin nhập rate từ hợp đồng/dashboard chính thức; bootstrap không seed giá.
- Thiếu rate trả `pricing_not_configured` trước outbound. Budget organization bằng `0` khóa AI; member limit cũng phải được kiểm tra.
- Reservation/settlement/release dùng transaction `Serializable` và operation key idempotent.
- `RateSnapshotJson` quyết toán request cũ; đổi giá mới không sửa lịch sử. Thiếu usage đáng tin cậy dùng estimate đã khóa theo policy, không mặc định ghi actual cost 0.

## 6. Vòng đời project video

Project có hai cấu trúc:

- `OpenAiStructuredPlan`: video dài nhiều cảnh.
- `DirectShortVideo`: video ngắn một cảnh.

`LongForm` và `Default` là scope của video provider policy, không phải tên cấu trúc project. Project gắn `OrganizationId`, `CreatedByUserId` và snapshot provider/model/policy/resolution/speech production policy. Đổi policy organization chỉ ảnh hưởng project mới.

Tạo project và lưu nháp không tự gọi OpenAI/Veo/Kling, không reserve budget và không tự coi scene/first frame/clip là đã duyệt. Mỗi request AI sau đó phải qua access, rate, budget và idempotency; nơi workflow có quote/xác nhận thì quote phải khớp đúng request trước outbound. Thay nội dung, nhân vật, scene, ratio hoặc policy snapshot qua luồng chuyển hợp lệ phải vô hiệu kết quả phụ thuộc nhưng giữ request/usage lịch sử để đối soát.

## 7. Video dài và content plan

- OpenAI dùng Responses API, JSON Schema, `store=false` và safety identifier là hash user ID.
- Nội dung `OpenAiStructuredPlan` dùng `vi-VN`; output gồm script, character, scene, speech intent và project asset có key ổn định.
- Scene có content duration và generation duration; provider có thể tạo clip dài hơn rồi desktop trim tail.
- Output sai schema/ngôn ngữ/nhịp lời vẫn phải ghi request, usage và chi phí đã phát sinh; trả lỗi có field/reason/request ID an toàn.
- Replay cùng idempotency trả lỗi cũ. Chỉ cho tối đa một lượt repair sau quote/xác nhận; repair không đổi key, scene count hoặc cấu trúc đã khóa.

## 8. Character, asset và first frame

- Character có version, visual identity, wardrobe, immutable traits và forbidden changes. Chỉ reference primary Approved/current đúng project được dùng.
- Project asset `Background`, `Prop`, `Item` hiện là text-only, có version và trạng thái Draft/Locked. Scene có assignment phải có đúng một Background và mọi asset phải hợp lệ/được khóa.
- Cảnh một nhân vật nói trực diện dùng `OnCameraDialogue`; `NativeVoiceOver` dành cho B-roll không gắn nhân vật.
- `SceneFirstFrame` là entity riêng, đúng project/scene/aspect/source snapshot và có lifecycle generate/materialize/review.
- Fal/Veo Image-to-Video bắt buộc first frame Approved/current. Không gửi identity image vuông trực tiếp và không fallback Text-to-Video.
- Sửa scene/character/asset làm invalid các output phụ thuộc theo version/hash, không sửa lịch sử.

## 9. Video generation và provider

- Server xác minh duration, ratio, resolution, audio và reference capability; submit idempotent và không tự failover model/provider.
- Worker server là nơi polling duy nhất, cache output trước khi hoàn tất và settle terminal.
- Desktop tải qua relative proxy, dùng `.part`, kiểm tra MIME/size/hash/FFprobe rồi tạo local asset.
- Kling là default catalog/video dài khi policy tổ chức chọn Kling. Video ngắn mới yêu cầu Fal/Veo theo snapshot `LongForm`, không tự dùng Kling; BytePlus Seedance và Fal/Veo có adapter nhưng được seed `Disabled` và phải rollout riêng.
- Fal Standard/Fast là endpoint riêng, không fallback. Output URL gốc không rời server.

## 10. Video ngắn

- `DirectShortVideo` có một scene 4/6/8 giây, tỷ lệ 9:16 hoặc 16:9, dùng Veo 3.1 Standard/Fast qua Fal ở 720p theo snapshot policy `LongForm`; chỉ hỗ trợ policy Native Audio, desktop có thể tắt tiếng sau tải. Thiếu policy Veo/rate/credential phải dừng trước outbound.
- `TextOnly` là mode mặc định; `CharacterOutfit` cần bật cả cờ server/desktop và ảnh nhân vật + trang phục đã kiểm. Cờ tắt không loại bỏ phụ thuộc schema 4.1.9 của bảng quote `vf.ShortVideoOperations` cho `TextOnly`. Không cấp quyền CRUD hai bảng 4.1.9 cho desktop; quote/approval được server sở hữu.
- Cả `TextOnly` và `CharacterOutfit` bắt buộc `SceneFirstFrame` Approved/current. TextOnly tạo ảnh từ nội dung qua OpenAI; CharacterOutfit dùng ảnh mặc thử đã duyệt. Ảnh và video có bước báo giá/xác nhận/duyệt riêng, kể cả clip tắt tiếng.
- Quote phải còn hiệu lực và khớp project/scene/revision/input hash, provider/model/rate/credential snapshot trước submit; `Unknown` hoặc request chưa terminal không được tự submit lại để "phục hồi". Desktop reconnect lấy lại status/output của request cũ, kiểm file/lineage rồi mới cho duyệt/render.
- Dự án đã snapshot Kling cần thao tác **Chuyển dự án sang Veo** có xác nhận thời lượng/tỷ lệ. Không tự sửa snapshot khi mở dự án hay khi policy tổ chức đổi. Chuyển không gọi AI hoặc giữ ngân sách; giữ lịch sử, vô hiệu báo giá cũ, chỉ giữ ảnh mặc thử đã duyệt khi tỷ lệ không đổi và ảnh đúng yêu cầu. Chặn chuyển khi còn request chưa có kết quả cuối.
- Không gọi OpenAI để viết lại content và không áp policy tiếng Việt dành riêng cho video dài.
- Khi tắt audio, desktop strip toàn bộ audio khỏi output local; provider policy không làm thay đổi cấu trúc workflow.

## 11. Speech, Native Audio và Canonical Voice

Scene speech mode là `None`, `OnCameraDialogue` hoặc `NativeVoiceOver`. Project speech production policy là:

- `ProviderNativeVerified`: provider tạo Native Audio. Với video dài, desktop kiểm tra audio kỹ thuật rồi người dùng phát video, xác nhận checklist và duyệt trực tiếp; không quote/gọi ASR và không tạo `SpeechVerificationReport`.
- `CanonicalVoice`: dùng voice profile/version Approved để tạo WAV chuẩn và ghép vào video.

Quy tắc Canonical Voice:

- Voice profile version bất biến; preview phải được nghe/xác nhận trước khi approve.
- Catalog do server trả về gồm `alloy`, `ash`, `ballad`, `coral`, `echo`, `fable`, `onyx`, `nova`, `sage`, `shimmer`, `verse`, `marin`, `cedar`. Alias `female-sweet`/`male-warm` ánh xạ `shimmer`/`onyx` cho dữ liệu cũ.
- Mở/chọn modal không gọi provider. Preview TTS là request có phí, dùng project hiện hành hoặc project kỹ thuật ẩn theo user+organization làm context ownership/audit/budget. Project ẩn không xuất hiện trong list/dashboard và không sửa cấu hình project nội dung.
- Phải quote/xác nhận trước outbound; phát lại WAV đã tải trong phiên không tạo request mới.
- Content plan hướng lời tới 85–95% content duration; validator dùng biên 80–105% trước TTS. Lỗi nhịp chỉ mở repair có quote, không tự sinh lại.
- TTS qua đầy đủ credential/model/rate/budget/idempotency/output proxy. WAV được kiểm tra MIME, hash, sample rate, duration, audibility và ratio.
- Canonical Voice không gửi WAV qua ASR và không phụ thuộc WER/CER. Speech verification chỉ áp dụng cho workflow không phải `OpenAiStructuredPlan` khi feature flag độc lập được bật.
- `NativeVoiceOver` có WAV hiện hành đúng lineage đi thẳng sang tạo video nền; lệnh tạo video kiểm tra file rồi chấp nhận đúng VoiceGeneration trước outbound, không có bước duyệt WAV riêng.
- `OnCameraDialogue` dùng giọng nhân vật: chuẩn bị và duyệt WAV, tạo video nền, ghép WAV, nghe/duyệt clip rồi render. Hình và chuyển động miệng giữ theo clip provider; không có bước chỉnh khẩu hình Cloud. Trạng thái chờ của project cũ không tự chứng minh WAV đã duyệt; hệ thống phải kiểm bản ghi giọng hiện hành trước khi tiếp tục.
- Narrated asset mới dùng `scene-audio-sync-v3`; `v2` chỉ tương thích khi là exact approved pointer và còn khớp generation, VoiceGeneration, speech/voice snapshot và hash. Phiên bản cũ hơn bị chặn.
- Retry `NativeAudioInvalid`, repair, TTS hoặc provider lần hai là request có phí mới và cần xác nhận/idempotency phù hợp.

## 12. Render và xuất video

- Bản dựng cần tối thiểu một scene đã duyệt; lấy các scene Approved/current theo đúng thứ tự scene plan và bỏ qua scene chưa duyệt.
- Mỗi scene phải có approved render asset đúng generation/voice/speech snapshot và file khớp SHA-256.
- FFmpeg normalize, concat, mix voice/music/ambience, chèn silent track khi cần và ghi qua file tạm; FFprobe xác minh output.
- Retry render chỉ làm local, không gọi provider.
- FinalVideo được xuất MP4 nhiều lần qua file tạm/atomic replace sau khi kiểm hash; thao tác export không render hoặc gọi AI lại và không làm mất bản workspace dùng để preview.

## 13. Vietsub local-first

- `VietsubProjectId` độc lập project video. `vs.Projects` giữ registry metadata/ownership/audit; `OrganizationId=NULL` là project Local cá nhân, luôn lọc `CreatedByUserId`; public/native dùng `Guid.Empty` để chọn rõ workspace cá nhân. Audit cá nhân ghi `auth.AccountAuditLogs` cùng transaction lưu project; project tổ chức giữ organization audit và membership/role. Cloud từ chối workspace cá nhân trước outbound; dữ liệu biên tập, media và path nằm local. Riêng thao tác **Dịch Cloud** chủ động gửi snapshot text có giới hạn vào job server, mã hóa và dọn theo retention; không upload video/audio/path.
- System Setup sau đăng nhập/license/organization hiển thị dashboard trước rồi dùng modal kiểm FFmpeg/OCR/Qwen/Piper cho Owner/OrganizationAdmin/BillingManager/Member; Viewer không thuộc gate. Khi thiếu component bắt buộc, React khóa nền và host C# từ chối command nghiệp vụ; chỉ mở sau khi mọi component không `DISABLED` được kiểm `READY`. `NOT_INSTALLED`, checksum sai hoặc probe hỏng phải có luồng cài/sửa/thử lại, không biến thành READY do một cờ UI.
- Workspace thuộc exact organization + owner; COPY sao chép/hash atomically, LINK phát hiện source mất/đổi.
- Playback dùng virtual HTTPS URL và HTTP Range, không lộ absolute path; mọi mutation dùng track revision.
- Cue manual/locked không bị job ghi đè. Local job có state/checkpoint/pause/resume/retry/cancel và recovery.
- OCR local chỉ chạy khi session/license/membership/role/owner hợp lệ; Viewer bị chặn.
- Panel **Thiết lập dự án** chỉ hiển thị ba tác vụ **Quét OCR**, **Dịch tiếng Việt** và **Tạo giọng Việt**; không lặp lại card video nguồn, track phụ đề, revision hay trạng thái runtime đã sẵn sàng. **Quét OCR** mở popup có video hiện tại, điều khiển phát/tua và khung chỉnh vùng subtitle cứng; quét thử phải dùng đúng timestamp đang chọn. **Dịch tiếng Việt** mở popup chọn Local hoặc Cloud. Cloud dùng readiness server độc lập Local, bấm một lần để lưu draft và tạo job; không có model picker/API key. Thiếu flag/config/quyền/ngân sách thì khóa Cloud với lý do an toàn.
- **Thiết kế phụ đề** chỉ mở khi dự án có video phát được. Người dùng có thể tua video, ẩn/hiện phụ đề, chọn chế độ vừa khung/lấp đầy, thu phóng, lật độc lập hình video trái–phải/trên–dưới (hoặc cả hai), chọn preset rồi chỉnh font trong allowlist, màu/độ trong suốt, viền, bóng, nền, căn chữ, chiều rộng và số dòng mục tiêu. Lật hình không lật chữ phụ đề hoặc đổi âm thanh/tỷ lệ video. Vị trí phụ đề được kéo trực tiếp trên video hoặc tinh chỉnh bằng phím mũi tên và lưu theo phần trăm của content box. Cùng modal có bộ trộn hai kênh để chỉnh riêng âm thanh gốc `0–100%`, giọng dịch `0–150%`, tắt/bật từng kênh và bật tự giảm âm gốc khi có tín hiệu giọng Việt. Bản nháp phụ đề/âm thanh/lật hình chỉ tác động preview; phải bấm **Lưu thay đổi** mới ghi theo project. Khi đóng lúc còn thay đổi, UI phải xác nhận bỏ bản nháp. Preview thành phẩm và adapter render phải dùng cùng cấu hình đã lưu.
- Phần đầu card **Biên tập phụ đề** đặt tiêu đề và nhóm nút trên cùng một hàng; số câu, trạng thái dịch và cảnh báo nằm ở hàng nhỏ bên dưới. Không lặp thêm nhãn PHỤ ĐỀ phía trên tiêu đề.
- Nút **Xuất video** nằm ngay cạnh **Thiết kế phụ đề** và menu **Tệp phụ đề** trên thanh công cụ biên tập, đồng thời có thêm nút ở cuối thanh công cụ Timeline. Hai nút dùng chung luồng lưu các chỉnh sửa phụ đề đang chờ trước khi mở hộp thoại chọn nơi lưu MP4; lưu lỗi thì dừng để người dùng sửa. Cả hai cùng có trạng thái **Đang xuất…**, khóa bấm lặp kể cả khi bấm xen kẽ và dùng cùng chức năng xuất với **Xuất MP4** trong thiết kế thành phẩm. Thiếu video/track hoặc đang xử lý thao tác khác thì khóa nút; nhãn xuất luôn hiển thị ở panel hẹp.
- Xuất MP4 phải lưu bản nháp hợp lệ trước, snapshot media/track/revision/style/cấu hình trộn/lật hình và timeline giọng Việt hiện hành, tạo ASS từ bản dịch rồi render local bằng FFmpeg. Khi có lật hình, áp bộ lọc lật trước bộ lọc chèn phụ đề để chữ giữ nguyên chiều. Khi timeline giọng Việt hợp lệ và kênh này không bị tắt, FFmpeg trộn nó với âm thanh gốc; tùy chọn tự giảm nền dùng voice làm sidechain và output luôn qua limiter chống vỡ tiếng. Nếu chưa tạo timeline giọng Việt thì vẫn được xuất với âm gốc theo cấu hình. Output được ghi vào tệp `.partial`, kiểm tra lại video/audio stream, kích thước và thời lượng bằng FFprobe và chỉ publish khi toàn bộ snapshot vẫn còn hiện hành. Không được ghi đè video nguồn hoặc gửi phụ đề/âm thanh ra Cloud.

### Dịch ngữ cảnh Qwen

- Nút **Dịch tiếng Việt** luôn hiện; thiếu active OCR track có cue phải yêu cầu quét OCR và không tạo job.
- Chỉ nhận source `PADDLE_OCR_LOCAL`, language `en`/`zh`, cue tồn tại và revision khớp.
- LLamaSharp/Qwen chạy trong worker x64 riêng qua IPC giới hạn; không có Cloud client, credential hoặc workflow database.
- READY phải khớp model/worker/protocol/config/backend/native fingerprint và probe runtime/English/Chinese.
- Resource warning RAM/commit cần người dùng xác nhận và snapshot theo job; không bỏ qua platform, disk, checksum/probe hoặc OOM thật.
- Output stale/invalid không apply; translation memory/cache tách theo fingerprint/profile và SRT ghi atomically.
- `VietsubLocalTranslationEnabled=false` cho tới khi model integration, benchmark và smoke desktop đạt; `Skipped` không phải pass.

### Tạo giọng Việt local theo giọng đã chọn

- Dự án lưu đúng một giọng local đã chọn trong `VoiceSettings`: Piper VAIS-1000 hoặc một trong 14 Kokoro Vietnamese voicepack đã pin. Người dùng nghe thử/chọn/cài hoặc kiểm tra runtime trong modal rồi tạo giọng từ các cue đã bật và đã dịch. Job snapshot engine/model/version/voice; nếu giọng đã chọn thiếu model hoặc chưa qua probe WAV trên máy hiện hành thì không bắt đầu. Không fallback sang Piper khi Kokoro lỗi. Đổi giọng làm timeline của giọng trước mất hiệu lực cho preview và export; job local đang hoạt động phải kết thúc trước khi đổi giọng.

- Chỉ track hiện hành có revision khớp và các cue được bật tạo giọng đều có bản dịch tiếng Việt mới được tạo giọng; warning chất lượng dịch không chặn. Mặc định mọi cue được bật; người dùng có thể bỏ qua/bật lại từng câu trong menu Thao tác hoặc menu chuột phải trên timeline mà không xóa phụ đề hay đổi timing. Danh sách không còn thanh chọn hàng loạt hoặc checkbox chọn câu; API/storage vẫn hỗ trợ danh sách ID. Cue bỏ qua không gửi tới Piper và ngắt cụm đọc. Hệ thống cố gắng fit giọng câu trước đến đầu câu bỏ qua; nếu không đủ thời gian, vẫn giữ phần giọng tràn và hoàn thành tác vụ.
- Lựa chọn tạo giọng lưu tại `subtitle_cues.voice_enabled` trong SQLite schema 6; nâng từ schema 5 đặt mặc định bật cho dữ liệu cũ. Thay đổi theo batch phải khớp active track/revision, không có job local đang hoạt động và tăng revision một lần; split/duplicate kế thừa lựa chọn của câu gốc.
- Đổi lựa chọn làm timeline giọng revision cũ mất hiệu lực. Chỉ dựng lại bằng cache khi các phrase phủ đủ những câu đang bật, không chứa câu bị bỏ qua và còn khớp nội dung/cấu hình/hash. Nếu cache không đủ, UI báo **Cập nhật giọng Việt**; không phát hoặc xuất âm giọng cũ. Bỏ qua toàn bộ câu cho phép xuất phụ đề và âm gốc theo mixer. Khi đã từng có giọng nhưng lựa chọn hiện tại chưa có timeline hợp lệ, xuất MP4 phải yêu cầu cập nhật giọng hoặc tắt kênh giọng Việt.
- Piper và Kokoro CPU chạy trong Python worker riêng với model/config/runtime đã pin; không nhận provider credential, URL tùy ý hoặc output path từ WebView.
- Phrase/WAV/timeline cache theo content/config/revision. File `.partial` phải qua RIFF/PCM, size và SHA-256 trước promote.
- Timeline giọng Việt là track riêng dùng chung playhead/play/pause/seek/rate với video, không dùng audio player độc lập.
- Chỉnh timing nhưng giữ nguyên cue/nội dung không được làm mất voice: trường hợp chỉ nới `end` được chuyển tiếp WAV đã xác minh sang revision mới; các thay đổi timing khác dựng lại timeline bằng FFmpeg từ phrase WAV cache cùng giọng đã chọn, không gọi lại synthesizer hoặc tải model. Đổi nội dung/speaker hay cấu trúc cue vẫn làm timeline cũ mất hiệu lực.
- Hệ thống mượn khoảng trống kế tiếp và tăng tốc tối đa `1.20x`; phrase dài hơn vẫn publish ở tốc độ tối đa, giữ diagnostic và không cắt phần tiếng nói. Nếu tràn vào câu bỏ qua, có thể rút tối đa 120 ms đuôi WAV đã được bộ phân tích xác định nằm sau tín hiệu tiếng nói, giữ thêm 5 ms đệm; không sửa WAV phrase gốc. Thiếu khoảng lặng thì giữ phần tràn, lưu diagnostic `REVIEW_REQUIRED` không chặn và hoàn thành tạo giọng bình thường. Quy tắc này dùng chung cho tạo mới, thử lại và dựng lại cache, kể cả câu bắt đầu trong vùng bỏ qua.
- `VietsubLocalVoiceEnabled=true` làm workflow cài đặt/ trạng thái hiển thị. Thiếu component phải trả `NOT_INSTALLED`; chỉ trả READY sau checksum/probe và vẫn cần legal review, benchmark, nghe nghiệm thu, smoke trước production.

## 13A. Đăng video TikTok

- Chức năng là item độc lập theo user, không phụ thuộc organization/project. Mỗi user có thể quản lý nhiều kết nối TikTok sau khi bật `TikTok:MultiAccountEnabled`; mỗi bài đăng phải chọn một `ConnectionId` thuộc user hiện hành.
- Kết nối tài khoản phải dùng Login Kit Desktop OAuth + PKCE và trình duyệt hệ thống. Không nhận cookie, browser session, access token hoặc refresh token do người dùng tự nhập.
- Server giữ TikTok app secret và mã hóa token theo user; desktop không nhận các giá trị này. Chuyển tài khoản không ngắt kết nối hoặc đổi tài khoản của job cũ. Kết nối lại phải khớp danh tính TikTok/app đã lưu; đăng nhập nhầm tài khoản phải bị từ chối.
- Chỉ Global Admin quản lý TikTok Developer App credential. Credential mới được mã hóa trên server ở trạng thái `Pending`, response chỉ có hint; một OAuth code exchange thật do chính Admin yêu cầu xác minh thực hiện mới được chuyển sang `Active`.
- Yêu cầu xác minh có hạn 15 phút và tạm khóa integration. Không được xoay credential khi còn kết nối TikTok chưa thu hồi hoặc publish job chưa terminal; kích hoạt credential mới luôn reset xác nhận public posting về false.
- File video, absolute path và preview chỉ ở desktop. Server chỉ nhận metadata cần để khởi tạo Direct Post; desktop upload trực tiếp đến exact HTTPS TikTok upload host bằng signed URL tạm thời.
- Trước mỗi bài đăng phải query creator info mới; privacy bắt buộc do người dùng chọn, interaction mặc định bỏ chọn và không được bật khi TikTok cấm. Người dùng phải xác nhận consent/disclosure liên quan.
- `ClientRequestId` ổn định theo lần đăng, tách khỏi `MediaId`; retry cùng ID chỉ chấp nhận cùng tài khoản và payload. Mỗi lần khởi tạo được ghi bền vững trước outbound. Timeout không rõ kết quả không được tự khởi tạo lại; người dùng kiểm tra trên TikTok trước khi chủ động chuẩn bị lần đăng mới.
- Desktop chỉ upload một video tại một thời điểm; sau upload có thể chọn tài khoản khác trong khi server tiếp tục theo dõi nhiều job. Mở lại desktop đọc các job đang chạy và lịch sử; có nhiều tài khoản thì yêu cầu chọn rõ tài khoản nhận bài.
- Ngắt một tài khoản chỉ xóa quyền của kết nối đó trên server và dừng theo dõi các job chưa terminal của nó; giữ metadata lịch sử. Video đã gửi vẫn có thể tiếp tục được TikTok xử lý. Không tuyên bố thao tác ngắt là xóa hoặc hủy bài trên TikTok.
- Khi chuyển tài khoản, lấy lại creator info, bỏ privacy/consent/disclosure và lựa chọn tương tác cũ. Lịch sử dùng tên tài khoản snapshot lúc tạo job; dữ liệu cũ thiếu snapshot không được tự suy đoán.
- Không tự fallback sang automation trình duyệt. Public posting chỉ được bật sau app review, quyền `video.publish`, audit Content Posting API và nghiệm thu sandbox/production phù hợp.
- Bật public posting cần Global Admin xác nhận rõ ràng và lưu metadata bằng chứng audit; không có xác nhận này thì chỉ cho phép `SELF_ONLY`.

## 13B. Tải video Bilibili

- Chức năng độc lập trong menu desktop, theo phiên người dùng và cần license/session hợp lệ; không thuộc organization/project AI, không reserve budget hay gọi provider AI.
- Link video/kênh → quét metadata → người dùng chọn video/chất lượng/thư mục → hàng đợi tải local. Quét đủ các trang khả dụng; lỗi, hủy hoặc giới hạn phải ghi rõ chưa đầy đủ và giữ kết quả đã lấy được.
- Chỉ hỗ trợ video công khai không cần đăng nhập. Không đọc cookie/trình duyệt hoặc nhập credential Bilibili. Chất lượng là mức tối đa theo nguồn public; MP4 phải có video/audio/thời lượng hợp lệ trước khi hoàn tất.
- Hủy/thử lại theo đúng job; không tự tải toàn bộ danh sách chưa được chọn. File tạm qua kiểm size/signature/hash/probe trước promote; không ghi đè file có sẵn. Hàng đợi thuộc phiên hiện tại; đóng app hủy tác vụ local.
- Xem [triển khai và giới hạn](TRIEN_KHAI_TAI_VIDEO_BILIBILI.md).

## 14. SePay và seat

- SePay mặc định tắt trong SQL. Global Admin quản lý tài khoản nhận tiền và bật/tắt nhận đơn mới tại **Thanh toán SePay**; thêm/chọn tài khoản không tự bật thanh toán. Payment snapshot amount/account/content/expiry và xử lý webhook idempotent.
- Thay tài khoản được chọn hoặc sửa số tài khoản chỉ áp dụng cho đơn mới. Đơn cũ, QR và webhook đối chiếu thông tin snapshot của đơn, kể cả khi nhận đơn mới đã tắt.
- Luồng hiện hành theo yêu cầu sản phẩm không dùng key/chữ ký xác thực webhook. Endpoint giới hạn dung lượng/tần suất và kiểm dữ liệu thanh toán; không xác minh nguồn gửi. Cấu hình nhận tiền chỉ Global Admin được sửa, có version chống ghi đè và audit không chứa số tài khoản.
- Chỉ match giao dịch vào đúng account/code/exact amount và trạng thái; dữ liệu mơ hồ chuyển thủ công.
- Pool/seat allocation dùng khóa cạnh tranh, không vượt capacity. Replay webhook không cấp license/seat hai lần.
- Late payment, refund/chargeback và provisioning failure phải có trạng thái đối soát; không sửa ledger ad-hoc.

## 15. Dữ liệu, output và bảo mật

- Desktop chỉ đọc/ghi workflow `vf` trong giai đoạn chuyển tiếp; server là nguồn sự thật cho credential, cloud request và usage.
- Provider outbound chỉ qua HTTPS exact allowlist. Output proxy xác minh auth/ownership/scheme/host/DNS/redirect/MIME/size và không lộ signed URL.
- Path local phải chuẩn hóa, tương đối trong workspace root và chống traversal/reparse escape.
- Không log secret, Authorization, signed URL, Base64, prompt/transcript nhạy cảm hoặc absolute local path.

## 16. Update và release

- Server lưu metadata/artifact release. Setup/updater kiểm tra size, SHA-256, traversal, package root và bundle FFmpeg.
- Update bảo vệ appsettings, workspace và WebView2 user data; thay file có backup/rollback.
- Publish Release bị chặn nếu FFmpeg provenance chưa có `Approval scope: Release`.

## 17. API error và tương thích

- Lỗi nghiệp vụ dùng HTTP status phù hợp và `ApiErrorResponse` có code ổn định; không trả raw exception/provider response/secret.
- `401` từ API authenticated làm desktop xóa phiên; `403` license/role không mặc định là token hỏng.
- Lỗi polling/cache không được biến thành submit hoặc settlement mới.
- Project/output lịch sử tiếp tục đọc được theo compatibility policy; không xóa entity legacy chỉ vì không thấy call site trực tiếp.

## 18. Điều kiện nghiệm thu

- Contract/source/migration/tài liệu đồng bộ; migration idempotent và least-privilege đạt trên clone.
- Cross-user/cross-org/Viewer, thiếu rate/budget/credential/policy bị chặn trước outbound.
- Idempotency, polling restart, settlement và output proxy đạt test.
- Build/test đạt trên commit phát hành; Failed/Skipped được giải thích.
- Integration/model/provider thật có smoke trên đúng môi trường/bundle và chi phí được phê duyệt.
- Monitoring, rollback và runbook khả dụng; không có secret/dữ liệu nhạy cảm trong source/log/artifact.

## Admin thiết lập mở bán 3 bước — cập nhật 2026-10-14

Luồng đơn giản: Kết nối AI → Chi phí & khách hàng → Tạo gói & mở bán. SePay giữ trang riêng. Nhóm mới chưa nhận khách trước publication; activation, plan, mapping và receipt cùng Serializable transaction. OpenAI Text/Image dùng default thực tế toàn hệ thống, Veo dùng policy LongForm của tổ chức. Giá AI USD là global và cần xác nhận khi thay đổi; giá gói VND là độc lập. Gói wizard có profile OpenAI nội dung/ảnh + Fal/Veo, được kiểm lại trên offers/reserve và giữ khi clone; không áp profile này lên mọi gói legacy. Ngừng bán dùng active/private để giữ license và heartbeat của khách hiện có. Xem [biên bản triển khai](TRIEN_KHAI_ADMIN_SETUP_3_BUOC.md) cho trạng thái kiểm thử và giới hạn môi trường.
