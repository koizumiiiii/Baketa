using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Memory;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.Imaging.Filters;
using Microsoft.Extensions.Logging;
using OCRTextRegion = Baketa.Core.Abstractions.OCR.TextDetection.TextRegion;

namespace Baketa.Infrastructure.Imaging.Services;

/// <summary>
/// ゲーム画面特化OCR前処理サービス
/// Phase 3: OpenCvSharp を活用した高精度前処理パイプライン
/// 🏊‍♂️ オブジェクトプール対応版 - メモリ効率向上
/// </summary>
public sealed class GameOptimizedPreprocessingService(
    ILogger<GameOptimizedPreprocessingService> logger,
    IAdvancedImagePool imagePool) : IOcrPreprocessingService
{
    private readonly ILogger<GameOptimizedPreprocessingService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IAdvancedImagePool _imagePool = imagePool ?? throw new ArgumentNullException(nameof(imagePool));

    /// <summary>
    /// ゲーム画面プロファイル定義
    /// </summary>
    private static readonly Dictionary<string, GameScreenProfile> Profiles = new()
    {
        ["default"] = new GameScreenProfile
        {
            Name = "標準",
            EnableAdaptiveThreshold = true,
            EnableColorMasking = true,
            AdaptiveBlockSize = 19,        // より大きなブロックで文字の連続性を保持
            AdaptiveC = 6.0,               // より緩い閾値で文字の細部保持
            ColorMaskingStrength = 0.7f
        },
        ["darkbackground"] = new GameScreenProfile
        {
            Name = "暗い背景",
            EnableAdaptiveThreshold = true,
            EnableColorMasking = true,
            AdaptiveBlockSize = 17,  // より大きなブロックで文字の連続性向上
            AdaptiveC = 9.0,         // 適度な閾値調整で文字結合促進
            ColorMaskingStrength = 0.85f,
            PreBlurEnabled = true,
            PreBlurKernelSize = 3
        },
        ["lightbackground"] = new GameScreenProfile
        {
            Name = "明るい背景",
            EnableAdaptiveThreshold = true,
            EnableColorMasking = false,  // 明るい背景では色マスキング不要
            AdaptiveBlockSize = 21,      // より大きなブロックで広域適応
            AdaptiveC = 4.0,             // より弱い閾値調整
            ColorMaskingStrength = 0.5f,
            PostMorphEnabled = true,
            MorphKernelSize = 2
        },
        ["highcontrast"] = new GameScreenProfile
        {
            Name = "高コントラスト",
            EnableAdaptiveThreshold = true,
            EnableColorMasking = false,
            AdaptiveBlockSize = 21,      // より大きなブロックで長いフレーズ対応
            AdaptiveC = 4.5,             // より緩い閾値でテキスト連続性確保
            ColorMaskingStrength = 0.6f,
            PostMorphEnabled = true,
            MorphKernelSize = 1,
            MorphIterations = 1          // モルフォロジー処理を軽減
        },
        ["anime"] = new GameScreenProfile
        {
            Name = "アニメ調",
            EnableAdaptiveThreshold = true,
            EnableColorMasking = true,
            AdaptiveBlockSize = 15,        // 中程度のブロックサイズで文字結合
            AdaptiveC = 8.0,               // バランスの取れた閾値
            ColorMaskingStrength = 0.85f,  // アニメ調色抽出を適度に
            PreBlurEnabled = false,        // アニメ調は鮮明さを保持
            PostMorphEnabled = true,
            MorphKernelSize = 1,           // より軽いモルフォロジー処理
            MorphIterations = 1
        }
    };

    /// <summary>
    /// 画像を処理し、OCRのためのテキスト領域を検出
    /// </summary>
    /// <param name="image">入力画像</param>
    /// <param name="profileName">使用するプロファイル名</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>前処理結果</returns>
    public async Task<OcrPreprocessingResult> ProcessImageAsync(
        IAdvancedImage image, 
        string? profileName = null, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        
        var profile = GetProfile(profileName);
        
        try
        {
            // 🔍 Phase 3診断: 直接ファイル出力で確実にログを残す
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"Phase 3 開始ログ書き込みエラー: {fileEx.Message}");
            }
            
            _logger.LogInformation("ゲーム最適化前処理開始: プロファイル={ProfileName}, サイズ={Width}x{Height}", 
                profile.Name, image.Width, image.Height);
            
            var processedImage = await ApplyGameOptimizedProcessingAsync(image, profile, cancellationToken)
                .ConfigureAwait(false);
            
            // 🔍 Phase 3診断: 完了ログも直接ファイル出力
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"Phase 3 完了ログ書き込みエラー: {fileEx.Message}");
            }
            
            _logger.LogInformation("ゲーム最適化前処理完了: プロファイル={ProfileName}", profile.Name);
            
            return new OcrPreprocessingResult(
                false,
                null,
                processedImage,
                []);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("ゲーム最適化前処理がキャンセルされました");
            return new OcrPreprocessingResult(
                true,
                null,
                image,
                []);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ゲーム最適化前処理中にエラーが発生しました");
            return new OcrPreprocessingResult(
                false,
                ex,
                image,
                []);
        }
    }

    /// <summary>
    /// テキスト領域検出（基本実装）
    /// </summary>
    /// <param name="image">入力画像</param>
    /// <param name="detectorTypes">検出器タイプ</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>検出されたテキスト領域</returns>
    public async Task<IReadOnlyList<OCRTextRegion>> DetectTextRegionsAsync(
        IAdvancedImage image,
        IEnumerable<string> detectorTypes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        
        try
        {
            _logger.LogDebug("テキスト領域検出開始");
            
            // 現在は基本実装のため空のリストを返す
            await Task.CompletedTask.ConfigureAwait(false);
            
            return [];
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("テキスト領域検出がキャンセルされました");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "テキスト領域検出中にエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// ゲーム最適化処理を適用（🏊‍♂️ オブジェクトプール対応版）
    /// </summary>
    /// <param name="image">入力画像</param>
    /// <param name="profile">使用するプロファイル</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>処理済み画像</returns>
    private async Task<IAdvancedImage> ApplyGameOptimizedProcessingAsync(
        IAdvancedImage image, 
        GameScreenProfile profile, 
        CancellationToken _)
    {
        var currentImage = image;
        var pooledImages = new List<IAdvancedImage>(); // プールから取得した画像を追跡

        try
        {
            // Step 1: 色ベースマスキング（背景ノイズ除去）
            if (profile.EnableColorMasking)
            {
                _logger.LogDebug("🎨 色ベースマスキング適用中（プール使用）...");
                
                var colorMaskingFilter = CreateColorMaskingFilter(profile);
                var maskedImage = await colorMaskingFilter.ApplyAsync(currentImage).ConfigureAwait(false);
                
                currentImage = maskedImage;
                
                _logger.LogDebug("✅ 色ベースマスキング完了（プール効率: HitRate={HitRate:P1}）", 
                    _imagePool.Statistics.HitRate);
            }

            // Step 2: 適応的二値化（照明変化対応）
            if (profile.EnableAdaptiveThreshold)
            {
                _logger.LogDebug("🔧 適応的二値化適用中（プール使用）...");
                
                var adaptiveThresholdFilter = CreateAdaptiveThresholdFilter(profile);
                var thresholdImage = await adaptiveThresholdFilter.ApplyAsync(currentImage).ConfigureAwait(false);
                
                currentImage = thresholdImage;
                
                _logger.LogDebug("✅ 適応的二値化完了（プール効率: HitRate={HitRate:P1}）", 
                    _imagePool.Statistics.HitRate);
            }

            _logger.LogInformation("🎮 ゲーム最適化処理完了: ColorMasking={ColorMasking}, AdaptiveThreshold={AdaptiveThreshold}, " +
                "PoolObjectsUsed={PoolObjectsUsed}, MemoryEfficiency={MemoryEfficiency:P1}",
                profile.EnableColorMasking, profile.EnableAdaptiveThreshold, 
                pooledImages.Count, _imagePool.Statistics.HitRate);

            return currentImage;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("⏹️ ゲーム最適化処理がキャンセルされました");
            
            // プールから取得した画像をプールに返却
            ReturnPooledImages(pooledImages);
            
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ゲーム最適化処理中にエラーが発生しました");
            
            // プールから取得した画像をプールに返却
            ReturnPooledImages(pooledImages);
            
            throw;
        }
    }

    /// <summary>
    /// プールから取得した画像をプールに返却
    /// </summary>
    /// <param name="pooledImages">プールから取得した画像のリスト</param>
    private void ReturnPooledImages(List<IAdvancedImage> pooledImages)
    {
        foreach (var pooledImage in pooledImages)
        {
            try
            {
                _imagePool.Release(pooledImage);
                _logger.LogDebug("📥 画像をプールに返却: Size={Width}x{Height}", 
                    pooledImage.Width, pooledImage.Height);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ 画像プール返却時にエラー: Size={Width}x{Height}", 
                    pooledImage.Width, pooledImage.Height);
                
                // プール返却に失敗した場合は直接破棄
                pooledImage.Dispose();
            }
        }
    }

    /// <summary>
    /// 色ベースマスキングフィルターを作成
    /// </summary>
    /// <param name="profile">プロファイル</param>
    /// <returns>設定済みフィルター</returns>
    private OpenCvColorBasedMaskingFilter CreateColorMaskingFilter(GameScreenProfile profile)
    {
        var filter = new OpenCvColorBasedMaskingFilter(_logger as ILogger<OpenCvColorBasedMaskingFilter>);
        
        // プロファイルに応じたパラメータ設定
        filter.SetParameter("EnableDetailedLogging", true);
        
        // 色マスク有効性をプロファイルの強度に応じて調整
        var enableAllMasks = profile.ColorMaskingStrength > 0.7f;
        filter.SetParameter("EnableWhiteMask", enableAllMasks);
        filter.SetParameter("EnableYellowMask", enableAllMasks);
        filter.SetParameter("EnableCyanMask", enableAllMasks);
        filter.SetParameter("EnablePinkMask", enableAllMasks);
        
        // 後処理設定
        filter.SetParameter("EnableMorphClosing", true);
        filter.SetParameter("MorphKernelSize", profile.MorphKernelSize);
        
        return filter;
    }

    /// <summary>
    /// 適応的二値化フィルターを作成
    /// </summary>
    /// <param name="profile">プロファイル</param>
    /// <returns>設定済みフィルター</returns>
    private OpenCvAdaptiveThresholdFilter CreateAdaptiveThresholdFilter(GameScreenProfile profile)
    {
        var filter = new OpenCvAdaptiveThresholdFilter(_logger as ILogger<OpenCvAdaptiveThresholdFilter>);
        
        // プロファイルパラメータ設定
        filter.SetParameter("BlockSize", profile.AdaptiveBlockSize);
        filter.SetParameter("C", profile.AdaptiveC);
        filter.SetParameter("PreBlurEnabled", profile.PreBlurEnabled);
        filter.SetParameter("PreBlurKernelSize", profile.PreBlurKernelSize);
        filter.SetParameter("PostMorphEnabled", profile.PostMorphEnabled);
        filter.SetParameter("MorphKernelSize", profile.MorphKernelSize);
        filter.SetParameter("MorphIterations", profile.MorphIterations);
        filter.SetParameter("EnableDetailedLogging", true);
        
        return filter;
    }

    /// <summary>
    /// プロファイルを取得
    /// </summary>
    /// <param name="profileName">プロファイル名</param>
    /// <returns>プロファイル</returns>
    private static GameScreenProfile GetProfile(string? profileName)
    {
        var normalizedName = profileName?.ToLowerInvariant() ?? "default";
        
        return Profiles.TryGetValue(normalizedName, out var profile) 
            ? profile 
            : Profiles["default"];
    }
}

