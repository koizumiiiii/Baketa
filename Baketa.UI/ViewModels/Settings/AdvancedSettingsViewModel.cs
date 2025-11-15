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
/// 拡張設定画面のViewModel
/// 高度な機能とシステム最適化の設定を管理
/// </summary>
public sealed class AdvancedSettingsViewModel : Framework.ViewModelBase
{
    private readonly AdvancedSettings _originalSettings;
    private readonly ILogger<AdvancedSettingsViewModel>? _logger;

    // バッキングフィールド - 基本設定
    private bool _enableAdvancedFeatures;
    private bool _optimizeMemoryUsage;
    private bool _optimizeGarbageCollection;

    // バッキングフィールド - CPU/プロセス設定
    private int _cpuAffinityMask;
    private ProcessPriority _processPriority;
    private int _workerThreadCount;
    private int _ioThreadCount;

    // バッキングフィールド - メモリ/バッファリング設定
    private BufferingStrategy _bufferingStrategy;
    private int _maxQueueSize;

    // バッキングフィールド - ネットワーク設定
    private int _networkTimeoutSeconds;
    private int _maxHttpConnections;

    // バッキングフィールド - リトライ設定
    private RetryStrategy _retryStrategy;
    private int _maxRetryCount;
    private int _retryDelayMs;

    // バッキングフィールド - 統計/監視設定
    private bool _enableStatisticsCollection;
    private int _statisticsRetentionDays;
    private bool _enableProfiling;
    private bool _enableAnomalyDetection;
    private bool _enableAutoRecovery;

    // バッキングフィールド - 実験的/デバッグ設定
    private bool _enableExperimentalFeatures;
    private bool _exposeInternalApis;
    private bool _enableDebugBreaks;
    private bool _generateMemoryDumps;
    private string _customConfigPath;

    // UI制御フィールド
    private bool _showAdvancedSettings;
    private bool _showExperimentalSettings;
    private bool _hasChanges;

    /// <summary>
    /// AdvancedSettingsViewModelを初期化します
    /// </summary>
    /// <param name="settings">拡張設定データ</param>
    /// <param name="eventAggregator">イベント集約器</param>
    /// <param name="logger">ロガー（オプション）</param>
    public AdvancedSettingsViewModel(
        AdvancedSettings settings,
        IEventAggregator eventAggregator,
        ILogger<AdvancedSettingsViewModel>? logger = null) : base(eventAggregator)
    {
        _originalSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
        _customConfigPath = string.Empty; // デフォルト値で初期化

        // 初期化
        InitializeFromSettings(settings);

        // 変更追跡の設定
        SetupChangeTracking();

        // 選択肢の初期化
        ProcessPriorityOptions = [.. Enum.GetValues<ProcessPriority>()];
        BufferingStrategyOptions = [.. Enum.GetValues<BufferingStrategy>()];
        RetryStrategyOptions = [.. Enum.GetValues<RetryStrategy>()];
        ThreadCountOptions = [0, 1, 2, 4, 6, 8, 12, 16, 24, 32];
        QueueSizeOptions = [100, 500, 1000, 2000, 5000, 10000];

        // コマンドの初期化
        ResetToDefaultsCommand = ReactiveCommand.Create(ResetToDefaults);
        ToggleAdvancedSettingsCommand = ReactiveCommand.Create(ToggleAdvancedSettings);
        ToggleExperimentalSettingsCommand = ReactiveCommand.Create(ToggleExperimentalSettings);
        RunSystemDiagnosticsCommand = ReactiveCommand.Create(RunSystemDiagnostics);
        OptimizeSystemCommand = ReactiveCommand.Create(OptimizeSystem);
        BrowseConfigPathCommand = ReactiveCommand.Create(BrowseConfigPath);
    }

    #region 基本設定プロパティ

    /// <summary>
    /// 高度な設定の有効化
    /// </summary>
    public bool EnableAdvancedFeatures
    {
        get => _enableAdvancedFeatures;
        set => this.RaiseAndSetIfChanged(ref _enableAdvancedFeatures, value);
    }

