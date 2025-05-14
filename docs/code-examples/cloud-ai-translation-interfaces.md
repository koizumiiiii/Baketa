# クラウドAI翻訳のインターフェース設計

Baketaプロジェクトでは、高品質な翻訳のために様々なクラウドAI翻訳サービスとの連携をサポートしています。このドキュメントでは、クラウドAI翻訳サービスの抽象化と統合のためのインターフェース設計について説明します。

## 1. クラウド翻訳プロバイダーの抽象化

```csharp
namespace Baketa.Core.Translation.Cloud
{
    /// <summary>
    /// クラウド翻訳プロバイダーの種類
    /// </summary>
    public enum CloudTranslationProviderType
    {
        /// <summary>
        /// Google Cloud Translation
        /// </summary>
        Google,
        
        /// <summary>
        /// Microsoft Azure Translator
        /// </summary>
        Azure,
        
        /// <summary>
        /// DeepL API
        /// </summary>
        DeepL,
        
        /// <summary>
        /// Amazon Translate
        /// </summary>
        Amazon,
        
        /// <summary>
        /// OpenAI API
        /// </summary>
        OpenAI,
        
        /// <summary>
        /// カスタムプロバイダー
        /// </summary>
        Custom
    }
    
    /// <summary>
    /// クラウド翻訳プロバイダーインターフェース
    /// </summary>
    public interface ICloudTranslationProvider
    {
        /// <summary>
        /// プロバイダーの識別子
        /// </summary>
        string ProviderId { get; }
        
        /// <summary>
        /// プロバイダー名
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// プロバイダーの種類
        /// </summary>
        CloudTranslationProviderType ProviderType { get; }
        
        /// <summary>
        /// サポートされている翻訳元言語
        /// </summary>
        Task<IReadOnlyList<CloudLanguage>> GetSupportedSourceLanguagesAsync();
        
        /// <summary>
        /// サポートされている翻訳先言語
        /// </summary>
        Task<IReadOnlyList<CloudLanguage>> GetSupportedTargetLanguagesAsync();
        
        /// <summary>
        /// プロバイダーのAPIキー認証情報を設定
        /// </summary>
        /// <param name="apiKey">APIキー</param>
        /// <returns>認証が成功したかどうか</returns>
        Task<bool> AuthenticateWithApiKeyAsync(string apiKey);
        
        /// <summary>
        /// プロバイダーの認証情報を設定
        /// </summary>
        /// <param name="credentials">認証情報</param>
        /// <returns>認証が成功したかどうか</returns>
        Task<bool> AuthenticateAsync(ICloudCredentials credentials);
        
        /// <summary>
        /// プロバイダーが認証済みかどうか
        /// </summary>
        bool IsAuthenticated { get; }
        
        /// <summary>
        /// 言語ペアがサポートされているかを確認
        /// </summary>
        /// <param name="sourceLanguage">翻訳元言語</param>
        /// <param name="targetLanguage">翻訳先言語</param>
        /// <returns>サポートされている場合はtrue</returns>
        Task<bool> SupportsLanguagePairAsync(string sourceLanguage, string targetLanguage);
        
        /// <summary>
        /// 翻訳リクエスト
        /// </summary>
        /// <param name="sourceText">翻訳元テキスト</param>
        /// <param name="sourceLanguage">翻訳元言語</param>
        /// <param name="targetLanguage">翻訳先言語</param>
        /// <param name="options">翻訳オプション</param>
        /// <returns>翻訳結果</returns>
        Task<ICloudTranslationResult> TranslateAsync(
            string sourceText,
            string sourceLanguage,
            string targetLanguage,
            CloudTranslationOptions options = null);
        
        /// <summary>
        /// バッチ翻訳リクエスト
        /// </summary>
        /// <param name="texts">翻訳元テキストの配列</param>
        /// <param name="sourceLanguage">翻訳元言語</param>
        /// <param name="targetLanguage">翻訳先言語</param>
        /// <param name="options">翻訳オプション</param>
        /// <returns>翻訳結果の配列</returns>
        Task<IReadOnlyList<ICloudTranslationResult>> TranslateBatchAsync(
            IReadOnlyList<string> texts,
            string sourceLanguage,
            string targetLanguage,
            CloudTranslationOptions options = null);
        
        /// <summary>
        /// 言語の自動検出
        /// </summary>
        /// <param name="text">検出対象テキスト</param>
        /// <returns>検出された言語情報</returns>
        Task<ILanguageDetectionResult> DetectLanguageAsync(string text);
        
        /// <summary>
        /// 利用状況の取得
        /// </summary>
        /// <returns>API利用状況</returns>
        Task<IApiUsage> GetApiUsageAsync();
    }
    
    /// <summary>
    /// クラウド言語情報
    /// </summary>
    public class CloudLanguage
    {
        /// <summary>
        /// 言語コード（ISO 639-1）
        /// </summary>
        public string LanguageCode { get; }
        
        /// <summary>
        /// 言語名（英語）
        /// </summary>
        public string Name { get; }
        
        /// <summary>
        /// 言語のネイティブ名
        /// </summary>
        public string NativeName { get; }
        
        /// <summary>
        /// 追加のプロパティ
        /// </summary>
        public IReadOnlyDictionary<string, string> Properties { get; }
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        public CloudLanguage(
            string languageCode,
            string name,
            string nativeName = null,
            IReadOnlyDictionary<string, string> properties = null)
        {
            LanguageCode = languageCode;
            Name = name;
            NativeName = nativeName ?? name;
            Properties = properties ?? new Dictionary<string, string>();
        }
    }
}
```

## 2. 認証と設定管理

