using Baketa.Core.UI.Fullscreen;
using Baketa.Core.UI.Overlay.Positioning;
using Baketa.UI.Monitors;
using Baketa.UI.Overlay;
using Baketa.UI.Overlay.MultiMonitor;
using Baketa.UI.Overlay.Positioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.DI.Modules;

/// <summary>
/// オーバーレイ位置管理システムのDIモジュール
/// </summary>
public sealed class OverlayPositioningModule
{
    /// <summary>
    /// オーバーレイ位置管理関連のサービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public static void RegisterServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // マルチモニター関連サービス（依存関係の順序で登録）
        services.AddSingleton<AvaloniaMultiMonitorAdapter>();
        services.AddSingleton<AvaloniaOverlayWindowAdapter>();
        services.AddSingleton<MultiMonitorOverlayManager>();

        // テキスト測定サービス
        services.AddSingleton<ITextMeasurementService, AvaloniaTextMeasurementService>();

        // オーバーレイ位置管理システム（ファクトリーパターンで作成）
        services.AddSingleton<IOverlayPositionManagerFactory, OverlayPositionManagerFactory>();
    }
}

/// <summary>
/// オーバーレイ位置管理システムのファクトリー実装
/// </summary>
/// <param name="logger">ロガー</param>
/// <param name="multiMonitorManager">マルチモニター管理システム</param>
/// <param name="textMeasurementService">テキスト測定サービス</param>
public sealed class OverlayPositionManagerFactory(
    ILogger<OverlayPositionManager> logger,
    MultiMonitorOverlayManager multiMonitorManager,
    ITextMeasurementService textMeasurementService) : IOverlayPositionManagerFactory
{
    private readonly ILogger<OverlayPositionManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly MultiMonitorOverlayManager _multiMonitorManager = multiMonitorManager ?? throw new ArgumentNullException(nameof(multiMonitorManager));
    private readonly ITextMeasurementService _textMeasurementService = textMeasurementService ?? throw new ArgumentNullException(nameof(textMeasurementService));

    /// <inheritdoc/>
    public async Task<IOverlayPositionManager> CreateAsync(CancellationToken cancellationToken = default)
    {
        return await CreateWithSettingsAsync(OverlayPositionSettings.ForTranslation, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IOverlayPositionManager> CreateWithSettingsAsync(
        OverlayPositionSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await Task.Yield(); // 非同期メソッドのため

        var positionManager = new OverlayPositionManager(
            _logger,
            _multiMonitorManager,
            _textMeasurementService
        )
        {
            // 設定を適用
            PositionMode = settings.PositionMode,
            SizeMode = settings.SizeMode,
            FixedPosition = settings.FixedPosition,
            FixedSize = settings.FixedSize,
            PositionOffset = settings.PositionOffset,
            MaxSize = settings.MaxSize,
            MinSize = settings.MinSize
        };

        return positionManager;
    }
}
