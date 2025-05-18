using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Infrastructure.Platform.Windows.OpenCv;

    /// <summary>
    /// Windows環境でのOpenCVライブラリの機能を提供するインターフェース
    /// </summary>
    public interface IWindowsOpenCvLibrary
    {
        /// <summary>
        /// 画像に閾値処理を適用します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="threshold">閾値</param>
        /// <param name="maxValue">最大値</param>
        /// <param name="thresholdType">閾値処理タイプ</param>
        /// <returns>閾値処理された画像</returns>
        Task<IAdvancedImage> ThresholdAsync(IAdvancedImage source, double threshold, double maxValue, int thresholdType);
        
        /// <summary>
        /// 画像に適応的閾値処理を適用します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="maxValue">最大値</param>
        /// <param name="adaptiveMethod">適応的閾値処理タイプ</param>
        /// <param name="thresholdType">閾値処理タイプ</param>
        /// <param name="blockSize">ブロックサイズ</param>
        /// <param name="c">定数</param>
        /// <returns>閾値処理された画像</returns>
        Task<IAdvancedImage> AdaptiveThresholdAsync(IAdvancedImage source, double maxValue, int adaptiveMethod, int thresholdType, int blockSize, double c);
        
        /// <summary>
        /// 画像にモルフォロジー演算を適用します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="morphType">モルフォロジー演算タイプ</param>
        /// <param name="kernelSize">カーネルサイズ</param>
        /// <returns>モルフォロジー演算結果画像</returns>
        Task<IAdvancedImage> MorphologyAsync(IAdvancedImage source, int morphType, int kernelSize);
        
        /// <summary>
        /// 連結コンポーネントを抽出します
        /// </summary>
        /// <param name="image">元画像</param>
        /// <param name="minComponentSize">最小コンポーネントサイズ</param>
        /// <param name="maxComponentSize">最大コンポーネントサイズ</param>
        /// <returns>連結コンポーネントのリスト</returns>
        IReadOnlyList<Point[]> FindConnectedComponents(IAdvancedImage image, int minComponentSize, int maxComponentSize);
        
        /// <summary>
        /// MSERアルゴリズムを使用して領域を検出します
        /// </summary>
        /// <param name="image">元画像</param>
        /// <param name="delta">デルタパラメータ</param>
        /// <param name="minArea">最小領域サイズ</param>
        /// <param name="maxArea">最大領域サイズ</param>
        /// <returns>検出された領域のリスト</returns>
        IReadOnlyList<DetectedRegion> DetectMserRegions(IAdvancedImage image, int delta, int minArea, int maxArea);
        
        /// <summary>
        /// SWTアルゴリズムを使用して領域を検出します
        /// </summary>
        /// <param name="image">元画像</param>
        /// <param name="darkTextOnLight">暗い背景上の明るいテキストかどうか</param>
        /// <param name="strokeWidthRatio">ストローク幅比率</param>
        /// <returns>検出された領域のリスト</returns>
        IReadOnlyList<DetectedRegion> DetectSwtRegions(IAdvancedImage image, bool darkTextOnLight, float strokeWidthRatio);
    }
    
    /// <summary>
    /// 検出された領域の情報を格納するクラス
    /// </summary>
    public class DetectedRegion
    {
        /// <summary>
        /// 領域の境界矩形
        /// </summary>
        public Rectangle Bounds { get; set; }
        
        /// <summary>
        /// 領域を構成するポイント配列
        /// </summary>
        public Point[] Points { get; set; } = Array.Empty<Point>();
        
        /// <summary>
        /// 検出信頼度
        /// </summary>
        public float Confidence { get; set; }
        
        /// <summary>
        /// ストローク幅（SWT領域用）
        /// </summary>
        public float StrokeWidth { get; set; }
    }
