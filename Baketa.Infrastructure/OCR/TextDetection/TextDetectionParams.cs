using System;
using DetectionMethod = Baketa.Core.Abstractions.OCR.TextDetectionMethod;

namespace Baketa.Infrastructure.OCR.TextDetection;

    /// <summary>
    /// テキスト検出パラメータのデフォルト値を提供するクラス
    /// </summary>
    public class TextDetectionParams
    {
        /// <summary>
        /// MSERデルタ値
        /// </summary>
        public int MserDelta { get; set; } = 5;
        
        /// <summary>
        /// MSER最小面積
        /// </summary>
        public int MserMinArea { get; set; } = 60;
        
        /// <summary>
        /// MSER最大面積
        /// </summary>
        public int MserMaxArea { get; set; } = 14400;
        
        /// <summary>
        /// 最小横幅
        /// </summary>
        public int MinWidth { get; set; } = 10;
        
        /// <summary>
        /// 最小高さ
        /// </summary>
        public int MinHeight { get; set; } = 10;
        
        /// <summary>
        /// 最小アスペクト比
        /// </summary>
        public float MinAspectRatio { get; set; } = 0.1f;
        
        /// <summary>
        /// 最大アスペクト比
        /// </summary>
        public float MaxAspectRatio { get; set; } = 10.0f;
        
        /// <summary>
        /// 領域マージ閾値
        /// </summary>
        public float MergeThreshold { get; set; } = 0.3f;
        
        /// <summary>
        /// 検出方法に応じたデフォルトパラメータを作成します
        /// </summary>
        /// <param name="method">検出方法</param>
        /// <returns>パラメータオブジェクト</returns>
        public static TextDetectionParams CreateForMethod(DetectionMethod method)
        {
            var parameters = new TextDetectionParams();
            
            switch (method)
            {
                case DetectionMethod.Mser:
                    // MSER用のデフォルト値
                    parameters.MserDelta = 5;
                    parameters.MserMinArea = 60;
                    parameters.MserMaxArea = 14400;
                    parameters.MinWidth = 10;
                    parameters.MinHeight = 10;
                    parameters.MinAspectRatio = 0.1f;
                    parameters.MaxAspectRatio = 10.0f;
                    parameters.MergeThreshold = 0.3f;
                    break;
                    
                case DetectionMethod.Swt:
                    // SWT用のデフォルト値
                    parameters.MinWidth = 8;
                    parameters.MinHeight = 8;
                    parameters.MinAspectRatio = 0.1f;
                    parameters.MaxAspectRatio = 10.0f;
                    parameters.MergeThreshold = 0.5f;
                    break;
                    
                case DetectionMethod.EdgeBased:
                    // エッジベース検出用のデフォルト値
                    parameters.MinWidth = 15;
                    parameters.MinHeight = 15;
                    parameters.MinAspectRatio = 0.2f;
                    parameters.MaxAspectRatio = 8.0f;
                    parameters.MergeThreshold = 0.4f;
                    break;
                    
                case DetectionMethod.ConnectedComponents:
                    // 連結成分分析用のデフォルト値
                    parameters.MinWidth = 5;
                    parameters.MinHeight = 5;
                    parameters.MinAspectRatio = 0.05f;
                    parameters.MaxAspectRatio = 15.0f;
                    parameters.MergeThreshold = 0.2f;
                    break;
                    
                case DetectionMethod.Combined:
                    // 複合手法用のデフォルト値
                    parameters.MinWidth = 10;
                    parameters.MinHeight = 10;
                    parameters.MinAspectRatio = 0.1f;
                    parameters.MaxAspectRatio = 10.0f;
                    parameters.MergeThreshold = 0.3f;
                    break;
                    
                default:
                    // 不明な方法の場合は汎用値
                    parameters.MinWidth = 10;
                    parameters.MinHeight = 10;
                    parameters.MinAspectRatio = 0.1f;
                    parameters.MaxAspectRatio = 10.0f;
                    parameters.MergeThreshold = 0.3f;
                    break;
            }
            
            return parameters;
        }
    }
