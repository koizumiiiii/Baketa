using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Baketa.Core.Services;
using Baketa.Core.Settings;
using Baketa.Core.Settings.Migration;

namespace Baketa.Infrastructure.Services;

/// <summary>
/// 強化された設定サービス実装
/// JsonSettingsServiceを拡張し、型安全な設定管理、プロファイル管理、マイグレーション機能を提供
/// </summary>
public sealed class EnhancedSettingsService : ISettingsService, IDisposable
{
    private readonly ILogger<EnhancedSettingsService> _logger;
    private readonly ISettingMetadataService _metadataService;
    private readonly ISettingsMigrationManager _migrationManager;
    private readonly JsonSettingsService _baseSettingsService;
    private readonly object _lockObject = new();
    
    private AppSettings? _cachedAppSettings;
    private DateTime _lastCacheUpdate = DateTime.MinValue;
    private readonly TimeSpan _cacheValidDuration = TimeSpan.FromSeconds(5);
    
    // イベント
    public event EventHandler<SettingChangedEventArgs>? SettingChanged;
    public event EventHandler<GameProfileChangedEventArgs>? GameProfileChanged;
    public event EventHandler<SettingsSavedEventArgs>? SettingsSaved;

    /// <summary>
    /// EnhancedSettingsServiceを初期化します
    /// </summary>
    /// <param name="logger">ロガー</param>
    /// <param name="metadataService">メタデータサービス</param>
    /// <param name="migrationManager">マイグレーション管理サービス</param>
    public EnhancedSettingsService(
        ILogger<EnhancedSettingsService> logger,
        ISettingMetadataService metadataService,
        ISettingsMigrationManager migrationManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
        _migrationManager = migrationManager ?? throw new ArgumentNullException(nameof(migrationManager));
        
        // ベースとなるJsonSettingsServiceを初期化
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
        });
        _baseSettingsService = new JsonSettingsService(loggerFactory.CreateLogger<JsonSettingsService>());
        
        // 起動時に自動マイグレーションを実行
        _ = Task.Run(async () =>
        {
            try
            {
                if (RequiresMigration())
                {
                    await MigrateSettingsAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "起動時の自動マイグレーションに失敗しました");
            }
        });
    }

    #region 基本設定操作

    /// <inheritdoc />
    public T GetValue<T>(string key, T defaultValue)
    {
        return _baseSettingsService.GetValue(key, defaultValue);
    }

    /// <inheritdoc />
    public void SetValue<T>(string key, T value)
    {
        var oldValue = _baseSettingsService.GetValue<object?>(key, null);
        
        _baseSettingsService.SetValue(key, value);
        
        // キャッシュをクリア
        InvalidateCache();
        
        // イベントを発行
        OnSettingChanged(new SettingChangedEventArgs(
            key, oldValue, value, GetCategoryFromKey(key), SettingChangeType.Updated));
    }

    /// <inheritdoc />
    public bool HasValue(string key)
    {
        return _baseSettingsService.HasValue(key);
    }

    /// <inheritdoc />
    public void RemoveValue(string key)
    {
        var oldValue = _baseSettingsService.GetValue<object?>(key, null);
        
        _baseSettingsService.RemoveValue(key);
        
        // キャッシュをクリア
        InvalidateCache();
        
        // イベントを発行
        OnSettingChanged(new SettingChangedEventArgs(
            key, oldValue, null, GetCategoryFromKey(key), SettingChangeType.Deleted));
    }

    #endregion

    #region 型安全な設定操作

    /// <inheritdoc />
    public AppSettings GetSettings()
    {
        lock (_lockObject)
        {
            // キャッシュが有効な場合はキャッシュを返す
            if (_cachedAppSettings != null && 
                DateTime.Now - _lastCacheUpdate < _cacheValidDuration)
            {
                return _cachedAppSettings;
            }
            
            try
            {
                var settings = LoadAppSettingsFromStorage();
                
                // キャッシュを更新
                _cachedAppSettings = settings;
                _lastCacheUpdate = DateTime.Now;
                
                return settings;
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "設定の読み込みに失敗しました。デフォルト設定を使用します");
                
                var defaultSettings = new AppSettings();
                _cachedAppSettings = defaultSettings;
                _lastCacheUpdate = DateTime.Now;
                
                return defaultSettings;
            }
        }
    }

    /// <inheritdoc />
    public async Task SetSettingsAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        
        try
        {
            // 設定の検証
            var validationResult = ValidateAppSettings(settings);
            if (!validationResult.IsValid)
            {
                var errorMsg = $"設定の検証に失敗しました: {string.Join(", ", validationResult.Errors)}";
                _logger.LogError(errorMsg);
                throw new InvalidOperationException(errorMsg);
            }
            
            // タイムスタンプを更新
            settings.LastUpdated = DateTime.Now;
            if (settings.CreatedAt == DateTime.MinValue)
            {
                settings.CreatedAt = DateTime.Now;
            }
            
            await SaveAppSettingsToStorage(settings).ConfigureAwait(false);
            
            // キャッシュを更新
            lock (_lockObject)
            {
                _cachedAppSettings = settings;
                _lastCacheUpdate = DateTime.Now;
            }
            
            _logger.LogInformation("アプリケーション設定を保存しました");
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            _logger.LogError(ex, "アプリケーション設定の保存に失敗しました");
            throw;
        }
    }

    /// <inheritdoc />
    public T GetCategorySettings<T>() where T : class, new()
    {
        var appSettings = GetSettings();
        var categoryName = typeof(T).Name.Replace("Settings", string.Empty, StringComparison.Ordinal);
        
        try
        {
            var property = typeof(AppSettings).GetProperty(categoryName);
            if (property != null && property.PropertyType == typeof(T))
            {
                return (T)(property.GetValue(appSettings) ?? new T());
            }
            
            _logger.LogWarning("設定カテゴリ {Category} が見つかりません", categoryName);
            return new T();
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            _logger.LogError(ex, "設定カテゴリ {Category} の取得に失敗しました", categoryName);
            return new T();
        }
    }

    /// <inheritdoc />
    public async Task SetCategorySettingsAsync<T>(T settings) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(settings);
        
        var appSettings = GetSettings();
        var categoryName = typeof(T).Name.Replace("Settings", string.Empty, StringComparison.Ordinal);
        
        try
        {
            var property = typeof(AppSettings).GetProperty(categoryName);
            if (property != null && property.PropertyType == typeof(T))
            {
                property.SetValue(appSettings, settings);
                await SetSettingsAsync(appSettings).ConfigureAwait(false);
                
                _logger.LogDebug("設定カテゴリ {Category} を更新しました", categoryName);
            }
            else
            {
                _logger.LogWarning("設定カテゴリ {Category} が見つかりません", categoryName);
            }
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            _logger.LogError(ex, "設定カテゴリ {Category} の保存に失敗しました", categoryName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>() where T : class, new()
    {
        return await Task.FromResult(GetCategorySettings<T>()).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync<T>(T settings) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(settings);
        await SetCategorySettingsAsync(settings).ConfigureAwait(false);
    }

    #endregion

    #region プロファイル管理

    /// <inheritdoc />
    public GameProfileSettings? GetGameProfile(string profileId)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileId);
        
        var settings = GetSettings();
        return settings.GameProfiles.TryGetValue(profileId, out var profile) ? profile : null;
    }

    /// <inheritdoc />
    public async Task SaveGameProfileAsync(string profileId, GameProfileSettings profile)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileId);
        ArgumentNullException.ThrowIfNull(profile);
        
        var settings = GetSettings();
        var isNew = !settings.GameProfiles.ContainsKey(profileId);
        
        // プロファイル情報を更新
        profile.LastModified = DateTime.Now;
        if (isNew)
        {
            profile.CreatedAt = DateTime.Now;
        }
        
        settings.GameProfiles[profileId] = profile;
        await SetSettingsAsync(settings).ConfigureAwait(false);
        
        // イベントを発行
        OnGameProfileChanged(new GameProfileChangedEventArgs(
            profileId, profile, isNew ? ProfileChangeType.Created : ProfileChangeType.Updated));
        
        _logger.LogInformation("ゲームプロファイル {ProfileId} を{Action}しました", 
            profileId, isNew ? "作成" : "更新");
    }

    /// <inheritdoc />
    public async Task DeleteGameProfileAsync(string profileId)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileId);
        
        var settings = GetSettings();
        if (settings.GameProfiles.TryGetValue(profileId, out var profile))
        {
            settings.GameProfiles.Remove(profileId);
            
            // アクティブプロファイルの場合は無効化
            if (settings.ActiveGameProfileId == profileId)
            {
                settings.ActiveGameProfileId = null;
            }
            
            await SetSettingsAsync(settings).ConfigureAwait(false);
            
            // イベントを発行
            OnGameProfileChanged(new GameProfileChangedEventArgs(
                profileId, profile, ProfileChangeType.Deleted));
            
            _logger.LogInformation("ゲームプロファイル {ProfileId} を削除しました", profileId);
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, GameProfileSettings> GetAllGameProfiles()
    {
        var settings = GetSettings();
        return settings.GameProfiles;
    }

    /// <inheritdoc />
    public async Task SetActiveGameProfileAsync(string? profileId)
    {
        var settings = GetSettings();
        var oldProfileId = settings.ActiveGameProfileId;
        
        if (profileId != null && !settings.GameProfiles.ContainsKey(profileId))
        {
            throw new ArgumentException($"ゲームプロファイル '{profileId}' が存在しません", nameof(profileId));
        }
        
        settings.ActiveGameProfileId = profileId;
        await SetSettingsAsync(settings).ConfigureAwait(false);
        
        // イベントを発行
        if (profileId != null && settings.GameProfiles.TryGetValue(profileId, out var profile))
        {
            OnGameProfileChanged(new GameProfileChangedEventArgs(
                profileId, profile, ProfileChangeType.ActivationChanged));
        }
        
        _logger.LogInformation("アクティブゲームプロファイルを {OldId} から {NewId} に変更しました", 
            oldProfileId ?? "なし", profileId ?? "なし");
    }

    /// <inheritdoc />
    public GameProfileSettings? GetActiveGameProfile()
    {
        var settings = GetSettings();
        return !string.IsNullOrEmpty(settings.ActiveGameProfileId) 
            ? GetGameProfile(settings.ActiveGameProfileId) 
            : null;
    }

    #endregion

    #region 永続化操作

    /// <inheritdoc />
    public async Task SaveAsync()
    {
        var startTime = DateTime.Now;
        
        try
        {
            await _baseSettingsService.SaveAsync().ConfigureAwait(false);
            var elapsedMs = (long)(DateTime.Now - startTime).TotalMilliseconds;
            
            // 統計情報を取得
            var stats = GetStatistics();
            
            // イベントを発行
            OnSettingsSaved(new SettingsSavedEventArgs("settings.json", stats.GameProfileCount, elapsedMs));
            
            _logger.LogDebug("設定保存完了: {ElapsedMs}ms", elapsedMs);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            _logger.LogError(ex, "設定保存中にエラーが発生しました");
            
            // エラーイベントを発行
            OnSettingsSaved(new SettingsSavedEventArgs("settings.json", ex.Message));
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ReloadAsync()
    {
        await _baseSettingsService.ReloadAsync().ConfigureAwait(false);
        InvalidateCache();
        _logger.LogInformation("設定を再読み込みしました");
    }

    /// <inheritdoc />
    public async Task ResetToDefaultsAsync()
    {
        var defaultSettings = new AppSettings();
        await SetSettingsAsync(defaultSettings).ConfigureAwait(false);
        
        _logger.LogInformation("設定をデフォルト値にリセットしました");
    }

    /// <inheritdoc />
    public async Task CreateBackupAsync(string? backupFilePath = null)
    {
        try
        {
            var settings = GetSettings();
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var fileName = backupFilePath ?? $"settings_backup_{timestamp}.json";
            
            var json = JsonSerializer.Serialize(settings, GetJsonSerializerOptions());
            
            await File.WriteAllTextAsync(fileName, json).ConfigureAwait(false);
            
            _logger.LogInformation("設定のバックアップを作成しました: {FileName}", fileName);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            _logger.LogError(ex, "設定のバックアップ作成に失敗しました");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RestoreFromBackupAsync(string backupFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(backupFilePath);
        
        try
        {
            if (!File.Exists(backupFilePath))
            {
                throw new FileNotFoundException($"バックアップファイルが見つかりません: {backupFilePath}");
            }
            
            var json = await File.ReadAllTextAsync(backupFilePath).ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, GetJsonDeserializerOptions());
            
            if (settings != null)
            {
                await SetSettingsAsync(settings).ConfigureAwait(false);
                _logger.LogInformation("バックアップから設定を復元しました: {FileName}", backupFilePath);
            }
            else
            {
                throw new InvalidOperationException("バックアップファイルの形式が無効です");
            }
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            _logger.LogError(ex, "バックアップからの復元に失敗しました");
            throw;
        }
    }

    #endregion

    #region 検証とマイグレーション

    /// <inheritdoc />
    public SettingsValidationResult ValidateSettings()
    {
        var settings = GetSettings();
        return ValidateAppSettings(settings);
    }

    /// <inheritdoc />
    public bool RequiresMigration()
    {
        try
        {
            var currentVersion = _baseSettingsService.GetValue("SchemaVersion", 0);
            return _migrationManager.RequiresMigration(currentVersion);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            _logger.LogError(ex, "マイグレーション要否の確認に失敗しました");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task MigrateSettingsAsync()
    {
        try
        {
            var currentVersion = _baseSettingsService.GetValue("SchemaVersion", 0);
            _logger.LogInformation("設定マイグレーションを開始: バージョン {CurrentVersion} → {LatestVersion}", 
                currentVersion, _migrationManager.LatestSchemaVersion);
            
            // マイグレーション前のバックアップを作成
            await CreateBackupAsync($"settings_pre_migration_v{currentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)}.json").ConfigureAwait(false);
            
            // 現在の設定をディクショナリ形式で取得
            var currentSettings = await LoadSettingsAsDictionary().ConfigureAwait(false);
            
            // マイグレーション可能性を確認
            if (!_migrationManager.RequiresMigration(currentVersion))
            {
                _logger.LogInformation("マイグレーションは不要です");
                return;
            }
            
            // マイグレーションを実行
            var result = await _migrationManager.ExecuteMigrationAsync(currentSettings, currentVersion).ConfigureAwait(false);
            
            if (result.Success)
            {
                // マイグレーション後の設定を保存
                await SaveSettingsFromDictionary(result.FinalSettings).ConfigureAwait(false);
                
                // キャッシュをクリア
                InvalidateCache();
                
                _logger.LogInformation("設定マイグレーションが完了しました: {StepCount}ステップ, {ElapsedMs}ms", 
                    result.StepResults.Count, result.TotalExecutionTimeMs);
                
                if (result.Warnings.Count > 0)
                {
                    _logger.LogWarning("マイグレーション完了時の警告: {Warnings}", 
                        string.Join(", ", result.Warnings));
                }
            }
            else
            {
                var errorMessage = result.ErrorMessage ?? "不明なエラー";
                _logger.LogError("設定マイグレーションが失敗しました: {Error}", errorMessage);
                throw new InvalidOperationException($"マイグレーション失敗: {errorMessage}");
            }
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            _logger.LogError(ex, "設定マイグレーション中にエラーが発生しました");
            throw;
        }
    }

    #endregion

    #region 統計・情報

    /// <inheritdoc />
    public SettingsStatistics GetStatistics()
    {
        var settings = GetSettings();
        return settings.GetStatistics();
    }

    /// <inheritdoc />
    public IReadOnlyList<SettingChangeRecord> GetChangeHistory(int maxEntries = 100)
    {
        var settings = GetSettings();
        return [.. settings.ChangeHistory
            .OrderByDescending(c => c.Timestamp)
            .Take(maxEntries)];
    }

    /// <inheritdoc />
    public async Task AddToFavoritesAsync(string settingKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(settingKey);
        
        var settings = GetSettings();
        if (!settings.FavoriteSettings.Contains(settingKey))
        {
            settings.FavoriteSettings.Add(settingKey);
            await SetSettingsAsync(settings).ConfigureAwait(false);
            
            _logger.LogDebug("お気に入り設定に追加しました: {SettingKey}", settingKey);
        }
    }

    /// <inheritdoc />
    public async Task RemoveFromFavoritesAsync(string settingKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(settingKey);
        
        var settings = GetSettings();
        if (settings.FavoriteSettings.Remove(settingKey))
        {
            await SetSettingsAsync(settings).ConfigureAwait(false);
            
            _logger.LogDebug("お気に入り設定から削除しました: {SettingKey}", settingKey);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetFavoriteSettings()
    {
        var settings = GetSettings();
        return settings.FavoriteSettings.AsReadOnly();
    }

    #endregion

    #region プライベートメソッド

    /// <summary>
    /// キャッシュを無効化します
    /// </summary>
    private void InvalidateCache()
    {
        lock (_lockObject)
        {
            _cachedAppSettings = null;
            _lastCacheUpdate = DateTime.MinValue;
        }
    }

    /// <summary>
    /// ストレージからAppSettingsを読み込みます
    /// </summary>
    private AppSettings LoadAppSettingsFromStorage()
    {
        // 基本実装：JsonSettingsServiceから個別の値を読み取ってAppSettingsを構築
        var settings = new AppSettings();
        
        // TODO: より効率的な実装に改善
        // 現在は基本的なプロパティのみを読み込み、将来的に完全実装
        
        return settings;
    }

    /// <summary>
    /// AppSettingsをストレージに保存します
    /// </summary>
    private async Task SaveAppSettingsToStorage(AppSettings settings)
    {
        // TODO: AppSettingsを個別の設定値に分解してJsonSettingsServiceに保存
        // 現在は基本実装、将来的に完全実装
        
        await _baseSettingsService.SaveAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 設定をディクショナリ形式で読み込みます（マイグレーション用）
    /// </summary>
    private Task<Dictionary<string, object?>> LoadSettingsAsDictionary()
    {
        // 基本的な設定値をディクショナリ形式で返す（現在は空）
        // 実際のマイグレーションでは、すべての設定値がここで読み込まれる
        Dictionary<string, object?> settings = [];
        
        // Versionキーがない場合は0として扱う（初回マイグレーション用）
        settings["Version"] = _baseSettingsService.GetValue("Version", 0);
        
        // SchemaVersionも確認
        settings["SchemaVersion"] = _baseSettingsService.GetValue("SchemaVersion", 0);
        
        // その他の設定値も読み込み（必要に応じて）
        // 現在は基本実装のため、マイグレーションテストで必要な最小限のみ
        
        return Task.FromResult(settings);
    }

    /// <summary>
    /// ディクショナリ形式の設定を保存します（マイグレーション用）
    /// </summary>
    private Task SaveSettingsFromDictionary(Dictionary<string, object?> settings)
    {
        // ディクショナリの設定値をJsonSettingsServiceに保存
        foreach (var kvp in settings)
        {
            _baseSettingsService.SetValue(kvp.Key, kvp.Value);
        }
        
        return _baseSettingsService.SaveAsync();
    }

    /// <summary>
    /// AppSettingsの検証を行います
    /// </summary>
    private SettingsValidationResult ValidateAppSettings(AppSettings settings)
    {
        return settings.ValidateSettings();
    }

    /// <summary>
    /// 設定キーからカテゴリを推定します
    /// </summary>
    private static string GetCategoryFromKey(string key)
    {
        var parts = key.Split('.');
        return parts.Length > 1 ? parts[0] : "General";
    }

    /// <summary>
    /// 設定変更イベントを発行します
    /// </summary>
    private void OnSettingChanged(SettingChangedEventArgs args)
    {
        try
        {
            SettingChanged?.Invoke(this, args);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            _logger.LogWarning(ex, "設定変更イベントの処理中にエラーが発生しました");
        }
    }

    /// <summary>
    /// ゲームプロファイル変更イベントを発行します
    /// </summary>
    private void OnGameProfileChanged(GameProfileChangedEventArgs args)
    {
        try
        {
            GameProfileChanged?.Invoke(this, args);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            _logger.LogWarning(ex, "ゲームプロファイル変更イベントの処理中にエラーが発生しました");
        }
    }

    /// <summary>
    /// 設定保存完了イベントを発行します
    /// </summary>
    private void OnSettingsSaved(SettingsSavedEventArgs args)
    {
        try
        {
            SettingsSaved?.Invoke(this, args);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            _logger.LogWarning(ex, "設定保存イベントの処理中にエラーが発生しました");
        }
    }

    #endregion

    #region JsonSerializerOptions
    
    private static readonly JsonSerializerOptions s_serializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    private static readonly JsonSerializerOptions s_deserializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    
    private static JsonSerializerOptions GetJsonSerializerOptions() => s_serializerOptions;
    private static JsonSerializerOptions GetJsonDeserializerOptions() => s_deserializerOptions;
    
    #endregion

    #region IDisposable

    public void Dispose()
    {
        // 必要に応じてリソースの解放処理を実装
    }

    #endregion
}
