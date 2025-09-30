using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Patterns;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation.Common;
using Baketa.Core.Translation.Exceptions;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Translation.Local.ConnectionPool;
using Baketa.Infrastructure.Translation.Models;
using Baketa.Infrastructure.Patterns;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Baketa.Infrastructure.ResourceManagement;
using Baketa.Core.Utilities; // DebugLogUtility用
using Baketa.Infrastructure.Translation.Cloud; // GeminiTranslationEngine用
using ResourceTranslationRequest = Baketa.Infrastructure.ResourceManagement.TranslationRequest;
using CoreTranslationRequest = Baketa.Core.Translation.Models.TranslationRequest;

namespace Baketa.Infrastructure.Translation.Local;

/// <summary>
/// 最適化された高速Python翻訳エンジン（目標: 500ms以下）
/// Issue #147 Phase 5: 動的ポート対応とサーバー管理統合
/// </summary>
public class OptimizedPythonTranslationEngine : ITranslationEngine
{
    private readonly ILogger<OptimizedPythonTranslationEngine> _logger;
    private readonly SemaphoreSlim _serverLock = new(1, 1);
    // Phase 1.5: バッチ並列度制限を削除 - appsettings.jsonのMaxConnections制御で十分
    private readonly IConnectionPool? _connectionPool; // Issue #147: 接続プール統合（動的ポートモードではnull）
    private readonly ILanguageConfigurationService _languageConfig; // Issue #147: 動的設定管理
    private readonly IPythonServerManager? _serverManager; // Phase 5: 動的ポート対応
    private readonly ICircuitBreaker<TranslationResponse>? _circuitBreaker; // Phase 2: サーキットブレーカー統合
    private readonly IResourceManager? _resourceManager; // Phase 2: ハイブリッドリソース管理統合
    private readonly GeminiTranslationEngine? _fallbackEngine; // 🆕 Gemini推奨: フォールバック翻訳エンジン

    // 🚀 UltraPhase 14.25: stdin/stdout通信クライアント（ハイブリッドアーキテクチャ）
    private ITranslationClient? _translationClient; // StdinStdoutTranslationClient instance

    // サーバープロセス管理（Phase 5以降はPythonServerManagerが管理）
    private Process? _serverProcess;
    private IPythonServerInfo? _managedServerInstance;
    
    // パフォーマンス監視
    // 🚨 CACHE_DISABLED: キャッシュ汚染問題根本解決のためキャッシュ機能完全無効化
    // private readonly ConcurrentDictionary<string, TranslationMetrics> _metricsCache = new();
    private long _totalRequests;
    private long _totalProcessingTimeMs;
    private readonly Stopwatch _uptimeStopwatch = new();
    
    // モデルロード完了待機機構
    private readonly TaskCompletionSource<bool> _modelLoadCompletion = new();
    private volatile bool _isModelLoaded = false;
    private readonly object _initializationLock = new();

    // 🆕 Gemini推奨: 指数バックオフ再起動機構
    private int _restartAttempts = 0;
    private readonly int _maxRestartAttempts = 5;
    private DateTime? _lastRestartTime;

    // 🆕 接続プール制御設定
    private readonly CircuitBreakerSettings _circuitBreakerSettings;
    
    // 設定
    private const string ServerHost = "127.0.0.1";
    private int _serverPort = 5556; // 動的ポート（NLLB-200専用: 5556）
    private const int ConnectionTimeoutMs = 10000; // 接続タイムアウトを10秒に延長
    private const int StartupTimeoutMs = 60000; // 起動タイムアウトを60秒に延長（モデルロード考慮）
    private const int HealthCheckIntervalMs = 30000; // ヘルスチェック間隔
    private readonly int _translationTimeoutMs; // CircuitBreakerから取得する翻訳タイムアウト（デフォルト120秒）
    
    // Python実行パス
    private readonly string _pythonPath;
    private string _serverScriptPath = string.Empty; // 動的設定のため読み取り専用を削除
    
    public string Name => "NLLB200";
    public string Description => "高速化されたPython翻訳エンジン（500ms目標）";
    public bool RequiresNetwork => false;

    public OptimizedPythonTranslationEngine(
        ILogger<OptimizedPythonTranslationEngine> logger,
        IConnectionPool? connectionPool,
        ILanguageConfigurationService languageConfig,
        IPythonServerManager? serverManager = null,
        ICircuitBreaker<TranslationResponse>? circuitBreaker = null,
        IResourceManager? resourceManager = null,
        IOptions<CircuitBreakerSettings>? circuitBreakerSettings = null,
        GeminiTranslationEngine? fallbackEngine = null)
    {
        // 🔍 UltraPhase 10.11: Gemini推奨 - コンストラクタ開始ログ
        Console.WriteLine("🔥 [CONSTRUCTOR_START] OptimizedPythonTranslationEngine コンストラクタ開始");

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Console.WriteLine("🔍 [CONSTRUCTOR_1] _logger 初期化完了");

        _connectionPool = connectionPool; // null許容（単発接続モード用）
        Console.WriteLine("🔍 [CONSTRUCTOR_2] _connectionPool 初期化完了");

        _languageConfig = languageConfig ?? throw new ArgumentNullException(nameof(languageConfig));
        Console.WriteLine("🔍 [CONSTRUCTOR_3] _languageConfig 初期化完了");
        _serverManager = serverManager; // null許容（既存の固定ポートモードとの互換性）
        _circuitBreaker = circuitBreaker; // null許容（サーキットブレーカー無効化時）
        _resourceManager = resourceManager; // null許容（レガシー互換性維持）
        _fallbackEngine = fallbackEngine; // 🆕 Gemini推奨: フォールバック翻訳エンジン（null許容）

        Console.WriteLine("🔍 [CONSTRUCTOR_4] 依存関係注入完了");

        // 🆕 Gemini推奨: 接続プール制御設定の初期化
        _circuitBreakerSettings = circuitBreakerSettings?.Value ?? new CircuitBreakerSettings();
        Console.WriteLine("🔍 [CONSTRUCTOR_5] _circuitBreakerSettings 初期化完了");

        // 🆕 CircuitBreakerSettings からタイムアウト設定を取得
        _translationTimeoutMs = _circuitBreakerSettings.TimeoutMs;
        _logger.LogInformation("🔧 [TIMEOUT_CONFIG] 翻訳タイムアウト設定: {TimeoutMs}ms (接続プール有効: {PoolEnabled})",
            _translationTimeoutMs, _circuitBreakerSettings.EnableConnectionPool);

        Console.WriteLine("🔍 [CONSTRUCTOR_6] タイムアウト設定完了");

        // Python実行環境設定（py launcherを使用）
        _pythonPath = "py";
        Console.WriteLine("🔍 [CONSTRUCTOR_7] _pythonPath 設定完了");
        
        // プロジェクトルート検索
        Console.WriteLine("🔍 [CONSTRUCTOR_8] プロジェクトルート検索開始");
        var currentDir = Directory.GetCurrentDirectory();
        Console.WriteLine($"🔍 [CONSTRUCTOR_9] CurrentDir: {currentDir}");
        var projectRoot = FindProjectRoot(currentDir);
        Console.WriteLine($"🔍 [CONSTRUCTOR_10] ProjectRoot: {projectRoot}");
        
        // 🎯 [NLLB-200] 動的ポート設定と動的スクリプトパス設定
        _logger.LogInformation("🔍 [UltraThink Phase 13] ConfigureServerSettings 呼び出し直前");
        try
        {
            ConfigureServerSettings(projectRoot);
            _logger.LogInformation("🔍 [UltraThink Phase 13] ConfigureServerSettings 呼び出し完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [UltraThink Phase 13] ConfigureServerSettings で例外発生");
            throw;
        }
        
        _logger.LogInformation("OptimizedPythonTranslationEngine初期化 - Python: {PythonPath}, Script: {ScriptPath}",
            _pythonPath, _serverScriptPath);

        _logger.LogInformation("モデルロード待機機構を初期化しました");

        Console.WriteLine("🔍 [CONSTRUCTOR_11] ConfigureServerSettings 完了");

        // 🔧 Phase 2.2.2: バックグラウンド初期化を削除
        // TranslationModelLoaderからの明示的InitializeAsync()呼び出しのみに統一
        // 理由: 構築子のTask.RunとTranslationModelLoader.InitializeAsync()が競合し、
        //       MarkModelAsLoaded()が複数回呼ばれる問題を防止

        _uptimeStopwatch.Start();
        Console.WriteLine("🔍 [CONSTRUCTOR_12] _uptimeStopwatch 開始完了");

        // 🚀 UltraPhase 14.25: StdinStdoutTranslationClient 初期化
        if (_serverManager != null)
        {
            // 🎯 UltraThink Phase 3: 動的言語ペア取得
            var currentLanguagePair = _languageConfig.GetCurrentLanguagePair();
            var languagePairKey = $"{currentLanguagePair.SourceCode}-{currentLanguagePair.TargetCode}";

            _translationClient = new StdinStdoutTranslationClient(
                _serverManager,
                languagePairKey, // 動的取得された言語ペア (例: "en-ja")
                logger); // ILogger<OptimizedPythonTranslationEngine> を直接渡す

            _logger.LogInformation("🚀 [UltraPhase 14.25] StdinStdoutTranslationClient 初期化完了");
            Console.WriteLine("🚀 [UltraPhase 14.25] StdinStdoutTranslationClient 初期化完了");
        }
        else
        {
            _logger.LogWarning("⚠️ [UltraPhase 14.25] PythonServerManager が null のため StdinStdoutTranslationClient を初期化できません");
        }

        // 🔍 UltraPhase 10.11: Gemini推奨 - コンストラクタ完了ログ
        Console.WriteLine("🔥 [CONSTRUCTOR_END] OptimizedPythonTranslationEngine コンストラクタ完了");
    }

