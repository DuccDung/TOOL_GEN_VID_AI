using TOOL_LOCAL.Authentication;

namespace TOOL_TESTS.Authentication;

public sealed class RegistrationInputValidatorTests
{
    [Fact]
    public void Validate_AcceptsValidRegistration()
    {
        var result = RegistrationInputValidator.Validate(
            "Nguyễn Văn A",
            "user@example.com",
            "StrongPass1!",
            "StrongPass1!");

        Assert.Empty(result);
    }

    [Fact]
    public void Validate_ReturnsFieldErrorsForInvalidInput()
    {
        var result = RegistrationInputValidator.Validate(
            null,
            "invalid-email",
            "weak",
            "different");

        Assert.Contains("email", result.Keys);
        Assert.Contains("password", result.Keys);
        Assert.Contains("passwordConfirmation", result.Keys);
        Assert.Contains(result["password"], message => message.Contains("10 ký tự"));
        Assert.Contains(result["password"], message => message.Contains("chữ hoa"));
        Assert.Contains(result["password"], message => message.Contains("chữ số"));
        Assert.Contains(result["password"], message => message.Contains("ký tự đặc biệt"));
    }
}
