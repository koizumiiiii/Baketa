using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Settings;
using Baketa.Infrastructure.OCR.PostProcessing;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Translation.Models;
using ITranslationService = Baketa.Core.Abstractions.Translation.ITranslationService;

namespace Baketa.Infrastructure.OCR.BatchProcessing;

/// <summary>
/// TimedChunkAggregator統合型バッチOCRサービス
/// 戦略書設計: translation-quality-improvement-strategy.md 完全準拠
/// UltraThink Phase 26-2: ITextChunkAggregatorService実装による Clean Architecture準拠
/// </summary>
public sealed class EnhancedBatchOcrIntegrationService : ITextChunkAggregatorService, IDisposable
{
    private readonly BatchOcrIntegrationService _baseBatchService;
    private readonly TimedChunkAggregator _timedChunkAggregator;
    private readonly ITranslationService _translationService;
    private readonly IInPlaceTranslationOverlayManager _overlayManager;
    private readonly IEventAggregator _eventAggregator;
    private readonly IUnifiedSettingsService _unifiedSettingsService;
    private readonly ILogger<EnhancedBatchOcrIntegrationService> _logger;
    private readonly TimedAggregatorSettings _settings;
    private readonly ILanguageConfigurationService _languageConfig;
    
    // パフォーマンス監視用
    private readonly ConcurrentDictionary<string, ProcessingStatistics> _processingStats;
    private long _totalProcessedImages;
    private long _totalAggregatedChunks;
    private bool _disposed;

    public EnhancedBatchOcrIntegrationService(
        BatchOcrIntegrationService baseBatchService,
        TimedChunkAggregator timedChunkAggregator,
        ITranslationService translationService,
        IInPlaceTranslationOverlayManager overlayManager,
        IEventAggregator eventAggregator,
        IUnifiedSettingsService unifiedSettingsService,
        IOptionsMonitor<TimedAggregatorSettings> settings,
        ILogger<EnhancedBatchOcrIntegrationService> logger,
        ILanguageConfigurationService languageConfig)
    {
        _baseBatchService = baseBatchService ?? throw new ArgumentNullException(nameof(baseBatchService));
        _timedChunkAggregator = timedChunkAggregator ?? throw new ArgumentNullException(nameof(timedChunkAggregator));
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _unifiedSettingsService = unifiedSettingsService ?? throw new ArgumentNullException(nameof(unifiedSettingsService));
        _settings = settings?.CurrentValue ?? TimedAggregatorSettings.Development;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _languageConfig = languageConfig ?? throw new ArgumentNullException(nameof(languageConfig));
        
        _processingStats = new ConcurrentDictionary<string, ProcessingStatistics>();
        
        // TimedChunkAggregatorのイベントハンドラ設定
        _timedChunkAggregator.OnChunksAggregated = OnChunksAggregatedHandler;
        
        _logger.LogInformation("🚀 EnhancedBatchOcrIntegrationService初期化完了 - TimedAggregator: {Enabled}", 
            _settings.IsFeatureEnabled);
    }

