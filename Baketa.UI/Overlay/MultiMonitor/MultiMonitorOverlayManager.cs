using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Baketa.Core.UI.Fullscreen;
using Baketa.Core.UI.Geometry;
using Baketa.Core.UI.Monitors;
using Baketa.Core.UI.Overlay;
using Baketa.UI.Monitors;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using System.Reactive.Subjects;

namespace Baketa.UI.Overlay.MultiMonitor;

/// <summary>
/// マルチモニター対応オーバーレイマネージャー
/// 自動クリーンアップ、インテリジェントフォールバック、高性能なオーバーレイ管理を提供
/// </summary>
public sealed class MultiMonitorOverlayManager : ReactiveObject, IAsyncDisposable
{
    private readonly AvaloniaMultiMonitorAdapter _monitorAdapter;
    private readonly IFullscreenModeService _fullscreenService;
    private readonly Baketa.UI.Overlay.AvaloniaOverlayWindowAdapter _overlayAdapter;
    private readonly ILogger<MultiMonitorOverlayManager> _logger;
    private readonly object _lockObject = new();
    
    // オーバーレイとモニターの関連管理（強化版）
    private readonly ConcurrentDictionary<nint, OverlayMonitorState> _overlayStates = new();
    private readonly Subject<OverlayMonitorChangedEventArgs> _overlayMonitorChangedSubject = new();
    
    // 自動クリーンアップとフォールバック機構
    private readonly Timer _cleanupTimer;
    private readonly Timer _healthCheckTimer;
    private readonly ConcurrentDictionary<nint, DateTime> _overlayLastSeen = new();
    private readonly ConcurrentDictionary<nint, int> _overlayErrorCounts = new();
    
    // エラー回復とリトライ機構
    private readonly ConcurrentDictionary<nint, OverlayRecoveryInfo> _recoveryInfo = new();
    
    /// <summary>
    /// ターゲットゲームウィンドウハンドル
    /// </summary>
    public nint TargetGameWindowHandle { get; private set; }
    
    /// <summary>
    /// 現在のアクティブモニター
    /// </summary>
    public MonitorInfo? CurrentActiveMonitor { get; private set; }
    
    /// <summary>
    /// オーバーレイ管理統計
    /// </summary>
    public OverlayManagerStatistics Statistics { get; } = new();
    
    private volatile bool _disposed;
    
    /// <summary>
    /// MultiMonitorOverlayManagerを初期化
    /// </summary>
    /// <param name="monitorAdapter">マルチモニターアダプター</param>
    /// <param name="fullscreenService">フルスクリーンモードサービス</param>
    /// <param name="overlayAdapter">オーバーレイアダプター</param>
    /// <param name="logger">ロガー</param>
    public MultiMonitorOverlayManager(
        AvaloniaMultiMonitorAdapter monitorAdapter,
        IFullscreenModeService fullscreenService,
        Baketa.UI.Overlay.AvaloniaOverlayWindowAdapter overlayAdapter,
        ILogger<MultiMonitorOverlayManager> logger)
    {
        _monitorAdapter = monitorAdapter;
        _fullscreenService = fullscreenService;
        _overlayAdapter = overlayAdapter;
        _logger = logger;
        
        // 自動クリーンアップタイマー（30秒間隔）
        _cleanupTimer = new Timer(AutoCleanupInvalidOverlays, null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        
        // ヘルスチェックタイマー（10秒間隔）
        _healthCheckTimer = new Timer(PerformHealthCheck, null,
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        
        // イベント購読
        InitializeEventSubscriptions();
        
        _logger.LogInformation("MultiMonitorOverlayManager initialized with auto-cleanup and recovery");
    }
    
    /// <summary>
    /// アクティブなオーバーレイ数
    /// </summary>
    public int ActiveOverlayCount => _overlayStates.Count;
    
    /// <summary>
    /// 有効なオーバーレイ数（ヘルスチェック済み）
    /// </summary>
    public int ValidOverlayCount => _overlayStates.Count(kvp => IsOverlayHealthy(kvp.Key));
    
    /// <summary>
    /// オーバーレイ・モニター変更のObservable
    /// </summary>
    public IObservable<OverlayMonitorChangedEventArgs> OverlayMonitorChanged => 
        _overlayMonitorChangedSubject.AsObservable();
    
    /// <summary>
    /// ゲームウィンドウを設定してマルチモニター管理を開始
    /// </summary>
    /// <param name="gameWindowHandle">ゲームウィンドウハンドル</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>管理開始タスク</returns>
    public async Task StartManagingAsync(nint gameWindowHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting multi-monitor overlay management for game window 0x{Handle:X}", gameWindowHandle);
            
            lock (_lockObject)
            {
                TargetGameWindowHandle = gameWindowHandle;
            }
            
            // 初期のアクティブモニターを決定
            await DetermineInitialActiveMonitorAsync(cancellationToken).ConfigureAwait(false);
            
            // フルスクリーンモード監視開始
            await _fullscreenService.StartMonitoringAsync(gameWindowHandle, cancellationToken).ConfigureAwait(false);
            
            // 統計更新
            Statistics.ManagementStartTime = DateTime.UtcNow;
            Statistics.IncrementOperationCount("StartManaging");
            
            _logger.LogInformation("Multi-monitor overlay management started successfully");
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "Multi-monitor overlay management start cancelled");
            Statistics.IncrementErrorCount("StartManaging");
            throw;
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid argument starting multi-monitor overlay management");
            Statistics.IncrementErrorCount("StartManaging");
            throw;
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogError(ex, "Service disposed during multi-monitor overlay management start");
            Statistics.IncrementErrorCount("StartManaging");
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation starting multi-monitor overlay management");
            Statistics.IncrementErrorCount("StartManaging");
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ArgumentException and not ObjectDisposedException and not InvalidOperationException)
        {
            // 予期しない例外のみをキャッチし、既に処理済みの例外は除外
            _logger.LogError(ex, "Unexpected error starting multi-monitor overlay management");
            Statistics.IncrementErrorCount("StartManaging");
            throw;
        }
    }
    
