using System.Text.RegularExpressions;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.License;
using Baketa.Core.Constants;
using Baketa.Core.License.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.License;

/// <summary>
/// ハイブリッドプロモーションコードサービス（DEBUG専用）
/// モックルールと本番ルールの両方を適用可能。
/// 1. モックコード（BAKETA-TEST*, BAKETA-EXPI*, BAKETA-USED*）はモック処理
/// 2. それ以外は本番サーバー（Supabase経由）で処理
/// </summary>
public sealed class HybridPromotionCodeService : IPromotionCodeService, IDisposable
{
    private readonly MockPromotionCodeService _mockService;
    private readonly PromotionCodeService _productionService;
    private readonly ILogger<HybridPromotionCodeService> _logger;
    private bool _disposed;

    /// <inheritdoc/>
    public event EventHandler<PromotionStateChangedEventArgs>? PromotionStateChanged;

    public HybridPromotionCodeService(
        MockPromotionCodeService mockService,
        PromotionCodeService productionService,
        ILogger<HybridPromotionCodeService> logger)
    {
        _mockService = mockService ?? throw new ArgumentNullException(nameof(mockService));
        _productionService = productionService ?? throw new ArgumentNullException(nameof(productionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 子サービスのイベントを転送
        _mockService.PromotionStateChanged += OnChildPromotionStateChanged;
        _productionService.PromotionStateChanged += OnChildPromotionStateChanged;

        _logger.LogDebug("HybridPromotionCodeService initialized (DEBUG mode)");
    }

    /// <inheritdoc/>
    public async Task<PromotionCodeResult> ApplyCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var normalizedCode = code.Trim().ToUpperInvariant();

        // 形式検証
        if (!ValidateCodeFormat(normalizedCode))
        {
            _logger.LogWarning("[Hybrid] Invalid promotion code format");
            return PromotionCodeResult.CreateFailure(
                PromotionErrorCode.InvalidFormat,
                "プロモーションコードの形式が正しくありません。");
        }

        // モックコードかどうかを判定
        if (IsMockCode(normalizedCode))
        {
            _logger.LogInformation("[Hybrid] Routing to MockPromotionCodeService: {Code}", MaskCode(normalizedCode));
            return await _mockService.ApplyCodeAsync(code, cancellationToken).ConfigureAwait(false);
        }

        // 本番コードとして処理
        _logger.LogInformation("[Hybrid] Routing to PromotionCodeService (production): {Code}", MaskCode(normalizedCode));
        return await _productionService.ApplyCodeAsync(code, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public PromotionInfo? GetCurrentPromotion()
    {
        // 本番サービスの状態を優先
        return _productionService.GetCurrentPromotion() ?? _mockService.GetCurrentPromotion();
    }

    /// <inheritdoc/>
    public bool ValidateCodeFormat(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        // BAKETA-XXXXXXXX 形式をチェック
        var normalized = code.Trim().ToUpperInvariant();
        return Regex.IsMatch(
            normalized,
            ValidationPatterns.PromotionCode,
            RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// モックコードかどうかを判定
    /// </summary>
    private static bool IsMockCode(string normalizedCode)
    {
        return normalizedCode.StartsWith("BAKETA-TEST", StringComparison.OrdinalIgnoreCase) ||
               normalizedCode.StartsWith("BAKETA-EXPI", StringComparison.OrdinalIgnoreCase) ||
               normalizedCode.StartsWith("BAKETA-USED", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// コードをマスク（ログ用）
    /// </summary>
    private static string MaskCode(string code)
    {
        if (code.Length <= 10) return "BAKETA-****";
        return code[..10] + "****";
    }

    private void OnChildPromotionStateChanged(object? sender, PromotionStateChangedEventArgs e)
    {
        // 子サービスからのイベントを転送
        PromotionStateChanged?.Invoke(this, e);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // イベントハンドラの解除
        _mockService.PromotionStateChanged -= OnChildPromotionStateChanged;
        _productionService.PromotionStateChanged -= OnChildPromotionStateChanged;

        _logger.LogDebug("HybridPromotionCodeService disposed");
    }
}
