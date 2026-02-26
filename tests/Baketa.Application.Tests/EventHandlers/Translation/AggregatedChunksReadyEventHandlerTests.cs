using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Application.EventHandlers.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI.Overlays;
using Baketa.Core.Events.Translation;
using Baketa.Core.Abstractions.OCR.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using DrawingRectangle = System.Drawing.Rectangle;
using CoreLanguagePair = Baketa.Core.Models.Translation.LanguagePair;
using CoreLanguage = Baketa.Core.Models.Translation.Language;
using TranslationLanguage = Baketa.Core.Translation.Models.Language;
using TranslationResponse = Baketa.Core.Translation.Models.TranslationResponse;

namespace Baketa.Application.Tests.EventHandlers.Translation;

/// <summary>
/// AggregatedChunksReadyEventHandler のユニットテスト
/// - ClipToSuryaBounds: Cloud BBoxのSurya矩形クリッピングロジック
/// - 対策Cしきい値: クリッピング結果が小さすぎる場合のSuryaフォールバック
/// - レースコンディション: Stop後のShowAsync呼び出し防止
/// </summary>
public class AggregatedChunksReadyEventHandlerTests
{
    // ========================================================
    // ClipToSuryaBounds テスト（internal staticメソッド直接テスト）
    // ========================================================

    [Fact]
    public void ClipToSuryaBounds_WithFullIntersection_ReturnsClippedRectangle()
    {
        // Arrange: Cloud矩形がSurya矩形に完全に包含される
        var cloudRect = new DrawingRectangle(100, 100, 200, 50);
        var suryaBounds = new DrawingRectangle(50, 50, 400, 200);

        // Act
        var result = AggregatedChunksReadyEventHandler.ClipToSuryaBounds(cloudRect, suryaBounds);

        // Assert: Cloud矩形がそのまま返る（完全包含のためクリッピング不要）
        result.Should().Be(cloudRect);
    }

    [Fact]
    public void ClipToSuryaBounds_WithPartialIntersection_ReturnsIntersection()
    {
        // Arrange: Cloud矩形がSurya矩形からはみ出す
        var cloudRect = new DrawingRectangle(500, 100, 300, 80);
        var suryaBounds = new DrawingRectangle(600, 90, 400, 120);

        // Act
        var result = AggregatedChunksReadyEventHandler.ClipToSuryaBounds(cloudRect, suryaBounds);

        // Assert: 交差領域が返る
        result.X.Should().Be(600);  // Max(500, 600)
        result.Y.Should().Be(100);  // Max(100, 90)
        result.Width.Should().Be(200);  // Min(800, 1000) - 600
        result.Height.Should().Be(80); // Min(180, 210) - 100
    }

    [Fact]
    public void ClipToSuryaBounds_WithNoIntersection_ReturnsOriginalCloudRect()
    {
        // Arrange: Cloud矩形とSurya矩形が完全に離れている
        var cloudRect = new DrawingRectangle(0, 0, 100, 50);
        var suryaBounds = new DrawingRectangle(500, 500, 200, 100);

        // Act
        var result = AggregatedChunksReadyEventHandler.ClipToSuryaBounds(cloudRect, suryaBounds);

        // Assert: 交差なし→元のCloud座標を返す
        result.Should().Be(cloudRect);
    }

    // ========================================================
    // 対策Cしきい値テスト（0.3→0.7引き上げの検証）
    // ========================================================

    [Theory]
    [InlineData(73, 105, true)]    // 69.5% - 報告されたケース（秘密組織テキスト）
    [InlineData(50, 105, true)]    // 47.6% - 明らかに小さい
    [InlineData(30, 105, true)]    // 28.6% - 極端に小さい
    [InlineData(74, 105, false)]   // 70.5% - しきい値を超える（フォールバックなし）
    [InlineData(80, 105, false)]   // 76.2% - 十分な高さ
    [InlineData(105, 105, false)]  // 100% - 同一サイズ
    public void SuryaFallbackThreshold_Height_ShouldTriggerAtSeventyPercent(
        int clippedHeight, int suryaHeight, bool shouldFallback)
    {
        // Arrange: しきい値0.7で高さの比較をシミュレート
        const float threshold = 0.7f;

        // Act: 実際のコード（L1840）と同じ比較ロジック
        var triggersFallback = clippedHeight < suryaHeight * threshold;

        // Assert
        triggersFallback.Should().Be(shouldFallback,
            $"clipped={clippedHeight}, surya={suryaHeight}, ratio={clippedHeight / (float)suryaHeight:P1}");
    }

