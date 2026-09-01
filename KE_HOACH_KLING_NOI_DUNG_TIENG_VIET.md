# Hồ sơ triển khai nội dung tiếng Việt bắt buộc cho Video Dài Kling

> Trạng thái source: đã triển khai ngày 2026-09-01. Việc smoke test Kling có phí chưa được thực hiện trong task này.  
> Phạm vi: chỉ workflow nhiều cảnh `OpenAiStructuredPlan` có provider snapshot là `Kling`.

## 1. Quyết định nghiệp vụ

- Nhãn, nút và thông báo của VideoMaker tiếp tục hiển thị bằng tiếng Việt.
- Toàn bộ nội dung dành cho người đọc do OpenAI sinh cho Video Dài Kling phải bằng tiếng Việt có dấu.
- Lời thoại/lời dẫn gửi Kling dùng tiếng Việt và metadata ngôn ngữ hiệu lực `vi-VN`.
- Prompt cuối của Video Dài Kling dùng wrapper tiếng Việt, đồng thời giữ nguyên câu đã duyệt trong JSON string để hạn chế dịch hoặc diễn giải lại.
- Tên riêng như `Maya` được giữ nguyên. `character_key`, `asset_key`, enum, GUID và tên thuộc tính JSON là dữ liệu máy đọc nên không dịch.
- Video Ngắn `DirectShortVideo` và BytePlus/Seedance không bị policy này thay đổi.
- Không tự dịch hoặc sửa hàng loạt dữ liệu cũ. Dự án Kling còn content tiếng Anh phải sinh lại content plan tiếng Việt trước khi tạo clip mới.

## 2. Ma trận ngôn ngữ

| Nhóm dữ liệu | Quy tắc |
|---|---|
| UI và thông báo lỗi | Tiếng Việt |
| Title, hook, angle, audience, CTA, kịch bản | Tiếng Việt |
| Mục đích cảnh, mô tả hình ảnh, negative prompt | Tiếng Việt |
| `spoken_text`, voice style, ambience, SFX | Tiếng Việt, metadata `vi-VN` |
| Vai trò và hồ sơ nhân vật | Tiếng Việt; tên riêng được giữ nguyên |
| Tên/mô tả bối cảnh, đạo cụ, item | Tiếng Việt |
| Khóa, enum và định danh máy đọc | Giữ định dạng kỹ thuật |
| Video Ngắn và BytePlus/Seedance | Giữ hành vi/ngôn ngữ hiện có |

## 3. Hạng mục đã triển khai

### 3.1. Policy và sinh content

- `KlingLongFormLanguagePolicy` trả `vi-VN` khi đồng thời là Kling và `OpenAiStructuredPlan`.
- Idempotency/safe request snapshot ghi policy version `kling-long-form-vietnamese-v1` để không replay nhầm response của policy tiếng Anh cũ.
- `OpenAiContentClient` liệt kê rõ mọi nhóm trường dành cho người đọc phải trả bằng tiếng Việt, kể cả character, asset và audio intent.
- Kết quả OpenAI sai ngôn ngữ bị từ chối sau outbound OpenAI, quyết toán token đã tiêu thụ và không trở thành scene plan hợp lệ.

### 3.2. Chốt chặn trước chi phí Kling

- Server kiểm tra scene prompt, negative prompt, speech, hồ sơ nhân vật và tài sản đã khóa trước resolver/rate/budget/outbound Kling.
- Chỉnh scene, chỉnh tài sản hoặc sinh ảnh nhân vật bằng dữ liệu tiếng Anh bị chặn bằng `kling_prompt_language_invalid`.
- Tên nhân vật được miễn kiểm tra ngôn ngữ vì có thể là tên riêng.
- Không có migration SQL mới cho policy này; hệ thống dùng provider snapshot, structure type và các cột hiện có.

### 3.3. Prompt Native Audio tiếng Việt

- Video Dài Kling dùng template `kling-native-audio-v4-vietnamese-speech-first`.
- Khối lời nói/performance đứng trước identity và continuity asset.
- On-camera yêu cầu nhân vật bắt đầu nói trong 0,5 giây đầu, thấy rõ mặt/môi/hàm và không được chỉ đứng cười im lặng.
- Voice-over chỉ dùng cho B-roll không gắn nhân vật.
- `speech-recovery-v1` vẫn được áp dụng cho attempt có xác nhận chi phí sau `NativeAudioInvalid`.
- Template v3 mặc định được giữ cho các đường gọi không thuộc policy Video Dài Kling tiếng Việt.

### 3.4. Desktop và dữ liệu cũ

- Dự án Video Dài tạo mới dùng cố định `vi-VN`; người dùng không phải chọn ngôn ngữ ở bước thiết lập.
- Card nội dung hiển thị rõ “Tiếng Việt (bắt buộc cho Video Dài dùng Kling)”.
- Dashboard phát hiện content tiếng Anh cũ và hiển thị hành động **Sinh lại nội dung tiếng Việt**.
- Submit clip từ dữ liệu cũ vẫn bị server chặn, nên không phát sinh chi phí Kling ngay cả khi client cũ bỏ qua cảnh báo UI.
- Sinh lại content tạo version mới theo cơ chế hiện hành; không âm thầm dịch hoặc ghi đè lịch sử.

## 4. Kiểm thử và nghiệm thu

Các nhóm regression test bao phủ:

- chỉ Kling + `OpenAiStructuredPlan` bị ép `vi-VN`;
- BytePlus và `DirectShortVideo` không đổi;
- OpenAI nhận chỉ dẫn đầy đủ cho mọi trường tiếng Việt;
- output/content chỉnh tay/tài sản tiếng Anh bị chặn đúng biên;
- prompt cuối dùng wrapper, speech instruction và template version tiếng Việt;
- `spoken_text` tiếng Việt được giữ nguyên;
- UI nhận diện dự án cũ và hướng dẫn sinh lại;
- voice-over/on-camera, version, idempotency, budget và outbound guard vẫn giữ bất biến.

Mốc xác minh source ngày 2026-09-01:

- `dotnet restore TOOL_GEN_POST_VIDEO.slnx`: đạt;
- Release build toàn solution: 0 warning, 0 error;
- `TOOL-TESTS`: 390/390 test đạt;
- `npm ci` và production build Web: đạt.

Điều kiện nghiệm thu vận hành còn lại là smoke test có phí trên môi trường được người dùng chỉ rõ: tạo một scene on-camera tiếng Việt, nghe đủ câu, xác nhận đúng người nói và khẩu hình chấp nhận được. Không coi audio stream có âm lượng là bằng chứng lời nói đúng.

## 5. Ngoài phạm vi

- Dịch dữ liệu lịch sử trực tiếp trong database.
- Thay đổi policy ngôn ngữ của Video Ngắn hoặc BytePlus/Seedance.
- Tự động retry request Kling có phí.
- Gọi provider thật, chạy migration production hoặc publish release trong task source này.
