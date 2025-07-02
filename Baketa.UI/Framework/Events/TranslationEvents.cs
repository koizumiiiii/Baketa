using System;

namespace Baketa.UI.Framework.Events;

    /// <summary>
    /// 翻訳設定変更イベント
    /// </summary>
    /// <param name="engine">翻訳エンジン</param>
    /// <param name="targetLanguage">翻訳先言語</param>
    internal sealed class TranslationSettingsChangedEvent(string engine, string targetLanguage) : UIEventBase
    {
        /// <summary>
        /// 使用された翻訳エンジン
        /// </summary>
        public string Engine { get; } = engine;

        /// <summary>
        /// 翻訳先言語
        /// </summary>
        public string TargetLanguage { get; } = targetLanguage;

        /// <inheritdoc/>
        public override string Name => "TranslationSettingsChanged";

        /// <inheritdoc/>
        public override string Category => "Translation";
    }

    /// <summary>
    /// キャプチャステータス変更イベント
    /// </summary>
    /// <param name="isActive">キャプチャがアクティブかどうか</param>
    internal sealed class CaptureStatusChangedEvent(bool isActive) : UIEventBase
    {
        /// <summary>
        /// キャプチャがアクティブかどうか
        /// </summary>
        public bool IsActive { get; } = isActive;

        /// <inheritdoc/>
        public override string Name => "CaptureStatusChanged";

        /// <inheritdoc/>
        public override string Category => "Capture";
    }

    /// <summary>
    /// 翻訳完了イベント
    /// </summary>
    /// <param name="sourceText">原文</param>
    /// <param name="translatedText">翻訳結果</param>
    internal sealed class TranslationCompletedEvent(string sourceText, string translatedText) : UIEventBase
    {
        /// <summary>
        /// 原文
        /// </summary>
        public string SourceText { get; } = sourceText;

        /// <summary>
        /// 翻訳結果
        /// </summary>
        public string TranslatedText { get; } = translatedText;

        /// <inheritdoc/>
        public override string Name => "TranslationCompleted";

        /// <inheritdoc/>
        public override string Category => "Translation";
    }

    /// <summary>
    /// 翻訳エラーイベント
    /// </summary>
    /// <param name="errorMessage">エラーメッセージ</param>
    internal sealed class TranslationErrorEvent(string errorMessage) : UIEventBase
    {
        /// <summary>
        /// エラーメッセージ
        /// </summary>
        public string ErrorMessage { get; } = errorMessage;

        /// <inheritdoc/>
        public override string Name => "TranslationError";

        /// <inheritdoc/>
        public override string Category => "Translation";
    }