    /// <summary>
    /// オーバーレイウィンドウを作成（マルチモニター対応、エラー回復機能付き）
    /// </summary>
    /// <param name="initialSize">初期サイズ</param>
    /// <param name="relativePosition">ゲームウィンドウからの相対位置</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>作成されたオーバーレイウィンドウ</returns>
    public async Task<IOverlayWindow> CreateOverlayAsync(
        CoreSize initialSize,
        CorePoint relativePosition,
        CancellationToken cancellationToken = default)
    {
        const int maxRetries = 3;
        Exception? lastException = null;
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger.LogDebug("Creating overlay (attempt {Attempt}/{MaxRetries})", attempt, maxRetries);
                
                // アクティブモニターを取得
                var activeMonitor = await EnsureActiveMonitorAsync(cancellationToken).ConfigureAwait(false);
                
                // DPIスケールを考慮した絶対位置を計算
                var absolutePosition = CalculateAbsolutePosition(relativePosition, activeMonitor);
                
                // オーバーレイウィンドウを作成
                var overlay = await _overlayAdapter.CreateOverlayWindowAsync(
                    TargetGameWindowHandle, initialSize, absolutePosition).ConfigureAwait(false);
                
                // オーバーレイ状態を登録
                var overlayState = new OverlayMonitorState(
                    overlay.Handle,
                    activeMonitor,
                    relativePosition,
                    initialSize,
                    DateTime.UtcNow);
                
                if (_overlayStates.TryAdd(overlay.Handle, overlayState))
                {
                    // ヘルス情報を初期化
                    _overlayLastSeen[overlay.Handle] = DateTime.UtcNow;
                    _overlayErrorCounts[overlay.Handle] = 0;
                    
                    // 回復情報を初期化
                    _recoveryInfo[overlay.Handle] = new OverlayRecoveryInfo(
                        initialSize, relativePosition, activeMonitor);
                    
                    // 統計更新
                    Statistics.IncrementOperationCount("CreateOverlay");
                    Statistics.TotalOverlaysCreated++;
                    
                    _logger.LogInformation("Multi-monitor overlay created successfully: Handle=0x{Handle:X}, Monitor={Monitor}",
                        overlay.Handle, activeMonitor.Name);
                    
                    return overlay;
                }
                else
                {
                    _logger.LogWarning("Failed to register overlay state for handle 0x{Handle:X}", overlay.Handle);
                    // オーバーレイを破棄
                    overlay.Close();
                }
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogDebug(ex, "Multi-monitor overlay creation cancelled");
                Statistics.IncrementErrorCount("CreateOverlay");
                throw;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "Invalid argument creating multi-monitor overlay (attempt {Attempt})", attempt);
                lastException = ex;
                
                // 最後のリトライの場合は例外を再スロー
                if (attempt >= maxRetries)
                {
                    Statistics.IncrementErrorCount("CreateOverlay");
                    throw;
                }
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogError(ex, "Service disposed during multi-monitor overlay creation (attempt {Attempt})", attempt);
                lastException = ex;
                
                // 最後のリトライの場合は例外を再スロー
                if (attempt >= maxRetries)
                {
                    Statistics.IncrementErrorCount("CreateOverlay");
                    throw;
                }
                
                // 短い遅延後にリトライ
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Invalid operation creating multi-monitor overlay (attempt {Attempt})", attempt);
                lastException = ex;
                
                // 最後のリトライの場合は例外を再スロー
                if (attempt >= maxRetries)
                {
                    Statistics.IncrementErrorCount("CreateOverlay");
                    throw;
                }
                
