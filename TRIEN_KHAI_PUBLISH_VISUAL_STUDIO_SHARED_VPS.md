# Triển khai đóng gói SharedVps khi Publish bằng Visual Studio

**Luồng mặc định đã được thay thế:** theo [triển khai ZIP Publish đơn giản](TRIEN_KHAI_ZIP_PUBLISH_DON_GIAN.md), `FolderProfile` hiện xuất thư mục để nén trực tiếp; server tự kiểm và tạo metadata khi ZIP không có ba JSON. Các yêu cầu phía dưới về ZIP có metadata do packager tạo áp dụng cho đường tùy chọn `DesktopCreateVerifiedReleaseZip=true` và bằng chứng lịch sử. ZIP thông thường cần Setup dùng chung tối thiểu 1.1.0. Không dùng trạng thái thiếu ZIP/thiếu dung lượng của lượt dưới đây thay kết quả kiểm ZIP VPS mới; các giới hạn Setup/model/GPL/production vẫn được phân biệt riêng.

Ngày rà soát: **2026-10-17**, nhánh `main`, commit nền `f0651c918f28ca477f28875818148e9e86240b7b`, checkout có thay đổi chưa commit. Biên bản này chỉ ghi bằng chứng của lượt hiện tại; kết quả build hoặc Publish cũ không thay kết quả kiểm tra mới.

Đã nối quy trình đóng gói vào `TOOL-LOCAL → Publish → FolderProfile → Publish`. Khi đủ đầu vào phát hành, nút Publish build ứng dụng rồi tự xuất ZIP `SharedVps` có manifest, profile và inventory ở gốc. Luồng đóng gói dùng kết quả Publish vừa chạy, không gọi một lượt Publish lồng bên trong và không build Setup riêng theo từng ZIP.

Hướng dẫn thao tác và cấu hình mẫu nằm tại [scripts/VisualStudioPublish.README.vi.md](scripts/VisualStudioPublish.README.vi.md). Bằng chứng cuối của lượt bổ sung FFmpeg nằm tại [biên bản chuẩn hóa FFmpeg và Publish](TRIEN_KHAI_CHUAN_HOA_FFMPEG_VA_PUBLISH.md): đã tạo output SDK TOOL-LOCAL thật và probe native đạt; bước tạo ZIP sau Publish bị chặn dung lượng. Chưa có ZIP khách, bản cài Setup mới được nghiệm thu hoặc rollout production.

**Bổ sung FFmpeg mặc định — 2026-10-17**

`FolderProfile` đã tự chuẩn bị offline bundle `gyan-9.0.1-essentials-win-x64` tại `artifacts/ffmpeg-release/gyan-9.0.1-essentials-win-x64`. Helper dùng [định nghĩa đã pin](third_party/ffmpeg/FFMPEG_RELEASE_APPROVED.json), kiểm binary/license và hồ sơ nguồn, kiểm phiên bản native, staging rồi promote nguyên thư mục. Cache còn nguyên vẹn được dùng lại; cache thay đổi bị từ chối, không ghi đè. Bộ gốc `third_party/ffmpeg/win-x64` vẫn giữ hồ sơ Development. MSBuild thêm đủ tám file vào Content sau chuẩn bị nên lần Publish đầu cũng có license, README, nguồn và kết quả probe; các Content khác giữ đường dẫn riêng.

Scope `Release` mới là **TechnicalPackaging** theo yêu cầu chuẩn hóa Publish/test cục bộ của người dùng. Đây không phải xác nhận đủ điều kiện phân phối GPL: [hồ sơ rà soát](third_party/ffmpeg/RELEASE_REVIEW.md) và `SOURCE-AVAILABILITY.md` ghi corresponding source chưa đầy đủ; `correspondingSourceComplete=false`, `redistributionQualified=false`, `productionQualified=false`. Các trạng thái này được giữ trong định nghĩa/báo cáo và hồ sơ đi kèm ZIP; không suy ra đã nghiệm thu production từ nhãn Release.

