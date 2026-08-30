using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Organizations;

namespace TOOL_SERVER.Organizations;

public sealed record BudgetSnapshot(
    Guid BudgetPeriodId,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    decimal HardLimit,
    decimal ReservedCost,
    decimal ActualCost,
    decimal RemainingBudget,
    string CurrencyCode);

public sealed record BudgetReservationResult(
    Guid ReservationId,
    Guid BudgetPeriodId,
    decimal ReservedAmount,
    string CurrencyCode);

public interface IAiBudgetService
{
    Task<BudgetSnapshot> GetSnapshotAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<BudgetReservationResult> ReserveAsync(
        Guid organizationId,
        string userId,
        Guid projectId,
        Guid providerRequestId,
        string operationKey,
        string providerCode,
        string modelCode,
        decimal amount,
        CancellationToken cancellationToken);

    Task SettleAsync(
        Guid reservationId,
        decimal actualAmount,
        Guid? organizationProviderCredentialId,
        object? usage,
        object? rateSnapshot,
        CancellationToken cancellationToken);

    Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken);
}

internal sealed class AiBudgetService(
    AiGovernanceDbContext dbContext,
    TimeProvider timeProvider) : IAiBudgetService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BudgetSnapshot> GetSnapshotAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var organization = await RequireOrganizationAsync(organizationId, cancellationToken);
        var period = await GetOrCreateCurrentPeriodAsync(organization, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToSnapshot(period);
    }

    public async Task<BudgetReservationResult> ReserveAsync(
        Guid organizationId,
        string userId,
        Guid projectId,
        Guid providerRequestId,
        string operationKey,
        string providerCode,
        string modelCode,
        decimal amount,
        CancellationToken cancellationToken)
    {
        if (amount <= 0)
        {
            throw new AccountApiException(
                StatusCodes.Status503ServiceUnavailable,
                "pricing_not_configured",
                "Chưa cấu hình đơn giá AI hợp lệ cho model này.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var existing = await dbContext.AiBudgetReservations.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.OperationKey == operationKey,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.ProjectId != projectId || existing.ProviderRequestId != providerRequestId)
            {
                throw new AccountApiException(
                    StatusCodes.Status409Conflict,
                    "idempotency_key_conflict",
                    "Idempotency key đã được dùng cho một yêu cầu khác.");
            }

            await transaction.CommitAsync(cancellationToken);
            return new BudgetReservationResult(
                existing.AiBudgetReservationId,
                existing.OrganizationBudgetPeriodId,
                existing.ReservedAmount,
                existing.CurrencyCode);
        }

        var organization = await RequireOrganizationAsync(organizationId, cancellationToken);
        var member = await dbContext.OrganizationMembers.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId &&
                 x.UserId == userId &&
                 x.Status == OrganizationMemberStatuses.Active,
            cancellationToken)
            ?? throw new AccountApiException(
                StatusCodes.Status403Forbidden,
                "organization_access_denied",
                "Tài khoản không còn là thành viên đang hoạt động của tổ chức.");
        var period = await GetOrCreateCurrentPeriodAsync(organization, cancellationToken);

        if (period.HardLimit <= 0 || period.ActualCost + period.ReservedCost + amount > period.HardLimit)
        {
            throw new AccountApiException(
                StatusCodes.Status409Conflict,
                "organization_budget_exceeded",
                "Ngân sách AI tháng của tổ chức không đủ cho yêu cầu này.");
        }

        if (member.MonthlyBudgetLimit is { } memberLimit)
        {
            var memberReserved = await dbContext.AiBudgetReservations
                .Where(x => x.OrganizationBudgetPeriodId == period.OrganizationBudgetPeriodId &&
                            x.UserId == userId &&
                            x.Status == BudgetReservationStatuses.Reserved)
                .SumAsync(x => (decimal?)x.ReservedAmount, cancellationToken) ?? 0;
            var memberActual = await dbContext.AiUsageLedger
                .Where(x => x.OrganizationBudgetPeriodId == period.OrganizationBudgetPeriodId &&
                            x.UserId == userId &&
                            (x.EntryKind == UsageLedgerEntryKinds.Actual ||
                             x.EntryKind == UsageLedgerEntryKinds.Adjustment))
                .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0;
            if (memberLimit <= 0 || memberActual + memberReserved + amount > memberLimit)
            {
                throw new AccountApiException(
                    StatusCodes.Status409Conflict,
                    "member_budget_exceeded",
                    "Hạn mức AI tháng của thành viên không đủ cho yêu cầu này.");
            }
        }

        var now = UtcNow();
        var reservation = new AiBudgetReservation
        {
            AiBudgetReservationId = Guid.NewGuid(),
            OrganizationBudgetPeriodId = period.OrganizationBudgetPeriodId,
            OrganizationId = organizationId,
            UserId = userId,
            ProjectId = projectId,
            ProviderRequestId = providerRequestId,
            OperationKey = operationKey,
            ProviderCode = providerCode,
            ModelCode = modelCode,
            ReservedAmount = amount,
            CurrencyCode = period.CurrencyCode,
            Status = BudgetReservationStatuses.Reserved,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(24)
        };
        period.ReservedCost += amount;
        period.UpdatedAtUtc = now;
        dbContext.AiBudgetReservations.Add(reservation);
        dbContext.AiUsageLedger.Add(CreateLedger(
            reservation,
            UsageLedgerEntryKinds.Reservation,
            amount,
            null,
            null,
            now));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new BudgetReservationResult(
            reservation.AiBudgetReservationId,
            period.OrganizationBudgetPeriodId,
            amount,
            period.CurrencyCode);
    }

    public async Task SettleAsync(
        Guid reservationId,
        decimal actualAmount,
        Guid? organizationProviderCredentialId,
        object? usage,
        object? rateSnapshot,
        CancellationToken cancellationToken)
    {
        if (actualAmount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actualAmount));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var reservation = await dbContext.AiBudgetReservations.SingleOrDefaultAsync(
            x => x.AiBudgetReservationId == reservationId,
            cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy khoản giữ ngân sách AI.");
        if (reservation.Status == BudgetReservationStatuses.Settled)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        if (reservation.Status != BudgetReservationStatuses.Reserved)
        {
            throw new InvalidOperationException("Khoản giữ ngân sách AI không còn hiệu lực.");
        }

        var period = await dbContext.OrganizationBudgetPeriods.SingleAsync(
            x => x.OrganizationBudgetPeriodId == reservation.OrganizationBudgetPeriodId,
            cancellationToken);
        var now = UtcNow();
        period.ReservedCost = Math.Max(0, period.ReservedCost - reservation.ReservedAmount);
        period.ActualCost += actualAmount;
        period.UpdatedAtUtc = now;
        reservation.ActualAmount = actualAmount;
        reservation.Status = BudgetReservationStatuses.Settled;
        reservation.SettledAtUtc = now;

        var actualLedger = CreateLedger(
            reservation,
            UsageLedgerEntryKinds.Actual,
            actualAmount,
            organizationProviderCredentialId,
            usage,
            now);
        actualLedger.RateSnapshotJson = rateSnapshot is null
            ? null
            : JsonSerializer.Serialize(rateSnapshot, JsonOptions);
        dbContext.AiUsageLedger.Add(actualLedger);
        dbContext.AiUsageLedger.Add(CreateLedger(
            reservation,
            UsageLedgerEntryKinds.Release,
            reservation.ReservedAmount,
            organizationProviderCredentialId,
            null,
            now));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var reservation = await dbContext.AiBudgetReservations.SingleOrDefaultAsync(
            x => x.AiBudgetReservationId == reservationId,
            cancellationToken);
        if (reservation is null || reservation.Status != BudgetReservationStatuses.Reserved)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var period = await dbContext.OrganizationBudgetPeriods.SingleAsync(
            x => x.OrganizationBudgetPeriodId == reservation.OrganizationBudgetPeriodId,
            cancellationToken);
        var now = UtcNow();
        period.ReservedCost = Math.Max(0, period.ReservedCost - reservation.ReservedAmount);
        period.UpdatedAtUtc = now;
        reservation.Status = BudgetReservationStatuses.Released;
        reservation.SettledAtUtc = now;
        dbContext.AiUsageLedger.Add(CreateLedger(
            reservation,
            UsageLedgerEntryKinds.Release,
            reservation.ReservedAmount,
            null,
            null,
            now));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Organization> RequireOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken) =>
        await dbContext.Organizations.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.Status == OrganizationStatuses.Active,
            cancellationToken)
        ?? throw new AccountApiException(
            StatusCodes.Status403Forbidden,
            "organization_unavailable",
            "Tổ chức không tồn tại hoặc đã bị khóa.");

    private async Task<OrganizationBudgetPeriod> GetOrCreateCurrentPeriodAsync(
        Organization organization,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var startsAt = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endsAt = startsAt.AddMonths(1);
        var period = await dbContext.OrganizationBudgetPeriods.SingleOrDefaultAsync(
            x => x.OrganizationId == organization.OrganizationId && x.StartsAtUtc == startsAt,
            cancellationToken);
        if (period is not null)
        {
            return period;
        }

        period = new OrganizationBudgetPeriod
        {
            OrganizationBudgetPeriodId = Guid.NewGuid(),
            OrganizationId = organization.OrganizationId,
            StartsAtUtc = startsAt,
            EndsAtUtc = endsAt,
            HardLimit = organization.MonthlyBudgetLimit,
            CurrencyCode = organization.CurrencyCode,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.OrganizationBudgetPeriods.Add(period);
        await dbContext.SaveChangesAsync(cancellationToken);
        return period;
    }

    private static AiUsageLedgerEntry CreateLedger(
        AiBudgetReservation reservation,
        string kind,
        decimal amount,
        Guid? credentialId,
        object? details,
        DateTime now) =>
        new()
        {
            AiUsageLedgerEntryId = Guid.NewGuid(),
            OrganizationBudgetPeriodId = reservation.OrganizationBudgetPeriodId,
            OrganizationId = reservation.OrganizationId,
            UserId = reservation.UserId,
            ProjectId = reservation.ProjectId,
            ProviderRequestId = reservation.ProviderRequestId,
            OrganizationProviderCredentialId = credentialId,
            ProviderCode = reservation.ProviderCode,
            ModelCode = reservation.ModelCode,
            EntryKind = kind,
            Amount = amount,
            CurrencyCode = reservation.CurrencyCode,
            UsageJson = details is null ? null : JsonSerializer.Serialize(details, JsonOptions),
            OccurredAtUtc = now,
            CreatedAtUtc = now
        };

    private static BudgetSnapshot ToSnapshot(OrganizationBudgetPeriod period) =>
        new(
            period.OrganizationBudgetPeriodId,
            period.StartsAtUtc,
            period.EndsAtUtc,
            period.HardLimit,
            period.ReservedCost,
            period.ActualCost,
            Math.Max(0, period.HardLimit - period.ReservedCost - period.ActualCost),
            period.CurrencyCode);

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