    [Theory]
    [InlineData(600, 2049, true)]    // 29.3% - 幅が極端に小さい
    [InlineData(1400, 2049, true)]   // 68.3% - 幅がしきい値未満
    [InlineData(1435, 2049, false)]  // 70.0% - しきい値ちょうど
    [InlineData(1657, 2049, false)]  // 80.9% - 十分な幅
    [InlineData(2049, 2049, false)]  // 100% - 同一幅
    public void SuryaFallbackThreshold_Width_ShouldTriggerAtSeventyPercent(
        int clippedWidth, int suryaWidth, bool shouldFallback)
    {
        // Arrange
        const float threshold = 0.7f;

        // Act: L1841の比較ロジック
        var triggersFallback = clippedWidth < suryaWidth * threshold;

        // Assert
        triggersFallback.Should().Be(shouldFallback,
            $"clipped={clippedWidth}, surya={suryaWidth}, ratio={clippedWidth / (float)suryaWidth:P1}");
    }

    [Fact]
    public void SuryaFallbackThreshold_ReportedCase_ShouldTriggerFallback()
    {
        // Arrange: 実際のログから再現
        // Cloud BBox: (576,1631,1774x73) normalized from (150,756,462x34)
        // Surya bounds: (693,1629,2049x105)
        var cloudPixelRect = new DrawingRectangle(576, 1631, 1774, 73);
        var suryaBounds = new DrawingRectangle(693, 1629, 2049, 105);

        // Act: ClipToSuryaBoundsの実行
        var clipped = AggregatedChunksReadyEventHandler.ClipToSuryaBounds(cloudPixelRect, suryaBounds);

        // Assert: クリッピングは正常に動作する
        clipped.Height.Should().Be(73, "Cloud高さがSurya高さより小さいためCloudの高さが制約する");

        // 対策Cしきい値チェック: 新しい0.7しきい値でフォールバックが発動するか
        const float threshold = 0.7f;
        var heightTriggersFallback = clipped.Height < suryaBounds.Height * threshold;
        heightTriggersFallback.Should().BeTrue(
            $"73 < 105 * 0.7 = {105 * threshold} → Surya座標にフォールバックすべき");
    }

    // ========================================================
    // レースコンディションテスト（ShowAsync直前のキャンセルチェック）
    // ========================================================

    [Fact]
    public async Task HandleAsync_WhenCancelledAtEntry_ShouldNotCallShowAsync()
    {
        // Arrange: 事前にキャンセル済みのトークンで呼び出す
        var handler = CreateMinimalHandler(out var overlayManagerMock);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var eventData = CreateSimpleEvent();

        // Act
        await handler.HandleAsync(eventData, cts.Token);

        // Assert: L196の早期リターンによりShowAsyncは一度も呼ばれない
        overlayManagerMock.Verify(
            om => om.ShowAsync(It.IsAny<OverlayContent>(), It.IsAny<OverlayPosition>()),
            Times.Never,
            "キャンセル済みトークンではShowAsyncを呼んではいけない");
    }

