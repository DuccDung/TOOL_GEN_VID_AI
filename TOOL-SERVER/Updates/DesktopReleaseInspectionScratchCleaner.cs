using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Data;

namespace TOOL_SERVER.Updates;

/// <summary>Removes only expired inspection directories owned by this subsystem, after checking database leases.</summary>
public sealed partial class DesktopReleaseInspectionScratchCleaner(IServiceScopeFactory scopes,
    IOptions<DesktopReleaseOptions> options, IWebHostEnvironment environment, TimeProvider clock,
    ILogger<DesktopReleaseInspectionScratchCleaner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = options.Value;
                if (settings.SharedBootstrapperEnabled && DesktopSharedReleaseValidation.IsConfigured(settings))
                {
                    using var scope = scopes.CreateScope();
                    var root = Path.GetFullPath(settings.ScratchRoot, environment.ContentRootPath);
                    var age = TimeSpan.FromSeconds(Math.Clamp(settings.InspectionTimeoutSeconds, 5, 7200)
                        + Math.Clamp(settings.PreparationLeaseSeconds, 10, 3600) + 300);
                    await CleanupAsync(scope.ServiceProvider.GetRequiredService<AccountDbContext>(), root, clock.GetUtcNow().UtcDateTime, age, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            { logger.LogWarning("Desktop inspection cleanup deferred after {ErrorType}.", error.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal static async Task<int> CleanupAsync(AccountDbContext db, string scratchRoot, DateTime now, TimeSpan minimumAge, CancellationToken token)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scratchRoot));
        if (!Directory.Exists(root)) return 0;
        AssertSafeDirectory(root);
        // A failed database read must prevent cleanup rather than assume that leases are absent.
        var active = await db.DesktopReleasePreparations.AsNoTracking().Where(x => x.LeaseOwner != null && x.LeaseExpiresAtUtc > now)
            .Select(x => new { x.PreparationId, x.Generation }).ToListAsync(token);
        var activeNames = active.Select(x => $"{x.PreparationId:N}-{x.Generation}").ToHashSet(StringComparer.Ordinal);
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var removed = 0;
        foreach (var directory in Directory.EnumerateDirectories(root).Take(1000))
        {
            token.ThrowIfCancellationRequested();
            var absolute = Path.GetFullPath(directory);
            if (!absolute.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || Path.GetDirectoryName(absolute) != root) continue;
            var name = Path.GetFileName(absolute);
            var match = OwnedDirectory().Match(name);
            if (!match.Success || activeNames.Contains(match.Groups["identity"].Value)
                || Directory.GetCreationTimeUtc(absolute) > now - minimumAge) continue;
            try
            {
                AssertSafeDirectory(absolute);
                // Only fixed metadata/media targets are extracted by the inspector. Refuse unexpected links.
                if (ContainsReparsePoint(absolute)) continue;
                Directory.Delete(absolute, recursive: true); removed++;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return removed;
    }

    private static void AssertSafeDirectory(string path)
    {
        for (var item = new DirectoryInfo(path); item is not null; item = item.Parent)
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Unsafe inspection storage.");
    }

    private static bool ContainsReparsePoint(string root)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return true;
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
            }
        return false;
    }

    [GeneratedRegex(@"\A(?<identity>[a-f0-9]{32}-[1-9][0-9]*)-[a-f0-9]{32}\z", RegexOptions.CultureInvariant)]
    private static partial Regex OwnedDirectory();
}
