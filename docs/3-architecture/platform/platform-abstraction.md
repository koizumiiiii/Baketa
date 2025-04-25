# プラットフォーム抽象化レイヤー

*最終更新: 2025年4月18日*

## 1. 概要

このドキュメントでは、Baketaプロジェクトにおけるプラットフォーム抽象化レイヤーの設計と実装方針について説明します。プラットフォーム抽象化レイヤーは、Windows依存のネイティブAPI呼び出しを適切にカプセル化し、アプリケーションのコア機能から分離することを目的としています。

## 2. 設計原則

プラットフォーム抽象化レイヤーの設計には、以下の原則を適用します：

1. **依存関係の方向性**: コアロジックはプラットフォーム実装に依存せず、プラットフォーム実装がコアインターフェースに依存する
2. **適切な抽象化レベル**: 過度な抽象化を避け、実用的なインターフェースを提供する
3. **明確な責任分離**: プラットフォーム特有のコードを明確に分離する
4. **テスト容易性**: モック可能なインターフェースを通じてテスト容易性を向上させる
5. **明示的な依存性**: 暗黙的な依存関係を避け、依存関係を明示的に表現する

## 3. レイヤー構造

### 3.1 コアレイヤー（プラットフォーム非依存インターフェース）

```
Baketa.Core/
├── Interfaces/
│   ├── Image/
│   │   └── IImage.cs            # 画像抽象化基本インターフェース
│   └── Platform/
│       ├── IWindowManager.cs    # ウィンドウ管理インターフェース
│       ├── IScreenCapturer.cs   # 画面キャプチャインターフェース
│       ├── IKeyboardHook.cs     # キーボードフックインターフェース
│       └── INativeWindow.cs     # ネイティブウィンドウインターフェース
```

### 3.2 プラットフォーム実装レイヤー

```
Baketa.Infrastructure.Platform/
├── Abstractions/              # プラットフォーム固有インターフェース
│   └── IWindowsImage.cs       # Windows画像インターフェース
│
├── Windows/                   # Windows実装
│   ├── WindowsManager.cs      # Windows用ウィンドウ管理
│   ├── WindowsCapturer.cs     # Windows用画面キャプチャ
│   ├── WindowsKeyboardHook.cs # Windows用キーボードフック
│   ├── WindowsImage.cs        # Windows画像実装
│   ├── NativeWindow.cs        # Windows用ネイティブウィンドウ
│   └── NativeMethods/         # P/Invoke定義
│       ├── User32Methods.cs   # User32.dll関連P/Invoke
│       ├── Gdi32Methods.cs    # Gdi32.dll関連P/Invoke
│       └── DwmMethods.cs      # DWM API関連P/Invoke
│
└── Adapters/                  # アダプターレイヤー
    ├── PlatformAdapter.cs     # プラットフォームサービスアダプター
    └── WindowsImageAdapter.cs # Windows画像アダプター
```

## 4. 主要インターフェース

### 4.1 IWindowManager

```csharp
namespace Baketa.Core.Interfaces.Platform
{
    /// <summary>
    /// ウィンドウ管理インターフェース
    /// </summary>
    public interface IWindowManager
    {
        /// <summary>
        /// アクティブなウィンドウハンドルを取得
        /// </summary>
        /// <returns>アクティブウィンドウのハンドル</returns>
        IntPtr GetActiveWindowHandle();
        
        /// <summary>
        /// 指定したタイトルを持つウィンドウハンドルを取得
        /// </summary>
        /// <param name="title">ウィンドウタイトル (部分一致)</param>
        /// <returns>一致するウィンドウのハンドル。見つからなければIntPtr.Zero</returns>
        IntPtr FindWindowByTitle(string title);
        
        /// <summary>
        /// 指定したクラス名を持つウィンドウハンドルを取得
        /// </summary>
        /// <param name="className">ウィンドウクラス名</param>
        /// <returns>一致するウィンドウのハンドル。見つからなければIntPtr.Zero</returns>
        IntPtr FindWindowByClass(string className);
        
        /// <summary>
        /// ウィンドウの位置とサイズを取得
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>ウィンドウの位置とサイズを表す Rectangle</returns>
        Rectangle? GetWindowBounds(IntPtr handle);
        
        /// <summary>
        /// ウィンドウのクライアント領域を取得
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>クライアント領域の位置とサイズを表す Rectangle</returns>
        Rectangle? GetClientBounds(IntPtr handle);
        
        /// <summary>
        /// ウィンドウのタイトルを取得
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>ウィンドウタイトル</returns>
        string GetWindowTitle(IntPtr handle);
        
        /// <summary>
        /// ウィンドウが最小化されているか確認
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>最小化されている場合はtrue</returns>
        bool IsMinimized(IntPtr handle);
        
        /// <summary>
        /// ウィンドウが最大化されているか確認
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>最大化されている場合はtrue</returns>
        bool IsMaximized(IntPtr handle);
    }
}
```

