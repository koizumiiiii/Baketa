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
/// キャプチャ設定画面のViewModel
/// 画面キャプチャとスクリーンショット機能の設定を管理
/// </summary>
public sealed class CaptureSettingsViewModel : Framework.ViewModelBase
{
    private readonly CaptureSettings _originalSettings;
    private readonly ILogger<CaptureSettingsViewModel>? _logger;
    
    // バッキングフィールド - 基本設定
    private bool _isEnabled;
    private int _captureIntervalMs;
    private int _captureQuality;
    private bool _autoDetectCaptureArea;
    
    // バッキングフィールド - 固定領域設定
    private int _fixedCaptureAreaX;
    private int _fixedCaptureAreaY;
    private int _fixedCaptureAreaWidth;
    private int _fixedCaptureAreaHeight;
    
    // バッキングフィールド - 詳細設定
    private int _targetMonitor;
    private bool _considerDpiScaling;
    private bool _useHardwareAcceleration;
    private bool _enableDifferenceDetection;
    private int _differenceDetectionSensitivity;
    private double _differenceThreshold;
    private int _differenceDetectionGridSize;
    
    // バッキングフィールド - ゲーム対応
    private bool _fullscreenOptimization;
    private bool _autoOptimizeForGames;
    
    // バッキングフィールド - 履歴設定
    private bool _saveCaptureHistory;
    private int _maxCaptureHistoryCount;
    
    // バッキングフィールド - デバッグ設定
    private bool _saveDebugCaptures;
    private string _debugCaptureSavePath;
    
    // UI制御フィールド
    private bool _showAdvancedSettings;
    private bool _hasChanges;

    /// <summary>
    /// CaptureSettingsViewModelを初期化します
    /// </summary>
    /// <param name="settings">キャプチャ設定データ</param>
    /// <param name="eventAggregator">イベント集約器</param>
    /// <param name="logger">ロガー（オプション）</param>
    public CaptureSettingsViewModel(
        CaptureSettings settings,
        IEventAggregator eventAggregator,
        ILogger<CaptureSettingsViewModel>? logger = null) : base(eventAggregator)
    {
        _originalSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
        _debugCaptureSavePath = string.Empty; // デフォルト値で初期化

        // 初期化
        InitializeFromSettings(settings);

        // 変更追跡の設定
        SetupChangeTracking();

        // 選択肢の初期化
        MonitorOptions = GetAvailableMonitors();
        GridSizeOptions = [4, 8, 16, 32, 64];

        // コマンドの初期化
        ResetToDefaultsCommand = ReactiveCommand.Create(ResetToDefaults);
        ToggleAdvancedSettingsCommand = ReactiveCommand.Create(ToggleAdvancedSettings);
        TestCaptureCommand = ReactiveCommand.Create(TestCapture);
        OptimizeForCurrentGameCommand = ReactiveCommand.Create(OptimizeForCurrentGame);
        BrowseDebugPathCommand = ReactiveCommand.Create(BrowseDebugPath);
    }

    #region 基本設定プロパティ

