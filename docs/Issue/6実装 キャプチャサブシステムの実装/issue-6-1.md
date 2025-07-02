# 実装: GDIベースのキャプチャメソッド

## 概要
Windows GDI (Graphics Device Interface) を使用した画面キャプチャ機能を実装します。

## 目的・理由
GDIは安定性と互換性に優れたWindowsの標準的な画面キャプチャ方法です。多くのゲームとの互換性があり、基本的なキャプチャ手段として実装する必要があります。Direct3Dよりも汎用性が高く、幅広いゲームに対応できるため、第一の実装対象とします。

## 詳細
- GDIの`BitBlt`および`PrintWindow`関数を利用したキャプチャ実装
- ウィンドウハンドル（HWND）ベースのキャプチャ機能
- 領域指定キャプチャ機能
- スクリーンスケーリング対応
- DPI対応

## タスク分解
- [ ] `IGdiScreenCapturer`インターフェースの設計
  - [ ] 全画面キャプチャメソッドの定義
  - [ ] ウィンドウキャプチャメソッドの定義
  - [ ] 領域キャプチャメソッドの定義
- [ ] `GdiScreenCapturer`クラスの実装
  - [ ] `BitBlt`を使用した画面キャプチャの実装
  - [ ] `PrintWindow`を使用したウィンドウキャプチャの実装
  - [ ] キャプチャ結果のIImageへの変換
- [ ] キャプチャ処理のパフォーマンス最適化
  - [ ] DIBセクションの再利用
  - [ ] メモリ割り当ての最小化
- [ ] キャプチャ対象ウィンドウの検出機能
  - [ ] アクティブウィンドウの取得
  - [ ] ウィンドウタイトルによる検索
  - [ ] ウィンドウクラスによる検索
- [ ] エラーハンドリング
  - [ ] アクセス拒否エラーの処理
  - [ ] ウィンドウが閉じられた場合の処理
- [ ] 単体テストの作成

