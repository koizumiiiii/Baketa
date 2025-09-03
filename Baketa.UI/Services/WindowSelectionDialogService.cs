using Baketa.Application.Services.UI;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;
using Baketa.UI.ViewModels;
using Baketa.UI.Views;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// ウィンドウ選択ダイアログサービス実装（UIレイヤー）
/// Clean Architecture原則に従い、UI固有のダイアログ表示責務を担当
/// </summary>
public sealed class WindowSelectionDialogService : IWindowSelectionDialogService
{
    private readonly IEventAggregator _eventAggregator;
    private readonly IWindowManagerAdapter _windowManager;
    private readonly ILogger<WindowSelectionDialogService> _logger;

    public WindowSelectionDialogService(
        IEventAggregator eventAggregator,
        IWindowManagerAdapter windowManager,
        ILogger<WindowSelectionDialogService> logger)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<WindowInfo?> ShowWindowSelectionDialogAsync()
    {
        try
        {
            _logger.LogDebug("🎯 UltraThink XAML修正版: ウィンドウ選択ダイアログ開始");
            Console.WriteLine("🎯 UltraThink XAML修正版: ウィンドウ選択ダイアログ開始");

            return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync<WindowInfo?>(async () =>
            {
                try
                {
                    _logger.LogDebug("🔧 XAML修正版ウィンドウ作成開始");
                    Console.WriteLine("🔧 XAML修正版ウィンドウ作成開始");
                    
                    // UltraThink Phase 1: ViewModelの事前初期化と準備
                    // Note: 型安全なロガーを作成（NullLoggerを使用してエラー回避）
                    var viewModelLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<WindowSelectionDialogViewModel>.Instance;
                    var viewModel = new WindowSelectionDialogViewModel(_eventAggregator, viewModelLogger, _windowManager);
                    
                    Console.WriteLine("🔧 ViewModel作成完了 - ExecuteRefreshAsync実行開始");
                    
                    // 🎯 重要: ウィンドウリストを事前読み込み（XAMLバインディングエラー防止）
                    await viewModel.ExecuteRefreshAsync();
                    
                    _logger.LogDebug("🔧 ViewModel事前初期化完了: Windows={Count}", viewModel.AvailableWindows.Count);
                    Console.WriteLine($"🔧 ViewModel事前初期化完了: Windows={viewModel.AvailableWindows.Count}");

                    // 🎯 UltraThink Phase 2: WindowSelectionDialogViewを完全回避
                    // XAML初期化問題の根本回避のため、プログラマティックWindow構築
                    _logger.LogDebug("🔧 プログラマティックWindow構築開始");
                    Console.WriteLine("🔧 プログラマティックWindow構築開始");
                    
                    var dialogView = CreateProgrammaticWindow(viewModel);

                    _logger.LogDebug("🔧 プログラマティックWindow構築完了");
                    Console.WriteLine("🔧 プログラマティックWindow構築完了");

                    // メインウィンドウを取得
                    var owner = Avalonia.Application.Current?.ApplicationLifetime
                        is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                        ? desktop.MainWindow : null;

                    if (owner != null)
                    {
                        _logger.LogDebug("🎯 修正XAML ShowDialog実行直前");
                        Console.WriteLine("🎯 修正XAML ShowDialog実行直前");
                        
                        // UltraThink Phase 3: ShowDialog実行（修正済みXAML使用）
                        await dialogView.ShowDialog(owner);
                        
                        _logger.LogDebug("✅ 修正XAML ShowDialog成功: DialogResult={Result}", viewModel.DialogResult != null);
                        Console.WriteLine($"✅ 修正XAML ShowDialog成功: DialogResult={viewModel.DialogResult != null}");
                        
                        return viewModel.DialogResult;
                    }
                    else
                    {
                        _logger.LogWarning("❌ オーナーウィンドウが取得できませんでした");
                        Console.WriteLine("❌ オーナーウィンドウが取得できませんでした");
                        return null;
                    }
                }
                catch (Exception innerEx)
                {
                    _logger.LogError(innerEx, "💥 修正XAML版でもエラー発生");
                    Console.WriteLine($"💥 修正XAML版でもエラー発生: {innerEx.Message}");
                    Console.WriteLine($"💥 スタックトレース: {innerEx.StackTrace}");
                    return null;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 テストウィンドウ表示前にクラッシュ - UIThread問題");
            Console.WriteLine($"💥 テストウィンドウ表示前にクラッシュ - UIThread問題: {ex.Message}");
            Console.WriteLine($"💥 スタックトレース: {ex.StackTrace}");
            return null;
        }
    }
    
    /// <summary>
    /// 🎯 UltraThink修正: プログラマティックWindow構築（XAML初期化問題回避）
    /// </summary>
    private Avalonia.Controls.Window CreateProgrammaticWindow(WindowSelectionDialogViewModel viewModel)
    {
        var window = new Avalonia.Controls.Window
        {
            Title = "ウィンドウ選択",
            Width = 800,
            Height = 600,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen,
            CanResize = false,
            ShowInTaskbar = false,
            Topmost = true
        };

        // Grid レイアウト
        var grid = new Avalonia.Controls.Grid();
        grid.RowDefinitions.Add(new Avalonia.Controls.RowDefinition(Avalonia.Controls.GridLength.Auto)); // Header
        grid.RowDefinitions.Add(new Avalonia.Controls.RowDefinition(Avalonia.Controls.GridLength.Star));  // Content
        grid.RowDefinitions.Add(new Avalonia.Controls.RowDefinition(Avalonia.Controls.GridLength.Auto)); // Footer

        // Header
        var headerBorder = new Avalonia.Controls.Border
        {
            Background = Avalonia.Media.Brushes.White,
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(224, 224, 224)),
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            Padding = new Avalonia.Thickness(20)
        };

        var headerText = new Avalonia.Controls.TextBlock
        {
            Text = "翻訳対象ウィンドウを選択してください",
            FontSize = 18,
            FontWeight = Avalonia.Media.FontWeight.Medium
        };
        headerBorder.Child = headerText;
        Avalonia.Controls.Grid.SetRow(headerBorder, 0);

        // Content: ScrollViewer for windows
        var scrollViewer = new Avalonia.Controls.ScrollViewer
        {
            Padding = new Avalonia.Thickness(20),
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

        // 🎯 4列グリッドレイアウト実装: UniformGridによる整然とした配置
        var uniformGrid = new Avalonia.Controls.Primitives.UniformGrid
        {
            Columns = 4, // 横4列固定
            Margin = new Avalonia.Thickness(10)
        };

        var itemsControl = new Avalonia.Controls.ItemsControl
        {
            ItemsSource = viewModel.AvailableWindows,
            ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Avalonia.Controls.Panel>(() => uniformGrid)
        };

        // Footer: Buttons
        var footerBorder = new Avalonia.Controls.Border
        {
            Background = Avalonia.Media.Brushes.White,
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(224, 224, 224)),
            BorderThickness = new Avalonia.Thickness(0, 1, 0, 0),
            Padding = new Avalonia.Thickness(20)
        };

        var buttonPanel = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 12
        };

        var cancelButton = new Avalonia.Controls.Button
        {
            Content = "キャンセル",
            Width = 100,
            Height = 32
        };

        var selectButton = new Avalonia.Controls.Button
        {
            Content = "選択",
            Width = 100,
            Height = 32,
            IsEnabled = false
        };

        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(selectButton);
        footerBorder.Child = buttonPanel;
        Avalonia.Controls.Grid.SetRow(footerBorder, 2);

        // Grid assembly
        grid.Children.Add(headerBorder);
        grid.Children.Add(scrollViewer);
        grid.Children.Add(footerBorder);
        window.Content = grid;

        // Event handlers - 2段階選択機能
        WindowInfo? selectedWindow = null;
        Avalonia.Controls.Border? selectedBorder = null;
        DateTime lastClickTime = DateTime.MinValue;
        const int DoubleClickTimeMs = 500; // ダブルクリック判定時間
        bool shouldCloseOnDoubleClick = false; // ダブルクリック完了フラグ
        
        // 選択状態を視覚的に更新するヘルパー関数
        void UpdateSelectionVisual(Avalonia.Controls.Border? newSelectedBorder, WindowInfo? newSelectedWindow)
        {
            // 前の選択を解除
            if (selectedBorder != null)
            {
                selectedBorder.BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(224, 224, 224));
                selectedBorder.BorderThickness = new Avalonia.Thickness(1);
                selectedBorder.Background = Avalonia.Media.Brushes.White;
            }
            
            // 新しい選択をハイライト
            if (newSelectedBorder != null)
            {
                newSelectedBorder.BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0, 120, 215)); // Windows青色
                newSelectedBorder.BorderThickness = new Avalonia.Thickness(3);
                newSelectedBorder.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(230, 243, 255)); // 薄い青背景
            }
            
            selectedBorder = newSelectedBorder;
            selectedWindow = newSelectedWindow;
            selectButton.IsEnabled = selectedWindow != null;
            
            Console.WriteLine($"🎯 選択状態更新: {selectedWindow?.Title ?? "なし"}");
        }
        
        // ダブルクリック完了処理
        void CompleteDoubleClickSelection(WindowInfo windowInfo)
        {
            Console.WriteLine($"🎯 ダブルクリック検出: {windowInfo.Title} - 選択完了");
            viewModel.DialogResult = windowInfo;
            shouldCloseOnDoubleClick = true;
            // Dispatcher.UIThread.Postで非同期でダイアログを閉じる
            Avalonia.Threading.Dispatcher.UIThread.Post(() => window.Close());
        }

        // ItemTemplate 設定 - 4列グリッド対応のコンパクトレイアウト（変数宣言後）
        itemsControl.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<WindowInfo>((window, _) =>
        {
            var border = new Avalonia.Controls.Border
            {
                Background = Avalonia.Media.Brushes.White,
                BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(224, 224, 224)),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(8),
                Margin = new Avalonia.Thickness(8),
                Padding = new Avalonia.Thickness(16),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };

            // 🎯 4列グリッド用レイアウト: 縦方向コンパクト配置
            var verticalPanel = new Avalonia.Controls.StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Vertical,
                Spacing = 8
            };

            // 🎯 4列グリッド用コンパクトサムネイル
            var thumbnailBorder = new Avalonia.Controls.Border
            {
                Width = 120,
                Height = 90,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(245, 245, 245)),
                BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(200, 200, 200)),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(4),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };
            
            var thumbnailImage = new Avalonia.Controls.Image
            {
                Stretch = Avalonia.Media.Stretch.Uniform
            };
            
            thumbnailBorder.Child = thumbnailImage;
            
            // ウィンドウのサムネイル取得を試行
            try
            {
                // 🎯 NullReference対策: null安全性チェック (例外throw回避)
                if (window == null || _windowManager == null)
                {
                    Console.WriteLine($"⚠️  ウィンドウキャプチャ取得スキップ - Null参照: window={window != null}, _windowManager={_windowManager != null}");
                    thumbnailBorder.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(255, 200, 200));
                    // 早期リターンではなく処理続行
                }
                else
                {
                    string? thumbnailBase64 = _windowManager.GetWindowThumbnail(window.Handle, 120, 90);
                if (!string.IsNullOrEmpty(thumbnailBase64))
                {
                    var bytes = Convert.FromBase64String(thumbnailBase64);
                    using var stream = new System.IO.MemoryStream(bytes);
                    thumbnailImage.Source = new Avalonia.Media.Imaging.Bitmap(stream);
                    Console.WriteLine($"✅ ウィンドウキャプチャ表示成功: {window.Title}");
                }
                else
                {
                    // サムネイル取得失敗時はプレースホルダー（グレー表示）
                    thumbnailBorder.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(240, 240, 240));
                    Console.WriteLine($"⚠️  ウィンドウキャプチャ取得失敗: {window.Title}");
                }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ウィンドウキャプチャ表示エラー: {window.Title}, {ex.Message}");
                thumbnailBorder.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(255, 200, 200));
            }
            
            // 🎯 4列グリッド用コンパクトテキスト
            var titleText = new Avalonia.Controls.TextBlock
            {
                Text = window.Title.Length > 25 ? window.Title.Substring(0, 22) + "..." : window.Title,
                FontSize = 12,
                FontWeight = Avalonia.Media.FontWeight.Medium,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                TextAlignment = Avalonia.Media.TextAlignment.Center,
                MaxLines = 2
            };
            
            verticalPanel.Children.Add(thumbnailBorder);
            verticalPanel.Children.Add(titleText);
            border.Child = verticalPanel;
            
            // 🎯 2段階クリック選択機能実装
            border.Tag = window; // Windowの参照をTagに保存
            border.PointerPressed += (sender, e) =>
            {
                var currentTime = DateTime.Now;
                var timeSinceLastClick = (currentTime - lastClickTime).TotalMilliseconds;
                lastClickTime = currentTime;
                
                Console.WriteLine($"🖱️ ウィンドウクリック: {window.Title}, 前回から{timeSinceLastClick}ms");
                
                // 同じウィンドウを短時間内にクリック = ダブルクリック
                if (selectedWindow == window && timeSinceLastClick < DoubleClickTimeMs)
                {
                    CompleteDoubleClickSelection(window);
                    return;
                }
                
                // 1回クリック = 選択状態
                Console.WriteLine($"🔄 シングルクリック: {window.Title} - 選択状態に変更");
                UpdateSelectionVisual((Avalonia.Controls.Border)sender!, window);
                viewModel.SelectedWindow = window;
            };
            
            return border;
        });

        scrollViewer.Content = itemsControl;
        Avalonia.Controls.Grid.SetRow(scrollViewer, 1);

        selectButton.Click += (s, e) =>
        {
            if (selectedWindow != null)
            {
                Console.WriteLine($"✅ 選択ボタン押下: {selectedWindow.Title}");
                viewModel.DialogResult = selectedWindow;
                window.Close();
            }
            else
            {
                Console.WriteLine("⚠️ 選択ボタン押下されましたが、選択されたウィンドウがありません");
            }
        };

        cancelButton.Click += (s, e) =>
        {
            viewModel.DialogResult = null;
            window.Close();
        };

        return window;
    }
}