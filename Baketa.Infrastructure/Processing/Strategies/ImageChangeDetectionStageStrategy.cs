using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Models.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Baketa.Infrastructure.Processing.Strategies;

/// <summary>
/// 画像変化検知段階の処理戦略
/// P0で実装済みのIImageChangeDetectionServiceを活用
/// </summary>
public class ImageChangeDetectionStageStrategy : IProcessingStageStrategy
{
    private readonly IImageChangeDetectionService _changeDetectionService;
    private readonly IOptionsMonitor<ImageChangeDetectionSettings> _settings;
    private readonly ILogger<ImageChangeDetectionStageStrategy> _logger;
    
    public ProcessingStageType StageType => ProcessingStageType.ImageChangeDetection;
    public TimeSpan EstimatedProcessingTime => TimeSpan.FromMilliseconds(5);

    public ImageChangeDetectionStageStrategy(
        IImageChangeDetectionService changeDetectionService,
        IOptionsMonitor<ImageChangeDetectionSettings> settings,
        ILogger<ImageChangeDetectionStageStrategy> logger)
    {
        _changeDetectionService = changeDetectionService ?? throw new ArgumentNullException(nameof(changeDetectionService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProcessingStageResult> ExecuteAsync(ProcessingContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var settings = _settings.CurrentValue;
            if (!settings.Enabled)
            {
                _logger.LogDebug("画像変化検知が無効化されています");
                return ProcessingStageResult.CreateSkipped(StageType, "画像変化検知が無効化されています");
            }

            var input = context.Input;
            
            // 画像データを取得
            var currentImageData = await ConvertImageToByteArrayAsync(input.CapturedImage).ConfigureAwait(false);
            if (currentImageData == null)
            {
                _logger.LogWarning("画像データ変換失敗 - 変化ありとして処理継続");
                return ProcessingStageResult.CreateSuccess(StageType, 
                    ImageChangeDetectionResult.CreateFirstTime(), stopwatch.Elapsed);
            }

            // 前回画像との比較
            if (!string.IsNullOrEmpty(input.PreviousImageHash))
            {
                var currentHash = _changeDetectionService.GeneratePerceptualHash(currentImageData, settings.DefaultAlgorithm);
                var changePercentage = CalculateHashChangePercentage(input.PreviousImageHash, currentHash);
                
                var hasChanged = changePercentage >= settings.ChangeThreshold;
                
                _logger.LogDebug("画像変化検知完了 - 変化: {HasChanged}, 変化率: {ChangePercentage:F3}%, しきい値: {Threshold:F1}%",
                    hasChanged, changePercentage * 100, settings.ChangeThreshold * 100);
                
                var result = new ImageChangeDetectionResult
                {
                    HasChanged = hasChanged,
                    ChangePercentage = changePercentage,
                    PreviousHash = input.PreviousImageHash,
                    CurrentHash = currentHash,
                    ProcessingTime = stopwatch.Elapsed,
                    AlgorithmUsed = settings.DefaultAlgorithm.ToString()
                };
                
                return ProcessingStageResult.CreateSuccess(StageType, result);
            }

            // 初回実行時は変化ありとして処理継続
            _logger.LogDebug("初回画像キャプチャ - 変化ありとして処理継続");
            return ProcessingStageResult.CreateSuccess(StageType, 
                ImageChangeDetectionResult.CreateFirstTime(), stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "画像変化検知段階でエラーが発生");
            return ProcessingStageResult.CreateError(StageType, ex.Message, stopwatch.Elapsed);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    public bool ShouldExecute(ProcessingContext context)
    {
        var settings = _settings.CurrentValue;
        return settings.Enabled;
    }

    /// <summary>
    /// IImageをbyte配列に変換
    /// </summary>
    private static async Task<byte[]?> ConvertImageToByteArrayAsync(Baketa.Core.Abstractions.Imaging.IImage image)
    {
        try
        {
            var imageData = await image.ToByteArrayAsync().ConfigureAwait(false);
            if (imageData != null && imageData.Length > 0)
            {
                return imageData;
            }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// ハッシュ間の変化率を計算
    /// </summary>
    private static float CalculateHashChangePercentage(string hash1, string hash2)
    {
        if (string.IsNullOrEmpty(hash1) || string.IsNullOrEmpty(hash2) || hash1.Length != hash2.Length)
        {
            return 1.0f; // 完全に異なる
        }

        var diffCount = 0;
        for (int i = 0; i < hash1.Length; i++)
        {
            if (hash1[i] != hash2[i])
                diffCount++;
        }

        return (float)diffCount / hash1.Length;
    }
}