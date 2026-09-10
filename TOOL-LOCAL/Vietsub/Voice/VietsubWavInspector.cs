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

    public static VietsubWavMetadata Inspect(string path, bool analyzeSilence)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 44)
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.ResultInvalid, "Worker không tạo được file WAV hợp lệ.");
        }

        using var stream = info.OpenRead();
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (ReadFourCc(reader) != "RIFF" || reader.ReadUInt32() + 8 > stream.Length || ReadFourCc(reader) != "WAVE")
        {
            throw InvalidWav();
        }

        ushort format = 0;
        ushort channels = 0;
        int sampleRate = 0;
        ushort blockAlign = 0;
        ushort bitsPerSample = 0;
        long dataOffset = -1;
        int dataSize = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            var chunkId = ReadFourCc(reader);
            var chunkSize = reader.ReadUInt32();
            if (chunkSize > MaximumDataBytes || stream.Position + chunkSize > stream.Length)
            {
                throw InvalidWav();
            }
            if (chunkId == "fmt ")
            {
                if (chunkSize < 16) throw InvalidWav();
                format = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadInt32();
                _ = reader.ReadInt32();
                blockAlign = reader.ReadUInt16();
                bitsPerSample = reader.ReadUInt16();
                stream.Position += chunkSize - 16;
            }
            else if (chunkId == "data")
            {
                dataOffset = stream.Position;
                dataSize = checked((int)chunkSize);
                stream.Position += chunkSize;
            }
            else
            {
                stream.Position += chunkSize;
            }
            if ((chunkSize & 1) != 0 && stream.Position < stream.Length) stream.Position++;
        }

        if (format != 1 || channels is < 1 or > 2 || sampleRate is < 8_000 or > 192_000
            || bitsPerSample != 16 || blockAlign != channels * 2 || dataOffset < 0 || dataSize < blockAlign)
        {
            throw InvalidWav();
        }

        var totalFrames = dataSize / blockAlign;
        var duration = Math.Max(1, (long)Math.Round(totalFrames * 1000d / sampleRate));
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
