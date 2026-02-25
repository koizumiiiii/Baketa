using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;
using Baketa.Infrastructure.Platform.Windows.Overlay;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// Win32 Layered Window ベースのインプレース翻訳オーバーレイマネージャー
///
/// [Issue #408] Phase 5: 領域指定オーバーレイ削除対応版
/// 設計原則:
/// - OS-Native透過ウィンドウで角丸・シャドウ問題を根本解決
/// - ILayeredOverlayWindowFactory による依存性注入
/// - ConcurrentDictionary によるスレッドセーフなコレクション管理（ChunkId・Bounds追跡）
/// - 領域指定削除で不要なオーバーレイのみを選択的に削除
/// </summary>
public sealed class SimpleInPlaceOverlayManager : IInPlaceTranslationOverlayManager, IDisposable
{
    private readonly ILayeredOverlayWindowFactory _windowFactory;
    private readonly ILogger<SimpleInPlaceOverlayManager> _logger;

    // [Issue #408] オーバーレイエントリ（ChunkId・Bounds追跡対応）
    private record OverlayEntry(ILayeredOverlayWindow Window, Rectangle Bounds, int ChunkId, DateTime CreatedAt);
    private readonly ConcurrentDictionary<int, OverlayEntry> _activeOverlays = new();
    private int _nextEntryId;

    private bool _disposed;

