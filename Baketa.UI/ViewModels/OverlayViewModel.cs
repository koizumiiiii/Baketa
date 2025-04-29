using System;
using System.Collections.Generic;
using System.Reactive;
using System.Threading.Tasks;
using Baketa.UI.Framework;
using Baketa.UI.Framework.Events;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.ViewModels
{
    /// <summary>
    /// オーバーレイ設定画面のビューモデル
    /// </summary>
    internal class OverlayViewModel : Framework.ViewModelBase
    {
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
        /// <param name="logger">ロガー</param>
        public OverlayViewModel(IEventAggregator eventAggregator, ILogger? logger = null)
            : base(eventAggregator, logger)
        {
            // コマンドの初期化
            SaveSettingsCommand = ReactiveCommandFactory.Create(ExecuteSaveSettingsAsync);
            PreviewOverlayCommand = ReactiveCommandFactory.Create(ExecutePreviewOverlayAsync);
            ResetToDefaultsCommand = ReactiveCommandFactory.Create(ExecuteResetToDefaultsAsync);
            ResetSettingsCommand = ReactiveCommandFactory.Create(ExecuteResetToDefaultsAsync); // CS8618対応
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
                BackgroundOpacity / 100.0)).ConfigureAwait(true);
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // プレビューコマンド実行
        private async Task ExecutePreviewOverlayAsync()
        {
            // 実際のプレビュー表示処理
            // オーバーレイウィンドウを一時的に表示する処理
            
            await Task.CompletedTask.ConfigureAwait(true);
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
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
    }
    
    // イベント定義
    internal class OverlaySettingsChangedEvent(
        string position, 
        int fontSize, 
        string fontColor, 
        string backgroundColor, 
        double backgroundOpacity) : IEvent
    {
        public string Position { get; } = position;
        public int FontSize { get; } = fontSize;
        public string FontColor { get; } = fontColor;
        public string BackgroundColor { get; } = backgroundColor;
        public double BackgroundOpacity { get; } = backgroundOpacity;
    }
}