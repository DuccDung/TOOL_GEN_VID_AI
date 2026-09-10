using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.TikTok.Data;

namespace TOOL_SERVER.TikTok;

// Session-owned SQL application locks coordinate instances without holding a SQL
// transaction across provider HTTP calls. The separate connection owns the lease.
internal static class TikTokOperationLock
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> TestLocks = new();

    public static async Task<IAsyncDisposable> AcquireAsync(
        TikTokDbContext db, string resource, CancellationToken cancellationToken)
    {
        var key = "VideoMaker.TikTok:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resource)));
        if (!db.Database.IsSqlServer())
        {
            // Only the test providers use this branch; production uses SQL Server.
            var semaphore = TestLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            if (!await semaphore.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)) throw Busy();
            return new MemoryLease(semaphore);
        }
        var source = db.Database.GetDbConnection();
        // SqlConnection.Clone preserves credentials even after PersistSecurityInfo=false
        // has removed a password from the public ConnectionString getter.
        var connection = (DbConnection)((ICloneable)source).Clone();
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=10000; SELECT @result;";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@resource";
            parameter.Value = key;
            command.Parameters.Add(parameter);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) < 0) throw Busy();
            return new SqlLease(connection, key);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static AccountApiException Busy() => new(409, "tiktok_operation_busy", "Tài khoản TikTok đang được xử lý. Vui lòng thử lại.");
    private sealed class MemoryLease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { semaphore.Release(); return ValueTask.CompletedTask; }
    }
    private sealed class SqlLease(DbConnection connection, string key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (connection.State == ConnectionState.Open)
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "EXEC sys.sp_releaseapplock @Resource=@resource, @LockOwner='Session';";
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@resource";
                    parameter.Value = key;
                    command.Parameters.Add(parameter);
                    await command.ExecuteNonQueryAsync(CancellationToken.None);
                }
            }
            finally { await connection.DisposeAsync(); }
        }
    }
}
