using Baketa.Core.Settings;
using Baketa.UI.Resources;
using ReactiveUI;

namespace Baketa.UI.Models.Settings;

/// <summary>
/// 設定カテゴリの定義
/// プログレッシブディスクロージャーによる階層表示をサポート
/// ReactiveObjectを継承してContentの変更通知をサポート
/// </summary>
public sealed class SettingCategory : ReactiveObject
{
    private object? _content;
    private string _name = string.Empty;
    private string _description = string.Empty;

    /// <summary>
    /// カテゴリの一意識別子
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// カテゴリ名のリソースキー（動的言語切り替え対応）
    /// </summary>
    public string NameResourceKey { get; set; } = string.Empty;

    /// <summary>
    /// カテゴリ説明のリソースキー（動的言語切り替え対応）
    /// </summary>
    public string DescriptionResourceKey { get; set; } = string.Empty;

    /// <summary>
    /// カテゴリの表示名（リソースキーから動的に取得）
    /// </summary>
    public string Name
    {
        get => !string.IsNullOrEmpty(NameResourceKey)
            ? Strings.ResourceManager.GetString(NameResourceKey, Strings.Culture) ?? _name
            : _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

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
    /// PropertyChangedを発火してバインディングを更新
    /// </summary>
    public object? Content
    {
        get => _content;
        set => this.RaiseAndSetIfChanged(ref _content, value);
    }

    /// <summary>
    /// 表示順序（小さい値ほど上に表示）
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// カテゴリの説明文（リソースキーから動的に取得）
    /// </summary>
    public string Description
    {
        get => !string.IsNullOrEmpty(DescriptionResourceKey)
            ? Strings.ResourceManager.GetString(DescriptionResourceKey, Strings.Culture) ?? _description
            : _description;
        set => this.RaiseAndSetIfChanged(ref _description, value);
    }

    /// <summary>
    /// カテゴリが有効かどうか
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 言語変更時にNameとDescriptionのPropertyChangedを発火する
    /// </summary>
    public void RefreshLocalizedStrings()
    {
        this.RaisePropertyChanged(nameof(Name));
        this.RaisePropertyChanged(nameof(Description));
    }
}
