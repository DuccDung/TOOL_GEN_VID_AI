namespace TOOL_TESTS;

// Native OCR, FFmpeg and WebView2 share CPU/RAM and Windows UI resources.
// Keep these tests in the standard suite, with no overlap with other collections.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NativeWindowsCollection
{
    public const string Name = "Native Windows";
}
