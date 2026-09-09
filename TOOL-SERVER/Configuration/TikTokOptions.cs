using System.ComponentModel.DataAnnotations;

namespace TOOL_SERVER.Configuration;

public sealed class TikTokOptions
{
    public const string SectionName = "TikTok";

    public bool Enabled { get; init; }

    public bool AdminManagedCredentialsEnabled { get; init; } = true;

    public bool EmergencyDisabled { get; init; }

    public bool AuditedForPublicPosting { get; init; }

    [MaxLength(200)]
    public string ClientKey { get; init; } = string.Empty;

    [MaxLength(500)]
    public string ClientSecret { get; init; } = string.Empty;

    public string[] Scopes { get; init; } = ["video.publish"];

    [Range(5, 30)]
    public int OAuthSessionMinutes { get; init; } = 10;

    [Range(1, 168)]
    public int MaximumJobAgeHours { get; init; } = 72;

    public static bool IsValidOrDisabled(TikTokOptions options) =>
        options.Scopes.Contains("video.publish", StringComparer.Ordinal) &&
        (!options.Enabled ||
         (!string.IsNullOrWhiteSpace(options.ClientKey) &&
          !string.IsNullOrWhiteSpace(options.ClientSecret)));
}
