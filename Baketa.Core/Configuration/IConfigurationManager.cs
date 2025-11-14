using Microsoft.Extensions.Configuration;

namespace Baketa.Core.Configuration;

/// <summary>
/// 設定管理の統一インターフェース
/// Clean Architecture準拠: Core層での抽象化定義
/// </summary>
public interface IConfigurationManager
{
    /// <summary>
    /// 型安全な設定取得
    /// </summary>
    T GetSettings<T>() where T : class, new();

    /// <summary>
    /// 型安全な設定取得（セクション名指定）
    /// </summary>
    T GetSettings<T>(string sectionName) where T : class, new();

    /// <summary>
    /// 設定の存在確認
    /// </summary>
    bool SectionExists(string sectionName);

    /// <summary>
    /// 設定値の直接取得
    /// </summary>
    string? GetValue(string key);

    /// <summary>
    /// 設定読み込み状況のデバッグ情報取得
    /// </summary>
    ConfigurationDebugInfo GetDebugInfo();
}

/// <summary>
/// 設定デバッグ情報
/// </summary>
public record ConfigurationDebugInfo(
    string[] LoadedFiles,
    string[] AvailableSections,
    Dictionary<string, string> AllKeyValues,
    string BasePath
);
