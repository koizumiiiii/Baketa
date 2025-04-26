namespace Baketa.Core.Abstractions.OCR
{
    /// <summary>
    /// テキスト検出パラメータ
    /// </summary>
    public class TextDetectionParams
    {
        /// <summary>
        /// MSER: 領域の安定性を決定するパラメータ (5-10が一般的)
        /// </summary>
        public int MserDelta { get; set; } = 5;
        
        /// <summary>
        /// MSER: 検出する領域の最小面積
        /// </summary>
        public int MserMinArea { get; set; } = 60;
        
        /// <summary>
        /// MSER: 検出する領域の最大面積
        /// </summary>
        public int MserMaxArea { get; set; } = 14400;
        
        /// <summary>
        /// 検出された領域の最小アスペクト比 (幅/高さ)
        /// </summary>
        public float MinAspectRatio { get; set; } = 0.1f;
        
        /// <summary>
        /// 検出された領域の最大アスペクト比 (幅/高さ)
        /// </summary>
        public float MaxAspectRatio { get; set; } = 10.0f;
        
        /// <summary>
        /// 検出された領域の最小幅（ピクセル）
        /// </summary>
        public int MinWidth { get; set; } = 10;
        
        /// <summary>
        /// 検出された領域の最小高さ（ピクセル）
        /// </summary>
        public int MinHeight { get; set; } = 10;
        
        /// <summary>
        /// 重複する領域をマージするための閾値（0.0～1.0）
        /// 値が大きいほど、より重複度の高い領域のみがマージされる
        /// </summary>
        public float MergeThreshold { get; set; } = 0.5f;
        
        /// <summary>
        /// デフォルトパラメータでインスタンスを作成します
        /// </summary>
        public TextDetectionParams()
        {
            // デフォルト値はプロパティ初期化子で設定
        }
        
        /// <summary>
        /// テキスト検出方法に応じたパラメータを設定したインスタンスを作成します
        /// </summary>
        /// <param name="method">テキスト検出方法</param>
        /// <returns>最適化されたパラメータを持つインスタンス</returns>
        public static TextDetectionParams CreateForMethod(TextDetectionMethod method)
        {
            return method switch
            {
                TextDetectionMethod.Mser => new TextDetectionParams
                {
                    MserDelta = 5,
                    MserMinArea = 60,
                    MserMaxArea = 14400,
                    MinAspectRatio = 0.1f,
                    MaxAspectRatio = 10.0f
                },
                TextDetectionMethod.ConnectedComponents => new TextDetectionParams
                {
                    MinWidth = 5,
                    MinHeight = 5,
                    MinAspectRatio = 0.1f,
                    MaxAspectRatio = 15.0f
                },
                TextDetectionMethod.Contours => new TextDetectionParams
                {
                    MinWidth = 8,
                    MinHeight = 8,
                    MinAspectRatio = 0.2f,
                    MaxAspectRatio = 8.0f
                },
                TextDetectionMethod.EdgeBased => new TextDetectionParams
                {
                    MinWidth = 10,
                    MinHeight = 10,
                    MinAspectRatio = 0.2f,
                    MaxAspectRatio = 5.0f,
                    MergeThreshold = 0.6f
                },
                _ => new TextDetectionParams()
            };
        }
    }
}