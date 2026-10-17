using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_TESTS.Updates;

public sealed class DesktopMaintenanceProtocolTests
{
    internal static DesktopReleaseResponse Release()
    {
        var id = Guid.NewGuid(); var packageHash = new string('a', 64);
        var setup = new DesktopSetupArtifactResponse(id, "1.2.0", 12, "Stable", "win-x64", packageHash,
            "Setup.exe", "/api/setup/download", 100, new string('b', 64), ModelBundleId: "models-fixture");
        return new(id, "VideoMaker", "1.2.0", 12, "Stable", "win-x64", null, null, DateTime.UtcNow,
            "app.zip", DesktopArtifactDownloadPolicy.ApplicationPrefix + "app.zip", 100, packageHash,
            DesktopArtifactSourceKinds.ExternalVps, DesktopInstallModes.StaticArchive, 2, setup, "models-fixture");
    }
    private static DesktopMaintenanceRequest Request() => new(1, Guid.NewGuid().ToString("N"), new('c', 64), "update",
        Release(), @"C:\vm-maintenance-fixture", "1.1.0", 11, new('d', 64), 100, 123, "S-1-5-21-fixture", true, DateTime.UtcNow);
    private static DesktopMaintenanceAck Ack(DesktopMaintenanceRequest request) => new(1, request.OperationId, request.Nonce,
        request.Release.ReleaseId, request.Release.Sha256, request.TargetRoot, 200, 456, request.UserSid, DateTime.UtcNow.AddSeconds(30));

    [Theory]
    [InlineData("missing")]
    [InlineData("package")]
    [InlineData("release")]
    [InlineData("version")]
    [InlineData("protocol")]
    [InlineData("bundle")]
    public void Maintenance_never_substitutes_latest_or_mismatched_Setup(string changed)
    {
        var release = Release();
        release = changed switch
        {
            "missing" => release with { SetupArtifact = null },
            "package" => release with { SetupArtifact = release.SetupArtifact! with { PackageSha256 = new('f', 64) } },
            "release" => release with { SetupArtifact = release.SetupArtifact! with { ReleaseId = Guid.NewGuid() } },
            "version" => release with { SetupArtifact = release.SetupArtifact! with { Version = "9.0.0" } },
            "protocol" => release with { DistributionProtocol = 3 },
            _ => release with { SetupArtifact = release.SetupArtifact! with { ModelBundleId = "different" } }
        };
        Assert.Throws<InvalidDataException>(() => DesktopMaintenanceProtocol.ValidateRelease(release));
    }

