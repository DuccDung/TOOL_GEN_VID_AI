using System.Net;
using Microsoft.AspNetCore.Http;
using TOOL_SERVER.Generation;

namespace TOOL_TESTS.Generation;

public sealed class ProviderErrorPresentationTests
{
    [Fact]
    public void InsufficientProviderBalance_IsPresentedAsTemporaryMaintenance()
    {
        var providerException = new ProviderHttpException(
            "kling",
            "kling_http_400",
            "Account balance not enough",
            statusCode: HttpStatusCode.BadRequest);

        var result = GenerationService.ToApiException(providerException);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal("provider_temporarily_unavailable", result.Code);
        Assert.Equal("Hệ thống AI đang bảo trì hoặc tạm thời gián đoạn. Vui lòng thử lại sau.", result.Message);
        Assert.DoesNotContain("balance", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void ProviderCredentialFailure_DoesNotExposeProviderDetails(HttpStatusCode statusCode)
    {
        var providerException = new ProviderHttpException(
            "kling",
            $"kling_http_{(int)statusCode}",
            "Sensitive provider credential diagnostic",
            statusCode: statusCode);

        var result = GenerationService.ToApiException(providerException);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal("provider_temporarily_unavailable", result.Code);
        Assert.DoesNotContain("credential", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContentSpeechBudgetViolation_IsReturnedAsStructuredValidationError()
    {
        var providerException = new ProviderHttpException(
            "openai",
            "openai_spoken_text_too_long",
            "Nội dung AI cho cảnh 2 có 52 từ, vượt mức 28 từ cho clip 15 giây.",
            errors: new Dictionary<string, string[]>
            {
                ["sceneNumber"] = ["2"],
                ["durationSeconds"] = ["15"],
                ["wordCount"] = ["52"],
                ["maximumWords"] = ["28"]
            });

        var result = GenerationService.ToApiException(providerException);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, result.StatusCode);
        Assert.Equal("openai_spoken_text_too_long", result.Code);
        Assert.Equal("2", Assert.Single(result.Errors!["sceneNumber"]));
        Assert.Equal("28", Assert.Single(result.Errors["maximumWords"]));
    }
}
