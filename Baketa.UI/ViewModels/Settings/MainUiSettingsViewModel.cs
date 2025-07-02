using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Baketa.Core.Settings;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Framework;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.ViewModels.Settings;

/// <summary>
/// メイン操作UI設定画面のViewModel
/// プログレッシブディスクロージャーによる基本/詳細設定の階層表示をサポート
/// </summary>
public sealed class MainUiSettingsViewModel : Framework.ViewModelBase
{
    private readonly MainUiSettings _originalSettings;
    private readonly ILogger<MainUiSettingsViewModel>? _logger;
    
    // バッキングフィールド
    private double _panelOpacity;
    private bool _autoHideWhenIdle;
    private int _autoHideDelaySeconds;
    private bool _highlightOnHover;
    private UiSize _panelSize;
    private bool _alwaysOnTop;
    private int _singleShotDisplayTime;
    private bool _enableDragging;
    private bool _enableBoundarySnap;
    private int _boundarySnapDistance;
    private bool _enableAnimations;
    private int _animationDurationMs;
    private UiTheme _themeStyle;
    private bool _showDebugInfo;
    private bool _showFrameRate;
    private bool _showAdvancedSettings;
    private bool _hasChanges;

    /// <summary>
    /// MainUiSettingsViewModelを初期化します
    /// </summary>
    /// <param name="settings">メインUI設定データ</param>
    /// <param name="eventAggregator">イベント集約器</param>
    /// <param name="logger">ロガー（オプション）</param>
    public MainUiSettingsViewModel(
        MainUiSettings settings,
        IEventAggregator eventAggregator,
        ILogger<MainUiSettingsViewModel>? logger = null) : base(eventAggregator)
    {
        _originalSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;

        // バッキングフィールドの初期化
        _panelOpacity = settings.PanelOpacity;
        _autoHideWhenIdle = settings.AutoHideWhenIdle;
        _autoHideDelaySeconds = settings.AutoHideDelaySeconds;
        _highlightOnHover = settings.HighlightOnHover;
        _panelSize = settings.PanelSize;
        _alwaysOnTop = settings.AlwaysOnTop;
        _singleShotDisplayTime = settings.SingleShotDisplayTime;
        _enableDragging = settings.EnableDragging;
        _enableBoundarySnap = settings.EnableBoundarySnap;
        _boundarySnapDistance = settings.BoundarySnapDistance;
        _enableAnimations = settings.EnableAnimations;
        _animationDurationMs = settings.AnimationDurationMs;
        _themeStyle = settings.ThemeStyle;
        _showDebugInfo = settings.ShowDebugInfo;
        _showFrameRate = settings.ShowFrameRate;

        // ReactiveUI でのプロパティ変更追跡
        // 主要プロパティの変更時にHasChangesをtrueに設定
        this.WhenAnyValue(x => x.PanelOpacity)
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        this.WhenAnyValue(x => x.AutoHideWhenIdle)
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        this.WhenAnyValue(x => x.AutoHideDelaySeconds)
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        this.WhenAnyValue(x => x.PanelSize)
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        this.WhenAnyValue(x => x.ThemeStyle)
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        this.WhenAnyValue(x => x.AlwaysOnTop)
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        this.WhenAnyValue(x => x.EnableDragging)
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        this.WhenAnyValue(x => x.EnableAnimations)
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        // パネルサイズの選択肢
        PanelSizes = [.. Enum.GetValues<UiSize>()];

        // テーマの選択肢  
        ThemeOptions = [.. Enum.GetValues<UiTheme>()];

        // コマンドの初期化
        ResetToDefaultsCommand = ReactiveCommand.Create(ResetToDefaults);
        ToggleAdvancedSettingsCommand = ReactiveCommand.Create(ToggleAdvancedSettings);
    }

    #region 基本設定プロパティ

    /// <summary>
    /// 翻訳パネルの透明度（0.1-1.0）
    /// </summary>
    public double PanelOpacity
    {
        get => _panelOpacity;
        set => this.RaiseAndSetIfChanged(ref _panelOpacity, value);
    }

    /// <summary>
    /// 未使用時の自動非表示機能を使用するか
    /// </summary>
    public bool AutoHideWhenIdle
    {
        get => _autoHideWhenIdle;
        set => this.RaiseAndSetIfChanged(ref _autoHideWhenIdle, value);
    }

