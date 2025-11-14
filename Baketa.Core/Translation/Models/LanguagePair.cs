using System;

namespace Baketa.Core.Translation.Models;

/// <summary>
/// 翻訳の言語ペア(ソース言語と対象言語のペア)を表すクラス
/// </summary>
public class LanguagePair : IEquatable<LanguagePair>
{
    /// <summary>
    /// ソース言語
    /// </summary>
    public required Language SourceLanguage { get; set; }

    /// <summary>
    /// 対象言語
    /// </summary>
    public required Language TargetLanguage { get; set; }

    /// <summary>
    /// デフォルトコンストラクタ
    /// </summary>
    public LanguagePair()
    {
    }

    /// <summary>
    /// ソース言語と対象言語を指定して言語ペアを初期化
    /// </summary>
    /// <param name="sourceLanguage">ソース言語</param>
    /// <param name="targetLanguage">対象言語</param>
    public LanguagePair(Language sourceLanguage, Language targetLanguage)
    {
        SourceLanguage = sourceLanguage;
        TargetLanguage = targetLanguage;
    }

    /// <summary>
    /// 言語コードを指定して言語ペアを初期化
    /// </summary>
    /// <param name="sourceLanguageCode">ソース言語コード</param>
    /// <param name="targetLanguageCode">対象言語コード</param>
    public LanguagePair(string sourceLanguageCode, string targetLanguageCode)
    {
        SourceLanguage = Language.FromCode(sourceLanguageCode);
        TargetLanguage = Language.FromCode(targetLanguageCode);
    }

    /// <summary>
    /// 言語ペアを作成します
    /// </summary>
    /// <param name="sourceLanguage">元言語</param>
    /// <param name="targetLanguage">対象言語</param>
    /// <returns>言語ペアインスタンス</returns>
    public static LanguagePair Create(Language sourceLanguage, Language targetLanguage)
    {
        return new LanguagePair
        {
            SourceLanguage = sourceLanguage,
            TargetLanguage = targetLanguage
        };
    }

    /// <summary>
    /// 等価比較の実装
    /// </summary>
    public bool Equals(LanguagePair? other)
    {
        if (other is null)
            return false;

        return SourceLanguage.Equals(other.SourceLanguage) &&
               TargetLanguage.Equals(other.TargetLanguage);
    }

    /// <summary>
    /// 等価比較をオーバーライド
    /// </summary>
    public override bool Equals(object? obj)
    {
        return Equals(obj as LanguagePair);
    }

    /// <summary>
    /// ハッシュコードを生成
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(SourceLanguage, TargetLanguage);
    }

    /// <summary>
    /// 文字列表現を返す
    /// </summary>
    public override string ToString()
    {
        return $"{SourceLanguage.Code}{(SourceLanguage.RegionCode != null ? $"-{SourceLanguage.RegionCode}" : "")} → " +
               $"{TargetLanguage.Code}{(TargetLanguage.RegionCode != null ? $"-{TargetLanguage.RegionCode}" : "")}";
    }

    /// <summary>
    /// 文字列表現から言語ペアを作成（"ja-en"形式）
    /// </summary>
    /// <param name="pairString">言語ペア文字列（"ja-en"形式）</param>
    /// <returns>言語ペアオブジェクト</returns>
    public static LanguagePair FromString(string pairString)
    {
        if (string.IsNullOrEmpty(pairString))
        {
            throw new ArgumentException("言語ペア文字列は空にできません", nameof(pairString));
        }

        string[] parts = pairString.Split('-');
        if (parts.Length != 2)
        {
            throw new ArgumentException("言語ペア文字列は'source-target'形式でなければなりません", nameof(pairString));
        }

        return new LanguagePair { SourceLanguage = Language.FromCode(parts[0]), TargetLanguage = Language.FromCode(parts[1]) };
    }

    /// <summary>
    /// サーバー管理用の標準化されたキーを生成
    /// STEP7 IsReady失敗問題の根本解決 - 一貫した言語ペアキー形式
    /// </summary>
    /// <returns>標準化された言語ペアキー（"source-target"形式）</returns>
    public string ToServerKey()
    {
        return $"{SourceLanguage.Code}-{TargetLanguage.Code}";
    }

    /// <summary>
    /// ソース言語コードを取得
    /// </summary>
    public string SourceCode => SourceLanguage?.Code ?? string.Empty;

    /// <summary>
    /// ターゲット言語コードを取得
    /// </summary>
    public string TargetCode => TargetLanguage?.Code ?? string.Empty;
}
