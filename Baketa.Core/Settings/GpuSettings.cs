namespace Baketa.Core.Settings;

/// <summary>
/// GPU判定設定
/// appsettings.json の GpuSettings セクションから読み込まれる
/// </summary>
public class GpuSettings
{
    /// <summary>
    /// 専用GPUとして判定するキーワードのリスト
    /// </summary>
    public required List<string> DedicatedGpuKeywords { get; init; } = [];

    /// <summary>
    /// 統合GPUとして判定するキーワードのリスト
    /// </summary>
    public required List<string> IntegratedGpuKeywords { get; init; } = [];

    /// <summary>
    /// 未知のGPUを専用GPUとして扱うかどうか
    /// </summary>
    public bool FallbackToDedicated { get; init; } = true;

    /// <summary>
    /// 設定についての注釈（設定ファイル用）
    /// </summary>
    public string? Note { get; init; }
}
