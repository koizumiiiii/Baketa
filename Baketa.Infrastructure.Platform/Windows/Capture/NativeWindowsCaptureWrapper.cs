using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.IO;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Abstractions.Memory;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Platform.Adapters;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.Windows.Capture;

/// <summary>
/// ネイティブ Windows Graphics Capture API の高レベルラッパー
/// </summary>
public class NativeWindowsCaptureWrapper : IDisposable
{
    private readonly ILogger<NativeWindowsCaptureWrapper>? _logger;
    private readonly WindowsImageFactory _imageFactory;
    private readonly ISafeImageFactory _safeImageFactory; // 🔧 [SAFEIMAGE_FIX] SafeImageAdapterでOCR画像をラップ
    private readonly LoggingSettings _loggingSettings;
    private bool _disposed;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0032:Use auto property", Justification = "Field needs thread-safe access and initialization state tracking")]
    private bool _initialized;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0032:Use auto property", Justification = "Field needs thread-safe access and modification during session management")]
    private int _sessionId = -1;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0032:Use auto property", Justification = "Field needs thread-safe access during session management")]
    private IntPtr _windowHandle;
    private static readonly object _shutdownLock = new();
    private static int _activeInstances;
    private static bool _hasBeenShutdown;
    private static bool _isApplicationExiting;
    private static bool _globalInitialized;
    
    // 🔒 ウィンドウ選択時の安全化: キャプチャ一時停止機能
    private static bool _isPausedForWindowSelection;
    private static readonly object _pauseLock = new();

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
    /// <param name="safeImageFactory">SafeImage ファクトリー（OCR画像のメモリ安全性確保）</param>
    /// <param name="logger">ロガー（オプション）</param>
    /// <param name="loggingSettings">ログ設定（オプション）</param>
    public NativeWindowsCaptureWrapper(
        WindowsImageFactory imageFactory,
        ISafeImageFactory safeImageFactory,
        ILogger<NativeWindowsCaptureWrapper>? logger = null,
        LoggingSettings? loggingSettings = null)
    {
        _imageFactory = imageFactory ?? throw new ArgumentNullException(nameof(imageFactory));
        _safeImageFactory = safeImageFactory ?? throw new ArgumentNullException(nameof(safeImageFactory));
        _logger = logger;
        _loggingSettings = loggingSettings ?? new LoggingSettings();
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
                var debugPath = _loggingSettings.GetFullDebugLogPath();
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
                        var debugPath = _loggingSettings.GetFullDebugLogPath();
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
                        var debugPath = _loggingSettings.GetFullDebugLogPath();
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
                        var debugPath = _loggingSettings.GetFullDebugLogPath();
                        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                        var dllPath = System.IO.Path.Combine(baseDir, "BaketaCaptureNative.dll");
                        
                        // P/Invoke前の詳細ログ
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 P/Invoke前チェック: BaseDir='{baseDir}'{Environment.NewLine}");
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 P/Invoke前チェック: DLL予想パス='{dllPath}'{Environment.NewLine}");
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 P/Invoke前チェック: DLL存在確認={System.IO.File.Exists(dllPath)}{Environment.NewLine}");
                        
                        if (System.IO.File.Exists(dllPath))
                        {
                            var dllInfo = new System.IO.FileInfo(dllPath);
                            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 P/Invoke前チェック: DLLサイズ={dllInfo.Length} bytes{Environment.NewLine}");
                        }
                        
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🧪 NativeWrapper: BaketaCapture_IsSupported()テスト呼び出し開始{Environment.NewLine}");
                        
                        int supportResult = NativeWindowsCapture.BaketaCapture_IsSupported();
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📊 NativeWrapper: BaketaCapture_IsSupported()結果 = {supportResult}{Environment.NewLine}");
                    }
                    catch (DllNotFoundException dllEx)
                    {
                        var debugPath = _loggingSettings.GetFullDebugLogPath();
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ DLL読み込み失敗: {dllEx.Message}{Environment.NewLine}");
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ DLL検索パス: {Environment.GetEnvironmentVariable("PATH")}{Environment.NewLine}");
                    }
                    catch (EntryPointNotFoundException entryEx)
                    {
                        var debugPath = _loggingSettings.GetFullDebugLogPath();
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ エントリポイント未発見: {entryEx.Message}{Environment.NewLine}");
                    }
                    catch (BadImageFormatException imageEx)
                    {
                        var debugPath = _loggingSettings.GetFullDebugLogPath();
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ DLLフォーマットエラー（x86/x64不整合？）: {imageEx.Message}{Environment.NewLine}");
                    }
                    catch (Exception supportEx)
                    {
                        var debugPath = _loggingSettings.GetFullDebugLogPath();
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ NativeWrapper: BaketaCapture_IsSupported()例外 {supportEx.GetType().Name}: {supportEx.Message}{Environment.NewLine}");
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ 詳細スタックトレース: {supportEx.StackTrace}{Environment.NewLine}");
                    }

