using System;
using System.Collections.Generic;

namespace Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// SentencePieceモデルのメタデータ
/// </summary>
public class ModelMetadata
{
    /// <summary>
    /// モデル名
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// ダウンロード日時
    /// </summary>
    public DateTime DownloadedAt { get; set; }

    /// <summary>
    /// モデルのバージョン
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// ファイルサイズ（バイト）
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// チェックサム（SHA256）
    /// </summary>
    public string Checksum { get; set; } = string.Empty;

    /// <summary>
    /// 最終アクセス日時
    /// </summary>
    public DateTime LastAccessedAt { get; set; }

    /// <summary>
    /// ダウンロード元URL
    /// </summary>
    public Uri? SourceUrl { get; set; }

    /// <summary>
    /// モデルの説明
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 対応言語ペア（ソース言語）
    /// </summary>
    public string SourceLanguage { get; set; } = string.Empty;

    /// <summary>
    /// 対応言語ペア（ターゲット言語）
    /// </summary>
    public string TargetLanguage { get; set; } = string.Empty;

    /// <summary>
    /// モデルの種類（例: "SentencePiece", "ONNX"）
    /// </summary>
    public string ModelType { get; set; } = "SentencePiece";

    /// <summary>
    /// カスタムメタデータのディクショナリ
    /// </summary>
    public Dictionary<string, object> CustomMetadata { get; set; } = [];

    /// <summary>
    /// モデルの有効期限（オプション）
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// モデルが有効かどうかを判定
    /// </summary>
    /// <param name="cacheDays">キャッシュ有効期限（日数）</param>
    /// <returns>有効かどうか</returns>
    public bool IsValid(int cacheDays)
    {
        // 有効期限が設定されている場合は、それを優先
        if (ExpiresAt.HasValue)
        {
            return DateTime.UtcNow < ExpiresAt.Value;
        }

        // キャッシュ有効期限をチェック
        return DateTime.UtcNow < DownloadedAt.AddDays(cacheDays);
    }

    /// <summary>
    /// メタデータのコピーを作成
    /// </summary>
    /// <returns>メタデータのコピー</returns>
    public ModelMetadata Clone()
    {
        return new ModelMetadata
        {
            ModelName = ModelName,
            DownloadedAt = DownloadedAt,
            Version = Version,
            Size = Size,
            Checksum = Checksum,
            LastAccessedAt = LastAccessedAt,
            SourceUrl = SourceUrl,
            Description = Description,
            SourceLanguage = SourceLanguage,
            TargetLanguage = TargetLanguage,
            ModelType = ModelType,
            CustomMetadata = new Dictionary<string, object>(CustomMetadata),
            ExpiresAt = ExpiresAt
        };
    }

    /// <summary>
    /// 言語ペアの文字列表現
    /// </summary>
    public string LanguagePair
    {
        get
        {
            if (string.IsNullOrEmpty(SourceLanguage) || string.IsNullOrEmpty(TargetLanguage))
            {
                return string.Empty;
            }

            return $"{SourceLanguage}-{TargetLanguage}";
        }
    }

    /// <summary>
    /// ファイルサイズの人間が読みやすい表現
    /// </summary>
    public string FormattedSize
    {
        get
        {
            const long KB = 1024;
            const long MB = KB * 1024;
            const long GB = MB * 1024;

            return Size switch
            {
                < KB => $"{Size} B",
                < MB => $"{Size / (double)KB:F1} KB",
                < GB => $"{Size / (double)MB:F1} MB",
                _ => $"{Size / (double)GB:F1} GB"
            };
        }
    }

    /// <summary>
    /// 経過時間の人間が読みやすい表現を取得
    /// </summary>
    /// <returns>経過時間の文字列（例: "3 days ago"）</returns>
#pragma warning disable CA1024 // Use properties where appropriate - このメソッドは計算処理があるためメソッドが適切
    public string GetTimeAgo()
#pragma warning restore CA1024
    {
        var now = DateTime.UtcNow;
        var timeSpan = now - LastAccessedAt;

        return timeSpan.TotalDays switch
        {
            < 1 when timeSpan.TotalHours < 1 => $"{(int)timeSpan.TotalMinutes} minutes ago",
            < 1 => $"{(int)timeSpan.TotalHours} hours ago",
            < 7 => $"{(int)timeSpan.TotalDays} days ago",
            < 30 => $"{(int)(timeSpan.TotalDays / 7)} weeks ago",
            < 365 => $"{(int)(timeSpan.TotalDays / 30)} months ago",
            _ => $"{(int)(timeSpan.TotalDays / 365)} years ago"
        };
    }

    /// <summary>
    /// デバッグ用の文字列表現
    /// </summary>
    /// <returns>デバッグ情報</returns>
    public override string ToString()
    {
        return $"ModelMetadata[{ModelName}] {LanguagePair} - {FormattedSize} - {Version}";
    }

    /// <summary>
    /// 妥当性検証
    /// </summary>
    /// <returns>検証結果</returns>
    public ValidationResult Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ModelName))
        {
            errors.Add("ModelName is required");
        }

        if (Size <= 0)
        {
            errors.Add("Size must be greater than 0");
        }

        if (DownloadedAt == default)
        {
            errors.Add("DownloadedAt must be set");
        }

        if (DownloadedAt > DateTime.UtcNow)
        {
            errors.Add("DownloadedAt cannot be in the future");
        }

        if (SourceUrl != null && !SourceUrl.IsAbsoluteUri)
        {
            errors.Add("SourceUrl must be an absolute URL if provided");
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors.AsReadOnly()
        };
    }
}

/// <summary>
/// メタデータの妥当性検証結果
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// 妥当かどうか
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// エラーメッセージのリスト
    /// </summary>
    public IReadOnlyList<string> Errors { get; set; } = [];

    /// <summary>
    /// エラーメッセージを結合した文字列を取得
    /// </summary>
    /// <returns>エラーメッセージ</returns>
    public string GetErrorMessage()
    {
        return string.Join("; ", Errors);
    }
}
