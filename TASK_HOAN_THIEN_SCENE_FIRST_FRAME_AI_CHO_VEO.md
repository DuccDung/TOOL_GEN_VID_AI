# Task hoàn thiện Scene First-Frame AI cho Veo

> Trạng thái: Đã triển khai và kiểm thử source; chưa rollout database/provider thật  
> Phạm vi: `TOOL-SHARED.Contracts`, `TOOL-SERVER`, `TOOL-LOCAL`, `TOOL-TESTS`, `database` và tài liệu vận hành  
> Mục tiêu chính: tách ảnh nhận diện nhân vật vuông khỏi first-frame theo từng cảnh để Fal/Veo luôn nhận ảnh đúng tỷ lệ video.

## 1. Bối cảnh và nguyên nhân

Luồng tạo ảnh nhân vật hiện tại chủ ý tạo một ảnh định danh trung tính `1024x1024`:

- `OpenAiImageClient` gửi `size = "1024x1024"` tới GPT-Image-2.
- `GeneratedImageValidator` bắt buộc output ảnh nhân vật phải đúng `1024x1024`.
- Prompt ảnh nhân vật yêu cầu bố cục vuông, toàn thân, nền studio trung tính.
- Ảnh sau khi tải về được lưu làm `CharacterReference` chính và được khóa để tái sử dụng giữa các cảnh.

Fal/Veo Image-to-Video lại yêu cầu first-frame:

- PNG hoặc JPEG.
- Tối đa `8 MB`.
- Dự án `16:9`: tối thiểu `1280x720` và đúng tỷ lệ.
- Dự án `9:16`: tối thiểu `720x1280` và đúng tỷ lệ.
- First-frame phải được duyệt trước khi submit.

Do đó, việc dùng trực tiếp ảnh định danh vuông làm first-frame tạo ra xung đột thiết kế. Bấm **Sinh lại ảnh nhân vật** không giải quyết được vì ảnh mới vẫn là `1024x1024`.

## 2. Mục tiêu nghiệp vụ

Giữ nguyên ảnh nhân vật vuông làm nguồn khóa nhận diện, đồng thời bổ sung một first-frame riêng cho từng cảnh Veo:

```text
Ảnh nhận diện nhân vật 1024x1024
        ↓ dùng làm nguồn giữ nhận diện
GPT-Image-2 dựng first-frame theo nội dung cảnh
        ↓ chuẩn hóa đúng 16:9 hoặc 9:16
Người dùng xem, từ chối hoặc duyệt
        ↓ khóa đúng phiên bản
Fal/Veo dùng first-frame của cảnh để tạo clip
```

Kết quả cần đạt:

- Không gửi ảnh nhân vật vuông trực tiếp sang Fal/Veo.
- Không kéo giãn, crop hoặc ghi đè ảnh nhân vật đã khóa.
- Cảnh có nhân vật và B-roll đều có thể có first-frame đúng tỷ lệ.
- Người dùng phải duyệt first-frame trước khi phát sinh request video Veo.
- Kling, video ngắn và project cũ giữ nguyên hành vi hiện tại.

## 3. Phạm vi và giới hạn

### 3.1. Trong phạm vi

- Sinh first-frame AI riêng cho từng scene.
- Dùng ảnh nhân vật đã duyệt làm image input cho cảnh có nhân vật.
- Sinh first-frame text-to-image cho B-roll không có nhân vật.
- Snapshot scene prompt, ảnh nhân vật và các project asset đã khóa.
- Preview, duyệt, từ chối, sinh lại và phát hiện first-frame lỗi thời.
- Lưu lịch sử phiên bản và first-frame đã dùng trong provider request.
- Budget, rate snapshot, idempotency, output proxy và audit an toàn.
- Hỗ trợ cả `16:9` và `9:16`.

### 3.2. Ngoài phạm vi