```csharp
namespace Baketa.Core.Translation.Cloud
{
    /// <summary>
    /// クラウド認証情報のインターフェース
    /// </summary>
    public interface ICloudCredentials
    {
        /// <summary>
        /// 認証情報の種類
        /// </summary>
        CloudCredentialType CredentialType { get; }
        
        /// <summary>
        /// プロバイダーの種類
        /// </summary>
        CloudTranslationProviderType ProviderType { get; }
        
        /// <summary>
        /// 認証情報を暗号化する
        /// </summary>
        /// <returns>暗号化された認証情報</returns>
        string EncryptToString();
        
        /// <summary>
        /// 認証情報が有効かどうかを確認
        /// </summary>
        /// <returns>有効な場合はtrue</returns>
        bool IsValid();
    }
    
    /// <summary>
    /// 認証情報の種類
    /// </summary>
    public enum CloudCredentialType
    {
        /// <summary>
        /// APIキー
        /// </summary>
        ApiKey,
        
        /// <summary>
        /// クライアントIDとシークレット
        /// </summary>
        ClientCredentials,
        
        /// <summary>
        /// OAuth2トークン
        /// </summary>
        OAuth2Token,
        
        /// <summary>
        /// サービスアカウント
        /// </summary>
        ServiceAccount,
        
        /// <summary>
        /// カスタム認証
        /// </summary>
        Custom
    }
    
    /// <summary>
    /// APIキー認証情報
    /// </summary>
    public class ApiKeyCredentials : ICloudCredentials
    {
        /// <summary>
        /// APIキー
        /// </summary>
        public string ApiKey { get; }
        
        /// <inheritdoc/>
        public CloudCredentialType CredentialType => CloudCredentialType.ApiKey;
        
        /// <inheritdoc/>
        public CloudTranslationProviderType ProviderType { get; }
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        public ApiKeyCredentials(string apiKey, CloudTranslationProviderType providerType)
        {
            ApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            ProviderType = providerType;
        }
        
        /// <inheritdoc/>
        public string EncryptToString()
        {
            // 実際の実装では適切な暗号化を行う
            return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ProviderType}:{ApiKey}"));
        }
        
        /// <inheritdoc/>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(ApiKey);
        }
    }
    
    /// <summary>
    /// クラウド翻訳設定マネージャーのインターフェース
    /// </summary>
    public interface ICloudTranslationSettingsManager
    {
        /// <summary>
        /// 利用可能なすべてのプロバイダー設定を取得
        /// </summary>
        /// <returns>プロバイダー設定のコレクション</returns>
        Task<IReadOnlyList<ICloudProviderSettings>> GetAllProviderSettingsAsync();
        
        /// <summary>
        /// プロバイダー設定を取得
        /// </summary>
        /// <param name="providerType">プロバイダーの種類</param>
        /// <returns>プロバイダー設定</returns>
        Task<ICloudProviderSettings> GetProviderSettingsAsync(CloudTranslationProviderType providerType);
        
        /// <summary>
        /// プロバイダー設定を保存
        /// </summary>
        /// <param name="settings">プロバイダー設定</param>
        /// <returns>保存が成功したかどうか</returns>
        Task<bool> SaveProviderSettingsAsync(ICloudProviderSettings settings);
        
        /// <summary>
        /// プロバイダー設定を削除
        /// </summary>
        /// <param name="providerType">プロバイダーの種類</param>
        /// <returns>削除が成功したかどうか</returns>
        Task<bool> DeleteProviderSettingsAsync(CloudTranslationProviderType providerType);
        
        /// <summary>
        /// すべてのプロバイダー設定をリセット
        /// </summary>
        /// <returns>リセットが成功したかどうか</returns>
        Task<bool> ResetAllProviderSettingsAsync();
        
        /// <summary>
        /// 認証情報を保存
        /// </summary>
        /// <param name="credentials">認証情報</param>
        /// <param name="rememberCredentials">認証情報を記憶するかどうか</param>
        /// <returns>保存が成功したかどうか</returns>
        Task<bool> SaveCredentialsAsync(ICloudCredentials credentials, bool rememberCredentials = true);
        
        /// <summary>
        /// 認証情報を取得
        /// </summary>
        /// <param name="providerType">プロバイダーの種類</param>
        /// <returns>認証情報</returns>
        Task<ICloudCredentials> GetCredentialsAsync(CloudTranslationProviderType providerType);
    }
    
    /// <summary>
    /// クラウドプロバイダー設定のインターフェース
    /// </summary>
    public interface ICloudProviderSettings
    {
        /// <summary>
        /// プロバイダーの種類
        /// </summary>
        CloudTranslationProviderType ProviderType { get; }
        
        /// <summary>
        /// 表示名
        /// </summary>
        string DisplayName { get; set; }
        
        /// <summary>
        /// プロバイダーが有効かどうか
        /// </summary>
        bool IsEnabled { get; set; }
        
        /// <summary>
        /// 優先度（複数プロバイダーがある場合）
        /// </summary>
        int Priority { get; set; }
        
        /// <summary>
        /// APIエンドポイントURL
        /// </summary>
        string ApiEndpoint { get; set; }
        
        /// <summary>
        /// レート制限（1分あたりのリクエスト数）
        /// </summary>
        int? RateLimit { get; set; }
        
        /// <summary>
        /// 1ヶ月あたりの最大文字数制限
        /// </summary>
        long? MonthlyCharacterLimit { get; set; }
        
        /// <summary>
        /// 使用する認証タイプ
        /// </summary>
        CloudCredentialType CredentialType { get; set; }
        
        /// <summary>
        /// 認証情報を記憶するかどうか
        /// </summary>
        bool RememberCredentials { get; set; }
        
        /// <summary>
        /// 追加設定
        /// </summary>
        IReadOnlyDictionary<string, string> AdditionalSettings { get; }
        
        /// <summary>
        /// 追加設定を設定
        /// </summary>
        /// <param name="key">設定キー</param>
        /// <param name="value">設定値</param>
        void SetAdditionalSetting(string key, string value);
        
        /// <summary>
        /// 追加設定を取得
        /// </summary>
        /// <param name="key">設定キー</param>
        /// <param name="defaultValue">デフォルト値</param>
        /// <returns>設定値</returns>
        string GetAdditionalSetting(string key, string defaultValue = null);
    }
}
```

## 3. 翻訳結果と関連モデル

