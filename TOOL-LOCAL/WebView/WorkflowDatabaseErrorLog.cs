using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace TOOL_LOCAL.WebView;

internal static class WorkflowDatabaseErrorLog
{
    private static readonly object Sync = new();
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private static readonly Regex ConstraintNamePattern = new(
        "\\bconstraint\\s+(?:\"|\\[)(?<name>[A-Za-z0-9_.-]+)(?:\"|\\])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static void Write(DbUpdateException exception, string operationType)
    {
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ToolGenPostVideo",
                "Logs");
            var logPath = Path.Combine(logDirectory, "workflow-database-errors.log");
            var diagnostic = Describe(exception);
            var line = $"{DateTime.UtcNow:O}\tOperation={NormalizeOperation(operationType)}\t{diagnostic}{Environment.NewLine}";

            lock (Sync)
            {
                Directory.CreateDirectory(logDirectory);
                File.AppendAllText(logPath, line, Utf8WithoutBom);
            }
        }
        catch
        {
            // Diagnostics must never replace the original workflow error shown to the user.
        }
    }

    internal static string FormatDiagnostic(IEnumerable<(int Number, string Message)> errors)
    {
        var materialized = errors.ToArray();
        var numbers = materialized
            .Select(x => x.Number)
            .Distinct()
            .Order()
            .Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        var constraints = materialized
            .Select(x => ConstraintNamePattern.Match(x.Message ?? string.Empty))
            .Where(x => x.Success)
            .Select(x => x.Groups["name"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return $"SqlErrorNumbers={(numbers.Length == 0 ? "unknown" : string.Join(',', numbers))}; " +
               $"Constraints={(constraints.Length == 0 ? "unknown" : string.Join(',', constraints))}";
    }

    internal static string NormalizeOperation(string? operationType)
    {
        if (string.IsNullOrWhiteSpace(operationType))
        {
            return "unknown";
        }

        return new string(operationType
            .Take(64)
            .Select(x => char.IsAsciiLetterOrDigit(x) || x is '.' or '-' or '_' ? x : '_')
            .ToArray());
    }

    private static string Describe(DbUpdateException exception)
    {
        Exception? current = exception;
        while (current is not null)
        {
            if (current is SqlException sqlException)
            {
                var errors = new List<(int Number, string Message)>();
                foreach (SqlError error in sqlException.Errors)
                {
                    errors.Add((error.Number, error.Message));
                }
                return FormatDiagnostic(errors);
            }

            current = current.InnerException;
        }

        return FormatDiagnostic([]);
    }
}
