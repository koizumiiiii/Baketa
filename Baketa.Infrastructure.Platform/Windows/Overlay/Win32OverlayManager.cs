using System;
using System.Collections.Concurrent;
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
        // 🚨 [ULTRATHINK_WIN32_TRACE1] Win32OverlayManager.ShowAsync開始トレースログ
        var timestamp1 = DateTime.Now.ToString("HH:mm:ss.fff");
        var threadId1 = Environment.CurrentManagedThreadId;
        System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log"),
            $"[{timestamp1}][T{threadId1:D2}] 🔥 [ULTRATHINK_WIN32_TRACE1] Win32OverlayManager.ShowAsync開始 - Position: ({position.X},{position.Y},{position.Width}x{position.Height})\r\n");

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

            // コンテンツを設定（現時点ではテキストのみ、将来的にスタイル情報も適用）
            overlayWindow.UpdateContent(content.Text);

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
    public async Task HideAllAsync()
    {
        try
        {
            var overlayCount = _activeOverlays.Count;
            _logger.LogInformation("全Win32オーバーレイ非表示開始: Count={Count}", overlayCount);

            // WindowsOverlayWindowManagerで全オーバーレイを閉じる
            await _windowsOverlayWindowManager.CloseAllOverlaysAsync().ConfigureAwait(false);

            // アクティブオーバーレイディクショナリをクリア
            // (WindowsOverlayWindowManagerが既に破棄しているため、Disposeは不要)
            _activeOverlays.Clear();

            _logger.LogInformation("全Win32オーバーレイ非表示完了: {Count}個のオーバーレイを閉じました", overlayCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "全Win32オーバーレイ非表示中にエラーが発生しました");
            throw;
        }
    }

    /// <inheritdoc/>
    public int ActiveOverlayCount => _windowsOverlayWindowManager.ActiveOverlayCount;
}
