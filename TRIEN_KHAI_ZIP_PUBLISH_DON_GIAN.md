# ZIP Publish đơn giản cho TOOL-LOCAL

Ngày rà soát: **2026-10-17**. Source trên nhánh `main`, HEAD nền `f0651c918f28ca477f28875818148e9e86240b7b`, cùng working tree có thay đổi chưa commit. Kết quả dưới đây thuộc checkout đang thay đổi và binary được tạo trong lượt này; không khẳng định commit nền nguyên trạng đã có hoặc đã kiểm thử tính năng.

Luồng được triển khai là **Publish thư mục → nén ZIP → upload VPS → nhập phiên bản và URL → server kiểm → phát hành → chạy Setup dùng chung**. Người phát hành không phải chuẩn bị ba file `update-manifest.json`, `distribution-profile.json`, `package-inventory.json` trong ZIP.

## Hành vi trong source

| Thành phần | Hành vi hiện tại |
|---|---|
| FolderProfile | Giữ kiểm đầu vào và runtime, xuất thư mục Publish; `DesktopCreateVerifiedReleaseZip=false` mặc định. ZIP kiểm chứng cũ là tùy chọn `true`. |
| ZIP thường | Nhận file ở gốc hoặc đúng một thư mục bọc tên bất kỳ, ví dụ `publish/`; chỉ một `TOOL-LOCAL.exe`, đủ toàn bộ file Publish. |
| Inspector server | Tải đầy đủ ZIP qua allowlist HTTPS, tính SHA-256 toàn ZIP/từng file, kiểm native version/build, cấu hình API/StaticArchive và media. Không thực thi EXE tải lên. |
| Metadata server | Sinh manifest/profile/inventory của ZIP nguyên gốc và lưu trong bằng chứng preparation hiện có; cung cấp artifact metadata qua API có hash, dung lượng, release và Setup identity. Không viết lại ZIP trên VPS. |
| Setup từ 1.1.0 | Tải metadata đúng API/snapshot và ZIP nguyên gốc, kiểm hash/size/từng file, bỏ một wrapper, sinh ba JSON nội bộ trước transaction cài/update/repair. |
| WebView2 | Setup mới nhúng installer Microsoft đã pin khi build một lần; chỉ dùng khi probe runtime còn thiếu, kiểm hash/chữ ký trước chạy. ZIP ứng dụng thường không phải kèm installer này. |
| Model/readiness | Bundle model riêng và mọi probe Qwen/Piper/OCR/media/WebView2 vẫn cần đạt trước khi hoàn tất. Marker hoặc EXE mở trực tiếp không thay xác nhận cài. |

Source chính: [profile Publish](TOOL-LOCAL/Build/VisualStudioRelease.props), [target Publish](TOOL-LOCAL/Build/VisualStudioRelease.targets), [DTO metadata](TOOL-SHARED.Contracts/Updates/DesktopPackageMetadataContracts.cs), [policy ZIP/metadata](TOOL-DISTRIBUTION/Updates/DesktopPackageMetadataPolicy.cs), [inspector](TOOL-SERVER/Updates/DesktopReleaseArchiveInspector.cs), [API metadata](TOOL-SERVER/Controllers/LauncherDistributionController.cs), [luồng Setup](TOOL-SETUP/LauncherInstallerService.Static.cs), [WebView2 đã pin](TOOL-SETUP/ApprovedWebViewInstaller.cs) và [script build Setup dùng chung](scripts/Publish-SharedDesktopSetup.ps1).

Giới hạn vẫn giữ: ZIP tối đa 2 GiB, expanded tối đa 4 GiB, tối đa 200.000 entry và metadata API tối đa 4 MiB. Path traversal, ADS/DOS path, file trùng/case collision, link/reparse, file-directory conflict, nhiều bộ ứng dụng hoặc file ngoài wrapper bị từ chối. Không đưa `appsettings.user.json`, receipt cài đặt hoặc thư mục phát triển vào ZIP. ZIP có ba JSON theo pipeline cũ tiếp tục được kiểm nghiêm ngặt ở gốc; metadata có một phần hoặc sai hash không được bỏ qua bằng nhánh ZIP thường.

Không thêm migration SQL trong thay đổi này. Metadata sử dụng cột bằng chứng preparation hiện có; database đích vẫn phải có schema nền **4.1.18** của Setup dùng chung. Không chạy SQL hoặc xác minh lại database hosting trong lượt này.

## ZIP người dùng đã upload

Inspector trong source mới đã tải và kiểm trực tiếp file sau trên môi trường làm việc. HTTPS dùng kiểm chứng chỉ bình thường; không bỏ qua TLS. Không sửa hoặc upload thêm file trên VPS.

