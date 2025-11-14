using System.Collections.Concurrent;
using System.Diagnostics;
using Baketa.Core.Abstractions.Processing;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Text.ChangeDetection;

/// <summary>
/// テキスト変化検知サービス
/// OCR結果テキストの効率的な変化検知を提供
/// Geminiフィードバック反映: ConcurrentDictionaryによるスレッドセーフ実装
/// </summary>
public class TextChangeDetectionService : ITextChangeDetectionService
{
    private readonly ILogger<TextChangeDetectionService> _logger;
    private readonly ConcurrentDictionary<string, string> _previousTextCache = new();

    public TextChangeDetectionService(ILogger<TextChangeDetectionService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TextChangeResult> DetectTextChangeAsync(string previousText, string currentText, string contextId)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 入力検証
            currentText ??= string.Empty;
            previousText ??= string.Empty;

            // 完全一致チェック（最も高速）
            if (string.Equals(previousText, currentText, StringComparison.Ordinal))
            {
                _logger.LogDebug("テキスト変化なし (完全一致) - Context: {ContextId}", contextId);
                return TextChangeResult.CreateNoChange(previousText, stopwatch.Elapsed);
            }

            // 両方空文字列の場合
            if (string.IsNullOrEmpty(previousText) && string.IsNullOrEmpty(currentText))
            {
                return TextChangeResult.CreateNoChange(string.Empty, stopwatch.Elapsed);
            }

            // どちらか一方が空文字列の場合
            if (string.IsNullOrEmpty(previousText) || string.IsNullOrEmpty(currentText))
            {
                _logger.LogDebug("テキスト変化検知 (一方が空) - Context: {ContextId}, Previous: {PrevLen}, Current: {CurrLen}",
                    contextId, previousText?.Length ?? 0, currentText.Length);
                return TextChangeResult.CreateSignificantChange(previousText, currentText, 1.0f, stopwatch.Elapsed);
            }

            // Edit Distance（Levenshtein Distance）による類似度計算
            var editDistance = CalculateEditDistance(previousText, currentText);
            var maxLength = Math.Max(previousText.Length, currentText.Length);
            var changePercentage = maxLength > 0 ? editDistance / maxLength : 0f;

            var hasChanged = IsSignificantTextChange(changePercentage);

            _logger.LogDebug("テキスト変化検知完了 - Context: {ContextId}, 変化: {HasChanged}, 変化率: {ChangePercentage:F3}, 編集距離: {EditDistance}",
                contextId, hasChanged, changePercentage, editDistance);

            // キャッシュ更新（スレッドセーフ）
            _previousTextCache.AddOrUpdate(contextId, currentText, (key, oldValue) => currentText);

            return new TextChangeResult
            {
                HasChanged = hasChanged,
                ChangePercentage = changePercentage,
                EditDistance = editDistance,
                PreviousLength = previousText.Length,
                CurrentLength = currentText.Length,
                ProcessingTime = stopwatch.Elapsed,
                AlgorithmUsed = TextChangeAlgorithmType.EditDistance,
                PreviousText = previousText,
                CurrentText = currentText
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "テキスト変化検知エラー - Context: {ContextId}", contextId);
            // エラー時は変化ありとして安全側に処理
            return TextChangeResult.CreateSignificantChange(previousText, currentText, 1.0f, stopwatch.Elapsed);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    public float CalculateEditDistance(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1)) return text2?.Length ?? 0;
        if (string.IsNullOrEmpty(text2)) return text1.Length;

        var len1 = text1.Length;
        var len2 = text2.Length;

        // 効率化: 片方が著しく長い場合は早期リターン
        if (Math.Abs(len1 - len2) > Math.Max(len1, len2) * 0.8)
        {
            return Math.Max(len1, len2);
        }

        // DP行列による編集距離計算
        var matrix = new int[len1 + 1, len2 + 1];

        // 初期化
        for (int i = 0; i <= len1; i++) matrix[i, 0] = i;
        for (int j = 0; j <= len2; j++) matrix[0, j] = j;

        // DP計算
        for (int i = 1; i <= len1; i++)
        {
            for (int j = 1; j <= len2; j++)
            {
                var cost = text1[i - 1] == text2[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[len1, len2];
    }

    public bool IsSignificantTextChange(float changePercentage, float threshold = 0.1f)
    {
        return changePercentage >= threshold;
    }

    public void ClearPreviousText(string contextId)
    {
        _previousTextCache.TryRemove(contextId, out _);
        _logger.LogDebug("前回テキストキャッシュクリア - Context: {ContextId}", contextId);
    }

    public void ClearAllPreviousTexts()
    {
        var clearedCount = _previousTextCache.Count;
        _previousTextCache.Clear();
        _logger.LogInformation("全前回テキストキャッシュクリア - クリア件数: {Count}", clearedCount);
    }

    /// <summary>
    /// キャッシュから前回テキストを取得
    /// </summary>
    public string? GetPreviousText(string contextId)
    {
        _previousTextCache.TryGetValue(contextId, out var previousText);
        return previousText;
    }

    /// <summary>
    /// 現在のキャッシュサイズを取得
    /// </summary>
    public int GetCacheSize()
    {
        return _previousTextCache.Count;
    }
}
