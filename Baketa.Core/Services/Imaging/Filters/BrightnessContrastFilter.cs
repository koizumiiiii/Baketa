using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;

namespace Baketa.Core.Services.Imaging.Filters
{
    /// <summary>
    /// 明度・コントラスト調整フィルター
    /// </summary>
    public class BrightnessContrastFilter : ImageFilterBase
    {
        /// <summary>
        /// フィルターの名前
        /// </summary>
        public override string Name => "明度・コントラスト調整";
        
        /// <summary>
        /// フィルターの説明
        /// </summary>
        public override string Description => "画像の明度とコントラストを調整します";
        
        /// <summary>
        /// フィルターのカテゴリ
        /// </summary>
        public override FilterCategory Category => FilterCategory.ColorAdjustment;
        
        /// <summary>
        /// デフォルトパラメータを初期化します
        /// </summary>
        protected override void InitializeDefaultParameters()
        {
            RegisterParameter("Brightness", 0.0); // -1.0～1.0 (0.0で変化なし)
            RegisterParameter("Contrast", 1.0);   // 0.5～2.0 (1.0で変化なし)
        }
        
        /// <summary>
        /// 画像にフィルターを適用します
        /// </summary>
        /// <param name="inputImage">入力画像</param>
        /// <returns>フィルター適用後の新しい画像</returns>
        /// <exception cref="System.ArgumentNullException">inputImageがnullの場合</exception>
        public override async Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
        {
            ArgumentNullException.ThrowIfNull(inputImage);
            
            // パラメータを取得
            double brightness = GetParameterValue<double>("Brightness");
            double contrast = GetParameterValue<double>("Contrast");
            
            // ImageEnhancementOptionsを使用して調整を適用
            var enhancementOptions = new ImageEnhancementOptions
            {
                Brightness = (float)brightness,
                Contrast = (float)contrast
            };
            
            // 明度・コントラスト調整を実行
            return await inputImage.EnhanceAsync(enhancementOptions).ConfigureAwait(false);
        }
    }
}