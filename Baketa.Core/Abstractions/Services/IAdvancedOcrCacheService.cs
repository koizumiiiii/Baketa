using System;
using Baketa.Core.Abstractions.OCR;

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// Step3: 高度OCRキャッシングサービスのインターフェース
/// Gemini推奨戦略 - 数ミリ秒応答の実現
/// </summary>
public interface IAdvancedOcrCacheService : IDisposable
{
    /// <summary>
    /// 画像データから一意のハッシュを生成します
    /// </summary>
    /// <param name="imageData">画像バイナリデータ</param>
    /// <returns>SHA256ベースハッシュ文字列</returns>
    string GenerateImageHash(byte[] imageData);
    
    /// <summary>
    /// OCR結果をキャッシュに保存します
    /// </summary>
    /// <param name="imageHash">画像ハッシュ</param>
    /// <param name="result">OCR結果</param>
    void CacheResult(string imageHash, OcrResults result);
    
    /// <summary>
    /// キャッシュからOCR結果を取得します
    /// </summary>
    /// <param name="imageHash">画像ハッシュ</param>
    /// <returns>キャッシュされたOCR結果、または null（キャッシュミス時）</returns>
    OcrResults? GetCachedResult(string imageHash);
}