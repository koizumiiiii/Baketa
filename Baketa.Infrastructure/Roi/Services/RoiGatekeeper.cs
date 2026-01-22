using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using Baketa.Core.Abstractions.Roi;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Roi.Services;

/// <summary>
/// ROI Gatekeeper実装
/// </summary>
/// <remarks>
/// [Issue #293] テキスト変化検知後のCloud AI翻訳Gate機能。
/// 相対閾値を使用して短文・長文で異なる判定基準を適用し、
/// 不要なAPI呼び出しを削減してトークンを節約します。
/// </remarks>
public sealed class RoiGatekeeper : IRoiGatekeeper
{
    private readonly ILogger<RoiGatekeeper> _logger;
    private readonly IRoiManager _roiManager;
    private readonly RoiGatekeeperSettings _settings;

    private long _totalDecisions;
    private long _allowedCount;
    private long _deniedCount;
    private long _estimatedTokensSaved;
    private long _actualTokensUsed;
    private double _totalChangeRatio;
    private DateTime? _lastDecisionAt;
    private readonly DateTime _statisticsStartedAt = DateTime.UtcNow;
    private readonly ConcurrentDictionary<GatekeeperReason, long> _decisionsByReason = new();
    private readonly object _statisticsLock = new();

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public RoiGatekeeper(
        ILogger<RoiGatekeeper> logger,
        IRoiManager roiManager,
        IOptions<RoiGatekeeperSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _roiManager = roiManager ?? throw new ArgumentNullException(nameof(roiManager));
        _settings = settings?.Value ?? RoiGatekeeperSettings.CreateDefault();

        IsEnabled = _settings.Enabled;

        _logger.LogInformation(
            "RoiGatekeeper initialized: Enabled={Enabled}, ShortThreshold={Short}, MediumThreshold={Medium}, LongThreshold={Long}",
            IsEnabled, _settings.ShortTextChangeThreshold, _settings.MediumTextChangeThreshold, _settings.LongTextChangeThreshold);
    }

    /// <inheritdoc />
    public bool IsEnabled { get; set; }

    /// <inheritdoc />
    public GatekeeperDecision ShouldTranslate(
        string? previousText,
        string currentText,
        GatekeeperRegionInfo? region = null)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Gatekeeperが無効の場合は常に許可
            if (!IsEnabled)
            {
                var disabledDecision = CreateDecision(
                    true,
                    GatekeeperReason.GatekeeperDisabled,
                    0.0f,
                    0.0f,
                    previousText?.Length ?? 0,
                    currentText.Length,
                    stopwatch.Elapsed);

                RecordDecision(disabledDecision);
                return disabledDecision;
            }

            // 除外ゾーンチェック
            if (_settings.EnableExclusionZoneCheck && region?.IsInExclusionZone == true)
            {
                var exclusionDecision = CreateDecision(
                    false,
                    GatekeeperReason.InExclusionZone,
                    0.0f,
                    0.0f,
                    previousText?.Length ?? 0,
                    currentText.Length,
                    stopwatch.Elapsed);

                RecordDecision(exclusionDecision);
                return exclusionDecision;
            }

            // 空テキストチェック
            if (_settings.SkipEmptyText && string.IsNullOrWhiteSpace(currentText))
            {
                var emptyDecision = CreateDecision(
                    false,
                    GatekeeperReason.EmptyText,
                    0.0f,
                    0.0f,
                    previousText?.Length ?? 0,
                    0,
                    stopwatch.Elapsed);

                RecordDecision(emptyDecision);
                return emptyDecision;
            }

            // 最小長チェック
            if (currentText.Length < _settings.MinTextLength)
            {
                var shortDecision = CreateDecision(
                    false,
                    GatekeeperReason.EmptyText,
                    0.0f,
                    0.0f,
                    previousText?.Length ?? 0,
                    currentText.Length,
                    stopwatch.Elapsed);

                RecordDecision(shortDecision);
                return shortDecision;
            }

