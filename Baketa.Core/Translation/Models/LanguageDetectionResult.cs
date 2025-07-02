using System;

namespace Baketa.Core.Translation.Models;

/// <summary>
/// 言語検出結果
/// </summary>
public class LanguageDetectionResult
{
    /// <summary>
    /// 検出された言語
    /// </summary>
    public required Language DetectedLanguage { get; set; }
    
    /// <summary>
    /// 検出の信頼度スコア
    /// </summary>
    public double ConfidenceScore { get; set; }
    
    /// <summary>
    /// 検出時の信頼度（0～1）
    /// </summary>
    public double Confidence 
    { 
        get => ConfidenceScore;
        set => ConfidenceScore = value; 
    }
    
    /// <summary>
    /// 検出結果が信頼できるかどうか
    /// </summary>
    public bool IsReliable { get; set; }
    
    /// <summary>
    /// 検出が成功したかどうか
    /// </summary>
    public bool IsSuccessful { get; set; } = true;
    
    /// <summary>
    /// エラー情報（検出失敗時）
    /// </summary>
    public TranslationError? Error { get; set; }
    
    /// <summary>
    /// 翻訳エンジン名
    /// </summary>
    public string? EngineName { get; set; }
    
    /// <summary>
    /// デフォルトコンストラクタ
    /// </summary>
    public LanguageDetectionResult()
    {
    }
    
    /// <summary>
    /// 成功結果を作成
    /// </summary>
    /// <param name="language">検出された言語</param>
    /// <param name="confidenceScore">信頼度スコア</param>
    /// <param name="isReliable">信頼できるかどうか</param>
    /// <returns>言語検出結果</returns>
    public static LanguageDetectionResult CreateSuccess(
        Language language,
        double confidenceScore,
        bool isReliable = true)
    {
        ArgumentNullException.ThrowIfNull(language);
        
        return new LanguageDetectionResult
        {
            DetectedLanguage = language,
            ConfidenceScore = confidenceScore,
            IsReliable = isReliable,
            IsSuccessful = true
        };
    }
    
    /// <summary>
    /// エラー結果を作成
    /// </summary>
    /// <param name="error">エラー情報</param>
    /// <returns>言語検出結果</returns>
    public static LanguageDetectionResult CreateError(
        TranslationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        
        return new LanguageDetectionResult
        {
            DetectedLanguage = new Language { Code = "unknown", DisplayName = "Unknown" },
            ConfidenceScore = 0,
            IsReliable = false,
            IsSuccessful = false,
            Error = error
        };
    }
    
    /// <summary>
    /// 例外からエラー結果を作成
    /// </summary>
    /// <param name="errorType">エラータイプ</param>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="exception">例外</param>
    /// <returns>言語検出結果</returns>
    public static LanguageDetectionResult CreateErrorFromException(
        TranslationErrorType errorType,
        string message,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(exception);
        
        return CreateError(new TranslationError
        {
            ErrorCode = errorType.ToString(),
            Message = message,
            Exception = exception
        });
    }
}