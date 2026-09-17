# Tăng tốc dịch Local bằng CPU + NVIDIA GPU

Rà soát/triển khai ngày 2026-09-17, nhánh `main`, nền commit `e7e8e83`; thay đổi trong working tree, chưa commit. Phạm vi: dịch Qwen local trên Windows x64. OCR, tạo giọng và Dịch Cloud có pipeline riêng.

## Hành vi đã triển khai

1. Trong **Dịch → Local**, chọn **Tự động — kết hợp CPU và GPU NVIDIA** hoặc **Chỉ dùng CPU**. Lựa chọn lưu vào project và snapshot của job; job đang chạy không đổi theo lần sửa UI tiếp theo. Job strategy 1–3 vẫn dùng CPU, strategy 4 chứa policy mới.
2. Màn hình Dịch Local chỉ có nút chính **Dịch bằng Local**, giữ lựa chọn Auto/Chỉ CPU; đã bỏ nút kiểm tra/cài/sửa NVIDIA khỏi màn hình này. Khi bắt đầu phần cần dịch trong Auto, host kiểm gói tăng tốc, GPU/driver/VRAM và probe trước khi dịch phụ đề. Thiếu gói không tự tải khi bấm dịch: chuyển CPU và báo modal. Installer bảo trì vẫn có source riêng, tải component tùy chọn 618 MB vào `components/vietsub-translation/acceleration/llamasharp-0.27.0-cuda12-12.4-v1`. CPU `READY` đủ cho gate Setup.
3. Pack lấy đúng LLamaSharp CUDA Windows 0.27.0, cuBLAS 12.4.5.8 và cudart 12.4.127. Dùng URL HTTPS cố định, không redirect, giới hạn MIME/size, `.part`, SHA-256 archive rồi whitelist entry/DLL/hash, staging và promote thư mục có rollback. Giữ license NVIDIA nguyên văn. Không yêu cầu người dùng cài cả CUDA Toolkit.
4. Worker hỏi CUDA driver/NVML, chọn UUID GPU còn đủ VRAM, ràng buộc UUID trước khi CUDA khởi tạo, rồi dùng ordinal 0 trong process đó. Driver/device không dùng được trả trạng thái riêng. Ngân sách ban đầu: 1 GiB cho buffer/dự phòng + 86 MiB/layer; chọn 12/24/36 layer. Đây là ước lượng admission, nạp và inference thật vẫn phải thành công.
5. Worker ghim dependency và CPU kernel theo hash, cấu hình backend trước native load, kiểm số layer **thực sự offload** từ native callback. Log không ghi prompt/transcript; lỗi native chỉ được ghi trong lúc nạp model công khai đã ghim, che đường dẫn component và giới hạn độ dài.
6. CUDA phải vượt probe Runtime/Anh/Trung. Marker CUDA tách khỏi `probe.json` CPU; gắn model, worker/binary, protocol, CPU kernels, CUDA native fingerprint, config, planner version, UUID/driver và máy. VRAM trống được kiểm tra lại mỗi lần nạp. Getter trạng thái không chạy model/probe.
7. Trong Auto, lỗi tài nguyên thử giảm layer **một lần** rồi chuyển CPU; thiếu gói/GPU/driver, crash/timeout/backend hoặc probe GPU thất bại cũng chuyển CPU. Gói CUDA sai checksum bị từ chối sử dụng rồi tạo worker CPU riêng với model/probe CPU hợp lệ. Sai model hoặc protocol vẫn dừng, không fallback. Mỗi lần đổi backend tạo process mới. Trạng thái fallback được lưu vào checkpoint **trước khi thử lại**, nên pause/reopen vẫn giữ CPU cho phần còn lại.
8. Bản dịch đã lưu, CAS, receipt, cue sửa tay/khóa và SRT atomic giữ đường xử lý hiện hành. CPU/GPU policy không tham gia fingerprint nội dung, không đổi scene/prompt profile. Chỉ scene chưa commit phải thử lại.
9. Bridge gửi backend/device/fallback kèm mã lý do theo đúng organization/project/job. UI báo modal **Đã chuyển sang dịch bằng CPU** một lần cho mỗi job; nút **Đã hiểu** chỉ đóng thông báo, không gửi lại lệnh dịch. Chọn CPU chủ động hoặc GPU chạy được không hiện modal. Cảnh báo RAM được ưu tiên và vẫn cần xác nhận riêng. Đổi project/organization xóa thông báo cũ; event khác job đang hoạt động bị bỏ qua. UI khóa lựa chọn khi có job đang hoạt động; tiến độ vẫn giữ số câu đã lưu.

## Tương thích và vận hành

