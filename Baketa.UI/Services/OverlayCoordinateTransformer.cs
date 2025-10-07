using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.UI.Monitors;
using Baketa.UI.Services.Monitor;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// オーバーレイ座標変換とDPI補正を担当するサービス実装
/// Phase 4.1: InPlaceOverlayFactoryから座標変換ロジックを抽出
/// </summary>
public class OverlayCoordinateTransformer(
    IOverlayPositioningService overlayPositioningService,
    IMonitorManager monitorManager,
    IAdvancedMonitorService advancedMonitorService,
    ILogger<OverlayCoordinateTransformer> logger) : IOverlayCoordinateTransformer
{
    private readonly IOverlayPositioningService _overlayPositioningService = overlayPositioningService ?? throw new ArgumentNullException(nameof(overlayPositioningService));
    private readonly IMonitorManager _monitorManager = monitorManager ?? throw new ArgumentNullException(nameof(monitorManager));
    private readonly IAdvancedMonitorService _advancedMonitorService = advancedMonitorService ?? throw new ArgumentNullException(nameof(advancedMonitorService));
    private readonly ILogger<OverlayCoordinateTransformer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// 座標変換とDPI補正を実行して最適な表示位置を計算します
    /// </summary>
    public async Task<CoordinateTransformResult> TransformCoordinatesAsync(
        TextChunk textChunk,
        List<Rectangle> existingBounds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(textChunk);
        ArgumentNullException.ThrowIfNull(existingBounds);

        // キャンセレーションチェック
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var overlaySize = textChunk.GetOverlaySize();
            var options = new OverlayPositioningOptions(); // デフォルト設定を使用

            _logger.LogDebug("座標変換開始 - ChunkId: {ChunkId}, 入力ROI: ({X},{Y}) サイズ: {W}x{H}",
                textChunk.ChunkId, textChunk.CombinedBounds.X, textChunk.CombinedBounds.Y,
                textChunk.CombinedBounds.Width, textChunk.CombinedBounds.Height);

            // モニター情報取得
            var monitorResult = _monitorManager.DetermineOptimalMonitor(textChunk.SourceWindowHandle);
            if (monitorResult == null)
            {
                throw new InvalidOperationException($"モニター情報取得失敗 - ChunkId: {textChunk.ChunkId}");
            }

            _logger.LogDebug("対象モニター: {MonitorName}, 境界: ({X},{Y}) サイズ: {W}x{H}",
                monitorResult.Name, monitorResult.Bounds.X, monitorResult.Bounds.Y,
                monitorResult.Bounds.Width, monitorResult.Bounds.Height);

            // 座標変換実行
            var result = _overlayPositioningService.CalculateOptimalPosition(
                textChunk, overlaySize, monitorResult, existingBounds, options);

            var optimalPosition = result.Position;

            _logger.LogDebug("座標変換完了 - ChunkId: {ChunkId}, 最終座標: ({X},{Y}), 使用戦略: {Strategy}",
                textChunk.ChunkId, optimalPosition.X, optimalPosition.Y, result.UsedStrategy);

            // 座標妥当性検証
            var geometryPoint = new Baketa.Core.UI.Geometry.Point(optimalPosition.X, optimalPosition.Y);
            var isInMonitorBounds = monitorResult.Bounds.Contains(geometryPoint);

            if (!isInMonitorBounds)
            {
                _logger.LogWarning("座標がモニター境界外 - 座標: ({X},{Y}), モニター境界: {Bounds}",
                    optimalPosition.X, optimalPosition.Y, monitorResult.Bounds);
            }

            // Avalonia DPI補正適用
            var monitorType = _advancedMonitorService.DetectMonitorType(monitorResult);
            var advancedDpiInfo = _advancedMonitorService.GetAdvancedDpiInfo(monitorResult);

            var avaloniaCompensatedPosition = _advancedMonitorService.CompensateCoordinatesForAvalonia(
                optimalPosition, advancedDpiInfo);

            _logger.LogDebug("Avalonia DPI補正完了 - ChunkId: {ChunkId}, MonitorType: {MonitorType}, " +
                "Before: ({BeforeX},{BeforeY}) → After: ({AfterX},{AfterY}), Factor: {Factor}",
                textChunk.ChunkId, monitorType, optimalPosition.X, optimalPosition.Y,
                avaloniaCompensatedPosition.X, avaloniaCompensatedPosition.Y, advancedDpiInfo.CompensationFactor);

            // 座標変換結果を返す
            return new CoordinateTransformResult
            {
                OptimalPosition = optimalPosition,
                AvaloniaCompensatedPosition = avaloniaCompensatedPosition,
                Monitor = monitorResult,
                UsedStrategy = result.UsedStrategy.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "座標変換エラー - ChunkId: {ChunkId}", textChunk.ChunkId);
            throw;
        }
    }
}
