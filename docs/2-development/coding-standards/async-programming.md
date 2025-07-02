# 非同期プログラミングガイドライン

## 1. はじめに

このドキュメントでは、Baketaプロジェクトにおける非同期プログラミングのベストプラクティスを定義しています。適切な非同期プログラミングパターンを実践することで、パフォーマンスの向上とデッドロックの回避を実現できます。

## 2. ConfigureAwait(false)の使用

### 2.1 基本原則

非同期メソッド内で`await`を使用する際は、常に`.ConfigureAwait(false)`を追加することを推奨します。これにより、同期コンテキストへの復帰を避け、パフォーマンスの向上とデッドロックの可能性を減少させます。

```csharp
// 非推奨
var result = await SomeAsyncMethod();

// 推奨
var result = await SomeAsyncMethod().ConfigureAwait(false);
```

### 2.2 適用例外

UI関連コードなど、同期コンテキストへの復帰が必要な場合は例外とします。ただし、ライブラリコードでは原則として常に`.ConfigureAwait(false)`を使用してください。

### 2.3 実装時の注意点

非同期メソッドを実装する際の注意点：

1. メソッド名には必ず`Async`サフィックスを付ける
2. 戻り値は`Task`または`Task<T>`を使用する
3. `async void`は使用しない
4. `await`の各呼び出しに`.ConfigureAwait(false)`を追加する

```csharp
// 正しい実装例
public async Task<int> CalculateValueAsync(string input)
{
    var data = await GetDataAsync(input).ConfigureAwait(false);
    var processed = await ProcessDataAsync(data).ConfigureAwait(false);
    return processed.Length;
}
```

## 3. よくある間違いとその修正

### 3.1 拡張メソッドによる簡略化の落とし穴

```csharp
// 間違った拡張メソッド設計
public static ConfiguredTaskAwaitable<T> CAF<T>(this Task<T> task)
{
    return task.ConfigureAwait(false);
}

// 問題点: Task<T>とConfiguredTaskAwaitable<T>は互換性がない
// 以下はコンパイルエラーになる
Task<int> result = SomeMethod().CAF(); // エラー
```

このようなアプローチは型の不一致によりコンパイルエラーを引き起こします。直接`.ConfigureAwait(false)`を使用するのが最も安全です。

### 3.2 複数の非同期操作の連鎖

複数の非同期操作を連鎖させる場合、各操作に`.ConfigureAwait(false)`を追加します：

```csharp
// 正しい連鎖
var data = await GetDataAsync().ConfigureAwait(false);
var processed = await ProcessDataAsync(data).ConfigureAwait(false);
var result = await SaveDataAsync(processed).ConfigureAwait(false);
```

## 4. 実装例

### 4.1 正しい実装例

```csharp
public async Task<IAdvancedImage> ProcessImageAsync(IAdvancedImage image)
{
    // 適切なConfigureAwait(false)の使用
    var grayscale = await image.ToGrayscaleAsync().ConfigureAwait(false);
    var binary = await grayscale.ToBinaryAsync(128).ConfigureAwait(false);
    return binary;
}
```

### 4.2 Task.Run()の適切な使用

CPU集約的な処理は`Task.Run()`でラップし、UIスレッドをブロックしないようにします：

```csharp
public Task<byte[]> ProcessLargeDataAsync(byte[] data)
{
    // CPU集約的な処理はTask.Runでラップする
    return Task.Run(() => {
        // 長時間実行される計算処理
        var result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            result[i] = CalculateValue(data[i]);
        }
        return result;
    });
}
```

## 5. デッドロック防止のベストプラクティス

1. 常に`.ConfigureAwait(false)`を使用する
2. 同期的なブロック呼び出し（`.Result`や`.Wait()`）を避ける
3. `async void`メソッドを使用しない
4. `Task.Run()`を適切に使用する

## 6. 参考資料

- [Microsoft - 非同期プログラミングのベストプラクティス](https://docs.microsoft.com/ja-jp/dotnet/standard/asynchronous-programming-patterns/best-practices-for-asynchronous-programming)
- [ConfigureAwait FAQs](https://devblogs.microsoft.com/dotnet/configureawait-faq/)
