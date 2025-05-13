using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Abstractions.Capture
{
    /// <summary>
    /// 画像間の差分を検出するインターフェース
    /// </summary>
    public interface IDifferenceDetector
    {
        /// <summary>
        /// 二つの画像間に有意な差分があるかを検出します
        /// </summary>
        /// <param name="previousImage">前回の画像</param>
        /// <param name="currentImage">現在の画像</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>有意な差分がある場合はtrue</returns>
        Task<bool> HasSignificantChangeAsync(IImage previousImage, IImage currentImage, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 二つの画像間の差分領域を検出します
        /// </summary>
        /// <param name="previousImage">前回の画像</param>
        /// <param name="currentImage">現在の画像</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>差分が検出された領域のリスト</returns>
        Task<IReadOnlyList<Rectangle>> DetectChangedRegionsAsync(IImage previousImage, IImage currentImage, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 差分検出の閾値を設定します
        /// </summary>
        /// <param name="threshold">閾値（0.0～1.0）</param>
        void SetThreshold(double threshold);
        
        /// <summary>
        /// 現在の差分検出設定を取得します
        /// </summary>
        /// <returns>設定情報</returns>
        DifferenceDetectionSettings GetSettings();
        
        /// <summary>
        /// 差分検出設定を適用します
        /// </summary>
        /// <param name="settings">設定情報</param>
        void ApplySettings(DifferenceDetectionSettings settings);
        
        /// <summary>
        /// 前回検出されたテキスト領域を設定します
        /// </summary>
        /// <param name="textRegions">テキスト領域のリスト</param>
        void SetPreviousTextRegions(IReadOnlyList<Rectangle> textRegions);
        
        /// <summary>
        /// テキスト消失を検出します
        /// </summary>
        /// <param name="previousImage">前回の画像</param>
        /// <param name="currentImage">現在の画像</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>消失したテキスト領域のリスト</returns>
        Task<IReadOnlyList<Rectangle>> DetectTextDisappearanceAsync(IImage previousImage, IImage currentImage, CancellationToken cancellationToken = default);
    }
}