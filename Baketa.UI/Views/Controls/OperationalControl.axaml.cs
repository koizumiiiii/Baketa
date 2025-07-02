using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Baketa.UI.ViewModels.Controls;

namespace Baketa.UI.Views.Controls;

/// <summary>
/// 操作UI（自動/単発翻訳ボタン）のビュー
/// </summary>
public partial class OperationalControl : UserControl
{
    /// <summary>
    /// コンストラクタ
    /// </summary>
    public OperationalControl()
    {
        InitializeComponent();
        
        // ロード時のアニメーション効果
        this.AttachedToVisualTree += OnAttachedToVisualTree;
        
        // フォーカス可能にする
        this.Focusable = true;
        
        // アクセシビリティ設定
        SetupAccessibility();
    }

    /// <summary>
    /// ビジュアルツリーにアタッチされた時の処理
    /// </summary>
    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // ロードアニメーション用のクラスを追加
        Classes.Add("loaded");
    }

    /// <summary>
    /// 自動翻訳トグルスイッチのツールチップを動的に更新
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is OperationalControlViewModel viewModel)
        {
            // ViewModelのプロパティ変更に応じてUI要素を更新
            UpdateToggleSwitchToolTip(viewModel);
            UpdateButtonToolTip(viewModel);
        }
    }

    /// <summary>
    /// トグルスイッチのツールチップを更新
    /// </summary>
    private void UpdateToggleSwitchToolTip(OperationalControlViewModel viewModel)
    {
        if (this.FindControl<ToggleSwitch>("AutomaticModeToggle") is ToggleSwitch toggle)
        {
            var toolTip = viewModel.IsAutomaticMode
                ? "自動翻訳を停止する（手動モードに切り替え）"
                : "自動翻訳を開始する（連続翻訳モード）";
            
            ToolTip.SetTip(toggle, toolTip);
        }
    }

    /// <summary>
    /// 単発翻訳ボタンのツールチップを更新
    /// </summary>
    private void UpdateButtonToolTip(OperationalControlViewModel viewModel)
    {
        if (this.FindControl<Button>("SingleTranslationButton") is Button button)
        {
            var toolTip = viewModel.IsTranslating
                ? "翻訳処理中です..."
                : "現在の画面を一度だけ翻訳します";
            
            ToolTip.SetTip(button, toolTip);
        }
    }

    /// <summary>
    /// コントロールがフォーカスを受け取った時の処理
    /// </summary>
    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        
        // フォーカス時に視覚的フィードバックを提供
        Classes.Add("focused");
    }

    /// <summary>
    /// コントロールがフォーカスを失った時の処理
    /// </summary>
    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        
        // フォーカス喪失時に視覚的フィードバックを削除
        Classes.Remove("focused");
    }

    /// <summary>
    /// マウス進入時の処理
    /// </summary>
    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        
        // ホバー状態のクラスを追加
        Classes.Add("hovered");
    }

    /// <summary>
    /// マウス退出時の処理
    /// </summary>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        
        // ホバー状態のクラスを削除
        Classes.Remove("hovered");
    }

    /// <summary>
    /// コンパクトモードの切り替え
    /// </summary>
    /// <param name="isCompact">コンパクトモードにするかどうか</param>
    public void SetCompactMode(bool isCompact)
    {
        if (isCompact)
        {
            Classes.Add("compact");
        }
        else
        {
            Classes.Remove("compact");
        }
    }

    /// <summary>
    /// アクセシビリティのサポート強化
    /// </summary>
    private void SetupAccessibility()
    {
        // アクセシビリティ属性の設定
        // TODO: Avaloniaのアクセシビリティサポートの実装が完了したら有効化
        // SetValue(AutomationProperties.NameProperty, "翻訳操作コントロール");
        // SetValue(AutomationProperties.HelpTextProperty, 
        //     "自動翻訳のオン・オフ切り替えと単発翻訳の実行を行います。");
        // SetValue(AutomationProperties.AccessKeyProperty, "T");
    }
}
