using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Application.Services.UI;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Translation.Events;
using Baketa.Core.UI.Fullscreen;
using Microsoft.Extensions.Logging;

// [Issue #307] 2つの異なるTranslationCompletedEventタイプに対応
using TranslationEventsCompleted = Baketa.Core.Translation.Events.TranslationCompletedEvent;
using EventTypesCompleted = Baketa.Core.Events.EventTypes.TranslationCompletedEvent;

namespace Baketa.UI.Services;

/// <summary>
/// [Issue #269] アナリティクスイベントプロセッサ
/// TranslationCompletedEventを購読して使用統計を記録
/// [Issue #297] 名前空間修正: Core.Events.TranslationEvents → Core.Translation.Events
/// [Issue #307] 両方の名前空間のTranslationCompletedEventに対応 + ゲーム名収集
/// </summary>
public sealed class AnalyticsEventProcessor :
    IEventProcessor<TranslationEventsCompleted>,
    IEventProcessor<EventTypesCompleted>
{
    private readonly IUsageAnalyticsService _analyticsService;
    private readonly IFullscreenModeService? _fullscreenModeService;
    private readonly IWindowManagerAdapter? _windowManagerAdapter;
    private readonly IWindowManagementService? _windowManagementService;
    private readonly ILogger<AnalyticsEventProcessor>? _logger;

    // [Issue #307] ユーザー名等の個人情報を除去するための正規表現
    private static readonly Regex UserPathPattern = new(
        @"[A-Z]:\\Users\\[^\\]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public AnalyticsEventProcessor(
        IUsageAnalyticsService analyticsService,
        IFullscreenModeService? fullscreenModeService = null,
        IWindowManagerAdapter? windowManagerAdapter = null,
        IWindowManagementService? windowManagementService = null,
        ILogger<AnalyticsEventProcessor>? logger = null)
    {
        _analyticsService = analyticsService ?? throw new ArgumentNullException(nameof(analyticsService));
        _fullscreenModeService = fullscreenModeService;
        _windowManagerAdapter = windowManagerAdapter;
        _windowManagementService = windowManagementService;
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
    /// [Issue #307] 翻訳完了イベントを処理（Baketa.Core.Translation.Events版）
    /// StandardTranslationPipelineから発行されるイベント
    /// </summary>
    public Task HandleAsync(TranslationEventsCompleted eventData, CancellationToken cancellationToken = default)
    {
        return TrackTranslationEvent(
            eventData.SourceLanguage,
            eventData.TargetLanguage,
            eventData.TranslationEngine,
            eventData.ProcessingTimeMs);
    }

    /// <summary>
    /// [Issue #307] 翻訳完了イベントを処理（Baketa.Core.Events.EventTypes版）
    /// TranslationPipelineServiceから発行されるイベント
    /// </summary>
    public Task HandleAsync(EventTypesCompleted eventData, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("[Issue #307] EventTypesCompleted received: {Source}->{Target}, Engine={Engine}",
            eventData.SourceLanguage, eventData.TargetLanguage, eventData.EngineName);
        return TrackTranslationEvent(
            eventData.SourceLanguage,
            eventData.TargetLanguage,
            eventData.EngineName,  // EventTypes版はEngineNameプロパティを使用
            (long)eventData.ProcessingTime.TotalMilliseconds);
    }

    /// <summary>
    /// [Issue #307] 共通の翻訳イベント記録ロジック
    /// </summary>
    private Task TrackTranslationEvent(string sourceLanguage, string targetLanguage, string engine, long processingTimeMs)
    {
        _logger?.LogInformation("[Issue #307] TrackTranslationEvent: IsEnabled={IsEnabled}", _analyticsService.IsEnabled);
        if (!_analyticsService.IsEnabled)
        {
            _logger?.LogInformation("[Issue #307] Analytics disabled, skipping translation event");
            return Task.CompletedTask;
        }

        try
        {
            // プライバシーを考慮: テキスト内容は送信しない
            // 言語ペアと処理時間のみを記録
            var data = new Dictionary<string, object>
            {
                ["source_language"] = sourceLanguage,
                ["target_language"] = targetLanguage,
                ["engine"] = engine,
                ["processing_time_ms"] = processingTimeMs
            };

            // [Issue #307] ゲーム名（ウィンドウタイトル）を追加
            var gameTitle = GetSanitizedGameTitle();
            if (!string.IsNullOrEmpty(gameTitle))
            {
                data["game_title"] = gameTitle;
            }

            _analyticsService.TrackEvent("translation", data);
            _logger?.LogDebug("[Issue #307] translation イベント記録: {Source}->{Target}, Engine={Engine}, Game={Game}, {Time}ms",
                sourceLanguage, targetLanguage, engine, gameTitle ?? "(none)", processingTimeMs);
        }
        catch (Exception ex)
        {
            // アナリティクス失敗はアプリ動作に影響しない
            _logger?.LogWarning(ex, "[Issue #269] translation イベント記録失敗（継続）");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// [Issue #307] ターゲットウィンドウのタイトルを取得し、個人情報を除去して返す
    /// </summary>
    private string? GetSanitizedGameTitle()
    {
        try
        {
            // [Issue #307] 優先順位1: IWindowManagementService.SelectedWindow から取得
            // WindowManagementServiceで選択されたウィンドウのタイトルを直接取得
            if (_windowManagementService?.SelectedWindow != null)
            {
                var selectedTitle = _windowManagementService.SelectedWindow.Title;
                if (!string.IsNullOrWhiteSpace(selectedTitle))
                {
                    _logger?.LogDebug("[Issue #307] GetSanitizedGameTitle: IWindowManagementService から取得: '{Title}'", selectedTitle);
                    return SanitizeWindowTitle(selectedTitle);
                }
            }

            // [Issue #307] 優先順位2: IFullscreenModeService.TargetWindowHandle から取得（フォールバック）
            if (_fullscreenModeService != null && _windowManagerAdapter != null)
            {
                var windowHandle = _fullscreenModeService.TargetWindowHandle;
                if (windowHandle != nint.Zero)
                {
                    var title = _windowManagerAdapter.GetWindowTitle(windowHandle);
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        _logger?.LogDebug("[Issue #307] GetSanitizedGameTitle: IFullscreenModeService から取得: '{Title}'", title);
                        return SanitizeWindowTitle(title);
                    }
                }
            }

            _logger?.LogDebug("[Issue #307] GetSanitizedGameTitle: ウィンドウタイトル取得不可 (WindowManagement={WM}, Fullscreen={FS})",
                _windowManagementService?.SelectedWindow != null,
                _fullscreenModeService?.TargetWindowHandle != nint.Zero);
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[Issue #307] ゲームタイトル取得失敗（無視）");
            return null;
        }
    }

    /// <summary>
    /// [Issue #307] ウィンドウタイトルから個人情報を除去
    /// </summary>
    private static string SanitizeWindowTitle(string title)
    {
        // ユーザーパス（C:\Users\username\...）を除去
        var sanitized = UserPathPattern.Replace(title, "[USER_PATH]");

        // 長すぎるタイトルを切り詰め（100文字制限）
        if (sanitized.Length > 100)
        {
            sanitized = sanitized[..97] + "...";
        }

        return sanitized;
    }
}
