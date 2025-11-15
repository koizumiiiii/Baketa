using System;
using System.Collections.Generic;

namespace Baketa.Core.Translation.Models;

/// <summary>
/// 翻訳レスポンスを表すクラス
/// </summary>
public class TranslationResponse
{
    /// <summary>
    /// 対応するリクエストのID
    /// </summary>
    public required Guid RequestId { get; set; }

    /// <summary>
    /// 翻訳元テキスト
    /// </summary>
    public required string SourceText { get; set; }

    /// <summary>
    /// 翻訳結果テキスト
    /// </summary>
    public string? TranslatedText { get; set; }

    /// <summary>
    /// 翻訳元言語
    /// </summary>
    public required Language SourceLanguage { get; set; }

    /// <summary>
    /// 翻訳先言語
    /// </summary>
    public required Language TargetLanguage { get; set; }

    /// <summary>
    /// 使用された翻訳エンジン名
    /// </summary>
    public required string EngineName { get; set; }

    /// <summary>
    /// 翻訳の信頼度スコア
    /// </summary>
    public float ConfidenceScore { get; set; } = -1.0f;

    /// <summary>
    /// 翻訳処理時間（ミリ秒）
    /// </summary>
    public long ProcessingTimeMs { get; set; }

    /// <summary>
    /// 翻訳が成功したかどうか
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// エラー情報
    /// </summary>
    public TranslationError? Error { get; set; }

    /// <summary>
    /// メタデータ
    /// </summary>
    public Dictionary<string, object?> Metadata { get; } = [];

    /// <summary>
    /// レスポンスのタイムスタンプ
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// デフォルトコンストラクタ
    /// </summary>
    public TranslationResponse()
    {
    }

    /// <summary>
    /// リクエストから初期化
    /// </summary>
    /// <param name="request">元となる翻訳リクエスト</param>
    /// <param name="engineName">翻訳エンジン名</param>
    public TranslationResponse(TranslationRequest request, string engineName)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(engineName);
        RequestId = request.RequestId;
        SourceText = request.SourceText;
        SourceLanguage = request.SourceLanguage;
        TargetLanguage = request.TargetLanguage;
        EngineName = engineName;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// 成功レスポンスを作成
    /// </summary>
    /// <param name="request">元となる翻訳リクエスト</param>
    /// <param name="translatedText">翻訳結果テキスト</param>
    /// <param name="engineName">翻訳エンジン名</param>
    /// <param name="processingTimeMs">処理時間（ミリ秒）</param>
    /// <returns>成功レスポンス</returns>
    public static TranslationResponse CreateSuccess(
        TranslationRequest request,
        string translatedText,
        string engineName,
        long processingTimeMs)
    {
        ArgumentNullException.ThrowIfNull(request);
        // null参照チェックを緩和し、nullの場合は空文字列として扱う
        // ArgumentNullException.ThrowIfNull(translatedText);
        var safeTranslatedText = translatedText ?? string.Empty;
        ArgumentNullException.ThrowIfNull(engineName);
        return new TranslationResponse
        {
            RequestId = request.RequestId,
            SourceText = request.SourceText,
            TranslatedText = safeTranslatedText,
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            EngineName = engineName,
            ProcessingTimeMs = processingTimeMs,
            IsSuccess = true,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 信頼度スコア付きの成功レスポンスを作成します
    /// </summary>
    /// <param name="request">元のリクエスト</param>
    /// <param name="translatedText">翻訳結果テキスト</param>
    /// <param name="engineName">使用されたエンジン名</param>
    /// <param name="processingTimeMs">処理時間（ミリ秒）</param>
    /// <param name="confidenceScore">信頼度スコア（0.0～1.0）</param>
    /// <returns>成功レスポンスインスタンス</returns>
    public static TranslationResponse CreateSuccessWithConfidence(
        TranslationRequest request,
        string translatedText,
        string engineName,
        long processingTimeMs,
        float confidenceScore)
    {
        ArgumentNullException.ThrowIfNull(request);
        var safeTranslatedText = translatedText ?? string.Empty;
        ArgumentNullException.ThrowIfNull(engineName);

        var response = CreateSuccess(request, safeTranslatedText, engineName, processingTimeMs);
        response.ConfidenceScore = confidenceScore;
        return response;
    }

    /// <summary>
    /// エラーレスポンスを作成
    /// </summary>
    /// <param name="request">元となる翻訳リクエスト</param>
    /// <param name="error">エラー情報</param>
    /// <param name="engineName">翻訳エンジン名</param>
    /// <returns>エラーレスポンス</returns>
    public static TranslationResponse CreateError(
        TranslationRequest request,
        TranslationError error,
        string engineName)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(engineName);
        return new TranslationResponse
        {
            RequestId = request.RequestId,
            SourceText = request.SourceText,
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            EngineName = engineName,
            Error = error,
            IsSuccess = false,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 例外からエラーレスポンスを作成します
    /// </summary>
    /// <param name="request">元のリクエスト</param>
    /// <param name="engineName">使用されたエンジン名</param>
    /// <param name="errorCode">エラーコード</param>
    /// <param name="errorMessage">エラーメッセージ</param>
    /// <param name="exception">例外</param>
    /// <param name="processingTimeMs">処理時間（ミリ秒）</param>
    /// <returns>エラーレスポンスインスタンス</returns>
    public static TranslationResponse CreateErrorFromException(
        TranslationRequest request,
        string engineName,
        string errorCode,
        string errorMessage,
        Exception exception,
        long processingTimeMs = 0)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(engineName);
        ArgumentNullException.ThrowIfNull(errorCode);
        ArgumentNullException.ThrowIfNull(errorMessage);
        ArgumentNullException.ThrowIfNull(exception);

        var error = TranslationError.FromException(errorCode, errorMessage, exception);
        var response = CreateError(request, error, engineName);

        // 処理時間を設定
        response.ProcessingTimeMs = processingTimeMs;

        return response;
    }

    /// <summary>
    /// クローンを作成
    /// </summary>
    /// <returns>このレスポンスのクローン</returns>
    public TranslationResponse Clone()
    {
        var clone = new TranslationResponse
        {
            RequestId = this.RequestId,
            SourceText = this.SourceText,
            TranslatedText = this.TranslatedText,
            SourceLanguage = this.SourceLanguage,
            TargetLanguage = this.TargetLanguage,
            EngineName = this.EngineName,
            ConfidenceScore = this.ConfidenceScore,
            ProcessingTimeMs = this.ProcessingTimeMs,
            IsSuccess = this.IsSuccess,
            Error = this.Error?.Clone(),
            Timestamp = this.Timestamp
        };

        foreach (var item in Metadata)
        {
            clone.Metadata[item.Key] = item.Value;
        }

        return clone;
    }
}
