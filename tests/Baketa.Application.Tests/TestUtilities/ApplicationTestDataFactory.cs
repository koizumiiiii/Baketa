using System;
using Baketa.Application.Models;
using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Settings;
using Moq;

// 名前空間競合を解決するためのエイリアス
using ApplicationTranslationSettings = Baketa.Application.Services.Translation.TranslationSettings;
using CoreTranslationSettings = Baketa.Core.Settings.TranslationSettings;

namespace Baketa.Application.Tests.TestUtilities;

/// <summary>
/// アプリケーション層テスト用のデータファクトリー
/// </summary>
internal static class ApplicationTestDataFactory
{
    /// <summary>
    /// テスト用のキャプチャオプションを作成
    /// </summary>
    public static CaptureOptions CreateTestCaptureOptions(
        int quality = 85,
        int captureInterval = 1000,
        bool includeCursor = false)
    {
        return new CaptureOptions
        {
            Quality = quality,
            CaptureInterval = captureInterval,
            IncludeCursor = includeCursor,
            OptimizationLevel = 2
        };
    }

    /// <summary>
    /// テスト用の翻訳設定を作成
    /// </summary>
    public static ApplicationTranslationSettings CreateTestTranslationSettings(
        int displaySeconds = 5,
        int intervalMs = 1000,
        float threshold = 0.1f)
    {
        return new ApplicationTranslationSettings
        {
            SingleTranslationDisplaySeconds = displaySeconds,
            AutomaticTranslationIntervalMs = intervalMs,
            ChangeDetectionThreshold = threshold
        };
    }

    /// <summary>
    /// モック画像オブジェクトを作成
    /// </summary>
    public static Mock<IImage> CreateMockImage(
        int width = 1920,
        int height = 1080)
    {
        var imageMock = new Mock<IImage>();
        
        imageMock.SetupGet(x => x.Width).Returns(width);
        imageMock.SetupGet(x => x.Height).Returns(height);
        
        // Dispose メソッドのモック
        imageMock.Setup(x => x.Dispose());
        
        return imageMock;
    }

    /// <summary>
    /// 翻訳処理結果を作成
    /// </summary>
    public static TranslationResult CreateTranslationResult(
        string? id = null,
        TranslationMode mode = TranslationMode.Manual,
        bool isSuccessful = true)
    {
        return new TranslationResult
        {
            Id = id ?? GenerateTestId(),
            Mode = mode,
            OriginalText = isSuccessful ? "Sample text" : string.Empty,
            TranslatedText = isSuccessful ? "サンプルテキスト" : "翻訳エラー",
            DetectedLanguage = "en",
            TargetLanguage = "ja",
            Confidence = isSuccessful ? 0.95f : 0.0f,
            ProcessingTime = TimeSpan.FromMilliseconds(Random.Shared.Next(200, 2000)),
            DisplayDuration = mode == TranslationMode.Manual 
                ? TimeSpan.FromSeconds(5) 
                : TimeSpan.Zero
        };
    }

    /// <summary>
    /// 翻訳進行状況を作成
    /// </summary>
    public static TranslationProgress CreateTranslationProgress(
        string? id = null,
        TranslationStatus status = TranslationStatus.ProcessingOCR,
        float progress = 0.5f)
    {
        return new TranslationProgress
        {
            Id = id ?? GenerateTestId(),
            Status = status,
            Progress = Math.Clamp(progress, 0.0f, 1.0f),
            Message = GetMessageForStatus(status, progress)
        };
    }

    /// <summary>
    /// テスト用IDを生成
    /// </summary>
    private static string GenerateTestId()
    {
        return Guid.NewGuid().ToString("N")[..8];
    }

    /// <summary>
    /// ステータスに応じたメッセージを取得
    /// </summary>
    private static string? GetMessageForStatus(TranslationStatus status, float progress)
    {
        return status switch
        {
            TranslationStatus.Capturing => "画面キャプチャ中...",
            TranslationStatus.ProcessingOCR => "テキスト認識中...",
            TranslationStatus.Translating => "翻訳中...",
            TranslationStatus.Completed => "完了",
            TranslationStatus.Error => "エラーが発生しました",
            TranslationStatus.Cancelled => "キャンセルされました",
            _ => $"処理中... ({progress:P0})"
        };
    }
}
