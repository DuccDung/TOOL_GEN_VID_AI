# Nghiệp vụ dự án nhiều cảnh, đồng bộ nhân vật và tài sản

> Cập nhật ngữ cảnh: 2026-09-01. Tài liệu này mô tả chi tiết luồng dự án nhiều cảnh đang có trong source. Đây là tài liệu bổ sung cho [Nghiệp vụ hệ thống VideoMaker](NGHIEP_VU_HE_THONG_VIDEOMAKER.md); khi có khác biệt, tài liệu nghiệp vụ hệ thống và source/migration hiện hành được ưu tiên.

## 1. Mục tiêu và phạm vi

Luồng này biến một chủ đề thành video nhiều cảnh có nội dung, nhân vật và hình ảnh nhất quán:

```text
Chọn tổ chức và tạo project
  → OpenAI sinh content plan có cấu trúc
  → desktop lưu version content/scene-plan, server materialize tài sản text theo `asset_key`
  → người dùng duyệt/chỉnh hồ sơ nhân vật và storyboard
  → tạo hoặc chọn ảnh chuẩn, rồi khóa nhân vật
  → xác nhận tài sản ngay trên từng card cảnh
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
- Continuity asset chỉ có hiệu lực trên scene được gắn; nếu scene có tài sản thì phải có đúng một `Background`, còn `Prop`/`Item` là tùy chọn.
- Video chỉ dùng `ProjectAssetVersion` đã khóa. Xác nhận tài sản theo cảnh phải nguyên tử và không được phát sinh provider request hoặc usage.
- Clip chỉ được duyệt sau khi desktop đã tải, kiểm tra media và người dùng đã nghe/xác nhận nếu output có âm thanh.
- Final render chỉ dùng `SceneVideo` thuộc đúng `ApprovedGenerationId` của scene hiện hành.
- Không fallback ngầm sang TTS/WAV khi Native Audio lỗi hoặc im lặng.

## 3. Ranh giới trách nhiệm

### `TOOL-SERVER`

- Xác thực, tổ chức, RBAC, license và quyền sở hữu dữ liệu.
- Credential mã hóa, provider/model catalog và video policy tổ chức.
- Pricing, budget reservation, settlement/release, usage ledger và audit.
- OpenAI structured content, GPT-Image-2 và gateway video đa provider.
- Materialize, version hóa, preflight và xác nhận thư viện text `Background`/`Prop`/`Item` theo scene.
- Prompt composer theo provider, worker polling, cache/cleanup và output proxy an toàn.

### `TOOL-LOCAL`

- Tạo project/workspace và quản lý dữ liệu workflow trong giai đoạn chuyển tiếp.
- Hiển thị content, hồ sơ nhân vật, tài sản text, storyboard, preview và trạng thái generation.
- Tạo/chọn ảnh tham chiếu, kiểm tra SHA-256, khóa nhân vật và gửi request trung lập provider.
- Cho phép xác nhận tài sản ngay trên card cảnh; trình chọn chi tiết và thao tác đồng bộ/khóa theo lô nằm ở luồng nâng cao.
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

Nếu snapshot video của project là Kling, workflow `OpenAiStructuredPlan` dùng ngôn ngữ nội dung hiệu lực `vi-VN` bất kể ngôn ngữ project cũ. Title, kịch bản, scene, lời nói, hồ sơ nhân vật, continuity asset, ambience/SFX và prompt cuối phải bằng tiếng Việt; tên riêng và khóa/enum máy đọc được giữ nguyên. Kết quả OpenAI sai ngôn ngữ bị ghi `Failed` nhưng vẫn quyết toán token đã tiêu thụ; dữ liệu không được lưu thành scene plan hợp lệ. BytePlus/Seedance tiếp tục dùng ngôn ngữ project.

Trong đúng phạm vi video dài Kling này, content plan còn phải thỏa ma trận speech intent: một nhân vật + có lời dùng `OnCameraDialogue`; không nhân vật + có lời dùng `NativeVoiceOver`; `None` không có lời/speaker và vẫn cho phép một nhịp không lời có nhân vật. Voice-over còn `character_keys` hoặc on-camera thiếu/mismatch speaker bị từ chối. Visual prompt on-camera phải mô tả nhân vật đang nói với mặt/miệng rõ và cử chỉ trong khi nói, nhưng không chép nguyên văn `spoken_text`.

Content plan gồm tối thiểu:

- tiêu đề/tóm tắt và concept;
- script hiện hành;
- style profile;
- tối đa một nhân vật lặp lại trong phạm vi tự động hiện tại;
- thư viện text `Background`/`Prop`/`Item` có `asset_key`, tên và mô tả chuẩn;
- scene plan theo thứ tự;
- danh sách `asset_key` áp dụng riêng cho từng scene;
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
- nội dung mới vượt qua validate speech mode, nội dung bắt buộc và character mapping.

Chỉnh scene tạo prompt version mới, cập nhật speech intent và làm vô hiệu snapshot cũ. Không được sửa một scene rồi âm thầm áp dụng thay đổi cho scene khác.

### 7.1. Tài sản text và trải nghiệm xác nhận theo cảnh

Content plan hiện hành có thể trả bối cảnh, đạo cụ, item và mapping `asset_key`. Server materialize dữ liệu này thành thư viện project; tài sản AI mới ở trạng thái `Draft` để người dùng vẫn là người xác nhận cuối cùng. Đồng bộ lại đề xuất dùng response OpenAI đã hoàn tất, không gọi OpenAI lần hai và không ghi đè tài sản `Locked` hoặc nguồn `Manual`.

Luồng chính nằm ngay trên card Storyboard:

1. `Chờ xác nhận`: assignment hợp lệ nhưng còn tài sản nháp. UI hiển thị loại và tên tài sản, sau đó cho bấm **Xác nhận tài sản cảnh**.
2. `Cần chỉnh sửa`: assignment thiếu/thừa `Background`, chứa tài sản không hợp lệ hoặc phần prompt bắt buộc vượt giới hạn. Người dùng bấm **Sửa lựa chọn**.
3. `Đã sẵn sàng`: assignment hợp lệ và toàn bộ tài sản đang gắn đã khóa; scene có thể tiếp tục tạo clip khi các điều kiện khác cũng đạt.

Khi xác nhận, desktop gửi đúng tập asset ID và concurrency token đang hiển thị. Server kiểm tra tập này vẫn trùng assignment hiện hành, chạy lại preflight, rồi khóa trong một lần lưu mọi tài sản nháp đang gắn với scene, kể cả nguồn `Manual`. Tài sản ngoài scene không bị khóa. Nếu dữ liệu đã thay đổi, server trả `scene_asset_confirmation_stale`, desktop tải lại và yêu cầu xác nhận lại. Thao tác không gọi OpenAI/Kling/BytePlus, không giữ ngân sách và không ghi usage.

Trình chọn dùng radio cho `Background`, checkbox cho `Prop`/`Item`; scene được phép không dùng continuity asset. UI không hiển thị `Draft`, version hoặc độ dài prompt hoàn chỉnh như điều kiện người dùng phải hiểu. **Chi tiết nâng cao** chỉ nêu độ dài phần prompt bắt buộc; composer có thể tự co scene prompt/negative prompt tùy chọn để giữ prompt cuối trong giới hạn Kling. Chỉ phần bắt buộc vượt giới hạn mới là blocker.

Quản lý thư viện, mở khóa, khóa từng tài sản, duyệt AI theo lô và **Khôi phục đề xuất AI** vẫn tồn tại cho tình huống nâng cao. Đây không phải đường bắt buộc của người dùng phổ thông.

## 8. Sinh clip đa provider

1. Desktop chọn scene hợp lệ và chuẩn bị ảnh primary từ workspace nếu cần.
2. Desktop gửi `SubmitVideoRequest` gồm project ID, scene ID, idempotency key, organization ID, scene-plan version, prompt version và ảnh reference nếu có.
3. Server đọc project/scene/prompt/speech intent cùng assignment và `ProjectAssetVersion` từ database, resolve hoặc tạo snapshot video policy của project.
4. Server chặn assignment sai hoặc tài sản chưa khóa trước resolver/budget/outbound; sau đó mới kiểm tra provider/model/capability/credential/rate/budget và quyền rồi route qua Kling hoặc BytePlus adapter.
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

Server dựng prompt từ speech intent đã lưu; desktop không gửi raw speech làm nguồn sự thật. Với on-camera video dài Kling, template tiếng Việt `kling-native-audio-v4-vietnamese-speech-first` đặt speaker/câu thoại/performance trước identity/tài sản, gắn người nói với ảnh first-frame, bắt đầu trong 0,5 giây đầu, giữ rõ mặt/môi/hàm và cấm narrator hoặc chỉ cười im lặng. Hệ thống không đếm hoặc chặn lời theo ngưỡng số từ cố định: `spoken_text` được giữ nguyên khi gửi provider và không bị tự cắt. Do chất lượng phát lời vẫn phụ thuộc thời lượng, ngôn ngữ và model, người dùng bắt buộc nghe duyệt từng clip và có thể chỉnh lời/tạo lại nếu provider nói nhanh, thiếu lời hoặc lệch khẩu hình.

Sau khi tải output:

1. Desktop kiểm tra video stream, thời lượng, kích thước, SHA-256 và audio stream.
2. Audio thiếu hoặc gần như im lặng chuyển scene/generation sang `NativeAudioInvalid`; clip không thể duyệt.
3. Audio nghe được chuyển sang `AudioReviewRequired`.
4. Người dùng phải phát preview và xác nhận đã nghe đủ câu, đúng người nói/kiểu voice-over và khẩu hình/đồng bộ chấp nhận được, sau đó mới bấm **Duyệt hình và âm thanh**.
5. Chỉ lúc đó scene mới nhận `ApprovedGenerationId` và trạng thái `Approved`.

Video dài dùng Kling chỉ phát lời tiếng Việt và metadata speech là `vi-VN`. Dữ liệu Kling tiếng Anh cũ hoặc chỉnh sửa thủ công bằng tiếng Anh bị chặn trước quote/reserve/outbound; người dùng phải sinh lại content plan tiếng Việt. Quy tắc này không thay đổi luồng video ngắn direct prompt và không tự áp dụng cho BytePlus/Seedance. Kiểm tra tự động vẫn chỉ xác nhận có âm thanh nghe được; manual review từng clip là bắt buộc.

Nếu generation terminal là `NativeAudioInvalid`, hệ thống không tự retry. Khi người dùng xác nhận một request có phí mới, server tự chọn `speech-recovery-v1` cho on-camera và ghi profile vào safe request snapshot/hash: khung medium close-up/medium shot, không intro im lặng, room tone tối thiểu, không nhạc và không hành động phức tạp cạnh tranh với lời. Desktop không được gửi profile này như một tham số provider. Replay cùng idempotency key/hash vẫn trả request cũ; attempt mới dùng key/reservation riêng.

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
- `NativeAudioInvalid` cần attempt video mới có xác nhận chi phí nếu người dùng muốn sinh lại; on-camera dùng `speech-recovery-v1`, không tái sử dụng idempotency key của request terminal cũ với payload khác.

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
| `scene_asset_background_invalid` / `kling_prompt_too_long` | Sửa lựa chọn tài sản; chọn đúng một bối cảnh và rút gọn phần mô tả bắt buộc nếu cần. |
| `scene_asset_not_locked` / `scene_asset_version_missing` | Xác nhận tài sản trên card cảnh trước khi tạo clip. |
| `scene_asset_confirmation_stale` / `project_asset_changed` | Dữ liệu vừa thay đổi; để desktop tải lại rồi xác nhận lại tập tài sản hiện hành. |
| `kling_on_camera_speaker_required` / `kling_speech_intent_invalid` | Khóa đúng một nhân vật/reference và sửa mode/lời của scene. |
| `kling_voice_over_character_not_allowed` | Chuyển sang nhân vật nói trực tiếp hoặc bỏ nhân vật để scene thành B-roll. |
| `kling_on_camera_action_invalid` | Bỏ yêu cầu im lặng/khép miệng/voice-over khỏi prompt hình ảnh. |
| `NativeAudioInvalid` | Nghe/kiểm tra clip, sửa prompt nếu cần và tạo attempt mới. |
| `media_tool_not_ready` | Cài/sửa FFmpeg/FFprobe rồi tiếp tục tải request đã hoàn tất; không submit lại provider. |

UI phải chuyển lỗi nội bộ thành thông báo có thể hành động; không để exception thô làm dừng desktop.

## 14. Điều kiện vận hành và giới hạn hiện tại

- Migration 4.0.0–4.0.8 có trong source nhưng không mặc định được coi là đã chạy trên database thật. Xác nhận tài sản một chạm và speech-first/recovery profile dùng schema/request snapshot hiện có, không yêu cầu migration mới.
- Kling là policy mặc định. BytePlus chỉ sẵn sàng sau rollout có kiểm soát.
- Desktop SQL trực tiếp cho workflow là kiến trúc chuyển tiếp; server mới là nguồn sự thật cho credential/provider request/usage.
- Media bundle dev hiện không tự động đủ điều kiện phát hành production; release cần hồ sơ/license approval đúng scope.
- Không tự chạy migration production, rotate credential, nhập giá đoán hoặc gọi smoke test có phí.

## 15. Điểm vào source trước khi thay đổi

- Contracts: `TOOL-SHARED.Contracts/Generation/GenerationContracts.cs`.
- Asset contracts: `TOOL-SHARED.Contracts/Projects/ProjectAssetContracts.cs`.
- Gateway: `TOOL-SERVER/Generation/GenerationService.cs`.
- Asset API/service: `TOOL-SERVER/Controllers/ProjectAssetsController.cs` và `TOOL-SERVER/Projects/ProjectAssetService.cs`.
- Provider policy: `TOOL-SERVER/Generation/VideoModelPolicy.cs`.
- Provider adapters: `KlingVideoClient.cs`, `BytePlusVideoClient.cs` và prompt composer tương ứng.
- Worker/cache/proxy: `KlingPollingWorker.cs`, `KlingOutputProxyService.cs` và `GeneratedVideoCleanupWorker.cs`.
- Desktop orchestration: `TOOL-LOCAL/Generation/ProjectGenerationService.cs`.
- Character/storyboard: `TOOL-LOCAL/Projects/ProjectService.cs` và `TOOL-LOCAL/Web/src/App.tsx`.
- Final render: `TOOL-LOCAL/Projects/ProjectRenderService.cs` và `Media/FfmpegRenderService.cs`.
- Migration trạng thái/tài sản hiện hành: `database/VideoFactory.4.0.6.NativeAudioWorkflowStatuses.sql`, `VideoFactory.4.0.7.ProjectAssetTextLibrary.sql` và `VideoFactory.4.0.8.AiGeneratedProjectAssets.sql`.
- Runbook vận hành: `TRIEN_KHAI_AI_GATEWAY_TO_CHUC.md`.