Trên máy build đã tạo riêng `TOOL-LOCAL/appsettings.release.json`, được Git bỏ qua. **Chưa tạo `FolderProfile.local.props`**; lượt này giữ version **1.0.0**, build **1** và các đường dẫn mặc định. `.local.props` chỉ là tùy chọn override. Cấu hình release không có SQL password dùng chung; các validator cấu hình/model, FFmpeg, OCR và WebView2 đã đạt. Desktop vẫn cần provision workflow SQL theo mục phía dưới.

Bằng chứng cuối tại `C:\vm-ffmpeg-release\20261017-053859`: `build-final.log` đạt **0 error / 0 warning**; `focused-layout-final.log` và `ffmpeg-focused-layout-final.trx` ghi **96 Passed / 0 Failed / 0 Skipped**, 38 giây, gồm SDK single-file regression mới. Test kiểm runtime Content có Link còn rời và có trong danh sách SDK, managed reference vẫn bundle, Content `Never` không bị mở phân phối. Frontend `frontend-final.log` đạt **341 Passed / 0 Failed / 0 Skipped**, 48 file. Build frontend thật chạy trong SDK đạt, còn Vite chunk warning; không chạy lại `npm ci` vì D chỉ còn khoảng 34 MB. Các lượt 86/95 focused trước được giữ trong thư mục bằng chứng, không cộng vào số bài duy nhất hoặc thay kết quả cuối.

SDK đã tạo output TOOL-LOCAL single-file thật `1.0.0`/build `1` tại `C:\vm-ffmpeg-release\20261017-053859\publish`; worker/fixture/license được giữ rời. `native-bundle-final.log` ghi WebView2, Media, OCR Anh/Trung **READY**, `OcrNativeRuntimeAppLocal=True`; `ServerAccessChecked=False`. Đây là probe local trên máy build, chưa phải cài Setup hoặc xác minh server/SQL/quyền.

**Bước tạo ZIP chưa đạt:** `publish-final-loose-content.log` dừng đúng guard dung lượng, chỉ dọn payload tạm thuộc lượt chạy; không có ZIP mới. Full suite chưa chạy lại: `full-suite-resource-preflight.json` yêu cầu **5.290.061.824 byte**, khả dụng **2.570.477.568 byte** (khoảng 2,39 GiB) **tại thời điểm đo trước**, đánh giá `Blocked`; preflight không bao gồm ngân sách cài model hoặc nghiệm thu Windows sạch. Đo cuối lượt còn **C: 0,771 GiB; D: 0,034 GiB**. Chưa QA ZIP, cài Setup, Qwen/Piper installed inference, Windows sạch hoặc rollout. Các số full suite phía dưới là lịch sử, không phải kết quả mới sau bổ sung FFmpeg.

`source-final-summary.json` ghi **1.400 file**, nhánh `main`, HEAD nền như đầu tài liệu, working tree dirty. SHA-256 manifest `source-final-manifest.json`: `d97754e5d288aa2c32606fc7b746a70820e96c0b1af53b640a61da7d2a97a742`. `verification-final.json` xác nhận 1.400 file source không đổi sau test, hash native EXE không đổi và số ZIP mới **0**. Không dùng manifest lịch sử 1.117 file phía dưới làm identity cho lượt này.

**Thay đổi đã triển khai**

