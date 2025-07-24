using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Models.Capture;
using System.Drawing;

namespace Baketa.Core.Abstractions.Capture;

/// <summary>
/// テキスト領域検出器のインターフェース
/// </summary>
public interface ITextRegionDetector
{
    /// <summary>
    /// 画像からテキスト領域を検出
    /// </summary>
    Task<IList<Rectangle>> DetectTextRegionsAsync(IWindowsImage image);
    
    /// <summary>
    /// 検出パラメータを調整
    /// </summary>
    void ConfigureDetection(TextDetectionConfig config);
    
    /// <summary>
    /// 現在の検出設定を取得
    /// </summary>
    TextDetectionConfig GetCurrentConfig();
    
    /// <summary>
    /// 検出精度を向上させるためのプレビューモード
    /// </summary>
    Task<IList<Rectangle>> DetectWithPreviewAsync(IWindowsImage image, bool showDebugInfo = false);
}