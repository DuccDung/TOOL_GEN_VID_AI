using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Controllers;
using TOOL_SHARED.Contracts.Authentication;

namespace TOOL_TESTS.Authentication;

public sealed class PasswordResetControllerTests
{
    [Fact]
    public async Task ForgotPassword_ReturnsSameAcceptedMessageAfterServiceCompletes()
    {
        var service = new StubPasswordResetService();
        var controller = CreateController(service);

        var response = await controller.ForgotPassword(
            new ForgotPasswordRequest("user@example.com"),
            CancellationToken.None);

        var result = Assert.IsType<AcceptedResult>(response.Result);
        var body = Assert.IsType<ForgotPasswordResponse>(result.Value);
        Assert.Equal(PasswordResetService.RequestAcceptedMessage, body.Message);
        Assert.Equal("user@example.com", service.RequestedEmail);
    }

    [Fact]
    public async Task ResetPassword_ReturnsNoContentAfterServiceCompletes()
    {
        var service = new StubPasswordResetService();
        var controller = CreateController(service);
        var request = new ResetPasswordRequest("user@example.com", "123456", "NewStrongPass1!");

        var response = await controller.ResetPassword(request, CancellationToken.None);

        Assert.IsType<NoContentResult>(response);
        Assert.Equal(request, service.ResetRequest);
    }

    private static PasswordResetController CreateController(IPasswordResetService service) =>
        new(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "trace-password-reset-001"
                }
            }
        };

    private sealed class StubPasswordResetService : IPasswordResetService
    {
        public string? RequestedEmail { get; private set; }

        public ResetPasswordRequest? ResetRequest { get; private set; }

        public Task RequestAsync(
            ForgotPasswordRequest request,
            ClientRequestContext client,
            CancellationToken cancellationToken)
        {
            RequestedEmail = request.Email;
            return Task.CompletedTask;
        }

        public Task ResetAsync(
            ResetPasswordRequest request,
            ClientRequestContext client,
            CancellationToken cancellationToken)
        {
            ResetRequest = request;
            return Task.CompletedTask;
        }
    }
}