| File | Trách nhiệm |
| --- | --- |
| [TOOL-LOCAL/Build/VisualStudioRelease.props](TOOL-LOCAL/Build/VisualStudioRelease.props) | Nhận diện `FolderProfile` từ `PublishProfile`, `WebPublishProfileFile` hoặc `PublishProfileFullPath`, import cấu hình riêng sớm để version/build và protocol áp dụng ngay khi compile. |
| [TOOL-LOCAL/Build/VisualStudioRelease.targets](TOOL-LOCAL/Build/VisualStudioRelease.targets) | Kiểm đầu vào trước build/publish; giữ runtime Content rời trước `GetCopyToPublishDirectoryItems` bằng metadata trên Content/ContentWithTargetPath, giữ managed reference trong bundle. Sau Publish ghi danh sách SDK, bổ sung single-file apphost và gọi script đóng gói. |
| [TOOL-LOCAL/Properties/PublishProfiles/FolderProfile.pubxml](TOOL-LOCAL/Properties/PublishProfiles/FolderProfile.pubxml) | Bật tích hợp cho profile quen thuộc, giữ `Release`, `win-x64`, self-contained và single-file. |
| [TOOL-LOCAL/Properties/PublishProfiles/FolderProfile.local.props.example](TOOL-LOCAL/Properties/PublishProfiles/FolderProfile.local.props.example) | Mẫu version/build và đường dẫn đầu vào riêng cho máy build. |
| [TOOL-LOCAL/TOOL-LOCAL.csproj](TOOL-LOCAL/TOOL-LOCAL.csproj) | Import props trước các item phụ thuộc cấu hình, import targets ở cuối project. |
| [scripts/Test-VisualStudioDesktopPublish.ps1](scripts/Test-VisualStudioDesktopPublish.ps1) | Kiểm version, cấu hình khách, StaticArchive, FFmpeg Release, OCR runtime và offline WebView2; không in connection string hoặc cấu hình nhạy cảm. |
| [scripts/Prepare-ReleaseFfmpegBundle.ps1](scripts/Prepare-ReleaseFfmpegBundle.ps1) và [ApprovedFfmpegBundle.psm1](scripts/ApprovedFfmpegBundle.psm1) | Chuẩn bị offline bộ FFmpeg đã pin vào thư mục riêng; kiểm mọi file và native version, staging/promote, dùng lại cache bất biến và giữ trạng thái qualification. |
| [FFMPEG_RELEASE_APPROVED.json](third_party/ffmpeg/FFMPEG_RELEASE_APPROVED.json) và [RELEASE_REVIEW.md](third_party/ffmpeg/RELEASE_REVIEW.md) | Identity/hash và bằng chứng rà soát kỹ thuật; ghi riêng phần corresponding source, phân phối GPL và production chưa được xác nhận. |
| [scripts/New-DesktopPublishedPackage.ps1](scripts/New-DesktopPublishedPackage.ps1) | Đóng gói output đã Publish, kiểm binary, cấu hình, checksum, inventory và ZIP giải nén; chỉ promote kết quả sau khi đạt các bước kiểm tra. |
| [scripts/DesktopPublishedPackage.psm1](scripts/DesktopPublishedPackage.psm1) | Chọn/copy file hiện hành, chặn state/cache/model/secret không được phân phối, kiểm đường dẫn, tạo metadata và quản lý output bất biến theo version/build. |
| [TOOL-TESTS/Updates/VisualStudioPublishIntegrationTests.cs](TOOL-TESTS/Updates/VisualStudioPublishIntegrationTests.cs) | Kiểm MSBuild/profile/hook và SDK single-file publish fixture thật: linked runtime Content còn rời, managed reference vẫn bundle, Content Never không phân phối. |
| [TOOL-TESTS/Updates/DesktopPublishedPackageTests.cs](TOOL-TESTS/Updates/DesktopPublishedPackageTests.cs) | Kiểm file list, native identity, đường dẫn, dữ liệu bị cấm, metadata, checksum và promote/reuse output. |
| [TOOL-TESTS/Updates/ApprovedFfmpegBundleTests.cs](TOOL-TESTS/Updates/ApprovedFfmpegBundleTests.cs) | Kiểm pin, nguồn/đường dẫn, cache bất biến, staging rollback và target MSBuild giữ đủ hồ sơ mà không đổi Content khác. |
| [.gitignore](.gitignore) | Không đưa `.local.props` và `appsettings.release.json` riêng vào Git. |

