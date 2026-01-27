using Baketa.Application.Services.UI;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Roi;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Events;
using Baketa.Core.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Application.Services.Learning;

/// <summary>
/// [Issue #324] ROI監視ホステッドサービス
/// 学習済みROI領域を監視し、テキスト送りを検知して即時キャプチャをトリガー
/// </summary>
/// <remarks>
/// - 学習完了後に自動的に監視を開始
/// - 1秒間隔でROI領域のハッシュ比較
/// - 変化検知時にTextAdvanceDetectedEventを発行
/// </remarks>
public sealed class RoiMonitoringHostedService : BackgroundService
{
    private readonly IRoiChangeMonitorService? _roiChangeMonitorService;
    private readonly IRoiManager? _roiManager;
    private readonly ICaptureService? _captureService;
    private readonly IWindowManagementService? _windowManagementService;
    private readonly ITranslationModeService _translationModeService;
    private readonly IEventAggregator? _eventAggregator;
    private readonly IOptionsMonitor<RoiManagerSettings> _settingsMonitor;
    private readonly ILogger<RoiMonitoringHostedService> _logger;

    // 監視状態
    private IntPtr _selectedWindowHandle;
    private bool _isMonitoringActive;
    private DateTime _lastCheckTime = DateTime.MinValue;
    private int _consecutiveChangeCount;

    // 設定
    private const int DefaultPollingIntervalMs = 2000; // 2秒間隔（ネイティブDLL負荷軽減）
    private const int MinChangeCountForTrigger = 2; // 2回連続変化で翻訳トリガー
    private const int MaxConsecutiveChanges = 5; // 5回連続変化で一時停止（アニメーション除外）

