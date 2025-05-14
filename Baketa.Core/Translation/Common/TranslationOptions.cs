using System;

namespace Baketa.Core.Translation.Common
{
    /// <summary>
    /// 翻訳サブシステム全体の設定クラス
    /// </summary>
    public class TranslationOptions
    {
        /// <summary>
        /// WebAPI翻訳エンジンの設定
        /// </summary>
        public WebApiTranslationOptions WebApiOptions { get; set; } = new();
        
        /// <summary>
        /// 翻訳結果管理の設定
        /// </summary>
        public TranslationManagementOptions ManagementOptions { get; set; } = new();
        
        /// <summary>
        /// 翻訳キャッシュの設定
        /// </summary>
        public TranslationCacheOptions CacheOptions { get; set; } = new();
        
        /// <summary>
        /// 翻訳イベントの設定
        /// </summary>
        public TranslationEventOptions EventOptions { get; set; } = new();
    }

    /// <summary>
    /// WebAPI翻訳エンジンの設定クラス
    /// </summary>
    public class WebApiTranslationOptions
    {
        // デフォルトコンストラクタは自動的に生成されます。プロパティのデフォルト値は宣言時に設定済み。
        
        /// <summary>
        /// Google翻訳APIを有効にするかどうか
        /// </summary>
        public bool EnableGoogleTranslate { get; set; } = true;
        
        /// <summary>
        /// Google翻訳APIキー
        /// </summary>
        public string GoogleTranslateApiKey { get; set; } = string.Empty;
        
        /// <summary>
        /// DeepL APIを有効にするかどうか
        /// </summary>
        public bool EnableDeepL { get; set; }
        
        /// <summary>
        /// DeepL APIキー
        /// </summary>
        public string DeepLApiKey { get; set; } = string.Empty;
        
        /// <summary>
        /// リクエストタイムアウト（秒）
        /// </summary>
        public int RequestTimeoutSeconds { get; set; } = 10;
        
        /// <summary>
        /// 最大リトライ回数
        /// </summary>
        public int MaxRetryCount { get; set; } = 3;
        
        /// <summary>
        /// リトライ間隔（ミリ秒）
        /// </summary>
        public int RetryIntervalMs { get; set; } = 1000;
        
        /// <summary>
        /// ユーザーエージェント
        /// </summary>
        public string UserAgent { get; set; } = "Baketa Translation Client/1.0";
        
        /// <summary>
        /// APIリクエストの同時実行数上限
        /// </summary>
        public int MaxConcurrentRequests { get; set; } = 5;
    }

    /// <summary>
    /// 翻訳結果管理の設定クラス
    /// </summary>
    public class TranslationManagementOptions
    {
        /// <summary>
        /// データベースの接続文字列
        /// </summary>
        public string ConnectionString { get; set; } = "Data Source=translation_results.db";
        
        /// <summary>
        /// 最大記録件数
        /// </summary>
        public int MaxRecords { get; set; } = 100000;
        
        /// <summary>
        /// 最大保持期間
        /// </summary>
        public TimeSpan MaxRetentionPeriod { get; set; } = TimeSpan.FromDays(90);
        
        /// <summary>
        /// 自動クリーンアップを有効にするかどうか
        /// </summary>
        public bool EnableAutoCleanup { get; set; } = true;
        
        /// <summary>
        /// クリーンアップの間隔
        /// </summary>
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromDays(7);
        
        /// <summary>
        /// 統計収集を有効にするかどうか
        /// </summary>
        public bool EnableStatistics { get; set; } = true;
    }

    /// <summary>
    /// 翻訳キャッシュの設定クラス
    /// </summary>
    public class TranslationCacheOptions
    {
        /// <summary>
        /// メモリキャッシュを有効にするかどうか
        /// </summary>
        public bool EnableMemoryCache { get; set; } = true;
        
        /// <summary>
        /// メモリキャッシュの最大アイテム数
        /// </summary>
        public int MemoryCacheMaxItems { get; set; } = 10000;
        
        /// <summary>
        /// 永続化キャッシュを有効にするかどうか
        /// </summary>
        public bool EnablePersistentCache { get; set; } = true;
        
        /// <summary>
        /// 永続化キャッシュのデータベースパス
        /// </summary>
        public string PersistentCachePath { get; set; } = "translation_cache.db";
        
        /// <summary>
        /// 永続化キャッシュの最大サイズ（MB）
        /// </summary>
        public int PersistentCacheMaxSizeMB { get; set; } = 500;
        
        /// <summary>
        /// キャッシュの有効期限（null=無期限）
        /// </summary>
        public TimeSpan? CacheExpiration { get; set; } = TimeSpan.FromDays(30);
        
        /// <summary>
        /// キャッシュ統計収集を有効にするかどうか
        /// </summary>
        public bool EnableCacheStatistics { get; set; } = true;
    }

    /// <summary>
    /// 翻訳イベントの設定クラス
    /// </summary>
    public class TranslationEventOptions
    {
        /// <summary>
        /// イベント処理を有効にするかどうか
        /// </summary>
        public bool EnableEvents { get; set; } = true;
        
        /// <summary>
        /// イベント処理の同時実行数上限
        /// </summary>
        public int MaxConcurrentEvents { get; set; } = 10;
        
        /// <summary>
        /// イベント処理タイムアウト（秒）
        /// </summary>
        public int EventProcessingTimeoutSeconds { get; set; } = 30;
        
        /// <summary>
        /// イベントキューの最大サイズ
        /// </summary>
        public int MaxEventQueueSize { get; set; } = 1000;
        
        /// <summary>
        /// すべてのイベントをログに記録するかどうか
        /// </summary>
        public bool LogAllEvents { get; set; }
    }
}
