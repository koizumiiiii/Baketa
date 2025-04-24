# アダプターレイヤーの実装ガイド

*最終更新: 2025年4月24日*

## 1. 概要

このドキュメントでは、Baketaプロジェクトにおけるアダプターレイヤーの設計と実装について説明します。アダプターレイヤーは、プラットフォーム依存コード（Windows実装）と抽象化レイヤー間の連携を行い、レイヤー間の疎結合を実現します。

## 2. アダプターパターンの目的

アダプターパターンを導入する主な目的は以下の通りです：

1. **レイヤー間の疎結合化**: プラットフォーム固有の実装とコア抽象化レイヤーを分離
2. **テスト容易性の向上**: モック可能なインターフェースによるテスト容易性の確保
3. **拡張性の向上**: 新しいプラットフォーム実装の追加を容易にする
4. **責任分担の明確化**: 変換ロジックの集中管理

## 3. アダプターレイヤーの構造

### 3.1 アダプターインターフェース

すべてのアダプターは、以下の命名規則に従ったインターフェースを実装します：

```
I[機能名]Adapter
```

例：
- `IWindowsImageAdapter`
- `ICaptureAdapter`
- `IWindowManagerAdapter`

### 3.2 実装クラス

アダプターの実装クラスは以下の命名規則に従います：

```
[プラットフォーム名][機能名]Adapter
```

例：
- `WindowsImageAdapter`
- `WindowsCaptureAdapter`
- `WindowsManagerAdapter`

## 4. 主要アダプターインターフェース

### 4.1 IWindowsImageAdapter

```csharp
/// <summary>
/// Windows画像をコア画像に変換するアダプターインターフェース
/// </summary>
/// <remarks>インターフェースの実際の定義は Baketa.Infrastructure.Platform.Adapters 名前空間に実装されています</remarks>
public interface IWindowsImageAdapter
{
    /// <summary>
    /// Windowsネイティブイメージをコアイメージ(IAdvancedImage)に変換します
    /// </summary>
    /// <param name="windowsImage">変換元のWindowsイメージ</param>
    /// <returns>変換後のAdvancedImage</returns>
    IAdvancedImage ToAdvancedImage(IWindowsImage windowsImage);

    /// <summary>
    /// Windowsネイティブイメージをコアイメージ(IImage)に変換します
    /// </summary>
    /// <param name="windowsImage">変換元のWindowsイメージ</param>
    /// <returns>変換後のImage</returns>
    IImage ToImage(IWindowsImage windowsImage);

    /// <summary>
    /// コアイメージ(IAdvancedImage)をWindowsネイティブイメージに変換します
    /// </summary>
    /// <param name="advancedImage">変換元のAdvancedImage</param>
    /// <returns>変換後のWindowsイメージ</returns>
    Task<IWindowsImage> FromAdvancedImageAsync(IAdvancedImage advancedImage);

    /// <summary>
    /// コアイメージ(IImage)をWindowsネイティブイメージに変換します
    /// </summary>
    /// <param name="image">変換元のImage</param>
    /// <returns>変換後のWindowsイメージ</returns>
    Task<IWindowsImage> FromImageAsync(IImage image);

    /// <summary>
    /// Bitmapからコアイメージ(IAdvancedImage)を作成します
    /// </summary>
    /// <param name="bitmap">変換元のBitmap</param>
    /// <returns>変換後のAdvancedImage</returns>
    IAdvancedImage CreateAdvancedImageFromBitmap(Bitmap bitmap);

    /// <summary>
    /// バイト配列からコアイメージ(IAdvancedImage)を作成します
    /// </summary>
    /// <param name="imageData">画像データのバイト配列</param>
    /// <returns>変換後のAdvancedImage</returns>
    Task<IAdvancedImage> CreateAdvancedImageFromBytesAsync(byte[] imageData);

    /// <summary>
    /// ファイルからコアイメージ(IAdvancedImage)を作成します
    /// </summary>
    /// <param name="filePath">画像ファイルのパス</param>
    /// <returns>変換後のAdvancedImage</returns>
    Task<IAdvancedImage> CreateAdvancedImageFromFileAsync(string filePath);
}
```

### 4.2 ICaptureAdapter

