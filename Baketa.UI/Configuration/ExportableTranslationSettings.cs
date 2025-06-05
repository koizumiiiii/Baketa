using System;
using System.Text.Json.Serialization;
using Baketa.UI.Models;

namespace Baketa.UI.Configuration;

/// <summary>
/// エクスポート可能な翻訳設定データ
/// </summary>
public sealed record ExportableTranslationSettings
{
    /// <summary>
    /// 設定のバージョン（将来の互換性のため）
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0";

    /// <summary>
    /// エクスポート日時
    /// </summary>
    [JsonPropertyName("exportedAt")]
    public DateTime ExportedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 選択された翻訳エンジン
    /// </summary>
    [JsonPropertyName("selectedEngine")]
    public TranslationEngine SelectedEngine { get; init; }

    /// <summary>
    /// 選択された言語ペア
    /// </summary>
    [JsonPropertyName("selectedLanguagePair")]
    public string SelectedLanguagePair { get; init; } = string.Empty;

    /// <summary>
    /// 選択された中国語変種
    /// </summary>
    [JsonPropertyName("selectedChineseVariant")]
    public ChineseVariant SelectedChineseVariant { get; init; }

    /// <summary>
    /// 選択された翻訳戦略
    /// </summary>
    [JsonPropertyName("selectedStrategy")]
    public TranslationStrategy SelectedStrategy { get; init; }

    /// <summary>
    /// フォールバック機能の有効状態
    /// </summary>
    [JsonPropertyName("enableFallback")]
    public bool EnableFallback { get; init; }

    /// <summary>
    /// 最後に保存された日時
    /// </summary>
    [JsonPropertyName("lastSaved")]
    public DateTime LastSaved { get; init; }

    /// <summary>
    /// エクスポート時のコメント（オプション）
    /// </summary>
    [JsonPropertyName("comments")]
    public string? Comments { get; init; }

    /// <summary>
    /// アプリケーションバージョン（参考情報）
    /// </summary>
    [JsonPropertyName("applicationVersion")]
    public string? ApplicationVersion { get; init; }
}

/// <summary>
/// 設定インポート結果
/// </summary>
public sealed class ImportResult
{
    /// <summary>
    /// インポートが成功したか
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// インポートされた設定（成功時）
    /// </summary>
    public ExportableTranslationSettings? Settings { get; init; }

    /// <summary>
    /// エラーメッセージ（失敗時）
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 警告メッセージ（部分的成功時）
    /// </summary>
    public string? WarningMessage { get; init; }

    /// <summary>
    /// インポート時に自動修正された項目があるか
    /// </summary>
    public bool HasAutoCorrections { get; init; }

    /// <summary>
    /// 自動修正の詳細
    /// </summary>
    public string? AutoCorrectionDetails { get; init; }

    /// <summary>
    /// 成功結果を作成
    /// </summary>
    public static ImportResult CreateSuccess(
        ExportableTranslationSettings settings, 
        string? warning = null,
        bool hasAutoCorrections = false,
        string? autoCorrectionDetails = null)
    {
        return new ImportResult
        {
            Success = true,
            Settings = settings,
            WarningMessage = warning,
            HasAutoCorrections = hasAutoCorrections,
            AutoCorrectionDetails = autoCorrectionDetails
        };
    }

    /// <summary>
    /// 失敗結果を作成
    /// </summary>
    public static ImportResult CreateFailure(string errorMessage)
    {
        return new ImportResult
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }
}
