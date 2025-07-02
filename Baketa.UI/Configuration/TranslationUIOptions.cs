using System.ComponentModel.DataAnnotations;

namespace Baketa.UI.Configuration;

/// <summary>
/// 翻訳UI設定オプション
/// </summary>
public class TranslationUIOptions
{
    /// <summary>
    /// 設定セクション名
    /// </summary>
    public const string SectionName = "TranslationUI";

    /// <summary>
    /// 通知機能の有効/無効
    /// </summary>
    public bool EnableNotifications { get; set; } = true;

    /// <summary>
    /// エンジン状態更新間隔（秒）
    /// </summary>
    [Range(5, 300)]
    public int StatusUpdateIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// 設定の自動保存有効/無効
    /// </summary>
    public bool AutoSaveSettings { get; set; } = true;

    /// <summary>
    /// 詳細ログの有効/無効
    /// </summary>
    public bool EnableVerboseLogging { get; set; }

    /// <summary>
    /// フォールバック情報のユーザー表示有効/無効
    /// </summary>
    public bool ShowFallbackInformation { get; set; } = true;

    /// <summary>
    /// エンジン状態変更時のアニメーション有効/無効
    /// </summary>
    public bool EnableStatusAnimations { get; set; } = true;

    /// <summary>
    /// 中国語変種の自動検出有効/無効
    /// </summary>
    public bool EnableChineseVariantAutoDetection { get; set; } = true;

    /// <summary>
    /// 初期表示時のエンジン設定（LocalOnly, CloudOnly）
    /// </summary>
    public string DefaultEngineStrategy { get; set; } = "LocalOnly";

    /// <summary>
    /// 初期表示時の言語ペア
    /// </summary>
    public string DefaultLanguagePair { get; set; } = "ja-en";

    /// <summary>
    /// 初期表示時の中国語変種（Simplified, Traditional）
    /// </summary>
    public string DefaultChineseVariant { get; set; } = "Simplified";

    /// <summary>
    /// 初期表示時の翻訳戦略（Direct, TwoStage）
    /// </summary>
    public string DefaultTranslationStrategy { get; set; } = "Direct";
}
