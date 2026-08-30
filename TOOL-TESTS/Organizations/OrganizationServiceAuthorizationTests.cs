using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Controllers;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Domain.Providers;
using TOOL_SERVER.Organizations;
using TOOL_SERVER.Providers;
using TOOL_SHARED.Contracts.Common;
using TOOL_SHARED.Contracts.Organizations;

namespace TOOL_TESTS.Organizations;

public sealed class OrganizationServiceAuthorizationTests
{
    [Fact]
    public async Task Audit_OnlyAllowsOwnerOrOrganizationAdmin()
    {
        await using var fixture = await OrganizationFixture.CreateAsync();
        fixture.AddMember("owner", OrganizationMemberRoles.Owner);
        fixture.AddMember("organization-admin", OrganizationMemberRoles.OrganizationAdmin);
        fixture.AddMember("billing", OrganizationMemberRoles.BillingManager);
        fixture.AddAudit("owner", "OrganizationProviderCredentialRotated", """{"providerCode":"openai","secretHint":"••••1234","apiKey":"must-not-leak"}""");
        await fixture.SaveAsync();

        var ownerAudit = await fixture.Service.GetAuditAsync(fixture.OrganizationId, "owner", 50, CancellationToken.None);
        var adminAudit = await fixture.Service.GetAuditAsync(fixture.OrganizationId, "organization-admin", 50, CancellationToken.None);
        var denied = await Assert.ThrowsAsync<AccountApiException>(() => fixture.Service.GetAuditAsync(
            fixture.OrganizationId,
            "billing",
            50,
            CancellationToken.None));

        Assert.Single(ownerAudit);
        Assert.Single(adminAudit);
        Assert.Equal("organization_role_denied", denied.Code);
        Assert.False(ownerAudit[0].Data.ContainsKey("apiKey"));
        Assert.Equal("••••1234", ownerAudit[0].Data["secretHint"]);
    }

    [Fact]
    public async Task GlobalAdminIdentityWithoutMembership_CannotReadOrganizationAudit()
    {
        await using var fixture = await OrganizationFixture.CreateAsync();
        fixture.AddMember("owner", OrganizationMemberRoles.Owner);
        await fixture.SaveAsync();

        var exception = await Assert.ThrowsAsync<AccountApiException>(() => fixture.Service.GetAuditAsync(
            fixture.OrganizationId,
            "global-admin-without-membership",
            50,
            CancellationToken.None));

        Assert.Equal("organization_access_denied", exception.Code);
    }

    [Fact]
    public async Task Usage_AllowsBillingManagerButRejectsMember()
    {
        await using var fixture = await OrganizationFixture.CreateAsync();
        fixture.AddMember("owner", OrganizationMemberRoles.Owner);
        fixture.AddMember("billing", OrganizationMemberRoles.BillingManager);
        fixture.AddMember("member", OrganizationMemberRoles.Member);
        fixture.GovernanceDb.AiUsageLedger.Add(new AiUsageLedgerEntry
        {
            AiUsageLedgerEntryId = Guid.NewGuid(),
            OrganizationBudgetPeriodId = fixture.BudgetPeriodId,
            OrganizationId = fixture.OrganizationId,
            UserId = "member",
            ProjectId = Guid.NewGuid(),
            ProviderCode = "openai",
            ModelCode = "gpt-5.6-luna",
            EntryKind = UsageLedgerEntryKinds.Actual,
            Amount = 1.25m,
            CurrencyCode = "USD",
            UsageJson = """{"inputTokens":100,"outputTokens":25}""",
            OccurredAtUtc = fixture.NowUtc,
            CreatedAtUtc = fixture.NowUtc
        });
        await fixture.SaveAsync();

        var usage = await fixture.Service.GetUsageAsync(fixture.OrganizationId, "billing", 50, CancellationToken.None);
        var denied = await Assert.ThrowsAsync<AccountApiException>(() => fixture.Service.GetUsageAsync(
            fixture.OrganizationId,
            "member",
            50,
            CancellationToken.None));

        Assert.Equal(100, usage.InputTokens);
        Assert.Equal(25, usage.OutputTokens);
        Assert.Equal("organization_role_denied", denied.Code);
    }

