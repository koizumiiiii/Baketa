using Baketa.Application.Services.UI;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Abstractions.Monitoring;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Platform;
using Baketa.Core.Abstractions.Roi;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Models.Roi;
using Baketa.Core.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Application.Services.Learning;

/// <summary>
/// バックグラウンド学習サービス
/// </summary>
/// <remarks>
/// Issue #293 Phase 10: 学習駆動型投機的OCR
/// IHostedServiceとしてバックグラウンドで定期的にキャプチャ・OCRを実行し、ROI学習を加速します。
/// </remarks>
public sealed class BackgroundLearningService : BackgroundService
{
    private readonly ILearningScheduler _learningScheduler;
    private readonly ISpeculativeOcrService? _speculativeOcrService;
    private readonly IRoiManager? _roiManager;
    private readonly ICaptureService? _captureService;
    private readonly IWindowManager? _windowManager;
    private readonly IWindowManagementService? _windowManagementService;
    private readonly IResourceMonitor _resourceMonitor;
    private readonly ITranslationModeService _translationModeService;
    private readonly IOptionsMonitor<SpeculativeOcrSettings> _settingsMonitor;
    private readonly ILogger<BackgroundLearningService> _logger;

    // イベント購読管理
    private readonly IDisposable? _windowSelectionSubscription;

    // 状態管理
    private bool _isWindowSelected;
    private IntPtr _selectedWindowHandle;
    private DateTime _lastCaptureTime = DateTime.MinValue;
    private int _consecutiveSkipCount;
    private const int MaxConsecutiveSkips = 10;

