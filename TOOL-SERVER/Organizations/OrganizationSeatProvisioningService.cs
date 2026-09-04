using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Accounts;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Domain.Organizations;

namespace TOOL_SERVER.Organizations;

public sealed record OrganizationSeatAvailability(
    Guid LicensePlanId,
    bool IsConfigured,
    bool IsAvailable,
    string? PoolName,
    int AvailableSeats);

public sealed record OrganizationSeatSnapshot(
    Guid OrganizationSeatAssignmentId,
    Guid OrganizationId,
    string OrganizationName,
    string Status,
    bool ConsumesSeat);

public interface IOrganizationSeatProvisioningService
{
    Task<IReadOnlyDictionary<Guid, OrganizationSeatAvailability>> GetAvailabilityAsync(
        IReadOnlyCollection<Guid> licensePlanIds,
        CancellationToken cancellationToken);

    Task<OrganizationSeatAssignment> ReserveAsync(
        LicensePayment payment,
        DateTime now,
        CancellationToken cancellationToken);

    Task ActivateAsync(
        LicensePayment payment,
        UserLicense license,
        DateTime now,
        CancellationToken cancellationToken);

    Task ReleaseReservationAsync(
        Guid licensePaymentId,
        string reason,
        DateTime now,
        CancellationToken cancellationToken);

    Task<OrganizationSeatSnapshot?> GetSnapshotAsync(
        Guid licensePaymentId,
        CancellationToken cancellationToken);

    Task ReconcileAsync(DateTime now, CancellationToken cancellationToken);
}

