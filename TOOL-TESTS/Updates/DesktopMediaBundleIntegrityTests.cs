using System.Security.Cryptography;
using TOOL_SHARED.Distribution;

namespace TOOL_TESTS.Updates;

public sealed class DesktopMediaBundleIntegrityTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "VideoMakerMediaBundleIntegrityTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ValidatePackageRoot_AcceptsCompleteBundleAndManifest()
    {
        WriteValidBundle();

        var result = DesktopMediaBundleIntegrity.ValidatePackageRoot(
            _root,
            [.. DesktopMediaBundleIntegrity.RequiredRelativePaths, "TOOL-LOCAL.exe"]);

        Assert.Equal(3, result.Sha256ByFileName.Count);
        Assert.All(result.Sha256ByFileName.Values, hash => Assert.Equal(64, hash.Length));
        Assert.Equal("Release", result.ApprovalScope);
    }

    [Fact]
    public void ValidatePackageRoot_RejectsManifestWithoutChecksumProfile()
    {
        WriteValidBundle();
        var managedFiles = DesktopMediaBundleIntegrity.RequiredRelativePaths
            .Where(path => !path.EndsWith("checksums.sha256", StringComparison.OrdinalIgnoreCase));

        var exception = Assert.Throws<InvalidDataException>(() =>
            DesktopMediaBundleIntegrity.ValidatePackageRoot(_root, managedFiles));

        Assert.Contains("manifest", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("checksums.sha256", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateBundleDirectory_RejectsModifiedBinary()
    {
        var bundle = WriteValidBundle();
        File.AppendAllText(Path.Combine(bundle, "ffprobe.exe"), "modified");

        var exception = Assert.Throws<InvalidDataException>(() =>
            DesktopMediaBundleIntegrity.ValidateBundleDirectory(bundle));

        Assert.Contains("SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ffprobe.exe", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateBundleDirectory_RejectsUnsupportedChecksumEntry()
    {
        var bundle = WriteValidBundle();
        File.AppendAllText(
            Path.Combine(bundle, "checksums.sha256"),
            $"{new string('0', 64)} *unexpected.dll{Environment.NewLine}");

        var exception = Assert.Throws<InvalidDataException>(() =>
            DesktopMediaBundleIntegrity.ValidateBundleDirectory(bundle));

        Assert.Contains("không được hỗ trợ", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatePackageRoot_RejectsDevelopmentOnlyBundle()
    {
        WriteValidBundle("Development");

        var exception = Assert.Throws<InvalidDataException>(() =>
            DesktopMediaBundleIntegrity.ValidatePackageRoot(
                _root,
                [.. DesktopMediaBundleIntegrity.RequiredRelativePaths, "TOOL-LOCAL.exe"]));

        Assert.Contains("Development", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("phát hành", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateBundleDirectory_AllowsDevelopmentBundleForLocalPreflight()
    {
        var bundle = WriteValidBundle("Development");

        var result = DesktopMediaBundleIntegrity.ValidateBundleDirectory(
            bundle,
            requireReleaseApproval: false);

        Assert.Equal("Development", result.ApprovalScope);
    }

    private string WriteValidBundle(string approvalScope = "Release")
    {
        var bundle = Path.Combine(_root, "tools", "ffmpeg");
        Directory.CreateDirectory(bundle);
        File.WriteAllText(Path.Combine(bundle, "ffmpeg.exe"), "test ffmpeg");
        File.WriteAllText(Path.Combine(bundle, "ffprobe.exe"), "test ffprobe");
        File.WriteAllText(Path.Combine(bundle, "LICENSE.txt"), "test license");
        File.WriteAllText(
            Path.Combine(bundle, "PROVENANCE.md"),
            $"# Approved test bundle{Environment.NewLine}{Environment.NewLine}- Approval scope: {approvalScope}{Environment.NewLine}");
        var checksums = new[] { "ffmpeg.exe", "ffprobe.exe", "LICENSE.txt" }
            .Select(fileName =>
            {
                using var stream = File.OpenRead(Path.Combine(bundle, fileName));
                return $"{Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()} *{fileName}";
            });
        File.WriteAllLines(Path.Combine(bundle, "checksums.sha256"), checksums);
        return bundle;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
