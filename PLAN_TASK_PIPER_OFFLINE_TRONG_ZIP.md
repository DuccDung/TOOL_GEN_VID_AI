# Kế hoạch triển khai Piper offline trong bản ZIP

Ngày lập: 2026-09-26. Rà soát nhánh `main`, commit nền `bc1c6aec3c731bdb0edcebbde17ae13640245e52` cùng working tree đang có các thay đổi Setup ZIP và ổn định kiểm thử.

**Đã triển khai source/payload và kiểm thử cục bộ theo yêu cầu tiếp theo của người dùng ngày 2026-09-26.** Nội dung dưới đây giữ thiết kế và tiêu chí ban đầu để đối chiếu. Bằng chứng được ghi tại [báo cáo triển khai](TRIEN_KHAI_PIPER_OFFLINE_TRONG_ZIP.md). Máy Windows sạch/thứ hai, tài khoản thử và rollout chưa được xác minh. Giữ nguyên cấu hình `TOOL-LOCAL/appsettings.json` của người dùng.

## 1. Kết quả cần đạt

Người dùng nhận một ZIP có ứng dụng và bộ Piper đầy đủ. Sau khi giải nén và qua các bước đăng nhập/license/organization hiện hành, ứng dụng tự chuẩn bị Piper từ file đi kèm, tạo thử WAV tiếng Việt và mở gate khi mọi thành phần bắt buộc đã sẵn sàng.

- Máy khách không phải cài Python, uv hoặc tải thêm thành phần Piper qua Internet.
- Chuẩn bị lần đầu và sửa runtime Piper dùng gói local đã xác minh. Những lần sau dùng lại bộ hợp lệ trên máy đó.
- Đóng gói Piper cùng model `vi_VN-vais1000-medium`. Model Qwen/Kokoro và runtime chuyển giọng Veo nằm ngoài phạm vi bổ sung này.
- Tính offline áp dụng cho chuẩn bị/chạy Piper. Đăng nhập, license, SQL workflow và Cloud vẫn theo kiến trúc hiện hành.
- Dung lượng ZIP, dung lượng khi cài và thời gian lần mở đầu phải được đo trên artifact thật trước bàn giao.

## 2. Căn cứ khi lập kế hoạch, trước triển khai

| Thành phần | Hiện trạng tại thời điểm lập kế hoạch |
|---|---|
| `VietsubVoiceComponentStore.InstallAsync` | Tải uv từ GitHub, tạo Python bằng uv, cài wheel từ PyPI, tải model/config từ Hugging Face, probe rồi ghi marker. Chưa có nguồn cài Piper offline từ ZIP. |
| Phiên bản | Runtime hiện tại `piper-1.6.0-python-3.11.15-locked-v2`; uv `0.12.3`; Piper `1.6.0`; Python `3.11.15` x64. Đây là pin hiện có, chưa phải xác nhận đủ artifact để tạo bundle offline. |
| Dependency | `piper-requirements.lock` có hash của các wheel; metadata Python hiện do bản uv đã pin cung cấp. Bundle offline cần ghi rõ archive Python và hash dùng thực tế. |
| Đường dẫn | `.venv` được tạo tại thư mục cuối. Composition dùng component theo tài khoản Windows và vẫn có nhánh tương thích workspace legacy. |
| READY | Ràng buộc máy/tài khoản, runtime, worker, requirements, Python, model/config. `VerifyAsync` tạo WAV thật; marker từ máy build không thay thế bước này. |
| Startup | Modal React tự gửi `check`; cài là operation riêng qua coordinator có kiểm quyền, context và khóa runtime. Getter trạng thái không cài đặt. |
| Kokoro | `VietsubKokoroRuntime` gọi `RequireUvInstaller()` từ store Piper. Thay bố cục Piper phải giữ đường dùng uv đã xác minh này. |
| Đóng gói | Project copy worker/lockfile và hồ sơ voice; script publish chưa yêu cầu payload Piper đầy đủ. ZIP sau giải nén đang được probe OCR/media/WebView2. |
| CLI chẩn đoán | `--check-bundled-components` hiện chỉ kiểm OCR/media/WebView2. Các lệnh `--check-*` hiện không tự cài model. |
| Lỗi đã quan sát | Lần cài Piper thật trước đây dừng khi .NET HTTP không phân giải được `github.com:443`, ngay lúc tải uv. Nguyên nhân DNS sâu hơn và các bước cài tiếp theo chưa được xác minh. |

