using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TOOL_SERVER.Data;
using TOOL_SERVER.Generation;

namespace TOOL_TESTS.Generation;

public sealed class ProviderCatalogBootstrapperTests
{
    [Fact]
    public void ExistingCatalogUpdates_AreSetBasedAndDoNotTrackTheLoadedRowVersion()
    {
        var source = ReadRepositoryFile("TOOL-SERVER", "Generation", "ProviderCatalogBootstrapper.cs");

        Assert.Contains("var provider = await dbContext.Providers\n            .AsNoTracking()", source);
        Assert.DoesNotContain(".Include(x => x.Models)", source);
        Assert.Equal(2, source.Split(".ExecuteUpdateAsync(", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task EnsureAsync_GroupsModelsByProviderAndIsIdempotent()
    {
        var interceptor = new RecordingSaveChangesInterceptor();
        await using var services = CreateServices(interceptor);

        await ProviderCatalogBootstrapper.EnsureAsync(services);
        Assert.Equal(3, interceptor.SaveAttempts);

        await ProviderCatalogBootstrapper.EnsureAsync(services);

        Assert.Equal(3, interceptor.SaveAttempts);
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ProviderAdminDbContext>();
        var providers = await dbContext.Providers
            .AsNoTracking()
            .Include(x => x.Models)
            .OrderBy(x => x.ProviderCode)
            .ToListAsync();

        Assert.Equal(3, providers.Count);
        var openAi = Assert.Single(providers, x => x.ProviderCode == "openai");
        Assert.Equal(3, openAi.Models.Count);
        Assert.Single(openAi.Models, x => x.ModelCode == "gpt-5.6-luna" && x.Modality == "Text");
        Assert.Single(openAi.Models, x => x.ModelCode == "gpt-image-2" && x.Modality == "Image");
        Assert.Single(openAi.Models, x => x.ModelCode == "gpt-4o-mini-tts" && x.Modality == "Voice");

        var kling = Assert.Single(providers, x => x.ProviderCode == "kling");
        Assert.Single(kling.Models, x => x.ModelCode == "kling-3.0" && x.Modality == "Video");

        var bytePlus = Assert.Single(providers, x => x.ProviderCode == "byteplus");
        Assert.False(bytePlus.IsEnabled);
        Assert.Equal(2, bytePlus.Models.Count);
        Assert.All(bytePlus.Models, model => Assert.False(model.IsEnabled));
        Assert.Single(bytePlus.Models, x => x.ModelCode == "dreamina-seedance-2-0-260128" && x.Modality == "Video");
        Assert.Single(bytePlus.Models, x => x.ModelCode == "dreamina-seedance-2-5-260628" && x.Modality == "Video");
    }

    [Fact]
    public async Task EnsureAsync_RefreshesCapabilitiesWithoutOverwritingAdminManagedSettings()
    {
        var interceptor = new RecordingSaveChangesInterceptor();
        await using var services = CreateServices(interceptor);
        await ProviderCatalogBootstrapper.EnsureAsync(services);

        await using (var setupScope = services.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ProviderAdminDbContext>();
            var openAi = await setupDb.Providers
                .Include(x => x.Models)
                .SingleAsync(x => x.ProviderCode == "openai");
            var textModel = openAi.Models.Single(x => x.ModelCode == "gpt-5.6-luna");
            openAi.DisplayName = "OpenAI do admin quản lý";
            openAi.IsEnabled = false;
            openAi.CapabilitiesJson = "{}";
            textModel.DisplayName = "Model text do admin quản lý";
            textModel.IsEnabled = false;
            textModel.IsDefault = false;
            textModel.CapabilitiesJson = "{}";
            await setupDb.SaveChangesAsync();
        }

        await ProviderCatalogBootstrapper.EnsureAsync(services);

        await using var verificationScope = services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ProviderAdminDbContext>();
        var refreshedOpenAi = await verificationDb.Providers
            .AsNoTracking()
            .Include(x => x.Models)
            .SingleAsync(x => x.ProviderCode == "openai");
        var refreshedTextModel = refreshedOpenAi.Models.Single(x => x.ModelCode == "gpt-5.6-luna");

        Assert.Equal("OpenAI do admin quản lý", refreshedOpenAi.DisplayName);
        Assert.False(refreshedOpenAi.IsEnabled);
        Assert.Equal("{\"responses\":true,\"imageGeneration\":true,\"speechGeneration\":true}", refreshedOpenAi.CapabilitiesJson);
        Assert.Equal("Model text do admin quản lý", refreshedTextModel.DisplayName);
        Assert.False(refreshedTextModel.IsEnabled);
        Assert.False(refreshedTextModel.IsDefault);
        Assert.Equal("{\"api\":\"responses\",\"structuredOutput\":true}", refreshedTextModel.CapabilitiesJson);
    }

    [Fact]
    public async Task EnsureAsync_ConcurrencyConflictUsesFreshContextAndRetries()
    {
        var interceptor = new RecordingSaveChangesInterceptor(failFirstSave: true);
        await using var services = CreateServices(interceptor);

        await ProviderCatalogBootstrapper.EnsureAsync(services);

        Assert.Equal(4, interceptor.SaveAttempts);
        var contextIds = interceptor.ContextIds.ToArray();
        Assert.Equal(4, contextIds.Length);
        Assert.NotEqual(contextIds[0], contextIds[1]);

        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ProviderAdminDbContext>();
        Assert.Equal(3, await dbContext.Providers.CountAsync());
        Assert.Equal(6, await dbContext.ProviderModels.CountAsync());
    }

    [Fact]
    public async Task EnsureAsync_RepeatedConcurrencyConflictStopsAfterBoundedRetries()
    {
        var interceptor = new RecordingSaveChangesInterceptor(failEverySave: true);
        await using var services = CreateServices(interceptor);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            ProviderCatalogBootstrapper.EnsureAsync(services));

        Assert.Equal(3, interceptor.SaveAttempts);
        Assert.Equal(3, interceptor.ContextIds.Distinct().Count());
    }

    private static ServiceProvider CreateServices(RecordingSaveChangesInterceptor interceptor)
    {
        var databaseName = $"provider-catalog-{Guid.NewGuid():N}";
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddDbContext<ProviderAdminDbContext>(options =>
            options
                .UseInMemoryDatabase(databaseName)
                .AddInterceptors(interceptor));
        return serviceCollection.BuildServiceProvider();
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

    private sealed class RecordingSaveChangesInterceptor(
        bool failFirstSave = false,
        bool failEverySave = false) : SaveChangesInterceptor
    {
        private int saveAttempts;

        public int SaveAttempts => saveAttempts;

        public ConcurrentQueue<Guid> ContextIds { get; } = new();

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref saveAttempts);
            ContextIds.Enqueue(eventData.Context!.ContextId.InstanceId);
            if (failEverySave || (failFirstSave && attempt == 1))
            {
                throw new DbUpdateConcurrencyException("Simulated provider catalog concurrency conflict.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
