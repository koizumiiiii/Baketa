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
/// オーバーレイ重複・衝突検出実装
/// Phase 13重複防止フィルターを抽象化・拡張したシステム
/// Clean Architecture: Application層 - ビジネスロジック実装
/// </summary>
public class OverlayCollisionDetector : IOverlayCollisionDetector
{
    private readonly ILogger<OverlayCollisionDetector> _logger;
    private readonly CollisionDetectionSettings _settings;
    
    /// <summary>
    /// Phase 13互換: テキストハッシュベース重複検出
    /// Key: テキストハッシュ, Value: 最後の表示時刻
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recentTranslations = new();
    
    /// <summary>
    /// 表示中オーバーレイの位置情報管理
    /// Key: オーバーレイID, Value: オーバーレイ情報
    /// </summary>
    private readonly ConcurrentDictionary<string, OverlayInfo> _activeOverlays = new();

    /// <summary>
    /// 自動クリーンアップ用のカウンター
    /// </summary>
    private long _operationCounter = 0;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public OverlayCollisionDetector(
        ILogger<OverlayCollisionDetector> logger,
        CollisionDetectionSettings? settings = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings ?? new CollisionDetectionSettings();
        
        _logger.LogDebug("🔍 OverlayCollisionDetector インスタンス作成 - 設定: {Settings}", _settings);
    }

    /// <inheritdoc />
    public int RegisteredCount => _activeOverlays.Count;

    /// <inheritdoc />
    public async Task<bool> ShouldDisplayAsync(OverlayDisplayRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            _logger.LogWarning("OverlayDisplayRequest が null");
            return false;
        }

        var currentTime = DateTimeOffset.UtcNow;
        