public sealed class OrganizationSeatProvisioningService(
    AccountDbContext dbContext,
    ILogger<OrganizationSeatProvisioningService> logger,
    IOrganizationProvisioningReadinessEvaluator? readinessEvaluator = null) : IOrganizationSeatProvisioningService
{
    private static readonly string[] OpenAssignmentStatuses =
    [
        OrganizationSeatAssignmentStatuses.Reserved,
        OrganizationSeatAssignmentStatuses.Scheduled,
        OrganizationSeatAssignmentStatuses.Active
    ];

    public async Task<IReadOnlyDictionary<Guid, OrganizationSeatAvailability>> GetAvailabilityAsync(
        IReadOnlyCollection<Guid> licensePlanIds,
        CancellationToken cancellationToken)
    {
        if (licensePlanIds.Count == 0)
        {
            return new Dictionary<Guid, OrganizationSeatAvailability>();
        }

        var mappings = await (
                from mapping in dbContext.LicensePlanOrganizationPools.AsNoTracking()
                join pool in dbContext.OrganizationPools.AsNoTracking()
                    on mapping.OrganizationPoolId equals pool.OrganizationPoolId
                where licensePlanIds.Contains(mapping.LicensePlanId) &&
                      mapping.IsActive &&
                      pool.Status == OrganizationPoolStatuses.Active
                select new
                {
                    mapping.LicensePlanId,
                    pool.OrganizationPoolId,
                    pool.Name
                })
            .ToListAsync(cancellationToken);

        var poolIds = mappings.Select(x => x.OrganizationPoolId).Distinct().ToArray();
        var capacities = poolIds.Length == 0
            ? []
            : await (
                    from configured in dbContext.OrganizationPoolOrganizations.AsNoTracking()
                    join organization in dbContext.Organizations.AsNoTracking()
                        on configured.OrganizationId equals organization.OrganizationId
                    where poolIds.Contains(configured.OrganizationPoolId) &&
                          configured.IsAutoAssignmentEnabled &&
                          configured.IsReady &&
                          organization.Status == OrganizationStatuses.Active
                    select new
                    {
                        configured.OrganizationPoolId,
                        configured.OrganizationId,
                        Available = configured.SeatCapacity - configured.ActiveSeatCount - configured.ReservedSeatCount
                    })
                .ToListAsync(cancellationToken);

        if (readinessEvaluator is not null)
        {
            var readiness = new Dictionary<Guid, bool>();
            foreach (var organizationId in capacities.Select(x => x.OrganizationId).Distinct())
            {
                readiness[organizationId] = (await readinessEvaluator.EvaluateAsync(
                    organizationId,
                    cancellationToken)).Ready;
            }
            capacities = capacities.Where(x => readiness.GetValueOrDefault(x.OrganizationId)).ToList();
        }

        var availableByPool = capacities
            .GroupBy(x => x.OrganizationPoolId)
            .ToDictionary(x => x.Key, x => Math.Max(0, x.Sum(y => Math.Max(0, y.Available))));
        var result = new Dictionary<Guid, OrganizationSeatAvailability>();
        foreach (var planId in licensePlanIds)
        {
            var mapping = mappings.SingleOrDefault(x => x.LicensePlanId == planId);
            if (mapping is null)
            {
                result[planId] = new(planId, false, false, null, 0);
                continue;
            }

            var available = availableByPool.GetValueOrDefault(mapping.OrganizationPoolId);
            result[planId] = new(planId, true, available > 0, mapping.Name, available);
        }

        return result;
    }

    public async Task<OrganizationSeatAssignment> ReserveAsync(
        LicensePayment payment,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.OrganizationSeatAssignments.SingleOrDefaultAsync(
            x => x.LicensePaymentId == payment.LicensePaymentId,
            cancellationToken);
        if (existing is not null && OpenAssignmentStatuses.Contains(existing.Status))
        {
            return existing;
        }

        var mapping = await (
                from planPool in dbContext.LicensePlanOrganizationPools
                join pool in dbContext.OrganizationPools
                    on planPool.OrganizationPoolId equals pool.OrganizationPoolId
                where planPool.LicensePlanId == payment.LicensePlanId &&
                      planPool.IsActive &&
                      pool.Status == OrganizationPoolStatuses.Active
                select new
                {
                    Mapping = planPool,
                    Pool = pool
                })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw Conflict(
                "license_plan_pool_not_configured",
                "Gói chưa được liên kết với cụm tổ chức đang hoạt động.");

        var configuredOrganizations = await (
                from configured in dbContext.OrganizationPoolOrganizations
                join organization in dbContext.Organizations
                    on configured.OrganizationId equals organization.OrganizationId
                where configured.OrganizationPoolId == mapping.Pool.OrganizationPoolId &&
                      configured.IsAutoAssignmentEnabled &&
                      configured.IsReady &&
                      organization.Status == OrganizationStatuses.Active
                select new Candidate(configured, organization))
            .ToListAsync(cancellationToken);

        if (readinessEvaluator is not null)
        {
            var readyCandidates = new List<Candidate>(configuredOrganizations.Count);
            foreach (var candidate in configuredOrganizations)
            {
                var readiness = await readinessEvaluator.EvaluateAsync(
                    candidate.Configuration.OrganizationId,
                    cancellationToken);
                if (readiness.Ready)
                {
                    readyCandidates.Add(candidate);
                }
                else
                {
                    logger.LogWarning(
                        "Skipped stale-ready organization {OrganizationId} in pool {OrganizationPoolId}: {ReadinessMessage}",
                        candidate.Configuration.OrganizationId,
                        candidate.Configuration.OrganizationPoolId,
                        readiness.Message);
                }
            }
            configuredOrganizations = readyCandidates;
        }

        var configuredIds = configuredOrganizations.Select(x => x.Configuration.OrganizationId).ToArray();
        var existingMemberships = configuredIds.Length == 0
            ? []
            : await dbContext.OrganizationMembers
                .AsNoTracking()
                .Where(x => configuredIds.Contains(x.OrganizationId) &&
                            x.UserId == payment.UserId)
                .OrderBy(x => x.OrganizationId)
                .ToListAsync(cancellationToken);

        // A paid, active entitlement can be extended without occupying another seat. A
        // reservation owned by another payment must never be reused: that reservation
        // can expire independently and would otherwise free the seat under this payment.
        var reusableAssignment = await dbContext.OrganizationSeatAssignments
            .AsNoTracking()
            .Where(x => x.UserId == payment.UserId &&
                        x.OrganizationPoolId == mapping.Pool.OrganizationPoolId &&
                        x.LicensePlanId == payment.LicensePlanId &&
                        x.ConsumesSeat &&
                        (x.Status == OrganizationSeatAssignmentStatuses.Active ||
                         x.Status == OrganizationSeatAssignmentStatuses.Scheduled))
            .OrderByDescending(x => x.Status == OrganizationSeatAssignmentStatuses.Active)
            .ThenByDescending(x => x.EndsAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var reusableCandidate = reusableAssignment is null
            ? null
            : configuredOrganizations.SingleOrDefault(x => x.Configuration.OrganizationId == reusableAssignment.OrganizationId);

        Candidate? selected = null;
        var consumesSeat = false;
        var membershipManaged = false;

        if (reusableCandidate is not null)
        {
            var member = existingMemberships.SingleOrDefault(
                x => x.OrganizationId == reusableCandidate.Configuration.OrganizationId);
            if (member is null || member.IsProvisioningManaged)
            {
                selected = reusableCandidate;
                membershipManaged = reusableAssignment!.MembershipManaged;
            }
            else if (member.Status == OrganizationMemberStatuses.Active &&
                     member.Role != OrganizationMemberRoles.Viewer)
            {
                selected = reusableCandidate;
            }
        }

        if (selected is null)
        {
            // An expired automatically-managed membership remains as a suspended marker.
            // Prefer its former organization for a same-plan renewal when capacity permits.
            var previousManagedAssignment = await dbContext.OrganizationSeatAssignments
                .AsNoTracking()
                .Where(x => x.UserId == payment.UserId &&
                            x.OrganizationPoolId == mapping.Pool.OrganizationPoolId &&
                            x.LicensePlanId == payment.LicensePlanId &&
                            x.MembershipManaged)
                .OrderByDescending(x => x.Status == OrganizationSeatAssignmentStatuses.Active)
                .ThenByDescending(x => x.EndsAtUtc)
                .ThenByDescending(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            var previousManagedMember = previousManagedAssignment is null
                ? null
                : existingMemberships.SingleOrDefault(x =>
                    x.OrganizationId == previousManagedAssignment.OrganizationId &&
                    x.IsProvisioningManaged &&
                    x.Role == OrganizationMemberRoles.Member);
            var previousCandidate = previousManagedMember is null
                ? null
                : configuredOrganizations.SingleOrDefault(x =>
                    x.Configuration.OrganizationId == previousManagedMember.OrganizationId &&
                    x.Configuration.ActiveSeatCount + x.Configuration.ReservedSeatCount < x.Configuration.SeatCapacity);
            if (previousCandidate is not null)
            {
                selected = previousCandidate;
                selected.Configuration.ReservedSeatCount++;
                selected.Configuration.UpdatedAtUtc = now;
                consumesSeat = true;
                membershipManaged = true;
            }
        }

        if (selected is null)
        {
            var manualMembership = existingMemberships.FirstOrDefault(x =>
                !x.IsProvisioningManaged &&
                x.Status == OrganizationMemberStatuses.Active &&
                x.Role != OrganizationMemberRoles.Viewer);
            if (manualMembership is not null)
            {
                selected = configuredOrganizations.Single(
                    x => x.Configuration.OrganizationId == manualMembership.OrganizationId);
            }
        }

        if (selected is null)
        {
            var protectedOrganizationIds = existingMemberships.Select(x => x.OrganizationId).ToHashSet();
            selected = configuredOrganizations
                .Where(x => !protectedOrganizationIds.Contains(x.Configuration.OrganizationId))
                .Where(x => x.Configuration.ActiveSeatCount + x.Configuration.ReservedSeatCount < x.Configuration.SeatCapacity)
                .OrderBy(x => x.Configuration.Priority)
                .ThenBy(x => (decimal)(x.Configuration.ActiveSeatCount + x.Configuration.ReservedSeatCount) /
                             x.Configuration.SeatCapacity)
                .ThenBy(x => x.Configuration.OrganizationId)
                .FirstOrDefault();
            if (selected is null)
            {
                throw Conflict(
                    "organization_capacity_unavailable",
                    "Hiện không còn tổ chức sẵn sàng cho gói này. Vui lòng thử lại sau.");
            }

            selected.Configuration.ReservedSeatCount++;
            selected.Configuration.UpdatedAtUtc = now;
            consumesSeat = true;
            membershipManaged = true;
        }

        var assignment = existing ?? new OrganizationSeatAssignment
        {
            OrganizationSeatAssignmentId = Guid.NewGuid(),
            LicensePaymentId = payment.LicensePaymentId,
            UserId = payment.UserId,
            LicensePlanId = payment.LicensePlanId,
            CreatedAtUtc = now
        };
        assignment.OrganizationPoolId = mapping.Pool.OrganizationPoolId;
        assignment.OrganizationId = selected.Configuration.OrganizationId;
        assignment.Status = OrganizationSeatAssignmentStatuses.Reserved;
        assignment.ConsumesSeat = consumesSeat;
        assignment.MembershipManaged = membershipManaged;
        assignment.ReservedAtUtc = now;
        assignment.ReservationExpiresAtUtc = payment.ExpiresAtUtc > now
            ? payment.ExpiresAtUtc
            : now.AddMinutes(15);
        assignment.StartsAtUtc = null;
        assignment.EndsAtUtc = null;
        assignment.ActivatedAtUtc = null;
        assignment.ReleasedAtUtc = null;
        assignment.ReleaseReason = null;
        assignment.FailureCode = null;
        assignment.UpdatedAtUtc = now;
        if (existing is null)
        {
            dbContext.OrganizationSeatAssignments.Add(assignment);
        }

        logger.LogInformation(
            "Reserved organization {OrganizationId} in pool {OrganizationPoolId} for payment {LicensePaymentId}; consumes seat: {ConsumesSeat}.",
            assignment.OrganizationId,
            assignment.OrganizationPoolId,
            assignment.LicensePaymentId,
            assignment.ConsumesSeat);
        return assignment;
    }

    public async Task ActivateAsync(
        LicensePayment payment,
        UserLicense license,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var assignment = await dbContext.OrganizationSeatAssignments.SingleOrDefaultAsync(
            x => x.LicensePaymentId == payment.LicensePaymentId,
            cancellationToken)
            ?? await ReserveAsync(payment, now, cancellationToken);

        var mapping = await dbContext.LicensePlanOrganizationPools
            .AsNoTracking()
            .SingleAsync(x => x.LicensePlanId == payment.LicensePlanId, cancellationToken);
        var configuration = await dbContext.OrganizationPoolOrganizations.SingleAsync(
            x => x.OrganizationPoolId == assignment.OrganizationPoolId &&
                 x.OrganizationId == assignment.OrganizationId,
            cancellationToken);

        // The payment may have reserved its seat before an older same-plan payment was
        // fulfilled. Collapse it into that active entitlement at fulfillment time and
        // release the redundant reservation atomically.
        OrganizationSeatAssignment? primaryAssignment = null;
        if (assignment.Status == OrganizationSeatAssignmentStatuses.Reserved)
        {
            primaryAssignment = await dbContext.OrganizationSeatAssignments.SingleOrDefaultAsync(
                x => x.OrganizationSeatAssignmentId != assignment.OrganizationSeatAssignmentId &&
                     x.OrganizationPoolId == assignment.OrganizationPoolId &&
                     x.UserId == assignment.UserId &&
                     x.LicensePlanId == assignment.LicensePlanId &&
                     x.ConsumesSeat &&
                     (x.Status == OrganizationSeatAssignmentStatuses.Active ||
                      x.Status == OrganizationSeatAssignmentStatuses.Scheduled),
                cancellationToken);
            if (primaryAssignment is not null)
            {
                var primaryMember = await dbContext.OrganizationMembers.SingleOrDefaultAsync(
                    x => x.OrganizationId == primaryAssignment.OrganizationId &&
                         x.UserId == primaryAssignment.UserId,
                    cancellationToken);
                var canReusePrimary = primaryMember is null ||
                                      primaryMember.IsProvisioningManaged ||
                                      (primaryMember.Status == OrganizationMemberStatuses.Active &&
                                       primaryMember.Role != OrganizationMemberRoles.Viewer);
                if (!canReusePrimary)
                {
                    primaryAssignment = null;
                }
            }
            if (primaryAssignment is not null && assignment.ConsumesSeat)
            {
                configuration.ReservedSeatCount = Math.Max(0, configuration.ReservedSeatCount - 1);
                configuration.UpdatedAtUtc = now;
                assignment.OrganizationId = primaryAssignment.OrganizationId;
                assignment.ConsumesSeat = false;
                assignment.MembershipManaged = primaryAssignment.MembershipManaged;
                configuration = await FindConfigurationAsync(primaryAssignment, cancellationToken);
            }
        }

        var startsAt = NormalizeUtc(license.StartsAtUtc);
        var endsAt = license.ExpiresAtUtc.HasValue ? NormalizeUtc(license.ExpiresAtUtc.Value) : (DateTime?)null;
        if (!assignment.ConsumesSeat)
        {
            primaryAssignment ??= await dbContext.OrganizationSeatAssignments.SingleOrDefaultAsync(
                x => x.OrganizationSeatAssignmentId != assignment.OrganizationSeatAssignmentId &&
                     x.OrganizationId == assignment.OrganizationId &&
                     x.UserId == assignment.UserId &&
                     x.LicensePlanId == assignment.LicensePlanId &&
                     x.ConsumesSeat &&
                     (x.Status == OrganizationSeatAssignmentStatuses.Active ||
                      x.Status == OrganizationSeatAssignmentStatuses.Scheduled),
                cancellationToken);
            if (primaryAssignment is not null)
            {
                primaryAssignment.UserLicenseId = license.UserLicenseId;
                primaryAssignment.EndsAtUtc = endsAt;
                primaryAssignment.UpdatedAtUtc = now;
            }
        }
        assignment.UserLicenseId = license.UserLicenseId;
        assignment.StartsAtUtc = startsAt;
        assignment.EndsAtUtc = endsAt;
        assignment.ReservationExpiresAtUtc = startsAt > now ? startsAt : payment.ExpiresAtUtc;
        assignment.FailureCode = null;
        assignment.UpdatedAtUtc = now;

        if (startsAt <= now)
        {
            if (assignment.ConsumesSeat && assignment.Status == OrganizationSeatAssignmentStatuses.Reserved)
            {
                configuration.ReservedSeatCount = Math.Max(0, configuration.ReservedSeatCount - 1);
                configuration.ActiveSeatCount++;
                configuration.UpdatedAtUtc = now;
            }
            assignment.Status = OrganizationSeatAssignmentStatuses.Active;
            assignment.ActivatedAtUtc ??= now;
            await ActivateMembershipAsync(assignment, mapping.DefaultMemberMonthlyBudgetLimit, now, cancellationToken);
        }
        else
        {
            assignment.Status = OrganizationSeatAssignmentStatuses.Scheduled;
        }
    }

    public async Task ReleaseReservationAsync(
        Guid licensePaymentId,
        string reason,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var assignment = await dbContext.OrganizationSeatAssignments.SingleOrDefaultAsync(
            x => x.LicensePaymentId == licensePaymentId,
            cancellationToken);
        if (assignment is null || !OpenAssignmentStatuses.Contains(assignment.Status))
        {
            return;
        }

        await ReleaseAsync(assignment, reason, now, cancellationToken);
    }

    public async Task<OrganizationSeatSnapshot?> GetSnapshotAsync(
        Guid licensePaymentId,
        CancellationToken cancellationToken)
    {
        return await (
                from assignment in dbContext.OrganizationSeatAssignments.AsNoTracking()
                join organization in dbContext.Organizations.AsNoTracking()
                    on assignment.OrganizationId equals organization.OrganizationId
                where assignment.LicensePaymentId == licensePaymentId
                select new OrganizationSeatSnapshot(
                    assignment.OrganizationSeatAssignmentId,
                    assignment.OrganizationId,
                    organization.Name,
                    assignment.Status,
                    assignment.ConsumesSeat))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task ReconcileAsync(DateTime now, CancellationToken cancellationToken)
    {
        var expiredReservations = await (
                from assignment in dbContext.OrganizationSeatAssignments
                join payment in dbContext.LicensePayments
                    on assignment.LicensePaymentId equals payment.LicensePaymentId
                where assignment.Status == OrganizationSeatAssignmentStatuses.Reserved &&
                      assignment.ReservationExpiresAtUtc <= now &&
                      (payment.Status == LicensePaymentStatuses.Pending || payment.Status == LicensePaymentStatuses.Expired)
                select new { Assignment = assignment, Payment = payment })
            .ToListAsync(cancellationToken);
        foreach (var item in expiredReservations)
        {
            item.Payment.Status = LicensePaymentStatuses.Expired;
            await ReleaseAsync(item.Assignment, "payment_expired", now, cancellationToken);
        }

        var scheduled = await dbContext.OrganizationSeatAssignments
            .Where(x => x.Status == OrganizationSeatAssignmentStatuses.Scheduled && x.StartsAtUtc <= now)
            .ToListAsync(cancellationToken);
        foreach (var assignment in scheduled)
        {
            var configuration = await FindConfigurationAsync(assignment, cancellationToken);
            if (assignment.ConsumesSeat)
            {
                configuration.ReservedSeatCount = Math.Max(0, configuration.ReservedSeatCount - 1);
                configuration.ActiveSeatCount++;
                configuration.UpdatedAtUtc = now;
            }
            assignment.Status = OrganizationSeatAssignmentStatuses.Active;
            assignment.ActivatedAtUtc ??= now;
            assignment.UpdatedAtUtc = now;
            var mapping = await dbContext.LicensePlanOrganizationPools
                .AsNoTracking()
                .SingleAsync(x => x.LicensePlanId == assignment.LicensePlanId, cancellationToken);
            await ActivateMembershipAsync(assignment, mapping.DefaultMemberMonthlyBudgetLimit, now, cancellationToken);
        }

        var ended = await (
                from assignment in dbContext.OrganizationSeatAssignments
                join license in dbContext.UserLicenses
                    on assignment.UserLicenseId equals (Guid?)license.UserLicenseId
                where assignment.Status == OrganizationSeatAssignmentStatuses.Active &&
                      (assignment.EndsAtUtc <= now ||
                       license.Status == "Suspended" ||
                       license.Status == "Revoked" ||
                       license.Status == "Expired")
                select assignment)
            .ToListAsync(cancellationToken);
        foreach (var assignment in ended)
        {
            await ReleaseAsync(
                assignment,
                assignment.EndsAtUtc <= now ? "license_ended" : "license_inactive",
                now,
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await RecalculateCountersAsync(now, cancellationToken);
    }

    private async Task ActivateMembershipAsync(
        OrganizationSeatAssignment assignment,
        decimal? monthlyBudgetLimit,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var member = await dbContext.OrganizationMembers.SingleOrDefaultAsync(
            x => x.OrganizationId == assignment.OrganizationId && x.UserId == assignment.UserId,
            cancellationToken);
        if (member is null)
        {
            dbContext.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = assignment.OrganizationId,
                UserId = assignment.UserId,
                Role = OrganizationMemberRoles.Member,
                Status = OrganizationMemberStatuses.Active,
                IsProvisioningManaged = true,
                MonthlyBudgetLimit = monthlyBudgetLimit,
                JoinedAtUtc = now,
                UpdatedAtUtc = now
            });
            assignment.MembershipManaged = true;
            return;
        }

        if (!member.IsProvisioningManaged)
        {
            assignment.MembershipManaged = false;
            return;
        }

        if (member.Role != OrganizationMemberRoles.Member)
        {
            assignment.MembershipManaged = false;
            return;
        }

        assignment.MembershipManaged = true;
        member.Status = OrganizationMemberStatuses.Active;
        member.MonthlyBudgetLimit = monthlyBudgetLimit;
        member.UpdatedAtUtc = now;
    }

    private async Task ReleaseAsync(
        OrganizationSeatAssignment assignment,
        string reason,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var previousStatus = assignment.Status;
        if (assignment.ConsumesSeat)
        {
            var configuration = await FindConfigurationAsync(assignment, cancellationToken);
            if (previousStatus == OrganizationSeatAssignmentStatuses.Active)
            {
                configuration.ActiveSeatCount = Math.Max(0, configuration.ActiveSeatCount - 1);
            }
            else
            {
                configuration.ReservedSeatCount = Math.Max(0, configuration.ReservedSeatCount - 1);
            }
            configuration.UpdatedAtUtc = now;
        }

        assignment.Status = OrganizationSeatAssignmentStatuses.Released;
        assignment.ReleasedAtUtc = now;
        assignment.ReleaseReason = reason.Length <= 500 ? reason : reason[..500];
        assignment.UpdatedAtUtc = now;

        if (!assignment.MembershipManaged || previousStatus != OrganizationSeatAssignmentStatuses.Active)
        {
            return;
        }

        var relatedAssignments = await dbContext.OrganizationSeatAssignments
            .Where(x => x.OrganizationSeatAssignmentId != assignment.OrganizationSeatAssignmentId &&
                        x.OrganizationId == assignment.OrganizationId &&
                        x.UserId == assignment.UserId &&
                        x.MembershipManaged)
            .ToListAsync(cancellationToken);
        var stillActive = relatedAssignments.Any(x =>
            x.Status == OrganizationSeatAssignmentStatuses.Active &&
            (x.EndsAtUtc == null || x.EndsAtUtc > now));
        if (stillActive)
        {
            return;
        }

        var member = await dbContext.OrganizationMembers.SingleOrDefaultAsync(
            x => x.OrganizationId == assignment.OrganizationId && x.UserId == assignment.UserId,
            cancellationToken);
        if (member is not null &&
            member.IsProvisioningManaged &&
            member.Role == OrganizationMemberRoles.Member)
        {
            member.Status = OrganizationMemberStatuses.Suspended;
            member.UpdatedAtUtc = now;
        }
    }

    private Task<OrganizationPoolOrganization> FindConfigurationAsync(
        OrganizationSeatAssignment assignment,
        CancellationToken cancellationToken) =>
        dbContext.OrganizationPoolOrganizations.SingleAsync(
            x => x.OrganizationPoolId == assignment.OrganizationPoolId &&
                 x.OrganizationId == assignment.OrganizationId,
            cancellationToken);

    private async Task RecalculateCountersAsync(DateTime now, CancellationToken cancellationToken)
    {
        var configurations = await dbContext.OrganizationPoolOrganizations.ToListAsync(cancellationToken);
        var counts = await dbContext.OrganizationSeatAssignments
            .Where(x => x.ConsumesSeat && OpenAssignmentStatuses.Contains(x.Status))
            .GroupBy(x => new { x.OrganizationPoolId, x.OrganizationId, x.Status })
            .Select(x => new { x.Key.OrganizationPoolId, x.Key.OrganizationId, x.Key.Status, Count = x.Count() })
            .ToListAsync(cancellationToken);
        foreach (var configuration in configurations)
        {
            var current = counts.Where(x => x.OrganizationPoolId == configuration.OrganizationPoolId &&
                                            x.OrganizationId == configuration.OrganizationId).ToArray();
            var active = current.Where(x => x.Status == OrganizationSeatAssignmentStatuses.Active).Sum(x => x.Count);
            var reserved = current.Where(x => x.Status is OrganizationSeatAssignmentStatuses.Reserved or
                                                   OrganizationSeatAssignmentStatuses.Scheduled).Sum(x => x.Count);
            if (configuration.ActiveSeatCount != active || configuration.ReservedSeatCount != reserved)
            {
                logger.LogWarning(
                    "Corrected seat counters for organization {OrganizationId}: active {OldActive}->{NewActive}, reserved {OldReserved}->{NewReserved}.",
                    configuration.OrganizationId,
                    configuration.ActiveSeatCount,
                    active,
                    configuration.ReservedSeatCount,
                    reserved);
                configuration.ActiveSeatCount = active;
                configuration.ReservedSeatCount = reserved;
                configuration.UpdatedAtUtc = now;
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static AccountApiException Conflict(string code, string message) =>
        new(StatusCodes.Status409Conflict, code, message);

    private sealed record Candidate(
        OrganizationPoolOrganization Configuration,
        Organization Organization);
}
