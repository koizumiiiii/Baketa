# Issue #198: 初回起動時ローディングUX改善 - 詳細設計書

## 1. 問題の背景

### 問題1: 解凍処理の進捗が見えない
- ダウンロード完了後、665MBのZIPファイルを解凍（展開後1GB超）
- 解凍処理は1分以上かかるが、UIには「ダウンロード完了」と表示されたまま
- ユーザーから見ると「100%になったのにフリーズした」ように見える

### 問題2: 解凍中にサーバー監視が誤動作
- ServerManagerHostedService と PythonServerHealthMonitor はIHostedServiceとして登録
- アプリ起動時に自動でStartAsync()が呼ばれ、ダウンロード・解凍と並行して動作
- 解凍処理中のディスクI/O高負荷でヘルスチェックがタイムアウトし誤判定

## 2. 修正方針

### 修正1: 解凍処理中のUI進捗表示

**変更ファイル:** `Baketa.Infrastructure/Services/Setup/ComponentDownloadService.cs`

**変更内容:**
1. ReportProgressメソッドにstatusMessageパラメータを追加
2. ExtractZipAsyncの前に「展開中...」メッセージをUIに通知
3. 解凍完了後に最終完了を通知

```csharp
// ComponentDownloadService.cs の修正箇所 (行148付近)

// ダウンロード完了後、チェックサム検証後
await DownloadFileWithProgressAsync(component, tempZipPath, cancellationToken);

// チェックサム検証
if (!string.IsNullOrEmpty(component.Checksum))
{
    var actualChecksum = await ComputeChecksumAsync(tempZipPath, cancellationToken);
    // ...
}

// ★追加: 解凍開始を通知（ダウンロード完了だが、まだ展開中）
ReportProgress(component, component.ExpectedSizeBytes, component.ExpectedSizeBytes, 0,
    isCompleted: false,  // まだ完了ではない
    statusMessage: "ファイルを展開しています... (数分かかる場合があります)");

// 解凍処理
await ExtractZipAsync(tempZipPath, component.LocalPath, cancellationToken);

// ★変更: 解凍完了後に最終完了を通知
ReportProgress(component, component.ExpectedSizeBytes, component.ExpectedSizeBytes, 0,
    isCompleted: true,
    statusMessage: null);  // 完了時はメッセージなし
```

**変更ファイル:** `Baketa.Core/Abstractions/Services/ComponentDownloadProgressEventArgs.cs`

```csharp
public class ComponentDownloadProgressEventArgs : EventArgs
{
    // 既存プロパティ...
    public ComponentInfo Component { get; init; } = default!;
    public long BytesReceived { get; init; }
    public long TotalBytes { get; init; }
    public double SpeedBytesPerSecond { get; init; }
    public bool IsCompleted { get; init; }
    public string? ErrorMessage { get; init; }

    // ★追加: 状態メッセージ（展開中など）
    public string? StatusMessage { get; init; }

    // 計算プロパティ
    public double PercentComplete => TotalBytes > 0 ? (double)BytesReceived / TotalBytes * 100 : 0;
    public TimeSpan? EstimatedTimeRemaining => ...;
}
```

**変更ファイル:** `Baketa.Application/Services/ApplicationInitializer.cs`

```csharp
// FormatDownloadMessage メソッドの修正 (行399-420)
private static string FormatDownloadMessage(ComponentDownloadProgressEventArgs e)
{
    // ★追加: StatusMessageがあればそれを優先表示
    if (!string.IsNullOrEmpty(e.StatusMessage))
    {
        return e.StatusMessage;
    }

    if (e.IsCompleted)
    {
        return $"{e.Component.DisplayName} のインストール完了";  // 「ダウンロード完了」→「インストール完了」
    }
    // 以下既存ロジック...
}
```

### 修正2: 初期化完了シグナルの導入

**新規ファイル:** `Baketa.Core/Abstractions/Services/IInitializationCompletionSignal.cs`

```csharp
namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// アプリケーション初期化完了を通知するシグナル
/// コンポーネントダウンロード・解凍が完了するまで翻訳サーバー起動を遅延させる
/// </summary>
public interface IInitializationCompletionSignal
{
    /// <summary>
    /// 初期化完了を待機するTask
    /// </summary>
    Task WaitForCompletionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 初期化完了を通知
    /// </summary>
    void SignalCompletion();

    /// <summary>
    /// 初期化が完了したかどうか
    /// </summary>
    bool IsCompleted { get; }
}
```

**新規ファイル:** `Baketa.Application/Services/InitializationCompletionSignal.cs`

```csharp
namespace Baketa.Application.Services;

public class InitializationCompletionSignal : IInitializationCompletionSignal
{
    private readonly TaskCompletionSource _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _isCompleted;

    public bool IsCompleted => _isCompleted;

    public Task WaitForCompletionAsync(CancellationToken cancellationToken = default)
    {
        if (_isCompleted) return Task.CompletedTask;

        return cancellationToken.CanBeCanceled
            ? WaitWithCancellationAsync(cancellationToken)
            : _completionSource.Task;
    }

    private async Task WaitWithCancellationAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource();
        await using var registration = cancellationToken.Register(() => tcs.TrySetCanceled());
        await Task.WhenAny(_completionSource.Task, tcs.Task);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void SignalCompletion()
    {
        _isCompleted = true;
        _completionSource.TrySetResult();
    }
}
```

