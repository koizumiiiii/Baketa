using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;

namespace Baketa.Core.Abstractions.UI;

/// <summary>
/// AR風翻訳オーバーレイ管理の抽象インターフェース
/// Google翻訳カメラのような、元テキストを翻訳テキストで置き換える表示システム
/// </summary>
public interface IARTranslationOverlayManager
{
    /// <summary>
    /// TextChunkのAR風オーバーレイを表示
    /// 既存のオーバーレイがある場合は更新、ない場合は新規作成
    /// </summary>
    /// <param name="textChunk">表示するテキストチャンク</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task ShowAROverlayAsync(TextChunk textChunk, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 指定されたチャンクのARオーバーレイを非表示
    /// </summary>
    /// <param name="chunkId">非表示にするチャンクID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task HideAROverlayAsync(int chunkId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// すべてのARオーバーレイを非表示
    /// </summary>
    Task HideAllAROverlaysAsync();
    
    /// <summary>
    /// ARオーバーレイをリセット（Stop時に呼び出し）
    /// </summary>
    Task ResetAsync();
    
    /// <summary>
    /// 現在アクティブなARオーバーレイの数を取得
    /// </summary>
    int ActiveOverlayCount { get; }
    
    /// <summary>
    /// ARオーバーレイマネージャーを初期化
    /// </summary>
    Task InitializeAsync();
}