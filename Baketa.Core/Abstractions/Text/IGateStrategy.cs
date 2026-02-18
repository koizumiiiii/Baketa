namespace Baketa.Core.Abstractions.Text;

/// <summary>
/// [Issue #293] テキスト変化に基づくGate判定戦略
/// </summary>
/// <remarks>
/// Geminiフィードバック反映: Strategy パターンにより責務を分離。
/// TextChangeDetectionService の責務を「変化率計算」に限定し、
/// 「ゲート判定」は注入可能な戦略として分離。
/// </remarks>
public interface IGateStrategy
{
    /// <summary>
    /// Gate判定を実行
    /// </summary>
    /// <param name="changePercentage">変化率 (0.0-1.0)</param>
    /// <param name="threshold">適用閾値</param>
    /// <param name="currentText">現在のテキスト</param>
    /// <param name="previousText">前回のテキスト（初回はnull）</param>
    /// <returns>判定結果</returns>
    GateDecision Evaluate(
        float changePercentage,
        float threshold,
        string currentText,
        string? previousText);
}

/// <summary>
/// [Issue #293] Gate判定理由
/// </summary>
public enum GateDecision
{
    /// <summary>
    /// 初回テキスト（前回なし）→ 翻訳実行
    /// </summary>
    FirstText,

    /// <summary>
    /// 十分な変化あり → 翻訳実行
    /// </summary>
    SufficientChange,

    /// <summary>
    /// 変化不足 → 翻訳スキップ
    /// </summary>
    InsufficientChange,

    /// <summary>
    /// 空テキスト → 翻訳スキップ
    /// </summary>
    EmptyText,

    /// <summary>
    /// 同一テキスト → 翻訳スキップ
    /// </summary>
    SameText,

    /// <summary>
    /// 最小文字数未満 → 翻訳スキップ
    /// </summary>
    TextTooShort,

    /// <summary>
    /// 大幅な長さ変化 → 翻訳実行
    /// </summary>
    SignificantLengthChange,

    /// <summary>
    /// [Issue #432] タイプライター成長中 → 翻訳遅延
    /// </summary>
    TypewriterGrowing,

    /// <summary>
    /// [Issue #432] タイプライター最大遅延超過 → 強制翻訳
    /// </summary>
    TypewriterMaxDelayExceeded
}