    /// <summary>
    /// キャプチャ機能の有効化
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => this.RaiseAndSetIfChanged(ref _isEnabled, value);
    }

    /// <summary>
    /// キャプチャ間隔（ミリ秒）
    /// </summary>
    public int CaptureIntervalMs
    {
        get => _captureIntervalMs;
        set => this.RaiseAndSetIfChanged(ref _captureIntervalMs, value);
    }

    /// <summary>
    /// キャプチャ品質（1-100）
    /// </summary>
    public int CaptureQuality
    {
        get => _captureQuality;
        set => this.RaiseAndSetIfChanged(ref _captureQuality, value);
    }

    /// <summary>
    /// キャプチャ領域の自動検出
    /// </summary>
    public bool AutoDetectCaptureArea
    {
        get => _autoDetectCaptureArea;
        set => this.RaiseAndSetIfChanged(ref _autoDetectCaptureArea, value);
    }

    #endregion

    #region 固定領域設定プロパティ

    /// <summary>
    /// 固定キャプチャ領域のX座標
    /// </summary>
    public int FixedCaptureAreaX
    {
        get => _fixedCaptureAreaX;
        set => this.RaiseAndSetIfChanged(ref _fixedCaptureAreaX, value);
    }

    /// <summary>
    /// 固定キャプチャ領域のY座標
    /// </summary>
    public int FixedCaptureAreaY
    {
        get => _fixedCaptureAreaY;
        set => this.RaiseAndSetIfChanged(ref _fixedCaptureAreaY, value);
    }

    /// <summary>
    /// 固定キャプチャ領域の幅
    /// </summary>
    public int FixedCaptureAreaWidth
    {
        get => _fixedCaptureAreaWidth;
        set => this.RaiseAndSetIfChanged(ref _fixedCaptureAreaWidth, value);
    }

    /// <summary>
    /// 固定キャプチャ領域の高さ
    /// </summary>
    public int FixedCaptureAreaHeight
    {
        get => _fixedCaptureAreaHeight;
        set => this.RaiseAndSetIfChanged(ref _fixedCaptureAreaHeight, value);
    }

    /// <summary>
    /// 固定領域設定が有効かどうか
    /// </summary>
    public bool IsFixedAreaEnabled => !AutoDetectCaptureArea;

    #endregion

    #region 詳細設定プロパティ

    /// <summary>
    /// モニター選択（マルチモニター環境）
    /// </summary>
    public int TargetMonitor
    {
        get => _targetMonitor;
        set => this.RaiseAndSetIfChanged(ref _targetMonitor, value);
    }

    /// <summary>
    /// DPIスケーリング考慮
    /// </summary>
    public bool ConsiderDpiScaling
    {
        get => _considerDpiScaling;
        set => this.RaiseAndSetIfChanged(ref _considerDpiScaling, value);
    }

    /// <summary>
    /// ハードウェアアクセラレーション使用
    /// </summary>
    public bool UseHardwareAcceleration
    {
        get => _useHardwareAcceleration;
        set => this.RaiseAndSetIfChanged(ref _useHardwareAcceleration, value);
    }

    /// <summary>
    /// 差分検出機能の有効化
    /// </summary>
    public bool EnableDifferenceDetection
    {
        get => _enableDifferenceDetection;
        set => this.RaiseAndSetIfChanged(ref _enableDifferenceDetection, value);
    }

    /// <summary>
    /// キャプチャ差分検出の感度
    /// </summary>
    public int DifferenceDetectionSensitivity
    {
        get => _differenceDetectionSensitivity;
        set => this.RaiseAndSetIfChanged(ref _differenceDetectionSensitivity, value);
    }

    /// <summary>
    /// 差分検出闾値（0.0～1.0）
    /// </summary>
    public double DifferenceThreshold
    {
        get => _differenceThreshold;
        set => this.RaiseAndSetIfChanged(ref _differenceThreshold, value);
    }

    /// <summary>
    /// 差分検出領域の分割数
    /// </summary>
    public int DifferenceDetectionGridSize
    {
        get => _differenceDetectionGridSize;
        set => this.RaiseAndSetIfChanged(ref _differenceDetectionGridSize, value);
    }

    #endregion

    #region ゲーム対応プロパティ

    /// <summary>
    /// フルスクリーンゲーム対応モード
    /// </summary>
    public bool FullscreenOptimization
    {
        get => _fullscreenOptimization;
        set => this.RaiseAndSetIfChanged(ref _fullscreenOptimization, value);
    }

    /// <summary>
    /// ゲーム検出時の自動最適化
    /// </summary>
    public bool AutoOptimizeForGames
    {
        get => _autoOptimizeForGames;
        set => this.RaiseAndSetIfChanged(ref _autoOptimizeForGames, value);
    }

    #endregion

    #region 履歴設定プロパティ

    /// <summary>
    /// キャプチャ履歴の保存
    /// </summary>
    public bool SaveCaptureHistory
    {
        get => _saveCaptureHistory;
        set => this.RaiseAndSetIfChanged(ref _saveCaptureHistory, value);
    }

    /// <summary>
    /// キャプチャ履歴の最大保存数
    /// </summary>
    public int MaxCaptureHistoryCount
    {
        get => _maxCaptureHistoryCount;
        set => this.RaiseAndSetIfChanged(ref _maxCaptureHistoryCount, value);
    }

    #endregion

    #region デバッグ設定プロパティ

    /// <summary>
    /// デバッグ用キャプチャ保存
    /// </summary>
    public bool SaveDebugCaptures
    {
        get => _saveDebugCaptures;
        set => this.RaiseAndSetIfChanged(ref _saveDebugCaptures, value);
    }

    /// <summary>
    /// デバッグ用キャプチャ保存パス
    /// </summary>
    public string DebugCaptureSavePath
    {
        get => _debugCaptureSavePath;
        set => this.RaiseAndSetIfChanged(ref _debugCaptureSavePath, value);
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
    /// モニター選択の選択肢
    /// </summary>
    public IReadOnlyList<MonitorOption> MonitorOptions { get; }

    /// <summary>
    /// 選択されたモニターオプション
    /// </summary>
    public MonitorOption? SelectedMonitorOption
    {
        get => MonitorOptions.FirstOrDefault(m => m.Index == TargetMonitor);
        set
        {
            if (value is not null && TargetMonitor != value.Index)
            {
                TargetMonitor = value.Index;
                this.RaisePropertyChanged();
            }
        }
    }

    /// <summary>
    /// 差分検出グリッドサイズの選択肢
    /// </summary>
    public IReadOnlyList<int> GridSizeOptions { get; }

    /// <summary>
    /// キャプチャ間隔の表示用文字列
    /// </summary>
    public string CaptureIntervalText => $"{CaptureIntervalMs}ms ({1000.0 / CaptureIntervalMs:F1} FPS)";

    /// <summary>
    /// 差分閾値のパーセンテージ表示用
    /// </summary>
    public string DifferenceThresholdPercentage => $"{DifferenceThreshold:P1}";

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
    /// テストキャプチャコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> TestCaptureCommand { get; }

    /// <summary>
    /// 現在のゲームに最適化コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> OptimizeForCurrentGameCommand { get; }

    /// <summary>
    /// デバッグパス参照コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> BrowseDebugPathCommand { get; }

    #endregion

    #region メソッド

    /// <summary>
    /// 設定データから初期化
    /// </summary>
    private void InitializeFromSettings(CaptureSettings settings)
    {
        _isEnabled = settings.IsEnabled;
        _captureIntervalMs = settings.CaptureIntervalMs;
        _captureQuality = settings.CaptureQuality;
        _autoDetectCaptureArea = settings.AutoDetectCaptureArea;
        _fixedCaptureAreaX = settings.FixedCaptureAreaX;
        _fixedCaptureAreaY = settings.FixedCaptureAreaY;
        _fixedCaptureAreaWidth = settings.FixedCaptureAreaWidth;
        _fixedCaptureAreaHeight = settings.FixedCaptureAreaHeight;
        _targetMonitor = settings.TargetMonitor;
        _considerDpiScaling = settings.ConsiderDpiScaling;
        _useHardwareAcceleration = settings.UseHardwareAcceleration;
        _enableDifferenceDetection = settings.EnableDifferenceDetection;
        _differenceDetectionSensitivity = settings.DifferenceDetectionSensitivity;
        _differenceThreshold = settings.DifferenceThreshold;
        _differenceDetectionGridSize = settings.DifferenceDetectionGridSize;
        _fullscreenOptimization = settings.FullscreenOptimization;
        _autoOptimizeForGames = settings.AutoOptimizeForGames;
        _saveCaptureHistory = settings.SaveCaptureHistory;
        _maxCaptureHistoryCount = settings.MaxCaptureHistoryCount;
        _saveDebugCaptures = settings.SaveDebugCaptures;
        _debugCaptureSavePath = settings.DebugCaptureSavePath;
    }

    /// <summary>
    /// 変更追跡を設定
    /// </summary>
    private void SetupChangeTracking()
    {
        // 基本設定プロパティの変更追跡
        this.WhenAnyValue(x => x.IsEnabled)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        this.WhenAnyValue(x => x.CaptureIntervalMs)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        this.WhenAnyValue(x => x.CaptureQuality)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        this.WhenAnyValue(x => x.AutoDetectCaptureArea)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => {
                HasChanges = true;
                this.RaisePropertyChanged(nameof(IsFixedAreaEnabled));
            });
        
        // 固定領域設定の変更追跡
        this.WhenAnyValue(x => x.FixedCaptureAreaX, x => x.FixedCaptureAreaY, 
                          x => x.FixedCaptureAreaWidth, x => x.FixedCaptureAreaHeight)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        // 詳細設定プロパティの変更追跡
        this.WhenAnyValue(x => x.TargetMonitor)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => {
                HasChanges = true;
                this.RaisePropertyChanged(nameof(SelectedMonitorOption));
            });
        
        this.WhenAnyValue(x => x.ConsiderDpiScaling, x => x.UseHardwareAcceleration, x => x.EnableDifferenceDetection)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        this.WhenAnyValue(x => x.DifferenceDetectionSensitivity, x => x.DifferenceThreshold, 
                          x => x.DifferenceDetectionGridSize)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        // ゲーム対応設定の変更追跡
        this.WhenAnyValue(x => x.FullscreenOptimization, x => x.AutoOptimizeForGames)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        // 履歴設定の変更追跡
        this.WhenAnyValue(x => x.SaveCaptureHistory, x => x.MaxCaptureHistoryCount)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        // デバッグ設定の変更追跡
        this.WhenAnyValue(x => x.SaveDebugCaptures, x => x.DebugCaptureSavePath)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
    }

    /// <summary>
    /// デフォルト値にリセット
    /// </summary>
    private void ResetToDefaults()
    {
        var defaultSettings = new CaptureSettings();
        InitializeFromSettings(defaultSettings);
        HasChanges = true;
        _logger?.LogInformation("キャプチャ設定をデフォルト値にリセットしました");
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
    /// テストキャプチャを実行
    /// </summary>
    private void TestCapture()
    {
        // TODO: 実際のテストキャプチャ機能を実装
        _logger?.LogInformation("テストキャプチャを実行しました");
    }

    /// <summary>
    /// 現在のゲームに最適化
    /// </summary>
    private void OptimizeForCurrentGame()
    {
        // TODO: 現在実行中のゲームを検出して設定を最適化
        _logger?.LogInformation("現在のゲームに設定を最適化しました");
    }

    /// <summary>
    /// デバッグパスを参照
    /// </summary>
    private void BrowseDebugPath()
    {
        // TODO: フォルダ選択ダイアログを開く実装
        _logger?.LogInformation("デバッグパス選択ダイアログを開きます");
    }

    /// <summary>
    /// 利用可能なモニターの一覧を取得
    /// </summary>
    private static List<MonitorOption> GetAvailableMonitors()
    {
        var monitors = new List<MonitorOption>
        {
            new MonitorOption { Index = -1, Name = "自動選択" },
            new MonitorOption { Index = 0, Name = "プライマリモニター" }
        };
        
        // TODO: 実際のモニター検出ロジックを実装
        for (int i = 1; i <= 3; i++)
        {
            monitors.Add(new MonitorOption { Index = i, Name = $"モニター {i + 1}" });
        }
        
        return monitors;
    }

    /// <summary>
    /// 設定を検証
    /// </summary>
    public bool ValidateSettings()
    {
        try
        {
            // キャプチャ間隔の検証
            if (CaptureIntervalMs < 100 || CaptureIntervalMs > 5000)
            {
                _logger?.LogWarning("キャプチャ間隔が範囲外です: {Interval}ms", CaptureIntervalMs);
                return false;
            }
            
            // キャプチャ品質の検証
            if (CaptureQuality < 1 || CaptureQuality > 100)
            {
                _logger?.LogWarning("キャプチャ品質が範囲外です: {Quality}", CaptureQuality);
                return false;
            }
            
            // 固定領域設定の検証
            if (!AutoDetectCaptureArea)
            {
                if (FixedCaptureAreaWidth <= 0 || FixedCaptureAreaHeight <= 0)
                {
                    _logger?.LogWarning("固定キャプチャ領域のサイズが無効です: {Width}x{Height}", 
                        FixedCaptureAreaWidth, FixedCaptureAreaHeight);
                    return false;
                }
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
    public CaptureSettings CurrentSettings => new()
    {
        IsEnabled = IsEnabled,
        CaptureIntervalMs = CaptureIntervalMs,
        CaptureQuality = CaptureQuality,
        AutoDetectCaptureArea = AutoDetectCaptureArea,
        FixedCaptureAreaX = FixedCaptureAreaX,
        FixedCaptureAreaY = FixedCaptureAreaY,
        FixedCaptureAreaWidth = FixedCaptureAreaWidth,
        FixedCaptureAreaHeight = FixedCaptureAreaHeight,
        TargetMonitor = TargetMonitor,
        ConsiderDpiScaling = ConsiderDpiScaling,
        UseHardwareAcceleration = UseHardwareAcceleration,
        EnableDifferenceDetection = EnableDifferenceDetection,
        DifferenceDetectionSensitivity = DifferenceDetectionSensitivity,
        DifferenceThreshold = DifferenceThreshold,
        DifferenceDetectionGridSize = DifferenceDetectionGridSize,
        FullscreenOptimization = FullscreenOptimization,
        AutoOptimizeForGames = AutoOptimizeForGames,
        SaveCaptureHistory = SaveCaptureHistory,
        MaxCaptureHistoryCount = MaxCaptureHistoryCount,
        SaveDebugCaptures = SaveDebugCaptures,
        DebugCaptureSavePath = DebugCaptureSavePath
    };

    /// <summary>
    /// 設定データを更新
    /// </summary>
    /// <param name="settings">新しい設定データ</param>
    public void UpdateSettings(CaptureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        
        InitializeFromSettings(settings);
        HasChanges = false;
        _logger?.LogDebug("キャプチャ設定を更新しました");
    }

    #endregion
}

/// <summary>
/// モニター選択肢用のデータクラス
/// </summary>
public record MonitorOption
{
    /// <summary>
    /// モニターのインデックス
    /// </summary>
    public int Index { get; init; }
    
    /// <summary>
    /// モニターの表示名
    /// </summary>
    public string Name { get; init; } = string.Empty;
}
