using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.Windows.Overlay;

/// <summary>
/// LayeredOverlayWindow のファクトリー実装
/// </summary>
/// <remarks>
/// 🎯 [WIN32_OVERLAY_MIGRATION] Phase 1: Factory Pattern
/// - ILayeredOverlayWindow の生成を抽象化
/// - DIコンテナからのロガー注入
/// - テスト時のモック化が容易
/// </remarks>
[SupportedOSPlatform("windows")]
public interface ILayeredOverlayWindowFactory
{
    /// <summary>
    /// 新しい LayeredOverlayWindow インスタンスを作成
    /// </summary>
    ILayeredOverlayWindow Create();
}

/// <summary>
/// LayeredOverlayWindowFactory の実装
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LayeredOverlayWindowFactory : ILayeredOverlayWindowFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public LayeredOverlayWindowFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new System.ArgumentNullException(nameof(loggerFactory));
    }

    public ILayeredOverlayWindow Create()
    {
        var logger = _loggerFactory.CreateLogger<LayeredOverlayWindow>();
        return new LayeredOverlayWindow(logger);
    }
}
