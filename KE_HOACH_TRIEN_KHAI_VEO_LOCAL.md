# Triển khai đồng nhất giọng Veo local

Theo nghiệp vụ được người dùng xác nhận ngày 2026-09-09. Đây là nhật ký triển khai, không phải chứng nhận rollout.

## Phạm vi

Project Fal/Veo OpenAiStructuredPlan, ProviderNativeVerified, một nhân vật thoại trực diện mỗi cảnh. Chọn Voice Anchor từ native clip được duyệt; xử lý local; duyệt converted asset riêng; giữ native immutable. Retry không gọi provider/tính phí nhưng vẫn xác minh quyền qua server. ASR local tùy chọn, giọng ngoài và Cloud Fal LipSync không thuộc MVP này.

## Baseline đã kiểm tra

- Branch fix-voice, HEAD 2b4171c. README và các file LipSync/untracked đã tồn tại trước công việc này.
- Restore đạt. Build ban đầu lỗi 51 vị trí do cloud LipSync chưa có mapping/pricing/DI.
- Directory.Build.targets giữ nguyên source thử nghiệm, loại rõ các file cloud LipSync và test tương ứng khỏi build mặc định. Chúng KHÔNG được tính là test đã đạt hoặc đã skip bởi runner. EnableExperimentalFalLipSync=true dành cho việc hoàn thiện độc lập, hiện chưa build được.
- Máy phát triển i7-11800H, RAM khoảng 16 GB, RTX 3050 Laptop 4 GB VRAM.

## Công việc

- [x] Kiểm tra source, tài liệu, baseline và phần dang dở.
- [x] Profile CPU, pin model/dependency/archive hash và ghi provenance/license upstream.
- [x] Contract, policy project, migration metadata.
- [x] Component store và worker Python riêng, môi trường lọc, kiểm byte và khóa component.
- [x] Native preparation, VAD, separation, Voice Anchor.
- [x] Conversion, mixing, media validation và MP4 atomic promotion.
- [x] Job/checkpoint, cache theo hash nguồn/model/anchor, retry, cancel và stale lineage.
- [x] WebView/React context, nghe duyệt, batch thủ công, native exception có xác nhận và cleanup.
- [x] Render chỉ nhận approved/current, kiểm nguồn/mẫu/hash/approval fingerprint trước publish.
- [x] Tests .NET/Web/Python/FFmpeg và opt-in model smoke kỹ thuật.
- [x] Tài liệu và báo cáo kiểm chứng riêng cho model/rollout.

## Kết quả kiểm chứng ngày 2026-09-09

Worktree trên fix-voice, nền HEAD 2b4171c, chưa commit hoặc publish. Đây là kết quả lệnh thực tế, không phải kết quả branch cũ.

| Nhóm | Passed | Failed | Skipped | Ghi chú |
|---|---:|---:|---:|---|
| .NET suite cuối | 978 | 0 | 4 | 982 bài; model opt-in không được gộp thành Passed |
| React/Vitest | 71 | 0 | 0 | 13 test files |
| Python worker helpers | 5 | 0 | 0 | Không nạp model/provider |
| Model smoke opt-in | 1 | 0 | 0 | Chạy riêng trên model thật và audio demo upstream |

Restore thành công; build Release cuối 0 warning/0 error của MSBuild. npm ci, npm run build và npm test thành công. Vite vẫn cảnh báo chunk lớn hơn 500 kB; không che cảnh báo này. git diff --check đạt.

Model smoke chạy qua đúng LocalVoiceRuntime của desktop: kiểm byte/probe, tách mẫu, chuyển giọng, remux, xác minh audio/duration và video packet hash, chạy lại từ cache giữ output hash. Dùng 8 giây audio demo_speaker0/demo_speaker1 từ archive OpenVoice đã ghim và hình testsrc2; không phải clip Veo tiếng Việt hoặc video khẩu hình thật. Thời gian probe 68,3 giây; toàn lượt probe + anchor + conversion + remux + cache retry 280,9 giây. Chưa có benchmark peak RAM hoặc batch dài đáng tin cậy.

