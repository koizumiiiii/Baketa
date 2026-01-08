using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Events.TranslationEvents;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// [Issue #269] アナリティクスイベントプロセッサ
/// TranslationCompletedEventを購読して使用統計を記録
/// </summary>
public sealed class AnalyticsEventProcessor : IEventProcessor<TranslationCompletedEvent>
{
    private readonly IUsageAnalyticsService _analyticsService;
    private readonly ILogger<AnalyticsEventProcessor>? _logger;

    public AnalyticsEventProcessor(
        IUsageAnalyticsService analyticsService,
        ILogger<AnalyticsEventProcessor>? logger = null)
    {
        _analyticsService = analyticsService ?? throw new ArgumentNullException(nameof(analyticsService));
        _logger = logger;
    }

    /// <summary>
    /// 処理の優先度（通常優先度）
    /// </summary>
    public int Priority => 100;

    /// <summary>
    /// 同期実行が必要かどうか（Analytics は非同期で問題ない）
    /// </summary>
    public bool SynchronousExecution => false;

    /// <summary>
    /// 翻訳完了イベントを処理
    /// </summary>
    public Task HandleAsync(TranslationCompletedEvent eventData)
    {
        if (!_analyticsService.IsEnabled)
        {
            return Task.CompletedTask;
        }

        try
        {
            // プライバシーを考慮: テキスト内容は送信しない
            // 言語ペアと処理時間のみを記録
            var data = new Dictionary<string, object>
            {
                ["source_language"] = eventData.SourceLanguage,
                ["target_language"] = eventData.TargetLanguage,
                ["engine"] = eventData.Engine,
                ["processing_time_ms"] = eventData.ProcessingTimeMs
            };

            _analyticsService.TrackEvent("translation", data);
            _logger?.LogDebug("[Issue #269] translation イベント記録: {Source}->{Target}, {Time}ms",
                eventData.SourceLanguage, eventData.TargetLanguage, eventData.ProcessingTimeMs);
        }
        catch (Exception ex)
        {
            // アナリティクス失敗はアプリ動作に影響しない
            _logger?.LogWarning(ex, "[Issue #269] translation イベント記録失敗（継続）");
        }

        return Task.CompletedTask;
    }
}
