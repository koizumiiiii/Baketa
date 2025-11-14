using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// バッチ翻訳エンジンインターフェース
/// Issue #147 Phase 3.2: バッチ処理能力を持つ翻訳エンジンの抽象契約
/// Clean Architecture準拠: Core層でバッチ処理能力を定義
/// 注意: TranslateBatchAsyncメソッドはITranslationEngineで既に定義済み
/// </summary>
public interface IBatchTranslationEngine : ITranslationEngine
{
    // ITranslationEngineのTranslateBatchAsyncメソッドを使用
    // 追加のバッチ処理専用メソッドが必要な場合はここに定義
}
