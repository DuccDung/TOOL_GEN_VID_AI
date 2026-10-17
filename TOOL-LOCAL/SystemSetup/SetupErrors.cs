using TOOL_LOCAL.Vietsub.Translation;
using TOOL_LOCAL.Vietsub.Voice;
using TOOL_LOCAL.Vietsub.Ocr;

namespace TOOL_LOCAL.SystemSetup;

internal static class SetupErrors
{
    public static string Code(Exception exception) => exception switch
    {
        SetupException e => e.Code,
        TOOL_SHARED.Distribution.ModelDistribution.ModelDistributionException e => e.Code,
        VietsubTranslationException e => e.Code,
        VietsubVoiceException e => e.Code,
        VietsubOcrException e => e.Code,
        UnauthorizedAccessException => "system_setup_storage_denied",
        HttpRequestException => "system_setup_network_failed",
        IOException => "system_setup_storage_failed",
        _ => "system_setup_failed"
    };
    public static string Message(Exception error)
    {
        var message = Message(Code(error));
        return error is VietsubTranslationException { DiagnosticId: { } id } && Guid.TryParseExact(id, "N", out _)
            ? message + " Mã chẩn đoán: " + id + "." : message;
    }

    public static string Message(string code) => code switch
    {
        "model_distribution_disabled" => "Kênh tải model đang tắt. Hãy dùng bộ offline hợp lệ hoặc cấu hình đúng bản phát hành.",
        "model_distribution_busy" => "Thành phần đang được chuẩn bị ở cửa sổ khác. Hãy đợi rồi thử lại.",
        "model_distribution_insufficient_disk" => "Không đủ dung lượng trống để tải và chuẩn bị model.",
        "model_distribution_trust_not_configured" => "Bản ứng dụng chưa có khóa xác thực kho model. Hãy dùng bản phát hành đã cấu hình đúng.",
        "model_distribution_signature_invalid" or "model_distribution_catalog_invalid" or "model_distribution_catalog_rollback"
            => "Danh mục model chưa xác thực được. Ứng dụng đã dừng tải để bảo vệ bộ đang dùng.",
        "model_distribution_catalog_expired" => "Danh mục model đã hết hiệu lực. Hãy kiểm tra lại hoặc liên hệ quản trị viên phát hành.",
        "model_distribution_artifact_not_approved" => "Phiên bản model không được phép sử dụng hoặc đã bị thu hồi.",
        "model_distribution_artifact_invalid" => "File model tải về sai kích thước hoặc checksum; chưa được đưa vào sử dụng.",
        "model_distribution_artifact_not_found" => "Kho model chưa cung cấp thành phần tương thích với ứng dụng.",
        "model_distribution_incompatible" => "Model yêu cầu bản ứng dụng hoặc worker khác. Hãy cập nhật ứng dụng.",
        "model_distribution_download_failed" => "Chưa tải xong model. Kiểm tra mạng, dung lượng và thử lại để tiếp tục.",
        "system_setup_ocr_fixture_invalid" or "system_setup_ocr_probe_failed" => VietsubOcrRuntimeDiagnostics.Message(code),
        VietsubVoiceErrorCodes.BundleMissing => "Bộ ứng dụng thiếu gói giọng Việt offline. Hãy dùng bản ZIP đầy đủ đúng phiên bản.",
        VietsubVoiceErrorCodes.BundleInvalid => "Gói giọng Việt offline bị hỏng hoặc không khớp phiên bản. Hãy sửa bộ ứng dụng hoặc giải nén lại bản ZIP đầy đủ.",
        VietsubVoiceErrorCodes.RuntimeBusy => "Giọng Việt đang được chuẩn bị ở cửa sổ khác. Hãy đợi rồi thử lại.",
        VietsubVoiceErrorCodes.RuntimeUnsupported => "Giọng Việt offline yêu cầu Windows x64.",
        VietsubVoiceErrorCodes.RuntimeInstallFailed => "Chưa chuẩn bị được giọng Việt. Kiểm tra dung lượng, quyền ghi và bản ZIP đầy đủ rồi thử lại.",
        VietsubVoiceErrorCodes.RuntimeInvalid => "Runtime giọng Việt chưa hợp lệ. Chọn Cài giọng Việt để kiểm tra và sửa.",
        "TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED" => "RAM hoặc bộ nhớ khả dụng thấp. Đóng ứng dụng nặng và thử lại; chỉ tiếp tục sau khi xác nhận cảnh báo.",
        VietsubTranslationErrorCodes.ProcessCrashed => "Tiến trình Qwen đã dừng bất thường. Hãy kiểm tra lại; nếu lỗi lặp lại, gửi mã chẩn đoán cho hỗ trợ.",
        VietsubTranslationErrorCodes.ProcessTimeout => "Qwen không phản hồi trong thời gian cho phép. Hãy kiểm tra tài nguyên máy rồi thử lại.",
        VietsubTranslationErrorCodes.RuntimeOutOfMemory => "Qwen đã hết bộ nhớ khi xử lý. Hãy đóng ứng dụng nặng, kiểm tra bộ nhớ khả dụng rồi thử lại.",
        VietsubTranslationErrorCodes.BackendLoadFailed => "Không nạp được runtime Qwen đi kèm. Hãy dùng bộ ứng dụng đầy đủ đúng phiên bản hoặc sửa bộ ứng dụng.",
        VietsubTranslationErrorCodes.WorkerProtocolInvalid => "Worker Qwen không khớp giao thức của ứng dụng. Hãy dùng bộ ứng dụng đầy đủ cùng phiên bản.",
        VietsubTranslationErrorCodes.RuntimeProbeFailed or VietsubTranslationErrorCodes.EnglishProbeFailed
            or VietsubTranslationErrorCodes.ChineseProbeFailed => "Qwen chưa vượt qua bài dịch thử trên máy này. Hãy kiểm tra lại và gửi mã lỗi cho hỗ trợ nếu lỗi lặp lại.",
        VietsubTranslationErrorCodes.RuntimeInsufficientDisk => "Không đủ dung lượng trống để chuẩn bị Qwen. Hãy giải phóng dung lượng trên ổ chứa thành phần rồi thử lại.",
        "system_setup_network_failed" => "Không tải được thành phần. Kiểm tra kết nối mạng rồi thử lại.",
        "system_setup_storage_denied" => "Không có quyền ghi thư mục cài đặt.",
        "system_setup_storage_failed" => "Không thể ghi dữ liệu. Kiểm tra dung lượng và quyền truy cập ổ đĩa.",
        _ => "Thành phần chưa vượt qua kiểm tra. Hãy thử lại hoặc sửa bộ ứng dụng nếu thiếu runtime đi kèm."
    };
}
