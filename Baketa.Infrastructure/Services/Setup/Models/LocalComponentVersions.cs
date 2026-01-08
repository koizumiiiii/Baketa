using System.Text.Json.Serialization;
using Baketa.Core.Abstractions.Services;

namespace Baketa.Infrastructure.Services.Setup.Models;

/// <summary>
/// Issue #256: ローカルコンポーネントバージョン管理
/// %LOCALAPPDATA%/Baketa/component-versions.json に保存
/// </summary>
public sealed class LocalComponentVersions
{
    /// <summary>
    /// インストール済みコンポーネント（キー: コンポーネントID）
    /// </summary>
    [JsonPropertyName("components")]
    public Dictionary<string, InstalledComponentInfo> Components { get; init; } = [];
}

/// <summary>
/// インストール済みコンポーネント情報
/// </summary>
public sealed class InstalledComponentInfo
{
    /// <summary>
    /// インストール済みバージョン
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// インストール済みバリアント（cpu/cuda）
    /// </summary>
    [JsonPropertyName("variant")]
    public string Variant { get; init; } = string.Empty;

    /// <summary>
    /// インストール日時（UTC）
    /// </summary>
    [JsonPropertyName("installedAt")]
    public DateTimeOffset InstalledAt { get; init; }

    /// <summary>
    /// インストールパス（オプション）
    /// </summary>
    [JsonPropertyName("installPath")]
    public string? InstallPath { get; init; }
}

/// <summary>
/// コンポーネント更新チェック結果
/// IComponentUpdateCheckResultを実装し、UI層がCore層の抽象に依存できるようにする
/// </summary>
public sealed class ComponentUpdateCheckResult : IComponentUpdateCheckResult
{
    /// <summary>
    /// コンポーネントID
    /// </summary>
    public required string ComponentId { get; init; }

    /// <summary>
    /// 表示名
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// 現在のバージョン（未インストールの場合はnull）
    /// </summary>
    public string? CurrentVersion { get; init; }

    /// <summary>
    /// 最新バージョン
    /// </summary>
    public required string LatestVersion { get; init; }

    /// <summary>
    /// 更新が利用可能か
    /// </summary>
    public required bool UpdateAvailable { get; init; }

    /// <summary>
    /// 変更履歴
    /// </summary>
    public string? Changelog { get; init; }

    /// <summary>
    /// ダウンロードサイズ合計（バイト）
    /// </summary>
    public long TotalDownloadSize { get; init; }

    /// <summary>
    /// 最低アプリバージョン要件
    /// </summary>
    public string? MinAppVersion { get; init; }

    /// <summary>
    /// アプリバージョンが要件を満たしているか
    /// </summary>
    public bool MeetsAppVersionRequirement { get; init; } = true;
}
