using Baketa.Core.Abstractions.OCR.TextDetection;

namespace Baketa.Core.Abstractions.Memory;

/// <summary>
/// OCR結果オブジェクト専用のプール（汎用的な実装）
/// </summary>
public interface IOcrResultPool<T> : IObjectPool<T> where T : class
{
    /// <summary>
    /// 指定されたテキスト領域数の容量を持つOCR結果を取得
    /// </summary>
    /// <param name="estimatedRegionCount">推定テキスト領域数</param>
    /// <returns>プールされたOCR結果または新規作成オブジェクト</returns>
    T AcquireWithCapacity(int estimatedRegionCount);
}

/// <summary>
/// TextRegion専用のプール
/// </summary>
public interface ITextRegionPool : IObjectPool<TextRegion>
{
    /// <summary>
    /// 初期化されたTextRegionを取得
    /// </summary>
    /// <param name="boundingBox">テキスト領域の境界ボックス</param>
    /// <param name="text">認識されたテキスト</param>
    /// <param name="confidence">信頼度</param>
    /// <returns>プールされたTextRegionまたは新規作成オブジェクト</returns>
    TextRegion AcquireInitialized(Rectangle boundingBox, string text, double confidence);
}

/// <summary>
/// 境界ボックスの座標情報
/// </summary>
public readonly struct Rectangle(int x, int y, int width, int height)
{
    public int X { get; init; } = x;
    public int Y { get; init; } = y;
    public int Width { get; init; } = width;
    public int Height { get; init; } = height;
}
