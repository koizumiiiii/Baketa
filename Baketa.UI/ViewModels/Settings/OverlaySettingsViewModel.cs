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
/// オーバーレイ設定画面のViewModel
/// 翻訳結果オーバーレイ表示の設定を管理
/// </summary>
public sealed class OverlaySettingsViewModel : Framework.ViewModelBase
{
    private readonly OverlaySettings _originalSettings;
    private readonly ILogger<OverlaySettingsViewModel>? _logger;
    
    // バッキングフィールド - 基本設定
    private bool _isEnabled;
    private double _opacity;
    private int _fontSize;
    private uint _backgroundColor;
    private uint _textColor;
    
    // バッキングフィールド - 自動非表示設定
    private bool _enableAutoHideForAutoTranslation;
    private int _autoHideDelayForAutoTranslation;
    private bool _enableAutoHideForSingleShot;
    private int _autoHideDelayForSingleShot;
    
    // バッキングフィールド - 詳細設定
    private int _maxWidth;
    private int _maxHeight;
    private bool _enableTextTruncation;
    private bool _allowManualClose;
    private bool _enableClickThrough;
    private int _fadeOutDurationMs;
    
    // バッキングフィールド - 位置設定
    private OverlayPositionMode _positionMode;
    private int _fixedPositionX;
    private int _fixedPositionY;
    
    // バッキングフィールド - 外観設定
    private bool _showBorder;
    private uint _borderColor;
    private int _borderThickness;
    private int _cornerRadius;
    
    // バッキングフィールド - デバッグ設定
    private bool _showDebugBounds;
    
    // UI制御フィールド
    private bool _showAdvancedSettings;
    private bool _hasChanges;

    /// <summary>
    /// OverlaySettingsViewModelを初期化します
    /// </summary>
    /// <param name="settings">オーバーレイ設定データ</param>
    /// <param name="eventAggregator">イベント集約器</param>
    /// <param name="logger">ロガー（オプション）</param>
    public OverlaySettingsViewModel(
        OverlaySettings settings,
        IEventAggregator eventAggregator,
        ILogger<OverlaySettingsViewModel>? logger = null) : base(eventAggregator)
    {
        _originalSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;

        // 初期化
        InitializeFromSettings(settings);

        // 変更追跡の設定
        SetupChangeTracking();

        // 選択肢の初期化
        PositionModeOptions = [.. Enum.GetValues<OverlayPositionMode>()];
        FontSizeOptions = [8, 10, 12, 14, 16, 18, 20, 24, 28, 32, 36, 48];
        BorderThicknessOptions = [1, 2, 3, 4, 5];

        // コマンドの初期化
        ResetToDefaultsCommand = ReactiveCommand.Create(ResetToDefaults);
        ToggleAdvancedSettingsCommand = ReactiveCommand.Create(ToggleAdvancedSettings);
        PreviewOverlayCommand = ReactiveCommand.Create(PreviewOverlay);
        ChooseBackgroundColorCommand = ReactiveCommand.Create(ChooseBackgroundColor);
        ChooseTextColorCommand = ReactiveCommand.Create(ChooseTextColor);
        ChooseBorderColorCommand = ReactiveCommand.Create(ChooseBorderColor);
    }

    #region 基本設定プロパティ