                // 短い遅延後にリトライ
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex, "Timeout creating multi-monitor overlay (attempt {Attempt})", attempt);
                lastException = ex;
                
                // 最後のリトライの場合は例外を再スロー
                if (attempt >= maxRetries)
                {
                    Statistics.IncrementErrorCount("CreateOverlay");
                    throw;
                }
                
                // 短い遅延後にリトライ
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (NotSupportedException ex)
            {
                _logger.LogError(ex, "Operation not supported creating multi-monitor overlay (attempt {Attempt})", attempt);
                lastException = ex;
                
                if (attempt >= maxRetries)
                {
                    Statistics.IncrementErrorCount("CreateOverlay");
                    throw;
                }
                
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (ExternalException ex)
            {
                _logger.LogError(ex, "External error creating multi-monitor overlay (attempt {Attempt})", attempt);
                lastException = ex;
                
                if (attempt >= maxRetries)
                {
                    Statistics.IncrementErrorCount("CreateOverlay");
                    throw;
                }
                
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
        
        // 全てのリトライが失敗した場合
        Statistics.IncrementErrorCount("CreateOverlay");
        throw lastException ?? new InvalidOperationException("Failed to create overlay after multiple attempts");
    }
    
    /// <summary>
    /// オーバーレイウィンドウをモニター間で移動（インテリジェントエラー処理付き）
    /// </summary>
    /// <param name="overlayHandle">オーバーレイハンドル</param>
    /// <param name="targetMonitor">対象モニター</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>移動タスク</returns>
    public async Task MoveOverlayToMonitorAsync(
        nint overlayHandle,
        MonitorInfo targetMonitor,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_overlayStates.TryGetValue(overlayHandle, out var currentState))
            {
                _logger.LogWarning("Overlay state not found for handle 0x{Handle:X}", overlayHandle);
                Statistics.IncrementErrorCount("MoveOverlay");
                return;
            }
            
            var overlay = _overlayAdapter.GetOverlayWindow(overlayHandle);
            if (overlay is null)
            {
                _logger.LogWarning("Overlay window not found for handle 0x{Handle:X}", overlayHandle);
                await RemoveOverlayStateAsync(overlayHandle).ConfigureAwait(false);
                Statistics.IncrementErrorCount("MoveOverlay");
                return;
            }
            
            // ヘルス状態をチェック
            if (!IsOverlayHealthy(overlayHandle))
            {
                _logger.LogWarning("Overlay 0x{Handle:X} is not healthy, attempting recovery", overlayHandle);
                var recovered = await AttemptOverlayRecoveryAsync(overlayHandle, cancellationToken).ConfigureAwait(false);
                if (!recovered)
                {
                    Statistics.IncrementErrorCount("MoveOverlay");
                    return;
                }
            }
            
            _logger.LogDebug("Moving overlay from {Source} to {Target}",
                currentState.CurrentMonitor.Name, targetMonitor.Name);
            
            // DPI変更をチェック
            bool dpiChanged = _monitorAdapter.HasDpiChanged(currentState.CurrentMonitor, targetMonitor);
            
            // 新しい位置を計算（DPIスケーリング考慮）
            var newPosition = CalculatePositionForMonitor(
                currentState.RelativePosition, 
                targetMonitor);
            
            // DPI変更がある場合はサイズも調整
            var newSize = dpiChanged 
                ? CalculateSizeForMonitor(currentState.Size, currentState.CurrentMonitor, targetMonitor)
                : currentState.Size;
            
            // オーバーレイを移動・リサイズ
            await Task.Run(() =>
            {
                overlay.Position = newPosition;
                if (dpiChanged)
                {
                    overlay.Size = newSize;
                }
            }, cancellationToken).ConfigureAwait(false);
            
            // 状態を更新
            var newState = currentState with 
            { 
                CurrentMonitor = targetMonitor,
                Size = newSize,
                LastUpdated = DateTime.UtcNow
            };
            
            if (_overlayStates.TryUpdate(overlayHandle, newState, currentState))
            {
                // ヘルス情報更新
                _overlayLastSeen[overlayHandle] = DateTime.UtcNow;
                _overlayErrorCounts[overlayHandle] = 0; // エラーカウントリセット
                
                // 回復情報更新
                _recoveryInfo[overlayHandle] = new OverlayRecoveryInfo(
                    newSize, currentState.RelativePosition, targetMonitor);
                
                // イベント通知
                await NotifyOverlayMonitorChangedAsync(
                    OverlayMonitorChangeType.Moved,
                    overlayHandle,
                    currentState.CurrentMonitor,
                    targetMonitor,
                    dpiChanged,
                    cancellationToken).ConfigureAwait(false);
                
                // 統計更新
                Statistics.IncrementOperationCount("MoveOverlay");
                Statistics.TotalOverlayMoves++;
                
                _logger.LogInformation("Overlay moved successfully: Handle=0x{Handle:X}, {Source} → {Target}",
                    overlayHandle, currentState.CurrentMonitor.Name, targetMonitor.Name);
            }
            else
            {
                _logger.LogWarning("Failed to update overlay state for handle 0x{Handle:X}", overlayHandle);
                Statistics.IncrementErrorCount("MoveOverlay");
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "Overlay move cancelled");
            Statistics.IncrementErrorCount("MoveOverlay");
            throw;
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid argument moving overlay to monitor");
            await HandleOverlayErrorAsync(overlayHandle, ex).ConfigureAwait(false);
            Statistics.IncrementErrorCount("MoveOverlay");
            throw;
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogError(ex, "Service disposed during overlay move to monitor");
            await HandleOverlayErrorAsync(overlayHandle, ex).ConfigureAwait(false);
            Statistics.IncrementErrorCount("MoveOverlay");
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation moving overlay to monitor");
            await HandleOverlayErrorAsync(overlayHandle, ex).ConfigureAwait(false);
            Statistics.IncrementErrorCount("MoveOverlay");
            throw;
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Timeout moving overlay to monitor");
            await HandleOverlayErrorAsync(overlayHandle, ex).ConfigureAwait(false);
            Statistics.IncrementErrorCount("MoveOverlay");
            throw;
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported moving overlay to monitor");
            await HandleOverlayErrorAsync(overlayHandle, ex).ConfigureAwait(false);
            Statistics.IncrementErrorCount("MoveOverlay");
            throw;
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error moving overlay to monitor");
            await HandleOverlayErrorAsync(overlayHandle, ex).ConfigureAwait(false);
            Statistics.IncrementErrorCount("MoveOverlay");
            throw;
        }
    }
    
    /// <summary>
    /// オーバーレイのヘルス状態をチェック
    /// </summary>
    /// <param name="overlayHandle">オーバーレイハンドル</param>
    /// <returns>ヘルシーかどうか</returns>
    private bool IsOverlayHealthy(nint overlayHandle)
    {
        try
        {
            // ハンドル有効性チェック
            if (overlayHandle == nint.Zero)
                return false;
            
            // 最終確認時刻チェック
            if (_overlayLastSeen.TryGetValue(overlayHandle, out var lastSeen))
            {
                var timeSinceLastSeen = DateTime.UtcNow - lastSeen;
                if (timeSinceLastSeen > TimeSpan.FromMinutes(5)) // 5分以上未確認
                    return false;
            }
            
            // エラー回数チェック
            if (_overlayErrorCounts.TryGetValue(overlayHandle, out var errorCount))
            {
                if (errorCount > 3) // 3回以上エラー
                    return false;
            }
            
            // オーバーレイアダプターでの存在確認
            var overlay = _overlayAdapter.GetOverlayWindow(overlayHandle);
            return overlay is not null;
        }
        catch (ObjectDisposedException)
        {
            // サービスが破棄済みの場合は非ヘルシー
            return false;
        }
        catch (InvalidOperationException)
        {
            // 無効な操作の場合は非ヘルシー
            return false;
        }
        catch (ArgumentException)
        {
            // 引数エラーの場合は非ヘルシー
            return false;
        }
        catch (NotSupportedException)
        {
            // サポートされていない操作の場合は非ヘルシー
            return false;
        }
        catch (ExternalException)
        {
            // 外部エラーの場合は非ヘルシー
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // キャンセル例外以外の予期しない例外の場合は非ヘルシー
            // キャンセル例外は再スローし、その他はfalseを返す
            _logger.LogWarning(ex, "Unexpected error checking overlay health for handle 0x{Handle:X}", overlayHandle);
            return false;
        }
    }
    
    /// <summary>
    /// オーバーレイの自動回復を試行
    /// </summary>
    /// <param name="overlayHandle">オーバーレイハンドル</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>回復成功かどうか</returns>
    private async Task<bool> AttemptOverlayRecoveryAsync(nint overlayHandle, CancellationToken cancellationToken)
    {
        try
        {
            if (!_recoveryInfo.TryGetValue(overlayHandle, out var recoveryInfo))
            {
                _logger.LogWarning("No recovery info found for overlay 0x{Handle:X}", overlayHandle);
                return false;
            }
            
            _logger.LogInformation("Attempting overlay recovery for handle 0x{Handle:X}", overlayHandle);
            
            // 既存のオーバーレイを削除
            await RemoveOverlayStateAsync(overlayHandle).ConfigureAwait(false);
            
            // 新しいオーバーレイを作成
            var newOverlay = await CreateOverlayAsync(
                recoveryInfo.Size, 
                recoveryInfo.RelativePosition, 
                cancellationToken).ConfigureAwait(false);
            
            Statistics.TotalOverlayRecoveries++;
            _logger.LogInformation("Overlay recovery successful: Old=0x{OldHandle:X}, New=0x{NewHandle:X}",
                overlayHandle, newOverlay.Handle);
            
            return true;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "Overlay recovery cancelled for handle 0x{Handle:X}", overlayHandle);
            Statistics.IncrementErrorCount("RecoverOverlay");
            return false;
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogError(ex, "Service disposed during overlay recovery for handle 0x{Handle:X}", overlayHandle);
            Statistics.IncrementErrorCount("RecoverOverlay");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation during overlay recovery for handle 0x{Handle:X}", overlayHandle);
            Statistics.IncrementErrorCount("RecoverOverlay");
            return false;
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported recovering overlay 0x{Handle:X}", overlayHandle);
            Statistics.IncrementErrorCount("RecoverOverlay");
            return false;
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error recovering overlay 0x{Handle:X}", overlayHandle);
            Statistics.IncrementErrorCount("RecoverOverlay");
            return false;
        }
    }
    
    /// <summary>
    /// オーバーレイエラーを処理
    /// </summary>
    /// <param name="overlayHandle">オーバーレイハンドル</param>
    /// <param name="exception">発生した例外</param>
    /// <returns>処理タスク</returns>
    private async Task HandleOverlayErrorAsync(nint overlayHandle, Exception _)
    {
        try
        {
            // エラーカウントを増加
            _overlayErrorCounts.AddOrUpdate(overlayHandle, 1, (key, count) => count + 1);
            
            // エラーが多い場合は自動回復を試行
            if (_overlayErrorCounts.TryGetValue(overlayHandle, out var errorCount) && errorCount >= 3)
            {
                _logger.LogWarning("Overlay 0x{Handle:X} has {ErrorCount} errors, attempting recovery", 
                    overlayHandle, errorCount);
                
                await AttemptOverlayRecoveryAsync(overlayHandle, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Service disposed during overlay error handling for handle 0x{Handle:X}", overlayHandle);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation during overlay error handling for handle 0x{Handle:X}", overlayHandle);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported handling overlay error for handle 0x{Handle:X}", overlayHandle);
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error handling overlay error for handle 0x{Handle:X}", overlayHandle);
        }
    }
    
    /// <summary>
    /// 自動クリーンアップ（無効なオーバーレイを除去）
    /// </summary>
    /// <param name="state">タイマー状態</param>
    private async void AutoCleanupInvalidOverlays(object? state)
    {
        if (_disposed) return;
        
        try
        {
            var invalidHandles = new List<nint>();
            
            foreach (var kvp in _overlayStates)
            {
                if (!IsOverlayHealthy(kvp.Key))
                {
                    invalidHandles.Add(kvp.Key);
                }
            }
            
            foreach (var handle in invalidHandles)
            {
                await RemoveOverlayStateAsync(handle).ConfigureAwait(false);
                _logger.LogDebug("Auto-cleaned invalid overlay: 0x{Handle:X}", handle);
            }
            
            if (invalidHandles.Count > 0)
            {
                Statistics.IncrementOperationCount("AutoCleanup");
                Statistics.TotalAutoCleanups++;
                _logger.LogInformation("Auto-cleanup completed: removed {Count} invalid overlays", invalidHandles.Count);
            }
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Service disposed during overlay auto-cleanup");
            Statistics.IncrementErrorCount("AutoCleanup");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation during overlay auto-cleanup");
            Statistics.IncrementErrorCount("AutoCleanup");
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported during overlay auto-cleanup");
            Statistics.IncrementErrorCount("AutoCleanup");
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error during overlay auto-cleanup");
            Statistics.IncrementErrorCount("AutoCleanup");
        }
    }
    
    /// <summary>
    /// 定期ヘルスチェック
    /// </summary>
    /// <param name="state">タイマー状態</param>
    private async void PerformHealthCheck(object? state)
    {
        if (_disposed) return;
        
        try
        {
            var totalOverlays = _overlayStates.Count;
            var healthyOverlays = 0;
            var recoveredOverlays = 0;
            
            foreach (var kvp in _overlayStates.ToList())
            {
                if (IsOverlayHealthy(kvp.Key))
                {
                    healthyOverlays++;
                    _overlayLastSeen[kvp.Key] = DateTime.UtcNow;
                }
                else
                {
                    // 回復を試行
                    var recovered = await AttemptOverlayRecoveryAsync(kvp.Key, CancellationToken.None).ConfigureAwait(false);
                    if (recovered)
                    {
                        recoveredOverlays++;
                    }
                }
            }
            
            Statistics.LastHealthCheckTime = DateTime.UtcNow;
            _logger.LogDebug("Health check completed: {Healthy}/{Total} healthy, {Recovered} recovered",
                healthyOverlays, totalOverlays, recoveredOverlays);
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Service disposed during health check");
            Statistics.IncrementErrorCount("HealthCheck");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation during health check");
            Statistics.IncrementErrorCount("HealthCheck");
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported during health check");
            Statistics.IncrementErrorCount("HealthCheck");
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error during health check");
            Statistics.IncrementErrorCount("HealthCheck");
        }
    }
    
    /// <summary>
    /// オーバーレイ状態を削除
    /// </summary>
    /// <param name="overlayHandle">オーバーレイハンドル</param>
    /// <returns>削除タスク</returns>
    private async Task RemoveOverlayStateAsync(nint overlayHandle)
    {
        try
        {
            if (_overlayStates.TryRemove(overlayHandle, out var removedState))
            {
                // 関連情報もクリーンアップ
                _overlayLastSeen.TryRemove(overlayHandle, out _);
                _overlayErrorCounts.TryRemove(overlayHandle, out _);
                _recoveryInfo.TryRemove(overlayHandle, out _);
                
                // イベント通知
                await NotifyOverlayMonitorChangedAsync(
                    OverlayMonitorChangeType.Removed,
                    overlayHandle,
                    removedState.CurrentMonitor,
                    null,
                    false,
                    CancellationToken.None).ConfigureAwait(false);
                
                _logger.LogInformation("Overlay state removed: 0x{Handle:X}", overlayHandle);
            }
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Service disposed during overlay state removal for handle 0x{Handle:X}", overlayHandle);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation removing overlay state for handle 0x{Handle:X}", overlayHandle);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported removing overlay state for handle 0x{Handle:X}", overlayHandle);
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error removing overlay state for handle 0x{Handle:X}", overlayHandle);
        }
    }
    
    /// <summary>
    /// ゲームウィンドウのモニター変更を処理
    /// </summary>
    /// <param name="newActiveMonitor">新しいアクティブモニター</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>処理タスク</returns>
    public async Task HandleGameWindowMonitorChangeAsync(
        MonitorInfo newActiveMonitor,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var previousMonitor = CurrentActiveMonitor;
            if (previousMonitor?.Handle == newActiveMonitor.Handle)
            {
                _logger.LogDebug("Game window monitor unchanged: {Monitor}", newActiveMonitor.Name);
                return;
            }
            
            _logger.LogInformation("Game window monitor changed: {Previous} → {Current}",
                previousMonitor?.Name ?? "None", newActiveMonitor.Name);
            
            lock (_lockObject)
            {
                CurrentActiveMonitor = newActiveMonitor;
            }
            
            // 全オーバーレイを新しいモニターに移動
            var moveTasks = _overlayStates.Keys.Select(handle =>
                MoveOverlayToMonitorAsync(handle, newActiveMonitor, cancellationToken));
            
            await Task.WhenAll(moveTasks).ConfigureAwait(false);
            
            // プロパティ変更通知
            this.RaisePropertyChanged(nameof(CurrentActiveMonitor));
            
            Statistics.IncrementOperationCount("HandleMonitorChange");
            _logger.LogInformation("Game window monitor change handling completed");
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "Game window monitor change handling cancelled");
            Statistics.IncrementErrorCount("HandleMonitorChange");
            throw;
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid argument handling game window monitor change");
            Statistics.IncrementErrorCount("HandleMonitorChange");
            throw;
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogError(ex, "Service disposed during game window monitor change handling");
            Statistics.IncrementErrorCount("HandleMonitorChange");
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation handling game window monitor change");
            Statistics.IncrementErrorCount("HandleMonitorChange");
            throw;
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Timeout handling game window monitor change");
            Statistics.IncrementErrorCount("HandleMonitorChange");
            throw;
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported handling game window monitor change");
            Statistics.IncrementErrorCount("HandleMonitorChange");
            throw;
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error handling game window monitor change");
            Statistics.IncrementErrorCount("HandleMonitorChange");
            throw;
        }
    }
    
    /// <summary>
    /// フルスクリーンモード変更を処理
    /// </summary>
    /// <param name="fullscreenMode">フルスクリーンモード情報</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>処理タスク</returns>
    public async Task HandleFullscreenModeChangeAsync(
        FullscreenModeChangedEventArgs fullscreenMode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Handling fullscreen mode change: {Mode}", fullscreenMode.ToString());
            
            if (!fullscreenMode.CanShowOverlay)
            {
                // 排他的フルスクリーンでオーバーレイが表示できない場合
                await HideAllOverlaysAsync(cancellationToken).ConfigureAwait(false);
                
                // ユーザーに推奨表示
                await _fullscreenService.ShowRecommendationAsync(fullscreenMode).ConfigureAwait(false);
            }
            else
            {
                // オーバーレイ表示可能な場合は復元
                await ShowAllOverlaysAsync(cancellationToken).ConfigureAwait(false);
            }
            
            Statistics.IncrementOperationCount("HandleFullscreenChange");
            _logger.LogDebug("Fullscreen mode change handling completed");
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "Fullscreen mode change handling cancelled");
            Statistics.IncrementErrorCount("HandleFullscreenChange");
            throw;
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogError(ex, "Service disposed during fullscreen mode change handling");
            Statistics.IncrementErrorCount("HandleFullscreenChange");
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation handling fullscreen mode change");
            Statistics.IncrementErrorCount("HandleFullscreenChange");
            throw;
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Timeout handling fullscreen mode change");
            Statistics.IncrementErrorCount("HandleFullscreenChange");
            throw;
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported handling fullscreen mode change");
            Statistics.IncrementErrorCount("HandleFullscreenChange");
            throw;
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error handling fullscreen mode change");
            Statistics.IncrementErrorCount("HandleFullscreenChange");
            throw;
        }
    }
    
    /// <summary>
    /// すべてのオーバーレイを非表示
    /// </summary>
    private async Task HideAllOverlaysAsync(CancellationToken cancellationToken)
    {
        var hideTasks = _overlayStates.Keys.Select(async handle =>
        {
            try
            {
                var overlay = _overlayAdapter.GetOverlayWindow(handle);
                if (overlay is not null)
                {
                    await Task.Run(() => overlay.Hide(), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogDebug(ex, "Service disposed while hiding overlay 0x{Handle:X}", handle);
                await HandleOverlayErrorAsync(handle, ex).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Invalid operation hiding overlay 0x{Handle:X}", handle);
                await HandleOverlayErrorAsync(handle, ex).ConfigureAwait(false);
            }
            catch (NotSupportedException ex)
            {
                _logger.LogError(ex, "Operation not supported hiding overlay 0x{Handle:X}", handle);
                await HandleOverlayErrorAsync(handle, ex).ConfigureAwait(false);
            }
            catch (ExternalException ex)
            {
                _logger.LogError(ex, "External error hiding overlay 0x{Handle:X}", handle);
                await HandleOverlayErrorAsync(handle, ex).ConfigureAwait(false);
            }
        });
        
        await Task.WhenAll(hideTasks).ConfigureAwait(false);
        _logger.LogDebug("All overlays hidden due to exclusive fullscreen");
    }
    
    /// <summary>
    /// すべてのオーバーレイを表示
    /// </summary>
    private async Task ShowAllOverlaysAsync(CancellationToken cancellationToken)
    {
        var showTasks = _overlayStates.Keys.Select(async handle =>
        {
            try
            {
                var overlay = _overlayAdapter.GetOverlayWindow(handle);
                if (overlay is not null)
                {
                    await Task.Run(() => overlay.Show(), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogDebug(ex, "Service disposed while showing overlay 0x{Handle:X}", handle);
                await HandleOverlayErrorAsync(handle, ex).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Invalid operation showing overlay 0x{Handle:X}", handle);
                await HandleOverlayErrorAsync(handle, ex).ConfigureAwait(false);
            }
            catch (NotSupportedException ex)
            {
                _logger.LogError(ex, "Operation not supported showing overlay 0x{Handle:X}", handle);
                await HandleOverlayErrorAsync(handle, ex).ConfigureAwait(false);
            }
            catch (ExternalException ex)
            {
                _logger.LogError(ex, "External error showing overlay 0x{Handle:X}", handle);
                await HandleOverlayErrorAsync(handle, ex).ConfigureAwait(false);
            }
        });
        
        await Task.WhenAll(showTasks).ConfigureAwait(false);
        _logger.LogDebug("All overlays shown after fullscreen mode change");
    }
    
    /// <summary>
    /// すべてのオーバーレイを閉じる
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>閉じるタスク</returns>
    public async Task CloseAllOverlaysAsync(CancellationToken _ = default)
    {
        try
        {
            _logger.LogInformation("Closing all multi-monitor overlays");
            
            var allHandles = _overlayStates.Keys.ToList();
            foreach (var handle in allHandles)
            {
                await RemoveOverlayStateAsync(handle).ConfigureAwait(false);
            }
            
            await _overlayAdapter.CloseAllOverlaysAsync().ConfigureAwait(false);
            
            Statistics.IncrementOperationCount("CloseAllOverlays");
            _logger.LogInformation("All multi-monitor overlays closed");
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "Close all overlays cancelled");
            Statistics.IncrementErrorCount("CloseAllOverlays");
            throw;
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogError(ex, "Service disposed during close all overlays");
            Statistics.IncrementErrorCount("CloseAllOverlays");
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation closing all overlays");
            Statistics.IncrementErrorCount("CloseAllOverlays");
            throw;
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Timeout closing all overlays");
            Statistics.IncrementErrorCount("CloseAllOverlays");
            throw;
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported closing all overlays");
            Statistics.IncrementErrorCount("CloseAllOverlays");
            throw;
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error closing all overlays");
            Statistics.IncrementErrorCount("CloseAllOverlays");
            throw;
        }
    }
    
    /// <summary>
    /// イベント購読の初期化
    /// </summary>
    private void InitializeEventSubscriptions()
    {
        // モニター変更イベント
        _monitorAdapter.MonitorChanged.Subscribe(OnMonitorChanged);
        
        // フルスクリーンモード変更イベント
        _fullscreenService.FullscreenModeChanged += OnFullscreenModeChanged;
    }
    
    /// <summary>
    /// モニター変更イベント処理
    /// </summary>
    private async void OnMonitorChanged(MonitorChangedEventArgs e)
    {
        try
        {
            _logger.LogDebug("Monitor change detected: {Change}", e.ToString());
            
            if (TargetGameWindowHandle != nint.Zero)
            {
                var newActiveMonitor = _monitorAdapter.DetermineOptimalMonitorForGame(TargetGameWindowHandle);
                await HandleGameWindowMonitorChangeAsync(newActiveMonitor).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "Monitor change handling cancelled");
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Service disposed during monitor change handling");
            Statistics.IncrementErrorCount("OnMonitorChanged");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation handling monitor change");
            Statistics.IncrementErrorCount("OnMonitorChanged");
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported handling monitor change");
            Statistics.IncrementErrorCount("OnMonitorChanged");
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error handling monitor change");
            Statistics.IncrementErrorCount("OnMonitorChanged");
        }
    }
    
    /// <summary>
    /// フルスクリーンモード変更イベント処理
    /// </summary>
    private async void OnFullscreenModeChanged(object? sender, FullscreenModeChangedEventArgs e)
    {
        try
        {
            await HandleFullscreenModeChangeAsync(e).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "Fullscreen mode change handling cancelled");
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Service disposed during fullscreen mode change handling");
            Statistics.IncrementErrorCount("OnFullscreenModeChanged");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation handling fullscreen mode change");
            Statistics.IncrementErrorCount("OnFullscreenModeChanged");
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported handling fullscreen mode change");
            Statistics.IncrementErrorCount("OnFullscreenModeChanged");
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error handling fullscreen mode change");
            Statistics.IncrementErrorCount("OnFullscreenModeChanged");
        }
    }
    
    /// <summary>
    /// 初期アクティブモニターを決定
    /// </summary>
    private async Task DetermineInitialActiveMonitorAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            var activeMonitor = _monitorAdapter.DetermineOptimalMonitorForGame(TargetGameWindowHandle);
            
            lock (_lockObject)
            {
                CurrentActiveMonitor = activeMonitor;
            }
            
            _logger.LogInformation("Initial active monitor determined: {Monitor}", activeMonitor.Name);
        }, cancellationToken).ConfigureAwait(false);
    }
    
    /// <summary>
    /// アクティブモニターの確保
    /// </summary>
    private async Task<MonitorInfo> EnsureActiveMonitorAsync(CancellationToken cancellationToken)
    {
        if (CurrentActiveMonitor.HasValue)
            return CurrentActiveMonitor.Value;
        
        await DetermineInitialActiveMonitorAsync(cancellationToken).ConfigureAwait(false);
        return CurrentActiveMonitor ?? throw new InvalidOperationException("アクティブモニターを決定できませんでした");
    }
    
    /// <summary>
    /// 相対位置から絶対位置を計算
    /// </summary>
    private static CorePoint CalculateAbsolutePosition(CorePoint relativePosition, MonitorInfo monitor)
    {
        return new CorePoint(
            monitor.Bounds.X + relativePosition.X,
            monitor.Bounds.Y + relativePosition.Y);
    }
    
    /// <summary>
    /// モニター用の位置を計算
    /// </summary>
    private static CorePoint CalculatePositionForMonitor(CorePoint relativePosition, MonitorInfo targetMonitor)
    {
        return CalculateAbsolutePosition(relativePosition, targetMonitor);
    }
    
    /// <summary>
    /// モニター用のサイズを計算（DPIスケーリング考慮）
    /// </summary>
    private static CoreSize CalculateSizeForMonitor(CoreSize originalSize, MonitorInfo sourceMonitor, MonitorInfo targetMonitor)
    {
        var scaleFactorX = targetMonitor.ScaleFactorX / sourceMonitor.ScaleFactorX;
        var scaleFactorY = targetMonitor.ScaleFactorY / sourceMonitor.ScaleFactorY;
        
        return new CoreSize(
            originalSize.Width * scaleFactorX,
            originalSize.Height * scaleFactorY);
    }
    
    /// <summary>
    /// オーバーレイ・モニター変更イベントを通知
    /// </summary>
    private async Task NotifyOverlayMonitorChangedAsync(
        OverlayMonitorChangeType changeType,
        nint overlayHandle,
        MonitorInfo? sourceMonitor,
        MonitorInfo? targetMonitor,
        bool dpiChanged,
        CancellationToken cancellationToken)
    {
        try
        {
            var eventArgs = new OverlayMonitorChangedEventArgs(
                changeType, overlayHandle, sourceMonitor, targetMonitor, dpiChanged);
            
            await Task.Run(() =>
            {
                _overlayMonitorChangedSubject.OnNext(eventArgs);
            }, cancellationToken).ConfigureAwait(false);
            
            _logger.LogDebug("Overlay monitor change notified: {Change}", eventArgs.ToString());
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "Overlay monitor change notification cancelled");
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Service disposed during overlay monitor change notification");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation notifying overlay monitor change");
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported notifying overlay monitor change");
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error notifying overlay monitor change");
        }
    }
    
    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        
        _disposed = true;
        
        try
        {
            // イベント購読解除
            _fullscreenService.FullscreenModeChanged -= OnFullscreenModeChanged;
            
            // タイマー停止
            await _cleanupTimer.DisposeAsync().ConfigureAwait(false);
            await _healthCheckTimer.DisposeAsync().ConfigureAwait(false);
            
            // 全オーバーレイクローズ
            await CloseAllOverlaysAsync().ConfigureAwait(false);
            
            // Subject破棄
            _overlayMonitorChangedSubject.Dispose();
            
            _logger.LogInformation("MultiMonitorOverlayManager disposed asynchronously - Statistics: {Statistics}",
                Statistics.ToString());
        }
        catch (ObjectDisposedException)
        {
            // 既に破棄済み - 無視
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation during MultiMonitorOverlayManager disposal");
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported during MultiMonitorOverlayManager disposal");
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error during MultiMonitorOverlayManager disposal");
        }
    }
}

