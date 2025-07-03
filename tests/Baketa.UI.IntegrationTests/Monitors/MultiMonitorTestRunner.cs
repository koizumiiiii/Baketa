using Baketa.Core.UI.Geometry;
using Baketa.Core.UI.Monitors;
using Baketa.UI.Monitors;
using Microsoft.Extensions.Logging;
using Point = Baketa.Core.UI.Geometry.Point;
using Rect = Baketa.Core.UI.Geometry.Rect;

namespace Baketa.UI.IntegrationTests.Monitors;

/// <summary>
/// マルチモニターサポートの動作確認テストクラス
/// 開発・デバッグ時の動作検証に使用
/// </summary>
/// <remarks>
/// MultiMonitorTestRunnerを初期化
/// </remarks>
/// <param name="adapter">マルチモニターアダプター</param>
/// <param name="logger">ロガー</param>
public sealed class MultiMonitorTestRunner(
    AvaloniaMultiMonitorAdapter adapter,
    ILogger<MultiMonitorTestRunner> logger) : IDisposable
{
    private readonly AvaloniaMultiMonitorAdapter _adapter = adapter;
    private readonly ILogger<MultiMonitorTestRunner> _logger = logger;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private bool _disposed;

    /// <summary>
    /// 基本的なマルチモニター機能テストを実行
    /// </summary>
    /// <returns>テスト実行タスク</returns>
    public async Task RunBasicTestsAsync()
    {
        try
        {
            _logger.LogInformation("=== マルチモニターサポート 基本テスト開始 ===");
            
            // 1. モニター列挙テスト
            await TestMonitorEnumerationAsync().ConfigureAwait(false);
            
            // 2. モニター情報詳細テスト
            await TestMonitorDetailsAsync().ConfigureAwait(false);
            
            // 3. 座標変換テスト
            await TestCoordinateTransformationAsync().ConfigureAwait(false);
            
            // 4. DPI変更検出テスト
            await TestDpiChangeDetectionAsync().ConfigureAwait(false);
            
            // 5. モニター監視テスト
            await TestMonitorMonitoringAsync().ConfigureAwait(false);
            
            _logger.LogInformation("=== マルチモニターサポート 基本テスト完了 ===");
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "マルチモニターテストがキャンセルされました");
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "マルチモニターテストで無効な操作が発生しました");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "マルチモニターテストでエラーが発生しました");
            throw;
        }
    }
    
    /// <summary>
    /// モニター列挙機能のテスト
    /// </summary>
    private async Task TestMonitorEnumerationAsync()
    {
        _logger.LogInformation("--- モニター列挙テスト ---");
        
        await _adapter.RefreshMonitorsAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
        
        _logger.LogInformation("検出されたモニター数: {Count}", _adapter.MonitorCount);
        
        var monitors = _adapter.Monitors;
        for (int i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            _logger.LogInformation("モニター {Index}: {Monitor}", i + 1, monitor.ToString());
        }
        
        var primaryMonitor = _adapter.PrimaryMonitor;
        if (primaryMonitor.HasValue)
        {
            _logger.LogInformation("プライマリモニター: {Primary}", primaryMonitor.Value.ToString());
        }
        else
        {
            _logger.LogWarning("プライマリモニターが見つかりません");
        }
    }
    
    /// <summary>
    /// モニター詳細情報のテスト
    /// </summary>
    private Task TestMonitorDetailsAsync()
    {
        _logger.LogInformation("--- モニター詳細情報テスト ---");
        
        var monitors = _adapter.Monitors;
        foreach (var monitor in monitors)
        {
            _logger.LogInformation("モニター詳細:");
            _logger.LogInformation("  ハンドル: 0x{Handle:X}", monitor.Handle);
            _logger.LogInformation("  名前: {Name}", monitor.Name);
            _logger.LogInformation("  デバイスID: {DeviceId}", monitor.DeviceId);
            _logger.LogInformation("  境界: {Bounds}", monitor.Bounds);
            _logger.LogInformation("  作業領域: {WorkArea}", monitor.WorkArea);
            _logger.LogInformation("  プライマリ: {IsPrimary}", monitor.IsPrimary);
            _logger.LogInformation("  DPI: {DpiX}x{DpiY}", monitor.DpiX, monitor.DpiY);
            _logger.LogInformation("  スケールファクター: {ScaleX}x{ScaleY}", 
                monitor.ScaleFactorX, monitor.ScaleFactorY);
            _logger.LogInformation("");
        }
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// 座標変換機能のテスト
    /// </summary>
    private Task TestCoordinateTransformationAsync()
    {
        _logger.LogInformation("--- 座標変換テスト ---");
        
        var monitors = _adapter.Monitors;
        if (monitors.Count < 2)
        {
            _logger.LogInformation("座標変換テストにはモニターが2台以上必要です（現在: {Count}台）", monitors.Count);
            return Task.CompletedTask;
        }
        
        var sourceMonitor = monitors[0];
        var targetMonitor = monitors[1];
        
        // テスト座標
        var testPoints = new Point[]
        {
            new(100, 100),
            new(sourceMonitor.Bounds.Width / 2, sourceMonitor.Bounds.Height / 2),
            new(sourceMonitor.Bounds.Width - 100, sourceMonitor.Bounds.Height - 100)
        };
        
        foreach (var point in testPoints)
        {
            var transformedPoint = _adapter.TransformOverlayBetweenMonitors(
                new Rect(point.X, point.Y, 50, 30),
                sourceMonitor,
                targetMonitor);
            
            _logger.LogInformation("座標変換: ({X}, {Y}) → ({TransX}, {TransY})",
                point.X, point.Y, transformedPoint.X, transformedPoint.Y);
        }
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// DPI変更検出機能のテスト
    /// </summary>
    private Task TestDpiChangeDetectionAsync()
    {
        _logger.LogInformation("--- DPI変更検出テスト ---");
        
        var monitors = _adapter.Monitors;
        if (monitors.Count < 2)
        {
            _logger.LogInformation("DPI変更検出テストにはモニターが2台以上必要です");
            return Task.CompletedTask;
        }
        
        for (int i = 0; i < monitors.Count - 1; i++)
        {
            var monitor1 = monitors[i];
            var monitor2 = monitors[i + 1];
            
            bool dpiChanged = _adapter.HasDpiChanged(monitor1, monitor2);
            _logger.LogInformation("DPI変更検出: {Monitor1} → {Monitor2} = {Changed}",
                monitor1.Name, monitor2.Name, dpiChanged);
        }
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// モニター監視機能のテスト
    /// </summary>
    private async Task TestMonitorMonitoringAsync()
    {
        _logger.LogInformation("--- モニター監視テスト ---");
        
        // モニター変更イベントの購読
        using var subscription = _adapter.MonitorChanged.Subscribe(eventArgs =>
        {
            _logger.LogInformation("モニター変更イベント: {Change}", eventArgs.ToString());
        });
        
        // 監視が既に開始されているかチェック
        if (!_adapter.IsMonitoring)
        {
            await _adapter.StartMonitoringAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
            _logger.LogInformation("モニター監視を開始しました");
        }
        else
        {
            _logger.LogInformation("モニター監視は既に開始されています");
        }
        
        // 短時間待機（実際の変更は手動でモニターを接続/切断する必要がある）
        await Task.Delay(2000, _cancellationTokenSource.Token).ConfigureAwait(false);
        
        _logger.LogInformation("モニター監視テスト完了（手動でモニターを接続/切断して変更イベントを確認してください）");
    }
    
    /// <summary>
    /// 特定のゲームウィンドウでのテスト
    /// </summary>
    /// <param name="gameWindowHandle">ゲームウィンドウハンドル</param>
    /// <returns>テスト実行タスク</returns>
    public async Task TestWithGameWindowAsync(nint gameWindowHandle)
    {
        try
        {
            _logger.LogInformation("=== ゲームウィンドウでのマルチモニターテスト 開始 ===");
            _logger.LogInformation("ゲームウィンドウハンドル: 0x{Handle:X}", gameWindowHandle);
            
            // ウィンドウのモニターを特定
            var currentMonitor = _adapter.GetMonitorFromWindow(gameWindowHandle);
            if (currentMonitor.HasValue)
            {
                _logger.LogInformation("現在のモニター: {Monitor}", currentMonitor.Value.ToString());
                
                // 最適なモニター決定のテスト
                var optimalMonitor = _adapter.DetermineOptimalMonitorForGame(gameWindowHandle);
                _logger.LogInformation("最適モニター: {Monitor}", optimalMonitor.ToString());
                
                var isSameMonitor = currentMonitor.Value.Handle == optimalMonitor.Handle;
                _logger.LogInformation("現在と最適が一致: {IsSame}", isSameMonitor);
            }
            else
            {
                _logger.LogWarning("ゲームウィンドウのモニターを特定できませんでした");
            }
            
            await Task.Delay(100).ConfigureAwait(false); // 短い遅延
            
            _logger.LogInformation("=== ゲームウィンドウでのマルチモニターテスト 完了 ===");
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "ゲームウィンドウテストがキャンセルされました");
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "ゲームウィンドウテストがキャンセルされました");
            throw;
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "ゲームウィンドウテストで無効な引数エラーが発生しました");
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "ゲームウィンドウテストで無効な操作エラーが発生しました");
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "ゲームウィンドウテストで予期しないエラーが発生しました");
            throw;
        }
#pragma warning restore CA1031
    }
    
    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        
        _disposed = true;
        
        try
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            _adapter.Dispose();
            
            _logger.LogInformation("MultiMonitorTestRunner disposed");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation during MultiMonitorTestRunner disposal");
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during MultiMonitorTestRunner disposal");
        }
#pragma warning restore CA1031
    }
}
