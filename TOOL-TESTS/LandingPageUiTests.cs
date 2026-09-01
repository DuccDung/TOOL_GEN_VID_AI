namespace TOOL_TESTS;

public sealed class LandingPageUiTests
{
    [Fact]
    public void LandingPage_ContainsCustomerFocusedProductJourney()
    {
        var page = ReadRepositoryFile("TOOL-SERVER", "Pages", "Index.cshtml");

        Assert.Contains("Layout = \"_LandingLayout\"", page);
        Assert.Contains("Biến một ý tưởng", page);
        Assert.Contains("id=\"use-cases\"", page);
        Assert.Contains("id=\"how-it-works\"", page);
        Assert.Contains("id=\"features\"", page);
        Assert.Contains("id=\"experience\"", page);
        Assert.Contains("id=\"faq\"", page);
        Assert.Contains("id=\"start\"", page);
        Assert.Contains("Tải VideoMaker", page);
        Assert.Contains("/api/launcher-distribution/setup/latest/download?channel=Stable&amp;platform=win-x64", page);
        Assert.Contains("href=\"/privacy\"", page);
        Assert.Contains("class=\"landing-photo hero-photo photo--editor\"", page);
        Assert.Contains("~/images/landing/hero-video-creator.jpg", page);
        Assert.Contains("~/images/landing/idea-planning.jpg", page);
        Assert.Contains("fetchpriority=\"high\"", page);
        Assert.Contains("loading=\"lazy\"", page);
        Assert.DoesNotContain("videomaker-", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("app-ui-shot", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=\"product-window\"", page);
        Assert.DoesNotContain("floating-note", page);
        Assert.DoesNotContain("ĐANG TẠO VIDEO", page);
        Assert.DoesNotContain("Đã sẵn sàng để duyệt", page);
    }

    [Fact]
    public void LandingPage_DoesNotExposeInternalImplementationLanguageOrInventCommercialProof()
    {
        var page = ReadRepositoryFile("TOOL-SERVER", "Pages", "Index.cshtml");
        var internalTerms = new[]
        {
            "AI gateway theo tổ chức",
            "OPENAI",
            "VIDEO PROVIDER",
            "Credential ở server",
            "Budget reservation",
            "Kling",
            "BytePlus",
            "FFmpeg",
            "JWT",
            "SSRF"
        };

        foreach (var term in internalTerms)
        {
            Assert.DoesNotContain(term, page, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("VND", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("testimonial", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("khách hàng nói", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LandingPage_UsesIsolatedLocalAssetsWithoutReferenceSiteDependencies()
    {
        var page = ReadRepositoryFile("TOOL-SERVER", "Pages", "Index.cshtml");
        var layout = ReadRepositoryFile("TOOL-SERVER", "Pages", "Shared", "_LandingLayout.cshtml");
        var css = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "css", "landing.css");
        var script = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "js", "landing.js");
        var combined = string.Join('\n', page, layout, css, script);

        Assert.Contains("~/css/landing.css", layout);
        Assert.Contains("~/js/landing.js", layout);
        Assert.DoesNotContain("bootstrap", layout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tasks.datools.info", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/_nuxt", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://images.", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LandingPage_ProvidesResponsiveNavigationAndAccessibleFaq()
    {
        var page = ReadRepositoryFile("TOOL-SERVER", "Pages", "Index.cshtml");
        var layout = ReadRepositoryFile("TOOL-SERVER", "Pages", "Shared", "_LandingLayout.cshtml");
        var css = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "css", "landing.css");
        var script = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "js", "landing.js");

        Assert.Contains("lang=\"vi\"", layout);
        Assert.Contains("class=\"skip-link\"", layout);
        Assert.Contains("aria-controls=\"primary-navigation\"", page);
        Assert.Contains("data-faq-list", page);
        Assert.Contains("class=\"faq-item\"", page);
        Assert.Contains("@media (max-width: 920px)", css);
        Assert.Contains("prefers-reduced-motion", css);
        Assert.Contains("event.key !== 'Escape'", script);
        Assert.Contains("button.setAttribute('aria-controls'", script);
        Assert.Contains("answer.setAttribute('role', 'region')", script);
    }

    [Fact]
    public void LandingPage_MobileHeroKeepsEditorialPhotoInDocumentFlow()
    {
        var css = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "css", "landing.css");
        var tabletBreakpoint = css.IndexOf("@media (max-width: 920px)", StringComparison.Ordinal);
        var mobileBreakpoint = css.IndexOf("@media (max-width: 700px)", StringComparison.Ordinal);
        var narrowBreakpoint = css.IndexOf("@media (max-width: 460px)", StringComparison.Ordinal);

        Assert.True(tabletBreakpoint >= 0);
        Assert.True(mobileBreakpoint >= 0);
        Assert.True(narrowBreakpoint > mobileBreakpoint);

        var tabletCss = css[tabletBreakpoint..mobileBreakpoint];
        var mobileCss = css[mobileBreakpoint..narrowBreakpoint];
        Assert.Contains(".hero-photo {\n    position: relative;\n    top: auto;", tabletCss);
        Assert.Contains(".hero-product {\n    min-height: 0;", mobileCss);
        Assert.DoesNotContain("min-height: 435px", css);
    }

    [Fact]
    public void LandingPage_UseCasePhotosStayInFlowAndCannotOverlapCopy()
    {
        var css = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "css", "landing.css");
        var cardCss = ReadCssRule(css, ".use-case-card");
        var copyCss = ReadCssRule(css, ".use-case-card > p");
        var photoCss = ReadCssRule(css, ".portrait-scene");

        Assert.Contains("display: flex;", cardCss);
        Assert.Contains("flex-direction: column;", cardCss);
        Assert.Contains("margin: 0 0 24px;", copyCss);
        Assert.Contains("position: relative;", photoCss);
        Assert.Contains("margin-top: auto;", photoCss);
        Assert.DoesNotContain("position: absolute;", photoCss);
    }

    [Theory]
    [InlineData("hero-video-creator.jpg")]
    [InlineData("marketing-creator.jpg")]
    [InlineData("mobile-video.jpg")]
    [InlineData("idea-planning.jpg")]
    [InlineData("production-crew.jpg")]
    [InlineData("creative-workspace.jpg")]
    [InlineData("character-design.jpg")]
    [InlineData("voice-audio.jpg")]
    [InlineData("scene-review.jpg")]
    public void LandingPage_EditorialPhotosAreLocalAndOptimized(string fileName)
    {
        var assetPath = LocateRepositoryFile("TOOL-SERVER", "wwwroot", "images", "landing", fileName);
        var file = new FileInfo(assetPath);

        Assert.True(file.Length > 35_000, $"Photo {fileName} is unexpectedly small.");
        Assert.True(file.Length < 280_000, $"Photo {fileName} should remain below 280 KB.");
    }

    [Fact]
    public void LandingPage_TracksPhotoSourcesAndLicense()
    {
        var sources = ReadRepositoryFile("TOOL-SERVER", "LANDING_IMAGE_SOURCES.md");

        Assert.Contains("https://unsplash.com/license", sources);
        Assert.Contains("hero-video-creator.jpg", sources);
        Assert.Contains("scene-review.jpg", sources);
        Assert.Contains("Unsplash License", sources);
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        return File.ReadAllText(LocateRepositoryFile(relativeParts)).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string ReadCssRule(string css, string selector)
    {
        var start = css.IndexOf($"{selector} {{", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Cannot locate CSS rule: {selector}");

        var end = css.IndexOf('}', start);
        Assert.True(end > start, $"Cannot locate the end of CSS rule: {selector}");
        return css[start..(end + 1)];
    }

    private static string LocateRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Cannot locate repository file: {Path.Combine(relativeParts)}");
    }
}
