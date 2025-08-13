using OpenCvSharp;
using Baketa.Core.Utilities;
using Baketa.Core.Logging;
using Baketa.Core.Settings;
using System.Diagnostics;

namespace Baketa.Infrastructure.OCR.Preprocessing;

/// <summary>
/// ゲームテキストプロファイル - 各ゲームタイプに最適化された前処理設定
/// </summary>
public enum GameTextProfile
{
    /// <summary>暗い背景（RPG、ダークテーマゲーム）</summary>
    DarkBackground,
    /// <summary>明るい背景（カジュアルゲーム、白背景UI）</summary>
    LightBackground,
    /// <summary>低コントラスト（薄いテキスト、グラデーション背景）</summary>
    LowContrast,
    /// <summary>混在フォント（複数フォントサイズ、スタイル）</summary>
    MultiFont,
    /// <summary>UI重複（複雑な背景エフェクト、重複UI）</summary>
    UIOverlay,
    /// <summary>自動検出（画像解析による最適プロファイル選択）</summary>
    Auto
}

/// <summary>
/// ゲーム前処理パラメータ設定
/// </summary>
public class GamePreprocessingParameters
{
    /// <summary>AdaptiveThreshold blockSize (奇数のみ)</summary>
    public int BlockSize { get; set; } = 7;
    
    /// <summary>AdaptiveThreshold c パラメータ</summary>
    public double C { get; set; } = 1.5;
    
    /// <summary>動的blockSize調整の有効化</summary>
    public bool EnableDynamicBlockSize { get; set; } = true;
    
    /// <summary>CLAHEクリップリミット</summary>
    public double CLAHEClipLimit { get; set; } = 3.0;
    
    /// <summary>バイラテラルフィルタのsigmaColor</summary>
    public double BilateralSigmaColor { get; set; } = 75.0;
    
    /// <summary>ガウシアンブラーのsigma</summary>
    public double GaussianSigma { get; set; } = 0.5;
    
    /// <summary>A/Bテスト用の代替パラメータセット</summary>
    public GamePreprocessingParameters? AlternativeParameters { get; set; }
}

/// <summary>
/// ゲーム画面のテキスト認識に特化した画像前処理パイプライン
/// 低コントラスト、ノイズ、エフェクトが多いゲーム画面の品質向上を行う
/// 統一ログシステムとA/Bテスト機能を統合
/// </summary>
public static class GameTextPreprocessor
{
    /// <summary>
    /// デフォルトプロファイル別パラメータ設定（設定が無い場合のフォールバック）
    /// </summary>
    private static readonly Dictionary<GameTextProfile, GamePreprocessingParameters> DefaultProfileParameters = new()
    {
        [GameTextProfile.DarkBackground] = new GamePreprocessingParameters
        {
            BlockSize = 7,
            C = 1.2,
            CLAHEClipLimit = 4.0,
            BilateralSigmaColor = 80.0,
            GaussianSigma = 0.3
        },
        [GameTextProfile.LightBackground] = new GamePreprocessingParameters
        {
            BlockSize = 9,
            C = 2.0,
            CLAHEClipLimit = 2.5,
            BilateralSigmaColor = 60.0,
            GaussianSigma = 0.7
        },
        [GameTextProfile.LowContrast] = new GamePreprocessingParameters
        {
            BlockSize = 5,
            C = 1.0,
            CLAHEClipLimit = 5.0,
            BilateralSigmaColor = 100.0,
            GaussianSigma = 0.2
        },
        [GameTextProfile.MultiFont] = new GamePreprocessingParameters
        {
            BlockSize = 11,
            C = 1.8,
            EnableDynamicBlockSize = true,
            CLAHEClipLimit = 3.5,
            BilateralSigmaColor = 70.0,
            GaussianSigma = 0.4
        },
        [GameTextProfile.UIOverlay] = new GamePreprocessingParameters
        {
            BlockSize = 13,
            C = 2.5,
            CLAHEClipLimit = 4.5,
            BilateralSigmaColor = 90.0,
            GaussianSigma = 0.8
        }
    };

    /// <summary>
    /// ゲーム画面の包括的前処理を実行（既存API互換）
    /// </summary>
    /// <param name="input">入力画像</param>
    /// <returns>前処理後の画像</returns>
    public static Mat ProcessGameImage(Mat input)
    {
        return ProcessGameImage(input, GameTextProfile.Auto);
    }

