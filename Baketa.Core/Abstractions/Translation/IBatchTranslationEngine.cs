using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// バッチ翻訳エンジンインターフェース
/// Issue #147 Phase 3.2: バッチ処理能力を持つ翻訳エンジンの抽象契約
/// Clean Architecture準拠: Core層でバッチ処理能力を定義
/// </summary>
public interface IBatchTranslationEngine : ITranslationEngine
{
    /// <summary>
    /// 複数のテキストをバッチ翻訳します
    /// </summary>
    /// <param name="requests">翻訳リクエストのコレクション</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>翻訳レスポンスのコレクション</returns>
    Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequest> requests,
        CancellationToken cancellationToken = default);
}