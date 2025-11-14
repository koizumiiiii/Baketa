using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Abstractions.Memory;

/// <summary>
/// IImage → ReferencedSafeImage型変換コンバーター
/// Phase 3.14: UltraThink設計による型変換ブリッジ実装
///
/// 責務:
/// - IImageインターフェースからReferencedSafeImageへの安全な変換
/// - 参照カウント管理の自動化
/// - メモリ効率的な変換プロセス
/// </summary>
public interface IImageToReferencedSafeImageConverter
{
    /// <summary>
    /// IImageをReferencedSafeImageに変換（非同期版）
    /// パイプライン処理での推奨メソッド
    /// </summary>
    /// <param name="image">変換元IImage</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>参照カウント付きSafeImage（初期参照カウント: 1）</returns>
    Task<ReferencedSafeImage> ConvertAsync(
        IImage image,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// IImageをReferencedSafeImageに変換（同期版）
    /// 軽量処理での使用を想定
    /// </summary>
    /// <param name="image">変換元IImage</param>
    /// <returns>参照カウント付きSafeImage（初期参照カウント: 1）</returns>
    ReferencedSafeImage Convert(IImage image);

    /// <summary>
    /// 既存のSafeImageからReferencedSafeImageを作成
    /// SafeImageの所有権はReferencedSafeImageに移譲される
    /// </summary>
    /// <param name="safeImage">変換元SafeImage</param>
    /// <returns>参照カウント付きSafeImage（初期参照カウント: 1）</returns>
    ReferencedSafeImage ConvertFromSafeImage(SafeImage safeImage);
}
