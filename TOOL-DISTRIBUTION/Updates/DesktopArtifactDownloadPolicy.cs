using TOOL_SHARED.Contracts.Updates;

namespace TOOL_SHARED.Distribution.Updates;

/// <summary>Executable file sources, independent from the authenticated API and AI providers.</summary>
public static class DesktopArtifactDownloadPolicy
{
    public const string ApplicationPrefix = "https://103.179.190.182:8443/apps/videomaker/";
    public const string ModelHostPackagePrefix = "https://103.179.190.182:8443/models/";
    public const int PolicyVersion = 1;
    public const long MaximumAppArchiveBytes = 2L * 1024 * 1024 * 1024;
    public const long MaximumExpandedBytes = 4L * 1024 * 1024 * 1024;
    public const int MaximumEntryCount = 200_000;
    public const long MaximumSetupArtifactBytes = 512L * 1024 * 1024;
    public const int MaximumArtifactFileNameLength = 180;

    public static IReadOnlyList<string> ApprovedExternalPrefixes { get; } =
        Array.AsReadOnly(new[] { ApplicationPrefix, ModelHostPackagePrefix });

    public static Uri Validate(Uri apiOrigin, string downloadUrl, string sourceKind,
        IReadOnlyList<string>? approvedPrefixes = null)
    {
        if (!apiOrigin.IsAbsoluteUri || apiOrigin.Scheme != Uri.UriSchemeHttps ||
            apiOrigin.UserInfo.Length != 0 || apiOrigin.Query.Length != 0 || apiOrigin.Fragment.Length != 0)
            throw new InvalidDataException("Artifact API origin must be an HTTPS address.");
        AssertUnambiguous(downloadUrl);
        if (!Uri.TryCreate(apiOrigin, downloadUrl, out var resource) || resource.Scheme != Uri.UriSchemeHttps ||
            resource.UserInfo.Length != 0 || resource.Query.Length != 0 || resource.Fragment.Length != 0)
            throw new InvalidDataException("Artifact download URL is not approved.");
        if (sourceKind == DesktopArtifactSourceKinds.Internal)
        {
            if (!SameOrigin(apiOrigin, resource)) throw new InvalidDataException("Internal artifact is outside the API origin.");
        }
        else if (sourceKind == DesktopArtifactSourceKinds.ExternalVps)
        {
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out _) ||
                !(approvedPrefixes ?? ApprovedExternalPrefixes).Any(prefix => MatchesPrefix(resource, prefix)))
                throw new InvalidDataException("External artifact is outside approved file sources.");
        }
        else throw new InvalidDataException("Unknown desktop artifact source.");
        return resource;
    }

    public static bool MatchesPrefix(Uri resource, string prefix)
    {
        AssertUnambiguous(prefix);
        if (!Uri.TryCreate(prefix, UriKind.Absolute, out var root) || root.Scheme != Uri.UriSchemeHttps ||
            root.UserInfo.Length != 0 || root.Query.Length != 0 || root.Fragment.Length != 0 ||
            !root.AbsolutePath.EndsWith('/') || !SameOrigin(root, resource)) return false;
        return resource.AbsolutePath.StartsWith(root.AbsolutePath, StringComparison.Ordinal) &&
            resource.AbsolutePath.Length > root.AbsolutePath.Length;
    }

    public static bool SameOrigin(Uri left, Uri right) => left.Scheme == right.Scheme &&
        left.IdnHost.Equals(right.IdnHost, StringComparison.OrdinalIgnoreCase) && left.Port == right.Port;

    private static void AssertUnambiguous(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2000 || value != value.Trim() ||
            value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\') ||
            value.Contains('%') || value.Contains('#') || value.Contains('?') ||
            value.Split('/').Any(segment => segment is "." or ".."))
            throw new InvalidDataException("Artifact URL contains unsupported characters or path segments.");
    }
}
