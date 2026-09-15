using System.Globalization;
using TOOL_SERVER.Authentication;
using TOOL_SHARED.Contracts.Publishing;

namespace TOOL_SERVER.Publishing;

internal static class PublishingCalendar
{
    public static void Validate(PublishingScheduleInput value, DateTime now)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.Title) || value.Title.Length > 100 ||
            string.IsNullOrWhiteSpace(value.Description) || value.Description.Length > 1500 ||
            value.CharacterImageId == Guid.Empty || value.ProductImageId == Guid.Empty ||
            value.CharacterImageId == value.ProductImageId || value.DurationSeconds is not (4 or 6 or 8) ||
            value.AspectRatio is not ("9:16" or "16:9") || value.MaximumCostPerRun is <= 0 or > 100 ||
            value.LeadMinutes is < 15 or > 1440 || value.Weekdays is < 1 or > 127 ||
            value.LatePolicy is not ("SameDay" or "Skip") || value.Targets is null || value.Targets.Count is < 1 or > 10)
            throw Error("publishing_invalid_input", "Kiểm tra nội dung, ảnh, giờ tạo, giới hạn chi phí và nơi đăng.");
        if (!DateOnly.TryParseExact(value.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !DateOnly.TryParseExact(value.EndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) ||
            !TimeOnly.TryParseExact(value.PublishTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
            end < start || end.DayNumber - start.DayNumber > 89)
            throw Error("publishing_invalid_dates", "Khoảng lịch tối đa 90 ngày; ngày và giờ phải hợp lệ.");
        _ = Zone(value.TimeZoneId);
        if (start > DateOnly.FromDateTime(now.AddDays(365)) || end < DateOnly.FromDateTime(now.AddDays(-1)))
            throw Error("publishing_invalid_dates", "Chọn ngày hiện tại hoặc trong vòng một năm tới.");
        if (value.Targets.Any(x => x is null || !PublishingPlatforms.IsValid(x.Platform) || x.ConnectionId == Guid.Empty ||
            (x.Platform == PublishingPlatforms.YouTube && x.Privacy is not ("public" or "private" or "unlisted")) ||
            (x.Platform == PublishingPlatforms.Facebook && x.Privacy != "public") ||
            (x.Platform == PublishingPlatforms.TikTok && x.Privacy != "review")) ||
            value.Targets.Select(x => (x.Platform, x.ConnectionId)).Distinct().Count() != value.Targets.Count)
            throw Error("publishing_invalid_targets", "Nơi đăng bị trùng hoặc chưa có thiết lập hợp lệ.");
        if (value.Targets.Any(x => x.Platform == PublishingPlatforms.Facebook) && value.AspectRatio != "9:16")
            throw Error("publishing_facebook_ratio", "Lịch đăng Facebook Reels cần video dọc 9:16.");
    }

    // Weekday mask uses ISO order: Monday=1 ... Sunday=64. EndDate is inclusive.
    public static DateTime? Next(PublishingScheduleInput value, DateTime afterUtc, bool inclusive = false)
    {
        var zone = Zone(value.TimeZoneId);
        var start = DateOnly.ParseExact(value.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = DateOnly.ParseExact(value.EndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var clock = TimeOnly.ParseExact(value.PublishTime, "HH:mm", CultureInfo.InvariantCulture);
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var day = ((int)date.DayOfWeek + 6) % 7;
            if ((value.Weekdays & (1 << day)) == 0) continue;
            var local = date.ToDateTime(clock, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local)) continue; // No invented instant during a DST gap.
            // A repeated wall-clock time produces exactly one occurrence: the earlier instant.
            var utc = zone.IsAmbiguousTime(local)
                ? new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).UtcDateTime
                : TimeZoneInfo.ConvertTimeToUtc(local, zone);
            if (utc > afterUtc || inclusive && utc == afterUtc) return utc;
        }
        return null;
    }

    public static DateTime Deadline(PublishingScheduleInput value, DateTime publishUtc)
    {
        if (value.LatePolicy == "Skip") return publishUtc.AddMinutes(5);
        var zone = Zone(value.TimeZoneId);
        var nextMidnight = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(publishUtc, zone).Date.AddDays(1), DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(nextMidnight)) nextMidnight = nextMidnight.AddMinutes(1);
        return TimeZoneInfo.ConvertTimeToUtc(nextMidnight, zone);
    }

    private static TimeZoneInfo Zone(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100) throw Error("publishing_timezone_invalid", "Chọn múi giờ hợp lệ.");
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw Error("publishing_timezone_invalid", "Múi giờ chưa được hỗ trợ trên server."); }
    }

    internal static AccountApiException Error(string code, string message, int status = 422) => new(status, code, message);
}
