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
