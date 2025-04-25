using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Abstractions.OCR
{
    /// <summary>
    /// OpenCV機能へのアクセスを提供するインターフェース
    /// </summary>
    public interface IOpenCvWrapper : IDisposable
    {
        /// <summary>
        /// 画像をグレースケールに変換します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <returns>グレースケール変換された画像</returns>
        Task<IAdvancedImage> ConvertToGrayscaleAsync(IAdvancedImage source);
        
        /// <summary>
        /// 画像に閾値処理を適用します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="threshold">閾値</param>
        /// <param name="maxValue">最大値</param>
        /// <param name="type">閾値処理タイプ</param>
        /// <returns>閾値処理された画像</returns>
        Task<IAdvancedImage> ApplyThresholdAsync(IAdvancedImage source, double threshold, double maxValue, ThresholdType type);
        
        /// <summary>
        /// 画像に適応的閾値処理を適用します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="maxValue">最大値</param>
        /// <param name="adaptiveType">適応的閾値処理タイプ</param>
        /// <param name="thresholdType">閾値処理タイプ</param>
        /// <param name="blockSize">ブロックサイズ</param>
        /// <param name="c">定数</param>
        /// <returns>閾値処理された画像</returns>
        Task<IAdvancedImage> ApplyAdaptiveThresholdAsync(IAdvancedImage source, double maxValue, AdaptiveThresholdType adaptiveType, ThresholdType thresholdType, int blockSize, double c);
        
        /// <summary>
        /// 画像にガウシアンブラーを適用します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="kernelSize">カーネルサイズ</param>
        /// <param name="sigmaX">X方向のシグマ値</param>
        /// <param name="sigmaY">Y方向のシグマ値（0の場合はsigmaXと同じ値が使用される）</param>
        /// <returns>ブラー処理された画像</returns>
        Task<IAdvancedImage> ApplyGaussianBlurAsync(IAdvancedImage source, Size kernelSize, double sigmaX = 0, double sigmaY = 0);
        
        /// <summary>
        /// 画像にメディアンブラーを適用します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="kernelSize">カーネルサイズ</param>
        /// <returns>ブラー処理された画像</returns>
        Task<IAdvancedImage> ApplyMedianBlurAsync(IAdvancedImage source, int kernelSize);
        
        /// <summary>
        /// 画像に対してCannyエッジ検出を適用します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="threshold1">下側閾値</param>
        /// <param name="threshold2">上側閾値</param>
        /// <param name="apertureSize">アパーチャーサイズ</param>
        /// <param name="l2Gradient">L2勾配を使用するかどうか</param>
        /// <returns>エッジ検出結果画像</returns>
        Task<IAdvancedImage> ApplyCannyEdgeAsync(IAdvancedImage source, double threshold1, double threshold2, int apertureSize = 3, bool l2Gradient = false);
        
        /// <summary>
        /// 画像にモルフォロジー演算を適用します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="morphType">モルフォロジー演算タイプ</param>
        /// <param name="kernelSize">カーネルサイズ</param>
        /// <param name="iterations">反復回数</param>
        /// <returns>モルフォロジー演算結果画像</returns>
        Task<IAdvancedImage> ApplyMorphologyAsync(IAdvancedImage source, MorphType morphType, Size kernelSize, int iterations = 1);
        
        /// <summary>
        /// 画像からテキスト領域の候補となる矩形を検出します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="method">テキスト検出方法</param>
        /// <param name="parameters">テキスト検出パラメータ（nullの場合はデフォルト値が使用される）</param>
        /// <returns>検出された矩形のリスト</returns>
        Task<IReadOnlyList<Rectangle>> DetectTextRegionsAsync(IAdvancedImage source, TextDetectionMethod method, TextDetectionParams parameters = null);
    }
}