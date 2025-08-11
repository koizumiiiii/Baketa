namespace Baketa.Core.Exceptions.Capture;

/// <summary>
/// キャプチャ関連の基底例外
/// </summary>
public abstract class CaptureException(string message, Exception? innerException = null) : Exception(message, innerException)
{
}

/// <summary>
/// GPU制約による例外
/// </summary>
public class GPUConstraintException(uint requestedSize, uint maximumSize) : CaptureException($"要求サイズ {requestedSize} が最大サイズ {maximumSize} を超えています")
{
    public uint RequestedSize { get; } = requestedSize;
    public uint MaximumSize { get; } = maximumSize;
}

/// <summary>
/// TDR（Timeout Detection and Recovery）例外
/// </summary>
public class TDRException(int hResult) : CaptureException($"GPU タイムアウトが検出されました (HRESULT: 0x{hResult:X8})")
{
    public new int HResult { get; } = hResult;
}

/// <summary>
/// キャプチャ戦略実行失敗例外
/// </summary>
public class CaptureStrategyException(string strategyName, string message, Exception? innerException = null) : CaptureException($"キャプチャ戦略 '{strategyName}' の実行に失敗: {message}", innerException)
{
    public string StrategyName { get; } = strategyName;
}

/// <summary>
/// GPU環境検出失敗例外
/// </summary>
public class GPUEnvironmentDetectionException(string message, Exception? innerException = null) : CaptureException($"GPU環境検出に失敗: {message}", innerException)
{
}

/// <summary>
/// テキスト領域検出失敗例外
/// </summary>
public class TextRegionDetectionException(string message, Exception? innerException = null) : CaptureException($"テキスト領域検出に失敗: {message}", innerException)
{
}
