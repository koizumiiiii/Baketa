#pragma warning disable CS0618 // Type or member is obsolete
using System;
using Baketa.Application.Events;
using Baketa.Application.Services.Translation;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
// 名前空間競合を解決するためのエイリアス
using ApplicationTranslationSettings = Baketa.Application.Services.Translation.TranslationSettings;
using CoreTranslationSettings = Baketa.Core.Settings.TranslationSettings;
using TranslationMode = Baketa.Core.Abstractions.Services.TranslationMode;

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
        TranslationMode mode = TranslationMode.Singleshot,
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
        TranslationMode newMode = TranslationMode.Live,
        TranslationMode previousMode = TranslationMode.Singleshot)
    {
        return new TranslationModeChangedEvent(newMode, previousMode);
    }

    /// <summary>
    /// 翻訳実行イベントを作成
    /// </summary>
    public static TranslationTriggeredEvent CreateTranslationTriggeredEvent(
        TranslationMode mode = TranslationMode.Singleshot)
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

    /// <summary>
    /// テスト用の一般設定データを作成
    /// </summary>
    public static GeneralSettings CreateGeneralSettings() => new()
    {
        AutoStartWithWindows = false,
        MinimizeToTray = true,
        ShowExitConfirmation = true,
        AllowUsageStatistics = true,
        CheckForUpdatesAutomatically = true,
        PerformanceMode = false,
        MaxMemoryUsageMb = 512,
        LogLevel = LogLevel.Information,
        LogRetentionDays = 30,
        EnableDebugMode = false,
        ActiveGameProfile = null
    };

    /// <summary>
    /// テスト用のテーマ設定データを作成
    /// </summary>
    public static ThemeSettings CreateThemeSettings() => new()
    {
        AppTheme = UiTheme.Auto,
        AccentColor = 0xFF0078D4,
        FontFamily = "Yu Gothic UI",
        BaseFontSize = 12,
        HighContrastMode = false,
        EnableDpiScaling = true,
        CustomScaleFactor = 1.0,
        EnableAnimations = true,
        AnimationSpeed = AnimationSpeed.Normal,
        RoundedWindowCorners = true,
        EnableBlurEffect = true,
        EnableCustomCss = false,
        CustomCssFilePath = string.Empty
    };

    /// <summary>
    /// テスト用のメインUI設定データを作成
    /// </summary>
    public static MainUiSettings CreateMainUiSettings() => new()
    {
        PanelOpacity = 0.9,
        AutoHideWhenIdle = true,
        AutoHideDelaySeconds = 5,
        HighlightOnHover = true,
        PanelSize = UiSize.Medium,
        AlwaysOnTop = true,
        SingleShotDisplayTime = 3,
        EnableDragging = true,
        EnableBoundarySnap = true,
        BoundarySnapDistance = 20,
        EnableAnimations = true,
        AnimationDurationMs = 300,
        ThemeStyle = UiTheme.Auto,
        ShowDebugInfo = false,
        ShowFrameRate = false
    };

    /// <summary>
    /// テスト用のOCR設定データを作成
    /// </summary>
    public static OcrSettings CreateOcrSettings() => new()
    {
        EnableOcr = true,
        Language = "Japanese",
        ConfidenceThreshold = 0.8,
        EnableTextFiltering = true
    };

    /// <summary>
    /// キャプチャ設定のテストデータを作成します
    /// </summary>
    /// <returns>キャプチャ設定</returns>
    public static CaptureSettings CreateCaptureSettings() =>
        new()
        {
            IsEnabled = true,
            CaptureIntervalMs = 500,
            CaptureQuality = 85,
            AutoDetectCaptureArea = true,
            FixedCaptureAreaX = 0,
            FixedCaptureAreaY = 0,
            FixedCaptureAreaWidth = 800,
            FixedCaptureAreaHeight = 600,
            TargetMonitor = -1,
            ConsiderDpiScaling = true,
            UseHardwareAcceleration = true,
            EnableDifferenceDetection = true,
            DifferenceDetectionSensitivity = 30,
            DifferenceThreshold = 0.1,
            DifferenceDetectionGridSize = 16,
            FullscreenOptimization = true,
            AutoOptimizeForGames = true,
            SaveCaptureHistory = false,
            MaxCaptureHistoryCount = 100,
            SaveDebugCaptures = false,
            DebugCaptureSavePath = string.Empty
        };

    /// <summary>
    /// オーバーレイ設定のテストデータを作成します
    /// </summary>
    /// <returns>オーバーレイ設定</returns>
    public static OverlaySettings CreateOverlaySettings() =>
        new()
        {
            IsEnabled = true,
            Opacity = 0.9,
            FontSize = 14,
            BackgroundColor = 0xFF000000,
            TextColor = 0xFFFFFFFF,
            EnableAutoHideForAutoTranslation = false,
            AutoHideDelayForAutoTranslation = 5,
            EnableAutoHideForSingleShot = true,
            AutoHideDelayForSingleShot = 10,
            MaxWidth = 400,
            MaxHeight = 200,
            EnableTextTruncation = true,
            AllowManualClose = true,
            EnableClickThrough = false,
            FadeOutDurationMs = 500,
            PositionMode = OverlayPositionMode.NearText,
            FixedPositionX = 100,
            FixedPositionY = 100,
            ShowBorder = true,
            BorderColor = 0xFF808080,
            BorderThickness = 1,
            CornerRadius = 5,
            ShowDebugBounds = false
        };

    /// <summary>
    /// 拡張設定のテストデータを作成します
    /// </summary>
    /// <returns>拡張設定</returns>
    public static AdvancedSettings CreateAdvancedSettings() =>
        new()
        {
            EnableAdvancedFeatures = false,
            OptimizeMemoryUsage = true,
            OptimizeGarbageCollection = true,
            CpuAffinityMask = 0,
            ProcessPriority = ProcessPriority.Normal,
            WorkerThreadCount = 0,
            IoThreadCount = 0,
            BufferingStrategy = BufferingStrategy.Balanced,
            MaxQueueSize = 1000,
            NetworkTimeoutSeconds = 30,
            MaxHttpConnections = 10,
            RetryStrategy = RetryStrategy.Exponential,
            MaxRetryCount = 3,
            RetryDelayMs = 1000,
            EnableStatisticsCollection = true,
            StatisticsRetentionDays = 30,
            EnableProfiling = false,
            EnableAnomalyDetection = true,
            EnableAutoRecovery = true,
            EnableExperimentalFeatures = false,
            ExposeInternalApis = false,
            EnableDebugBreaks = false,
            GenerateMemoryDumps = false,
            CustomConfigPath = string.Empty
        };
}