    [Theory]
    [InlineData("nonce")]
    [InlineData("operation")]
    [InlineData("release")]
    [InlineData("target")]
    [InlineData("user")]
    [InlineData("pid")]
    [InlineData("start")]
    [InlineData("expired")]
    [InlineData("future")]
    public void Invalid_ACK_cannot_authorize_desktop_shutdown(string changed)
    {
        var request = Request(); var ack = Ack(request);
        ack = changed switch
        {
            "nonce" => ack with { Nonce = new('e', 64) }, "operation" => ack with { OperationId = Guid.NewGuid().ToString("N") },
            "release" => ack with { ReleaseId = Guid.NewGuid() }, "target" => ack with { TargetRoot = @"C:\another-app" },
            "user" => ack with { UserSid = "another-user" }, "pid" => ack with { SetupProcessId = 201 },
            "start" => ack with { SetupStartTimeUtcTicks = 457 }, "expired" => ack with { ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1) },
            _ => ack with { ExpiresAtUtc = DateTime.UtcNow.AddHours(1) }
        };
        Assert.Throws<InvalidDataException>(() => DesktopMaintenanceProtocol.ValidateAck(request, ack, 200, 456, DateTime.UtcNow));
    }

    [Fact]
    public void Exact_repair_and_current_request_are_required_before_handoff()
    {
        var request = Request();
        DesktopMaintenanceProtocol.ValidateRequest(request, DateTime.UtcNow);
        Assert.Throws<InvalidDataException>(() => DesktopMaintenanceProtocol.ValidateRequest(request with { Action = "repair" }, DateTime.UtcNow));
        Assert.Throws<InvalidDataException>(() => DesktopMaintenanceProtocol.ValidateRequest(request with { CreatedAtUtc = DateTime.UtcNow.AddMinutes(-2) }, DateTime.UtcNow));
        Assert.Throws<InvalidDataException>(() => DesktopMaintenanceProtocol.ValidateRequest(request with { CreatedAtUtc = null }, DateTime.UtcNow));
        Assert.Throws<InvalidDataException>(() => DesktopMaintenanceProtocol.ValidateRequest(request with { CurrentVersion = request.Release.Version,
            CurrentBuildNumber = request.Release.BuildNumber, CurrentPackageSha256 = request.Release.Sha256 }, DateTime.UtcNow));
        Assert.Throws<InvalidDataException>(() => DesktopMaintenanceProtocol.ValidateRequest(request with { CurrentBuildNumber = request.Release.BuildNumber + 1 }, DateTime.UtcNow));
        DesktopMaintenanceProtocol.ValidateAck(request, Ack(request), 200, 456, DateTime.UtcNow);
    }

    [Fact]
    public async Task Framing_roundtrip_is_bounded_and_duplicate_or_unknown_members_are_rejected()
    {
        using var stream = new MemoryStream(); var request = Request();
        await DesktopMaintenanceProtocol.WriteAsync(stream, request, default); stream.Position = 0;
        Assert.Equal(request, await DesktopMaintenanceProtocol.ReadAsync<DesktopMaintenanceRequest>(stream, default));
        foreach (var raw in new[] { "{\"schemaVersion\":1,\"schemaVersion\":2}\n", "{\"schemaVersion\":1,\"SCHEMAVERSION\":2}\n" })
        {
            using var invalid = new MemoryStream(Encoding.UTF8.GetBytes(raw));
            await Assert.ThrowsAsync<InvalidDataException>(() => DesktopMaintenanceProtocol.ReadAsync<DesktopMaintenanceAck>(invalid, default));
        }
        using var unknown = new MemoryStream(Encoding.UTF8.GetBytes("{\"unexpected\":true}\n"));
        await Assert.ThrowsAsync<JsonException>(() => DesktopMaintenanceProtocol.ReadAsync<DesktopMaintenanceAck>(unknown, default));
        using var oversized = new MemoryStream(Enumerable.Repeat((byte)'x', DesktopMaintenanceProtocol.MaximumFrameBytes + 1).ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(() => DesktopMaintenanceProtocol.ReadAsync<DesktopMaintenanceAck>(oversized, default));
        using var truncated = new MemoryStream(Encoding.UTF8.GetBytes("{"));
        await Assert.ThrowsAsync<EndOfStreamException>(() => DesktopMaintenanceProtocol.ReadAsync<DesktopMaintenanceAck>(truncated, default));
    }

    [Fact]
    public async Task Native_pipe_peer_identity_is_the_actual_connected_process()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var name = "vm-maintenance-test-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var connected = server.WaitForConnectionAsync(timeout.Token);
        await client.ConnectAsync(timeout.Token); await connected;
        Assert.Equal(Environment.ProcessId, DesktopMaintenanceProtocol.GetClientProcessId(server));
        Assert.Equal(Environment.ProcessId, DesktopMaintenanceProtocol.GetServerProcessId(client));
        var request = Request();
        // Native pipe writes can wait for the peer to consume the frame. Exercise
        // both peers concurrently, as the actual app/Setup handshake does.
        var received = DesktopMaintenanceProtocol.ReadAsync<DesktopMaintenanceRequest>(client, timeout.Token);
        await DesktopMaintenanceProtocol.WriteAsync(server, request, timeout.Token);
        Assert.Equal(request, await received);
    }
}
