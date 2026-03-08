using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Baketa.UI.Configuration;
using Baketa.UI.Models;
using Baketa.UI.Services;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.ViewModels;

/// <summary>
/// [Issue #495] 初回セットアップウィザード ViewModel
/// 母国語を選択するだけで TargetLang + UI言語が自動設定される
/// </summary>
public sealed class FirstRunWizardViewModel : ReactiveObject
{
    private readonly ILocalizationService _localizationService;
    private readonly SettingsFileManager _settingsFileManager;
    private readonly Infrastructure.Services.IFirstRunService _firstRunService;
    private readonly ILogger<FirstRunWizardViewModel> _logger;

    /// <summary>
    /// 現在選択可能な言語コード（言語拡張時にここに追加するだけでOK）
    /// </summary>
    private static readonly HashSet<string> EnabledLanguageCodes = ["en", "ja", "zh-CN", "zh-TW", "ko", "es", "fr", "de", "it", "pt"];

    private SupportedLanguage? _selectedLanguage;

    /// <summary>
    /// 選択可能な言語一覧（EnabledLanguageCodesでフィルタ）
    /// </summary>
    public IReadOnlyList<SupportedLanguage> AvailableLanguages { get; }

    /// <summary>
    /// 選択された母国語
    /// </summary>
    public SupportedLanguage? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedLanguage, value);
            if (value != null)
            {
                _ = OnLanguageSelectedAsync(value);
            }
        }
    }

    /// <summary>
    /// ウィザード完了（確定ボタン）コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }

    /// <summary>
    /// ウィザードが完了したか
    /// </summary>
    public bool IsCompleted { get; private set; }

    public FirstRunWizardViewModel(
        ILocalizationService localizationService,
        SettingsFileManager settingsFileManager,
        Infrastructure.Services.IFirstRunService firstRunService,
        ILogger<FirstRunWizardViewModel> logger)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _settingsFileManager = settingsFileManager ?? throw new ArgumentNullException(nameof(settingsFileManager));
        _firstRunService = firstRunService ?? throw new ArgumentNullException(nameof(firstRunService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // EnabledLanguageCodesでフィルタした言語リスト
        AvailableLanguages = _localizationService.SupportedLanguages
            .Where(lang => EnabledLanguageCodes.Contains(lang.Code))
            .ToList()
            .AsReadOnly();

        // デフォルト: English
        _selectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == "en")
            ?? AvailableLanguages.FirstOrDefault();

        var canConfirm = this.WhenAnyValue(x => x.SelectedLanguage)
            .Select(lang => lang != null);
        ConfirmCommand = ReactiveCommand.CreateFromTask(ConfirmAsync, canConfirm);
    }

    /// <summary>
    /// 言語選択時にUI言語をリアルタイム切替
    /// </summary>
    private async Task OnLanguageSelectedAsync(SupportedLanguage language)
    {
        try
        {
            _logger.LogInformation("[Issue #495] 言語選択: {Code} ({Name})", language.Code, language.NativeName);
            await _localizationService.ChangeLanguageAsync(language.Code).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #495] UI言語切替に失敗: {Code}", language.Code);
        }
    }

    /// <summary>
    /// ウィザード確定: TargetLang保存 + UI言語保存 + MarkAsRun
    /// </summary>
    private async Task ConfirmAsync()
    {
        if (SelectedLanguage == null) return;

        try
        {
            var targetLang = SelectedLanguage.Code;
            var sourceLang = DetermineSourceLanguage(targetLang);

            // UI言語を確実に適用（デフォルト選択のままOKした場合、setterを経由しないため）
            await _localizationService.ChangeLanguageAsync(targetLang).ConfigureAwait(true);

            var languagePair = $"{sourceLang}-{targetLang}";
            await _settingsFileManager.SaveLanguagePairSettingsAsync(
                languagePair, ChineseVariant.Auto).ConfigureAwait(true);

            _logger.LogInformation("[Issue #495] ウィザード完了 - LanguagePair: {Pair}, UiLanguage: {UiLang}",
                languagePair, targetLang);

            // 初回起動フラグをマーク
            _firstRunService.MarkAsRun();

            IsCompleted = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #495] ウィザード確定処理に失敗");
        }
    }

    /// <summary>
    /// 母国語（TargetLang）からソース言語を決定する。
    /// - デフォルト: "en"（大半のゲームは英語）
    /// - TargetLangが"en"の場合: "ja"（Baketaの主要対象は日本語ゲーム）
    ///   言語拡張後はユーザーが設定画面で変更可能
    /// </summary>
    private static string DetermineSourceLanguage(string targetLang)
    {
        const string defaultSource = "en";
        const string fallbackForEnglishUsers = "ja";

        return targetLang != defaultSource ? defaultSource : fallbackForEnglishUsers;
    }
}
