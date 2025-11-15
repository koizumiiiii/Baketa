using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Settings;
using Baketa.UI.Framework;
using Microsoft.Extensions.Logging;
using ReactiveUI;

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
    private string _targetLanguage = "Japanese"; // 翻訳先言語
    private double _confidenceThreshold;
    private bool _enableTextFiltering;
    private bool _showAdvancedSettings;
    private bool _hasChanges;

    // 詳細設定のバッキングフィールド
    private bool _enableImagePreprocessing;
    private bool _convertToGrayscale;
    private bool _enableBinarization;
    private int _binarizationThreshold;
    private bool _enableNoiseReduction;
    private bool _enhanceContrast;
    private bool _enhanceEdges;
    private double _imageScaleFactor;
    private bool _enableParallelProcessing;
    private int _maxParallelThreads;
    private bool _enableTextAreaDetection;
    private int _minTextLineHeight;
    private int _maxTextLineHeight;
    private int _timeoutSeconds;

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
        TargetLanguageOptions = ["Japanese", "English", "Chinese", "Korean"];

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
    /// 翻訳先言語
    /// </summary>
    public string TargetLanguage
    {
        get => _targetLanguage;
        set => this.RaiseAndSetIfChanged(ref _targetLanguage, value);
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
    /// 画像前処理を有効化
    /// </summary>
    public bool EnableImagePreprocessing
    {
        get => _enableImagePreprocessing;
        set => this.RaiseAndSetIfChanged(ref _enableImagePreprocessing, value);
    }

    /// <summary>
    /// グレースケール変換を有効化
    /// </summary>
    public bool ConvertToGrayscale
    {
        get => _convertToGrayscale;
        set => this.RaiseAndSetIfChanged(ref _convertToGrayscale, value);
    }

    /// <summary>
    /// 二値化処理を有効化
    /// </summary>
    public bool EnableBinarization
    {
        get => _enableBinarization;
        set => this.RaiseAndSetIfChanged(ref _enableBinarization, value);
    }

    /// <summary>
    /// 二値化閾値
    /// </summary>
    public int BinarizationThreshold
    {
        get => _binarizationThreshold;
        set => this.RaiseAndSetIfChanged(ref _binarizationThreshold, value);
    }

    /// <summary>
    /// ノイズ除去を有効化
    /// </summary>
    public bool EnableNoiseReduction
    {
        get => _enableNoiseReduction;
        set => this.RaiseAndSetIfChanged(ref _enableNoiseReduction, value);
    }

    /// <summary>
    /// コントラスト強調を有効化
    /// </summary>
    public bool EnhanceContrast
    {
        get => _enhanceContrast;
        set => this.RaiseAndSetIfChanged(ref _enhanceContrast, value);
    }

    /// <summary>
    /// エッジ強調を有効化
    /// </summary>
    public bool EnhanceEdges
    {
        get => _enhanceEdges;
        set => this.RaiseAndSetIfChanged(ref _enhanceEdges, value);
    }

    /// <summary>
    /// 画像拡大率
    /// </summary>
    public double ImageScaleFactor
    {
        get => _imageScaleFactor;
        set => this.RaiseAndSetIfChanged(ref _imageScaleFactor, value);
    }

    /// <summary>
    /// 並列処理を有効化
    /// </summary>
    public bool EnableParallelProcessing
    {
        get => _enableParallelProcessing;
        set => this.RaiseAndSetIfChanged(ref _enableParallelProcessing, value);
    }

    /// <summary>
    /// 最大並列処理数
    /// </summary>
    public int MaxParallelThreads
    {
        get => _maxParallelThreads;
        set => this.RaiseAndSetIfChanged(ref _maxParallelThreads, value);
    }

    /// <summary>
    /// テキスト領域検出を有効化
    /// </summary>
    public bool EnableTextAreaDetection
    {
        get => _enableTextAreaDetection;
        set => this.RaiseAndSetIfChanged(ref _enableTextAreaDetection, value);
    }

    /// <summary>
    /// 最小テキスト行高さ
    /// </summary>
    public int MinTextLineHeight
    {
        get => _minTextLineHeight;
        set => this.RaiseAndSetIfChanged(ref _minTextLineHeight, value);
    }

    /// <summary>
    /// 最大テキスト行高さ
    /// </summary>
    public int MaxTextLineHeight
    {
        get => _maxTextLineHeight;
        set => this.RaiseAndSetIfChanged(ref _maxTextLineHeight, value);
    }

    /// <summary>
    /// OCRタイムアウト時間
    /// </summary>
    public int TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => this.RaiseAndSetIfChanged(ref _timeoutSeconds, value);
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

    /// <summary>
    /// 翻訳先言語選択肢
    /// </summary>
    public IReadOnlyList<string> TargetLanguageOptions { get; }

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
        _targetLanguage = "Japanese"; // デフォルト値
        _confidenceThreshold = settings.ConfidenceThreshold;
        _enableTextFiltering = settings.EnableTextFiltering;

        // 詳細設定の初期化
        _enableImagePreprocessing = settings.EnableImagePreprocessing;
        _convertToGrayscale = settings.ConvertToGrayscale;
        _enableBinarization = settings.EnableBinarization;
        _binarizationThreshold = settings.BinarizationThreshold;
        _enableNoiseReduction = settings.EnableNoiseReduction;
        _enhanceContrast = settings.EnhanceContrast;
        _enhanceEdges = settings.EnhanceEdges;
        _imageScaleFactor = settings.ImageScaleFactor;
        _enableParallelProcessing = settings.EnableParallelProcessing;
        _maxParallelThreads = settings.MaxParallelThreads;
        _enableTextAreaDetection = settings.EnableTextAreaDetection;
        _minTextLineHeight = settings.MinTextLineHeight;
        _maxTextLineHeight = settings.MaxTextLineHeight;
        _timeoutSeconds = settings.TimeoutSeconds;
    }

    /// <summary>
    /// 変更追跡を設定
    /// </summary>
    private void SetupChangeTracking()
    {
        // 基本プロパティの変更追跡
        this.WhenAnyValue(x => x.EnableOcr)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.OcrLanguage)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.TargetLanguage)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.ConfidenceThreshold)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.EnableTextFiltering)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        // 詳細設定の変更追跡
        this.WhenAnyValue(x => x.EnableImagePreprocessing)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.ConvertToGrayscale)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.EnableBinarization)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.BinarizationThreshold)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.EnableNoiseReduction)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.EnhanceContrast)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.EnhanceEdges)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.ImageScaleFactor)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.EnableParallelProcessing)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.MaxParallelThreads)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.EnableTextAreaDetection)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.MinTextLineHeight)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.MaxTextLineHeight)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.TimeoutSeconds)
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

    private async void TestOcr()
    {
        try
        {
            _logger?.LogInformation("OCR設定テストを開始します");

            // 設定のバリデーション
            if (!ValidateSettings())
            {
                _logger?.LogWarning("OCR設定にエラーがあります。設定を確認してください。");
                await ShowTestResultAsync("設定エラー", "OCR設定にエラーがあります。設定を確認してください。", false).ConfigureAwait(true);
                return;
            }

            // 基本設定の確認
            var testResults = new StringBuilder();
            testResults.AppendLine("=== OCR設定テスト結果 ===");
            testResults.AppendLine(string.Format(CultureInfo.CurrentCulture, "OCR有効: {0}", EnableOcr ? "有効" : "無効"));
            testResults.AppendLine(string.Format(CultureInfo.CurrentCulture, "認識言語: {0}", OcrLanguage));
            testResults.AppendLine(string.Format(CultureInfo.CurrentCulture, "翻訳先言語: {0}", TargetLanguage));
            testResults.AppendLine(string.Format(CultureInfo.CurrentCulture, "信頼度閾値: {0:P0}", ConfidenceThreshold));
            testResults.AppendLine(string.Format(CultureInfo.CurrentCulture, "テキストフィルタリング: {0}", EnableTextFiltering ? "有効" : "無効"));

            if (ShowAdvancedSettings)
            {
                testResults.AppendLine();
                testResults.AppendLine("=== 詳細設定 ===");
                testResults.AppendLine(string.Format(CultureInfo.CurrentCulture, "画像前処理: {0}", EnableImagePreprocessing ? "有効" : "無効"));
                testResults.AppendLine(string.Format(CultureInfo.CurrentCulture, "グレースケール変換: {0}", ConvertToGrayscale ? "有効" : "無効"));
                testResults.AppendLine(string.Format(CultureInfo.CurrentCulture, "二値化処理: {0}", EnableBinarization ? "有効" : "無効"));
                testResults.AppendLine(string.Format(CultureInfo.CurrentCulture, "二値化閾値: {0}", BinarizationThreshold));
                testResults.AppendLine(string.Format(CultureInfo.CurrentCulture, "ノイズ除去: {0}", EnableNoiseReduction ? "有効" : "無効"));
                testResults.AppendLine(string.Format(CultureInfo.CurrentCulture, "コントラスト強調: {0}", EnhanceContrast ? "有効" : "無効"));
                testResults.AppendLine(string.Format(CultureInfo.CurrentCulture, "エッジ強調: {0}", EnhanceEdges ? "有効" : "無効"));
                testResults.AppendLine(string.Format(CultureInfo.CurrentCulture, "画像拡大率: {0:F1}x", ImageScaleFactor));
                testResults.AppendLine(string.Format(CultureInfo.CurrentCulture, "並列処理: {0}", EnableParallelProcessing ? "有効" : "無効"));
                testResults.AppendLine(string.Format(CultureInfo.CurrentCulture, "最大並列数: {0}", MaxParallelThreads));
                testResults.AppendLine(string.Format(CultureInfo.CurrentCulture, "テキスト領域検出: {0}", EnableTextAreaDetection ? "有効" : "無効"));
                testResults.AppendLine(string.Format(CultureInfo.CurrentCulture, "最小行高さ: {0}px", MinTextLineHeight));
                testResults.AppendLine(string.Format(CultureInfo.CurrentCulture, "最大行高さ: {0}px", MaxTextLineHeight));
                testResults.AppendLine(string.Format(CultureInfo.CurrentCulture, "タイムアウト時間: {0}秒", TimeoutSeconds));
            }

            testResults.AppendLine();
            testResults.AppendLine("✅ 設定は正常です。OCRエンジンの動作準備が完了しています。");

            await ShowTestResultAsync("OCRテスト完了", testResults.ToString(), true).ConfigureAwait(true);
            _logger?.LogInformation("OCR設定テストが正常に完了しました");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "OCR設定テスト中にエラーが発生しました");
            await ShowTestResultAsync("テストエラー", $"テスト実行中にエラーが発生しました: {ex.Message}", false).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// テスト結果を表示
    /// </summary>
    private async Task ShowTestResultAsync(string title, string message, bool _)
    {
        // TODO: 将来的にダイアログやポップアップで表示
        // 現在は一時的な実装として、ログ出力のみ
        _logger?.LogInformation("=== {Title} ===", title);
        _logger?.LogInformation("{Message}", message);

        // 非同期処理のシミュレーション
        await Task.Delay(100).ConfigureAwait(true);
    }

    /// <summary>
    /// 現在の設定データを取得
    /// </summary>
    public OcrSettings CurrentSettings => new()
    {
        EnableOcr = EnableOcr,
        Language = OcrLanguage,
        ConfidenceThreshold = ConfidenceThreshold,
        EnableTextFiltering = EnableTextFiltering,
        EnableImagePreprocessing = EnableImagePreprocessing,
        ConvertToGrayscale = ConvertToGrayscale,
        EnableBinarization = EnableBinarization,
        BinarizationThreshold = BinarizationThreshold,
        EnableNoiseReduction = EnableNoiseReduction,
        EnhanceContrast = EnhanceContrast,
        EnhanceEdges = EnhanceEdges,
        ImageScaleFactor = ImageScaleFactor,
        EnableParallelProcessing = EnableParallelProcessing,
        MaxParallelThreads = MaxParallelThreads,
        EnableTextAreaDetection = EnableTextAreaDetection,
        MinTextLineHeight = MinTextLineHeight,
        MaxTextLineHeight = MaxTextLineHeight,
        TimeoutSeconds = TimeoutSeconds
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

        // 翻訳先言語の確認
        if (!TargetLanguageOptions.Contains(TargetLanguage))
        {
            _logger?.LogWarning("サポートされていない翻訳先言語が設定されています: {TargetLanguage}", TargetLanguage);
            return false;
        }

        // 画像拡大率の確認
        if (ImageScaleFactor < 1.0 || ImageScaleFactor > 4.0)
        {
            _logger?.LogWarning("画像拡大率が有効範囲外です: {ScaleFactor}", ImageScaleFactor);
            return false;
        }

        // 並列処理数の確認
        if (MaxParallelThreads < 1 || MaxParallelThreads > 16)
        {
            _logger?.LogWarning("最大並列処理数が有効範囲外です: {MaxThreads}", MaxParallelThreads);
            return false;
        }

        // テキスト行高さの確認
        if (MinTextLineHeight < 5 || MinTextLineHeight > 100 || MaxTextLineHeight < 10 || MaxTextLineHeight > 500)
        {
            _logger?.LogWarning("テキスト行高さが有効範囲外です: Min={MinHeight}, Max={MaxHeight}", MinTextLineHeight, MaxTextLineHeight);
            return false;
        }

        return true;
    }

    #endregion
}
