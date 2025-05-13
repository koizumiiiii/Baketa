using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Abstractions.Capture
{
    /// <summary>
    /// 差分検出アルゴリズムインターフェース
    /// </summary>
    public interface IDetectionAlgorithm
    {
        /// <summary>
        /// アルゴリズムの種類
        /// </summary>
        DifferenceDetectionAlgorithm AlgorithmType { get; }
        
        /// <summary>
        /// 差分を検出します
        /// </summary>
        /// <param name="previousImage">前回の画像</param>
        /// <param name="currentImage">現在の画像</param>
        /// <param name="settings">検出設定</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>検出結果</returns>
        Task<DetectionResult> DetectAsync(
            IImage previousImage, 
            IImage currentImage, 
            DifferenceDetectionSettings settings, 
            CancellationToken cancellationToken = default);
    }
    
    /// <summary>
    /// 検出結果クラス
    /// </summary>
    public class DetectionResult
    {
        /// <summary>
        /// 有意な変化があるかどうか
        /// </summary>
        public bool HasSignificantChange { get; set; }
        
        /// <summary>
        /// 変化が検出された領域
        /// </summary>
        public IReadOnlyList<Rectangle> ChangedRegions { get; set; } = new List<Rectangle>();
        
        /// <summary>
        /// 変化の比率 (0.0～1.0)
        /// </summary>
        public double ChangeRatio { get; set; }
        
        /// <summary>
        /// 消失したテキスト領域
        /// </summary>
        public IReadOnlyList<Rectangle> DisappearedTextRegions { get; set; } = new List<Rectangle>();
    }
}