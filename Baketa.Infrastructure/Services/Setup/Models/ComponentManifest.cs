using System.Text.Json.Serialization;

namespace Baketa.Infrastructure.Services.Setup.Models;

/// <summary>
/// Issue #256: コンポーネントマニフェスト
/// models-v1リリースで配布されるコンポーネントのメタデータを管理
/// </summary>
public sealed class ComponentManifest
{
    /// <summary>
    /// マニフェストバージョン（スキーマバージョン）
    /// </summary>
    [JsonPropertyName("manifestVersion")]
    public string ManifestVersion { get; init; } = "1.1";

    /// <summary>
    /// 最終更新日時（UTC）
    /// </summary>
    [JsonPropertyName("lastUpdated")]
    public DateTimeOffset LastUpdated { get; init; }

    /// <summary>
    /// ダウンロードベースURL
    /// </summary>
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// コンポーネント一覧（キー: コンポーネントID）
    /// </summary>
    [JsonPropertyName("components")]
    public Dictionary<string, ComponentInfo> Components { get; init; } = [];
}

/// <summary>
/// コンポーネント情報
/// </summary>
public sealed class ComponentInfo
{
    /// <summary>
    /// 表示名
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// コンポーネントバージョン（SemVer形式）
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// 最低アプリバージョン要件
    /// </summary>
    [JsonPropertyName("minAppVersion")]
    public string? MinAppVersion { get; init; }

    /// <summary>
    /// 変更履歴（このバージョンでの変更点）
    /// </summary>
    [JsonPropertyName("changelog")]
    public string? Changelog { get; init; }

    /// <summary>
    /// バリアント一覧（キー: バリアント名、例: "cpu", "cuda"）
    /// </summary>
    [JsonPropertyName("variants")]
    public Dictionary<string, ComponentVariant> Variants { get; init; } = [];
}

/// <summary>
/// コンポーネントバリアント（CPU版/CUDA版など）
/// </summary>
public sealed class ComponentVariant
{
    /// <summary>
    /// ファイル一覧
    /// </summary>
    [JsonPropertyName("files")]
    public List<ComponentFile> Files { get; init; } = [];
}

/// <summary>
/// コンポーネントファイル情報
/// </summary>
public sealed class ComponentFile
{
    /// <summary>
    /// ファイル名
    /// </summary>
    [JsonPropertyName("filename")]
    public string Filename { get; init; } = string.Empty;

    /// <summary>
    /// SHA256ハッシュ値（検証用）
    /// </summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>
    /// ファイルサイズ（バイト）
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; init; }
}
