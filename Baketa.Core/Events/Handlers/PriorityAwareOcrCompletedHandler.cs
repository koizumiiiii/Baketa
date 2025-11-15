using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Models.OCR;
using Baketa.Core.Models.Translation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Events.Handlers;

/// <summary>
/// 優先度付きOCR完了ハンドラー - 画面中央からの距離に基づいた翻訳優先度制御
///
/// アーキテクチャ: Center-First Priority Translation System
/// - Phase A+対応: 複数グループの個別翻訳処理
/// - 距離ベースグルーピングされた結果を個別に処理
/// - 各グループが独立した翻訳リクエストとして処理される
/// </summary>
public class PriorityAwareOcrCompletedHandler : IEventProcessor<OcrCompletedEvent>
{
    private readonly IEventAggregator _eventAggregator;
    private readonly IUnifiedSettingsService _settingsService;
    private readonly ILogger<PriorityAwareOcrCompletedHandler> _logger;
    private readonly ILanguageConfigurationService _languageConfig;
    private readonly ITextChunkAggregatorService _textChunkAggregatorService;

    // Phase 5設計値
    private const int MaxConcurrentTranslations = 3; // SemaphoreSlim制限値
    private const double MinPriorityThreshold = 0.8; // 優先度フィルタリング閾値（画面端の20%を除外）
    /// <summary>
    /// デフォルト信頼度（暫定対応）
    /// TODO: 将来的にはOCR結果から実際の信頼度を取得する
    /// </summary>
    private const float DefaultConfidence = 0.95f;

    /// <inheritdoc />
    public int Priority => 100; // 既存ハンドラーより高い優先度で先行処理

    /// <inheritdoc />
    public bool SynchronousExecution => false;

