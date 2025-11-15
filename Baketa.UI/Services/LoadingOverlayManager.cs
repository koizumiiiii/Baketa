using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Baketa.UI.Utils;
using Baketa.UI.Views;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// 画面中央ローディングオーバーレイの管理サービス
/// </summary>
public class LoadingOverlayManager(ILogger<LoadingOverlayManager> logger) : IDisposable
{
    private readonly ILogger<LoadingOverlayManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private LoadingOverlayView? _loadingWindow;
    private bool _disposed;
    private readonly object _lockObject = new();

    /// <summary>
    /// ローディングオーバーレイを表示
    /// </summary>
    public async Task ShowAsync()
    {
        Console.WriteLine("🔄 LoadingOverlayManager.ShowAsync開始");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 LoadingOverlayManager.ShowAsync開始");

        lock (_lockObject)
        {
            if (_disposed)
            {
                Console.WriteLine("⚠️ LoadingOverlayManager既に破棄済み");
                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "⚠️ LoadingOverlayManager既に破棄済み");
                return;
            }
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    // 既存のウィンドウがある場合は閉じる
                    if (_loadingWindow != null)
                    {
                        _loadingWindow.Close();
                        _loadingWindow = null;
                    }

                    // 新しいローディングウィンドウを作成
                    _loadingWindow = new LoadingOverlayView();
                    _loadingWindow.Show();

                    Console.WriteLine("✅ ローディングオーバーレイ表示完了");
                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ ローディングオーバーレイ表示完了");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"💥 ローディングオーバーレイ表示エラー: {ex.Message}");
                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 ローディングオーバーレイ表示エラー: {ex.Message}");
                    _logger.LogError(ex, "ローディングオーバーレイ表示に失敗");
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 LoadingOverlayManager.ShowAsync例外: {ex.Message}");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 LoadingOverlayManager.ShowAsync例外: {ex.Message}");
            _logger.LogError(ex, "ローディングオーバーレイ表示で予期しない例外");
        }
    }

    /// <summary>
    /// ローディングオーバーレイを非表示
    /// </summary>
    public async Task HideAsync()
    {
        Console.WriteLine("🔄 LoadingOverlayManager.HideAsync開始");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 LoadingOverlayManager.HideAsync開始");

        lock (_lockObject)
        {
            if (_disposed)
            {
                Console.WriteLine("⚠️ LoadingOverlayManager既に破棄済み");
                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "⚠️ LoadingOverlayManager既に破棄済み");
                return;
            }
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    if (_loadingWindow != null)
                    {
                        _loadingWindow.Close();
                        _loadingWindow = null;

                        Console.WriteLine("✅ ローディングオーバーレイ非表示完了");
                        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ ローディングオーバーレイ非表示完了");
                    }
                    else
                    {
                        Console.WriteLine("⚠️ ローディングオーバーレイが既にnull");
                        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "⚠️ ローディングオーバーレイが既にnull");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"💥 ローディングオーバーレイ非表示エラー: {ex.Message}");
                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 ローディングオーバーレイ非表示エラー: {ex.Message}");
                    _logger.LogError(ex, "ローディングオーバーレイ非表示に失敗");
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 LoadingOverlayManager.HideAsync例外: {ex.Message}");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 LoadingOverlayManager.HideAsync例外: {ex.Message}");
            _logger.LogError(ex, "ローディングオーバーレイ非表示で予期しない例外");
        }
    }

    public void Dispose()
    {
        lock (_lockObject)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        try
        {
            if (_loadingWindow != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        _loadingWindow.Close();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "ローディングオーバーレイ破棄時エラー");
                    }
                });
                _loadingWindow = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoadingOverlayManager破棄時エラー");
        }

        GC.SuppressFinalize(this);
    }
}