```csharp
namespace Baketa.Core.Translation.Cloud
{
    /// <summary>
    /// クラウド翻訳結果のインターフェース
    /// </summary>
    public interface ICloudTranslationResult
    {
        /// <summary>
        /// 翻訳元テキスト
        /// </summary>
        string SourceText { get; }
        
        /// <summary>
        /// 翻訳結果テキスト
        /// </summary>
        string TranslatedText { get; }
        
        /// <summary>
        /// 翻訳元言語
        /// </summary>
        string SourceLanguage { get; }
        
        /// <summary>
        /// 翻訳先言語
        /// </summary>
        string TargetLanguage { get; }
        
        /// <summary>
        /// プロバイダーの種類
        /// </summary>
        CloudTranslationProviderType ProviderType { get; }
        
        /// <summary>
        /// プロバイダー名
        /// </summary>
        string ProviderName { get; }
        
        /// <summary>
        /// 処理時間（ミリ秒）
        /// </summary>
        long ProcessingTimeMs { get; }
        
        /// <summary>
        /// リクエストID
        /// </summary>
        string RequestId { get; }
        
        /// <summary>
        /// 文字数
        /// </summary>
        int CharacterCount { get; }
        
        /// <summary>
        /// 翻訳の信頼度スコア（0.0～1.0）
        /// </summary>
        float? ConfidenceScore { get; }
        
        /// <summary>
        /// 代替翻訳候補
        /// </summary>
        IReadOnlyList<ICloudTranslationAlternative> Alternatives { get; }
        
        /// <summary>
        /// 翻訳がキャッシュから取得されたかどうか
        /// </summary>
        bool IsFromCache { get; }
        
        /// <summary>
        /// リクエスト日時
        /// </summary>
        DateTimeOffset RequestTimestamp { get; }
        
        /// <summary>
        /// プロバイダー固有のレスポンスデータ
        /// </summary>
        IReadOnlyDictionary<string, object> ProviderSpecificData { get; }
    }
    
    /// <summary>
    /// クラウド翻訳の代替候補
    /// </summary>
    public interface ICloudTranslationAlternative
    {
        /// <summary>
        /// 代替翻訳テキスト
        /// </summary>
        string Text { get; }
        
        /// <summary>
        /// 信頼度スコア（0.0～1.0）
        /// </summary>
        float? ConfidenceScore { get; }
        
        /// <summary>
        /// 追加情報
        /// </summary>
        IReadOnlyDictionary<string, object> AdditionalInfo { get; }
    }
    
    /// <summary>
    /// 言語検出結果
    /// </summary>
    public interface ILanguageDetectionResult
    {
        /// <summary>
        /// 検出された言語コード
        /// </summary>
        string LanguageCode { get; }
        
        /// <summary>
        /// 言語名（英語）
        /// </summary>
        string LanguageName { get; }
        
        /// <summary>
        /// 信頼度スコア（0.0～1.0）
        /// </summary>
        float ConfidenceScore { get; }
        
        /// <summary>
        /// 代替言語検出結果
        /// </summary>
        IReadOnlyList<ILanguageDetectionAlternative> Alternatives { get; }
        
        /// <summary>
        /// 検出に使用されたテキスト
        /// </summary>
        string DetectedText { get; }
        
        /// <summary>
        /// プロバイダーの種類
        /// </summary>
        CloudTranslationProviderType ProviderType { get; }
    }
    
    /// <summary>
    /// 代替言語検出結果
    /// </summary>
    public interface ILanguageDetectionAlternative
    {
        /// <summary>
        /// 言語コード
        /// </summary>
        string LanguageCode { get; }
        
        /// <summary>
        /// 言語名
        /// </summary>
        string LanguageName { get; }
        
        /// <summary>
        /// 信頼度スコア
        /// </summary>
        float ConfidenceScore { get; }
    }
    
    /// <summary>
    /// API使用状況
    /// </summary>
    public interface IApiUsage
    {
        /// <summary>
        /// プロバイダーの種類
        /// </summary>
        CloudTranslationProviderType ProviderType { get; }
        
        /// <summary>
        /// 使用文字数
        /// </summary>
        long CharactersUsed { get; }
        
        /// <summary>
        /// 使用文字数の上限
        /// </summary>
        long? CharacterLimit { get; }
        
        /// <summary>
        /// 使用率（%）
        /// </summary>
        float UsagePercentage { get; }
        
        /// <summary>
        /// リセット日
        /// </summary>
        DateTimeOffset? ResetDate { get; }
        
        /// <summary>
        /// リクエスト回数
        /// </summary>
        long RequestCount { get; }
        
        /// <summary>
        /// 残りリクエスト回数
        /// </summary>
        long? RemainingRequests { get; }
        
        /// <summary>
        /// 使用状況の取得日時
        /// </summary>
        DateTimeOffset Timestamp { get; }
    }
}
```

## 4. クラウド翻訳オプションとエラーハンドリング

