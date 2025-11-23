using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Baketa.Core.Services;
using Baketa.Core.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services;

/// <summary>
/// JSON ベースの設定サービス実装
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private readonly ILogger<JsonSettingsService> _logger;
    private readonly string _settingsFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Dictionary<string, object> _settings;
    private readonly object _lockObject = new();

    /// <summary>
    /// JsonSettingsService を初期化します
    /// </summary>
    /// <param name="logger">ロガー</param>
    public JsonSettingsService(ILogger<JsonSettingsService> logger)
        : this(logger, null)
    {
    }

    /// <summary>
    /// JsonSettingsService を初期化します（テスト用コンストラクタ）
    /// </summary>
    /// <param name="logger">ロガー</param>
    /// <param name="settingsFilePath">設定ファイルパス（nullでデフォルト）</param>
    public JsonSettingsService(ILogger<JsonSettingsService> logger, string? settingsFilePath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // ファイルパスの決定
        if (string.IsNullOrWhiteSpace(settingsFilePath))
        {
            // ユーザー設定ディレクトリを取得
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var settingsDirectory = Path.Combine(userProfile, ".baketa", "settings");
            _settingsFilePath = Path.Combine(settingsDirectory, "user-settings.json");
        }
        else
        {
            _settingsFilePath = settingsFilePath;
        }

        // JSON シリアライゼーションオプション
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        _settings = [];

        // 設定ディレクトリを作成
        EnsureSettingsDirectoryExists();

        // 初期設定を読み込み
        _ = Task.Run(async () =>
        {
            try
            {
                await ReloadAsync().ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                _logger.LogDebug("設定ファイルが見つかりません。初回起動時の正常な状態です");
            }
            catch (DirectoryNotFoundException)
            {
                _logger.LogDebug("設定ディレクトリが見つかりません。初回起動時の正常な状態です");
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "設定ファイルへのアクセス権限がありません");
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "設定ファイルの入出力エラーが発生しました");
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "設定ファイルの形式が無効です。デフォルト設定を使用します");
            }
        });
    }

    /// <inheritdoc />
    public T GetValue<T>(string key, T defaultValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        lock (_lockObject)
        {
            if (_settings.TryGetValue(key, out var value))
            {
                try
                {
                    // JsonElementからの変換処理
                    if (value is JsonElement jsonElement)
                    {
                        return ConvertJsonElement<T>(jsonElement, defaultValue);
                    }

                    // 直接変換可能な場合
                    if (value is T directValue)
                    {
                        return directValue;
                    }

                    // 型変換を試行
                    if (typeof(T) == typeof(string))
                    {
                        return (T)(object)value.ToString()!;
                    }

                    // その他の型変換
                    return (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (InvalidCastException ex)
                {
                    _logger.LogWarning(ex, "設定値の型変換に失敗しました: Key={Key}, Type={Type}", key, typeof(T).Name);
                    return defaultValue;
                }
                catch (FormatException ex)
                {
                    _logger.LogWarning(ex, "設定値の形式が無効です: Key={Key}, Type={Type}", key, typeof(T).Name);
                    return defaultValue;
                }
                catch (OverflowException ex)
                {
                    _logger.LogWarning(ex, "設定値が範囲外です: Key={Key}, Type={Type}", key, typeof(T).Name);
                    return defaultValue;
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning(ex, "設定値の引数が無効です: Key={Key}, Type={Type}", key, typeof(T).Name);
                    return defaultValue;
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "JSONデシリアライゼーションに失敗しました: Key={Key}, Type={Type}", key, typeof(T).Name);
                    return defaultValue;
                }
            }

            return defaultValue;
        }
    }

    /// <inheritdoc />
    public void SetValue<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        lock (_lockObject)
        {
            if (value == null)
            {
                _settings.Remove(key);
            }
            else
            {
                _settings[key] = value;
            }
        }

        _logger.LogDebug("設定値を更新しました: Key={Key}, Value={Value}", key, value);
    }

    /// <inheritdoc />
    public bool HasValue(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        lock (_lockObject)
        {
            return _settings.ContainsKey(key);
        }
    }

    /// <inheritdoc />
    public void RemoveValue(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        lock (_lockObject)
        {
            _settings.Remove(key);
        }

        _logger.LogDebug("設定値を削除しました: Key={Key}", key);
    }

    /// <inheritdoc />
    public async Task SaveAsync()
    {
        try
        {
            Dictionary<string, object> settingsToSave;

            lock (_lockObject)
            {
                settingsToSave = new Dictionary<string, object>(_settings);
            }

            EnsureSettingsDirectoryExists();

            // バックアップ作成
            await CreateBackupAsync().ConfigureAwait(false);

            // JSON に変換して保存
            var json = JsonSerializer.Serialize(settingsToSave, _jsonOptions);

            // 🔥 [CI_FIX] ファイルアクセス競合を回避するため、排他的アクセス + リトライロジック
            // Issue: CI/CD環境で複数スレッドが同時にファイルアクセスし IOException が発生
            // Solution: FileStream with FileShare.None で排他制御、3回リトライで堅牢性向上
            const int maxRetries = 3;
            const int retryDelayMs = 50;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    // FileShare.None で排他的アクセスを確保（他プロセス/スレッドからの読み書き禁止）
                    using (var stream = new FileStream(_settingsFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(stream))
                    {
                        await writer.WriteAsync(json).ConfigureAwait(false);
                    }
                    break; // 成功したらループ脱出
                }
                catch (IOException ex) when (attempt < maxRetries - 1)
                {
                    // 最後の試行以外では、リトライ前に短時間待機
                    _logger.LogWarning(ex, "ファイル保存リトライ {Attempt}/{MaxRetries}: {FilePath}", attempt + 1, maxRetries, _settingsFilePath);
                    await Task.Delay(retryDelayMs).ConfigureAwait(false);
                }
            }

            _logger.LogInformation("設定を保存しました: {FilePath}", _settingsFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "設定の保存に失敗しました");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ReloadAsync()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                _logger.LogDebug("設定ファイルが存在しません。新規作成します: {FilePath}", _settingsFilePath);

                lock (_lockObject)
                {
                    _settings.Clear();
                }
                return;
            }

            var json = await File.ReadAllTextAsync(_settingsFilePath).ConfigureAwait(false);
            var loadedSettings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, _jsonOptions);

            if (loadedSettings != null)
            {
                lock (_lockObject)
                {
                    _settings.Clear();
                    foreach (var kvp in loadedSettings)
                    {
                        _settings[kvp.Key] = kvp.Value;
                    }
                }

                _logger.LogInformation("設定を読み込みました: {Count} 項目", loadedSettings.Count);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "設定ファイルのJSONパースに失敗しました");

            // 破損した設定ファイルのバックアップを作成
            await CreateCorruptBackupAsync().ConfigureAwait(false);

            // 設定をクリア
            lock (_lockObject)
            {
                _settings.Clear();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "設定の読み込みに失敗しました");
            throw;
        }
    }

    /// <summary>
    /// JsonElement から指定された型に変換します
    /// </summary>
    private static T ConvertJsonElement<T>(JsonElement jsonElement, T defaultValue)
    {
        try
        {
            var targetType = typeof(T);

            // null許容型の処理
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                if (jsonElement.ValueKind == JsonValueKind.Null)
                {
                    return defaultValue;
                }

                targetType = Nullable.GetUnderlyingType(targetType)!;
            }

            // 型別の変換
            return targetType.Name switch
            {
                nameof(String) => (T)(object)jsonElement.GetString()!,
                nameof(Boolean) => (T)(object)jsonElement.GetBoolean(),
                nameof(Int32) => (T)(object)jsonElement.GetInt32(),
                nameof(Int64) => (T)(object)jsonElement.GetInt64(),
                nameof(Double) => (T)(object)jsonElement.GetDouble(),
                nameof(Decimal) => (T)(object)jsonElement.GetDecimal(),
                nameof(DateTime) => (T)(object)jsonElement.GetDateTime(),
                _ => JsonSerializer.Deserialize<T>(jsonElement.GetRawText())!
            };
        }
        catch (JsonException)
        {
            return defaultValue;
        }
        catch (InvalidOperationException)
        {
            return defaultValue;
        }
        catch (FormatException)
        {
            return defaultValue;
        }
        catch (InvalidCastException)
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// 設定ディレクトリの存在を確認し、必要に応じて作成します
    /// </summary>
    private void EnsureSettingsDirectoryExists()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogInformation("設定ディレクトリを作成しました: {Directory}", directory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "設定ディレクトリの作成に失敗しました");
            throw;
        }
    }

    /// <summary>
    /// 設定ファイルのバックアップを作成します
    /// </summary>
    private Task CreateBackupAsync()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var backupPath = $"{_settingsFilePath}.backup";
                File.Copy(_settingsFilePath, backupPath, true);
                _logger.LogDebug("バックアップを作成しました: {BackupPath}", backupPath);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "バックアップファイルへのアクセスが拒否されました");
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogWarning(ex, "バックアップディレクトリが見つかりません");
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "バックアップファイルの入出力エラーが発生しました");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 破損した設定ファイルのバックアップを作成します
    /// </summary>
    private Task CreateCorruptBackupAsync()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
                var corruptBackupPath = $"{_settingsFilePath}.corrupt.{timestamp}";
                File.Copy(_settingsFilePath, corruptBackupPath, true);
                _logger.LogWarning("破損した設定ファイルのバックアップを作成しました: {BackupPath}", corruptBackupPath);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "破損ファイルのバックアップ作成権限がありません");
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogWarning(ex, "破損ファイルのバックアップディレクトリが見つかりません");
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "破損ファイルのバックアップ作成中に入出力エラーが発生しました");
        }

        return Task.CompletedTask;
    }

    #region ISettingsServiceの不足メソッド実装

    /// <inheritdoc />
    public AppSettings GetSettings()
    {
        // TODO: 実際の実装
        return new AppSettings();
    }

    /// <inheritdoc />
    public Task SetSettingsAsync(AppSettings settings)
    {
        // TODO: 実際の実装
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public T GetCategorySettings<T>() where T : class, new()
    {
        // TODO: 実際の実装
        return new T();
    }

    /// <inheritdoc />
    public Task SetCategorySettingsAsync<T>(T settings) where T : class, new()
    {
        // TODO: 実際の実装
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public GameProfileSettings? GetGameProfile(string profileId)
    {
        // TODO: 実際の実装
        return null;
    }

    /// <inheritdoc />
    public Task SaveGameProfileAsync(string profileId, GameProfileSettings profile)
    {
        // TODO: 実際の実装
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteGameProfileAsync(string profileId)
    {
        // TODO: 実際の実装
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, GameProfileSettings> GetAllGameProfiles()
    {
        // TODO: 実際の実装
        return new Dictionary<string, GameProfileSettings>();
    }

    /// <inheritdoc />
    public Task SetActiveGameProfileAsync(string? profileId)
    {
        // TODO: 実際の実装
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public GameProfileSettings? GetActiveGameProfile()
    {
        // TODO: 実際の実装
        return null;
    }

    /// <inheritdoc />
    public async Task ResetToDefaultsAsync()
    {
        lock (_lockObject)
        {
            _settings.Clear();
        }

        await SaveAsync().ConfigureAwait(false);
        _logger.LogInformation("設定をデフォルト値にリセットしました");
    }

    /// <inheritdoc />
    public Task CreateBackupAsync(string? backupFilePath = null)
    {
        backupFilePath ??= $"{_settingsFilePath}.backup_{DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture)}";

        try
        {
            if (File.Exists(_settingsFilePath))
            {
                File.Copy(_settingsFilePath, backupFilePath, true);
                _logger.LogInformation("設定のバックアップを作成しました: {BackupPath}", backupFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "設定のバックアップ作成に失敗しました");
            throw;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RestoreFromBackupAsync(string backupFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(backupFilePath);

        if (!File.Exists(backupFilePath))
        {
            throw new FileNotFoundException($"バックアップファイルが見つかりません: {backupFilePath}");
        }

        try
        {
            File.Copy(backupFilePath, _settingsFilePath, true);
            await ReloadAsync().ConfigureAwait(false);

            _logger.LogInformation("バックアップから設定を復元しました: {BackupPath}", backupFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "バックアップからの復元に失敗しました");
            throw;
        }
    }

    /// <inheritdoc />
    public SettingsValidationResult ValidateSettings()
    {
        // TODO: 実際の検証ロジックを実装
        return SettingsValidationResult.CreateSuccess();
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

    /// <inheritdoc />
#pragma warning disable CS0067 // インターフェース実装のためにイベントを定義しているが、将来的な機能拡張のため未使用
    public event EventHandler<SettingChangedEventArgs>? SettingChanged;

    /// <inheritdoc />
    public event EventHandler<GameProfileChangedEventArgs>? GameProfileChanged;

    /// <inheritdoc />
    public event EventHandler<SettingsSavedEventArgs>? SettingsSaved;
#pragma warning restore CS0067

    /// <inheritdoc />
    public SettingsStatistics GetStatistics()
    {
        // TODO: 実際の統計情報を実装
        return SettingsStatistics.CreateEmpty();
    }

    /// <inheritdoc />
    public IReadOnlyList<SettingChangeRecord> GetChangeHistory(int maxEntries = 100)
    {
        // TODO: 実際の変更履歴を実装
        return [];
    }

    /// <inheritdoc />
    public Task AddToFavoritesAsync(string settingKey)
    {
        // TODO: 実際のお気に入り機能を実装
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveFromFavoritesAsync(string settingKey)
    {
        // TODO: 実際のお気に入り機能を実装
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetFavoriteSettings()
    {
        // TODO: 実際のお気に入り機能を実装
        return [];
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
}