```csharp
/// <summary>
/// Windows固有の画面キャプチャ実装と抽象化レイヤーの間のアダプターインターフェース
/// </summary>
public interface ICaptureAdapter
{
    /// <summary>
    /// Windows固有のキャプチャサービスを使用して画面全体をキャプチャし、プラットフォーム非依存のIImageを返します
    /// </summary>
    /// <returns>キャプチャした画像</returns>
    Task<IImage> CaptureScreenAsync();
    
    /// <summary>
    /// Windows固有のキャプチャサービスを使用して指定した領域をキャプチャし、プラットフォーム非依存のIImageを返します
    /// </summary>
    /// <param name="region">キャプチャする領域</param>
    /// <returns>キャプチャした画像</returns>
    Task<IImage> CaptureRegionAsync(Rectangle region);
    
    /// <summary>
    /// Windows固有のキャプチャサービスを使用して指定したウィンドウをキャプチャし、プラットフォーム非依存のIImageを返します
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>キャプチャした画像</returns>
    Task<IImage> CaptureWindowAsync(IntPtr windowHandle);
    
    /// <summary>
    /// Windows固有のキャプチャサービスを使用して指定したウィンドウのクライアント領域をキャプチャし、プラットフォーム非依存のIImageを返します
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>キャプチャした画像</returns>
    Task<IImage> CaptureClientAreaAsync(IntPtr windowHandle);
    
    /// <summary>
    /// コアのCaptureOptionsをWindows固有のキャプチャオプションに変換して設定します
    /// </summary>
    /// <param name="options">プラットフォーム非依存のキャプチャオプション</param>
    void SetCaptureOptions(CaptureOptions options);
    
    /// <summary>
    /// 現在のWindows固有のキャプチャオプションをコアのCaptureOptionsに変換して返します
    /// </summary>
    /// <returns>プラットフォーム非依存のキャプチャオプション</returns>
    CaptureOptions GetCaptureOptions();
    
    /// <summary>
    /// Windows固有のキャプチャオプションをコアのCaptureOptionsに変換します
    /// </summary>
    /// <param name="windowsOptions">Windows固有のキャプチャオプション</param>
    /// <returns>プラットフォーム非依存のキャプチャオプション</returns>
    CaptureOptions ConvertToCoreOptions(WindowsCaptureOptions windowsOptions);
    
    /// <summary>
    /// コアのCaptureOptionsをWindows固有のキャプチャオプションに変換します
    /// </summary>
    /// <param name="coreOptions">プラットフォーム非依存のキャプチャオプション</param>
    /// <returns>Windows固有のキャプチャオプション</returns>
    WindowsCaptureOptions ConvertToWindowsOptions(CaptureOptions coreOptions);
}
```

### 4.3 IWindowManagerAdapter

