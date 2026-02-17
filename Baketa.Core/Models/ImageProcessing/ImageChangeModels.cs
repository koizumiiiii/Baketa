using System.Drawing;

namespace Baketa.Core.Models.ImageProcessing;

/// <summary>
/// 画像変化検知結果
/// P0: 画像変化検知システム - Record型によるイミュータブル設計
/// Geminiフィードバック反映: Factory methods, 詳細メトリクス
/// </summary>
public record ImageChangeResult
{
    /// <summary>変化が検知されたかどうか</summary>
    public bool HasChanged { get; init; }

    /// <summary>変化率（0.0-1.0）</summary>
    public float ChangePercentage { get; init; }

    /// <summary>変化が検知された領域</summary>
    public Rectangle[] ChangedRegions { get; init; } = Array.Empty<Rectangle>();

    /// <summary>処理時間</summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>使用されたハッシュアルゴリズム</summary>
    public HashAlgorithmType AlgorithmUsed { get; init; }

    /// <summary>前回のハッシュ値</summary>
    public string PreviousHash { get; init; } = string.Empty;

    /// <summary>現在のハッシュ値</summary>
    public string CurrentHash { get; init; } = string.Empty;

    /// <summary>検知段階（1:高速, 2:中精度, 3:高精度）</summary>
    public int DetectionStage { get; init; }

    /// <summary>SSIM値（構造的類似性指数、Stage 3で使用）</summary>
    public float? SSIMScore { get; init; }

    /// <summary>追加のメトリクス情報</summary>
    public Dictionary<string, object> AdditionalMetrics { get; init; } = new();

    // Factory Methods

    /// <summary>
    /// 初回検知時の結果を作成
    /// </summary>
    public static ImageChangeResult CreateFirstTime(string currentHash, HashAlgorithmType algorithm, TimeSpan processingTime) =>
        new()
        {
            HasChanged = true,
            ChangePercentage = 1.0f,
            CurrentHash = currentHash,
            AlgorithmUsed = algorithm,
            ProcessingTime = processingTime,
            DetectionStage = 1,
            AdditionalMetrics = new Dictionary<string, object> { ["IsFirstTime"] = true }
        };

    /// <summary>
    /// 変化検知時の結果を作成
    /// </summary>
    public static ImageChangeResult CreateChanged(
        string prevHash,
        string currHash,
        float changePercentage,
        HashAlgorithmType algorithm,
        TimeSpan processingTime,
        int detectionStage = 1,
        Rectangle[]? regions = null,
        float? ssimScore = null) =>
        new()
        {
            HasChanged = true,
            ChangePercentage = changePercentage,
            PreviousHash = prevHash,
            CurrentHash = currHash,
            AlgorithmUsed = algorithm,
            ProcessingTime = processingTime,
            DetectionStage = detectionStage,
            ChangedRegions = regions ?? Array.Empty<Rectangle>(),
            SSIMScore = ssimScore
        };

    /// <summary>
    /// 変化なし時の結果を作成
    /// </summary>
    public static ImageChangeResult CreateNoChange(TimeSpan processingTime, int detectionStage = 1) =>
        new()
        {
            HasChanged = false,
            ChangePercentage = 0.0f,
            ProcessingTime = processingTime,
            DetectionStage = detectionStage
        };
}

/// <summary>
/// 高速フィルタ結果
/// Stage 1で90%のフレームを除外するための軽量結果
/// </summary>
public record QuickFilterResult
{
    /// <summary>潜在的変化があるかどうか</summary>
    public bool HasPotentialChange { get; init; }

    /// <summary>AverageHash値</summary>
    public string AverageHash { get; init; } = string.Empty;

    /// <summary>DifferenceHash値</summary>
    public string DifferenceHash { get; init; } = string.Empty;

    /// <summary>処理時間</summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>最高類似度スコア</summary>
    public float MaxSimilarity { get; init; }

    // Factory Methods