- Không đổi ảnh nhân vật chuẩn khỏi `1024x1024`.
- Không tự đổi policy `LongForm` hoặc model Veo của project đã snapshot.
- Không fallback Veo sang Kling, Standard sang Fast hoặc I2V sang T2V.
- Không tự duyệt ảnh sau khi AI tạo.
- Không hỗ trợ nhiều hơn một nhân vật trong một scene Veo ở phiên bản đầu.
- Không tự đoán hoặc seed giá OpenAI/Fal.

## 4. Quyết định thiết kế

### 4.1. Tách hai loại ảnh

`CharacterReference` tiếp tục là:

- Ảnh định danh trung tính.
- Kích thước `1024x1024`.
- Dùng để khóa khuôn mặt, tóc, cơ thể và trang phục.
- Không mang bố cục của một cảnh cụ thể.

`SceneFirstFrame` mới là:

- Thuộc đúng một scene.
- Có bối cảnh, góc máy, ánh sáng, hành động và bố cục của scene.
- Đúng tỷ lệ project.
- Có version, trạng thái và nguồn sinh riêng.
- Là ảnh duy nhất được gửi sang Fal/Veo.

### 4.2. Cảnh có nhân vật dùng image editing

Server dùng ảnh `CharacterReference` đã duyệt làm image input, sau đó ghép prompt từ:

- Visual description của scene.
- Camera direction, lighting, motion và emotion.
- Dialogue/narration đã duyệt.
- Background, Prop và Item đã khóa.
- Quy tắc giữ nhận diện nhân vật.
- Quy tắc bố cục theo aspect ratio của project.

Mục tiêu là giữ nhận diện nhưng tạo một khung hình thực tế của cảnh, thay vì biến ảnh studio vuông thành video.

### 4.3. Cảnh B-roll dùng generation không có nhân vật

Scene không gắn nhân vật được sinh first-frame từ scene prompt và project asset đã khóa. Không yêu cầu hoặc giả lập `CharacterReference`.

### 4.4. Chỉ chuẩn hóa ảnh first-frame nháp

Nếu provider ảnh trả output lớn hơn hoặc chưa đúng tỷ lệ đích:

- Prompt phải đặt chủ thể trong vùng an toàn.
- Chỉ crop/resize bản first-frame nháp trước khi duyệt.
- Không crop ảnh nhân vật nguồn.
- Không kéo giãn làm méo hình.
- Sau chuẩn hóa phải tính lại SHA-256, MIME, kích thước và dung lượng.

Đầu ra cuối:

- `16:9`: `1280x720`.
- `9:16`: `720x1280`.
- PNG/JPEG và không quá `8 MB`.

## 5. Thiết kế dữ liệu

Tạo migration idempotent mới dự kiến:

`database/VideoFactory.4.1.1.SceneFirstFrames.sql`

Không sửa migration lịch sử đã có.

### 5.1. Bảng `vf.SceneFirstFrames`

Các trường dự kiến:

- `SceneFirstFrameId uniqueidentifier` — khóa chính.
- `SceneId uniqueidentifier` — scene sở hữu frame.
- `MediaAssetId uniqueidentifier` — file trong workspace.
- `Version int` — version tăng dần trong scene.
- `Status varchar(...)` — trạng thái vòng đời.
- `SourceCharacterReferenceId uniqueidentifier NULL` — null cho B-roll.
- `GeneratedByProviderRequestId uniqueidentifier NULL` — request GPT-Image đã tạo ảnh.
- `ScenePlanVersion int`.
- `ScenePromptId uniqueidentifier`.
- `ScenePromptVersion int`.
- `AspectRatio varchar(...)`.
- `PromptTemplateVersion varchar(...)`.
- `CreatedByUserId nvarchar(...)`.
- `ApprovedByUserId nvarchar(...) NULL`.
- `CreatedAtUtc datetime2(3)`.
- `ApprovedAtUtc datetime2(3) NULL`.
- `InvalidatedAtUtc datetime2(3) NULL`.
- `RowVersion rowversion`.

Trạng thái:

```text
PendingReview
Approved
Rejected
Superseded
Invalidated
```

Ràng buộc:

- `Version > 0`.
- Unique `(SceneId, Version)`.
- Mỗi scene chỉ có tối đa một frame `Approved` đang hiệu lực.
- `MediaAssetId` và `GeneratedByProviderRequestId` là unique khi có giá trị.
- FK không cascade vào lịch sử provider request hoặc usage.
- First-frame, scene, media asset và project phải cùng phạm vi dữ liệu.

### 5.2. Snapshot first-frame trong provider request

Bổ sung `InputSceneFirstFrameId` nullable vào `vf.ProviderRequests` hoặc một bảng liên kết immutable tương đương.

Mục đích:

- Truy vết chính xác frame nào đã được gửi sang Veo.
- Không thay đổi lịch sử khi người dùng sinh hoặc duyệt frame mới.
- Cho phép kiểm tra cost, output và lỗi theo đúng version nguồn.

### 5.3. Quyền database

- Server tạo và thay đổi trạng thái `SceneFirstFrames`.
- Desktop không được tự ghi trạng thái duyệt bằng SQL.
- Cập nhật `VideoFactory.DesktopLeastPrivilege.sql` để chặn ghi trực tiếp bảng mới.
- Desktop đọc dữ liệu first-frame qua API server.
- Ghi version `4.1.1-scene-first-frames` vào `ai.SchemaVersions`.
- Không backfill ảnh vuông cũ thành first-frame.

## 6. Shared contracts

Tạo file contracts riêng cho first-frame hoặc mở rộng nhóm generation theo hướng tương thích ngược.

### 6.1. Request tạo first-frame

Chứa tối thiểu:

- `ProjectId`.
- `SceneId`.
- `ScenePlanVersion`.
- `ScenePromptVersion`.
- `IdempotencyKey`.
- `OrganizationId`.
- Ảnh tham chiếu nhân vật kèm ID, MIME, Base64 và SHA-256 khi scene có nhân vật.

Desktop không được gửi:

- API key.
- Provider/model tùy ý.
- Full prompt hiệu lực.
- Giá.
- Output size tùy ý.
- URL ảnh công khai.

Server phải lấy provider, model, rate, prompt và aspect ratio từ nguồn sự thật hiện hành.

### 6.2. Response tạo first-frame

Trả về:

- `ProviderRequestId`.
- `ProviderCode`.
- `ModelCode`.
- `ContentUrl` tương đối.
- `MimeType`.
- `Sha256`.
- `Width`.
- `Height`.
- `SizeBytes`.
- `ActualCost`.
- `CurrencyCode`.
- `ExpiresAtUtc`.

### 6.3. Submit video

Giữ `ReferenceImage` hiện tại để tương thích Kling. Thêm field nullable ở cuối `SubmitVideoRequest`:

- `SceneFirstFrameInput? FirstFrame`.

`SceneFirstFrameInput` chứa:

- `SceneFirstFrameId`.
- `MimeType`.
- `Base64Data`.
- `Sha256`.

Quy tắc:

- Fal/Veo bắt buộc dùng `FirstFrame`.
- Kling tiếp tục dùng `ReferenceImage`.
- Fal không được fallback sang ảnh nhân vật vuông khi thiếu first-frame.

## 7. Luồng tạo first-frame trên server

### 7.1. Tiền kiểm trước outbound

Thứ tự bắt buộc:

1. JWT có session/device claim hợp lệ.
2. Session, user và device còn Active.
3. License và device lease còn hiệu lực.
4. Membership Active và role được phép phát sinh AI.
5. Project thuộc đúng organization và user.
6. Project là video dài và snapshot provider là Fal/Veo.
7. Scene plan và scene prompt đúng version hiện hành.
8. Background/Prop/Item của scene đã khóa.
9. Scene on-camera có đúng một nhân vật đã khóa.
10. Ảnh nhân vật nguồn đúng ID/hash/version đã duyệt.
11. Idempotency key không xung đột request hash.
12. OpenAI model, credential và rate đang hoạt động.
13. Budget organization/member được reserve thành công.

Viewer, cross-user, cross-project và cross-organization phải bị chặn trước outbound.

