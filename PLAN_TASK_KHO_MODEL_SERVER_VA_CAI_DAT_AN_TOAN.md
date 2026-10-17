# Kế hoạch kho model trên server và cài đặt an toàn

Ngày lập: 2026-10-16, múi giờ Asia/Bangkok. Nhánh rà soát `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, cùng working tree có thay đổi chưa commit. Commit nền không đại diện đầy đủ cho source đang có.

Mục tiêu là tập hợp đúng bộ Qwen 4B, Piper offline và OCR đồng bộ, đưa lên kho file riêng, nghiệm thu đúng bộ đã tập hợp, rồi triển khai cơ chế tải có xác thực và phục hồi trên desktop. Khi khách chạy Setup, ứng dụng phải chuẩn bị được component còn thiếu và chỉ báo `READY` sau khi kiểm tra thực tế trên máy khách.

Trạng thái cập nhật ngày 2026-10-16: **đã triển khai source, tạo bộ bàn giao và kiểm thử tại máy phát triển trong phạm vi đã ghi** theo yêu cầu tiếp theo của người dùng. Full .NET/frontend, HTTPS local với GGUF >2 GiB, probe Qwen/Piper/OCR và Qwen 1 cold start/30 fixture đã có bằng chứng. Kết quả chi tiết, lỗi đường dẫn dài Piper và phần việc còn phụ thuộc server/khóa ký/máy sạch được ghi trong [biên bản triển khai](TRIEN_KHAI_KHO_MODEL_SERVER_VA_CAI_DAT_AN_TOAN.md). Các nhận định ở mục 2 là baseline lúc lập kế hoạch; không dùng chúng thay kết quả source/test mới. Chưa upload hoặc rollout production.

Kế hoạch này bổ sung kênh phân phối model riêng cho [kế hoạch bộ offline](PLAN_TASK_DONG_GOI_TOOL_LOCAL_KEM_MODEL_OFFLINE.md). Bộ offline vẫn là một chế độ bàn giao cần được giữ tương thích. Các hướng dẫn chung tiếp tục theo [nghiệp vụ](NGHIEP_VU_HE_THONG_VIDEOMAKER.md), [kiến trúc](KIEN_TRUC_KY_THUAT.md), [vận hành](VAN_HANH_VA_PHAT_HANH.md) và [kiểm thử](KIEM_THU_VA_NGHIEM_THU.md).

## 1 Mục tiêu và phạm vi

### 1.1 Bộ chính

| Thành phần | Phạm vi được chọn | Cách phân phối mục tiêu |
|---|---|---|
| Qwen | Qwen3 4B Q4_K_M, CPU Windows x64, dịch Anh/Trung sang Việt | GGUF riêng trên kho model; worker và native CPU đi theo bản desktop tương thích. |
| Piper | VAIS-1000 medium và bộ `piper-1.6.0-python-3.11.15-offline-v3` | Giữ nguyên ZIP và manifest đã pin; tải payload rồi cài hoàn toàn từ file local. |
| OCR | PaddleOCR EnglishV5/ChineseV5 cùng managed/native dependencies được pin | Lưu bộ nguồn đồng bộ trong kho bàn giao; phiên bản đầu tiếp tục giao OCR cùng package ứng dụng. |
| FFmpeg/FFprobe | Dependency của Setup và xuất media, không phải model | Tiếp tục theo bundle desktop được kiểm provenance/checksum và điều kiện Release. |

Kokoro là nhánh tùy chọn, không trở thành dependency bắt buộc của startup. File hiện có mới bao phủ một voicepack trong catalog 14 giọng; khi mở nhánh này phải có inventory, runtime, quyền phân phối và nghiệm thu riêng. Qwen 1.7B, CUDA/GPU và các model thay thế không nằm trong bộ chính.

### 1.2 Kết quả cần đạt

- Có bộ bàn giao bất biến, đủ file, metadata, nguồn, license, checksum và bảng tương thích.
- File ở server và file khách tải về phải trùng byte với bộ đã nghiệm thu.
- Ứng dụng tải tiếp đúng file khi mạng gián đoạn, không ghép hai phiên bản hoặc dùng file chưa xác minh.
- Cài mới, cập nhật, hủy, mở lại và phục hồi giữ được bộ đang hoạt động cùng dữ liệu người dùng.
- Qwen/Piper chỉ `READY` sau probe gắn đúng model, worker/runtime, cấu hình và máy hiện tại.
- Build ứng dụng nhỏ hơn được tách khỏi model lớn; sửa giao diện không buộc tải lại GGUF còn hợp lệ.

Login, license và quyền chạy nghiệp vụ vẫn theo kiến trúc hiện hành. Model Local không tạo đường gọi provider trực tiếp, không thêm BYOK và không biến toàn bộ sản phẩm thành ứng dụng offline. Việc chuyển workflow SQL sang API không thuộc kế hoạch này.

## 2 Đầu vào đã kiểm và giới hạn của bằng chứng

### 2.1 File đã kiểm trong cùng cuộc trao đổi

| Đầu vào thực tế | Kích thước byte | Kết quả kiểm file | Chạy runtime trong lượt kiểm kê |
|---|---:|---|---|
| Qwen3 4B GGUF trong workspace | 2.497.280.256 | Khớp size và SHA-256 source. Bản đang cài cũng có cùng hash. | Chưa chạy. |
| Piper `piper-offline.zip` | 159.970.075 | Khớp size và SHA-256 định nghĩa nhúng. | Chưa chạy. |
| Piper `manifest.json` | 977.507 | Khớp size và SHA-256; 3.254/3.254 entry ZIP khớp size/hash, tổng giải nén 231.194.288 byte. | Chưa chạy. |
| Piper ONNX và JSON đang cài | 63.201.294 và 4.860 | Khớp pin của model/cấu hình. | Chưa chạy. |
| Năm gói NuGet OCR/native được ghi trong source | Tổng 260.231.811 | SHA-512 raw archive khớp hồ sơ source và sidecar của cả năm gói. | Chưa chạy. |
| Hai assembly model OCR trong bản Release hiện có | 21.835.264 và 116.122.624 | Hash trùng assembly đọc trực tiếp từ package được pin. | Chưa chạy. |
| Kokoro tùy chọn | ONNX 325.731.953, config 2.351, voice Diễm Trinh 523.838 | Ba file hiện có khớp catalog source; chưa đủ toàn bộ 14 voicepack. | Chưa chạy. |

Đây là bằng chứng về tính toàn vẹn file trên máy kiểm kê. Không dùng nó để kết luận bộ bàn giao mới đã được copy đúng, file trên server đúng, runtime chạy ổn định, hoặc đã rollout. Khi tạo bộ bàn giao phải tính lại hash sau copy và lưu báo cáo mới.

### 2.2 Pin của bộ chính

| File | SHA-256 kỳ vọng |
|---|---|
| `Qwen3-4B-Q4_K_M.gguf` | `7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5` |
| `piper-offline.zip` | `e2d1b5f212d2cc6a3b547f9280e380416c57c5c151d639595009624caf4f6e1d` |
| Piper `manifest.json` | `69bfe70e9ef038f2c157db035cf6f59ae8310028359d93b1dc16de73c691f1b5` |
| `vi_VN-vais1000-medium.onnx` | `ec7c89e2c85f4d1edc24b6120c18aaf1bda614f06b511567eb9c7c0de15e2dab` |
| `vi_VN-vais1000-medium.onnx.json` | `fafb9da1354ed4b77c31af228ed41fb41cd825c14cffa105454b25e6ae751ee0` |

Qwen dùng source revision `bc640142c66e1fdd12af0bd68f40445458f3869b`, engine `bc64014-llamasharp-0.27.0-adapter-1`. Worker hiện tại là `1.2.1`, IPC `3`, LLamaSharp/CPU `0.27.0`. Lưu các giá trị này thành bảng tương thích; không suy protocol từ tài liệu manifest cũ của một module phụ.

OCR dùng `Sdcb.PaddleOCR`, `.Models.Local`, `.Models.LocalV5` phiên bản `3.3.1`; `Sdcb.PaddleInference.runtime.win64.mkl` `3.3.1.70`; `OpenCvSharp4.runtime.win` `4.11.0.20250507`. SHA-512 kỳ vọng lấy từ [hồ sơ NuGet OCR](third_party/ocr/NUGET_HASHES.sha512), không tự tính pin mới để hợp thức hóa file sai.

### 2.3 Những ràng buộc source phải xử lý

| Ràng buộc | Source hiện hành | Hệ quả với kế hoạch |
|---|---|---|
| Artifact server tối đa 2 GiB | [DesktopReleaseOptions](TOOL-SERVER/Configuration/DesktopReleaseOptions.cs), [storage](TOOL-SERVER/Updates/DesktopReleaseStorage.cs) | Giới hạn áp cả file upload và tổng ZIP giải nén. Qwen riêng đã vượt mức này; tạo kho component riêng, giữ giới hạn release cũ. |
| Chỉ có desktop ZIP và Setup EXE | [release domain](TOOL-SERVER/Domain/Updates/AppRelease.cs), [launcher API](TOOL-SERVER/Controllers/LauncherDistributionController.cs) | Lưu file staging chưa tự tạo API model hoặc khả năng tải trong desktop. |
| Bật dịch Local phải kèm GGUF trong publish | [Publish-DesktopRelease.ps1](scripts/Publish-DesktopRelease.ps1) | Cần chế độ phát hành online/offline rõ ràng và task đổi gate, sau khi downloader đạt. |
| Qwen/Piper chưa resume | [Qwen store](TOOL-LOCAL/Vietsub/Translation/VietsubTranslationRuntime.cs), [Piper store](TOOL-LOCAL/Vietsub/Voice/VietsubVoiceComponentStore.cs) | Không coi HTTP Range của server là client đã tải tiếp được. |
| Piper tin định nghĩa nhúng trong assembly | [PiperOfflineBundle](TOOL-LOCAL/Vietsub/Voice/PiperOfflineBundle.cs) | Giữ pin ZIP/manifest/worker/lockfile; chuyển nguồn tải không được làm mất gốc tin cậy. |
| OCR liên kết với app | [csproj](TOOL-LOCAL/TOOL-LOCAL.csproj), [recognizer](TOOL-LOCAL/Vietsub/Ocr/PaddleVietsubOcrRecognizer.cs) | Phiên bản đầu không hot-load hoặc thay riêng assembly/native OCR. |
| Setup có gate hai lớp | [startup workflow](TOOL-LOCAL/SystemSetup/StartupSystemSetupWorkflow.cs), [host gate](TOOL-LOCAL/SystemSetup/StartupSystemSetupGate.cs) | Tải xong không tự mở khóa nghiệp vụ; vẫn cần probe của component. |
| Công tắc Piper chỉ chọn lượt Setup | [SystemSetupPanel](TOOL-LOCAL/Web/src/features/systemSetup/SystemSetupPanel.tsx) | Không biến việc bỏ chọn thành tắt tính năng hoặc bỏ dependency của startup. |

## 3 Thiết kế triển khai được chọn cho phiên bản đầu

### 3.1 Hai loại bộ phân phối

- **Bộ online:** Setup cài desktop cùng worker, OCR, media và các file nhỏ cần thiết. Qwen và payload Piper được tải từ kho component sau khi ứng dụng xác minh quyền Setup.
- **Bộ offline:** chứa cùng các artifact Qwen/Piper đã nghiệm thu, giúp cài khi không tải được model từ mạng. Bản đủ GGUF dùng kênh bàn giao riêng đã được chỉ định, như USB/chuyển file, không đi qua `App_Releases` giới hạn 2 GiB hiện hành. Vẫn kiểm giới hạn 4 GiB/200.000 entry của pipeline Direct và đo dung lượng thực tế; nếu vượt, cần thiết kế bộ bàn giao khác thay vì tự nới cap. Login/license tiếp tục cần kết nối theo sản phẩm.

Hai chế độ phải dùng cùng pin và compatibility matrix. Không duy trì hai bộ model khác nhau có cùng version. Client cũ tiếp tục dùng kênh release/package tương thích trong thời gian hỗ trợ; chưa bỏ payload khỏi package của client cũ.

Bộ offline của phiên bản đầu tin pin model/manifest/worker nhúng trong bản ứng dụng được ký và đã phân phối đúng kênh. Nó chỉ cài đúng bộ app đã phê duyệt, không nhận model/version mới từ file sidecar tùy ý. Catalog online hết hạn không tự làm mất khả năng chuẩn bị bộ offline đúng pin này; thông tin revoke đã được client xác thực vẫn phải được tôn trọng. Đây là policy tương thích với bộ offline hiện hành, không phải cách bỏ qua verifier cho import model bất kỳ.

### 3.2 Kho server

Chọn kho file/static hoặc object storage riêng, có vùng staging không public và vùng artifacts bất biến. Dữ liệu nhị phân không lưu vào SQL. Phiên bản đầu dùng metadata ký và hồ sơ thao tác trên filesystem; chưa cần schema hoặc UI Admin mới. Nếu cần quản trị catalog/audit bằng database về sau, tách ADR và migration idempotent riêng.

Metadata và payload công khai chỉ có quyền đọc, theo kiểu phân phối launcher hiện có, sau khi rà quyền phân phối. Chỉ máy/người phát hành được ghi staging và promote; tiến trình phục vụ download chỉ có quyền đọc vùng đã publish. Quyền tải model công khai không cấp quyền sử dụng công cụ: cài/chạy job vẫn kiểm session/license/device và context hiện hành. Local cá nhân không bị thêm yêu cầu organization.

Catalog/payload không có secret, connection string, token, đường dẫn máy người dùng hoặc prompt. Kênh ingest ban đầu là chuyển file bởi người vận hành tới staging; server runtime không fetch URL do client/Admin nhập. Đích server, domain HTTPS, thư mục và người vận hành phải được xác định trước bước chuyển thật.

### 3.3 Xác thực phát hành

Ưu tiên dùng client/thư viện triển khai TUF đã được rà khả năng tích hợp Windows/.NET, với root tin cậy đi cùng desktop. Task MD15 phải chốt giải pháp và kiểm thử tương thích trước khi code luồng tải. Nếu giải pháp chưa đáp ứng, giữ chế độ offline hiện hành và trình phương án thay thế cụ thể; không âm thầm giảm xuống chỉ kiểm checksum từ cùng server.

Metadata phải được kiểm chữ ký, phiên bản và hiệu lực trước khi dùng; kiểm liên kết metadata để chống trộn các đợt phát hành. Khóa phát hành quan trọng tách khỏi máy web; quy trình có rotation/revocation và kiểm root mới theo chuỗi tin cậy. Đây là các yêu cầu thiết kế theo [TUF specification](https://theupdateframework.github.io/specification/latest/), không phải tuyên bố source hiện đã triển khai TUF.

Ký số Setup/EXE bằng Authenticode là task riêng với ký metadata model. Ký các executable do dự án phát hành trước khi chốt hash/inventory; giữ chữ ký, license và checksum của dependency bên thứ ba. Không đặt private key trong repository hoặc kho tải. [Microsoft SignTool verification](https://learn.microsoft.com/en-us/windows/win32/seccrypto/using-signtool-to-verify-a-file-signature).

### 3.4 Bộ bàn giao và bố cục lưu trữ

Bố cục logic dự kiến sau đây chưa được tạo trong lượt lập kế hoạch. Các tên metadata cuối cùng theo thư viện được chốt ở MD15; `inventory.json` kỹ thuật không thay metadata đã được xác thực.

```text
<handover-root>/<candidate-id>/
  inventory.json
  checksums.sha256
  source-snapshot.json
  compatibility.json
  artifacts/
    qwen/<component-id>/<engine-version>/Qwen3-4B-Q4_K_M.gguf
    piper/<bundle-version>/manifest.json
    piper/<bundle-version>/piper-offline.zip
    ocr/<compatibility-id>/build-inputs/<five-pinned-packages>.nupkg
  provenance/
  licenses/
  qualification/
    artifact-verification.json
    runtime-results/