    /// <summary>
    /// メモリ管理の最適化
    /// </summary>
    public bool OptimizeMemoryUsage
    {
        get => _optimizeMemoryUsage;
        set => this.RaiseAndSetIfChanged(ref _optimizeMemoryUsage, value);
    }

    /// <summary>
    /// ガベージコレクション調整
    /// </summary>
    public bool OptimizeGarbageCollection
    {
        get => _optimizeGarbageCollection;
        set => this.RaiseAndSetIfChanged(ref _optimizeGarbageCollection, value);
    }

    #endregion

    #region CPU/プロセス設定プロパティ

    /// <summary>
    /// CPU親和性の設定
    /// </summary>
    public int CpuAffinityMask
    {
        get => _cpuAffinityMask;
        set => this.RaiseAndSetIfChanged(ref _cpuAffinityMask, value);
    }

    /// <summary>
    /// プロセス優先度
    /// </summary>
    public ProcessPriority ProcessPriority
    {
        get => _processPriority;
        set => this.RaiseAndSetIfChanged(ref _processPriority, value);
    }

    /// <summary>
    /// ワーカースレッド数
    /// </summary>
    public int WorkerThreadCount
    {
        get => _workerThreadCount;
        set => this.RaiseAndSetIfChanged(ref _workerThreadCount, value);
    }

    /// <summary>
    /// I/Oスレッド数
    /// </summary>
    public int IoThreadCount
    {
        get => _ioThreadCount;
        set => this.RaiseAndSetIfChanged(ref _ioThreadCount, value);
    }

    #endregion

    #region メモリ/バッファリング設定プロパティ

    /// <summary>
    /// バッファリング戦略
    /// </summary>
    public BufferingStrategy BufferingStrategy
    {
        get => _bufferingStrategy;
        set => this.RaiseAndSetIfChanged(ref _bufferingStrategy, value);
    }

    /// <summary>
    /// キューサイズ制限
    /// </summary>
    public int MaxQueueSize
    {
        get => _maxQueueSize;
        set => this.RaiseAndSetIfChanged(ref _maxQueueSize, value);
    }

    #endregion

    #region ネットワーク設定プロパティ

    /// <summary>
    /// ネットワークタイムアウト
    /// </summary>
    public int NetworkTimeoutSeconds
    {
        get => _networkTimeoutSeconds;
        set => this.RaiseAndSetIfChanged(ref _networkTimeoutSeconds, value);
    }

    /// <summary>
    /// HTTP接続プール最大サイズ
    /// </summary>
    public int MaxHttpConnections
    {
        get => _maxHttpConnections;
        set => this.RaiseAndSetIfChanged(ref _maxHttpConnections, value);
    }

    #endregion

    #region リトライ設定プロパティ

    /// <summary>
    /// リトライ戦略の設定
    /// </summary>
    public RetryStrategy RetryStrategy
    {
        get => _retryStrategy;
        set => this.RaiseAndSetIfChanged(ref _retryStrategy, value);
    }

    /// <summary>
    /// 最大リトライ回数
    /// </summary>
    public int MaxRetryCount
    {
        get => _maxRetryCount;
        set => this.RaiseAndSetIfChanged(ref _maxRetryCount, value);
    }

    /// <summary>
    /// リトライ間隔（ミリ秒）
    /// </summary>
    public int RetryDelayMs
    {
        get => _retryDelayMs;
        set => this.RaiseAndSetIfChanged(ref _retryDelayMs, value);
    }

    /// <summary>
    /// リトライ設定が有効かどうか
    /// </summary>
    public bool IsRetryConfigEnabled => RetryStrategy != RetryStrategy.None;

    #endregion

    #region 統計/監視設定プロパティ

    /// <summary>
    /// 統計情報収集の有効化
    /// </summary>
    public bool EnableStatisticsCollection
    {
        get => _enableStatisticsCollection;
        set => this.RaiseAndSetIfChanged(ref _enableStatisticsCollection, value);
    }

