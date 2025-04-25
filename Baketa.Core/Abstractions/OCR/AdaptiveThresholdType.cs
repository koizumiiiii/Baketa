namespace Baketa.Core.Abstractions.OCR
{
    /// <summary>
    /// 適応的閾値処理のタイプ
    /// </summary>
    public enum AdaptiveThresholdType
    {
        /// <summary>
        /// 近傍領域の平均値を閾値として使用
        /// </summary>
        Mean,
        
        /// <summary>
        /// 近傍領域のガウス加重平均を閾値として使用
        /// </summary>
        Gaussian
    }
}