/// <summary>
/// オーバーレイのモニター状態情報
/// </summary>
/// <param name="OverlayHandle">オーバーレイハンドル</param>
/// <param name="CurrentMonitor">現在のモニター</param>
/// <param name="RelativePosition">ゲームウィンドウからの相対位置</param>
/// <param name="Size">サイズ</param>
/// <param name="LastUpdated">最終更新時刻</param>
public readonly record struct OverlayMonitorState(
    nint OverlayHandle,
    MonitorInfo CurrentMonitor,
    CorePoint RelativePosition,
    CoreSize Size,
    DateTime LastUpdated);

/// <summary>
/// オーバーレイ回復情報
/// </summary>
/// <param name="Size">サイズ</param>
/// <param name="RelativePosition">相対位置</param>
/// <param name="Monitor">モニター</param>
public readonly record struct OverlayRecoveryInfo(
    CoreSize Size,
    CorePoint RelativePosition,
    MonitorInfo Monitor);

/// <summary>
/// オーバーレイ・モニター変更イベント引数
/// </summary>
/// <param name="ChangeType">変更タイプ</param>
/// <param name="OverlayHandle">オーバーレイハンドル</param>
/// <param name="SourceMonitor">変更前のモニター</param>
/// <param name="TargetMonitor">変更後のモニター</param>
/// <param name="DpiChanged">DPI変更があったかどうか</param>
public readonly record struct OverlayMonitorChangedEventArgs(
    OverlayMonitorChangeType ChangeType,
    nint OverlayHandle,
    MonitorInfo? SourceMonitor,
    MonitorInfo? TargetMonitor,
    bool DpiChanged)
{
    /// <summary>
    /// イベント概要の文字列表現
    /// </summary>
    public override string ToString() => ChangeType switch
    {
        OverlayMonitorChangeType.Moved => 
            $"Overlay Moved: 0x{OverlayHandle:X} from {SourceMonitor?.Name} to {TargetMonitor?.Name}{(DpiChanged ? " (DPI Changed)" : "")}",
        OverlayMonitorChangeType.Created => 
            $"Overlay Created: 0x{OverlayHandle:X} on {TargetMonitor?.Name}",
        OverlayMonitorChangeType.Removed => 
            $"Overlay Removed: 0x{OverlayHandle:X} from {SourceMonitor?.Name}",
        _ => $"Unknown Change: {ChangeType}"
    };
}