### 7.2. Prompt composer riêng

Tạo `SceneFirstFramePromptComposer`, không dùng nguyên prompt video Veo.

Prompt cần có:

- Nhận diện nhân vật và đặc điểm bất biến.
- Bối cảnh và asset đã khóa.
- Visual description của scene.
- Camera, lighting, motion và emotion.
- Tỷ lệ đích và vùng bố cục an toàn.
- Với on-camera: chỉ một nhân vật, mặt/môi/hàm rõ, không bị che.
- Cấm subtitle, chữ, logo, watermark, split panel và người thừa.

Request log chỉ lưu:

- Prompt template version.
- Prompt hash.
- ID/version nguồn.
- Thông số output an toàn.

Không lưu full prompt nhạy cảm, ảnh Base64 hoặc URL output gốc.

### 7.3. Mở rộng OpenAI image client

Tách hai đường gọi:

- `Generate`: B-roll không có nhân vật.
- `Edit`: cảnh có nhân vật, dùng ảnh primary đã duyệt làm image input.

Mọi outbound vẫn đi qua `TOOL-SERVER`, `IHttpClientFactory`, credential tổ chức và allowlist OpenAI hiện tại.

### 7.4. Validator theo profile

Refactor validator để có hai profile rõ ràng:

- `CharacterReference`: PNG, đúng `1024x1024`, giới hạn hiện tại.
- `SceneFirstFrame`: PNG/JPEG, đúng project ratio, tối thiểu 720p, tối đa `8 MB`.

Kiểm tra nội dung file bằng signature/header thực tế, không chỉ tin MIME hoặc metadata do client gửi.

## 8. Lưu file và materialize

Luồng đề xuất:

1. Server sinh ảnh và lưu output tạm trong `vf.GeneratedImageOutputs`.
2. Desktop tải qua content proxy có xác thực.
3. Desktop ghi file đuôi `.part`.
4. Desktop kiểm tra SHA-256, MIME, kích thước và dung lượng.
5. Desktop đổi tên atomically vào workspace.
6. Desktop gọi API materialize bằng `ProviderRequestId` và đường dẫn tương đối đã chuẩn hóa.
7. Server xác minh request/output rồi tạo `MediaAsset` và `SceneFirstFrame(PendingReview)`.
8. Nếu download lỗi, UI cho phép tải lại output cũ trong thời gian retention, không tạo ảnh và không tính phí lần nữa.

Đường dẫn dự kiến:

```text
projects/{project}/scenes/{scene}/first-frames/
```

Server không chấp nhận absolute path hoặc đường dẫn thoát khỏi workspace.

## 9. Duyệt, sinh lại và invalidation

### 9.1. Duyệt first-frame

Người dùng phải xem preview và bấm duyệt. Server kiểm tra lại:

- Frame thuộc đúng scene/project/organization.
- File metadata và hash khớp bản materialized.
- Scene prompt chưa thay đổi.
- Character reference nguồn vẫn là primary hiện hành.
- Asset version vẫn là bản đã khóa hiện hành.
- Aspect ratio, kích thước và dung lượng vẫn hợp lệ.

Duyệt bản mới sẽ chuyển bản Approved cũ sang `Superseded`.

### 9.2. Invalidation

Frame phải chuyển `Invalidated` khi thay đổi:

- Scene plan hoặc scene prompt.
- Visual description, camera, lighting, motion hoặc emotion.
- Character hoặc primary reference.
- Background/Prop/Item được gán cho scene.
- Version của asset đã khóa.
- Aspect ratio project.

Dù event invalidation bị bỏ sót, server vẫn phải so sánh snapshot động lần cuối trước submit Veo.

### 9.3. Sinh lại

- Bản Approved hiện tại vẫn được giữ cho đến khi bản mới được duyệt.
- Sinh lại là request AI mới và phải xác nhận chi phí.
- Retry do timeout phải dùng request cũ, không tự tăng attempt.

## 10. Idempotency và chi phí

Idempotency key cần bao gồm snapshot nguồn, ví dụ:

```text
scene-first-frame:
{scenePromptId}:
{scenePromptVersion}:
{characterReferenceId-or-none}:
{assetVersionHash}:
{aspectRatio}:
{attempt}
```

Quy tắc:

- Replay cùng payload trả lại request cũ.
- Cùng key nhưng payload khác trả idempotency conflict.
- Chỉ thao tác **Sinh lại** rõ ràng mới tăng attempt và reserve mới.
- Provider tạo ảnh thành công nhưng desktop download lỗi vẫn phải settle cost.
- Retry download không reserve budget lần nữa.
- Thiếu rate trả `pricing_not_configured` trước outbound.
- Request phải lưu rate snapshot và usage thực tế/estimate theo quy tắc hiện tại.
- Không seed hoặc tự đoán giá mới.

## 11. Thay đổi UI/UX desktop

Trong mỗi scene của Storyboard thêm khu vực **First-frame Veo**.

Trạng thái hiển thị:

- Chưa tạo.
- Đang tạo.
- Chờ duyệt.
- Đã duyệt.
- Đã lỗi thời.
- Tạo thất bại.
- Đã tạo trên server nhưng chưa tải xong.

Thao tác:

- **Tạo first-frame bằng AI**.
- **Xem ảnh lớn**.
- **Duyệt**.
- **Từ chối**.
- **Sinh lại**.
- **Tải lại output**.

Trước khi sinh/sinh lại phải hiển thị:

- Provider/model ảnh.
- Tỷ lệ và kích thước đầu ra.
- Chi phí ước tính.
- Scene và nhân vật nguồn.
- Cảnh báo đây là request AI có phí.

Nút **Tạo video** chỉ bật khi:

- Fal/Veo đang sẵn sàng.
- First-frame đã Approved.
- First-frame chưa Invalidated.
- File local tồn tại và hash hợp lệ.
- Scene prompt và asset vẫn đúng version.

Thông báo lỗi kỹ thuật hiện tại cần đổi thành hướng dẫn hành động:

> Cảnh này chưa có first-frame đúng tỷ lệ đã duyệt. Hãy tạo và duyệt first-frame trước khi gửi sang Veo.

## 12. Chuyển Fal/Veo sang dùng SceneFirstFrame

Trong `GenerationService`:

- Fal/Veo lấy `SceneFirstFrameInput`, không lấy `CharacterReference` làm first-frame.
- Kiểm tra ID, hash, MIME, size, dimensions và version nguồn.
- Ghi `InputSceneFirstFrameId` vào provider request trước outbound.
- Safe request snapshot chỉ lưu ID/hash/version, không lưu Base64.
- Fal payload vẫn giữ `720p`, Native Audio, duration `4/6/8`, ratio `16:9/9:16` và `auto_fix=false`.

Kling tiếp tục dùng đường `ReferenceImage` hiện tại. Không thay đổi video ngắn hoặc project đã snapshot Kling.

## 13. API dự kiến

Các route có thể triển khai:

```text
POST /api/generation/images/scene-first-frames
GET  /api/generation/images/scene-first-frames/{providerRequestId}/content

GET  /api/projects/{projectId}/scenes/{sceneId}/first-frames
GET  /api/projects/{projectId}/scene-first-frames
POST /api/projects/{projectId}/scenes/{sceneId}/first-frames/materialize
POST /api/projects/{projectId}/scenes/{sceneId}/first-frames/{frameId}/approve
POST /api/projects/{projectId}/scenes/{sceneId}/first-frames/{frameId}/reject
```

Controller phải mỏng; authorization, lifecycle, transaction và validation nằm trong service.

## 14. Tương thích ngược

Phải giữ nguyên:

- CharacterReference `1024x1024`.
- Kling Image-to-Video hiện tại.
- Video ngắn dùng policy `Default`.
- Snapshot provider/model của project cũ.
- API Kling legacy.
- Quy trình preview và duyệt Native Audio.

Fal/Veo mới yêu cầu `SceneFirstFrame`. Không tự fallback:

