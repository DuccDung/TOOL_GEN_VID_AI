namespace TOOL_SERVER.Domain.Accounts;

public sealed class SepayReceivingAccount
{
    public Guid AccountId { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;
    public string BankCode { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public Guid Revision { get; set; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class SepayPaymentSettings
{
    public int SettingsId { get; set; } = 1;
    public bool Enabled { get; set; }
    public Guid? SelectedAccountId { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public DateTime UpdatedAtUtc { get; set; }
}
