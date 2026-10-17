using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Domain.Providers;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Organizations;
using TOOL_SERVER.Payments;
using TOOL_SERVER.Providers;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_SERVER.Accounts;

/// <summary>Global Admin wizard. All durable commands and their receipts share AccountDbContext.</summary>
public sealed class AdminSetupService(AccountDbContext db, AdminLicensePlanComposerService composer,
    IOrganizationProviderCredentialTester tester, IProviderCredentialProtector protector,
    ISepayPaymentConfiguration payments, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DateTime Now => time.GetUtcNow().UtcDateTime;
    private DateTime Month => new(Now.Year, Now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    private static AccountApiException Error(string code, string message, int status = 409) => new(status, code, message);
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json))));
    private static AdminSetupForm Form(AdminSetupDraftEntity d) => JsonSerializer.Deserialize<AdminSetupForm>(d.FormJson, Json)!;
    private static AdminSetupDraft Response(AdminSetupDraftEntity d) => new(d.DraftId, d.OrganizationId, d.PoolId,
        d.State, d.Step, d.Revision, Form(d), d.PlanId, d.UpdatedAtUtc);
    private static AdminSetupOperation Receipt(AdminSetupOperationEntity o, bool replay = false) =>
        new(o.OperationId, o.Command, o.State, o.ResultId, o.ErrorCode, replay);

    public async Task<AdminSetupBootstrap> BootstrapAsync(string actor, CancellationToken ct)
    {
        var orgs = await (from m in db.OrganizationMembers.AsNoTracking()
            join o in db.Organizations.AsNoTracking() on m.OrganizationId equals o.OrganizationId
            where m.UserId == actor && m.Status == OrganizationMemberStatuses.Active && o.Status == OrganizationStatuses.Active &&
                  (m.Role == OrganizationMemberRoles.Owner || m.Role == OrganizationMemberRoles.OrganizationAdmin)
            orderby o.Name select new AdminSetupOrganization(o.OrganizationId, o.Name, m.Role)).ToListAsync(ct);
        var drafts = await db.AdminSetupDrafts.AsNoTracking().Where(x => x.ActorUserId == actor)
            .OrderByDescending(x => x.UpdatedAtUtc).Take(30).ToListAsync(ct);
        return new(orgs, (await SelectedModelsAsync(null, ct)).Select(Model).ToArray(),
            (await payments.GetOptionsAsync(ct)).IsReady, drafts.Select(Response).ToArray());
    }

    public async Task<AdminSetupDraft> CreateDraftAsync(CreateAdminSetupDraftRequest request, string actor, CancellationToken ct)
    {
        if (request.DraftId == Guid.Empty) throw Error("invalid_draft", "Mã bản nháp không hợp lệ.", 400);
        return await TransactionAsync(async () =>
        {
            var existing = await db.AdminSetupDrafts.SingleOrDefaultAsync(x => x.DraftId == request.DraftId, ct);
            if (existing is not null)
            {
                if (existing.ActorUserId != actor || existing.OrganizationId != request.OrganizationId)
                    throw Error("draft_conflict", "Mã bản nháp đã được sử dụng.");
                return Response(existing);
            }
            var f = new AdminSetupForm();
            Guid? pool = null;
            if (request.OrganizationId is { } id)
            {
                await RequireOrganizationAsync(id, actor, ct);
                var org = await db.Organizations.SingleAsync(x => x.OrganizationId == id, ct);
                var link = await db.OrganizationPoolOrganizations.FirstOrDefaultAsync(x => x.OrganizationId == id && x.IsAutoAssignmentEnabled, ct);
                var policy = await db.OrganizationVideoPolicies.SingleOrDefaultAsync(x => x.OrganizationId == id && x.PolicyScope == "LongForm", ct);
                f = f with { OrganizationName = org.Name, OrganizationBudget = org.MonthlyBudgetLimit,
                    Capacity = link?.SeatCapacity ?? 10, VideoModelId = policy?.ProviderModelId };
                pool = link?.OrganizationPoolId;
            }
            var d = new AdminSetupDraftEntity { DraftId = request.DraftId, ActorUserId = actor, OrganizationId = request.OrganizationId,
                PoolId = pool, FormJson = JsonSerializer.Serialize(f, Json), CreatedAtUtc = Now, UpdatedAtUtc = Now };
            db.AdminSetupDrafts.Add(d);
            await db.SaveChangesAsync(ct);
            if (d.OrganizationId is not null) { d.ConfigurationFingerprint = await ConfigurationFingerprintAsync(d, ct); await db.SaveChangesAsync(ct); }
            return Response(d);
        }, ct);
    }

    public async Task<AdminSetupDraft> GetDraftAsync(Guid id, string actor, CancellationToken ct) => Response(await DraftAsync(id, actor, ct));

    public async Task<AdminSetupDraft> SaveDraftAsync(Guid id, SaveAdminSetupDraftRequest request, string actor, CancellationToken ct) =>
        await TransactionAsync(async () =>
        {
            var d = await DraftAsync(id, actor, ct); CheckRevision(d, request.Revision); EnsureEditable(d);
            ValidateForm(request.Form, false);
            if (request.Step is < 1 or > 3) throw Error("invalid_step", "Bước thiết lập không hợp lệ.", 400);
            d.FormJson = JsonSerializer.Serialize(request.Form, Json); d.Step = request.Step;
            Touch(d); await db.SaveChangesAsync(ct); return Response(d);
        }, ct);

    public Task<AdminSetupResult> PrepareOrganizationAsync(Guid id, AdminSetupCommand request, string actor, CancellationToken ct) =>
        CommandAsync(id, request.OperationId, request.Revision, "organization", request, actor, async d =>
        {
            if (d.OrganizationId is not null) return d.OrganizationId;
            var name = Form(d).OrganizationName.Trim();
            if (name.Length is < 1 or > 200) throw Error("organization_name_required", "Nhập tên tổ chức trước khi kết nối AI.", 400);
            var org = new Organization { OrganizationId = Guid.NewGuid(), Code = $"setup-{d.DraftId:N}", Name = name,
                MonthlyBudgetLimit = 0, CreatedByUserId = actor, CreatedAtUtc = Now, UpdatedAtUtc = Now };
            db.Organizations.Add(org);
            db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = org.OrganizationId, UserId = actor,
                Role = OrganizationMemberRoles.Owner, Status = OrganizationMemberStatuses.Active, JoinedAtUtc = Now, UpdatedAtUtc = Now });
            d.OrganizationId = org.OrganizationId;
            Audit(actor, "AdminSetupOrganizationCreated", new { d.DraftId, org.OrganizationId });
            await db.SaveChangesAsync(ct);
            d.ConfigurationFingerprint = await ConfigurationFingerprintAsync(d, ct);
            return org.OrganizationId;
        }, ct);

    // Claim before the non-billable credential probe. Never hold a SQL transaction over HTTP.
    public async Task<AdminSetupResult> ConnectAsync(Guid id, AdminSetupConnectionRequest request, string actor, CancellationToken ct)
    {
        var code = request.ProviderCode.Trim().ToLowerInvariant();
        if (code is not ("openai" or "fal") || string.IsNullOrWhiteSpace(request.ApiKey) ||
            request.ApiKey.Length is < 8 or > 10000 || request.ApiKey.Any(char.IsControl))
            throw Error("invalid_credential", "Provider hoặc API key không hợp lệ.", 400);
        var safeHash = Hash(new { request.Revision, code, request.ExpectedVersion });
        var claim = await TransactionAsync(async () =>
        {
            var d = await DraftAsync(id, actor, ct);
            var previous = await FindOperationAsync(id, request.OperationId, "connection", safeHash, actor, ct);
            if (previous?.State == "Succeeded")
            {
                var credential = await db.OrganizationProviderCredentials.AsNoTracking().SingleAsync(x => x.OrganizationProviderCredentialId == previous.ResultId, ct);
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(request.ApiKey)),
                    SHA256.HashData(Encoding.UTF8.GetBytes(protector.Unprotect(credential.EncryptedPayload)))))
                    throw Error("idempotency_key_conflict", "Mã thao tác đã được dùng cho một key khác.");
                return new AdminSetupResult(Response(d), Receipt(previous, true));
            }
            CheckRevision(d, request.Revision); EnsureEditable(d);
            var orgId = d.OrganizationId ?? throw Error("organization_required", "Tạo hoặc chọn tổ chức trước.");
            await RequireOrganizationAsync(orgId, actor, ct);
            if (previous?.LeaseExpiresAtUtc > Now) throw Error("operation_running", "Đang kiểm tra kết nối. Chờ kết quả trước khi thử lại.");
            var provider = await ProviderAsync(code, ct);
            var version = await ActiveVersionAsync(orgId, provider.ProviderId, ct);
            if (version != request.ExpectedVersion) throw Error("credential_changed", "Kết nối đã thay đổi. Tải lại để kiểm tra phiên bản mới.");
            previous ??= NewOperation(id, request.OperationId, "connection", safeHash, actor);
            previous.State = "Running"; previous.ErrorCode = null; previous.LeaseExpiresAtUtc = Now.AddMinutes(2); previous.UpdatedAtUtc = Now;
            await db.SaveChangesAsync(ct);
            return new AdminSetupResult(Response(d), Receipt(previous));
        }, ct);
        if (claim.Operation.Replayed) return claim;
        try
        {
            var provider = await ProviderAsync(code, ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            await tester.TestAsync(code, provider.BaseUrl, request.ApiKey, timeout.Token);
            return await TransactionAsync(async () =>
            {
                var d = await DraftAsync(id, actor, ct); CheckRevision(d, request.Revision); EnsureEditable(d);
                var orgId = d.OrganizationId!.Value;
                await RequireOrganizationAsync(orgId, actor, ct);
                provider = await ProviderAsync(code, ct);
                if (await ActiveVersionAsync(orgId, provider.ProviderId, ct) != request.ExpectedVersion)
                    throw Error("credential_changed", "Kết nối đã thay đổi trong lúc kiểm tra. Key cũ vẫn được giữ.");
                var op = (await FindOperationAsync(id, request.OperationId, "connection", safeHash, actor, ct))!;
                if (op.State != "Running" || op.LeaseExpiresAtUtc <= Now) throw Error("operation_expired", "Lượt kiểm tra đã hết hạn. Hãy kiểm tra lại.");
                var added = await OrganizationCredentialWriter.RotateAsync(db, orgId, provider.ProviderId, provider.DisplayName,
                    null, request.ApiKey, actor, Now, protector, ct);
                Succeed(op, added.OrganizationProviderCredentialId); d.State = "ConnectionsSaved"; Touch(d);
                Audit(actor, "AdminSetupConnectionSaved", new { d.DraftId, orgId, code, added.Version });
                await db.SaveChangesAsync(ct);
                return new AdminSetupResult(Response(d), Receipt(op));
            }, ct);
        }
        catch
        {
            // Safe failure code only; never persist or log exception/request content.
            await TransactionAsync(async () =>
            {
                var op = await db.AdminSetupOperations.SingleAsync(x => x.OperationId == request.OperationId && x.ActorUserId == actor, CancellationToken.None);
                if (op.State == "Running") { op.State = "Failed"; op.ErrorCode = "connection_not_saved"; op.LeaseExpiresAtUtc = null; op.UpdatedAtUtc = Now; await db.SaveChangesAsync(CancellationToken.None); }
                return true;
            }, CancellationToken.None);
            throw;
        }
    }

    public Task<AdminSetupResult> SavePricingAsync(Guid id, AdminSetupPricingRequest request, string actor, CancellationToken ct) =>
        CommandAsync(id, request.OperationId, request.Revision, "pricing", request, actor, async d =>
        {
            var models = await SelectedModelsAsync(Form(d).VideoModelId, ct);
            var expected = models.SelectMany(m => UsageTypes(m).Select(u => (m.ProviderModelId, u))).ToHashSet();
            if (request.Rates.Count != 5 || request.Rates.Select(r => (r.ModelId, r.UsageType)).ToHashSet().Count != 5 ||
                !expected.SetEquals(request.Rates.Select(r => (r.ModelId, r.UsageType))))
                throw Error("pricing_model_changed", "Model mặc định đã thay đổi hoặc thiếu đủ 5 đơn giá. Tải lại bảng giá.");
            var changed = false;
            foreach (var entry in request.Rates)
            {
                var model = models.Single(x => x.ProviderModelId == entry.ModelId);
                if (entry.Price is null or <= 0 or > 1000000 || decimal.Round(entry.Price.Value, 8) != entry.Price.Value || entry.Unit != DisplayUnit(model))
                    throw Error("invalid_rate", "Đơn giá USD phải lớn hơn 0, tối đa 1.000.000 và không quá 8 chữ số thập phân.", 400);
                var active = await db.CostRates.Where(x => x.ProviderModelId == entry.ModelId && x.UsageType == entry.UsageType && x.IsActive).ToListAsync(ct);
                if (RateFingerprint(active) != entry.Fingerprint) throw Error("pricing_changed", "Đơn giá đã được thay đổi. Tải lại trước khi lưu.");
                var current = CurrentRate(active, model, Now);
                if (current is not null && DisplayPrice(current) == entry.Price) continue;
                changed = true;
                if (!request.ConfirmGlobalChange) throw Error("global_price_confirmation_required", "Xác nhận thay đổi giá AI áp dụng toàn hệ thống.");
                if (active.Any(x => x.EffectiveFromUtc >= Now)) throw Error("cost_rate_effective_date_conflict", "Đơn giá mới cần ngày hiệu lực sau đơn giá hiện tại.");
                foreach (var old in active) { old.IsActive = false; old.EffectiveToUtc = Now; }
                db.CostRates.Add(new AiCostRate { CostRateId = Guid.NewGuid(), ProviderModelId = entry.ModelId, UsageType = entry.UsageType,
                    Unit = entry.Unit, UnitPrice = entry.Price.Value, CurrencyCode = "USD", IsActive = true,
                    EffectiveFromUtc = Now, CreatedAtUtc = Now, MetadataJson = model.Modality == "Video"
                        ? JsonSerializer.Serialize(new { resolution = "720p", nativeAudio = true, endpointId = model.ModelCode }) : null });
            }
            if (changed) Audit(actor, "AdminSetupGlobalPricingChanged", new { d.DraftId, request.Rates });
            return d.OrganizationId;
        }, ct);

    public Task<AdminSetupResult> ConfigureAsync(Guid id, AdminSetupCommand request, string actor, CancellationToken ct) =>
        CommandAsync(id, request.OperationId, request.Revision, "configuration", request, actor, async d =>
        {
            var f = Form(d); ValidateForm(f, false);
            var orgId = d.OrganizationId ?? throw Error("organization_required", "Chưa có tổ chức.");
            await RequireOrganizationAsync(orgId, actor, ct);
            if (d.ConfigurationFingerprint is not null && d.ConfigurationFingerprint != await ConfigurationFingerprintAsync(d, ct))
                throw Error("organization_configuration_changed", "Ngân sách, policy hoặc nhóm đã thay đổi ngoài bản nháp. Tạo thiết lập mới từ cấu hình hiện tại trước khi lưu.");
            if (f.VideoModelId is null) throw Error("video_model_required", "Chọn model Veo trước khi lưu cấu hình.", 400);
            var video = (await SelectedModelsAsync(f.VideoModelId, ct)).SingleOrDefault(x => x.Modality == "Video")
                ?? throw Error("video_model_not_enabled", "Bật Fal và chọn Veo 3.1 trước khi lưu cấu hình.");
            ProjectVideoPolicyResolver.ValidateVariant("720p", true, VideoModelCapabilities.Parse(video.CapabilitiesJson, "fal"));
            if (await ActiveVersionAsync(orgId, video.ProviderId, ct) == 0) throw Error("credential_missing", "Kết nối Fal trước khi lưu policy.");
            var org = await db.Organizations.SingleAsync(x => x.OrganizationId == orgId, ct);
            var period = await db.OrganizationBudgetPeriods.SingleOrDefaultAsync(x => x.OrganizationId == orgId && x.StartsAtUtc == Month, ct);
            if (period is not null && f.OrganizationBudget < period.ActualCost + period.ReservedCost)
                throw Error("budget_below_usage", "Ngân sách không thể thấp hơn chi phí đã dùng và đang giữ.");
            org.MonthlyBudgetLimit = f.OrganizationBudget; org.CurrencyCode = "USD"; org.UpdatedAtUtc = Now;
            if (period is not null) { period.HardLimit = f.OrganizationBudget; period.UpdatedAtUtc = Now; }
            var policy = await db.OrganizationVideoPolicies.SingleOrDefaultAsync(x => x.OrganizationId == orgId && x.PolicyScope == "LongForm", ct);
            if (policy is null) { policy = new OrganizationVideoPolicy { OrganizationId = orgId, PolicyScope = "LongForm", PolicyVersion = 1, CreatedAtUtc = Now }; db.OrganizationVideoPolicies.Add(policy); }
            else if (policy.ProviderModelId != video.ProviderModelId || !policy.IsActive || policy.Resolution != "720p" || !policy.NativeAudio) policy.PolicyVersion++;
            policy.ProviderId = video.ProviderId; policy.ProviderModelId = video.ProviderModelId; policy.Resolution = "720p";
            policy.NativeAudio = true; policy.IsActive = true; policy.UpdatedByUserId = actor; policy.UpdatedAtUtc = Now;
            var activeLink = await db.OrganizationPoolOrganizations.SingleOrDefaultAsync(x => x.OrganizationId == orgId && x.IsAutoAssignmentEnabled, ct);
            if (activeLink is not null && d.PoolId is not null && d.PoolId != activeLink.OrganizationPoolId)
                throw Error("organization_pool_changed", "Tổ chức đã được liên kết với nhóm khác. Tạo bản nháp mới từ cấu hình hiện tại.");
            if (activeLink is not null) d.PoolId = activeLink.OrganizationPoolId;
            if (d.PoolId is null)
            {
                var pool = new OrganizationPool { OrganizationPoolId = Guid.NewGuid(), Code = $"setup-{d.DraftId:N}", Name = $"Nhóm {org.Name}",
                    Status = OrganizationPoolStatuses.Inactive, CreatedAtUtc = Now, UpdatedAtUtc = Now };
                db.OrganizationPools.Add(pool); d.PoolId = pool.OrganizationPoolId;
            }
            var link = await db.OrganizationPoolOrganizations.SingleOrDefaultAsync(x => x.OrganizationPoolId == d.PoolId && x.OrganizationId == orgId, ct);
            if (link is null) { link = new OrganizationPoolOrganization { OrganizationPoolId = d.PoolId.Value, OrganizationId = orgId, CreatedAtUtc = Now }; db.OrganizationPoolOrganizations.Add(link); }
            if (f.Capacity < link.ActiveSeatCount + link.ReservedSeatCount) throw Error("capacity_below_occupied", "Sức chứa không thể thấp hơn số khách và chỗ đang giữ.");
            link.SeatCapacity = f.Capacity; link.UpdatedAtUtc = Now;
            // Existing live group flags remain intact; a new group stays inactive and auto-off.
            d.State = "ConfigurationSaved"; d.Step = 3;
            Audit(actor, "AdminSetupConfigurationSaved", new { d.DraftId, orgId, d.PoolId, f.OrganizationBudget, f.Capacity, video.ModelCode });
            await db.SaveChangesAsync(ct);
            d.ConfigurationFingerprint = await ConfigurationFingerprintAsync(d, ct);
            return d.PoolId;
        }, ct);

    public async Task<AdminSetupReadiness> ReadinessAsync(Guid id, string actor, CancellationToken ct)
    {
        var d = await DraftAsync(id, actor, ct); var f = Form(d); var checks = new List<AdminSetupCheck>();
        var models = await SelectedModelsAsync(f.VideoModelId, ct);
        var allRates = await db.CostRates.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
        var connections = new List<AdminSetupConnection>();
        foreach (var code in new[] { "openai", "fal" })
        {
            var provider = await db.Providers.AsNoTracking().SingleOrDefaultAsync(x => x.ProviderCode == code, ct);
            var c = provider is null ? null : await db.OrganizationProviderCredentials.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == d.OrganizationId && x.ProviderId == provider.ProviderId && x.Status == ProviderCredentialStatuses.Active, ct);
            connections.Add(new(code, c is not null && provider!.IsEnabled, c?.Version ?? 0, c?.SecretHint));
            checks.Add(new($"credential_{code}", $"Kết nối {code}", c is not null && provider!.IsEnabled, c is null ? "Chưa có key đã kiểm tra và đang hoạt động." : provider!.IsEnabled ? null : "Provider đang tắt.", 1));
            var hostReady = provider is not null && Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var uri) && ProviderRuntimeResolver.IsAllowedBaseUri(code, uri);
            checks.Add(new($"host_{code}", $"Địa chỉ {code}", hostReady, hostReady ? null : "Địa chỉ provider phải là HTTPS và đúng allowlist của server.", 1));
        }
        foreach (var modality in new[] { "Text", "Image", "Video" })
            checks.Add(new($"model_{modality}", modality switch { "Text" => "AI viết nội dung", "Image" => "AI tạo ảnh", _ => "Veo 3.1" }, models.Any(x => x.Modality == modality), models.Any(x => x.Modality == modality) ? null : "Chưa bật model hợp lệ trong quản lý AI.", 1));
        var rates = models.SelectMany(m => UsageTypes(m).Select(u =>
        {
            var candidates = allRates.Where(x => x.ProviderModelId == m.ProviderModelId && x.UsageType == u).ToArray();
            var rate = CurrentRate(candidates, m, Now);
            checks.Add(new($"pricing_{m.Modality}_{u}", $"Đơn giá {m.DisplayName} · {u}", rate is not null, rate is null ? "Thiếu đơn giá USD hợp lệ cho đúng model/biến thể." : null, 2));
            return new AdminSetupRate(m.ProviderModelId, u, rate is null ? null : DisplayPrice(rate), DisplayUnit(m), RateFingerprint(candidates));
        })).ToArray();
        var org = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == d.OrganizationId, ct);
        var period = await db.OrganizationBudgetPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == d.OrganizationId && x.StartsAtUtc == Month, ct);
        var remaining = period is null ? org?.MonthlyBudgetLimit ?? 0 : Math.Max(0, period.HardLimit - period.ActualCost - period.ReservedCost);
        var quote = rates.Where(x => x.Price is not null).Sum(x => x.Unit == "Second" ? x.Price!.Value * 8 : x.Price!.Value * 1000 / 1000000);
        var budgetReady = org is { Status: OrganizationStatuses.Active, MonthlyBudgetLimit: > 0, CurrencyCode: "USD" } && remaining > 0 && remaining >= quote;
        checks.Add(new("budget", "Ngân sách AI", budgetReady, budgetReady ? null : "Ngân sách bằng 0 hoặc không đủ cho ước tính kiểm tra 1.000 token mỗi loại và 8 giây video.", 2));
        var memberReady = f.MemberBudget is null || f.MemberBudget > 0 && f.MemberBudget >= quote;
        checks.Add(new("member_budget", "Hạn mức mỗi khách", memberReady, memberReady ? null : "Hạn mức khách bằng 0 hoặc không đủ cho ước tính kiểm tra.", 2));
        var policy = await db.OrganizationVideoPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == d.OrganizationId && x.PolicyScope == "LongForm", ct);
        var policyReady = policy is { IsActive: true, Resolution: "720p", NativeAudio: true } && policy.ProviderModelId == f.VideoModelId && models.Any(x => x.ProviderModelId == policy.ProviderModelId && x.Modality == "Video");
        var videoModel = models.FirstOrDefault(x => x.Modality == "Video" && x.ProviderModelId == f.VideoModelId);
        if (videoModel is not null)
        {
            var caps = VideoModelCapabilities.Parse(videoModel.CapabilitiesJson, "fal");
            policyReady &= caps.NativeAudio && caps.ReferenceImage && caps.Resolutions.Contains("720p") &&
                new[] { 4, 6, 8 }.All(caps.AllowedDurationsSeconds.Contains) && caps.AspectRatios.Contains("16:9") && caps.AspectRatios.Contains("9:16");
        }
        checks.Add(new("policy", "Policy video tổ chức", policyReady, policyReady ? null : "Chưa lưu policy LongForm Fal/Veo 720p Native Audio.", 2));
        var link = await db.OrganizationPoolOrganizations.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationPoolId == d.PoolId && x.OrganizationId == d.OrganizationId, ct);
        var seats = link is null ? 0 : Math.Max(0, link.SeatCapacity - link.ActiveSeatCount - link.ReservedSeatCount);
        var configReady = link is not null && link.SeatCapacity == f.Capacity && org?.MonthlyBudgetLimit == f.OrganizationBudget;
        var otherPool = await db.OrganizationPoolOrganizations.AsNoTracking().AnyAsync(x => x.OrganizationId == d.OrganizationId && x.OrganizationPoolId != d.PoolId && x.IsAutoAssignmentEnabled, ct);
        checks.Add(new("allocation", "Nhóm nhận khách", configReady && seats > 0 && !otherPool,
            otherPool ? "Tổ chức đã nhận khách trong nhóm khác." : configReady && seats > 0 ? null : "Lưu cấu hình nhóm và đảm bảo còn chỗ trống.", 2));
        var versionReady = d.ConfigurationFingerprint is null || d.ConfigurationFingerprint == await ConfigurationFingerprintAsync(d, ct);
        checks.Add(new("configuration_version", "Phiên bản cấu hình tổ chức", versionReady,
            versionReady ? null : "Cấu hình đã thay đổi ngoài bản nháp. Tạo thiết lập mới từ tổ chức hiện tại để rà soát.", 2));
        var paymentReady = (await payments.GetOptionsAsync(ct)).IsReady;
        checks.Add(new("payments", "Thanh toán SePay", paymentReady, paymentReady ? null : "Cấu hình SePay ở mục riêng rồi quay lại kiểm tra.", 3));
        var aiReady = checks.Where(x => x.Code != "allocation" && x.Code != "payments").All(x => x.Ready);
        var allocationReady = checks.Single(x => x.Code == "allocation").Ready;
        var fingerprint = Hash(new { checks, rates, connections, remaining, seats, policy = policy?.PolicyVersion,
            month = Month, configReady, orgVersion = org?.UpdatedAtUtc, linkVersion = link?.UpdatedAtUtc, models = models.Select(x => new { x.ProviderModelId, x.UpdatedAtUtc }) });
        return new(aiReady && allocationReady && paymentReady, aiReady, allocationReady, paymentReady, remaining,
            seats, fingerprint, checks, models.Select(Model).ToArray(), rates, connections);
    }

    public Task<AdminSetupResult> PublishAsync(Guid id, AdminSetupPublishRequest request, string actor, CancellationToken ct) =>
        CommandAsync(id, request.OperationId, request.Revision, "publication", request, actor, async d =>
        {
            var f = Form(d); ValidateForm(f, true);
            if (request.Publish)
            {
                var check = await ReadinessAsync(id, actor, ct);
                if (check.Fingerprint != request.ExpectedReadiness) throw Error("setup_changed", "Cấu hình đã thay đổi. Kiểm tra lại trước khi mở bán.");
                if (!check.Ready) throw Error("setup_not_ready", string.Join(" ", check.Checks.Where(x => !x.Ready).Select(x => x.Message)));
                var pool = await db.OrganizationPools.SingleAsync(x => x.OrganizationPoolId == d.PoolId, ct);
                var link = await db.OrganizationPoolOrganizations.SingleAsync(x => x.OrganizationPoolId == d.PoolId && x.OrganizationId == d.OrganizationId, ct);
                pool.Status = OrganizationPoolStatuses.Active; pool.UpdatedAtUtc = Now;
                link.IsAutoAssignmentEnabled = true; link.IsReady = true; link.ReadinessMessage = null; link.UpdatedAtUtc = Now;
                await db.SaveChangesAsync(ct);
            }
            var result = await composer.CreateAsync(new ComposeLicensePlanRequest(request.OperationId, f.PlanName, f.DurationDays,
                f.PriceVnd, f.MaxDevices, d.PoolId, request.Publish, f.Description, f.MarketingFeatures, MemberMonthlyAiBudget: f.MemberBudget), actor, ct);
            var entity = await db.LicensePlans.SingleAsync(x => x.LicensePlanId == result.Plan.LicensePlanId, ct);
            var flags = JsonNode.Parse(entity.FeatureFlagsJson ?? "{}")!.AsObject();
            flags[AdminSetupCapabilityReadiness.Flag] = true;
            entity.FeatureFlagsJson = flags.ToJsonString();
            d.PlanId = result.Plan.LicensePlanId; d.State = request.Publish ? "Published" : "PlanDraftSaved";
            Audit(actor, "AdminSetupPublished", new { d.DraftId, d.PlanId, d.PoolId, request.Publish });
            return d.PlanId;
        }, ct);

    public async Task<AdminSetupOperation> OperationAsync(Guid id, Guid operationId, string actor, CancellationToken ct)
    {
        await DraftAsync(id, actor, ct);
        var op = await db.AdminSetupOperations.AsNoTracking().SingleOrDefaultAsync(x => x.DraftId == id && x.OperationId == operationId && x.ActorUserId == actor, ct)
            ?? throw Error("operation_not_found", "Chưa ghi nhận thao tác này. Có thể thử lại bằng cùng mã thao tác.", 404);
        return Receipt(op);
    }

    private async Task<AdminSetupResult> CommandAsync(Guid id, Guid opId, long revision, string command, object request,
        string actor, Func<AdminSetupDraftEntity, Task<Guid?>> action, CancellationToken ct) => await TransactionAsync(async () =>
    {
        var d = await DraftAsync(id, actor, ct); var hash = Hash(request);
        var op = await FindOperationAsync(id, opId, command, hash, actor, ct);
        if (op is { State: "Succeeded" }) return await ResultAsync(d, op, true, ct);
        CheckRevision(d, revision); EnsureEditable(d);
        op ??= NewOperation(id, opId, command, hash, actor);
        var result = await action(d); Succeed(op, result); Touch(d);
        await db.SaveChangesAsync(ct); return await ResultAsync(d, op, false, ct);
    }, ct);

    private async Task<AdminSetupResult> ResultAsync(AdminSetupDraftEntity d, AdminSetupOperationEntity op, bool replay, CancellationToken ct)
    {
        var plan = op.Command == "publication" && op.ResultId is { } id ? await db.LicensePlans.AsNoTracking().SingleAsync(x => x.LicensePlanId == id, ct) : null;
        return new(Response(d), Receipt(op, replay), plan is null ? null : AdminLicenseService.ToPlanResponse(plan));
    }
    private async Task<T> TransactionAsync<T>(Func<Task<T>> action, CancellationToken ct) =>
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try { var result = await action(); await tx.CommitAsync(ct); return result; }
            catch { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
        });
    private async Task<AdminSetupDraftEntity> DraftAsync(Guid id, string actor, CancellationToken ct)
    {
        var d = await db.AdminSetupDrafts.SingleOrDefaultAsync(x => x.DraftId == id && x.ActorUserId == actor, ct)
            ?? throw Error("draft_not_found", "Không tìm thấy bản nháp của quản trị viên hiện tại.", 404);
        if (d.OrganizationId is { } org) await RequireOrganizationAsync(org, actor, ct);
        return d;
    }
    private async Task RequireOrganizationAsync(Guid id, string actor, CancellationToken ct)
    {
        var m = await db.OrganizationMembers.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == id && x.UserId == actor && x.Status == OrganizationMemberStatuses.Active && x.Organization.Status == OrganizationStatuses.Active, ct);
        if (m is null || !OrganizationMemberRoles.CanManageCredentials(m.Role))
            throw Error("organization_role_denied", "Cần membership Owner hoặc Admin đang hoạt động trong tổ chức.", 403);
    }
    private static void CheckRevision(AdminSetupDraftEntity d, long revision)
    { if (d.Revision != revision) throw Error("draft_changed", "Bản nháp đã thay đổi ở tab khác. Tải lại trước khi lưu."); }
    private static void EnsureEditable(AdminSetupDraftEntity d)
    { if (d.State is "Published" or "PlanDraftSaved" or "Cancelled") throw Error("draft_closed", "Thiết lập đã hoàn tất. Tạo bản nháp mới để làm gói tiếp theo."); }
    private void Touch(AdminSetupDraftEntity d) { d.Revision++; d.UpdatedAtUtc = Now; }
    private async Task<AdminSetupOperationEntity?> FindOperationAsync(Guid draft, Guid id, string command, string hash, string actor, CancellationToken ct)
    {
        if (id == Guid.Empty) throw Error("invalid_operation", "Mã thao tác không hợp lệ.", 400);
        var op = await db.AdminSetupOperations.SingleOrDefaultAsync(x => x.OperationId == id, ct);
        if (op is not null && (op.DraftId != draft || op.ActorUserId != actor || op.Command != command || op.RequestHash != hash))
            throw Error("idempotency_key_conflict", "Mã thao tác đã được dùng cho nội dung hoặc quản trị viên khác.");
        return op;
    }
    private AdminSetupOperationEntity NewOperation(Guid draft, Guid id, string command, string hash, string actor)
    {
        var op = new AdminSetupOperationEntity { DraftId = draft, OperationId = id, Command = command, RequestHash = hash, ActorUserId = actor, UpdatedAtUtc = Now };
        db.AdminSetupOperations.Add(op); return op;
    }
    private void Succeed(AdminSetupOperationEntity op, Guid? result)
    { op.State = "Succeeded"; op.ResultId = result; op.ErrorCode = null; op.LeaseExpiresAtUtc = null; op.UpdatedAtUtc = Now; }
    private void Audit(string actor, string name, object data) => db.AccountAuditLogs.Add(new AccountAuditLog
    { UserId = actor, EventType = name, Succeeded = true, OccurredAtUtc = Now, DetailsJson = JsonSerializer.Serialize(data, Json) });
    private async Task<string> ConfigurationFingerprintAsync(AdminSetupDraftEntity d, CancellationToken ct)
    {
        var org = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == d.OrganizationId, ct);
        var link = await db.OrganizationPoolOrganizations.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationPoolId == d.PoolId && x.OrganizationId == d.OrganizationId, ct);
        var pool = await db.OrganizationPools.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationPoolId == d.PoolId, ct);
        var policy = await db.OrganizationVideoPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == d.OrganizationId && x.PolicyScope == "LongForm", ct);
        // Usage and seat occupancy can change while a draft is open; readiness checks them live.
        return Hash(new { org = org is null ? null : new { org.MonthlyBudgetLimit, org.CurrencyCode, org.Status },
            link = link is null ? null : new { link.OrganizationPoolId, link.SeatCapacity, link.Priority, link.IsAutoAssignmentEnabled, link.IsReady },
            pool = pool?.Status, policy = policy is null ? null : new { policy.ProviderId, policy.ProviderModelId, policy.PolicyVersion, policy.Resolution, policy.NativeAudio, policy.IsActive } });
    }
    private async Task<AiProvider> ProviderAsync(string code, CancellationToken ct) =>
        await db.Providers.AsNoTracking().SingleOrDefaultAsync(x => x.ProviderCode == code && x.IsEnabled, ct)
        ?? throw Error("provider_disabled", "Bật provider trong mục Quản lý AI & tổ chức trước khi kết nối.");
    private Task<int> ActiveVersionAsync(Guid org, Guid provider, CancellationToken ct) => db.OrganizationProviderCredentials
        .Where(x => x.OrganizationId == org && x.ProviderId == provider && x.Status == ProviderCredentialStatuses.Active).Select(x => x.Version).SingleOrDefaultAsync(ct);
    private async Task<List<AiProviderModel>> SelectedModelsAsync(Guid? video, CancellationToken ct)
    {
        var all = await db.ProviderModels.AsNoTracking().Include(x => x.Provider).Where(x => x.IsEnabled && x.Provider.IsEnabled).ToListAsync(ct);
        var selected = new List<AiProviderModel>();
        foreach (var modality in new[] { "Text", "Image" })
        {
            var m = all.Where(x => x.Provider.ProviderCode == "openai" && x.Modality == modality).OrderByDescending(x => x.IsDefault).ThenByDescending(x => x.UpdatedAtUtc).FirstOrDefault();
            if (m is not null) selected.Add(m);
        }
        selected.AddRange(all.Where(x => x.Provider.ProviderCode == "fal" && x.Modality == "Video" && FalVeoPolicy.IsApprovedEndpoint(x.ModelCode) && (video is null || x.ProviderModelId == video)));
        return selected;
    }
    private static AdminSetupModel Model(AiProviderModel m) => new(m.ProviderModelId, m.Provider.ProviderCode, m.ModelCode, m.DisplayName, m.Modality);
    private static string[] UsageTypes(AiProviderModel m) => m.Modality == "Video" ? ["VideoSecond"] : ["InputToken", "OutputToken"];
    private static string DisplayUnit(AiProviderModel m) => m.Modality == "Video" ? "Second" : "MillionTokens";
    private static decimal DisplayPrice(AiCostRate r) => r.Unit switch { "Token" => r.UnitPrice * 1000000, "1KTokens" => r.UnitPrice * 1000, _ => r.UnitPrice };
    private static string RateFingerprint(IEnumerable<AiCostRate> rates) => Hash(rates.OrderBy(x => x.CostRateId).Select(x => new { x.CostRateId, x.Unit, x.UnitPrice, x.CurrencyCode, x.IsActive, x.EffectiveFromUtc, x.EffectiveToUtc, x.MetadataJson }));
    private static AiCostRate? CurrentRate(IEnumerable<AiCostRate> rates, AiProviderModel model, DateTime now)
    {
        var matching = rates.Where(x => x.IsActive && x.CurrencyCode == "USD" && x.UnitPrice > 0 && x.EffectiveFromUtc <= now &&
            (x.EffectiveToUtc == null || x.EffectiveToUtc > now) && (model.Modality == "Video"
                ? x.Unit == "Second" && FalVeoPolicy.MatchesRateMetadata(x.MetadataJson, model.ModelCode)
                : x.Unit is "Token" or "1KTokens" or "MillionTokens")).ToArray();
        return matching.Length == 1 ? matching[0] : null;
    }
    private static void ValidateForm(AdminSetupForm f, bool plan)
    {
        if (f.OrganizationName.Length > 200 || f.OrganizationBudget is < 0 or > 100000000 || f.MemberBudget is < 0 or > 100000000 ||
            decimal.Round(f.OrganizationBudget, 6) != f.OrganizationBudget || f.MemberBudget is { } mb && decimal.Round(mb, 6) != mb ||
            f.Capacity is < 1 or > 100000 || f.PlanName.Length > 200 || f.Description?.Length > 1000 ||
            f.MarketingFeatures?.Count > 12 || f.MarketingFeatures?.Any(x => x is null || x.Length > 200) == true)
            throw Error("invalid_setup_form", "Kiểm tra ngân sách, sức chứa và độ dài nội dung.", 400);
        if (plan && (string.IsNullOrWhiteSpace(f.PlanName) || f.PriceVnd is null or < 1 or > 1000000000000 ||
            decimal.Truncate(f.PriceVnd.Value) != f.PriceVnd.Value || f.DurationDays is < 1 or > 3650 || f.MaxDevices is < 1 or > 1000))
            throw Error("invalid_plan", "Nhập tên, giá VND nguyên dương, thời hạn 1–3650 ngày và số thiết bị 1–1000.", 400);
    }
}