## 実装例
```csharp
namespace Baketa.Infrastructure.Platform.Windows.Capture
{
    /// <summary>
    /// GDIベースの画面キャプチャ実装
    /// </summary>
    public class GdiScreenCapturer : IGdiScreenCapturer
    {
        private readonly IWindowsImageFactory _imageFactory;
        private readonly ILogger<GdiScreenCapturer>? _logger;
        
        // DIBセクションの再利用による最適化のためのフィールド
        private IntPtr _hdcMemory;
        private IntPtr _hBitmap;
        private int _lastWidth;
        private int _lastHeight;
        
        public GdiScreenCapturer(
            IWindowsImageFactory imageFactory,
            ILogger<GdiScreenCapturer>? logger = null)
        {
            _imageFactory = imageFactory ?? throw new ArgumentNullException(nameof(imageFactory));
            _logger = logger;
            
            _hdcMemory = IntPtr.Zero;
            _hBitmap = IntPtr.Zero;
            _lastWidth = 0;
            _lastHeight = 0;
        }
        
        public async Task<IWindowsImage> CaptureScreenAsync()
        {
            _logger?.LogDebug("プライマリスクリーンのキャプチャを開始");
            
            // プライマリスクリーンのサイズ取得
            int screenWidth = User32.GetSystemMetrics(SystemMetric.SM_CXSCREEN);
            int screenHeight = User32.GetSystemMetrics(SystemMetric.SM_CYSCREEN);
            
            return await CaptureRegionAsync(new Rectangle(0, 0, screenWidth, screenHeight));
        }
        
        public async Task<IWindowsImage> CaptureWindowAsync(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                throw new ArgumentException("ウィンドウハンドルが無効です", nameof(hWnd));
                
            _logger?.LogDebug("ウィンドウ (HWND: {HWND}) のキャプチャを開始", hWnd);
            
            // ウィンドウの領域を取得
            User32.GetWindowRect(hWnd, out RECT rect);
            
            // クライアント領域のキャプチャ
            int width = rect.right - rect.left;
            int height = rect.bottom - rect.top;
            
            // DPI対応
            User32.SetProcessDPIAware();
            
            // メモリDCの準備
            using var screenDC = new DeviceContextHandle(User32.GetDC(IntPtr.Zero));
            using var memoryDC = new DeviceContextHandle(Gdi32.CreateCompatibleDC(screenDC));
            using var bitmapHandle = new BitmapHandle(Gdi32.CreateCompatibleBitmap(screenDC, width, height));
            
            Gdi32.SelectObject(memoryDC, bitmapHandle);
            
            // PrintWindowを使用してウィンドウをキャプチャ
            User32.PrintWindow(hWnd, memoryDC, PrintWindowFlags.PW_CLIENTONLY);
            
            // ビットマップからイメージを作成
            var bitmap = System.Drawing.Image.FromHbitmap(bitmapHandle);
            var windowsImage = _imageFactory.CreateFromBitmap((Bitmap)bitmap);
            
            _logger?.LogDebug("ウィンドウキャプチャ完了: {Width}x{Height}", width, height);
            
            return windowsImage;
        }
        
        public async Task<IWindowsImage> CaptureRegionAsync(Rectangle region)
        {
            _logger?.LogDebug("画面領域 {Region} のキャプチャを開始", region);
            
            int width = region.Width;
            int height = region.Height;
            
            if (width <= 0 || height <= 0)
                throw new ArgumentException("キャプチャ領域のサイズが無効です", nameof(region));
                
            // DPI対応
            User32.SetProcessDPIAware();
            
            // デバイスコンテキスト取得
            using var screenDC = new DeviceContextHandle(User32.GetDC(IntPtr.Zero));
            
            // メモリDCの準備または再利用
            EnsureMemoryDC(screenDC, width, height);
            
            // BitBltでキャプチャ実行
            Gdi32.BitBlt(
                _hdcMemory,
                0, 0, width, height,
                screenDC,
                region.X, region.Y,
                BitBltFlags.SRCCOPY);
            
            // ビットマップからイメージを作成
            var bitmap = System.Drawing.Image.FromHbitmap(_hBitmap);
            var windowsImage = _imageFactory.CreateFromBitmap((Bitmap)bitmap);
            
            _logger?.LogDebug("領域キャプチャ完了: {Width}x{Height}", width, height);
            
            return windowsImage;
        }
        
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
            _hdcMemory = Gdi32.CreateCompatibleDC(hdcScreen);
            _hBitmap = Gdi32.CreateCompatibleBitmap(hdcScreen, width, height);
            
            if (_hdcMemory == IntPtr.Zero || _hBitmap == IntPtr.Zero)
            {
                CleanupResources();
                throw new InvalidOperationException("メモリDCの作成に失敗しました");
            }
            
            // メモリDCにビットマップを選択
            Gdi32.SelectObject(_hdcMemory, _hBitmap);
            
            _lastWidth = width;
            _lastHeight = height;
        }
        
        private void CleanupResources()
        {
            if (_hBitmap != IntPtr.Zero)
            {
                Gdi32.DeleteObject(_hBitmap);
                _hBitmap = IntPtr.Zero;
            }
            
            if (_hdcMemory != IntPtr.Zero)
            {
                Gdi32.DeleteDC(_hdcMemory);
                _hdcMemory = IntPtr.Zero;
            }
            
            _lastWidth = 0;
            _lastHeight = 0;
        }
        
        public void Dispose()
        {
            CleanupResources();
            GC.SuppressFinalize(this);
        }
    }
    
    // 一般的なハンドル管理用の内部クラス
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
                User32.ReleaseDC(IntPtr.Zero, handle);
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
                Gdi32.DeleteObject(handle);
            }
            return true;
        }
    }
}
```

## Windows API 定義
```csharp
internal static class User32
{
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    
    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);
    
    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hDC, PrintWindowFlags flags);
    
    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(SystemMetric smIndex);
    
    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();
}

internal static class Gdi32
{
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
    
    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    
    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, BitBltFlags dwRop);
    
    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);
    
    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int left;
    public int top;
    public int right;
    public int bottom;
}

[Flags]
internal enum BitBltFlags : uint
{
    SRCCOPY = 0x00CC0020
}

[Flags]
internal enum PrintWindowFlags : uint
{
    PW_DEFAULT = 0,
    PW_CLIENTONLY = 1
}

internal enum SystemMetric
{
    SM_CXSCREEN = 0,
    SM_CYSCREEN = 1
}
```

## 関連Issue/参考
- 親Issue: #6 実装: キャプチャサブシステムの実装
- 関連: #3.2 改善: キャプチャアダプターレイヤーの実装
- 参照: E:\dev\Baketa\docs\3-architecture\platform\platform-abstraction.md
- 参照: Windows API Documentation (GDI, User32)

## マイルストーン
マイルストーン2: キャプチャとOCR基盤

## ラベル
- `type: feature`
- `priority: high`
- `component: core`
