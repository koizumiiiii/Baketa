namespace Baketa.Core.Abstractions.Imaging;

    /// <summary>
    /// フィルターカテゴリを表す列挙型
    /// </summary>
    public enum FilterCategory
    {
        /// <summary>
        /// 色調変換
        /// </summary>
        ColorAdjustment,
        
        /// <summary>
        /// ぼかし・ノイズ除去
        /// </summary>
        Blur,
        
        /// <summary>
        /// シャープ化
        /// </summary>
        Sharpen,
        
        /// <summary>
        /// エッジ検出
        /// </summary>
        EdgeDetection,
        
        /// <summary>
        /// 二値化
        /// </summary>
        Threshold,
        
        /// <summary>
        /// 形態学的処理
        /// </summary>
        Morphology,
        
        /// <summary>
        /// 特殊効果
        /// </summary>
        Effect,
        
        /// <summary>
        /// 複合フィルター
        /// </summary>
        Composite
    }
