# 翻訳ユニットテスト作成ガイド

## 1. テスト戦略の概要

Baketaプロジェクトの翻訳コンポーネントに対する効果的なテスト戦略を以下に示します。テストピラミッドの考え方に基づき、単体テスト、統合テスト、エンドツーエンドテストの3つのレベルでテストを設計します。

### 1.1 テストレベル

1. **単体テスト (Unit Tests)**
   - 個々のクラス、メソッドの機能をテスト
   - モックを利用した依存コンポーネントの分離
   - 高速で実行可能なテスト

2. **統合テスト (Integration Tests)**
   - 複数のコンポーネントの連携をテスト
   - 部分的な依存関係を実際のコンポーネントで構成
   - パフォーマンスとエラー処理のテスト

3. **機能テスト (Functional Tests)**
   - エンドツーエンドでの翻訳処理をテスト
   - 実際の依存関係を使用
   - ユーザーシナリオのテスト

## 2. テスト対象コンポーネント

テストの優先度が高いコンポーネントを以下に示します：

### 2.1 コアコンポーネント

1. **翻訳エンジン基盤**
   - `TranslationEngineBase`
   - `WebApiTranslationEngineBase`
   - `TranslationEngineAdapter`
   - `TranslationFactoryExtensions`

2. **翻訳キャッシュシステム**
   - `MemoryTranslationCache`
   - `TranslationCacheOptions`

3. **翻訳結果管理**
   - `InMemoryTranslationRepository`
   - `TranslationContext`

4. **イベントシステム**
   - `LoggingTranslationEventHandler`
   - `TranslationEvents`
   - `EventAggregator`

### 2.2 アプリケーション層コンポーネント

1. **標準翻訳パイプライン**
   - `StandardTranslationPipeline`
   - `TranslationPipelineOptions`

2. **翻訳サービス**
   - `StandardTranslationService`
   - `TranslationTransactionManager`

## 3. テストパターン

効果的なテストを作成するためのパターンを以下に示します：

### 3.1 AAA (Arrange-Act-Assert) パターン

```csharp
[Fact]
public async Task TranslateAsync_ValidRequest_ReturnsCorrectTranslation()
{
    // Arrange
    var options = new TranslationOptions();
    var engine = new SomeTranslationEngine(options);
    var request = new TranslationRequest
    {
        SourceText = "こんにちは",
        SourceLanguage = new Language { Code = "ja" },
        TargetLanguage = new Language { Code = "en" }
    };

    // Act
    var result = await engine.TranslateAsync(request);

    // Assert
    Assert.NotNull(result);
    Assert.Equal("Hello", result.TranslatedText);
    Assert.True(result.IsSuccess);
}
```

### 3.2 テストケース設計

各テストケースでは以下のシナリオをカバーするように設計します：

1. **正常系**
   - 標準的な入力に対して期待通りの出力が得られること

2. **境界値**
   - 最大長のテキスト
   - 空文字
   - 特殊文字を含むテキスト

3. **例外ケース**
   - Nullパラメータ
   - 無効な言語コード
   - タイムアウト
   - ネットワークエラー

4. **エラー処理**
   - エラー発生時の適切なエラー情報返却
   - リトライ機能のテスト
   - 回復メカニズムのテスト

## 4. モックの活用

依存コンポーネントをモック化するためのパターンを以下に示します：

### 4.1 インターフェースモック

```csharp
// Moq を使用したモック例
var translationEngineMock = new Mock<ITranslationEngine>();
translationEngineMock
    .Setup(e => e.TranslateAsync(It.IsAny<TranslationRequest>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(new TranslationResponse { 
        TranslatedText = "Hello", 
        IsSuccess = true 
    });

var engine = translationEngineMock.Object;
```

### 4.2 依存コンポーネントの分離

```csharp
// テスト用の依存コンポーネント（フェイク）
public class InMemoryTranslationCache : ITranslationCache
{
    private readonly Dictionary<string, TranslationCacheEntry> _cache = new();

    public Task<TranslationCacheEntry?> GetAsync(string key)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            return Task.FromResult<TranslationCacheEntry?>(entry);
        }
        return Task.FromResult<TranslationCacheEntry?>(null);
    }

    // その他のメソッド実装...
}
```