<server-model-root>/
  staging/<candidate-id>/
  artifacts/<component-id>/<version>/<sha256>/<file>
  metadata/<channel>/
  operation-records/
```

OCR `build-inputs` là đầu vào phục vụ build/repair và truy nguyên; không phải gói đã được desktop hỗ trợ cài trực tiếp. Bộ publish tương thích cần được lưu tham chiếu riêng cho nghiệm thu. Piper giữ ZIP nguyên bản; `installedFiles` trong manifest mô tả môi trường sẽ tạo trên máy khách, không yêu cầu copy `.venv` từ máy build.

Máy khách dùng kho component dưới tài khoản Windows hiện hành, tách vùng tải tạm, staging, version đã cài và bằng chứng probe. Giữ khả năng tìm model cũ ở vị trí legacy đã được source hỗ trợ, xác minh trước khi dùng lại; không tự di chuyển/xóa workspace người dùng.

### 3.5 Danh mục và vòng đời

Mỗi component phải có ID/version, nguồn/revision, file format, size/hash chính xác, đường dẫn tương đối, dependency, platform, giới hạn giải nén và bản desktop/worker/protocol tương thích. Danh mục còn phải chỉ rõ component bắt buộc hay tùy chọn theo chế độ phát hành; signed catalog không được tự mở thêm engine hoặc executable ngoài allowlist của app.

Trạng thái bằng chứng được giữ tách biệt:

| Trạng thái | Ý nghĩa |
|---|---|
| `Inventoried` | Đã lập danh mục, chưa đủ bằng chứng copy/hash. |
| `ArtifactVerified` | File của candidate khớp inventory/pin sau copy. |
| `ServerVerified` | Bản ở server và bản tải lại khớp candidate. |
| `RuntimeQualified` | Đúng candidate đạt ma trận runtime trên môi trường được ghi rõ. |
| `Published` | Metadata phát hành trỏ tới đúng artifact đã đạt điều kiện. |
| `Retired` hoặc `Revoked` | Dừng cung cấp mới hoặc thu hồi theo chính sách được ký; giữ lịch sử và hồ sơ xử lý. |

`READY` là trạng thái component trên một máy khách, không phải trạng thái catalog. File Published vẫn phải probe trên máy mới. Không copy `probe.json`, marker READY, diagnostics, session, cache hoặc venv đã cài vào bộ bàn giao.

## 4 Đầu vào cần xác định trước từng bước có tác động

| Đầu vào | Cần cho | Trạng thái hiện tại |
|---|---|---|
| Server đích, staging root và người chuyển file | MD07–MD09 | Chưa xác định cụ thể trong yêu cầu. Không suy từ cấu hình checkout. |
| Domain HTTPS, nơi phục vụ file, proxy/CDN và quyền filesystem | MD09, MD18, MD21 | Cần khảo sát môi trường được chỉ định. |
| Candidate ID, desktop version/build/channel và source snapshot | MD02, MD04 | Dùng bản mới, không ghi đè candidate cũ. |
| Máy nghiệm thu, Windows/CPU/RAM/đĩa, tài khoản/license test | MD10–MD14, MD33 | Chưa chốt ma trận máy đích. |
| Server auth/registry và schema phù hợp binary dùng để smoke | MD33 | Preflight đọc trên đúng môi trường; Local cá nhân cần registry hỗ trợ migration 4.1.16. Thiếu schema đưa sang runbook môi trường riêng, không tự áp SQL trong đợt gom model. |
| Tài nguyên Qwen và việc chấp nhận cảnh báo khi cần | MD10, MD13 | Không coi im lặng hoặc timeout là chấp thuận. |
| Quyền phân phối model/runtime, FFmpeg Release | MD03, MD31, MD35 | Hồ sơ FFmpeg trong source vẫn ghi Development; cần giải quyết điều kiện Release trước giao khách. Giữ hồ sơ nguồn/license, không tự đổi nhãn để qua gate. |
| Chủ thể giữ khóa, chính sách ký, hết hạn và phục hồi | MD15, MD17 | Thiết kế trước; chưa sinh/rotate khóa thật. |
| Môi trường rollout và nhóm canary | MD35 | Chỉ triển khai khi môi trường và tác động được chỉ rõ. |

Thiếu thông tin server không chặn công việc kiểm kê và chuẩn bị candidate local. Nó chặn bước chuyển hoặc công bố file lên môi trường đó. Kế hoạch không ủy quyền chạy migration, gọi provider có phí, restart server hoặc publish production.

## 5 Task chốt bộ chính

Các bảng dưới đây giữ tiêu chí nghiệm thu và phụ thuộc của kế hoạch gốc. Trạng thái thực hiện mới nằm trong [biên bản triển khai](TRIEN_KHAI_KHO_MODEL_SERVER_VA_CAI_DAT_AN_TOAN.md), không suy rằng toàn bộ task đã đạt chỉ vì có source. Ưu tiên P0 là điều kiện bắt buộc trước bước phụ thuộc; P1 là công việc hoàn thiện trong cùng đợt. Các mã MD là mã task của kế hoạch.

| Mã | Ưu tiên | Công việc | Đầu ra và tiêu chí hoàn tất | Phụ thuộc |
|---|---|---|---|---|
| MD01 | P0 | Chốt bộ Qwen 4B/Piper/OCR, phạm vi CPU, online/offline, môi trường cần hỗ trợ. | Danh mục được ghi rõ; Kokoro/CUDA tùy chọn và Qwen 1.7B không lọt vào dependency chính. | Đầu vào đã chốt trong yêu cầu. |
| MD02 | P0 | Đóng băng nguồn của candidate: branch/HEAD, danh sách và hash source thay đổi, pin, scripts và cấu hình phân phối an toàn. | `source-snapshot.json` gắn candidate/build; không chứa secret. Thay source quan trọng phải tạo snapshot/candidate mới. | MD01. |
| MD03 | P0 | Rà nguồn, license, notice, attribution của GGUF, ONNX, Python/wheels, OCR/native, MSVC và media. | Hồ sơ phân phối đủ cho các file thực có; giữ nguyên nguồn/license. Asset chưa được phép public ở lại staging. | MD01–MD02. |
| MD04 | P0 | Lập BOM và bảng tương thích model–runtime–desktop–worker–protocol–platform. | Pin Qwen/Piper/OCR khớp source; nêu rõ OCR theo app, Piper worker/lockfile theo bundle, metadata cũ khác source được chỉ rõ trước khi dùng. | MD02–MD03. |

## 6 Task gom bộ bàn giao

| Mã | Ưu tiên | Công việc | Đầu ra và tiêu chí hoàn tất | Phụ thuộc |
|---|---|---|---|---|
| MD05 | P0 | Copy input đã kiểm vào thư mục candidate mới; giữ nguyên GGUF và cặp ZIP/manifest Piper, đủ năm package OCR và hồ sơ nguồn. | Không hard-link candidate bất biến vào file đang dùng. Không lấy `bin/obj` làm pin; output build chỉ là sản phẩm cần đối chiếu. Không có cache, venv hay READY máy build. | MD02–MD04. |
| MD06 | P0 | Kiểm sau copy, tạo inventory/checksums và niêm candidate. | Mỗi file có path tương đối, bytes, hash, nguồn/license, vai trò và compatibility. Kiểm toàn entry Piper; phát hiện file thiếu/thừa/đổi. Inventory không tự hash chính nó; checksum inventory lưu ở hồ sơ ngoài hoặc metadata ký sau. | MD05. |

Đầu ra MD06 đủ để người dùng rà soát nội dung bộ file. Nó chưa được coi là bộ phát hành hoặc metadata có chữ ký.

## 7 Task đưa file lên staging server

| Mã | Ưu tiên | Công việc | Đầu ra và tiêu chí hoàn tất | Phụ thuộc |
|---|---|---|---|---|
| MD07 | P0 | Khảo sát server đích, dung lượng, filesystem, quota, đường chuyển và phân quyền staging/download. | Có biên bản đích, capacity và quyền. Staging ngoài vùng public; uploader có quyền riêng; web process không được sửa artifacts đã niêm. Không thay cấu hình live ngoài phạm vi được chỉ định. | MD06 và đích server cụ thể. |
| MD08 | P0 | Chuyển candidate vào staging; file đang chuyển có tên tạm, kiểm checksum tại server bằng phiên/kênh độc lập. | Inventory server trùng local từng file; upload dở không được phục vụ. Có số byte/hash hai đầu và biên bản thời gian/candidate. | MD07. |
| MD09 | P0 | Khảo sát kênh HTTPS thử nghiệm cho candidate, tải lại file và kiểm Range/ETag qua đúng proxy/storage. | Tải đầy đủ Qwen >2 GiB về workspace mới đúng hash; kiểm bytes đầu/cuối. Phân biệt host/storage có hỗ trợ với client chưa triển khai. Nếu cần code server mới để phục vụ thì MD18 hoàn tất trước phép thử HTTP đó; hash ở server vẫn làm được độc lập. | MD08; MD18 nếu môi trường chưa có kênh file thử nghiệm. |

Bộ file ở MD08 giữ trạng thái staging. Bước chuyển/công bố thật phải có môi trường được chỉ định; không tự dùng kênh `App_Releases` hoặc nâng giới hạn 2 GiB của release desktop để chứa GGUF.

## 8 Task nghiệm thu bộ đã tập hợp

Giai đoạn này thử component từ đúng candidate trước khi triển khai downloader mới. Có thể lấy bản đã kiểm ở server bằng kênh chuyển file được phép; kết quả chưa thay kiểm thử luồng tải tự động ở MD33.

| Mã | Ưu tiên | Công việc | Đầu ra và tiêu chí hoàn tất | Phụ thuộc |
|---|---|---|---|---|
| MD10 | P0 | Qwen: dùng GGUF candidate và worker của build tương thích, probe runtime/English/Chinese, cold start và kiểm ổn định. | Ghi file hash, worker/version/IPC/backend/native/config fingerprint, máy và từng kết quả. Chạy harness riêng với workspace mới; không dùng cache hoặc worker khác để thay bằng chứng. | MD04, MD06, MD08 hoặc bản copy đã xác minh tương đương. |
| MD11 | P0 | Piper: từ ZIP candidate tạo môi trường mới khi chặn tải mạng, kiểm installed files, tạo WAV và nghe fixture tiếng Việt. | Hash archive/manifest/worker/lock đúng; WAV hợp lệ, có tín hiệu, nội dung nghe đạt; cài/probe không tải dependency. Không copy venv hoặc marker cũ. | MD04, MD06, MD08 hoặc bản copy đã xác minh tương đương. |
| MD12 | P0 | OCR: nghiệm thu bộ app đồng bộ với candidate, fixtures en/zh, Paddle/MKL/OpenCV và bốn DLL MSVC x64. | Hai ngôn ngữ nhận dạng đạt; bốn DLL được nạp app-local, không lấy System32/máy dev làm nguồn. Kiểm Windows thiếu prerequisite trả lỗi rõ. | MD04, MD06 và build tương thích. |
| MD13 | P0 | Chạy ma trận Windows/profile sạch, user thường, đường dẫn có dấu/khoảng trắng, Intel/AMD và nhóm RAM được hỗ trợ. | Báo theo từng cấu hình; profile/cảnh báo tài nguyên được snapshot. Qwen test nặng chạy tách build/OCR/FFmpeg; không tự chấp nhận warning hoặc dừng IDE để lấy RAM. | MD10–MD12. |
| MD14 | P0 | Chốt qualification của candidate trước tích hợp tự tải. | Mỗi component có ArtifactVerified/RuntimeQualified riêng và giới hạn hỗ trợ. Lỗi Qwen native cũ chỉ được coi là giải quyết khi có bằng chứng trên build/máy liên quan. Skipped hoặc thiếu inference giữ chưa nghiệm thu. | MD09 khi đánh giá kênh HTTP, MD10–MD13. |

Qwen dùng [probe offline](scripts/Test-QwenOfflineBundle.ps1) và [stability harness](scripts/Test-QwenStability.ps1) theo runbook; cấu hình khởi đầu là 3 cold start và ít nhất 30 iteration để đánh giá tăng bộ nhớ sau warm-up. Các trường của harness như `CustomerReleaseQualified=false` không được sửa để biến một bài kiểm thành chứng nhận phát hành. Ma trận máy và nghe nghiệm thu vẫn là bằng chứng riêng.

## 9 Task triển khai cơ chế phân phối và tự tải

### 9.1 Metadata và server

| Mã | Ưu tiên | Công việc | Đầu ra và tiêu chí hoàn tất | Phụ thuộc |
|---|---|---|---|---|
| MD15 | P0 | Chốt threat model, cách ký/xác minh, thư viện và định dạng repository; thực hiện spike nhỏ trong môi trường test khi được triển khai. | ADR nêu gốc tin cậy, key roles/threshold, expiry, revoke, anti-replay và khả năng .NET; có vector ký/xác minh và kết quả review. Không gọi manifest có chữ ký đơn giản là TUF compliant. | MD04; có thể thiết kế trong khi MD10–MD14 đang chạy. |
| MD16 | P0 | Định nghĩa DTO public cho descriptor/catalog view và error; tách DTO server khỏi native bridge. | Sửa Contracts trước producer/consumer. Payload chỉ chứa metadata an toàn; parse bị giới hạn. Không nhận executable, URL hoặc absolute path từ DOM/project. | MD15. |
| MD17 | P0 | Triển khai công cụ ký và quy trình giữ/rotate/revoke khóa, bootstrap root trong client, diễn tập recovery. | Khóa riêng không nằm trên máy download/repo; root mới chỉ được tin qua chuỗi hợp lệ. Kiểm khóa lạ, chữ ký sai và việc phục hồi client cũ. Sinh khóa thật chỉ theo môi trường được chỉ định. | MD15–MD16. |
| MD18 | P0 | Thêm module server chỉ đọc metadata/artifacts của kho model, với cấu hình/giới hạn riêng và route theo component/version/hash đã publish. | GET/HEAD/Range stream file >2 GiB bằng kiểu `long`, strong ETag ổn định, MIME phù hợp/nosniff. Resolve đường dẫn chuẩn hóa trong artifact root; chặn traversal, absolute/encoded path và reparse/symlink. Không phục vụ staging/operation records, đọc cả model vào RAM, fetch URL tùy ý hoặc phụ thuộc workflow SQL desktop. | MD07, MD15–MD16. |
| MD19 | P0 | Tạo quy trình import/verify/promote metadata và artifact bất biến; ghi hồ sơ actor/time/hash/evidence. | Chỉ candidate được duyệt mới Publish; không ghi đè object Published. Promote metadata sau khi mọi file được phục vụ; lỗi publish không để catalog trỏ file thiếu. V1 vận hành bằng công cụ phát hành, chưa mở UI Admin/DB mới. | MD14, MD17–MD18. |

### 9.2 Desktop và quản lý file

| Mã | Ưu tiên | Công việc | Đầu ra và tiêu chí hoàn tất | Phụ thuộc |
|---|---|---|---|---|
| MD20 | P0 | Tích hợp verifier metadata/trusted root trong thư viện desktop dùng chung cho các adapter, sau qualification candidate. | Chặn chữ ký/key/schema sai, metadata hết hạn hoặc lùi phiên bản, mix metadata; kiểm cả app/worker/protocol và allowlist component trước tạo job tải. | MD14, MD15–MD17. |
| MD21 | P0 | Viết downloader streaming với giới hạn byte, host HTTPS/443 cụ thể, timeout và cancellation. | Chặn URL/redirect ngoài policy; đọc tới quá size dự kiến là dừng, không chờ hết response. Không forward token sang host khác, không lộ path/token trong log. Chỉ tải file đã được verifier chấp nhận. | MD18, MD20. |
| MD22 | P0 | Thêm tải tiếp với receipt chứa identity/hash/version/length/strong ETag của file tạm; retry có giới hạn. | Kiểm 200/206/416, offset/total và strong ETag đúng; weak/missing ETag thì tải mới, không resume. Không append hai representation. Kill app/mất mạng rồi mở lại tải tiếp đúng; cuối cùng hash toàn file. Partial lỗi không được promote. | MD21. |
| MD23 | P0 | Xác minh archive/file, giải nén vào staging mới và promote an toàn. | Giữ kiểm Piper hiện có; chặn traversal, absolute path, ADS, reserved names, collision, link/reparse và entry ngoài inventory. Giới hạn cả byte thực đọc, số file và tổng giải nén; fail/cancel không phá bản hoạt động. | MD20–MD22. |
| MD24 | P0 | Quản lý version component, receipt, active pointer, khóa sử dụng và cleanup. | Hai app không cùng cài đè; không thay/xóa runtime đang dùng. Dùng lại model hợp lệ, giữ bản trước để phục hồi; update thực hiện khi job kết thúc. Bằng chứng READY phải khớp runtime/fingerprint hiện tại. | MD23. |

### 9.3 Adapter và giao diện Setup

| Mã | Ưu tiên | Công việc | Đầu ra và tiêu chí hoàn tất | Phụ thuộc |
|---|---|---|---|---|
| MD25 | P0 | Qwen nhận nguồn component đã xác thực từ kho server hoặc bộ offline, giữ pin/probe/resource policy. | Không fallback upstream âm thầm khi signed catalog lỗi hoặc artifact bị revoke. Dùng lại GGUF đúng hash, chạy probe khi fingerprint đổi. CPU độc lập CUDA; worker vẫn cô lập, không có Cloud/credential. | MD10, MD20–MD24. |
| MD26 | P0 | Piper tìm payload đã xác thực trong component cache và bộ offline; phân biệt payload chưa tải với ứng dụng bị hỏng. | Thiếu payload online mở được luồng tải, không ép repair cả ứng dụng. ZIP/manifest vẫn khớp embedded approved definition và worker/lockfile. Tạo venv mới offline; đổi pin/payload cần bundle version mới. | MD11, MD20–MD24. |
| MD27 | P0 | Giữ OCR và media theo application package; nối trạng thái verify/repair với Setup mới. | Không tải DLL rời để thay OCR đang chạy; repair dùng đúng app build. Bốn MSVC app-local và FFmpeg gate tiếp tục được kiểm. OCR không biến thành dependency của kho Qwen/Piper. | MD12, MD16, MD24. |
| MD28 | P1 | Cập nhật TypeScript message, C# contract/handler, validation, busy/cancel và UI trạng thái tải/xác minh/cài/probe. | React chỉ nhận metadata an toàn; đổi account/context loại response cũ. Local cá nhân không đòi tổ chức. Startup modal và host gate đồng bộ; công tắc Piper giữ đúng nghĩa chọn thao tác, không bypass startup. | MD16, MD25–MD27. |

### 9.4 Đóng gói và tương thích

| Mã | Ưu tiên | Công việc | Đầu ra và tiêu chí hoàn tất | Phụ thuộc |
|---|---|---|---|---|
| MD29 | P0 | Tách mode publish online/offline trong scripts, cấu hình và package validation. | Chỉ online build có downloader đạt kiểm tra theo phạm vi mới được thiếu GGUF/Piper payload. Online vẫn có worker/approved definitions/OCR/media và tuân giới hạn release hiện có. Offline đủ bytes dùng kênh Direct riêng, giữ cap của pipeline Direct; không bỏ gate chỉ để publish qua. | MD25–MD28. |
| MD30 | P0 | Tương thích app cũ, model đã cài và package repair; kiểm đường bootstrap từ Setup đến đăng nhập và Setup component. | Client cũ còn package hợp lệ; client mới reuse file đúng hash. Component mới không làm app cũ nạp runtime không tương thích. Chuyển offline/online và repair không làm mất workspace/token/cấu hình. | MD24, MD29. |
| MD31 | P0 | Ký Setup/EXE của dự án, chốt license/provenance Release, source snapshot và artifact cuối. | Ký trước inventory/hash cuối, kiểm publisher/timestamp. Cập nhật compatibility/source snapshot cuối; nếu model/worker/native/config/lockfile hoặc fingerprint thay đổi thì chạy lại qualification liên quan. Dependency pin giữ nguyên; FFmpeg Release và quyền phân phối thực có mới được nhận vào public release. | MD03, MD17, MD29–MD30; quay lại MD10–MD14 khi bộ runtime thay đổi. |

### 9.5 Kiểm thử và rollout

| Mã | Ưu tiên | Công việc | Đầu ra và tiêu chí hoàn tất | Phụ thuộc |
|---|---|---|---|---|
| MD32 | P0 | Viết regression/security tests theo mục 11 trong khi phát triển, rồi chạy kiểm tra source chuẩn trên thay đổi cuối cùng. | Verifier/downloader/adapters/bridge và pipeline bảo vệ hành vi thực; Passed/Failed/Skipped tách riêng. Tests giả HTTP/clock/temp workspace không được gọi SQL/provider production. MD29 chỉ đi tiếp sau kiểm tra theo phạm vi; lượt full cuối chốt sau MD31. | MD16–MD31. |
| MD33 | P0 | Nghiệm thu tích hợp từ Setup cuối, đúng signed metadata và file tải từ server staging, trên máy sạch. | Tải Qwen >2 GiB, mạng đứt/resume, Piper offline install, OCR, startup gate, job Vietsub và xuất MP4 đạt; hash từ server khớp bộ đã nghiệm thu. Test model thật tách khỏi full build. | MD09, MD14, MD19–MD32. |
| MD34 | P1 | Bổ sung metrics, mã lỗi, giới hạn tải, retention và hướng hỗ trợ. | Biết tỷ lệ lỗi hash/signature/resume/probe, byte đã tải và dung lượng staging; log không chứa secret/path/transcript. Có hạn mức và cảnh báo theo môi trường, test cleanup không xóa file đang dùng. | MD18, MD22, MD24. |
| MD35 | P0 | Rollout nội bộ rồi staging rồi nhóm khách nhỏ; mở rộng theo kết quả. | Có môi trường/nhóm đích được chỉ định, manifest/build/hash và tiêu chí dừng. Model trước, desktop tương thích sau, kiểm end-to-end rồi mới mở kênh rộng. Không tự bật mặc định Qwen cho mọi môi trường. | MD31, MD33–MD34, MD36. |
| MD36 | P0 | Diễn tập rollback, revoke và recovery sau lỗi cài hoặc publish. | Đưa về artifact cũ được phép bằng metadata mới có phiên bản cao hơn; không hạ state tin cậy. Không dùng bản đã revoke. Bản đang dùng không bị xóa khi job chạy; có kiểm phục hồi máy offline và khóa bị mất. | MD17, MD19, MD24, MD30; trước MD35. |

## 10 Quy tắc xử lý bắt buộc khi triển khai

### 10.1 Tải tiếp và xác minh mạng

Receipt file tạm ràng buộc catalog/repository, component/version, hash đích, tổng byte, ETag và progress. Không dùng filename hoặc số byte đã tải làm bằng chứng identity. Chỉ dùng các host HTTPS được ứng dụng cho phép; metadata ký không tự mở rộng allowlist.

- Gửi Range/If-Range khi receipt còn khớp và có strong ETag; weak/missing ETag thì tải mới. Phản hồi 206 phải có Content-Range đúng offset và tổng kích thước.
- Nếu server trả 200 thay 206, bắt đầu lại an toàn, không append vào partial cũ.
- Với 416, kiểm file local có đủ và đúng hash; nếu không thì bắt đầu lại theo policy giới hạn.
- ETag/identity đổi, response bị encode khác hoặc tổng byte sai phải từ chối resume; dùng representation ổn định cho model.
- Sau resume vẫn hash toàn file trước promote. Hạn mức byte/disk áp dụng trong khi đọc để chặn luồng dữ liệu vô hạn.

Hành vi HTTP căn cứ [RFC 9110, Range requests](https://www.rfc-editor.org/rfc/rfc9110.html#section-14) và [If-Range](https://www.rfc-editor.org/rfc/rfc9110.html#section-13.1.5). Retry lỗi mạng không tự xác nhận resource warning, đổi model, hạ yêu cầu bảo mật hoặc chuyển nguồn tải.

### 10.2 Đĩa và cài đặt nguyên tử

Tính free disk theo các volume thực sự dùng: download tạm, artifact hoàn chỉnh, staging giải nén, venv cần tạo, bản cũ phục hồi và phần dự phòng. Với luồng online mới, giá trị đĩa Piper 727.344.098 byte hiện tại chỉ là yêu cầu của quá trình cài offline; không tự coi nó đã bao gồm mọi bản ZIP/cache/backup của thiết kế mới. Qwen cũng phải tính thêm bản tải và copy nếu không thể reuse cùng file.

Promote trong cùng volume sau verify; nếu cần chuyển volume thì copy vào staging đích và kiểm lại trước đổi pointer. Phải xử lý crash ở từng bước: tải dở, giải nén dở, tạo venv dở, trước/sau đổi active pointer và trước/sau ghi READY. Runtime lock được giữ cho cả cài và sử dụng; cleanup chỉ đụng thư mục do component manager sở hữu, kiểm absolute path trước xóa.

### 10.3 Quyền và readiness

- Các adapter tiếp tục kiểm session/license/device; org-scoped giữ membership/role/context, Local cá nhân dùng context cá nhân.
- `DISABLED` theo cấu hình không tạo job tải hoặc vượt quyền gói. Tải xong không tự thay feature flag.
- Không tạo `READY` từ UI, catalog, sự tồn tại của model hoặc output probe máy khác.
- Qwen cần đúng worker/runtime/native/config/backend và probe Runtime/English/Chinese. Cảnh báo RAM cần xác nhận đúng lượt/profile; platform, checksum, disk, OOM thật và probe lỗi vẫn chặn.
- Piper cần approved archive/manifest/worker/lock, môi trường Python vừa được xác minh và WAV mẫu; không fallback sang engine khác khi lỗi.
- Metadata hết hạn hoặc mạng lỗi chặn lấy/cài phiên bản mới qua catalog online đó; không tự xóa bộ đã cài hợp lệ. Bộ offline đúng pin nhúng của ứng dụng áp dụng policy mục 3.1, vẫn cần verify/probe và kiểm revoke đã biết. Việc chạy bộ đã cài vẫn theo license và chính sách local; không hứa có thể thu hồi tức thời trên máy hoàn toàn offline.

### 10.4 Phục hồi và phát hành bất biến

Candidate hoặc URL đã publish không được thay byte. Sửa artifact phải có identity/version/hash mới. Khi rollback vận hành, metadata mới được ký có phiên bản tăng và chỉ trỏ tới bản cũ còn được cho phép; không tắt cơ chế chống metadata replay.

Không xóa payload/version cũ chỉ vì catalog ngừng phát hành mới. Retention phải xét client còn được hỗ trợ, cửa sổ rollback và file đang sử dụng. Khóa ký bị lộ, license phân phối bị thu hồi hoặc hash sai cần quy trình revoke riêng; dừng download mới không đồng nghĩa file offline của khách tự biến mất.

## 11 Ma trận kiểm thử và bằng chứng cần lưu

| Nhóm | Ca bắt buộc | Bằng chứng đạt |
|---|---|---|
| Inventory | Thiếu/thừa file, sai size/hash, nguồn thay đổi giữa copy, candidate bị sửa sau niêm. | Mã lỗi chính xác; candidate sai không được publish. |
| Metadata | Chữ ký sai, key lạ, root chain sai, hết hạn, sequence lùi, mix metadata, schema/size vượt hạn. | Client không tạo request tải artifact chưa được xác thực; trust state không bị cập nhật từ dữ liệu lỗi. |
| Compatibility | Sai Windows/platform, app build, worker IPC, native version, dependency hoặc bundle Piper. | Chặn trước nạp/chạy; thông báo an toàn để dùng đúng app/component. |
| Server | File Qwen thật >2 GiB, HEAD/GET/Range, concurrent reads, upload dở, object thay đổi, catalog thiếu target; traversal/absolute/encoded path, reparse/symlink, đoán đường staging/operation records. | Hash tải về đúng, bộ nhớ có giới hạn, đường đích chỉ trong artifact root đã publish; file staging và hồ sơ nội bộ không public. |
| Resume | Mất mạng, kill app, 200/206/416, Content-Range thiếu/sai offset hoặc tổng, ETag đổi/weak/missing, gzip/br, prefix bị sửa. | Tải tiếp hoặc restart đúng; không ghép version; toàn file phải vượt hash cuối. |
| Network | HTTP, port/host ngoài policy, redirect ngoài allowlist, URL userinfo/IP hoặc payload URL do DOM gửi. | Bị từ chối; không rò JWT/token/credential sang host khác. Server không fetch URL client. |
| Archive | Traversal, ADS, symlink/reparse, collision tên Windows, khai size giả, zip bomb, file ngoài manifest. | Dừng trước thực thi; byte thực và mọi đường đích nằm trong hạn. |
| Storage | Hết đĩa trước/trong tải/cài, thiếu quyền, file lock, hai app cài cùng lúc, khác volume. | Không phá bản hoạt động; retry có thể phục hồi trạng thái được xác minh. |
| Qwen | Model/native sai hash, worker crash/timeout/cancel, CPU unsupported, resource warning, cold start, soak. | Probe Runtime/English/Chinese theo đúng build; failure giữ mã/diagnostic an toàn, không READY giả. |
| Piper | Thiếu ZIP, sai manifest/worker/lock, cài khi chặn HTTP, WAV lỗi, app di chuyển, venv không khớp. | Tạo môi trường tại đích và WAV hợp lệ; payload lỗi không đi vào engine. |
| OCR | Ảnh Anh/Trung, thiếu/hỏng từng native DLL và MSVC, DLL bị khóa, app build khác. | Nhận dạng đạt bằng bộ tương thích; bốn MSVC app-local; repair đúng nguyên bộ app. |
| Setup và quyền | Local cá nhân, org role hợp lệ/Viewer, license hết hạn/revoke, đổi tài khoản/context trong lúc tải. | Modal và host gate đồng bộ; response cũ không cấp quyền. File hợp lệ đã tải không bị xóa tùy tiện. |
| Phục hồi | Crash ở các điểm promote, rollback pointer, metadata mới trỏ bản cũ, target bị revoke. | Không dùng file nửa chừng/revoked; bản cũ chỉ được dùng khi còn được phép và tương thích. |
| End to end | Setup cuối → login/license → tải → probe → OCR → dịch → tạo giọng → preview/xuất MP4 → mở lại. | Dùng đúng server/artifact/build; có hash, môi trường và kết quả nghe/xuất thực tế. |

Tests mạng dùng fake HTTP/clock/stream trong vòng phát triển. Kiểm full file >2 GiB và ma trận Windows sạch là phép nghiệm thu riêng, không thay bằng fake stream. Model test bị Skipped không được tính là model đã đạt.

Sau khi có thay đổi source/script, chạy các lệnh theo repository:

```powershell
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build

Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
npm test
```

Lưu từng lượt Passed/Failed/Skipped, source snapshot, candidate ID, artifact hash, máy/OS/hardware và log an toàn. Source có sẵn, test tự động, smoke môi trường và rollout production là bốn mức khác nhau. Chỉ cập nhật biên bản bằng lệnh thực sự đã chạy.

## 12 Thứ tự phụ thuộc và cách chia đợt

```text
MD01 → MD02 → MD03 → MD04 → MD05 → MD06
                                      ↓
                         MD07 → MD08 → MD09
                                   ↓
                         MD10 / MD11 / MD12
                                   ↓
                             MD13 → MD14

