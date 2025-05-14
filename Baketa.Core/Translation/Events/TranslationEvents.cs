using System;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Models.Translation;
// using Baketa.Core.Events; // 名前空間の競合を避けるためにコメントアウト

namespace Baketa.Core.Translation.Events
{
    /// <summary>
    /// 翻訳イベントの基底クラス
    /// </summary>
    public abstract class TranslationEventBase : Baketa.Core.Abstractions.Events.IEvent
    {
        /// <summary>
        /// イベントID
        /// </summary>
        public Guid Id { get; } = Guid.NewGuid();

        /// <summary>
        /// タイムスタンプ
        /// </summary>
        public DateTime Timestamp { get; } = DateTime.UtcNow;

        /// <summary>
        /// イベント名
        /// </summary>
        public string Name => GetType().Name;

        /// <summary>
        /// イベントカテゴリ
        /// </summary>
        public string Category => "Translation";

        /// <summary>
        /// 翻訳元テキスト
        /// </summary>
        public required string SourceText { get; set; }

        /// <summary>
        /// 翻訳元言語
        /// </summary>
        public required Language SourceLanguage { get; set; }

        /// <summary>
        /// 翻訳先言語
        /// </summary>
        public required Language TargetLanguage { get; set; }

        /// <summary>
        /// 翻訳エンジン名
        /// </summary>
        public required string EngineName { get; set; }
    }

    /// <summary>
    /// 翻訳開始イベント
    /// </summary>
    public class TranslationStartedEvent : TranslationEventBase
    {
        /// <summary>
        /// リクエストID
        /// </summary>
        public required Guid RequestId { get; set; }
    }

    /// <summary>
    /// 翻訳完了イベント
    /// </summary>
    public class TranslationCompletedEvent : TranslationEventBase
    {
        /// <summary>
        /// リクエストID
        /// </summary>
        public required Guid RequestId { get; set; }

        /// <summary>
        /// 翻訳結果テキスト
        /// </summary>
        public string? TranslatedText { get; set; }

        /// <summary>
        /// 処理時間（ミリ秒）
        /// </summary>
        public long ProcessingTimeMs { get; set; }
        
        /// <summary>
        /// キャッシュヒットかどうか
        /// </summary>
        public bool IsCacheHit { get; set; }
    }

    /// <summary>
    /// 翻訳エラーイベント
    /// </summary>
    public class TranslationErrorEvent : TranslationEventBase
    {
        /// <summary>
        /// リクエストID
        /// </summary>
        public required Guid RequestId { get; set; }

        /// <summary>
        /// エラーメッセージ
        /// </summary>
        public required string ErrorMessage { get; set; }

        /// <summary>
        /// エラーコード
        /// </summary>
        public string? ErrorCode { get; set; }

        /// <summary>
        /// エラーの種類
        /// </summary>
        public TranslationErrorType ErrorType { get; set; } = TranslationErrorType.Unknown;
    }

    /// <summary>
    /// 翻訳エラーの種類
    /// </summary>
    public enum TranslationErrorType
    {
        /// <summary>
        /// 不明なエラー
        /// </summary>
        Unknown,
        
        /// <summary>
        /// ネットワークエラー
        /// </summary>
        Network,
        
        /// <summary>
        /// 認証エラー
        /// </summary>
        Authentication,
        
        /// <summary>
        /// APIエラー
        /// </summary>
        Api,
        
        /// <summary>
        /// コンテンツエラー
        /// </summary>
        Content,
        
        /// <summary>
        /// サーバーエラー
        /// </summary>
        Server,
        
        /// <summary>
        /// クライアントエラー
        /// </summary>
        Client,
        
        /// <summary>
        /// タイムアウト
        /// </summary>
        Timeout
    }
}