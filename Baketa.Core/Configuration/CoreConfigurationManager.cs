using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Baketa.Core.Configuration;

/// <summary>
/// Core層設定管理実装（Clean Architecture準拠）
/// Infrastructure層への依存を排除した完全自律型
/// </summary>
public sealed class CoreConfigurationManager : IConfigurationManager
{
    private readonly IConfiguration _configuration;
    private readonly string _basePath;
    private readonly string[] _loadedFiles;

    /// <summary>
    /// コンストラクタ: IConfigurationを受け取る軽量実装
    /// </summary>
    public CoreConfigurationManager(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _basePath = System.IO.Directory.GetCurrentDirectory();
        _loadedFiles = ["appsettings.json"]; // 簡略化
        
        Console.WriteLine("🔧 [CORE_CONFIG] CoreConfigurationManager初期化完了");
    }

    public T GetSettings<T>() where T : class, new()
    {
        var sectionName = typeof(T).Name.Replace("Settings", "");
        return GetSettings<T>(sectionName);
    }

    public T GetSettings<T>(string sectionName) where T : class, new()
    {
        Console.WriteLine($"🔧 [CORE_CONFIG] GetSettings<{typeof(T).Name}>(\"{sectionName}\") 開始");
        
        var section = _configuration.GetSection(sectionName);
        
        if (!section.Exists())
        {
            Console.WriteLine($"⚠️ [CORE_CONFIG] セクション '{sectionName}' が見つかりません - デフォルト値使用");
            return new T();
        }
        
        var settings = new T();
        section.Bind(settings);
        
        Console.WriteLine($"✅ [CORE_CONFIG] {typeof(T).Name} 設定取得完了");
        
        return settings;
    }

    public bool SectionExists(string sectionName)
    {
        return _configuration.GetSection(sectionName).Exists();
    }

    public string? GetValue(string key)
    {
        return _configuration[key];
    }

    public ConfigurationDebugInfo GetDebugInfo()
    {
        var allSections = _configuration.GetChildren()
            .Select(x => x.Key)
            .ToArray();
            
        var allKeyValues = _configuration.AsEnumerable()
            .Where(kvp => kvp.Value != null)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!);
        
        return new ConfigurationDebugInfo(_loadedFiles, allSections, allKeyValues, _basePath);
    }
}