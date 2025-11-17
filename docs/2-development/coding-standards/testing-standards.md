# Testing Standards and Best Practices

## 1. Test Infrastructure Overview

Baketaプロジェクトは包括的なテストカバレッジを維持しており、Phase 5.3時点で**1,518テストケース**を含んでいます。

### テストプロジェクト構成

| プロジェクト | テスト数 | 主な対象 |
|------------|---------|----------|
| Baketa.Core.Tests | 511 | コアロジック、イベント集約、設定管理 |
| Baketa.Infrastructure.Tests | 492 | OCR、翻訳エンジン、画像処理 |
| Baketa.Application.Tests | 415 | ビジネスロジック、オーケストレーション |
| Baketa.UI.Tests | 74 | ViewModels、ReactiveUI検証 |
| Baketa.UI.IntegrationTests | 20 | UI統合テスト |
| Baketa.Integration.Tests | 6 | システム全体統合テスト |
| **合計** | **1,518** | |

### テストフレームワーク

- **単体テスト**: xUnit with Moq
- **UIテスト**: Avalonia test framework
- **パフォーマンス**: カスタムベンチマーク

### テストカテゴリ

テストは`[Trait("Category", "...")]`属性で分類されます：

- **Unit**: 単体テスト（デフォルト）
- **Integration**: 統合テスト
- **LocalOnly**: ローカル環境のみで実行（CI/CD除外）
- **Performance**: パフォーマンステスト

```csharp
[Fact]
[Trait("Category", "LocalOnly")]
public async Task PerformanceTest_WithRealOcrEngine()
{
    // ローカル環境のみで実行されるテスト
}
```

## 2. Mocking Best Practices

### モッキング設計の原則

#### 1. インターフェースをモック化する

**推奨**: インターフェースに対してモックを作成します。

```csharp
// 推奨
var loggerMock = new Mock<ILogger<MyService>>();
var serviceMock = new Mock<IMyService>();
var engineMock = new Mock<ITranslationEngine>();

var service = new MyService(
    loggerMock.Object,
    serviceMock.Object,
    engineMock.Object
);
```

**非推奨**: 具象クラスに対するモックは避けます。

```csharp
// 非推奨
var concreteServiceMock = new Mock<MyConcreteService>();
```

**理由**:
- インターフェースベースのモックは依存性逆転原則（DIP）に準拠
- テストの脆弱性が減少（実装詳細への依存がない）
- モックの動作が予測可能

#### 2. 必要最小限のSetupを行う

テストに必要な動作のみをモック化します。

```csharp
[Fact]
public async Task TranslateAsync_WhenSuccessful_ReturnsTranslatedText()
{
    // Arrange
    var engineMock = new Mock<ITranslationEngine>();
    engineMock
        .Setup(e => e.TranslateAsync("Hello", "en", "ja", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new TranslationResult
        {
            TranslatedText = "こんにちは",
            IsSuccess = true
        });

    var service = new TranslationService(engineMock.Object);

    // Act
    var result = await service.TranslateAsync("Hello", "en", "ja");

    // Assert
    Assert.True(result.IsSuccess);
    Assert.Equal("こんにちは", result.TranslatedText);
}
```

#### 3. カスタム動作が必要な場合はサブクラス化を検討

リフレクションを使用したプロパティ書き換えは避け、継承を使用します。

**ケーススタディ: CustomNamedMockTranslationEngine**

**問題**: `MockTranslationEngine`の`Name`プロパティをテストごとに変更したい

**問題のあるコード**（リフレクション使用）:

```csharp
// 非推奨: リフレクションによるプロパティ書き換え
_mockEngine2 = new Mock<MockTranslationEngine>(_engineLoggerMock.Object) { CallBase = true }.Object;
var mockEngine2NameProperty = _mockEngine2.GetType().GetProperty("Name");
mockEngine2NameProperty.SetValue(_mockEngine2, "CustomMockEngine");
```

**問題点**:
- リフレクションは実行時エラーのリスクが高い
- テストの意図が不明確
- リファクタリング時に壊れやすい

**解決策**（継承使用）:

```csharp
/// <summary>
/// カスタム名を持つMockTranslationEngineのサブクラス
/// テストで異なる名前のエンジンを使い分ける際に使用
/// </summary>
public class CustomNamedMockTranslationEngine : MockTranslationEngine
{
    private readonly string _customName;

    public override string Name => _customName;

    public CustomNamedMockTranslationEngine(
        ILogger<MockTranslationEngine> logger,
        string customName,
        int simulatedDelayMs = 0,
        float simulatedErrorRate = 0.0f)
        : base(logger, simulatedDelayMs, simulatedErrorRate)
    {
        _customName = customName ?? throw new ArgumentNullException(nameof(customName));
    }
}

// テストでの使用
_mockEngine2 = new CustomNamedMockTranslationEngine(
    _engineLoggerMock.Object,
    "CustomMockEngine"
);
```

