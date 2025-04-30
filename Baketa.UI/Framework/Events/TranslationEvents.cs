using System;

namespace Baketa.UI.Framework.Events
{
    /// <summary>
    /// 翻訳設定変更イベント
    /// </summary>
    internal sealed class TranslationSettingsChangedEvent : IEvent
    {
        public string Engine { get; }
        public string TargetLanguage { get; }
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.Now;

        public TranslationSettingsChangedEvent(string engine, string targetLanguage)
        {
            Engine = engine;
            TargetLanguage = targetLanguage;
        }
    }

    /// <summary>
    /// キャプチャステータス変更イベント
    /// </summary>
    internal sealed class CaptureStatusChangedEvent : IEvent
    {
        public bool IsActive { get; }
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.Now;

        public CaptureStatusChangedEvent(bool isActive)
        {
            IsActive = isActive;
        }
    }

    /// <summary>
    /// 翻訳完了イベント
    /// </summary>
    internal sealed class TranslationCompletedEvent : IEvent
    {
        public string SourceText { get; }
        public string TranslatedText { get; }
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.Now;

        public TranslationCompletedEvent(string sourceText, string translatedText)
        {
            SourceText = sourceText;
            TranslatedText = translatedText;
        }
    }

    /// <summary>
    /// キャプチャ開始要求イベント
    /// </summary>
    internal sealed class StartCaptureRequestedEvent : IEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.Now;
    }

    /// <summary>
    /// キャプチャ停止要求イベント
    /// </summary>
    internal sealed class StopCaptureRequestedEvent : IEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.Now;
    }

    /// <summary>
    /// アプリケーション終了要求イベント
    /// </summary>
    internal sealed class ApplicationExitRequestedEvent : IEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.Now;
    }

    /// <summary>
    /// トレイ最小化要求イベント
    /// </summary>
    internal sealed class MinimizeToTrayRequestedEvent : IEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.Now;
    }

    /// <summary>
    /// 翻訳エラーイベント
    /// </summary>
    internal sealed class TranslationErrorEvent : IEvent
    {
        public string ErrorMessage { get; }
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.Now;

        public TranslationErrorEvent(string errorMessage)
        {
            ErrorMessage = errorMessage;
        }
    }

    /// <summary>
    /// キャプチャ設定画面を開く要求イベント
    /// </summary>
    internal sealed class OpenCaptureSettingsRequestedEvent : IEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.Now;
    }

    /// <summary>
    /// 翻訳設定画面を開く要求イベント
    /// </summary>
    internal sealed class OpenTranslationSettingsRequestedEvent : IEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.Now;
    }

    /// <summary>
    /// 履歴画面を開く要求イベント
    /// </summary>
    internal sealed class OpenHistoryViewRequestedEvent : IEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.Now;
    }
}