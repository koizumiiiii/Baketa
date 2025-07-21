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
        if (_initialized)
            return true;

        try
        {
            int result = NativeWindowsCapture.BaketaCapture_Initialize();
            if (result != NativeWindowsCapture.ErrorCodes.Success)
            {
                string errorMsg = NativeWindowsCapture.GetLastErrorMessage();
                _logger?.LogError("ネイティブライブラリの初期化に失敗: {ErrorCode}, {ErrorMessage}", result, errorMsg);
                return false;
            }

            _initialized = true;
            _logger?.LogInformation("ネイティブ Windows Graphics Capture ライブラリが初期化されました");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ネイティブライブラリ初期化中に例外が発生");
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

                for (int y = 0; y < frame.height; y++)
                {
                    byte* srcRow = src + (y * frame.stride);
                    byte* dstRow = dst + (y * bitmapData.Stride);
                    
                    // BGRAデータをそのままコピー（フォーマットが一致）
                    for (int x = 0; x < frame.width; x++)
                    {
                        int srcOffset = x * 4;
                        int dstOffset = x * 4;
                        
                        dstRow[dstOffset + 0] = srcRow[srcOffset + 0]; // B
                        dstRow[dstOffset + 1] = srcRow[srcOffset + 1]; // G
                        dstRow[dstOffset + 2] = srcRow[srcOffset + 2]; // R
                        dstRow[dstOffset + 3] = srcRow[srcOffset + 3]; // A
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        return bitmap;
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            // セッションを削除
            if (_sessionId >= 0)
            {
                NativeWindowsCapture.BaketaCapture_ReleaseSession(_sessionId);
                _sessionId = -1;
            }

            // ライブラリをシャットダウン
            if (_initialized)
            {
                NativeWindowsCapture.BaketaCapture_Shutdown();
                _initialized = false;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "リソース解放中に例外が発生");
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// ファイナライザー
    /// </summary>
    ~NativeWindowsCaptureWrapper()
    {
        Dispose();
    }
}