                    // 🔍🔍🔍 デバッグ: ネイティブDLL初期化試行
                    try
                    {
                        var debugPath = _loggingSettings.GetFullDebugLogPath();
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚀 NativeWrapper: BaketaCapture_Initialize()呼び出し開始{Environment.NewLine}");
                    }
                    catch { /* デバッグログ失敗は無視 */ }

                    int result = NativeWindowsCapture.BaketaCapture_Initialize();
                    
                    // 🔍🔍🔍 デバッグ: 初期化結果
                    try
                    {
                        var debugPath = _loggingSettings.GetFullDebugLogPath();
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
                            var debugPath = _loggingSettings.GetFullDebugLogPath();
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
                        var debugPath = _loggingSettings.GetFullDebugLogPath();
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ NativeWrapper: グローバル初期化成功{Environment.NewLine}");
                    }
                    catch { /* デバッグログ失敗は無視 */ }
                }
                else
                {
                    // 🔍🔍🔍 デバッグ: 既に初期化済み
                    try
                    {
                        var debugPath = _loggingSettings.GetFullDebugLogPath();
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
                    var debugPath = _loggingSettings.GetFullDebugLogPath();
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
                var debugPath = _loggingSettings.GetFullDebugLogPath();
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
                
                // システムダイアログやセキュリティウィンドウのキャプチャ失敗は想定内のためDebugレベル
                // Windows Graphics Capture APIの仕様により、これらは意図的に保護されている
                bool isExpectedFailure = false;
                
                // HRESULTエラーコードから判定（E_ACCESSDENIED, E_INVALIDARG など）
                if (result == -2147024891 || // E_ACCESSDENIED (0x80070005)
                    result == -2147024809 || // E_INVALIDARG (0x80070057)
                    result == -2147467259)   // E_FAIL (0x80004005) - 一般的な失敗
                {
                    isExpectedFailure = true;
                }
                
                // 2560x1080などの大画面解像度の場合のメモリ不足エラーを特定
                if (result == NativeWindowsCapture.ErrorCodes.Memory)
                {
                    _logger?.LogError("大画面キャプチャでメモリ不足: WindowHandle=0x{WindowHandle:X8}, {ErrorMessage}", windowHandle.ToInt64(), errorMsg);
                }
                else if (result == NativeWindowsCapture.ErrorCodes.Device)
                {
                    _logger?.LogError("Graphics Device初期化失敗: WindowHandle=0x{WindowHandle:X8}, {ErrorMessage}", windowHandle.ToInt64(), errorMsg);
                }
                else if (isExpectedFailure)
                {
                    // システムダイアログ等の想定内失敗はDebugレベルで静寂化
                    _logger?.LogDebug("システム保護ウィンドウのキャプチャ制限: ErrorCode={ErrorCode}, WindowHandle=0x{WindowHandle:X8}", result, windowHandle.ToInt64());
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
        
        // 🔍🔍🔍 デバッグ: CaptureFrameAsync開始とウィンドウ情報取得
        try
        {
            var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎬 NativeWrapper.CaptureFrameAsync開始: SessionId={_sessionId}, Timeout={timeoutMs}ms{Environment.NewLine}");
            
            // ウィンドウデバッグ情報取得
            var (windowInfo, screenRect) = NativeWindowsCapture.GetSessionDebugInfo(_sessionId);
            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📋 ウィンドウ情報: {windowInfo}{Environment.NewLine}");
            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📍 スクリーン座標: {screenRect}{Environment.NewLine}");
        }
        catch { /* デバッグログ失敗は無視 */ }

        return await Task.Run(() =>
        {
            // 🔒 安全化: ウィンドウ選択中は一時停止
            lock (_pauseLock)
            {
                if (_isPausedForWindowSelection)
                {
                    try
                    {
                        var debugPath = _loggingSettings.GetFullDebugLogPath();
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ⏸️ [WINDOW_SELECTION] キャプチャは一時停止中のため、null を返します{Environment.NewLine}");
                        System.Diagnostics.Debug.WriteLine("⏸️ [WINDOW_SELECTION] キャプチャは一時停止中のため、null を返します");
                        Console.WriteLine("⏸️ [WINDOW_SELECTION] キャプチャは一時停止中のため、null を返します");
                    }
                    catch { /* デバッグログ失敗は無視 */ }
                    return null;
                }
            }
            
            // 🚀 安全化: フレーム構造体を初期化
            var frame = new NativeWindowsCapture.BaketaCaptureFrame();
            bool frameValid = false;
            
            try
            {
                int result = NativeWindowsCapture.BaketaCapture_CaptureFrame(_sessionId, out frame, timeoutMs);
                if (result != NativeWindowsCapture.ErrorCodes.Success)
                {
                    string errorMsg = NativeWindowsCapture.GetLastErrorMessage();
                    _logger?.LogError("フレームキャプチャに失敗: {ErrorCode}, {ErrorMessage}", result, errorMsg);
                    
                    // 🔍🔍🔍 デバッグ: キャプチャ失敗時の詳細情報
                    try
                    {
                        var debugPath = _loggingSettings.GetFullDebugLogPath();
                        var (windowInfo, screenRect) = NativeWindowsCapture.GetSessionDebugInfo(_sessionId);
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ CaptureFrame失敗: ErrorCode={result}, SessionId={_sessionId}{Environment.NewLine}");
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ エラー詳細: {errorMsg}{Environment.NewLine}");
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ ウィンドウ状態: {windowInfo}{Environment.NewLine}");
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ 座標状態: {screenRect}{Environment.NewLine}");
                    }
                    catch { /* デバッグログ失敗は無視 */ }
                    
                    return null; // フレーム無効なので解放不要
                }

                // フレームが有効であることをマーク
                frameValid = true;

                try
                {
                    // BGRAデータからBitmapを作成
                    var bitmap = CreateBitmapFromBGRA(frame);

                    // 🔧 [SAFEIMAGE_FIX] SafeImageを作成してメモリ安全性を確保
                    var safeImage = _safeImageFactory.CreateFromBitmap(bitmap, frame.width, frame.height);

                    // 🔧 [SAFEIMAGE_FIX] SafeImageAdapterでラップしてIWindowsImageとして返す
                    var safeImageAdapter = new SafeImageAdapter(safeImage, _safeImageFactory);

                    _logger?.LogDebug("✅ [SAFEIMAGE_FIX] フレームキャプチャ成功（SafeImage統合）: {Width}x{Height}, Timestamp={Timestamp}",
                        frame.width, frame.height, frame.timestamp);

                    return safeImageAdapter;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "フレームからビットマップ作成中に例外が発生");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "フレームキャプチャ中に例外が発生");
                return null;
            }
            finally
            {
                // 🚀 安全化: フレームが有効な場合のみ解放
                if (frameValid && frame.bgraData != IntPtr.Zero)
                {
                    try
                    {
                        NativeWindowsCapture.BaketaCapture_ReleaseFrame(ref frame);
                    }
                    catch (Exception ex)
                    {
                        // メモリ解放時の例外をログに記録（クラッシュを防ぐ）
                        _logger?.LogError(ex, "フレーム解放中に例外が発生");
                        
                        try
                        {
                            var debugPath = _loggingSettings.GetFullDebugLogPath();
                            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 フレーム解放例外: {ex.Message}{Environment.NewLine}");
                        }
                        catch { /* デバッグログ失敗は無視 */ }
                    }
                }
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// BGRAデータからBitmapを作成 - Gemini推奨ゼロコピー・ラッパー方式
    /// </summary>
    /// <param name="frame">キャプチャフレーム</param>
    /// <returns>作成されたBitmap</returns>
    private Bitmap CreateBitmapFromBGRA(NativeWindowsCapture.BaketaCaptureFrame frame)
    {
        if (frame.bgraData == IntPtr.Zero || frame.width <= 0 || frame.height <= 0)
        {
            throw new InvalidOperationException("無効なフレームデータです");
        }

        // 🚀 Gemini推奨: 安全化Bitmap方式でStride問題とメモリ安全性を両立解決
        _logger?.LogDebug("🚀 安全化Bitmap方式: サイズ={Width}x{Height}, stride={Stride}, timestamp={Timestamp}",
            frame.width, frame.height, frame.stride, frame.timestamp);
        _logger?.LogDebug("✅ Stride問題+メモリ安全性解決: Clone()によるネイティブ→管理メモリ転送");


        // 🎯 Gemini推奨: ネイティブメモリを一時的にラップし、安全な管理コピーを作成
        using var tempBitmap = new Bitmap(
            width: frame.width,
            height: frame.height,
            stride: frame.stride,
            format: System.Drawing.Imaging.PixelFormat.Format32bppArgb,
            scan0: frame.bgraData);

        // 🛡️ メモリ安全性: Clone()で管理メモリにコピーしてAccessViolationException防止
        var bitmap = tempBitmap.Clone(
            new System.Drawing.Rectangle(0, 0, tempBitmap.Width, tempBitmap.Height),
            tempBitmap.PixelFormat);

        _logger?.LogDebug("安全化Bitmap作成成功: {Width}x{Height}, Stride={Stride}",
            frame.width, frame.height, frame.stride);

        // 🔍 デバッグ: メモリ安全なピクセルデータ品質検証
        try
        {
            var bitmapData = bitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                bitmap.PixelFormat);

            try
            {
                unsafe
                {
                    byte* data = (byte*)bitmapData.Scan0;
                    int blackPixels = 0;

                    // 中央部分の品質サンプリング（10x10領域）
                    int centerStartX = bitmap.Width / 2 - 5;
                    int centerStartY = bitmap.Height / 2 - 5;

                    for (int y = centerStartY; y < centerStartY + 10 && y < bitmap.Height; y++)
                    {
                        for (int x = centerStartX; x < centerStartX + 10 && x < bitmap.Width; x++)
                        {
                            int pixelOffset = (y * bitmapData.Stride) + (x * 4);
                            byte b = data[pixelOffset + 0];
                            byte g = data[pixelOffset + 1];
                            byte r = data[pixelOffset + 2];

                            if (b == 0 && g == 0 && r == 0) blackPixels++;
                        }
                    }

                    _logger?.LogDebug("🎨 安全化品質検証: 黒ピクセル={BlackPixels}/100 ({Percentage:F1}%)",
                        blackPixels, blackPixels / 100.0 * 100);
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }
        catch { /* デバッグログ失敗は無視 */ }

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
                    var debugPath = _loggingSettings.GetFullDebugLogPath();
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
                        var debugPath = _loggingSettings.GetFullDebugLogPath();
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
                        var debugPath = _loggingSettings.GetFullDebugLogPath();
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
                        var debugPath = _loggingSettings.GetFullDebugLogPath();
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
                            var debugPath = _loggingSettings.GetFullDebugLogPath();
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
                                var debugPath = _loggingSettings.GetFullDebugLogPath();
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
                                var debugPath = _loggingSettings.GetFullDebugLogPath();
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
                            var debugPath = _loggingSettings.GetFullDebugLogPath();
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
                var debugPath = _loggingSettings.GetFullDebugLogPath();
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
    /// ウィンドウ選択時にキャプチャを安全に一時停止
    /// </summary>
    public static void PauseForWindowSelection()
    {
        lock (_pauseLock)
        {
            _isPausedForWindowSelection = true;
            
            // 🔍 デバッグ: 一時停止開始ログ
            try
            {
                var defaultLoggingSettings = new LoggingSettings();
                var debugPath = defaultLoggingSettings.GetFullDebugLogPath();
                System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔒 [WINDOW_SELECTION] ネイティブキャプチャを一時停止しました{Environment.NewLine}");
                System.Diagnostics.Debug.WriteLine("🔒 [WINDOW_SELECTION] ネイティブキャプチャを一時停止しました");
                Console.WriteLine("🔒 [WINDOW_SELECTION] ネイティブキャプチャを一時停止しました");
            }
            catch { /* デバッグログ失敗は無視 */ }
        }
    }

    /// <summary>
    /// ウィンドウ選択完了後にキャプチャを再開
    /// </summary>
    public static void ResumeAfterWindowSelection()
    {
        lock (_pauseLock)
        {
            _isPausedForWindowSelection = false;
            
            // 🔍 デバッグ: 再開ログ
            try
            {
                var defaultLoggingSettings = new LoggingSettings();
                var debugPath = defaultLoggingSettings.GetFullDebugLogPath();
                System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚀 [WINDOW_SELECTION] ネイティブキャプチャを再開しました{Environment.NewLine}");
                System.Diagnostics.Debug.WriteLine("🚀 [WINDOW_SELECTION] ネイティブキャプチャを再開しました");
                Console.WriteLine("🚀 [WINDOW_SELECTION] ネイティブキャプチャを再開しました");
            }
            catch { /* デバッグログ失敗は無視 */ }
        }
    }

    /// <summary>
    /// 現在一時停止中かどうかを確認
    /// </summary>
    public static bool IsPausedForWindowSelection => _isPausedForWindowSelection;

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
