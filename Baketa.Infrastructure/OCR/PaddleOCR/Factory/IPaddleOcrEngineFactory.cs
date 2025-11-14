using Baketa.Core.Abstractions.OCR;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Factory;

/// <summary>
/// PaddleOCRエンジンインスタンス作成ファクトリーインターフェース
/// OCRエンジンプール化のためのファクトリーパターン実装
/// </summary>
public interface IPaddleOcrEngineFactory
{
    /// <summary>
    /// 新しいPaddleOCRエンジンインスタンスを作成します
    /// </summary>
    /// <returns>新しいIOcrEngineインスタンス</returns>
    Task<IOcrEngine> CreateAsync();

    /// <summary>
    /// エンジンインスタンスをクリーンアップします
    /// プールへの返却時に呼び出されます
    /// </summary>
    /// <param name="engine">クリーンアップ対象エンジン</param>
    Task CleanupAsync(IOcrEngine engine);

    /// <summary>
    /// エンジンインスタンスが再利用可能かどうかを判定します
    /// </summary>
    /// <param name="engine">判定対象エンジン</param>
    /// <returns>再利用可能な場合true</returns>
    bool IsReusable(IOcrEngine engine);
}
