# アダプター実装のベストプラクティス

## 概要

アダプターレイヤーは、プラットフォーム依存コード（Windows固有実装など）とプラットフォーム非依存のコア機能との間の橋渡しを担います。このドキュメントでは、Baketaプロジェクトにおけるアダプター実装の推奨プラクティスをまとめています。

## ベストプラクティス

### 1. Null参照の安全性確保

```csharp
// 推奨: パラメータにNull許容性を明示
public IImage ConvertToImage(IWindowsImage? windowsImage)
{
    ArgumentNullException.ThrowIfNull(windowsImage);
    // 変換処理
}

// 推奨: C# 8.0以降のNull許容注釈を活用
public void ProcessImage(IImage image!)
{
    // image はNull非許容として処理
}

// 推奨: Null合体演算子の活用
var imageFormat = format ?? ImageFormat.Png;
```

### 2. リソース管理の最適化

```csharp
// 推奨: 簡略化されたusing宣言を使用
using var bitmap = new Bitmap(width, height);
// 処理後、スコープを抜けると自動的にDispose

// 推奨: Try-Finallyによる確実なリソース解放
var images = new IImage[10];
try
{
    // リソースを使用する処理
}
finally
{
    foreach (var img in images)
    {
        img?.Dispose();
    }
}

// 推奨: IDisposableの適切な実装
public void Dispose()
{
    Dispose(true);
    GC.SuppressFinalize(this);
}

protected virtual void Dispose(bool disposing)
{
    if (!_disposed)
    {
        if (disposing)
        {
            _managedResource?.Dispose();
        }
        
        _disposed = true;
    }
}
```

### 3. 命名規則と構造

```csharp
// インターフェース
namespace Baketa.Core.Abstractions.Platform.Windows
{
    public interface IWindowsImageAdapter
    {
        IImage ToImage(IWindowsImage windowsImage);
        Task<IWindowsImage> FromImageAsync(IImage image);
    }
}

// 実装
namespace Baketa.Infrastructure.Platform.Adapters
{
    public class WindowsImageAdapter : IWindowsImageAdapter
    {
        // 実装
    }
}
```

### 4. 非同期プログラミング

```csharp
// 推奨: ConfigureAwaitを使用して同期コンテキストの伝播を防止
public async Task<byte[]> ProcessImageAsync(IImage image)
{
    var result = await image.ToByteArrayAsync().ConfigureAwait(false);
    return result;
}

// 推奨: 非同期メソッドの命名
public async Task<IImage> CreateImageFromFileAsync(string path)
{
    // 実装
}
```

### 5. コード品質とメンテナンス性

```csharp
// フィールドの読み取り専用化
private readonly ILogger _logger;
private readonly IWindowManager _windowManager;

// 不要な変数の削除または特殊変数名の使用
// 値を使用しない場合
_ = await image.ToByteArrayAsync();
```

## アダプターテスト実装のポイント

1. **リソース管理のテスト**: `using`ステートメントによる適切なリソース解放を確認
2. **Null参照処理のテスト**: `null`引数に対する適切な例外発生をテスト
3. **変換の正確性テスト**: 変換前後でプロパティ値が保持されていることを確認
4. **エラーハンドリングのテスト**: 異常系シナリオでの動作を確認

## 関連ドキュメント

- [アーキテクチャ概要](../improved-architecture.md)
- [アダプター実装サマリー](./adapter-implementation-summary.md)
- [プラットフォーム抽象化](../platform/platform-abstraction.md)