    public async Task<bool> InitializeAsync()
    {
        try
        {
            // 🔍 UltraPhase 9.5: _loggerハング回避のため、Console.WriteLineを最優先
            Console.WriteLine("🔥 [ENGINE_INIT_START] OptimizedPythonTranslationEngine.InitializeAsync() 開始");

            // 🔧 Phase 2.2.5: InitializeAsync()実行パス詳細トレース
            // _logger.LogInformation("🔥 [INIT_TRACE] InitializeAsync() 開始"); // UltraPhase 9.5: ハング原因のため無効化

            // 🔧 [DEBUG] _translationClient状態確認
            Console.WriteLine($"🔍 [DEBUG] InitializeAsync開始時の_translationClient状態: {(_translationClient != null ? "NOT NULL" : "NULL")}");
            _logger.LogInformation($"🔍 [DEBUG] InitializeAsync開始時の_translationClient状態: {(_translationClient != null ? "NOT NULL" : "NULL")}");
            if (_translationClient != null)
            {
                Console.WriteLine($"🔍 [DEBUG] _translationClient型: {_translationClient.GetType().Name}");
                _logger.LogInformation($"🔍 [DEBUG] _translationClient型: {_translationClient.GetType().Name}");
            }

            // 🔧 UltraThink修正: _translationClientがnullの場合のフォールバック初期化（先頭移動）
            if (_translationClient == null && _serverManager != null)
            {
                try
                {
                    // 🎯 UltraThink Phase 3: フォールバック時も動的言語ペア取得
                    var currentLanguagePair = _languageConfig.GetCurrentLanguagePair();
                    var languagePairKey = $"{currentLanguagePair.SourceCode}-{currentLanguagePair.TargetCode}";

                    _translationClient = new StdinStdoutTranslationClient(
                        _serverManager,
                        languagePairKey, // 動的取得された言語ペア (例: "en-ja")
                        _logger); // ILogger<OptimizedPythonTranslationEngine> を直接渡す

                    _logger.LogInformation("🚀 [UltraThink修正] フォールバック StdinStdoutTranslationClient 初期化完了");
                    Console.WriteLine("🚀 [UltraThink修正] フォールバック StdinStdoutTranslationClient 初期化完了");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [UltraThink修正] フォールバック StdinStdoutTranslationClient 初期化失敗: {Message}", ex.Message);
                }
            }

            // 🚀 UltraPhase 14.25: StdinStdoutTranslationClient 使用時は サーバー起動をスキップ（先頭移動）
            if (_translationClient != null)
            {
                _logger.LogInformation("🚀 [UltraPhase 14.25] StdinStdoutTranslationClient 利用可能 - サーバー起動をスキップ");
                Console.WriteLine("🔧 [CRITICAL DEBUG] StdinStdoutTranslationClient利用可能 - 早期リターン実行");
                _logger.LogInformation("🚀 [UltraPhase 14.25] stdin/stdout通信モードで初期化完了");
                Console.WriteLine("🔧 [UltraThink DEBUG] MarkModelAsLoaded()呼び出し");
                MarkModelAsLoaded(); // 🔧 UltraThink修正: TaskCompletionSource完了シグナルとロック機構の活用
                Console.WriteLine($"🔧 [UltraThink DEBUG] MarkModelAsLoaded完了 - _isModelLoaded = {_isModelLoaded}");
                Console.WriteLine("🔧 [UltraThink DEBUG] 早期リターン実行 - return true");
                return true; // 早期リターン - サーバー起動とTCP接続確認は不要
            }

            // 🔧 [GEMINI_REVIEW] 設定ファイルベースの接続プール制御
            // 🆕 Gemini推奨: 設定ファイルベースの接続プール制御
            var useConnectionPool = _circuitBreakerSettings.EnableConnectionPool;
            var useExternalServer = false; // 固定値使用

            _logger.LogInformation($"🔧 [CONFIG] UseConnectionPool: {useConnectionPool}, UseExternalServer: {useExternalServer}");

            // Issue #147: 外部サーバー使用設定の確認
            if (useExternalServer)
            {
                _logger.LogInformation("外部Pythonサーバー使用モード - プロセス起動をスキップ");
            }
            else
            {
                _logger.LogInformation("永続化Pythonサーバー起動開始");

                // 既存サーバープロセスをクリーンアップ
                await CleanupExistingProcessesAsync().ConfigureAwait(false);

                // サーバー起動
                _logger.LogInformation("🔥 [INIT_TRACE] StartOptimizedServerAsync() 呼び出し開始");
                var serverStartResult = await StartOptimizedServerAsync().ConfigureAwait(false);
                _logger.LogInformation("🔥 [INIT_TRACE] StartOptimizedServerAsync() 結果: {Result}", serverStartResult);

                if (!serverStartResult)
                {
                    _logger.LogError("🔥 [INIT_TRACE] サーバー起動失敗により InitializeAsync() 終了");
                    return false;
                }
                _logger.LogInformation("🔥 [INIT_TRACE] サーバー起動成功 - 接続確認フェーズへ");
            }

            // 🔧 UltraThink修正: _translationClientがnullの場合のフォールバック初期化
            if (_translationClient == null && _serverManager != null)
            {
                try
                {
                    // 🎯 UltraThink Phase 3: フォールバック時も動的言語ペア取得
                    var currentLanguagePair = _languageConfig.GetCurrentLanguagePair();
                    var languagePairKey = $"{currentLanguagePair.SourceCode}-{currentLanguagePair.TargetCode}";

                    _translationClient = new StdinStdoutTranslationClient(
                        _serverManager,
                        languagePairKey, // 動的取得された言語ペア (例: "en-ja")
                        _logger); // ILogger<OptimizedPythonTranslationEngine> を直接渡す

                    _logger.LogInformation("🚀 [UltraThink修正] フォールバック StdinStdoutTranslationClient 初期化完了");
                    Console.WriteLine("🚀 [UltraThink修正] フォールバック StdinStdoutTranslationClient 初期化完了");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [UltraThink修正] フォールバック StdinStdoutTranslationClient 初期化失敗: {Message}", ex.Message);
                }
            }

            // 🚀 UltraPhase 14.25: StdinStdoutTranslationClient 使用時は TCP接続確認をスキップ
            if (_translationClient != null)
            {
                _logger.LogInformation("🚀 [UltraPhase 14.25] StdinStdoutTranslationClient 利用可能 - TCP接続確認をスキップ");
                Console.WriteLine("🔧 [CRITICAL DEBUG] 242行目ログ出力直後");
                Console.WriteLine("🔧 [UltraThink DEBUG] 243行目実行前");
                _logger.LogInformation("🚀 [UltraPhase 14.25] stdin/stdout通信モードで初期化完了");
                Console.WriteLine("🔧 [UltraThink DEBUG] 244行目実行前 - MarkModelAsLoaded()呼び出し");
                MarkModelAsLoaded(); // 🔧 UltraThink修正: TaskCompletionSource完了シグナルとロック機構の活用
                Console.WriteLine($"🔧 [UltraThink DEBUG] 244行目実行後 - _isModelLoaded = {_isModelLoaded}");
                Console.WriteLine("🔧 [UltraThink DEBUG] 245行目実行前 - return true");
                return true; // 早期リターン - TCP接続確認は不要
            }

            // 接続確認（Gemini推奨：リトライロジック付き）- レガシーTCP モードのみ
            try
            {
                _logger.LogInformation("⚠️ [LEGACY] TCP接続確認モード - StdinStdoutTranslationClient が null");

                if (useConnectionPool && _connectionPool != null)
                {
                    using var testCts = new CancellationTokenSource(5000);
                    var testConnection = await _connectionPool.GetConnectionAsync(testCts.Token).ConfigureAwait(false);
                    await _connectionPool.ReturnConnectionAsync(testConnection, testCts.Token).ConfigureAwait(false);
                    _logger.LogInformation("接続プール経由でサーバー接続を確認");
                }
                else
                {
                    // 🆕 Gemini推奨：指数バックオフ付きサーバー健全性確認
                    if (!await EnsureServerHealthyWithBackoffAsync().ConfigureAwait(false))
                    {
                        _logger.LogError("🚨 [EXPONENTIAL_BACKOFF] 指数バックオフ再起動機構でも復旧できませんでした");
                        return false;
                    }
                    _logger.LogInformation("🆕 指数バックオフ機構によるサーバー健全性確認完了");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "サーバー接続確認失敗");
                return false;
            }
            
            // ヘルスチェックタスク開始
            _ = Task.Run(async () => await MonitorServerHealthAsync().ConfigureAwait(false));
            
            _logger.LogInformation("🔥 [INIT_TRACE] 接続確認完了 - MarkModelAsLoaded() 呼び出し直前");
            _logger.LogInformation("OptimizedPythonTranslationEngine初期化完了");

            // モデルロード完了のシグナル
            _logger.LogInformation("🔥 [INIT_TRACE] MarkModelAsLoaded() 呼び出し開始");
            MarkModelAsLoaded();
            _logger.LogInformation("🔥 [INIT_TRACE] MarkModelAsLoaded() 呼び出し完了");

            _logger.LogInformation("🔥 [INIT_TRACE] InitializeAsync() 正常終了 - return true");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [INIT_TRACE] InitializeAsync() 例外キャッチ - 初期化エラー");

            // 初期化失敗時はモデルロード失敗を通知
            _logger.LogInformation("🔥 [INIT_TRACE] MarkModelLoadFailed() 呼び出し開始");
            MarkModelLoadFailed(ex);
            _logger.LogInformation("🔥 [INIT_TRACE] MarkModelLoadFailed() 呼び出し完了");

            _logger.LogInformation("🔥 [INIT_TRACE] InitializeAsync() 例外終了 - return false");
            return false;
        }
    }

    private async Task<bool> StartOptimizedServerAsync()
    {
        try
        {
            _logger.LogInformation("🔥 [START_TRACE] StartOptimizedServerAsync() 開始");
            await _serverLock.WaitAsync().ConfigureAwait(false);

            // Phase 5: PythonServerManagerが利用可能な場合は動的ポート管理を使用
            _logger.LogInformation("🔥 [START_TRACE] _serverManager null判定: {IsNull}", _serverManager == null);
            if (_serverManager != null)
            {
                _logger.LogInformation("🔥 [START_TRACE] StartManagedServerAsync() パス選択");
                var managedResult = await StartManagedServerAsync().ConfigureAwait(false);
                _logger.LogInformation("🔥 [START_TRACE] StartManagedServerAsync() 結果: {Result}", managedResult);
                return managedResult;
            }

            // 従来の固定ポートモード（後方互換性）
            _logger.LogInformation("🔥 [START_TRACE] StartLegacyFixedPortServerAsync() パス選択");
            var legacyResult = await StartLegacyFixedPortServerAsync().ConfigureAwait(false);
            _logger.LogInformation("🔥 [START_TRACE] StartLegacyFixedPortServerAsync() 結果: {Result}", legacyResult);
            return legacyResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [START_TRACE] StartOptimizedServerAsync() 例外発生");
            return false;
        }
        finally
        {
            _serverLock.Release();
        }
    }

