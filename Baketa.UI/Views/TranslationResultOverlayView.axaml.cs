using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Baketa.UI.ViewModels;
using Baketa.UI.Utils;
using System;
using System.IO;

namespace Baketa.UI.Views;

public partial class TranslationResultOverlayView : Window
{
    public TranslationResultOverlayView()
    {
        Console.WriteLine("🖥️ TranslationResultOverlayView初期化開始");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🖥️ TranslationResultOverlayView初期化開始");
        
        InitializeComponent();
        
        Console.WriteLine("🖥️ TranslationResultOverlayView - InitializeComponent完了");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🖥️ TranslationResultOverlayView - InitializeComponent完了");
        
        // ウィンドウの設定
        DataContextChanged += OnDataContextChanged;
        
        // マウスイベントを無効化（オーバーレイがゲームプレイを邪魔しないように）
        this.IsHitTestVisible = false;
        
        Console.WriteLine($"🖥️ TranslationResultOverlayView初期化完了 - IsHitTestVisible: {IsHitTestVisible}");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🖥️ TranslationResultOverlayView初期化完了 - IsHitTestVisible: {IsHitTestVisible}");
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        Console.WriteLine($"🖥️ TranslationResultOverlayView.OnDataContextChanged呼び出し - DataContext: {DataContext?.GetType().Name ?? "null"}");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🖥️ TranslationResultOverlayView.OnDataContextChanged呼び出し - DataContext: {DataContext?.GetType().Name ?? "null"}");
        
        if (DataContext is TranslationResultOverlayViewModel viewModel)
        {
            var viewInstanceId = this.GetHashCode().ToString("X8");
            var viewModelInstanceId = viewModel.GetHashCode().ToString("X8");
            Console.WriteLine($"🖥️ TranslationResultOverlayView - ViewModelのPropertyChangedイベント購読開始");
            Console.WriteLine($"   🔗 View インスタンスID: {viewInstanceId}");
            Console.WriteLine($"   🔗 ViewModel インスタンスID: {viewModelInstanceId}");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🖥️ TranslationResultOverlayView - ViewModelのPropertyChangedイベント購読開始");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   🔗 View インスタンスID: {viewInstanceId}");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   🔗 ViewModel インスタンスID: {viewModelInstanceId}");
            
            // ViewModelの変更を監視
            viewModel.PropertyChanged += (s, e) =>
            {
                var senderInstanceId = s?.GetHashCode().ToString("X8") ?? "NULL";
                Console.WriteLine($"🖥️ TranslationResultOverlayView - PropertyChanged受信: {e.PropertyName} (Sender: {senderInstanceId})");
                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🖥️ TranslationResultOverlayView - PropertyChanged受信: {e.PropertyName} (Sender: {senderInstanceId})");
                
                if (e.PropertyName == nameof(TranslationResultOverlayViewModel.IsOverlayVisible))
                {
                    Console.WriteLine($"🖥️ TranslationResultOverlayView - IsOverlayVisibleプロパティ変更検出: {viewModel.IsOverlayVisible}");
                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🖥️ TranslationResultOverlayView - IsOverlayVisibleプロパティ変更検出: {viewModel.IsOverlayVisible}");
                    
                    Console.WriteLine($"🔍 UpdateVisibility呼び出し前 - View.IsVisible: {IsVisible}, ViewModel.IsOverlayVisible: {viewModel.IsOverlayVisible}");
                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 UpdateVisibility呼び出し前 - View.IsVisible: {IsVisible}, ViewModel.IsOverlayVisible: {viewModel.IsOverlayVisible}");
                    
                    UpdateVisibility(viewModel.IsOverlayVisible);
                    
                    Console.WriteLine($"🔍 UpdateVisibility呼び出し後 - View.IsVisible: {IsVisible}");
                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 UpdateVisibility呼び出し後 - View.IsVisible: {IsVisible}");
                }
                else if (e.PropertyName == nameof(TranslationResultOverlayViewModel.PositionX) ||
                         e.PropertyName == nameof(TranslationResultOverlayViewModel.PositionY))
                {
                    Console.WriteLine($"🖥️ TranslationResultOverlayView - 位置プロパティ変更検出: X={viewModel.PositionX}, Y={viewModel.PositionY}");
                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🖥️ TranslationResultOverlayView - 位置プロパティ変更検出: X={viewModel.PositionX}, Y={viewModel.PositionY}");
                    UpdatePosition(viewModel.PositionX, viewModel.PositionY);
                }
                else
                {
                    // 他のプロパティ変更もログに記録
                    Console.WriteLine($"🖥️ TranslationResultOverlayView - その他プロパティ変更: {e.PropertyName}");
                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🖥️ TranslationResultOverlayView - その他プロパティ変更: {e.PropertyName}");
                }
            };
            
            Console.WriteLine("✅ TranslationResultOverlayView - PropertyChangedイベント購読完了");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ TranslationResultOverlayView - PropertyChangedイベント購読完了");
            
            // 初期状態を同期（PropertyChangedイベントを逃した場合に備えて）
            Console.WriteLine($"🔄 初期状態同期開始 - ViewModel.IsOverlayVisible: {viewModel.IsOverlayVisible}");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔄 初期状態同期開始 - ViewModel.IsOverlayVisible: {viewModel.IsOverlayVisible}");
            
            UpdateVisibility(viewModel.IsOverlayVisible);
            UpdatePosition(viewModel.PositionX, viewModel.PositionY);
            
            Console.WriteLine("✅ 初期状態同期完了");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ 初期状態同期完了");
            
            // PropertyChangedイベントの代替として定期的な状態同期を開始
            StartPeriodicSync(viewModel);
            
            Console.WriteLine("🔄 定期的状態同期を開始しました");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 定期的状態同期を開始しました");
        }
        else
        {
            Console.WriteLine("⚠️ TranslationResultOverlayView - DataContextがTranslationResultOverlayViewModelではありません");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "⚠️ TranslationResultOverlayView - DataContextがTranslationResultOverlayViewModelではありません");
        }
    }

    private System.Threading.Timer? _syncTimer;
    private TranslationResultOverlayViewModel? _currentViewModel;
    
    private void StartPeriodicSync(TranslationResultOverlayViewModel viewModel)
    {
        _currentViewModel = viewModel;
        
        // 200ms間隔でViewModelの状態をチェック（リアルタイム性向上）
        _syncTimer = new System.Threading.Timer(SyncWithViewModel, null, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));
    }
    
    private bool _lastTargetVisibility = false;
    
    private void SyncWithViewModel(object? state)
    {
        if (_currentViewModel == null) return;
        
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    // IsOverlayVisibleの状態を強制同期
                    var currentViewVisibility = IsVisible;
                    var targetVisibility = _currentViewModel.IsOverlayVisible;
                    
                    // 前回と同じ状態で、かつ実際の表示状態も一致している場合は何もしない
                    if (_lastTargetVisibility == targetVisibility && currentViewVisibility == targetVisibility)
                    {
                        return; // ログ出力せずに早期リターン
                    }
                    
                    Console.WriteLine($"🔄 同期チェック: View.IsVisible={currentViewVisibility}, Target={targetVisibility}, LastTarget={_lastTargetVisibility}");
                    
                    if (currentViewVisibility != targetVisibility)
                    {
                        Console.WriteLine($"🔄 強制状態同期: View.IsVisible={currentViewVisibility} -> Target={targetVisibility}");
                        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔄 強制状態同期: View.IsVisible={currentViewVisibility} -> Target={targetVisibility}");
                        
                        UpdateVisibility(targetVisibility);
                        _lastTargetVisibility = targetVisibility;
                    }
                    else if (_lastTargetVisibility != targetVisibility)
                    {
                        // 表示状態は一致しているが、targetが変更された場合
                        Console.WriteLine($"🔄 状態変更検出: Target={targetVisibility} (View既に同期済み)");
                        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔄 状態変更検出: Target={targetVisibility} (View既に同期済み)");
                        _lastTargetVisibility = targetVisibility;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ 状態同期エラー: {ex.Message}");
                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"⚠️ 状態同期エラー: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ 定期同期エラー: {ex.Message}");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"⚠️ 定期同期エラー: {ex.Message}");
        }
    }

    private void UpdateVisibility(bool isVisible)
    {
        Console.WriteLine($"🖥️ TranslationResultOverlayView.UpdateVisibility呼び出し: {isVisible}");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🖥️ TranslationResultOverlayView.UpdateVisibility呼び出し: {isVisible}");
        
        if (isVisible)
        {
            // デバッグ: ViewModelのテキスト内容を確認
            if (DataContext is TranslationResultOverlayViewModel vm)
            {
                Console.WriteLine($"🔍 表示前デバッグ - TranslatedText: '{vm.TranslatedText}', OriginalText: '{vm.OriginalText}', HasText: {vm.HasText}");
                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 表示前デバッグ - TranslatedText: '{vm.TranslatedText}', OriginalText: '{vm.OriginalText}', HasText: {vm.HasText}");
                
                // HasTextがfalseの場合は表示しない
                if (!vm.HasText)
                {
                    Console.WriteLine("⚠️ HasText=false のため表示をスキップ");
                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "⚠️ HasText=false のため表示をスキップ");
                    return;
                }
                
                // 位置を最新の状態に更新してからShow
                UpdatePosition(vm.PositionX, vm.PositionY);
            }
            
            Show();
            
            // Show後の詳細な状態確認
            Console.WriteLine($"🖥️ TranslationResultOverlayView.Show()実行完了:");
            Console.WriteLine($"   - IsVisible: {IsVisible}");
            Console.WriteLine($"   - Position: ({Position.X}, {Position.Y})");
            Console.WriteLine($"   - Size: {Width}x{Height}");
            Console.WriteLine($"   - Topmost: {Topmost}");
            Console.WriteLine($"   - WindowState: {WindowState}");
            
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🖥️ TranslationResultOverlayView.Show()実行完了:");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   - IsVisible: {IsVisible}");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   - Position: ({Position.X}, {Position.Y})");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   - Size: {Width}x{Height}");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   - Topmost: {Topmost}");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"   - WindowState: {WindowState}");
        }
        else
        {
            Hide();
            Console.WriteLine($"🖥️ TranslationResultOverlayView.Hide()実行完了 - IsVisible: {IsVisible}");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🖥️ TranslationResultOverlayView.Hide()実行完了 - IsVisible: {IsVisible}");
        }
    }

    private void UpdatePosition(double x, double y)
    {
        // スクリーンサイズを考慮した位置調整
        var screen = Screens.Primary;
        if (screen != null)
        {
            var bounds = screen.WorkingArea;
            var adjustedX = Math.Max(0, Math.Min(x, bounds.Width - Width));
            var adjustedY = Math.Max(0, Math.Min(y, bounds.Height - Height));
            
            Position = new Avalonia.PixelPoint((int)adjustedX, (int)adjustedY);
        }
        else
        {
            Position = new Avalonia.PixelPoint((int)x, (int)y);
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        Console.WriteLine("🖥️ TranslationResultOverlayView.OnLoaded呼び出し");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🖥️ TranslationResultOverlayView.OnLoaded呼び出し");
        
        base.OnLoaded(e);
        
        // 初期状態で非表示（ViewModelのIsOverlayVisibleに従って表示制御）
        Console.WriteLine($"🖥️ TranslationResultOverlayView.OnLoaded - DataContext: {DataContext?.GetType().Name ?? "null"}");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🖥️ TranslationResultOverlayView.OnLoaded - DataContext: {DataContext?.GetType().Name ?? "null"}");
        
        if (DataContext is TranslationResultOverlayViewModel viewModel)
        {
            Console.WriteLine($"🖥️ TranslationResultOverlayView.OnLoaded - ViewModel.IsOverlayVisible: {viewModel.IsOverlayVisible}");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🖥️ TranslationResultOverlayView.OnLoaded - ViewModel.IsOverlayVisible: {viewModel.IsOverlayVisible}");
            
            // ViewModelの状態に応じて表示/非表示
            if (!viewModel.IsOverlayVisible)
            {
                Hide();
                Console.WriteLine("🖥️ TranslationResultOverlayView.OnLoaded - Hide()実行（ViewModelのIsOverlayVisible=false）");
                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🖥️ TranslationResultOverlayView.OnLoaded - Hide()実行（ViewModelのIsOverlayVisible=false）");
            }
            else
            {
                Show();
                Console.WriteLine("🖥️ TranslationResultOverlayView.OnLoaded - Show()実行（ViewModelのIsOverlayVisible=true）");
                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🖥️ TranslationResultOverlayView.OnLoaded - Show()実行（ViewModelのIsOverlayVisible=true）");
            }
        }
        else
        {
            Hide();
            Console.WriteLine("🖥️ TranslationResultOverlayView.OnLoaded - Hide()実行（DataContextがnull）");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🖥️ TranslationResultOverlayView.OnLoaded - Hide()実行（DataContextがnull）");
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        // オーバーレイはクリック不可
        e.Handled = false;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        // オーバーレイはマウスイベントを無視
        e.Handled = false;
    }
}