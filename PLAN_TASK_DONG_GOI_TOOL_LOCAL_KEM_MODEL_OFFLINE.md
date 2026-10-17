# Kế hoạch đóng gói TOOL-LOCAL kèm model offline

Ngày lập: 2026-10-08. Nhánh rà soát: `main`, commit `f0651c918f28ca477f28875818148e9e86240b7b`. Working tree sạch trước khi tạo kế hoạch.

Mục tiêu là bàn giao một bộ ZIP Windows x64 có TOOL-LOCAL, các runtime và model cần cho OCR, dịch Qwen và giọng Piper. Máy khách sử dụng tài nguyên đi kèm để chuẩn bị component, không cần tải lại các model này. Model phải được kiểm tra và chạy thử trên máy khách trước khi component được báo `READY`.

Trạng thái hiện tại: **đã triển khai pipeline và tạo ZIP chẩn đoán; chưa đủ điều kiện bàn giao máy khách**. Trạng thái task và bằng chứng mới ở mục 9 và [báo cáo triển khai](TRIEN_KHAI_TOOL_LOCAL_KEM_MODEL_OFFLINE.md).

Ở lượt lập kế hoạch ban đầu chỉ tạo tài liệu, chưa sửa code/build/test/đóng gói. Những kiểm tra đầu vào trước lượt lập kế hoạch được ghi riêng ở mục 2; không thay kết quả triển khai mới.

Kế hoạch này bổ sung Qwen vào phạm vi của [kế hoạch ZIP Piper trước đây](PLAN_TASK_DONG_GOI_ZIP_PHAT_HANH.md). [Báo cáo đóng gói cũ](TRIEN_KHAI_DONG_GOI_ZIP.md) là bằng chứng lịch sử của gói khác, không chứng minh ZIP mới đã đạt.

## 1. Phạm vi bộ gửi khách

Phạm vi cơ bản được đề xuất từ yêu cầu hiện tại là **OCR Anh/Trung + Qwen3 4B + Piper VAIS-1000**, gồm đầy đủ dependency để chạy những engine này. Task PK01 phải chốt danh mục trước triển khai; chưa có xác nhận rằng “đầy đủ model” bao gồm cả 14 voicepack Kokoro, CUDA hoặc bộ chuyển giọng Veo.

| Thành phần | Nội dung phải đi cùng bộ ZIP |
|---|---|
| Desktop | TOOL-LOCAL Release, self-contained `win-x64`, frontend React production và cấu hình triển khai riêng cho khách. |
| Worker dịch | Toàn bộ `_translation_worker`, gồm EXE, assembly, native LLamaSharp CPU và các file runtime tương ứng bản publish. |
| Model dịch | `Qwen3-4B-Q4_K_M.gguf` đúng phiên bản đã pin, size và SHA-256; dùng chung cho các project Vietsub. |
| OCR | Model PaddleOCR Anh/Trung được project tham chiếu, native Paddle/OpenCV và bốn DLL Visual C++ x64 đã pin. |
| Giọng Piper | Payload `piper-1.6.0-python-3.11.15-offline-v3`, Python/uv/wheel/model/config và worker/lockfile khớp định nghĩa đã duyệt. |
| Media | FFmpeg/FFprobe, license, provenance và checksum đã được duyệt đúng phạm vi phân phối. |
| WebView2 | Loader x64 đúng đường dẫn. Chốt máy khách đã có Evergreen Runtime hay cần bộ cài Microsoft được kiểm nguồn/chữ ký đi kèm. Loader không thay thế runtime trình duyệt. |
| Updater và hồ sơ | `_updater`, manifest, inventory, checksum, license/attribution của mọi thành phần thực sự nằm trong ZIP. |

Login, license, organization và workflow SQL vẫn cần kết nối tới môi trường triển khai. Bộ model offline không làm toàn bộ ứng dụng hoạt động offline. Máy khách không phải cài .NET hoặc Python riêng khi bộ self-contained/runtime đi kèm đã được kiểm chứng.

