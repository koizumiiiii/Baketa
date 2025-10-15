using Baketa.Core.Abstractions.OCR;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.Measurement;

/// <summary>
/// OCR精度測定の実行とテストを行うサービス
/// </summary>
public sealed class OcrAccuracyTestRunner(
    RuntimeOcrAccuracyLogger accuracyLogger,
    TestImageGenerator imageGenerator,
    ILogger<OcrAccuracyTestRunner> logger)
{
    private readonly RuntimeOcrAccuracyLogger _accuracyLogger = accuracyLogger ?? throw new ArgumentNullException(nameof(accuracyLogger));
    private readonly TestImageGenerator _imageGenerator = imageGenerator ?? throw new ArgumentNullException(nameof(imageGenerator));
    private readonly ILogger<OcrAccuracyTestRunner> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// OCR精度測定の基本テストを実行
    /// </summary>
    /// <param name="outputDir">出力ディレクトリ</param>
    /// <returns>テスト結果レポートパス</returns>
    public async Task<string> RunBasicAccuracyTestAsync(string? outputDir = null)
    {
        try
        {
            outputDir ??= System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BaketaOcrAccuracyTest");
            
            _logger.LogInformation("🚀 OCR精度測定テスト開始 - 出力先: {OutputDir}", outputDir);
            
            // テスト画像の生成
            _logger.LogInformation("📷 テスト画像生成中...");
            var testCases = await _imageGenerator.GenerateTestCasesAsync(outputDir).ConfigureAwait(false);
            
            _logger.LogInformation("✅ テスト画像生成完了: {Count}件", testCases.Count);
            
            // 各テストケースについてOCR結果をシミュレート
            foreach (var (imagePath, expectedText) in testCases)
            {
                // 実際のOCRエンジンの代わりにシミュレート結果を生成
                var simulatedOcrResult = await SimulateOcrResultAsync(imagePath, expectedText).ConfigureAwait(false);
                
                // OCR結果を記録（期待テキスト付き）
                await _accuracyLogger.LogOcrResultWithExpectedAsync(
                    simulatedOcrResult, 
                    expectedText, 
                    imagePath).ConfigureAwait(false);
                
                _logger.LogInformation("📊 OCR結果記録: {ImagePath} -> '{ExpectedText}'", 
                    System.IO.Path.GetFileName(imagePath), expectedText);
            }
            
            // 統計情報の取得
            var stats = _accuracyLogger.GetAccuracyStats();
            _logger.LogInformation("📈 測定統計: 総数={Total}, 期待テキスト付き={WithExpected}, 平均精度={AvgAccuracy:P2}",
                stats.TotalMeasurements,
                stats.MeasurementsWithExpected,
                stats.AverageOverallAccuracy);
            
            // 詳細レポートの生成
            var reportPath = System.IO.Path.Combine(outputDir, "ocr_accuracy_test_report.md");
            var generatedReportPath = await _accuracyLogger.GenerateDetailedReportAsync(reportPath).ConfigureAwait(false);
            
            _logger.LogInformation("🎯 OCR精度測定テスト完了 - レポート: {ReportPath}", generatedReportPath);
            
            return generatedReportPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR精度測定テスト中にエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// OCR結果をシミュレート（実際のOCRエンジンの代替）
    /// </summary>
    /// <param name="imagePath">画像パス</param>
    /// <param name="expectedText">期待テキスト</param>
    /// <returns>シミュレートされたOCR結果</returns>
    private async Task<OcrResults> SimulateOcrResultAsync(string imagePath, string expectedText)
    {
        await Task.Delay(50).ConfigureAwait(false); // OCR処理時間をシミュレート
        
        // 画像ファイル名から精度をシミュレート
        var fileName = System.IO.Path.GetFileName(imagePath);
        var accuracy = fileName switch
        {
            var name when name.Contains("simple") => 0.95, // 単純なテキストは高精度
            var name when name.Contains("mixed") => 0.85,  // 混合テキストは中精度
            var name when name.Contains("game") => 0.80,   // ゲーム風は中精度
            _ => 0.90 // デフォルト
        };
        
        // 精度に基づいてテキストを変更（エラーシミュレート）
        var detectedText = SimulateTextRecognitionErrors(expectedText, accuracy);
        
        // ダミー画像オブジェクト（実際の実装では実際のIImageが必要）
        var dummyImage = new DummyImage(imagePath);
        
        // OCR領域の生成
        var textRegion = new OcrTextRegion(
            detectedText,
            new System.Drawing.Rectangle(10, 10, 280, 80),
            accuracy);
        
        return new OcrResults(
            [textRegion],
            dummyImage,
            TimeSpan.FromMilliseconds(Random.Shared.Next(100, 500)),
            "jpn",
            null,
            detectedText);
    }

    /// <summary>
    /// テキスト認識エラーをシミュレート
    /// </summary>
    /// <param name="originalText">元のテキスト</param>
    /// <param name="accuracy">目標精度</param>
    /// <returns>エラーが含まれる可能性があるテキスト</returns>
    private static string SimulateTextRecognitionErrors(string originalText, double accuracy)
    {
        if (accuracy >= 0.98) return originalText; // ほぼ完璧
        
        var random = Random.Shared;
        var chars = originalText.ToCharArray();
        var errorRate = 1.0 - accuracy;
        var errorsToIntroduce = (int)(chars.Length * errorRate);
        
        for (int i = 0; i < errorsToIntroduce && i < chars.Length; i++)
        {
            var index = random.Next(chars.Length);
            
            // ランダムにエラーを導入
            switch (random.Next(4))
            {
                case 0: // 文字置換
                    chars[index] = GetSimilarCharacter(chars[index]);
                    break;
                case 1: // 文字削除（最後の文字でない場合）
                    if (index < chars.Length - 1)
                        chars[index] = '\0'; // 削除マーク
                    break;
                case 2: // 文字挿入は複雑なため省略
                    break;
                case 3: // 文字順序入れ替え
                    if (index < chars.Length - 1)
                        (chars[index], chars[index + 1]) = (chars[index + 1], chars[index]);
                    break;
            }
        }
        
        return new string([.. chars.Where(c => c != '\0')]);
    }

    /// <summary>
    /// 似た文字を取得（OCR誤認識をシミュレート）
    /// </summary>
    /// <param name="original">元の文字</param>
    /// <returns>似た文字</returns>
    private static char GetSimilarCharacter(char original) => original switch
    {
        'o' or 'O' => '0',
        '0' => 'O',
        'l' or 'I' => '1',
        '1' => 'l',
        'こ' => 'ニ',
        'ニ' => 'こ',
        'ロ' => 'n',
        _ => original
    };

    /// <summary>
    /// 履歴をクリアしてテストをリセット
    /// </summary>
    public void ResetTest()
    {
        _accuracyLogger.ClearHistory();
        _logger.LogInformation("🗑️ OCR精度測定テストをリセットしました");
    }
}

/// <summary>
/// ダミー画像実装（テスト用）
/// </summary>
internal sealed class DummyImage(string path) : Baketa.Core.Abstractions.Imaging.IImage
{
    public int Width { get; } = 300;
    public int Height { get; } = 100;
    public Baketa.Core.Abstractions.Imaging.ImageFormat Format { get; } = Baketa.Core.Abstractions.Imaging.ImageFormat.Png;
    public string? FilePath { get; } = path;
    public DateTime CreatedAt { get; } = DateTime.Now;
    public long SizeInBytes => 1024; // ダミー値

    /// <summary>
    /// PixelFormat property for IImage extension
    /// </summary>
    public Baketa.Core.Abstractions.Memory.ImagePixelFormat PixelFormat => Baketa.Core.Abstractions.Memory.ImagePixelFormat.Rgba32;

    /// <summary>
    /// GetImageMemory method for IImage extension
    /// </summary>
    public ReadOnlyMemory<byte> GetImageMemory()
    {
        return new ReadOnlyMemory<byte>(Array.Empty<byte>());
    }

    /// <summary>
    /// 🔥 [PHASE5.2G-A] LockPixelData (DummyImage is test-only, not supported)
    /// </summary>
    public Baketa.Core.Abstractions.Imaging.PixelDataLock LockPixelData() => throw new NotSupportedException("DummyImage does not support LockPixelData");

    public void Dispose() { }
    public byte[] ToByteArray() => [];
    public Task<byte[]> ToByteArrayAsync() => Task.FromResult(Array.Empty<byte>());
    public System.Drawing.Bitmap ToBitmap() => new(Width, Height);
    public Baketa.Core.Abstractions.Imaging.IImage Clone() => new DummyImage(FilePath ?? string.Empty);

    public Baketa.Core.Abstractions.Imaging.IImage Crop(System.Drawing.Rectangle _) => 
        new DummyImage(FilePath ?? string.Empty);

    public Baketa.Core.Abstractions.Imaging.IImage Resize(int _1, int _2) => 
        new DummyImage(FilePath ?? string.Empty);
        
    public Task<Baketa.Core.Abstractions.Imaging.IImage> ResizeAsync(int _1, int _2) => 
        Task.FromResult<Baketa.Core.Abstractions.Imaging.IImage>(new DummyImage(FilePath ?? string.Empty));

    public void SaveToFile(string _) { }

    public Task SaveToFileAsync(string _1, CancellationToken _2 = default) => 
        Task.CompletedTask;
}
