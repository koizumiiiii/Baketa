# アダプター実装サマリー

## 1. アダプターレイヤーの目的

Baketaプロジェクトでは、クリーンアーキテクチャの原則に従い、プラットフォーム依存コード（Windows固有機能など）とコア機能を分離するためにアダプターパターンを採用しています。アダプターレイヤーは以下の役割を担います：

- プラットフォーム依存インターフェースをコア抽象インターフェースに変換
- 双方向の変換を提供（コア→プラットフォーム、プラットフォーム→コア）
- テスト容易性の向上とモックによる置き換えの実現
- 将来的な他プラットフォーム拡張の可能性の確保

## 2. 実装されたアダプター

現在、以下のアダプターが実装されています：

### 2.1 画像アダプター (WindowsImageAdapter)

- **主要インターフェース**: `IWindowsImageAdapter`
- **変換対象**: `IWindowsImage` ⇔ `IImage`/`IAdvancedImage`
- **主要機能**:
  - 画像オブジェクト間の変換
  - メモリ効率の良い変換処理
  - リソース管理の適切なハンドリング

```csharp
// 実装例
public class WindowsImageAdapter : IWindowsImageAdapter, IDisposable
{
    // 基本的な変換メソッド
    public IImage ToImage(IWindowsImage windowsImage)
    public IAdvancedImage ToAdvancedImage(IWindowsImage windowsImage)
    public async Task<IWindowsImage> FromImageAsync(IImage image)
    public async Task<IWindowsImage> FromAdvancedImageAsync(IAdvancedImage advancedImage)
    
    // ユーティリティメソッド
    public async Task<IAdvancedImage> CreateAdvancedImageFromBytesAsync(byte[] imageData)
    public async Task<IAdvancedImage> CreateAdvancedImageFromFileAsync(string filePath)
}
```

### 2.2 キャプチャアダプター (CaptureAdapter)

- **主要インターフェース**: `ICaptureService`, `IScreenCapturer`
- **変換対象**: Windows GDIキャプチャ機能 ⇔ プラットフォーム非依存キャプチャインターフェース
- **主要機能**:
  - 画面キャプチャ機能
  - 差分検出最適化
  - キャプチャオプション管理

```csharp
// 実装例
public class CaptureAdapter : ICaptureService, IDisposable
{
    // キャプチャメソッド
    public async Task<IImage> CaptureScreenAsync()
    public async Task<IImage> CaptureRegionAsync(Rectangle region)
    public async Task<IImage> CaptureWindowAsync(IntPtr windowHandle)
    public async Task<IImage> CaptureClientAreaAsync(IntPtr windowHandle)
    
    // オプション管理
    public void SetCaptureOptions(CaptureOptions options)
    public CaptureOptions GetCaptureOptions()
    
    // 差分検出
    private bool HasSignificantDifference(IImage previous, IImage current)
}
```

### 2.3 ウィンドウマネージャーアダプター (WindowManagerAdapter)

- **主要インターフェース**: `IWindowManager`
- **変換対象**: Win32 APIウィンドウ管理 ⇔ プラットフォーム非依存ウィンドウ管理
- **主要機能**:
  - ウィンドウ列挙
  - ウィンドウ情報取得（サイズ、タイトル等）
  - ゲームウィンドウ検出

```csharp
// 実装例
public class WindowManagerAdapter : IWindowManager, IDisposable
{
    // ウィンドウ操作
    public IntPtr GetActiveWindowHandle()
    public IntPtr FindWindowByTitle(string title)
    public IntPtr FindWindowByClass(string className)
    
    // ウィンドウ情報
    public Rectangle GetWindowBounds(IntPtr handle)
    public Rectangle GetClientBounds(IntPtr handle)
    public string GetWindowTitle(IntPtr handle)
    
    // ゲームウィンドウ関連
    public IntPtr FindGameWindow(string gameTitle)
    public WindowType GetWindowType(IntPtr handle)
}
```

## 3. 実装における共通パターン

### 3.1 リソース管理パターン

各アダプターは `IDisposable` を実装し、アンマネージドリソースを適切に解放します：

```csharp
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
            // マネージドリソースの解放
        }
        
        // アンマネージドリソースの解放
        _disposed = true;
    }
}
```

### 3.2 アダプター生成パターン

アダプターはDIコンテナを通じて注入されます：

```csharp
// モジュール登録
public class InfrastructureModule : IServiceModule
{
    public void RegisterServices(IServiceCollection services)
    {
        // プラットフォーム依存実装の登録
        services.AddSingleton<IWindowsImageAdapter, WindowsImageAdapter>();
        services.AddSingleton<ICaptureService, CaptureAdapter>();
        services.AddSingleton<IWindowManager, WindowManagerAdapter>();
    }
}
```

## 4. 今後の拡張ポイント

現在のアダプター実装は、Windows向けに最適化されていますが、将来的には以下の拡張が考えられます：

1. **他プラットフォーム用アダプター**: Linux, macOS向けアダプター実装
2. **パフォーマンス最適化**: さらなるメモリ効率と処理速度の改善
3. **高度な機能連携**: 画像処理とキャプチャの統合的な最適化

## 5. 関連ドキュメント

- [アダプター実装のベストプラクティス](./adapter-implementation-best-practices.md)
- [アーキテクチャ改善計画](../improved-architecture.md)
- [プラットフォーム抽象化](../platform/platform-abstraction.md)
