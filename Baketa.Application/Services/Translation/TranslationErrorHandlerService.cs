using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Factories;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Exceptions;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;
using Language = Baketa.Core.Models.Translation.Language;
using TranslationMode = Baketa.Core.Abstractions.Services.TranslationMode;

namespace Baketa.Application.Services.Translation;

/// <summary>
/// 翻訳エラーハンドリング統一サービス
/// フォールバック戦略、リトライ機構、エンジン健全性管理を提供
/// </summary>
public class TranslationErrorHandlerService(
    ITranslationService translationService,
    ILogger<TranslationErrorHandlerService> logger) : ITranslationErrorHandlerService
{
    private readonly ITranslationService _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
    private readonly ILogger<TranslationErrorHandlerService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // リトライ設定
    private const int MaxRetryCount = 2;
    private const int RetryDelayMs = 1000;

    /// <summary>
    /// フォールバック戦略付き翻訳実行
    /// </summary>
    /// <param name="sourceText">翻訳対象テキスト</param>
    /// <param name="sourceLanguage">元言語</param>
    /// <param name="targetLanguage">翻訳先言語</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>翻訳結果</returns>
    public async Task<TranslationResult> TranslateWithFallbackAsync(
        string sourceText,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            _logger.LogWarning("🚨 [ERROR_HANDLER] 空のテキストが翻訳要求されました");
            return CreateFallbackResult(sourceText, "翻訳対象テキストが空です");
        }

        _logger.LogDebug("🔄 [ERROR_HANDLER] フォールバック戦略付き翻訳開始: '{SourceText}' ({SourceLang} -> {TargetLang})",
            sourceText.Length > 50 ? sourceText[..50] : sourceText, sourceLanguage, targetLanguage);

        Exception? lastException = null;

        // 🎯 Phase 2タスク3: リトライ機構付きフォールバック戦略
        for (int retry = 0; retry <= MaxRetryCount; retry++)
        {
            if (cancellationToken.IsCancellationRequested)
                cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (retry > 0)
                {
                    _logger.LogDebug("🔄 [ERROR_HANDLER] リトライ実行: 試行回数={RetryCount}", retry + 1);
                    await Task.Delay(RetryDelayMs * retry, cancellationToken).ConfigureAwait(false);
                }

                _logger.LogDebug("🚀 [ERROR_HANDLER] 翻訳サービス呼び出し開始: '{SourceText}'",
                    sourceText.Length > 20 ? sourceText[..20] : sourceText);
                Console.WriteLine($"🚀 [ERROR_HANDLER] 翻訳サービス呼び出し開始: '{sourceText[..Math.Min(20, sourceText.Length)]}'");

                // 既存のITranslationServiceを使用してシンプルに翻訳実行
                var translationResult = await _translationService.TranslateAsync(
                    sourceText,
                    new Language(sourceLanguage, sourceLanguage),
                    new Language(targetLanguage, targetLanguage),
                    null, // context
                    cancellationToken).ConfigureAwait(false);

                _logger.LogDebug("📝 [ERROR_HANDLER] 翻訳サービス応答受信: IsSuccess={IsSuccess}, Text='{Text}'",
                    translationResult?.IsSuccess, translationResult?.TranslatedText?[..Math.Min(20, translationResult?.TranslatedText?.Length ?? 0)]);
                Console.WriteLine($"📝 [ERROR_HANDLER] 翻訳サービス応答受信: IsSuccess={translationResult?.IsSuccess}, Text='{translationResult?.TranslatedText?[..Math.Min(20, translationResult?.TranslatedText?.Length ?? 0)]}'");

                if (IsValidTranslationResult(translationResult))
                {
                    _logger.LogInformation("✅ [ERROR_HANDLER] 翻訳成功: 結果='{TranslatedText}' (試行回数: {RetryCount})",
                        translationResult.TranslatedText?.Length > 50 ? translationResult.TranslatedText[..50] : translationResult.TranslatedText,
                        retry + 1);

                    // Core.Translation.ModelsのTranslationResultをApplication用のTranslationResultに変換
                    return ConvertToApplicationTranslationResult(translationResult, sourceText, targetLanguage);
                }
                else
                {
                    _logger.LogWarning("⚠️ [ERROR_HANDLER] 無効な翻訳結果: IsSuccess={IsSuccess}, Text='{Text}'",
                        translationResult?.IsSuccess, translationResult?.TranslatedText);
                    Console.WriteLine($"⚠️ [ERROR_HANDLER] 無効な翻訳結果: IsSuccess={translationResult?.IsSuccess}, Text='{translationResult?.TranslatedText}'");
                    throw new TranslationEngineException($"無効な翻訳結果が返されました: IsSuccess={translationResult?.IsSuccess}");
                }
            }
            catch (OperationCanceledException)
            {
                throw; // キャンセレーションは再スロー
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "⚠️ [ERROR_HANDLER] 翻訳リトライエラー: 試行={RetryAttempt}, エラー型={ExceptionType}, メッセージ={Message}",
                    retry + 1, ex.GetType().Name, ex.Message);
                Console.WriteLine($"⚠️ [ERROR_HANDLER] 翻訳リトライエラー: 試行={retry + 1}, エラー型={ex.GetType().Name}, メッセージ={ex.Message}");

                if (retry == MaxRetryCount)
                {
                    break; // 最後のリトライでも失敗
                }
            }
        }

        // すべてのリトライで失敗した場合のフォールバック
        _logger.LogError(lastException, "💥 [ERROR_HANDLER] すべてのリトライで失敗しました");
        return CreateFallbackResult(sourceText, $"翻訳失敗: {lastException?.Message ?? "不明なエラー"}");
    }

    /// <summary>
    /// Core.Translation.ModelsのTranslationResponseをApplication用のTranslationResultに変換
    /// </summary>
    private static TranslationResult ConvertToApplicationTranslationResult(
        Core.Translation.Models.TranslationResponse coreResponse,
        string sourceText,
        string targetLanguage)
    {
        return new TranslationResult
        {
            Id = Guid.NewGuid().ToString(),
            Mode = TranslationMode.Singleshot,
            OriginalText = sourceText,
            TranslatedText = coreResponse.TranslatedText ?? $"[翻訳エラー: 結果がnull]",
            TargetLanguage = targetLanguage,
            DetectedLanguage = coreResponse.SourceLanguage.Code,
            Confidence = coreResponse.ConfidenceScore,
            ProcessingTime = TimeSpan.FromMilliseconds(coreResponse.ProcessingTimeMs),
            IsCoordinateBasedMode = false
        };
    }

    /// <summary>
    /// 翻訳結果の妥当性チェック（Core.Translation.Models用）
    /// </summary>
    private static bool IsValidTranslationResult(Core.Translation.Models.TranslationResponse? result)
    {
        return result != null &&
               result.IsSuccess &&
               !string.IsNullOrWhiteSpace(result.TranslatedText) &&
               result.TranslatedText != result.SourceText; // 翻訳されていることを確認
    }

    /// <summary>
    /// フォールバック用の結果を作成
    /// </summary>
    private static TranslationResult CreateFallbackResult(string originalText, string errorMessage)
    {
        return new TranslationResult
        {
            Id = Guid.NewGuid().ToString(),
            Mode = TranslationMode.Singleshot,
            OriginalText = originalText,
            TranslatedText = $"[翻訳エラー: {errorMessage}]",
            TargetLanguage = "ja",
            DetectedLanguage = "unknown",
            Confidence = 0.0f,
            ProcessingTime = TimeSpan.Zero,
            IsCoordinateBasedMode = false
        };
    }

}

/// <summary>
/// 翻訳エラーハンドリングサービスのインターフェース
/// </summary>
public interface ITranslationErrorHandlerService
{
    /// <summary>
    /// フォールバック戦略付き翻訳実行
    /// </summary>
    Task<TranslationResult> TranslateWithFallbackAsync(
        string sourceText,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default);
}
