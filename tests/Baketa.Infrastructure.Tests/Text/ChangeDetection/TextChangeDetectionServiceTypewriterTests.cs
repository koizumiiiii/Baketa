using Baketa.Core.Abstractions.Text;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Text.ChangeDetection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Text.ChangeDetection;

/// <summary>
/// [Issue #432] タイプライター演出検知機能のテスト
/// </summary>
public class TextChangeDetectionServiceTypewriterTests
{
    private readonly Mock<ILogger<TextChangeDetectionService>> _loggerMock = new();

    private TextChangeDetectionService CreateService(RoiGatekeeperSettings? settings = null)
    {
        settings ??= new RoiGatekeeperSettings
        {
            Enabled = true,
            EnableTypewriterDetection = true,
            TypewriterStabilizationCycles = 1,
            TypewriterMaxDelayCycles = 10,
            AlwaysTranslateFirstText = true,
            SkipIdenticalText = true,
            SkipEmptyText = true,
            MinTextLength = 2
        };

        var options = Options.Create(settings);
        return new TextChangeDetectionService(_loggerMock.Object, options);
    }

    [Fact]
    public async Task TypewriterGrowth_PrefixMatch_ShouldNotTranslate()
    {
        // Arrange
        var service = CreateService();
        const string sourceId = "roi-1";

        // Act: 初回テキスト → 翻訳実行
        var result1 = await service.DetectChangeWithGateAsync("Hello", sourceId);
        result1.ShouldTranslate.Should().BeTrue();
        result1.Decision.Should().Be(GateDecision.FirstText);

        // Act: 前方一致で成長 → 翻訳遅延
        var result2 = await service.DetectChangeWithGateAsync("Hello, W", sourceId);
        result2.ShouldTranslate.Should().BeFalse();
        result2.Decision.Should().Be(GateDecision.TypewriterGrowing);

        // Act: さらに成長 → 引き続き翻訳遅延
        var result3 = await service.DetectChangeWithGateAsync("Hello, World", sourceId);
        result3.ShouldTranslate.Should().BeFalse();
        result3.Decision.Should().Be(GateDecision.TypewriterGrowing);
    }

    [Fact]
    public async Task TypewriterStabilization_SameTextAfterGrowth_ShouldTranslate()
    {
        // Arrange
        var service = CreateService();
        const string sourceId = "roi-1";

        // 初回テキスト
        await service.DetectChangeWithGateAsync("He", sourceId);

        // 成長中
        await service.DetectChangeWithGateAsync("Hello", sourceId);

        // 成長中
        await service.DetectChangeWithGateAsync("Hello, World!", sourceId);

        // Act: テキスト安定（同一テキスト）→ 翻訳実行
        var result = await service.DetectChangeWithGateAsync("Hello, World!", sourceId);
        result.ShouldTranslate.Should().BeTrue();
        result.Decision.Should().Be(GateDecision.SufficientChange);
    }

    [Fact]
    public async Task TypewriterStabilization_MultiCycles_WaitsForRequired()
    {
        // Arrange: 安定化に2サイクル必要
        var settings = new RoiGatekeeperSettings
        {
            Enabled = true,
            EnableTypewriterDetection = true,
            TypewriterStabilizationCycles = 2,
            TypewriterMaxDelayCycles = 10,
            AlwaysTranslateFirstText = true,
            SkipIdenticalText = true,
            SkipEmptyText = true,
            MinTextLength = 2
        };
        var service = CreateService(settings);
        const string sourceId = "roi-1";

        // 初回
        await service.DetectChangeWithGateAsync("He", sourceId);
        // 成長
        await service.DetectChangeWithGateAsync("Hello", sourceId);

        // 1回目の同一テキスト → まだ安定化待ち
        var result1 = await service.DetectChangeWithGateAsync("Hello", sourceId);
        result1.ShouldTranslate.Should().BeFalse();
        result1.Decision.Should().Be(GateDecision.TypewriterGrowing);

        // 2回目の同一テキスト → 安定化完了、翻訳実行
        var result2 = await service.DetectChangeWithGateAsync("Hello", sourceId);
        result2.ShouldTranslate.Should().BeTrue();
    }

