using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TOOL_LOCAL.Authentication;
using TOOL_SHARED.Contracts.Common;
using TOOL_SHARED.Contracts.Vietsub;

namespace TOOL_LOCAL.Vietsub.Api;

internal interface IVietsubCloudTranslationClient
{
    Task<VietsubCloudAvailability> AvailabilityAsync(Guid projectId, Guid organizationId, CancellationToken ct);
    Task<VietsubCloudJobResponse?> FindAsync(Guid projectId, Guid organizationId, Guid operationId, CancellationToken ct);
    Task<VietsubCloudJobResponse> StartAsync(Guid projectId, VietsubCloudStartRequest input, CancellationToken ct);
    Task<VietsubCloudJobResponse> GetAsync(Guid projectId, Guid organizationId, Guid jobId, CancellationToken ct);
    Task<VietsubCloudResultPage> ResultsAsync(Guid projectId, Guid organizationId, Guid jobId, int cursor, CancellationToken ct);
    Task<VietsubCloudJobResponse> ControlAsync(Guid projectId, Guid organizationId, Guid jobId, string action, CancellationToken ct);
}

internal sealed class VietsubCloudTranslationClient(HttpClient http, AccountSessionManager sessions,
    LicenseSessionManager licenses) : IVietsubCloudTranslationClient
{
    private static string Route(Guid project, string path, Guid org) =>
        $"api/vietsub/projects/{project:D}/cloud-translation/{path}{(path.Contains('?') ? '&' : '?')}organizationId={org:D}";
    public async Task<VietsubCloudAvailability> AvailabilityAsync(Guid projectId, Guid organizationId, CancellationToken ct)
    {
        try { return (await SendAsync<VietsubCloudAvailability>(HttpMethod.Get, Route(projectId, "availability", organizationId), null, ct))!; }
        catch (AccountClientException e) when (e.StatusCode != 401)
        { return new(false, e.Code, "Dịch Cloud chưa sẵn sàng. Vui lòng liên hệ quản trị viên."); }
        catch (HttpRequestException) { return new(false, "CLOUD_OFFLINE", "Không kết nối được dịch vụ Dịch Cloud."); }
    }
    public Task<VietsubCloudJobResponse?> FindAsync(Guid projectId, Guid org, Guid operationId, CancellationToken ct) =>
        SendAsync<VietsubCloudJobResponse>(HttpMethod.Get, Route(projectId, $"jobs?clientOperationId={operationId:D}", org), null, ct);
    public async Task<VietsubCloudJobResponse> StartAsync(Guid projectId, VietsubCloudStartRequest input, CancellationToken ct) =>
        (await SendAsync<VietsubCloudJobResponse>(HttpMethod.Post, Route(projectId, "jobs", input.OrganizationId), input, ct))!;
    public async Task<VietsubCloudJobResponse> GetAsync(Guid projectId, Guid org, Guid jobId, CancellationToken ct) =>
        (await SendAsync<VietsubCloudJobResponse>(HttpMethod.Get, Route(projectId, $"jobs/{jobId:D}", org), null, ct))!;
    public async Task<VietsubCloudResultPage> ResultsAsync(Guid projectId, Guid org, Guid jobId, int cursor, CancellationToken ct) =>
        (await SendAsync<VietsubCloudResultPage>(HttpMethod.Get, Route(projectId, $"jobs/{jobId:D}/results?cursor={cursor}", org), null, ct))!;
    public async Task<VietsubCloudJobResponse> ControlAsync(Guid projectId, Guid org, Guid jobId, string action, CancellationToken ct) =>
        (await SendAsync<VietsubCloudJobResponse>(HttpMethod.Post, Route(projectId, $"jobs/{jobId:D}/{action}", org), null, ct))!;

    private async Task<T?> SendAsync<T>(HttpMethod method, string uri, object? body, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { return await SendCoreAsync<T>(method, uri, body, timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new HttpRequestException("Kết nối Dịch Cloud hết thời gian chờ."); }
    }

    private async Task<T?> SendCoreAsync<T>(HttpMethod method, string uri, object? body, CancellationToken ct)
    {
        await licenses.EnsureAccessAsync(ct);
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await sessions.GetValidAccessTokenAsync(ct));
        if (body != null) request.Content = JsonContent.Create(body, options: VietsubCloudSnapshot.JsonOptions);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await response.Content.LoadIntoBufferAsync(2 * 1024 * 1024, ct);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized) await sessions.InvalidateAsync(CancellationToken.None);
            ApiErrorResponse? error = null;
            try { error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(cancellationToken: ct); }
            catch (JsonException) { }
            throw new AccountClientException(error?.Code ?? "CLOUD_SERVER_ERROR",
                error?.Message ?? "Dịch Cloud chưa xử lý được yêu cầu.", (int)response.StatusCode);
        }
        if (response.StatusCode == HttpStatusCode.NoContent) return default;
        return await response.Content.ReadFromJsonAsync<T>(VietsubCloudSnapshot.JsonOptions, ct)
            ?? throw new HttpRequestException("Dịch Cloud trả về phản hồi trống.");
    }
}