Props compile `DesktopDistributionProtocol=3`, `StaticModelSetupRequired=true`, profile tương ứng, channel `Stable`, `InformationalVersion=<version>+desktopbuild.<build>`. Piper payload không được bundle vào ZIP ứng dụng. Model vẫn theo bundle `models-20261016-03` đã pin và được cài riêng qua Setup.

**Hành vi đóng gói**

Máy hiện tại đã có `appsettings.release.json` riêng và các đầu vào ở đường dẫn mặc định; không cần tạo `.local.props` hoặc chọn/duyệt lại FFmpeg mỗi lần Publish. Khi cần override đầu vào/version/build, có thể dùng `.local.props.example` làm mẫu. Version/build do người phát hành quản lý; không tự tăng sau mỗi lần bấm Publish. Thiếu cấu hình hoặc đầu vào không đạt sẽ làm Publish lỗi với thông báo có hướng xử lý, không âm thầm dùng cấu hình phát triển.

Mặc định ZIP cuối nằm tại:

```text
artifacts/releases/<version>-<build>/
  VideoMaker-<version>-<build>-win-x64.zip
  checksums.sha256
  package-report.json
```

Không tự nén `desktop-publish` rồi upload. Danh sách file từ SDK loại file cũ còn sót ở thư mục Publish; helper không copy môi trường Python, cache, model weights hoặc trạng thái sẵn sàng của máy build. Đường dẫn không được thoát khỏi phạm vi hoặc đi qua reparse point. ZIP có `update-manifest.json`, `distribution-profile.json`, `package-inventory.json` ngay tại gốc, không thêm thư mục bao ngoài.

Kết quả được chuẩn bị trong thư mục `.part` riêng, kiểm đầy đủ rồi promote cả thư mục bằng thao tác không ghi đè. Publish lại cùng version/build với nội dung file giống hệt dùng lại ZIP đã có, kể cả metadata timestamp của ZIP mới khác. Cùng version/build mà nội dung khác sẽ báo phải tăng build và giữ bản cũ nguyên vẹn. Kết quả chưa hoàn tất không được thay thế ZIP đã tạo thành công.

Các kiểm tra Release hiện có được giữ: FFmpeg phải có hồ sơ `Release`, WebView2 phải đúng size/hash/chữ ký Microsoft, cấu hình phải đúng API và không có SQL password dùng chung; binary phải khớp version/build/profile/protocol, probe và inventory phải qua. `packageVerified=true` không tự nâng thành `productionQualified=true`.

**Bằng chứng lượt tích hợp ban đầu, trước bổ sung FFmpeg**

Thư mục bằng chứng: `C:\vm-vs-publish\20261017-050325`.

| Kiểm tra | Passed | Failed | Skipped | Bằng chứng/phạm vi |
| --- | ---: | ---: | ---: | --- |
| `dotnet restore TOOL_GEN_POST_VIDEO.slnx` | Thành công | 0 | — | `restore.log`, exit 0. |
| Build solution Release | Thành công | 0 | — | `build-final.log`, exit 0, 0 warning, 0 error. |
| Nhóm .NET Publish/đóng gói sau sửa | 53 | 0 | 0 | `focused-final.log`, `vs-publish-focused-final.trx`. Fixture có kiểm soát, không gọi VPS/SQL/model. |
| Frontend | 341 | 0 | 0 | 48 file test; `frontend-ci.log`, `frontend-build.log`, `frontend-test.log`; `npm ci`, build và test đều đạt. |
| Toàn bộ .NET suite | 2.535 | 1 | 18 | 2.554 tổng, 8 phút 18 giây; `full.log`, `vs-publish-full.trx`, `full-outcomes.json`. Bài hai giờ bị chặn vì thiếu disk, chi tiết dưới đây. |

