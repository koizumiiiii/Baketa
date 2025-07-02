using System;
using System.Collections.Generic;
using System.Linq;

namespace Baketa.Core.Translation.Models;

/// <summary>
/// 拡張された言語情報
/// 翻訳エンジンで使用する追加情報を含む
/// </summary>
public class LanguageInfo
{
    /// <summary>
    /// 言語コード (例: "en", "ja", "zh-Hans")
    /// </summary>
    public required string Code { get; set; }

    /// <summary>
    /// 言語の表示名 (例: "English", "日本語", "中国語（簡体字）")
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// 言語のネイティブ名 (例: "English", "日本語", "中文（简体）")
    /// </summary>
    public string? NativeName { get; set; }

    /// <summary>
    /// OPUS-MTで使用するプレフィックス (例: ">>cmn_Hans<<")
    /// </summary>
    public string? OpusPrefix { get; set; }

    /// <summary>
    /// 中国語変種（中国語の場合のみ）
    /// </summary>
    public ChineseVariant? Variant { get; set; }

    /// <summary>
    /// 地域コード (例: "US", "JP", "CN", "TW")
    /// </summary>
    public string? RegionCode { get; set; }

    /// <summary>
    /// この言語がRTL（右から左）かどうか
    /// </summary>
    public bool IsRightToLeft { get; set; }

    /// <summary>
    /// この言語が自動検出用かどうか
    /// </summary>
    public bool IsAutoDetect { get; set; }

    /// <summary>
    /// この言語が翻訳でサポートされているかどうか
    /// </summary>
    public bool IsSupported { get; set; } = true;

    /// <summary>
    /// デフォルトコンストラクタ
    /// </summary>
    public LanguageInfo()
    {
    }

    /// <summary>
    /// 基本情報を指定してLanguageInfoを作成
    /// </summary>
    /// <param name="code">言語コード</param>
    /// <param name="name">表示名</param>
    /// <param name="nativeName">ネイティブ名</param>
    /// <param name="opusPrefix">OPUS-MTプレフィックス</param>
    /// <param name="variant">中国語変種</param>
    public LanguageInfo(string code, string name, string? nativeName = null, 
        string? opusPrefix = null, ChineseVariant? variant = null)
    {
        Code = code;
        Name = name;
        NativeName = nativeName;
        OpusPrefix = opusPrefix;
        Variant = variant;
    }

    /// <summary>
    /// 既存のLanguageオブジェクトからLanguageInfoを作成
    /// </summary>
    /// <param name="language">既存の言語オブジェクト</param>
    /// <returns>LanguageInfo</returns>
    public static LanguageInfo FromLanguage(Language language)
    {
        ArgumentNullException.ThrowIfNull(language);

        // 中国語変種の判定 - 型安全な方法で行う
        ChineseVariant? variant = null;
        if (language.Code.StartsWith("zh", StringComparison.OrdinalIgnoreCase) || 
            language.Code.StartsWith("cmn", StringComparison.OrdinalIgnoreCase) || 
            language.Code.StartsWith("yue", StringComparison.OrdinalIgnoreCase))
        {
            variant = ChineseVariantExtensions.FromLanguageCode(language.Code);
        }

        return new LanguageInfo
        {
            Code = language.Code,
            Name = language.DisplayName ?? language.Name ?? language.Code,
            NativeName = language.NativeName,
            OpusPrefix = variant?.GetOpusPrefix() ?? string.Empty,
            Variant = variant,
            RegionCode = language.RegionCode,
            IsRightToLeft = language.IsRightToLeft,
            IsAutoDetect = language.IsAutoDetect,
            IsSupported = true
        };
    }

    /// <summary>
    /// LanguageInfoからLanguageオブジェクトを作成
    /// </summary>
    /// <returns>Language</returns>
    public Language ToLanguage()
    {
        return new Language
        {
            Code = Code,
            DisplayName = Name,
            Name = Name, // 互換性のため
            NativeName = NativeName ?? Name,
            RegionCode = RegionCode,
            IsRightToLeft = IsRightToLeft,
            IsAutoDetect = IsAutoDetect
        };
    }

    /// <summary>
    /// この言語が中国語系かどうかを判定
    /// </summary>
    /// <returns>中国語系の場合はtrue</returns>
    public bool IsChinese()
    {
        return Variant.HasValue || 
               Code.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ||
               Code.StartsWith("cmn", StringComparison.OrdinalIgnoreCase) ||
               Code.StartsWith("yue", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 等価比較
    /// </summary>
    public override bool Equals(object? obj)
    {
        if (obj is not LanguageInfo other)
            return false;

        return string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase) &&
               Variant == other.Variant;
    }

    /// <summary>
    /// ハッシュコード生成
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(Code ?? string.Empty),
            Variant);
    }

    /// <summary>
    /// 文字列表現
    /// </summary>
    public override string ToString()
    {
        if (Variant.HasValue && Variant.Value != ChineseVariant.Auto)
        {
            return $"{Name} ({Code}, {Variant.Value.GetDisplayName()})";
        }

        return $"{Name} ({Code})";
    }
}