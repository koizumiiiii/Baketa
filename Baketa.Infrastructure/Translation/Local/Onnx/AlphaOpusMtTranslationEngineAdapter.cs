using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Local.Onnx;

/// <summary>
/// AlphaOpusMtTranslationEngineを旧インターフェースに適応させるアダプター
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="adaptee">適応対象のエンジン</param>
/// <param name="logger">ロガー</param>
public class AlphaOpusMtTranslationEngineAdapter(
    AlphaOpusMtTranslationEngine adaptee,
    ILogger<AlphaOpusMtTranslationEngineAdapter> logger) : ITranslationEngine
{
    private readonly AlphaOpusMtTranslationEngine _adaptee = adaptee ?? throw new ArgumentNullException(nameof(adaptee));
    private readonly ILogger<AlphaOpusMtTranslationEngineAdapter> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public string Name => _adaptee.Name;

    /// <inheritdoc/>
    public string Description => _adaptee.Description;

    /// <inheritdoc/>
    public bool RequiresNetwork => _adaptee.RequiresNetwork;

    /// <inheritdoc/>
    public async Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync()
    {
        var pairs = await _adaptee.GetSupportedLanguagePairsAsync().ConfigureAwait(false);
        return pairs;
    }

    /// <inheritdoc/>
    public async Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair)
    {
        return await _adaptee.SupportsLanguagePairAsync(languagePair).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<TranslationResponse> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        return await _adaptee.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequest> requests,
        CancellationToken cancellationToken = default)
    {
        return await _adaptee.TranslateBatchAsync(requests, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> IsReadyAsync()
    {
        return await _adaptee.IsReadyAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> InitializeAsync()
    {
        return await _adaptee.InitializeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _adaptee?.Dispose();
        GC.SuppressFinalize(this);
    }
}
