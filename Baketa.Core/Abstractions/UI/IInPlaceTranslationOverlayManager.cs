using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;

namespace Baketa.Core.Abstractions.UI;

/// <summary>
/// インプレース翻訳オーバーレイ管理の抽象インターフェース
/// Google翻訳カメラのような、元テキストを翻訳テキストで置き換える表示システム
/// </summary>
public interface IInPlaceTranslationOverlayManager
{
    /// <summary>
    /// TextChunkのインプレースオーバーレイを表示
    /// 既存のオーバーレイがある場合は更新、ない場合は新規作成
    /// </summary>
    /// <param name="textChunk">表示するテキストチャンク</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task ShowInPlaceOverlayAsync(TextChunk textChunk, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 指定されたチャンクのインプレースオーバーレイを非表示
    /// </summary>
    /// <param name="chunkId">非表示にするチャンクID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task HideInPlaceOverlayAsync(int chunkId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 指定領域内のオーバーレイを非表示にする
    /// UltraThink Phase 1: オーバーレイ自動削除システム対応
    /// </summary>
    /// <param name="area">対象領域</param>
    /// <param name="excludeChunkId">除外するChunkID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task HideOverlaysInAreaAsync(Rectangle area, int excludeChunkId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// すべてのインプレースオーバーレイを非表示
    /// </summary>
    Task HideAllInPlaceOverlaysAsync();
    
    /// <summary>
    /// すべてのインプレースオーバーレイの可視性を切り替え（高速化版）
    /// オーバーレイの削除/再作成ではなく、可視性プロパティのみを変更
    /// </summary>
    /// <param name="visible">表示する場合はtrue、非表示にする場合はfalse</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task SetAllOverlaysVisibilityAsync(bool visible, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// インプレースオーバーレイをリセット（Stop時に呼び出し）
    /// </summary>
    Task ResetAsync();
    
    /// <summary>
    /// 現在アクティブなインプレースオーバーレイの数を取得
    /// </summary>
    int ActiveOverlayCount { get; }
    
    /// <summary>
    /// インプレースオーバーレイマネージャーを初期化
    /// </summary>
    Task InitializeAsync();
}