## 5. テストケース例

各コンポーネントのテストケース例を以下に示します：

### 5.1 翻訳エンジンテスト

```csharp
public class TranslationEngineBaseTests
{
    [Fact]
    public async Task TranslateAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        var engine = new MockTranslationEngine();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            engine.TranslateAsync(null));
    }

    [Fact]
    public async Task TranslateAsync_ValidRequest_ReturnsTranslation()
    {
        // Arrange
        var engine = new MockTranslationEngine();
        var request = new TranslationRequest
        {
            SourceText = "Hello",
            SourceLanguage = new Language { Code = "en" },
            TargetLanguage = new Language { Code = "ja" }
        };

        // Act
        var result = await engine.TranslateAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("こんにちは", result.TranslatedText);
        Assert.True(result.IsSuccess);
    }

    // 以下、その他のテストケース
}
```

### 5.2 キャッシュシステムテスト

```csharp
public class MemoryTranslationCacheTests
{
    [Fact]
    public async Task SetAndGetAsync_ValidEntry_ReturnsCachedEntry()
    {
        // Arrange
        var options = new TranslationCacheOptions
        {
            MemoryCacheSize = 100,
            DefaultExpirationHours = 1
        };
        var cache = new MemoryTranslationCache(options);
        var key = "test-key";
        var entry = new TranslationCacheEntry
        {
            SourceText = "Hello",
            TranslatedText = "こんにちは",
            SourceLanguage = "en",
            TargetLanguage = "ja"
        };

        // Act
        await cache.SetAsync(key, entry);
        var result = await cache.GetAsync(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(entry.SourceText, result.SourceText);
        Assert.Equal(entry.TranslatedText, result.TranslatedText);
    }

    [Fact]
    public async Task GetAsync_ExpiredEntry_ReturnsNull()
    {
        // Arrange
        var options = new TranslationCacheOptions
        {
            MemoryCacheSize = 100,
            DefaultExpirationHours = 0.001 // 3.6秒
        };
        var cache = new MemoryTranslationCache(options);
        var key = "test-key";
        var entry = new TranslationCacheEntry
        {
            SourceText = "Hello",
            TranslatedText = "こんにちは",
            SourceLanguage = "en",
            TargetLanguage = "ja"
        };
        
        // Act
        await cache.SetAsync(key, entry);
        await Task.Delay(5000); // 5秒待機
        var result = await cache.GetAsync(key);
        
        // Assert
        Assert.Null(result);
    }

    // 以下、その他のテストケース
}
```

### 5.3 翻訳パイプラインテスト

