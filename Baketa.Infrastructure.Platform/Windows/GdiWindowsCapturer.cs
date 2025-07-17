using System;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Infrastructure.Platform.Windows.Capture;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.Windows;

/// <summary>
/// GdiScreenCapturerをIWindowsCapturerに適応させるアダプター
/// </summary>
public class GdiWindowsCapturer : IWindowsCapturer, IDisposable
{
    private readonly IGdiScreenCapturer _gdiCapturer;
    private readonly ILogger<GdiWindowsCapturer>? _logger;
    private WindowsCaptureOptions _options = new();
    private bool _disposed;

    /// <summary>
    /// GdiWindowsCapturerのコンストラクタ
    /// </summary>
    /// <param name="gdiCapturer">GDI スクリーンキャプチャサービス</param>
    /// <param name="logger">ロガー</param>
    public GdiWindowsCapturer(IGdiScreenCapturer gdiCapturer, ILogger<GdiWindowsCapturer>? logger = null)
    {
        _gdiCapturer = gdiCapturer ?? throw new ArgumentNullException(nameof(gdiCapturer));
        _logger = logger;
    }

    /// <summary>
    /// 画面全体をキャプチャ
    /// </summary>
    /// <returns>キャプチャした画像</returns>
    public async Task<IWindowsImage> CaptureScreenAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        _logger?.LogDebug("画面全体キャプチャを開始");
        
        try
        {
            var result = await _gdiCapturer.CaptureScreenAsync().ConfigureAwait(false);
            _logger?.LogDebug("画面全体キャプチャが完了");
            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "画面全体キャプチャでエラーが発生");
            throw;
        }
    }

    /// <summary>
    /// 指定した領域をキャプチャ
    /// </summary>
    /// <param name="region">キャプチャする領域</param>
    /// <returns>キャプチャした画像</returns>
    public async Task<IWindowsImage> CaptureRegionAsync(Rectangle region)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        _logger?.LogDebug("領域キャプチャを開始: {Region}", region);
        
        try
        {
            var result = await _gdiCapturer.CaptureRegionAsync(region).ConfigureAwait(false);
            _logger?.LogDebug("領域キャプチャが完了");
            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "領域キャプチャでエラーが発生: {Region}", region);
            throw;
        }
    }

    /// <summary>
    /// 指定したウィンドウをキャプチャ
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>キャプチャした画像</returns>
    public async Task<IWindowsImage> CaptureWindowAsync(IntPtr windowHandle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        _logger?.LogDebug("ウィンドウキャプチャを開始: {WindowHandle}", windowHandle);
        
        try
        {
            var result = await _gdiCapturer.CaptureWindowAsync(windowHandle).ConfigureAwait(false);
            _logger?.LogDebug("ウィンドウキャプチャが完了");
            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ウィンドウキャプチャでエラーが発生: {WindowHandle}", windowHandle);
            throw;
        }
    }

    /// <summary>
    /// 指定したウィンドウのクライアント領域をキャプチャ
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>キャプチャした画像</returns>
    public async Task<IWindowsImage> CaptureClientAreaAsync(IntPtr windowHandle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        _logger?.LogDebug("ウィンドウクライアント領域キャプチャを開始: {WindowHandle}", windowHandle);
        
        try
        {
            // GdiScreenCapturerにはCaptureClientAreaAsyncがないため、ウィンドウキャプチャを代替として使用
            var result = await _gdiCapturer.CaptureWindowAsync(windowHandle).ConfigureAwait(false);
            _logger?.LogDebug("ウィンドウクライアント領域キャプチャが完了");
            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ウィンドウクライアント領域キャプチャでエラーが発生: {WindowHandle}", windowHandle);
            throw;
        }
    }

    /// <summary>
    /// キャプチャオプションを設定
    /// </summary>
    /// <param name="options">キャプチャオプション</param>
    public void SetCaptureOptions(WindowsCaptureOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger?.LogDebug("キャプチャオプションを設定: Quality={Quality}, IncludeCursor={IncludeCursor}", 
            _options.Quality, _options.IncludeCursor);
    }

    /// <summary>
    /// 現在のキャプチャオプションを取得
    /// </summary>
    /// <returns>キャプチャオプション</returns>
    public WindowsCaptureOptions GetCaptureOptions()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _options;
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        
        _gdiCapturer?.Dispose();
        _disposed = true;
        
        GC.SuppressFinalize(this);
        _logger?.LogDebug("GdiWindowsCapturerが破棄されました");
    }
}