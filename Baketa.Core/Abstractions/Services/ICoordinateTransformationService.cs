using System;
using System.Drawing;

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// 座標変換サービスのインターフェース
/// ROI座標からスクリーン座標への変換を担当
/// UltraThink P0: 座標変換問題修正 - DPIスケーリング・ウィンドウオフセット計算
/// Phase 2.1: ボーダーレス/フルスクリーン検出機能追加
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
    /// <param name="isBorderlessOrFullscreen">ボーダーレス/フルスクリーンウィンドウかどうか（デフォルト: false）</param>
    /// <returns>スクリーン座標系での境界</returns>
    Rectangle ConvertRoiToScreenCoordinates(
        Rectangle roiBounds,
        IntPtr windowHandle,
        float roiScaleFactor = 1.0f,
        bool isBorderlessOrFullscreen = false);

    /// <summary>
    /// 複数のROI座標を一括変換
    /// 効率化のため、ウィンドウオフセットを一度だけ取得
    /// </summary>
    /// <param name="roiBounds">ROI座標系での境界のリスト</param>
    /// <param name="windowHandle">対象ウィンドウハンドル</param>
    /// <param name="roiScaleFactor">ROIスケールファクタ（デフォルト: 1.0f）</param>
    /// <param name="isBorderlessOrFullscreen">ボーダーレス/フルスクリーンウィンドウかどうか（デフォルト: false）</param>
    /// <returns>スクリーン座標系での境界のリスト</returns>
    Rectangle[] ConvertRoiToScreenCoordinatesBatch(
        Rectangle[] roiBounds,
        IntPtr windowHandle,
        float roiScaleFactor = 1.0f,
        bool isBorderlessOrFullscreen = false);

    /// <summary>
    /// ウィンドウオフセットを取得
    /// Win32 API GetWindowRectを使用した正確なオフセット計算
    /// </summary>
    /// <param name="windowHandle">対象ウィンドウハンドル</param>
    /// <returns>ウィンドウの左上座標オフセット</returns>
    Point GetWindowOffset(IntPtr windowHandle);

    /// <summary>
    /// 🔥 [PHASE2.1] ボーダーレス/フルスクリーンウィンドウ検出
    /// DWM Hybrid方式: DwmGetWindowAttribute（主）+ GetWindowLong（フォールバック）
    ///
    /// 検出対象:
    /// - ボーダーレスウィンドウ（タイトルバーなし、画面全体）
    /// - 非排他的フルスクリーン（Window-basedフルスクリーン）
    ///
    /// 検出方法:
    /// 1. DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)でウィンドウサイズ取得
    /// 2. モニタサイズ（rcMonitor）と比較（±10pxまたは95%相対マッチング）
    /// 3. フォールバック: GetWindowLong(GWL_STYLE)でWS_CAPTION等のビットチェック
    /// </summary>
    /// <param name="windowHandle">検出対象ウィンドウハンドル</param>
    /// <returns>ボーダーレス/フルスクリーンの場合true、それ以外false</returns>
    bool DetectBorderlessOrFullscreen(IntPtr windowHandle);
}