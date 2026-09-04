# Task xử lý merge conflict `main` vào `local-2`

> Ngày ghi nhận: 2026-09-04
> Trạng thái: Hoàn tất resolve, rà semantic, build/test, stage resolution và merge `main` vào `local-2`.
> Phạm vi: Chỉ xử lý các lỗi tích hợp phát sinh từ lần merge `main` vào `local-2`.

## 1. Mục tiêu

Hoàn tất merge theo hướng bảo toàn đồng thời:

- Module Vietsub, editor workspace và các sửa đổi FFmpeg/UI đang có trên `local-2`.
- Fal/Veo cho video dài, thanh toán SePay, tự động phân bổ người dùng vào tổ chức và các sửa đổi dự án/UI từ `main`.
- Luồng mở dự án video dài cũ phải tải và hiển thị content/kịch bản đã lưu, kể cả khi dự án chưa có scene.
- Các bất biến bảo mật của AI Gateway, license, organization và WebView bridge.

Không mở rộng task sang thiết kế lại nghiệp vụ, chạy migration thật, gọi provider có chi phí, thanh toán thật hoặc phát hành ứng dụng.

## 2. Baseline Git đã kiểm tra

- Nhánh hiện tại: `local-2`.
- HEAD của `local-2`: `09b763d` (`fix ffmed & UI`).
- `MERGE_HEAD` từ `main`: `3948396` (`setup auto payment auto add to-chuc`).
- Common base gần nhất: `e42c086`.
- Git đã tự merge và đưa vào staging 120 đường dẫn.
- Còn 5 file unmerged với tổng cộng 16 conflict block.

Không dùng `git reset --hard`, không thay nguyên toàn bộ file bằng một phía và không tạo merge commit trước khi hoàn tất các gate kiểm tra.

## 3. Phân loại thay đổi hai nhánh

### `local-2`

- Tích hợp module Vietsub độc lập.
- Thêm `VietsubWebBridge`, project/store/media/playback/subtitle service riêng.
- Thêm trang và state React dành cho Vietsub.
- Bổ sung virtual media host `vietsub-media.app.local`.
- Sửa FFmpeg, playback và UI Vietsub.

### `main`

- Tích hợp Fal/Veo cho workflow video dài.
- Thêm license gate và thanh toán SePay.
- Thêm tự động phân bổ người dùng vào organization sau thanh toán.
- Sửa điều hướng dự án ngắn/dài theo `workflowStructureType` và request ID.
- Thêm `ProjectContentSummary`, tải script đã lưu và hiển thị content của dự án dài cũ.

Hai nhóm tính năng không thay thế nhau. Cách xử lý mặc định là hợp nhất cộng dồn có kiểm soát, sau đó kiểm tra tương tác giữa chúng.

## 4. Danh sách conflict và quyết định xử lý

| File | Số block | Quyết định |
|---|---:|---|
| `TOOL-LOCAL/Form1.cs` | 6 | Giữ cả dependency Vietsub và `LicensePaymentApiClient`; truyền đủ dependency cho hai bridge |
| `TOOL-LOCAL/Program.cs` | 2 | Khởi tạo cả service Vietsub và payment client; truyền đầy đủ vào `Form1` |
| `TOOL-LOCAL/Web/index.html` | 1 | Hợp nhất CSP cho media VideoMaker, media Vietsub và QR SePay |
| `TOOL-LOCAL/Web/src/App.tsx` | 4 | Giữ state/effect của Vietsub và SePay; giữ routing mới của `main`; bỏ state ngắn hạn đã bị loại bỏ |
| `TOOL-LOCAL/WebView/DashboardBridge.cs` | 3 | Giữ feature flag Vietsub, payment client, handler thanh toán và license lock policy |

### 4.1. `TOOL-LOCAL/Form1.cs`