```csharp
/// <summary>
/// Windows固有のウィンドウ管理機能と抽象化レイヤーの間のアダプターインターフェース
/// </summary>
public interface IWindowManagerAdapter
{
    /// <summary>
    /// Windows固有のウィンドウマネージャーを使用してアクティブなウィンドウハンドルを取得します
    /// </summary>
    /// <returns>アクティブウィンドウのハンドル</returns>
    IntPtr GetActiveWindowHandle();
    
    /// <summary>
    /// Windows固有のウィンドウマネージャーを使用して指定したタイトルを持つウィンドウハンドルを取得します
    /// </summary>
    /// <param name="title">ウィンドウタイトル (部分一致)</param>
    /// <returns>一致するウィンドウのハンドル。見つからなければIntPtr.Zero</returns>
    IntPtr FindWindowByTitle(string title);
    
    /// <summary>
    /// Windows固有のウィンドウマネージャーを使用して指定したクラス名を持つウィンドウハンドルを取得します
    /// </summary>
    /// <param name="className">ウィンドウクラス名</param>
    /// <returns>一致するウィンドウのハンドル。見つからなければIntPtr.Zero</returns>
    IntPtr FindWindowByClass(string className);
    
    /// <summary>
    /// Windows固有のウィンドウマネージャーを使用してウィンドウの位置とサイズを取得します
    /// </summary>
    /// <param name="handle">ウィンドウハンドル</param>
    /// <returns>ウィンドウの位置とサイズを表す Rectangle</returns>
    Rectangle? GetWindowBounds(IntPtr handle);
    
    /// <summary>
    /// Windows固有のウィンドウマネージャーを使用してウィンドウのクライアント領域を取得します
    /// </summary>
    /// <param name="handle">ウィンドウハンドル</param>
    /// <returns>クライアント領域の位置とサイズを表す Rectangle</returns>
    Rectangle? GetClientBounds(IntPtr handle);
    
    /// <summary>
    /// Windows固有のウィンドウマネージャーを使用してウィンドウのタイトルを取得します
    /// </summary>
    /// <param name="handle">ウィンドウハンドル</param>
    /// <returns>ウィンドウタイトル</returns>
    string GetWindowTitle(IntPtr handle);
    
    /// <summary>
    /// Windows固有のウィンドウマネージャーを使用して実行中のアプリケーションのウィンドウリストを取得します
    /// </summary>
    /// <returns>ウィンドウ情報のリスト</returns>
    IReadOnlyCollection<WindowInfo> GetRunningApplicationWindows();
    
    /// <summary>
    /// Windows固有のウィンドウマネージャーを使用してウィンドウが最小化されているか確認します
    /// </summary>
    /// <param name="handle">ウィンドウハンドル</param>
    /// <returns>最小化されている場合はtrue</returns>
    bool IsMinimized(IntPtr handle);
    
    /// <summary>
    /// Windows固有のウィンドウマネージャーを使用してウィンドウが最大化されているか確認します
    /// </summary>
    /// <param name="handle">ウィンドウハンドル</param>
    /// <returns>最大化されている場合はtrue</returns>
    bool IsMaximized(IntPtr handle);
    
    /// <summary>
    /// IWindowManager(Windows)からIWindowManager(Core)への適応を行います
    /// </summary>
    /// <param name="windowsManager">Windows固有のウィンドウマネージャー</param>
    /// <returns>プラットフォーム非依存のウィンドウマネージャー</returns>
    Baketa.Core.Abstractions.Platform.IWindowManager AdaptWindowManager(Baketa.Core.Abstractions.Platform.Windows.IWindowManager windowsManager);
    
    /// <summary>
    /// ゲームウィンドウを特定します
    /// </summary>
    /// <param name="gameTitle">ゲームタイトル（部分一致）</param>
    /// <returns>ゲームウィンドウのハンドル。見つからなければIntPtr.Zero</returns>
    IntPtr FindGameWindow(string gameTitle);
    
    /// <summary>
    /// ウィンドウの種類を判定します
    /// </summary>
    /// <param name="handle">ウィンドウハンドル</param>
    /// <returns>ウィンドウの種類</returns>
    WindowType GetWindowType(IntPtr handle);
}
```

## 5. アダプター実装例

### 5.1 WindowsImageAdapter スタブ実装例

```csharp
/// <summary>
/// IWindowsImageAdapterインターフェースの基本スタブ実装
/// 注：実際の機能実装は後の段階で行います
/// </summary>
public class WindowsImageAdapterStub : IWindowsImageAdapter
{
    private readonly IWindowsImageFactory? _imageFactory;
    
    public WindowsImageAdapterStub(IWindowsImageFactory? imageFactory = null)
    {
        _imageFactory = imageFactory;
    }
    
    public IAdvancedImage ToAdvancedImage(IWindowsImage windowsImage)
    {
        ArgumentNullException.ThrowIfNull(windowsImage, nameof(windowsImage));
        
        // スタブ実装では既存のWindowsImageAdapterを利用
        return new WindowsImageAdapter(windowsImage);
    }
    
    public IImage ToImage(IWindowsImage windowsImage)
    {
        ArgumentNullException.ThrowIfNull(windowsImage, nameof(windowsImage));
        
        // IAdvancedImageはIImageを継承しているので、ToAdvancedImageの結果をそのまま返す
        return ToAdvancedImage(windowsImage);
    }
    
    public async Task<IWindowsImage> FromAdvancedImageAsync(IAdvancedImage advancedImage)
    {
        ArgumentNullException.ThrowIfNull(advancedImage, nameof(advancedImage));
        
        // スタブ実装では単純にバイト配列を経由して変換
        var imageBytes = await advancedImage.ToByteArrayAsync().ConfigureAwait(false);
        using var stream = new MemoryStream(imageBytes);
        using var bitmap = new Bitmap(stream);
        
        // 所有権移転のためのクローン作成
        var persistentBitmap = (Bitmap)bitmap.Clone();
        return new Windows.WindowsImage(persistentBitmap);
    }
    
    public async Task<IWindowsImage> FromImageAsync(IImage image)
    {
        ArgumentNullException.ThrowIfNull(image, nameof(image));
        
        // IAdvancedImageの場合は特化したメソッドを使用
        if (image is IAdvancedImage advancedImage)
        {
            return await FromAdvancedImageAsync(advancedImage).ConfigureAwait(false);
        }
        
        // それ以外はバイト配列を経由して変換
        var imageBytes = await image.ToByteArrayAsync().ConfigureAwait(false);
        using var stream = new MemoryStream(imageBytes);
        using var bitmap = new Bitmap(stream);
        
        // 所有権移転のためのクローン作成
        var persistentBitmap = (Bitmap)bitmap.Clone();
        return new Windows.WindowsImage(persistentBitmap);
    }
    
    public IAdvancedImage CreateAdvancedImageFromBitmap(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap, nameof(bitmap));
        
        // BitmapをWindowsImageに変換し、それをAdvancedImageに変換
        var windowsImage = new Windows.WindowsImage((Bitmap)bitmap.Clone());
        return ToAdvancedImage(windowsImage);
    }
    
    public Task<IAdvancedImage> CreateAdvancedImageFromBytesAsync(byte[] imageData)
    {
        ArgumentNullException.ThrowIfNull(imageData, nameof(imageData));
        
        try
        {
            using var stream = new MemoryStream(imageData);
            using var bitmap = new Bitmap(stream);
            
            // 所有権移転のためのクローン作成
            var persistentBitmap = (Bitmap)bitmap.Clone();
            var windowsImage = new Windows.WindowsImage(persistentBitmap);
            
            var result = ToAdvancedImage(windowsImage);
            return Task.FromResult(result);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException("無効な画像データです", nameof(imageData), ex);
        }
    }
    
    public async Task<IAdvancedImage> CreateAdvancedImageFromFileAsync(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath, nameof(filePath));
        
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("指定されたファイルが見つかりません", filePath);
        }

        try
        {
            // ファイルをバイト配列として読み込み
            var imageData = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
            return await CreateAdvancedImageFromBytesAsync(imageData).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"ファイル '{filePath}' は有効な画像ではありません", nameof(filePath), ex);
        }
    }
}
```

