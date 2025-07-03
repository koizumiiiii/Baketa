using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Settings.Migration;

/// <summary>
/// 設定マイグレーション管理サービス実装
/// マイグレーションの発見、順序付け、実行を管理
/// </summary>
public sealed class SettingsMigrationManager : ISettingsMigrationManager
{
    private readonly ILogger<SettingsMigrationManager> _logger;
    private readonly Dictionary<int, List<ISettingsMigration>> _migrations = [];
    private readonly object _lockObject = new();

    /// <inheritdoc />
    public int LatestSchemaVersion { get; private set; } = 1;

    /// <inheritdoc />
    public event EventHandler<MigrationProgressEventArgs>? MigrationProgress;

    /// <summary>
    /// SettingsMigrationManagerを初期化します
    /// </summary>
    /// <param name="logger">ロガー</param>
    public SettingsMigrationManager(ILogger<SettingsMigrationManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // デフォルトマイグレーションを登録
        RegisterDefaultMigrations();
    }

    /// <inheritdoc />
    public void RegisterMigration(ISettingsMigration migration)
    {
        ArgumentNullException.ThrowIfNull(migration);
        
        lock (_lockObject)
        {
            if (!_migrations.TryGetValue(migration.FromVersion, out var migrationList))
            {
                migrationList = [];
                _migrations[migration.FromVersion] = migrationList;
            }
            
            migrationList.Add(migration);
            
            // 最新バージョンを更新
            if (migration.ToVersion > LatestSchemaVersion)
            {
                LatestSchemaVersion = migration.ToVersion;
            }
            
            _logger.LogDebug("マイグレーションを登録しました: {FromVersion}→{ToVersion} ({Description})", 
                migration.FromVersion, migration.ToVersion, migration.Description);
        }
    }

    /// <inheritdoc />
    public bool RequiresMigration(int currentVersion)
    {
        return currentVersion < LatestSchemaVersion;
    }

    /// <inheritdoc />
    public IReadOnlyList<ISettingsMigration> GetMigrationPath(int fromVersion, int? toVersion = null)
    {
        var targetVersion = toVersion ?? LatestSchemaVersion;
        var path = new List<ISettingsMigration>();
        
        lock (_lockObject)
        {
            var currentVersion = fromVersion;
            
            while (currentVersion < targetVersion)
            {
                if (!_migrations.TryGetValue(currentVersion, out var availableMigrations))
                {
                    _logger.LogWarning("バージョン {Version} からのマイグレーションが見つかりません", currentVersion);
                    break;
                }
                
                // 最適なマイグレーションを選択（最も進んだバージョンに移行するもの）
                var bestMigration = availableMigrations
                    .Where(m => m.ToVersion <= targetVersion)
                    .OrderByDescending(m => m.ToVersion)
                    .FirstOrDefault();
                
                if (bestMigration == null)
                {
                    _logger.LogWarning("バージョン {Version} から {TargetVersion} への適切なマイグレーションが見つかりません", 
                        currentVersion, targetVersion);
                    break;
                }
                
                path.Add(bestMigration);
                currentVersion = bestMigration.ToVersion;
            }
        }
        
        _logger.LogDebug("マイグレーションパスを生成しました: {FromVersion}→{ToVersion}, {StepCount}ステップ", 
            fromVersion, targetVersion, path.Count);
        
        return path;
    }

