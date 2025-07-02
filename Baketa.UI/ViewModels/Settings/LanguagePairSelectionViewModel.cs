using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReactiveUI;
using DynamicData;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Configuration;
using Baketa.UI.Framework;
using Baketa.UI.Models;
using Baketa.UI.Services;
using StatusUpdate = Baketa.UI.Services.TranslationEngineStatusUpdate;

namespace Baketa.UI.ViewModels.Settings;

/// <summary>
/// 言語ペア選択ViewModel
/// </summary>
public sealed class LanguagePairSelectionViewModel : Framework.ViewModelBase, IActivatableViewModel
{
    private readonly ITranslationEngineStatusService _statusService;
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<LanguagePairSelectionViewModel> _logger;
    private readonly TranslationUIOptions _options;
    private readonly CompositeDisposable _disposables = [];
    
    private readonly SourceList<LanguagePairConfiguration> _languagePairsSource = new();
    private readonly ReadOnlyObservableCollection<LanguagePairConfiguration> _languagePairs;

    private LanguagePairConfiguration? _selectedLanguagePair;
    private ChineseVariant _selectedChineseVariant = ChineseVariant.Auto;
    private bool _isChineseRelatedPair;
    private bool _isLoading;
    private string _filterText = string.Empty;

    /// <summary>
    /// ViewModel活性化管理
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CS0109:Member does not hide an accessible member", Justification = "Intentional shadowing for independent activation management")]
    public new ViewModelActivator Activator { get; } = new();

    /// <summary>
    /// 利用可能な言語ペア一覧
    /// </summary>
    public ReadOnlyObservableCollection<LanguagePairConfiguration> LanguagePairs => _languagePairs;

    /// <summary>
    /// 選択された言語ペア
    /// </summary>
    public LanguagePairConfiguration? SelectedLanguagePair
    {
        get => _selectedLanguagePair;
        set => this.RaiseAndSetIfChanged(ref _selectedLanguagePair, value);
    }

    /// <summary>
    /// 選択された中国語変種
    /// </summary>
    public ChineseVariant SelectedChineseVariant
    {
        get => _selectedChineseVariant;
        set => this.RaiseAndSetIfChanged(ref _selectedChineseVariant, value);
    }

    /// <summary>
    /// 中国語関連の言語ペアかどうか
    /// </summary>
    public bool IsChineseRelatedPair
    {
        get => _isChineseRelatedPair;
        private set => this.RaiseAndSetIfChanged(ref _isChineseRelatedPair, value);
    }

    /// <summary>
    /// ローディング中かどうか
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CS0109:Member does not hide an accessible member", Justification = "Intentional shadowing for independent loading state management")]
    public new bool IsLoading
    {
        get => _isLoading;
        private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    /// <summary>
    /// フィルターテキスト
    /// </summary>
    public string FilterText
    {
        get => _filterText;
        set => this.RaiseAndSetIfChanged(ref _filterText, value);
    }

    /// <summary>
    /// 利用可能な中国語変種一覧
    /// </summary>
    public IEnumerable<ChineseVariantItem> AvailableChineseVariants { get; }

    /// <summary>
    /// 利用可能な言語一覧
    /// </summary>
    public IEnumerable<LanguageInfo> AvailableLanguages => Models.AvailableLanguages.SupportedLanguages;

    /// <summary>
    /// 言語ペア選択コマンド
    /// </summary>
    public ReactiveCommand<LanguagePairConfiguration, Unit> SelectLanguagePairCommand { get; }

    /// <summary>
    /// 中国語変種選択コマンド
    /// </summary>
    public ReactiveCommand<ChineseVariant, Unit> SelectChineseVariantCommand { get; }

    /// <summary>
    /// 言語ペア有効/無効切り替えコマンド
    /// </summary>
    public ReactiveCommand<LanguagePairConfiguration, Unit> ToggleLanguagePairCommand { get; }

    /// <summary>
    /// フィルタークリアコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ClearFilterCommand { get; }

    /// <summary>
    /// 言語ペア更新コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> RefreshLanguagePairsCommand { get; }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public LanguagePairSelectionViewModel(
        ITranslationEngineStatusService statusService,
        ILocalizationService localizationService,
        INotificationService notificationService,
        IOptions<TranslationUIOptions> options,
        ILogger<LanguagePairSelectionViewModel> logger,
        IEventAggregator eventAggregator) : base(eventAggregator)
    {
        ArgumentNullException.ThrowIfNull(statusService);
        ArgumentNullException.ThrowIfNull(localizationService);
        ArgumentNullException.ThrowIfNull(notificationService);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        
        _statusService = statusService;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _logger = logger;
        _options = options.Value;

        // 中国語変種一覧の作成
        AvailableChineseVariants = CreateChineseVariantsList();

        // 初期設定
        SelectedChineseVariant = ParseChineseVariantFromString(_options.DefaultChineseVariant);

        // フィルタリング可能な言語ペアコレクションの設定
        var filterPredicate = this.WhenAnyValue(x => x.FilterText)
            .Throttle(TimeSpan.FromMilliseconds(300))
            .DistinctUntilChanged()
            .Select(CreateFilterPredicate);

        _languagePairsSource
            .Connect()
            .Filter(filterPredicate)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out _languagePairs)
            .Subscribe()
            .DisposeWith(_disposables);

