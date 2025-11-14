namespace Baketa.Core.Models.Translation;

/// <summary>
/// 翻訳言語ペア（ソース言語→ターゲット言語）
/// バリデーション機能付きで翻訳の妥当性を保証
/// </summary>
public sealed record LanguagePair(Language Source, Language Target)
{
    /// <summary>
    /// ソース言語コード
    /// </summary>
    public string SourceCode => Source.Code;

    /// <summary>
    /// ターゲット言語コード
    /// </summary>
    public string TargetCode => Target.Code;

    /// <summary>
    /// デフォルト言語ペア（日本語→英語）
    /// UI設定のデフォルト値として使用
    /// </summary>
    public static LanguagePair Default => new(Language.Japanese, Language.English);

    /// <summary>
    /// 自動検出→英語ペア
    /// 自動言語検出時の標準設定
    /// </summary>
    public static LanguagePair AutoToEnglish => new(Language.Auto, Language.English);

    /// <summary>
    /// 翻訳に有効な言語ペアかどうか
    /// 同一言語間の翻訳や無効な組み合わせを除外
    /// </summary>
    /// <returns>翻訳可能な場合はtrue</returns>
    public bool IsValidForTranslation()
    {
        // 同一言語は翻訳不要
        if (Source.Equals(Target))
            return false;

        // ターゲット言語は自動検出以外である必要がある
        if (!Target.IsValidForTranslation)
            return false;

        return true;
    }

    /// <summary>
    /// 逆方向の言語ペアを取得
    /// </summary>
    /// <returns>ターゲット→ソースの言語ペア</returns>
    public LanguagePair Reverse() => new(Target, Source);

    /// <summary>
    /// 言語ペアの文字列表現（"ja→en"形式）
    /// </summary>
    /// <returns>言語コードペア文字列</returns>
    public override string ToString() => $"{SourceCode}→{TargetCode}";

    /// <summary>
    /// 表示用文字列（"Japanese→English"形式）
    /// </summary>
    /// <returns>言語表示名ペア文字列</returns>
    public string ToDisplayString() => $"{Source.DisplayName}→{Target.DisplayName}";

    /// <summary>
    /// 言語コードから言語ペアを作成
    /// </summary>
    /// <param name="sourceCode">ソース言語コード</param>
    /// <param name="targetCode">ターゲット言語コード</param>
    /// <returns>言語ペア</returns>
    /// <exception cref="ArgumentException">無効な言語コードの場合</exception>
    public static LanguagePair FromCodes(string sourceCode, string targetCode)
    {
        var source = Language.FromCode(sourceCode);
        var target = Language.FromCode(targetCode);
        return new LanguagePair(source, target);
    }

    /// <summary>
    /// サーバー管理用の標準化されたキーを生成
    /// STEP7 IsReady失敗問題の根本解決 - 一貫した言語ペアキー形式
    /// </summary>
    /// <returns>標準化された言語ペアキー（"source-target"形式）</returns>
    public string ToServerKey()
    {
        return $"{SourceCode}-{TargetCode}";
    }

    /// <summary>
    /// よく使用される言語ペアの一覧
    /// </summary>
    public static IReadOnlyList<LanguagePair> CommonPairs =>
    [
        new(Language.Japanese, Language.English),
        new(Language.English, Language.Japanese),
        new(Language.Auto, Language.English),
        new(Language.Auto, Language.Japanese)
    ];
}
