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
            else if (normalizedPrev == normalizedCurr)
            {
                // [Issue #397] 正規化後のテキストが同一
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
            else if (normalizedCurr.StartsWith(normalizedPrev, StringComparison.Ordinal) && normalizedCurr.Length > normalizedPrev.Length)
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
            var hasSignificantChange = changeResult.HasChanged && changeResult.ChangePercentage >= threshold;

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
    /// [Issue #397] テキスト比較用の正規化（OCRの空白・改行揺れを吸収）
    /// </summary>
    private static string NormalizeForComparison(string text)
    {
        return NormalizeWhitespaceRegex().Replace(text, " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex NormalizeWhitespaceRegex();
}

