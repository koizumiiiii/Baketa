# Windows相互運用ガイドライン

このドキュメントでは、Baketaプロジェクトにおける Windows APIとの相互運用に関する標準規約を定義します。

> **重要**: Baketaプロジェクトは Windows専用アプリケーションとして開発を継続し、Linux/macOSへのクロスプラットフォーム対応は行いません。

## 1. Windows依存性の適切な分離

### 1.1 プロジェクト構造

Windows固有のコードを明確に分離します：

```
Baketa.Core/                  # プラットフォーム非依存のコア実装
Baketa.Infrastructure/        # インフラストラクチャ層（Windows実装含む）
  └── Platform/
      └── Windows/         # Windows固有実装
Baketa.Application/           # アプリケーションサービス
Baketa.UI/                    # UI層
```

### 1.2 インターフェースと実装の分離

インターフェース定義と実装を明確に分け、Windows固有の実装をインターフェースの背後に隠します。

```csharp
// コア層のインターフェース
public interface ICaptureService
{
    Task<IImage> CaptureScreenAsync();
}

// Windows実装
[SupportedOSPlatform("windows")]
public class WindowsCaptureService : ICaptureService
{
    // Windows固有の実装
}
```

### 1.3 アダプターパターンの使用

Windowsネイティブ型と抽象インターフェース間のアダプターを実装します。

## 2. P/Invokeのベストプラクティス

### 2.1 LibraryImportとDllImportの選択

.NET 7以降では、新しいソース生成P/Invoke（LibraryImport属性）を活用します。

```csharp
// .NET 7以降で推奨
[LibraryImport("user32.dll")]
[return: MarshalAs(UnmanagedType.Bool)]
private static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
```

### 2.2 ネイティブ構造体の定義

P/Invokeで使用する構造体は、メモリレイアウトを明示的に指定します。

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}
```

### 2.3 P/Invoke呼び出しの抽象化

P/Invoke呼び出しは専用のラッパークラスにカプセル化し、直接の呼び出しを避けます。

## 3. COM相互運用

### 3.1 COMオブジェクトの生成と解放

COMオブジェクトを適切に生成・解放します。

```csharp
object comObject = Activator.CreateInstance(comType);
try
{
    // COMオブジェクトの使用
}
finally
{
    // 解放
    if (comObject != null)
    {
        Marshal.ReleaseComObject(comObject);
    }
}
```

### 3.2 COMラッパーの分離

COMインターフェースのラッパーを専用クラスとして実装し、COMの詳細をアプリケーションから隠します。

## 4. ファクトリパターンの活用

Windows固有のオブジェクト生成にはファクトリパターンを使用します。

```csharp
// Windows画像ファクトリインターフェース
public interface IWindowsImageFactory
{
    Task<IWindowsImage> CreateFromFileAsync(string path);
    Task<IWindowsImage> CreateFromBytesAsync(byte[] data);
}

// 実装
public class WindowsImageFactory : IWindowsImageFactory
{
    // 実装詳細
}
```

## 5. セキュリティの考慮事項

ネイティブコードの呼び出しに関するセキュリティリスクを軽減するプラクティス：

1. **最小権限の原則**: 必要最小限のAPI機能のみ使用する
2. **入力の検証**: ネイティブ関数に渡す前にすべての入力を検証する
3. **バッファオーバーフローの防止**: 文字列や配列のバッファサイズを常に管理する
4. **リソースリーク防止**: リソースを常に明示的に解放する
5. **エラー処理**: すべてのネイティブ呼び出しのエラーを適切に処理する

## 6. テストとトラブルシューティング

### 6.1 Windows固有コードのテスト戦略

1. **インターフェース抽象化**: Windows固有コードをインターフェースの背後に隠す
2. **モック実装**: テスト用のモック実装を提供する
3. **ファクトリのオーバーライド**: テスト時にファクトリを差し替える機能を提供する

詳細なコード例やユースケースについては、開発チームに問い合わせてください。