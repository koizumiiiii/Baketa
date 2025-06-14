using System.Reactive;
using System.Runtime.InteropServices;
using Baketa.Core.UI.Geometry;
using Baketa.Core.UI.Overlay;
using Baketa.UI.Framework;
using Baketa.UI.Framework.ReactiveUI;
using Baketa.UI.Overlay;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using UIEvents = Baketa.UI.Framework.Events;

namespace Baketa.UI.ViewModels;

    /// <summary>
    /// オーバーレイ設定画面のビューモデル
    /// </summary>
    internal sealed class OverlayViewModel : Framework.ViewModelBase
    {
        private readonly AvaloniaOverlayWindowAdapter? _overlayAdapter;
        private IOverlayWindow? _previewOverlay;
        // オーバーレイの表示位置
        private string _position = "上";
        public string Position
        {
            get => _position;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _position, value);
        }
        
        // フォントサイズ
        private int _fontSize = 16;
        public int FontSize
        {
            get => _fontSize;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _fontSize, value);
        }
        
        // フォントカラー
        private string _fontColor = "#FFFFFF";
        public string FontColor
        {
            get => _fontColor;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _fontColor, value);
        }
        
        // 背景カラー
        private string _backgroundColor = "#000000";
        public string BackgroundColor
        {
            get => _backgroundColor;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _backgroundColor, value);
        }
        
        // 背景の透明度
        private int _backgroundOpacity = 80;
        public int BackgroundOpacity
        {
            get => _backgroundOpacity;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _backgroundOpacity, value);
        }
        
        // オーバーレイの幅
        private int _width = 600;
        public int Width
        {
            get => _width;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _width, value);
        }
        
        // オーバーレイの高さ
        private int _height = 100;
        public int Height
        {
            get => _height;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _height, value);
        }
        
        // 表示時間（秒）
        private int _displayDuration = 5;
        public int DisplayDuration
        {
            get => _displayDuration;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _displayDuration, value);
        }
        
        // プレビューテキスト
        private string _previewText = "これはオーバーレイのプレビューです";
        public string PreviewText
        {
        get => _previewText;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _previewText, value);
        }
        
        // オーバーレイ表示制御
        private bool _isOverlayVisible;
    public bool IsOverlayVisible
    {
        get => _isOverlayVisible;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _isOverlayVisible, value);
    }
    
    // テキスト色ピッカー用
    private string _textColor = "#FFFFFF";
    public string TextColor 
    {
        get => _textColor;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _textColor, value);
    }
    
    // フォント設定
    public string[] AvailableFonts { get; } = ["Yu Gothic UI", "Meiryo UI", "MS UI Gothic", "Arial", "Segoe UI"];
    
    private string _fontFamily = "Yu Gothic UI";
    public string FontFamily
    {
        get => _fontFamily;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _fontFamily, value);
    }
    
    private bool _isBold;
    public bool IsBold
    {
        get => _isBold;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _isBold, value);
    }
    
    // オーバーレイ位置設定
    public string[] AvailablePositions { get; } = ["上", "下", "左", "右", "中央", "カスタム"];
    
    private int _offsetX;
    public int OffsetX
    {
        get => _offsetX;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _offsetX, value);
    }
    
    private int _offsetY;
    public int OffsetY
    {
        get => _offsetY;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _offsetY, value);
    }
    
    // 動作設定
    private bool _allowDrag = true;
    public bool AllowDrag
    {
        get => _allowDrag;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _allowDrag, value);
    }
    
    private bool _allowResize = true;
    public bool AllowResize
    {
        get => _allowResize;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _allowResize, value);
    }
    
    private bool _showCloseButton = true;
    public bool ShowCloseButton
    {
        get => _showCloseButton;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _showCloseButton, value);
    }
    
    // 設定リセットコマンド
    public ReactiveCommand<Unit, Unit> ResetSettingsCommand { get; }
        
        // コマンド
        public ReactiveCommand<Unit, Unit> SaveSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> PreviewOverlayCommand { get; }
        public ReactiveCommand<Unit, Unit> ResetToDefaultsCommand { get; }
        
        /// <summary>
        /// 新しいOverlayViewModelを初期化します
        /// </summary>
        /// <param name="eventAggregator">イベント集約器</param>
        /// <param name="overlayAdapter">オーバーレイアダプター</param>
        /// <param name="logger">ロガー</param>
        public OverlayViewModel(
            UIEvents.IEventAggregator eventAggregator,
            AvaloniaOverlayWindowAdapter? overlayAdapter = null,
            ILogger? logger = null)
            : base(eventAggregator, logger)
        {
            _overlayAdapter = overlayAdapter;
            
            // コマンドの初期化
            SaveSettingsCommand = global::Baketa.UI.Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteSaveSettingsAsync);
            PreviewOverlayCommand = global::Baketa.UI.Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecutePreviewOverlayAsync);
            ResetToDefaultsCommand = global::Baketa.UI.Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteResetToDefaultsAsync);
            ResetSettingsCommand = global::Baketa.UI.Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteResetToDefaultsAsync); // CS8618対応
        }
        
        // 設定保存コマンド実行
        private async Task ExecuteSaveSettingsAsync()
        {
            // 設定保存ロジック
            await PublishEventAsync(new OverlaySettingsChangedEvent(
                Position, 
                FontSize, 
                FontColor, 
                BackgroundColor, 
                BackgroundOpacity / 100.0)).ConfigureAwait(false);
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // プレビューコマンド実行
        private async Task ExecutePreviewOverlayAsync()
        {
            try
            {
                if (_overlayAdapter == null)
                {
                    Logger?.LogWarning("オーバーレイアダプターが利用できません。プレビューをスキップします。");
                    return;
                }
                
                // 既存のプレビューを閉じる
                if (_previewOverlay != null)
                {
                    _previewOverlay.Dispose();
                    _previewOverlay = null;
                }
                
                // プレビューオーバーレイを作成
                var overlaySize = new CoreSize(Width, Height);
                var overlayPosition = new CorePoint(OffsetX + 100, OffsetY + 100); // オフセットを適用
                
                _previewOverlay = await _overlayAdapter.CreateOverlayWindowAsync(
                    nint.Zero, // プレビュー用はターゲット無し
                    overlaySize,
                    overlayPosition).ConfigureAwait(false);
                
                // クリックスルー設定
                _previewOverlay.IsClickThrough = false; // プレビューではクリック可能に
                
                // プレビュー表示
                _previewOverlay.Show();
                
                Logger?.LogInformation("オーバーレイプレビューを表示しました。サイズ: {Size}, 位置: {Position}", overlaySize, overlayPosition);
                
                // 一定時間後に自動で閉じる
                _ = Task.Delay(TimeSpan.FromSeconds(DisplayDuration))
                    .ContinueWith(_ =>
                    {
                        if (_previewOverlay != null)
                        {
                            _previewOverlay.Dispose();
                            _previewOverlay = null;
                            Logger?.LogDebug("プレビューオーバーレイを自動で閉じました。");
                        }
                    }, TaskScheduler.Default);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogError(ex, "プレビューオーバーレイの表示中に無効な操作エラーが発生しました。");
            }
            catch (ExternalException ex)
            {
                Logger?.LogError(ex, "プレビューオーバーレイの表示中に外部エラーが発生しました。");
            }
        }
        
        // デフォルト設定リセットコマンド実行
        private async Task ExecuteResetToDefaultsAsync()
        {
            // デフォルト値に戻す
            Position = "上";
            FontSize = 16;
            FontColor = "#FFFFFF";
            BackgroundColor = "#000000";
            BackgroundOpacity = 80;
            Width = 600;
            Height = 100;
            DisplayDuration = 5;
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }
    
    // イベント定義
    /// <summary>
    /// オーバーレイ設定変更イベント
    /// </summary>
    /// <param name="position">位置</param>
    /// <param name="fontSize">フォントサイズ</param>
    /// <param name="fontColor">フォント色</param>
    /// <param name="backgroundColor">背景色</param>
    /// <param name="backgroundOpacity">背景透過度</param>
    internal sealed class OverlaySettingsChangedEvent(
        string position,
        int fontSize,
        string fontColor,
        string backgroundColor,
        double backgroundOpacity) : global::Baketa.UI.Framework.Events.UIEventBase
    {
        /// <summary>
        /// 位置
        /// </summary>
        public string Position { get; } = position;

        /// <summary>
        /// フォントサイズ
        /// </summary>
        public int FontSize { get; } = fontSize;

        /// <summary>
        /// フォント色
        /// </summary>
        public string FontColor { get; } = fontColor;

        /// <summary>
        /// 背景色
        /// </summary>
        public string BackgroundColor { get; } = backgroundColor;

        /// <summary>
        /// 背景透過度
        /// </summary>
        public double BackgroundOpacity { get; } = backgroundOpacity;
        
        /// <inheritdoc/>
        public override string Name => "OverlaySettingsChanged";
        
        /// <inheritdoc/>
        public override string Category => "UI.Overlay";
    }
