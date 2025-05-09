using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;

namespace Baketa.Core.Services.Imaging.Filters
{
    /// <summary>
    /// ガウシアンぼかしフィルター
    /// </summary>
    public class GaussianBlurFilter : ImageFilterBase
    {
        /// <summary>
        /// フィルターの名前
        /// </summary>
        public override string Name => "ガウシアンぼかし";
        
        /// <summary>
        /// フィルターの説明
        /// </summary>
        public override string Description => "ガウシアン関数を用いたぼかしを適用します";
        
        /// <summary>
        /// フィルターのカテゴリ
        /// </summary>
        public override FilterCategory Category => FilterCategory.Blur;
        
        /// <summary>
        /// デフォルトパラメータを初期化します
        /// </summary>
        protected override void InitializeDefaultParameters()
        {
            RegisterParameter("KernelSize", 3);  // カーネルサイズ（3x3, 5x5, 7x7, ...）
            RegisterParameter("Sigma", 1.0);     // 標準偏差
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
            int kernelSize = GetParameterValue<int>("KernelSize");
            double sigma = GetParameterValue<double>("Sigma");
            
            // カーネルサイズが奇数であることを確認
            if (kernelSize % 2 == 0)
            {
                kernelSize++; // 偶数の場合は奇数に調整
            }
            
            // カーネルサイズが3以上であることを確認
            // 以下のように宣言すると不要な代入を避けられる
            kernelSize = kernelSize < 3 ? 3 : kernelSize;
            
            // 標準偏差が正の値であることを確認
            // 不要な代入を避けるため以下のように書く
            sigma = sigma < 0.1 ? 0.1 : sigma;
            
            // ぼかし処理のためのオプションを設定
            var enhancementOptions = new ImageEnhancementOptions
            {
                NoiseReduction = (float)sigma / 3.0f // ノイズ除去パラメータとして使用
            };
            
            // ぼかし処理を実行
            // 実際の実装はIAdvancedImageの実装により異なる
            // カーネルサイズとシグマを直接渡せない場合は、代替手段を使用
            return await inputImage.EnhanceAsync(enhancementOptions).ConfigureAwait(false);
        }
    }
}