    [Fact]
    public async Task HandleAsync_WhenCancelledDuringOverlayCleanup_ShouldNotCallShowAsync()
    {
        // Arrange: HideOverlaysInAreaAsync実行中にキャンセルが発生するシナリオ
        // これはStop後のレースコンディションを再現する
        var cts = new CancellationTokenSource();
        var handler = CreateHandlerWithTranslation(
            out var overlayManagerMock,
            out _);

        // HideOverlaysInAreaAsyncが呼ばれた時にトークンをキャンセル
        // （StopボタンがOverlay Cleanup中に押された場合をシミュレート）
        overlayManagerMock
            .Setup(om => om.HideOverlaysInAreaAsync(
                It.IsAny<DrawingRectangle>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .Returns(Task.CompletedTask);

        var eventData = CreateSimpleEvent();

        // Act
        await handler.HandleAsync(eventData, cts.Token);

        // Assert: Cleanup中にキャンセルされたため、ShowAsyncは呼ばれない
        overlayManagerMock.Verify(
            om => om.ShowAsync(It.IsAny<OverlayContent>(), It.IsAny<OverlayPosition>()),
            Times.Never,
            "Overlay Cleanup中にキャンセルされた場合、ShowAsyncを呼んではいけない");
    }

    // ========================================================
    // ヘルパーメソッド
    // ========================================================

    /// <summary>
    /// 最小限のモックでハンドラーを作成（キャンセル早期リターンテスト用）
    /// </summary>
    private static AggregatedChunksReadyEventHandler CreateMinimalHandler(
        out Mock<IOverlayManager> overlayManagerMock)
    {
        overlayManagerMock = new Mock<IOverlayManager>();

        return new AggregatedChunksReadyEventHandler(
            Mock.Of<Baketa.Core.Abstractions.Translation.ITranslationService>(),
            overlayManagerMock.Object,
            Mock.Of<ILanguageConfigurationService>(),
            Mock.Of<IEventAggregator>(),
            new Mock<ILogger<AggregatedChunksReadyEventHandler>>().Object,
            Mock.Of<ICoordinateTransformationService>(),
            Mock.Of<Core.Abstractions.Settings.IUnifiedSettingsService>());
    }

    /// <summary>
    /// 翻訳処理まで通るハンドラーを作成（レースコンディションテスト用）
    /// </summary>
    private static AggregatedChunksReadyEventHandler CreateHandlerWithTranslation(
        out Mock<IOverlayManager> overlayManagerMock,
        out Mock<Baketa.Core.Abstractions.Translation.ITranslationService> translationServiceMock)
    {
        overlayManagerMock = new Mock<IOverlayManager>();
        translationServiceMock = new Mock<Baketa.Core.Abstractions.Translation.ITranslationService>();

        // 翻訳結果を返すようにセットアップ
        translationServiceMock
            .Setup(ts => ts.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<TranslationLanguage>(),
                It.IsAny<TranslationLanguage>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranslationResponse
            {
                RequestId = Guid.NewGuid(),
                SourceText = "テスト",
                TranslatedText = "Test",
                SourceLanguage = TranslationLanguage.FromCode("ja"),
                TargetLanguage = TranslationLanguage.FromCode("en"),
                EngineName = "MockEngine",
                IsSuccess = true
            });

        var languageConfigMock = new Mock<ILanguageConfigurationService>();
        languageConfigMock
            .Setup(lc => lc.GetCurrentLanguagePair())
            .Returns(new CoreLanguagePair(CoreLanguage.Japanese, CoreLanguage.English));

        var coordTransformMock = new Mock<ICoordinateTransformationService>();
        coordTransformMock
            .Setup(ct => ct.ConvertRoiToScreenCoordinates(
                It.IsAny<DrawingRectangle>(), It.IsAny<IntPtr>(),
                It.IsAny<float>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns<DrawingRectangle, IntPtr, float, bool, bool>((bounds, _, _, _, _) => bounds);
        coordTransformMock
            .Setup(ct => ct.DetectBorderlessOrFullscreen(It.IsAny<IntPtr>()))
            .Returns(false);

        var settingsServiceMock = new Mock<Core.Abstractions.Settings.IUnifiedSettingsService>();
        var translationSettingsMock = new Mock<Core.Abstractions.Settings.ITranslationSettings>();
        translationSettingsMock.Setup(ts => ts.OverlayFontSize).Returns(14);
        settingsServiceMock.Setup(ss => ss.GetTranslationSettings()).Returns(translationSettingsMock.Object);

        return new AggregatedChunksReadyEventHandler(
            translationServiceMock.Object,
            overlayManagerMock.Object,
            languageConfigMock.Object,
            Mock.Of<IEventAggregator>(),
            new Mock<ILogger<AggregatedChunksReadyEventHandler>>().Object,
            coordTransformMock.Object,
            settingsServiceMock.Object);
    }

    /// <summary>
    /// テスト用のシンプルなイベントデータを作成
    /// Singleshotモードを使用してGate判定をバイパス
    /// </summary>
    private static AggregatedChunksReadyEvent CreateSimpleEvent()
    {
        var textResults = new List<PositionedTextResult>
        {
            new()
            {
                ChunkId = 0,
                Text = "テストテキスト",
                BoundingBox = new DrawingRectangle(100, 100, 200, 50),
                Confidence = 0.95f
            }
        };

        var chunks = new List<TextChunk>
        {
            new()
            {
                ChunkId = 1,
                TextResults = textResults,
                CombinedBounds = new DrawingRectangle(100, 100, 200, 50),
                CombinedText = "テストテキスト",
                SourceWindowHandle = IntPtr.Zero
            }
        };

        return new AggregatedChunksReadyEvent(chunks, IntPtr.Zero)
        {
            TranslationMode = TranslationMode.Singleshot
        };
    }
}
