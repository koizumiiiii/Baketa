namespace Baketa.Core.Abstractions.Validation;

/// <summary>
/// ファジーテキストマッチングインターフェース
/// レーベンシュタイン距離を用いたテキスト類似度判定
/// </summary>
/// <remarks>
/// Issue #78 Phase 3: ローカルOCRとCloud AI結果の相互検証に使用
/// </remarks>
public interface IFuzzyTextMatcher
{
    /// <summary>
    /// 2つのテキスト間の類似度を計算 (0.0 - 1.0)
    /// </summary>
    /// <param name="text1">比較対象テキスト1</param>
    /// <param name="text2">比較対象テキスト2</param>
    /// <returns>類似度（0.0=完全不一致、1.0=完全一致）</returns>
    float CalculateSimilarity(string text1, string text2);

    /// <summary>
    /// テキストが一致するか判定（長さに応じた閾値を自動適用）
    /// </summary>
    /// <param name="text1">比較対象テキスト1</param>
    /// <param name="text2">比較対象テキスト2</param>
    /// <returns>マッチング結果</returns>
    /// <remarks>
    /// 閾値:
    /// - 1-5文字: 90%以上
    /// - 6-9文字: 85%以上
    /// - 10文字以上: 80%以上
    /// </remarks>
    FuzzyMatchResult IsMatch(string text1, string text2);

    /// <summary>
    /// カスタム閾値でマッチング判定
    /// </summary>
    /// <param name="text1">比較対象テキスト1</param>
    /// <param name="text2">比較対象テキスト2</param>
    /// <param name="threshold">カスタム閾値 (0.0 - 1.0)</param>
    /// <returns>マッチング結果</returns>
    FuzzyMatchResult IsMatch(string text1, string text2, float threshold);
}
