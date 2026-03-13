using System.Runtime.Versioning;
using Baketa.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.Windows.Overlay;

/// <summary>
/// [Issue #497] PhantomCursorWindow を IPhantomCursorWindowAdapter として公開するアダプター
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class PhantomCursorWindowAdapter : IPhantomCursorWindowAdapter
{
    private readonly PhantomCursorWindow _window;

    public PhantomCursorWindowAdapter(ILogger logger)
    {
        _window = new PhantomCursorWindow(logger);
    }

    public void UpdatePosition(int screenX, int screenY) => _window.UpdatePosition(screenX, screenY);
    public void Show() => _window.Show();
    public void Hide() => _window.Hide();
    public void Dispose() => _window.Dispose();
}
