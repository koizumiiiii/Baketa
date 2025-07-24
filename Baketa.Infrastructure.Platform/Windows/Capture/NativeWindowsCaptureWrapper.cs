using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Platform.Windows;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.Windows.Capture;

/// <summary>
/// ネイティブ Windows Graphics Capture API の高レベルラッパー
/// </summary>
public class NativeWindowsCaptureWrapper : IDisposable
{
    private readonly ILogger<NativeWindowsCaptureWrapper>? _logger;
    private readonly WindowsImageFactory _imageFactory;
    private bool _disposed;
    private bool _initialized;
    private int _sessionId = -1;
    private IntPtr _windowHandle;
    private static readonly object _shutdownLock = new object();
    private static int _activeInstances;
    private static bool _hasBeenShutdown;
    private static bool _isApplicationExiting;
    private static bool _globalInitialized;

    /// <summary>
    /// ライブラリが初期化済みかどうか
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// 現在のセッションID
    /// </summary>
    public int SessionId => _sessionId;

    /// <summary>
    /// 対象ウィンドウハンドル
    /// </summary>
    public IntPtr WindowHandle => _windowHandle;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="imageFactory">画像ファクトリー</param>
    /// <param name="logger">ロガー（オプション）</param>
    public NativeWindowsCaptureWrapper(
        WindowsImageFactory imageFactory,
        ILogger<NativeWindowsCaptureWrapper>? logger = null)
    {
        _imageFactory = imageFactory ?? throw new ArgumentNullException(nameof(imageFactory));
        _logger = logger;
    }