### 5.2 WindowManagerAdapter 実装例

```csharp
/// <summary>
/// Windows固有のウィンドウマネージャーをプラットフォーム非依存のウィンドウマネージャーに変換するアダプター
/// </summary>
public class WindowManagerAdapter : Baketa.Core.Abstractions.Platform.IWindowManager
{
    private readonly Baketa.Core.Abstractions.Platform.Windows.IWindowManager _windowsManager;

    /// <summary>
    /// WindowManagerAdapterのコンストラクタ
    /// </summary>
    /// <param name="windowsManager">Windows固有のウィンドウマネージャー</param>
    public WindowManagerAdapter(Baketa.Core.Abstractions.Platform.Windows.IWindowManager windowsManager)
    {
        _windowsManager = windowsManager ?? throw new ArgumentNullException(nameof(windowsManager));
    }

    /// <inheritdoc />
    public IntPtr GetActiveWindowHandle()
    {
        return _windowsManager.GetActiveWindowHandle();
    }

    /// <inheritdoc />
    public IntPtr FindWindowByTitle(string title)
    {
        return _windowsManager.FindWindowByTitle(title);
    }

    /// <inheritdoc />
    public IntPtr FindWindowByClass(string className)
    {
        return _windowsManager.FindWindowByClass(className);
    }

    /// <inheritdoc />
    public Rectangle? GetWindowBounds(IntPtr handle)
    {
        return _windowsManager.GetWindowBounds(handle);
    }

    /// <inheritdoc />
    public Rectangle? GetClientBounds(IntPtr handle)
    {
        return _windowsManager.GetClientBounds(handle);
    }

    /// <inheritdoc />
    public string GetWindowTitle(IntPtr handle)
    {
        return _windowsManager.GetWindowTitle(handle);
    }

    /// <inheritdoc />
    public bool IsMinimized(IntPtr handle)
    {
        return _windowsManager.IsMinimized(handle);
    }

    /// <inheritdoc />
    public bool IsMaximized(IntPtr handle)
    {
        return _windowsManager.IsMaximized(handle);
    }

    /// <inheritdoc />
    public bool SetWindowBounds(IntPtr handle, Rectangle bounds)
    {
        return _windowsManager.SetWindowBounds(handle, bounds);
    }

    /// <inheritdoc />
    public bool BringWindowToFront(IntPtr handle)
    {
        return _windowsManager.BringWindowToFront(handle);
    }

    /// <inheritdoc />
    public Dictionary<IntPtr, string> GetRunningApplicationWindows()
    {
        return _windowsManager.GetRunningApplicationWindows();
    }
}
```

### 5.3 WindowManagerAdapterStub 実装例

