using System.Text.Json;
using System.Text.Json.Serialization;

namespace Baketa.Infrastructure.Translation.Cloud;

/// <summary>
/// Cloud AI翻訳サービス共通ヘルパー
/// </summary>
/// <remarks>
/// [Issue #351] DirectGeminiImageTranslator削除により、現在はDirectOpenAIImageTranslatorで使用。
/// JSONパース、言語コード変換などのユーティリティメソッドを提供。
/// </remarks>
internal static class CloudTranslationHelpers
{
    /// <summary>
    /// snake_case用JsonSerializerOptions（翻訳結果パース用）
    /// </summary>
    internal static readonly JsonSerializerOptions SnakeCaseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// camelCase用JsonSerializerOptions（APIリクエスト用）
    /// </summary>
    internal static readonly JsonSerializerOptions CamelCaseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// レスポンスからJSON部分を抽出
    /// </summary>
    /// <param name="response">APIレスポンステキスト</param>
    /// <returns>抽出されたJSON文字列</returns>
    internal static string ExtractJsonFromResponse(string response)
    {
        var jsonText = response.Trim();

        // マークダウンのコードブロックを除去
        if (jsonText.StartsWith("```json", StringComparison.Ordinal))
        {
            jsonText = jsonText[7..];
        }
        else if (jsonText.StartsWith("```", StringComparison.Ordinal))
        {
            jsonText = jsonText[3..];
        }

        if (jsonText.EndsWith("```", StringComparison.Ordinal))
        {
            jsonText = jsonText[..^3];
        }

        return jsonText.Trim();
    }

    /// <summary>
    /// 言語コードから表示名を取得
    /// </summary>
    /// <param name="languageCode">ISO 639-1言語コード</param>
    /// <returns>言語の日本語表示名</returns>
    internal static string GetLanguageDisplayName(string languageCode)
    {
        return languageCode.ToLowerInvariant() switch
        {
            "ja" => "日本語",
            "en" => "英語",
            "ko" => "韓国語",
            "zh" or "zh-cn" => "中国語（簡体字）",
            "zh-tw" => "中国語（繁体字）",
            "es" => "スペイン語",
            "fr" => "フランス語",
            "de" => "ドイツ語",
            "it" => "イタリア語",
            "pt" => "ポルトガル語",
            "ru" => "ロシア語",
            _ => languageCode
        };
    }
}
