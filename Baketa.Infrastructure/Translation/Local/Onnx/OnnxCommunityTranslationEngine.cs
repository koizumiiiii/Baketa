using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Baketa.Infrastructure.Translation.Local.Onnx;

/// <summary>
/// ONNX-Community提供のEncoder-Decoder分離モデル専用翻訳エンジン
/// </summary>
public sealed class OnnxCommunityTranslationEngine : ITranslationEngine
{
    private readonly InferenceSession _encoderSession;
    private readonly InferenceSession _decoderSession;
    private readonly OpusMtNativeTokenizer _tokenizer;
    private readonly LanguagePair _languagePair;
    private readonly ILogger<OnnxCommunityTranslationEngine> _logger;
    private bool _disposed;

    public string Name => "ONNX-Community Translation Engine";
    public string Description => "Encoder-Decoder分離アーキテクチャONNX翻訳エンジン (ONNX-Community提供)";
    public bool RequiresNetwork => false;

    public OnnxCommunityTranslationEngine(
        string encoderModelPath,
        string decoderModelPath,
        string tokenizerModelPath,
        LanguagePair languagePair,
        ILogger<OnnxCommunityTranslationEngine> logger)
    {
        _languagePair = languagePair ?? throw new ArgumentNullException(nameof(languagePair));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        try
        {
            _logger.LogInformation("ONNX-Community Encoder-Decoder翻訳エンジンの初期化開始");
            
            // Encoder session initialization
            _logger.LogInformation("🔧 Encoderモデル読み込み: {EncoderPath}", encoderModelPath);
            _encoderSession = new InferenceSession(encoderModelPath);
            
            // Decoder session initialization
            _logger.LogInformation("🔧 Decoderモデル読み込み: {DecoderPath}", decoderModelPath);
            _decoderSession = new InferenceSession(decoderModelPath);
            
            // Tokenizer initialization
            _logger.LogInformation("🔧 Native Tokenizerの初期化: {TokenizerPath}", tokenizerModelPath);
            _tokenizer = new OpusMtNativeTokenizer(tokenizerModelPath);
            
            _logger.LogInformation("✅ ONNX-Community翻訳エンジンの初期化完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ONNX-Community翻訳エンジンの初期化失敗");
            Dispose();
            throw;
        }
    }

    public async Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync()
    {
        return await Task.FromResult(new[] { _languagePair }).ConfigureAwait(false);
    }