### 4.2 IScreenCapturer

```csharp
namespace Baketa.Core.Interfaces.Platform
{
    /// <summary>
    /// 画面キャプチャインターフェース
    /// </summary>
    public interface IScreenCapturer
    {
        /// <summary>
        /// 画面全体をキャプチャ
        /// </summary>
        /// <returns>キャプチャした画像</returns>
        Task<IImage> CaptureScreenAsync();
        
        /// <summary>
        /// 指定した領域をキャプチャ
        /// </summary>
        /// <param name="region">キャプチャする領域</param>
        /// <returns>キャプチャした画像</returns>
        Task<IImage> CaptureRegionAsync(Rectangle region);
        
        /// <summary>
        /// 指定したウィンドウをキャプチャ
        /// </summary>
        /// <param name="windowHandle">ウィンドウハンドル</param>
        /// <returns>キャプチャした画像</returns>
        Task<IImage> CaptureWindowAsync(IntPtr windowHandle);
        
        /// <summary>
        /// 指定したウィンドウのクライアント領域をキャプチャ
        /// </summary>
        /// <param name="windowHandle">ウィンドウハンドル</param>
        /// <returns>キャプチャした画像</returns>
        Task<IImage> CaptureClientAreaAsync(IntPtr windowHandle);
    }
}
```

### 4.3 IKeyboardHook

```csharp
namespace Baketa.Core.Interfaces.Platform
{
    /// <summary>
    /// キーボードフックイベント引数
    /// </summary>
    public class KeyboardHookEventArgs : EventArgs
    {
        /// <summary>
        /// 仮想キーコード
        /// </summary>
        public int VirtualKeyCode { get; }
        
        /// <summary>
        /// キーが押されているかどうか（falseの場合は離された）
        /// </summary>
        public bool IsKeyDown { get; }
        
        /// <summary>
        /// ALTキーが押されているかどうか
        /// </summary>
        public bool IsAltPressed { get; }
        
        /// <summary>
        /// Ctrlキーが押されているかどうか
        /// </summary>
        public bool IsCtrlPressed { get; }
        
        /// <summary>
        /// Shiftキーが押されているかどうか
        /// </summary>
        public bool IsShiftPressed { get; }
        
        /// <summary>
        /// イベントをキャンセルするかどうか（trueの場合、キー入力は処理されない）
        /// </summary>
        public bool Handled { get; set; }
        
        public KeyboardHookEventArgs(int virtualKeyCode, bool isKeyDown, bool isAltPressed, bool isCtrlPressed, bool isShiftPressed)
        {
            VirtualKeyCode = virtualKeyCode;
            IsKeyDown = isKeyDown;
            IsAltPressed = isAltPressed;
            IsCtrlPressed = isCtrlPressed;
            IsShiftPressed = isShiftPressed;
            Handled = false;
        }
    }

    /// <summary>
    /// キーボードフックインターフェース
    /// </summary>
    public interface IKeyboardHook : IDisposable
    {
        /// <summary>
        /// キーボードイベント
        /// </summary>
        event EventHandler<KeyboardHookEventArgs> KeyboardEvent;
        
        /// <summary>
        /// フックを開始
        /// </summary>
        void Start();
        
        /// <summary>
        /// フックを停止
        /// </summary>
        void Stop();
        
        /// <summary>
        /// フックが有効かどうかを取得
        /// </summary>
        bool IsActive { get; }
        
        /// <summary>
        /// ホットキーの登録
        /// </summary>
        /// <param name="id">ホットキー識別子</param>
        /// <param name="modifiers">修飾キー</param>
        /// <param name="virtualKey">仮想キーコード</param>
        /// <returns>登録成功時はtrue</returns>
        bool RegisterHotKey(int id, int modifiers, int virtualKey);
        
        /// <summary>
        /// ホットキーの登録解除
        /// </summary>
        /// <param name="id">ホットキー識別子</param>
        /// <returns>解除成功時はtrue</returns>
        bool UnregisterHotKey(int id);
    }
}
```

