using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
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
            else if ((normalizedCurr.StartsWith(normalizedPrev, StringComparison.Ordinal) ||
                      NormalizeSpaceless(normalizedCurr).StartsWith(NormalizeSpaceless(normalizedPrev), StringComparison.Ordinal))
                     && normalizedCurr.Length > normalizedPrev.Length)
            {
                // [Issue #397] P0-2: タイプライター演出検出
                // 現在テキストが前回テキストを包含し末尾が成長 → 演出中と判定
                _typewriterInProgress[contextId] = true;
                _logger.LogDebug(
                    "[Issue #397] タイプライター演出検出 - 翻訳を遅延 (PrevLen={PrevLen}, CurrLen={CurrLen}, Growth=+{Growth})",
                    previousText.Length, currentText.Length, currentText.Length - previousText.Length);
                changeResult = TextChangeResult.CreateNoChange(previousText, stopwatch.Elapsed);
            }
            else
            {
                // テキストが完全に異なる（シーン切替等） → タイプライター状態をクリアして通常判定
                _typewriterInProgress.TryRemove(contextId, out _);
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

    [GeneratedRegex(@"\s+")]
    private static partial Regex NormalizeWhitespaceRegex();
}

