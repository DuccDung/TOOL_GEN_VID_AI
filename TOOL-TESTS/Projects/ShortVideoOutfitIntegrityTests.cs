using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using TOOL_LOCAL.Generation;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Projects;

public sealed class ShortVideoOutfitIntegrityTests
{
    [Fact]
    public void FileValidation_RejectsDifferentPixelsAndFakeMimeEvenWithSameDimensions()
    {
        static byte[] ImageBytes(Color color) { using var image = new Bitmap(20, 30); using (var graphics = Graphics.FromImage(image)) graphics.Clear(color); using var stream = new MemoryStream(); image.Save(stream, ImageFormat.Png); return stream.ToArray(); }
        var a = ImageBytes(Color.Red); var b = ImageBytes(Color.Blue);
        var info = ShortVideoWorkflowService.Inspect(a);
        ShortVideoWorkflowService.CheckImage(a, info);
        Assert.Throws<InvalidDataException>(() => ShortVideoWorkflowService.CheckImage(b, info));
        Assert.Throws<InvalidDataException>(() => ShortVideoWorkflowService.CheckImage(a, info with { MimeType = "image/jpeg" }));
        Assert.Throws<InvalidDataException>(() => ShortVideoWorkflowService.Inspect("not an image"u8.ToArray()));
    }

    [Theory]
    [InlineData("PendingReview", 2, false, false)]
    [InlineData("Rejected", 2, false, false)]
    [InlineData("Approved", 1, false, false)]
    [InlineData("Approved", 2, true, false)]
    [InlineData("Approved", 2, false, true)]
    public void ApprovalRenderExportGuard_RejectsOldCompositionRevisionOrHash(string status, int revision, bool wrongId, bool wrongHash)
    {
        var composition = new ShortVideoComposition(Guid.NewGuid(), status, new string('a', 64), "image/png", 100, 720, 1280, "/image", 2);
        var state = new ShortVideoState(Guid.NewGuid(), 2, null, null, "background", "motion", composition, true);
        var request = JsonSerializer.Serialize(new { shortVideoCompositionId = wrongId ? Guid.NewGuid() : composition.CompositionId, shortVideoRevision = revision, referenceSha256 = wrongHash ? new string('b', 64) : composition.Sha256 });
        Assert.Throws<InvalidDataException>(() => ShortVideoWorkflowService.ValidateSnapshot(state, request));
    }

    [Fact]
    public void ApprovalRenderExportGuard_AcceptsTheCurrentApprovedImage()
    {
        var composition = new ShortVideoComposition(Guid.NewGuid(), "Approved", new string('a', 64), "image/png", 100, 720, 1280, "/image", 2);
        var state = new ShortVideoState(Guid.NewGuid(), 2, null, null, "background", "motion", composition, true);
        ShortVideoWorkflowService.ValidateSnapshot(state, JsonSerializer.Serialize(new { shortVideoCompositionId = composition.CompositionId, shortVideoRevision = 2, referenceSha256 = composition.Sha256 }));
    }
}