    [Fact]
    public async Task TypewriterMaxDelay_ExceedsLimit_ForcesTranslation()
    {
        // Arrange: 最大遅延3サイクル
        var settings = new RoiGatekeeperSettings
        {
            Enabled = true,
            EnableTypewriterDetection = true,
            TypewriterStabilizationCycles = 1,
            TypewriterMaxDelayCycles = 3,
            AlwaysTranslateFirstText = true,
            SkipIdenticalText = true,
            SkipEmptyText = true,
            MinTextLength = 2
        };
        var service = CreateService(settings);
        const string sourceId = "roi-1";

        // 初回
        await service.DetectChangeWithGateAsync("AB", sourceId);

        // 成長1
        var r1 = await service.DetectChangeWithGateAsync("ABC", sourceId);
        r1.Decision.Should().Be(GateDecision.TypewriterGrowing);

        // 成長2
        var r2 = await service.DetectChangeWithGateAsync("ABCD", sourceId);
        r2.Decision.Should().Be(GateDecision.TypewriterGrowing);

        // 成長3（最大遅延到達）→ 強制翻訳
        var r3 = await service.DetectChangeWithGateAsync("ABCDE", sourceId);
        r3.ShouldTranslate.Should().BeTrue();
        r3.Decision.Should().Be(GateDecision.TypewriterMaxDelayExceeded);
    }

    [Fact]
    public async Task TypewriterDisabled_NormalBehavior()
    {
        // Arrange: タイプライター検知無効
        var settings = new RoiGatekeeperSettings
        {
            Enabled = true,
            EnableTypewriterDetection = false,
            AlwaysTranslateFirstText = true,
            SkipIdenticalText = true,
            SkipEmptyText = true,
            MinTextLength = 2
        };
        var service = CreateService(settings);
        const string sourceId = "roi-1";

        // 初回
        await service.DetectChangeWithGateAsync("Hello", sourceId);

        // 前方一致成長でもタイプライター検知なし → 通常の長さ変化/編集距離ロジックへ
        var result = await service.DetectChangeWithGateAsync("Hello, World", sourceId);
        result.Decision.Should().NotBe(GateDecision.TypewriterGrowing);
        result.Decision.Should().NotBe(GateDecision.TypewriterMaxDelayExceeded);
    }

    [Fact]
    public async Task TypewriterReset_NonPrefixChange_ResetsState()
    {
        // Arrange
        var service = CreateService();
        const string sourceId = "roi-1";

        // 初回
        await service.DetectChangeWithGateAsync("Hello", sourceId);

        // 成長中
        var r1 = await service.DetectChangeWithGateAsync("Hello, W", sourceId);
        r1.Decision.Should().Be(GateDecision.TypewriterGrowing);

        // シーン切替（前方一致でない完全に別のテキスト）→ リセット
        var r2 = await service.DetectChangeWithGateAsync("Goodbye!", sourceId);
        r2.Decision.Should().NotBe(GateDecision.TypewriterGrowing);

        // 新しいテキストからの成長も正常に検知
        var r3 = await service.DetectChangeWithGateAsync("Goodbye! See", sourceId);
        r3.Decision.Should().Be(GateDecision.TypewriterGrowing);
    }

    [Fact]
    public async Task TypewriterIndependentSources_TrackSeparately()
    {
        // Arrange
        var service = CreateService();

        // Source A: 初回 + 成長
        await service.DetectChangeWithGateAsync("AA", "source-a");
        var rA = await service.DetectChangeWithGateAsync("AAB", "source-a");
        rA.Decision.Should().Be(GateDecision.TypewriterGrowing);

        // Source B: 初回 + 成長（独立して動作）
        await service.DetectChangeWithGateAsync("XX", "source-b");
        var rB = await service.DetectChangeWithGateAsync("XXY", "source-b");
        rB.Decision.Should().Be(GateDecision.TypewriterGrowing);

        // Source A: 安定化（Source Bの状態に影響しない）
        var rA2 = await service.DetectChangeWithGateAsync("AAB", "source-a");
        rA2.ShouldTranslate.Should().BeTrue();

        // Source B: まだ成長中
        var rB2 = await service.DetectChangeWithGateAsync("XXYY", "source-b");
        rB2.Decision.Should().Be(GateDecision.TypewriterGrowing);
    }

