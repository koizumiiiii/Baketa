using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Baketa.UI.Configuration;
using Baketa.UI.Models;

namespace Baketa.UI.Services;

/// <summary>
/// ローカライゼーションサービスの実装
/// </summary>
public class LocalizationService : ILocalizationService, IDisposable
{
    private readonly ILogger<LocalizationService> _logger;
    private readonly TranslationUIOptions _options;
    private readonly BehaviorSubject<CultureInfo> _currentLanguageSubject;
    
    private static readonly Dictionary<char, bool> SimplifiedChars = new()
    {
        ['国'] = true, ['语'] = true, ['简'] = true, ['体'] = true, ['字'] = true,
        ['学'] = true, ['问'] = true, ['题'] = true, ['经'] = true, ['济'] = true
    };
    
    private static readonly Dictionary<char, bool> TraditionalChars = new()
    {
        ['國'] = true, ['語'] = true, ['簡'] = true, ['體'] = true, ['字'] = true,
        ['學'] = true, ['問'] = true, ['題'] = true, ['經'] = true, ['濟'] = true
    };

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="logger">ロガー</param>
    /// <param name="options">UI設定オプション</param>
    public LocalizationService(
        ILogger<LocalizationService> logger,
        IOptions<TranslationUIOptions> options)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        
        _logger = logger;
        _options = options.Value;
        
        // システム言語を検出して初期化
        var detectedLanguage = DetectSystemLanguage();
        CurrentCulture = new CultureInfo(detectedLanguage);
        _currentLanguageSubject = new BehaviorSubject<CultureInfo>(CurrentCulture);
        
        _logger.LogInformation("LocalizationService initialized with culture: {Culture}", CurrentCulture.Name);
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
    public Task<bool> ChangeLanguageAsync(string cultureCode)
    {
        try
        {
            var newCulture = new CultureInfo(cultureCode);
            var oldCulture = CurrentCulture;
            
            // 対応言語かチェック
            if (!SupportedLanguages.Any(lang => lang.Code == cultureCode))
            {
                _logger.LogWarning("Unsupported language code: {CultureCode}", cultureCode);
                return Task.FromResult(false);
            }
            
            CurrentCulture = newCulture;
            CultureInfo.CurrentCulture = newCulture;
            CultureInfo.CurrentUICulture = newCulture;
            
            // オブザーバブルに変更を通知
            _currentLanguageSubject.OnNext(newCulture);
            
            _logger.LogInformation("Language changed from {OldCulture} to {NewCulture}", 
                oldCulture.Name, newCulture.Name);
            
            LanguageChanged?.Invoke(this, new LanguageChangedEventArgs(oldCulture, newCulture));
            
            return Task.FromResult(true);
        }
        catch (CultureNotFoundException ex)
        {
            _logger.LogError(ex, "Invalid culture code: {CultureCode}", cultureCode);
            return Task.FromResult(false);
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
