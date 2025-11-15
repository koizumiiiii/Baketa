using System;
using System.Collections.Generic;
using System.Drawing;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.UI.Monitors;

namespace Baketa.Core.Abstractions.UI;

/// <summary>
/// オーバーレイ位置調整サービスのインターフェース
/// UltraThink Phase 10.3: Clean Architecture準拠の責務分離
/// TextChunkからUI位置計算責務を分離
/// </summary>
public interface IOverlayPositioningService
{
    /// <summary>
    /// 精密オーバーレイ位置調整（DPI/マルチモニター対応）
    /// Phase 2対応の8段階精密位置調整システム
    /// </summary>
    /// <param name="textChunk">対象テキストチャンク</param>
    /// <param name="overlaySize">オーバーレイサイズ（論理ピクセル）</param>
    /// <param name="targetMonitor">対象モニター情報</param>
    /// <param name="existingOverlayBounds">既存オーバーレイ境界（物理ピクセル）</param>
    /// <param name="options">位置調整オプション</param>
    /// <returns>位置調整結果</returns>
    PositioningResult CalculateOptimalPosition(
        TextChunk textChunk,
        Size overlaySize,
        MonitorInfo targetMonitor,
        IReadOnlyList<Rectangle>? existingOverlayBounds = null,
        OverlayPositioningOptions? options = null);

    /// <summary>
    /// 基本位置調整（従来互換）
    /// 既存コードとの互換性維持用
    /// </summary>
    /// <param name="textChunk">対象テキストチャンク</param>
    /// <param name="overlaySize">オーバーレイサイズ</param>
    /// <param name="screenBounds">画面境界</param>
    /// <returns>最適位置</returns>
    Point CalculateBasicPosition(
        TextChunk textChunk,
        Size overlaySize,
        Rectangle screenBounds);

    /// <summary>
    /// 衝突回避付き位置調整（従来互換）
    /// 既存コードとの互換性維持用
    /// </summary>
    /// <param name="textChunk">対象テキストチャンク</param>
    /// <param name="overlaySize">オーバーレイサイズ</param>
    /// <param name="screenBounds">画面境界</param>
    /// <param name="existingOverlayBounds">既存オーバーレイ境界</param>
    /// <returns>衝突回避位置</returns>
    Point CalculatePositionWithCollisionAvoidance(
        TextChunk textChunk,
        Size overlaySize,
        Rectangle screenBounds,
        IReadOnlyList<Rectangle> existingOverlayBounds);
}
