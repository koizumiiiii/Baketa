using OpenCvSharp;
using Baketa.Core.Utilities;

namespace Baketa.Infrastructure.OCR.Preprocessing;

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
        if (input == null || input.Empty())
        {
            throw new ArgumentException("入力画像が無効です", nameof(input));
        }

        DebugLogUtility.WriteLog($"🚀 PP-OCRv5専用前処理開始: {input.Width}x{input.Height}");
        
        var processed = new Mat();
        
        try
        {
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
            
            DebugLogUtility.WriteLog($"✅ PP-OCRv5専用前処理完了");
            return processed;
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"❌ PP-OCRv5前処理エラー: {ex.Message}");
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
        DebugLogUtility.WriteLog($"   🔆 PP-OCRv5コントラスト強化開始");
        
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
            
            DebugLogUtility.WriteLog($"   ✅ PP-OCRv5コントラスト強化完了");
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"   ❌ V5コントラスト強化エラー: {ex.Message}");
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
        DebugLogUtility.WriteLog($"   🎯 PP-OCRv5ノイズ除去開始");
        
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
            
            DebugLogUtility.WriteLog($"   ✅ PP-OCRv5ノイズ除去完了");
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"   ❌ V5ノイズ除去エラー: {ex.Message}");
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
        DebugLogUtility.WriteLog($"   🌍 PP-OCRv5多言語テキスト強調開始");
        
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
            
            DebugLogUtility.WriteLog($"   ✅ PP-OCRv5多言語テキスト強調完了");
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"   ❌ V5多言語テキスト強調エラー: {ex.Message}");
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
        DebugLogUtility.WriteLog($"   ✨ PP-OCRv5シャープネス最適化開始");
        
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
            
            DebugLogUtility.WriteLog($"   ✅ PP-OCRv5シャープネス最適化完了");
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"   ❌ V5シャープネス最適化エラー: {ex.Message}");
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
        DebugLogUtility.WriteLog($"   🌟 PP-OCRv5最終最適化開始");
        
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
            
            DebugLogUtility.WriteLog($"   ✅ PP-OCRv5最終最適化完了");
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"   ❌ V5最終最適化エラー: {ex.Message}");
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
        DebugLogUtility.WriteLog($"🎮🚀 ゲーム画面PP-OCRv5専用処理開始");
        
        try
        {
            // 1. ゲーム特化前処理を軽量化して適用
            using var gameProcessed = GameTextPreprocessor.ProcessGameImage(input);
            
            // 2. PP-OCRv5専用最適化を追加適用
            var v5Optimized = ProcessForPPOCRv5(gameProcessed);
            
            DebugLogUtility.WriteLog($"✅ ゲーム画面PP-OCRv5専用処理完了");
            return v5Optimized;
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"❌ ゲーム画面V5処理エラー: {ex.Message}");
            
            // エラー時は元画像を返す
            var fallback = new Mat();
            input.CopyTo(fallback);
            return fallback;
        }
    }
}