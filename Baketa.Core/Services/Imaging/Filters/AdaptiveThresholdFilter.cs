using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;

namespace Baketa.Core.Services.Imaging.Filters;

/// <summary>
/// 適応的二値化フィルター
/// </summary>
public class AdaptiveThresholdFilter : ImageFilterBase
{
    /// <summary>
    /// フィルターの名前
    /// </summary>
    public override string Name => "適応的二値化";

    /// <summary>
    /// フィルターの説明
    /// </summary>
    public override string Description => "画像を適応的なアルゴリズムで二値化します";

    /// <summary>
    /// フィルターのカテゴリ
    /// </summary>
    public override FilterCategory Category => FilterCategory.Threshold;

    /// <summary>
    /// 適応的二値化の方法
    /// </summary>
    public enum AdaptiveMethod
    {
        /// <summary>
        /// 平均値に基づく適応的二値化
        /// </summary>
        Mean,

        /// <summary>
        /// ガウシアン重み付けに基づく適応的二値化
        /// </summary>
        Gaussian
    }

    /// <summary>
    /// デフォルトパラメータを初期化します
    /// </summary>
    protected override void InitializeDefaultParameters()
    {
        RegisterParameter("BlockSize", 11);            // ブロックサイズ (3, 5, 7, ..., 奇数のみ)
        RegisterParameter("C", 2.0);                   // 定数（閾値調整値）
        RegisterParameter("MaxValue", 255);            // 最大値 (0～255)
        RegisterParameter("Method", AdaptiveMethod.Gaussian); // 適応的二値化の方法
        RegisterParameter("InvertResult", false);      // 結果を反転するかどうか
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
        int blockSize = GetParameterValue<int>("BlockSize");
        // double c = GetParameterValue<double>("C"); // 未使用の変数
        // int maxValue = GetParameterValue<int>("MaxValue"); // 未使用の変数
        // AdaptiveMethod method = GetParameterValue<AdaptiveMethod>("Method"); // 未使用の変数
        // bool invertResult = GetParameterValue<bool>("InvertResult"); // 未使用の変数

        // ブロックサイズが奇数であることを確認
        if (blockSize % 2 == 0)
        {
            blockSize++; // 偶数の場合は奇数に調整
        }

        // ブロックサイズが3以上であることを確認
        blockSize = Math.Max(3, blockSize);

        // 最大値が有効範囲内であることを確認
        // int maxValue = GetParameterValue<int>("MaxValue");
        // maxValue = Math.Clamp(maxValue, 0, 255);

        // 適応的二値化処理のためのオプションを設定
        var enhancementOptions = new ImageEnhancementOptions
        {
            UseAdaptiveThreshold = true,
            AdaptiveBlockSize = blockSize,
            // 他のパラメータはOcrImageOptionsに対応するものがない場合があります
            // その場合は、カスタム実装が必要かもしれません
        };

        // 適応的二値化処理を実行
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
        // 適応的二値化処理後はグレースケール画像になることが多い
        return new ImageInfo
        {
            Width = inputImage.Width,
            Height = inputImage.Height,
            Format = ImageFormat.Grayscale8,
            Channels = 1
        };
    }
}