    /// <summary>
    /// ゲーム画面の包括的前処理を実行（プロファイル指定 + 設定）
    /// </summary>
    /// <param name="input">入力画像</param>
    /// <param name="profile">ゲームテキストプロファイル</param>
    /// <param name="settings">ゲーム前処理設定</param>
    /// <returns>前処理後の画像</returns>
    public static Mat ProcessGameImage(Mat input, GameTextProfile profile, GamePreprocessingSettings? settings = null)
    {
        var parameters = GetParametersForProfile(profile, input, settings);
        return ProcessGameImage(input, profile, parameters);
    }

    /// <summary>
    /// ゲーム画面の包括的前処理を実行（パラメータ直接指定）
    /// </summary>
    /// <param name="input">入力画像</param>
    /// <param name="profile">ゲームテキストプロファイル</param>
    /// <param name="parameters">前処理パラメータ</param>
    /// <returns>前処理後の画像</returns>
    public static Mat ProcessGameImage(Mat input, GameTextProfile profile, GamePreprocessingParameters parameters)
    {
        if (input == null || input.Empty())
        {
            throw new ArgumentException("入力画像が無効です", nameof(input));
        }

        var stopwatch = Stopwatch.StartNew();
        BaketaLogManager.LogSystemDebug($"🎮 ゲーム特化前処理開始: {input.Width}x{input.Height}, プロファイル: {profile}");
        
        // パフォーマンス測定用
        var operationId = Guid.NewGuid().ToString("N")[..8];
        var performanceEntry = new PerformanceLogEntry
        {
            OperationId = operationId,
            Timestamp = DateTime.Now,
            OperationName = $"GameTextPreprocessor.ProcessGameImage_{profile}",
            BottleneckAnalysis = new Dictionary<string, object>
            {
                ["InputSize"] = $"{input.Width}x{input.Height}",
                ["Profile"] = profile.ToString(),
                ["BlockSize"] = parameters.BlockSize,
                ["C"] = parameters.C,
                ["CLAHEClipLimit"] = parameters.CLAHEClipLimit
            }
        };
        
        var processed = new Mat();
        
        try
        {
            // 1. 適応的コントラスト強化
            var contrastEnhanced = EnhanceAdaptiveContrast(input);
            
            // 2. ゲーム特有のノイズ除去（パラメータ適用）
            var denoised = RemoveGameNoise(contrastEnhanced, parameters);
            
            // 3. テキスト背景分離（最適化されたAdaptiveThreshold）
            var backgroundSeparated = SeparateTextFromBackground(denoised, parameters);
            
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
            
            stopwatch.Stop();
            
            // パフォーマンスログ記録
            var finalEntry = performanceEntry with { DurationMs = stopwatch.ElapsedMilliseconds };
            BaketaLogManager.LogPerformance(finalEntry);
            
            BaketaLogManager.LogSystemDebug($"✅ ゲーム特化前処理完了: {stopwatch.ElapsedMilliseconds}ms");
            return processed;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            // エラーログ記録
            BaketaLogManager.LogError(ex, "GameTextPreprocessor");
            
            // パフォーマンスログ記録（エラー）
            var errorEntry = new PerformanceLogEntry
            {
                OperationId = Guid.NewGuid().ToString("N")[..8],
                Timestamp = DateTime.Now,
                OperationName = $"GameTextPreprocessor.ProcessGameImage_{profile}_ERROR",
                DurationMs = stopwatch.ElapsedMilliseconds,
                BottleneckAnalysis = new Dictionary<string, object>
                {
                    ["ErrorMessage"] = ex.Message,
                    ["InputSize"] = $"{input.Width}x{input.Height}"
                }
            };
            BaketaLogManager.LogPerformance(errorEntry);
            
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
        BaketaLogManager.LogSystemDebug($"   🔆 適応的コントラスト強化開始");
        
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
            
            BaketaLogManager.LogSystemDebug($"   ✅ 適応的コントラスト強化完了");
        }
        catch (Exception ex)
        {
            BaketaLogManager.LogSystemDebug($"   ❌ コントラスト強化エラー: {ex.Message}");
            input.CopyTo(output);
        }
        
        return output;
    }
    
