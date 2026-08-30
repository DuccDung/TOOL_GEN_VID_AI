using TOOL_LOCAL.WebView;

namespace TOOL_TESTS.Database;

public sealed class WorkflowDatabaseErrorLogTests
{
    [Fact]
    public void FormatDiagnostic_RecordsOnlySqlNumbersAndConstraintNames()
    {
        const string providerMessage =
            "The UPDATE statement conflicted with the CHECK constraint \"CK_VideoGenerations_Status\". " +
            "Sensitive duplicate value: user@example.test.";

        var diagnostic = WorkflowDatabaseErrorLog.FormatDiagnostic([(547, providerMessage)]);

        Assert.Equal(
            "SqlErrorNumbers=547; Constraints=CK_VideoGenerations_Status",
            diagnostic);
        Assert.DoesNotContain("user@example.test", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeOperation_RemovesControlCharactersAndCapsLength()
    {
        var operation = WorkflowDatabaseErrorLog.NormalizeOperation(
            "generation.video\r\nSecret=" + new string('x', 80));

        Assert.Equal(64, operation.Length);
        Assert.DoesNotContain('\r', operation);
        Assert.DoesNotContain('\n', operation);
        Assert.StartsWith("generation.video__Secret_", operation, StringComparison.Ordinal);
    }
}
