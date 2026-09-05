using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Domain.Providers;
using TOOL_SERVER.Payments;
using TOOL_SHARED.Contracts.Organizations;
using TOOL_SHARED.Contracts.Common;

namespace TOOL_SERVER.Organizations;

public interface IOrganizationProvisioningAdminService
{
    Task<IReadOnlyList<OrganizationPoolSummaryResponse>> GetPoolsAsync(CancellationToken cancellationToken);
    Task<PagedResponse<OrganizationPoolSummaryResponse>> GetPoolsPageAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<OrganizationPoolDetailResponse> GetPoolAsync(Guid poolId, CancellationToken cancellationToken);
    Task<OrganizationPoolSummaryResponse> CreatePoolAsync(SaveOrganizationPoolRequest request, string adminUserId, CancellationToken cancellationToken);
    Task<OrganizationPoolSummaryResponse> UpdatePoolAsync(Guid poolId, SaveOrganizationPoolRequest request, string adminUserId, CancellationToken cancellationToken);
    Task<OrganizationPoolOrganizationResponse> UpsertOrganizationAsync(Guid poolId, SaveOrganizationPoolOrganizationRequest request, string adminUserId, CancellationToken cancellationToken);
    Task RemoveOrganizationAsync(Guid poolId, Guid organizationId, string adminUserId, CancellationToken cancellationToken);
    Task<LicensePlanOrganizationPoolResponse> UpsertLicensePlanAsync(Guid licensePlanId, SaveLicensePlanOrganizationPoolRequest request, string adminUserId, CancellationToken cancellationToken);
    Task RemoveLicensePlanAsync(Guid licensePlanId, string adminUserId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrganizationSeatAssignmentResponse>> GetAssignmentsAsync(string? status, int take, CancellationToken cancellationToken);
    Task<RetryOrganizationSeatAssignmentResponse> RetryAssignmentAsync(Guid assignmentId, string adminUserId, CancellationToken cancellationToken);
}

internal sealed partial class OrganizationProvisioningAdminService(
    AccountDbContext accountDb,
    AiGovernanceDbContext governanceDb,
    ProviderAdminDbContext providerDb,
    TimeProvider timeProvider,
    ILicensePaymentService paymentService,
    IOrganizationProvisioningReadinessEvaluator runtimeReadinessEvaluator) : IOrganizationProvisioningAdminService
{
    public async Task<IReadOnlyList<OrganizationPoolSummaryResponse>> GetPoolsAsync(CancellationToken cancellationToken)
    {
        var pools = await accountDb.OrganizationPools.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var organizations = await accountDb.OrganizationPoolOrganizations.AsNoTracking().ToListAsync(cancellationToken);
        var plans = await accountDb.LicensePlanOrganizationPools.AsNoTracking().ToListAsync(cancellationToken);
        var organizationIds = organizations.Select(x => x.OrganizationId).Distinct().ToArray();
        var organizationDirectory = await accountDb.Organizations.AsNoTracking()
            .Where(x => organizationIds.Contains(x.OrganizationId))
            .ToDictionaryAsync(x => x.OrganizationId, cancellationToken);
        var planIds = plans.Select(x => x.LicensePlanId).Distinct().ToArray();
        var planDirectory = await accountDb.LicensePlans.AsNoTracking()
            .Where(x => planIds.Contains(x.LicensePlanId))
            .ToDictionaryAsync(x => x.LicensePlanId, cancellationToken);
        var runtimeReadiness = await EvaluateRuntimeReadinessAsync(
            organizations.Where(x => x.IsAutoAssignmentEnabled && x.IsReady),
            cancellationToken);
        return pools.Select(pool => ToSummary(
            pool,
            organizations,
            plans,
            organizationDirectory,
            planDirectory,
            runtimeReadiness)).ToArray();
    }

    public async Task<PagedResponse<OrganizationPoolSummaryResponse>> GetPoolsPageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1) throw Validation("invalid_page", "Số trang phải lớn hơn hoặc bằng 1.");
        if (pageSize is < 1 or > 100) throw Validation("invalid_page_size", "Số bản ghi mỗi trang phải từ 1 đến 100.");
        var query = accountDb.OrganizationPools.AsNoTracking();
        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
        var effectivePage = totalPages == 0 ? 1 : Math.Min(page, totalPages);
        var pools = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.OrganizationPoolId)
            .Skip((effectivePage - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var organizations = await accountDb.OrganizationPoolOrganizations.AsNoTracking().ToListAsync(cancellationToken);
        var plans = await accountDb.LicensePlanOrganizationPools.AsNoTracking().ToListAsync(cancellationToken);
        var organizationIds = organizations.Select(x => x.OrganizationId).Distinct().ToArray();
        var organizationDirectory = await accountDb.Organizations.AsNoTracking()
            .Where(x => organizationIds.Contains(x.OrganizationId))
            .ToDictionaryAsync(x => x.OrganizationId, cancellationToken);
        var planIds = plans.Select(x => x.LicensePlanId).Distinct().ToArray();
        var planDirectory = await accountDb.LicensePlans.AsNoTracking()
            .Where(x => planIds.Contains(x.LicensePlanId))
            .ToDictionaryAsync(x => x.LicensePlanId, cancellationToken);
        var runtimeReadiness = await EvaluateRuntimeReadinessAsync(
            organizations.Where(x => x.IsAutoAssignmentEnabled && x.IsReady), cancellationToken);
        var items = pools.Select(pool => ToSummary(pool, organizations, plans, organizationDirectory, planDirectory, runtimeReadiness)).ToArray();
        return new PagedResponse<OrganizationPoolSummaryResponse>(items, effectivePage, pageSize, totalCount);
    }

    public async Task<OrganizationPoolDetailResponse> GetPoolAsync(Guid poolId, CancellationToken cancellationToken)
    {
        var pool = await RequirePoolAsync(poolId, cancellationToken);
        var organizationLinks = await accountDb.OrganizationPoolOrganizations
            .AsNoTracking()
            .Where(x => x.OrganizationPoolId == poolId)
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.OrganizationId)
            .ToListAsync(cancellationToken);
        var organizationIds = organizationLinks.Select(x => x.OrganizationId).ToArray();
        var organizations = await accountDb.Organizations.AsNoTracking()
            .Where(x => organizationIds.Contains(x.OrganizationId))
            .ToDictionaryAsync(x => x.OrganizationId, cancellationToken);
        var planLinks = await accountDb.LicensePlanOrganizationPools.AsNoTracking()
            .Where(x => x.OrganizationPoolId == poolId)
            .ToListAsync(cancellationToken);
        var planIds = planLinks.Select(x => x.LicensePlanId).ToArray();
        var plans = await accountDb.LicensePlans.AsNoTracking()
            .Where(x => planIds.Contains(x.LicensePlanId))
            .ToDictionaryAsync(x => x.LicensePlanId, cancellationToken);
        var recentAssignments = await BuildAssignmentQuery(null)
            .Where(x => x.OrganizationPoolId == poolId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);
        var allPlanLinks = await accountDb.LicensePlanOrganizationPools.AsNoTracking().ToListAsync(cancellationToken);

        var runtimeReadiness = await EvaluateRuntimeReadinessAsync(
            organizationLinks.Where(x => x.IsReady),
            cancellationToken);
        foreach (var link in organizationLinks.Where(x => x.IsReady))
        {
            var currentReadiness = runtimeReadiness[link.OrganizationId];
            link.ReadinessMessage = currentReadiness.Message;
            if (!currentReadiness.Ready)
            {
                link.IsReady = false;
            }
        }

        return new OrganizationPoolDetailResponse(
            ToSummary(pool, organizationLinks, allPlanLinks, organizations, plans, runtimeReadiness),
            organizationLinks.Select(link => ToOrganizationResponse(
                link,
                organizations[link.OrganizationId],
                pool.Status == OrganizationPoolStatuses.Active && planLinks.Any(x =>
                    x.IsActive &&
                    plans.TryGetValue(x.LicensePlanId, out var plan) &&
                    IsSellable(plan)))).ToArray(),
            planLinks.Select(link => ToPlanResponse(link, plans[link.LicensePlanId], pool)).ToArray(),
            await MapAssignmentsAsync(recentAssignments, cancellationToken));
    }

    public async Task<OrganizationPoolSummaryResponse> CreatePoolAsync(
        SaveOrganizationPoolRequest request,
        string adminUserId,
        CancellationToken cancellationToken)
    {
        var values = ValidatePool(request);
        if (await accountDb.OrganizationPools.AnyAsync(x => x.Code == values.Code, cancellationToken))
        {
            throw Conflict("organization_pool_code_exists", "Mã pool tổ chức đã tồn tại.");
        }

        var now = UtcNow();
        var pool = new OrganizationPool
        {
            OrganizationPoolId = Guid.NewGuid(),
            Code = values.Code,
            Name = values.Name,
            AllocationStrategy = OrganizationPoolAllocationStrategies.PriorityBalanced,
            Status = values.Status,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        accountDb.OrganizationPools.Add(pool);
        AddAudit(adminUserId, "OrganizationPoolCreated", new { pool.OrganizationPoolId, pool.Code, pool.Name, pool.Status }, now);
        await accountDb.SaveChangesAsync(cancellationToken);
        return ToSummary(
            pool,
            [],
            [],
            new Dictionary<Guid, Organization>(),
            new Dictionary<Guid, LicensePlan>(),
            new Dictionary<Guid, OrganizationProvisioningReadiness>());
    }

    public async Task<OrganizationPoolSummaryResponse> UpdatePoolAsync(
        Guid poolId,
        SaveOrganizationPoolRequest request,
        string adminUserId,
        CancellationToken cancellationToken)
    {
        var values = ValidatePool(request);
        var pool = await RequirePoolAsync(poolId, cancellationToken);
        if (await accountDb.OrganizationPools.AnyAsync(x => x.OrganizationPoolId != poolId && x.Code == values.Code, cancellationToken))
        {
            throw Conflict("organization_pool_code_exists", "Mã pool tổ chức đã tồn tại.");
        }

        pool.Code = values.Code;
        pool.Name = values.Name;
        pool.Status = values.Status;
        pool.UpdatedAtUtc = UtcNow();
        AddAudit(adminUserId, "OrganizationPoolUpdated", new { pool.OrganizationPoolId, pool.Code, pool.Name, pool.Status }, pool.UpdatedAtUtc);
        await accountDb.SaveChangesAsync(cancellationToken);
        var organizationLinks = await accountDb.OrganizationPoolOrganizations.AsNoTracking().ToListAsync(cancellationToken);
        var planLinks = await accountDb.LicensePlanOrganizationPools.AsNoTracking().ToListAsync(cancellationToken);
        var organizationIds = organizationLinks.Select(x => x.OrganizationId).Distinct().ToArray();
        var organizationDirectory = await accountDb.Organizations.AsNoTracking()
            .Where(x => organizationIds.Contains(x.OrganizationId))
            .ToDictionaryAsync(x => x.OrganizationId, cancellationToken);
        var planIds = planLinks.Select(x => x.LicensePlanId).Distinct().ToArray();
        var planDirectory = await accountDb.LicensePlans.AsNoTracking()
            .Where(x => planIds.Contains(x.LicensePlanId))
            .ToDictionaryAsync(x => x.LicensePlanId, cancellationToken);
        var runtimeReadiness = await EvaluateRuntimeReadinessAsync(
            organizationLinks.Where(x => x.IsAutoAssignmentEnabled && x.IsReady),
            cancellationToken);
        return ToSummary(pool, organizationLinks, planLinks, organizationDirectory, planDirectory, runtimeReadiness);
    }

    public async Task<OrganizationPoolOrganizationResponse> UpsertOrganizationAsync(
        Guid poolId,
        SaveOrganizationPoolOrganizationRequest request,
        string adminUserId,
        CancellationToken cancellationToken)
    {
        var pool = await RequirePoolAsync(poolId, cancellationToken);
        if (request.SeatCapacity is < 1 or > 100000)
        {
            throw Validation("invalid_seat_capacity", "Sức chứa khách hàng phải từ 1 đến 100.000.");
        }
        if (request.Priority is < 0 or > 100000)
        {
            throw Validation("invalid_assignment_priority", "Độ ưu tiên phải từ 0 đến 100.000.");
        }

        var organization = await accountDb.Organizations.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId,
            cancellationToken) ?? throw NotFound("organization_not_found", "Không tìm thấy tổ chức.");
        var existing = await accountDb.OrganizationPoolOrganizations.SingleOrDefaultAsync(
            x => x.OrganizationPoolId == poolId && x.OrganizationId == request.OrganizationId,
            cancellationToken);
        if (existing is not null && request.SeatCapacity < existing.ActiveSeatCount + existing.ReservedSeatCount)
        {
            throw Conflict("organization_capacity_in_use", "Sức chứa mới nhỏ hơn số seat đang dùng hoặc đang giữ.");
        }
        if (request.IsAutoAssignmentEnabled && await accountDb.OrganizationPoolOrganizations.AnyAsync(
                x => x.OrganizationId == request.OrganizationId &&
                     x.OrganizationPoolId != poolId &&
                     x.IsAutoAssignmentEnabled,
                cancellationToken))
        {
            throw Conflict("organization_already_in_active_pool", "Tổ chức đang bật nhận người ở một pool khác.");
        }

        var readinessMessage = NormalizeOptional(request.ReadinessMessage, 500);
        if (request.IsReady)
        {
            var readiness = await runtimeReadinessEvaluator.EvaluateAsync(request.OrganizationId, cancellationToken);
            if (!readiness.Ready)
            {
                throw Conflict("organization_not_ready", readiness.Message);
            }
            readinessMessage = readiness.Message;
        }

        var now = UtcNow();
        var link = existing ?? new OrganizationPoolOrganization
        {
            OrganizationPoolId = poolId,
            OrganizationId = request.OrganizationId,
            CreatedAtUtc = now
        };
        link.SeatCapacity = request.SeatCapacity;
        link.Priority = request.Priority;
        link.IsAutoAssignmentEnabled = request.IsAutoAssignmentEnabled;
        link.IsReady = request.IsReady;
        link.ReadinessMessage = readinessMessage;
        link.UpdatedAtUtc = now;
        if (existing is null)
        {
            accountDb.OrganizationPoolOrganizations.Add(link);
        }
        AddAudit(adminUserId, existing is null ? "OrganizationAddedToPool" : "OrganizationPoolCapacityUpdated", new
        {
            poolId,
            request.OrganizationId,
            request.SeatCapacity,
            request.Priority,
            request.IsAutoAssignmentEnabled,
            request.IsReady
        }, now);
        await accountDb.SaveChangesAsync(cancellationToken);
        var poolCanAllocate = pool.Status == OrganizationPoolStatuses.Active &&
                              await (
                                  from mapping in accountDb.LicensePlanOrganizationPools.AsNoTracking()
                                  join plan in accountDb.LicensePlans.AsNoTracking()
                                      on mapping.LicensePlanId equals plan.LicensePlanId
                                  where mapping.OrganizationPoolId == poolId &&
                                        mapping.IsActive &&
                                        plan.IsActive &&
                                        plan.IsPublic &&
                                        plan.SalePriceVnd > 0 &&
                                        plan.DefaultDurationDays > 0 &&
                                        plan.DefaultDurationDays <= 3650
                                  select mapping.LicensePlanId)
                              .AnyAsync(cancellationToken);
        return ToOrganizationResponse(link, organization, poolCanAllocate);
    }

    public async Task RemoveOrganizationAsync(
        Guid poolId,
        Guid organizationId,
        string adminUserId,
        CancellationToken cancellationToken)
    {
        var link = await accountDb.OrganizationPoolOrganizations.SingleOrDefaultAsync(
            x => x.OrganizationPoolId == poolId && x.OrganizationId == organizationId,
            cancellationToken) ?? throw NotFound("organization_pool_link_not_found", "Tổ chức không thuộc pool này.");
        if (link.ActiveSeatCount > 0 || link.ReservedSeatCount > 0)
        {
            throw Conflict("organization_capacity_in_use", "Không thể gỡ tổ chức khi còn seat đang dùng hoặc đang giữ.");
        }
        accountDb.OrganizationPoolOrganizations.Remove(link);
        var now = UtcNow();
        AddAudit(adminUserId, "OrganizationRemovedFromPool", new { poolId, organizationId }, now);
        await accountDb.SaveChangesAsync(cancellationToken);
    }

    public async Task<LicensePlanOrganizationPoolResponse> UpsertLicensePlanAsync(
        Guid licensePlanId,
        SaveLicensePlanOrganizationPoolRequest request,
        string adminUserId,
        CancellationToken cancellationToken)
    {
        if (request.DefaultMemberMonthlyBudgetLimit < 0)
        {
            throw Validation("invalid_member_budget", "Hạn mức AI mặc định của thành viên không được âm.");
        }
        var plan = await accountDb.LicensePlans.SingleOrDefaultAsync(x => x.LicensePlanId == licensePlanId, cancellationToken)
            ?? throw NotFound("license_plan_not_found", "Không tìm thấy gói license.");
        var pool = await RequirePoolAsync(request.OrganizationPoolId, cancellationToken);
        var existing = await accountDb.LicensePlanOrganizationPools.SingleOrDefaultAsync(
            x => x.LicensePlanId == licensePlanId,
            cancellationToken);
        if (existing is not null &&
            existing.OrganizationPoolId != request.OrganizationPoolId &&
            await accountDb.OrganizationSeatAssignments.AnyAsync(
                x => x.LicensePlanId == licensePlanId &&
                     (x.Status == OrganizationSeatAssignmentStatuses.Reserved ||
                      x.Status == OrganizationSeatAssignmentStatuses.Scheduled ||
                      x.Status == OrganizationSeatAssignmentStatuses.Active),
                cancellationToken))
        {
            throw Conflict(
                "license_plan_pool_in_use",
                "Không thể chuyển gói sang pool khác khi còn seat đang hoạt động hoặc giữ chỗ.");
        }
        var now = UtcNow();
        var link = existing ?? new LicensePlanOrganizationPool
        {
            LicensePlanId = licensePlanId,
            CreatedAtUtc = now
        };
        link.OrganizationPoolId = request.OrganizationPoolId;
        link.DefaultMemberMonthlyBudgetLimit = request.DefaultMemberMonthlyBudgetLimit;
        link.IsActive = request.IsActive;
        link.UpdatedAtUtc = now;
        if (existing is null)
        {
            accountDb.LicensePlanOrganizationPools.Add(link);
        }
        AddAudit(adminUserId, existing is null ? "LicensePlanMappedToOrganizationPool" : "LicensePlanOrganizationPoolUpdated", new
        {
            licensePlanId,
            request.OrganizationPoolId,
            request.DefaultMemberMonthlyBudgetLimit,
            request.IsActive
        }, now);
        await accountDb.SaveChangesAsync(cancellationToken);
        return ToPlanResponse(link, plan, pool);
    }

    public async Task RemoveLicensePlanAsync(Guid licensePlanId, string adminUserId, CancellationToken cancellationToken)
    {
        var link = await accountDb.LicensePlanOrganizationPools.SingleOrDefaultAsync(
            x => x.LicensePlanId == licensePlanId,
            cancellationToken) ?? throw NotFound("license_plan_pool_not_found", "Gói chưa được ánh xạ vào pool.");
        if (await accountDb.OrganizationSeatAssignments.AnyAsync(
                x => x.LicensePlanId == licensePlanId &&
                     (x.Status == OrganizationSeatAssignmentStatuses.Reserved ||
                      x.Status == OrganizationSeatAssignmentStatuses.Scheduled ||
                      x.Status == OrganizationSeatAssignmentStatuses.Active),
                cancellationToken))
        {
            throw Conflict("license_plan_pool_in_use", "Không thể gỡ mapping khi gói còn seat đang hoạt động hoặc giữ chỗ.");
        }
        accountDb.LicensePlanOrganizationPools.Remove(link);
        var now = UtcNow();
        AddAudit(adminUserId, "LicensePlanRemovedFromOrganizationPool", new { licensePlanId, link.OrganizationPoolId }, now);
        await accountDb.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrganizationSeatAssignmentResponse>> GetAssignmentsAsync(
        string? status,
        int take,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = NormalizeAssignmentStatus(status);
        var safeTake = Math.Clamp(take, 1, 200);
        var rows = await BuildAssignmentQuery(normalizedStatus)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(safeTake)
            .ToListAsync(cancellationToken);
        return await MapAssignmentsAsync(rows, cancellationToken);
    }

    public async Task<RetryOrganizationSeatAssignmentResponse> RetryAssignmentAsync(
        Guid assignmentId,
        string adminUserId,
        CancellationToken cancellationToken)
    {
        var assignment = await accountDb.OrganizationSeatAssignments.SingleOrDefaultAsync(
            x => x.OrganizationSeatAssignmentId == assignmentId,
            cancellationToken)
            ?? throw NotFound("organization_seat_assignment_not_found", "Không tìm thấy seat assignment.");
        var fulfilled = await paymentService.RetryProvisioningAsync(
            assignment.LicensePaymentId,
            cancellationToken);
        var payment = await accountDb.LicensePayments.AsNoTracking().SingleAsync(
            x => x.LicensePaymentId == assignment.LicensePaymentId,
            cancellationToken);
        var refreshed = await accountDb.OrganizationSeatAssignments.AsNoTracking().SingleOrDefaultAsync(
            x => x.LicensePaymentId == assignment.LicensePaymentId,
            cancellationToken);
        var mapped = refreshed is null
            ? null
            : (await MapAssignmentsAsync([refreshed], cancellationToken)).Single();
        var now = UtcNow();
        AddAudit(adminUserId, "OrganizationSeatAssignmentRetryRequested", new
        {
            assignment.OrganizationSeatAssignmentId,
            assignment.LicensePaymentId,
            fulfilled,
            payment.Status,
            payment.FailureCode
        }, now);
        await accountDb.SaveChangesAsync(cancellationToken);
        return new RetryOrganizationSeatAssignmentResponse(
            payment.LicensePaymentId,
            payment.Status,
            mapped,
            fulfilled
                ? "Đã cấp license và tổ chức thành công."
                : "Thanh toán vẫn đang chờ một tổ chức còn sức chứa và sẵn sàng.");
    }

    private IQueryable<OrganizationSeatAssignment> BuildAssignmentQuery(string? status)
    {
        var query = accountDb.OrganizationSeatAssignments.AsNoTracking();
        return status is null ? query : query.Where(x => x.Status == status);
    }

    private async Task<IReadOnlyList<OrganizationSeatAssignmentResponse>> MapAssignmentsAsync(
        IReadOnlyList<OrganizationSeatAssignment> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }
        var poolIds = rows.Select(x => x.OrganizationPoolId).Distinct().ToArray();
        var organizationIds = rows.Select(x => x.OrganizationId).Distinct().ToArray();
        var planIds = rows.Select(x => x.LicensePlanId).Distinct().ToArray();
        var paymentIds = rows.Select(x => x.LicensePaymentId).Distinct().ToArray();
        var userIds = rows.Select(x => x.UserId).Distinct().ToArray();
        var pools = await accountDb.OrganizationPools.AsNoTracking().Where(x => poolIds.Contains(x.OrganizationPoolId)).ToDictionaryAsync(x => x.OrganizationPoolId, cancellationToken);
        var organizations = await accountDb.Organizations.AsNoTracking().Where(x => organizationIds.Contains(x.OrganizationId)).ToDictionaryAsync(x => x.OrganizationId, cancellationToken);
        var plans = await accountDb.LicensePlans.AsNoTracking().Where(x => planIds.Contains(x.LicensePlanId)).ToDictionaryAsync(x => x.LicensePlanId, cancellationToken);
        var payments = await accountDb.LicensePayments.AsNoTracking().Where(x => paymentIds.Contains(x.LicensePaymentId)).ToDictionaryAsync(x => x.LicensePaymentId, cancellationToken);
        var users = await accountDb.Users.AsNoTracking().Where(x => userIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        return rows.Select(row => new OrganizationSeatAssignmentResponse(
            row.OrganizationSeatAssignmentId,
            row.OrganizationPoolId,
            pools.TryGetValue(row.OrganizationPoolId, out var pool) ? pool.Code : "unknown",
            row.OrganizationId,
            organizations.TryGetValue(row.OrganizationId, out var organization) ? organization.Code : "unknown",
            organization?.Name ?? "Tổ chức không còn khả dụng",
            row.UserId,
            users.TryGetValue(row.UserId, out var user) ? user.Email ?? row.UserId : row.UserId,
            row.LicensePlanId,
            plans.TryGetValue(row.LicensePlanId, out var plan) ? plan.PlanCode : "unknown",
            row.LicensePaymentId,
            payments.TryGetValue(row.LicensePaymentId, out var payment) ? payment.OrderCode : "unknown",
            row.UserLicenseId,
            row.Status,
            row.ConsumesSeat,
            row.MembershipManaged,
            row.ReservedAtUtc,
            row.ReservationExpiresAtUtc,
            row.StartsAtUtc,
            row.EndsAtUtc,
            row.ActivatedAtUtc,
            row.ReleasedAtUtc,
            row.ReleaseReason,
            row.FailureCode ?? (payments.TryGetValue(row.LicensePaymentId, out var failedPayment)
                ? failedPayment.FailureCode
                : null),
            row.UpdatedAtUtc,
            payments.TryGetValue(row.LicensePaymentId, out var currentPayment)
                ? currentPayment.Status
                : null)).ToArray();
    }

    private async Task<IReadOnlyDictionary<Guid, OrganizationProvisioningReadiness>> EvaluateRuntimeReadinessAsync(
        IEnumerable<OrganizationPoolOrganization> links,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<Guid, OrganizationProvisioningReadiness>();
        foreach (var organizationId in links.Select(x => x.OrganizationId).Distinct())
        {
            results[organizationId] = await runtimeReadinessEvaluator.EvaluateAsync(
                organizationId,
                cancellationToken);
        }
        return results;
    }

    private async Task<ProvisioningReadiness> EvaluateReadinessAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var organization = await governanceDb.Organizations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId, cancellationToken);
        if (organization is null)
        {
            return new(false, "Không tìm thấy tổ chức.");
        }

        var reasons = new List<string>();
        if (organization.Status != OrganizationStatuses.Active)
        {
            reasons.Add("tổ chức chưa Active");
        }
        if (organization.MonthlyBudgetLimit <= 0)
        {
            reasons.Add("budget đang bằng 0");
        }

        var credentials = await governanceDb.OrganizationProviderCredentials.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.Status == ProviderCredentialStatuses.Active)
            .Select(x => x.ProviderId)
            .ToListAsync(cancellationToken);
        var credentialIds = credentials.ToHashSet();
        var policies = await governanceDb.OrganizationVideoPolicies.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.IsActive)
            .ToListAsync(cancellationToken);
        var providers = await providerDb.Providers.AsNoTracking()
            .Include(x => x.Models)
            .ThenInclude(x => x.CostRates)
            .ToListAsync(cancellationToken);
        var now = UtcNow();
        var openAi = providers.SingleOrDefault(x => x.ProviderCode == "openai");
        var openAiTextReady = openAi is { IsEnabled: true } && credentialIds.Contains(openAi.ProviderId) &&
                              openAi.Models.Any(model =>
                                  model.IsEnabled && model.Modality == "Text" &&
                                  HasActiveRate(model, "InputToken", now) &&
                                  HasActiveRate(model, "OutputToken", now));
        if (!openAiTextReady)
        {
            reasons.Add("OpenAI text/credential/rate chưa sẵn sàng");
        }

        var modelsById = providers.SelectMany(x => x.Models.Select(model => (Provider: x, Model: model)))
            .ToDictionary(x => x.Model.ProviderModelId);
        var videoReady = policies.Any(policy =>
        {
            if (!modelsById.TryGetValue(policy.ProviderModelId, out var selected) ||
                !selected.Provider.IsEnabled || !selected.Model.IsEnabled ||
                !credentialIds.Contains(selected.Provider.ProviderId))
            {
                return false;
            }
            var usageType = selected.Provider.ProviderCode == "byteplus" ? "OutputToken" : "VideoSecond";
            return HasActiveRate(selected.Model, usageType, now);
        });
        if (!videoReady)
        {
            reasons.Add("video policy/credential/rate chưa sẵn sàng");
        }

        return reasons.Count == 0
            ? new(true, "Credential, policy, pricing và budget đã sẵn sàng.")
            : new(false, $"Tổ chức chưa sẵn sàng: {string.Join("; ", reasons)}.");
    }

    private static bool HasActiveRate(AiProviderModel model, string usageType, DateTime now) =>
        model.CostRates.Any(rate => rate.IsActive && rate.UsageType == usageType &&
                                    rate.EffectiveFromUtc <= now &&
                                    (rate.EffectiveToUtc == null || rate.EffectiveToUtc > now));

    private async Task<OrganizationPool> RequirePoolAsync(Guid poolId, CancellationToken cancellationToken) =>
        await accountDb.OrganizationPools.SingleOrDefaultAsync(x => x.OrganizationPoolId == poolId, cancellationToken)
        ?? throw NotFound("organization_pool_not_found", "Không tìm thấy pool tổ chức.");

    private static OrganizationPoolSummaryResponse ToSummary(
        OrganizationPool pool,
        IReadOnlyCollection<OrganizationPoolOrganization> organizations,
        IReadOnlyCollection<LicensePlanOrganizationPool> plans,
        IReadOnlyDictionary<Guid, Organization> organizationDirectory,
        IReadOnlyDictionary<Guid, LicensePlan> planDirectory,
        IReadOnlyDictionary<Guid, OrganizationProvisioningReadiness> runtimeReadiness)
    {
        var rows = organizations.Where(x => x.OrganizationPoolId == pool.OrganizationPoolId).ToArray();
        var allocatableRows = rows.Where(x =>
                x.IsAutoAssignmentEnabled &&
                x.IsReady &&
                organizationDirectory.TryGetValue(x.OrganizationId, out var organization) &&
                organization.Status == OrganizationStatuses.Active &&
                runtimeReadiness.TryGetValue(x.OrganizationId, out var readiness) &&
                readiness.Ready)
            .ToArray();
        var planRows = plans.Where(x => x.OrganizationPoolId == pool.OrganizationPoolId).ToArray();
        var capacity = rows.Sum(x => x.SeatCapacity);
        var active = rows.Sum(x => x.ActiveSeatCount);
        var reserved = rows.Sum(x => x.ReservedSeatCount);
        var activePlanCount = planRows.Count(x =>
            x.IsActive &&
            planDirectory.TryGetValue(x.LicensePlanId, out var plan) &&
            IsSellable(plan));
        var poolCanAllocate = pool.Status == OrganizationPoolStatuses.Active && activePlanCount > 0;
        var allocatableCapacity = poolCanAllocate ? allocatableRows.Sum(x => x.SeatCapacity) : 0;
        var allocatableAvailable = poolCanAllocate
            ? allocatableRows.Sum(x => Math.Max(0, x.SeatCapacity - x.ActiveSeatCount - x.ReservedSeatCount))
            : 0;
        return new OrganizationPoolSummaryResponse(
            pool.OrganizationPoolId,
            pool.Code,
            pool.Name,
            pool.AllocationStrategy,
            pool.Status,
            rows.Length,
            planRows.Length,
            capacity,
            active,
            reserved,
            Math.Max(0, capacity - active - reserved),
            pool.CreatedAtUtc,
            pool.UpdatedAtUtc,
            allocatableRows.Length,
            activePlanCount,
            allocatableCapacity,
            allocatableAvailable);
    }

    private static OrganizationPoolOrganizationResponse ToOrganizationResponse(
        OrganizationPoolOrganization link,
        Organization organization,
        bool poolCanAllocate) =>
        new(
            link.OrganizationPoolId,
            organization.OrganizationId,
            organization.Code,
            organization.Name,
            organization.Status,
            link.SeatCapacity,
            link.ActiveSeatCount,
            link.ReservedSeatCount,
            Math.Max(0, link.SeatCapacity - link.ActiveSeatCount - link.ReservedSeatCount),
            link.Priority,
            link.IsAutoAssignmentEnabled,
            link.IsReady,
            link.ReadinessMessage,
            link.UpdatedAtUtc,
            poolCanAllocate &&
            link.IsAutoAssignmentEnabled &&
            link.IsReady &&
            organization.Status == OrganizationStatuses.Active,
            poolCanAllocate &&
            link.IsAutoAssignmentEnabled &&
            link.IsReady &&
            organization.Status == OrganizationStatuses.Active
                ? Math.Max(0, link.SeatCapacity - link.ActiveSeatCount - link.ReservedSeatCount)
                : 0);

    private static LicensePlanOrganizationPoolResponse ToPlanResponse(
        LicensePlanOrganizationPool link,
        LicensePlan plan,
        OrganizationPool pool) =>
        new(
            plan.LicensePlanId,
            plan.PlanCode,
            plan.Name,
            pool.OrganizationPoolId,
            pool.Code,
            pool.Name,
            link.DefaultMemberMonthlyBudgetLimit,
            link.IsActive,
            link.UpdatedAtUtc,
            plan.IsActive,
            plan.IsPublic,
            IsSellable(plan));

    private static bool IsSellable(LicensePlan plan) =>
        plan.IsActive &&
        plan.IsPublic &&
        plan.SalePriceVnd > 0 &&
        plan.DefaultDurationDays is > 0 and <= 3650;

    private void AddAudit(string userId, string eventType, object data, DateTime now) =>
        accountDb.AccountAuditLogs.Add(new AccountAuditLog
        {
            UserId = userId,
            EventType = eventType,
            Succeeded = true,
            DetailsJson = JsonSerializer.Serialize(data),
            OccurredAtUtc = now
        });

    private static PoolValues ValidatePool(SaveOrganizationPoolRequest request)
    {
        var code = request.Code.Trim().ToLowerInvariant();
        var name = request.Name.Trim();
        var status = request.Status.Trim();
        if (!PoolCodeRegex().IsMatch(code))
        {
            throw Validation("invalid_organization_pool_code", "Mã pool chỉ gồm chữ thường, số và dấu gạch ngang, dài 2–50 ký tự.");
        }
        if (name.Length is < 2 or > 200)
        {
            throw Validation("invalid_organization_pool_name", "Tên pool phải dài từ 2 đến 200 ký tự.");
        }
        if (status is not (OrganizationPoolStatuses.Active or OrganizationPoolStatuses.Inactive))
        {
            throw Validation("invalid_organization_pool_status", "Trạng thái pool không hợp lệ.");
        }
        return new(code, name, status);
    }

    private static string? NormalizeAssignmentStatus(string? status)
    {
        var value = status?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return value switch
        {
            OrganizationSeatAssignmentStatuses.Reserved => value,
            OrganizationSeatAssignmentStatuses.Scheduled => value,
            OrganizationSeatAssignmentStatuses.Active => value,
            OrganizationSeatAssignmentStatuses.Released => value,
            OrganizationSeatAssignmentStatuses.Failed => value,
            _ => throw Validation("invalid_assignment_status", "Trạng thái seat assignment không hợp lệ.")
        };
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized[..Math.Min(normalized.Length, maxLength)];
    }

    private static AccountApiException Validation(string code, string message) =>
        new(StatusCodes.Status422UnprocessableEntity, code, message);

    private static AccountApiException NotFound(string code, string message) =>
        new(StatusCodes.Status404NotFound, code, message);

    private static AccountApiException Conflict(string code, string message) =>
        new(StatusCodes.Status409Conflict, code, message);

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private sealed record PoolValues(string Code, string Name, string Status);
    private sealed record ProvisioningReadiness(bool Ready, string Message);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,48}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex PoolCodeRegex();
}
