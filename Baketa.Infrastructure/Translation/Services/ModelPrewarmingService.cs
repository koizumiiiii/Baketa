using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Models.Translation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// モデル事前ウォーミングサービス
/// Phase 1: 初回起動問題の根本解決機能
/// アプリ起動と並行してバックグラウンドでPythonサーバー初期化
/// </summary>
public sealed class ModelPrewarmingService : IHostedService, IDisposable
{
    private readonly IPythonServerManager _pythonServerManager;
    private readonly ModelCacheManager _modelCacheManager;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILanguageConfigurationService _languageConfig;
    private readonly ILogger<ModelPrewarmingService> _logger;
    
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _warmupTask;
    private bool _disposed;

    public ModelPrewarmingService(
        IPythonServerManager pythonServerManager,
        ModelCacheManager modelCacheManager,
        IEventAggregator eventAggregator,
        ILanguageConfigurationService languageConfig,
        ILogger<ModelPrewarmingService> logger)
    {
        _pythonServerManager = pythonServerManager ?? throw new ArgumentNullException(nameof(pythonServerManager));
        _modelCacheManager = modelCacheManager ?? throw new ArgumentNullException(nameof(modelCacheManager));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _languageConfig = languageConfig ?? throw new ArgumentNullException(nameof(languageConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// サービス開始（アプリケーション起動時）
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🚀 ModelPrewarmingService開始 - バックグラウンド事前ウォーミング");
        
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        // バックグラウンドでウォーミングタスクを開始
        _warmupTask = Task.Run(async () => await ExecuteWarmupAsync(_cancellationTokenSource.Token)
            .ConfigureAwait(false), _cancellationTokenSource.Token);
        
        _logger.LogInformation("✅ ModelPrewarmingService開始完了 - UI初期化と並行実行中");
        
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// サービス停止
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🛑 ModelPrewarmingService停止開始");
        
        if (_cancellationTokenSource != null)
        {
            await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);
        }
        
        if (_warmupTask != null)
        {
            try
            {
                await _warmupTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("⏰ ウォーミングタスクがキャンセルされました");
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("⚠️ ウォーミングタスクの停止がタイムアウト");
            }
        }
        
        _logger.LogInformation("✅ ModelPrewarmingService停止完了");
    }

    /// <summary>
    /// 事前ウォーミング実行
    /// </summary>
    private async Task ExecuteWarmupAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("🔥 事前ウォーミング実行開始");
            
            // Phase 0: 初期化開始イベント発行
            await PublishStatusAsync(false, "モデル事前ウォーミング開始", 
                "バックグラウンドでNLLBモデル初期化中").ConfigureAwait(false);
            
            // 1. モデルキャッシュ状況の確認
            var cacheInfo = await _modelCacheManager.GetCacheInfoAsync().ConfigureAwait(false);
            
            if (cacheInfo.IsModelCached)
            {
                _logger.LogInformation("✅ モデルキャッシュ確認済み - 高速起動可能");
                
                // キャッシュ済みの場合は短時間でサーバー起動
                await StartServerWithRetryAsync(cancellationToken, isFromCache: true).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning("⚠️ 初回起動 - モデルダウンロードが発生します（約2.4GB）");
                _logger.LogInformation("💡 この処理は初回のみです。2回目以降は高速化されます。");
                
                // 初回起動の場合はより長い時間をかけてサーバー起動
                await StartServerWithRetryAsync(cancellationToken, isFromCache: false).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("⏹️ 事前ウォーミングがキャンセルされました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 事前ウォーミング実行エラー");
            
            // エラー分類と詳細なユーザー向けメッセージ生成
            var errorInfo = ClassifyAndCreateErrorMessage(ex, "事前ウォーミング");
            
            // Phase 0: 詳細エラー状態イベント発行
            await PublishStatusAsync(false, errorInfo.UserMessage, errorInfo.Details).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// リトライ付きサーバー起動
    /// </summary>
    private async Task StartServerWithRetryAsync(CancellationToken cancellationToken, bool isFromCache)
    {
        const int maxRetries = 3;
        var retryDelay = isFromCache ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30);
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                _logger.LogInformation("🔄 Pythonサーバー起動試行 {Attempt}/{MaxRetries} (Cache: {IsCache})", 
                    attempt, maxRetries, isFromCache);
                
                // 現在の言語設定に基づくサーバー起動（STEP7 IsReady失敗修正）
                var currentLanguagePair = _languageConfig.GetCurrentLanguagePair();
                var languagePairKey = currentLanguagePair.ToServerKey();

                _logger.LogInformation("🔄 動的言語ペア使用: {LanguagePairKey} (ハードコード'en-ja'から修正)", languagePairKey);

                var serverInfo = await _pythonServerManager.StartServerAsync(languagePairKey).ConfigureAwait(false);
                
                _logger.LogInformation("🎉 事前ウォーミング成功: Port {Port}", serverInfo.Port);
                
                // Phase 0: 成功イベント発行
                await PublishStatusAsync(true, "翻訳機能準備完了", 
                    $"事前ウォーミング成功 - Port {serverInfo.Port}").ConfigureAwait(false);
                
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger.LogWarning(ex, "⚠️ サーバー起動失敗 {Attempt}/{MaxRetries}: {Error}", 
                    attempt, maxRetries, ex.Message);
                
                // 指数バックオフで待機
                var delay = TimeSpan.FromMilliseconds(retryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        
        // 最終試行が失敗した場合 - より詳細なエラー情報を提供
        _logger.LogError("❌ 事前ウォーミング最終失敗 - 全{MaxRetries}回の試行が失敗", maxRetries);
        
        // 最終失敗のユーザー向け詳細メッセージ
        var finalErrorMessage = isFromCache 
            ? "翻訳サーバーの起動に失敗しました" 
            : "翻訳モデルのダウンロード・初期化に失敗しました";
            
        var finalErrorDetails = isFromCache 
            ? $"キャッシュ済みモデルでの起動も失敗（{maxRetries}回試行）。ネットワーク接続またはPython環境を確認してください。"
            : $"初回セットアップに失敗（{maxRetries}回試行）。インターネット接続と空き容量（約3GB）を確認してください。";
        
        // Phase 0: 詳細最終失敗イベント発行
        await PublishStatusAsync(false, finalErrorMessage, finalErrorDetails).ConfigureAwait(false);
    }

    /// <summary>
    /// 状態変更イベント発行ヘルパー
    /// </summary>
    private async Task PublishStatusAsync(bool isReady, string message, string details)
    {
        try
        {
            var statusEvent = new PythonServerStatusChangedEvent
            {
                IsServerReady = isReady,
                ServerPort = 0, // ウォーミングサービスでは具体的なポート情報なし
                StatusMessage = message,
                Details = details
            };

            await _eventAggregator.PublishAsync(statusEvent).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ 状態イベント発行エラー");
        }
    }

    /// <summary>
    /// エラー分類とユーザー向けメッセージ生成
    /// </summary>
    private ErrorInfo ClassifyAndCreateErrorMessage(Exception ex, string operation)
    {
        return ex switch
        {
            // ネットワーク関連エラー
            HttpRequestException or 
            System.Net.Sockets.SocketException or 
            TaskCanceledException => new ErrorInfo
            {
                UserMessage = "ネットワーク接続エラー",
                Details = $"{operation}中にネットワークエラーが発生しました。インターネット接続を確認してください。詳細: {ex.Message}"
            },
            
            // ディスク容量不足
            IOException ioEx when ioEx.Message.Contains("space") || ioEx.Message.Contains("容量") => new ErrorInfo
            {
                UserMessage = "ディスク容量不足",
                Details = $"モデルファイル（約3GB）のダウンロードに必要な空き容量が不足しています。詳細: {ex.Message}"
            },
            
            // Python環境エラー
            Exception pythonEx when pythonEx.Message.Contains("python") || 
                                   pythonEx.Message.Contains("pip") || 
                                   pythonEx.Message.Contains("torch") => new ErrorInfo
            {
                UserMessage = "Python環境エラー",
                Details = $"Python環境またはライブラリに問題があります。Python 3.10+とrequirements.txtの依存関係を確認してください。詳細: {ex.Message}"
            },
            
            // ファイルアクセスエラー
            UnauthorizedAccessException or 
            DirectoryNotFoundException or 
            FileNotFoundException => new ErrorInfo
            {
                UserMessage = "ファイルアクセスエラー",
                Details = $"必要なファイルまたはフォルダーにアクセスできません。管理者権限またはフォルダー権限を確認してください。詳細: {ex.Message}"
            },
            
            // メモリ不足エラー
            OutOfMemoryException => new ErrorInfo
            {
                UserMessage = "メモリ不足",
                Details = "翻訳モデルの読み込みに必要なメモリが不足しています。他のアプリケーションを終了してから再試行してください。"
            },
            
            // 一般的なエラー
            _ => new ErrorInfo
            {
                UserMessage = "翻訳機能初期化エラー",
                Details = $"{operation}中に予期しないエラーが発生しました。再起動で解決する場合があります。詳細: {ex.Message}"
            }
        };
    }

    /// <summary>
    /// リソース解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        
        _disposed = true;
        
        _logger.LogDebug("🗑️ ModelPrewarmingService リソース解放完了");
    }
}

/// <summary>
/// エラー情報
/// </summary>
internal sealed record ErrorInfo
{
    /// <summary>ユーザー向けメッセージ</summary>
    public required string UserMessage { get; init; }
    
    /// <summary>詳細情報</summary>
    public required string Details { get; init; }
}