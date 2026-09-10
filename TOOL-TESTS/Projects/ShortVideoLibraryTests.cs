using System.Drawing;
using System.Drawing.Imaging;
using TOOL_LOCAL.Generation;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Projects;

public sealed class ShortVideoLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "videomaker-library-tests", Guid.NewGuid().ToString("N"));
    private readonly Guid _org = Guid.NewGuid();
    private const string User = "library-user";
    private ShortVideoAssetLibraryService Service() => new(_root);
    private static byte[] Picture(Color color, int width = 700, int height = 900)
    {
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(color);
        using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
        return ShortVideoWorkflowService.NormalizeImage(stream.ToArray());
    }
    private async Task<ShortVideoLibraryAsset> Add(ShortVideoAssetLibraryService service, string kind = "Character", Color? color = null)
    {
        var upload = await service.StageAsync(User, _org, "Ảnh mẫu", Picture(color ?? Color.Blue), default);
        return await service.CommitAsync(User, _org, kind, "Nhân vật chính", upload.UploadId, null, 0, default);
    }

    [Fact]
    public async Task LibrarySurvivesRestartAndUsesFullResolutionRatherThanThumbnail()
    {
        var item = await Add(Service()); var restarted = Service();
        var state = await restarted.GetAsync(User, _org, null, default);
        Assert.Equal(item, Assert.Single(state.Items));
        var full = await restarted.ReadAsync(User, _org, new(item.AssetId, item.Version), "Character", default);
        var thumb = await restarted.OpenPreviewAsync(User, _org, new(item.ThumbnailUrl), default);
        Assert.Equal(Picture(Color.Blue), full.Bytes);
        Assert.Equal(900, full.Asset.Image.Height);
        Assert.Equal(256, ShortVideoWorkflowService.Inspect(thumb.Bytes).Height);
        Assert.NotEqual(full.Asset.Image.Sha256, ShortVideoWorkflowService.Inspect(thumb.Bytes).Sha256);
    }

    [Fact]
    public async Task ScopeAndRoleAreCheckedForEveryReadAndPreview()
    {
        var service = Service(); var item = await Add(service); var reference = new ShortVideoAssetRef(item.AssetId, 1);
        Assert.Empty((await service.GetAsync("other-user", _org, null, default)).Items);
        Assert.Empty((await service.GetAsync(User, Guid.NewGuid(), null, default)).Items);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReadAsync("other-user", _org, reference, "Character", default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReadAsync(User, _org, reference, "Outfit", default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.OpenPreviewAsync(User, Guid.NewGuid(), new(item.PreviewUrl), default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.OpenPreviewAsync(User, _org, new(item.PreviewUrl.Replace("https:", "http:")), default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.OpenPreviewAsync(User, _org, new(item.PreviewUrl + "?path=secret"), default));
    }

    [Fact]
    public async Task RenameDedupReplaceAndSoftDeletePreserveOldVersion()
    {
        var service = Service(); var original = await Add(service);
        var duplicate = await Add(service); Assert.Equal(original.AssetId, duplicate.AssetId);
        var separateKind = await Add(service, "Outfit"); Assert.NotEqual(original.AssetId, separateKind.AssetId);
        var renamed = await service.RenameAsync(User, _org, original.AssetId, 1, "  Tên mới  ", default);
        Assert.Equal("Tên mới", renamed.Name); Assert.Equal(original.Image, renamed.Image); Assert.Equal(1, renamed.Version);
        var upload = await service.StageAsync(User, _org, "new", Picture(Color.Red), default);
        var replacement = await service.CommitAsync(User, _org, "Character", "Tên mới", upload.UploadId, original.AssetId, 1, default);
        Assert.Equal(2, replacement.Version);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CommitAsync(User, _org, "Character", "old", upload.UploadId, original.AssetId, 1, default));
        await service.DeleteAsync(User, _org, replacement.AssetId, 2, default);
        Assert.Single((await service.GetAsync(User, _org, null, default)).Items);
        Assert.Equal(Picture(Color.Blue), (await service.ReadAsync(User, _org, new(original.AssetId, 1), "Character", default)).Bytes);
        Assert.Equal(Picture(Color.Red), (await Service().ReadAsync(User, _org, new(original.AssetId, 2), "Character", default)).Bytes);
    }

    [Fact]
    public async Task TamperedBlobFailsBeforeUseAndMetadataCannotCommitInvalidImages()
    {
        var service = Service();
        await Assert.ThrowsAsync<InvalidDataException>(() => service.StageAsync(User, _org, "bad", "fake PNG"u8.ToArray(), default));
        var asset = await Add(service);
        var blob = Path.Combine(_root, ShortVideoAssetLibraryService.Scope(User, _org), "images", asset.Image.Sha256 + ".image");
        await File.WriteAllBytesAsync(blob, Picture(Color.Red));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ReadAsync(User, _org, new(asset.AssetId, 1), "Character", default));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.OpenPreviewAsync(User, _org, new(asset.PreviewUrl), default));
    }

    [Fact]
    public async Task DraftUsesRevisionAndRecoversTheSameReservedProjectAfterRestart()
    {
        var service = Service(); var character = await Add(service); var outfit = await Add(service, "Outfit");
        var draft = new ShortVideoDraft("Studio", "9:16", 15, true, new(character.AssetId, 1), new(outfit.AssetId, 1), "Bối cảnh riêng", "Chuyển động riêng");
        var saved = await service.SaveDraftAsync(User, _org, null, 0, draft, default);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveDraftAsync(User, _org, null, 0, draft with { Content = "Old" }, default));
        var resumed = await Service().GetAsync(User, _org, null, default); Assert.Equal(draft, resumed.Draft.Draft);
        var reserved = await service.ReserveProjectAsync(User, _org, saved.Revision, default);
        var retry = await Service().ReserveProjectAsync(User, _org, saved.Revision, default);
        Assert.Equal(reserved.CreatedProjectId, retry.CreatedProjectId);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveDraftAsync(User, _org, null, saved.Revision, draft, default));
        await service.CompleteProjectAsync(User, _org, reserved.CreatedProjectId!.Value, draft with { ServerRevision = 1 }, default);
        Assert.Null((await Service().GetAsync(User, _org, null, default)).Draft.Draft);
        Assert.Equal(1, (await Service().GetAsync(User, _org, reserved.CreatedProjectId, default)).Draft.Draft!.ServerRevision);
        await service.DeleteAsync(User, _org, character.AssetId, 1, default);
        var restored = await Service().GetAsync(User, _org, reserved.CreatedProjectId, default);
        Assert.Single(restored.Items); Assert.Equal(2, restored.Selections!.Count);
    }

    public void Dispose()
    {
        var full = Path.GetFullPath(_root);
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "videomaker-library-tests")) + Path.DirectorySeparatorChar;
        if (full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(full)) Directory.Delete(full, true);
    }
}
