using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.UI.Overlays;
using Baketa.Core.UI.Overlay;
using Microsoft.Extensions.Logging;
using CoreGeometry = Baketa.Core.UI.Geometry;

namespace Baketa.Infrastructure.Platform.Windows.Overlay;

/// <summary>
/// Win32オーバーレイウィンドウマネージャーをIOverlayManagerインターフェースにアダプトするクラス
/// 既存のWindowsOverlayWindowManagerを新しい統一インターフェースにブリッジ
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Win32OverlayManager : IOverlayManager
{
    private readonly IOverlayWindowManager _windowsOverlayWindowManager;
    private readonly ILogger<Win32OverlayManager> _logger;
    private readonly ConcurrentDictionary<string, Win32Overlay> _activeOverlays = new();

    /// <summary>
    /// Win32OverlayManagerの新しいインスタンスを初期化します
    /// </summary>
    /// <param name="windowsOverlayWindowManager">ラップするWin32オーバーレイウィンドウマネージャー</param>
    /// <param name="logger">ロガー</param>
    public Win32OverlayManager(
        IOverlayWindowManager windowsOverlayWindowManager,
        ILogger<Win32OverlayManager> logger)
    {
        _windowsOverlayWindowManager = windowsOverlayWindowManager ?? throw new ArgumentNullException(nameof(windowsOverlayWindowManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<IOverlay> ShowAsync(OverlayContent content, OverlayPosition position)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(position);

        try
        {
            _logger.LogDebug("Win32オーバーレイ作成開始: Position=({X},{Y}), Size=({Width}x{Height})",
                position.X, position.Y, position.Width, position.Height);

            // OverlayPositionからCore.UI.GeometryのSizeとPointに変換
            var initialSize = new CoreGeometry.Size(position.Width, position.Height);
            var initialPosition = new CoreGeometry.Point(position.X, position.Y);

            // ターゲットウィンドウハンドル（現時点では0、将来的に設定可能にする）
            nint targetWindowHandle = IntPtr.Zero;

            // WindowsOverlayWindowManagerで実際のWin32ウィンドウを作成
            var overlayWindow = await _windowsOverlayWindowManager
                .CreateOverlayWindowAsync(targetWindowHandle, initialSize, initialPosition)
                .ConfigureAwait(false);

            // コンテンツを設定（OverlayContent全体を渡してフォントサイズなどの設定を反映）
            overlayWindow.UpdateContent(content);

            // Win32OverlayでラップしてIOverlayとして返す
            var win32Overlay = new Win32Overlay(overlayWindow);

            // アクティブオーバーレイディクショナリに追加
            if (!_activeOverlays.TryAdd(win32Overlay.Id, win32Overlay))
            {
                _logger.LogWarning("オーバーレイID {Id} は既に存在します（重複追加）", win32Overlay.Id);
            }

            _logger.LogInformation("Win32オーバーレイ作成成功: Id={Id}, Handle={Handle}",
                win32Overlay.Id, win32Overlay.Handle);

            // オーバーレイを表示
            await win32Overlay.ShowAsync().ConfigureAwait(false);

            return win32Overlay;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Win32オーバーレイ作成中にエラーが発生しました");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task HideAsync(IOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);

        try
        {
            _logger.LogDebug("Win32オーバーレイ非表示開始: Id={Id}", overlay.Id);

            // IOverlayからWin32Overlayにキャスト
            if (overlay is not Win32Overlay win32Overlay)
            {
                throw new InvalidOperationException(
                    $"指定されたオーバーレイはWin32Overlayではありません: {overlay.GetType().FullName}");
            }

            // オーバーレイを非表示
            await win32Overlay.HideAsync().ConfigureAwait(false);

            // アクティブオーバーレイディクショナリから削除
            if (_activeOverlays.TryRemove(win32Overlay.Id, out _))
            {
                _logger.LogDebug("オーバーレイをアクティブリストから削除: Id={Id}", win32Overlay.Id);
            }

            // オーバーレイを破棄
            win32Overlay.Dispose();

            _logger.LogInformation("Win32オーバーレイ非表示完了: Id={Id}", overlay.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Win32オーバーレイ非表示中にエラーが発生しました: Id={Id}", overlay.Id);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task HideAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var overlayCount = _activeOverlays.Count;
            _logger.LogInformation("全Win32オーバーレイ非表示開始: Count={Count}", overlayCount);

            // WindowsOverlayWindowManagerで全オーバーレイを閉じる
            await _windowsOverlayWindowManager.CloseAllOverlaysAsync(cancellationToken).ConfigureAwait(false);

            // アクティブオーバーレイディクショナリをクリア
            // (WindowsOverlayWindowManagerが既に破棄しているため、Disposeは不要)
            _activeOverlays.Clear();

            _logger.LogInformation("全Win32オーバーレイ非表示完了: {Count}個のオーバーレイを閉じました", overlayCount);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("全Win32オーバーレイ非表示がキャンセルされました");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "全Win32オーバーレイ非表示中にエラーが発生しました");
            throw;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// [Issue #408] Win32Overlayの位置情報を使用して領域指定削除を実行。
    /// 指定された領域と交差するオーバーレイのみを非表示・破棄する。
    /// </remarks>
    public async Task HideOverlaysInAreaAsync(Rectangle area, int excludeChunkId, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var overlaysToRemove = new List<(string Key, Win32Overlay Overlay)>();

            foreach (var kvp in _activeOverlays)
            {
                var pos = kvp.Value.Position;
                var overlayRect = new Rectangle(pos.X, pos.Y, pos.Width, pos.Height);

                if (overlayRect.IntersectsWith(area))
                {
                    overlaysToRemove.Add((kvp.Key, kvp.Value));
                }
            }

            if (overlaysToRemove.Count == 0)
            {
                _logger.LogDebug("[Issue #408] HideOverlaysInAreaAsync - 交差するオーバーレイなし: Area={Area}", area);
                return;
            }

            _logger.LogDebug("[Issue #408] HideOverlaysInAreaAsync - 領域指定削除: Area={Area}, 対象={Count}/{Total}",
                area, overlaysToRemove.Count, _activeOverlays.Count);

            foreach (var (key, overlay) in overlaysToRemove)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await overlay.HideAsync().ConfigureAwait(false);
                if (_activeOverlays.TryRemove(key, out _))
                {
                    overlay.Dispose();
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #408] HideOverlaysInAreaAsync エラー: Area={Area}", area);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task SetAllVisibilityAsync(bool isVisible, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var overlayCount = _activeOverlays.Count;
            _logger.LogInformation("全Win32オーバーレイ可視性変更開始: IsVisible={IsVisible}, Count={Count}",
                isVisible, overlayCount);

            // 全てのアクティブオーバーレイに対して可視性を設定
            var tasks = new List<Task>();
            foreach (var overlay in _activeOverlays.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var task = isVisible ? overlay.ShowAsync() : overlay.HideAsync();
                tasks.Add(task);
            }

            // すべての可視性変更を並列実行
            await Task.WhenAll(tasks).ConfigureAwait(false);

            _logger.LogInformation("全Win32オーバーレイ可視性変更完了: IsVisible={IsVisible}, {Count}個のオーバーレイ",
                isVisible, overlayCount);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("全Win32オーバーレイ可視性変更がキャンセルされました: IsVisible={IsVisible}", isVisible);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "全Win32オーバーレイ可視性変更中にエラーが発生しました: IsVisible={IsVisible}",
                isVisible);
            throw;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// [Issue #481] Win32OverlayManager自身の_activeOverlaysから取得。
    /// WindowsOverlayWindowManagerに委譲すると、HideOverlaysInAreaAsync()で
    /// 削除されたオーバーレイがWindowsOverlayWindowManager側に残り、
    /// ActiveOverlayCountが実際より多くなる不整合が発生するため。
    /// </remarks>
    public int ActiveOverlayCount => _activeOverlays.Count;
}