Nếu bổ sung Kokoro, CUDA hoặc Veo local, lập inventory, payload, điều kiện phần cứng và probe riêng cho từng engine trước khi nhận vào gói. Không coi việc có model hoặc WAV nghe thử là đã có runtime tổng hợp giọng.

## 2. Căn cứ và điều kiện hiện tại

Các kết quả dưới đây thuộc các bước kiểm tra trước lượt lập plan trong cùng phiên, trên checkout nêu ở đầu tài liệu. Chúng chỉ xác nhận đầu vào; không phải kết quả build/test hoặc nghiệm thu ZIP mới.

| Đầu vào | Kết quả đã kiểm | Công việc còn cần |
|---|---|---|
| Qwen3 4B trong workspace | File ở `third_party/translation/runtime-test/qwen3-4b-q4-k-m-cpu/bc64014-llamasharp-0.27.0-adapter-1/`; size `2.497.280.256` byte và SHA-256 khớp source. | Kiểm lại bản sao trong ZIP và chạy worker/probe thực tế từ bộ đã giải nén. |
| Piper offline | `Test-PiperOfflineBundle.ps1 -ArtifactOnly` xác nhận hash payload, worker và lockfile hợp lệ; `RuntimeProbed=false`. | Cài mới và probe WAV bằng payload của ZIP cuối trên môi trường chưa có component. |
| Native OCR | `Test-OcrNativeRuntime.ps1` xác nhận đủ bốn DLL và hash hợp lệ. | Kiểm file thực nạp từ thư mục ứng dụng, chạy fixture OCR Anh/Trung bằng ZIP cuối. |
| FFmpeg | Kiểm tra có `-RequireReleaseApproval` đã dừng vì provenance ghi `Approval scope: Development`. | Có hồ sơ rà soát phân phối và bundle được duyệt cho Release trước khi bàn giao. |
| Cấu hình desktop mặc định | Còn SQL host phát triển; `VietsubLocalTranslationEnabled=false`. | Tạo cấu hình bản phân phối riêng, chốt truy cập server/SQL của máy khách và nghiệm thu Qwen trước khi bật cho khách. |
| Lỗi người dùng báo | Ảnh Setup có `TRANSLATION_PROCESS_FAILED`; chưa xác định binary, máy và bước lỗi cụ thể. | Thu thập phiên bản/stage và chẩn đoán worker; không kết luận đổi model sẽ tự sửa lỗi. |

Model Qwen bắt buộc:

```text
File: Qwen3-4B-Q4_K_M.gguf
Engine version: bc64014-llamasharp-0.27.0-adapter-1
Size: 2497280256 bytes
SHA-256: 7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5
Source revision: bc640142c66e1fdd12af0bd68f40445458f3869b
```

Các điểm source ảnh hưởng trực tiếp:

