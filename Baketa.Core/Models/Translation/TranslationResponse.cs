using System;
using System.Collections.Generic;

namespace Baketa.Core.Models.Translation
{
    /// <summary>
    /// 翻訳レスポンスを表すクラス
    /// </summary>
    [Obsolete("代わりに Baketa.Core.Translation.Models.TranslationResponse を使用してください。", false)]
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
        /// エラー発生時はnullになる場合があります
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
        /// 翻訳の信頼度スコア（0.0～1.0）
        /// 値が負の場合（デフォルト: -1.0）は、スコア情報が利用できないことを示します
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
        /// エラーが発生した場合のエラー情報
        /// </summary>
        public TranslationError? Error { get; set; }

        /// <summary>
        /// 追加のメタデータ
        /// 例:
        /// - "AlternativeTranslations": 代替翻訳のリスト (List&lt;string&gt;)
        /// - "DetectedLanguage": 自動検出された言語 (Language)
        /// - "TokenCount": 処理されたトークン数 (int)
        /// - "QualityScore": 品質評価スコア (float)
        /// </summary>
        public Dictionary<string, object?> Metadata { get; } = new();

        /// <summary>
        /// 成功レスポンスを作成します
        /// </summary>
        /// <param name="request">元のリクエスト</param>
        /// <param name="translatedText">翻訳結果テキスト</param>
        /// <param name="engineName">使用されたエンジン名</param>
        /// <param name="processingTimeMs">処理時間（ミリ秒）</param>
        /// <returns>成功レスポンスインスタンス</returns>
        public static TranslationResponse CreateSuccess(
            TranslationRequest request,
            string translatedText,
            string engineName,
            long processingTimeMs)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(translatedText);
            ArgumentNullException.ThrowIfNull(engineName);
            
            return new TranslationResponse
            {
                RequestId = request.RequestId,
                SourceText = request.SourceText,
                TranslatedText = translatedText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                EngineName = engineName,
                ProcessingTimeMs = processingTimeMs,
                IsSuccess = true
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
            ArgumentNullException.ThrowIfNull(translatedText);
            ArgumentNullException.ThrowIfNull(engineName);
            
            var response = CreateSuccess(request, translatedText, engineName, processingTimeMs);
            response.ConfidenceScore = confidenceScore;
            return response;
        }

        /// <summary>
        /// エラーレスポンスを作成します
        /// </summary>
        /// <param name="request">元のリクエスト</param>
        /// <param name="engineName">使用されたエンジン名</param>
        /// <param name="errorCode">エラーコード</param>
        /// <param name="errorMessage">エラーメッセージ</param>
        /// <param name="processingTimeMs">処理時間（ミリ秒）</param>
        /// <returns>エラーレスポンスインスタンス</returns>
        public static TranslationResponse CreateError(
            TranslationRequest request,
            string engineName,
            string errorCode,
            string errorMessage,
            long processingTimeMs = 0)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(engineName);
            ArgumentNullException.ThrowIfNull(errorCode);
            ArgumentNullException.ThrowIfNull(errorMessage);
            
            return new TranslationResponse
            {
                RequestId = request.RequestId,
                SourceText = request.SourceText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                EngineName = engineName,
                ProcessingTimeMs = processingTimeMs,
                IsSuccess = false,
                Error = TranslationError.Create(errorCode, errorMessage)
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
            
            return new TranslationResponse
            {
                RequestId = request.RequestId,
                SourceText = request.SourceText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                EngineName = engineName,
                ProcessingTimeMs = processingTimeMs,
                IsSuccess = false,
                Error = TranslationError.FromException(errorCode, errorMessage, exception)
            };
        }

        /// <summary>
        /// 暗黙的変換演算子 - 元のレスポンスオブジェクトを新しい名前空間のオブジェクトに変換
        /// </summary>
        /// <param name="response">変換するレスポンスオブジェクト</param>
        public static implicit operator Baketa.Core.Translation.Models.TranslationResponse(TranslationResponse response)
        {
            if (response == null) return null!;
            
            var newResponse = new Baketa.Core.Translation.Models.TranslationResponse
            {
                RequestId = response.RequestId,
                SourceText = response.SourceText,
                TranslatedText = response.TranslatedText,
                SourceLanguage = response.SourceLanguage,
                TargetLanguage = response.TargetLanguage,
                EngineName = response.EngineName,
                ConfidenceScore = response.ConfidenceScore,
                ProcessingTimeMs = response.ProcessingTimeMs,
                IsSuccess = response.IsSuccess,
                Error = response.Error
            };
            
            // メタデータのコピー
            foreach (var item in response.Metadata)
            {
                newResponse.Metadata[item.Key] = item.Value;
            }
            
            return newResponse;
        }
        
        /// <summary>
        /// 新しい名前空間のレスポンスオブジェクトに変換するメソッド
        /// （暗黙的変換演算子と同等の機能）
        /// </summary>
        /// <returns>新しい名前空間のレスポンスオブジェクト</returns>
        public Baketa.Core.Translation.Models.TranslationResponse ToTranslationResponse()
        {
            return (Baketa.Core.Translation.Models.TranslationResponse)this;
        }
        
        /// <summary>
        /// 明示的変換演算子 - 新しい名前空間のレスポンスオブジェクトを元のオブジェクトに変換
        /// </summary>
        /// <param name="response">変換するレスポンスオブジェクト</param>
        public static explicit operator TranslationResponse(Baketa.Core.Translation.Models.TranslationResponse response)
        {
            if (response == null) return null!;
            
            var oldResponse = new TranslationResponse
            {
                RequestId = response.RequestId,
                SourceText = response.SourceText,
                TranslatedText = response.TranslatedText,
                SourceLanguage = (Language)(object)response.SourceLanguage,
                TargetLanguage = (Language)(object)response.TargetLanguage,
                EngineName = response.EngineName,
                ConfidenceScore = response.ConfidenceScore,
                ProcessingTimeMs = response.ProcessingTimeMs,
                IsSuccess = response.IsSuccess,
                Error = response.Error != null ? (TranslationError)(object)response.Error : null
            };
            
            // メタデータのコピー
            foreach (var item in response.Metadata)
            {
                oldResponse.Metadata[item.Key] = item.Value;
            }
            
            return oldResponse;
        }
        
        /// <summary>
        /// 新しい名前空間のレスポンスオブジェクトから変換するメソッド
        /// （明示的変換演算子と同等の機能）
        /// </summary>
        /// <param name="response">新しい名前空間のレスポンスオブジェクト</param>
        /// <returns>元のレスポンスオブジェクト</returns>
        public static TranslationResponse FromTranslationResponse(Baketa.Core.Translation.Models.TranslationResponse response)
        {
            return (TranslationResponse)response;
        }
    }
}