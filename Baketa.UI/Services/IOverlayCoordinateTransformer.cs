using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.UI.Monitors;

namespace Baketa.UI.Services;

/// <summary>
/// オーバーレイ座標変換とDPI補正を担当するサービス
/// Phase 4.1: 座標変換、DPI補正、座標妥当性検証の統一化
/// </summary>
public interface IOverlayCoordinateTransformer
{
    /// <summary>
    /// 座標変換とDPI補正を実行して最適な表示位置を計算します
    /// </summary>
    /// <param name="textChunk">翻訳結果を含むテキストチャンク</param>
    /// <param name="existingBounds">衝突回避のための既存オーバーレイ境界情報</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>座標変換結果（最適位置、DPI補正済み位置、モニター情報）</returns>
    Task<CoordinateTransformResult> TransformCoordinatesAsync(
        TextChunk textChunk,
        List<Rectangle> existingBounds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 座標変換結果
/// </summary>
public record CoordinateTransformResult
{
    /// <summary>
    /// IOverlayPositioningServiceで計算された最適位置
    /// </summary>
    public required System.Drawing.Point OptimalPosition { get; init; }

    /// <summary>
    /// Avalonia DPI補正適用後の位置
    /// </summary>
    public required System.Drawing.Point AvaloniaCompensatedPosition { get; init; }

    /// <summary>
    /// 対象モニター情報
    /// </summary>
    public required MonitorInfo Monitor { get; init; }

    /// <summary>
    /// 使用された位置決定戦略
    /// </summary>
    public required string UsedStrategy { get; init; }
}
