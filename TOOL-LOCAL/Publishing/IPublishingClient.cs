using TOOL_SHARED.Contracts.Publishing;

namespace TOOL_LOCAL.Publishing;

internal interface IPublishingClient
{
    Task<PublishingState> GetPublishingStateAsync(Guid organizationId, CancellationToken ct) => throw new NotSupportedException();
    Task<PublishingImage> UploadPublishingImageAsync(UploadPublishingImageRequest request, CancellationToken ct) => throw new NotSupportedException();
    Task<PublishingScheduleSummary> SavePublishingScheduleAsync(SavePublishingScheduleRequest request, CancellationToken ct) => throw new NotSupportedException();
    Task<PublishingScheduleSummary> ChangePublishingScheduleAsync(ChangePublishingScheduleRequest request, CancellationToken ct) => throw new NotSupportedException();
    Task<StartPublishingOAuthResponse> StartPublishingOAuthAsync(StartPublishingOAuthRequest request, CancellationToken ct) => throw new NotSupportedException();
    Task PublishingRunActionAsync(PublishingRunActionRequest request, CancellationToken ct) => throw new NotSupportedException();
    Task ReviewPublishingRunAsync(ApprovePublishingRunRequest request, CancellationToken ct) => throw new NotSupportedException();
    Task DisconnectPublishingAsync(Guid connectionId, CancellationToken ct) => throw new NotSupportedException();
    Task DownloadPublishingPreviewAsync(Guid organizationId, Guid runId, string sha256, string path, CancellationToken ct) => throw new NotSupportedException();
}
