using System.Drawing;
using System.IO;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Models.Capture;
using Baketa.Infrastructure.Platform.Adapters;
using Microsoft.Extensions.Logging;
using ServicesCaptureOptions = Baketa.Core.Abstractions.Services.CaptureOptions;

namespace Baketa.Application.Services.Capture;

/// <summary>
/// 適応的キャプチャサービスを従来のICaptureServiceインターフェースに適合させるアダプター
/// </summary>
public class AdaptiveCaptureServiceAdapter : ICaptureService, IDisposable
{
    private readonly IAdaptiveCaptureService _adaptiveCaptureService;
    private readonly ILogger<AdaptiveCaptureServiceAdapter> _logger;
    private ServicesCaptureOptions _currentOptions = new();
    private bool _disposed = false;

    public AdaptiveCaptureServiceAdapter(
        IAdaptiveCaptureService adaptiveCaptureService,
        ILogger<AdaptiveCaptureServiceAdapter> logger)
    {
        _adaptiveCaptureService = adaptiveCaptureService ?? throw new ArgumentNullException(nameof(adaptiveCaptureService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IImage> CaptureScreenAsync()
    {
        try
        {
            _logger.LogInformation("🔥 適応的キャプチャサービスアダプター: CaptureScreenAsync呼び出され - Windows Graphics Capture API使用予定");
            _logger.LogDebug("適応的画面キャプチャ開始");

            // 画面全体キャプチャ用のCaptureOptionsを作成
            var adaptiveCaptureOptions = CreateAdaptiveCaptureOptions();
            
            // デスクトップのHWNDを取得（画面全体キャプチャ用）
            var desktopHwnd = GetDesktopWindowHandle();
            
            var result = await _adaptiveCaptureService.CaptureAsync(desktopHwnd, adaptiveCaptureOptions).ConfigureAwait(false);
            
            if (!result.Success || result.CapturedImages == null || result.CapturedImages.Count == 0)
            {
                throw new InvalidOperationException($"適応的画面キャプチャに失敗: {result.ErrorDetails}");
            }

            _logger.LogInformation("適応的画面キャプチャ成功: 戦略={Strategy}, 処理時間={ProcessingTime}ms", 
                result.StrategyUsed, result.ProcessingTime.TotalMilliseconds);

            // IWindowsImageをIImageアダプターでラップして返す
            return new WindowsImageAdapter(result.CapturedImages[0]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "適応的画面キャプチャでエラー");
            throw;
        }
    }

    public async Task<IImage> CaptureRegionAsync(Rectangle region)
    {
        try
        {
            _logger.LogDebug("適応的領域キャプチャ開始: {Region}", region);

            // 領域キャプチャ用のCaptureOptionsを作成
            var adaptiveCaptureOptions = CreateAdaptiveCaptureOptions();
            // 注意: 現在のCaptureOptionsにはTargetRegionプロパティがないため、ROI処理を有効化のみ
            adaptiveCaptureOptions.AllowROIProcessing = true;
            
            // デスクトップのHWNDを使用
            var desktopHwnd = GetDesktopWindowHandle();
            
            var result = await _adaptiveCaptureService.CaptureAsync(desktopHwnd, adaptiveCaptureOptions).ConfigureAwait(false);
            
            if (!result.Success || result.CapturedImages == null || result.CapturedImages.Count == 0)
            {
                throw new InvalidOperationException($"適応的領域キャプチャに失敗: {result.ErrorDetails}");
            }

            _logger.LogInformation("適応的領域キャプチャ成功: 戦略={Strategy}, 処理時間={ProcessingTime}ms", 
                result.StrategyUsed, result.ProcessingTime.TotalMilliseconds);

            // IWindowsImageをIImageアダプターでラップして返す
            return new WindowsImageAdapter(result.CapturedImages[0]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "適応的領域キャプチャでエラー");
            throw;
        }
    }

    public async Task<IImage> CaptureWindowAsync(IntPtr windowHandle)
    {
        try
        {
            Console.WriteLine("🔥🔥🔥 [ADAPTER] CaptureWindowAsync呼び出されました！HWND=0x{0:X}", windowHandle.ToInt64());
        
        // ログファイルにも出力
        try 
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
            File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥🔥🔥 [ADAPTER] CaptureWindowAsync呼び出されました！HWND=0x{windowHandle.ToInt64():X}{Environment.NewLine}");
        }
        catch { /* ログファイル書き込み失敗は無視 */ }
            _logger.LogInformation("🔥 適応的キャプチャサービスアダプター: CaptureWindowAsync呼び出され - HWND=0x{WindowHandle:X}", windowHandle.ToInt64());
            _logger.LogDebug("適応的ウィンドウキャプチャ開始: HWND=0x{WindowHandle:X}", windowHandle.ToInt64());

            // ウィンドウキャプチャ用のCaptureOptionsを作成
            var adaptiveCaptureOptions = CreateAdaptiveCaptureOptions();
            
            var result = await _adaptiveCaptureService.CaptureAsync(windowHandle, adaptiveCaptureOptions).ConfigureAwait(false);
            
            if (!result.Success || result.CapturedImages == null || result.CapturedImages.Count == 0)
            {
                throw new InvalidOperationException($"適応的ウィンドウキャプチャに失敗: {result.ErrorDetails}");
            }

            _logger.LogInformation("適応的ウィンドウキャプチャ成功: 戦略={Strategy}, 処理時間={ProcessingTime}ms", 
                result.StrategyUsed, result.ProcessingTime.TotalMilliseconds);

            // IWindowsImageをIImageアダプターでラップして返す
            return new WindowsImageAdapter(result.CapturedImages[0]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "適応的ウィンドウキャプチャでエラー");
            throw;
        }
    }

    public async Task<IImage> CaptureClientAreaAsync(IntPtr windowHandle)
    {
        try
        {
            _logger.LogDebug("適応的クライアント領域キャプチャ開始: HWND=0x{WindowHandle:X}", windowHandle.ToInt64());

            // クライアント領域キャプチャ用のCaptureOptionsを作成
            var adaptiveCaptureOptions = CreateAdaptiveCaptureOptions();
            // 注意: 現在のCaptureOptionsにはCaptureClientAreaOnlyプロパティがないため、通常のキャプチャを使用
            
            var result = await _adaptiveCaptureService.CaptureAsync(windowHandle, adaptiveCaptureOptions).ConfigureAwait(false);
            
            if (!result.Success || result.CapturedImages == null || result.CapturedImages.Count == 0)
            {
                throw new InvalidOperationException($"適応的クライアント領域キャプチャに失敗: {result.ErrorDetails}");
            }

            _logger.LogInformation("適応的クライアント領域キャプチャ成功: 戦略={Strategy}, 処理時間={ProcessingTime}ms", 
                result.StrategyUsed, result.ProcessingTime.TotalMilliseconds);

            // IWindowsImageをIImageアダプターでラップして返す
            return new WindowsImageAdapter(result.CapturedImages[0]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "適応的クライアント領域キャプチャでエラー");
            throw;
        }
    }

    public async Task<bool> DetectChangesAsync(IImage previousImage, IImage currentImage, float threshold = 0.05f)
    {
        try
        {
            // 基本的な差分検出の実装
            // より高度な差分検出は適応的キャプチャシステム内で実装される
            if (previousImage == null || currentImage == null)
                return true;

            // 画像サイズが異なる場合は変更ありとみなす
            if (previousImage.Width != currentImage.Width || previousImage.Height != currentImage.Height)
                return true;

            // 簡易的な差分検出（より高度な実装は将来的に適応的キャプチャシステムに移行）
            await Task.CompletedTask.ConfigureAwait(false);
            return true; // 一時的に常に変更ありとする
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "差分検出でエラー");
            return true; // エラー時は変更ありとみなす
        }
    }

    public void SetCaptureOptions(ServicesCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        
        _currentOptions = options;
        
        _logger.LogDebug("キャプチャオプション設定: 間隔={Interval}ms, 品質={Quality}", 
            options.CaptureInterval, options.Quality);
    }

    public ServicesCaptureOptions GetCaptureOptions()
    {
        return _currentOptions;
    }

    private Baketa.Core.Models.Capture.CaptureOptions CreateAdaptiveCaptureOptions()
    {
        return new Baketa.Core.Models.Capture.CaptureOptions
        {
            AllowDirectFullScreen = true,
            AllowROIProcessing = true,
            AllowSoftwareFallback = true,
            ROIScaleFactor = 0.25f,
            MaxRetryAttempts = 3,
            EnableHDRProcessing = true,
            TDRTimeoutMs = 2000
        };
    }

    private static IntPtr GetDesktopWindowHandle()
    {
        // デスクトップのHWNDを取得（Win32 API）
        return GetDesktopWindow();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();
    
    /// <summary>
    /// キャプチャサービスを停止
    /// </summary>
    public async Task StopAsync()
    {
        if (_disposed)
            return;
            
        try
        {
            _logger.LogInformation("AdaptiveCaptureServiceAdapter停止処理開始");
            await _adaptiveCaptureService.StopAsync().ConfigureAwait(false);
            _logger.LogInformation("AdaptiveCaptureServiceAdapter停止処理完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AdaptiveCaptureServiceAdapter停止中にエラー");
        }
    }
    
    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
            
        try
        {
            StopAsync().Wait(TimeSpan.FromSeconds(5));
            
            if (_adaptiveCaptureService is IDisposable disposableService)
            {
                disposableService.Dispose();
            }
            
            _disposed = true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "AdaptiveCaptureServiceAdapter破棄中にエラー");
        }
        
        GC.SuppressFinalize(this);
    }
}