```csharp
/// <summary>
/// IWindowManagerAdapterインターフェースの基本スタブ実装
/// 注：実際の機能実装は後の段階で行います
/// </summary>
public class WindowManagerAdapterStub : IWindowManagerAdapter
{
    private readonly Baketa.Core.Abstractions.Platform.Windows.IWindowManager _windowManager;

    public WindowManagerAdapterStub(Baketa.Core.Abstractions.Platform.Windows.IWindowManager windowManager)
    {
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
    }

    /// <summary>
    /// Windows固有のウィンドウマネージャーを使用してアクティブなウィンドウハンドルを取得します
    /// </summary>
    /// <returns>アクティブウィンドウのハンドル</returns>
    public IntPtr GetActiveWindowHandle()
    {
        return _windowManager.GetActiveWindowHandle();
    }

    /// <summary>
    /// Windows固有のウィンドウマネージャーを使用して指定したタイトルを持つウィンドウハンドルを取得します
    /// </summary>
    /// <param name="title">ウィンドウタイトル (部分一致)</param>
    /// <returns>一致するウィンドウのハンドル。見つからなければIntPtr.Zero</returns>
    public IntPtr FindWindowByTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title, nameof(title));
        return _windowManager.FindWindowByTitle(title);
    }

    /// <summary>
    /// Windows固有のウィンドウマネージャーを使用して指定したクラス名を持つウィンドウハンドルを取得します
    /// </summary>
    /// <param name="className">ウィンドウクラス名</param>
    /// <returns>一致するウィンドウのハンドル。見つからなければIntPtr.Zero</returns>
    public IntPtr FindWindowByClass(string className)
    {
        ArgumentNullException.ThrowIfNull(className, nameof(className));
        return _windowManager.FindWindowByClass(className);
    }

    /// <summary>
    /// Windows固有のウィンドウマネージャーを使用してウィンドウの位置とサイズを取得します
    /// </summary>
    /// <param name="handle">ウィンドウハンドル</param>
    /// <returns>ウィンドウの位置とサイズを表す Rectangle</returns>
    public Rectangle? GetWindowBounds(IntPtr handle)
    {
        return _windowManager.GetWindowBounds(handle);
    }

    /// <summary>
    /// Windows固有のウィンドウマネージャーを使用してウィンドウのクライアント領域を取得します
    /// </summary>
    /// <param name="handle">ウィンドウハンドル</param>
    /// <returns>クライアント領域の位置とサイズを表す Rectangle</returns>
    public Rectangle? GetClientBounds(IntPtr handle)
    {
        return _windowManager.GetClientBounds(handle);
    }

    /// <summary>
    /// Windows固有のウィンドウマネージャーを使用してウィンドウのタイトルを取得します
    /// </summary>
    /// <param name="handle">ウィンドウハンドル</param>
    /// <returns>ウィンドウタイトル</returns>
    public string GetWindowTitle(IntPtr handle)
    {
        return _windowManager.GetWindowTitle(handle);
    }

    /// <summary>
    /// Windows固有のウィンドウマネージャーを使用して実行中のアプリケーションのウィンドウリストを取得します
    /// </summary>
    /// <returns>ウィンドウ情報のリスト</returns>
    public IReadOnlyCollection<WindowInfo> GetRunningApplicationWindows()
    {
        // スタブ実装では空のリストを返す
        // 実際の実装ではWindows API を用いて実行中のアプリケーションウィンドウを列挙する
        return [];
    }

    /// <summary>
    /// Windows固有のウィンドウマネージャーを使用してウィンドウが最小化されているか確認します
    /// </summary>
    /// <param name="handle">ウィンドウハンドル</param>
    /// <returns>最小化されている場合はtrue</returns>
    public bool IsMinimized(IntPtr handle)
    {
        return _windowManager.IsMinimized(handle);
    }

    /// <summary>
    /// Windows固有のウィンドウマネージャーを使用してウィンドウが最大化されているか確認します
    /// </summary>
    /// <param name="handle">ウィンドウハンドル</param>
    /// <returns>最大化されている場合はtrue</returns>
    public bool IsMaximized(IntPtr handle)
    {
        return _windowManager.IsMaximized(handle);
    }

    /// <summary>
    /// IWindowManager(Windows)からIWindowManager(Core)への適応を行います
    /// </summary>
    /// <param name="windowsManager">Windows固有のウィンドウマネージャー</param>
    /// <returns>プラットフォーム非依存のウィンドウマネージャー</returns>
    public Baketa.Core.Abstractions.Platform.IWindowManager AdaptWindowManager(Baketa.Core.Abstractions.Platform.Windows.IWindowManager windowsManager)
    {
        // スタブ実装では適切なアダプターを返す
        return new WindowManagerAdapter(windowsManager);
    }

    /// <summary>
    /// ゲームウィンドウを特定します
    /// </summary>
    /// <param name="gameTitle">ゲームタイトル（部分一致）</param>
    /// <returns>ゲームウィンドウのハンドル。見つからなければIntPtr.Zero</returns>
    public IntPtr FindGameWindow(string gameTitle)
    {
        ArgumentNullException.ThrowIfNull(gameTitle, nameof(gameTitle));
        
        // スタブ実装では単純にタイトルで検索
        // 実際の実装ではゲーム特有の検出ロジックを使用
        return FindWindowByTitle(gameTitle);
    }

    /// <summary>
    /// ウィンドウの種類を判定します
    /// </summary>
    /// <param name="handle">ウィンドウハンドル</param>
    /// <returns>ウィンドウの種類</returns>
    public WindowType GetWindowType(IntPtr handle)
    {
        // スタブ実装では常にNormalを返す
        // 実際の実装ではウィンドウのスタイルやクラス名などを調査して種類を判定
        return WindowType.Normal;
    }
}
```

