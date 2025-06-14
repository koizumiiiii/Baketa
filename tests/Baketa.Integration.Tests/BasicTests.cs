using Xunit;
using Xunit.Abstractions;

namespace Baketa.Integration.Tests;

/// <summary>
/// Test Explorerでの認識確認用の基本テスト
/// </summary>
public class BasicTests
{
    private readonly ITestOutputHelper _output;
    
    public BasicTests(ITestOutputHelper output)
    {
        _output = output;
    }
    
    /// <summary>
    /// 最も基本的なテスト - Test Explorerでの認識確認
    /// </summary>
    [Fact]
    public void BasicTestShouldPass()
    {
        // Arrange
        var expected = 42;
        
        // Act
        var actual = 40 + 2;
        
        // Assert
        Assert.Equal(expected, actual);
        
        _output.WriteLine("✅ 基本テストが正常に実行されました");
    }
    
    /// <summary>
    /// 文字列操作のテスト
    /// </summary>
    [Fact]
    public void StringOperationsShouldWork()
    {
        // Arrange
        var input = "Baketa";
        
        // Act
        var result = input.ToUpperInvariant();
        
        // Assert
        Assert.Equal("BAKETA", result);
        Assert.StartsWith("BAK", result, StringComparison.Ordinal);
        Assert.EndsWith("ETA", result, StringComparison.Ordinal);
        
        _output.WriteLine($"✅ 文字列操作テスト完了: '{input}' → '{result}'");
    }
    
    /// <summary>
    /// 数値計算のテスト
    /// </summary>
    [Theory]
    [InlineData(1, 1, 2)]
    [InlineData(5, 3, 8)]
    [InlineData(-1, 1, 0)]
    [InlineData(0, 0, 0)]
    public void MathAdditionShouldWork(int a, int b, int expected)
    {
        // Act
        var result = a + b;
        
        // Assert
        Assert.Equal(expected, result);
        
        _output.WriteLine($"✅ 計算テスト: {a} + {b} = {result}");
    }
    
    /// <summary>
    /// 例外処理のテスト
    /// </summary>
    [Fact]
    public void ExceptionHandlingShouldWork()
    {
        // Act & Assert
        var exception = Assert.Throws<DivideByZeroException>(() =>
        {
            int zero = 0;
            int result = 10 / zero;
            return result;
        });
        
        Assert.NotNull(exception);
        _output.WriteLine("✅ 例外処理テスト完了");
    }
    
    /// <summary>
    /// 非同期処理のテスト
    /// </summary>
    [Fact]
    public async Task AsyncOperationsShouldWork()
    {
        // Arrange
        var delay = TimeSpan.FromMilliseconds(10);
        
        // Act
        var startTime = DateTime.UtcNow;
        await Task.Delay(delay).ConfigureAwait(true);
        var endTime = DateTime.UtcNow;
        
        // Assert
        var actualDelay = endTime - startTime;
        Assert.True(actualDelay >= delay);
        
        _output.WriteLine($"✅ 非同期処理テスト完了: 遅延 {actualDelay.TotalMilliseconds:F1}ms");
    }
}