/// <summary>
/// オーバーレイ・モニター変更タイプ
/// </summary>
public enum OverlayMonitorChangeType
{
    /// <summary>
    /// オーバーレイが作成された
    /// </summary>
    Created,
    
    /// <summary>
    /// オーバーレイがモニター間で移動した
    /// </summary>
    Moved,
    
    /// <summary>
    /// オーバーレイが削除された
    /// </summary>
    Removed
}

/// <summary>
/// オーバーレイマネージャー統計情報
/// </summary>
public sealed class OverlayManagerStatistics
{
    private readonly ConcurrentDictionary<string, int> _operationCounts = new();
    private readonly ConcurrentDictionary<string, int> _errorCounts = new();
    
    /// <summary>
    /// 管理開始時刻
    /// </summary>
    public DateTime? ManagementStartTime { get; set; }
    
    /// <summary>
    /// 最終ヘルスチェック時刻
    /// </summary>
    public DateTime? LastHealthCheckTime { get; set; }
    
    /// <summary>
    /// 作成されたオーバーレイ総数
    /// </summary>
    public int TotalOverlaysCreated { get; set; }
    
    /// <summary>
    /// オーバーレイ移動総数
    /// </summary>
    public int TotalOverlayMoves { get; set; }
    
    /// <summary>
    /// オーバーレイ回復総数
    /// </summary>
    public int TotalOverlayRecoveries { get; set; }
    
