using OpenCvSharp;
using Baketa.Core.Utilities;

namespace Baketa.Infrastructure.OCR.Preprocessing;

/// <summary>
/// 最適化モード
/// </summary>
public enum OptimizationMode
{
    /// <summary>標準モード</summary>
    Standard,
    /// <summary>漢字認識強化</summary>
    KanjiEnhanced,
    /// <summary>コントラスト改善</summary>
    ContrastEnhanced,
    /// <summary>小さなテキスト強化</summary>
    SmallTextEnhanced,
    /// <summary>全手法統合</summary>
    Combined,
    /// <summary>100%精度特化（超高精度）</summary>
    UltraHighAccuracy,
    /// <summary>極限精度（全手法 + 超高精度）</summary>
    PerfectAccuracy
}

/// <summary>
/// PP-OCRv5モデル専用の画像前処理パイプライン
/// v5の高精度・高速性能を最大限引き出すための最適化
/// </summary>
public static class PPOCRv5Preprocessor
{
    /// <summary>
    /// PP-OCRv5モデル向け最適化前処理
    /// V5の特性に合わせた高品質・高速処理
    /// </summary>
    /// <param name="input">入力画像</param>
    /// <returns>V5最適化済み画像</returns>
    public static Mat ProcessForPPOCRv5(Mat input)
    {
        return ProcessForPPOCRv5(input, OptimizationMode.Standard);
    }

    /// <summary>
    /// PP-OCRv5モデル向け最適化前処理（適応的処理）
    /// 画像特性を自動分析して最適な前処理を選択
    /// </summary>
    /// <param name="input">入力画像</param>
    /// <returns>V5最適化済み画像</returns>
    public static Mat ProcessForPPOCRv5Adaptive(Mat input)
    {
        if (input == null || input.Empty())
            return new Mat();

        try
        {
            // 画像特性を自動分析
            var characteristics = ImageCharacteristicsAnalyzer.AnalyzeImage(input);
            
            // 分析結果に基づいて適応的処理
            if (characteristics.IsBrightBackground)
            {
                return ProcessBrightGameImage(input, characteristics.RecommendedMode);
            }
            else if (characteristics.IsDarkBackground)
            {
                return ProcessDarkGameImage(input, characteristics.RecommendedMode);
            }
            else
            {
                // 中間明度の場合は従来の処理
                return ProcessForPPOCRv5(input, characteristics.RecommendedMode);
            }
        }
        catch (Exception)
        {
            // エラー時は標準処理にフォールバック
            return ProcessForPPOCRv5(input, OptimizationMode.Standard);
        }
    }

    /// <summary>
    /// PP-OCRv5モデル向け最適化前処理（最適化モード指定）
    /// V5の特性に合わせた高品質・高速処理
    /// </summary>
    /// <param name="input">入力画像</param>
    /// <param name="mode">最適化モード</param>
    /// <returns>V5最適化済み画像</returns>
    public static Mat ProcessForPPOCRv5(Mat input, OptimizationMode mode)
    {
        if (input == null || input.Empty())
        {
            throw new ArgumentException("入力画像が無効です", nameof(input));
        }

        Console.WriteLine($"🚀 PP-OCRv5専用前処理開始: {input.Width}x{input.Height}, モード: {mode}");
        
        var processed = new Mat();
        
        try
        {
            // モードに応じた最適化処理を選択
            switch (mode)
            {
                case OptimizationMode.KanjiEnhanced:
                    return ProcessWithKanjiOptimization(input);
                    
                case OptimizationMode.ContrastEnhanced:
                    return ProcessWithContrastOptimization(input);
                    
                case OptimizationMode.SmallTextEnhanced:
                    return ProcessWithSmallTextOptimization(input);
                    
                case OptimizationMode.Combined:
                    return ProcessWithCombinedOptimization(input);
                    
                case OptimizationMode.UltraHighAccuracy:
                    return UltraHighAccuracyPreprocessor.ProcessForUltraAccuracy(input);
                    
                case OptimizationMode.PerfectAccuracy:
                    return UltraHighAccuracyPreprocessor.ProcessForPerfectAccuracy(input);
                    
                default: // Standard
                    break;
            }
            
            // 標準処理
            // 1. V5専用適応的コントラスト強化（高精度）
            var contrastOptimized = EnhanceContrastForV5(input);
            
            // 2. V5専用高周波ノイズ除去
            var denoised = RemoveHighFrequencyNoiseForV5(contrastOptimized);
            
            // 3. V5多言語対応テキスト強調
            var textEnhanced = EnhanceMultilingualTextForV5(denoised);
            
            // 4. V5専用シャープネス最適化
            var sharpened = OptimizeSharpnessForV5(textEnhanced);
            
            // 5. V5高速処理向け最終調整
            var finalResult = ApplyV5FinalOptimization(sharpened);
            
            finalResult.CopyTo(processed);
            
            // リソース解放
            contrastOptimized.Dispose();
            denoised.Dispose();
            textEnhanced.Dispose();
            sharpened.Dispose();
            finalResult.Dispose();
            
            Console.WriteLine($"✅ PP-OCRv5専用前処理完了");
            return processed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ PP-OCRv5前処理エラー: {ex.Message}");
            processed?.Dispose();
            
            // エラー時は元画像を返す
            var fallback = new Mat();
            input.CopyTo(fallback);
            return fallback;
        }
    }
    
