using System.Diagnostics;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Validation;
using Baketa.Core.Models.Validation;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;

// 名前空間の曖昧性解決
using ITranslationServiceCore = Baketa.Core.Abstractions.Translation.ITranslationService;

namespace Baketa.Application.Services.Translation;

/// <summary>
/// 並列翻訳オーケストレーター実装
/// ローカル翻訳とCloud AI翻訳を並列実行し、相互検証で統合
/// </summary>
/// <remarks>
/// Issue #78 Phase 4: 並列翻訳オーケストレーション
///
/// 処理フロー:
/// 1. ローカル翻訳タスクとCloud AI翻訳タスクを並列起動
/// 2. Task.WhenAllで両方の完了を待機
/// 3. CrossValidatorで相互検証・統合
/// 4. 検証済み結果を返却
///
/// エラーハンドリング:
/// - Cloud AI失敗: ローカル翻訳結果のみ使用（LocalOnly）
/// - ローカル失敗: Cloud AI結果のみ使用（CloudOnly、座標情報なし）
/// - 両方失敗: エラー結果を返却
/// </remarks>
public sealed class ParallelTranslationOrchestrator : IParallelTranslationOrchestrator, IDisposable
{
    private bool _disposed;
    private readonly ITranslationServiceCore _translationService;
    private readonly IFallbackOrchestrator? _fallbackOrchestrator;
    private readonly ICrossValidator? _crossValidator;
    private readonly ILogger<ParallelTranslationOrchestrator> _logger;

