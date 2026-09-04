using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Providers;
using TOOL_SHARED.Contracts.Organizations;

namespace TOOL_SERVER.Organizations;

public sealed record OrganizationRequestContext(
    string UserId,
    string? IpAddress,
    string? UserAgent,
    string CorrelationId);

public sealed record OrganizationOperationFailure(
    int StatusCode,
    string Code,
    string Message);

public sealed class OrganizationProviderCredentialRotationResult
{
    private OrganizationProviderCredentialRotationResult(
        OrganizationProviderResponse? response,
        OrganizationOperationFailure? failure)
    {
        Response = response;
        Failure = failure;
    }

    public OrganizationProviderResponse? Response { get; }

    public OrganizationOperationFailure? Failure { get; }

    public static OrganizationProviderCredentialRotationResult Success(OrganizationProviderResponse response) =>
        new(response ?? throw new ArgumentNullException(nameof(response)), null);

    public static OrganizationProviderCredentialRotationResult Rejected(OrganizationOperationFailure failure) =>
        new(null, failure ?? throw new ArgumentNullException(nameof(failure)));
}

public interface IOrganizationService
{
    Task<IReadOnlyList<OrganizationSummaryResponse>> GetMineAsync(string userId, CancellationToken cancellationToken);
    Task<OrganizationSummaryResponse> CreateAsync(CreateOrganizationRequest request, OrganizationRequestContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrganizationMemberResponse>> GetMembersAsync(Guid organizationId, string userId, CancellationToken cancellationToken);
    Task<OrganizationMemberResponse> AddMemberAsync(Guid organizationId, AddOrganizationMemberRequest request, OrganizationRequestContext context, CancellationToken cancellationToken);
    Task<OrganizationMemberResponse> UpdateMemberAsync(Guid organizationId, string memberUserId, UpdateOrganizationMemberRequest request, OrganizationRequestContext context, CancellationToken cancellationToken);
    Task<OrganizationSummaryResponse> UpdateBudgetAsync(Guid organizationId, UpdateOrganizationBudgetRequest request, OrganizationRequestContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrganizationProviderResponse>> GetProvidersAsync(Guid organizationId, string userId, CancellationToken cancellationToken);
    Task<OrganizationProviderCredentialRotationResult> RotateProviderCredentialAsync(Guid organizationId, string providerCode, SaveOrganizationProviderCredentialRequest request, OrganizationRequestContext context, CancellationToken cancellationToken);
    Task<OrganizationVideoPolicyResponse?> GetVideoPolicyAsync(Guid organizationId, string userId, string scope, CancellationToken cancellationToken);
    Task<OrganizationVideoPolicyResponse> UpdateVideoPolicyAsync(Guid organizationId, UpdateOrganizationVideoPolicyRequest request, OrganizationRequestContext context, CancellationToken cancellationToken);
    Task<OrganizationUsageResponse> GetUsageAsync(Guid organizationId, string userId, int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrganizationAuditItemResponse>> GetAuditAsync(Guid organizationId, string userId, int take, CancellationToken cancellationToken);
}

internal sealed partial class OrganizationService(
    AiGovernanceDbContext governanceDb,
    AccountDbContext accountDb,
    ProviderAdminDbContext providerDb,
    IProviderCredentialProtector credentialProtector,
    IOrganizationProviderCredentialTester credentialTester,
    IAiBudgetService budgetService,
    TimeProvider timeProvider) : IOrganizationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<OrganizationSummaryResponse>> GetMineAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var memberships = await governanceDb.OrganizationMembers
            .AsNoTracking()
            .Include(x => x.Organization)
            .Where(x => x.UserId == userId &&
                        x.Status == OrganizationMemberStatuses.Active &&
                        x.Organization.Status == OrganizationStatuses.Active)
            .OrderBy(x => x.Organization.Name)
            .ToListAsync(cancellationToken);
        if (memberships.Count == 0)
        {
            return [];
        }

        var organizationIds = memberships.Select(x => x.OrganizationId).ToArray();
        var memberCounts = await governanceDb.OrganizationMembers
            .AsNoTracking()
            .Where(x => organizationIds.Contains(x.OrganizationId) &&
                        x.Status != OrganizationMemberStatuses.Removed)
            .GroupBy(x => x.OrganizationId)
            .Select(group => new
            {
                OrganizationId = group.Key,
                Total = group.Count(),
                Active = group.Count(x => x.Status == OrganizationMemberStatuses.Active)
            })
            .ToDictionaryAsync(x => x.OrganizationId, cancellationToken);
        var activeCredentials = (await governanceDb.OrganizationProviderCredentials
            .AsNoTracking()
            .Where(x => organizationIds.Contains(x.OrganizationId) &&
                        x.Status == ProviderCredentialStatuses.Active)
            .Select(x => new { x.OrganizationId, x.ProviderId })
            .ToListAsync(cancellationToken))
            .Select(x => (x.OrganizationId, x.ProviderId))
            .ToHashSet();
        var providers = await providerDb.Providers
            .AsNoTracking()
            .Include(x => x.Models)
                .ThenInclude(x => x.CostRates)
            .Where(x => x.ProviderCode == ProviderCodes.OpenAi ||
                        x.ProviderCode == ProviderCodes.Kling ||
                        x.ProviderCode == ProviderCodes.BytePlus ||
                        x.ProviderCode == ProviderCodes.Fal)
            .ToListAsync(cancellationToken);
        var now = UtcNow();
        var result = new List<OrganizationSummaryResponse>(memberships.Count);
        foreach (var membership in memberships)
        {
            var budget = await budgetService.GetSnapshotAsync(membership.OrganizationId, cancellationToken);
            memberCounts.TryGetValue(membership.OrganizationId, out var counts);
            var readiness = providers.Select(provider =>
            {
                var model = provider.Models
                    .OrderByDescending(x => x.IsEnabled && x.IsDefault)
                    .ThenByDescending(x => x.IsEnabled)
                    .ThenByDescending(x => x.UpdatedAtUtc)
                    .FirstOrDefault();
                var usageTypes = model?.CostRates
                    .Where(x => x.IsActive &&
                                x.EffectiveFromUtc <= now &&
                                (x.EffectiveToUtc == null || x.EffectiveToUtc > now) &&
                                (x.UsageType != "VideoSecond" ||
                                 provider.ProviderCode switch
                                 {
                                     ProviderCodes.Kling => KlingNativeAudioPolicy.MatchesRateMetadata(x.MetadataJson),
                                     ProviderCodes.Fal when model is not null => FalVeoPolicy.MatchesRateMetadata(x.MetadataJson, model.ModelCode),
                                     _ => true
                                 }))
                    .Select(x => x.UsageType) ?? [];
                return OrganizationReadinessEvaluator.Evaluate(
                    provider.ProviderCode,
                    model?.ModelCode,
                    provider.IsEnabled,
                    model?.IsEnabled == true,
                    activeCredentials.Contains((membership.OrganizationId, provider.ProviderId)),
                    budget.HardLimit,
                    usageTypes);
            }).ToArray();
            result.Add(ToSummary(
                membership.Organization,
                membership.Role,
                budget,
                counts?.Total ?? 0,
                counts?.Active ?? 0,
                readiness));
        }
        return result;
    }

    public async Task<OrganizationSummaryResponse> CreateAsync(
        CreateOrganizationRequest request,
        OrganizationRequestContext context,
        CancellationToken cancellationToken)
    {
        var name = Required(request.Name, 200, "Tên tổ chức");
        ValidateBudget(request.MonthlyBudgetLimit, request.CurrencyCode);
        var code = NormalizeCode(request.Code, name);
        await using var transaction = await governanceDb.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        if (await governanceDb.Organizations.AnyAsync(x => x.Code == code, cancellationToken))
        {
            code = $"{code[..Math.Min(code.Length, 70)]}-{Guid.NewGuid():N}"[..Math.Min(80, Math.Min(code.Length, 70) + 33)];
        }

        var now = UtcNow();
        var organization = new Organization
        {
            OrganizationId = Guid.NewGuid(),
            Code = code,
            Name = name,
            Status = OrganizationStatuses.Active,
            MonthlyBudgetLimit = request.MonthlyBudgetLimit,
            CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(),
            CreatedByUserId = context.UserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        organization.Members.Add(new OrganizationMember
        {
            OrganizationId = organization.OrganizationId,
            UserId = context.UserId,
            Role = OrganizationMemberRoles.Owner,
            Status = OrganizationMemberStatuses.Active,
            JoinedAtUtc = now,
            UpdatedAtUtc = now
        });
        governanceDb.Organizations.Add(organization);
        AddAudit(organization.OrganizationId, context, "OrganizationCreated", new { organization.Code, organization.Name });
        await governanceDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var budget = await budgetService.GetSnapshotAsync(organization.OrganizationId, cancellationToken);
        return ToSummary(organization, OrganizationMemberRoles.Owner, budget);
    }

    public async Task<IReadOnlyList<OrganizationMemberResponse>> GetMembersAsync(
        Guid organizationId,
        string userId,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(organizationId, userId, cancellationToken);
        var members = await governanceDb.OrganizationMembers
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.Status != OrganizationMemberStatuses.Removed)
            .OrderBy(x => x.Role)
            .ThenBy(x => x.JoinedAtUtc)
            .ToListAsync(cancellationToken);
        var userIds = members.Select(x => x.UserId).ToArray();
        var users = await accountDb.Users
            .AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        return members.Select(x =>
        {
            users.TryGetValue(x.UserId, out var user);
            return new OrganizationMemberResponse(
                x.UserId,
                user?.Email ?? string.Empty,
                user?.DisplayName,
                x.Role,
                x.Status,
                x.MonthlyBudgetLimit,
                x.JoinedAtUtc,
                x.IsProvisioningManaged);
        }).ToArray();
    }

    public async Task<OrganizationMemberResponse> AddMemberAsync(
        Guid organizationId,
        AddOrganizationMemberRequest request,
        OrganizationRequestContext context,
        CancellationToken cancellationToken)
    {
        var actor = await RequireManagementRoleAsync(organizationId, context.UserId, false, cancellationToken);
        var role = ValidateRole(request.Role);
        EnsureCanAssignRole(actor.Role, role);
        ValidateMemberLimit(request.MonthlyBudgetLimit);
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var user = await accountDb.Users.SingleOrDefaultAsync(
            x => x.NormalizedEmail == normalizedEmail && x.DeletedAtUtc == null,
            cancellationToken)
            ?? throw new AccountApiException(StatusCodes.Status404NotFound, "user_not_found", "Không tìm thấy tài khoản theo email.");
        var now = UtcNow();
        var membership = await governanceDb.OrganizationMembers.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.UserId == user.Id,
            cancellationToken);
        if (membership is null)
        {
            membership = new OrganizationMember
            {
                OrganizationId = organizationId,
                UserId = user.Id,
                JoinedAtUtc = now
            };
            governanceDb.OrganizationMembers.Add(membership);
        }
        membership.Role = role;
        membership.Status = OrganizationMemberStatuses.Active;
        membership.IsProvisioningManaged = false;
        membership.MonthlyBudgetLimit = request.MonthlyBudgetLimit;
        membership.UpdatedAtUtc = now;
        AddAudit(organizationId, context, "OrganizationMemberAdded", new { user.Id, user.Email, role, request.MonthlyBudgetLimit });
        await governanceDb.SaveChangesAsync(cancellationToken);
        return new OrganizationMemberResponse(user.Id, user.Email!, user.DisplayName, role, membership.Status, membership.MonthlyBudgetLimit, membership.JoinedAtUtc, membership.IsProvisioningManaged);
    }

