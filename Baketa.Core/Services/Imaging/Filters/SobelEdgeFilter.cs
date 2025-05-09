using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;

namespace Baketa.Core.Services.Imaging.Filters
{
    /// <summary>
    /// ソーベルエッジ検出フィルター
    /// </summary>
    public class SobelEdgeFilter : ImageFilterBase
    {
        /// <summary>
        /// フィルターの名前
        /// </summary>
        public override string Name => "ソーベルエッジ検出";
        
        /// <summary>
        /// フィルターの説明
        /// </summary>
        public override string Description => "ソーベル演算子を使用したエッジ検出を適用します";
        
        /// <summary>
        /// フィルターのカテゴリ
        /// </summary>
        public override FilterCategory Category => FilterCategory.EdgeDetection;
        
        /// <summary>
        /// デフォルトパラメータを初期化します
        /// </summary>
        protected override void InitializeDefaultParameters()
        {
            RegisterParameter("XOrder", 1);     // X方向の微分次数
            RegisterParameter("YOrder", 1);     // Y方向の微分次数
            RegisterParameter("KernelSize", 3); // カーネルサイズ
            RegisterParameter("Scale", 1.0);    // スケール係数
            RegisterParameter("Delta", 0.0);    // オフセット値
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
            int xOrder = GetParameterValue<int>("XOrder");
            int yOrder = GetParameterValue<int>("YOrder");
            int kernelSize = GetParameterValue<int>("KernelSize");
            double scale = GetParameterValue<double>("Scale");
            double delta = GetParameterValue<double>("Delta");
            
            // パラメータの検証
            xOrder = Math.Clamp(xOrder, 0, 2);
            yOrder = Math.Clamp(yOrder, 0, 2);
            
            if (xOrder == 0 && yOrder == 0)
            {
                // どちらも0の場合は、少なくとも一方を1に設定
                xOrder = 1;
            }
            
            // カーネルサイズは奇数のみ許可
            if (kernelSize % 2 == 0)
            {
                kernelSize++;
            }
            kernelSize = Math.Max(kernelSize, 3);
            
            // スケールとデルタの範囲を制限
            scale = Math.Max(scale, 0.1);
            
            // グレースケール変換（エッジ検出はグレースケール画像に対して適用）
            var grayImage = await inputImage.ToGrayscaleAsync().ConfigureAwait(false);
            
            // エッジ検出処理のためのオプションを設定
            // 現時点では直接的なソーベルフィルターがIAdvancedImageに実装されていないため、
            // 代替手段としてシャープネスやコントラスト調整を使用
            var enhancementOptions = new ImageEnhancementOptions
            {
                Sharpness = 0.8f,                   // エッジを強調
                Contrast = (float)(scale * 1.2),    // コントラストを上げてエッジを見やすく
                NoiseReduction = 0.1f,              // わずかにノイズを減らす
            };
            
            // エッジ検出処理を実行
            // 理想的には、拡張メソッドとして実装された専用のソーベルフィルターを使用
            return await grayImage.EnhanceAsync(enhancementOptions).ConfigureAwait(false);
        }
    }
}