namespace TOOL_LOCAL.Vietsub.Domain;

internal sealed class VietsubAudioMixSettings
{
    public double OriginalVolume { get; set; } = 0.25;

    public double TranslatedVoiceVolume { get; set; } = 1;

    public bool OriginalMuted { get; set; }

    public bool TranslatedVoiceMuted { get; set; }

    public bool AutoDuckOriginal { get; set; } = true;

    public static VietsubAudioMixSettings CreateDefault() => new();

    public VietsubAudioMixSettings Copy() => new()
    {
        OriginalVolume = OriginalVolume,
        TranslatedVoiceVolume = TranslatedVoiceVolume,
        OriginalMuted = OriginalMuted,
        TranslatedVoiceMuted = TranslatedVoiceMuted,
        AutoDuckOriginal = AutoDuckOriginal
    };

    public void Normalize()
    {
        ValidateVolume(OriginalVolume, 0, 1, nameof(OriginalVolume));
        ValidateVolume(TranslatedVoiceVolume, 0, 1.5, nameof(TranslatedVoiceVolume));
    }

    private static void ValidateVolume(double value, double minimum, double maximum, string parameterName)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Âm lượng phải nằm trong khoảng {minimum} đến {maximum}.");
        }
    }
}
