using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Models.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Processing.Strategies;

/// <summary>
/// テキスト変化検知段階の処理戦略
/// OCR結果のテキストレベルでの変化を高速検出
/// </summary>
public partial class TextChangeDetectionStageStrategy : IProcessingStageStrategy
{
    private readonly ITextChangeDetectionService _textChangeService;
    private readonly IOptionsMonitor<ProcessingPipelineSettings> _settings;
    private readonly ILogger<TextChangeDetectionStageStrategy> _logger;

    // [Issue #397] P0-2: タイプライター演出検出の状態管理
    private readonly ConcurrentDictionary<string, bool> _typewriterInProgress = new();

    // [Issue #501] 前回のOCRテキスト領域キャッシュ（領域単位タイプライター判定用）
    private readonly ConcurrentDictionary<string, List<OcrTextRegion>> _previousTextRegions = new();

    // [Issue #486] パイプライン連続低変化率ブロック検出。
    // 変化率が0超だがthreshold未満の状態が連続した場合、前回テキストをリセットして
    // 次サイクルでパイプラインを通過させる。ゲームダイアログの小さな変化が
    // パイプラインを永久にブロックする問題を解決する。
    private readonly ConcurrentDictionary<string, int> _consecutiveLowChangeCount = new();

    /// <summary>
    /// [Issue #486] 連続低変化率でリセットするまでのサイクル数
    /// </summary>
    private const int ConsecutiveLowChangeResetThreshold = 3;

    public ProcessingStageType StageType => ProcessingStageType.TextChangeDetection;
    public TimeSpan EstimatedProcessingTime => TimeSpan.FromMilliseconds(1);

