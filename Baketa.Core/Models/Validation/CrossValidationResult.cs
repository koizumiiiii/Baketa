namespace Baketa.Core.Models.Validation;

/// <summary>
/// 相互検証結果
/// </summary>
/// <remarks>
/// Issue #78 Phase 3: ローカルOCRとCloud AI結果の統合結果
/// </remarks>
public sealed class CrossValidationResult
{
    /// <summary>
    /// 検証済みテキストチャンクのリスト
    /// </summary>
    public required IReadOnlyList<ValidatedTextChunk> ValidatedChunks { get; init; }

    /// <summary>
    /// 統計情報
    /// </summary>
    public required CrossValidationStatistics Statistics { get; init; }

    /// <summary>
    /// 処理時間
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>
    /// 成功した結果を作成
    /// </summary>
    public static CrossValidationResult Create(
        IReadOnlyList<ValidatedTextChunk> validatedChunks,
        CrossValidationStatistics statistics,
        TimeSpan processingTime)
        => new()
        {
            ValidatedChunks = validatedChunks,
            Statistics = statistics,
            ProcessingTime = processingTime
        };

    /// <summary>
    /// 空の結果を作成
    /// </summary>
    public static CrossValidationResult Empty(TimeSpan processingTime)
        => new()
        {
            ValidatedChunks = [],
            Statistics = CrossValidationStatistics.Empty,
            ProcessingTime = processingTime
        };
}

/// <summary>
/// 相互検証統計情報
/// </summary>
public sealed record CrossValidationStatistics
{
    /// <summary>
    /// ローカルOCRチャンクの総数
    /// </summary>
    public int TotalLocalChunks { get; init; }

    /// <summary>
    /// Cloud AI検出テキストの総数
    /// </summary>
    public int TotalCloudDetections { get; init; }

    /// <summary>
    /// 相互検証で採用された数
    /// </summary>
    public int CrossValidatedCount { get; init; }

    /// <summary>
    /// ローカルのみで検出された数（除外）
    /// </summary>
    public int LocalOnlyCount { get; init; }

    /// <summary>
    /// Cloud AIのみで検出された数（除外）
    /// </summary>
    public int CloudOnlyCount { get; init; }

    /// <summary>
    /// 救済された数
    /// </summary>
    public int RescuedCount { get; init; }

    /// <summary>
    /// 信頼度でフィルタリングされた数
    /// </summary>
    public int FilteredByConfidenceCount { get; init; }

    /// <summary>
    /// 不一致でフィルタリングされた数
    /// </summary>
    public int FilteredByMismatchCount { get; init; }

    /// <summary>
    /// 強制統合された数（Phase 3.5: 複数ローカル→1 Cloud AI）
    /// </summary>
    public int ForceMergedCount { get; init; }

    /// <summary>
    /// 分割された数（Phase 3.5: 1ローカル→複数 Cloud AI）
    /// </summary>
    public int SplitCount { get; init; }

    /// <summary>
    /// 採用率
    /// </summary>
    public float AcceptanceRate => TotalLocalChunks > 0
        ? (float)(CrossValidatedCount + RescuedCount + ForceMergedCount + SplitCount) / TotalLocalChunks
        : 0f;

    /// <summary>
    /// 空の統計情報
    /// </summary>
    public static CrossValidationStatistics Empty => new();
}
