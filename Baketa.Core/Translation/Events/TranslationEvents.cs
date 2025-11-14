using System;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Events;

/// <summary>
/// 翻訳開始イベント
/// </summary>
public class TranslationStartedEvent : IEvent
{
    /// <summary>
    /// イベントID
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// イベント発生時刻
    /// </summary>
    public DateTime Timestamp { get; } = DateTime.UtcNow;

    /// <summary>
    /// イベント名
    /// </summary>
    public string Name => "TranslationStarted";

    /// <summary>
    /// イベントカテゴリ
    /// </summary>
    public string Category => "Translation";

    /// <summary>
    /// リクエストID
    /// </summary>
    public required string RequestId { get; set; }

    /// <summary>
    /// 元テキスト
    /// </summary>
    public required string SourceText { get; set; }

    /// <summary>
    /// 元言語
    /// </summary>
    public required string SourceLanguage { get; set; }

    /// <summary>
    /// 対象言語
    /// </summary>
    public required string TargetLanguage { get; set; }

    /// <summary>
    /// 翻訳コンテキスト
    /// </summary>
    public TranslationEventContext? Context { get; set; }
}

/// <summary>
/// 翻訳完了イベント
/// </summary>
public class TranslationCompletedEvent : IEvent
{
    /// <summary>
    /// イベントID
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// イベント発生時刻
    /// </summary>
    public DateTime Timestamp { get; } = DateTime.UtcNow;

    /// <summary>
    /// イベント名
    /// </summary>
    public string Name => "TranslationCompleted";

    /// <summary>
    /// イベントカテゴリ
    /// </summary>
    public string Category => "Translation";

    /// <summary>
    /// リクエストID
    /// </summary>
    public required string RequestId { get; set; }

    /// <summary>
    /// 元テキスト
    /// </summary>
    public required string SourceText { get; set; }

    /// <summary>
    /// 元言語
    /// </summary>
    public required string SourceLanguage { get; set; }

    /// <summary>
    /// 対象言語
    /// </summary>
    public required string TargetLanguage { get; set; }

    /// <summary>
    /// 翻訳結果テキスト
    /// </summary>
    public required string TranslatedText { get; set; }

    /// <summary>
    /// 処理時間（ミリ秒）
    /// </summary>
    public long ProcessingTimeMs { get; set; }

    /// <summary>
    /// 使用された翻訳エンジン
    /// </summary>
    public required string TranslationEngine { get; set; }

    /// <summary>
    /// キャッシュからの取得かどうか
    /// </summary>
    public bool FromCache { get; set; }
}

/// <summary>
/// 翻訳エラーイベント
/// </summary>
public class TranslationErrorEvent : IEvent
{
    /// <summary>
    /// イベントID
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// イベント発生時刻
    /// </summary>
    public DateTime Timestamp { get; } = DateTime.UtcNow;

    /// <summary>
    /// イベント名
    /// </summary>
    public string Name => "TranslationError";

    /// <summary>
    /// イベントカテゴリ
    /// </summary>
    public string Category => "Translation";

    /// <summary>
    /// リクエストID
    /// </summary>
    public required string RequestId { get; set; }

    /// <summary>
    /// 元テキスト
    /// </summary>
    public required string SourceText { get; set; }

    /// <summary>
    /// 元言語
    /// </summary>
    public required string SourceLanguage { get; set; }

    /// <summary>
    /// 対象言語
    /// </summary>
    public required string TargetLanguage { get; set; }

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public required string ErrorMessage { get; set; }

    /// <summary>
    /// エラータイプ
    /// </summary>
    public TranslationErrorType ErrorType { get; set; }

    /// <summary>
    /// 使用された翻訳エンジン（ある場合）
    /// </summary>
    public string? TranslationEngine { get; set; }

    /// <summary>
    /// 処理時間（ミリ秒）
    /// </summary>
    public long ProcessingTimeMs { get; set; }
}

/// <summary>
/// キャッシュヒットイベント
/// </summary>
public class TranslationCacheHitEvent : IEvent
{
    /// <summary>
    /// イベントID
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// イベント発生時刻
    /// </summary>
    public DateTime Timestamp { get; } = DateTime.UtcNow;

    /// <summary>
    /// イベント名
    /// </summary>
    public string Name => "TranslationCacheHit";

    /// <summary>
    /// イベントカテゴリ
    /// </summary>
    public string Category => "Translation";

    /// <summary>
    /// リクエストID
    /// </summary>
    public required string RequestId { get; set; }

    /// <summary>
    /// 元テキスト
    /// </summary>
    public required string SourceText { get; set; }

    /// <summary>
    /// 元言語
    /// </summary>
    public required string SourceLanguage { get; set; }

    /// <summary>
    /// 対象言語
    /// </summary>
    public required string TargetLanguage { get; set; }

    /// <summary>
    /// キャッシュから取得された翻訳結果
    /// </summary>
    public required string TranslatedText { get; set; }
}
