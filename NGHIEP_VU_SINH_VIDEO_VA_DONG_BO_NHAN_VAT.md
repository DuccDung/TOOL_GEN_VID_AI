# Nghiệp vụ dự án nhiều cảnh và đồng bộ nhân vật

> Cập nhật ngữ cảnh: 2026-08-31. Tài liệu này mô tả chi tiết luồng dự án nhiều cảnh đang có trong source. Đây là tài liệu bổ sung cho [Nghiệp vụ hệ thống VideoMaker](NGHIEP_VU_HE_THONG_VIDEOMAKER.md); khi có khác biệt, tài liệu nghiệp vụ hệ thống và source/migration hiện hành được ưu tiên.

## 1. Mục tiêu và phạm vi

Luồng này biến một chủ đề thành video nhiều cảnh có nội dung, nhân vật và hình ảnh nhất quán:

```text
Chọn tổ chức và tạo project
  → OpenAI sinh content plan có cấu trúc
  → người dùng duyệt/chỉnh hồ sơ nhân vật và storyboard
  → tạo hoặc chọn ảnh chuẩn, rồi khóa nhân vật
  → server sinh clip theo video policy đã snapshot cho project
  → worker polling và cache output
  → desktop tải, kiểm tra, nghe và duyệt từng clip
  → FFmpeg dựng video cuối từ đúng clip đã duyệt
```

Luồng **Tạo video ngắn** một scene, direct prompt và không qua OpenAI được mô tả riêng tại [NGHIEP_VU_TAO_VIDEO_NGAN_KLING.md](NGHIEP_VU_TAO_VIDEO_NGAN_KLING.md).

## 2. Bất biến bắt buộc

- Mọi request OpenAI/Kling/BytePlus đi qua `TOOL-SERVER`; desktop không giữ key và không gọi provider trực tiếp.
- Desktop không chọn provider/model và không gửi prompt, thời lượng, độ phân giải hoặc Native Audio làm nguồn sự thật.
- Server phải kiểm tra JWT, session, device, license lease, organization membership, role và quyền sở hữu project trước outbound.
- `Viewer` không được phát sinh chi phí AI.
- Thiếu credential, rate, budget hoặc capability phải dừng trước outbound; không tự đoán giá và không fallback giữa provider.
- Project giữ snapshot provider/model/policy bất biến từ lần dùng video đầu tiên.
- Clip chỉ được duyệt sau khi desktop đã tải, kiểm tra media và người dùng đã nghe/xác nhận nếu output có âm thanh.
- Final render chỉ dùng `SceneVideo` thuộc đúng `ApprovedGenerationId` của scene hiện hành.
- Không fallback ngầm sang TTS/WAV khi Native Audio lỗi hoặc im lặng.

## 3. Ranh giới trách nhiệm

### `TOOL-SERVER`

- Xác thực, tổ chức, RBAC, license và quyền sở hữu dữ liệu.
- Credential mã hóa, provider/model catalog và video policy tổ chức.
- Pricing, budget reservation, settlement/release, usage ledger và audit.
- OpenAI structured content, GPT-Image-2 và gateway video đa provider.
- Prompt composer theo provider, worker polling, cache/cleanup và output proxy an toàn.

### `TOOL-LOCAL`

- Tạo project/workspace và quản lý dữ liệu workflow trong giai đoạn chuyển tiếp.
- Hiển thị content, hồ sơ nhân vật, storyboard, preview và trạng thái generation.
- Tạo/chọn ảnh tham chiếu, kiểm tra SHA-256, khóa nhân vật và gửi request trung lập provider.
- Tải output qua proxy vào file `.part`, xác minh media, đo audio và cho người dùng duyệt.
- Render cục bộ bằng FFmpeg/FFprobe; retry render không gọi lại provider.

Desktop vẫn kết nối SQL trực tiếp cho một phần dữ liệu workflow trong giai đoạn chuyển tiếp. Đây không phải quyền ghi vào các bảng sự thật về credential, provider request, reservation hoặc usage.

## 4. Điều kiện trước khi dùng AI

Một thao tác generation chỉ được phép khi đồng thời thỏa:

1. User đăng nhập, session/device claim và license lease còn hiệu lực.
2. User là thành viên `Active` của tổ chức đang chọn và project thuộc đúng tổ chức/user.
3. Role là `Owner`, `OrganizationAdmin`, `BillingManager` hoặc `Member`.
4. Provider/model cần dùng đang Active; credential Active đã được test.
5. Có đủ rate Active đúng usage type/capability.
6. Budget tổ chức lớn hơn `0`, còn đủ tiền; member limit còn đủ nếu được cấu hình.
7. Idempotency key hợp lệ và không xung đột payload.
8. Với video, FFmpeg/FFprobe cục bộ phải sẵn sàng trước outbound để tránh tạo clip có phí nhưng không thể xử lý.

