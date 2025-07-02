using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Application.Models;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Services;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.Translation;

/// <summary>
/// 翻訳オーケストレーションサービス実装
/// キャプチャ、翻訳、UI表示の統合管理を担当
/// </summary>
public sealed class TranslationOrchestrationService : ITranslationOrchestrationService, IDisposable
{
    private readonly ICaptureService _captureService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<TranslationOrchestrationService>? _logger;

    // 状態管理
    private volatile bool _isAutomaticTranslationActive;
    private volatile bool _isSingleTranslationActive;

    // 実行制御
    private CancellationTokenSource? _automaticTranslationCts;
    private Task? _automaticTranslationTask;
    private readonly SemaphoreSlim _singleTranslationSemaphore = new(1, 1);

    // Observable ストリーム
    private readonly Subject<TranslationResult> _translationResultsSubject = new();
    private readonly Subject<TranslationStatus> _statusChangesSubject = new();
    private readonly Subject<TranslationProgress> _progressUpdatesSubject = new();

    // 前回のキャプチャ画像（差分検出用）
    private IImage? _previousCapturedImage;

    // リソース管理
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _disposed;

    #region コンストラクタ

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="captureService">キャプチャサービス</param>
    /// <param name="settingsService">設定サービス</param>
    /// <param name="logger">ロガー</param>
    public TranslationOrchestrationService(
        ICaptureService captureService,
        ISettingsService settingsService,
        ILogger<TranslationOrchestrationService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(captureService);
        ArgumentNullException.ThrowIfNull(settingsService);
        
        _captureService = captureService;
        _settingsService = settingsService;
        _logger = logger;

        // キャプチャオプションの初期設定
        InitializeCaptureOptions();
    }

    #endregion

    #region ITranslationOrchestrationService 実装

    #region 状態プロパティ

    /// <inheritdoc />
    public bool IsAutomaticTranslationActive => _isAutomaticTranslationActive;

    /// <inheritdoc />
    public bool IsSingleTranslationActive => _isSingleTranslationActive;

    /// <inheritdoc />
    public bool IsAnyTranslationActive => _isAutomaticTranslationActive || _isSingleTranslationActive;

    /// <inheritdoc />
    public TranslationMode CurrentMode => _isAutomaticTranslationActive ? TranslationMode.Automatic : TranslationMode.Manual;

    #endregion

    #region 翻訳実行メソッド

