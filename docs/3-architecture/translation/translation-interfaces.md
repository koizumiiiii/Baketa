# 翻訳エンジンインターフェース

## 概要

Baketaの翻訳機能の中核となる翻訳エンジンインターフェースについて解説します。このインターフェースは、様々な翻訳エンジン（Web API、ローカルモデル）を統一的に扱うための抽象化レイヤーです。

## インターフェース階層

- **ITranslationEngine**: 基本的な翻訳エンジン機能を定義
- **TranslationEngineBase**: 共通実装を提供する抽象クラス
- **具象実装クラス**: 特定の翻訳エンジンを実装するクラス

## 主要機能

### 1. 翻訳機能

```csharp
// テキスト翻訳
Task<TranslationResponse> TranslateAsync(
    TranslationRequest request, 
    CancellationToken cancellationToken = default);
    
// バッチ翻訳
Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
    IReadOnlyList<TranslationRequest> requests, 
    CancellationToken cancellationToken = default);
```

### 2. 言語サポート管理

```csharp
// サポートしている言語ペアを取得
Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync();

// 言語ペアのサポート確認
Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair);
```

### 3. ステータス管理

```csharp
// エンジンの準備状態確認
Task<bool> IsReadyAsync();

// エンジンの初期化
Task<bool> InitializeAsync();
```

## データモデル

### TranslationRequest

翻訳リクエストを表すモデルです。

```csharp
public class TranslationRequest
{
    // 翻訳元テキスト
    public required string SourceText { get; set; }
    
    // 元言語
    public required Language SourceLanguage { get; set; }
    
    // 対象言語
    public required Language TargetLanguage { get; set; }
    
    // 翻訳コンテキスト（オプション）
    public string? Context { get; set; }
    
    // リクエストオプション
    public Dictionary<string, object?> Options { get; } = new();
    
    // リクエストのユニークID
    public Guid RequestId { get; } = Guid.NewGuid();
}
```

### TranslationResponse

翻訳レスポンスを表すモデルです。

```csharp
public class TranslationResponse
{
    // 対応するリクエストのID
    public required Guid RequestId { get; set; }
    
    // 翻訳元テキスト
    public required string SourceText { get; set; }
    
    // 翻訳結果テキスト
    public string? TranslatedText { get; set; }
    
    // 翻訳元言語
    public required Language SourceLanguage { get; set; }
    
    // 翻訳先言語
    public required Language TargetLanguage { get; set; }
    
    // 使用された翻訳エンジン名
    public required string EngineName { get; set; }
    
    // 翻訳の信頼度スコア
    public float ConfidenceScore { get; set; } = -1.0f;
    
    // 翻訳処理時間（ミリ秒）
    public long ProcessingTimeMs { get; set; }
    
    // 翻訳が成功したかどうか
    public bool IsSuccess { get; set; }
    
    // エラー情報
    public TranslationError? Error { get; set; }
    
    // メタデータ
    public Dictionary<string, object?> Metadata { get; } = new();
}
```

## エラー処理

エラー処理は、TranslationErrorクラスを通じて統一的に行われます。

```csharp
public class TranslationError
{
    // エラーコード
    public required string ErrorCode { get; set; }
    
    // エラーメッセージ
    public required string Message { get; set; }
    
    // 詳細
    public string? Details { get; set; }
    
    // 例外情報
    public Exception? Exception { get; set; }
    
    // 標準エラーコード定数
    public static readonly string NetworkError = "NetworkError";
    public static readonly string AuthenticationError = "AuthenticationError";
    public static readonly string QuotaExceeded = "QuotaExceeded";
    public static readonly string ServiceUnavailable = "ServiceUnavailable";
    public static readonly string UnsupportedLanguagePair = "UnsupportedLanguagePair";
    public static readonly string InvalidRequest = "InvalidRequest";
    public static readonly string InternalError = "InternalError";
    public static readonly string TimeoutError = "TimeoutError";
}
```

## ライフサイクル管理

翻訳エンジンは正しくリソース管理を行うため、IDisposableインターフェースを実装しています。

```csharp
public interface ITranslationEngine : IDisposable
{
    // ...インターフェースメンバー...
}
```

## オフライン対応

オフライン環境での動作をサポートするため、ネットワーク要件を明示的に定義しています。

```csharp
// エンジンがオンライン接続を必要とするかどうか
bool RequiresNetwork { get; }

// ネットワーク接続確認
protected virtual Task<bool> CheckNetworkConnectivityAsync();
```

## 拡張ポイント

新しい翻訳エンジンを追加する場合は、ITranslationEngineを実装するか、
より簡単な方法としてTranslationEngineBaseを継承し、抽象メソッドを実装します。

```csharp
public class CustomTranslationEngine : TranslationEngineBase
{
    public override string Name => "CustomEngine";
    public override string Description => "カスタム翻訳エンジン";
    public override bool RequiresNetwork => true;
    
    // 必須の抽象メソッドを実装
    protected override Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync() { ... }
    protected override Task<TranslationResponse> TranslateInternalAsync(...) { ... }
    protected override Task<bool> InitializeInternalAsync() { ... }
}
```

## 性能考慮事項

- 初期化は非同期で行われ、同時に複数回呼び出されても安全です
- バッチ翻訳はパフォーマンス最適化のために提供されています
- 処理時間計測機能が組み込まれています

## テスト設計の考慮事項

翻訳エンジンのテストでは、以下のアプローチを推奨します：

### モック翻訳エンジンの使用

`MockTranslationEngine`クラスを使用して翻訳機能をテストできます：

```csharp
var logger = new Mock<ILogger<MockTranslationEngine>>().Object;
var mockEngine = new MockTranslationEngine(logger);

// 事前定義翻訳のテスト
var response = await mockEngine.TranslateAsync(
    new TranslationRequest
    {
        SourceText = "Hello",
        SourceLanguage = Language.English,
        TargetLanguage = Language.Japanese
    });

Assert.Equal("こんにちは", response.TranslatedText);
```

### カスタムエンジンの継承

特定の動作をカスタマイズしたい場合は、`MockTranslationEngine`を継承してカスタム実装を作成します：

```csharp
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
        _customName = customName;
    }
}
```

### テスト設計の注意点

- 具象クラスを直接`Mock<T>`でモック化する代わりに、継承またはインターフェースモックを使用する
- 翻訳処理の遅延やエラー率をシミュレートするパラメータを活用する
- 翻訳前後のテキスト比較によるパラメータ化テストを実装する

詳細なモッキングのベストプラクティスについては、[docs/4-testing/guidelines/mocking-best-practices.md](../../4-testing/guidelines/mocking-best-practices.md) を参照してください。