```csharp
public class StandardTranslationPipelineTests
{
    [Fact]
    public async Task ProcessAsync_MultipleEngines_SelectsBestEngine()
    {
        // Arrange
        var serviceProvider = CreateServiceProvider();
        var pipeline = serviceProvider.GetRequiredService<ITranslationPipeline>();
        var request = new TranslationRequest
        {
            SourceText = "Hello",
            SourceLanguage = new Language { Code = "en" },
            TargetLanguage = new Language { Code = "ja" }
        };
        
        // Act
        var result = await pipeline.ProcessAsync(request);
        
        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Equal("HighQualityEngine", result.EngineName);
    }
    
    [Fact]
    public async Task ProcessAsync_CachedResult_ReturnsCacheHit()
    {
        // Arrange
        var serviceProvider = CreateServiceProvider();
        var pipeline = serviceProvider.GetRequiredService<ITranslationPipeline>();
        var cache = serviceProvider.GetRequiredService<ITranslationCache>();
        var request = new TranslationRequest
        {
            SourceText = "Hello",
            SourceLanguage = new Language { Code = "en" },
            TargetLanguage = new Language { Code = "ja" }
        };
        
        var cacheEntry = new TranslationCacheEntry
        {
            SourceText = "Hello",
            TranslatedText = "こんにちは（キャッシュ）",
            SourceLanguage = "en",
            TargetLanguage = "ja"
        };
        
        var cacheKey = "some-cache-key"; // キャッシュキー生成ロジックに合わせて調整
        await cache.SetAsync(cacheKey, cacheEntry);
        
        // Act
        var result = await pipeline.ProcessAsync(request);
        
        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Equal("こんにちは（キャッシュ）", result.TranslatedText);
    }
    
    // 他のテストケース...
    
    private IServiceProvider CreateServiceProvider()
    {
        // テスト用のサービスプロバイダーを構築
        var services = new ServiceCollection();
        
        // モックの設定
        var engineDiscoveryMock = new Mock<ITranslationEngineDiscovery>();
        var engineMock1 = new Mock<ITranslationEngine>();
        var engineMock2 = new Mock<ITranslationEngine>();
        
        // エンジン1の設定（低品質エンジン）
        engineMock1.Setup(e => e.Name).Returns("LowQualityEngine");
        engineMock1.Setup(e => e.TranslateAsync(It.IsAny<TranslationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranslationResponse { 
                TranslatedText = "こんにちは（低品質）", 
                EngineName = "LowQualityEngine",
                IsSuccess = true 
            });
            
        // エンジン2の設定（高品質エンジン）
        engineMock2.Setup(e => e.Name).Returns("HighQualityEngine");
        engineMock2.Setup(e => e.TranslateAsync(It.IsAny<TranslationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranslationResponse { 
                TranslatedText = "こんにちは（高品質）", 
                EngineName = "HighQualityEngine",
                IsSuccess = true 
            });
        
        // エンジン検出モックの設定
        engineDiscoveryMock.Setup(d => d.GetAvailableEnginesAsync())
            .ReturnsAsync(new List<ITranslationEngine> { engineMock1.Object, engineMock2.Object });
        engineDiscoveryMock.Setup(d => d.GetBestEngineForLanguagePairAsync(It.IsAny<LanguagePair>()))
            .ReturnsAsync(engineMock2.Object);
            
        // キャッシュの設定
        services.AddSingleton<ITranslationCache>(new InMemoryTranslationCache());
        
        // サービス登録
        services.AddSingleton(engineDiscoveryMock.Object);
        services.AddTransient<ITranslationPipeline, StandardTranslationPipeline>();
        
        return services.BuildServiceProvider();
    }
}
```

## 6. 統合テストのセットアップ

複数のコンポーネントの連携をテストするための統合テストの例を以下に示します：

```csharp
public class TranslationPipelineIntegrationTests
{
    [Fact]
    public async Task CompleteTranslationPipeline_EndToEnd_Success()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // 実際のコンポーネントを登録
        services.AddTranslation(options => {
            options.UseMemoryCache = true;
            options.RegisterTranslationEngine<MockTranslationEngine>();
        });
        
        var serviceProvider = services.BuildServiceProvider();
        var translationService = serviceProvider.GetRequiredService<ITranslationService>();
        
        var request = new TranslationRequest
        {
            SourceText = "Hello world",
            SourceLanguage = new Language { Code = "en" },
            TargetLanguage = new Language { Code = "ja" }
        };
        
        // Act
        var result = await translationService.TranslateAsync(request);
        
        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Equal("こんにちは世界", result.TranslatedText);
        
        // キャッシュのテスト - 同じリクエストを再度実行
        var cachedResult = await translationService.TranslateAsync(request);
        Assert.Equal("こんにちは世界", cachedResult.TranslatedText);
        
        // イベントハンドラーの確認（モックロガーなどを使用）
    }
}
```

## 7. テスト自動化

テストの自動化とCI/CDへの統合に関する推奨事項：

1. **テスト分類**
   - `[Trait("Category", "Unit")]`: 単体テスト
   - `[Trait("Category", "Integration")]`: 統合テスト
   - `[Trait("Category", "Functional")]`: 機能テスト

2. **CI/CDパイプラインでの実行**
   - 単体テスト: すべてのコミットで実行
   - 統合テスト: プルリクエスト時に実行
   - 機能テスト: マージ前またはリリースビルド時に実行

3. **テストカバレッジ**
   - 目標: コア機能の80%以上のカバレッジ
   - カバレッジレポートの定期的な確認
   - 未カバーの重要コードパスの特定と対応