    [Fact]
    public async Task UpdateMember_CannotRemoveLastActiveOwner()
    {
        await using var fixture = await OrganizationFixture.CreateAsync();
        fixture.AddMember("owner", OrganizationMemberRoles.Owner);
        await fixture.SaveAsync();

        var exception = await Assert.ThrowsAsync<AccountApiException>(() => fixture.Service.UpdateMemberAsync(
            fixture.OrganizationId,
            "owner",
            new UpdateOrganizationMemberRequest(
                OrganizationMemberRoles.Member,
                OrganizationMemberStatuses.Active),
            new OrganizationRequestContext("owner", null, null, "test-correlation"),
            CancellationToken.None));

        Assert.Equal("last_owner_required", exception.Code);
    }

    [Fact]
    public async Task RotateCredential_DisabledProvider_ReturnsStructuredConflictWithoutTestingCredential()
    {
        await using var fixture = await OrganizationFixture.CreateAsync();
        fixture.AddMember("owner", OrganizationMemberRoles.Owner);
        fixture.AddProvider("byteplus", "BytePlus ModelArk", isEnabled: false);
        await fixture.SaveAsync();
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "trace-provider-disabled"
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "owner")],
            "test"));
        var controller = new OrganizationsController(fixture.Service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };

        var response = await controller.RotateProviderCredential(
            fixture.OrganizationId,
            " BYTEPLUS ",
            new SaveOrganizationProviderCredentialRequest("test-key-123"),
            CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(result.Value);
        Assert.Equal("provider_disabled", error.Code);
        Assert.Equal("trace-provider-disabled", error.TraceId);
        Assert.Equal(0, fixture.CredentialTestCalls);
        Assert.Empty(fixture.GovernanceDb.OrganizationProviderCredentials);
    }

    private sealed class OrganizationFixture : IAsyncDisposable
    {
        private OrganizationFixture(
            AiGovernanceDbContext governanceDb,
            AccountDbContext accountDb,
            ProviderAdminDbContext providerDb,
            Guid organizationId,
            Guid budgetPeriodId,
            DateTime nowUtc)
        {
            GovernanceDb = governanceDb;
            AccountDb = accountDb;
            ProviderDb = providerDb;
            OrganizationId = organizationId;
            BudgetPeriodId = budgetPeriodId;
            NowUtc = nowUtc;
            credentialTester = new NoOpCredentialTester();
            Service = new OrganizationService(
                governanceDb,
                accountDb,
                providerDb,
                new NoOpCredentialProtector(),
                credentialTester,
                new FixedBudgetService(organizationId, budgetPeriodId, nowUtc),
                TimeProvider.System);
        }

        public AiGovernanceDbContext GovernanceDb { get; }
        public AccountDbContext AccountDb { get; }
        public ProviderAdminDbContext ProviderDb { get; }
        public OrganizationService Service { get; }
        public int CredentialTestCalls => credentialTester.CallCount;
        public Guid OrganizationId { get; }
        public Guid BudgetPeriodId { get; }
        public DateTime NowUtc { get; }

        private readonly NoOpCredentialTester credentialTester;

        public static async Task<OrganizationFixture> CreateAsync()
        {
            var suffix = Guid.NewGuid().ToString("N");
            var governanceDb = new AiGovernanceDbContext(
                new DbContextOptionsBuilder<AiGovernanceDbContext>()
                    .UseInMemoryDatabase($"organization-governance-{suffix}")
                    .Options);
            var accountDb = new AccountDbContext(
                new DbContextOptionsBuilder<AccountDbContext>()
                    .UseInMemoryDatabase($"organization-account-{suffix}")
                    .Options);
            var providerDb = new ProviderAdminDbContext(
                new DbContextOptionsBuilder<ProviderAdminDbContext>()
                    .UseInMemoryDatabase($"organization-provider-{suffix}")
                    .Options);
            var organizationId = Guid.NewGuid();
            var budgetPeriodId = Guid.NewGuid();
            var now = new DateTime(2026, 8, 27, 6, 0, 0, DateTimeKind.Utc);
            governanceDb.Organizations.Add(new Organization
            {
                OrganizationId = organizationId,
                Code = "test-organization",
                Name = "Test Organization",
                Status = OrganizationStatuses.Active,
                MonthlyBudgetLimit = 100,
                CurrencyCode = "USD",
                CreatedByUserId = "owner",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            await governanceDb.SaveChangesAsync();
            return new OrganizationFixture(governanceDb, accountDb, providerDb, organizationId, budgetPeriodId, now);
        }

        public void AddMember(string userId, string role)
        {
            GovernanceDb.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = OrganizationId,
                UserId = userId,
                Role = role,
                Status = OrganizationMemberStatuses.Active,
                JoinedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc
            });
            AccountDb.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = $"{userId}@example.test",
                NormalizedUserName = $"{userId}@example.test".ToUpperInvariant(),
                Email = $"{userId}@example.test",
                NormalizedEmail = $"{userId}@example.test".ToUpperInvariant(),
                AccountStatus = AccountStatuses.Active,
                CreatedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc
            });
        }

        public void AddAudit(string actorUserId, string eventType, string dataJson) =>
            GovernanceDb.OrganizationAuditLogs.Add(new OrganizationAuditLog
            {
                OrganizationId = OrganizationId,
                ActorUserId = actorUserId,
                EventType = eventType,
                DataJson = dataJson,
                CorrelationId = "test-correlation",
                OccurredAtUtc = NowUtc
            });

        public void AddProvider(string providerCode, string displayName, bool isEnabled)
        {
            ProviderDb.Providers.Add(new AiProvider
            {
                ProviderId = Guid.NewGuid(),
                ProviderCode = providerCode,
                DisplayName = displayName,
                BaseUrl = "https://provider.example.test/",
                IsEnabled = isEnabled,
                CreatedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc
            });
        }

        public async Task SaveAsync()
        {
            await AccountDb.SaveChangesAsync();
            await GovernanceDb.SaveChangesAsync();
            await ProviderDb.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await GovernanceDb.DisposeAsync();
            await AccountDb.DisposeAsync();
            await ProviderDb.DisposeAsync();
        }
    }

    private sealed class NoOpCredentialProtector : IProviderCredentialProtector
    {
        public string Protect(string apiKey) => apiKey;
        public string Unprotect(string protectedPayload) => protectedPayload;
    }

    private sealed class NoOpCredentialTester : IOrganizationProviderCredentialTester
    {
        public int CallCount { get; private set; }

        public Task TestAsync(string providerCode, string? baseUrl, string apiKey, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedBudgetService(Guid organizationId, Guid periodId, DateTime nowUtc) : IAiBudgetService
    {
        public Task<BudgetSnapshot> GetSnapshotAsync(Guid requestedOrganizationId, CancellationToken cancellationToken)
        {
            Assert.Equal(organizationId, requestedOrganizationId);
            return Task.FromResult(new BudgetSnapshot(periodId, nowUtc.AddDays(-1), nowUtc.AddDays(1), 100, 0, 1.25m, 98.75m, "USD"));
        }

        public Task<BudgetReservationResult> ReserveAsync(Guid organizationId, string userId, Guid projectId, Guid providerRequestId, string operationKey, string providerCode, string modelCode, decimal amount, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SettleAsync(Guid reservationId, decimal actualAmount, Guid? organizationProviderCredentialId, object? usage, object? rateSnapshot, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
