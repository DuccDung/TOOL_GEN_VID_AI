using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Organizations;

namespace TOOL_TESTS.Organizations;

public sealed class OrganizationProvisioningAdminServiceTests
{
    [Fact]
    public async Task PoolSummary_SeparatesConfiguredCapacityFromCurrentlyAllocatableCapacity()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await using var accountDb = new AccountDbContext(
            new DbContextOptionsBuilder<AccountDbContext>()
                .UseInMemoryDatabase($"provisioning-admin-account-{suffix}")
                .Options);
        await using var governanceDb = new AiGovernanceDbContext(
            new DbContextOptionsBuilder<AiGovernanceDbContext>()
                .UseInMemoryDatabase($"provisioning-admin-governance-{suffix}")
                .Options);
        await using var providerDb = new ProviderAdminDbContext(
            new DbContextOptionsBuilder<ProviderAdminDbContext>()
                .UseInMemoryDatabase($"provisioning-admin-provider-{suffix}")
                .Options);
        var now = new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc);
        var poolId = Guid.NewGuid();
        var readyOrganizationId = Guid.NewGuid();
        var staleOrganizationId = Guid.NewGuid();
        var uncheckedOrganizationId = Guid.NewGuid();
        var disabledOrganizationId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        accountDb.OrganizationPools.Add(new OrganizationPool
        {
            OrganizationPoolId = poolId,
            Code = "veo-production",
            Name = "Veo Production",
            Status = OrganizationPoolStatuses.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        accountDb.Organizations.AddRange(
            Organization(readyOrganizationId, "ready", now),
            Organization(staleOrganizationId, "stale", now),
            Organization(uncheckedOrganizationId, "unchecked", now),
            Organization(disabledOrganizationId, "disabled", now));
        accountDb.LicensePlans.Add(new LicensePlan
        {
            LicensePlanId = planId,
            PlanCode = "veo-monthly",
            Name = "Veo Monthly",
            MaxActivatedDevices = 1,
            OfflineGraceHours = 24,
            DefaultDurationDays = 30,
            SalePriceVnd = 500000,
            IsPublic = true,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        accountDb.OrganizationPoolOrganizations.AddRange(
            PoolOrganization(poolId, readyOrganizationId, 10, 2, 1, true, true, now),
            PoolOrganization(poolId, staleOrganizationId, 20, 0, 0, true, true, now),
            PoolOrganization(poolId, uncheckedOrganizationId, 30, 0, 0, true, false, now),
            PoolOrganization(poolId, disabledOrganizationId, 40, 0, 0, false, true, now));
        accountDb.LicensePlanOrganizationPools.Add(new LicensePlanOrganizationPool
        {
            LicensePlanId = planId,
            OrganizationPoolId = poolId,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await accountDb.SaveChangesAsync();

        var readiness = new FakeReadinessEvaluator(new Dictionary<Guid, bool>
        {
            [readyOrganizationId] = true,
            [staleOrganizationId] = false,
            [disabledOrganizationId] = true
        });
        var service = new OrganizationProvisioningAdminService(
            accountDb,
            governanceDb,
            providerDb,
            new FixedTimeProvider(now),
            null!,
            readiness);

        var summary = Assert.Single(await service.GetPoolsAsync(CancellationToken.None));

        Assert.Equal(100, summary.SeatCapacity);
        Assert.Equal(97, summary.AvailableSeats);
        Assert.Equal(1, summary.AllocatableOrganizationCount);
        Assert.Equal(1, summary.ActiveLicensePlanCount);
        Assert.Equal(10, summary.AllocatableSeatCapacity);
        Assert.Equal(7, summary.AllocatableAvailableSeats);

        var plan = await accountDb.LicensePlans.SingleAsync();
        plan.IsPublic = false;
        await accountDb.SaveChangesAsync();

        summary = Assert.Single(await service.GetPoolsAsync(CancellationToken.None));
        Assert.Equal(0, summary.ActiveLicensePlanCount);
        Assert.Equal(0, summary.AllocatableAvailableSeats);

        plan.IsPublic = true;
        var pool = await accountDb.OrganizationPools.SingleAsync();
        pool.Status = OrganizationPoolStatuses.Inactive;
        await accountDb.SaveChangesAsync();

        summary = Assert.Single(await service.GetPoolsAsync(CancellationToken.None));
        Assert.Equal(0, summary.AllocatableSeatCapacity);
        Assert.Equal(0, summary.AllocatableAvailableSeats);
    }

    [Fact]
    public async Task AssignmentProjection_IncludesPaidStatusForProvisioningAttention()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await using var accountDb = new AccountDbContext(
            new DbContextOptionsBuilder<AccountDbContext>()
                .UseInMemoryDatabase($"provisioning-assignment-account-{suffix}")
                .Options);
        await using var governanceDb = new AiGovernanceDbContext(
            new DbContextOptionsBuilder<AiGovernanceDbContext>()
                .UseInMemoryDatabase($"provisioning-assignment-governance-{suffix}")
                .Options);
        await using var providerDb = new ProviderAdminDbContext(
            new DbContextOptionsBuilder<ProviderAdminDbContext>()
                .UseInMemoryDatabase($"provisioning-assignment-provider-{suffix}")
                .Options);
        var now = new DateTime(2026, 9, 4, 9, 0, 0, DateTimeKind.Utc);
        var poolId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        const string userId = "paid-user";

        accountDb.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = "paid@example.test",
            NormalizedUserName = "PAID@EXAMPLE.TEST",
            Email = "paid@example.test",
            NormalizedEmail = "PAID@EXAMPLE.TEST",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        accountDb.OrganizationPools.Add(new OrganizationPool
        {
            OrganizationPoolId = poolId,
            Code = "paid-pool",
            Name = "Paid Pool",
            Status = OrganizationPoolStatuses.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        accountDb.Organizations.Add(Organization(organizationId, "paid-org", now));
        accountDb.LicensePlans.Add(new LicensePlan
        {
            LicensePlanId = planId,
            PlanCode = "paid-plan",
            Name = "Paid Plan",
            MaxActivatedDevices = 1,
            OfflineGraceHours = 24,
            DefaultDurationDays = 30,
            SalePriceVnd = 500000,
            IsPublic = true,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        accountDb.LicensePayments.Add(new LicensePayment
        {
            LicensePaymentId = paymentId,
            UserId = userId,
            LicensePlanId = planId,
            OrderCode = "VM-PAID-001",
            TransferCode = "VM-PAID-001",
            IdempotencyKey = "paid-001",
            PriceSnapshotVnd = 500000,
            DurationSnapshotDays = 30,
            PlanCodeSnapshot = "paid-plan",
            PlanNameSnapshot = "Paid Plan",
            Status = LicensePaymentStatuses.Paid,
            ReceiverBankCodeSnapshot = "TEST",
            ReceiverAccountNumberSnapshot = "000000",
            ReceiverAccountNameSnapshot = "TEST",
            FailureCode = "organization_capacity_unavailable",
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(15),
            PaidAtUtc = now
        });
        accountDb.OrganizationSeatAssignments.Add(new OrganizationSeatAssignment
        {
            OrganizationSeatAssignmentId = Guid.NewGuid(),
            OrganizationPoolId = poolId,
            OrganizationId = organizationId,
            UserId = userId,
            LicensePlanId = planId,
            LicensePaymentId = paymentId,
            Status = OrganizationSeatAssignmentStatuses.Released,
            ConsumesSeat = false,
            MembershipManaged = true,
            ReservedAtUtc = now.AddMinutes(-10),
            ReservationExpiresAtUtc = now.AddMinutes(5),
            ReleasedAtUtc = now,
            ReleaseReason = "payment_expired",
            CreatedAtUtc = now.AddMinutes(-10),
            UpdatedAtUtc = now
        });
        await accountDb.SaveChangesAsync();

        var service = new OrganizationProvisioningAdminService(
            accountDb,
            governanceDb,
            providerDb,
            new FixedTimeProvider(now),
            null!,
            new FakeReadinessEvaluator(new Dictionary<Guid, bool>()));

        var assignment = Assert.Single(await service.GetAssignmentsAsync(null, 10, CancellationToken.None));

        Assert.Equal(LicensePaymentStatuses.Paid, assignment.PaymentStatus);
        Assert.Equal("organization_capacity_unavailable", assignment.FailureCode);
    }

    private static Organization Organization(Guid id, string code, DateTime now) => new()
    {
        OrganizationId = id,
        Code = code,
        Name = code,
        Status = OrganizationStatuses.Active,
        MonthlyBudgetLimit = 100,
        CurrencyCode = "USD",
        CreatedByUserId = "admin",
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };

    private static OrganizationPoolOrganization PoolOrganization(
        Guid poolId,
        Guid organizationId,
        int capacity,
        int active,
        int reserved,
        bool autoAssignment,
        bool storedReady,
        DateTime now) => new()
    {
        OrganizationPoolId = poolId,
        OrganizationId = organizationId,
        SeatCapacity = capacity,
        ActiveSeatCount = active,
        ReservedSeatCount = reserved,
        Priority = 100,
        IsAutoAssignmentEnabled = autoAssignment,
        IsReady = storedReady,
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };

    private sealed class FakeReadinessEvaluator(IReadOnlyDictionary<Guid, bool> values)
        : IOrganizationProvisioningReadinessEvaluator
    {
        public Task<OrganizationProvisioningReadiness> EvaluateAsync(
            Guid organizationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new OrganizationProvisioningReadiness(
                values.GetValueOrDefault(organizationId),
                values.GetValueOrDefault(organizationId) ? "Sẵn sàng." : "Readiness hiện hành không đạt."));
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