    /// <summary>
    /// 統計データの保持期間（日）
    /// </summary>
    public int StatisticsRetentionDays
    {
        get => _statisticsRetentionDays;
        set => this.RaiseAndSetIfChanged(ref _statisticsRetentionDays, value);
    }

    /// <summary>
    /// プロファイリングの有効化
    /// </summary>
    public bool EnableProfiling
    {
        get => _enableProfiling;
        set => this.RaiseAndSetIfChanged(ref _enableProfiling, value);
    }

    /// <summary>
    /// 異常検出の有効化
    /// </summary>
    public bool EnableAnomalyDetection
    {
        get => _enableAnomalyDetection;
        set => this.RaiseAndSetIfChanged(ref _enableAnomalyDetection, value);
    }

    /// <summary>
    /// 自動修復機能
    /// </summary>
    public bool EnableAutoRecovery
    {
        get => _enableAutoRecovery;
        set => this.RaiseAndSetIfChanged(ref _enableAutoRecovery, value);
    }

    #endregion

    #region 実験的/デバッグ設定プロパティ

    /// <summary>
    /// 実験的機能の有効化
    /// </summary>
    public bool EnableExperimentalFeatures
    {
        get => _enableExperimentalFeatures;
        set => this.RaiseAndSetIfChanged(ref _enableExperimentalFeatures, value);
    }

    /// <summary>
    /// 内部API露出
    /// </summary>
    public bool ExposeInternalApis
    {
        get => _exposeInternalApis;
        set => this.RaiseAndSetIfChanged(ref _exposeInternalApis, value);
    }

    /// <summary>
    /// デバッグブレークポイント
    /// </summary>
    public bool EnableDebugBreaks
    {
        get => _enableDebugBreaks;
        set => this.RaiseAndSetIfChanged(ref _enableDebugBreaks, value);
    }

    /// <summary>
    /// メモリダンプ生成
    /// </summary>
    public bool GenerateMemoryDumps
    {
        get => _generateMemoryDumps;
        set => this.RaiseAndSetIfChanged(ref _generateMemoryDumps, value);
    }

