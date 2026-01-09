using System.Diagnostics;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Validation;
using Baketa.Core.Models.Validation;
using Baketa.Core.Translation.Abstractions;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Validation;

/// <summary>
/// ローカルOCRとCloud AI結果の相互検証実装
/// </summary>
/// <remarks>
/// Issue #78 Phase 3: 相互検証ロジック
/// - 両方で検出 → 採用
/// - 片方のみ → 除外
/// - 信頼度 &lt; 0.30 → 除外
/// - 低信頼度でもCloud AIと一致 → 救済
///
/// Phase 3.5: 双方向マッチング
/// - 統合方向: 複数ローカル ⊂ 1 Cloud AI → Force Merge
/// - 分割方向: 1 ローカル ⊃ 複数 Cloud AI → Split
/// </remarks>
public sealed class CrossValidator : ICrossValidator
{
    private readonly IFuzzyTextMatcher _fuzzyMatcher;
    private readonly IConfidenceRescuer _rescuer;
    private readonly IContainmentMatcher? _containmentMatcher;
    private readonly ILogger<CrossValidator> _logger;

    // 信頼度閾値
    private const float MinConfidenceThreshold = 0.30f;
    private const float NormalConfidenceThreshold = 0.70f;

    // 分割チャンクID生成用の乗数（Geminiレビュー反映: マジックナンバー定数化）
    private const int SplitChunkIdMultiplier = 1000;

    /// <summary>
    /// コンストラクタ（Phase 3互換）
    /// </summary>
    public CrossValidator(
        IFuzzyTextMatcher fuzzyMatcher,
        IConfidenceRescuer rescuer,
        ILogger<CrossValidator> logger)
        : this(fuzzyMatcher, rescuer, null, logger)
    {
    }