Các lần thử trước đã phát hiện và sửa: đường dẫn MAX_PATH/junction alias của managed Python, mặc định đọc checkpoint PyTorch, oneDNN primitive trên Windows, và lỗi trạng thái khi InvalidDataException. Profile cuối tắt oneDNN, Demucs segment 4 giây; không chuyển sang Cloud. Có một lượt model test cũ bị chủ động dừng để cập nhật verifier; lượt đạt nêu trên là lượt chạy lại sau sửa, không tính lượt bị dừng là Passed.

Log test cục bộ: artifacts/test-results/local-voice/local-voice-final-suite.trx và local-voice-model-smoke.trx. Artifact/model test tải vào artifacts và cache riêng, không đưa binary hoặc sample audio vào source/Git. Xem [component provenance](TOOL-LOCAL/LocalVoice/COMPONENTS.md).

## Model của profile thử nghiệm

- OpenVoice V2 tone-color converter, source https://github.com/myshell-ai/OpenVoice commit 74a1d147b17a8c3092dd5430504bd83ef6c7eb23; model https://huggingface.co/myshell-ai/OpenVoiceV2 revision f36e7edfe1684461a8343844af60babc2efbb727. Không dùng TTS.
- Silero VAD: https://github.com/snakers4/silero-vad.
- Demucs htdemucs: https://github.com/facebookresearch/demucs. Chưa coi việc có adapter là bằng chứng chất lượng separation giọng nói/tiếng Việt.

## Triển khai môi trường

Không tự chạy migration trên database thật hoặc provider có phí. Feature mặc định tắt. Migration 4.1.8 bổ sung policy; không phụ thuộc bảng cloud LipSync thử nghiệm 4.1.6–4.1.7.

### Cách bật thử

1. Chọn database clone/test, xác minh instance/database, backup/restore và phê duyệt theo runbook. Áp dụng migration 4.1.8 hai lần để kiểm idempotency, rồi kiểm mapping/quyền vf. Rehearsal và áp migration trên database người dùng chỉ định đã thực hiện ngày 2026-09-09, xem biên bản bên dưới; kiểm quyền bằng account desktop thật vẫn chưa thực hiện.
2. Triển khai server có endpoint local-voice/access và desktop cùng contract. Binary mới cần cột LocalVoicePolicyVersion kể cả khi feature tắt.
3. Trong cấu hình desktop đặt Features.VeoLocalVoiceConsistencyEnabled=true rồi khởi động lại. Không bật Canonical Voice/TTS cho workflow này.
4. Mở project Fal/Veo video dài, đến Storyboard hoặc bước xuất video → Đồng nhất giọng Veo local → xác nhận bật cho project → cài runtime. Cài đặt có tải component từ nguồn ghim và có thể cần vài GB đĩa/cache; quá trình inference không upload media.
5. Nghe duyệt native clip, chọn cảnh một nhân vật làm mẫu, xác nhận quyền dùng giọng, chuẩn bị và nghe duyệt mẫu. Chọn các cảnh cùng nhân vật để chạy; nghe A/B kết quả rồi duyệt.
6. Nếu kết quả không đạt: từ chối/chạy lại local hoặc dùng native với lý do và xác nhận. Render bỏ qua cảnh thoại chưa đủ duyệt, không tự fallback. Dọn intermediate chỉ khi có xác nhận, giữ native/anchor/output/checkpoint.

### Điều kiện trước khi bật rộng

- [x] Backup mới, restore rehearsal, áp migration và chạy lặp trên clone; áp/verify trên database người dùng chỉ định ngày 2026-09-09.
- [ ] Kiểm quyền bằng account desktop thật và smoke WebView2 trong ứng dụng thật.
- [ ] 2–3 clip Veo tiếng Việt cùng nhân vật có quyền dùng giọng; nghe câu chữ/màu giọng/khẩu hình/ambient/SFX.
- [ ] Bộ mẫu giọng nam/nữ, no-speech và nhiều người nói; MVP không có diarization tự động và không bảo đảm câu chữ bằng ASR.
- [ ] Benchmark CPU/RAM và batch dài, kiểm restart/hủy/khôi phục trên máy đích.
- [ ] Rà soát notice/quyền phân phối checkpoint và package trước publish; không suy từ license code.

