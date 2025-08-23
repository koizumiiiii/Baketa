using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Settings;
using System.Collections.Concurrent;

namespace Baketa.Application.Services.Translation;

/// <summary>
/// 設定ファイルベースの翻訳辞書サービス実装
/// ハードコード翻訳から設定ファイルベースへの移行を支援
/// </summary>
public sealed class TranslationDictionaryService : ITranslationDictionaryService
{
    private readonly IOptionsMonitor<CommonTranslationsSettings> _optionsMonitor;
    private readonly ILogger<TranslationDictionaryService> _logger;
    
    // パフォーマンス最適化: 翻訳結果をメモリキャッシュ
    private readonly ConcurrentDictionary<string, string> _translationCache = new();
    private CommonTranslationsSettings? _cachedSettings;
    private readonly object _settingsLock = new();

    public TranslationDictionaryService(
        IOptionsMonitor<CommonTranslationsSettings> optionsMonitor,
        ILogger<TranslationDictionaryService> logger)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // 設定変更の監視
        _optionsMonitor.OnChange(OnSettingsChanged);
        
        _logger.LogInformation("📚 TranslationDictionaryService初期化完了 - 設定ファイルベース翻訳開始");
    }

    public async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        ArgumentNullException.ThrowIfNull(sourceLanguage);
        ArgumentNullException.ThrowIfNull(targetLanguage);

        var cacheKey = $"{sourceLanguage}:{targetLanguage}:{text}";
        
        // キャッシュから検索
        if (_translationCache.TryGetValue(cacheKey, out var cachedTranslation))
        {
            _logger.LogTrace("📚 キャッシュヒット: '{Text}' -> '{Translation}'", text, cachedTranslation);
            return cachedTranslation;
        }

        var settings = GetCurrentSettings();
        var translatedText = await Task.Run(() => PerformTranslation(text, sourceLanguage, targetLanguage, settings), cancellationToken);

        // 結果をキャッシュに保存（元テキストと異なる場合のみ）
        if (!string.Equals(text, translatedText, StringComparison.Ordinal))
        {
            _translationCache.TryAdd(cacheKey, translatedText);
            _logger.LogTrace("📚 翻訳成功: '{Text}' -> '{Translation}' ({SourceLang} -> {TargetLang})", 
                text, translatedText, sourceLanguage, targetLanguage);
        }

        return translatedText;
    }

    public bool HasTranslation(string text, string sourceLanguage, string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var settings = GetCurrentSettings();
        var dictionary = GetTranslationDictionary(settings, sourceLanguage, targetLanguage);
        
        return dictionary != null && ContainsInAnyCategory(dictionary, text);
    }

    public async Task ReloadConfigurationAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🔄 翻訳辞書設定を再読み込み中...");
        
        lock (_settingsLock)
        {
            _cachedSettings = null;
        }
        
        // キャッシュをクリア
        _translationCache.Clear();
        
        // 新しい設定を取得（次回アクセス時に読み込まれる）
        _ = GetCurrentSettings();
        
        _logger.LogInformation("✅ 翻訳辞書設定再読み込み完了");
        
        await Task.CompletedTask;
    }

    public int GetTranslationCount(string sourceLanguage, string targetLanguage)
    {
        var settings = GetCurrentSettings();
        var dictionary = GetTranslationDictionary(settings, sourceLanguage, targetLanguage);
        
        if (dictionary == null)
            return 0;

        return dictionary.UI.Count + 
               dictionary.Game.Count + 
               dictionary.Actions.Count + 
               dictionary.Common.Count + 
               dictionary.Custom.Count;
    }

    public IReadOnlyList<(string sourceLanguage, string targetLanguage)> GetSupportedLanguagePairs()
    {
        var settings = GetCurrentSettings();
        var supportedPairs = new List<(string, string)>();

        // 日本語 ⇄ 英語の双方向サポート
        if (HasAnyTranslations(settings.JapaneseToEnglish))
            supportedPairs.Add(("ja", "en"));
            
        if (HasAnyTranslations(settings.EnglishToJapanese))
            supportedPairs.Add(("en", "ja"));

        return supportedPairs.AsReadOnly();
    }

    private CommonTranslationsSettings GetCurrentSettings()
    {
        lock (_settingsLock)
        {
            if (_cachedSettings == null)
            {
                _cachedSettings = _optionsMonitor.CurrentValue;
                _logger.LogDebug("📚 翻訳辞書設定をキャッシュに読み込み");
            }
            return _cachedSettings;
        }
    }

    private void OnSettingsChanged(CommonTranslationsSettings newSettings)
    {
        _logger.LogInformation("🔄 翻訳辞書設定が変更されました - キャッシュをクリア");
        
        lock (_settingsLock)
        {
            _cachedSettings = null;
        }
        
        _translationCache.Clear();
    }

    private string PerformTranslation(string text, string sourceLanguage, string targetLanguage, CommonTranslationsSettings settings)
    {
        var dictionary = GetTranslationDictionary(settings, sourceLanguage, targetLanguage);
        
        if (dictionary == null)
        {
            _logger.LogTrace("📚 サポートされていない言語ペア: {SourceLang} -> {TargetLang}", sourceLanguage, targetLanguage);
            return text;
        }

        // カテゴリ別に翻訳を検索（優先度順）
        var translation = FindTranslationInCategories(dictionary, text);
        
        if (translation != null)
        {
            return translation;
        }

        // フォールバック処理
        return HandleFallback(text, sourceLanguage, targetLanguage, settings.Fallback);
    }

    private TranslationDictionary? GetTranslationDictionary(CommonTranslationsSettings settings, string sourceLanguage, string targetLanguage)
    {
        var sourceLang = sourceLanguage.ToLowerInvariant();
        var targetLang = targetLanguage.ToLowerInvariant();

        return (sourceLang, targetLang) switch
        {
            ("ja" or "jpn" or "japanese", "en" or "eng" or "english") => settings.JapaneseToEnglish,
            ("en" or "eng" or "english", "ja" or "jpn" or "japanese") => settings.EnglishToJapanese,
            _ => null
        };
    }

    private string? FindTranslationInCategories(TranslationDictionary dictionary, string text)
    {
        // 優先度順でカテゴリを検索
        var categories = new[]
        {
            dictionary.UI,      // UI要素が最優先
            dictionary.Common,  // 一般的な表現
            dictionary.Actions, // アクション
            dictionary.Game,    // ゲーム用語
            dictionary.Custom   // カスタム翻訳
        };

        foreach (var category in categories)
        {
            if (category.TryGetValue(text, out var translation) && !string.IsNullOrWhiteSpace(translation))
            {
                return translation;
            }
        }

        return null;
    }

    private bool ContainsInAnyCategory(TranslationDictionary dictionary, string text)
    {
        return dictionary.UI.ContainsKey(text) ||
               dictionary.Common.ContainsKey(text) ||
               dictionary.Actions.ContainsKey(text) ||
               dictionary.Game.ContainsKey(text) ||
               dictionary.Custom.ContainsKey(text);
    }

    private bool HasAnyTranslations(TranslationDictionary dictionary)
    {
        return dictionary.UI.Count > 0 ||
               dictionary.Common.Count > 0 ||
               dictionary.Actions.Count > 0 ||
               dictionary.Game.Count > 0 ||
               dictionary.Custom.Count > 0;
    }

    private string HandleFallback(string text, string sourceLanguage, string targetLanguage, FallbackSettings fallback)
    {
        _logger.LogTrace("📚 翻訳が見つかりません - フォールバック処理: '{Text}' ({SourceLang} -> {TargetLang})", 
            text, sourceLanguage, targetLanguage);

        return fallback.NotFoundBehavior switch
        {
            FallbackBehavior.ReturnOriginal => text,
            FallbackBehavior.ReturnEmpty => string.Empty,
            FallbackBehavior.ReturnPlaceholder => $"[{text}]",
            _ => text
        };
    }
}