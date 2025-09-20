using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Models.OCR;
using Baketa.Core.Models.Translation;

namespace Baketa.Core.Events.Handlers;

/// <summary>
/// 優先度付きOCR完了ハンドラー - 画面中央からの距離に基づいた翻訳優先度制御
/// 
/// アーキテクチャ: Center-First Priority Translation System
/// - Phase 5対応: 画面中央優先度翻訳システム実装
/// - 座標正規化による解像度非依存処理
/// - 二乗ユークリッド距離による高速優先度計算
/// - SemaphoreSlimによる制限付き並列翻訳（3-5並列）
/// </summary>
public class PriorityAwareOcrCompletedHandler : IEventProcessor<OcrCompletedEvent>
{
    private readonly IEventAggregator _eventAggregator;
    private readonly IUnifiedSettingsService _settingsService;
    private readonly ILogger<PriorityAwareOcrCompletedHandler> _logger;
    private readonly IConfiguration _configuration;
    
    // Phase 5設計値
    private const int MaxConcurrentTranslations = 3; // SemaphoreSlim制限値
    private const double MinPriorityThreshold = 0.8; // 優先度フィルタリング閾値（画面端の20%を除外）
    
    /// <inheritdoc />
    public int Priority => 100; // 既存ハンドラーより高い優先度で先行処理
    
    /// <inheritdoc />
    public bool SynchronousExecution => false;

    public PriorityAwareOcrCompletedHandler(
        IEventAggregator eventAggregator,
        IUnifiedSettingsService settingsService,
        ILogger<PriorityAwareOcrCompletedHandler> logger,
        IConfiguration configuration)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc />
    public async Task HandleAsync(OcrCompletedEvent eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Results == null || !eventData.Results.Any())
        {
            _logger.LogDebug("優先度付きOCR処理: OCR結果が空のためスキップ");
            return;
        }