- Worker protocol **2**, worker version **1.1.0**; host và worker cần cập nhật cùng bundle. CPU probe cũ phải chạy lại vì worker fingerprint/protocol đã đổi; cue đã dịch không bị xóa.
- Engine/model ID cũ có hậu tố `cpu` được giữ để bảo toàn job/cache và đường dẫn model; backend thực tế nằm trong metadata execution riêng.
- Feature flag `VietsubLocalTranslationEnabled` không tự bật bởi thay đổi này.
- Giai đoạn này hỗ trợ một NVIDIA GPU được chọn tự động trên Windows x64. AMD/Intel dùng CPU; chưa chia model đồng thời lên nhiều GPU.
- Setup/cài GPU dùng runtime use gate, authorization và cảnh báo RAM hiện hành. Dưới mức RAM khuyến nghị cần xác nhận; không dùng GPU để bỏ qua resource gate.
- UI không nhận model path, native path, URL tải, device UUID hoặc số layer do DOM chỉ định. Worker không có Cloud client/API key/database.
- CPU_ONLY là cách ngừng dùng tăng tốc cho job mới. Cài/sửa pack không thay model. Update pack dùng thư mục version mới; không xóa component cũ khi chưa xác minh job và bundle rollback.
- Desktop cũ không đọc được job strategy 4. Trước rollback về bản cũ cần hoàn tất/hủy job mới và giữ backup workspace; không sửa metadata job để ép bản cũ tiếp tục. Cue đã commit vẫn là dữ liệu local cần giữ lại.

## Kiểm tra và đo hiệu năng

Các lệnh chuẩn: restore/build solution Release; full `TOOL-TESTS`; `npm ci`, `npm run build`, `npm test`. Regression bổ sung bao phủ lựa chọn backend, thiếu GPU, giảm layer có giới hạn, crash, cancel, checksum/protocol fail-closed, checkpoint CPU qua resume, ZIP traversal, input DOM và UI/organization context.

Benchmark model thật, chạy riêng sau build:

```powershell
.\scripts\Benchmark-VietsubTranslationGpu.ps1 `
  -ComponentRoot 'D:\VideoMakerData\components\vietsub-translation' `
  -Repeats 3 -NoBuild
```

Chỉ thêm `-AcceptResourceWarning` khi người vận hành đã chấp nhận cảnh báo thiếu RAM. Script phục hồi env sau khi chạy. Bài đo chờ tối đa 30 giây để Windows thu hồi bộ nhớ sau khi dừng backend trước, vẫn giữ resource gate; JSON ghi từng backend đã hoàn tất, nên phải kiểm kết quả test và đủ hai backend trước khi so sánh. Corpus fixture Anh/Trung giống nhau trên CPU và GPU, gọi inference trực tiếp nên không tính cache dịch thành tăng tốc. Báo cold-load riêng, tổng inference, p50/p95 scene, cues/giây, peak RAM worker và VRAM toàn thiết bị lấy mẫu 500 ms (gồm cả desktop; không phải peak riêng process). Kiểm schema và ý nghĩa fixture; không bắt output CPU/GPU giống từng ký tự.

Thời gian video một giờ còn phụ thuộc số câu/ký tự sau OCR và bối cảnh. Corpus ngắn không thay nghiệm thu clip 5/15/60 phút. Cần thêm đo các clip đại diện, preview song song, Windows sạch CPU/NVIDIA và update/rollback trước rollout.

## Bằng chứng lần triển khai CPU/GPU ban đầu (trước chỉnh luồng UI)

- i7-11800H, RAM khoảng 16 GB, RTX 3050 Laptop 4 GB, driver 610.62.
- Đã tải/cài pack thật, kiểm archive/DLL SHA-256; native CUDA smoke đạt **1 Passed / 0 Failed / 0 Skipped**.
- Benchmark model thật ngày 2026-09-17 đạt **1 Passed / 0 Failed / 0 Skipped**: cùng Qwen3 4B Q4_K_M, context 4096, CPU 4 thread, profile tiết kiệm RAM; 2 scene Anh/Trung, mỗi scene 2 cue, lặp 3 lần trên từng backend. Không dùng cache, không chấp nhận cảnh báo RAM. Native callback xác nhận **24 layer thực sự trên GPU**.

| Chỉ số | CPU AVX2 | CPU + RTX 3050, 24 layer |
|---|---:|---:|
| Tổng inference, 6 scene | 168,355 giây | 34,168 giây |
| Cold load | 7,761 giây | 7,944 giây |
| p50 / p95 mỗi scene | 27,785 / 29,261 giây | 5,458 / 6,376 giây |
| Cue/giây | 0,0713 | 0,3512 |
| Peak working set worker | 4,680 GiB | 3,042 GiB |
| Mẫu peak VRAM toàn thiết bị | Không đo | 2,641 GiB |

