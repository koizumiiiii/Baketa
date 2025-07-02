# Baketa 翻訳基盤実装計画

## はじめに

このドキュメントでは、Baketaプロジェクトにおける翻訳基盤機能（#53, #63, #64, #65）の実装計画を詳細に記述します。各Issue間の依存関係や実装順序、コア機能の設計アプローチを明確にし、効率的な開発を進めるためのガイドラインとして活用します。

## 1. 実装対象のIssue概要

- **#63: WebAPI翻訳エンジンの実装** - 翻訳処理を行うWebAPIベース翻訳エンジンの実装
- **#64: 翻訳結果データとマネージャーシステムの実装** - 翻訳結果の管理と永続化機能の提供
- **#65: 翻訳イベントシステムの実装** - 翻訳処理の各段階と層間を疎結合に連携するイベントシステム
- **#53: 翻訳キャッシュシステム** - 翻訳結果をリアルタイムでキャッシュするシステム

## 2. アーキテクチャと名前空間構造

プロジェクトのクリーンアーキテクチャに基づき、以下の名前空間構造を採用します：

```
Baketa.Translation                     // 翻訳機能のルート名前空間
├── Baketa.Core.Translation            // コアレイヤー：インターフェースと抽象化
│   ├── Abstractions                   // 基本抽象化
│   ├── Events                         // イベント定義
│   ├── Models                         // データモデル
│   └── Services                       // サービスインターフェース
│
├── Baketa.Infrastructure.Translation  // インフラレイヤー：実装
│   ├── Engines                        // 翻訳エンジン実装
│   ├── Cache                          // キャッシュシステム実装
│   ├── Repository                     // データ永続化実装
│   └── Services                       // サービス実装
│
└── Baketa.Application.Translation     // アプリケーションレイヤー：ビジネスロジック
    ├── Services                       // 翻訳アプリケーションサービス
    ├── Handlers                       // イベントハンドラー
    └── Configuration                  // 翻訳設定
```

## 3. 依存関係の整理と実装順序

4つのIssueは相互に依存関係がありますが、効率的な実装のために以下の順序で実装します：

### フェーズ1: インターフェース設計と共通モデル（全Issueで共通）
1. データモデルとインターフェースの詳細設計
2. 例外クラス階層の構築
3. モックオブジェクトの設計と基本テスト体制の構築

### フェーズ2: コア機能実装
1. **#64: 翻訳結果管理システム** - 基本データ構造と操作機能
2. **#63: WebAPI翻訳エンジン** - HTTP基盤と基本翻訳機能
3. **#65: イベントシステム** - イベント集約機構とイベント定義

### フェーズ3: 拡張機能実装
1. **#53: キャッシュシステム** - メモリと永続化キャッシュ実装

### フェーズ4: 統合テストとパフォーマンス最適化
1. 各コンポーネント間の統合
2. エラー処理とリカバリメカニズムの強化
3. パフォーマンス計測と最適化

## 4. 主要インターフェースと実装クラスの設計

### 4.1 データモデル

```csharp
// 翻訳リクエスト
public class TranslationRequest
{
    public string SourceText { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public TranslationContext? Context { get; set; }
    public Dictionary<string, object?> Options { get; } = new();
    public Guid RequestId { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
}

// 翻訳レスポンス
public class TranslationResponse
{
    public Guid RequestId { get; set; }
    public string SourceText { get; set; } = string.Empty;
    public string? TranslatedText { get; set; }
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public string EngineName { get; set; } = string.Empty;
    public long ProcessingTimeMs { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsSuccess { get; set; }
    public TranslationError? Error { get; set; }
}

// 翻訳コンテキスト
public class TranslationContext
{
    public string? GameProfileId { get; set; }
    public string? SceneId { get; set; }
    public Rectangle? ScreenRegion { get; set; }
    public List<string> Tags { get; } = new();
    public Dictionary<string, object?> AdditionalContext { get; } = new();
}

// 翻訳エラー
public class TranslationError
{
    public string ErrorCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public bool IsRetryable { get; set; }
}
```

### 4.2 基本インターフェース