- Giữ toàn bộ namespace Vietsub và `TOOL_LOCAL.Payments`.
- Giữ các field service Vietsub, `_featureOptions` và `_licensePaymentClient`.
- Constructor nhận, validate và gán đủ cả hai nhóm dependency.
- Khi tạo `DashboardBridge`, truyền cả payment client và `VietsubEnabled` theo đúng chữ ký đã hợp nhất.
- Giữ nguyên `VietsubWebBridge`, routing message prefix `vietsub.*`, virtual host và xử lý HTTP Range hiện tại.

### 4.2. `TOOL-LOCAL/Program.cs`

- Giữ các namespace Vietsub và Payments.
- Giữ `LicensePaymentApiClient` dùng session hiện hành.
- Giữ khởi tạo service Vietsub có điều kiện theo `Features:VietsubEnabled`.
- Lời gọi `new Form1(...)` phải truyền đủ feature options, các service Vietsub và payment client.
- Không đưa provider key hoặc provider client trực tiếp vào desktop.

### 4.3. `TOOL-LOCAL/WebView/DashboardBridge.cs`

- Giữ đồng thời `_vietsubEnabled` và `_licensePaymentClient`.
- Constructor nhận và gán cả hai dependency.
- Giữ các message `license.offers.get`, `license.payment.create`, `license.payment.current.get`, `license.payment.status` và `license.refresh`.
- Giữ `DashboardFeatureFlagsResponse(_vietsubEnabled)` trong `dashboard.state`.
- Giữ allowlist thao tác được phép khi license đang khóa; không cho phép generation hoặc Vietsub phát sinh chi phí vượt qua license gate.

### 4.4. `TOOL-LOCAL/Web/index.html`

CSP sau hợp nhất phải có đúng hợp nguồn cần thiết:

- `img-src`: `'self'`, `data:`, `https://media.app.local`, `https://vietsub-media.app.local`, `https://qr.sepay.vn`, `https://vietqr.app`.
- `media-src`: `https://media.app.local`, `https://vietsub-media.app.local`.
- Giữ nguyên `connect-src 'none'`, `object-src 'none'`, `frame-src 'none'`, `base-uri 'none'` và `form-action 'none'`.
- Không thêm wildcard hoặc host tùy ý.

### 4.5. `TOOL-LOCAL/Web/src/App.tsx`

- Giữ `useVietsubModule` và toàn bộ page/state Vietsub.
- Giữ `selectedProjectRequestRef`, `licenseRequestsRef`, `licenseBootstrapRequestedRef` và `licenseStatusInFlightRef`.
- Giữ các effect bảo vệ feature flag Vietsub, lưu trạng thái sidebar và polling thanh toán.
- Khai báo đồng thời `pageBusy` và `licenseLocked` vì hai biến phục vụ hai luồng khác nhau.
- Khi đổi organization:
  1. Nếu editor Vietsub đang mở, flush dữ liệu đang sửa.
  2. Đóng Vietsub project; nếu thất bại thì dừng chuyển organization.
  3. Xóa `selectedProjectRequestRef.current`.
  4. Bật busy và gửi `organization.select`.
- Không giữ `setShortVideoProjectId(null)`: state `shortVideoProjectId` đã bị `main` loại bỏ.
- Giữ routing mới dựa trên `workflowStructureType`:
  - `DirectShortVideo` mở trang video ngắn.
  - Các project còn lại mở trang video dài.
- Chỉ đổi trang sau khi nhận `dashboard.state` có đúng request ID của thao tác chọn project.

## 5. Bảo toàn sửa lỗi content dự án dài

Các thay đổi sau từ `main` phải được giữ nguyên:

- `ProjectService.GetDashboardAsync` tìm script theo `CurrentScriptVersion`; nếu thiếu thì fallback về script `Approved` mới nhất.
- `ProjectDashboard` trả thêm `ProjectContentSummary` gồm version, title, full script, hook, angle, audience và call to action.
- Frontend xác định content tồn tại bằng dữ liệu content đã lưu, không dùng số scene làm điều kiện duy nhất.
- Dự án có content nhưng `0` scene vẫn hiển thị toàn bộ kịch bản.
- Dự án sinh content thất bại và chưa có dữ liệu phải hiển thị trạng thái lỗi rõ ràng, không giả là “Chờ sinh nội dung”.
- Giữ các test trong `ProjectDashboardContentTests` và test UI liên quan.