    public async Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair)
    {
        if (languagePair == null) return false;
        
        return await Task.FromResult(
            languagePair.SourceLanguage == _languagePair.SourceLanguage &&
            languagePair.TargetLanguage == _languagePair.TargetLanguage).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequest> requests, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        
        var responses = new List<TranslationResponse>();
        foreach (var request in requests)
        {
            var response = await TranslateAsync(request, cancellationToken).ConfigureAwait(false);
            responses.Add(response);
        }
        
        return responses;
    }

    public async Task<bool> IsReadyAsync()
    {
        if (_disposed) return false;
        
        try
        {
            return await Task.FromResult(
                _encoderSession != null && 
                _decoderSession != null && 
                _tokenizer != null).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> InitializeAsync()
    {
        return await Task.FromResult(!_disposed).ConfigureAwait(false);
    }

    public async Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.SourceText))
        {
            return TranslationResponse.CreateSuccess(request, string.Empty, Name, 0);
        }

        try
        {
            _logger.LogDebug("🌐 ONNX-Community翻訳開始: '{SourceText}' ({SourceLang} → {TargetLang})", 
                request.SourceText, _languagePair.SourceLanguage, _languagePair.TargetLanguage);

            // Step 1: Tokenize input text
            var sourceTokens = _tokenizer.Tokenize(request.SourceText);
            if (sourceTokens.Length == 0)
            {
                _logger.LogWarning("⚠️  トークナイゼーション結果が空です");
                var error = new TranslationError
                {
                    ErrorCode = "TOKENIZATION_FAILED",
                    Message = "Tokenization produced empty result"
                };
                return TranslationResponse.CreateError(request, error, Name);
            }

            _logger.LogDebug("🔤 トークナイゼーション完了: {TokenCount}トークン", sourceTokens.Length);

            // Step 2: Run encoder to get hidden states
            var encoderOutput = await RunEncoderAsync(sourceTokens, cancellationToken).ConfigureAwait(false);
            
            // Step 3: Run decoder with encoder output
            var translatedText = await RunDecoderAsync(encoderOutput, sourceTokens, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("✅ ONNX-Community翻訳完了: '{TranslatedText}'", translatedText);

            return TranslationResponse.CreateSuccess(request, translatedText, Name, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ONNX-Community翻訳エラー: {Message}", ex.Message);
            return TranslationResponse.CreateErrorFromException(
                request, Name, "TRANSLATION_FAILED", ex.Message, ex);
        }
    }

    private Task<DenseTensor<float>> RunEncoderAsync(int[] inputTokens, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Create input tensors for encoder
        var inputIdsTensor = new DenseTensor<long>(
            inputTokens.Select(x => (long)x).ToArray(),
            [1, inputTokens.Length]);

        var attentionMaskTensor = new DenseTensor<long>(
            Enumerable.Repeat(1L, inputTokens.Length).ToArray(),
            [1, inputTokens.Length]);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor)
        };

        _logger.LogDebug("🔧 Encoder推論実行開始");

        // Run encoder inference
        using var results = _encoderSession.Run(inputs);
        
        // Extract encoder hidden states
        var hiddenStatesResult = results.FirstOrDefault(r => r.Name == "last_hidden_state")
            ?? throw new InvalidOperationException("Encoder output 'last_hidden_state' not found");
        
        var hiddenStatesTensor = hiddenStatesResult.AsTensor<float>()
            ?? throw new InvalidOperationException("Failed to convert encoder output to Float Tensor");

        _logger.LogDebug("✅ Encoder推論完了: Hidden States shape [{BatchSize}, {SeqLen}, {HiddenSize}]",
            hiddenStatesTensor.Dimensions[0], hiddenStatesTensor.Dimensions[1], hiddenStatesTensor.Dimensions[2]);

        // Convert to DenseTensor if needed
        if (hiddenStatesTensor is DenseTensor<float> denseTensor)
        {
            return Task.FromResult(denseTensor);
        }
        
        // Create new DenseTensor from existing tensor
        var newTensor = new DenseTensor<float>(hiddenStatesTensor.Dimensions);
        
        // Copy values manually
        for (int i = 0; i < hiddenStatesTensor.Dimensions[0]; i++)
        {
            for (int j = 0; j < hiddenStatesTensor.Dimensions[1]; j++)
            {
                for (int k = 0; k < hiddenStatesTensor.Dimensions[2]; k++)
                {
                    newTensor[i, j, k] = hiddenStatesTensor[i, j, k];
                }
            }
        }
        
        return Task.FromResult(newTensor);
    }

    private Task<string> RunDecoderAsync(
        DenseTensor<float> encoderHiddenStates, 
        int[] inputTokens, 
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Get special token IDs
        var bosTokenId = _tokenizer.GetSpecialTokenId("BOS");
        var eosTokenId = _tokenizer.GetSpecialTokenId("EOS");
        var padTokenId = _tokenizer.GetSpecialTokenId("PAD");

        // Handle Helsinki model special case (EOS = BOS when EOS is invalid)
        if (eosTokenId < 0) eosTokenId = bosTokenId;

        _logger.LogDebug("🎯 特殊トークンID: BOS={BosId}, EOS={EosId}, PAD={PadId}", 
            bosTokenId, eosTokenId, padTokenId);

        // Initialize decoder with BOS token
        var decoderInputIds = new List<long> { bosTokenId };
        const int maxLength = 100;

        // Create encoder attention mask
        var encoderAttentionMask = new DenseTensor<long>(
            Enumerable.Repeat(1L, inputTokens.Length).ToArray(),
            [1, inputTokens.Length]);

        _logger.LogDebug("🔧 Decoder Greedy Search開始 (最大長: {MaxLength})", maxLength);

        // Greedy decoding loop
        for (int step = 0; step < maxLength; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Create decoder input tensor
            var decoderInputTensor = new DenseTensor<long>(
                decoderInputIds.ToArray(),
                [1, decoderInputIds.Count]);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("encoder_attention_mask", encoderAttentionMask),
                NamedOnnxValue.CreateFromTensor("input_ids", decoderInputTensor),
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encoderHiddenStates)
            };

            // Run decoder inference
            using var results = _decoderSession.Run(inputs);
            
            // Get logits
            var logitsResult = results.FirstOrDefault(r => r.Name == "logits")
                ?? throw new InvalidOperationException("Decoder output 'logits' not found");
            
            var logitsTensor = logitsResult.AsTensor<float>()
                ?? throw new InvalidOperationException("Failed to convert decoder logits to Float Tensor");

            // Get last token logits and find best token
            var lastTokenLogits = new float[logitsTensor.Dimensions[2]]; // vocab_size
            for (int i = 0; i < lastTokenLogits.Length; i++)
            {
                lastTokenLogits[i] = logitsTensor[0, decoderInputIds.Count - 1, i];
            }

            // Greedy search: find token with highest probability
            int bestTokenId = 0;
            float bestScore = float.NegativeInfinity;
            
            for (int i = 0; i < lastTokenLogits.Length; i++)
            {
                if (lastTokenLogits[i] > bestScore)
                {
                    bestScore = lastTokenLogits[i];
                    bestTokenId = i;
                }
            }

            _logger.LogTrace("Step {Step}: Best token ID = {TokenId}, Score = {Score:F4}",
                step, bestTokenId, bestScore);

            // Check for EOS token
            if (bestTokenId == eosTokenId)
            {
                _logger.LogDebug("🏁 EOS token detected at step {Step}", step);
                break;
            }

            decoderInputIds.Add(bestTokenId);
        }

        // Convert token IDs to text (skip BOS token)
        var outputTokens = decoderInputIds.Skip(1).Select(x => (int)x).ToArray();
        var translatedText = _tokenizer.Decode(outputTokens);

        _logger.LogDebug("✅ Greedy Search完了: {StepCount}ステップ, 出力トークン数: {TokenCount}",
            decoderInputIds.Count - 1, outputTokens.Length);

        return Task.FromResult(translatedText);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _encoderSession?.Dispose();
        _decoderSession?.Dispose();
        _tokenizer?.Dispose();
        _disposed = true;

        _logger.LogDebug("🗑️  ONNX-Community翻訳エンジンのリソース解放完了");
    }
}