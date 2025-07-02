namespace Baketa.Core.Abstractions.Imaging;

    /// <summary>
    /// 画像強調オプション
    /// </summary>
    public class ImageEnhancementOptions
    {
        /// <summary>
        /// 明るさ調整 (-1.0〜1.0)
        /// </summary>
        public float Brightness { get; set; }
        
        /// <summary>
        /// コントラスト調整 (0.0〜2.0)
        /// </summary>
        public float Contrast { get; set; }
        
        /// <summary>
        /// シャープネス調整 (0.0〜1.0)
        /// </summary>
        public float Sharpness { get; set; }
        
        /// <summary>
        /// ノイズ除去レベル (0.0〜1.0)
        /// </summary>
        public float NoiseReduction { get; set; }
        
        /// <summary>
        /// 二値化閾値 (0〜255、0で無効)
        /// </summary>
        public int BinarizationThreshold { get; set; }
        
        /// <summary>
        /// 適応的二値化を使用するかどうか
        /// </summary>
        public bool UseAdaptiveThreshold { get; set; }
        
        /// <summary>
        /// 適応的二値化のブロックサイズ
        /// </summary>
        public int AdaptiveBlockSize { get; set; }
        
        /// <summary>
        /// テキスト検出のための最適化を行うかどうか
        /// </summary>
        public bool OptimizeForTextDetection { get; set; }
    }