**メリット**:
- 型安全性が保証される
- コンパイル時にエラーを検出
- テストの意図が明確
- リファクタリングに強い

#### 4. Verify()で相互作用を検証する

モックが期待通りに呼び出されたかを検証します。

```csharp
[Fact]
public async Task TranslateAsync_WhenCalled_InvokesEngine()
{
    // Arrange
    var engineMock = new Mock<ITranslationEngine>();
    engineMock
        .Setup(e => e.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new TranslationResult { IsSuccess = true });

    var service = new TranslationService(engineMock.Object);

    // Act
    await service.TranslateAsync("Hello", "en", "ja");

    // Assert
    engineMock.Verify(
        e => e.TranslateAsync("Hello", "en", "ja", It.IsAny<CancellationToken>()),
        Times.Once
    );
}
```

### モッキングのアンチパターン

#### ❌ 具象クラスのモック化

```csharp
// 避けるべき
var concreteMock = new Mock<ConcreteTranslationService>();
```

**理由**: 具象クラスの実装詳細に依存してしまう

#### ❌ 過度なSetup

```csharp
// 避けるべき: テストに無関係なSetupが多数
engineMock.Setup(e => e.Name).Returns("TestEngine");
engineMock.Setup(e => e.Version).Returns("1.0");
engineMock.Setup(e => e.SupportedLanguages).Returns(new[] { "en", "ja" });
engineMock.Setup(e => e.IsInitialized).Returns(true);
engineMock.Setup(e => e.MaxBatchSize).Returns(10);
// ... 実際にテストで使うのはTranslateAsyncのみ
```

**理由**: テストの保守性が低下し、意図が不明確になる

#### ❌ リフレクションによるプロパティ書き換え

```csharp
// 避けるべき
var prop = mock.GetType().GetProperty("SomeProperty");
prop.SetValue(mock, "NewValue");
```

**理由**: 型安全性がなく、リファクタリングに弱い。継承を使用すべき。

## 3. OpenCV Wrapper Testing Strategy

### テスト範囲

OpenCVラッパークラス（`OpenCvWrapper`、`WindowsImage`など）のテストでは以下をカバーします：

- **初期化と構築処理**: 画像の読み込み、メモリ確保
- **画像変換機能**: グレースケール、二値化、リサイズ
- **フィルター適用**: ガウシアン、メディアン、バイラテラルフィルター
- **エッジ検出**: Cannyエッジ検出
- **モルフォロジー演算**: 膨張、収縮、オープニング、クロージング
- **テキスト領域検出**: 輪郭検出、境界矩形抽出
- **リソース管理**: Dispose、メモリリーク防止
- **例外処理**: null入力、無効パラメータへの対処

### テストデータ準備

#### 実画像ベースのテスト

```csharp
public class OpenCvWrapperTests : IDisposable
{
    private readonly string _testImagePath;
    private readonly byte[] _testImageData;

    public OpenCvWrapperTests()
    {
        // テスト用画像の準備
        _testImagePath = Path.Combine(
            Path.GetTempPath(),
            $"test_image_{Guid.NewGuid()}.png"
        );

        // 100x100の白い画像を生成
        _testImageData = CreateTestImage(100, 100);
        File.WriteAllBytes(_testImagePath, _testImageData);
    }

    private byte[] CreateTestImage(int width, int height)
    {
        // テスト用のシンプルな画像データを生成
        // 実装詳細は省略
    }

    public void Dispose()
    {
        if (File.Exists(_testImagePath))
        {
            File.Delete(_testImagePath);
        }
    }
}
```

#### ArrayPoolベースのメモリ管理テスト

```csharp
[Fact]
public async Task ConvertToGrayscaleAsync_UsesArrayPool()
{
    // Arrange
    var wrapper = new OpenCvWrapper();
    var sourceImage = await CreateTestImageAsync(100, 100);

    var initialRentCount = ArrayPool<byte>.Shared.RentedCount; // 仮想的なカウンター

    // Act
    using var result = await wrapper.ConvertToGrayscaleAsync(sourceImage);

    // Assert
    Assert.NotNull(result);
    // ArrayPoolが正しく使用され、返却されていることを確認
}
```

### モッキング戦略

#### IAdvancedImageのモック化

