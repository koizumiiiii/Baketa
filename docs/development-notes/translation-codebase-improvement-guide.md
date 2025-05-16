# Baketa翻訳コードベース改善ガイド

## はじめに

このドキュメントはBaketa翻訳基盤の改善を継続的に行うためのガイドラインです。コード品質を維持し、将来的な拡張性を確保するための推奨プラクティスをまとめています。

## 1. 非同期プログラミングのベストプラクティス

### 1.1 ConfigureAwaitの使用

すべての非同期メソッドでは、`ConfigureAwait(false)`を使用してください。これはUI関連のデッドロックを防止し、パフォーマンスを向上させます。

```csharp
// 推奨
await SomeMethodAsync().ConfigureAwait(false);

// 非推奨
await SomeMethodAsync(); // デッドロックのリスクあり
```

### 1.2 非同期メソッドの設計

非同期メソッドを設計する際は以下の原則に従ってください：

1. 非同期メソッドは `Async` サフィックスを持つべき
2. 常に`Task`または`Task<T>`を返す（`void`は避ける）
3. `CancellationToken`パラメータを提供する
4. 内部で呼び出す非同期メソッドにも`CancellationToken`を渡す

```csharp
// 良い例
public async Task<TranslationResponse> TranslateAsync(
    TranslationRequest request, 
    CancellationToken cancellationToken = default)
{
    var result = await _engine.ProcessAsync(request, cancellationToken).ConfigureAwait(false);
    return result;
}

// 避けるべき例
public async void Translate(TranslationRequest request) // voidを返し、キャンセル不可
{
    await _engine.Process(request); // CancellationTokenなし
}
```

### 1.3 同期コードでの非同期メソッド呼び出し

同期コンテキストでどうしても非同期メソッドを呼び出す必要がある場合：

```csharp
// 避けるべき（デッドロックの可能性）
result = asyncMethod.Result;
asyncMethod.Wait();

// 代替方法（同期コンテキストなしでブロック）
result = Task.Run(() => asyncMethod()).Result;

// より良い代替方法（可能な場合）
// コードを非同期パターンに再設計
```

## 2. Null参照の安全な処理

### 2.1 Null許容参照型の活用

C# 8.0以降のNull許容参照型を活用してください：

```csharp
// Null許容型
public string? OptionalProperty { get; set; }

// Null非許容型
public required string RequiredProperty { get; set; }
```

### 2.2 Nullチェックパターン

パブリックAPIでは、必ず入力値のNullチェックを行ってください：

```csharp
public void ProcessRequest(TranslationRequest request)
{
    // .NET 6.0以降
    ArgumentNullException.ThrowIfNull(request);
    
    // もしくは従来の方法
    if (request == null)
    {
        throw new ArgumentNullException(nameof(request));
    }
    
    // 処理...
}
```

### 2.3 Null合体演算子の活用

Null値に対するデフォルト値の提供には、Null合体演算子を使用してください：

```csharp
// 良い例
var name = person?.Name ?? "Unknown";

// 代替パターン（条件付きアクセス演算子とNull合体演算子）
var address = person?.Address?.Street ?? "No address";
```

## 3. コレクション設計のベストプラクティス

### 3.1 公開コレクションプロパティ

公開コレクションプロパティは読み取り専用インターフェースを使用してください：

```csharp
// 内部フィールド
private readonly List<string> _tags = new();

// 公開プロパティ
public IReadOnlyList<string> Tags => _tags;

// 操作メソッド
public void AddTag(string tag) { /* ... */ }
public void RemoveTag(string tag) { /* ... */ }
```

### 3.2 コレクション初期化の最適化

コレクション初期化は最適化された方法で行ってください：

```csharp
// 推奨（容量を予め指定）
var items = new List<string>(expectedCapacity);

// 推奨（C# 9.0以降）
var items = new List<string> { "Item1", "Item2" };

// 非推奨（複数回の拡張が発生する可能性）
var items = new List<string>();
items.Add("Item1");
items.Add("Item2");
// ...
```

### 3.3 LINQ最適化

LINQは便利ですが、パフォーマンスに注意してください：

```csharp
// 良い例（遅延評価とメモリ効率）
var result = items.Where(x => x.IsValid)
                  .Select(x => x.Name)
                  .ToList(); // 必要な場合のみ実体化

// 避けるべき例（複数回の列挙）
var filtered = items.Where(x => x.IsValid); // IEnumerable
var count = filtered.Count(); // 一度列挙
var first = filtered.FirstOrDefault(); // 再度列挙
```

## 4. 例外処理のベストプラクティス

### 4.1 例外の種類

例外は適切な種類を使用してください：

```csharp
// 引数関連
throw new ArgumentNullException(nameof(parameter));
throw new ArgumentOutOfRangeException(nameof(parameter));
throw new ArgumentException("Invalid argument", nameof(parameter));

// ビジネスロジック
throw new InvalidOperationException("Cannot process in current state");

// リソースアクセス
throw new IOException("Failed to read file");
```

### 4.2 例外のキャッチ

一般的な例外のキャッチは避け、具体的な例外をキャッチしてください：

```csharp
// 良い例
try
{
    // 何かの処理
}
catch (IOException ex)
{
    _logger.LogError(ex, "ファイル読み込みエラー");
    // 回復処理
}
catch (TimeoutException ex)
{
    _logger.LogError(ex, "タイムアウトエラー");
    // 回復処理
}

// 避けるべき例（CA1031違反）
try
{
    // 何かの処理
}
catch (Exception ex) // 全ての例外を捕捉（避けるべき）
{
    _logger.LogError(ex, "エラーが発生しました");
}
```

