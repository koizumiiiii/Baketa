# Baketa 翻訳基盤実装のための依存性管理戦略

## 概要

Baketa プロジェクトの翻訳基盤実装（Issues #53, #63, #64, #65）は相互に依存関係がある複雑なコンポーネント群です。本ドキュメントでは、これらのIssue間の依存関係を明確にし、開発プロセスを円滑に進めるための戦略を提供します。

## 1. 依存関係マップ

以下の図は、4つの主要コンポーネント間の依存関係を示します：

```
           ┌───────────────┐
           │     #62       │
           │ 翻訳インターフェース │
           │    (前提)      │
           └───────▲───────┘
                   │
         ┌─────────┴─────────┐
         │                   │
┌────────▼─────────┐ ┌───────▼──────────┐
│       #63        │ │        #64        │
│ WebAPI翻訳エンジン  │◄┼─────► 翻訳結果管理    │
└────────┬─────────┘ └───────┬──────────┘
         │                   │
         │                   │
┌────────▼─────────┐ ┌───────▼──────────┐
│       #65        │ │        #53        │
│   イベントシステム   │◄┼─────► キャッシュシステム │
└──────────────────┘ └──────────────────┘
```

### 主要な依存関係

1. **#62 翻訳インターフェース（前提）**
   - すべてのコンポーネントの基盤となるインターフェースと抽象化
   - 完了済み、もしくは実装の最初のステップとして位置づけ

2. **#63 WebAPI翻訳エンジン → #64 翻訳結果管理**
   - 翻訳エンジンは翻訳結果を管理システムに渡す
   - 結果管理は翻訳エンジンの出力を保存・管理する

3. **#64 翻訳結果管理 → #53 キャッシュシステム**
   - 結果管理はキャッシュシステムを利用して高速なデータアクセスを実現
   - キャッシュシステムは結果管理のデータモデルに依存

4. **#63 WebAPI翻訳エンジン → #65 イベントシステム**
   - 翻訳エンジンは処理状態をイベントとして発行
   - イベントシステムは翻訳エンジンの状態変化を検知

5. **#64 翻訳結果管理 → #65 イベントシステム**
   - 結果管理は処理状態をイベントとして発行
   - イベントシステムは結果管理の状態変化を検知

## 2. 依存性解決戦略

相互依存関係にある複数のコンポーネントを効率的に開発するために、以下の戦略を採用します：

### 2.1 インターフェース先行設計

開発の最初のステップとして、すべてのコンポーネントのインターフェースを先に設計します。これにより、各コンポーネントの責務と相互作用が明確になり、並行開発が可能になります。

```csharp
// 例：翻訳エンジンインターフェース
public interface ITranslationEngine
{
    Task<TranslationResponse> TranslateAsync(
        TranslationRequest request, 
        CancellationToken cancellationToken = default);
}

// 例：翻訳マネージャーインターフェース
public interface ITranslationManager
{
    Task<TranslationRecord> SaveTranslationAsync(
        TranslationResponse response, 
        TranslationContext? context = null);
}
```

### 2.2 モック実装の活用

依存するコンポーネントがまだ完成していない場合でも開発を進めるために、モック実装を活用します。

```csharp
// 例：モック翻訳エンジン
public class MockTranslationEngine : ITranslationEngine
{
    public Task<TranslationResponse> TranslateAsync(
        TranslationRequest request, 
        CancellationToken cancellationToken = default)
    {
        // 固定の応答を返すモック実装
        return Task.FromResult(new TranslationResponse
        {
            RequestId = request.RequestId,
            SourceText = request.SourceText,
            TranslatedText = $"[Translated] {request.SourceText}",
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            EngineName = "MockEngine",
            ProcessingTimeMs = 50,
            Timestamp = DateTime.UtcNow,
            IsSuccess = true
        });
    }
}
```

### 2.3 段階的な統合アプローチ

コンポーネントの開発と統合を段階的に行い、各ステップで機能を検証します。

1. **インターフェース設計 (全てのIssue)**
   - すべてのコンポーネントのインターフェースを設計
   - データモデルの詳細を確定

