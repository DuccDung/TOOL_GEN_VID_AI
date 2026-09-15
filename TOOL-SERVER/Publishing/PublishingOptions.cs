namespace TOOL_SERVER.Publishing;

internal sealed class PublishingOptions
{
    public const string SectionName = "Publishing";
    public bool Enabled { get; set; }
    public bool WorkerEnabled { get; set; }
    public bool EmergencyDisabled { get; set; }
    public string? MediaRoot { get; set; }
    public string? FfprobePath { get; set; }
    public string? FfprobeSha256 { get; set; }
    public PublishingOAuthOptions YouTube { get; set; } = new();
    public PublishingOAuthOptions Facebook { get; set; } = new();
}

internal sealed class PublishingOAuthOptions
{
    public bool Enabled { get; set; }
    // Configure only through the server's secret/configuration store.
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? RedirectUri { get; set; }
    public string? ApiVersion { get; set; }
    public bool PublicPostingApproved { get; set; }
}
