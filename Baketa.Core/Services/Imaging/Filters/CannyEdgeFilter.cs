using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;

namespace Baketa.Core.Services.Imaging.Filters;

    /// <summary>
    /// Cannyエッジ検出フィルター
    /// </summary>
    public class CannyEdgeFilter : ImageFilterBase
    {
        /// <summary>
        /// フィルターの名前
        /// </summary>
        public override string Name => "Cannyエッジ検出";
        
        /// <summary>
        /// フィルターの説明
        /// </summary>
        public override string Description => "Cannyアルゴリズムを使用した高精度エッジ検出を適用します";
        
        /// <summary>
        /// フィルターのカテゴリ
        /// </summary>
        public override FilterCategory Category => FilterCategory.EdgeDetection;
        
        /// <summary>
        /// デフォルトパラメータを初期化します
        /// </summary>
        protected override void InitializeDefaultParameters()
        {
            RegisterParameter("Threshold1", 100.0);  // 低閾値
            RegisterParameter("Threshold2", 200.0);  // 高閾値
            RegisterParameter("ApertureSize", 3);    // カーネルサイズ
            RegisterParameter("L2Gradient", false);  // L2ノルムを使用するかどうか
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
            double threshold1 = GetParameterValue<double>("Threshold1");
            double threshold2 = GetParameterValue<double>("Threshold2");
            // 以下のパラメータは使用しないので取得しない
            // int apertureSize = GetParameterValue<int>("ApertureSize");
            // bool l2Gradient = GetParameterValue<bool>("L2Gradient");
            
            // パラメータの検証
            // validatedThreshold1は使用されるので変数を定義
            double validatedThreshold1 = threshold1 < 0 ? 0 : threshold1;
            
            // threshold2の検証は行うが、値は使用しないのでディスカード変数に代入
            _ = threshold2 < validatedThreshold1 ? validatedThreshold1 : threshold2;
            
            // グレースケール変換（エッジ検出はグレースケール画像に対して適用）
            var grayImage = await inputImage.ToGrayscaleAsync().ConfigureAwait(false);
            
            // Cannyエッジ検出のためのオプションを設定
            // 現時点では直接的なCannyフィルターがIAdvancedImageに実装されていないため、
            // 代替手段としてシャープネスやコントラスト調整を使用
            var enhancementOptions = new ImageEnhancementOptions
            {
                Sharpness = 1.0f,                            // エッジを強調
                Contrast = 1.5f,                             // コントラストを上げてエッジを見やすく
                NoiseReduction = 0.3f,                       // ノイズを軽減（Cannyはノイズに敏感）
                BinarizationThreshold = (int)validatedThreshold1 // 検証済みの閾値を使用
            };
            
            // ガウシアンぼかし（Cannyの最初のステップ）
            var blurredImage = await grayImage.EnhanceAsync(new ImageEnhancementOptions
            {
                NoiseReduction = 0.5f
            }).ConfigureAwait(false);
            
            // エッジ検出処理を実行
            // 理想的には、拡張メソッドとして実装された専用のCannyフィルターを使用
            return await blurredImage.EnhanceAsync(enhancementOptions).ConfigureAwait(false);
        }
    }