        // コマンドの作成
        var canExecute = this.WhenAnyValue(x => x.IsLoading).Select(loading => !loading);
        
        SelectLanguagePairCommand = ReactiveCommand.CreateFromTask<LanguagePairConfiguration, Unit>(
            async languagePair => 
            {
                await SelectLanguagePairAsync(languagePair).ConfigureAwait(false);
                return Unit.Default;
            }, canExecute);

        SelectChineseVariantCommand = ReactiveCommand.CreateFromTask<ChineseVariant, Unit>(
            async variant => 
            {
                await SelectChineseVariantAsync(variant).ConfigureAwait(false);
                return Unit.Default;
            }, canExecute);

        ToggleLanguagePairCommand = ReactiveCommand.CreateFromTask<LanguagePairConfiguration, Unit>(
            async languagePair => 
            {
                await ToggleLanguagePairAsync(languagePair).ConfigureAwait(false);
                return Unit.Default;
            }, canExecute);

        ClearFilterCommand = ReactiveCommand.Create<Unit, Unit>(_ => 
        {
            FilterText = string.Empty;
            return Unit.Default;
        });

        RefreshLanguagePairsCommand = ReactiveCommand.CreateFromTask<Unit, Unit>(async _ => 
        {
            await RefreshLanguagePairsAsync().ConfigureAwait(false);
            return Unit.Default;
        }, canExecute);

