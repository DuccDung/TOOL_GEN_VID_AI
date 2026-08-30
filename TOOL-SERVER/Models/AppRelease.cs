using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class AppRelease
{
    public Guid AppReleaseId { get; set; }

    public string Version { get; set; } = null!;

    public int BuildNumber { get; set; }

    public string Channel { get; set; } = null!;

    public string Platform { get; set; } = null!;

    public string? MinimumSupportedDesktopVersion { get; set; }

    public string? DownloadUrl { get; set; }

    public string? Sha256 { get; set; }

    public string? ReleaseNotes { get; set; }

    public bool IsMandatory { get; set; }

    public bool IsActive { get; set; }

    public DateTime PublishedAtUtc { get; set; }

    public virtual ICollection<AppReleaseArtifact> Artifacts { get; set; } = new List<AppReleaseArtifact>();
}
