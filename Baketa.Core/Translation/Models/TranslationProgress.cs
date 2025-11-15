namespace Baketa.Core.Translation.Models;

/// <summary>
/// 翻訳進行状況
/// </summary>
public class TranslationProgress
{
    /// <summary>
    /// 総チャンク数
    /// </summary>
    public int TotalChunks { get; set; }

    /// <summary>
    /// 完了したチャンク数
    /// </summary>
    public int CompletedChunks { get; set; }

    /// <summary>
    /// 進行率（0-100）
    /// </summary>
    public int ProgressPercentage => TotalChunks > 0 ? (CompletedChunks * 100) / TotalChunks : 0;

    /// <summary>
    /// 現在処理中のチャンクインデックス
    /// </summary>
    public int CurrentChunkIndex { get; set; }

    /// <summary>
    /// 推定残り時間（ミリ秒）
    /// </summary>
    public long EstimatedRemainingMs { get; set; }
}
