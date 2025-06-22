using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Reactive;
using Baketa.Core.Settings;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Framework;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.ViewModels.Settings;

/// <summary>
/// OCR設定画面のViewModel
/// OCR処理の詳細設定を管理
/// </summary>
public sealed class OcrSettingsViewModel : Framework.ViewModelBase
{
    private readonly OcrSettings _originalSettings;
    private readonly ILogger<OcrSettingsViewModel>? _logger;
    
    // バッキングフィールド
    private bool _enableOcr;
    private string _ocrLanguage = "Japanese"; // デフォルト値で初期化
    private double _confidenceThreshold;
    private bool _enableTextFiltering;
    private bool _showAdvancedSettings;
    private bool _hasChanges;

    /// <summary>
    /// OcrSettingsViewModelを初期化します
    /// </summary>
    /// <param name="settings">OCR設定データ</param>
    /// <param name="eventAggregator">イベント集約器</param>
    /// <param name="logger">ロガー（オプション）</param>
    public OcrSettingsViewModel(
        OcrSettings settings,
        IEventAggregator eventAggregator,
        ILogger<OcrSettingsViewModel>? logger = null) : base(eventAggregator)
    {
        _originalSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;

        // 初期化
        InitializeFromSettings(settings);

        // 変更追跡の設定
        SetupChangeTracking();

        // 選択肢の初期化
        LanguageOptions = ["Japanese", "English", "Chinese", "Korean"];

        // コマンドの初期化
        ResetToDefaultsCommand = ReactiveCommand.Create(ResetToDefaults);
        ToggleAdvancedSettingsCommand = ReactiveCommand.Create(ToggleAdvancedSettings);
        TestOcrCommand = ReactiveCommand.Create(TestOcr);
    }

    #region プロパティ

    /// <summary>
    /// OCRを有効化
    /// </summary>
    public bool EnableOcr
    {
        get => _enableOcr;
        set => this.RaiseAndSetIfChanged(ref _enableOcr, value);
    }

    /// <summary>
    /// OCR言語
    /// </summary>
    public string OcrLanguage
    {
        get => _ocrLanguage;
        set => this.RaiseAndSetIfChanged(ref _ocrLanguage, value);
    }

    /// <summary>
    /// 信頼度閾値
    /// </summary>
    public double ConfidenceThreshold
    {
        get => _confidenceThreshold;
        set => this.RaiseAndSetIfChanged(ref _confidenceThreshold, value);
    }

    /// <summary>
    /// テキストフィルタリングを有効化
    /// </summary>
    public bool EnableTextFiltering
    {
        get => _enableTextFiltering;
        set => this.RaiseAndSetIfChanged(ref _enableTextFiltering, value);
    }

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
    /// 言語選択肢
    /// </summary>
    public IReadOnlyList<string> LanguageOptions { get; }

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
    /// OCRテストコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> TestOcrCommand { get; }

    #endregion

    #region メソッド

    /// <summary>
    /// 設定データから初期化
    /// </summary>
    private void InitializeFromSettings(OcrSettings settings)
    {
        _enableOcr = settings.EnableOcr;
        _ocrLanguage = settings.Language;
        _confidenceThreshold = settings.ConfidenceThreshold;
        _enableTextFiltering = settings.EnableTextFiltering;
    }

    /// <summary>
    /// 変更追跡を設定
    /// </summary>
    private void SetupChangeTracking()
    {
        // 主要プロパティの変更追跡
        this.WhenAnyValue(x => x.EnableOcr)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        this.WhenAnyValue(x => x.OcrLanguage)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        this.WhenAnyValue(x => x.ConfidenceThreshold)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
        
        this.WhenAnyValue(x => x.EnableTextFiltering)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
    }

    /// <summary>
    /// デフォルト値にリセット
    /// </summary>

    private void ResetToDefaults()
    {
        var defaultSettings = new OcrSettings();
        InitializeFromSettings(defaultSettings);
        HasChanges = true;
        _logger?.LogInformation("OCR設定をデフォルト値にリセットしました");
    }

    private void ToggleAdvancedSettings()
    {
        ShowAdvancedSettings = !ShowAdvancedSettings;
    }

    private void TestOcr()
    {
        _logger?.LogInformation("OCRテストを実行します");
        // TODO: OCRテスト機能の実装
    }

    /// <summary>
    /// 現在の設定データを取得
    /// </summary>
    public OcrSettings CurrentSettings => new()
    {
        EnableOcr = EnableOcr,
        Language = OcrLanguage,
        ConfidenceThreshold = ConfidenceThreshold,
        EnableTextFiltering = EnableTextFiltering
    };

    /// <summary>
    /// 設定データを更新
    /// </summary>
    /// <param name="settings">新しい設定データ</param>
    public void UpdateSettings(OcrSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        
        InitializeFromSettings(settings);
        HasChanges = false;
        _logger?.LogDebug("OCR設定を更新しました");
    }

    /// <summary>
    /// 設定のバリデーション
    /// </summary>
    /// <returns>バリデーション結果</returns>
    public bool ValidateSettings()
    {
        // 信頼度閾値の範囲チェック
        if (ConfidenceThreshold < 0.0 || ConfidenceThreshold > 1.0)
        {
            _logger?.LogWarning("信頼度閾値が有効範囲外です: {Threshold}", ConfidenceThreshold);
            return false;
        }

        // 言語設定のチェック
        if (EnableOcr && string.IsNullOrEmpty(OcrLanguage))
        {
            _logger?.LogWarning("OCRが有効ですが言語が設定されていません");
            return false;
        }

        // 使用可能言語の確認
        if (EnableOcr && !LanguageOptions.Contains(OcrLanguage))
        {
            _logger?.LogWarning("サポートされていない言語が設定されています: {Language}", OcrLanguage);
            return false;
        }

        return true;
    }

    #endregion
}
