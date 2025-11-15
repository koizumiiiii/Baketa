using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.UI.Geometry;
using Baketa.Core.UI.Monitors;
using Baketa.UI.Services.Monitor;
using Baketa.UI.Views.Overlay;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Factories;

/// <summary>
/// インプレースオーバーレイの作成と表示を担当するファクトリー実装
/// Phase 4.1: Factory Pattern適用によるInPlaceTranslationOverlayManager簡素化
/// </summary>
public class InPlaceOverlayFactory(
    IOverlayPositioningService overlayPositioningService,
    IMonitorManager monitorManager,
    IAdvancedMonitorService advancedMonitorService,
    ILogger<InPlaceOverlayFactory> logger) : IInPlaceOverlayFactory
{
    private readonly IOverlayPositioningService _overlayPositioningService = overlayPositioningService ?? throw new ArgumentNullException(nameof(overlayPositioningService));
    private readonly IMonitorManager _monitorManager = monitorManager ?? throw new ArgumentNullException(nameof(monitorManager));
    private readonly IAdvancedMonitorService _advancedMonitorService = advancedMonitorService ?? throw new ArgumentNullException(nameof(advancedMonitorService));
    private readonly ILogger<InPlaceOverlayFactory> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// 新規インプレースオーバーレイを作成して表示します
    /// </summary>
    public async Task<InPlaceTranslationOverlayWindow> CreateAndShowOverlayAsync(
        TextChunk textChunk,
        List<Rectangle> existingBounds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(textChunk);
        ArgumentNullException.ThrowIfNull(existingBounds);

        // キャンセレーションチェック
        cancellationToken.ThrowIfCancellationRequested();

        InPlaceTranslationOverlayWindow? newOverlay = null;

        try
        {
            // UIスレッドでオーバーレイウィンドウを作成
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _logger.LogDebug("新規インプレースオーバーレイ作成開始 - ChunkId: {ChunkId}", textChunk.ChunkId);

                newOverlay = new InPlaceTranslationOverlayWindow
                {
                    ChunkId = textChunk.ChunkId,
                    OriginalText = textChunk.CombinedText,
                    TranslatedText = textChunk.TranslatedText,
                    TargetBounds = textChunk.CombinedBounds,
                    SourceWindowHandle = textChunk.SourceWindowHandle
                };

                _logger.LogDebug("新規インプレースオーバーレイ作成完了 - ChunkId: {ChunkId}", textChunk.ChunkId);
            }, DispatcherPriority.Normal, cancellationToken);

            if (newOverlay == null)
            {
                throw new InvalidOperationException("インプレースオーバーレイウィンドウの作成に失敗しました");
            }

            // キャンセレーションチェック
            cancellationToken.ThrowIfCancellationRequested();

            // 座標変換とDPI補正
            System.Drawing.Point optimalPosition;
            System.Drawing.Point avaloniaCompensatedPosition;
            MonitorInfo actualMonitor;

            try
            {
                var overlaySize = textChunk.GetOverlaySize();
                var options = new OverlayPositioningOptions(); // デフォルト設定を使用

                _logger.LogDebug("座標変換開始 - ChunkId: {ChunkId}, 入力ROI: ({X},{Y}) サイズ: {W}x{H}",
                    textChunk.ChunkId, textChunk.CombinedBounds.X, textChunk.CombinedBounds.Y,
                    textChunk.CombinedBounds.Width, textChunk.CombinedBounds.Height);

                // モニター情報取得
                var monitorResult = _monitorManager.DetermineOptimalMonitor(textChunk.SourceWindowHandle);
#pragma warning disable CS8073 // MonitorInfoは値型だがnullチェックは後方互換性のため保持
                if (monitorResult == null)
                {
                    throw new InvalidOperationException($"モニター情報取得失敗 - ChunkId: {textChunk.ChunkId}");
                }
#pragma warning restore CS8073
                actualMonitor = monitorResult;

                _logger.LogDebug("対象モニター: {MonitorName}, 境界: ({X},{Y}) サイズ: {W}x{H}",
                    actualMonitor.Name, actualMonitor.Bounds.X, actualMonitor.Bounds.Y,
                    actualMonitor.Bounds.Width, actualMonitor.Bounds.Height);

                // 座標変換実行
                var result = _overlayPositioningService.CalculateOptimalPosition(
                    textChunk, overlaySize, actualMonitor, existingBounds, options);

                optimalPosition = result.Position;

                _logger.LogDebug("座標変換完了 - ChunkId: {ChunkId}, 最終座標: ({X},{Y}), 使用戦略: {Strategy}",
                    textChunk.ChunkId, optimalPosition.X, optimalPosition.Y, result.UsedStrategy);

                // 座標妥当性検証
                var geometryPoint = new Baketa.Core.UI.Geometry.Point(optimalPosition.X, optimalPosition.Y);
                var isInMonitorBounds = actualMonitor.Bounds.Contains(geometryPoint);

                if (!isInMonitorBounds)
                {
                    _logger.LogWarning("座標がモニター境界外 - 座標: ({X},{Y}), モニター境界: {Bounds}",
                        optimalPosition.X, optimalPosition.Y, actualMonitor.Bounds);
                }

                // Avalonia DPI補正適用
                var monitorType = _advancedMonitorService.DetectMonitorType(actualMonitor);
                var advancedDpiInfo = _advancedMonitorService.GetAdvancedDpiInfo(actualMonitor);

                avaloniaCompensatedPosition = _advancedMonitorService.CompensateCoordinatesForAvalonia(
                    optimalPosition, advancedDpiInfo);

                _logger.LogDebug("Avalonia DPI補正完了 - ChunkId: {ChunkId}, MonitorType: {MonitorType}, " +
                    "Before: ({BeforeX},{BeforeY}) → After: ({AfterX},{AfterY}), Factor: {Factor}",
                    textChunk.ChunkId, monitorType, optimalPosition.X, optimalPosition.Y,
                    avaloniaCompensatedPosition.X, avaloniaCompensatedPosition.Y, advancedDpiInfo.CompensationFactor);
            }
            catch (Exception ex)
            {
                // Phase 4.1: フォールバック処理 - ユーザー体験を損なわないため例外をスローせず基本位置を使用
                // 精密位置計算またはDPI補正失敗時でもオーバーレイ表示を継続することを優先
                optimalPosition = textChunk.GetBasicOverlayPosition();
                avaloniaCompensatedPosition = optimalPosition;
                _logger.LogWarning(ex, "精密位置計算またはDPI補正失敗、基本位置フォールバック使用 - ChunkId: {ChunkId}", textChunk.ChunkId);
            }

            // 調整済みTextChunkを作成
            var adjustedTextChunk = CreateAdjustedTextChunk(textChunk, optimalPosition);

            _logger.LogDebug("オーバーレイ表示開始 - ChunkId: {ChunkId}, 調整後座標: ({X},{Y}) サイズ: {W}x{H}",
                textChunk.ChunkId, adjustedTextChunk.CombinedBounds.X, adjustedTextChunk.CombinedBounds.Y,
                adjustedTextChunk.CombinedBounds.Width, adjustedTextChunk.CombinedBounds.Height);

            // UIスレッドでオーバーレイ表示を実行
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await newOverlay.ShowInPlaceOverlayAsync(adjustedTextChunk, cancellationToken).ConfigureAwait(false);
            }, DispatcherPriority.Normal, cancellationToken);

            // 表示完了後のウィンドウ状態取得
            var isVisible = await Dispatcher.UIThread.InvokeAsync(() => newOverlay.IsVisible,
                DispatcherPriority.Normal, cancellationToken);

            _logger.LogInformation("新規オーバーレイ表示完了 - ChunkId: {ChunkId}, FinalPosition: ({X},{Y}), Visible: {IsVisible}",
                textChunk.ChunkId, optimalPosition.X, optimalPosition.Y, isVisible);

            return newOverlay;
        }
        catch (Exception ex)
        {
            // エラー時のクリーンアップ
            if (newOverlay != null)
            {
                try
                {
                    newOverlay.Dispose();
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogError(cleanupEx, "インプレースオーバーレイクリーンアップエラー - ChunkId: {ChunkId}", textChunk.ChunkId);
                }
            }

            _logger.LogError(ex, "新規インプレースオーバーレイ作成エラー - ChunkId: {ChunkId}", textChunk.ChunkId);
            throw;
        }
    }

    /// <summary>
    /// 精密位置調整で調整されたTextChunkを作成
    /// 元のTextChunkのプロパティを維持しつつ、表示位置のみを調整
    /// </summary>
    private static TextChunk CreateAdjustedTextChunk(TextChunk originalChunk, System.Drawing.Point adjustedPosition)
    {
        // 元の境界サイズを維持しつつ、位置のみを調整
        var adjustedBounds = new Rectangle(adjustedPosition.X, adjustedPosition.Y,
            originalChunk.CombinedBounds.Width, originalChunk.CombinedBounds.Height);

        // 調整済みTextChunkを作成（元のプロパティを全て継承）
        return new TextChunk
        {
            ChunkId = originalChunk.ChunkId,
            TextResults = originalChunk.TextResults,
            CombinedBounds = adjustedBounds, // 調整済み位置
            CombinedText = originalChunk.CombinedText,
            TranslatedText = originalChunk.TranslatedText,
            SourceWindowHandle = originalChunk.SourceWindowHandle,
            DetectedLanguage = originalChunk.DetectedLanguage,
            CreatedAt = originalChunk.CreatedAt
        };
    }
}
