using System.Drawing;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Models.OCR;

/// <summary>
/// OCR処理に必要なコンテキスト情報を一元管理するレコード
/// FIX7 Option C改良版: Gemini専門家推奨設計（⭐⭐⭐⭐⭐評価）
/// </summary>
/// <remarks>
/// 利点:
/// 1. 将来の引数追加時にメソッドシグネチャ変更不要
/// 2. 引数のバケツリレー問題を緩和
/// 3. 関連情報が一体となり、保守性向上
/// 4. Option Bへの移行がスムーズ
///
/// Geminiレビュー結果:
/// - 技術的妥当性: ⭐⭐⭐⭐⭐
/// - 拡張性: ⭐⭐⭐⭐⭐
/// - 保守性: ⭐⭐⭐⭐⭐
/// </remarks>
public sealed record OcrContext(
    IAdvancedImage Image,
    IntPtr WindowHandle,
    Rectangle? CaptureRegion,
    CancellationToken CancellationToken = default
)
{
    /// <summary>
    /// デフォルトコンストラクタ（CancellationToken省略可能）
    /// </summary>
    public OcrContext(
        IAdvancedImage image,
        IntPtr windowHandle,
        Rectangle? captureRegion)
        : this(image, windowHandle, captureRegion, CancellationToken.None)
    {
    }

    /// <summary>
    /// CaptureRegionが設定されているかどうか
    /// </summary>
    public bool HasCaptureRegion => CaptureRegion.HasValue && CaptureRegion.Value != Rectangle.Empty;

    /// <summary>
    /// ROIキャプチャかどうか（CaptureRegionが有効な場合true）
    /// </summary>
    public bool IsRoiCapture => HasCaptureRegion;
}