## 5. Content plan và version

Desktop gửi `projectId`, `organizationId` và idempotency key. Server đọc topic, language, platform, aspect ratio và target duration từ project, sau đó gọi OpenAI Responses API bằng structured output.

Content plan gồm tối thiểu:

- tiêu đề/tóm tắt và concept;
- script hiện hành;
- style profile;
- tối đa một nhân vật lặp lại trong phạm vi tự động hiện tại;
- scene plan theo thứ tự;
- visual prompt và negative prompt;
- `speech_mode`, `spoken_text`, speaker, voice style, ambience và sound effects.

Giới hạn hiện hành:

- Project có thể được tạo với thời lượng 5–3.600 giây, nhưng luồng OpenAI tự động hiện chỉ hỗ trợ tổng thời lượng tối đa 360 giây.
- Scene do content planner tạo nằm trong khoảng 3–30 giây để phù hợp catalog đa provider; thời lượng hợp lệ cuối cùng còn phụ thuộc model snapshot của project.
- Một scene tự động có tối đa một `character_key`; thoại trước camera yêu cầu đúng một nhân vật.

Kết quả mới tạo version mới cho concept/script/style/scene plan. Version cũ được giữ để audit; server và desktop phải dùng version hiện hành, không ghi đè lịch sử đã duyệt.

Sinh lại content không được âm thầm thay thế scene đang có request provider hoạt động hoặc clip đã duyệt. Nếu muốn đổi hướng nội dung sau khi phát sinh clip, người dùng phải tạo version/attempt mới theo trạng thái cho phép.

## 6. Hồ sơ và ảnh nhân vật

### 6.1. Vòng đời

```text
Draft → Approved → Superseded
```

- `Draft`: được sửa hồ sơ, thay hoặc sinh lại ảnh.
- `Approved`: hồ sơ và primary reference bị khóa cho các scene hiện hành.
- `Superseded`: version cũ chỉ còn phục vụ truy vết dữ liệu đã tạo.

Không sửa trực tiếp version đã `Approved`. Muốn thay đổi nhân vật phải tạo version mới và duyệt lại.

### 6.2. Nguồn ảnh tham chiếu

Nhân vật Draft hỗ trợ hai nguồn:

- ảnh người dùng chọn từ máy và lưu vào workspace;
- ảnh do server tạo bằng `openai/gpt-image-2` từ hồ sơ nhân vật đã lưu.

Với ảnh GPT-Image-2:

1. Desktop chỉ gửi project/character/organization/idempotency, không gửi prompt tùy ý.
2. Server tự dựng prompt, giữ ngân sách và tạo đúng một PNG 1024×1024 quality medium.
3. Binary tạm được kiểm tra MIME, kích thước và SHA-256; desktop tải qua URL tương đối có xác thực.
4. Desktop ghi `.part`, kiểm tra lại hash rồi tạo `MediaAsset` và primary `CharacterReference` mới.
5. Sinh ảnh không tự khóa nhân vật; người dùng phải xem và bấm khóa thủ công.

Với BytePlus Seedance, chỉ primary reference có `SourceType=Generated`, do OpenAI trong hệ thống tạo và đã được duyệt mới được gửi provider. Ảnh upload hoặc ảnh người thật bị chặn trước outbound. Kling tiếp tục hỗ trợ reference tương thích theo capability model.

### 6.3. Ánh xạ nhân vật vào scene

- Scene không có nhân vật không gửi reference.
- Scene có nhân vật phải trỏ đến đúng character version đã khóa và primary reference hiện hành.
- Server kiểm tra lại character–scene mapping, MIME, size và SHA-256; dữ liệu từ desktop không được thay thế hồ sơ đã duyệt.
- Provider request ghi snapshot character version/reference ID để kết quả cũ vẫn truy vết được sau khi có version mới.

## 7. Storyboard và chỉnh scene

Storyboard hiển thị theo thứ tự scene: mô tả hình ảnh, prompt, lời nói/voice-over, ambience/SFX, nhân vật, thời lượng, preview và trạng thái generation.

Chỉ cho sửa scene khi:

