using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.UI.Overlay;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.UI.Overlay;

/// <summary>
/// オーバーレイライフサイクル管理実装
/// オーバーレイの作成・更新・削除を統一的に管理
/// Clean Architecture: Application層 - ビジネスロジック実装
/// </summary>
public class OverlayLifecycleManager : IOverlayLifecycleManager
{
    private readonly ILogger<OverlayLifecycleManager> _logger;
    
    /// <summary>
    /// アクティブオーバーレイの管理
    /// Key: オーバーレイID, Value: オーバーレイ情報
    /// </summary>
    private readonly ConcurrentDictionary<string, OverlayInfo> _activeOverlays = new();
    
    /// <summary>
    /// 統計情報の管理
    /// </summary>
    private readonly LifecycleStatistics _statistics;
    
    private bool _isInitialized = false;
    private readonly object _initLock = new();

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public OverlayLifecycleManager(ILogger<OverlayLifecycleManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _statistics = new LifecycleStatistics { StartTime = DateTimeOffset.UtcNow };
        
        _logger.LogDebug("🔄 OverlayLifecycleManager インスタンス作成");
    }

    /// <inheritdoc />
    public int ActiveCount => _activeOverlays.Count;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            _logger.LogDebug("OverlayLifecycleManager は既に初期化済み");
            return;
        }

        lock (_initLock)
        {
            if (_isInitialized)
                return;
                
            _logger.LogInformation("🚀 OverlayLifecycleManager 初期化開始");
        }

        try
        {
            // 初期化処理（必要に応じて拡張）
            _activeOverlays.Clear();
            
            lock (_initLock)
            {
                _isInitialized = true;
            }

            _logger.LogInformation("✅ OverlayLifecycleManager 初期化完了");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ OverlayLifecycleManager 初期化失敗");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<OverlayInfo> CreateOverlayAsync(OverlayCreationRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        EnsureInitialized();

        try
        {
            _logger.LogDebug("🆕 オーバーレイ作成開始 - ID: {Id}, Text: '{Text}', Area: {Area}",
                request.Id, request.Text.Substring(0, Math.Min(30, request.Text.Length)), request.DisplayArea);

            // 既存の同じIDがある場合は更新処理
            if (_activeOverlays.ContainsKey(request.Id))
            {
                _logger.LogDebug("⚠️ 同じIDのオーバーレイが既に存在 - 更新処理に切り替え - ID: {Id}", request.Id);
                
                var updateRequest = new OverlayUpdateRequest
                {
                    Text = request.Text,
                    DisplayArea = request.DisplayArea,
                    Visibility = request.InitialVisibility,
                    ZIndex = request.ZIndex
                };
                
                var updatedInfo = await UpdateOverlayAsync(request.Id, updateRequest, cancellationToken).ConfigureAwait(false);
                return updatedInfo ?? throw new InvalidOperationException($"既存オーバーレイの更新に失敗 - ID: {request.Id}");
            }

            // 新規オーバーレイ情報作成
            var overlayInfo = new OverlayInfo
            {
                Id = request.Id,
                Text = request.Text,
                DisplayArea = request.DisplayArea,
                OriginalText = request.OriginalText,
                EngineName = request.EngineName,
                IsVisible = request.InitialVisibility,
                DisplayStartTime = DateTimeOffset.UtcNow,
                LastAccessTime = DateTimeOffset.UtcNow
            };

            // アクティブオーバーレイリストに追加
            if (!_activeOverlays.TryAdd(request.Id, overlayInfo))
            {
                throw new InvalidOperationException($"オーバーレイの追加に失敗 - ID: {request.Id}");
            }

            // 統計情報更新
            UpdateStatistics(StatisticOperation.Create);

            _logger.LogInformation("✅ [PHASE15_LIFECYCLE] オーバーレイ作成完了 - ID: {Id}, Text: '{Text}', ActiveCount: {Count}",
                request.Id, request.Text.Substring(0, Math.Min(30, request.Text.Length)), ActiveCount);

            return await Task.FromResult(overlayInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ オーバーレイ作成中にエラー発生 - ID: {Id}", request.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<OverlayInfo?> UpdateOverlayAsync(string overlayId, OverlayUpdateRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(overlayId))
            throw new ArgumentException("overlayId が null または空文字です", nameof(overlayId));

        if (request == null)
            throw new ArgumentNullException(nameof(request));

        EnsureInitialized();

        try
        {
            if (!_activeOverlays.TryGetValue(overlayId, out var currentInfo))
            {
                _logger.LogWarning("⚠️ 更新対象のオーバーレイが見つからない - ID: {Id}", overlayId);
                return null;
            }

            _logger.LogDebug("🔄 オーバーレイ更新開始 - ID: {Id}", overlayId);

            // 更新情報を適用
            var updatedInfo = currentInfo with
            {
                Text = request.Text ?? currentInfo.Text,
                DisplayArea = request.DisplayArea ?? currentInfo.DisplayArea,
                IsVisible = request.Visibility ?? currentInfo.IsVisible,
                LastAccessTime = request.UpdateLastAccessTime ? DateTimeOffset.UtcNow : currentInfo.LastAccessTime
            };

            // 辞書を更新
            _activeOverlays[overlayId] = updatedInfo;

            // 統計情報更新
            UpdateStatistics(StatisticOperation.Update);

            _logger.LogDebug("✅ [PHASE15_LIFECYCLE] オーバーレイ更新完了 - ID: {Id}", overlayId);

            return await Task.FromResult(updatedInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ オーバーレイ更新中にエラー発生 - ID: {Id}", overlayId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveOverlayAsync(string overlayId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(overlayId))
        {
            _logger.LogWarning("overlayId が null または空文字");
            return false;
        }

        EnsureInitialized();

        try
        {
            if (_activeOverlays.TryRemove(overlayId, out var removedInfo))
            {
                // 統計情報更新
                UpdateStatistics(StatisticOperation.Remove);

                _logger.LogDebug("✅ [PHASE15_LIFECYCLE] オーバーレイ削除完了 - ID: {Id}, Text: '{Text}', ActiveCount: {Count}",
                    overlayId, removedInfo.Text.Substring(0, Math.Min(30, removedInfo.Text.Length)), ActiveCount);

                return true;
            }
            else
            {
                _logger.LogWarning("⚠️ 削除対象のオーバーレイが見つからない - ID: {Id}", overlayId);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ オーバーレイ削除中にエラー発生 - ID: {Id}", overlayId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<int> RemoveOverlaysInAreaAsync(Rectangle area, IEnumerable<string>? excludeIds = null, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        try
        {
            var excludeIdSet = excludeIds?.ToHashSet() ?? new HashSet<string>();
            var overlaysToRemove = _activeOverlays.Values
                .Where(overlay => !excludeIdSet.Contains(overlay.Id) && IsRectangleIntersect(overlay.DisplayArea, area))
                .Select(overlay => overlay.Id)
                .ToList();

            int removedCount = 0;
            foreach (var overlayId in overlaysToRemove)
            {
                if (await RemoveOverlayAsync(overlayId, cancellationToken).ConfigureAwait(false))
                {
                    removedCount++;
                }
            }

            _logger.LogDebug("🗑️ [PHASE15_LIFECYCLE] 領域内オーバーレイ削除完了 - Area: {Area}, 削除数: {Count}, ActiveCount: {ActiveCount}",
                area, removedCount, ActiveCount);

            return removedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 領域内オーバーレイ削除中にエラー発生 - Area: {Area}", area);
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task<int> SetAllVisibilityAsync(bool visible, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        try
        {
            int changedCount = 0;
            var updateRequests = _activeOverlays.Keys.ToList();

            foreach (var overlayId in updateRequests)
            {
                var updateRequest = new OverlayUpdateRequest { Visibility = visible };
                var updatedInfo = await UpdateOverlayAsync(overlayId, updateRequest, cancellationToken).ConfigureAwait(false);
                
                if (updatedInfo != null)
                {
                    changedCount++;
                }
            }

            _logger.LogDebug("👁️ [PHASE15_LIFECYCLE] 全オーバーレイ可視性変更完了 - Visible: {Visible}, 変更数: {Count}",
                visible, changedCount);

            return changedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 全オーバーレイ可視性変更中にエラー発生 - Visible: {Visible}", visible);
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task<OverlayInfo?> GetOverlayInfoAsync(string overlayId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(overlayId))
            return null;

        EnsureInitialized();

        _activeOverlays.TryGetValue(overlayId, out var overlayInfo);
        
        // 最終アクセス時刻を更新
        if (overlayInfo != null)
        {
            var updatedInfo = overlayInfo with { LastAccessTime = DateTimeOffset.UtcNow };
            _activeOverlays[overlayId] = updatedInfo;
            return await Task.FromResult(updatedInfo);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<OverlayInfo>> GetAllOverlaysAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        return await Task.FromResult(_activeOverlays.Values.ToList());
    }

    /// <inheritdoc />
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var overlayCount = _activeOverlays.Count;
            _activeOverlays.Clear();

            _logger.LogInformation("🔄 [PHASE15_LIFECYCLE] ライフサイクルマネージャーリセット完了 - 削除オーバーレイ数: {Count}",
                overlayCount);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ライフサイクルマネージャーリセット中にエラー発生");
        }
    }

    /// <summary>
    /// 初期化確認
    /// </summary>
    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("OverlayLifecycleManager が初期化されていません。InitializeAsync() を先に呼び出してください。");
        }
    }

    /// <summary>
    /// 矩形交差判定
    /// </summary>
    private static bool IsRectangleIntersect(Rectangle rect1, Rectangle rect2)
    {
        return rect1.IntersectsWith(rect2);
    }

    /// <summary>
    /// 統計情報更新
    /// </summary>
    private void UpdateStatistics(StatisticOperation operation)
    {
        // 統計情報は将来的に IOptionsSnapshot<> や別サービスで管理する予定
        // 現在は基本的な実装のみ
    }

    /// <summary>
    /// 統計操作種別
    /// </summary>
    private enum StatisticOperation
    {
        Create,
        Update,
        Remove
    }
}