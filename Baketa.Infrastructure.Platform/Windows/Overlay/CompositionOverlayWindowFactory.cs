using System.Runtime.Versioning;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Platform.Windows.Overlay;

/// <summary>
/// CompositionOverlayWindow のファクトリー実装
/// </summary>
/// <remarks>
/// 🔥 [DWM_BLUR_IMPLEMENTATION] DWM Composition Window Factory
/// - DWM Composition + Blur効果対応のオーバーレイウィンドウを生成
/// - OverlaySettings からブラー設定を読み込み
/// - DIコンテナからのロガー注入
/// - テスト時のモック化が容易
/// </remarks>
[SupportedOSPlatform("windows6.0")] // Windows Vista+
public interface ICompositionOverlayWindowFactory
{
    /// <summary>
    /// 新しい CompositionOverlayWindow インスタンスを作成
    /// </summary>
    ILayeredOverlayWindow Create();
}

/// <summary>
/// CompositionOverlayWindowFactory の実装
/// </summary>
[SupportedOSPlatform("windows6.0")] // Windows Vista+
public sealed class CompositionOverlayWindowFactory : ICompositionOverlayWindowFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly OverlaySettings _overlaySettings;

    public CompositionOverlayWindowFactory(
        ILoggerFactory loggerFactory,
        IOptions<OverlaySettings> overlaySettings)
    {
        _loggerFactory = loggerFactory ?? throw new System.ArgumentNullException(nameof(loggerFactory));
        _overlaySettings = overlaySettings?.Value ?? throw new System.ArgumentNullException(nameof(overlaySettings));
    }

    public ILayeredOverlayWindow Create()
    {
        var logger = _loggerFactory.CreateLogger<CompositionOverlayWindow>();

        // 🔥 [DWM_BLUR_IMPLEMENTATION] 設定からブラーパラメータを読み込み
        var enableBlur = _overlaySettings.EnableBlur;
        var blurOpacity = _overlaySettings.BlurOpacity;

        _loggerFactory.CreateLogger<CompositionOverlayWindowFactory>()
            .LogDebug("🔥 [DWM_BLUR_IMPLEMENTATION] Creating CompositionOverlayWindow (EnableBlur: {EnableBlur}, BlurOpacity: {BlurOpacity})",
                enableBlur, blurOpacity);

        return new CompositionOverlayWindow(logger, enableBlur, blurOpacity);
    }
}
