using System.Net;
using System.Text.Json;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.SystemSetup;
using TOOL_SHARED.Contracts.Updates;

namespace TOOL_LOCAL.Updates;

internal static class DesktopRepairErrors
{
    internal static async Task<AccountClientException> FromResponseAsync(HttpResponseMessage response, CancellationToken token)
    {
        var code = DesktopRepairErrorCodes.Unavailable;
        if (response.StatusCode == HttpStatusCode.Forbidden) code = DesktopRepairErrorCodes.AccessDenied;
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Old servers and reverse proxies also return 404. Only the explicit code proves
            // that the repair endpoint was reached and found no matching release.
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(token);
                var bytes = new byte[4097];
                var count = 0;
                while (count < bytes.Length)
                {
                    var read = await stream.ReadAsync(bytes.AsMemory(count), token);
                    if (read == 0) break;
                    count += read;
                }
                if (count <= 4096)
                {
                    using var json = JsonDocument.Parse(bytes.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 8 });
                    if (json.RootElement.ValueKind == JsonValueKind.Object
                        && json.RootElement.TryGetProperty("code", out var value)
                        && value.ValueKind == JsonValueKind.String
                        && value.GetString() == DesktopRepairErrorCodes.PackageNotFound)
                        code = DesktopRepairErrorCodes.PackageNotFound;
                }
            }
            catch (Exception e) when (e is JsonException or IOException or HttpRequestException) { }
        }
        return new AccountClientException(code, Message(code), (int)response.StatusCode);
    }

    internal static DesktopRepairFailure FromException(Exception exception)
    {
        var code = exception switch
        {
            AccountClientException e when e.Code is DesktopRepairErrorCodes.PackageNotFound
                or DesktopRepairErrorCodes.Unavailable or DesktopRepairErrorCodes.AccessDenied => e.Code,
            AccountClientException { StatusCode: 401 } => "session_expired",
            HttpRequestException or OperationCanceledException => DesktopRepairErrorCodes.NetworkFailed,
            SetupException { Code: "system_setup_busy" } => "system_setup_busy",
            SetupException { Code: "system_setup_storage_denied" } => "system_setup_storage_denied",
            UnauthorizedAccessException => "system_setup_storage_denied",
            _ => DesktopRepairErrorCodes.Failed
        };
        return new(code, Message(code), DesktopBuildInfo.Version, DesktopBuildInfo.BuildNumber);
    }

    private static string Message(string code) => code switch
    {
        DesktopRepairErrorCodes.PackageNotFound =>
            $"Server chưa có gói sửa cho bản {DesktopBuildInfo.Version} (build {DesktopBuildInfo.BuildNumber}). " +
            "Hãy lấy bản ZIP đầy đủ từ người cung cấp, thoát ứng dụng rồi giải nén vào thư mục mới. Giữ nguyên workspace và dữ liệu cũ.",
        DesktopRepairErrorCodes.Unavailable =>
            $"Chưa lấy được gói sửa cho bản {DesktopBuildInfo.Version} (build {DesktopBuildInfo.BuildNumber}). " +
            "Cần kiểm tra dịch vụ sửa chữa và gói phát hành trên server. Bạn có thể lấy lại bản ZIP đầy đủ từ người cung cấp.",
        DesktopRepairErrorCodes.AccessDenied => "Tài khoản chưa được phép tải gói sửa. Hãy liên hệ quản trị viên.",
        DesktopRepairErrorCodes.NetworkFailed => "Không kết nối được dịch vụ sửa chữa hoặc yêu cầu đã hết thời gian. Kiểm tra mạng rồi thử lại.",
        "session_expired" => AccountSessionManager.SessionExpiredMessage,
        "system_setup_busy" => "Một tác vụ đang dùng hoặc cài thành phần. Hãy chờ tác vụ kết thúc rồi sửa ứng dụng.",
        "system_setup_storage_denied" => "Không có quyền ghi thư mục sửa chữa. Hãy kiểm tra quyền truy cập rồi thử lại.",
        _ => "Chưa thể sửa ứng dụng. Kiểm tra dung lượng đĩa hoặc lấy lại bản ZIP đầy đủ từ người cung cấp. Dữ liệu dự án cần được giữ nguyên."
    };
}
