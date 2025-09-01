using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Models.Processing;
using Baketa.Core.Models.ImageProcessing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Baketa.Infrastructure.Processing.Strategies;

/// <summary>
/// 拡張画像変化検知段階の処理戦略
/// P0: 3段階フィルタリング対応（Stage 1: 90% → Stage 2: 8% → Stage 3: 2%）
/// EnhancedImageChangeDetectionServiceによる高速化実装
/// </summary>
public class ImageChangeDetectionStageStrategy : IProcessingStageStrategy
{
    private readonly IImageChangeDetectionService _changeDetectionService;
    private readonly ILogger<ImageChangeDetectionStageStrategy> _logger;
    
    // 🔥 Critical Fix: 前回画像管理のためのフィールド追加
    private readonly object _imageLock = new object();
    private IImage? _previousImage;
    
    public ProcessingStageType StageType => ProcessingStageType.ImageChangeDetection;
    public TimeSpan EstimatedProcessingTime => TimeSpan.FromMilliseconds(2); // 3段階フィルタリングによる高速化

    public ImageChangeDetectionStageStrategy(
        IImageChangeDetectionService changeDetectionService,
        ILogger<ImageChangeDetectionStageStrategy> logger)
    {
        _changeDetectionService = changeDetectionService ?? throw new ArgumentNullException(nameof(changeDetectionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProcessingStageResult> ExecuteAsync(ProcessingContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var input = context.Input;
            var currentImage = input.CapturedImage;
            
            if (currentImage == null)
            {
                _logger.LogWarning("キャプチャ画像が null - 変化ありとして処理継続");
                return ProcessingStageResult.CreateSuccess(StageType, 
                    CreateLegacyResult(ImageChangeResult.CreateFirstTime("NULL", HashAlgorithmType.AverageHash, stopwatch.Elapsed)), 
                    stopwatch.Elapsed);
            }

            // コンテキストIDを生成（デフォルト）
            var contextId = "default";
            
            // 🔥 Critical Fix: 前回画像を適切に管理
            IImage? previousImageToUse;
            lock (_imageLock)
            {
                previousImageToUse = _previousImage;
            }

            // 3段階フィルタリング画像変化検知を実行
            var changeResult = await _changeDetectionService.DetectChangeAsync(
                previousImageToUse, 
                currentImage, 
                contextId, 
                cancellationToken).ConfigureAwait(false);

            // 🔥 Critical Fix: 前回画像を更新（リソース管理付き）
            lock (_imageLock)
            {
                // 古い画像を破棄
                if (_previousImage is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                _previousImage = currentImage;
            }

            var processingResult = CreateLegacyResult(changeResult);
            
            _logger.LogDebug("🎯 拡張画像変化検知完了 - 変化: {HasChanged}, Stage: {DetectionStage}, 変化率: {ChangePercentage:F3}%, 処理時間: {ProcessingTimeMs}ms",
                changeResult.HasChanged, 
                changeResult.DetectionStage, 
                changeResult.ChangePercentage * 100, 
                changeResult.ProcessingTime.TotalMilliseconds);

            // 統計情報をログ出力（パフォーマンス監視用）
            LogPerformanceStatistics();

            return ProcessingStageResult.CreateSuccess(StageType, processingResult, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 拡張画像変化検知段階でエラーが発生 - 処理時間: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            
            // エラー時は変化ありとして安全側で処理継続
            var fallbackResult = CreateLegacyResult(
                ImageChangeResult.CreateChanged("ERROR", "ERROR", 1.0f, HashAlgorithmType.AverageHash, stopwatch.Elapsed));
            
            return ProcessingStageResult.CreateSuccess(StageType, fallbackResult, stopwatch.Elapsed);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    public bool ShouldExecute(ProcessingContext context)
    {
        // 新しい実装では常に実行（3段階フィルタリングで効率的に処理）
        return context.Input?.CapturedImage != null;
    }

    /// <summary>
    /// 新しいImageChangeResultを既存のImageChangeDetectionResultに変換
    /// 後方互換性のためのアダプター
    /// </summary>
    private static ImageChangeDetectionResult CreateLegacyResult(ImageChangeResult changeResult)
    {
        return new ImageChangeDetectionResult
        {
            HasChanged = changeResult.HasChanged,
            ChangePercentage = changeResult.ChangePercentage,
            PreviousHash = changeResult.PreviousHash,
            CurrentHash = changeResult.CurrentHash,
            ProcessingTime = changeResult.ProcessingTime,
            AlgorithmUsed = changeResult.AlgorithmUsed.ToString(),
            // 拡張情報は現在のImageChangeDetectionResultでは未対応
            // 将来的に拡張予定
        };
    }

    /// <summary>
    /// パフォーマンス統計をログ出力
    /// </summary>
    private void LogPerformanceStatistics()
    {
        try
        {
            var statistics = _changeDetectionService.GetStatistics();
            
            if (statistics.TotalProcessed > 0 && statistics.TotalProcessed % 100 == 0) // 100回毎に統計出力
            {
                _logger.LogInformation("📊 画像変化検知統計 - 総処理: {TotalProcessed}, Stage1除外率: {Stage1FilterRate:F1}%, " +
                    "Stage1平均: {Stage1AvgMs:F1}ms, Stage2平均: {Stage2AvgMs:F1}ms, Stage3平均: {Stage3AvgMs:F1}ms, " +
                    "キャッシュサイズ: {CacheSize}",
                    statistics.TotalProcessed,
                    statistics.FilteringEfficiency * 100,
                    statistics.AverageStage1Time.TotalMilliseconds,
                    statistics.AverageStage2Time.TotalMilliseconds,
                    statistics.AverageStage3Time.TotalMilliseconds,
                    statistics.CurrentCacheSize);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "統計情報取得エラー");
        }
    }
}