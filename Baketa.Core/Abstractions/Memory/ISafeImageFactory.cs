using System;
using System.Buffers;

namespace Baketa.Core.Abstractions.Memory;

/// <summary>
/// SafeImageインスタンス生成専用Factoryインターフェース
/// Core層とApplication層の依存関係を適切に管理するためのFactoryパターン実装
/// </summary>
public interface ISafeImageFactory
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
    SafeImage CreateSafeImage(
        byte[] rentedBuffer,
        ArrayPool<byte> arrayPool,
        int actualDataLength,
        int width,
        int height,
        ImagePixelFormat pixelFormat,
        Guid id);
}