using System.Collections.Concurrent;
using System.Security.Cryptography;
using TOOL_LOCAL.Vietsub.Domain;

namespace TOOL_LOCAL.Vietsub.Voice;

internal sealed record VietsubVoicePlaybackEntry(
    Guid ProjectId,
    Guid ArtifactId,
    Guid TrackId,
    int TrackRevision,
    string AbsolutePath,
    long SizeBytes,
    string Sha256);

internal sealed class VietsubVoicePlaybackRegistry(
    Func<Guid, Guid, int, bool>? isTrackRevisionCurrent = null)
{
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ArtifactId), VietsubVoicePlaybackEntry> _entries = new();
    private readonly ConcurrentDictionary<string, VerifiedFile> _verified = new(StringComparer.OrdinalIgnoreCase);

    public static string CreateUrl(Guid projectId, Guid artifactId, string sha256) =>
        $"https://{Playback.VietsubMediaPlaybackService.HostName}/projects/{projectId:N}/voice/{artifactId:N}/{sha256.ToLowerInvariant()}.wav";

    public void RegisterCurrent(VietsubVoicePlaybackEntry entry)
    {
        ClearProject(entry.ProjectId);
        _entries[(entry.ProjectId, entry.ArtifactId)] = entry;
    }

    public void ClearProject(Guid projectId)
    {
        foreach (var key in _entries.Keys.Where(key => key.ProjectId == projectId))
        {
            _entries.TryRemove(key, out _);
        }
    }

    public bool TryResolve(
        VietsubProjectManifest activeProject,
        Guid projectId,
        Guid artifactId,
        string sha256,
        out VietsubVoicePlaybackEntry entry)
    {
        entry = null!;
        if (activeProject.ProjectId != projectId
            || activeProject.ActiveSubtitleTrackId is not Guid activeTrackId
            || !_entries.TryGetValue((projectId, artifactId), out var candidate)
            || candidate.TrackId != activeTrackId
            || (isTrackRevisionCurrent is not null
                && !isTrackRevisionCurrent(candidate.ProjectId, candidate.TrackId, candidate.TrackRevision))
            || !FixedHashEquals(candidate.Sha256, sha256)
            || !IsVerified(candidate))
        {
            return false;
        }
        entry = candidate;
        return true;
    }

    private bool IsVerified(VietsubVoicePlaybackEntry entry)
    {
        try
        {
            var file = new FileInfo(entry.AbsolutePath);
            if (!file.Exists || file.Length != entry.SizeBytes || file.Length <= 44) return false;
            if (_verified.TryGetValue(entry.AbsolutePath, out var cached)
                && cached.Length == file.Length
                && cached.LastWriteAtUtc == file.LastWriteTimeUtc)
            {
                return cached.Valid;
            }
            using var stream = file.OpenRead();
            var valid = CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(stream),
                Convert.FromHexString(entry.Sha256));
            _verified[entry.AbsolutePath] = new(file.Length, file.LastWriteTimeUtc, valid);
            return valid;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            return false;
        }
    }

    private static bool FixedHashEquals(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed record VerifiedFile(long Length, DateTime LastWriteAtUtc, bool Valid);
}
