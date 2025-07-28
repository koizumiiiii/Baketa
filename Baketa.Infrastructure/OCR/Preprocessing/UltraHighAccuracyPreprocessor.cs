using OpenCvSharp;
using Baketa.Core.Utilities;

namespace Baketa.Infrastructure.OCR.Preprocessing;

/// <summary>
/// 100%認識精度を目指す超高精度前処理システム
/// 特に低コントラスト・複雑な日本語テキストに特化
/// </summary>
public static class UltraHighAccuracyPreprocessor
{
    /// <summary>
    /// 超高精度前処理実行（低コントラスト特化）
    /// </summary>
    public static Mat ProcessForUltraAccuracy(Mat input)
    {
        DebugLogUtility.WriteLog("🎯 超高精度前処理開始");
        
        try
        {
            // 1. 極精密グレースケール変換
            using var gray = ConvertToOptimalGrayscale(input);
            
            // 2. ノイズ除去（最高品質）
            using var denoised = UltraDenoising(gray);
            
            // 3. 適応的超精密コントラスト強化
            using var enhanced = UltraContrastEnhancement(denoised);
            
            // 4. 文字形状最適化
            using var optimized = OptimizeCharacterShapes(enhanced);
            
            // 5. ひらがな・漢字特化強化
            using var japanese = EnhanceJapaneseCharacters(optimized);
            
            // 6. 最終シャープネス調整
            var final = FinalSharpnessOptimization(japanese);
            
            DebugLogUtility.WriteLog("✅ 超高精度前処理完了");
            return final;
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"❌ 超高精度前処理エラー: {ex.Message}");
            var fallback = new Mat();
            input.CopyTo(fallback);
            return fallback;
        }
    }

    /// <summary>
    /// 極精密グレースケール変換（輝度最適化）
    /// </summary>
    private static Mat ConvertToOptimalGrayscale(Mat input)
    {
        var output = new Mat();
        
        if (input.Channels() == 3)
        {
            // カスタム重み付きグレースケール（日本語テキスト最適化）
            var channels = Cv2.Split(input);
            using var weighted = new Mat();
            
            // 青チャンネルを重視（白文字の視認性向上）
            Cv2.AddWeighted(channels[0], 0.4, channels[1], 0.3, 0, weighted); // B + G
            using var temp = new Mat();
            Cv2.AddWeighted(weighted, 1.0, channels[2], 0.3, 0, temp); // + R
            
            temp.CopyTo(output);
            
            foreach (var ch in channels) ch.Dispose();
        }
        else
        {
            input.CopyTo(output);
        }
        
        return output;
    }

    /// <summary>
    /// 超精密ノイズ除去
    /// </summary>
    private static Mat UltraDenoising(Mat input)
    {
        var output = new Mat();
        
        // 1. バイラテラルフィルタ（エッジ保持）
        using var bilateral = new Mat();
        Cv2.BilateralFilter(input, bilateral, d: 15, sigmaColor: 80, sigmaSpace: 80);
        
        // 2. ガウシアンブラー（微細ノイズ除去）
        using var gaussian = new Mat();
        Cv2.GaussianBlur(bilateral, gaussian, new OpenCvSharp.Size(3, 3), 0.5);
        
        // 3. 非局所平均デノイジング  
        Cv2.FastNlMeansDenoising(gaussian, output, h: 10, templateWindowSize: 7, searchWindowSize: 21);
        
        return output;
    }

    /// <summary>
    /// 適応的超精密コントラスト強化
    /// </summary>
    private static Mat UltraContrastEnhancement(Mat input)
    {
        var output = new Mat();
        
        // 1. マルチスケールCLAHE
        var results = new List<Mat>();
        var clipLimits = new[] { 1.0, 2.0, 3.0, 4.0 };
        var tileSizes = new[] { 
            new OpenCvSharp.Size(4, 4), 
            new OpenCvSharp.Size(8, 8), 
            new OpenCvSharp.Size(16, 16) 
        };
        
        foreach (var limit in clipLimits)
        {
            foreach (var tileSize in tileSizes)
            {
                using var clahe = Cv2.CreateCLAHE(clipLimit: limit, tileGridSize: tileSize);
                var result = new Mat();
                clahe.Apply(input, result);
                results.Add(result);
            }
        }
        
        // 2. 最適結果の選択（ヒストグラム分析ベース）
        var bestResult = SelectBestContrastResult(results, input);
        bestResult.CopyTo(output);
        
        // リソース解放
        foreach (var result in results)
        {
            result.Dispose();
        }
        
        return output;
    }

    /// <summary>
    /// 最適なコントラスト結果を選択
    /// </summary>
    private static Mat SelectBestContrastResult(List<Mat> results, Mat _)
    {
        var bestScore = 0.0;
        var bestIndex = 0;
        
        for (int i = 0; i < results.Count; i++)
        {
            // コントラスト評価（標準偏差ベース）
            var mean = new Scalar();
            var stddev = new Scalar();
            Cv2.MeanStdDev(results[i], out mean, out stddev);
            
            // エッジ密度評価
            using var edges = new Mat();
            Cv2.Canny(results[i], edges, 50, 150);
            var edgeDensity = Cv2.CountNonZero(edges) / (double)(edges.Rows * edges.Cols);
            
            // 総合スコア（コントラスト + エッジ品質）
            var score = stddev.Val0 * 0.7 + edgeDensity * 1000 * 0.3;
            
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }
        
        return results[bestIndex];
    }

    /// <summary>
    /// 文字形状最適化
    /// </summary>
    private static Mat OptimizeCharacterShapes(Mat input)
    {
        var output = new Mat();
        
        // 1. 適応的二値化（文字形状強調）
        using var adaptive = new Mat();
        Cv2.AdaptiveThreshold(input, adaptive, 
            maxValue: 255,
            adaptiveMethod: AdaptiveThresholdTypes.GaussianC,
            thresholdType: ThresholdTypes.Binary,
            blockSize: 11,
            c: 2);
        
        // 2. モルフォロジー操作（文字の連結性改善）
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(2, 2));
        
        // 開放演算：ノイズ除去
        using var opened = new Mat();
        Cv2.MorphologyEx(adaptive, opened, MorphTypes.Open, kernel);
        
        // 閉鎖演算：文字の隙間埋め
        Cv2.MorphologyEx(opened, output, MorphTypes.Close, kernel);
        
        return output;
    }

    /// <summary>
    /// ひらがな・漢字特化強化
    /// </summary>
    private static Mat EnhanceJapaneseCharacters(Mat input)
    {
        var output = new Mat();
        
        // 1. 方向性フィルタ（漢字の縦横線強調）
        var kernelHorizontal = new Mat(3, 3, MatType.CV_32F);
        kernelHorizontal.Set<float>(0, 0, -1); kernelHorizontal.Set<float>(0, 1, -1); kernelHorizontal.Set<float>(0, 2, -1);
        kernelHorizontal.Set<float>(1, 0, 3);  kernelHorizontal.Set<float>(1, 1, 3);  kernelHorizontal.Set<float>(1, 2, 3);
        kernelHorizontal.Set<float>(2, 0, -1); kernelHorizontal.Set<float>(2, 1, -1); kernelHorizontal.Set<float>(2, 2, -1);
        
        var kernelVertical = new Mat(3, 3, MatType.CV_32F);
        kernelVertical.Set<float>(0, 0, -1); kernelVertical.Set<float>(0, 1, 3); kernelVertical.Set<float>(0, 2, -1);
        kernelVertical.Set<float>(1, 0, -1); kernelVertical.Set<float>(1, 1, 3); kernelVertical.Set<float>(1, 2, -1);
        kernelVertical.Set<float>(2, 0, -1); kernelVertical.Set<float>(2, 1, 3); kernelVertical.Set<float>(2, 2, -1);
        
        // 2. 曲線強調（ひらがな対応）
        var kernelCurve = new Mat(3, 3, MatType.CV_32F);
        kernelCurve.Set<float>(0, 0, 0); kernelCurve.Set<float>(0, 1, -1); kernelCurve.Set<float>(0, 2, 0);
        kernelCurve.Set<float>(1, 0, -1); kernelCurve.Set<float>(1, 1, 5); kernelCurve.Set<float>(1, 2, -1);
        kernelCurve.Set<float>(2, 0, 0); kernelCurve.Set<float>(2, 1, -1); kernelCurve.Set<float>(2, 2, 0);
        
        using var horizontal = new Mat();
        using var vertical = new Mat();
        using var curve = new Mat();
        
        Cv2.Filter2D(input, horizontal, MatType.CV_8U, kernelHorizontal);
        Cv2.Filter2D(input, vertical, MatType.CV_8U, kernelVertical);
        Cv2.Filter2D(input, curve, MatType.CV_8U, kernelCurve);
        
        // 3. 統合（重み付き合成）
        using var combined = new Mat();
        Cv2.AddWeighted(horizontal, 0.3, vertical, 0.3, 0, combined);
        Cv2.AddWeighted(combined, 0.8, curve, 0.4, 0, output);
        
        return output;
    }

    /// <summary>
    /// 最終シャープネス最適化
    /// </summary>
    private static Mat FinalSharpnessOptimization(Mat input)
    {
        var output = new Mat();
        
        // 1. ラプラシアンシャープニング
        using var laplacian = new Mat();
        Cv2.Laplacian(input, laplacian, MatType.CV_64F, ksize: 3);
        
        using var laplacianNormalized = new Mat();
        laplacian.ConvertTo(laplacianNormalized, MatType.CV_8U);
        
        // 2. アンシャープマスク
        using var blurred = new Mat();
        Cv2.GaussianBlur(input, blurred, new OpenCvSharp.Size(3, 3), 1.0);
        
        using var unsharpMask = new Mat();
        Cv2.AddWeighted(input, 1.5, blurred, -0.5, 0, unsharpMask);
        
        // 3. 最終統合
        Cv2.AddWeighted(unsharpMask, 0.8, laplacianNormalized, 0.2, 0, output);
        
        return output;
    }

    /// <summary>
    /// 極精密前処理（全手法統合 + 超高精度特化）
    /// </summary>
    public static Mat ProcessForPerfectAccuracy(Mat input)
    {
        DebugLogUtility.WriteLog("🎯 100%精度前処理開始");
        
        try
        {
            // 1. 基本的なPP-OCRv5最適化
            using var v5Optimized = PPOCRv5Preprocessor.ProcessForPPOCRv5(input, OptimizationMode.Combined);
            
            // 2. 超高精度特化処理
            var ultraProcessed = ProcessForUltraAccuracy(v5Optimized);
            
            DebugLogUtility.WriteLog("✅ 100%精度前処理完了");
            return ultraProcessed;
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"❌ 100%精度前処理エラー: {ex.Message}");
            var fallback = new Mat();
            input.CopyTo(fallback);
            return fallback;
        }
    }
}