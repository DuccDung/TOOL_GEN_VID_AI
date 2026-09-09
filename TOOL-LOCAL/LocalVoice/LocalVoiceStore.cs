using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TOOL_LOCAL.Storage;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_LOCAL.LocalVoice;

internal sealed record LocalVoiceSource(Guid OrganizationId, string OwnerId, Guid ProjectId, Guid SceneId,
    Guid CharacterId, Guid GenerationId, Guid MediaAssetId, string RelativePath, string Sha256,
    long DurationMs, string ContextHash)
{
    [JsonIgnore]
    public string Fingerprint => LocalVoiceStore.Hash(JsonSerializer.Serialize(this));
}

internal sealed class LocalVoiceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool IsAnchor { get; set; }
    public LocalVoiceSource Source { get; set; } = null!;
    public Guid? AnchorId { get; set; }
    public string? AnchorFingerprint { get; set; }
    public string RuntimeFingerprint { get; set; } = "";
    public string Status { get; set; } = LocalVoiceStatuses.Preparing;
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
    public string? OutputRelativePath { get; set; }
    public string? OutputSha256 { get; set; }
    public Guid? OutputAssetId { get; set; }
    public bool NativeException { get; set; }
    public string? ReviewReason { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    [JsonIgnore]
    public string Fingerprint => LocalVoiceStore.Hash($"{Id:D}|{Source.Fingerprint}|{RuntimeFingerprint}|{OutputSha256}|{ReviewedAtUtc:O}");
}

internal sealed class LocalVoiceStore(ProjectWorkspaceService workspace)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string DirectoryRelative(Guid projectId) => $"projects/{projectId:N}/local-voice";
    public string JobRelative(Guid projectId, Guid id) => $"{DirectoryRelative(projectId)}/{id:N}";
    public FileStream AcquireProjectLock(Guid projectId)
    {
        var path = Resolve(DirectoryRelative(projectId) + "/operation.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public string Resolve(string relative)
    {
        var path = workspace.Resolve(relative);
        for (var current = new DirectoryInfo(Path.GetDirectoryName(path)!); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Workspace giọng local chứa liên kết thư mục không được phép.");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Media giọng local không được là liên kết.");
        return path;
    }

    public IReadOnlyList<LocalVoiceRecord> List(Guid projectId)
    {
        var dir = Resolve(DirectoryRelative(projectId) + "/index");
        var directory = Path.GetDirectoryName(dir)!;
        if (!Directory.Exists(directory)) return [];
        var records = new List<LocalVoiceRecord>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out var id)) continue;
            var record = Read(projectId, id);
            records.Add(record);
        }
        return records.OrderByDescending(x => x.UpdatedAtUtc).ToArray();
    }

    public LocalVoiceRecord Read(Guid projectId, Guid id)
    {
        var file = Resolve($"{DirectoryRelative(projectId)}/{id:N}.json");
        if (new FileInfo(file).Length > 128 * 1024) throw new InvalidDataException("Checkpoint giọng local quá lớn.");
        var record = JsonSerializer.Deserialize<LocalVoiceRecord>(File.ReadAllText(file), JsonOptions)
            ?? throw new InvalidDataException("Checkpoint giọng local không hợp lệ.");
        if (record.Id != id || record.Source?.ProjectId != projectId)
            throw new InvalidDataException("Checkpoint không khớp project.");
        var sourcePath = record.Source.RelativePath.Replace('\\', '/');
        if (!sourcePath.StartsWith($"projects/{projectId:N}/", StringComparison.OrdinalIgnoreCase) ||
            sourcePath.Split('/').Any(part => part is ".." or "."))
            throw new InvalidDataException("Nguồn checkpoint nằm ngoài project.");
        if (record.OutputRelativePath is not null)
        {
            var expected = record.NativeException ? record.Source.RelativePath :
                JobRelative(projectId, id) + (record.IsAnchor ? "/anchor.wav" : "/converted.mp4");
            if (!string.Equals(record.OutputRelativePath.Replace('\\', '/'), expected.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Kết quả checkpoint không thuộc tác vụ.");
        }
        return record;
    }

    public void Save(LocalVoiceRecord record)
    {
        var file = Resolve($"{DirectoryRelative(record.Source.ProjectId)}/{record.Id:N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        record.UpdatedAtUtc = DateTime.UtcNow;
        var partial = file + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using (var stream = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, record, JsonOptions);
                stream.Flush(true);
            }
            File.Move(partial, file, true);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static async Task<string> FileHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }
    public async Task RequireHashAsync(string relative, string expected, CancellationToken cancellationToken)
    {
        if (!string.Equals(await FileHashAsync(Resolve(relative), cancellationToken), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("File đã thay đổi; cần chuẩn bị lại giọng local từ nguồn hiện hành.");
    }
}
