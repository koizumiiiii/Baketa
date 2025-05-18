using System;
using System.Drawing;

namespace Baketa.Core.Events.EventTypes;

    /// <summary>
    /// オーバーレイ更新イベント
    /// </summary>
    public class OverlayUpdateEvent : EventBase
    {
        /// <summary>
        /// 更新されたテキスト
        /// </summary>
        public string Text { get; }
        
        /// <summary>
        /// 表示位置
        /// </summary>
        public Rectangle DisplayArea { get; }
        
        /// <summary>
        /// 元テキスト（利用可能な場合）
        /// </summary>
        public string? OriginalText { get; }
        
        /// <summary>
        /// 翻訳元言語（利用可能な場合）
        /// </summary>
        public string? SourceLanguage { get; }
        
        /// <summary>
        /// 翻訳先言語
        /// </summary>
        public string? TargetLanguage { get; }
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="text">更新されたテキスト</param>
        /// <param name="displayArea">表示位置</param>
        /// <param name="originalText">元テキスト（利用可能な場合）</param>
        /// <param name="sourceLanguage">翻訳元言語（利用可能な場合）</param>
        /// <param name="targetLanguage">翻訳先言語</param>
        /// <exception cref="ArgumentNullException">textがnullの場合</exception>
        public OverlayUpdateEvent(
            string text, 
            Rectangle displayArea, 
            string? originalText = null, 
            string? sourceLanguage = null, 
            string? targetLanguage = null)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            DisplayArea = displayArea;
            OriginalText = originalText ?? string.Empty;
            SourceLanguage = sourceLanguage ?? string.Empty;
            TargetLanguage = targetLanguage ?? string.Empty;
        }
        
        /// <inheritdoc />
        public override string Name => "OverlayUpdate";
        
        /// <inheritdoc />
        public override string Category => "UI";
    }
