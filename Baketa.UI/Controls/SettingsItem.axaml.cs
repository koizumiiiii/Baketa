using Avalonia;
using Avalonia.Controls;

namespace Baketa.UI.Controls;

/// <summary>
/// 設定項目表示用の共通コントロール
/// タイトル、説明、警告メッセージ、コンテンツエリアを提供
/// </summary>
public partial class SettingsItem : UserControl
{
    /// <summary>
    /// 設定項目のタイトル
    /// </summary>
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<SettingsItem, string>(nameof(Title), string.Empty);

    /// <summary>
    /// 設定項目の説明文
    /// </summary>
    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<SettingsItem, string>(nameof(Description), string.Empty);

    /// <summary>
    /// 警告メッセージ（設定変更時の注意事項など）
    /// </summary>
    public static readonly StyledProperty<string> WarningMessageProperty =
        AvaloniaProperty.Register<SettingsItem, string>(nameof(WarningMessage), string.Empty);

    /// <summary>
    /// 設定コントロールのコンテンツ（ToggleSwitch、Slider等）
    /// </summary>
    public static readonly StyledProperty<object> SettingContentProperty =
        AvaloniaProperty.Register<SettingsItem, object>(nameof(SettingContent));

    /// <summary>
    /// SettingsItemを初期化します
    /// </summary>
    public SettingsItem()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 設定項目のタイトル
    /// </summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// 設定項目の説明文
    /// </summary>
    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>
    /// 警告メッセージ（設定変更時の注意事項など）
    /// </summary>
    public string WarningMessage
    {
        get => GetValue(WarningMessageProperty);
        set => SetValue(WarningMessageProperty, value);
    }

    /// <summary>
    /// 設定コントロールのコンテンツ（ToggleSwitch、Slider等）
    /// </summary>
    public object SettingContent
    {
        get => GetValue(SettingContentProperty);
        set => SetValue(SettingContentProperty, value);
    }
}