**変更ファイル:** `Baketa.Application/Services/ApplicationInitializer.cs`

```csharp
public class ApplicationInitializer : ILoadingScreenInitializer
{
    private readonly IInitializationCompletionSignal _completionSignal;

    public ApplicationInitializer(
        // 既存パラメータ...
        IInitializationCompletionSignal completionSignal)  // ★追加
    {
        _completionSignal = completionSignal;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Step 0: コンポーネントダウンロード
            await ExecuteStepAsync("download_components", ...);

            // ★追加: ダウンロード・解凍完了を通知
            // これにより ServerManagerHostedService が翻訳サーバー起動を開始できる
            _completionSignal.SignalCompletion();

            // Step 0.5以降の処理...
        }
        catch
        {
            // エラー時も完了通知（サーバー起動は試みる）
            _completionSignal.SignalCompletion();
            throw;
        }
    }
}
```

**変更ファイル:** `Baketa.Infrastructure/Translation/Services/ServerManagerHostedService.cs`

```csharp
public sealed class ServerManagerHostedService : IHostedService
{
    private readonly IInitializationCompletionSignal _initSignal;  // ★追加

    public ServerManagerHostedService(
        IPythonServerManager serverManager,
        GrpcPortProvider portProvider,
        ILogger<ServerManagerHostedService> logger,
        IInitializationCompletionSignal initSignal)  // ★追加
    {
        _initSignal = initSignal;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🚀 [HOSTED_SERVICE] 初期化完了を待機中...");

        _ = Task.Run(async () =>
        {
            try
            {
                // ★追加: 初期化完了を待機（ダウンロード・解凍が終わるまで）
                await _initSignal.WaitForCompletionAsync(cancellationToken);

                _logger.LogInformation("🔄 [HOSTED_SERVICE] 初期化完了 - Python翻訳サーバー起動開始");

                // 既存のサーバー起動処理...
                var serverInfo = await _serverManager.StartServerAsync("grpc-all");
                _portProvider.SetPort(serverInfo.Port);
                _serverManager.InitializeHealthCheckTimer();
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("⚠️ [HOSTED_SERVICE] 起動がキャンセルされました");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [HOSTED_SERVICE] Python翻訳サーバー起動失敗");
                _portProvider.SetException(ex);
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }
}
```

**変更ファイル:** `Baketa.Application/DI/Modules/ApplicationModule.cs`

```csharp
// DI登録追加
services.AddSingleton<IInitializationCompletionSignal, InitializationCompletionSignal>();
```

## 3. シーケンス図

```
アプリ起動
   │
   ├──→ DIコンテナ初期化
   │       │
   │       ├──→ InitializationCompletionSignal (Singleton)
   │       ├──→ ApplicationInitializer
   │       └──→ ServerManagerHostedService
   │
   ├──→ IHostedService.StartAsync() 呼び出し
   │       │
   │       └──→ ServerManagerHostedService.StartAsync()
   │               │
   │               └──→ _initSignal.WaitForCompletionAsync() [待機開始]
   │
   ├──→ LoadingViewModel → ApplicationInitializer.InitializeAsync()
   │       │
   │       ├──→ Step0: DownloadMissingComponentsAsync()
   │       │       │
   │       │       ├──→ ダウンロード (5分)
   │       │       │       └──→ UI: "○○: 100MB / 665MB (1.9MB/s)"
   │       │       │
   │       │       ├──→ ReportProgress(statusMessage: "展開中...")  ★新規
   │       │       │       └──→ UI: "ファイルを展開しています..."
   │       │       │
   │       │       └──→ ExtractZipAsync (1分)
   │       │               └──→ ReportProgress(isCompleted: true)
   │       │                       └──→ UI: "○○のインストール完了"
   │       │
   │       └──→ _completionSignal.SignalCompletion()  ★新規
   │               │
   │               └──→ ServerManagerHostedService [待機解除]
   │                       │
   │                       └──→ Python翻訳サーバー起動
   │                               └──→ ヘルスチェック開始
   │
   └──→ メイン画面表示
```

## 4. リスク分析

| リスク | 影響度 | 対策 |
|--------|--------|------|
| TaskCompletionSourceのメモリリーク | 低 | シングルトンなので1インスタンスのみ |
| 初期化が永遠に完了しないケース | 中 | try-catchでエラー時もSignalCompletion()を呼ぶ |
| キャンセルトークンの伝播漏れ | 低 | WaitWithCancellationAsync()で対応済み |
| 既存テストへの影響 | 中 | DIモック追加が必要 |

## 5. テスト計画

1. **単体テスト:**
   - InitializationCompletionSignal の動作テスト
   - FormatDownloadMessage の StatusMessage 対応テスト

2. **統合テスト:**
   - コンポーネントダウンロード→解凍→サーバー起動の順序テスト
   - キャンセル時の動作テスト

3. **E2Eテスト:**
   - 初回起動時のUI表示確認
   - 解凍中のメッセージ表示確認
