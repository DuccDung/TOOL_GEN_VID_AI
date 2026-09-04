using System.Net.Http.Headers;
using System.Text.Json;
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
            ProviderCodes.Fal => null,
            _ => throw new AccountApiException(StatusCodes.Status404NotFound, "provider_not_found", "Provider không được hỗ trợ.")
        };
        var requestUri = providerCode == ProviderCodes.Fal
            ? new Uri(
                "https://api.fal.ai/v1/models?endpoint_id=" +
                Uri.EscapeDataString(FalVeoPolicy.StandardEndpointId) +
                "&endpoint_id=" +
                Uri.EscapeDataString(FalVeoPolicy.FastEndpointId))
            : new Uri(baseUri, relativePath!);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            providerCode == ProviderCodes.Fal ? "Key" : "Bearer",
            apiKey);
        using var response = await httpClientFactory.CreateClient("ProviderCredentialTest")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AccountApiException(
                StatusCodes.Status422UnprocessableEntity,
                "provider_credential_test_failed",
                $"Provider từ chối credential (HTTP {(int)response.StatusCode}). Credential cũ vẫn được giữ nguyên.");
        }
        if (providerCode == ProviderCodes.Fal)
        {
            await EnsureFalEndpointsAvailableAsync(response, cancellationToken);
        }
    }

    private static async Task EnsureFalEndpointsAvailableAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var endpointIds = document.RootElement.GetProperty("models")
                .EnumerateArray()
                .Select(model => model.GetProperty("endpoint_id").GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.Ordinal);
            if (endpointIds.Contains(FalVeoPolicy.StandardEndpointId) &&
                endpointIds.Contains(FalVeoPolicy.FastEndpointId))
            {
                return;
            }
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            // Normalize malformed provider responses below without exposing their body.
        }

        throw new AccountApiException(
            StatusCodes.Status422UnprocessableEntity,
            "provider_credential_test_failed",
            "Fal không xác nhận đủ hai endpoint Veo đã phê duyệt. Credential cũ vẫn được giữ nguyên.");
    }
}
