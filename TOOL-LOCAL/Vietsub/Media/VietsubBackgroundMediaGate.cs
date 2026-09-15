namespace TOOL_LOCAL.Vietsub.Media;

// Preview artifacts share one background decode slot across services and projects.
// The job manager separately limits OCR/translation/voice to one heavy local job.
internal static class VietsubBackgroundMediaGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<IDisposable> EnterAsync(CancellationToken token)
    {
        await Gate.WaitAsync(token);
        return new Lease();
    }

    private sealed class Lease : IDisposable
    {
        private int released;
        public void Dispose() { if (Interlocked.Exchange(ref released, 1) == 0) Gate.Release(); }
    }
}
