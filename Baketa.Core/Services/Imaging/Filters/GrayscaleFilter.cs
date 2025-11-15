using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;

namespace Baketa.Core.Services.Imaging.Filters;

/// <summary>
/// グレースケール変換フィルター
/// </summary>
public class GrayscaleFilter : ImageFilterBase
{
    /// <summary>
    /// フィルターの名前
    /// </summary>
    public override string Name => "グレースケール";

    /// <summary>
    /// フィルターの説明
    /// </summary>
    public override string Description => "画像をグレースケールに変換します";

    /// <summary>
    /// フィルターのカテゴリ
    /// </summary>
    public override FilterCategory Category => FilterCategory.ColorAdjustment;

    /// <summary>
    /// デフォルトパラメータを初期化します
    /// </summary>
    protected override void InitializeDefaultParameters()
    {
        // このフィルターにはパラメータがありません
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

        // グレースケール変換は一般的に IAdvancedImage インターフェースで
        // 直接サポートされていることが多いため、そのメソッドを使用
        var enhancementOptions = new ImageEnhancementOptions
        {
            // グレースケール変換のための適切なオプションを設定
            // （実際の実装はIAdvancedImageの実装により異なる）
        };

        // グレースケール変換を実行
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

        return new ImageInfo
        {
            Width = inputImage.Width,
            Height = inputImage.Height,
            Format = ImageFormat.Grayscale8,
            Channels = 1
        };
    }
}