    /// <summary>
    /// PP-OCRv5専用適応的コントラスト強化
    /// V5の高精度認識に最適化された控えめなコントラスト調整
    /// </summary>
    private static Mat EnhanceContrastForV5(Mat input)
    {
        Console.WriteLine($"   🔆 PP-OCRv5コントラスト強化開始");
        
        var output = new Mat();
        
        try
        {
            // V5専用CLAHE設定：より控えめで精密
            using var clahe = Cv2.CreateCLAHE(clipLimit: 2.0, tileGridSize: new OpenCvSharp.Size(8, 8));
            
            if (input.Channels() == 3)
            {
                // カラー画像：Lab色空間でL成分のみを精密処理
                using var lab = new Mat();
                using var enhancedL = new Mat();
                
                Cv2.CvtColor(input, lab, ColorConversionCodes.BGR2Lab);
                var channels = Cv2.Split(lab);
                
                // V5では細やかなコントラスト調整が効果的
                clahe.Apply(channels[0], enhancedL);
                
                // L成分を置き換えて統合
                var enhancedChannels = new Mat[] { enhancedL, channels[1], channels[2] };
                using var enhancedLab = new Mat();
                Cv2.Merge(enhancedChannels, enhancedLab);
                Cv2.CvtColor(enhancedLab, output, ColorConversionCodes.Lab2BGR);
                
                // リソース解放
                foreach (var ch in channels) ch.Dispose();
                foreach (var ch in enhancedChannels.Skip(1)) ch.Dispose();
            }
            else
            {
                // グレースケール：直接適用
                clahe.Apply(input, output);
            }
            
            Console.WriteLine($"   ✅ PP-OCRv5コントラスト強化完了");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ V5コントラスト強化エラー: {ex.Message}");
            input.CopyTo(output);
        }
        
        return output;
    }
    
    /// <summary>
    /// PP-OCRv5専用高周波ノイズ除去
    /// V5の高感度認識に対応した精密ノイズ除去
    /// </summary>
    private static Mat RemoveHighFrequencyNoiseForV5(Mat input)
    {
        Console.WriteLine($"   🎯 PP-OCRv5ノイズ除去開始");
        
        var output = new Mat();
        
        try
        {
            // V5専用：エッジ保持を重視したバイラテラルフィルタ
            // より大きなカーネルサイズでV5の高精度認識をサポート
            Cv2.BilateralFilter(input, output, d: 9, sigmaColor: 50, sigmaSpace: 50);
            
            // V5向け微細ノイズ除去：ガウシアンブラーを強めに
            using var temp = new Mat();
            output.CopyTo(temp);
            Cv2.GaussianBlur(temp, output, new OpenCvSharp.Size(5, 5), 0.8);
            
            Console.WriteLine($"   ✅ PP-OCRv5ノイズ除去完了");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ V5ノイズ除去エラー: {ex.Message}");
            input.CopyTo(output);
        }
        
        return output;
    }
    