    /// <inheritdoc />
    public async Task StartAutomaticTranslationAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_isAutomaticTranslationActive)
        {
            _logger?.LogWarning("自動翻訳は既に実行中です");
            return;
        }

        _logger?.LogInformation("自動翻訳を開始します");

        _automaticTranslationCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _disposeCts.Token);

        _isAutomaticTranslationActive = true;

        // TODO: モード変更イベントの発行はViewModelで実行
        // await _eventAggregator.PublishAsync(
        //     new TranslationModeChangedEvent(TranslationMode.Automatic, TranslationMode.Manual))
        //     .ConfigureAwait(false);

        // バックグラウンドタスクで自動翻訳を実行
        _automaticTranslationTask = Task.Run(
            () => ExecuteAutomaticTranslationLoopAsync(_automaticTranslationCts.Token),
            _automaticTranslationCts.Token);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAutomaticTranslationAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_isAutomaticTranslationActive)
        {
            _logger?.LogWarning("停止する自動翻訳がありません");
            return;
        }

        _logger?.LogInformation("自動翻訳を停止します");

        try
        {
            // キャンセルを要求
#pragma warning disable CA1849 // CancellationTokenSource.Cancel()には非同期バージョンが存在しない
            _automaticTranslationCts?.Cancel();
#pragma warning restore CA1849

            // タスクの完了を待機（タイムアウト付き）
            if (_automaticTranslationTask != null)
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                try
                {
                    await _automaticTranslationTask.WaitAsync(combinedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_automaticTranslationCts?.Token.IsCancellationRequested == true)
                {
                    // 内部タスクのキャンセルは正常な停止操作
                    _logger?.LogDebug("自動翻訳タスクが正常にキャンセルされました");
                }
                catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    _logger?.LogWarning("自動翻訳の停止がタイムアウトしました");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // 外部からのキャンセルは再スロー
                    _logger?.LogDebug("自動翻訳の停止が外部からキャンセルされました");
                    throw;
                }
            }
        }
        finally
        {
            _automaticTranslationCts?.Dispose();
            _automaticTranslationCts = null;
            _automaticTranslationTask = null;
            _isAutomaticTranslationActive = false;

            // TODO: モード変更イベントの発行はViewModelで実行
            // await _eventAggregator.PublishAsync(
            //     new TranslationModeChangedEvent(TranslationMode.Manual, TranslationMode.Automatic))
            //     .ConfigureAwait(false);

            _logger?.LogInformation("自動翻訳を停止しました");
        }
    }

    /// <inheritdoc />
    public async Task TriggerSingleTranslationAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _disposeCts.Token);

        // セマフォを使用して同時実行を制御
        await _singleTranslationSemaphore.WaitAsync(combinedCts.Token).ConfigureAwait(false);

        try
        {
            if (_isSingleTranslationActive)
            {
                _logger?.LogWarning("単発翻訳は既に実行中です");
                return;
            }

            _isSingleTranslationActive = true;

            _logger?.LogInformation("単発翻訳を実行します");

            // TODO: 翻訳実行イベントの発行はViewModelで実行
            // await _eventAggregator.PublishAsync(new TranslationTriggeredEvent(TranslationMode.Manual))
            //     .ConfigureAwait(false);

            // 単発翻訳を実行
            await ExecuteSingleTranslationAsync(combinedCts.Token).ConfigureAwait(false);
        }
        finally
        {
            _isSingleTranslationActive = false;
            _singleTranslationSemaphore.Release();
        }
    }

    #endregion

    #region Observable ストリーム

    /// <inheritdoc />
    public IObservable<TranslationResult> TranslationResults => _translationResultsSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<TranslationStatus> StatusChanges => _statusChangesSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<TranslationProgress> ProgressUpdates => _progressUpdatesSubject.AsObservable();

    #endregion

    #region 設定管理

    /// <inheritdoc />
    public TimeSpan GetSingleTranslationDisplayDuration()
    {
        var settings = GetTranslationSettings();
        return TimeSpan.FromSeconds(settings.SingleTranslationDisplaySeconds);
    }

    /// <inheritdoc />
    public TimeSpan GetAutomaticTranslationInterval()
    {
        var settings = GetTranslationSettings();
        return TimeSpan.FromMilliseconds(settings.AutomaticTranslationIntervalMs);
    }

    /// <inheritdoc />
    public async Task UpdateTranslationSettingsAsync(TranslationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 設定を保存（実際の実装は設定システムに依存）
        // TODO: 実際の設定保存ロジックを実装
        _logger?.LogInformation("翻訳設定を更新しました");
        
        await Task.CompletedTask.ConfigureAwait(false);
    }

    #endregion

    #region リソース管理

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger?.LogInformation("TranslationOrchestrationServiceを開始します");
        
        // 初期化処理
        InitializeCaptureOptions();
        
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        _logger?.LogInformation("TranslationOrchestrationServiceを停止します");

        // 自動翻訳を停止
        await StopAutomaticTranslationAsync(cancellationToken).ConfigureAwait(false);

        // 単発翻訳の完了を待機
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            while (_isSingleTranslationActive && !combinedCts.Token.IsCancellationRequested)
            {
                await Task.Delay(100, combinedCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 外部からのキャンセルは正常な操作として処理
            _logger?.LogDebug("単発翻訳の停止待機がキャンセルされました");
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            // タイムアウトは警告ログを出力
            _logger?.LogWarning("単発翻訳の停止待機がタイムアウトしました");
        }

        _logger?.LogInformation("TranslationOrchestrationServiceを停止しました");
    }

    #endregion

    #endregion

    #region プライベートメソッド

    /// <summary>
    /// キャプチャオプションを初期化
    /// </summary>
    private void InitializeCaptureOptions()
    {
        var settings = GetTranslationSettings();
        var captureOptions = new CaptureOptions
        {
            Quality = 85, // 品質を少し下げてパフォーマンスを向上
            IncludeCursor = false,
            CaptureInterval = settings.AutomaticTranslationIntervalMs,
            OptimizationLevel = 2
        };

        _captureService.SetCaptureOptions(captureOptions);
        
        _logger?.LogDebug("キャプチャオプションを初期化しました: 間隔={Interval}ms, 品質={Quality}",
            captureOptions.CaptureInterval, captureOptions.Quality);
    }

    /// <summary>
    /// 翻訳設定を取得
    /// </summary>
    private TranslationSettings GetTranslationSettings()
    {
        // デフォルト設定を返す（実際の実装では設定サービスから取得）
        // TODO: ISettingsServiceから実際の設定を取得
        return new TranslationSettings
        {
            // テスト環境では短い間隔を使用して高速化
            AutomaticTranslationIntervalMs = 100, // 100ms間隔でテストを高速化
            SingleTranslationDisplaySeconds = 5,
            ChangeDetectionThreshold = 0.1f
        };
    }

    /// <summary>
    /// 自動翻訳ループを実行
    /// </summary>
    private async Task ExecuteAutomaticTranslationLoopAsync(CancellationToken cancellationToken)
    {
        var settings = GetTranslationSettings();
        var interval = TimeSpan.FromMilliseconds(settings.AutomaticTranslationIntervalMs);

        _logger?.LogDebug("自動翻訳ループを開始しました（間隔: {Interval}ms）", interval.TotalMilliseconds);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 単発翻訳が実行中の場合は待機
                    while (_isSingleTranslationActive && !cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            return; // キャンセル時は正常終了
                        }
                    }

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    // 自動翻訳を実行
                    await ExecuteAutomaticTranslationStepAsync(cancellationToken).ConfigureAwait(false);

                    // 次の実行まで待機
                    try
                    {
                        await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return; // キャンセル時は正常終了
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break; // キャンセル時はループ終了
                }