```csharp
// 翻訳エンジンインターフェース
public interface ITranslationEngine : IDisposable
{
    string Name { get; }
    string Description { get; }
    bool RequiresNetwork { get; }
    
    Task<TranslationResponse> TranslateAsync(
        TranslationRequest request, 
        CancellationToken cancellationToken = default);
        
    Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequest> requests, 
        CancellationToken cancellationToken = default);
        
    Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync();
    Task<bool> SupportsLanguagePairAsync(string sourceLanguage, string targetLanguage);
    Task<bool> IsReadyAsync();
    Task<bool> InitializeAsync();
}

// 翻訳マネージャーインターフェース
public interface ITranslationManager
{
    Task<TranslationRecord> SaveTranslationAsync(
        TranslationResponse translationResponse, 
        TranslationContext? context = null);
        
    Task<TranslationRecord?> GetTranslationAsync(
        string sourceText, 
        string sourceLang, 
        string targetLang, 
        TranslationContext? context = null);
        
    Task<IReadOnlyDictionary<string, TranslationRecord?>> GetTranslationStatusAsync(
        IReadOnlyCollection<string> sourceTexts, 
        string sourceLang, 
        string targetLang, 
        TranslationContext? context = null);
        
    Task<bool> UpdateTranslationAsync(Guid recordId, string newTranslatedText);
    Task<bool> DeleteTranslationAsync(Guid recordId);
    Task<IReadOnlyList<TranslationRecord>> SearchTranslationsAsync(TranslationSearchQuery query);
    Task<TranslationStatistics> GetStatisticsAsync(StatisticsOptions options);
}

// 翻訳キャッシュインターフェース
public interface ITranslationCache
{
    Task<TranslationCacheEntry?> GetAsync(string key);
    Task<IDictionary<string, TranslationCacheEntry>> GetManyAsync(IEnumerable<string> keys);
    Task<bool> SetAsync(string key, TranslationCacheEntry entry, TimeSpan? expiration = null);
    Task<bool> SetManyAsync(IDictionary<string, TranslationCacheEntry> entries, TimeSpan? expiration = null);
    Task<bool> RemoveAsync(string key);
    Task<bool> ClearAsync();
    Task<CacheStatistics> GetStatisticsAsync();
}

// イベントインターフェース
public interface ITranslationEvent : IEvent
{
    string SourceText { get; }
    string SourceLanguage { get; }
    string TargetLanguage { get; }
}

public interface IEventHandler<TEvent> where TEvent : IEvent
{
    Task HandleAsync(TEvent @event);
}

public interface IEventAggregator
{
    Task PublishAsync<TEvent>(TEvent @event) where TEvent : IEvent;
    void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent;
    void Unsubscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent;
}
```

### 4.3 主要実装クラス

#### WebAPI翻訳エンジン (#63)

```csharp
// 基本エンジン実装
public abstract class TranslationEngineBase : ITranslationEngine
{
    // 共通実装
}

// WebAPI共通基盤
public abstract class WebApiTranslationEngine : TranslationEngineBase
{
    protected readonly HttpClient _httpClient;
    protected readonly ILogger _logger;
    
    // WebAPI共通実装
}

// 具体的なAPI実装
public class GoogleTranslateEngine : WebApiTranslationEngine
{
    // Google翻訳API実装
}

public class DeepLTranslationEngine : WebApiTranslationEngine
{
    // DeepL API実装
}
```

#### 翻訳結果管理システム (#64)

```csharp
// マネージャー実装
public class TranslationManager : ITranslationManager
{
    private readonly ITranslationRepository _repository;
    private readonly IEventAggregator _eventAggregator;
    
    // 実装
}

// リポジトリ実装
public class SqliteTranslationRepository : ITranslationRepository
{
    private readonly SQLiteConnection _connection;
    
    // 実装
}
```

#### 翻訳キャッシュシステム (#53)

```csharp
// メモリキャッシュ
public class MemoryTranslationCache : ITranslationCache
{
    private readonly MemoryCache _cache;
    
    // 実装
}

// SQLiteキャッシュ
public class SqliteTranslationCache : ITranslationCache
{
    private readonly SQLiteConnection _connection;
    private readonly MemoryTranslationCache _memoryCache;
    
    // 実装
}
```

#### 翻訳イベントシステム (#65)

```csharp
// イベント定義
public class TranslationRequestedEvent : EventBase, ITranslationEvent
{
    // プロパティと実装
}

public class TranslationCompletedEvent : EventBase, ITranslationEvent
{
    // プロパティと実装
}

// イベント集約機構
public class EventAggregator : IEventAggregator
{
    private readonly ConcurrentDictionary<Type, IList<object>> _handlers = new();
    
    // 実装
}
```

## 5. テスト戦略

プロジェクトのテスト戦略は、以下の3層で構成します：

1. **ユニットテスト**
   - 各クラスの基本機能テスト
   - モックを使用した依存性のない単体テスト

2. **統合テスト**
   - コンポーネント間の連携のテスト
   - リアルなデータパスのテスト

3. **パフォーマンステスト**
   - 翻訳処理性能の計測
   - キャッシュヒット率とレイテンシの測定

具体的には以下のテストツールを使用します：

- **xUnit**: テストフレームワーク
- **Moq**: モッキングライブラリ
- **FluentAssertions**: アサーション
- **BenchmarkDotNet**: パフォーマンス計測

## 6. 例外処理の階層

例外処理は、明確な階層を持つ専用例外クラスを使用します：

