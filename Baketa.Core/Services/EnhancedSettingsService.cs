using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Settings;
using Baketa.Core.Settings.Migration;

namespace Baketa.Core.Services;

/// <summary>
/// 拡張設定サービス実装
/// 型安全な設定管理、変更通知、プロファイル管理、マイグレーション機能を提供
/// </summary>
public sealed class EnhancedSettingsService : ISettingsService, IDisposable
{
    private readonly ILogger<EnhancedSettingsService> _logger;
    private readonly ISettingMetadataService _metadataService;
    private readonly ISettingsMigrationManager _migrationManager;
    
    private AppSettings _settings;
    private readonly Dictionary<string, SettingChangeRecord> _changeHistory;
    private readonly HashSet<string> _favoriteSettings;
    private readonly object _lockObject = new();
    private readonly SemaphoreSlim _fileSemaphore = new(1, 1);
    private readonly string _settingsFilePath;
    private bool _disposed;
    
    // JsonSerializerOptionsをキャッシュしてパフォーマンスを向上
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true
    };

    #region Events
    
    /// <inheritdoc />
    public event EventHandler<SettingChangedEventArgs>? SettingChanged;
    
    /// <inheritdoc />
    public event EventHandler<GameProfileChangedEventArgs>? GameProfileChanged;
    
    /// <inheritdoc />
    public event EventHandler<SettingsSavedEventArgs>? SettingsSaved;
    
    #endregion

    /// <summary>
    /// EnhancedSettingsServiceを初期化します
    /// </summary>
    /// <param name="logger">ロガー</param>
    /// <param name="metadataService">メタデータサービス</param>
    /// <param name="migrationManager">マイグレーション管理サービス</param>
    /// <param name="settingsFilePath">設定ファイルパス（nullでデフォルト）</param>
    public EnhancedSettingsService(
        ILogger<EnhancedSettingsService> logger,
        ISettingMetadataService metadataService,
        ISettingsMigrationManager migrationManager,
        string? settingsFilePath = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
        _migrationManager = migrationManager ?? throw new ArgumentNullException(nameof(migrationManager));
        
        _settings = new AppSettings();
        _changeHistory = [];
        _favoriteSettings = [];
        
        _settingsFilePath = settingsFilePath ?? GetDefaultSettingsFilePath();
        
        // 起動時に設定を読み込み
        _ = InitializeAsync();
    }

    #region 基本設定操作

    /// <inheritdoc />
    public T GetValue<T>(string key, T defaultValue)
    {
        lock (_lockObject)
        {
            try
            {
                if (TryGetSettingValue(key, out var value) && value is T typedValue)
                {
                    return typedValue;
                }
                
                _logger.LogDebug("設定キー '{Key}' が見つからないか型が一致しません。デフォルト値を返します: {DefaultValue}", key, defaultValue);
                return defaultValue;
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "設定値の取得中にエラーが発生しました: {Key}", key);
                return defaultValue;
            }
        }
    }

    /// <inheritdoc />
    public void SetValue<T>(string key, T value)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Setting key cannot be null or empty.", nameof(key));
            
        lock (_lockObject)
        {
            try
            {
                var oldValue = GetValue(key, default(T)!);
                
                if (SetSettingValue(key, value))
                {
                    RecordChange(key, oldValue, value, SettingChangeType.Updated, "SetValue API");
                    OnSettingChanged(key, oldValue, value, GetCategoryFromKey(key), SettingChangeType.Updated);
                }
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or ArgumentNullException))
            {
                _logger.LogError(ex, "設定値の設定中にエラーが発生しました: {Key} = {Value}", key, value);
                throw;
            }
        }
    }

    /// <inheritdoc />
    public bool HasValue(string key)
    {
        lock (_lockObject)
        {
            return TryGetSettingValue(key, out _);
        }
    }

    /// <inheritdoc />
    public void RemoveValue(string key)
    {
        lock (_lockObject)
        {
            try
            {
                if (TryGetSettingValue(key, out var oldValue))
                {
                    if (RemoveSettingValue(key))
                    {
                        RecordChange(key, oldValue, null, SettingChangeType.Deleted, "RemoveValue API");
                        OnSettingChanged(key, oldValue, null, GetCategoryFromKey(key), SettingChangeType.Deleted);
                    }
                }
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "設定値の削除中にエラーが発生しました: {Key}", key);
                throw;
            }
        }
    }

    #endregion

    #region 型安全な設定操作

    /// <inheritdoc />
    public AppSettings GetSettings()
    {
        lock (_lockObject)
        {
            // ディープコピーを返して外部からの変更を防ぐ
            var json = JsonSerializer.Serialize(_settings, s_jsonOptions);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
    }

    /// <inheritdoc />
    public async Task SetSettingsAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        
        lock (_lockObject)
        {
            var oldSettings = _settings;
            _settings = settings;
            
            RecordChange("AppSettings", oldSettings, settings, SettingChangeType.Updated, "SetSettingsAsync API");
        }
        
        await SaveAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public T GetCategorySettings<T>() where T : class, new()
    {
        lock (_lockObject)
        {
            try
            {
                return GetCategorySettingsInternal<T>();
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or ArgumentNullException or ArgumentException))
            {
                _logger.LogError(ex, "カテゴリ設定の取得中にエラーが発生しました: {Type}", typeof(T).Name);
                return new T();
            }
        }
    }

    /// <inheritdoc />
    public async Task SetCategorySettingsAsync<T>(T settings) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(settings);
        
        lock (_lockObject)
        {
            var oldSettings = GetCategorySettingsInternal<T>();
            SetCategorySettingsInternal(settings);
            
            RecordChange(typeof(T).Name, oldSettings, settings, SettingChangeType.Updated, "SetCategorySettingsAsync API");
        }
        
        await SaveAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>() where T : class, new()
    {
        return await Task.FromResult(GetCategorySettings<T>()).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync<T>(T _) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(_);
        await SetCategorySettingsAsync(_).ConfigureAwait(false);
    }

    #endregion

    #region プロファイル管理

    /// <inheritdoc />
    public GameProfileSettings? GetGameProfile(string profileId)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        
        lock (_lockObject)
        {
            return _settings.GameProfiles.TryGetValue(profileId, out var profile) ? profile : null;
        }
    }

    /// <inheritdoc />
    public async Task SaveGameProfileAsync(string profileId, GameProfileSettings profile)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(profile);
        
        GameProfileSettings? oldProfile;
        
        lock (_lockObject)
        {
            oldProfile = GetGameProfile(profileId);
            _settings.GameProfiles[profileId] = profile;
        }
        
        var changeType = oldProfile == null ? ProfileChangeType.Created : ProfileChangeType.Updated;
        OnGameProfileChanged(profileId, profile, changeType);
        
        await SaveAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteGameProfileAsync(string profileId)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        
        GameProfileSettings? oldProfile;
        
        lock (_lockObject)
        {
            oldProfile = GetGameProfile(profileId);
            _settings.GameProfiles.Remove(profileId);
        }
        
        if (oldProfile != null)
        {
            OnGameProfileChanged(profileId, null, ProfileChangeType.Deleted);
            await SaveAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, GameProfileSettings> GetAllGameProfiles()
    {
        lock (_lockObject)
        {
            return new Dictionary<string, GameProfileSettings>(_settings.GameProfiles);
        }
    }

    /// <inheritdoc />
    public async Task SetActiveGameProfileAsync(string? profileId)
    {
        lock (_lockObject)
        {
            _settings.General.ActiveGameProfile = profileId;
        }
        
        await SaveAsync().ConfigureAwait(false);
        OnGameProfileChanged(profileId ?? string.Empty, null, ProfileChangeType.ActivationChanged);
    }

    /// <inheritdoc />
    public GameProfileSettings? GetActiveGameProfile()
    {
        lock (_lockObject)
        {
            var activeId = _settings.General.ActiveGameProfile;
            return string.IsNullOrEmpty(activeId) ? null : GetGameProfile(activeId);
        }
    }

    #endregion

    #region 永続化操作

    /// <inheritdoc />
    public async Task SaveAsync()
    {
        var startTime = DateTime.Now;
        var settingCount = 0;
        
        await _fileSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            string json;
            lock (_lockObject)
            {
                json = JsonSerializer.Serialize(_settings, s_jsonOptions);
                settingCount = CountSettings();
            }
            
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
            
            // Root cause solution: Implement retry logic for file access conflicts
            const int maxRetries = 3;
            const int retryDelayMs = 100;
            
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    await File.WriteAllTextAsync(_settingsFilePath, json).ConfigureAwait(false);
                    break; // Success, exit retry loop
                }
                catch (IOException ex) when (attempt < maxRetries - 1 && 
                    (ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("access", StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("File access conflict on attempt {Attempt}, retrying in {DelayMs}ms: {Message}", 
                        attempt + 1, retryDelayMs, ex.Message);
                    await Task.Delay(retryDelayMs).ConfigureAwait(false);
                }
            }
            
            var saveTime = (long)(DateTime.Now - startTime).TotalMilliseconds;
            
            _logger.LogInformation("設定を保存しました: {FilePath} ({SettingCount}項目, {SaveTime}ms)", 
                _settingsFilePath, settingCount, saveTime.ToString(System.Globalization.CultureInfo.InvariantCulture));
            
            OnSettingsSaved(_settingsFilePath, settingCount, saveTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "設定の保存に失敗しました: {FilePath}", _settingsFilePath);
            OnSettingsSaved(_settingsFilePath, ex.Message);
            throw;
        }
        finally
        {
            _fileSemaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task ReloadAsync()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                _logger.LogInformation("設定ファイルが存在しません。デフォルト設定を使用します: {FilePath}", _settingsFilePath);
                lock (_lockObject)
                {
                    _settings = new AppSettings();
                }
                return;
            }
            
            var json = await File.ReadAllTextAsync(_settingsFilePath).ConfigureAwait(false);
            var loadedSettings = JsonSerializer.Deserialize<AppSettings>(json);
            
            if (loadedSettings != null)
            {
                lock (_lockObject)
                {
                    _settings = loadedSettings;
                }
                
                _logger.LogInformation("設定を再読み込みしました: {FilePath}", _settingsFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "設定の再読み込みに失敗しました: {FilePath}", _settingsFilePath);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ResetToDefaultsAsync()
    {
        lock (_lockObject)
        {
            var oldSettings = _settings;
            _settings = new AppSettings();
            
            RecordChange("AppSettings", oldSettings, _settings, SettingChangeType.Reset, "ResetToDefaultsAsync API");
        }
        
        await SaveAsync().ConfigureAwait(false);
        _logger.LogInformation("設定をデフォルト値にリセットしました");
    }

    /// <inheritdoc />
    public Task CreateBackupAsync(string? _ = null)
    {
        _ ??= GenerateBackupFilePath();
        
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_)!);
            File.Copy(_settingsFilePath, _, true);
            
            _logger.LogInformation("設定のバックアップを作成しました: {BackupPath}", _);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "設定のバックアップ作成に失敗しました: {BackupPath}", _);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RestoreFromBackupAsync(string backupFilePath)
    {
        ArgumentNullException.ThrowIfNull(backupFilePath);
        
        if (!File.Exists(backupFilePath))
        {
            throw new FileNotFoundException($"バックアップファイルが見つかりません: {backupFilePath}");
        }
        
        try
        {
            File.Copy(backupFilePath, _settingsFilePath, true);
            await ReloadAsync().ConfigureAwait(false);
            
            _logger.LogInformation("設定をバックアップから復元しました: {BackupPath}", backupFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "設定の復元に失敗しました: {BackupPath}", backupFilePath);
            throw;
        }
    }

    #endregion

    #region 検証とマイグレーション

    /// <inheritdoc />
    public SettingsValidationResult ValidateSettings()
    {
        lock (_lockObject)
        {
            try
            {
                // TODO: 実際の検証ロジックを実装
                return SettingsValidationResult.CreateSuccess();
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "設定の検証中にエラーが発生しました");
                throw;
            }
        }
    }

    /// <inheritdoc />
    public bool RequiresMigration()
    {
        // TODO: 実際のマイグレーション判定ロジックを実装
        return false;
    }

    /// <inheritdoc />
    public Task MigrateSettingsAsync()
    {
        // TODO: 実際のマイグレーションロジックを実装
        return Task.CompletedTask;
    }

    #endregion

    #region 統計・情報

    /// <inheritdoc />
    public SettingsStatistics GetStatistics()
    {
        lock (_lockObject)
        {
            return SettingsStatistics.CreateEmpty(); // TODO: 実際の統計計算を実装
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<SettingChangeRecord> GetChangeHistory(int maxEntries = 100)
    {
        lock (_lockObject)
        {
            return [.. _changeHistory.Values
                .OrderByDescending(c => c.Timestamp)
                .Take(maxEntries)];
        }
    }

    /// <inheritdoc />
    public async Task AddToFavoritesAsync(string settingKey)
    {
        ArgumentNullException.ThrowIfNull(settingKey);
        
        lock (_lockObject)
        {
            _favoriteSettings.Add(settingKey);
        }
        
        await SaveAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveFromFavoritesAsync(string settingKey)
    {
        ArgumentNullException.ThrowIfNull(settingKey);
        
        lock (_lockObject)
        {
            _favoriteSettings.Remove(settingKey);
        }
        
        await SaveAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetFavoriteSettings()
    {
        lock (_lockObject)
        {
            return [.. _favoriteSettings];
        }
    }

    #endregion

    #region Private Methods

    private async Task InitializeAsync()
    {
        try
        {
            await ReloadAsync().ConfigureAwait(false);
            
            if (RequiresMigration())
            {
                _logger.LogInformation("設定のマイグレーションが必要です");
                await MigrateSettingsAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or ArgumentNullException or ArgumentException))
        {
            _logger.LogError(ex, "設定サービスの初期化中にエラーが発生しました");
        }
    }

    private bool TryGetSettingValue(string key, out object? value)
    {
        // TODO: 実際の設定値取得ロジックを実装
        value = null;
        return false;
    }

    private bool SetSettingValue(string key, object? value)
    {
        // TODO: 実際の設定値設定ロジックを実装
        return true;
    }

    private bool RemoveSettingValue(string key)
    {
        // TODO: 実際の設定値削除ロジックを実装
        return true;
    }

    private T GetCategorySettingsInternal<T>() where T : class, new()
    {
        // 実際のカテゴリ設定取得ロジックを実装
        var categoryName = typeof(T).Name.Replace("Settings", string.Empty, StringComparison.Ordinal);
        
        try
        {
            var property = typeof(AppSettings).GetProperty(categoryName);
            if (property != null && property.PropertyType == typeof(T))
            {
                return (property.GetValue(_settings) as T) ?? new T();
            }
            
            _logger.LogWarning("設定カテゴリ {Category} が見つかりません", categoryName);
            return new T();
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or ArgumentNullException or ArgumentException))
        {
            _logger.LogError(ex, "設定カテゴリ {Category} の取得に失敗しました", categoryName);
            return new T();
        }
    }

    private void SetCategorySettingsInternal<T>(T settings) where T : class
    {
        var categoryName = typeof(T).Name.Replace("Settings", string.Empty, StringComparison.Ordinal);
        
        try
        {
            var property = typeof(AppSettings).GetProperty(categoryName);
            if (property != null && property.PropertyType == typeof(T))
            {
                property.SetValue(_settings, settings);
                _logger.LogDebug("設定カテゴリ {Category} を更新しました", categoryName);
            }
            else
            {
                _logger.LogWarning("設定カテゴリ {Category} が見つかりません", categoryName);
            }
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or ArgumentNullException or ArgumentException))
        {
            _logger.LogError(ex, "設定カテゴリ {Category} の設定に失敗しました", categoryName);
            throw;
        }
    }

    private string GetCategoryFromKey(string key)
    {
        // TODO: キーからカテゴリを推測するロジックを実装
        return "General";
    }

    private void RecordChange(string key, object? oldValue, object? newValue, SettingChangeType changeType, string source)
    {
        var record = new SettingChangeRecord(
            key,
            GetCategoryFromKey(key),
            oldValue,
            newValue,
            changeType,
            source);
        
        _changeHistory[record.Id] = record;
        
        // 履歴のサイズ制限
        if (_changeHistory.Count > 1000)
        {
            var oldestId = _changeHistory.Values.OrderBy(r => r.Timestamp).First().Id;
            _changeHistory.Remove(oldestId);
        }
    }

    private int CountSettings()
    {
        // TODO: 実際の設定数カウントロジックを実装
        return 0;
    }

    private string GetDefaultSettingsFilePath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appDataPath, "Baketa", "settings.json");
    }

    private string GenerateBackupFilePath()
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var backupDir = Path.Combine(Path.GetDirectoryName(_settingsFilePath)!, "backups");
        return Path.Combine(backupDir, $"settings_backup_{timestamp}.json");
    }

    private void OnSettingChanged(string key, object? oldValue, object? newValue, string category, SettingChangeType changeType)
    {
        try
        {
            SettingChanged?.Invoke(this, new SettingChangedEventArgs(key, oldValue, newValue, category, changeType));
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            _logger.LogWarning(ex, "設定変更イベントの処理中にエラーが発生しました");
        }
    }

    private void OnGameProfileChanged(string profileId, GameProfileSettings? profile, ProfileChangeType changeType)
    {
        try
        {
            GameProfileChanged?.Invoke(this, new GameProfileChangedEventArgs(profileId, profile, changeType));
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            _logger.LogWarning(ex, "ゲームプロファイル変更イベントの処理中にエラーが発生しました");
        }
    }

    private void OnSettingsSaved(string filePath, int settingCount, long saveTime)
    {
        try
        {
            SettingsSaved?.Invoke(this, new SettingsSavedEventArgs(filePath, settingCount, saveTime));
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            _logger.LogWarning(ex, "設定保存イベントの処理中にエラーが発生しました");
        }
    }

    private void OnSettingsSaved(string _, string _1)
    {
        try
        {
            SettingsSaved?.Invoke(this, new SettingsSavedEventArgs(_, _1));
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            _logger.LogWarning(ex, "設定保存失敗イベントの処理中にエラーが発生しました");
        }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// リソースを解放します
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                // 変更があれば保存
                _ = SaveAsync();
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "設定サービス解放時の保存でエラーが発生しました");
            }
            
            _fileSemaphore?.Dispose();
            _disposed = true;
        }
    }

    #endregion
}