Nhóm focused 53 bài đã nằm trong tổng full, không cộng thêm. 18 bài opt-in SQL/model/GPU/voice bị Skipped không chứng minh runtime đích hoặc model thật đã đạt. Source trong `source-final.json` và ba assembly ở `binaries-final.json` được so lại sau full: **0 file thay đổi**, xem `consistency-final.json`. Frontend build vẫn có cảnh báo kích thước chunk Vite; 0 warning ở đây là kết quả MSBuild.

Lượt focused đầu tiên có **52 Passed / 1 Failed** do đường copy chưa giữ đúng file Hidden. Đã sửa xử lý `Force`, build lại và chạy focused cuối đạt **53/53**. Giữ cả `focused.log` và `vs-publish-focused.trx` của lượt lỗi; không thay kết quả đó bằng kết quả sau sửa.

Preflight thực với cấu hình release còn thiếu trả exit 1 và thông báo hành động đúng kỳ vọng (`preflight-missing-settings.json`). Fixture xác nhận SQL password bị chặn, sentinel secret không lộ trong lỗi và FFmpeg `Development` bị chặn (`preflight-fixtures.json`). Các bài từ chối đúng kỳ vọng này không tạo ZIP khách.

Trong lượt full, bài `VietsubLongVideoTests.VoiceRenderer_TwoHours_KeepsLastPhraseAndUsesBoundedIntermediateFiles` bị chặn bởi kiểm tra dung lượng của renderer. Bài hai giờ yêu cầu tối thiểu **4.753.190.912 byte, khoảng 4,43 GiB** trống tại volume temp; lúc rà soát C còn khoảng **3,33 GiB**, D khoảng **2,96 GiB** trước khi chuẩn bị WebView2. Đây là một kết quả Failed do môi trường, không đổi thành Passed hoặc Skipped. Chưa hạ gate hoặc sửa renderer. Kiểm metadata read-only các thư mục bằng chứng được chỉ định không tìm đủ dung lượng có thể giải phóng; chưa xóa hoặc di chuyển dữ liệu. Cần chuẩn bị volume QA đủ chỗ để chạy lại bài này.

Manifest source cuối cho tập code/script được chọn ghi **1.117 file**, SHA-256 của `source-final.json`:

```text
bb95545ef9a936f109b2308f467dfc12d8165923a34a6260dfeea0bac460f8a4
```

Xem `source-final-summary.json` và `binaries-final.json`. Bộ lọc của manifest này khác các snapshot cũ; không so số lượng file giữa các bộ lọc để suy ra source thiếu hoặc thay đổi. Các tài liệu bàn giao cập nhật sau snapshot không được mô tả là có trong snapshot code/script.

**SDK fixture và giới hạn của bằng chứng tích hợp**

`sdk-fixture` dùng SDK **10.0.301** để Publish thật một Console fixture x64 self-contained, single-file. Hai entry point preflight/package trong lần chạy SDK được thay bằng **stub riêng của fixture**, cho phép quan sát thứ tự hook và danh sách file/argument. Đây không phải một lượt chạy thành công toàn bộ production packager trên TOOL-LOCAL.

`trace.jsonl` chứng minh preflight rồi package; single-file apphost xuất hiện đúng một lần, managed worker cần giữ rời vẫn có trong danh sách, assembly đã bundle không bị yêu cầu copy riêng. Sau đó helper production hiện hành thực sự copy file đã chọn, đọc native x64 version/build mà không chạy exe, tạo metadata ở gốc, tạo ZIP, giải nén và kiểm inventory cùng chính sách archive. Xem `sdk-fixture/README.md`, `verification.json` và `source-artifact-hashes.json`.

Artifact fixture sau sửa Hidden:

```text
fixture-package-after-hidden-fix.zip
Size: 32.035.891 bytes
SHA-256: be91be6a9e208a74ebe950f6c867939b1648814c947be3ccb21c405dbc345ddb
```