```csharp
// 基底例外クラス
public abstract class TranslationBaseException : Exception
{
    public string ErrorCode { get; }
    public string LocationInfo { get; }
    public bool IsRetryable { get; }
    
    protected TranslationBaseException(
        string message, 
        string errorCode, 
        string locationInfo = "", 
        bool isRetryable = false, 
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        LocationInfo = locationInfo;
        IsRetryable = isRetryable;
    }
}

// カテゴリ別例外
public class TranslationDataException : TranslationBaseException { }
public class TranslationNetworkException : TranslationBaseException { }
public class TranslationConfigurationException : TranslationBaseException { }

// コンポーネント固有例外
public class TranslationCacheException : TranslationDataException { }
public class TranslationEngineException : TranslationBaseException { }
public class TranslationRepositoryException : TranslationDataException { }
```

## 7. 実装スケジュール

全体で5週間の実装期間を見込み、以下のスケジュールで進めます：

| 週 | フェーズ | 主要タスク |
|----|---------|------------|
| 1 | 設計 | インターフェース設計、データモデル定義、モック実装 |
| 2 | コア実装-1 | 翻訳結果管理と翻訳エンジン基本実装 |
| 3 | コア実装-2 | イベントシステムとキャッシュ基本実装 |
| 4 | 統合 | コンポーネント間の統合とテスト |
| 5 | 最適化 | パフォーマンスチューニングとドキュメント作成 |

詳細なタスク分解と優先順位付けは、各週の開始時に再評価して調整します。

## 8. 依存性注入設定

依存性注入は拡張メソッドを使用して一元管理します：

```csharp
// DIコンテナ設定
public static class TranslationServiceCollectionExtensions
{
    public static IServiceCollection AddTranslationServices(
        this IServiceCollection services, 
        TranslationOptions options)
    {
        // WebAPI翻訳エンジンの登録
        services.AddWebApiTranslationEngines(options.WebApiOptions);
        
        // 翻訳マネージャーの登録
        services.AddTranslationManagement(options.ManagementOptions);
        
        // 翻訳キャッシュの登録
        services.AddTranslationCache(options.CacheOptions);
        
        // イベントシステムの登録
        services.AddTranslationEvents();
        
        return services;
    }
    
    // 各コンポーネントの登録メソッド
    public static IServiceCollection AddWebApiTranslationEngines(
        this IServiceCollection services,
        WebApiTranslationOptions options)
    {
        // エンジン登録の実装
        services.AddHttpClient(); // HttpClientのDI登録
        
        if (options.EnableGoogleTranslate)
        {
            services.AddSingleton<ITranslationEngine, GoogleTranslateEngine>();
        }
        
        if (options.EnableDeepL)
        {
            services.AddSingleton<ITranslationEngine, DeepLTranslationEngine>();
        }
        
        return services;
    }
    
    // 他のコンポーネント登録メソッド...
}
```

## 9. 設定クラス構造

各コンポーネントの設定は階層化された設定クラスで管理します：

```csharp
public class TranslationOptions
{
    public WebApiTranslationOptions WebApiOptions { get; set; } = new();
    public TranslationManagementOptions ManagementOptions { get; set; } = new();
    public TranslationCacheOptions CacheOptions { get; set; } = new();
}

public class WebApiTranslationOptions
{
    public bool EnableGoogleTranslate { get; set; } = true;
    public string GoogleTranslateApiKey { get; set; } = string.Empty;
    public bool EnableDeepL { get; set; } = false;
    public string DeepLApiKey { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 10;
    public int MaxRetryCount { get; set; } = 3;
}

// 他の設定クラス...
```

## 10. パフォーマンス最適化指針

パフォーマンス最適化は以下のポイントに注力します：

1. **効率的なキャッシュ戦略**
   - メモリキャッシュで頻繁なアクセスを高速化
   - 永続化キャッシュで長期的なキャッシュを維持

2. **翻訳エンジンの最適化**
   - バッチ処理でAPIコール数を削減
   - 並列処理で全体的なスループットを向上

3. **非同期処理の最適化**
   - イベント発行の非同期処理で応答性を確保
   - キャンセレーションの適切な伝播で長時間実行タスクを制御

4. **メモリ管理**
   - メモリ使用量の監視と制限
   - 大量データ処理時のストリーミング対応

## 11. 実装後のメンテナンス計画

実装完了後の継続的なメンテナンスと拡張のために以下の計画を策定します：

1. **拡張性のための設計**
   - 新しい翻訳エンジンの追加を容易にするプラグイン設計
   - 設定可能なキャッシュ戦略

2. **監視とメトリクス**
   - エラー率、翻訳時間、キャッシュヒット率の記録
   - パフォーマンスボトルネックの特定

3. **ドキュメント**
   - インターフェースとクラス設計の詳細ドキュメント
   - 使用方法と設定のガイド
   - 拡張ポイントの解説

## 結論

この実装計画に基づき、Baketaプロジェクトの翻訳サブシステムを効率的に構築します。各コンポーネントは明確な責任を持ち、疎結合な設計により、将来の拡張性と保守性を確保します。インターフェース駆動設計と早期のモック実装により、依存コンポーネント間の並行開発を可能にし、モジュール間の連携をスムーズに進めます。
