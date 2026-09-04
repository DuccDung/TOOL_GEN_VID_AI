namespace TOOL_TESTS.Projects;

public sealed class LicensePaymentUiContractTests
{
    [Fact]
    public void Overlay_IsNonDismissibleFocusTrappedAndCoversTheApp()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var styles = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "styles.css");
        var overlay = Slice(app, "function LicenseGateOverlay(", "function TransferRow(");

        Assert.Contains("role=\"dialog\"", overlay, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", overlay, StringComparison.Ordinal);
        Assert.Contains("event.key === 'Escape'", overlay, StringComparison.Ordinal);
        Assert.Contains("event.preventDefault()", overlay, StringComparison.Ordinal);
        Assert.Contains("event.key !== 'Tab'", overlay, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('focusin', containFocus, true)", overlay, StringComparison.Ordinal);
        Assert.Contains("!card.contains(event.target)", overlay, StringComparison.Ordinal);
        Assert.Contains("!cardRef.current.contains(document.activeElement)", overlay, StringComparison.Ordinal);
        Assert.Contains("tabIndex={-1}", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("onClose", overlay, StringComparison.Ordinal);
        Assert.Contains(".license-gate-overlay {", styles, StringComparison.Ordinal);
        Assert.Contains("position: fixed;", styles, StringComparison.Ordinal);
        Assert.Contains("z-index: 5000;", styles, StringComparison.Ordinal);
        Assert.Contains("inset: 0;", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void Checkout_AllowsOnlyApprovedQrImageHostsAndSuppressesReferrer()
    {
        var index = ReadRepositoryFile("TOOL-LOCAL", "Web", "index.html");
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");

        Assert.Contains("img-src 'self' data: https://media.app.local https://qr.sepay.vn https://vietqr.app", index, StringComparison.Ordinal);
        Assert.Contains("referrerPolicy=\"no-referrer\"", app, StringComparison.Ordinal);
    }

    [Fact]
    public void Checkout_PollsRestoresPendingPaymentAndUsesServerClock()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");

        Assert.Contains("'license.payment.current.get'", app, StringComparison.Ordinal);
        Assert.Contains("window.setInterval(requestLicensePaymentStatus, 5000)", app, StringComparison.Ordinal);
        Assert.Contains("parseServerUtc(checkout.serverTimeUtc) - Date.now()", app, StringComparison.Ordinal);
        Assert.Contains("parseServerUtc(checkout.expiresAtUtc)", app, StringComparison.Ordinal);
        Assert.Contains("const hasTimeZone = /(?:Z|[+-]\\d{2}:\\d{2})$/i.test(timestamp)", app, StringComparison.Ordinal);
        Assert.Contains("navigator.clipboard.writeText(value)", app, StringComparison.Ordinal);
        Assert.Contains("Tạo mã thanh toán mới", app, StringComparison.Ordinal);
    }

    [Fact]
    public void Checkout_UsesCompactLowRadiusPanelsAndResetsScroll()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var styles = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "styles.css");
        var checkoutCardStyles = Slice(styles, ".license-gate-card.has-checkout {", ".license-gate-header {");
        var checkoutStyles = Slice(styles, ".license-checkout-view {", "@media (max-width: 820px)");

        Assert.Contains("card.scrollTo({ top: 0, behavior: 'auto' })", app, StringComparison.Ordinal);
        Assert.Contains("max-height: none;", checkoutCardStyles, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden;", checkoutCardStyles, StringComparison.Ordinal);
        Assert.Contains("width: min(100%, 198px);", checkoutStyles, StringComparison.Ordinal);
        Assert.Contains("align-items: start;", checkoutStyles, StringComparison.Ordinal);
        Assert.Contains("border-radius: 8px;", checkoutStyles, StringComparison.Ordinal);
        Assert.Contains("gap: 12px;", checkoutStyles, StringComparison.Ordinal);
        Assert.DoesNotContain("border-radius: 19px;", checkoutStyles, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanSelection_UsesCompactProductCopyAndLowRadiusCards()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var styles = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "styles.css");
        var overlay = Slice(app, "function LicenseGateOverlay(", "function TransferRow(");
        var planStyles = Slice(styles, ".license-plan-grid {", ".license-loading {");

        Assert.Contains("QUYỀN SỬ DỤNG", overlay, StringComparison.Ordinal);
        Assert.Contains("Gói {offer.durationDays} ngày", overlay, StringComparison.Ordinal);
        Assert.Contains("Phổ biến", overlay, StringComparison.Ordinal);
        Assert.Contains("Chọn gói này", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("{offer.planCode}", overlay, StringComparison.Ordinal);
        Assert.Contains("border-radius: 8px;", planStyles, StringComparison.Ordinal);
        Assert.Contains("background: #fff; box-shadow: none;", planStyles, StringComparison.Ordinal);
        Assert.DoesNotContain("linear-gradient", planStyles, StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Cannot locate repository file: {Path.Combine(relativeParts)}");
    }
}
