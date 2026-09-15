using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TOOL_LOCAL.Vietsub.Voice;

internal sealed partial class VietsubVoiceTimelineRenderer
{
    private const long SegmentMilliseconds = 120_000;
    private const long FilterContextMilliseconds = 1000;
    private const long MaximumSegmentCacheBytes = 4L * 1024 * 1024 * 1024;
    private sealed record SegmentReceipt(string Sha256, long SizeBytes, long DurationMilliseconds);

    private async Task<VietsubVoiceTimelineRenderResult> RenderSegmentsAsync(Guid projectId, Guid jobId,
        Guid trackId, int revision, IReadOnlyList<VietsubVoicePhraseAudio> audio,
        IReadOnlyList<VietsubVoiceTimingDiagnostic> diagnostics, long duration, CancellationToken token,
        Func<int, int, CancellationToken, Task>? progress)
    {
        // One complete timeline, cached segments and bounded working files, independent of phrase count.
        EnsureDiskSpace(projectId, duration + SegmentMilliseconds, 3);
        var temp = paths.GetProjectPath(projectId, "temp", $"voice-{jobId:N}");
        var cache = paths.GetProjectPath(projectId, "cache", "voice-segments-v2");
        var output = paths.GetProjectPath(projectId, "voice", trackId.ToString("N"), $"revision-{revision}");
        Directory.CreateDirectory(temp); Directory.CreateDirectory(cache); Directory.CreateDirectory(output);
        var byId = diagnostics.ToDictionary(value => value.PhraseId, StringComparer.Ordinal);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var partial = Path.Combine(temp, "timeline.partial.wav");
        var segmentCount = (int)((duration + SegmentMilliseconds - 1) / SegmentMilliseconds);
        Task ReportAsync(long end) => progress?.Invoke((int)((end + SegmentMilliseconds - 1) / SegmentMilliseconds), segmentCount, token)
            ?? Task.CompletedTask;
        try
        {
            foreach (var item in audio)
            {
                token.ThrowIfCancellationRequested();
                // Bind cached segments to the actual source bytes, including historical phrase reuse.
                if (!hashes.ContainsKey(item.AbsolutePath)) hashes.Add(item.AbsolutePath, await FileHashAsync(item.AbsolutePath, token));
            }
            await using (var destination = new FileStream(partial, FileMode.Create, FileAccess.Write,
                FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                WritePcmHeader(destination, duration);
                for (long start = 0; start < duration; start += SegmentMilliseconds)
                {
                    token.ThrowIfCancellationRequested();
                    var end = Math.Min(duration, start + SegmentMilliseconds);
                    var expandedStart = Math.Max(0, start - FilterContextMilliseconds);
                    var expandedEnd = Math.Min(duration, end + FilterContextMilliseconds);
                    var selected = audio.Where(item => item.Phrase.StartMilliseconds < expandedEnd
                        && item.Phrase.StartMilliseconds + RenderedDuration(item.Metadata, byId[item.Phrase.PhraseId].Tempo) > expandedStart).ToArray();
                    if (selected.Length == 0)
                    {
                        await WriteSilenceAsync(destination, (end - start) * 192, token);
                        await ReportAsync(end);
                        continue;
                    }
                    var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        Version = 2, Start = expandedStart, End = expandedEnd,
                        Inputs = selected.Select(item => new { item.Phrase.StartMilliseconds, item.Metadata,
                            item.TailFadeMilliseconds, Tempo = byId[item.Phrase.PhraseId].Tempo, Hash = hashes[item.AbsolutePath] })
                    }))).ToLowerInvariant();
                    var cached = Path.Combine(cache, fingerprint + ".wav");
                    used.Add(cached);
                    if (!await IsSegmentValidAsync(cached, expandedEnd - expandedStart, token))
                    {
                        EnsureDiskSpace(projectId, expandedEnd - expandedStart,
                            (selected.Length + MaximumInputsPerPartition - 1) / MaximumInputsPerPartition + 3);
                        var working = new List<string>();
                        foreach (var partition in selected.Chunk(MaximumInputsPerPartition))
                        {
                            var stem = Path.Combine(temp, $"stem-{Guid.NewGuid():N}.wav");
                            await RenderPartitionAsync(partition, byId, expandedEnd - expandedStart,
                                stem, token, expandedStart);
                            _ = VietsubWavInspector.InspectTimeline(stem, expandedEnd - expandedStart, token);
                            working.Add(stem);
                        }
                        // Hierarchical mixes also bound command size when many phrases overlap.
                        while (working.Count > 1)
                        {
                            var next = new List<string>();
                            foreach (var group in working.Chunk(MaximumInputsPerPartition))
                            {
                                if (group.Length == 1) { next.Add(group[0]); continue; }
                                var mixed = Path.Combine(temp, $"mix-{Guid.NewGuid():N}.wav");
                                await MixStemsAsync(group, expandedEnd - expandedStart, mixed, token);
                                _ = VietsubWavInspector.InspectTimeline(mixed, expandedEnd - expandedStart, token);
                                foreach (var old in group) File.Delete(old);
                                next.Add(mixed);
                            }
                            working = next;
                        }
                        var receipt = new SegmentReceipt(await FileHashAsync(working[0], token),
                            new FileInfo(working[0]).Length, expandedEnd - expandedStart);
                        File.Move(working[0], cached, overwrite: true);
                        var receiptPartial = Path.Combine(temp, "receipt.partial.json");
                        await File.WriteAllTextAsync(receiptPartial, JsonSerializer.Serialize(receipt), token);
                        File.Move(receiptPartial, cached + ".json", overwrite: true);
                        PruneSegmentCache(cache, used);
                    }
                    var layout = VietsubWavInspector.ReadTimelineLayout(cached, expandedEnd - expandedStart, token);
                    await using var source = new FileStream(cached, FileMode.Open, FileAccess.Read, FileShare.Read,
                        1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    source.Position = layout.DataOffset + (start - expandedStart) * 192;
                    await CopyBytesAsync(source, destination, (end - start) * 192, token);
                    await ReportAsync(end);
                }
                await destination.FlushAsync(token);
            }
            var metadata = VietsubWavInspector.InspectTimeline(partial, duration, token);
            token.ThrowIfCancellationRequested();
            var final = Path.Combine(output, $"voice-timeline-{Guid.NewGuid():N}.wav");
            File.Move(partial, final);
            PruneSegmentCache(cache, used);
            return new(final, metadata, diagnostics);
        }
        finally { TryDeleteDirectory(temp); }
    }

    private static async Task<bool> IsSegmentValidAsync(string path, long duration, CancellationToken token)
    {
        try
        {
            var receiptFile = new FileInfo(path + ".json");
            if (!receiptFile.Exists || receiptFile.Length > 1024) return false;
            var receipt = JsonSerializer.Deserialize<SegmentReceipt>(await File.ReadAllTextAsync(receiptFile.FullName, token));
            if (receipt is null || receipt.DurationMilliseconds != duration || !File.Exists(path)
                || new FileInfo(path).Length != receipt.SizeBytes || receipt.Sha256 is not { Length: 64 }) return false;
            _ = VietsubWavInspector.InspectTimeline(path, duration, token);
            return string.Equals(receipt.Sha256, await FileHashAsync(path, token), StringComparison.Ordinal);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or VietsubVoiceException)
        { return false; }
    }

    private static async Task<string> FileHashAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
    }

    private static void WritePcmHeader(Stream stream, long duration)
    {
        var bytes = checked((uint)(duration * 192));
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8); writer.Write(checked(bytes + 36)); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)2);
        writer.Write(48000); writer.Write(192000); writer.Write((ushort)4); writer.Write((ushort)16);
        writer.Write("data"u8); writer.Write(bytes);
    }

    private static async Task CopyBytesAsync(Stream source, Stream destination, long bytes, CancellationToken token)
    {
        var buffer = new byte[1024 * 1024];
        while (bytes > 0)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(bytes, buffer.Length)), token);
            if (read == 0) throw new EndOfStreamException("Segment PCM bị cắt ngắn.");
            await destination.WriteAsync(buffer.AsMemory(0, read), token);
            bytes -= read;
        }
    }

    private static async Task WriteSilenceAsync(Stream destination, long bytes, CancellationToken token)
    {
        var buffer = new byte[1024 * 1024];
        while (bytes > 0)
        {
            var count = (int)Math.Min(bytes, buffer.Length);
            await destination.WriteAsync(buffer.AsMemory(0, count), token);
            bytes -= count;
        }
    }

    private static void PruneSegmentCache(string cache, HashSet<string> used)
    {
        try
        {
            var files = new DirectoryInfo(cache).GetFiles("*.wav");
            var total = files.Sum(file => file.Length);
            foreach (var file in files.OrderBy(file => file.LastWriteTimeUtc))
            {
                if (total <= MaximumSegmentCacheBytes) break;
                if (used.Contains(file.FullName)) continue;
                total -= file.Length;
                file.Delete();
                File.Delete(file.FullName + ".json");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
