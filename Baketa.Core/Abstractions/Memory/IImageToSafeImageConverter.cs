using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Abstractions.Memory;

/// <summary>
/// IImage→SafeImage変換のためのコンバーター
/// Phase 3.13: 型変換ブリッジの実装
/// </summary>
public interface IImageToSafeImageConverter
{
    /// <summary>
    /// IImageをSafeImageに変換（標準メソッド）
    /// パフォーマンス：ReadOnlyMemory&lt;byte&gt;から直接コピーで効率的
    /// </summary>
    /// <param name="image">変換元IImage</param>
    /// <returns>変換後SafeImage</returns>
    Task<SafeImage> ConvertAsync(IImage image);

    /// <summary>
    /// IImageからReadOnlyMemory&lt;byte&gt;を明示的に使用してSafeImage作成
    /// 注意：ConvertAsyncと同等の実装。後方互換性のために残存
    /// 推奨：新しいコードではConvertAsyncを使用してください
    /// </summary>
    /// <param name="image">変換元IImage</param>
    /// <returns>変換後SafeImage</returns>
    Task<SafeImage> ConvertFromMemoryAsync(IImage image);

    /// <summary>
    /// 同期版: IImageをSafeImageに変換
    /// </summary>
    /// <param name="image">変換元IImage</param>
    /// <returns>変換後SafeImage</returns>
    SafeImage Convert(IImage image);
}