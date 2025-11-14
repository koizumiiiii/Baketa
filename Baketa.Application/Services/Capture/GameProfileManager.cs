using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using SettingsGameCaptureProfile = Baketa.Core.Settings.GameCaptureProfile;

namespace Baketa.Application.Services.Capture;

/// <summary>
/// ゲームキャプチャプロファイル管理サービス
/// </summary>
public interface IGameProfileManager
{
    /// <summary>
    /// 利用可能なプロファイル一覧を取得します
    /// </summary>
    /// <returns>プロファイルのリスト</returns>
    Task<IReadOnlyList<SettingsGameCaptureProfile>> GetProfilesAsync();

    /// <summary>
    /// プロファイルを作成または更新します
    /// </summary>
    /// <param name="profile">プロファイル</param>
    Task SaveProfileAsync(SettingsGameCaptureProfile profile);

    /// <summary>
    /// プロファイルを削除します
    /// </summary>
    /// <param name="profileName">削除するプロファイル名</param>
    Task DeleteProfileAsync(string profileName);

    /// <summary>
    /// 指定されたゲームプロセス名に適合するプロファイルを検索します
    /// </summary>
    /// <param name="processName">プロセス名</param>
    /// <param name="windowTitle">ウィンドウタイトル</param>
    /// <returns>適合するプロファイル（見つからない場合はnull）</returns>
    Task<SettingsGameCaptureProfile?> FindMatchingProfileAsync(string processName, string? windowTitle = null);

    /// <summary>
    /// デフォルトプロファイルを取得します
    /// </summary>
    /// <returns>デフォルトプロファイル</returns>
    SettingsGameCaptureProfile GetDefaultProfile();
}

/// <summary>
/// ゲームキャプチャプロファイル管理サービスの実装
/// </summary>
public sealed class GameProfileManager : IGameProfileManager
{
    private readonly ILogger<GameProfileManager>? _logger;
    private readonly string _profilesDirectory;
    private readonly Dictionary<string, SettingsGameCaptureProfile> _profiles = [];
    private readonly object _syncLock = new();

    // JsonSerializerオプションをキャッシュ
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public GameProfileManager(ILogger<GameProfileManager>? logger = null)
    {
        _logger = logger;

        // プロファイル保存ディレクトリの設定
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _profilesDirectory = Path.Combine(appDataPath, "Baketa", "CaptureProfiles");

        // ディレクトリが存在しない場合は作成
        if (!Directory.Exists(_profilesDirectory))
        {
            Directory.CreateDirectory(_profilesDirectory);
        }

        // 初期化時にプロファイルを読み込み
        _ = LoadProfilesAsync();
    }

    public async Task<IReadOnlyList<SettingsGameCaptureProfile>> GetProfilesAsync()
    {
        await LoadProfilesAsync().ConfigureAwait(false);

        lock (_syncLock)
        {
            return [.. _profiles.Values];
        }
    }

    public async Task SaveProfileAsync(SettingsGameCaptureProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrWhiteSpace(profile.Name))
            throw new ArgumentException("プロファイル名が指定されていません", nameof(profile));

        profile.UpdatedAt = DateTime.Now;

        // ファイルに保存
        var fileName = GetProfileFileName(profile.Name);
        var filePath = Path.Combine(_profilesDirectory, fileName);

        try
        {
            var json = JsonSerializer.Serialize(profile, WriteOptions);

            await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);

            // メモリ内キャッシュを更新
            lock (_syncLock)
            {
                _profiles[profile.Name] = profile;
            }