    /// <summary>変化なしの結果</summary>
    public static QuickFilterResult NoChange => new() { HasPotentialChange = false };

    /// <summary>潜在的変化ありの結果</summary>
    public static QuickFilterResult PotentialChange => new() { HasPotentialChange = true };
}

/// <summary>
/// 領域別変化検知結果
/// ROI（関心領域）ベース分析用
/// </summary>
public record RegionChangeResult
{
    /// <summary>対象領域</summary>
    public Rectangle Region { get; init; }

    /// <summary>この領域で変化が検知されたか</summary>
    public bool HasChanged { get; init; }

    /// <summary>SSIM類似度スコア</summary>
    public float SimilarityScore { get; init; }

    /// <summary>領域タイプ（テキスト、UI要素等）</summary>
    public RegionType RegionType { get; init; } = RegionType.Text;

    public RegionChangeResult(Rectangle region, bool hasChanged, float similarityScore)
    {
        Region = region;
        HasChanged = hasChanged;
        SimilarityScore = similarityScore;
    }
}

/// <summary>
/// Perceptual Hash アルゴリズム種別
/// Geminiフィードバック: 段階的フィルタリング対応
/// </summary>
public enum HashAlgorithmType
{
    /// <summary>平均ハッシュ - 高速、基本的な変化検知（Stage 1）</summary>
    AverageHash,

    /// <summary>差分ハッシュ - エッジ変化に敏感（Stage 1推奨）</summary>
    DifferenceHash,

    /// <summary>知覚ハッシュ - 高精度、処理コスト中（Stage 2）</summary>
    PerceptualHash,

    /// <summary>ウェーブレットハッシュ - 周波数ベース、ゲーム向け（Stage 2-3）</summary>
    WaveletHash
}

/// <summary>
/// 画像タイプ
/// 最適アルゴリズム選択に使用
/// </summary>
public enum ImageType
{
    /// <summary>ゲームUIスクリーンショット</summary>
    GameUI,

    /// <summary>ゲーム内シーン</summary>
    GameScene,

    /// <summary>一般アプリケーション</summary>
    Application,

    /// <summary>UI要素</summary>
    UIElement,

    /// <summary>不明</summary>
    Unknown
}

/// <summary>
/// 領域タイプ
/// ROI分析用
/// </summary>
public enum RegionType
{
    /// <summary>テキスト領域</summary>
    Text,

    /// <summary>UI要素</summary>
    UIElement,

    /// <summary>背景</summary>
    Background,

    /// <summary>エフェクト領域</summary>
    Effect
}


/// <summary>
/// キャッシュされたハッシュ情報
/// Thread-safe実装用
/// </summary>
public record CachedImageHash
{
    /// <summary>ハッシュ値</summary>
    public string Hash { get; init; } = string.Empty;

    /// <summary>タイムスタンプ</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>使用されたアルゴリズム</summary>
    public HashAlgorithmType Algorithm { get; init; }

    public CachedImageHash(string hash, DateTime timestamp, HashAlgorithmType algorithm = HashAlgorithmType.DifferenceHash)
    {
        Hash = hash;
        Timestamp = timestamp;
        Algorithm = algorithm;
    }
}

/// <summary>
/// 高速キャッシュ（Stage 1用）
/// 複数ハッシュを保持
/// </summary>
public record QuickHashCache
{
    /// <summary>AverageHash</summary>
    public string AverageHash { get; init; } = string.Empty;

    /// <summary>DifferenceHash</summary>
    public string DifferenceHash { get; init; } = string.Empty;

    /// <summary>タイムスタンプ</summary>
    public DateTime Timestamp { get; init; }

    public QuickHashCache(string averageHash, string differenceHash, DateTime timestamp)
    {
        AverageHash = averageHash;
        DifferenceHash = differenceHash;
        Timestamp = timestamp;
    }
}

