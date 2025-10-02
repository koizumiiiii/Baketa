using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.Configuration;
using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace Baketa.Core.DI;

/// <summary>
/// 設定システム統合型サービスモジュール基底クラス
/// 既存のServiceModuleBaseを完全置換
/// 完全自律型: 設定の自動読み込み・検証・登録
/// </summary>
public abstract class ConfigurableServiceModuleBase : ServiceModuleBase
{
    protected Configuration.IConfigurationManager ConfigurationManager { get; private set; } = null!;
    
    /// <summary>
    /// サービス登録（設定システム自動初期化）
    /// </summary>
    public override void RegisterServices(IServiceCollection services)
    {
        Console.WriteLine($"🔧 [PHASE12.2_DIAG] {GetType().Name}.RegisterServices() 開始");

        try
        {
            // 設定管理システムの初期化
            Console.WriteLine($"🔧 [PHASE12.2_DIAG] {GetType().Name} - InitializeConfigurationSystem() 実行直前");
            InitializeConfigurationSystem(services);
            Console.WriteLine($"✅ [PHASE12.2_DIAG] {GetType().Name} - InitializeConfigurationSystem() 完了");

            // サブクラスのサービス登録
            Console.WriteLine($"🔧 [PHASE12.2_DIAG] {GetType().Name} - RegisterConfigurableServices() 実行直前");
            RegisterConfigurableServices(services);
            Console.WriteLine($"✅ [PHASE12.2_DIAG] {GetType().Name} - RegisterConfigurableServices() 完了");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [PHASE12.2_DIAG] {GetType().Name}.RegisterServices() 失敗: {ex.GetType().Name}");
            Console.WriteLine($"❌ [PHASE12.2_DIAG] Message: {ex.Message}");
            Console.WriteLine($"❌ [PHASE12.2_DIAG] StackTrace: {ex.StackTrace}");
            throw;
        }

        Console.WriteLine($"✅ [PHASE12.2_DIAG] {GetType().Name}.RegisterServices() 完全完了");
    }
    
    /// <summary>
    /// サブクラスで実装すべき設定可能サービス登録
    /// </summary>
    protected abstract void RegisterConfigurableServices(IServiceCollection services);
    
    /// <summary>
    /// 設定システムの初期化（Gemini指摘反映: BuildServiceProvider回避）
    /// パフォーマンス改善: IConfigurationをコンストラクタから受け取るよう変更予定
    /// 現在は一時的にRegistration時点での参照で対応
    /// </summary>
    private void InitializeConfigurationSystem(IServiceCollection services)
    {
        // Gemini指摘: BuildServiceProviderアンチパターンの一時的対応
        // 将来的にはコンストラクタでIConfigurationを受け取る設計に変更
        
        // 既存の登録からIConfigurationを探す
        var configurationDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IConfiguration));
        if (configurationDescriptor == null)
        {
            throw new InvalidOperationException("IConfigurationが登録されていません。Program.csで事前にConfiguration登録が必要です。");
        }

        // ServiceDescriptorからIConfigurationインスタンスを取得
        // 注意: これは完全な解決策ではないが、BuildServiceProvider回避の一時対応
        if (configurationDescriptor.ImplementationInstance is IConfiguration existingConfiguration)
        {
            var configManager = new CoreConfigurationManager(existingConfiguration);
            services.AddSingleton<Configuration.IConfigurationManager>(configManager);
            ConfigurationManager = configManager;
        }
        else
        {
            // ファクトリーまたは型ベースの登録の場合の対応（非推奨パターンだが必要）
            var serviceProvider = services.BuildServiceProvider();
            var fallbackConfiguration = serviceProvider.GetService<IConfiguration>();
            
            if (fallbackConfiguration == null)
            {
                throw new InvalidOperationException("IConfigurationインスタンスの取得に失敗しました。");
            }
            
            var configManager = new CoreConfigurationManager(fallbackConfiguration);
            services.AddSingleton<Configuration.IConfigurationManager>(configManager);
            ConfigurationManager = configManager;
            serviceProvider.Dispose(); // リソースリーク防止
        }
        
        Console.WriteLine($"✅ [MODULE] {GetType().Name} - 設定システム初期化完了");
    }
    
    // DetectConfigurationBasePathは使用されないため削除
    
    /// <summary>
    /// 型安全な設定登録ヘルパー
    /// </summary>
    protected void RegisterSettings<T>(IServiceCollection services, string? sectionName = null) 
        where T : class, new()
    {
        var section = sectionName ?? typeof(T).Name.Replace("Settings", "");
        
        Console.WriteLine($"🔧 [MODULE] {GetType().Name} - {typeof(T).Name} 設定登録開始 (セクション: {section})");
        
        if (!ConfigurationManager.SectionExists(section))
        {
            Console.WriteLine($"⚠️ [MODULE] {GetType().Name} - セクション '{section}' が見つかりません - デフォルト値使用");
        }
        
        var settings = ConfigurationManager.GetSettings<T>(section);
        
        // IOptionsMonitorのみ登録（IOptions削除でDI曖昧性解決）
        services.Configure<T>(options =>
        {
            // 設定値を直接コピー
            var properties = typeof(T).GetProperties();
            foreach (var prop in properties)
            {
                if (prop.CanWrite)
                {
                    prop.SetValue(options, prop.GetValue(settings));
                }
            }
        });
        
        // 直接インスタンスも登録
        services.AddSingleton(settings);
        
        Console.WriteLine($"✅ [MODULE] {GetType().Name} - {typeof(T).Name} 設定登録完了");
        Console.WriteLine($"🔧 [MODULE] {GetType().Name} - 設定値: {System.Text.Json.JsonSerializer.Serialize(settings)}");
    }
    
    /// <summary>
    /// 設定存在チェック
    /// </summary>
    protected bool HasSection(string sectionName)
    {
        return ConfigurationManager.SectionExists(sectionName);
    }
    
    /// <summary>
    /// 設定値直接取得
    /// </summary>
    protected string? GetConfigValue(string key)
    {
        return ConfigurationManager.GetValue(key);
    }
    
    /// <summary>
    /// デバッグ情報出力
    /// </summary>
    protected void LogConfigurationDebug()
    {
        var debugInfo = ConfigurationManager.GetDebugInfo();
        Console.WriteLine($"🔧 [MODULE] {GetType().Name} - 利用可能セクション: {string.Join(", ", debugInfo.AvailableSections)}");
    }
}