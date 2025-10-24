using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.PostProcessing;

/// <summary>
/// 近接度ベースのTextChunkグループ化サービス
/// 連結成分アルゴリズム（DFS）を使用してグループ化
/// </summary>
public sealed class ProximityGroupingService
{
    private readonly ChunkProximityAnalyzer _proximityAnalyzer;
    private readonly ILogger<ProximityGroupingService> _logger;

    public ProximityGroupingService(
        ChunkProximityAnalyzer proximityAnalyzer,
        ILogger<ProximityGroupingService> logger)
    {
        _proximityAnalyzer = proximityAnalyzer ?? throw new ArgumentNullException(nameof(proximityAnalyzer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// TextChunkリストを近接度でグループ化
    /// </summary>
    public List<List<TextChunk>> GroupByProximity(IReadOnlyList<TextChunk> chunks)
    {
        // 🚨 [ULTRA_DEBUG] GroupByProximityメソッド実行確認
        Console.WriteLine($"🚨🚨🚨 [ULTRA_DEBUG] GroupByProximity実行開始！ - Count: {chunks.Count}");
        _logger?.LogDebug($"🚨🚨🚨 [ULTRA_DEBUG] GroupByProximity実行開始！ - Count: {chunks.Count}");

        if (chunks.Count == 0)
        {
            _logger.LogDebug("チャンクが0個 - 空のグループリストを返します");
            return [];
        }

        if (chunks.Count == 1)
        {
            _logger.LogDebug("チャンクが1個 - 単一グループを返します");
            return [chunks.ToList()];
        }

        var startTime = DateTime.UtcNow;

        // 1. 近接度コンテキスト分析
        var context = _proximityAnalyzer.AnalyzeChunks(chunks);

        // 2. 連結成分探索でグループ化
        var groups = FindConnectedComponents(chunks, context);

        var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

        _logger.LogInformation(
            "🔗 近接度グループ化完了 - " +
            "入力:{InputCount}個 → 出力:{OutputCount}グループ, " +
            "処理時間:{ProcessingTime:F1}ms",
            chunks.Count, groups.Count, processingTime);

        // デバッグ情報出力
        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var chunkIds = string.Join(", ", group.Select(c => c.ChunkId));
            var texts = string.Join(" | ", group.Select(c =>
                (c.CombinedText ?? c.TextResults?.FirstOrDefault()?.Text ?? "").Trim()));

            _logger.LogDebug(
                "  グループ {Index}: {Count}個のチャンク [ID: {ChunkIds}] → 「{Texts}」",
                i + 1, group.Count, chunkIds,
                texts.Length > 100 ? texts[..100] + "..." : texts);
        }

        return groups;
    }

    /// <summary>
    /// 連結成分探索アルゴリズム（DFS）
    /// </summary>
    private List<List<TextChunk>> FindConnectedComponents(
        IReadOnlyList<TextChunk> chunks,
        ProximityContext context)
    {
        Console.WriteLine($"🚨🚨🚨 [ULTRA_DEBUG] FindConnectedComponents開始！ - Count: {chunks.Count}");
        _logger?.LogDebug($"🚨🚨🚨 [ULTRA_DEBUG] FindConnectedComponents開始！ - Count: {chunks.Count}");

        var groups = new List<List<TextChunk>>();
        var visited = new bool[chunks.Count];
        var chunksList = chunks.ToList(); // List操作のためのコピー

        Console.WriteLine($"🚨🚨🚨 [ULTRA_DEBUG] forループ開始 - chunks.Count: {chunks.Count}");
        _logger?.LogDebug($"🚨🚨🚨 [ULTRA_DEBUG] forループ開始 - chunks.Count: {chunks.Count}");

        for (int i = 0; i < chunks.Count; i++)
        {
            Console.WriteLine($"🚨 [ULTRA_DEBUG] forループ i={i}, visited[{i}]={visited[i]}");
            _logger?.LogDebug($"🚨 [ULTRA_DEBUG] forループ i={i}, visited[{i}]={visited[i]}");

            if (!visited[i])
            {
                Console.WriteLine($"🚨 [ULTRA_DEBUG] DepthFirstSearch呼び出し直前 - i={i}");
                _logger?.LogDebug($"🚨 [ULTRA_DEBUG] DepthFirstSearch呼び出し直前 - i={i}");

                var group = new List<TextChunk>();
                DepthFirstSearch(chunksList, i, visited, group, context);

                Console.WriteLine($"🚨 [ULTRA_DEBUG] DepthFirstSearch完了 - i={i}, group.Count={group.Count}");
                _logger?.LogDebug($"🚨 [ULTRA_DEBUG] DepthFirstSearch完了 - i={i}, group.Count={group.Count}");

                if (group.Count > 0)
                {
                    groups.Add(group);

                    _logger.LogDebug(
                        "新しいグループを発見 - 開始チャンク:{StartId}, グループサイズ:{Size}",
                        chunks[i].ChunkId, group.Count);
                }
            }
        }

        return groups;
    }

    /// <summary>
    /// 深度優先探索（DFS）
    /// スタックオーバーフロー防止のため反復実装
    /// </summary>
    private void DepthFirstSearch(
        List<TextChunk> chunks,
        int startIndex,
        bool[] visited,
        List<TextChunk> group,
        ProximityContext context)
    {
        Console.WriteLine($"🚨🚨🚨 [ULTRA_DEBUG] DepthFirstSearch開始！ - startIndex: {startIndex}, chunks.Count: {chunks.Count}");
        _logger?.LogDebug($"🚨🚨🚨 [ULTRA_DEBUG] DepthFirstSearch開始！ - startIndex: {startIndex}, chunks.Count: {chunks.Count}");

        var stack = new Stack<int>();
        stack.Push(startIndex);

        Console.WriteLine($"🚨 [ULTRA_DEBUG] whileループ開始 - stack.Count: {stack.Count}");
        _logger?.LogDebug($"🚨 [ULTRA_DEBUG] whileループ開始 - stack.Count: {stack.Count}");

        while (stack.Count > 0)
        {
            var currentIndex = stack.Pop();

            Console.WriteLine($"🚨 [ULTRA_DEBUG] stack.Pop() - currentIndex: {currentIndex}, visited[{currentIndex}]: {visited[currentIndex]}");
            _logger?.LogDebug($"🚨 [ULTRA_DEBUG] stack.Pop() - currentIndex: {currentIndex}, visited[{currentIndex}]: {visited[currentIndex]}");

            if (visited[currentIndex])
                continue;

            visited[currentIndex] = true;
            group.Add(chunks[currentIndex]);

            Console.WriteLine($"🚨 [ULTRA_DEBUG] チャンク追加 - currentIndex: {currentIndex}, group.Count: {group.Count}");
            _logger?.LogDebug($"🚨 [ULTRA_DEBUG] チャンク追加 - currentIndex: {currentIndex}, group.Count: {group.Count}");

            Console.WriteLine($"🚨 [ULTRA_DEBUG] 隣接チャンク探索開始 - chunks.Count: {chunks.Count}");
            _logger?.LogDebug($"🚨 [ULTRA_DEBUG] 隣接チャンク探索開始 - chunks.Count: {chunks.Count}");

            // 隣接するチャンクを探索
            for (int i = 0; i < chunks.Count; i++)
            {
                Console.WriteLine($"🚨 [ULTRA_DEBUG] forループ i={i}, visited[{i}]={visited[i]}");
                _logger?.LogDebug($"🚨 [ULTRA_DEBUG] forループ i={i}, visited[{i}]={visited[i]}");

                if (!visited[i])
                {
                    Console.WriteLine($"🚨 [ULTRA_DEBUG] IsProximityClose呼び出し直前 - currentIndex:{currentIndex}, i:{i}");
                    _logger?.LogDebug($"🚨 [ULTRA_DEBUG] IsProximityClose呼び出し直前 - currentIndex:{currentIndex}, i:{i}");

                    var isClose = _proximityAnalyzer.IsProximityClose(chunks[currentIndex], chunks[i], context);

                    Console.WriteLine($"🚨 [ULTRA_DEBUG] IsProximityClose結果 - isClose:{isClose}");
                    _logger?.LogDebug($"🚨 [ULTRA_DEBUG] IsProximityClose結果 - isClose:{isClose}");

                    if (isClose)
                    {
                        stack.Push(i);

                        _logger.LogTrace(
                            "近接チャンク発見 - {CurrentId} → {NextId}",
                            chunks[currentIndex].ChunkId, chunks[i].ChunkId);
                    }
                }
            }
        }
    }

    /// <summary>
    /// グループ化統計を取得
    /// </summary>
    public GroupingStatistics GetGroupingStatistics(
        IReadOnlyList<TextChunk> originalChunks,
        List<List<TextChunk>> groups)
    {
        var totalChunks = originalChunks.Count;
        var totalGroups = groups.Count;
        var largestGroupSize = groups.Count > 0 ? groups.Max(g => g.Count) : 0;
        var averageGroupSize = groups.Count > 0 ? groups.Average(g => g.Count) : 0;
        var compressionRatio = totalChunks > 0 ? (double)totalGroups / totalChunks : 0;

        return new GroupingStatistics
        {
            TotalInputChunks = totalChunks,
            TotalOutputGroups = totalGroups,
            LargestGroupSize = largestGroupSize,
            AverageGroupSize = averageGroupSize,
            CompressionRatio = compressionRatio
        };
    }
}

/// <summary>
/// グループ化統計情報
/// </summary>
public sealed record GroupingStatistics
{
    public int TotalInputChunks { get; init; }
    public int TotalOutputGroups { get; init; }
    public int LargestGroupSize { get; init; }
    public double AverageGroupSize { get; init; }
    public double CompressionRatio { get; init; }
}