    /// <summary>
    /// 自動クリーンアップ総数
    /// </summary>
    public int TotalAutoCleanups { get; set; }
    
    /// <summary>
    /// 操作回数を増加
    /// </summary>
    /// <param name="operationName">操作名</param>
    public void IncrementOperationCount(string operationName)
    {
        _operationCounts.AddOrUpdate(operationName, 1, (key, count) => count + 1);
    }
    
    /// <summary>
    /// エラー回数を増加
    /// </summary>
    /// <param name="errorType">エラータイプ</param>
    public void IncrementErrorCount(string errorType)
    {
        _errorCounts.AddOrUpdate(errorType, 1, (key, count) => count + 1);
    }
    
    /// <summary>
    /// 操作回数を取得
    /// </summary>
    /// <param name="operationName">操作名</param>
    /// <returns>操作回数</returns>
    public int GetOperationCount(string operationName)
    {
        return _operationCounts.TryGetValue(operationName, out var count) ? count : 0;
    }
    
    /// <summary>
    /// エラー回数を取得
    /// </summary>
    /// <param name="errorType">エラータイプ</param>
    /// <returns>エラー回数</returns>
    public int GetErrorCount(string errorType)
    {
        return _errorCounts.TryGetValue(errorType, out var count) ? count : 0;
    }
    
    /// <summary>
    /// 統計情報の文字列表現
    /// </summary>
    public override string ToString()
    {
        var uptime = ManagementStartTime.HasValue 
            ? DateTime.UtcNow - ManagementStartTime.Value 
            : TimeSpan.Zero;
        
        return $"Uptime: {uptime:hh\\:mm\\:ss}, Created: {TotalOverlaysCreated}, " +
               $"Moves: {TotalOverlayMoves}, Recoveries: {TotalOverlayRecoveries}, " +
               $"Cleanups: {TotalAutoCleanups}";
    }
}
