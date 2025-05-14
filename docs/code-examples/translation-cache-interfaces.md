# 翻訳キャッシュインターフェース設計例

翻訳キャッシュシステムのインターフェース設計です。これを基に実装を進めてください。

```csharp
namespace Baketa.Translation.Cache
{
    /// <summary>
    /// 翻訳キャッシュインターフェース
    /// </summary>
    public interface ITranslationCache
    {
        /// <summary>
        /// キャッシュからテキストを取得します
        /// </summary>
        /// <param name="key">キャッシュキー</param>
        /// <returns>キャッシュされた翻訳結果、存在しない場合はnull</returns>
        Task<TranslationCacheEntry?> GetAsync(string key);
        
        /// <summary>
        /// 複数のキーに対応するキャッシュを一括取得します
        /// </summary>
        /// <param name="keys">キャッシュキーのリスト</param>
        /// <returns>キーと翻訳結果のディクショナリ</returns>
        Task<IDictionary<string, TranslationCacheEntry>> GetManyAsync(IEnumerable<string> keys);
        
        /// <summary>
        /// テキストをキャッシュに保存します
        /// </summary>
        /// <param name="key">キャッシュキー</param>
        /// <param name="entry">キャッシュエントリ</param>
        /// <param name="expiration">有効期限（null=無期限）</param>
        /// <returns>保存が成功したかどうか</returns>
        Task<bool> SetAsync(string key, TranslationCacheEntry entry, TimeSpan? expiration = null);
        
        /// <summary>
        /// 複数のエントリを一括保存します
        /// </summary>
        /// <param name="entries">キーとエントリのディクショナリ</param>
        /// <param name="expiration">有効期限（null=無期限）</param>
        /// <returns>保存が成功したかどうか</returns>
        Task<bool> SetManyAsync(IDictionary<string, TranslationCacheEntry> entries, TimeSpan? expiration = null);
        
        /// <summary>
        /// キャッシュからアイテムを削除します
        /// </summary>
        /// <param name="key">キャッシュキー</param>
        /// <returns>削除が成功したかどうか</returns>
        Task<bool> RemoveAsync(string key);
        
        /// <summary>
        /// 複数のキーに対応するキャッシュを一括削除します
        /// </summary>
        /// <param name="keys">キャッシュキーのリスト</param>
        /// <returns>削除が成功したかどうか</returns>
        Task<bool> RemoveManyAsync(IEnumerable<string> keys);
        
        /// <summary>
        /// キャッシュをクリアします
        /// </summary>
        /// <returns>クリアが成功したかどうか</returns>
        Task<bool> ClearAsync();
        
        /// <summary>
        /// キャッシュのキー一覧を取得します
        /// </summary>
        /// <param name="pattern">検索パターン</param>
        /// <param name="maxCount">最大取得数</param>
        /// <returns>キャッシュキーのリスト</returns>
        Task<IEnumerable<string>> GetKeysAsync(string? pattern = null, int maxCount = 1000);
        
        /// <summary>
        /// キャッシュの統計情報を取得します
        /// </summary>
        /// <returns>キャッシュ統計情報</returns>
        Task<CacheStatistics> GetStatisticsAsync();
        
        /// <summary>
        /// キャッシュを最適化します
        /// </summary>
        /// <returns>最適化が成功したかどうか</returns>
        Task<bool> OptimizeAsync();
        
        /// <summary>
        /// キャッシュをエクスポートします
        /// </summary>
        /// <param name="filePath">エクスポート先ファイルパス</param>
        /// <returns>エクスポートが成功したかどうか</returns>
        Task<bool> ExportAsync(string filePath);
        
        /// <summary>
        /// キャッシュをインポートします
        /// </summary>
        /// <param name="filePath">インポート元ファイルパス</param>
        /// <param name="mergeStrategy">マージ戦略</param>
        /// <returns>インポートが成功したかどうか</returns>
        Task<bool> ImportAsync(string filePath, CacheMergeStrategy mergeStrategy = CacheMergeStrategy.ReplaceExisting);
    }
    
    /// <summary>
    /// 翻訳キャッシュエントリクラス
    /// </summary>
    public class TranslationCacheEntry
    {
        /// <summary>
        /// 原文テキスト
        /// </summary>
        public string SourceText { get; set; } = string.Empty;
        
        /// <summary>
        /// 翻訳されたテキスト
        /// </summary>
        public string TranslatedText { get; set; } = string.Empty;
        
        /// <summary>
        /// 元言語
        /// </summary>
        public string SourceLanguage { get; set; } = string.Empty;
        
        /// <summary>
        /// 対象言語
        /// </summary>
        public string TargetLanguage { get; set; } = string.Empty;
        
        /// <summary>
        /// 翻訳エンジン
        /// </summary>
        public string Engine { get; set; } = string.Empty;
        
        /// <summary>
        /// 作成日時
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// 最終アクセス日時
        /// </summary>
        public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// アクセス回数
        /// </summary>
        public int AccessCount { get; set; } = 1;
        
        /// <summary>
        /// メタデータ
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }
    
    /// <summary>
    /// キャッシュ統計情報クラス
    /// </summary>
    public class CacheStatistics
    {
        /// <summary>
        /// キャッシュアイテム数
        /// </summary>
        public int ItemCount { get; set; }
        
        /// <summary>
        /// キャッシュサイズ（バイト）
        /// </summary>
        public long SizeInBytes { get; set; }
        
        /// <summary>
        /// キャッシュヒット数
        /// </summary>
        public long Hits { get; set; }
        
        /// <summary>
        /// キャッシュミス数
        /// </summary>
        public long Misses { get; set; }
        
        /// <summary>
        /// ヒット率
        /// </summary>
        public double HitRate => Hits + Misses == 0 ? 0 : (double)Hits / (Hits + Misses);
        
        /// <summary>
        /// 最も古いエントリの作成日時
        /// </summary>
        public DateTime? OldestEntryCreatedAt { get; set; }
        
        /// <summary>
        /// 最も新しいエントリの作成日時
        /// </summary>
        public DateTime? NewestEntryCreatedAt { get; set; }
        
        /// <summary>
        /// 最も古いアクセス日時
        /// </summary>
        public DateTime? OldestAccessedAt { get; set; }
        
        /// <summary>
        /// 最も新しいアクセス日時
        /// </summary>
        public DateTime? NewestAccessedAt { get; set; }
        
        /// <summary>
        /// 言語ペア統計
        /// </summary>
        public Dictionary<string, int> LanguagePairStats { get; set; } = new Dictionary<string, int>();
        
        /// <summary>
        /// エンジン統計
        /// </summary>
        public Dictionary<string, int> EngineStats { get; set; } = new Dictionary<string, int>();
    }
    
    /// <summary>
    /// キャッシュマージ戦略列挙型
    /// </summary>
    public enum CacheMergeStrategy
    {
        /// <summary>
        /// 既存のエントリを置き換える
        /// </summary>
        ReplaceExisting,
        
        /// <summary>
        /// 既存のエントリを保持する
        /// </summary>
        KeepExisting,
        
        /// <summary>
        /// より新しいエントリを使用
        /// </summary>
        UseNewer,
        
        /// <summary>
        /// アクセス回数が多いエントリを使用
        /// </summary>
        UseMoreAccessed
    }
    
    /// <summary>
    /// キャッシュキーの生成に使用するユーティリティクラス
    /// </summary>
    public static class CacheKeyGenerator
    {
        /// <summary>
        /// 翻訳キャッシュキーを生成します
        /// </summary>
        /// <param name="sourceText">原文テキスト</param>
        /// <param name="sourceLanguage">元言語</param>
        /// <param name="targetLanguage">対象言語</param>
        /// <param name="engine">翻訳エンジン</param>
        /// <returns>キャッシュキー</returns>
        public static string GenerateKey(string sourceText, string sourceLanguage, string targetLanguage, string engine)
        {
            // クリーンなテキストを使用してキーを生成
            string cleanText = CleanText(sourceText);
            
            // キーを構築
            return $"{sourceLanguage}|{targetLanguage}|{engine}|{cleanText}";
        }
        
        /// <summary>
        /// キャッシュキー用にテキストをクリーンアップします
        /// </summary>
        /// <param name="text">クリーンアップするテキスト</param>
        /// <returns>クリーンアップされたテキスト</returns>
        private static string CleanText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
                
            // 余分な空白を削除して正規化
            text = text.Trim();
            text = Regex.Replace(text, @"\s+", " ");
            
            // 翻訳に影響しない特殊文字を処理
            // （実装詳細は省略）
            
            return text;
        }
    }
}
```