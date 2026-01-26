using Baketa.Core.Abstractions.Events;

namespace Baketa.Core.Events;

/// <summary>
/// [Issue #324] テキスト送り検知イベント
/// ROI監視によりテキスト変化（テキスト送り）を検知した際に発行
/// </summary>
/// <remarks>
/// 学習済みROI領域のハッシュ比較により、テキストボックス内の
/// 内容が変化したことを検知した際にトリガーされます。
/// このイベントを受けて即時キャプチャ・翻訳を実行できます。
/// </remarks>
public sealed class TextAdvanceDetectedEvent : IEvent
{
    /// <inheritdoc />
    public Guid Id { get; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTime Timestamp { get; } = DateTime.UtcNow;

    /// <inheritdoc />
    public string Name => "TextAdvanceDetected";

    /// <inheritdoc />
    public string Category => "Translation";

    /// <summary>
    /// 検知時刻
    /// </summary>
    public required DateTime DetectedAt { get; init; }

    /// <summary>
    /// 変化が検知されたROI領域数
    /// </summary>
    public required int ChangedRegionCount { get; init; }

    /// <summary>
    /// 対象ウィンドウハンドル
    /// </summary>
    public required IntPtr WindowHandle { get; init; }

    /// <summary>
    /// テキスト送りの推定信頼度（0.0-1.0）
    /// </summary>
    /// <remarks>
    /// 複数の高信頼度ROI領域が同時に変化した場合、
    /// テキスト送りの可能性が高いと判断して高い値となります。
    /// </remarks>
    public float ConfidenceLevel { get; init; } = 1.0f;

    /// <summary>
    /// 即時キャプチャ・翻訳を推奨するかどうか
    /// </summary>
    public bool ShouldTriggerImmediateTranslation { get; init; } = true;
}
