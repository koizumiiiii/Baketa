using Baketa.Application.Services.Diagnostics;
using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;
using Baketa.Core.Abstractions.UI;
using Microsoft.Extensions.Logging;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Baketa.Application.Services.Translation;

/// <summary>
/// 翻訳制御統一サービス実装
/// MainOverlayViewModelから抽出された翻訳制御・状態管理ロジックを統一化
/// </summary>
public sealed class TranslationControlService : ITranslationControlService, IDisposable
{
    private readonly IInPlaceTranslationOverlayManager _overlayManager;
    private readonly IDiagnosticReportService _diagnosticReportService;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<TranslationControlService> _logger;
    
    private readonly Subject<TranslationStateChanged> _translationStateSubject = new();
    private readonly Subject<bool> _loadingStateSubject = new();
    private readonly Subject<TranslationUIState> _uiStateSubject = new();
    
    // 競合状態対策: 非同期排他制御用セマフォ
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    
    private TranslationStatus _currentStatus = TranslationStatus.Idle;
    private bool _isTranslationActive;
    private bool _isTranslationResultVisible;
    private bool _isLoading;
    private bool _disposed;

    public TranslationControlService(
        IInPlaceTranslationOverlayManager overlayManager,
        IDiagnosticReportService diagnosticReportService,
        IEventAggregator eventAggregator,
        ILogger<TranslationControlService> logger)
    {
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        _diagnosticReportService = diagnosticReportService ?? throw new ArgumentNullException(nameof(diagnosticReportService));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 初期UI状態を発行
        EmitUIState();
    }

    /// <inheritdoc />
    public bool IsTranslationActive => _isTranslationActive;

    /// <inheritdoc />
    public TranslationStatus CurrentStatus => _currentStatus;

    /// <inheritdoc />
    public bool IsTranslationResultVisible => _isTranslationResultVisible;

    /// <inheritdoc />
    public bool IsLoading => _isLoading;

    /// <inheritdoc />
    public IObservable<TranslationStateChanged> TranslationStateChanged => _translationStateSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<bool> LoadingStateChanged => _loadingStateSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<TranslationUIState> UIStateChanged => _uiStateSubject.AsObservable();

    /// <inheritdoc />
    public async Task<TranslationControlResult> ExecuteStartStopAsync(
        WindowInfo? windowInfo, 
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return new TranslationControlResult(false, "Service is disposed");

        var executionTimer = System.Diagnostics.Stopwatch.StartNew();
        
        // 競合状態対策: 状態チェックと更新をアトミックに実行
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("翻訳開始/停止処理開始 - IsTranslationActive={IsTranslationActive}", _isTranslationActive);

            // 診断レポート生成
            var operation = _isTranslationActive ? "Stop" : "Start";
            var trigger = $"execute_{operation.ToLower()}_button_pressed";
            var context = $"ExecuteStartStopAsync {operation} operation";
            
            _logger.LogDebug("診断レポート生成開始（統一サービス使用 - {Operation}操作時）", operation);
            await _diagnosticReportService.GenerateReportAsync(trigger, context, cancellationToken);

            TranslationControlResult result;
            
            if (_isTranslationActive)
            {
                _logger.LogDebug("翻詳停止処理呼び出し");
                result = await StopTranslationAsync(cancellationToken);
            }
            else
            {
                if (windowInfo == null)
                {
                    return new TranslationControlResult(false, "ウィンドウが選択されていません");
                }
                
                _logger.LogDebug("翻訳開始処理呼び出し");
                result = await StartTranslationAsync(windowInfo, cancellationToken);
            }

            executionTimer.Stop();
            return result with { ExecutionTime = executionTimer.Elapsed };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳開始/停止処理中にエラーが発生");
            
            // エラー時は安全な状態に戻す
            await UpdateTranslationStateAsync(TranslationStatus.Idle, false, false, false, "ExecuteStartStopAsync_Error");
            
            executionTimer.Stop();
            return new TranslationControlResult(false, ex.Message, null, executionTimer.Elapsed);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<TranslationControlResult> StartTranslationAsync(
        WindowInfo windowInfo,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return new TranslationControlResult(false, "Service is disposed");
        if (windowInfo == null) throw new ArgumentNullException(nameof(windowInfo));

        var executionTimer = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("翻訳ワークフローを開始");

            // 診断レポート生成
            _logger.LogDebug("診断レポート生成開始（統一サービス使用 - Start押下時）");
            await _diagnosticReportService.GenerateReportAsync("start_button_pressed", "StartTranslationAsync operation", cancellationToken);

            // ローディング状態開始
            await UpdateTranslationStateAsync(TranslationStatus.Initializing, false, false, true, "StartTranslationAsync_Begin");

            _logger.LogDebug("選択済みウィンドウを使用: '{Title}' (Handle={Handle})", windowInfo.Title, windowInfo.Handle);

            // ローディングオーバーレイ表示
            _logger.LogDebug("LoadingOverlayManager.ShowAsync呼び出し開始");
            try
            {
                // TODO: LoadingManagerの統合が必要 - 現在は一時的にコメントアウト
                // await _loadingManager.ShowAsync().ConfigureAwait(false);
                _logger.LogDebug("LoadingOverlayManager.ShowAsync呼び出し完了");
            }
            catch (Exception loadingEx)
            {
                _logger.LogError(loadingEx, "ローディングオーバーレイ表示に失敗");
            }

            // 翻訳開始処理（実際の翻訳サービスとの統合が必要）
            // TODO: 翻訳サービスとの統合実装
            
            // 翻訳開始状態に更新
            await UpdateTranslationStateAsync(TranslationStatus.Ready, true, false, false, "StartTranslationAsync_Complete");

            _logger.LogInformation("翻訳ワークフロー開始完了");
            
            executionTimer.Stop();
            return new TranslationControlResult(true, null, TranslationStatus.Ready, executionTimer.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳開始処理中にエラーが発生");
            
            // エラー時は安全な状態に戻す
            await UpdateTranslationStateAsync(TranslationStatus.Idle, false, false, false, "StartTranslationAsync_Error");
            
            executionTimer.Stop();
            return new TranslationControlResult(false, ex.Message, null, executionTimer.Elapsed);
        }
    }

    /// <inheritdoc />
    public async Task<TranslationControlResult> StopTranslationAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return new TranslationControlResult(false, "Service is disposed");

        var executionTimer = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("翻訳停止処理開始");

            // オーバーレイを非表示にしてリセット
            await _overlayManager.HideAllInPlaceOverlaysAsync();
            await _overlayManager.ResetAsync();

            // 翻訳停止状態に更新
            await UpdateTranslationStateAsync(TranslationStatus.Idle, false, false, false, "StopTranslationAsync");

            _logger.LogInformation("翻訳停止処理完了");
            
            executionTimer.Stop();
            return new TranslationControlResult(true, null, TranslationStatus.Idle, executionTimer.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳停止処理中にエラーが発生");
            
            executionTimer.Stop();
            return new TranslationControlResult(false, ex.Message, null, executionTimer.Elapsed);
        }
    }

