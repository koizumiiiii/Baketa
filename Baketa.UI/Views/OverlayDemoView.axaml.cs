using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Baketa.UI.Controls;

namespace Baketa.UI.Views;

public partial class OverlayDemoView : UserControl
{
    public OverlayDemoView()
    {
        InitializeComponent();
        SetupEventHandlers();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void SetupEventHandlers()
    {
        // トグルボタンのイベントハンドラー
        var toggleButton = this.FindControl<Button>("ToggleButton");
        if (toggleButton != null)
        {
            toggleButton.Click += OnToggleButtonClick;
        }

        // テーマ選択のイベントハンドラー
        var themeComboBox = this.FindControl<ComboBox>("ThemeComboBox");
        if (themeComboBox != null)
        {
            themeComboBox.SelectedIndex = 0; // Auto
            themeComboBox.SelectionChanged += OnThemeSelectionChanged;
        }

        // アニメーション有効/無効のイベントハンドラー
        var animationCheckBox = this.FindControl<CheckBox>("AnimationCheckBox");
        if (animationCheckBox != null)
        {
            animationCheckBox.IsCheckedChanged += OnAnimationEnabledChanged;
        }
    }

    private void OnToggleButtonClick(object? sender, RoutedEventArgs e)
    {
        // すべてのオーバーレイの表示/非表示をトグル
        var overlay1 = this.FindControl<OverlayTextBlock>("SampleOverlay1");
        var overlay2 = this.FindControl<OverlayTextBlock>("SampleOverlay2");
        var overlay3 = this.FindControl<OverlayTextBlock>("SampleOverlay3");

        overlay1?.ToggleVisibility();
        overlay2?.ToggleVisibility();
        overlay3?.ToggleVisibility();
    }

    private void OnThemeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            var themeName = selectedItem.Content?.ToString();
            if (Enum.TryParse<OverlayTheme>(themeName, out var theme))
            {
                // すべてのオーバーレイのテーマを変更
                var overlay1 = this.FindControl<OverlayTextBlock>("SampleOverlay1");
                var overlay2 = this.FindControl<OverlayTextBlock>("SampleOverlay2");
                var overlay3 = this.FindControl<OverlayTextBlock>("SampleOverlay3");

                if (overlay1 != null) overlay1.Theme = theme;
                if (overlay2 != null) overlay2.Theme = theme;
                if (overlay3 != null) overlay3.Theme = theme;
            }
        }
    }

    private void OnAnimationEnabledChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
        {
            var isEnabled = checkBox.IsChecked ?? false;

            // すべてのオーバーレイのアニメーション設定を変更
            var overlay1 = this.FindControl<OverlayTextBlock>("SampleOverlay1");
            var overlay2 = this.FindControl<OverlayTextBlock>("SampleOverlay2");
            var overlay3 = this.FindControl<OverlayTextBlock>("SampleOverlay3");

            if (overlay1 != null) overlay1.AnimationEnabled = isEnabled;
            if (overlay2 != null) overlay2.AnimationEnabled = isEnabled;
            if (overlay3 != null) overlay3.AnimationEnabled = isEnabled;
        }
    }
}
