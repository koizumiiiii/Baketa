using System.Globalization;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Validation;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Validation;

/// <summary>
/// 包含マッチング実装
/// </summary>
/// <remarks>
/// Issue #78 Phase 3.5: 双方向マッチング
/// - 統合方向: 複数ローカルチャンク ⊂ 1つのCloud AIテキスト → Force Merge
/// - 分割方向: 1つのローカルチャンク ⊃ 複数のCloud AIテキスト → Split
///
/// Geminiレビュー反映:
/// - 境界考慮マッチング（文字種変化を境界として判定）
/// - 最小文字数チェック（3文字未満は除外）
/// - 重複防止（IndexOfのstartIndexで対応）
/// - 近接チャンクのみ統合
/// </remarks>
public sealed class ContainmentMatcher : IContainmentMatcher
{
    private readonly ILogger<ContainmentMatcher> _logger;

    // 設定値
    private const int MinTextLengthForContainment = 3;
    private const int MaxProximityDistance = 100; // ピクセル単位

    // Unicode範囲定数（Geminiレビュー反映: クラスレベルに移動してスタイル一貫性向上）
    private const char HiraganaStart = '\u3040';
    private const char HiraganaEnd = '\u309F';
    private const char KatakanaStart = '\u30A0';
    private const char KatakanaEnd = '\u30FF';
    private const char CjkExtAStart = '\u3400';
    private const char CjkExtAEnd = '\u4DBF';
    private const char CjkUnifiedStart = '\u4E00';
    private const char CjkUnifiedEnd = '\u9FFF';

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public ContainmentMatcher(ILogger<ContainmentMatcher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsContainedWithBoundary(string text, string container)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(container))
            return false;

        // 最小文字数チェック
        if (text.Length < MinTextLengthForContainment)
            return false;

        // 明らかに包含不可能
        if (text.Length > container.Length)
            return false;

        var index = container.IndexOf(text, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return false;

        // 境界チェック
        var beforeOk = index == 0 || IsBoundary(container[index - 1], text[0]);
        var afterOk = index + text.Length >= container.Length ||
                      IsBoundary(text[^1], container[index + text.Length]);

        return beforeOk && afterOk;
    }

