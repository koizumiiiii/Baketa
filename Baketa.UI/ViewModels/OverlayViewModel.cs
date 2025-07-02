using System.Reactive;
using System.Runtime.InteropServices;
using Baketa.Core.UI.Geometry;
using Baketa.Core.UI.Overlay;
using Baketa.Core.UI.Overlay.Positioning;
using Baketa.UI.Framework;
using Baketa.UI.Framework.ReactiveUI;
using Baketa.UI.Overlay;
using Baketa.UI.Overlay.Positioning;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using Baketa.Core.Abstractions.Events;
using UIEvents = Baketa.UI.Framework.Events;

namespace Baketa.UI.ViewModels;

    /// <summary>
    /// オーバーレイ設定画面のビューモデル
    /// オーバーレイ位置・サイズ管理システムと連携
    /// </summary>
    internal sealed class OverlayViewModel : Framework.ViewModelBase
    {
        private readonly AvaloniaOverlayWindowAdapter? _overlayAdapter;
        private readonly IOverlayPositionManager? _positionManager;
        private readonly IOverlayPositionManagerFactory? _positionManagerFactory;
        private IOverlayWindow? _previewOverlay;
        
        // オーバーレイの表示位置
        private string _position = "上";
        public string Position
        {
            get => _position;
            set
            {
                this.RaiseAndSetIfChanged(ref _position, value ?? string.Empty);
                SyncPositionToManager();
            }
        }
        
        // フォントサイズ
        private int _fontSize = 16;
        public int FontSize
        {
            get => _fontSize;
            set => this.RaiseAndSetIfChanged(ref _fontSize, Math.Max(0, value));
        }
        
        // フォントカラー
        private string _fontColor = "#FFFFFF";
        public string FontColor
        {
            get => _fontColor;
            set => this.RaiseAndSetIfChanged(ref _fontColor, value ?? "#FFFFFF");
        }
        
        // 背景カラー
        private string _backgroundColor = "#000000";
        public string BackgroundColor
        {
            get => _backgroundColor;
            set => this.RaiseAndSetIfChanged(ref _backgroundColor, value ?? "#000000");
        }
        
        // 背景の透明度
        private int _backgroundOpacity = 80;
        public int BackgroundOpacity
        {
            get => _backgroundOpacity;
            set => this.RaiseAndSetIfChanged(ref _backgroundOpacity, value);
        }
        
        // オーバーレイの幅
        private int _width = 600;
        public int Width
        {
            get => _width;
            set
            {
                var newValue = Math.Max(0, value);
                var changed = !EqualityComparer<int>.Default.Equals(_width, newValue);
                this.RaiseAndSetIfChanged(ref _width, newValue);
                if (changed)
                {
                    SyncSizeToManager();
                }
            }
        }
        
        // オーバーレイの高さ
        private int _height = 100;
        public int Height
        {
            get => _height;
            set
            {
                var newValue = Math.Max(0, value);
                var changed = !EqualityComparer<int>.Default.Equals(_height, newValue);
                this.RaiseAndSetIfChanged(ref _height, newValue);
                if (changed)
                {
                    SyncSizeToManager();
                }
            }
        }
        
        // 表示時間（秒）
        private int _displayDuration = 5;
        public int DisplayDuration
        {
            get => _displayDuration;
            set => this.RaiseAndSetIfChanged(ref _displayDuration, Math.Max(1, value));
        }
        
        // プレビューテキスト
        private string _previewText = "これはオーバーレイのプレビューです";
        public string PreviewText
        {
            get => _previewText;
            set => this.RaiseAndSetIfChanged(ref _previewText, value ?? "これはオーバーレイのプレビューです");
        }
        
        // オーバーレイ表示制御
        private bool _isOverlayVisible;
    public bool IsOverlayVisible
    {
        get => _isOverlayVisible;
        set => this.RaiseAndSetIfChanged(ref _isOverlayVisible, value);
    }
    
    // テキスト色ピッカー用
    private string _textColor = "#FFFFFF";
    public string TextColor 
    {
        get => _textColor;
        set => this.RaiseAndSetIfChanged(ref _textColor, value ?? "#FFFFFF");
    }
    
    // フォント設定
    public string[] AvailableFonts { get; } = ["Yu Gothic UI", "Meiryo UI", "MS UI Gothic", "Arial", "Segoe UI"];
    
    private string _fontFamily = "Yu Gothic UI";
    public string FontFamily
    {
        get => _fontFamily;
        set => this.RaiseAndSetIfChanged(ref _fontFamily, value ?? "Yu Gothic UI");
    }
    
    private bool _isBold;
    public bool IsBold
    {
        get => _isBold;
        set => this.RaiseAndSetIfChanged(ref _isBold, value);
    }
    
    // オーバーレイ位置設定
    public string[] AvailablePositions { get; } = ["上", "下", "左", "右", "中央", "カスタム"];
    
    private int _offsetX;
    public int OffsetX
    {
        get => _offsetX;
        set
        {
            var changed = !EqualityComparer<int>.Default.Equals(_offsetX, value);
            this.RaiseAndSetIfChanged(ref _offsetX, value);
            if (changed)
            {
                SyncPositionToManager();
            }
        }
    }
    
    private int _offsetY;
    public int OffsetY
    {
        get => _offsetY;
        set
        {
            var changed = !EqualityComparer<int>.Default.Equals(_offsetY, value);
            this.RaiseAndSetIfChanged(ref _offsetY, value);
            if (changed)
            {
                SyncPositionToManager();
            }
        }
    }
    
    // 動作設定
    private bool _allowDrag = true;
    public bool AllowDrag
    {
        get => _allowDrag;
        set => this.RaiseAndSetIfChanged(ref _allowDrag, value);
    }
    
    private bool _allowResize = true;
    public bool AllowResize
    {
        get => _allowResize;
        set => this.RaiseAndSetIfChanged(ref _allowResize, value);
    }
    
    private bool _showCloseButton = true;
    public bool ShowCloseButton
    {
        get => _showCloseButton;
        set => this.RaiseAndSetIfChanged(ref _showCloseButton, value);
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
        /// <param name="positionManager">位置管理システム</param>
        /// <param name="positionManagerFactory">位置管理システムファクトリー</param>
        /// <param name="logger">ロガー</param>
        public OverlayViewModel(
            IEventAggregator eventAggregator,
            AvaloniaOverlayWindowAdapter? overlayAdapter = null,
            IOverlayPositionManager? positionManager = null,
            IOverlayPositionManagerFactory? positionManagerFactory = null,
            ILogger? logger = null)
            : base(eventAggregator, logger)
        {
            _overlayAdapter = overlayAdapter;
            _positionManager = positionManager;
            _positionManagerFactory = positionManagerFactory;
            
            // 位置管理システムのイベント購読
            if (_positionManager != null)
            {
                _positionManager.PositionUpdated += OnPositionUpdated;
                
                // 初期設定を位置管理システムに同期
                SyncToPositionManager();
            }
            
            // コマンドの初期化
            SaveSettingsCommand = global::Baketa.UI.Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteSaveSettingsAsync);
            PreviewOverlayCommand = global::Baketa.UI.Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecutePreviewOverlayAsync);
            ResetToDefaultsCommand = global::Baketa.UI.Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteResetToDefaultsAsync);
            ResetSettingsCommand = global::Baketa.UI.Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteResetToDefaultsAsync); // CS8618対応
        }
        
        /// <summary>
        /// 位置管理システムの位置更新イベントハンドラー
        /// </summary>
        private void OnPositionUpdated(object? sender, OverlayPositionUpdatedEventArgs e)
        {
            Logger?.LogDebug("位置管理システムからの位置更新: {PrevPos} → {NewPos}, 理由: {Reason}",
                e.PreviousPosition, e.NewPosition, e.Reason);
            
            // UIプロパティを更新（循環参照を避けるため内部フィールドを直接更新）
            if (e.PositionChanged)
            {
                _offsetX = (int)e.NewPosition.X;
                _offsetY = (int)e.NewPosition.Y;
                
                // プロパティ変更通知
                this.RaisePropertyChanged(nameof(OffsetX));
                this.RaisePropertyChanged(nameof(OffsetY));
            }
            
            if (e.SizeChanged)
            {
                _width = (int)e.NewSize.Width;
                _height = (int)e.NewSize.Height;
                
                // プロパティ変更通知
                this.RaisePropertyChanged(nameof(Width));
                this.RaisePropertyChanged(nameof(Height));
            }
        }
        
        /// <summary>
        /// UI設定を位置管理システムに同期します
        /// </summary>
        private void SyncToPositionManager()
        {
            if (_positionManager == null) return;
            
            try
            {
                // 現在のUI設定を位置管理システムに適用
                var positionMode = Position switch
                {
                    "上" or "下" or "左" or "右" or "中央" => OverlayPositionMode.OcrRegionBased,
                    "カスタム" => OverlayPositionMode.Fixed,
                    _ => OverlayPositionMode.OcrRegionBased
                };
                
                _positionManager.PositionMode = positionMode;
                _positionManager.SizeMode = OverlaySizeMode.ContentBased; // コンテンツベースをデフォルトに
                _positionManager.FixedPosition = new CorePoint(OffsetX, OffsetY);
                _positionManager.FixedSize = new CoreSize(Width, Height);
                _positionManager.PositionOffset = CoreVector.Zero;
                _positionManager.MaxSize = new CoreSize(1200, 800);
                _positionManager.MinSize = new CoreSize(200, 60);
                
                Logger?.LogDebug("位置管理システムに設定を同期しました: Mode={Mode}", positionMode);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogError(ex, "位置管理システムへの設定同期中に無効な操作エラーが発生しました");
            }
            catch (ArgumentException ex)
            {
                Logger?.LogError(ex, "位置管理システムへの設定同期中に引数エラーが発生しました");
            }
        }
        
        /// <summary>
        /// 位置設定を位置管理システムに同期します
        /// </summary>
        private void SyncPositionToManager()
        {
            if (_positionManager == null) return;
            
            try
            {
                var positionMode = Position switch
                {
                    "上" or "下" or "左" or "右" or "中央" => OverlayPositionMode.OcrRegionBased,
                    "カスタム" => OverlayPositionMode.Fixed,
                    _ => OverlayPositionMode.OcrRegionBased
                };
                
                _positionManager.PositionMode = positionMode;
                _positionManager.FixedPosition = new CorePoint(OffsetX, OffsetY);
                
                // オフセット調整（"上"、"下"等に応じて）
                var offset = Position switch
                {
                    "上" => new CoreVector(0, -10),
                    "下" => new CoreVector(0, 5),
                    "左" => new CoreVector(-10, 0),
                    "右" => new CoreVector(5, 0),
                    _ => CoreVector.Zero
                };
                
                _positionManager.PositionOffset = offset;
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogError(ex, "位置設定同期中に無効な操作エラーが発生しました");
            }
            catch (ArgumentException ex)
            {
                Logger?.LogError(ex, "位置設定同期中に引数エラーが発生しました");
            }
        }
        
        /// <summary>
        /// サイズ設定を位置管理システムに同期します
        /// </summary>
        private void SyncSizeToManager()
        {
            if (_positionManager == null) return;
            
            try
            {
                _positionManager.FixedSize = new CoreSize(Width, Height);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogError(ex, "サイズ設定同期中に無効な操作エラーが発生しました");
            }
            catch (ArgumentException ex)
            {
                Logger?.LogError(ex, "サイズ設定同期中に引数エラーが発生しました");
            }
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
                
                // 位置管理システムとの連携テスト
                if (_positionManager != null)
                {
                    try
                    {
                        await _positionManager.ApplyPositionAndSizeAsync(_previewOverlay).ConfigureAwait(false);
                        Logger?.LogDebug("位置管理システムによるプレビュー位置調整が完了しました");
                    }
                    catch (InvalidOperationException ex)
                    {
                        Logger?.LogWarning(ex, "位置管理システムによるプレビュー調整中に無効な操作エラーが発生しました。手動設定を使用します");
                    }
                    catch (ArgumentException ex)
                    {
                        Logger?.LogWarning(ex, "位置管理システムによるプレビュー調整中に引数エラーが発生しました。手動設定を使用します");
                    }
                    catch (OperationCanceledException)
                    {
                        // キャンセル例外は再スロー
                        throw;
                    }
                }
                
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
            catch (ArgumentException ex)
            {
                Logger?.LogError(ex, "プレビューオーバーレイの表示中に引数エラーが発生しました。");
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
            OffsetX = 0;
            OffsetY = 0;
            
            // 位置管理システムの設定もリセット
            SyncToPositionManager();
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        /// <summary>
        /// リソースを解放します
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 位置管理システムのイベント購読解除
                if (_positionManager != null)
                {
                    _positionManager.PositionUpdated -= OnPositionUpdated;
                }
                
                // プレビューオーバーレイの清理
                _previewOverlay?.Dispose();
                _previewOverlay = null;
            }
            
            base.Dispose(disposing);
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