```csharp
namespace Baketa.Core.Translation.Cloud
{
    /// <summary>
    /// クラウド翻訳オプション
    /// </summary>
    public class CloudTranslationOptions
    {
        /// <summary>
        /// 翻訳モード
        /// </summary>
        public TranslationMode Mode { get; set; } = TranslationMode.Standard;
        
        /// <summary>
        /// フォーマリティレベル
        /// </summary>
        public FormalityLevel Formality { get; set; } = FormalityLevel.Default;
        
        /// <summary>
        /// 翻訳する最大文字数（0は無制限）
        /// </summary>
        public int MaxCharacters { get; set; } = 0;
        
        /// <summary>
        /// キャッシュを使用するかどうか
        /// </summary>
        public bool UseCache { get; set; } = true;
        
        /// <summary>
        /// キャッシュの有効期間（秒）
        /// </summary>
        public int CacheTtlSeconds { get; set; } = 86400; // 24時間
        
        /// <summary>
        /// 生成する代替翻訳の数
        /// </summary>
        public int AlternativesCount { get; set; } = 0;
        
        /// <summary>
        /// 自動検出された言語を使用するかどうか
        /// </summary>
        public bool UseAutoDetectedLanguage { get; set; } = false;
        
        /// <summary>
        /// タグ処理モード
        /// </summary>
        public TagHandlingMode TagHandling { get; set; } = TagHandlingMode.None;
        
        /// <summary>
        /// コンテキスト情報
        /// </summary>
        public ITranslationContext Context { get; set; }
        
        /// <summary>
        /// タイムアウト（ミリ秒）
        /// </summary>
        public int TimeoutMs { get; set; } = 10000; // 10秒
        
        /// <summary>
        /// リトライ回数
        /// </summary>
        public int RetryCount { get; set; } = 3;
        
        /// <summary>
        /// リトライ間隔（ミリ秒）
        /// </summary>
        public int RetryIntervalMs { get; set; } = 1000; // 1秒
        
        /// <summary>
        /// プロバイダー固有のオプション
        /// </summary>
        public Dictionary<string, object> ProviderSpecificOptions { get; set; } = new Dictionary<string, object>();
    }
    
    /// <summary>
    /// 翻訳モード
    /// </summary>
    public enum TranslationMode
    {
        /// <summary>
        /// 標準翻訳
        /// </summary>
        Standard,
        
        /// <summary>
        /// 専門用語に最適化
        /// </summary>
        Technical,
        
        /// <summary>
        /// 会話に最適化
        /// </summary>
        Conversational,
        
        /// <summary>
        /// 文学的翻訳
        /// </summary>
        Literary,
        
        /// <summary>
        /// 簡略化された翻訳
        /// </summary>
        Simplified
    }
    
    /// <summary>
    /// フォーマリティレベル
    /// </summary>
    public enum FormalityLevel
    {
        /// <summary>
        /// デフォルト
        /// </summary>
        Default,
        
        /// <summary>
        /// カジュアル
        /// </summary>
        Informal,
        
        /// <summary>
        /// 標準
        /// </summary>
        Neutral,
        
        /// <summary>
        /// フォーマル
        /// </summary>
        Formal,
        
        /// <summary>
        /// 非常にフォーマル
        /// </summary>
        VeryFormal
    }
    
    /// <summary>
    /// タグ処理モード
    /// </summary>
    public enum TagHandlingMode
    {
        /// <summary>
        /// タグを無視
        /// </summary>
        None,
        
        /// <summary>
        /// XMLタグを保持
        /// </summary>
        Xml,
        
        /// <summary>
        /// HTMLタグを保持
        /// </summary>
        Html,
        
        /// <summary>
        /// Markdownを保持
        /// </summary>
        Markdown,
        
        /// <summary>
        /// カスタムタグを保持
        /// </summary>
        Custom
    }
    
    /// <summary>
    /// クラウド翻訳例外
    /// </summary>
    public class CloudTranslationException : Exception
    {
        /// <summary>
        /// プロバイダーの種類
        /// </summary>
        public CloudTranslationProviderType ProviderType { get; }
        
        /// <summary>
        /// エラーの種類
        /// </summary>
        public CloudTranslationErrorType ErrorType { get; }
        
        /// <summary>
        /// エラーコード
        /// </summary>
        public string ErrorCode { get; }
        
        /// <summary>
        /// リクエストID
        /// </summary>
        public string RequestId { get; }
        
        /// <summary>
        /// リトライ可能かどうか
        /// </summary>
        public bool IsRetryable { get; }
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        public CloudTranslationException(
            string message,
            CloudTranslationProviderType providerType,
            CloudTranslationErrorType errorType,
            string errorCode = null,
            string requestId = null,
            bool isRetryable = false,
            Exception innerException = null)
            : base(message, innerException)
        {
            ProviderType = providerType;
            ErrorType = errorType;
            ErrorCode = errorCode;
            RequestId = requestId;
            IsRetryable = isRetryable;
        }
    }
    
    /// <summary>
    /// クラウド翻訳エラーの種類
    /// </summary>
    public enum CloudTranslationErrorType
    {
        /// <summary>
        /// 認証エラー
        /// </summary>
        Authentication,
        
        /// <summary>
        /// 無効なリクエスト
        /// </summary>
        InvalidRequest,
        
        /// <summary>
        /// レート制限超過
        /// </summary>
        RateLimitExceeded,
        
        /// <summary>
        /// クォータ超過
        /// </summary>
        QuotaExceeded,
        
        /// <summary>
        /// サポートされていない言語
        /// </summary>
        UnsupportedLanguage,
        
        /// <summary>
        /// テキストが長すぎる
        /// </summary>
        TextTooLong,
        
        /// <summary>
        /// サービス一時停止
        /// </summary>
        ServiceUnavailable,
        
        /// <summary>
        /// タイムアウト
        /// </summary>
        Timeout,
        
        /// <summary>
        /// ネットワークエラー
        /// </summary>
        Network,
        
        /// <summary>
        /// 内部サーバーエラー
        /// </summary>
        ServerError,
        
        /// <summary>
        /// 予期しないエラー
        /// </summary>
        Unexpected
    }
}
```

## 5. クラウド翻訳サービスとファクトリー

```csharp
namespace Baketa.Core.Translation.Cloud
{
    /// <summary>
    /// クラウド翻訳サービスのインターフェース
    /// </summary>
    public interface ICloudTranslationService : ITranslationService
    {
        /// <summary>
        /// 利用可能なすべてのプロバイダーを取得
        /// </summary>
        /// <returns>利用可能なプロバイダーのコレクション</returns>
        Task<IReadOnlyList<ICloudTranslationProvider>> GetAvailableProvidersAsync();
        
        /// <summary>
        /// 有効なプロバイダーを取得
        /// </summary>
        /// <returns>有効なプロバイダーのコレクション</returns>
        Task<IReadOnlyList<ICloudTranslationProvider>> GetEnabledProvidersAsync();
        
        /// <summary>
        /// プロバイダーを取得
        /// </summary>
        /// <param name="providerType">プロバイダーの種類</param>
        /// <returns>プロバイダー</returns>
        Task<ICloudTranslationProvider> GetProviderAsync(CloudTranslationProviderType providerType);
        
        /// <summary>
        /// 指定された言語ペアに最適なプロバイダーを取得
        /// </summary>
        /// <param name="sourceLanguage">翻訳元言語</param>
        /// <param name="targetLanguage">翻訳先言語</param>
        /// <returns>最適なプロバイダー</returns>
        Task<ICloudTranslationProvider> GetBestProviderForLanguagePairAsync(
            string sourceLanguage, string targetLanguage);
        
        /// <summary>
        /// 翻訳リクエスト
        /// </summary>
        /// <param name="sourceText">翻訳元テキスト</param>
        /// <param name="sourceLanguage">翻訳元言語</param>
        /// <param name="targetLanguage">翻訳先言語</param>
        /// <param name="providerType">使用するプロバイダーの種類</param>
        /// <param name="options">翻訳オプション</param>
        /// <returns>翻訳結果</returns>
        Task<ICloudTranslationResult> TranslateWithProviderAsync(
            string sourceText,
            string sourceLanguage,
            string targetLanguage,
            CloudTranslationProviderType providerType,
            CloudTranslationOptions options = null);
        
        /// <summary>
        /// 言語の自動検出
        /// </summary>
        /// <param name="text">検出対象テキスト</param>
        /// <param name="providerType">使用するプロバイダーの種類</param>
        /// <returns>検出された言語情報</returns>
        Task<ILanguageDetectionResult> DetectLanguageAsync(
            string text, CloudTranslationProviderType? providerType = null);
    }
    
    /// <summary>
    /// クラウド翻訳プロバイダーファクトリーのインターフェース
    /// </summary>
    public interface ICloudTranslationProviderFactory
    {
        /// <summary>
        /// プロバイダーを作成
        /// </summary>
        /// <param name="providerType">プロバイダーの種類</param>
        /// <returns>プロバイダーインスタンス</returns>
        Task<ICloudTranslationProvider> CreateProviderAsync(CloudTranslationProviderType providerType);
        
        /// <summary>
        /// 認証済みプロバイダーを作成
        /// </summary>
        /// <param name="providerType">プロバイダーの種類</param>
        /// <param name="credentials">認証情報</param>
        /// <returns>認証済みプロバイダーインスタンス</returns>
        Task<ICloudTranslationProvider> CreateAuthenticatedProviderAsync(
            CloudTranslationProviderType providerType, ICloudCredentials credentials);
        
        /// <summary>
        /// 設定済みプロバイダーを作成
        /// </summary>
        /// <param name="settings">プロバイダー設定</param>
        /// <param name="credentials">認証情報</param>
        /// <returns>設定済みプロバイダーインスタンス</returns>
        Task<ICloudTranslationProvider> CreateConfiguredProviderAsync(
            ICloudProviderSettings settings, ICloudCredentials credentials = null);
        
        /// <summary>
        /// サポートされているプロバイダーの種類を取得
        /// </summary>
        /// <returns>サポートされているプロバイダーの種類のコレクション</returns>
        IReadOnlyList<CloudTranslationProviderType> GetSupportedProviderTypes();
    }
}
```