    /// <inheritdoc />
    public IReadOnlyList<MergeGroup> FindMergeGroups(
        IReadOnlyList<TextChunk> unmatchedChunks,
        IReadOnlyList<string> cloudTexts)
    {
        if (unmatchedChunks.Count == 0 || cloudTexts.Count == 0)
            return [];

        var mergeGroups = new List<MergeGroup>();

        // 各Cloud AIテキストに対して、含まれるローカルチャンクを探す
        for (int cloudIndex = 0; cloudIndex < cloudTexts.Count; cloudIndex++)
        {
            var cloudText = cloudTexts[cloudIndex];
            if (string.IsNullOrEmpty(cloudText))
                continue;

            var containedChunks = new List<TextChunk>();

            foreach (var chunk in unmatchedChunks)
            {
                var localText = chunk.CombinedText;
                if (string.IsNullOrEmpty(localText))
                    continue;

                if (IsContainedWithBoundary(localText, cloudText))
                {
                    containedChunks.Add(chunk);
                }
            }

            // 2つ以上のチャンクが含まれている場合のみ統合対象
            if (containedChunks.Count >= 2)
            {
                // 近接チャンクのみをフィルタリング
                var proximityFiltered = FilterByProximity(containedChunks);

                if (proximityFiltered.Count >= 2)
                {
                    mergeGroups.Add(new MergeGroup
                    {
                        LocalChunks = proximityFiltered,
                        CloudTextIndex = cloudIndex,
                        CloudText = cloudText
                    });

                    _logger.LogDebug(
                        "統合グループ検出: CloudText='{CloudText}', LocalChunks={Count}",
                        cloudText.Length > 30 ? cloudText[..30] + "..." : cloudText,
                        proximityFiltered.Count);
                }
            }
        }

        return mergeGroups;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Geminiレビュー反映: 最長一致優先ロジック
    /// 全候補をリストアップし、包含関係にあるものを除外して最長マッチを優先
    /// </remarks>
    public SplitInfo? FindSplitInfo(
        TextChunk unmatchedChunk,
        IReadOnlyList<string> cloudTexts)
    {
        var localText = unmatchedChunk.CombinedText;
        if (string.IsNullOrEmpty(localText))
            return null;

        // Step 1: 全候補をリストアップ（位置0から全て検索）
        var allCandidates = new List<SplitSegment>();

        for (int cloudIndex = 0; cloudIndex < cloudTexts.Count; cloudIndex++)
        {
            var cloudText = cloudTexts[cloudIndex];
            if (string.IsNullOrEmpty(cloudText))
                continue;

            // 最小文字数チェック
            if (cloudText.Length < MinTextLengthForContainment)
                continue;

            // ローカルテキスト内での全ての出現位置を検索
            int searchStart = 0;
            while (searchStart < localText.Length)
            {
                var index = localText.IndexOf(cloudText, searchStart, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    break;

                // 境界チェック
                var beforeOk = index == 0 || IsBoundary(localText[index - 1], cloudText[0]);
                var afterOk = index + cloudText.Length >= localText.Length ||
                              IsBoundary(cloudText[^1], localText[index + cloudText.Length]);

                if (beforeOk && afterOk)
                {
                    allCandidates.Add(new SplitSegment
                    {
                        CloudTextIndex = cloudIndex,
                        CloudText = cloudText,
                        StartIndex = index,
                        EndIndex = index + cloudText.Length
                    });
                }

                searchStart = index + 1;
            }
        }

        if (allCandidates.Count < 2)
            return null;

        // Step 2: 長い順にソート（最長一致優先）
        var sortedByLength = allCandidates
            .OrderByDescending(s => s.CloudText.Length)
            .ThenBy(s => s.StartIndex)
            .ToList();

        // Step 3: 重複除去（包含関係にあるものを除外）
        // Geminiレビュー反映: 範囲[A,B)と[X,Y)の重複条件 A < Y && X < B に簡素化
        var selectedSegments = new List<SplitSegment>();

        foreach (var candidate in sortedByLength)
        {
            // 既に選択されたセグメントと重複していないか確認
            var overlaps = selectedSegments.Any(s =>
                candidate.StartIndex < s.EndIndex && s.StartIndex < candidate.EndIndex);

            if (!overlaps)
            {
                selectedSegments.Add(candidate);

                _logger.LogDebug(
                    "分割セグメント検出: CloudText='{CloudText}', Position={Start}-{End}",
                    candidate.CloudText.Length > 20 ? candidate.CloudText[..20] + "..." : candidate.CloudText,
                    candidate.StartIndex,
                    candidate.EndIndex);
            }
        }

        // 2つ以上のセグメントが見つかった場合のみ分割対象
        if (selectedSegments.Count >= 2)
        {
            // 開始位置でソート
            var sortedSegments = selectedSegments.OrderBy(s => s.StartIndex).ToList();

            return new SplitInfo
            {
                LocalChunk = unmatchedChunk,
                Segments = sortedSegments
            };
        }

        return null;
    }

    /// <summary>
    /// 境界判定（空白・句読点・文字種変化）
    /// </summary>
    private static bool IsBoundary(char before, char after)
    {
        // 空白・句読点は常に境界
        if (char.IsWhiteSpace(before) || char.IsPunctuation(before))
            return true;

        // 文字種が変わったら境界（日本語対応）
        var beforeType = GetCharacterType(before);
        var afterType = GetCharacterType(after);

        return beforeType != afterType;
    }

    /// <summary>
    /// 文字種を判定
    /// </summary>
    /// <remarks>
    /// Geminiレビュー反映: CJK統合漢字拡張A/B対応、Unicode範囲定数化
    /// </remarks>
    private static CharacterType GetCharacterType(char c)
    {
        // ひらがな
        if (c >= HiraganaStart && c <= HiraganaEnd)
            return CharacterType.Hiragana;

        // カタカナ
        if (c >= KatakanaStart && c <= KatakanaEnd)
            return CharacterType.Katakana;

        // 漢字: CJK統合漢字 + 拡張A
        // 注: CJK統合漢字拡張B (U+20000-U+2A6DF) はサロゲートペアのためcharでは判定不可
        if ((c >= CjkExtAStart && c <= CjkExtAEnd) ||
            (c >= CjkUnifiedStart && c <= CjkUnifiedEnd))
            return CharacterType.Kanji;

        // 英字
        if (char.IsLetter(c))
            return CharacterType.Alphabet;

        // 数字
        if (char.IsDigit(c))
            return CharacterType.Digit;

        return CharacterType.Other;
    }

    /// <summary>
    /// 近接チャンクのみをフィルタリング（Union-Findクラスタリング）
    /// </summary>
    /// <remarks>
    /// Geminiレビュー反映: 全ペア比較による網羅的クラスタリング
    /// 隣接要素のみでなく、全てのペアを比較して連結成分を求める
    /// </remarks>
    private List<TextChunk> FilterByProximity(List<TextChunk> chunks)
    {
        if (chunks.Count <= 1)
            return chunks;

        var n = chunks.Count;
        var parent = new int[n];
        var rank = new int[n];

        // Union-Find初期化
        for (int i = 0; i < n; i++)
        {
            parent[i] = i;
            rank[i] = 0;
        }

        // 全ペアを比較してUnion
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                var distance = CalculateDistance(
                    chunks[i].CombinedBounds,
                    chunks[j].CombinedBounds);

                if (distance <= MaxProximityDistance)
                {
                    Union(parent, rank, i, j);
                }
            }
        }

        // 最大クラスタを見つける
        var clusterCounts = new Dictionary<int, int>();
        for (int i = 0; i < n; i++)
        {
            var root = Find(parent, i);
            clusterCounts[root] = clusterCounts.GetValueOrDefault(root, 0) + 1;
        }

        var largestClusterRoot = clusterCounts.MaxBy(kv => kv.Value).Key;

        // 最大クラスタに属するチャンクを抽出
        var result = new List<TextChunk>();
        for (int i = 0; i < n; i++)
        {
            if (Find(parent, i) == largestClusterRoot)
            {
                result.Add(chunks[i]);
            }
            else
            {
                _logger.LogDebug(
                    "近接判定で除外（別クラスタ）: ChunkId={ChunkId}",
                    chunks[i].ChunkId);
            }
        }

        // Y座標→X座標でソート
        return [.. result
            .OrderBy(c => c.CombinedBounds.Y)
            .ThenBy(c => c.CombinedBounds.X)];
    }

