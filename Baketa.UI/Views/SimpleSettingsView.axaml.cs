using Avalonia.Controls;
using Avalonia.Interactivity;
using Baketa.UI.ViewModels;
using System;
using System.Threading.Tasks;

namespace Baketa.UI.Views;

public partial class SimpleSettingsView : Window
{
    public SimpleSettingsView()
    {
        var windowHash = GetHashCode();
        Console.WriteLine($"🔧 [SimpleSettingsView#{windowHash}] コンストラクター開始");
        InitializeComponent();
        
        // ウィンドウの設定
        DataContextChanged += OnDataContextChanged;
        
        // ウィンドウイベントのログ追加（ハッシュコード付き）
        Opened += (s, e) => Console.WriteLine($"🔧 [SimpleSettingsView#{windowHash}] Openedイベント - IsVisible:{IsVisible}");
        Closed += (s, e) => Console.WriteLine($"🔧 [SimpleSettingsView#{windowHash}] Closedイベント - IsVisible:{IsVisible}");
        Closing += (s, e) => Console.WriteLine($"🔧 [SimpleSettingsView#{windowHash}] Closingイベント - IsVisible:{IsVisible}");
        
        Console.WriteLine($"🔧 [SimpleSettingsView#{windowHash}] コンストラクター完了");
    }

    private SimpleSettingsViewModel? _currentViewModel;

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        var windowHash = GetHashCode();
        Console.WriteLine($"🔧 [SimpleSettingsView#{windowHash}] OnDataContextChanged - DataContext: {DataContext?.GetType().Name}");
        
        // 前のViewModelの処理をクリーンアップ
        if (_currentViewModel != null)
        {
            Console.WriteLine($"🔧 [SimpleSettingsView#{windowHash}] 前のViewModelをクリーンアップ: {_currentViewModel.GetHashCode()}");
            _currentViewModel.CloseRequested -= OnCloseRequested;
        }

        if (DataContext is SimpleSettingsViewModel viewModel)
        {
            _currentViewModel = viewModel;
            var vmHash = viewModel.GetHashCode();
            Console.WriteLine($"🔧 [SimpleSettingsView#{windowHash}] 新しいViewModelを設定: {vmHash}");
            
            // ウィンドウ閉じる要求を処理
            viewModel.CloseRequested += OnCloseRequested;
            
            // ViewModelの設定をダブルチェック - MainOverlayViewModelで既に読み込み済みのはず
            Console.WriteLine($"🔧 [SimpleSettingsView#{windowHash}] DataContext設定後の設定確認");
            Console.WriteLine($"🔧 [SimpleSettingsView#{windowHash}] ViewModel設定: UseLocalEngine={viewModel.UseLocalEngine}, SourceLanguage={viewModel.SourceLanguage}, TargetLanguage={viewModel.TargetLanguage}, FontSize={viewModel.FontSize}");
        }
    }

    private void OnCloseRequested()
    {
        var windowHash = GetHashCode();
        Console.WriteLine($"🔧 [SimpleSettingsView#{windowHash}] OnCloseRequested呼び出し - スレッドID: {Environment.CurrentManagedThreadId}, IsVisible: {IsVisible}");
        Console.WriteLine($"🔧 [SimpleSettingsView#{windowHash}] Close()メソッド呼び出し前");
        
        try
        {
            // 1. まずHide()で非表示にする
            Hide();
            Console.WriteLine($"🔧 [SimpleSettingsView#{windowHash}] Hide()完了 - IsVisible: {IsVisible}");
            
            // 2. DataContextを明示的にクリア
            var previousContext = DataContext;
            DataContext = null;
            Console.WriteLine($"🔧 [SimpleSettingsView#{windowHash}] DataContext cleared - 前の値: {previousContext?.GetType().Name}");
            
            // 3. ViewModelのクリーンアップ
            if (_currentViewModel != null)
            {
                _currentViewModel.CloseRequested -= OnCloseRequested;
                _currentViewModel = null;
                Console.WriteLine($"🔧 [SimpleSettingsView#{windowHash}] ViewModel cleanup完了");
            }
            
            // 4. ウィンドウを閉じる
            Close();
            Console.WriteLine($"🔧 [SimpleSettingsView#{windowHash}] Close()メソッド呼び出し完了 - IsVisible: {IsVisible}");
            
            // 5. 強制的にGC実行（デバッグ用）
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Console.WriteLine($"🔧 [SimpleSettingsView#{windowHash}] GC.Collect()完了");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 [SimpleSettingsView#{windowHash}] Close()エラー: {ex.Message}");
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        
        var windowHash = GetHashCode();
        Console.WriteLine($"🔧 [SimpleSettingsView#{windowHash}] OnLoaded呼び出し");
        
        if (DataContext is SimpleSettingsViewModel viewModel)
        {
            Console.WriteLine($"🔧 [SimpleSettingsView#{windowHash}] OnLoaded - ViewModel設定: UseLocalEngine={viewModel.UseLocalEngine}, SourceLanguage={viewModel.SourceLanguage}, TargetLanguage={viewModel.TargetLanguage}, FontSize={viewModel.FontSize}");
        }
        else
        {
            Console.WriteLine($"⚠️ [SimpleSettingsView#{windowHash}] OnLoaded - DataContextがSimpleSettingsViewModelではありません: {DataContext?.GetType().Name}");
        }
    }
}