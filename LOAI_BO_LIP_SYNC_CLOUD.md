# Loại bỏ lip-sync Cloud — 2026-09-10

Đã triển khai trên working tree `main` từ `bb5f5e2`, chưa commit/push. Phạm vi là source, build và kiểm thử; server/desktop đang chạy chưa được thay bằng bản mới.

## Hai luồng được giữ lại

1. **Canonical Voice:** tạo audio WAV riêng, tạo video nền từng cảnh, ghép WAV bằng FFmpeg, nghe/duyệt rồi dựng video dài. Lời dẫn giữ luồng hiện có; thoại nhân vật cần duyệt WAV trước khi tạo video nền. Hình ảnh và chuyển động miệng giữ theo clip provider.
2. **Veo Native Audio + local voice:** Veo tạo hình và tiếng; chọn mẫu từ clip đã duyệt, chuyển màu giọng local, nghe so sánh/duyệt rồi dựng. Source của component, worker, UI local voice và API kiểm quyền không thay đổi.

## Các task đã hoàn thành

| Task | Kết quả |
|---|---|
| T01 — Phạm vi | Đã đối chiếu tham chiếu; các model lip-sync chuyên biệt không nằm trong mapping/navigation hiện hành. |
| T02 — Contract/trạng thái | Bỏ DTO Cloud và mã lỗi chặn; chỉ giữ tên trạng thái cũ dưới dạng hằng `LegacySpeechReadyForLipSync` để đọc tương thích. Không phát sinh trạng thái chờ mới. |
| T03 — Audio riêng | Server xác minh WAV của narrator/nhân vật theo version, lời nói và approval trước cost gate. Desktop cho thoại nhân vật đã duyệt WAV tiếp tục tạo clip và ghép. |
| T04 — Duyệt/render | Clip nền Canonical chưa được coi là bản cuối. Kết quả ghép cần duyệt riêng, kiểm voice/speech/scene-plan/raw-video lineage và hash. Cache hỏng hoặc sai nguồn được ghép lại local. |
| T05 — Module Cloud | Xóa provider client, service, polling worker, input store, runtime settings/API, media preparer, DTO/model và test chuyên biệt; bỏ `EnableExperimentalFalLipSync` cùng file build exclusion. |
| T06 — UI | Bỏ bước/thông báo chờ lip-sync; cảnh Canonical đã duyệt WAV có bước tạo video nền và ghép. Giữ checklist Native Audio. |
| T07 — Project cũ | Dashboard kiểm voice version, lời nói, pointer và thời điểm duyệt để chiếu bước tiếp tục; đọc không ghi dữ liệu. Thiếu WAV thì chuẩn bị lại, chưa duyệt thì nghe/duyệt. |
| T08 — Kiểm thử/tài liệu | Restore, Release build, .NET và frontend đạt; cập nhật README, nghiệp vụ, kiến trúc, trạng thái, vận hành và nghiệm thu. |

Migration 4.1.6/4.1.7 lip-sync được giữ nguyên làm lịch sử. Không DROP bảng, xóa request/ledger hay chạy SQL thay đổi dữ liệu. Hai luồng hiện hành không yêu cầu triển khai các migration Cloud này. Không sửa license/provenance/checksum của component.

## Bằng chứng kiểm thử

Checkout kiểm thử: `D:\VideoMakerValidation\remove-cloud-lipsync-20260910`. Tách khỏi thư mục server Release đang chạy để giữ nguyên tiến trình người dùng. Các file source thay đổi được đồng bộ từ working tree chính; không dùng binary cũ làm bằng chứng.

- `dotnet restore TOOL_GEN_POST_VIDEO.slnx`: đạt.
- `dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore`: đạt, 0 warning MSBuild / 0 error.
- `dotnet test TOOL-TESTS/TOOL-TESTS.csproj -c Release --no-build`: **1091 Passed / 0 Failed / 5 Skipped**.
- Frontend `npm ci --no-audit --no-fund`, `npm run build`, `npm test`: đạt; **89 Passed / 0 Failed / 0 Skipped**. Vite còn cảnh báo bundle lớn hơn 500 kB.
- `git diff --check`: đạt.
- TRX cuối: `D:\VideoMakerValidation\remove-cloud-lipsync-20260910\artifacts\test-results\remove-cloud-lipsync-final-v2.trx`.

Test mới chạy FFmpeg thật với video màu và WAV tổng hợp, gateway giả chỉ trả output có sẵn. Đã kiểm cả lời dẫn và thoại nhân vật: tiếp tục, dùng lại cache, cache hỏng, cache sai nguồn, duyệt cuối; không gọi TTS/video submit mới. Test server kiểm từ chối trước resolver/reservation/outbound khi WAV thiếu hoặc sai phiên bản, lời nói, snapshot hoặc bằng chứng duyệt. Test render chặn thiếu approval, sai lời hoặc sai nguồn video trước FFmpeg. Test project cũ xác minh dashboard không tự ghi approval.

Năm test Skipped: OpenVoice/Veo local model thật; Piper model thật; Qwen integration; Qwen benchmark; SQL Server rehearsal TikTok nhiều tài khoản. Không tính chúng là model/database đã nghiệm thu.

Lượt test đầu dùng TEMP trên ổ C gần hết dung lượng nên có lỗi môi trường; các lượt cuối đặt TEMP/TMP dưới `D:\VideoMakerValidation\test-temp`. Fixture InMemory mô phỏng giá trị rowversion do SQL Server sinh, vẫn giữ kiểm tra thuộc tính bắt buộc.

## Dùng bản mới

Server và desktop cần chạy cùng phiên bản source mới. Không dùng desktop mới với server cũ còn chặn thoại Canonical. Cấu hình vẫn có gate riêng: server bật Canonical/speech-verification trong source, desktop `SpeechSynchronizationEnabled=false`, Veo local `VeoLocalVoiceConsistencyEnabled=true`; cấu hình máy có thể ghi đè. Việc xóa Cloud không tự thay đổi rollout hoặc cấu hình người dùng.

Chưa tạo video provider có phí, chưa nghe nghiệm thu clip Veo tiếng Việt thật trong lần thay đổi này, chưa publish và chưa restart server/desktop người dùng.
