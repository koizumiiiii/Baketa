using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Baketa.Core.Translation.Models;

    /// <summary>
    /// 言語検出結果を表すクラス
    /// </summary>
    public sealed class LanguageDetectionResult
    {
        /// <summary>
        /// 検出された言語
        /// </summary>
        public required Language DetectedLanguage { get; set; }
        
        /// <summary>
        /// 言語検出の信頼度スコア（0-1の範囲）
        /// </summary>
        public float Confidence { get; set; }
        
        /// <summary>
        /// 検出に使用されたエンジン名
        /// </summary>
        public string? EngineName { get; set; }
        
        private readonly List<LanguageDetection> _alternativeLanguages = new();
        
        /// <summary>
        /// 代替言語候補リスト（信頼度順）
        /// </summary>
        public IReadOnlyList<LanguageDetection> AlternativeLanguages => _alternativeLanguages;
        
        /// <summary>
        /// エラーメッセージ（検出失敗時）
        /// </summary>
        public string? ErrorMessage { get; set; }
        
        /// <summary>
        /// デフォルトコンストラクタ
        /// </summary>
        public LanguageDetectionResult()
        {
        }
        
        /// <summary>
        /// 基本情報を指定して初期化
        /// </summary>
        /// <param name="detectedLanguage">検出された言語</param>
        /// <param name="confidence">信頼度</param>
        /// <param name="engineName">エンジン名</param>
        public LanguageDetectionResult(Language detectedLanguage, float confidence, string engineName)
        {
            DetectedLanguage = detectedLanguage;
            Confidence = confidence;
            EngineName = engineName;
        }
        
        /// <summary>
        /// 代替言語候補を追加する
        /// </summary>
        /// <param name="alternative">代替言語候補</param>
        /// <exception cref="ArgumentNullException">alternativeがnullの場合</exception>
        public void AddAlternativeLanguage(LanguageDetection alternative)
        {
            ArgumentNullException.ThrowIfNull(alternative);
            _alternativeLanguages.Add(alternative);
        }
        
        /// <summary>
        /// 代替言語候補を追加する
        /// </summary>
        /// <param name="language">言語</param>
        /// <param name="confidence">信頼度</param>
        /// <exception cref="ArgumentNullException">languageがnullの場合</exception>
        public void AddAlternativeLanguage(Language language, float confidence)
        {
            ArgumentNullException.ThrowIfNull(language);
            _alternativeLanguages.Add(new LanguageDetection
            {
                Language = language, // requiredプロパティを明示的に設定
                Confidence = confidence
            });
        }
        
        /// <summary>
        /// クローンを作成
        /// </summary>
        /// <returns>このオブジェクトのクローン</returns>
        public LanguageDetectionResult Clone()
        {
            // requiredプロパティを予め取得
            var detectedLang = this.DetectedLanguage;

            var clone = new LanguageDetectionResult
            {
                // requiredプロパティを明示的に設定
                DetectedLanguage = detectedLang,
                Confidence = Confidence,
                EngineName = EngineName,
                ErrorMessage = ErrorMessage
            };
            
            foreach (var alt in _alternativeLanguages)
            {
                clone._alternativeLanguages.Add(alt.Clone());
            }
            
            return clone;
        }
    }
    
    /// <summary>
    /// 言語検出の候補を表すクラス
    /// </summary>
    public sealed class LanguageDetection
    {
        /// <summary>
        /// 検出された言語
        /// </summary>
        public required Language Language { get; set; }
        
        /// <summary>
        /// 検出の信頼度（0-1の範囲）
        /// </summary>
        public float Confidence { get; set; }
        
        /// <summary>
        /// デフォルトコンストラクタ
        /// </summary>
        public LanguageDetection()
        {
        }
        
        /// <summary>
        /// 基本情報を指定して初期化
        /// </summary>
        /// <param name="language">検出された言語</param>
        /// <param name="confidence">信頼度</param>
        public LanguageDetection(Language language, float confidence)
        {
            Language = language;
            Confidence = confidence;
        }
        
        /// <summary>
        /// クローンを作成
        /// </summary>
        /// <returns>このオブジェクトのクローン</returns>
        public LanguageDetection Clone()
        {
            return new LanguageDetection
            {
                Language = this.Language, // requiredプロパティの定義が必要
                Confidence = this.Confidence
            };
        }
    }
