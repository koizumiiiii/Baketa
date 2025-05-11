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
        /// MSER領域を検出します
        /// </summary>
        /// <param name="image">検出対象の画像</param>
        /// <param name="parameters">検出パラメータ</param>
        /// <returns>検出された領域のポイント配列のリスト</returns>
        IReadOnlyList<Point[]> DetectMSERRegions(IAdvancedImage image, Dictionary<string, object> parameters);
        
        /// <summary>
        /// ポイント配列に基づくバウンディングボックスを取得します
        /// </summary>
        /// <param name="points">ポイント配列</param>
        /// <returns>バウンディングボックス</returns>
        Rectangle GetBoundingRect(Point[] points);
        
        /// <summary>
        /// Cannyエッジ検出を実行します
        /// </summary>
        /// <param name="image">元画像</param>
        /// <param name="threshold1">下側閾値</param>
        /// <param name="threshold2">上側閾値</param>
        /// <returns>エッジ検出結果画像</returns>
        IAdvancedImage CannyEdgeDetection(IAdvancedImage image, int threshold1, int threshold2);
        
        /// <summary>
        /// ストローク幅変換を適用します
        /// </summary>
        /// <param name="grayImage">グレースケール画像</param>
        /// <param name="edgeImage">エッジ検出結果画像</param>
        /// <param name="minStrokeWidth">最小ストローク幅</param>
        /// <param name="maxStrokeWidth">最大ストローク幅</param>
        /// <returns>ストローク幅変換結果画像</returns>
        IAdvancedImage StrokeWidthTransform(IAdvancedImage grayImage, IAdvancedImage edgeImage, float minStrokeWidth, float maxStrokeWidth);
        
        /// <summary>
        /// 連結成分を抽出します
        /// </summary>
        /// <param name="image">元画像</param>
        /// <param name="minComponentSize">最小成分サイズ</param>
        /// <param name="maxComponentSize">最大成分サイズ</param>
        /// <returns>抽出された連結成分のリスト</returns>
        IReadOnlyList<Point[]> ExtractConnectedComponents(IAdvancedImage image, int minComponentSize, int maxComponentSize);
        
        /// <summary>
        /// ストローク幅の分散を計算します
        /// </summary>
        /// <param name="swtImage">ストローク幅変換画像</param>
        /// <param name="region">領域のポイント配列</param>
        /// <returns>ストローク幅の分散</returns>
        float CalculateStrokeWidthVariance(IAdvancedImage swtImage, Point[] region);
        
        /// <summary>
        /// 平均ストローク幅を計算します
        /// </summary>
        /// <param name="swtImage">ストローク幅変換画像</param>
        /// <param name="region">領域のポイント配列</param>
        /// <returns>平均ストローク幅</returns>
        float CalculateMeanStrokeWidth(IAdvancedImage swtImage, Point[] region);
    }
}