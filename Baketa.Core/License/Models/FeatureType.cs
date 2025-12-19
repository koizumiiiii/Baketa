namespace Baketa.Core.License.Models;

/// <summary>
/// ライセンスによってゲートされる機能の種類
/// </summary>
public enum FeatureType
{
    /// <summary>
    /// ローカル翻訳（全プランで利用可能）
    /// </summary>
    LocalTranslation,

    /// <summary>
    /// クラウドAI翻訳（Pro/Premiaのみ）
    /// </summary>
    CloudAiTranslation,

    /// <summary>
    /// 広告非表示（Standard以上）
    /// </summary>
    AdFree,

    /// <summary>
    /// 優先サポート（Premiaのみ）
    /// </summary>
    PrioritySupport,

    /// <summary>
    /// 高度なOCR設定（Pro以上）
    /// </summary>
    AdvancedOcrSettings,

    /// <summary>
    /// バッチ翻訳（Pro以上）
    /// </summary>
    BatchTranslation
}
