---
description: テストファイル固有のルール（xUnit, Moq）
globs:
  - "tests/**/*.cs"
---

# テストファイルルール

## フレームワーク
- **ユニットテスト**: xUnit
- **モック**: Moq
- **アサーション**: FluentAssertions 推奨

## 命名規則

### テストクラス
```csharp
// 対象クラス名 + Tests
public class TranslationServiceTests { }
```

### テストメソッド
```csharp
// メソッド名_条件_期待結果
[Fact]
public async Task TranslateAsync_WithValidInput_ReturnsTranslatedText() { }

[Theory]
[InlineData("en", "ja")]
[InlineData("ja", "en")]
public async Task TranslateAsync_WithLanguagePair_Succeeds(string source, string target) { }
```

## 構造

### Arrange-Act-Assert パターン
```csharp
[Fact]
public async Task MyTest()
{
    // Arrange
    var mockService = new Mock<IMyService>();
    var sut = new MyClass(mockService.Object);

    // Act
    var result = await sut.DoSomethingAsync();

    // Assert
    result.Should().BeTrue();
}
```

## 特記事項
- `ConfigureAwait(false)` はテストでは不要
- テストは並列実行されるため、共有状態を避ける
- `IDisposable` を実装するテストクラスでリソースをクリーンアップ
