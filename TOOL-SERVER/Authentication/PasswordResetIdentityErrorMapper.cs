using Microsoft.AspNetCore.Identity;

namespace TOOL_SERVER.Authentication;

internal static class PasswordResetIdentityErrorMapper
{
    public static AccountApiException Map(IdentityResult result)
    {
        if (result.Succeeded)
        {
            throw new ArgumentException("A successful IdentityResult cannot be mapped to an error.", nameof(result));
        }

        var errors = result.Errors.ToArray();
        if (errors.Any(error => error.Code.Equals("InvalidToken", StringComparison.OrdinalIgnoreCase)))
        {
            return InvalidToken();
        }

        var passwordErrors = errors
            .Select(MapPasswordError)
            .Where(message => message.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (passwordErrors.Length == 0)
        {
            return InvalidToken();
        }

        return new AccountApiException(
            StatusCodes.Status400BadRequest,
            "password_reset_validation_failed",
            "Mật khẩu mới chưa đáp ứng yêu cầu bảo mật.",
            new Dictionary<string, string[]> { ["newPassword"] = passwordErrors });
    }

    private static string MapPasswordError(IdentityError error) => error.Code switch
    {
        "PasswordTooShort" => "Mật khẩu phải có ít nhất 10 ký tự.",
        "PasswordRequiresDigit" => "Mật khẩu phải có ít nhất một chữ số.",
        "PasswordRequiresLower" => "Mật khẩu phải có ít nhất một chữ thường.",
        "PasswordRequiresUpper" => "Mật khẩu phải có ít nhất một chữ hoa.",
        "PasswordRequiresNonAlphanumeric" => "Mật khẩu phải có ít nhất một ký tự đặc biệt.",
        "PasswordRequiresUniqueChars" => "Mật khẩu chưa có đủ số ký tự khác nhau.",
        _ => string.Empty
    };

    private static AccountApiException InvalidToken() =>
        new(
            StatusCodes.Status400BadRequest,
            "invalid_password_reset_otp",
            "Mã OTP không hợp lệ, đã hết hạn hoặc đã vượt quá số lần thử.");
}