| Trường | Kết quả |
|---|---|
| URL | `https://103.179.190.182:8443/models/publish-01.zip` |
| Phiên bản / build native | `1.0.0` / `1` |
| Dung lượng ZIP | `485.992.498` byte |
| SHA-256 | `20b59b48f774c2ea89c5a5e000306dd445e90a17c3f2b284679739b6f993ab73` |
| Wrapper | `publish/` |
| File được kiểm | `338` |
| Tổng dung lượng giải nén | `1.107.651.969` byte |
| Metadata do server sinh | Có |
| Setup tối thiểu | `1.1.0` |
| Thực thi package / cài / probe model | Chưa thực hiện |
| Production qualified | `false` |

Bằng chứng cuối, chạy lại bằng binary server sau thay đổi source cuối: `C:\vm-simple-zip\20261017-125003\actual-vps-inspection.json`, `actual-vps-inspector-final.log` và `actual-vps-package-metadata.json`. Lượt kiểm đầu giữ riêng trong `actual-vps-first/`. Kiểm file đạt không xác nhận server hosting đang chạy source mới hoặc ZIP đã được đăng ký/phát hành.

## Setup dùng chung đã build

Đã build file Setup dùng chung thật, kiểm CLI inspection và metadata native đạt:

| Trường | Kết quả |
|---|---|
| File | `C:\vm-simple-zip\20261017-125003\shared-setup-1.1.0\VideoMaker Setup.exe` |
| Dung lượng | `267.467.285` byte |
| SHA-256 | `8007e8ec55841c08d1e7f23c8cbf796e20108b03a4975af3c75415008cc6b101` |
| Version / protocol | `1.1.0` / `3` |
| API / profile / channel | `https://taphoatool.com/` / `Production` / `Stable` |
| Model bundle | `models-20261016-03` |
| WebView2 nhúng | Installer `214.131.448` byte đúng pin, chữ ký Microsoft đã kiểm |

File này dùng chung cho các ZIP tương thích và không chứa version/build/hash cố định của một ZIP ứng dụng. Chưa đăng ký/kích hoạt trên server hoặc dùng file này cài ứng dụng. Kiểm binding/resource khi build không thay nghiệm thu WebView2 hoặc model trên Windows sạch.

## Kiểm thử trên checkout này

Bằng chứng của lượt triển khai lưu tại `C:\vm-simple-zip\20261017-125003`:

- Restore và Release solution build cuối đạt **0 Error / 0 Warning**, xem `restore-final.log`, `build-final.log`. Lượt đầu giữ riêng ở `restore.log`, `build.log`.
- Full .NET cuối: **2.718 Passed / 1 Failed / 18 Skipped / 2.737 Total**, thời gian console 4 phút 57 giây. Xem `full-final.log`, `rawzip-full-final.trx`, `test-outcomes-final.json`. Bài duy nhất không đạt là `VoiceRenderer_TwoHours_KeepsLastPhraseAndUsesBoundedIntermediateFiles`: renderer từ chối vì ổ đĩa không đủ chỗ cho timeline hai giờ. Không hạ kiểm tra dung lượng, loại bài khỏi suite hoặc đổi thành skip. 18 bài SQL/model/GPU/voice opt-in bị skip vẫn chưa được nghiệm thu.
- Nhóm liên quan ZIP/Setup/server/Publish trong chính full suite cuối đạt **448 Passed / 0 Failed / 0 Skipped**, gồm hai bài bổ sung từ chối tên trường cấu hình trùng hoặc chỉ khác hoa/thường. Không cộng nhóm này thêm vào tổng full. Lượt focused trước đó **446/0/0** giữ tại `focused.log`, `rawzip-focused.trx`.
- Frontend `npm ci`, build và test đã chạy; **341 Passed / 0 Failed / 0 Skipped**, 48 file. Xem `frontend-ci.log`, `frontend-build.log`, `frontend-test.log`.
- Admin browser: **27 Passed / 0 Failed / 0 Skipped** cho `admin-release-preparations.browser.test.cjs` và `admin-external-release.browser.test.cjs`, dùng Razor/JS/CSS thật với API fixture; xem `admin-release-browser-final.log`. Chạy headless Microsoft Edge đã có bằng package `playwright-core` ngoài repository. Lượt đầu chưa chỉ định module và không chạy được vì thiếu `playwright`; giữ log tại `admin-release-browser-initial.log`, không sửa source để vượt qua.
- Đã kiểm trực tiếp ZIP VPS theo bảng trên bằng inspector mới, không thực thi binary trong ZIP.

Các nhóm regression bảo vệ root/wrapper, directory, traversal/link/duplicate/size, metadata JSON nghiêm ngặt, hash ZIP/từng file, API snapshot, update/repair ownership, stage/rollback và WebView2 nhúng qua `.part`. Fixture HTTP/SQLite/resource không thay thế cài Setup thật hoặc nghiệm thu model trên máy đích. Phần tài liệu này không chạy thêm build/test.

Danh mục **1.397 file source/script/config/frontend** trước và sau kiểm tra cuối có cùng SHA-256 `c5c009c30e6d54b61d5bc2453ca620affac44610958c243db8e20bd13f781761`; xem `source-before-final-checks.json`, `source-after-final-checks.json`, `source-verification.json`. Tài liệu Markdown được cập nhật sau kiểm thử và không nằm trong danh mục này. Hash sáu binary build/Setup lưu tại `final-binary-fingerprints.json`.

