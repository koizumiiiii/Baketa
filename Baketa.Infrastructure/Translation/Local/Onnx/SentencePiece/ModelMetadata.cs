using System;
using System.Text.Json.Serialization;

namespace Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// ダウンロードしたモデルのメタデータ
/// </summary>
public class ModelMetadata
{
    /// <summary>
    /// モデル名
    /// </summary>
    [JsonPropertyName("modelName")]
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// ダウンロード日時（UTC）
    /// </summary>
    [JsonPropertyName("downloadedAt")]
    public DateTime DownloadedAt { get; set; }

    /// <summary>
    /// バージョン（ETagなど）
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// ファイルサイズ（バイト）
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// SHA256チェックサム
    /// </summary>
    [JsonPropertyName("checksum")]
    public string Checksum { get; set; } = string.Empty;

    /// <summary>
    /// 最終アクセス日時（UTC）
    /// </summary>
    [JsonPropertyName("lastAccessedAt")]
    public DateTime LastAccessedAt { get; set; }

    /// <summary>
    /// ダウンロード元URL
    /// </summary>
    [JsonPropertyName("sourceUrl")]
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>
    /// モデルの説明（オプション）
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
