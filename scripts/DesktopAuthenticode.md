# Ký executable trong pipeline desktop

`Publish-DesktopRelease.ps1` hỗ trợ Authenticode theo lựa chọn tường minh. Mặc định `-AuthenticodeMode None` giữ luồng build hiện có; report ghi `Signed=false`, `ProductionQualified=false`. Một build không ký không phải bằng chứng nghiệm thu Authenticode ST16.

Để yêu cầu ký, người vận hành cung cấp certificate code signing đã được cấp trong `CurrentUser\My` hoặc `LocalMachine\My`, có private key, đúng thumbprint và còn hạn. Module không tạo/import/export certificate hoặc key, không nhận PFX/password và không sao chép private key vào source/package. Certificate trong Windows store có thể trỏ tới key provider hoặc thiết bị ký do người vận hành cấu hình.

Các tham số bổ sung:

| Tham số | Ý nghĩa |
|---|---|
| `AuthenticodeMode` | `None` mặc định hoặc `WindowsStore`. |
| `SigningCertificateThumbprint` | Thumbprint SHA-1 40 hex chọn **đúng certificate**; thuật toán ký file vẫn là SHA-256. |
| `SigningCertificateStoreLocation` | `CurrentUser` mặc định hoặc `LocalMachine`, store `My`. |
| `SignToolPath` | Đường tuyệt đối tới `signtool.exe` của Windows SDK đã được kiểm trên máy ký. |
| `SigningTimestampUrl` | Endpoint RFC 3161 HTTPS/443 do người vận hành chỉ định, không credential/query/fragment. Không có timestamp server mặc định. |
| `RequireAuthenticode` | Bắt buộc chế độ ký được cấu hình; từ chối `None` trước publish. Dùng khi nghiệm thu ST16/phát hành yêu cầu ký. |

Ví dụ chỉ bổ sung vào lệnh publish đã có profile hợp lệ:

```powershell
# Placeholder: chỉ thực thi sau khi certificate, timestamp endpoint và môi trường được chỉ định.
.\scripts\Publish-DesktopRelease.ps1 <các-tham-số-profile-đã-duyệt> `
  -AuthenticodeMode WindowsStore `
  -SigningCertificateThumbprint '<40-hex-of-approved-code-signing-certificate>' `
  -SigningCertificateStoreLocation CurrentUser `
  -SignToolPath '<absolute-Windows-SDK-path>\signtool.exe' `
  -SigningTimestampUrl 'https://<approved-rfc3161-timestamp-host>/' `
  -RequireAuthenticode
```

Không chạy nguyên ví dụ có placeholder. Thao tác ký thật cần quyền truy cập key và có thể gọi timestamp endpoint; tests của dự án không thực hiện các thao tác đó. Giữ ký catalog model ECDSA, TLS của server và Authenticode thành các vai trò riêng.

Thứ tự pipeline:

1. Publish desktop xong, ký đúng ba executable do dự án sở hữu: `TOOL-LOCAL.exe`, `VideoMaker.Updater.exe`, `_translation_worker/VideoMaker.Vietsub.TranslationWorker.exe`.
2. Chạy probe trên byte đã ký; copy sang package, xác minh lại trước tạo inventory/ZIP/hash. Sau giải nén ZIP kiểm inventory và chữ ký một lần nữa.
3. Publish `VideoMaker Setup.exe`, ký, copy sang thư mục giao nhận, xác minh lại rồi mới tính hash/size Setup.
4. Ghi `authenticode-report.json` cùng SHA-256 ZIP/Setup trong `checksums.sha256`. Report không tự nâng `ProductionQualified`; license, runtime, hosting và nghiệm thu vẫn có gate riêng.

Module chỉ ký allowlist trên; không ký lại FFmpeg, Python, uv, native DLL hay executable bên thứ ba. Chữ ký có sẵn khác signer, signature hỏng hoặc thiếu timestamp làm luồng ký bị từ chối; không ghi đè để che lỗi. Luồng `WindowsStore` dùng SignTool `/fd SHA256 /tr <endpoint> /td SHA256`, xác minh `/pa /all /tw`, đồng thời kiểm Windows Authenticode status `Valid`, certificate thumbprint đúng và có timestamp. [Microsoft SignTool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool), [khuyến nghị timestamp](https://learn.microsoft.com/en-us/windows/win32/seccrypto/time-stamping-authenticode-signatures).

Không ký/sửa executable sau khi inventory/ZIP/hash đã chốt. Nếu cần thay chữ ký hoặc timestamp, tạo candidate mới bằng pipeline; không cập nhật checksum thủ công để che package cũ. Không sửa gate `RequireReleaseApproval` của FFmpeg hoặc chuyển mode model từ Bundled/Server để vượt điều kiện phát hành.

Kiểm thử tự động dùng certificate metadata và native command giả, kiểm thuật toán/thứ tự tham số, exact signer/timestamp, failure trước signing, không ký file bên thứ ba, hash sau ký và thứ tự seal package. Các bài này không thay việc ký và xác minh certificate thật trên máy ký hoặc máy khách sạch.
