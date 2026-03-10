using Baketa.Core.Abstractions.License;
using Baketa.Core.Translation.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// Cloud AI翻訳のフォールバック制御を実装
/// Primary → Secondary → Local の3段階フォールバック
/// </summary>
public sealed class FallbackOrchestrator : IFallbackOrchestrator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEngineStatusManager _engineStatusManager;
    private readonly ILicenseManager _licenseManager;
    private readonly ILogger<FallbackOrchestrator> _logger;

    /// <summary>
    /// Primary翻訳エンジンのキー
    /// </summary>
    private const string PrimaryKey = "primary";

    /// <summary>
    /// Secondary翻訳エンジンのキー
    /// </summary>
    private const string SecondaryKey = "secondary";

    /// <summary>
    /// フォールバック無効化期間（エンジン失敗後）
    /// </summary>
    private static readonly TimeSpan FallbackCooldown = TimeSpan.FromMinutes(5);

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public FallbackOrchestrator(
        IServiceProvider serviceProvider,
        IEngineStatusManager engineStatusManager,
        ILicenseManager licenseManager,
        ILogger<FallbackOrchestrator> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _engineStatusManager = engineStatusManager ?? throw new ArgumentNullException(nameof(engineStatusManager));
        _licenseManager = licenseManager ?? throw new ArgumentNullException(nameof(licenseManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<FallbackTranslationResult> TranslateWithFallbackAsync(
        ImageTranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var attempts = new List<FallbackAttempt>();
        var startTime = DateTime.UtcNow;

        _logger.LogDebug(
            "フォールバック翻訳開始: RequestId={RequestId}, Target={Target}",
            request.RequestId,
            request.TargetLanguage);

        // Step 1: Primary (Gemini) を試行
        if (_engineStatusManager.IsEngineAvailable(PrimaryKey))
        {
            var primaryResult = await TryTranslateAsync(
                PrimaryKey,
                FallbackLevel.Primary,
                request,
                attempts,
                cancellationToken).ConfigureAwait(false);

            if (primaryResult != null)
            {
                return primaryResult;
            }
        }
        else
        {
            var nextRetry = _engineStatusManager.GetNextRetryTime(PrimaryKey);
            _logger.LogDebug(
                "Primary翻訳エンジンは利用不可: NextRetry={NextRetry}",
                nextRetry?.ToString("HH:mm:ss") ?? "unknown");
        }

        // Step 2: Secondary (GPT-4 Vision) を試行
        if (_engineStatusManager.IsEngineAvailable(SecondaryKey))
        {
            var secondaryResult = await TryTranslateAsync(
                SecondaryKey,
                FallbackLevel.Secondary,
                request,
                attempts,
                cancellationToken).ConfigureAwait(false);

            if (secondaryResult != null)
            {
                return secondaryResult;
            }
        }
        else
        {
            var nextRetry = _engineStatusManager.GetNextRetryTime(SecondaryKey);
            _logger.LogDebug(
                "Secondary翻訳エンジンは利用不可: NextRetry={NextRetry}",
                nextRetry?.ToString("HH:mm:ss") ?? "unknown");
        }

        // [Issue #519] Step 3: Local (NLLB-200) へフォールバック
        // ローカルNLLBはテキスト入力（OCR結果必要）のため、FallbackOrchestrator内では
        // 画像翻訳として実行できない。呼び出し元（AggregatedChunksReadyEventHandler）が
        // OCR結果を使ってローカル翻訳を実行する。
        _logger.LogWarning(
            "[Issue #519] すべてのCloud AI翻訳エンジンが失敗、ローカルNLLBフォールバックを推奨: RequestId={RequestId}",
            request.RequestId);

        attempts.Add(new FallbackAttempt
        {
            Level = FallbackLevel.Local,
            ProviderId = "local-nllb",
            Success = false,
            ErrorCode = TranslationErrorDetail.Codes.CloudAllFailed,
            Duration = DateTime.UtcNow - startTime
        });

        return FallbackTranslationResult.LocalFallbackRequired(attempts);
    }

    /// <inheritdoc />
    public FallbackStatus GetCurrentStatus()
    {
        var primaryStatus = _engineStatusManager.GetStatus(PrimaryKey);
        var secondaryStatus = _engineStatusManager.GetStatus(SecondaryKey);

        return new FallbackStatus
        {
            PrimaryAvailable = primaryStatus.IsAvailable,
            SecondaryAvailable = secondaryStatus.IsAvailable,
            LocalAvailable = true, // ローカルは常に利用可能（Phase 2では未実装）
            PrimaryNextRetry = primaryStatus.NextRetryTime,
            SecondaryNextRetry = secondaryStatus.NextRetryTime
        };
    }

    /// <summary>
    /// 指定されたエンジンで翻訳を試行
    /// </summary>
    private async Task<FallbackTranslationResult?> TryTranslateAsync(
        string engineKey,
        FallbackLevel level,
        ImageTranslationRequest request,
        List<FallbackAttempt> attempts,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            var translator = _serviceProvider.GetKeyedService<ICloudImageTranslator>(engineKey);

            if (translator == null)
            {
                _logger.LogWarning("翻訳エンジンが見つかりません: {EngineKey}", engineKey);

                attempts.Add(new FallbackAttempt
                {
                    Level = level,
                    ProviderId = engineKey,
                    Success = false,
                    ErrorCode = TranslationErrorDetail.Codes.InternalError,
                    Duration = DateTime.UtcNow - startTime
                });

                return null;
            }

            // 可用性チェック
            var isAvailable = await translator.IsAvailableAsync(cancellationToken).ConfigureAwait(false);
            if (!isAvailable)
            {
                _logger.LogDebug("{Level}翻訳エンジンは利用不可", level);

                attempts.Add(new FallbackAttempt
                {
                    Level = level,
                    ProviderId = translator.ProviderId,
                    Success = false,
                    ErrorCode = TranslationErrorDetail.Codes.ApiError,
                    Duration = DateTime.UtcNow - startTime
                });

                _engineStatusManager.MarkEngineUnavailable(
                    engineKey,
                    FallbackCooldown,
                    "可用性チェック失敗");

                return null;
            }

            // 翻訳実行
            var response = await translator.TranslateImageAsync(request, cancellationToken).ConfigureAwait(false);
            var duration = DateTime.UtcNow - startTime;

            if (response.IsSuccess)
            {
                // [Issue #518] ハルシネーション検出: 成功レスポンスでも品質を検証
                var hallucinationReason = DetectHallucination(response);
                if (hallucinationReason != null)
                {
                    _logger.LogWarning(
                        "[Issue #518] {Level}翻訳ハルシネーション検出: RequestId={RequestId}, Reason={Reason} - Secondaryへフォールバック",
                        level, request.RequestId, hallucinationReason);

                    attempts.Add(new FallbackAttempt
                    {
                        Level = level,
                        ProviderId = translator.ProviderId,
                        Success = false,
                        ErrorCode = TranslationErrorDetail.Codes.HallucinationDetected,
                        Duration = duration
                    });

                    _engineStatusManager.MarkEngineUnavailable(
                        engineKey,
                        FallbackCooldown,
                        $"Hallucination: {hallucinationReason}");

                    return null; // → 次のエンジンへフォールバック
                }

                _logger.LogInformation(
                    "{Level}翻訳成功: RequestId={RequestId}, Duration={Duration}ms",
                    level,
                    request.RequestId,
                    duration.TotalMilliseconds);

                attempts.Add(new FallbackAttempt
                {
                    Level = level,
                    ProviderId = translator.ProviderId,
                    Success = true,
                    Duration = duration
                });

                _engineStatusManager.MarkEngineAvailable(engineKey);

                // [Issue #258] トークン消費をLicenseManagerに記録
                // これによりUIにトークン使用量が反映される
                if (response.TokenUsage?.TotalTokens > 0)
                {
                    try
                    {
                        var consumeResult = await _licenseManager.ConsumeCloudAiTokensAsync(
                            response.TokenUsage.TotalTokens,
                            request.RequestId,
                            cancellationToken).ConfigureAwait(false);

                        if (!consumeResult.Success)
                        {
                            // [Issue #293] SessionInvalidは未ログイン時の期待動作のためDebugレベルに
                            // 翻訳自体は成功しているため、警告は不要
                            _logger.LogDebug(
                                "[Issue #258] トークン消費記録に失敗: RequestId={RequestId}, Reason={Reason}",
                                request.RequestId,
                                consumeResult.FailureReason);
                        }
                        else
                        {
                            _logger.LogDebug(
                                "[Issue #258] トークン消費記録完了: RequestId={RequestId}, Tokens={Tokens}, NewTotal={NewTotal}",
                                request.RequestId,
                                response.TokenUsage.TotalTokens,
                                consumeResult.NewUsageTotal);
                        }
                    }
                    catch (Exception ex)
                    {
                        // トークン消費記録の失敗は翻訳結果には影響しない
                        _logger.LogWarning(ex,
                            "[Issue #258] トークン消費記録でエラー（継続）: RequestId={RequestId}",
                            request.RequestId);
                    }
                }

                return FallbackTranslationResult.Success(response, level, attempts);
            }

            // 翻訳失敗
            _logger.LogWarning(
                "{Level}翻訳失敗: RequestId={RequestId}, Error={Error}",
                level,
                request.RequestId,
                response.Error?.Message);

            attempts.Add(new FallbackAttempt
            {
                Level = level,
                ProviderId = translator.ProviderId,
                Success = false,
                ErrorCode = response.Error?.Code,
                Duration = duration
            });

            // リトライ不可エラーの場合はエンジンを一時的に無効化
            if (response.Error?.IsRetryable == false)
            {
                _engineStatusManager.MarkEngineUnavailable(
                    engineKey,
                    FallbackCooldown,
                    response.Error.Message);
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("{Level}翻訳がキャンセルされました", level);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Level}翻訳中に予期せぬエラー", level);

            attempts.Add(new FallbackAttempt
            {
                Level = level,
                ProviderId = engineKey,
                Success = false,
                ErrorCode = TranslationErrorDetail.Codes.InternalError,
                Duration = DateTime.UtcNow - startTime
            });

            _engineStatusManager.MarkEngineUnavailable(
                engineKey,
                FallbackCooldown,
                ex.Message);

            return null;
        }
    }

    /// <summary>
    /// [Issue #518] Cloud AI翻訳レスポンスのハルシネーション検出
    /// 成功レスポンスでも品質に問題がある場合を検出し、Secondaryへのフォールバックを促す
    /// </summary>
    /// <returns>ハルシネーション理由（正常なら null）</returns>
    private static string? DetectHallucination(ImageTranslationResponse response)
    {
        // 複数テキスト結果の検証
        if (response.Texts is { Count: > 0 } texts)
        {
            // ルール1: 結果件数が異常に多い（AggregatedChunksReadyEventHandlerの既存ルール前倒し）
            const int maxReasonableResults = 20;
            if (texts.Count > maxReasonableResults)
            {
                return $"結果件数が異常: {texts.Count}件 (閾値: {maxReasonableResults})";
            }

            // ルール2: 全結果の翻訳テキストが同一（明らかな異常）
            if (texts.Count >= 3)
            {
                var distinctTranslations = texts
                    .Select(t => t.Translation?.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .Distinct(StringComparer.Ordinal)
                    .Count();

                if (distinctTranslations == 1)
                {
                    return $"全{texts.Count}件の翻訳結果が同一テキスト";
                }
            }

            // ルール3: 翻訳テキストの繰り返しパターン検出
            foreach (var text in texts)
            {
                if (IsRepetitiveText(text.Translation))
                {
                    return $"繰り返しパターン検出: '{text.Translation?[..Math.Min(50, text.Translation?.Length ?? 0)]}...'";
                }
            }
        }

        // 単一テキスト結果の検証
        if (IsRepetitiveText(response.TranslatedText))
        {
            return $"繰り返しパターン検出: '{response.TranslatedText?[..Math.Min(50, response.TranslatedText?.Length ?? 0)]}...'";
        }

        return null;
    }

    /// <summary>
    /// [Issue #518] テキストの繰り返しパターンを検出
    /// 例: "candycandycandy", "THE PARTY OF THE PARTY OF THE PARTY OF"
    /// </summary>
    private static bool IsRepetitiveText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 20)
            return false;

        const int minPhraseLength = 4;
        const int minRepetitionCount = 3;

        for (int phraseLen = minPhraseLength; phraseLen <= text.Length / minRepetitionCount; phraseLen++)
        {
            var phrase = text[..phraseLen];
            if (string.IsNullOrWhiteSpace(phrase))
                continue;

            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(phrase, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                index += phrase.Length;
            }

            if (count >= minRepetitionCount)
            {
                var repetitionRatio = (double)phrase.Length * count / text.Length;
                if (repetitionRatio >= 0.5)
                    return true;
            }
        }

        return false;
    }
}