    /// <summary>
    /// PP-OCRv5多言語対応テキスト強調
    /// V5の多言語同時認識機能に最適化
    /// </summary>
    private static Mat EnhanceMultilingualTextForV5(Mat input)
    {
        Console.WriteLine($"   🌍 PP-OCRv5多言語テキスト強調開始");
        
        var output = new Mat();
        
        try
        {
            // グレースケール変換
            using var gray = new Mat();
            if (input.Channels() == 3)
            {
                Cv2.CvtColor(input, gray, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                input.CopyTo(gray);
            }
            
            // V5専用：多言語対応適応的二値化
            // ブロックサイズを大きくして多様な文字サイズに対応
            using var binary = new Mat();
            Cv2.AdaptiveThreshold(gray, binary, 
                maxValue: 255,
                adaptiveMethod: AdaptiveThresholdTypes.GaussianC,
                thresholdType: ThresholdTypes.Binary,
                blockSize: 15,  // V5用：大きなブロックサイズ
                c: 3);           // V5用：高めのC値
            
            // V5専用：多言語文字形状に対応したモルフォロジー
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(3, 3));
            using var cleaned = new Mat();
            
            // 開放演算：ノイズ除去
            Cv2.MorphologyEx(binary, cleaned, MorphTypes.Open, kernel);
            
            // 閉鎖演算：文字の隙間埋め（V5では強めに）
            using var strongKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(2, 2));
            Cv2.MorphologyEx(cleaned, output, MorphTypes.Close, strongKernel);
            
            Console.WriteLine($"   ✅ PP-OCRv5多言語テキスト強調完了");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ V5多言語テキスト強調エラー: {ex.Message}");
            input.CopyTo(output);
        }
        
