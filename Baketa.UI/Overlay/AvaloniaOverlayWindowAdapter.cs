using Baketa.Core.UI.Overlay;
using Baketa.Core.UI.Geometry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Overlay;

/// <summary>
/// Avaloniaオーバーレイウィンドウのアダプタークラス
/// プラットフォーム固有実装とUI層の仲介を行う
/// </summary>
/// <param name="serviceProvider">サービスプロバイダー</param>
/// <param name="logger">ロガー</param>
public sealed class AvaloniaOverlayWindowAdapter(IServiceProvider serviceProvider, ILogger<AvaloniaOverlayWindowAdapter> logger)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<AvaloniaOverlayWindowAdapter> _logger = logger;
    private IOverlayWindowManager? _platformManager;
    
    /// <summary>
    /// プラットフォーム固有のマネージャーを遅延初期化で取得
    /// </summary>
    private IOverlayWindowManager PlatformManager =>
        _platformManager ??= GetPlatformManager();
    
    /// <summary>
    /// オーバーレイウィンドウを作成します
    /// </summary>
    public async Task<IOverlayWindow> CreateOverlayWindowAsync(
        nint targetWindowHandle, 
        CoreSize initialSize, 
        CorePoint initialPosition)
    {
        try
        {
            _logger.LogDebug("Creating overlay window via adapter");
            var overlay = await PlatformManager.CreateOverlayWindowAsync(targetWindowHandle, initialSize, initialPosition).ConfigureAwait(false);
            _logger.LogInformation("Overlay window created successfully via adapter");
            return overlay;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create overlay window via adapter");
            throw;
        }
    }
    
    /// <summary>
    /// 指定されたハンドルのオーバーレイウィンドウを取得します
    /// </summary>
    public IOverlayWindow? GetOverlayWindow(nint handle)
    {
        return PlatformManager.GetOverlayWindow(handle);
    }
    
    /// <summary>
    /// すべてのオーバーレイウィンドウを閉じます
    /// </summary>
    public async Task CloseAllOverlaysAsync()
    {
        try
        {
            await PlatformManager.CloseAllOverlaysAsync().ConfigureAwait(false);
            _logger.LogInformation("All overlays closed via adapter");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing overlays via adapter");
            throw;
        }
    }
    
    /// <summary>
    /// アクティブなオーバーレイウィンドウの数
    /// </summary>
    public int ActiveOverlayCount => PlatformManager.ActiveOverlayCount;
    
    /// <summary>
    /// プラットフォーム固有のマネージャーを取得
    /// </summary>
    private IOverlayWindowManager GetPlatformManager()
    {
        try
        {
            // プラットフォーム固有の実装を取得
            // 実際の実装はDI設定時に決定される
            var manager = _serviceProvider.GetRequiredService<IOverlayWindowManager>();
            _logger.LogDebug("Platform overlay manager resolved: {Type}", manager.GetType().Name);
            return manager;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve platform overlay manager");
            throw new InvalidOperationException("Platform overlay manager is not available", ex);
        }
    }
}

/// <summary>
/// オーバーレイウィンドウのファクトリークラス
/// </summary>
public static class OverlayWindowFactory
{
    /// <summary>
    /// デフォルト設定でオーバーレイウィンドウを作成
    /// </summary>
    /// <param name="manager">オーバーレイマネージャー</param>
    /// <param name="targetWindowHandle">ターゲットウィンドウハンドル</param>
    /// <returns>作成されたオーバーレイウィンドウ</returns>
    public static async Task<IOverlayWindow> CreateDefaultOverlayAsync(
        IOverlayWindowManager manager, 
        nint targetWindowHandle)
    {
        ArgumentNullException.ThrowIfNull(manager);
        
        // デフォルト設定
        var defaultSize = new CoreSize(600, 100);
        var defaultPosition = new CorePoint(100, 100);
        
        return await manager.CreateOverlayWindowAsync(targetWindowHandle, defaultSize, defaultPosition).ConfigureAwait(false);
    }
    
    /// <summary>
    /// 翻訳テキスト表示用のオーバーレイウィンドウを作成
    /// </summary>
    /// <param name="manager">オーバーレイマネージャー</param>
    /// <param name="targetWindowHandle">ターゲットウィンドウハンドル</param>
    /// <param name="textBounds">テキスト境界</param>
    /// <returns>作成されたオーバーレイウィンドウ</returns>
    public static async Task<IOverlayWindow> CreateTranslationOverlayAsync(
        IOverlayWindowManager manager,
        nint targetWindowHandle,
        CoreRect textBounds)
    {
        ArgumentNullException.ThrowIfNull(manager);
        
        // テキスト境界に基づいてサイズと位置を計算
        var overlaySize = new CoreSize(
            Math.Max(textBounds.Width, 200), 
            Math.Max(textBounds.Height + 20, 60));
        
        var overlayPosition = new CorePoint(
            textBounds.X, 
            textBounds.Y + textBounds.Height + 5);
        
        var overlay = await manager.CreateOverlayWindowAsync(targetWindowHandle, overlaySize, overlayPosition).ConfigureAwait(false);
        
        // 翻訳テキスト用のヒットテスト領域を追加
        var hitTestArea = new CoreRect(0, 0, overlaySize.Width, overlaySize.Height);
        overlay.AddHitTestArea(hitTestArea);
        
        return overlay;
    }
}