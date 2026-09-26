using Microsoft.Data.Sqlite;
using TOOL_LOCAL.Vietsub.Jobs;

namespace TOOL_TESTS.Vietsub;

internal static class VietsubTestStorage
{
    // Call after the fixture's managers and sessions have stopped. Other stores use
    // non-pooled connections; only the job-store pool needs to release its handles.
    internal static void ClearPools(string workspaceRoot)
    {
        if (!Directory.Exists(workspaceRoot)) return;
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var database in Directory.EnumerateFiles(workspaceRoot, "project.db", options))
        {
            using var connection = VietsubJobStore.CreateConnection(database);
            SqliteConnection.ClearPool(connection);
        }
    }
}
