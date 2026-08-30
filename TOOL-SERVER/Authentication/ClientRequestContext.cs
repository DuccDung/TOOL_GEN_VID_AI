namespace TOOL_SERVER.Authentication;

public sealed record ClientRequestContext(
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId);
