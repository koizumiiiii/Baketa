using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReactiveUI;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Configuration;
using Baketa.UI.Framework;
using Baketa.UI.Models;
using Baketa.UI.Services;

namespace Baketa.UI.ViewModels.Settings;

/// <summary>
/// 翻訳設定統合ViewModel
/// </summary>
public sealed class TranslationSettingsViewModel : Framework.ViewModelBase, IActivatableViewModel
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<TranslationSettingsViewModel> _logger;
    private readonly TranslationUIOptions _options;
    private readonly SettingsFileManager _settingsFileManager;
    private readonly IFileDialogService _fileDialogService;
    private readonly SettingsExportImportService _exportImportService;
    private readonly CompositeDisposable _disposables = [];

    private bool _hasChanges;
    private bool _isSaving;
    private bool _isLoading;
    private string _statusMessage = string.Empty;
    private DateTime _lastSaved = DateTime.Now;

    /// <summary>
    /// ViewModel活性化管理
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CS0109:Member does not hide an accessible member", Justification = "Intentional shadowing for independent activation management")]
    public new ViewModelActivator Activator { get; } = new();

    /// <summary>
    /// エンジン選択ViewModel
    /// </summary>
    public EngineSelectionViewModel EngineSelection { get; }

    /// <summary>
    /// 言語ペア選択ViewModel
    /// </summary>
    public LanguagePairSelectionViewModel LanguagePairSelection { get; }

    /// <summary>
    /// 翻訳戦略ViewModel
    /// </summary>
    public TranslationStrategyViewModel TranslationStrategy { get; }

    /// <summary>
    /// エンジン状態ViewModel
    /// </summary>
    public EngineStatusViewModel EngineStatus { get; }

    /// <summary>
    /// 変更があるかどうか
    /// </summary>
    public bool HasChanges
    {
        get => _hasChanges;
        private set => this.RaiseAndSetIfChanged(ref _hasChanges, value);
    }

    /// <summary>
    /// 保存中かどうか
    /// </summary>
    public bool IsSaving
    {
        get => _isSaving;
        private set => this.RaiseAndSetIfChanged(ref _isSaving, value);
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
    /// 状態メッセージ
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    /// <summary>
    /// 最後に保存された時刻
    /// </summary>
    public DateTime LastSaved
    {
        get => _lastSaved;
        private set => this.RaiseAndSetIfChanged(ref _lastSaved, value);
    }

    /// <summary>
    /// 現在の設定サマリー
    /// </summary>
    public TranslationSettingsSummary CurrentSettings => CreateSettingsSummary();

    /// <summary>
    /// 設定保存コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }

    /// <summary>
    /// 設定読み込みコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> LoadCommand { get; }

    /// <summary>
    /// 設定リセットコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ResetCommand { get; }

    /// <summary>
    /// 設定エクスポートコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ExportCommand { get; }

    /// <summary>
    /// 設定インポートコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ImportCommand { get; }

    /// <summary>
    /// 変更破棄コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> DiscardChangesCommand { get; }

    /// <summary>
    /// ヘルプ表示コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ShowHelpCommand { get; }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public TranslationSettingsViewModel(
        EngineSelectionViewModel engineSelection,
        LanguagePairSelectionViewModel languagePairSelection,
        TranslationStrategyViewModel translationStrategy,
        EngineStatusViewModel engineStatus,
        INotificationService notificationService,
        SettingsFileManager settingsFileManager,
        IFileDialogService fileDialogService,
        SettingsExportImportService exportImportService,
        IOptions<TranslationUIOptions> options,
        ILogger<TranslationSettingsViewModel> logger,
        IEventAggregator eventAggregator) : base(eventAggregator)
    {
        ArgumentNullException.ThrowIfNull(engineSelection);
        ArgumentNullException.ThrowIfNull(languagePairSelection);
        ArgumentNullException.ThrowIfNull(translationStrategy);
        ArgumentNullException.ThrowIfNull(engineStatus);
        ArgumentNullException.ThrowIfNull(notificationService);
        ArgumentNullException.ThrowIfNull(settingsFileManager);
        ArgumentNullException.ThrowIfNull(fileDialogService);
        ArgumentNullException.ThrowIfNull(exportImportService);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        
        EngineSelection = engineSelection;
        LanguagePairSelection = languagePairSelection;
        TranslationStrategy = translationStrategy;
        EngineStatus = engineStatus;
        _notificationService = notificationService;
        _settingsFileManager = settingsFileManager;
        _fileDialogService = fileDialogService;
        _exportImportService = exportImportService;
        _logger = logger;
        _options = options.Value;

        // 初期状態メッセージ
        StatusMessage = "設定準備完了";

        // コマンドの作成
        var canSave = this.WhenAnyValue(
            x => x.HasChanges,
            x => x.IsSaving,
            x => x.IsLoading,
            (hasChanges, saving, loading) => hasChanges && !saving && !loading);

        var canExecute = this.WhenAnyValue(
            x => x.IsSaving,
            x => x.IsLoading,
            (saving, loading) => !saving && !loading);

        SaveCommand = ReactiveCommand.CreateFromTask(SaveSettingsAsync, canSave);
        LoadCommand = ReactiveCommand.CreateFromTask(LoadSettingsAsync, canExecute);
        ResetCommand = ReactiveCommand.CreateFromTask(ResetSettingsAsync, canExecute);
        ExportCommand = ReactiveCommand.CreateFromTask(ExportSettingsAsync, canExecute);
        ImportCommand = ReactiveCommand.CreateFromTask(ImportSettingsAsync, canExecute);
        
        var canDiscard = this.WhenAnyValue(x => x.HasChanges);
        DiscardChangesCommand = ReactiveCommand.CreateFromTask(DiscardChangesAsync, canDiscard);
        
        ShowHelpCommand = ReactiveCommand.Create(ShowHelp);

        // ViewModel活性化時の処理
        this.WhenActivated(disposables =>
        {
            // 子ViewModelの変更監視
            SetupChangeDetection().DisposeWith(disposables);

            // 言語ペア変更時の連携処理
            SetupLanguagePairIntegration().DisposeWith(disposables);

            // 自動保存機能
            if (_options.AutoSaveSettings)
            {
                SetupAutoSave().DisposeWith(disposables);
            }

            // 初期設定読み込み
            LoadCommand.Execute().Subscribe().DisposeWith(disposables);

            _logger.LogDebug("TranslationSettingsViewModel activated");
        });

        _logger.LogInformation("TranslationSettingsViewModel created");
    }

    /// <summary>
    /// 設定保存処理
    /// </summary>
    private async Task SaveSettingsAsync()
    {
        try
        {
            IsSaving = true;
            StatusMessage = "設定を保存中...";

            // 設定の妥当性チェック
            var validationResult = ValidateCurrentSettings();
            if (!validationResult.IsValid)
            {
                await _notificationService.ShowErrorAsync(
                "設定エラー",
                $"設定に問題があります: {validationResult.ErrorMessage}").ConfigureAwait(false);
                return;
            }

            // 各ViewModelの設定を保存
            // TODO: 実際の設定保存ロジックを実装
            await SaveEngineSettingsAsync().ConfigureAwait(false);
            await SaveLanguagePairSettingsAsync().ConfigureAwait(false);
            await SaveStrategySettingsAsync().ConfigureAwait(false);

            HasChanges = false;
            LastSaved = DateTime.Now;
            StatusMessage = "設定を保存しました";

            if (_options.EnableNotifications)
            {
                await _notificationService.ShowSuccessAsync(
                    "設定保存",
                    "翻訳設定を正常に保存しました。").ConfigureAwait(false);
            }

            _logger.LogInformation("Translation settings saved successfully");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Unauthorized access when saving translation settings");
            StatusMessage = "アクセス拒否";
            await _notificationService.ShowErrorAsync(
                "アクセスエラー",
                "設定ファイルへの書き込みが拒否されました。管理者権限が必要の可能性があります。").ConfigureAwait(false);
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogError(ex, "Settings directory not found when saving translation settings");
            StatusMessage = "フォルダーが見つかりません";
            await _notificationService.ShowErrorAsync(
                "フォルダーエラー",
                "設定フォルダーが存在しません。アプリケーションを再インストールしてください。").ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error when saving translation settings");
            StatusMessage = "ファイル書き込みエラー";
            await _notificationService.ShowErrorAsync(
                "ファイルエラー",
                "設定ファイルの書き込みに失敗しました。ディスク容量を確認してください。").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not (UnauthorizedAccessException or DirectoryNotFoundException or IOException))
        {
            _logger.LogError(ex, "Unexpected error when saving translation settings");
            StatusMessage = "予期しないエラー";
            await _notificationService.ShowErrorAsync(
                "保存エラー",
                $"予期しないエラーが発生しました: {ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// 設定読み込み処理
    /// </summary>
    private async Task LoadSettingsAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "設定を読み込み中...";

            // TODO: 実際の設定読み込みロジックを実装
            await LoadEngineSettingsAsync().ConfigureAwait(false);
            await LoadLanguagePairSettingsAsync().ConfigureAwait(false);
            await LoadStrategySettingsAsync().ConfigureAwait(false);

            HasChanges = false;
            StatusMessage = "設定を読み込みました";

            _logger.LogInformation("Translation settings loaded successfully");
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "Settings file not found when loading translation settings");
            StatusMessage = "デフォルト設定を使用";
            await _notificationService.ShowInfoAsync(
                "設定ファイルなし",
                "設定ファイルが見つからないため、デフォルト設定を使用します。").ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Unauthorized access when loading translation settings");
            StatusMessage = "アクセス拒否";
            await _notificationService.ShowErrorAsync(
                "アクセスエラー",
                "設定ファイルへのアクセスが拒否されました。").ConfigureAwait(false);
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Settings file format error when loading translation settings");
            StatusMessage = "設定ファイル形式エラー";
            await _notificationService.ShowErrorAsync(
                "ファイル形式エラー",
                "設定ファイルの形式が正しくありません。設定をリセットしてください。").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not (FileNotFoundException or UnauthorizedAccessException or FormatException))
        {
            _logger.LogError(ex, "Unexpected error when loading translation settings");
            StatusMessage = "予期しないエラー";
            await _notificationService.ShowErrorAsync(
                "読み込みエラー",
                $"予期しないエラーが発生しました: {ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 設定リセット処理
    /// </summary>
    private async Task ResetSettingsAsync()
    {
        try
        {
            var result = await _notificationService.ShowConfirmationAsync(
                "設定リセット",
                "全ての設定をデフォルト値に戻しますか？\nこの操作は元に戻せません。").ConfigureAwait(false);

            if (!result) return;

            IsLoading = true;
            StatusMessage = "設定をリセット中...";

            // デフォルト設定に戻す
            EngineSelection.SelectedEngine = TranslationEngine.LocalOnly;
            TranslationStrategy.SelectedStrategy = Models.TranslationStrategy.Direct;
            TranslationStrategy.EnableFallback = true;
            // 言語ペアは最初の項目を選択
            if (LanguagePairSelection.LanguagePairs.Count > 0)
            {
                LanguagePairSelection.SelectedLanguagePair = LanguagePairSelection.LanguagePairs[0];
            }

            HasChanges = true;
            StatusMessage = "設定をリセットしました";

            await _notificationService.ShowSuccessAsync(
                "設定リセット",
                "設定をデフォルト値にリセットしました。").ConfigureAwait(false);

            _logger.LogInformation("Translation settings reset to defaults");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation when resetting translation settings");
            StatusMessage = "操作エラー";
            await _notificationService.ShowErrorAsync(
                "操作エラー",
                "現在の状態では設定をリセットできません。").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Unexpected error when resetting translation settings");
            StatusMessage = "リセットエラー";
            await _notificationService.ShowErrorAsync(
                "リセットエラー",
                $"予期しないエラーが発生しました: {ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 設定エクスポート処理
    /// </summary>
    private async Task ExportSettingsAsync()
    {
        try
        {
            StatusMessage = "ファイル保存ダイアログを表示中...";

            // ファイル保存ダイアログを表示
            var fileTypeFilters = new List<FileTypeFilter>
            {
                new("翻訳設定ファイル", ["json"]),
                new("すべてのファイル", ["*"])
            };

            var filePath = await _fileDialogService.ShowSaveFileDialogAsync(
                "翻訳設定をエクスポート",
                $"Baketa_Settings_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                fileTypeFilters).ConfigureAwait(false);

            if (string.IsNullOrEmpty(filePath))
            {
                StatusMessage = "エクスポートがキャンセルされました";
                return;
            }

            StatusMessage = "設定をエクスポート中...";

            // 現在の設定をエクスポート用データに変換
            var currentSettings = CreateSettingsSummary();
            var exportableSettings = new ExportableTranslationSettings
            {
                SelectedEngine = currentSettings.SelectedEngine,
                SelectedLanguagePair = currentSettings.SelectedLanguagePair,
                SelectedChineseVariant = currentSettings.SelectedChineseVariant,
                SelectedStrategy = currentSettings.SelectedStrategy,
                EnableFallback = currentSettings.EnableFallback,
                LastSaved = currentSettings.LastSaved,
                Comments = "Baketa翻訳設定のエクスポート"
            };

            // ファイルにエクスポート
            await _exportImportService.ExportSettingsAsync(
                exportableSettings, 
                filePath,
                "手動エクスポート").ConfigureAwait(false);

            StatusMessage = "エクスポート完了";
            
            await _notificationService.ShowSuccessAsync(
                "エクスポート完了",
                $"翻訳設定を正常にエクスポートしました。\nファイル: {Path.GetFileName(filePath)}").ConfigureAwait(false);
                
            _logger.LogInformation("Settings export completed: {FilePath}", filePath);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Operation error when exporting settings");
            StatusMessage = "エクスポートエラー";
            await _notificationService.ShowErrorAsync(
                "エクスポートエラー",
                ex.Message).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Unexpected error when exporting settings");
            StatusMessage = "予期しないエラー";
            
            await _notificationService.ShowErrorAsync(
                "エクスポートエラー",
                $"設定のエクスポートに失敗しました: {ex.Message}").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 設定インポート処理
    /// </summary>
    private async Task ImportSettingsAsync()
    {
        try
        {
            StatusMessage = "ファイル選択ダイアログを表示中...";

            // ファイル選択ダイアログを表示
            var fileTypeFilters = new List<FileTypeFilter>
            {
                new("翻訳設定ファイル", ["json"]),
                new("すべてのファイル", ["*"])
            };

            var selectedFiles = await _fileDialogService.ShowOpenFileDialogAsync(
                "翻訳設定をインポート",
                fileTypeFilters,
                false).ConfigureAwait(false);

            if (selectedFiles == null || selectedFiles.Count == 0)
            {
                StatusMessage = "インポートがキャンセルされました";
                return;
            }

            var filePath = selectedFiles[0];
            StatusMessage = "設定をインポート中...";

            // ファイルからインポート
            var importResult = await _exportImportService.ImportSettingsAsync(filePath).ConfigureAwait(false);
            
            if (!importResult.Success)
            {
                StatusMessage = "インポートに失敗しました";
                await _notificationService.ShowErrorAsync(
                    "インポートエラー",
                    importResult.ErrorMessage ?? "不明なエラーが発生しました").ConfigureAwait(false);
                return;
            }

            if (importResult.Settings == null)
            {
                StatusMessage = "インポートに失敗しました";
                await _notificationService.ShowErrorAsync(
                    "インポートエラー",
                    "設定データが見つかりませんでした").ConfigureAwait(false);
                return;
            }

            // 警告または自動修正の通知
            if (importResult.HasAutoCorrections)
            {
                await _notificationService.ShowWarningAsync(
                    "設定の自動修正",
                    $"一部の設定が自動修正されました:\n{importResult.AutoCorrectionDetails}").ConfigureAwait(false);
            }
            else if (!string.IsNullOrEmpty(importResult.WarningMessage))
            {
                await _notificationService.ShowWarningAsync(
                    "インポート警告",
                    importResult.WarningMessage).ConfigureAwait(false);
            }

            // インポートされた設定を適用
            await ApplyImportedSettingsAsync(importResult.Settings).ConfigureAwait(false);

            HasChanges = true;
            StatusMessage = "インポート完了";
            
            await _notificationService.ShowSuccessAsync(
                "インポート完了",
                $"翻訳設定を正常にインポートしました。\nファイル: {Path.GetFileName(filePath)}").ConfigureAwait(false);
                
            _logger.LogInformation("Settings import completed: {FilePath}", filePath);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(ex, "Settings file not found when importing");
            StatusMessage = "ファイルが見つかりません";
            await _notificationService.ShowErrorAsync(
                "ファイルエラー",
                "選択されたファイルが見つかりません。").ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Unauthorized access when importing settings");
            StatusMessage = "アクセス拒否";
            await _notificationService.ShowErrorAsync(
                "アクセスエラー",
                "ファイルへのアクセスが拒否されました。").ConfigureAwait(false);
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Invalid file format when importing settings");
            StatusMessage = "ファイル形式エラー";
            await _notificationService.ShowErrorAsync(
                "形式エラー",
                "ファイルの形式が正しくありません。").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not (FileNotFoundException or UnauthorizedAccessException or FormatException))
        {
            _logger.LogError(ex, "Unexpected error when importing settings");
            StatusMessage = "予期しないエラー";
            await _notificationService.ShowErrorAsync(
                "インポートエラー",
                $"設定のインポートに失敗しました: {ex.Message}").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 変更破棄処理
    /// </summary>
    private async Task DiscardChangesAsync()
    {
        try
        {
            var result = await _notificationService.ShowConfirmationAsync(
                "変更の破棄",
                "未保存の変更を破棄して、最後に保存した設定に戻しますか？").ConfigureAwait(false);

            if (!result) return;

            await LoadSettingsAsync().ConfigureAwait(false);
            
            await _notificationService.ShowInfoAsync(
                "変更破棄",
                "変更を破棄し、保存済み設定に戻しました。").ConfigureAwait(false);

            _logger.LogInformation("Settings changes discarded");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation when discarding changes");
            await _notificationService.ShowErrorAsync(
                "操作エラー",
                "現在の状態では変更を破棄できません。").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to discard changes");
            await _notificationService.ShowErrorAsync(
                "操作エラー",
                $"変更の破棄に失敗しました: {ex.Message}").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// ヘルプ表示
    /// </summary>
    private void ShowHelp()
    {
        var helpText = 
            "【翻訳設定ヘルプ】\n\n" +
            "🔧 エンジン選択:\n" +
            "• LocalOnly: 高速・無料・オフライン対応\n" +
            "• CloudOnly: 高品質・プレミアムプラン必須\n\n" +
            "🌐 言語ペア:\n" +
            "• 日本語⇔英語: 最高品質\n" +
            "• 中国語関連: 簡体字・繁体字対応\n" +
            "• 2段階翻訳: ja→zh専用\n\n" +
            "⚙️ 翻訳戦略:\n" +
            "• Direct: 単一モデル、最高速度\n" +
            "• TwoStage: 中継言語経由、高品質\n\n" +
            "📊 ステータス:\n" +
            "• リアルタイム監視\n" +
            "• フォールバック通知\n" +
            "• エラー状態表示";

        _notificationService.ShowInfoAsync("翻訳設定ヘルプ", helpText);
    }

    /// <summary>
    /// 変更検出の設定
    /// </summary>
    private CompositeDisposable SetupChangeDetection()
    {
        CompositeDisposable disposables = [];

        // 各ViewModelの変更を監視
        EngineSelection.WhenAnyValue(x => x.SelectedEngine)
            .Skip(1)
            .Subscribe(_ => HasChanges = true)
            .DisposeWith(disposables);

        LanguagePairSelection.WhenAnyValue(x => x.SelectedLanguagePair)
            .Skip(1)
            .Subscribe(_ => HasChanges = true)
            .DisposeWith(disposables);

        LanguagePairSelection.WhenAnyValue(x => x.SelectedChineseVariant)
            .Skip(1)
            .Subscribe(_ => HasChanges = true)
            .DisposeWith(disposables);

        TranslationStrategy.WhenAnyValue(x => x.SelectedStrategy)
            .Skip(1)
            .Subscribe(_ => HasChanges = true)
            .DisposeWith(disposables);

        TranslationStrategy.WhenAnyValue(x => x.EnableFallback)
            .Skip(1)
            .Subscribe(_ => HasChanges = true)
            .DisposeWith(disposables);

        return disposables;
    }

    /// <summary>
    /// 言語ペア連携の設定
    /// </summary>
    private IDisposable SetupLanguagePairIntegration()
    {
        return LanguagePairSelection.WhenAnyValue(x => x.SelectedLanguagePair)
            .WhereNotNull()
            .Subscribe(languagePair =>
            {
                // 言語ペア変更時の翻訳戦略連携
                TranslationStrategy.CurrentLanguagePair = languagePair.LanguagePairKey;
                
                _logger.LogDebug("Language pair integration updated: {Pair}", languagePair.LanguagePairKey);
            });
    }

    /// <summary>
    /// 自動保存の設定
    /// </summary>
    private IDisposable SetupAutoSave()
    {
        return this.WhenAnyValue(x => x.HasChanges)
            .Where(hasChanges => hasChanges)
            .Throttle(TimeSpan.FromSeconds(30))
            .Where(_ => _options.AutoSaveSettings)
            .SelectMany(_ => SaveCommand.Execute())
            .Subscribe(
                _ => _logger.LogDebug("Auto-save completed"),
                ex => _logger.LogWarning(ex, "Auto-save failed"));
    }

    /// <summary>
    /// 現在の設定妥当性検証
    /// </summary>
    private SettingsValidationResult ValidateCurrentSettings()
    {
        // 基本的な妥当性チェック
        if (EngineSelection.SelectedEngine == TranslationEngine.CloudOnly && 
            !EngineSelection.IsCloudOnlyEnabled)
        {
            return new SettingsValidationResult(false, "CloudOnlyエンジンが選択されていますが、プレミアムプランが必要です。");
        }

        if (TranslationStrategy.SelectedStrategy == Models.TranslationStrategy.TwoStage && 
            !TranslationStrategy.IsTwoStageAvailable)
        {
            return new SettingsValidationResult(false, "2段階翻訳が選択されていますが、現在の言語ペアでは利用できません。");
        }

        if (LanguagePairSelection.SelectedLanguagePair?.IsEnabled == false)
        {
            return new SettingsValidationResult(false, "選択された言語ペアが無効になっています。");
        }

        return new SettingsValidationResult(true, string.Empty);
    }

    /// <summary>
    /// 設定サマリーの作成
    /// </summary>
    private TranslationSettingsSummary CreateSettingsSummary()
    {
        return new TranslationSettingsSummary
        {
            SelectedEngine = EngineSelection.SelectedEngine,
            SelectedLanguagePair = LanguagePairSelection.SelectedLanguagePair?.LanguagePairKey ?? string.Empty,
            SelectedChineseVariant = LanguagePairSelection.SelectedChineseVariant,
            SelectedStrategy = TranslationStrategy.SelectedStrategy,
            EnableFallback = TranslationStrategy.EnableFallback,
            LastSaved = LastSaved,
            HasChanges = HasChanges
        };
    }

    /// <summary>
    /// エクスポートデータの作成
    /// </summary>
    private static string CreateExportData(TranslationSettingsSummary settings)
    {
        return $"エンジン: {settings.SelectedEngine}\n" +
               $"言語ペア: {settings.SelectedLanguagePair}\n" +
               $"中国語変種: {settings.SelectedChineseVariant}\n" +
               $"翻訳戦略: {settings.SelectedStrategy}\n" +
               $"フォールバック: {(settings.EnableFallback ? "有効" : "無効")}\n" +
               $"最終保存: {settings.LastSaved:yyyy/MM/dd HH:mm:ss}";
    }

    /// <summary>
    /// インポートされた設定を適用します
    /// </summary>
    private async Task ApplyImportedSettingsAsync(ExportableTranslationSettings settings)
    {
        try
        {
            _logger.LogDebug("インポートされた設定を適用中...");

            // エンジン設定を適用
            EngineSelection.SelectedEngine = settings.SelectedEngine;

            // 翻訳戦略設定を適用
            TranslationStrategy.SelectedStrategy = settings.SelectedStrategy;
            TranslationStrategy.EnableFallback = settings.EnableFallback;

            // 中国語変種設定を適用
            LanguagePairSelection.SelectedChineseVariant = settings.SelectedChineseVariant;

            // 言語ペア設定を適用
            if (!string.IsNullOrWhiteSpace(settings.SelectedLanguagePair))
            {
                var languagePair = LanguagePairSelection.LanguagePairs
                    .FirstOrDefault(p => p.LanguagePairKey.Equals(settings.SelectedLanguagePair, StringComparison.OrdinalIgnoreCase));
                
                if (languagePair != null)
                {
                    LanguagePairSelection.SelectedLanguagePair = languagePair;
                }
                else
                {
                    _logger.LogWarning("インポートされた言語ペアが見つかりません: {LanguagePair}", settings.SelectedLanguagePair);
                }
            }

            // 必要に応じてUI更新を通知
            this.RaisePropertyChanged(nameof(CurrentSettings));

            _logger.LogInformation("インポートされた設定を正常に適用しました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "インポートされた設定の適用中にエラーが発生しました");
            throw;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    // 設定保存・読み込みの実装
    private async Task SaveEngineSettingsAsync()
    {
        try
        {
            await _settingsFileManager.SaveEngineSettingsAsync(EngineSelection.SelectedEngine).ConfigureAwait(false);
            _logger.LogDebug("エンジン設定を保存しました: {Engine}", EngineSelection.SelectedEngine);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "エンジン設定の保存に失敗しました");
            throw;
        }
    }

    private async Task SaveLanguagePairSettingsAsync()
    {
        try
        {
            var languagePair = LanguagePairSelection.SelectedLanguagePair?.LanguagePairKey ?? "ja-en";
            var chineseVariant = LanguagePairSelection.SelectedChineseVariant;
            
            await _settingsFileManager.SaveLanguagePairSettingsAsync(languagePair, chineseVariant).ConfigureAwait(false);
            _logger.LogDebug("言語ペア設定を保存しました: {LanguagePair}, {ChineseVariant}", languagePair, chineseVariant);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "言語ペア設定の保存に失敗しました");
            throw;
        }
    }

    private async Task SaveStrategySettingsAsync()
    {
        try
        {
            await _settingsFileManager.SaveStrategySettingsAsync(
                TranslationStrategy.SelectedStrategy, 
                TranslationStrategy.EnableFallback).ConfigureAwait(false);
                
            _logger.LogDebug("翻訳戦略設定を保存しました: {Strategy}, Fallback: {EnableFallback}", 
                TranslationStrategy.SelectedStrategy, TranslationStrategy.EnableFallback);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳戦略設定の保存に失敗しました");
            throw;
        }
    }

    private async Task LoadEngineSettingsAsync()
    {
        try
        {
            var engine = await _settingsFileManager.LoadEngineSettingsAsync().ConfigureAwait(false);
            EngineSelection.SelectedEngine = engine;
            
            _logger.LogDebug("エンジン設定を読み込みました: {Engine}", engine);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "エンジン設定の読み込みに失敗しました");
            throw;
        }
    }

    private async Task LoadLanguagePairSettingsAsync()
    {
        try
        {
            var (languagePair, chineseVariant) = await _settingsFileManager.LoadLanguagePairSettingsAsync().ConfigureAwait(false);
            
            // 言語ペアを検索して設定
            var selectedPair = LanguagePairSelection.LanguagePairs
                .FirstOrDefault(p => p.LanguagePairKey == languagePair);
            
            if (selectedPair != null)
            {
                LanguagePairSelection.SelectedLanguagePair = selectedPair;
            }
            
            LanguagePairSelection.SelectedChineseVariant = chineseVariant;
            
            _logger.LogDebug("言語ペア設定を読み込みました: {LanguagePair}, {ChineseVariant}", languagePair, chineseVariant);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "言語ペア設定の読み込みに失敗しました");
            throw;
        }
    }

    private async Task LoadStrategySettingsAsync()
    {
        try
        {
            var (strategy, enableFallback) = await _settingsFileManager.LoadStrategySettingsAsync().ConfigureAwait(false);
            
            TranslationStrategy.SelectedStrategy = strategy;
            TranslationStrategy.EnableFallback = enableFallback;
            
            _logger.LogDebug("翻訳戦略設定を読み込みました: {Strategy}, Fallback: {EnableFallback}", strategy, enableFallback);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳戦略設定の読み込みに失敗しました");
            throw;
        }
    }

    /// <summary>
    /// リソースを解放します
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposables?.Dispose();
            EngineSelection?.Dispose();
            LanguagePairSelection?.Dispose();
            TranslationStrategy?.Dispose();
            EngineStatus?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// 翻訳設定サマリー
/// </summary>
public sealed class TranslationSettingsSummary
{
    public TranslationEngine SelectedEngine { get; init; }
    public string SelectedLanguagePair { get; init; } = string.Empty;
    public ChineseVariant SelectedChineseVariant { get; init; }
    public Models.TranslationStrategy SelectedStrategy { get; init; }
    public bool EnableFallback { get; init; }
    public DateTime LastSaved { get; init; }
    public bool HasChanges { get; init; }
}

/// <summary>
/// 設定妥当性検証結果
/// </summary>
public sealed record SettingsValidationResult(bool IsValid, string ErrorMessage);
