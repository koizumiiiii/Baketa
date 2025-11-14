using System;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Common;
using Baketa.Infrastructure.Platform.Adapters;
using Moq;

namespace Baketa.Infrastructure.Platform.Tests.Adapters;

/// <summary>
/// テスト用のWindowsImageAdapterスタブクラス
/// </summary>
internal sealed class WindowsImageAdapterStub : DisposableBase, IWindowsImageAdapter
{
    public IAdvancedImage ToAdvancedImage(IWindowsImage windowsImage)
    {
        ArgumentNullException.ThrowIfNull(windowsImage);
        return new Mock<IAdvancedImage>().Object;
    }

    public IImage ToImage(IWindowsImage windowsImage)
    {
        ArgumentNullException.ThrowIfNull(windowsImage);
        return new Mock<IImage>().Object;
    }

    public Task<IWindowsImage> FromAdvancedImageAsync(IAdvancedImage advancedImage)
    {
        ArgumentNullException.ThrowIfNull(advancedImage);
        return Task.FromResult(new Mock<IWindowsImage>().Object);
    }

    public Task<IWindowsImage> FromImageAsync(IImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return Task.FromResult(new Mock<IWindowsImage>().Object);
    }

    public IAdvancedImage CreateAdvancedImageFromBitmap(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        return new Mock<IAdvancedImage>().Object;
    }

    public Task<IAdvancedImage> CreateAdvancedImageFromBytesAsync(byte[] imageData)
    {
        ArgumentNullException.ThrowIfNull(imageData);
        return Task.FromResult(new Mock<IAdvancedImage>().Object);
    }

    public Task<IAdvancedImage> CreateAdvancedImageFromFileAsync(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        return Task.FromResult(new Mock<IAdvancedImage>().Object);
    }

    protected override void DisposeManagedResources()
    {
        // テスト用スタブなので特に何もする必要はない
    }
}
