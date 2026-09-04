using TOOL_SERVER.Authentication;

namespace TOOL_SERVER.Generation;

internal sealed record SceneDurationAllocation(
    int ContentDurationSeconds,
    int GenerationDurationSeconds)
{
    public int TailTrimSeconds => GenerationDurationSeconds - ContentDurationSeconds;
}

internal static class VideoDurationAllocator
{
    public static IReadOnlyList<SceneDurationAllocation> Allocate(
        int targetContentDurationSeconds,
        VideoModelCapabilities capabilities)
    {
        if (targetContentDurationSeconds <= 0)
        {
            throw Unsupported(capabilities);
        }

        var allowed = capabilities.AllowedDurationsSeconds
            .Where(duration => duration >= capabilities.MinimumDurationSeconds &&
                               duration <= capabilities.MaximumDurationSeconds)
            .OrderByDescending(duration => duration)
            .ToArray();
        if (allowed.Length == 0)
        {
            throw Unsupported(capabilities);
        }

        // Preserve the historical balanced allocation for providers whose
        // duration catalog is a continuous range (Kling/BytePlus). Sparse
        // catalogs such as Veo 4/6/8 must use the exact-duration allocator
        // below and may require a small tail trim.
        if (IsContinuous(allowed))
        {
            var sceneCount = Math.Max(
                1,
                (int)Math.Ceiling(targetContentDurationSeconds / (decimal)allowed.Max()));
            var baseDuration = targetContentDurationSeconds / sceneCount;
            var remainder = targetContentDurationSeconds % sceneCount;
            var balanced = Enumerable.Range(0, sceneCount)
                .Select(index => baseDuration + (index < remainder ? 1 : 0))
                .ToArray();
            if (balanced.All(duration => allowed.Contains(duration)))
            {
                return balanced
                    .Select(duration => new SceneDurationAllocation(duration, duration))
                    .ToArray();
            }
        }

        var upperBound = checked(targetContentDurationSeconds + allowed.Max());
        int[]? bestCounts = null;
        var bestTotal = 0;
        for (var total = targetContentDurationSeconds; total <= upperBound; total++)
        {
            var counts = FindMinimumClipCounts(total, allowed);
            if (counts is null)
            {
                continue;
            }

            bestCounts = counts;
            bestTotal = total;
            break;
        }

        if (bestCounts is null)
        {
            throw Unsupported(capabilities);
        }

        var generationDurations = new List<int>();
        for (var index = 0; index < allowed.Length; index++)
        {
            generationDurations.AddRange(Enumerable.Repeat(allowed[index], bestCounts[index]));
        }

        var contentDurations = generationDurations.ToArray();
        var remainingTrim = bestTotal - targetContentDurationSeconds;
        var preferredMinimum = Math.Min(
            allowed.Min(),
            Math.Max(1, targetContentDurationSeconds / generationDurations.Count));
        RemoveTail(contentDurations, preferredMinimum, ref remainingTrim);
        RemoveTail(contentDurations, 1, ref remainingTrim);
        if (remainingTrim != 0 || contentDurations.Any(duration => duration <= 0))
        {
            throw Unsupported(capabilities);
        }

        return contentDurations
            .Select((content, index) => new SceneDurationAllocation(content, generationDurations[index]))
            .ToArray();
    }

    private static int[]? FindMinimumClipCounts(int total, IReadOnlyList<int> allowed)
    {
        var minimumClips = Enumerable.Repeat(int.MaxValue, total + 1).ToArray();
        var previousDurationIndex = Enumerable.Repeat(-1, total + 1).ToArray();
        minimumClips[0] = 0;
        for (var sum = 1; sum <= total; sum++)
        {
            for (var index = 0; index < allowed.Count; index++)
            {
                var previous = sum - allowed[index];
                if (previous < 0 || minimumClips[previous] == int.MaxValue)
                {
                    continue;
                }

                var candidate = minimumClips[previous] + 1;
                if (candidate < minimumClips[sum])
                {
                    minimumClips[sum] = candidate;
                    previousDurationIndex[sum] = index;
                }
            }
        }

        if (minimumClips[total] == int.MaxValue)
        {
            return null;
        }

        var counts = new int[allowed.Count];
        for (var sum = total; sum > 0;)
        {
            var index = previousDurationIndex[sum];
            if (index < 0)
            {
                return null;
            }
            counts[index]++;
            sum -= allowed[index];
        }
        return counts;
    }

    private static void RemoveTail(int[] contentDurations, int minimum, ref int remainingTrim)
    {
        for (var index = contentDurations.Length - 1; index >= 0 && remainingTrim > 0; index--)
        {
            var available = Math.Max(0, contentDurations[index] - minimum);
            var trim = Math.Min(available, remainingTrim);
            contentDurations[index] -= trim;
            remainingTrim -= trim;
        }
    }

    private static bool IsContinuous(IReadOnlyCollection<int> allowed) =>
        allowed.Count > 0 && allowed.Max() - allowed.Min() + 1 == allowed.Count;

    private static AccountApiException Unsupported(VideoModelCapabilities capabilities) =>
        new(
            StatusCodes.Status422UnprocessableEntity,
            "video_duration_not_supported",
            $"Không thể chia thời lượng dự án theo các thời lượng provider hỗ trợ: {string.Join(", ", capabilities.AllowedDurationsSeconds.Order())} giây.");
}
