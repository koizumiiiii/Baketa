using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.UI.Overlay;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.UI.Overlay;

/// <summary>
/// オーバーレイレンダラーのスタブ実装
/// UI層の実装が完成するまでの一時的な実装
/// Phase 15 動作確認・テスト用
/// </summary>
public class StubOverlayRenderer : IOverlayRenderer
{
    private readonly ILogger<StubOverlayRenderer> _logger;

    /// <summary>
    /// スタブで管理するオーバーレイ情報
    /// 実際の UI 要素は作成せず、情報のみ保持
    /// </summary>
    private readonly Dictionary<string, OverlayInfo> _stubOverlays = new();

    /// <summary>
    /// スタブレンダラーの統計情報
    /// </summary>
    private long _totalRendered = 0;
    private long _totalRemoved = 0;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public StubOverlayRenderer(ILogger<StubOverlayRenderer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("🎭 [STUB_RENDERER] StubOverlayRenderer 初期化");
    }

    /// <inheritdoc />
    public int RenderedCount => _stubOverlays.Count;

    /// <inheritdoc />
    public RendererCapabilities Capabilities => RendererCapabilities.None; // スタブのため機能なし

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🚀 [STUB_RENDERER] スタブレンダラー初期化開始");

        // スタブでは実際の UI 初期化は行わない
        _stubOverlays.Clear();
        _totalRendered = 0;
        _totalRemoved = 0;

        _logger.LogInformation("✅ [STUB_RENDERER] スタブレンダラー初期化完了");
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> RenderOverlayAsync(OverlayInfo info, CancellationToken cancellationToken = default)
    {
        if (info == null)
        {
            _logger.LogWarning("[STUB_RENDERER] OverlayInfo が null");
            return false;
        }

        try
        {
            // スタブでは実際の描画は行わず、情報のみ保存
            _stubOverlays[info.Id] = info;
            _totalRendered++;

            _logger.LogDebug("🎭 [STUB_RENDERER] オーバーレイ描画シミュレート - ID: {Id}, Text: '{Text}', Area: {Area}",
                info.Id, info.Text.Substring(0, Math.Min(30, info.Text.Length)), info.DisplayArea);

            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [STUB_RENDERER] 描画シミュレート中にエラー - ID: {Id}", info.Id);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateOverlayAsync(string overlayId, OverlayRenderUpdate updateInfo, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(overlayId) || updateInfo == null)
            return false;

        try
        {
            if (!_stubOverlays.TryGetValue(overlayId, out var currentInfo))
            {
                _logger.LogWarning("[STUB_RENDERER] 更新対象オーバーレイが見つからない - ID: {Id}", overlayId);
                return false;
            }

            // スタブでは更新情報を適用
            var updatedInfo = currentInfo with
            {
                Text = updateInfo.Text ?? currentInfo.Text,
                DisplayArea = updateInfo.DisplayArea ?? currentInfo.DisplayArea,
                IsVisible = true // スタブでは常に可視とする
            };

            _stubOverlays[overlayId] = updatedInfo;

            _logger.LogDebug("🎭 [STUB_RENDERER] オーバーレイ更新シミュレート - ID: {Id}", overlayId);
            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [STUB_RENDERER] 更新シミュレート中にエラー - ID: {Id}", overlayId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetVisibilityAsync(string overlayId, bool visible, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(overlayId))
            return false;

        try
        {
            if (_stubOverlays.TryGetValue(overlayId, out var info))
            {
                _stubOverlays[overlayId] = info with { IsVisible = visible };
                _logger.LogDebug("🎭 [STUB_RENDERER] 可視性変更シミュレート - ID: {Id}, Visible: {Visible}", overlayId, visible);
                return true;
            }

            return await Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [STUB_RENDERER] 可視性変更中にエラー - ID: {Id}", overlayId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<int> SetAllVisibilityAsync(bool visible, CancellationToken cancellationToken = default)
    {
        try
        {
            var overlayIds = _stubOverlays.Keys.ToList();
            int changedCount = 0;

            foreach (var overlayId in overlayIds)
            {
                if (await SetVisibilityAsync(overlayId, visible, cancellationToken))
                {
                    changedCount++;
                }
            }

            _logger.LogDebug("🎭 [STUB_RENDERER] 全オーバーレイ可視性変更シミュレート - Visible: {Visible}, 変更数: {Count}", visible, changedCount);
            return changedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [STUB_RENDERER] 全オーバーレイ可視性変更中にエラー");
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveOverlayAsync(string overlayId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(overlayId))
            return false;

        try
        {
            if (_stubOverlays.Remove(overlayId))
            {
                _totalRemoved++;
                _logger.LogDebug("🎭 [STUB_RENDERER] オーバーレイ削除シミュレート - ID: {Id}", overlayId);
                return true;
            }

            return await Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [STUB_RENDERER] 削除シミュレート中にエラー - ID: {Id}", overlayId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<int> RemoveOverlaysInAreaAsync(Rectangle area, IEnumerable<string>? excludeIds = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var excludeIdSet = excludeIds?.ToHashSet() ?? new HashSet<string>();
            var overlaysToRemove = _stubOverlays.Values
                .Where(overlay => !excludeIdSet.Contains(overlay.Id) && overlay.DisplayArea.IntersectsWith(area))
                .Select(overlay => overlay.Id)
                .ToList();

            int removedCount = 0;
            foreach (var overlayId in overlaysToRemove)
            {
                if (await RemoveOverlayAsync(overlayId, cancellationToken))
                {
                    removedCount++;
                }
            }

            _logger.LogDebug("🎭 [STUB_RENDERER] 領域内オーバーレイ削除シミュレート - Area: {Area}, 削除数: {Count}", area, removedCount);
            return removedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [STUB_RENDERER] 領域削除シミュレート中にエラー - Area: {Area}", area);
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task RemoveAllOverlaysAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var overlayCount = _stubOverlays.Count;
            _stubOverlays.Clear();
            _totalRemoved += overlayCount;

            _logger.LogDebug("🎭 [STUB_RENDERER] 全オーバーレイ削除シミュレート - 削除数: {Count}", overlayCount);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [STUB_RENDERER] 全削除シミュレート中にエラー");
        }
    }

    /// <inheritdoc />
    public async Task<Rectangle?> GetOverlayBoundsAsync(string overlayId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(overlayId))
            return null;

        try
        {
            if (_stubOverlays.TryGetValue(overlayId, out var info))
            {
                return await Task.FromResult(info.DisplayArea);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [STUB_RENDERER] 位置取得中にエラー - ID: {Id}", overlayId);
            return null;
        }
    }

    /// <summary>
    /// スタブレンダラーの統計情報取得
    /// デバッグ・テスト用
    /// </summary>
    public RenderingStatistics GetStatistics()
    {
        return new RenderingStatistics
        {
            TotalRendered = _totalRendered,
            TotalRemoved = _totalRemoved,
            AverageRenderTime = 0.0, // スタブでは実際の描画時間なし
            CurrentFps = 0.0,
            GpuUsage = 0.0
        };
    }
}
