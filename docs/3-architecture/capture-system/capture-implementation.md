# キャプチャシステム実装ガイド

*最終更新日: 2025年4月25日*

## 1. 概要

このドキュメントでは、Baketaプロジェクトのキャプチャサブシステムの実装方法について説明します。キャプチャサブシステムは、ゲーム画面をリアルタイムでキャプチャし、OCR処理のための画像を提供する重要なコンポーネントです。

## 2. キャプチャシステムのアーキテクチャ

キャプチャシステムは以下のコンポーネントで構成されています：

1. **キャプチャインターフェース**: 抽象化されたキャプチャ機能を定義
2. **キャプチャ実装**: 複数のキャプチャ方法（GDI, Direct3D等）
3. **差分検出**: 画面変更の効率的な検出
4. **リソース管理**: GCプレッシャーを最小限に抑えるメモリ管理

### 2.1 レイヤー構造

```
Baketa.Core/
├── Interfaces/
│   └── Platform/
│       └── IScreenCapturer.cs   # 基本キャプチャインターフェース

Baketa.Infrastructure.Platform/
├── Windows/
│   ├── Capture/
│   │   ├── IGdiScreenCapturer.cs  # GDI特化キャプチャインターフェース
│   │   ├── GdiScreenCapturer.cs   # GDIキャプチャ実装
│   │   └── SafeHandles.cs         # リソース管理
│   └── NativeMethods/
│       ├── User32Methods.cs     # User32 API定義
│       └── Gdi32Methods.cs      # GDI32 API定義

Baketa.Infrastructure/
└── Capture/
    ├── CaptureService.cs          # キャプチャサービス
    └── DifferenceDetector.cs      # 差分検出アルゴリズム
```

## 3. GDIベースのキャプチャ実装

Windows Graphics Device Interface (GDI) を使用したキャプチャ実装は、安定性と互換性に優れています。多くのゲームタイトルとの互換性があり、基本的なキャプチャ方法として実装されています。

### 3.1 インターフェース設計

```csharp
public interface IGdiScreenCapturer : IDisposable
{
    /// <summary>
    /// プライマリスクリーン全体をキャプチャします
    /// </summary>
    /// <returns>キャプチャした画像</returns>
    Task<IWindowsImage> CaptureScreenAsync();
    
    /// <summary>
    /// 指定したウィンドウをキャプチャします
    /// </summary>
    /// <param name="hWnd">ウィンドウハンドル</param>
    /// <returns>キャプチャした画像</returns>
    Task<IWindowsImage> CaptureWindowAsync(IntPtr hWnd);
    
    /// <summary>
    /// 指定した領域をキャプチャします
    /// </summary>
    /// <param name="region">キャプチャする領域</param>
    /// <returns>キャプチャした画像</returns>
    Task<IWindowsImage> CaptureRegionAsync(Rectangle region);
}
```

### 3.2 使用するWin32 API

GDIキャプチャ実装では、以下のWin32 APIを使用します：

1. **BitBlt**: 画面領域のキャプチャに使用
2. **PrintWindow**: ウィンドウ全体のキャプチャに使用
3. **GetDC/ReleaseDC**: デバイスコンテキストの取得と解放
4. **CreateCompatibleDC/DeleteDC**: メモリDCの作成と解放
5. **CreateCompatibleBitmap/DeleteObject**: 互換ビットマップの作成と解放

### 3.3 実装の要点

#### 3.3.1 リソース管理

GDIリソースは適切に管理する必要があります。以下のような`SafeHandle`派生クラスを使用して確実にリソースを解放します：

```csharp
internal sealed class DeviceContextHandle : SafeHandle
{
    public DeviceContextHandle(IntPtr hDC) : base(IntPtr.Zero, true)
    {
        SetHandle(hDC);
    }
    
    public override bool IsInvalid => handle == IntPtr.Zero;
    
    protected override bool ReleaseHandle()
    {
        if (handle != IntPtr.Zero)
        {
            int result = User32Methods.ReleaseDC(IntPtr.Zero, handle);
            return result != 0;
        }
        return true;
    }
}

internal sealed class BitmapHandle : SafeHandle
{
    public BitmapHandle(IntPtr hBitmap) : base(IntPtr.Zero, true)
    {
        SetHandle(hBitmap);
    }
    
    public override bool IsInvalid => handle == IntPtr.Zero;
    
    protected override bool ReleaseHandle()
    {
        if (handle != IntPtr.Zero)
        {
            Gdi32Methods.DeleteObject(handle);
        }
        return true;
    }
}
```

#### 3.3.2 パフォーマンス最適化

キャプチャのパフォーマンスを最適化するために、以下の手法を適用します：

