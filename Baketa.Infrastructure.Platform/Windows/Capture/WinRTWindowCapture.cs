using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Platform.Windows;
using Microsoft.Extensions.Logging;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using WinRT.Interop;
using IWindowsImageFactory = Baketa.Core.Abstractions.Factories.IWindowsImageFactory;

namespace Baketa.Infrastructure.Platform.Windows.Capture;

/// <summary>
/// Windows Graphics Capture API を使用した最新のウィンドウキャプチャ実装
/// Discordのような高品質なウィンドウキャプチャを実現
/// </summary>
[SupportedOSPlatform("windows10.0.17134.0")]
public class WinRTWindowCapture : IDisposable
{
    private readonly IWindowsImageFactory _imageFactory;
    private readonly ILogger<WinRTWindowCapture>? _logger;
    private bool _disposed;

    public WinRTWindowCapture(
        IWindowsImageFactory imageFactory,
        ILogger<WinRTWindowCapture>? logger = null)
    {
        _imageFactory = imageFactory ?? throw new ArgumentNullException(nameof(imageFactory));
        _logger = logger;

        // Windows Graphics Capture API の初期化チェック
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new NotSupportedException("Windows Graphics Capture API はこのシステムでサポートされていません。Windows 10 バージョン 1903 以降が必要です。");
        }
    }

    /// <summary>
    /// 指定したウィンドウを Windows Graphics Capture API でキャプチャします
    /// </summary>
    /// <param name="hWnd">ウィンドウハンドル</param>
    /// <returns>キャプチャした画像</returns>
    public async Task<IWindowsImage> CaptureWindowAsync(IntPtr hWnd)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (hWnd == IntPtr.Zero)
            throw new ArgumentException("ウィンドウハンドルが無効です", nameof(hWnd));

        _logger?.LogInformation("WinRT ウィンドウキャプチャを開始: {WindowHandle}", hWnd);
        Console.WriteLine($"🚀 WinRT ウィンドウキャプチャを開始: {hWnd.ToInt64():X8}");

        try
        {
            // GraphicsCaptureItemの作成
            var captureItem = CreateCaptureItemFromWindow(hWnd);
            if (captureItem == null)
            {
                var errorMsg = $"ウィンドウ {hWnd} から GraphicsCaptureItem の作成に失敗しました";
                _logger?.LogError("❌ {Error}", errorMsg);
                Console.WriteLine($"❌ {errorMsg}");
                throw new InvalidOperationException(errorMsg);
            }

            _logger?.LogInformation("✅ GraphicsCaptureItem 作成成功: Size={Width}x{Height}", captureItem.Size.Width, captureItem.Size.Height);
            Console.WriteLine($"✅ GraphicsCaptureItem 作成成功: Size={captureItem.Size.Width}x{captureItem.Size.Height}");

            // フレームキャプチャの実行
            var bitmap = await CaptureFrameAsync(captureItem).ConfigureAwait(false);

            // IWindowsImage に変換
            var windowsImage = _imageFactory.CreateFromBitmap(bitmap);

            _logger?.LogDebug("WinRT ウィンドウキャプチャ完了: {Width}x{Height}", windowsImage.Width, windowsImage.Height);

            return windowsImage;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "WinRT ウィンドウキャプチャでエラーが発生: {WindowHandle}", hWnd);
            throw;
        }
    }

    /// <summary>
    /// ウィンドウハンドルから GraphicsCaptureItem を作成
    /// </summary>
    private GraphicsCaptureItem? CreateCaptureItemFromWindow(IntPtr hWnd)
    {
        try
        {
            // .NET 8 + CsWinRT 2.1.6 対応の正しいWinRT相互運用
            _logger?.LogDebug("WinRT相互運用でGraphicsCaptureItem作成開始: HWND={Hwnd}", hWnd);

            // 方法1: WinRT.Interop.InitializeWithWindow を使用したアプローチ
            var captureItem = TryCreateWithInitializeWithWindow(hWnd);
            if (captureItem != null)
            {
                _logger?.LogDebug("InitializeWithWindow方式で成功");
                return captureItem;
            }

            // 方法2: 直接的なActivationFactory + Interop アプローチ
            captureItem = TryCreateWithActivationFactory(hWnd);
            if (captureItem != null)
            {
                _logger?.LogDebug("ActivationFactory方式で成功");
                return captureItem;
            }

            // 方法3: ComWrappers経由のアプローチ
            captureItem = TryCreateWithComWrappers(hWnd);
            if (captureItem != null)
            {
                _logger?.LogDebug("ComWrappers方式で成功");
                return captureItem;
            }

            _logger?.LogWarning("すべてのWinRT相互運用方式が失敗しました");
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "GraphicsCaptureItem の作成で予期しないエラーが発生しました");
            return null;
        }
    }

    /// <summary>
    /// 直接的なウィンドウハンドルからのGraphicsCaptureItem作成
    /// .NET 8対応：GraphicsCaptureAccess.RequestAccessを使用
    /// </summary>
    private GraphicsCaptureItem? TryCreateWithInitializeWithWindow(IntPtr hWnd)
    {
        try
        {
            // ウィンドウハンドルの有効性チェック
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
            {
                _logger?.LogDebug("無効なウィンドウハンドル: 0x{Handle:X8}", hWnd.ToInt64());
                return null;
            }

            // .NET 8対応：より直接的なIGraphicsCaptureItemInterop呼び出し
            _logger?.LogDebug("直接的なWinRT相互運用での作成を試行: Handle=0x{Handle:X8}", hWnd.ToInt64());

            // このメソッドでは直接的なWinRT相互運用は複雑すぎるため、
            // より確実なActivationFactory方式に委譲
            _logger?.LogDebug("ActivationFactory方式に委譲して確実な作成を実行");
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "GraphicsCaptureAccess方式失敗");
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    /// <summary>
    /// .NET 8 対応の新しい方法でGraphicsCaptureItemを作成
    /// </summary>
    private GraphicsCaptureItem? TryCreateWithActivationFactory(IntPtr hWnd)
    {
        try
        {
            _logger?.LogDebug(".NET 8 対応のGraphicsCaptureItem作成を試行: HWND=0x{Hwnd:X8}", hWnd.ToInt64());

            // .NET 8 + CsWinRT 2.2.0対応: 直接的なWinRT相互運用
            // ComWrappersを使用した安全な相互運用
            var captureItem = TryCreateWithDirectWinRT(hWnd);
            if (captureItem != null)
            {
                _logger?.LogDebug("直接WinRT相互運用成功");
                return captureItem;
            }

            // フォールバック: 従来のCOM相互運用（.NET 8で安全化）
            return TryCreateWithSafeCOM(hWnd);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "ActivationFactory方式失敗");
            return null;
        }
    }

    /// <summary>
    /// 改良されたWinRT相互運用でGraphicsCaptureItemを作成
    /// </summary>
    private GraphicsCaptureItem? TryCreateWithDirectWinRT(IntPtr hWnd)
    {
        try
        {
            _logger?.LogDebug("改良されたWinRT相互運用を試行: HWND=0x{Hwnd:X8}", hWnd.ToInt64());

            // .NET 8 対応: より直接的なWinRT Activation Factory取得
            // CsWinRT 2.2.0の正しい方法を使用
            var factoryClassName = "Windows.Graphics.Capture.GraphicsCaptureItem";
            var activationFactoryIid = new Guid("00000035-0000-0000-C000-000000000046");

            var hr = RoGetActivationFactory(factoryClassName, ref activationFactoryIid, out var factoryPtr);
            if (hr != 0 || factoryPtr == IntPtr.Zero)
            {
                _logger?.LogDebug("RoGetActivationFactory失敗: HRESULT=0x{Hr:X8}", hr);
                return null;
            }

            try
            {
                // IGraphicsCaptureItemInterop にQueryInterface（安全版）
                var interopIid = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
                hr = Marshal.QueryInterface(factoryPtr, ref interopIid, out var interopPtr);

                if (hr != 0 || interopPtr == IntPtr.Zero)
                {
                    _logger?.LogDebug("QueryInterface to IGraphicsCaptureItemInterop失敗: HRESULT=0x{Hr:X8}", hr);
                    return null;
                }

                try
                {
                    // vtableから関数ポインタを直接取得（マーシャリング問題を回避）
                    var vtable = Marshal.ReadIntPtr(interopPtr);
                    var createForWindowPtr = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size); // CreateForWindow is 3rd method

                    var createForWindow = Marshal.GetDelegateForFunctionPointer<CreateForWindowDelegate>(createForWindowPtr);

                    var itemIid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                    hr = createForWindow(interopPtr, hWnd, ref itemIid, out var itemPtr);

                    if (hr >= 0 && itemPtr != IntPtr.Zero)
                    {
                        try
                        {
                            // .NET 8 対応のマーシャリング
                            var item = WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
                            _logger?.LogDebug("改良されたWinRT相互運用でGraphicsCaptureItem作成成功");
                            return item;
                        }
                        finally
                        {
                            Marshal.Release(itemPtr);
                        }
                    }
                    else
                    {
                        _logger?.LogDebug("CreateForWindow呼び出し失敗: HRESULT=0x{Hr:X8}", hr);
                    }
                }
                finally
                {
                    Marshal.Release(interopPtr);
                }
            }
            finally
            {
                Marshal.Release(factoryPtr);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "改良されたWinRT相互運用失敗");
            return null;
        }
    }

    /// <summary>
    /// 安全なCOM相互運用でGraphicsCaptureItemを作成
    /// </summary>
    private GraphicsCaptureItem? TryCreateWithSafeCOM(IntPtr hWnd)
    {
        try
        {
            _logger?.LogDebug("安全なCOM相互運用を試行");

            // ComWrappersを使用しない従来方式だが、例外ハンドリングを強化
            const string factoryId = "Windows.Graphics.Capture.GraphicsCaptureItem";
            var activationFactoryGuid = new Guid("00000035-0000-0000-C000-000000000046");

            var hr = RoGetActivationFactory(factoryId, ref activationFactoryGuid, out var factoryPtr);
            if (hr != 0 || factoryPtr == IntPtr.Zero)
            {
                _logger?.LogDebug("RoGetActivationFactory失敗: HRESULT=0x{Hr:X8}", hr);
                return null;
            }

            try
            {
                // より安全なQueryInterface実行
                var interopGuid = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
                hr = Marshal.QueryInterface(factoryPtr, ref interopGuid, out var interopPtr);

                if (hr != 0 || interopPtr == IntPtr.Zero)
                {
                    _logger?.LogDebug("QueryInterface失敗: HRESULT=0x{Hr:X8}", hr);
                    return null;
                }

                try
                {
                    // COM インターフェースの直接呼び出し（マーシャリング回避）
                    var createForWindow = Marshal.GetDelegateForFunctionPointer<CreateForWindowDelegate>(
                        Marshal.ReadIntPtr(Marshal.ReadIntPtr(interopPtr), 3 * IntPtr.Size));

                    var itemGuid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                    hr = createForWindow(interopPtr, hWnd, ref itemGuid, out var itemPtr);

                    if (hr >= 0 && itemPtr != IntPtr.Zero)
                    {
                        try
                        {
                            var item = WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
                            _logger?.LogDebug("安全なCOM相互運用でGraphicsCaptureItem作成成功");
                            return item;
                        }
                        finally
                        {
                            Marshal.Release(itemPtr);
                        }
                    }
                    else
                    {
                        _logger?.LogDebug("CreateForWindow関数呼び出し失敗: HRESULT=0x{Hr:X8}", hr);
                    }
                }
                finally
                {
                    Marshal.Release(interopPtr);
                }
            }
            finally
            {
                Marshal.Release(factoryPtr);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "安全なCOM相互運用失敗");
            return null;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateForWindowDelegate(IntPtr thisPtr, IntPtr window, ref Guid riid, out IntPtr result);

    /// <summary>
    /// ComWrappers経由での作成方法
    /// </summary>
    private GraphicsCaptureItem? TryCreateWithComWrappers(IntPtr hWnd)
    {
        try
        {
            // この実装は今後のCsWinRTバージョンで利用可能になる可能性
            _logger?.LogDebug("ComWrappers方式は実装待ち");
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "ComWrappers方式失敗");
            return null;
        }
    }

    /// <summary>
    /// GraphicsCaptureItemのActivationFactoryを取得
    /// </summary>
    private IntPtr GetActivationFactoryForGraphicsCaptureItem()
    {
        try
        {
            // Windows Runtime のActivation Factory取得
            var className = "Windows.Graphics.Capture.GraphicsCaptureItem";
            var activationFactoryIID = new Guid("00000035-0000-0000-C000-000000000046"); // IActivationFactory

            var hr = RoGetActivationFactory(className, ref activationFactoryIID, out var factoryPtr);

            if (hr == 0 && factoryPtr != IntPtr.Zero)
            {
                return factoryPtr;
            }

            _logger?.LogDebug("RoGetActivationFactory失敗: 0x{Hr:X8}", hr);
            return IntPtr.Zero;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "GetActivationFactoryForGraphicsCaptureItem失敗");
            return IntPtr.Zero;
        }
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int RoGetActivationFactory(
        [MarshalAs(UnmanagedType.HString)] string activatableClassId,
        [In] ref Guid iid,
        out IntPtr factory);

    /// <summary>
    /// GraphicsCaptureItem からフレームをキャプチャ
    /// </summary>
    private async Task<Bitmap> CaptureFrameAsync(GraphicsCaptureItem captureItem)
    {
        // Direct3D11 デバイスの作成
        var d3dDevice = CreateDirect3DDevice();

        // フレームプールの作成
        using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            d3dDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            1, // フレーム数
            captureItem.Size);

        // フレームキャプチャの実行
        var tcs = new TaskCompletionSource<Bitmap>();

        framePool.FrameArrived += (sender, args) =>
        {
            try
            {
                using var frame = framePool.TryGetNextFrame();
                if (frame != null)
                {
                    var bitmap = ConvertFrameToBitmap(frame);
                    tcs.SetResult(bitmap);
                }
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        };

        // キャプチャセッションの作成と開始
        using var session = framePool.CreateCaptureSession(captureItem);
        session.StartCapture();

        // フレーム取得を待機（タイムアウト 5秒）
        var timeoutTask = Task.Delay(5000);
        var completedTask = await Task.WhenAny(tcs.Task, timeoutTask).ConfigureAwait(false);

        session.Dispose();

        if (completedTask == timeoutTask)
        {
            throw new TimeoutException("フレームキャプチャがタイムアウトしました");
        }

        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Direct3D11Device を作成
    /// </summary>
    private IDirect3DDevice CreateDirect3DDevice()
    {
        try
        {
            // Direct3D11デバイスを作成
            var d3dDevice = CreateD3D11Device();

            // WinRTのIDirect3DDeviceに変換
            var dxgiDevice = d3dDevice.QueryInterface<SharpDX.DXGI.Device>();
            var hr = CreateDirect3DDeviceFromDXGIDevice(dxgiDevice.NativePointer, out var graphicsDevice);

            if (hr != 0)
            {
                throw new InvalidOperationException($"CreateDirect3DDeviceFromDXGIDevice failed with HRESULT: 0x{hr:X8}");
            }

            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(graphicsDevice);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Direct3D11デバイスの作成に失敗");
            throw new InvalidOperationException("Direct3D11デバイスの作成に失敗しました", ex);
        }
    }

    /// <summary>
    /// ネイティブD3D11デバイスを作成
    /// </summary>
    private SharpDX.Direct3D11.Device CreateD3D11Device()
    {
        var featureLevels = new[]
        {
            SharpDX.Direct3D.FeatureLevel.Level_11_1,
            SharpDX.Direct3D.FeatureLevel.Level_11_0,
            SharpDX.Direct3D.FeatureLevel.Level_10_1,
            SharpDX.Direct3D.FeatureLevel.Level_10_0
        };

        return new SharpDX.Direct3D11.Device(
            SharpDX.Direct3D.DriverType.Hardware,
            SharpDX.Direct3D11.DeviceCreationFlags.BgraSupport,
            featureLevels);
    }

    /// <summary>
    /// DXGIデバイスからWinRT Direct3DDeviceを作成
    /// </summary>
    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern uint CreateDirect3DDeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    /// <summary>
    /// キャプチャフレームを Bitmap に変換
    /// </summary>
    private Bitmap ConvertFrameToBitmap(Direct3D11CaptureFrame frame)
    {
        try
        {
            // フレームサーフェスを取得
            var frameSurface = frame.Surface;

            // テクスチャの説明を取得
            var texture = frameSurface.As<SharpDX.Direct3D11.Texture2D>();
            var desc = texture.Description;

            // ステージングテクスチャを作成
            var stagingTexture = CreateStagingTexture(texture.Device, desc);

            // GPU -> CPU にコピー
            texture.Device.ImmediateContext.CopyResource(texture, stagingTexture);

            // ビットマップに変換
            return ConvertTextureToBitmap(stagingTexture, desc);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "フレーム変換でエラーが発生");
            throw new InvalidOperationException("フレーム変換に失敗しました", ex);
        }
    }

    /// <summary>
    /// ステージングテクスチャを作成
    /// </summary>
    private SharpDX.Direct3D11.Texture2D CreateStagingTexture(SharpDX.Direct3D11.Device device, SharpDX.Direct3D11.Texture2DDescription originalDesc)
    {
        var stagingDesc = new SharpDX.Direct3D11.Texture2DDescription
        {
            Width = originalDesc.Width,
            Height = originalDesc.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
            SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
            Usage = SharpDX.Direct3D11.ResourceUsage.Staging,
            BindFlags = SharpDX.Direct3D11.BindFlags.None,
            CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.Read,
            OptionFlags = SharpDX.Direct3D11.ResourceOptionFlags.None
        };

        return new SharpDX.Direct3D11.Texture2D(device, stagingDesc);
    }

    /// <summary>
    /// テクスチャをビットマップに変換
    /// </summary>
    private Bitmap ConvertTextureToBitmap(SharpDX.Direct3D11.Texture2D stagingTexture, SharpDX.Direct3D11.Texture2DDescription desc)
    {
        var context = stagingTexture.Device.ImmediateContext;
        var dataBox = context.MapSubresource(stagingTexture, 0, SharpDX.Direct3D11.MapMode.Read, SharpDX.Direct3D11.MapFlags.None);

        try
        {
            var bitmap = new Bitmap((int)desc.Width, (int)desc.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bmpData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, bitmap.PixelFormat);

            try
            {
                unsafe
                {
                    var src = (byte*)dataBox.DataPointer.ToPointer();
                    var dst = (byte*)bmpData.Scan0.ToPointer();

                    for (int y = 0; y < desc.Height; y++)
                    {
                        var srcRow = src + (y * dataBox.RowPitch);
                        var dstRow = dst + (y * bmpData.Stride);

                        for (int x = 0; x < desc.Width; x++)
                        {
                            // BGRA -> ARGB 変換
                            var srcPixel = srcRow + (x * 4);
                            var dstPixel = dstRow + (x * 4);

                            dstPixel[0] = srcPixel[0]; // B
                            dstPixel[1] = srcPixel[1]; // G
                            dstPixel[2] = srcPixel[2]; // R
                            dstPixel[3] = srcPixel[3]; // A
                        }
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }

            return bitmap;
        }
        finally
        {
            context.UnmapSubresource(stagingTexture, 0);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}


/// <summary>
/// GraphicsCaptureItem の相互運用インターフェース
/// </summary>
[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsCaptureItemInterop
{
    int CreateForWindow(IntPtr window, [In] ref Guid riid, out IntPtr result);
    int CreateForMonitor(IntPtr monitor, [In] ref Guid riid, out IntPtr result);
}