    /// <inheritdoc />
    public async Task<MigrationPlanResult> DryRunMigrationAsync(
        Dictionary<string, object?> currentSettings, 
        int fromVersion, 
        int? toVersion = null)
    {
        _logger.LogInformation("マイグレーションのドライランを開始: {FromVersion}→{ToVersion}", fromVersion, toVersion);
        
        var stopwatch = Stopwatch.StartNew();
        var path = GetMigrationPath(fromVersion, toVersion);
        var stepResults = new List<MigrationStepResult>();
        var warnings = new List<string>();
        var workingSettings = new Dictionary<string, object?>(currentSettings);
        
        try
        {
            for (int i = 0; i < path.Count; i++)
            {
                var migration = path[i];
                
                OnMigrationProgress(new MigrationProgressEventArgs(
                    i, path.Count, migration, $"ドライラン実行中: {migration.Description}"));
                
                if (!migration.CanMigrate(workingSettings))
                {
                    const string errorTemplate = "マイグレーション {FromVersion}→{ToVersion} を実行できません";
                    var errorMsg = $"マイグレーション {migration.FromVersion}→{migration.ToVersion} を実行できません";
                    _logger.LogError(errorTemplate, migration.FromVersion, migration.ToVersion);
                    return new MigrationPlanResult(
                        false, workingSettings, fromVersion, toVersion ?? LatestSchemaVersion, 
                        stepResults, errorMsg, warnings, stopwatch.ElapsedMilliseconds);
                }
                
                var result = await migration.DryRunAsync(workingSettings).ConfigureAwait(false);
                stepResults.Add(new MigrationStepResult(migration, result, i));
                
                if (!result.IsSuccess)
                {
                    _logger.LogError("ドライラン失敗: {Migration}, {Error}", migration.Description, result.ErrorMessage);
                    return new MigrationPlanResult(
                        false, workingSettings, fromVersion, toVersion ?? LatestSchemaVersion, 
                        stepResults, result.ErrorMessage, warnings, stopwatch.ElapsedMilliseconds);
                }
                
                workingSettings = result.MigratedSettings;
                warnings.AddRange(result.Warnings);
            }
            
            OnMigrationProgress(new MigrationProgressEventArgs(
                path.Count, path.Count, null, "ドライラン完了"));
            
            _logger.LogInformation("マイグレーションドライラン完了: {StepCount}ステップ, {ElapsedMs}ms", 
                path.Count, stopwatch.ElapsedMilliseconds);
            
            return new MigrationPlanResult(
                true, workingSettings, fromVersion, toVersion ?? LatestSchemaVersion, 
                stepResults, null, warnings, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            _logger.LogError(ex, "マイグレーションドライラン中にエラーが発生しました");
            return new MigrationPlanResult(
                false, workingSettings, fromVersion, toVersion ?? LatestSchemaVersion, 
                stepResults, $"ドライランエラー: {ex.Message}", warnings, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <inheritdoc />
    public async Task<MigrationPlanResult> ExecuteMigrationAsync(
        Dictionary<string, object?> currentSettings, 
        int fromVersion, 
        int? toVersion = null)
    {
        _logger.LogInformation("マイグレーションを開始: {FromVersion}→{ToVersion}", fromVersion, toVersion);
        
        var stopwatch = Stopwatch.StartNew();
        var path = GetMigrationPath(fromVersion, toVersion);
        var stepResults = new List<MigrationStepResult>();
        var warnings = new List<string>();
        var workingSettings = new Dictionary<string, object?>(currentSettings);
        
        try
        {
            for (int i = 0; i < path.Count; i++)
            {
                var migration = path[i];
                
                OnMigrationProgress(new MigrationProgressEventArgs(
                    i, path.Count, migration, $"実行中: {migration.Description}"));
                
                if (!migration.CanMigrate(workingSettings))
                {
                    const string errorTemplate = "マイグレーション {FromVersion}→{ToVersion} を実行できません";
                    var errorMsg = $"マイグレーション {migration.FromVersion}→{migration.ToVersion} を実行できません";
                    _logger.LogError(errorTemplate, migration.FromVersion, migration.ToVersion);
                    return new MigrationPlanResult(
                        false, workingSettings, fromVersion, toVersion ?? LatestSchemaVersion, 
                        stepResults, errorMsg, warnings, stopwatch.ElapsedMilliseconds);
                }
                
                var result = await migration.MigrateAsync(workingSettings).ConfigureAwait(false);
                stepResults.Add(new MigrationStepResult(migration, result, i));
                
                if (!result.IsSuccess)
                {
                    _logger.LogError("マイグレーション失敗: {Migration}, {Error}", migration.Description, result.ErrorMessage);
                    return new MigrationPlanResult(
                        false, workingSettings, fromVersion, toVersion ?? LatestSchemaVersion, 
                        stepResults, result.ErrorMessage, warnings, stopwatch.ElapsedMilliseconds);
                }
                
                workingSettings = result.MigratedSettings;
                warnings.AddRange(result.Warnings);
                
                _logger.LogInformation("マイグレーション完了: {Migration} ({ElapsedMs}ms)", 
                    migration.Description, result.ExecutionTimeMs);
            }
            
            OnMigrationProgress(new MigrationProgressEventArgs(
                path.Count, path.Count, null, "マイグレーション完了"));
            
            _logger.LogInformation("全マイグレーション完了: {StepCount}ステップ, {ElapsedMs}ms", 
                path.Count, stopwatch.ElapsedMilliseconds);
            
            return new MigrationPlanResult(
                true, workingSettings, fromVersion, toVersion ?? LatestSchemaVersion, 
                stepResults, null, warnings, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            _logger.LogError(ex, "マイグレーション中にエラーが発生しました");
            return new MigrationPlanResult(
                false, workingSettings, fromVersion, toVersion ?? LatestSchemaVersion, 
                stepResults, $"マイグレーションエラー: {ex.Message}", warnings, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<MigrationInfo> GetAvailableMigrations()
    {
        var migrations = new List<MigrationInfo>();
        
        lock (_lockObject)
        {
            foreach (var kvp in _migrations)
            {
                foreach (var migration in kvp.Value)
                {
                    migrations.Add(new MigrationInfo(
                        migration.FromVersion,
                        migration.ToVersion,
                        migration.Description,
                        migration.GetType().Name
                    ));
                }
            }
        }
        
        return [.. migrations.OrderBy(m => m.FromVersion).ThenBy(m => m.ToVersion)];
    }

    /// <summary>
    /// デフォルトマイグレーションを登録します
    /// </summary>
    private void RegisterDefaultMigrations()
    {
        // V0→V1: ホットキー削除マイグレーション
        RegisterMigration(new V0ToV1Migration());
        
        _logger.LogDebug("デフォルトマイグレーションを登録しました");
    }

    /// <summary>
    /// マイグレーション進捗イベントを発行します
    /// </summary>
    /// <param name="args">イベント引数</param>
    private void OnMigrationProgress(MigrationProgressEventArgs args)
    {
        try
        {
            MigrationProgress?.Invoke(this, args);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            _logger.LogWarning(ex, "マイグレーション進捗イベントの処理中にエラーが発生しました");
        }
    }
}