1. **メモリDCの再利用**: 同じサイズのキャプチャでメモリDCとビットマップを再利用
2. **非同期処理**: UIスレッドをブロックしないようにTask.Runで実行
3. **エラー処理**: 適切な例外とフォールバックメカニズムの実装
4. **ロギング最適化**: LoggerMessageパターンを使用した効率的なロギング

```csharp
private void EnsureMemoryDC(IntPtr hdcScreen, int width, int height)
{
    // 既存のメモリDCが再利用可能か確認
    if (_hdcMemory != IntPtr.Zero && _lastWidth == width && _lastHeight == height)
    {
        return;
    }
    
    // 既存のリソースを解放
    CleanupResources();
    
    // 新しいメモリDC作成
    _hdcMemory = Gdi32Methods.CreateCompatibleDC(hdcScreen);
    _hBitmap = Gdi32Methods.CreateCompatibleBitmap(hdcScreen, width, height);
    
    if (_hdcMemory == IntPtr.Zero || _hBitmap == IntPtr.Zero)
    {
        CleanupResources();
        throw new InvalidOperationException("メモリDCの作成に失敗しました");
    }
    
    // メモリDCにビットマップを選択
    Gdi32Methods.SelectObject(_hdcMemory, _hBitmap);
    
    _lastWidth = width;
    _lastHeight = height;
}
```

#### 3.3.3 セキュリティ対策

DLLハイジャック対策として、以下の措置を講じています：

1. **明示的なシステムディレクトリパス**: 完全なパスを定数として定義
   ```csharp
   private const string USER32_DLL = @"C:\Windows\System32\user32.dll";
   private const string GDI32_DLL = @"C:\Windows\System32\gdi32.dll";
   ```

2. **DllImport属性の最適化**: 追加パラメータによる安全性向上
   ```csharp
   [DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
   ```

3. **入力検証**: すべてのメソッドでパラメータの検証を徹底
   ```csharp
   if (hWnd == IntPtr.Zero)
       throw new ArgumentException("ウィンドウハンドルが無効です", nameof(hWnd));
   ```

## 4. 使用方法

### 4.1 依存性注入の設定

キャプチャサービスを利用するには、依存性注入コンテナに登録します：

```csharp
public static IServiceCollection AddCaptureServices(this IServiceCollection services)
{
    // GDIキャプチャ実装を登録
    services.AddSingleton<IGdiScreenCapturer, GdiScreenCapturer>();
    
    // 他のキャプチャ実装も必要に応じて登録
    
    return services;
}
```

### 4.2 実装例

以下はキャプチャサービスの使用例です：

```csharp
public class OcrService
{
    private readonly IGdiScreenCapturer _capturer;
    private readonly IOcrEngine _ocrEngine;
    
    public OcrService(IGdiScreenCapturer capturer, IOcrEngine ocrEngine)
    {
        _capturer = capturer;
        _ocrEngine = ocrEngine;
    }
    
    public async Task<string> RecognizeWindowTextAsync(IntPtr windowHandle)
    {
        // ウィンドウをキャプチャ
        var image = await _capturer.CaptureWindowAsync(windowHandle);
        
        // キャプチャした画像をOCR処理
        var result = await _ocrEngine.RecognizeAsync(image);
        
        return result.Text;
    }
}
```

## 5. テスト方法

キャプチャ実装のテストには以下のアプローチを取ります：

1. **モックテスト**: インターフェースのモック実装を使用した単体テスト
2. **統合テスト**: 実際のウィンドウでの動作確認
3. **パフォーマンステスト**: 連続キャプチャ時のメモリ消費とCPU使用率の計測

### 5.1 単体テストの例

```csharp
[Fact]
public async Task CaptureWindowAsync_NullWindowHandle_ThrowsArgumentException()
{
    // Arrange
    var capturer = new GdiScreenCapturer(_mockImageFactory.Object);
    
    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(() => 
        capturer.CaptureWindowAsync(IntPtr.Zero));
}
```

## 6. 今後の改善計画

キャプチャシステムの今後の改善点として以下を計画しています：

1. **Direct3Dキャプチャの実装**: 一部の3Dゲームでより適切に動作するキャプチャ方法
2. **自動最適化**: ゲームプロファイルに基づく最適なキャプチャ方法の自動選択
3. **マルチモニター対応**: 複数モニター環境での正確なキャプチャ
4. **ハードウェアアクセラレーション**: GPUを活用したキャプチャパフォーマンスの向上

## 7. 関連ドキュメント

- [プラットフォーム抽象化レイヤー](../platform/platform-abstraction.md)
- [OCR前処理システム](../ocr-system/preprocessing/index.md)
- [パフォーマンス最適化ガイドライン](../../2-development/coding-standards/performance.md)
