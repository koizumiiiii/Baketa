using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;
using Baketa.UI.ViewModels;

namespace Baketa.UI.Views;

public partial class WindowSelectionDialogView : Window
{
    public WindowSelectionDialogView()
    {
        Console.WriteLine("🎯 XAML WindowSelectionDialogView コンストラクタ呼び出し");
        InitializeComponent();
        Console.WriteLine("🎯 XAML WindowSelectionDialogView InitializeComponent完了");
        
        // ViewModelの変更を監視してダイアログを閉じる
        DataContextChanged += OnDataContextChanged;
        
        // ClosedイベントでViewModelに通知
        Closed += OnWindowClosed;
        Console.WriteLine("🎯 XAML WindowSelectionDialogView 初期化完了");
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        Console.WriteLine("🎯 XAML WindowSelectionDialogView OnDataContextChanged開始");
        if (DataContext is WindowSelectionDialogViewModel viewModel)
        {
            Console.WriteLine("🎯 XAML WindowSelectionDialogView DataContext設定完了 - ViewModelバインド");
            
            // IsClosed プロパティの変更を監視
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(WindowSelectionDialogViewModel.IsClosed) && viewModel.IsClosed)
                {
                    Close(viewModel.DialogResult);
                }
                
                // 🎯 UltraThink修正: SelectedWindow変更監視（複雑バインディングの代替）
                if (e.PropertyName == nameof(WindowSelectionDialogViewModel.SelectedWindow))
                {
                    UpdateSelectionIndicators(viewModel);
                }
                
                // 🎯 UltraThink修正: IsLoading変更監視（空状態表示制御）
                if (e.PropertyName == nameof(WindowSelectionDialogViewModel.IsLoading))
                {
                    UpdateEmptyState(viewModel);
                }
            };
            
            // 🎯 UltraThink修正: AvailableWindowsコレクション変更監視
            viewModel.AvailableWindows.CollectionChanged += (s, e) =>
            {
                Console.WriteLine($"🎯 XAML AvailableWindows変更: Count={viewModel.AvailableWindows.Count}");
                UpdateEmptyState(viewModel);
                UpdateSelectionIndicators(viewModel);
            };
            
            // 初期状態設定
            UpdateEmptyState(viewModel);
            Console.WriteLine("🎯 XAML WindowSelectionDialogView ViewModelバインド完了");
        }
        else
        {
            Console.WriteLine("🎯 XAML WindowSelectionDialogView DataContextがWindowSelectionDialogViewModelではありません");
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        Console.WriteLine("🎯 XAML WindowSelectionDialogView OnLoaded開始");
        base.OnLoaded(e);
        
        // ダイアログの位置を画面中央に設定
        if (VisualRoot is Window)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        Console.WriteLine("🎯 XAML WindowSelectionDialogView OnLoaded完了");
    }

    /// <summary>
    /// ウィンドウアイテムのクリックイベント処理
    /// </summary>
    private void OnWindowItemClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is WindowInfo windowInfo)
        {
            if (DataContext is WindowSelectionDialogViewModel viewModel)
            {
                // クリック数に応じて処理を分ける
                if (e.ClickCount == 1)
                {
                    // シングルクリック: 選択状態を設定
                    viewModel.SelectedWindow = windowInfo;
                }
                else if (e.ClickCount >= 2)
                {
                    // ダブルクリック: 選択して即座に決定
                    viewModel.SelectedWindow = windowInfo;
                    
                    // ダブルクリックで即座に選択を実行
                    viewModel.SelectWindowCommand.Execute(windowInfo);
                }
            }
        }
    }
    
    /// <summary>
    /// ウィンドウが閉じられた時のViewModel清理
    /// </summary>
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (DataContext is WindowSelectionDialogViewModel viewModel && !viewModel.IsClosed)
        {
            // ウィンドウが強制的に閉じられた場合はキャンセルとして扱う
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                viewModel.CancelCommand.Execute(System.Reactive.Unit.Default);
            });
        }
    }
    
    /// <summary>
    /// 🎯 UltraThink修正: 選択状態インジケーター更新（複雑バインディング代替）
    /// </summary>
    private void UpdateSelectionIndicators(WindowSelectionDialogViewModel viewModel)
    {
        try
        {
            // ItemsControlの各アイテムを検索してSelectionIndicatorを更新
            var itemsControl = this.FindControl<ItemsControl>("AvailableWindowsList");
            if (itemsControl != null)
            {
                // Visual Tree内のすべてのEllipse（SelectionIndicator）を検索
                var indicators = itemsControl.GetVisualDescendants()
                    .OfType<Ellipse>()
                    .Where(e => e.Name == "SelectionIndicator");
                
                foreach (var indicator in indicators)
                {
                    // 親のDataContextから対象ウィンドウを取得
                    var parentBorder = indicator.GetLogicalAncestors().OfType<Border>().FirstOrDefault();
                    if (parentBorder?.DataContext is WindowInfo windowInfo)
                    {
                        indicator.IsVisible = viewModel.SelectedWindow?.Handle == windowInfo.Handle;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // ログ出力またはデバッグ用（UI処理でエラーが発生してもクラッシュしない）
            System.Diagnostics.Debug.WriteLine($"選択状態インジケーター更新エラー: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 🎯 UltraThink修正: 空状態表示制御（複雑バインディング代替）
    /// </summary>
    private void UpdateEmptyState(WindowSelectionDialogViewModel viewModel)
    {
        try
        {
            var emptyStatePanel = this.FindControl<StackPanel>("EmptyStatePanel");
            var itemsControl = this.FindControl<ItemsControl>("AvailableWindowsList");
            
            if (emptyStatePanel != null && itemsControl != null)
            {
                bool isEmpty = viewModel.AvailableWindows.Count == 0 && !viewModel.IsLoading;
                emptyStatePanel.IsVisible = isEmpty;
                itemsControl.IsVisible = !viewModel.IsLoading && !isEmpty;
            }
        }
        catch (Exception ex)
        {
            // ログ出力またはデバッグ用（UI処理でエラーが発生してもクラッシュしない）
            System.Diagnostics.Debug.WriteLine($"空状態表示制御エラー: {ex.Message}");
        }
    }
}