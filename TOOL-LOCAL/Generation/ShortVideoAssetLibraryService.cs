using System.Drawing.Imaging;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_LOCAL.Generation;

/// <summary>A local, account/organization-scoped library. Project images are copied, never linked.</summary>
internal sealed class ShortVideoAssetLibraryService
{
    internal const string HostName = "short-library.app.local";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, Upload> _uploads = [];
    private sealed record Upload(string Scope, string Name, byte[] Bytes, ShortVideoImageInfo Info, DateTime Expires);

    internal ShortVideoAssetLibraryService(string? root = null) => _root = Path.GetFullPath(root ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoMaker", "ShortVideoLibrary"));

    internal static string Scope(string user, Guid org)
    {
        if (string.IsNullOrWhiteSpace(user) || org == Guid.Empty) throw new ArgumentException("Hãy đăng nhập và chọn tổ chức.");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{user.Length}:{user}:{org:N}"))).ToLowerInvariant();
    }
    private string SafePath(string scope, string name)
    {
        var path = Path.GetFullPath(Path.Combine(_root, scope, name));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Đường dẫn thư viện không hợp lệ.");
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Thư viện không hỗ trợ thư mục liên kết.");
        return path;
    }
    private static SqliteCommand Command(SqliteConnection db, string sql, params (string, object?)[] values)
    {
        var command = db.CreateCommand(); command.CommandText = sql;
        foreach (var (key, value) in values) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return command;
    }
    private async Task<T> InScopeAsync<T>(string user, Guid org, Func<SqliteConnection, string, Task<T>> work, CancellationToken ct)
    {
        var scope = Scope(user, org);
        await _gate.WaitAsync(ct);
        try
        {
            var database = SafePath(scope, "library.sqlite3");
            Directory.CreateDirectory(Path.GetDirectoryName(database)!);
            await using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false, DefaultTimeout = 10 }.ToString());
            await db.OpenAsync(ct);
            using (var version = Command(db, "PRAGMA user_version"))
                if (Convert.ToInt32(await version.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) > 1)
                    throw new ArgumentException("Thư viện được tạo bởi bản taphoatool mới hơn. Hãy cập nhật ứng dụng trước khi mở.");
            using var schema = Command(db, """
                PRAGMA foreign_keys=ON;
                CREATE TABLE IF NOT EXISTS Assets(Id TEXT PRIMARY KEY,Kind TEXT NOT NULL,Name TEXT NOT NULL,
                    Version INTEGER NOT NULL,Deleted INTEGER NOT NULL DEFAULT 0,Created TEXT NOT NULL,Updated TEXT NOT NULL,Used TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS Versions(AssetId TEXT NOT NULL,Version INTEGER NOT NULL,Image TEXT NOT NULL,Thumbnail TEXT NOT NULL,
                    PRIMARY KEY(AssetId,Version),FOREIGN KEY(AssetId) REFERENCES Assets(Id));
                CREATE TABLE IF NOT EXISTS Drafts(Id TEXT PRIMARY KEY,Revision INTEGER NOT NULL,Content TEXT NOT NULL,CreatedProjectId TEXT NULL);
                PRAGMA user_version=1;
                """);
            await schema.ExecuteNonQueryAsync(ct);
            return await work(db, scope);
        }
        finally { _gate.Release(); }
    }
    private static string Name(string? value)
    {
        var name = value?.Trim() ?? "";
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl)) throw new ArgumentException("Tên phải có 1–80 ký tự, không chứa ký tự điều khiển.");
        return name;
    }
    private static void Kind(string? kind)
    {
        if (kind is not ("Character" or "Outfit")) throw new ArgumentException("Loại ảnh không hợp lệ.");
    }
    private static string Url(string scope, Guid id, int version, string size) => $"https://{HostName}/{scope}/{id:N}/{version}/{size}";
    private static DateTime Date(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static ShortVideoLibraryAsset Asset(SqliteDataReader row, string scope)
    {
        var id = Guid.Parse(row.GetString(0)); var version = row.GetInt32(3);
        return new(id, row.GetString(1), row.GetString(2), version, JsonSerializer.Deserialize<ShortVideoImageInfo>(row.GetString(4), Json)!,
            Url(scope, id, version, "thumb"), Url(scope, id, version, "image"), Date(row.GetString(5)), Date(row.GetString(6)));
    }
    private static async Task<ShortVideoLibraryAsset> FindAsync(SqliteConnection db, string scope, ShortVideoAssetRef reference, string kind, CancellationToken ct)
    {
        Kind(kind);
        using var command = Command(db, "SELECT a.Id,a.Kind,a.Name,v.Version,v.Image,a.Created,a.Updated FROM Assets a JOIN Versions v ON a.Id=v.AssetId WHERE a.Id=$id AND v.Version=$v AND a.Kind=$kind",
            ("$id", reference.AssetId.ToString("N")), ("$v", reference.Version), ("$kind", kind));
        await using var row = await command.ExecuteReaderAsync(ct);
        if (!await row.ReadAsync(ct)) throw new ArgumentException("Không tìm thấy ảnh trong thư viện của tài khoản và tổ chức này.");
        return Asset(row, scope);
    }
    internal Task<ShortVideoLibraryState> GetAsync(string user, Guid org, Guid? project, CancellationToken ct) => InScopeAsync(user, org, async (db, scope) =>
    {
        var items = new List<ShortVideoLibraryAsset>();
        using (var command = Command(db, "SELECT a.Id,a.Kind,a.Name,a.Version,v.Image,a.Created,a.Updated FROM Assets a JOIN Versions v ON a.Id=v.AssetId AND a.Version=v.Version WHERE a.Deleted=0 ORDER BY a.Used DESC,a.Created DESC,a.Id"))
        await using (var row = await command.ExecuteReaderAsync(ct))
            while (await row.ReadAsync(ct)) items.Add(Asset(row, scope));
        var draft = await DraftAsync(db, project, ct);
        var selections = new List<ShortVideoLibraryAsset>();
        if (draft.Draft?.Character is { } character) selections.Add(await FindAsync(db, scope, character, "Character", ct));
        if (draft.Draft?.Outfit is { } outfit) selections.Add(await FindAsync(db, scope, outfit, "Outfit", ct));
        return new ShortVideoLibraryState(items, draft, selections);
    }, ct);

    internal async Task<ShortVideoLibraryUpload> PickAsync(string user, Guid org, string path, CancellationToken ct)
    {
        var bytes = await ShortVideoWorkflowService.ReadBoundedAsync(path, ct);
        bytes = await Task.Run(() => ShortVideoWorkflowService.NormalizeImage(bytes), ct);
        return await StageAsync(user, org, Path.GetFileNameWithoutExtension(path), bytes, ct);
    }
    internal Task<ShortVideoLibraryUpload> StageAsync(string user, Guid org, string name, byte[] bytes, CancellationToken ct) => InScopeAsync(user, org, (db, scope) =>
    {
        var info = ShortVideoWorkflowService.Inspect(bytes);
        foreach (var old in _uploads.Where(x => x.Value.Expires <= DateTime.UtcNow || x.Value.Scope != scope).Select(x => x.Key).ToArray()) _uploads.Remove(old);
        // Only the current pick is retained; opening a file picker and cancelling does not replace it.
        foreach (var old in _uploads.Keys.ToArray()) _uploads.Remove(old);
        var id = Guid.NewGuid();
        var suggested = new string(name.Where(c => !char.IsControl(c)).Take(80).ToArray()).Trim();
        if (suggested.Length == 0) suggested = "Ảnh mới";
        _uploads[id] = new(scope, suggested, bytes, info, DateTime.UtcNow.AddMinutes(15));
        return Task.FromResult(new ShortVideoLibraryUpload(id, suggested, info, Url(scope, id, 0, "upload")));
    }, ct);

    internal Task<ShortVideoLibraryAsset> CommitAsync(string user, Guid org, string kind, string name, Guid uploadId, Guid? replaceId, int expectedVersion, CancellationToken ct) => InScopeAsync(user, org, async (db, scope) =>
    {
        Kind(kind); name = Name(name);
        if (!_uploads.TryGetValue(uploadId, out var upload) || upload.Scope != scope || upload.Expires <= DateTime.UtcNow)
            throw new ArgumentException("Ảnh xem trước đã hết hạn. Hãy chọn lại ảnh.");
        ShortVideoWorkflowService.CheckImage(upload.Bytes, upload.Info);
        if (replaceId is null)
        {
            using var duplicates = Command(db, "SELECT a.Id,a.Version,v.Image FROM Assets a JOIN Versions v ON a.Id=v.AssetId AND a.Version=v.Version WHERE a.Kind=$kind AND a.Deleted=0", ("$kind", kind));
            await using var rows = await duplicates.ExecuteReaderAsync(ct);
            ShortVideoAssetRef? duplicate = null;
            while (await rows.ReadAsync(ct))
                if (JsonSerializer.Deserialize<ShortVideoImageInfo>(rows.GetString(2), Json)!.Sha256 == upload.Info.Sha256) { duplicate = new(Guid.Parse(rows.GetString(0)), rows.GetInt32(1)); break; }
            await rows.DisposeAsync();
            if (duplicate is not null) return await FindAsync(db, scope, duplicate, kind, ct);
        }
        using var transaction = db.BeginTransaction();
        var id = replaceId ?? Guid.NewGuid(); var version = replaceId.HasValue ? checked(expectedVersion + 1) : 1;
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        using (var change = replaceId.HasValue
            ? Command(db, "UPDATE Assets SET Name=$name,Version=$v,Updated=$now WHERE Id=$id AND Version=$old AND Kind=$kind AND Deleted=0", ("$name", name), ("$v", version), ("$now", now), ("$id", id.ToString("N")), ("$old", expectedVersion), ("$kind", kind))
            : Command(db, "INSERT INTO Assets VALUES($id,$kind,$name,1,0,$now,$now,$now)", ("$id", id.ToString("N")), ("$kind", kind), ("$name", name), ("$now", now)))
            if (await change.ExecuteNonQueryAsync(ct) != 1) throw new ArgumentException("Ảnh đã thay đổi. Hãy tải lại thư viện.");
        var thumbnail = Thumbnail(upload.Bytes); var thumbnailInfo = ShortVideoWorkflowService.Inspect(thumbnail);
        await WriteBlobAsync(scope, upload.Info, upload.Bytes, ct);
        await WriteBlobAsync(scope, thumbnailInfo, thumbnail, ct);
        using (var insert = Command(db, "INSERT INTO Versions VALUES($id,$v,$image,$thumb)", ("$id", id.ToString("N")), ("$v", version),
            ("$image", JsonSerializer.Serialize(upload.Info, Json)), ("$thumb", JsonSerializer.Serialize(thumbnailInfo, Json)))) await insert.ExecuteNonQueryAsync(ct);
        transaction.Commit();
        return await FindAsync(db, scope, new(id, version), kind, ct);
    }, ct);
    private static byte[] Thumbnail(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var image = Image.FromStream(stream);
        var scale = Math.Min(1d, 256d / Math.Max(image.Width, image.Height));
        using var bitmap = new Bitmap(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(image, 0, 0, bitmap.Width, bitmap.Height);
        }
        using var output = new MemoryStream(); bitmap.Save(output, ImageFormat.Png); return output.ToArray();
    }
    private string BlobPath(string scope, ShortVideoImageInfo info)
    {
        if (info.Sha256.Length != 64 || !info.Sha256.All(Uri.IsHexDigit) || info.MimeType is not ("image/png" or "image/jpeg")) throw new InvalidDataException("Ảnh thư viện không hợp lệ.");
        return SafePath(scope, Path.Combine("images", info.Sha256.ToLowerInvariant() + ".image"));
    }
    private async Task WriteBlobAsync(string scope, ShortVideoImageInfo info, byte[] bytes, CancellationToken ct)
    {
        ShortVideoWorkflowService.CheckImage(bytes, info);
        var destination = BlobPath(scope, info);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, ct);
            ShortVideoWorkflowService.CheckImage(await ShortVideoWorkflowService.ReadBoundedAsync(temporary, ct), info);
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal async Task ClearTransientAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { _uploads.Clear(); } finally { _gate.Release(); }
    }
    private async Task<byte[]> ReadBlobAsync(string scope, ShortVideoImageInfo info, CancellationToken ct)
    {
        var bytes = await ShortVideoWorkflowService.ReadBoundedAsync(BlobPath(scope, info), ct);
        ShortVideoWorkflowService.CheckImage(bytes, info); return bytes;
    }
    internal Task<ShortVideoLibraryAsset> RenameAsync(string user, Guid org, Guid id, int version, string name, CancellationToken ct) => InScopeAsync(user, org, async (db, scope) =>
    {
        name = Name(name);
        using var update = Command(db, "UPDATE Assets SET Name=$name,Updated=$now WHERE Id=$id AND Version=$v AND Deleted=0", ("$name", name), ("$now", DateTime.UtcNow.ToString("O")), ("$id", id.ToString("N")), ("$v", version));
        if (await update.ExecuteNonQueryAsync(ct) != 1) throw new ArgumentException("Ảnh đã thay đổi. Hãy tải lại thư viện.");
        using var kind = Command(db, "SELECT Kind FROM Assets WHERE Id=$id", ("$id", id.ToString("N")));
        return await FindAsync(db, scope, new(id, version), (string)(await kind.ExecuteScalarAsync(ct))!, ct);
    }, ct);
    internal Task<bool> DeleteAsync(string user, Guid org, Guid id, int version, CancellationToken ct) => InScopeAsync(user, org, async (db, scope) =>
    {
        using var update = Command(db, "UPDATE Assets SET Deleted=1 WHERE Id=$id AND Version=$v AND Deleted=0", ("$id", id.ToString("N")), ("$v", version));
        if (await update.ExecuteNonQueryAsync(ct) != 1) throw new ArgumentException("Ảnh đã thay đổi. Hãy tải lại thư viện.");
        return true;
    }, ct);
    internal Task<(ShortVideoLibraryAsset Asset, byte[] Bytes)> ReadAsync(string user, Guid org, ShortVideoAssetRef reference, string kind, CancellationToken ct) => InScopeAsync(user, org, async (db, scope) =>
    {
        var asset = await FindAsync(db, scope, reference, kind, ct);
        var bytes = await ReadBlobAsync(scope, asset.Image, ct);
        using var used = Command(db, "UPDATE Assets SET Used=$now WHERE Id=$id", ("$id", reference.AssetId.ToString("N")), ("$now", DateTime.UtcNow.ToString("O")));
        await used.ExecuteNonQueryAsync(ct);
        return (asset, bytes);
    }, ct);

    internal Task<(byte[] Bytes, string Mime)> OpenPreviewAsync(string user, Guid org, Uri uri, CancellationToken ct) => InScopeAsync(user, org, async (db, scope) =>
    {
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (uri.Scheme != "https" || uri.Host != HostName || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || parts.Length != 4 || parts[0] != scope ||
            !Guid.TryParseExact(parts[1], "N", out var id) || !int.TryParse(parts[2], out var version)) throw new ArgumentException("Ảnh không thuộc phiên hiện hành.");
        if (parts[3] == "upload" && version == 0 && _uploads.TryGetValue(id, out var upload) && upload.Scope == scope && upload.Expires > DateTime.UtcNow)
            return (upload.Bytes, upload.Info.MimeType);
        if (parts[3] is not ("thumb" or "image") || version < 1) throw new ArgumentException("Ảnh không hợp lệ.");
        using var query = Command(db, parts[3] == "thumb" ? "SELECT Thumbnail FROM Versions WHERE AssetId=$id AND Version=$v" : "SELECT Image FROM Versions WHERE AssetId=$id AND Version=$v", ("$id", id.ToString("N")), ("$v", version));
        var json = await query.ExecuteScalarAsync(ct) as string ?? throw new ArgumentException("Không tìm thấy ảnh.");
        var info = JsonSerializer.Deserialize<ShortVideoImageInfo>(json, Json)!;
        return (await ReadBlobAsync(scope, info, ct), info.MimeType);
    }, ct);
    private static string DraftKey(Guid? project) => project?.ToString("N") ?? "new";
    private static async Task<ShortVideoDraftState> DraftAsync(SqliteConnection db, Guid? project, CancellationToken ct)
    {
        using var query = Command(db, "SELECT Revision,Content,CreatedProjectId FROM Drafts WHERE Id=$id", ("$id", DraftKey(project)));
        await using var row = await query.ExecuteReaderAsync(ct);
        return await row.ReadAsync(ct) ? new(row.GetInt32(0), JsonSerializer.Deserialize<ShortVideoDraft>(row.GetString(1), Json), row.IsDBNull(2) ? null : Guid.Parse(row.GetString(2))) : new(0, null);
    }
    internal static void ValidateDraft(ShortVideoDraft draft)
    {
        if (draft.Content is null || draft.Content.Length > 2000 || draft.Background is null || draft.Background.Length > 1500 || draft.Motion is null || draft.Motion.Length > 2000 ||
            draft.AspectRatio is not ("9:16" or "16:9" or "1:1") || draft.DurationSeconds is < 4 or > 15 || draft.ServerRevision < 0)
            throw new ArgumentException("Nội dung hoặc thiết lập video không hợp lệ.");
    }
    internal Task<ShortVideoDraftState> SaveDraftAsync(string user, Guid org, Guid? project, int revision, ShortVideoDraft draft, CancellationToken ct) => InScopeAsync(user, org, async (db, scope) =>
    {
        ValidateDraft(draft);
        if (draft.Character is { } character) await FindAsync(db, scope, character, "Character", ct);
        if (draft.Outfit is { } outfit) await FindAsync(db, scope, outfit, "Outfit", ct);
        using var tx = db.BeginTransaction();
        var current = await DraftAsync(db, project, ct);
        if (current.Revision != revision) throw new ArgumentException("Bản nháp đã thay đổi ở cửa sổ khác. Hãy tải lại trạng thái.");
        if (current.CreatedProjectId.HasValue) throw new ArgumentException("Bản nháp đang được chuyển thành dự án. Hãy tiếp tục lưu dự án hoặc mở dự án đã tạo.");
        using var save = Command(db, "INSERT INTO Drafts(Id,Revision,Content) VALUES($id,$revision,$json) ON CONFLICT(Id) DO UPDATE SET Revision=$revision,Content=$json",
            ("$id", DraftKey(project)), ("$revision", revision + 1), ("$json", JsonSerializer.Serialize(draft, Json)));
        await save.ExecuteNonQueryAsync(ct); tx.Commit(); return new ShortVideoDraftState(revision + 1, draft);
    }, ct);
    internal Task<ShortVideoDraftState> ReserveProjectAsync(string user, Guid org, int revision, CancellationToken ct) => InScopeAsync(user, org, async (db, scope) =>
    {
        using var tx = db.BeginTransaction(); var current = await DraftAsync(db, null, ct);
        if (current.Draft is null || current.Revision != revision) throw new ArgumentException("Hãy lưu bản nháp mới nhất trước.");
        var id = current.CreatedProjectId ?? Guid.NewGuid();
        using var update = Command(db, "UPDATE Drafts SET CreatedProjectId=$project WHERE Id='new'", ("$project", id.ToString("N")));
        await update.ExecuteNonQueryAsync(ct); tx.Commit(); return current with { CreatedProjectId = id };
    }, ct);
    internal Task<bool> CompleteProjectAsync(string user, Guid org, Guid project, ShortVideoDraft draft, CancellationToken ct) => InScopeAsync(user, org, async (db, scope) =>
    {
        using var tx = db.BeginTransaction(); var current = await DraftAsync(db, null, ct);
        if (current.CreatedProjectId != project) throw new ArgumentException("Bản nháp đã đổi dự án.");
        using var save = Command(db, "INSERT INTO Drafts(Id,Revision,Content) VALUES($id,1,$json) ON CONFLICT(Id) DO NOTHING; DELETE FROM Drafts WHERE Id='new' AND CreatedProjectId=$id",
            ("$id", project.ToString("N")), ("$json", JsonSerializer.Serialize(draft, Json)));
        await save.ExecuteNonQueryAsync(ct); tx.Commit(); return true;
    }, ct);
}
