using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Accounts;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Infrastructure;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Organizations;
using TOOL_SERVER.Providers;
using TOOL_SERVER.Payments;
using TOOL_SERVER.Updates;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using TOOL_SHARED.Contracts.Common;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ApiErrorResponse(
                "rate_limit_exceeded",
                "Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau.",
                null,
                context.HttpContext.TraceIdentifier),
            cancellationToken);
    };
    options.AddPolicy("ai-gateway", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            httpContext.Connection.RemoteIpAddress?.ToString() ??
            "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("password-reset-request", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"password-reset-request:{httpContext.Connection.RemoteIpAddress}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("password-reset", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"password-reset:{httpContext.Connection.RemoteIpAddress}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("license-payment-create", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"license-payment-create:{httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? httpContext.Connection.RemoteIpAddress?.ToString()}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("license-payment-status", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"license-payment-status:{httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? httpContext.Connection.RemoteIpAddress?.ToString()}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 180,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("sepay-webhook", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"sepay-webhook:{httpContext.Connection.RemoteIpAddress}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services
    .AddOptions<DesktopReleaseOptions>()
    .Bind(builder.Configuration.GetSection(DesktopReleaseOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services
    .AddOptions<PasswordResetOptions>()
    .Bind(builder.Configuration.GetSection(PasswordResetOptions.SectionName))
    .Validate(PasswordResetOptions.IsValid, "Password reset OTP configuration is invalid.")
    .ValidateOnStart();
builder.Services
    .AddOptions<SmtpOptions>()
    .Bind(builder.Configuration.GetSection(SmtpOptions.SectionName))
    .Validate(SmtpOptions.IsValidOrDisabled, "SMTP configuration is invalid.")
    .ValidateOnStart();
builder.Services
    .AddOptions<SepayPaymentOptions>()
    .Bind(builder.Configuration.GetSection(SepayPaymentOptions.SectionName))
    .Validate(SepayPaymentOptions.IsValidOrDisabled, "SePay payment configuration is invalid.")
    .ValidateOnStart();
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024);

var connectionString = builder.Configuration.GetConnectionString("VideoFactory")
    ?? throw new InvalidOperationException("Connection string 'VideoFactory' is not configured.");

builder.Services.AddDbContext<AccountDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDbContext<AiGovernanceDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDbContext<ProviderAdminDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDbContext<VideoFactoryDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDbContext<DataProtectionKeyDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services
    .AddDataProtection()
    .SetApplicationName("VideoMaker.Server")
    .PersistKeysToDbContext<DataProtectionKeyDbContext>();

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AccountDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(options => Encoding.UTF8.GetByteCount(options.SigningKey) >= 32, "JWT signing key must contain at least 32 bytes.")
    .ValidateOnStart();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("JWT configuration is missing.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var sessionValue = context.Principal?.FindFirstValue(AuthClaimTypes.SessionId);
                var deviceValue = context.Principal?.FindFirstValue(AuthClaimTypes.DeviceId);
                if (string.IsNullOrWhiteSpace(userId) ||
                    !Guid.TryParse(sessionValue, out var sessionId) ||
                    !Guid.TryParse(deviceValue, out var deviceId))
                {
                    context.Fail("Required session claims are missing.");
                    return;
                }

                var accountDb = context.HttpContext.RequestServices.GetRequiredService<AccountDbContext>();
                var now = DateTime.UtcNow;
                var active = await accountDb.UserSessions
                    .AsNoTracking()
                    .AnyAsync(x => x.SessionId == sessionId &&
                                   x.UserId == userId &&
                                   x.DeviceId == deviceId &&
                                   x.Status == SessionStatuses.Active &&
                                   x.AbsoluteExpiresAtUtc > now &&
                                   !x.Device!.IsRevoked &&
                                   x.User.AccountStatus == AccountStatuses.Active &&
                                   x.User.DeletedAtUtc == null,
                        context.HttpContext.RequestAborted);
                if (!active)
                {
                    context.Fail("The account session is no longer active.");
                }
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<ITokenFactory, JwtTokenFactory>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddScoped<IPasswordResetOtpStore, PasswordResetOtpStore>();
builder.Services.AddSingleton<IPasswordResetEmailSender, SmtpPasswordResetEmailSender>();
builder.Services.AddScoped<IAccountManagementService, AccountManagementService>();
builder.Services.AddScoped<IAdminLicenseService, AdminLicenseService>();
builder.Services.AddSingleton<ILicensePaymentTelemetry, LicensePaymentTelemetry>();
builder.Services.AddScoped<ILicensePaymentService, LicensePaymentService>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();
builder.Services.AddScoped<IOrganizationProvisioningReadinessEvaluator, OrganizationProvisioningReadinessEvaluator>();
builder.Services.AddScoped<IOrganizationSeatProvisioningService, OrganizationSeatProvisioningService>();
builder.Services.AddScoped<IOrganizationProvisioningAdminService, OrganizationProvisioningAdminService>();
builder.Services.AddScoped<IOrganizationProviderCredentialTester, OrganizationProviderCredentialTester>();
builder.Services.AddScoped<IAiPricingAdminService, AiPricingAdminService>();
builder.Services.AddScoped<IAiBudgetService, AiBudgetService>();
builder.Services.AddScoped<IGenerationAccessService, GenerationAccessService>();
builder.Services.AddScoped<TOOL_SERVER.Projects.IProjectAssetService, TOOL_SERVER.Projects.ProjectAssetService>();
builder.Services.AddScoped<IProviderCredentialProtector, ProviderCredentialProtector>();
builder.Services.AddScoped<IProviderRuntimeResolver, ProviderRuntimeResolver>();
builder.Services.AddScoped<IProjectVideoPolicyResolver, ProjectVideoPolicyResolver>();
builder.Services.AddScoped<IAiCostEstimator, AiCostEstimator>();
builder.Services.AddScoped<IOpenAiContentClient, OpenAiContentClient>();
builder.Services.AddScoped<IOpenAiImageClient, OpenAiImageClient>();
builder.Services.AddScoped<IOpenAiSpeechClient, OpenAiSpeechClient>();
builder.Services.AddScoped<IKlingVideoClient, KlingVideoClient>();
builder.Services.AddScoped<IVideoProviderClient, KlingVideoProviderAdapter>();
builder.Services.AddScoped<IVideoProviderClient, BytePlusVideoClient>();
builder.Services.AddScoped<IVideoProviderClient, FalVeoVideoClient>();
builder.Services.AddScoped<IVideoProviderRouter, VideoProviderRouter>();
builder.Services.AddScoped<IGenerationService, GenerationService>();
builder.Services.AddScoped<IGeneratedImageContentService, GeneratedImageContentService>();
builder.Services.AddScoped<IGeneratedVoiceContentService, GeneratedVoiceContentService>();
builder.Services.AddScoped<KlingOutputProxyService>();
builder.Services.AddScoped<IKlingOutputProxyService>(services => services.GetRequiredService<KlingOutputProxyService>());
builder.Services.AddScoped<IVideoOutputStore>(services => services.GetRequiredService<KlingOutputProxyService>());
builder.Services.AddScoped<IVideoPollingProcessor, VideoPollingProcessor>();
builder.Services.AddHostedService<VideoPollingWorker>();
builder.Services.AddHostedService<ProviderCredentialRetirementWorker>();
builder.Services.AddHostedService<BudgetReconciliationWorker>();
builder.Services.AddHostedService<OrganizationSeatProvisioningWorker>();
builder.Services.AddHostedService<GeneratedImageCleanupWorker>();
builder.Services.AddHostedService<GeneratedVoiceCleanupWorker>();
builder.Services.AddHostedService<GeneratedVideoCleanupWorker>();
builder.Services.Configure<OpenAiImageOptions>(
    builder.Configuration.GetSection(OpenAiImageOptions.SectionName));
builder.Services.Configure<OpenAiSpeechOptions>(
    builder.Configuration.GetSection(OpenAiSpeechOptions.SectionName));
builder.Services.Configure<VideoOutputOptions>(
    builder.Configuration.GetSection(VideoOutputOptions.SectionName));
builder.Services.Configure<VideoPollingOptions>(
    builder.Configuration.GetSection(VideoPollingOptions.SectionName));
builder.Services.AddHttpClient("OpenAiRuntime", client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("VideoMaker-Server/1.0");
});
builder.Services.AddHttpClient("KlingRuntime", client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("VideoMaker-Server/1.0");
});
builder.Services.AddHttpClient("BytePlusRuntime", client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("VideoMaker-Server/1.0");
});
builder.Services.AddHttpClient("FalRuntime", client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("VideoMaker-Server/1.0");
});
builder.Services.AddHttpClient("ProviderMediaDownload", client =>
{
    client.Timeout = TimeSpan.FromMinutes(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("VideoMaker-Server/1.0");
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AllowAutoRedirect = false,
    UseProxy = false,
    ConnectTimeout = TimeSpan.FromSeconds(20),
    ConnectCallback = KlingOutputProxyService.ConnectPublicHostAsync
});
builder.Services.AddHttpClient("ProviderCredentialTest", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("VideoMaker-Server/1.0");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false
});
builder.Services.AddSingleton<IDesktopReleaseStorage, DesktopReleaseStorage>();
builder.Services.AddScoped<IDesktopReleaseService, DesktopReleaseService>();

var app = builder.Build();

await AdminBootstrapper.EnsureAsync(app.Services, app.Configuration, app.Logger);
await LicensePlanBootstrapper.EnsureAsync(app.Services);
await ProviderCatalogBootstrapper.EnsureAsync(app.Services);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/admin") ||
        context.Request.Path.StartsWithSegments("/account"))
    {
        context.Response.Headers.ContentSecurityPolicy =
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
            "connect-src 'self'; font-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.XFrameOptions = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers.CacheControl = "no-store";
    }

    await next();
});

app.UseRouting();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
