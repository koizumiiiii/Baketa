using System;
using System.Drawing;

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// 座標変換サービスのインターフェース
/// ROI座標からスクリーン座標への変換を担当
/// UltraThink P0: 座標変換問題修正 - DPIスケーリング・ウィンドウオフセット計算
/// </summary>
public interface ICoordinateTransformationService
{
    /// <summary>
    /// ROI座標をスクリーン座標に変換
    /// coordinate_test/Program.csの実装に基づく正確な変換
    /// </summary>
    /// <param name="roiBounds">ROI座標系での境界</param>
    /// <param name="windowHandle">対象ウィンドウハンドル</param>
    /// <param name="roiScaleFactor">ROIスケールファクタ（デフォルト: 1.0f）</param>
    /// <returns>スクリーン座標系での境界</returns>
    Rectangle ConvertRoiToScreenCoordinates(Rectangle roiBounds, IntPtr windowHandle, float roiScaleFactor = 1.0f);

    /// <summary>
    /// 複数のROI座標を一括変換
    /// 効率化のため、ウィンドウオフセットを一度だけ取得
    /// </summary>
    /// <param name="roiBounds">ROI座標系での境界のリスト</param>
    /// <param name="windowHandle">対象ウィンドウハンドル</param>
    /// <param name="roiScaleFactor">ROIスケールファクタ（デフォルト: 1.0f）</param>
    /// <returns>スクリーン座標系での境界のリスト</returns>
    Rectangle[] ConvertRoiToScreenCoordinatesBatch(Rectangle[] roiBounds, IntPtr windowHandle, float roiScaleFactor = 1.0f);

    /// <summary>
    /// ウィンドウオフセットを取得
    /// Win32 API GetWindowRectを使用した正確なオフセット計算
    /// </summary>
    /// <param name="windowHandle">対象ウィンドウハンドル</param>
    /// <returns>ウィンドウの左上座標オフセット</returns>
    Point GetWindowOffset(IntPtr windowHandle);
}