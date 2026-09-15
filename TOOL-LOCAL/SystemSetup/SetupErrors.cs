using TOOL_LOCAL.Vietsub.Translation;
using TOOL_LOCAL.Vietsub.Voice;

namespace TOOL_LOCAL.SystemSetup;

internal static class SetupErrors
{
    public static string Code(Exception exception) => exception switch
    {
        SetupException e => e.Code,
        VietsubTranslationException e => e.Code,
        VietsubVoiceException e => e.Code,
        UnauthorizedAccessException => "system_setup_storage_denied",
        HttpRequestException => "system_setup_network_failed",
        IOException => "system_setup_storage_failed",
        _ => "system_setup_failed"
    };
    public static string Message(string code) => code switch
    {
        "TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED" => "RAM hoặc bộ nhớ khả dụng thấp. Đóng ứng dụng nặng và thử lại; chỉ tiếp tục sau khi xác nhận cảnh báo.",
        "system_setup_network_failed" => "Không tải được thành phần. Kiểm tra kết nối mạng rồi thử lại.",
        "system_setup_storage_denied" => "Không có quyền ghi thư mục cài đặt.",
        "system_setup_storage_failed" => "Không thể ghi dữ liệu. Kiểm tra dung lượng và quyền truy cập ổ đĩa.",
        _ => "Thành phần chưa vượt qua kiểm tra. Hãy thử lại hoặc sửa bộ ứng dụng nếu thiếu runtime đi kèm."
    };
}
