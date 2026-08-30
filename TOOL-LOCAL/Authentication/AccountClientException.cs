namespace TOOL_LOCAL.Authentication;

public sealed class AccountClientException(
    string code,
    string message,
    int statusCode,
    IReadOnlyDictionary<string, string[]>? errors = null,
    string? traceId = null) : Exception(message)
{
    public string Code { get; } = code;

    public int StatusCode { get; } = statusCode;

    public IReadOnlyDictionary<string, string[]> Errors { get; } =
        errors ?? new Dictionary<string, string[]>();

    public string? TraceId { get; } = traceId;
}
