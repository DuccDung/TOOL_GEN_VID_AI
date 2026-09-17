using System.IO.Compression;
using System.Security.Cryptography;

namespace TOOL_LOCAL.Vietsub.Translation;

internal sealed class VietsubTranslationCudaInstaller
{
    internal sealed record Archive(string Url, long Size, string Sha256, string Prefix);
    internal static readonly Archive[] Archives =
    [
        new("https://api.nuget.org/v3-flatcontainer/llamasharp.backend.cuda12.windows/0.27.0/llamasharp.backend.cuda12.windows.0.27.0.nupkg",
            224196120, "0a3302a2014d378ee295941e72f39e688768917b98972ea4f5f14e7974513c03", "LLamaSharpRuntimes/win-x64/native/cuda12/"),
        new("https://developer.download.nvidia.com/compute/cuda/redist/libcublas/windows-x86_64/libcublas-windows-x86_64-12.4.5.8-archive.zip",
            391538487, "698140f12da055a3709eee2e022fcfe7bc8edf31f30115e3f7a5c877a9491de5", "libcublas-windows-x86_64-12.4.5.8-archive/bin/"),
        new("https://developer.download.nvidia.com/compute/cuda/redist/cuda_cudart/windows-x86_64/cuda_cudart-windows-x86_64-12.4.127-archive.zip",
            2474721, "6a1c32e68ee1a95ca17334691ff9ad1ffe7f352c24a083d55e4c96b8063b2bcb", "cuda_cudart-windows-x86_64-12.4.127-archive/bin/")
    ];

    public async Task InstallAsync(string componentsRoot,
        IProgress<VietsubTranslationRuntimeInstallProgress>? progress, CancellationToken ct)
    {
        var target = VietsubTranslationCudaPack.DirectoryPath(componentsRoot);
        try { VietsubTranslationCudaPack.Verify(target); return; }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { }
        ct.ThrowIfCancellationRequested();
        var parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        if (new DriveInfo(Path.GetPathRoot(parent)!).AvailableFreeSpace < 2500L * 1024 * 1024)
            throw new VietsubTranslationException(VietsubTranslationErrorCodes.RuntimeInvalid,
                "Cần ít nhất 2,5 GB trống để cài gói tăng tốc NVIDIA.");
        var staging = Path.Combine(parent, $".cuda-{Guid.NewGuid():N}");
        var backup = target + $".previous-{Guid.NewGuid():N}";
        Directory.CreateDirectory(staging);
        try
        {
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                { Timeout = TimeSpan.FromMinutes(30) };
            long downloaded = 0;
            foreach (var archive in Archives)
            {
                var part = Path.Combine(staging, "download.part");
                using var response = await http.GetAsync(archive.Url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is { } size && size != archive.Size)
                    throw new InvalidDataException("Gói CUDA có kích thước tải không khớp.");
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType is not ("application/octet-stream" or "application/zip" or "application/x-zip-compressed"))
                    throw new InvalidDataException("Gói CUDA có MIME không hợp lệ.");
                await using (var source = await response.Content.ReadAsStreamAsync(ct))
                await using (var destination = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[1024 * 1024];
                    long count = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct)) != 0)
                    {
                        count += read;
                        if (count > archive.Size) throw new InvalidDataException("Gói CUDA vượt kích thước đã ghim.");
                        await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                        progress?.Report(new("DOWNLOADING_CUDA", 80d * (downloaded + count) / Archives.Sum(a => a.Size),
                            "Đang tải gói tăng tốc NVIDIA tùy chọn (khoảng 618 MB).", downloaded + count, Archives.Sum(a => a.Size)));
                    }
                    if (count != archive.Size) throw new InvalidDataException("Gói CUDA tải chưa đầy đủ.");
                }
                await ExtractVerifiedAsync(part, archive, staging, ct);
                File.Delete(part);
                downloaded += archive.Size;
            }
            VietsubTranslationCudaPack.Verify(staging);
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(target)) Directory.Move(target, backup);
            try { Directory.Move(staging, target); }
            catch { if (Directory.Exists(backup)) Directory.Move(backup, target); throw; }
            if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }

    internal static async Task ExtractVerifiedAsync(string archivePath, Archive definition, string destination, CancellationToken ct)
    {
        await using var file = File.OpenRead(archivePath);
        if (file.Length != definition.Size
            || !Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).Equals(definition.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Gói CUDA sai SHA-256; không giải nén.");
        file.Position = 0;
        if (file.ReadByte() != 'P' || file.ReadByte() != 'K') throw new InvalidDataException("Gói CUDA sai signature ZIP.");
        file.Position = 0;
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        foreach (var name in VietsubTranslationCudaPack.NativeHashes.Keys)
        {
            var entryName = name == "LICENSE-NVIDIA.txt"
                ? definition.Prefix.Replace("bin/", "", StringComparison.Ordinal) + "LICENSE"
                : definition.Prefix + name;
            var matches = zip.Entries.Where(e => e.FullName == entryName).ToArray();
            if (matches.Length == 0) continue;
            if (matches.Length != 1 || matches[0].Length > 600L * 1024 * 1024)
                throw new InvalidDataException("Gói CUDA có entry trùng hoặc quá lớn.");
            var path = Path.Combine(destination, name); // only constant filenames, never archive paths
            await using var input = matches[0].Open();
            await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[1024 * 1024];
            long written = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) != 0)
            {
                written += read;
                if (written > matches[0].Length) throw new InvalidDataException("Gói CUDA giải nén vượt giới hạn.");
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
    }
}
