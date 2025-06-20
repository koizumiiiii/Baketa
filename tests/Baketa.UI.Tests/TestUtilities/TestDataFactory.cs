using System;
using Baketa.Application.Events;
using Baketa.Application.Models;
using Baketa.Application.Services.Translation;
using Baketa.Core.Settings;

// 名前空間競合を解決するためのエイリアス
using ApplicationTranslationSettings = Baketa.Application.Services.Translation.TranslationSettings;
using CoreTranslationSettings = Baketa.Core.Settings.TranslationSettings;

namespace Baketa.UI.Tests.TestUtilities;

/// <summary>
/// テストデータ生成用のファクトリークラス
/// </summary>
internal static class TestDataFactory
{
    /// <summary>
    /// サンプルの翻訳結果を作成
    /// </summary>
    public static TranslationResult CreateSampleTranslationResult(
        string? id = null,
        TranslationMode mode = TranslationMode.Manual,
        string originalText = "Hello World",
        string translatedText = "こんにちは 世界")
    {
        return new TranslationResult
        {
            Id = id ?? Guid.NewGuid().ToString("N")[..8],
            Mode = mode,
            OriginalText = originalText,
            TranslatedText = translatedText,
            DetectedLanguage = "en",
            TargetLanguage = "ja",
            Confidence = 0.95f,
            ProcessingTime = TimeSpan.FromMilliseconds(Random.Shared.Next(300, 1500))
        };
    }

    /// <summary>
    /// 翻訳モード変更イベントを作成
    /// </summary>
    public static TranslationModeChangedEvent CreateModeChangedEvent(
        TranslationMode newMode = TranslationMode.Automatic,
        TranslationMode previousMode = TranslationMode.Manual)
    {
        return new TranslationModeChangedEvent(newMode, previousMode);
    }

    /// <summary>
    /// 翻訳実行イベントを作成
    /// </summary>
    public static TranslationTriggeredEvent CreateTranslationTriggeredEvent(
        TranslationMode mode = TranslationMode.Manual)
    {
        return new TranslationTriggeredEvent(mode, DateTime.UtcNow);
    }

    /// <summary>
    /// デフォルトの翻訳設定を作成
    /// </summary>
    public static ApplicationTranslationSettings CreateDefaultSettings()
    {
        return new ApplicationTranslationSettings
        {
            SingleTranslationDisplaySeconds = 5,
            AutomaticTranslationIntervalMs = 1000,
            ChangeDetectionThreshold = 0.1f
        };
    }

    /// <summary>
    /// 翻訳進行状況データを作成
    /// </summary>
    public static TranslationProgress CreateTranslationProgress(
        string? id = null,
        TranslationStatus status = TranslationStatus.ProcessingOCR,
        float progress = 0.5f,
        string? message = null)
    {
        return new TranslationProgress
        {
            Id = id ?? Guid.NewGuid().ToString("N")[..8],
            Status = status,
            Progress = progress,
            Message = message ?? $"処理中... ({progress:P0})"
        };
    }
}