Tổng inference nhanh **4,93 lần**, giảm **79,7% thời gian** trên corpus này. Không suy từ số liệu này ra thời gian dịch video một giờ. JSON/TRX nằm tại `D:\vmt-gpu-0917\benchmark-final.json` và `gpu-benchmark.trx`. Worker DLL của lượt benchmark có SHA-256 `03388f3c7210e8ddf2df1ccdefe7e196176ff0061639a569ac1df07325e633a0`; sau lượt này bổ sung mapping lỗi checksum thành `TRANSLATION_GPU_INTEGRITY` và regression cho đường lỗi, không đổi cấu hình inference.

- Trong quá trình kiểm tra, lượt đầu bị cảnh báo RAM 1,29 GiB; lượt sau bắt được lỗi `MainGpu=0` của CPU và đã sửa CPU thành `-1`, GPU thành `0`. CPU đã dịch thành công sau sửa. Một lượt khác dừng khi Windows chưa thu hồi RAM của backend CPU; bài benchmark cuối chờ admission và hoàn tất cả hai backend.
- Restore/build Release thành công; build cuối **0 Warning / 0 Error**. Frontend: `npm ci`, `npm run build` thành công (cảnh báo chunk Vite > 500 kB); `npm test`: **249 Passed / 0 Failed / 0 Skipped**.
- Full .NET của lần triển khai ban đầu: **1.393 Passed / 1 Failed / 10 Skipped**, tổng 1.404 (`D:\vmt-gpu-0917\gpu-complete-suite.trx`). Test thất bại là `WebView2_keeps_label_descenders_and_waveforms_visible_at_multiple_zoom_levels` với kiểm tra chữ căn giữa; chạy riêng cùng source/binary đạt **1 Passed / 0 Failed / 0 Skipped** (`timeline-isolated.trx`). Không cộng lượt chạy riêng thành full-suite xanh; cần theo dõi độ ổn định của bài WebView2 khi chạy chung. Các bài acceleration/worker/bridge đạt; model opt-in trong full suite vẫn tính Skipped, bằng chứng model thật là lượt riêng ở trên.
- Lượt full suite dùng TEMP ổ C đã dừng do ổ C còn khoảng 0,13 GiB; lượt kết quả cuối dùng TEMP/TMP riêng tại `D:\vmt-gpu-0917\temp`, không thay đổi disk guard của sản phẩm. Trước sửa cuối đường lỗi checksum, một lượt full suite cũng đã đạt 1.393 Passed / 0 Failed / 10 Skipped (`gpu-verified-suite.trx`); đây là lịch sử, không thay thế kết quả working tree cuối.
- Chưa rollout production; chưa nghiệm thu Windows sạch, thao tác Setup qua UI trên bản đóng gói hoặc clip dài của người dùng. Benchmark trực tiếp worker không tự ghi marker READY cho Setup.

## Kiểm chứng luồng một nút dịch — 2026-09-17

Working tree trên `main`, nền `e7e8e83`, chưa commit. Đã bỏ nút kiểm tra NVIDIA; Auto tự kiểm GPU ở lần xử lý đầu, lưu lý do chuyển CPU vào checkpoint và hiện modal một lần theo job. Đóng modal không gửi lại lệnh dịch. Runtime CPU và cảnh báo thiếu RAM vẫn phải thỏa điều kiện hiện hành.

- `dotnet restore`, Release build solution: đạt, **0 Warning / 0 Error**. `npm ci`, `npm run build`: đạt; Vite vẫn cảnh báo chunk > 500 kB.
- Frontend, một worker: **252 Passed / 0 Failed / 0 Skipped**, 42 file. Bao phủ một nút Local, giữ policy qua cảnh báo RAM, bỏ event khác context/job, modal fallback/dismiss không tạo lệnh dịch mới và không lặp theo tiến độ.
- Full .NET, collection tuần tự, TEMP/TMP trên D: **1.399 Passed / 1 Failed / 10 Skipped**, 1.410 tổng (`D:\vmt-gpu-0917\gpu-ui-flow.trx`). Trong đó 23 test acceleration và 122 test translation Passed, 0 Failed.
- Test `OcrAdapter_RechecksAndRecognizesBundledEnglishChineseFixtures` trả `REPAIR_REQUIRED` ở lần probe thứ hai khi chạy chung. Chạy riêng cùng binary đạt **1 Passed / 0 Failed / 0 Skipped** (`gpu-ui-ocr-isolated.trx`). Chưa xác định nguyên nhân phụ thuộc môi trường/thứ tự chạy; không sửa OCR hoặc tính full suite là xanh.
- Không chạy lại benchmark model thật cho chỉnh luồng UI này; số đo CPU/GPU phía trên là bằng chứng lịch sử của lần triển khai trước. Chưa smoke modal qua app đang đăng nhập hoặc phát hành production.

