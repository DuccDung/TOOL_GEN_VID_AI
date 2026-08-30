using System.Net.Http.Headers;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Generation;

namespace TOOL_SERVER.Organizations;

public interface IOrganizationProviderCredentialTester
{
    Task TestAsync(string providerCode, string? baseUrl, string apiKey, CancellationToken cancellationToken);
}

internal sealed class OrganizationProviderCredentialTester(
    IHttpClientFactory httpClientFactory) : IOrganizationProviderCredentialTester
{
    public async Task TestAsync(
        string providerCode,
        string? baseUrl,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            !ProviderRuntimeResolver.IsAllowedBaseUri(providerCode, baseUri))
        {
            throw new AccountApiException(
                StatusCodes.Status422UnprocessableEntity,
                "provider_base_url_rejected",
                "Base URL của provider không nằm trong allowlist.");
        }
        var relativePath = providerCode switch
        {
            ProviderCodes.OpenAi => "models",
            ProviderCodes.Kling => "tasks?task_ids=videomaker-credential-test",
            ProviderCodes.BytePlus => "models",
            _ => throw new AccountApiException(StatusCodes.Status404NotFound, "provider_not_found", "Provider không được hỗ trợ.")
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await httpClientFactory.CreateClient("ProviderCredentialTest")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AccountApiException(
                StatusCodes.Status422UnprocessableEntity,
                "provider_credential_test_failed",
                $"Provider từ chối credential (HTTP {(int)response.StatusCode}). Credential cũ vẫn được giữ nguyên.");
        }
    }
}
