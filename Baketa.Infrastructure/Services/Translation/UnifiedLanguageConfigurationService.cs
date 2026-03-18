using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Models.Translation;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services.Translation;

/// <summary>
/// 統一言語設定サービス実装
/// UI設定を単一ソースとした言語設定管理
/// Clean Architecture準拠でテスト可能な実装
/// </summary>
public sealed class UnifiedLanguageConfigurationService : ILanguageConfigurationService
{
    private readonly IUnifiedSettingsService _settingsService;
    private readonly ILogger<UnifiedLanguageConfigurationService> _logger;
    private LanguagePair? _cachedLanguagePair;
    private readonly object _cacheLock = new();

    /// <summary>
    /// 言語設定変更イベント
    /// </summary>
    public event EventHandler<LanguagePair>? LanguagePairChanged;

    public UnifiedLanguageConfigurationService(
        IUnifiedSettingsService settingsService,
        ILogger<UnifiedLanguageConfigurationService> logger)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 🔥 [Issue #189] 設定変更時にキャッシュをクリアして最新の言語ペアを反映
        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>
    /// 設定変更時のハンドラ
    /// </summary>
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        // 翻訳設定が変更された場合のみキャッシュをクリア
        if (e.SettingsType == SettingsType.Translation)
        {
            lock (_cacheLock)
            {
                _cachedLanguagePair = null;
            }
            _logger.LogDebug("言語ペアキャッシュをクリアしました（設定変更検出）");
        }
    }

    /// <inheritdoc />
    public LanguagePair GetCurrentLanguagePair()
    {
        // 🔥 [Issue #189] キャッシュを無効化し、毎回ファイルから読み取る
        // JsonSettingsServiceがtranslation-settings.jsonに書き込んでも
        // UnifiedSettingsServiceのキャッシュがクリアされない問題を回避
        // 翻訳は頻繁に呼ばれないのでパフォーマンス影響は最小
        lock (_cacheLock)
        {
            // 🔥 キャッシュは使用せず、常に最新の設定を読み取る
            var settings = _settingsService.GetTranslationSettings();
            var languagePair = CreateLanguagePairFromSettings(settings);

            _logger.LogDebug("言語ペア取得（毎回読み取り）: {LanguagePair}", languagePair.ToDisplayString());
            return languagePair;
        }
    }

    /// <inheritdoc />
    public async Task<LanguagePair> GetLanguagePairAsync()
    {
        // 現在の実装では同期実行だが、将来の非同期設定取得に対応
        return await Task.FromResult(GetCurrentLanguagePair()).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public bool IsAutoDetectionEnabled =>
        _settingsService.GetTranslationSettings().AutoDetectSourceLanguage;

    /// <inheritdoc />
    public async Task UpdateLanguagePairAsync(LanguagePair pair)
    {
        ArgumentNullException.ThrowIfNull(pair);

        if (!pair.IsValidForTranslation())
        {
            throw new ArgumentException($"Invalid language pair for translation: {pair.ToDisplayString()}", nameof(pair));
        }

        var currentSettings = _settingsService.GetTranslationSettings();

        // ITranslationSettingsは読み取り専用のため、実装クラスを作成
        var updatedSettings = new TranslationSettingsImpl
        {
            DefaultSourceLanguage = pair.SourceCode,
            DefaultTargetLanguage = pair.TargetCode,
            AutoDetectSourceLanguage = currentSettings.AutoDetectSourceLanguage,
            DefaultEngine = currentSettings.DefaultEngine,
            UseLocalEngine = currentSettings.UseLocalEngine,
            ConfidenceThreshold = currentSettings.ConfidenceThreshold,
            TimeoutMs = currentSettings.TimeoutMs
        };

        try
        {
            await _settingsService.UpdateTranslationSettingsAsync(updatedSettings).ConfigureAwait(false);

            // キャッシュ更新とイベント発火
            lock (_cacheLock)
            {
                _cachedLanguagePair = pair;
            }

            LanguagePairChanged?.Invoke(this, pair);

            _logger.LogInformation("言語ペア更新完了: {Source} → {Target}",
                pair.Source.DisplayName, pair.Target.DisplayName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "言語ペア更新に失敗: {LanguagePair}", pair.ToDisplayString());
            throw;
        }
    }

    /// <inheritdoc />
    public string GetSourceLanguageCode()
    {
        return GetCurrentLanguagePair().SourceCode;
    }

    /// <inheritdoc />
    public string GetTargetLanguageCode()
    {
        return GetCurrentLanguagePair().TargetCode;
    }

    /// <summary>
    /// 設定から言語ペアを作成
    /// </summary>
    private LanguagePair CreateLanguagePairFromSettings(ITranslationSettings settings)
    {
        try
        {
            var sourceCode = settings.DefaultSourceLanguage ?? "en";
            var targetCode = settings.DefaultTargetLanguage ?? "ja";

            var source = Language.FromCode(sourceCode);
            var target = Language.FromCode(targetCode);

            return new LanguagePair(source, target);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "[Issue #517] 言語コードの解析に失敗しました。デフォルト(ja→en)にフォールバック: Source={Source}, Target={Target}",
                settings.DefaultSourceLanguage, settings.DefaultTargetLanguage);
            return LanguagePair.Default;
        }
    }
}

/// <summary>
/// ITranslationSettings実装クラス（設定更新用）
/// </summary>
internal sealed class TranslationSettingsImpl : ITranslationSettings
{
    public bool AutoDetectSourceLanguage { get; set; }
    public string DefaultSourceLanguage { get; set; } = string.Empty;
    public string DefaultTargetLanguage { get; set; } = string.Empty;
    public string DefaultEngine { get; set; } = string.Empty;
    public bool UseLocalEngine { get; set; }
    public double ConfidenceThreshold { get; set; }
    public int TimeoutMs { get; set; }
    public int OverlayFontSize { get; set; }
    /// <summary>
    /// [Issue #78 Phase 5] Cloud AI翻訳の有効化フラグ
    /// </summary>
    public bool EnableCloudAiTranslation { get; set; }
    /// <summary>
    /// [Issue #554] EXモード自動ON済みフラグ
    /// </summary>
    public bool ExModeAutoEnabled { get; set; }
}