### 5.4 CaptureAdapterStub 実装例

```csharp
/// <summary>
/// ICaptureAdapterインターフェースの基本スタブ実装
/// 注：実際の機能実装は後の段階で行います
/// </summary>
public class CaptureAdapterStub : ICaptureAdapter
{
    private readonly IWindowsImageAdapter _imageAdapter;
    private readonly IWindowsCapturer _windowsCapturer;
    private CaptureOptions _captureOptions = new();

    public CaptureAdapterStub(IWindowsImageAdapter imageAdapter, IWindowsCapturer windowsCapturer)
    {
        _imageAdapter = imageAdapter ?? throw new ArgumentNullException(nameof(imageAdapter));
        _windowsCapturer = windowsCapturer ?? throw new ArgumentNullException(nameof(windowsCapturer));
    }

    /// <summary>
    /// Windows固有のキャプチャサービスを使用して画面全体をキャプチャし、プラットフォーム非依存のIImageを返します
    /// </summary>
    /// <returns>キャプチャした画像</returns>
    public async Task<IImage> CaptureScreenAsync()
    {
        // Windowsのキャプチャ機能を使用
        var windowsImage = await _windowsCapturer.CaptureScreenAsync().ConfigureAwait(false);
        
        // コアのImage型に変換
        return _imageAdapter.ToImage(windowsImage);
    }

    /// <summary>
    /// Windows固有のキャプチャサービスを使用して指定した領域をキャプチャし、プラットフォーム非依存のIImageを返します
    /// </summary>
    /// <param name="region">キャプチャする領域</param>
    /// <returns>キャプチャした画像</returns>
    public async Task<IImage> CaptureRegionAsync(Rectangle region)
    {
        var windowsImage = await _windowsCapturer.CaptureRegionAsync(region).ConfigureAwait(false);
        return _imageAdapter.ToImage(windowsImage);
    }

    /// <summary>
    /// Windows固有のキャプチャサービスを使用して指定したウィンドウをキャプチャし、プラットフォーム非依存のIImageを返します
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>キャプチャした画像</returns>
    public async Task<IImage> CaptureWindowAsync(IntPtr windowHandle)
    {
        var windowsImage = await _windowsCapturer.CaptureWindowAsync(windowHandle).ConfigureAwait(false);
        return _imageAdapter.ToImage(windowsImage);
    }

    /// <summary>
    /// Windows固有のキャプチャサービスを使用して指定したウィンドウのクライアント領域をキャプチャし、プラットフォーム非依存のIImageを返します
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>キャプチャした画像</returns>
    public async Task<IImage> CaptureClientAreaAsync(IntPtr windowHandle)
    {
        var windowsImage = await _windowsCapturer.CaptureClientAreaAsync(windowHandle).ConfigureAwait(false);
        return _imageAdapter.ToImage(windowsImage);
    }

    /// <summary>
    /// コアのCaptureOptionsをWindows固有のキャプチャオプションに変換して設定します
    /// </summary>
    /// <param name="options">プラットフォーム非依存のキャプチャオプション</param>
    public void SetCaptureOptions(CaptureOptions options)
    {
        _captureOptions = options ?? throw new ArgumentNullException(nameof(options));
        
        // Windows固有オプションに変換して設定
        var windowsOptions = ConvertToWindowsOptions(_captureOptions);
        _windowsCapturer.SetCaptureOptions(windowsOptions);
    }

    /// <summary>
    /// 現在のWindows固有のキャプチャオプションをコアのCaptureOptionsに変換して返します
    /// </summary>
    /// <returns>プラットフォーム非依存のキャプチャオプション</returns>
    public CaptureOptions GetCaptureOptions()
    {
        // 現在のWindowsオプションを取得して変換
        var windowsOptions = _windowsCapturer.GetCaptureOptions();
        return ConvertToCoreOptions(windowsOptions);
    }

    /// <summary>
    /// IWindowsCapturerFromIScreenCapturerへの適応を行います
    /// </summary>
    /// <param name="windowsCapturer">Windows固有のキャプチャサービス</param>
    /// <returns>プラットフォーム非依存のキャプチャサービス</returns>
    public IScreenCapturer AdaptCapturer(IWindowsCapturer windowsCapturer)
    {
        // スタブ実装では自身を返す（実際の実装では専用のアダプターを返す）
        // ここでは必要なインターフェースが揃っていないためコンパイルエラーを避けるためのスタブ
        throw new NotImplementedException("実際の実装ではない場所で呼び出されました");
    }

    /// <summary>
    /// Windows固有のキャプチャオプションをコアのCaptureOptionsに変換します
    /// </summary>
    /// <param name="windowsOptions">Windows固有のキャプチャオプション</param>
    /// <returns>プラットフォーム非依存のキャプチャオプション</returns>
    public CaptureOptions ConvertToCoreOptions(WindowsCaptureOptions windowsOptions)
    {
        ArgumentNullException.ThrowIfNull(windowsOptions, nameof(windowsOptions));
        
        return new CaptureOptions
        {
            Quality = windowsOptions.Quality,
            IncludeCursor = windowsOptions.IncludeCursor,
            // Windowsオプションには対応するものがないためデフォルト値を設定
            CaptureInterval = 100
        };
    }

    /// <summary>
    /// コアのCaptureOptionsをWindows固有のキャプチャオプションに変換します
    /// </summary>
    /// <param name="coreOptions">プラットフォーム非依存のキャプチャオプション</param>
    /// <returns>Windows固有のキャプチャオプション</returns>
    public WindowsCaptureOptions ConvertToWindowsOptions(CaptureOptions coreOptions)
    {
        ArgumentNullException.ThrowIfNull(coreOptions, nameof(coreOptions));
        
        return new WindowsCaptureOptions
        {
            Quality = coreOptions.Quality,
            IncludeCursor = coreOptions.IncludeCursor,
            // コアオプションでは対応するものがないためデフォルト値を設定
            IncludeWindowDecorations = true,
            PreserveTransparency = true,
            UseDwmCapture = true
        };
    }
}
```

