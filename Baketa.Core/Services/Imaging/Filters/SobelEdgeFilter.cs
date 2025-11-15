using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;

namespace Baketa.Core.Services.Imaging.Filters;

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

        // 現時点では実装が簡略化されているため、大部分のパラメータは使用していない
        _ = GetParameterValue<int>("XOrder");
        _ = GetParameterValue<int>("YOrder");
        _ = GetParameterValue<int>("KernelSize");
        double scale = GetParameterValue<double>("Scale"); // スケールは使用する
        _ = GetParameterValue<double>("Delta");

        // パラメータの検証（注：実際のOpenCV実装では使用される予定のパラメータ）
        // 現在は簡易実装のため実際には使用していない

        // スケールの範囲を制限（唯一使用するパラメータ）
        double scaleFactor = Math.Max(scale, 0.1);

        // グレースケール変換（エッジ検出はグレースケール画像に対して適用）
        var grayImage = await inputImage.ToGrayscaleAsync().ConfigureAwait(false);

        // エッジ検出処理のためのオプションを設定
        // 現時点では直接的なソーベルフィルターがIAdvancedImageに実装されていないため、
        // 代替手段としてシャープネスやコントラスト調整を使用
        var enhancementOptions = new ImageEnhancementOptions
        {
            Sharpness = 0.8f,                        // エッジを強調
            Contrast = (float)(scaleFactor * 1.2), // コントラストを上げてエッジを見やすく
            NoiseReduction = 0.1f,                   // わずかにノイズを減らす
        };

        // エッジ検出処理を実行
        // 理想的には、拡張メソッドとして実装された専用のソーベルフィルターを使用
        return await grayImage.EnhanceAsync(enhancementOptions).ConfigureAwait(false);
    }
}
