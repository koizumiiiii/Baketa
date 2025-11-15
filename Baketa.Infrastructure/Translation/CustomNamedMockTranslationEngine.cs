using System;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation;

/// <summary>
/// カスタム名を持つモック翻訳エンジン
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="logger">ロガー</param>
/// <param name="customName">カスタムエンジン名</param>
/// <param name="simulatedDelayMs">シミュレートする処理遅延（ミリ秒）</param>
/// <param name="simulatedErrorRate">シミュレートするエラー率（0.0～1.0）</param>
public sealed class CustomNamedMockTranslationEngine(
        ILogger<MockTranslationEngine> logger,
        string customName,
        int simulatedDelayMs = 0,
        float simulatedErrorRate = 0.0f) : MockTranslationEngine(logger, simulatedDelayMs, simulatedErrorRate)
{
    private readonly string _customName = customName ?? throw new ArgumentNullException(nameof(customName));

    /// <summary>
    /// カスタムエンジン名
    /// </summary>
    public override string Name => _customName;
}
