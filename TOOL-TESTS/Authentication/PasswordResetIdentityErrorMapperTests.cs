using Microsoft.AspNetCore.Identity;
using TOOL_SERVER.Authentication;

namespace TOOL_TESTS.Authentication;

public sealed class PasswordResetIdentityErrorMapperTests
{
    [Fact]
    public void Map_InvalidToken_DoesNotExposeIdentityDescription()
    {
        var result = IdentityResult.Failed(new IdentityError
        {
            Code = "InvalidToken",
            Description = "raw identity token detail"
        });

        var exception = PasswordResetIdentityErrorMapper.Map(result);

        Assert.Equal("invalid_password_reset_otp", exception.Code);
        Assert.DoesNotContain("raw", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(exception.Errors);
    }

    [Fact]
    public void Map_PasswordPolicyErrors_ReturnsSafeFieldErrors()
    {
        var result = IdentityResult.Failed(
            new IdentityError { Code = "PasswordTooShort", Description = "raw" },
            new IdentityError { Code = "PasswordRequiresUpper", Description = "raw" });

        var exception = PasswordResetIdentityErrorMapper.Map(result);

        Assert.Equal("password_reset_validation_failed", exception.Code);
        Assert.Equal(2, exception.Errors!["newPassword"].Length);
        Assert.DoesNotContain(exception.Errors["newPassword"], message => message == "raw");
    }
}