    [Fact]
    public async Task ClearPreviousText_ClearsTypewriterState()
    {
        // Arrange
        var service = CreateService();
        const string sourceId = "roi-1";

        // 成長中状態を作る
        await service.DetectChangeWithGateAsync("He", sourceId);
        await service.DetectChangeWithGateAsync("Hello", sourceId);

        // Act: クリア
        service.ClearPreviousText(sourceId);

        // Assert: 新しい初回テキストとして扱われる
        var result = await service.DetectChangeWithGateAsync("Hello", sourceId);
        result.Decision.Should().Be(GateDecision.FirstText);
    }

    [Fact]
    public async Task ClearAllPreviousTexts_ClearsAllTypewriterState()
    {
        // Arrange
        var service = CreateService();

        // 複数ソースで成長中状態を作る
        await service.DetectChangeWithGateAsync("AA", "src-1");
        await service.DetectChangeWithGateAsync("AAB", "src-1");
        await service.DetectChangeWithGateAsync("XX", "src-2");
        await service.DetectChangeWithGateAsync("XXY", "src-2");

        // Act: 全クリア
        service.ClearAllPreviousTexts();

        // Assert: 両方とも初回テキストとして扱われる
        var r1 = await service.DetectChangeWithGateAsync("AAB", "src-1");
        r1.Decision.Should().Be(GateDecision.FirstText);

        var r2 = await service.DetectChangeWithGateAsync("XXY", "src-2");
        r2.Decision.Should().Be(GateDecision.FirstText);
    }

    [Fact]
    public async Task TypewriterGrowth_FullWidthHalfWidthMix_ShouldDetectGrowth()
    {
        // Arrange: OCRが全角？→半角?で揺れるケース（Issue #432の根本原因）
        var service = CreateService();
        const string sourceId = "roi-1";

        // 初回: 全角「？」を含むテキスト
        var r1 = await service.DetectChangeWithGateAsync("読んでないの？", sourceId);
        r1.ShouldTranslate.Should().BeTrue();
        r1.Decision.Should().Be(GateDecision.FirstText);

        // 成長: 半角「?」に変わり + テキスト追加 → タイプライター成長として検出
        var r2 = await service.DetectChangeWithGateAsync("読んでないの? ビッグバン", sourceId);
        r2.Decision.Should().Be(GateDecision.TypewriterGrowing);
    }

    [Fact]
    public async Task TypewriterStabilization_FullWidthHalfWidthIdentical_ShouldStabilize()
    {
        // Arrange: 正規化後に同一テキストなら安定化判定
        var service = CreateService();
        const string sourceId = "roi-1";

        // 初回
        await service.DetectChangeWithGateAsync("テスト！", sourceId);

        // 成長（半角!で揺れ + テキスト追加）
        var r1 = await service.DetectChangeWithGateAsync("テスト! 追加", sourceId);
        r1.Decision.Should().Be(GateDecision.TypewriterGrowing);

        // 安定化: 全角「！」に戻るが正規化後は同一
        var r2 = await service.DetectChangeWithGateAsync("テスト！ 追加", sourceId);
        r2.ShouldTranslate.Should().BeTrue("正規化後に同一テキストなので安定化として翻訳実行されるべき");
    }

    [Fact]
    public async Task TypewriterAfterMaxDelay_ResetsAndCanDetectAgain()
    {
        // Arrange: 最大遅延2サイクル
        var settings = new RoiGatekeeperSettings
        {
            Enabled = true,
            EnableTypewriterDetection = true,
            TypewriterStabilizationCycles = 1,
            TypewriterMaxDelayCycles = 2,
            AlwaysTranslateFirstText = true,
            SkipIdenticalText = true,
            SkipEmptyText = true,
            MinTextLength = 2
        };
        var service = CreateService(settings);
        const string sourceId = "roi-1";

        // 初回
        await service.DetectChangeWithGateAsync("AB", sourceId);

        // 成長1
        await service.DetectChangeWithGateAsync("ABC", sourceId);

        // 成長2（最大遅延超過、強制翻訳）
        var r1 = await service.DetectChangeWithGateAsync("ABCD", sourceId);
        r1.Decision.Should().Be(GateDecision.TypewriterMaxDelayExceeded);

        // リセット後、再び成長検知可能
        var r2 = await service.DetectChangeWithGateAsync("ABCDE", sourceId);
        r2.Decision.Should().Be(GateDecision.TypewriterGrowing);
    }
}
