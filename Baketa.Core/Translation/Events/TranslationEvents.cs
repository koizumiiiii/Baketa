using System;
using Baketa.Core.Events;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Events
{
    /// <summary>
    /// 翻訳イベントの基底クラス
    /// </summary>
    public abstract class TranslationEventBase : EventBase, ITranslationEvent
    {
        /// <summary>
        /// 翻訳対象のテキスト
        /// </summary>
        public string SourceText { get; }
        
        /// <summary>
        /// 翻訳元言語
        /// </summary>
        public string SourceLanguage { get; }
        
        /// <summary>
        /// 翻訳先言語
        /// </summary>
        public string TargetLanguage { get; }
        
        /// <summary>
        /// 基本コンストラクタ
        /// </summary>
        /// <param name="sourceText">翻訳対象テキスト</param>
        /// <param name="sourceLanguage">翻訳元言語</param>
        /// <param name="targetLanguage">翻訳先言語</param>
        protected TranslationEventBase(string sourceText, string sourceLanguage, string targetLanguage)
        {
            SourceText = sourceText;
            SourceLanguage = sourceLanguage;
            TargetLanguage = targetLanguage;
        }
        
        /// <summary>
        /// 翻訳リクエストからイベントを初期化
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        protected TranslationEventBase(TranslationRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            
            SourceText = request.SourceText;
            SourceLanguage = request.SourceLanguage.Code;
            TargetLanguage = request.TargetLanguage.Code;
        }
        
        /// <summary>
        /// 翻訳レスポンスからイベントを初期化
        /// </summary>
        /// <param name="response">翻訳レスポンス</param>
        protected TranslationEventBase(TranslationResponse response)
        {
            ArgumentNullException.ThrowIfNull(response);
            
            SourceText = response.SourceText;
            SourceLanguage = response.SourceLanguage.Code;
            TargetLanguage = response.TargetLanguage.Code;
        }
    }
    
    /// <summary>
    /// 翻訳リクエスト開始イベント実装
    /// </summary>
    public class TranslationRequestedEvent : TranslationEventBase, ITranslationRequestedEvent
    {
        /// <summary>
        /// リクエスト識別子
        /// </summary>
        public string RequestId { get; }
        
        /// <summary>
        /// 翻訳元のコンテキスト情報
        /// </summary>
        public TranslationContext? Context { get; }
        
        /// <summary>
        /// イベント名
        /// </summary>
        public override string Name => "TranslationRequested";
        
        /// <summary>
        /// イベントカテゴリ
        /// </summary>
        public override string Category => "Translation";
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="sourceText">翻訳対象テキスト</param>
        /// <param name="sourceLanguage">翻訳元言語</param>
        /// <param name="targetLanguage">翻訳先言語</param>
        /// <param name="requestId">リクエストID</param>
        /// <param name="context">翻訳コンテキスト</param>
        public TranslationRequestedEvent(
            string sourceText, 
            string sourceLanguage, 
            string targetLanguage, 
            string requestId,
            TranslationContext? context = null) 
            : base(sourceText, sourceLanguage, targetLanguage)
        {
            RequestId = requestId;
            Context = context?.Clone();
        }
        
        /// <summary>
        /// 翻訳リクエストからイベントを作成
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        public TranslationRequestedEvent(TranslationRequest request)
            : base(request)
        {
            ArgumentNullException.ThrowIfNull(request);
            
            RequestId = request.RequestId.ToString();
            Context = request.Context?.Clone();
        }
    }
    
    /// <summary>
    /// 翻訳完了イベント実装
    /// </summary>
    public class TranslationCompletedEvent : TranslationEventBase, ITranslationCompletedEvent
    {
        /// <summary>
        /// 元のリクエスト識別子
        /// </summary>
        public string RequestId { get; }
        
        /// <summary>
        /// 翻訳結果テキスト
        /// </summary>
        public string TranslatedText { get; }
        
        /// <summary>
        /// 翻訳元のコンテキスト情報
        /// </summary>
        public TranslationContext? Context { get; }
        
        /// <summary>
        /// 翻訳にかかった時間（ミリ秒）
        /// </summary>
        public long ProcessingTimeMs { get; }
        
        /// <summary>
        /// 使用された翻訳エンジン
        /// </summary>
        public string TranslationEngine { get; }
        
        /// <summary>
        /// イベント名
        /// </summary>
        public override string Name => "TranslationCompleted";
        
        /// <summary>
        /// イベントカテゴリ
        /// </summary>
        public override string Category => "Translation";
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="requestId">リクエストID</param>
        /// <param name="sourceText">翻訳対象テキスト</param>
        /// <param name="translatedText">翻訳結果テキスト</param>
        /// <param name="sourceLanguage">翻訳元言語</param>
        /// <param name="targetLanguage">翻訳先言語</param>
        /// <param name="translationEngine">翻訳エンジン</param>
        /// <param name="processingTimeMs">処理時間（ミリ秒）</param>
        /// <param name="context">翻訳コンテキスト</param>
        public TranslationCompletedEvent(
            string requestId,
            string sourceText, 
            string translatedText,
            string sourceLanguage, 
            string targetLanguage, 
            string translationEngine,
            long processingTimeMs = 0,
            TranslationContext? context = null) 
            : base(sourceText, sourceLanguage, targetLanguage)
        {
            RequestId = requestId;
            TranslatedText = translatedText;
            Context = context?.Clone();
            ProcessingTimeMs = processingTimeMs;
            TranslationEngine = translationEngine;
        }
        
        /// <summary>
        /// 翻訳レスポンスからイベントを作成
        /// </summary>
        /// <param name="response">翻訳レスポンス</param>
        /// <param name="context">翻訳コンテキスト</param>
        public TranslationCompletedEvent(TranslationResponse response, TranslationContext? context = null)
            : base(response)
        {
            ArgumentNullException.ThrowIfNull(response);
            
            if (!response.IsSuccess || response.TranslatedText == null)
            {
                throw new ArgumentException("成功した翻訳レスポンスからのみイベントを作成できます", nameof(response));
            }
            
            RequestId = response.RequestId.ToString();
            TranslatedText = response.TranslatedText;
            Context = context?.Clone();
            ProcessingTimeMs = response.ProcessingTimeMs;
            TranslationEngine = response.EngineName;
        }
    }
    
    /// <summary>
    /// 翻訳失敗イベント実装
    /// </summary>
    public class TranslationFailedEvent : TranslationEventBase, ITranslationFailedEvent
    {
        /// <summary>
        /// 元のリクエスト識別子
        /// </summary>
        public string RequestId { get; }
        
        /// <summary>
        /// エラーメッセージ
        /// </summary>
        public string ErrorMessage { get; }
        
        /// <summary>
        /// エラーコード
        /// </summary>
        public string ErrorCode { get; }
        
        /// <summary>
        /// 再試行可能かどうか
        /// </summary>
        public bool IsRetryable { get; }
        
        /// <summary>
        /// イベント名
        /// </summary>
        public override string Name => "TranslationFailed";
        
        /// <summary>
        /// イベントカテゴリ
        /// </summary>
        public override string Category => "Translation";
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="requestId">リクエストID</param>
        /// <param name="sourceText">翻訳対象テキスト</param>
        /// <param name="sourceLanguage">翻訳元言語</param>
        /// <param name="targetLanguage">翻訳先言語</param>
        /// <param name="errorMessage">エラーメッセージ</param>
        /// <param name="errorCode">エラーコード</param>
        /// <param name="isRetryable">再試行可能かどうか</param>
        public TranslationFailedEvent(
            string requestId,
            string sourceText, 
            string sourceLanguage, 
            string targetLanguage, 
            string errorMessage,
            string errorCode = "Unknown",
            bool isRetryable = false) 
            : base(sourceText, sourceLanguage, targetLanguage)
        {
            RequestId = requestId;
            ErrorMessage = errorMessage;
            ErrorCode = errorCode;
            IsRetryable = isRetryable;
        }
        
        /// <summary>
        /// 翻訳レスポンスからイベントを作成
        /// </summary>
        /// <param name="response">翻訳レスポンス</param>
        public TranslationFailedEvent(TranslationResponse response)
            : base(response)
        {
            ArgumentNullException.ThrowIfNull(response);
            
            if (response.IsSuccess || response.Error == null)
            {
                throw new ArgumentException("失敗した翻訳レスポンスからのみイベントを作成できます", nameof(response));
            }
            
            RequestId = response.RequestId.ToString();
            ErrorMessage = response.Error.Message;
            ErrorCode = response.Error.ErrorCode;
            IsRetryable = response.Error.IsRetryable;
        }
    }
    
    /// <summary>
    /// キャッシュヒットイベント実装
    /// </summary>
    public class TranslationCacheHitEvent : TranslationEventBase, ITranslationCacheHitEvent
    {
        /// <summary>
        /// 元のリクエスト識別子
        /// </summary>
        public string RequestId { get; }
        
        /// <summary>
        /// キャッシュから取得された翻訳結果
        /// </summary>
        public string TranslatedText { get; }
        
        /// <summary>
        /// キャッシュのソース（メモリ/永続化）
        /// </summary>
        public string CacheSource { get; }
        
        /// <summary>
        /// イベント名
        /// </summary>
        public override string Name => "TranslationCacheHit";
        
        /// <summary>
        /// イベントカテゴリ
        /// </summary>
        public override string Category => "Translation";
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="requestId">リクエストID</param>
        /// <param name="sourceText">翻訳対象テキスト</param>
        /// <param name="translatedText">翻訳結果テキスト</param>
        /// <param name="sourceLanguage">翻訳元言語</param>
        /// <param name="targetLanguage">翻訳先言語</param>
        /// <param name="cacheSource">キャッシュソース</param>
        public TranslationCacheHitEvent(
            string requestId,
            string sourceText, 
            string translatedText,
            string sourceLanguage, 
            string targetLanguage, 
            string cacheSource = "Memory") 
            : base(sourceText, sourceLanguage, targetLanguage)
        {
            RequestId = requestId;
            TranslatedText = translatedText;
            CacheSource = cacheSource;
        }
        
        /// <summary>
        /// 翻訳リクエストとキャッシュエントリからイベントを作成
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        /// <param name="cacheEntry">キャッシュエントリ</param>
        /// <param name="cacheSource">キャッシュソース</param>
        public TranslationCacheHitEvent(
            TranslationRequest request, 
            TranslationCacheEntry cacheEntry, 
            string cacheSource = "Memory")
            : base(request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(cacheEntry);
            
            RequestId = request.RequestId.ToString();
            TranslatedText = cacheEntry.TranslatedText;
            CacheSource = cacheSource;
        }
    }
}
