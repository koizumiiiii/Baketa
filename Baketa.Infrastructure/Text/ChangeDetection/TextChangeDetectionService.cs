using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Abstractions.Text;
using Baketa.Core.Models.Text;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Text.ChangeDetection;

/// <summary>
/// [Issue #293] テキスト変化検知サービス（Gatekeeper機能統合版）
/// </summary>
/// <remarks>
/// OCR結果テキストの効率的な変化検知を提供。
/// Geminiフィードバック反映:
/// - ConcurrentDictionaryによるスレッドセーフ実装
/// - 既存Gatekeeperの機能を統合
/// - 最適化されたLevenshtein距離計算（stackalloc/ArrayPool）
/// - Strategyパターンによる責務分離
/// </remarks>
public partial class TextChangeDetectionService : ITextChangeDetectionService
{
    private readonly ILogger<TextChangeDetectionService> _logger;
    private readonly RoiGatekeeperSettings _settings;
    private readonly IGateStrategy _gateStrategy;
    private readonly ConcurrentDictionary<string, string> _previousTextCache = new();

    // [Issue #432] タイプライター演出検知用の状態追跡
    private readonly ConcurrentDictionary<string, int> _typewriterGrowthCycles = new();
    private readonly ConcurrentDictionary<string, int> _typewriterStabilizationCount = new();

    // [Issue #465] 静的UI要素検出用の状態追跡
    private readonly ConcurrentDictionary<string, int> _sameTextConsecutiveCount = new();
    private readonly ConcurrentDictionary<string, string> _staticUiMarkers = new(); // sourceId → normalizedText

    // [Issue #486] OCR確認ベースのテキスト安定性追跡
    // OCRがテキストを検出するたびにタイムスタンプを記録し、
    // TextDisappearanceの誤判定による不要なオーバーレイ削除を抑制する
    private readonly ConcurrentDictionary<string, DateTime> _textPresenceConfirmations = new();

    /// <summary>
    /// スタック割り当ての閾値（512要素 = 約2KB）
    /// </summary>
    private const int StackAllocThreshold = 512;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public TextChangeDetectionService(
        ILogger<TextChangeDetectionService> logger,
        IOptions<RoiGatekeeperSettings> settings,
        IGateStrategy? gateStrategy = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? RoiGatekeeperSettings.CreateDefault();
        _gateStrategy = gateStrategy ?? new DefaultGateStrategy(_settings);

        _logger.LogInformation(
            "[Issue #293] TextChangeDetectionService initialized with Gatekeeper: " +
            "ShortThreshold={Short}, MediumThreshold={Medium}, LongThreshold={Long}",
            _settings.ShortTextChangeThreshold,
            _settings.MediumTextChangeThreshold,
            _settings.LongTextChangeThreshold);
    }

    /// <inheritdoc />
    /// <remarks>
    /// [Issue #293] 後方互換性のために維持。
    /// 注意: previousText パラメータは無視され、内部キャッシュが使用されます。
    /// 新規コードでは DetectChangeWithGateAsync の使用を推奨。
    /// </remarks>
    [Obsolete("Use DetectChangeWithGateAsync instead. The previousText parameter is ignored and internal cache is used.")]
    public async Task<TextChangeResult> DetectTextChangeAsync(
        string previousText,
        string currentText,
        string contextId)
    {
        // Gate判定版を呼び出して結果を変換（後方互換性維持）
        var gateResult = await DetectChangeWithGateAsync(
            currentText,
            contextId,
            regionInfo: null,
            CancellationToken.None).ConfigureAwait(false);

        return new TextChangeResult
        {
            HasChanged = gateResult.ShouldTranslate,
            ChangePercentage = gateResult.ChangePercentage,
            EditDistance = gateResult.EditDistance,
            PreviousLength = previousText?.Length ?? 0,
            CurrentLength = currentText?.Length ?? 0,
            ProcessingTime = gateResult.ProcessingTime,
            AlgorithmUsed = TextChangeAlgorithmType.EditDistance,
            PreviousText = gateResult.PreviousText,
            CurrentText = gateResult.CurrentText
        };
    }

