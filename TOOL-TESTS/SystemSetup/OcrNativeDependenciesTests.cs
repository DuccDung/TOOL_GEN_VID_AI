using System.Text.Json;
using TOOL_LOCAL.Vietsub.Ocr;

namespace TOOL_TESTS.SystemSetup;

public sealed class OcrNativeDependenciesTests
{
    [Theory]
    [InlineData("msvcp140.dll")]
    [InlineData("vcruntime140.dll")]
    [InlineData("vcruntime140_1.dll")]
    [InlineData("vcomp140.dll")]
    public async Task MissingLocalDependencyNeverPassesOcrEvenWhenWindowsHasARuntime(string name)
    {
        var root = CopyRuntime();
        try
        {
            File.Delete(Path.Combine(root, name));
            await using var recognizer = new PaddleVietsubOcrRecognizer(root);
            var status = await recognizer.GetRuntimeStatusAsync(default);
            Assert.False(status.Ready);
            Assert.Equal(OcrNativeDependencies.Missing, status.ErrorCode);
            Assert.DoesNotContain(root, JsonSerializer.Serialize(status));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AlteredSameSizeDllIsRejectedAndRestoredFileCanBeRetried()
    {
        var root = CopyRuntime();
        try
        {
            var path = Path.Combine(root, "vcomp140.dll");
            var bytes = File.ReadAllBytes(path);
            bytes[^1] ^= 0x01;
            File.WriteAllBytes(path, bytes);
            Assert.Equal(OcrNativeDependencies.Invalid, OcrNativeDependencies.CheckFiles(root));
            File.Copy(Path.Combine(AppContext.BaseDirectory, "vcomp140.dll"), path, true);
            Assert.Null(OcrNativeDependencies.CheckFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void UntrustedSidecarCannotAuthorizeChangedRuntime()
    {
        var root = CopyRuntime();
        try
        {
            File.WriteAllText(Path.Combine(root, "MSVC_RUNTIME.json"), "{\"files\":[]}");
            File.WriteAllBytes(Path.Combine(root, "msvcp140.dll"), [0, 1, 2]);
            Assert.Equal(OcrNativeDependencies.Invalid, OcrNativeDependencies.CheckFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string CopyRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-native-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var file in OcrNativeDependencies.Files)
            File.Copy(Path.Combine(AppContext.BaseDirectory, file.Name), Path.Combine(root, file.Name));
        return root;
    }
}
