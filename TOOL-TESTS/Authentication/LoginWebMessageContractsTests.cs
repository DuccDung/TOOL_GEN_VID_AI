using System.Text.Json;
using TOOL_LOCAL.Authentication;

namespace TOOL_TESTS.Authentication;

public sealed class LoginWebMessageContractsTests
{
    private const string LoginPage = "https://auth.app.local/login.html?v=1";

    [Theory]
    [InlineData("https://app.local/login.html")]
    [InlineData("https://auth.app.local/index.html")]
    [InlineData("http://auth.app.local/login.html")]
    [InlineData("https://auth.app.local:444/login.html")]
    public void TryRead_RejectsMessagesOutsideTheLoginPage(string source)
    {
        Assert.False(LoginWebMessageContracts.TryRead(source, ValidRequest(), out _));
    }

    [Fact]
    public void TryRead_RequiresBoundedTypedMessageAndRequestId()
    {
        Assert.True(LoginWebMessageContracts.TryRead(LoginPage, ValidRequest(), out var request));
        Assert.Equal("auth.login", request?.Type);
        Assert.False(LoginWebMessageContracts.TryRead(LoginPage, "not-json", out _));
        Assert.False(LoginWebMessageContracts.TryRead(LoginPage,
            JsonSerializer.Serialize(new { type = "auth.login", requestId = "bad-id" }), out _));
        Assert.False(LoginWebMessageContracts.TryRead(LoginPage, new string('x', 8193), out _));
    }

    [Fact]
    public void TryReadLogin_AcceptsRememberChoiceAndRejectsForgedPayload()
    {
        using var valid = JsonDocument.Parse("""
            {"email":" user@example.com ","password":"pass1234567","rememberMe":false}
            """);
        Assert.True(LoginWebMessageContracts.TryReadLogin(valid.RootElement, out var email,
            out var password, out var rememberMe, out var error));
        Assert.Equal("user@example.com", email);
        Assert.Equal("pass1234567", password);
        Assert.False(rememberMe);
        Assert.Null(error);

        using var forged = JsonDocument.Parse("""
            {"email":"user@example.com","password":"pass1234567","rememberMe":"true"}
            """);
        Assert.False(LoginWebMessageContracts.TryReadLogin(forged.RootElement, out _, out _,
            out _, out var forgedError));
        Assert.True(forgedError?.Error);
    }

    [Fact]
    public void TryReadLogin_ReportsFieldErrors()
    {
        using var invalid = JsonDocument.Parse("""
            {"email":"wrong","password":"","rememberMe":true}
            """);
        Assert.False(LoginWebMessageContracts.TryReadLogin(invalid.RootElement, out _, out _,
            out _, out var error));
        Assert.Equal("Email không đúng định dạng.", error?.EmailError);
        Assert.Equal("Vui lòng nhập mật khẩu.", error?.PasswordError);
        Assert.Equal("email", error?.FocusField);
    }

    private static string ValidRequest() => JsonSerializer.Serialize(new
    {
        type = "auth.login",
        requestId = Guid.NewGuid(),
        payload = new { email = "user@example.com", password = "pass1234567", rememberMe = true }
    });
}