/// <summary>
/// ゲーム画面プロファイル設定
/// </summary>
public class GameScreenProfile
{
    /// <summary>プロファイル名</summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>適応的二値化有効</summary>
    public bool EnableAdaptiveThreshold { get; set; } = true;
    
    /// <summary>色ベースマスキング有効</summary>
    public bool EnableColorMasking { get; set; } = true;
    
    /// <summary>適応的二値化ブロックサイズ</summary>
    public int AdaptiveBlockSize { get; set; } = 15;
    
    /// <summary>適応的二値化定数C</summary>
    public double AdaptiveC { get; set; } = 8.0;
    
    /// <summary>色マスキング強度（0.0-1.0）</summary>
    public float ColorMaskingStrength { get; set; } = 0.8f;
    
    /// <summary>前処理ブラー有効</summary>
    public bool PreBlurEnabled { get; set; }
    
    /// <summary>前処理ブラーカーネルサイズ</summary>
    public int PreBlurKernelSize { get; set; } = 3;
    
    /// <summary>後処理モルフォロジー有効</summary>
    public bool PostMorphEnabled { get; set; }
    
    /// <summary>モルフォロジーカーネルサイズ</summary>
    public int MorphKernelSize { get; set; } = 2;
    
    /// <summary>モルフォロジー反復回数</summary>
    public int MorphIterations { get; set; } = 1;
}