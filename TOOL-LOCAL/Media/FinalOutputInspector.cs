namespace TOOL_LOCAL.Media;

internal sealed record FinalOutputInspection(
    MediaProbeResult Probe,
    AudioQualityResult AudioQuality);

internal interface IFinalOutputInspector
{
    Task<FinalOutputInspection> InspectAsync(string outputPath, CancellationToken cancellationToken);
}

internal sealed class FinalOutputInspector(
    FfprobeService mediaProbe,
    AudioQualityValidator audioQualityValidator) : IFinalOutputInspector
{
    public async Task<FinalOutputInspection> InspectAsync(
        string outputPath,
        CancellationToken cancellationToken)
    {
        var probe = await mediaProbe.ProbeAsync(outputPath, cancellationToken);
        var audioQuality = await audioQualityValidator.AnalyzeAsync(outputPath, cancellationToken);
        return new FinalOutputInspection(probe, audioQuality);
    }
}
