using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_SHARED.Contracts.Common;
using TOOL_SHARED.Contracts.Vietsub;

namespace TOOL_LOCAL.Vietsub.Api;

internal interface IVietsubProjectRegistryClient
{
    Task<IReadOnlyList<VietsubProjectResponse>> ListAsync(
        Guid organizationId,
        CancellationToken cancellationToken);

    Task<VietsubProjectResponse> RegisterAsync(
        VietsubProjectManifest manifest,
        CancellationToken cancellationToken);

    Task<VietsubProjectResponse> RenameAsync(
        Guid projectId,
        Guid organizationId,
        string name,
        CancellationToken cancellationToken);

    Task<VietsubProjectResponse> ArchiveAsync(
        Guid projectId,
        Guid organizationId,
        CancellationToken cancellationToken);
}

internal sealed class VietsubProjectRegistryClient(
    HttpClient httpClient,
    AccountSessionManager sessionManager,
    LicenseSessionManager licenseManager) : IVietsubProjectRegistryClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<VietsubProjectResponse>> ListAsync(
        Guid organizationId,
        CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyList<VietsubProjectResponse>>(
            HttpMethod.Get,
            $"api/vietsub/projects?organizationId={organizationId:D}",
            body: null,
            cancellationToken);

    public Task<VietsubProjectResponse> RegisterAsync(
        VietsubProjectManifest manifest,
        CancellationToken cancellationToken) =>
        SendAsync<VietsubProjectResponse>(
            HttpMethod.Post,
            "api/vietsub/projects",
            new TOOL_SHARED.Contracts.Vietsub.CreateVietsubProjectRequest(
                manifest.ProjectId,
                manifest.OrganizationId,
                manifest.Name,
                manifest.SourceLanguageCode,
                manifest.TargetLanguageCode),
            cancellationToken);

    public Task<VietsubProjectResponse> RenameAsync(
        Guid projectId,
        Guid organizationId,
        string name,
        CancellationToken cancellationToken) =>
        SendAsync<VietsubProjectResponse>(
            HttpMethod.Put,
            $"api/vietsub/projects/{projectId:D}",
            new TOOL_SHARED.Contracts.Vietsub.RenameVietsubProjectRequest(organizationId, name),
            cancellationToken);

    public Task<VietsubProjectResponse> ArchiveAsync(
        Guid projectId,
        Guid organizationId,
        CancellationToken cancellationToken) =>
        SendAsync<VietsubProjectResponse>(
            HttpMethod.Delete,
            $"api/vietsub/projects/{projectId:D}?organizationId={organizationId:D}",
            body: null,
            cancellationToken);

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

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken)
            ?? throw new AccountClientException(
                "invalid_server_response",
                "Server trả về dữ liệu Vietsub không hợp lệ.",
                (int)response.StatusCode);
    }

    private async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        ApiErrorResponse? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(
                JsonOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
        }

        var exception = new AccountClientException(
            error?.Code ?? "vietsub_server_error",
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
