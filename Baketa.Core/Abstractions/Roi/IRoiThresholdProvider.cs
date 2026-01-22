namespace Baketa.Core.Abstractions.Roi;

/// <summary>
/// [Issue #293] ROIベース動的閾値プロバイダーインターフェース
/// </summary>
/// <remarks>
/// ROIヒートマップデータに基づいて、画像変化検知の閾値を動的に調整します。
/// 高頻度テキスト検出領域（高ヒートマップ値）では閾値を上げて感度を高く、
/// 低頻度領域では閾値を下げてノイズ耐性を向上させます。
/// </remarks>
public interface IRoiThresholdProvider
{
    /// <summary>
    /// ROIベース閾値調整が有効かどうか
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 指定位置の閾値を取得
    /// </summary>
    /// <param name="normalizedX">正規化X座標 (0.0～1.0)</param>
    /// <param name="normalizedY">正規化Y座標 (0.0～1.0)</param>
    /// <param name="defaultThreshold">デフォルト閾値</param>
    /// <returns>調整後の閾値</returns>
    float GetThresholdAt(float normalizedX, float normalizedY, float defaultThreshold);

    /// <summary>
    /// グリッドセルの閾値を取得
    /// </summary>
    /// <param name="row">グリッド行インデックス</param>
    /// <param name="column">グリッド列インデックス</param>
    /// <param name="totalRows">グリッド総行数</param>
    /// <param name="totalColumns">グリッド総列数</param>
    /// <param name="defaultThreshold">デフォルト閾値</param>
    /// <returns>調整後の閾値</returns>
    float GetThresholdForCell(int row, int column, int totalRows, int totalColumns, float defaultThreshold);

    /// <summary>
    /// 指定位置が高優先度領域かどうかを判定
    /// </summary>
    /// <param name="normalizedX">正規化X座標 (0.0～1.0)</param>
    /// <param name="normalizedY">正規化Y座標 (0.0～1.0)</param>
    /// <returns>高優先度領域の場合はtrue</returns>
    bool IsHighPriorityRegion(float normalizedX, float normalizedY);

    /// <summary>
    /// 指定位置のヒートマップ値を取得
    /// </summary>
    /// <param name="normalizedX">正規化X座標 (0.0～1.0)</param>
    /// <param name="normalizedY">正規化Y座標 (0.0～1.0)</param>
    /// <returns>ヒートマップ値 (0.0～1.0)</returns>
    float GetHeatmapValueAt(float normalizedX, float normalizedY);
}
