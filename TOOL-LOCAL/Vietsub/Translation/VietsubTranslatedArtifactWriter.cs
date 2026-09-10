using System.Security.Cryptography;
using System.Text;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Subtitles;

namespace TOOL_LOCAL.Vietsub.Translation;

internal static class VietsubTranslatedArtifactWriter
{
    public static async Task WriteAsync(
        VietsubAppPaths paths,
        VietsubSubtitleStore subtitleStore,
        Guid projectId,
        VietsubSubtitleTrack track,
        CancellationToken cancellationToken)
    {
        var relativePath = Path.Combine(
            "subtitles",
            $"translated-{track.TrackId:N}-r{track.Revision}.srt");
        var absolutePath = paths.GetProjectPath(projectId, relativePath);
        var partialPath = absolutePath + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        var content = VietsubSubtitleService.Serialize(track.Cues, preferTranslation: true);
        var bytes = new UTF8Encoding(false).GetBytes(content);
        try
        {
            await using (var stream = new FileStream(
                partialPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(partialPath, absolutePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(partialPath))
            {
                try
                {
                    File.Delete(partialPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        var now = DateTime.UtcNow;
        var artifact = new VietsubSubtitleArtifact
        {
            ArtifactType = "SRT_TRANSLATED",
            TrackRevision = track.Revision,
            WorkspaceRelativePath = relativePath,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Status = VietsubSubtitleArtifactStatuses.Ready,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        if (!await subtitleStore.TrySaveArtifactAsync(
                projectId,
                track.TrackId,
                track.Revision,
                artifact,
                cancellationToken))
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.TrackChanged,
                "Track đã thay đổi trong lúc xuất SRT dịch; artifact không được công bố.",
                retryable: true);
        }
    }

}