        return output;
    }
    
    /// <summary>
    /// PP-OCRv5専用シャープネス最適化
    /// V5の高速処理に合わせた効率的シャープネス強化
    /// </summary>
    private static Mat OptimizeSharpnessForV5(Mat input)
    {
        Console.WriteLine($"   ✨ PP-OCRv5シャープネス最適化開始");
        
        var output = new Mat();
        
        try
        {
            // V5専用：高精度Laplacianエッジ検出
            using var laplacian = new Mat();
            Cv2.Laplacian(input, laplacian, MatType.CV_64F, ksize: 3);
            
            // エッジ情報を正規化
            using var laplacianNormalized = new Mat();
            laplacian.ConvertTo(laplacianNormalized, MatType.CV_8U);
            
            // V5専用：控えめなエッジ統合（高速処理重視）
            Cv2.AddWeighted(input, 0.85, laplacianNormalized, 0.15, 0, output);
            
            Console.WriteLine($"   ✅ PP-OCRv5シャープネス最適化完了");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ V5シャープネス最適化エラー: {ex.Message}");
            input.CopyTo(output);
        }
        
        return output;
    }
    
    /// <summary>
    /// PP-OCRv5高速処理向け最終調整
    /// V5の高速性能とバランスを取った最終最適化
    /// </summary>
    private static Mat ApplyV5FinalOptimization(Mat input)
    {
        Console.WriteLine($"   🌟 PP-OCRv5最終最適化開始");
        
        var output = new Mat();
        
        try
        {
            // V5専用：高速アンシャープマスク
            using var blurred = new Mat();
            using var unsharpMask = new Mat();
            
            // V5では軽微なブラーで高速化
            Cv2.GaussianBlur(input, blurred, new OpenCvSharp.Size(3, 3), 0.8);
            Cv2.AddWeighted(input, 1.3, blurred, -0.3, 0, unsharpMask);
            
            // V5専用：控えめなコントラスト調整（高速処理維持）
            unsharpMask.ConvertTo(output, MatType.CV_8U, alpha: 1.05, beta: 3);
            
            Console.WriteLine($"   ✅ PP-OCRv5最終最適化完了");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ V5最終最適化エラー: {ex.Message}");
            input.CopyTo(output);
        }
        
        return output;
    }
    
    /// <summary>
    /// ゲーム画面向けPP-OCRv5最適化処理
    /// ゲーム特化前処理とV5専用処理を組み合わせた最高品質処理
    /// </summary>
    public static Mat ProcessGameImageForV5(Mat input)
    {
        Console.WriteLine($"🎮🚀 ゲーム画面PP-OCRv5専用処理開始");
        
        try
        {
            // 1. ゲーム特化前処理を軽量化して適用
            using var gameProcessed = GameTextPreprocessor.ProcessGameImage(input);
            
            // 2. PP-OCRv5専用最適化を追加適用
            var v5Optimized = ProcessForPPOCRv5(gameProcessed);
            
            Console.WriteLine($"✅ ゲーム画面PP-OCRv5専用処理完了");
            return v5Optimized;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ ゲーム画面V5処理エラー: {ex.Message}");
            
            // エラー時は元画像を返す
            var fallback = new Mat();
            input.CopyTo(fallback);
            return fallback;
        }
    }

    /// <summary>
    /// 漢字認識最適化処理
    /// </summary>
    private static Mat ProcessWithKanjiOptimization(Mat input)
    {
        Console.WriteLine($"🔍 漢字認識最適化処理開始");
        
        var output = new Mat();
        try
        {
            // 細かいCLAHE（漢字の細部強調）
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
            
            // 方向性フィルタ（漢字の縦横線強調）
            using var gray = new Mat();
            if (contrastEnhanced.Channels() == 3)
            {
                Cv2.CvtColor(contrastEnhanced, gray, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                contrastEnhanced.CopyTo(gray);
            }
            
            // 横線・縦線強調カーネル
            var kernelHorizontal = new Mat(3, 3, MatType.CV_32F);
            kernelHorizontal.Set<float>(0, 0, -1); kernelHorizontal.Set<float>(0, 1, -1); kernelHorizontal.Set<float>(0, 2, -1);
            kernelHorizontal.Set<float>(1, 0, 2);  kernelHorizontal.Set<float>(1, 1, 2);  kernelHorizontal.Set<float>(1, 2, 2);
            kernelHorizontal.Set<float>(2, 0, -1); kernelHorizontal.Set<float>(2, 1, -1); kernelHorizontal.Set<float>(2, 2, -1);
            
            var kernelVertical = new Mat(3, 3, MatType.CV_32F);
            kernelVertical.Set<float>(0, 0, -1); kernelVertical.Set<float>(0, 1, 2); kernelVertical.Set<float>(0, 2, -1);
            kernelVertical.Set<float>(1, 0, -1); kernelVertical.Set<float>(1, 1, 2); kernelVertical.Set<float>(1, 2, -1);
            kernelVertical.Set<float>(2, 0, -1); kernelVertical.Set<float>(2, 1, 2); kernelVertical.Set<float>(2, 2, -1);
            
            using var horizontalEnhanced = new Mat();
            using var verticalEnhanced = new Mat();
            
            Cv2.Filter2D(gray, horizontalEnhanced, MatType.CV_8U, kernelHorizontal);
            Cv2.Filter2D(gray, verticalEnhanced, MatType.CV_8U, kernelVertical);
            
            // 統合
            Cv2.AddWeighted(horizontalEnhanced, 0.5, verticalEnhanced, 0.5, 0, output);
            
            Console.WriteLine($"✅ 漢字認識最適化完了");
            return output;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 漢字認識最適化エラー: {ex.Message}");
            output?.Dispose();
            input.CopyTo(output = new Mat());
            return output;
        }
    }

    /// <summary>
    /// コントラスト改善最適化処理
    /// </summary>
    private static Mat ProcessWithContrastOptimization(Mat input)
    {
        Console.WriteLine($"🔍 コントラスト改善最適化処理開始");
        
        var output = new Mat();
        try
        {
            // 複数スケールCLAHE
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
            
            // 中間値を使用
            results[1].CopyTo(output);
            
            // リソース解放
            foreach (var result in results)
            {
                result.Dispose();
            }
            
            Console.WriteLine($"✅ コントラスト改善最適化完了");
            return output;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ コントラスト改善最適化エラー: {ex.Message}");
            output?.Dispose();
            input.CopyTo(output = new Mat());
            return output;
        }
    }

    /// <summary>
    /// 小さなテキスト強化最適化処理
    /// </summary>
    private static Mat ProcessWithSmallTextOptimization(Mat input)
    {
        Console.WriteLine($"🔍 小さなテキスト強化最適化処理開始");
        
        var output = new Mat();
        try
        {
            // グレースケール変換
            using var grayInput = new Mat();
            if (input.Channels() == 3)
            {
                Cv2.CvtColor(input, grayInput, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                input.CopyTo(grayInput);
            }
            
            // 2倍アップスケール
            using var upscaled = new Mat();
            Cv2.Resize(grayInput, upscaled, new OpenCvSharp.Size(grayInput.Width * 2, grayInput.Height * 2), 
                       interpolation: InterpolationFlags.Cubic);
            
            // 適応的しきい値処理
            using var adaptive = new Mat();
            Cv2.AdaptiveThreshold(upscaled, adaptive, 255, AdaptiveThresholdTypes.GaussianC, 
                                ThresholdTypes.Binary, 11, 2);
            
            // 軽微なノイズ除去
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(2, 2));
            using var cleaned = new Mat();
            Cv2.MorphologyEx(adaptive, cleaned, MorphTypes.Close, kernel);
            
            // 元サイズに戻す
            Cv2.Resize(cleaned, output, new OpenCvSharp.Size(input.Width, input.Height), 
                       interpolation: InterpolationFlags.Area);
            
            Console.WriteLine($"✅ 小さなテキスト強化最適化完了");
            return output;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 小さなテキスト強化最適化エラー: {ex.Message}");
            output?.Dispose();
            input.CopyTo(output = new Mat());
            return output;
        }
    }

    /// <summary>
    /// 全手法統合最適化処理
    /// </summary>
    private static Mat ProcessWithCombinedOptimization(Mat input)
    {
        Console.WriteLine($"🔍 全手法統合最適化処理開始");
        
        try
        {
            // 1. コントラスト改善
            using var contrastImproved = ProcessWithContrastOptimization(input);
            
            // 2. 漢字認識最適化
            using var kanjiOptimized = ProcessWithKanjiOptimization(contrastImproved);
            
            // 3. 小さなテキスト強化
            var smallTextEnhanced = ProcessWithSmallTextOptimization(kanjiOptimized);
            
            Console.WriteLine($"✅ 全手法統合最適化完了");
            return smallTextEnhanced;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 全手法統合最適化エラー: {ex.Message}");
            var fallback = new Mat();
            input.CopyTo(fallback);
            return fallback;
        }
    }

    /// <summary>
    /// 明るいゲーム画像専用前処理
    /// 黄色背景・高コントラスト環境に最適化
    /// </summary>
    /// <param name="input">入力画像</param>
    /// <param name="mode">最適化モード</param>
    /// <returns>明るい画像最適化済み画像</returns>
    public static Mat ProcessBrightGameImage(Mat input, OptimizationMode mode)
    {
        if (input == null || input.Empty())
            return new Mat();

        try
        {
            return mode switch
            {
                OptimizationMode.SmallTextEnhanced => ProcessBrightSmallText(input),
                OptimizationMode.ContrastEnhanced => ProcessBrightContrast(input),
                OptimizationMode.UltraHighAccuracy => ProcessBrightUltraAccuracy(input),
                OptimizationMode.PerfectAccuracy => ProcessBrightPerfectAccuracy(input),
                _ => ProcessBrightStandard(input)
            };
        }
        catch (Exception)
        {
            // エラー時は標準処理
            return ProcessBrightStandard(input);
        }
    }

    /// <summary>
    /// 暗いゲーム画像専用前処理
    /// 洞窟・低コントラスト環境に最適化
    /// </summary>
    /// <param name="input">入力画像</param>
    /// <param name="mode">最適化モード</param>
    /// <returns>暗い画像最適化済み画像</returns>
    public static Mat ProcessDarkGameImage(Mat input, OptimizationMode mode)
    {
        if (input == null || input.Empty())
            return new Mat();

        try
        {
            return mode switch
            {
                OptimizationMode.KanjiEnhanced => ProcessDarkKanji(input),
                OptimizationMode.ContrastEnhanced => ProcessDarkContrast(input),
                OptimizationMode.UltraHighAccuracy => ProcessDarkUltraAccuracy(input),
                OptimizationMode.PerfectAccuracy => ProcessDarkPerfectAccuracy(input),
                _ => ProcessDarkStandard(input)
            };
        }
        catch (Exception)
        {
            // エラー時は標準処理
            return ProcessDarkStandard(input);
        }
    }

    #region 明るい画像専用処理メソッド

    /// <summary>
    /// 明るい画像の標準処理
    /// </summary>
    private static Mat ProcessBrightStandard(Mat input)
    {
        var output = new Mat();
        try
        {
            // 1. 軽量グレースケール変換
            using var gray = new Mat();
            if (input.Channels() == 3)
            {
                Cv2.CvtColor(input, gray, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                input.CopyTo(gray);
            }

            // 2. 明るい画像用軽量CLAHE（clipLimit控えめ）
            using var clahe = Cv2.CreateCLAHE(clipLimit: 1.5, tileGridSize: new OpenCvSharp.Size(8, 8));
            using var enhanced = new Mat();
            clahe.Apply(gray, enhanced);

            // 3. 軽量ガウシアンブラー（ノイズ除去）
            using var blurred = new Mat();
            Cv2.GaussianBlur(enhanced, blurred, new OpenCvSharp.Size(3, 3), 0.5);

            blurred.CopyTo(output);
            return output;
        }
        catch
        {
            output?.Dispose();
            input.CopyTo(output = new Mat());
            return output;
        }
    }

    /// <summary>
    /// 明るい画像の小さなテキスト強化処理
    /// </summary>
    private static Mat ProcessBrightSmallText(Mat input)
    {
        var output = new Mat();
        try
        {
            // 1. グレースケール変換
            using var gray = new Mat();
            if (input.Channels() == 3)
            {
                Cv2.CvtColor(input, gray, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                input.CopyTo(gray);
            }

            // 2. 2倍アップスケール（小さなテキスト拡大）
            using var upscaled = new Mat();
            Cv2.Resize(gray, upscaled, new OpenCvSharp.Size(gray.Width * 2, gray.Height * 2), 
                       interpolation: InterpolationFlags.Cubic);

            // 3. 明るい画像用シャープニング
            var kernel = new Mat(3, 3, MatType.CV_32F);
            kernel.Set<float>(0, 0, 0);  kernel.Set<float>(0, 1, -1); kernel.Set<float>(0, 2, 0);
            kernel.Set<float>(1, 0, -1); kernel.Set<float>(1, 1, 5);  kernel.Set<float>(1, 2, -1);
            kernel.Set<float>(2, 0, 0);  kernel.Set<float>(2, 1, -1); kernel.Set<float>(2, 2, 0);

            using var sharpened = new Mat();
            Cv2.Filter2D(upscaled, sharpened, MatType.CV_8U, kernel);

            // 4. 元サイズに戻す
            Cv2.Resize(sharpened, output, new OpenCvSharp.Size(gray.Width, gray.Height), 
                       interpolation: InterpolationFlags.Area);

            return output;
        }
        catch
        {
            output?.Dispose();
            input.CopyTo(output = new Mat());
            return output;
        }
    }

    /// <summary>
    /// 明るい画像のコントラスト強化処理
    /// </summary>
    private static Mat ProcessBrightContrast(Mat input)
    {
        var output = new Mat();
        try
        {
            // 明るい画像では既にコントラストが高いため、微調整のみ
            using var gray = new Mat();
            if (input.Channels() == 3)
            {
                Cv2.CvtColor(input, gray, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                input.CopyTo(gray);
            }

            // 軽量なコントラスト調整
            gray.ConvertTo(output, MatType.CV_8UC1, 1.1, 5); // alpha=1.1, beta=5

            return output;
        }
        catch
        {
            output?.Dispose();
            input.CopyTo(output = new Mat());
            return output;
        }
    }

    /// <summary>
    /// 明るい画像の超高精度処理
    /// </summary>
    private static Mat ProcessBrightUltraAccuracy(Mat input)
    {
        return UltraHighAccuracyPreprocessor.ProcessForUltraAccuracy(input);
    }

    /// <summary>
    /// 明るい画像の極限精度処理
    /// </summary>
    private static Mat ProcessBrightPerfectAccuracy(Mat input)
    {
        return UltraHighAccuracyPreprocessor.ProcessForPerfectAccuracy(input);
    }

    #endregion

    #region 暗い画像専用処理メソッド

    /// <summary>
    /// 暗い画像の標準処理
    /// </summary>
    private static Mat ProcessDarkStandard(Mat input)
    {
        var output = new Mat();
        try
        {
            // 1. グレースケール変換
            using var gray = new Mat();
            if (input.Channels() == 3)
            {
                Cv2.CvtColor(input, gray, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                input.CopyTo(gray);
            }

            // 2. 暗い画像用強力CLAHE
            using var clahe = Cv2.CreateCLAHE(clipLimit: 3.0, tileGridSize: new OpenCvSharp.Size(6, 6));
            using var enhanced = new Mat();
            clahe.Apply(gray, enhanced);

            // 3. ガンマ補正（暗い部分を明るく）
            using var gamma = new Mat();
            var lookupTable = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                lookupTable[i] = (byte)(255.0 * Math.Pow(i / 255.0, 0.7)); // ガンマ = 0.7
            }
            Cv2.LUT(enhanced, lookupTable, gamma);

            gamma.CopyTo(output);
            return output;
        }
        catch
        {
            output?.Dispose();
            input.CopyTo(output = new Mat());
            return output;
        }
    }

    /// <summary>
    /// 暗い画像の漢字強化処理
    /// </summary>
    private static Mat ProcessDarkKanji(Mat input)
    {
        var output = new Mat();
        try
        {
            // 1. グレースケール変換
            using var gray = new Mat();
            if (input.Channels() == 3)
            {
                Cv2.CvtColor(input, gray, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                input.CopyTo(gray);
            }

            // 2. 漢字に特化した強力CLAHE
            using var clahe = Cv2.CreateCLAHE(clipLimit: 4.0, tileGridSize: new OpenCvSharp.Size(4, 4));
            using var enhanced = new Mat();
            clahe.Apply(gray, enhanced);

            // 3. 方向性エッジ強化（漢字の横線・縦線強調）
            var kernelH = new Mat(3, 3, MatType.CV_32F);
            kernelH.Set<float>(0, 0, -1); kernelH.Set<float>(0, 1, -1); kernelH.Set<float>(0, 2, -1);
            kernelH.Set<float>(1, 0, 2);  kernelH.Set<float>(1, 1, 2);  kernelH.Set<float>(1, 2, 2);
            kernelH.Set<float>(2, 0, -1); kernelH.Set<float>(2, 1, -1); kernelH.Set<float>(2, 2, -1);

            var kernelV = new Mat(3, 3, MatType.CV_32F);
            kernelV.Set<float>(0, 0, -1); kernelV.Set<float>(0, 1, 2); kernelV.Set<float>(0, 2, -1);
            kernelV.Set<float>(1, 0, -1); kernelV.Set<float>(1, 1, 2); kernelV.Set<float>(1, 2, -1);
            kernelV.Set<float>(2, 0, -1); kernelV.Set<float>(2, 1, 2); kernelV.Set<float>(2, 2, -1);

            using var horizontal = new Mat();
            using var vertical = new Mat();
            Cv2.Filter2D(enhanced, horizontal, MatType.CV_8U, kernelH);
            Cv2.Filter2D(enhanced, vertical, MatType.CV_8U, kernelV);

            // 4. 方向別結果を統合
            Cv2.AddWeighted(horizontal, 0.5, vertical, 0.5, 0, output);

            return output;
        }
        catch
        {
            output?.Dispose();
            input.CopyTo(output = new Mat());
            return output;
        }
    }

    /// <summary>
    /// 暗い画像のコントラスト強化処理
    /// </summary>
    private static Mat ProcessDarkContrast(Mat input)
    {
        var output = new Mat();
        try
        {
            // 暗い画像では積極的なコントラスト強化が必要
            using var gray = new Mat();
            if (input.Channels() == 3)
            {
                Cv2.CvtColor(input, gray, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                input.CopyTo(gray);
            }

            // 強力なコントラスト調整 + ヒストグラム平坦化
            using var equalized = new Mat();
            Cv2.EqualizeHist(gray, equalized);

            // 追加のコントラスト強化
            equalized.ConvertTo(output, MatType.CV_8UC1, 1.3, 20); // alpha=1.3, beta=20

            return output;
        }
        catch
        {
            output?.Dispose();
            input.CopyTo(output = new Mat());
            return output;
        }
    }

    /// <summary>
    /// 暗い画像の超高精度処理
    /// </summary>
    private static Mat ProcessDarkUltraAccuracy(Mat input)
    {
        return UltraHighAccuracyPreprocessor.ProcessForUltraAccuracy(input);
    }

    /// <summary>
    /// 暗い画像の極限精度処理
    /// </summary>
    private static Mat ProcessDarkPerfectAccuracy(Mat input)
    {
        return UltraHighAccuracyPreprocessor.ProcessForPerfectAccuracy(input);
    }

    #endregion

    #region フォント特化統合処理メソッド

    /// <summary>
    /// PP-OCRv5向けフォント特化適応的前処理（画像・フォント特性を統合分析）
    /// </summary>
    public static Mat ProcessForPPOCRv5AdaptiveWithFont(Mat input)
    {
        if (input == null || input.Empty())
            return new Mat();

        try
        {
            // 1. 画像特性分析
            var imageCharacteristics = ImageCharacteristicsAnalyzer.AnalyzeImage(input);
            
            // 2. フォント特性分析
            var fontCharacteristics = FontSpecificPreprocessor.AnalyzeFontCharacteristics(input);
            
            Console.WriteLine($"🔍 統合適応的前処理 - 分析結果:");
            Console.WriteLine($"   📸 画像タイプ: {imageCharacteristics.ImageType}");
            Console.WriteLine($"   🔤 フォントタイプ: {fontCharacteristics.DetectedType}");
            Console.WriteLine($"   💡 画像輝度: {imageCharacteristics.AverageBrightness:F1}");
            Console.WriteLine($"   📏 ストローク幅: {fontCharacteristics.AverageStrokeWidth:F2}");
            Console.WriteLine($"   🎯 画像推奨: {imageCharacteristics.RecommendedMode}");
            Console.WriteLine($"   🎯 フォント推奨: {fontCharacteristics.RecommendedMode}");

            // 3. 統合最適化戦略の決定
            var integratedMode = DetermineIntegratedOptimizationMode(imageCharacteristics, fontCharacteristics);
            Console.WriteLine($"   ⚡ 統合戦略: {integratedMode}");

            // 4. フォント特化前処理の適用
            Mat fontOptimized;
            if (fontCharacteristics.DetectedType != FontSpecificPreprocessor.FontType.Standard)
            {
                Console.WriteLine($"🔤 フォント特化前処理適用: {fontCharacteristics.DetectedType}");
                fontOptimized = FontSpecificPreprocessor.ProcessForFontType(input, fontCharacteristics.DetectedType);
            }
            else
            {
                fontOptimized = new Mat();
                input.CopyTo(fontOptimized);
            }

            // 5. 画像特性に基づく後処理
            Mat finalResult;
            if (imageCharacteristics.IsBrightBackground)
            {
                Console.WriteLine("🌞 明るい画像向け後処理適用");
                finalResult = ProcessBrightGameImage(fontOptimized, integratedMode);
            }
            else if (imageCharacteristics.IsDarkBackground)
            {
                Console.WriteLine("🌙 暗い画像向け後処理適用");
                finalResult = ProcessDarkGameImage(fontOptimized, integratedMode);
            }
            else
            {
                Console.WriteLine("⚖️ 標準後処理適用");
                finalResult = ProcessForPPOCRv5(fontOptimized, integratedMode);
            }

            fontOptimized.Dispose();
            return finalResult;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 統合適応的前処理エラー: {ex.Message}");
            Console.WriteLine("🔄 基本適応的前処理にフォールバック");
            return ProcessForPPOCRv5Adaptive(input);
        }
    }

    /// <summary>
    /// 画像特性とフォント特性を統合して最適な処理モードを決定
    /// </summary>
    private static OptimizationMode DetermineIntegratedOptimizationMode(
        dynamic imageChar,
        FontSpecificPreprocessor.FontCharacteristics fontChar)
    {
        // フォント特性を優先し、画像特性で調整
        var baseMode = fontChar.RecommendedMode;
        
        // 小さなフォント + 暗い画像 = 超高精度必要
        if (fontChar.DetectedType == FontSpecificPreprocessor.FontType.SmallThin && imageChar.IsDarkBackground)
        {
            return OptimizationMode.PerfectAccuracy;
        }
        
        // 装飾フォント + 低コントラスト = 完璧な前処理必要
        if (fontChar.DetectedType == FontSpecificPreprocessor.FontType.Decorative && imageChar.IsLowContrast)
        {
            return OptimizationMode.PerfectAccuracy;
        }
        
        // 標準フォント + 明るい画像 = 軽量処理で十分
        if (fontChar.DetectedType == FontSpecificPreprocessor.FontType.Standard && imageChar.IsBrightBackground)
        {
            return OptimizationMode.ContrastEnhanced;
        }
        
        // ピクセルフォント = 複合処理が効果的
        if (fontChar.DetectedType == FontSpecificPreprocessor.FontType.Pixel)
        {
            return OptimizationMode.Combined;
        }

        return baseMode;
    }

    #endregion
}