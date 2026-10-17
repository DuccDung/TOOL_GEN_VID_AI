using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Win32.SafeHandles;
using TOOL_SHARED.Contracts.Updates;

namespace TOOL_SHARED.Distribution.Updates;

public static partial class DesktopMaintenanceProtocol
{
    public const int MaximumFrameBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Json = CreateJson();
    private static JsonSerializerOptions CreateJson()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            foreach (var property in info.Properties.Where(p => p.Name == "supportedCatalogProtocols"))
                property.ShouldSerialize = (_, value) => value is not null && !Equals(value, "3");
        });
        return new(JsonSerializerDefaults.Web)
        { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, MaxDepth = 16, TypeInfoResolver = resolver };
    }

    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        if (bytes.Length is 0 or > MaximumFrameBytes) throw new InvalidDataException("Maintenance message is too large.");
        await stream.WriteAsync(bytes, token);
        await stream.WriteAsync(new byte[] { 10 }, token);
        await stream.FlushAsync(token);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var next = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(next, token) == 0) throw new EndOfStreamException("Maintenance peer disconnected.");
            if (next[0] == 10) break;
            if (buffer.Length >= MaximumFrameBytes) throw new InvalidDataException("Maintenance message is too large.");
            buffer.WriteByte(next[0]);
        }
        using var document = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)),
            new JsonDocumentOptions { MaxDepth = 16 });
        RejectDuplicates(document.RootElement);
        return document.RootElement.Deserialize<T>(Json)
            ?? throw new InvalidDataException("Maintenance message is invalid.");
    }

    public static async Task<object> ReadMaintenanceRequestAsync(Stream stream, CancellationToken token)
    {
        var value = await ReadAsync<JsonElement>(stream, token);
        if (!value.TryGetProperty("schemaVersion", out var schema) || !schema.TryGetInt32(out var version))
            throw new InvalidDataException("Maintenance schema is missing.");
        return version switch
        {
            1 => value.Deserialize<DesktopMaintenanceRequest>(Json) ?? throw new InvalidDataException("Maintenance request is invalid."),
            2 => value.Deserialize<DesktopDirectMaintenanceRequest>(Json) ?? throw new InvalidDataException("Direct maintenance request is invalid."),
            3 => value.Deserialize<DesktopStandaloneMaintenanceRequest>(Json) ?? throw new InvalidDataException("Standalone maintenance request is invalid."),
            _ => throw new InvalidDataException("Maintenance schema is unsupported.")
        };
    }

    public static void ValidateRequest(DesktopDirectMaintenanceRequest request, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Release?.SharedBootstrapper is not { } setup) throw new InvalidDataException("Direct maintenance catalog is missing.");
        DesktopDirectCatalogPolicy.ValidateRelease(request.Release, new Uri(setup.ApiBaseUrl));
        if (request.SchemaVersion != 2 || !IsHex(request.OperationId, 32) || !IsHex(request.Nonce, 64) ||
            request.Action is not ("update" or "repair") || request.ParentProcessId <= 0 || request.ParentStartTimeUtcTicks <= 0 ||
            string.IsNullOrWhiteSpace(request.UserSid) || request.UserSid.Length > 200 ||
            !DesktopReleaseVersionPolicy.IsSupportedStableVersion(request.CurrentVersion) || request.CurrentBuildNumber <= 0 || !IsHash(request.CurrentPackageSha256) ||
            request.CreatedAtUtc is null || request.CreatedAtUtc.Value.Kind != DateTimeKind.Utc ||
            request.CreatedAtUtc > nowUtc.AddSeconds(5) || request.CreatedAtUtc < nowUtc.AddMinutes(-1) ||
            string.IsNullOrWhiteSpace(request.TargetRoot) || request.TargetRoot.Length > 160 || request.TargetRoot.Any(char.IsControl) ||
            !Path.IsPathFullyQualified(request.TargetRoot) || request.TargetRoot.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidDataException("Direct maintenance request is invalid or expired.");
        if (request.Action == "repair" && (request.CurrentVersion != request.Release.Version ||
            request.CurrentBuildNumber != request.Release.DeclaredBuildNumber || request.CurrentPackageSha256 != request.Release.ExpectedSha256))
            throw new InvalidDataException("Repair must use the exact installed package.");
        if (request.Action == "update" && DesktopReleaseVersionPolicy.CompareVersions(request.Release.Version, request.CurrentVersion) <= 0)
            throw new InvalidDataException("A direct catalog update requires a newer version.");
    }

    public static void ValidateAck(DesktopDirectMaintenanceRequest request, DesktopMaintenanceAck ack,
        int expectedSetupProcessId, long expectedSetupStartTicks, DateTime nowUtc)
    {
        if (ack.SchemaVersion != 2 || ack.OperationId != request.OperationId || ack.Nonce != request.Nonce ||
            ack.ReleaseId != request.Release.ReleaseId || ack.PackageSha256 != request.Release.ExpectedSha256 ||
            !string.Equals(ack.TargetRoot, request.TargetRoot, StringComparison.OrdinalIgnoreCase) || ack.UserSid != request.UserSid ||
            ack.SetupProcessId != expectedSetupProcessId || ack.SetupStartTimeUtcTicks != expectedSetupStartTicks ||
            ack.ExpiresAtUtc.Kind != DateTimeKind.Utc || ack.ExpiresAtUtc <= nowUtc || ack.ExpiresAtUtc > nowUtc.AddMinutes(1))
            throw new InvalidDataException("Direct maintenance acknowledgement does not match its owner.");
    }

    public static void ValidateRelease(DesktopReleaseResponse release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (release.DistributionProtocol == DesktopDistributionProtocol.Shared)
        {
            DesktopSharedBootstrapperPolicy.ValidateRelease(release);
            return;
        }
        var setup = release.SetupArtifact ?? throw new InvalidDataException("The exact Setup for this release is unavailable.");
        if (release.ReleaseId == Guid.Empty || release.DistributionProtocol != DesktopDistributionProtocol.Current ||
            release.InstallMode != DesktopInstallModes.StaticArchive || !IsHash(release.Sha256) ||
            release.SizeBytes is <= 0 or > DesktopArtifactDownloadPolicy.MaximumAppArchiveBytes ||
            setup.ReleaseId != release.ReleaseId || setup.Version != release.Version || setup.BuildNumber != release.BuildNumber ||
            setup.Channel != release.Channel || setup.Platform != release.Platform || setup.Platform != "win-x64" ||
            setup.PackageSha256 != release.Sha256 || setup.InstallMode != release.InstallMode ||
            setup.DistributionProtocol != release.DistributionProtocol || setup.ModelBundleId != release.ModelBundleId ||
            string.IsNullOrWhiteSpace(release.ModelBundleId) || !IsHash(setup.Sha256) ||
            setup.SizeBytes is <= 0 or > DesktopArtifactDownloadPolicy.MaximumSetupArtifactBytes || !IsFileName(setup.FileName, ".exe"))
            throw new InvalidDataException("Setup metadata does not match the exact application release.");
    }

    public static void ValidateRequest(DesktopMaintenanceRequest request, DateTime nowUtc)
    {
        ValidateRelease(request.Release);
        if (request.SchemaVersion != 1 || !IsHex(request.OperationId, 32) || !IsHex(request.Nonce, 64) ||
            request.Action is not ("update" or "repair") || request.ParentProcessId <= 0 || request.ParentStartTimeUtcTicks <= 0 ||
            string.IsNullOrWhiteSpace(request.UserSid) || request.UserSid.Length > 200 ||
            string.IsNullOrWhiteSpace(request.CurrentVersion) || request.CurrentBuildNumber <= 0 || !IsHash(request.CurrentPackageSha256) ||
            request.CreatedAtUtc is null || request.CreatedAtUtc.Value.Kind != DateTimeKind.Utc ||
            request.CreatedAtUtc > nowUtc.AddSeconds(5) || request.CreatedAtUtc < nowUtc.AddMinutes(-1) ||
            string.IsNullOrWhiteSpace(request.TargetRoot) || request.TargetRoot.Length > 160 ||
            request.TargetRoot.Any(char.IsControl) || !Path.IsPathFullyQualified(request.TargetRoot) ||
            request.TargetRoot.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidDataException("Maintenance request is invalid or expired.");
        if (request.Action == "repair" && (request.CurrentVersion != request.Release.Version ||
            request.CurrentBuildNumber != request.Release.BuildNumber || request.CurrentPackageSha256 != request.Release.Sha256))
            throw new InvalidDataException("Repair must use the exact installed version and package.");
        var newer = request.Release.DistributionProtocol == DesktopDistributionProtocol.Shared
            ? DesktopReleaseVersionPolicy.IsReleaseNewerVersionFirst(request.Release.Version, request.Release.BuildNumber, request.CurrentVersion, request.CurrentBuildNumber)
            : DesktopReleaseVersionPolicy.IsReleaseNewer(request.Release.Version, request.Release.BuildNumber, request.CurrentVersion, request.CurrentBuildNumber);
        if (request.Action == "update" && !newer)
            throw new InvalidDataException("Update must select a newer release; use exact repair for the installed build.");
    }

    public static void ValidateAck(DesktopMaintenanceRequest request, DesktopMaintenanceAck ack,
        int expectedSetupProcessId, long expectedSetupStartTicks, DateTime nowUtc)
    {
        if (ack.SchemaVersion != 1 || ack.OperationId != request.OperationId || ack.Nonce != request.Nonce ||
            ack.ReleaseId != request.Release.ReleaseId || ack.PackageSha256 != request.Release.Sha256 ||
            !string.Equals(ack.TargetRoot, request.TargetRoot, StringComparison.OrdinalIgnoreCase) || ack.UserSid != request.UserSid ||
            ack.SetupProcessId != expectedSetupProcessId || ack.SetupStartTimeUtcTicks != expectedSetupStartTicks ||
            ack.ExpiresAtUtc.Kind != DateTimeKind.Utc || ack.ExpiresAtUtc <= nowUtc || ack.ExpiresAtUtc > nowUtc.AddMinutes(1))
            throw new InvalidDataException("Maintenance acknowledgement does not match its owner.");
    }

    public static int GetClientProcessId(NamedPipeServerStream pipe)
    {
        if (!OperatingSystem.IsWindows() || !GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var id))
            throw new InvalidDataException("Cannot verify maintenance client ownership.");
        return checked((int)id);
    }

    public static int GetServerProcessId(NamedPipeClientStream pipe)
    {
        if (!OperatingSystem.IsWindows() || !GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var id))
            throw new InvalidDataException("Cannot verify maintenance server ownership.");
        return checked((int)id);
    }

    public static bool IsHash(string? value) => IsHex(value, 64);
    public static bool IsHex(string? value, int length) => value is not null && value.Length == length && value.All(char.IsAsciiHexDigit);
    public static bool IsFileName(string? value, string extension) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= DesktopArtifactDownloadPolicy.MaximumArtifactFileNameLength && value == Path.GetFileName(value) && value.EndsWith(extension, StringComparison.OrdinalIgnoreCase) &&
        !value.Any(c => char.IsControl(c) || c is '/' or '\\' or ':' or '"' or '<' or '>' or '|' or '?' or '*');

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate maintenance field.");
                RejectDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}