- [Publish-DesktopRelease.ps1](scripts/Publish-DesktopRelease.ps1) đã publish desktop/worker/Piper, tạo manifest và kiểm ZIP sau giải nén. Script hiện chưa tự đưa GGUF Qwen vào gói và đang dùng `Compress-Archive`/`Expand-Archive`; phải xác minh khả năng xử lý entry GGUF lớn hơn 2 GiB trước khi dùng cho gói mới.
- [VietsubTranslationRuntime.cs](TOOL-LOCAL/Vietsub/Translation/VietsubTranslationRuntime.cs) đã có cơ chế tìm model local tại `third_party/translation/runtime-test/<component>/<engine>/<file>`, xác minh rồi đưa vào kho component user trước khi tải mạng. Có thể dùng cơ chế này để kèm model mà giữ nguyên code nghiệp vụ.
- [QwenGgufVietsubTranslationProvider.cs](TOOL-LOCAL/Vietsub/Translation/QwenGgufVietsubTranslationProvider.cs) phải nạp worker và probe Runtime/English/Chinese mới ghi bằng chứng sẵn sàng. `probe.json` ràng buộc máy/runtime, không mang từ máy build sang khách.
- [DesktopReleaseOptions.cs](TOOL-SERVER/Configuration/DesktopReleaseOptions.cs) và cấu hình server mặc định giới hạn artifact ở `2.147.483.648` byte. Dung lượng ZIP phải đo thực tế; không suy từ kích thước GGUF rằng ZIP đã vượt giới hạn.
- [DesktopPackageUpdateService.cs](TOOL-LOCAL/Updates/DesktopPackageUpdateService.cs) giới hạn tổng dữ liệu giải nén ở `4 GiB` và số entry ở `200.000`. Bộ gửi trực tiếp vẫn phải ghi rõ có tương thích updater/repair hiện tại hay không.
- [Test-DesktopSetupPublish.ps1](scripts/Test-DesktopSetupPublish.ps1) đã kiểm OCR/media/WebView2/Piper. Kiểm Qwen thực tế từ worker của ZIP là phần cần bổ sung cho kế hoạch này.

## 3. Đầu vào cần chốt

| Đầu vào | Yêu cầu |
|---|---|
| Danh mục model | Chốt bộ cơ bản hoặc phạm vi mở rộng; ghi rõ engine nào dùng CPU, engine nào cần GPU. |
| Version và build | Dùng version/build riêng, phù hợp ứng dụng/manifest; tạo output mới và giữ gói cũ. Chưa chốt giá trị trong lượt plan. |
| Channel và platform | Đồng nhất cấu hình/manifest theo `win-x64`; channel hiện hành là `Stable`, thay đổi chỉ khi được chỉ định. |
| Môi trường khách | HTTPS server thực tế, Windows x64, tài khoản test, license/organization, CPU/RAM/đĩa và trạng thái WebView2. |
| Workflow SQL | Cơ chế xác thực/kết nối khách dùng được, đúng quyền desktop. Không đóng gói mật khẩu SQL dùng chung hoặc mặc định dựa vào SQL host máy phát triển. |
| Bản đang lỗi | Đường dẫn TOOL-LOCAL.exe, version/build, thao tác Setup và stage cuối trước lỗi worker. |
| Phân phối | Hồ sơ Release của FFmpeg và các dependency/model/voice preview thực nằm trong gói. Không đổi nhãn phê duyệt chỉ để qua script. |

Tạo `appsettings.json` dành cho bộ phân phối trong vùng staging riêng; giữ cấu hình source và cấu hình máy người dùng. Cấu hình ứng viên dùng để nghiệm thu Qwen có thể bật Dịch Local; bản bàn giao chỉ bật khi các bài model/benchmark/smoke yêu cầu đã đạt. Các feature ngoài danh mục đã chốt phải có trạng thái rõ ràng.

## 4. Danh sách task

Bảng dưới đây xác định phạm vi và tiêu chí task. Trạng thái thực hiện xem mục 9. `P0` là điều kiện phải giải quyết trước khi bàn giao, `P1` là công việc tạo và nghiệm thu gói.

