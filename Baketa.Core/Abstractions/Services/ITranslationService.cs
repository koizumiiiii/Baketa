using System.Collections.Generic;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Services;

    /// <summary>
    /// 翻訳サービスインターフェース
    /// </summary>
    public interface ITranslationService
    {
        /// <summary>
        /// テキストを翻訳します
        /// </summary>
        /// <param name="text">翻訳するテキスト</param>
        /// <param name="sourceLanguage">ソース言語コード（null の場合は自動検出）</param>
        /// <param name="targetLanguage">対象言語コード</param>
        /// <returns>翻訳されたテキスト</returns>
        Task<string> TranslateAsync(string text, string? sourceLanguage = null, string targetLanguage = "en");
        
        /// <summary>
        /// 複数のテキストを一括翻訳します
        /// </summary>
        /// <param name="texts">翻訳するテキストのリスト</param>
        /// <param name="sourceLanguage">ソース言語コード（null の場合は自動検出）</param>
        /// <param name="targetLanguage">対象言語コード</param>
        /// <returns>翻訳されたテキストのリスト</returns>
        Task<IList<string>> TranslateBatchAsync(IList<string> texts, string? sourceLanguage = null, string targetLanguage = "en");
        
        /// <summary>
        /// テキストの言語を検出します
        /// </summary>
        /// <param name="text">検出するテキスト</param>
        /// <returns>検出された言語コードと信頼度</returns>
        Task<LanguageDetectionResult> DetectLanguageAsync(string text);
        
        /// <summary>
        /// 利用可能な言語のリストを取得します
        /// </summary>
        /// <returns>利用可能な言語のリスト</returns>
        Task<IList<LanguageInfo>> GetAvailableLanguagesAsync();
        
        /// <summary>
        /// 翻訳エンジンを設定します
        /// </summary>
        /// <param name="engine">翻訳エンジン</param>
        void SetTranslationEngine(TranslationEngine engine);
        
        /// <summary>
        /// 現在の翻訳エンジンを取得します
        /// </summary>
        /// <returns>翻訳エンジン</returns>
        TranslationEngine GetCurrentEngine();
        
        /// <summary>
        /// 翻訳設定を取得します
        /// </summary>
        /// <returns>翻訳設定</returns>
        TranslationSettings GetSettings();
        
        /// <summary>
        /// 翻訳設定を設定します
        /// </summary>
        /// <param name="settings">翻訳設定</param>
        void SetSettings(TranslationSettings settings);
    }
    
    /// <summary>
    /// 言語検出結果
    /// </summary>
    public class LanguageDetectionResult
    {
        /// <summary>
        /// 検出された言語コード
        /// </summary>
        public required string LanguageCode { get; set; }
        
        /// <summary>
        /// 検出の信頼度 (0.0-1.0)
        /// </summary>
        public float Confidence { get; set; }
        
        /// <summary>
        /// 他の候補言語と信頼度
        /// </summary>
        public IReadOnlyDictionary<string, float>? AlternativeLanguages { get; set; }
    }
    
    /// <summary>
    /// 言語情報
    /// </summary>
    public class LanguageInfo
    {
        /// <summary>
        /// 言語コード
        /// </summary>
        public required string Code { get; set; }
        
        /// <summary>
        /// 言語名（現在の言語での表示用）
        /// </summary>
        public required string DisplayName { get; set; }
        
        /// <summary>
        /// ネイティブ言語名（その言語での表示用）
        /// </summary>
        public required string NativeName { get; set; }
        
        /// <summary>
        /// 翻訳方向のサポート情報
        /// </summary>
        public required TranslationDirectionSupport Support { get; set; }
    }
    
    /// <summary>
    /// 翻訳方向のサポート情報
    /// </summary>
    public class TranslationDirectionSupport
    {
        /// <summary>
        /// ソース言語としてサポートされているか
        /// </summary>
        public bool AsSource { get; set; }
        
        /// <summary>
        /// 対象言語としてサポートされているか
        /// </summary>
        public bool AsTarget { get; set; }
    }
    
    /// <summary>
    /// 翻訳エンジン
    /// </summary>
    public enum TranslationEngine
    {
        /// <summary>
        /// ローカルモデル
        /// </summary>
        Local = 0,
        
        /// <summary>
        /// Google Gemini AI翻訳
        /// </summary>
        Gemini = 1
    }
    
    /// <summary>
    /// 翻訳設定
    /// </summary>
    public class TranslationSettings
    {
        /// <summary>
        /// デフォルトのソース言語
        /// </summary>
        public string? DefaultSourceLanguage { get; set; }
        
        /// <summary>
        /// デフォルトの対象言語
        /// </summary>
        public string DefaultTargetLanguage { get; set; } = "en";
        
        /// <summary>
        /// プリファードエンジン
        /// </summary>
        public TranslationEngine PreferredEngine { get; set; } = TranslationEngine.Local;
        
        /// <summary>
        /// API キー（必要な場合）
        /// </summary>
        public string? ApiKey { get; set; }
        
        /// <summary>
        /// カスタムAPIエンドポイント（カスタムエンジン用）
        /// </summary>
        public string? CustomApiEndpoint { get; set; }
        
        /// <summary>
        /// 未翻訳のテキストをキャッシュするかどうか
        /// </summary>
        public bool CacheTranslations { get; set; } = true;
        
        /// <summary>
        /// 形式を保持するかどうか
        /// </summary>
        public bool PreserveFormatting { get; set; } = true;
        
        /// <summary>
        /// キャッシュの有効期間（時間）
        /// </summary>
        public int CacheExpirationHours { get; set; } = 24;
    }
