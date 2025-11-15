namespace Baketa.Core.Abstractions.OCR;

/// <summary>
/// OCR精度測定結果
/// </summary>
public sealed class AccuracyMeasurementResult
{
    /// <summary>
    /// 全体精度（0.0-1.0）
    /// </summary>
    public double OverallAccuracy { get; init; }

    /// <summary>
    /// 文字レベル精度（0.0-1.0）
    /// </summary>
    public double CharacterAccuracy { get; init; }

    /// <summary>
    /// 単語レベル精度（0.0-1.0）
    /// </summary>
    public double WordAccuracy { get; init; }

    /// <summary>
    /// 検出した文字数
    /// </summary>
    public int DetectedCharacterCount { get; init; }

    /// <summary>
    /// 正答した文字数
    /// </summary>
    public int CorrectCharacterCount { get; init; }

    /// <summary>
    /// 期待文字数（Ground Truth）
    /// </summary>
    public int ExpectedCharacterCount { get; init; }

    /// <summary>
    /// 処理時間
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>
    /// 信頼度の平均値
    /// </summary>
    public double AverageConfidence { get; init; }

    /// <summary>
    /// 測定に使用した設定のハッシュ
    /// </summary>
    public string SettingsHash { get; init; } = string.Empty;
}

/// <summary>
/// OCR精度測定インターフェース
/// </summary>
public interface IOcrAccuracyMeasurement
{
    /// <summary>
    /// 基準となるテスト画像セットで精度を測定
    /// </summary>
    /// <param name="ocrEngine">測定対象のOCRエンジン</param>
    /// <param name="testImagePath">テスト画像パス</param>
    /// <param name="expectedText">期待するテキスト（Ground Truth）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>精度測定結果</returns>
    Task<AccuracyMeasurementResult> MeasureAccuracyAsync(
        Baketa.Core.Abstractions.OCR.IOcrEngine ocrEngine,
        string testImagePath,
        string expectedText,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 複数のテスト画像で一括精度測定
    /// </summary>
    /// <param name="ocrEngine">測定対象のOCRエンジン</param>
    /// <param name="testCases">テストケース（画像パス, 期待テキスト）のペア</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>測定結果のリスト</returns>
    Task<IReadOnlyList<AccuracyMeasurementResult>> MeasureBatchAccuracyAsync(
        Baketa.Core.Abstractions.OCR.IOcrEngine ocrEngine,
        IReadOnlyList<(string ImagePath, string ExpectedText)> testCases,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 設定比較による精度改善効果の測定
    /// </summary>
    /// <param name="ocrEngine">測定対象のOCRエンジン</param>
    /// <param name="baselineSettings">基準設定</param>
    /// <param name="improvedSettings">改善設定</param>
    /// <param name="testCases">テストケース</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>基準設定と改善設定の比較結果</returns>
    Task<AccuracyComparisonResult> CompareSettingsAccuracyAsync(
        Baketa.Core.Abstractions.OCR.IOcrEngine ocrEngine,
        OcrEngineSettings baselineSettings,
        OcrEngineSettings improvedSettings,
        IReadOnlyList<(string ImagePath, string ExpectedText)> testCases,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 精度比較結果
/// </summary>
public sealed class AccuracyComparisonResult
{
    /// <summary>
    /// 基準設定での結果
    /// </summary>
    public AccuracyMeasurementResult BaselineResult { get; init; } = null!;

    /// <summary>
    /// 改善設定での結果
    /// </summary>
    public AccuracyMeasurementResult ImprovedResult { get; init; } = null!;

    /// <summary>
    /// 精度改善率（-1.0～1.0、正の値が改善）
    /// </summary>
    public double AccuracyImprovement => ImprovedResult.OverallAccuracy - BaselineResult.OverallAccuracy;

    /// <summary>
    /// 処理時間変化率（-1.0～∞、負の値が高速化）
    /// </summary>
    public double ProcessingTimeChange =>
        BaselineResult.ProcessingTime.TotalMilliseconds > 0
            ? (ImprovedResult.ProcessingTime.TotalMilliseconds - BaselineResult.ProcessingTime.TotalMilliseconds)
              / BaselineResult.ProcessingTime.TotalMilliseconds
            : 0.0;

    /// <summary>
    /// 改善が統計的に有意かどうか
    /// </summary>
    public bool IsSignificantImprovement => Math.Abs(AccuracyImprovement) > 0.05; // 5%以上の変化を有意とする
}
