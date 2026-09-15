using System.Text.Json.Serialization;

namespace TOOL_LOCAL.SystemSetup;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record SystemSetupRequest(Guid OperationId, Guid ExpectedOrganizationId, string ContextGeneration,
    string[] ComponentIds, Guid? PreviousOperationId = null, bool ResourceWarningAccepted = false,
    string? ConfirmedResourceProfileId = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record SystemSetupCancelRequest(Guid OperationId);
internal sealed record SetupComponent(string Id, string Name, string Version, string State, string Message,
    bool CanInstall, bool CanVerify, bool CanRepair, long? DownloadBytes = null, long? MinimumFreeDiskBytes = null,
    string? ErrorCode = null, DateTime? CheckedAtUtc = null, string? ResourceProfileId = null);
internal sealed record SetupOperation(Guid OperationId, string Mode, string State, string[] ComponentIds,
    long Sequence, string? CurrentComponent = null, string? Stage = null, double? Percent = null,
    long? BytesProcessed = null, long? TotalBytes = null, bool AllSelectedReady = false, bool AllRequiredReady = false);
internal sealed record SetupSnapshot(string ContextGeneration, Guid? OrganizationId,
    SetupComponent[] Components, SetupOperation? Operation, long Revision = 0,
    bool StartupRequired = false);
internal sealed class SetupException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
internal interface ISetupComponentAdapter
{
    SetupComponent Component { get; }
    Task<SetupComponent> RunAsync(bool install, bool resourceWarningAccepted,
        Action<string, double?, long?, long?> progress, CancellationToken cancellationToken);
}

// Startup only needs a cheap, local status read for components that already have
// machine/build-bound readiness evidence. The explicit CHECK command still runs
// the full adapter verification when the user requests it from Settings.
internal interface ISetupComponentStatusInspector
{
    SetupComponent Inspect();
}