            // 初回テキスト
            if (string.IsNullOrEmpty(previousText))
            {
                if (_settings.AlwaysTranslateFirstText)
                {
                    var firstDecision = CreateDecision(
                        true,
                        GatekeeperReason.FirstText,
                        1.0f,
                        0.0f,
                        0,
                        currentText.Length,
                        stopwatch.Elapsed);

                    RecordDecision(firstDecision);
                    return firstDecision;
                }
            }

            // 同一テキストチェック
            if (_settings.SkipIdenticalText && previousText == currentText)
            {
                var identicalDecision = CreateDecision(
                    false,
                    GatekeeperReason.IdenticalText,
                    0.0f,
                    0.0f,
                    previousText?.Length ?? 0,
                    currentText.Length,
                    stopwatch.Elapsed);

                RecordDecision(identicalDecision);
                return identicalDecision;
            }

            // 長さ変化による強制翻訳チェック
            if (_settings.EnableLengthChangeForceTranslate && previousText != null)
            {
                var lengthChangeRatio = CalculateLengthChangeRatio(previousText.Length, currentText.Length);
                if (lengthChangeRatio >= _settings.LengthChangeForceThreshold)
                {
                    var lengthDecision = CreateDecision(
                        true,
                        GatekeeperReason.SignificantLengthChange,
                        lengthChangeRatio,
                        _settings.LengthChangeForceThreshold,
                        previousText.Length,
                        currentText.Length,
                        stopwatch.Elapsed);

                    RecordDecision(lengthDecision);
                    return lengthDecision;
                }
            }

            // 変化率を計算
            var changeRatio = CalculateChangeRatio(previousText ?? "", currentText);

            // 適用する閾値を決定
            var threshold = GetAppliedThreshold(currentText.Length, region);

            // 判定
            var shouldAllow = changeRatio >= threshold;
            var reason = GetReasonForTextLength(currentText.Length, shouldAllow);

            var decision = CreateDecision(
                shouldAllow,
                reason,
                changeRatio,
                threshold,
                previousText?.Length ?? 0,
                currentText.Length,
                stopwatch.Elapsed);

            RecordDecision(decision);

            _logger.LogDebug(
                "Gatekeeper decision: {Decision} - Reason={Reason}, ChangeRatio={Ratio:F3}, Threshold={Threshold:F3}, TextLength={Length}",
                shouldAllow ? "ALLOW" : "DENY", reason, changeRatio, threshold, currentText.Length);

            return decision;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Gatekeeper ShouldTranslate - allowing translation");