    /// <summary>
    /// オーバーレイ表示を有効にするか
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => this.RaiseAndSetIfChanged(ref _isEnabled, value);
    }

    /// <summary>
    /// オーバーレイの透明度（0.0-1.0）
    /// </summary>
    public double Opacity
    {
        get => _opacity;
        set => this.RaiseAndSetIfChanged(ref _opacity, value);
    }

    /// <summary>
    /// オーバーレイのフォントサイズ
    /// </summary>
    public int FontSize
    {
        get => _fontSize;
        set => this.RaiseAndSetIfChanged(ref _fontSize, value);
    }

    /// <summary>
    /// オーバーレイの背景色（ARGB形式）
    /// </summary>
    public uint BackgroundColor
    {
        get => _backgroundColor;
        set => this.RaiseAndSetIfChanged(ref _backgroundColor, value);
    }

    /// <summary>
    /// オーバーレイのテキスト色（ARGB形式）
    /// </summary>
    public uint TextColor
    {
        get => _textColor;
        set => this.RaiseAndSetIfChanged(ref _textColor, value);
    }

    #endregion

    #region 自動非表示設定プロパティ

    /// <summary>
    /// 自動翻訳での自動非表示を有効化
    /// </summary>
    public bool EnableAutoHideForAutoTranslation
    {
        get => _enableAutoHideForAutoTranslation;
        set => this.RaiseAndSetIfChanged(ref _enableAutoHideForAutoTranslation, value);
    }

    /// <summary>
    /// 自動翻訳での自動非表示までの時間（秒）
    /// </summary>
    public int AutoHideDelayForAutoTranslation
    {
        get => _autoHideDelayForAutoTranslation;
        set => this.RaiseAndSetIfChanged(ref _autoHideDelayForAutoTranslation, value);
    }

    /// <summary>
    /// 単発翻訳での自動非表示を有効化（常にtrue）
    /// </summary>
    public bool EnableAutoHideForSingleShot
    {
        get => _enableAutoHideForSingleShot;
        set => this.RaiseAndSetIfChanged(ref _enableAutoHideForSingleShot, value);
    }

    /// <summary>
    /// 単発翻訳での自動非表示までの時間（秒）
    /// </summary>
    public int AutoHideDelayForSingleShot
    {
        get => _autoHideDelayForSingleShot;
        set => this.RaiseAndSetIfChanged(ref _autoHideDelayForSingleShot, value);
    }

    #endregion

    #region 詳細設定プロパティ

    /// <summary>
    /// オーバーレイの最大幅（ピクセル、0で制限なし）
    /// </summary>
    public int MaxWidth
    {
        get => _maxWidth;
        set => this.RaiseAndSetIfChanged(ref _maxWidth, value);
    }

    /// <summary>
    /// オーバーレイの最大高さ（ピクセル、0で制限なし）
    /// </summary>
    public int MaxHeight
    {
        get => _maxHeight;
        set => this.RaiseAndSetIfChanged(ref _maxHeight, value);
    }

    /// <summary>
    /// テキストが長い場合の省略表示
    /// </summary>
    public bool EnableTextTruncation
    {
        get => _enableTextTruncation;
        set => this.RaiseAndSetIfChanged(ref _enableTextTruncation, value);
    }

    /// <summary>
    /// マウスクリックでオーバーレイを手動で閉じることを許可
    /// </summary>
    public bool AllowManualClose
    {
        get => _allowManualClose;
        set => this.RaiseAndSetIfChanged(ref _allowManualClose, value);
    }

    /// <summary>
    /// クリックスルー機能の有効化
    /// </summary>
    public bool EnableClickThrough
    {
        get => _enableClickThrough;
        set => this.RaiseAndSetIfChanged(ref _enableClickThrough, value);
    }

    /// <summary>
    /// 翻訳結果のフェードアウトアニメーション時間（ミリ秒）
    /// </summary>
    public int FadeOutDurationMs
    {
        get => _fadeOutDurationMs;
        set => this.RaiseAndSetIfChanged(ref _fadeOutDurationMs, value);
    }

    #endregion

    #region 位置設定プロパティ

    /// <summary>
    /// オーバーレイの表示位置モード
    /// </summary>
    public OverlayPositionMode PositionMode
    {
        get => _positionMode;
        set => this.RaiseAndSetIfChanged(ref _positionMode, value);
    }

    /// <summary>
    /// 固定位置表示時のX座標
    /// </summary>
    public int FixedPositionX
    {
        get => _fixedPositionX;
        set => this.RaiseAndSetIfChanged(ref _fixedPositionX, value);
    }

    /// <summary>
    /// 固定位置表示時のY座標
    /// </summary>
    public int FixedPositionY
    {
        get => _fixedPositionY;
        set => this.RaiseAndSetIfChanged(ref _fixedPositionY, value);
    }

    /// <summary>
    /// 固定位置設定が有効かどうか
    /// </summary>
    public bool IsFixedPositionEnabled => PositionMode == OverlayPositionMode.Fixed;

    #endregion

    #region 外観設定プロパティ

    /// <summary>
    /// オーバーレイの境界線を表示するか
    /// </summary>
    public bool ShowBorder
    {
        get => _showBorder;
        set => this.RaiseAndSetIfChanged(ref _showBorder, value);
    }

    /// <summary>
    /// 境界線の色（ARGB形式）
    /// </summary>
    public uint BorderColor
    {
        get => _borderColor;
        set => this.RaiseAndSetIfChanged(ref _borderColor, value);
    }

    /// <summary>
    /// 境界線の太さ（ピクセル）
    /// </summary>
    public int BorderThickness
    {
        get => _borderThickness;
        set => this.RaiseAndSetIfChanged(ref _borderThickness, value);
    }

    /// <summary>
    /// オーバーレイの角丸半径（ピクセル）
    /// </summary>
    public int CornerRadius
    {
        get => _cornerRadius;
        set => this.RaiseAndSetIfChanged(ref _cornerRadius, value);
    }

    #endregion

    #region デバッグ設定プロパティ

    /// <summary>
    /// デバッグ用のオーバーレイ境界表示
    /// </summary>
    public bool ShowDebugBounds
    {
        get => _showDebugBounds;
        set => this.RaiseAndSetIfChanged(ref _showDebugBounds, value);
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
    /// 位置モードの選択肢
    /// </summary>
    public IReadOnlyList<OverlayPositionMode> PositionModeOptions { get; }

    /// <summary>
    /// フォントサイズの選択肢
    /// </summary>
    public IReadOnlyList<int> FontSizeOptions { get; }

    /// <summary>
    /// 境界線太さの選択肢
    /// </summary>
    public IReadOnlyList<int> BorderThicknessOptions { get; }

    /// <summary>
    /// 透明度のパーセンテージ表示用
    /// </summary>
    public string OpacityPercentage => $"{Opacity:P0}";

    /// <summary>
    /// 背景色のプレビュー用
    /// </summary>
    public string BackgroundColorHex => $"#{BackgroundColor:X8}";

    /// <summary>
    /// テキスト色のプレビュー用
    /// </summary>
    public string TextColorHex => $"#{TextColor:X8}";

    /// <summary>
    /// 境界線色のプレビュー用
    /// </summary>
    public string BorderColorHex => $"#{BorderColor:X8}";

    /// <summary>
    /// フェードアウト時間の表示用
    /// </summary>
    public string FadeOutDurationText => $"{FadeOutDurationMs}ms ({FadeOutDurationMs / 1000.0:F1}秒)";

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

    /// <summary>
    /// オーバーレイプレビューコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> PreviewOverlayCommand { get; }

    /// <summary>
    /// 背景色選択コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ChooseBackgroundColorCommand { get; }

    /// <summary>
    /// テキスト色選択コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ChooseTextColorCommand { get; }

    /// <summary>
    /// 境界線色選択コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ChooseBorderColorCommand { get; }

    #endregion

    #region メソッド

    /// <summary>
    /// 設定データから初期化
    /// </summary>
    private void InitializeFromSettings(OverlaySettings settings)
    {
        _isEnabled = settings.IsEnabled;
        _opacity = settings.Opacity;
        _fontSize = settings.FontSize;
        _backgroundColor = settings.BackgroundColor;
        _textColor = settings.TextColor;
        _enableAutoHideForAutoTranslation = settings.EnableAutoHideForAutoTranslation;
        _autoHideDelayForAutoTranslation = settings.AutoHideDelayForAutoTranslation;
        _enableAutoHideForSingleShot = settings.EnableAutoHideForSingleShot;
        _autoHideDelayForSingleShot = settings.AutoHideDelayForSingleShot;
        _maxWidth = settings.MaxWidth;
        _maxHeight = settings.MaxHeight;
        _enableTextTruncation = settings.EnableTextTruncation;
        _allowManualClose = settings.AllowManualClose;
        _enableClickThrough = settings.EnableClickThrough;
        _fadeOutDurationMs = settings.FadeOutDurationMs;
        _positionMode = settings.PositionMode;
        _fixedPositionX = settings.FixedPositionX;
        _fixedPositionY = settings.FixedPositionY;
        _showBorder = settings.ShowBorder;
        _borderColor = settings.BorderColor;
        _borderThickness = settings.BorderThickness;
        _cornerRadius = settings.CornerRadius;
        _showDebugBounds = settings.ShowDebugBounds;
    }

    /// <summary>
    /// 変更追跡を設定
    /// </summary>
    private void SetupChangeTracking()
    {
        // 基本設定プロパティの変更追跡
        this.WhenAnyValue(x => x.IsEnabled, x => x.Opacity, x => x.FontSize, 
                          x => x.BackgroundColor, x => x.TextColor)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        // 自動非表示設定の変更追跡
        this.WhenAnyValue(x => x.EnableAutoHideForAutoTranslation, x => x.AutoHideDelayForAutoTranslation,
                          x => x.EnableAutoHideForSingleShot, x => x.AutoHideDelayForSingleShot)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        // 詳細設定プロパティの変更追跡
        this.WhenAnyValue(x => x.MaxWidth, x => x.MaxHeight, x => x.EnableTextTruncation,
                          x => x.AllowManualClose, x => x.EnableClickThrough, x => x.FadeOutDurationMs)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        // 位置設定プロパティの変更追跡
        this.WhenAnyValue(x => x.PositionMode)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => {
                HasChanges = true;
                this.RaisePropertyChanged(nameof(IsFixedPositionEnabled));
            });
        
        this.WhenAnyValue(x => x.FixedPositionX, x => x.FixedPositionY)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        // 外観設定プロパティの変更追跡
        this.WhenAnyValue(x => x.ShowBorder, x => x.BorderColor, 
                          x => x.BorderThickness, x => x.CornerRadius)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        // デバッグ設定の変更追跡
        this.WhenAnyValue(x => x.ShowDebugBounds)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
    }

    /// <summary>
    /// デフォルト値にリセット
    /// </summary>
    private void ResetToDefaults()
    {
        var defaultSettings = new OverlaySettings();
        InitializeFromSettings(defaultSettings);
        HasChanges = true;
        _logger?.LogInformation("オーバーレイ設定をデフォルト値にリセットしました");
    }

    /// <summary>
    /// 詳細設定表示を切り替え
    /// </summary>
    private void ToggleAdvancedSettings()
    {
        ShowAdvancedSettings = !ShowAdvancedSettings;
        _logger?.LogDebug("詳細設定表示を切り替えました: {ShowAdvanced}", ShowAdvancedSettings);
    }

    /// <summary>
    /// オーバーレイのプレビュー表示
    /// </summary>
    private void PreviewOverlay()
    {
        // TODO: オーバーレイのプレビュー機能を実装
        _logger?.LogInformation("オーバーレイプレビューを表示しました");
    }

    /// <summary>
    /// 背景色を選択
    /// </summary>
    private void ChooseBackgroundColor()
    {
        // TODO: カラーピッカーダイアログを開く実装
        _logger?.LogInformation("背景色選択ダイアログを開きます");
    }

    /// <summary>
    /// テキスト色を選択
    /// </summary>
    private void ChooseTextColor()
    {
        // TODO: カラーピッカーダイアログを開く実装
        _logger?.LogInformation("テキスト色選択ダイアログを開きます");
    }

    /// <summary>
    /// 境界線色を選択
    /// </summary>
    private void ChooseBorderColor()
    {
        // TODO: カラーピッカーダイアログを開く実装
        _logger?.LogInformation("境界線色選択ダイアログを開きます");
    }

    /// <summary>
    /// 設定を検証
    /// </summary>
    public bool ValidateSettings()
    {
        try
        {
            // 透明度の検証
            if (Opacity < 0.1 || Opacity > 1.0)
            {
                _logger?.LogWarning("透明度が範囲外です: {Opacity}", Opacity);
                return false;
            }
            
            // フォントサイズの検証
            if (FontSize < 8 || FontSize > 48)
            {
                _logger?.LogWarning("フォントサイズが範囲外です: {FontSize}", FontSize);
                return false;
            }
            
            // 自動非表示時間の検証
            if (AutoHideDelayForAutoTranslation < 2 || AutoHideDelayForAutoTranslation > 30)
            {
                _logger?.LogWarning("自動翻訳の自動非表示時間が範囲外です: {Delay}秒", AutoHideDelayForAutoTranslation);
                return false;
            }
            
            if (AutoHideDelayForSingleShot < 3 || AutoHideDelayForSingleShot > 60)
            {
                _logger?.LogWarning("単発翻訳の自動非表示時間が範囲外です: {Delay}秒", AutoHideDelayForSingleShot);
                return false;
            }
            
            return true;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger?.LogError(ex, "設定値が範囲外です");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "設定の組み合わせが無効です");
            return false;
        }
    }

    /// <summary>
    /// 現在の設定データを取得
    /// </summary>
    public OverlaySettings CurrentSettings => new()
    {
        IsEnabled = IsEnabled,
        Opacity = Opacity,
        FontSize = FontSize,
        BackgroundColor = BackgroundColor,
        TextColor = TextColor,
        EnableAutoHideForAutoTranslation = EnableAutoHideForAutoTranslation,
        AutoHideDelayForAutoTranslation = AutoHideDelayForAutoTranslation,
        EnableAutoHideForSingleShot = EnableAutoHideForSingleShot,
        AutoHideDelayForSingleShot = AutoHideDelayForSingleShot,
        MaxWidth = MaxWidth,
        MaxHeight = MaxHeight,
        EnableTextTruncation = EnableTextTruncation,
        AllowManualClose = AllowManualClose,
        EnableClickThrough = EnableClickThrough,
        FadeOutDurationMs = FadeOutDurationMs,
        PositionMode = PositionMode,
        FixedPositionX = FixedPositionX,
        FixedPositionY = FixedPositionY,
        ShowBorder = ShowBorder,
        BorderColor = BorderColor,
        BorderThickness = BorderThickness,
        CornerRadius = CornerRadius,
        ShowDebugBounds = ShowDebugBounds
    };

    /// <summary>
    /// 設定データを更新
    /// </summary>
    /// <param name="settings">新しい設定データ</param>
    public void UpdateSettings(OverlaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        
        InitializeFromSettings(settings);
        HasChanges = false;
        _logger?.LogDebug("オーバーレイ設定を更新しました");
    }

    #endregion
}
