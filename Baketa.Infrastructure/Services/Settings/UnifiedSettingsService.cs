using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Constants;
using Baketa.Core.Extensions;
using Baketa.Core.Settings;
using Baketa.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private UnifiedPromotionSettings? _cachedPromotionSettings;
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
        // 🔥 [Issue #189] キャッシュを無効化し、毎回ファイルから読み取る
        // JsonSettingsServiceがtranslation-settings.jsonに書き込んでも
        // このキャッシュがクリアされない問題を回避
        // 翻訳設定は頻繁にアクセスされないのでパフォーマンス影響は最小
        _settingsLock.Wait();
        try
        {
            // 🔥 常に最新の設定をファイルから読み取る
            return LoadTranslationSettings();
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
            // 🔥 [DEADLOCK_FIX] GetTranslationSettings()/GetOcrSettings()呼び出しはデッドロックの原因
            // _settingsLock再入不可のため、直接LoadXxxSettings()を呼ぶ
            _cachedTranslationSettings ??= LoadTranslationSettings();
            _cachedOcrSettings ??= LoadOcrSettings();

            _cachedAppSettings ??= new UnifiedAppSettings(_cachedTranslationSettings, _cachedOcrSettings, _appSettingsOptions.Value);
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
        // 🔥 [Issue #189] selectedLanguagePair形式で保存（CreateTranslationSettingsFromUserと統一）
        // 言語コードを小文字で保存（例: "en-ja"）
        var sourceLang = settings.DefaultSourceLanguage?.ToLowerInvariant() ?? "en";
        var targetLang = settings.DefaultTargetLanguage?.ToLowerInvariant() ?? "ja";
        var selectedLanguagePair = $"{sourceLang}-{targetLang}";

        var userSettings = new Dictionary<string, object>
        {
            ["useLocalEngine"] = settings.UseLocalEngine,
            ["selectedLanguagePair"] = selectedLanguagePair,  // 🔥 新形式
            ["autoDetectSourceLanguage"] = settings.AutoDetectSourceLanguage,
            ["defaultEngine"] = settings.DefaultEngine,
            ["confidenceThreshold"] = settings.ConfidenceThreshold,
            ["timeoutMs"] = settings.TimeoutMs,
            ["overlayFontSize"] = settings.OverlayFontSize,
            // [Issue #243] Cloud AI翻訳設定を保存
            ["enableCloudAiTranslation"] = settings.EnableCloudAiTranslation
        };

        var json = JsonSerializer.Serialize(userSettings, new JsonSerializerOptions { WriteIndented = true });

        // [Issue #237] FileSystemWatcher競合状態回避: 自己書き込み中は監視を一時停止
        var wasWatching = _isWatching;
        if (wasWatching) StopWatching();
        try
        {
            await File.WriteAllTextAsync(BaketaSettingsPaths.TranslationSettingsPath, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (wasWatching) StartWatching();
        }

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

        _logger?.LogInformation("翻訳設定を更新しました: {SelectedLanguagePair}, Engine: {Engine}, CloudAI: {CloudAI}",
            selectedLanguagePair, settings.DefaultEngine, settings.EnableCloudAiTranslation);

        // [Issue #243] 設定変更イベントを発火してUI更新をトリガー
        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs("translation", SettingsType.Translation));
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

        // [Issue #237] FileSystemWatcher競合状態回避: 自己書き込み中は監視を一時停止
        var wasWatching = _isWatching;
        if (wasWatching) StopWatching();
        try
        {
            await File.WriteAllTextAsync(BaketaSettingsPaths.OcrSettingsPath, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (wasWatching) StartWatching();
        }

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
    public IPromotionSettings GetPromotionSettings()
    {
        if (_cachedPromotionSettings is not null)
            return _cachedPromotionSettings;

        _settingsLock.Wait();
        try
        {
            _cachedPromotionSettings ??= LoadPromotionSettings();
            return _cachedPromotionSettings;
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpdatePromotionSettingsAsync(IPromotionSettings settings, CancellationToken cancellationToken = default)
    {
        // [Issue #237] ディレクトリが存在することを確認
        BaketaSettingsPaths.EnsureUserSettingsDirectoryExists();

        var promotionData = new Dictionary<string, object?>();

        if (settings.AppliedPromotionCode is not null)
            promotionData["appliedPromotionCode"] = settings.AppliedPromotionCode;

        if (settings.PromotionPlanType.HasValue)
            promotionData["promotionPlanType"] = settings.PromotionPlanType.Value;

        if (settings.PromotionExpiresAt is not null)
            promotionData["promotionExpiresAt"] = settings.PromotionExpiresAt;

        if (settings.PromotionAppliedAt is not null)
            promotionData["promotionAppliedAt"] = settings.PromotionAppliedAt;

        if (settings.LastOnlineVerification is not null)
            promotionData["lastOnlineVerification"] = settings.LastOnlineVerification;

        var json = JsonSerializer.Serialize(promotionData, new JsonSerializerOptions { WriteIndented = true });

        // [Issue #237] FileSystemWatcher競合状態回避: 自己書き込み中は監視を一時停止
        var wasWatching = _isWatching;
        if (wasWatching) StopWatching();
        try
        {
            await File.WriteAllTextAsync(BaketaSettingsPaths.PromotionSettingsPath, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (wasWatching) StartWatching();
        }

        // キャッシュをクリア
        await _settingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cachedPromotionSettings = null;
        }
        finally
        {
            _settingsLock.Release();
        }

        _logger?.LogInformation("[Issue #237] プロモーション設定を更新しました: Plan: {Plan}, ExpiresAt: {ExpiresAt}",
            settings.PromotionPlanType, settings.PromotionExpiresAt);

        // 設定変更イベントを発火
        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs("promotion", SettingsType.Promotion));
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
            _cachedPromotionSettings = null;

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
            appSettings.TimeoutSeconds * 1000, // 秒をミリ秒に変換
            14, // デフォルトフォントサイズ
            appSettings.EnableCloudAiTranslation); // [Issue #78 Phase 5] Cloud AI翻訳設定
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

    /// <summary>
    /// [Issue #237] プロモーション設定をファイルから読み込む
    /// </summary>
    private UnifiedPromotionSettings LoadPromotionSettings()
    {
        // プロモーション設定ファイルが存在する場合は読み込み
        if (File.Exists(BaketaSettingsPaths.PromotionSettingsPath))
        {
            try
            {
                var jsonContent = File.ReadAllText(BaketaSettingsPaths.PromotionSettingsPath);
                var promotionData = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);

                if (promotionData is not null)
                {
                    return CreatePromotionSettingsFromData(promotionData);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Issue #237] プロモーション設定ファイルの読み込みに失敗しました。デフォルト設定を使用します");
            }
        }

        // デフォルト設定（プロモーション未適用）
        return new UnifiedPromotionSettings(null, null, null, null, null);
    }

    /// <summary>
    /// [Issue #237] Dictionary からプロモーション設定を作成
    /// </summary>
    private static UnifiedPromotionSettings CreatePromotionSettingsFromData(Dictionary<string, object> data)
    {
        return new UnifiedPromotionSettings(
            GetStringFromValue(data.GetValueOrDefault("appliedPromotionCode")),
            GetNullableIntValue(data, "promotionPlanType"),
            GetStringFromValue(data.GetValueOrDefault("promotionExpiresAt")),
            GetStringFromValue(data.GetValueOrDefault("promotionAppliedAt")),
            GetStringFromValue(data.GetValueOrDefault("lastOnlineVerification")));
    }

    /// <summary>
    /// [Issue #237] Nullable int 値の取得
    /// </summary>
    private static int? GetNullableIntValue(Dictionary<string, object> settings, string key)
    {
        if (!settings.TryGetValue(key, out var value) || value is null)
            return null;

        return value switch
        {
            int intValue => intValue,
            JsonElement element when element.ValueKind == JsonValueKind.Number => element.GetInt32(),
            JsonElement element when element.ValueKind == JsonValueKind.Null => null,
            string stringValue when int.TryParse(stringValue, out var parsed) => parsed,
            _ => null
        };
    }

    private static UnifiedTranslationSettings CreateTranslationSettingsFromUser(
        Dictionary<string, object> userSettings,
        TranslationSettings appSettings)
    {
        // 🔥 [Issue #189] UIが保存する"selectedLanguagePair"プロパティを優先的に読み取る
        string sourceLanguage;
        string targetLanguage;

        var languagePairValue = userSettings.GetValueOrDefault("selectedLanguagePair");
        var languagePairString = GetStringFromValue(languagePairValue);

        if (!string.IsNullOrEmpty(languagePairString) && languagePairString.Contains('-'))
        {
            // 🔥 "ja-en" 形式をパース（source-target）
            var parts = languagePairString.Split('-', 2);
            if (parts.Length == 2)
            {
                sourceLanguage = parts[0].Trim().ToLowerInvariant();
                targetLanguage = parts[1].Trim().ToLowerInvariant();
            }
            else
            {
                // パース失敗時はデフォルト
                sourceLanguage = "en";
                targetLanguage = "ja";
            }
        }
        else
        {
            // レガシー形式にフォールバック（存在しない可能性が高い）
            sourceLanguage = LanguageCodeConverter.ToLanguageCode(
                userSettings.GetValueOrDefault("sourceLanguage")?.ToString() ?? "English");
            targetLanguage = LanguageCodeConverter.ToLanguageCode(
                userSettings.GetValueOrDefault("targetLanguage")?.ToString() ?? "Japanese");
        }

        return new UnifiedTranslationSettings(
            GetBoolValue(userSettings, "autoDetectSourceLanguage", appSettings.AutoDetectSourceLanguage),
            sourceLanguage,
            targetLanguage,
            userSettings.GetValueOrDefault("defaultEngine")?.ToString() ?? appSettings.DefaultEngine.ToString(),
            GetBoolValue(userSettings, "useLocalEngine", true),
            GetDoubleValue(userSettings, "confidenceThreshold", 0.7),
            GetIntValue(userSettings, "timeoutMs", appSettings.TimeoutSeconds * 1000),
            GetIntValue(userSettings, "overlayFontSize", 14),
            // [Issue #78 Phase 5] Cloud AI翻訳設定
            GetBoolValue(userSettings, "enableCloudAiTranslation", appSettings.EnableCloudAiTranslation));
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

    /// <summary>
    /// オブジェクトから文字列を取得（JsonElement対応）
    /// </summary>
    private static string? GetStringFromValue(object? value)
    {
        return value switch
        {
            null => null,
            string stringValue => stringValue,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => value.ToString()
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
                    "promotion-settings" => SettingsType.Promotion,
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
    int TimeoutMs,
    int OverlayFontSize,
    // [Issue #78 Phase 5] Cloud AI翻訳の有効化フラグ
    bool EnableCloudAiTranslation = true) : ITranslationSettings;

/// <summary>
/// 統一OCR設定実装
/// </summary>
internal sealed record UnifiedOcrSettings(
    string DefaultLanguage,
    double ConfidenceThreshold,
    int TimeoutMs,
    bool EnablePreprocessing,
    // [Issue #229] ボーダーライン信頼度緩和設定
    bool EnableBorderlineConfidenceRelaxation = true,
    double BorderlineMinConfidence = 0.60,
    double BorderlineRelaxedThreshold = 0.65,
    int BorderlineMinTextLength = 5,
    int BorderlineMinBoundsHeight = 25,
    double BorderlineMinAspectRatio = 2.0) : IOcrSettings;

/// <summary>
/// 統一アプリケーション設定実装
/// </summary>
internal sealed class UnifiedAppSettings(ITranslationSettings translation, IOcrSettings ocr, AppSettings appSettings) : IAppSettings
{
    public ITranslationSettings Translation { get; } = translation;
    public IOcrSettings Ocr { get; } = ocr;
    public string LogLevel { get; } = "Information"; // デフォルトログレベル
    public bool EnableDebugMode { get; } // デフォルトデバッグモード
}

/// <summary>
/// [Issue #237] 統一プロモーション設定実装
/// </summary>
internal sealed record UnifiedPromotionSettings(
    string? AppliedPromotionCode,
    int? PromotionPlanType,
    string? PromotionExpiresAt,
    string? PromotionAppliedAt,
    string? LastOnlineVerification) : IPromotionSettings
{
    /// <summary>
    /// プロモーションが有効かどうかを判定（拡張メソッドに委譲）
    /// </summary>
    public bool IsPromotionActive => this.IsCurrentlyActive();
}