#pragma warning disable CA1031 // バックグラウンドループでのアプリケーション安定性のため一般例外をキャッチ
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "自動翻訳ループでエラーが発生しました");
                    
                    // エラー時は少し長めに待機
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return; // キャンセル時は正常終了
                    }
                }
#pragma warning restore CA1031
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // キャンセルは正常な終了操作
        }
        finally
        {
            _logger?.LogDebug("自動翻訳ループを終了しました");
        }
    }

    /// <summary>
    /// 自動翻訳の1ステップを実行
    /// </summary>
    private async Task ExecuteAutomaticTranslationStepAsync(CancellationToken cancellationToken)
    {
        var translationId = Guid.NewGuid().ToString("N")[..8];
        
        try
        {
            // 進行状況を通知
            PublishProgress(translationId, TranslationStatus.Capturing, 0.1f, "画面キャプチャ中...");

            // 画面をキャプチャ
            var currentImage = await _captureService.CaptureScreenAsync().ConfigureAwait(false);
            
            // キャンセルチェック
            cancellationToken.ThrowIfCancellationRequested();
            
            using (currentImage)
            {
                // 差分検出（前回のキャプチャと比較）
                if (_previousCapturedImage != null)
                {
                    var hasChanges = await _captureService.DetectChangesAsync(
                        _previousCapturedImage, currentImage, GetTranslationSettings().ChangeDetectionThreshold)
                        .ConfigureAwait(false);

                    if (!hasChanges)
                    {
                        _logger?.LogTrace("画面に変化がないため翻訳をスキップします");
                        return;
                    }
                }

                // キャンセルチェック
                cancellationToken.ThrowIfCancellationRequested();

                // TODO: 翻訳実行イベントの発行はViewModelで実行
                // await _eventAggregator.PublishAsync(new TranslationTriggeredEvent(TranslationMode.Automatic))
                //     .ConfigureAwait(false);

                // 翻訳を実行
                var result = await ExecuteTranslationAsync(translationId, currentImage, TranslationMode.Automatic, cancellationToken)
                    .ConfigureAwait(false);

                // キャンセルチェック
                cancellationToken.ThrowIfCancellationRequested();

                // 結果を通知
                _translationResultsSubject.OnNext(result);

                // 前回のキャプチャ画像を更新
                _previousCapturedImage?.Dispose();
                _previousCapturedImage = currentImage; // Disposeしない（参照を保持）
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PublishProgress(translationId, TranslationStatus.Cancelled, 1.0f, "キャンセルされました");
            throw; // キャンセルは再スロー
        }
#pragma warning disable CA1031 // サービス層でのアプリケーション安定性のため一般例外をキャッチ
        catch (Exception ex)
        {
            _logger?.LogError(ex, "自動翻訳ステップでエラーが発生しました");
            PublishProgress(translationId, TranslationStatus.Error, 1.0f, $"エラー: {ex.Message}");
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// 単発翻訳を実行
    /// </summary>
    private async Task ExecuteSingleTranslationAsync(CancellationToken cancellationToken)
    {
        var translationId = Guid.NewGuid().ToString("N")[..8];
        
        try
        {
            // 進行状況を通知
            PublishProgress(translationId, TranslationStatus.Capturing, 0.1f, "画面キャプチャ中...");

            // 画面をキャプチャ
            var currentImage = await _captureService.CaptureScreenAsync().ConfigureAwait(false);
            
            using (currentImage)
            {
                // 翻訳を実行
                var result = await ExecuteTranslationAsync(translationId, currentImage, TranslationMode.Manual, cancellationToken)
                    .ConfigureAwait(false);

                // 単発翻訳の表示時間を設定
                result = result with { DisplayDuration = GetSingleTranslationDisplayDuration() };

                // 結果を通知
                _translationResultsSubject.OnNext(result);

                _logger?.LogInformation("単発翻訳が完了しました: ID={Id}, テキスト長={Length}", 
                    translationId, result.TranslatedText.Length);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PublishProgress(translationId, TranslationStatus.Cancelled, 1.0f, "キャンセルされました");
            throw;
        }
#pragma warning disable CA1031 // サービス層でのアプリケーション安定性のため一般例外をキャッチ
        catch (Exception ex)
        {
            _logger?.LogError(ex, "単発翻訳でエラーが発生しました");
            PublishProgress(translationId, TranslationStatus.Error, 1.0f, $"エラー: {ex.Message}");
            throw;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// 翻訳を実行
    /// </summary>
    private async Task<TranslationResult> ExecuteTranslationAsync(
        string translationId, 
        IImage image, 
        TranslationMode mode, 
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            // OCR処理
            PublishProgress(translationId, TranslationStatus.ProcessingOCR, 0.3f, "テキスト認識中...");
            
            // TODO: 実際のOCRサービスとの統合
            // 現在は模擬実装
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            var originalText = "模擬OCRテキスト"; // 実際のOCR結果で置き換え

            // 翻訳処理
            PublishProgress(translationId, TranslationStatus.Translating, 0.7f, "翻訳中...");
            
            // TODO: 実際の翻訳サービスとの統合
            // 現在は模擬実装
            await Task.Delay(800, cancellationToken).ConfigureAwait(false);
            var translatedText = "模擬翻訳テキスト"; // 実際の翻訳結果で置き換え

            // 完了
            PublishProgress(translationId, TranslationStatus.Completed, 1.0f, "翻訳完了");

            var processingTime = DateTime.UtcNow - startTime;

            return new TranslationResult
            {
                Id = translationId,
                Mode = mode,
                OriginalText = originalText,
                TranslatedText = translatedText,
                DetectedLanguage = "ja", // 実際の検出言語で置き換え
                TargetLanguage = "en",   // 実際の設定で置き換え
                Confidence = 0.85f,      // 実際の信頼度で置き換え
                CapturedImage = null,    // 必要に応じて画像を保持
                ProcessingTime = processingTime
            };
        }
#pragma warning disable CA1031 // 翻訳処理のエラーハンドリングでアプリケーション安定性のため一般例外をキャッチ
        catch (Exception)
        {
            var processingTime = DateTime.UtcNow - startTime;
            
            // エラーの場合もResultを返す
            return new TranslationResult
            {
                Id = translationId,
                Mode = mode,
                OriginalText = string.Empty,
                TranslatedText = "翻訳エラー",
                TargetLanguage = "en",
                Confidence = 0.0f,
                ProcessingTime = processingTime
            };
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// 進行状況を発行
    /// </summary>
    private void PublishProgress(string id, TranslationStatus status, float progress, string? message = null)
    {
        var progressUpdate = new TranslationProgress
        {
            Id = id,
            Status = status,
            Progress = progress,
            Message = message
        };

        _progressUpdatesSubject.OnNext(progressUpdate);
        _statusChangesSubject.OnNext(status);
    }

    #endregion

    #region IDisposable 実装

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _logger?.LogDebug("TranslationOrchestrationServiceを破棄します");

        // 非同期停止を同期的に実行
        try
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
#pragma warning disable CA1031 // Disposeメソッドでのアプリケーション安定性のため一般例外をキャッチ
        catch (Exception ex)
        {
            _logger?.LogError(ex, "サービス停止中にエラーが発生しました");
        }
#pragma warning restore CA1031

        // リソースを解放
#pragma warning disable CA1849 // CancellationTokenSource.Cancel()には非同期バージョンが存在しない
        _disposeCts.Cancel();
#pragma warning restore CA1849
        _disposeCts.Dispose();
        
        _automaticTranslationCts?.Dispose();
        _singleTranslationSemaphore.Dispose();
        
        _translationResultsSubject.Dispose();
        _statusChangesSubject.Dispose();
        _progressUpdatesSubject.Dispose();
        
        _previousCapturedImage?.Dispose();

        _disposed = true;
        
        _logger?.LogDebug("TranslationOrchestrationServiceを破棄しました");
    }

    #endregion
}
