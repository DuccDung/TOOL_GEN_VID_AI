using System.Buffers.Binary;
using TOOL_SERVER.Generation;

namespace TOOL_TESTS.Generation;

public sealed class FalVeoPolicyTests
{
    [Fact]
    public void ValidateFirstFrame_AcceptsApprovedLandscapePngMetadata()
    {
        var bytes = CreatePngHeader(1280, 720);
        var reference = new VideoProviderReferenceImage(
            Guid.NewGuid(),
            "image/png",
            Convert.ToBase64String(bytes),
            new string('a', 64));

        FalVeoPolicy.ValidateFirstFrame(reference, 1280, 720, "16:9");
    }

    [Theory]
    [InlineData(1024, 1024, "16:9", "fal_first_frame_aspect_invalid")]
    [InlineData(720, 1280, "16:9", "fal_first_frame_aspect_invalid")]
    public void ValidateFirstFrame_RejectsUnsafeDimensions(int width, int height, string aspectRatio, string code)
    {
        var bytes = CreatePngHeader(width, height);
        var reference = new VideoProviderReferenceImage(
            Guid.NewGuid(),
            "image/png",
            Convert.ToBase64String(bytes),
            new string('a', 64));

        var exception = Assert.Throws<KlingPromptValidationException>(() =>
            FalVeoPolicy.ValidateFirstFrame(reference, width, height, aspectRatio));

        Assert.Equal(code, exception.Code);
    }

    private static byte[] CreatePngHeader(int width, int height)
    {
        var bytes = new byte[24];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        return bytes;
    }
}
