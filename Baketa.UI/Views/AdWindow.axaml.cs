using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.WebView.Desktop;
using Baketa.UI.Constants;
using Baketa.UI.ViewModels;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Views;

/// <summary>
/// 広告表示専用ウィンドウ（移動可能、画面外制約付き）
/// </summary>
public partial class AdWindow : Window
{
    private readonly ILogger<AdWindow>? _logger;
    // TODO: WebView型名を確認後に有効化
    // private WebView? _adWebView;

    // ドラッグ中のパフォーマンス最適化用キャッシュ
    private double _cachedScaling = 1.0;
    private Screen? _cachedScreen;
    private PixelRect _cachedWorkingArea;

    public AdWindow()
    {
        InitializeComponent();

        // 画面右下に配置
        PositionWindowAtBottomRight();

        // Loadedイベントでウィンドウ位置を再調整
        Loaded += OnLoaded;
    }

    /// <summary>
    /// DI対応コンストラクタ
    /// </summary>
    public AdWindow(AdViewModel viewModel, ILogger<AdWindow> logger) : this()
    {
        DataContext = viewModel;
        _logger = logger;

        _logger.LogInformation("AdWindow初期化: ViewModel設定完了");

        // ViewModelの広告コンテンツ変更を監視
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // 🔧 DPIスケーリング対応: 物理サイズを考慮した位置計算
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
            if (screen == null) return;

            // DPIスケーリング率を取得
            var scaling = this.VisualRoot?.RenderScaling ?? 1.0;
            var workingArea = screen.WorkingArea;

            // 🔧 Issue #199 再修正: 実際のFrameSizeを優先使用
            // Loaded後はFrameSizeが確定しているため、計算値より正確
            var frameSize = FrameSize;
            double physicalWidth;
            double physicalHeight;

            if (frameSize is { Width: > 0, Height: > 0 })
            {
                // 実際のフレームサイズを使用（最も正確）
                physicalWidth = frameSize.Value.Width;
                physicalHeight = frameSize.Value.Height;
                _logger?.LogInformation("FrameSize使用: {FrameSize}", frameSize);
            }
            else
            {
                // フォールバック: 論理サイズ×スケーリングで計算
                physicalWidth = AdConstants.Width * scaling;
                physicalHeight = AdConstants.Height * scaling;
                _logger?.LogInformation("計算サイズ使用（FrameSize未取得）");
            }

            // マージンもスケーリングを適用（論理ピクセル→物理ピクセル）
            var physicalMargin = (int)(AdConstants.ScreenMargin * scaling);

            _logger?.LogInformation("ウィンドウサイズ: Physical=({PhysicalW}x{PhysicalH}), Margin={Margin}, Scaling={Scaling}",
                physicalWidth, physicalHeight, physicalMargin, scaling);
            _logger?.LogInformation("作業領域: {WorkingArea}, 現在位置: {Position}",
                workingArea, Position);

            // 物理サイズで右下端に配置
            var x = workingArea.Right - (int)physicalWidth - physicalMargin;
            var y = workingArea.Bottom - (int)physicalHeight - physicalMargin;

            // 画面左上端制約
            x = Math.Max(x, workingArea.X);
            y = Math.Max(y, workingArea.Y);

            var newPosition = new PixelPoint(x, y);
            Position = newPosition;
            _logger?.LogInformation("物理サイズで位置補正: ({X}, {Y})", x, y);

        }, Avalonia.Threading.DispatcherPriority.Loaded);

        // TODO: WebView統合後に有効化
        // // WebViewを取得
        // _adWebView = this.FindControl<WebView>("AdWebView");
        //
        // // 初回の広告コンテンツをロード
        // if (DataContext is AdViewModel viewModel && _adWebView != null)
        // {
        //     LoadAdContent(viewModel.AdHtmlContent);
        // }

        // 🎯 ドラッグ移動機能を有効化（Avaloniaネイティブドラッグ + 画面外制約）
        PointerPressed += OnPointerPressed;
        PositionChanged += OnPositionChanged;

        _logger?.LogInformation("AdWindow表示完了: 画面右下に配置、ネイティブドラッグ移動可能");
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // TODO: WebView統合後に有効化
        // if (e.PropertyName == nameof(AdViewModel.AdHtmlContent) && DataContext is AdViewModel viewModel)
        // {
        //     LoadAdContent(viewModel.AdHtmlContent);
        // }
        // else
        if (e.PropertyName == nameof(AdViewModel.ShouldShowAd))
        {
            // 表示/非表示の切り替え
            _logger?.LogDebug("広告表示状態変更: {ShouldShowAd}", (DataContext as AdViewModel)?.ShouldShowAd);
        }
    }

    // TODO: WebView統合後に有効化
    // private void LoadAdContent(string htmlContent)
    // {
    //     if (_adWebView == null || string.IsNullOrEmpty(htmlContent))
    //     {
    //         _logger?.LogDebug("WebViewまたはHTMLコンテンツが空のため、広告をロードしません");
    //         return;
    //     }
    //
    //     try
    //     {
    //         _adWebView.LoadHtml(htmlContent);
    //         _logger?.LogInformation("広告HTMLをWebViewにロード完了");
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger?.LogError(ex, "広告HTMLのロード中にエラーが発生: {Message}", ex.Message);
    //     }
    // }

    /// <summary>
    /// ウィンドウを画面右下に配置
    /// </summary>
    private void PositionWindowAtBottomRight()
    {
        try
        {
            // デバッグ: すべてのスクリーン情報をログ出力
            var allScreens = Screens.All.ToList();
            _logger?.LogInformation("検出されたスクリーン数: {Count}", allScreens.Count);
            foreach (var s in allScreens)
            {
                _logger?.LogInformation("  - {Name}: Bounds={Bounds}, WorkingArea={WorkingArea}, Primary={IsPrimary}",
                    s.DisplayName ?? "Unknown", s.Bounds, s.WorkingArea, s == Screens.Primary);
            }

            // マルチモニター環境対応: 最も左上のスクリーンを使用（最小のBounds.Xとyを持つ）
            var screen = Screens.All
                .OrderBy(s => s.Bounds.X)
                .ThenBy(s => s.Bounds.Y)
                .FirstOrDefault() ?? Screens.Primary;

            if (screen == null)
            {
                _logger?.LogWarning("スクリーンが見つかりません");
                return;
            }

            var workingArea = screen.WorkingArea;

            // DPIスケーリングを取得（この時点ではVisualRootが未設定の可能性があるためscreen.Scalingを使用）
            var scaling = screen.Scaling;

            // 🔧 Issue #199: この時点ではBoundsが未設定なのでscalingで計算
            // Loaded後に再調整されるため、ここでは概算値を使用
            var physicalWidth = (int)(AdConstants.Width * scaling);
            var physicalHeight = (int)(AdConstants.Height * scaling);
            // マージンもスケーリングを適用（論理ピクセル→物理ピクセル）
            var physicalMargin = (int)(AdConstants.ScreenMargin * scaling);

            var x = workingArea.Right - physicalWidth - physicalMargin;
            var y = workingArea.Bottom - physicalHeight - physicalMargin;

            Position = new PixelPoint(x, y);

            _logger?.LogInformation("AdWindow位置設定: Screen={ScreenName}, Bounds={Bounds}, WorkingArea={WorkingArea}, Position=({X}, {Y}), Scaling={Scaling}",
                screen.DisplayName ?? "Unknown", screen.Bounds, workingArea, x, y, scaling);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ウィンドウ位置設定中にエラーが発生: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// ドラッグ開始処理（Avaloniaネイティブドラッグを使用）
    /// </summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // パフォーマンス最適化: ドラッグ開始時にスクリーン情報とDPIをキャッシュ
            _cachedScaling = this.VisualRoot?.RenderScaling ?? 1.0;
            _cachedScreen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
            _cachedWorkingArea = _cachedScreen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);

            _logger?.LogDebug("ネイティブドラッグ開始: Position={Position}, Scaling={Scaling}", Position, _cachedScaling);

            // Avaloniaネイティブのドラッグ機能を使用（OS最適化、カーソル同期）
            BeginMoveDrag(e);
        }
    }

    // BeginMoveDrag() 使用のため OnPointerMoved() と OnPointerReleased() は不要

    /// <summary>
    /// ウィンドウ位置変更時の画面内制約チェック（BeginMoveDrag後に呼ばれる）
    /// </summary>
    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        // BeginMoveDrag() による移動後、画面外に出ていないか確認
        var constrainedPosition = ConstrainToScreen(e.Point);
        if (e.Point != constrainedPosition)
        {
            Position = constrainedPosition;
            _logger?.LogDebug("画面外検出: 位置を補正 {Old} → {New}", e.Point, constrainedPosition);
        }
    }

    /// <summary>
    /// ウィンドウ位置を画面内に制約（DPIスケーリング対応、キャッシュ優先）
    /// </summary>
    private PixelPoint ConstrainToScreen(PixelPoint position)
    {
        try
        {
            // キャッシュが有効なら使用（パフォーマンス最適化）
            Screen? screen;
            double scaling;
            PixelRect workingArea;

            if (_cachedScreen != null)
            {
                // キャッシュを使用（高速）
                screen = _cachedScreen;
                scaling = _cachedScaling;
                workingArea = _cachedWorkingArea;
            }
            else
            {
                // 通常処理（Screen検索）
                screen = Screens.All.FirstOrDefault(s => s.Bounds.Contains(position))
                    ?? Screens.All.FirstOrDefault(s => s.Bounds.Contains(new PixelPoint(0, 0)))
                    ?? Screens.Primary;

                if (screen == null) return position;

                scaling = this.VisualRoot?.RenderScaling ?? 1.0;
                workingArea = screen.WorkingArea;
            }

            // 🔧 Issue #199 再修正: Boundsに依存せず、常に論理サイズ×RenderScalingで計算
            var physicalWidth = (int)(AdConstants.Width * scaling);
            var physicalHeight = (int)(AdConstants.Height * scaling);

            // 画面左端制約
            var constrainedX = Math.Max(workingArea.X, position.X);
            // 画面右端制約（ウィンドウが完全に表示されるように）
            constrainedX = Math.Min(constrainedX, workingArea.Right - physicalWidth);

            // 画面上端制約
            var constrainedY = Math.Max(workingArea.Y, position.Y);
            // 画面下端制約（ウィンドウが完全に表示されるように）
            constrainedY = Math.Min(constrainedY, workingArea.Bottom - physicalHeight);

            return new PixelPoint(constrainedX, constrainedY);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "画面内制約の適用中にエラーが発生: {Message}", ex.Message);
            return position;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // ViewModelのイベント購読を解除
        if (DataContext is AdViewModel viewModel)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        base.OnClosed(e);
        _logger?.LogInformation("AdWindow終了");
    }
}