    /// <summary>
    /// コンストラクタ（Phase 3.5対応）
    /// </summary>
    public CrossValidator(
        IFuzzyTextMatcher fuzzyMatcher,
        IConfidenceRescuer rescuer,
        IContainmentMatcher? containmentMatcher,
        ILogger<CrossValidator> logger)
    {
        _fuzzyMatcher = fuzzyMatcher ?? throw new ArgumentNullException(nameof(fuzzyMatcher));
        _rescuer = rescuer ?? throw new ArgumentNullException(nameof(rescuer));
        _containmentMatcher = containmentMatcher; // null許容（Phase 3.5オプション）
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<CrossValidationResult> ValidateAsync(
        IReadOnlyList<TextChunk> localOcrChunks,
        ImageTranslationResponse cloudAiResponse,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // 空チェック
        if (localOcrChunks.Count == 0 || !cloudAiResponse.IsSuccess)
        {
            _logger.LogDebug(
                "相互検証スキップ: ローカルOCRチャンク数={ChunkCount}, CloudAI成功={IsSuccess}",
                localOcrChunks.Count,
                cloudAiResponse.IsSuccess);

            return Task.FromResult(CrossValidationResult.Empty(stopwatch.Elapsed));
        }

        // Cloud AI検出テキストを分割してリスト化（Issue #242: Textsプロパティ優先）
        var cloudDetectedTexts = ExtractCloudDetectedTexts(cloudAiResponse);

        _logger.LogDebug(
            "相互検証開始: ローカルOCR={LocalCount}チャンク, CloudAI={CloudCount}テキスト",
            localOcrChunks.Count,
            cloudDetectedTexts.Count);

        var validatedChunks = new List<ValidatedTextChunk>();
        var stats = new ValidationStatisticsBuilder
        {
            TotalLocalChunks = localOcrChunks.Count,
            TotalCloudDetections = cloudDetectedTexts.Count
        };

        // Cloud AIの翻訳テキストも分割（Issue #242: Textsプロパティ優先）
        var translatedTexts = ExtractCloudTranslatedTexts(cloudAiResponse);

        // Phase 3: ファジーマッチング
        var unmatchedChunks = new List<TextChunk>();

        foreach (var chunk in localOcrChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = ValidateChunk(chunk, cloudDetectedTexts, translatedTexts, stats);
            if (result != null)
            {
                validatedChunks.Add(result);
            }
            else if (chunk.AverageConfidence >= MinConfidenceThreshold)
            {
                // ファジーマッチング失敗だが信頼度は十分 → Phase 3.5フォールバック候補
                unmatchedChunks.Add(chunk);
            }
        }

        // Phase 3.5: 包含マッチングフォールバック
        // Issue #275: TranslatedTextItem（BoundingBox含む）を渡してAI座標を保持
        if (_containmentMatcher != null && unmatchedChunks.Count > 0)
        {
            ProcessContainmentFallback(
                unmatchedChunks,
                cloudDetectedTexts,
                cloudAiResponse.Texts ?? [],  // Issue #275: BoundingBox座標含むTranslatedTextItem
                translatedTexts,
                validatedChunks,
                stats,
                cancellationToken);
        }

        stopwatch.Stop();

        var statistics = new CrossValidationStatistics
        {
            TotalLocalChunks = stats.TotalLocalChunks,
            TotalCloudDetections = stats.TotalCloudDetections,
            CrossValidatedCount = stats.CrossValidatedCount,
            LocalOnlyCount = stats.LocalOnlyCount,
            CloudOnlyCount = stats.CloudOnlyCount,
            RescuedCount = stats.RescuedCount,
            FilteredByConfidenceCount = stats.FilteredByConfidenceCount,
            FilteredByMismatchCount = stats.FilteredByMismatchCount,
            ForceMergedCount = stats.ForceMergedCount,
            SplitCount = stats.SplitCount
        };

        _logger.LogInformation(
            "相互検証完了: 採用={Accepted}, 除外={Filtered}, 救済={Rescued}, 統合={Merged}, 分割={Split}, 時間={Time:F1}ms",
            statistics.CrossValidatedCount,
            statistics.FilteredByConfidenceCount + statistics.FilteredByMismatchCount,
            statistics.RescuedCount,
            statistics.ForceMergedCount,
            statistics.SplitCount,
            stopwatch.Elapsed.TotalMilliseconds);

        return Task.FromResult(CrossValidationResult.Create(
            validatedChunks,
            statistics,
            stopwatch.Elapsed));
    }

    /// <summary>
    /// Phase 3.5: 包含マッチングフォールバック処理
    /// </summary>
    /// <remarks>
    /// Issue #275: cloudTextItemsパラメータ追加でBoundingBox座標を保持
    /// </remarks>
    private void ProcessContainmentFallback(
        List<TextChunk> unmatchedChunks,
        IReadOnlyList<string> cloudDetectedTexts,
        IReadOnlyList<TranslatedTextItem> cloudTextItems,  // Issue #275: BoundingBox含む
        IReadOnlyList<string> translatedTexts,
        List<ValidatedTextChunk> validatedChunks,
        ValidationStatisticsBuilder stats,
        CancellationToken cancellationToken)
    {
        if (_containmentMatcher == null)
            return;

        _logger.LogDebug(
            "Phase 3.5 フォールバック開始: 未マッチ={UnmatchedCount}チャンク",
            unmatchedChunks.Count);

        // Step 3A: 統合グループ検出（複数ローカル ⊂ 1 Cloud AI）
        // FindMergeGroupsは文字列リストを使用（座標は統合されるため）
        var mergeGroups = _containmentMatcher.FindMergeGroups(unmatchedChunks, cloudDetectedTexts);
        var mergedChunkIds = new HashSet<int>();

        foreach (var group in mergeGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var mergedResult = ProcessMergeGroup(group, translatedTexts);
            if (mergedResult != null)
            {
                validatedChunks.Add(mergedResult);
                stats.ForceMergedCount++;

                // 統合済みチャンクを記録
                foreach (var chunk in group.LocalChunks)
                {
                    mergedChunkIds.Add(chunk.ChunkId);
                }
            }
        }

        // Step 3B: 分割検出（1 ローカル ⊃ 複数 Cloud AI）
        // Issue #275: FindSplitInfoにTranslatedTextItem（BoundingBox含む）を渡す
        foreach (var chunk in unmatchedChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 既に統合済みならスキップ
            if (mergedChunkIds.Contains(chunk.ChunkId))
                continue;

            var splitInfo = _containmentMatcher.FindSplitInfo(chunk, cloudTextItems);
            if (splitInfo != null)
            {
                var splitResults = ProcessSplitInfo(splitInfo, translatedTexts);
                validatedChunks.AddRange(splitResults);
                stats.SplitCount += splitResults.Count;
            }
        }

        _logger.LogDebug(
            "Phase 3.5 フォールバック完了: 統合={Merged}, 分割={Split}",
            stats.ForceMergedCount,
            stats.SplitCount);
    }

    /// <summary>
    /// 統合グループを処理
    /// </summary>
    /// <remarks>
    /// Geminiレビュー反映: ContainmentMatcher.FilterByProximityで既にソート済みのため、
    /// 冗長なソート処理を削除
    /// </remarks>
    private ValidatedTextChunk? ProcessMergeGroup(
        MergeGroup group,
        IReadOnlyList<string> translatedTexts)
    {
        if (group.LocalChunks.Count == 0)
            return null;

        // 注: group.LocalChunksはContainmentMatcher.FilterByProximityでソート済み
        var chunks = group.LocalChunks;

        // UnionRect計算
        var unionBounds = CalculateUnionRect(chunks);

        // 統合チャンク生成
        var mergedChunk = new TextChunk
        {
            ChunkId = chunks[0].ChunkId, // 最初のチャンクIDを継承
            CombinedText = string.Join(" ", chunks.Select(c => c.CombinedText)),
            CombinedBounds = unionBounds,
            TextResults = chunks.SelectMany(c => c.TextResults).ToArray(),
            SourceWindowHandle = chunks[0].SourceWindowHandle
        };

        // 翻訳テキスト取得
        var translation = group.CloudTextIndex < translatedTexts.Count
            ? translatedTexts[group.CloudTextIndex]
            : group.CloudText;

        _logger.LogDebug(
            "統合完了: {Count}チャンク → 1チャンク, CloudText='{CloudText}'",
            group.LocalChunks.Count,
            group.CloudText.Length > 30 ? group.CloudText[..30] + "..." : group.CloudText);

        return ValidatedTextChunk.ForceMerged(
            mergedChunk,
            translation,
            group.CloudText,
            group.LocalChunks.Count);
    }

    /// <summary>
    /// 分割情報を処理
    /// </summary>
    /// <remarks>
    /// Issue #275: CloudBoundingBoxがある場合はCloud AI座標を優先使用
    /// </remarks>
    private List<ValidatedTextChunk> ProcessSplitInfo(
        SplitInfo splitInfo,
        IReadOnlyList<string> translatedTexts)
    {
        var results = new List<ValidatedTextChunk>();
        var localBounds = splitInfo.LocalChunk.CombinedBounds;
        var localText = splitInfo.LocalChunk.CombinedText ?? string.Empty;

        foreach (var segment in splitInfo.Segments)
        {
            // 🔥 [Issue #275] 常にローカルOCR座標を使用
            // Cloud AI (Gemini) 座標は不安定で毎回位置がずれるため、
            // 安定したローカルOCR座標からテキスト長比率で按分計算する
            var ratio = localText.Length > 0
                ? (float)segment.StartIndex / localText.Length
                : 0f;
            var widthRatio = localText.Length > 0
                ? (float)segment.CloudText.Length / localText.Length
                : 1f;

            var splitBounds = new System.Drawing.Rectangle(
                localBounds.X + (int)(localBounds.Width * ratio),
                localBounds.Y,
                (int)(localBounds.Width * widthRatio),
                localBounds.Height
            );

            _logger.LogDebug(
                "分割（ローカルOCR座標）: CloudText='{CloudText}', Box=({X},{Y},{W},{H})",
                segment.CloudText.Length > 20 ? segment.CloudText[..20] + "..." : segment.CloudText,
                splitBounds.X, splitBounds.Y, splitBounds.Width, splitBounds.Height);

            // 分割チャンク生成
            var splitChunk = new TextChunk
            {
                ChunkId = splitInfo.LocalChunk.ChunkId * SplitChunkIdMultiplier + segment.CloudTextIndex,
                CombinedText = segment.CloudText,
                CombinedBounds = splitBounds,
                TextResults = [], // 分割後は個別のTextResultsは持たない
                SourceWindowHandle = splitInfo.LocalChunk.SourceWindowHandle
            };

            // 翻訳テキスト取得
            var translation = segment.CloudTextIndex < translatedTexts.Count
                ? translatedTexts[segment.CloudTextIndex]
                : segment.CloudText;

            results.Add(ValidatedTextChunk.Split(
                splitChunk,
                translation,
                segment.CloudText,
                splitInfo.LocalChunk.ChunkId));

            _logger.LogDebug(
                "分割完了: OriginalChunkId={OriginalId}, Segment='{CloudText}', Position={Start}-{End}",
                splitInfo.LocalChunk.ChunkId,
                segment.CloudText.Length > 20 ? segment.CloudText[..20] + "..." : segment.CloudText,
                segment.StartIndex,
                segment.EndIndex);
        }

        return results;
    }

    /// <summary>
    /// UnionRect計算
    /// </summary>
    private static System.Drawing.Rectangle CalculateUnionRect(IReadOnlyList<TextChunk> chunks)
    {
        if (chunks.Count == 0)
            return System.Drawing.Rectangle.Empty;

        var minX = chunks.Min(c => c.CombinedBounds.X);
        var minY = chunks.Min(c => c.CombinedBounds.Y);
        var maxRight = chunks.Max(c => c.CombinedBounds.Right);
        var maxBottom = chunks.Max(c => c.CombinedBounds.Bottom);

        return new System.Drawing.Rectangle(minX, minY, maxRight - minX, maxBottom - minY);
    }

    /// <summary>
    /// 個々のチャンクを検証
    /// </summary>
    private ValidatedTextChunk? ValidateChunk(
        TextChunk chunk,
        IReadOnlyList<string> cloudDetectedTexts,
        IReadOnlyList<string> translatedTexts,
        ValidationStatisticsBuilder stats)
    {
        var confidence = chunk.AverageConfidence;
        var localText = chunk.CombinedText;

        // 1. 信頼度 < 0.30 → 即除外
        // Geminiレビュー: 採用されないチャンクはインスタンス生成せずnullを返す
        if (confidence < MinConfidenceThreshold)
        {
            stats.FilteredByConfidenceCount++;
            _logger.LogDebug(
                "除外（低信頼度）: Text='{Text}', Confidence={Confidence:F3}",
                localText.Length > 20 ? localText[..20] + "..." : localText,
                confidence);

            return null; // 採用されないためインスタンス生成を省略
        }

        // 2. Cloud AIテキストとファジーマッチング
        var matchResult = FindBestMatch(localText, cloudDetectedTexts);

        if (matchResult.IsMatch)
        {
            // マッチ成功
            var translatedText = GetCorrespondingTranslation(
                matchResult.MatchedIndex,
                translatedTexts,
                cloudDetectedTexts);

            if (confidence >= NormalConfidenceThreshold)
            {
                // 通常採用
                stats.CrossValidatedCount++;
                _logger.LogDebug(
                    "採用（相互検証）: Text='{Text}', Confidence={Confidence:F3}, Similarity={Similarity:F3}",
                    localText.Length > 20 ? localText[..20] + "..." : localText,
                    confidence,
                    matchResult.Similarity);

                return ValidatedTextChunk.CrossValidated(
                    chunk,
                    translatedText,
                    matchResult.MatchedText,
                    matchResult.Similarity);
            }
            else
            {
                // 低信頼度だがCloud AIと一致 → 救済
                stats.RescuedCount++;
                _logger.LogDebug(
                    "救済: Text='{Text}', Confidence={Confidence:F3}, Similarity={Similarity:F3}",
                    localText.Length > 20 ? localText[..20] + "..." : localText,
                    confidence,
                    matchResult.Similarity);

                return ValidatedTextChunk.Rescued(
                    chunk,
                    translatedText,
                    matchResult.MatchedText,
                    matchResult.Similarity);
            }
        }

        // 3. マッチ失敗
        if (confidence >= NormalConfidenceThreshold)
        {
            // 高信頼度だがCloud AIで検出されず → LocalOnlyとして除外
            // Geminiレビュー: 採用されないためインスタンス生成を省略
            stats.LocalOnlyCount++;
            _logger.LogDebug(
                "除外（ローカルのみ）: Text='{Text}', Confidence={Confidence:F3}",
                localText.Length > 20 ? localText[..20] + "..." : localText,
                confidence);

            return null; // 採用されないためインスタンス生成を省略
        }
        else
        {
            // 低信頼度かつCloud AIでも検出されず → 救済試行
            var rescueResult = _rescuer.TryRescue(chunk, cloudDetectedTexts);

            if (rescueResult.IsRescued)
            {
                stats.RescuedCount++;
                var translatedText = GetTranslationForRescued(
                    rescueResult.MatchedCloudText,
                    cloudDetectedTexts,
                    translatedTexts);

                return ValidatedTextChunk.Rescued(
                    chunk,
                    translatedText,
                    rescueResult.MatchedCloudText ?? string.Empty,
                    rescueResult.MatchSimilarity);
            }

            // 救済失敗 → 除外
            // Geminiレビュー: 採用されないためインスタンス生成を省略
            stats.FilteredByMismatchCount++;
            _logger.LogDebug(
                "除外（不一致）: Text='{Text}', Confidence={Confidence:F3}",
                localText.Length > 20 ? localText[..20] + "..." : localText,
                confidence);

            return null; // 採用されないためインスタンス生成を省略
        }
    }

    /// <summary>
    /// Cloud AIテキストから最もマッチするものを検索
    /// </summary>
    private (bool IsMatch, float Similarity, int MatchedIndex, string MatchedText) FindBestMatch(
        string localText,
        IReadOnlyList<string> cloudTexts)
    {
        var bestSimilarity = 0f;
        var bestIndex = -1;
        var bestText = string.Empty;

        for (int i = 0; i < cloudTexts.Count; i++)
        {
            var cloudText = cloudTexts[i];
            var matchResult = _fuzzyMatcher.IsMatch(localText, cloudText);

            if (matchResult.IsMatch && matchResult.Similarity > bestSimilarity)
            {
                bestSimilarity = matchResult.Similarity;
                bestIndex = i;
                bestText = cloudText;
            }
        }

        return (bestIndex >= 0, bestSimilarity, bestIndex, bestText);
    }

    /// <summary>
    /// マッチしたCloud AIテキストに対応する翻訳を取得
    /// </summary>
    private static string GetCorrespondingTranslation(
        int matchedIndex,
        IReadOnlyList<string> translatedTexts,
        IReadOnlyList<string> cloudDetectedTexts)
    {
        // インデックスが翻訳テキストの範囲内なら対応する翻訳を返す
        if (matchedIndex >= 0 && matchedIndex < translatedTexts.Count)
        {
            return translatedTexts[matchedIndex];
        }

        // 翻訳テキストが1つなら全体の翻訳を返す
        if (translatedTexts.Count == 1)
        {
            return translatedTexts[0];
        }

        // フォールバック: 元のCloud検出テキストを返す
        if (matchedIndex >= 0 && matchedIndex < cloudDetectedTexts.Count)
        {
            return cloudDetectedTexts[matchedIndex];
        }

        return string.Empty;
    }

    /// <summary>
    /// 救済されたテキストの翻訳を取得
    /// </summary>
    private static string GetTranslationForRescued(
        string? matchedCloudText,
        IReadOnlyList<string> cloudDetectedTexts,
        IReadOnlyList<string> translatedTexts)
    {
        if (string.IsNullOrEmpty(matchedCloudText))
        {
            return string.Empty;
        }

        // マッチしたCloud検出テキストのインデックスを探す
        for (int i = 0; i < cloudDetectedTexts.Count; i++)
        {
            if (cloudDetectedTexts[i] == matchedCloudText)
            {
                return i < translatedTexts.Count ? translatedTexts[i] : matchedCloudText;
            }
        }

        // 見つからない場合は翻訳テキストが1つなら全体を返す
        return translatedTexts.Count == 1 ? translatedTexts[0] : matchedCloudText;
    }

    /// <summary>
    /// Cloud AIの検出テキストをリスト化（Issue #242: Textsプロパティ優先）
    /// </summary>
    /// <remarks>
    /// Issue #242対応: Textsプロパティがある場合はそちらを使用
    /// 後方互換性のため、Textsがない場合はDetectedTextを改行で分割
    /// </remarks>
    private static List<string> ExtractCloudDetectedTexts(ImageTranslationResponse response)
    {
        // Issue #242: Textsプロパティがある場合は優先使用
        if (response.Texts is { Count: > 0 })
        {
            return response.Texts.Select(t => t.Original).ToList();
        }

        // 後方互換性: DetectedTextを改行で分割
        return ExtractTextByLineBreak(response.DetectedText);
    }

    /// <summary>
    /// Cloud AIの翻訳テキストをリスト化（Issue #242: Textsプロパティ優先）
    /// </summary>
    private static List<string> ExtractCloudTranslatedTexts(ImageTranslationResponse response)
    {
        // Issue #242: Textsプロパティがある場合は優先使用
        if (response.Texts is { Count: > 0 })
        {
            return response.Texts.Select(t => t.Translation).ToList();
        }

        // 後方互換性: TranslatedTextを改行で分割
        return ExtractTextByLineBreak(response.TranslatedText);
    }

    /// <summary>
    /// テキストを改行で分割してリスト化（後方互換性用ヘルパー）
    /// </summary>
    /// <remarks>
    /// 改行文字で分割してテキスト要素のリストを取得
    /// Geminiレビュー反映: 到達不能コードを削除（string.Splitは区切り文字がない場合も
    /// 元の文字列を単一要素の配列で返すため、texts.Count == 0にならない）
    /// </remarks>
    private static List<string> ExtractTextByLineBreak(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        // 改行で分割（C# 12 コレクション式）
        char[] separators = ['\n', '\r'];
        return text
            .Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();
    }

    /// <summary>
    /// 統計情報ビルダー
    /// </summary>
    private sealed class ValidationStatisticsBuilder
    {
        public int TotalLocalChunks { get; set; }
        public int TotalCloudDetections { get; set; }
        public int CrossValidatedCount { get; set; }
        public int LocalOnlyCount { get; set; }
        public int CloudOnlyCount { get; set; }
        public int RescuedCount { get; set; }
        public int FilteredByConfidenceCount { get; set; }
        public int FilteredByMismatchCount { get; set; }
        public int ForceMergedCount { get; set; }
        public int SplitCount { get; set; }
    }
}
