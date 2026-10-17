using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Configuration;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_SERVER.Updates;

internal static class DesktopSharedReleaseValidation
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static AccountApiException Invalid(string code, string message) => new(400, code, message);
    internal static AccountApiException Conflict(string code, string message) => new(409, code, message);
    internal static AccountApiException Missing() => new(404, "desktop_preparation_not_found", "Không tìm thấy nháp phát hành.");

    internal static bool IsConfigured(DesktopReleaseOptions options) =>
        !string.IsNullOrWhiteSpace(options.Profile) && options.Profile.Length <= 50 &&
        options.AllowedExternalPrefixes is { Length: > 0 } &&
        Uri.TryCreate(options.ApiBaseUrl, UriKind.Absolute, out var api) && api.Scheme == "https" &&
        !api.IsLoopback && api.UserInfo.Length == 0 && api.Query.Length == 0 && api.Fragment.Length == 0;

    internal static void RequireEnabled(DesktopReleaseOptions options)
    {
        if (!options.SharedBootstrapperEnabled || !IsConfigured(options))
            throw Invalid("desktop_shared_bootstrapper_disabled", "Luồng Setup dùng chung chưa được bật hoặc chưa cấu hình API/nguồn VPS.");
    }

    internal static string NormalizeApi(string value) => new Uri(value).AbsoluteUri.TrimEnd('/') + "/";

    internal static string ValidateUrl(string value, DesktopReleaseOptions options)
    {
        try
        {
            var uri = DesktopArtifactDownloadPolicy.Validate(new Uri(options.ApiBaseUrl), value,
                DesktopArtifactSourceKinds.ExternalVps, options.AllowedExternalPrefixes);
            _ = DesktopArtifactDownloadPolicy.Validate(new Uri(options.ApiBaseUrl), value, DesktopArtifactSourceKinds.ExternalVps);
            if (!uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
            return uri.AbsoluteUri;
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException)
        { throw Invalid("desktop_external_url_invalid", "Link ZIP phải dùng HTTPS và thuộc nguồn ứng dụng VPS đã duyệt."); }
    }

    internal static string Fingerprint(string version, string url, DesktopReleaseOptions options) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            version, url, options.Profile, api = NormalizeApi(options.ApiBaseUrl), channel = "Stable", platform = "win-x64",
            modelBundleId = ApprovedStaticModelBundle.Current.BundleId, protocol = DesktopDistributionProtocol.Shared
        }, Json)))).ToLowerInvariant();

    internal static string CreateFingerprint(string inputFingerprint, string? notes, bool mandatory) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { inputFingerprint, notes, mandatory }, Json)))).ToLowerInvariant();

    internal static void ValidateBinding(DesktopBootstrapperBindingInfo binding, DesktopReleaseOptions options)
    {
        if (binding is null || binding.SchemaVersion != 1 || !DesktopReleaseVersionPolicy.IsSupportedStableVersion(binding.Version)
            || binding.DistributionProtocol != DesktopDistributionProtocol.Shared || binding.Profile != options.Profile
            || binding.Platform != "win-x64" || binding.Channel != "Stable" || binding.ModelBundleId != ApprovedStaticModelBundle.Current.BundleId)
            throw Invalid("desktop_bootstrapper_binding_invalid", "Setup không hỗ trợ đúng profile/giao thức/nền tảng/bộ model hiện hành.");
        if (binding.SupportedCatalogProtocols is not ("3" or "3,4" or "3,4,5")
            || binding.SupportedCatalogProtocols == "3,4" && DesktopReleaseVersionPolicy.CompareVersions(binding.Version, DesktopDirectCatalogProtocol.MinimumBootstrapperVersion) < 0
            || binding.SupportedCatalogProtocols == "3,4,5" && DesktopReleaseVersionPolicy.CompareVersions(binding.Version, DesktopStandaloneCatalogProtocol.MinimumSetupVersion) < 0)
            throw Invalid("desktop_bootstrapper_binding_invalid", "Setup khai báo khả năng catalog không hợp lệ.");
        try
        {
            if (NormalizeApi(binding.ApiBaseUrl) != NormalizeApi(options.ApiBaseUrl))
                throw Invalid("desktop_bootstrapper_binding_invalid", "Setup không trỏ đúng API của profile hệ thống.");
        }
        catch (UriFormatException) { throw Invalid("desktop_bootstrapper_binding_invalid", "API trong Setup không hợp lệ."); }
    }

    internal static void ValidateInspection(DesktopReleaseInspectionResult result, string expectedVersion, DesktopReleaseOptions options)
    {
        var p = result.DistributionProfile;
        if (result.PackageManifest is not { Product: "VideoMaker", BuildNumber: > 0, Platform: "win-x64" } manifest
            || manifest.Version != expectedVersion || !DesktopReleaseVersionPolicy.IsSupportedStableVersion(manifest.Version)
            || result.SizeBytes is <= 0 or > DesktopArtifactDownloadPolicy.MaximumAppArchiveBytes
            || result.SizeBytes > options.MaximumExternalArtifactBytes
            || result.ExpandedSizeBytes is <= 0 or > DesktopArtifactDownloadPolicy.MaximumExpandedBytes
            || result.EntryCount is <= 0 or > DesktopArtifactDownloadPolicy.MaximumEntryCount
            || result.Sha256 is not { Length: 64 } || result.Sha256.Any(c => !Uri.IsHexDigit(c))
            || string.IsNullOrWhiteSpace(result.FileName) || result.FileName.Length > DesktopArtifactDownloadPolicy.MaximumArtifactFileNameLength
            || result.FileName != Path.GetFileName(result.FileName) || !result.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || p is null || p.SchemaVersion != 1 || p.DistributionProtocol != DesktopDistributionProtocol.Shared
            || p.Profile != options.Profile || p.Platform != "win-x64" || p.Channel != "Stable"
            || p.ModelBundleId != ApprovedStaticModelBundle.Current.BundleId
            || result.ModelBundleId != p.ModelBundleId || result.MinimumBootstrapperVersion != p.MinimumBootstrapperVersion
            || !DesktopReleaseVersionPolicy.IsSupportedStableVersion(p.MinimumBootstrapperVersion))
            throw Invalid("desktop_release_inspection_invalid", "Kết quả kiểm ZIP không khớp phiên bản hoặc policy hiện hành.");
        if (NormalizeApi(p.ServerBaseUrl) != NormalizeApi(options.ApiBaseUrl))
            throw Invalid("desktop_release_profile_mismatch", "Gói ứng dụng không trỏ đúng API của profile hệ thống.");
        if (result.PackageMetadata is { } metadata)
        {
            try { DesktopPackageMetadataPolicy.ValidateDocument(metadata); }
            catch (InvalidDataException) { throw Invalid("desktop_release_inspection_invalid", "Metadata ZIP Publish không hợp lệ."); }
            if (metadata.ArchiveSha256 != result.Sha256 || metadata.ExpandedSizeBytes != result.ExpandedSizeBytes || metadata.EntryCount != result.EntryCount
                || JsonSerializer.Serialize(metadata.Manifest, Json) != JsonSerializer.Serialize(result.PackageManifest, Json)
                || JsonSerializer.Serialize(metadata.DistributionProfile, Json) != JsonSerializer.Serialize(result.DistributionProfile, Json)
                || DesktopReleaseVersionPolicy.CompareVersions(p.MinimumBootstrapperVersion, "1.1.0") < 0)
                throw Invalid("desktop_release_inspection_invalid", "Metadata ZIP Publish không khớp bằng chứng kiểm tra.");
        }
    }

    internal static string SerializeEvidence(DesktopReleaseInspectionResult result)
    {
        var json = JsonSerializer.Serialize(result, Json);
        if (Encoding.UTF8.GetByteCount(json) > 4 * 1024 * 1024)
            throw Invalid("desktop_release_inspection_invalid", "Bằng chứng kiểm ZIP vượt giới hạn.");
        return json;
    }
}