    // 設定値
    private const int MaxParallelTranslations = 3;
    private static readonly TimeSpan CloudTranslationTimeout = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _parallelSemaphore = new(MaxParallelTranslations, MaxParallelTranslations);

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public ParallelTranslationOrchestrator(
        ITranslationServiceCore translationService,
        IFallbackOrchestrator? fallbackOrchestrator,
        ICrossValidator? crossValidator,
        ILogger<ParallelTranslationOrchestrator> logger)
    {
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _fallbackOrchestrator = fallbackOrchestrator;
        _crossValidator = crossValidator;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsCloudTranslationAvailable =>
        _fallbackOrchestrator?.GetCurrentStatus().PrimaryAvailable == true ||
        _fallbackOrchestrator?.GetCurrentStatus().SecondaryAvailable == true;

    /// <inheritdoc />
    public ParallelTranslationStatus GetStatus()
    {
        var fallbackStatus = _fallbackOrchestrator?.GetCurrentStatus();

        return new ParallelTranslationStatus
        {
            LocalEngineAvailable = true,
            CloudEngineAvailable = IsCloudTranslationAvailable,
            CrossValidationEnabled = _crossValidator != null,
            FallbackStatus = fallbackStatus
        };
    }

    /// <inheritdoc />
    public async Task<ParallelTranslationResult> TranslateAsync(
        ParallelTranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var overallStopwatch = Stopwatch.StartNew();

        _logger.LogDebug(
            "並列翻訳開始: RequestId={RequestId}, Chunks={ChunkCount}, UseCloud={UseCloud}",
            request.RequestId, request.OcrChunks.Count, request.UseCloudTranslation);

        try
        {
            await _parallelSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // Cloud AI翻訳を使用するかどうかで分岐
                if (request.UseCloudTranslation && _fallbackOrchestrator != null && !string.IsNullOrEmpty(request.SessionToken))
                {
                    return await ExecuteParallelTranslationAsync(request, overallStopwatch, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    return await ExecuteLocalOnlyTranslationAsync(request, overallStopwatch, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                _parallelSemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("並列翻訳がキャンセルされました: RequestId={RequestId}", request.RequestId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "並列翻訳で予期せぬエラー: RequestId={RequestId}", request.RequestId);

            return ParallelTranslationResult.Failure(
                new TranslationErrorDetail
                {
                    Code = TranslationErrorDetail.Codes.InternalError,
                    Message = ex.Message,
                    IsRetryable = false
                },
                new ParallelTranslationTiming
                {
                    TotalDuration = overallStopwatch.Elapsed
                });
        }
    }

    /// <summary>
    /// ローカル翻訳のみを実行（Free/Standardプラン）
    /// </summary>
    private async Task<ParallelTranslationResult> ExecuteLocalOnlyTranslationAsync(
        ParallelTranslationRequest request,
        Stopwatch overallStopwatch,
        CancellationToken cancellationToken)
    {
        var localStopwatch = Stopwatch.StartNew();

        try
        {
            var translatedChunks = await TranslateChunksLocallyAsync(
                request.OcrChunks,
                request.SourceLanguage,
                request.TargetLanguage,
                request.Context,
                cancellationToken).ConfigureAwait(false);

            localStopwatch.Stop();
            overallStopwatch.Stop();

            _logger.LogDebug(
                "ローカル翻訳完了: RequestId={RequestId}, Chunks={Count}, Duration={Duration}ms",
                request.RequestId, translatedChunks.Count, localStopwatch.ElapsedMilliseconds);

            return ParallelTranslationResult.LocalOnlySuccess(translatedChunks, localStopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ローカル翻訳失敗: RequestId={RequestId}", request.RequestId);

            return ParallelTranslationResult.Failure(
                new TranslationErrorDetail
                {
                    Code = TranslationErrorDetail.Codes.InternalError,
                    Message = $"ローカル翻訳エラー: {ex.Message}",
                    IsRetryable = true
                });
        }
    }

    /// <summary>
    /// 並列翻訳を実行（Pro/Premiaプラン）
    /// </summary>
    private async Task<ParallelTranslationResult> ExecuteParallelTranslationAsync(
        ParallelTranslationRequest request,
        Stopwatch overallStopwatch,
        CancellationToken cancellationToken)
    {
        var localStopwatch = new Stopwatch();
        var cloudStopwatch = new Stopwatch();
        var validationStopwatch = new Stopwatch();

        // ローカル翻訳タスク（I/Oバウンドなのでawaitせずに開始）
        async Task<(bool Success, IReadOnlyList<TextChunk>? Chunks, Exception? Error)> ExecuteLocalAsync()
        {
            localStopwatch.Start();
            try
            {
                var result = await TranslateChunksLocallyAsync(
                    request.OcrChunks,
                    request.SourceLanguage,
                    request.TargetLanguage,
                    request.Context,
                    cancellationToken).ConfigureAwait(false);
                return (Success: true, Chunks: result, Error: null);
            }
            catch (Exception ex)
            {
                return (Success: false, Chunks: null, Error: ex);
            }
            finally
            {
                localStopwatch.Stop();
            }
        }

        // Cloud AI翻訳タスク（I/Oバウンドなのでawaitせずに開始）
        async Task<(bool Success, FallbackTranslationResult? Result, Exception? Error)> ExecuteCloudAsync()
        {
            cloudStopwatch.Start();
            try
            {
                using var cloudCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cloudCts.CancelAfter(CloudTranslationTimeout);

                var imageRequest = new ImageTranslationRequest
                {
                    RequestId = request.RequestId,
                    ImageBase64 = request.ImageBase64,
                    MimeType = request.MimeType,
                    SourceLanguage = request.SourceLanguage,
                    TargetLanguage = request.TargetLanguage,
                    Width = request.ImageWidth,
                    Height = request.ImageHeight,
                    Context = request.Context,
                    SessionToken = request.SessionToken!
                };

                var result = await _fallbackOrchestrator!.TranslateWithFallbackAsync(imageRequest, cloudCts.Token)
                    .ConfigureAwait(false);

                return (Success: result.IsSuccess, Result: result, Error: null);
            }
            catch (Exception ex)
            {
                return (Success: false, Result: null, Error: ex);
            }
            finally
            {
                cloudStopwatch.Stop();
            }
        }

        // 並列タスクを開始（awaitせずに即座に開始し、両方完了を待機）
        var localTask = ExecuteLocalAsync();
        var cloudTask = ExecuteCloudAsync();

        // 両方の完了を待機
        await Task.WhenAll(localTask, cloudTask).ConfigureAwait(false);

        var localResult = await localTask.ConfigureAwait(false);
        var cloudResult = await cloudTask.ConfigureAwait(false);

        _logger.LogDebug(
            "並列翻訳完了: RequestId={RequestId}, LocalSuccess={LocalSuccess} ({LocalMs}ms), CloudSuccess={CloudSuccess} ({CloudMs}ms)",
            request.RequestId,
            localResult.Success, localStopwatch.ElapsedMilliseconds,
            cloudResult.Success, cloudStopwatch.ElapsedMilliseconds);

        // 結果の統合
        return await IntegrateResultsAsync(
            request,
            localResult,
            cloudResult,
            localStopwatch,
            cloudStopwatch,
            validationStopwatch,
            overallStopwatch,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// ローカル翻訳とCloud AI翻訳の結果を統合
    /// </summary>
    private async Task<ParallelTranslationResult> IntegrateResultsAsync(
        ParallelTranslationRequest request,
        (bool Success, IReadOnlyList<TextChunk>? Chunks, Exception? Error) localResult,
        (bool Success, FallbackTranslationResult? Result, Exception? Error) cloudResult,
        Stopwatch localStopwatch,
        Stopwatch cloudStopwatch,
        Stopwatch validationStopwatch,
        Stopwatch overallStopwatch,
        CancellationToken cancellationToken)
    {
        // ケース1: 両方成功 → 相互検証
        if (localResult.Success && cloudResult.Success && cloudResult.Result?.Response != null)
        {
            if (request.EnableCrossValidation && _crossValidator != null)
            {
                validationStopwatch.Start();

                try
                {
                    var validationResult = await _crossValidator.ValidateAsync(
                        localResult.Chunks!,
                        cloudResult.Result.Response,
                        cancellationToken).ConfigureAwait(false);

                    validationStopwatch.Stop();
                    overallStopwatch.Stop();

                    _logger.LogInformation(
                        "相互検証完了: RequestId={RequestId}, Validated={Count}, AcceptanceRate={Rate:P1}",
                        request.RequestId,
                        validationResult.ValidatedChunks.Count,
                        validationResult.Statistics.AcceptanceRate);

                    return ParallelTranslationResult.CrossValidatedSuccess(
                        validationResult.ValidatedChunks,
                        localResult.Chunks!,
                        cloudResult.Result.Response,
                        validationResult.Statistics,
                        new ParallelTranslationTiming
                        {
                            LocalTranslationDuration = localStopwatch.Elapsed,
                            CloudTranslationDuration = cloudStopwatch.Elapsed,
                            CrossValidationDuration = validationStopwatch.Elapsed,
                            TotalDuration = overallStopwatch.Elapsed
                        });
                }
                catch (Exception ex)
                {
                    validationStopwatch.Stop();
                    overallStopwatch.Stop();

                    _logger.LogWarning(ex, "相互検証エラー、ローカル結果にフォールバック: RequestId={RequestId}", request.RequestId);

                    // 相互検証失敗時は明確なステータスで返す
                    return ParallelTranslationResult.CrossValidationFailedSuccess(
                        localResult.Chunks!,
                        cloudResult.Result.Response,
                        new ParallelTranslationTiming
                        {
                            LocalTranslationDuration = localStopwatch.Elapsed,
                            CloudTranslationDuration = cloudStopwatch.Elapsed,
                            CrossValidationDuration = validationStopwatch.Elapsed,
                            TotalDuration = overallStopwatch.Elapsed
                        },
                        ex.Message);
                }
            }

            // 相互検証なし → ローカル結果を使用
            overallStopwatch.Stop();
            return ParallelTranslationResult.LocalOnlySuccess(localResult.Chunks!, localStopwatch.Elapsed);
        }

        // ケース2: ローカルのみ成功 → ローカル結果を使用
        if (localResult.Success)
        {
            overallStopwatch.Stop();

            if (cloudResult.Error != null)
            {
                _logger.LogWarning(
                    "Cloud AI翻訳失敗、ローカル結果を使用: RequestId={RequestId}, Error={Error}",
                    request.RequestId, cloudResult.Error.Message);
            }

            return ParallelTranslationResult.LocalOnlySuccess(localResult.Chunks!, localStopwatch.Elapsed);
        }

        // ケース3: Cloud AIのみ成功 → Cloud AI結果を使用（座標情報なし）
        if (cloudResult.Success && cloudResult.Result?.Response != null)
        {
            overallStopwatch.Stop();

            _logger.LogWarning(
                "ローカル翻訳失敗、Cloud AI結果を使用: RequestId={RequestId}, Error={Error}",
                request.RequestId, localResult.Error?.Message);

            return ParallelTranslationResult.CloudFallbackSuccess(
                cloudResult.Result.Response,
                cloudStopwatch.Elapsed);
        }

        // ケース4: 両方失敗 → エラー
        overallStopwatch.Stop();

        var errorMessage = $"ローカル: {localResult.Error?.Message ?? "不明"}, Cloud: {cloudResult.Error?.Message ?? cloudResult.Result?.FinalError?.Message ?? "不明"}";

        _logger.LogError(
            "並列翻訳完全失敗: RequestId={RequestId}, Errors={Errors}",
            request.RequestId, errorMessage);

        return ParallelTranslationResult.Failure(
            new TranslationErrorDetail
            {
                Code = TranslationErrorDetail.Codes.InternalError,
                Message = errorMessage,
                IsRetryable = true
            },
            new ParallelTranslationTiming
            {
                LocalTranslationDuration = localStopwatch.Elapsed,
                CloudTranslationDuration = cloudStopwatch.Elapsed,
                TotalDuration = overallStopwatch.Elapsed
            });
    }

    /// <summary>
    /// ローカル翻訳サービスでチャンクを翻訳
    /// </summary>
    private async Task<IReadOnlyList<TextChunk>> TranslateChunksLocallyAsync(
        IReadOnlyList<TextChunk> chunks,
        string sourceLanguage,
        string targetLanguage,
        string? context,
        CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
            return [];

        var sourceLang = Language.FromCode(sourceLanguage);
        var targetLang = Language.FromCode(targetLanguage);

        // バッチ翻訳を使用
        var textsToTranslate = chunks
            .Select(c => c.CombinedText)
            .ToList();

        var translations = await _translationService.TranslateBatchAsync(
            textsToTranslate,
            sourceLang,
            targetLang,
            context,
            cancellationToken).ConfigureAwait(false);

        // 翻訳結果をチャンクに適用（TranslatedTextを更新）
        for (int i = 0; i < chunks.Count; i++)
        {
            var translatedText = i < translations.Count && translations[i].IsSuccess
                ? translations[i].TranslatedText
                : chunks[i].CombinedText;

            // TextChunk.TranslatedTextはsetアクセサを持つため直接更新可能
            chunks[i].TranslatedText = translatedText;
        }

        return chunks;
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _parallelSemaphore.Dispose();
        _disposed = true;
    }
}
