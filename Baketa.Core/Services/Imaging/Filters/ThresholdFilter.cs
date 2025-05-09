using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;

namespace Baketa.Core.Services.Imaging.Filters
{
    /// <summary>
    /// 二値化フィルター
    /// </summary>
    public class ThresholdFilter : ImageFilterBase
    {
        /// <summary>
        /// フィルターの名前
        /// </summary>
        public override string Name => "二値化";
        
        /// <summary>
        /// フィルターの説明
        /// </summary>
        public override string Description => "画像を指定された閾値で二値化します";
        
        /// <summary>
        /// フィルターのカテゴリ
        /// </summary>
        public override FilterCategory Category => FilterCategory.Threshold;
        
        /// <summary>
        /// デフォルトパラメータを初期化します
        /// </summary>
        protected override void InitializeDefaultParameters()
        {
            RegisterParameter("Threshold", 128);       // 閾値 (0～255)
            RegisterParameter("MaxValue", 255);        // 最大値 (0～255)
            RegisterParameter("InvertResult", false);  // 結果を反転するかどうか
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
            int threshold = GetParameterValue<int>("Threshold");
            int maxValue = GetParameterValue<int>("MaxValue");
            bool invertResult = GetParameterValue<bool>("InvertResult");
            
            // 閾値が有効範囲内であることを確認
            // 不要な代入を避けるため条件文で表現
            threshold = threshold < 0 ? 0 : (threshold > 255 ? 255 : threshold);
            maxValue = maxValue < 0 ? 0 : (maxValue > 255 ? 255 : maxValue);
            
            // 二値化処理のためのオプションを設定
            var enhancementOptions = new ImageEnhancementOptions
            {
                BinarizationThreshold = threshold,
                // invertResultを使用するには、カスタム実装が必要かもしれません
            };
            
            // 二値化処理を実行
            return await inputImage.EnhanceAsync(enhancementOptions).ConfigureAwait(false);
        }
        
        /// <summary>
        /// フィルター適用後の画像情報を取得します
        /// </summary>
        /// <param name="inputImage">入力画像</param>
        /// <returns>出力画像の情報</returns>
        /// <exception cref="System.ArgumentNullException">inputImageがnullの場合</exception>
        public override ImageInfo GetOutputImageInfo(IAdvancedImage inputImage)
        {
            ArgumentNullException.ThrowIfNull(inputImage);
            
            // 二値化処理後はグレースケール画像になることが多い
            return new ImageInfo
            {
                Width = inputImage.Width,
                Height = inputImage.Height,
                Format = ImageFormat.Grayscale8,
                Channels = 1
            };
        }
    }
}