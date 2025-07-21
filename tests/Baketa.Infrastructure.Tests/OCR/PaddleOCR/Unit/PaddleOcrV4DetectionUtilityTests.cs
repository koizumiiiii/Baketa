using Xunit;
using Xunit.Abstractions;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;
using Sdcb.PaddleOCR.Models.Shared;

namespace Baketa.Infrastructure.Tests.OCR.PaddleOCR.Unit;

/// <summary>
/// PaddleOCR V4検出ユーティリティのテスト
/// 実際のAPIに基づいた正確な検出方法を提供
/// </summary>
public class PaddleOcrV4DetectionUtilityTests(ITestOutputHelper output)
{
    [Fact]
    public void IsV4Model_WithV4Model_ReturnsTrue()
    {
        // Arrange
        var v4Model = LocalFullModels.JapanV4;
        
        // Act
        var result = IsV4Model(v4Model);
        
        // Assert
        Assert.True(result);
        output.WriteLine($"V4モデル検出: {result} (期待値: True)");
    }

    [Fact]
    public void IsV4Model_WithV3Model_ReturnsFalse()
    {
        // Arrange
        var v3Model = LocalFullModels.JapanV3;
        
        // Act
        var result = IsV4Model(v3Model);
        
        // Assert
        Assert.False(result);
        output.WriteLine($"V3モデル検出: {result} (期待値: False)");
    }

    [Fact]
    public void DetectModelVersion_ReturnsCorrectVersions()
    {
        // Arrange & Act
        var v4Version = DetectModelVersion(LocalFullModels.JapanV4);
        var v3Version = DetectModelVersion(LocalFullModels.JapanV3);
        
        // Assert
        Assert.Equal("V4", v4Version);
        Assert.Equal("V3", v3Version);
        
        output.WriteLine($"V4モデルバージョン: {v4Version}");
        output.WriteLine($"V3モデルバージョン: {v3Version}");
    }

    [Fact]
    public void GetModelName_ReturnsCorrectNames()
    {
        // Arrange & Act
        var v4Name = GetModelName(LocalFullModels.JapanV4);
        var v3Name = GetModelName(LocalFullModels.JapanV3);
        
        // Assert
        Assert.Contains("PP-OCRv4", v4Name);
        Assert.Contains("PP-OCRv3", v3Name);
        
        output.WriteLine($"V4モデル名: {v4Name}");
        output.WriteLine($"V3モデル名: {v3Name}");
    }

    [Fact]
    public void CompareAllDetectionMethods()
    {
        // Arrange
        var v4Model = LocalFullModels.JapanV4;
        var v3Model = LocalFullModels.JapanV3;
        
        output.WriteLine("=== 全検出メソッド比較 ===");
        
        // V4モデルテスト
        output.WriteLine("\n--- V4モデル検出結果 ---");
        output.WriteLine($"方法1 (Version): {IsV4ByVersion(v4Model)}");
        output.WriteLine($"方法2 (Name含む): {IsV4ByNameContains(v4Model)}");
        output.WriteLine($"方法3 (Name完全): {IsV4ByNameExact(v4Model)}");
        
        // V3モデルテスト
        output.WriteLine("\n--- V3モデル検出結果 ---");
        output.WriteLine($"方法1 (Version): {IsV4ByVersion(v3Model)}");
        output.WriteLine($"方法2 (Name含む): {IsV4ByNameContains(v3Model)}");
        output.WriteLine($"方法3 (Name完全): {IsV4ByNameExact(v3Model)}");
        
        // すべての方法が一致することを確認
        Assert.True(IsV4ByVersion(v4Model));
        Assert.True(IsV4ByNameContains(v4Model));
        Assert.True(IsV4ByNameExact(v4Model));
        
        Assert.False(IsV4ByVersion(v3Model));
        Assert.False(IsV4ByNameContains(v3Model));
        Assert.False(IsV4ByNameExact(v3Model));
    }

    #region 推奨実装メソッド

    /// <summary>
    /// V4モデルかどうかを判定する推奨メソッド
    /// Versionプロパティを使用（最も確実）
    /// </summary>
    public static bool IsV4Model(FullOcrModel? model)
    {
        return model?.RecognizationModel?.Version == ModelVersion.V4;
    }

    /// <summary>
    /// モデルバージョンを文字列で取得
    /// </summary>
    public static string DetectModelVersion(FullOcrModel? model)
    {
        return model?.RecognizationModel?.Version switch
        {
            ModelVersion.V4 => "V4",
            ModelVersion.V3 => "V3",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// モデル名を取得
    /// </summary>
    public static string GetModelName(FullOcrModel? model)
    {
        return model?.RecognizationModel?.ToString() ?? "Unknown";
    }

    #endregion

    #region 代替検出メソッド

    /// <summary>
    /// 方法1: Versionプロパティによる検出
    /// </summary>
    private static bool IsV4ByVersion(FullOcrModel? model)
    {
        return model?.RecognizationModel?.Version == ModelVersion.V4;
    }

    /// <summary>
    /// 方法2: ToString()に"PP-OCRv4"が含まれるかで検出
    /// </summary>
    private static bool IsV4ByNameContains(FullOcrModel? model)
    {
        return model?.RecognizationModel?.ToString()?.Contains("PP-OCRv4") == true;
    }

    /// <summary>
    /// 方法3: ToString()の完全一致による検出
    /// </summary>
    private static bool IsV4ByNameExact(FullOcrModel? model)
    {
        return model?.RecognizationModel?.ToString() == "japan_PP-OCRv4_rec";
    }

    #endregion
}
