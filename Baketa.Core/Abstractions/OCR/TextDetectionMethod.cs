namespace Baketa.Core.Abstractions.OCR
{
    /// <summary>
    /// テキスト検出方法
    /// </summary>
    public enum TextDetectionMethod
    {
        /// <summary>
        /// MSER (Maximally Stable Extremal Regions) アルゴリズムを使用
        /// </summary>
        Mser,
        
        /// <summary>
        /// 連結成分分析を使用
        /// </summary>
        ConnectedComponents,
        
        /// <summary>
        /// 輪郭ベースの検出を使用
        /// </summary>
        Contours,
        
        /// <summary>
        /// エッジベースの検出を使用
        /// </summary>
        EdgeBased
    }
}