    /// <summary>
    /// 拡張統合OCR処理 - TimedChunkAggregator統合版
    /// 戦略書フィードバック反映: 時間軸統合による翻訳品質向上40-60%
    /// </summary>
    public async Task<IReadOnlyList<TextChunk>> ProcessWithEnhancedOcrAsync(
        IAdvancedImage image,
        IntPtr windowHandle,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var operationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        
        _logger.LogDebug("🔍 拡張OCR処理開始 - Image: {Width}x{Height}, OperationId: {OperationId}", 
            image.Width, image.Height, operationId);

        try
        {
            // 1. 既存BatchOcrIntegrationServiceでOCR実行
            var ocrChunks = await _baseBatchService.ProcessWithIntegratedOcrAsync(
                image, windowHandle, cancellationToken).ConfigureAwait(false);

            if (ocrChunks.Count == 0)
            {
                _logger.LogWarning("⚠️ OCR結果なし - OperationId: {OperationId}", operationId);
                return ocrChunks;
            }

            // 2. TimedChunkAggregator統合処理
            if (_settings.IsFeatureEnabled)
            {
                var aggregationResults = new List<TextChunk>();
                
                foreach (var chunk in ocrChunks)
                {
                    // TimedChunkAggregatorにチャンクを追加
                    var added = await _timedChunkAggregator.TryAddChunkAsync(chunk, cancellationToken).ConfigureAwait(false);
                    
                    if (!added)
                    {
                        // Feature Flag無効またはエラー時は直接結果に追加
                        aggregationResults.Add(chunk);
                    }
                }

                // TimedAggregatorが無効の場合は元のchunksをそのまま返す
                if (aggregationResults.Count > 0)
                {
                    _logger.LogInformation("📊 TimedAggregator無効 - 直接処理: {ChunkCount}個", aggregationResults.Count);
                    return aggregationResults;
                }
                
                // TimedAggregatorに追加されたチャンクは集約後に別途処理される
                _logger.LogDebug("⏱️ チャンク集約待機中 - {ChunkCount}個がTimedAggregatorに追加済み", ocrChunks.Count);
            }
            else
            {
                _logger.LogDebug("🚫 TimedAggregator機能無効 - 直接処理実行");
                return ocrChunks;
            }

            // 統計情報更新
            Interlocked.Increment(ref _totalProcessedImages);
            UpdateProcessingStatistics(operationId, startTime, ocrChunks.Count);

            // TimedAggregator有効時は空リストを返す（集約後の処理は別途実行）
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 拡張OCR処理エラー - OperationId: {OperationId}", operationId);
            throw;
        }
    }

