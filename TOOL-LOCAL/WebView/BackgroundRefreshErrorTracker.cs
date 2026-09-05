namespace TOOL_LOCAL.WebView;

internal sealed class BackgroundRefreshErrorTracker
{
    private string? _lastSignature;

    public WebMessageResponse? TryCreateResponse(string code, string message)
    {
        var signature = $"{code}\n{message}";
        if (string.Equals(_lastSignature, signature, StringComparison.Ordinal))
        {
            return null;
        }

        _lastSignature = signature;
        return new WebMessageResponse(
            "operation.error",
            null,
            Error: new WebMessageError(code, message));
    }

    public void MarkSuccessful() => _lastSignature = null;
}
