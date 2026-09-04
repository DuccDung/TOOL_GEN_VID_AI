using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TOOL_SERVER.Accounts;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Organizations;
using TOOL_SERVER.Payments;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_TESTS.Payments;

public sealed class OrganizationSeatProvisioningTests
{
    [Fact]
    public async Task CreatePayment_ReservesSeatBeforeReturningQr()
    {
        await using var fixture = await Fixture.CreateAsync();

        var checkout = await fixture.CreatePaymentAsync();

        var assignment = await fixture.Db.OrganizationSeatAssignments.SingleAsync();
        var capacity = await fixture.Db.OrganizationPoolOrganizations.SingleAsync();
        Assert.Equal(OrganizationSeatAssignmentStatuses.Reserved, assignment.Status);
        Assert.Equal(1, capacity.ReservedSeatCount);
        Assert.Equal(fixture.OrganizationId, checkout.AssignedOrganizationId);
        Assert.Equal("Tổ chức 1", checkout.AssignedOrganizationName);
        Assert.Equal(OrganizationSeatAssignmentStatuses.Reserved, checkout.ProvisioningStatus);
    }

    [Fact]
    public async Task CreatePayment_SkipsFullOrganizationAndUsesNextPriority()
    {
        await using var fixture = await Fixture.CreateAsync(addSecondOrganization: true);
        var first = await fixture.Db.OrganizationPoolOrganizations.SingleAsync(
            x => x.OrganizationId == fixture.OrganizationId);
        first.ActiveSeatCount = first.SeatCapacity;
        await fixture.Db.SaveChangesAsync();

        var checkout = await fixture.CreatePaymentAsync();

        Assert.Equal(fixture.SecondOrganizationId, checkout.AssignedOrganizationId);
    }

    [Fact]
    public async Task CreatePayment_WhenPoolIsFull_DoesNotCreatePayment()
    {
        await using var fixture = await Fixture.CreateAsync();
        var capacity = await fixture.Db.OrganizationPoolOrganizations.SingleAsync();
        capacity.ActiveSeatCount = capacity.SeatCapacity;
        await fixture.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<TOOL_SERVER.Authentication.AccountApiException>(
            () => fixture.CreatePaymentAsync());

        Assert.Equal("organization_capacity_unavailable", exception.Code);
        Assert.Equal(0, await fixture.Db.LicensePayments.CountAsync());
        Assert.Equal(0, await fixture.Db.OrganizationSeatAssignments.CountAsync());
    }

    [Fact]
    public async Task MultipleBuyers_NeverExceedConfiguredCapacity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var configuration = await fixture.Db.OrganizationPoolOrganizations.SingleAsync();
        configuration.SeatCapacity = 1;
        await fixture.Db.SaveChangesAsync();

        await fixture.CreatePaymentAsync("buyer-one-12345678", "user-1");
        var exception = await Assert.ThrowsAsync<TOOL_SERVER.Authentication.AccountApiException>(() =>
            fixture.CreatePaymentAsync("buyer-two-12345678", "user-2"));

