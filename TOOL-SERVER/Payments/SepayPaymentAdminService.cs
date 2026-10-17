using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_SERVER.Payments;

public interface ISepayPaymentConfiguration
{
    Task<SepayPaymentOptions> GetOptionsAsync(CancellationToken cancellationToken);
}

public interface ISepayPaymentAdminService
{
    Task<AdminSepaySettingsResponse> GetAsync(CancellationToken cancellationToken);
    Task<AdminSepaySettingsResponse> SaveAccountAsync(Guid? accountId, SaveSepayAccountRequest request,
        string adminUserId, CancellationToken cancellationToken);
    Task<AdminSepaySettingsResponse> SelectAccountAsync(SelectSepayAccountRequest request,
        string adminUserId, CancellationToken cancellationToken);
    Task<AdminSepaySettingsResponse> SetEnabledAsync(SetSepayEnabledRequest request,
        string adminUserId, CancellationToken cancellationToken);
}

public sealed class SepayPaymentAdminService(AccountDbContext db, IOptions<SepayPaymentOptions> options,
    TimeProvider timeProvider) : ISepayPaymentAdminService, ISepayPaymentConfiguration
{
    public async Task<AdminSepaySettingsResponse> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await db.SepayPaymentSettings.AsNoTracking().SingleOrDefaultAsync(
            x => x.SettingsId == 1, cancellationToken) ?? throw Unavailable();
        var accounts = await db.SepayReceivingAccounts.AsNoTracking()
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.AccountId)
            .Select(x => new AdminSepayAccountResponse(x.AccountId, x.DisplayName, x.BankCode,
                x.BankName, x.AccountNumber, x.AccountName, x.Revision)).ToListAsync(cancellationToken);
        return new(settings.Enabled, settings.SelectedAccountId, settings.Revision, accounts);
    }

    public async Task<SepayPaymentOptions> GetOptionsAsync(CancellationToken cancellationToken)
    {
        // One query observes settings and receiver together, including when a purchase is in a transaction.
        var selected = await (from settings in db.SepayPaymentSettings.AsNoTracking()
                              join account in db.SepayReceivingAccounts.AsNoTracking()
                                  on settings.SelectedAccountId equals (Guid?)account.AccountId
                              where settings.SettingsId == 1
                              select new { settings.Enabled, Account = account })
            .SingleOrDefaultAsync(cancellationToken);
        return BuildOptions(selected?.Enabled ?? false, selected?.Account);
    }

    public Task<AdminSepaySettingsResponse> SaveAccountAsync(Guid? accountId, SaveSepayAccountRequest request,
        string adminUserId, CancellationToken cancellationToken)
    {
        var name = Text(request.DisplayName, 100, "Tên gợi nhớ");
        var bank = Text(request.BankCode, 50, "Mã ngân hàng").ToUpperInvariant();
        var bankName = Text(request.BankName, 100, "Tên ngân hàng");
        var number = Text(request.AccountNumber, 50, "Số tài khoản");
        var holder = Text(request.AccountName, 200, "Tên chủ tài khoản");
        var normalized = new SepayReceivingAccount
        {
            DisplayName = name, BankCode = bank, BankName = bankName,
            AccountNumber = number, AccountName = holder
        };
        if (!BuildOptions(true, normalized).IsReady)
            throw Invalid("Thông tin ngân hàng hoặc cấu hình QR thanh toán không hợp lệ.");
        if (accountId is null && request.Revision is not null)
            throw Invalid("Tài khoản mới không được chứa phiên bản chỉnh sửa.");

        return MutateAsync(request.SettingsRevision, adminUserId, "SepayAccountSaved", async settings =>
        {
            var account = accountId is null ? normalized :
                await db.SepayReceivingAccounts.SingleOrDefaultAsync(x => x.AccountId == accountId, cancellationToken)
                ?? throw MissingAccount();
            if (accountId is not null && account.Revision != request.Revision) throw Changed();
            if (await db.SepayReceivingAccounts.AnyAsync(x => x.AccountId != account.AccountId &&
                    x.BankCode == bank && x.AccountNumber == number, cancellationToken))
                throw DuplicateAccount();
            account.DisplayName = name;
            account.BankCode = bank;
            account.BankName = bankName;
            account.AccountNumber = number;
            account.AccountName = holder;
            account.Revision = Guid.NewGuid();
            account.UpdatedAtUtc = Now();
            if (accountId is null)
            {
                account.CreatedAtUtc = account.UpdatedAtUtc;
                db.SepayReceivingAccounts.Add(account);
            }
            if (request.UseForNewOrders) settings.SelectedAccountId = account.AccountId;
            // Do not implicitly enable sales when adding/selecting an account.
            return new { account.AccountId, request.UseForNewOrders };
        }, cancellationToken);
    }

    public Task<AdminSepaySettingsResponse> SelectAccountAsync(SelectSepayAccountRequest request,
        string adminUserId, CancellationToken cancellationToken) =>
        MutateAsync(request.Revision, adminUserId, "SepayAccountSelected", async settings =>
        {
            var account = await db.SepayReceivingAccounts.SingleOrDefaultAsync(
                x => x.AccountId == request.AccountId, cancellationToken) ?? throw MissingAccount();
            if (!BuildOptions(true, account).IsReady) throw Invalid("Tài khoản nhận tiền chưa hợp lệ.");
            settings.SelectedAccountId = account.AccountId;
            return new { account.AccountId };
        }, cancellationToken);

    public Task<AdminSepaySettingsResponse> SetEnabledAsync(SetSepayEnabledRequest request,
        string adminUserId, CancellationToken cancellationToken) =>
        MutateAsync(request.Revision, adminUserId, "SepayPaymentEnabledChanged", async settings =>
        {
            if (request.Enabled)
            {
                var account = await db.SepayReceivingAccounts.SingleOrDefaultAsync(
                    x => x.AccountId == settings.SelectedAccountId, cancellationToken);
                if (!BuildOptions(true, account).IsReady)
                    throw Invalid("Vui lòng chọn tài khoản nhận tiền hợp lệ trước khi bật thanh toán.");
            }
            settings.Enabled = request.Enabled;
            return new { request.Enabled };
        }, cancellationToken);

    private async Task<AdminSepaySettingsResponse> MutateAsync(Guid revision, string adminUserId,
        string eventType, Func<SepayPaymentSettings, Task<object>> change, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(adminUserId))
            throw new AccountApiException(403, "admin_required", "Cần quyền quản trị hệ thống.");
        try
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = db.Database.IsRelational()
                    ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
                var settings = await db.SepayPaymentSettings.SingleOrDefaultAsync(
                    x => x.SettingsId == 1, cancellationToken) ?? throw Unavailable();
                if (revision == Guid.Empty || settings.Revision != revision) throw Changed();
                var auditDetails = await change(settings);
                settings.Revision = Guid.NewGuid();
                settings.UpdatedAtUtc = Now();
                db.AccountAuditLogs.Add(new AccountAuditLog
                {
                    UserId = adminUserId, EventType = eventType, Succeeded = true,
                    DetailsJson = JsonSerializer.Serialize(auditDetails), OccurredAtUtc = settings.UpdatedAtUtc
                });
                await db.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            });
        }
        catch (DbUpdateConcurrencyException) { throw Changed(); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        { throw DuplicateAccount(); }
        return await GetAsync(cancellationToken);
    }

    private SepayPaymentOptions BuildOptions(bool enabled, SepayReceivingAccount? account) => new()
    {
        Enabled = enabled,
        QrBaseUrl = options.Value.QrBaseUrl,
        TransferCodePrefix = options.Value.TransferCodePrefix,
        PaymentExpireMinutes = options.Value.PaymentExpireMinutes,
        ReceiverBankCode = account?.BankCode ?? string.Empty,
        ReceiverAccountNumber = account?.AccountNumber ?? string.Empty,
        ReceiverAccountName = account?.AccountName ?? string.Empty
    };

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
    private static string Text(string? value, int maxLength, string label)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Length > maxLength || value!.Any(char.IsControl))
            throw Invalid($"{label} không hợp lệ (tối đa {maxLength} ký tự).");
        return text;
    }
    private static AccountApiException Invalid(string message) => new(400, "invalid_sepay_settings", message);
    private static AccountApiException Changed() => new(409, "sepay_settings_changed",
        "Cấu hình đã được thay đổi. Vui lòng làm mới rồi thử lại.");
    private static AccountApiException DuplicateAccount() => new(409, "sepay_account_exists",
        "Tài khoản ngân hàng này đã có trong danh sách.");
    private static AccountApiException MissingAccount() => new(404, "sepay_account_not_found",
        "Không tìm thấy tài khoản nhận tiền.");
    private static AccountApiException Unavailable() => new(503, "sepay_settings_not_initialized",
        "Cấu hình thanh toán chưa sẵn sàng. Vui lòng liên hệ quản trị hệ thống.");
}
