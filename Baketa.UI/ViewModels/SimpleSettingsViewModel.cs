using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Framework;
using Baketa.UI.Framework.Events;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.ViewModels;

/// <summary>
/// シンプル設定画面のViewModel
/// αテスト向け基本設定のみ - タブなしのシンプル版
/// </summary>
public class SimpleSettingsViewModel : ViewModelBase
{
    private bool _useLocalEngine = true;
    private string _sourceLanguage = "Japanese";
    private string _targetLanguage = "English";
    private int _fontSize = 14;
    private double _overlayOpacity = 0.9;
    private bool _hasChanges;

    public SimpleSettingsViewModel(
        IEventAggregator eventAggregator,
        ILogger<SimpleSettingsViewModel> logger)
        : base(eventAggregator, logger)
    {
        InitializeCommands();
        InitializeCollections();
    }

    #region Properties

    /// <summary>
    /// ローカル翻訳エンジンを使用するか
    /// </summary>
    public bool UseLocalEngine
    {
        get => _useLocalEngine;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _useLocalEngine, value);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でUseLocalEngine設定失敗 - 直接設定で続行");
                _useLocalEngine = value;
            }
        }
    }

    /// <summary>
    /// 翻訳元言語
    /// </summary>
    public string SourceLanguage
    {
        get => _sourceLanguage;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _sourceLanguage, value);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でSourceLanguage設定失敗 - 直接設定で続行");
                _sourceLanguage = value;
            }
        }
    }

    /// <summary>
    /// 翻訳先言語
    /// </summary>
    public string TargetLanguage
    {
        get => _targetLanguage;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _targetLanguage, value);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でTargetLanguage設定失敗 - 直接設定で続行");
                _targetLanguage = value;
            }
        }
    }

    /// <summary>
    /// フォントサイズ
    /// </summary>
    public int FontSize
    {
        get => _fontSize;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _fontSize, value);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でFontSize設定失敗 - 直接設定で続行");
                _fontSize = value;
            }
        }
    }

    /// <summary>
    /// オーバーレイ透明度
    /// </summary>
    public double OverlayOpacity
    {
        get => _overlayOpacity;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _overlayOpacity, value);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でOverlayOpacity設定失敗 - 直接設定で続行");
                _overlayOpacity = value;
            }
        }
    }

    /// <summary>
    /// 変更があるかどうか
    /// </summary>
    public bool HasChanges
    {
        get => _hasChanges;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _hasChanges, value);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でHasChanges設定失敗 - 直接設定で続行");
                _hasChanges = value;
            }
        }
    }

    /// <summary>
    /// 利用可能な言語リスト
    /// </summary>
    public ObservableCollection<string> AvailableLanguages { get; } = [];

    /// <summary>
    /// フォントサイズ選択肢
    /// </summary>
    public ObservableCollection<int> FontSizeOptions { get; } = [];

    #endregion

    #region Commands

    public ReactiveCommand<Unit, Unit> ApplyCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> CancelCommand { get; private set; } = null!;

    #endregion

    #region Initialization

    private void InitializeCommands()
    {
        // コマンドをUIスレッドで安全に初期化
        try
        {
            ApplyCommand = ReactiveCommand.CreateFromTask(ExecuteApplyAsync,
                this.WhenAnyValue(x => x.HasChanges).ObserveOn(RxApp.MainThreadScheduler),
                outputScheduler: RxApp.MainThreadScheduler);
            CancelCommand = ReactiveCommand.CreateFromTask(ExecuteCancelAsync,
                outputScheduler: RxApp.MainThreadScheduler);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "SimpleSettingsViewModelのReactiveCommand初期化エラー");
            throw;
        }
    }

    private void InitializeCollections()
    {
        // 言語選択肢（αテスト版は限定）
        AvailableLanguages.Add("Japanese");
        AvailableLanguages.Add("English");

        // フォントサイズ選択肢
        FontSizeOptions.Add(10);
        FontSizeOptions.Add(12);
        FontSizeOptions.Add(14);
        FontSizeOptions.Add(16);
        FontSizeOptions.Add(18);
        FontSizeOptions.Add(20);

        // プロパティ変更監視（UIスレッドで安全に処理）
        try
        {
            this.WhenAnyValue(
                    x => x.UseLocalEngine,
                    x => x.SourceLanguage,
                    x => x.TargetLanguage,
                    x => x.FontSize,
                    x => x.OverlayOpacity)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => 
                {
                    try
                    {
                        HasChanges = true;
                    }
                    catch (InvalidOperationException ex)
                    {
                        Logger?.LogWarning(ex, "UIスレッド違反でHasChanges設定失敗 - 続行");
                    }
                });
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "SimpleSettingsViewModelのプロパティ監視初期化エラー");
            throw;
        }
    }

    #endregion

    #region Command Handlers

    private async Task ExecuteApplyAsync()
    {
        try
        {
            Logger?.LogInformation("Applying settings changes");

            // 設定適用イベントを発行
            var settingsEvent = new SettingsChangedEvent
            {
                UseLocalEngine = UseLocalEngine,
                SourceLanguage = SourceLanguage,
                TargetLanguage = TargetLanguage,
                FontSize = FontSize,
                OverlayOpacity = OverlayOpacity
            };

            await PublishEventAsync(settingsEvent).ConfigureAwait(false);

            HasChanges = false;
            Logger?.LogInformation("Settings applied successfully");

            // 設定画面を閉じる
            CloseRequested?.Invoke();
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to apply settings");
        }
    }

    private async Task ExecuteCancelAsync()
    {
        try
        {
            Logger?.LogDebug("Settings changes cancelled");

            // 設定画面を閉じる
            CloseRequested?.Invoke();
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to cancel settings");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }


    #endregion

    #region Events

    /// <summary>
    /// ウィンドウを閉じる要求イベント
    /// </summary>
    public event Action? CloseRequested;

    #endregion

    #region Public Methods

    /// <summary>
    /// 設定を読み込み
    /// </summary>
    public async Task LoadSettingsAsync()
    {
        try
        {
            Logger?.LogDebug("Loading current settings");

            // 設定読み込み要求イベントを発行
            var loadEvent = new LoadSettingsRequestEvent();
            await PublishEventAsync(loadEvent).ConfigureAwait(false);

            // 実際の設定読み込みはイベントハンドラーで処理
            HasChanges = false;
            Logger?.LogDebug("Settings loaded successfully");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to load settings");
        }
    }

    /// <summary>
    /// 外部から設定値を更新
    /// </summary>
    public void UpdateSettings(bool useLocalEngine, string sourceLanguage, string targetLanguage, int fontSize, double overlayOpacity)
    {
        UseLocalEngine = useLocalEngine;
        SourceLanguage = sourceLanguage;
        TargetLanguage = targetLanguage;
        FontSize = fontSize;
        OverlayOpacity = overlayOpacity;
        HasChanges = false;
    }

    #endregion
}