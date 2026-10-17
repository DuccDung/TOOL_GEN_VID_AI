namespace TOOL_SETUP;

internal static class SetupProgressStages
{
    internal static string DisplayName(string stage) => stage switch
    {
        "preflight" => "Kiểm tra trước cài đặt",
        "download" => "Tải ứng dụng",
        "extract" => "Chuẩn bị ứng dụng",
        "prerequisite" => "Kiểm tra prerequisite",
        "models" => "Tải và kiểm tra model",
        "install" => "Cài ứng dụng",
        "probe" => "Kiểm tra runtime/model",
        "commit" => "Hoàn tất giao dịch cài đặt",
        "complete" => "Cài đặt hoàn tất",
        _ => "Đang xử lý"
    };
}
