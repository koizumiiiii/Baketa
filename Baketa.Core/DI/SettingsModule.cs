using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.Settings;
using Baketa.Core.Settings.Migration;
using Baketa.Core.Services;

namespace Baketa.Core.DI;

/// <summary>
/// 設定システムのDIモジュール
/// 設定管理、メタデータサービス、マイグレーション機能を統合
/// </summary>
public static class SettingsModule
{
    /// <summary>
    /// 設定システムのサービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>サービスコレクション（メソッドチェーン用）</returns>
    public static IServiceCollection AddSettingsSystem(this IServiceCollection services)
    {
        // 設定メタデータサービス
        services.AddSingleton<ISettingMetadataService, SettingMetadataService>();
        
        // マイグレーション管理サービス
        services.AddSingleton<ISettingsMigrationManager, SettingsMigrationManager>();
        
        // メイン設定サービス（Coreレイヤーの実装を使用）
        services.AddSingleton<ISettingsService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<EnhancedSettingsService>>();
            var metadataService = provider.GetRequiredService<ISettingMetadataService>();
            var migrationManager = provider.GetRequiredService<ISettingsMigrationManager>();
            
            return new EnhancedSettingsService(logger, metadataService, migrationManager);
        });
        
        return services;
    }
    
    /// <summary>
    /// 設定システムのサービスを高度なオプション付きで登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="configAction">設定アクション</param>
    /// <returns>サービスコレクション（メソッドチェーン用）</returns>
    public static IServiceCollection AddSettingsSystem(
        this IServiceCollection services, 
        Action<SettingsOptions> configAction)
    {
        ArgumentNullException.ThrowIfNull(configAction);
        
        // 設定オプションを構成
        var options = new SettingsOptions();
        configAction(options);
        services.AddSingleton(options);
        
        // 基本的な設定システムを追加
        services.AddSettingsSystem();
        
        // カスタムマイグレーションを登録
        if (options.CustomMigrations.Count > 0)
        {
            services.AddSingleton<ISettingsMigrationRegistrar>(provider =>
            {
                var migrationManager = provider.GetRequiredService<ISettingsMigrationManager>();
                return new SettingsMigrationRegistrar(migrationManager, options.CustomMigrations);
            });
        }
        
        return services;
    }
    
    /// <summary>
    /// 設定関連のイベントハンドラーを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>サービスコレクション（メソッドチェーン用）</returns>
    public static IServiceCollection AddSettingsEventHandlers(this IServiceCollection services)
    {
        // 設定変更イベントハンドラー
        services.AddTransient<ISettingsEventHandler, SettingsEventHandler>();
        
        // ゲームプロファイル変更イベントハンドラー
        services.AddTransient<IGameProfileEventHandler, GameProfileEventHandler>();
        
        return services;
    }
}

/// <summary>
/// 設定システムのオプション
/// </summary>
public sealed class SettingsOptions
{
    /// <summary>
    /// 設定ファイルの保存パス（nullでデフォルト）
    /// </summary>
    public string? SettingsFilePath { get; set; }
    
    /// <summary>
    /// 自動バックアップの有効化
    /// </summary>
    public bool EnableAutoBackup { get; set; } = true;
    
    /// <summary>
    /// バックアップ間隔（時間）
    /// </summary>
    public int BackupIntervalHours { get; set; } = 24;
    
    /// <summary>
    /// 最大バックアップ数
    /// </summary>
    public int MaxBackupCount { get; set; } = 10;
    
    /// <summary>
    /// 設定変更履歴の最大保持数
    /// </summary>
    public int MaxChangeHistoryCount { get; set; } = 100;
    
    /// <summary>
    /// 自動マイグレーションの有効化
    /// </summary>
    public bool EnableAutoMigration { get; set; } = true;
    
    /// <summary>
    /// マイグレーション前のバックアップ作成
    /// </summary>
    public bool CreateBackupBeforeMigration { get; set; } = true;
    
    /// <summary>
    /// 詳細ログの有効化
    /// </summary>
    public bool EnableVerboseLogging { get; set; }
    
    /// <summary>
    /// カスタムマイグレーションのリスト
    /// </summary>
    public IList<Type> CustomMigrations { get; set; } = [];
    
    /// <summary>
    /// 設定検証の有効化
    /// </summary>
    public bool EnableSettingsValidation { get; set; } = true;
    