| Task | Ưu tiên | Công việc | Tiêu chí hoàn tất |
|---|---|---|---|
| PK01 | P0 | Chốt phạm vi và môi trường khách | Có danh mục model, version/build/channel, profile Windows/WebView2, endpoint và cơ chế SQL sử dụng được; chưa đưa secret vào tài liệu/gói. |
| PK02 | P0 | Chẩn đoán `TRANSLATION_PROCESS_FAILED` | Phân biệt lỗi tải, nạp backend, probe/inference và hủy. Ghi stage, worker version/exit/error type an toàn; tái hiện trên bản thử. Có bằng chứng gói ứng viên vượt probe. Nếu cần sửa runtime, tách thay đổi nhỏ kèm regression và báo phạm vi trước triển khai. |
| PK03 | P0 | Kiểm kê model và dependency | Inventory đầy đủ filename/version/size/hash/license; xác minh Qwen, payload Piper, OCR, worker CPU, media, WebView2 và updater. Không lấy runtime từ PATH/System32 hoặc môi trường Python máy dev. |
| PK04 | P0 | Hoàn tất điều kiện phân phối | FFmpeg có bằng chứng Release; rà soát license/notice của Python, uv, Piper, Qwen, native OCR và mọi preview Kokoro đã nằm trong frontend. Quyền Kokoro/LarVoice chưa được xác minh phải được giải quyết trước khi phân phối asset liên quan. |
| PK05 | P0 | Chuẩn bị cấu hình khách | Cấu hình riêng qua kiểm tra deployment; HTTPS/channel/platform đúng, SQL chỉ workflow được phép, không có secret/path máy dev. Có kiểm chứng truy cập môi trường khách bằng tài khoản test được chỉ định; chưa chạy migration thật. |
| PK06 | P1 | Publish ứng viên từ source được chốt | Desktop Release self-contained x64, frontend hiện hành, toàn bộ worker/native, OCR, Piper, WebView2 loader và updater đi cùng. Lưu commit/source snapshot và output riêng; không ghi đè gói cũ. |
| PK07 | P0 | Tạo ZIP chứa GGUF lớn | Dùng cách tạo/giải nén đã chứng minh hỗ trợ entry `2.497.280.256` byte và Unicode. Thêm model trước khi chốt inventory/manifest/hash. Nếu script hiện hành không đáp ứng, cập nhật tối thiểu phần đóng gói có kiểm tra đường dẫn/hash; giữ gate Release. Đo ZIP/tổng dữ liệu giải nén và ghi tương thích giới hạn server/updater. |
| PK08 | P1 | Build và kiểm thử theo phạm vi | Chạy lệnh chuẩn nếu triển khai có thay source/script; kiểm local reuse không HTTP, checksum sai, payload thiếu/hỏng, hủy/thử lại, worker isolation và manifest chứa model. Model thật chạy riêng sau build; báo Passed/Failed/Skipped. |
| PK09 | P0 | Kiểm chính ZIP sau giải nén | Inventory/hash đủ; OCR Anh/Trung, FFmpeg, WebView2 và Piper offline đạt. Harness dùng EXE/native worker của ZIP, Qwen được lấy từ model kèm gói và probe thực tế Anh/Trung → Việt. Không dùng model/cache/worker máy build để thay kết quả này. |
| PK10 | P0 | Nghiệm thu trên Windows/profile sạch | User thường, component/cache mới, thư mục có dấu/khoảng trắng, CWD ngoài repository. Startup qua auth/license/organization và gate hai lớp; cài Qwen/Piper từ gói, dịch/tạo WAV, playback/xuất MP4 fixture, mở lại. Kiểm nhánh cảnh báo RAM khi áp dụng; không bỏ checksum/probe hoặc lỗi OOM thật. |
| PK11 | P1 | Chốt artifact và hướng phục hồi | Ghi checksum ZIP cuối, size nén/giải nén, thành phần, model hash, prerequisite, môi trường kiểm và các giới hạn. Giữ ZIP cũ; xác minh phục hồi không làm mất workspace/tùy chọn/token. Repair/update qua server chỉ ghi đạt khi đã kiểm đúng kênh/giới hạn/package. |
| PK12 | P1 | Bàn giao bộ ZIP và biên bản | Giao đường dẫn ZIP/checksum/inventory/hướng dẫn; có kết quả PK01–PK11 và các mục chưa xác minh. Việc gửi trực tiếp cho khách hoặc upload/Active release trên server thực hiện theo chỉ định tương ứng. |