    /// <summary>
    /// Union-Find: Find操作（経路圧縮付き）
    /// </summary>
    private static int Find(int[] parent, int i)
    {
        if (parent[i] != i)
            parent[i] = Find(parent, parent[i]);
        return parent[i];
    }

    /// <summary>
    /// Union-Find: Union操作（ランク付き）
    /// </summary>
    private static void Union(int[] parent, int[] rank, int x, int y)
    {
        var rootX = Find(parent, x);
        var rootY = Find(parent, y);

        if (rootX == rootY)
            return;

        if (rank[rootX] < rank[rootY])
            parent[rootX] = rootY;
        else if (rank[rootX] > rank[rootY])
            parent[rootY] = rootX;
        else
        {
            parent[rootY] = rootX;
            rank[rootX]++;
        }
    }

    /// <summary>
    /// 2つの矩形間の最短距離を計算
    /// </summary>
    private static int CalculateDistance(System.Drawing.Rectangle a, System.Drawing.Rectangle b)
    {
        // 水平方向の距離
        int dx = 0;
        if (a.Right < b.Left)
            dx = b.Left - a.Right;
        else if (b.Right < a.Left)
            dx = a.Left - b.Right;

        // 垂直方向の距離
        int dy = 0;
        if (a.Bottom < b.Top)
            dy = b.Top - a.Bottom;
        else if (b.Bottom < a.Top)
            dy = a.Top - b.Bottom;

        // double にキャストしてから乗算（整数オーバーフロー回避）
        return (int)Math.Sqrt((double)dx * dx + (double)dy * dy);
    }

    /// <summary>
    /// 文字種
    /// </summary>
    private enum CharacterType
    {
        Hiragana,
        Katakana,
        Kanji,
        Alphabet,
        Digit,
        Other
    }
}
