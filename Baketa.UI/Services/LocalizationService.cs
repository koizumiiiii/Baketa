using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Baketa.Core.Services;
using Baketa.Core.Settings;
using Baketa.UI.Configuration;
using Baketa.UI.Models;
using Baketa.UI.Resources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Note: This service handles both UI localization (Strings.resx) and translation localization features

namespace Baketa.UI.Services;

/// <summary>
/// ローカライゼーションサービスの実装
/// </summary>
public class LocalizationService : ILocalizationService, IDisposable
{
    private readonly ILogger<LocalizationService> _logger;
    private readonly TranslationUIOptions _options;
    private readonly ISettingsService _settingsService;
    private readonly BehaviorSubject<CultureInfo> _currentLanguageSubject;

    private static readonly Dictionary<char, bool> SimplifiedChars = new()
    {
        ['国'] = true,
        ['语'] = true,
        ['简'] = true,
        ['体'] = true,
        ['字'] = true,
        ['学'] = true,
        ['问'] = true,
        ['题'] = true,
        ['经'] = true,
        ['济'] = true
    };

    private static readonly Dictionary<char, bool> TraditionalChars = new()
    {
        ['國'] = true,
        ['語'] = true,
        ['簡'] = true,
        ['體'] = true,
        ['字'] = true,
        ['學'] = true,
        ['問'] = true,
        ['題'] = true,
        ['經'] = true,
        ['濟'] = true
    };

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="logger">ロガー</param>
    /// <param name="options">UI設定オプション</param>
    /// <param name="settingsService">設定サービス</param>
    public LocalizationService(
        ILogger<LocalizationService> logger,
        IOptions<TranslationUIOptions> options,
        ISettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settingsService);

        _logger = logger;
        _options = options.Value;
        _settingsService = settingsService;

        Console.WriteLine($"[LocalizationService] 初期化開始: SettingsService={settingsService.GetType().Name}");
        _logger.LogInformation("LocalizationService初期化開始: SettingsService=あり");

        // 保存された言語設定を使用、なければシステム言語を検出して初期化
        string initialLanguage;
        var savedUiLanguage = GetSavedUiLanguage();
        Console.WriteLine($"[LocalizationService] 保存されたUI言語設定: {savedUiLanguage ?? "(null)"}");
        _logger.LogInformation("保存されたUI言語設定: {SavedLanguage}", savedUiLanguage ?? "(null)");

        if (!string.IsNullOrEmpty(savedUiLanguage) &&
            SupportedLanguages.Any(lang => lang.Code == savedUiLanguage))
        {
            initialLanguage = savedUiLanguage;
            _logger.LogInformation("保存された言語設定を使用: {Language}", initialLanguage);
        }
        else
        {
            initialLanguage = DetectSystemLanguage();
            _logger.LogInformation("保存設定なし、システム言語を使用: {Language}", initialLanguage);
        }

        CurrentCulture = new CultureInfo(initialLanguage);
        _currentLanguageSubject = new BehaviorSubject<CultureInfo>(CurrentCulture);

        // Initialize Strings.resx culture for UI localization
        Strings.Culture = CurrentCulture;

        // Initialize LocalizationManager for dynamic UI update support
        LocalizationManager.Instance.Initialize(this);

