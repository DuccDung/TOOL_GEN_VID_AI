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
using TOOL_SERVER.Organizations;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_SERVER.Payments;

public sealed partial class LicensePaymentService(
    AccountDbContext dbContext,
    IOptions<SepayPaymentOptions> options,
    TimeProvider timeProvider,
    ILicensePaymentTelemetry telemetry,
    ILogger<LicensePaymentService> logger,
    IOrganizationSeatProvisioningService? seatProvisioningService = null,
    ISepayPaymentConfiguration? paymentConfiguration = null) : ILicensePaymentService
{
    private readonly SepayPaymentOptions _options = options.Value;

    public async Task<IReadOnlyList<LicenseOfferResponse>> GetOffersAsync(CancellationToken cancellationToken)
    {
        var paymentOptions = await ResolvePaymentOptionsAsync(cancellationToken);
        if (!paymentOptions.IsReady)
        {
            return [];
        }

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

        var allocatedPlans = plans.Where(x => LicenseProductPolicy.RequiresOrganizationAllocation(x.FeatureFlagsJson)).ToArray();
        var availability = seatProvisioningService is null || allocatedPlans.Length == 0
            ? new Dictionary<Guid, OrganizationSeatAvailability>()
            : await seatProvisioningService.GetAvailabilityAsync(
                allocatedPlans.Select(x => x.LicensePlanId).ToArray(),
                cancellationToken);
        return plans.Where(x => LicenseProductPolicy.Scope(x.FeatureFlagsJson) != "invalid").Select(x =>
        {
            availability.TryGetValue(x.LicensePlanId, out var seats);
            return new LicenseOfferResponse(
                x.LicensePlanId,
                x.PlanCode,
                x.Name,
                x.Description,
                x.SalePriceVnd!.Value,
                x.DefaultDurationDays!.Value,
                x.MaxActivatedDevices,
                ParseMarketingFeatures(x.MarketingFeaturesJson),
                x.DisplayOrder,
                LicenseProductPolicy.IsLocalOnly(x.FeatureFlagsJson) || (seats?.IsAvailable ?? false),
                seats?.PoolName,
                seats?.AvailableSeats,
                LicenseProductPolicy.Scope(x.FeatureFlagsJson),
                LicenseProductPolicy.CloudAllowed(x.FeatureFlagsJson));
        })
            .ToArray();
    }

    public Task<LicensePaymentCheckoutResponse> CreateOrReuseAsync(
        string userId,
        CreateLicensePaymentRequest request,
        CancellationToken cancellationToken)
    {
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();
        return executionStrategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            return await CreateOrReuseCoreAsync(userId, request, cancellationToken);
        });
    }

    public async Task<IReadOnlyList<LicenseOfferResponse>> GetOffersForUserAsync(string userId, CancellationToken ct)
    {
        var offers = await GetOffersAsync(ct);
        var now = UtcNow();
        var tail = await LicenseSchedule.TailAsync(dbContext, userId, now, ct);
        if (tail is { ExpiresAtUtc: null }) return offers.Select(x => x with {
            OrganizationSeatAvailable = false, PurchaseBlockedReason = "Tài khoản đang có gói không giới hạn thời hạn. Vui lòng liên hệ quản trị viên trước khi đổi gói."
        }).ToArray();
        var starts = tail?.ExpiresAtUtc ?? now;
        return offers.Select(x => x with { ExpectedStartsAtUtc = NormalizeUtc(starts),
            ExpectedExpiresAtUtc = NormalizeUtc(starts.AddDays(x.DurationDays)) }).ToArray();
    }

    private async Task<LicensePaymentCheckoutResponse> CreateOrReuseCoreAsync(
        string userId,
        CreateLicensePaymentRequest request,
        CancellationToken cancellationToken)
    {
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

        var paymentOptions = await ResolvePaymentOptionsAsync(cancellationToken);
        EnsurePaymentsReady(paymentOptions);

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
            return await BuildCheckoutResponseAsync(replay, now, true, cancellationToken);
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
                        ((x.Status == LicensePaymentStatuses.Pending && x.ExpiresAtUtc > now) ||
                         x.Status == LicensePaymentStatuses.Paid))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            if (seatProvisioningService is not null && !LicenseProductPolicy.IsLocalOnly(existing.EntitlementSnapshotJson) && existing.Status == LicensePaymentStatuses.Pending)
            {
                await seatProvisioningService.ReserveAsync(existing, now, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            logger.LogInformation(
                "Reused open license payment {LicensePaymentId} ({OrderCode}) with status {PaymentStatus}.",
                existing.LicensePaymentId,
                existing.OrderCode,
                existing.Status);
            return await BuildCheckoutResponseAsync(existing, now, true, cancellationToken);
        }

        if (LicenseProductPolicy.Scope(plan.FeatureFlagsJson) == "invalid")
            throw Conflict("invalid_product_scope", "Quyền của gói chưa hợp lệ. Vui lòng liên hệ quản trị viên.");
        if (await LicenseSchedule.TailAsync(dbContext, userId, now, cancellationToken) is { ExpiresAtUtc: null })
            throw Conflict("license_schedule_unavailable", "Tài khoản đang có gói không giới hạn thời hạn. Cần quản trị viên xử lý trước khi đổi gói.");
        var payment = new LicensePayment
        {
            LicensePaymentId = Guid.NewGuid(),
            UserId = userId,
            LicensePlanId = plan.LicensePlanId,
            OrderCode = await CreateUniqueCodeAsync("VMO", x => x.OrderCode, cancellationToken),
            TransferCode = await CreateUniqueCodeAsync(
                paymentOptions.TransferCodePrefix.Trim().ToUpperInvariant(),
                x => x.TransferCode,
                cancellationToken),
            IdempotencyKey = idempotencyKey,
            PriceSnapshotVnd = decimal.Truncate(plan.SalePriceVnd!.Value),
            DurationSnapshotDays = plan.DefaultDurationDays!.Value,
            PlanCodeSnapshot = plan.PlanCode,
            PlanNameSnapshot = plan.Name,
            EntitlementSnapshotJson = plan.FeatureFlagsJson ?? "{}",
            Status = LicensePaymentStatuses.Pending,
            ReceiverBankCodeSnapshot = paymentOptions.ReceiverBankCode.Trim(),
            ReceiverAccountNumberSnapshot = paymentOptions.ReceiverAccountNumber.Trim(),
            ReceiverAccountNameSnapshot = paymentOptions.ReceiverAccountName.Trim(),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(paymentOptions.PaymentExpireMinutes)
        };
        dbContext.LicensePayments.Add(payment);
        if (seatProvisioningService is not null && !LicenseProductPolicy.IsLocalOnly(payment.EntitlementSnapshotJson))
        {
            await seatProvisioningService.ReserveAsync(payment, now, cancellationToken);
        }

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
                return await BuildCheckoutResponseAsync(
                    concurrentReplay,
                    UtcNow(),
                    true,
                    cancellationToken);
            }
            throw;
        }

        return await BuildCheckoutResponseAsync(payment, now, false, cancellationToken);
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
        return await BuildStatusResponseAsync(payment, now, cancellationToken);
    }

    public async Task<CurrentLicensePaymentResponse> GetCurrentAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        // Existing orders remain readable even if sales are disabled or the receiver changes.
        var now = UtcNow();
        var payment = await dbContext.LicensePayments
            .AsNoTracking()
            .Where(x => x.UserId == userId &&
                        ((x.Status == LicensePaymentStatuses.Pending && x.ExpiresAtUtc > now) ||
                         x.Status == LicensePaymentStatuses.Paid))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return new CurrentLicensePaymentResponse(payment is null
            ? null
            : await BuildCheckoutResponseAsync(payment, now, true, cancellationToken));
    }

    public async Task HandleWebhookAsync(
        SepayWebhookPayload payload,
        CancellationToken cancellationToken)
    {
        // Match the receiver snapshot of the order, never the currently selected bank account.
        if (payload.Id <= 0 ||
            !string.Equals(payload.TransferType?.Trim(), "in", StringComparison.OrdinalIgnoreCase) ||
            payload.TransferAmount <= 0)
        {
            return;
        }

        var receiverAccount = NormalizeAlphaNumeric(payload.AccountNumber);
        if (string.IsNullOrEmpty(receiverAccount))
        {
            logger.LogWarning("Ignored SePay transaction {ProviderTransactionId}: receiver account did not match.", payload.Id);
            telemetry.RecordUnmatchedWebhook(LicensePaymentWebhookMismatchReason.ReceiverAccount);
            return;
        }

        var executionStrategy = dbContext.Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;

            var duplicatePayment = await dbContext.LicensePayments.SingleOrDefaultAsync(
                x => x.ProviderTransactionId == payload.Id,
                cancellationToken);
            if (duplicatePayment is not null)
            {
                if (NormalizeAlphaNumeric(duplicatePayment.ReceiverAccountNumberSnapshot) != receiverAccount ||
                    duplicatePayment.PriceSnapshotVnd != payload.TransferAmount)
                {
                    telemetry.RecordUnmatchedWebhook(LicensePaymentWebhookMismatchReason.PaymentDetails);
                    return;
                }
                if (duplicatePayment.Status == LicensePaymentStatuses.Paid)
                {
                    await TryFulfillPaymentAsync(duplicatePayment, UtcNow(), cancellationToken);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
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
                telemetry.RecordUnmatchedWebhook(
                    NormalizeAlphaNumeric(payment.ReceiverAccountNumberSnapshot) != receiverAccount
                        ? LicensePaymentWebhookMismatchReason.ReceiverAccount
                        : LicensePaymentWebhookMismatchReason.PaymentDetails);
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
            await TryFulfillPaymentAsync(payment, now, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            if (payment.Status == LicensePaymentStatuses.Fulfilled)
            {
                logger.LogInformation(
                    "Fulfilled license payment {LicensePaymentId} ({OrderCode}) with status {PaymentStatus}.",
                    payment.LicensePaymentId,
                    payment.OrderCode,
                    payment.Status);
            }
            else
            {
                logger.LogWarning(
                    "License payment {LicensePaymentId} ({OrderCode}) is paid but provisioning is pending with {FailureCode}.",
                    payment.LicensePaymentId,
                    payment.OrderCode,
                    payment.FailureCode);
            }
        });
    }

    public async Task<bool> RetryProvisioningAsync(
        Guid licensePaymentId,
        CancellationToken cancellationToken)
    {
        var fulfilled = false;
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;
            var payment = await dbContext.LicensePayments.SingleOrDefaultAsync(
                x => x.LicensePaymentId == licensePaymentId,
                cancellationToken)
                ?? throw NotFound("license_payment_not_found", "Không tìm thấy giao dịch thanh toán.");
            if (payment.Status == LicensePaymentStatuses.Fulfilled)
            {
                fulfilled = true;
            }
            else if (payment.Status == LicensePaymentStatuses.Paid)
            {
                fulfilled = await TryFulfillPaymentAsync(payment, UtcNow(), cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                throw Conflict(
                    "license_payment_not_paid",
                    "Chỉ có thể cấp lại tổ chức cho giao dịch đã nhận tiền.");
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        });
        return fulfilled;
    }

    private async Task<bool> TryFulfillPaymentAsync(
        LicensePayment payment,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var tail = await LicenseSchedule.TailAsync(dbContext, payment.UserId, now, cancellationToken);
        try
        {
            if (tail is { ExpiresAtUtc: null })
                throw Conflict("license_schedule_unavailable", "Cần quản trị viên xử lý gói không giới hạn thời hạn trước khi cấp gói mới.");
            if (seatProvisioningService is not null && !LicenseProductPolicy.IsLocalOnly(payment.EntitlementSnapshotJson))
            {
                await seatProvisioningService.ReserveAsync(payment, now, cancellationToken);
            }
        }
        catch (AccountApiException exception) when (
            exception.Code is "organization_capacity_unavailable" or "license_plan_pool_not_configured" or "organization_not_ready" or "license_schedule_unavailable")
        {
            var shouldAudit = payment.FailureCode != exception.Code;
            payment.Status = LicensePaymentStatuses.Paid;
            payment.FailureCode = exception.Code;
            if (shouldAudit)
            {
                dbContext.AccountAuditLogs.Add(new AccountAuditLog
                {
                    UserId = payment.UserId,
                    EventType = "LicensePaymentProvisioningPending",
                    Succeeded = false,
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        payment.LicensePaymentId,
                        payment.OrderCode,
                        payment.LicensePlanId,
                        exception.Code
                    }),
                    OccurredAtUtc = now
                });
            }
            return false;
        }

        UserLicense fulfilledLicense;
        if (tail is not null && tail.LicensePlanId == payment.LicensePlanId &&
            LicenseSchedule.SameRights(tail.EntitlementSnapshotJson ?? tail.LicensePlan.FeatureFlagsJson, payment.EntitlementSnapshotJson) &&
            tail.ExpiresAtUtc is { } currentExpiry)
        {
            tail.ExpiresAtUtc = currentExpiry.AddDays(payment.DurationSnapshotDays);
            tail.EntitlementSnapshotJson ??= payment.EntitlementSnapshotJson;
            tail.UpdatedAtUtc = now;
            fulfilledLicense = tail;
        }
        else
        {
            var startsAt = tail?.ExpiresAtUtc is { } activeExpiry && activeExpiry > now
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

        if (seatProvisioningService is not null && !LicenseProductPolicy.IsLocalOnly(payment.EntitlementSnapshotJson))
        {
            await seatProvisioningService.ActivateAsync(payment, fulfilledLicense, now, cancellationToken);
        }
        else if (seatProvisioningService is not null && await dbContext.OrganizationSeatAssignments.AnyAsync(x =>
            x.LicensePaymentId == payment.LicensePaymentId && x.Status == "Reserved", cancellationToken))
        {
            // Orders created by the old Local flow may still hold a seat. Release it without creating membership.
            await seatProvisioningService.ReleaseReservationAsync(payment.LicensePaymentId, "local_license_only", now, cancellationToken);
        }
        payment.FulfilledUserLicenseId = fulfilledLicense.UserLicenseId;
        payment.FulfilledAtUtc = now;
        payment.FailureCode = null;
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
        telemetry.RecordFulfilled();
        return true;
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
            if (seatProvisioningService is not null)
            {
                await seatProvisioningService.ReleaseReservationAsync(
                    payment.LicensePaymentId,
                    "payment_expired",
                    now,
                    cancellationToken);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Expired license payment {LicensePaymentId} ({OrderCode}) with status {PaymentStatus}.",
                payment.LicensePaymentId,
                payment.OrderCode,
                payment.Status);
            telemetry.RecordExpired();
        }
    }

    private async Task<LicensePaymentCheckoutResponse> BuildCheckoutResponseAsync(
        LicensePayment payment,
        DateTime now,
        bool reused,
        CancellationToken cancellationToken)
    {
        var assignment = seatProvisioningService is null || LicenseProductPolicy.IsLocalOnly(payment.EntitlementSnapshotJson)
            ? null
            : await seatProvisioningService.GetSnapshotAsync(payment.LicensePaymentId, cancellationToken);
        var term = await PaymentTermAsync(payment, now, cancellationToken);
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
            isExpired,
            assignment?.OrganizationId,
            assignment?.OrganizationName,
            assignment?.Status, term.Starts, term.Ends, term.Scheduled);
    }

    private async Task<LicensePaymentStatusResponse> BuildStatusResponseAsync(
        LicensePayment payment,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var assignment = seatProvisioningService is null || LicenseProductPolicy.IsLocalOnly(payment.EntitlementSnapshotJson)
            ? null
            : await seatProvisioningService.GetSnapshotAsync(payment.LicensePaymentId, cancellationToken);
        var term = await PaymentTermAsync(payment, now, cancellationToken);
        var isExpired = payment.Status == LicensePaymentStatuses.Expired ||
                        (payment.Status == LicensePaymentStatuses.Pending && payment.ExpiresAtUtc <= now);
        var status = isExpired ? LicensePaymentStatuses.Expired : payment.Status;
        var message = status switch
        {
            LicensePaymentStatuses.Fulfilled when term.Scheduled => "Đã thanh toán và xếp lịch. Gói mới có hiệu lực sau khi gói trước kết thúc.",
            LicensePaymentStatuses.Fulfilled => "Thanh toán thành công. Gói sử dụng đã được cấp.",
            LicensePaymentStatuses.Paid => "Đã thanh toán — đang chờ kích hoạt. Không cần thanh toán lại; hệ thống hoặc quản trị viên sẽ xử lý cấp gói.",
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
            message,
            assignment?.OrganizationId,
            assignment?.OrganizationName,
            assignment?.Status, term.Starts, term.Ends, term.Scheduled);
    }

    private async Task<(DateTime? Starts, DateTime? Ends, bool Scheduled)> PaymentTermAsync(
        LicensePayment payment, DateTime now, CancellationToken ct)
    {
        if (payment.FulfilledUserLicenseId is { } id)
        {
            var license = await dbContext.UserLicenses.AsNoTracking().SingleOrDefaultAsync(x => x.UserLicenseId == id, ct);
            if (license is not null) return (NormalizeUtc(license.StartsAtUtc), NormalizeUtc(license.ExpiresAtUtc), license.StartsAtUtc > now);
        }
        var tail = await LicenseSchedule.TailAsync(dbContext, payment.UserId, now, ct);
        if (tail is { ExpiresAtUtc: null }) return (null, null, false);
        var starts = tail?.ExpiresAtUtc ?? now;
        return (NormalizeUtc(starts), NormalizeUtc(starts.AddDays(payment.DurationSnapshotDays)), starts > now);
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

    private Task<SepayPaymentOptions> ResolvePaymentOptionsAsync(CancellationToken cancellationToken) =>
        paymentConfiguration?.GetOptionsAsync(cancellationToken) ?? Task.FromResult(_options);

    private static void EnsurePaymentsReady(SepayPaymentOptions configuration)
    {
        if (!configuration.IsReady)
        {
            throw new AccountApiException(
                StatusCodes.Status503ServiceUnavailable,
                "payments_unavailable",
                "Thanh toán đang tạm ngừng. Vui lòng thử lại sau.");
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
