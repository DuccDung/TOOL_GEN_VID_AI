namespace TOOL_SHARED.Contracts.Accounts;

public sealed record AdminSepayAccountResponse(
    Guid AccountId,
    string DisplayName,
    string BankCode,
    string BankName,
    string AccountNumber,
    string AccountName,
    Guid Revision);

public sealed record AdminSepaySettingsResponse(
    bool Enabled,
    Guid? SelectedAccountId,
    Guid Revision,
    IReadOnlyList<AdminSepayAccountResponse> Accounts);

public sealed record SaveSepayAccountRequest(
    string DisplayName,
    string BankCode,
    string BankName,
    string AccountNumber,
    string AccountName,
    bool UseForNewOrders,
    Guid SettingsRevision,
    Guid? Revision = null);

public sealed record SelectSepayAccountRequest(Guid AccountId, Guid Revision);

public sealed record SetSepayEnabledRequest(bool Enabled, Guid Revision);
