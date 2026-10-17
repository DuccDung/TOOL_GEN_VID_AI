using TOOL_SHARED.Contracts.Updates;

namespace TOOL_SETUP;

/// <summary>Combines sub-stages into one operation; completion remains a distinct committed event.</summary>
internal sealed class MonotonicSetupProgress(IProgress<DesktopUpdateProgress> target) : IProgress<DesktopUpdateProgress>
{
    private readonly object _gate = new();
    private int _percent;
    public void Report(DesktopUpdateProgress value)
    {
        lock (_gate)
        {
            var maximum = value.Stage == "complete" ? 100 : 99;
            _percent = Math.Max(_percent, Math.Clamp(value.Percent, 0, maximum));
            target.Report(value with { Percent = _percent });
        }
    }
}