2. **コア機能実装 (Issue #63, #64)**
   - WebAPI翻訳エンジンの基本機能実装
   - 翻訳結果管理の基本機能実装
   - モックを使用した依存コンポーネントの代替

3. **拡張機能実装 (Issue #53, #65)**
   - イベントシステムの実装と翻訳エンジンとの統合
   - キャッシュシステムの実装と結果管理との統合

4. **フル統合とテスト**
   - すべてのコンポーネントを統合
   - エンドツーエンドのテストと検証

### 2.4 ファクトリークラスと依存性注入

コンポーネント間の依存を疎結合に管理するため、ファクトリークラスと依存性注入を活用します。

```csharp
// 例：翻訳エンジンファクトリー
public interface ITranslationEngineFactory
{
    ITranslationEngine CreateEngine(string engineType);
}

// 例：依存性注入による登録
public static class TranslationServiceCollectionExtensions
{
    public static IServiceCollection AddTranslationEngines(
        this IServiceCollection services)
    {
        services.AddSingleton<ITranslationEngineFactory, TranslationEngineFactory>();
        services.AddSingleton<ITranslationEngine>(sp => 
        {
            var factory = sp.GetRequiredService<ITranslationEngineFactory>();
            return factory.CreateEngine("google");  // デフォルトエンジン
        });
        
        return services;
    }
}
```

## 3. 具体的な実装順序

各Issueの実装順序を以下のように定義します：

### フェーズ1: 基盤設計

1. **インターフェースとモデル設計 (全Issue共通)**
   - 基本データモデル: `TranslationRequest`, `TranslationResponse`, `TranslationContext` など
   - 各インターフェース: `ITranslationEngine`, `ITranslationManager`, `ITranslationCache`, `IEventAggregator` など
   - 例外クラス階層

### フェーズ2: 基本コンポーネント実装

2. **WebAPI翻訳エンジン (Issue #63)**
   - `WebApiTranslationEngine` 抽象クラス
   - `GoogleTranslateEngine`, `DeepLTranslationEngine` など具体的実装
   - APIクライアント基盤と通信最適化

3. **翻訳結果管理 (Issue #64)**
   - `TranslationManager` 実装
   - `SqliteTranslationRepository` 実装
   - データベースアクセスと管理機能

### フェーズ3: 拡張コンポーネント実装

4. **イベントシステム (Issue #65)**
   - `EventAggregator` 実装
   - イベントクラス実装
   - イベントハンドラー実装

5. **キャッシュシステム (Issue #53)**
   - `MemoryTranslationCache` 実装
   - `SqliteTranslationCache` 実装
   - キャッシュポリシーと管理機能

### フェーズ4: 統合と最適化

6. **統合テスト**
   - コンポーネント間の連携確認
   - エラー処理と回復機能

7. **パフォーマンス最適化**
   - キャッシュヒット率の最適化
   - メモリ使用量とレイテンシの最適化

## 4. 依存性管理のためのコーディングパターン

効果的な依存性管理のために、以下のコーディングパターンを採用します：

### 4.1 アダプターパターン

異なるインターフェースを持つコンポーネント間の変換を行います。

```csharp
// 例：キャッシュアダプター
public class TranslationCacheAdapter : ITranslationCache
{
    private readonly ILegacyCache _legacyCache;
    
    public TranslationCacheAdapter(ILegacyCache legacyCache)
    {
        _legacyCache = legacyCache;
    }
    
    // ITranslationCacheインターフェースの実装
    public async Task<TranslationCacheEntry?> GetAsync(string key)
    {
        var legacyEntry = await _legacyCache.GetItemAsync(key);
        if (legacyEntry == null)
            return null;
            
        return ConvertToNewFormat(legacyEntry);
    }
    
    private TranslationCacheEntry ConvertToNewFormat(LegacyCacheItem item)
    {
        // 変換ロジック...
    }
}
```

### 4.2 ストラテジーパターン

処理アルゴリズムを実行時に切り替えられるようにします。

```csharp
// 例：翻訳エンジン選択ストラテジー
public interface ITranslationEngineSelector
{
    ITranslationEngine SelectEngine(TranslationRequest request);
}

public class OptimalEngineSelector : ITranslationEngineSelector
{
    private readonly IEnumerable<ITranslationEngine> _availableEngines;
    
    public OptimalEngineSelector(IEnumerable<ITranslationEngine> availableEngines)
    {
        _availableEngines = availableEngines;
    }
    
    public ITranslationEngine SelectEngine(TranslationRequest request)
    {
        // 最適なエンジン選択ロジック...
    }
}
```

### 4.3 デコレーターパターン

既存の実装に機能を追加します。

```csharp
// 例：キャッシング翻訳エンジンデコレーター
public class CachingTranslationEngine : ITranslationEngine
{
    private readonly ITranslationEngine _innerEngine;
    private readonly ITranslationCache _cache;
    
    public CachingTranslationEngine(
        ITranslationEngine innerEngine,
        ITranslationCache cache)
    {
        _innerEngine = innerEngine;
        _cache = cache;
    }
    
    public async Task<TranslationResponse> TranslateAsync(
        TranslationRequest request, 
        CancellationToken cancellationToken = default)
    {
        // キャッシュチェック
        string cacheKey = GenerateCacheKey(request);
        var cachedEntry = await _cache.GetAsync(cacheKey);
        
        if (cachedEntry != null)
        {
            return CreateResponseFromCache(request, cachedEntry);
        }
        
        // キャッシュにない場合は内部エンジンで翻訳
        var response = await _innerEngine.TranslateAsync(request, cancellationToken);
        
        // 成功したら結果をキャッシュ
        if (response.IsSuccess)
        {
            await _cache.SetAsync(cacheKey, CreateCacheEntry(response));
        }
        
        return response;
    }
    
    // ヘルパーメソッド...
}
```

## 5. 依存性管理のテスト戦略

依存性を適切に管理しつつ、効果的なテストを行うための戦略を提供します。

### 5.1 モックベーステスト

依存コンポーネントをモック化してテストします。

```csharp
[Fact]
public async Task TranslateAsync_WithValidRequest_ReturnsSuccessResponse()
{
    // Arrange
    var mockCache = new Mock<ITranslationCache>();
    mockCache.Setup(m => m.GetAsync(It.IsAny<string>()))
        .ReturnsAsync((TranslationCacheEntry?)null);
        
    var mockEngine = new Mock<ITranslationEngine>();
    mockEngine.Setup(m => m.TranslateAsync(It.IsAny<TranslationRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new TranslationResponse { IsSuccess = true /* ... */ });
        
    var sut = new CachingTranslationEngine(mockEngine.Object, mockCache.Object);
    
    // Act
    var request = new TranslationRequest { /* ... */ };
    var result = await sut.TranslateAsync(request);
    
    // Assert
    Assert.True(result.IsSuccess);
    mockEngine.Verify(m => m.TranslateAsync(It.IsAny<TranslationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    mockCache.Verify(m => m.SetAsync(It.IsAny<string>(), It.IsAny<TranslationCacheEntry>(), null), Times.Once);
}
```

### 5.2 インテグレーションテスト

実際のコンポーネントを組み合わせてテストします。

```csharp
[Fact]
public async Task TranslationSystem_EndToEnd_WorksCorrectly()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddTranslationServices(new TranslationOptions
    {
        // テスト設定...
    });
    
    var serviceProvider = services.BuildServiceProvider();
    var translationService = serviceProvider.GetRequiredService<ITranslationService>();
    
    // Act
    var result = await translationService.TranslateTextAsync(
        "Hello, world!", "en", "ja");
    
    // Assert
    Assert.NotNull(result);
    Assert.Equal("こんにちは、世界！", result.TranslatedText);
}
```

### 5.3 コンポーネントステートベーステスト

コンポーネントの状態変化をテストします。

```csharp
[Fact]
public async Task CacheSystem_MissHitEviction_WorksCorrectly()
{
    // Arrange
    var cache = new MemoryTranslationCache(new MemoryCacheOptions
    {
        SizeLimit = 1024  // 小さいサイズ制限
    });
    
    var entry1 = new TranslationCacheEntry { /* ... */ };
    var entry2 = new TranslationCacheEntry { /* ... */ };
    
    // Act & Assert
    
    // 1. 最初はキャッシュミス
    var result1 = await cache.GetAsync("key1");
    Assert.Null(result1);
    
    // 2. 値を設定
    await cache.SetAsync("key1", entry1);
    
    // 3. キャッシュヒット
    var result2 = await cache.GetAsync("key1");
    Assert.NotNull(result2);
    Assert.Equal(entry1.TranslatedText, result2.TranslatedText);
    
    // 4. 多数のエントリを追加してevictionを発生させる
    for (int i = 0; i < 100; i++)
    {
        await cache.SetAsync($"test_key_{i}", entry2);
    }
    
    // 5. 最初のキーはevictされているはず
    var result3 = await cache.GetAsync("key1");
    Assert.Null(result3);
}
```

## 6. 依存性の段階的な構築

依存性を段階的に構築するためのアプローチを提供します。

### 6.1 最小限のインターフェースから始める

最初は最小限の機能を持つインターフェースから始め、必要に応じて拡張します。

```csharp
// フェーズ1: 最小限のインターフェース
public interface ITranslationEngine
{
    Task<string?> TranslateTextAsync(string text, string sourceLang, string targetLang);
}

// フェーズ2: 拡張されたインターフェース
public interface ITranslationEngine
{
    Task<string?> TranslateTextAsync(string text, string sourceLang, string targetLang);
    Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(IReadOnlyList<TranslationRequest> requests, CancellationToken cancellationToken = default);
}
```

### 6.2 部分的な実装

各コンポーネントは段階的に実装し、まず最も重要な機能から始めます。

```csharp
// フェーズ1: 基本機能のみ
public class BasicTranslationManager : ITranslationManager
{
    public Task<TranslationRecord> SaveTranslationAsync(
        TranslationResponse translationResponse, 
        TranslationContext? context = null)
    {
        // 基本実装...
    }
    
    public Task<TranslationRecord?> GetTranslationAsync(
        string sourceText, 
        string sourceLang, 
        string targetLang, 
        TranslationContext? context = null)
    {
        // 基本実装...
    }
    
    // その他のメソッドは NotImplementedException
    public Task<bool> UpdateTranslationAsync(Guid recordId, string newTranslatedText)
    {
        throw new NotImplementedException("フェーズ2で実装予定");
    }
    
    // ...
}
```

### 6.3 独立したテスト用ビルド

各コンポーネントが他の未完成コンポーネントに依存しない、テスト専用のビルド設定を用意します。

```csharp
// テスト用のDI設定
public static IServiceCollection AddTranslationServicesForTesting(
    this IServiceCollection services)
{
    // 基本サービス
    services.AddSingleton<ITranslationEngine, MockTranslationEngine>();
    services.AddSingleton<ITranslationCache, InMemoryTranslationCache>();
    services.AddSingleton<ITranslationManager, BasicTranslationManager>();
    services.AddSingleton<IEventAggregator, SimpleEventAggregator>();
    
    return services;
}
```

## 7. 統合プロセス

すべてのコンポーネントが実装された後の統合プロセスを定義します。

### 7.1 統合手順

1. **各コンポーネントの単体テスト確認**
   - 各コンポーネントが単体でテストパスすることを確認

2. **最小統合テスト**
   - 2つのコンポーネント間の統合テスト
   - 例：翻訳エンジン + 結果管理の連携テスト

3. **部分統合テスト**
   - 主要なユースケースに必要なコンポーネントの統合
   - 例：翻訳フロー全体のテスト（UI除く）

4. **完全統合テスト**
   - すべてのコンポーネントを含む統合テスト
   - エンドツーエンドのシナリオテスト

### 7.2 統合チェックリスト

各統合ステップで確認すべき項目のチェックリストを提供します。

1. **インターフェース契約の遵守**
   - 各コンポーネントがインターフェース契約を正しく実装していることを確認

2. **データモデルの一貫性**
   - コンポーネント間で受け渡されるデータモデルが一貫していることを確認

3. **エラー処理の連携**
   - あるコンポーネントで発生したエラーが適切に他のコンポーネントに伝播されることを確認

4. **パフォーマンス要件の達成**
   - 統合後のパフォーマンスが要件を満たしていることを確認

5. **リソース管理の適切さ**
   - メモリやデータベース接続などのリソースが適切に管理されていることを確認

## 結論

Baketa プロジェクトの翻訳基盤実装における依存性管理は、インターフェース先行設計、モック実装の活用、段階的な統合アプローチを組み合わせることで効果的に行うことができます。各Issueの実装は相互に依存していますが、適切な設計パターンとテスト戦略を用いることで、並行開発と安全な統合が可能になります。

この依存性管理戦略に従うことで、複雑なコンポーネント間の連携を効率的に実現し、高品質な翻訳基盤を構築できます。
