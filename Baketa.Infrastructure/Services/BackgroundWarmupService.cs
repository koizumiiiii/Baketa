using System.Collections.Concurrent;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Translation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services;

/// <summary>
/// バックグラウンドウォームアップサービス（Issue #143: 95%コールドスタート遅延削減）
/// OCRと翻訳エンジンの非同期初期化により、初回実行遅延を根絶
/// </summary>
public sealed class BackgroundWarmupService(
    IServiceProvider serviceProvider,
    ILogger<BackgroundWarmupService> logger) : IWarmupService, IDisposable
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly ILogger<BackgroundWarmupService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // ウォームアップ状態管理
    private volatile bool _isWarmupCompleted;
    private volatile bool _isOcrWarmupCompleted;
    private volatile bool _isTranslationWarmupCompleted;
    private double _warmupProgress; // volatileはdoubleに使用不可、lockで同期化

    // 🔥 [PHASE5.2E.1] エラー状態管理
    private WarmupStatus _status = WarmupStatus.NotStarted;
    private Exception? _lastError;

    // 同期制御
    private readonly SemaphoreSlim _warmupSemaphore = new(1, 1);
    private CancellationTokenSource? _warmupCancellationSource;
    private Task? _warmupTask;

    // エンジンインスタンスキャッシュ
    private readonly ConcurrentDictionary<Type, object> _engineCache = new();

    private bool _disposed;

    public bool IsWarmupCompleted => _isWarmupCompleted;
    public bool IsOcrWarmupCompleted => _isOcrWarmupCompleted;
    public bool IsTranslationWarmupCompleted => _isTranslationWarmupCompleted;
    public double WarmupProgress
    {
        get
        {
            lock (_lockObject)
            {
                return _warmupProgress;
            }
        }
    }

    // 🔥 [PHASE5.2E.1] エラー状態管理プロパティ
    public WarmupStatus Status
    {
        get
        {
            lock (_lockObject)
            {
                return _status;
            }
        }
        private set
        {
            lock (_lockObject)
            {
                _status = value;
            }
        }
    }

    public Exception? LastError
    {
        get
        {
            lock (_lockObject)
            {
                return _lastError;
            }
        }
        private set
        {
            lock (_lockObject)
            {
                _lastError = value;
            }
        }
    }

    private readonly object _lockObject = new();

    public event EventHandler<WarmupProgressEventArgs>? WarmupProgressChanged;

    public async Task StartWarmupAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _warmupSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_warmupTask?.IsCompleted == false)
            {
                _logger.LogDebug("ウォームアップは既に実行中です");
                return;
            }

            _logger.LogInformation("バックグラウンドウォームアップを開始します");

            // 新しいキャンセルトークンソース作成
            _warmupCancellationSource?.Dispose();
            _warmupCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // バックグラウンドでウォームアップ実行
            _warmupTask = ExecuteWarmupAsync(_warmupCancellationSource.Token);

            // ファイアアンドフォーゲットで実行継続
            _ = Task.Run(async () =>
            {
                try
                {
                    await _warmupTask.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "バックグラウンドウォームアップでエラーが発生しました");
                }
            }, cancellationToken);
        }
        finally
        {
            _warmupSemaphore.Release();
        }
    }

    public async Task<bool> WaitForWarmupAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (_isWarmupCompleted)
        {
            return true;
        }

        if (_warmupTask == null)
        {
            _logger.LogWarning("ウォームアップが開始されていません");
            return false;
        }

        try
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            await _warmupTask.WaitAsync(combinedCts.Token).ConfigureAwait(false);
            return _isWarmupCompleted;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("ウォームアップ待機がキャンセルされました");
            return false;
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("ウォームアップ待機がタイムアウトしました: {Timeout}", timeout);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ウォームアップ待機中にエラーが発生しました");
            return false;
        }
    }

    private async Task ExecuteWarmupAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 🔥 [PHASE5.2E.1] ウォームアップ開始状態を設定
            Status = WarmupStatus.Running;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            ReportProgress(0.0, "ウォームアップ開始", WarmupPhase.Starting);

            // フェーズ1: GPU環境検出（10%）
            await WarmupGpuEnvironmentAsync(cancellationToken).ConfigureAwait(false);
            ReportProgress(0.1, "GPU環境検出完了", WarmupPhase.GpuDetection);

            // フェーズ2: OCRエンジン初期化とウォームアップ（50%）
            await WarmupOcrEnginesAsync(cancellationToken).ConfigureAwait(false);
            _isOcrWarmupCompleted = true;
            ReportProgress(0.6, "OCRウォームアップ完了", WarmupPhase.OcrWarmup);

            // フェーズ3: 翻訳エンジン初期化とウォームアップ（40%）
            await WarmupTranslationEnginesAsync(cancellationToken).ConfigureAwait(false);
            _isTranslationWarmupCompleted = true;
            ReportProgress(1.0, "全ウォームアップ完了", WarmupPhase.Completed);

            _isWarmupCompleted = true;
            stopwatch.Stop();

            // 🔥 [PHASE5.2E.1] 正常完了状態を設定
            Status = WarmupStatus.Completed;
            LastError = null; // エラーをクリア

            _logger.LogInformation("バックグラウンドウォームアップ完了: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 🔥 [PHASE5.2E.1] キャンセル状態を設定
            Status = WarmupStatus.Cancelled;
            _logger.LogInformation("ウォームアップがキャンセルされました");
            throw;
        }
        catch (Exception ex)
        {
            // 🔥 [PHASE5.2E.1] エラー状態を設定
            Status = WarmupStatus.Failed;
            LastError = ex;

            _logger.LogError(ex, "ウォームアップ中にエラーが発生しました");
            ReportProgress(_warmupProgress, $"エラー: {ex.Message}", WarmupPhase.Starting);
            throw;
        }
    }

    private async Task WarmupGpuEnvironmentAsync(CancellationToken cancellationToken)
    {
        try
        {
            var gpuDetector = _serviceProvider.GetService<IGpuEnvironmentDetector>();
            if (gpuDetector != null)
            {
                _logger.LogDebug("GPU環境検出を実行中...");
                var gpuInfo = await gpuDetector.DetectEnvironmentAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("GPU環境検出完了: {GpuName}, VRAM: {VramMB}MB",
                    gpuInfo.GpuName, gpuInfo.AvailableMemoryMB);

                _engineCache.TryAdd(typeof(IGpuEnvironmentDetector), gpuDetector);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GPU環境検出でエラーが発生しましたが、処理を続行します");
        }
    }

    private async Task WarmupOcrEnginesAsync(CancellationToken cancellationToken)
    {
        try
        {
            ReportProgress(0.2, "OCRエンジン初期化中", WarmupPhase.OcrInitialization);

            // IOcrEngineサービスを取得
            var ocrEngine = _serviceProvider.GetService<IOcrEngine>();
            if (ocrEngine == null)
            {
                _logger.LogWarning("IOcrEngineサービスが見つかりません");
                return;
            }

            _logger.LogDebug("OCRエンジン初期化: {EngineName}", ocrEngine.EngineName);

            // OCRエンジン初期化
            if (!ocrEngine.IsInitialized)
            {
                var initialized = await ocrEngine.InitializeAsync(null, cancellationToken).ConfigureAwait(false);
                if (!initialized)
                {
                    _logger.LogWarning("OCRエンジンの初期化に失敗しました");
                    return;
                }
            }

            ReportProgress(0.4, "OCRエンジンウォームアップ中", WarmupPhase.OcrWarmup);

            // OCRエンジンウォームアップ
            var warmupSuccess = await ocrEngine.WarmupAsync(cancellationToken).ConfigureAwait(false);
            if (warmupSuccess)
            {
                _logger.LogInformation("OCRエンジンウォームアップ完了: {EngineName}", ocrEngine.EngineName);
                _engineCache.TryAdd(typeof(IOcrEngine), ocrEngine);
            }
            else
            {
                _logger.LogWarning("OCRエンジンウォームアップに失敗しました");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCRエンジンウォームアップ中にエラーが発生しました");
            throw;
        }
    }

    private async Task WarmupTranslationEnginesAsync(CancellationToken cancellationToken)
    {
        try
        {
            ReportProgress(0.7, "翻訳エンジン初期化中", WarmupPhase.TranslationInitialization);

            // ITranslationEngineサービスを取得（全ての実装をウォームアップ）
            var translationEngines = _serviceProvider.GetServices<ITranslationEngine>().ToList();
            if (!translationEngines.Any())
            {
                _logger.LogWarning("ITranslationEngineサービスが見つかりません");
                return;
            }

            var warmupTasks = new List<Task>();
            var progressIncrement = 0.3 / translationEngines.Count; // 30%を翻訳エンジン数で分割

            foreach (var (engine, index) in translationEngines.Select((e, i) => (e, i)))
            {
                var currentProgress = 0.7 + (index * progressIncrement);

                warmupTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogDebug("翻訳エンジン初期化: {EngineName}", engine.Name);

                        // 翻訳エンジン初期化
                        var initialized = await engine.InitializeAsync().ConfigureAwait(false);
                        if (initialized)
                        {
                            // 準備完了確認
                            var ready = await engine.IsReadyAsync().ConfigureAwait(false);
                            if (ready)
                            {
                                _logger.LogInformation("翻訳エンジンウォームアップ完了: {EngineName}", engine.Name);
                                _engineCache.TryAdd(engine.GetType(), engine);
                            }
                        }

                        ReportProgress(currentProgress + progressIncrement,
                            $"{engine.Name}ウォームアップ完了", WarmupPhase.TranslationWarmup);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "翻訳エンジンウォームアップ失敗: {EngineName}", engine.Name);
                    }
                }, cancellationToken));
            }

            // 全ての翻訳エンジンウォームアップを並行実行
            await Task.WhenAll(warmupTasks).ConfigureAwait(false);

            _logger.LogInformation("全翻訳エンジンウォームアップ完了: {Count}個", translationEngines.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳エンジンウォームアップ中にエラーが発生しました");
            throw;
        }
    }

    private void ReportProgress(double progress, string status, WarmupPhase phase)
    {
        lock (_lockObject)
        {
            _warmupProgress = Math.Clamp(progress, 0.0, 1.0);
        }

        try
        {
            WarmupProgressChanged?.Invoke(this, new WarmupProgressEventArgs(progress, status, phase));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ウォームアップ進捗通知中にエラーが発生しました");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _warmupCancellationSource?.Cancel();
            _warmupCancellationSource?.Dispose();
            _warmupSemaphore?.Dispose();

            // キャッシュされたエンジンは手動でDispose呼び出さない（DI管理のため）
            _engineCache.Clear();

            _disposed = true;
        }
    }
}