/// <summary>
/// [Issue #229] グリッド分割ハッシュキャッシュ
/// 画面を分割した各ブロックのハッシュを保持
/// </summary>
public record GridHashCache
{
    /// <summary>ブロック別ハッシュ配列（行優先順）</summary>
    public string[] BlockHashes { get; init; } = [];

    /// <summary>グリッド行数</summary>
    public int Rows { get; init; }

    /// <summary>グリッド列数</summary>
    public int Columns { get; init; }

    /// <summary>タイムスタンプ</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// [Issue #229] 画像全体のチェックサム
    /// ハッシュが衝突した場合のフォールバック検出用
    /// </summary>
    public long ImageChecksum { get; init; }

    /// <summary>
    /// [Issue #436] ロバストチェックサム（量子化SUM）
    /// GPUノイズ耐性のある変化検知用。画像全体を16バイト間隔でサンプリングし、
    /// 各バイトを3bit量子化(>>5)した合計値。カーソル等の局所ノイズに強い。
    /// </summary>
    public long RobustImageChecksum { get; init; }

    public GridHashCache(string[] blockHashes, int rows, int columns, DateTime timestamp, long imageChecksum = 0, long robustImageChecksum = 0)
    {
        BlockHashes = blockHashes;
        Rows = rows;
        Columns = columns;
        Timestamp = timestamp;
        ImageChecksum = imageChecksum;
        RobustImageChecksum = robustImageChecksum;
    }
}

/// <summary>
/// [Issue #229] 単一ブロックの変化情報
/// </summary>
public readonly record struct BlockChangeInfo(
    int Index,
    int Row,
    int Col,
    float Similarity,
    Rectangle Region
);

/// <summary>
/// [Issue #229] グリッド変化検知の詳細結果
/// Stage 1 の出力として Stage 2 に渡される
/// </summary>
public sealed class GridChangeDetectionResult
{
    /// <summary>処理時間</summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>変化したブロックのリスト</summary>
    public IReadOnlyList<BlockChangeInfo> ChangedBlocks { get; init; } = [];

    /// <summary>グリッド総ブロック数</summary>
    public int TotalBlocks { get; init; }

    /// <summary>グリッド行数</summary>
    public int GridRows { get; init; }

    /// <summary>グリッド列数</summary>
    public int GridColumns { get; init; }

    /// <summary>変化の可能性があるか（1つ以上のブロックが変化）</summary>
    public bool HasPotentialChange => ChangedBlocks.Count > 0;

    /// <summary>最小類似度（最も変化が大きいブロック）</summary>
    public float MinSimilarity { get; init; } = 1.0f;

    /// <summary>最も変化が大きいブロックのインデックス</summary>
    public int MostChangedBlockIndex { get; init; } = -1;
}

/// <summary>
/// [Issue #229] Stage 2 変化検証の結果
/// </summary>
public sealed class ChangeValidationResult
{
    /// <summary>処理時間</summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>有意な変化かどうか（ノイズではない）</summary>
    public bool IsSignificantChange { get; init; }

    /// <summary>フィルタリング理由</summary>
    public string? FilterReason { get; init; }

    /// <summary>変化ブロック数</summary>
    public int ChangedBlockCount { get; init; }

    /// <summary>隣接ブロックが存在するか</summary>
    public bool HasAdjacentBlocks { get; init; }

    /// <summary>端ブロックのみの変化か</summary>
    public bool IsEdgeOnlyChange { get; init; }

    /// <summary>Stage 1 の結果（参照用）</summary>
    public GridChangeDetectionResult? Stage1Result { get; init; }
}

/// <summary>
/// [Issue #229] Stage 3 領域分析の結果
/// </summary>
public sealed class RegionAnalysisResult
{
    /// <summary>処理時間</summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>変化領域の配列</summary>
    public Rectangle[] ChangedRegions { get; init; } = [];

    /// <summary>変化領域の総面積（ピクセル）</summary>
    public int TotalChangedArea { get; init; }

    /// <summary>画面全体に対する変化割合</summary>
    public float ChangePercentage { get; init; }
}
