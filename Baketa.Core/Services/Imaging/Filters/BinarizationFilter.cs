using System;
using System.Collections.Generic;
using System.Linq;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Services.Imaging.Filters
{
    /// <summary>
    /// 二値化フィルター
    /// </summary>
    public sealed class BinarizationFilter(byte threshold) : IImageFilter
    {
        private readonly byte _threshold = threshold;
        
        /// <summary>
        /// フィルター名
        /// </summary>
        public static string FilterName => "二値化フィルター";
        
        // インターフェース実装のため、static化できない
        /// <summary>
        /// フィルター名
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
            Justification = "インターフェース実装のため静的化できない")]
        public string Name => FilterName;
        
        /// <summary>
        /// 指定された閾値に対する説明テキストを生成します
        /// </summary>
        /// <param name="thresholdValue">閾値</param>
        /// <returns>説明テキスト</returns>
        public static string GenerateDescription(byte thresholdValue) => $"画像を指定された閾値({thresholdValue})で二値化します";
        
        /// <inheritdoc/>
        public string Description => GenerateDescription(_threshold);
        
        /// <inheritdoc/>
        public IReadOnlyDictionary<string, object> Parameters => new Dictionary<string, object> { ["threshold"] = _threshold };
        
        /// <inheritdoc/>
        public IReadOnlyList<byte> Apply(IReadOnlyList<byte> imageData, int width, int height, int stride)
        {
            ArgumentNullException.ThrowIfNull(imageData, nameof(imageData));
            
            // IReadOnlyList<byte>をbyte[]に変換して処理
            byte[] sourceData = imageData.ToArray();
                
            // 結果配列の作成
            byte[] resultData = new byte[sourceData.Length];
            
            for (int i = 0; i < sourceData.Length; i++)
            {
                // グレースケールを想定
                resultData[i] = sourceData[i] >= _threshold ? (byte)255 : (byte)0;
            }
            
            return resultData;
        }
    }
}