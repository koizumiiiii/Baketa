using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.UI.Overlay;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.UI.Overlay;

/// <summary>
/// オーバーレイシステム全体の中央調整実装
/// 全ての翻訳結果オーバーレイ要求を統一的に処理し、重複排除を実現
/// Clean Architecture: Application層 - ビジネスロジック実装
/// </summary>
public class OverlayOrchestrator : IOverlayOrchestrator
{
    private readonly IOverlayCollisionDetector _collisionDetector;
    private readonly IOverlayLifecycleManager _lifecycleManager;
    private readonly IOverlayRenderer _renderer;
    private readonly IOverlayPositionCalculator _positionCalculator;
    private readonly ILogger<OverlayOrchestrator> _logger;

    private bool _isInitialized = false;
    private readonly object _initLock = new();

    /// <summary>
    /// コンストラクタ
    /// 依存サービスを注入
    /// </summary>
    public OverlayOrchestrator(
        IOverlayCollisionDetector collisionDetector,
        IOverlayLifecycleManager lifecycleManager,
        IOverlayRenderer renderer,
        IOverlayPositionCalculator positionCalculator,
        ILogger<OverlayOrchestrator> logger)
    {
        _collisionDetector = collisionDetector ?? throw new ArgumentNullException(nameof(collisionDetector));
        _lifecycleManager = lifecycleManager ?? throw new ArgumentNullException(nameof(lifecycleManager));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _positionCalculator = positionCalculator ?? throw new ArgumentNullException(nameof(positionCalculator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogDebug("🏗️ OverlayOrchestrator インスタンス作成");
    }

    /// <inheritdoc />
    public int ActiveOverlayCount => _lifecycleManager.ActiveCount;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            _logger.LogDebug("OverlayOrchestrator は既に初期化済み");
            return;
        }

        lock (_initLock)
        {
            if (_isInitialized)
                return;

            _logger.LogInformation("🚀 OverlayOrchestrator 初期化開始");
        }

        try
        {
            // 依存サービスを並行初期化
            var tasks = new[]
            {
                _collisionDetector.ResetAsync(cancellationToken),
                _lifecycleManager.InitializeAsync(cancellationToken),
                _renderer.InitializeAsync(cancellationToken),
                _positionCalculator.InitializeAsync(cancellationToken)
            };

            await Task.WhenAll(tasks).ConfigureAwait(false);

            lock (_initLock)
            {
                _isInitialized = true;
            }

            _logger.LogInformation("✅ OverlayOrchestrator 初期化完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ OverlayOrchestrator 初期化失敗");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> HandleTranslationResultAsync(TranslationResult result, CancellationToken cancellationToken = default)
    {
        if (result == null)
        {
            _logger.LogWarning("TranslationResult が null - 処理をスキップ");
            return false;
        }

        EnsureInitialized();

        // デバッグログ
        _logger.LogDebug("🎯 翻訳結果処理開始 - ID: {Id}, Text: '{Text}', Area: {Area}",
            result.Id, result.TranslatedText?.Substring(0, Math.Min(50, result.TranslatedText?.Length ?? 0)), result.DisplayArea);

        try
        {
            // Phase 1: 重複・衝突検出
            var displayRequest = new OverlayDisplayRequest
            {
                Id = result.Id,
                Text = result.TranslatedText,
                DisplayArea = result.DisplayArea,
                OriginalText = result.OriginalText,
                RequestTime = result.Timestamp,
                EngineName = result.EngineName
            };

            if (!await _collisionDetector.ShouldDisplayAsync(displayRequest, cancellationToken).ConfigureAwait(false))
            {
                _logger.LogDebug("🚫 [PHASE15_ORCHESTRATOR] 重複検出により表示をスキップ - ID: {Id}, Text: '{Text}'",
                    result.Id, result.TranslatedText?.Substring(0, Math.Min(30, result.TranslatedText?.Length ?? 0)));
                return false;
            }

            // Phase 2: 位置最適化
            var positionRequest = new PositionCalculationRequest
            {
                Id = result.Id,
                Text = result.TranslatedText,
                DesiredArea = result.DisplayArea,
                Strategy = PositionStrategy.AvoidCollision
            };

            var optimizedArea = await _positionCalculator.CalculateOptimalPositionAsync(positionRequest, cancellationToken).ConfigureAwait(false);

            // Phase 3: オーバーレイ作成
            var creationRequest = new OverlayCreationRequest
            {
                Id = result.Id,
                Text = result.TranslatedText,
                DisplayArea = optimizedArea,
                OriginalText = result.OriginalText,
                SourceLanguage = result.SourceLanguage,
                TargetLanguage = result.TargetLanguage,
                EngineName = result.EngineName
            };

            var overlayInfo = await _lifecycleManager.CreateOverlayAsync(creationRequest, cancellationToken).ConfigureAwait(false);

            // Phase 4: レンダリング
            if (!await _renderer.RenderOverlayAsync(overlayInfo, cancellationToken).ConfigureAwait(false))
            {
                _logger.LogWarning("⚠️ オーバーレイレンダリング失敗 - ID: {Id}", result.Id);
                await _lifecycleManager.RemoveOverlayAsync(result.Id, cancellationToken).ConfigureAwait(false);
                return false;
            }

            // Phase 5: 衝突検出器に登録
            await _collisionDetector.RegisterDisplayedAsync(overlayInfo, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("✅ [PHASE15_ORCHESTRATOR] オーバーレイ表示成功 - ID: {Id}, Text: '{Text}', Area: {Area}",
                result.Id, result.TranslatedText?.Substring(0, Math.Min(30, result.TranslatedText?.Length ?? 0)), optimizedArea);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 翻訳結果処理中にエラー発生 - ID: {Id}", result.Id);

            // エラー時のクリーンアップ
            try
            {
                await _lifecycleManager.RemoveOverlayAsync(result.Id, cancellationToken).ConfigureAwait(false);
                await _collisionDetector.UnregisterAsync(result.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogError(cleanupEx, "エラー時クリーンアップ失敗 - ID: {Id}", result.Id);
            }

            return false;
        }
    }

    /// <inheritdoc />
    public async Task RemoveOverlaysInAreaAsync(Rectangle area, string? excludeId = null, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _logger.LogDebug("🗑️ 領域内オーバーレイ削除開始 - Area: {Area}, ExcludeId: {ExcludeId}", area, excludeId);

        try
        {
            // 衝突検出器から対象オーバーレイを取得
            var overlaysInArea = await _collisionDetector.DetectCollisionsAsync(area, cancellationToken).ConfigureAwait(false);

            int removedCount = 0;
            foreach (var overlayInfo in overlaysInArea)
            {
                if (excludeId != null && overlayInfo.Id == excludeId)
                {
                    _logger.LogDebug("除外ID設定によりスキップ - ID: {Id}", overlayInfo.Id);
                    continue;
                }

                // ライフサイクルマネージャーから削除
                if (await _lifecycleManager.RemoveOverlayAsync(overlayInfo.Id, cancellationToken).ConfigureAwait(false))
                {
                    // レンダラーからも削除
                    await _renderer.RemoveOverlayAsync(overlayInfo.Id, cancellationToken).ConfigureAwait(false);

                    // 衝突検出器から登録解除
                    await _collisionDetector.UnregisterAsync(overlayInfo.Id, cancellationToken).ConfigureAwait(false);

                    removedCount++;
                    _logger.LogDebug("オーバーレイ削除完了 - ID: {Id}", overlayInfo.Id);
                }
            }

            _logger.LogInformation("✅ 領域内オーバーレイ削除完了 - Area: {Area}, 削除数: {Count}", area, removedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 領域内オーバーレイ削除中にエラー発生 - Area: {Area}", area);
        }
    }

    /// <inheritdoc />
    public async Task SetAllOverlaysVisibilityAsync(bool visible, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _logger.LogDebug("👁️ 全オーバーレイ可視性変更開始 - Visible: {Visible}", visible);

        try
        {
            // ライフサイクルマネージャーとレンダラーで並行処理
            var tasks = new[]
            {
                _lifecycleManager.SetAllVisibilityAsync(visible, cancellationToken),
                _renderer.SetAllVisibilityAsync(visible, cancellationToken).ContinueWith(t => t.Result, cancellationToken)
            };

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            var changedCount = Math.Max(results[0], results[1]);

            _logger.LogInformation("✅ 全オーバーレイ可視性変更完了 - Visible: {Visible}, 変更数: {Count}", visible, changedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 全オーバーレイ可視性変更中にエラー発生 - Visible: {Visible}", visible);
        }
    }

    /// <inheritdoc />
    public async Task ResetAllOverlaysAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🔄 全オーバーレイリセット開始");

        try
        {
            // すべてのサービスを並行リセット
            var tasks = new[]
            {
                _renderer.RemoveAllOverlaysAsync(cancellationToken),
                _lifecycleManager.ResetAsync(cancellationToken),
                _collisionDetector.ResetAsync(cancellationToken)
            };

            await Task.WhenAll(tasks).ConfigureAwait(false);

            _logger.LogInformation("✅ 全オーバーレイリセット完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 全オーバーレイリセット中にエラー発生");
            throw;
        }
    }

    /// <summary>
    /// 初期化確認
    /// 未初期化の場合は例外をスロー
    /// </summary>
    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("OverlayOrchestrator が初期化されていません。InitializeAsync() を先に呼び出してください。");
        }
    }
}
