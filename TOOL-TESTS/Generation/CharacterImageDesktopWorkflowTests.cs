namespace TOOL_TESTS.Generation;

public sealed class CharacterImageDesktopWorkflowTests
{
    [Fact]
    public void UiAndBridge_ExposeGenerateRegeneratePreviewAndExplicitLockFlow()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var bridge = ReadRepositoryFile("TOOL-LOCAL", "WebView", "DashboardBridge.cs");

        Assert.Contains("postToHost('character.reference.generate'", app);
        Assert.Contains("Tạo ảnh bằng AI", app);
        Assert.Contains("Sinh lại ảnh", app);
        Assert.Contains("Thay bằng ảnh khác", app);
        Assert.Contains("GPT-Image-2 · 1024×1024 · PNG", app);
        Assert.Contains("CHARACTER IMAGE", app);
        Assert.Contains("imageSetupGuidance", app);
        Assert.Contains("Bảng giá AI → gpt-image-2", app);
        Assert.Contains("character.approve", app);
        Assert.Contains("case \"character.reference.generate\"", bridge);
        Assert.Contains("GenerateCharacterReferenceImageAsync", bridge);
    }

    [Fact]
    public void Desktop_PersistsVerifiedDownloadAtomicallyAndKeepsCharacterDraft()
    {
        var service = ReadRepositoryFile("TOOL-LOCAL", "Generation", "ProjectGenerationService.cs");
        var client = ReadRepositoryFile("TOOL-LOCAL", "Generation", "ServerGenerationClient.cs");

        Assert.Contains("var partialPath = $\"{finalPath}.part\"", service);
        Assert.Contains("ValidateDownloadedCharacterImageAsync", service);
        Assert.Contains("File.Move(partialPath, finalPath, false)", service);
        Assert.Contains("SourceProviderRequestId = response.ProviderRequestId", service);
        Assert.Contains("SourceType = \"Generated\"", service);
        Assert.Contains("SourceProviderCode = response.ProviderCode", service);
        Assert.Contains("current.IsPrimary = false", service);
        Assert.Contains("IsPrimary = true", service);
        Assert.Contains("if (character.Status != \"Draft\")", service);
        Assert.DoesNotContain("character.Status = \"Approved\"", service, StringComparison.Ordinal);
        Assert.Contains("IncrementalHash.CreateHash(HashAlgorithmName.SHA256)", client);
    }

    [Fact]
    public void Desktop_UsesOnlyAuthenticatedRelativeServerContentUrl()
    {
        var desktopFiles = EnumerateSourceFiles("TOOL-LOCAL").ToArray();
        var client = ReadRepositoryFile("TOOL-LOCAL", "Generation", "ServerGenerationClient.cs");

        Assert.Contains("/api/generation/character-images/{image.ProviderRequestId:D}/content", client);
        Assert.Contains("request.Headers.Authorization", client);
        Assert.Contains("uri.IsAbsoluteUri", client);
        Assert.DoesNotContain(
            desktopFiles,
            file => File.ReadAllText(file).Contains("api.openai.com", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> EnumerateSourceFiles(params string[] relativeParts)
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(new[] { root }.Concat(relativeParts).ToArray());
        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                           file.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                           file.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                           !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                           !file.Contains($"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                           !file.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var path = Path.Combine(new[] { FindRepositoryRoot() }.Concat(relativeParts).ToArray());
        return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TOOL_GEN_POST_VIDEO.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Cannot locate the VideoMaker repository root.");
    }
}
