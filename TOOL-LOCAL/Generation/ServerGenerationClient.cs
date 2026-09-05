using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Providers;
using TOOL_SHARED.Contracts.Common;
using TOOL_SHARED.Contracts.Generation;
using TOOL_SHARED.Contracts.Organizations;
using TOOL_SHARED.Contracts.Projects;

namespace TOOL_LOCAL.Generation;

internal sealed class ServerGenerationClient(
    HttpClient httpClient,
    AccountSessionManager sessionManager,
    LicenseSessionManager licenseManager) : IGenerationClient
{
    private const long MaximumVideoBytes = 1024L * 1024 * 1024;
    private const long MaximumImageBytes = 10L * 1024 * 1024;
    private const long MaximumVoiceBytes = 50L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _organizationLock = new(1, 1);
    private readonly object _organizationStateLock = new();
    private Guid? _organizationId;

    public Guid? SelectedOrganizationId
    {
        get
        {
            lock (_organizationStateLock)
            {
                return _organizationId;
            }
        }
    }

    public Task<IReadOnlyList<OrganizationSummaryResponse>> GetOrganizationsAsync(
        CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyList<OrganizationSummaryResponse>>(
            HttpMethod.Get,
            "api/organizations",
            null,
            cancellationToken);

    public async Task SelectOrganizationAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Tổ chức được chọn không hợp lệ.", nameof(organizationId));
        }

        var organizations = await GetOrganizationsAsync(cancellationToken);
        if (organizations.All(x => x.OrganizationId != organizationId))
        {
            throw new AccountClientException(
                "organization_access_denied",
                "Tài khoản không còn quyền truy cập tổ chức được chọn.",
                403);
        }

        await _organizationLock.WaitAsync(cancellationToken);
        try
        {
            lock (_organizationStateLock)
            {
                _organizationId = organizationId;
            }
        }
        finally
        {
            _organizationLock.Release();
        }
    }

    public async Task<GenerationProviderStatusResponse> GetProviderStatusAsync(CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await SendAsync<GenerationProviderStatusResponse>(
            HttpMethod.Get,
            $"api/generation/providers/status?organizationId={organizationId:D}",
            null,
            cancellationToken);
    }

    public Task<GeneratedContentResponse> GenerateContentAsync(
        GenerateContentRequest request,
        CancellationToken cancellationToken) =>
        SendContentAsync(request, cancellationToken);

    public Task<GenerateCharacterReferenceImageResponse> GenerateCharacterReferenceImageAsync(
        GenerateCharacterReferenceImageRequest request,
        CancellationToken cancellationToken) =>
        SendCharacterImageAsync(request, cancellationToken);

    public async Task<SceneFirstFrameQuoteResponse> GetSceneFirstFrameQuoteAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await SendAsync<SceneFirstFrameQuoteResponse>(
            HttpMethod.Get,
            $"api/projects/{projectId:D}/scenes/{sceneId:D}/first-frames/quote?organizationId={organizationId:D}",
            null,
            cancellationToken);
    }

    public async Task<GenerateSceneFirstFrameResponse> GenerateSceneFirstFrameAsync(
        GenerateSceneFirstFrameRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await SendAsync<GenerateSceneFirstFrameResponse>(
            HttpMethod.Post,
            "api/generation/images/scene-first-frames",
            request with { OrganizationId = organizationId },
            cancellationToken);
    }

    public async Task<SceneFirstFrameListResponse> GetSceneFirstFramesAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await SendAsync<SceneFirstFrameListResponse>(
            HttpMethod.Get,
            $"api/projects/{projectId:D}/scenes/{sceneId:D}/first-frames?organizationId={organizationId:D}",
            null,
            cancellationToken);
    }

    public async Task<ProjectSceneFirstFrameListResponse> GetProjectSceneFirstFramesAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await SendAsync<ProjectSceneFirstFrameListResponse>(
            HttpMethod.Get,
            $"api/projects/{projectId:D}/scene-first-frames?organizationId={organizationId:D}",
            null,
            cancellationToken);
    }

    public async Task<SceneFirstFrameSummary> MaterializeSceneFirstFrameAsync(
        Guid projectId,
        Guid sceneId,
        MaterializeSceneFirstFrameRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await SendAsync<SceneFirstFrameSummary>(
            HttpMethod.Post,
            $"api/projects/{projectId:D}/scenes/{sceneId:D}/first-frames/materialize",
            request with { OrganizationId = organizationId },
            cancellationToken);
    }

    public async Task<SceneFirstFrameSummary> ApproveSceneFirstFrameAsync(
        Guid projectId,
        Guid sceneId,
        Guid frameId,
        ChangeSceneFirstFrameStatusRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await SendAsync<SceneFirstFrameSummary>(
            HttpMethod.Post,
            $"api/projects/{projectId:D}/scenes/{sceneId:D}/first-frames/{frameId:D}/approve",
            request with { OrganizationId = organizationId },
            cancellationToken);
    }

    public async Task<SceneFirstFrameSummary> RejectSceneFirstFrameAsync(
        Guid projectId,
        Guid sceneId,
        Guid frameId,
        ChangeSceneFirstFrameStatusRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await SendAsync<SceneFirstFrameSummary>(
            HttpMethod.Post,
            $"api/projects/{projectId:D}/scenes/{sceneId:D}/first-frames/{frameId:D}/reject",
            request with { OrganizationId = organizationId },
            cancellationToken);
    }

    public Task<SceneVoiceGenerationResponse> GenerateSceneVoiceAsync(
        GenerateSceneVoiceRequest request,
        CancellationToken cancellationToken) =>
        SendSceneVoiceAsync(request, cancellationToken);

    public Task<VideoTaskResponse> SubmitVideoAsync(
        SubmitVideoRequest request,
        CancellationToken cancellationToken) =>
        SendVideoAsync(request, cancellationToken);

    public Task<VideoTaskResponse> GetVideoStatusAsync(
        Guid providerRequestId,
        CancellationToken cancellationToken) =>
        SendAsync<VideoTaskResponse>(HttpMethod.Get, $"api/generation/videos/{providerRequestId:D}", null, cancellationToken);

    public async Task<ProjectAssetLibraryResponse> GetProjectAssetLibraryAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await SendAsync<ProjectAssetLibraryResponse>(
            HttpMethod.Get,
            $"api/projects/{projectId:D}/assets?organizationId={organizationId:D}",
            null,
            cancellationToken);
    }

    public async Task<MaterializeProjectAssetPlanResponse> MaterializeProjectAssetPlanAsync(
        Guid projectId,
        MaterializeProjectAssetPlanRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await SendAsync<MaterializeProjectAssetPlanResponse>(
            HttpMethod.Post,
            $"api/projects/{projectId:D}/assets/materialize",
            request with { OrganizationId = organizationId },
            cancellationToken);
    }

    public async Task<ProjectAssetSummary> CreateProjectAssetAsync(
        Guid projectId,
        CreateProjectAssetRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await SendAsync<ProjectAssetSummary>(
            HttpMethod.Post,
            $"api/projects/{projectId:D}/assets",
            request with { OrganizationId = organizationId },
            cancellationToken);
    }

    public async Task<ProjectAssetSummary> UpdateProjectAssetAsync(
        Guid projectId,
        Guid projectAssetId,
        UpdateProjectAssetRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await SendAsync<ProjectAssetSummary>(
            HttpMethod.Put,
            $"api/projects/{projectId:D}/assets/{projectAssetId:D}",
            request with { OrganizationId = organizationId },
            cancellationToken);
    }

    public async Task<ProjectAssetSummary> LockProjectAssetAsync(
        Guid projectId,
        Guid projectAssetId,
        ChangeProjectAssetLockRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await SendAsync<ProjectAssetSummary>(
            HttpMethod.Post,
            $"api/projects/{projectId:D}/assets/{projectAssetId:D}/lock",
            request with { OrganizationId = organizationId },
            cancellationToken);
    }

    public async Task<ProjectAssetSummary> UnlockProjectAssetAsync(
        Guid projectId,
        Guid projectAssetId,
        ChangeProjectAssetLockRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await SendAsync<ProjectAssetSummary>(
            HttpMethod.Post,
            $"api/projects/{projectId:D}/assets/{projectAssetId:D}/unlock",
            request with { OrganizationId = organizationId },
            cancellationToken);
    }

    public async Task<ApproveAiProjectAssetsResponse> ApproveAiProjectAssetsAsync(
        Guid projectId,
        ApproveAiProjectAssetsRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await SendAsync<ApproveAiProjectAssetsResponse>(
            HttpMethod.Post,
            $"api/projects/{projectId:D}/assets/approve-ai",
            request with { OrganizationId = organizationId },
            cancellationToken);
    }

    public async Task DeleteProjectAssetAsync(
        Guid projectId,
        Guid projectAssetId,
        DeleteProjectAssetRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        await SendWithoutResponseAsync(
            HttpMethod.Delete,
            $"api/projects/{projectId:D}/assets/{projectAssetId:D}",
            request with { OrganizationId = organizationId },
            cancellationToken);
    }

    public async Task<SceneAssetAssignmentSummary> UpdateSceneAssetAssignmentsAsync(
        Guid projectId,
        Guid sceneId,
        UpdateSceneAssetAssignmentsRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await SendAsync<SceneAssetAssignmentSummary>(
            HttpMethod.Put,
            $"api/projects/{projectId:D}/assets/scenes/{sceneId:D}",
            request with { OrganizationId = organizationId },
            cancellationToken);
    }

    public async Task<ConfirmSceneProjectAssetsResponse> ConfirmSceneProjectAssetsAsync(
        Guid projectId,
        Guid sceneId,
        ConfirmSceneProjectAssetsRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await SendAsync<ConfirmSceneProjectAssetsResponse>(
            HttpMethod.Post,
            $"api/projects/{projectId:D}/assets/scenes/{sceneId:D}/confirm",
            request with { OrganizationId = organizationId },
            cancellationToken);
    }

    public async Task DownloadVideoAsync(
        string outputUrl,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await licenseManager.EnsureAccessAsync(cancellationToken);
        if (!Uri.TryCreate(outputUrl, UriKind.RelativeOrAbsolute, out var uri) || uri.IsAbsoluteUri)
        {
            throw new InvalidDataException("Server trả về đường dẫn video không hợp lệ.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await sessionManager.GetValidAccessTokenAsync(cancellationToken));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        if (response.Content.Headers.ContentLength is > MaximumVideoBytes)
        {
            throw new InvalidDataException("Video vượt quá giới hạn tải xuống 1 GB.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }
            total += read;
            if (total > MaximumVideoBytes)
            {
                throw new InvalidDataException("Video vượt quá giới hạn tải xuống 1 GB.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    public async Task DownloadCharacterImageAsync(
        GenerateCharacterReferenceImageResponse image,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await licenseManager.EnsureAccessAsync(cancellationToken);
        var expectedPath = $"/api/generation/character-images/{image.ProviderRequestId:D}/content";
        if (!Uri.TryCreate(image.ContentUrl, UriKind.RelativeOrAbsolute, out var uri) ||
            uri.IsAbsoluteUri ||
            !string.Equals(image.ContentUrl, expectedPath, StringComparison.OrdinalIgnoreCase) ||
            image.SizeBytes is <= 0 or > MaximumImageBytes ||
            image.MimeType is not ("image/png" or "image/jpeg") ||
            image.Sha256.Length != 64)
        {
            throw new InvalidDataException("Server trả về metadata ảnh nhân vật không hợp lệ.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await sessionManager.GetValidAccessTokenAsync(cancellationToken));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var mimeType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(mimeType, image.MimeType, StringComparison.OrdinalIgnoreCase) ||
            response.Content.Headers.ContentLength is > MaximumImageBytes ||
            response.Content.Headers.ContentLength is { } contentLength && contentLength != image.SizeBytes)
        {
            throw new InvalidDataException("Nội dung ảnh tải về không khớp metadata của server.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }
            total += read;
            if (total > MaximumImageBytes || total > image.SizeBytes)
            {
                throw new InvalidDataException("Ảnh nhân vật vượt quá dung lượng đã xác nhận.");
            }
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        await destination.FlushAsync(cancellationToken);
        var sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (total != image.SizeBytes || !string.Equals(sha256, image.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Ảnh nhân vật tải về không khớp SHA-256 hoặc dung lượng đã xác nhận.");
        }
    }

    public async Task DownloadSceneFirstFrameAsync(
        GenerateSceneFirstFrameResponse image,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await licenseManager.EnsureAccessAsync(cancellationToken);
        var expectedPath = $"/api/generation/images/scene-first-frames/{image.ProviderRequestId:D}/content";
        if (!Uri.TryCreate(image.ContentUrl, UriKind.RelativeOrAbsolute, out var uri) ||
            uri.IsAbsoluteUri ||
            !string.Equals(image.ContentUrl, expectedPath, StringComparison.OrdinalIgnoreCase) ||
            image.SizeBytes is <= 0 or > 8L * 1024 * 1024 ||
            image.MimeType is not ("image/png" or "image/jpeg") ||
            image.Sha256.Length != 64 ||
            (image.Width, image.Height) is not ((1280, 720) or (720, 1280)))
        {
            throw new InvalidDataException("Server trả về metadata first-frame không hợp lệ.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await sessionManager.GetValidAccessTokenAsync(cancellationToken));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var mimeType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(mimeType, image.MimeType, StringComparison.OrdinalIgnoreCase) ||
            response.Content.Headers.ContentLength is > 8L * 1024 * 1024 ||
            response.Content.Headers.ContentLength is { } contentLength && contentLength != image.SizeBytes)
        {
            throw new InvalidDataException("Nội dung first-frame tải về không khớp metadata server.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }
            total += read;
            if (total > 8L * 1024 * 1024 || total > image.SizeBytes)
            {
                throw new InvalidDataException("First-frame vượt quá dung lượng đã xác nhận.");
            }
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        await destination.FlushAsync(cancellationToken);
        var sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (total != image.SizeBytes || !string.Equals(sha256, image.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("First-frame tải về không khớp SHA-256 hoặc dung lượng.");
        }
    }

    public async Task DownloadSceneVoiceAsync(
        SceneVoiceGenerationResponse voice,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await licenseManager.EnsureAccessAsync(cancellationToken);
        var expectedPath = $"/api/generation/scene-voices/{voice.ProviderRequestId:D}/content";
        if (!Uri.TryCreate(voice.ContentUrl, UriKind.RelativeOrAbsolute, out var uri) ||
            uri.IsAbsoluteUri ||
            !string.Equals(voice.ContentUrl, expectedPath, StringComparison.OrdinalIgnoreCase) ||
            voice.SizeBytes is <= 0 or > MaximumVoiceBytes ||
            voice.MimeType != "audio/wav" ||
            voice.Sha256.Length != 64 ||
            voice.DurationMs <= 0 ||
            voice.SampleRate is < 8_000 or > 192_000 ||
            voice.Channels is < 1 or > 2)
        {
            throw new InvalidDataException("Server trả về metadata giọng đọc không hợp lệ.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await sessionManager.GetValidAccessTokenAsync(cancellationToken));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var mimeType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(mimeType, voice.MimeType, StringComparison.OrdinalIgnoreCase) ||
            response.Content.Headers.ContentLength is > MaximumVoiceBytes ||
            response.Content.Headers.ContentLength is { } contentLength && contentLength != voice.SizeBytes)
        {
            throw new InvalidDataException("Nội dung giọng đọc tải về không khớp metadata của server.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }
            total += read;
            if (total > MaximumVoiceBytes || total > voice.SizeBytes)
            {
                throw new InvalidDataException("Giọng đọc vượt quá dung lượng đã xác nhận.");
            }
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        await destination.FlushAsync(cancellationToken);
        var sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (total != voice.SizeBytes || !string.Equals(sha256, voice.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Giọng đọc tải về không khớp SHA-256 hoặc dung lượng đã xác nhận.");
        }
    }

    private async Task<GeneratedContentResponse> SendContentAsync(
        GenerateContentRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await SendAsync<GeneratedContentResponse>(
            HttpMethod.Post,
            "api/generation/content",
            request with { OrganizationId = organizationId },
            cancellationToken);
    }

    private async Task<VideoTaskResponse> SendVideoAsync(
        SubmitVideoRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await SendAsync<VideoTaskResponse>(
            HttpMethod.Post,
            "api/generation/videos",
            request with { OrganizationId = organizationId },
            cancellationToken);
    }

    private async Task<GenerateCharacterReferenceImageResponse> SendCharacterImageAsync(
        GenerateCharacterReferenceImageRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await SendAsync<GenerateCharacterReferenceImageResponse>(
            HttpMethod.Post,
            $"api/generation/characters/{request.CharacterId:D}/reference-images",
            request with { OrganizationId = organizationId },
            cancellationToken);
    }

    private async Task<SceneVoiceGenerationResponse> SendSceneVoiceAsync(
        GenerateSceneVoiceRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = await GetOrganizationIdAsync(cancellationToken);
        return await SendAsync<SceneVoiceGenerationResponse>(
            HttpMethod.Post,
            $"api/generation/scenes/{request.SceneId:D}/voice",
            request with { OrganizationId = organizationId },
            cancellationToken);
    }

    private async Task<Guid> GetOrganizationIdAsync(CancellationToken cancellationToken)
    {
        if (SelectedOrganizationId is { } cached)
        {
            return cached;
        }

        await _organizationLock.WaitAsync(cancellationToken);
        try
        {
            if (SelectedOrganizationId is { } current)
            {
                return current;
            }
            var organizations = await GetOrganizationsAsync(cancellationToken);
            var selected = organizations.FirstOrDefault()
                ?? throw new AccountClientException(
                    "organization_access_denied",
                    "Tài khoản chưa được gán vào tổ chức. Hãy liên hệ quản trị viên.",
                    403);
            lock (_organizationStateLock)
            {
                _organizationId = selected.OrganizationId;
            }
            return selected.OrganizationId;
        }
        finally
        {
            _organizationLock.Release();
        }
    }

    public async Task<ProviderSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var status = await GetProviderStatusAsync(cancellationToken);
        return new ProviderSettingsResponse(
            status.OpenAiReady,
            null,
            status.OpenAiModel ?? "",
            status.VideoReady,
            status.VideoProviderCode,
            status.VideoModel ?? "");
    }

    public async Task TestProviderAsync(string providerCode, CancellationToken cancellationToken)
    {
        var status = await GetProviderStatusAsync(cancellationToken);
        var ready = providerCode switch
        {
            "openai" => status.OpenAiReady,
            "video" => status.VideoReady,
            _ => throw new ArgumentException("Provider không được hỗ trợ.", nameof(providerCode))
        };
        if (!ready)
        {
            throw new AccountClientException(
                $"{providerCode}_not_configured",
                $"{providerCode} chưa được quản trị viên cấu hình cho tổ chức.",
                503);
        }
    }

    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string uri,
        object? body,
        CancellationToken cancellationToken)
    {
        await licenseManager.EnsureAccessAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await sessionManager.GetValidAccessTokenAsync(cancellationToken));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken)
            ?? throw new AccountClientException("invalid_server_response", "Server trả về dữ liệu không hợp lệ.", (int)response.StatusCode);
    }

    private async Task SendWithoutResponseAsync(
        HttpMethod method,
        string uri,
        object? body,
        CancellationToken cancellationToken)
    {
        await licenseManager.EnsureAccessAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await sessionManager.GetValidAccessTokenAsync(cancellationToken));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        ApiErrorResponse? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
        }
        var exception = new AccountClientException(
            error?.Code ?? "generation_server_error",
            error?.Message ?? $"Server trả về HTTP {(int)response.StatusCode}.",
            (int)response.StatusCode,
            error?.Errors,
            error?.TraceId);
        if (exception.StatusCode == 401)
        {
            await sessionManager.InvalidateAsync(CancellationToken.None);
        }

        throw exception;
    }
}
