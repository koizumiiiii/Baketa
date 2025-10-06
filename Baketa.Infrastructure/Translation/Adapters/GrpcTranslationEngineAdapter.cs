using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Adapters;

/// <summary>
/// gRPC翻訳クライアントをITranslationEngineインターフェースに適合させるAdapter
/// Phase 3.1: OptimizedPythonTranslationEngine削除 - シンプルなAdapter実装
///
/// 責務:
/// - ITranslationClientをITranslationEngineインターフェースでラップ
/// - バッチ翻訳の並行実行制御
/// - 言語ペアサポート確認
/// </summary>
public sealed class GrpcTranslationEngineAdapter : ITranslationEngine
{
    private readonly ITranslationClient _client;
    private readonly ILogger<GrpcTranslationEngineAdapter> _logger;
    private readonly IReadOnlyList<LanguagePair> _supportedLanguagePairs;
    private bool _disposed;

    /// <summary>
    /// NLLB-200がサポートする主要言語ペア（日英翻訳特化）
    /// </summary>
    private static readonly IReadOnlyList<LanguagePair> DefaultSupportedLanguagePairs = new List<LanguagePair>
    {
        new() { SourceLanguage = Language.Japanese, TargetLanguage = Language.English },
        new() { SourceLanguage = Language.English, TargetLanguage = Language.Japanese },
        new() { SourceLanguage = Language.Japanese, TargetLanguage = Language.ChineseSimplified },
        new() { SourceLanguage = Language.ChineseSimplified, TargetLanguage = Language.Japanese },
        new() { SourceLanguage = Language.Japanese, TargetLanguage = Language.Korean },
        new() { SourceLanguage = Language.Korean, TargetLanguage = Language.Japanese },
        new() { SourceLanguage = Language.English, TargetLanguage = Language.ChineseSimplified },
        new() { SourceLanguage = Language.ChineseSimplified, TargetLanguage = Language.English },
        new() { SourceLanguage = Language.English, TargetLanguage = Language.Korean },
        new() { SourceLanguage = Language.Korean, TargetLanguage = Language.English }
    }.AsReadOnly();

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="client">gRPC翻訳クライアント</param>
    /// <param name="logger">ロガー</param>
    /// <param name="supportedLanguagePairs">サポート言語ペア（nullの場合はデフォルト）</param>
    public GrpcTranslationEngineAdapter(
        ITranslationClient client,
        ILogger<GrpcTranslationEngineAdapter> logger,
        IReadOnlyList<LanguagePair>? supportedLanguagePairs = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _supportedLanguagePairs = supportedLanguagePairs ?? DefaultSupportedLanguagePairs;

        _logger.LogInformation(
            "GrpcTranslationEngineAdapter initialized: Mode={CommunicationMode}, SupportedPairs={Count}",
            _client.CommunicationMode,
            _supportedLanguagePairs.Count
        );
    }

    /// <inheritdoc/>
    public string Name => "gRPC Translation Engine";

    /// <inheritdoc/>
    public string Description => "gRPC-based Python translation server (NLLB-200)";

    /// <inheritdoc/>
    public bool RequiresNetwork => true;

    /// <inheritdoc/>
    public Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IReadOnlyCollection<LanguagePair> result = _supportedLanguagePairs.ToList().AsReadOnly();
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(languagePair);

        var isSupported = _supportedLanguagePairs.Any(pair =>
            pair.SourceLanguage.Code.Equals(languagePair.SourceLanguage.Code, StringComparison.OrdinalIgnoreCase) &&
            pair.TargetLanguage.Code.Equals(languagePair.TargetLanguage.Code, StringComparison.OrdinalIgnoreCase)
        );

        return Task.FromResult(isSupported);
    }

    /// <inheritdoc/>
    public async Task<TranslationResponse> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            // ITranslationClient.TranslateAsyncを直接呼び出し
            var response = await _client.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[GrpcAdapter] Translation failed: {SourceLang} -> {TargetLang}",
                request.SourceLanguage.Code,
                request.TargetLanguage.Code
            );

            return TranslationResponse.CreateErrorFromException(
                request,
                Name,
                "ADAPTER_ERROR",
                $"gRPC translation failed: {ex.Message}",
                ex
            );
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(requests);

        if (requests.Count == 0)
        {
            return Array.Empty<TranslationResponse>();
        }

        _logger.LogDebug("[GrpcAdapter] Batch translation: {Count} requests", requests.Count);

        try
        {
            // 🔧 [PHASE3.1] 各リクエストを並行実行（Task.WhenAll）
            // Note: GrpcTranslationClientにTranslateBatchAsyncメソッドが実装されたら切り替え
            var tasks = requests.Select(request => TranslateAsync(request, cancellationToken));
            var responses = await Task.WhenAll(tasks).ConfigureAwait(false);

            _logger.LogDebug(
                "[GrpcAdapter] Batch translation completed: {SuccessCount}/{TotalCount} successful",
                responses.Count(r => r.IsSuccess),
                responses.Length
            );

            return responses;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GrpcAdapter] Batch translation failed: {Count} requests", requests.Count);

            // エラー時は各リクエストに対してエラーレスポンスを返す
            return requests.Select(request =>
                TranslationResponse.CreateErrorFromException(
                    request,
                    Name,
                    "BATCH_ERROR",
                    $"Batch translation failed: {ex.Message}",
                    ex
                )
            ).ToList();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsReadyAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            return await _client.IsReadyAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GrpcAdapter] IsReady check failed");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            _logger.LogInformation("[GrpcAdapter] Initializing gRPC translation engine");

            // ヘルスチェックで初期化確認
            var isHealthy = await _client.HealthCheckAsync(CancellationToken.None).ConfigureAwait(false);

            if (isHealthy)
            {
                _logger.LogInformation("[GrpcAdapter] gRPC translation engine initialized successfully");
                return true;
            }

            _logger.LogWarning("[GrpcAdapter] gRPC translation engine health check failed");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GrpcAdapter] Initialization failed");
            return false;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogDebug("[GrpcAdapter] Disposing gRPC translation engine adapter");

        try
        {
            if (_client is IDisposable disposableClient)
            {
                disposableClient.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GrpcAdapter] Error disposing client");
        }

        _disposed = true;
    }
}
