namespace Baketa.Core.Abstractions.OCR.TextDetection;

    /// <summary>
    /// テキスト検出方法を表す列挙型
    /// </summary>
    public enum TextDetectionMethod
    {
        /// <summary>
        /// 未知の方法
        /// </summary>
        Unknown = 0,
        
        /// <summary>
        /// MSER (Maximally Stable Extremal Regions) を使用
        /// </summary>
        Mser = 1,
        
        /// <summary>
        /// エッジベースの検出
        /// </summary>
        EdgeBased = 2,
        
        /// <summary>
        /// SWT (Stroke Width Transform) を使用
        /// </summary>
        Swt = 3,
        
        /// <summary>
        /// 連結成分分析を使用
        /// </summary>
        ConnectedComponents = 4,
        
        /// <summary>
        /// 複合手法
        /// </summary>
        Combined = 5
    }