Thứ tự thực hiện: PK01 → PK02/PK03/PK04/PK05 → PK06 → PK07 → PK08 → PK09 → PK10 → PK11 → PK12. Những bước kiểm kê có thể tiếp tục trong lúc chờ thông tin máy khách; PK02 và PK04 phải được giải quyết trước khi kết luận gói dùng được để bàn giao.

Phạm vi thay đổi ưu tiên là cấu hình bản phân phối và pipeline đóng gói. Không tự mở rộng sang thay model, chuyển toàn bộ workflow SQL sang API, nâng giới hạn updater hoặc sửa luồng AI cloud. Nếu cần những thay đổi đó, xác định task và tiêu chí riêng.

## 5. Bố cục gói dự kiến

```text
VideoMaker/
  TOOL-LOCAL.exe
  appsettings.json
  update-manifest.json
  _translation_worker/              # toàn bộ publish worker CPU
  _updater/                        # toàn bộ publish updater
  runtimes/win-x64/native/WebView2Loader.dll
  msvcp140.dll
  vcruntime140.dll
  vcruntime140_1.dll
  vcomp140.dll
  tools/ffmpeg/                    # binary và hồ sơ phân phối
  workers/                         # Piper worker và lockfile
  components/piper/piper-1.6.0-python-3.11.15-offline-v3/
    manifest.json
    piper-offline.zip
  third_party/translation/runtime-test/
    qwen3-4b-q4-k-m-cpu/
      bc64014-llamasharp-0.27.0-adapter-1/
        Qwen3-4B-Q4_K_M.gguf
  third_party/                      # notice/license/provenance liên quan
  ...                              # các file frontend/OCR/runtime khác theo publish
```

Đây là các đường bắt buộc nổi bật, không thay inventory toàn bộ file. Model Qwen dùng đường tìm local đã có trong source; khi cài, ứng dụng đưa model hợp lệ vào kho component của user bằng hard link hoặc sao chép rồi probe.

Không đưa workspace/project/media của người dùng, token/session, WebView2 profile, venv/cache máy build, `probe.json` hoặc marker READY vào ZIP. Các thành phần trên máy khách phải tạo bằng chứng sẵn sàng mới.

## 6. Ma trận nghiệm thu

| Ca kiểm | Điều kiện đạt |
|---|---|
| Qwen từ bộ offline | Model trong ZIP đúng size/hash; cài thành công khi luồng tải model bị chặn riêng cho phép thử; worker của chính ZIP vượt probe Anh/Trung → Việt. |
| Worker Qwen | Không còn `TRANSLATION_PROCESS_FAILED` trong các ca đã tái hiện; ghi code/stage/error type an toàn; không log prompt/transcript. Worker được dọn sau hủy/thoát, retry không dùng kết quả chưa được kiểm. |
| Model hỏng hoặc thiếu | Thử trên bản sao riêng; checksum sai không READY. Ghi đúng hành vi fallback tải hiện tại và điều kiện mạng, không hứa toàn bộ UI đã có chế độ offline tuyệt đối. |
| Piper mới cài | Python/wheel/model lấy từ payload local; WAV tiếng Việt có RIFF/PCM, hash và tín hiệu hợp lệ. Không dùng venv/READY cũ. |
| OCR và media | OCR Anh/Trung đạt fixture; bốn DLL được nạp tại ứng dụng; FFmpeg/FFprobe kiểm video/audio/thời lượng và xuất MP4. |
| Máy khách sạch | Không cần .NET/Python dev; đáp ứng WebView2 đã chốt. Component/cache mới, đường dẫn Unicode, mở lại ít nhất ba lần, không nhầm marker máy khác. |
| Startup và quyền | Dashboard/modal/gate C# giữ đúng hành vi; component cần thiết chưa READY vẫn chặn command; session/license/organization và context được kiểm thực tế. |
| Mạng | Chứng minh phần chuẩn bị Qwen/Piper dùng file đi kèm bằng phương pháp giới hạn theo tiến trình/nguồn model. Login/server/SQL vẫn hoạt động; không đổi DNS/proxy/firewall toàn máy. |
| ZIP lớn và update | Nén/giải nén entry GGUF thành công, file cuối khớp hash. Ghi số byte ZIP/tổng giải nén và kết quả đối chiếu giới hạn 2 GiB/4 GiB; không tự nâng cấu hình server hoặc bypass giới hạn. |
| Đối chiếu cuối | Hash/inventory khớp đúng ZIP bàn giao; các probe chạy từ bản giải nén cuối. Mọi thay đổi nội dung sau kiểm phải tạo lại hash và kiểm lại phần ảnh hưởng. |

