namespace Baketa.Core.Abstractions.OCR
{
    /// <summary>
    /// モルフォロジー演算タイプ
    /// </summary>
    public enum MorphType
    {
        /// <summary>
        /// 収縮処理: オブジェクトの境界部分を削る
        /// </summary>
        Erode,
        
        /// <summary>
        /// 膨張処理: オブジェクトの境界部分を拡張する
        /// </summary>
        Dilate,
        
        /// <summary>
        /// オープン処理: 収縮処理後に膨張処理を行う（小さなノイズを除去）
        /// </summary>
        Open,
        
        /// <summary>
        /// クローズ処理: 膨張処理後に収縮処理を行う（小さな穴を埋める）
        /// </summary>
        Close,
        
        /// <summary>
        /// 勾配処理: 膨張処理と収縮処理の差分を取る（オブジェクトの輪郭を強調）
        /// </summary>
        Gradient,
        
        /// <summary>
        /// トップハット処理: 元画像とオープン処理結果の差分を取る（明るい小さな特徴を強調）
        /// </summary>
        TopHat,
        
        /// <summary>
        /// ブラックハット処理: クローズ処理結果と元画像の差分を取る（暗い小さな特徴を強調）
        /// </summary>
        BlackHat
    }
}