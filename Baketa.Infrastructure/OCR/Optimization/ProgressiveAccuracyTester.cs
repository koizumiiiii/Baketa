using OpenCvSharp;
using Baketa.Core.Utilities;
using Baketa.Infrastructure.OCR.Preprocessing;
using Baketa.Infrastructure.OCR.PaddleOCR.Engine;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Imaging;
using System.Text.Json;
using System.IO;
using System.Text;

namespace Baketa.Infrastructure.OCR.Optimization;

/// <summary>
/// 段階的OCR精度改善テストシステム
/// 各最適化手法の効果を個別に測定・比較
/// </summary>
public class ProgressiveAccuracyTester
{
    private readonly IOcrEngine _ocrEngine;
    private readonly string _testImagePath;

    public ProgressiveAccuracyTester(IOcrEngine ocrEngine, string testImagePath)
    {
        _ocrEngine = ocrEngine ?? throw new ArgumentNullException(nameof(ocrEngine));
        _testImagePath = testImagePath ?? throw new ArgumentNullException(nameof(testImagePath));
    }

    /// <summary>
    /// 段階的精度改善テストを実行
    /// </summary>
    public async Task<ProgressiveTestResults> RunProgressiveTestAsync(CancellationToken cancellationToken = default)
    {
        DebugLogUtility.WriteLog("🧪 段階的精度改善テスト開始");

        if (!File.Exists(_testImagePath))
        {
            throw new FileNotFoundException($"テスト画像が見つかりません: {_testImagePath}");
        }

        using var originalImage = Cv2.ImRead(_testImagePath);
        if (originalImage.Empty())
        {
            throw new InvalidOperationException($"画像を読み込めません: {_testImagePath}");
        }

        var results = new ProgressiveTestResults
        {
            OriginalImagePath = _testImagePath,
            ImageSize = $"{originalImage.Width}x{originalImage.Height}",
            TestStartTime = DateTime.Now
        };

        // 1. ベースライン（現在の前処理）
        DebugLogUtility.WriteLog("📊 ベースライン測定開始");
        var baselineResult = await TestPreprocessingMethod(originalImage, "ベースライン（PP-OCRv5標準）", 
            image => PPOCRv5Preprocessor.ProcessGameImageForV5(image), cancellationToken).ConfigureAwait(false);
        results.BaselineResult = baselineResult;

        // 2. 小さなテキスト強化
        DebugLogUtility.WriteLog("📊 小さなテキスト強化テスト開始");
        var smallTextResult = await TestPreprocessingMethod(originalImage, "小さなテキスト強化", 
            image => EnhanceSmallText(image), cancellationToken).ConfigureAwait(false);
        results.SmallTextResult = smallTextResult;

        // 3. 漢字認識強化
        DebugLogUtility.WriteLog("📊 漢字認識強化テスト開始");
        var kanjiResult = await TestPreprocessingMethod(originalImage, "漢字認識強化", 
            image => OptimizeForKanji(image), cancellationToken).ConfigureAwait(false);
        results.KanjiResult = kanjiResult;

        // 4. 低コントラスト改善
        DebugLogUtility.WriteLog("📊 低コントラスト改善テスト開始");
        var contrastResult = await TestPreprocessingMethod(originalImage, "低コントラスト改善", 
            image => ImproveContrast(image), cancellationToken).ConfigureAwait(false);
        results.ContrastResult = contrastResult;

        // 5. 全手法統合
        DebugLogUtility.WriteLog("📊 全手法統合テスト開始");
        var combinedResult = await TestPreprocessingMethod(originalImage, "全手法統合", 
            image => ApplyCombinedOptimizations(image), cancellationToken).ConfigureAwait(false);
        results.CombinedResult = combinedResult;

        results.TestEndTime = DateTime.Now;
        results.TotalTestDuration = results.TestEndTime - results.TestStartTime;

        // 結果レポート生成
        await GenerateProgressiveReportAsync(results).ConfigureAwait(false);

        DebugLogUtility.WriteLog($"✅ 段階的精度改善テスト完了: 総時間 {results.TotalTestDuration.TotalSeconds:F1}秒");
        return results;
    }

