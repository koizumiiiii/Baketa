using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Abstractions.OCR;

/// <summary>
/// 並列OCR実行設定
/// </summary>
public class ParallelOcrSettings
{
    /// <summary>
    /// 最大並列実行数（デフォルト: 4）
    /// </summary>
    public int MaxParallelism { get; set; } = 4;

    /// <summary>
    /// タイル分割数（横方向）
    /// </summary>
    public int TileColumnsCount { get; set; } = 2;

    /// <summary>
    /// タイル分割数（縦方向）
    /// </summary>
    public int TileRowsCount { get; set; } = 2;

    /// <summary>
    /// タイル間のオーバーラップ（ピクセル）
    /// 境界付近のテキスト検出漏れを防ぐ
    /// </summary>
    public int TileOverlapPixels { get; set; } = 20;

    /// <summary>
    /// 並列OCRを有効にするか
    /// falseの場合は通常の単一OCRを実行
    /// </summary>
    public bool EnableParallelOcr { get; set; } = true;

    /// <summary>
    /// 並列OCRを適用する最小画像サイズ（幅 x 高さ）
    /// 小さな画像では分割によるオーバーヘッドが大きい
    /// </summary>
    public int MinImageSizeForParallel { get; set; } = 640 * 480;
}

/// <summary>
/// [Issue #290] 並列OCR実行サービス
/// 画像をタイルに分割し、並列にOCRを実行することで処理時間を短縮
/// </summary>
public interface IParallelOcrExecutor
{
    /// <summary>
    /// 画像に対して並列OCRを実行
    /// </summary>
    /// <param name="image">処理する画像</param>
    /// <param name="progressCallback">進捗通知コールバック（オプション）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>マージされたOCR結果</returns>
    Task<OcrResults> ExecuteParallelOcrAsync(
        IImage image,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 並列OCR設定を取得
    /// </summary>
    ParallelOcrSettings GetSettings();

    /// <summary>
    /// 並列OCR設定を更新
    /// </summary>
    void UpdateSettings(ParallelOcrSettings settings);
}