MD04 → MD15 → MD16 → MD17
MD07 + MD15 + MD16 → MD18
MD14 + MD17 + MD18 → MD19
MD14 + MD17 → MD20 → MD21 → MD22 → MD23 → MD24
MD18 + MD20 → MD21
MD10/11/12 + MD24 → MD25 / MD26 / MD27 → MD28
MD28 → MD29 → MD30 → MD31 → MD32
MD19 + MD31 + MD32 → MD33
MD18/22/24 → MD34
MD17/19/24/30 → MD36
MD31 + MD33 + MD34 + MD36 → MD35
```

MD09 chỉ đợi MD18 ở phần thử HTTP nếu môi trường không có kênh file staging sẵn; qualification component có thể bắt đầu từ bản copy đã được kiểm tại server. Không tạo vòng phụ thuộc giữa qualification và code server. Thiết kế MD15–MD17 có thể làm song song với MD10–MD14; không công bố artifact trước đủ bằng chứng.

| Đợt | Phạm vi | Điểm dừng có thể rà soát |
|---|---|---|
| 1 | MD01–MD06 | Bộ bàn giao local đã niêm, inventory/checksums/provenance/compatibility đầy đủ. |
| 2 | MD07–MD09 | Bộ ở staging server đúng hash; kênh tải thử được ghi rõ đạt/chưa đạt. |
| 3 | MD10–MD14 | Báo cáo runtime của đúng candidate, ma trận máy và điều kiện còn thiếu. |
| 4 | MD15–MD24 | Metadata/trust/server/downloader/component manager đạt kiểm thử; chưa mở khách rộng. |
| 5 | MD25–MD32 | Adapter/Setup/pipeline online và offline tương thích, artifacts cuối có chữ ký/hash. |
| 6 | MD33–MD36 | End-to-end staging, diễn tập phục hồi và rollout đúng môi trường được chỉ định. |

Không ước lượng ngày hoàn tất trước khi chốt server, ma trận máy, quyền phân phối và giải pháp thư viện ký. Khối lượng code/kiểm thử sẽ được ước lượng sau MD04 và MD15; copy file vài GB không đại diện thời gian nghiệm thu.

## 13 Điều kiện dừng và rollback

Dừng candidate hoặc rollout nếu có một trong các tình huống: hash/signature không khớp; root/metadata không tin cậy; sai compatibility; Qwen/Piper/OCR không vượt probe trên nhóm máy được công bố hỗ trợ; app báo READY sai; cài mới làm hỏng bộ đang hoạt động; hoặc thiếu quyền phân phối file thực tế.

Khi dừng, giữ file/log để đối chiếu, ngừng phát metadata mới lỗi, phục hồi metadata hợp lệ bằng một đợt ký mới và dùng bộ tương thích đã nghiệm thu. Không sửa checksum để nhận file lỗi, không ghi đè URL Published, không tự hạ yêu cầu RAM/probe hoặc thay DLL hệ thống để tiếp tục.

Trước public rollout phải diễn tập: server staging hết dung lượng, transfer bị cắt, catalog publish nửa chừng, mất khóa online, client giữ metadata cũ, app crash khi đổi version và rollback khi job đang giữ runtime. Rollback không được sửa workspace, subtitle, license hoặc ledger nghiệp vụ.

## 14 Hồ sơ bàn giao và điều kiện hoàn tất

- [ ] Bộ chính và tùy chọn tách rõ; không chứa Qwen 1.7B/CUDA ngoài phạm vi.
- [ ] Source snapshot, BOM, license/attribution và compatibility matrix đi cùng candidate.
- [ ] Hash sau copy local, tại server và sau tải HTTPS trùng nhau.
- [ ] Qwen/Piper/OCR có báo cáo runtime riêng trên đúng bộ và môi trường.
- [ ] Kênh model riêng xử lý >2 GiB; không phá giới hạn release desktop hiện hành.
- [ ] Metadata/verifier/trust root/rotation/revoke và chữ ký Setup được kiểm.
- [ ] Download resume, disk quota, staging, atomic promote, runtime lock và rollback đạt.
- [ ] Setup React/host đồng bộ; Local cá nhân không bị đòi tổ chức; READY gắn probe máy đích.
- [ ] Online/offline publish và client cũ/repair có kiểm chứng tương thích.
- [ ] Build/test, model thật, Windows sạch và end-to-end có báo cáo tách biệt; mọi Failed/Skipped được giải thích.
- [ ] Kho có retention, metrics, quy trình hỗ trợ và phục hồi; không lộ secret/dữ liệu người dùng.
- [ ] Rollout chỉ được ghi hoàn tất cho môi trường/nhóm khách đã thực sự kiểm.

Lượt lập kế hoạch chỉ kiểm nội dung tài liệu, đường dẫn tham chiếu và tính nhất quán với source đã đọc. Chưa chạy build/test/inference, chưa tạo bộ bàn giao hoặc triển khai server; không có số Passed mới của tính năng phân phối model trong tài liệu này.
