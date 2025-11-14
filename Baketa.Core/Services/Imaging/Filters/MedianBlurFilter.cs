using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;

namespace Baketa.Core.Services.Imaging.Filters;

/// <summary>
/// メディアンぼかしフィルター
/// </summary>
public class MedianBlurFilter : ImageFilterBase
{
    /// <summary>
    /// フィルターの名前
    /// </summary>
    public override string Name => "メディアンぼかし";

    /// <summary>
    /// フィルターの説明
    /// </summary>
    public override string Description => "メディアンフィルターを使用したノイズ除去を適用します";

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

        // カーネルサイズが奇数であることを確認
        if (kernelSize % 2 == 0)
        {
            kernelSize++; // 偶数の場合は奇数に調整
        }

        // カーネルサイズが3以上であることを確認
        kernelSize = kernelSize < 3 ? 3 : kernelSize;

        // ぼかし処理のためのオプションを設定
        // メディアンフィルターの実装が無いため、現状はノイズ除去パラメータを使用
        var enhancementOptions = new ImageEnhancementOptions
        {
            NoiseReduction = kernelSize / 3.0f // ノイズ除去パラメータとして使用
        };

        // メディアンぼかし処理を実行
        // IAdvancedImageのExtensionメソッドを使用する実装が望ましい
        return await inputImage.EnhanceAsync(enhancementOptions).ConfigureAwait(false);
    }
}