Artifact này là bằng chứng kỹ thuật, không dùng gửi khách hoặc tạo Active release. Nó không kiểm production native probes, model inference, SQL, cài Setup hay VPS. Lượt xác minh thủ công đầu đã thiếu import module registration; đã bổ sung import rồi kiểm lại cùng byte, không đổi source/package để xử lý lỗi shell đó. Lịch sử được giữ trong README fixture.

**Đầu vào thật và bước còn lại**

Cấu hình release riêng `TOOL-LOCAL/appsettings.release.json` **đã được chuẩn bị và qua validator** trong lượt bổ sung FFmpeg. File dùng API/profile StaticArchive, bốn feature local và update `Stable/win-x64`, không có SQL password dùng chung. Không chọn nguyên `TOOL-LOCAL/appsettings.json` phát triển để thay file này; không đưa cấu hình riêng vào Git.

Desktop hiện còn dùng SQL workflow. Profile cho phép SQL chuyển tiếp không cấp tài khoản hoặc biến desktop thành API-only. Tài khoản SQL riêng cho khách chỉ được quyền workflow `vf`; credential cần cấu hình riêng từng máy qua [Configure-CustomerSql.ps1](scripts/Configure-CustomerSql.ps1), không gửi trong ZIP. Script ghi `appsettings.user.json` với ACL người dùng và không tự kiểm kết nối/quyền/schema. Thiếu password trong gói là điều kiện an toàn phát hành, không phải bằng chứng máy khách đã truy cập SQL được.

FFmpeg mặc định hiện dùng bundle **Release/TechnicalPackaging** được tự chuẩn bị riêng, không sửa hồ sơ Development gốc. License, provenance, checksum cùng hồ sơ nguồn/qualification được giữ và kiểm theo pin; corresponding source và điều kiện phân phối vẫn có phần chưa hoàn tất như mục bổ sung phía trên. OCR native mặc định đã kiểm đủ bốn file/hash. Offline WebView2 đã được copy không ghi đè vào `artifacts/prerequisites/webview2-runtime-x64.exe`, kiểm size **214.131.448 byte**, SHA-256 `3505a37b54a294c2bc423b30d6c02cd33a2e58dbcdc7e03b50d7a4112746c986` và chữ ký Microsoft hợp lệ. Các bộ này dùng đường dẫn mặc định; không cần nhập lại `.local.props` trừ khi đổi nơi lưu. `actual-inputs.json` và `prepared-webview2-input.json` thuộc lượt kiểm đầu vào trước; bằng chứng bổ sung FFmpeg ở thư mục mới phía trên, chưa phải toàn production packager đạt.

Chưa thực hiện hoặc chưa xác minh trong lượt này:

- Hoàn tất production packager trên output TOOL-LOCAL thật, tạo/giải nén/kiểm ZIP khách; lượt vừa chạy dừng guard dung lượng trước khi có ZIP.
- Bấm Publish qua giao diện Visual Studio với đầu vào khách hoàn chỉnh.
- Cài/upgrade/repair bằng Setup trên Windows sạch; chạy Qwen/Piper từ bản cài và OCR trong toàn luồng cài. Probe native OCR Anh/Trung trên output publish local đã đạt riêng như mục bằng chứng cuối.
- Upload ZIP lên VPS, cấu hình Nginx, đăng ký/kích hoạt Setup, phát hành release hoặc rollout production.

Không chạy SQL, deploy server hoặc tạo release thật trong lượt tích hợp. Không thay cấu hình phát triển, xóa model người dùng hoặc ghi đè ZIP cũ. Sau khi đủ đầu vào và Publish thành công, upload ZIP được tạo lên URL ứng dụng theo version/build, tạo release bằng version + link tải, chờ server kiểm đạt rồi mới phát hành. Luồng Setup dùng chung và các gate môi trường tiếp tục theo [biên bản Setup dùng chung](TRIEN_KHAI_FORM_RELEASE_TOI_GIAN_VA_SETUP_DUNG_CHUNG.md).
