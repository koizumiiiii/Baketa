namespace Baketa.Core.Settings;

/// <summary>
/// 翻訳設定クラス（UX改善対応版）
/// 自動翻訳と単発翻訳のエンジン設定を管理
/// </summary>
public sealed class TranslationSettings
{
    /// <summary>
    /// 翻訳機能の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "翻訳機能", 
        Description = "翻訳機能を有効にします")]
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>
    /// デフォルト翻訳エンジン
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "翻訳エンジン", 
        Description = "使用する翻訳エンジン", 
        ValidValues = new object[] { TranslationEngine.Local, TranslationEngine.GoogleTranslate, TranslationEngine.DeepL, TranslationEngine.BingTranslator, TranslationEngine.OpenAI })]
    public TranslationEngine DefaultEngine { get; set; } = TranslationEngine.Local;
    
    /// <summary>
    /// ソース言語の自動検出
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "言語自動検出", 
        Description = "翻訳元言語を自動的に検出します")]
    public bool AutoDetectSourceLanguage { get; set; } = true;
    
    /// <summary>
    /// デフォルトソース言語
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "翻訳元言語", 
        Description = "自動検出無効時のデフォルト翻訳元言語", 
        ValidValues = new object[] { "ja", "en", "zh-cn", "zh-tw", "ko", "fr", "de", "es", "pt", "ru" })]
    public string DefaultSourceLanguage { get; set; } = "ja";
    
    /// <summary>
    /// デフォルトターゲット言語
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "翻訳先言語", 
        Description = "翻訳先の言語", 
        ValidValues = new object[] { "ja", "en", "zh-cn", "zh-tw", "ko", "fr", "de", "es", "pt", "ru" })]
    public string DefaultTargetLanguage { get; set; } = "en";
    
    /// <summary>
    /// 翻訳遅延時間（ミリ秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "翻訳遅延", 
        Description = "OCR検出後、翻訳開始までの遅延時間", 
        Unit = "ms", 
        MinValue = 0, 
        MaxValue = 5000)]
    public int TranslationDelayMs { get; set; } = 200;
    
    /// <summary>
    /// 翻訳キャッシュの有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "翻訳キャッシュ", 
        Description = "同じテキストの翻訳結果をキャッシュして高速化します")]
    public bool EnableTranslationCache { get; set; } = true;
    
    /// <summary>
    /// 翻訳スタイル
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "翻訳スタイル", 
        Description = "翻訳の文体・スタイル", 
        ValidValues = new object[] { TranslationStyle.Natural, TranslationStyle.Literal, TranslationStyle.Formal, TranslationStyle.Casual })]
    public TranslationStyle Style { get; set; } = TranslationStyle.Natural;
    
    /// <summary>
    /// フォールバック翻訳エンジン
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "フォールバックエンジン", 
        Description = "メインエンジンが失敗した時に使用するエンジン", 
        ValidValues = new object[] { TranslationEngine.None, TranslationEngine.Local, TranslationEngine.GoogleTranslate, TranslationEngine.BingTranslator })]
    public TranslationEngine FallbackEngine { get; set; } = TranslationEngine.Local;
    
    /// <summary>
    /// 翻訳タイムアウト時間
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "タイムアウト時間", 
        Description = "翻訳処理のタイムアウト時間", 
        Unit = "秒", 
        MinValue = 5, 
        MaxValue = 60)]
    public int TimeoutSeconds { get; set; } = 15;
    
    /// <summary>
    /// 最大文字数制限
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "最大文字数", 
        Description = "一度に翻訳する最大文字数", 
        MinValue = 10, 
        MaxValue = 10000)]
    public int MaxCharactersPerRequest { get; set; } = 1000;
    
    /// <summary>
    /// 最小文字数制限
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "最小文字数", 
        Description = "翻訳を実行する最小文字数", 
        MinValue = 1, 
        MaxValue = 100)]
    public int MinCharactersToTranslate { get; set; } = 2;
    
    /// <summary>
    /// 同一テキストの重複翻訳防止
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "重複翻訳防止", 
        Description = "同じテキストの連続翻訳を防止します")]
    public bool PreventDuplicateTranslations { get; set; } = true;
    
    /// <summary>
    /// 重複判定の類似度閾値
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "重複判定閾値", 
        Description = "テキストの類似度がこの値以上の場合重複とみなします", 
        MinValue = 0.5, 
        MaxValue = 1.0)]
    public double DuplicateSimilarityThreshold { get; set; } = 0.95;
    
    /// <summary>
    /// 並列翻訳の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "並列翻訳", 
        Description = "複数のテキストを同時に翻訳して高速化します")]
    public bool EnableParallelTranslation { get; set; } = true;
    
    /// <summary>
    /// 最大並列翻訳数
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "最大並列数", 
        Description = "同時に実行する翻訳の最大数", 
        MinValue = 1, 
        MaxValue = 10)]
    public int MaxParallelTranslations { get; set; } = 3;
    
    /// <summary>
    /// キャッシュ保持期間（時間）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "キャッシュ保持期間", 
        Description = "翻訳キャッシュを保持する期間", 
        Unit = "時間", 
        MinValue = 1, 
        MaxValue = 168)]
    public int CacheRetentionHours { get; set; } = 24;
    
    /// <summary>
    /// 最大キャッシュエントリ数
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "最大キャッシュ数", 
        Description = "保持する翻訳キャッシュの最大数", 
        MinValue = 100, 
        MaxValue = 10000)]
    public int MaxCacheEntries { get; set; } = 1000;
    
    /// <summary>
    /// APIキーの暗号化保存
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "APIキー暗号化", 
        Description = "翻訳サービスのAPIキーを暗号化して保存します")]
    public bool EncryptApiKeys { get; set; } = true;
    
    /// <summary>
    /// Google Translate APIキー
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "Google APIキー", 
        Description = "Google Translate APIのキー（暗号化保存）")]
    public string GoogleTranslateApiKey { get; set; } = string.Empty;
    
    /// <summary>
    /// DeepL APIキー
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "DeepL APIキー", 
        Description = "DeepL APIのキー（暗号化保存）")]
    public string DeepLApiKey { get; set; } = string.Empty;
    
    /// <summary>
    /// OpenAI APIキー
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "OpenAI APIキー", 
        Description = "OpenAI APIのキー（暗号化保存）")]
    public string OpenAiApiKey { get; set; } = string.Empty;
    
    /// <summary>
    /// カスタム翻訳プロンプト（OpenAI用）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "カスタムプロンプト", 
        Description = "OpenAI翻訳時のカスタムプロンプト")]
    public string CustomTranslationPrompt { get; set; } = string.Empty;
    
    /// <summary>
    /// ローカル翻訳モデルパス
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "ローカルモデルパス", 
        Description = "ローカル翻訳モデルのファイルパス")]
    public string LocalModelPath { get; set; } = string.Empty;
    
    /// <summary>
    /// ローカル翻訳モデルの自動更新
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "モデル自動更新", 
        Description = "ローカル翻訳モデルを自動的に更新します")]
    public bool AutoUpdateLocalModel { get; set; } = true;
    
    /// <summary>
    /// 翻訳品質推定の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "品質推定", 
        Description = "翻訳結果の品質を推定して表示します")]
    public bool EnableQualityEstimation { get; set; } = false;
    
    /// <summary>
    /// 品質推定の最小閾値
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "品質最小閾値", 
        Description = "この値以下の品質の翻訳には警告を表示します", 
        MinValue = 0.0, 
        MaxValue = 1.0)]
    public double QualityThreshold { get; set; } = 0.7;
    
    /// <summary>
    /// 詳細ログ出力の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Translation", "詳細ログ", 
        Description = "翻訳処理の詳細ログを出力します（開発者向け）")]
    public bool EnableVerboseLogging { get; set; } = false;
    
    /// <summary>
    /// 翻訳結果の保存
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Translation", "結果保存", 
        Description = "翻訳結果をファイルに保存します（開発者向け）")]
    public bool SaveTranslationResults { get; set; } = false;
    
    /// <summary>
    /// API使用統計の記録
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Translation", "API統計記録", 
        Description = "翻訳API使用統計を記録します（開発者向け）")]
    public bool RecordApiUsageStatistics { get; set; } = false;
    
    /// <summary>
    /// 設定のクローンを作成します
    /// </summary>
    /// <returns>クローンされた設定</returns>
    public TranslationSettings Clone()
    {
        return new TranslationSettings
        {
            IsEnabled = IsEnabled,
            DefaultEngine = DefaultEngine,
            AutoDetectSourceLanguage = AutoDetectSourceLanguage,
            DefaultSourceLanguage = DefaultSourceLanguage,
            DefaultTargetLanguage = DefaultTargetLanguage,
            TranslationDelayMs = TranslationDelayMs,
            EnableTranslationCache = EnableTranslationCache,
            Style = Style,
            FallbackEngine = FallbackEngine,
            TimeoutSeconds = TimeoutSeconds,
            MaxCharactersPerRequest = MaxCharactersPerRequest,
            MinCharactersToTranslate = MinCharactersToTranslate,
            PreventDuplicateTranslations = PreventDuplicateTranslations,
            DuplicateSimilarityThreshold = DuplicateSimilarityThreshold,
            EnableParallelTranslation = EnableParallelTranslation,
            MaxParallelTranslations = MaxParallelTranslations,
            CacheRetentionHours = CacheRetentionHours,
            MaxCacheEntries = MaxCacheEntries,
            EncryptApiKeys = EncryptApiKeys,
            GoogleTranslateApiKey = GoogleTranslateApiKey,
            DeepLApiKey = DeepLApiKey,
            OpenAiApiKey = OpenAiApiKey,
            CustomTranslationPrompt = CustomTranslationPrompt,
            LocalModelPath = LocalModelPath,
            AutoUpdateLocalModel = AutoUpdateLocalModel,
            EnableQualityEstimation = EnableQualityEstimation,
            QualityThreshold = QualityThreshold,
            EnableVerboseLogging = EnableVerboseLogging,
            SaveTranslationResults = SaveTranslationResults,
            RecordApiUsageStatistics = RecordApiUsageStatistics
        };
    }
}

/// <summary>
/// 翻訳エンジンの種類
/// </summary>
public enum TranslationEngine
{
    /// <summary>
    /// なし（翻訳しない）
    /// </summary>
    None,
    
    /// <summary>
    /// ローカル翻訳（オフライン）
    /// </summary>
    Local,
    
    /// <summary>
    /// Google Translate
    /// </summary>
    GoogleTranslate,
    
    /// <summary>
    /// DeepL Translator
    /// </summary>
    DeepL,
    
    /// <summary>
    /// Microsoft Bing Translator
    /// </summary>
    BingTranslator,
    
    /// <summary>
    /// OpenAI ChatGPT
    /// </summary>
    OpenAI
}

/// <summary>
/// 翻訳スタイル
/// </summary>
public enum TranslationStyle
{
    /// <summary>
    /// 自然な翻訳（推奨）
    /// </summary>
    Natural,
    
    /// <summary>
    /// 直訳
    /// </summary>
    Literal,
    
    /// <summary>
    /// フォーマル（丁寧語）
    /// </summary>
    Formal,
    
    /// <summary>
    /// カジュアル（口語的）
    /// </summary>
    Casual
}