## 6. Rà soát conflict logic trong các file Git đã auto-merge

Git không báo conflict không có nghĩa là tích hợp đã đúng. Phải kiểm tra tối thiểu:

- `TOOL-LOCAL/Web/src/types.ts`: có cả `DashboardFeatures`, license/payment DTO và `ProjectContentSummary`.
- `TOOL-LOCAL/WebView/WebMessageContracts.cs`: dashboard response có cả license và feature flags; request thanh toán còn đầy đủ.
- `TOOL-LOCAL/Web/src/styles.css`: giữ cả selector `.vietsub-*`, `.license-*` và các sửa UI video dài; kiểm tra các selector shell dùng chung.
- `TOOL-LOCAL/Projects/ProjectService.cs` và `ProjectContracts.cs`: giữ logic content dự án cũ.
- `TOOL-SERVER/Program.cs`: giữ đăng ký Vietsub, Fal/Veo, SePay và organization provisioning; không đăng ký trùng hoặc thiếu lifetime.
- `TOOL-TESTS/TOOL-TESTS.csproj`: không làm mất test suite của một trong hai nhánh.
- Các migration được giữ theo thứ tự vận hành `4.0.9` → `4.0.10` → `4.0.11` → `4.1.0`; không chạy chúng trong task merge.

## 7. Trình tự triển khai

- [x] Chụp lại `git status`, danh sách stage 1/2/3 của 5 file conflict và giữ baseline trong phần mô tả merge.
- [x] Resolve `Program.cs` và `Form1.cs` trước để chốt dependency composition.
- [x] Resolve `DashboardBridge.cs` theo chữ ký constructor vừa chốt.
- [x] Resolve `Web/index.html` bằng CSP hợp nguồn tối thiểu.
- [x] Resolve `App.tsx` và loại bỏ tham chiếu legacy `shortVideoProjectId`.
- [x] Rà soát các file auto-merge nêu tại mục 6.
- [x] Tìm toàn repository để bảo đảm không còn conflict marker.
- [x] Build frontend TypeScript.
- [x] Restore, build Release và chạy toàn bộ test solution.
- [x] Chạy smoke test tự động cho content dự án dài, routing, license/SePay, Vietsub và media mà không phát sinh chi phí.
- [x] Kiểm tra `git diff`, `git diff --cached` và `git status` lần cuối trước khi stage resolution.
- [x] Stage 5 file đã resolve cùng tài liệu task và tạo merge commit khi toàn bộ gate đạt.

## 8. Lệnh kiểm tra dự kiến

### Conflict marker và dấu vết legacy

```powershell
rg -n "^(<<<<<<<|=======|>>>>>>>)" .
rg -n "shortVideoProjectId|setShortVideoProjectId" TOOL-LOCAL\Web\src\App.tsx
```

Kết quả mong đợi: không còn conflict marker và không còn hai định danh legacy.

### Kiểm tra frontend

```powershell
Set-Location TOOL-LOCAL\Web
npm ci --no-audit --no-fund
npm run build
```

### Kiểm tra toàn solution

```powershell
Set-Location ..\..
dotnet restore TOOL_GEN_POST_VIDEO.slnx
dotnet build TOOL_GEN_POST_VIDEO.slnx -c Release --no-restore
dotnet test TOOL-TESTS\TOOL-TESTS.csproj -c Release --no-build
```

Không ghi nhận mốc test mới nếu chưa thực sự chạy và đạt.

### Kiểm tra bất biến gateway-only của desktop

```powershell
rg -n -i "openAiApiKey|klingApiKey|providers\.settings\.save|DirectGenerationClient|LocalProviderRuntimeResolver|ProviderSecretStore" TOOL-LOCAL
```

## 9. Smoke test không phát sinh chi phí

