using System.Text.Json;

namespace Baketa.Infrastructure.Tests.Helpers;

/// <summary>
/// テスト用の共通JSON設定
/// </summary>
public static class JsonTestHelper
{
    /// <summary>
    /// テスト用に最適化されたJsonSerializerOptions（CA1869警告対応）
    /// </summary>
    public static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