    public TextChangeDetectionStageStrategy(
        ITextChangeDetectionService textChangeService,
        IOptionsMonitor<ProcessingPipelineSettings> settings,
        ILogger<TextChangeDetectionStageStrategy> logger)
    {
        _textChangeService = textChangeService ?? throw new ArgumentNullException(nameof(textChangeService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProcessingStageResult> ExecuteAsync(ProcessingContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var ocrResult = context.GetStageResult<OcrExecutionResult>(ProcessingStageType.OcrExecution);
            if (ocrResult?.DetectedText == null)
            {
                _logger.LogWarning("OCR結果が取得できません - テキスト変化検知をスキップ");
                return ProcessingStageResult.CreateError(StageType, "OCR結果が取得できません", stopwatch.Elapsed);
            }

            // [Issue #500] Detection-Onlyフィルタによるスキップ → テキスト変化なしとして早期終了
            if (ocrResult.DetectionOnlySkipped)
            {
                _logger.LogDebug("[Issue #500] Detection-Onlyフィルタでスキップ済み - テキスト変化なしとして処理");
                var skipResult = new TextChangeDetectionResult
                {
                    HasTextChanged = false,
                    ChangePercentage = 0f,
                    PreviousText = context.Input.PreviousOcrText,
                    CurrentText = context.Input.PreviousOcrText,
                    ProcessingTime = stopwatch.Elapsed,
                    AlgorithmUsed = "DetectionOnlySkip"
                };
                return ProcessingStageResult.CreateSuccess(StageType, skipResult);
            }

            var currentText = ocrResult.DetectedText;
            var previousText = context.Input.PreviousOcrText;
            var contextId = context.Input.ContextId;

            _logger.LogDebug("テキスト変化検知開始 - ContextId: {ContextId}, CurrentLen: {CurrentLen}, PreviousLen: {PreviousLen}",
                contextId, currentText.Length, previousText?.Length ?? 0);

            TextChangeResult changeResult;

            var normalizedPrev = string.IsNullOrEmpty(previousText) ? "" : NormalizeForComparison(previousText);
            var normalizedCurr = NormalizeForComparison(currentText);

            if (string.IsNullOrEmpty(previousText))
            {
                // 初回実行時は変化ありとして処理継続
                _logger.LogDebug("初回テキスト検出 - 変化ありとして処理継続");
                changeResult = TextChangeResult.CreateFirstTime(currentText, stopwatch.Elapsed);
            }
            else if (normalizedPrev == normalizedCurr ||
                     NormalizeSpaceless(normalizedPrev) == NormalizeSpaceless(normalizedCurr))
            {
                // [Issue #397] 正規化後のテキストが同一（スペース除去比較含む）
                if (_typewriterInProgress.TryRemove(contextId, out _))
                {
                    // タイプライター完了 → 最終テキストを翻訳対象にする
                    // [Issue #501] 領域キャッシュもクリア
                    _previousTextRegions.TryRemove(contextId, out _);
                    _logger.LogInformation(
                        "[Issue #397] タイプライター完了検出 - 最終テキストを翻訳 (Len={Len})",
                        currentText.Length);
                    changeResult = TextChangeResult.CreateFirstTime(currentText, stopwatch.Elapsed);
                }
                else
                {
                    // 通常の同一テキスト → 変化なし
                    _logger.LogDebug("[Issue #397] テキスト正規化比較で同一判定 - 翻訳スキップ");
                    changeResult = TextChangeResult.CreateNoChange(previousText, stopwatch.Elapsed);
                }
            }
            else if (normalizedCurr.Length > normalizedPrev.Length)
            {
                // [Issue #501] 領域単位のタイプライター判定を優先
                var currentRegions = ocrResult.TextChunks.OfType<OcrTextRegion>().ToList();
                _previousTextRegions.TryGetValue(contextId, out var previousRegions);

                var isTypewriter = false;

                if (previousRegions != null && previousRegions.Count > 0 && currentRegions.Count > 0)
                {
                    // 領域単位の判定: 同一位置の領域内テキスト成長のみタイプライター
                    isTypewriter = IsTypewriterByRegion(currentRegions, previousRegions);
                    _logger.LogDebug(
                        "[Issue #501] 領域単位タイプライター判定: Result={IsTypewriter}, CurrRegions={CurrCount}, PrevRegions={PrevCount}",
                        isTypewriter, currentRegions.Count, previousRegions.Count);
                }
                else
                {
                    // フォールバック: 前回領域情報がない場合は既存の結合テキスト比較
                    isTypewriter = normalizedCurr.StartsWith(normalizedPrev, StringComparison.Ordinal) ||
                                   NormalizeSpaceless(normalizedCurr).StartsWith(NormalizeSpaceless(normalizedPrev), StringComparison.Ordinal);
                    _logger.LogDebug(
                        "[Issue #501] フォールバック判定（前回領域なし）: Result={IsTypewriter}, CurrRegions={CurrCount}",
                        isTypewriter, currentRegions.Count);
                }

                if (isTypewriter)
                {
                    _typewriterInProgress[contextId] = true;
                    _logger.LogDebug(
                        "[Issue #501] タイプライター演出検出（領域単位） - 翻訳を遅延 (PrevLen={PrevLen}, CurrLen={CurrLen})",
                        previousText.Length, currentText.Length);
                    changeResult = TextChangeResult.CreateNoChange(previousText, stopwatch.Elapsed);
                }
                else
                {
                    // テキストは成長しているが、領域単位ではタイプライターではない（OCRカバレッジ差分等）
                    // → 通常の変化検知に委任
                    _typewriterInProgress.TryRemove(contextId, out _);
                    changeResult = await _textChangeService.DetectTextChangeAsync(
                        previousText, currentText, contextId).ConfigureAwait(false);
                }
            }
            else
            {
                // テキストが完全に異なる（シーン切替等） → タイプライター状態をクリアして通常判定
                _typewriterInProgress.TryRemove(contextId, out _);
                // [Issue #501] 領域キャッシュもクリア
                _previousTextRegions.TryRemove(contextId, out _);
                changeResult = await _textChangeService.DetectTextChangeAsync(
                    previousText, currentText, contextId).ConfigureAwait(false);
            }

            var threshold = _settings.CurrentValue.TextChangeThreshold;
            // [Issue #410] Strategy層の閾値で独立判定
            // TextChangeDetectionService（Gatekeeper）は長さベースの高い閾値（例: 19%）を使用するが、
            // Strategy層のTextChangeThreshold（例: 10%）とは目的が異なる。
            // Service層のHasChangedに依存すると、17%の変化が19%閾値で抑制され、
            // ゲームダイアログの変化が永久に検出されないケースが発生する。
            var hasSignificantChange = changeResult.ChangePercentage >= threshold;

            // [Issue #486] 連続低変化率ブロック検出:
            // 変化率が0超だがthreshold未満の状態が連続した場合、前回テキストをリセット。
            // ゲームダイアログの小さな変化（例: 1ゾーンのみ更新で全体6.25%）が
            // パイプラインを永久にブロックする問題を解決する。
            if (!hasSignificantChange && changeResult.ChangePercentage > 0)
            {
                var lowChangeCount = _consecutiveLowChangeCount.AddOrUpdate(contextId, 1, (_, c) => c + 1);
                if (lowChangeCount >= ConsecutiveLowChangeResetThreshold)
                {
                    _logger.LogInformation(
                        "[Issue #486] {Count}回連続低変化率検知 - 前回テキストをリセットしてパイプラインを通過: " +
                        "ChangeRate={ChangeRate:F3}%, Threshold={Threshold:F1}%",
                        lowChangeCount, changeResult.ChangePercentage * 100, threshold * 100);

                    // 前回テキストを現在テキストに更新して、次サイクルの基準を最新にする
                    _textChangeService.SetPreviousText(contextId, currentText);
                    _consecutiveLowChangeCount.TryRemove(contextId, out _);
                    // 今回のサイクルを通過させる
                    hasSignificantChange = true;
                }
            }
            else
            {
                // 変化が十分 or 完全同一（0%） → カウンタリセット
                _consecutiveLowChangeCount.TryRemove(contextId, out _);
            }

            // Service層のキャッシュ同期: Strategy層が翻訳を決定したがService層が更新していない場合、
            // Service層のキャッシュを明示的に更新して次サイクルの比較基準を正しくする
            if (hasSignificantChange && !changeResult.HasChanged)
            {
                _textChangeService.SetPreviousText(contextId, currentText);
                _logger.LogInformation(
                    "[Issue #410] Strategy層が翻訳決定（Service層閾値超過で補正）: ChangeRatio={Ratio:F3}, StrategyThreshold={SThreshold:F3}, ServiceThreshold={ServiceThreshold}",
                    changeResult.ChangePercentage, threshold, "dynamic");
            }

            _logger.LogDebug("テキスト変化検知完了 - 変化: {HasChanged}, 変化率: {ChangePercentage:F3}%, しきい値: {Threshold:F1}%",
                hasSignificantChange, changeResult.ChangePercentage * 100, threshold * 100);

            var result = new TextChangeDetectionResult
            {
                HasTextChanged = hasSignificantChange,
                ChangePercentage = changeResult.ChangePercentage,
                PreviousText = previousText,
                CurrentText = currentText,
                ProcessingTime = stopwatch.Elapsed,
                AlgorithmUsed = changeResult.AlgorithmUsed.ToString()
            };

            // [Issue #501] 今回のOCR領域を次回比較用に保存
            var regionsToCache = ocrResult.TextChunks.OfType<OcrTextRegion>().ToList();
            if (regionsToCache.Count > 0)
            {
                _previousTextRegions[contextId] = regionsToCache;
            }

            return ProcessingStageResult.CreateSuccess(StageType, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "テキスト変化検知段階でエラーが発生");
            return ProcessingStageResult.CreateError(StageType, ex.Message, stopwatch.Elapsed);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    public bool ShouldExecute(ProcessingContext context)
    {
        // Stage 2でOCRが成功した場合のみ実行
        if (context.PreviousStageResult?.Success == true &&
            context.PreviousStageResult.Data is OcrExecutionResult ocrResult)
        {
            return ocrResult.Success && !string.IsNullOrEmpty(ocrResult.DetectedText);
        }

        // OCRステージの結果が存在する場合もチェック
        var existingOcrResult = context.GetStageResult<OcrExecutionResult>(ProcessingStageType.OcrExecution);
        return existingOcrResult?.Success == true && !string.IsNullOrEmpty(existingOcrResult.DetectedText);
    }

    /// <summary>
    /// [Issue #397][Issue #432] テキスト比較用の正規化（OCRの空白・改行揺れ＋全角/半角揺れを吸収）
    /// </summary>
    private static string NormalizeForComparison(string text)
    {
        var whitespaceNormalized = NormalizeWhitespaceRegex().Replace(text, " ").Trim();
        return NormalizeFullWidthToHalfWidth(whitespaceNormalized);
    }

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
    /// [Issue #413] スペース除去正規化（OCRグルーピング揺れによる単語境界差異を吸収）
    /// 例: "Sava Can I heck!" と "SavaCan I heck!" を同一と判定
    /// </summary>
    private static string NormalizeSpaceless(string text)
    {
        return text.Replace(" ", "");
    }

    /// <summary>
    /// [Issue #501] 領域単位でタイプライター演出を判定。
    /// 同一位置（IoU >= 閾値）の領域内でテキストが成長している場合のみタイプライター。
    /// 新規領域の追加や、異なる位置の領域変化はタイプライターとみなさない。
    /// </summary>
    private bool IsTypewriterByRegion(
        List<OcrTextRegion> currentRegions,
        List<OcrTextRegion> previousRegions)
    {
        if (currentRegions.Count == 0 || previousRegions.Count == 0)
            return false;

        const float iouThreshold = 0.5f;
        var hasGrowingRegion = false;

        foreach (var current in currentRegions)
        {
            var matchedPrevious = previousRegions
                .FirstOrDefault(prev => CalculateIoU(current.Bounds, prev.Bounds) >= iouThreshold);

            if (matchedPrevious == null)
                continue; // 新規領域 → タイプライター判定に関与しない

            var normalizedCurr = NormalizeForComparison(current.Text);
            var normalizedPrev = NormalizeForComparison(matchedPrevious.Text);

            if (normalizedCurr.Length > normalizedPrev.Length &&
                (normalizedCurr.StartsWith(normalizedPrev, StringComparison.Ordinal) ||
                 NormalizeSpaceless(normalizedCurr).StartsWith(NormalizeSpaceless(normalizedPrev), StringComparison.Ordinal)))
            {
                hasGrowingRegion = true;
            }
        }

        return hasGrowingRegion;
    }

    /// <summary>
    /// [Issue #501] 2つの矩形のIoU（Intersection over Union）を計算
    /// </summary>
    internal static float CalculateIoU(Rectangle a, Rectangle b)
    {
        var intersection = Rectangle.Intersect(a, b);
        if (intersection.IsEmpty)
            return 0f;

        float intersectionArea = (float)intersection.Width * intersection.Height;
        float unionArea = ((float)a.Width * a.Height) + ((float)b.Width * b.Height) - intersectionArea;

        return unionArea > 0 ? intersectionArea / unionArea : 0f;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex NormalizeWhitespaceRegex();
}

