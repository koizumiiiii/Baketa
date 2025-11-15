using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Events;

/// <summary>
/// 翻訳関連イベントの基本インターフェース
/// </summary>
public interface ITranslationEvent : IEvent
{
    /// <summary>
    /// 翻訳対象のテキスト
    /// </summary>
    string SourceText { get; }

    /// <summary>
    /// 翻訳元言語
    /// </summary>
    string SourceLanguage { get; }

    /// <summary>
    /// 翻訳先言語
    /// </summary>
    string TargetLanguage { get; }
}

/// <summary>
/// 翻訳リクエスト開始イベント
/// </summary>
public interface ITranslationRequestedEvent : ITranslationEvent
{
    /// <summary>
    /// リクエスト識別子
    /// </summary>
    string RequestId { get; }

    /// <summary>
    /// 翻訳元のコンテキスト情報
    /// </summary>
    TranslationContext? Context { get; }
}

/// <summary>
/// 翻訳完了イベント
/// </summary>
public interface ITranslationCompletedEvent : ITranslationEvent
{
    /// <summary>
    /// 元のリクエスト識別子
    /// </summary>
    string RequestId { get; }

    /// <summary>
    /// 翻訳結果テキスト
    /// </summary>
    string TranslatedText { get; }

    /// <summary>
    /// 翻訳元のコンテキスト情報
    /// </summary>
    TranslationContext? Context { get; }

    /// <summary>
    /// 翻訳にかかった時間（ミリ秒）
    /// </summary>
    long ProcessingTimeMs { get; }

    /// <summary>
    /// 使用された翻訳エンジン
    /// </summary>
    string TranslationEngine { get; }
}

/// <summary>
/// 翻訳失敗イベント
/// </summary>
public interface ITranslationFailedEvent : ITranslationEvent
{
    /// <summary>
    /// 元のリクエスト識別子
    /// </summary>
    string RequestId { get; }

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    string ErrorMessage { get; }

    /// <summary>
    /// エラーコード
    /// </summary>
    string ErrorCode { get; }

    /// <summary>
    /// 再試行可能かどうか
    /// </summary>
    bool IsRetryable { get; }
}

/// <summary>
/// キャッシュヒットイベント
/// </summary>
public interface ITranslationCacheHitEvent : ITranslationEvent
{
    /// <summary>
    /// 元のリクエスト識別子
    /// </summary>
    string RequestId { get; }

    /// <summary>
    /// キャッシュから取得された翻訳結果
    /// </summary>
    string TranslatedText { get; }

    /// <summary>
    /// キャッシュのソース（メモリ/永続化）
    /// </summary>
    string CacheSource { get; }
}
