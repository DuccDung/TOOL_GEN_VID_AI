using System.Net;
using System.Text.Json;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.SystemSetup;
using TOOL_LOCAL.Updates;
using TOOL_SHARED.Contracts.Updates;

namespace TOOL_TESTS.Updates;

public sealed class DesktopRepairErrorsTests
{
    [Theory]
    [InlineData("", DesktopRepairErrorCodes.Unavailable)]
    [InlineData("<html>private-path</html>", DesktopRepairErrorCodes.Unavailable)]
    [InlineData("{\"code\":\"not_found\",\"message\":\"private-path\"}", DesktopRepairErrorCodes.Unavailable)]
    [InlineData("{\"code\":\"desktop_repair_package_not_found\",\"message\":\"private-path\"}", DesktopRepairErrorCodes.PackageNotFound)]
    public async Task OnlyExplicitApiCodeProvesThatMatchingPackageIsMissing(string body, string expectedCode)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(body) };
        var error = await DesktopRepairErrors.FromResponseAsync(response, default);
        Assert.Equal(expectedCode, error.Code);
        Assert.DoesNotContain("private-path", error.Message);
        Assert.Contains("ZIP", error.Message);
    }

    [Fact]
    public async Task OversizedErrorIsNotTrustedAndCancellationIsHonored()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound) {
            Content = new StringContent("{\"code\":\"desktop_repair_package_not_found\",\"message\":\"" + new string('a', 5000) + "\"}") };
        Assert.Equal(DesktopRepairErrorCodes.Unavailable, (await DesktopRepairErrors.FromResponseAsync(response, default)).Code);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var next = new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DesktopRepairErrors.FromResponseAsync(next, cancelled.Token));
    }

    [Fact]
    public void FailureEnvelopeDoesNotExposeRawExceptionOrLoseBuildIdentity()
    {
        foreach (var error in new Exception[] { new IOException("private-path"), new HttpRequestException("private-token"),
            new InvalidDataException("private-url"), new SetupException("system_setup_busy", "private-context"),
            new AccountClientException("session_expired", "private-token", 401) })
        {
            var failure = DesktopRepairErrors.FromException(error);
            Assert.DoesNotContain("private-", JsonSerializer.Serialize(failure));
            Assert.False(string.IsNullOrWhiteSpace(failure.Version));
            Assert.True(failure.BuildNumber > 0);
        }
        Assert.Equal("system_setup_busy", DesktopRepairErrors.FromException(new SetupException("system_setup_busy", "private")).Code);
    }
}