    // [Issue #293] 連続エラー時の指数バックオフ
    private int _consecutiveErrorCount;
    private const int MaxConsecutiveErrors = 5;
    private static readonly TimeSpan MaxErrorRetryDelay = TimeSpan.FromSeconds(60);

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <remarks>
    /// Null許容パラメータの設計意図:
    /// - speculativeOcrService, roiManager, captureService, windowManager, windowManagementService はオプショナル依存
    /// - これらがnullの場合、サービスは正常に起動するが学習サイクルは実行されない
    /// - ShouldExecuteLearningCycle()でnullチェックを行い、安全にスキップする
    /// - 将来的にNullオブジェクトパターンを導入する場合は、DIコンテナで設定する
    /// </remarks>
    public BackgroundLearningService(
        ILearningScheduler learningScheduler,
        ISpeculativeOcrService? speculativeOcrService,
        IRoiManager? roiManager,
        ICaptureService? captureService,
        IWindowManager? windowManager,
        IWindowManagementService? windowManagementService,
        IResourceMonitor resourceMonitor,
        ITranslationModeService translationModeService,
        IOptionsMonitor<SpeculativeOcrSettings> settingsMonitor,
        ILogger<BackgroundLearningService> logger)
    {
        _learningScheduler = learningScheduler ?? throw new ArgumentNullException(nameof(learningScheduler));
        _speculativeOcrService = speculativeOcrService;
        _roiManager = roiManager;
        _captureService = captureService;
        _windowManager = windowManager;
        _windowManagementService = windowManagementService;
        _resourceMonitor = resourceMonitor ?? throw new ArgumentNullException(nameof(resourceMonitor));
        _translationModeService = translationModeService ?? throw new ArgumentNullException(nameof(translationModeService));
        _settingsMonitor = settingsMonitor ?? throw new ArgumentNullException(nameof(settingsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // ウィンドウ選択イベントを購読
        _windowSelectionSubscription = _windowManagementService?.WindowSelectionChanged
            .Subscribe(OnWindowSelectionChanged);

        _logger.LogInformation("🎓 [Phase 10] BackgroundLearningService初期化完了");
    }

    private SpeculativeOcrSettings Settings => _settingsMonitor.CurrentValue;

    /// <summary>
    /// ウィンドウ選択変更イベントハンドラ
    /// </summary>
    private void OnWindowSelectionChanged(WindowSelectionChanged e)
    {
        var newHandle = e.CurrentWindow?.Handle ?? IntPtr.Zero;
        SetSelectedWindow(newHandle);
    }

    /// <summary>
    /// ウィンドウ選択状態を更新
    /// </summary>
    public void SetSelectedWindow(IntPtr windowHandle)
    {
        var wasSelected = _isWindowSelected;
        _selectedWindowHandle = windowHandle;
        _isWindowSelected = windowHandle != IntPtr.Zero;

        if (_isWindowSelected && !wasSelected)
        {
            _logger.LogInformation("🎓 [Phase 10] ウィンドウ選択検出 - バックグラウンド学習開始準備");
            _learningScheduler.ResetForNewProfile();
        }
        else if (!_isWindowSelected && wasSelected)
        {
            _logger.LogInformation("🎓 [Phase 10] ウィンドウ選択解除 - バックグラウンド学習一時停止");
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🎓 [Phase 10] BackgroundLearningService開始");

        // 初期待機（アプリケーション起動完了を待つ）
        await Task.Delay(Settings.BackgroundLearningStartupDelay, stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 実行間隔を取得
                var interval = _learningScheduler.GetNextExecutionInterval();

                // 実行判定
                if (ShouldExecuteLearningCycle())
                {
                    await ExecuteLearningCycleAsync(stoppingToken).ConfigureAwait(false);
                    _consecutiveSkipCount = 0;
                    _consecutiveErrorCount = 0; // 成功時にエラーカウントをリセット
                }
                else
                {
                    // 実行が連続してスキップされた場合にログを残すため、回数を記録する
                    // これにより、長期間学習が進まない状況を検出可能
                    _consecutiveSkipCount++;
                    if (_consecutiveSkipCount >= MaxConsecutiveSkips && Settings.EnableDetailedLogging)
                    {
                        _logger.LogDebug("🎓 [Phase 10] 連続スキップ: {Count}回", _consecutiveSkipCount);
                        _consecutiveSkipCount = 0;
                    }
                }

                // 次の実行まで待機
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _consecutiveErrorCount++;

                // [Issue #293] 指数バックオフ: 連続エラー時に待機時間を増加
                // 1回目: 10秒, 2回目: 20秒, 3回目: 40秒, 4回目: 60秒（上限）, 5回目: 60秒（上限）
                var backoffMultiplier = Math.Min((int)Math.Pow(2, _consecutiveErrorCount - 1), 6);
                var retryDelay = TimeSpan.FromTicks(
                    Math.Min(
                        Settings.BackgroundLearningErrorRetryDelay.Ticks * backoffMultiplier,
                        MaxErrorRetryDelay.Ticks));

                _logger.LogWarning(ex,
                    "🎓 [Phase 10] 学習サイクルエラー (連続{Count}回目, 次回リトライ: {Delay}秒後)",
                    _consecutiveErrorCount, retryDelay.TotalSeconds);

                // 連続エラーが多い場合は警告レベルを上げる
                if (_consecutiveErrorCount >= MaxConsecutiveErrors)
                {
                    _logger.LogError(
                        "🎓 [Phase 10] 連続エラー上限到達 ({Count}回) - サービス継続中だが注意が必要",
                        _consecutiveErrorCount);
                }

                await Task.Delay(retryDelay, stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("🎓 [Phase 10] BackgroundLearningService終了");
    }

    /// <summary>
    /// 学習サイクルを実行すべきか判定
    /// </summary>
    private bool ShouldExecuteLearningCycle()
    {
        // バックグラウンド学習が無効
        if (!Settings.EnableBackgroundLearning)
        {
            return false;
        }

        // ウィンドウが選択されていない
        if (!_isWindowSelected || _selectedWindowHandle == IntPtr.Zero)
        {
            return false;
        }

        // Live翻訳中は実行しない
        if (_translationModeService.CurrentMode == TranslationMode.Live)
        {
            return false;
        }

        // スケジューラが実行を許可していない
        if (!_learningScheduler.ShouldExecuteNow())
        {
            return false;
        }

        // 必要なサービスがない
        if (_speculativeOcrService == null || _captureService == null)
        {
            return false;
        }

        // 投機的OCRが実行中
        if (_speculativeOcrService.IsExecuting)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 学習サイクルを実行
    /// </summary>
    private async Task ExecuteLearningCycleAsync(CancellationToken cancellationToken)
    {
        // 1. GPU使用率チェック
        if (!await CheckResourceAvailabilityAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Settings.EnableDetailedLogging)
                _logger.LogDebug("🎓 [Phase 10] リソース不足でスキップ");
            return;
        }

        // 2. ウィンドウがアクティブかチェック（Geminiフィードバック: 非アクティブ時は一時停止）
        if (!IsWindowActive())
        {
            if (Settings.EnableDetailedLogging)
                _logger.LogDebug("🎓 [Phase 10] ウィンドウ非アクティブでスキップ");
            return;
        }

        try
        {
            _logger.LogDebug("🎓 [Phase 10] 学習サイクル開始");

            // 3. キャプチャ実行
            var capturedImage = await _captureService!.CaptureWindowAsync(
                _selectedWindowHandle).ConfigureAwait(false);

            if (capturedImage == null)
            {
                _logger.LogDebug("🎓 [Phase 10] キャプチャ失敗 - スキップ");
                return;
            }

            _lastCaptureTime = DateTime.UtcNow;

            // 4. 投機的OCR実行
            var executed = await _speculativeOcrService!.TryExecuteSpeculativeOcrAsync(
                capturedImage,
                imageHash: null,
                cancellationToken).ConfigureAwait(false);

            if (!executed)
            {
                _logger.LogDebug("🎓 [Phase 10] 投機的OCR実行スキップ");
                return;
            }

            // 5. OCR結果をROI学習に送信
            await ReportOcrResultsToRoiManagerAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("🎓 [Phase 10] 学習サイクル完了");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "🎓 [Phase 10] 学習サイクル実行エラー");
        }
    }

    /// <summary>
    /// リソース可用性チェック
    /// </summary>
    private async Task<bool> CheckResourceAvailabilityAsync(CancellationToken cancellationToken)
    {
        try
        {
            var metrics = await _resourceMonitor.GetCurrentMetricsAsync(cancellationToken).ConfigureAwait(false);
            var gpuUsage = metrics.GpuUsagePercent ?? 0;

            // バックグラウンド学習用の閾値（通常より高め）
            return gpuUsage < Settings.GpuUsageThresholdForSkip;
        }
        catch
        {
            // エラー時は安全のためスキップ
            return false;
        }
    }

    /// <summary>
    /// ウィンドウがアクティブかチェック
    /// </summary>
    /// <remarks>
    /// 最小化されたウィンドウではOCRが無効なため、リソース浪費を防ぐ
    /// </remarks>
    private bool IsWindowActive()
    {
        if (_selectedWindowHandle == IntPtr.Zero)
        {
            return false;
        }

        // 最小化状態をチェック（IWindowManagerが利用可能な場合）
        if (_windowManager != null && _windowManager.IsMinimized(_selectedWindowHandle))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// OCR結果をRoiManagerに送信
    /// </summary>
    private Task ReportOcrResultsToRoiManagerAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken; // 将来の拡張用に保持
        if (_roiManager == null || !_roiManager.IsEnabled)
        {
            // ROI学習なしでもスケジューラに通知
            _learningScheduler.OnOcrCompleted(0);
            return Task.CompletedTask;
        }

        try
        {
            // 投機的OCRの結果からテキスト領域を取得
            var cachedResult = _speculativeOcrService?.CachedResult;
            if (cachedResult?.OcrResults == null)
            {
                _learningScheduler.OnOcrCompleted(0);
                return Task.CompletedTask;
            }

            var ocrResults = cachedResult.OcrResults;
            var detections = new List<(NormalizedRect bounds, float confidence)>();
            var imageSize = cachedResult.ImageSize;

            foreach (var result in ocrResults.TextRegions)
            {
                // 信頼度フィルタ（Geminiフィードバック: 高信頼度のみ学習）
                if (result.Confidence < Settings.MinOcrConfidenceForLearning)
                {
                    continue;
                }

                // [Issue #293] テキスト長フィルタ - 空文字列やノイズを除外
                var textLength = result.Text?.Length ?? 0;
                if (textLength < Settings.MinTextLengthForLearning)
                {
                    if (Settings.EnableDetailedLogging)
                    {
                        _logger.LogTrace(
                            "🎓 [Phase 10] テキスト長フィルタで除外: Text='{Text}' Length={Length} < {Min}",
                            result.Text ?? "", textLength, Settings.MinTextLengthForLearning);
                    }
                    continue;
                }

                // [Issue #293] 領域サイズフィルタ - 極小のノイズ領域を除外
                if (result.Bounds.Width < Settings.MinRegionWidthForLearning ||
                    result.Bounds.Height < Settings.MinRegionHeightForLearning)
                {
                    if (Settings.EnableDetailedLogging)
                    {
                        _logger.LogTrace(
                            "🎓 [Phase 10] 領域サイズフィルタで除外: Bounds={Bounds} (Min: {MinW}x{MinH})",
                            result.Bounds, Settings.MinRegionWidthForLearning, Settings.MinRegionHeightForLearning);
                    }
                    continue;
                }

                // 正規化座標に変換
                if (imageSize.Width > 0 && imageSize.Height > 0)
                {
                    var normalizedRect = new NormalizedRect
                    {
                        X = (float)result.Bounds.X / imageSize.Width,
                        Y = (float)result.Bounds.Y / imageSize.Height,
                        Width = (float)result.Bounds.Width / imageSize.Width,
                        Height = (float)result.Bounds.Height / imageSize.Height
                    };

                    detections.Add((normalizedRect, (float)result.Confidence));
                }
            }

            // ROI学習に送信
            if (detections.Count > 0)
            {
                _roiManager.ReportTextDetections(detections);

                if (Settings.EnableDetailedLogging)
                {
                    _logger.LogDebug(
                        "🎓 [Phase 10] ROI学習送信: {Count}個の高信頼度検出 (閾値={Threshold:P0})",
                        detections.Count, Settings.MinOcrConfidenceForLearning);
                }
            }

            // スケジューラに通知
            var highConfidenceCount = detections.Count(d => d.confidence >= 0.95f);
            _learningScheduler.OnOcrCompleted(detections.Count, highConfidenceCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "🎓 [Phase 10] ROI学習送信エラー");
            _learningScheduler.OnOcrCompleted(0);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _windowSelectionSubscription?.Dispose();
        base.Dispose();
    }
}