        try
        {
            _logger.LogInformation("🎯 Phase5優先度付きOCR処理開始: {Count}個のテキスト領域を処理", eventData.Results.Count);
            
            // 翻訳設定取得（設定ベース言語使用）
            var defaultSourceLanguage = _configuration.GetValue<string>("Translation:DefaultSourceLanguage", "en");
            var defaultTargetLanguage = _configuration.GetValue<string>("Translation:DefaultTargetLanguage", "ja");
            var translationSettings = _settingsService.GetTranslationSettings();

            var sourceLanguageCode = translationSettings.AutoDetectSourceLanguage
                ? defaultSourceLanguage
                : translationSettings.DefaultSourceLanguage;
            var targetLanguageCode = translationSettings.DefaultTargetLanguage;

            // 画面サイズ情報取得（画像から推定）
            var screenWidth = eventData.SourceImage?.Width ?? 1920; // デフォルト値
            var screenHeight = eventData.SourceImage?.Height ?? 1080; // デフォルト値

            // Step 3: OCR結果に優先度付け処理を追加
            var prioritizedTexts = await CreatePrioritizedTextListAsync(eventData.Results, screenWidth, screenHeight)
                .ConfigureAwait(false);

            _logger.LogInformation("🎯 優先度付けComplete: {PriorityCount}個（中央優先順）、元件数: {OriginalCount}個", 
                prioritizedTexts.Count, eventData.Results.Count);

            // Step 4-5: 優先度キューシステム + SemaphoreSlim制限付き並列翻訳
            await ProcessPrioritizedTranslationsAsync(prioritizedTexts, sourceLanguageCode, targetLanguageCode)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "優先度付きOCR処理でエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// OCR結果から優先度付きテキストリストを作成
    /// </summary>
    private async Task<List<TextPriority>> CreatePrioritizedTextListAsync(IReadOnlyList<OcrResult> ocrResults, int screenWidth, int screenHeight)
    {
        var prioritizedList = new List<TextPriority>();

        foreach (var ocrResult in ocrResults)
        {
            try
            {
                var textPriority = TextPriority.Create(
                    originalText: ocrResult.Text,
                    boundingBox: ocrResult.Bounds,
                    screenWidth: screenWidth,
                    screenHeight: screenHeight);

                // 優先度フィルタリング（画面端の20%を除外）
                if (textPriority.DistanceFromCenterSquared <= MinPriorityThreshold)
                {
                    prioritizedList.Add(textPriority);
                }
                else
                {
                    _logger.LogTrace("優先度フィルタリングによりスキップ: '{Text}' (距離: {Distance:F3})", 
                        ocrResult.Text[..Math.Min(15, ocrResult.Text.Length)], 
                        textPriority.DistanceFromCenterSquared);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "テキスト優先度計算でエラー: '{Text}'", ocrResult.Text);
            }
        }

        // 優先度順（中央からの距離の昇順）でソート
        prioritizedList.Sort((a, b) => a.DistanceFromCenterSquared.CompareTo(b.DistanceFromCenterSquared));

        _logger.LogDebug("優先度順ソート完了: 最優先'{FirstText}' (距離:{FirstDist:F3}) → 最低優先'{LastText}' (距離:{LastDist:F3})",
            prioritizedList.FirstOrDefault()?.OriginalText[..Math.Min(10, prioritizedList.FirstOrDefault()?.OriginalText?.Length ?? 0)] ?? "N/A",
            prioritizedList.FirstOrDefault()?.DistanceFromCenterSquared ?? 0,
            prioritizedList.LastOrDefault()?.OriginalText[..Math.Min(10, prioritizedList.LastOrDefault()?.OriginalText?.Length ?? 0)] ?? "N/A",
            prioritizedList.LastOrDefault()?.DistanceFromCenterSquared ?? 0);

        return prioritizedList;
    }

    /// <summary>
    /// Step 4-5: 優先度キューシステム + SemaphoreSlim制限付き並列翻訳処理
    /// PriorityQueue<TextPriority, double>による中央優先順処理
    /// </summary>
    private async Task ProcessPrioritizedTranslationsAsync(List<TextPriority> prioritizedTexts, string sourceLanguage, string targetLanguage)
    {
        if (prioritizedTexts == null || prioritizedTexts.Count == 0)
            return;

        // Step 4: PriorityQueue<TextPriority, double>による優先度キューシステム
        var priorityQueue = new PriorityQueue<TextPriority, double>();
        foreach (var prioritizedText in prioritizedTexts)
        {
            // 距離が小さいほど優先度が高い（昇順ソート）
            priorityQueue.Enqueue(prioritizedText, prioritizedText.DistanceFromCenterSquared);
        }

        _logger.LogInformation("🎯 PriorityQueue初期化完了: {QueueSize}個のテキストを優先度順にキューイング", priorityQueue.Count);

        // Step 5: SemaphoreSlim(3)による制限付き並列翻訳処理
        using var semaphore = new SemaphoreSlim(MaxConcurrentTranslations, MaxConcurrentTranslations);
        var translationTasks = new List<Task>();

        var processedCount = 0;
        while (priorityQueue.Count > 0)
        {
            // 優先度順にデキュー
            var textPriority = priorityQueue.Dequeue();
            processedCount++;

            // SemaphoreSlimによる並列制限
            await semaphore.WaitAsync().ConfigureAwait(false);

            var translationTask = ProcessSingleTranslationAsync(textPriority, sourceLanguage, targetLanguage, processedCount, semaphore);
            translationTasks.Add(translationTask);
        }

        // 全翻訳タスクの完了を待機
        await Task.WhenAll(translationTasks).ConfigureAwait(false);
        
        _logger.LogInformation("🎯 優先度付き並列翻訳完了: {TotalProcessed}個の翻訳処理完了", processedCount);
    }

    /// <summary>
    /// 単一テキストの翻訳処理（並列実行される）
    /// </summary>
    private async Task ProcessSingleTranslationAsync(TextPriority textPriority, string sourceLanguage, string targetLanguage, int priority, SemaphoreSlim semaphore)
    {
        try
        {
            _logger.LogTrace("🎯 並列翻訳開始: 優先度{Priority} '{Text}' (距離:{Distance:F3})", 
                priority, textPriority.OriginalText[..Math.Min(10, textPriority.OriginalText.Length)], 
                textPriority.DistanceFromCenterSquared);

            // 既存のTranslationRequestEventシステムを使用
            var ocrResult = new OcrResult(
                text: textPriority.OriginalText,
                bounds: textPriority.BoundingBox,
                confidence: 0.95f);

            var translationRequestEvent = new TranslationRequestEvent(
                ocrResult: ocrResult,
                sourceLanguage: sourceLanguage,
                targetLanguage: targetLanguage);

            await _eventAggregator.PublishAsync(translationRequestEvent).ConfigureAwait(false);

            _logger.LogTrace("🎯 並列翻訳完了: 優先度{Priority} '{Text}'", 
                priority, textPriority.OriginalText[..Math.Min(10, textPriority.OriginalText.Length)]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "並列翻訳処理エラー: 優先度{Priority} '{Text}'", 
                priority, textPriority.OriginalText);
        }
        finally
        {
            // SemaphoreSlim解放
            semaphore.Release();
        }
    }

    /// <summary>
    /// 優先度付きテキストリストから個別翻訳リクエストイベントを発行
    /// 旧実装（デバッグ用保持）
    /// </summary>
    private async Task PublishIndividualTranslationRequestsAsync(List<TextPriority> prioritizedTexts, string sourceLanguage, string targetLanguage)
    {
        var publishedCount = 0;
        var skippedCount = 0;

        foreach (var prioritizedText in prioritizedTexts)
        {
            try
            {
                // 既存のOcrResultオブジェクトを再作成（Boundsは元の座標を保持）
                var ocrResult = new OcrResult(
                    text: prioritizedText.OriginalText,
                    bounds: prioritizedText.BoundingBox,
                    confidence: 0.95f); // デフォルト信頼度

                var translationRequestEvent = new TranslationRequestEvent(
                    ocrResult: ocrResult,
                    sourceLanguage: sourceLanguage,
                    targetLanguage: targetLanguage);

                await _eventAggregator.PublishAsync(translationRequestEvent).ConfigureAwait(false);
                publishedCount++;

                _logger.LogTrace("🎯 優先度付き翻訳リクエスト発行: '{Text}' (優先度: {Priority:F3})", 
                    prioritizedText.OriginalText[..Math.Min(15, prioritizedText.OriginalText.Length)], 
                    prioritizedText.DistanceFromCenterSquared);
            }
            catch (Exception ex)
            {
                skippedCount++;
                _logger.LogError(ex, "翻訳リクエストイベント発行エラー: '{Text}'", prioritizedText.OriginalText);
            }
        }

        _logger.LogInformation("🎯 優先度付き翻訳リクエスト発行完了: 成功 {Published}件, エラー {Skipped}件", 
            publishedCount, skippedCount);
    }
}