        Assert.Equal("organization_capacity_unavailable", exception.Code);
        Assert.Equal(1, (await fixture.Db.OrganizationPoolOrganizations.SingleAsync()).ReservedSeatCount);
        Assert.Equal(1, await fixture.Db.OrganizationSeatAssignments.CountAsync());
    }

    [Fact]
    public async Task Webhook_FulfillsLicenseSeatAndMemberTogether()
    {
        await using var fixture = await Fixture.CreateAsync(memberBudget: 25m);
        var checkout = await fixture.CreatePaymentAsync();

        await fixture.Service.HandleWebhookAsync(fixture.Webhook(checkout), CancellationToken.None);

        var payment = await fixture.Db.LicensePayments.SingleAsync();
        var assignment = await fixture.Db.OrganizationSeatAssignments.SingleAsync();
        var member = await fixture.Db.OrganizationMembers.SingleAsync();
        var capacity = await fixture.Db.OrganizationPoolOrganizations.SingleAsync();
        Assert.Equal(LicensePaymentStatuses.Fulfilled, payment.Status);
        Assert.NotNull(payment.FulfilledUserLicenseId);
        Assert.Equal(OrganizationSeatAssignmentStatuses.Active, assignment.Status);
        Assert.Equal(payment.FulfilledUserLicenseId, assignment.UserLicenseId);
        Assert.Equal(OrganizationMemberRoles.Member, member.Role);
        Assert.Equal(OrganizationMemberStatuses.Active, member.Status);
        Assert.True(member.IsProvisioningManaged);
        Assert.Equal(25m, member.MonthlyBudgetLimit);
        Assert.Equal(1, capacity.ActiveSeatCount);
        Assert.Equal(0, capacity.ReservedSeatCount);

        var currentLicense = await new AccountManagementService(fixture.Db, fixture.Time)
            .GetCurrentLicenseAsync("user-1", Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(fixture.OrganizationId, currentLicense.AssignedOrganizationId);
        Assert.Equal("Tổ chức 1", currentLicense.AssignedOrganizationName);
    }

    [Fact]
    public async Task DuplicateWebhook_DoesNotDuplicateLicenseMembershipOrSeat()
    {
        await using var fixture = await Fixture.CreateAsync();
        var checkout = await fixture.CreatePaymentAsync();
        var webhook = fixture.Webhook(checkout);

        await fixture.Service.HandleWebhookAsync(webhook, CancellationToken.None);
        await fixture.Service.HandleWebhookAsync(webhook, CancellationToken.None);

        Assert.Equal(1, await fixture.Db.UserLicenses.CountAsync());
        Assert.Equal(1, await fixture.Db.OrganizationMembers.CountAsync());
        Assert.Equal(1, await fixture.Db.OrganizationSeatAssignments.CountAsync());
        var capacity = await fixture.Db.OrganizationPoolOrganizations.SingleAsync();
        Assert.Equal(1, capacity.ActiveSeatCount);
        Assert.Equal(0, capacity.ReservedSeatCount);
    }

    [Fact]
    public async Task ExpiredPayment_ReleasesReservation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var checkout = await fixture.CreatePaymentAsync();
        fixture.Time.Advance(TimeSpan.FromMinutes(20));

        var status = await fixture.Service.GetStatusAsync("user-1", checkout.OrderCode, CancellationToken.None);

        Assert.True(status.IsExpired);
        Assert.Equal(OrganizationSeatAssignmentStatuses.Released,
            (await fixture.Db.OrganizationSeatAssignments.SingleAsync()).Status);
        Assert.Equal(0, (await fixture.Db.OrganizationPoolOrganizations.SingleAsync()).ReservedSeatCount);
    }

    [Fact]
    public async Task LatePaymentWithoutCapacity_RemainsPaidForRetry()
    {
        await using var fixture = await Fixture.CreateAsync();
        var checkout = await fixture.CreatePaymentAsync();
        fixture.Time.Advance(TimeSpan.FromMinutes(20));
        await fixture.Service.GetStatusAsync("user-1", checkout.OrderCode, CancellationToken.None);
        var capacity = await fixture.Db.OrganizationPoolOrganizations.SingleAsync();
        capacity.ActiveSeatCount = capacity.SeatCapacity;
        await fixture.Db.SaveChangesAsync();

        await fixture.Service.HandleWebhookAsync(fixture.Webhook(checkout), CancellationToken.None);

        var payment = await fixture.Db.LicensePayments.SingleAsync();
        Assert.Equal(LicensePaymentStatuses.Paid, payment.Status);
        Assert.Equal("organization_capacity_unavailable", payment.FailureCode);
        Assert.Empty(fixture.Db.UserLicenses);
        var current = await fixture.Service.GetCurrentAsync("user-1", CancellationToken.None);
        Assert.NotNull(current.Payment);
        Assert.True(current.Payment.IsPaid);
        Assert.False(current.Payment.IsFulfilled);
    }

    [Fact]
    public async Task PaidPayment_AfterCapacityIsRestored_RetryFulfillsExactlyOnce()
    {
        await using var fixture = await Fixture.CreateAsync();
        var checkout = await fixture.CreatePaymentAsync();
        fixture.Time.Advance(TimeSpan.FromMinutes(20));
        await fixture.Service.GetStatusAsync("user-1", checkout.OrderCode, CancellationToken.None);
        var capacity = await fixture.Db.OrganizationPoolOrganizations.SingleAsync();
        capacity.ActiveSeatCount = capacity.SeatCapacity;
        await fixture.Db.SaveChangesAsync();
        await fixture.Service.HandleWebhookAsync(fixture.Webhook(checkout), CancellationToken.None);
        var payment = await fixture.Db.LicensePayments.SingleAsync();
        Assert.Equal(LicensePaymentStatuses.Paid, payment.Status);
        Assert.Equal("organization_capacity_unavailable", payment.FailureCode);

        capacity = await fixture.Db.OrganizationPoolOrganizations.SingleAsync();
        capacity.ActiveSeatCount = 0;
        await fixture.Db.SaveChangesAsync();
        Assert.True(await fixture.Service.RetryProvisioningAsync(payment.LicensePaymentId, CancellationToken.None));
        Assert.True(await fixture.Service.RetryProvisioningAsync(payment.LicensePaymentId, CancellationToken.None));

        payment = await fixture.Db.LicensePayments.SingleAsync();
        var assignment = await fixture.Db.OrganizationSeatAssignments.SingleAsync();
        var member = await fixture.Db.OrganizationMembers.SingleAsync();
        capacity = await fixture.Db.OrganizationPoolOrganizations.SingleAsync();
        Assert.Equal(LicensePaymentStatuses.Fulfilled, payment.Status);
        Assert.Null(payment.FailureCode);
        Assert.Equal(OrganizationSeatAssignmentStatuses.Active, assignment.Status);
        Assert.True(assignment.ConsumesSeat);
        Assert.True(assignment.MembershipManaged);
        Assert.Equal(OrganizationMemberStatuses.Active, member.Status);
        Assert.True(member.IsProvisioningManaged);
        Assert.Single(await fixture.Db.UserLicenses.ToListAsync());
        Assert.Equal(1, capacity.ActiveSeatCount);
        Assert.Equal(0, capacity.ReservedSeatCount);
    }

    [Fact]
    public async Task PaidPayment_AfterPlanMappingIsRestored_RetryClearsProvisioningFailure()
    {
        await using var fixture = await Fixture.CreateAsync();
        var checkout = await fixture.CreatePaymentAsync();
        fixture.Time.Advance(TimeSpan.FromMinutes(20));
        await fixture.Service.GetStatusAsync("user-1", checkout.OrderCode, CancellationToken.None);
        var mapping = await fixture.Db.LicensePlanOrganizationPools.SingleAsync();
        fixture.Db.LicensePlanOrganizationPools.Remove(mapping);
        await fixture.Db.SaveChangesAsync();

        await fixture.Service.HandleWebhookAsync(fixture.Webhook(checkout), CancellationToken.None);

        var payment = await fixture.Db.LicensePayments.SingleAsync();
        Assert.Equal(LicensePaymentStatuses.Paid, payment.Status);
        Assert.Equal("license_plan_pool_not_configured", payment.FailureCode);
        Assert.Empty(await fixture.Db.UserLicenses.ToListAsync());

        mapping.UpdatedAtUtc = fixture.Now;
        fixture.Db.LicensePlanOrganizationPools.Add(mapping);
        await fixture.Db.SaveChangesAsync();
        Assert.True(await fixture.Service.RetryProvisioningAsync(payment.LicensePaymentId, CancellationToken.None));

        payment = await fixture.Db.LicensePayments.SingleAsync();
        Assert.Equal(LicensePaymentStatuses.Fulfilled, payment.Status);
        Assert.Null(payment.FailureCode);
        Assert.Equal(
            OrganizationSeatAssignmentStatuses.Active,
            (await fixture.Db.OrganizationSeatAssignments.SingleAsync()).Status);
        Assert.Single(await fixture.Db.UserLicenses.ToListAsync());
        Assert.Single(await fixture.Db.OrganizationMembers.ToListAsync());
    }

    [Fact]
    public async Task Renewal_ReusesOrganizationWithoutConsumingSecondSeat()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.CreatePaymentAsync();
        await fixture.Service.HandleWebhookAsync(fixture.Webhook(first, 1001), CancellationToken.None);
        var originalExpiry = (await fixture.Db.UserLicenses.SingleAsync()).ExpiresAtUtc;

        var second = await fixture.CreatePaymentAsync("renewal-request-12345678");
        await fixture.Service.HandleWebhookAsync(fixture.Webhook(second, 1002), CancellationToken.None);

        var assignments = await fixture.Db.OrganizationSeatAssignments.OrderBy(x => x.CreatedAtUtc).ToListAsync();
        var capacity = await fixture.Db.OrganizationPoolOrganizations.SingleAsync();
        Assert.Equal(2, assignments.Count);
        Assert.True(assignments[0].ConsumesSeat);
        Assert.False(assignments[1].ConsumesSeat);
        Assert.Equal(1, capacity.ActiveSeatCount);
        Assert.Equal(originalExpiry!.Value.AddDays(30), (await fixture.Db.UserLicenses.SingleAsync()).ExpiresAtUtc);
        Assert.Equal(assignments[0].EndsAtUtc, assignments[1].EndsAtUtc);
    }

    [Fact]
    public async Task Renewal_AfterLicenseEnded_ReactivatesFormerManagedOrganization()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.CreatePaymentAsync();
        await fixture.Service.HandleWebhookAsync(fixture.Webhook(first, 2001), CancellationToken.None);
        fixture.Time.Advance(TimeSpan.FromDays(31));
        await fixture.Provisioning.ReconcileAsync(fixture.Now, CancellationToken.None);

        var suspendedMember = await fixture.Db.OrganizationMembers.SingleAsync();
        Assert.True(suspendedMember.IsProvisioningManaged);
        Assert.Equal(OrganizationMemberStatuses.Suspended, suspendedMember.Status);

        var renewal = await fixture.CreatePaymentAsync("renewal-after-expiry-12345678");
        Assert.Equal(fixture.OrganizationId, renewal.AssignedOrganizationId);
        await fixture.Service.HandleWebhookAsync(fixture.Webhook(renewal, 2002), CancellationToken.None);

        var assignments = await fixture.Db.OrganizationSeatAssignments.OrderBy(x => x.CreatedAtUtc).ToListAsync();
        var member = await fixture.Db.OrganizationMembers.SingleAsync();
        var capacity = await fixture.Db.OrganizationPoolOrganizations.SingleAsync();
        Assert.Equal(2, assignments.Count);
        Assert.All(assignments, x => Assert.True(x.ConsumesSeat));
        Assert.Equal(OrganizationSeatAssignmentStatuses.Released, assignments[0].Status);
        Assert.Equal(OrganizationSeatAssignmentStatuses.Active, assignments[1].Status);
        Assert.Equal(OrganizationMemberStatuses.Active, member.Status);
        Assert.True(member.IsProvisioningManaged);
        Assert.Equal(1, capacity.ActiveSeatCount);
        Assert.Equal(0, capacity.ReservedSeatCount);
    }

    [Fact]
    public async Task LateOlderPayment_DoesNotBorrowReservationOwnedByNewerPayment()
    {
        await using var fixture = await Fixture.CreateAsync();
        var configuration = await fixture.Db.OrganizationPoolOrganizations.SingleAsync();
        configuration.SeatCapacity = 1;
        await fixture.Db.SaveChangesAsync();

        var older = await fixture.CreatePaymentAsync("older-payment-12345678");
        fixture.Time.Advance(TimeSpan.FromMinutes(20));
        await fixture.Service.GetStatusAsync("user-1", older.OrderCode, CancellationToken.None);
        var newer = await fixture.CreatePaymentAsync("newer-payment-12345678");

        await fixture.Service.HandleWebhookAsync(fixture.Webhook(older, 3001), CancellationToken.None);
        var olderPayment = await fixture.Db.LicensePayments.SingleAsync(x => x.OrderCode == older.OrderCode);
        Assert.Equal(LicensePaymentStatuses.Paid, olderPayment.Status);
        Assert.Equal("organization_capacity_unavailable", olderPayment.FailureCode);

        fixture.Time.Advance(TimeSpan.FromMinutes(20));
        await fixture.Service.GetStatusAsync("user-1", newer.OrderCode, CancellationToken.None);
        Assert.True(await fixture.Service.RetryProvisioningAsync(olderPayment.LicensePaymentId, CancellationToken.None));
        await fixture.Service.HandleWebhookAsync(fixture.Webhook(newer, 3002), CancellationToken.None);

        var assignments = await fixture.Db.OrganizationSeatAssignments.OrderBy(x => x.CreatedAtUtc).ToListAsync();
        configuration = await fixture.Db.OrganizationPoolOrganizations.SingleAsync();
        Assert.Equal(2, assignments.Count);
        Assert.Single(assignments, x => x.ConsumesSeat);
        Assert.All(assignments, x => Assert.Equal(OrganizationSeatAssignmentStatuses.Active, x.Status));
        Assert.Equal(1, await fixture.Db.UserLicenses.CountAsync());
        Assert.Equal(1, configuration.ActiveSeatCount);
        Assert.Equal(0, configuration.ReservedSeatCount);
    }

    [Fact]
    public async Task AdminTakeover_IsNotOverwrittenByRenewalProvisioning()
    {
        await using var fixture = await Fixture.CreateAsync(addSecondOrganization: true);
        var first = await fixture.CreatePaymentAsync();
        await fixture.Service.HandleWebhookAsync(fixture.Webhook(first, 4001), CancellationToken.None);
        var member = await fixture.Db.OrganizationMembers.SingleAsync();
        member.Role = OrganizationMemberRoles.OrganizationAdmin;
        member.Status = OrganizationMemberStatuses.Suspended;
        member.MonthlyBudgetLimit = 999m;
        member.IsProvisioningManaged = false;
        await fixture.Db.SaveChangesAsync();

        var renewal = await fixture.CreatePaymentAsync("admin-takeover-renewal-12345678");
        Assert.Equal(fixture.SecondOrganizationId, renewal.AssignedOrganizationId);
        await fixture.Service.HandleWebhookAsync(fixture.Webhook(renewal, 4002), CancellationToken.None);

        var protectedMember = await fixture.Db.OrganizationMembers.SingleAsync(
            x => x.OrganizationId == fixture.OrganizationId);
        Assert.Equal(OrganizationMemberRoles.OrganizationAdmin, protectedMember.Role);
        Assert.Equal(OrganizationMemberStatuses.Suspended, protectedMember.Status);
        Assert.Equal(999m, protectedMember.MonthlyBudgetLimit);
        Assert.False(protectedMember.IsProvisioningManaged);
        Assert.Contains(await fixture.Db.OrganizationMembers.ToListAsync(), x =>
            x.OrganizationId == fixture.SecondOrganizationId &&
            x.IsProvisioningManaged &&
            x.Status == OrganizationMemberStatuses.Active);
    }

    [Fact]
    public async Task ChangingPlanToAnotherPool_SchedulesThenMovesAutomaticMembership()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.CreatePaymentAsync();
        await fixture.Service.HandleWebhookAsync(fixture.Webhook(first, 5001), CancellationToken.None);

        var secondPlanId = Guid.NewGuid();
        var secondPoolId = Guid.NewGuid();
        fixture.Db.LicensePlans.Add(new LicensePlan
        {
            LicensePlanId = secondPlanId,
            PlanCode = "premium",
            Name = "GÃ³i premium",
            MaxActivatedDevices = 1,
            OfflineGraceHours = 0,
            DefaultDurationDays = 30,
            FeatureFlagsJson = "{}",
            SalePriceVnd = 250_000,
            IsPublic = true,
            IsActive = true,
            CreatedAtUtc = fixture.Now,
            UpdatedAtUtc = fixture.Now
        });
        fixture.Db.OrganizationPools.Add(new OrganizationPool
        {
            OrganizationPoolId = secondPoolId,
            Code = "premium-pool",
            Name = "Premium pool",
            Status = OrganizationPoolStatuses.Active,
            CreatedAtUtc = fixture.Now,
            UpdatedAtUtc = fixture.Now
        });
        fixture.Db.LicensePlanOrganizationPools.Add(new LicensePlanOrganizationPool
        {
            LicensePlanId = secondPlanId,
            OrganizationPoolId = secondPoolId,
            DefaultMemberMonthlyBudgetLimit = 50m,
            IsActive = true,
            CreatedAtUtc = fixture.Now,
            UpdatedAtUtc = fixture.Now
        });
        var secondOrganizationId = Fixture.AddOrganization(
            fixture.Db,
            secondPoolId,
            "premium-org",
            "Tá»• chá»©c premium",
            10,
            1,
            fixture.Now);
        await fixture.Db.SaveChangesAsync();

        var second = await fixture.Service.CreateOrReuseAsync(
            "user-1",
            new CreateLicensePaymentRequest(secondPlanId, "change-plan-payment-12345678"),
            CancellationToken.None);
        await fixture.Service.HandleWebhookAsync(fixture.Webhook(second, 5002), CancellationToken.None);

        var scheduled = await fixture.Db.OrganizationSeatAssignments.SingleAsync(x => x.LicensePlanId == secondPlanId);
        Assert.Equal(secondOrganizationId, scheduled.OrganizationId);
        Assert.Equal(OrganizationSeatAssignmentStatuses.Scheduled, scheduled.Status);
        Assert.DoesNotContain(await fixture.Db.OrganizationMembers.ToListAsync(), x => x.OrganizationId == secondOrganizationId);

        fixture.Time.Advance(TimeSpan.FromDays(30));
        await fixture.Provisioning.ReconcileAsync(fixture.Now, CancellationToken.None);

        var memberships = await fixture.Db.OrganizationMembers.ToListAsync();
        Assert.Contains(memberships, x =>
            x.OrganizationId == fixture.OrganizationId &&
            x.Status == OrganizationMemberStatuses.Suspended);
        Assert.Contains(memberships, x =>
            x.OrganizationId == secondOrganizationId &&
            x.Status == OrganizationMemberStatuses.Active &&
            x.IsProvisioningManaged &&
            x.MonthlyBudgetLimit == 50m);
        Assert.Equal(OrganizationSeatAssignmentStatuses.Active, scheduled.Status);
    }

    [Fact]
    public async Task ExistingManualMembership_IsNotManagedOrCountedAsCommercialSeat()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = fixture.OrganizationId,
            UserId = "user-1",
            Role = OrganizationMemberRoles.OrganizationAdmin,
            Status = OrganizationMemberStatuses.Active,
            JoinedAtUtc = fixture.Now,
            UpdatedAtUtc = fixture.Now
        });
        await fixture.Db.SaveChangesAsync();

        var checkout = await fixture.CreatePaymentAsync();
        await fixture.Service.HandleWebhookAsync(fixture.Webhook(checkout), CancellationToken.None);

        var assignment = await fixture.Db.OrganizationSeatAssignments.SingleAsync();
        var member = await fixture.Db.OrganizationMembers.SingleAsync();
        Assert.False(assignment.ConsumesSeat);
        Assert.False(assignment.MembershipManaged);
        Assert.Equal(OrganizationMemberRoles.OrganizationAdmin, member.Role);
        Assert.Equal(0, (await fixture.Db.OrganizationPoolOrganizations.SingleAsync()).ActiveSeatCount);
    }

    [Fact]
    public async Task ExistingViewerMembership_IsPreservedAndAnotherOrganizationIsSelected()
    {
        await using var fixture = await Fixture.CreateAsync(addSecondOrganization: true);
        fixture.Db.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = fixture.OrganizationId,
            UserId = "user-1",
            Role = OrganizationMemberRoles.Viewer,
            Status = OrganizationMemberStatuses.Active,
            JoinedAtUtc = fixture.Now,
            UpdatedAtUtc = fixture.Now
        });
        await fixture.Db.SaveChangesAsync();

        var checkout = await fixture.CreatePaymentAsync();
        await fixture.Service.HandleWebhookAsync(fixture.Webhook(checkout), CancellationToken.None);

        Assert.Equal(fixture.SecondOrganizationId, checkout.AssignedOrganizationId);
        var memberships = await fixture.Db.OrganizationMembers.OrderBy(x => x.OrganizationId).ToListAsync();
        Assert.Contains(memberships, x => x.OrganizationId == fixture.OrganizationId && x.Role == OrganizationMemberRoles.Viewer);
        Assert.Contains(memberships, x => x.OrganizationId == fixture.SecondOrganizationId && x.Role == OrganizationMemberRoles.Member);
    }

    [Fact]
    public async Task Reconciliation_SuspendsManagedMembershipAndReleasesRevokedLicenseSeat()
    {
        await using var fixture = await Fixture.CreateAsync();
        var checkout = await fixture.CreatePaymentAsync();
        await fixture.Service.HandleWebhookAsync(fixture.Webhook(checkout), CancellationToken.None);
        var license = await fixture.Db.UserLicenses.SingleAsync();
        license.Status = "Revoked";
        await fixture.Db.SaveChangesAsync();

        await fixture.Provisioning.ReconcileAsync(fixture.Now, CancellationToken.None);

        Assert.Equal(OrganizationSeatAssignmentStatuses.Released,
            (await fixture.Db.OrganizationSeatAssignments.SingleAsync()).Status);
        Assert.Equal(OrganizationMemberStatuses.Suspended,
            (await fixture.Db.OrganizationMembers.SingleAsync()).Status);
        Assert.Equal(0, (await fixture.Db.OrganizationPoolOrganizations.SingleAsync()).ActiveSeatCount);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            AccountDbContext db,
            LicensePaymentService service,
            OrganizationSeatProvisioningService provisioning,
            MutableTimeProvider time,
            SepayPaymentOptions options,
            Guid planId,
            Guid organizationId,
            Guid? secondOrganizationId)
        {
            Db = db;
            Service = service;
            Provisioning = provisioning;
            Time = time;
            Options = options;
            PlanId = planId;
            OrganizationId = organizationId;
            SecondOrganizationId = secondOrganizationId;
        }

        public AccountDbContext Db { get; }
        public LicensePaymentService Service { get; }
        public OrganizationSeatProvisioningService Provisioning { get; }
        public MutableTimeProvider Time { get; }
        public SepayPaymentOptions Options { get; }
        public Guid PlanId { get; }
        public Guid OrganizationId { get; }
        public Guid? SecondOrganizationId { get; }
        public DateTime Now => Time.GetUtcNow().UtcDateTime;

        public static async Task<Fixture> CreateAsync(
            bool addSecondOrganization = false,
            decimal? memberBudget = null)
        {
            var db = new AccountDbContext(new DbContextOptionsBuilder<AccountDbContext>()
                .UseInMemoryDatabase($"organization-seat-{Guid.NewGuid():N}")
                .Options);
            var time = new MutableTimeProvider(new DateTime(2026, 9, 4, 4, 0, 0, DateTimeKind.Utc));
            var options = new SepayPaymentOptions
            {
                Enabled = true,
                QrBaseUrl = "https://vietqr.app/img",
                ReceiverBankCode = "TESTBANK",
                ReceiverAccountNumber = "123456789",
                ReceiverAccountName = "VIDEO MAKER TEST",
                TransferCodePrefix = "VM",
                PaymentExpireMinutes = 15
            };
            db.Users.Add(new ApplicationUser
            {
                Id = "user-1",
                UserName = "user-1@example.test",
                Email = "user-1@example.test",
                AccountStatus = AccountStatuses.Active,
                CreatedAtUtc = time.GetUtcNow().UtcDateTime,
                UpdatedAtUtc = time.GetUtcNow().UtcDateTime
            });
            db.Users.Add(new ApplicationUser
            {
                Id = "user-2",
                UserName = "user-2@example.test",
                Email = "user-2@example.test",
                AccountStatus = AccountStatuses.Active,
                CreatedAtUtc = time.GetUtcNow().UtcDateTime,
                UpdatedAtUtc = time.GetUtcNow().UtcDateTime
            });
            var planId = Guid.NewGuid();
            db.LicensePlans.Add(new LicensePlan
            {
                LicensePlanId = planId,
                PlanCode = "monthly",
                Name = "Gói tháng",
                MaxActivatedDevices = 1,
                OfflineGraceHours = 0,
                DefaultDurationDays = 30,
                FeatureFlagsJson = "{\"maxConcurrentSessions\":1}",
                SalePriceVnd = 132_000,
                IsPublic = true,
                IsActive = true,
                CreatedAtUtc = time.GetUtcNow().UtcDateTime,
                UpdatedAtUtc = time.GetUtcNow().UtcDateTime
            });
            var poolId = Guid.NewGuid();
            db.OrganizationPools.Add(new OrganizationPool
            {
                OrganizationPoolId = poolId,
                Code = "monthly-pool",
                Name = "Pool tháng",
                Status = OrganizationPoolStatuses.Active,
                CreatedAtUtc = time.GetUtcNow().UtcDateTime,
                UpdatedAtUtc = time.GetUtcNow().UtcDateTime
            });
            db.LicensePlanOrganizationPools.Add(new LicensePlanOrganizationPool
            {
                LicensePlanId = planId,
                OrganizationPoolId = poolId,
                DefaultMemberMonthlyBudgetLimit = memberBudget,
                IsActive = true,
                CreatedAtUtc = time.GetUtcNow().UtcDateTime,
                UpdatedAtUtc = time.GetUtcNow().UtcDateTime
            });
            var organizationId = AddOrganization(db, poolId, "org-1", "Tổ chức 1", 10, 1, time.GetUtcNow().UtcDateTime);
            var secondId = addSecondOrganization
                ? AddOrganization(db, poolId, "org-2", "Tổ chức 2", 10, 2, time.GetUtcNow().UtcDateTime)
                : (Guid?)null;
            await db.SaveChangesAsync();
            var provisioning = new OrganizationSeatProvisioningService(
                db,
                NullLogger<OrganizationSeatProvisioningService>.Instance);
            var service = new LicensePaymentService(
                db,
                Microsoft.Extensions.Options.Options.Create(options),
                time,
                new NoOpTelemetry(),
                NullLogger<LicensePaymentService>.Instance,
                provisioning);
            return new Fixture(db, service, provisioning, time, options, planId, organizationId, secondId);
        }

        public Task<LicensePaymentCheckoutResponse> CreatePaymentAsync(
            string idempotencyKey = "seat-request-12345678",
            string userId = "user-1") =>
            Service.CreateOrReuseAsync(
                userId,
                new CreateLicensePaymentRequest(PlanId, idempotencyKey),
                CancellationToken.None);

        public SepayWebhookPayload Webhook(LicensePaymentCheckoutResponse checkout, long id = 987654321) => new(
            id,
            "TESTBANK",
            "2026-09-04 11:01:00",
            Options.ReceiverAccountNumber,
            string.Empty,
            checkout.TransferCode,
            checkout.TransferCode,
            "in",
            "test transfer",
            checkout.AmountVnd,
            checkout.AmountVnd,
            $"TEST-{id}");

        public ValueTask DisposeAsync() => Db.DisposeAsync();

        public static Guid AddOrganization(
            AccountDbContext db,
            Guid poolId,
            string code,
            string name,
            int capacity,
            int priority,
            DateTime now)
        {
            var id = Guid.NewGuid();
            db.Organizations.Add(new Organization
            {
                OrganizationId = id,
                Code = code,
                Name = name,
                Status = OrganizationStatuses.Active,
                MonthlyBudgetLimit = 100,
                CurrencyCode = "USD",
                CreatedByUserId = "admin",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            db.OrganizationPoolOrganizations.Add(new OrganizationPoolOrganization
            {
                OrganizationPoolId = poolId,
                OrganizationId = id,
                SeatCapacity = capacity,
                Priority = priority,
                IsAutoAssignmentEnabled = true,
                IsReady = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            return id;
        }
    }

    private sealed class NoOpTelemetry : ILicensePaymentTelemetry
    {
        public void RecordCreated() { }
        public void RecordFulfilled() { }
        public void RecordExpired() { }
        public void RecordDuplicateWebhook() { }
        public void RecordUnmatchedWebhook(LicensePaymentWebhookMismatchReason reason) { }
    }

    private sealed class MutableTimeProvider(DateTime nowUtc) : TimeProvider
    {
        private DateTimeOffset _now = new(nowUtc);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