Bằng chứng trước đợt này nằm trong [báo cáo ổn định kiểm thử](TRIEN_KHAI_ON_DINH_KIEM_THU.md) và [báo cáo Setup ZIP](TRIEN_KHAI_SUA_LOI_SETUP_BAN_ZIP.md). Các kết quả đó không chứng minh bundle Piper offline đã tồn tại hoặc đã đạt.

## 3. Thiết kế đề xuất

### 3.1. Một payload Piper có phiên bản đi cùng ứng dụng

Bố cục dự kiến, sẽ chốt sau task kiểm chứng runtime:

```text
taphoatool/
  TOOL-LOCAL.exe
  workers/
    piper_worker.py
    piper-requirements.lock
  components/piper/<bundle-version>/
    manifest.json
    piper-offline.zip
  third_party/voice/
    ... hồ sơ và license của các thành phần thực đóng gói
```

Payload chứa bộ CPython x64 đầy đủ, uv đã pin, toàn bộ wheel phù hợp Windows/CPython đã khóa, model ONNX/config và dependency native cần thiết. Worker/lockfile đi với ứng dụng phải khớp manifest của payload.

- Chuẩn bị artifact bằng bước riêng có mạng; release chỉ nhận bộ đã kiểm. Nếu thiếu artifact đã pin, ghi rõ lỗi và xử lý nguồn cung trước khi đóng gói; không tự đổi phiên bản để lấy kết quả đạt.
- Tách metadata nguồn khỏi artifact nặng: source giữ định nghĩa, hash, lockfile và script; cache/output có kiểm soát giữ các gói tải và ZIP tạo ra.
- Manifest có schema, component/bundle/runtime version, nền tảng, protocol, file, size, SHA-256, kích thước giải nén và nguồn gốc dependency. Không đưa đường dẫn máy build vào manifest.
- Neo identity/hash kỳ vọng vào metadata đã duyệt của bản ứng dụng, chẳng hạn resource được nhúng lúc build. Manifest đặt cạnh ZIP không tự làm nguồn tin cậy cho chính ZIP đó.
- Dùng một archive payload giúp tránh bộ lọc publish hiện tại làm mất file bên trong dependency, chẳng hạn file `.xml`. Kiểm cấu trúc và nội dung của archive trong bước đóng gói.
- Bổ sung đủ license/attribution và hồ sơ phân phối của Python, uv, Piper, thư viện phụ thuộc và model theo artifact thực tế. Hồ sơ đang có chưa tự chứng minh gói phân phối mới đã được duyệt.

### 3.2. Tạo môi trường riêng trên máy khách

Ưu tiên component theo tài khoản tại `SystemSetupPaths.ComponentsRoot`, có thư mục runtime theo phiên bản. Ứng dụng có thể nằm trong thư mục chỉ đọc; file cài, cache và WAV thử nằm ở vùng ghi được của tài khoản.

Luồng dự kiến:

1. Xác thực request Setup, feature/role/context và giành khóa runtime hiện hành.
2. Kiểm nền tảng, dung lượng đĩa và payload: identity, hash, file thiếu/thừa, đường dẫn giải nén.
3. Giải nén thành phần vào staging; xác minh trước khi chạy uv/Python. Đưa base Python và file bất biến vào vị trí cuối theo phiên bản.
4. Tạo `.venv` ngay tại vị trí cuối bằng Python đi kèm, chỉ cài từ wheel local đã kiểm hash.
5. Chạy worker hiện hành, kiểm protocol và WAV tiếng Việt; kiểm fingerprint của runtime/thư viện, worker, model và máy đích.
6. Ghi marker bằng thao tác thay file atomically, cập nhật trạng thái Setup. Gate chỉ mở khi tất cả component bắt buộc READY.

