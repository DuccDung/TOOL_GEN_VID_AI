using Microsoft.AspNetCore.Identity;

namespace TOOL_SERVER.Authentication;

internal static class RegistrationIdentityErrorMapper
{
    private const string DuplicateEmail = "DuplicateEmail";
    private const string DuplicateUserName = "DuplicateUserName";

    public static AccountApiException Map(IdentityResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Succeeded)
        {
            throw new ArgumentException("A successful IdentityResult cannot be mapped to an error.", nameof(result));
        }

        var identityErrors = result.Errors.ToArray();
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var error in identityErrors)
        {
            var (field, message) = MapError(error.Code);
            if (!errors.TryGetValue(field, out var messages))
            {
                messages = [];
                errors[field] = messages;
            }

            if (!messages.Contains(message, StringComparer.Ordinal))
            {
                messages.Add(message);
            }
        }

        var duplicateEmail = identityErrors.Any(error =>
            error.Code is DuplicateEmail or DuplicateUserName);
        return new AccountApiException(
            duplicateEmail ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest,
            duplicateEmail ? "email_already_exists" : "registration_validation_failed",
            duplicateEmail ? "Email này đã được sử dụng." : "Vui lòng kiểm tra lại thông tin đăng ký.",
            errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
    }

    private static (string Field, string Message) MapError(string code) => code switch
    {
        DuplicateEmail or DuplicateUserName => ("email", "Email này đã được sử dụng."),
        "InvalidEmail" => ("email", "Email không đúng định dạng."),
        "InvalidUserName" => ("email", "Email chứa ký tự không được hỗ trợ."),
        "PasswordTooShort" => ("password", "Mật khẩu phải có ít nhất 10 ký tự."),
        "PasswordRequiresDigit" => ("password", "Mật khẩu phải có ít nhất một chữ số."),
        "PasswordRequiresLower" => ("password", "Mật khẩu phải có ít nhất một chữ thường."),
        "PasswordRequiresUpper" => ("password", "Mật khẩu phải có ít nhất một chữ hoa."),
        "PasswordRequiresNonAlphanumeric" => ("password", "Mật khẩu phải có ít nhất một ký tự đặc biệt."),
        "PasswordRequiresUniqueChars" => ("password", "Mật khẩu chưa có đủ số ký tự khác nhau."),
        _ => ("account", "Dữ liệu tài khoản không hợp lệ.")
    };
}
