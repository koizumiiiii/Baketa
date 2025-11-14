using System;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Events.EventTypes;

/// <summary>
/// OCR失敗イベント
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="sourceImage">OCR処理元の画像</param>
/// <param name="exception">発生した例外</param>
/// <param name="errorMessage">エラーメッセージ</param>
/// <param name="elapsedTime">経過時間</param>
public class OcrFailedEvent(IImage? sourceImage, Exception? exception, string? errorMessage = null, TimeSpan elapsedTime = default) : EventBase
{
    /// <summary>
    /// OCR処理元の画像
    /// </summary>
    public IImage? SourceImage { get; } = sourceImage;

    /// <summary>
    /// 発生した例外
    /// </summary>
    public Exception? Exception { get; } = exception;

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string ErrorMessage { get; } = errorMessage ?? exception?.Message ?? "不明なエラー";

    /// <summary>
    /// 経過時間
    /// </summary>
    public TimeSpan ElapsedTime { get; } = elapsedTime;

    /// <inheritdoc />
    public override string Name => "OcrFailed";

    /// <inheritdoc />
    public override string Category => "OCR";
}