## 5. Windows実装例

### 5.1 WindowsManager 実装

```csharp
namespace Baketa.Infrastructure.Platform.Windows
{
    /// <summary>
    /// Windows用ウィンドウ管理クラス
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WindowsManager : IWindowManager
    {
        private readonly ILogger<WindowsManager> _logger;
        
        public WindowsManager(ILogger<WindowsManager> logger)
        {
            _logger = logger;
        }
        
        public IntPtr GetActiveWindowHandle()
        {
            return User32Methods.GetForegroundWindow();
        }
        
        public IntPtr FindWindowByTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
                return IntPtr.Zero;
                
            return User32Methods.FindWindow(null, title);
        }
        
        public IntPtr FindWindowByClass(string className)
        {
            if (string.IsNullOrEmpty(className))
                return IntPtr.Zero;
                
            return User32Methods.FindWindow(className, null);
        }
        
        public Rectangle? GetWindowBounds(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return null;
                
            if (!User32Methods.GetWindowRect(handle, out RECT rect))
            {
                _logger.LogWarning("GetWindowRect失敗: {Handle}", handle);
                return null;
            }
            
            return new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
        
        public Rectangle? GetClientBounds(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return null;
                
            if (!User32Methods.GetClientRect(handle, out RECT rect))
            {
                _logger.LogWarning("GetClientRect失敗: {Handle}", handle);
                return null;
            }
            
            // クライアント座標からスクリーン座標への変換
            var point = new POINT { X = 0, Y = 0 };
            User32Methods.ClientToScreen(handle, ref point);
            
            return new Rectangle(point.X, point.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
        
        public string GetWindowTitle(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return string.Empty;
                
            // ウィンドウタイトルの長さを取得
            int length = User32Methods.GetWindowTextLength(handle);
            if (length == 0)
                return string.Empty;
                
            // バッファを準備
            var builder = new StringBuilder(length + 1);
            int result = User32Methods.GetWindowText(handle, builder, builder.Capacity);
            
            return result > 0 ? builder.ToString() : string.Empty;
        }
        
        public bool IsMinimized(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return false;
                
            return User32Methods.IsIconic(handle);
        }
        
        public bool IsMaximized(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return false;
                
            return User32Methods.IsZoomed(handle);
        }
    }
}
```

### 5.2 P/Invoke定義例

```csharp
namespace Baketa.Infrastructure.Platform.Windows.NativeMethods
{
    /// <summary>
    /// User32.dll P/Invoke定義
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static partial class User32Methods
    {
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
        
        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetForegroundWindow();
        
        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr FindWindow(string? lpClassName, string? lpWindowName);
        
        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        
        [LibraryImport("user32.dll")]
        internal static partial int GetWindowTextLength(IntPtr hWnd);
        
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsIconic(IntPtr hWnd);
        
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsZoomed(IntPtr hWnd);
        
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
        
        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetDC(IntPtr hWnd);
        
        [LibraryImport("user32.dll")]
        internal static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        
        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetDesktopWindow();
        
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool UnregisterHotKey(IntPtr hWnd, int id);
        
        [LibraryImport("user32.dll")]
        internal static partial IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool UnhookWindowsHookEx(IntPtr hhk);
        
        [LibraryImport("user32.dll")]
        internal static partial IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        
        internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    }
}
```