            // エラー時は安全側で許可
            return CreateDecision(
                true,
                GatekeeperReason.ForcedAllow,
                0.0f,
                0.0f,
                previousText?.Length ?? 0,
                currentText.Length,
                stopwatch.Elapsed);
        }
    }

    /// <inheritdoc />
    public void ReportTranslationResult(GatekeeperDecision decision, bool wasSuccessful, int tokensUsed)
    {
        if (!_settings.EnableStatistics)
        {
            return;
        }

        lock (_statisticsLock)
        {
            _actualTokensUsed += tokensUsed;
        }

        _logger.LogTrace(
            "Translation result: Success={Success}, Tokens={Tokens}, DecisionId={DecisionId}",
            wasSuccessful, tokensUsed, decision.DecisionId);
    }

    /// <inheritdoc />
    public void ResetStatistics()
    {
        lock (_statisticsLock)
        {
            _totalDecisions = 0;
            _allowedCount = 0;
            _deniedCount = 0;
            _estimatedTokensSaved = 0;
            _actualTokensUsed = 0;
            _totalChangeRatio = 0;
            _lastDecisionAt = null;
        }

        _decisionsByReason.Clear();

        _logger.LogInformation("Gatekeeper statistics reset");
    }

    /// <inheritdoc />
    public GatekeeperStatistics GetStatistics()
    {
        lock (_statisticsLock)
        {
            var avgChangeRatio = _totalDecisions > 0 ? (float)(_totalChangeRatio / _totalDecisions) : 0.0f;

            return new GatekeeperStatistics
            {
                TotalDecisions = _totalDecisions,
                AllowedCount = _allowedCount,
                DeniedCount = _deniedCount,
                EstimatedTokensSaved = _estimatedTokensSaved,
                ActualTokensUsed = _actualTokensUsed,
                AverageChangeRatio = avgChangeRatio,
                LastDecisionAt = _lastDecisionAt,
                StatisticsStartedAt = _statisticsStartedAt,
                DecisionsByReason = new Dictionary<GatekeeperReason, long>(_decisionsByReason)
            };
        }
    }

    /// <summary>
    /// 判定を記録
    /// </summary>
    private void RecordDecision(GatekeeperDecision decision)
    {
        if (!_settings.EnableStatistics)
        {
            return;
        }

        lock (_statisticsLock)
        {
            _totalDecisions++;
            _totalChangeRatio += decision.ChangeRatio;
            _lastDecisionAt = decision.DecisionAt;

            if (decision.ShouldTranslate)
            {
                _allowedCount++;
            }
            else
            {
                _deniedCount++;

                // 拒否された場合、節約トークン数を推定
                var estimatedTokens = (int)(decision.CurrentTextLength * _settings.TokensPerCharacterEstimate);
                _estimatedTokensSaved += estimatedTokens;
            }
        }

        _decisionsByReason.AddOrUpdate(
            decision.Reason,
            1,
            (_, count) => count + 1);
    }

    /// <summary>
    /// GatekeeperDecisionを作成
    /// </summary>
    private static GatekeeperDecision CreateDecision(
        bool shouldTranslate,
        GatekeeperReason reason,
        float changeRatio,
        float appliedThreshold,
        int previousTextLength,
        int currentTextLength,
        TimeSpan processingTime)
    {
        return new GatekeeperDecision
        {
            ShouldTranslate = shouldTranslate,
            Reason = reason,
            ChangeRatio = changeRatio,
            AppliedThreshold = appliedThreshold,
            PreviousTextLength = previousTextLength,
            CurrentTextLength = currentTextLength,
            ProcessingTime = processingTime
        };
    }

    /// <summary>
    /// Levenshtein距離を使用して変化率を計算
    /// </summary>
    private static float CalculateChangeRatio(string previous, string current)
    {
        if (previous.Length == 0 && current.Length == 0)
        {
            return 0.0f;
        }

        var distance = CalculateLevenshteinDistance(previous, current);
        var maxLength = Math.Max(previous.Length, current.Length);

        return (float)distance / maxLength;
    }

    /// <summary>
    /// Levenshtein距離を計算
    /// </summary>
    /// <remarks>
    /// [Issue #293] 最適化版:
    /// - 同一文字列の早期終了
    /// - stackalloc による短い文字列のスタック割り当て
    /// - ArrayPool による長い文字列のヒープ効率化
    /// </remarks>
    private static int CalculateLevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
        {
            return target?.Length ?? 0;
        }

        if (string.IsNullOrEmpty(target))
        {
            return source.Length;
        }

        // 最適化: 同一文字列の早期終了
        if (string.Equals(source, target, StringComparison.Ordinal))
        {
            return 0;
        }

        var sourceLength = source.Length;
        var targetLength = target.Length;

        // 最適化: 短い文字列が target になるように入れ替え（メモリ使用量削減）
        if (sourceLength < targetLength)
        {
            (source, target) = (target, source);
            (sourceLength, targetLength) = (targetLength, sourceLength);
        }

        // スタック割り当ての閾値（512要素 = 約2KB）
        const int StackAllocThreshold = 512;
        var bufferSize = (targetLength + 1) * 2;

        int[]? rentedArray = null;
        Span<int> buffer = bufferSize <= StackAllocThreshold
            ? stackalloc int[bufferSize]
            : (rentedArray = ArrayPool<int>.Shared.Rent(bufferSize));

        try
        {
            var previousRow = buffer[..(targetLength + 1)];
            var currentRow = buffer[(targetLength + 1)..];

            // 初期化
            for (var j = 0; j <= targetLength; j++)
            {
                previousRow[j] = j;
            }

            for (var i = 1; i <= sourceLength; i++)
            {
                currentRow[0] = i;

                for (var j = 1; j <= targetLength; j++)
                {
                    var cost = source[i - 1] == target[j - 1] ? 0 : 1;

                    currentRow[j] = Math.Min(
                        Math.Min(
                            currentRow[j - 1] + 1,      // 挿入
                            previousRow[j] + 1),        // 削除
                        previousRow[j - 1] + cost);     // 置換
                }

                // 行をスワップ（Spanなのでポインタ操作に最適化される）
                var temp = previousRow;
                previousRow = currentRow;
                currentRow = temp;
            }

            return previousRow[targetLength];
        }
        finally
        {
            if (rentedArray is not null)
            {
                ArrayPool<int>.Shared.Return(rentedArray);
            }
        }
    }

    /// <summary>
    /// テキスト長変化率を計算
    /// </summary>
    private static float CalculateLengthChangeRatio(int previousLength, int currentLength)
    {
        if (previousLength == 0 && currentLength == 0)
        {
            return 0.0f;
        }

        var maxLength = Math.Max(previousLength, currentLength);
        var lengthDiff = Math.Abs(previousLength - currentLength);

        return (float)lengthDiff / maxLength;
    }

    /// <summary>
    /// 適用する閾値を取得
    /// </summary>
    private float GetAppliedThreshold(int textLength, GatekeeperRegionInfo? region)
    {
        var baseThreshold = _settings.GetThresholdForTextLength(textLength);

        // ROI信頼度による閾値調整
        if (_settings.EnableConfidenceBasedThresholdAdjustment && region?.ConfidenceScore.HasValue == true)
        {
            if (region.ConfidenceScore >= 0.7f) // 高信頼度
            {
                baseThreshold *= _settings.HighConfidenceThresholdMultiplier;
            }
        }

        // [Issue #293] ROI学習ヒートマップによる動的閾値調整
        // 高ヒートマップ領域（テキストが頻繁に検出される）は閾値を下げて感度アップ
        // 低ヒートマップ領域は閾値を上げてノイズ除去
        if (_settings.EnableHeatmapBasedThresholdAdjustment && region?.HeatmapValue.HasValue == true)
        {
            var heatmapValue = region.HeatmapValue.Value;

            if (heatmapValue >= _settings.HighHeatmapThreshold)
            {
                // 高ヒートマップ領域：閾値を下げる（小さな変化でも翻訳トリガー）
                baseThreshold *= _settings.HighHeatmapThresholdMultiplier;
                _logger.LogDebug(
                    "[Issue #293] High heatmap region detected: HeatmapValue={Heatmap:F3}, Threshold multiplier={Multiplier:F2}",
                    heatmapValue, _settings.HighHeatmapThresholdMultiplier);
            }
            else if (heatmapValue <= _settings.LowHeatmapThreshold)
            {
                // 低ヒートマップ領域：閾値を上げる（大きな変化のみ翻訳トリガー）
                baseThreshold *= _settings.LowHeatmapThresholdMultiplier;
                _logger.LogDebug(
                    "[Issue #293] Low heatmap region detected: HeatmapValue={Heatmap:F3}, Threshold multiplier={Multiplier:F2}",
                    heatmapValue, _settings.LowHeatmapThresholdMultiplier);
            }
            // else: 中間領域は調整なし
        }

        return Math.Clamp(baseThreshold, 0.0f, 1.0f);
    }

    /// <summary>
    /// テキスト長に基づいて判定理由を取得
    /// </summary>
    private GatekeeperReason GetReasonForTextLength(int textLength, bool allowed)
    {
        if (!allowed)
        {
            return GatekeeperReason.InsufficientChange;
        }

        if (textLength <= _settings.ShortTextThreshold)
        {
            return GatekeeperReason.ShortTextChange;
        }

        if (textLength >= _settings.LongTextThreshold)
        {
            return GatekeeperReason.LongTextChange;
        }

        return GatekeeperReason.SufficientChange;
    }
}