    public PriorityAwareOcrCompletedHandler(
        IEventAggregator eventAggregator,
        IUnifiedSettingsService settingsService,
        ILogger<PriorityAwareOcrCompletedHandler> logger,
        ILanguageConfigurationService languageConfig,
        ITextChunkAggregatorService textChunkAggregatorService)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _languageConfig = languageConfig ?? throw new ArgumentNullException(nameof(languageConfig));
        _textChunkAggregatorService = textChunkAggregatorService ?? throw new ArgumentNullException(nameof(textChunkAggregatorService));
    }

    /// <inheritdoc />
    public async Task HandleAsync(OcrCompletedEvent eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        // 🚀 [DUPLICATE_FIX] TimedChunkAggregator統合処理有効時は個別処理をスキップ
        if (_textChunkAggregatorService.IsFeatureEnabled)
        {
            _logger.LogInformation("🚀 [DUPLICATE_FIX] TimedChunkAggregator統合処理有効のため個別翻訳処理をスキップ - 統合グルーピング翻訳を使用");
            Console.WriteLine("🚀 [DUPLICATE_FIX] 統合処理有効: 個別翻訳スキップ → TimedChunkAggregatorグルーピング翻訳使用");
            return; // 統合処理に委ねる
        }

        if (eventData.Results == null || !eventData.Results.Any())
        {
            _logger.LogInformation("🎯 [OCR_RESULT_EMPTY] OCR結果が空のためグループ翻訳をスキップ - Results: {ResultsNull}, Count: {Count}",
                eventData.Results == null, eventData.Results?.Count ?? 0);
            Console.WriteLine($"🎯 [OCR_RESULT_EMPTY] OCR結果なし - テキスト検出されませんでした");
            return;
        }

        try
        {
            _logger.LogInformation("🎯 Phase A+処理開始: {Count}個のグループを個別処理", eventData.Results.Count);

            // 統一言語設定サービスから言語ペア取得
            var languagePair = await _languageConfig.GetLanguagePairAsync().ConfigureAwait(false);
            var sourceLanguageCode = languagePair.SourceCode;
            var targetLanguageCode = languagePair.TargetCode;

            // 🎯 Phase A+修正: 各グループを個別に翻訳リクエスト発行
            var groupIndex = 1;
            foreach (var ocrResult in eventData.Results)
            {
                if (!string.IsNullOrWhiteSpace(ocrResult.Text))
                {
                    var logMessage = $"🎯 [GROUP_TRANSLATION] グループ{groupIndex}翻訳リクエスト開始 - テキスト: '{(ocrResult.Text.Length > 30 ? ocrResult.Text[..30] + "..." : ocrResult.Text)}', 座標: ({ocrResult.Bounds.X},{ocrResult.Bounds.Y},{ocrResult.Bounds.Width},{ocrResult.Bounds.Height}), 文字数: {ocrResult.Text.Length}";

                    _logger.LogInformation("🎯 [GROUP_TRANSLATION] グループ{GroupIndex}翻訳リクエスト開始 - テキスト: '{Text}', 座標: ({X},{Y},{W},{H}), 文字数: {Length}",
                        groupIndex,
                        ocrResult.Text.Length > 30 ? ocrResult.Text[..30] + "..." : ocrResult.Text,
                        ocrResult.Bounds.X, ocrResult.Bounds.Y,
                        ocrResult.Bounds.Width, ocrResult.Bounds.Height,
                        ocrResult.Text.Length);

                    Console.WriteLine($"🎯 [GROUP_TRANSLATION] グループ{groupIndex}翻訳リクエスト - " +
                        $"テキスト: '{(ocrResult.Text.Length > 30 ? ocrResult.Text[..30] + "..." : ocrResult.Text)}', " +
                        $"座標: ({ocrResult.Bounds.X},{ocrResult.Bounds.Y},{ocrResult.Bounds.Width},{ocrResult.Bounds.Height})");

                    var translationRequest = new TranslationRequestEvent(
                        ocrResult: ocrResult,
                        sourceLanguage: sourceLanguageCode,
                        targetLanguage: targetLanguageCode);

                    await _eventAggregator.PublishAsync(translationRequest).ConfigureAwait(false);

                    _logger.LogDebug("🎯 [GROUP_TRANSLATION] グループ{GroupIndex}翻訳リクエスト発行完了", groupIndex);
                }
                else
                {
                    _logger.LogDebug("🎯 [GROUP_TRANSLATION] グループ{GroupIndex}をスキップ - 空テキスト", groupIndex);
                }
                groupIndex++;
            }

            var completionMessage = $"🎯 Phase A+完了: {eventData.Results.Count}個の翻訳リクエストを発行";
            _logger.LogInformation("🎯 Phase A+完了: {Count}個の翻訳リクエストを発行", eventData.Results.Count);
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
    /// 複数のテキスト領域を包含する統合バウンディングボックスを計算
    /// Gemini推奨: 意味的結合ロジック実装
    /// </summary>
    private static Rectangle CalculateCombinedBoundingBox(IReadOnlyList<TextPriority> textPriorities)
    {
        if (textPriorities.Count == 0)
            return Rectangle.Empty;

        var firstBox = textPriorities[0].BoundingBox;
        var minX = firstBox.X;
        var minY = firstBox.Y;
        var maxX = firstBox.X + firstBox.Width;
        var maxY = firstBox.Y + firstBox.Height;

        // パフォーマンス改善: ToList()を避けて直接列挙
        for (int i = 1; i < textPriorities.Count; i++)
        {
            var box = textPriorities[i].BoundingBox;
            minX = Math.Min(minX, box.X);
            minY = Math.Min(minY, box.Y);
            maxX = Math.Max(maxX, box.X + box.Width);
            maxY = Math.Max(maxY, box.Y + box.Height);
        }

        return new Rectangle(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// 優先度重み付き信頼度計算
    /// 中央に近いテキスト（優先度高）の信頼度をより重視
    /// </summary>
    private static float CalculateWeightedConfidence(IReadOnlyList<TextPriority> textPriorities)
    {
        if (textPriorities.Count == 0)
            return DefaultConfidence;

        // 距離の逆数を重みとして使用（中央に近いほど高重み）
        var totalWeight = 0.0;
        var weightedSum = 0.0;

        foreach (var priority in textPriorities)
        {
            // 距離が0の場合を避けるため最小値を設定
            var distance = Math.Max(0.01, priority.DistanceFromCenterSquared);
            var weight = 1.0 / distance;

            totalWeight += weight;
            weightedSum += weight * DefaultConfidence; // 定数使用に修正
        }

        return totalWeight > 0 ? (float)(weightedSum / totalWeight) : DefaultConfidence;
    }

}