Chưa có bằng chứng cài Setup mới trọn bộ trên Windows sạch, chạy model Qwen/Piper đã cài bằng Setup này hoặc workflow SQL của máy khách. Chưa deploy server, đăng ký/kích hoạt Setup mới hay publish release production trong lượt triển khai. Full suite còn một bài bị chặn dung lượng như ghi trên, không kết luận toàn bộ hệ thống đã đạt.

## Các bước vận hành tiếp theo

1. **Deploy TOOL-SERVER source mới** lên API đích. Cấu hình đúng `DesktopReleases.ApiBaseUrl`, profile và `SharedBootstrapperEnabled`, cùng `AllowedExternalPrefixes` chứa nguồn ZIP thực dùng. Binary mới hỗ trợ cả `/apps/videomaker/` và `/models/` trên host/port đã duyệt; cấu hình server cũng phải cho phép prefix được chọn. Kiểm server khởi động với schema nền hiện có.
2. **Đăng ký và kích hoạt Setup dùng chung 1.1.0 đã build ở trên**, nếu API/profile đích đúng `https://taphoatool.com/` / `Production` / `Stable`. Nếu cần API/profile khác, build một lần bằng `Publish-SharedDesktopSetup.ps1` với `-WebView2InstallerPath` trỏ file đúng pin; script nhúng installer, không tải bản Evergreen mới rồi tự đổi pin. Không dùng Setup cũ 1.0.0 để nhận metadata ZIP thường.
3. **Tạo release ứng dụng trong Admin**: nhập phiên bản `1.0.0` và URL ZIP hiện tại, tùy chọn ghi chú/bắt buộc cập nhật. **Lưu nháp → chờ Kiểm tra đạt → Phát hành**. Server tự lấy build `1`, tên file, size/checksum và metadata từ file thực; không nhập JSON hoặc checksum bằng tay.
4. **Chạy Setup mới** trỏ đúng API, chọn phiên bản vừa phát hành và thư mục cài. Setup chỉ báo hoàn tất sau kiểm ứng dụng, model và runtime; tiếp tục nghiệm thu trên máy đích trước rollout rộng.
5. **Phát hành lần sau**: tăng version/build, Publish, nén đầy đủ thư mục và upload bằng URL riêng; lặp bước tạo release. Một Setup chung tiếp tục dùng cho các ZIP tương thích, không build Setup theo từng ZIP.

Nếu database đã từng phát hành `1.0.0`/build `1` bằng nội dung khác, không thay bytes của release đó. Cần tăng version hoặc build và Publish lại; kết quả kiểm file VPS ở trên không bỏ qua bất biến phiên bản đã phát hành.

Để phát hành ví dụ `1.0.1`/build `2`, đặt vào `TOOL-LOCAL/Properties/PublishProfiles/FolderProfile.local.props` trước khi Publish:

```xml
<Project>
  <PropertyGroup>
    <DesktopPublishVersion>1.0.1</DesktopPublishVersion>
    <DesktopPublishBuildNumber>2</DesktopPublishBuildNumber>
  </PropertyGroup>
</Project>
```

Nén output Publish vừa thành công rồi nhập **`1.0.1`** trong Admin. Server tự đọc build `2` từ binary. Chỉ đổi tên ZIP hoặc nhập `1.0.1` cho ZIP chứa binary `1.0.0`/build `1` sẽ bị từ chối vì sai phiên bản; không cần tạo thêm JSON để đổi version.

Desktop cũ chỉ chấp nhận nguồn ứng dụng `/apps/videomaker/` chưa thể tự nhận release đặt ở `/models/`. Setup mới độc lập có thể cài ZIP hiện tại, nhưng không sửa policy đã compile trong EXE cũ bên trong ZIP. Để ứng dụng tự cập nhật qua `/models/`, cần Publish desktop từ source mới và dùng Setup mới cài bản đó một lần. Việc thay source trong repository hoặc chỉ đổi Setup không tự sửa binary đã cài.

Hồ sơ corresponding source/GPL của bộ FFmpeg vẫn chưa hoàn tất và `redistributionQualified=false`, `productionQualified=false`; xem [hồ sơ nguồn](third_party/ffmpeg/RELEASE_REVIEW.md). Không nâng kết quả kiểm ZIP hoặc phê duyệt kỹ thuật thành nghiệm thu phân phối/production. Các bằng chứng Publish/ZIP cũ thiếu dung lượng được giữ trong [biên bản FFmpeg](TRIEN_KHAI_CHUAN_HOA_FFMPEG_VA_PUBLISH.md); luồng mặc định mới không còn bắt buộc bước packager cũ đó.

Hướng dẫn thao tác Publish chi tiết: [VisualStudioPublish.README.vi.md](scripts/VisualStudioPublish.README.vi.md).
