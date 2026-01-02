using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Services;
using Baketa.Core.Settings;
using Baketa.Core.Abstractions.License;
using Baketa.Core.Abstractions.Payment;
using Baketa.UI.Framework;
using Baketa.UI.Models.Settings;
using Baketa.UI.Resources;
using Baketa.UI.Services;
using Baketa.UI.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using UiFramework = Baketa.UI.Framework;

namespace Baketa.UI.ViewModels;

/// <summary>
/// 設定ウィンドウのViewModel
/// プログレッシブディスクロージャーによる基本/詳細設定の階層表示をサポート
/// </summary>
public sealed class SettingsWindowViewModel : UiFramework.ViewModelBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISettingsChangeTracker _changeTracker;
    private readonly IEventAggregator _eventAggregator;
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService? _localizationService;
    private readonly IUnifiedSettingsService? _unifiedSettingsService;
    private readonly ILogger<SettingsWindowViewModel>? _logger;
    private SettingCategory? _selectedCategory;
    private string _statusMessage = string.Empty;

    /// <summary>
    /// SettingsWindowViewModelを初期化します
    /// </summary>
    /// <param name="serviceProvider">DIコンテナのサービスプロバイダー</param>
    /// <param name="changeTracker">設定変更追跡サービス</param>
    /// <param name="eventAggregator">イベント集約器</param>
    /// <param name="settingsService">設定サービス</param>
    /// <param name="localizationService">ローカライゼーションサービス（オプション）</param>
    /// <param name="unifiedSettingsService">統合設定サービス（オプション）</param>
    /// <param name="logger">ロガー（オプション）</param>
    public SettingsWindowViewModel(
        IServiceProvider serviceProvider,
        ISettingsChangeTracker changeTracker,
        IEventAggregator eventAggregator,
        ISettingsService settingsService,
        ILocalizationService? localizationService = null,
        IUnifiedSettingsService? unifiedSettingsService = null,
        ILogger<SettingsWindowViewModel>? logger = null) : base(eventAggregator)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _changeTracker = changeTracker ?? throw new ArgumentNullException(nameof(changeTracker));
        _eventAggregator = eventAggregator; // 既にbase()でnullチェック済み
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _localizationService = localizationService;
        _unifiedSettingsService = unifiedSettingsService;
        _logger = logger;

        // カテゴリの初期化（null引数チェック後に実行）
        InitializeCategories();

        // 変更追跡の設定
        this._changeTracker.HasChangesChanged += (_, e) =>
        {
            this.RaisePropertyChanged(nameof(HasChanges));
            StatusMessage = e.HasChanges ? Strings.Settings_Status_HasChanges : Strings.Settings_Status_NoChanges;
        };

        // コマンドの初期化
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, this.WhenAnyValue(x => x.HasChanges));
        CancelCommand = ReactiveCommand.CreateFromTask(CancelAsync);

        // 初期カテゴリの選択
        SelectedCategory = AllCategories.Count > 0 ? AllCategories[0] : null;
    }

    #region プロパティ

    /// <summary>
    /// すべての設定カテゴリ
    /// </summary>
    public IReadOnlyList<SettingCategory> AllCategories { get; private set; } = [];

    /// <summary>
    /// 現在選択されているカテゴリ
    /// </summary>
    public SettingCategory? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (_selectedCategory == value) return;

            // Contentが未作成の場合は遅延作成（変更通知前に作成）
            if (value != null)
            {
                EnsureCategoryContent(value);
            }

            // プロパティ変更を通知（Contentが既に作成済みの状態で）
            this.RaiseAndSetIfChanged(ref _selectedCategory, value);
        }
    }

    /// <summary>
    /// 設定に変更があるかどうか
    /// </summary>
    public bool HasChanges => this._changeTracker.HasChanges;

    /// <summary>
    /// ステータスメッセージ
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    #endregion

    #region イベント

    /// <summary>
    /// ウィンドウを閉じることを要求するイベント
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// ウィンドウを閉じることを要求します
    /// </summary>
    private void RequestClose()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region コマンド

    /// <summary>
    /// 保存コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }

    /// <summary>
    /// キャンセルコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    #endregion

    #region メソッド

    /// <summary>
    /// カテゴリを初期化します
    /// </summary>
    private void InitializeCategories()
    {
        var categories = new List<SettingCategory>
        {
            // 基本設定カテゴリ
            new()
            {
                Id = "settings_general",
                NameResourceKey = "Settings_General_Title",
                DescriptionResourceKey = "Settings_General_Subtitle",
                IconData = "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11.03L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.22,8.95 2.27,9.22 2.46,9.37L4.57,11.03C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.65 4.57,12.97L2.46,14.63C2.27,14.78 2.22,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.03 4.95,18.95L7.44,17.94C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.68 16.04,18.34 16.56,17.94L19.05,18.95C19.27,19.03 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z", // Settings icon
                Level = SettingLevel.Basic,
                DisplayOrder = 1,
                Content = null // 遅延作成
            },

            new()
            {
                Id = "settings_account",
                NameResourceKey = "Settings_Account_Title",
                DescriptionResourceKey = "Settings_Account_Subtitle",
                IconData = "M12,4A4,4 0 0,1 16,8A4,4 0 0,1 12,12A4,4 0 0,1 8,8A4,4 0 0,1 12,4M12,14C16.42,14 20,15.79 20,18V20H4V18C4,15.79 7.58,14 12,14Z", // Account icon
                Level = SettingLevel.Basic,
                DisplayOrder = 2,
                Content = null // 遅延作成
            },

            new()
            {
                Id = "settings_license",
                NameResourceKey = "Settings_License_Title",
                DescriptionResourceKey = "Settings_License_Subtitle",
                IconData = "M12,1L3,5V11C3,16.55 6.84,21.74 12,23C17.16,21.74 21,16.55 21,11V5L12,1M12,5A3,3 0 0,1 15,8A3,3 0 0,1 12,11A3,3 0 0,1 9,8A3,3 0 0,1 12,5M17.13,17C15.92,18.85 14.11,20.24 12,20.92C9.89,20.24 8.08,18.85 6.87,17C6.53,16.5 6.24,16 6,15.47C6,13.82 8.71,12.47 12,12.47C15.29,12.47 18,13.79 18,15.47C17.76,16 17.47,16.5 17.13,17Z", // Shield account icon
                Level = SettingLevel.Basic,
                DisplayOrder = 3,
                Content = null // 遅延作成
            }
        };

        AllCategories = categories;

        // 言語変更イベントをサブスクライブしてカテゴリ名を更新
        if (_localizationService != null)
        {
            _localizationService.LanguageChanged += OnLanguageChanged;
        }
    }

    /// <summary>
    /// 言語変更イベントハンドラ - カテゴリのローカライズ文字列を更新
    /// </summary>
    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        foreach (var category in AllCategories)
        {
            category.RefreshLocalizedStrings();
        }
        _logger?.LogDebug("設定カテゴリのローカライズ文字列を更新しました: {NewCulture}", e.NewCulture.Name);
    }

    /// <summary>
    /// カテゴリのContentを遅延作成します（DIからViewModelを取得）
    /// ViewLocatorがViewModelからViewへの変換を自動的に行います
    /// </summary>
    private void EnsureCategoryContent(SettingCategory category)
    {
        if (category.Content != null)
        {
            return; // 既に作成済み
        }

        // DIコンテナからViewModelを取得し、category.Contentに設定
        // ViewLocatorがViewModelからViewへの自動変換を行う
        category.Content = category.Id switch
        {
            "settings_general" => _serviceProvider.GetRequiredService<GeneralSettingsViewModel>(),
            "settings_account" => _serviceProvider.GetRequiredService<AccountSettingsViewModel>(),
            "settings_license" => _serviceProvider.GetService<LicenseInfoViewModel>(), // オプショナル
            _ => null
        };

        _logger?.LogDebug("カテゴリ {CategoryId} のViewModelを作成しました: {ContentType}",
            category.Id, category.Content?.GetType().Name ?? "null");
    }

    /// <summary>
    /// 設定画面を閉じる際にカテゴリのContentをクリアします
    /// Avaloniaのビジュアルツリー問題を回避するため、再表示時に新しいViewを作成できるようにします
    /// </summary>
    public void ClearCategoryContents()
    {
        foreach (var category in AllCategories)
        {
            category.Content = null;
        }
        _logger?.LogDebug("すべてのカテゴリContentをクリアしました");
    }

    /// <summary>
    /// 選択中のカテゴリのContentを確実に再作成します
    /// ウィンドウを再表示する際に、ClearCategoryContentsでnullになったContentを再作成するために使用します
    /// </summary>
    public void EnsureSelectedCategoryContent()
    {
        if (_selectedCategory != null)
        {
            EnsureCategoryContent(_selectedCategory);
            // ContentControlのバインディングを更新するためにPropertyChangedを発行
            this.RaisePropertyChanged(nameof(SelectedCategory));
            _logger?.LogDebug("選択中のカテゴリ {CategoryId} のContentを再作成しました", _selectedCategory.Id);
        }
    }

    /// <summary>
    /// 設定を保存します
    /// </summary>
    private async Task SaveAsync()
    {
        try
        {
            StatusMessage = Strings.Settings_Status_Saving;

            // 各カテゴリの設定を保存（UIスレッドコンテキストを維持）
            await SaveCategorySettingsAsync();

            this._changeTracker.ClearChanges();
            StatusMessage = Strings.Settings_Status_Saved;
            _logger?.LogInformation("設定の保存が完了しました");

            // ウィンドウを閉じる
            RequestClose();
        }
        catch (InvalidOperationException ex)
        {
            // CA1863: ローカライズされたリソース文字列は言語変更時に内容が変わるため、
            // CompositeFormatキャッシュは不適切。低頻度のエラー処理なのでパフォーマンス影響も軽微。
#pragma warning disable CA1863
            StatusMessage = string.Format(Strings.Settings_Status_SaveFailed, ex.Message);
#pragma warning restore CA1863
            _logger?.LogError(ex, "設定の保存中にエラーが発生しました");
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusMessage = Strings.Settings_Status_AccessDenied;
            _logger?.LogError(ex, "設定ファイルへのアクセスが拒否されました");
        }
        catch (System.IO.IOException ex)
        {
            StatusMessage = Strings.Settings_Status_IOError;
            _logger?.LogError(ex, "設定ファイルのI/Oエラーが発生しました");
        }
    }

    /// <summary>
    /// 各カテゴリの設定を保存します
    /// </summary>
    private async Task SaveCategorySettingsAsync()
    {
        _logger?.LogInformation("設定カテゴリの保存処理を実行中 - カテゴリ数: {Count}", AllCategories.Count);

        // UIスレッドで設定データを収集（ConfigureAwait(false)の前に完了）
        GeneralSettings? generalSettingsToSave = null;
        TranslationSettings? translationSettingsToSave = null;
        GeneralSettingsViewModel? generalViewModel = null;

        foreach (var category in AllCategories)
        {
            // 保存前にContentを確実に作成
            EnsureCategoryContent(category);

            _logger?.LogInformation("カテゴリ処理: {CategoryId}, Content: {ContentType}",
                category.Id,
                category.Content?.GetType().Name ?? "null");

            // ContentはViewModel（DIから取得済み）
            switch (category.Content)
            {
                case GeneralSettingsViewModel generalVm:
                    generalViewModel = generalVm;
                    generalSettingsToSave = generalVm.CurrentSettings;
                    translationSettingsToSave = generalVm.CurrentTranslationSettings;
                    _logger?.LogInformation("GeneralSettings収集: UiLanguage={UiLanguage}, AutoStart={AutoStart}, MinimizeToTray={MinimizeToTray}",
                        generalSettingsToSave.UiLanguage ?? "(null)",
                        generalSettingsToSave.AutoStartWithWindows,
                        generalSettingsToSave.MinimizeToTray);
                    _logger?.LogInformation("TranslationSettings収集: SourceLang={SourceLang}, TargetLang={TargetLang}, FontSize={FontSize}",
                        translationSettingsToSave.DefaultSourceLanguage,
                        translationSettingsToSave.DefaultTargetLanguage,
                        translationSettingsToSave.OverlayFontSize);
                    break;
                    // 他のViewModelも必要に応じて追加
            }
        }

        // ファイルI/O操作を実行（ConfigureAwait(false)で非UIスレッドへ移行可能）
        try
        {
            if (generalSettingsToSave != null)
            {
                _logger?.LogInformation("一般設定を保存中: UiLanguage={UiLanguage}", generalSettingsToSave.UiLanguage ?? "(null)");
                await _settingsService.SaveAsync(generalSettingsToSave).ConfigureAwait(false);
                _logger?.LogInformation("一般設定を保存しました");

                // UI言語を適用（保存後に実際に言語を変更）
                if (generalViewModel != null)
                {
                    await generalViewModel.ApplyUiLanguageAsync().ConfigureAwait(false);

                    // テーマを適用（保存後に実際にテーマを変更）
                    generalViewModel.ApplySelectedTheme();
                }
            }
            else
            {
                _logger?.LogWarning("保存する一般設定がありません（generalSettingsToSave is null）");
            }

            // 翻訳設定を保存（翻訳言語・フォントサイズ）
            if (translationSettingsToSave != null)
            {
                _logger?.LogInformation("翻訳設定を保存中: SourceLang={SourceLang}, TargetLang={TargetLang}, FontSize={FontSize}",
                    translationSettingsToSave.DefaultSourceLanguage,
                    translationSettingsToSave.DefaultTargetLanguage,
                    translationSettingsToSave.OverlayFontSize);
                await _settingsService.SaveAsync(translationSettingsToSave).ConfigureAwait(false);
                _logger?.LogInformation("翻訳設定を保存しました");

                // translation-settings.jsonにも保存（翻訳エンジンが読み込むファイル）
                if (_unifiedSettingsService != null)
                {
                    _logger?.LogInformation("translation-settings.jsonに翻訳設定を保存中...");
                    await _unifiedSettingsService.UpdateTranslationSettingsAsync(translationSettingsToSave).ConfigureAwait(false);
                    _logger?.LogInformation("translation-settings.jsonに翻訳設定を保存しました");
                }
            }
            else
            {
                _logger?.LogWarning("保存する翻訳設定がありません（translationSettingsToSave is null）");
            }

            // 設定をファイルに永続化
            await _settingsService.SaveAsync().ConfigureAwait(false);
            _logger?.LogInformation("すべての設定をファイルに保存しました");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "設定の保存中にエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// 変更をキャンセルして閉じます
    /// </summary>
    private Task CancelAsync()
    {
        StatusMessage = Strings.Settings_Status_Cancelled;
        _logger?.LogInformation("設定の変更がキャンセルされました");

        // ウィンドウを閉じる
        RequestClose();
        return Task.CompletedTask;
    }

    #endregion
}
