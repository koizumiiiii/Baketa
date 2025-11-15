using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Settings;
using Baketa.UI.Framework;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.ViewModels.Settings;

/// <summary>
/// テーマ設定画面のViewModel
/// アプリケーションの外観とテーマ設定を管理
/// </summary>
public sealed class ThemeSettingsViewModel : Framework.ViewModelBase
{
    private readonly ThemeSettings _originalSettings;
    private readonly ILogger<ThemeSettingsViewModel>? _logger;

    // バッキングフィールド
    private UiTheme _appTheme;
    private uint _accentColor;
    private string _fontFamily = "Yu Gothic UI"; // デフォルト値で初期化
    private int _baseFontSize;
    private bool _highContrastMode;
    private bool _enableDpiScaling;
    private double _customScaleFactor;
    private bool _enableAnimations;
    private AnimationSpeed _animationSpeed;
    private bool _roundedWindowCorners;
    private bool _enableBlurEffect;
    private bool _enableCustomCss;
    private string _customCssFilePath = string.Empty; // デフォルト値で初期化
    private bool _showAdvancedSettings;
    private bool _hasChanges;

    /// <summary>
    /// ThemeSettingsViewModelを初期化します
    /// </summary>
    /// <param name="settings">テーマ設定データ</param>
    /// <param name="eventAggregator">イベント集約器</param>
    /// <param name="logger">ロガー（オプション）</param>
    public ThemeSettingsViewModel(
        ThemeSettings settings,
        IEventAggregator eventAggregator,
        ILogger<ThemeSettingsViewModel>? logger = null) : base(eventAggregator)
    {
        _originalSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;

        // 初期化
        InitializeFromSettings(settings);

        // 変更追跡の設定
        SetupChangeTracking();

        // 選択肢の初期化
        ThemeOptions = [.. Enum.GetValues<UiTheme>()];
        AnimationSpeedOptions = [.. Enum.GetValues<AnimationSpeed>()];
        FontFamilyOptions = ["Yu Gothic UI", "Meiryo UI", "Microsoft YaHei UI", "Segoe UI", "Arial"];

        // コマンドの初期化
        ResetToDefaultsCommand = ReactiveCommand.Create(ResetToDefaults);
        ToggleAdvancedSettingsCommand = ReactiveCommand.Create(ToggleAdvancedSettings);
        ChooseAccentColorCommand = ReactiveCommand.Create(ChooseAccentColor);
        BrowseCssFileCommand = ReactiveCommand.Create(BrowseCssFile);
    }

    #region 基本設定プロパティ

    /// <summary>
    /// アプリケーションテーマ
    /// </summary>
    public UiTheme AppTheme
    {
        get => _appTheme;
        set => this.RaiseAndSetIfChanged(ref _appTheme, value);
    }

    /// <summary>
    /// アクセントカラー（ARGB形式）
    /// </summary>
    public uint AccentColor
    {
        get => _accentColor;
        set => this.RaiseAndSetIfChanged(ref _accentColor, value);
    }

    /// <summary>
    /// フォントファミリー
    /// </summary>
    public string FontFamily
    {
        get => _fontFamily;
        set => this.RaiseAndSetIfChanged(ref _fontFamily, value);
    }

    /// <summary>
    /// ベースフォントサイズ
    /// </summary>
    public int BaseFontSize
    {
        get => _baseFontSize;
        set => this.RaiseAndSetIfChanged(ref _baseFontSize, value);
    }

    /// <summary>
    /// ハイコントラストモード
    /// </summary>
    public bool HighContrastMode
    {
        get => _highContrastMode;
        set => this.RaiseAndSetIfChanged(ref _highContrastMode, value);
    }

    #endregion

    #region 詳細設定プロパティ

    /// <summary>
    /// DPIスケーリング対応
    /// </summary>
    public bool EnableDpiScaling
    {
        get => _enableDpiScaling;
        set => this.RaiseAndSetIfChanged(ref _enableDpiScaling, value);
    }

    /// <summary>
    /// カスタムスケールファクター
    /// </summary>
    public double CustomScaleFactor
    {
        get => _customScaleFactor;
        set => this.RaiseAndSetIfChanged(ref _customScaleFactor, value);
    }

    /// <summary>
    /// アニメーション効果の有効化
    /// </summary>
    public bool EnableAnimations
    {
        get => _enableAnimations;
        set => this.RaiseAndSetIfChanged(ref _enableAnimations, value);
    }

    /// <summary>
    /// アニメーション速度
    /// </summary>
    public AnimationSpeed AnimationSpeed
    {
        get => _animationSpeed;
        set => this.RaiseAndSetIfChanged(ref _animationSpeed, value);
    }

    /// <summary>
    /// ウィンドウの角丸効果
    /// </summary>
    public bool RoundedWindowCorners
    {
        get => _roundedWindowCorners;
        set => this.RaiseAndSetIfChanged(ref _roundedWindowCorners, value);
    }