## Sửa lỗi chuyển GPU sang CPU do vòng đời worker — 2026-09-17

Nhánh `main`, nền commit `e7e8e83`, working tree đã có thay đổi CPU/GPU trước lượt sửa và vẫn chưa commit. Job thật lúc 21:31 ghi `AUTO`, `TRANSLATION_PROCESS_CRASHED`, `CpuFallback=true`. Chẩn đoán trên cùng máy tái hiện chuỗi dò GPU → reset → nạp CUDA: EOF của worker bị chủ động dừng báo lỗi vào handshake/request của worker mới. Gói CUDA đúng checksum; chờ luồng đọc cũ kết thúc trong chương trình đối chứng giúp nạp GPU thành công.

`VietsubTranslationWorkerClient` hiện giữ process, handshake, request đang chờ, fingerprint, stderr và task đọc theo từng `WorkerSession`. Reset đánh dấu dừng chủ động, kết thúc request của phiên đó, dừng process và chờ cả stdout/stderr trước khi thay phiên. Kênh đọc bị treo được hủy/đóng với thời hạn; chưa dọn xong thì không mở worker kế tiếp. Dispose hủy cả startup/request và giữ semaphore hợp lệ cho những caller đang thoát. Chẩn đoán chỉ thêm PID, mã thoát và loại sự kiện; không thêm prompt hoặc phụ đề vào log. Không thay protocol, model, GPU planner, cấu hình người dùng hoặc dữ liệu project.

Regression giữ EOF/frame lỗi cũ bằng barrier: trước sửa **0 Passed / 2 Failed / 0 Skipped**, sau sửa các nhánh EOF đến muộn, cancel/reset, dispose trong startup, dispose khi có request chờ và phục hồi sau crash đều đạt. Nhóm worker/acceleration vòng đầu **40 Passed / 0 Failed / 0 Skipped**; full suite trên binary cuối bao gồm thêm test dispose trong startup.

- Restore và Release build solution đạt **0 Warning / 0 Error**. `npm ci`, production web build đạt; Vite vẫn cảnh báo chunk > 500 kB. Frontend **252 Passed / 0 Failed / 0 Skipped**.
- Full .NET mặc định: **1.405 Passed / 1 Failed / 11 Skipped**, tổng 1.417. Lỗi duy nhất `LoginForm_LoadsWebViewAndAuthenticatesThroughTheHost` ở `BeginInvoke` khi window handle đã bị đóng; chạy riêng cùng binary đạt **1 Passed / 0 Failed / 0 Skipped**. Không cộng lượt riêng thành full suite xanh.
- Full .NET chạy lại trên cùng binary với `-- xUnit.ParallelizeTestCollections=false`: **1.406 Passed / 0 Failed / 11 Skipped**, tổng 1.417, một lượt đầy đủ 3 phút 59 giây. Đây là bằng chứng full suite tuần tự đạt; lượt mặc định ở trên vẫn được giữ để theo dõi độ ổn định của test WebView2. Các bài model/SQL opt-in bị Skipped không được tính là đạt.
- GPU thật chạy riêng, không đồng thời với build/test media: **1 Passed / 0 Failed / 0 Skipped**. Test `Hardware_probe_reset_and_standard_GPU_inference_survive_three_cold_starts` thực hiện ba chu kỳ dò GPU → reset → nạp mới, profile Standard `qwen3-cpu-safe-v2`, không bỏ qua cảnh báo tài nguyên. RTX 3050 Laptop / driver 610.62 nạp **24 layer CUDA** ở cả ba chu kỳ, mỗi chu kỳ dịch thành công fixture Anh và Trung (hai cue/ngôn ngữ). Đây là nghiệm thu chức năng worker, không phải benchmark clip dài hoặc smoke UI.

Bằng chứng nằm tại `D:\VideoMakerDiagnostics\gpu-worker-fix-20260917`: `regression-before.trx`, `worker-focused.trx`, `build-release.log`, `full-suite.trx`, `full-suite-sequential.trx`, `login-isolated.trx`, `gpu-three-starts.trx`. Desktop DLL Release đã kiểm có SHA-256 `14e3d0e3f075f50dca383de55e0ad4024ce2762ca33e71f62df6f4b9fc89a63`. Lượt GPU không cài lại component, thay marker READY hoặc sửa project của người dùng. Chưa smoke luồng dịch trong phiên desktop đăng nhập thật, Windows sạch, bundle phát hành hoặc rollout production.

Sau cập nhật binary, mở lại ứng dụng và bắt đầu job Local mới với chế độ `AUTO` để thử GPU. Job cũ đã lưu `CpuFallback=true` vẫn giữ CPU khi resume, nhằm bảo toàn checkpoint; không tự sửa checkpoint hoặc xóa các câu đã commit để bật lại GPU.
