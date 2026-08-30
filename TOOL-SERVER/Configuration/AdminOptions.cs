namespace TOOL_SERVER.Configuration;

public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    public string? BootstrapEmail { get; init; }
}

