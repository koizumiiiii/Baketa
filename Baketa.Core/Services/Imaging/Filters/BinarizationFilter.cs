using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;

namespace Baketa.Core.Services.Imaging.Filters;

    /// <summary>
    /// 二値化フィルター
    /// </summary>
    public sealed class BinarizationFilter : ImageFilterBase
    {
        /// <summary>
        /// フィルター名
        /// </summary>
        public override string Name => "二値化フィルター";
        
        /// <summary>
        /// フィルターの説明
        /// </summary>
        public override string Description => $"画像を指定された閾値({GetParameterValue<byte>("Threshold")})で二値化します";
        
        /// <summary>
        /// フィルターのカテゴリ
        /// </summary>
        public override FilterCategory Category => FilterCategory.Threshold;

        /// <summary>
        /// デフォルトのコンストラクター
        /// </summary>
        public BinarizationFilter()
        {
            InitializeDefaultParameters();
        }
        
        /// <summary>
        /// 閾値を指定して初期化するコンストラクター
        /// </summary>
        /// <param name="threshold">二値化の閾値（0～255）</param>
        public BinarizationFilter(byte threshold)
        {
            InitializeDefaultParameters();
            SetParameter("Threshold", threshold);
        }
        
        /// <summary>
        /// デフォルトパラメータを初期化します
        /// </summary>
        protected override void InitializeDefaultParameters()
        {
            RegisterParameter("Threshold", (byte)128);
            RegisterParameter("InvertResult", false);
        }
        
        /// <summary>
        /// 画像にフィルターを適用します
        /// </summary>
        /// <param name="inputImage">入力画像</param>
        /// <returns>フィルター適用後の新しい画像</returns>
        public override async Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
        {
            byte threshold = GetParameterValue<byte>("Threshold");
            return await inputImage.ToBinaryAsync(threshold).ConfigureAwait(false);
        }
        
        /// <summary>
        /// フィルター適用後の画像情報を取得します
        /// </summary>
        /// <param name="inputImage">入力画像</param>
        /// <returns>出力画像の情報</returns>
        public override ImageInfo GetOutputImageInfo(IAdvancedImage inputImage)
        {
            return new ImageInfo
            {
                Width = inputImage.Width,
                Height = inputImage.Height,
                Format = ImageFormat.Grayscale8,
                Channels = 1
            };
        }
        
        /// <summary>
        /// レガシーインターフェースとの互換性のための実装
        /// </summary>
        /// <param name="imageData">処理する画像データ</param>
        /// <param name="_">画像の幅</param>
        /// <param name="__">画像の高さ</param>
        /// <param name="___">ストライド（1行あたりのバイト数）</param>
        /// <returns>処理後の画像データ</returns>
        public IReadOnlyList<byte> Apply(IReadOnlyList<byte> imageData, int _, int __, int ___)
        {
            ArgumentNullException.ThrowIfNull(imageData, nameof(imageData));
            
            // IReadOnlyList<byte>をbyte[]に変換して処理
            byte[] sourceData = imageData.ToArray();
                
            // 結果配列の作成
            byte[] resultData = new byte[sourceData.Length];
            
            byte threshold = GetParameterValue<byte>("Threshold");
            bool invertResult = GetParameterValue<bool>("InvertResult");
            
            for (int i = 0; i < sourceData.Length; i++)
            {
                // グレースケールを想定
                bool isAboveThreshold = sourceData[i] >= threshold;
                resultData[i] = (byte)(invertResult ? (isAboveThreshold ? 0 : 255) : (isAboveThreshold ? 255 : 0));
            }
            
            return resultData;
        }
    }
