using TOOL_LOCAL.Vietsub.Translation;

namespace VideoMaker.Vietsub.Translation.Worker;

internal static class TranslationWorkerProgram
{
    public static async Task<int> RunAsync()
    {
        Console.InputEncoding = new System.Text.UTF8Encoding(false, true);
        Console.OutputEncoding = new System.Text.UTF8Encoding(false, true);
        var testMode = Environment.GetEnvironmentVariable("VIDEOMAKER_TRANSLATION_WORKER_TEST_MODE")
            ?.Trim()
            .ToLowerInvariant();

        await using ITranslationWorkerEngine engine = testMode switch
        {
            "echo" or "stderr-large" or "hang-after-hello" or "hang-with-child" =>
                new EchoTranslationWorkerEngine(),
            "probe-fail-runtime" => new ProbeFailureTranslationWorkerEngine("PROBING_RUNTIME"),
            "probe-fail-en" => new ProbeFailureTranslationWorkerEngine("PROBING_EN"),
            "probe-fail-zh" => new ProbeFailureTranslationWorkerEngine("PROBING_ZH"),
            "backend-preflight" => new BackendPreflightTranslationWorkerEngine(forceMissing: false),
            "backend-preflight-cuda" => new BackendPreflightTranslationWorkerEngine(forceMissing: false, cuda: true),
            "backend-preflight-missing" => new BackendPreflightTranslationWorkerEngine(forceMissing: true),
            _ => new QwenTranslationWorkerEngine()
        };
        await using var host = new TranslationWorkerHost(
            Console.OpenStandardInput(),
            Console.OpenStandardOutput(),
            Console.Error,
            engine,
            testMode);
        try
        {
            await host.RunAsync(CancellationToken.None);
            return 0;
        }
        catch (VietsubTranslationWorkerProtocolException exception)
        {
            Console.Error.WriteLine($"protocol_error:{Sanitize(exception.Message)}");
            return 64;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"worker_error:{exception.GetType().Name}:{Sanitize(exception.Message)}");
            return 70;
        }
    }

    private static string Sanitize(string value)
    {
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= 300 ? singleLine : singleLine[..300];
    }
}
