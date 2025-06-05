using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Baketa.Core.Services;
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
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // ユーザー設定ディレクトリを取得
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var settingsDirectory = Path.Combine(userProfile, ".baketa", "settings");
        _settingsFilePath = Path.Combine(settingsDirectory, "user-settings.json");
        
        // JSON シリアライゼーションオプション
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        
        _settings = new Dictionary<string, object>();
        
        // 設定ディレクトリを作成
        EnsureSettingsDirectoryExists();
        
        // 初期設定を読み込み
        _ = Task.Run(async () =>
        {
            try
            {
                await ReloadAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "初期設定の読み込みに失敗しました");
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
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "設定値の型変換に失敗しました: Key={Key}, Type={Type}", key, typeof(T).Name);
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
            await File.WriteAllTextAsync(_settingsFilePath, json).ConfigureAwait(false);
            
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
        catch
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
    private async Task CreateBackupAsync()
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "バックアップの作成に失敗しました");
        }
        
        await Task.CompletedTask.ConfigureAwait(false);
    }
    
    /// <summary>
    /// 破損した設定ファイルのバックアップを作成します
    /// </summary>
    private async Task CreateCorruptBackupAsync()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var corruptBackupPath = $"{_settingsFilePath}.corrupt.{timestamp}";
                File.Copy(_settingsFilePath, corruptBackupPath, true);
                _logger.LogWarning("破損した設定ファイルのバックアップを作成しました: {BackupPath}", corruptBackupPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "破損ファイルのバックアップ作成に失敗しました");
        }
        
        await Task.CompletedTask.ConfigureAwait(false);
    }
}