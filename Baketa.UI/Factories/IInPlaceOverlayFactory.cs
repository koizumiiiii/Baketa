using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.UI.Views.Overlay;

namespace Baketa.UI.Factories;

/// <summary>
/// インプレースオーバーレイの作成と表示を担当するファクトリー
/// Phase 4.1: Factory Pattern適用によるInPlaceTranslationOverlayManager簡素化
/// </summary>
public interface IInPlaceOverlayFactory
{
    /// <summary>
    /// 新規インプレースオーバーレイを作成して表示します
    /// </summary>
    /// <param name="textChunk">翻訳結果を含むテキストチャンク</param>
    /// <param name="existingBounds">衝突回避のための既存オーバーレイ境界情報</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>作成されたオーバーレイウィンドウ</returns>
    /// <exception cref="InvalidOperationException">オーバーレイ作成または表示に失敗した場合</exception>
    Task<InPlaceTranslationOverlayWindow> CreateAndShowOverlayAsync(
        TextChunk textChunk,
        List<Rectangle> existingBounds,
        CancellationToken cancellationToken = default);
}
