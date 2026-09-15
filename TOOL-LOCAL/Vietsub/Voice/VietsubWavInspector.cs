using System.Text;

namespace TOOL_LOCAL.Vietsub.Voice;

internal sealed record VietsubWavMetadata(
    long DurationMilliseconds,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    long TrimStartMilliseconds,
    long TrimEndMilliseconds,
    long? SignalEndMilliseconds = null)
{
    public long AudibleDurationMilliseconds => Math.Max(1, TrimEndMilliseconds - TrimStartMilliseconds);
}

internal static class VietsubWavInspector
{
    private const int MaximumDataBytes = 32 * 1024 * 1024;
    internal const long MaximumTimelineMilliseconds = 4 * 60 * 60 * 1000L;

    internal sealed record PcmLayout(long DataOffset, long DataBytes, int SampleRate, int Channels)
    {
        public long DurationMilliseconds => (long)Math.Round(DataBytes * 1000d / (SampleRate * Channels * 2L));
    }

    // Phrase validation remains limited to 32 MiB. Only the renderer's bounded,
    // expected-duration timeline path can admit large PCM data chunks.
    public static VietsubWavMetadata InspectTimeline(string path, long expectedMilliseconds,
        CancellationToken cancellationToken = default)
    {
        var layout = ReadTimelineLayout(path, expectedMilliseconds, cancellationToken);
        return new(layout.DurationMilliseconds, layout.SampleRate, layout.Channels, 16, 0, layout.DurationMilliseconds);
    }

    internal static PcmLayout ReadTimelineLayout(string path, long expectedMilliseconds,
        CancellationToken cancellationToken = default)
    {
        if (expectedMilliseconds is <= 0 or > MaximumTimelineMilliseconds) throw InvalidWav();
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var layout = ReadLayout(reader, checked(expectedMilliseconds * 192), cancellationToken);
        if (layout.SampleRate != 48000 || layout.Channels != 2
            || layout.DataBytes != checked(expectedMilliseconds * 192)) throw InvalidWav();
        return layout;
    }

