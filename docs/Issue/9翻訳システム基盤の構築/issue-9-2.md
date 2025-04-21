# Issue 9-2: WebAPI翻訳エンジンの実装

## 概要
外部の翻訳WebAPIを利用する翻訳エンジンを実装します。これにより、複数の翻訳APIサービスに対応し、高品質な翻訳結果をユーザーに提供します。

## 目的・理由
WebAPIベースの翻訳エンジンを実装する理由は以下の通りです：

1. ローカルモデルよりも高品質な翻訳結果を提供できる
2. 複数の翻訳APIサービスに対応することで選択肢を増やせる
3. APIごとの特性（翻訳精度、対応言語、コスト）を活かした最適化が可能
4. 将来的な翻訳APIの進化に柔軟に対応できる

## 詳細
- WebAPI翻訳エンジンの基盤クラスの設計と実装
- 複数のAPIプロバイダー（Google、DeepL、Microsoft等）への対応
- API通信処理と認証メカニズムの実装
- エラーハンドリングとリトライロジックの実装

## タスク分解
- [ ] WebAPI基盤クラスの設計
  - [ ] `WebApiTranslationEngineBase`抽象クラスの設計と実装
  - [ ] HTTP通信処理の共通実装
  - [ ] 認証処理の抽象化
  - [ ] レート制限対応の実装
- [ ] Google翻訳API実装
  - [ ] `GoogleTranslationEngine`クラスの実装
  - [ ] APIリクエスト/レスポンス構造の実装
  - [ ] 認証処理の実装
  - [ ] 言語コード変換の実装
- [ ] DeepL翻訳API実装
  - [ ] `DeepLTranslationEngine`クラスの実装
  - [ ] APIリクエスト/レスポンス構造の実装
  - [ ] 認証処理の実装
  - [ ] 言語コード変換の実装
- [ ] Microsoft翻訳API実装
  - [ ] `MicrosoftTranslationEngine`クラスの実装
  - [ ] APIリクエスト/レスポンス構造の実装
  - [ ] 認証処理の実装
  - [ ] 言語コード変換の実装
- [ ] バッチ処理の最適化
  - [ ] 効率的なバッチ処理の実装
  - [ ] バッチサイズの最適化
- [ ] エラーハンドリングとリトライロジック
  - [ ] 一時的エラーの検出
  - [ ] 指数バックオフによるリトライ
  - [ ] フォールバック処理の実装
- [ ] 単体テストの実装
  - [ ] モックHTTPクライアントの実装
  - [ ] 各APIレスポンスのモック
  - [ ] エラーケースのテスト

