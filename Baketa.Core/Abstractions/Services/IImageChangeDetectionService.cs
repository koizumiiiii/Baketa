using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Models.ImageProcessing;

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// 画像変化検知サービスインターフェース
/// P0: 3段階フィルタリング対応（Stage 1: 90% → Stage 2: 8% → Stage 3: 2%）
/// OpenCV SIMD最適化による高速処理 (<1ms for Stage 1)
/// </summary>
public interface IImageChangeDetectionService
{
    /// <summary>
    /// 画像変化を検知（3段階フィルタリング実行）
    /// Stage 1: 高速フィルタ (90%除外) → Stage 2: 中精度 (8%処理) → Stage 3: 高精度 (2%処理)
    /// </summary>
    /// <param name="previousImage">前回画像</param>
    /// <param name="currentImage">現在画像</param>
    /// <param name="contextId">コンテキストID（キャッシュキー）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>詳細な変化検知結果</returns>
    Task<ImageChangeResult> DetectChangeAsync(
        IImage? previousImage,
        IImage currentImage,
        string contextId = "default",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 高速フィルタ（Stage 1）のみ実行
    /// 90%のフレームを<1msで除外するための軽量検査
    /// </summary>
    /// <param name="previousImage">前回画像</param>
    /// <param name="currentImage">現在画像</param>
    /// <param name="contextId">コンテキストID</param>
    /// <returns>高速フィルタ結果</returns>
    Task<QuickFilterResult> QuickFilterAsync(
        IImage? previousImage,
        IImage currentImage,
        string contextId = "default");

    /// <summary>
    /// 画像タイプを自動判定
    /// ゲームUI、ゲーム内シーン、一般アプリケーション等を判定し最適アルゴリズム選択
    /// </summary>
    /// <param name="image">対象画像</param>
    /// <returns>判定された画像タイプ</returns>
    Task<ImageType> DetectImageTypeAsync(IImage image);

    /// <summary>
    /// ROI（関心領域）ベース変化検知
    /// 複数領域での独立した変化検知を実行
    /// </summary>
    /// <param name="previousImage">前回画像</param>
    /// <param name="currentImage">現在画像</param>
    /// <param name="regions">検知対象領域配列</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>領域別変化検知結果</returns>
    Task<RegionChangeResult[]> DetectRegionChangesAsync(
        IImage? previousImage,
        IImage currentImage,
        Rectangle[] regions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 画像キャッシュをクリア
    /// メモリ管理とコンテキスト切り替え時に使用
    /// </summary>
    /// <param name="contextId">クリア対象コンテキストID（null=全てクリア）</param>
    void ClearCache(string? contextId = null);

    /// <summary>
    /// 統計情報を取得
    /// パフォーマンス監視とデバッグ用
    /// </summary>
    /// <returns>検知統計情報</returns>
    ImageChangeDetectionStatistics GetStatistics();

    /// <summary>
    /// 後方互換性のための既存メソッド（レガシー）
    /// 新規実装では DetectChangeAsync(IImage, IImage) を使用推奨
    /// </summary>
    /// <param name="previousImage">前回の画像データ</param>
    /// <param name="currentImage">現在の画像データ</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>変化検知の結果</returns>
    [Obsolete("Use DetectChangeAsync(IImage, IImage, string, CancellationToken) instead")]
    Task<ImageChangeResult> DetectChangeAsync(
        byte[] previousImage,
        byte[] currentImage,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 画像変化検知統計情報
/// パフォーマンス監視とデバッグ用
/// </summary>
public record ImageChangeDetectionStatistics
{
    /// <summary>総処理回数</summary>
    public long TotalProcessed { get; init; }

    /// <summary>Stage 1で除外された回数</summary>
    public long Stage1Filtered { get; init; }

    /// <summary>Stage 2で除外された回数</summary>
    public long Stage2Filtered { get; init; }

    /// <summary>Stage 3まで進んだ回数</summary>
    public long Stage3Processed { get; init; }

    /// <summary>平均処理時間（Stage 1）</summary>
    public TimeSpan AverageStage1Time { get; init; }

    /// <summary>平均処理時間（Stage 2）</summary>
    public TimeSpan AverageStage2Time { get; init; }

    /// <summary>平均処理時間（Stage 3）</summary>
    public TimeSpan AverageStage3Time { get; init; }

    /// <summary>キャッシュヒット率</summary>
    public float CacheHitRate { get; init; }

    /// <summary>現在のキャッシュサイズ</summary>
    public int CurrentCacheSize { get; init; }

    /// <summary>フィルタリング効率（Stage 1での除外率）</summary>
    public float FilteringEfficiency { get; init; }
}