            _logger?.LogInformation("プロファイル '{ProfileName}' を保存しました", profile.Name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "プロファイル '{ProfileName}' の保存に失敗しました", profile.Name);
            throw;
        }
    }

    public Task DeleteProfileAsync(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            throw new ArgumentException("プロファイル名が指定されていません", nameof(profileName));

        var fileName = GetProfileFileName(profileName);
        var filePath = Path.Combine(_profilesDirectory, fileName);

        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            // メモリ内キャッシュからも削除
            lock (_syncLock)
            {
                _profiles.Remove(profileName);
            }

            _logger?.LogInformation("プロファイル '{ProfileName}' を削除しました", profileName);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "プロファイル '{ProfileName}' の削除に失敗しました", profileName);
            throw;
        }

        return Task.CompletedTask;
    }

    public async Task<SettingsGameCaptureProfile?> FindMatchingProfileAsync(string processName, string? windowTitle = null)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return null;

        await LoadProfilesAsync().ConfigureAwait(false);

        lock (_syncLock)
        {
            // 完全一致を優先
            var exactMatch = _profiles.Values.FirstOrDefault(p =>
                p.IsEnabled &&
                string.Equals(p.ExecutableName, processName, StringComparison.OrdinalIgnoreCase));

            if (exactMatch != null)
            {
                _logger?.LogDebug("プロセス名による完全一致でプロファイル '{ProfileName}' を検出", exactMatch.Name);
                return exactMatch;
            }

            // ウィンドウタイトルによる部分一致
            if (!string.IsNullOrWhiteSpace(windowTitle))
            {
                var titleMatch = _profiles.Values.FirstOrDefault(p =>
                    p.IsEnabled &&
                    !string.IsNullOrWhiteSpace(p.WindowTitlePattern) &&
                    p.MatchesWindowTitle(windowTitle));

                if (titleMatch != null)
                {
                    _logger?.LogDebug("ウィンドウタイトルによる部分一致でプロファイル '{ProfileName}' を検出", titleMatch.Name);
                    return titleMatch;
                }
            }

            // プロセス名による部分一致
            var partialMatch = _profiles.Values.FirstOrDefault(p =>
                p.IsEnabled &&
                processName.Contains(p.ExecutableName, StringComparison.OrdinalIgnoreCase) ||
                p.ExecutableName.Contains(processName, StringComparison.OrdinalIgnoreCase));

            if (partialMatch != null)
            {
                _logger?.LogDebug("プロセス名による部分一致でプロファイル '{ProfileName}' を検出", partialMatch.Name);
                return partialMatch;
            }
        }

        _logger?.LogTrace("プロセス '{ProcessName}' に適合するプロファイルが見つかりませんでした", processName);
        return null;
    }

    public SettingsGameCaptureProfile GetDefaultProfile()
    {
        return new SettingsGameCaptureProfile
        {
            Name = "Default",
            Description = "デフォルトのキャプチャ設定",
            ExecutableName = "",
            WindowTitlePattern = "",
            IsEnabled = true,
            CaptureSettings = new CaptureSettings
            {
                IsEnabled = true,
                CaptureIntervalMs = 500,
                CaptureQuality = 85,
                AutoDetectCaptureArea = true,
                DifferenceDetectionSensitivity = 30,
                DifferenceDetectionGridSize = 16,
                FullscreenOptimization = true,
                AutoOptimizeForGames = true
            },
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
    }

    private async Task LoadProfilesAsync()
    {
        try
        {
            if (!Directory.Exists(_profilesDirectory))
            {
                _logger?.LogDebug("プロファイルディレクトリが存在しません: {Directory}", _profilesDirectory);
                return;
            }

            var profileFiles = Directory.GetFiles(_profilesDirectory, "*.json");

            var loadedProfiles = new Dictionary<string, SettingsGameCaptureProfile>();

            foreach (var filePath in profileFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                    var profile = JsonSerializer.Deserialize<SettingsGameCaptureProfile>(json, ReadOptions);

                    if (profile != null && !string.IsNullOrWhiteSpace(profile.Name))
                    {
                        loadedProfiles[profile.Name] = profile;
                        _logger?.LogTrace("プロファイル '{ProfileName}' を読み込みました", profile.Name);
                    }
                }
                catch (JsonException ex)
                {
                    _logger?.LogWarning(ex, "プロファイルファイル '{FilePath}' のJSON解析に失敗しました", filePath);
                }
                catch (FileNotFoundException ex)
                {
                    _logger?.LogWarning(ex, "プロファイルファイル '{FilePath}' が見つかりません", filePath);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger?.LogWarning(ex, "プロファイルファイル '{FilePath}' へのアクセスが拒否されました", filePath);
                }
            }

            lock (_syncLock)
            {
                _profiles.Clear();
                foreach (var kvp in loadedProfiles)
                {
                    _profiles[kvp.Key] = kvp.Value;
                }
            }

            _logger?.LogDebug("{Count} 個のプロファイルを読み込みました", loadedProfiles.Count);
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger?.LogError(ex, "プロファイルディレクトリが見つかりません: {Directory}", _profilesDirectory);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogError(ex, "プロファイルディレクトリへのアクセスが拒否されました: {Directory}", _profilesDirectory);
        }
    }

    private static string GetProfileFileName(string profileName)
    {
        // ファイル名に使用できない文字を除去
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitizedName = string.Join("_", profileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

        return $"{sanitizedName}.json";
    }
}
