using Microsoft.AspNetCore.Identity;
using TOOL_SERVER.Authentication;

namespace TOOL_TESTS.Authentication;

public sealed class RegistrationIdentityErrorMapperTests
{
    [Fact]
    public void Map_ReturnsConflictAndEmailErrorForDuplicateEmail()
    {
        var result = IdentityResult.Failed(new IdentityError
        {
            Code = "DuplicateEmail",
            Description = "Raw Identity description must not be exposed."
        });

        var exception = RegistrationIdentityErrorMapper.Map(result);

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal("email_already_exists", exception.Code);
        Assert.Equal("Email này đã được sử dụng.", Assert.Single(exception.Errors!["email"]));
    }

    [Fact]
    public void Map_GroupsPasswordErrorsUnderPasswordField()
    {
        var result = IdentityResult.Failed(
            new IdentityError { Code = "PasswordTooShort", Description = "raw" },
            new IdentityError { Code = "PasswordRequiresUpper", Description = "raw" },
            new IdentityError { Code = "PasswordRequiresDigit", Description = "raw" });

        var exception = RegistrationIdentityErrorMapper.Map(result);

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("registration_validation_failed", exception.Code);
        Assert.Equal(3, exception.Errors!["password"].Length);
        Assert.DoesNotContain(exception.Errors["password"], message => message == "raw");
    }
}