        // ViewModel活性化時の処理
        this.WhenActivated(disposables =>
        {
            // 言語ペア選択時の処理
            this.WhenAnyValue(x => x.SelectedLanguagePair)
                .WhereNotNull()
                .Subscribe(OnLanguagePairSelected)
                .DisposeWith(disposables);

            // アプリケーション言語変更の監視
            _localizationService.CurrentLanguageChanged
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => RefreshLanguageDisplayNames())
                .DisposeWith(disposables);

            // エンジン状態変更の監視
            _statusService.StatusUpdates
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(update => OnEngineStatusUpdate(update))
                .DisposeWith(disposables);

            // 初期データ読み込み
            RefreshLanguagePairsCommand.Execute().Subscribe()
                .DisposeWith(disposables);

            _logger.LogDebug("LanguagePairSelectionViewModel activated");
        });

        _logger.LogInformation("LanguagePairSelectionViewModel created");
    }

    /// <summary>
    /// 言語ペア選択処理
    /// </summary>
    private async Task SelectLanguagePairAsync(LanguagePairConfiguration languagePair)
    {
        if (SelectedLanguagePair == languagePair)
            return;

        _logger.LogInformation("Selecting language pair: {Pair}", languagePair.LanguagePairKey);

        try
        {
            IsLoading = true;

            // 言語ペアの利用可能性チェック
            if (!languagePair.IsEnabled)
            {
                await _notificationService.ShowWarningAsync(
                "言語ペア選択",
                "選択された言語ペアは現在利用できません。").ConfigureAwait(false);
                return;
            }

            // ダウンロードが必要な場合の確認
            if (languagePair.RequiresDownload)
            {
                await _notificationService.ShowInfoAsync(
                "モデルダウンロード",
                $"{languagePair.DisplayName}の翻訳モデルのダウンロードが必要です。").ConfigureAwait(false);
                // TODO: 実際のダウンロード処理の実装
            }

            SelectedLanguagePair = languagePair;

            // 成功通知
            if (_options.EnableNotifications)
            {
                await _notificationService.ShowSuccessAsync(
                    "言語ペア選択",
                    $"{languagePair.DisplayName}を選択しました。").ConfigureAwait(false);
            }

            _logger.LogInformation("Language pair selected successfully: {Pair}", languagePair.LanguagePairKey);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation when selecting language pair: {Pair}", languagePair.LanguagePairKey);
            await _notificationService.ShowErrorAsync(
                "言語ペア選択エラー",
                $"選択できない言語ペアです: {ex.Message}").ConfigureAwait(false);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Language pair selection was cancelled: {Pair}", languagePair.LanguagePairKey);
            await _notificationService.ShowWarningAsync(
                "選択キャンセル",
                "言語ペアの選択がキャンセルされました。").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not (InvalidOperationException or TaskCanceledException))
        {
            _logger.LogError(ex, "Unexpected error when selecting language pair: {Pair}", languagePair.LanguagePairKey);
            await _notificationService.ShowErrorAsync(
                "言語ペア選択エラー",
                $"予期しないエラーが発生しました: {ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 中国語変種選択処理
    /// </summary>
    private async Task SelectChineseVariantAsync(ChineseVariant variant)
    {
        if (SelectedChineseVariant == variant)
            return;

        _logger.LogInformation("Selecting Chinese variant: {Variant}", variant);

        try
        {
            SelectedChineseVariant = variant;

            // 選択中の言語ペアの中国語変種も更新
            if (SelectedLanguagePair is { IsChineseRelated: true })
            {
                SelectedLanguagePair.ChineseVariant = variant;
            }

            // 成功通知
            if (_options.EnableNotifications)
            {
                var variantName = GetChineseVariantDisplayName(variant);
                await _notificationService.ShowSuccessAsync(
                    "中国語変種選択",
                    $"{variantName}を選択しました。").ConfigureAwait(false);
            }

            _logger.LogInformation("Chinese variant selected successfully: {Variant}", variant);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid Chinese variant selection: {Variant}", variant);
            await _notificationService.ShowErrorAsync(
                "中国語変種選択エラー",
                $"無効な中国語変種です: {ex.Message}").ConfigureAwait(false);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Unsupported Chinese variant: {Variant}", variant);
            await _notificationService.ShowErrorAsync(
                "非対応変種",
                $"この中国語変種はサポートされていません: {ex.Message}").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not (ArgumentException or NotSupportedException))
        {
            _logger.LogError(ex, "Unexpected error when selecting Chinese variant: {Variant}", variant);
            await _notificationService.ShowErrorAsync(
                "中国語変種選択エラー",
                $"予期しないエラーが発生しました: {ex.Message}").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 言語ペア有効/無効切り替え処理
    /// </summary>
    private async Task ToggleLanguagePairAsync(LanguagePairConfiguration languagePair)
    {
        try
        {
            languagePair.IsEnabled = !languagePair.IsEnabled;

            var status = languagePair.IsEnabled ? "有効" : "無効";
            await _notificationService.ShowInfoAsync(
                "言語ペア設定",
                $"{languagePair.DisplayName}を{status}にしました。").ConfigureAwait(false);

            _logger.LogInformation("Language pair {Pair} toggled to {Status}", 
                languagePair.LanguagePairKey, status);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation when toggling language pair: {Pair}", languagePair.LanguagePairKey);
            await _notificationService.ShowErrorAsync(
                "設定変更エラー",
                $"設定を変更できません: {ex.Message}").ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Unauthorized access when toggling language pair: {Pair}", languagePair.LanguagePairKey);
            await _notificationService.ShowErrorAsync(
                "アクセスエラー",
                "設定を変更する権限がありません。").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not (InvalidOperationException or UnauthorizedAccessException))
        {
            _logger.LogError(ex, "Unexpected error when toggling language pair: {Pair}", languagePair.LanguagePairKey);
            await _notificationService.ShowErrorAsync(
                "設定変更エラー",
                $"予期しないエラーが発生しました: {ex.Message}").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 言語ペア一覧の更新
    /// </summary>
    private async Task RefreshLanguagePairsAsync()
    {
        try
        {
            IsLoading = true;
            
            var languagePairsArray = CreateLanguagePairConfigurations().ToArray();
            
            _languagePairsSource.Clear();
            _languagePairsSource.AddRange(languagePairsArray);

            // デフォルト言語ペアの選択
            var defaultPair = languagePairsArray.FirstOrDefault(p => 
            string.Equals(p.LanguagePairKey, _options.DefaultLanguagePair, StringComparison.Ordinal));
            
            if (defaultPair != null)
            {
                SelectedLanguagePair = defaultPair;
            }

            _logger.LogInformation("Language pairs refreshed. Count: {Count}", languagePairsArray.Length);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Language pairs refresh was cancelled");
            await _notificationService.ShowWarningAsync(
                "更新キャンセル",
                "言語ペアの更新がキャンセルされました。").ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation during language pairs refresh");
            await _notificationService.ShowErrorAsync(
                "操作エラー",
                "現在の状態では言語ペアを更新できません。").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not (TaskCanceledException or InvalidOperationException))
        {
            _logger.LogError(ex, "Unexpected error during language pairs refresh");
            await _notificationService.ShowErrorAsync(
                "言語ペア更新エラー",
                $"予期しないエラーが発生しました: {ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 言語ペア選択時の処理
    /// </summary>
    private void OnLanguagePairSelected(LanguagePairConfiguration languagePair)
    {
        IsChineseRelatedPair = languagePair.IsChineseRelated;
        
        if (IsChineseRelatedPair)
        {
            SelectedChineseVariant = languagePair.ChineseVariant;
        }

        _logger.LogDebug("Language pair selected: {Pair}, IsChineseRelated: {IsChineseRelated}", 
            languagePair.LanguagePairKey, IsChineseRelatedPair);
    }

    /// <summary>
    /// エンジン状態更新時の処理
    /// </summary>
    private void OnEngineStatusUpdate(StatusUpdate update)
    {
        // エンジン状態に応じて言語ペアの利用可否を更新
        foreach (var languagePair in _languagePairsSource.Items)
        {
            UpdateLanguagePairAvailability(languagePair, update);
        }
    }

    /// <summary>
    /// 言語ペアの利用可否を更新
    /// </summary>
    private void UpdateLanguagePairAvailability(LanguagePairConfiguration languagePair, StatusUpdate update)
    {
        // CloudOnlyエンジン使用の言語ペアの場合
        if (languagePair.SelectedEngine == "CloudOnly" && update.EngineName == "CloudOnly")
        {
            var isCloudHealthy = _statusService.CloudEngineStatus.IsHealthy;
            
            if (!isCloudHealthy && languagePair.IsEnabled)
            {
                languagePair.IsEnabled = false;
                languagePair.Description = "CloudOnlyエンジンがオフラインのため利用できません";
            }
            else if (isCloudHealthy && !languagePair.IsEnabled)
            {
                languagePair.IsEnabled = true;
                UpdateLanguagePairDescription(languagePair);
            }
        }
    }

    /// <summary>
    /// 言語表示名の更新
    /// </summary>
    private void RefreshLanguageDisplayNames()
    {
        foreach (var languagePair in _languagePairsSource.Items)
        {
            UpdateLanguagePairDescription(languagePair);
        }
    }

    /// <summary>
    /// 言語ペア設定一覧の作成
    /// </summary>
    private IEnumerable<LanguagePairConfiguration> CreateLanguagePairConfigurations()
    {
        List<LanguagePairConfiguration> configurations = [];

        foreach (var pairKey in Models.AvailableLanguages.SupportedLanguagePairs)
        {
            var parts = pairKey.Split('-');
            if (parts.Length < 2) continue;

            var sourceCode = parts[0];
            var targetCode = string.Join("-", parts[1..]);

            var sourceLanguage = Models.AvailableLanguages.SupportedLanguages
                .FirstOrDefault(l => l.Code == sourceCode);
            var targetLanguage = Models.AvailableLanguages.SupportedLanguages
                .FirstOrDefault(l => l.Code == targetCode);

            if (sourceLanguage == null || targetLanguage == null) continue;

            var configuration = new LanguagePairConfiguration
            {
                SourceLanguage = sourceCode,
                TargetLanguage = targetCode,
                SourceLanguageDisplay = sourceLanguage.DisplayName,
                TargetLanguageDisplay = targetLanguage.DisplayName,
                Priority = GetPairPriority(pairKey),
                Strategy = GetDefaultStrategy(pairKey),
                ChineseVariant = GetDefaultChineseVariant(sourceCode, targetCode),
                IsEnabled = true
            };

            UpdateLanguagePairDescription(configuration);
            configurations.Add(configuration);
        }

        return [.. configurations.OrderBy(c => c.Priority)];
    }

    /// <summary>
    /// 言語ペアの説明を更新
    /// </summary>
    private static void UpdateLanguagePairDescription(LanguagePairConfiguration configuration)
    {
        var strategyText = configuration.Strategy == TranslationStrategy.TwoStage ? "（2段階翻訳）" : "";
        var variantText = configuration.IsChineseRelated && configuration.ChineseVariant != ChineseVariant.Auto 
            ? $" - {GetChineseVariantDisplayName(configuration.ChineseVariant)}" : "";
        
        configuration.Description = $"{configuration.DisplayName}{strategyText}{variantText} - {configuration.LatencyDisplayText}";
    }

    /// <summary>
    /// フィルター述語の作成
    /// </summary>
    private static Func<LanguagePairConfiguration, bool> CreateFilterPredicate(string filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText))
            return _ => true;
        return pair => 
            pair.SourceLanguageDisplay.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
            pair.TargetLanguageDisplay.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
            pair.LanguagePairKey.Contains(filterText, StringComparison.Ordinal) ||
            pair.Description.Contains(filterText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 中国語変種一覧の作成
    /// </summary>
    private static IEnumerable<ChineseVariantItem> CreateChineseVariantsList()
    {
        return [
            new ChineseVariantItem(ChineseVariant.Simplified, "简体字", "簡体字中国語", "🇨🇳"),
            new ChineseVariantItem(ChineseVariant.Traditional, "繁體字", "繁体字中国語", "🇹🇼")
            // 初期リリースではAuto/Cantoneseは除外
            // new ChineseVariantItem(ChineseVariant.Auto, "自动", "自動選択", "🤖"),
            // new ChineseVariantItem(ChineseVariant.Cantonese, "粵語", "広東語", "🇭🇰")
        ];
    }

    /// <summary>
    /// 言語ペアの優先度取得
    /// </summary>
    private static int GetPairPriority(string pairKey)
    {
        return pairKey switch
        {
            "ja-en" => 1,
            "en-ja" => 2,
            "zh-en" => 3,
            "en-zh" => 4,
            "zh-ja" => 5,
            "ja-zh" => 6,
            _ => 10
        };
    }

    /// <summary>
    /// デフォルト翻訳戦略取得
    /// </summary>
    private static TranslationStrategy GetDefaultStrategy(string pairKey)
    {
        return pairKey switch
        {
            "ja-zh" => TranslationStrategy.TwoStage, // 日本語→中国語は2段階翻訳
            _ => TranslationStrategy.Direct
        };
    }

    /// <summary>
    /// デフォルト中国語変種取得
    /// </summary>
    private static ChineseVariant GetDefaultChineseVariant(string sourceCode, string targetCode)
    {
        if (sourceCode.StartsWith("zh", StringComparison.Ordinal) || targetCode.StartsWith("zh", StringComparison.Ordinal))
        {
            return ChineseVariant.Simplified; // 初期リリースでは簡体字をデフォルト
        }
        return ChineseVariant.Auto;
    }

    /// <summary>
    /// 中国語変種表示名取得
    /// </summary>
    private static string GetChineseVariantDisplayName(ChineseVariant variant)
    {
        return variant switch
        {
            ChineseVariant.Simplified => "簡体字",
            ChineseVariant.Traditional => "繁体字",
            ChineseVariant.Auto => "自動選択",
            ChineseVariant.Cantonese => "広東語",
            _ => "不明"
        };
    }

    /// <summary>
    /// 文字列から中国語変種をパース
    /// </summary>
    private static ChineseVariant ParseChineseVariantFromString(string variantString)
    {
        return variantString?.ToUpperInvariant() switch
        {
            "SIMPLIFIED" => ChineseVariant.Simplified,
            "TRADITIONAL" => ChineseVariant.Traditional,
            "AUTO" => ChineseVariant.Auto,
            "CANTONESE" => ChineseVariant.Cantonese,
            _ => ChineseVariant.Simplified // デフォルト
        };
    }

    /// <summary>
    /// リソースを解放します
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposables.Dispose();
            _languagePairsSource.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// 中国語変種選択項目
/// </summary>
public sealed record ChineseVariantItem(
    ChineseVariant Variant,
    string NativeName,
    string DisplayName,
    string Flag);