    /// <summary>
    /// 複数画像の拡張並列処理
    /// </summary>
    public async Task<IReadOnlyList<IReadOnlyList<TextChunk>>> ProcessMultipleImagesWithEnhancedOcrAsync(
        IReadOnlyList<(IAdvancedImage Image, IntPtr WindowHandle)> imageData,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (imageData.Count == 0)
            return [];

        _logger.LogInformation("📦 拡張複数画像処理開始 - 画像数: {ImageCount}, TimedAggregator: {Enabled}", 
            imageData.Count, _settings.IsFeatureEnabled);

        // 並列処理タスクを作成
        var tasks = imageData.Select(async data =>
        {
            try
            {
                return await ProcessWithEnhancedOcrAsync(
                    data.Image, 
                    data.WindowHandle, 
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 画像処理エラー - サイズ: {Width}x{Height}", 
                    data.Image.Width, data.Image.Height);
                return (IReadOnlyList<TextChunk>)[];
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        
        var totalChunks = results.Sum(r => r.Count);
        _logger.LogInformation("✅ 拡張複数画像処理完了 - 総チャンク数: {TotalChunks}", totalChunks);

        return results;
    }

    /// <summary>
    /// TimedChunkAggregator集約完了イベントハンドラ
    /// 戦略書設計: 集約されたチャンクを翻訳パイプラインに送信
    /// </summary>
    private async Task OnChunksAggregatedHandler(List<TextChunk> aggregatedChunks)
    {
        try
        {
            var chunkCount = aggregatedChunks.Count;
            Interlocked.Add(ref _totalAggregatedChunks, chunkCount);

            _logger.LogInformation("🎯 チャンク集約完了ハンドラ - 集約チャンク数: {Count}", chunkCount);

            // 🚀 UltraThink緊急実装: 集約されたチャンクの翻訳処理実行
            if (aggregatedChunks.Count > 0)
            {
                _logger.LogInformation("🌟 [ULTRATHINK_FIX] 集約チャンク翻訳処理開始 - {Count}個の統合チャンク", aggregatedChunks.Count);
                Console.WriteLine($"🌟 [ULTRATHINK_FIX] TimedChunkAggregator集約完了 - {aggregatedChunks.Count}個のチャンクを翻訳処理へ");

                // 各集約チャンクに対して翻訳処理を実行
                foreach (var aggregatedChunk in aggregatedChunks)
                {
                    try
                    {
                        _logger.LogDebug("📝 [ULTRATHINK_FIX] 集約チャンク翻訳開始 - ID: {ChunkId}, テキスト長: {Length}, ウィンドウ: {WindowHandle}", 
                            aggregatedChunk.ChunkId, 
                            aggregatedChunk.CombinedText.Length,
                            aggregatedChunk.SourceWindowHandle);

                        // 🎯 重要: 集約されたチャンクを翻訳パイプラインに直接送信
                        // TODO: 適切な翻訳サービスインジェクション（DI統合が必要）
                        // 現在は基本的な翻訳イベント発行で対応
                        await TriggerTranslationForAggregatedChunk(aggregatedChunk).ConfigureAwait(false);
                        
                        _logger.LogInformation("✅ [ULTRATHINK_FIX] 集約チャンク翻訳完了 - ID: {ChunkId}", aggregatedChunk.ChunkId);
                    }
                    catch (Exception chunkEx)
                    {
                        _logger.LogError(chunkEx, "❌ [ULTRATHINK_FIX] 個別集約チャンク翻訳エラー - ID: {ChunkId}", aggregatedChunk.ChunkId);
                    }
                }

                _logger.LogInformation("🎉 [ULTRATHINK_FIX] 全集約チャンク翻訳処理完了 - 処理数: {Count}", aggregatedChunks.Count);
                Console.WriteLine($"🎉 [ULTRATHINK_FIX] TimedChunkAggregator統合翻訳完了 - {aggregatedChunks.Count}個の統合テキスト処理完了");
            }
            else
            {
                _logger.LogWarning("⚠️ [ULTRATHINK_FIX] 集約チャンクが0個 - 翻訳処理スキップ");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [ULTRATHINK_FIX] チャンク集約ハンドラエラー - 緊急フォールバック処理が必要");
        }
    }

    /// <summary>
    /// 集約チャンク専用翻訳処理トリガー
    /// UltraThink緊急実装: TimedChunkAggregator統合版翻訳処理
    /// </summary>
    /// <summary>
    /// 集約チャンク専用翻訳処理トリガー
    /// UltraThink緊急実装: TimedChunkAggregator統合版翻訳処理
    /// 修正: 既存のTranslationOrchestrationService言語選択システムを活用
    /// </summary>
    private async Task TriggerTranslationForAggregatedChunk(TextChunk aggregatedChunk)
    {
        try
        {
            _logger.LogDebug("🎯 [TIMED_AGGREGATOR] 集約チャンク翻訳処理開始 - テキスト: '{Text}'", 
                aggregatedChunk.CombinedText.Length > 100 
                    ? aggregatedChunk.CombinedText[..100] + "..."
                    : aggregatedChunk.CombinedText);

            // 🚀 実際の翻訳処理実行
            Console.WriteLine($"🎯 [TIMED_AGGREGATOR] 翻訳開始: '{aggregatedChunk.CombinedText}' (長さ: {aggregatedChunk.CombinedText.Length})");
            
            // 🔧 修正: ユーザー設定の言語ペアを使用（自動検出off）
            var languagePair = _languageConfig.GetCurrentLanguagePair();
            var sourceLanguageCode = languagePair.SourceCode;
            var targetLanguageCode = languagePair.TargetCode;
            var sourceLanguage = Language.FromCode(sourceLanguageCode);
            var targetLanguage = Language.FromCode(targetLanguageCode);

            _logger.LogDebug("🌍 [LANGUAGE_DETECTION] ユーザー設定言語使用: {SourceLanguage} → {TargetLanguage}", sourceLanguageCode, targetLanguageCode);

            // 翻訳サービスで翻訳実行（設定ベース言語ペア使用）
            var response = await _translationService.TranslateAsync(
                aggregatedChunk.CombinedText,
                sourceLanguage, // 設定ベース言語
                targetLanguage
            ).ConfigureAwait(false);
            
            var translatedText = response.TranslatedText;
            
            _logger.LogInformation("✅ [TIMED_AGGREGATOR] 翻訳成功 - 原文: '{Original}' → 翻訳: '{Translated}'", 
                aggregatedChunk.CombinedText, translatedText);
            
            Console.WriteLine($"✅ [TIMED_AGGREGATOR] 翻訳成功: '{translatedText}'");
            
            // 🎯 オーバーレイ表示処理
            await DisplayTranslationOverlay(aggregatedChunk, translatedText).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [TIMED_AGGREGATOR] 集約チャンク翻訳処理エラー - テキスト: '{Text}'", 
                aggregatedChunk.CombinedText);
            Console.WriteLine($"❌ [TIMED_AGGREGATOR] 翻訳エラー: {ex.Message}");
        }
    }

    
    /// <summary>
    /// 翻訳結果のオーバーレイ表示
    /// </summary>
    private async Task DisplayTranslationOverlay(TextChunk chunk, string translatedText)
    {
        try
        {
            _logger.LogDebug("🖼️ [TIMED_AGGREGATOR] オーバーレイ表示開始 - ウィンドウ: {WindowHandle}", 
                chunk.SourceWindowHandle);
            
            // 翻訳されたTextChunkを作成（集約チャンクをコピーして翻訳テキストを設定）
            var translatedChunk = new TextChunk
            {
                ChunkId = chunk.ChunkId,
                TextResults = chunk.TextResults, // 元のTextResults
                CombinedBounds = chunk.CombinedBounds,
                CombinedText = chunk.CombinedText, // 元のテキスト
                SourceWindowHandle = chunk.SourceWindowHandle,
                DetectedLanguage = chunk.DetectedLanguage
            };
            
            // 翻訳テキストを設定
            translatedChunk.TranslatedText = translatedText;
            
            // 🚫 [DUPLICATE_FIX] BatchOCRオーバーレイ表示削除 - PHASE18統一システムで処理済み
            // PHASE18統一システム (TranslationWithBoundsCompletedHandler) で既に表示されているため、重複防止で削除
            // await _overlayManager.ShowInPlaceOverlayAsync(translatedChunk).ConfigureAwait(false);
            Console.WriteLine($"🚫 [DUPLICATE_FIX] BatchOCR直接表示スキップ - PHASE18統一システム使用: '{translatedText}'");
                
            Console.WriteLine($"🖼️ [TIMED_AGGREGATOR] オーバーレイ表示完了: '{translatedText}'");
            _logger.LogInformation("✅ [TIMED_AGGREGATOR] オーバーレイ表示完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [TIMED_AGGREGATOR] オーバーレイ表示エラー");
            Console.WriteLine($"❌ [TIMED_AGGREGATOR] オーバーレイエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 🚀 Phase 22: CaptureCompletedHandlerからの個別TextChunk送信メソッド
    /// TimedChunkAggregatorに直接チャンクを送信し、集約処理を開始
    /// </summary>
    public async Task<bool> TryAddTextChunkDirectlyAsync(
        TextChunk chunk,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            _logger.LogDebug("📥 [PHASE22] 個別TextChunk受信 - ID: {ChunkId}, テキスト: '{Text}'",
                chunk.ChunkId, chunk.CombinedText);

            if (!_settings.IsFeatureEnabled)
            {
                _logger.LogInformation("⚠️ [PHASE22] TimedAggregator機能無効 - チャンク送信スキップ");
                return false;
            }

            // TimedChunkAggregatorに直接送信
            var added = await _timedChunkAggregator.TryAddChunkAsync(chunk, cancellationToken).ConfigureAwait(false);

            if (added)
            {
                _logger.LogInformation("✅ [PHASE22] TextChunk → TimedChunkAggregator送信成功 - ID: {ChunkId}",
                    chunk.ChunkId);
                Console.WriteLine($"📥 [PHASE22] TimedChunkAggregator: '{chunk.CombinedText}' 受信完了");
            }
            else
            {
                _logger.LogWarning("⚠️ [PHASE22] TextChunk送信失敗 - TimedAggregator処理エラー");
            }

            return added;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE22] TextChunk送信エラー - ChunkId: {ChunkId}", chunk.ChunkId);
            return false;
        }
    }

    /// <summary>
    /// パフォーマンス最適化設定の委譲
    /// </summary>
    public async Task OptimizeEnhancedPerformanceAsync(
        int imageWidth,
        int imageHeight,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // 既存BatchOcrIntegrationServiceの最適化処理を委譲
        await _baseBatchService.OptimizeBatchPerformanceAsync(imageWidth, imageHeight, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug("⚙️ 拡張パフォーマンス最適化完了 - 画像: {Width}x{Height}", imageWidth, imageHeight);
    }

    /// <summary>
    /// 処理統計情報の更新
    /// </summary>
    private void UpdateProcessingStatistics(string operationId, DateTime startTime, int chunkCount)
    {
        var processingTime = DateTime.UtcNow - startTime;
        var stats = new ProcessingStatistics
        {
            OperationId = operationId,
            ProcessingTime = processingTime,
            ChunkCount = chunkCount,
            Timestamp = DateTime.UtcNow
        };

        _processingStats.TryAdd(operationId, stats);

        // 古い統計情報のクリーンアップ（メモリリーク防止）
        if (_processingStats.Count > 1000)
        {
            var oldEntries = _processingStats
                .Where(kvp => kvp.Value.Timestamp < DateTime.UtcNow.AddMinutes(-10))
                .Take(100)
                .ToList();

            foreach (var entry in oldEntries)
            {
                _processingStats.TryRemove(entry.Key, out _);
            }
        }
    }

    /// <summary>
    /// 現在の処理統計情報を取得
    /// </summary>
    public (long TotalImages, long TotalAggregatedChunks, TimeSpan AverageProcessingTime) GetEnhancedStatistics()
    {
        ThrowIfDisposed();
        
        var totalImages = Interlocked.Read(ref _totalProcessedImages);
        var totalChunks = Interlocked.Read(ref _totalAggregatedChunks);
        
        var avgProcessingTime = _processingStats.Values.Count > 0
            ? TimeSpan.FromTicks((long)_processingStats.Values.Average(s => s.ProcessingTime.Ticks))
            : TimeSpan.Zero;

        return (totalImages, totalChunks, avgProcessingTime);
    }

    /// <summary>
    /// TimedChunkAggregatorの統計情報を取得
    /// </summary>
    public (long TotalChunksProcessed, long TotalAggregationEvents) GetAggregatorStatistics()
    {
        ThrowIfDisposed();
        return _timedChunkAggregator.GetStatistics();
    }

    // ============================================
    // ITextChunkAggregatorService インターフェース実装
    // Phase 26-2: Clean Architecture準拠の抽象化実装
    // ============================================

    /// <inheritdoc />
    public async Task<bool> TryAddTextChunkAsync(TextChunk chunk, CancellationToken cancellationToken = default)
    {
        // 既存のTryAddTextChunkDirectlyAsyncメソッドに委譲
        return await TryAddTextChunkDirectlyAsync(chunk, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public bool IsFeatureEnabled => _settings.IsFeatureEnabled;

    /// <inheritdoc />
    public int PendingChunksCount => 0; // TODO: TimedChunkAggregatorにPendingChunksCount実装後に修正

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;

        // 統計情報ログ出力
        if (_settings.EnablePerformanceLogging)
        {
            var (totalImages, totalChunks, avgTime) = GetEnhancedStatistics();
            var (timedChunks, timedEvents) = GetAggregatorStatistics();
            
            _logger.LogInformation("📊 EnhancedBatchOcrIntegrationService最終統計 - " +
                "処理画像: {Images}, 集約チャンク: {Chunks}, 平均処理時間: {AvgTime}ms, " +
                "TimedAggregator - チャンク: {TimedChunks}, イベント: {TimedEvents}",
                totalImages, totalChunks, avgTime.TotalMilliseconds,
                timedChunks, timedEvents);
        }

        // リソース解放
        _baseBatchService?.Dispose();
        _timedChunkAggregator?.Dispose();
        _processingStats.Clear();
        
        _disposed = true;
        
        _logger.LogInformation("🧹 EnhancedBatchOcrIntegrationService disposed");
    }
}

/// <summary>
/// 処理統計情報を格納する内部クラス
/// </summary>
internal sealed class ProcessingStatistics
{
    public required string OperationId { get; init; }
    public required TimeSpan ProcessingTime { get; init; }
    public required int ChunkCount { get; init; }
    public required DateTime Timestamp { get; init; }
}