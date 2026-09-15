using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TOOL_LOCAL.Data;
using TOOL_LOCAL.Data.Models;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Storage;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_LOCAL.Generation;

internal sealed record ShortVideoView(ShortVideoState State, string? CharacterPreview, string? OutfitPreview, string? CompositionPreview);
internal sealed record ShortVideoImportedImage(ShortVideoImageInfo Info, string Preview);

public interface IShortVideoLineageValidator
{
    Task ValidateLineageAsync(Project project, Scene scene, Guid? requestId, CancellationToken ct);
}

internal sealed class ShortVideoWorkflowService(IDbContextFactory<VideoFactoryDbContext> factory,
    ProjectWorkspaceService workspace, IGenerationClient api, bool enabled) : IShortVideoLineageValidator
{
    public bool Enabled => enabled;
    internal ShortVideoAssetLibraryService Library { get; } = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static bool IsOutfit(string? json)
    {
        try { using var doc = JsonDocument.Parse(json ?? "{}"); return doc.RootElement.TryGetProperty("shortVideoMode", out var mode) && mode.GetString() == ShortVideoModes.CharacterOutfit; }
        catch (JsonException) { return false; }
    }
    internal static bool RequiresImageReview(string? json)
    {
        if (IsOutfit(json)) return true;
        try { using var doc = JsonDocument.Parse(json ?? "{}"); return doc.RootElement.TryGetProperty("requiresFirstFrame", out var flag) && flag.ValueKind == JsonValueKind.True; }
        catch (JsonException) { return false; }
    }
    private async Task<Project> ProjectAsync(Guid id, string user, Guid org, CancellationToken ct, bool requireOutfit = true)
    {
        if ((requireOutfit && !enabled) || api.SelectedOrganizationId != org) throw new ArgumentException("Chức năng phối đồ chưa bật hoặc tổ chức đã đổi.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == id && x.OrganizationId == org && x.RemoteUserId == user && x.DeletedAtUtc == null, ct)
            ?? throw new ArgumentException("Dự án không thuộc phiên hiện hành.");
        var capabilities = await db.Scenes.Where(x => x.ProjectId == id && x.ScenePlanVersion == project.CurrentScenePlanVersion).Select(x => x.RequiredCapabilitiesJson).SingleAsync(ct);
        if (requireOutfit && !IsOutfit(capabilities)) throw new ArgumentException("Dự án không thuộc chế độ phối đồ.");
        if (!await db.Scripts.AnyAsync(x => x.ProjectId == id && x.StructureType == "DirectShortVideo", ct)) throw new ArgumentException("Dự án không phải video ngắn.");
        return project;
    }
    private string Folder(Project p)
    {
        if (!string.Equals(p.WorkspaceRelativePath.Replace('\\', '/').TrimEnd('/'), $"projects/{p.ProjectId:N}", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Workspace không khớp dự án phối đồ.");
        return Path.Combine(p.WorkspaceRelativePath, "short-outfit");
    }
    private string ImagePath(Project p, ShortVideoImageInfo info)
    {
        if (info.Sha256 is null || info.Sha256.Length != 64 || !info.Sha256.All(Uri.IsHexDigit) || info.MimeType is not ("image/png" or "image/jpeg")) throw new InvalidDataException("Metadata ảnh không hợp lệ.");
        return workspace.Resolve(Path.Combine(Folder(p), info.Sha256.ToLowerInvariant() + (info.MimeType == "image/png" ? ".png" : ".jpg")));
    }
    private string? Preview(Project p, ShortVideoImageInfo? info)
    {
        if (info is null || !File.Exists(ImagePath(p, info))) return null;
        return "https://media.app.local/" + string.Join('/', Path.GetRelativePath(workspace.WorkspaceRoot, ImagePath(p, info)).Split(Path.DirectorySeparatorChar).Select(Uri.EscapeDataString));
    }
    internal static ShortVideoImageInfo Inspect(byte[] bytes)
    {
        if (bytes.Length is <= 0 or > 10 * 1024 * 1024) throw new InvalidDataException("Ảnh phải nhỏ hơn 10 MiB.");
        var png = bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var jpeg = bytes.Length > 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
        if (!png && !jpeg) throw new InvalidDataException("Chỉ nhận ảnh PNG/JPEG.");
        using var stream = new MemoryStream(bytes);
        using var image = Image.FromStream(stream, false, true);
        if (image.Width <= 0 || image.Height <= 0 || (long)image.Width * image.Height > 16_000_000) throw new InvalidDataException("Ảnh vượt 16 megapixel.");
        return new(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), png ? "image/png" : "image/jpeg", bytes.LongLength, image.Width, image.Height);
    }
    internal static void CheckImage(byte[] bytes, ShortVideoImageInfo expected)
    {
        if (Inspect(bytes) != expected) throw new InvalidDataException("Ảnh đã thay đổi hoặc không khớp hash, định dạng, kích thước.");
    }
    internal static async Task AtomicAsync(string path, byte[] bytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".part";
        try { await File.WriteAllBytesAsync(temporary, bytes, ct); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private async Task<byte[]> ReadImageAsync(Project p, ShortVideoImageInfo info, CancellationToken ct)
    {
        var path = ImagePath(p, info);
        if (!File.Exists(path) || new FileInfo(path).Length != info.SizeBytes || info.SizeBytes is <= 0 or > 10 * 1024 * 1024)
            throw new InvalidDataException("Ảnh trong workspace thiếu hoặc đã đổi. Hãy chọn lại ảnh.");
        var bytes = await ReadBoundedAsync(path, ct); CheckImage(bytes, info); return bytes;
    }
    internal static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken ct)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (file.Length is <= 0 or > 10 * 1024 * 1024) throw new InvalidDataException("Ảnh tối đa 10 MiB.");
        var bytes = new byte[file.Length]; await file.ReadExactlyAsync(bytes, ct);
        return bytes;
    }
    public async Task<ShortVideoImportedImage> ImportAsync(Guid id, string user, Guid org, string path, CancellationToken ct)
    {
        var project = await ProjectAsync(id, user, org, ct);
        if (!File.Exists(path) || new FileInfo(path).Length is <= 0 or > 10 * 1024 * 1024) throw new InvalidDataException("Ảnh tối đa 10 MiB.");
        var bytes = await ReadBoundedAsync(path, ct);
        bytes = NormalizeImage(bytes);
        var info = Inspect(bytes);
        await AtomicAsync(ImagePath(project, info), bytes, ct);
        return new(info, Preview(project, info)!);
    }
    internal static byte[] NormalizeImage(byte[] bytes)
    {
        Inspect(bytes);
        using (var stream = new MemoryStream(bytes))
        using (var source = Image.FromStream(stream, false, true))
        {
            if (source.PropertyIdList.Contains(0x112))
            {
                var orientation = source.GetPropertyItem(0x112)?.Value?.FirstOrDefault() ?? 1;
                source.RotateFlip(orientation switch { 2 => RotateFlipType.RotateNoneFlipX, 3 => RotateFlipType.Rotate180FlipNone, 4 => RotateFlipType.Rotate180FlipX, 5 => RotateFlipType.Rotate90FlipX, 6 => RotateFlipType.Rotate90FlipNone, 7 => RotateFlipType.Rotate270FlipX, 8 => RotateFlipType.Rotate270FlipNone, _ => RotateFlipType.RotateNoneFlipNone });
            }
            using var clean = new Bitmap(source.Width, source.Height);
            using (var graphics = Graphics.FromImage(clean)) graphics.DrawImage(source, 0, 0, source.Width, source.Height);
            using var output = new MemoryStream(); clean.Save(output, ImageFormat.Png); bytes = output.ToArray();
        }
        Inspect(bytes);
        return bytes;
    }
    internal async Task<ShortVideoImportedImage> AttachAsync(Guid id, string user, Guid org, byte[] bytes, ShortVideoImageInfo info, CancellationToken ct)
    {
        var project = await ProjectAsync(id, user, org, ct);
        CheckImage(bytes, info);
        await AtomicAsync(ImagePath(project, info), bytes, ct);
        return new(info, Preview(project, info)!);
    }
    internal async Task<byte[]> ReadSourceAsync(Guid id, string user, Guid org, ShortVideoImageInfo info, CancellationToken ct)
        => await ReadImageAsync(await ProjectAsync(id, user, org, ct), info, ct);
    private async Task EnsureCompositionAsync(Project project, ShortVideoComposition image, CancellationToken ct)
    {
        var info = new ShortVideoImageInfo(image.Sha256, image.MimeType, image.SizeBytes, image.Width, image.Height);
        if (File.Exists(ImagePath(project, info))) { await ReadImageAsync(project, info, ct); return; }
        var bytes = await api.DownloadOutfitAsync(image, ct); CheckImage(bytes, info);
        await AtomicAsync(ImagePath(project, info), bytes, ct);
    }
    public async Task<ShortVideoView> GetAsync(Guid id, string user, Guid org, CancellationToken ct)
    {
        var project = await ProjectAsync(id, user, org, ct);
        var state = await api.GetOutfitAsync(id, ct);
        if (state.Composition is { } image)
        {
            try { await EnsureCompositionAsync(project, image, ct); }
            catch (AccountClientException error) when (error.Code == "generated_image_expired")
            { state = state with { Message = "Ảnh tạm trên server đã hết hạn và máy này chưa có bản tải về. Chọn lại ảnh nguồn nếu cần rồi lấy báo giá tạo ảnh mới." }; }
        }
        return new(state, Preview(project, state.Character), Preview(project, state.Outfit), state.Composition is { } c ? Preview(project, new(c.Sha256, c.MimeType, c.SizeBytes, c.Width, c.Height)) : null);
    }
    public async Task SaveAsync(ShortVideoSettingsRequest request, string user, CancellationToken ct)
    {
        var project = await ProjectAsync(request.ProjectId, user, request.OrganizationId, ct);
        await ReadImageAsync(project, request.Character, ct); await ReadImageAsync(project, request.Outfit, ct);
        await api.SaveOutfitAsync(request, ct);
    }
    public async Task ComposeAsync(Guid id, string user, Guid org, Guid quoteId, CancellationToken ct)
    {
        var project = await ProjectAsync(id, user, org, ct);
        var state = await api.GetOutfitAsync(id, ct);
        var a = state.Character ?? throw new ArgumentException("Thiếu ảnh nhân vật.");
        var b = state.Outfit ?? throw new ArgumentException("Thiếu ảnh trang phục.");
        var image = await api.ComposeOutfitAsync(new(id, org, quoteId, new(a, Convert.ToBase64String(await ReadImageAsync(project, a, ct))), new(b, Convert.ToBase64String(await ReadImageAsync(project, b, ct)))), ct);
        await EnsureCompositionAsync(project, image, ct);
    }
    public async Task SaveVideoQuoteAsync(Guid id, string user, Guid org, ShortVideoQuote quote, CancellationToken ct)
    {
        var project = await ProjectAsync(id, user, org, ct);
        await AtomicAsync(workspace.Resolve(Path.Combine(Folder(project), "video-quote.json")), JsonSerializer.SerializeToUtf8Bytes(quote, Json), ct);
    }
    public async Task PrepareTextVideoAsync(Guid id, string user, Guid org, ShortVideoQuote quote, CancellationToken ct)
    {
        var project = await ProjectAsync(id, user, org, ct, false);
        if (project.VideoProviderCode != "fal" || quote.Kind != "Video") throw new ArgumentException("Cần báo giá Veo cho video ngắn.");
        await AtomicAsync(workspace.Resolve(Path.Combine(Folder(project), "text-video-quote.json")), JsonSerializer.SerializeToUtf8Bytes(quote, Json), ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var scene = await db.Scenes.SingleAsync(x => x.ProjectId == id && x.ScenePlanVersion == project.CurrentScenePlanVersion, ct);
        if (IsOutfit(scene.RequiredCapabilitiesJson)) throw new ArgumentException("Hãy tạo video trong chế độ phối đồ.");
        scene.ApprovedGenerationId = null; scene.ApprovedRenderMediaAssetId = null; scene.Status = "PromptReady";
        await db.SaveChangesAsync(ct);
    }
    internal async Task<Guid> TextVideoQuoteAsync(Project project, CancellationToken ct)
    {
        if (api.SelectedOrganizationId != project.OrganizationId) throw new ArgumentException("Tổ chức đã thay đổi.");
        var path = workspace.Resolve(Path.Combine(Folder(project), "text-video-quote.json"));
        if (!File.Exists(path) || new FileInfo(path).Length > 16384) throw new ArgumentException("Hãy lấy báo giá và xác nhận chi phí Veo trước.");
        return JsonSerializer.Deserialize<ShortVideoQuote>(await File.ReadAllTextAsync(path, ct), Json)?.QuoteId ?? throw new InvalidDataException("Báo giá không hợp lệ.");
    }
    public async Task RequireTextResumeAsync(Guid id, string user, Guid org, CancellationToken ct)
    {
        var project = await ProjectAsync(id, user, org, ct, false);
        var key = $"short-video:{await TextVideoQuoteAsync(project, ct):N}";
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.ProviderRequests.AnyAsync(x => x.ProjectId == id && x.IdempotencyKey == key && x.ProviderCode == "fal" &&
            x.RequestKind == "Video" && x.Status != "Failed" && x.Status != "Cancelled" && x.Status != "Expired", ct))
            throw new ArgumentException("Chưa có tác vụ Veo để tiếp tục. Hãy xem báo giá trước khi tạo video mới.");
    }
    public async Task PrepareVideoAsync(Guid id, string user, Guid org, CancellationToken ct)
    {
        var project = await ProjectAsync(id, user, org, ct);
        await VideoInputAsync(project, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var scene = await db.Scenes.SingleAsync(x => x.ProjectId == id && x.ScenePlanVersion == project.CurrentScenePlanVersion, ct);
        scene.ApprovedGenerationId = null; scene.ApprovedRenderMediaAssetId = null; scene.Status = "PromptReady";
        await db.SaveChangesAsync(ct);
    }
    public async Task<ShortVideoCompositionInput> VideoInputAsync(Project project, CancellationToken ct)
    {
        if (!enabled || api.SelectedOrganizationId != project.OrganizationId) throw new ArgumentException("Chức năng hoặc tổ chức không hợp lệ.");
        var state = await api.GetOutfitAsync(project.ProjectId, ct);
        var c = state.Composition;
        if (c?.Status != "Approved") throw new ArgumentException("Hãy duyệt ảnh phối trang phục hiện hành.");
        var quotePath = workspace.Resolve(Path.Combine(Folder(project), "video-quote.json"));
        if (!File.Exists(quotePath)) throw new ArgumentException("Hãy xác nhận báo giá video trước.");
        var quote = JsonSerializer.Deserialize<ShortVideoQuote>(await File.ReadAllTextAsync(quotePath, ct), Json)!;
        if (quote.Revision != state.Revision || quote.Kind != "Video") throw new ArgumentException("Báo giá không khớp phiên bản ảnh.");
        await EnsureCompositionAsync(project, c, ct);
        var bytes = await ReadImageAsync(project, new(c.Sha256, c.MimeType, c.SizeBytes, c.Width, c.Height), ct);
        return new(c.CompositionId, quote.QuoteId, state.Revision, c.MimeType, Convert.ToBase64String(bytes), c.Sha256);
    }
    public async Task ValidateLineageAsync(Project project, Scene scene, Guid? requestId, CancellationToken ct)
    {
        if (!RequiresImageReview(scene.RequiredCapabilitiesJson)) return;
        if (!IsOutfit(scene.RequiredCapabilitiesJson))
        {
            await using var textDb = await factory.CreateDbContextAsync(ct);
            var source = await textDb.ProviderRequests.AsNoTracking().SingleOrDefaultAsync(x => x.ProviderRequestId == requestId && x.ProjectId == project.ProjectId && x.SceneId == scene.SceneId && x.RequestKind == "Video", ct)
                ?? throw new InvalidDataException("Không tìm thấy nguồn tạo video Veo.");
            using var snapshot = JsonDocument.Parse(source.RequestJson);
            if (!snapshot.RootElement.TryGetProperty("sceneFirstFrameId", out var frameId) || !frameId.TryGetGuid(out var sourceFrameId))
                throw new InvalidDataException("Video chưa có nguồn ảnh đầu vào Veo.");
            var frame = (await api.GetSceneFirstFramesAsync(project.ProjectId, scene.SceneId, ct)).Frames.SingleOrDefault(x => x.SceneFirstFrameId == sourceFrameId && x.Status == "Approved" && x.IsCurrent)
                ?? throw new InvalidDataException("Ảnh đầu vào video đã đổi hoặc không còn được duyệt.");
            if (!snapshot.RootElement.TryGetProperty("referenceSha256", out var hash) || hash.GetString() != frame.Sha256)
                throw new InvalidDataException("Ảnh đầu vào không khớp nguồn video.");
            var framePath = workspace.Resolve(frame.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var imageBytes = await ReadBoundedAsync(framePath, ct);
            CheckImage(imageBytes, new(frame.Sha256, frame.MimeType, frame.SizeBytes, frame.Width, frame.Height));
            return;
        }
        var state = await api.GetOutfitAsync(project.ProjectId, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var request = await db.ProviderRequests.AsNoTracking().SingleOrDefaultAsync(x => x.ProviderRequestId == requestId && x.ProjectId == project.ProjectId && x.SceneId == scene.SceneId && x.RequestKind == "Video", ct);
        if (request is null) throw new InvalidDataException("Không tìm thấy nguồn tạo video.");
        ValidateSnapshot(state, request.RequestJson);
    }
    internal static void ValidateSnapshot(ShortVideoState state, string requestJson)
    {
        using var snapshot = JsonDocument.Parse(requestJson);
        if (state.Composition?.Status != "Approved" || !snapshot.RootElement.TryGetProperty("shortVideoCompositionId", out var id) || id.GetGuid() != state.Composition.CompositionId ||
            !snapshot.RootElement.TryGetProperty("referenceSha256", out var hash) || hash.GetString() != state.Composition.Sha256 ||
            !snapshot.RootElement.TryGetProperty("shortVideoRevision", out var revision) || revision.GetInt32() != state.Revision)
            throw new InvalidDataException("Video thuộc ảnh hoặc thiết lập cũ. Hãy tạo video từ ảnh đã duyệt hiện hành.");
    }
}