    /// <summary>
    /// ゲーム特有のノイズ除去 - 圧縮アーティファクト、ジャギー対策（パラメータ対応）
    /// </summary>
    private static Mat RemoveGameNoise(Mat input, GamePreprocessingParameters parameters)
    {
        BaketaLogManager.LogSystemDebug($"   🎯 ゲームノイズ除去開始");
        
        var output = new Mat();
        
        try
        {
            // バイラテラルフィルタでエッジを保持しながらノイズ除去（パラメータ適用）
            Cv2.BilateralFilter(input, output, d: 9, sigmaColor: parameters.BilateralSigmaColor, sigmaSpace: parameters.BilateralSigmaColor);
            
            // 軽微なガウシアンブラーで細かいノイズを平滑化（パラメータ適用）
            using var temp = new Mat();
            output.CopyTo(temp);
            Cv2.GaussianBlur(temp, output, new OpenCvSharp.Size(3, 3), parameters.GaussianSigma);
            
            BaketaLogManager.LogSystemDebug($"   ✅ ゲームノイズ除去完了");
        }
        catch (Exception ex)
        {
            BaketaLogManager.LogSystemDebug($"   ❌ ノイズ除去エラー: {ex.Message}");
            input.CopyTo(output);
        }
        
        return output;
    }
    
    /// <summary>
    /// テキスト背景分離 - ゲームUIの複雑な背景からテキストを分離（最適化されたAdaptiveThreshold）
    /// </summary>
    private static Mat SeparateTextFromBackground(Mat input, GamePreprocessingParameters parameters)
    {
        BaketaLogManager.LogSystemDebug($"   🎨 テキスト背景分離開始");
        
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
            
            // 適応的二値化でテキストを抽出（最適化されたパラメータ）
            using var binary = new Mat();
            
            // 動的blockSize調整
            var adaptiveBlockSize = parameters.EnableDynamicBlockSize ? 
                CalculateDynamicBlockSize(input.Width, input.Height, parameters.BlockSize) : 
                parameters.BlockSize;
            
            BaketaLogManager.LogSystemDebug($"   🎯 AdaptiveThreshold: blockSize={adaptiveBlockSize}, c={parameters.C}");
            
            Cv2.AdaptiveThreshold(gray, binary, 
                maxValue: 255,
                adaptiveMethod: AdaptiveThresholdTypes.GaussianC,
                thresholdType: ThresholdTypes.Binary,
                blockSize: adaptiveBlockSize,
                c: parameters.C);
            
            // モルフォロジー演算でテキスト形状を整える
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(2, 2));
            using var cleaned = new Mat();
            
            // 開放演算でノイズ除去
            Cv2.MorphologyEx(binary, cleaned, MorphTypes.Open, kernel);
            
            // 閉鎖演算で文字の隙間を埋める
            Cv2.MorphologyEx(cleaned, output, MorphTypes.Close, kernel);
            
