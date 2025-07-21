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
        ValidValues = [TranslationEngine.Local, TranslationEngine.Gemini, TranslationEngine.AlphaOpusMT])]
    public TranslationEngine DefaultEngine { get; set; } = TranslationEngine.AlphaOpusMT;
    
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
        ValidValues = ["ja", "en"])]
    public string DefaultSourceLanguage { get; set; } = "ja";
    
    /// <summary>
    /// デフォルトターゲット言語
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "翻訳先言語", 
        Description = "翻訳先の言語", 
        ValidValues = ["ja", "en"])]
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
        ValidValues = [TranslationStyle.Natural, TranslationStyle.Literal, TranslationStyle.Formal, TranslationStyle.Casual])]
    public TranslationStyle Style { get; set; } = TranslationStyle.Natural;
    
    /// <summary>
    /// フォールバック翻訳エンジン
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "フォールバックエンジン", 
        Description = "メインエンジンが失敗した時に使用するエンジン", 
        ValidValues = [TranslationEngine.None, TranslationEngine.Local, TranslationEngine.Gemini, TranslationEngine.AlphaOpusMT])]
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
    /// Google Gemini APIキー
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "Gemini APIキー", 
        Description = "Google Gemini APIのキー（暗号化保存）")]
    public string GeminiApiKey { get; set; } = string.Empty;
    
    /// <summary>
    /// カスタム翻訳プロンプト（Gemini用）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "カスタムプロンプト", 
        Description = "Gemini翻訳時のカスタムプロンプト")]
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
    /// αテスト向けOPUS-MT機能の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "αテスト機能", 
        Description = "αテスト向けOPUS-MT翻訳機能を有効にします")]
    public bool EnableAlphaOpusMT { get; set; } = true;
    
    /// <summary>
    /// αテスト向けOPUS-MTモデルディレクトリ
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "αテストモデルパス", 
        Description = "αテスト向けOPUS-MTモデルファイルのディレクトリパス")]
    public string AlphaOpusMTModelDirectory { get; set; } = "Models/OpusMT";
    
    /// <summary>
    /// αテスト向けOPUS-MTメモリ制限（MB）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "αテストメモリ制限", 
        Description = "αテスト向けOPUS-MTの最大メモリ使用量", 
        Unit = "MB", 
        MinValue = 100, 
        MaxValue = 1000)]
    public int AlphaOpusMTMemoryLimitMb { get; set; } = 300;
    
    /// <summary>
    /// テキストグループ化機能を有効にする
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "文章グループ化", 
        Description = "OCR結果を文章のまとまりごとにグループ化して翻訳表示します")]
    public bool EnableTextGrouping { get; set; } = true;
    
    /// <summary>
    /// 段落区切りを保持する
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "段落区切り保持", 
        Description = "文章グループ化時に段落区切りを保持します")]
    public bool PreserveParagraphs { get; set; } = true;
    
    /// <summary>
    /// 同じ行と判定する閾値
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "行判定閾値", 
        Description = "同じ行と判定する垂直距離の閾値（平均文字高に対する比率）", 
        MinValue = 0.1, 
        MaxValue = 1.0)]
    public double SameLineThreshold { get; set; } = 0.5;
    
    /// <summary>
    /// 段落区切り判定閾値
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "段落区切り閾値", 
        Description = "段落区切りと判定する行間の閾値（平均行高に対する比率）", 
        MinValue = 0.5, 
        MaxValue = 3.0)]
    public double ParagraphSeparationThreshold { get; set; } = 1.5;
    
    /// <summary>
    /// 翻訳完了後のクールダウン時間（秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "翻訳後クールダウン", 
        Description = "翻訳完了後の一時停止時間（重複翻訳を防止）", 
        Unit = "秒", 
        MinValue = 0, 
        MaxValue = 10)]
    public int PostTranslationCooldownSeconds { get; set; } = 3;
    
    /// <summary>
    /// 真のウィンドウキャプチャを使用する
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "真のウィンドウキャプチャ", 
        Description = "PrintWindowを使用して他のウィンドウの重なりを除外したキャプチャを行います")]
    public bool UseTrueWindowCapture { get; set; } = true;
    
    /// <summary>
    /// 従来のキャプチャ方式を優先する（デバッグ用）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "従来キャプチャ優先", 
        Description = "BitBltを優先して使用します（PrintWindowでテキスト検出に問題がある場合）")]
    public bool PreferLegacyCapture { get; set; } = false;
    
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
            GeminiApiKey = GeminiApiKey,
            CustomTranslationPrompt = CustomTranslationPrompt,
            LocalModelPath = LocalModelPath,
            AutoUpdateLocalModel = AutoUpdateLocalModel,
            EnableQualityEstimation = EnableQualityEstimation,
            QualityThreshold = QualityThreshold,
            EnableVerboseLogging = EnableVerboseLogging,
            SaveTranslationResults = SaveTranslationResults,
            RecordApiUsageStatistics = RecordApiUsageStatistics,
            EnableAlphaOpusMT = EnableAlphaOpusMT,
            AlphaOpusMTModelDirectory = AlphaOpusMTModelDirectory,
            AlphaOpusMTMemoryLimitMb = AlphaOpusMTMemoryLimitMb,
            EnableTextGrouping = EnableTextGrouping,
            PreserveParagraphs = PreserveParagraphs,
            SameLineThreshold = SameLineThreshold,
            ParagraphSeparationThreshold = ParagraphSeparationThreshold,
            PostTranslationCooldownSeconds = PostTranslationCooldownSeconds,
            UseTrueWindowCapture = UseTrueWindowCapture,
            PreferLegacyCapture = PreferLegacyCapture
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
    /// Google Gemini AI翻訳（クラウド）
    /// </summary>
    Gemini,
    
    /// <summary>
    /// αテスト向けOPUS-MT翻訳（ローカル）
    /// </summary>
    AlphaOpusMT
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
