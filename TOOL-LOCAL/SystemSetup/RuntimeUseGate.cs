namespace TOOL_LOCAL.SystemSetup;

// File leases are released by the OS after a crash and can cross async thread boundaries.
// All users of native runtimes acquire this lease before any component-specific semaphore.
internal sealed class RuntimeUseGate(string directory, params string[] componentDirectories)
{
    public static RuntimeUseGate Shared { get; private set; } = new(SystemSetupPaths.MetadataRoot);
    internal static void ConfigureShared(IEnumerable<string> componentDirectories) =>
        Shared = new(SystemSetupPaths.MetadataRoot, componentDirectories.ToArray());
    public IDisposable Acquire(bool exclusive)
    {
        var leases = new List<IDisposable>();
        try
        {
            foreach (var path in componentDirectories.Append(directory).Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
                leases.Add(AcquireAt(path, exclusive));
            return new LeaseSet(leases);
        }
        catch { foreach (var lease in leases) lease.Dispose(); throw; }
    }
    private static IDisposable AcquireAt(string directory, bool exclusive)
    {
        try
        {
            SystemSetupPaths.EnsureSafeDirectory(directory);
            var path = Path.Combine(directory, "runtime.lock");
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Invalid lock file.");
            // Seed the file without truncating another process's lease.
            if (!File.Exists(path))
            {
                try { using var seed = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite); }
                catch (IOException) when (File.Exists(path)) { }
            }
            return new FileStream(path, FileMode.Open, FileAccess.Read,
                exclusive ? FileShare.None : FileShare.Read);
        }
        catch (IOException)
        {
            throw new SetupException("system_setup_busy", "Thành phần local đang được sử dụng hoặc cài đặt. Hãy chờ tác vụ hiện tại kết thúc.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new SetupException("system_setup_storage_denied", "Không thể ghi thư mục Setup của tài khoản Windows hiện tại.");
        }
    }
    private sealed class LeaseSet(List<IDisposable> leases) : IDisposable
    {
        public void Dispose() { foreach (var lease in leases) lease.Dispose(); }
    }
}