## インターフェース設計案
```csharp
namespace Baketa.Translation.WebApi
{
    /// <summary>
    /// WebAPI翻訳エンジンの基底クラス
    /// </summary>
    public abstract class WebApiTranslationEngineBase : ITranslationEngine
    {
        /// <summary>
        /// HTTPクライアント
        /// </summary>
        protected readonly HttpClient _httpClient;
        
        /// <summary>
        /// ロガー
        /// </summary>
        protected readonly ILogger? _logger;
        
        /// <summary>
        /// 認証情報
        /// </summary>
        protected readonly IApiAuthentication _authentication;
        
        /// <summary>
        /// レート制限マネージャー
        /// </summary>
        protected readonly IRateLimitManager _rateLimitManager;
        
        /// <summary>
        /// 新しいWebAPI翻訳エンジンのインスタンスを初期化します
        /// </summary>
        /// <param name="httpClient">HTTPクライアント</param>
        /// <param name="authentication">API認証情報</param>
        /// <param name="rateLimitManager">レート制限マネージャー</param>
        /// <param name="logger">ロガー</param>
        protected WebApiTranslationEngineBase(
            HttpClient httpClient,
            IApiAuthentication authentication,
            IRateLimitManager? rateLimitManager = null,
            ILogger? logger = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
            _rateLimitManager = rateLimitManager ?? new DefaultRateLimitManager();
            _logger = logger;
        }
        
        /// <summary>
        /// エンジン名
        /// </summary>
        public abstract string Name { get; }
        
        /// <summary>
        /// エンジンの説明
        /// </summary>
        public abstract string Description { get; }
        
        /// <summary>
        /// エンジンがオンライン接続を必要とするかどうか
        /// </summary>
        public virtual bool RequiresNetwork => true;
        
        /// <summary>
        /// APIエンドポイントのベースURL
        /// </summary>
        protected abstract string BaseUrl { get; }
        
        /// <summary>
        /// 翻訳APIエンドポイントのパス
        /// </summary>
        protected abstract string TranslateEndpoint { get; }
        
        /// <summary>
        /// 言語一覧APIエンドポイントのパス
        /// </summary>
        protected abstract string LanguagesEndpoint { get; }
        
        /// <summary>
        /// サポートしている言語ペアを取得します
        /// </summary>
        public abstract Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync();
        
        /// <summary>
        /// 指定された言語ペアをサポートしているかどうかを確認します
        /// </summary>
        public abstract Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair);
        
        /// <summary>
        /// テキストを翻訳します
        /// </summary>
        public abstract Task<TranslationResponse> TranslateAsync(
            TranslationRequest request, 
            CancellationToken cancellationToken = default);
            
        /// <summary>
        /// 複数のテキストをバッチ翻訳します
        /// </summary>
        public abstract Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
            IReadOnlyList<TranslationRequest> requests, 
            CancellationToken cancellationToken = default);
            
        /// <summary>
        /// エンジンの準備状態を確認します
        /// </summary>
        public abstract Task<bool> IsReadyAsync();
        
        /// <summary>
        /// エンジンを初期化します
        /// </summary>
        public abstract Task<bool> InitializeAsync();
        
        /// <summary>
        /// リソースを解放します
        /// </summary>
        public virtual void Dispose()
        {
            // リソース解放処理
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// API言語コードとBaketa言語コードを変換します
        /// </summary>
        /// <param name="apiLanguageCode">API言語コード</param>
        /// <returns>Baketa言語オブジェクト</returns>
        protected abstract Language ConvertFromApiLanguage(string apiLanguageCode);
        
        /// <summary>
        /// Baketa言語コードとAPI言語コードを変換します
        /// </summary>
        /// <param name="language">Baketa言語オブジェクト</param>
        /// <returns>API言語コード</returns>
        protected abstract string ConvertToApiLanguage(Language language);
        
        /// <summary>
        /// APIリクエストを送信します
        /// </summary>
        /// <typeparam name="TResponse">レスポンス型</typeparam>
        /// <param name="endpoint">エンドポイント</param>
        /// <param name="method">HTTPメソッド</param>
        /// <param name="content">リクエスト内容</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>APIレスポンス</returns>
        protected async Task<TResponse> SendRequestAsync<TResponse>(
            string endpoint,
            HttpMethod method,
            HttpContent? content = null,
            CancellationToken cancellationToken = default)
        {
            // API通信の共通処理実装
            throw new NotImplementedException();
        }
        
        /// <summary>
        /// APIリクエストを認証情報付きで準備します
        /// </summary>
        /// <param name="endpoint">エンドポイント</param>
        /// <param name="method">HTTPメソッド</param>
        /// <returns>認証情報付きのリクエスト</returns>
        protected virtual HttpRequestMessage PrepareRequest(string endpoint, HttpMethod method)
        {
            // 認証情報の追加処理
            throw new NotImplementedException();
        }
        
        /// <summary>
        /// レスポンスからレート制限情報を抽出します
        /// </summary>
        /// <param name="response">HTTPレスポンス</param>
        protected virtual void ProcessRateLimitHeaders(HttpResponseMessage response)
        {
            // レート制限ヘッダー処理
            throw new NotImplementedException();
        }
    }
    
    /// <summary>
    /// Google翻訳APIエンジン
    /// </summary>
    public class GoogleTranslationEngine : WebApiTranslationEngineBase
    {
        /// <summary>
        /// 新しいGoogle翻訳エンジンのインスタンスを初期化します
        /// </summary>
        public GoogleTranslationEngine(
            HttpClient httpClient,
            IApiAuthentication authentication,
            IRateLimitManager? rateLimitManager = null,
            ILogger? logger = null)
            : base(httpClient, authentication, rateLimitManager, logger)
        {
        }
        
        /// <override />
        public override string Name => "Google Translate";
        
        /// <override />
        public override string Description => "Google Cloud Translation API based translation engine";
        
        /// <override />
        protected override string BaseUrl => "https://translation.googleapis.com/";
        
        /// <override />
        protected override string TranslateEndpoint => "v3/projects/{0}/translateText";
        
        /// <override />
        protected override string LanguagesEndpoint => "v3/projects/{0}/supportedLanguages";
        
        // 実装メソッド
    }
    
    /// <summary>
    /// DeepL翻訳APIエンジン
    /// </summary>
    public class DeepLTranslationEngine : WebApiTranslationEngineBase
    {
        /// <summary>
        /// 新しいDeepL翻訳エンジンのインスタンスを初期化します
        /// </summary>
        public DeepLTranslationEngine(
            HttpClient httpClient,
            IApiAuthentication authentication,
            IRateLimitManager? rateLimitManager = null,
            ILogger? logger = null)
            : base(httpClient, authentication, rateLimitManager, logger)
        {
        }
        
        /// <override />
        public override string Name => "DeepL";
        
        /// <override />
        public override string Description => "DeepL API based translation engine";
        
        /// <override />
        protected override string BaseUrl => "https://api.deepl.com/";
        
        /// <override />
        protected override string TranslateEndpoint => "v2/translate";
        
        /// <override />
        protected override string LanguagesEndpoint => "v2/languages";
        
        // 実装メソッド
    }
    
    /// <summary>
    /// API認証情報インターフェース
    /// </summary>
    public interface IApiAuthentication
    {
        /// <summary>
        /// 認証タイプ
        /// </summary>
        ApiAuthenticationType AuthType { get; }
        
        /// <summary>
        /// リクエストに認証情報を適用します
        /// </summary>
        /// <param name="request">HTTPリクエスト</param>
        void ApplyToRequest(HttpRequestMessage request);
        
        /// <summary>
        /// 認証情報が有効かどうかを確認します
        /// </summary>
        /// <returns>有効であればtrue</returns>
        Task<bool> ValidateAsync();
    }
    
    /// <summary>
    /// APIレート制限管理インターフェース
    /// </summary>
    public interface IRateLimitManager
    {
        /// <summary>
        /// レート制限情報を更新します
        /// </summary>
        /// <param name="remainingRequests">残りリクエスト数</param>
        /// <param name="resetTimeUtc">リセット時刻（UTC）</param>
        /// <param name="totalLimit">総リクエスト制限</param>
        void UpdateRateLimitInfo(int remainingRequests, DateTime resetTimeUtc, int totalLimit);
        
        /// <summary>
        /// リクエスト可能かどうかを確認します
        /// </summary>
        /// <returns>リクエスト可能であればtrue</returns>
        bool CanMakeRequest();
        
        /// <summary>
        /// リクエスト可能になるまで待機します
        /// </summary>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        Task WaitForAvailabilityAsync(CancellationToken cancellationToken = default);
    }
    
    /// <summary>
    /// API認証タイプ
    /// </summary>
    public enum ApiAuthenticationType
    {
        None,
        ApiKey,
        OAuth2,
        BasicAuth
    }
}
```

## 実装上の注意点
- API通信時のセキュリティに配慮し、APIキーなどの機密情報は適切に管理する
- レート制限に対応し、APIの使用量を最適化する
- タイムアウトやネットワークエラーに対する適切なリトライ処理を実装する
- WebAPI通信でのメモリ効率を考慮し、大きなデータの処理に対応する
- 認証情報の管理を安全に行い、露出させないようにする
- バックグラウンドスレッドでの実行を考慮し、スレッドセーフな設計にする

## 関連Issue/参考
- 親Issue: #9 翻訳システム基盤の構築
- 依存Issue: #9-1 翻訳エンジンインターフェースの設計と実装
- 参照: E:\dev\Baketa\docs\3-architecture\translation\web-api-translation.md
- 参照: E:\dev\Baketa\docs\4-integrations\translation-apis.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (4. エラー処理と例外)

## マイルストーン
マイルストーン3: 翻訳とUI

## ラベル
- `type: feature`
- `priority: high`
- `component: translation`
