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
    private readonly IPythonServerManager? _serverManager;
    private bool _disposed;
    private bool _serverEnsured; // サーバー起動確認済みフラグ
    private readonly SemaphoreSlim _serverLock = new(1, 1); // サーバー起動の排他制御

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
    /// <param name="serverManager">Pythonサーバーマネージャー（nullの場合はサーバー起動なし）</param>
    /// <param name="supportedLanguagePairs">サポート言語ペア（nullの場合はデフォルト）</param>
    public GrpcTranslationEngineAdapter(
        ITranslationClient client,
        ILogger<GrpcTranslationEngineAdapter> logger,
        IPythonServerManager? serverManager = null,
        IReadOnlyList<LanguagePair>? supportedLanguagePairs = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serverManager = serverManager;
        _supportedLanguagePairs = supportedLanguagePairs ?? DefaultSupportedLanguagePairs;

        Console.WriteLine($"🔥 [GrpcAdapter] コンストラクタ開始 - ServerManager: {_serverManager != null}");
        _logger.LogInformation(
            "GrpcTranslationEngineAdapter initialized: Mode={CommunicationMode}, ServerManager={HasServerManager}, SupportedPairs={Count}",
            _client.CommunicationMode,
            _serverManager != null,
            _supportedLanguagePairs.Count
        );
        Console.WriteLine($"🔥 [GrpcAdapter] コンストラクタ完了 - ServerManager is null: {_serverManager == null}");
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

    /// <summary>
    /// Pythonサーバーが起動していることを確認します（初回のみ実行）
    /// </summary>
    private async Task EnsureServerStartedAsync()
    {
        var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log");
        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [PHASE5_ENSURE_START] EnsureServerStartedAsync メソッド開始\r\n");

        if (_serverEnsured || _serverManager == null)
        {
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [PHASE5_ENSURE_EARLY_RETURN] _serverEnsured={_serverEnsured}, _serverManager==null={_serverManager == null}\r\n");
            return;
        }

        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [PHASE5_LOCK_BEFORE] _serverLock.WaitAsync呼び出し直前\r\n");
        await _serverLock.WaitAsync().ConfigureAwait(false);
        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [PHASE5_LOCK_AFTER] _serverLock.WaitAsync完了 - ロック取得成功\r\n");
        try
        {
            if (_serverEnsured) // Double-check
            {
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [PHASE5_DOUBLE_CHECK] _serverEnsured=true - 早期リターン\r\n");
                return;
            }

            _logger.LogInformation("[GrpcAdapter] 🔥 Ensuring Python gRPC server is started");

            const string GrpcServerLanguagePair = "grpc-all";
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [PHASE5_GET_SERVER_BEFORE] GetServerAsync呼び出し直前\r\n");
            var serverInfo = await _serverManager.GetServerAsync(GrpcServerLanguagePair).ConfigureAwait(false);
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [PHASE5_GET_SERVER_AFTER] GetServerAsync完了 - serverInfo==null: {serverInfo == null}, IsHealthy: {serverInfo?.IsHealthy ?? false}\r\n");

            if (serverInfo == null || !serverInfo.IsHealthy)
            {
                _logger.LogInformation("[GrpcAdapter] 🚀 Starting Python gRPC server");
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [PHASE5_START_SERVER_BEFORE] StartServerAsync呼び出し直前\r\n");
                serverInfo = await _serverManager.StartServerAsync(GrpcServerLanguagePair).ConfigureAwait(false);
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [PHASE5_START_SERVER_AFTER] StartServerAsync完了 - serverInfo==null: {serverInfo == null}\r\n");

                if (serverInfo != null)
                {
                    _logger.LogInformation("[GrpcAdapter] ✅ Python gRPC server started on port {Port}", serverInfo.Port);
                }
                else
                {
                    _logger.LogWarning("[GrpcAdapter] ⚠️ Failed to start Python gRPC server");
                }
            }
            else
            {
                _logger.LogInformation("[GrpcAdapter] ✅ Python gRPC server already running on port {Port}", serverInfo.Port);
            }

            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [PHASE5_SET_ENSURED] _serverEnsured = true 設定直前\r\n");
            _serverEnsured = true;
        }
        finally
        {
            _serverLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<TranslationResponse> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        // 🔥 [PHASE3.1_FIX] 翻訳実行前にサーバー起動を確認
        await EnsureServerStartedAsync().ConfigureAwait(false);

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
        // 🚨🚨🚨 [ULTRA_ULTRA_CRITICAL] 絶対に実行される診断ログ（最優先）
        var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log");
        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🚨🚨🚨 [GRPC_ADAPTER_ENTRY] TranslateBatchAsync ENTRY - Count: {requests?.Count ?? -1}\r\n");
        Console.WriteLine($"🚨🚨🚨 [GRPC_ADAPTER_ENTRY] TranslateBatchAsync ENTRY - Count: {requests?.Count ?? -1}");

        // 🔧 [ULTRAPHASE5] Line 204到達確認
        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [PHASE5_L204] Line 204到達\r\n");

        // 🔧 [ULTRAPHASE4_L3] Entry診断 (Gemini推奨: ILogger使用)
        _logger.LogDebug("[L3_ENTRY] GrpcAdapter.TranslateBatchAsync ENTRY. ThreadId: {ThreadId}", Environment.CurrentManagedThreadId);

        // 🔧 [ULTRAPHASE5] Line 207到達確認
        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [PHASE5_L207] ObjectDisposedException.ThrowIf呼び出し直前\r\n");
        ObjectDisposedException.ThrowIf(_disposed, this);
        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [PHASE5_L208] Disposed check passed\r\n");
        _logger.LogDebug("[L3_CHECK] Disposed check passed. ThreadId: {ThreadId}", Environment.CurrentManagedThreadId);

        // 🔧 [ULTRAPHASE5] Line 210到達確認
        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [PHASE5_L210] ArgumentNullException.ThrowIfNull呼び出し直前\r\n");
        ArgumentNullException.ThrowIfNull(requests);
        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [PHASE5_L211] Null check passed - Count: {requests?.Count ?? 0}\r\n");
        _logger.LogDebug("[L3_CHECK] Null check passed - Count: {Count}. ThreadId: {ThreadId}", requests?.Count ?? 0, Environment.CurrentManagedThreadId);

        if (requests.Count == 0)
        {
            _logger.LogDebug("[L3_RETURN] Empty requests - returning empty array. ThreadId: {ThreadId}", Environment.CurrentManagedThreadId);
            return Array.Empty<TranslationResponse>();
        }

        // 🔥 [PHASE3.1_FIX] 翻訳実行前にサーバー起動を確認
        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [PHASE5_L220] EnsureServerStartedAsync呼び出し直前\r\n");
        _logger.LogDebug("[L3_STEP] EnsureServerStartedAsync呼び出し直前. ThreadId: {ThreadId}", Environment.CurrentManagedThreadId);
        await EnsureServerStartedAsync().ConfigureAwait(false);
        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [PHASE5_L222] EnsureServerStartedAsync完了\r\n");
        _logger.LogDebug("[L3_STEP] EnsureServerStartedAsync完了. ThreadId: {ThreadId}", Environment.CurrentManagedThreadId);

        // 🔥 [PHASE3.1_DEBUG] 必ず出力される詳細ログ
        Console.WriteLine($"🔥 [GrpcAdapter] TranslateBatchAsync開始 - リクエスト数: {requests.Count}");
        for (int i = 0; i < requests.Count; i++)
        {
            Console.WriteLine($"🔥 [GrpcAdapter] Request[{i}]: {requests[i].SourceLanguage.Code} → {requests[i].TargetLanguage.Code}, Text: '{requests[i].SourceText}'");
        }
        System.IO.File.AppendAllText(
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log"),
            $"[{DateTime.Now:HH:mm:ss.fff}] 🔥 [GrpcAdapter] TranslateBatchAsync - Count: {requests.Count}\r\n"
        );

        _logger.LogDebug("[GrpcAdapter] Batch translation: {Count} requests", requests.Count);

        try
        {
            _logger.LogDebug("[L3_STEP] Task.WhenAll実行直前. ThreadId: {ThreadId}", Environment.CurrentManagedThreadId);
            // 🔧 [PHASE3.1] 各リクエストを並行実行（Task.WhenAll）
            // Note: GrpcTranslationClientにTranslateBatchAsyncメソッドが実装されたら切り替え
            var tasks = requests.Select(request => TranslateAsync(request, cancellationToken));
            var responses = await Task.WhenAll(tasks).ConfigureAwait(false);
            _logger.LogDebug("[L3_STEP] Task.WhenAll完了 - 結果数: {ResponseCount}. ThreadId: {ThreadId}", responses?.Length ?? 0, Environment.CurrentManagedThreadId);

            // 🔥 [PHASE3.1_DEBUG] 翻訳結果ログ
            Console.WriteLine($"🔥 [GrpcAdapter] TranslateBatchAsync完了 - 成功: {responses.Count(r => r.IsSuccess)}/{responses.Length}");
            for (int i = 0; i < responses.Length; i++)
            {
                Console.WriteLine($"🔥 [GrpcAdapter] Response[{i}]: IsSuccess={responses[i].IsSuccess}, TranslatedText='{responses[i].TranslatedText}'");
            }

            _logger.LogDebug(
                "[GrpcAdapter] Batch translation completed: {SuccessCount}/{TotalCount} successful",
                responses.Count(r => r.IsSuccess),
                responses.Length
            );

            _logger.LogDebug("[L3_RETURN] 正常リターン - 結果数: {ResponseCount}. ThreadId: {ThreadId}", responses.Length, Environment.CurrentManagedThreadId);
            return responses;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[L3_CATCH] 例外発生 - ExceptionType: {ExceptionType}. ThreadId: {ThreadId}", ex.GetType().Name, Environment.CurrentManagedThreadId);
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

            // 🔥 [PHASE3.1_FIX] Pythonサーバーを自動起動
            if (_serverManager != null)
            {
                _logger.LogInformation("[GrpcAdapter] Starting Python gRPC server via ServerManager");

                // gRPCサーバーは単一サーバーがすべての言語ペアを処理するため、固定の識別子を使用
                const string GrpcServerLanguagePair = "grpc-all";
                var serverInfo = await _serverManager.GetServerAsync(GrpcServerLanguagePair).ConfigureAwait(false);

                if (serverInfo == null)
                {
                    _logger.LogInformation("[GrpcAdapter] gRPC server not found, starting new instance");
                    serverInfo = await _serverManager.StartServerAsync(GrpcServerLanguagePair).ConfigureAwait(false);
                }

                if (serverInfo == null || !serverInfo.IsHealthy)
                {
                    _logger.LogWarning("[GrpcAdapter] Python gRPC server failed to start or is unhealthy");
                    return false;
                }

                _logger.LogInformation("[GrpcAdapter] Python gRPC server started successfully on port {Port}", serverInfo.Port);
            }
            else
            {
                _logger.LogInformation("[GrpcAdapter] No ServerManager provided - expecting externally managed server");
            }

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

            _serverLock?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GrpcAdapter] Error disposing resources");
        }

        _disposed = true;
    }
}
