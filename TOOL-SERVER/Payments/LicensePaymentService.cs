using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Accounts;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_SERVER.Payments;

public sealed partial class LicensePaymentService(
    AccountDbContext dbContext,
    IOptions<SepayPaymentOptions> options,
    TimeProvider timeProvider,
    ILicensePaymentTelemetry telemetry,
    ILogger<LicensePaymentService> logger) : ILicensePaymentService
{
    private readonly SepayPaymentOptions _options = options.Value;

    public async Task<IReadOnlyList<LicenseOfferResponse>> GetOffersAsync(CancellationToken cancellationToken)
    {
        EnsurePaymentsReady();
        var plans = await dbContext.LicensePlans
            .AsNoTracking()
            .Where(x => x.IsActive &&
                        x.IsPublic &&
                        x.SalePriceVnd != null &&
                        x.SalePriceVnd > 0 &&
                        x.DefaultDurationDays != null &&
                        x.DefaultDurationDays > 0 &&
                        x.DefaultDurationDays <= 3650)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.SalePriceVnd)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return plans.Select(x => new LicenseOfferResponse(
                x.LicensePlanId,
                x.PlanCode,
                x.Name,
                x.Description,
                x.SalePriceVnd!.Value,
                x.DefaultDurationDays!.Value,
                x.MaxActivatedDevices,
                ParseMarketingFeatures(x.MarketingFeaturesJson),
                x.DisplayOrder))
            .ToArray();
    }

    public async Task<LicensePaymentCheckoutResponse> CreateOrReuseAsync(
        string userId,
        CreateLicensePaymentRequest request,
        CancellationToken cancellationToken)
    {
        EnsurePaymentsReady();
        if (request.LicensePlanId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            !IdempotencyKeyRegex().IsMatch(request.IdempotencyKey.Trim()))
        {
            throw Validation("invalid_payment_request", "Yêu cầu tạo thanh toán không hợp lệ.");
        }

        var now = UtcNow();
        var idempotencyKey = request.IdempotencyKey.Trim();
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        var replay = await dbContext.LicensePayments
            .SingleOrDefaultAsync(
                x => x.UserId == userId && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (replay is not null)
        {
            if (replay.LicensePlanId != request.LicensePlanId)
            {
                throw Conflict("idempotency_conflict", "Mã yêu cầu đã được dùng cho một gói khác.");
            }

            await MarkExpiredIfNeededAsync(replay, now, cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            logger.LogInformation(
                "Reused license payment {LicensePaymentId} ({OrderCode}) with status {PaymentStatus}.",
                replay.LicensePaymentId,
                replay.OrderCode,
                replay.Status);
            return BuildCheckoutResponse(replay, now, true);
        }

        var plan = await dbContext.LicensePlans.SingleOrDefaultAsync(
            x => x.LicensePlanId == request.LicensePlanId &&
                 x.IsActive && x.IsPublic &&
                 x.SalePriceVnd != null && x.SalePriceVnd > 0 &&
                 x.DefaultDurationDays != null &&
                 x.DefaultDurationDays > 0 &&
                 x.DefaultDurationDays <= 3650,
            cancellationToken)
            ?? throw NotFound("license_offer_not_found", "Gói không còn được mở bán.");

        var existing = await dbContext.LicensePayments
            .Where(x => x.UserId == userId &&
                        x.LicensePlanId == plan.LicensePlanId &&
                        x.Status == LicensePaymentStatuses.Pending &&
                        x.ExpiresAtUtc > now)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            logger.LogInformation(
                "Reused pending license payment {LicensePaymentId} ({OrderCode}).",
                existing.LicensePaymentId,
                existing.OrderCode);
            return BuildCheckoutResponse(existing, now, true);
        }

        var payment = new LicensePayment
        {
            LicensePaymentId = Guid.NewGuid(),
            UserId = userId,
            LicensePlanId = plan.LicensePlanId,
            OrderCode = await CreateUniqueCodeAsync("VMO", x => x.OrderCode, cancellationToken),
            TransferCode = await CreateUniqueCodeAsync(
                _options.TransferCodePrefix.Trim().ToUpperInvariant(),
                x => x.TransferCode,
                cancellationToken),
            IdempotencyKey = idempotencyKey,
            PriceSnapshotVnd = decimal.Truncate(plan.SalePriceVnd!.Value),
            DurationSnapshotDays = plan.DefaultDurationDays!.Value,
            PlanCodeSnapshot = plan.PlanCode,
            PlanNameSnapshot = plan.Name,
            EntitlementSnapshotJson = plan.FeatureFlagsJson,
            Status = LicensePaymentStatuses.Pending,
            ReceiverBankCodeSnapshot = _options.ReceiverBankCode.Trim(),
            ReceiverAccountNumberSnapshot = _options.ReceiverAccountNumber.Trim(),
            ReceiverAccountNameSnapshot = _options.ReceiverAccountName.Trim(),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(_options.PaymentExpireMinutes)
        };
        dbContext.LicensePayments.Add(payment);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            logger.LogInformation(
                "Created license payment {LicensePaymentId} ({OrderCode}) with status {PaymentStatus}.",
                payment.LicensePaymentId,
                payment.OrderCode,
                payment.Status);
            telemetry.RecordCreated();
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            dbContext.ChangeTracker.Clear();
            var concurrentReplay = await dbContext.LicensePayments
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.UserId == userId && x.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            if (concurrentReplay is not null && concurrentReplay.LicensePlanId == request.LicensePlanId)
            {
                return BuildCheckoutResponse(concurrentReplay, UtcNow(), true);
            }
            throw;
        }

        return BuildCheckoutResponse(payment, now, false);
    }

    public async Task<LicensePaymentStatusResponse> GetStatusAsync(
        string userId,
        string orderCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderCode) || orderCode.Length > 40)
        {
            throw NotFound("license_payment_not_found", "Không tìm thấy giao dịch thanh toán.");
        }

        var payment = await dbContext.LicensePayments.SingleOrDefaultAsync(
            x => x.UserId == userId && x.OrderCode == orderCode.Trim(),
            cancellationToken)
            ?? throw NotFound("license_payment_not_found", "Không tìm thấy giao dịch thanh toán.");
        var now = UtcNow();
        await MarkExpiredIfNeededAsync(payment, now, cancellationToken);
        return BuildStatusResponse(payment, now);
    }

    public async Task<CurrentLicensePaymentResponse> GetCurrentAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        EnsureWebhookReady();
        var now = UtcNow();
        var payment = await dbContext.LicensePayments
            .AsNoTracking()
            .Where(x => x.UserId == userId &&
                        x.Status == LicensePaymentStatuses.Pending &&
                        x.ExpiresAtUtc > now)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return new CurrentLicensePaymentResponse(
            payment is null ? null : BuildCheckoutResponse(payment, now, true));
    }

    public async Task HandleWebhookAsync(
        SepayWebhookPayload payload,
        CancellationToken cancellationToken)
    {
        EnsureWebhookReady();
        if (payload.Id <= 0 ||
            !string.Equals(payload.TransferType?.Trim(), "in", StringComparison.OrdinalIgnoreCase) ||
            payload.TransferAmount <= 0)
        {
            return;
        }

        var receiverAccount = NormalizeAlphaNumeric(payload.AccountNumber);
        if (!string.Equals(
                receiverAccount,
                NormalizeAlphaNumeric(_options.ReceiverAccountNumber),
                StringComparison.Ordinal))
        {
            logger.LogWarning("Ignored SePay transaction {ProviderTransactionId}: receiver account did not match.", payload.Id);
            telemetry.RecordUnmatchedWebhook(LicensePaymentWebhookMismatchReason.ReceiverAccount);
            return;
        }

        var executionStrategy = dbContext.Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;

            var duplicatePayment = await dbContext.LicensePayments.SingleOrDefaultAsync(
                x => x.ProviderTransactionId == payload.Id,
                cancellationToken);
            if (duplicatePayment is not null)
            {
                logger.LogInformation(
                    "Ignored duplicate SePay transaction {ProviderTransactionId} for license payment {LicensePaymentId} ({OrderCode}) with status {PaymentStatus}.",
                    payload.Id,
                    duplicatePayment.LicensePaymentId,
                    duplicatePayment.OrderCode,
                    duplicatePayment.Status);
                telemetry.RecordDuplicateWebhook();
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                return;
            }

            var payment = await FindWebhookPaymentAsync(payload, cancellationToken);
            if (payment is null)
            {
                logger.LogWarning("Ignored unmatched SePay transaction {ProviderTransactionId}.", payload.Id);
                telemetry.RecordUnmatchedWebhook(LicensePaymentWebhookMismatchReason.PaymentNotFound);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                return;
            }

            if (!string.Equals(
                    NormalizeAlphaNumeric(payment.ReceiverAccountNumberSnapshot),
                    receiverAccount,
                    StringComparison.Ordinal) ||
                payload.TransferAmount != payment.PriceSnapshotVnd)
            {
                logger.LogWarning(
                    "Ignored SePay transaction {ProviderTransactionId} for license payment {LicensePaymentId} ({OrderCode}) with status {PaymentStatus}: payment details did not match.",
                    payload.Id,
                    payment.LicensePaymentId,
                    payment.OrderCode,
                    payment.Status);
                telemetry.RecordUnmatchedWebhook(LicensePaymentWebhookMismatchReason.PaymentDetails);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                return;
            }

            var now = UtcNow();
            payment.Status = LicensePaymentStatuses.Paid;
            payment.ProviderTransactionId = payload.Id;
            payment.ProviderReferenceCode = NormalizeOptional(payload.ReferenceCode, 100);
            payment.PaidAtUtc = now;

            var activeLicense = await dbContext.UserLicenses
                .Include(x => x.LicensePlan)
                .Where(x => x.UserId == payment.UserId &&
                            (x.Status == "Active" || x.Status == "Trial") &&
                            x.StartsAtUtc <= now &&
                            (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now) &&
                            x.LicensePlan.IsActive)
                .OrderByDescending(x => x.ExpiresAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            UserLicense fulfilledLicense;
            if (activeLicense is not null &&
                activeLicense.LicensePlanId == payment.LicensePlanId &&
                activeLicense.ExpiresAtUtc is { } currentExpiry)
            {
                activeLicense.ExpiresAtUtc = currentExpiry.AddDays(payment.DurationSnapshotDays);
                activeLicense.EntitlementSnapshotJson = payment.EntitlementSnapshotJson;
                activeLicense.UpdatedAtUtc = now;
                fulfilledLicense = activeLicense;
            }
            else
            {
                var startsAt = activeLicense?.ExpiresAtUtc is { } activeExpiry && activeExpiry > now
                    ? activeExpiry
                    : now;
                fulfilledLicense = new UserLicense
                {
                    UserLicenseId = Guid.NewGuid(),
                    UserId = payment.UserId,
                    LicensePlanId = payment.LicensePlanId,
                    Status = "Active",
                    StartsAtUtc = startsAt,
                    ExpiresAtUtc = startsAt.AddDays(payment.DurationSnapshotDays),
                    EntitlementSnapshotJson = payment.EntitlementSnapshotJson,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                dbContext.UserLicenses.Add(fulfilledLicense);
            }

            payment.FulfilledUserLicenseId = fulfilledLicense.UserLicenseId;
            payment.FulfilledAtUtc = now;
            payment.Status = LicensePaymentStatuses.Fulfilled;
            dbContext.AccountAuditLogs.Add(new AccountAuditLog
            {
                UserId = payment.UserId,
                EventType = "LicensePaymentFulfilled",
                Succeeded = true,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    payment.LicensePaymentId,
                    payment.OrderCode,
                    fulfilledLicense.UserLicenseId,
                    payment.LicensePlanId
                }),
                OccurredAtUtc = now
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            logger.LogInformation(
                "Fulfilled license payment {LicensePaymentId} ({OrderCode}) with status {PaymentStatus}.",
                payment.LicensePaymentId,
                payment.OrderCode,
                payment.Status);
            telemetry.RecordFulfilled();
        });
    }

    private async Task<LicensePayment?> FindWebhookPaymentAsync(
        SepayWebhookPayload payload,
        CancellationToken cancellationToken)
    {
        var code = NormalizeAlphaNumeric(payload.Code);
        if (!string.IsNullOrWhiteSpace(code))
        {
            var exact = await dbContext.LicensePayments.SingleOrDefaultAsync(
                x => x.TransferCode == code &&
                     (x.Status == LicensePaymentStatuses.Pending || x.Status == LicensePaymentStatuses.Expired),
                cancellationToken);
            if (exact is not null)
            {
                return exact;
            }
        }

        var searchableText = NormalizeAlphaNumeric(string.Join(' ', new[]
        {
            payload.Code,
            payload.Content,
            payload.Description
        }.Where(x => !string.IsNullOrWhiteSpace(x))));
        if (string.IsNullOrWhiteSpace(searchableText))
        {
            return null;
        }

        var amount = payload.TransferAmount;
        var candidates = await dbContext.LicensePayments
            .Where(x => x.PriceSnapshotVnd == amount &&
                        (x.Status == LicensePaymentStatuses.Pending || x.Status == LicensePaymentStatuses.Expired))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);
        var matches = candidates
            .Where(x => searchableText.Contains(
                NormalizeAlphaNumeric(x.TransferCode),
                StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private async Task<string> CreateUniqueCodeAsync(
        string prefix,
        System.Linq.Expressions.Expression<Func<LicensePayment, string>> selector,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var value = $"{prefix}{RandomNumberGenerator.GetHexString(12)}";
            if (!await dbContext.LicensePayments.Select(selector).AnyAsync(x => x == value, cancellationToken))
            {
                return value;
            }
        }

        throw new InvalidOperationException("Không thể tạo mã thanh toán duy nhất.");
    }

    private async Task MarkExpiredIfNeededAsync(
        LicensePayment payment,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (payment.Status == LicensePaymentStatuses.Pending && payment.ExpiresAtUtc <= now)
        {
            payment.Status = LicensePaymentStatuses.Expired;
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Expired license payment {LicensePaymentId} ({OrderCode}) with status {PaymentStatus}.",
                payment.LicensePaymentId,
                payment.OrderCode,
                payment.Status);
            telemetry.RecordExpired();
        }
    }

    private LicensePaymentCheckoutResponse BuildCheckoutResponse(
        LicensePayment payment,
        DateTime now,
        bool reused)
    {
        var isExpired = payment.Status == LicensePaymentStatuses.Expired ||
                        (payment.Status == LicensePaymentStatuses.Pending && payment.ExpiresAtUtc <= now);
        return new LicensePaymentCheckoutResponse(
            payment.OrderCode,
            payment.TransferCode,
            payment.PlanCodeSnapshot,
            payment.PlanNameSnapshot,
            payment.DurationSnapshotDays,
            payment.PriceSnapshotVnd,
            payment.ReceiverBankCodeSnapshot,
            payment.ReceiverAccountNumberSnapshot,
            payment.ReceiverAccountNameSnapshot,
            payment.TransferCode,
            BuildQrImageUrl(payment),
            isExpired ? LicensePaymentStatuses.Expired : payment.Status,
            NormalizeUtc(payment.CreatedAtUtc),
            NormalizeUtc(payment.ExpiresAtUtc),
            NormalizeUtc(now),
            reused,
            payment.Status is LicensePaymentStatuses.Paid or LicensePaymentStatuses.Fulfilled,
            payment.Status == LicensePaymentStatuses.Fulfilled,
            isExpired);
    }

    private static LicensePaymentStatusResponse BuildStatusResponse(LicensePayment payment, DateTime now)
    {
        var isExpired = payment.Status == LicensePaymentStatuses.Expired ||
                        (payment.Status == LicensePaymentStatuses.Pending && payment.ExpiresAtUtc <= now);
        var status = isExpired ? LicensePaymentStatuses.Expired : payment.Status;
        var message = status switch
        {
            LicensePaymentStatuses.Fulfilled => "Thanh toán thành công. Đang kích hoạt gói sử dụng.",
            LicensePaymentStatuses.Paid => "Đã nhận thanh toán. Server đang cấp gói sử dụng.",
            LicensePaymentStatuses.Expired => "Mã thanh toán đã hết thời gian chờ. Bạn có thể tạo mã mới.",
            LicensePaymentStatuses.Failed => "Không thể hoàn tất cấp gói. Vui lòng liên hệ hỗ trợ.",
            _ => "Đang chờ ngân hàng xác nhận giao dịch."
        };
        return new LicensePaymentStatusResponse(
            payment.OrderCode,
            status,
            NormalizeUtc(payment.ExpiresAtUtc),
            NormalizeUtc(now),
            NormalizeUtc(payment.PaidAtUtc),
            NormalizeUtc(payment.FulfilledAtUtc),
            payment.Status is LicensePaymentStatuses.Paid or LicensePaymentStatuses.Fulfilled,
            payment.Status == LicensePaymentStatuses.Fulfilled,
            isExpired,
            payment.FailureCode,
            message);
    }

    // SQL Server datetime2 does not persist DateTime.Kind. Values in these columns are UTC,
    // so restore the kind before serializing; otherwise clients in UTC+7 read them as local time.
    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value.HasValue ? NormalizeUtc(value.Value) : null;

    private string BuildQrImageUrl(LicensePayment payment)
    {
        var query = new QueryBuilder
        {
            { "acc", payment.ReceiverAccountNumberSnapshot },
            { "bank", payment.ReceiverBankCodeSnapshot },
            { "amount", payment.PriceSnapshotVnd.ToString("0", CultureInfo.InvariantCulture) },
            { "des", payment.TransferCode }
        };
        return $"{_options.QrBaseUrl.TrimEnd('?')}{query.ToQueryString()}";
    }

    private void EnsurePaymentsReady()
    {
        if (!_options.IsReady)
        {
            throw new AccountApiException(
                StatusCodes.Status503ServiceUnavailable,
                "payments_unavailable",
                "Thanh toán đang tạm ngừng. Vui lòng thử lại sau.");
        }
    }

    private void EnsureWebhookReady()
    {
        if (!_options.CanProcessWebhooks)
        {
            throw new AccountApiException(
                StatusCodes.Status503ServiceUnavailable,
                "payments_unavailable",
                "Webhook thanh toán chưa được cấu hình.");
        }
    }

    private static IReadOnlyList<string> ParseMarketingFeatures(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(value)?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Take(12)
                .ToArray() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string NormalizeAlphaNumeric(string? value) =>
        string.Concat((value ?? string.Empty).Where(char.IsLetterOrDigit)).ToUpperInvariant();

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized)
            ? null
            : normalized[..Math.Min(normalized.Length, maxLength)];
    }

    private static AccountApiException Validation(string code, string message) =>
        new(StatusCodes.Status422UnprocessableEntity, code, message);

    private static AccountApiException NotFound(string code, string message) =>
        new(StatusCodes.Status404NotFound, code, message);

    private static AccountApiException Conflict(string code, string message) =>
        new(StatusCodes.Status409Conflict, code, message);

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    [GeneratedRegex("^[A-Za-z0-9_-]{8,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdempotencyKeyRegex();
}
