using Baketa.Core.Abstractions.OCR;
using Sdcb.PaddleOCR;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;

/// <summary>
/// 言語別最適化、言語判定、パラメータ調整を担当するサービス
/// </summary>
public interface IPaddleOcrLanguageOptimizer
{
    /// <summary>
    /// 設定から言語決定
    /// </summary>
    string DetermineLanguageFromSettings(OcrEngineSettings settings);

    /// <summary>
    /// 表示名から言語コードマッピング
    /// </summary>
    string MapDisplayNameToLanguageCode(string displayName);

    /// <summary>
    /// 言語別最適化適用
    /// </summary>
    void ApplyLanguageOptimizations(PaddleOcrAll engine, string language);

    /// <summary>
    /// 最適ゲームプロファイル選択
    /// </summary>
    string SelectOptimalGameProfile(ImageCharacteristics characteristics);
}

/// <summary>
/// 画像特性情報
/// </summary>
public record ImageCharacteristics(int Width, int Height, int AverageBrightness);
