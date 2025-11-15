using System;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Platform.Windows;
using IWindowsImageFactoryInterface = Baketa.Core.Abstractions.Factories.IWindowsImageFactory;

namespace Baketa.Infrastructure.Platform.Windows;

/// <summary>
/// IWindowsCapturerインターフェースのスタブ実装
/// 注：実際の機能実装は後の段階で行います
/// </summary>
public class WindowsCapturerStub : IWindowsCapturer
{
    private readonly IWindowsImageFactoryInterface _imageFactory;
    private WindowsCaptureOptions _options = new();

    /// <summary>
    /// WindowsCapturerStubのコンストラクタ
    /// </summary>
    /// <param name="imageFactory">Windows画像ファクトリー</param>
    public WindowsCapturerStub(IWindowsImageFactoryInterface imageFactory)
    {
        _imageFactory = imageFactory ?? throw new ArgumentNullException(nameof(imageFactory));
    }

    /// <summary>
    /// 画面全体をキャプチャします
    /// </summary>
    /// <returns>キャプチャした画像</returns>
    public async Task<IWindowsImage> CaptureScreenAsync()
    {
        // スタブ実装では単に空のビットマップを返す
        return await _imageFactory.CreateEmptyAsync(800, 600).ConfigureAwait(false);
    }

    /// <summary>
    /// 指定した領域をキャプチャします
    /// </summary>
    /// <param name="region">キャプチャする領域</param>
    /// <returns>キャプチャした画像</returns>
    public async Task<IWindowsImage> CaptureRegionAsync(Rectangle region)
    {
        // スタブ実装では単にサイズ指定で空のビットマップを返す
        return await _imageFactory.CreateEmptyAsync(
            Math.Max(1, region.Width),
            Math.Max(1, region.Height)).ConfigureAwait(false);
    }

    /// <summary>
    /// 指定したウィンドウをキャプチャします
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>キャプチャした画像</returns>
    public async Task<IWindowsImage> CaptureWindowAsync(IntPtr windowHandle)
    {
        // スタブ実装では単に空のビットマップを返す
        return await _imageFactory.CreateEmptyAsync(640, 480).ConfigureAwait(false);
    }

    /// <summary>
    /// 指定したウィンドウのクライアント領域をキャプチャします
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>キャプチャした画像</returns>
    public async Task<IWindowsImage> CaptureClientAreaAsync(IntPtr windowHandle)
    {
        // スタブ実装では単に空のビットマップを返す
        return await _imageFactory.CreateEmptyAsync(640, 480).ConfigureAwait(false);
    }

    /// <summary>
    /// キャプチャオプションを設定します
    /// </summary>
    /// <param name="options">キャプチャオプション</param>
    public void SetCaptureOptions(WindowsCaptureOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// 現在のキャプチャオプションを取得します
    /// </summary>
    /// <returns>キャプチャオプション</returns>
    public WindowsCaptureOptions GetCaptureOptions()
    {
        return _options;
    }
}
