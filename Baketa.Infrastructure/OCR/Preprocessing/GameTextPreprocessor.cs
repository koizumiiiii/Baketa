using OpenCvSharp;
using Baketa.Core.Utilities;

namespace Baketa.Infrastructure.OCR.Preprocessing;

/// <summary>
/// ゲーム画面のテキスト認識に特化した画像前処理パイプライン
/// 低コントラスト、ノイズ、エフェクトが多いゲーム画面の品質向上を行う
/// </summary>
public static class GameTextPreprocessor
{
    /// <summary>
    /// ゲーム画面の包括的前処理を実行
    /// </summary>
    /// <param name="input">入力画像</param>
    /// <returns>前処理後の画像</returns>
    public static Mat ProcessGameImage(Mat input)
    {
        if (input == null || input.Empty())
        {
            throw new ArgumentException("入力画像が無効です", nameof(input));
        }

        DebugLogUtility.WriteLog($"🎮 ゲーム特化前処理開始: {input.Width}x{input.Height}");
        
        var processed = new Mat();
        
        try
        {
            // 1. 適応的コントラスト強化
            var contrastEnhanced = EnhanceAdaptiveContrast(input);
            
            // 2. ゲーム特有のノイズ除去
            var denoised = RemoveGameNoise(contrastEnhanced);
            
            // 3. テキスト背景分離
            var backgroundSeparated = SeparateTextFromBackground(denoised);
            
            // 4. 文字エッジ強調
            var edgeEnhanced = EnhanceTextEdges(backgroundSeparated);
            
            // 5. 最終品質向上
            var finalResult = ApplyFinalQualityEnhancement(edgeEnhanced);
            
            finalResult.CopyTo(processed);
            
            // リソース解放
            contrastEnhanced.Dispose();
            denoised.Dispose();
            backgroundSeparated.Dispose();
            edgeEnhanced.Dispose();
            finalResult.Dispose();
            
            DebugLogUtility.WriteLog($"✅ ゲーム特化前処理完了");
            return processed;
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"❌ ゲーム前処理エラー: {ex.Message}");
            processed?.Dispose();
            
            // エラー時は元画像を返す
            var fallback = new Mat();
            input.CopyTo(fallback);
            return fallback;
        }
    }
    
    /// <summary>
    /// 適応的コントラスト強化 - ゲーム画面の不均一な照明に対応
    /// </summary>
    private static Mat EnhanceAdaptiveContrast(Mat input)
    {
        DebugLogUtility.WriteLog($"   🔆 適応的コントラスト強化開始");
        
        var output = new Mat();
        
        try
        {
            // CLAHEを使用して局所的コントラスト強化
            using var clahe = Cv2.CreateCLAHE(clipLimit: 3.0, tileGridSize: new OpenCvSharp.Size(8, 8));
            
            if (input.Channels() == 3)
            {
                // カラー画像の場合はLab色空間でL成分のみ処理
                using var lab = new Mat();
                using var lChannel = new Mat();
                using var enhancedL = new Mat();
                
                Cv2.CvtColor(input, lab, ColorConversionCodes.BGR2Lab);
                var channels = Cv2.Split(lab);
                
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
                // グレースケールの場合は直接適用
                clahe.Apply(input, output);
            }
            
            DebugLogUtility.WriteLog($"   ✅ 適応的コントラスト強化完了");
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"   ❌ コントラスト強化エラー: {ex.Message}");
            input.CopyTo(output);
        }
        
        return output;
    }
    
    /// <summary>
    /// ゲーム特有のノイズ除去 - 圧縮アーティファクト、ジャギー対策
    /// </summary>
    private static Mat RemoveGameNoise(Mat input)
    {
        DebugLogUtility.WriteLog($"   🎯 ゲームノイズ除去開始");
        
        var output = new Mat();
        
        try
        {
            // バイラテラルフィルタでエッジを保持しながらノイズ除去
            Cv2.BilateralFilter(input, output, d: 9, sigmaColor: 75, sigmaSpace: 75);
            
            // 軽微なガウシアンブラーで細かいノイズを平滑化
            using var temp = new Mat();
            output.CopyTo(temp);
            Cv2.GaussianBlur(temp, output, new OpenCvSharp.Size(3, 3), 0.5);
            
            DebugLogUtility.WriteLog($"   ✅ ゲームノイズ除去完了");
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"   ❌ ノイズ除去エラー: {ex.Message}");
            input.CopyTo(output);
        }
        
        return output;
    }
    
    /// <summary>
    /// テキスト背景分離 - ゲームUIの複雑な背景からテキストを分離
    /// </summary>
    private static Mat SeparateTextFromBackground(Mat input)
    {
        DebugLogUtility.WriteLog($"   🎨 テキスト背景分離開始");
        
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
            
            // 適応的二値化でテキストを抽出
            using var binary = new Mat();
            Cv2.AdaptiveThreshold(gray, binary, 
                maxValue: 255,
                adaptiveMethod: AdaptiveThresholdTypes.GaussianC,
                thresholdType: ThresholdTypes.Binary,
                blockSize: 11,
                c: 2);
            
            // モルフォロジー演算でテキスト形状を整える
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(2, 2));
            using var cleaned = new Mat();
            
            // 開放演算でノイズ除去
            Cv2.MorphologyEx(binary, cleaned, MorphTypes.Open, kernel);
            
            // 閉鎖演算で文字の隙間を埋める
            Cv2.MorphologyEx(cleaned, output, MorphTypes.Close, kernel);
            
            DebugLogUtility.WriteLog($"   ✅ テキスト背景分離完了");
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"   ❌ 背景分離エラー: {ex.Message}");
            input.CopyTo(output);
        }
        
        return output;
    }
    
    /// <summary>
    /// 文字エッジ強調 - 文字輪郭の明確化
    /// </summary>
    private static Mat EnhanceTextEdges(Mat input)
    {
        DebugLogUtility.WriteLog($"   ✨ 文字エッジ強調開始");
        
        var output = new Mat();
        
        try
        {
            // Sobelエッジ検出
            using var sobelX = new Mat();
            using var sobelY = new Mat();
            using var sobelCombined = new Mat();
            
            Cv2.Sobel(input, sobelX, MatType.CV_64F, 1, 0, ksize: 3);
            Cv2.Sobel(input, sobelY, MatType.CV_64F, 0, 1, ksize: 3);
            
            // X, Y方向のエッジを統合
            Cv2.Magnitude(sobelX, sobelY, sobelCombined);
            
            // エッジ情報を元画像に適用
            using var edgeNormalized = new Mat();
            sobelCombined.ConvertTo(edgeNormalized, MatType.CV_8U);
            
            // エッジを元画像に重ね合わせ
            Cv2.AddWeighted(input, 0.8, edgeNormalized, 0.2, 0, output);
            
            DebugLogUtility.WriteLog($"   ✅ 文字エッジ強調完了");
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"   ❌ エッジ強調エラー: {ex.Message}");
            input.CopyTo(output);
        }
        
        return output;
    }
    
    /// <summary>
    /// 最終品質向上 - シャープネス強化と最終調整
    /// </summary>
    private static Mat ApplyFinalQualityEnhancement(Mat input)
    {
        DebugLogUtility.WriteLog($"   🌟 最終品質向上開始");
        
        var output = new Mat();
        
        try
        {
            // アンシャープマスクでシャープネス強化
            using var blurred = new Mat();
            using var unsharpMask = new Mat();
            
            Cv2.GaussianBlur(input, blurred, new OpenCvSharp.Size(3, 3), 1.0);
            Cv2.AddWeighted(input, 1.5, blurred, -0.5, 0, unsharpMask);
            
            // 最終的なコントラスト調整
            unsharpMask.ConvertTo(output, MatType.CV_8U, alpha: 1.1, beta: 5);
            
            DebugLogUtility.WriteLog($"   ✅ 最終品質向上完了");
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"   ❌ 最終品質向上エラー: {ex.Message}");
            input.CopyTo(output);
        }
        
        return output;
    }
}