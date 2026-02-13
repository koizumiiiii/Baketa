using System;
using Baketa.Core.Translation.Abstractions;

namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// [Issue #415] Cloud翻訳結果のFork-Join段階キャッシュ
/// 画像ハッシュによりAPIコール前に画面変化を検出し、変化なしの場合は前回結果を再利用してトークン消費を削減
/// </summary>
public interface ICloudTranslationCache
{
    /// <summary>
    /// 画像データからXxHash64ハッシュを計算（サンプリングベースで高速）
    /// </summary>
    long ComputeImageHash(ReadOnlyMemory<byte> imageData);

    /// <summary>
    /// キャッシュからCloud結果を取得（画像ハッシュ一致 + TTL有効）
    /// </summary>
    bool TryGetCachedResult(IntPtr windowHandle, long imageHash,
        out FallbackTranslationResult? result);

    /// <summary>
    /// Cloud結果をキャッシュに保存
    /// </summary>
    void CacheResult(IntPtr windowHandle, long imageHash,
        FallbackTranslationResult result);

    /// <summary>
    /// ウィンドウのキャッシュをクリア
    /// </summary>
    void ClearWindow(IntPtr windowHandle);

    /// <summary>
    /// 全キャッシュクリア
    /// </summary>
    void ClearAll();
}