    /// <summary>
    /// 半透明効果（ブラー）
    /// </summary>
    public bool EnableBlurEffect
    {
        get => _enableBlurEffect;
        set => this.RaiseAndSetIfChanged(ref _enableBlurEffect, value);
    }

    #endregion

    #region デバッグ設定プロパティ

    /// <summary>
    /// カスタムCSS適用
    /// </summary>
    public bool EnableCustomCss
    {
        get => _enableCustomCss;
        set => this.RaiseAndSetIfChanged(ref _enableCustomCss, value);
    }

    /// <summary>
    /// カスタムCSSファイルパス
    /// </summary>
    public string CustomCssFilePath
    {
        get => _customCssFilePath;
        set => this.RaiseAndSetIfChanged(ref _customCssFilePath, value);
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
    /// テーマオプションの選択肢
    /// </summary>
    public IReadOnlyList<UiTheme> ThemeOptions { get; }

    /// <summary>
    /// アニメーション速度オプションの選択肢
    /// </summary>
    public IReadOnlyList<AnimationSpeed> AnimationSpeedOptions { get; }

    /// <summary>
    /// フォントファミリーの選択肢
    /// </summary>
    public IReadOnlyList<string> FontFamilyOptions { get; }

    /// <summary>
    /// アクセントカラーのプレビュー用
    /// </summary>
    public string AccentColorHex => $"#{AccentColor:X8}";

    /// <summary>
    /// スケールファクターのパーセンテージ表示用
    /// </summary>
    public string ScaleFactorPercentage => $"{CustomScaleFactor:P0}";

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
    /// アクセントカラー選択コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ChooseAccentColorCommand { get; }

    /// <summary>
    /// CSSファイル参照コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> BrowseCssFileCommand { get; }

    #endregion

    #region メソッド

    /// <summary>
    /// 設定データから初期化
    /// </summary>
    private void InitializeFromSettings(ThemeSettings settings)
    {
        _appTheme = settings.AppTheme;
        _accentColor = settings.AccentColor;
        _fontFamily = settings.FontFamily;
        _baseFontSize = settings.BaseFontSize;
        _highContrastMode = settings.HighContrastMode;
        _enableDpiScaling = settings.EnableDpiScaling;
        _customScaleFactor = settings.CustomScaleFactor;
        _enableAnimations = settings.EnableAnimations;
        _animationSpeed = settings.AnimationSpeed;
        _roundedWindowCorners = settings.RoundedWindowCorners;
        _enableBlurEffect = settings.EnableBlurEffect;
        _enableCustomCss = settings.EnableCustomCss;
        _customCssFilePath = settings.CustomCssFilePath;
    }

    /// <summary>
    /// 変更追跡を設定
    /// </summary>
    private void SetupChangeTracking()
    {
        // 主要プロパティの変更追跡
        this.WhenAnyValue(x => x.AppTheme)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.AccentColor)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.FontFamily)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.BaseFontSize)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.HighContrastMode)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.EnableDpiScaling)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.CustomScaleFactor)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.EnableAnimations)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.AnimationSpeed)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.RoundedWindowCorners)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.EnableBlurEffect)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.EnableCustomCss)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.CustomCssFilePath)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
    }

    /// <summary>
    /// デフォルト値にリセット
    /// </summary>
    private void ResetToDefaults()
    {
        var defaultSettings = new ThemeSettings();
        InitializeFromSettings(defaultSettings);
        HasChanges = true;
        _logger?.LogInformation("テーマ設定をデフォルト値にリセットしました");
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
    /// アクセントカラーを選択
    /// </summary>
    private void ChooseAccentColor()
    {
        // TODO: カラーピッカーダイアログを開く実装
        // 現在は簡単なサンプル実装
        _logger?.LogInformation("アクセントカラー選択ダイアログを開きます");
    }

    /// <summary>
    /// CSSファイルを参照
    /// </summary>
    private void BrowseCssFile()
    {
        // TODO: ファイル選択ダイアログを開く実装
        _logger?.LogInformation("CSSファイル選択ダイアログを開きます");
    }

    /// <summary>
    /// 現在の設定データを取得
    /// </summary>
    public ThemeSettings CurrentSettings => new()
    {
        AppTheme = AppTheme,
        AccentColor = AccentColor,
        FontFamily = FontFamily,
        BaseFontSize = BaseFontSize,
        HighContrastMode = HighContrastMode,
        EnableDpiScaling = EnableDpiScaling,
        CustomScaleFactor = CustomScaleFactor,
        EnableAnimations = EnableAnimations,
        AnimationSpeed = AnimationSpeed,
        RoundedWindowCorners = RoundedWindowCorners,
        EnableBlurEffect = EnableBlurEffect,
        EnableCustomCss = EnableCustomCss,
        CustomCssFilePath = CustomCssFilePath
    };

    /// <summary>
    /// 設定データを更新
    /// </summary>
    /// <param name="settings">新しい設定データ</param>
    public void UpdateSettings(ThemeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        InitializeFromSettings(settings);
        HasChanges = false;
        _logger?.LogDebug("テーマ設定を更新しました");
    }

    #endregion
}