    /// <inheritdoc />
    public async Task ToggleTranslationVisibilityAsync()
    {
        if (_disposed) return;

        try
        {
            var newVisibility = !_isTranslationResultVisible;
            
            // 可視性を切り替え（高速化版 - 削除/再作成ではなく可視性プロパティのみ変更）
            await _overlayManager.SetAllOverlaysVisibilityAsync(newVisibility);

            await UpdateTranslationStateAsync(_currentStatus, _isTranslationActive, newVisibility, _isLoading, "ToggleTranslationVisibility");
            
            _logger.LogDebug("翻訳表示切り替え完了: {IsVisible}", newVisibility);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳表示切り替え中にエラーが発生");
        }
    }

    /// <summary>
    /// 翻訳状態を更新し、関連する通知を発行します
    /// </summary>
    private async Task UpdateTranslationStateAsync(
        TranslationStatus status, 
        bool isActive, 
        bool isResultVisible, 
        bool isLoading, 
        string source)
    {
        var previousStatus = _currentStatus;
        var statusChanged = previousStatus != status;
        var activeChanged = _isTranslationActive != isActive;
        var visibilityChanged = _isTranslationResultVisible != isResultVisible;
        var loadingChanged = _isLoading != isLoading;

        // 状態を更新
        _currentStatus = status;
        _isTranslationActive = isActive;
        _isTranslationResultVisible = isResultVisible;
        _isLoading = isLoading;

        // 変更通知の発行
        if (statusChanged || activeChanged || visibilityChanged)
        {
            var stateChangeEvent = new TranslationStateChanged(
                previousStatus,
                status,
                isActive,
                isResultVisible,
                DateTime.UtcNow,
                source
            );
            
            _translationStateSubject.OnNext(stateChangeEvent);
        }

        if (loadingChanged)
        {
            _loadingStateSubject.OnNext(isLoading);
        }

        // UI状態の更新
        EmitUIState();

        await Task.CompletedTask;
    }

    /// <summary>
    /// UI制御状態を生成・発行します
    /// </summary>
    private void EmitUIState()
    {
        var uiState = new TranslationUIState(
            ShowHideEnabled: _isTranslationActive,
            SettingsEnabled: !_isLoading && !_isTranslationActive,
            IsSelectWindowEnabled: !_isLoading, // OCR初期化状態は別途管理
            StartStopText: _isTranslationActive ? "Stop" : "Start",
            ShowHideText: _isTranslationResultVisible ? "Hide" : "Show",
            StatusText: GetStatusText(_currentStatus)
        );

        _uiStateSubject.OnNext(uiState);
    }

    /// <summary>
    /// 状態に応じたテキストを取得します
    /// </summary>
    private static string GetStatusText(TranslationStatus status)
    {
        return status switch
        {
            TranslationStatus.Initializing => "初期化中...",
            TranslationStatus.Idle => "未選択",
            TranslationStatus.Ready => "準備完了",
            TranslationStatus.Capturing or TranslationStatus.ProcessingOCR or TranslationStatus.Translating => "翻訳中",
            TranslationStatus.Error => "エラー",
            // TranslationStatus.Stopped => "停止", // この値は存在しない
            _ => status.ToString()
        };
    }

    /// <summary>
    /// リソースを解放します
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposeパターン実装（堅牢性向上）
    /// </summary>
    private void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            // マネージドリソースの解放
            _translationStateSubject?.OnCompleted();
            _translationStateSubject?.Dispose();
            _loadingStateSubject?.OnCompleted();
            _loadingStateSubject?.Dispose();
            _uiStateSubject?.OnCompleted();
            _uiStateSubject?.Dispose();
            _stateLock?.Dispose();
        }

        _disposed = true;
    }
}