using System;
using System.Collections.Concurrent;
using Baketa.Core.Abstractions.Roi;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.Roi;

/// <summary>
/// [Issue #293] 翻訳Gatekeeperサービス実装
/// </summary>
/// <remarks>
/// Application層で使用する翻訳Gate機能の実装。
/// IRoiGatekeeperをラップし、テキストソース別の状態管理を提供します。
/// IRoiManagerと連携してROI学習ヒートマップに基づく動的閾値を適用します。
/// </remarks>
public sealed class TranslationGatekeeperService : ITranslationGatekeeperService
{
    private readonly ILogger<TranslationGatekeeperService> _logger;
    private readonly IRoiGatekeeper _gatekeeper;
    private readonly IRoiManager? _roiManager; // [Issue #293] ROI学習連携

    /// <summary>
    /// ソースID別の前回テキスト
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _previousTextBySource = new();

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public TranslationGatekeeperService(
        ILogger<TranslationGatekeeperService> logger,
        IRoiGatekeeper gatekeeper,
        IRoiManager? roiManager = null) // [Issue #293] ROI学習連携（オプショナル）
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gatekeeper = gatekeeper ?? throw new ArgumentNullException(nameof(gatekeeper));
        _roiManager = roiManager;

        _logger.LogInformation(
            "[Issue #293] TranslationGatekeeperService initialized: GatekeeperEnabled={Enabled}, RoiManagerAvailable={RoiAvailable}",
            _gatekeeper.IsEnabled, _roiManager != null);
    }

    /// <inheritdoc />
    public bool IsEnabled
    {
        get => _gatekeeper.IsEnabled;
        set
        {
            _gatekeeper.IsEnabled = value;
            _logger.LogInformation(
                "[Issue #293] TranslationGatekeeperService.IsEnabled changed to {Enabled}",
                value);
        }
    }

    /// <inheritdoc />
    public GatekeeperDecision ShouldTranslate(
        string sourceId,
        string currentText,
        GatekeeperRegionInfo? regionInfo = null)
    {
        ArgumentNullException.ThrowIfNull(sourceId);

        // 前回テキストを取得
        var previousText = _previousTextBySource.GetValueOrDefault(sourceId);

        // [Issue #293] IRoiManagerが利用可能な場合、RegionInfoにヒートマップ値を追加
        var enrichedRegionInfo = EnrichRegionInfoWithHeatmap(regionInfo);

        // Gatekeeperで判定
        var decision = _gatekeeper.ShouldTranslate(previousText, currentText, enrichedRegionInfo);

        // 判定結果に基づいて前回テキストを更新
        if (decision.ShouldTranslate)
        {
            // 翻訳を許可した場合、現在のテキストを前回テキストとして保存
            _previousTextBySource[sourceId] = currentText;
        }

        _logger.LogDebug(
            "[Issue #293] Gatekeeper decision for source '{SourceId}': {Decision} (Reason={Reason}, ChangeRatio={Ratio:F3}, Threshold={Threshold:F3}, HeatmapValue={Heatmap:F3})",
            sourceId,
            decision.ShouldTranslate ? "ALLOW" : "DENY",
            decision.Reason,
            decision.ChangeRatio,
            decision.AppliedThreshold,
            enrichedRegionInfo?.HeatmapValue ?? -1f);

        return decision;
    }

    /// <summary>
    /// [Issue #293] RegionInfoにヒートマップ値を追加（IRoiManager経由）
    /// </summary>
    private GatekeeperRegionInfo? EnrichRegionInfoWithHeatmap(GatekeeperRegionInfo? regionInfo)
    {
        // RegionInfoがnullの場合、またはIRoiManagerが利用不可の場合はそのまま返す
        if (regionInfo == null || _roiManager == null || !_roiManager.IsEnabled)
        {
            return regionInfo;
        }

        // 既にHeatmapValueが設定されている場合はそのまま返す
        if (regionInfo.HeatmapValue.HasValue)
        {
            return regionInfo;
        }

        // IRoiManagerからヒートマップ値を取得
        // 領域の中心座標でヒートマップ値を取得
        var centerX = regionInfo.NormalizedX + regionInfo.NormalizedWidth / 2f;
        var centerY = regionInfo.NormalizedY + regionInfo.NormalizedHeight / 2f;

        // 除外ゾーンチェック
        var isInExclusionZone = _roiManager.IsInExclusionZone(centerX, centerY);
        if (isInExclusionZone)
        {
            return regionInfo with
            {
                HeatmapValue = 0f,
                IsInExclusionZone = true
            };
        }

        // [Issue #293] IRoiManager経由で直接ヒートマップ値を取得
        // 複雑な逆算ロジックを削除し、責務を明確化
        var heatmapValue = _roiManager.GetHeatmapValueAt(centerX, centerY);

        _logger.LogDebug(
            "[Issue #293] Enriched RegionInfo with heatmap: Center=({CenterX:F3},{CenterY:F3}), HeatmapValue={Heatmap:F3}",
            centerX, centerY, heatmapValue);

        return regionInfo with
        {
            HeatmapValue = heatmapValue,
            IsInExclusionZone = false
        };
    }

    /// <inheritdoc />
    public void ClearPreviousText(string sourceId)
    {
        ArgumentNullException.ThrowIfNull(sourceId);

        if (_previousTextBySource.TryRemove(sourceId, out _))
        {
            _logger.LogDebug(
                "[Issue #293] Cleared previous text for source '{SourceId}'",
                sourceId);
        }
    }

    /// <inheritdoc />
    public void ClearAllPreviousText()
    {
        var count = _previousTextBySource.Count;
        _previousTextBySource.Clear();

        _logger.LogInformation(
            "[Issue #293] Cleared all previous text ({Count} sources)",
            count);
    }

    /// <inheritdoc />
    public void ReportTranslationResult(GatekeeperDecision decision, bool wasSuccessful, int tokensUsed)
    {
        _gatekeeper.ReportTranslationResult(decision, wasSuccessful, tokensUsed);
    }

    /// <inheritdoc />
    public GatekeeperStatistics GetStatistics()
    {
        return _gatekeeper.GetStatistics();
    }

    /// <inheritdoc />
    public void ResetStatistics()
    {
        _gatekeeper.ResetStatistics();
        ClearAllPreviousText();
    }
}
