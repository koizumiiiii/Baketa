using System;
using System.Buffers;
using Baketa.Core.Abstractions.Memory;

namespace Baketa.Application.Services.Memory;

/// <summary>
/// SafeImageインスタンス生成Factory実装
/// Core層とApplication層の依存関係を適切に管理するFactoryパターン
/// SafeImageの内部コンストラクタにアクセス可能なApplication層で実装
/// </summary>
public sealed class SafeImageFactory : ISafeImageFactory
{
    /// <summary>
    /// ArrayPool管理下のSafeImageインスタンスを生成
    /// </summary>
    /// <param name="rentedBuffer">ArrayPoolから借用したバッファ</param>
    /// <param name="arrayPool">使用中のArrayPoolインスタンス</param>
    /// <param name="actualDataLength">実際のデータ長</param>
    /// <param name="width">画像幅</param>
    /// <param name="height">画像高さ</param>
    /// <param name="pixelFormat">ピクセルフォーマット</param>
    /// <param name="id">一意識別ID</param>
    /// <returns>生成されたSafeImageインスタンス</returns>
    public SafeImage CreateSafeImage(
        byte[] rentedBuffer,
        ArrayPool<byte> arrayPool,
        int actualDataLength,
        int width,
        int height,
        ImagePixelFormat pixelFormat,
        Guid id)
    {
        // Phase 3: Factory パターンによる安全なSafeImageインスタンス生成
        // Clean Architecture原則を維持しつつ、内部コンストラクタアクセス問題を解決
        return new SafeImage(rentedBuffer, arrayPool, actualDataLength, width, height, pixelFormat, id);
    }
}