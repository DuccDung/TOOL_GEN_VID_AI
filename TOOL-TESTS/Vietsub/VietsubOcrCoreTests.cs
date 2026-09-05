using TOOL_LOCAL.Vietsub.Ocr;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubOcrCoreTests
{
    [Fact]
    public void CueAccumulator_MergesSmallChanges_AndDropsLowConfidenceFlash()
    {
        var accumulator = new VietsubOcrCueAccumulator(500);
        accumulator.Add(0, "Hello   world", 0.9f);
        accumulator.Add(500, "Hello wor1d", 0.85f);
        accumulator.Add(1000, "Noise", 0.5f);
        accumulator.Add(1500, string.Empty, 0);

        var cues = accumulator.Complete();

        var cue = Assert.Single(cues);
        Assert.Equal(0, cue.StartMilliseconds);
        Assert.Equal(1000, cue.EndMilliseconds);
        Assert.Equal("Hello wor1d", cue.OriginalText);
    }

    [Fact]
    public void ChangeTracker_WaitsForStableChange_AndForcesSafetyRefresh()
    {
        var first = new byte[32];
        first[5] = 1;
        var second = new byte[32];
        second[20] = 1;
        var tracker = new VietsubOcrFrameChangeTracker(2, 0.015);

        Assert.Equal(VietsubOcrFrameDecisionKind.Hold, tracker.Analyze(first, 0).Kind);
        var initial = tracker.Analyze(first, 250);
        Assert.Equal(VietsubOcrFrameDecisionKind.Recognize, initial.Kind);
        Assert.Equal(0, initial.TimestampMilliseconds);
        Assert.Equal(VietsubOcrFrameDecisionKind.Reuse, tracker.Analyze(first, 500).Kind);
        Assert.Equal(VietsubOcrFrameDecisionKind.Hold, tracker.Analyze(second, 750).Kind);
        var changed = tracker.Analyze(second, 1000);
        Assert.Equal(VietsubOcrFrameDecisionKind.Recognize, changed.Kind);
        Assert.Equal(750, changed.TimestampMilliseconds);
        Assert.Equal(VietsubOcrFrameDecisionKind.Reuse, tracker.Analyze(second, 1250).Kind);
        Assert.Equal(VietsubOcrFrameDecisionKind.Reuse, tracker.Analyze(second, 1500).Kind);
        Assert.Equal(VietsubOcrFrameDecisionKind.Recognize, tracker.Analyze(second, 1750).Kind);
    }

    [Fact]
    public void BuildSignature_RejectsIncorrectBgr24Length()
    {
        Assert.Throws<ArgumentException>(() => VietsubOcrFrameChangeTracker.BuildSignature(
            new VietsubRawVideoFrame(0, 0, 100, 50, new byte[10])));
    }

    [Fact]
    public void CueAccumulator_RestoresPendingCue_AndIgnoresResumeOverlap()
    {
        var firstRun = new VietsubOcrCueAccumulator(250);
        firstRun.Add(10_000, "A long subtitle", 0.9f);
        firstRun.Add(10_250, "A long subtitle", 0.9f);
        firstRun.Add(10_500, "A long subtitle", 0.9f);

        var restored = VietsubOcrCueAccumulator.Restore(250, firstRun.Snapshot());
        restored.Add(10_000, "duplicate overlap", 0.99f);
        restored.Add(10_250, "duplicate overlap", 0.99f);
        restored.Add(10_750, "A long subtitle", 0.9f);
        restored.Add(11_000, string.Empty, 0);

        var cue = Assert.Single(restored.Complete());
        Assert.Equal(10_000, cue.StartMilliseconds);
        Assert.Equal(11_000, cue.EndMilliseconds);
        Assert.Equal("A long subtitle", cue.OriginalText);
    }

    [Fact]
    public void CueAccumulator_RejectsCorruptedPendingSnapshot()
    {
        var snapshot = new VietsubOcrAccumulatorSnapshot(
            1_000,
            "subtitle",
            1_000,
            900,
            0.9f,
            1);

        Assert.Throws<ArgumentException>(() => VietsubOcrCueAccumulator.Restore(250, snapshot));
    }
}
