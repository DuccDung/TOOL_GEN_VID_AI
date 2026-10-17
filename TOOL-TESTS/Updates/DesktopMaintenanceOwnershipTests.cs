using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_LOCAL.ModelDistribution;
using TOOL_LOCAL.Updates;
using TOOL_SHARED.Distribution.StaticModels;

namespace TOOL_TESTS.Updates;

public sealed class DesktopMaintenanceOwnershipTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-own-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private (string Target, string Metadata, string Journal) Create()
    {
        var target = Path.Combine(_root, "app"); var metadata = Path.Combine(_root, "metadata");
        Directory.CreateDirectory(target);
        var operation = Guid.NewGuid().ToString("N"); var hash = new string('a', 64);
        var receipt = new StaticApplicationReceipt(1, ApprovedStaticModelBundle.Current.BundleId, target, "1.2.0", 12, hash, operation, DateTime.UtcNow);
        File.WriteAllText(Path.Combine(target, "static-setup-receipt.json"), JsonSerializer.Serialize(receipt, _json));
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(target.TrimEnd('\\').ToUpperInvariant()))).ToLowerInvariant();
        var journalPath = Path.Combine(metadata, "installer-transactions", key, "transaction.json");
        Directory.CreateDirectory(Path.GetDirectoryName(journalPath)!);
        var journal = new StaticApplicationJournal(1, operation, target, hash, "Committed", JsonSerializer.SerializeToElement(Array.Empty<object>()));
        File.WriteAllText(journalPath, JsonSerializer.Serialize(journal, _json));
        return (target, metadata, journalPath);
    }

    [Fact]
    public void Repair_ownership_does_not_require_model_READY_or_even_application_binary()
    {
        var value = Create();
        Assert.False(File.Exists(Path.Combine(value.Target, "TOOL-LOCAL.exe")));
        var receipt = DesktopInstalledMaintenanceService.ReadOwnership(value.Target, value.Metadata);
        Assert.Equal("1.2.0", receipt.Version);
        Assert.False(File.Exists(Path.Combine(value.Metadata, "static-model-install.json")));
    }

    [Theory]
    [InlineData("state")]
    [InlineData("hash")]
    [InlineData("operation")]
    [InlineData("target")]
    public void Inconsistent_commit_cannot_authorize_maintenance(string changed)
    {
        var value = Create(); var original = File.ReadAllText(value.Journal);
        var journal = JsonSerializer.Deserialize<StaticApplicationJournal>(original, _json)!;
        journal = changed switch
        {
            "state" => journal with { State = "Applying" }, "hash" => journal with { ReleaseSha256 = new('b', 64) },
            "operation" => journal with { OperationId = Guid.NewGuid().ToString("N") },
            _ => journal with { TargetRoot = Path.Combine(_root, "other") }
        };
        var changedJson = JsonSerializer.Serialize(journal, _json);
        File.WriteAllText(value.Journal, changedJson);
        Assert.Throws<InvalidDataException>(() => DesktopInstalledMaintenanceService.ReadOwnership(value.Target, value.Metadata));
        Assert.Equal(changedJson, File.ReadAllText(value.Journal));
    }

    [Fact]
    public void Duplicate_receipt_fields_are_not_treated_as_valid_ownership()
    {
        var value = Create(); var path = Path.Combine(value.Target, "static-setup-receipt.json");
        var original = File.ReadAllText(path);
        File.WriteAllText(path, original.Insert(1, "\"schemaVersion\":1,"));
        Assert.Throws<InvalidDataException>(() => DesktopInstalledMaintenanceService.ReadOwnership(value.Target, value.Metadata));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
