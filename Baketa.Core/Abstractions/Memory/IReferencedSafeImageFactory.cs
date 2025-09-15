using System;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Memory;

/// <summary>
/// 参照カウント付きSafeImage生成ファクトリのインターフェース
/// Phase 3.11: SafeImage早期破棄問題解決のためのライフサイクル管理
/// </summary>
public interface IReferencedSafeImageFactory
{
    /// <summary>
    /// 生バイトデータから参照カウント付きSafeImageを作成
    /// 初期参照カウントは1
    /// </summary>
    /// <param name="sourceData">元画像データ</param>
    /// <param name="width">画像幅</param>
    /// <param name="height">画像高さ</param>
    /// <param name="pixelFormat">ピクセルフォーマット</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>参照カウント付きSafeImage</returns>
    Task<ReferencedSafeImage> CreateReferencedSafeImageAsync(
        ReadOnlyMemory<byte> sourceData,
        int width,
        int height,
        ImagePixelFormat pixelFormat = ImagePixelFormat.Bgra32,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 既存のSafeImageから参照カウント付きSafeImageを作成
    /// 初期参照カウントは1
    /// 注意: 元のSafeImageの所有権はReferencedSafeImageに移譲される
    /// </summary>
    /// <param name="safeImage">既存のSafeImage</param>
    /// <returns>参照カウント付きSafeImage</returns>
    ReferencedSafeImage CreateFromSafeImage(SafeImage safeImage);

    /// <summary>
    /// 参照カウント付きSafeImageのクローンを作成
    /// 元のインスタンスとは独立した新しい参照カウント付きSafeImage
    /// </summary>
    /// <param name="original">元の参照カウント付きSafeImage</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>クローンされた参照カウント付きSafeImage</returns>
    Task<ReferencedSafeImage> CloneReferencedSafeImageAsync(
        ReferencedSafeImage original,
        CancellationToken cancellationToken = default);
}