```csharp
[Fact]
public async Task ApplyGaussianBlurAsync_WithValidParameters_ReturnsBlurredImage()
{
    // Arrange
    var mockImage = new Mock<IAdvancedImage>();
    mockImage.Setup(i => i.Width).Returns(100);
    mockImage.Setup(i => i.Height).Returns(100);
    mockImage.Setup(i => i.Format).Returns(ImageFormat.Rgb24);
    mockImage.Setup(i => i.ToByteArrayAsync())
        .ReturnsAsync(() =>
        {
            var buffer = ArrayPool<byte>.Shared.Rent(100 * 100 * 3);
            // テスト用のピクセルデータを設定
            return buffer;
        });

    var wrapper = new OpenCvWrapper();

    // Act
    using var result = await wrapper.ApplyGaussianBlurAsync(
        mockImage.Object,
        kernelSize: 5,
        sigmaX: 1.0
    );

    // Assert
    Assert.NotNull(result);
    mockImage.Verify(i => i.ToByteArrayAsync(), Times.Once);
}
```

#### スタブ実装の活用

プラットフォーム固有の機能をテストする場合、スタブ実装を使用します。

```csharp
public class OpenCvWrapperStub : IOpenCvWrapper
{
    public Task<IAdvancedImage> ConvertToGrayscaleAsync(IAdvancedImage source)
    {
        // テスト用の簡易実装
        return Task.FromResult<IAdvancedImage>(new StubAdvancedImage(source.Width, source.Height));
    }

    // その他のメソッドも同様にスタブ実装
}
```

### 非同期メソッドのテスト

#### ConfigureAwait(true)の使用

xUnitテストでは、テスト実行コンテキストを維持するために`ConfigureAwait(true)`を使用します。

```csharp
[Fact]
public async Task ConvertToGrayscaleAsync_WithValidImage_ReturnsGrayscaleImage()
{
    // Arrange
    var wrapper = new OpenCvWrapper();
    var sourceImage = await CreateTestImageAsync(100, 100).ConfigureAwait(true);

    // Act
    var result = await wrapper.ConvertToGrayscaleAsync(sourceImage).ConfigureAwait(true);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(ImageFormat.Grayscale8, result.Format);

    // リソースのクリーンアップ
    result.Dispose();
}
```

#### CancellationTokenのテスト

```csharp
[Fact]
public async Task ApplyFilterAsync_WhenCancelled_ThrowsOperationCanceledException()
{
    // Arrange
    var wrapper = new OpenCvWrapper();
    var sourceImage = await CreateTestImageAsync(100, 100).ConfigureAwait(true);
    var cts = new CancellationTokenSource();
    cts.Cancel(); // 事前にキャンセル

    // Act & Assert
    await Assert.ThrowsAsync<OperationCanceledException>(async () =>
    {
        await wrapper.ApplyGaussianBlurAsync(
            sourceImage,
            kernelSize: 5,
            sigmaX: 1.0,
            cancellationToken: cts.Token
        ).ConfigureAwait(true);
    }).ConfigureAwait(true);
}
```

### リソース管理のテスト

#### Disposeパターンの検証

```csharp
[Fact]
public void Dispose_ReleasesUnmanagedResources()
{
    // Arrange
    var wrapper = new OpenCvWrapper();
    var image = CreateTestImage(100, 100);

    // Act
    wrapper.Dispose();

    // Assert
    // 2回目のDisposeが例外を投げないことを確認
    wrapper.Dispose();
}

[Fact]
public async Task MultipleOperations_ProperlyManageMemory()
{
    // Arrange
    var wrapper = new OpenCvWrapper();
    var initialMemory = GC.GetTotalMemory(true);

    // Act
    for (int i = 0; i < 100; i++)
    {
        var image = await CreateTestImageAsync(100, 100).ConfigureAwait(true);
        var processed = await wrapper.ConvertToGrayscaleAsync(image).ConfigureAwait(true);
        processed.Dispose();
        image.Dispose();
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    var finalMemory = GC.GetTotalMemory(true);

    // Assert
    var memoryIncrease = finalMemory - initialMemory;
    Assert.True(memoryIncrease < 10 * 1024 * 1024, $"Memory leak detected: {memoryIncrease} bytes");
}
```

### 例外処理のテスト

#### パラメータ検証