    /// <inheritdoc />
    public async Task<TextChangeWithGateResult> DetectChangeWithGateAsync(
        string currentText,
        string sourceId,
        GateRegionInfo? regionInfo = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // キャンセルチェック（長時間処理前）
            cancellationToken.ThrowIfCancellationRequested();

            currentText ??= string.Empty;

            // 除外ゾーンチェック
            if (_settings.EnableExclusionZoneCheck && regionInfo?.IsInExclusionZone == true)
            {
                _logger.LogDebug(
                    "[Issue #293] Text in exclusion zone, skipping - SourceId: {SourceId}",
                    sourceId);

                return TextChangeWithGateResult.CreateEmptyText(
                    GetPreviousText(sourceId),
                    0f,
                    stopwatch.Elapsed);
            }

            // 空テキストチェック
            if (_settings.SkipEmptyText && string.IsNullOrWhiteSpace(currentText))
            {
                _logger.LogDebug(
                    "[Issue #293] Empty text, skipping - SourceId: {SourceId}",
                    sourceId);

                return TextChangeWithGateResult.CreateEmptyText(
                    GetPreviousText(sourceId),
                    GetAppliedThreshold(0, regionInfo),
                    stopwatch.Elapsed);
            }

            // 最小文字数チェック
            if (currentText.Length < _settings.MinTextLength)
            {
                _logger.LogDebug(
                    "[Issue #293] Text too short ({Length} < {Min}), skipping - SourceId: {SourceId}",
                    currentText.Length, _settings.MinTextLength, sourceId);

                return TextChangeWithGateResult.CreateTextTooShort(
                    GetPreviousText(sourceId),
                    currentText,
                    GetAppliedThreshold(currentText.Length, regionInfo),
                    stopwatch.Elapsed);
            }

            // [Issue #465] 静的UI要素チェック（最も早い段階で判定）
            var normalizedCurrentForStaticUi = NormalizeFullWidthToHalfWidth(NormalizeOcrNoise(currentText));
            if (_settings.EnableStaticUiDetection
                && _staticUiMarkers.TryGetValue(sourceId, out var markedText)
                && markedText == normalizedCurrentForStaticUi)
            {
                _logger.LogDebug(
                    "[Issue #465] Static UI element detected, skipping - SourceId: {SourceId}, Text: {Text}",
                    sourceId, currentText.Length > 20 ? currentText[..20] + "..." : currentText);

                return TextChangeWithGateResult.CreateStaticUiElement(
                    currentText,
                    GetAppliedThreshold(currentText.Length, regionInfo),
                    stopwatch.Elapsed);
            }

            // 前回テキストを取得
            var previousText = GetPreviousText(sourceId);

            // 初回テキスト
            if (string.IsNullOrEmpty(previousText))
            {
                if (_settings.AlwaysTranslateFirstText)
                {
                    _logger.LogDebug(
                        "[Issue #293] First text detected - SourceId: {SourceId}, Length: {Length}",
                        sourceId, currentText.Length);

                    // キャッシュ更新
                    UpdatePreviousText(sourceId, currentText);

                    return TextChangeWithGateResult.CreateFirstText(
                        currentText,
                        GetAppliedThreshold(currentText.Length, regionInfo),
                        stopwatch.Elapsed);
                }
            }

            // 同一テキストチェック（[Issue #409] OCRノイズ正規化 + [Issue #432] 全角/半角正規化後に比較）
            var normalizedCurrent = NormalizeFullWidthToHalfWidth(NormalizeOcrNoise(currentText));
            var normalizedPrevious = NormalizeFullWidthToHalfWidth(NormalizeOcrNoise(previousText ?? string.Empty));
            if (_settings.SkipIdenticalText && normalizedPrevious == normalizedCurrent)
            {
                // [Issue #432] タイプライター成長中に同一テキストを検出 → 安定化判定
                if (_settings.EnableTypewriterDetection && _typewriterGrowthCycles.TryGetValue(sourceId, out var growthCycles) && growthCycles > 0)
                {
                    var stabilizationCount = _typewriterStabilizationCount.AddOrUpdate(sourceId, 1, (_, count) => count + 1);

                    if (stabilizationCount >= _settings.TypewriterStabilizationCycles)
                    {
                        // 安定化完了 → 翻訳実行
                        _logger.LogDebug(
                            "[Issue #432] Typewriter stabilized - SourceId: {SourceId}, GrowthCycles: {GrowthCycles}, StabilizationCount: {StabilizationCount}",
                            sourceId, growthCycles, stabilizationCount);

                        // タイプライター状態をリセット
                        _typewriterGrowthCycles.TryRemove(sourceId, out _);
                        _typewriterStabilizationCount.TryRemove(sourceId, out _);

                        return TextChangeWithGateResult.CreateSufficientChange(
                            previousText,
                            currentText,
                            1.0f,
                            GetAppliedThreshold(currentText.Length, regionInfo),
                            0,
                            stopwatch.Elapsed);
                    }

                    // まだ安定化待ち
                    _logger.LogDebug(
                        "[Issue #432] Typewriter waiting for stabilization - SourceId: {SourceId}, StabilizationCount: {StabilizationCount}/{Required}",
                        sourceId, stabilizationCount, _settings.TypewriterStabilizationCycles);

                    return TextChangeWithGateResult.CreateTypewriterGrowing(
                        previousText,
                        currentText,
                        GetAppliedThreshold(currentText.Length, regionInfo),
                        stopwatch.Elapsed);
                }

                // [Issue #465] 静的UI要素検出: 連続同一テキスト回数をインクリメント
                if (_settings.EnableStaticUiDetection)
                {
                    var consecutiveCount = _sameTextConsecutiveCount.AddOrUpdate(sourceId, 1, (_, count) => count + 1);
                    if (consecutiveCount >= _settings.StaticUiDetectionThreshold)
                    {
                        _staticUiMarkers[sourceId] = normalizedCurrent;
                        _logger.LogInformation(
                            "[Issue #465] Static UI element registered - SourceId: {SourceId}, ConsecutiveCount: {Count}",
                            sourceId, consecutiveCount);
                    }
                }

                _logger.LogDebug(
                    "[Issue #293] Identical text, skipping - SourceId: {SourceId}",
                    sourceId);

                return TextChangeWithGateResult.CreateSameText(
                    currentText,
                    GetAppliedThreshold(currentText.Length, regionInfo),
                    stopwatch.Elapsed);
            }

            // [Issue #465] テキストが変化した → 連続同一カウンタをリセット
            _sameTextConsecutiveCount.TryRemove(sourceId, out _);
            // テキストが変化した場合は静的UIマーカーも解除（動的テキストに変わった可能性）
            _staticUiMarkers.TryRemove(sourceId, out _);

            // [Issue #432] タイプライター演出検知（正規化後の前方一致で成長中かチェック）
            if (_settings.EnableTypewriterDetection && previousText != null
                && normalizedCurrent.StartsWith(normalizedPrevious, StringComparison.Ordinal)
                && normalizedCurrent.Length > normalizedPrevious.Length)
            {
                // 安定化カウンタをリセット（テキストが変化したため）
                _typewriterStabilizationCount.TryRemove(sourceId, out _);

                var currentGrowthCycles = _typewriterGrowthCycles.AddOrUpdate(sourceId, 1, (_, count) => count + 1);

                if (currentGrowthCycles >= _settings.TypewriterMaxDelayCycles)
                {
                    // 最大遅延超過 → 強制翻訳
                    _logger.LogDebug(
                        "[Issue #432] Typewriter max delay exceeded - SourceId: {SourceId}, GrowthCycles: {GrowthCycles}",
                        sourceId, currentGrowthCycles);

                    // タイプライター状態をリセット
                    _typewriterGrowthCycles.TryRemove(sourceId, out _);

                    // キャッシュ更新
                    UpdatePreviousText(sourceId, currentText);

                    return TextChangeWithGateResult.CreateTypewriterMaxDelayExceeded(
                        previousText,
                        currentText,
                        GetAppliedThreshold(currentText.Length, regionInfo),
                        stopwatch.Elapsed);
                }

                // 成長中 → 翻訳遅延（キャッシュは更新して次回比較用に最新テキストを保持）
                _logger.LogDebug(
                    "[Issue #432] Typewriter growing - SourceId: {SourceId}, GrowthCycles: {GrowthCycles}/{Max}, PrevLen: {PrevLen}, CurrLen: {CurrLen}",
                    sourceId, currentGrowthCycles, _settings.TypewriterMaxDelayCycles, previousText.Length, currentText.Length);

                UpdatePreviousText(sourceId, currentText);

                return TextChangeWithGateResult.CreateTypewriterGrowing(
                    previousText,
                    currentText,
                    GetAppliedThreshold(currentText.Length, regionInfo),
                    stopwatch.Elapsed);
            }

            // [Issue #432] 前方一致でなくなった → タイプライター状態リセット（シーン切替等）
            if (_typewriterGrowthCycles.ContainsKey(sourceId))
            {
                _logger.LogDebug(
                    "[Issue #432] Typewriter reset (non-prefix change) - SourceId: {SourceId}",
                    sourceId);
                _typewriterGrowthCycles.TryRemove(sourceId, out _);
                _typewriterStabilizationCount.TryRemove(sourceId, out _);
            }

            // 長さ変化による強制翻訳チェック（[Issue #409] 正規化後の長さで比較）
            if (_settings.EnableLengthChangeForceTranslate && previousText != null)
            {
                var lengthChangeRatio = CalculateLengthChangeRatio(normalizedPrevious.Length, normalizedCurrent.Length);
                if (lengthChangeRatio >= _settings.LengthChangeForceThreshold)
                {
                    _logger.LogDebug(
                        "[Issue #293] Significant length change ({Ratio:F3} >= {Threshold:F3}) - SourceId: {SourceId}",
                        lengthChangeRatio, _settings.LengthChangeForceThreshold, sourceId);

                    // キャッシュ更新
                    UpdatePreviousText(sourceId, currentText);

                    return TextChangeWithGateResult.CreateSignificantLengthChange(
                        previousText,
                        currentText,
                        lengthChangeRatio,
                        GetAppliedThreshold(currentText.Length, regionInfo),
                        stopwatch.Elapsed);
                }
            }

            // Levenshtein距離計算（最適化版）（[Issue #409] 正規化後テキストで計算）
            var editDistance = CalculateLevenshteinDistanceOptimized(normalizedPrevious, normalizedCurrent);
            var maxLength = Math.Max(normalizedPrevious.Length, normalizedCurrent.Length);
            var changePercentage = maxLength > 0 ? (float)editDistance / maxLength : 0f;

            // 適用する閾値を決定
            var threshold = GetAppliedThreshold(currentText.Length, regionInfo);

            // Gate判定（Strategyに委譲）
            var decision = _gateStrategy.Evaluate(changePercentage, threshold, currentText, previousText);
            var shouldTranslate = decision is GateDecision.SufficientChange or GateDecision.FirstText or GateDecision.SignificantLengthChange;

            _logger.LogDebug(
                "[Issue #293] Gate decision: {Decision} - SourceId: {SourceId}, ChangeRatio: {Ratio:F3}, Threshold: {Threshold:F3}, HeatmapValue: {Heatmap}",
                decision, sourceId, changePercentage, threshold, regionInfo?.HeatmapValue);

            // キャッシュ更新（翻訳実行時のみ）
            if (shouldTranslate)
            {
                UpdatePreviousText(sourceId, currentText);
            }

            if (shouldTranslate)
            {
                return TextChangeWithGateResult.CreateSufficientChange(
                    previousText,
                    currentText,
                    changePercentage,
                    threshold,
                    editDistance,
                    stopwatch.Elapsed);
            }
            else
            {
                return TextChangeWithGateResult.CreateInsufficientChange(
                    previousText,
                    currentText,
                    changePercentage,
                    threshold,
                    editDistance,
                    stopwatch.Elapsed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #293] Error in DetectChangeWithGateAsync - SourceId: {SourceId}", sourceId);

            // エラー時は安全側で許可
            return TextChangeWithGateResult.CreateFirstText(
                currentText ?? string.Empty,
                0f,
                stopwatch.Elapsed);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    /// <inheritdoc />
    public float CalculateEditDistance(string text1, string text2)
    {
        return CalculateLevenshteinDistanceOptimized(text1 ?? "", text2 ?? "");
    }

    /// <inheritdoc />
    public bool IsSignificantTextChange(float changePercentage, float threshold = 0.1f)
    {
        return changePercentage >= threshold;
    }

    /// <inheritdoc />
    public void ClearPreviousText(string contextId)
    {
        _previousTextCache.TryRemove(contextId, out _);
        // [Issue #432] タイプライター状態もクリア
        _typewriterGrowthCycles.TryRemove(contextId, out _);
        _typewriterStabilizationCount.TryRemove(contextId, out _);
        // [Issue #486] _textPresenceConfirmationsは意図的にクリアしない。
        // ClearPreviousTextはTextDisappearanceイベントから呼ばれるが、
        // 安定性タイムスタンプをここでクリアするとIsZoneStableチェックが無効化され、
        // 安定性追跡の目的が失われる。ClearAllPreviousTexts()（Start/Stop時）でのみクリアする。
        _logger.LogDebug("前回テキストキャッシュクリア - Context: {ContextId}", contextId);
    }

    /// <inheritdoc />
    public void ConfirmTextPresence(string sourceId)
    {
        _textPresenceConfirmations[sourceId] = DateTime.UtcNow;
    }

    /// <inheritdoc />
    public DateTime? GetLastTextConfirmation(string sourceId)
    {
        return _textPresenceConfirmations.TryGetValue(sourceId, out var timestamp) ? timestamp : null;
    }

    /// <inheritdoc />
    public void ClearAllPreviousTexts()
    {
        var clearedCount = _previousTextCache.Count;
        _previousTextCache.Clear();
        // [Issue #432] タイプライター状態もクリア
        _typewriterGrowthCycles.Clear();
        _typewriterStabilizationCount.Clear();
        // [Issue #465] 連続カウンタはクリアするが、静的UIマーカーは意図的に保持
        _sameTextConsecutiveCount.Clear();
        // [Issue #486] テキスト存在確認もクリア（Start/Stop切り替え時）
        _textPresenceConfirmations.Clear();
        _logger.LogInformation("全前回テキストキャッシュクリア - クリア件数: {Count}, 静的UIマーカー保持: {MarkerCount}",
            clearedCount, _staticUiMarkers.Count);
    }

    /// <inheritdoc />
    public void ClearStaticUiMarkers()
    {
        var markerCount = _staticUiMarkers.Count;
        _staticUiMarkers.Clear();
        _sameTextConsecutiveCount.Clear();
        _logger.LogInformation("[Issue #465] 静的UIマーカーをクリア - クリア件数: {Count}", markerCount);
    }

    /// <inheritdoc />
    public string? GetPreviousText(string contextId)
    {
        _previousTextCache.TryGetValue(contextId, out var previousText);
        return previousText;
    }

    /// <inheritdoc />
    public void SetPreviousText(string contextId, string text)
    {
        UpdatePreviousText(contextId, text);
    }

    /// <summary>
    /// 現在のキャッシュサイズを取得
    /// </summary>
    public int GetCacheSize() => _previousTextCache.Count;

    #region Private Methods

    /// <summary>
    /// [Issue #432] OCR全角/半角不一致を吸収するためのテキスト正規化。
    /// 全角ASCII (U+FF01～U+FF5E) を半角 (U+0021～U+007E) に変換する。
    /// </summary>
    private static string NormalizeFullWidthToHalfWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // 全角ASCII文字が含まれていなければそのまま返す（高速パス）
        var hasFullWidth = false;
        foreach (var c in text)
        {
            if (c is >= '\uFF01' and <= '\uFF5E')
            {
                hasFullWidth = true;
                break;
            }
        }

        if (!hasFullWidth)
        {
            return text;
        }

        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                span[i] = c is >= '\uFF01' and <= '\uFF5E'
                    ? (char)(c - 0xFEE0)
                    : c;
            }
        });
    }

    /// <summary>
    /// [Issue #409] OCR末尾ノイズ正規化。
    /// HTMLタグ除去、末尾装飾文字（●◎○◆◇■□▲△▼▽★☆※）除去、末尾空白トリミング。
    /// Gate比較前に適用し、OCRの揺れによる不要な再翻訳を防止する。
    /// </summary>
    internal static string NormalizeOcrNoise(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // HTMLタグ除去
        var result = HtmlTagRegex().Replace(text, "");

        // 末尾装飾文字除去
        result = result.TrimEnd('●', '◎', '○', '◆', '◇', '■', '□', '▲', '△', '▼', '▽', '★', '☆', '※');

        // 末尾空白トリミング
        result = result.TrimEnd();

        return result;
    }

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    /// <summary>
    /// 前回テキストを更新（スレッドセーフ）
    /// </summary>
    private void UpdatePreviousText(string sourceId, string text)
    {
        _previousTextCache.AddOrUpdate(sourceId, text, (_, _) => text);
        _logger.LogDebug(
            "[Issue #293] Previous text updated - SourceId: {SourceId}, Length: {Length}",
            sourceId, text.Length);
    }

    /// <summary>
    /// 適用する閾値を取得（ヒートマップ調整含む）
    /// </summary>
    private float GetAppliedThreshold(int textLength, GateRegionInfo? regionInfo)
    {
        var baseThreshold = _settings.GetThresholdForTextLength(textLength);

        // OCR信頼度による閾値調整
        if (_settings.EnableConfidenceBasedThresholdAdjustment && regionInfo?.ConfidenceScore is { } confidence)
        {
            if (confidence >= 0.7f)
            {
                baseThreshold *= _settings.HighConfidenceThresholdMultiplier;
            }
        }

        // [Issue #293] ヒートマップベース閾値調整
        if (_settings.EnableHeatmapBasedThresholdAdjustment && regionInfo?.HeatmapValue is { } heatmapValue)
        {
            if (heatmapValue >= _settings.HighHeatmapThreshold)
            {
                // 高ヒートマップ領域：閾値を下げる（小さな変化でも翻訳トリガー）
                baseThreshold *= _settings.HighHeatmapThresholdMultiplier;
                _logger.LogDebug(
                    "[Issue #293] High heatmap region: {Heatmap:F3}, threshold multiplier: {Multiplier:F2}",
                    heatmapValue, _settings.HighHeatmapThresholdMultiplier);
            }
            else if (heatmapValue <= _settings.LowHeatmapThreshold)
            {
                // 低ヒートマップ領域：閾値を上げる（大きな変化のみ翻訳トリガー）
                baseThreshold *= _settings.LowHeatmapThresholdMultiplier;
                _logger.LogDebug(
                    "[Issue #293] Low heatmap region: {Heatmap:F3}, threshold multiplier: {Multiplier:F2}",
                    heatmapValue, _settings.LowHeatmapThresholdMultiplier);
            }
        }

        return Math.Clamp(baseThreshold, 0.0f, 1.0f);
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
    /// [Issue #293] 最適化版Levenshtein距離計算
    /// </summary>
    /// <remarks>
    /// RoiGatekeeperから移植:
    /// - 同一文字列の早期終了
    /// - stackallocによる短い文字列のスタック割り当て
    /// - ArrayPoolによる長い文字列のヒープ効率化
    /// </remarks>
    private static int CalculateLevenshteinDistanceOptimized(string source, string target)
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

        // 最適化: 短い文字列がtargetになるように入れ替え（メモリ使用量削減）
        if (sourceLength < targetLength)
        {
            (source, target) = (target, source);
            (sourceLength, targetLength) = (targetLength, sourceLength);
        }

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

                // 行をスワップ
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

    #endregion
}

/// <summary>
/// [Issue #293] デフォルトのGate判定戦略
/// </summary>
/// <remarks>
/// Geminiフィードバック反映: Strategy パターンによる責務分離。
/// </remarks>
internal sealed class DefaultGateStrategy : IGateStrategy
{
    private readonly RoiGatekeeperSettings _settings;

    public DefaultGateStrategy(RoiGatekeeperSettings settings)
    {
        _settings = settings ?? RoiGatekeeperSettings.CreateDefault();
    }

    public GateDecision Evaluate(
        float changePercentage,
        float threshold,
        string currentText,
        string? previousText)
    {
        if (string.IsNullOrWhiteSpace(currentText))
        {
            return GateDecision.EmptyText;
        }

        if (currentText.Length < _settings.MinTextLength)
        {
            return GateDecision.TextTooShort;
        }

        if (previousText is null)
        {
            return GateDecision.FirstText;
        }

        if (string.Equals(currentText, previousText, StringComparison.Ordinal))
        {
            return GateDecision.SameText;
        }

        return changePercentage >= threshold
            ? GateDecision.SufficientChange
            : GateDecision.InsufficientChange;
    }
}