## 6. ネイティブ構造体定義

プラットフォーム固有のネイティブ構造体は明示的なレイアウトを指定します:

```csharp
namespace Baketa.Infrastructure.Platform.Windows.NativeMethods
{
    /// <summary>
    /// RECT構造体
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        
        public override string ToString() => $"[{Left}, {Top}, {Right}, {Bottom}]";
    }
    
    /// <summary>
    /// POINT構造体
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
        
        public override string ToString() => $"({X}, {Y})";
    }
    
    /// <summary>
    /// SIZE構造体
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int Width;
        public int Height;
        
        public override string ToString() => $"{Width}x{Height}";
    }
    
    /// <summary>
    /// BLENDFUNCTION構造体
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }
}
```

## 7. アダプターパターンの実装

コアインターフェース(Baketa.Core.Interfaces)とプラットフォーム固有実装(Baketa.Infrastructure.Platform)の連携を行うアダプターの実装例:

```csharp
namespace Baketa.Infrastructure.Platform.Adapters
{
    /// <summary>
    /// Windowsイメージをコアイメージに変換するアダプター
    /// </summary>
    public class WindowsImageAdapter : IImage
    {
        private readonly IWindowsImage _windowsImage;
        private bool _disposed;
        
        public WindowsImageAdapter(IWindowsImage windowsImage)
        {
            _windowsImage = windowsImage ?? throw new ArgumentNullException(nameof(windowsImage));
        }
        
        public int Width => _windowsImage.Width;
        
        public int Height => _windowsImage.Height;
        
        public IImage Clone()
        {
            var nativeImage = _windowsImage.GetNativeImage();
            var clonedBitmap = new Bitmap(nativeImage);
            var clonedWindowsImage = new WindowsImage(clonedBitmap);
            
            return new WindowsImageAdapter(clonedWindowsImage);
        }
        
        public async Task<byte[]> ToByteArrayAsync()
        {
            using var stream = new MemoryStream();
            var nativeImage = _windowsImage.GetNativeImage();
            nativeImage.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        
        public async Task<IImage> ResizeAsync(int width, int height)
        {
            var nativeImage = _windowsImage.GetNativeImage();
            var resized = new Bitmap(nativeImage, width, height);
            var resizedWindowsImage = new WindowsImage(resized);
            
            return new WindowsImageAdapter(resizedWindowsImage);
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                _windowsImage.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
```

## 8. 依存性注入の設定

```csharp
namespace Baketa.Infrastructure.Platform
{
    public static class PlatformServiceExtensions
    {
        /// <summary>
        /// プラットフォームサービスの登録
        /// </summary>
        public static IServiceCollection AddPlatformServices(this IServiceCollection services)
        {
            // Windows専用アプリケーションのため、直接Windows実装を登録
            services.AddSingleton<IWindowManager, WindowsManager>();
            services.AddSingleton<IScreenCapturer, WindowsCapturer>();
            services.AddSingleton<IKeyboardHook, WindowsKeyboardHook>();
            
            // イメージファクトリ登録
            services.AddSingleton<IWindowsImageFactory, WindowsImageFactory>();
            
            // アダプター登録
            services.AddSingleton<IImageFactory, WindowsImageAdapterFactory>();
            
            return services;
        }
    }
}
```

## 9. プラットフォーム間の例外処理

