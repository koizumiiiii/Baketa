namespace Baketa.Core.Translation.Models;

/// <summary>
/// 中国語文字体系の種類
/// </summary>
public enum ChineseScriptType
{
    /// <summary>
    /// 不明（判定不可能）
    /// </summary>
    Unknown,

    /// <summary>
    /// 簡体字
    /// </summary>
    Simplified,

    /// <summary>
    /// 繁体字
    /// </summary>
    Traditional,

    /// <summary>
    /// 混在（簡体字と繁体字が混在）
    /// </summary>
    Mixed
}

/// <summary>
/// ChineseScriptType拡張メソッド
/// </summary>
public static class ChineseScriptTypeExtensions
{
    /// <summary>
    /// 文字体系の表示名を取得
    /// </summary>
    /// <param name="scriptType">文字体系</param>
    /// <returns>表示名</returns>
    public static string GetDisplayName(this ChineseScriptType scriptType)
    {
        return scriptType switch
        {
            ChineseScriptType.Simplified => "Simplified Chinese",
            ChineseScriptType.Traditional => "Traditional Chinese",
            ChineseScriptType.Mixed => "Mixed Chinese",
            ChineseScriptType.Unknown => "Unknown Chinese",
            _ => "Chinese"
        };
    }

    /// <summary>
    /// 文字体系のネイティブ表示名を取得
    /// </summary>
    /// <param name="scriptType">文字体系</param>
    /// <returns>ネイティブ表示名</returns>
    public static string GetNativeDisplayName(this ChineseScriptType scriptType)
    {
        return scriptType switch
        {
            ChineseScriptType.Simplified => "简体中文",
            ChineseScriptType.Traditional => "繁體中文",
            ChineseScriptType.Mixed => "中文混合",
            ChineseScriptType.Unknown => "中文未知",
            _ => "中文"
        };
    }

    /// <summary>
    /// 文字体系が有効かどうかを判定
    /// </summary>
    /// <param name="scriptType">文字体系</param>
    /// <returns>有効な場合はtrue</returns>
    public static bool IsValid(this ChineseScriptType scriptType)
    {
        return Enum.IsDefined<ChineseScriptType>(scriptType);
    }

    /// <summary>
    /// 対応するChineseVariantを取得
    /// </summary>
    /// <param name="scriptType">文字体系</param>
    /// <returns>対応するChineseVariant</returns>
    public static ChineseVariant ToChineseVariant(this ChineseScriptType scriptType)
    {
        return scriptType switch
        {
            ChineseScriptType.Simplified => ChineseVariant.Simplified,
            ChineseScriptType.Traditional => ChineseVariant.Traditional,
            ChineseScriptType.Mixed => ChineseVariant.Auto,
            ChineseScriptType.Unknown => ChineseVariant.Auto,
            _ => ChineseVariant.Auto
        };
    }
}
