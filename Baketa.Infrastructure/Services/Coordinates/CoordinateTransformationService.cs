using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Baketa.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services.Coordinates;

/// <summary>
/// 座標変換サービス実装
/// coordinate_test/Program.csの変換ロジックに基づく正確なROI→スクリーン座標変換
/// UltraThink P0: 座標変換問題修正 - DPIスケーリング・ウィンドウオフセット計算
/// </summary>
public sealed class CoordinateTransformationService : ICoordinateTransformationService
{
    private readonly ILogger<CoordinateTransformationService> _logger;

    // Win32 API declarations
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out WindowRect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public CoordinateTransformationService(ILogger<CoordinateTransformationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// ROI座標をスクリーン座標に変換
    /// coordinate_test/Program.csのConvertRoiToScreenCoordinatesと同じロジック
    /// </summary>
    public Rectangle ConvertRoiToScreenCoordinates(Rectangle roiBounds, IntPtr windowHandle, float roiScaleFactor = 1.0f)
    {
        try
        {
            _logger.LogDebug("🎯 [P0_COORDINATE_TRANSFORM] ROI→スクリーン座標変換開始: ROI=({X},{Y},{W},{H}), Handle={Handle}, ScaleFactor={ScaleFactor}",
                roiBounds.X, roiBounds.Y, roiBounds.Width, roiBounds.Height, windowHandle, roiScaleFactor);

            // ROIスケールファクタの逆数でスケーリング
            var inverseScale = 1.0f / roiScaleFactor;

            // 1. ROI座標を実際の画面座標にスケーリング
            var scaledBounds = new Rectangle(
                (int)(roiBounds.X * inverseScale),
                (int)(roiBounds.Y * inverseScale),
                (int)(roiBounds.Width * inverseScale),
                (int)(roiBounds.Height * inverseScale)
            );

            // 2. ターゲットウィンドウのオフセットを取得
            var windowOffset = GetWindowOffset(windowHandle);

            // 3. 最終的な画面座標を計算
            var finalBounds = new Rectangle(
                scaledBounds.X + windowOffset.X,
                scaledBounds.Y + windowOffset.Y,
                scaledBounds.Width,
                scaledBounds.Height
            );

            _logger.LogInformation("🎯 [P0_COORDINATE_TRANSFORM] 座標変換完了: ROI=({RoiX},{RoiY}) → Scaled=({ScaledX},{ScaledY}) + Offset=({OffsetX},{OffsetY}) → Final=({FinalX},{FinalY})",
                roiBounds.X, roiBounds.Y, scaledBounds.X, scaledBounds.Y, windowOffset.X, windowOffset.Y, finalBounds.X, finalBounds.Y);

            return finalBounds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [P0_COORDINATE_TRANSFORM] 座標変換エラー: ROI=({X},{Y},{W},{H}), Handle={Handle}",
                roiBounds.X, roiBounds.Y, roiBounds.Width, roiBounds.Height, windowHandle);

            // フォールバック: 元の座標をそのまま返す
            return roiBounds;
        }
    }

    /// <summary>
    /// 複数のROI座標を一括変換
    /// 効率化のため、ウィンドウオフセットを一度だけ取得
    /// </summary>
    public Rectangle[] ConvertRoiToScreenCoordinatesBatch(Rectangle[] roiBounds, IntPtr windowHandle, float roiScaleFactor = 1.0f)
    {
        if (roiBounds == null || roiBounds.Length == 0)
            return [];

        try
        {
            _logger.LogDebug("🎯 [P0_COORDINATE_TRANSFORM] 一括座標変換開始: Count={Count}, Handle={Handle}, ScaleFactor={ScaleFactor}",
                roiBounds.Length, windowHandle, roiScaleFactor);

            var inverseScale = 1.0f / roiScaleFactor;
            var windowOffset = GetWindowOffset(windowHandle);

            var results = new Rectangle[roiBounds.Length];

            for (int i = 0; i < roiBounds.Length; i++)
            {
                var roi = roiBounds[i];

                // ROI座標をスケーリング
                var scaledBounds = new Rectangle(
                    (int)(roi.X * inverseScale),
                    (int)(roi.Y * inverseScale),
                    (int)(roi.Width * inverseScale),
                    (int)(roi.Height * inverseScale)
                );

                // ウィンドウオフセットを追加
                results[i] = new Rectangle(
                    scaledBounds.X + windowOffset.X,
                    scaledBounds.Y + windowOffset.Y,
                    scaledBounds.Width,
                    scaledBounds.Height
                );
            }

            _logger.LogDebug("🎯 [P0_COORDINATE_TRANSFORM] 一括座標変換完了: Count={Count}, WindowOffset=({X},{Y})",
                results.Length, windowOffset.X, windowOffset.Y);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [P0_COORDINATE_TRANSFORM] 一括座標変換エラー: Count={Count}, Handle={Handle}",
                roiBounds.Length, windowHandle);

            // フォールバック: 元の座標をそのまま返す
            return roiBounds;
        }
    }

    /// <summary>
    /// ウィンドウオフセットを取得
    /// coordinate_test/Program.csのGetTargetWindowOffsetと同じロジック
    /// </summary>
    public Point GetWindowOffset(IntPtr windowHandle)
    {
        try
        {
            if (windowHandle == IntPtr.Zero)
            {
                _logger.LogDebug("⚠️ [P0_COORDINATE_TRANSFORM] ウィンドウハンドルが無効、(0,0)を使用");
                return Point.Empty;
            }

            // ウィンドウの矩形情報を取得
            if (GetWindowRect(windowHandle, out var rect))
            {
                var offset = new Point(rect.Left, rect.Top);
                _logger.LogDebug("🎯 [P0_COORDINATE_TRANSFORM] ウィンドウオフセット取得成功: Handle={Handle}, Offset=({X},{Y})",
                    windowHandle, offset.X, offset.Y);
                return offset;
            }

            _logger.LogWarning("⚠️ [P0_COORDINATE_TRANSFORM] GetWindowRect失敗、(0,0)を使用: Handle={Handle}", windowHandle);
            return Point.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [P0_COORDINATE_TRANSFORM] ウィンドウオフセット取得エラー: Handle={Handle}", windowHandle);
            return Point.Empty;
        }
    }
}