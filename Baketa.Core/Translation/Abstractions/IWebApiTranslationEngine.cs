using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Abstractions
{
    /// <summary>
    /// WebAPI翻訳エンジン固有の機能を定義するインターフェース
    /// </summary>
    public interface IWebApiTranslationEngine : ITranslationEngine
    {
        /// <summary>
        /// APIのベースURL
        /// </summary>
        Uri ApiBaseUrl { get; }
        
        /// <summary>
        /// APIキーが設定されているかどうか
        /// </summary>
        bool HasApiKey { get; }
        
        /// <summary>
        /// APIのリクエスト制限（リクエスト/分）
        /// </summary>
        int RateLimit { get; }
        
        /// <summary>
        /// APIの現在のクォータ残量（リクエスト数）
        /// </summary>
        int? QuotaRemaining { get; }
        
        /// <summary>
        /// APIのクォータリセット時刻
        /// </summary>
        DateTime? QuotaResetTime { get; }
        
        /// <summary>
        /// 自動検出言語をサポートしているかどうか
        /// </summary>
        bool SupportsAutoDetection { get; }
        
        /// <summary>
        /// APIのステータスを確認します
        /// </summary>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>APIのステータス情報</returns>
        Task<ApiStatusInfo> CheckApiStatusAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// テキストの言語を自動検出します
        /// </summary>
        /// <param name="text">検出対象テキスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>検出された言語と信頼度</returns>
        Task<LanguageDetectionResult> DetectLanguageAsync(string text, CancellationToken cancellationToken = default);
    }
    
    /// <summary>
    /// APIステータス情報
    /// </summary>
    public class ApiStatusInfo
    {
        /// <summary>
        /// APIが利用可能かどうか
        /// </summary>
        public bool IsAvailable { get; set; }
        
        /// <summary>
        /// APIのバージョン
        /// </summary>
        public string? ApiVersion { get; set; }
        
        /// <summary>
        /// 現在のクォータ残量
        /// </summary>
        public int? QuotaRemaining { get; set; }
        
        /// <summary>
        /// クォータのリセット時刻
        /// </summary>
        public DateTime? QuotaResetTime { get; set; }
        
        /// <summary>
        /// ステータスメッセージ
        /// </summary>
        public string StatusMessage { get; set; } = string.Empty;
        
        /// <summary>
        /// ステータス取得時刻
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
    
    /// <summary>
    /// 言語検出結果
    /// </summary>
    public class LanguageDetectionResult
    {
        /// <summary>
        /// 検出された言語
        /// </summary>
        public Language DetectedLanguage { get; set; } = Language.English;
        
        /// <summary>
        /// 言語検出の信頼度（0.0～1.0）
        /// </summary>
        public float Confidence { get; set; }
        
        /// <summary>
        /// その他の候補言語と信頼度
        /// </summary>
        public Dictionary<Language, float> AlternativeLanguages { get; } = new();
        
        /// <summary>
        /// 検出処理時間（ミリ秒）
        /// </summary>
        public long ProcessingTimeMs { get; set; }
        
        /// <summary>
        /// 検出エンジン名
        /// </summary>
        public string EngineName { get; set; } = string.Empty;
    }
}
