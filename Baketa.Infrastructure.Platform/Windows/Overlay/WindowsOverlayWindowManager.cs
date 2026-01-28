using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Baketa.Core.Settings;
using Baketa.Core.UI.Overlay;
using Baketa.Infrastructure.Platform.Windows.NativeMethods;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CoreGeometry = Baketa.Core.UI.Geometry;

namespace Baketa.Infrastructure.Platform.Windows.Overlay;

/// <summary>
/// Windows用オーバーレイウィンドウ管理サービス
/// </summary>
/// <remarks>
/// 🔥 [DWM_BLUR_IMPLEMENTATION] DWM Composition対応版
/// - OverlaySettings.UseComposition に基づいてウィンドウタイプを選択
/// - DWM非サポート環境では自動的にLayeredウィンドウにフォールバック
/// - 既存のLayeredOverlayWindow実装と完全な互換性を維持
///
/// 🔥 [GEMINI_REVIEW] IDisposable実装
/// - 正常なスレッド終了とリソース解放を保証
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsOverlayWindowManager : IOverlayWindowManager, IDisposable
{
    private readonly ILogger<WindowsOverlayWindowManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILayeredOverlayWindowFactory _layeredWindowFactory;
    private readonly ICompositionOverlayWindowFactory? _compositionWindowFactory;
    private readonly OverlaySettings _overlaySettings;
    private readonly ConcurrentDictionary<nint, IOverlayWindow> _activeOverlays = new();

    public WindowsOverlayWindowManager(
        ILogger<WindowsOverlayWindowManager> logger,
        ILoggerFactory loggerFactory,
        ILayeredOverlayWindowFactory layeredWindowFactory,
        ICompositionOverlayWindowFactory? compositionWindowFactory,
        IOptions<OverlaySettings> overlaySettings)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _layeredWindowFactory = layeredWindowFactory ?? throw new ArgumentNullException(nameof(layeredWindowFactory));
        _compositionWindowFactory = compositionWindowFactory; // Optional: null for environments without DWM support
        _overlaySettings = overlaySettings?.Value ?? throw new ArgumentNullException(nameof(overlaySettings));
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
                // 🔥 [DWM_BLUR_IMPLEMENTATION] ウィンドウタイプの選択ロジック
                var useComposition = ShouldUseCompositionMode();
                var windowTypeName = useComposition ? "CompositionOverlayWindow" : "LayeredOverlayWindow";

                _logger.LogDebug("🔥 [DWM_BLUR_IMPLEMENTATION] Creating {WindowType}. Target: {Target}, Size: {Size}, Position: {Position}",
                    windowTypeName, targetWindowHandle, initialSize, initialPosition);

                // ファクトリーを使ってウィンドウを作成
                ILayeredOverlayWindow underlyingWindow = useComposition
                    ? _compositionWindowFactory!.Create()
                    : _layeredWindowFactory.Create();

                // 🔥 [ADAPTER_PATTERN] LayeredOverlayWindowAdapterでIOverlayWindowに適応
                var adapter = new LayeredOverlayWindowAdapter(underlyingWindow)
                {
                    Size = initialSize,
                    Position = initialPosition,
                    TargetWindowHandle = targetWindowHandle
                };

                _activeOverlays.TryAdd(adapter.Handle, adapter);

                _logger.LogInformation("✅ [DWM_BLUR_IMPLEMENTATION] {WindowType} created successfully. Handle: {Handle}",
                    windowTypeName, adapter.Handle);

                return adapter;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "❌ Failed to create overlay window due to invalid operation");
                throw;
            }
            catch (ExternalException ex)
            {
                _logger.LogError(ex, "❌ Failed to create overlay window due to external error");
                throw;
            }
            catch (OutOfMemoryException ex)
            {
                _logger.LogError(ex, "❌ Failed to create overlay window due to insufficient memory");
                throw;
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// DWM Compositionモードを使用すべきか判定
    /// </summary>
    /// <returns>Compositionモードを使用する場合 true</returns>
    /// <remarks>
    /// 🔥 [DWM_BLUR_IMPLEMENTATION] フォールバック戦略:
    /// 1. OverlaySettings.EnableClickThrough が true → Layeredモード（クリックスルー優先）
    /// 2. OverlaySettings.UseComposition が false → Layeredモード
    /// 3. CompositionWindowFactory が null → Layeredモード（DI未登録）
    /// 4. DWM Compositionがサポートされていない → Layeredモード（Windows XP等）
    /// 5. 上記すべてクリア → Compositionモード
    ///
    /// ⚠️ [WINDOWS_API_CONSTRAINT] ブラー効果とクリックスルーは共存不可能
    /// - DWM Composition (ブラー効果): WS_EX_LAYERED と互換性なし
    /// - クリックスルー: WS_EX_LAYERED + UpdateLayeredWindow が必要
    /// - EnableClickThrough=true の場合、ブラー効果は自動的に無効化される
    /// </remarks>
    private bool ShouldUseCompositionMode()
    {
        // クリックスルーが有効な場合、Layeredモード必須（ブラー効果と共存不可）
        if (_overlaySettings.EnableClickThrough)
        {
            _logger.LogDebug("🔥 [CLICK_THROUGH_PRIORITY] EnableClickThrough=true: Layeredモードを使用（ブラー効果無効）");
            return false;
        }

        // 設定で無効化されている場合
        if (!_overlaySettings.UseComposition)
        {
            _logger.LogDebug("🔥 [DWM_BLUR_IMPLEMENTATION] UseComposition=false: Layeredモードを使用");
            return false;
        }

        // CompositionWindowFactoryが登録されていない場合
        if (_compositionWindowFactory == null)
        {
            _logger.LogWarning("⚠️ [DWM_BLUR_IMPLEMENTATION] CompositionWindowFactory未登録: Layeredモードにフォールバック");
            return false;
        }

        // DWM Compositionのサポート確認
        if (!DwmApiMethods.IsCompositionSupported())
        {
            _logger.LogWarning("⚠️ [DWM_BLUR_IMPLEMENTATION] DWM Composition未サポート: Layeredモードにフォールバック");
            return false;
        }

        // すべてクリア: Compositionモードを使用
        _logger.LogDebug("🔥 [DWM_BLUR_IMPLEMENTATION] UseComposition=true & DWM supported: Compositionモードを使用");
        return true;
    }

    /// <inheritdoc/>
    public IOverlayWindow? GetOverlayWindow(nint handle)
    {
        _activeOverlays.TryGetValue(handle, out var overlay);
        return overlay;
    }

    /// <inheritdoc/>
    public async Task CloseAllOverlaysAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Closing {Count} active overlays", _activeOverlays.Count);

        var tasks = new List<Task>();

        foreach (var (handle, overlay) in _activeOverlays)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    overlay.Dispose();
                    _activeOverlays.TryRemove(handle, out _);
                    _logger.LogDebug("Overlay {Handle} closed successfully", handle);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("Overlay {Handle} close cancelled", handle);
                    throw;
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogError(ex, "Invalid operation while closing overlay {Handle}", handle);
                }
                catch (ExternalException ex)
                {
                    _logger.LogError(ex, "External error while closing overlay {Handle}", handle);
                }
            }, cancellationToken));
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
            _logger.LogInformation("All overlays closed");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("CloseAllOverlaysAsync was cancelled");
            throw;
        }
    }

    /// <inheritdoc/>
    public int ActiveOverlayCount => _activeOverlays.Count;

    /// <summary>
    /// 🔥 [GEMINI_REVIEW] リソースの解放とスレッドの正常終了
    /// </summary>
    /// <remarks>
    /// すべてのオーバーレイウィンドウを破棄し、専用UIスレッドを正常終了させます。
    /// アプリケーション終了時に呼び出してリソースリークを防ぎます。
    /// </remarks>
    public void Dispose()
    {
        _logger.LogInformation("🔥 [DWM_BLUR_IMPLEMENTATION] Disposing WindowsOverlayWindowManager...");

        try
        {
            // すべてのオーバーレイを閉じる（既存のメソッドを使用）
            CloseAllOverlaysAsync().GetAwaiter().GetResult();

            _logger.LogInformation("✅ [DWM_BLUR_IMPLEMENTATION] All overlays disposed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [DWM_BLUR_IMPLEMENTATION] Error during WindowsOverlayWindowManager disposal");
        }
    }
}