特定の理由で一般的な例外をキャッチする必要がある場合は、コメントで理由を説明してください：

```csharp
// 例外的に認められる場合
try
{
    // プラグイン処理（外部コード実行）
}
catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
{
    // プラグインの失敗がアプリケーション全体に影響しないようにするために
    // 一般的な例外をキャッチしています
    _logger.LogError(ex, "プラグイン実行エラー: {PluginName}", pluginName);
    return ErrorResult.FromException(ex);
}
```

## 5. コードスタイルと最適化

### 5.1 文字列操作

文字列操作は最適化された方法で行ってください：

```csharp
// C# 8.0以降で推奨
var path = $"{directory}{Path.DirectorySeparatorChar}{filename}";

// 大きな文字列連結
var sb = new StringBuilder();
foreach (var item in items)
{
    sb.Append(item);
}
var result = sb.ToString();

// コードスタイル上の推奨（C# 11以降）
string text = """
    複数行のテキストは
    文字列リテラルを使用
    """;
```

### 5.2 メソッド設計

メソッドはシンプルで単一責任を持つように設計してください：

- 1メソッドは1つの論理的操作のみを行う
- メソッドの長さは30行以内を目指す
- パラメータの数は3-4個以内に抑える

```csharp
// 良い例
public TranslationResult Translate(string text, Language source, Language target)
{
    // 単一の責任：翻訳処理
}

// 避けるべき例
public void ProcessAndTranslateAndSaveAndNotify(string text, /*多数のパラメータ*/)
{
    // 複数の責任が混在
}
```

### 5.3 コード解析ツールの活用

IDE組み込みのコード分析ツールを活用してください：

- ReSharperやRider提案に注目
- Visual Studioのコード分析警告を解消
- `.editorconfig`で一貫したスタイルを強制

## 6. 名前空間設計と整理

### 6.1 名前空間の階層

一貫した名前空間階層を維持してください：

```
Baketa.Core.Translation                 // ルート名前空間
├── Baketa.Core.Translation.Abstractions // 基本インターフェース定義
├── Baketa.Core.Translation.Models       // モデル定義
│   ├── Common                           // 共通モデル
│   ├── Requests                         // リクエスト関連
│   ├── Responses                        // レスポンス関連
│   └── Events                           // イベント関連
├── Baketa.Core.Translation.Services     // サービス実装
└── Baketa.Core.Translation.Common       // 共通ユーティリティ
```

### 6.2 名前空間競合の回避

名前空間競合は以下の方法で回避します：

1. 一貫した命名規則
2. 必要な場合は明示的なエイリアスを使用
3. 移行計画を立てて段階的に統一

```csharp
// 必要に応じてエイリアスを使用
using CoreModels = Baketa.Core.Models.Translation;
using TransModels = Baketa.Core.Translation.Models;

// 長期的には統一名前空間に移行
```

## 7. テスト駆動開発の促進

### 7.1 単体テスト

各コンポーネントには十分な単体テストを用意してください：

- 各パブリックメソッドは少なくとも1つのテストを持つべき
- エッジケースや境界値のテストを含める
- 例外のテストを忘れない

```csharp
[Fact]
public void Translate_WithNullInput_ThrowsArgumentNullException()
{
    // Arrange
    var translator = new Translator();
    
    // Act & Assert
    Assert.Throws<ArgumentNullException>(() => translator.Translate(null));
}
```

### 7.2 モックの適切な使用

依存関係はモックを使用してテストしてください：

```csharp
[Fact]
public async Task TranslateAsync_CallsEngineCorrectly()
{
    // Arrange
    var mockEngine = new Mock<ITranslationEngine>();
    mockEngine.Setup(e => e.TranslateAsync(It.IsAny<TranslationRequest>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new TranslationResponse { /* ... */ });
    
    var service = new TranslationService(mockEngine.Object);
    
    // Act
    await service.TranslateAsync(new TranslationRequest());
    
    // Assert
    mockEngine.Verify(e => e.TranslateAsync(It.IsAny<TranslationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
}
```

## 8. ドキュメント化

### 8.1 XMLドキュメントコメント

パブリックAPIには必ずXMLドキュメントコメントを記述してください：

```csharp
/// <summary>
/// テキストを翻訳します
/// </summary>
/// <param name="request">翻訳リクエスト</param>
/// <param name="cancellationToken">キャンセレーショントークン</param>
/// <returns>翻訳結果</returns>
/// <exception cref="ArgumentNullException">requestがnullの場合</exception>
/// <exception cref="TranslationException">翻訳中にエラーが発生した場合</exception>
public async Task<TranslationResponse> TranslateAsync(
    TranslationRequest request,
    CancellationToken cancellationToken = default)
{
    // 実装...
}
```

### 8.2 コンポーネントドキュメント

主要なコンポーネントには専用のドキュメントを作成してください：

- クラス・モジュールの責任
- 主要な設計決定と理由
- 使用例とサンプルコード
- 既知の制限事項

## まとめ

このガイドラインに従うことで、Baketa翻訳基盤のコード品質を維持し、将来的な拡張性を確保することができます。すべての開発者はこれらのベストプラクティスを守り、コードベースの一貫性を維持するよう努めてください。

ガイドラインは開発の進行に合わせて定期的に見直し、必要に応じて更新していきます。