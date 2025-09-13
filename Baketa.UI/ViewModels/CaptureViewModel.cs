using System;
using System.Reactive;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Framework;
using Microsoft.Extensions.Logging;
using ReactiveUI;

using UIEvents = Baketa.UI.Framework.Events;

namespace Baketa.UI.ViewModels;

    /// <summary>
    /// キャプチャ設定画面のビューモデル
    /// </summary>
    public sealed class CaptureViewModel : Framework.ViewModelBase
    {
        // キャプチャ状態
        private bool _isCapturing;
        public bool IsCapturing
        {
            get => _isCapturing;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _isCapturing, value);
        }
        
        // 選択領域情報
        private string _selectedRegion = "領域が選択されていません";
        public string SelectedRegion
        {
            get => _selectedRegion;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _selectedRegion, value);
        }
        
        // OCR言語選択
        private string _sourceLanguage = "日本語";
        public string SourceLanguage
        {
            get => _sourceLanguage;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _sourceLanguage, value);
        }
        
        // OCR設定
        private bool _enablePreprocessing = true;
        public bool EnablePreprocessing
        {
            get => _enablePreprocessing;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _enablePreprocessing, value);
        }
        
        private int _captureInterval = 1000;
        public int CaptureInterval
        {
            get => _captureInterval;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _captureInterval, value);
        }
        
        private bool _useIntelligentCapture = true;
        public bool UseIntelligentCapture
        {
            get => _useIntelligentCapture;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _useIntelligentCapture, value);
        }
        
        // コマンド
        public ReactiveCommand<Unit, Unit> StartCaptureCommand { get; }
        public ReactiveCommand<Unit, Unit> StopCaptureCommand { get; }
        public ReactiveCommand<Unit, Unit> SelectRegionCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveSettingsCommand { get; }
        
        /// <summary>
        /// 新しいCaptureViewModelを初期化します
        /// </summary>
        /// <param name="eventAggregator">イベント集約器</param>
        /// <param name="logger">ロガー</param>
        public CaptureViewModel(IEventAggregator eventAggregator, ILogger? logger = null)
            : base(eventAggregator, logger)
        {
            // コマンドの実行可否条件
            var canStartCapture = this.WhenAnyValue<CaptureViewModel, bool, bool>(
                x => x.IsCapturing,
                isCapturing => !isCapturing);
                
            var canStopCapture = this.WhenAnyValue<CaptureViewModel, bool, bool>(
                x => x.IsCapturing,
                isCapturing => isCapturing);
            
            // コマンドの初期化
            StartCaptureCommand = CommandHelper.CreateCommand(ExecuteStartCaptureAsync, canStartCapture);
            StopCaptureCommand = CommandHelper.CreateCommand(ExecuteStopCaptureAsync, canStopCapture);
            SelectRegionCommand = CommandHelper.CreateCommand(ExecuteSelectRegionAsync);
            SaveSettingsCommand = CommandHelper.CreateCommand(ExecuteSaveSettingsAsync);
        }
        
        /// <summary>
        /// アクティベーション時の処理
        /// </summary>
        protected override void HandleActivation()
        {
            // イベント購読（実装予定）
            // SubscribeToEvent<CaptureStatusChangedEvent>(OnCaptureStatusChanged);
        }
        
        // キャプチャ開始コマンド実行
        private async Task ExecuteStartCaptureAsync()
        {
            Console.WriteLine("🚀 キャプチャ開始コマンドが実行されました");
            await PublishEventAsync(new UIEvents.StartCaptureRequestedEvent()).ConfigureAwait(false);
            IsCapturing = true;
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // キャプチャ停止コマンド実行
        private async Task ExecuteStopCaptureAsync()
        {
            Console.WriteLine("🛑 キャプチャ停止コマンドが実行されました");
            await PublishEventAsync(new UIEvents.StopCaptureRequestedEvent()).ConfigureAwait(false);
            IsCapturing = false;
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // 領域選択コマンド実行
        private async Task ExecuteSelectRegionAsync()
        {
            //_logger?.LogInformation("領域選択コマンドが実行されました");
            
            // 領域選択ロジック（実際にはダイアログなどを表示）
            SelectedRegion = "X: 100, Y: 100, 幅: 500, 高さ: 300";
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // 設定保存コマンド実行
        private async Task ExecuteSaveSettingsAsync()
        {
            //_logger?.LogInformation("キャプチャ設定保存コマンドが実行されました");
            
            // 設定保存ロジック
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // キャプチャステータス変更イベントハンドラ（実装予定）
        // private async Task OnCaptureStatusChanged(CaptureStatusChangedEvent eventData)
        // {
        //     IsCapturing = eventData.IsActive;
        //     await Task.CompletedTask.ConfigureAwait(false);
        // }
    }
