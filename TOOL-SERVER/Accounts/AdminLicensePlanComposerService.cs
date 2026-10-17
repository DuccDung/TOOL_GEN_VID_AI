using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Organizations;
using TOOL_SERVER.Payments;
using TOOL_SERVER.Vietsub.Translation;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_SERVER.Accounts;

public sealed class AdminLicensePlanComposerService(
    AccountDbContext db,
    IAdminLicenseService licenses,
    IOrganizationProvisioningReadinessEvaluator readiness,
    ISepayPaymentConfiguration paymentConfiguration,
    TimeProvider time,
    IOptions<VietsubCloudTranslationOptions>? cloudOptions = null)
{
    private const string AuditEvent = "LicensePlanComposed";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AdminLicensePlanSetupResponse> GetSetupAsync(CancellationToken ct)
    {
        var payments = await paymentConfiguration.GetOptionsAsync(ct);
        var groups = await GetGroupsAsync(null, ct);
        var local = await GetGroupsAsync(null, ct, LicenseProductPolicy.Vietsub, false);
        var cloud = await GetGroupsAsync(null, ct, LicenseProductPolicy.Vietsub, true);
        return new(payments.IsReady,
            payments.IsReady ? null : "Thanh toán SePay chưa sẵn sàng. Hãy chọn tài khoản nhận tiền và bật nhận thanh toán.",
            groups.Select(group => group with { VietsubLocal = Availability(local.Single(x => x.OrganizationPoolId == group.OrganizationPoolId)),
                VietsubCloud = Availability(cloud.Single(x => x.OrganizationPoolId == group.OrganizationPoolId)) }).ToArray());
    }

    public async Task<ComposeLicensePlanResponse> CreateAsync(
        ComposeLicensePlanRequest request, string adminUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(adminUserId)) throw Error("admin_required", "Cần quyền quản trị hệ thống.", 403);
        if (request.CreationId == Guid.Empty || request.OrganizationPoolId == Guid.Empty || request.SourcePlanId == Guid.Empty)
            throw Error("invalid_plan_request", "Mã thao tác, gói nguồn hoặc nhóm nhận khách không hợp lệ.", 400);
        if (string.IsNullOrWhiteSpace(request.Name) || request.MemberMonthlyAiBudget < 0)
            throw Error("invalid_plan_request", "Tên gói hoặc hạn mức AI không hợp lệ.", 400);
        if (request.ProductScope is not (null or LicenseProductPolicy.AllTools or LicenseProductPolicy.Vietsub))
            throw Error("invalid_product_scope", "Loại gói không hợp lệ.", 400);
        if (request.PlanId is { } editId)
            return await UpdateAsync(editId, request, adminUserId, ct);

        // A deterministic unique plan code and audit hash survive retries without a new schema.
        var code = $"goi-{request.CreationId:N}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, JsonOptions))));
        async Task<ComposeLicensePlanResponse> ComposeCoreAsync()
        {
            var operationMarker = $"\"creationId\":\"{request.CreationId:D}\"";
            var previous = await db.AccountAuditLogs.AsNoTracking().SingleOrDefaultAsync(x =>
                (x.EventType == AuditEvent || x.EventType == "LicensePlanEdited") && x.DetailsJson != null &&
                x.DetailsJson.Contains(operationMarker), ct);
            if (previous is not null)
            {
                if (previous.EventType != AuditEvent || previous.UserId != adminUserId || JsonDocumentHash(previous.DetailsJson) != hash)
                    throw Error("idempotency_key_conflict", "Yêu cầu này đã được dùng cho một gói hoặc nội dung khác.");
                using var details = JsonDocument.Parse(previous.DetailsJson!);
                var savedId = details.RootElement.GetProperty("licensePlanId").GetGuid();
                var existing = await db.LicensePlans.AsNoTracking().SingleOrDefaultAsync(x => x.LicensePlanId == savedId, ct)
                    ?? throw Error("license_plan_not_found", "Gói của yêu cầu trước không còn tồn tại.", 404);
                return new ComposeLicensePlanResponse(AdminLicenseService.ToPlanResponse(existing), LicenseProductPolicy.IsLocalOnly(existing.FeatureFlagsJson) ? null : request.OrganizationPoolId, request.Publish, true);
            }
            if (await db.LicensePlans.AsNoTracking().AnyAsync(x => x.PlanCode == code, ct))
                throw Error("idempotency_key_conflict", "Mã thao tác đã được dùng cho một gói khác.");

            string? sourceFlags = null;
            if (request.SourcePlanId is { } sourceId)
                sourceFlags = (await db.LicensePlans.AsNoTracking().SingleOrDefaultAsync(x => x.LicensePlanId == sourceId, ct)
                    ?? throw Error("license_plan_not_found", "Gói nguồn không còn tồn tại.", 404)).FeatureFlagsJson;
            sourceFlags = ProductFlags(sourceFlags, request);
            if (LicenseProductPolicy.Scope(sourceFlags) == "invalid")
                throw Error("invalid_product_scope", "Quyền gói nguồn không hợp lệ. Hãy chọn loại gói rõ ràng.", 400);
            var requiresAllocation = LicenseProductPolicy.RequiresOrganizationAllocation(sourceFlags);
            var poolIdForPlan = requiresAllocation ? request.OrganizationPoolId : null;
            var quota = MemberQuota(sourceFlags, request);
            AdminLicensePlanGroupOption? group = null;
            if (poolIdForPlan is { } poolId)
                group = (await GetGroupsAsync(poolId, ct, LicenseProductPolicy.Scope(sourceFlags), LicenseProductPolicy.CloudAllowed(sourceFlags), quota)).SingleOrDefault()
                    ?? throw Error("organization_pool_not_found", "Không tìm thấy nhóm nhận khách.", 404);
            if (request.Publish && (request.IsActive ?? true))
            {
                if (requiresAllocation && group is null) throw Error("license_plan_pool_not_configured", "Hãy chọn nhóm nhận khách trước khi mở bán.");
                if (group is { Ready: false }) throw Error(group.BlockingCode!, group.BlockingMessage!);
                if (!(await paymentConfiguration.GetOptionsAsync(ct)).IsReady)
                    throw Error("payments_unavailable", "Thanh toán SePay chưa sẵn sàng. Gói chưa được tạo hoặc mở bán.");
            }
            if (request.Publish && (request.IsActive ?? true) && AdminSetupCapabilityReadiness.Required(sourceFlags))
                await RequireSetupGroupAsync(request.OrganizationPoolId!.Value, request.MemberMonthlyAiBudget, ct);
            var plan = await licenses.CreatePlanAsync(new SaveLicensePlanRequest(
                code, request.Name, request.Description, request.MaxActivatedDevices,
                request.MaxConcurrentSessions, request.OfflineGraceHours, request.DurationDays,
                sourceFlags, request.IsActive ?? request.Publish, request.PriceVnd, request.Publish, request.DisplayOrder,
                request.MarketingFeatures is null ? null : JsonSerializer.Serialize(request.MarketingFeatures)), adminUserId, ct);
            if (poolIdForPlan is { } selectedPool)
                db.LicensePlanOrganizationPools.Add(new LicensePlanOrganizationPool
                {
                    LicensePlanId = plan.LicensePlanId, OrganizationPoolId = selectedPool,
                    DefaultMemberMonthlyBudgetLimit = quota, IsActive = true,
                    CreatedAtUtc = time.GetUtcNow().UtcDateTime, UpdatedAtUtc = time.GetUtcNow().UtcDateTime
                });
            db.AccountAuditLogs.Add(new AccountAuditLog
            {
                UserId = adminUserId, EventType = AuditEvent, Succeeded = true,
                OccurredAtUtc = time.GetUtcNow().UtcDateTime,
                DetailsJson = JsonSerializer.Serialize(new { request.CreationId, requestHash = hash, plan.LicensePlanId,
                    request.OrganizationPoolId, request.Publish }, JsonOptions)
            });
            await db.SaveChangesAsync(ct);
            return new ComposeLicensePlanResponse(plan, poolIdForPlan, request.Publish, false);
        }
        // Setup publication owns the transaction, including group activation and the receipt.
        if (db.Database.CurrentTransaction is not null) return await ComposeCoreAsync();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var result = await ComposeCoreAsync();
            await transaction.CommitAsync(ct);
            return result;
        });
    }

    public async Task<AdminLicensePlanEditorResponse> GetEditorAsync(Guid planId, CancellationToken ct)
    {
        var plan = await db.LicensePlans.AsNoTracking().SingleOrDefaultAsync(x => x.LicensePlanId == planId, ct)
            ?? throw Error("license_plan_not_found", "Không tìm thấy gói sử dụng.", 404);
        var mapping = await db.LicensePlanOrganizationPools.AsNoTracking().SingleOrDefaultAsync(x => x.LicensePlanId == planId, ct);
        return new(AdminLicenseService.ToPlanResponse(plan), LicenseProductPolicy.IsLocalOnly(plan.FeatureFlagsJson) ? null : mapping?.OrganizationPoolId, mapping?.DefaultMemberMonthlyBudgetLimit, mapping?.UpdatedAtUtc);
    }

    private async Task<ComposeLicensePlanResponse> UpdateAsync(Guid planId, ComposeLicensePlanRequest request, string admin, CancellationToken ct)
    {
        if (planId == Guid.Empty || request.ExpectedUpdatedAtUtc is null || request.SourcePlanId is not null)
            throw Error("invalid_plan_request", "Yêu cầu sửa gói thiếu phiên bản hoặc có gói nguồn không hợp lệ.", 400);
        const string editEvent = "LicensePlanEdited";
        var marker = $"\"creationId\":\"{request.CreationId:D}\"";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, JsonOptions))));
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var audit = await db.AccountAuditLogs.AsNoTracking().SingleOrDefaultAsync(x =>
                (x.EventType == editEvent || x.EventType == AuditEvent) && x.DetailsJson != null && x.DetailsJson.Contains(marker), ct);
            if (audit is not null)
            {
                if (audit.EventType != editEvent || audit.UserId != admin || JsonDocumentHash(audit.DetailsJson) != hash)
                    throw Error("idempotency_key_conflict", "Yêu cầu này đã được dùng cho một nội dung hoặc quản trị viên khác.");
                var restored = await GetEditorAsync(planId, ct);
                await transaction.CommitAsync(ct);
                return new ComposeLicensePlanResponse(restored.Plan, restored.OrganizationPoolId, restored.Plan.IsPublic, true);
            }
            var current = await db.LicensePlans.AsNoTracking().SingleOrDefaultAsync(x => x.LicensePlanId == planId, ct)
                ?? throw Error("license_plan_not_found", "Gói không còn tồn tại.", 404);
            if (current.UpdatedAtUtc != request.ExpectedUpdatedAtUtc.Value)
                throw Error("license_plan_changed", "Gói đã được quản trị viên khác thay đổi. Đóng form và mở lại để tải bản mới.");
            if (request.ProductScope is not null && request.ProductScope != LicenseProductPolicy.Scope(current.FeatureFlagsJson) ||
                request.VietsubCloudEnabled is not null && request.VietsubCloudEnabled != LicenseProductPolicy.CloudAllowed(current.FeatureFlagsJson))
                throw Error("license_plan_entitlements_immutable", "Quyền gói hiện có được giữ nguyên. Hãy nhân bản để tạo gói có quyền khác.");
            var mapping = await db.LicensePlanOrganizationPools.SingleOrDefaultAsync(x => x.LicensePlanId == planId, ct);
            if (mapping?.UpdatedAtUtc != request.ExpectedMappingUpdatedAtUtc)
                throw Error("license_plan_mapping_changed", "Liên kết nhóm của gói đã thay đổi. Đóng form và mở lại để tải bản mới.");
            var requiresAllocation = LicenseProductPolicy.RequiresOrganizationAllocation(current.FeatureFlagsJson);
            var poolIdForPlan = requiresAllocation ? request.OrganizationPoolId : null;
            if (requiresAllocation && mapping is not null && mapping.OrganizationPoolId != request.OrganizationPoolId &&
                await db.OrganizationSeatAssignments.AnyAsync(x => x.LicensePlanId == planId &&
                    (x.Status == OrganizationSeatAssignmentStatuses.Active ||
                     x.Status == OrganizationSeatAssignmentStatuses.Reserved ||
                     x.Status == OrganizationSeatAssignmentStatuses.Scheduled), ct))
                throw Error("license_plan_pool_in_use", "Không thể chuyển hoặc bỏ nhóm khi gói còn khách hoặc chỗ đang giữ.");
            var quota = MemberQuota(current.FeatureFlagsJson, request);
            if (requiresAllocation) await LicensePlanQuotaGuard.EnsureCanChangeAsync(db, planId, mapping, quota, time.GetUtcNow().UtcDateTime, ct);
            AdminLicensePlanGroupOption? group = null;
            if (poolIdForPlan is { } pool)
                group = (await GetGroupsAsync(pool, ct, LicenseProductPolicy.Scope(current.FeatureFlagsJson), LicenseProductPolicy.CloudAllowed(current.FeatureFlagsJson), quota)).SingleOrDefault()
                    ?? throw Error("organization_pool_not_found", "Không tìm thấy nhóm nhận khách.", 404);
            var active = request.IsActive ?? request.Publish;
            if (request.Publish && active)
            {
                if (requiresAllocation && group is null) throw Error("license_plan_pool_not_configured", "Hãy chọn nhóm nhận khách trước khi mở bán.");
                if (group is { Ready: false }) throw Error(group.BlockingCode!, group.BlockingMessage!);
                if (!(await paymentConfiguration.GetOptionsAsync(ct)).IsReady)
                    throw Error("payments_unavailable", "Thanh toán SePay chưa sẵn sàng. Thay đổi chưa được lưu.");
                if (AdminSetupCapabilityReadiness.Required(current.FeatureFlagsJson))
                    await RequireSetupGroupAsync(request.OrganizationPoolId!.Value, request.MemberMonthlyAiBudget, ct);
            }
            var plan = await licenses.UpdatePlanAsync(planId, new SaveLicensePlanRequest(current.PlanCode, request.Name,
                request.Description, request.MaxActivatedDevices, request.MaxConcurrentSessions, request.OfflineGraceHours,
                request.DurationDays, current.FeatureFlagsJson, active, request.PriceVnd, request.Publish, request.DisplayOrder,
                request.MarketingFeatures is null ? null : JsonSerializer.Serialize(request.MarketingFeatures)), admin, ct);
            var now = time.GetUtcNow().UtcDateTime;
            if (poolIdForPlan is { } selectedPool)
            {
                if (mapping is null)
                {
                    mapping = new LicensePlanOrganizationPool { LicensePlanId = planId, CreatedAtUtc = now, IsActive = true };
                    db.LicensePlanOrganizationPools.Add(mapping);
                }
                mapping.OrganizationPoolId = selectedPool;
                mapping.DefaultMemberMonthlyBudgetLimit = quota;
                if (request.Publish && active) mapping.IsActive = true;
                mapping.UpdatedAtUtc = now;
            }
            // Keep historical Local mappings/assignments for reconciliation; new Local purchases never use them.
            else if (requiresAllocation && mapping is not null) db.LicensePlanOrganizationPools.Remove(mapping);
            db.AccountAuditLogs.Add(new AccountAuditLog { UserId = admin, EventType = editEvent, Succeeded = true,
                OccurredAtUtc = now, DetailsJson = JsonSerializer.Serialize(new { request.CreationId, requestHash = hash,
                    LicensePlanId = planId, request.OrganizationPoolId, request.Publish }, JsonOptions) });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new ComposeLicensePlanResponse(plan, poolIdForPlan, request.Publish, false);
        });
    }

    private async Task<IReadOnlyList<AdminLicensePlanGroupOption>> GetGroupsAsync(Guid? selected, CancellationToken ct,
        string? productScope = null, bool cloud = false, decimal? memberBudget = null)
    {
        var pools = await db.OrganizationPools.AsNoTracking().Where(x => selected == null || x.OrganizationPoolId == selected)
            .OrderBy(x => x.Name).ToListAsync(ct);
        var ids = pools.Select(x => x.OrganizationPoolId).ToArray();
        var links = await db.OrganizationPoolOrganizations.AsNoTracking().Where(x => ids.Contains(x.OrganizationPoolId)).ToListAsync(ct);
        var orgIds = links.Select(x => x.OrganizationId).Distinct().ToArray();
        var organizations = await db.Organizations.AsNoTracking().Where(x => orgIds.Contains(x.OrganizationId)).ToDictionaryAsync(x => x.OrganizationId, ct);
        var checks = new Dictionary<Guid, OrganizationProvisioningReadiness>();
        var options = new List<AdminLicensePlanGroupOption>();
        foreach (var pool in pools)
        {
            var rows = links.Where(x => x.OrganizationPoolId == pool.OrganizationPoolId).ToArray();
            string? reasonCode = null, reason = null;
            AdminLicensePlanSetupTarget? setupTarget = null;
            var available = 0;
            if (pool.Status != OrganizationPoolStatuses.Active) { reasonCode = "organization_pool_inactive"; reason = "Nhóm đang ngừng nhận khách."; }
            else if (rows.Length == 0) { reasonCode = "pool_organization_missing"; reason = "Nhóm chưa có tổ chức nhận khách. Hãy thêm tổ chức và sức chứa."; }
            else
            {
                foreach (var row in rows.Where(x => x.IsAutoAssignmentEnabled))
                {
                    if (!organizations.TryGetValue(row.OrganizationId, out var org) || org.Status != OrganizationStatuses.Active) continue;
                    if (!checks.TryGetValue(row.OrganizationId, out var check))
                        checks[row.OrganizationId] = check = productScope == LicenseProductPolicy.Vietsub
                            ? await VietsubPlanReadiness.EvaluateAsync(db, row.OrganizationId, cloud, memberBudget, time.GetUtcNow().UtcDateTime, cloudOptions?.Value, ct)
                            : await readiness.EvaluateAsync(row.OrganizationId, ct);
                    if (!check.Ready) {
                        reason = check.Message;
                        setupTarget = new(row.OrganizationId, check.Target ?? "capacity", "openai", check.ModelId);
                        continue;
                    }
                    setupTarget ??= new(row.OrganizationId, check.Target ?? "credential", "openai", check.ModelId);
                    if (productScope != LicenseProductPolicy.Vietsub && !row.IsReady) { reason ??= "Tổ chức chưa được kiểm tra sẵn sàng. Hãy bấm Kiểm tra lại trong thiết lập phân bổ."; continue; }
                    available = (int)Math.Min(int.MaxValue, (long)available + Math.Max(0, row.SeatCapacity - row.ActiveSeatCount - row.ReservedSeatCount));
                }
                if (available == 0)
                {
                    reasonCode = reason is not null ? "organization_not_ready" : "organization_capacity_unavailable";
                    reason ??= rows.All(x => !x.IsAutoAssignmentEnabled)
                        ? "Nhóm chưa bật tự động phân bổ khách."
                        : rows.All(x => !organizations.TryGetValue(x.OrganizationId, out var org) || org.Status != OrganizationStatuses.Active)
                            ? "Nhóm chưa có tổ chức đang hoạt động."
                            : "Nhóm đã hết chỗ nhận khách.";
                }
            }
            // Capacity is independent of existing plan mappings: the first plan can be published.
            options.Add(new(pool.OrganizationPoolId, pool.Name, available, available > 0 && reasonCode is null,
                available > 0 ? null : reasonCode, available > 0 ? null : reason, SetupTarget: setupTarget));
        }
        return options;
    }

    private async Task RequireSetupGroupAsync(Guid pool, decimal? quota, CancellationToken ct)
    {
        var links = await db.OrganizationPoolOrganizations.AsNoTracking().Where(x => x.OrganizationPoolId == pool &&
            x.IsAutoAssignmentEnabled && x.IsReady && x.SeatCapacity > x.ActiveSeatCount + x.ReservedSeatCount).ToListAsync(ct);
        foreach (var link in links)
            if (await AdminSetupCapabilityReadiness.ReadyAsync(db, link.OrganizationId, quota, time.GetUtcNow().UtcDateTime, ct)) return;
        throw Error("setup_not_ready", "Nhóm cần có tổ chức còn chỗ và đủ OpenAI nội dung/ảnh, Fal/Veo, đơn giá và ngân sách cho gói này.");
    }

    private static AdminLicensePlanGroupAvailability Availability(AdminLicensePlanGroupOption group) =>
        new(group.AvailableSeats, group.Ready, group.BlockingCode, group.BlockingMessage, group.SetupTarget);

    private static decimal? MemberQuota(string? flags, ComposeLicensePlanRequest request) =>
        LicenseProductPolicy.Scope(flags) == LicenseProductPolicy.Vietsub && !LicenseProductPolicy.CloudAllowed(flags)
            ? 0 : request.MemberMonthlyAiBudget;

    private static string? ProductFlags(string? previous, ComposeLicensePlanRequest request)
    {
        if (request.ProductScope is null && request.VietsubCloudEnabled is null) return previous;
        var scope = request.ProductScope ?? LicenseProductPolicy.Scope(previous);
        if (scope != LicenseProductPolicy.Vietsub && request.VietsubCloudEnabled is not null)
            throw Error("invalid_product_scope", "Tùy chọn dịch Cloud chỉ áp dụng cho gói tool dịch.", 400);
        var flags = JsonNode.Parse(previous ?? "{}") as JsonObject ?? throw Error("invalid_product_scope", "Quyền gói nguồn không hợp lệ.", 400);
        flags[LicenseProductPolicy.ScopeKey] = scope;
        if (scope == LicenseProductPolicy.Vietsub)
        {
            flags[LicenseProductPolicy.CloudKey] = request.VietsubCloudEnabled ??
                (LicenseProductPolicy.Scope(previous) == LicenseProductPolicy.Vietsub && LicenseProductPolicy.CloudAllowed(previous));
            flags.Remove(AdminSetupCapabilityReadiness.Flag);
        }
        else flags.Remove(LicenseProductPolicy.CloudKey);
        return flags.ToJsonString();
    }

    private static string? JsonDocumentHash(string? json)
    {
        using var document = JsonDocument.Parse(json ?? "{}");
        return document.RootElement.TryGetProperty("requestHash", out var value) ? value.GetString() : null;
    }
    private static AccountApiException Error(string code, string message, int status = 409) => new(status, code, message);
}
