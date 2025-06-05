using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Events;

    /// <summary>
    /// 翻訳イベント用のコンテキスト情報
    /// </summary>
    public class TranslationEventContext
    {
        private readonly List<string> _tags = [];
        private readonly Dictionary<string, object?> _additionalContext = [];
        
        /// <summary>
        /// 元のコンテキストからイベント用コンテキストを作成
        /// </summary>
        /// <param name="context">翻訳コンテキスト</param>
        /// <exception cref="System.ArgumentNullException">contextがnullの場合</exception>
        public TranslationEventContext(TranslationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            
            GameProfileId = context.GameProfileId;
            SceneId = context.SceneId;
            DialogueId = context.DialogueId;
            Priority = context.Priority;
            
            if (context.Tags != null)
            {
                _tags.AddRange(context.Tags);
            }
            
            if (context.AdditionalContext != null)
            {
                foreach (var kvp in context.AdditionalContext)
                {
                    _additionalContext[kvp.Key] = kvp.Value;
                }
            }
        }
        
        /// <summary>
        /// ゲームプロファイルID
        /// </summary>
        public string? GameProfileId { get; set; }
        
        /// <summary>
        /// シーン識別子
        /// </summary>
        public string? SceneId { get; set; }
        
        /// <summary>
        /// 会話ID
        /// </summary>
        public string? DialogueId { get; set; }
        
        /// <summary>
        /// コンテキスト優先度
        /// </summary>
        public int Priority { get; set; }
        
        /// <summary>
        /// コンテキストタグ
        /// </summary>
        public IReadOnlyList<string> Tags => _tags;
        
        /// <summary>
        /// 追加コンテキスト情報
        /// </summary>
        public IReadOnlyDictionary<string, object?> AdditionalContext => _additionalContext;
        
        /// <summary>
        /// タグを追加する
        /// </summary>
        /// <param name="tag">追加するタグ</param>
        public void AddTag(string tag)
        {
            if (!string.IsNullOrWhiteSpace(tag) && !_tags.Contains(tag))
            {
                _tags.Add(tag);
            }
        }
        
        /// <summary>
        /// 追加コンテキスト情報を追加する
        /// </summary>
        /// <param name="key">キー</param>
        /// <param name="value">値</param>
        public void AddContextData(string key, object? value)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                _additionalContext[key] = value;
            }
        }
    }
