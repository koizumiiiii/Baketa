using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Baketa.UI.Views.Overlay;

namespace Baketa.UI.Services;

/// <summary>
/// オーバーレイコレクション管理を担当するサービス
/// Phase 4.1: InPlaceTranslationOverlayManagerからコレクション管理ロジックを抽出
/// </summary>
public interface IOverlayCollectionManager
{
    /// <summary>
    /// アクティブオーバーレイ数
    /// </summary>
    int ActiveOverlayCount { get; }

    /// <summary>
    /// オーバーレイを追加します
    /// </summary>
    /// <param name="chunkId">チャンクID</param>
    /// <param name="overlay">オーバーレイウィンドウ</param>
    /// <returns>追加に成功した場合true</returns>
    bool AddOverlay(int chunkId, InPlaceTranslationOverlayWindow overlay);

    /// <summary>
    /// オーバーレイを取得します
    /// </summary>
    /// <param name="chunkId">チャンクID</param>
    /// <param name="overlay">取得されたオーバーレイ</param>
    /// <returns>取得に成功した場合true</returns>
    bool TryGetOverlay(int chunkId, out InPlaceTranslationOverlayWindow? overlay);

    /// <summary>
    /// オーバーレイを削除します
    /// </summary>
    /// <param name="chunkId">チャンクID</param>
    /// <param name="overlay">削除されたオーバーレイ</param>
    /// <returns>削除に成功した場合true</returns>
    bool RemoveOverlay(int chunkId, out InPlaceTranslationOverlayWindow? overlay);

    /// <summary>
    /// すべてのオーバーレイを非表示にします
    /// </summary>
    Task HideAllOverlaysAsync();

    /// <summary>
    /// すべてのオーバーレイの可視性を切り替えます
    /// </summary>
    /// <param name="visible">可視性</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task SetAllOverlaysVisibilityAsync(bool visible, CancellationToken cancellationToken = default);

    /// <summary>
    /// 個別オーバーレイを非表示にします
    /// </summary>
    /// <param name="chunkId">チャンクID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task HideOverlayAsync(int chunkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 衝突回避用の既存オーバーレイ境界情報を取得します
    /// </summary>
    /// <returns>境界リスト</returns>
    List<Rectangle> GetExistingOverlayBounds();

    /// <summary>
    /// オーバーレイコレクションが空かどうか
    /// </summary>
    bool IsEmpty { get; }
}
