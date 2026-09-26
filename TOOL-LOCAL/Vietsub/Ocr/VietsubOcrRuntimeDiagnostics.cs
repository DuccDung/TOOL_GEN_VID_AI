namespace TOOL_LOCAL.Vietsub.Ocr;

internal static class VietsubOcrRuntimeDiagnostics
{
    internal const string NativeLoadFailed = "OCR_NATIVE_LOAD_FAILED";
    internal const string BinaryInvalid = "OCR_BINARY_INVALID";
    internal const string ComponentMissing = "OCR_COMPONENT_MISSING";
    internal const string ComponentLoadFailed = "OCR_COMPONENT_LOAD_FAILED";
    internal const string PlatformUnsupported = "OCR_PLATFORM_UNSUPPORTED";

    internal static string Classify(Exception exception)
    {
        // TypeInitializationException often wraps the actual loader failure. Do not parse or
        // return exception text: it can contain private paths, configuration and native stderr.
        var code = VietsubOcrErrorCodes.RuntimeInvalid;
        for (var depth = 0; exception is not null && depth < 12; depth++, exception = exception.InnerException!)
        {
            switch (exception)
            {
                case BadImageFormatException: return BinaryInvalid;
                case DllNotFoundException: return NativeLoadFailed;
                case FileNotFoundException: code = ComponentMissing; break;
                case FileLoadException when code == VietsubOcrErrorCodes.RuntimeInvalid: code = ComponentLoadFailed; break;
            }
        }
        return code;
    }

    internal static string Message(string? code) => code switch
    {
        PlatformUnsupported => "PaddleOCR cần Windows 64-bit và ứng dụng x64.",
        OcrNativeDependencies.Missing => "Bộ ứng dụng thiếu DLL Visual C++ x64 cho OCR. Hãy lấy ZIP đầy đủ mới nhất và giải nén toàn bộ vào thư mục mới.",
        OcrNativeDependencies.Invalid => "DLL Visual C++ đi kèm OCR bị thay đổi hoặc không đọc được. Hãy giải nén lại ZIP đầy đủ mới nhất vào thư mục mới.",
        OcrNativeDependencies.MediaMissing => "Windows thiếu hoặc không nạp được Media Foundation. Hãy bật Media Feature Pack cho Windows N hoặc Media Foundation cho Windows Server, rồi mở lại ứng dụng.",
        NativeLoadFailed => "Không nạp được thư viện OCR hoặc dependency của thư viện. Hãy kiểm tra bộ ZIP đầy đủ và thành phần hệ thống trên máy này.",
        BinaryInvalid => "Một thư viện OCR không đúng định dạng hoặc kiến trúc. Hãy lấy lại bộ ứng dụng Windows x64 đầy đủ.",
        ComponentMissing or VietsubOcrErrorCodes.RuntimeNotInstalled => "Thiếu thành phần OCR trong bộ ứng dụng. Hãy giải nén lại toàn bộ ZIP hoặc sửa ứng dụng.",
        ComponentLoadFailed => "Có thành phần OCR nhưng Windows không nạp được. Hãy kiểm tra bộ ứng dụng và quyền truy cập trên máy này.",
        "system_setup_ocr_fixture_invalid" => "Thiếu hoặc hỏng ảnh kiểm tra OCR trong bộ ứng dụng. Hãy giải nén lại toàn bộ ZIP.",
        "system_setup_ocr_probe_failed" => "OCR đã nạp nhưng chưa nhận dạng đạt ảnh kiểm tra Anh/Trung. Hãy kiểm tra lại hoặc sửa bộ ứng dụng.",
        _ => "Chưa khởi tạo được OCR trên máy này. Hãy kiểm tra lại; nếu vẫn lỗi, gửi mã lỗi cùng phiên bản ứng dụng cho người cung cấp."
    };
}
