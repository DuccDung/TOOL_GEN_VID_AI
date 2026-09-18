using System.Runtime.CompilerServices;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Transformers;
using TOOL_LOCAL.Vietsub.Translation;

namespace VideoMaker.Vietsub.Translation.Worker;

// Benchmark-only. One context and one bounded token prefix, scoped to a loaded model/job.
// Prompt bytes, template and sampler settings are identical to the stateless production path.
internal sealed class QwenPrefixCacheExecutor : IDisposable
{
    private const int MaximumPrefixTokens = 1536;
    private readonly LLamaWeights _weights;
    private readonly LLamaContext _context;
    private readonly string _systemPrompt;
    private readonly LLamaBatch _batch = new();
    private LLamaToken[] _cachedTokens = [];
    public int PromptTokens { get; private set; }
    public int GeneratedTokens { get; private set; }
    public int ReusedPromptTokens { get; private set; }

    public QwenPrefixCacheExecutor(LLamaWeights weights, ModelParams parameters, string systemPrompt)
    {
        _weights = weights;
        _systemPrompt = systemPrompt;
        _context = weights.CreateContext(parameters);
    }

    public async IAsyncEnumerable<string> InferAsync(string prompt, InferenceParams inference,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var template = new LLamaTemplate(_weights.NativeHandle) { AddAssistant = true };
        template.Add("system", _systemPrompt);
        template.Add("user", prompt);
        var tokens = _context.Tokenize(PromptTemplateTransformer.ToModelPrompt(template), special: true).ToArray();
        PromptTokens = tokens.Length;
        GeneratedTokens = 0;
        ReusedPromptTokens = 0;
        // This experiment does not implement context shifting: reject instead of dropping instructions.
        if (tokens.Length == 0 || tokens.Length + inference.MaxTokens >= _context.ContextSize)
            throw new TranslationWorkerException("TRANSLATION_CONTEXT_INVALID",
                "Prompt vượt giới hạn thử nghiệm cache context.", false);

        var keep = Math.Min(MaximumPrefixTokens, tokens.Length - 1);
        while (ReusedPromptTokens < Math.Min(keep, _cachedTokens.Length)
            && tokens[ReusedPromptTokens].Equals(_cachedTokens[ReusedPromptTokens])) ReusedPromptTokens++;
        var completed = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReusedPromptTokens == 0) _context.NativeHandle.MemoryClear();
            else _context.NativeHandle.MemorySequenceRemove(LLamaSeqId.Zero, ReusedPromptTokens, -1);
            inference.SamplingPipeline.Reset();
            var decoder = new StreamingTokenDecoder(_context) { DecodeSpecialTokens = inference.DecodeSpecialTokens };
            var stops = new AntipromptProcessor(inference.AntiPrompts);
            var position = ReusedPromptTokens;
            while (position < tokens.Length)
            {
                _batch.Clear();
                var end = Math.Min(tokens.Length, position + checked((int)_context.BatchSize));
                for (; position < end; position++)
                    _batch.Add(tokens[position], position, LLamaSeqId.Zero, position == tokens.Length - 1);
                await DecodeAsync(cancellationToken);
            }
            for (var generated = 0; generated < inference.MaxTokens; generated++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var token = inference.SamplingPipeline.Sample(_context.NativeHandle, _batch.TokenCount - 1);
                if (token.IsEndOfGeneration(_weights.Vocab)) break;
                GeneratedTokens++;
                decoder.Add(token);
                var text = decoder.Read();
                yield return text;
                if (stops.Add(text)) break;
                _batch.Clear();
                _batch.Add(token, position++, LLamaSeqId.Zero, true);
                await DecodeAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            completed = true;
        }
        finally
        {
            if (completed)
            {
                // Retain prompt tokens only; never retain the generated answer or stale suffix.
                _context.NativeHandle.MemorySequenceRemove(LLamaSeqId.Zero, keep, -1);
                _cachedTokens = tokens[..keep];
            }
            else
            {
                _context.NativeHandle.MemoryClear();
                _cachedTokens = [];
            }
        }
    }

    private async Task DecodeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _context.DecodeAsync(_batch, cancellationToken);
        if (result != DecodeResult.Ok)
            throw new TranslationWorkerException(VietsubTranslationErrorCodes.ProcessFailed,
                "Backend không xử lý được context thử nghiệm.", false);
    }

    public void Dispose()
    {
        _cachedTokens = [];
        _context.Dispose();
    }
}
