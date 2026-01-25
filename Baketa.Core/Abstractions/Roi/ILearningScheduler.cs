using Baketa.Core.Models.Roi;

namespace Baketa.Core.Abstractions.Roi;

/// <summary>
/// 学習スケジューラインターフェース
/// </summary>
/// <remarks>
/// Issue #293 Phase 10: 学習駆動型投機的OCR
/// ROI学習の進捗を監視し、投機的OCRの実行頻度を動的に調整します。
/// </remarks>
public interface ILearningScheduler
{
    /// <summary>
    /// 現在の学習進捗
    /// </summary>
    LearningProgress CurrentProgress { get; }

    /// <summary>
    /// 現在の学習フェーズ
    /// </summary>
    LearningPhase CurrentPhase { get; }

    /// <summary>
    /// 学習が完了しているか（維持モードに移行済み）
    /// </summary>
    bool IsLearningComplete { get; }

    /// <summary>
    /// 次の実行までの間隔を取得
    /// </summary>
    /// <returns>学習フェーズに応じた実行間隔</returns>
    TimeSpan GetNextExecutionInterval();

    /// <summary>
    /// 今すぐ実行すべきかを判定
    /// </summary>
    /// <returns>実行すべき場合はtrue</returns>
    bool ShouldExecuteNow();

    /// <summary>
    /// OCR完了時に呼び出し、学習進捗を更新
    /// </summary>
    /// <param name="detectionCount">検出されたテキスト領域数</param>
    /// <param name="highConfidenceCount">高信頼度の検出数</param>
    void OnOcrCompleted(int detectionCount, int highConfidenceCount = 0);

    /// <summary>
    /// プロファイル切り替え時に学習状態をリセット
    /// </summary>
    void ResetForNewProfile();

    /// <summary>
    /// 学習進捗を外部データから復元
    /// </summary>
    /// <param name="progress">復元する進捗データ</param>
    void RestoreProgress(LearningProgress progress);
}