    public static VietsubWavMetadata Inspect(string path, bool analyzeSilence)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 44)
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.ResultInvalid, "Worker không tạo được file WAV hợp lệ.");
        }

        using var stream = info.OpenRead();
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var layout = ReadLayout(reader, MaximumDataBytes, CancellationToken.None);
        var sampleRate = layout.SampleRate;
        var channels = layout.Channels;
        var bitsPerSample = 16;
        var dataOffset = layout.DataOffset;
        var totalFrames = checked((int)(layout.DataBytes / (channels * 2)));
        var duration = Math.Max(1, layout.DurationMilliseconds);
        if (!analyzeSilence)
        {
            return new(duration, sampleRate, channels, bitsPerSample, 0, duration);
        }

        var peak = 0;
        stream.Position = dataOffset;
        for (var frame = 0; frame < totalFrames; frame++)
        {
            var framePeak = 0;
            for (var channel = 0; channel < channels; channel++)
            {
                var sample = Math.Abs((int)reader.ReadInt16());
                framePeak = Math.Max(framePeak, sample);
            }
            peak = Math.Max(peak, framePeak);
        }

        var threshold = Math.Max(400, (int)Math.Round(peak * 0.02));
        var first = -1;
        var last = -1;
        stream.Position = dataOffset;
        for (var frame = 0; frame < totalFrames; frame++)
        {
            var framePeak = 0;
            for (var channel = 0; channel < channels; channel++)
            {
                framePeak = Math.Max(framePeak, Math.Abs((int)reader.ReadInt16()));
            }
            if (framePeak < threshold) continue;
            if (first < 0) first = frame;
            last = frame;
        }
        if (first < 0 || last < first)
        {
            return new(duration, sampleRate, channels, bitsPerSample, 0, duration);
        }

        // Preserve the unpadded boundary as evidence for shortening only the tail.
        // Unknown/all-quiet audio deliberately has no such evidence.
        var signalEnd = (long)Math.Ceiling((last + 1) * 1000d / sampleRate);
        var leadFrames = sampleRate * 60 / 1000;
        var tailFrames = sampleRate * 120 / 1000;
        first = Math.Max(0, first - leadFrames);
        last = Math.Min(totalFrames - 1, last + tailFrames);
        var trimStart = (long)Math.Floor(first * 1000d / sampleRate);
        var trimEnd = Math.Min(duration, (long)Math.Ceiling((last + 1) * 1000d / sampleRate));
        return new(duration, sampleRate, channels, bitsPerSample, trimStart, Math.Max(trimStart + 1, trimEnd), signalEnd);
    }

    private static string ReadFourCc(BinaryReader reader) => Encoding.ASCII.GetString(reader.ReadBytes(4));

    private static PcmLayout ReadLayout(BinaryReader reader, long maximumDataBytes, CancellationToken token)
    {
        var stream = reader.BaseStream;
        if (stream.Length < 44 || ReadFourCc(reader) != "RIFF"
            || (long)reader.ReadUInt32() + 8 != stream.Length || ReadFourCc(reader) != "WAVE") throw InvalidWav();
        ushort format = 0, channels = 0, blockAlign = 0, bits = 0;
        uint rate = 0, byteRate = 0;
        long dataOffset = -1, dataSize = 0;
        var hasFormat = false;
        var chunks = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            token.ThrowIfCancellationRequested();
            if (++chunks > 128) throw InvalidWav();
            var id = ReadFourCc(reader);
            var size = reader.ReadUInt32();
            var end = checked(stream.Position + size + (size & 1));
            if (end > stream.Length || size > (id == "data" ? maximumDataBytes : 1024 * 1024)) throw InvalidWav();
            if (id == "fmt ")
            {
                if (hasFormat || size < 16) throw InvalidWav();
                hasFormat = true;
                format = reader.ReadUInt16(); channels = reader.ReadUInt16();
                rate = reader.ReadUInt32(); byteRate = reader.ReadUInt32();
                blockAlign = reader.ReadUInt16(); bits = reader.ReadUInt16();
            }
            else if (id == "data")
            {
                if (dataOffset >= 0) throw InvalidWav();
                dataOffset = stream.Position; dataSize = size;
            }
            stream.Position = end;
        }
        if (stream.Position != stream.Length || !hasFormat || format != 1 || channels is < 1 or > 2
            || rate is < 8000 or > 192000 || bits != 16 || blockAlign != channels * 2
            || byteRate != rate * blockAlign || dataOffset < 0 || dataSize < blockAlign
            || dataSize % blockAlign != 0) throw InvalidWav();
        return new(dataOffset, dataSize, (int)rate, channels);
    }

    private static VietsubVoiceException InvalidWav() =>
        new(VietsubVoiceErrorCodes.ResultInvalid, "Định dạng WAV do worker tạo ra không hợp lệ hoặc không được hỗ trợ.");
}

internal static class VietsubVoiceTimelineFitPolicy
{
    public static VietsubVoiceTimingDiagnostic Evaluate(
        VietsubVoicePhrase phrase,
        VietsubWavMetadata wav,
        long? nextPhraseStartMilliseconds,
        VietsubVoiceSettingsSnapshot settings)
    {
        var phraseEnd = phrase.HardEndMilliseconds is { } hardEnd
            ? Math.Min(phrase.EndMilliseconds, hardEnd) : phrase.EndMilliseconds;
        var baseTarget = Math.Max(1, phraseEnd - phrase.StartMilliseconds);
        var availableGap = nextPhraseStartMilliseconds is long nextStart
            ? Math.Max(0, nextStart - phraseEnd)
            : 0;
        var borrowCapacity = Math.Min(settings.MaximumBorrowedGapMilliseconds, availableGap);
        var natural = wav.AudibleDurationMilliseconds;
        var borrowed = natural > baseTarget
            ? Math.Min(borrowCapacity, natural - baseTarget)
            : 0;
        var target = baseTarget + borrowed;

        var requiredTempo = natural <= target ? 1d : natural / (double)target;
        var tempo = Math.Min(settings.MaximumTempo, Math.Max(1, requiredTempo));
        var status = natural <= baseTarget
            ? VietsubVoiceTimingStatuses.Natural
            : requiredTempo > settings.MaximumTempo
                ? VietsubVoiceTimingStatuses.ReviewRequired
                : requiredTempo > 1.0001
                    ? VietsubVoiceTimingStatuses.Compressed
                    : VietsubVoiceTimingStatuses.BorrowedGap;
        var suggested = status == VietsubVoiceTimingStatuses.ReviewRequired
            ? Math.Max(1, (int)Math.Floor(phrase.Text.Length * settings.MaximumTempo / requiredTempo))
            : phrase.Text.Length;
        return new(
            phrase.PhraseId,
            natural,
            target,
            borrowed,
            tempo,
            status,
            suggested);
    }
}
