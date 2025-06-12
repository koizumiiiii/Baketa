using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Settings.Migration;

/// <summary>
/// バージョン0→1マイグレーション：ホットキー設定削除対応
/// UX改善によりホットキー機能が完全に削除されたため、
/// 既存設定からホットキー関連項目を削除し、新しいMainUI設定を追加
/// </summary>
public sealed class V0ToV1Migration : ISettingsMigration
{
    private readonly ILogger<V0ToV1Migration>? _logger;

    /// <inheritdoc />
    public int FromVersion => 0;

    /// <inheritdoc />
    public int ToVersion => 1;

    /// <inheritdoc />
    public string Description => "ホットキー機能削除とMainUI設定追加（UX改善対応）";

    /// <summary>
    /// V0ToV1Migrationを初期化します
    /// </summary>
    /// <param name="logger">ロガー（任意）</param>
    public V0ToV1Migration(ILogger<V0ToV1Migration>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// マイグレーションが適用可能かどうかをチェックします（CanMigrateのエイリアス）
    /// </summary>
    /// <param name="currentSettings">現在の設定データ</param>
    /// <returns>適用可能な場合はtrue</returns>
    /// <exception cref="ArgumentNullException">currentSettingsがnullの場合</exception>
    public bool IsApplicable(Dictionary<string, object?> currentSettings)
    {
        ArgumentNullException.ThrowIfNull(currentSettings);
        return CanMigrate(currentSettings);
    }
    
    /// <inheritdoc />
    public bool CanMigrate(Dictionary<string, object?> currentSettings)
    {
        ArgumentNullException.ThrowIfNull(currentSettings);
        
        // スキーマバージョンが0または未設定の場合のみマイグレーション可能
        // "Version"と"SchemaVersion"両方をサポート（"SchemaVersion"を優先）
        var versionKeys = new[] { "SchemaVersion", "Version" };
        
        foreach (var key in versionKeys)
        {
            if (currentSettings.TryGetValue(key, out var versionObj))
            {
                if (versionObj is int version)
                {
                    // バージョン0のみマイグレーション可能（厳密なチェック）
                    return version == FromVersion;
                }
                
                // nullの場合は初回設定とみなしてマイグレーション可能
                if (versionObj is null)
                {
                    return true;
                }
                
                // 他の型（stringなど）の場合はマイグレーション不可能
                return false;
            }
        }
        
        // どちらのバージョンキーも存在しない場合は初回マイグレーション
        return true;
    }

    /// <inheritdoc />
    public async Task<MigrationResult> MigrateAsync(Dictionary<string, object?> currentSettings)
    {
        ArgumentNullException.ThrowIfNull(currentSettings);
        
        _logger?.LogInformation("V0→V1マイグレーションを開始: ホットキー削除とMainUI設定追加");
        
        var startTime = DateTime.Now;
        var migratedSettings = new Dictionary<string, object?>(currentSettings);
        var changes = new List<MigrationSettingChange>();
        var deletedSettings = new List<string>();
        var addedSettings = new List<string>();
        var warnings = new List<string>();

        try
        {
            // 1. ホットキー関連設定の削除
            await RemoveHotkeySettingsAsync(migratedSettings, changes, deletedSettings, warnings).ConfigureAwait(false);
            
            // 2. MainUI設定の追加
            await AddMainUiSettingsAsync(migratedSettings, changes, addedSettings).ConfigureAwait(false);
            
            // 3. スキーマバージョンの更新（必ずVersionキーを設定）
            UpdateSchemaVersion(migratedSettings, changes);
            
            // 4. 最終更新日時の設定
            UpdateTimestamps(migratedSettings, changes);
            
            var executionTime = (long)(DateTime.Now - startTime).TotalMilliseconds;
            
            _logger?.LogInformation(
                "V0→V1マイグレーション完了: {ChangedCount}変更, {DeletedCount}削除, {AddedCount}追加, {ExecutionTime}ms", 
                changes.Count, deletedSettings.Count, addedSettings.Count, executionTime);
            
            return MigrationResult.CreateSuccess(
                migratedSettings, 
                changes, 
                deletedSettings, 
                addedSettings, 
                warnings, 
                executionTime);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            var executionTime = (long)(DateTime.Now - startTime).TotalMilliseconds;
            _logger?.LogError(ex, "V0→V1マイグレーション中にエラーが発生しました");
            return MigrationResult.CreateFailure($"マイグレーションエラー: {ex.Message}", executionTime);
        }
    }

    /// <inheritdoc />
    public async Task<MigrationResult> DryRunAsync(Dictionary<string, object?> currentSettings)
    {
        ArgumentNullException.ThrowIfNull(currentSettings);
        
        _logger?.LogInformation("V0→V1マイグレーションのドライランを実行");
        
        // 実際のマイグレーションと同じロジックだが、設定は変更しない
        var testSettings = new Dictionary<string, object?>(currentSettings);
        return await MigrateAsync(testSettings).ConfigureAwait(false);
    }

    /// <summary>
    /// ホットキー関連設定を削除します
    /// </summary>
    private async Task RemoveHotkeySettingsAsync(
        Dictionary<string, object?> settings, 
        List<MigrationSettingChange> changes, 
        List<string> deletedSettings, 
        List<string> warnings)
    {
        var hotkeyKeys = new[]
        {
            "Hotkey",
            "HotkeySettings",
            "Hotkey.Enabled",
            "Hotkey.ModifierKeys", 
            "Hotkey.Key",
            "GlobalHotkey",
            "TranslationHotkey",
            "CaptureHotkey",
            "ToggleHotkey",
            "SingleShotHotkey",
            "AutoModeHotkey",
            "ManualModeHotkey",
            "OverlayToggleHotkey",
            "SettingsHotkey"
        };

        var removedKeys = new List<string>();
        
        foreach (var key in hotkeyKeys)
        {
            if (settings.TryGetValue(key, out var oldValue))
            {
                settings.Remove(key);
                changes.Add(new MigrationSettingChange(key, oldValue, null, "ホットキー機能削除によるUX改善"));
                deletedSettings.Add(key);
                removedKeys.Add(key);
            }
        }

        // ネストされたオブジェクト内のホットキー設定も削除
        await RemoveNestedHotkeySettingsAsync(settings, changes, deletedSettings, warnings).ConfigureAwait(false);

        if (removedKeys.Count > 0)
        {
            _logger?.LogInformation("ホットキー設定を削除しました: {Keys}", string.Join(", ", removedKeys));
            warnings.Add($"ホットキー設定（{removedKeys.Count}項目）がUX改善により削除されました。ゲーム操作との衝突を避けるための変更です。");
        }
    }

    /// <summary>
    /// ネストされたオブジェクト内のホットキー設定を削除します
    /// </summary>
    private async Task RemoveNestedHotkeySettingsAsync(
        Dictionary<string, object?> settings, 
        List<MigrationSettingChange> changes, 
        List<string> deletedSettings, 
        List<string> warnings)
    {
        // AppSettings内のHotkey系プロパティを削除
        var keysToProcess = settings.Keys.ToList();
        
        foreach (var key in keysToProcess)
        {
            if (settings[key] is Dictionary<string, object?> nestedDict)
            {
                var nestedKeysToRemove = nestedDict.Keys
                    .Where(k => k.Contains("Hotkey", StringComparison.OrdinalIgnoreCase) || 
                               k.Contains("KeyBinding", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var nestedKey in nestedKeysToRemove)
                {
                    var fullKey = $"{key}.{nestedKey}";
                    var oldValue = nestedDict[nestedKey];
                    
                    nestedDict.Remove(nestedKey);
                    changes.Add(new MigrationSettingChange(fullKey, oldValue, null, "ネストされたホットキー設定の削除"));
                    deletedSettings.Add(fullKey);
                }
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// MainUI設定を追加します
    /// </summary>
    private async Task AddMainUiSettingsAsync(
        Dictionary<string, object?> settings, 
        List<MigrationSettingChange> changes, 
        List<string> addedSettings)
    {
        var mainUiKey = "MainUi";
        
        if (!settings.TryGetValue(mainUiKey, out _))
        {
            var mainUiSettings = new Dictionary<string, object?>
            {
                ["PanelPositionX"] = 50,
                ["PanelPositionY"] = 50,
                ["PanelOpacity"] = 0.8,
                ["AutoHideWhenIdle"] = true,
                ["AutoHideDelaySeconds"] = 10,
                ["HighlightOnHover"] = true,
                ["PanelSize"] = "Small",
                ["AlwaysOnTop"] = true,
                ["SingleShotDisplayTime"] = 10,
                ["EnableDragging"] = true,
                ["EnableBoundarySnap"] = true,
                ["BoundarySnapDistance"] = 20,
                ["EnableAnimations"] = true,
                ["AnimationDurationMs"] = 300,
                ["ThemeStyle"] = "Auto",
                ["ShowDebugInfo"] = false,
                ["ShowFrameRate"] = false
            };

            settings[mainUiKey] = mainUiSettings;
            changes.Add(new MigrationSettingChange(mainUiKey, null, mainUiSettings, "新しいMainUI設定の追加"));
            addedSettings.Add(mainUiKey);

            _logger?.LogInformation("MainUI設定を追加しました");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// スキーマバージョンを更新します（必ずVersionキーを設定）
    /// </summary>
    private void UpdateSchemaVersion(Dictionary<string, object?> settings, List<MigrationSettingChange> changes)
    {
        // 既存バージョンの確認
        var versionKeys = new[] { "SchemaVersion", "Version" };
        object? oldVersion = null;
        
        // 既存のバージョンキーを検索
        foreach (var key in versionKeys)
        {
            if (settings.TryGetValue(key, out var versionObj))
            {
                oldVersion = versionObj;
                break;
            }
        }
        
        // Versionキーを必ず設定（テストが期待しているため）
        var newVersion = ToVersion;
        settings["Version"] = newVersion;
        changes.Add(new MigrationSettingChange("Version", oldVersion, newVersion, "マイグレーションによるバージョン更新"));
        
        // SchemaVersionも設定（将来的な一貫性のため）
        if (!settings.ContainsKey("SchemaVersion"))
        {
            settings["SchemaVersion"] = newVersion;
            changes.Add(new MigrationSettingChange("SchemaVersion", null, newVersion, "スキーマバージョンの設定"));
        }
    }

    /// <summary>
    /// タイムスタンプを更新します
    /// </summary>
    private void UpdateTimestamps(Dictionary<string, object?> settings, List<MigrationSettingChange> changes)
    {
        var now = DateTime.Now;

        // 作成日時が未設定の場合のみ設定
        if (!settings.TryGetValue("CreatedAt", out _))
        {
            settings["CreatedAt"] = now;
            changes.Add(new MigrationSettingChange("CreatedAt", null, now, "初回マイグレーション時の作成日時設定"));
        }

        // 最終更新日時を更新
        var oldLastUpdated = settings.TryGetValue("LastUpdated", out var lastUpdatedObj) ? lastUpdatedObj : null;
        settings["LastUpdated"] = now;
        changes.Add(new MigrationSettingChange("LastUpdated", oldLastUpdated, now, "マイグレーション実行による更新"));
    }
}