    /// <summary>
    /// 🆕 Gemini推奨: 指数バックオフ付きサーバー再起動機構
    /// 再起動ループを防止し、段階的に待機時間を延長
    /// </summary>
    private async Task<bool> RestartServerWithBackoffAsync()
    {
        if (_restartAttempts >= _maxRestartAttempts)
        {
            _logger.LogError("🚨 最大再起動試行回数({MaxAttempts})に到達 - 手動介入が必要", _maxRestartAttempts);
            return false;
        }

        // 指数バックオフ: 2^n秒待機 (1, 2, 4, 8, 16秒)
        var delay = TimeSpan.FromSeconds(Math.Pow(2, _restartAttempts));
        _logger.LogWarning("🔄 サーバー再起動試行 {Attempt}/{Max} - {Delay}秒後に実行",
            _restartAttempts + 1, _maxRestartAttempts, delay.TotalSeconds);

        await Task.Delay(delay).ConfigureAwait(false);
        _restartAttempts++;
        _lastRestartTime = DateTime.UtcNow;

        // 既存プロセスのクリーンアップ
        await CleanupExistingProcessesAsync().ConfigureAwait(false);

        // サーバー再起動
        return await StartOptimizedServerAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 🆕 Gemini推奨: サーバー健全性確認 + 自動回復機構
    /// </summary>
    private async Task<bool> EnsureServerHealthyWithBackoffAsync()
    {
        // 直接接続テストで健全性確認
        var healthCheck = await TestDirectConnectionAsync().ConfigureAwait(false);
        if (healthCheck)
        {
            // 成功時はリトライカウンターをリセット
            _restartAttempts = 0;
            _lastRestartTime = null;
            return true;
        }

        _logger.LogWarning("🩺 サーバー健全性チェック失敗 - 再起動を試行");
        return await RestartServerWithBackoffAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// PythonServerManager経由での動的ポートサーバー起動
    /// </summary>
    private async Task<bool> StartManagedServerAsync()
    {
        try
        {
            Console.WriteLine("🚀 [UltraPhase 14.12] StartManagedServerAsync() 開始");
            _logger.LogInformation("🚀 動的ポート管理によるサーバー起動開始");

            // 動的言語ペア取得（設定から）
            var currentLanguagePair = _languageConfig.GetCurrentLanguagePair();
            var languagePairKey = $"{currentLanguagePair.SourceCode}-{currentLanguagePair.TargetCode}";

            // 🔥 STEP7 デバッグ: サーバー起動時の言語ペアキー追跡
            _logger.LogDebug("🔥 [SERVER_START] 動的言語ペア取得: Source={Source}, Target={Target}",
                currentLanguagePair.SourceCode, currentLanguagePair.TargetCode);
            _logger.LogDebug("🔥 [SERVER_START] 言語ペアキー生成: '{LanguagePairKey}'", languagePairKey);
            Console.WriteLine($"🔥 [SERVER_START] 言語ペアキー: '{languagePairKey}' でサーバー起動");

            Console.WriteLine($"🔍 [UltraPhase 14.12] _serverManager.StartServerAsync(\"{languagePairKey}\") 呼び出し直前");
            _managedServerInstance = await _serverManager!.StartServerAsync(languagePairKey).ConfigureAwait(false);
            Console.WriteLine($"✅ [UltraPhase 14.12] _serverManager.StartServerAsync(\"{languagePairKey}\") 完了");

            _logger.LogInformation("✅ 動的ポートサーバー起動完了: Port {Port}, StartedAt {StartedAt}",
                _managedServerInstance.Port, _managedServerInstance.StartedAt);

            // 接続プールのポート更新
            if (_connectionPool != null)
            {
                // TODO: 接続プールにポート変更通知メソッドを追加予定
                _logger.LogDebug("接続プール更新: Port {Port}", _managedServerInstance.Port);
            }

            Console.WriteLine("✅ [UltraPhase 14.12] StartManagedServerAsync() 正常終了");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [UltraPhase 14.12] StartManagedServerAsync() 例外: {ex.GetType().Name} - {ex.Message}");
            _logger.LogError(ex, "❌ 動的ポートサーバー起動失敗");
            return false;
        }
    }
    
    /// <summary>
    /// 従来の固定ポートサーバー起動（後方互換性）
    /// </summary>
    private async Task<bool> StartLegacyFixedPortServerAsync()
    {
        _logger.LogInformation("🔥 [LEGACY_TRACE] StartLegacyFixedPortServerAsync() 開始");
        _logger.LogInformation("🔥 [LEGACY_TRACE] Python Path: {PythonPath}", _pythonPath);
        _logger.LogInformation("🔥 [LEGACY_TRACE] Script Path: {ScriptPath}", _serverScriptPath);
        _logger.LogInformation("🔥 [LEGACY_TRACE] Server Port: {Port}", _serverPort);
        
        _logger.LogInformation("🔧 固定ポートモードでサーバー起動開始 (Port {Port})", _serverPort);
        
        // Phase 2.2.7: ファイルパス検証
        _logger.LogInformation("🔥 [LEGACY_TRACE] ファイル存在確認 - Python: {PythonExists}, Script: {ScriptExists}", 
            File.Exists(_pythonPath), File.Exists(_serverScriptPath));
        
        if (!File.Exists(_pythonPath))
        {
            _logger.LogError("🔥 [LEGACY_TRACE] Python実行ファイルが見つかりません: {PythonPath}", _pythonPath);
            return false;
        }
        
        if (!File.Exists(_serverScriptPath))
        {
            _logger.LogError("🔥 [LEGACY_TRACE] スクリプトファイルが見つかりません: {ScriptPath}", _serverScriptPath);
            return false;
        }
        
        // 直接Python実行（PowerShell経由を排除）
        var arguments = $"\"{_serverScriptPath}\" --port {_serverPort} --optimized";
        _logger.LogInformation("🔥 [LEGACY_TRACE] Process Arguments: {Arguments}", arguments);
        
        var processInfo = new ProcessStartInfo
        {
            FileName = _pythonPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        
        _logger.LogInformation("🔥 [LEGACY_TRACE] ProcessStartInfo作成完了");
        
        try
        {
            _serverProcess = new Process { StartInfo = processInfo };
            _logger.LogInformation("🔥 [LEGACY_TRACE] Process.Start()呼び出し前");
            _serverProcess.Start();
            _logger.LogInformation("🔥 [LEGACY_TRACE] Process.Start()呼び出し後 - PID: {ProcessId}", _serverProcess.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [LEGACY_TRACE] Process.Start()で例外発生: {Message}", ex.Message);
            return false;
        }
        
        _logger.LogInformation("Pythonサーバープロセス起動 - PID: {ProcessId}", _serverProcess.Id);
        
        // 🚨 Phase 1.3: 詳細エラーログ取得機能 - 標準出力・エラー監視
        _ = Task.Run(async () => await MonitorServerOutputAsync().ConfigureAwait(false));
        _ = Task.Run(async () => await MonitorServerErrorAsync().ConfigureAwait(false));
        
        _logger.LogInformation("🔥 [LEGACY_TRACE] 出力・エラー監視タスク開始");
        
        // サーバー起動待機（最大60秒、モデルロード完了まで）
        var startTime = DateTime.UtcNow;
        _logger.LogInformation("🔥 [LEGACY_TRACE] 接続テスト開始 - タイムアウト: {TimeoutMs}ms", StartupTimeoutMs);
        
        while ((DateTime.UtcNow - startTime).TotalMilliseconds < StartupTimeoutMs)
        {
            await Task.Delay(2000).ConfigureAwait(false); // ポーリング間隔を2秒に延長
            
            var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogDebug("🔥 [LEGACY_TRACE] 接続テスト中 - 経過時間: {ElapsedMs}ms", elapsedMs);
            
            try
            {
                if (_serverProcess.HasExited)
                {
                    _logger.LogError("🔥 [LEGACY_TRACE] サーバープロセスが異常終了 - ExitCode: {ExitCode}", _serverProcess.ExitCode);
                    return false;
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "🔥 [LEGACY_TRACE] サーバープロセス状態確認で例外: {Message}", ex.Message);
                return false;
            }
            
            // Issue #147: 接続テスト（タイムアウト延長）
            try
            {
                _logger.LogDebug("🔥 [LEGACY_TRACE] TestConnectionAsync()呼び出し開始");
                var connectionResult = await TestConnectionAsync().ConfigureAwait(false);
                _logger.LogDebug("🔥 [LEGACY_TRACE] TestConnectionAsync()結果: {Result}", connectionResult);
                
                if (connectionResult)
                {
                    var finalElapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    _logger.LogInformation("🔥 [LEGACY_TRACE] 接続テスト成功！起動時間: {ElapsedMs}ms", finalElapsedMs);
                    _logger.LogInformation("サーバー起動成功 - 起動時間: {ElapsedMs}ms", finalElapsedMs);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "🔥 [LEGACY_TRACE] TestConnectionAsync()で例外: {Message}", ex.Message);
                // 接続テスト失敗 - サーバーがまだ起動していない
            }
        }
        
        _logger.LogError("🔥 [LEGACY_TRACE] サーバー起動タイムアウト - 最終経過時間: {TotalMs}ms", (DateTime.UtcNow - startTime).TotalMilliseconds);
        _logger.LogError("サーバー起動タイムアウト");
        return false;
    }

    /// <summary>
    /// モデルロード完了をマーク
    /// </summary>
    private void MarkModelAsLoaded()
    {
        lock (_initializationLock)
        {
            if (!_isModelLoaded)
            {
                _isModelLoaded = true;
                _modelLoadCompletion.TrySetResult(true);
                _logger.LogInformation("🚀 モデルロード完了 - 翻訳リクエスト受付開始");
            }
        }
    }

    /// <summary>
    /// モデルロード失敗をマーク
    /// </summary>
    /// <param name="exception">失敗理由</param>
    private void MarkModelLoadFailed(Exception exception)
    {
        lock (_initializationLock)
        {
            if (!_isModelLoaded)
            {
                _modelLoadCompletion.TrySetException(exception);
                _logger.LogError(exception, "⚠️ モデルロード失敗 - 翻訳リクエストはエラーを返します");
            }
        }
    }

    /// <summary>
    /// モデルロード状態をリセット（テスト用）
    /// </summary>
    internal void ResetModelLoadState()
    {
        lock (_initializationLock)
        {
            _isModelLoaded = false;
            // 新しいTaskCompletionSourceは再初期化時に作成
        }
    }

    // Issue #147: EstablishPersistentConnectionAsyncメソッドは接続プール統合により削除
    // 接続管理は FixedSizeConnectionPool が担当

    public async Task<TranslationResponse> TranslateAsync(
        Baketa.Core.Translation.Models.TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
#if DEBUG
        // [DEBUG] TranslateAsyncメソッド入口確認
        Console.WriteLine("[DEBUG] OptimizedPythonTranslationEngine.TranslateAsync メソッドに入りました");
        DebugLogUtility.WriteLog("[DEBUG] OptimizedPythonTranslationEngine.TranslateAsync メソッドに入りました");
#endif
        
        try
        {
            // 🔥 [STEP1] 初期化チェック開始
            Console.WriteLine("🔥 [STEP1] 初期化チェック開始");
            DebugLogUtility.WriteLog("🔥 [STEP1] 初期化チェック開始");
            
            // 🔥 [TRANSLATE_DEBUG] TranslateAsyncメソッド開始デバッグ
            _logger.LogDebug("🔥 [TRANSLATE_DEBUG] TranslateAsync 呼び出し開始");
            _logger.LogDebug("🔥 [TRANSLATE_DEBUG] - RequestId: {RequestId}", request.RequestId);
            _logger.LogDebug("🔥 [TRANSLATE_DEBUG] - SourceText: '{SourceText}'", request.SourceText);
            _logger.LogDebug("🔥 [TRANSLATE_DEBUG] - SourceLanguage: {SourceLanguage}", request.SourceLanguage);
            _logger.LogDebug("🔥 [TRANSLATE_DEBUG] - TargetLanguage: {TargetLanguage}", request.TargetLanguage);
            Console.WriteLine($"🔥 [TRANSLATE_DEBUG] TranslateAsync 呼び出し開始 - RequestId: {request.RequestId}");
            Console.WriteLine($"🔥 [TRANSLATE_DEBUG] SourceText: '{request.SourceText}', {request.SourceLanguage} → {request.TargetLanguage}");
            
            // 🔥 [STEP2] Stopwatch開始
            Console.WriteLine("🔥 [STEP2] Stopwatch開始");
            DebugLogUtility.WriteLog("🔥 [STEP2] Stopwatch開始");
            
            var stopwatch = Stopwatch.StartNew();
            
            // 🔥 [STEP3] モデルロード完了まで待機
            Console.WriteLine("🔥 [STEP3] モデルロード完了まで待機");
            DebugLogUtility.WriteLog("🔥 [STEP3] モデルロード完了まで待機");
            
            // モデルロード完了まで待機（タイムアウト付き）
            _logger.LogDebug("翻訳リクエスト開始 - モデルロード待機中...");
            using var modelLoadTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(120)); // 🔧 [TIMEOUT_TEST] 30秒→120秒に延長してタイムアウト原因を確定検証
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, modelLoadTimeout.Token);
            
            try
            {
                // 🔥 [STEP4] モデルロードTask待機
                Console.WriteLine("🔥 [STEP4] モデルロードTask待機開始");
                DebugLogUtility.WriteLog("🔥 [STEP4] モデルロードTask待機開始");

                // 🔧 Phase 2.2.4: 詳細診断ログ追加
                _logger.LogInformation("🔧 [STEP4_DIAGNOSIS] モデルロード状態診断:");
                _logger.LogInformation("🔧 [STEP4_DIAGNOSIS]   _isModelLoaded: {IsModelLoaded}", _isModelLoaded);
                _logger.LogInformation("🔧 [STEP4_DIAGNOSIS]   _modelLoadCompletion.Task.IsCompleted: {IsCompleted}", _modelLoadCompletion.Task.IsCompleted);
                _logger.LogInformation("🔧 [STEP4_DIAGNOSIS]   _modelLoadCompletion.Task.Status: {Status}", _modelLoadCompletion.Task.Status);

                if (_isModelLoaded)
                {
                    _logger.LogInformation("✅ [STEP4_DIAGNOSIS] モデル既にロード完了 - 待機をスキップ");
                    Console.WriteLine("✅ [STEP4_DIAGNOSIS] モデル既にロード完了 - 待機をスキップ");
                }
                else
                {
                    _logger.LogInformation("⏳ [STEP4_DIAGNOSIS] モデル未ロード - Task待機実行");
                    Console.WriteLine("⏳ [STEP4_DIAGNOSIS] モデル未ロード - Task待機実行");
                    await _modelLoadCompletion.Task.WaitAsync(combinedCts.Token).ConfigureAwait(false);
                }
                
                // 🔥 [STEP5] モデルロード待機成功
                Console.WriteLine("🔥 [STEP5] モデルロード待機成功");
                DebugLogUtility.WriteLog("🔥 [STEP5] モデルロード待機成功");
                
                _logger.LogDebug("モデルロード完了 - 翻訳処理開始");
            }
            catch (OperationCanceledException) when (modelLoadTimeout.Token.IsCancellationRequested)
            {
                // 🔥 [STEP_ERROR] モデルロードタイムアウト
                Console.WriteLine("🔥 [STEP_ERROR] モデルロードタイムアウト発生");
                DebugLogUtility.WriteLog("🔥 [STEP_ERROR] モデルロードタイムアウト発生");
                
                _logger.LogWarning("モデルロード待機タイムアウト（30秒） - 初期化を試行します");
                // タイムアウト時は初期化を試行
            }
            catch (OperationCanceledException)
            {
                // 🔥 [STEP_ERROR] 翻訳リクエストキャンセル
                Console.WriteLine("🔥 [STEP_ERROR] 翻訳リクエストキャンセル");
                DebugLogUtility.WriteLog("🔥 [STEP_ERROR] 翻訳リクエストキャンセル");
                
                _logger.LogDebug("翻訳リクエストがキャンセルされました");
                throw;
            }
            
            // 🔥 [STEP6] IsReadyAsync確認
            Console.WriteLine("🔥 [STEP6] IsReadyAsync確認開始");
            DebugLogUtility.WriteLog("🔥 [STEP6] IsReadyAsync確認開始");
            
            // 初期化確認（テスト環境では迅速に失敗）
            if (!await IsReadyAsync().ConfigureAwait(false))
            {
                // 🔥 [STEP7] IsReady失敗 - 初期化が必要
                Console.WriteLine("🔥 [STEP7] IsReady失敗 - 初期化が必要");
                DebugLogUtility.WriteLog("🔥 [STEP7] IsReady失敗 - 初期化が必要");
                
                // テスト環境やサーバーなし環境では初期化を試行しない
                if (!File.Exists(_serverScriptPath))
                {
                    // 🔥 [STEP_ERROR] サーバースクリプトが見つからない
                    Console.WriteLine($"🔥 [STEP_ERROR] サーバースクリプトが見つからない: {_serverScriptPath}");
                    DebugLogUtility.WriteLog($"🔥 [STEP_ERROR] サーバースクリプトが見つからない: {_serverScriptPath}");
                    
                    _logger.LogWarning("サーバースクリプトが見つかりません: {ScriptPath}", _serverScriptPath);
                    var error = TranslationError.Create(
                        TranslationError.ServiceUnavailable, 
                        $"翻訳サーバースクリプトが見つかりません: {_serverScriptPath}",
                        false, 
                        TranslationErrorType.ServiceUnavailable);
                    return TranslationResponse.CreateError(request, error, Name);
                }
                
                // 🔥 [STEP8] InitializeAsync実行
                Console.WriteLine("🔥 [STEP8] InitializeAsync実行開始");
                DebugLogUtility.WriteLog("🔥 [STEP8] InitializeAsync実行開始");
                
                var initResult = await InitializeAsync().ConfigureAwait(false);
                if (!initResult)
                {
                    // 🔥 [STEP_ERROR] 初期化失敗
                    Console.WriteLine("🔥 [STEP_ERROR] InitializeAsync失敗");
                    DebugLogUtility.WriteLog("🔥 [STEP_ERROR] InitializeAsync失敗");
                    
                    var error = TranslationError.Create(
                        TranslationError.ServiceUnavailable, 
                        "翻訳サーバーの初期化に失敗しました",
                        true, 
                        TranslationErrorType.ServiceUnavailable);
                    return TranslationResponse.CreateError(request, error, Name);
                }
            }
            else
            {
                // 🔥 [STEP6_OK] IsReady成功
                Console.WriteLine("🔥 [STEP6_OK] IsReady成功 - サーバー準備完了");
                DebugLogUtility.WriteLog("🔥 [STEP6_OK] IsReady成功 - サーバー準備完了");
            }

            // 🔥 [STEP9] 言語ペアサポート確認
            Console.WriteLine("🔥 [STEP9] 言語ペアサポート確認開始");
            DebugLogUtility.WriteLog("🔥 [STEP9] 言語ペアサポート確認開始");
            
            // 言語ペアのサポート確認
            var languagePair = new LanguagePair 
            { 
                SourceLanguage = request.SourceLanguage, 
                TargetLanguage = request.TargetLanguage 
            };
            bool isSupported = await SupportsLanguagePairAsync(languagePair).ConfigureAwait(false);
            if (!isSupported)
            {
                // 🔥 [STEP_ERROR] 言語ペアサポートなし
                Console.WriteLine($"🔥 [STEP_ERROR] 言語ペアサポートなし: {request.SourceLanguage.Code}-{request.TargetLanguage.Code}");
                DebugLogUtility.WriteLog($"🔥 [STEP_ERROR] 言語ペアサポートなし: {request.SourceLanguage.Code}-{request.TargetLanguage.Code}");
                
                var error = TranslationError.Create(
                    TranslationError.UnsupportedLanguagePair, 
                    $"言語ペア {request.SourceLanguage.Code}-{request.TargetLanguage.Code} はサポートされていません",
                    false, 
                    TranslationErrorType.UnsupportedLanguage);
                return TranslationResponse.CreateError(request, error, Name);
            }
            
            // 🔥 [STEP10] キャッシュ無効化モード
            Console.WriteLine("🔥 [STEP10] キャッシュ無効化モード");
            DebugLogUtility.WriteLog("🔥 [STEP10] キャッシュ無効化モード");
            
            // 🚨 CACHE_DISABLED: キャッシュ機能完全無効化 - 汚染問題根本解決
            // キャッシュチェック処理を完全削除
            _logger.LogDebug("キャッシュ無効化モード - 常に新鮮な翻訳を実行");
            
            // 🔥 [STEP11] HybridResourceManager確認
            Console.WriteLine($"🔥 [STEP11] HybridResourceManager確認 - _resourceManager != null: {_resourceManager != null}");
            DebugLogUtility.WriteLog($"🔥 [STEP11] HybridResourceManager確認 - _resourceManager != null: {_resourceManager != null}");
            
            // Phase 3.2統合: HybridResourceManager経由でVRAMモニタリング付き翻訳実行
            TranslationResponse result;
            if (_resourceManager != null)
            {
                // 🔥 [STEP12] HybridResourceManager使用
                Console.WriteLine("🔥 [STEP12] HybridResourceManager使用");
                DebugLogUtility.WriteLog("🔥 [STEP12] HybridResourceManager使用");
                
                _logger.LogInformation("🚀 [PHASE3.2] HybridResourceManager経由でVRAMモニタリング付き翻訳実行開始");
                
                // 🎯 Phase 3.2: HybridResourceManagerの初期化を確実に実行
                try 
                {
                    if (!_resourceManager.IsInitialized)
                    {
                        // 🔥 [STEP13] HybridResourceManager初期化
                        Console.WriteLine("🔥 [STEP13] HybridResourceManager初期化開始");
                        DebugLogUtility.WriteLog("🔥 [STEP13] HybridResourceManager初期化開始");
                        
                        _logger.LogInformation("🔧 [PHASE3.2] HybridResourceManager初期化実行中...");
                        await _resourceManager.InitializeAsync(cancellationToken).ConfigureAwait(false);
                        _logger.LogInformation("✅ [PHASE3.2] HybridResourceManager初期化完了 - VRAMモニタリング開始");
                    }
                    else
                    {
                        // 🔥 [STEP13_OK] HybridResourceManager既に初期化済み
                        Console.WriteLine("🔥 [STEP13_OK] HybridResourceManager既に初期化済み");
                        DebugLogUtility.WriteLog("🔥 [STEP13_OK] HybridResourceManager既に初期化済み");
                        
                        _logger.LogDebug("✅ [PHASE3.2] HybridResourceManager既に初期化済み");
                    }
                }
                catch (Exception initEx)
                {
                    // 🔥 [STEP_ERROR] HybridResourceManager初期化失敗
                    Console.WriteLine($"🔥 [STEP_ERROR] HybridResourceManager初期化失敗: {initEx.Message}");
                    DebugLogUtility.WriteLog($"🔥 [STEP_ERROR] HybridResourceManager初期化失敗: {initEx.Message}");
                    
                    _logger.LogError(initEx, "❌ [PHASE3.2] HybridResourceManager初期化失敗: {Message}", initEx.Message);
                }
                
                // 🔥 [STEP14] ProcessTranslationAsync実行
                Console.WriteLine("🔥 [STEP14] ProcessTranslationAsync実行開始");
                DebugLogUtility.WriteLog("🔥 [STEP14] ProcessTranslationAsync実行開始");
                
                _logger.LogDebug("🔧 [HYBRID_RESOURCE_MANAGER] HybridResourceManager経由で翻訳実行開始");
                
                var translationRequest = new ResourceTranslationRequest(
                    Text: request.SourceText,
                    SourceLanguage: request.SourceLanguage.Code,
                    TargetLanguage: request.TargetLanguage.Code,
                    OperationId: request.RequestId.ToString(),
                    Timestamp: DateTime.UtcNow
                );

                result = await _resourceManager.ProcessTranslationAsync(
                    async (req, ct) =>
                    {
                        // 🔥 [STEP15] 内部翻訳処理実行
                        Console.WriteLine("🔥 [STEP15] 内部翻訳処理実行開始");
                        DebugLogUtility.WriteLog("🔥 [STEP15] 内部翻訳処理実行開始");
                        
                        _logger.LogDebug("🔧 [HYBRID_RESOURCE_MANAGER] 翻訳処理実行中 - OperationId: {OperationId}", req.OperationId);
                        
                        // サーキットブレーカーによる翻訳実行（既存ロジック保持）
                        if (_circuitBreaker != null)
                        {
                            // 🔥 [STEP16] サーキットブレーカー使用
                            Console.WriteLine("🔥 [STEP16] サーキットブレーカー使用");
                            DebugLogUtility.WriteLog("🔥 [STEP16] サーキットブレーカー使用");

                            // 🔥 [ULTRA_DEBUG] サーキットブレーカー呼び出し前
                            Console.WriteLine($"🔥 [ULTRA_DEBUG_PRE_CB] ExecuteAsync呼び出し直前 - RequestId: {request.RequestId}");
                            DebugLogUtility.WriteLog($"🔥 [ULTRA_DEBUG_PRE_CB] ExecuteAsync呼び出し直前 - RequestId: {request.RequestId}");

                            var cbResult = await _circuitBreaker.ExecuteAsync(
                                async cbt => {
                                    // 🔥 [ULTRA_DEBUG] ラムダ関数内部に到達
                                    Console.WriteLine($"🔥 [ULTRA_DEBUG_LAMBDA] ラムダ関数実行開始 - RequestId: {request.RequestId}");
                                    DebugLogUtility.WriteLog($"🔥 [ULTRA_DEBUG_LAMBDA] ラムダ関数実行開始 - RequestId: {request.RequestId}");

                                    var lambdaResult = await TranslateWithOptimizedServerAsync(request, cbt).ConfigureAwait(false);

                                    // 🔥 [ULTRA_DEBUG] ラムダ関数結果確認
                                    Console.WriteLine($"🔥 [ULTRA_DEBUG_LAMBDA_RESULT] 翻訳結果: IsSuccess={lambdaResult.IsSuccess}, Text='{lambdaResult.TranslatedText}'");
                                    DebugLogUtility.WriteLog($"🔥 [ULTRA_DEBUG_LAMBDA_RESULT] 翻訳結果: IsSuccess={lambdaResult.IsSuccess}, Text='{lambdaResult.TranslatedText}'");

                                    return lambdaResult;
                                },
                                ct).ConfigureAwait(false);

                            // 🔥 [ULTRA_DEBUG] サーキットブレーカー呼び出し後
                            Console.WriteLine($"🔥 [ULTRA_DEBUG_POST_CB] ExecuteAsync完了 - IsSuccess: {cbResult.IsSuccess}, Text: '{cbResult.TranslatedText}'");
                            DebugLogUtility.WriteLog($"🔥 [ULTRA_DEBUG_POST_CB] ExecuteAsync完了 - IsSuccess: {cbResult.IsSuccess}, Text: '{cbResult.TranslatedText}'");

                            return cbResult;
                        }
                        else
                        {
                            // 🔥 [STEP17] TranslateWithOptimizedServerAsync直接実行
                            Console.WriteLine("🔥 [STEP17] TranslateWithOptimizedServerAsync直接実行");
                            DebugLogUtility.WriteLog("🔥 [STEP17] TranslateWithOptimizedServerAsync直接実行");
                            
                            return await TranslateWithOptimizedServerAsync(request, ct).ConfigureAwait(false);
                        }
                    },
                    translationRequest,
                    cancellationToken).ConfigureAwait(false);
                    
                // 🔥 [STEP18] ProcessTranslationAsync完了
                Console.WriteLine("🔥 [STEP18] ProcessTranslationAsync完了");
                DebugLogUtility.WriteLog("🔥 [STEP18] ProcessTranslationAsync完了");
                
                _logger.LogDebug("🔧 [HYBRID_RESOURCE_MANAGER] HybridResourceManager経由で翻訳実行完了");
            }
            else
            {
                // 🔥 [STEP19] レガシーモード
                Console.WriteLine("🔥 [STEP19] レガシーモード - HybridResourceManager無効");
                DebugLogUtility.WriteLog("🔥 [STEP19] レガシーモード - HybridResourceManager無効");
                
                // レガシーモード: HybridResourceManager無しでの従来処理
                _logger.LogDebug("🔧 [LEGACY_MODE] HybridResourceManager無効 - 従来の直接実行モード");
                
                if (_circuitBreaker != null)
                {
                    // 🔥 [STEP20] レガシー - サーキットブレーカー使用
                    Console.WriteLine("🔥 [STEP20] レガシー - サーキットブレーカー使用");
                    DebugLogUtility.WriteLog("🔥 [STEP20] レガシー - サーキットブレーカー使用");
                    
                    _logger.LogDebug("🔧 [CIRCUIT_BREAKER] サーキットブレーカー経由で翻訳実行開始");
                    result = await _circuitBreaker.ExecuteAsync(
                        async ct => await TranslateWithOptimizedServerAsync(request, ct).ConfigureAwait(false), 
                        cancellationToken).ConfigureAwait(false);
                    _logger.LogDebug("🔧 [CIRCUIT_BREAKER] サーキットブレーカー経由で翻訳実行完了");
                }
                else
                {
                    // 🔥 [STEP21] レガシー - 直接実行
                    Console.WriteLine("🔥 [STEP21] レガシー - TranslateWithOptimizedServerAsync直接実行");
                    DebugLogUtility.WriteLog("🔥 [STEP21] レガシー - TranslateWithOptimizedServerAsync直接実行");
                    
                    // サーキットブレーカー無効時は従来通り直接実行
                    _logger.LogDebug("🔥 TranslateWithOptimizedServerAsync 直接呼び出し開始");
                    result = await TranslateWithOptimizedServerAsync(request, cancellationToken).ConfigureAwait(false);
                    _logger.LogDebug("🔥 TranslateWithOptimizedServerAsync 直接呼び出し完了");
                }
            }
            
            // 🔥 [STEP22] 処理時間設定
            Console.WriteLine("🔥 [STEP22] 処理時間設定とメトリクス更新");
            DebugLogUtility.WriteLog("🔥 [STEP22] 処理時間設定とメトリクス更新");
            
            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;
            
            // 処理時間を設定
            result.ProcessingTimeMs = elapsedMs;
            
            // メトリクス更新
            Interlocked.Increment(ref _totalRequests);
            Interlocked.Add(ref _totalProcessingTimeMs, elapsedMs);
            
            // 500ms目標チェック
            if (elapsedMs > 500)
            {
                _logger.LogWarning("処理時間が目標を超過: {ElapsedMs}ms > 500ms", elapsedMs);
            }
            else
            {
                _logger.LogInformation("高速翻訳成功: {ElapsedMs}ms", elapsedMs);
            }
            
            // 🚨 CACHE_DISABLED: キャッシュ保存機能完全無効化 - 汚染問題根本解決
            // キャッシュ保存処理を完全削除
            _logger.LogDebug("キャッシュ無効化モード - 翻訳結果をキャッシュに保存しません");
            
            // 🔥 [STEP_FINAL] 成功終了
            Console.WriteLine($"🔥 [STEP_FINAL] 成功終了 - IsSuccess: {result.IsSuccess}, ProcessingTime: {elapsedMs}ms");
            DebugLogUtility.WriteLog($"🔥 [STEP_FINAL] 成功終了 - IsSuccess: {result.IsSuccess}, ProcessingTime: {elapsedMs}ms");
            
            return result;
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            // 🔥 [EXCEPTION] OperationCanceledException
            Console.WriteLine($"🔥 [EXCEPTION] OperationCanceledException: {ex.Message}");
            DebugLogUtility.WriteLog($"🔥 [EXCEPTION] OperationCanceledException: {ex.Message}");
            
            _logger.LogWarning("個別翻訳タイムアウト（5秒）- Text: '{Text}'", request.SourceText);
            
            return new TranslationResponse
            {
                RequestId = request.RequestId,
                TranslatedText = "翻訳タイムアウト（サーバー応答なし）",
                SourceText = request.SourceText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                ConfidenceScore = 0.0f,
                EngineName = Name,
                IsSuccess = false
            };
        }
        catch (CircuitBreakerOpenException ex)
        {
            // 🔥 [EXCEPTION] CircuitBreakerOpenException
            Console.WriteLine($"🔥 [EXCEPTION] CircuitBreakerOpenException: {ex.Message}");
            DebugLogUtility.WriteLog($"🔥 [EXCEPTION] CircuitBreakerOpenException: {ex.Message}");
            
            _logger.LogWarning("🚨 [CIRCUIT_BREAKER] サーキットブレーカーが開いています");
            
            var error = TranslationError.FromException(
                TranslationError.ServiceUnavailable, 
                "翻訳サービスが一時的に利用できません（サーキットブレーカー開放中）",
                ex,
                true, 
                TranslationErrorType.ServiceUnavailable);
            var response = TranslationResponse.CreateError(request, error, Name);
            return response;
        }
        catch (TranslationTimeoutException ex)
        {
            // 🔥 [EXCEPTION] TranslationTimeoutException
            Console.WriteLine($"🔥 [EXCEPTION] TranslationTimeoutException: {ex.Message}");
            DebugLogUtility.WriteLog($"🔥 [EXCEPTION] TranslationTimeoutException: {ex.Message}");
            
            _logger.LogWarning("⏱️ [CIRCUIT_BREAKER] 翻訳タイムアウト");
            
            var error = TranslationError.FromException(
                TranslationError.TimeoutError, 
                "翻訳がタイムアウトしました",
                ex,
                true, 
                TranslationErrorType.Timeout);
            var response = TranslationResponse.CreateError(request, error, Name);
            return response;
        }
        catch (Exception ex)
        {
            // 🔥 [EXCEPTION] その他の例外
            Console.WriteLine($"🔥 [EXCEPTION] 一般例外: {ex.GetType().Name} - {ex.Message}");
            DebugLogUtility.WriteLog($"🔥 [EXCEPTION] 一般例外: {ex.GetType().Name} - {ex.Message}");
            DebugLogUtility.WriteLog($"🔥 [EXCEPTION] スタックトレース: {ex.StackTrace}");
            
            _logger.LogError(ex, "翻訳エラー");
            
            // 🔥 [ERROR_DEBUG] 例外の詳細情報を出力
            _logger.LogDebug("🔥 [ERROR_DEBUG] 例外詳細:");
            _logger.LogDebug("🔥 [ERROR_DEBUG] - 例外タイプ: {ExceptionType}", ex.GetType().Name);
            _logger.LogDebug("🔥 [ERROR_DEBUG] - 例外メッセージ: {Message}", ex.Message);
            _logger.LogDebug("🔥 [ERROR_DEBUG] - スタックトレース: {StackTrace}", ex.StackTrace);
            Console.WriteLine($"🔥 [ERROR_DEBUG] 翻訳エラー発生: {ex.GetType().Name} - {ex.Message}");
            
            var error = TranslationError.FromException(
                TranslationError.InternalError, 
                "翻訳エラーが発生しました",
                ex,
                false, 
                TranslationErrorType.Exception);
            var response = TranslationResponse.CreateError(request, error, Name);
            return response;
        }
    }

    public virtual async Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
        IReadOnlyList<CoreTranslationRequest> requests, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
            
        if (requests.Count == 0)
            return [];

        // 言語ペアでグループ化
        var groupedRequests = requests.GroupBy(r => $"{r.SourceLanguage.Code}_{r.TargetLanguage.Code}");
        var allResponses = new List<TranslationResponse>();

        foreach (var group in groupedRequests)
        {
            var groupList = group.ToList();
            
            // バッチサイズ制限確認
            const int maxBatchSize = 50;
            if (groupList.Count > maxBatchSize)
            {
                // 大きなバッチを分割処理
                var splitResponses = await ProcessLargeBatchAsync(groupList, maxBatchSize, cancellationToken).ConfigureAwait(false);
                allResponses.AddRange(splitResponses);
            }
            else
            {
                // 通常のバッチ処理
                var batchResponses = await ProcessSingleBatchAsync(groupList, cancellationToken).ConfigureAwait(false);
                allResponses.AddRange(batchResponses);
            }
        }

        // 元の順序を保持するため、RequestIdでソート
        var responseMap = allResponses.ToDictionary(r => r.RequestId);
        return [..requests.Select(req => responseMap.TryGetValue(req.RequestId, out var response) 
            ? response 
            : TranslationResponse.CreateError(req, 
                new TranslationError { ErrorCode = "BATCH_PROCESSING_ERROR", Message = "Response not found" }, 
                Name))];
    }

    private async Task<IReadOnlyList<TranslationResponse>> ProcessSingleBatchAsync(
        IReadOnlyList<CoreTranslationRequest> requests, 
        CancellationToken cancellationToken)
    {
        var batchStopwatch = Stopwatch.StartNew();
        
        PersistentConnection? connection = null;
        TcpClient? directClient = null;
        NetworkStream? directStream = null;
        StreamWriter? directWriter = null;
        StreamReader? directReader = null;

        try
        {
            // 🔧 [GEMINI_REVIEW] 設定ファイルベースの接続プール制御
            // 🆕 Gemini推奨: 設定ファイルベースの接続プール制御
            var useConnectionPool = _circuitBreakerSettings.EnableConnectionPool;
            if (useConnectionPool && _connectionPool != null)
            {
                // Phase 1統合: 接続プールから接続を取得
                connection = await _connectionPool.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // 🔄 単発接続でバッチ処理（汚染対策モード）
                directClient = new TcpClient();
                await directClient.ConnectAsync(ServerHost, _serverPort, cancellationToken).ConfigureAwait(false);
                
                directStream = directClient.GetStream();
                directStream.ReadTimeout = ConnectionTimeoutMs;
                directStream.WriteTimeout = ConnectionTimeoutMs;
                
                // 🔧 [CRITICAL_ENCODING_FIX] システムレベルUTF-8エンコーディング指定（Windows問題対応）
                var utf8EncodingNoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
                directWriter = new StreamWriter(directStream, utf8EncodingNoBom, bufferSize: 8192, leaveOpen: true) { AutoFlush = true };
                directReader = new StreamReader(directStream, utf8EncodingNoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
            }

            // バッチリクエスト構築（同じ言語ペアが保証されている）
            var batchRequest = new
            {
                texts = requests.Select(r => r.SourceText).ToList(),
                source_lang = NormalizeLanguageCode(requests[0].SourceLanguage.Code),  // 🔧 言語コード正規化
                target_lang = NormalizeLanguageCode(requests[0].TargetLanguage.Code),  // 🔧 言語コード正規化
                batch_mode = true,
                max_batch_size = 50
            };

            // JSON送信
            var jsonRequest = JsonSerializer.Serialize(batchRequest);
            
            string? jsonResponse;
            if (connection != null)
            {
                // 接続プール使用モード
                await connection.Writer.WriteLineAsync(jsonRequest).ConfigureAwait(false);
                // 🔧 [TIMEOUT_FIX] バッチ翻訳ReadLineAsync()を10秒に短縮（30秒→10秒）- P2統合システム協調
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                jsonResponse = await connection.Reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
            }
            else
            {
                // 単発接続モード（汚染対策）
                await directWriter!.WriteLineAsync(jsonRequest).ConfigureAwait(false);
                // 🔧 [TIMEOUT_FIX] バッチ翻訳ReadLineAsync()を10秒に短縮（30秒→10秒）- P2統合システム協調
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                jsonResponse = await directReader!.ReadLineAsync(cts.Token).ConfigureAwait(false);
            }
            
            if (string.IsNullOrEmpty(jsonResponse))
                throw new InvalidOperationException("Empty response from Python server");

            var batchResponse = JsonSerializer.Deserialize<PythonBatchResponse>(jsonResponse);
            
            if (batchResponse == null)
                throw new InvalidOperationException("Failed to deserialize batch response");

            batchStopwatch.Stop();

            // レスポンスマッピング
            return MapBatchResponse(batchResponse, requests, batchStopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            batchStopwatch.Stop();
            _logger.LogWarning("バッチ翻訳タイムアウト（30秒）: Pythonサーバーからの応答待機でタイムアウト発生");
            
            // タイムアウト時は個別処理でフォールバック
            return await FallbackToIndividualProcessingAsync(requests, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            batchStopwatch.Stop();
            _logger.LogError(ex, "バッチ翻訳エラー: {Error}", ex.Message);
            
            // エラー時は個別処理でフォールバック
            return await FallbackToIndividualProcessingAsync(requests, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (connection != null)
            {
                // Phase 1統合: 接続をプールに返却
                await _connectionPool!.ReturnConnectionAsync(connection).ConfigureAwait(false);
            }
            else
            {
                // 🔄 単発接続リソースの解放（汚染対策モード）
                directWriter?.Dispose();
                directReader?.Dispose();
                directStream?.Dispose();
                directClient?.Dispose();
            }
        }
    }

    private async Task<IReadOnlyList<TranslationResponse>> ProcessLargeBatchAsync(
        IReadOnlyList<CoreTranslationRequest> requests,
        int maxBatchSize,
        CancellationToken cancellationToken)
    {
        var results = new List<TranslationResponse>();

        // バッチを分割して並列処理
        var batches = requests
            .Select((request, index) => new { request, index })
            .GroupBy(x => x.index / maxBatchSize)
            .Select(g => g.Select(x => x.request).ToList())
            .ToList();

        // Phase 1.5: 並列バッチ処理復元 - Task.WhenAllで最適パフォーマンス
        var batchTasks = batches.Select(batch => ProcessSingleBatchAsync(batch, cancellationToken));
        var batchResults = await Task.WhenAll(batchTasks).ConfigureAwait(false);

        // 結果をフラット化
        foreach (var batchResult in batchResults)
        {
            results.AddRange(batchResult);
        }

        return results;
    }

    private IReadOnlyList<TranslationResponse> MapBatchResponse(
        PythonBatchResponse batchResponse, 
        IReadOnlyList<CoreTranslationRequest> originalRequests, 
        long elapsedMilliseconds)
    {
        const string engineName = "OptimizedPythonTranslation";
        
        if (!batchResponse.Success || batchResponse.Translations == null)
        {
            // エラー時は全てFailureで返す
            var errorMessage = batchResponse.Errors?.FirstOrDefault() ?? "Unknown batch translation error";
            return [..originalRequests.Select(req => 
            {
                var error = new TranslationError
                {
                    ErrorCode = "BATCH_TRANSLATION_ERROR",
                    Message = errorMessage
                };
                return TranslationResponse.CreateError(req, error, engineName);
            })];
        }

        var results = new List<TranslationResponse>();
        var translations = batchResponse.Translations;
        var confidenceScores = batchResponse.ConfidenceScores ?? [];

        for (int i = 0; i < originalRequests.Count && i < translations.Count; i++)
        {
            var request = originalRequests[i];
            var translation = translations[i];
            var confidence = i < confidenceScores.Count ? confidenceScores[i] : 0.95f;
            var avgProcessingTime = elapsedMilliseconds / originalRequests.Count;

            var response = TranslationResponse.CreateSuccessWithConfidence(
                request,
                translation,
                engineName,
                avgProcessingTime,
                confidence
            );

            results.Add(response);
        }

        // バッチサイズ不一致の場合のフォールバック
        if (results.Count < originalRequests.Count)
        {
            _logger.LogWarning("バッチレスポンスサイズ不一致: expected {Expected}, got {Actual}", 
                originalRequests.Count, results.Count);
            
            // 不足分はエラーレスポンスで埋める
            for (int i = results.Count; i < originalRequests.Count; i++)
            {
                var request = originalRequests[i];
                var error = new TranslationError
                {
                    ErrorCode = "BATCH_SIZE_MISMATCH",
                    Message = "Batch response size mismatch"
                };
                var errorResponse = TranslationResponse.CreateError(request, error, engineName);
                errorResponse.ProcessingTimeMs = elapsedMilliseconds;
                results.Add(errorResponse);
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<TranslationResponse>> FallbackToIndividualProcessingAsync(
        IReadOnlyList<CoreTranslationRequest> requests,
        CancellationToken cancellationToken)
    {
        const string engineName = "OptimizedPythonTranslation";
        _logger.LogInformation("バッチ処理失敗 - 個別処理にフォールバック: {Count}件", requests.Count);
        
        var results = new List<TranslationResponse>();
        
        foreach (var request in requests)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
                
            try
            {
                var response = await TranslateAsync(request, cancellationToken).ConfigureAwait(false);
                results.Add(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "個別翻訳処理エラー: {Text}", request.SourceText);
                var errorResponse = TranslationResponse.CreateErrorFromException(
                    request,
                    engineName,
                    "INDIVIDUAL_PROCESSING_ERROR",
                    ex.Message,
                    ex,
                    0
                );
                results.Add(errorResponse);
            }
        }
        
        return results;
    }

    public virtual async Task<bool> IsReadyAsync()
    {
        Console.WriteLine("🔧 [IsReady_ENTRY] IsReadyAsync開始");
        _logger.LogInformation("🔧 [IsReady_ENTRY] IsReadyAsync開始");

        if (_disposed)
        {
            Console.WriteLine("🔧 [IsReady_DEBUG] _disposed=true, returning false");
            return false;
        }

        // 🔧 Phase 2.2.2: モデルロード完了フラグを最優先でチェック
        // TranslationModelLoaderがInitializeAsync()完了前にIsReadyAsync()を呼ぶ問題を修正
        if (!_isModelLoaded)
        {
            Console.WriteLine("🔧 [IsReady_DEBUG] _isModelLoaded=false, returning false");
            return false;
        }

        Console.WriteLine($"🔧 [IsReady_DEBUG] _translationClient == null: {_translationClient == null}");
        _logger.LogInformation($"🔧 [IsReady_DEBUG] _translationClient == null: {_translationClient == null}");

        // 🚀 UltraThink修正: StdinStdoutTranslationClient使用時の専用チェック
        if (_translationClient != null)
        {
            Console.WriteLine("🔧 [IsReady_DEBUG] StdinStdoutTranslationClient使用 - 専用チェック実行");
            _logger.LogInformation("🔧 [IsReady_DEBUG] StdinStdoutTranslationClient使用 - 専用チェック実行");

            // StdinStdoutTranslationClientが利用可能な場合は直接状態をチェック
            try
            {
                var isClientReady = await _translationClient.IsReadyAsync().ConfigureAwait(false);
                Console.WriteLine($"🔧 [IsReady_DEBUG] StdinStdoutTranslationClient.IsReady結果: {isClientReady}");
                _logger.LogInformation($"🔧 [IsReady_DEBUG] StdinStdoutTranslationClient.IsReady結果: {isClientReady}");

                // 🔥 UltraThink修正2: IsReady=falseでもプロセス生存なら翻訳試行を許可
                if (!isClientReady)
                {
                    Console.WriteLine("🔧 [IsReady_FALLBACK] is_ready=false、プロセス状態確認中...");
                    _logger.LogInformation("🔧 [IsReady_FALLBACK] is_ready=false、プロセス状態確認中...");

                    // サーバーマネージャーからプロセス状態を直接確認
                    if (_serverManager != null)
                    {
                        try
                        {
                            var currentLanguagePair = _languageConfig.GetCurrentLanguagePair();
                            var languagePairKey = $"{currentLanguagePair.SourceCode}-{currentLanguagePair.TargetCode}";
                            var serverInfo = await _serverManager.GetServerAsync(languagePairKey).ConfigureAwait(false);
                            if (serverInfo is PythonServerInstance instance &&
                                instance.Process != null &&
                                !instance.Process.HasExited)
                            {
                                Console.WriteLine("🔧 [IsReady_FALLBACK] プロセス生存確認、翻訳試行を許可");
                                _logger.LogInformation("🔧 [IsReady_FALLBACK] プロセス生存確認、翻訳試行を許可");
                                return true; // CTranslate2モデル破損でもプロセス生存なら翻訳試行
                            }
                        }
                        catch (Exception fallbackEx)
                        {
                            Console.WriteLine($"🔧 [IsReady_FALLBACK_ERROR] フォールバック確認失敗: {fallbackEx.Message}");
                            _logger.LogDebug(fallbackEx, "🔧 [IsReady_FALLBACK_ERROR] フォールバック確認失敗");
                        }
                    }
                }

                return isClientReady;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🔧 [IsReady_ERROR] StdinStdoutTranslationClient.IsReady例外: {ex.Message}");
                _logger.LogError(ex, "🔧 [IsReady_ERROR] StdinStdoutTranslationClient.IsReady例外");
                return false;
            }
        }

        Console.WriteLine("🔧 [IsReady_DEBUG] _translationClientがnull、従来のサーバープロセスチェック開始");

        // 従来のサーバープロセス確認（_translationClientがnullの場合）
        if (_serverProcess == null)
            return false;

        try
        {
            if (_serverProcess.HasExited)
                return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        // 接続テスト
        return await TestConnectionAsync().ConfigureAwait(false);
    }

    private async Task<TranslationResponse> TranslateWithOptimizedServerAsync(
        CoreTranslationRequest request,
        CancellationToken cancellationToken)
    {
        // 🔥🔥🔥 [ULTRA_DEBUG] メソッド到達確認（最優先ログ）
        Console.WriteLine($"🔥🔥🔥 [ULTRA_DEBUG_METHOD_ENTRY] TranslateWithOptimizedServerAsync到達！ - RequestId: {request.RequestId}");
        DebugLogUtility.WriteLog($"🔥🔥🔥 [ULTRA_DEBUG_METHOD_ENTRY] TranslateWithOptimizedServerAsync到達！ - RequestId: {request.RequestId}");

        // 🚀 UltraPhase 14.25: stdin/stdout通信への完全移行
        _logger.LogDebug("🚀 [UltraPhase 14.25] TranslateWithOptimizedServerAsync - stdin/stdout通信モード");
        Console.WriteLine($"🚀 [UltraPhase 14.25] TranslateWithOptimizedServerAsync - RequestId: {request.RequestId}");

        var totalStopwatch = Stopwatch.StartNew();

        // 🎯 UltraPhase 14.25: StdinStdoutTranslationClient 優先使用
        if (_translationClient != null)
        {
            try
            {
                _logger.LogDebug("📤 [StdinStdout] StdinStdoutTranslationClient.TranslateAsync() 呼び出し");

                var response = await _translationClient.TranslateAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                totalStopwatch.Stop();
                _logger.LogInformation("✅ [StdinStdout] 翻訳完了: {ElapsedMs}ms", totalStopwatch.ElapsedMilliseconds);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [StdinStdout] StdinStdoutTranslationClient エラー: {Message}", ex.Message);
                throw;
            }
        }

        // ⚠️ フォールバック: _translationClient が null の場合（レガシー互換性）
        _logger.LogWarning("⚠️ [UltraPhase 14.25] _translationClient が null - TCP接続ロジックへフォールバック");

        // 🔧 [LEGACY] 以下は旧TCP接続ロジック（_translationClient == null 時のみ実行）
        var connectionAcquireStopwatch = Stopwatch.StartNew();

        PersistentConnection? connection = null;
        TcpClient? directClient = null;
        NetworkStream? directStream = null;
        StreamWriter? directWriter = null;
        StreamReader? directReader = null;

        try
        {
            var useConnectionPool = _circuitBreakerSettings.EnableConnectionPool;
            if (!useConnectionPool)
            {
                Console.WriteLine($"🔧 [CONFIG] 設定により接続プール無効化、単発接続を使用");
                _logger.LogDebug("🔧 [CONFIG] 設定により接続プール無効化、単発接続を使用");
            }

            // 設定に基づく接続プール制御
            if (useConnectionPool && _connectionPool != null)
            {
                // Issue #147: 接続プールから接続を取得（接続ロック競合を解決）
                // 🔧 [TIMEOUT_FIX] 接続プール取得に30秒タイムアウトを追加
                using var poolTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(120)); // 🔧 [TIMEOUT_TEST] 30秒→120秒に延長してタイムアウト原因を確定検証
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, poolTimeout.Token);
                
                _logger.LogDebug("🔌 接続プール取得開始...");
                connection = await _connectionPool.GetConnectionAsync(combinedCts.Token).ConfigureAwait(false);
                connectionAcquireStopwatch.Stop();
                _logger.LogInformation("[TIMING] 接続プール取得: {ElapsedMs}ms", connectionAcquireStopwatch.ElapsedMilliseconds);
            }
            else
            {
                // 🔄 単発接続作成（汚染対策モード）
                directClient = new TcpClient();
                await directClient.ConnectAsync(ServerHost, _serverPort, cancellationToken).ConfigureAwait(false);

                directStream = directClient.GetStream();
                directStream.ReadTimeout = ConnectionTimeoutMs;
                directStream.WriteTimeout = ConnectionTimeoutMs;

                // 🔧 [ENCODING_SIMPLIFIED] シンプルなUTF-8エンコーディング指定（Windows修復処理削除）
                var utf8EncodingNoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
                directWriter = new StreamWriter(directStream, utf8EncodingNoBom, bufferSize: 8192, leaveOpen: true) { AutoFlush = true };
                directReader = new StreamReader(directStream, utf8EncodingNoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);

                connectionAcquireStopwatch.Stop();
                _logger.LogInformation("[TIMING] 単発接続作成（汚染対策）: {ElapsedMs}ms", connectionAcquireStopwatch.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            connectionAcquireStopwatch.Stop();
            _logger.LogError(ex, "接続取得失敗 - 経過時間: {ElapsedMs}ms", connectionAcquireStopwatch.ElapsedMilliseconds);
            directWriter?.Dispose();
            directReader?.Dispose();
            directStream?.Dispose();
            directClient?.Dispose();
            throw new InvalidOperationException($"接続取得に失敗: {ex.Message}", ex);
        }
        
        try
        {
            var serializationStopwatch = Stopwatch.StartNew();
            // リクエスト送信
            var requestData = new
            {
                text = request.SourceText,
                source_lang = NormalizeLanguageCode(request.SourceLanguage.Code),  // 🔧 言語コード正規化
                target_lang = NormalizeLanguageCode(request.TargetLanguage.Code),  // 🔧 言語コード正規化
                request_id = request.RequestId
            };
            
            var jsonRequest = JsonSerializer.Serialize(requestData);
            serializationStopwatch.Stop();
            _logger.LogInformation("[TIMING] JSONシリアライゼーション: {ElapsedMs}ms", serializationStopwatch.ElapsedMilliseconds);

            // 🎯 [DEBUG] JSONペイロード詳細ログ出力
            _logger.LogDebug("🌐 [JSON_PAYLOAD] 送信JSONペイロード: {JsonPayload}", jsonRequest);
            Console.WriteLine($"🌐 [JSON_PAYLOAD] 送信JSONペイロード: {jsonRequest}");
            
            var networkSendStopwatch = Stopwatch.StartNew();
            
            string? jsonResponse;
            if (connection != null)
            {
                // 接続プール使用モード
                await connection.Writer.WriteLineAsync(jsonRequest).ConfigureAwait(false);
                await connection.Writer.FlushAsync().ConfigureAwait(false); // 手動フラッシュ
                networkSendStopwatch.Stop();
                _logger.LogInformation("[TIMING] ネットワーク送信（プール接続）: {ElapsedMs}ms", networkSendStopwatch.ElapsedMilliseconds);
                
                var networkReceiveStopwatch = Stopwatch.StartNew();
                // 🔧 [TIMEOUT_FIX] ReadLineAsync()にCircuitBreaker設定タイムアウト追加でNLLB-200モデルロード時間を考慮
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(_translationTimeoutMs));
                jsonResponse = await connection.Reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
                networkReceiveStopwatch.Stop();
                _logger.LogInformation("[TIMING] ネットワーク受信（プール接続、Python処理含む）: {ElapsedMs}ms", networkReceiveStopwatch.ElapsedMilliseconds);
            }
            else
            {
                // 単発接続モード（汚染対策）
                await directWriter!.WriteLineAsync(jsonRequest).ConfigureAwait(false);
                networkSendStopwatch.Stop();
                _logger.LogInformation("[TIMING] ネットワーク送信（単発接続）: {ElapsedMs}ms", networkSendStopwatch.ElapsedMilliseconds);
                
                var networkReceiveStopwatch = Stopwatch.StartNew();
                // 🔧 [TIMEOUT_FIX] ReadLineAsync()にCircuitBreaker設定タイムアウト追加でNLLB-200モデルロード時間を考慮
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(_translationTimeoutMs));
                jsonResponse = await directReader!.ReadLineAsync(cts.Token).ConfigureAwait(false);
                networkReceiveStopwatch.Stop();
                _logger.LogInformation("[TIMING] ネットワーク受信（単発接続、Python処理含む）: {ElapsedMs}ms", networkReceiveStopwatch.ElapsedMilliseconds);
            }
            
            if (string.IsNullOrEmpty(jsonResponse))
            {
                var isConnected = connection?.TcpClient?.Connected ?? directClient?.Connected ?? false;
                var dataAvailable = connection?.TcpClient?.GetStream()?.DataAvailable ?? directStream?.DataAvailable ?? false;
                _logger.LogError("空のレスポンス受信 - 接続状態: Connected={Connected}, DataAvailable={DataAvailable}", 
                    isConnected, dataAvailable);
                throw new InvalidOperationException("サーバーから空のレスポンスを受信しました");
            }
            
            _logger.LogDebug("Python応答受信: {Response}", SanitizeForLogging(jsonResponse));
            
            // 🔥 [ENCODING_DEBUG] 受信したレスポンスの詳細バイト情報をログ出力（セキュリティ対策済み）
            var responseBytes = System.Text.Encoding.UTF8.GetBytes(jsonResponse);
            var sanitizedResponse = SanitizeForLogging(jsonResponse);
            _logger.LogDebug("🔍 [ENCODING_DEBUG] 受信したレスポンス詳細:");
            _logger.LogDebug("🔍 [ENCODING_DEBUG] - レスポンス文字列長: {Length}", jsonResponse.Length);
            _logger.LogDebug("🔍 [ENCODING_DEBUG] - UTF-8バイト長: {ByteLength}", responseBytes.Length);
            _logger.LogDebug("🔍 [ENCODING_DEBUG] - サニタイズ後レスポンス: {Response}", sanitizedResponse);
            Console.WriteLine($"🔍 [ENCODING_DEBUG] 受信したレスポンス長: {jsonResponse.Length}");
            Console.WriteLine($"🔍 [ENCODING_DEBUG] UTF-8バイト長: {responseBytes.Length}");
            
            // 🔧 [ENCODING_SIMPLIFIED] Windows環境エンコーディング修復処理を削除し、シンプルUTF-8処理に変更
            var originalResponse = jsonResponse;
            
            // 🚨 DEBUG: 不正翻訳結果の調査用詳細ログ（セキュリティ対策済み）
            var sanitizedJsonResponse = SanitizeForLogging(jsonResponse);
            var sanitizedSourceText = SanitizeForLogging(request.SourceText);
            Console.WriteLine($"🔍 [CORRUPTION_DEBUG] Python応答受信長: {jsonResponse.Length}文字");
            SafeAppendToDebugFile($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [PYTHON_RESPONSE] Request: '{sanitizedSourceText}' → Response: '{sanitizedJsonResponse}'{Environment.NewLine}");
            
            var deserializationStopwatch = Stopwatch.StartNew();
            
            // 🔧 [ENCODING_SIMPLIFIED] 直接UTF-8でJSONデシリアライゼーション
            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                PropertyNameCaseInsensitive = true
            };
            
            PythonTranslationResponse? response;
            try 
            {
                // シンプルなJSONデシリアライゼーション（エンコーディング修復処理なし）
                response = JsonSerializer.Deserialize<PythonTranslationResponse>(jsonResponse, jsonOptions);
            }
            catch (Exception jsonEx)
            {
                _logger.LogError(jsonEx, "JSONデシリアライゼーション失敗: {Error}", jsonEx.Message);
                throw new InvalidOperationException($"JSONレスポンスの解析に失敗: {jsonEx.Message}", jsonEx);
            }
            
            deserializationStopwatch.Stop();
            _logger.LogInformation("[TIMING] JSONデシリアライゼーション（シンプル版）: {ElapsedMs}ms", deserializationStopwatch.ElapsedMilliseconds);
            
            if (response == null)
            {
                throw new InvalidOperationException("レスポンスのデシリアライズに失敗しました");
            }
            
            // 🔥 [ENCODING_DEBUG] JSON解析後のレスポンス詳細情報をログ出力
            _logger.LogDebug("🔍 [JSON_DEBUG] JSON解析後のレスポンス詳細:");
            _logger.LogDebug("🔍 [JSON_DEBUG] - Success: {Success}", response.Success);
            _logger.LogDebug("🔍 [JSON_DEBUG] - Translation: '{Translation}'", response.Translation ?? "null");
            _logger.LogDebug("🔍 [JSON_DEBUG] - Translation Length: {Length}", response.Translation?.Length ?? 0);
            if (response.Translation != null)
            {
                var translationBytes = System.Text.Encoding.UTF8.GetBytes(response.Translation);
                _logger.LogDebug("🔍 [JSON_DEBUG] - Translation UTF-8バイト: {Bytes}", Convert.ToHexString(translationBytes));
            }
            _logger.LogDebug("🔍 [JSON_DEBUG] - Confidence: {Confidence}", response.Confidence);
            _logger.LogDebug("🔍 [JSON_DEBUG] - Error: '{Error}'", response.Error ?? "null");
            Console.WriteLine($"🔍 [JSON_DEBUG] Success: {response.Success}, Translation: '{response.Translation}', Length: {response.Translation?.Length ?? 0}");
            
            var resultCreationStopwatch = Stopwatch.StartNew();
            
            // エラー時の適切なハンドリング
            string translatedText;
            float confidenceScore;
            bool isSuccess;
            
            // 🆕 Gemini推奨: 正確な成功判定ロジック - 論理矛盾解消
            // エラーメッセージを含む翻訳結果を適切に失敗として判定
            bool isActualSuccess = !string.IsNullOrEmpty(response.Translation)
                                  && !response.Translation.Contains("翻訳エラーが発生しました")
                                  && !response.Translation.Contains("エラーが発生")
                                  && response.Success; // Pythonサーバーのフラグも考慮

            if (isActualSuccess)
            {
                translatedText = response.Translation;
                confidenceScore = response.Confidence ?? 0.95f;
                isSuccess = true;
                
                // 🔧 [ENCODING_DEBUG] 文字エンコーディング詳細情報をログ出力
                var originalBytes = System.Text.Encoding.UTF8.GetBytes(translatedText);
                var decodedText = System.Text.Encoding.UTF8.GetString(originalBytes);
                _logger.LogInformation("翻訳結果詳細情報 - IsSuccess: {IsSuccess}, Text: '{Text}', Length: {Length}", 
                    isSuccess, translatedText, translatedText.Length);
                
                Console.WriteLine($"🔍 [ENCODING_DEBUG] 翻訳結果詳細:");
                Console.WriteLine($"🔍 [ENCODING_DEBUG] - 原文: '{request.SourceText}'");
                Console.WriteLine($"🔍 [ENCODING_DEBUG] - 翻訳結果: '{translatedText}'");
                Console.WriteLine($"🔍 [ENCODING_DEBUG] - UTF-8再エンコード: '{decodedText}'");
                Console.WriteLine($"🔍 [ENCODING_DEBUG] - バイト長: {originalBytes.Length}");
                Console.WriteLine($"🔍 [ENCODING_DEBUG] - 文字長: {translatedText.Length}");
                
                _logger.LogDebug("翻訳成功 - Text: '{Text}', Confidence: {Confidence}", 
                    translatedText, confidenceScore);
                
                // 🚨 DEBUG: 不正翻訳結果の検出
                var suspiciousPatterns = new[] { "マグブキ", "マッテヤ", "イブハテ", "マククナ" };
                if (suspiciousPatterns.Any(pattern => translatedText.Contains(pattern)))
                {
                    Console.WriteLine($"🚨 [CORRUPTION_DETECTED] 不正翻訳結果検出!");
                    Console.WriteLine($"   入力長: {request.SourceText.Length}文字");
                    Console.WriteLine($"   出力長: {translatedText.Length}文字");
                    Console.WriteLine($"   Python応答長: {jsonResponse.Length}文字");
                    SafeAppendToDebugFile($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [CORRUPTION_DETECTED] 入力: '{sanitizedSourceText}' → 出力: '{SanitizeForLogging(translatedText)}' → Python応答: '{sanitizedJsonResponse}'{Environment.NewLine}");
                }
            }
            else
            {
                translatedText = "翻訳エラーが発生しました";
                confidenceScore = 0.0f;
                isSuccess = false;
                _logger.LogError("翻訳失敗 - Success: {Success}, Translation: '{Translation}', Error: '{Error}'", 
                    response.Success, response.Translation ?? "null", response.Error ?? "none");
            }
            
            var result = new TranslationResponse
            {
                RequestId = request.RequestId,
                TranslatedText = translatedText,
                SourceText = request.SourceText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                ConfidenceScore = confidenceScore,
                EngineName = Name,
                IsSuccess = isSuccess
            };
            resultCreationStopwatch.Stop();
            _logger.LogInformation("[TIMING] レスポンス生成: {ElapsedMs}ms", resultCreationStopwatch.ElapsedMilliseconds);
            
            totalStopwatch.Stop();
            _logger.LogInformation("[TIMING] 合計処理時間（C#側）: {ElapsedMs}ms", totalStopwatch.ElapsedMilliseconds);
            _logger.LogInformation("[TIMING] Python側処理時間: {PythonTimeMs}ms", (response.ProcessingTime ?? 0) * 1000);
            
            // 詳細ログ出力
            _logger.LogInformation("翻訳結果詳細 - IsSuccess: {IsSuccess}, Text: '{Text}', Length: {Length}", 
                result.IsSuccess, result.TranslatedText, result.TranslatedText?.Length ?? 0);
                
            return result;
        }
        finally
        {
            if (connection != null)
            {
                // Issue #147: 接続プールに接続を返却
                await _connectionPool!.ReturnConnectionAsync(connection).ConfigureAwait(false);
            }
            else
            {
                // 🔄 単発接続リソースの解放（汚染対策モード）
                directWriter?.Dispose();
                directReader?.Dispose();
                directStream?.Dispose();
                directClient?.Dispose();
            }
        }
    }

    private async Task MonitorServerHealthAsync()
    {
        while (!_disposed)
        {
            try
            {
                await Task.Delay(HealthCheckIntervalMs).ConfigureAwait(false);
                
                // Issue #147: 接続プールのヘルスチェックに委任
                // 接続プール自体がヘルスチェックを行うため、サーバープロセスの監視に専念
                if (_serverProcess == null || _serverProcess.HasExited)
                {
                    _logger.LogWarning("サーバープロセス異常終了を検出 - 再起動を試行");
                    await StartOptimizedServerAsync().ConfigureAwait(false);
                }
                
                // 🚨 Phase 1.2: メモリ監視アラート機能
                await CheckMemoryPressureAsync().ConfigureAwait(false);

                // メトリクスログ
                if (_totalRequests > 0)
                {
                    var avgMs = _totalProcessingTimeMs / _totalRequests;
                    _logger.LogInformation("パフォーマンス統計 - 平均処理時間: {AvgMs}ms, 総リクエスト: {TotalRequests}",
                        avgMs, _totalRequests);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ヘルスチェックエラー");
            }
        }
    }

    /// <summary>
    /// 🚨 Phase 1.2: メモリプレッシャー監視とアラート
    /// </summary>
    private async Task CheckMemoryPressureAsync()
    {
        try
        {
            // マネージドメモリ使用量取得
            var managedMemoryBytes = GC.GetTotalMemory(false);
            var managedMemoryMB = managedMemoryBytes / (1024 * 1024);

            // システム全体のメモリ使用率取得（Windows環境）
            double systemMemoryUsagePercentage = 0;
            long availableMemoryMB = 0;

            try
            {
                // Windows Performance Counter使用
                var process = System.Diagnostics.Process.GetCurrentProcess();
                var workingSetMB = process.WorkingSet64 / (1024 * 1024);

                // 現在のプロセスのメモリ使用量を基にシステムメモリ使用率を計算
                // Environment.WorkingSetで現在のプロセスの物理メモリ使用量を取得
                var processMemoryBytes = Environment.WorkingSet;
                var gcMemoryBytes = GC.GetTotalMemory(false);
                var totalProcessMemoryMB = (processMemoryBytes + gcMemoryBytes) / (1024 * 1024);

                // システム全体のメモリ使用率の概算（プロセスメモリ使用量ベース）
                systemMemoryUsagePercentage = Math.Min((double)totalProcessMemoryMB / 1024, 100); // 1GB当たりの使用率として概算
                availableMemoryMB = workingSetMB;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("システムメモリ情報取得失敗: {Error}", ex.Message);
            }

            // アラートレベル判定
            if (systemMemoryUsagePercentage >= 90.0)
            {
                _logger.LogError("🚨🚨 [MEMORY_CRITICAL] メモリ使用量が危険レベル: {Usage:F1}% - 即座の対応が必要", systemMemoryUsagePercentage);
                _logger.LogError("🚨 [MEMORY_DETAIL] マネージドメモリ: {ManagedMB}MB, プロセスメモリ: {ProcessMB}MB",
                    managedMemoryMB, availableMemoryMB);
            }
            else if (systemMemoryUsagePercentage >= 85.0)
            {
                _logger.LogWarning("🚨 [MEMORY_ALERT] メモリ使用量が警告レベル: {Usage:F1}% - 注意が必要", systemMemoryUsagePercentage);
                _logger.LogWarning("⚠️ [MEMORY_DETAIL] マネージドメモリ: {ManagedMB}MB, プロセスメモリ: {ProcessMB}MB",
                    managedMemoryMB, availableMemoryMB);
            }
            else if (systemMemoryUsagePercentage >= 75.0)
            {
                _logger.LogInformation("📊 [MEMORY_INFO] メモリ使用量: {Usage:F1}% (マネージドメモリ: {ManagedMB}MB)",
                    systemMemoryUsagePercentage, managedMemoryMB);
            }

            // NLLB-200モデル関連の詳細ログ（高メモリ使用時）
            if (systemMemoryUsagePercentage >= 80.0)
            {
                var gcInfo = GC.CollectionCount(2); // Gen2 GC回数
                _logger.LogInformation("🧠 [NLLB_MEMORY] NLLB-200モデルメモリ状況 - GC Gen2回数: {GCCount}, モデル状態: {ModelLoaded}",
                    gcInfo, _isModelLoaded ? "ロード済み" : "未ロード");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "メモリ監視エラー - 監視を継続");
        }
    }

    /// <summary>
    /// 🚨 Phase 1.3: Python標準出力監視（強化版）
    /// </summary>
    private async Task MonitorServerOutputAsync()
    {
        if (_serverProcess == null) return;

        try
        {
            while (true)
            {
                try
                {
                    if (_serverProcess.HasExited)
                        break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                var line = await _serverProcess.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (!string.IsNullOrEmpty(line))
                {
                    _logger.LogDebug("[PYTHON] {Output}", line);

                    // 🔥 モデルロード完了シグナルを監視
                    if (line.Contains("MODEL_READY:") || line.Contains("NLLB_MODEL_READY"))
                    {
                        _logger.LogInformation("🏁 Pythonからモデルロード完了シグナルを受信: {Signal}", line);
                        MarkModelAsLoaded();
                    }
                    // 🧠 モデルロード進捗情報をキャプチャ
                    else if (line.Contains("NLLB_MODEL_LOAD") || line.Contains("Loading model"))
                    {
                        _logger.LogInformation("🧠 [PYTHON_MODEL_PROGRESS] {Progress}", line);
                    }
                    // 🚀 サーバー起動情報をキャプチャ
                    else if (line.Contains("Translation Server listening") || line.Contains("Server started"))
                    {
                        _logger.LogInformation("🚀 [PYTHON_SERVER_START] {ServerInfo}", line);
                    }
                }
                else
                {
                    break; // EOF or process ended
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "サーバー出力監視エラー");
        }
    }

    /// <summary>
    /// 🚨 Phase 1.3: Python標準エラー監視（新規実装）
    /// </summary>
    private async Task MonitorServerErrorAsync()
    {
        if (_serverProcess == null) return;

        try
        {
            while (true)
            {
                try
                {
                    if (_serverProcess.HasExited)
                        break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                var line = await _serverProcess.StandardError.ReadLineAsync().ConfigureAwait(false);
                if (!string.IsNullOrEmpty(line))
                {
                    // Python エラーの重要度分類
                    if (line.Contains("Error") || line.Contains("Exception") || line.Contains("Traceback"))
                    {
                        _logger.LogError("🚨 [PYTHON_CRITICAL] クリティカルエラー: {Error}", line);

                        // メモリ関連エラーの特別処理
                        if (line.Contains("OutOfMemoryError") || line.Contains("CUDA out of memory"))
                        {
                            _logger.LogError("🧠💥 [PYTHON_MEMORY_ERROR] メモリ不足エラー検出: {MemoryError}", line);
                        }
                    }
                    else if (line.Contains("Warning") || line.Contains("WARN"))
                    {
                        _logger.LogWarning("⚠️ [PYTHON_WARNING] 警告: {Warning}", line);
                    }
                    else if (line.Contains("INFO") || line.Contains("DEBUG"))
                    {
                        _logger.LogDebug("🐍 [PYTHON_INFO] {Info}", line);
                    }
                    else
                    {
                        _logger.LogDebug("🐍 [PYTHON_STDERR] {Output}", line);
                    }
                }
                else
                {
                    break; // EOF or process ended
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Python標準エラー監視エラー - 監視を継続します");
        }
    }

    private async Task<bool> TestConnectionAsync()
    {
        try
        {
            // Phase 5: 動的ポート対応
            var targetPort = GetCurrentServerPort();
            
            // 🔧 [GEMINI_REVIEW] 設定ファイルベースの接続プール制御
            // 🆕 Gemini推奨: 設定ファイルベースの接続プール制御
            var useConnectionPool = _circuitBreakerSettings.EnableConnectionPool;
            if (useConnectionPool && _connectionPool != null)
            {
                // Issue #147: 接続プールによる接続テスト
                using var testCts = new CancellationTokenSource(ConnectionTimeoutMs);
                var testConnection = await _connectionPool.GetConnectionAsync(testCts.Token).ConfigureAwait(false);
                await _connectionPool.ReturnConnectionAsync(testConnection, testCts.Token).ConfigureAwait(false);
                return true;
            }
            else
            {
                // 🔄 単発接続テスト（汚染対策モード）- 動的ポート対応
                return await TestDirectConnectionAsync(targetPort).ConfigureAwait(false);
            }
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 現在のサーバーポート番号を取得
    /// UltraThink Phase 13: 動的ポート検出システム統合
    /// </summary>
    private int GetCurrentServerPort()
    {
        // Phase 5: 動的ポート管理の場合
        if (_managedServerInstance != null)
        {
            return _managedServerInstance.Port;
        }
        
        // UltraThink Phase 13: 動的ポート検出 - translation_ports_global.jsonから利用可能ポートを検出
        try
        {
            var globalRegistryPath = Path.Combine(Environment.CurrentDirectory, "translation_ports_global.json");
            if (File.Exists(globalRegistryPath))
            {
                var json = File.ReadAllText(globalRegistryPath);
                var portRegistry = JsonSerializer.Deserialize<JsonElement>(json);
                
                if (portRegistry.TryGetProperty("ports", out var portsElement))
                {
                    foreach (var portProperty in portsElement.EnumerateObject())
                    {
                        if (int.TryParse(portProperty.Name, out var availablePort))
                        {
                            _logger.LogInformation("🎯 [UltraThink Phase 13] 動的ポート検出成功: Port {Port} を使用", availablePort);
                            return availablePort;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "動的ポート検出失敗 - 固定ポート {Port} を使用", _serverPort);
        }
        
        // 固定ポートモード（フォールバック）
        return _serverPort;
    }

    /// <summary>
    /// 単発接続での接続テスト（接続プール無効化時用）
    /// </summary>
    private async Task<bool> TestDirectConnectionAsync(int? port = null)
    {
        TcpClient? testClient = null;
        NetworkStream? testStream = null;
        StreamWriter? writer = null;
        StreamReader? reader = null;

        try
        {
            using var testCts = new CancellationTokenSource(ConnectionTimeoutMs);
            
            // Phase 5: 動的ポート対応
            var targetPort = port ?? GetCurrentServerPort();

            testClient = new TcpClient();
            await testClient.ConnectAsync(ServerHost, targetPort, testCts.Token).ConfigureAwait(false);

            testStream = testClient.GetStream();
            testStream.ReadTimeout = ConnectionTimeoutMs;
            testStream.WriteTimeout = ConnectionTimeoutMs;

            writer = new StreamWriter(testStream, new UTF8Encoding(false)) { AutoFlush = true };
            reader = new StreamReader(testStream, Encoding.UTF8);

            // 簡単なping確認
            var pingRequest = JsonSerializer.Serialize(new { ping = true });
            await writer.WriteLineAsync(pingRequest).ConfigureAwait(false);

            var response = await reader.ReadLineAsync().ConfigureAwait(false);
            return !string.IsNullOrEmpty(response);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "単発接続テスト失敗");
            return false;
        }
        finally
        {
            writer?.Dispose();
            reader?.Dispose();
            testStream?.Dispose();
            testClient?.Dispose();
        }
    }

    /// <summary>
    /// サーバー接続テスト（リトライ機能付き）
    /// Gemini推奨：タイミング問題に対する堅牢な解決策
    /// </summary>
    /// <param name="port">テスト対象ポート（null=現在のサーバーポート）</param>
    /// <returns>接続成功可否</returns>
    private async Task<bool> TestDirectConnectionAsyncWithRetry(int? port = null)
    {
        const int maxRetries = 5;
        const int retryDelayMs = 2000;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                _logger.LogDebug($"🔄 [RETRY_LOGIC] 接続試行 {i + 1}/{maxRetries} - ポート: {port ?? GetCurrentServerPort()}");
                
                if (await TestDirectConnectionAsync(port).ConfigureAwait(false))
                {
                    _logger.LogInformation($"✅ [RETRY_LOGIC] 接続成功 - 試行回数: {i + 1}/{maxRetries}");
                    return true;
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
            {
                _logger.LogDebug($"⚠️ [RETRY_LOGIC] 接続拒否 (試行 {i + 1}/{maxRetries}) - {retryDelayMs}ms後に再試行");
                
                if (i < maxRetries - 1) // 最後の試行でない場合のみ待機
                {
                    await Task.Delay(retryDelayMs).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"🚨 [RETRY_LOGIC] 予期しないエラー (試行 {i + 1}/{maxRetries})");
                
                if (i < maxRetries - 1)
                {
                    await Task.Delay(retryDelayMs).ConfigureAwait(false);
                }
            }
        }

        _logger.LogError($"❌ [RETRY_LOGIC] 接続失敗 - 最大試行回数 {maxRetries} 到達");
        return false;
    }

    private void ConfigureKeepAlive(TcpClient client)
    {
        try
        {
            client.Client.SetSocketOption(
                System.Net.Sockets.SocketOptionLevel.Socket,
                System.Net.Sockets.SocketOptionName.KeepAlive,
                true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Keep-Alive設定失敗");
        }
    }

    // Issue #147: DisposePersistentConnectionメソッドは接続プール統合により削除
    // 接続管理は FixedSizeConnectionPool が担当

    private async Task CleanupExistingProcessesAsync()
    {
        try
        {
            _logger.LogInformation("🔄 既存Pythonサーバープロセスのクリーンアップ開始");
            
            var processes = Process.GetProcessesByName("python");
            var killedCount = 0;
            
            foreach (var process in processes)
            {
                try
                {
                    // 🔧 [SCRIPT_NAME_FIX] NLLB-200翻訳サーバーのプロセス検出
                    var commandLine = GetProcessCommandLine(process);
                    
                    if (commandLine?.Contains("nllb_translation_server") == true || 
                        commandLine?.Contains("optimized_translation_server") == true)
                    {
                        _logger.LogInformation("🚨 既存翻訳サーバープロセス発見: PID {ProcessId}, Command: {CommandLine}", 
                            process.Id, commandLine);
                        
                        process.Kill();
                        await Task.Delay(100).ConfigureAwait(false);
                        killedCount++;
                        
                        _logger.LogInformation("✅ 既存Pythonサーバープロセスを終了: PID {ProcessId}", process.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("プロセス {ProcessId} の確認中にエラー: {Error}", process.Id, ex.Message);
                }
            }
            
            _logger.LogInformation("🔄 クリーンアップ完了: {KilledCount}個のプロセスを終了", killedCount);
            
            // プロセス終了の安定化待機
            if (killedCount > 0)
            {
                await Task.Delay(1000).ConfigureAwait(false);
                _logger.LogInformation("🕒 プロセス終了安定化待機完了");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "既存プロセスのクリーンアップ中にエラー");
        }
    }
    
    /// <summary>
    /// プロセスのコマンドライン取得（WMI経由で確実に取得）
    /// </summary>
    private string? GetProcessCommandLine(Process process)
    {
        try
        {
            // MainModuleベースの簡易チェック
            var mainModule = process.MainModule?.FileName;
            if (mainModule != null)
            {
                return mainModule;
            }
            
            // 🔧 より確実なコマンドライン取得のため、WMI使用を検討
            // 現在は簡易実装で対応
            return null;
        }
        catch
        {
            return null;
        }
    }

    // 🚨 CACHE_DISABLED: キャッシュキー生成機能無効化
    // private string GenerateCacheKey(TranslationRequest request)
    // {
    //     return $"{request.SourceLanguage.Code}_{request.TargetLanguage.Code}_{request.SourceText.GetHashCode()}";
    // }

    private string FindProjectRoot(string currentDir)
    {
        var dir = new DirectoryInfo(currentDir);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Baketa.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? currentDir;
    }

    /// <summary>
    /// 言語コードをNLLB-200サーバー対応形式に正規化
    /// </summary>
    private static string NormalizeLanguageCode(string code)
    {
        return code?.ToLowerInvariant() switch
        {
            // autoは使わず、実際の言語コードのみ処理
            "ja-jp" or "jpn_jpan" or "japanese" => "ja",
            "en-us" or "eng_latn" or "english" => "en",
            "ja" or "en" => code,  // 既に正規化済み
            _ => "en"  // デフォルトは英語
        };
    }

    public async Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync()
    {
        // 設定から動的に言語を取得
        var languagePair = _languageConfig.GetCurrentLanguagePair();
        var defaultSourceLanguage = languagePair.SourceCode;
        var defaultTargetLanguage = languagePair.TargetCode;
        
        return await Task.FromResult<IReadOnlyCollection<LanguagePair>>(
        [
            // ユーザー設定に基づく言語ペア
            new() { SourceLanguage = new() { Code = defaultSourceLanguage, DisplayName = GetLanguageDisplayName(defaultSourceLanguage) },
                   TargetLanguage = new() { Code = defaultTargetLanguage, DisplayName = GetLanguageDisplayName(defaultTargetLanguage) } },
            
            // 逆方向もサポート（例：ja→en, en→ja）
            new() { SourceLanguage = new() { Code = defaultTargetLanguage, DisplayName = GetLanguageDisplayName(defaultTargetLanguage) },
                   TargetLanguage = new() { Code = defaultSourceLanguage, DisplayName = GetLanguageDisplayName(defaultSourceLanguage) } },
            
            // 固定言語ペア（日本語⇔英語）
            new() { SourceLanguage = new() { Code = "ja", DisplayName = "Japanese" },
                   TargetLanguage = new() { Code = "en", DisplayName = "English" } },
            new() { SourceLanguage = new() { Code = "en", DisplayName = "English" },
                   TargetLanguage = new() { Code = "ja", DisplayName = "Japanese" } }
        ]).ConfigureAwait(false);
    }
    
    private static string GetLanguageDisplayName(string languageCode)
    {
        return languageCode switch
        {
            "ja" => "Japanese",
            "en" => "English",
            _ => languageCode.ToUpperInvariant()
        };
    }

    public async Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair)
    {
        var supportedPairs = await GetSupportedLanguagePairsAsync().ConfigureAwait(false);
        return supportedPairs.Any(p => 
            p.SourceLanguage.Code == languagePair.SourceLanguage.Code &&
            p.TargetLanguage.Code == languagePair.TargetLanguage.Code);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            
            // Issue #147: 接続プールの破棄は DI コンテナが管理
            // FixedSizeConnectionPool は IAsyncDisposable として適切に破棄される
            
            if (_serverProcess != null)
            {
                try
                {
                    // Processの状態を安全に確認
                    if (!_serverProcess.HasExited)
                    {
                        _serverProcess.Kill();
                        _serverProcess.WaitForExit(5000);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "サーバープロセス終了中にエラー");
                }
                finally
                {
                    _serverProcess?.Dispose();
                    _serverProcess = null;
                }
            }
            
            _serverLock?.Dispose();
            
            _logger.LogInformation("OptimizedPythonTranslationEngineが破棄されました");
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private bool _disposed;

    // 内部クラス
    private class TranslationMetrics
    {
        public string TranslatedText { get; set; } = string.Empty;
        public float ConfidenceScore { get; set; }
        public long ProcessingTimeMs { get; set; }
        public DateTime Timestamp { get; set; }
    }

    private class PythonTranslationResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        
        [JsonPropertyName("translation")]
        public string? Translation { get; set; }
        
        [JsonPropertyName("confidence")]
        public float? Confidence { get; set; }
        
        [JsonPropertyName("error")]
        public string? Error { get; set; }
        
        [JsonPropertyName("processing_time")]
        public double? ProcessingTime { get; set; }
    }

    private class PythonBatchResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        
        [JsonPropertyName("translations")]
        public List<string>? Translations { get; set; }
        
        [JsonPropertyName("confidence_scores")]
        public List<float>? ConfidenceScores { get; set; }
        
        [JsonPropertyName("processing_time")]
        public double? ProcessingTime { get; set; }
        
        [JsonPropertyName("batch_size")]
        public int? BatchSize { get; set; }
        
        [JsonPropertyName("errors")]
        public List<string>? Errors { get; set; }
    }

    /// <summary>
    /// ファイル競合を防ぐ安全なデバッグファイル書き込み
    /// </summary>
    private void SafeAppendToDebugFile(string content)
    {
        var debugFilePath = Path.Combine(Path.GetTempPath(), "baketa_debug_translation_corruption.txt");
        const int maxRetries = 3;
        const int retryDelayMs = 10;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var fileStream = new FileStream(debugFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(fileStream, Encoding.UTF8);
                writer.Write(content);
                writer.Flush();
                return; // 成功
            }
            catch (IOException ex) when (ex.Message.Contains("being used by another process"))
            {
                if (attempt < maxRetries)
                {
                    Thread.Sleep(retryDelayMs * attempt); // 指数バックオフ
                    continue;
                }
                // 最終試行でも失敗した場合はログのみ
                _logger.LogWarning("デバッグファイル書き込み失敗（ファイル競合）: {Error}", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("デバッグファイル書き込み失敗: {Error}", ex.Message);
                break;
            }
        }
    }

    /// <summary>
    /// ログ出力用テキストサニタイズ - ログインジェクション攻撃対策
    /// </summary>
    /// <param name="input">サニタイズ対象の文字列</param>
    /// <returns>サニタイズされた安全な文字列</returns>
    private static string SanitizeForLogging(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "[empty]";

        // 長すぎる文字列は切り詰める
        const int maxLength = 200;
        var sanitized = input.Length > maxLength ? input[..maxLength] + "..." : input;
        
        // ログインジェクション攻撃を防ぐため制御文字を除去
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"[\r\n\t\x00-\x1F\x7F]", "");
        
        // 潜在的に危険な文字をエスケープ
        sanitized = sanitized
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("'", "\\'");
            
        return sanitized;
    }
    
    /// <summary>
    /// 🎯 [NLLB-200] 設定に基づく動的ポート設定とスクリプトパス設定（IConfiguration版）
    /// </summary>
    private void ConfigureServerSettings(string projectRoot)
    {
        try
        {
            // 動的に設定を取得（固定値使用）
            var defaultEngine = TranslationEngine.NLLB200; // 固定値使用
            
            if (defaultEngine == TranslationEngine.NLLB200)
            {
                // NLLB-200設定から動的にポートとスクリプトパスを取得
                _serverPort = 5556; // 固定値使用

                // CTranslate2版サーバー優先、フォールバックで旧版
                var ct2ScriptPath = Path.Combine(projectRoot, "scripts", "nllb_translation_server_ct2.py");
                var legacyScriptPath = Path.Combine(projectRoot, "scripts", "nllb_translation_server.py");

                if (File.Exists(ct2ScriptPath))
                {
                    _serverScriptPath = ct2ScriptPath;
                    _logger.LogInformation("✅ CTranslate2版サーバーを使用: {Script}", Path.GetFileName(_serverScriptPath));
                }
                else if (File.Exists(legacyScriptPath))
                {
                    _serverScriptPath = legacyScriptPath;
                    _logger.LogWarning("⚠️ CTranslate2版が見つからず、旧版サーバーを使用: {Script}", Path.GetFileName(_serverScriptPath));
                }
                else
                {
                    _serverScriptPath = ct2ScriptPath; // エラーメッセージ用
                    _logger.LogError("❌ 翻訳サーバースクリプトが見つかりません: CT2={CT2}, Legacy={Legacy}", ct2ScriptPath, legacyScriptPath);
                }
                
                // UltraThink Phase 13: 起動時に動的ポート検出を実行
                _logger.LogInformation("🔍 [UltraThink Phase 13] ConfigureServerSettings: 動的ポート検出開始 (現在の固定ポート: {Port})", _serverPort);
                var detectedPort = GetCurrentServerPort();
                _logger.LogInformation("🔍 [UltraThink Phase 13] ConfigureServerSettings: 検出結果 {ConfigPort} → {DetectedPort}", _serverPort, detectedPort);
                if (detectedPort != _serverPort)
                {
                    _logger.LogInformation("🎯 [UltraThink Phase 13] 動的ポート検出: {ConfigPort} → {DetectedPort}", 
                        _serverPort, detectedPort);
                    _serverPort = detectedPort;
                }
                
                _logger.LogInformation("🎯 [NLLB-200] NLLB-200モード - ポート: {Port}, スクリプト: {Script}", 
                    _serverPort, Path.GetFileName(_serverScriptPath));
            }
            else
            {
                // デフォルト設定から動的にポートとスクリプトパスを取得（レガシー互換性）
                _serverPort = 5556; // 固定値使用

                // CTranslate2版サーバー優先、フォールバックで旧版
                var ct2ScriptPath = Path.Combine(projectRoot, "scripts", "nllb_translation_server_ct2.py");
                var legacyScriptPath = Path.Combine(projectRoot, "scripts", "nllb_translation_server.py");

                if (File.Exists(ct2ScriptPath))
                {
                    _serverScriptPath = ct2ScriptPath;
                    _logger.LogInformation("✅ CTranslate2版サーバーを使用: {Script}", Path.GetFileName(_serverScriptPath));
                }
                else if (File.Exists(legacyScriptPath))
                {
                    _serverScriptPath = legacyScriptPath;
                    _logger.LogWarning("⚠️ CTranslate2版が見つからず、旧版サーバーを使用: {Script}", Path.GetFileName(_serverScriptPath));
                }
                else
                {
                    _serverScriptPath = ct2ScriptPath; // エラーメッセージ用
                    _logger.LogError("❌ 翻訳サーバースクリプトが見つかりません: CT2={CT2}, Legacy={Legacy}", ct2ScriptPath, legacyScriptPath);
                }
                
                // UltraThink Phase 13: レガシーモードでも動的ポート検出を実行
                _logger.LogInformation("🔍 [UltraThink Phase 13] ConfigureServerSettings(レガシー): 動的ポート検出開始 (現在の固定ポート: {Port})", _serverPort);
                var detectedPort = GetCurrentServerPort();
                _logger.LogInformation("🔍 [UltraThink Phase 13] ConfigureServerSettings(レガシー): 検出結果 {ConfigPort} → {DetectedPort}", _serverPort, detectedPort);
                if (detectedPort != _serverPort)
                {
                    _logger.LogInformation("🔧 [UltraThink Phase 13] レガシー動的ポート検出: {ConfigPort} → {DetectedPort}", 
                        _serverPort, detectedPort);
                    _serverPort = detectedPort;
                }
                
                _logger.LogInformation("🔧 [NLLB-200] デフォルトモード - ポート: {Port}, スクリプト: {Script}", 
                    _serverPort, Path.GetFileName(_serverScriptPath));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ サーバー設定エラー - デフォルト設定（NLLB-200）を使用");
            _serverPort = 5556;

            // CTranslate2版サーバー優先、フォールバックで旧版
            var ct2ScriptPath = Path.Combine(projectRoot, "scripts", "nllb_translation_server_ct2.py");
            var legacyScriptPath = Path.Combine(projectRoot, "scripts", "nllb_translation_server.py");

            if (File.Exists(ct2ScriptPath))
            {
                _serverScriptPath = ct2ScriptPath;
                _logger.LogInformation("✅ CTranslate2版サーバーを使用: {Script}", Path.GetFileName(_serverScriptPath));
            }
            else if (File.Exists(legacyScriptPath))
            {
                _serverScriptPath = legacyScriptPath;
                _logger.LogWarning("⚠️ CTranslate2版が見つからず、旧版サーバーを使用: {Script}", Path.GetFileName(_serverScriptPath));
            }
            else
            {
                _serverScriptPath = ct2ScriptPath; // エラーメッセージ用
                _logger.LogError("❌ 翻訳サーバースクリプトが見つかりません: CT2={CT2}, Legacy={Legacy}", ct2ScriptPath, legacyScriptPath);
            }
            
            // UltraThink Phase 13: エラー時でも動的ポート検出を試行
            try
            {
                var detectedPort = GetCurrentServerPort();
                if (detectedPort != _serverPort)
                {
                    _logger.LogInformation("⚠️ [UltraThink Phase 13] エラー時動的ポート検出: {ConfigPort} → {DetectedPort}", 
                        _serverPort, detectedPort);
                    _serverPort = detectedPort;
                }
            }
            catch (Exception detectionEx)
            {
                _logger.LogWarning(detectionEx, "動的ポート検出も失敗 - 固定ポート {Port} を使用", _serverPort);
            }
        }
    }
    
    /// <summary>
    /// 🎯 [DYNAMIC_CONFIG] 実行時設定取得
    /// </summary>
    private TranslationEngine GetCurrentTranslationEngine()
    {
        return TranslationEngine.NLLB200; // 固定値使用
    }
}