        try
        {
            // Phase 13互換: テキストハッシュによる重複検出
            var textHash = GenerateTextHash(request.Text);
            
            if (_recentTranslations.TryGetValue(textHash, out var lastDisplayTime))
            {
                var timeDiff = currentTime - lastDisplayTime;
                if (timeDiff < _settings.DuplicationPreventionWindow)
                {
                    _logger.LogDebug("🚫 [PHASE15_COLLISION] テキスト重複検出 - Hash: {Hash}, Text: '{Text}', 前回表示: {TimeDiff}ms前",
                        textHash, request.Text.Substring(0, Math.Min(50, request.Text.Length)), (int)timeDiff.TotalMilliseconds);
                    return false;
                }
            }

            // 位置衝突検出（有効な場合）
            if (_settings.EnablePositionCollisionDetection)
            {
                var positionCollision = await DetectPositionCollisionAsync(request.DisplayArea, request.Id, cancellationToken).ConfigureAwait(false);
                if (positionCollision)
                {
                    _logger.LogDebug("🚫 [PHASE15_COLLISION] 位置重複検出 - ID: {Id}, Area: {Area}", request.Id, request.DisplayArea);
                    return false;
                }
            }

            // 表示許可
            _logger.LogDebug("✅ [PHASE15_COLLISION] 表示許可 - ID: {Id}, Text: '{Text}', Hash: {Hash}",
                request.Id, request.Text.Substring(0, Math.Min(30, request.Text.Length)), textHash);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 衝突検出処理中にエラー発生 - ID: {Id}", request.Id);
            return false; // エラー時は安全のため非表示
        }
        finally
        {
            // 定期的な自動クリーンアップ
            if (Interlocked.Increment(ref _operationCounter) % 20 == 0)
            {
                _ = Task.Run(() => CleanupExpiredAsync(cancellationToken), cancellationToken);
            }
        }
    }

    /// <inheritdoc />
    public async Task RegisterDisplayedAsync(OverlayInfo info, CancellationToken cancellationToken = default)
    {
        if (info == null)
        {
            _logger.LogWarning("OverlayInfo が null");
            return;
        }

        try
        {
            var currentTime = DateTimeOffset.UtcNow;
            
            // テキストハッシュを登録（Phase 13互換）
            var textHash = GenerateTextHash(info.Text);
            _recentTranslations[textHash] = currentTime;
            
            // オーバーレイ情報を登録
            var registrationInfo = info with { DisplayStartTime = currentTime, LastAccessTime = currentTime };
            _activeOverlays[info.Id] = registrationInfo;

            _logger.LogDebug("📝 [PHASE15_COLLISION] オーバーレイ登録 - ID: {Id}, Text: '{Text}', Hash: {Hash}",
                info.Id, info.Text.Substring(0, Math.Min(30, info.Text.Length)), textHash);

            await Task.CompletedTask; // 非同期化のための await
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ オーバーレイ登録中にエラー発生 - ID: {Id}", info.Id);
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<OverlayInfo>> DetectCollisionsAsync(Rectangle area, CancellationToken cancellationToken = default)
    {
        try
        {
            var collisions = _activeOverlays.Values
                .Where(overlay => IsRectangleCollision(overlay.DisplayArea, area))
                .ToList();

            _logger.LogDebug("🔍 [PHASE15_COLLISION] 領域衝突検出 - Area: {Area}, 検出数: {Count}", area, collisions.Count);

            return await Task.FromResult(collisions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 衝突検出中にエラー発生 - Area: {Area}", area);
            return Enumerable.Empty<OverlayInfo>();
        }
    }

    /// <inheritdoc />
    public async Task UnregisterAsync(string overlayId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(overlayId))
        {
            _logger.LogWarning("overlayId が null または空文字");
            return;
        }

        try
        {
            if (_activeOverlays.TryRemove(overlayId, out var removedInfo))
            {
                _logger.LogDebug("🗑️ [PHASE15_COLLISION] オーバーレイ登録解除 - ID: {Id}, Text: '{Text}'",
                    overlayId, removedInfo.Text.Substring(0, Math.Min(30, removedInfo.Text.Length)));
            }
            else
            {
                _logger.LogDebug("⚠️ 登録解除対象が見つからない - ID: {Id}", overlayId);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ オーバーレイ登録解除中にエラー発生 - ID: {Id}", overlayId);
        }
    }

    /// <inheritdoc />
    public async Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var currentTime = DateTimeOffset.UtcNow;
            int cleanedUpCount = 0;

            // 期限切れテキストハッシュのクリーンアップ（Phase 13互換）
            var expiredTextHashes = _recentTranslations
                .Where(kvp => currentTime - kvp.Value > _settings.DuplicationPreventionWindow.Add(_settings.DuplicationPreventionWindow))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var expiredHash in expiredTextHashes)
            {
                if (_recentTranslations.TryRemove(expiredHash, out _))
                {
                    cleanedUpCount++;
                }
            }

            // 期限切れオーバーレイ情報のクリーンアップ
            var expiredOverlays = _activeOverlays.Values
                .Where(overlay => currentTime - overlay.LastAccessTime > _settings.MaxEntryLifetime)
                .Select(overlay => overlay.Id)
                .ToList();

            foreach (var expiredId in expiredOverlays)
            {
                if (_activeOverlays.TryRemove(expiredId, out _))
                {
                    cleanedUpCount++;
                }
            }

            // 自動クリーンアップ閾値チェック（Phase 13互換）
            if (_recentTranslations.Count > _settings.AutoCleanupThreshold)
            {
                var oldestEntries = _recentTranslations
                    .OrderBy(kvp => kvp.Value)
                    .Take(_recentTranslations.Count - _settings.AutoCleanupThreshold)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var oldHash in oldestEntries)
                {
                    if (_recentTranslations.TryRemove(oldHash, out _))
                    {
                        cleanedUpCount++;
                    }
                }
            }

            if (cleanedUpCount > 0)
            {
                _logger.LogDebug("🧹 [PHASE15_COLLISION] 期限切れエントリクリーンアップ完了 - 削除数: {Count}, テキストハッシュ登録数: {TextCount}, オーバーレイ登録数: {OverlayCount}",
                    cleanedUpCount, _recentTranslations.Count, _activeOverlays.Count);
            }

            return await Task.FromResult(cleanedUpCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 期限切れクリーンアップ中にエラー発生");
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var textHashCount = _recentTranslations.Count;
            var overlayCount = _activeOverlays.Count;

            _recentTranslations.Clear();
            _activeOverlays.Clear();
            _operationCounter = 0;

            _logger.LogInformation("🔄 [PHASE15_COLLISION] 衝突検出器リセット完了 - テキストハッシュ: {TextCount}, オーバーレイ: {OverlayCount}",
                textHashCount, overlayCount);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 衝突検出器リセット中にエラー発生");
        }
    }

    /// <summary>
    /// テキストハッシュ生成
    /// Phase 13互換のハッシュ計算
    /// </summary>
    private static string GenerateTextHash(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Phase 13と同じハッシュ計算ロジック
        return $"{text}_{text.Length}".GetHashCode().ToString();
    }

    /// <summary>
    /// 位置衝突検出の内部実装
    /// </summary>
    private async Task<bool> DetectPositionCollisionAsync(Rectangle area, string excludeId, CancellationToken cancellationToken)
    {
        try
        {
            var existingOverlays = _activeOverlays.Values
                .Where(overlay => overlay.Id != excludeId && overlay.IsVisible)
                .ToList();

            foreach (var existingOverlay in existingOverlays)
            {
                if (IsRectangleCollision(area, existingOverlay.DisplayArea))
                {
                    return true;
                }
            }

            return await Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 位置衝突検出中にエラー発生");
            return false; // エラー時は安全のため衝突なしとする
        }
    }

    /// <summary>
    /// 矩形衝突判定
    /// オーバーラップ率を考慮した衝突検出
    /// </summary>
    private bool IsRectangleCollision(Rectangle rect1, Rectangle rect2)
    {
        if (!rect1.IntersectsWith(rect2))
            return false;

        // オーバーラップ率の計算
        var intersection = Rectangle.Intersect(rect1, rect2);
        var smallerArea = Math.Min(rect1.Width * rect1.Height, rect2.Width * rect2.Height);
        
        if (smallerArea == 0)
            return false;

        var overlapRatio = (double)(intersection.Width * intersection.Height) / smallerArea;
        return overlapRatio >= _settings.PositionOverlapThreshold;
    }
}