- Đăng nhập bằng tài khoản có license hợp lệ và mở dashboard.
- Kiểm tra tài khoản thiếu/hết hạn license hiển thị license gate; chỉ dùng API/môi trường test, không thanh toán thật.
- Bật/tắt feature flag Vietsub và kiểm tra menu/route tương ứng.
- Mở một Vietsub project, tạo thay đổi chưa lưu rồi chuyển organization; dữ liệu phải flush/đóng an toàn trước khi chuyển.
- Kiểm tra phát media Vietsub qua `vietsub-media.app.local`.
- Mở dự án video ngắn cũ; giao diện phải chuyển sang trang video ngắn.
- Mở dự án video dài cũ có script nhưng chưa có scene; Bước 2 phải hiển thị content/kịch bản đã lưu.
- Mở dự án video dài có scene; storyboard và các track/scene hiện đúng.
- Kiểm tra FFmpeg/FFprobe preflight cục bộ.
- Kiểm tra QR test có thể hiển thị qua CSP mà không mở thêm host ngoài allowlist.
- Không bấm tạo content, ảnh nhân vật hoặc video thật trong smoke test merge.

## 10. Điều kiện nghiệm thu

- Không còn file trạng thái `UU` và không còn conflict marker.
- Frontend TypeScript build thành công.
- Release build không warning/error.
- Toàn bộ test đạt, bao gồm test Vietsub, license/SePay, organization provisioning, Fal/Veo và project content.
- `Form1` khởi động được với cả Vietsub bật và tắt.
- License gate hoạt động nhưng không làm mất feature flag hoặc phá dashboard state.
- Chuyển organization không để lại Vietsub session của organization cũ.
- Chọn project không bị response refresh cũ đổi sai trang.
- Dự án dài cũ hiển thị content đã lưu ngay cả khi chưa có scene.
- CSP chỉ chứa các host đã được phê duyệt.
- Desktop không chứa provider key hoặc đường gọi provider trực tiếp.
- Không có migration, request AI có chi phí, thanh toán thật hoặc publish release được thực hiện trong task này.

## 11. Phương án dừng và phục hồi

- Nếu merge chưa được resolve và cần hủy toàn bộ lần merge, chỉ cân nhắc `git merge --abort` sau khi xác nhận không có thay đổi làm việc ngoài merge cần giữ.
- Không dùng `git reset --hard` hoặc `git checkout --` để xử lý từng file.
- Nếu build/test phát hiện lỗi lớn ngoài phạm vi merge, dừng trước merge commit, ghi rõ lỗi và xin quyết định mở rộng phạm vi.
- Không sửa lịch sử migration hoặc chạy SQL để làm cho test source vượt qua.

## 12. Kết quả xác minh triển khai

- `npm ci --no-audit --no-fund`: thành công.
- `npm run build`: thành công; TypeScript và Vite build hoàn tất.
- `npm test`: 7/7 test đạt.
- `dotnet restore TOOL_GEN_POST_VIDEO.slnx`: thành công.
- Release build lần đầu phát hiện nhánh dashboard dành cho license bị khóa còn thiếu `DashboardFeatureFlagsResponse`; đã bổ sung feature flags Vietsub vào đúng response.
- Toàn bộ test lần đầu phát hiện thứ tự host trong CSP chưa thỏa contract SePay; đã đổi thứ tự nhưng không thêm host ngoài allowlist.
- Release build cuối: thành công, 0 warning và 0 error.
- Smoke test tự động mục tiêu: 49/49 test đạt.
- Toàn bộ test .NET cuối: 596/596 test đạt, không có test bị skip.
- `git diff --cached --check` chỉ còn cảnh báo Markdown đã tồn tại nguyên trạng trong các commit incoming của `main`; 5 file resolution và tài liệu task không có whitespace error.
- Không chạy migration, không gọi OpenAI/Kling/Fal/Veo, không thực hiện thanh toán và không publish release.
- Chưa chạy smoke test UI tương tác với server/staging thật vì task không cung cấp môi trường và quyền tác động tương ứng; các đường giao nhau của merge đã được kiểm tra bằng build và test tự động.