```csharp
[Fact]
public async Task ConvertToGrayscaleAsync_WithNullSource_ThrowsArgumentNullException()
{
    // Arrange
    var wrapper = new OpenCvWrapper();

    // Act & Assert
    var exception = await Assert.ThrowsAsync<ArgumentNullException>(
        () => wrapper.ConvertToGrayscaleAsync(null!)
    ).ConfigureAwait(true);

    Assert.Equal("source", exception.ParamName);
}

[Theory]
[InlineData(0)]
[InlineData(-1)]
[InlineData(2)] // 偶数はカーネルサイズとして無効
public async Task ApplyGaussianBlurAsync_WithInvalidKernelSize_ThrowsArgumentException(int kernelSize)
{
    // Arrange
    var wrapper = new OpenCvWrapper();
    var image = await CreateTestImageAsync(100, 100).ConfigureAwait(true);

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(
        () => wrapper.ApplyGaussianBlurAsync(image, kernelSize, sigmaX: 1.0)
    ).ConfigureAwait(true);
}
```

#### OpenCV内部エラーのハンドリング

```csharp
[Fact]
public async Task ProcessImage_WhenOpenCvFails_ThrowsImageProcessingException()
{
    // Arrange
    var wrapper = new OpenCvWrapper();
    var corruptedImage = CreateCorruptedImageData();

    // Act & Assert
    await Assert.ThrowsAsync<ImageProcessingException>(
        () => wrapper.ConvertToGrayscaleAsync(corruptedImage)
    ).ConfigureAwait(true);
}
```

### テスト分類

OpenCVテストは実行環境に応じて分類します：

```csharp
[Fact]
[Trait("Category", "Unit")]
public async Task MockBased_FastTest()
{
    // モックベースの高速テスト
}

[Fact]
[Trait("Category", "Integration")]
public async Task RealOpenCV_IntegrationTest()
{
    // 実際のOpenCVを使用する統合テスト
}

[Fact]
[Trait("Category", "LocalOnly")]
public async Task Performance_Benchmark()
{
    // パフォーマンステスト（CI/CD除外）
}
```

## 4. Test Coverage Guidelines

### カバレッジ目標

- **Core層**: 90%以上
- **Infrastructure層**: 80%以上
- **Application層**: 85%以上
- **UI層**: 70%以上（ReactiveUIの性質上）

### 優先的にテストすべき領域

1. **ビジネスロジック**: Application層のサービス、オーケストレーション
2. **データ変換**: 画像処理、OCR結果のパース、翻訳結果の処理
3. **エラーハンドリング**: 例外処理、リトライロジック
4. **リソース管理**: Dispose、ArrayPool、メモリリーク防止
5. **非同期処理**: Task、CancellationToken、デッドロック防止

### テストしなくてよい領域

- **DTOクラス**: 単純なプロパティのみのクラス
- **自動生成コード**: gRPCプロトコルファイルから生成されたコード
- **サードパーティライブラリのラッパー**: PaddleOCR、OpenCVの薄いラッパー（統合テストでカバー）

### CI/CDでのテスト実行

```yaml
# .github/workflows/ci.yml
- name: Run Core Tests
  run: dotnet test tests/Baketa.Core.Tests/Baketa.Core.Tests.csproj
    --configuration Debug
    --filter "Category!=LocalOnly&Category!=Integration"
    --collect:"XPlat Code Coverage"

- name: Run Application Tests
  run: dotnet test tests/Baketa.Application.Tests/Baketa.Application.Tests.csproj
    --configuration Debug
    --filter "Category!=LocalOnly&Category!=Integration"
```

### テスト実行コマンド

```cmd
# 全テスト実行
dotnet test

# 特定プロジェクトのテスト実行
dotnet test tests/Baketa.Core.Tests/

# カテゴリフィルター
dotnet test --filter "Category=Unit"
dotnet test --filter "Category!=LocalOnly"

# 特定クラスのテスト実行
dotnet test --filter "ClassName~TranslationServiceTests"

# 詳細出力
dotnet test --verbosity normal
```

## 5. 継続的な改善

### テスト品質の監視

- **定期的なカバレッジレビュー**: 四半期ごとにカバレッジレポートを確認
- **テスト実行時間の監視**: CI/CDパイプラインのテスト実行時間を追跡
- **フレーク検出**: 不安定なテストを特定し、修正

### ベストプラクティスの更新

- 新しいパターンが確立されたら、このドキュメントを更新
- チームレビューで発見されたアンチパターンを文書化
- 外部ライブラリのアップデートに伴うテスト戦略の見直し

---

**Last Updated**: 2025-11-17
**Related Documents**:
- `E:\dev\Baketa\docs\2-development\coding-standards\` - コーディング標準
- `E:\dev\Baketa\docs\3-architecture\clean-architecture.md` - アーキテクチャ設計
- `E:\dev\Baketa\.github\workflows\ci.yml` - CI/CD設定