    /// <summary>
    /// カスタム設定ファイルパス
    /// </summary>
    public string CustomConfigPath
    {
        get => _customConfigPath;
        set => this.RaiseAndSetIfChanged(ref _customConfigPath, value);
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
    /// 実験的設定を表示するかどうか
    /// </summary>
    public bool ShowExperimentalSettings
    {
        get => _showExperimentalSettings;
        set => this.RaiseAndSetIfChanged(ref _showExperimentalSettings, value);
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
    /// プロセス優先度の選択肢
    /// </summary>
    public IReadOnlyList<ProcessPriority> ProcessPriorityOptions { get; }

    /// <summary>
    /// バッファリング戦略の選択肢
    /// </summary>
    public IReadOnlyList<BufferingStrategy> BufferingStrategyOptions { get; }

    /// <summary>
    /// リトライ戦略の選択肢
    /// </summary>
    public IReadOnlyList<RetryStrategy> RetryStrategyOptions { get; }

    /// <summary>
    /// スレッド数の選択肢
    /// </summary>
    public IReadOnlyList<int> ThreadCountOptions { get; }

    /// <summary>
    /// キューサイズの選択肢
    /// </summary>
    public IReadOnlyList<int> QueueSizeOptions { get; }

    /// <summary>
    /// CPU親和性マスクの表示用文字列
    /// </summary>
    public string CpuAffinityMaskText => CpuAffinityMask == 0 ? "自動" : $"0x{CpuAffinityMask:X}";

    /// <summary>
    /// リトライ間隔の表示用文字列
    /// </summary>
    public string RetryDelayText => $"{RetryDelayMs}ms ({RetryDelayMs / 1000.0:F1}秒)";

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
    /// 実験的設定表示切り替えコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ToggleExperimentalSettingsCommand { get; }

    /// <summary>
    /// システム診断実行コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> RunSystemDiagnosticsCommand { get; }

    /// <summary>
    /// システム最適化コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> OptimizeSystemCommand { get; }

    /// <summary>
    /// 設定ファイルパス参照コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> BrowseConfigPathCommand { get; }

    #endregion

    #region メソッド

    /// <summary>
    /// 設定データから初期化
    /// </summary>
    private void InitializeFromSettings(AdvancedSettings settings)
    {
        _enableAdvancedFeatures = settings.EnableAdvancedFeatures;
        _optimizeMemoryUsage = settings.OptimizeMemoryUsage;
        _optimizeGarbageCollection = settings.OptimizeGarbageCollection;
        _cpuAffinityMask = settings.CpuAffinityMask;
        _processPriority = settings.ProcessPriority;
        _workerThreadCount = settings.WorkerThreadCount;
        _ioThreadCount = settings.IoThreadCount;
        _bufferingStrategy = settings.BufferingStrategy;
        _maxQueueSize = settings.MaxQueueSize;
        _networkTimeoutSeconds = settings.NetworkTimeoutSeconds;
        _maxHttpConnections = settings.MaxHttpConnections;
        _retryStrategy = settings.RetryStrategy;
        _maxRetryCount = settings.MaxRetryCount;
        _retryDelayMs = settings.RetryDelayMs;
        _enableStatisticsCollection = settings.EnableStatisticsCollection;
        _statisticsRetentionDays = settings.StatisticsRetentionDays;
        _enableProfiling = settings.EnableProfiling;
        _enableAnomalyDetection = settings.EnableAnomalyDetection;
        _enableAutoRecovery = settings.EnableAutoRecovery;
        _enableExperimentalFeatures = settings.EnableExperimentalFeatures;
        _exposeInternalApis = settings.ExposeInternalApis;
        _enableDebugBreaks = settings.EnableDebugBreaks;
        _generateMemoryDumps = settings.GenerateMemoryDumps;
        _customConfigPath = settings.CustomConfigPath;
    }

    /// <summary>
    /// 変更追跡を設定
    /// </summary>
    private void SetupChangeTracking()
    {
        // 基本設定プロパティの変更追跡
        this.WhenAnyValue(x => x.EnableAdvancedFeatures, x => x.OptimizeMemoryUsage,
                          x => x.OptimizeGarbageCollection)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        // CPU/プロセス設定の変更追跡
        this.WhenAnyValue(x => x.CpuAffinityMask, x => x.ProcessPriority,
                          x => x.WorkerThreadCount, x => x.IoThreadCount)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        // メモリ/バッファリング設定の変更追跡
        this.WhenAnyValue(x => x.BufferingStrategy, x => x.MaxQueueSize)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        // ネットワーク設定の変更追跡
        this.WhenAnyValue(x => x.NetworkTimeoutSeconds, x => x.MaxHttpConnections)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        // リトライ設定の変更追跡
        this.WhenAnyValue(x => x.RetryStrategy)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ =>
            {
                HasChanges = true;
                this.RaisePropertyChanged(nameof(IsRetryConfigEnabled));
            });

        this.WhenAnyValue(x => x.MaxRetryCount, x => x.RetryDelayMs)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        // 統計/監視設定の変更追跡
        this.WhenAnyValue(x => x.EnableStatisticsCollection, x => x.StatisticsRetentionDays,
                          x => x.EnableProfiling, x => x.EnableAnomalyDetection,
                          x => x.EnableAutoRecovery)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        // 実験的/デバッグ設定の変更追跡
        this.WhenAnyValue(x => x.EnableExperimentalFeatures, x => x.ExposeInternalApis,
                          x => x.EnableDebugBreaks, x => x.GenerateMemoryDumps,
                          x => x.CustomConfigPath)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
    }

    /// <summary>
    /// デフォルト値にリセット
    /// </summary>
    private void ResetToDefaults()
    {
        var defaultSettings = new AdvancedSettings();
        InitializeFromSettings(defaultSettings);
        HasChanges = true;
        _logger?.LogInformation("拡張設定をデフォルト値にリセットしました");
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
    /// 実験的設定表示を切り替え
    /// </summary>
    private void ToggleExperimentalSettings()
    {
        ShowExperimentalSettings = !ShowExperimentalSettings;
        _logger?.LogDebug("実験的設定表示を切り替えました: {ShowExperimental}", ShowExperimentalSettings);
    }

    /// <summary>
    /// システム診断を実行
    /// </summary>
    private void RunSystemDiagnostics()
    {
        // TODO: システム診断機能を実装
        _logger?.LogInformation("システム診断を実行しました");
    }

    /// <summary>
    /// システム最適化を実行
    /// </summary>
    private void OptimizeSystem()
    {
        // TODO: 自動システム最適化機能を実装
        _logger?.LogInformation("システム最適化を実行しました");
    }

    /// <summary>
    /// 設定ファイルパスを参照
    /// </summary>
    private void BrowseConfigPath()
    {
        // TODO: ファイル選択ダイアログを開く実装
        _logger?.LogInformation("設定ファイルパス選択ダイアログを開きます");
    }

    /// <summary>
    /// 設定を検証
    /// </summary>
    public bool ValidateSettings()
    {
        try
        {
            // CPU親和性マスクの検証
            if (CpuAffinityMask < 0 || CpuAffinityMask > 64)
            {
                _logger?.LogWarning("CPU親和性マスクが範囲外です: {Mask}", CpuAffinityMask);
                return false;
            }

            // スレッド数の検証
            if (WorkerThreadCount < 0 || WorkerThreadCount > 32)
            {
                _logger?.LogWarning("ワーカースレッド数が範囲外です: {Count}", WorkerThreadCount);
                return false;
            }

            if (IoThreadCount < 0 || IoThreadCount > 16)
            {
                _logger?.LogWarning("I/Oスレッド数が範囲外です: {Count}", IoThreadCount);
                return false;
            }

            // タイムアウトの検証
            if (NetworkTimeoutSeconds < 5 || NetworkTimeoutSeconds > 300)
            {
                _logger?.LogWarning("ネットワークタイムアウトが範囲外です: {Timeout}秒", NetworkTimeoutSeconds);
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
    public AdvancedSettings CurrentSettings => new()
    {
        EnableAdvancedFeatures = EnableAdvancedFeatures,
        OptimizeMemoryUsage = OptimizeMemoryUsage,
        OptimizeGarbageCollection = OptimizeGarbageCollection,
        CpuAffinityMask = CpuAffinityMask,
        ProcessPriority = ProcessPriority,
        WorkerThreadCount = WorkerThreadCount,
        IoThreadCount = IoThreadCount,
        BufferingStrategy = BufferingStrategy,
        MaxQueueSize = MaxQueueSize,
        NetworkTimeoutSeconds = NetworkTimeoutSeconds,
        MaxHttpConnections = MaxHttpConnections,
        RetryStrategy = RetryStrategy,
        MaxRetryCount = MaxRetryCount,
        RetryDelayMs = RetryDelayMs,
        EnableStatisticsCollection = EnableStatisticsCollection,
        StatisticsRetentionDays = StatisticsRetentionDays,
        EnableProfiling = EnableProfiling,
        EnableAnomalyDetection = EnableAnomalyDetection,
        EnableAutoRecovery = EnableAutoRecovery,
        EnableExperimentalFeatures = EnableExperimentalFeatures,
        ExposeInternalApis = ExposeInternalApis,
        EnableDebugBreaks = EnableDebugBreaks,
        GenerateMemoryDumps = GenerateMemoryDumps,
        CustomConfigPath = CustomConfigPath
    };

    /// <summary>
    /// 設定データを更新
    /// </summary>
    /// <param name="settings">新しい設定データ</param>
    public void UpdateSettings(AdvancedSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        InitializeFromSettings(settings);
        HasChanges = false;
        _logger?.LogDebug("拡張設定を更新しました");
    }

    #endregion
}