## 6. 代表的なプロバイダーの実装例

```csharp
namespace Baketa.Infrastructure.Translation.Cloud
{
    /// <summary>
    /// DeepL翻訳プロバイダーの実装
    /// </summary>
    public class DeepLTranslationProvider : ICloudTranslationProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<DeepLTranslationProvider> _logger;
        private readonly ICloudTranslationCache _cache;
        private readonly IEventAggregator _eventAggregator;
        
        private string _apiKey;
        private string _apiEndpoint;
        private bool _isFreePlan;
        
        /// <inheritdoc/>
        public string ProviderId => "deepl";
        
        /// <inheritdoc/>
        public string Name => "DeepL";
        
        /// <inheritdoc/>
        public CloudTranslationProviderType ProviderType => CloudTranslationProviderType.DeepL;
        
        /// <inheritdoc/>
        public bool IsAuthenticated => !string.IsNullOrEmpty(_apiKey);
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        public DeepLTranslationProvider(
            IHttpClientFactory httpClientFactory,
            ICloudTranslationCache cache,
            IEventAggregator eventAggregator,
            ILogger<DeepLTranslationProvider> logger)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // デフォルトのAPIエンドポイント
            _apiEndpoint = "https://api.deepl.com/v2";
        }
        
        /// <inheritdoc/>
        public async Task<bool> AuthenticateWithApiKeyAsync(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new ArgumentNullException(nameof(apiKey));
            }
            
            try
            {
                // APIキーを検証（無料版か有料版かを判定）
                _isFreePlan = apiKey.EndsWith(":fx");
                _apiEndpoint = _isFreePlan ? "https://api-free.deepl.com/v2" : "https://api.deepl.com/v2";
                
                // APIキーを設定
                _apiKey = apiKey;
                
                // 認証テスト（言語リストを取得）
                var languages = await GetSupportedTargetLanguagesAsync();
                
                return languages.Count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeepL認証に失敗しました");
                _apiKey = null;
                return false;
            }
        }
        
        /// <inheritdoc/>
        public async Task<bool> AuthenticateAsync(ICloudCredentials credentials)
        {
            if (credentials is ApiKeyCredentials apiKeyCredentials)
            {
                return await AuthenticateWithApiKeyAsync(apiKeyCredentials.ApiKey);
            }
            
            throw new NotSupportedException($"認証タイプ {credentials.CredentialType} はDeepLではサポートされていません");
        }
        
        /// <inheritdoc/>
        public async Task<IReadOnlyList<CloudLanguage>> GetSupportedSourceLanguagesAsync()
        {
            EnsureAuthenticated();
            
            try
            {
                var client = _httpClientFactory.CreateClient();
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_apiEndpoint}/languages?type=source");
                request.Headers.Add("Authorization", $"DeepL-Auth-Key {_apiKey}");
                
                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                var languagesArray = JsonSerializer.Deserialize<JsonElement[]>(content);
                
                var languages = new List<CloudLanguage>();
                
                foreach (var lang in languagesArray)
                {
                    var code = lang.GetProperty("language").GetString();
                    var name = lang.GetProperty("name").GetString();
                    
                    languages.Add(new CloudLanguage(code, name));
                }
                
                return languages;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeepLのソース言語リスト取得に失敗しました");
                throw CreateException("ソース言語リストの取得に失敗しました", ex, CloudTranslationErrorType.ServerError);
            }
        }
        
        /// <inheritdoc/>
        public async Task<IReadOnlyList<CloudLanguage>> GetSupportedTargetLanguagesAsync()
        {
            EnsureAuthenticated();
            
            try
            {
                var client = _httpClientFactory.CreateClient();
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_apiEndpoint}/languages?type=target");
                request.Headers.Add("Authorization", $"DeepL-Auth-Key {_apiKey}");
                
                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                var languagesArray = JsonSerializer.Deserialize<JsonElement[]>(content);
                
                var languages = new List<CloudLanguage>();
                
                foreach (var lang in languagesArray)
                {
                    var code = lang.GetProperty("language").GetString();
                    var name = lang.GetProperty("name").GetString();
                    
                    var properties = new Dictionary<string, string>();
                    if (lang.TryGetProperty("supports_formality", out var supportsFormality))
                    {
                        properties["supports_formality"] = supportsFormality.GetBoolean().ToString();
                    }
                    
                    languages.Add(new CloudLanguage(code, name, properties: properties));
                }
                
                return languages;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeepLのターゲット言語リスト取得に失敗しました");
                throw CreateException("ターゲット言語リストの取得に失敗しました", ex, CloudTranslationErrorType.ServerError);
            }
        }
        
        /// <inheritdoc/>
        public async Task<bool> SupportsLanguagePairAsync(string sourceLanguage, string targetLanguage)
        {
            var sourceLangs = await GetSupportedSourceLanguagesAsync();
            var targetLangs = await GetSupportedTargetLanguagesAsync();
            
            return sourceLangs.Any(l => l.LanguageCode.Equals(sourceLanguage, StringComparison.OrdinalIgnoreCase)) &&
                   targetLangs.Any(l => l.LanguageCode.Equals(targetLanguage, StringComparison.OrdinalIgnoreCase));
        }
        
        /// <inheritdoc/>
        public async Task<ICloudTranslationResult> TranslateAsync(
            string sourceText,
            string sourceLanguage,
            string targetLanguage,
            CloudTranslationOptions options = null)
        {
            options ??= new CloudTranslationOptions();
            EnsureAuthenticated();
            
            var requestId = Guid.NewGuid().ToString();
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                // イベント発行
                await _eventAggregator.PublishAsync(new CloudTranslationRequestedEvent(
                    requestId,
                    sourceText,
                    sourceLanguage,
                    targetLanguage,
                    ProviderType,
                    options?.Context));
                
                // キャッシュチェック
                if (options.UseCache)
                {
                    var cachedResult = await _cache.GetTranslationAsync(
                        sourceText, sourceLanguage, targetLanguage, ProviderType);
                    
                    if (cachedResult != null)
                    {
                        stopwatch.Stop();
                        
                        // キャッシュヒットイベント発行
                        await _eventAggregator.PublishAsync(new CloudTranslationCacheHitEvent(
                            requestId,
                            sourceText,
                            cachedResult.TranslatedText,
                            sourceLanguage,
                            targetLanguage,
                            ProviderType));
                        
                        return cachedResult;
                    }
                }
                
                // HTTPクライアント取得
                using var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromMilliseconds(options.TimeoutMs);
                
                // リクエスト作成
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiEndpoint}/translate");
                request.Headers.Add("Authorization", $"DeepL-Auth-Key {_apiKey}");
                
                var formData = new Dictionary<string, string>
                {
                    { "text", sourceText },
                    { "target_lang", MapLanguageCode(targetLanguage, isTarget: true) }
                };
                
                if (!string.IsNullOrEmpty(sourceLanguage) && !options.UseAutoDetectedLanguage)
                {
                    formData.Add("source_lang", MapLanguageCode(sourceLanguage, isTarget: false));
                }
                
                // フォーマリティ設定
                if (options.Formality != FormalityLevel.Default)
                {
                    formData.Add("formality", MapFormalityLevel(options.Formality));
                }
                
                // タグ処理設定
                if (options.TagHandling != TagHandlingMode.None)
                {
                    formData.Add("tag_handling", MapTagHandlingMode(options.TagHandling));
                }
                
                // 代替翻訳
                if (options.AlternativesCount > 0)
                {
                    formData.Add("alternatives", options.AlternativesCount.ToString());
                }
                
                request.Content = new FormUrlEncodedContent(formData);
                
                // リクエスト送信
                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                var responseJson = JsonDocument.Parse(content).RootElement;
                
                // レスポンス解析
                var translations = responseJson.GetProperty("translations");
                var translation = translations[0];
                
                var translatedText = translation.GetProperty("text").GetString();
                var detectedLanguage = translation.TryGetProperty("detected_source_language", out var detected)
                    ? detected.GetString()
                    : sourceLanguage;
                
                // 代替翻訳の解析
                var alternatives = new List<ICloudTranslationAlternative>();
                if (translation.TryGetProperty("alternatives", out var alts))
                {
                    foreach (var alt in alts.EnumerateArray())
                    {
                        var altText = alt.GetProperty("text").GetString();
                        
                        alternatives.Add(new CloudTranslationAlternative
                        {
                            Text = altText
                        });
                    }
                }
                
                stopwatch.Stop();
                
                var result = new CloudTranslationResult
                {
                    RequestId = requestId,
                    SourceText = sourceText,
                    TranslatedText = translatedText,
                    SourceLanguage = detectedLanguage,
                    TargetLanguage = targetLanguage,
                    ProviderType = ProviderType,
                    ProviderName = Name,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                    CharacterCount = sourceText.Length,
                    RequestTimestamp = DateTimeOffset.UtcNow,
                    Alternatives = alternatives,
                    IsFromCache = false
                };
                
                // キャッシュに保存
                if (options.UseCache)
                {
                    await _cache.StoreTranslationAsync(result, TimeSpan.FromSeconds(options.CacheTtlSeconds));
                }
                
                // 完了イベント発行
                await _eventAggregator.PublishAsync(new CloudTranslationCompletedEvent(
                    requestId,
                    sourceText,
                    translatedText,
                    detectedLanguage,
                    targetLanguage,
                    ProviderType,
                    stopwatch.ElapsedMilliseconds,
                    options?.Context));
                
                return result;
            }
            catch (HttpRequestException ex)
            {
                stopwatch.Stop();
                
                var errorType = ex.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => CloudTranslationErrorType.Authentication,
                    HttpStatusCode.BadRequest => CloudTranslationErrorType.InvalidRequest,
                    HttpStatusCode.TooManyRequests => CloudTranslationErrorType.RateLimitExceeded,
                    HttpStatusCode.RequestEntityTooLarge => CloudTranslationErrorType.TextTooLong,
                    HttpStatusCode.ServiceUnavailable => CloudTranslationErrorType.ServiceUnavailable,
                    _ => CloudTranslationErrorType.ServerError
                };
                
                var isRetryable = errorType is CloudTranslationErrorType.RateLimitExceeded 
                                  or CloudTranslationErrorType.ServiceUnavailable;
                
                var exception = CreateException(
                    $"DeepL翻訳リクエストに失敗しました: {ex.Message}",
                    ex, errorType, isRetryable: isRetryable);
                
                // 失敗イベント発行
                await _eventAggregator.PublishAsync(new CloudTranslationFailedEvent(
                    requestId,
                    sourceText,
                    sourceLanguage,
                    targetLanguage,
                    ProviderType,
                    exception.Message,
                    errorType));
                
                throw exception;
            }
            catch (TaskCanceledException ex)
            {
                stopwatch.Stop();
                
                var exception = CreateException(
                    "DeepL翻訳リクエストがタイムアウトしました",
                    ex, CloudTranslationErrorType.Timeout, isRetryable: true);
                
                // 失敗イベント発行
                await _eventAggregator.PublishAsync(new CloudTranslationFailedEvent(
                    requestId,
                    sourceText,
                    sourceLanguage,
                    targetLanguage,
                    ProviderType,
                    exception.Message,
                    CloudTranslationErrorType.Timeout));
                
                throw exception;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                
                var exception = CreateException(
                    $"DeepL翻訳処理中に予期しないエラーが発生しました: {ex.Message}",
                    ex, CloudTranslationErrorType.Unexpected);
                
                // 失敗イベント発行
                await _eventAggregator.PublishAsync(new CloudTranslationFailedEvent(
                    requestId,
                    sourceText,
                    sourceLanguage,
                    targetLanguage,
                    ProviderType,
                    exception.Message,
                    CloudTranslationErrorType.Unexpected));
                
                throw exception;
            }
        }
        
        /// <inheritdoc/>
        public async Task<IReadOnlyList<ICloudTranslationResult>> TranslateBatchAsync(
            IReadOnlyList<string> texts,
            string sourceLanguage,
            string targetLanguage,
            CloudTranslationOptions options = null)
        {
            if (texts == null || texts.Count == 0)
            {
                return new List<ICloudTranslationResult>();
            }
            
            options ??= new CloudTranslationOptions();
            
            if (texts.Count == 1)
            {
                var result = await TranslateAsync(texts[0], sourceLanguage, targetLanguage, options);
                return new[] { result };
            }
            
            // 実際のバッチ処理はDeepL APIの制限に合わせて実装する必要がある
            // ここでは簡略化のため、並列処理で各テキストを翻訳
            
            var tasks = texts.Select(text => TranslateAsync(text, sourceLanguage, targetLanguage, options)).ToList();
            return await Task.WhenAll(tasks);
        }
        
        /// <inheritdoc/>
        public async Task<ILanguageDetectionResult> DetectLanguageAsync(string text)
        {
            // DeepL APIは明示的な言語検出エンドポイントを提供していないため、
            // 翻訳リクエストを送信して検出された言語を取得
            
            EnsureAuthenticated();
            
            try
            {
                var client = _httpClientFactory.CreateClient();
                var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiEndpoint}/translate");
                request.Headers.Add("Authorization", $"DeepL-Auth-Key {_apiKey}");
                
                var formData = new Dictionary<string, string>
                {
                    { "text", text },
                    { "target_lang", "EN" } // 任意のターゲット言語（英語を使用）
                };
                
                request.Content = new FormUrlEncodedContent(formData);
                
                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                var responseJson = JsonDocument.Parse(content).RootElement;
                
                var translations = responseJson.GetProperty("translations");
                var translation = translations[0];
                
                var detectedLanguage = translation.GetProperty("detected_source_language").GetString();
                
                // 言語名を取得
                var languages = await GetSupportedSourceLanguagesAsync();
                var languageName = languages.FirstOrDefault(l => 
                    l.LanguageCode.Equals(detectedLanguage, StringComparison.OrdinalIgnoreCase))?.Name ?? detectedLanguage;
                
                return new LanguageDetectionResult
                {
                    LanguageCode = detectedLanguage,
                    LanguageName = languageName,
                    ConfidenceScore = 1.0f, // DeepLは信頼度スコアを提供していない
                    DetectedText = text,
                    ProviderType = ProviderType,
                    Alternatives = new List<ILanguageDetectionAlternative>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeepLでの言語検出に失敗しました");
                throw CreateException("言語検出に失敗しました", ex, CloudTranslationErrorType.ServerError);
            }
        }
        
        /// <inheritdoc/>
        public async Task<IApiUsage> GetApiUsageAsync()
        {
            EnsureAuthenticated();
            
            try
            {
                var client = _httpClientFactory.CreateClient();
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_apiEndpoint}/usage");
                request.Headers.Add("Authorization", $"DeepL-Auth-Key {_apiKey}");
                
                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                var usageJson = JsonDocument.Parse(content).RootElement;
                
                var characterCount = usageJson.GetProperty("character_count").GetInt64();
                var characterLimit = usageJson.GetProperty("character_limit").GetInt64();
                
                var usage = new ApiUsage
                {
                    ProviderType = ProviderType,
                    CharactersUsed = characterCount,
                    CharacterLimit = characterLimit,
                    UsagePercentage = characterLimit > 0 ? (float)characterCount / characterLimit * 100 : 0,
                    RequestCount = 0, // DeepLはリクエスト回数を提供していない
                    Timestamp = DateTimeOffset.UtcNow
                };
                
                return usage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeepLの使用状況取得に失敗しました");
                throw CreateException("使用状況の取得に失敗しました", ex, CloudTranslationErrorType.ServerError);
            }
        }
        
        /// <summary>
        /// 認証が完了しているか確認
        /// </summary>
        private void EnsureAuthenticated()
        {
            if (!IsAuthenticated)
            {
                throw new CloudTranslationException(
                    "DeepL APIが認証されていません",
                    ProviderType,
                    CloudTranslationErrorType.Authentication);
            }
        }
        
        /// <summary>
        /// 例外を作成
        /// </summary>
        private CloudTranslationException CreateException(
            string message,
            Exception innerException,
            CloudTranslationErrorType errorType,
            string errorCode = null,
            string requestId = null,
            bool isRetryable = false)
        {
            return new CloudTranslationException(
                message,
                ProviderType,
                errorType,
                errorCode,
                requestId,
                isRetryable,
                innerException);
        }
        
        /// <summary>
        /// 言語コードを変換
        /// </summary>
        private string MapLanguageCode(string code, bool isTarget)
        {
            // DeepLの言語コードへの変換（必要に応じて）
            return code.ToUpperInvariant();
        }
        
        /// <summary>
        /// フォーマリティレベルを変換
        /// </summary>
        private string MapFormalityLevel(FormalityLevel formality)
        {
            return formality switch
            {
                FormalityLevel.Informal => "less",
                FormalityLevel.Formal => "more",
                FormalityLevel.VeryFormal => "more",
                _ => "default"
            };
        }
        
        /// <summary>
        /// タグ処理モードを変換
        /// </summary>
        private string MapTagHandlingMode(TagHandlingMode tagHandling)
        {
            return tagHandling switch
            {
                TagHandlingMode.Xml => "xml",
                TagHandlingMode.Html => "html",
                _ => null
            };
        }
    }
    
    /// <summary>
    /// クラウド翻訳結果の実装
    /// </summary>
    public class CloudTranslationResult : ICloudTranslationResult
    {
        /// <inheritdoc/>
        public string SourceText { get; set; }
        
        /// <inheritdoc/>
        public string TranslatedText { get; set; }
        
        /// <inheritdoc/>
        public string SourceLanguage { get; set; }
        
        /// <inheritdoc/>
        public string TargetLanguage { get; set; }
        
        /// <inheritdoc/>
        public CloudTranslationProviderType ProviderType { get; set; }
        
        /// <inheritdoc/>
        public string ProviderName { get; set; }
        
        /// <inheritdoc/>
        public long ProcessingTimeMs { get; set; }
        
        /// <inheritdoc/>
        public string RequestId { get; set; }
        
        /// <inheritdoc/>
        public int CharacterCount { get; set; }
        
        /// <inheritdoc/>
        public float? ConfidenceScore { get; set; }
        
        /// <inheritdoc/>
        public IReadOnlyList<ICloudTranslationAlternative> Alternatives { get; set; } = new List<ICloudTranslationAlternative>();
        
        /// <inheritdoc/>
        public bool IsFromCache { get; set; }
        
        /// <inheritdoc/>
        public DateTimeOffset RequestTimestamp { get; set; }
        
        /// <inheritdoc/>
        public IReadOnlyDictionary<string, object> ProviderSpecificData { get; set; } = new Dictionary<string, object>();
    }
    
    /// <summary>
    /// クラウド翻訳の代替候補の実装
    /// </summary>
    public class CloudTranslationAlternative : ICloudTranslationAlternative
    {
        /// <inheritdoc/>
        public string Text { get; set; }
        
        /// <inheritdoc/>
        public float? ConfidenceScore { get; set; }
        
        /// <inheritdoc/>
        public IReadOnlyDictionary<string, object> AdditionalInfo { get; set; } = new Dictionary<string, object>();
    }
    
    /// <summary>
    /// 言語検出結果の実装
    /// </summary>
    public class LanguageDetectionResult : ILanguageDetectionResult
    {
        /// <inheritdoc/>
        public string LanguageCode { get; set; }
        
        /// <inheritdoc/>
        public string LanguageName { get; set; }
        
        /// <inheritdoc/>
        public float ConfidenceScore { get; set; }
        
        /// <inheritdoc/>
        public IReadOnlyList<ILanguageDetectionAlternative> Alternatives { get; set; } = new List<ILanguageDetectionAlternative>();
        
        /// <inheritdoc/>
        public string DetectedText { get; set; }
        
        /// <inheritdoc/>
        public CloudTranslationProviderType ProviderType { get; set; }
    }
    
    /// <summary>
    /// API使用状況の実装
    /// </summary>
    public class ApiUsage : IApiUsage
    {
        /// <inheritdoc/>
        public CloudTranslationProviderType ProviderType { get; set; }
        
        /// <inheritdoc/>
        public long CharactersUsed { get; set; }
        
        /// <inheritdoc/>
        public long? CharacterLimit { get; set; }
        
        /// <inheritdoc/>
        public float UsagePercentage { get; set; }
        
        /// <inheritdoc/>
        public DateTimeOffset? ResetDate { get; set; }
        
        /// <inheritdoc/>
        public long RequestCount { get; set; }
        
        /// <inheritdoc/>
        public long? RemainingRequests { get; set; }
        
        /// <inheritdoc/>
        public DateTimeOffset Timestamp { get; set; }
    }
}
```

