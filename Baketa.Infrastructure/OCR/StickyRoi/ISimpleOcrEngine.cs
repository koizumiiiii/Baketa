using System.Drawing;
using Baketa.Core.Abstractions.OCR;

namespace Baketa.Infrastructure.OCR.StickyRoi;

/// <summary>
/// スティッキーROI用の簡易OCRエンジンインターフェース
/// Issue #143 Week 3: テスト・検証専用の軽量インターフェース
/// </summary>
public interface ISimpleOcrEngine : IDisposable
{
    /// <summary>
    /// テキスト認識実行
    /// </summary>
    /// <param name="imageData">画像データ</param>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>OCR結果</returns>
    Task<Baketa.Core.Abstractions.OCR.OcrResult> RecognizeTextAsync(byte[] imageData, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// エンジンの利用可能性確認
    /// </summary>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>利用可能かどうか</returns>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}