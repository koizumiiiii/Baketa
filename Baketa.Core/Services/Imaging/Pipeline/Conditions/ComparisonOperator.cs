namespace Baketa.Core.Services.Imaging.Pipeline.Conditions;

/// <summary>
/// 比較演算子を表す列挙型
/// </summary>
public enum ComparisonOperator
{
    /// <summary>
    /// 等しい
    /// </summary>
    Equal,

    /// <summary>
    /// 等しくない
    /// </summary>
    NotEqual,

    /// <summary>
    /// より大きい
    /// </summary>
    GreaterThan,

    /// <summary>
    /// 以上
    /// </summary>
    GreaterThanOrEqual,

    /// <summary>
    /// より小さい
    /// </summary>
    LessThan,

    /// <summary>
    /// 以下
    /// </summary>
    LessThanOrEqual,

    /// <summary>
    /// 含む
    /// </summary>
    Contains,

    /// <summary>
    /// 前方一致
    /// </summary>
    StartsWith,

    /// <summary>
    /// 後方一致
    /// </summary>
    EndsWith
}