## 7. クラウド翻訳キャッシュ

```csharp
namespace Baketa.Core.Translation.Cloud
{
    /// <summary>
    /// クラウド翻訳キャッシュのインターフェース
    /// </summary>
    public interface ICloudTranslationCache
    {
        /// <summary>
        /// 翻訳結果の取得
        /// </summary>
        /// <param name="sourceText">翻訳元テキスト</param>
        /// <param name="sourceLanguage">翻訳元言語</param>
        /// <param name="targetLanguage">翻訳先言語</param>
        /// <param name="providerType">プロバイダーの種類</param>
        /// <returns>キャッシュされた翻訳結果、見つからない場合はnull</returns>
        Task<ICloudTranslationResult> GetTranslationAsync(
            string sourceText,
            string sourceLanguage,
            string targetLanguage,
            CloudTranslationProviderType providerType);
        
        /// <summary>
        /// 翻訳結果の保存
        /// </summary>
        /// <param name="result">翻訳結果</param>
        /// <param name="expiration">有効期限</param>
        /// <returns>保存が成功したかどうか</returns>
        Task<bool> StoreTranslationAsync(
            ICloudTranslationResult result,
            TimeSpan expiration);
        
        /// <summary>
        /// 翻訳結果の削除
        /// </summary>
        /// <param name="sourceText">翻訳元テキスト</param>
        /// <param name="sourceLanguage">翻訳元言語</param>
        /// <param name="targetLanguage">翻訳先言語</param>
        /// <param name="providerType">プロバイダーの種類</param>
        /// <returns>削除が成功したかどうか</returns>
        Task<bool> RemoveTranslationAsync(
            string sourceText,
            string sourceLanguage,
            string targetLanguage,
            CloudTranslationProviderType providerType);
        
        /// <summary>
        /// キャッシュのクリア
        /// </summary>
        /// <param name="providerType">特定のプロバイダーのキャッシュをクリア（nullの場合はすべて）</param>
        /// <returns>クリアされたエントリ数</returns>
        Task<int> ClearCacheAsync(CloudTranslationProviderType? providerType = null);
        
        /// <summary>
        /// 期限切れエントリのクリーンアップ
        /// </summary>
        /// <returns>削除されたエントリ数</returns>
        Task<int> CleanupExpiredEntriesAsync();
        
        /// <summary>
        /// キャッシュ統計情報の取得
        /// </summary>
        /// <returns>キャッシュ統計情報</returns>
        Task<ICacheStatistics> GetStatisticsAsync();
    }
    
    /// <summary>
    /// キャッシュ統計情報のインターフェース
    /// </summary>
    public interface ICacheStatistics
    {
        /// <summary>
        /// エントリ数
        /// </summary>
        int EntryCount { get; }
        
        /// <summary>
        /// メモリ使用量（バイト単位）
        /// </summary>
        long MemoryUsage { get; }
        
        /// <summary>
        /// ヒット数
        /// </summary>
        long HitCount { get; }
        
        /// <summary>
        /// ミス数
        /// </summary>
        long MissCount { get; }
        
        /// <summary>
        /// ヒット率（%）
        /// </summary>
        float HitRate { get; }
        
        /// <summary>
        /// プロバイダーごとのエントリ数
        /// </summary>
        IReadOnlyDictionary<CloudTranslationProviderType, int> EntriesByProvider { get; }
        
        /// <summary>
        /// 言語ペアごとのエントリ数
        /// </summary>
        IReadOnlyDictionary<string, int> EntriesByLanguagePair { get; }
    }
}
```