using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.Imaging.Filters;
using Microsoft.Extensions.Logging;
using OCRTextRegion = Baketa.Core.Abstractions.OCR.TextDetection.TextRegion;

namespace Baketa.Infrastructure.Imaging.Services;

/// <summary>
/// ゲーム画面特化OCR前処理サービス
/// Phase 3: OpenCvSharp を活用した高精度前処理パイプライン
/// </summary>
public sealed class GameOptimizedPreprocessingService(
    ILogger<GameOptimizedPreprocessingService> logger) : IOcrPreprocessingService
{
    private readonly ILogger<GameOptimizedPreprocessingService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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
            AdaptiveBlockSize = 15,
            AdaptiveC = 8.0,
            ColorMaskingStrength = 0.8f
        },
        ["darkbackground"] = new GameScreenProfile
        {
            Name = "暗い背景",
            EnableAdaptiveThreshold = true,
            EnableColorMasking = true,
            AdaptiveBlockSize = 11,  // より小さなブロックで細かな適応
            AdaptiveC = 12.0,        // より強い閾値調整
            ColorMaskingStrength = 0.9f,
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
            AdaptiveBlockSize = 17,
            AdaptiveC = 6.0,
            ColorMaskingStrength = 0.6f,
            PostMorphEnabled = true,
            MorphKernelSize = 1,
            MorphIterations = 2
        },
        ["anime"] = new GameScreenProfile
        {
            Name = "アニメ調",
            EnableAdaptiveThreshold = true,
            EnableColorMasking = true,
            AdaptiveBlockSize = 13,
            AdaptiveC = 10.0,
            ColorMaskingStrength = 0.95f,  // アニメ調は色抽出が効果的
            PreBlurEnabled = false,        // アニメ調は鮮明さを保持
            PostMorphEnabled = true,
            MorphKernelSize = 2,
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
            _logger.LogInformation("ゲーム最適化前処理開始: プロファイル={ProfileName}, サイズ={Width}x{Height}", 
                profile.Name, image.Width, image.Height);
            
            var processedImage = await ApplyGameOptimizedProcessingAsync(image, profile, cancellationToken)
                .ConfigureAwait(false);
            
            _logger.LogInformation("ゲーム最適化前処理完了: プロファイル={ProfileName}", profile.Name);
            
            return new OcrPreprocessingResult(
                false,
                null,
                processedImage,
                Array.Empty<OCRTextRegion>());
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("ゲーム最適化前処理がキャンセルされました");
            return new OcrPreprocessingResult(
                true,
                null,
                image,
                Array.Empty<OCRTextRegion>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ゲーム最適化前処理中にエラーが発生しました");
            return new OcrPreprocessingResult(
                false,
                ex,
                image,
                Array.Empty<OCRTextRegion>());
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
            
            return Array.Empty<OCRTextRegion>();
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
    /// ゲーム最適化処理を適用
    /// </summary>
    /// <param name="image">入力画像</param>
    /// <param name="profile">使用するプロファイル</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>処理済み画像</returns>
    private async Task<IAdvancedImage> ApplyGameOptimizedProcessingAsync(
        IAdvancedImage image, 
        GameScreenProfile profile, 
        CancellationToken cancellationToken)
    {
        var currentImage = image;
        var requiresDisposal = new List<IAdvancedImage>();

        try
        {
            // Step 1: 色ベースマスキング（背景ノイズ除去）
            if (profile.EnableColorMasking)
            {
                _logger.LogDebug("色ベースマスキング適用中...");
                
                var colorMaskingFilter = CreateColorMaskingFilter(profile);
                var maskedImage = await colorMaskingFilter.ApplyAsync(currentImage).ConfigureAwait(false);
                
                if (maskedImage != currentImage)
                {
                    requiresDisposal.Add(maskedImage);
                    currentImage = maskedImage;
                }
                
                _logger.LogDebug("色ベースマスキング完了");
            }

            // Step 2: 適応的二値化（照明変化対応）
            if (profile.EnableAdaptiveThreshold)
            {
                _logger.LogDebug("適応的二値化適用中...");
                
                var adaptiveThresholdFilter = CreateAdaptiveThresholdFilter(profile);
                var thresholdImage = await adaptiveThresholdFilter.ApplyAsync(currentImage).ConfigureAwait(false);
                
                if (thresholdImage != currentImage)
                {
                    requiresDisposal.Add(thresholdImage);
                    currentImage = thresholdImage;
                }
                
                _logger.LogDebug("適応的二値化完了");
            }

            _logger.LogInformation("ゲーム最適化処理完了: ColorMasking={ColorMasking}, AdaptiveThreshold={AdaptiveThreshold}",
                profile.EnableColorMasking, profile.EnableAdaptiveThreshold);

            return currentImage;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("ゲーム最適化処理がキャンセルされました");
            
            // 作成された中間画像を破棄
            foreach (var disposableImage in requiresDisposal)
            {
                disposableImage.Dispose();
            }
            
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ゲーム最適化処理中にエラーが発生しました");
            
            // 作成された中間画像を破棄
            foreach (var disposableImage in requiresDisposal)
            {
                disposableImage.Dispose();
            }
            
            throw;
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