`--check-bundled-components` không cài/probe Qwen; `--check-desktop` và `Verify-VietsubTranslationModel.ps1` cũng không tự thay thế nghiệm thu model/worker của ZIP trong Startup. Test source với model thật là bằng chứng riêng, phải gắn đúng binary và môi trường.

## 7. Kiểm thử dự kiến khi triển khai

Các lệnh ở mục này chưa chạy trong lượt lập kế hoạch. Tuân thủ [hướng dẫn kiểm thử](KIEM_THU_VA_NGHIEM_THU.md) và [runbook phát hành](VAN_HANH_VA_PHAT_HANH.md).

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Frontend, chạy trong `TOOL-LOCAL/Web`:

```powershell
npm ci --no-audit --no-fund
npm run build
npm test
```

Kiểm đầu vào và publish, sau khi chốt đủ PK01–PK05:

```powershell
.\scripts\Test-PiperOfflineBundle.ps1 -ArtifactOnly
.\scripts\Test-OcrNativeRuntime.ps1 -BundleDirectory '<ocr-native-runtime>'
.\scripts\Test-FfmpegBundle.ps1 -BundlePath '<release-approved-ffmpeg>' -RequireReleaseApproval
```

Tham số publish dự kiến gồm version/build/channel/server, `-AppSettingsPath`, `-AllowTransitionalSql`, đường payload Piper và runtime OCR đã kiểm. Chỉ chạy pipeline đã xử lý PK07; không giả định thêm GGUF vào ZIP của script cũ là đủ.

Sau giải nén gói cuối:

```powershell
.\scripts\Test-DesktopSetupPublish.ps1 -PublishRoot '<extracted-final-zip>' `
  -ProbeWebView2 -ProbeBundledComponents -ProbePiperOffline
