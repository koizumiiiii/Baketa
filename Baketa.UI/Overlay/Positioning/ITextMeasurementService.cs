using Baketa.Core.UI.Overlay.Positioning;

namespace Baketa.UI.Overlay.Positioning;

/// <summary>
/// テキスト測定サービスのインターフェース
/// </summary>
public interface ITextMeasurementService
{
    /// <summary>
    /// テキストを測定します
    /// </summary>
    /// <param name="text">測定するテキスト</param>
    /// <param name="options">測定オプション</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>測定結果</returns>
    Task<TextMeasurementResult> MeasureTextAsync(string text, TextMeasurementOptions options, CancellationToken cancellationToken = default);
}
