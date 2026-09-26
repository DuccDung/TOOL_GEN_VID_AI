using TOOL_LOCAL.Vietsub.Translation;
using TOOL_LOCAL.Vietsub.Voice;
using TOOL_LOCAL.Vietsub.Ocr;

namespace TOOL_LOCAL.SystemSetup;

internal static class SetupErrors
{
    public static string Code(Exception exception) => exception switch
    {
        SetupException e => e.Code,
        VietsubTranslationException e => e.Code,
        VietsubVoiceException e => e.Code,
        VietsubOcrException e => e.Code,
        UnauthorizedAccessException => "system_setup_storage_denied",
        HttpRequestException => "system_setup_network_failed",
        IOException => "system_setup_storage_failed",
        _ => "system_setup_failed"
    };
    public static string Message(string code) => code switch
    {
        "system_setup_ocr_fixture_invalid" or "system_setup_ocr_probe_failed" => VietsubOcrRuntimeDiagnostics.Message(code),
        VietsubVoiceErrorCodes.BundleMissing => "Bộ ứng dụng thiếu gói giọng Việt offline. Hãy dùng bản ZIP đầy đủ đúng phiên bản.",
        VietsubVoiceErrorCodes.BundleInvalid => "Gói giọng Việt offline bị hỏng hoặc không khớp phiên bản. Hãy sửa bộ ứng dụng hoặc giải nén lại bản ZIP đầy đủ.",
        VietsubVoiceErrorCodes.RuntimeBusy => "Giọng Việt đang được chuẩn bị ở cửa sổ khác. Hãy đợi rồi thử lại.",
        VietsubVoiceErrorCodes.RuntimeUnsupported => "Giọng Việt offline yêu cầu Windows x64.",
        VietsubVoiceErrorCodes.RuntimeInstallFailed => "Chưa chuẩn bị được giọng Việt. Kiểm tra dung lượng, quyền ghi và bản ZIP đầy đủ rồi thử lại.",
        VietsubVoiceErrorCodes.RuntimeInvalid => "Runtime giọng Việt chưa hợp lệ. Chọn Cài giọng Việt để kiểm tra và sửa.",
        "TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED" => "RAM hoặc bộ nhớ khả dụng thấp. Đóng ứng dụng nặng và thử lại; chỉ tiếp tục sau khi xác nhận cảnh báo.",
        "system_setup_network_failed" => "Không tải được thành phần. Kiểm tra kết nối mạng rồi thử lại.",
        "system_setup_storage_denied" => "Không có quyền ghi thư mục cài đặt.",
        "system_setup_storage_failed" => "Không thể ghi dữ liệu. Kiểm tra dung lượng và quyền truy cập ổ đĩa.",
        _ => "Thành phần chưa vượt qua kiểm tra. Hãy thử lại hoặc sửa bộ ứng dụng nếu thiếu runtime đi kèm."
    };
}
