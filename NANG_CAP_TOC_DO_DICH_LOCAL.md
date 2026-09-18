# Nâng cấp tốc độ dịch local — 2026-09-17–18

Source bắt đầu từ `main` / `9646a7b`; working tree sạch trước triển khai. Thay đổi này chưa commit. Kết quả ở mục nghiệm thu chỉ áp dụng cho binary/môi trường được ghi, không xác nhận rollout production.

## Phạm vi triển khai

- Worker 1.2.0 / IPC 3. Một `StatelessExecutor` cho mỗi lần nạp model; mỗi request vẫn có context inference và sampling/grammar độc lập. Constructor chỉ chạy ở request đầu. Model/config/reset/dispose/lỗi loại bỏ executor đúng vòng đời.
- Constructor executor nằm trong xử lý lỗi native allocation. Cancellation không trả bản dịch dở thành kết quả thành công.
- Khi shutdown mà native inference chưa kết thúc sau thời gian chờ hủy, worker thoát process; không dispose context/weights đồng thời với native decode. Regression gửi IPC trực tiếp xác minh không gọi dispose engine đang chạy.
- Chính sách `qwen4b-cuda12-v3` hỗ trợ 12/24/28/30/32/36 layer. Desktop và worker dùng cùng allowlist. Giữ dự phòng 1 GiB + 86 MiB/layer; 32 layer cần tối thiểu 3.776 MiB VRAM trống theo ước lượng. Ngưỡng này không thay thế load/inference/probe thật.
- Các mức mới 28/30/32 chỉ được chọn cho job có mọi nhóm tối đa 6 target cue. Job executor truyền kích thước nhóm lớn nhất đã lập kế hoạch cho provider trước khi nạp model. Nếu có nhóm lớn hơn, giữ các mức cũ 12/24/36; trên RTX 3050 4 GiB là tối đa 24. Nếu request tăng kích thước ngoài kế hoạch, hạ về 24 trước inference và lưu giới hạn để resume không tăng lại. Không tự chia nhỏ nhóm hoặc thay prompt để lấy số benchmark tốt hơn.
- OOM: mức trên 24 → 24 → 12 → CPU, tối đa ba lần GPU thất bại trong một job. Sau reset, kiểm VRAM trên đúng UUID rồi bỏ qua mức không đủ tài nguyên. Các lỗi CUDA khác giữ phân loại/fallback hiện hành; model/protocol sai không bị che bằng CPU.
- Lưu mức đã chọn trước khi load, lưu mức giảm trước retry. Resume không tăng layer, không đổi GPU đã chọn và không thử lại GPU sau `CpuFallback=true`. Checkpoint cũ không có `PlannerPolicy` vẫn đọc được.
- Probe CPU/CUDA gắn với worker/config fingerprint mới; không sửa tay marker READY. Cache kết quả dịch, engine/model identity, prompt, sampling, cue manual/locked, CAS và SRT atomic giữ nguyên.

## Thử nghiệm cache prefix