- scene thuộc scene-plan hiện hành;
- chưa có `ApprovedGenerationId`;
- không có provider request đang Active hoặc đã `Completed` chờ tải/xử lý;
- nội dung mới vượt qua validate speech mode, character mapping và word budget.

Chỉnh scene tạo prompt version mới, cập nhật speech intent và làm vô hiệu snapshot cũ. Không được sửa một scene rồi âm thầm áp dụng thay đổi cho scene khác.

## 8. Sinh clip đa provider

1. Desktop chọn scene hợp lệ và chuẩn bị ảnh primary từ workspace nếu cần.
2. Desktop gửi `SubmitVideoRequest` gồm project ID, scene ID, idempotency key, organization ID, scene-plan version, prompt version và ảnh reference nếu có.
3. Server đọc project/scene/prompt/speech intent từ database, resolve hoặc tạo snapshot video policy của project.
4. Server kiểm tra provider/model/capability/credential/rate/budget và quyền; sau đó route qua Kling hoặc BytePlus adapter.
5. Không fallback tự động giữa provider. Retry hợp lệ tạo attempt provider mới nhưng giữ nguyên snapshot project.
6. Worker server sở hữu polling và tiếp tục chạy khi desktop đóng.
7. Khi provider hoàn tất, server cache output, xác minh host/MIME/size/hash và chỉ trả URL `/api/generation/videos/{providerRequestId}/content`.

Các giới hạn thời lượng theo catalog hiện hành:

- Kling 3.0: 3–15 giây, 720p, Native Audio.
- Seedance 2.0: 4–15 giây, 720p, Native Audio.
- Seedance 2.5: 4–30 giây, 720p, Native Audio.

Provider/model BytePlus được seed disabled. Chỉ Global Admin/Owner vận hành mới bật sau migration, credential, rate, budget và smoke test có phê duyệt chi phí.

## 9. Native Audio và duyệt clip

Mỗi scene có `SpeechMode`:

- `None`: không có lời nói chính thức; vẫn có thể có ambience/SFX.
- `OnCameraDialogue`: đúng một nhân vật nói, yêu cầu speaker và lip-sync.
- `NativeVoiceOver`: lời dẫn native ngoài khung hình.

Server dựng prompt từ speech intent đã lưu; desktop không gửi raw speech làm nguồn sự thật. Với Kling, word budget hiện hành là tối đa 8 từ cho 3–5 giây, 18 từ cho 6–10 giây và 28 từ cho 11–15 giây. Vượt giới hạn phải chặn trước outbound, không tự cắt lời.

Sau khi tải output:

1. Desktop kiểm tra video stream, thời lượng, kích thước, SHA-256 và audio stream.
2. Audio thiếu hoặc gần như im lặng chuyển scene/generation sang `NativeAudioInvalid`; clip không thể duyệt.
3. Audio nghe được chuyển sang `AudioReviewRequired`.
4. Người dùng phải phát preview và xác nhận đã nghe, sau đó bấm **Duyệt hình và âm thanh**.
5. Chỉ lúc đó scene mới nhận `ApprovedGenerationId` và trạng thái `Approved`.

Tiếng Việt Native Audio hiện là experimental/best-effort. Kiểm tra tự động chỉ xác nhận có âm thanh nghe được, không chứng minh lời đúng, phát âm đúng hoặc lip-sync đạt; manual review là bắt buộc.

Workflow mặc định không gọi OpenAI Speech, không tải WAV, không tạo `SceneVoice`, không mix/ducking và không tạo `SceneVideoNarrated`. Schema/API TTS được giữ cho tương thích và phát triển tương lai, không phải đường gọi Active.

## 10. Trạng thái chính

Project đi qua các mốc nghiệp vụ:

```text
Draft → ContentPlanning → ScenePlanning → GeneratingScenes
      → ScenePlanning (còn scene chưa duyệt)
      → ReadyToRender → Rendering → AwaitingFinalApproval
```

Render lỗi đưa project về `ReadyToRender` để retry bằng asset cục bộ, không tạo request AI mới.

Scene/video generation có thể đi qua:

```text
PromptReady → WaitingProvider → Generated/Downloading
            → NativeAudioInvalid
            → AudioReviewRequired → Approved
            → Failed/Cancelled/Expired
```

`Completed` của provider request chỉ có nghĩa provider/server đã hoàn tất; không đồng nghĩa scene đã được desktop tải và người dùng duyệt.

## 11. Final render