```

Chạy riêng harness worker/model Qwen của PK09 và smoke Startup của PK10. Giữ build, model inference và media nặng theo thứ tự để tránh tranh tài nguyên. Test model/SQL bị `Skipped` không tính là đạt. Mọi kết quả phải ghi Passed/Failed/Skipped hoặc trạng thái probe thực tế, commit/source snapshot và môi trường.

## 8. Điều kiện bàn giao

- [ ] PK01 chốt danh mục model và môi trường khách; không thiếu cấu hình SQL cần cho binary hiện hành.
- [ ] Lỗi worker đã được chẩn đoán và gói cuối vượt probe Qwen thật.
- [ ] Hồ sơ phân phối hợp lệ cho các binary/model/preview thực nằm trong gói.
- [ ] ZIP64 hoặc cách đóng gói được chọn đã qua kiểm entry model lớn, Unicode và hash sau giải nén.
- [ ] Build/test cần thiết và các probe ZIP cuối đạt; Failed/Skipped và phạm vi chưa kiểm được ghi riêng.
- [ ] Windows/profile sạch, Startup có auth/license/organization và OCR/dịch/giọng/xuất MP4 fixture đã được nghiệm thu.
- [ ] ZIP, `checksums.sha256`, inventory và hướng dẫn mở/cài component/báo lỗi/phục hồi được bàn giao cùng nhau.
- [ ] Báo cáo mới phân biệt rõ source, kiểm thử tự động, smoke máy khách và rollout production.

Khi thiếu hồ sơ Release, chưa có cơ chế SQL cho khách, probe Qwen thất bại hoặc chưa kiểm máy/profile đích, giữ gói ở trạng thái ứng viên và ghi điều kiện còn thiếu. Không tự upload/Active release, chạy migration database thật hoặc tạo request provider có phí để hoàn thành kế hoạch.

## 9. Trạng thái triển khai — 2026-10-08

Nhánh/commit nền giữ như đầu tài liệu, cùng working tree chưa commit. Đã tạo ứng viên `1.0.0 / build 20261008`, có Qwen3 4B CPU, OCR và payload Piper. ZIP đúng hash, 3.052.786.145 byte; tổng giải nén 3.707.168.567 byte. Model từ ZIP đã qua size/SHA-256; probe Qwen bị chặn bởi `TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED`, chưa được tính READY.

| Task | Trạng thái | Bằng chứng hoặc điều kiện còn thiếu |
|---|---|---|
| PK01 | Một phần | Triển khai phạm vi cơ bản đã đề xuất; còn xác nhận danh mục và cấu hình/môi trường khách. |
| PK02 | Một phần | Có chẩn đoán offline ghi code/stage/error type; lỗi screenshot trên binary máy khách chưa tái hiện. Lỗi máy đóng gói là cảnh báo tài nguyên trước khi nạp Qwen. |
| PK03 | Đạt kỹ thuật ở ứng viên | Inventory xác minh 325 file, ZIP 326 entry gồm inventory; model/payload/native đúng pin. Hồ sơ phân phối thuộc PK04. |
| PK04 | Chưa đạt | Kiểm FFmpeg Release bị từ chối vì Development; rà soát dependency và preview Kokoro còn cần trước phân phối. |
| PK05 | Chưa đạt | Cấu hình nguồn bị từ chối vì SQL host phát triển; ứng viên dùng cấu hình rỗng, chưa dùng đăng nhập/workflow khách. |
| PK06 | Đạt ứng viên chẩn đoán | Publish self-contained x64 cùng frontend, worker/native, OCR, Piper, media và updater. Chưa phải bản triển khai khách. |
| PK07 | Đạt kỹ thuật ở ứng viên | Nén/giải nén theo stream, GGUF >2 GiB đúng hash sau giải nén, đường dẫn có dấu; manifest quản lý model. ZIP vượt cap upload server mặc định, phù hợp gửi file riêng sau đủ điều kiện. |
| PK08 | Đạt phạm vi đã chạy | Restore/build đạt; đóng gói 18 Passed/0 Failed/0 Skipped; Full C# 1.594 Passed/0 Failed/13 Skipped; frontend 280 Passed/0 Failed/0 Skipped. Model/GPU/SQL opt-in chưa được tính đạt. |
| PK09 | Một phần | Inventory, WebView2, OCR/media và Piper offline từ ZIP đạt. Qwen chưa nạp/probe vì thiếu RAM/commit khả dụng; đang chờ xác nhận thử tài nguyên hoặc máy đủ tài nguyên. |
| PK10 | Chưa thực hiện | Chưa có Windows/profile sạch, tài khoản và auth/license/organization máy khách; chưa smoke Startup/UI/MP4 của khách. |
| PK11 | Một phần | Có ZIP/checksum/inventory/qualification và giới hạn; chưa nghiệm thu repair/update/rollback môi trường khách. |
| PK12 | Một phần | Artifact chẩn đoán và báo cáo nằm trong workspace; chưa bàn giao bản khách, gửi ra ngoài hoặc upload release. |

Chi tiết file, lệnh, kết quả và điều kiện tiếp tục nằm trong [báo cáo triển khai](TRIEN_KHAI_TOOL_LOCAL_KEM_MODEL_OFFLINE.md). Cấu hình nguồn và provenance FFmpeg được giữ nguyên.