    /// <summary>
    /// ライブラリを初期化
    /// </summary>
    /// <returns>成功時は true</returns>
    public bool Initialize()
    {
        try
        {
            // 🔍🔍🔍 デバッグ: 初期化開始ログ
            try
            {
                var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 NativeWrapper.Initialize開始: _globalInitialized={_globalInitialized}, _hasBeenShutdown={_hasBeenShutdown}, _isApplicationExiting={_isApplicationExiting}, _activeInstances={_activeInstances}{Environment.NewLine}");
            }
            catch { /* デバッグログ失敗は無視 */ }

            lock (_shutdownLock)
            {
                // 既にシャットダウン済みの場合は初期化しない
                if (_hasBeenShutdown || _isApplicationExiting)
                {
                    _logger?.LogWarning("ネイティブライブラリは既にシャットダウン済みです");
                    
                    // 🔍🔍🔍 デバッグ: シャットダウン済み警告
                    try
                    {
                        var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ⚠️ NativeWrapper: 既にシャットダウン済み (_hasBeenShutdown={_hasBeenShutdown}, _isApplicationExiting={_isApplicationExiting}){Environment.NewLine}");
                    }
                    catch { /* デバッグログ失敗は無視 */ }
                    
                    return false;
                }

                // グローバル初期化は1回のみ実行
                if (!_globalInitialized)
                {
                    // 🔍🔍🔍 デバッグ: DLL存在確認
                    try
                    {
                        var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                        var dllPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BaketaCaptureNative.dll");
                        var dllExists = System.IO.File.Exists(dllPath);
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📁 DLL存在確認: {dllPath} = {dllExists}{Environment.NewLine}");
                        
                        if (dllExists)
                        {
                            var dllInfo = new System.IO.FileInfo(dllPath);
                            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📊 DLL情報: サイズ={dllInfo.Length}bytes, 更新={dllInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
                        }
                    }
                    catch { /* デバッグログ失敗は無視 */ }

                    // 🔍🔍🔍 デバッグ: サポート状況チェック先行実行
                    try
                    {
                        var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🧪 NativeWrapper: BaketaCapture_IsSupported()テスト呼び出し開始{Environment.NewLine}");
                        
                        int supportResult = NativeWindowsCapture.BaketaCapture_IsSupported();
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📊 NativeWrapper: BaketaCapture_IsSupported()結果 = {supportResult}{Environment.NewLine}");
                    }
                    catch (Exception supportEx)
                    {
                        var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ NativeWrapper: BaketaCapture_IsSupported()例外 {supportEx.GetType().Name}: {supportEx.Message}{Environment.NewLine}");
                    }

                    // 🔍🔍🔍 デバッグ: ネイティブDLL初期化試行
                    try
                    {
                        var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚀 NativeWrapper: BaketaCapture_Initialize()呼び出し開始{Environment.NewLine}");
                    }
                    catch { /* デバッグログ失敗は無視 */ }

                    int result = NativeWindowsCapture.BaketaCapture_Initialize();
                    
                    // 🔍🔍🔍 デバッグ: 初期化結果
                    try
                    {
                        var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📊 NativeWrapper: BaketaCapture_Initialize()結果 = {result}{Environment.NewLine}");
                    }
                    catch { /* デバッグログ失敗は無視 */ }

                    if (result != NativeWindowsCapture.ErrorCodes.Success)
                    {
                        string errorMsg = "";
                        try
                        {
                            errorMsg = NativeWindowsCapture.GetLastErrorMessage();
                        }
                        catch (Exception errorEx)
                        {
                            errorMsg = $"エラーメッセージ取得失敗: {errorEx.Message}";
                        }
                        
                        _logger?.LogError("ネイティブライブラリの初期化に失敗: {ErrorCode}, {ErrorMessage}", result, errorMsg);
                        
                        // 🔍🔍🔍 デバッグ: 初期化失敗詳細
                        try
                        {
                            var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ NativeWrapper: 初期化失敗 ErrorCode={result}, ErrorMsg='{errorMsg}'{Environment.NewLine}");
                        }
                        catch { /* デバッグログ失敗は無視 */ }
                        
                        return false;
                    }
                    _globalInitialized = true;
                    _logger?.LogInformation("ネイティブ Windows Graphics Capture ライブラリをグローバル初期化");
                    
                    // 🔍🔍🔍 デバッグ: グローバル初期化成功
                    try
                    {
                        var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ NativeWrapper: グローバル初期化成功{Environment.NewLine}");
                    }
                    catch { /* デバッグログ失敗は無視 */ }
                }
                else
                {
                    // 🔍🔍🔍 デバッグ: 既に初期化済み
                    try
                    {
                        var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ♻️ NativeWrapper: 既にグローバル初期化済み{Environment.NewLine}");
                    }
                    catch { /* デバッグログ失敗は無視 */ }
                }

                _initialized = true;
                _activeInstances++;
                _logger?.LogInformation("ネイティブ Windows Graphics Capture インスタンス初期化 (ActiveInstances={ActiveInstances})", _activeInstances);
                
                // 🔍🔍🔍 デバッグ: インスタンス初期化完了
                try
                {
                    var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ NativeWrapper: インスタンス初期化完了 ActiveInstances={_activeInstances}{Environment.NewLine}");
                }
                catch { /* デバッグログ失敗は無視 */ }
                
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ネイティブライブラリ初期化中に例外が発生");
            
            // 🔍🔍🔍 デバッグ: 初期化例外
            try
            {
                var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 NativeWrapper: 初期化例外 {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
                System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 スタックトレース: {ex.StackTrace}{Environment.NewLine}");
            }
            catch { /* デバッグログ失敗は無視 */ }
            
            return false;
        }
    }

    /// <summary>
    /// Windows Graphics Capture API がサポートされているかチェック
    /// </summary>
    /// <returns>サポートされている場合は true</returns>
    public bool IsSupported()
    {
        try
        {
            int result = NativeWindowsCapture.BaketaCapture_IsSupported();
            return result == 1;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "サポート状況チェック中に例外が発生");
            return false;
        }
    }

    /// <summary>
    /// ウィンドウキャプチャセッションを作成
    /// </summary>
    /// <param name="windowHandle">対象ウィンドウハンドル</param>
    /// <returns>成功時は true</returns>
    public bool CreateCaptureSession(IntPtr windowHandle)
    {
        if (!_initialized)
        {
            _logger?.LogError("ライブラリが初期化されていません");
            return false;
        }

        if (windowHandle == IntPtr.Zero)
        {
            _logger?.LogError("無効なウィンドウハンドルです");
            return false;
        }

        try
        {
            // 既存セッションがある場合は削除
            if (_sessionId >= 0)
            {
                NativeWindowsCapture.BaketaCapture_ReleaseSession(_sessionId);
                _sessionId = -1;
            }

            int result = NativeWindowsCapture.BaketaCapture_CreateSession(windowHandle, out _sessionId);
            if (result != NativeWindowsCapture.ErrorCodes.Success)
            {
                string errorMsg = NativeWindowsCapture.GetLastErrorMessage();
                
                // 2560x1080などの大画面解像度の場合のメモリ不足エラーを特定
                if (result == NativeWindowsCapture.ErrorCodes.Memory)
                {
                    _logger?.LogError("大画面キャプチャでメモリ不足: WindowHandle=0x{WindowHandle:X8}, {ErrorMessage}", windowHandle.ToInt64(), errorMsg);
                }
                else if (result == NativeWindowsCapture.ErrorCodes.Device)
                {
                    _logger?.LogError("Graphics Device初期化失敗: WindowHandle=0x{WindowHandle:X8}, {ErrorMessage}", windowHandle.ToInt64(), errorMsg);
                }
                else
                {
                    _logger?.LogError("キャプチャセッション作成に失敗: ErrorCode={ErrorCode}, WindowHandle=0x{WindowHandle:X8}, {ErrorMessage}", result, windowHandle.ToInt64(), errorMsg);
                }
                return false;
            }

            _windowHandle = windowHandle;
            _logger?.LogDebug("キャプチャセッションを作成しました: SessionId={SessionId}, WindowHandle=0x{WindowHandle:X8}", 
                _sessionId, windowHandle.ToInt64());
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "キャプチャセッション作成中に例外が発生");
            return false;
        }
    }

    /// <summary>
    /// フレームをキャプチャしてWindowsImageを作成
    /// </summary>
    /// <param name="timeoutMs">タイムアウト時間（ミリ秒）</param>
    /// <returns>キャプチャしたWindowsImage、失敗時はnull</returns>
    public async Task<IWindowsImage?> CaptureFrameAsync(int timeoutMs = 5000)
    {
        if (_sessionId < 0)
        {
            _logger?.LogError("キャプチャセッションが作成されていません");
            return null;
        }
        
        // 🔍🔍🔍 デバッグ: CaptureFrameAsync開始
        try
        {
            var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎬 NativeWrapper.CaptureFrameAsync: SessionId={_sessionId}, HWND=0x{_windowHandle.ToInt64():X8}, Timeout={timeoutMs}ms{Environment.NewLine}");
        }
        catch { /* デバッグログ失敗は無視 */ }

        return await Task.Run(() =>
        {
            try
            {
                var frame = new NativeWindowsCapture.BaketaCaptureFrame();
                int result = NativeWindowsCapture.BaketaCapture_CaptureFrame(_sessionId, out frame, timeoutMs);
                if (result != NativeWindowsCapture.ErrorCodes.Success)
                {
                    string errorMsg = NativeWindowsCapture.GetLastErrorMessage();
                    _logger?.LogError("フレームキャプチャに失敗: {ErrorCode}, {ErrorMessage}", result, errorMsg);
                    
                    // 🔍🔍🔍 デバッグ: キャプチャ失敗
                    try
                    {
                        var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ NativeWrapper.CaptureFrame失敗: ErrorCode={result}, ErrorMsg={errorMsg}, SessionId={_sessionId}{Environment.NewLine}");
                    }
                    catch { /* デバッグログ失敗は無視 */ }
                    
                    return null;
                }

                try
                {
                    // BGRAデータからBitmapを作成
                    var bitmap = CreateBitmapFromBGRA(frame);
                    
                    // WindowsImageを作成
                    var windowsImage = _imageFactory.CreateFromBitmap(bitmap);
                    
                    _logger?.LogDebug("フレームキャプチャ成功: {Width}x{Height}, Timestamp={Timestamp}", 
                        frame.width, frame.height, frame.timestamp);
                    
                    return windowsImage;
                }
                finally
                {
                    // ネイティブメモリを解放
                    NativeWindowsCapture.BaketaCapture_ReleaseFrame(ref frame);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "フレームキャプチャ中に例外が発生");
                return null;
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// BGRAデータからBitmapを作成
    /// </summary>
    /// <param name="frame">キャプチャフレーム</param>
    /// <returns>作成されたBitmap</returns>
    private Bitmap CreateBitmapFromBGRA(NativeWindowsCapture.BaketaCaptureFrame frame)
    {
        if (frame.bgraData == IntPtr.Zero || frame.width <= 0 || frame.height <= 0)
        {
            throw new InvalidOperationException("無効なフレームデータです");
        }

        // 🔍🔍🔍 デバッグ: ピクセルデータ検証
        try
        {
            var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🖼️ CreateBitmapFromBGRA: サイズ={frame.width}x{frame.height}, stride={frame.stride}, timestamp={frame.timestamp}{Environment.NewLine}");
            
            // 最初の数ピクセルをサンプリング
            unsafe
            {
                byte* data = (byte*)frame.bgraData.ToPointer();
                var pixelSamples = new System.Text.StringBuilder();
                for (int i = 0; i < Math.Min(10, frame.width * frame.height); i++)
                {
                    int offset = i * 4;
                    pixelSamples.Append($"[{data[offset]},{data[offset+1]},{data[offset+2]},{data[offset+3]}] ");
                }
                System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎨 最初の10ピクセル: {pixelSamples}{Environment.NewLine}");
            }
        }
        catch { /* デバッグログ失敗は無視 */ }

        // BGRAデータからBitmapを作成
        var bitmap = new Bitmap(frame.width, frame.height, PixelFormat.Format32bppArgb);
        
        var bitmapData = bitmap.LockBits(
            new Rectangle(0, 0, frame.width, frame.height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            // ネイティブメモリからマネージドメモリにコピー
            unsafe
            {
                byte* src = (byte*)frame.bgraData.ToPointer();
                byte* dst = (byte*)bitmapData.Scan0.ToPointer();

                int totalBlackPixels = 0;
                int totalPixels = frame.width * frame.height;
                
                for (int y = 0; y < frame.height; y++)
                {
                    byte* srcRow = src + (y * frame.stride);
                    byte* dstRow = dst + (y * bitmapData.Stride);
                    
                    // BGRAデータをそのままコピー（フォーマットが一致）
                    for (int x = 0; x < frame.width; x++)
                    {
                        int srcOffset = x * 4;
                        int dstOffset = x * 4;
                        
                        byte b = srcRow[srcOffset + 0];
                        byte g = srcRow[srcOffset + 1];
                        byte r = srcRow[srcOffset + 2];
                        byte a = srcRow[srcOffset + 3];
                        
                        dstRow[dstOffset + 0] = b; // B
                        dstRow[dstOffset + 1] = g; // G
                        dstRow[dstOffset + 2] = r; // R
                        dstRow[dstOffset + 3] = a; // A
                        
                        // 黒ピクセルをカウント
                        if (b == 0 && g == 0 && r == 0)
                        {
                            totalBlackPixels++;
                        }
                    }
                }
                
                // 🔍🔍🔍 デバッグ: 黒ピクセル統計
                try
                {
                    var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                    double blackPercentage = (double)totalBlackPixels / totalPixels * 100;
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📊 黒ピクセル統計: {totalBlackPixels}/{totalPixels} ({blackPercentage:F2}%){Environment.NewLine}");
                }
                catch { /* デバッグログ失敗は無視 */ }
            }
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        return bitmap;
    }

    /// <summary>
    /// 現在のキャプチャセッションを停止
    /// </summary>
    public void StopCurrentSession()
    {
        try
        {
            if (_sessionId >= 0)
            {
                _logger?.LogDebug("キャプチャセッション停止: SessionId={SessionId}", _sessionId);
                
                try
                {
                    var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🛑 NativeWrapper: セッション停止 SessionId={_sessionId}{Environment.NewLine}");
                }
                catch { /* デバッグログ失敗は無視 */ }
                
                NativeWindowsCapture.BaketaCapture_ReleaseSession(_sessionId);
                _sessionId = -1;
                _windowHandle = IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "キャプチャセッション停止中にエラー");
        }
    }
    
    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        // 🔍🔍🔍 デバッグ: Dispose開始
        try
        {
            var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🗑️ NativeWrapper.Dispose開始: _initialized={_initialized}, _sessionId={_sessionId}, _activeInstances={_activeInstances}{Environment.NewLine}");
        }
        catch { /* デバッグログ失敗は無視 */ }

        try
        {
            // セッションを削除
            if (_sessionId >= 0)
            {
                try
                {
                    NativeWindowsCapture.BaketaCapture_ReleaseSession(_sessionId);
                    
                    // 🔍🔍🔍 デバッグ: セッション削除成功
                    try
                    {
                        var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ NativeWrapper: セッション削除成功 SessionId={_sessionId}{Environment.NewLine}");
                    }
                    catch { /* デバッグログ失敗は無視 */ }
                }
                catch (Exception sessionEx)
                {
                    _logger?.LogError(sessionEx, "セッション削除中に例外が発生");
                    
                    // 🔍🔍🔍 デバッグ: セッション削除失敗
                    try
                    {
                        var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ NativeWrapper: セッション削除失敗 {sessionEx.GetType().Name}: {sessionEx.Message}{Environment.NewLine}");
                    }
                    catch { /* デバッグログ失敗は無視 */ }
                }
                _sessionId = -1;
            }

            // ライブラリをシャットダウン（最後のインスタンスのみ）
            if (_initialized)
            {
                lock (_shutdownLock)
                {
                    _activeInstances--;
                    _logger?.LogDebug("ネイティブ Windows Graphics Capture インスタンス削除 (ActiveInstances={ActiveInstances}, HasBeenShutdown={HasBeenShutdown}, IsApplicationExiting={IsApplicationExiting})", 
                        _activeInstances, _hasBeenShutdown, _isApplicationExiting);
                    
                    // 🔍🔍🔍 デバッグ: インスタンス削除
                    try
                    {
                        var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📉 NativeWrapper: インスタンス削除 ActiveInstances={_activeInstances} (削除後){Environment.NewLine}");
                    }
                    catch { /* デバッグログ失敗は無視 */ }
                    
                    // シャットダウン条件を厳格化 - アプリケーション終了時のみシャットダウン
                    if (_activeInstances <= 0 && !_hasBeenShutdown && _globalInitialized && _isApplicationExiting)
                    {
                        _hasBeenShutdown = true;
                        _globalInitialized = false;
                        _logger?.LogInformation("ネイティブ Windows Graphics Capture ライブラリをシャットダウン開始");
                        
                        // 🔍🔍🔍 デバッグ: グローバルシャットダウン実行
                        try
                        {
                            var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🛑 NativeWrapper: グローバルシャットダウン実行開始{Environment.NewLine}");
                        }
                        catch { /* デバッグログ失敗は無視 */ }
                        
                        try
                        {
                            NativeWindowsCapture.BaketaCapture_Shutdown();
                            _logger?.LogInformation("ネイティブ Windows Graphics Capture ライブラリシャットダウン完了");
                            
                            // 🔍🔍🔍 デバッグ: グローバルシャットダウン成功
                            try
                            {
                                var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                                System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ NativeWrapper: グローバルシャットダウン完了{Environment.NewLine}");
                            }
                            catch { /* デバッグログ失敗は無視 */ }
                        }
                        catch (Exception shutdownEx)
                        {
                            _logger?.LogError(shutdownEx, "ネイティブライブラリシャットダウン中に例外が発生");
                            
                            // 🔍🔍🔍 デバッグ: グローバルシャットダウン失敗
                            try
                            {
                                var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                                System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ NativeWrapper: グローバルシャットダウン失敗 {shutdownEx.GetType().Name}: {shutdownEx.Message}{Environment.NewLine}");
                            }
                            catch { /* デバッグログ失敗は無視 */ }
                        }
                    }
                    else
                    {
                        _logger?.LogDebug("ネイティブ Windows Graphics Capture ライブラリのシャットダウンをスキップ (ActiveInstances={ActiveInstances}, HasBeenShutdown={HasBeenShutdown}, IsApplicationExiting={IsApplicationExiting})", _activeInstances, _hasBeenShutdown, _isApplicationExiting);
                        
                        // 🔍🔍🔍 デバッグ: グローバルシャットダウンスキップ
                        try
                        {
                            var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ⏭️ NativeWrapper: グローバルシャットダウンスキップ (ActiveInstances={_activeInstances}, HasBeenShutdown={_hasBeenShutdown}, IsApplicationExiting={_isApplicationExiting}){Environment.NewLine}");
                        }
                        catch { /* デバッグログ失敗は無視 */ }
                    }
                }
                _initialized = false;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "リソース解放中に例外が発生");
            
            // 🔍🔍🔍 デバッグ: Dispose例外
            try
            {
                var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 NativeWrapper: Dispose例外 {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
            }
            catch { /* デバッグログ失敗は無視 */ }
        }

        _disposed = true;
        GC.SuppressFinalize(this);
        
        // 🔍🔍🔍 デバッグ: Dispose完了
        try
        {
            var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ NativeWrapper.Dispose完了{Environment.NewLine}");
        }
        catch { /* デバッグログ失敗は無視 */ }
    }

    /// <summary>
    /// アプリケーション終了時の強制クリーンアップ
    /// </summary>
    public static void ForceShutdownOnApplicationExit()
    {
        lock (_shutdownLock)
        {
            _isApplicationExiting = true;
            if (!_hasBeenShutdown)
            {
                _hasBeenShutdown = true;
                try
                {
                    NativeWindowsCapture.BaketaCapture_Shutdown();
                }
                catch
                {
                    // アプリケーション終了時は例外を無視
                }
            }
        }
    }


    /// <summary>
    /// ファイナライザー
    /// </summary>
    ~NativeWindowsCaptureWrapper()
    {
        // ファイナライザーでは例外を抑制し、ネイティブ呼び出しを避ける
        try
        {
            if (!_disposed)
            {
                // セッションのみ削除、シャットダウンは実行しない
                _sessionId = -1;
                _initialized = false;
                _disposed = true;
            }
        }
        catch
        {
            // ファイナライザーでは例外を抑制
        }
    }
}