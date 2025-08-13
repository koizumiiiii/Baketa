using System;
using System.IO;
using System.Text.Json;
using Baketa.Core.Constants;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Settings;
using Baketa.Core.Utilities;

namespace Baketa.Infrastructure.Services.Settings;

/// <summary>
/// 統一設定管理サービス実装
/// アプリケーション設定とユーザー設定を統合し、リアルタイム変更監視を提供
/// </summary>
public sealed class UnifiedSettingsService : IUnifiedSettingsService, IDisposable
{
    private readonly IOptions<AppSettings> _appSettingsOptions;
    private readonly ILogger<UnifiedSettingsService>? _logger;
    private readonly FileSystemWatcher? _fileWatcher;
    private readonly SemaphoreSlim _settingsLock = new(1, 1);
    
    private UnifiedTranslationSettings? _cachedTranslationSettings;
    private UnifiedOcrSettings? _cachedOcrSettings;
    private UnifiedAppSettings? _cachedAppSettings;
    private bool _isWatching;
    private bool _disposed;

    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    public UnifiedSettingsService(
        IOptions<AppSettings> appSettingsOptions,
        ILogger<UnifiedSettingsService>? logger = null)
    {
        _appSettingsOptions = appSettingsOptions ?? throw new ArgumentNullException(nameof(appSettingsOptions));
        _logger = logger;

        // ユーザー設定ディレクトリを確実に作成
        BaketaSettingsPaths.EnsureUserSettingsDirectoryExists();

        // ファイルシステム監視の初期化
        if (Directory.Exists(BaketaSettingsPaths.UserSettingsDirectory))
        {
            _fileWatcher = new FileSystemWatcher(BaketaSettingsPaths.UserSettingsDirectory, "*.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = false
            };
            _fileWatcher.Changed += OnSettingsFileChanged;
            _fileWatcher.Created += OnSettingsFileChanged;
        }

        _logger?.LogInformation("UnifiedSettingsService初期化完了 - 監視ディレクトリ: {Directory}", 
            BaketaSettingsPaths.UserSettingsDirectory);
    }