`ProjectRenderService` chỉ nhận scene-plan hiện hành khi mọi scene có `ApprovedGenerationId`. Với từng scene, service phải tìm đúng `SceneVideo` của generation được duyệt, xác minh hash và cờ audio, chuẩn hóa rồi nối theo thứ tự.

Final MP4 chỉ được ghi nhận khi có video stream, audio stream nghe được đối với workflow nhiều cảnh Native Audio, đúng kích thước và thời lượng trong tolerance. Render/mix lỗi chỉ retry từ asset đã có; không gọi lại OpenAI, Kling, BytePlus hoặc TTS.

## 12. Idempotency, retry và chi phí

- Idempotency key nằm trong phạm vi organization.
- Cùng key và cùng hash trả request/kết quả cũ.
- Cùng key nhưng khác nội dung trả `idempotency_key_conflict`.
- Content, image và video có operation key/ledger riêng.
- Reservation được tạo trước outbound bằng transaction `Serializable` và dùng rate snapshot.
- Provider thành công được settlement theo actual usage khi có; request terminal thất bại/cancel/expired được release theo nghiệp vụ.
- Retry tải, kiểm tra audio hoặc render cục bộ không tạo usage mới.
- `NativeAudioInvalid` cần attempt video mới nếu người dùng muốn sinh lại, không tái sử dụng idempotency key của request terminal cũ với payload khác.

## 13. Lỗi cần hiển thị có hướng dẫn

| Mã/nhóm lỗi | Hướng xử lý nghiệp vụ |
|---|---|
| `organization_generation_denied` | Kiểm tra membership và role; Viewer không được tạo AI. |
| `organization_budget_exceeded` | Owner/BillingManager kiểm tra budget và member limit; budget `0` là khóa AI. |
| `pricing_not_configured` | Global Admin nhập đủ rate đúng model/capability trước khi thử lại. |
| `provider_credential_missing` / `provider_credential_invalid` | Owner/OrganizationAdmin lưu hoặc rotate credential và test lại. |
| `project_video_policy_missing` / `video_provider_not_ready` | Cấu hình video policy, model, credential và rate của tổ chức. |
| `character_image_source_not_allowed` | Với BytePlus, dùng ảnh Generated trong hệ thống đã duyệt. |
| `scene_snapshot_stale` / `prompt_version_conflict` | Tải lại project/storyboard và tạo request từ version hiện hành. |
| `NativeAudioInvalid` | Nghe/kiểm tra clip, sửa prompt nếu cần và tạo attempt mới. |
| `media_tool_not_ready` | Cài/sửa FFmpeg/FFprobe rồi tiếp tục tải request đã hoàn tất; không submit lại provider. |

UI phải chuyển lỗi nội bộ thành thông báo có thể hành động; không để exception thô làm dừng desktop.

## 14. Điều kiện vận hành và giới hạn hiện tại

- Migration 4.0.0–4.0.6 có trong source nhưng không mặc định được coi là đã chạy trên database thật.
- Kling là policy mặc định. BytePlus chỉ sẵn sàng sau rollout có kiểm soát.
- Desktop SQL trực tiếp cho workflow là kiến trúc chuyển tiếp; server mới là nguồn sự thật cho credential/provider request/usage.
- Media bundle dev hiện không tự động đủ điều kiện phát hành production; release cần hồ sơ/license approval đúng scope.
- Không tự chạy migration production, rotate credential, nhập giá đoán hoặc gọi smoke test có phí.

## 15. Điểm vào source trước khi thay đổi

- Contracts: `TOOL-SHARED.Contracts/Generation/GenerationContracts.cs`.
- Gateway: `TOOL-SERVER/Generation/GenerationService.cs`.
- Provider policy: `TOOL-SERVER/Generation/VideoModelPolicy.cs`.
- Provider adapters: `KlingVideoClient.cs`, `BytePlusVideoClient.cs` và prompt composer tương ứng.
- Worker/cache/proxy: `KlingPollingWorker.cs`, `KlingOutputProxyService.cs` và `GeneratedVideoCleanupWorker.cs`.
- Desktop orchestration: `TOOL-LOCAL/Generation/ProjectGenerationService.cs`.
- Character/storyboard: `TOOL-LOCAL/Projects/ProjectService.cs`.
- Final render: `TOOL-LOCAL/Projects/ProjectRenderService.cs` và `Media/FfmpegRenderService.cs`.
- Migration trạng thái hiện hành: `database/VideoFactory.4.0.6.NativeAudioWorkflowStatuses.sql`.
- Runbook vận hành: `TRIEN_KHAI_AI_GATEWAY_TO_CHUC.md`.