    public RoiMonitoringHostedService(
        IRoiChangeMonitorService? roiChangeMonitorService,
        IRoiManager? roiManager,
        ICaptureService? captureService,
        IWindowManagementService? windowManagementService,
        ITranslationModeService translationModeService,
        IEventAggregator? eventAggregator,
        IOptionsMonitor<RoiManagerSettings> settingsMonitor,
        ILogger<RoiMonitoringHostedService> logger)
    {
        _roiChangeMonitorService = roiChangeMonitorService;
        _roiManager = roiManager;
        _captureService = captureService;
        _windowManagementService = windowManagementService;
        _translationModeService = translationModeService ?? throw new ArgumentNullException(nameof(translationModeService));
        _eventAggregator = eventAggregator;
        _settingsMonitor = settingsMonitor ?? throw new ArgumentNullException(nameof(settingsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // ウィンドウ選択イベントを購読
        _windowManagementService?.WindowSelectionChanged.Subscribe(OnWindowSelectionChanged);

        _logger.LogInformation("[Issue #324] RoiMonitoringHostedService初期化完了");
    }

    private RoiManagerSettings Settings => _settingsMonitor.CurrentValue;

    /// <summary>
    /// ウィンドウ選択変更ハンドラ
    /// </summary>
    private void OnWindowSelectionChanged(WindowSelectionChanged e)
    {
        _selectedWindowHandle = e.CurrentWindow?.Handle ?? IntPtr.Zero;

        if (_selectedWindowHandle != IntPtr.Zero)
        {
            _logger.LogDebug("[Issue #324] ウィンドウ選択: Handle={Handle}", _selectedWindowHandle);
        }
        else
        {
            _logger.LogDebug("[Issue #324] ウィンドウ選択解除");
            _isMonitoringActive = false;
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Issue #324] RoiMonitoringHostedService開始");

        // 起動待機（アプリケーション初期化完了を待つ）
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 監視実行判定
                if (ShouldExecuteMonitoring())
                {
                    await ExecuteMonitoringCycleAsync(stoppingToken).ConfigureAwait(false);
                }

                // 次のサイクルまで待機
                await Task.Delay(DefaultPollingIntervalMs, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Issue #324] 監視サイクルエラー");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("[Issue #324] RoiMonitoringHostedService終了");
    }

    /// <summary>
    /// 監視を実行すべきか判定
    /// </summary>
    private bool ShouldExecuteMonitoring()
    {
        // 機能が無効
        if (!Settings.Enabled)
        {
            return false;
        }

        // 必要なサービスがない
        if (_roiChangeMonitorService == null || _roiManager == null || _captureService == null)
        {
            return false;
        }

        // ウィンドウが選択されていない
        if (_selectedWindowHandle == IntPtr.Zero)
        {
            return false;
        }

        // 学習が完了していない
        if (!_roiManager.IsLearningComplete)
        {
            return false;
        }

        // Live翻訳中は監視しない
        if (_translationModeService.CurrentMode == TranslationMode.Live)
        {
            if (_isMonitoringActive)
            {
                _logger.LogDebug("[Issue #324] Live翻訳開始 - 監視一時停止");
                _isMonitoringActive = false;
            }
            return false;
        }

        // 監視開始
        if (!_isMonitoringActive)
        {
            _logger.LogInformation("[Issue #324] ROI監視開始 - 学習完了、高信頼度領域: {Count}",
                _roiManager.GetHighConfidenceRegions().Count);
            _isMonitoringActive = true;
        }

        return true;
    }

    /// <summary>
    /// 監視サイクルを実行
    /// </summary>
    private async Task ExecuteMonitoringCycleAsync(CancellationToken cancellationToken)
    {
        try
        {
            // キャプチャ実行
            var capturedImage = await _captureService!.CaptureWindowAsync(
                _selectedWindowHandle).ConfigureAwait(false);

            if (capturedImage == null)
            {
                return;
            }

            _lastCheckTime = DateTime.UtcNow;

            // 変化チェック
            var changedRegions = await _roiChangeMonitorService!.CheckForChangesAsync(
                capturedImage, cancellationToken).ConfigureAwait(false);

            if (changedRegions.Count > 0)
            {
                _consecutiveChangeCount++;

                _logger.LogDebug(
                    "[Issue #324] 変化検出: {Count}領域, 連続={Consecutive}回",
                    changedRegions.Count, _consecutiveChangeCount);

                // 連続変化が多すぎる場合はアニメーション等と判断してスキップ
                if (_consecutiveChangeCount > MaxConsecutiveChanges)
                {
                    _logger.LogDebug("[Issue #324] 連続変化が多すぎるためスキップ（アニメーション等）");
                    return;
                }

                // 閾値以上の連続変化でテキスト送りと判定
                if (_consecutiveChangeCount >= MinChangeCountForTrigger)
                {
                    await OnTextAdvanceDetectedAsync(changedRegions.Count, cancellationToken)
                        .ConfigureAwait(false);

                    // ベースライン更新（次の変化検知のため）
                    await _roiChangeMonitorService.UpdateBaselineAsync(capturedImage, cancellationToken)
                        .ConfigureAwait(false);

                    _consecutiveChangeCount = 0;
                }
            }
            else
            {
                // 変化なし - カウンタリセット
                if (_consecutiveChangeCount > 0)
                {
                    _logger.LogDebug("[Issue #324] 変化安定 - カウンタリセット");
                }
                _consecutiveChangeCount = 0;

                // 変化がない場合もベースラインを更新（初回または安定状態）
                await _roiChangeMonitorService.UpdateBaselineAsync(capturedImage, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #324] 監視サイクル実行エラー");
        }
    }

    /// <summary>
    /// テキスト送り検知時の処理
    /// </summary>
    private async Task OnTextAdvanceDetectedAsync(int changedRegionCount, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[Issue #324] テキスト送り検知！ 変化領域: {Count}, 即時キャプチャをトリガー",
            changedRegionCount);

        // イベント発行（翻訳オーケストレーションがハンドル）
        if (_eventAggregator != null)
        {
            try
            {
                var textAdvanceEvent = new TextAdvanceDetectedEvent
                {
                    DetectedAt = DateTime.UtcNow,
                    ChangedRegionCount = changedRegionCount,
                    WindowHandle = _selectedWindowHandle
                };

                await _eventAggregator.PublishAsync(textAdvanceEvent, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogDebug("[Issue #324] TextAdvanceDetectedEvent発行完了");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Issue #324] TextAdvanceDetectedEvent発行エラー");
            }
        }
    }
}
