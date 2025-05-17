using System;

#pragma warning disable CA1805 // 参照型の初期化は処理系によって自動的に行われる

namespace Baketa.Core.Translation.Common
{
    /// <summary>
    /// 翻訳オプション
    /// </summary>
    public class TranslationOptions
    {
        // コンストラクタで初期化
        public TranslationOptions()
        {
            WebApiOptions = new WebApiTranslationOptions();
            ManagementOptions = new TranslationManagementOptions();
            CacheOptions = new TranslationCacheOptions();
            EventOptions = new TranslationEventOptions();
            PipelineOptions = new TranslationPipelineOptions();
        }
        
        /// <summary>
        /// WebAPI翻訳オプション
        /// </summary>
        public WebApiTranslationOptions WebApiOptions { get; set; }
        
        /// <summary>
        /// 翻訳結果管理オプション
        /// </summary>
        public TranslationManagementOptions ManagementOptions { get; set; }
        
        /// <summary>
        /// 翻訳キャッシュオプション
        /// </summary>
        public TranslationCacheOptions CacheOptions { get; set; }
        
        /// <summary>
        /// 翻訳イベントオプション
        /// </summary>
        public TranslationEventOptions EventOptions { get; set; }
        
        /// <summary>
        /// 翻訳パイプラインオプション
        /// </summary>
        public TranslationPipelineOptions PipelineOptions { get; set; }
    }
    
    /// <summary>
    /// WebAPI翻訳オプション
    /// </summary>
    public class WebApiTranslationOptions
    {
        /// <summary>
        /// 最大リクエスト回数
        /// </summary>
        public int MaxRetries { get; set; } = 3;
        
        /// <summary>
        /// タイムアウト（秒）
        /// </summary>
        public int TimeoutSeconds { get; set; } = 10;
        
        /// <summary>
        /// クライアント証明書の検証をスキップするかどうか
        /// </summary>
        public bool SkipCertificateValidation { get; set; } = false;
        
        /// <summary>
        /// リトライ間隔（秒）
        /// </summary>
        public int RetryIntervalSeconds { get; set; } = 1;
        
        /// <summary>
        /// プロキシ設定を使用するかどうか
        /// </summary>
        public bool UseProxy { get; set; } = false;
        
        /// <summary>
        /// プロキシURL
        /// </summary>
        public Uri? ProxyUri { get; set; }
    }
    
    /// <summary>
    /// 翻訳結果管理オプション
    /// </summary>
    public class TranslationManagementOptions
    {
        /// <summary>
        /// 最大保存レコード数
        /// </summary>
        public int MaxStoredRecords { get; set; } = 10000;
        
        /// <summary>
        /// データベースパス
        /// </summary>
        public string DatabasePath { get; set; } = "data/translations.db";
        
        /// <summary>
        /// 自動エクスポートを有効にするかどうか
        /// </summary>
        public bool EnableAutoExport { get; set; } = false;
        
        /// <summary>
        /// 自動エクスポート間隔（時間）
        /// </summary>
        public int AutoExportIntervalHours { get; set; } = 24;
        
        /// <summary>
        /// ユーザー編集を優先するかどうか
        /// </summary>
        public bool PrioritizeUserEdits { get; set; } = true;
    }
    
    /// <summary>
    /// 翻訳キャッシュオプション
    /// </summary>
    public class TranslationCacheOptions
    {
        /// <summary>
        /// メモリキャッシュを有効にするかどうか
        /// </summary>
        public bool EnableMemoryCache { get; set; } = true;
        
        /// <summary>
        /// メモリキャッシュのサイズ（アイテム数）
        /// </summary>
        public int MemoryCacheSize { get; set; } = 1000;
        
        /// <summary>
        /// 永続化キャッシュを有効にするかどうか
        /// </summary>
        public bool EnablePersistentCache { get; set; } = true;
        
        /// <summary>
        /// 永続化キャッシュのサイズ（アイテム数）
        /// </summary>
        public int PersistentCacheSize { get; set; } = 10000;
        
        /// <summary>
        /// デフォルトの有効期限（時間）
        /// </summary>
        public int DefaultExpirationHours { get; set; } = 72;
    }
    
    /// <summary>
    /// 翻訳イベントオプション
    /// </summary>
    public class TranslationEventOptions
    {
        /// <summary>
        /// イベントを有効にするかどうか
        /// </summary>
        public bool EnableEvents { get; set; } = true;
        
        /// <summary>
        /// 詳細イベントを有効にするかどうか
        /// </summary>
        public bool EnableDetailedEvents { get; set; } = false;
        
        /// <summary>
        /// 非同期イベント処理を使用するかどうか
        /// </summary>
        public bool UseAsyncEventProcessing { get; set; } = true;
        
        /// <summary>
        /// イベントハンドラーの順序を保証するかどうか
        /// </summary>
        public bool GuaranteeEventHandlerOrder { get; set; } = false;
    }
    
    /// <summary>
    /// 翻訳パイプラインオプション
    /// </summary>
    public class TranslationPipelineOptions
    {
        /// <summary>
        /// パイプラインの最大並列度
        /// </summary>
        public int MaxConcurrentPipelines { get; set; } = 5;
        
        /// <summary>
        /// バッチサイズ
        /// </summary>
        public int BatchSize { get; set; } = 10;
        
        /// <summary>
        /// フォールバックエンジン名
        /// </summary>
        public string FallbackEngineName { get; set; } = "DummyEngine";
        
        /// <summary>
        /// トランザクションを有効にするかどうか
        /// </summary>
        public bool EnableTransactions { get; set; } = false;
        
        /// <summary>
        /// タイムアウト（秒）
        /// </summary>
        public int TimeoutSeconds { get; set; } = 30;
    }
}

#pragma warning restore CA1805
