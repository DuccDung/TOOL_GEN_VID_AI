using System.Text.Json;
using TOOL_LOCAL.SystemSetup;
using TOOL_LOCAL.Vietsub.Ocr;

namespace TOOL_TESTS.SystemSetup;

public sealed class OcrRuntimeDiagnosticsTests
{
    [Fact]
    public void NativeLoaderFailureInsideTypeInitializerKeepsItsCategoryWithoutLeakingPaths()
    {
        var exception = new TypeInitializationException("private-type", new DllNotFoundException("private-path and token"));
        var code = VietsubOcrRuntimeDiagnostics.Classify(exception);
        Assert.Equal("OCR_NATIVE_LOAD_FAILED", code);
        Assert.DoesNotContain("private-", VietsubOcrRuntimeDiagnostics.Message(code));
    }

    [Theory]
    [InlineData("binary", "OCR_BINARY_INVALID")]
    [InlineData("missing", "OCR_COMPONENT_MISSING")]
    [InlineData("load", "OCR_COMPONENT_LOAD_FAILED")]
    [InlineData("unknown", "OCR_RUNTIME_INVALID")]
    public async Task SetupPreservesDiagnosticCategoryButDoesNotTrustRecognizerErrorText(string kind, string expected)
    {
        Exception exception = kind switch {
            "binary" => new BadImageFormatException("private-path"),
            "missing" => new FileNotFoundException("private-path"),
            "load" => new FileLoadException("private-path"),
            _ => new InvalidOperationException("private-path") };
        var code = VietsubOcrRuntimeDiagnostics.Classify(exception);
        Assert.Equal(expected, code);
        await using var recognizer = new UnavailableVietsubOcrRecognizer(code, "private-path private-token");
        var adapter = new OcrSetupAdapter(recognizer, true);
        var status = await adapter.RunAsync(false, false, (_, _, _, _) => { }, default);
        Assert.Equal("REPAIR_REQUIRED", status.State);
        Assert.Equal(expected, status.ErrorCode);
        Assert.DoesNotContain("private-", JsonSerializer.Serialize(status));
        var report = Assert.Single(await DesktopReadinessCommand.InspectAsync([adapter], default));
        Assert.Equal(expected, report.ErrorCode);
    }
}
