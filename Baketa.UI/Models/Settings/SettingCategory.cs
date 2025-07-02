using Baketa.Core.Settings;

namespace Baketa.UI.Models.Settings;

/// <summary>
/// 設定カテゴリの定義
/// プログレッシブディスクロージャーによる階層表示をサポート
/// </summary>
public sealed class SettingCategory
{
    /// <summary>
    /// カテゴリの一意識別子
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// カテゴリの表示名
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// カテゴリアイコンのパスデータ（Material Design Icons）
    /// </summary>
    public string IconData { get; set; } = string.Empty;

    /// <summary>
    /// 設定レベル（Basic/Advanced/Debug）
    /// </summary>
    public SettingLevel Level { get; set; } = SettingLevel.Basic;

    /// <summary>
    /// カテゴリのコンテンツ（View/ViewModel）
    /// </summary>
    public object? Content { get; set; }

    /// <summary>
    /// 表示順序（小さい値ほど上に表示）
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// カテゴリの説明文
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// カテゴリが有効かどうか
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
