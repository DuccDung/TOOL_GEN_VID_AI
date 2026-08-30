using System.Net.Mail;

namespace TOOL_LOCAL.Authentication;

public static class RegistrationInputValidator
{
    public static IReadOnlyDictionary<string, string[]> Validate(
        string? displayName,
        string? email,
        string? password,
        string? passwordConfirmation)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(displayName) && displayName.Trim().Length > 200)
        {
            errors["displayName"] = ["Tên hiển thị không được vượt quá 200 ký tự."];
        }

        var normalizedEmail = email?.Trim() ?? string.Empty;
        if (normalizedEmail.Length == 0)
        {
            errors["email"] = ["Vui lòng nhập email."];
        }
        else if (normalizedEmail.Length > 256 ||
                 !MailAddress.TryCreate(normalizedEmail, out var parsedEmail) ||
                 !string.Equals(parsedEmail.Address, normalizedEmail, StringComparison.OrdinalIgnoreCase))
        {
            errors["email"] = ["Email không đúng định dạng."];
        }

        var passwordErrors = ValidatePassword(password);
        if (passwordErrors.Count > 0)
        {
            errors["password"] = passwordErrors.ToArray();
        }

        if (!string.Equals(password, passwordConfirmation, StringComparison.Ordinal))
        {
            errors["passwordConfirmation"] = ["Mật khẩu nhập lại không khớp."];
        }

        return errors;
    }

    private static IReadOnlyCollection<string> ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return ["Vui lòng nhập mật khẩu."];
        }

        var errors = new List<string>();
        if (password.Length < 10)
        {
            errors.Add("Mật khẩu phải có ít nhất 10 ký tự.");
        }

        if (password.Length > 256)
        {
            errors.Add("Mật khẩu không được vượt quá 256 ký tự.");
        }

        if (!password.Any(char.IsUpper))
        {
            errors.Add("Mật khẩu phải có ít nhất một chữ hoa.");
        }

        if (!password.Any(char.IsLower))
        {
            errors.Add("Mật khẩu phải có ít nhất một chữ thường.");
        }

        if (!password.Any(char.IsDigit))
        {
            errors.Add("Mật khẩu phải có ít nhất một chữ số.");
        }

        if (!password.Any(character => !char.IsLetterOrDigit(character)))
        {
            errors.Add("Mật khẩu phải có ít nhất một ký tự đặc biệt.");
        }

        return errors;
    }
}