Theo [source LLamaSharp 0.27.0](https://github.com/SciSharp/LLamaSharp/blob/v0.27.0/LLama/LLamaStatelessExecutor.cs), constructor tạo rồi hủy context; mỗi inference tiếp tục tạo context mới. Tái sử dụng executor chỉ bỏ phần khởi tạo dư ở các request sau, không tái sử dụng KV cache và không loại bỏ chi phí xử lý lại prompt.

`prefix-cache-experimental-v1` chỉ được worker chấp nhận khi có cờ benchmark trong môi trường tiến trình. Không có UI hoặc cấu hình project để bật chế độ này. Chế độ bình thường là `reuse-stateless-v1`; `legacy-stateless-v1` chỉ phục vụ đo A/B.

Thử nghiệm dùng một context, giữ tối đa 1.536 token prefix khớp chính xác sau chat template/tokenization. Trước request mới xóa suffix cũ; sau request chỉ giữ prefix của prompt, không giữ bản dịch. Reset grammar/sampler/decoder mỗi request; lỗi/cancel giải phóng context. Cache nằm trong RAM của worker, không ghi xuống disk và không sống qua job/reset. Prompt cộng giới hạn output phải nằm trong context; thử nghiệm không tự cắt prompt hoặc dịch chuyển context. Chỉ cân nhắc dùng bình thường sau benchmark và nghiệm thu chất lượng riêng.

## Công cụ kiểm chứng

`scripts/Benchmark-VietsubTranslationOptimization.ps1` dùng corpus cố định `translation-en-zh-2-6-12-v1`: Anh/Trung, xưng hô chị/em, tên speaker, số liệu, glossary và ngữ cảnh. Mặc định chỉ chạy nhóm 2/6 cue (tổng 16 cue); `-MaximumTargetCues 12` mở rộng nghiên cứu sang nhóm chưa đủ điều kiện dùng các mức layer mới. JSON chứa corpus hash, model hash, worker fingerprint, config, driver/VRAM, thời gian load, từng scene, thời điểm có output đầu tiên, RAM, VRAM lấy mẫu và hash output; không ghi nội dung bản dịch. Đảo thứ tự cấu hình giữa các round để phát hiện ảnh hưởng thứ tự/nhiệt độ. Khi có baseline `legacy:24`, test còn yêu cầu hash output của nhóm nhỏ ở chế độ reuse trùng baseline; Passed không chứng minh chất lượng trên mọi phụ đề thực tế.

Ví dụ chạy sau build (thay component root bằng thư mục đã cài/kiểm checksum trên máy đích):

```powershell
.\scripts\Benchmark-VietsubTranslationOptimization.ps1 -ComponentRoot D:\VideoMakerData\components\vietsub-translation -Repeats 3 -NoBuild
.\scripts\Benchmark-VietsubTranslationOptimization.ps1 -ComponentRoot D:\VideoMakerData\components\vietsub-translation -Soak -SoakCase reuse:32 -SoakScenes 100 -NoBuild
```

Script không cài component, sửa project hoặc bỏ qua cảnh báo RAM. Test model chạy riêng, sau build, không song song với OCR/FFmpeg nặng. Một cấu hình không đủ VRAM ghi `Skipped` trong JSON; việc test runner kết thúc Passed không biến cấu hình đó thành đã nghiệm thu.

VRAM hiện là mức sử dụng toàn thiết bị lấy mẫu mỗi 500 ms, không phải đỉnh cấp phát chính xác của process. `FirstOutputMilliseconds` bao gồm khởi tạo context và xử lý prompt, không phải phép đo riêng prefill. Đường stateless không báo token count khi chưa đo được chính xác. Đường prefix đếm token trực tiếp.

## Chất lượng và phạm vi chọn layer

Khảo sát ban đầu cho cả nhóm 2/6/12 cue có JSON hợp lệ, đủ cue và lặp lại ổn định, nhưng kiểm tra nội dung fixture 12 câu phát hiện thay đổi nghĩa: ở 28/30/32 layer câu “Thank you for waiting for me.” thành “Cảm ơn em đã chờ em.”; ở 30/32 layer “今天下午可能下雨。” thành “Sáng mai có thể mưa.”. Baseline 24 layer cũng có lỗi xưng hô/thời điểm (câu tiếng Trung thành “Sáng nay có thể mưa.”), nên baseline không phải bản dịch chuẩn tuyệt đối.

Vì vậy không bật các mức mới cho nhóm hơn 6 cue. Nhóm 2/6 cue trong ba vòng khảo sát cho hash output trùng baseline ở tất cả mức mới; đây là bằng chứng hồi quy trên corpus cụ thể, không phải bảo đảm mọi nhóm ngắn đều dịch đúng. Không đổi sampling/prompt hoặc sửa bản dịch để che chênh lệch chất lượng. Các file kiểm tra nội dung chỉ chứa fixture tổng hợp trong `D:\VideoMakerDiagnostics\translation-optimization-20260917\quality-audit`, không chứa phụ đề người dùng.

## Nghiệm thu

Restore và Release build toàn solution đã đạt, **0 Warning / 0 Error** ở MSBuild; Vite còn cảnh báo kích thước chunk trên 500 kB. `npm ci`, `npm run build` và frontend test đạt **252 Passed / 0 Failed / 0 Skipped** (42 file).

Full .NET trên binary cuối có giới hạn chất lượng: lượt song song mặc định **1.442 Passed / 1 Failed / 13 Skipped / 1.456 Total**, lỗi khởi tạo native predictor ở `LocalPipeline_RecognizesHardSubtitleFromRealVideoFrame`. Chạy lại toàn bộ trên cùng binary với `-- xUnit.ParallelizeTestCollections=false` đạt **1.443 Passed / 0 Failed / 13 Skipped / 1.456 Total** (4 phút 12 giây), gồm bài OCR đó. Giữ kết quả lượt mặc định; chưa quy nguyên nhân cuối cùng cho PaddleOCR. Các bài model/SQL opt-in Skipped không được tính là model/SQL đã nghiệm thu.

Binary Release dùng cho full suite và benchmark xác nhận:

- Worker DLL SHA-256: `F03B915B15B3DD66E0904B5D12CE5C1A2AC55B0920A0A84E478F7B055225A0BF`.
- Desktop DLL SHA-256: `E333F071BC15F01261286F06564A6A8A0FD657B0103762D413CCA5349EF59FDA`.
- Bằng chứng trong `D:\VideoMakerDiagnostics\translation-optimization-20260917`: `qualified-build.json`, `restore-qualified.log`, `build-qualified.log`, `full-qualified.trx`, `full-qualified-serial.trx`, `web-test.log`.

### Khảo sát trước khi thêm giới hạn chất lượng

Binary worker `2AD9A027E396E74804332728C282FD9C3DEC138B1FC8AED4E276CB7FDC5F64D6`, planner v2, RTX 3050 Laptop 4 GiB / driver 610.62, Qwen3 4B Q4_K_M, profile Standard (8 thread, context 4096, batch 256, micro-batch 64). Test cấu trúc/độc lập **1 Passed / 0 Failed / 0 Skipped**, bên trong **15/15 lượt cấu hình Passed**. Mỗi cấu hình ba vòng, sáu scene / 40 cue Anh-Trung; worker mới mỗi lượt, cache kết quả tắt, có A → B → A. Kết quả này chỉ là khảo sát hiệu năng: kiểm tra nội dung sau đó phát hiện lỗi ở nhóm 12 cue như đã ghi trên, nên không dùng nó để nghiệm thu chất lượng hoặc bật 32 layer cho mọi job.

| Cấu hình | Trung bình inference / 40 cue | Thấp nhất–cao nhất trong ba vòng |
|---|---:|---:|
| Tạo executor từng scene, 24 layer | 71,645 giây | 71,328–71,991 giây |
| Tái sử dụng executor, 24 layer | 71,466 giây | 71,157–71,788 giây |
| Tái sử dụng executor, 28 layer | 55,281 giây | 55,140–55,426 giây |
| Tái sử dụng executor, 30 layer | 48,784 giây | 48,571–48,894 giây |
| Tái sử dụng executor, 32 layer | 42,920 giây | 42,587–43,185 giây |

Thời gian chỉ gồm inference sáu scene, không gồm load hay lượt kiểm lại A. Riêng tái sử dụng executor ở 24 layer chỉ chênh khoảng 0,25%, nằm trong dao động phép đo; không coi đây là tăng tốc đáng kể. Không suy ra thời gian dịch toàn bộ video từ corpus ngắn này.

Bằng chứng khảo sát: `D:\VideoMakerDiagnostics\translation-optimization-20260917\final\benchmark.json`, `summary.json`, `optimization-benchmark.trx`. Tên thư mục `final` được tạo trước khi phát hiện giới hạn chất lượng; binary cuối dùng thư mục `qualified`.

Trên cùng binary khảo sát, chạy liên tục 100 nhóm 2 câu Anh/Trung cho từng chế độ `reuse:32` và `prefix:32`: mỗi bài **1 Passed / 0 Failed / 0 Skipped**, output lặp lại ổn định, phục hồi sau lỗi giới hạn output và sau cancel/reset/load. Chênh median private RAM giữa nhóm 11–20 và 91–100 lần lượt **12,32 MiB** và **10,38 MiB**, dưới ngưỡng kiểm 256 MiB; VRAM toàn thiết bị lấy mẫu cao nhất **2.840,50 MiB**. Báo cáo trong `final/soak-reuse` và `final/soak-prefix`. Đây là bằng chứng native lifecycle trước thay đổi planner v3; không gọi là smoke UI hoặc benchmark trên binary cuối. Source inference không đổi sau hai lượt này.

### Xác nhận nhóm nhỏ trên binary cuối

Benchmark kết thúc 2026-09-18 trên binary có hash ghi ở trên, cùng máy/profile/model: **1 Passed / 0 Failed / 0 Skipped**, **15/15 lượt cấu hình Passed**, không fallback hoặc bỏ qua mức layer. Mỗi lượt bốn scene (2/6 câu ở mỗi ngôn ngữ), tổng 16 cue, thêm A → B → A. Hash output nhóm nhỏ ở tất cả cấu hình reuse trùng baseline `legacy:24` trong cả ba vòng; kiểm tra này là assertion trong test. Corpus hash: `ac79a350a12dfc0cb286744758d9558f92390657f6f8e40c686d755b04aee510`.

| Cấu hình | Trung bình inference / 16 cue | Thấp nhất–cao nhất trong ba vòng |
|---|---:|---:|
| Tạo executor từng scene, 24 layer | 28,492 giây | 28,406–28,621 giây |
| Tái sử dụng executor, 24 layer | 28,072 giây | 27,976–28,167 giây |
| Tái sử dụng executor, 28 layer | 22,722 giây | 22,668–22,770 giây |
| Tái sử dụng executor, 30 layer | 20,450 giây | 20,399–20,487 giây |
| Tái sử dụng executor, 32 layer | 18,203 giây | 18,132–18,249 giây |

Ở 32 layer, thời gian inference giảm **36,1%**, tương đương nhanh **1,565 lần** so với cách cũ 24 layer trên corpus nhóm nhỏ này. Mỗi lượt còn có khoảng 7,9–8,0 giây load model; số trên không gồm load, dò GPU hoặc lượt kiểm lại A. Riêng reuse ở 24 layer giảm khoảng 1,5% trong lượt đo nhóm nhỏ, so với khoảng 0,25% ở corpus trước; lợi ích chính vẫn đến từ thêm layer GPU. VRAM toàn thiết bị lấy mẫu cao nhất ở 32 layer là **2.840,50 MiB**. Không suy rộng thành mức tăng tốc cho mọi video hoặc job có nhóm lớn.

Bằng chứng: `D:\VideoMakerDiagnostics\translation-optimization-20260917\qualified\benchmark.json`, `summary.json`, `optimization-benchmark.trx`; fingerprint tổng hợp worker/native trong báo cáo là `da8c7010556d498baeffbaf01ebc2c626c679175fafcd5a7a91100acd248085f`. Kết thúc benchmark không còn process translation worker của bài đo.

## Khôi phục và giới hạn

Để dùng bản này, chạy desktop Release cùng worker 1.2.0 được build kèm, cho Setup kiểm tra/probe lại nếu được yêu cầu, rồi tạo job Dịch Local mới ở chế độ AUTO. Không có thao tác nhập layer thủ công. Với cấu hình Standard mặc định tối đa 12 câu/nhóm, job có nhóm hơn 6 câu vẫn dùng mức cũ; không hứa tăng tốc 32 layer cho mọi video. Không sửa settings hoặc project sẵn có để ép dùng nhóm nhỏ.

Nếu GPU thiếu tài nguyên, job giảm layer hoặc chuyển CPU và giữ các câu đã lưu. Job đã chuyển CPU giữ CPU khi resume; job AUTO mới mới được chọn GPU lại. Sau đổi binary, runtime cần probe lại theo fingerprint; marker cũ không chứng minh worker mới sẵn sàng.

Khi khôi phục binary cũ phải khôi phục cặp desktop/worker cùng phiên bản; IPC 2/3 không tương thích và cố ý từ chối handshake lệch. Không xóa model, cache bản dịch, cue hoặc checkpoint để khôi phục. Chưa phát hành hoặc xác minh bundle/Windows sạch/desktop đăng nhập thật nếu chưa có biên bản tương ứng.
