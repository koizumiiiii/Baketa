namespace Baketa.Core.Abstractions.Imaging;

/// <summary>
/// OCR画像最適化オプション
/// </summary>
public class OcrImageOptions
{
    /// <summary>
    /// 二値化閾値 (0〜255、0で無効)
    /// </summary>
    public int BinarizationThreshold { get; set; } // 初期値は自動的に0

    /// <summary>
    /// 適応的二値化を使用するかどうか
    /// </summary>
    public bool UseAdaptiveThreshold { get; set; } = true;

    /// <summary>
    /// 適応的二値化のブロックサイズ
    /// </summary>
    public int AdaptiveBlockSize { get; set; } = 11;

    /// <summary>
    /// ノイズ除去レベル (0.0〜1.0)
    /// </summary>
    public float NoiseReduction { get; set; } = 0.3f;

    /// <summary>
    /// コントラスト強調 (0.0〜2.0、1.0で変更なし)
    /// </summary>
    public float ContrastEnhancement { get; set; } = 1.2f;

    /// <summary>
    /// シャープネス強調 (0.0〜1.0)
    /// </summary>
    public float SharpnessEnhancement { get; set; } = 0.3f;

    /// <summary>
    /// 境界を膨張させる画素数
    /// </summary>
    public int DilationPixels { get; set; } // 初期値は自動的に0

    /// <summary>
    /// テキスト方向の検出と修正
    /// </summary>
    public bool DetectAndCorrectOrientation { get; set; } // 初期値は自動的にfalse

    /// <summary>
    /// プリセットプロファイルを作成します
    /// </summary>
    /// <param name="preset">使用するプリセット</param>
    /// <returns>指定されたプリセットに基づく最適化オプション</returns>
    /// <exception cref="System.ArgumentOutOfRangeException">不正なプリセット値が指定された場合</exception>
    public static OcrImageOptions CreatePreset(OcrPreset preset)
    {
        return preset switch
        {
            OcrPreset.Default => new OcrImageOptions(),
            OcrPreset.HighContrast => new OcrImageOptions
            {
                UseAdaptiveThreshold = true,
                ContrastEnhancement = 1.4f,
                NoiseReduction = 0.4f
            },
            OcrPreset.SmallText => new OcrImageOptions
            {
                AdaptiveBlockSize = 7,
                SharpnessEnhancement = 0.5f,
                NoiseReduction = 0.5f
            },
            OcrPreset.LightText => new OcrImageOptions
            {
                UseAdaptiveThreshold = true,
                ContrastEnhancement = 1.6f,
                NoiseReduction = 0.4f,
                DilationPixels = 1
            },
            _ => throw new System.ArgumentOutOfRangeException(nameof(preset), preset, "無効なOCRプリセットが指定されました")
        };
    }
}

/// <summary>
/// OCRプリセット
/// </summary>
public enum OcrPreset
{
    /// <summary>
    /// デフォルト設定
    /// </summary>
    Default = 0,

    /// <summary>
    /// 高コントラスト設定
    /// </summary>
    HighContrast = 1,

    /// <summary>
    /// 小さいテキスト向け設定
    /// </summary>
    SmallText = 2,

    /// <summary>
    /// 薄いテキスト向け設定
    /// </summary>
    LightText = 3
}