```csharp
namespace Baketa.Infrastructure.Platform.Exceptions
{
    /// <summary>
    /// プラットフォーム操作例外の基底クラス
    /// </summary>
    public class PlatformOperationException : Exception
    {
        public PlatformOperationException(string message) : base(message)
        {
        }
        
        public PlatformOperationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
    
    /// <summary>
    /// ウィンドウ操作例外
    /// </summary>
    public class WindowOperationException : PlatformOperationException
    {
        public IntPtr WindowHandle { get; }
        
        public WindowOperationException(string message, IntPtr windowHandle) 
            : base(message)
        {
            WindowHandle = windowHandle;
        }
        
        public WindowOperationException(string message, IntPtr windowHandle, Exception innerException) 
            : base(message, innerException)
        {
            WindowHandle = windowHandle;
        }
    }
    
    /// <summary>
    /// キャプチャ例外
    /// </summary>
    public class CaptureException : PlatformOperationException
    {
        public Rectangle? CaptureRegion { get; }
        
        public CaptureException(string message) 
            : base(message)
        {
        }
        
        public CaptureException(string message, Rectangle captureRegion) 
            : base(message)
        {
            CaptureRegion = captureRegion;
        }
        
        public CaptureException(string message, Exception innerException) 
            : base(message, innerException)
        {
        }
    }
}
```

## 10. テスト容易性のための設計

### 10.1 モックのための抽象化

```csharp
// テスト用のモック実装
public class MockWindowManager : IWindowManager
{
    // 実装は特定テストケースに合わせて定義
    // ...
}

public class MockScreenCapturer : IScreenCapturer
{
    private readonly IWindowsImageFactory _imageFactory;
    
    public MockScreenCapturer(IWindowsImageFactory imageFactory)
    {
        _imageFactory = imageFactory;
    }
    
    public Task<IWindowsImage> CaptureScreenAsync()
    {
        // テスト用の固定画像を生成
        var bitmap = new Bitmap(800, 600);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.White);
            g.DrawString("Mock Screen", new Font("Arial", 24), Brushes.Black, 50, 50);
        }
        
        return Task.FromResult<IWindowsImage>(new WindowsImage(bitmap));
    }
    
    // その他のメソッド実装
    // ...
}
```

### 10.2 テスト用DI設定

```csharp
public static class TestPlatformServiceExtensions
{
    /// <summary>
    /// テスト用プラットフォームサービスの登録
    /// </summary>
    public static IServiceCollection AddTestPlatformServices(this IServiceCollection services)
    {
        // モック実装を登録
        services.AddSingleton<IWindowManager, MockWindowManager>();
        services.AddSingleton<IScreenCapturer, MockScreenCapturer>();
        services.AddSingleton<IKeyboardHook, MockKeyboardHook>();
        
        // テスト用ファクトリ登録
        services.AddSingleton<IWindowsImageFactory, TestWindowsImageFactory>();
        
        // アダプター登録
        services.AddSingleton<IImageFactory, TestImageAdapterFactory>();
        
        return services;
    }
}
```

## 11. クラス図

以下は、主要なクラスとインターフェースの関係を表したクラス図です：

```
+-------------------+      +----------------------+
| <<interface>>     |      | <<interface>>        |
| IWindowManager    |      | IScreenCapturer      |
+-------------------+      +----------------------+
        ^                           ^
        |                           |
+-------------------+      +----------------------+
| WindowsManager    |      | WindowsCapturer      |
+-------------------+      +----------------------+
        |                           |
        v                           v
+---------------------------------------------------+
|               NativeMethods                       |
| +----------------+  +----------------+            |
| | User32Methods  |  | Gdi32Methods   |  ...       |
| +----------------+  +----------------+            |
+---------------------------------------------------+
```

## 12. セキュリティ考慮事項

プラットフォームAPIへのアクセスには以下のセキュリティ考慮事項を適用します：

1. **最小権限の原則**: 必要な API 呼び出しのみを実装
2. **入力検証**: すべてのメソッドパラメータを検証
3. **例外処理**: すべての P/Invoke 呼び出しを適切に例外処理
4. **リソース管理**: すべてのネイティブリソースを確実に解放
5. **境界チェック**: バッファサイズや配列のインデックスを厳密にチェック

## 13. パフォーマンス最適化

### 13.1 キャプチャパフォーマンス

画面キャプチャのパフォーマンスを最適化するためのアプローチ：

