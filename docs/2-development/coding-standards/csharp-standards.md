# C#コーディング標準

## 1. パラメータと変数の取り扱い

### 1.1 未使用パラメータの扱い

パラメータが未使用の場合は、アンダースコア接頭辞を使用してディスカードとして明示します。

```csharp
// 非推奨
private static void RegisterServices(IServiceCollection services)
{
    // サービスパラメータは使用されない
}

// 推奨
private static void RegisterServices(IServiceCollection _)
{
    // ディスカードパターン（アンダースコア）で未使用を明示
}
```

複数の未使用パラメータがある場合は、`_1`, `_2` などと区別することもできます。

### 1.2 不要な代入の回避

計算結果が一度しか使用されない場合は、中間変数への代入を避けます。

```csharp
// 非推奨
var moduleNames = string.Join(", ", modules.Select(t => t.Name));
logger.LogDebug("登録されたモジュール: {RegisteredModules}", moduleNames);

// 推奨
logger.LogDebug("登録されたモジュール: {RegisteredModules}", 
    string.Join(", ", modules.Select(t => t.Name)));
```

## 2. モダンC#言語機能の活用

### 2.1 Nullチェックの簡素化

C# 8.0以降の簡潔な構文を使用します。

```csharp
// 非推奨
var serviceProvider = Program.ServiceProvider;
if (serviceProvider == null)
{
    throw new InvalidOperationException("サービスプロバイダーが初期化されていません");
}

// 推奨
var serviceProvider = Program.ServiceProvider 
    ?? throw new InvalidOperationException("サービスプロバイダーが初期化されていません");
```

### 2.2 Null条件演算子の使用

プロパティアクセスやメソッド呼び出しでのnullチェックに使用します。

```csharp
// 非推奨
if (logger != null)
{
    logger.LogInformation("メッセージ");
}

// 推奨
logger?.LogInformation("メッセージ");
```

### 2.3 ラムダ式とローカル関数

単純な匿名メソッドにはラムダ式を使用します。より複雑なロジックはローカル関数を検討します。

```csharp
// ラムダ式（シンプルな場合）
services.AddSingleton<Func<string, ITranslationEngine>>(sp => engineType =>
    engineType switch
    {
        "local" => sp.GetRequiredService<LocalTranslationEngine>(),
        "cloud" => sp.GetRequiredService<CloudTranslationEngine>(),
        _ => throw new ArgumentException($"不明なエンジンタイプ: {engineType}")
    });

// ローカル関数（複雑な場合）
services.AddSingleton<Func<string, ITranslationEngine>>(sp => 
{
    return CreateEngine;

    ITranslationEngine CreateEngine(string engineType)
    {
        // 複雑なロジック...
        return engineType switch
        {
            "local" => sp.GetRequiredService<LocalTranslationEngine>(),
            "cloud" => CreateCloudEngine(),
            _ => throw new ArgumentException($"不明なエンジンタイプ: {engineType}")
        };
    }
});
```

## 3. 依存性注入のベストプラクティス

### 3.1 サービス登録の一貫性

サービス登録は機能ごとにグループ化し、命名規則を一貫させます。

```csharp
// 推奨パターン
private static void RegisterOcrServices(IServiceCollection services)
{
    services.AddSingleton<IOcrEngine, PaddleOcrEngine>();
    services.AddSingleton<IOcrProcessor, DefaultOcrProcessor>();
    // 関連サービスをグループ化
}
```

### 3.2 サービスライフタイムの適切な選択

- **Singleton**: 状態を共有するサービス、スレッドセーフなサービス
- **Scoped**: リクエスト単位で状態を共有するサービス（WebAPIなど）
- **Transient**: 軽量で状態を持たないサービス

```csharp
// 状態を共有する設定サービス
services.AddSingleton<ISettingsService, SettingsService>();

// リクエスト単位のコンテキスト
services.AddScoped<IRequestContext, RequestContext>();

// 軽量なヘルパー
services.AddTransient<ITextFormatter, TextFormatter>();
```

## 4. 非同期プログラミングのガイドライン

### 4.1 async/await パターン

非同期メソッドには常に接尾辞 `Async` を付け、戻り値は `Task` または `Task<T>` を使用します。

```csharp
// 推奨
public async Task<TranslationResult> TranslateAsync(string text)
{
    // 非同期実装
}
```

### 4.2 取り消しトークンの使用

長時間実行される可能性のある操作では、`CancellationToken` をサポートします。

```csharp
public async Task<TranslationResult> TranslateAsync(
    string text, 
    CancellationToken cancellationToken = default)
{
    // トークンを使用した非同期実装
    return await _translationEngine.TranslateAsync(text, cancellationToken);
}
```

## 5. コード分析ルールの遵守

プロジェクトでは.NET Analzyer（CA）とIDE分析による警告を最小限に抑えるよう努めています。

### 5.1 警告レベルの優先度

- **エラー (Error)**: 重大な問題で修正必須
- **警告 (Warning)**: 推奨される修正
- **情報 (Info)**: コーディングスタイルやベストプラクティスの提案
- **無効 (None)**: プロジェクト要件に合わない規則

### 5.2 一般的な分析ルール

| ルールID | 説明 | 対応方法 |
|---------|-----|---------|
| IDE0060 | 未使用パラメータを検出 | アンダースコア (`_`) でディスカード |
| IDE0059 | 不要な値代入を検出 | 中間変数を除去または再利用 |
| IDE0270 | Nullチェックの簡素化 | Null合体演算子 (`??`) を使用 |
| CA1822 | 静的メソッド候補を検出 | 適切な場合は `static` に変更 |
| CA2007 | Task待機に ConfigureAwait を使用 | `await task.ConfigureAwait(false)` |

適切な警告レベルと抑制（必要な場合のみ）を使用して、コードの品質維持とプロジェクト要件のバランスを取ります。
