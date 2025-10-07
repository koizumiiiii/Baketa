using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Baketa.UI.Views.Overlay;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// オーバーレイコレクション管理を担当するサービス実装
/// Phase 4.1: InPlaceTranslationOverlayManagerからコレクション管理ロジックを抽出
/// </summary>
public class OverlayCollectionManager(ILogger<OverlayCollectionManager> logger) : IOverlayCollectionManager
{
    private readonly ConcurrentDictionary<int, InPlaceTranslationOverlayWindow> _activeOverlays = new();
    private readonly ILogger<OverlayCollectionManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// アクティブオーバーレイ数
    /// </summary>
    public int ActiveOverlayCount => _activeOverlays.Count;

    /// <summary>
    /// オーバーレイコレクションが空かどうか
    /// </summary>
    public bool IsEmpty => _activeOverlays.IsEmpty;

    /// <summary>
    /// オーバーレイを追加します
    /// </summary>
    public bool AddOverlay(int chunkId, InPlaceTranslationOverlayWindow overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);

        var added = _activeOverlays.TryAdd(chunkId, overlay);
        if (added)
        {
            _logger.LogDebug("オーバーレイ追加成功 - ChunkId: {ChunkId}, 総数: {Count}", chunkId, _activeOverlays.Count);
        }
        else
        {
            _logger.LogWarning("オーバーレイ追加失敗（既存）- ChunkId: {ChunkId}", chunkId);
        }

        return added;
    }

    /// <summary>
    /// オーバーレイを取得します
    /// </summary>
    public bool TryGetOverlay(int chunkId, out InPlaceTranslationOverlayWindow? overlay)
    {
        return _activeOverlays.TryGetValue(chunkId, out overlay);
    }

    /// <summary>
    /// オーバーレイを削除します
    /// </summary>
    public bool RemoveOverlay(int chunkId, out InPlaceTranslationOverlayWindow? overlay)
    {
        var removed = _activeOverlays.TryRemove(chunkId, out overlay);
        if (removed)
        {
            _logger.LogDebug("オーバーレイ削除成功 - ChunkId: {ChunkId}, 残数: {Count}", chunkId, _activeOverlays.Count);
        }

        return removed;
    }

    /// <summary>
    /// すべてのオーバーレイを非表示にします
    /// </summary>
    public async Task HideAllOverlaysAsync()
    {
        _logger.LogDebug("すべてのオーバーレイ非表示開始");

        // アクティブなオーバーレイをコピー（列挙中の変更を避けるため）
        var overlaysToHide = _activeOverlays.ToList();

        _logger.LogDebug("非表示対象オーバーレイ数: {Count}", overlaysToHide.Count);

        if (overlaysToHide.Count == 0)
        {
            _logger.LogDebug("アクティブオーバーレイが存在しません - 処理スキップ");
            return;
        }

        // すべてのオーバーレイを並行して非表示
        var hideTasks = overlaysToHide.Select(async kvp =>
        {
            try
            {
                _logger.LogDebug("オーバーレイ非表示開始 - ChunkId: {ChunkId}", kvp.Key);

                _activeOverlays.TryRemove(kvp.Key, out _);
                await kvp.Value.HideAsync().ConfigureAwait(false);

                _logger.LogDebug("オーバーレイHide完了 - ChunkId: {ChunkId}", kvp.Key);

                kvp.Value.Dispose();

                _logger.LogDebug("オーバーレイDispose完了 - ChunkId: {ChunkId}", kvp.Key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "オーバーレイ非表示エラー - ChunkId: {ChunkId}", kvp.Key);
            }
        });

        await Task.WhenAll(hideTasks).ConfigureAwait(false);

        _logger.LogInformation("すべてのオーバーレイ非表示完了 - 処理済み: {ProcessedCount}, 残存: {RemainingCount}",
            overlaysToHide.Count, _activeOverlays.Count);
    }

    /// <summary>
    /// すべてのオーバーレイの可視性を切り替えます
    /// </summary>
    public async Task SetAllOverlaysVisibilityAsync(bool visible, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("オーバーレイ可視性切り替え開始: {Visible}, 対象数: {Count}", visible, _activeOverlays.Count);

        if (_activeOverlays.IsEmpty)
        {
            _logger.LogDebug("アクティブなオーバーレイが存在しません - 可視性切り替えをスキップ");
            return;
        }

        // アクティブなオーバーレイをコピー（列挙中の変更を避けるため）
        var overlaysToToggle = _activeOverlays.ToList();

        // すべてのオーバーレイの可視性を並行して切り替え
        var visibilityTasks = overlaysToToggle.Select(async kvp =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                // UIスレッドで可視性を変更
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        kvp.Value.IsVisible = visible;
                        _logger.LogTrace("オーバーレイ可視性変更: ChunkId={ChunkId}, Visible={Visible}", kvp.Key, visible);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "UIスレッドでの可視性変更エラー - ChunkId: {ChunkId}", kvp.Key);
                    }
                }, DispatcherPriority.Normal, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "可視性切り替えエラー - ChunkId: {ChunkId}", kvp.Key);
            }
        });

        await Task.WhenAll(visibilityTasks).ConfigureAwait(false);

        _logger.LogDebug("オーバーレイ可視性切り替え完了: {Visible}, 処理数: {Count}", visible, overlaysToToggle.Count);
    }

    /// <summary>
    /// 個別オーバーレイを非表示にします
    /// </summary>
    public async Task HideOverlayAsync(int chunkId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_activeOverlays.TryRemove(chunkId, out var overlay))
            {
                _logger.LogDebug("オーバーレイ非表示実行 - ChunkId: {ChunkId}", chunkId);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    overlay.Hide();
                    overlay.Dispose();
                }, DispatcherPriority.Normal, cancellationToken);

                _logger.LogDebug("オーバーレイ非表示完了 - ChunkId: {ChunkId}", chunkId);
            }
            else
            {
                _logger.LogDebug("非表示対象オーバーレイが見つかりません - ChunkId: {ChunkId}", chunkId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "オーバーレイ非表示処理エラー - ChunkId: {ChunkId}", chunkId);
        }
    }

    /// <summary>
    /// 衝突回避用の既存オーバーレイ境界情報を取得します
    /// </summary>
    public List<Rectangle> GetExistingOverlayBounds()
    {
        var bounds = new List<Rectangle>();

        foreach (var overlay in _activeOverlays.Values)
        {
            try
            {
                // オーバーレイの現在位置とサイズを取得
                var position = overlay.Position;
                var clientSize = overlay.ClientSize;
                bounds.Add(new Rectangle((int)position.X, (int)position.Y, (int)clientSize.Width, (int)clientSize.Height));
            }
            catch (Exception ex)
            {
                // 個別オーバーレイの情報取得失敗は無視（他のオーバーレイに影響しない）
                _logger.LogDebug(ex, "オーバーレイ境界情報取得失敗: ChunkId={ChunkId}", overlay.ChunkId);
            }
        }

        return bounds;
    }
}
