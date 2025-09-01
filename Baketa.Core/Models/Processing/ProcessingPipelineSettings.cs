using Baketa.Core.Models.Processing;

namespace Baketa.Core.Models.Processing;

/// <summary>
/// 段階的処理パイプラインの設定クラス
/// Clean Architecture準拠: Core層の設定定義
/// </summary>
public class ProcessingPipelineSettings
{
    /// <summary>
    /// 段階的処理を有効にするか
    /// </summary>
    public bool EnableStaging { get; set; } = true;

    /// <summary>
    /// 早期終了を有効にするか（変化なし時の処理停止）
    /// </summary>
    public bool EnableEarlyTermination { get; set; } = true;

    /// <summary>
    /// 最初のエラーで処理を停止するか
    /// </summary>
    public bool StopOnFirstError { get; set; } = true;

    /// <summary>
    /// テキスト変化検知のしきい値（0.0-1.0）
    /// </summary>
    public float TextChangeThreshold { get; set; } = 0.1f;

    /// <summary>
    /// パフォーマンスメトリクスを収集するか
    /// </summary>
    public bool EnablePerformanceMetrics { get; set; } = true;

    /// <summary>
    /// カスタム段階実行順序（null時はデフォルト順序を使用）
    /// </summary>
    public List<ProcessingStageType>? CustomStageOrder { get; set; }
}