1. **差分検出**: 前回のキャプチャから変更がない場合はキャプチャをスキップ
2. **領域最適化**: 監視する必要がある領域のみをキャプチャ
3. **スレッド管理**: キャプチャ処理を専用スレッドで実行
4. **メモリ再利用**: GCプレッシャーを減らすためにメモリバッファを再利用

```csharp
// キャプチャパフォーマンス最適化の例
public class OptimizedWindowsCapturer : IScreenCapturer
{
    private Bitmap? _cachedBitmap;
    private Rectangle _lastCaptureRegion;
    private readonly object _lockObject = new();
    
    public async Task<IWindowsImage> CaptureRegionAsync(Rectangle region)
    {
        lock (_lockObject)
        {
            // 必要なサイズのビットマップを作成または再利用
            if (_cachedBitmap == null || 
                _cachedBitmap.Width != region.Width || 
                _cachedBitmap.Height != region.Height)
            {
                _cachedBitmap?.Dispose();
                _cachedBitmap = new Bitmap(region.Width, region.Height);
            }
            
            // キャプチャを実行
            using (var graphics = Graphics.FromImage(_cachedBitmap))
            {
                graphics.CopyFromScreen(region.X, region.Y, 0, 0, region.Size);
            }
            
            // 結果をコピーして返す（キャッシュされたビットマップを変更されないように）
            var result = new Bitmap(_cachedBitmap);
            _lastCaptureRegion = region;
            
            return new WindowsImage(result);
        }
    }
    
    // その他のメソッド実装
    // ...
}
```

## 14. キャプチャ実装

### 14.1 GDIベースのキャプチャメソッド

Windows GDI (Graphics Device Interface)を使用した画面キャプチャ機能の実装について説明します。GDIベースのキャプチャは安定性と互換性に優れており、多くのゲームとの互換性があります。

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

#### 14.1.1 主な特徴

- **BitBlt関数**：スクリーン領域のキャプチャに使用
- **PrintWindow関数**：ウィンドウ全体のキャプチャに使用
- **DIBセクションの再利用**：キャプチャのパフォーマンス最適化
- **DPI対応**：高DPI環境でのキャプチャ精度を確保
- **メモリ管理**：SafeHandleを使用した適切なリソース解放

#### 14.1.2 リソース管理

GDIリソースは適切に管理する必要があります。以下のように`SafeHandle`を継承したクラスを使用して、リソースのリークを防止します：

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
```

#### 14.1.3 パフォーマンス最適化

GDIキャプチャでは、以下のパフォーマンス最適化テクニックを適用しています：

- **メモリDCの再利用**：同一サイズのキャプチャで再利用
- **非同期処理**：UIスレッドをブロックしない設計
- **エラー処理**：適切な例外とフォールバックメカニズム
- **ロギング**：LoggerMessageパターンによる効率的なロギング

#### 14.1.4 セキュリティ対策

DLLハイジャック脆弱性を防ぐために、以下の対策を実施しています：

- 明示的なシステムディレクトリパスの指定
- `SetLastError = true`と`ExactSpelling = true`の設定
- 適切なエラーハンドリングとアクセス制御

## 15. まとめ

プラットフォーム抽象化レイヤーは、Baketaアプリケーションのコア機能とWindows固有コードの間の明確な境界を提供します。適切な抽象化レベルと依存関係の方向性を維持することで、アプリケーションの保守性、テスト容易性、および将来的な拡張可能性が向上します。

このレイヤーは以下の特徴を持ちます：

1. **明確な責任分離**: プラットフォーム固有のコードを明確に分離
2. **適切な抽象化レベル**: 過度な抽象化を避け、実用的なインターフェースを提供
3. **エラー処理**: 堅牢なエラー処理と明確な例外階層
4. **テスト容易性**: モック可能なインターフェースによるテスト容易性の確保
5. **パフォーマンス最適化**: メモリ使用量とCPU負荷を最小限に抑えるための最適化

これらの設計原則により、Baketaアプリケーションは高品質で保守性の高いコードベースを維持しつつ、プラットフォーム固有の最適化も取り入れることができます。