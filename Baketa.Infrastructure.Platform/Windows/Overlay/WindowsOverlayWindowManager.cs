using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Baketa.Core.UI.Overlay;
using CoreGeometry = Baketa.Core.UI.Geometry;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.Windows.Overlay;

/// <summary>
/// Windows用オーバーレイウィンドウ管理サービス
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsOverlayWindowManager : IOverlayWindowManager
{
    private readonly ILogger<WindowsOverlayWindowManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<nint, IOverlayWindow> _activeOverlays = new();
    
    public WindowsOverlayWindowManager(ILogger<WindowsOverlayWindowManager> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
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
                _logger.LogDebug("Creating overlay window. Target: {Target}, Size: {Size}, Position: {Position}",
                    targetWindowHandle, initialSize, initialPosition);
                
                var overlay = new WindowsOverlayWindow(
                    initialSize,
                    initialPosition,
                    targetWindowHandle,
                    _loggerFactory.CreateLogger<WindowsOverlayWindow>());
                
                // IOverlayWindowインターフェース経由でHandleにアクセス
                var overlayInterface = (IOverlayWindow)overlay;
                _activeOverlays.TryAdd(overlayInterface.Handle, overlay);
                
                _logger.LogInformation("Overlay window created successfully. Handle: {Handle}", overlayInterface.Handle);
                
                return overlay;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Failed to create overlay window due to invalid operation");
                throw;
            }
            catch (ExternalException ex)
            {
                _logger.LogError(ex, "Failed to create overlay window due to external error");
                throw;
            }
            catch (OutOfMemoryException ex)
            {
                _logger.LogError(ex, "Failed to create overlay window due to insufficient memory");
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