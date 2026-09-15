using System.Security.Cryptography;
using System.Text;

namespace TOOL_LOCAL.Bilibili;

internal static class BilibiliFiles
{
    public static string RequireLocalDirectory(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)
            || path.Length > 240 || path.IndexOf(':', 2) >= 0)
            throw new BilibiliException("bilibili_folder_invalid", "Hãy chọn thư mục trên ổ đĩa máy này với đường dẫn ngắn hơn 240 ký tự.");
        var full = Path.GetFullPath(path);
        for (DirectoryInfo? current = new(full); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new BilibiliException("bilibili_folder_invalid", "Thư mục tải không được đi qua liên kết thư mục.");
        return full;
    }

    public static void RequireRegularFile(string path)
    {
        RequireLocalDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new BilibiliException("bilibili_file_invalid", "Tệp tải về không tồn tại hoặc là liên kết không hợp lệ.");
    }

    public static async Task<string> HashAsync(string path, CancellationToken token)
    {
        RequireRegularFile(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }

    public static string SafeName(string title, string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(title.Normalize(NormalizationForm.FormC)
            .Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).Take(70).ToArray()).Trim(' ', '.');
        if (name.Length == 0) name = "Video";
        if (char.IsHighSurrogate(name[^1])) name = name[..^1];
        // Prefix avoids reserved Windows device names, suffix avoids title collisions.
        return $"Bilibili - {name} [{id[..8]}].mp4";
    }

    public static void RequireDiskSpace(string directory, long bytes)
    {
        var drive = new DriveInfo(Path.GetPathRoot(directory)!);
        if (drive.AvailableFreeSpace < bytes)
            throw new BilibiliException("bilibili_disk_full", "Ổ đĩa không đủ dung lượng trống. Hãy chọn thư mục ở ổ khác hoặc giải phóng dung lượng.");
    }

    public static void CleanStaging(string directory, string jobId)
    {
        // Only this job's known yt-dlp temporary filenames; preserve any unexpected user file.
        if (!Guid.TryParseExact(jobId, "N", out _)) return;
        try
        {
            var stage = RequireLocalDirectory(Path.Combine(directory, ".videomaker-bilibili", jobId));
            if (!Directory.Exists(stage)) return;
            foreach (var file in Directory.EnumerateFiles(stage, "video.*", SearchOption.TopDirectoryOnly))
            {
                RequireRegularFile(file);
                File.Delete(file);
            }
            if (!Directory.EnumerateFileSystemEntries(stage).Any()) Directory.Delete(stage);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (BilibiliException) { }
    }
}