    /// <summary>
    /// 個別前処理手法のテスト実行
    /// </summary>
    private async Task<ProcessingTestResult> TestPreprocessingMethod(
        Mat originalImage, 
        string methodName, 
        Func<Mat, Mat> preprocessingMethod,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            DebugLogUtility.WriteLog($"   🔧 {methodName} 前処理開始");
            
            // 前処理実行
            using var processedImage = preprocessingMethod(originalImage);
            var preprocessingTime = stopwatch.ElapsedMilliseconds;
            
            DebugLogUtility.WriteLog($"   ✅ {methodName} 前処理完了: {preprocessingTime}ms");
            
            // デバッグ用画像保存
            var debugImagePath = SaveDebugImage(processedImage, methodName);
            
            // OCR実行
            DebugLogUtility.WriteLog($"   🤖 {methodName} OCR実行開始");
            var ocrResults = await ExecuteOcrAsync(processedImage, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            
            var recognizedText = string.Join("", ocrResults.Select(r => r.Text));
            var textRegionCount = ocrResults.Count;
            var avgConfidence = ocrResults.Count > 0 ? ocrResults.Average(r => r.Confidence) : 0.0;
            
            DebugLogUtility.WriteLog($"   ✅ {methodName} OCR完了: {textRegionCount}領域, 平均信頼度 {avgConfidence:F3}");
            DebugLogUtility.WriteLog($"   📝 認識テキスト: {recognizedText.Substring(0, Math.Min(100, recognizedText.Length))}...");
            
            return new ProcessingTestResult
            {
                MethodName = methodName,
                PreprocessingTimeMs = preprocessingTime,
                OcrTimeMs = stopwatch.ElapsedMilliseconds - preprocessingTime,
                TotalTimeMs = stopwatch.ElapsedMilliseconds,
                RecognizedText = recognizedText,
                TextRegionCount = textRegionCount,
                AverageConfidence = avgConfidence,
                DebugImagePath = debugImagePath,
                Success = true
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            DebugLogUtility.WriteLog($"   ❌ {methodName} テストエラー: {ex.Message}");
            
            return new ProcessingTestResult
            {
                MethodName = methodName,
                Success = false,
                ErrorMessage = ex.Message,
                TotalTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// 小さなテキスト強化前処理（修正版）
    /// </summary>
    private Mat EnhanceSmallText(Mat input)
    {
        DebugLogUtility.WriteLog($"      🔍 小さなテキスト強化処理開始");
        
        var output = new Mat();
        
        try
        {
            // 1. グレースケール変換
            using var grayInput = new Mat();
            if (input.Channels() == 3)
            {
                Cv2.CvtColor(input, grayInput, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                input.CopyTo(grayInput);
            }
            
            // 2. 2倍アップスケール（小さな文字を拡大）
            using var upscaled = new Mat();
            Cv2.Resize(grayInput, upscaled, new OpenCvSharp.Size(grayInput.Width * 2, grayInput.Height * 2), 
                       interpolation: InterpolationFlags.Cubic);
            
            // 3. 適応的しきい値処理（文字を鮮明化）
            using var adaptive = new Mat();
            Cv2.AdaptiveThreshold(upscaled, adaptive, 255, AdaptiveThresholdTypes.GaussianC, 
                                ThresholdTypes.Binary, 11, 2);
            
            // 4. 軽微なノイズ除去
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(2, 2));
            using var cleaned = new Mat();
            Cv2.MorphologyEx(adaptive, cleaned, MorphTypes.Close, kernel);
            
            // 5. 元サイズに戻す
            Cv2.Resize(cleaned, output, new OpenCvSharp.Size(input.Width, input.Height), 
                       interpolation: InterpolationFlags.Area);
            
            DebugLogUtility.WriteLog($"      ✅ 小さなテキスト強化完了");
            return output;
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"      ❌ 小さなテキスト強化エラー: {ex.Message}");
            output?.Dispose();
            input.CopyTo(output = new Mat());
            return output;
        }
    }

    /// <summary>
    /// 漢字認識最適化前処理
    /// </summary>
    private Mat OptimizeForKanji(Mat input)
    {
        DebugLogUtility.WriteLog($"      🔍 漢字認識最適化処理開始");
        
        var output = new Mat();
        
        try
        {
            // 1. より細かいCLAHE（漢字の細部強調）
            using var clahe = Cv2.CreateCLAHE(clipLimit: 1.8, tileGridSize: new OpenCvSharp.Size(4, 4));
            using var contrastEnhanced = new Mat();
            
            if (input.Channels() == 3)
            {
                using var lab = new Mat();
                Cv2.CvtColor(input, lab, ColorConversionCodes.BGR2Lab);
                var channels = Cv2.Split(lab);
                
                using var enhancedL = new Mat();
                clahe.Apply(channels[0], enhancedL);
                
                var enhancedChannels = new Mat[] { enhancedL, channels[1], channels[2] };
                using var enhancedLab = new Mat();
                Cv2.Merge(enhancedChannels, enhancedLab);
                Cv2.CvtColor(enhancedLab, contrastEnhanced, ColorConversionCodes.Lab2BGR);
                
                foreach (var ch in channels) ch.Dispose();
                foreach (var ch in enhancedChannels.Skip(1)) ch.Dispose();
            }
            else
            {
                clahe.Apply(input, contrastEnhanced);
            }
            
            // 2. 方向性フィルタ（漢字の縦横線強調）
            using var gray = new Mat();
            if (contrastEnhanced.Channels() == 3)
            {
                Cv2.CvtColor(contrastEnhanced, gray, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                contrastEnhanced.CopyTo(gray);
            }
            
            // 横線強調カーネル
            var kernelHorizontal = new Mat(3, 3, MatType.CV_32F);
            kernelHorizontal.Set<float>(0, 0, -1); kernelHorizontal.Set<float>(0, 1, -1); kernelHorizontal.Set<float>(0, 2, -1);
            kernelHorizontal.Set<float>(1, 0, 2);  kernelHorizontal.Set<float>(1, 1, 2);  kernelHorizontal.Set<float>(1, 2, 2);
            kernelHorizontal.Set<float>(2, 0, -1); kernelHorizontal.Set<float>(2, 1, -1); kernelHorizontal.Set<float>(2, 2, -1);
            
            // 縦線強調カーネル
            var kernelVertical = new Mat(3, 3, MatType.CV_32F);
            kernelVertical.Set<float>(0, 0, -1); kernelVertical.Set<float>(0, 1, 2); kernelVertical.Set<float>(0, 2, -1);
            kernelVertical.Set<float>(1, 0, -1); kernelVertical.Set<float>(1, 1, 2); kernelVertical.Set<float>(1, 2, -1);
            kernelVertical.Set<float>(2, 0, -1); kernelVertical.Set<float>(2, 1, 2); kernelVertical.Set<float>(2, 2, -1);
            
            using var horizontalEnhanced = new Mat();
            using var verticalEnhanced = new Mat();
            
            Cv2.Filter2D(gray, horizontalEnhanced, MatType.CV_8U, kernelHorizontal);
            Cv2.Filter2D(gray, verticalEnhanced, MatType.CV_8U, kernelVertical);
            
            // 3. 縦横線を統合
            Cv2.AddWeighted(horizontalEnhanced, 0.5, verticalEnhanced, 0.5, 0, output);
            
            DebugLogUtility.WriteLog($"      ✅ 漢字認識最適化完了");
            return output;
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"      ❌ 漢字認識最適化エラー: {ex.Message}");
            output?.Dispose();
            input.CopyTo(output = new Mat());
            return output;
        }
    }

    /// <summary>
    /// 低コントラスト改善前処理
    /// </summary>
    private Mat ImproveContrast(Mat input)
    {
        DebugLogUtility.WriteLog($"      🔍 低コントラスト改善処理開始");
        
        var output = new Mat();
        
        try
        {
            // 1. 複数スケールのCLAHE
            var clipLimits = new[] { 1.5, 2.5, 3.5 };
            var results = new List<Mat>();
            
            foreach (var limit in clipLimits)
            {
                using var clahe = Cv2.CreateCLAHE(clipLimit: limit, tileGridSize: new OpenCvSharp.Size(8, 8));
                var result = new Mat();
                
                if (input.Channels() == 3)
                {
                    using var lab = new Mat();
                    Cv2.CvtColor(input, lab, ColorConversionCodes.BGR2Lab);
                    var channels = Cv2.Split(lab);
                    
                    using var enhancedL = new Mat();
                    clahe.Apply(channels[0], enhancedL);
                    
                    var enhancedChannels = new Mat[] { enhancedL, channels[1], channels[2] };
                    using var enhancedLab = new Mat();
                    Cv2.Merge(enhancedChannels, enhancedLab);
                    Cv2.CvtColor(enhancedLab, result, ColorConversionCodes.Lab2BGR);
                    
                    foreach (var ch in channels) ch.Dispose();
                    foreach (var ch in enhancedChannels.Skip(1)) ch.Dispose();
                }
                else
                {
                    clahe.Apply(input, result);
                }
                
                results.Add(result);
            }
            
            // 2. 最適結果を選択（簡易版：中間値を使用）
            results[1].CopyTo(output);
            
            // リソース解放
            foreach (var result in results)
            {
                result.Dispose();
            }
            
            DebugLogUtility.WriteLog($"      ✅ 低コントラスト改善完了");
            return output;
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"      ❌ 低コントラスト改善エラー: {ex.Message}");
            output?.Dispose();
            input.CopyTo(output = new Mat());
            return output;
        }
    }

    /// <summary>
    /// 全手法統合前処理
    /// </summary>
    private Mat ApplyCombinedOptimizations(Mat input)
    {
        DebugLogUtility.WriteLog($"      🔍 全手法統合処理開始");
        
        try
        {
            // 1. 低コントラスト改善
            using var contrastImproved = ImproveContrast(input);
            
            // 2. 漢字認識最適化
            using var kanjiOptimized = OptimizeForKanji(contrastImproved);
            
            // 3. 小さなテキスト強化
            var smallTextEnhanced = EnhanceSmallText(kanjiOptimized);
            
            DebugLogUtility.WriteLog($"      ✅ 全手法統合完了");
            return smallTextEnhanced;
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"      ❌ 全手法統合エラー: {ex.Message}");
            var fallback = new Mat();
            input.CopyTo(fallback);
            return fallback;
        }
    }

    /// <summary>
    /// デバッグ画像保存
    /// </summary>
    private string SaveDebugImage(Mat image, string methodName)
    {
        try
        {
            var fileName = $"debug_optimization_{methodName.Replace(" ", "_").Replace("（", "_").Replace("）", "_")}.png";
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
            Cv2.ImWrite(filePath, image);
            DebugLogUtility.WriteLog($"      💾 デバッグ画像保存: {fileName}");
            return filePath;
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"      ⚠️ デバッグ画像保存失敗: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// OCR実行（既存システム利用）
    /// </summary>
    private async Task<IReadOnlyList<OcrTextRegion>> ExecuteOcrAsync(Mat image, CancellationToken cancellationToken)
    {
        var tempImagePath = Path.GetTempFileName() + ".png";
        try
        {
            Cv2.ImWrite(tempImagePath, image);
            var imageWrapper = new TempImageWrapper(tempImagePath, image.Width, image.Height);
            var ocrResults = await _ocrEngine.RecognizeAsync(imageWrapper, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ocrResults.TextRegions;
        }
        finally
        {
            if (File.Exists(tempImagePath))
            {
                File.Delete(tempImagePath);
            }
        }
    }

    /// <summary>
    /// レポート生成
    /// </summary>
    private async Task GenerateProgressiveReportAsync(ProgressiveTestResults results)
    {
        var reportPath = Path.Combine(Directory.GetCurrentDirectory(), "progressive_accuracy_report.md");
        
        var report = new StringBuilder();
        report.AppendLine("# OCR前処理最適化 段階的効果測定レポート");
        report.AppendLine();
        report.AppendLine($"**テスト実行日時**: {results.TestStartTime:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"**テスト画像**: {Path.GetFileName(results.OriginalImagePath)}");
        report.AppendLine($"**画像サイズ**: {results.ImageSize}");
        report.AppendLine($"**総テスト時間**: {results.TotalTestDuration.TotalSeconds:F1}秒");
        report.AppendLine();

        // 結果比較テーブル
        report.AppendLine("## 手法別性能比較");
        report.AppendLine();
        report.AppendLine("| 手法 | 処理時間 | OCR時間 | テキスト領域数 | 平均信頼度 | 認識文字数 |");
        report.AppendLine("|------|----------|---------|----------------|------------|------------|");
        
        var allResults = new[]
        {
            results.BaselineResult,
            results.SmallTextResult,
            results.KanjiResult,
            results.ContrastResult,
            results.CombinedResult
        };

        foreach (var result in allResults)
        {
            if (result.Success)
            {
                report.AppendLine($"| {result.MethodName} | {result.PreprocessingTimeMs}ms | {result.OcrTimeMs}ms | {result.TextRegionCount} | {result.AverageConfidence:F3} | {result.RecognizedText.Length} |");
            }
            else
            {
                report.AppendLine($"| {result.MethodName} | エラー | - | - | - | - |");
            }
        }

        report.AppendLine();
        report.AppendLine("## 認識テキスト詳細");
        report.AppendLine();

        foreach (var result in allResults)
        {
            if (result.Success)
            {
                report.AppendLine($"### {result.MethodName}");
                report.AppendLine("```");
                report.AppendLine(result.RecognizedText);
                report.AppendLine("```");
                report.AppendLine();
            }
        }

        await File.WriteAllTextAsync(reportPath, report.ToString()).ConfigureAwait(false);
        DebugLogUtility.WriteLog($"📊 段階的効果測定レポート生成: {reportPath}");
    }
}

/// <summary>
/// 段階的テスト結果
/// </summary>
public class ProgressiveTestResults
{
    public string OriginalImagePath { get; set; } = string.Empty;
    public string ImageSize { get; set; } = string.Empty;
    public DateTime TestStartTime { get; set; }
    public DateTime TestEndTime { get; set; }
    public TimeSpan TotalTestDuration { get; set; }
    
    public ProcessingTestResult BaselineResult { get; set; } = new();
    public ProcessingTestResult SmallTextResult { get; set; } = new();
    public ProcessingTestResult KanjiResult { get; set; } = new();
    public ProcessingTestResult ContrastResult { get; set; } = new();
    public ProcessingTestResult CombinedResult { get; set; } = new();
}

/// <summary>
/// 個別処理テスト結果
/// </summary>
public class ProcessingTestResult
{
    public string MethodName { get; set; } = string.Empty;
    public long PreprocessingTimeMs { get; set; }
    public long OcrTimeMs { get; set; }
    public long TotalTimeMs { get; set; }
    public string RecognizedText { get; set; } = string.Empty;
    public int TextRegionCount { get; set; }
    public double AverageConfidence { get; set; }
    public string DebugImagePath { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

