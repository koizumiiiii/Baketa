# セマフォデッドロック問題の修正方針相談

## 現状の問題

### 1. 発見された根本原因
- `StreamingTranslationService.cs`のProcessChunkAsyncメソッドにおいて、セマフォ取得タイムアウト（60秒）時にearly returnしているが、finally句での`semaphore.Release()`に到達しない
- これによりセマフォのカウントが永続的に減少し、デッドロックが発生

### 2. 問題箇所のコード構造
```csharp
try 
{
    await semaphore.WaitAsync(semaphoreCts.Token).ConfigureAwait(false);
    // 正常処理...
}
catch (OperationCanceledException) when (semaphoreTimeout.Token.IsCancellationRequested)
{
    // タイムアウト時の処理
    for (int j = 0; j < chunk.Texts.Count; j++)
    {
        results[chunk.StartIndex + j] = "[セマフォ取得タイムアウト]";
        onChunkCompleted?.Invoke(chunk.StartIndex + j, results[chunk.StartIndex + j]);
    }
    return; // 🔥 CRITICAL: ここでearly returnするとfinally句に到達しない
}
finally
{
    semaphore.Release(); // 🚨 early return時には実行されない
}
```

### 3. BatchOcrProcessor.csでの類似問題
- 並列OCR処理でSemaphoreSlimを使用しているが、ROI画像保存が実行されていない
- セマフォデッドロックにより並列タイル処理が完了していない

## 検討中の修正方針

### 案1: finally句前でのリリース
- catch句内でもsemaphore.Release()を明示的に実行
- finally句は冗長になるが、安全性を確保

### 案2: using patternの活用
- SemaphoreSlimWrapper的なクラスでIDisposableパターンを実装
- usingステートメントで自動リリースを保証

### 案3: 堅牢なセマフォ管理パターン
- セマフォ取得・リリースを確実にペアで実行するヘルパークラス
- リソース管理の標準化

## 質問
1. どの修正方針が最も堅牢で保守しやすいか？
2. Baketa全体で統一すべきセマフォ管理パターンはあるか？
3. 並列処理でのリソース管理のベストプラクティスは？
4. 他の潜在的なリソースリーク箇所の確認方法は？

技術的観点からの推奨修正方針をお聞かせください。