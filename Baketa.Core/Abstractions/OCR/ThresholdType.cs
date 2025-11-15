namespace Baketa.Core.Abstractions.OCR;

/// <summary>
/// 閾値処理のタイプ
/// </summary>
public enum ThresholdType
{
    /// <summary>
    /// バイナリ閾値処理: threshold より大きい値は maxValue になり、それ以外は 0 になる
    /// </summary>
    Binary,

    /// <summary>
    /// 反転バイナリ閾値処理: threshold より大きい値は 0 になり、それ以外は maxValue になる
    /// </summary>
    BinaryInv,

    /// <summary>
    /// 切り捨て閾値処理: threshold より大きい値は threshold になり、それ以外は元の値のまま
    /// </summary>
    Truncate,

    /// <summary>
    /// ゼロ閾値処理: threshold より大きい値は元の値のまま、それ以外は 0 になる
    /// </summary>
    ToZero,

    /// <summary>
    /// 反転ゼロ閾値処理: threshold より大きい値は 0 になり、それ以外は元の値のまま
    /// </summary>
    ToZeroInv,

    /// <summary>
    /// Otsu アルゴリズムを使用して自動的に最適な閾値を決定
    /// </summary>
    Otsu,

    /// <summary>
    /// 適応的閾値処理を使用（blockSize と C パラメータが必要）
    /// </summary>
    Adaptive
}
