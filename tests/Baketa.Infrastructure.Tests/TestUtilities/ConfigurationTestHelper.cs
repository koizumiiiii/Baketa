using Microsoft.Extensions.Configuration;

namespace Baketa.Infrastructure.Tests.TestUtilities;

/// <summary>
/// テスト用Configuration作成ユーティリティ
/// CS8620警告の根本解決：Nullable参照型対応
/// </summary>
public static class ConfigurationTestHelper
{
    /// <summary>
    /// Nullable参照型対応のテスト用Configuration作成
    /// Dictionary<string, string> → IEnumerable<KeyValuePair<string, string?>> 変換
    /// </summary>
    /// <param name="configData">設定データ</param>
    /// <returns>テスト用IConfiguration</returns>
    public static IConfiguration CreateTestConfiguration(Dictionary<string, string> configData)
    {
        // CS8620警告解決：明示的にnull許容型に変換
        var nullableConfigData = configData.Select(kvp =>
            new KeyValuePair<string, string?>(kvp.Key, kvp.Value));

        return new ConfigurationBuilder()
            .AddInMemoryCollection(nullableConfigData)
            .Build();
    }

    /// <summary>
    /// よく使用される翻訳設定のデフォルト値
    /// </summary>
    public static Dictionary<string, string> DefaultTranslationConfig => new()
    {
        ["Translation:DefaultEngine"] = "NLLB200",
        ["Translation:UseExternalServer"] = "true",
        ["Translation:MaxConnections"] = "3",
        ["Translation:MinConnections"] = "1",
        ["Translation:ConnectionTimeoutMs"] = "5000"
    };

    /// <summary>
    /// 接続プール設定のデフォルト値
    /// </summary>
    public static Dictionary<string, string> DefaultConnectionPoolConfig => new()
    {
        ["Translation:MaxConnections"] = "8",
        ["Translation:MinConnections"] = "1",
        ["Translation:OptimalChunksPerConnection"] = "4",
        ["Translation:ConnectionTimeoutMs"] = "5000"
    };
}
