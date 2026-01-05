---
description: Baketa.Infrastructure レイヤー固有のルール（外部サービス連携、実装詳細）
globs:
  - "Baketa.Infrastructure/**/*.cs"
  - "Baketa.Infrastructure.Platform/**/*.cs"
---

# Baketa.Infrastructure レイヤールール

## 責務
- Core で定義されたインターフェースの具象実装
- 外部サービス（OCR, 翻訳API, データベース）との連携
- ファイルI/O、ネットワーク通信

## 依存関係
- Core への依存: 許可（インターフェース実装のため）
- Application への依存: 禁止
- UI への依存: 禁止

## 実装ルール

### gRPC クライアント
```csharp
// 推奨: WithWaitForReady で接続待機
var callOptions = new CallOptions()
    .WithWaitForReady(true)
    .WithDeadline(DateTime.UtcNow.AddSeconds(30));
```

### HTTP クライアント
- `IHttpClientFactory` を使用（直接 `new HttpClient()` は禁止）
- タイムアウトを必ず設定
- リトライポリシーを実装（Polly推奨）

### ログ出力
```csharp
// 推奨: 構造化ログ
_logger.LogInformation("処理完了: RequestId={RequestId}, Duration={Duration}ms", requestId, duration);

// 禁止: 文字列補間
_logger.LogInformation($"処理完了: {requestId}"); // NG
```

### デバッグ専用コード
```csharp
#if DEBUG
    _logger.LogInformation("デバッグ情報: {Detail}", detail);
#endif
```

## Platform 固有（Windows）
- P/Invoke 宣言は `NativeWindowsCapture.cs` に集約
- `[DllImport]` より `[LibraryImport]` を優先（.NET 7+）
