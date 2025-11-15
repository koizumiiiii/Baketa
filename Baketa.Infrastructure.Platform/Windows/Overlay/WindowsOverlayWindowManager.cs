using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Baketa.Core.UI.Overlay;
using Microsoft.Extensions.Logging;
using CoreGeometry = Baketa.Core.UI.Geometry;

namespace Baketa.Infrastructure.Platform.Windows.Overlay;

/// <summary>
/// Windows用オーバーレイウィンドウ管理サービス
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsOverlayWindowManager : IOverlayWindowManager
{
    private readonly ILogger<WindowsOverlayWindowManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILayeredOverlayWindowFactory _layeredWindowFactory;
    private readonly ConcurrentDictionary<nint, IOverlayWindow> _activeOverlays = new();

    public WindowsOverlayWindowManager(
        ILogger<WindowsOverlayWindowManager> logger,
        ILoggerFactory loggerFactory,
        ILayeredOverlayWindowFactory layeredWindowFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _layeredWindowFactory = layeredWindowFactory ?? throw new ArgumentNullException(nameof(layeredWindowFactory));
    }

    /// <inheritdoc/>
    public async Task<IOverlayWindow> CreateOverlayWindowAsync(
        nint targetWindowHandle,
        CoreGeometry.Size initialSize,
        CoreGeometry.Point initialPosition)
    {
        return await Task.Run(() =>
        {
            try
            {
                _logger.LogDebug("🔥 [LAYERED_FIX] Creating LayeredOverlayWindow. Target: {Target}, Size: {Size}, Position: {Position}",
                    targetWindowHandle, initialSize, initialPosition);

                // 🔥 [LAYERED_FIX] LayeredOverlayWindowFactoryを使用してSTAスレッド+メッセージループ対応ウィンドウを作成
                var layeredWindow = _layeredWindowFactory.Create();

                // 🔥 [ADAPTER_PATTERN] LayeredOverlayWindowAdapterでIOverlayWindowに適応
                var adapter = new LayeredOverlayWindowAdapter(layeredWindow)
                {
                    Size = initialSize,
                    Position = initialPosition,
                    TargetWindowHandle = targetWindowHandle
                };

                _activeOverlays.TryAdd(adapter.Handle, adapter);

                _logger.LogInformation("✅ [LAYERED_FIX] LayeredOverlayWindow created successfully with adapter. Handle: {Handle}", adapter.Handle);

                return adapter;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "❌ [LAYERED_FIX] Failed to create overlay window due to invalid operation");
                throw;
            }
            catch (ExternalException ex)
            {
                _logger.LogError(ex, "❌ [LAYERED_FIX] Failed to create overlay window due to external error");
                throw;
            }
            catch (OutOfMemoryException ex)
            {
                _logger.LogError(ex, "❌ [LAYERED_FIX] Failed to create overlay window due to insufficient memory");
                throw;
            }
        }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public IOverlayWindow? GetOverlayWindow(nint handle)
    {
        _activeOverlays.TryGetValue(handle, out var overlay);
        return overlay;
    }

    /// <inheritdoc/>
    public async Task CloseAllOverlaysAsync()
    {
        _logger.LogInformation("Closing {Count} active overlays", _activeOverlays.Count);

        var tasks = new List<Task>();

        foreach (var (handle, overlay) in _activeOverlays)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    overlay.Dispose();
                    _activeOverlays.TryRemove(handle, out _);
                    _logger.LogDebug("Overlay {Handle} closed successfully", handle);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogError(ex, "Invalid operation while closing overlay {Handle}", handle);
                }
                catch (ExternalException ex)
                {
                    _logger.LogError(ex, "External error while closing overlay {Handle}", handle);
                }
            }));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        _logger.LogInformation("All overlays closed");
    }

    /// <inheritdoc/>
    public int ActiveOverlayCount => _activeOverlays.Count;
}