## 6. 依存性注入の設定

アダプターレイヤーを依存性注入コンテナに登録する例:

```csharp
/// <summary>
/// アダプターサービスの登録を行う拡張メソッド群
/// </summary>
public static class AdapterServiceExtensions
{
    /// <summary>
    /// アダプターサービスを依存性注入コンテナに登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>サービスコレクション（チェーン呼び出し用）</returns>
    public static IServiceCollection AddAdapterServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services, nameof(services));
        
        // Windowsサービス実装の登録
        services.AddSingleton<Baketa.Core.Abstractions.Factories.IWindowsImageFactory, Baketa.Infrastructure.Platform.Windows.WindowsImageFactory>();
        services.AddSingleton<Baketa.Core.Abstractions.Platform.Windows.IWindowsCapturer, Baketa.Infrastructure.Platform.Windows.WindowsCapturerStub>();
        services.AddSingleton<Baketa.Core.Abstractions.Platform.Windows.IWindowManager, Baketa.Infrastructure.Platform.Windows.WindowsManagerStub>();

        // アダプターインターフェースとスタブ実装を登録
        services.AddSingleton<IWindowsImageAdapter, WindowsImageAdapterStub>();
        services.AddSingleton<ICaptureAdapter>(sp => {
            var imageAdapter = sp.GetRequiredService<IWindowsImageAdapter>();
            var capturer = sp.GetRequiredService<Baketa.Core.Abstractions.Platform.Windows.IWindowsCapturer>();
            return new CaptureAdapterStub(imageAdapter, capturer);
        });
        services.AddSingleton<IWindowManagerAdapter>(sp => {
            var windowManager = sp.GetRequiredService<Baketa.Core.Abstractions.Platform.Windows.IWindowManager>();
            return new WindowManagerAdapterStub(windowManager);
        });
        
        return services;
    }
    
    /// <summary>
    /// テスト用のモックアダプターサービスを依存性注入コンテナに登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>サービスコレクション（チェーン呼び出し用）</returns>
    public static IServiceCollection AddMockAdapterServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services, nameof(services));
        
        // モックアダプター実装の登録（実際の実装ではスタブクラスを使用）
        services.AddSingleton<IWindowsImageAdapter, WindowsImageAdapterStub>();
        services.AddSingleton<ICaptureAdapter, CaptureAdapterStub>();
        services.AddSingleton<IWindowManagerAdapter, WindowManagerAdapterStub>();
        
        return services;
    }
}
```

