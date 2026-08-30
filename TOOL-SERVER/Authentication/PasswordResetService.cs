using System.Data;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SHARED.Contracts.Authentication;

namespace TOOL_SERVER.Authentication;

public sealed class PasswordResetService(
    AccountDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IPasswordResetEmailSender emailSender,
    IPasswordResetOtpStore otpStore,
    IOptions<PasswordResetOptions> optionsAccessor,
    TimeProvider timeProvider,
    ILogger<PasswordResetService> logger) : IPasswordResetService
{
    private const string GenericRequestMessage =
        "Nếu email thuộc một tài khoản hợp lệ, mã OTP đặt lại mật khẩu sẽ được gửi tới hộp thư.";

    private readonly PasswordResetOptions _options = optionsAccessor.Value;

    public async Task RequestAsync(
        ForgotPasswordRequest request,
        ClientRequestContext client,
        CancellationToken cancellationToken)
    {
        var email = ValidateEmail(request.Email);
        if (!emailSender.IsConfigured)
        {
            throw new AccountApiException(
                StatusCodes.Status503ServiceUnavailable,
                "password_reset_unavailable",
                "Chức năng gửi OTP chưa được cấu hình SMTP trên server.");
        }

        var user = await userManager.FindByEmailAsync(email);
        var canReset = user is not null &&
                       user.Email is not null &&
                       user.DeletedAtUtc is null &&
                       user.AccountStatus == AccountStatuses.Active;

        if (canReset)
        {
            var otp = PasswordResetOtpGenerator.Generate();
            var lifetime = TimeSpan.FromMinutes(_options.OtpLifetimeMinutes);
            await otpStore.SaveAsync(user!, otp, UtcNow().Add(lifetime), cancellationToken);
            try
            {
                await emailSender.SendAsync(
                    user!.Email!,
                    user.DisplayName,
                    otp,
                    lifetime,
                    cancellationToken);
                AddAudit(user.Id, "PasswordResetOtpSent", true, client);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unable to send password reset OTP for user {UserId}.", user!.Id);
                await otpStore.RemoveAsync(user, cancellationToken);
                AddAudit(user.Id, "PasswordResetOtpFailed", false, client);
            }
        }
        else
        {
            AddAudit(user?.Id, "PasswordResetOtpRequested", true, client);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetAsync(
        ResetPasswordRequest request,
        ClientRequestContext client,
        CancellationToken cancellationToken)
    {
        ValidateResetRequest(request);
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null ||
            user.DeletedAtUtc is not null ||
            user.AccountStatus != AccountStatuses.Active)
        {
            throw InvalidOtp();
        }

        var otpValidation = await otpStore.ValidateAsync(
            user,
            request.Otp,
            _options.MaxFailedAttempts,
            UtcNow(),
            cancellationToken);
        if (otpValidation != PasswordResetOtpValidation.Valid)
        {
            AddAudit(user.Id, "PasswordResetOtpRejected", false, client);
            await dbContext.SaveChangesAsync(cancellationToken);
            throw InvalidOtp();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var identityToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetResult = await userManager.ResetPasswordAsync(user, identityToken, request.NewPassword);
        if (!resetResult.Succeeded)
        {
            throw PasswordResetIdentityErrorMapper.Map(resetResult);
        }

        await otpStore.RemoveAsync(user, cancellationToken);

        var now = UtcNow();
        user.PasswordChangedAtUtc = now;
        user.UpdatedAtUtc = now;
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            throw IdentityOperationException("UpdateUserAfterPasswordReset", updateResult);
        }

        await dbContext.UserSessions
            .Where(session => session.UserId == user.Id && session.Status == SessionStatuses.Active)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(session => session.Status, SessionStatuses.Revoked)
                .SetProperty(session => session.RevokedAtUtc, now)
                .SetProperty(session => session.RevokedReason, "Password reset with email OTP"), cancellationToken);
        await dbContext.RefreshTokens
            .Where(token => token.UserId == user.Id && token.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(token => token.RevokedAtUtc, now)
                .SetProperty(token => token.RevokedReason, "Password reset with email OTP"), cancellationToken);

        AddAudit(user.Id, "PasswordResetSucceeded", true, client);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static string RequestAcceptedMessage => GenericRequestMessage;

    private static string ValidateEmail(string? value)
    {
        var email = value?.Trim() ?? string.Empty;
        if (email.Length == 0 ||
            email.Length > 256 ||
            !MailAddress.TryCreate(email, out var parsedEmail) ||
            !string.Equals(parsedEmail.Address, email, StringComparison.OrdinalIgnoreCase))
        {
            throw new AccountApiException(
                StatusCodes.Status400BadRequest,
                "validation_failed",
                "Dữ liệu đầu vào không hợp lệ.",
                new Dictionary<string, string[]> { ["email"] = ["Email không hợp lệ."] });
        }

        return email;
    }

    private static void ValidateResetRequest(ResetPasswordRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        try
        {
            ValidateEmail(request.Email);
        }
        catch (AccountApiException exception)
        {
            errors["email"] = exception.Errors!["email"];
        }

        if (request.Otp is null ||
            request.Otp.Length != 6 ||
            request.Otp.Any(character => character is < '0' or > '9'))
        {
            errors["otp"] = ["OTP phải gồm đúng 6 chữ số."];
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length > 256)
        {
            errors["newPassword"] = ["Mật khẩu mới không hợp lệ."];
        }

        if (errors.Count > 0)
        {
            throw new AccountApiException(
                StatusCodes.Status400BadRequest,
                "validation_failed",
                "Dữ liệu đầu vào không hợp lệ.",
                errors);
        }
    }

    private void AddAudit(
        string? userId,
        string eventType,
        bool succeeded,
        ClientRequestContext client)
    {
        dbContext.AccountAuditLogs.Add(new AccountAuditLog
        {
            UserId = userId,
            EventType = eventType,
            Succeeded = succeeded,
            IpAddress = NormalizeOptional(client.IpAddress, 45),
            UserAgent = NormalizeOptional(client.UserAgent, 1000),
            CorrelationId = NormalizeOptional(client.CorrelationId, 100),
            OccurredAtUtc = UtcNow()
        });
    }

    private static AccountApiException InvalidOtp() =>
        new(
            StatusCodes.Status400BadRequest,
            "invalid_password_reset_otp",
            "Mã OTP không hợp lệ, đã hết hạn hoặc đã vượt quá số lần thử.");

    private static InvalidOperationException IdentityOperationException(string operation, IdentityResult result) =>
        new($"Identity operation '{operation}' failed with error codes: " +
            string.Join(", ", result.Errors.Select(error => error.Code).Distinct(StringComparer.Ordinal)));

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