            BaketaLogManager.LogSystemDebug($"   ✅ テキスト背景分離完了");
        }
        catch (Exception ex)
        {
            BaketaLogManager.LogSystemDebug($"   ❌ 背景分離エラー: {ex.Message}");
            input.CopyTo(output);
        }
        
        return output;
    }
    
    /// <summary>
    /// 文字エッジ強調 - 文字輪郭の明確化
    /// </summary>
    private static Mat EnhanceTextEdges(Mat input)
    {
        BaketaLogManager.LogSystemDebug($"   ✨ 文字エッジ強調開始");
        
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
            
            BaketaLogManager.LogSystemDebug($"   ✅ 文字エッジ強調完了");
        }
        catch (Exception ex)
        {
            BaketaLogManager.LogSystemDebug($"   ❌ エッジ強調エラー: {ex.Message}");
            input.CopyTo(output);
        }
        
        return output;
    }
    
    /// <summary>
    /// 最終品質向上 - シャープネス強化と最終調整
    /// </summary>
    private static Mat ApplyFinalQualityEnhancement(Mat input)
    {
        BaketaLogManager.LogSystemDebug($"   🌟 最終品質向上開始");
        
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
            
            BaketaLogManager.LogSystemDebug($"   ✅ 最終品質向上完了");
        }
        catch (Exception ex)
        {
            BaketaLogManager.LogSystemDebug($"   ❌ 最終品質向上エラー: {ex.Message}");
            input.CopyTo(output);
        }
        
        return output;
    }

    /// <summary>
    /// 画像サイズに応じた動的blockSize計算
    /// ゲームフォントの多様性に対応
    /// </summary>
    /// <param name="imageWidth">画像幅</param>
    /// <param name="imageHeight">画像高さ</param>
    /// <param name="baseBlockSize">ベースblockSize</param>
    /// <returns>最適化されたblockSize</returns>
    private static int CalculateDynamicBlockSize(int imageWidth, int imageHeight, int baseBlockSize)
    {
        try
        {
            // 画像サイズに基づいたスケーリング
            var imageArea = imageWidth * imageHeight;
            double scaleFactor = 1.0;
            
            // 画像サイズ別調整
            if (imageArea < 100_000) // 小さい画像 (320x240程度)
            {
                scaleFactor = 0.7; // blockSizeを小さく
            }
            else if (imageArea > 2_000_000) // 大きい画像 (1600x1200程度)
            {
                scaleFactor = 1.4; // blockSizeを大きく
            }
            
            var calculatedSize = (int)(baseBlockSize * scaleFactor);
            
            // 奇数のみ、範囲内に制限
            calculatedSize = Math.Max(3, Math.Min(21, calculatedSize));
            if (calculatedSize % 2 == 0) calculatedSize++; // 奇数に調整
            
            BaketaLogManager.LogSystemDebug($"   📊 動的blockSize: {baseBlockSize} → {calculatedSize} (scale: {scaleFactor:F2}, area: {imageArea:N0})");
            
            return calculatedSize;
        }
        catch (Exception ex)
        {
            BaketaLogManager.LogError(ex, "CalculateDynamicBlockSize");
            return baseBlockSize; // エラー時はベース値を返す
        }
    }

    /// <summary>
    /// 画像解析による最適プロファイル自動検出
    /// </summary>
    /// <param name="input">入力画像</param>
    /// <returns>検出されたプロファイルパラメータ</returns>
    private static GamePreprocessingParameters DetectOptimalProfile(Mat input, GamePreprocessingSettings? settings = null)
    {
        try
        {
            BaketaLogManager.LogSystemDebug($"   🔍 自動プロファイル検出開始: {input.Width}x{input.Height}");
            
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
            
            // 基本的な画像特性を解析
            var mean = Cv2.Mean(gray);
            var stddev = new Scalar();
            Cv2.MeanStdDev(gray, out var meanOut, out stddev);
            
            double brightness = mean.Val0;
            double contrast = stddev.Val0;
            
            BaketaLogManager.LogSystemDebug($"   📊 画像特性: 明度={brightness:F1}, コントラスト={contrast:F1}");
            
            // プロファイル判定ロジック
            if (brightness < 80)
            {
                BaketaLogManager.LogSystemDebug($"   ✅ 暗い背景プロファイルを選択");
                return GetParametersForProfile(GameTextProfile.DarkBackground, input, settings);
            }
            else if (brightness > 180)
            {
                BaketaLogManager.LogSystemDebug($"   ✅ 明るい背景プロファイルを選択");
                return GetParametersForProfile(GameTextProfile.LightBackground, input, settings);
            }
            else if (contrast < 30)
            {
                BaketaLogManager.LogSystemDebug($"   ✅ 低コントラストプロファイルを選択");
                return GetParametersForProfile(GameTextProfile.LowContrast, input, settings);
            }
            else if (contrast > 80)
            {
                // 高コントラストはUI重複の可能性
                BaketaLogManager.LogSystemDebug($"   ✅ UI重複プロファイルを選択");
                return GetParametersForProfile(GameTextProfile.UIOverlay, input, settings);
            }
            else
            {
                // 中間的な特性の場合はマルチフォント対応
                BaketaLogManager.LogSystemDebug($"   ✅ マルチフォントプロファイルを選択");
                return GetParametersForProfile(GameTextProfile.MultiFont, input, settings);
            }
        }
        catch (Exception ex)
        {
            BaketaLogManager.LogError(ex, "DetectOptimalProfile");
            // エラー時はデフォルトプロファイルを返す
            return GetParametersForProfile(GameTextProfile.MultiFont, input, settings);
        }
    }
    
    /// <summary>
    /// プロファイルと設定からパラメータを取得
    /// </summary>
    private static GamePreprocessingParameters GetParametersForProfile(GameTextProfile profile, Mat _, GamePreprocessingSettings? settings)
    {
        if (settings == null)
        {
            // 設定が無い場合はデフォルト値を使用
            return DefaultProfileParameters.TryGetValue(profile, out var defaultParams) ? 
                defaultParams : DefaultProfileParameters[GameTextProfile.MultiFont];
        }
        
        // 設定からプロファイル対応のパラメータを取得
        var profileParams = profile switch
        {
            GameTextProfile.DarkBackground => ConvertToGamePreprocessingParameters(settings.DarkBackground),
            GameTextProfile.LightBackground => ConvertToGamePreprocessingParameters(settings.LightBackground),
            GameTextProfile.LowContrast => ConvertToGamePreprocessingParameters(settings.LowContrast),
            GameTextProfile.MultiFont => ConvertToGamePreprocessingParameters(settings.MultiFont),
            GameTextProfile.UIOverlay => ConvertToGamePreprocessingParameters(settings.UIOverlay),
            _ => DefaultProfileParameters[GameTextProfile.MultiFont]
        };
        
        return profileParams;
    }
    
    /// <summary>
    /// GameProfileParametersをGamePreprocessingParametersに変換
    /// </summary>
    private static GamePreprocessingParameters ConvertToGamePreprocessingParameters(GameProfileParameters profileParams)
    {
        return new GamePreprocessingParameters
        {
            BlockSize = profileParams.BlockSize,
            C = profileParams.C,
            EnableDynamicBlockSize = profileParams.EnableDynamicBlockSize,
            CLAHEClipLimit = profileParams.CLAHEClipLimit,
            BilateralSigmaColor = profileParams.BilateralSigmaColor,
            GaussianSigma = profileParams.GaussianSigma
        };
    }

    /// <summary>
    /// A/Bテスト用パラメータ比較処理
    /// 同一画像に対して異なるパラメータで前処理を実行し、結果を比較
    /// </summary>
    /// <param name="input">入力画像</param>
    /// <param name="profile">ベースプロファイル</param>
    /// <param name="baseParameters">ベースパラメータ</param>
    /// <returns>A/Bテスト結果</returns>
    public static AbTestResult ProcessGameImageABTest(Mat input, GameTextProfile profile, GamePreprocessingParameters baseParameters)
    {
        if (baseParameters.AlternativeParameters == null)
        {
            throw new ArgumentException("代替パラメータが設定されていません", nameof(baseParameters));
        }

        try
        {
            BaketaLogManager.LogSystemDebug($"🧪 A/Bテスト開始: {profile}");
            
            var stopwatchA = Stopwatch.StartNew();
            var resultA = ProcessGameImage(input, profile, baseParameters);
            stopwatchA.Stop();
            
            var stopwatchB = Stopwatch.StartNew();
            var resultB = ProcessGameImage(input, profile, baseParameters.AlternativeParameters);
            stopwatchB.Stop();
            
            // 結果比較ログ
            BaketaLogManager.LogSystemDebug($"📊 A/Bテスト結果: A={stopwatchA.ElapsedMilliseconds}ms, B={stopwatchB.ElapsedMilliseconds}ms");
            
            return new AbTestResult
            {
                OriginalResult = resultA,
                AlternativeResult = resultB,
                OriginalDuration = stopwatchA.ElapsedMilliseconds,
                AlternativeDuration = stopwatchB.ElapsedMilliseconds,
                OriginalParameters = baseParameters,
                AlternativeParameters = baseParameters.AlternativeParameters
            };
        }
        catch (Exception ex)
        {
            BaketaLogManager.LogError(ex, "ProcessGameImageABTest");
            throw;
        }
    }
}

/// <summary>
/// A/Bテスト結果
/// </summary>
public class AbTestResult
{
    public required Mat OriginalResult { get; init; }
    public required Mat AlternativeResult { get; init; }
    public long OriginalDuration { get; init; }
    public long AlternativeDuration { get; init; }
    public required GamePreprocessingParameters OriginalParameters { get; init; }
    public required GamePreprocessingParameters AlternativeParameters { get; init; }
    
    /// <summary>
    /// リソース解放
    /// </summary>
    public void Dispose()
    {
        OriginalResult?.Dispose();
        AlternativeResult?.Dispose();
    }
}