- Veo sang Kling.
- Standard sang Fast.
- Image-to-Video sang Text-to-Video.
- SceneFirstFrame sang ảnh vuông nhân vật.

## 15. Kiểm thử bắt buộc

### 15.1. Unit test ảnh và prompt

- CharacterReference vẫn chỉ nhận `1024x1024`.
- SceneFirstFrame nhận `1280x720` và `720x1280`.
- Chặn ảnh vuông, sai ratio, quá `8 MB`, MIME giả và metadata sai.
- Prompt on-camera giữ đúng nhân vật và chỉ một speaker.
- Prompt B-roll không yêu cầu nhân vật.
- Prompt đặt chủ thể trong safe composition.
- Không lưu/log Base64 hoặc full prompt.

### 15.2. Service và security test

- Role hợp lệ có thể tạo first-frame.
- Viewer bị chặn.
- Cross-user/cross-project/cross-organization bị chặn.
- Thiếu rate, credential hoặc budget bị chặn trước outbound.
- Replay idempotency không tạo provider request hoặc ledger thứ hai.
- Regenerate tạo attempt/reservation riêng.
- Download retry không phát sinh chi phí mới.
- Frame lỗi thời không được submit Veo.
- Video request snapshot đúng `SceneFirstFrameId`.
- Output response không chứa URL OpenAI/Fal gốc.

### 15.3. Desktop test

- Download bằng `.part` và rename atomically.
- Hash/MIME/kích thước sai phải xóa file tạm.
- Materialize lỗi không để trạng thái nửa chừng.
- UI không bật Tạo video khi frame chưa duyệt.
- Cập nhật scene làm frame hiển thị Đã lỗi thời.
- Kling và video ngắn không bị buộc dùng first-frame mới.

### 15.4. Migration test

- Migration chạy được trên database có dữ liệu cũ.
- Chạy lặp lại không lỗi.
- FK/index/check constraint đúng.
- Không backfill ảnh vuông thành first-frame.
- Desktop role không được ghi bảng mới.
- Schema version được ghi đúng một lần.

## 16. Thứ tự triển khai

1. Chốt state machine và schema `SceneFirstFrames`.
2. Tạo migration `4.1.1` và cập nhật EF mapping.
3. Thêm shared contracts tương thích ngược.
4. Refactor image validator theo profile.
5. Thêm GPT-Image generation/editing cho scene first-frame.
6. Thêm prompt composer và snapshot nguồn.
7. Hoàn thiện reserve/settle/idempotency.
8. Thêm API generate/download/materialize/approve/reject.
9. Chuyển Fal submit sang `SceneFirstFrameInput`.
10. Cập nhật desktop client, workspace và project dashboard.
11. Cập nhật WebView bridge và TypeScript types.
12. Hoàn thiện UI Storyboard.
13. Thêm unit/security/regression/migration test.
14. Cập nhật nghiệp vụ, runbook và hướng dẫn smoke test.
15. Chạy restore, Release build và toàn bộ test.
16. Triển khai trên staging đã backup.
17. Chỉ sau đó mới xin phép chạy paid smoke test Veo Fast 4 giây.

## 17. Rủi ro và biện pháp giảm thiểu

### Sai lệch nhận diện nhân vật

- Dùng image editing với CharacterReference đã duyệt.
- Đưa immutable traits vào prompt.
- Bắt buộc người dùng review trước khi duyệt.

### Crop mất đầu, tay hoặc chân

- Prompt dùng safe composition và vùng dư.
- Chỉ crop first-frame nháp.
- Preview đúng khung video trước khi duyệt.

### Phát sinh chi phí trùng

- Idempotency theo snapshot + attempt.
- Retry download dùng output cũ.
- Regenerate phải xác nhận chi phí mới.

### First-frame không còn khớp scene

- Snapshot scene prompt, character reference và asset version.
- Invalidate khi nguồn thay đổi.
- Server kiểm tra động lần cuối trước outbound Veo.

### File local và dữ liệu server không đồng bộ