## 7. アダプターの実装方針

### 7.1 責任分離の原則

- **アダプタークラスは単一責任原則に従う**: 一つのアダプターは一つの変換責任のみを持つ
- **変換ロジックの集中**: 変換ロジックはアダプターに集中させ、他のクラスには漏れないようにする
- **ビジネスロジックの排除**: アダプターにはビジネスロジックを含めない

### 7.2 エラー処理

- **明示的な引数検証**: すべての入力パラメータにnull/範囲チェックを実施
- **適切な例外スロー**: 変換時の問題は適切な例外としてスローする
- **内部例外のラッピング**: 内部例外は適切にラップして意味のある例外として再スロー

### 7.3 非同期処理

- **非同期メソッドの扱い**: Task、ValueTaskを適切に使い分ける
- **ConfigureAwait(false)の使用**: UI応答性確保のためConfigureAwait(false)を使用
- **キャンセレーション対応**: 長時間実行される変換処理にはキャンセレーショントークンを導入

### 7.4 スレッドセーフ設計

- **イミュータブル設計**: 変換処理は可能な限り副作用を持たないように設計
- **スレッドセーフな実装**: 共有リソースにアクセスする場合は適切な同期機構を使用
- **並列処理の最適化**: 変換処理が重い場合は並列処理を検討

## 8. テスト戦略

### 8.1 単体テスト

```csharp
/// <summary>
/// WindowsImageAdapterのテストクラス
/// </summary>
public class WindowsImageAdapterTests
{
    /// <summary>
    /// ToAdvancedImageメソッドがnull引数でArgumentNullExceptionをスローすることを確認
    /// </summary>
    [Fact]
    public void ToAdvancedImageWithNullArgumentThrowsArgumentNullException()
    {
        // Arrange
        var factory = new Mock<IWindowsImageFactory>();
        var adapter = new WindowsImageAdapter(factory.Object);
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => adapter.ToAdvancedImage(null!));
    }
    
    // 他のテストケース
    // ...
}
```

### 8.2 統合テスト

```csharp
/// <summary>
/// アダプターレイヤーの統合テスト
/// </summary>
public class AdapterIntegrationTests
{
    /// <summary>
    /// 画像変換の往復テスト（IAdvancedImage → IWindowsImage → IAdvancedImage）
    /// </summary>
    [Fact]
    public async Task ImageConversionRoundtripTest()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddAdapterServices();
        var serviceProvider = services.BuildServiceProvider();
        
        var imageAdapter = serviceProvider.GetRequiredService<IWindowsImageAdapter>();
        
        // テスト用の画像を作成
        var testImage = CreateTestImage();
        
        // Act - 往復変換
        var windowsImage = await imageAdapter.FromAdvancedImageAsync(testImage);
        var roundtripImage = imageAdapter.ToAdvancedImage(windowsImage);
        
        // Assert - 元の画像と往復後の画像が同等であることを確認
        AssertImagesEqual(testImage, roundtripImage);
    }
    
    // ヘルパーメソッド
    // ...
}
```

## 9. まとめ

アダプターレイヤーは、プラットフォーム依存コードと抽象化レイヤー間の明確な境界を提供する重要な役割を担います。適切に設計・実装されたアダプターにより、レイヤー間の疎結合化、テスト容易性、拡張性の向上が実現され、アプリケーション全体の品質向上に貢献します。

各アダプターは単一責任原則に従い、変換ロジックを集中管理することで、コードの保守性と可読性が高まります。また、適切なエラー処理と非同期処理の実装により、堅牢で効率的なアプリケーションの構築が可能になります。