    public async Task<OrganizationMemberResponse> UpdateMemberAsync(
        Guid organizationId,
        string memberUserId,
        UpdateOrganizationMemberRequest request,
        OrganizationRequestContext context,
        CancellationToken cancellationToken)
    {
        var actor = await RequireManagementRoleAsync(organizationId, context.UserId, false, cancellationToken);
        var role = ValidateRole(request.Role);
        var status = ValidateMemberStatus(request.Status);
        EnsureCanAssignRole(actor.Role, role);
        ValidateMemberLimit(request.MonthlyBudgetLimit);
        var member = await governanceDb.OrganizationMembers.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.UserId == memberUserId,
            cancellationToken)
            ?? throw new AccountApiException(StatusCodes.Status404NotFound, "organization_member_not_found", "Không tìm thấy thành viên.");
        if (member.Role == OrganizationMemberRoles.Owner &&
            actor.Role != OrganizationMemberRoles.Owner)
        {
            throw new AccountApiException(
                StatusCodes.Status403Forbidden,
                "owner_role_required",
                "Chỉ Owner mới có thể thay đổi một thành viên đang giữ vai trò Owner.");
        }
        if (member.Role == OrganizationMemberRoles.Owner &&
            (role != OrganizationMemberRoles.Owner || status != OrganizationMemberStatuses.Active))
        {
            var activeOwnerCount = await governanceDb.OrganizationMembers.CountAsync(
                x => x.OrganizationId == organizationId &&
                     x.Role == OrganizationMemberRoles.Owner &&
                     x.Status == OrganizationMemberStatuses.Active,
                cancellationToken);
            if (activeOwnerCount <= 1)
            {
                throw new AccountApiException(StatusCodes.Status409Conflict, "last_owner_required", "Tổ chức phải còn ít nhất một Owner đang hoạt động.");
            }
        }
        member.Role = role;
        member.Status = status;
        member.IsProvisioningManaged = false;
        member.MonthlyBudgetLimit = request.MonthlyBudgetLimit;
        member.UpdatedAtUtc = UtcNow();
        AddAudit(organizationId, context, "OrganizationMemberUpdated", new { memberUserId, role, status, request.MonthlyBudgetLimit });
        await governanceDb.SaveChangesAsync(cancellationToken);
        var user = await accountDb.Users.AsNoTracking().SingleAsync(x => x.Id == memberUserId, cancellationToken);
        return new OrganizationMemberResponse(user.Id, user.Email!, user.DisplayName, role, status, member.MonthlyBudgetLimit, member.JoinedAtUtc, member.IsProvisioningManaged);
    }

    public async Task<OrganizationSummaryResponse> UpdateBudgetAsync(
        Guid organizationId,
        UpdateOrganizationBudgetRequest request,
        OrganizationRequestContext context,
        CancellationToken cancellationToken)
    {
        var actor = await RequireManagementRoleAsync(organizationId, context.UserId, true, cancellationToken);
        ValidateBudget(request.MonthlyBudgetLimit, request.CurrencyCode);
        var organization = actor.Organization;
        organization.MonthlyBudgetLimit = request.MonthlyBudgetLimit;
        organization.CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant();
        organization.UpdatedAtUtc = UtcNow();
        var now = UtcNow();
        var currentPeriod = await governanceDb.OrganizationBudgetPeriods.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.StartsAtUtc <= now && x.EndsAtUtc > now,
            cancellationToken);
        if (currentPeriod is not null)
        {
            if (request.MonthlyBudgetLimit < currentPeriod.ActualCost + currentPeriod.ReservedCost)
            {
                throw new AccountApiException(
                    StatusCodes.Status409Conflict,
                    "budget_below_committed_cost",
                    "Ngân sách mới không được thấp hơn chi phí đã dùng cộng khoản đang giữ.");
            }
            currentPeriod.HardLimit = request.MonthlyBudgetLimit;
            currentPeriod.CurrencyCode = organization.CurrencyCode;
            currentPeriod.UpdatedAtUtc = now;
        }
        AddAudit(organizationId, context, "OrganizationBudgetUpdated", request);
        await governanceDb.SaveChangesAsync(cancellationToken);
        var budget = await budgetService.GetSnapshotAsync(organizationId, cancellationToken);
        return ToSummary(organization, actor.Role, budget);
    }

    public async Task<IReadOnlyList<OrganizationProviderResponse>> GetProvidersAsync(
        Guid organizationId,
        string userId,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(organizationId, userId, cancellationToken);
        var providers = await providerDb.Providers
            .AsNoTracking()
            .Include(x => x.Models)
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
        var credentials = await governanceDb.OrganizationProviderCredentials
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.Status == ProviderCredentialStatuses.Active)
            .ToDictionaryAsync(x => x.ProviderId, cancellationToken);
        return providers.Select(provider =>
        {
            credentials.TryGetValue(provider.ProviderId, out var credential);
            var model = provider.Models.Where(x => x.IsEnabled).OrderByDescending(x => x.IsDefault).ThenByDescending(x => x.UpdatedAtUtc).FirstOrDefault();
            return ToProviderResponse(provider.ProviderId, provider.ProviderCode, provider.DisplayName, model?.ModelCode, credential);
        }).ToArray();
    }

    public async Task<OrganizationProviderCredentialRotationResult> RotateProviderCredentialAsync(
        Guid organizationId,
        string providerCode,
        SaveOrganizationProviderCredentialRequest request,
        OrganizationRequestContext context,
        CancellationToken cancellationToken)
    {
        var actor = await RequireMembershipAsync(organizationId, context.UserId, cancellationToken);
        if (!OrganizationMemberRoles.CanManageCredentials(actor.Role))
        {
            throw new AccountApiException(
                StatusCodes.Status403Forbidden,
                "organization_role_denied",
                "Vai trò hiện tại không có quyền quản lý credential AI.");
        }
        var normalizedCode = providerCode.Trim().ToLowerInvariant();
        var provider = await providerDb.Providers
            .AsNoTracking()
            .Include(x => x.Models)
            .SingleOrDefaultAsync(x => x.ProviderCode == normalizedCode, cancellationToken);
        if (provider is null)
        {
            return OrganizationProviderCredentialRotationResult.Rejected(new OrganizationOperationFailure(
                StatusCodes.Status404NotFound,
                "provider_not_found",
                "Không tìm thấy provider AI cần cấu hình."));
        }
        if (!provider.IsEnabled)
        {
            return OrganizationProviderCredentialRotationResult.Rejected(new OrganizationOperationFailure(
                StatusCodes.Status409Conflict,
                "provider_disabled",
                $"Provider {provider.DisplayName} hiện chưa được kích hoạt."));
        }

        var apiKey = ValidateApiKey(request.ApiKey);
        await credentialTester.TestAsync(provider.ProviderCode, provider.BaseUrl, apiKey, cancellationToken);
        await using var transaction = await governanceDb.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var current = await governanceDb.OrganizationProviderCredentials
            .Where(x => x.OrganizationId == organizationId &&
                        x.ProviderId == provider.ProviderId &&
                        x.Status == ProviderCredentialStatuses.Active)
            .ToListAsync(cancellationToken);
        var now = UtcNow();
        foreach (var credential in current)
        {
            credential.Status = ProviderCredentialStatuses.Retiring;
            credential.RetiredAtUtc = now;
            credential.UpdatedAtUtc = now;
        }
        if (current.Count > 0)
        {
            // Release the filtered unique Active slot before inserting the new version.
            await governanceDb.SaveChangesAsync(cancellationToken);
        }
        var version = (await governanceDb.OrganizationProviderCredentials
            .Where(x => x.OrganizationId == organizationId && x.ProviderId == provider.ProviderId)
            .MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
        var added = new OrganizationProviderCredential
        {
            OrganizationProviderCredentialId = Guid.NewGuid(),
            OrganizationId = organizationId,
            ProviderId = provider.ProviderId,
            Version = version,
            Name = string.IsNullOrWhiteSpace(request.Name) ? $"{provider.DisplayName} v{version}" : Required(request.Name, 100, "Tên credential"),
            EncryptedPayload = credentialProtector.Protect(apiKey),
            SecretHint = Hint(apiKey),
            Status = ProviderCredentialStatuses.Active,
            CreatedByUserId = context.UserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        governanceDb.OrganizationProviderCredentials.Add(added);
        AddAudit(organizationId, context, "OrganizationProviderCredentialRotated", new { provider.ProviderCode, version, added.SecretHint });
        await governanceDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var model = provider.Models.Where(x => x.IsEnabled).OrderByDescending(x => x.IsDefault).ThenByDescending(x => x.UpdatedAtUtc).FirstOrDefault();
        return OrganizationProviderCredentialRotationResult.Success(
            ToProviderResponse(provider.ProviderId, provider.ProviderCode, provider.DisplayName, model?.ModelCode, added));
    }

    public async Task<OrganizationVideoPolicyResponse?> GetVideoPolicyAsync(
        Guid organizationId,
        string userId,
        string scope,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(organizationId, userId, cancellationToken);
        scope = ValidateVideoPolicyScope(scope);
        var policy = await governanceDb.OrganizationVideoPolicies
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.PolicyScope == scope,
                cancellationToken);
        if (policy is null)
        {
            return null;
        }
        return await ToVideoPolicyResponseAsync(policy, cancellationToken);
    }

    public async Task<OrganizationVideoPolicyResponse> UpdateVideoPolicyAsync(
        Guid organizationId,
        UpdateOrganizationVideoPolicyRequest request,
        OrganizationRequestContext context,
        CancellationToken cancellationToken)
    {
        var actor = await RequireMembershipAsync(organizationId, context.UserId, cancellationToken);
        if (!OrganizationMemberRoles.CanManageCredentials(actor.Role))
        {
            throw new AccountApiException(
                StatusCodes.Status403Forbidden,
                "organization_role_denied",
                "Vai trò hiện tại không có quyền quản lý policy video.");
        }
        var scope = ValidateVideoPolicyScope(request.Scope);
        var model = await providerDb.ProviderModels
            .AsNoTracking()
            .Include(x => x.Provider)
            .SingleOrDefaultAsync(
                x => x.ProviderModelId == request.ProviderModelId &&
                     x.Modality == "Video" &&
                     x.IsEnabled &&
                     x.Provider.IsEnabled,
                cancellationToken)
            ?? throw new AccountApiException(
                StatusCodes.Status404NotFound,
                "video_model_not_enabled",
                "Không tìm thấy model video đang được Global Admin cho phép.");
        var resolution = request.Resolution.Trim();
        var capabilities = VideoModelCapabilities.Parse(model.CapabilitiesJson, model.Provider.ProviderCode);
        ProjectVideoPolicyResolver.ValidateVariant(resolution, request.NativeAudio, capabilities);
        if (model.Provider.ProviderCode == ProviderCodes.Fal &&
            scope != OrganizationVideoPolicyScopes.LongForm)
        {
            throw new AccountApiException(
                StatusCodes.Status422UnprocessableEntity,
                "fal_long_form_only",
                "Fal/Veo chỉ được cấu hình cho policy Video dài.");
        }

        var credentialReady = await governanceDb.OrganizationProviderCredentials
            .AsNoTracking()
            .AnyAsync(
                x => x.OrganizationId == organizationId &&
                     x.ProviderId == model.ProviderId &&
                     x.Status == ProviderCredentialStatuses.Active,
                cancellationToken);
        if (!credentialReady)
        {
            throw new AccountApiException(
                StatusCodes.Status409Conflict,
                "provider_credential_not_configured",
                "Hãy cấu hình và kiểm tra credential của provider trước khi chọn policy video.");
        }

        var now = UtcNow();
        var policy = await governanceDb.OrganizationVideoPolicies
            .SingleOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.PolicyScope == scope,
                cancellationToken);
        if (policy is null)
        {
            policy = new OrganizationVideoPolicy
            {
                OrganizationId = organizationId,
                PolicyScope = scope,
                PolicyVersion = 1,
                CreatedAtUtc = now,
                RowVersion = new byte[8]
            };
            governanceDb.OrganizationVideoPolicies.Add(policy);
        }
        else if (policy.ProviderId != model.ProviderId ||
                 policy.ProviderModelId != model.ProviderModelId ||
                 !string.Equals(policy.Resolution, resolution, StringComparison.OrdinalIgnoreCase) ||
                 policy.NativeAudio != request.NativeAudio ||
                 !policy.IsActive)
        {
            policy.PolicyVersion++;
        }
        policy.ProviderId = model.ProviderId;
        policy.ProviderModelId = model.ProviderModelId;
        policy.Resolution = resolution;
        policy.NativeAudio = request.NativeAudio;
        policy.IsActive = true;
        policy.UpdatedByUserId = context.UserId;
        policy.UpdatedAtUtc = now;
        AddAudit(
            organizationId,
            context,
            "OrganizationVideoPolicyUpdated",
            new
            {
                model.Provider.ProviderCode,
                model.ModelCode,
                policy.PolicyScope,
                policy.PolicyVersion,
                policy.Resolution,
                policy.NativeAudio
            });
        await governanceDb.SaveChangesAsync(cancellationToken);
        return new OrganizationVideoPolicyResponse(
            organizationId,
            model.ProviderId,
            model.Provider.ProviderCode,
            model.Provider.DisplayName,
            model.ProviderModelId,
            model.ModelCode,
            model.DisplayName,
            policy.PolicyVersion,
            policy.Resolution,
            policy.NativeAudio,
            policy.IsActive,
            policy.UpdatedAtUtc,
            policy.PolicyScope);
    }

    public async Task<OrganizationUsageResponse> GetUsageAsync(
        Guid organizationId,
        string userId,
        int take,
        CancellationToken cancellationToken)
    {
        await RequireManagementRoleAsync(organizationId, userId, true, cancellationToken);
        take = Math.Clamp(take, 1, 500);
        var budget = await budgetService.GetSnapshotAsync(organizationId, cancellationToken);
        var itemRows = await governanceDb.AiUsageLedger
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId &&
                        x.OccurredAtUtc >= budget.StartsAtUtc &&
                        x.OccurredAtUtc < budget.EndsAtUtc)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(take)
            .Select(x => new UsageLedgerProjection(
                x.AiUsageLedgerEntryId,
                x.UserId,
                x.ProjectId,
                x.ProviderRequestId,
                x.ProviderCode,
                x.ModelCode,
                x.EntryKind,
                x.Amount,
                x.CurrencyCode,
                x.UsageJson,
                x.OccurredAtUtc))
            .ToListAsync(cancellationToken);
        var actualRows = await governanceDb.AiUsageLedger
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId &&
                        x.EntryKind == UsageLedgerEntryKinds.Actual &&
                        x.OccurredAtUtc >= budget.StartsAtUtc &&
                        x.OccurredAtUtc < budget.EndsAtUtc)
            .Select(x => new UsageAggregateProjection(
                x.UserId,
                x.ProviderCode,
                x.ModelCode,
                x.Amount,
                x.UsageJson))
            .ToListAsync(cancellationToken);
        var items = itemRows.Select(row =>
        {
            var metrics = OrganizationUsageMetricsParser.Parse(row.UsageJson);
            return new OrganizationUsageItemResponse(
                row.LedgerEntryId,
                row.UserId,
                row.ProjectId,
                row.ProviderRequestId,
                row.ProviderCode,
                row.ModelCode,
                row.EntryKind,
                row.Amount,
                row.CurrencyCode,
                row.OccurredAtUtc,
                metrics.InputTokens,
                metrics.OutputTokens,
                metrics.VideoSeconds);
        }).ToArray();
        var parsedActualRows = actualRows
            .Select(row => new ParsedUsageAggregateProjection(
                row.UserId,
                row.ProviderCode,
                row.ModelCode,
                row.Amount,
                OrganizationUsageMetricsParser.Parse(row.UsageJson)))
            .ToArray();
        var totalMetrics = OrganizationUsageMetricsParser.Sum(parsedActualRows.Select(x => x.Metrics));
        var groups = parsedActualRows
            .GroupBy(x => new { x.ProviderCode, x.ModelCode, x.UserId })
            .Select(group =>
            {
                var metrics = OrganizationUsageMetricsParser.Sum(group.Select(x => x.Metrics));
                return new OrganizationUsageGroupResponse(
                    group.Key.ProviderCode,
                    group.Key.ModelCode,
                    group.Key.UserId,
                    group.Sum(x => x.Amount),
                    metrics.InputTokens,
                    metrics.OutputTokens,
                    metrics.VideoSeconds);
            })
            .OrderByDescending(x => x.ActualCost)
            .ThenBy(x => x.ProviderCode)
            .ThenBy(x => x.ModelCode)
            .ThenBy(x => x.UserId)
            .ToArray();
        return new OrganizationUsageResponse(
            organizationId,
            budget.StartsAtUtc,
            budget.EndsAtUtc,
            budget.HardLimit,
            budget.ReservedCost,
            budget.ActualCost,
            budget.RemainingBudget,
            budget.CurrencyCode,
            items,
            totalMetrics.InputTokens,
            totalMetrics.OutputTokens,
            totalMetrics.VideoSeconds,
            groups);
    }

    public async Task<IReadOnlyList<OrganizationAuditItemResponse>> GetAuditAsync(
        Guid organizationId,
        string userId,
        int take,
        CancellationToken cancellationToken)
    {
        await RequireManagementRoleAsync(organizationId, userId, false, cancellationToken);
        take = Math.Clamp(take, 1, 200);
        var rows = await governanceDb.OrganizationAuditLogs
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.OrganizationAuditLogId)
            .Take(take)
            .Select(x => new AuditProjection(
                x.OrganizationAuditLogId,
                x.ActorUserId,
                x.EventType,
                x.DataJson,
                x.CorrelationId,
                x.OccurredAtUtc))
            .ToListAsync(cancellationToken);
        var actorIds = rows
            .Where(x => !string.IsNullOrWhiteSpace(x.ActorUserId))
            .Select(x => x.ActorUserId!)
            .Distinct()
            .ToArray();
        var actors = actorIds.Length == 0
            ? []
            : await accountDb.Users
                .AsNoTracking()
                .Where(x => actorIds.Contains(x.Id))
                .Select(x => new AuditActorProjection(x.Id, x.Email, x.DisplayName))
                .ToDictionaryAsync(x => x.UserId, cancellationToken);

        return rows.Select(row =>
        {
            AuditActorProjection? actor = null;
            if (row.ActorUserId is not null)
            {
                actors.TryGetValue(row.ActorUserId, out actor);
            }
            return new OrganizationAuditItemResponse(
                row.AuditLogId,
                row.ActorUserId,
                actor?.Email,
                actor?.DisplayName,
                row.EventType,
                OrganizationAuditDataSanitizer.Sanitize(row.DataJson),
                row.CorrelationId,
                row.OccurredAtUtc);
        }).ToArray();
    }

    private async Task<OrganizationMember> RequireMembershipAsync(Guid organizationId, string userId, CancellationToken cancellationToken) =>
        await governanceDb.OrganizationMembers
            .Include(x => x.Organization)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId &&
                                       x.UserId == userId &&
                                       x.Status == OrganizationMemberStatuses.Active &&
                                       x.Organization.Status == OrganizationStatuses.Active,
                cancellationToken)
        ?? throw new AccountApiException(StatusCodes.Status403Forbidden, "organization_access_denied", "Bạn không có quyền truy cập tổ chức này.");

    private async Task<OrganizationMember> RequireManagementRoleAsync(
        Guid organizationId,
        string userId,
        bool billing,
        CancellationToken cancellationToken)
    {
        var member = await RequireMembershipAsync(organizationId, userId, cancellationToken);
        var allowed = billing
            ? OrganizationMemberRoles.CanManageBilling(member.Role)
            : OrganizationMemberRoles.CanManageMembers(member.Role);
        if (!allowed)
        {
            throw new AccountApiException(StatusCodes.Status403Forbidden, "organization_role_denied", "Vai trò hiện tại không có quyền thực hiện thao tác này.");
        }
        return member;
    }

    private void AddAudit(Guid organizationId, OrganizationRequestContext context, string eventType, object data) =>
        governanceDb.OrganizationAuditLogs.Add(new OrganizationAuditLog
        {
            OrganizationId = organizationId,
            ActorUserId = context.UserId,
            EventType = eventType,
            DataJson = JsonSerializer.Serialize(data, JsonOptions),
            IpAddress = context.IpAddress,
            UserAgent = context.UserAgent,
            CorrelationId = context.CorrelationId,
            OccurredAtUtc = UtcNow()
        });

    private static OrganizationSummaryResponse ToSummary(
        Organization organization,
        string role,
        BudgetSnapshot budget,
        int memberCount = 0,
        int activeMemberCount = 0,
        IReadOnlyList<OrganizationAiReadinessResponse>? readiness = null) =>
        new(
            organization.OrganizationId,
            organization.Code,
            organization.Name,
            role,
            organization.Status,
            budget.HardLimit,
            budget.ReservedCost,
            budget.ActualCost,
            budget.RemainingBudget,
            budget.CurrencyCode,
            budget.StartsAtUtc,
            budget.EndsAtUtc,
            memberCount,
            activeMemberCount,
            readiness);

    private static OrganizationProviderResponse ToProviderResponse(
        Guid providerId,
        string providerCode,
        string displayName,
        string? modelCode,
        OrganizationProviderCredential? credential) =>
        new(
            providerId,
            providerCode,
            displayName,
            modelCode,
            credential is not null,
            credential?.OrganizationProviderCredentialId,
            credential?.Version,
            credential?.SecretHint,
            credential?.Status,
            credential?.UpdatedAtUtc);

    private async Task<OrganizationVideoPolicyResponse> ToVideoPolicyResponseAsync(
        OrganizationVideoPolicy policy,
        CancellationToken cancellationToken)
    {
        var model = await providerDb.ProviderModels
            .AsNoTracking()
            .Include(x => x.Provider)
            .SingleOrDefaultAsync(x => x.ProviderModelId == policy.ProviderModelId, cancellationToken)
            ?? throw new AccountApiException(
                StatusCodes.Status503ServiceUnavailable,
                "video_snapshot_unavailable",
                "Model video trong policy không còn trong catalog.");
        return new OrganizationVideoPolicyResponse(
            policy.OrganizationId,
            model.ProviderId,
            model.Provider.ProviderCode,
            model.Provider.DisplayName,
            model.ProviderModelId,
            model.ModelCode,
            model.DisplayName,
            policy.PolicyVersion,
            policy.Resolution,
            policy.NativeAudio,
            policy.IsActive,
            policy.UpdatedAtUtc,
            policy.PolicyScope);
    }

    private static string ValidateVideoPolicyScope(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? OrganizationVideoPolicyScopes.Default
            : value.Trim();
        return normalized switch
        {
            OrganizationVideoPolicyScopes.Default => normalized,
            OrganizationVideoPolicyScopes.LongForm => normalized,
            _ => throw new AccountApiException(
                StatusCodes.Status422UnprocessableEntity,
                "video_policy_scope_invalid",
                "Pháº¡m vi policy video khÃ´ng há»£p lá»‡.")
        };
    }

    private static string ValidateRole(string value)
    {
        var normalized = value.Trim();
        return normalized switch
        {
            OrganizationMemberRoles.Owner => normalized,
            OrganizationMemberRoles.OrganizationAdmin => normalized,
            OrganizationMemberRoles.BillingManager => normalized,
            OrganizationMemberRoles.Member => normalized,
            OrganizationMemberRoles.Viewer => normalized,
            _ => throw new ArgumentException("Vai trò tổ chức không hợp lệ.")
        };
    }

    private static string ValidateMemberStatus(string value)
    {
        var normalized = value.Trim();
        return normalized switch
        {
            OrganizationMemberStatuses.Active => normalized,
            OrganizationMemberStatuses.Suspended => normalized,
            OrganizationMemberStatuses.Removed => normalized,
            _ => throw new ArgumentException("Trạng thái thành viên không hợp lệ.")
        };
    }

    private static void EnsureCanAssignRole(string actorRole, string requestedRole)
    {
        if (requestedRole == OrganizationMemberRoles.Owner && actorRole != OrganizationMemberRoles.Owner)
        {
            throw new AccountApiException(StatusCodes.Status403Forbidden, "owner_role_required", "Chỉ Owner mới có thể cấp vai trò Owner.");
        }
    }

    private static void ValidateBudget(decimal limit, string currency)
    {
        if (limit is < 0 or > 100_000_000m)
        {
            throw new ArgumentException("Ngân sách tháng không hợp lệ.");
        }
        if (!currency.Trim().Equals("USD", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Phiên bản hiện tại chỉ hỗ trợ ngân sách USD.");
        }
    }

    private static void ValidateMemberLimit(decimal? limit)
    {
        if (limit is < 0 or > 100_000_000m)
        {
            throw new ArgumentException("Hạn mức thành viên không hợp lệ.");
        }
    }

    private static string Required(string? value, int maximumLength, string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximumLength)
        {
            throw new ArgumentException($"{field} không hợp lệ.");
        }
        return normalized;
    }

    private static string ValidateApiKey(string value)
    {
        var normalized = value.Trim().Trim('"');
        if (normalized.Length is < 8 or > 10_000 || normalized.Contains('\r') || normalized.Contains('\n'))
        {
            throw new ArgumentException("API key không hợp lệ.");
        }
        return normalized;
    }

    private static string Hint(string value) => value.Length <= 4 ? "••••" : $"••••{value[^4..]}";

    private static string NormalizeCode(string? requestedCode, string name)
    {
        var source = string.IsNullOrWhiteSpace(requestedCode) ? name : requestedCode;
        var code = InvalidCodeCharacters().Replace(source.Trim().ToLowerInvariant(), "-").Trim('-');
        while (code.Contains("--", StringComparison.Ordinal))
        {
            code = code.Replace("--", "-", StringComparison.Ordinal);
        }
        if (code.Length > 80)
        {
            code = code[..80].TrimEnd('-');
        }
        return string.IsNullOrWhiteSpace(code) ? $"org-{Guid.NewGuid():N}" : code;
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private sealed record UsageLedgerProjection(
        Guid LedgerEntryId,
        string UserId,
        Guid ProjectId,
        Guid? ProviderRequestId,
        string ProviderCode,
        string ModelCode,
        string EntryKind,
        decimal Amount,
        string CurrencyCode,
        string? UsageJson,
        DateTime OccurredAtUtc);

    private sealed record UsageAggregateProjection(
        string UserId,
        string ProviderCode,
        string ModelCode,
        decimal Amount,
        string? UsageJson);

    private sealed record ParsedUsageAggregateProjection(
        string UserId,
        string ProviderCode,
        string ModelCode,
        decimal Amount,
        OrganizationUsageMetrics Metrics);

    private sealed record AuditProjection(
        long AuditLogId,
        string? ActorUserId,
        string EventType,
        string? DataJson,
        string? CorrelationId,
        DateTime OccurredAtUtc);

    private sealed record AuditActorProjection(
        string UserId,
        string? Email,
        string? DisplayName);

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidCodeCharacters();
}
