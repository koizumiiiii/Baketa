# 翻訳結果管理システムのインターフェース設計

翻訳結果管理システムのインターフェース設計例です。

```csharp
namespace Baketa.Translation.Management
{
    /// <summary>
    /// 翻訳結果管理インターフェース
    /// </summary>
    public interface ITranslationManager
    {
        /// <summary>
        /// 翻訳結果を保存します
        /// </summary>
        /// <param name="translationResponse">翻訳レスポンス</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <returns>保存された翻訳レコード</returns>
        Task<TranslationRecord> SaveTranslationAsync(
            TranslationResponse translationResponse, 
            TranslationContext? context = null);
            
        /// <summary>
        /// キャッシュから翻訳結果を取得します
        /// </summary>
        /// <param name="sourceText">元テキスト</param>
        /// <param name="sourceLang">元言語</param>
        /// <param name="targetLang">対象言語</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <returns>キャッシュにある場合は翻訳レコード、なければnull</returns>
        Task<TranslationRecord?> GetTranslationAsync(
            string sourceText, 
            Language sourceLang, 
            Language targetLang, 
            TranslationContext? context = null);
            
        /// <summary>
        /// 複数のテキストのキャッシュ状態を一括確認します
        /// </summary>
        /// <param name="sourceTexts">元テキストのコレクション</param>
        /// <param name="sourceLang">元言語</param>
        /// <param name="targetLang">対象言語</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <returns>キャッシュ状態マップ（key=sourceText, value=翻訳レコードまたはnull）</returns>
        Task<IReadOnlyDictionary<string, TranslationRecord?>> GetTranslationStatusAsync(
            IReadOnlyCollection<string> sourceTexts, 
            Language sourceLang, 
            Language targetLang, 
            TranslationContext? context = null);
            
        /// <summary>
        /// 翻訳結果を更新します
        /// </summary>
        /// <param name="recordId">レコードID</param>
        /// <param name="newTranslatedText">新しい翻訳テキスト</param>
        /// <returns>更新が成功すればtrue</returns>
        Task<bool> UpdateTranslationAsync(Guid recordId, string newTranslatedText);
        
        /// <summary>
        /// 翻訳結果を削除します
        /// </summary>
        /// <param name="recordId">レコードID</param>
        /// <returns>削除が成功すればtrue</returns>
        Task<bool> DeleteTranslationAsync(Guid recordId);
        
        /// <summary>
        /// 翻訳履歴を検索します
        /// </summary>
        /// <param name="query">検索クエリ</param>
        /// <returns>検索結果のコレクション</returns>
        Task<IReadOnlyList<TranslationRecord>> SearchTranslationsAsync(TranslationSearchQuery query);
        
        /// <summary>
        /// 翻訳統計を取得します
        /// </summary>
        /// <param name="options">統計オプション</param>
        /// <returns>翻訳統計データ</returns>
        Task<TranslationStatistics> GetStatisticsAsync(StatisticsOptions options);
        
        /// <summary>
        /// キャッシュをクリアします
        /// </summary>
        /// <param name="options">クリアオプション</param>
        /// <returns>クリアされたレコード数</returns>
        Task<int> ClearCacheAsync(CacheClearOptions options);
        
        /// <summary>
        /// データベースをエクスポートします
        /// </summary>
        /// <param name="filePath">エクスポート先ファイルパス</param>
        /// <returns>エクスポートが成功すればtrue</returns>
        Task<bool> ExportDatabaseAsync(string filePath);
        
        /// <summary>
        /// データベースをインポートします
        /// </summary>
        /// <param name="filePath">インポート元ファイルパス</param>
        /// <param name="mergeStrategy">マージ戦略</param>
        /// <returns>インポートが成功すればtrue</returns>
        Task<bool> ImportDatabaseAsync(string filePath, MergeStrategy mergeStrategy);
    }
    
    /// <summary>
    /// 翻訳結果リポジトリインターフェース
    /// </summary>
    public interface ITranslationRepository
    {
        /// <summary>
        /// 翻訳レコードを保存します
        /// </summary>
        /// <param name="record">翻訳レコード</param>
        /// <returns>保存が成功すればtrue</returns>
        Task<bool> SaveRecordAsync(TranslationRecord record);
        
        /// <summary>
        /// 翻訳レコードを取得します
        /// </summary>
        /// <param name="id">レコードID</param>
        /// <returns>レコードが存在すればそのレコード、なければnull</returns>
        Task<TranslationRecord?> GetRecordAsync(Guid id);
        
        /// <summary>
        /// 翻訳レコードを検索します
        /// </summary>
        /// <param name="sourceText">元テキスト</param>
        /// <param name="sourceLang">元言語</param>
        /// <param name="targetLang">対象言語</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <returns>検索結果のコレクション</returns>
        Task<IReadOnlyList<TranslationRecord>> FindRecordsAsync(
            string sourceText, 
            Language sourceLang, 
            Language targetLang, 
            TranslationContext? context = null);
            
        /// <summary>
        /// 翻訳レコードを更新します
        /// </summary>
        /// <param name="record">更新するレコード</param>
        /// <returns>更新が成功すればtrue</returns>
        Task<bool> UpdateRecordAsync(TranslationRecord record);
        
        /// <summary>
        /// 翻訳レコードを削除します
        /// </summary>
        /// <param name="id">レコードID</param>
        /// <returns>削除が成功すればtrue</returns>
        Task<bool> DeleteRecordAsync(Guid id);
        
        /// <summary>
        /// 条件に一致するレコードを検索します
        /// </summary>
        /// <param name="query">検索クエリ</param>
        /// <returns>検索結果のコレクション</returns>
        Task<IReadOnlyList<TranslationRecord>> SearchRecordsAsync(TranslationSearchQuery query);
        
        /// <summary>
        /// 統計情報を取得します
        /// </summary>
        /// <param name="options">統計オプション</param>
        /// <returns>統計データ</returns>
        Task<TranslationStatistics> GetStatisticsAsync(StatisticsOptions options);
        
        /// <summary>
        /// 条件に一致するレコードを削除します
        /// </summary>
        /// <param name="options">削除オプション</param>
        /// <returns>削除されたレコード数</returns>
        Task<int> DeleteRecordsAsync(CacheClearOptions options);
    }
    
    /// <summary>
    /// 翻訳レコードを表すクラス
    /// </summary>
    public class TranslationRecord
    {
        /// <summary>
        /// レコードID
        /// </summary>
        public required Guid Id { get; set; }
        
        /// <summary>
        /// 元テキスト
        /// </summary>
        public required string SourceText { get; set; }
        
        /// <summary>
        /// 翻訳結果テキスト
        /// </summary>
        public required string TranslatedText { get; set; }
        
        /// <summary>
        /// 元言語
        /// </summary>
        public required Language SourceLanguage { get; set; }
        
        /// <summary>
        /// 対象言語
        /// </summary>
        public required Language TargetLanguage { get; set; }
        
        /// <summary>
        /// 使用された翻訳エンジン
        /// </summary>
        public required string TranslationEngine { get; set; }
        
        /// <summary>
        /// 翻訳コンテキスト
        /// </summary>
        public TranslationContext? Context { get; set; }
        
        /// <summary>
        /// 作成日時
        /// </summary>
        public required DateTime CreatedAt { get; set; }
        
        /// <summary>
        /// 最終更新日時
        /// </summary>
        public DateTime? UpdatedAt { get; set; }
        
        /// <summary>
        /// 使用回数
        /// </summary>
        public int UsageCount { get; set; }
        
        /// <summary>
        /// 最終使用日時
        /// </summary>
        public DateTime? LastUsedAt { get; set; }
        
        /// <summary>
        /// ユーザー編集済みフラグ
        /// </summary>
        public bool IsUserEdited { get; set; }
        
        /// <summary>
        /// 追加メタデータ
        /// </summary>
        public Dictionary<string, object?> Metadata { get; } = new();
    }
    
    /// <summary>
    /// 翻訳コンテキストを表すクラス
    /// </summary>
    public class TranslationContext
    {
        /// <summary>
        /// ゲームプロファイルID
        /// </summary>
        public string? GameProfileId { get; set; }
        
        /// <summary>
        /// シーン識別子
        /// </summary>
        public string? SceneId { get; set; }
        
        /// <summary>
        /// 会話ID
        /// </summary>
        public string? DialogueId { get; set; }
        
        /// <summary>
        /// 画面領域
        /// </summary>
        public Rectangle? ScreenRegion { get; set; }
        
        /// <summary>
        /// コンテキストタグ
        /// </summary>
        public List<string> Tags { get; } = new();
        
        /// <summary>
        /// コンテキスト優先度（0～100）
        /// </summary>
        public int Priority { get; set; } = 50;
        
        /// <summary>
        /// 追加コンテキスト情報
        /// </summary>
        public Dictionary<string, object?> AdditionalContext { get; } = new();
        
        /// <summary>
        /// コンテキストをキーに変換します
        /// </summary>
        /// <returns>コンテキストキー</returns>
        public string ToContextKey()
        {
            // コンテキストをユニークなキーに変換する実装
            throw new NotImplementedException();
        }
    }
    
    /// <summary>
    /// 翻訳検索クエリを表すクラス
    /// </summary>
    public class TranslationSearchQuery
    {
        /// <summary>
        /// テキスト検索パターン
        /// </summary>
        public string? TextPattern { get; set; }
        
        /// <summary>
        /// 元言語フィルター
        /// </summary>
        public Language? SourceLanguage { get; set; }
        
        /// <summary>
        /// 対象言語フィルター
        /// </summary>
        public Language? TargetLanguage { get; set; }
        
        /// <summary>
        /// エンジン名フィルター
        /// </summary>
        public string? EngineName { get; set; }
        
        /// <summary>
        /// ゲームプロファイルIDフィルター
        /// </summary>
        public string? GameProfileId { get; set; }
        
        /// <summary>
        /// タグフィルター
        /// </summary>
        public List<string> Tags { get; } = new();
        
        /// <summary>
        /// 作成日時範囲の開始
        /// </summary>
        public DateTime? CreatedAfter { get; set; }
        
        /// <summary>
        /// 作成日時範囲の終了
        /// </summary>
        public DateTime? CreatedBefore { get; set; }
        
        /// <summary>
        /// ユーザー編集済みフィルター
        /// </summary>
        public bool? IsUserEdited { get; set; }
        
        /// <summary>
        /// 最大結果数
        /// </summary>
        public int Limit { get; set; } = 100;
        
        /// <summary>
        /// 結果オフセット
        /// </summary>
        public int Offset { get; set; } = 0;
        
        /// <summary>
        /// 並べ替えフィールド
        /// </summary>
        public string SortField { get; set; } = "CreatedAt";
        
        /// <summary>
        /// 昇順か降順か
        /// </summary>
        public bool SortAscending { get; set; } = false;
    }
    
    /// <summary>
    /// 翻訳統計を表すクラス
    /// </summary>
    public class TranslationStatistics
    {
        /// <summary>
        /// 総翻訳レコード数
        /// </summary>
        public int TotalRecords { get; set; }
        
        /// <summary>
        /// ユーザー編集済みレコード数
        /// </summary>
        public int UserEditedRecords { get; set; }
        
        /// <summary>
        /// 言語ペア別統計
        /// </summary>
        public Dictionary<string, int> RecordsByLanguagePair { get; } = new();
        
        /// <summary>
        /// エンジン別統計
        /// </summary>
        public Dictionary<string, int> RecordsByEngine { get; } = new();
        
        /// <summary>
        /// ゲームプロファイル別統計
        /// </summary>
        public Dictionary<string, int> RecordsByGameProfile { get; } = new();
        
        /// <summary>
        /// タグ別統計
        /// </summary>
        public Dictionary<string, int> RecordsByTag { get; } = new();
        
        /// <summary>
        /// 時間帯別統計
        /// </summary>
        public Dictionary<string, int> RecordsByTimeFrame { get; } = new();
        
        /// <summary>
        /// キャッシュヒット率
        /// </summary>
        public float CacheHitRate { get; set; }
        
        /// <summary>
        /// 平均翻訳時間（ミリ秒）
        /// </summary>
        public float AverageTranslationTimeMs { get; set; }
        
        /// <summary>
        /// 統計の生成日時
        /// </summary>
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
    }
    
    /// <summary>
    /// 統計オプションを表すクラス
    /// </summary>
    public class StatisticsOptions
    {
        /// <summary>
        /// 指定期間の開始日時
        /// </summary>
        public DateTime? StartDate { get; set; }
        
        /// <summary>
        /// 指定期間の終了日時
        /// </summary>
        public DateTime? EndDate { get; set; }
        
        /// <summary>
        /// エンジン別統計を含めるか
        /// </summary>
        public bool IncludeEngineStats { get; set; } = true;
        
        /// <summary>
        /// 言語ペア別統計を含めるか
        /// </summary>
        public bool IncludeLanguagePairStats { get; set; } = true;
        
        /// <summary>
        /// ゲームプロファイル別統計を含めるか
        /// </summary>
        public bool IncludeGameProfileStats { get; set; } = true;
        
        /// <summary>
        /// タグ別統計を含めるか
        /// </summary>
        public bool IncludeTagStats { get; set; } = true;
        
        /// <summary>
        /// 時間帯別統計を含めるか
        /// </summary>
        public bool IncludeTimeFrameStats { get; set; } = false;
        
        /// <summary>
        /// パフォーマンス統計を含めるか
        /// </summary>
        public bool IncludePerformanceStats { get; set; } = true;
    }
    
    /// <summary>
    /// キャッシュクリアオプションを表すクラス
    /// </summary>
    public class CacheClearOptions
    {
        /// <summary>
        /// ゲームプロファイルID
        /// </summary>
        public string? GameProfileId { get; set; }
        
        /// <summary>
        /// 元言語
        /// </summary>
        public Language? SourceLanguage { get; set; }
        
        /// <summary>
        /// 対象言語
        /// </summary>
        public Language? TargetLanguage { get; set; }
        
        /// <summary>
        /// エンジン名
        /// </summary>
        public string? EngineName { get; set; }
        
        /// <summary>
        /// 指定日時より古いレコードを削除
        /// </summary>
        public DateTime? OlderThan { get; set; }
        
        /// <summary>
        /// ユーザー編集済みレコードを保持するか
        /// </summary>
        public bool PreserveUserEdited { get; set; } = true;
        
        /// <summary>
        /// 全てのキャッシュをクリアするか
        /// </summary>
        public bool ClearAll { get; set; } = false;
    }
    
    /// <summary>
    /// データベースマージ戦略
    /// </summary>
    public enum MergeStrategy
    {
        /// <summary>
        /// 既存レコードを上書き
        /// </summary>
        Overwrite,
        
        /// <summary>
        /// 既存レコードを保持
        /// </summary>
        KeepExisting,
        
        /// <summary>
        /// 新しい方を保持
        /// </summary>
        KeepNewer,
        
        /// <summary>
        /// ユーザー編集済みを優先
        /// </summary>
        PreferUserEdited
    }
}
```