    /// <summary>
    /// 設定変更の監視有効化
    /// </summary>
    public bool EnableSettingsWatching { get; set; } = true;
}

/// <summary>
/// カスタムマイグレーション登録サービス
/// </summary>
public interface ISettingsMigrationRegistrar
{
    /// <summary>
    /// 登録されたマイグレーション数
    /// </summary>
    int RegisteredMigrationCount { get; }
    
    /// <summary>
    /// マイグレーションの登録を実行します
    /// </summary>
    void RegisterMigrations();
}

/// <summary>
/// カスタムマイグレーション登録サービス実装
/// </summary>
internal sealed class SettingsMigrationRegistrar : ISettingsMigrationRegistrar
{
    private readonly ISettingsMigrationManager _migrationManager;
    private readonly List<Type> _customMigrationTypes;
    private readonly ILogger<SettingsMigrationRegistrar>? _logger;

    public int RegisteredMigrationCount { get; private set; }

    public SettingsMigrationRegistrar(
        ISettingsMigrationManager migrationManager, 
        IList<Type> customMigrationTypes,
        ILogger<SettingsMigrationRegistrar>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(migrationManager);
        ArgumentNullException.ThrowIfNull(customMigrationTypes);
        
        _migrationManager = migrationManager;
        _customMigrationTypes = [.. customMigrationTypes];
        _logger = logger;
    }

    public void RegisterMigrations()
    {
        foreach (var migrationType in _customMigrationTypes)
        {
            try
            {
                if (!typeof(ISettingsMigration).IsAssignableFrom(migrationType))
                {
                    _logger?.LogWarning("型 {Type} はISettingsMigrationを実装していません", migrationType.Name);
                    continue;
                }

                if (Activator.CreateInstance(migrationType) is ISettingsMigration migration)
                {
                    _migrationManager.RegisterMigration(migration);
                    RegisteredMigrationCount++;
                    _logger?.LogDebug("カスタムマイグレーションを登録しました: {Type}", migrationType.Name);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _logger?.LogError(ex, "カスタムマイグレーション {Type} の登録に失敗しました", migrationType.Name);
            }
        }

        _logger?.LogInformation("カスタムマイグレーションの登録が完了しました: {Count}件", RegisteredMigrationCount);
    }
}

/// <summary>
/// 設定イベントハンドラーインターフェース
/// </summary>
public interface ISettingsEventHandler
{
    /// <summary>
    /// 設定変更を処理します
    /// </summary>
    /// <param name="args">設定変更イベント引数</param>
    Task HandleSettingChangedAsync(SettingChangedEventArgs args);
}

/// <summary>
/// ゲームプロファイルイベントハンドラーインターフェース
/// </summary>
public interface IGameProfileEventHandler
{
    /// <summary>
    /// ゲームプロファイル変更を処理します
    /// </summary>
    /// <param name="args">ゲームプロファイル変更イベント引数</param>
    Task HandleGameProfileChangedAsync(GameProfileChangedEventArgs args);
}

/// <summary>
/// デフォルト設定イベントハンドラー実装
/// </summary>
internal sealed class SettingsEventHandler : ISettingsEventHandler
{
    private readonly ILogger<SettingsEventHandler> _logger;

    public SettingsEventHandler(ILogger<SettingsEventHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task HandleSettingChangedAsync(SettingChangedEventArgs args)
    {
        _logger.LogDebug("設定変更を処理中: {Key} = {NewValue}", args.SettingKey, args.NewValue);
        
        // ここで設定変更に応じた処理を実装
        // 例：特定の設定変更時の通知、キャッシュクリア、再計算など
        
        await Task.CompletedTask.ConfigureAwait(false);
    }
}

/// <summary>
/// デフォルトゲームプロファイルイベントハンドラー実装
/// </summary>
internal sealed class GameProfileEventHandler : IGameProfileEventHandler
{
    private readonly ILogger<GameProfileEventHandler> _logger;

    public GameProfileEventHandler(ILogger<GameProfileEventHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task HandleGameProfileChangedAsync(GameProfileChangedEventArgs args)
    {
        _logger.LogDebug("ゲームプロファイル変更を処理中: {ProfileId} ({ChangeType})", 
            args.ProfileId, args.ChangeType);
        
        // ここでゲームプロファイル変更に応じた処理を実装
        // 例：アクティブプロファイル変更時の設定適用、統計更新など
        
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
