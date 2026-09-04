using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_SERVER.Accounts;

public sealed partial class AdminLicenseService(AccountDbContext dbContext, TimeProvider timeProvider)
    : IAdminLicenseService
{
    public async Task<AdminLicenseOverviewResponse> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var onlineCutoff = now.Subtract(LicensePolicy.OnlineWindow);
        return new AdminLicenseOverviewResponse(
            await dbContext.Users.CountAsync(x => x.DeletedAtUtc == null, cancellationToken),
            await dbContext.UserLicenses.CountAsync(x =>
                (x.Status == "Active" || x.Status == "Trial") &&
                x.StartsAtUtc <= now &&
                (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now) &&
                x.LicensePlan.IsActive, cancellationToken),
            await dbContext.UserSessions.CountAsync(x =>
                x.Status == SessionStatuses.Active &&
                x.AbsoluteExpiresAtUtc > now &&
                x.LastSeenAtUtc >= onlineCutoff, cancellationToken),
            await dbContext.UserLicenses.CountAsync(x =>
                (x.Status == "Active" || x.Status == "Trial") &&
                x.ExpiresAtUtc > now &&
                x.ExpiresAtUtc <= now.AddDays(7) &&
                x.LicensePlan.IsActive, cancellationToken),
            await dbContext.UserLicenses.CountAsync(x =>
                x.Status == "Suspended" || x.Status == "Revoked", cancellationToken));
    }

    public async Task<IReadOnlyList<AdminLicensePlanResponse>> GetPlansAsync(CancellationToken cancellationToken) =>
        (await dbContext.LicensePlans
            .AsNoTracking()
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.DefaultDurationDays)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken))
        .Select(ToPlanResponse)
        .ToArray();

    public async Task<IReadOnlyList<AdminLicensePaymentResponse>> GetPaymentsAsync(
        string? search,
        string? status,
        int? take,
        CancellationToken cancellationToken)
    {
        var limit = take ?? 100;
        if (limit is < 1 or > 200)
        {
            throw Validation("invalid_payment_page_size", "Số giao dịch mỗi lần phải từ 1 đến 200.");
        }

        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (term?.Length > 100)
        {
            throw Validation("invalid_payment_search", "Từ khóa tra cứu không được vượt quá 100 ký tự.");
        }

        string? normalizedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            normalizedStatus = PaymentStatuses.FirstOrDefault(x =>
                x.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
            if (normalizedStatus is null)
            {
                throw Validation("invalid_payment_status", "Trạng thái giao dịch không hợp lệ.");
            }
        }

        IQueryable<LicensePayment> query = dbContext.LicensePayments
            .AsNoTracking()
            .Include(x => x.User);
        if (term is not null)
        {
            var code = term.ToUpperInvariant();
            var isProviderTransactionId = long.TryParse(
                term,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var providerTransactionId);
            query = query.Where(x =>
                x.OrderCode == code ||
                x.TransferCode == code ||
                (isProviderTransactionId && x.ProviderTransactionId == providerTransactionId));
        }
        if (normalizedStatus is not null)
        {
            query = query.Where(x => x.Status == normalizedStatus);
        }

        var payments = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.LicensePaymentId)
            .Take(limit)
            .ToListAsync(cancellationToken);
        var paymentIds = payments.Select(x => x.LicensePaymentId).ToArray();
        var assignments = await (
                from assignment in dbContext.OrganizationSeatAssignments.AsNoTracking()
                join organization in dbContext.Organizations.AsNoTracking()
                    on assignment.OrganizationId equals organization.OrganizationId
                where paymentIds.Contains(assignment.LicensePaymentId)
                select new
                {
                    assignment.LicensePaymentId,
                    assignment.OrganizationId,
                    OrganizationName = organization.Name,
                    assignment.Status,
                    assignment.FailureCode
                })
            .ToDictionaryAsync(x => x.LicensePaymentId, cancellationToken);
        return payments
            .Select(x =>
            {
                assignments.TryGetValue(x.LicensePaymentId, out var assignment);
                return new AdminLicensePaymentResponse(
                x.LicensePaymentId,
                x.UserId,
                x.User.Email ?? string.Empty,
                x.OrderCode,
                x.TransferCode,
                x.ProviderTransactionId,
                x.LicensePlanId,
                x.PlanCodeSnapshot,
                x.PlanNameSnapshot,
                x.PriceSnapshotVnd,
                x.DurationSnapshotDays,
                x.Status,
                x.CreatedAtUtc,
                x.ExpiresAtUtc,
                x.PaidAtUtc,
                x.FulfilledAtUtc,
                x.FulfilledUserLicenseId,
                assignment?.OrganizationId,
                assignment?.OrganizationName,
                assignment?.Status,
                x.FailureCode ?? assignment?.FailureCode);
            })
            .ToArray();
    }

    public async Task<AdminLicensePlanResponse> CreatePlanAsync(
        SaveLicensePlanRequest request,
        string adminUserId,
        CancellationToken cancellationToken)
    {
        var normalized = ValidatePlan(request);
        if (await dbContext.LicensePlans.AnyAsync(x => x.PlanCode == normalized.PlanCode, cancellationToken))
        {
            throw Conflict("plan_code_exists", "Mã gói đã tồn tại.");
        }

        var now = UtcNow();
        var plan = new LicensePlan
        {
            LicensePlanId = Guid.NewGuid(),
            PlanCode = normalized.PlanCode,
            Name = normalized.Name,
            Description = normalized.Description,
            MaxActivatedDevices = normalized.MaxActivatedDevices,
            OfflineGraceHours = normalized.OfflineGraceHours,
            DefaultDurationDays = normalized.DefaultDurationDays,
            FeatureFlagsJson = normalized.FeatureFlagsJson,
            SalePriceVnd = normalized.SalePriceVnd,
            IsPublic = normalized.IsPublic,
            DisplayOrder = normalized.DisplayOrder,
            MarketingFeaturesJson = normalized.MarketingFeaturesJson,
            IsActive = normalized.IsActive,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.LicensePlans.Add(plan);
        AddAudit(adminUserId, "LicensePlanCreated", new { plan.LicensePlanId, plan.PlanCode }, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToPlanResponse(plan);
    }

    public async Task<AdminLicensePlanResponse> UpdatePlanAsync(
        Guid planId,
        SaveLicensePlanRequest request,
        string adminUserId,
        CancellationToken cancellationToken)
    {
        var normalized = ValidatePlan(request);
        var plan = await dbContext.LicensePlans.SingleOrDefaultAsync(x => x.LicensePlanId == planId, cancellationToken)
            ?? throw NotFound("plan_not_found", "Không tìm thấy gói dịch vụ.");
        if (await dbContext.LicensePlans.AnyAsync(
                x => x.LicensePlanId != planId && x.PlanCode == normalized.PlanCode,
                cancellationToken))
        {
            throw Conflict("plan_code_exists", "Mã gói đã tồn tại.");
        }

        plan.PlanCode = normalized.PlanCode;
        plan.Name = normalized.Name;
        plan.Description = normalized.Description;
        plan.MaxActivatedDevices = normalized.MaxActivatedDevices;
        plan.OfflineGraceHours = normalized.OfflineGraceHours;
        plan.DefaultDurationDays = normalized.DefaultDurationDays;
        plan.FeatureFlagsJson = normalized.FeatureFlagsJson;
        plan.SalePriceVnd = normalized.SalePriceVnd;
        plan.IsPublic = normalized.IsPublic;
        plan.DisplayOrder = normalized.DisplayOrder;
        plan.MarketingFeaturesJson = normalized.MarketingFeaturesJson;
        plan.IsActive = normalized.IsActive;
        plan.UpdatedAtUtc = UtcNow();
        AddAudit(adminUserId, "LicensePlanUpdated", new { plan.LicensePlanId, plan.PlanCode }, plan.UpdatedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToPlanResponse(plan);
    }

    public async Task<IReadOnlyList<AdminUserSummaryResponse>> GetUsersAsync(
        string? search,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Users.AsNoTracking().Where(x => x.DeletedAtUtc == null);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                (x.Email != null && x.Email.Contains(term)) ||
                (x.DisplayName != null && x.DisplayName.Contains(term)));
        }

        var users = await query
            .OrderByDescending(x => x.LastLoginAtUtc)
            .ThenBy(x => x.Email)
            .Take(200)
            .Select(x => new UserRow(x.Id, x.Email ?? string.Empty, x.DisplayName, x.AccountStatus, x.LastLoginAtUtc))
            .ToListAsync(cancellationToken);
        return await BuildUserSummariesAsync(users, cancellationToken);
    }

    public async Task<AdminUserDetailResponse> GetUserAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .Where(x => x.Id == userId && x.DeletedAtUtc == null)
            .Select(x => new UserRow(x.Id, x.Email ?? string.Empty, x.DisplayName, x.AccountStatus, x.LastLoginAtUtc))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound("user_not_found", "Không tìm thấy người dùng.");
        var summary = (await BuildUserSummariesAsync([user], cancellationToken)).Single();
        var licenses = await LoadLicenseResponsesAsync([userId], cancellationToken);
        var devices = await dbContext.RegisteredDevices
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.LastSeenAtUtc)
            .Select(x => new RegisteredDeviceResponse(
                x.DeviceId,
                x.DeviceName,
                x.OperatingSystem,
                x.ApplicationVersion,
                x.IsTrusted,
                x.IsRevoked,
                false,
                x.FirstSeenAtUtc,
                x.LastSeenAtUtc))
            .ToListAsync(cancellationToken);
        var sessions = await dbContext.UserSessions
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.LastSeenAtUtc)
            .Take(100)
            .Select(x => new AdminSessionResponse(
                x.SessionId,
                x.DeviceId,
                x.Device != null ? x.Device.DeviceName : "Unknown device",
                x.Status,
                x.StartedAtUtc,
                x.LastSeenAtUtc,
                x.AbsoluteExpiresAtUtc,
                x.ApplicationVersion,
                x.IpAddress))
            .ToListAsync(cancellationToken);
        return new AdminUserDetailResponse(summary, licenses.Select(x => x.Response).ToArray(), devices, sessions);
    }

    public async Task<AdminUserLicenseResponse> GrantLicenseAsync(
        string userId,
        GrantUserLicenseRequest request,
        string adminUserId,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        if (!await dbContext.Users.AnyAsync(x => x.Id == userId && x.DeletedAtUtc == null, cancellationToken))
        {
            throw NotFound("user_not_found", "Không tìm thấy người dùng.");
        }

        var plan = await dbContext.LicensePlans.SingleOrDefaultAsync(
            x => x.LicensePlanId == request.LicensePlanId && x.IsActive,
            cancellationToken) ?? throw NotFound("plan_not_found", "Không tìm thấy gói đang hoạt động.");
        var startsAt = request.StartsAtUtc ?? now;
        var durationDays = request.DurationDays ?? plan.DefaultDurationDays;
        if (durationDays is <= 0 or > 3650)
        {
            throw Validation("invalid_duration", "Thời hạn gói phải từ 1 đến 3650 ngày.");
        }

        var expiresAt = request.ExpiresAtUtc ?? (durationDays.HasValue ? startsAt.AddDays(durationDays.Value) : null);
        if (expiresAt is not null && expiresAt <= startsAt)
        {
            throw Validation("invalid_expiry", "Ngày hết hạn phải sau ngày bắt đầu.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var oldLicenses = await dbContext.UserLicenses
            .Include(x => x.Activations)
            .Where(x => x.UserId == userId && (x.Status == "Active" || x.Status == "Trial"))
            .ToListAsync(cancellationToken);
        foreach (var old in oldLicenses)
        {
            RevokeLicense(old, "Replaced by administrator", now);
        }

        await RevokeUserSessionsAsync(userId, "License replaced by administrator", now, cancellationToken);
        var license = new UserLicense
        {
            UserLicenseId = Guid.NewGuid(),
            UserId = userId,
            LicensePlanId = plan.LicensePlanId,
            Status = request.IsTrial ? "Trial" : "Active",
            StartsAtUtc = startsAt,
            ExpiresAtUtc = expiresAt,
            EntitlementSnapshotJson = plan.FeatureFlagsJson,
            GrantedByUserId = adminUserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            LicensePlan = plan
        };
        dbContext.UserLicenses.Add(license);
        AddAudit(adminUserId, "UserLicenseGranted", new { userId, license.UserLicenseId, plan.PlanCode, expiresAt }, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToLicenseResponse(license);
    }

    public async Task<AdminUserLicenseResponse> ExtendLicenseAsync(
        Guid licenseId,
        ExtendUserLicenseRequest request,
        string adminUserId,
        CancellationToken cancellationToken)
    {
        if (request.DurationDays is < 1 or > 3650)
        {
            throw Validation("invalid_duration", "Số ngày gia hạn phải từ 1 đến 3650.");
        }

        var license = await dbContext.UserLicenses
            .Include(x => x.LicensePlan)
            .Include(x => x.Activations)
            .SingleOrDefaultAsync(x => x.UserLicenseId == licenseId, cancellationToken)
            ?? throw NotFound("license_not_found", "Không tìm thấy license.");
        if (license.Status is "Revoked" or "Expired")
        {
            throw Conflict("license_not_extendable", "License đã thu hồi hoặc hết hạn không thể gia hạn.");
        }

        var now = UtcNow();
        var baseDate = license.ExpiresAtUtc is { } expiry && expiry > now ? expiry : now;
        license.ExpiresAtUtc = baseDate.AddDays(request.DurationDays);
        license.Status = "Active";
        license.RevokedAtUtc = null;
        license.RevokedReason = null;
        license.UpdatedAtUtc = now;
        AddAudit(adminUserId, "UserLicenseExtended", new { license.UserLicenseId, request.DurationDays, license.ExpiresAtUtc }, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToLicenseResponse(license);
    }

    public async Task<AdminUserLicenseResponse> ChangeLicenseStatusAsync(
        Guid licenseId,
        ChangeUserLicenseStatusRequest request,
        string adminUserId,
        CancellationToken cancellationToken)
    {
        var status = request.Status.Trim();
        if (status is not ("Active" or "Suspended" or "Revoked"))
        {
            throw Validation("invalid_license_status", "Trạng thái license không hợp lệ.");
        }

        var license = await dbContext.UserLicenses
            .Include(x => x.LicensePlan)
            .Include(x => x.Activations)
            .SingleOrDefaultAsync(x => x.UserLicenseId == licenseId, cancellationToken)
            ?? throw NotFound("license_not_found", "Không tìm thấy license.");
        var now = UtcNow();
        if (status == "Active" && license.ExpiresAtUtc is { } expiry && expiry <= now)
        {
            throw Conflict("license_expired", "Hãy gia hạn license trước khi kích hoạt lại.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (status == "Revoked")
        {
            RevokeLicense(license, NormalizeReason(request.Reason, "Revoked by administrator"), now);
        }
        else
        {
            license.Status = status;
            license.UpdatedAtUtc = now;
            license.RevokedAtUtc = null;
            license.RevokedReason = status == "Suspended"
                ? NormalizeReason(request.Reason, "Suspended by administrator")
                : null;
            if (status == "Suspended")
            {
                foreach (var activation in license.Activations.Where(x => x.Status == "Active"))
                {
                    activation.Status = "Revoked";
                    activation.RevokedAtUtc = now;
                    activation.RevokedReason = license.RevokedReason;
                }
            }
        }

        if (status is "Suspended" or "Revoked")
        {
            await RevokeUserSessionsAsync(license.UserId, $"License {status.ToLowerInvariant()}", now, cancellationToken);
        }

        AddAudit(adminUserId, "UserLicenseStatusChanged", new { license.UserLicenseId, status, request.Reason }, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToLicenseResponse(license);
    }

    public async Task RevokeDeviceAsync(Guid deviceId, string adminUserId, CancellationToken cancellationToken)
    {
        var device = await dbContext.RegisteredDevices.SingleOrDefaultAsync(x => x.DeviceId == deviceId, cancellationToken)
            ?? throw NotFound("device_not_found", "Không tìm thấy thiết bị.");
        var now = UtcNow();
        device.IsRevoked = true;
        device.RevokedAtUtc = now;
        device.RevokedReason = "Revoked by administrator";
        var activations = await dbContext.LicenseActivations
            .Where(x => x.DeviceId == deviceId && x.Status == "Active")
            .ToListAsync(cancellationToken);
        foreach (var activation in activations)
        {
            activation.Status = "Revoked";
            activation.RevokedAtUtc = now;
            activation.RevokedReason = device.RevokedReason;
        }

        await RevokeDeviceSessionsAsync(deviceId, device.RevokedReason, now, cancellationToken);
        AddAudit(adminUserId, "AdminDeviceRevoked", new { deviceId, device.UserId }, now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeSessionAsync(Guid sessionId, string adminUserId, CancellationToken cancellationToken)
    {
        var session = await dbContext.UserSessions.SingleOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken)
            ?? throw NotFound("session_not_found", "Không tìm thấy phiên hoạt động.");
        var now = UtcNow();
        session.Status = SessionStatuses.Revoked;
        session.RevokedAtUtc = now;
        session.RevokedReason = "Revoked by administrator";
        await dbContext.RefreshTokens
            .Where(x => x.SessionId == sessionId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.RevokedReason, session.RevokedReason), cancellationToken);
        AddAudit(adminUserId, "AdminSessionRevoked", new { sessionId, session.UserId }, now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<AdminUserSummaryResponse>> BuildUserSummariesAsync(
        IReadOnlyList<UserRow> users,
        CancellationToken cancellationToken)
    {
        var ids = users.Select(x => x.UserId).ToArray();
        var deviceCounts = await dbContext.RegisteredDevices
            .AsNoTracking()
            .Where(x => ids.Contains(x.UserId))
            .GroupBy(x => x.UserId)
            .Select(x => new { UserId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);
        var now = UtcNow();
        var sessionCounts = await dbContext.UserSessions
            .AsNoTracking()
            .Where(x => ids.Contains(x.UserId) && x.Status == SessionStatuses.Active && x.AbsoluteExpiresAtUtc > now)
            .GroupBy(x => x.UserId)
            .Select(x => new { UserId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);
        var licenses = await LoadLicenseResponsesAsync(ids, cancellationToken);
        var currentByUser = licenses
            .Where(x => (x.Status == "Active" || x.Status == "Trial") &&
                        x.StartsAtUtc <= now &&
                        (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now) &&
                        x.PlanIsActive)
            .GroupBy(x => x.UserId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.ExpiresAtUtc).First().Response);

        return users.Select(user => new AdminUserSummaryResponse(
            user.UserId,
            user.Email,
            user.DisplayName,
            user.AccountStatus,
            user.LastLoginAtUtc,
            deviceCounts.GetValueOrDefault(user.UserId),
            sessionCounts.GetValueOrDefault(user.UserId),
            currentByUser.GetValueOrDefault(user.UserId))).ToArray();
    }

    private async Task<IReadOnlyList<LicenseRow>> LoadLicenseResponsesAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken)
    {
        var licenses = await dbContext.UserLicenses
            .AsNoTracking()
            .Include(x => x.LicensePlan)
            .Include(x => x.Activations)
            .Where(x => userIds.Contains(x.UserId))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return licenses.Select(x => new LicenseRow(x.UserId, x.LicensePlan.IsActive, ToLicenseResponse(x))).ToArray();
    }

    private static AdminLicensePlanResponse ToPlanResponse(LicensePlan plan) => new(
        plan.LicensePlanId,
        plan.PlanCode,
        plan.Name,
        plan.Description,
        plan.MaxActivatedDevices,
        LicensePolicy.GetMaxConcurrentSessions(plan.FeatureFlagsJson),
        plan.OfflineGraceHours,
        plan.DefaultDurationDays,
        plan.FeatureFlagsJson,
        plan.IsActive,
        plan.CreatedAtUtc,
        plan.UpdatedAtUtc,
        plan.SalePriceVnd,
        plan.IsPublic,
        plan.DisplayOrder,
        plan.MarketingFeaturesJson);

    private static AdminUserLicenseResponse ToLicenseResponse(UserLicense license) => new(
        license.UserLicenseId,
        license.LicensePlanId,
        license.LicensePlan.PlanCode,
        license.LicensePlan.Name,
        license.Status,
        license.StartsAtUtc,
        license.ExpiresAtUtc,
        license.Activations.Count(x => x.Status == "Active"),
        license.CreatedAtUtc,
        license.UpdatedAtUtc,
        license.RevokedAtUtc,
        license.RevokedReason);

    private static NormalizedPlan ValidatePlan(SaveLicensePlanRequest request)
    {
        var code = request.PlanCode.Trim().ToLowerInvariant();
        var name = request.Name.Trim();
        if (code.Length is < 2 or > 50 || !PlanCodeRegex().IsMatch(code))
        {
            throw Validation("invalid_plan_code", "Mã gói chỉ gồm chữ, số, dấu gạch ngang hoặc gạch dưới.");
        }
        if (name.Length is < 2 or > 200)
        {
            throw Validation("invalid_plan_name", "Tên gói phải từ 2 đến 200 ký tự.");
        }
        if (request.MaxActivatedDevices is < 1 or > 1000 || request.MaxConcurrentSessions is < 1 or > 100)
        {
            throw Validation("invalid_plan_limits", "Giới hạn thiết bị hoặc phiên không hợp lệ.");
        }
        if (request.OfflineGraceHours is < 0 or > 8760 || request.DefaultDurationDays is <= 0 or > 3650)
        {
            throw Validation("invalid_plan_duration", "Thời hạn hoặc thời gian offline không hợp lệ.");
        }
        if (request.SalePriceVnd is { } price &&
            (price <= 0 || price > 1_000_000_000_000m || decimal.Truncate(price) != price))
        {
            throw Validation("invalid_sale_price", "Giá bán phải là số nguyên VND dương.");
        }
        if (request.IsPublic && (request.SalePriceVnd is null || request.DefaultDurationDays is null))
        {
            throw Validation("invalid_public_plan", "Gói bán công khai phải có giá và thời hạn mặc định.");
        }
        if (request.DisplayOrder is < 0 or > 10000)
        {
            throw Validation("invalid_display_order", "Thứ tự hiển thị không hợp lệ.");
        }

        return new NormalizedPlan(
            code,
            name,
            NormalizeOptional(request.Description, 1000),
            request.MaxActivatedDevices,
            request.OfflineGraceHours,
            request.DefaultDurationDays,
            LicensePolicy.MergeMaxConcurrentSessions(request.FeatureFlagsJson, request.MaxConcurrentSessions),
            request.IsActive,
            request.SalePriceVnd,
            request.IsPublic,
            request.DisplayOrder,
            NormalizeMarketingFeatures(request.MarketingFeaturesJson));
    }

    private static string? NormalizeMarketingFeatures(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException();
            }

            var features = document.RootElement
                .EnumerateArray()
                .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()?.Trim() : null)
                .ToArray();
            if (features.Length > 12 || features.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 200))
            {
                throw new JsonException();
            }

            return JsonSerializer.Serialize(features);
        }
        catch (JsonException)
        {
            throw Validation(
                "invalid_marketing_features",
                "Quyền lợi hiển thị phải là JSON array gồm tối đa 12 chuỗi.");
        }
    }

    private static void RevokeLicense(UserLicense license, string reason, DateTime now)
    {
        license.Status = "Revoked";
        license.RevokedAtUtc = now;
        license.RevokedReason = reason;
        license.UpdatedAtUtc = now;
        foreach (var activation in license.Activations.Where(x => x.Status == "Active"))
        {
            activation.Status = "Revoked";
            activation.RevokedAtUtc = now;
            activation.RevokedReason = reason;
        }
    }

    private async Task RevokeUserSessionsAsync(string userId, string reason, DateTime now, CancellationToken cancellationToken)
    {
        var sessionIds = dbContext.UserSessions.Where(x => x.UserId == userId).Select(x => x.SessionId);
        await dbContext.RefreshTokens
            .Where(x => sessionIds.Contains(x.SessionId) && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.RevokedReason, reason), cancellationToken);
        await dbContext.UserSessions
            .Where(x => x.UserId == userId && x.Status == SessionStatuses.Active)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, SessionStatuses.Revoked)
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.RevokedReason, reason), cancellationToken);
    }

    private async Task RevokeDeviceSessionsAsync(Guid deviceId, string reason, DateTime now, CancellationToken cancellationToken)
    {
        var sessionIds = dbContext.UserSessions.Where(x => x.DeviceId == deviceId).Select(x => x.SessionId);
        await dbContext.RefreshTokens
            .Where(x => sessionIds.Contains(x.SessionId) && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.RevokedReason, reason), cancellationToken);
        await dbContext.UserSessions
            .Where(x => x.DeviceId == deviceId && x.Status == SessionStatuses.Active)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, SessionStatuses.Revoked)
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.RevokedReason, reason), cancellationToken);
    }

    private void AddAudit(string adminUserId, string eventType, object details, DateTime now) =>
        dbContext.AccountAuditLogs.Add(new AccountAuditLog
        {
            UserId = adminUserId,
            EventType = eventType,
            Succeeded = true,
            DetailsJson = JsonSerializer.Serialize(details),
            OccurredAtUtc = now
        });

    private static string NormalizeReason(string? reason, string fallback) =>
        NormalizeOptional(reason, 500) ?? fallback;

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maxLength)
        {
            throw Validation("value_too_long", $"Dữ liệu không được vượt quá {maxLength} ký tự.");
        }
        return normalized;
    }

    private static AccountApiException Validation(string code, string message) =>
        new(StatusCodes.Status400BadRequest, code, message);
    private static AccountApiException NotFound(string code, string message) =>
        new(StatusCodes.Status404NotFound, code, message);
    private static AccountApiException Conflict(string code, string message) =>
        new(StatusCodes.Status409Conflict, code, message);
    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    [GeneratedRegex("^[a-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PlanCodeRegex();

    private sealed record UserRow(string UserId, string Email, string? DisplayName, string AccountStatus, DateTime? LastLoginAtUtc);
    private sealed record LicenseRow(string UserId, bool PlanIsActive, AdminUserLicenseResponse Response)
    {
        public string Status => Response.Status;
        public DateTime StartsAtUtc => Response.StartsAtUtc;
        public DateTime? ExpiresAtUtc => Response.ExpiresAtUtc;
    }
    private sealed record NormalizedPlan(
        string PlanCode,
        string Name,
        string? Description,
        int MaxActivatedDevices,
        int OfflineGraceHours,
        int? DefaultDurationDays,
        string FeatureFlagsJson,
        bool IsActive,
        decimal? SalePriceVnd,
        bool IsPublic,
        int DisplayOrder,
        string? MarketingFeaturesJson);

    private static readonly string[] PaymentStatuses =
    [
        LicensePaymentStatuses.Pending,
        LicensePaymentStatuses.Paid,
        LicensePaymentStatuses.Fulfilled,
        LicensePaymentStatuses.Expired,
        LicensePaymentStatuses.Failed
    ];
}
