using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Baketa.Core.Models.Translation
{
    /// <summary>
    /// 言語検出結果を表すクラス
    /// </summary>
    public class LanguageDetectionResult
    {
        /// <summary>
        /// 検出された言語
        /// </summary>
        public Language DetectedLanguage { get; set; } = new Language { Code = "und", Name = "Unknown" };
        
        /// <summary>
        /// 検出の信頼度（0-1の範囲）
        /// </summary>
        public float Confidence { get; set; }
        
        /// <summary>
        /// 代替言語候補リスト（信頼度順）
        /// </summary>
        public Collection<LanguageDetection> AlternativeLanguages { get; set; } = new Collection<LanguageDetection>();
        
        /// <summary>
        /// 検出に使用されたエンジン名
        /// </summary>
        public string? EngineName { get; set; }
        
        /// <summary>
        /// エラーメッセージ（検出失敗時）
        /// </summary>
        public string? ErrorMessage { get; set; }
        
        /// <summary>
        /// 処理時間（ミリ秒）
        /// </summary>
        public long ProcessingTimeMs { get; set; }
    }
    
    /// <summary>
    /// 検出された言語候補を表すクラス
    /// </summary>
    public class LanguageDetection
    {
        /// <summary>
        /// 言語
        /// </summary>
        public Language Language { get; set; } = new Language { Code = "und", Name = "Unknown" };
        
        /// <summary>
        /// 信頼度（0-1の範囲）
        /// </summary>
        public float Confidence { get; set; }
    }
}