    public SimpleInPlaceOverlayManager(
        ILayeredOverlayWindowFactory windowFactory,
        ILogger<SimpleInPlaceOverlayManager> logger)
    {
        _windowFactory = windowFactory ?? throw new ArgumentNullException(nameof(windowFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation("[WIN32_OVERLAY] SimpleInPlaceOverlayManager初期化完了");
    }

    /// <summary>
    /// TextChunkの翻訳結果をインプレースオーバーレイで表示
    /// </summary>
    public Task ShowInPlaceOverlayAsync(TextChunk textChunk, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (textChunk == null)
        {
            _logger.LogWarning("[WIN32_OVERLAY] TextChunkがnullのため、オーバーレイ表示をスキップ");
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(textChunk.TranslatedText))
        {
            _logger.LogWarning("[WIN32_OVERLAY] TranslatedTextが空のため、オーバーレイ表示をスキップ - ChunkId: {ChunkId}",
                textChunk.ChunkId);
            return Task.CompletedTask;
        }

        _logger.LogDebug("[WIN32_OVERLAY] オーバーレイ表示開始 - ChunkId: {ChunkId}, Text: {Text}",
            textChunk.ChunkId, textChunk.TranslatedText);

        try
        {
            // Win32 Layered Windowを作成
            var window = _windowFactory.Create();

            // 翻訳テキストを設定
            window.SetText(textChunk.TranslatedText);

            _logger.LogInformation("[WIN32_COORDINATE] CombinedBounds座標: X={X}, Y={Y}, W={W}, H={H} - ChunkId: {ChunkId}",
                textChunk.CombinedBounds.X, textChunk.CombinedBounds.Y,
                textChunk.CombinedBounds.Width, textChunk.CombinedBounds.Height,
                textChunk.ChunkId);

            // マージン計算
            var baseX = textChunk.CombinedBounds.X;
            var baseY = textChunk.CombinedBounds.Y;
            var baseWidth = textChunk.CombinedBounds.Width;
            var baseHeight = textChunk.CombinedBounds.Height;

            var marginWidth = Math.Max(10, (int)(baseWidth * 0.1));
            var marginHeight = Math.Max(5, (int)(baseHeight * 0.1));

            var finalWidth = baseWidth + marginWidth;
            var finalHeight = baseHeight + marginHeight;

            // 画面境界チェック
            var screenBounds = GetScreenBounds(baseX, baseY);

            if (baseX + finalWidth > screenBounds.Right)
            {
                var overflow = (baseX + finalWidth) - screenBounds.Right;
                finalWidth = Math.Max(baseWidth, finalWidth - overflow);
            }

            if (baseY + finalHeight > screenBounds.Bottom)
            {
                var overflow = (baseY + finalHeight) - screenBounds.Bottom;
                finalHeight = Math.Max(baseHeight, finalHeight - overflow);
            }

            if (baseX < screenBounds.Left)
            {
                baseX = screenBounds.Left;
            }

            if (baseY < screenBounds.Top)
            {
                baseY = screenBounds.Top;
            }

            _logger.LogDebug("[MARGIN_CALC] 元: ({BaseW}x{BaseH}) → マージン追加後: ({FinalW}x{FinalH}), 画面: {ScreenBounds}",
                baseWidth, baseHeight, finalWidth, finalHeight, screenBounds);

            // 座標を設定
            window.SetPosition(baseX, baseY);

            // サイズを設定
            if (finalWidth > 0 && finalHeight > 0)
            {
                window.SetSize(finalWidth, finalHeight);
            }
            else
            {
                _logger.LogDebug("[WIN32_OVERLAY] サイズ未指定 - テキストサイズから自動計算");
            }

            // 背景色を設定（すりガラス風半透明白）
            window.SetBackgroundColor(240, 255, 255, 242);

            // ウィンドウを表示
            window.Show();

            // [Issue #408] ConcurrentDictionaryにChunkId・Bounds付きで保存
            var entryId = Interlocked.Increment(ref _nextEntryId);
            var entry = new OverlayEntry(window, textChunk.CombinedBounds, textChunk.ChunkId, DateTime.UtcNow);
            _activeOverlays.TryAdd(entryId, entry);

            _logger.LogInformation("[WIN32_OVERLAY] オーバーレイ表示完了 - ChunkId: {ChunkId}, EntryId: {EntryId}, Pos: ({X}, {Y}), Size: ({W}x{H})",
                textChunk.ChunkId, entryId, textChunk.CombinedBounds.X, textChunk.CombinedBounds.Y,
                textChunk.CombinedBounds.Width, textChunk.CombinedBounds.Height);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WIN32_OVERLAY] オーバーレイ表示中にエラー - ChunkId: {ChunkId}, Error: {Message}",
                textChunk.ChunkId, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// [Issue #408] 指定されたチャンクのインプレースオーバーレイを非表示
    /// ChunkIdで検索し該当のみClose/Dispose
    /// </summary>
    public Task HideInPlaceOverlayAsync(int chunkId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[WIN32_OVERLAY] 指定チャンクのオーバーレイ非表示 - ChunkId: {ChunkId}", chunkId);

        var removedCount = 0;
        foreach (var kvp in _activeOverlays)
        {
            if (kvp.Value.ChunkId == chunkId)
            {
                if (_activeOverlays.TryRemove(kvp.Key, out var entry))
                {
                    CloseAndDisposeWindow(entry.Window);
                    removedCount++;
                }
            }
        }

        _logger.LogDebug("[WIN32_OVERLAY] ChunkId={ChunkId}のオーバーレイ{Count}件を削除", chunkId, removedCount);
        return Task.CompletedTask;
    }

    /// <summary>
    /// [Issue #408] 指定領域内のオーバーレイを非表示にする
    /// Rectangle.IntersectsWithで該当のみ削除
    /// </summary>
    public Task HideOverlaysInAreaAsync(Rectangle area, int excludeChunkId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[WIN32_OVERLAY] 領域内オーバーレイ非表示 - Area: {Area}, Exclude: {ExcludeChunkId}",
            area, excludeChunkId);

        var removedCount = 0;
        foreach (var kvp in _activeOverlays)
        {
            var entry = kvp.Value;

            // 除外ChunkIdはスキップ
            if (entry.ChunkId == excludeChunkId && excludeChunkId >= 0)
                continue;

            // 領域が交差するか判定
            if (entry.Bounds.IntersectsWith(area))
            {
                if (_activeOverlays.TryRemove(kvp.Key, out var removed))
                {
                    CloseAndDisposeWindow(removed.Window);
                    removedCount++;
                }
            }
        }

        _logger.LogDebug("[WIN32_OVERLAY] 領域指定削除完了 - 削除数: {Count}, 残り: {Remaining}",
            removedCount, _activeOverlays.Count);
        return Task.CompletedTask;
    }

    /// <summary>
    /// すべてのインプレースオーバーレイを非表示
    /// </summary>
    public Task HideAllInPlaceOverlaysAsync()
    {
        _logger.LogInformation("[WIN32_OVERLAY] 全オーバーレイ非表示開始 - Count: {Count}", _activeOverlays.Count);

        var entries = _activeOverlays.Values.ToArray();
        _activeOverlays.Clear();

        foreach (var entry in entries)
        {
            CloseAndDisposeWindow(entry.Window);
        }

        _logger.LogInformation("[WIN32_OVERLAY] 全オーバーレイ非表示完了");
        return Task.CompletedTask;
    }

    /// <summary>
    /// すべてのインプレースオーバーレイの可視性を切り替え
    /// </summary>
    public Task SetAllOverlaysVisibilityAsync(bool visible, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[WIN32_OVERLAY] 全オーバーレイ可視性切り替え - Visible: {Visible}", visible);

        foreach (var entry in _activeOverlays.Values)
        {
            try
            {
                if (visible)
                {
                    entry.Window.Show();
                }
                else
                {
                    entry.Window.Hide();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[WIN32_OVERLAY] ウィンドウ可視性切り替え中にエラー: {Message}", ex.Message);
            }
        }

        _logger.LogInformation("[WIN32_OVERLAY] 全オーバーレイ可視性切り替え完了");
        return Task.CompletedTask;
    }

    /// <summary>
    /// インプレースオーバーレイをリセット（Stop時に呼び出し）
    /// </summary>
    public Task ResetAsync()
    {
        _logger.LogInformation("[WIN32_OVERLAY] オーバーレイマネージャーリセット");
        return HideAllInPlaceOverlaysAsync();
    }

    /// <summary>
    /// 現在アクティブなインプレースオーバーレイの数を取得
    /// </summary>
    public int ActiveOverlayCount => _activeOverlays.Count;

    /// <summary>
    /// インプレースオーバーレイマネージャーを初期化
    /// </summary>
    public Task InitializeAsync()
    {
        _logger.LogInformation("[WIN32_OVERLAY] オーバーレイマネージャー初期化");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 指定座標が属するモニターの作業領域を取得（マルチモニター対応）
    /// </summary>
    private Rectangle GetScreenBounds(int x, int y)
    {
        try
        {
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(x, y));
            var workingArea = screen.WorkingArea;

            _logger.LogTrace("[SCREEN_BOUNDS] 座標({X}, {Y}) → モニター: {MonitorName}, 作業領域: {Bounds}",
                x, y, screen.DeviceName, workingArea);

            return workingArea;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SCREEN_BOUNDS] モニター検出失敗 - プライマリモニターを使用");
            var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;
            return primaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        }
    }

    /// <summary>
    /// ウィンドウを安全にClose + Dispose
    /// </summary>
    private void CloseAndDisposeWindow(ILayeredOverlayWindow window)
    {
        try
        {
            window.Close();
            window.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[WIN32_OVERLAY] オーバーレイウィンドウの破棄中にエラー: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// リソース解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogInformation("[WIN32_OVERLAY] SimpleInPlaceOverlayManager Dispose開始");
        HideAllInPlaceOverlaysAsync().GetAwaiter().GetResult();
        _disposed = true;
        _logger.LogInformation("[WIN32_OVERLAY] SimpleInPlaceOverlayManager Disposed");
    }
}