## 8. 翻訳機能特有のテスト考慮事項

翻訳機能特有のテスト考慮事項を以下に示します：

### 8.1 言語対応のテスト

```csharp
[Theory]
[InlineData("en", "ja")]
[InlineData("ja", "en")]
[InlineData("zh", "en")]
[InlineData("en", "fr")]
[InlineData("ko", "en")]
// その他の言語ペア
public async Task TranslateAsync_VariousLanguagePairs_HandlesCorrectly(string sourceCode, string targetCode)
{
    // Arrange
    var engine = new ActualTranslationEngine(); // 実際のエンジンまたはモック
    var request = new TranslationRequest
    {
        SourceText = "テストテキスト",
        SourceLanguage = new Language { Code = sourceCode },
        TargetLanguage = new Language { Code = targetCode }
    };
    
    // Act
    var result = await engine.TranslateAsync(request);
    
    // Assert
    Assert.NotNull(result);
    Assert.True(result.IsSuccess);
    Assert.NotNull(result.TranslatedText);
}
```

### 8.2 並行処理のテスト

```csharp
[Fact]
public async Task TranslateMultipleRequestsConcurrently_HandlesCorrectly()
{
    // Arrange
    var engine = CreateTranslationEngine();
    var requests = new List<TranslationRequest>();
    for (int i = 0; i < 10; i++)
    {
        requests.Add(new TranslationRequest
        {
            SourceText = $"テストテキスト{i}",
            SourceLanguage = new Language { Code = "ja" },
            TargetLanguage = new Language { Code = "en" }
        });
    }
    
    // Act
    var tasks = requests.Select(r => engine.TranslateAsync(r));
    var results = await Task.WhenAll(tasks);
    
    // Assert
    Assert.Equal(10, results.Length);
    Assert.All(results, r => Assert.True(r.IsSuccess));
    Assert.All(results, r => Assert.NotNull(r.TranslatedText));
}
```

### 8.3 エラー回復のテスト

```csharp
[Fact]
public async Task TranslateAsync_EngineFailure_FallsBackToAlternative()
{
    // Arrange
    var serviceProvider = CreateServiceProviderWithFailingPrimaryEngine();
    var pipeline = serviceProvider.GetRequiredService<ITranslationPipeline>();
    var request = new TranslationRequest
    {
        SourceText = "Hello",
        SourceLanguage = new Language { Code = "en" },
        TargetLanguage = new Language { Code = "ja" }
    };
    
    // Act
    var result = await pipeline.ProcessAsync(request);
    
    // Assert
    Assert.NotNull(result);
    Assert.True(result.IsSuccess);
    Assert.Equal("BackupEngine", result.EngineName);
}
```

## 9. テスト実装チェックリスト

新しいテストを実装する際のチェックリスト：

- [ ] AAA (Arrange-Act-Assert) パターンを使用しているか
- [ ] テスト名は目的を明確に示しているか
- [ ] 依存関係が適切に分離されているか
- [ ] 境界値と例外ケースが考慮されているか
- [ ] 非同期メソッドのテストには `async/await` を使用しているか
- [ ] 並行処理の問題はないか
- [ ] モックの設定は適切か
- [ ] テストデータは十分多様か
- [ ] テストのパフォーマンスに問題はないか
- [ ] カルチャ依存の問題はないか（日時、数値、文字列比較など）

## 10. 参考リソース

- [xUnit ドキュメント](https://xunit.net/docs/getting-started/netcore/cmdline)
- [Moq ドキュメント](https://github.com/moq/moq4/wiki/Quickstart)
- [クリーンなユニットテストの書き方](https://docs.microsoft.com/ja-jp/dotnet/core/testing/unit-testing-best-practices)
- [非同期コードのテスト](https://docs.microsoft.com/ja-jp/dotnet/core/testing/unit-testing-best-practices#async-code)

このガイドに従ってテストを実装することで、翻訳システムの品質と信頼性を確保することができます。テストを先行して作成するテスト駆動開発（TDD）のアプローチも検討してください。