- Download `.part`, kiểm tra hash rồi mới rename.
- Materialize sau khi tải thành công.
- Trước submit, desktop và server cùng xác minh hash.

## 18. Tiêu chí nghiệm thu

Task chỉ hoàn thành khi:

- Ảnh nhân vật vuông hiện tại vẫn được giữ nguyên.
- Hệ thống tự tạo được first-frame đúng tỷ lệ cho scene có nhân vật.
- B-roll tạo được first-frame riêng.
- Người dùng có thể preview, từ chối, sinh lại và duyệt.
- Frame thay đổi nguồn bị đánh dấu lỗi thời và bị chặn submit.
- Fal/Veo chỉ nhận frame Approved đúng version.
- Không còn lỗi `1024x1024` khi first-frame hợp lệ.
- Retry không tạo duplicate provider request hoặc chi phí.
- Không lộ API key, Base64, full prompt hoặc output URL gốc.
- Toàn bộ test Kling/video ngắn vẫn đạt.
- Release build và test toàn solution đạt.
- Paid smoke test chỉ chạy sau khi được phê duyệt môi trường và chi phí.

## 19. Điều kiện rollout

- Không tự chạy migration trên database thật.
- Xác minh đúng SQL instance/database và có backup/restore test trước migration.
- Chạy migration idempotency trên bản sao staging trước.
- Kiểm tra OpenAI rate cho image input/output theo cấu hình hiện hành.
- Dùng organization staging riêng với budget/member limit nhỏ.
- Test một scene on-camera đơn giản trước, sau đó mới test B-roll.
- Chỉ bật production sau khi đối chiếu usage ledger, chất lượng nhận diện, tỷ lệ ảnh, audio Veo và output proxy.

## 20. Trạng thái hiện tại

- [x] Đã xác định nguyên nhân: CharacterReference AI bị cố định `1024x1024` nhưng Veo yêu cầu first-frame đúng tỷ lệ video.
- [x] Đã thống nhất hướng thiết kế: tách CharacterReference và SceneFirstFrame.
- [x] Đã lập kế hoạch dữ liệu, API, server, desktop, UI, security, pricing và test.
- [x] Đã tạo migration idempotent `VideoFactory.4.1.1.SceneFirstFrames.sql`, EF mapping và least-privilege deny; ngày 2026-09-05 đã xác minh migration hiện có trên `DUNGDEV/VideoFactory` do người vận hành áp dụng.
- [x] Đã thêm shared contracts tương thích ngược cho generate/quote/materialize/lifecycle và `SubmitVideoRequest.FirstFrame` nullable ở cuối.
- [x] Đã triển khai server, OpenAI generation/editing, validator, budget/idempotency/output proxy, materialize/approve/reject/invalidation, Fal snapshot và desktop/WebView/UI end-to-end.
- [x] Đã sửa refresh first-frame theo một request/project, tách rate-limit API chỉ đọc và chống lặp toast lỗi nền.
- [x] Đã sửa Fal/Veo polling dùng queue route chuẩn `fal-ai/veo3.1/requests/...`; submit vẫn dùng endpoint Standard/Fast đã snapshot và request cũ không bị tạo lại.
- [x] Đã hòa giải scene `WaitingProvider` với provider request `Completed` trên dashboard để hiện đúng trạng thái chờ tải, bỏ cảnh báo polling cũ và không báo chi phí tạo mới.
- [x] Đã bổ sung unit/service/security/desktop/migration regression test, cập nhật nghiệp vụ/runbook; ngày 2026-09-05 Release build đạt 0 warning/error và 657/657 test đạt.
- [x] Đã xác minh `DUNGDEV/VideoFactory` có đúng một version `4.1.1`, bảng/cột/FK/index đầy đủ và bảng first-frame đang rỗng; agent không chạy migration.
- [ ] Chưa xác minh idempotency migration trên database clone và khả năng restore từ backup.
- [ ] Chưa chạy paid smoke test OpenAI/Fal/Veo mới.
- [ ] Agent chưa phát sinh chi phí provider mới.
