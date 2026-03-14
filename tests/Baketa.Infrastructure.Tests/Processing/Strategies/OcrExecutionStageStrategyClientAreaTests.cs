using System.Drawing;
using Baketa.Core.Models.Roi;
using Baketa.Infrastructure.Processing.Strategies;
using Xunit;

namespace Baketa.Infrastructure.Tests.Processing.Strategies;

/// <summary>
/// [Issue #448] ApplyClientAreaConstraint メソッドの単体テスト
/// タイトルバー領域をOCR対象から除外するクライアント領域制約のテスト
/// </summary>
public class OcrExecutionStageStrategyClientAreaTests
{
    private const int ImageWidth = 1000;
    private const int ImageHeight = 800;

    // クライアント領域: タイトルバー40px、左右ボーダー各10px、下ボーダー10px
    // → ClientArea: X=10/1000=0.01, Y=40/800=0.05, W=980/1000=0.98, H=750/800=0.9375
    private static readonly NormalizedRect TestClientArea = new(0.01f, 0.05f, 0.98f, 0.9375f);

    [Fact]
    public void ApplyClientAreaConstraint_RegionInsideClientArea_ReturnsUnchanged()
    {
        // Arrange: クライアント領域内に完全に収まる領域
        var regions = new List<Rectangle>
        {
            new(100, 100, 200, 150)
        };

        // Act
        var result = OcrExecutionStageStrategy.ApplyClientAreaConstraint(
            regions, ImageWidth, ImageHeight, TestClientArea);

        // Assert
        Assert.Single(result);
        Assert.Equal(new Rectangle(100, 100, 200, 150), result[0]);
    }

    [Fact]
    public void ApplyClientAreaConstraint_RegionOverlapsTitleBar_IsTrimmed()
    {
        // Arrange: タイトルバー領域(Y=0-40)と重なる領域
        var regions = new List<Rectangle>
        {
            new(100, 10, 200, 100)  // Y=10からY=110まで
        };

        // Act
        var result = OcrExecutionStageStrategy.ApplyClientAreaConstraint(
            regions, ImageWidth, ImageHeight, TestClientArea);

        // Assert: Y=40(クライアント領域開始)で切り詰められる
        Assert.Single(result);
        var trimmed = result[0];
        Assert.Equal(100, trimmed.X);
        Assert.Equal(40, trimmed.Y);  // タイトルバー下端から開始
        Assert.True(trimmed.Height < 100);  // 元より小さくなる
    }

    [Fact]
    public void ApplyClientAreaConstraint_RegionEntirelyInTitleBar_IsExcluded()
    {
        // Arrange: 完全にタイトルバー内の領域
        var regions = new List<Rectangle>
        {
            new(100, 5, 200, 30)  // Y=5からY=35まで（タイトルバー内）
        };

        // Act
        var result = OcrExecutionStageStrategy.ApplyClientAreaConstraint(
            regions, ImageWidth, ImageHeight, TestClientArea);

        // Assert: 完全に除外される
        Assert.Empty(result);
    }

    [Fact]
    public void ApplyClientAreaConstraint_SmallIntersection_IsExcluded()
    {
        // Arrange: 交差結果が小さすぎる（10x10未満）領域
        var regions = new List<Rectangle>
        {
            new(100, 35, 8, 50)  // 幅8px → Intersect後もwidth<=10でフィルタされる
        };

        // Act
        var result = OcrExecutionStageStrategy.ApplyClientAreaConstraint(
            regions, ImageWidth, ImageHeight, TestClientArea);

        // Assert: 小さすぎる交差結果は除外
        Assert.Empty(result);
    }

    [Fact]
    public void ApplyClientAreaConstraint_MultipleRegions_FiltersCorrectly()
    {
        // Arrange: 複数領域（クライアント内、タイトルバー内、重なり）
        var regions = new List<Rectangle>
        {
            new(100, 200, 200, 150),  // クライアント内 → 残る
            new(100, 5, 200, 20),     // タイトルバー内 → 除外
            new(100, 20, 200, 100),   // タイトルバーと重なり → トリミング
        };

        // Act
        var result = OcrExecutionStageStrategy.ApplyClientAreaConstraint(
            regions, ImageWidth, ImageHeight, TestClientArea);

        // Assert: タイトルバー内の領域のみ除外
        Assert.Equal(2, result.Count);
        // 1つ目はそのまま
        Assert.Equal(new Rectangle(100, 200, 200, 150), result[0]);
        // 2つ目はトリミングされている
        Assert.True(result[1].Y >= 40);
    }

    [Fact]
    public void ApplyClientAreaConstraint_EmptyRegionList_ReturnsEmpty()
    {
        // Arrange
        var regions = new List<Rectangle>();

        // Act
        var result = OcrExecutionStageStrategy.ApplyClientAreaConstraint(
            regions, ImageWidth, ImageHeight, TestClientArea);

        // Assert
        Assert.Empty(result);
    }
}