Môi trường `venv` có thể chứa đường dẫn tuyệt đối và cần được tạo tại vị trí đích. Vì vậy, chỉ promote các payload có thể di chuyển; không tạo `.venv` trong staging rồi đổi tên thư mục cha. [Tài liệu Python](https://docs.python.org/3.11/library/venv.html#how-venvs-work).

Hướng cài offline: chọn rõ executable Python local, dùng wheelhouse local, kiểm hash và tắt mạng/tải Python trong tiến trình uv. `--offline` vô hiệu hóa mạng; `--no-index` bỏ registry nhưng vẫn cần kiểm nguồn file local. Các cờ và cách gọi chính xác phải được kiểm trên uv `0.12.3` đã pin ở task PO02. [Tài liệu uv](https://docs.astral.sh/uv/reference/cli/).

Dung lượng yêu cầu tính từ payload, staging, môi trường cuối và khả năng giữ runtime cũ; không dùng riêng kích thước model hoặc mặc định 768 MB làm bằng chứng đủ dung lượng.

### 3.3. Trải nghiệm mở ứng dụng

- Sau khi context hợp lệ và lượt kiểm ban đầu kết thúc, startup gửi operation chuẩn bị **Piper** một lần nếu component còn thiếu và payload được xác minh là khả dụng.
- Giữ riêng thao tác đọc trạng thái với thao tác cài. Request tự động vẫn đi qua cùng authorizer/coordinator/khóa runtime như thao tác từ nút UI.
- Hiển thị tiến độ dễ hiểu: chuẩn bị giọng Việt, cài thành phần đi kèm, kiểm tra giọng. UI không mô tả đây là tải Internet nếu đang dùng file local.
- Giữ hủy, thoát và thử lại. Lỗi/hủy không tự lặp vô hạn; đổi organization/session hoặc callback cũ không khởi động cài lại ngoài ý muốn.
- Runtime đã hợp lệ được dùng lại. Thiếu/hỏng runtime có thể sửa từ payload local; payload thiếu/hỏng phải báo rõ cần bộ ứng dụng đầy đủ đúng phiên bản.
- Luồng ZIP đầy đủ không tự chuyển sang tải GitHub/PyPI/Hugging Face khi gặp lỗi. Đường cài online cũ, nếu còn giữ cho công cụ bảo trì, phải được chọn rõ và có kiểm thử riêng.
- Giữ kiểm quyền, modal khóa nền và gate C#. Feature bị tắt hoặc vai trò không được quản lý Setup không được tự chuẩn bị Piper.

### 3.4. Cập nhật, hủy và tương thích

- Runtime mới dùng phiên bản riêng khi bố cục hoặc contract thay đổi. Giữ runtime đang dùng và dữ liệu project; không cài đè khi có job giữ lease.
- Khi hủy/crash: dừng đúng cây tiến trình của operation, giữ trạng thái chưa READY và dọn phần cài dở thuộc operation sau khi xác minh đường dẫn. Lượt sau có thể chuẩn bị lại từ payload.
- Tránh ghi đè model đang dùng. Nếu model không đổi, có thể dùng lại sau kiểm hash; nếu đổi, cần lưu theo phiên bản và xét snapshot project.
- Tách marker và bằng chứng theo runtime/version để cài phiên bản mới không vô hiệu hóa runtime cũ cần cho rollback.
- Kiểm nhánh legacy của store, hai bản ứng dụng cùng tài khoản và việc di chuyển thư mục ZIP. Runtime ở vùng component ổn định phải dùng được sau khi thư mục ứng dụng đổi vị trí; worker/lockfile mới vẫn phải được kiểm.
- Duy trì `RequireUvInstaller()` cho Kokoro. Đợt này không cài tự động Kokoro hoặc thay voice đã chọn của project.

## 4. Danh sách task triển khai

Tất cả task dưới đây ở trạng thái **chưa triển khai**. P0 là nền tảng cần xong trước khi tích hợp; P1 vẫn bắt buộc trong nghiệm thu/bàn giao.

| ID | Ưu tiên | Task | Phụ thuộc | Đầu ra và điều kiện hoàn tất |
|---|---|---|---|---|
| PO01 | P0 | Chốt baseline và contract bundle | — | Ghi source/commit/working tree, call chain, feature/role, đường component legacy, pin và nguồn tải. Chốt scope chỉ Piper, metadata tin cậy, package/runtime version và tiêu chí offline. Ghi lỗi DNS hiện có như đầu vào chưa giải quyết. |
| PO02 | P0 | Kiểm chứng Python/Piper độc lập máy build | PO01 | Lấy artifact chính thức có kiểm hash trên môi trường mạng hoạt động; tạo Python riêng và cài đúng lockfile. Thử ở đường dẫn đích khác với cache rỗng, chỉ file local, không dựa PATH/registry. Tạo WAV Việt thật. Xác nhận toàn bộ DLL/wheel và tham số uv trước khi chốt bố cục. |
| PO03 | P0 | Tạo quy trình đóng gói payload | PO02 | Script chuẩn bị bundle tái tạo từ đầu vào đã pin, lưu inventory/hash/size/nguồn và hồ sơ license. Output mới không ghi đè gói cũ. Từ chối thiếu wheel/Python/model, sai hash hoặc dependency không đúng nền tảng; không đóng `.venv`, marker READY, cache/tài khoản máy build. |
| PO04 | P0 | Đọc và xác minh bundle local | PO01, PO03 | Reader kiểm identity/hash bằng metadata tin cậy của app trước chạy executable. Giải nén có giới hạn kích thước/số file, từ chối traversal, path tuyệt đối, ADS, reparse/link, tên trùng sau chuẩn hóa và file ngoài manifest. Sai version/schema/platform có mã lỗi riêng. |
| PO05 | P0 | Cài Piper từ payload offline | PO02, PO04 | Store dùng base Python và wheel local; `.venv` tạo ở đường dẫn cuối. Tắt tải Python/registry/network trong tiến trình con, lọc UV/PIP/PYTHON kế thừa, dùng cache riêng và kiểm hash. Báo tiến độ, timeout/cancel; probe đạt mới ghi READY. Có seam để fixture dùng bundle/root riêng. |
| PO06 | P0 | Phục hồi và tương thích runtime | PO05 | Khóa giữa tiến trình và với job chạy đúng; bấm lặp không cài trùng. Hủy/crash/thử lại không để READY sai hoặc tiến trình mồ côi. Kiểm upgrade/rollback, marker máy khác, legacy root, thư mục ZIP đổi vị trí và uv cho Kokoro; bảo toàn project/voice snapshot. |
| PO07 | P1 | Tích hợp tự chuẩn bị trong Startup Setup | PO04–PO06 | Host trả khả năng chuẩn bị từ bundle đã kiểm; UI gửi một operation Piper trong context hợp lệ sau check. Cập nhật contract C#/TypeScript cùng nhau nếu thêm metadata. Có progress/cancel/retry; stale response, remount, đổi organization, Viewer/feature tắt và lỗi không gây tự chạy lặp. Gate hai lớp giữ đúng. |
| PO08 | P1 | Tích hợp build, publish và updater | PO03–PO06 | Release đầy đủ có payload/manifest/notices trong project và managedFiles. Publish từ chối thiếu/hỏng/mismatch Piper; dev build và unit test không tự tải model. Kiểm đúng ZIP sau giải nén; updater/rollback mang đúng payload và không xóa runtime/user data ngoài phạm vi. |
| PO09 | P1 | Thêm kiểm tra gói Piper thật có kiểm soát | PO05, PO08 | Có script hoặc CLI tường minh chuẩn bị/probe trong workspace chỉ định, báo JSON an toàn và exit code. Giữ `--check-*` hiện hành không tự cài. Probe nghiệm thu luôn dùng root/cache mới, không đọc bộ Piper đã cài trên máy dev, không cần auth/SQL/provider và không mở quyền qua WebView. |
| PO10 | P1 | Kiểm thử logic, bridge và frontend | PO04–PO09 | Đủ ca bundle xấu, thiếu tài nguyên, hủy/retry/concurrency, role/context, probe lỗi và gate. Kiểm HTTP giả không bị gọi trong nhánh offline; tham số và môi trường của uv không cho tải ngầm. Chạy lệnh chuẩn sau thay đổi; báo riêng Passed/Failed/Skipped. |
| PO11 | P1 | Nghiệm thu runtime thật và ZIP | PO08–PO10 | Đạt ma trận mục 5 trên artifact/hash cụ thể: cài mới offline, mở lại, Unicode, ổ đĩa khác, Windows sạch, WAV/playback/export và luồng startup được phép. Thu log mạng của cây tiến trình, thời gian/RAM/disk; mọi lỗi được giữ và điều tra. |
| PO12 | P1 | Bàn giao và chốt phạm vi phát hành | PO11 | Cập nhật runbook, dependency/provenance và báo cáo triển khai theo thực tế; ghi dung lượng ZIP, lệnh chuẩn bị/verify, trường hợp lỗi, rollback và môi trường chưa xác minh. Chỉ kết luận Piper offline đã nghiệm thu khi có bằng chứng; phát hành production là bước riêng theo môi trường được cho phép. |

Thứ tự chính: **PO01 → PO02 → PO03 → PO04 → PO05 → PO06 → PO07/PO08/PO09 → PO10 → PO11 → PO12**. Ca kiểm có thể được viết cùng task tương ứng; model thật chạy riêng sau build và các bài native nặng.

## 5. Ma trận nghiệm thu đề xuất

Các số lượt là tiêu chí đặt ra trước triển khai; chưa có kết quả mới cho kế hoạch này.

| Nhóm | Ca bắt buộc | Điều kiện đạt |
|---|---|---|
| Payload | Đủ file, sai version/platform, thiếu Python/wheel/model/DLL, hash sai dù giữ nguyên size, manifest và ZIP cùng bị sửa | Từ chối trước khi chạy executable chưa được tin cậy; báo đúng component/lỗi. |
| Giải nén | `..`, path tuyệt đối, ADS, reparse/link, tên trùng không phân biệt hoa/thường, file thừa, vượt size/file-count | Không ghi ra ngoài root được phép; không tạo READY. |
| Cài offline thật | Ba lần cài mới vào ba root/cache trống trên Windows x64 sạch, mạng của cây uv/Python bị chặn; không có Python/uv trong PATH | Cả ba lần cài và probe WAV đạt. Có log chứng minh không tìm/tải dependency ngoài gói; không dùng cache lần trước làm bằng chứng cài mới. |
| Môi trường thứ hai | Ít nhất một lần cài mới offline và ba lần mở lại trên Windows/tài khoản kiểm thử khác, phù hợp phạm vi hỗ trợ đã chốt | Cài được bằng quyền user thường; không phụ thuộc profile hoặc công cụ máy build. Nếu thiếu môi trường, ghi chưa nghiệm thu. |
| Đường dẫn | ZIP ở đường dẫn tiếng Việt/khoảng trắng; component/profile có dấu; ZIP trên ổ khác; thư mục ứng dụng chỉ đọc; đổi vị trí ZIP sau lần cài | Runtime vẫn hoạt động trong vùng component được phép, không cần sửa PATH/registry hoặc lấy quyền admin. |
| Tài nguyên | Thiếu disk theo nhu cầu thực, không có quyền ghi, file bị khóa, platform sai | Dừng có kiểm soát, không phá runtime/project cũ, retry được khi nguyên nhân được xử lý. |
| Lifecycle | Hủy khi giải nén/cài/probe, kill tiến trình sở hữu giữa chừng, mở hai app, job đang dùng runtime, bấm lặp | Giữ gate đúng, trả khóa sau cleanup, không còn tiến trình con, không có marker READY của lượt dở. |
| Mở lại và nâng cấp | Năm lần mở lại trên runtime đã cài; upgrade/rollback và marker từ máy khác; sửa/hỏng wheel đã cài, Python hoặc DLL | Bộ hợp lệ được dùng lại; bộ bị thay đổi phải bị phát hiện và kiểm/sửa từ payload trước READY. Rollback không làm mất dữ liệu người dùng. |
| UI và quyền | Tự chuẩn bị đúng một lần, cancel/retry, remount, phản hồi cũ, đổi organization/logout, Viewer, feature tắt; OCR còn lỗi | Không cài ngoài context/quyền; UI và host cùng khóa. Piper đạt không tự mở gate khi component bắt buộc khác còn lỗi. |
| Giọng thực tế | WAV probe, câu ngắn/dài có dấu/số/dấu câu, sinh giọng từ fixture Vietsub, playback và export | Protocol/PCM/hash/audibility/duration đúng; nghe mẫu để nghiệm thu tiếng Việt. Fixture giả không thay thế bước nghe/chạy model thật. |
| ZIP và hồi quy | ZIP đúng version/build/hash, updater/rollback, OCR/media/WebView2, uv cho Kokoro, bộ C#/frontend hiện hành | Mọi ca bắt buộc đạt; model opt-in Skipped vẫn ghi chưa kiểm. Không đổi voice/project hoặc phá đường gọi module khác. |

Để kiểm offline, tách hai phạm vi: CLI/harness Piper chạy hoàn toàn không mạng; UI ứng dụng kiểm với auth/license/organization ở môi trường thử hợp lệ, chặn riêng nguồn tải Piper. Không vô hiệu hóa auth/license để làm cho toàn ứng dụng chạy offline.

Trên source/binary cuối: chạy lại toàn bộ C#/frontend theo lệnh chuẩn, thêm ba lượt liên tiếp mỗi suite; các ca auto-start/cancel/context quan trọng chạy lặp 20 lượt với fixture mới. Khi lỗi, giữ lượt lỗi, sửa nguyên nhân và chạy lại chuỗi chịu ảnh hưởng. Test model thật chạy riêng, không song song build/OCR/FFmpeg nặng.

## 6. Phạm vi file dự kiến

| Nhóm | File/module cần đối chiếu |
|---|---|
| Runtime | `TOOL-LOCAL/Vietsub/Voice/VietsubVoiceComponentStore.cs`, `VietsubPiperVoiceSynthesizer.cs`, `VietsubKokoroRuntime.cs`, `Workers/piper_worker.py`. Tách reader/installer bundle nếu giúp kiểm soát trách nhiệm. |
| Dependency/metadata | `TOOL-LOCAL/SystemSetup/piper-requirements.in`, `.lock`, `DEPENDENCIES.md`, `third_party/voice/*`; định nghĩa bundle được duyệt mới. |
| Startup và bridge | `SystemSetupAdapters.cs`, `SystemSetupCoordinator.cs`, `SystemSetupContracts.cs`, `SystemSetupBridge.cs`, `SystemSetupPaths.cs`, `RuntimeUseGate.cs`, `DesktopComponentComposition.cs`, composition trong `Program`/`Form1`. |
| Frontend | `Web/src/features/systemSetup/StartupSystemSetupModal.tsx`, `SystemSetupPanel.tsx`, `useSystemSetup.ts`, `types.ts` cùng `Web/src/types.ts` và test liên quan. |
| Publish/diagnostics | `TOOL-LOCAL.csproj`, `DesktopReadinessCommand.cs`, `scripts/Publish-DesktopRelease.ps1`, `Test-DesktopSetupPublish.ps1`, `Test-DesktopBundleRuntime.ps1`; dự kiến thêm `Prepare-PiperOfflineBundle.ps1` và `Test-PiperOfflineBundle.ps1`. |
| Test/runner | `TOOL-TESTS/SystemSetup/*`, `TOOL-TESTS/Vietsub/*Voice*`, `VietsubPiperDownloadFailureTests.cs`, `scripts/Test-Stability.ps1` cùng report helper. Các tên script mới là đề xuất, chưa tồn tại do kế hoạch này. |
| Tài liệu | `VAN_HANH_VA_PHAT_HANH.md`, `KIEM_THU_VA_NGHIEM_THU.md`, hồ sơ voice và báo cáo triển khai mới. Runbook đang mô tả cài online phải được cập nhật sau khi hành vi mới có source/bằng chứng. |

Setup contract hiện nằm trong desktop. Nếu phát sinh DTO public dùng chung, sửa `TOOL-SHARED.Contracts` và consumer trong cùng thay đổi. Chưa thấy nhu cầu sửa database/server cho nguồn cài Piper offline.

## 7. Đầu vào và các mốc hoàn tất

**Đầu vào cần có lúc triển khai:** nguồn tải/artifact chính thức có kiểm hash cho các pin hiện hành; môi trường Windows sạch hoặc máy kiểm thử thứ hai; môi trường auth/license thử nếu kiểm UI/workflow; bản ZIP ứng viên có version/build/hash. Mạng máy chuẩn bị gói phải hoạt động hoặc có artifact đã xác minh chuyển từ môi trường chuẩn bị khác.

- **Mốc A — có gói offline:** PO01–PO04 đạt, inventory/nguồn/hash đầy đủ và mẫu cài offline tạo được WAV thật.
- **Mốc B — ứng dụng tự chuẩn bị đúng:** PO05–PO10 đạt, có hồi quy quyền/gate/cancel/context và tích hợp ZIP/updater.
- **Mốc C — đủ bằng chứng bàn giao:** PO11–PO12 đạt, có kết quả trên Windows sạch/môi trường thứ hai, số đo dung lượng/thời gian và hồ sơ phát hành phù hợp.

Build/test, smoke máy đích và rollout được ghi riêng. Chưa có môi trường hoặc test model bị skip thì giữ phần tương ứng ở trạng thái chưa xác minh. Kế hoạch này không tự cho phép thay DNS/proxy hệ thống, phát hành release, thay SQL/credential hoặc gọi provider có phí.

## 8. Trạng thái triển khai ngày 2026-09-26

| Task | Trạng thái có bằng chứng |
|---|---|
| PO01–PO04 | Đã có pin/inventory, builder, payload thật và reader neo hash vào resource của ứng dụng; proof offline tạo WAV đạt. Hồ sơ phê duyệt phân phối công khai vẫn cần hoàn tất phần dependency nêu trong báo cáo. |
| PO05–PO06 | Đã cài/kiểm/sửa offline, staging, khóa giữa tiến trình, quản lý cây worker và giữ runtime cũ; ba lượt model thật đạt. |
| PO07–PO09 | Đã tích hợp startup, build/publish và CLI chẩn đoán; ZIP đã giải nén được kiểm trên thư mục ứng dụng chỉ đọc, runtime ở ổ khác và năm lần mở lại. |
| PO10 | Build đạt. C# 1.570 Passed / 0 Failed / 13 Skipped mỗi lượt trong ba lượt; frontend 280 Passed / 0 Failed / 0 Skipped mỗi lượt trong ba lượt. Setup C# 58 ca và frontend 23 ca đều đạt 20 lượt riêng. Báo cáo lưu cả lượt timeout khi chạy Full đồng thời với frontend. |
| PO11 | Đã có bằng chứng runtime/ZIP/WAV và ghép MP4 trên máy hiện tại. Windows sạch/máy hoặc tài khoản thứ hai, startup có auth thật, nghe nghiệm thu, workflow UI và đo lưu lượng mạng độc lập: **chưa xác minh**. |
| PO12 | Đã bàn giao source, payload cục bộ, runbook và báo cáo. **Chưa phát hành production**; mốc C chỉ hoàn tất sau các phần nghiệm thu và hồ sơ phân phối còn thiếu. |