Chưa production-ready theo nghĩa nghiệm thu người dùng/môi trường. Không rollback sang desktop cũ để render project đã bật policy local, vì binary cũ không có guard này. Giữ dữ liệu/policy/lineage khi rollback; không xóa migration.

## Biên bản áp migration theo yêu cầu ngày 2026-09-09

- Người dùng cung cấp đích DUNGDEV / VideoFactory, Windows authentication, và xác nhận thực hiện quy trình backup → restore thử → migration. Hoàn tất xác minh lúc 11:29:39 +07:00. Không suy kết quả này thành chứng nhận production hoặc nghiệm thu UI/model.
- Chỉ chạy nội dung file `database/VideoFactory.4.1.8.LocalVoiceConsistency.sql`, SHA-256 `324484CC08006E8CA6048853AE3933199F0D99B1206B92C303B011FEC2C9F53E`. Không sửa file migration, không chạy các migration khác.
- Backup mới `COPY_ONLY, CHECKSUM, COMPRESSION`, không ghi đè backup cũ, hoàn tất lúc 11:24:55; kích thước nén khoảng 4,24 MB. File được giữ tại `D:\SQL2019\Microsoft SQL Server\MSSQL15.MSSQLSERVER\MSSQL\Backup\VideoFactory_pre_4.1.8_localvoice_20260909_112404.bak`.
- `RESTORE VERIFYONLY WITH CHECKSUM` đạt. Restore thật sang `VideoFactory_LocalVoiceCheck_20260909_112404` với file MDF/LDF riêng, không ghi đè database có sẵn; `DBCC CHECKDB` trên clone không báo lỗi.
- Áp migration trên clone và chạy lặp đạt: đúng một version row, cột `varchar(50) NULL`, constraint enabled/trusted, policy sai bị chặn và policy `veo-local-voice-v1` được nhận. Thử cập nhật policy chỉ trên clone trong transaction rồi rollback.
- Hai lượt đầu của harness kiểm tra gặp lỗi biên dịch tham chiếu cột mới trong cùng batch và thiếu `QUOTED_IDENTIFIER ON` khi thử UPDATE trên bảng có index liên quan. Đã sửa harness bằng dynamic SQL và `sqlcmd -I`; lượt kiểm tra cuối đạt. Các lỗi này xảy ra trước khi áp migration lên database đích, không sửa nội dung migration.
- Trên VideoFactory, áp và kiểm tra trong transaction bao ngoài với lock timeout 10 giây. So sánh SHA-256 nội bộ của toàn bộ cột project cũ theo thứ tự primary key trước/sau: dữ liệu 17 project giữ nguyên; không in nội dung project. Số FK và index project giữ nguyên.
- Kết nối độc lập sau commit xác nhận `LocalVoicePolicyVersion varchar(50) NULL`, `CK_Projects_LocalVoicePolicyVersion` enabled/trusted, một version `4.1.8-local-voice-consistency`, 17 project và 0 project có local policy. `DBCC CHECKCONSTRAINTS` không báo vi phạm; database vẫn ONLINE/MULTI_USER/READ_WRITE.
- Đã xóa riêng database rehearsal cùng hai file của nó sau khi xác minh đúng tên, đường dẫn, nguồn backup và không có user session đang dùng. Không dừng server/IDE hoặc ngắt session của người dùng. Có thể tạo lại clone từ backup còn giữ.
- Không bật feature, không khởi động binary mới, không gọi provider có phí. Phiên này chỉ thay đổi schema đã nêu và tài liệu kiểm chứng; không chạy lại build/test ứng dụng hoặc coi migration thành công là smoke WebView2/quyền desktop đã đạt.