        _logger.LogInformation("LocalizationService初期化完了: Culture={Culture}", CurrentCulture.Name);
    }

    /// <inheritdoc />
    public CultureInfo CurrentCulture { get; private set; }

    /// <inheritdoc />
    public System.IObservable<CultureInfo> CurrentLanguageChanged => _currentLanguageSubject;

    /// <inheritdoc />
    public IReadOnlyList<SupportedLanguage> SupportedLanguages { get; } = new List<SupportedLanguage>
    {
        new("ja", "日本語", "Japanese"),
        new("en", "English", "English"),
        new("zh-CN", "简体中文", "Chinese (Simplified)"),
        new("zh-TW", "繁體中文", "Chinese (Traditional)"),
        new("ko", "한국어", "Korean"),
        new("es", "Español", "Spanish"),
        new("fr", "Français", "French"),
        new("de", "Deutsch", "German"),
        new("it", "Italiano", "Italian"),
        new("pt", "Português", "Portuguese"),
        new("ru", "Русский", "Russian"),
        new("ar", "العربية", "Arabic", true),
        new("hi", "हिन्दी", "Hindi"),
        new("th", "ไทย", "Thai"),
        new("vi", "Tiếng Việt", "Vietnamese")
    }.AsReadOnly();

    /// <inheritdoc />
    public async Task<bool> ChangeLanguageAsync(string cultureCode)
    {
        System.Diagnostics.Debug.WriteLine($"[LocalizationService] ChangeLanguageAsync called with: {cultureCode}, HashCode: {this.GetHashCode()}");
        Console.WriteLine($"[LocalizationService] ChangeLanguageAsync called with: {cultureCode}");
        _logger.LogDebug("ChangeLanguageAsync開始: {CultureCode}, HashCode: {HashCode}", cultureCode, this.GetHashCode());

        try
        {
            var newCulture = new CultureInfo(cultureCode);
            var oldCulture = CurrentCulture;

            // 🔥 同じ言語への変更は無視（冗長なログ "ja to ja" を防止）
            if (oldCulture.Name == newCulture.Name)
            {
                _logger.LogDebug("言語変更スキップ（同一言語）: {CultureCode}", cultureCode);
                return true;
            }

            System.Diagnostics.Debug.WriteLine($"[LocalizationService] Old culture: {oldCulture?.Name ?? "(null)"}, New culture: {newCulture.Name}");
            Console.WriteLine($"[LocalizationService] Old culture: {oldCulture?.Name ?? "(null)"}, New culture: {newCulture.Name}");

            // 対応言語かチェック
            if (!SupportedLanguages.Any(lang => lang.Code == cultureCode))
            {
                Console.WriteLine($"[LocalizationService] Unsupported language code: {cultureCode}");
                _logger.LogWarning("Unsupported language code: {CultureCode}", cultureCode);
                return false;
            }

            CurrentCulture = newCulture;
            CultureInfo.CurrentCulture = newCulture;
            CultureInfo.CurrentUICulture = newCulture;

            // Update Strings.resx culture for UI localization
            Strings.Culture = newCulture;

            Console.WriteLine($"[LocalizationService] Strings.Culture updated to: {Strings.Culture?.Name ?? "(null)"}");

            // UIスレッドでバインディングをリフレッシュ（PropertyChangedはUIスレッドで発火する必要がある）
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                LocalizationManager.Instance.RefreshAllBindings();

                // [Issue #245] 言語に応じてフォントを切り替え
                UpdateFontForLanguage(cultureCode);
            });

            // オブザーバブルに変更を通知
            _currentLanguageSubject.OnNext(newCulture);

            _logger.LogInformation("Language changed from {OldCulture} to {NewCulture}",
                oldCulture.Name, newCulture.Name);

            var handlerCount = LanguageChanged?.GetInvocationList().Length ?? 0;
            Console.WriteLine($"[LocalizationService] LanguageChanged event handlers: {handlerCount}");
            LanguageChanged?.Invoke(this, new LanguageChangedEventArgs(oldCulture, newCulture));
            Console.WriteLine($"[LocalizationService] LanguageChanged event fired");

            return true;
        }
        catch (CultureNotFoundException ex)
        {
            Console.WriteLine($"[LocalizationService] CultureNotFoundException: {ex.Message}");
            _logger.LogError(ex, "Invalid culture code: {CultureCode}", cultureCode);
            return false;
        }
    }

    /// <summary>
    /// [Issue #245] 言語に応じてフォントを切り替えます
    /// </summary>
    /// <param name="cultureCode">言語コード</param>
    private void UpdateFontForLanguage(string cultureCode)
    {
        try
        {
            var app = Avalonia.Application.Current;
            if (app?.Resources == null) return;

            // 言語コードに基づいて適切なフォントキーを選択
            var fontKey = cultureCode switch
            {
                "ja" or "ja-JP" => "FontFamily.Japanese",
                "en" or "en-US" => "FontFamily.English",
                "zh" or "zh-CN" or "zh-Hans" => "FontFamily.Chinese",
                _ => "FontFamily.Japanese" // デフォルトは日本語
            };

            // フォントリソースを取得
            if (app.Resources.TryGetResource(fontKey, app.ActualThemeVariant, out var fontResource) &&
                fontResource is Avalonia.Media.FontFamily fontFamily)
            {
                // DefaultFontFamilyを更新
                app.Resources["DefaultFontFamily"] = fontFamily;
                _logger.LogInformation("フォント切り替え: {FontKey} -> {FontFamily}", fontKey, fontFamily.Name);
            }
            else
            {
                _logger.LogWarning("フォントリソースが見つかりません: {FontKey}", fontKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "フォント切り替えエラー: {CultureCode}", cultureCode);
        }
    }

    /// <summary>
    /// 保存されたUI言語設定を取得します
    /// </summary>
    /// <returns>保存されたUI言語コード。未設定の場合はnull</returns>
    private string? GetSavedUiLanguage()
    {
        try
        {
            Console.WriteLine("[LocalizationService] GetSavedUiLanguage: GetCategorySettings<GeneralSettings>()を呼び出し中...");
            var generalSettings = _settingsService.GetCategorySettings<GeneralSettings>();
            Console.WriteLine($"[LocalizationService] GetSavedUiLanguage: GeneralSettings={(generalSettings != null ? "取得成功" : "null")}");

            var uiLanguage = generalSettings?.UiLanguage;
            Console.WriteLine($"[LocalizationService] GetSavedUiLanguage: UiLanguage={uiLanguage ?? "(null)"}");

            // ISettingsServiceがまだ初期化完了していない場合、設定ファイルから直接読み込む
            if (string.IsNullOrEmpty(uiLanguage))
            {
                Console.WriteLine("[LocalizationService] GetSavedUiLanguage: UiLanguageがnull、設定ファイルから直接読み込みを試行...");
                uiLanguage = ReadUiLanguageFromSettingsFile();
                Console.WriteLine($"[LocalizationService] GetSavedUiLanguage: 設定ファイルから読み込んだUiLanguage={uiLanguage ?? "(null)"}");
            }

            return uiLanguage;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LocalizationService] GetSavedUiLanguage: 例外発生 - {ex.Message}");
            _logger.LogWarning(ex, "Failed to load saved UI language preference");
            return null;
        }
    }

    /// <summary>
    /// 設定ファイルから直接UI言語設定を読み込みます
    /// ISettingsServiceがまだ初期化されていない場合のフォールバック用
    /// </summary>
    /// <returns>保存されたUI言語コード。未設定の場合はnull</returns>
    private static string? ReadUiLanguageFromSettingsFile()
    {
        try
        {
            var settingsPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Baketa",
                "settings.json");

            if (!System.IO.File.Exists(settingsPath))
            {
                Console.WriteLine($"[LocalizationService] ReadUiLanguageFromSettingsFile: 設定ファイルが存在しません: {settingsPath}");
                return null;
            }

            var json = System.IO.File.ReadAllText(settingsPath);
            using var document = System.Text.Json.JsonDocument.Parse(json);

            if (document.RootElement.TryGetProperty("General", out var generalElement) &&
                generalElement.TryGetProperty("UiLanguage", out var uiLanguageElement))
            {
                return uiLanguageElement.GetString();
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LocalizationService] ReadUiLanguageFromSettingsFile: 例外発生 - {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc />
    public string DetectSystemLanguage()
    {
        var systemCulture = CultureInfo.CurrentUICulture;

        // システム言語がサポートされているかチェック
        var supportedLanguage = SupportedLanguages.FirstOrDefault(lang =>
            lang.Code == systemCulture.Name ||
            lang.Code == systemCulture.TwoLetterISOLanguageName);

        if (supportedLanguage != null)
        {
            _logger.LogDebug("Detected system language: {Language}", supportedLanguage.Code);
            return supportedLanguage.Code;
        }

        // サポートされていない場合は英語にフォールバック
        _logger.LogDebug("System language {SystemLanguage} not supported, falling back to English",
            systemCulture.Name);
        return "en";
    }

    /// <inheritdoc />
    public ChineseVariant DetectChineseVariant(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ChineseVariant.Auto;

        int simplifiedCount = 0;
        int traditionalCount = 0;

        foreach (char c in text)
        {
            if (SimplifiedChars.ContainsKey(c))
                simplifiedCount++;
            if (TraditionalChars.ContainsKey(c))
                traditionalCount++;
        }

        // 判定しきい値（文字数の5%または最低3文字）
        int threshold = Math.Max(3, text.Length / 20);

        if (simplifiedCount >= threshold && simplifiedCount > traditionalCount)
        {
            _logger.LogDebug("Detected Chinese variant: Simplified (simplified: {Simplified}, traditional: {Traditional})",
                simplifiedCount, traditionalCount);
            return ChineseVariant.Simplified;
        }

        if (traditionalCount >= threshold && traditionalCount > simplifiedCount)
        {
            _logger.LogDebug("Detected Chinese variant: Traditional (simplified: {Simplified}, traditional: {Traditional})",
                simplifiedCount, traditionalCount);
            return ChineseVariant.Traditional;
        }

        _logger.LogDebug("Could not determine Chinese variant, returning Auto (simplified: {Simplified}, traditional: {Traditional})",
            simplifiedCount, traditionalCount);
        return ChineseVariant.Auto;
    }

    /// <inheritdoc />
    public event EventHandler<LanguageChangedEventArgs>? LanguageChanged;

    /// <summary>
    /// リソースの解放
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// リソースの解放
    /// </summary>
    /// <param name="disposing">マネージドリソースの解放フラグ</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _currentLanguageSubject?.Dispose();
        }
    }
}