    /// <summary>
    /// 自動非表示までの時間（秒）
    /// </summary>
    public int AutoHideDelaySeconds
    {
        get => _autoHideDelaySeconds;
        set => this.RaiseAndSetIfChanged(ref _autoHideDelaySeconds, value);
    }

    /// <summary>
    /// マウスホバー時の表示強調を行うか
    /// </summary>
    public bool HighlightOnHover
    {
        get => _highlightOnHover;
        set => this.RaiseAndSetIfChanged(ref _highlightOnHover, value);
    }

    /// <summary>
    /// 翻訳パネルのサイズ（小・中・大）
    /// </summary>
    public UiSize PanelSize
    {
        get => _panelSize;
        set => this.RaiseAndSetIfChanged(ref _panelSize, value);
    }

    /// <summary>
    /// 常に最前面に表示するか
    /// </summary>
    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set => this.RaiseAndSetIfChanged(ref _alwaysOnTop, value);
    }

    /// <summary>
    /// 単発翻訳結果の表示時間（秒）
    /// </summary>
    public int SingleShotDisplayTime
    {
        get => _singleShotDisplayTime;
        set => this.RaiseAndSetIfChanged(ref _singleShotDisplayTime, value);
    }

    /// <summary>
    /// 翻訳パネルをドラッグで移動可能にするか
    /// </summary>
    public bool EnableDragging
    {
        get => _enableDragging;
        set => this.RaiseAndSetIfChanged(ref _enableDragging, value);
    }

    #endregion

    #region 詳細設定プロパティ

    /// <summary>
    /// 翻訳パネルの境界スナップ機能を使用するか
    /// </summary>
    public bool EnableBoundarySnap
    {
        get => _enableBoundarySnap;
        set => this.RaiseAndSetIfChanged(ref _enableBoundarySnap, value);
    }

    /// <summary>
    /// 境界スナップの距離（ピクセル）
    /// </summary>
    public int BoundarySnapDistance
    {
        get => _boundarySnapDistance;
        set => this.RaiseAndSetIfChanged(ref _boundarySnapDistance, value);
    }

    /// <summary>
    /// パネルアニメーション効果を有効にするか
    /// </summary>
    public bool EnableAnimations
    {
        get => _enableAnimations;
        set => this.RaiseAndSetIfChanged(ref _enableAnimations, value);
    }

    /// <summary>
    /// アニメーション持続時間（ミリ秒）
    /// </summary>
    public int AnimationDurationMs
    {
        get => _animationDurationMs;
        set => this.RaiseAndSetIfChanged(ref _animationDurationMs, value);
    }

    /// <summary>
    /// パネルのテーマスタイル
    /// </summary>
    public UiTheme ThemeStyle
    {
        get => _themeStyle;
        set => this.RaiseAndSetIfChanged(ref _themeStyle, value);
    }

    #endregion

    #region デバッグ設定プロパティ

    /// <summary>
    /// デバッグ情報表示の有効化
    /// </summary>
    public bool ShowDebugInfo
    {
        get => _showDebugInfo;
        set => this.RaiseAndSetIfChanged(ref _showDebugInfo, value);
    }

    /// <summary>
    /// フレームレート表示の有効化
    /// </summary>
    public bool ShowFrameRate
    {
        get => _showFrameRate;
        set => this.RaiseAndSetIfChanged(ref _showFrameRate, value);
    }

    #endregion

    #region UI制御プロパティ

    /// <summary>
    /// 詳細設定を表示するかどうか
    /// </summary>
    public bool ShowAdvancedSettings
    {
        get => _showAdvancedSettings;
        set => this.RaiseAndSetIfChanged(ref _showAdvancedSettings, value);
    }

    /// <summary>
    /// 設定に変更があるかどうか
    /// </summary>
    public bool HasChanges
    {
        get => _hasChanges;
        set => this.RaiseAndSetIfChanged(ref _hasChanges, value);
    }

    /// <summary>
    /// パネルサイズの選択肢
    /// </summary>
    public IReadOnlyList<UiSize> PanelSizes { get; }

    /// <summary>
    /// テーマオプションの選択肢
    /// </summary>
    public IReadOnlyList<UiTheme> ThemeOptions { get; }

    /// <summary>
    /// 透明度のパーセンテージ表示用
    /// </summary>
    public string PanelOpacityPercentage => $"{PanelOpacity:P0}";

    #endregion

    #region コマンド

    /// <summary>
    /// デフォルト値にリセットするコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ResetToDefaultsCommand { get; }

    /// <summary>
    /// 詳細設定表示切り替えコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ToggleAdvancedSettingsCommand { get; }

    #endregion

    #region メソッド

    /// <summary>
    /// 設定をデフォルト値にリセットします
    /// </summary>
    private void ResetToDefaults()
    {
        var defaultSettings = new MainUiSettings();
        
        PanelOpacity = defaultSettings.PanelOpacity;
        AutoHideWhenIdle = defaultSettings.AutoHideWhenIdle;
        AutoHideDelaySeconds = defaultSettings.AutoHideDelaySeconds;
        HighlightOnHover = defaultSettings.HighlightOnHover;
        PanelSize = defaultSettings.PanelSize;
        AlwaysOnTop = defaultSettings.AlwaysOnTop;
        SingleShotDisplayTime = defaultSettings.SingleShotDisplayTime;
        EnableDragging = defaultSettings.EnableDragging;
        EnableBoundarySnap = defaultSettings.EnableBoundarySnap;
        BoundarySnapDistance = defaultSettings.BoundarySnapDistance;
        EnableAnimations = defaultSettings.EnableAnimations;
        AnimationDurationMs = defaultSettings.AnimationDurationMs;
        ThemeStyle = defaultSettings.ThemeStyle;
        ShowDebugInfo = defaultSettings.ShowDebugInfo;
        ShowFrameRate = defaultSettings.ShowFrameRate;
        
        HasChanges = true;
    }

    /// <summary>
    /// 詳細設定表示を切り替えます
    /// </summary>
    private void ToggleAdvancedSettings()
    {
        ShowAdvancedSettings = !ShowAdvancedSettings;
    }

    /// <summary>
    /// 現在の設定データ
    /// </summary>
    public MainUiSettings CurrentSettings
    {
        get
        {
            return new MainUiSettings
            {
                PanelOpacity = PanelOpacity,
            AutoHideWhenIdle = AutoHideWhenIdle,
            AutoHideDelaySeconds = AutoHideDelaySeconds,
            HighlightOnHover = HighlightOnHover,
            PanelSize = PanelSize,
            AlwaysOnTop = AlwaysOnTop,
            SingleShotDisplayTime = SingleShotDisplayTime,
            EnableDragging = EnableDragging,
            EnableBoundarySnap = EnableBoundarySnap,
            BoundarySnapDistance = BoundarySnapDistance,
            EnableAnimations = EnableAnimations,
            AnimationDurationMs = AnimationDurationMs,
            ThemeStyle = ThemeStyle,
                ShowDebugInfo = ShowDebugInfo,
                ShowFrameRate = ShowFrameRate
            };
        }
    }

    /// <summary>
    /// 設定データを更新します
    /// </summary>
    /// <param name="settings">新しい設定データ</param>
    public void UpdateSettings(MainUiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        
        PanelOpacity = settings.PanelOpacity;
        AutoHideWhenIdle = settings.AutoHideWhenIdle;
        AutoHideDelaySeconds = settings.AutoHideDelaySeconds;
        HighlightOnHover = settings.HighlightOnHover;
        PanelSize = settings.PanelSize;
        AlwaysOnTop = settings.AlwaysOnTop;
        SingleShotDisplayTime = settings.SingleShotDisplayTime;
        EnableDragging = settings.EnableDragging;
        EnableBoundarySnap = settings.EnableBoundarySnap;
        BoundarySnapDistance = settings.BoundarySnapDistance;
        EnableAnimations = settings.EnableAnimations;
        AnimationDurationMs = settings.AnimationDurationMs;
        ThemeStyle = settings.ThemeStyle;
        ShowDebugInfo = settings.ShowDebugInfo;
        ShowFrameRate = settings.ShowFrameRate;
        
        HasChanges = false;
    }

    #endregion
}
