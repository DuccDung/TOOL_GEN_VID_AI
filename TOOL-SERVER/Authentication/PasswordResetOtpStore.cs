using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;

namespace TOOL_SERVER.Authentication;

public sealed class PasswordResetOtpStore(
    AccountDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider) : IPasswordResetOtpStore
{
    private const string LoginProvider = "VideoMaker";
    private const string TokenName = "PasswordResetOtp";
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "TOOL_SERVER.PasswordResetOtp.v1");

    public async Task SaveAsync(
        ApplicationUser user,
        string otp,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var token = await FindTokenAsync(user.Id, cancellationToken);
        var protectedValue = Protect(new PasswordResetOtpState(
            PasswordResetOtpGenerator.Hash(otp),
            expiresAtUtc,
            0));

        if (token is null)
        {
            dbContext.Set<IdentityUserToken<string>>().Add(new IdentityUserToken<string>
            {
                UserId = user.Id,
                LoginProvider = LoginProvider,
                Name = TokenName,
                Value = protectedValue
            });
        }
        else
        {
            token.Value = protectedValue;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<PasswordResetOtpValidation> ValidateAsync(
        ApplicationUser user,
        string otp,
        int maxFailedAttempts,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var token = await FindTokenAsync(user.Id, cancellationToken);
        if (token?.Value is null || !TryUnprotect(token.Value, out var state))
        {
            if (token is not null)
            {
                dbContext.Remove(token);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return PasswordResetOtpValidation.Invalid;
        }

        if (state.ExpiresAtUtc <= nowUtc)
        {
            dbContext.Remove(token);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PasswordResetOtpValidation.Expired;
        }

        if (PasswordResetOtpGenerator.Matches(otp, state.OtpHash))
        {
            await transaction.CommitAsync(cancellationToken);
            return PasswordResetOtpValidation.Valid;
        }

        var attempts = state.FailedAttempts + 1;
        if (attempts >= maxFailedAttempts)
        {
            dbContext.Remove(token);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PasswordResetOtpValidation.AttemptsExceeded;
        }

        token.Value = Protect(state with { FailedAttempts = attempts });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PasswordResetOtpValidation.Invalid;
    }

    public async Task RemoveAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var token = await FindTokenAsync(user.Id, cancellationToken);
        if (token is null)
        {
            return;
        }

        dbContext.Remove(token);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task<IdentityUserToken<string>?> FindTokenAsync(
        string userId,
        CancellationToken cancellationToken) =>
        dbContext.Set<IdentityUserToken<string>>().SingleOrDefaultAsync(
            token => token.UserId == userId &&
                     token.LoginProvider == LoginProvider &&
                     token.Name == TokenName,
            cancellationToken);

    private string Protect(PasswordResetOtpState state) =>
        _protector.Protect(JsonSerializer.Serialize(state));

    private bool TryUnprotect(string value, out PasswordResetOtpState state)
    {
        try
        {
            state = JsonSerializer.Deserialize<PasswordResetOtpState>(_protector.Unprotect(value))
                ?? throw new JsonException("Password reset OTP state is empty.");
            return state.OtpHash is { Length: 32 };
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            state = default!;
            return false;
        }
    }

    private sealed record PasswordResetOtpState(
        byte[] OtpHash,
        DateTime ExpiresAtUtc,
        int FailedAttempts);
}

internal static class PasswordResetOtpGenerator
{
    public static string Generate() =>
        RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString("D6");

    public static byte[] Hash(string otp) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(otp));

    public static bool Matches(string otp, byte[] expectedHash)
    {
        var actualHash = Hash(otp);
        return expectedHash.Length == actualHash.Length &&
               CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
