using System;

namespace Baketa.Core.Events.EventTypes
{
    /// <summary>
    /// 翻訳完了イベント
    /// </summary>
    public class TranslationCompletedEvent : EventBase
    {
        /// <summary>
        /// 元のテキスト
        /// </summary>
        public string SourceText { get; }
        
        /// <summary>
        /// 翻訳されたテキスト
        /// </summary>
        public string TranslatedText { get; }
        
        /// <summary>
        /// 元言語コード
        /// </summary>
        public string SourceLanguage { get; }
        
        /// <summary>
        /// 翻訳先言語コード
        /// </summary>
        public string TargetLanguage { get; }
        
        /// <summary>
        /// 翻訳処理時間
        /// </summary>
        public TimeSpan ProcessingTime { get; }
        
        /// <summary>
        /// 使用された翻訳エンジン名
        /// </summary>
        public string EngineName { get; }
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="sourceText">元のテキスト</param>
        /// <param name="translatedText">翻訳されたテキスト</param>
        /// <param name="sourceLanguage">元言語コード</param>
        /// <param name="targetLanguage">翻訳先言語コード</param>
        /// <param name="processingTime">翻訳処理時間</param>
        /// <param name="engineName">使用された翻訳エンジン名</param>
        /// <exception cref="ArgumentNullException">sourceTextまたはtranslatedTextがnullの場合</exception>
        public TranslationCompletedEvent(
            string sourceText, 
            string translatedText, 
            string sourceLanguage, 
            string targetLanguage, 
            TimeSpan processingTime,
            string engineName = "Default")
        {
            SourceText = sourceText ?? throw new ArgumentNullException(nameof(sourceText));
            TranslatedText = translatedText ?? throw new ArgumentNullException(nameof(translatedText));
            SourceLanguage = sourceLanguage ?? "auto";
            TargetLanguage = targetLanguage ?? throw new ArgumentNullException(nameof(targetLanguage));
            ProcessingTime = processingTime;
            EngineName = engineName ?? "Default";
        }
        
        /// <inheritdoc />
        public override string Name => "TranslationCompleted";
        
        /// <inheritdoc />
        public override string Category => "Translation";
    }
}