    /// <inheritdoc />
    public ITranslationSettings GetTranslationSettings()
    {
        if (_cachedTranslationSettings is not null)
            return _cachedTranslationSettings;

        _settingsLock.Wait();
        try
        {
            _cachedTranslationSettings ??= LoadTranslationSettings();
            return _cachedTranslationSettings;
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    /// <inheritdoc />
    public IOcrSettings GetOcrSettings()
    {
        if (_cachedOcrSettings is not null)
            return _cachedOcrSettings;

        _settingsLock.Wait();
        try
        {
            _cachedOcrSettings ??= LoadOcrSettings();
            return _cachedOcrSettings;
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    /// <inheritdoc />
    public IAppSettings GetAppSettings()
    {
        if (_cachedAppSettings is not null)
            return _cachedAppSettings;

        _settingsLock.Wait();
        try
        {
            _cachedAppSettings ??= new UnifiedAppSettings(GetTranslationSettings(), GetOcrSettings(), _appSettingsOptions.Value);
            return _cachedAppSettings;
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpdateTranslationSettingsAsync(ITranslationSettings settings, CancellationToken cancellationToken = default)
    {
        var userSettings = new Dictionary<string, object>
        {
            ["useLocalEngine"] = settings.UseLocalEngine,
            ["sourceLanguage"] = LanguageCodeConverter.ToDisplayName(settings.DefaultSourceLanguage),
            ["targetLanguage"] = LanguageCodeConverter.ToDisplayName(settings.DefaultTargetLanguage),
            ["autoDetectSourceLanguage"] = settings.AutoDetectSourceLanguage,
            ["defaultEngine"] = settings.DefaultEngine,
            ["confidenceThreshold"] = settings.ConfidenceThreshold,
            ["timeoutMs"] = settings.TimeoutMs
        };

        var json = JsonSerializer.Serialize(userSettings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(BaketaSettingsPaths.TranslationSettingsPath, json, cancellationToken).ConfigureAwait(false);

        // キャッシュをクリア
        await _settingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cachedTranslationSettings = null;
            _cachedAppSettings = null;
        }
        finally
        {
            _settingsLock.Release();
        }

        _logger?.LogInformation("翻訳設定を更新しました: {SourceLang} -> {TargetLang}, Engine: {Engine}",
            settings.DefaultSourceLanguage, settings.DefaultTargetLanguage, settings.DefaultEngine);
    }

    /// <inheritdoc />
    public async Task UpdateOcrSettingsAsync(IOcrSettings settings, CancellationToken cancellationToken = default)
    {
        var userSettings = new Dictionary<string, object>
        {
            ["defaultLanguage"] = settings.DefaultLanguage,
            ["confidenceThreshold"] = settings.ConfidenceThreshold,
            ["timeoutMs"] = settings.TimeoutMs,
            ["enablePreprocessing"] = settings.EnablePreprocessing
        };

        var json = JsonSerializer.Serialize(userSettings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(BaketaSettingsPaths.OcrSettingsPath, json, cancellationToken).ConfigureAwait(false);

        // キャッシュをクリア
        await _settingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cachedOcrSettings = null;
            _cachedAppSettings = null;
        }
        finally
        {
            _settingsLock.Release();
        }

        _logger?.LogInformation("OCR設定を更新しました: Language: {Language}, Threshold: {Threshold}",
            settings.DefaultLanguage, settings.ConfidenceThreshold);
    }

    /// <inheritdoc />
    public async Task ReloadSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _settingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 全キャッシュをクリア
            _cachedTranslationSettings = null;
            _cachedOcrSettings = null;
            _cachedAppSettings = null;

            _logger?.LogInformation("設定をリロードしました");
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    /// <inheritdoc />
    public void StartWatching()
    {
        if (_fileWatcher is null || _isWatching)
            return;

        _fileWatcher.EnableRaisingEvents = true;
        _isWatching = true;
        _logger?.LogInformation("設定ファイルの監視を開始しました");
    }

    /// <inheritdoc />
    public void StopWatching()
    {
        if (_fileWatcher is null || !_isWatching)
            return;

        _fileWatcher.EnableRaisingEvents = false;
        _isWatching = false;
        _logger?.LogInformation("設定ファイルの監視を停止しました");
    }

    private UnifiedTranslationSettings LoadTranslationSettings()
    {
        var appSettings = _appSettingsOptions.Value.Translation;
        
        // ユーザー設定ファイルが存在する場合は優先
        if (File.Exists(BaketaSettingsPaths.TranslationSettingsPath))
        {
            try
            {
                var jsonContent = File.ReadAllText(BaketaSettingsPaths.TranslationSettingsPath);
                var userSettings = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);

                if (userSettings is not null)
                {
                    return CreateTranslationSettingsFromUser(userSettings, appSettings);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "ユーザー翻訳設定ファイルの読み込みに失敗しました。デフォルト設定を使用します");
            }
        }

        // デフォルト設定を使用
        return new UnifiedTranslationSettings(
            appSettings.AutoDetectSourceLanguage,
            appSettings.DefaultSourceLanguage,
            appSettings.DefaultTargetLanguage,
            appSettings.DefaultEngine.ToString(),
            true, // ユーザー設定がない場合はローカルエンジンを使用
            BaketaConstants.Ocr.DefaultConfidenceThreshold, // デフォルト信頼度しきい値
            appSettings.TimeoutSeconds * 1000); // 秒をミリ秒に変換
    }

    private UnifiedOcrSettings LoadOcrSettings()
    {
        var appSettings = _appSettingsOptions.Value.Ocr;

        // ユーザー設定ファイルが存在する場合は優先
        if (File.Exists(BaketaSettingsPaths.OcrSettingsPath))
        {
            try
            {
                var jsonContent = File.ReadAllText(BaketaSettingsPaths.OcrSettingsPath);
                var userSettings = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);

                if (userSettings is not null)
                {
                    return CreateOcrSettingsFromUser(userSettings, appSettings);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "ユーザーOCR設定ファイルの読み込みに失敗しました。デフォルト設定を使用します");
            }
        }

        // デフォルト設定を使用
        return new UnifiedOcrSettings(
            "ja", // デフォルト言語
            appSettings.ConfidenceThreshold,
            BaketaConstants.Ocr.DefaultTimeoutMs, // デフォルトタイムアウト30秒
            appSettings.AutoOptimizationEnabled);
    }

    private static UnifiedTranslationSettings CreateTranslationSettingsFromUser(
        Dictionary<string, object> userSettings, 
        TranslationSettings appSettings)
    {
        var sourceLanguage = LanguageCodeConverter.ToLanguageCode(
            userSettings.GetValueOrDefault("sourceLanguage")?.ToString() ?? "English");
        var targetLanguage = LanguageCodeConverter.ToLanguageCode(
            userSettings.GetValueOrDefault("targetLanguage")?.ToString() ?? "Japanese");

        return new UnifiedTranslationSettings(
            GetBoolValue(userSettings, "autoDetectSourceLanguage", appSettings.AutoDetectSourceLanguage),
            sourceLanguage,
            targetLanguage,
            userSettings.GetValueOrDefault("defaultEngine")?.ToString() ?? appSettings.DefaultEngine.ToString(),
            GetBoolValue(userSettings, "useLocalEngine", true),
            GetDoubleValue(userSettings, "confidenceThreshold", 0.7),
            GetIntValue(userSettings, "timeoutMs", appSettings.TimeoutSeconds * 1000));
    }

    private static UnifiedOcrSettings CreateOcrSettingsFromUser(
        Dictionary<string, object> userSettings,
        OcrSettings appSettings)
    {
        return new UnifiedOcrSettings(
            userSettings.GetValueOrDefault("defaultLanguage")?.ToString() ?? "ja",
            GetDoubleValue(userSettings, "confidenceThreshold", appSettings.ConfidenceThreshold),
            GetIntValue(userSettings, "timeoutMs", 30000),
            GetBoolValue(userSettings, "enablePreprocessing", appSettings.AutoOptimizationEnabled));
    }

    private static bool GetBoolValue(Dictionary<string, object> settings, string key, bool defaultValue)
    {
        if (!settings.TryGetValue(key, out var value))
            return defaultValue;

        return value switch
        {
            bool boolValue => boolValue,
            JsonElement element when element.ValueKind == JsonValueKind.True => true,
            JsonElement element when element.ValueKind == JsonValueKind.False => false,
            string stringValue when bool.TryParse(stringValue, out var parsed) => parsed,
            _ => defaultValue
        };
    }

    private static double GetDoubleValue(Dictionary<string, object> settings, string key, double defaultValue)
    {
        if (!settings.TryGetValue(key, out var value))
            return defaultValue;

        return value switch
        {
            double doubleValue => doubleValue,
            JsonElement element when element.ValueKind == JsonValueKind.Number => element.GetDouble(),
            string stringValue when double.TryParse(stringValue, out var parsed) => parsed,
            _ => defaultValue
        };
    }

    private static int GetIntValue(Dictionary<string, object> settings, string key, int defaultValue)
    {
        if (!settings.TryGetValue(key, out var value))
            return defaultValue;

        return value switch
        {
            int intValue => intValue,
            JsonElement element when element.ValueKind == JsonValueKind.Number => element.GetInt32(),
            string stringValue when int.TryParse(stringValue, out var parsed) => parsed,
            _ => defaultValue
        };
    }

    // 言語変換ロジックはLanguageCodeConverterに統一

    private void OnSettingsFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!BaketaSettingsPaths.IsValidSettingsPath(e.FullPath))
            return;

        Task.Run(async () =>
        {
            try
            {
                // ファイル書き込み完了を待つ
                await Task.Delay(100);
                
                await ReloadSettingsAsync();

                var settingsType = Path.GetFileNameWithoutExtension(e.Name) switch
                {
                    "translation-settings" => SettingsType.Translation,
                    "ocr-settings" => SettingsType.Ocr,
                    _ => SettingsType.User
                };

                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(e.Name ?? "", settingsType));

                _logger?.LogInformation("設定ファイル変更を検出し、リロードしました: {FilePath}", e.FullPath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "設定ファイル変更処理中にエラーが発生しました: {FilePath}", e.FullPath);
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        StopWatching();
        _fileWatcher?.Dispose();
        _settingsLock.Dispose();
        _disposed = true;

        _logger?.LogInformation("UnifiedSettingsServiceを破棄しました");
    }
}

/// <summary>
/// 統一翻訳設定実装
/// </summary>
internal sealed record UnifiedTranslationSettings(
    bool AutoDetectSourceLanguage,
    string DefaultSourceLanguage,
    string DefaultTargetLanguage,
    string DefaultEngine,
    bool UseLocalEngine,
    double ConfidenceThreshold,
    int TimeoutMs) : ITranslationSettings;

/// <summary>
/// 統一OCR設定実装
/// </summary>
internal sealed record UnifiedOcrSettings(
    string DefaultLanguage,
    double ConfidenceThreshold,
    int TimeoutMs,
    bool EnablePreprocessing) : IOcrSettings;

/// <summary>
/// 統一アプリケーション設定実装
/// </summary>
internal sealed class UnifiedAppSettings(ITranslationSettings translation, IOcrSettings ocr, AppSettings appSettings) : IAppSettings
{
    public ITranslationSettings Translation { get; } = translation;
    public IOcrSettings Ocr { get; } = ocr;
    public string LogLevel { get; } = "Information"; // デフォルトログレベル
    public bool EnableDebugMode { get; } = false; // デフォルトデバッグモード
}
