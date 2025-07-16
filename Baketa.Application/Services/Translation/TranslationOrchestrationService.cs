using System;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Application.Models;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Services;
using Baketa.Core.Settings;
using Baketa.Core.Utilities;
using Microsoft.Extensions.Logging;
using TranslationService = Baketa.Core.Abstractions.Translation.ITranslationService;
using Baketa.Core.Translation.Models;

namespace Baketa.Application.Services.Translation;

/// <summary>
/// 翻訳オーケストレーションサービス実装
/// キャプチャ、翻訳、UI表示の統合管理を担当
/// </summary>
public sealed class TranslationOrchestrationService : ITranslationOrchestrationService, IDisposable
{
    private readonly ICaptureService _captureService;
    private readonly ISettingsService _settingsService;
    private readonly IOcrEngine _ocrEngine;
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

    // 翻訳対象ウィンドウハンドル
    private IntPtr? _targetWindowHandle;

    // リソース管理
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _disposed;

    #region コンストラクタ

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="captureService">キャプチャサービス</param>
    /// <param name="settingsService">設定サービス</param>
    /// <param name="ocrEngine">OCRエンジン</param>
    /// <param name="logger">ロガー</param>
    public TranslationOrchestrationService(
        ICaptureService captureService,
        ISettingsService settingsService,
        IOcrEngine ocrEngine,
        ILogger<TranslationOrchestrationService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(captureService);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(ocrEngine);
        
        _captureService = captureService;
        _settingsService = settingsService;
        _ocrEngine = ocrEngine;
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
    public async Task StartAutomaticTranslationAsync(IntPtr? targetWindowHandle = null, CancellationToken cancellationToken = default)
    {
        DebugLogUtility.WriteLog($"🎬 StartAutomaticTranslationAsync呼び出し");
        DebugLogUtility.WriteLog($"   🗑️ Disposed: {_disposed}");
        DebugLogUtility.WriteLog($"   🔄 すでにアクティブ: {_isAutomaticTranslationActive}");
        DebugLogUtility.WriteLog($"   🎯 対象ウィンドウハンドル: {(targetWindowHandle?.ToString() ?? "null (画面全体)")}");
        
        // 翻訳対象ウィンドウハンドルを保存
        _targetWindowHandle = targetWindowHandle;
        
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_isAutomaticTranslationActive)
        {
            DebugLogUtility.WriteLog($"⚠️ 自動翻訳は既に実行中です");
            _logger?.LogWarning("自動翻訳は既に実行中です");
            return;
        }

        DebugLogUtility.WriteLog($"🎬 自動翻訳を開始します");
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
    public async Task TriggerSingleTranslationAsync(IntPtr? targetWindowHandle = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        // 翻訳対象ウィンドウハンドルを保存
        _targetWindowHandle = targetWindowHandle;

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
        Console.WriteLine($"🔄 ExecuteAutomaticTranslationLoopAsync開始");
        Console.WriteLine($"   ⏱️ 開始時キャンセル要求: {cancellationToken.IsCancellationRequested}");
        
        var settings = GetTranslationSettings();
        var interval = TimeSpan.FromMilliseconds(settings.AutomaticTranslationIntervalMs);

        Console.WriteLine($"🔄 自動翻訳ループを開始しました（間隔: {interval.TotalMilliseconds}ms）");
        _logger?.LogDebug("自動翻訳ループを開始しました（間隔: {Interval}ms）", interval.TotalMilliseconds);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"🔄 自動翻訳ループ実行中 - キャンセル: {cancellationToken.IsCancellationRequested}");
                Console.WriteLine($"   🔒 単発翻訳実行中: {_isSingleTranslationActive}");
                
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
        DebugLogUtility.WriteLog($"🎯 自動翻訳ステップ開始: ID={translationId}");
        DebugLogUtility.WriteLog($"   ⏱️ 開始時キャンセル要求: {cancellationToken.IsCancellationRequested}");
        DebugLogUtility.WriteLog($"   📡 CaptureServiceが利用可能: {_captureService != null}");
        
        IImage? currentImage = null;
        try
        {
            // 進行状況を通知
            PublishProgress(translationId, TranslationStatus.Capturing, 0.1f, "画面キャプチャ中...");

            // 画面またはウィンドウをキャプチャ
            if (_targetWindowHandle.HasValue)
            {
                DebugLogUtility.WriteLog($"📷 ウィンドウキャプチャ開始: Handle={_targetWindowHandle.Value}");
                currentImage = await _captureService.CaptureWindowAsync(_targetWindowHandle.Value).ConfigureAwait(false);
                DebugLogUtility.WriteLog($"📷 ウィンドウキャプチャ完了: {(currentImage != null ? "成功" : "失敗")}");
            }
            else
            {
                DebugLogUtility.WriteLog($"📷 画面全体キャプチャ開始");
                currentImage = await _captureService.CaptureScreenAsync().ConfigureAwait(false);
                DebugLogUtility.WriteLog($"📷 画面全体キャプチャ完了: {(currentImage != null ? "成功" : "失敗")}");
            }
            
            // キャンセルチェック
            cancellationToken.ThrowIfCancellationRequested();
            
            // 差分検出は一時的に無効化（ObjectDisposedException対策）
            // if (_previousCapturedImage != null)
            // {
            //     var hasChanges = await _captureService.DetectChangesAsync(
            //         _previousCapturedImage, currentImage, GetTranslationSettings().ChangeDetectionThreshold)
            //         .ConfigureAwait(false);
            //
            //     if (!hasChanges)
            //     {
            //         _logger?.LogTrace("画面に変化がないため翻訳をスキップします");
            //         currentImage?.Dispose();
            //         return;
            //     }
            // }

            // キャンセルチェック
            cancellationToken.ThrowIfCancellationRequested();

            // TODO: 翻訳実行イベントの発行はViewModelで実行
            // await _eventAggregator.PublishAsync(new TranslationTriggeredEvent(TranslationMode.Automatic))
            //     .ConfigureAwait(false);

            // null チェック
            if (currentImage == null)
            {
                DebugLogUtility.WriteLog($"❌ 画面キャプチャが失敗しました: ID={translationId}");
                return;
            }

            // 翻訳を実行
            DebugLogUtility.WriteLog($"🌍 翻訳処理開始: ID={translationId}");
            var result = await ExecuteTranslationAsync(translationId, currentImage, TranslationMode.Automatic, cancellationToken)
                .ConfigureAwait(false);
            DebugLogUtility.WriteLog($"🌍 翻訳処理完了: ID={translationId}");

            // キャンセルチェック
            cancellationToken.ThrowIfCancellationRequested();

            // 結果を通知（UI層でスケジューラ制御）
            DebugLogUtility.WriteLog($"📤 翻訳結果をObservableに発行: '{result.TranslatedText}'");
            _translationResultsSubject.OnNext(result);
            DebugLogUtility.WriteLog($"✅ 翻訳結果発行完了");

            // 前回のキャプチャ画像を更新（一旦無効化）
            // var oldImage = _previousCapturedImage;
            // _previousCapturedImage = currentImage; // 参照を保持
            // oldImage?.Dispose(); // 古い画像を安全に破棄
            
            // 一旦画像を破棄してObjectDisposedExceptionを回避
            currentImage?.Dispose();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DebugLogUtility.WriteLog($"❌ 自動翻訳ステップがキャンセルされました: ID={translationId}");
            currentImage?.Dispose(); // キャンセル時のリソース破棄
            PublishProgress(translationId, TranslationStatus.Cancelled, 1.0f, "キャンセルされました");
            throw; // キャンセルは再スロー
        }
#pragma warning disable CA1031 // サービス層でのアプリケーション安定性のため一般例外をキャッチ
        catch (Exception ex)
        {
            Console.WriteLine($"💥 自動翻訳ステップでエラー: ID={translationId}, エラー={ex.Message}");
            currentImage?.Dispose(); // エラー時のリソース破棄
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

                // 結果を通知（UI層でスケジューラ制御）
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
        Console.WriteLine($"🚀 ExecuteTranslationAsync開始:");
        Console.WriteLine($"   🆔 翻訳ID: {translationId}");
        Console.WriteLine($"   📷 画像: {image?.GetType().Name ?? "null"}");
        Console.WriteLine($"   🎯 モード: {mode}");
        Console.WriteLine($"   ⏱️ キャンセル要求: {cancellationToken.IsCancellationRequested}");
        
        var startTime = DateTime.UtcNow;
        string originalText = string.Empty;
        double ocrConfidence = 0.0;

        try
        {
            // OCR処理
            PublishProgress(translationId, TranslationStatus.ProcessingOCR, 0.3f, "テキスト認識中...");
            
            DebugLogUtility.WriteLog($"🔍 OCRエンジン状態チェック - IsInitialized: {_ocrEngine.IsInitialized}");
            
            // OCRエンジンが初期化されていない場合は初期化
            if (!_ocrEngine.IsInitialized)
            {
                Console.WriteLine($"🛠️ OCRエンジン初期化開始");
                // System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🛠️ OCRエンジン初期化開始{Environment.NewLine}");
                
                var settings = new OcrEngineSettings
                {
                    Language = "jpn", // 日本語
                    DetectionThreshold = 0.3,
                    RecognitionThreshold = 0.5
                };
                
                try
                {
                    await _ocrEngine.InitializeAsync(settings, cancellationToken).ConfigureAwait(false);
                    Console.WriteLine($"✅ OCRエンジン初期化完了");
                    // System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ OCRエンジン初期化完了{Environment.NewLine}");
                }
                catch (Exception initEx)
                {
                    Console.WriteLine($"❌ OCRエンジン初期化エラー: {initEx.Message}");
                    // System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ OCRエンジン初期化エラー: {initEx.Message}{Environment.NewLine}");
                    throw;
                }
            }
            
            // 実際のOCR処理を実行
            Console.WriteLine($"🔍 画像オブジェクト確認:");
            Console.WriteLine($"   📷 画像オブジェクト: {image?.GetType().Name ?? "null"}");
            Console.WriteLine($"   📊 画像null判定: {image == null}");
            
            // System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 画像オブジェクト確認:{Environment.NewLine}");
            // System.IO.File.AppendAllText("debug_app_logs.txt", $"   📷 画像オブジェクト: {image?.GetType().Name ?? "null"}{Environment.NewLine}");
            // System.IO.File.AppendAllText("debug_app_logs.txt", $"   📊 画像null判定: {image == null}{Environment.NewLine}");
            
            try
            {
                DebugLogUtility.WriteLog($"🔍 OCR処理開始 - 画像サイズ: {image?.Width ?? 0}x{image?.Height ?? 0}");
                // System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 OCR処理開始 - 画像サイズ: {image?.Width ?? 0}x{image?.Height ?? 0}{Environment.NewLine}");
            }
            catch (Exception sizeEx)
            {
                DebugLogUtility.WriteLog($"❌ 画像サイズ取得エラー: {sizeEx.Message}");
                // System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ 画像サイズ取得エラー: {sizeEx.Message}{Environment.NewLine}");
                throw;
            }
            
            ArgumentNullException.ThrowIfNull(image, nameof(image));
            
            DebugLogUtility.WriteLog($"🤖 OCRエンジン呼び出し開始:");
            DebugLogUtility.WriteLog($"   🔧 エンジン名: {_ocrEngine?.EngineName ?? "(null)"}");
            DebugLogUtility.WriteLog($"   ✅ 初期化状態: {_ocrEngine?.IsInitialized ?? false}");
            DebugLogUtility.WriteLog($"   🌐 現在の言語: {_ocrEngine?.CurrentLanguage ?? "(null)"}");
            
            var ocrResults = await _ocrEngine.RecognizeAsync(image, cancellationToken: cancellationToken).ConfigureAwait(false);
            
            DebugLogUtility.WriteLog($"🤖 OCRエンジン呼び出し完了");
            
            DebugLogUtility.WriteLog($"📊 OCR結果: HasText={ocrResults.HasText}, TextRegions数={ocrResults.TextRegions?.Count ?? 0}");
            
            // 詳細なOCRデバッグ情報を表示
            if (ocrResults.TextRegions != null && ocrResults.TextRegions.Count > 0)
            {
                DebugLogUtility.WriteLog($"🔍 詳細なOCRテキストリージョン情報:");
                for (int i = 0; i < Math.Min(5, ocrResults.TextRegions.Count); i++) // 最初の5個だけ表示
                {
                    var region = ocrResults.TextRegions[i];
                    DebugLogUtility.WriteLog($"   リージョン {i + 1}:");
                    DebugLogUtility.WriteLog($"     📖 テキスト: '{region.Text ?? "(null)"}'");
                    DebugLogUtility.WriteLog($"     📊 信頼度: {region.Confidence:F4}");
                    DebugLogUtility.WriteLog($"     📍 座標: X={region.Bounds.X}, Y={region.Bounds.Y}, W={region.Bounds.Width}, H={region.Bounds.Height}");
                    DebugLogUtility.WriteLog($"     🔢 テキスト長: {region.Text?.Length ?? 0}");
                }
                if (ocrResults.TextRegions.Count > 5)
                {
                    DebugLogUtility.WriteLog($"   ... 他 {ocrResults.TextRegions.Count - 5} 個のリージョン");
                }
            }
            else
            {
                DebugLogUtility.WriteLog($"📝 TextRegionsが空またはnullです");
            }
            
            if (ocrResults.HasText)
            {
                originalText = ocrResults.Text;
                ocrConfidence = ocrResults.TextRegions?.Count > 0 
                    ? ocrResults.TextRegions.Average(r => r.Confidence) 
                    : 0.0;
                
                DebugLogUtility.WriteLog($"✅ OCR認識成功:");
                DebugLogUtility.WriteLog($"   📖 認識テキスト: '{originalText}'");
                DebugLogUtility.WriteLog($"   📊 平均信頼度: {ocrConfidence:F2}");
                DebugLogUtility.WriteLog($"   🔢 テキスト長: {originalText.Length}");
                DebugLogUtility.WriteLog($"   🔤 テキストがnullまたは空: {string.IsNullOrEmpty(originalText)}");
                DebugLogUtility.WriteLog($"   🔤 テキストが空白のみ: {string.IsNullOrWhiteSpace(originalText)}");
                    
                _logger?.LogDebug("OCR認識成功: テキスト長={Length}, 信頼度={Confidence:F2}", 
                    originalText.Length, ocrConfidence);
            }
            else
            {
                DebugLogUtility.WriteLog("❌ OCR処理でテキストが検出されませんでした");
                // System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ OCR処理でテキストが検出されませんでした{Environment.NewLine}");
                _logger?.LogWarning("OCR処理でテキストが検出されませんでした");
                originalText = string.Empty;
            }

            // 翻訳処理
            PublishProgress(translationId, TranslationStatus.Translating, 0.7f, "翻訳中...");
            
            string translatedText;
            if (!string.IsNullOrWhiteSpace(originalText))
            {
                try
                {
                    // 一時的に簡単なMock翻訳を使用（実際の翻訳サービス統合までの暫定処理）
                    DebugLogUtility.WriteLog($"🌍 Mock翻訳開始: '{originalText}'");
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false); // 翻訳処理のシミュレート
                    
                    // MockTranslationEngineと同様のフォーマットで翻訳結果を生成
                    translatedText = $"[日→英] {originalText}";
                    DebugLogUtility.WriteLog($"🌍 Mock翻訳完了: '{translatedText}'");
                }
                catch (Exception translationEx)
                {
                    DebugLogUtility.WriteLog($"⚠️ 翻訳エラー: {translationEx.Message}");
                    _logger?.LogWarning(translationEx, "翻訳処理でエラーが発生しました");
                    translatedText = $"翻訳エラー: {translationEx.Message}";
                }
            }
            else
            {
                translatedText = "テキストが検出されませんでした";
            }

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
                Confidence = (float)ocrConfidence,
                CapturedImage = null,    // 必要に応じて画像を保持
                ProcessingTime = processingTime
            };
        }
#pragma warning disable CA1031 // 翻訳処理のエラーハンドリングでアプリケーション安定性のため一般例外をキャッチ
        catch (Exception ex)
        {
            var processingTime = DateTime.UtcNow - startTime;
            
            Console.WriteLine($"❌ 翻訳処理で例外発生:");
            Console.WriteLine($"   🔍 例外タイプ: {ex.GetType().Name}");
            Console.WriteLine($"   📝 例外メッセージ: {ex.Message}");
            Console.WriteLine($"   📍 スタックトレース: {ex.StackTrace}");
            
            // System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ 翻訳処理で例外発生:{Environment.NewLine}");
            // System.IO.File.AppendAllText("debug_app_logs.txt", $"   🔍 例外タイプ: {ex.GetType().Name}{Environment.NewLine}");
            // System.IO.File.AppendAllText("debug_app_logs.txt", $"   📝 例外メッセージ: {ex.Message}{Environment.NewLine}");
            // System.IO.File.AppendAllText("debug_app_logs.txt", $"   📍 スタックトレース: {ex.StackTrace}{Environment.NewLine}");
            
            _logger?.LogError(ex, "翻訳処理で例外が発生しました: TranslationId={TranslationId}", translationId);
            
            // エラーの場合もResultを返す
            return new TranslationResult
            {
                Id = translationId,
                Mode = mode,
                OriginalText = string.Empty,
                TranslatedText = $"翻訳エラー: {ex.Message}",
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
